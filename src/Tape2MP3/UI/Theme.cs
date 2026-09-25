using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Tape2MP3.UI;

public enum ThemeMode { Automatico, Chiaro, Scuro }

public enum Tone { Dim, Normal, Ok, Warn, Error, Rec, Gap }

/// <summary>
/// Palette chiara/scura. I controlli custom leggono i colori al momento del disegno,
/// quelli standard vengono ricolorati da ApplyTo() quando cambia il tema.
/// </summary>
public static class Theme
{
    public static ThemeMode Mode { get; private set; } = ThemeMode.Automatico;
    public static bool IsDark { get; private set; } = true;
    public static event Action Changed;

    // superfici
    public static Color Back, Panel, Panel2, Border, InputBg;
    // testo
    public static Color Text, TextDim, TextOnAccent;
    // pulsanti
    public static Color BtnFace, BtnBorder, BtnHover, BtnPressed;
    // semantici
    public static Color Accent, Rec, Ok, Warn, Error;
    // forma d'onda
    public static Color LaneBg, LaneGuide, Ruler, RulerText, Wave, WaveOut, WaveClip, Marker, InColor, OutColor, PlayHead, CursorLine,
        Gap, GapDrop, SelFill, SelEdge, ShadeOut, CutHatchFore, CutHatchBack, CutText;
    // griglie
    public static Color GridSel, GridHeader;

    static Theme() => Set(ThemeMode.Automatico);

    public static void Set(ThemeMode mode)
    {
        Mode = mode;
        IsDark = mode switch
        {
            ThemeMode.Chiaro => false,
            ThemeMode.Scuro => true,
            _ => WindowsUsesDark()
        };
        if (IsDark) Dark(); else Light();
        Changed?.Invoke();
    }

    public static ThemeMode Next(ThemeMode m) => m switch
    {
        ThemeMode.Automatico => ThemeMode.Chiaro,
        ThemeMode.Chiaro => ThemeMode.Scuro,
        _ => ThemeMode.Automatico
    };

    private static bool WindowsUsesDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (k?.GetValue("AppsUseLightTheme") is int v) return v == 0;
        }
        catch { }
        return false;
    }

    private static void Dark()
    {
        Back = C(24, 25, 29); Panel = C(34, 36, 42); Panel2 = C(44, 47, 55); Border = C(72, 76, 88); InputBg = C(46, 49, 58);
        Text = C(236, 238, 243); TextDim = C(163, 168, 182); TextOnAccent = Color.White;
        BtnFace = C(64, 68, 82); BtnBorder = C(104, 110, 128); BtnHover = C(80, 85, 102); BtnPressed = C(54, 57, 69);
        Accent = C(58, 130, 246); Rec = C(222, 56, 56); Ok = C(40, 170, 95); Warn = C(240, 180, 40); Error = C(255, 95, 95);

        LaneBg = C(17, 18, 22); LaneGuide = C(42, 45, 54); Ruler = C(34, 36, 42); RulerText = C(160, 165, 178);
        Wave = C(96, 156, 255); WaveOut = C(66, 74, 94); WaveClip = C(255, 72, 72);
        Marker = C(255, 205, 60); InColor = C(70, 210, 110); OutColor = C(245, 85, 85); PlayHead = C(255, 150, 40);
        CursorLine = Color.FromArgb(230, 255, 255, 255);
        Gap = C(255, 140, 30); GapDrop = C(205, 110, 255);
        SelFill = Color.FromArgb(60, 120, 170, 255); SelEdge = Color.FromArgb(220, 140, 190, 255);
        ShadeOut = Color.FromArgb(160, 8, 8, 10);
        CutHatchFore = Color.FromArgb(160, 210, 50, 50); CutHatchBack = Color.FromArgb(180, 22, 10, 12); CutText = C(255, 160, 160);
        GridSel = C(46, 84, 150); GridHeader = C(44, 47, 55);
    }

    private static void Light()
    {
        Back = C(236, 238, 243); Panel = C(250, 251, 253); Panel2 = C(229, 232, 239); Border = C(190, 196, 208); InputBg = Color.White;
        Text = C(28, 31, 38); TextDim = C(92, 99, 114); TextOnAccent = Color.White;
        BtnFace = Color.White; BtnBorder = C(168, 175, 190); BtnHover = C(232, 238, 250); BtnPressed = C(212, 220, 236);
        Accent = C(37, 99, 235); Rec = C(205, 38, 38); Ok = C(22, 140, 72); Warn = C(176, 110, 0); Error = C(200, 30, 30);

        LaneBg = Color.White; LaneGuide = C(228, 231, 238); Ruler = C(243, 245, 249); RulerText = C(90, 97, 112);
        Wave = C(37, 99, 235); WaveOut = C(178, 186, 204); WaveClip = C(220, 30, 30);
        Marker = C(200, 140, 0); InColor = C(20, 150, 70); OutColor = C(210, 40, 40); PlayHead = C(235, 110, 0);
        CursorLine = Color.FromArgb(220, 20, 22, 28);
        Gap = C(240, 120, 0); GapDrop = C(150, 60, 220);
        SelFill = Color.FromArgb(55, 37, 99, 235); SelEdge = Color.FromArgb(220, 37, 99, 235);
        ShadeOut = Color.FromArgb(150, 200, 204, 214);
        CutHatchFore = Color.FromArgb(150, 220, 60, 60); CutHatchBack = Color.FromArgb(150, 255, 225, 225); CutText = C(170, 20, 20);
        GridSel = C(205, 222, 252); GridHeader = C(229, 232, 239);
    }

    private static Color C(int r, int g, int b) => Color.FromArgb(r, g, b);

    public static Color ToneColor(Tone t) => t switch
    {
        Tone.Ok => Ok,
        Tone.Warn => Warn,
        Tone.Error => Error,
        Tone.Rec => Rec,
        Tone.Gap => Gap,
        Tone.Normal => Text,
        _ => TextDim
    };

    // ------------------------------------------------------------------ fabbriche

    public static TdButton MakeButton(string text, BtnKind kind = BtnKind.Neutral, int width = 110)
    {
        return new TdButton
        {
            Text = text,
            Kind = kind,
            Width = width,
            Height = 36,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Margin = new Padding(3)
        };
    }

    public static void StyleInput(Control c)
    {
        c.BackColor = InputBg;
        c.ForeColor = Text;
        if (c is ComboBox cb) cb.FlatStyle = FlatStyle.Flat;
        if (c is TextBox tb) tb.BorderStyle = BorderStyle.FixedSingle;
        if (c is NumericUpDown nu) nu.BorderStyle = BorderStyle.FixedSingle;
    }

    public static void StyleGrid(DataGridView g)
    {
        g.BackgroundColor = Panel;
        g.GridColor = Border;
        g.DefaultCellStyle.BackColor = Panel;
        g.DefaultCellStyle.ForeColor = Text;
        g.DefaultCellStyle.SelectionBackColor = GridSel;
        g.DefaultCellStyle.SelectionForeColor = IsDark ? Color.White : Text;
        g.ColumnHeadersDefaultCellStyle.BackColor = GridHeader;
        g.ColumnHeadersDefaultCellStyle.ForeColor = TextDim;
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = GridHeader;
        g.ColumnHeadersDefaultCellStyle.SelectionForeColor = TextDim;
    }

    public static Label MakeLabel(string text, bool dim = true) => new Label
    {
        Text = text,
        AutoSize = true,
        ForeColor = dim ? TextDim : Text,
        Tag = dim ? "dim" : null,
        Margin = new Padding(3, 9, 3, 3)
    };

    /// <summary>
    /// Ricolora ricorsivamente i controlli standard. Tag supportati:
    /// "panel" (sfondo Panel), "dim" (testo secondario), "keep" (colori gestiti dal codice).
    /// </summary>
    public static void ApplyTo(Control root)
    {
        ApplyOne(root);
        foreach (Control c in root.Controls) ApplyTo(c);
    }

    private static void ApplyOne(Control c)
    {
        string tag = c.Tag as string;
        if (tag == "keep") { c.Invalidate(); return; }
        switch (c)
        {
            case Form f:
                f.BackColor = Back; f.ForeColor = Text;
                SetDarkTitleBar(f);
                break;
            case TdButton b:
                b.Invalidate();
                break;
            case DataGridView g:
                StyleGrid(g);
                break;
            case TextBox or ComboBox or NumericUpDown or ListBox:
                c.BackColor = InputBg; c.ForeColor = Text;
                break;
            case CheckBox or RadioButton:
                c.ForeColor = Text;
                break;
            case GroupBox:
                c.ForeColor = Text;
                break;
            case Label:
                c.ForeColor = tag == "dim" ? TextDim : Text;
                break;
            case WaveformView or LevelMeter:
                c.BackColor = tag == "panel" ? Panel : Back;
                c.Invalidate();
                break;
            case TableLayoutPanel or FlowLayoutPanel or System.Windows.Forms.Panel:
                c.BackColor = tag == "panel" ? Panel : (c.Parent is Form ? Back : (c.Parent?.BackColor ?? Back));
                break;
        }
    }

    // barra del titolo scura su Windows 10 20H1+ / 11
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public static void SetDarkTitleBar(Form f)
    {
        if (!f.IsHandleCreated) return;
        try
        {
            int v = IsDark ? 1 : 0;
            if (DwmSetWindowAttribute(f.Handle, 20, ref v, sizeof(int)) != 0)
                DwmSetWindowAttribute(f.Handle, 19, ref v, sizeof(int));
        }
        catch { }
    }

    public static string FormatTime(double sec, bool ms = false)
    {
        if (sec < 0) sec = 0;
        var t = TimeSpan.FromSeconds(sec);
        string s = t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
        return ms ? s + $".{t.Milliseconds / 100}" : s;
    }

    public static string FormatBytes(long b)
    {
        if (b >= 1L << 30) return $"{b / (double)(1L << 30):0.0} GB";
        if (b >= 1L << 20) return $"{b / (double)(1L << 20):0} MB";
        return $"{b / 1024.0:0} KB";
    }
}
