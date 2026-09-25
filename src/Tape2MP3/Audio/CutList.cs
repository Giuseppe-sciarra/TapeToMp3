namespace Tape2MP3.Audio;

/// <summary>Zona tagliata: non viene cancellata dal file, viene solo saltata in ascolto ed esportazione.</summary>
public readonly record struct CutRange(long Start, long End)
{
    public long Length => End - Start;
    public bool Contains(long f) => f >= Start && f < End;
}

public static class CutList
{
    /// <summary>Unisce e ordina le zone sovrapposte.</summary>
    public static List<CutRange> Normalize(IEnumerable<CutRange> cuts)
    {
        var list = cuts.Where(c => c.End > c.Start).OrderBy(c => c.Start).ToList();
        var outList = new List<CutRange>();
        foreach (var c in list)
        {
            if (outList.Count > 0 && c.Start <= outList[^1].End)
                outList[^1] = new CutRange(outList[^1].Start, Math.Max(outList[^1].End, c.End));
            else outList.Add(c);
        }
        return outList;
    }

    /// <summary>Le parti di [a,b) che restano togliendo i tagli.</summary>
    public static List<CutRange> Keep(long a, long b, IReadOnlyList<CutRange> cuts)
    {
        var res = new List<CutRange>();
        long pos = a;
        foreach (var c in cuts)
        {
            if (c.End <= pos) continue;
            if (c.Start >= b) break;
            if (c.Start > pos) res.Add(new CutRange(pos, Math.Min(c.Start, b)));
            pos = Math.Max(pos, c.End);
            if (pos >= b) break;
        }
        if (pos < b) res.Add(new CutRange(pos, b));
        return res;
    }

    public static long KeptLength(long a, long b, IReadOnlyList<CutRange> cuts) => Keep(a, b, cuts).Sum(r => r.Length);

    /// <summary>Se f cade in un taglio, lo sposta alla fine del taglio.</summary>
    public static long SkipCut(long f, IReadOnlyList<CutRange> cuts)
    {
        foreach (var c in cuts) if (c.Contains(f)) return c.End;
        return f;
    }
}
