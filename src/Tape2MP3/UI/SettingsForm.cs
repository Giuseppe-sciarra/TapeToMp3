using Tape2MP3.Export;

namespace Tape2MP3.UI;

/// <summary>Impostazioni: destinazioni di rete + parametri dell'analisi buchi.</summary>
public sealed class SettingsForm : Form
{
    private readonly AppSettings _s;
    private readonly List<Destination> _dests;
    private readonly ListBox _list;
    private readonly NumericUpDown _margin, _minGap, _minPause, _minTrack, _autoSec, _autoDb;
    private readonly CheckBox _drops, _autoPause, _openFolder, _checkUpd;
    private readonly ComboBox _theme;

    public SettingsForm(AppSettings s)
    {
        _s = s;
        _dests = s.Destinations.Select(d => new Destination { Name = d.Name, Path = d.Path, User = d.User, PasswordEnc = d.PasswordEnc }).ToList();

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Impostazioni — Tape2MP3";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(660, 716);
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9.5f);

        // --- destinazioni
        var gDest = new GroupBox { Text = "Destinazioni di salvataggio (cartelle locali o share di rete)", ForeColor = Theme.Text, Bounds = new Rectangle(12, 10, 636, 240) };
        _list = new ListBox { Bounds = new Rectangle(12, 26, 480, 200), IntegralHeight = false };
        Theme.StyleInput(_list);
        gDest.Controls.Add(_list);
        int bx = 502, by = 26;
        foreach (var (txt, act) in new (string, Action)[]
        {
            ("Aggiungi…", AddDest), ("Modifica…", EditDest), ("Rimuovi", RemoveDest), ("Su", () => MoveDest(-1)), ("Giù", () => MoveDest(1)), ("Prova", TestDest)
        })
        {
            var b = Theme.MakeButton(txt, BtnKind.Neutral, 120);
            b.Height = 30; b.Font = new Font("Segoe UI", 9f);
            b.Location = new Point(bx, by); by += 34;
            b.Click += (o, e) => act();
            gDest.Controls.Add(b);
        }
        _list.DoubleClick += (o, e) => EditDest();
        Controls.Add(gDest);

        // --- analisi buchi
        var gGap = new GroupBox { Text = "Analisi buchi (si adatta al fruscio di ogni cassetta)", ForeColor = Theme.Text, Bounds = new Rectangle(12, 258, 636, 188) };
        _margin = Num(1, 20, (decimal)s.GapMarginDb, 1, 0.5m);
        _minGap = Num(0.2m, 10, (decimal)s.MinGapSec, 1, 0.1m);
        _minPause = Num(0.5m, 15, (decimal)s.MinSilenceSec, 1, 0.1m);
        _minTrack = Num(5, 600, (decimal)s.MinTrackSec, 0, 5);
        AddRow(gGap, 26, "Margine sopra il fruscio (dB):", _margin, "più alto = trova più buchi");
        AddRow(gGap, 56, "Buco minimo (secondi):", _minGap, "più corti vengono ignorati");
        AddRow(gGap, 86, "Pausa fra brani da (secondi):", _minPause, "sotto è \"buco breve\"");
        AddRow(gGap, 116, "Brano minimo (secondi):", _minTrack, "per \"Dividi sulle pause\"");
        _drops = new CheckBox { Text = "Segnala anche i cali improvvisi dentro la musica (dropout del nastro)", Checked = s.DetectDrops, AutoSize = true, Location = new Point(14, 150), ForeColor = Theme.Text };
        gGap.Controls.Add(_drops);
        Controls.Add(gGap);

        // --- registrazione / esportazione
        var gRec = new GroupBox { Text = "Registrazione ed esportazione", ForeColor = Theme.Text, Bounds = new Rectangle(12, 454, 636, 124) };
        _autoPause = new CheckBox { Text = "Pausa automatica dopo", Checked = s.AutoPause, AutoSize = true, Location = new Point(14, 28), ForeColor = Theme.Text };
        _autoSec = Num(10, 600, (decimal)s.AutoPauseSec, 0, 5);
        _autoSec.Location = new Point(200, 26);
        var l1 = new Label { Text = "s sotto", AutoSize = true, Location = new Point(276, 29), ForeColor = Theme.TextDim };
        _autoDb = Num(-90, -20, (decimal)s.SilenceDb, 0, 1);
        _autoDb.Location = new Point(330, 26);
        var l2 = new Label { Text = "dBFS", AutoSize = true, Location = new Point(406, 29), ForeColor = Theme.TextDim };
        var l3 = new Label
        {
            Text = "Sconsigliata con le cassette: il fruscio inganna. Di norma meglio registrare tutto e controllare i buchi dopo.",
            AutoSize = false, Size = new Size(600, 20), Location = new Point(34, 54), ForeColor = Theme.TextDim, Font = new Font("Segoe UI", 8.5f)
        };
        _openFolder = new CheckBox { Text = "Apri la cartella al termine dell'esportazione", Checked = s.OpenFolderAfterExport, AutoSize = true, Location = new Point(14, 86), ForeColor = Theme.Text };
        gRec.Controls.AddRange(new Control[] { _autoPause, _autoSec, l1, _autoDb, l2, l3, _openFolder });
        Controls.Add(gRec);

        // --- aspetto
        var gLook = new GroupBox { Text = "Aspetto e aggiornamenti", ForeColor = Theme.Text, Bounds = new Rectangle(12, 586, 636, 64) };
        gLook.Controls.Add(new Label { Text = "Tema:", AutoSize = true, Location = new Point(14, 29), ForeColor = Theme.Text });
        _theme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(70, 25), Width = 140 };
        Theme.StyleInput(_theme);
        _theme.Items.AddRange(Enum.GetNames(typeof(ThemeMode)));
        _theme.SelectedItem = Enum.TryParse<ThemeMode>(s.Theme, out var tm) ? tm.ToString() : ThemeMode.Automatico.ToString();
        _checkUpd = new CheckBox { Text = "Controlla se c'è una versione nuova all'avvio", Checked = s.CheckUpdates, AutoSize = true, Location = new Point(240, 28), ForeColor = Theme.Text };
        gLook.Controls.Add(_theme);
        gLook.Controls.Add(_checkUpd);
        Controls.Add(gLook);

        var ok = Theme.MakeButton("Salva", BtnKind.Primary, 110);
        ok.Location = new Point(ClientSize.Width - 240, ClientSize.Height - 50);
        ok.Click += (o, e) => { Commit(); DialogResult = DialogResult.OK; };
        var cancel = Theme.MakeButton("Annulla", BtnKind.Neutral, 110);
        cancel.Location = new Point(ClientSize.Width - 122, ClientSize.Height - 50);
        cancel.Click += (o, e) => DialogResult = DialogResult.Cancel;
        Controls.Add(ok); Controls.Add(cancel);
        AcceptButton = ok; CancelButton = cancel;

        RefreshList();
        ResumeLayout(false);
        HandleCreated += (o, e) => Theme.SetDarkTitleBar(this);
    }

    private static NumericUpDown Num(decimal min, decimal max, decimal val, int dec, decimal inc)
    {
        var n = new NumericUpDown { Minimum = min, Maximum = max, DecimalPlaces = dec, Increment = inc, Width = 70 };
        n.Value = Math.Clamp(val, min, max);
        Theme.StyleInput(n);
        return n;
    }

    private static void AddRow(Control parent, int y, string label, Control input, string hint)
    {
        parent.Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(14, y + 3), ForeColor = Theme.Text });
        input.Location = new Point(230, y);
        parent.Controls.Add(input);
        if (!string.IsNullOrEmpty(hint))
            parent.Controls.Add(new Label { Text = hint, AutoSize = true, Location = new Point(310, y + 3), ForeColor = Theme.TextDim });
    }

    private void RefreshList(int select = -1)
    {
        _list.Items.Clear();
        foreach (var d in _dests) _list.Items.Add(d);
        if (select >= 0 && select < _list.Items.Count) _list.SelectedIndex = select;
    }

    private void AddDest()
    {
        var d = new Destination();
        if (DestinationDialog.Edit(this, d)) { _dests.Add(d); RefreshList(_dests.Count - 1); }
    }

    private void EditDest()
    {
        int i = _list.SelectedIndex;
        if (i < 0) return;
        if (DestinationDialog.Edit(this, _dests[i])) RefreshList(i);
    }

    private void RemoveDest()
    {
        int i = _list.SelectedIndex;
        if (i < 0) return;
        _dests.RemoveAt(i);
        RefreshList(Math.Min(i, _dests.Count - 1));
    }

    private void MoveDest(int dir)
    {
        int i = _list.SelectedIndex, j = i + dir;
        if (i < 0 || j < 0 || j >= _dests.Count) return;
        (_dests[i], _dests[j]) = (_dests[j], _dests[i]);
        RefreshList(j);
    }

    private void TestDest()
    {
        int i = _list.SelectedIndex;
        if (i < 0) return;
        var d = _dests[i];
        Cursor = Cursors.WaitCursor;
        try
        {
            NetShare.EnsureReady(d, d.Path);
            MessageBox.Show(this, $"Destinazione raggiungibile e scrivibile:\n{d.Path}", "Prova", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Prova", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { Cursor = Cursors.Default; }
    }

    private void Commit()
    {
        _s.Destinations = _dests;
        _s.GapMarginDb = (double)_margin.Value;
        _s.MinGapSec = (double)_minGap.Value;
        _s.MinSilenceSec = (double)_minPause.Value;
        _s.MinTrackSec = (double)_minTrack.Value;
        _s.DetectDrops = _drops.Checked;
        _s.AutoPause = _autoPause.Checked;
        _s.AutoPauseSec = (double)_autoSec.Value;
        _s.SilenceDb = (double)_autoDb.Value;
        _s.OpenFolderAfterExport = _openFolder.Checked;
        _s.Theme = _theme.SelectedItem as string ?? "Automatico";
        _s.CheckUpdates = _checkUpd.Checked;
        if (_s.LastDestination >= _dests.Count) _s.LastDestination = 0;
    }
}

internal static class DestinationDialog
{
    public static bool Edit(IWin32Window owner, Destination d)
    {
        using var f = new Form
        {
            Text = "Destinazione",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false,
            BackColor = Theme.Back, ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 9.5f),
            AutoScaleDimensions = new SizeF(96f, 96f), AutoScaleMode = AutoScaleMode.Dpi
        };
        f.SuspendLayout();
        f.ClientSize = new Size(560, 250);
        TextBox T(string v, int y, bool pwd = false)
        {
            var t = new TextBox { Text = v, Location = new Point(150, y), Width = 290, UseSystemPasswordChar = pwd };
            Theme.StyleInput(t);
            f.Controls.Add(t);
            return t;
        }
        void L(string s, int y) => f.Controls.Add(new Label { Text = s, AutoSize = true, Location = new Point(14, y + 3), ForeColor = Theme.Text });

        L("Nome:", 16); var name = T(d.Name, 14);
        L("Percorso:", 50); var path = T(d.Path, 48);
        var browse = Theme.MakeButton("Sfoglia…", BtnKind.Neutral, 100); browse.Height = 27; browse.Font = new Font("Segoe UI", 9f);
        browse.Location = new Point(448, 46);
        browse.Click += (o, e) =>
        {
            using var fb = new FolderBrowserDialog { SelectedPath = path.Text, UseDescriptionForTitle = true, Description = "Cartella di destinazione" };
            if (fb.ShowDialog(f) == DialogResult.OK) path.Text = fb.SelectedPath;
        };
        f.Controls.Add(browse);
        f.Controls.Add(new Label
        {
            Text = @"Es.: Y:\Riversaggi  oppure  \\nas\audio\Cassette",
            AutoSize = true, Location = new Point(150, 76), ForeColor = Theme.TextDim
        });
        L("Utente (opz.):", 110); var user = T(d.User, 108);
        L("Password (opz.):", 144); var pwd = T(d.Password, 142, true);
        f.Controls.Add(new Label
        {
            Text = "Solo per share \\\\server\\cartella che chiedono credenziali. La password è cifrata per il tuo utente Windows.",
            AutoSize = false, Size = new Size(400, 34), Location = new Point(150, 168), ForeColor = Theme.TextDim
        });

        var ok = Theme.MakeButton("OK", BtnKind.Primary, 100); ok.Location = new Point(338, 206);
        var ko = Theme.MakeButton("Annulla", BtnKind.Neutral, 100); ko.Location = new Point(446, 206);
        ok.DialogResult = DialogResult.OK; ko.DialogResult = DialogResult.Cancel;
        f.Controls.Add(ok); f.Controls.Add(ko);
        f.AcceptButton = ok; f.CancelButton = ko;
        f.ResumeLayout(false);
        f.HandleCreated += (o, e) => Theme.SetDarkTitleBar(f);

        while (f.ShowDialog(owner) == DialogResult.OK)
        {
            if (string.IsNullOrWhiteSpace(path.Text))
            {
                MessageBox.Show(f, "Indica il percorso.", "Destinazione", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                continue;
            }
            d.Name = name.Text.Trim();
            d.Path = path.Text.Trim().TrimEnd('\\');
            if (d.Path.Length == 2 && d.Path[1] == ':') d.Path += "\\";
            d.User = user.Text.Trim();
            d.Password = pwd.Text;
            return true;
        }
        return false;
    }
}
