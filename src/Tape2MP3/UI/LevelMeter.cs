using System.Drawing.Drawing2D;

namespace Tape2MP3.UI;

/// <summary>VU meter orizzontale L/R in dBFS con picco trattenuto e spia di saturazione.</summary>
public sealed class LevelMeter : Control
{
    private const float MinDb = -60f;
    private float _l = MinDb, _r = MinDb;
    private float _holdL = MinDb, _holdR = MinDb;
    private DateTime _holdTimeL, _holdTimeR;
    private DateTime _clipUntil;

    public LevelMeter()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Height = 46;
        BackColor = Theme.Panel;
    }

    private static float ToDb(float v) => v <= 0.000001f ? MinDb : Math.Max(MinDb, 20f * (float)Math.Log10(v));

    /// <summary>Picchi lineari 0..1. Chiamare ~30 volte al secondo.</summary>
    public void Push(float l, float r, bool clip)
    {
        float dl = ToDb(l), dr = ToDb(r);
        // salita immediata, discesa morbida
        _l = dl > _l ? dl : Math.Max(dl, _l - 1.2f);
        _r = dr > _r ? dr : Math.Max(dr, _r - 1.2f);
        var now = DateTime.UtcNow;
        if (dl >= _holdL || (now - _holdTimeL).TotalSeconds > 1.5) { _holdL = dl; _holdTimeL = now; }
        if (dr >= _holdR || (now - _holdTimeR).TotalSeconds > 1.5) { _holdR = dr; _holdTimeR = now; }
        if (clip) _clipUntil = now.AddSeconds(2);
        Invalidate();
    }

    public void Reset()
    {
        _l = _r = _holdL = _holdR = MinDb;
        _clipUntil = DateTime.MinValue;
        Invalidate();
    }

    public float CurrentMaxDb => Math.Max(_holdL, _holdR);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        int labelW = 18, clipW = 34;
        int barX = labelW, barW = Width - labelW - clipW - 6;
        int barH = (Height - 16) / 2 - 2;
        if (barW < 20 || barH < 4) return;

        using var font = new Font("Segoe UI", 7.5f);
        using var txt = new SolidBrush(Theme.TextDim);

        DrawBar(g, barX, 2, barW, barH, _l, _holdL);
        DrawBar(g, barX, 4 + barH, barW, barH, _r, _holdR);
        g.DrawString("L", font, txt, 3, 2 + barH / 2 - 7);
        g.DrawString("R", font, txt, 3, 4 + barH + barH / 2 - 7);

        // scala
        int sy = 6 + barH * 2;
        foreach (var db in new[] { -60, -48, -36, -24, -18, -12, -6, -3, 0 })
        {
            float x = barX + (db - MinDb) / -MinDb * barW;
            g.DrawLine(Pens.Gray, x, sy, x, sy + 3);
            var s = db.ToString();
            var sz = g.MeasureString(s, font);
            float tx = Math.Clamp(x - sz.Width / 2, barX, barX + barW - sz.Width);
            g.DrawString(s, font, txt, tx, sy + 2);
        }

        // spia clip
        bool clip = DateTime.UtcNow < _clipUntil;
        var clipRect = new Rectangle(Width - clipW - 2, 2, clipW, barH * 2 + 2);
        using (var b = new SolidBrush(clip ? Color.FromArgb(230, 40, 40) : Color.FromArgb(60, 60, 64)))
            g.FillRectangle(b, clipRect);
        using var cf = new Font("Segoe UI", 7f, FontStyle.Bold);
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("CLIP", cf, clip ? Brushes.White : Brushes.Gray, clipRect, sf);
    }

    private static void DrawBar(Graphics g, int x, int y, int w, int h, float db, float hold)
    {
        using (var bg = new SolidBrush(Color.FromArgb(34, 34, 38))) g.FillRectangle(bg, x, y, w, h);
        float frac = (db - MinDb) / -MinDb;
        int fw = (int)(w * frac);
        if (fw > 0)
        {
            using var lg = new LinearGradientBrush(new Rectangle(x, y, w, h), Color.Green, Color.Red, 0f);
            var blend = new ColorBlend
            {
                Colors = new[] { Color.FromArgb(40, 170, 80), Color.FromArgb(60, 200, 90), Color.FromArgb(230, 200, 40), Color.FromArgb(235, 60, 40) },
                Positions = new[] { 0f, (-18 - MinDb) / -MinDb, (-6 - MinDb) / -MinDb, 1f }
            };
            lg.InterpolationColors = blend;
            g.FillRectangle(lg, x, y, fw, h);
        }
        float hx = x + (hold - MinDb) / -MinDb * w;
        if (hold > MinDb + 0.5f)
            using (var p = new Pen(Color.White, 2)) g.DrawLine(p, hx, y, hx, y + h);
    }
}
