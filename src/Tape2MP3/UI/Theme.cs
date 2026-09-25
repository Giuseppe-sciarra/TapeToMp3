namespace Tape2MP3.UI;

public static class Theme
{
    public static readonly Color Back = Color.FromArgb(28, 28, 32);
    public static readonly Color Panel = Color.FromArgb(38, 38, 44);
    public static readonly Color Panel2 = Color.FromArgb(48, 48, 56);
    public static readonly Color Border = Color.FromArgb(64, 64, 74);
    public static readonly Color Text = Color.FromArgb(232, 232, 236);
    public static readonly Color TextDim = Color.FromArgb(160, 160, 170);
    public static readonly Color Accent = Color.FromArgb(70, 140, 255);
    public static readonly Color Rec = Color.FromArgb(220, 50, 50);
    public static readonly Color Ok = Color.FromArgb(60, 190, 100);
    public static readonly Color Warn = Color.FromArgb(240, 180, 40);

    public static readonly Color Wave = Color.FromArgb(90, 150, 255);
    public static readonly Color WaveOut = Color.FromArgb(70, 80, 100);
    public static readonly Color WaveClip = Color.FromArgb(255, 70, 70);
    public static readonly Color Marker = Color.FromArgb(255, 205, 60);
    public static readonly Color InColor = Color.FromArgb(70, 210, 110);
    public static readonly Color OutColor = Color.FromArgb(240, 80, 80);
    public static readonly Color PlayHead = Color.FromArgb(255, 150, 40);
    public static readonly Color Gap = Color.FromArgb(255, 140, 30);
    public static readonly Color GapDrop = Color.FromArgb(220, 90, 255);

    public static Button MakeButton(string text, Color? back = null, int width = 110)
    {
        var b = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = back ?? Panel2,
            ForeColor = Text,
            Width = width,
            Height = 36,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(3),
            UseVisualStyleBackColor = false
        };
        b.FlatAppearance.BorderColor = Border;
        b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(b.BackColor, 0.15f);
        return b;
    }

    public static void StyleInput(Control c)
    {
        c.BackColor = Panel2;
        c.ForeColor = Text;
        if (c is ComboBox cb) cb.FlatStyle = FlatStyle.Flat;
        if (c is TextBox tb) tb.BorderStyle = BorderStyle.FixedSingle;
        if (c is NumericUpDown nu) nu.BorderStyle = BorderStyle.FixedSingle;
    }

    public static Label MakeLabel(string text, bool dim = true) => new Label
    {
        Text = text,
        AutoSize = true,
        ForeColor = dim ? TextDim : Text,
        Margin = new Padding(3, 9, 3, 3)
    };

    public static string FormatTime(double sec, bool ms = false)
    {
        if (sec < 0) sec = 0;
        var t = TimeSpan.FromSeconds(sec);
        string s = t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
        return ms ? s + $".{t.Milliseconds / 100}" : s;
    }
}
