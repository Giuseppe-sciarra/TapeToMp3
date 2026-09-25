namespace Tape2MP3.Audio;

/// <summary>
/// Trova le pause fra un brano e l'altro (silenzio/fruscio sotto soglia per almeno N secondi)
/// e restituisce i punti di divisione (a metà della pausa).
/// </summary>
public static class SilenceDetector
{
    public static List<long> Detect(string path, long fromFrame, long toFrame,
        double thresholdDb, double minSilenceSec, double minTrackSec,
        IProgress<double> progress = null, CancellationToken ct = default)
    {
        var info = WavFile.ReadInfo(path);
        int sr = info.SampleRate;
        if (toFrame <= 0 || toFrame > info.Frames) toFrame = info.Frames;
        if (fromFrame < 0) fromFrame = 0;

        int win = Math.Max(1, sr / 20); // finestre da 50 ms
        double thr = Math.Pow(10, thresholdDb / 20.0) * 32768.0;
        double thrSq = thr * thr;

        var silences = new List<(long start, long end)>();
        long runStart = -1;

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20);
        fs.Position = info.DataOffset + fromFrame * WavFile.BytesPerFrame;
        var buf = new byte[win * WavFile.BytesPerFrame];
        long pos = fromFrame;
        long total = Math.Max(1, toFrame - fromFrame);

        while (pos < toFrame)
        {
            ct.ThrowIfCancellationRequested();
            int frames = (int)Math.Min(win, toFrame - pos);
            int want = frames * WavFile.BytesPerFrame;
            int got = 0;
            while (got < want)
            {
                int n = fs.Read(buf, got, want - got);
                if (n <= 0) break;
                got += n;
            }
            frames = got / WavFile.BytesPerFrame;
            if (frames == 0) break;

            double sumL = 0, sumR = 0;
            for (int i = 0; i < frames * 4; i += 4)
            {
                double l = (short)(buf[i] | (buf[i + 1] << 8));
                double r = (short)(buf[i + 2] | (buf[i + 3] << 8));
                sumL += l * l; sumR += r * r;
            }
            double ms = Math.Max(sumL, sumR) / frames;
            bool silent = ms < thrSq;

            if (silent) { if (runStart < 0) runStart = pos; }
            else if (runStart >= 0)
            {
                silences.Add((runStart, pos));
                runStart = -1;
            }
            pos += frames;
            if (((pos - fromFrame) / win) % 200 == 0) progress?.Report((double)(pos - fromFrame) / total);
        }
        // una pausa che arriva fino alla fine è coda, non divisione
        progress?.Report(1);

        long minSil = (long)(minSilenceSec * sr);
        long minTrack = (long)(minTrackSec * sr);
        var cuts = new List<long>();
        long lastCut = fromFrame;
        foreach (var (s, e) in silences)
        {
            if (e - s < minSil) continue;
            if (s <= fromFrame + win) continue; // silenzio iniziale: ci pensa il taglio d'inizio
            long cut = s + (e - s) / 2;
            if (cut - lastCut < minTrack) continue;
            if (toFrame - cut < minTrack) continue;
            cuts.Add(cut);
            lastCut = cut;
        }
        return cuts;
    }

    /// <summary>
    /// Trova il primo e l'ultimo punto con segnale sopra soglia (per il taglio automatico
    /// del silenzio prima e dopo). Lascia un margine di mezzo secondo.
    /// </summary>
    public static (long start, long end) DetectContent(string path, double thresholdDb, CancellationToken ct = default)
    {
        var info = WavFile.ReadInfo(path);
        int sr = info.SampleRate;
        int win = Math.Max(1, sr / 20);
        double thr = Math.Pow(10, thresholdDb / 20.0) * 32768.0;
        double thrSq = thr * thr;
        long first = -1, last = -1;

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20);
        fs.Position = info.DataOffset;
        var buf = new byte[win * WavFile.BytesPerFrame];
        long pos = 0;
        while (pos < info.Frames)
        {
            ct.ThrowIfCancellationRequested();
            int want = (int)Math.Min(win, info.Frames - pos) * WavFile.BytesPerFrame;
            int got = 0;
            while (got < want) { int n = fs.Read(buf, got, want - got); if (n <= 0) break; got += n; }
            int frames = got / 4;
            if (frames == 0) break;
            double sumL = 0, sumR = 0;
            for (int i = 0; i < frames * 4; i += 4)
            {
                double l = (short)(buf[i] | (buf[i + 1] << 8));
                double r = (short)(buf[i + 2] | (buf[i + 3] << 8));
                sumL += l * l; sumR += r * r;
            }
            if (Math.Max(sumL, sumR) / frames >= thrSq)
            {
                if (first < 0) first = pos;
                last = pos + frames;
            }
            pos += frames;
        }
        if (first < 0) return (0, info.Frames);
        long margin = sr / 2;
        return (Math.Max(0, first - margin), Math.Min(info.Frames, last + margin));
    }
}
