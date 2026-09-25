using System.Diagnostics;
using Tape2MP3.Export;

namespace Tape2MP3.UI;

/// <summary>Informazioni: versione, build, ffmpeg, cartelle e controllo aggiornamenti.</summary>
public sealed class AboutForm : Form
{
    private readonly AppSettings _s;
    private readonly Label _lblUpd;
    private readonly LinkLabel _lnkRelease;
    public AppInfo.UpdateInfo UpdateResult { get; private set; }

    public AboutForm(AppSettings s, AppInfo.UpdateInfo known)
    {
        _s = s;
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Informazioni — Tape2MP3";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(560, 400);
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9.5f);

        var pic = new PictureBox { Location = new Point(20, 20), Size = new Size(72, 72), SizeMode = PictureBoxSizeMode.Zoom };
        try
        {
            using var st = typeof(AboutForm).Assembly.GetManifestResourceStream("Tape2MP3.app.ico");
            if (st != null) pic.Image = new Icon(st, 128, 128).ToBitmap();
        }
        catch { }
        Controls.Add(pic);

        Controls.Add(new Label { Text = "Tape2MP3", Font = new Font("Segoe UI", 18f, FontStyle.Bold), AutoSize = true, Location = new Point(106, 18), ForeColor = Theme.Text });
        string ver = $"Versione {AppInfo.Version}" + (string.IsNullOrEmpty(AppInfo.Commit) ? "" : $"  ·  commit {AppInfo.Commit}") +
                     (AppInfo.IsDevBuild ? "  ·  build di sviluppo" : "");
        Controls.Add(new Label { Text = ver, AutoSize = true, Location = new Point(109, 56), ForeColor = Theme.Accent, Font = new Font("Segoe UI", 10f, FontStyle.Bold) });
        Controls.Add(new Label { Text = "Riversaggio musicassette in MP3 / FLAC / WAV / M4A", AutoSize = true, Location = new Point(109, 80), ForeColor = Theme.TextDim });

        int y = 118;
        void Row(string k, string v)
        {
            Controls.Add(new Label { Text = k, AutoSize = true, Location = new Point(20, y), ForeColor = Theme.TextDim });
            Controls.Add(new Label { Text = v, AutoSize = false, Size = new Size(400, 20), AutoEllipsis = true, Location = new Point(140, y), ForeColor = Theme.Text });
            y += 24;
        }
        var bd = AppInfo.BuildDate;
        Row("Compilato il:", bd == DateTime.MinValue ? "—" : bd.ToString("dd/MM/yyyy HH:mm"));
        var ff = Exporter.FindFfmpeg();
        Row("ffmpeg:", ff == null ? "NON TROVATO (serve accanto all'exe)" : $"{AppInfo.FfmpegVersion(ff)}  —  {ff}");
        Row(".NET:", Environment.Version.ToString());
        Row("Registrazioni:", AppSettings.WorkDir);
        Row("Impostazioni:", AppSettings.Dir);

        var bWork = Theme.MakeButton("Apri cartella registrazioni", BtnKind.Neutral, 210); bWork.Height = 30; bWork.Font = new Font("Segoe UI", 9f);
        bWork.Location = new Point(20, y + 6);
        bWork.Click += (o, e) => OpenFolder(AppSettings.WorkDir);
        var bSet = Theme.MakeButton("Apri cartella impostazioni", BtnKind.Neutral, 210); bSet.Height = 30; bSet.Font = new Font("Segoe UI", 9f);
        bSet.Location = new Point(238, y + 6);
        bSet.Click += (o, e) => OpenFolder(AppSettings.Dir);
        Controls.Add(bWork); Controls.Add(bSet);
        y += 48;

        var bUpd = Theme.MakeButton("Controlla aggiornamenti", BtnKind.Primary, 210); bUpd.Height = 30; bUpd.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        bUpd.Location = new Point(20, y);
        bUpd.Click += async (o, e) => await Check();
        Controls.Add(bUpd);
        _lblUpd = new Label { AutoSize = false, Size = new Size(300, 20), Location = new Point(238, y + 6), ForeColor = Theme.TextDim, AutoEllipsis = true };
        Controls.Add(_lblUpd);
        y += 34;
        string repo = string.IsNullOrWhiteSpace(s.UpdateRepo) ? AppInfo.DefaultRepo : s.UpdateRepo;
        _lnkRelease = new LinkLabel
        {
            Text = $"github.com/{repo}/releases", AutoSize = true, Location = new Point(20, y + 4),
            LinkColor = Theme.Accent, ActiveLinkColor = Theme.Rec, VisitedLinkColor = Theme.Accent, LinkBehavior = LinkBehavior.HoverUnderline
        };
        _lnkRelease.LinkClicked += (o, e) => OpenUrl($"https://github.com/{repo}/releases");
        Controls.Add(_lnkRelease);

        Controls.Add(new Label { Text = "© Tastiere Digitali srls", AutoSize = true, Location = new Point(20, ClientSize.Height - 34), ForeColor = Theme.TextDim });
        var ok = Theme.MakeButton("Chiudi", BtnKind.Neutral, 100);
        ok.Location = new Point(ClientSize.Width - 118, ClientSize.Height - 48);
        ok.DialogResult = DialogResult.OK;
        Controls.Add(ok);
        AcceptButton = ok; CancelButton = ok;

        ShowUpdate(known);
        ResumeLayout(false);
        HandleCreated += (o, e) => Theme.SetDarkTitleBar(this);
    }

    private async Task Check()
    {
        _lblUpd.ForeColor = Theme.TextDim;
        _lblUpd.Text = "controllo…";
        var u = await AppInfo.CheckAsync(string.IsNullOrWhiteSpace(_s.UpdateRepo) ? AppInfo.DefaultRepo : _s.UpdateRepo);
        UpdateResult = u;
        if (u == null) { _lblUpd.ForeColor = Theme.Warn; _lblUpd.Text = "non raggiungibile (offline o repo privato)"; return; }
        ShowUpdate(u);
    }

    private void ShowUpdate(AppInfo.UpdateInfo u)
    {
        if (u == null) { _lblUpd.Text = ""; return; }
        if (u.IsNewer) { _lblUpd.ForeColor = Theme.Ok; _lblUpd.Text = $"⬆ Nuova versione {u.LatestVersion} disponibile!"; }
        else { _lblUpd.ForeColor = Theme.TextDim; _lblUpd.Text = $"✔ Sei all'ultima versione ({u.LatestVersion})"; }
    }

    private static void OpenFolder(string path)
    {
        try { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); } catch { }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}
