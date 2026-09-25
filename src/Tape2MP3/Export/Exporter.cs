using System.Diagnostics;
using System.Globalization;
using System.Text;
using Tape2MP3.Audio;

namespace Tape2MP3.Export;

public sealed class ExportSegment
{
    public long StartFrame;
    public long EndFrame;
    public string Title;
    public int TrackNo;
}

public sealed class ExportFormat
{
    public string Key;
    public string Label;
    public string Ext;
    public override string ToString() => Label;

    public static readonly ExportFormat[] All =
    {
        new() { Key = "mp3",  Label = "MP3",                 Ext = ".mp3" },
        new() { Key = "flac", Label = "FLAC (senza perdita)", Ext = ".flac" },
        new() { Key = "wav",  Label = "WAV 16 bit",          Ext = ".wav" },
        new() { Key = "m4a",  Label = "M4A (AAC 256k)",      Ext = ".m4a" },
    };
}

/// <summary>
/// Esporta i segmenti del WAV di lavoro con ffmpeg (incluso accanto all'exe).
/// L'audio viene passato a ffmpeg via stdin già "montato": le zone tagliate vengono saltate.
/// </summary>
public static class Exporter
{
    public static string FindFfmpeg()
    {
        string dir = AppContext.BaseDirectory;
        foreach (var p in new[] { Path.Combine(dir, "ffmpeg.exe"), Path.Combine(dir, "tools", "ffmpeg.exe"), Path.Combine(dir, "ffmpeg", "ffmpeg.exe") })
            if (File.Exists(p)) return p;
        var env = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var d in env.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var p = Path.Combine(d.Trim(), "ffmpeg.exe");
                if (File.Exists(p)) return p;
            }
            catch { }
        }
        return null;
    }

    public static string SafeName(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "Senza titolo";
        var bad = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var c in s.Trim()) sb.Append(bad.Contains(c) ? '_' : c);
        var r = sb.ToString().Trim().TrimEnd('.');
        return r.Length == 0 ? "Senza titolo" : (r.Length > 120 ? r[..120] : r);
    }

    public static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        string dir = Path.GetDirectoryName(path), name = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            var p = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(p)) return p;
        }
    }

    /// <summary>
    /// Esporta tutti i segmenti saltando i tagli. progress riceve (indice file, totale, frazione del file corrente).
    /// Ritorna la lista dei file creati.
    /// </summary>
    public static async Task<List<string>> ExportAsync(string ffmpeg, string wavPath, int sampleRate,
        IList<ExportSegment> segments, IReadOnlyList<CutRange> cuts, string folder, string baseName, ExportFormat fmt, string mp3Quality,
        string album, IProgress<(int idx, int total, double frac)> progress, CancellationToken ct)
    {
        var created = new List<string>();
        var info = WavFile.ReadInfo(wavPath);
        cuts ??= Array.Empty<CutRange>();
        int total = segments.Count;
        for (int i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();
            var seg = segments[i];
            var parts = CutList.Keep(seg.StartFrame, seg.EndFrame, cuts);
            if (parts.Count == 0) continue;

            string fileName = total == 1
                ? SafeName(baseName)
                : $"{seg.TrackNo:00} - {SafeName(string.IsNullOrWhiteSpace(seg.Title) ? $"Traccia {seg.TrackNo:00}" : seg.Title)}";
            string outPath = UniquePath(Path.Combine(folder, fileName + fmt.Ext));
            string partPath = outPath + ".part";

            var args = new List<string>
            {
                "-hide_banner", "-nostdin", "-y", "-loglevel", "error",
                "-f", "s16le", "-ar", sampleRate.ToString(CultureInfo.InvariantCulture), "-ac", "2", "-i", "pipe:0"
            };

            switch (fmt.Key)
            {
                case "mp3":
                    args.AddRange(new[] { "-c:a", "libmp3lame", "-ar", "44100" });
                    if (mp3Quality == "V0") args.AddRange(new[] { "-q:a", "0" });
                    else args.AddRange(new[] { "-b:a", mp3Quality + "k" });
                    args.AddRange(new[] { "-id3v2_version", "3", "-write_id3v1", "1", "-f", "mp3" });
                    break;
                case "flac":
                    args.AddRange(new[] { "-c:a", "flac", "-compression_level", "8", "-f", "flac" });
                    break;
                case "wav":
                    args.AddRange(new[] { "-c:a", "pcm_s16le", "-f", "wav" });
                    break;
                case "m4a":
                    args.AddRange(new[] { "-c:a", "aac", "-b:a", "256k", "-ar", "44100", "-movflags", "+faststart", "-f", "ipod" });
                    break;
            }

            string title = total == 1 ? baseName : (string.IsNullOrWhiteSpace(seg.Title) ? $"Traccia {seg.TrackNo:00}" : seg.Title);
            if (fmt.Key != "wav")
            {
                args.AddRange(new[] { "-metadata", $"title={title}" });
                if (!string.IsNullOrWhiteSpace(album)) args.AddRange(new[] { "-metadata", $"album={album}" });
                if (total > 1) args.AddRange(new[] { "-metadata", $"track={seg.TrackNo}/{total}" });
            }
            args.Add(partPath);

            int idx = i;
            await RunFfmpegPiped(ffmpeg, args, wavPath, info, parts, f => progress?.Report((idx, total, f)), ct);

            if (File.Exists(outPath)) outPath = UniquePath(outPath);
            File.Move(partPath, outPath);
            created.Add(outPath);
            progress?.Report((i, total, 1));
        }
        return created;
    }

    private static async Task RunFfmpegPiped(string ffmpeg, List<string> args, string wavPath, WavFile.Info info,
        List<CutRange> parts, Action<double> onProgress, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = new Process { StartInfo = psi };
        var err = new StringBuilder();
        p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (err) err.AppendLine(e.Data); };
        p.OutputDataReceived += (s, e) => { };
        p.Start();
        p.BeginErrorReadLine();
        p.BeginOutputReadLine();

        long totalBytes = parts.Sum(r => r.Length) * 4;
        long sent = 0;
        bool pipeBroken = false;
        try
        {
            using var fs = new FileStream(wavPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20);
            var stdin = p.StandardInput.BaseStream;
            var buf = new byte[1 << 18];
            foreach (var part in parts)
            {
                fs.Position = info.DataOffset + part.Start * 4;
                long left = part.Length * 4;
                while (left > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    int n = await fs.ReadAsync(buf.AsMemory(0, (int)Math.Min(buf.Length, left)), ct);
                    if (n <= 0) break;
                    try { await stdin.WriteAsync(buf.AsMemory(0, n), ct); }
                    catch (IOException) { pipeBroken = true; break; }
                    left -= n; sent += n;
                    onProgress(Math.Clamp((double)sent / Math.Max(1, totalBytes), 0, 0.99));
                }
                if (pipeBroken) break;
            }
            try { stdin.Close(); } catch { }
            await p.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch { }
            TryDeletePart(args[^1]);
            throw;
        }
        if (p.ExitCode != 0 || pipeBroken)
        {
            TryDeletePart(args[^1]);
            throw new Exception("ffmpeg ha restituito un errore:\n" + err.ToString().Trim());
        }
    }

    private static async Task RunFfmpeg(string ffmpeg, List<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = new Process { StartInfo = psi };
        var err = new StringBuilder();
        p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (err) err.AppendLine(e.Data); };
        p.OutputDataReceived += (s, e) => { };
        p.Start();
        p.BeginErrorReadLine();
        p.BeginOutputReadLine();
        try { await p.WaitForExitAsync(ct); }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch { }
            TryDeletePart(args[^1]);
            throw;
        }
        if (p.ExitCode != 0)
        {
            TryDeletePart(args[^1]);
            throw new Exception("ffmpeg ha restituito un errore:\n" + err.ToString().Trim());
        }
    }

    private static void TryDeletePart(string path)
    {
        try { Thread.Sleep(200); if (File.Exists(path)) File.Delete(path); } catch { }
    }

    /// <summary>Converte un file audio qualsiasi nel formato interno (WAV PCM16 stereo).</summary>
    public static async Task ConvertToInternalAsync(string ffmpeg, string input, string outWav, CancellationToken ct)
    {
        var args = new List<string>
        {
            "-hide_banner", "-nostdin", "-y", "-loglevel", "error",
            "-i", input, "-vn", "-map_metadata", "-1", "-fflags", "+bitexact",
            "-ac", "2", "-c:a", "pcm_s16le", "-f", "wav", outWav
        };
        await RunFfmpeg(ffmpeg, args, ct);
    }
}
