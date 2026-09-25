using System.Drawing.Drawing2D;

namespace Tape2MP3.UI;

public enum BtnKind { Neutral, Primary, Danger, Success }

/// <summary>
/// Pulsante disegnato a mano: sempre leggibile in tema chiaro e scuro,
/// con stati hover/premuto/disabilitato ben distinti e bordo arrotondato.
/// Resta un Button vero (Invio/Esc, DialogResult, tastiera funzionano).
/// </summary>
public sealed class TdButton : Button
{
    private bool _hover, _down;
    private BtnKind _kind;

    public BtnKind Kind { get => _kind; set { _kind = value; Invalidate(); } }

    public TdButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Back);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Color fill, border, text;
        if (Kind == BtnKind.Neutral)
        {
            fill = _down ? Theme.BtnPressed : _hover ? Theme.BtnHover : Theme.BtnFace;
            border = _hover ? Theme.Accent : Theme.BtnBorder;
            text = Theme.Text;
        }
        else
        {
            var baseCol = Kind switch { BtnKind.Primary => Theme.Accent, BtnKind.Danger => Theme.Rec, _ => Theme.Ok };
            fill = _down ? Shade(baseCol, -0.18f) : _hover ? Shade(baseCol, 0.12f) : baseCol;
            border = Shade(baseCol, -0.25f);
            text = Theme.TextOnAccent;
        }

        if (!Enabled)
        {
            // disabilitato: più tenue ma sempre leggibile
            var bg = Parent?.BackColor ?? Theme.Back;
            fill = Mix(fill, bg, Kind == BtnKind.Neutral ? 0.55f : 0.6f);
            border = Mix(border, bg, 0.5f);
            text = Mix(Kind == BtnKind.Neutral ? Theme.Text : Theme.TextOnAccent, fill, 0.55f);
        }

        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Round(r, Math.Min(7f, Height / 4f)))
        {
            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            using (var p = new Pen(border, 1.2f)) g.DrawPath(p, path);
        }

        if (Focused && ShowFocusCues && Enabled)
        {
            var fr = RectangleF.Inflate(r, -3, -3);
            using var fp = new Pen(Kind == BtnKind.Neutral ? Theme.Accent : Color.FromArgb(200, Color.White), 1.5f) { DashStyle = DashStyle.Dot };
            using var path2 = Round(fr, Math.Min(5f, Height / 5f));
            g.DrawPath(fp, path2);
        }

        TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static Color Shade(Color c, float f)
    {
        if (f >= 0) return Color.FromArgb(c.A, (int)(c.R + (255 - c.R) * f), (int)(c.G + (255 - c.G) * f), (int)(c.B + (255 - c.B) * f));
        f = -f;
        return Color.FromArgb(c.A, (int)(c.R * (1 - f)), (int)(c.G * (1 - f)), (int)(c.B * (1 - f)));
    }

    private static Color Mix(Color a, Color b, float t) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
}
