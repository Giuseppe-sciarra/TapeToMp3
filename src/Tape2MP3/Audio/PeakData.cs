namespace Tape2MP3.Audio;

/// <summary>
/// Cache dei picchi (min/max per canale) per disegnare la forma d'onda velocemente
/// anche su registrazioni di 90+ minuti. Due livelli: blocchi da 256 frame e da 16384.
/// Riempita in tempo reale durante la registrazione.
/// </summary>
public sealed class PeakData
{
    public const int Block = 256;
    public const int Group = 64;              // blocchi per gruppo
    public const int BigBlock = Block * Group; // 16384 frame

    private readonly object _lock = new();
    private short[] _l1 = new short[4 * 4096];
    private int _l1Count;
    private short[] _l2 = new short[4 * 256];
    private int _l2Count;

    // accumulatore del blocco in corso
    private int _accFrames;
    private short _aMinL = short.MaxValue, _aMaxL = short.MinValue, _aMinR = short.MaxValue, _aMaxR = short.MinValue;
    private long _frames;

    public int SampleRate { get; private set; }
    public long Frames { get { lock (_lock) return _frames; } }

    public PeakData(int sampleRate) { SampleRate = sampleRate; }

    public void Clear()
    {
        lock (_lock)
        {
            _l1Count = 0; _l2Count = 0; _frames = 0; ResetAcc();
        }
    }

    private void ResetAcc()
    {
        _accFrames = 0;
        _aMinL = short.MaxValue; _aMaxL = short.MinValue; _aMinR = short.MaxValue; _aMaxR = short.MinValue;
    }

    /// <summary>Aggiunge PCM16 stereo interleaved.</summary>
    public void Append(byte[] pcm, int offset, int count)
    {
        lock (_lock)
        {
            int end = offset + count;
            for (int i = offset; i + 3 < end; i += 4)
            {
                short l = (short)(pcm[i] | (pcm[i + 1] << 8));
                short r = (short)(pcm[i + 2] | (pcm[i + 3] << 8));
                if (l < _aMinL) _aMinL = l; if (l > _aMaxL) _aMaxL = l;
                if (r < _aMinR) _aMinR = r; if (r > _aMaxR) _aMaxR = r;
                _frames++;
                if (++_accFrames == Block) CommitBlock();
            }
        }
    }

    private void CommitBlock()
    {
        if (_l1Count * 4 + 4 > _l1.Length) Array.Resize(ref _l1, _l1.Length * 2);
        int p = _l1Count * 4;
        _l1[p] = _aMinL; _l1[p + 1] = _aMaxL; _l1[p + 2] = _aMinR; _l1[p + 3] = _aMaxR;
        _l1Count++;
        ResetAcc();
        if (_l1Count % Group == 0)
        {
            short mnL = short.MaxValue, mxL = short.MinValue, mnR = short.MaxValue, mxR = short.MinValue;
            for (int b = _l1Count - Group; b < _l1Count; b++)
            {
                int q = b * 4;
                if (_l1[q] < mnL) mnL = _l1[q];
                if (_l1[q + 1] > mxL) mxL = _l1[q + 1];
                if (_l1[q + 2] < mnR) mnR = _l1[q + 2];
                if (_l1[q + 3] > mxR) mxR = _l1[q + 3];
            }
            if (_l2Count * 4 + 4 > _l2.Length) Array.Resize(ref _l2, _l2.Length * 2);
            int o = _l2Count * 4;
            _l2[o] = mnL; _l2[o + 1] = mxL; _l2[o + 2] = mnR; _l2[o + 3] = mxR;
            _l2Count++;
        }
    }

    /// <summary>
    /// Restituisce min/max (L e R) nell'intervallo di frame [from, to).
    /// Usa il livello più grosso possibile per restare veloce.
    /// </summary>
    public bool GetRange(long from, long to, out short minL, out short maxL, out short minR, out short maxR)
    {
        minL = short.MaxValue; maxL = short.MinValue; minR = short.MaxValue; maxR = short.MinValue;
        lock (_lock)
        {
            if (from < 0) from = 0;
            long fullFrames = (long)_l1Count * Block;
            if (to > fullFrames) to = fullFrames;
            if (to <= from) return false;

            long b = from / Block;
            long bEnd = (to + Block - 1) / Block;
            while (b < bEnd)
            {
                if (b % Group == 0 && b + Group <= bEnd && b / Group < _l2Count)
                {
                    int o = (int)(b / Group) * 4;
                    if (_l2[o] < minL) minL = _l2[o];
                    if (_l2[o + 1] > maxL) maxL = _l2[o + 1];
                    if (_l2[o + 2] < minR) minR = _l2[o + 2];
                    if (_l2[o + 3] > maxR) maxR = _l2[o + 3];
                    b += Group;
                }
                else
                {
                    int q = (int)b * 4;
                    if (_l1[q] < minL) minL = _l1[q];
                    if (_l1[q + 1] > maxL) maxL = _l1[q + 1];
                    if (_l1[q + 2] < minR) minR = _l1[q + 2];
                    if (_l1[q + 3] > maxR) maxR = _l1[q + 3];
                    b++;
                }
            }
            return true;
        }
    }

    /// <summary>Costruisce la cache leggendo un WAV interno (PCM16 stereo).</summary>
    public static PeakData FromFile(string path, IProgress<double> progress = null, CancellationToken ct = default)
    {
        var info = WavFile.ReadInfo(path);
        var pd = new PeakData(info.SampleRate);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20);
        fs.Position = info.DataOffset;
        var buf = new byte[1 << 20];
        long remaining = info.DataLength;
        long done = 0;
        while (remaining > 0)
        {
            ct.ThrowIfCancellationRequested();
            int n = fs.Read(buf, 0, (int)Math.Min(buf.Length, remaining));
            if (n <= 0) break;
            n -= n % 4;
            pd.Append(buf, 0, n);
            remaining -= n; done += n;
            progress?.Report((double)done / info.DataLength);
        }
        return pd;
    }
}
