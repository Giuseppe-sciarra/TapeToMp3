using System.Text;

namespace Tape2MP3.Audio;

/// <summary>
/// Formato interno fisso: PCM 16 bit, stereo, sample rate del dispositivo.
/// Semplice, robusto, appendibile e riparabile dopo un crash.
/// </summary>
public static class WavFile
{
    public const int Channels = 2;
    public const int BytesPerFrame = 4; // 2 canali * 16 bit

    public sealed class Info
    {
        public int SampleRate;
        public int Channels;
        public int BitsPerSample;
        public short FormatTag;
        public long DataOffset;
        public long DataLength;
        public long Frames => DataLength / (Channels * BitsPerSample / 8);
    }

    public static Info ReadInfo(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var br = new BinaryReader(fs);
        if (fs.Length < 44) throw new InvalidDataException("File WAV troppo corto.");
        if (Encoding.ASCII.GetString(br.ReadBytes(4)) != "RIFF") throw new InvalidDataException("Non è un file WAV (RIFF mancante).");
        br.ReadInt32();
        if (Encoding.ASCII.GetString(br.ReadBytes(4)) != "WAVE") throw new InvalidDataException("Non è un file WAV (WAVE mancante).");

        var info = new Info();
        bool fmt = false;
        while (fs.Position + 8 <= fs.Length)
        {
            string id = Encoding.ASCII.GetString(br.ReadBytes(4));
            uint size = br.ReadUInt32();
            long start = fs.Position;
            if (id == "fmt ")
            {
                info.FormatTag = br.ReadInt16();
                info.Channels = br.ReadInt16();
                info.SampleRate = br.ReadInt32();
                br.ReadInt32();
                br.ReadInt16();
                info.BitsPerSample = br.ReadInt16();
                if (info.FormatTag == unchecked((short)0xFFFE) && size >= 40)
                {
                    // WAVE_FORMAT_EXTENSIBLE: il subformat PCM ha i primi 2 byte = 1
                    fs.Position = start + 24;
                    info.FormatTag = br.ReadInt16();
                }
                fmt = true;
                fs.Position = start + size + (size & 1);
            }
            else if (id == "data")
            {
                info.DataOffset = start;
                long avail = fs.Length - start;
                // Se l'header non è stato aggiornato (crash) usiamo la lunghezza reale del file
                info.DataLength = (size == 0 || size == 0xFFFFFFFF || size > avail) ? avail : size;
                break;
            }
            else
            {
                fs.Position = start + size + (size & 1);
            }
        }
        if (!fmt || info.DataOffset == 0) throw new InvalidDataException("WAV senza chunk fmt/data.");
        int bpf = info.Channels * info.BitsPerSample / 8;
        if (bpf > 0) info.DataLength -= info.DataLength % bpf;
        return info;
    }

    public static bool IsInternalFormat(Info i) =>
        i.FormatTag == 1 && i.Channels == 2 && i.BitsPerSample == 16;

    /// <summary>Riscrive le dimensioni nell'header in base alla lunghezza reale del file.</summary>
    public static void RepairHeader(string path)
    {
        var info = ReadInfo(path);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        using var bw = new BinaryWriter(fs);
        fs.Position = 4;
        bw.Write((uint)Math.Min(uint.MaxValue, info.DataOffset + info.DataLength - 8));
        fs.Position = info.DataOffset - 4;
        bw.Write((uint)Math.Min(uint.MaxValue, info.DataLength));
        fs.SetLength(info.DataOffset + info.DataLength);
    }
}

/// <summary>Writer PCM16 stereo che può creare un file nuovo o accodare a uno esistente.</summary>
public sealed class WavWriter : IDisposable
{
    private readonly FileStream _fs;
    private readonly long _dataOffset;
    private long _dataLength;
    public int SampleRate { get; }
    public long Frames => _dataLength / WavFile.BytesPerFrame;
    public string Path { get; }

    private WavWriter(string path, FileStream fs, int sampleRate, long dataOffset, long dataLength)
    {
        Path = path; _fs = fs; SampleRate = sampleRate; _dataOffset = dataOffset; _dataLength = dataLength;
    }

    public static WavWriter Create(string path, int sampleRate)
    {
        var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 1 << 16);
        var bw = new BinaryWriter(fs, Encoding.ASCII, leaveOpen: true);
        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36u);
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16u);
        bw.Write((short)1);
        bw.Write((short)2);
        bw.Write(sampleRate);
        bw.Write(sampleRate * WavFile.BytesPerFrame);
        bw.Write((short)WavFile.BytesPerFrame);
        bw.Write((short)16);
        bw.Write(Encoding.ASCII.GetBytes("data"));
        bw.Write(0u);
        bw.Flush();
        return new WavWriter(path, fs, sampleRate, 44, 0);
    }

    public static WavWriter Append(string path)
    {
        var info = WavFile.ReadInfo(path);
        if (!WavFile.IsInternalFormat(info)) throw new InvalidDataException("Formato WAV non accodabile.");
        var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, 1 << 16);
        fs.SetLength(info.DataOffset + info.DataLength);
        fs.Position = info.DataOffset + info.DataLength;
        return new WavWriter(path, fs, info.SampleRate, info.DataOffset, info.DataLength);
    }

    public void Write(byte[] buffer, int offset, int count)
    {
        _fs.Write(buffer, offset, count);
        _dataLength += count;
    }

    /// <summary>Aggiorna l'header: chiamato periodicamente così un crash non perde nulla.</summary>
    public void Flush()
    {
        long pos = _fs.Position;
        var bw = new BinaryWriter(_fs, Encoding.ASCII, leaveOpen: true);
        _fs.Position = 4;
        bw.Write((uint)Math.Min(uint.MaxValue, _dataOffset + _dataLength - 8));
        _fs.Position = _dataOffset - 4;
        bw.Write((uint)Math.Min(uint.MaxValue, _dataLength));
        _fs.Position = pos;
        _fs.Flush(true);
    }

    public void Dispose()
    {
        try { Flush(); } catch { }
        _fs.Dispose();
    }
}
