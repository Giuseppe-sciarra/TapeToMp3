namespace Tape2MP3.Audio;

public enum GapKind { Inizio, Pausa, Buco, Calo, Fine }

/// <summary>Zona sospetta trovata dall'analisi: non viene toccata finché non decidi tu.</summary>
public sealed class Gap
{
    public long Start;
    public long End;
    public GapKind Kind;
    public double LevelDb;   // livello medio nella zona
    public long Length => End - Start;

    public string KindLabel => Kind switch
    {
        GapKind.Inizio => "Silenzio iniziale",
        GapKind.Fine => "Silenzio finale",
        GapKind.Pausa => "Pausa (fra brani?)",
        GapKind.Buco => "Buco breve",
        GapKind.Calo => "Calo improvviso",
        _ => "?"
    };
}

public sealed class GapAnalysis
{
    public double NoiseFloorDb;  // fruscio della cassetta
    public double MusicDb;       // livello tipico della musica
    public double ThresholdDb;   // soglia usata
    public List<Gap> Gaps = new();
}

/// <summary>
/// Analisi "adattiva": invece di una soglia fissa misura il fruscio di QUESTA cassetta
/// (i passaggi più bassi della registrazione) e considera silenzio tutto ciò che sta
/// entro pochi dB dal fruscio. Segnala anche i cali improvvisi dentro la musica (dropout).
/// </summary>
public static class GapAnalyzer
{
    private const int WinPerSec = 20; // finestre da 50 ms

    public static GapAnalysis Analyze(string path, double marginDb, double minGapSec, double pauseSec,
        bool detectDrops, IProgress<double> progress = null, CancellationToken ct = default)
    {
        var info = WavFile.ReadInfo(path);
        int sr = info.SampleRate;
        int win = Math.Max(1, sr / WinPerSec);
        long totalWins = (info.Frames + win - 1) / win;
        var db = new float[totalWins];

        // --- passata 1: livello RMS (max fra L e R) per finestra
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20))
        {
            fs.Position = info.DataOffset;
            var buf = new byte[win * 4];
            for (long w = 0; w < totalWins; w++)
            {
                if ((w & 1023) == 0) { ct.ThrowIfCancellationRequested(); progress?.Report((double)w / totalWins * 0.8); }
                int want = (int)Math.Min(win, info.Frames - w * win) * 4;
                int got = 0;
                while (got < want) { int n = fs.Read(buf, got, want - got); if (n <= 0) break; got += n; }
                int frames = got / 4;
                if (frames == 0) { db[w] = -100; continue; }
                double sl = 0, sr2 = 0;
                for (int i = 0; i < frames * 4; i += 4)
                {
                    double l = (short)(buf[i] | (buf[i + 1] << 8));
                    double r = (short)(buf[i + 2] | (buf[i + 3] << 8));
                    sl += l * l; sr2 += r * r;
                }
                double rms = Math.Sqrt(Math.Max(sl, sr2) / frames) / 32768.0;
                db[w] = rms <= 1e-6 ? -100f : (float)(20 * Math.Log10(rms));
            }
        }

        var res = new GapAnalysis();
        if (totalWins < WinPerSec) { res.NoiseFloorDb = -100; res.MusicDb = -100; res.ThresholdDb = -100; return res; }

        // --- stima fruscio e livello musica
        // fruscio = 2° percentile delle finestre non digitalmente mute; musica = mediana
        var valid = db.Where(v => v > -90).OrderBy(v => v).ToArray();
        double floor = valid.Length > 0 ? valid[(int)(valid.Length * 0.02)] : -100;
        double music = valid.Length > 0 ? valid[valid.Length / 2] : -100;
        // la soglia sta poco sopra il fruscio, ma sempre ben sotto la musica
        double thr = Math.Min(floor + marginDb, music - 15);
        thr = Math.Max(thr, floor + 1.5);
        res.NoiseFloorDb = floor; res.MusicDb = music; res.ThresholdDb = thr;

        // --- passata 2: zone sotto soglia
        int minGapW = Math.Max(1, (int)Math.Round(minGapSec * WinPerSec));
        int pauseW = Math.Max(minGapW, (int)Math.Round(pauseSec * WinPerSec));
        var silent = new bool[totalWins];
        long run = -1;
        for (long w = 0; w <= totalWins; w++)
        {
            bool s = w < totalWins && db[w] < thr;
            if (w < totalWins) silent[w] = s;
            if (s) { if (run < 0) run = w; continue; }
            if (run >= 0)
            {
                long len = w - run;
                bool atStart = run == 0, atEnd = w == totalWins;
                if (len >= minGapW || (atStart && len >= 2) || (atEnd && len >= 2))
                {
                    var g = new Gap
                    {
                        Start = run * win,
                        End = Math.Min(info.Frames, w * win),
                        LevelDb = Avg(db, run, w),
                        Kind = atStart ? GapKind.Inizio : atEnd ? GapKind.Fine : len >= pauseW ? GapKind.Pausa : GapKind.Buco
                    };
                    res.Gaps.Add(g);
                }
                run = -1;
            }
        }
        progress?.Report(0.9);

        // --- passata 3: cali improvvisi (dropout) dentro la musica
        if (detectDrops)
        {
            int ctx = 3 * WinPerSec;      // contesto ±3 s
            int side = WinPerSec / 2;     // 0.5 s prima e dopo devono essere "normali"
            int maxDrop = 2 * WinPerSec;  // più lungo di 2 s non è un dropout
            // media mobile del livello
            var pref = new double[totalWins + 1];
            for (long i = 0; i < totalWins; i++) pref[i + 1] = pref[i] + db[i];
            double Mean(long a, long b) { a = Math.Max(0, a); b = Math.Min(totalWins, b); return b > a ? (pref[b] - pref[a]) / (b - a) : -100; }

            long dStart = -1;
            for (long w = 0; w <= totalWins; w++)
            {
                bool drop = false;
                if (w < totalWins)
                {
                    double local = Mean(w - ctx, w + ctx);
                    drop = local > thr + 12 && db[w] < local - 18;
                }
                if (drop) { if (dStart < 0) dStart = w; continue; }
                if (dStart >= 0)
                {
                    long len = w - dStart;
                    if (len >= 2 && len <= maxDrop)
                    {
                        double before = Mean(dStart - side, dStart);
                        double after = Mean(w, w + side);
                        double local = Mean(dStart - ctx, w + ctx);
                        long gs = dStart * win, ge = Math.Min(info.Frames, w * win);
                        bool overlapsGap = res.Gaps.Any(g => g.Start < ge && g.End > gs);
                        if (!overlapsGap && before > local - 8 && after > local - 8)
                        {
                            res.Gaps.Add(new Gap
                            {
                                Start = gs,
                                End = ge,
                                LevelDb = Avg(db, dStart, w),
                                Kind = GapKind.Calo
                            });
                        }
                    }
                    dStart = -1;
                }
            }
        }

        res.Gaps.Sort((a, b) => a.Start.CompareTo(b.Start));
        progress?.Report(1);
        return res;
    }

    private static double Avg(float[] db, long a, long b)
    {
        double s = 0; long n = 0;
        for (long i = a; i < b && i < db.Length; i++) { s += db[i]; n++; }
        return n > 0 ? s / n : -100;
    }
}
