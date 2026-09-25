using System.Diagnostics;
using System.Media;
using Tape2MP3.Audio;
using Tape2MP3.Export;
using Tape2MP3.UI;

namespace Tape2MP3;

public sealed class MainForm : Form
{
    private enum State { Empty, Recording, Paused, Stopped, Busy }

    private readonly AppSettings _s;
    private readonly CaptureEngine _capture = new();
    private readonly PlaybackEngine _player = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };

    private State _state = State.Empty;
    private string _wavPath;
    private PeakData _peaks;
    private WavWriter _writer;
    private int _takeRate;
    private bool _exported;
    private bool _dirtyAfterExport;
    private bool _exporting;
    private CancellationTokenSource _cts;
    private readonly List<string> _titles = new();
    private DateTime _lowSince = DateTime.MaxValue;
    private GapAnalysis _analysis;
    private Tone _statusTone = Tone.Dim;
    private int _tickCount;
    private AppInfo.UpdateInfo _update;
    private TdButton _btnTheme;
    private LinkLabel _lnkVersion, _lnkUpdate;
    private Label _lblFooter;
    private bool _syncingGapGrid;

    // controlli
    private ComboBox _cbDevice;
    private CheckBox _chkMonitor;
    private LevelMeter _meter;
    private Label _lblSignal;
    private TdButton _btnRec, _btnPause, _btnStop, _btnPlay, _btnOpen, _btnNew, _btnSettings, _btnRefresh;
    private Label _lblTime, _lblStatus;
    private WaveformView _wave;
    private TdButton _btnIn, _btnOut, _btnAutoTrim, _btnCutSel, _btnUndoCut, _btnDetect, _btnAddMarker, _btnClearMarkers, _btnZoomIn, _btnZoomOut, _btnFit;
    private DataGridView _gapGrid;
    private Label _lblGaps;
    private TdButton _btnAnalyze, _btnPrevGap, _btnNextGap, _btnGapCut, _btnGapSplit, _btnGapIgnore;
    private DataGridView _grid;
    private ComboBox _cbFormat, _cbQuality, _cbDest;
    private CheckBox _chkSplit;
    private TextBox _txtFolder, _txtName;
    private TdButton _btnExport;
    private ProgressBar _progress;
    private Label _lblExport;

    public MainForm()
    {
        _s = AppSettings.Load();
        Directory.CreateDirectory(AppSettings.WorkDir);
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;

        Theme.Set(Enum.TryParse<ThemeMode>(_s.Theme, out var tm) ? tm : ThemeMode.Automatico);
        Text = $"Tape2MP3 v{AppInfo.Version} — Riversaggio musicassette";
        try { Icon = new Icon(typeof(MainForm).Assembly.GetManifestResourceStream("Tape2MP3.app.ico")); } catch { }
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9.5f);
        KeyPreview = true;

        BuildUi();
        ResumeLayout(false);
        PerformLayout();

        // dimensioni dopo l'auto-scale DPI (le salvate sono già in pixel reali)
        float k = DeviceDpi / 96f;
        MinimumSize = new Size((int)(1120 * k), (int)(720 * k));
        StartPosition = FormStartPosition.Manual;
        Size = new Size(Math.Max(MinimumSize.Width, _s.WinW), Math.Max(MinimumSize.Height, _s.WinH));
        if (_s.WinX >= 0 && Screen.AllScreens.Any(sc => sc.WorkingArea.Contains(_s.WinX + 50, _s.WinY + 50)))
            Location = new Point(_s.WinX, _s.WinY);
        else StartPosition = FormStartPosition.CenterScreen;
        if (_s.WinMax) WindowState = FormWindowState.Maximized;

        _capture.AutoPaused += () => BeginInvoke(new Action(OnAutoPaused));
        _capture.DeviceStopped += ex => BeginInvoke(new Action(() =>
        {
            if (_state == State.Recording) DoPause();
            SetStatus("Il dispositivo d'ingresso si è scollegato o è andato in errore: " + ex.Message, Tone.Error);
        }));
        _player.Finished += () => BeginInvoke(new Action(() => { _player.Stop(); _wave.PlayFrame = -1; UpdateUi(); }));
        _timer.Tick += OnTick;

        Theme.Changed += OnThemeChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnSystemPrefChanged;
        HandleCreated += (o, e) => Theme.SetDarkTitleBar(this);

        // trascina un file audio sulla finestra per aprirlo
        AllowDrop = true;
        DragEnter += (o, e) =>
        {
            e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true && (_state == State.Empty || _state == State.Stopped)
                ? DragDropEffects.Copy : DragDropEffects.None;
        };
        DragDrop += async (o, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
            if (_state != State.Empty && _state != State.Stopped) return;
            if (!DiscardCurrentKeepIfSame(files[0])) return;
            await OpenAudio(files[0]);
        };

        Load += (o, e) =>
        {
            LoadDevices();
            _timer.Start();
            UpdateUi();
            UpdateFooter();
            BeginInvoke(new Action(CheckRecovery));
            if (_s.CheckUpdates) _ = CheckUpdatesAsync(false);
            else _lnkUpdate.Text = "controlla aggiornamenti";
        };
    }

    // =====================================================================================
    // UI
    // =====================================================================================

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(10, 8, 10, 4), BackColor = Theme.Back
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));  // ingresso + meter
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));  // trasporto
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // forma d'onda
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));  // modifica
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 236)); // buchi + brani + export
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));  // barra inferiore
        Controls.Add(root);

        // --- riga 1: ingresso + livelli
        var r1 = new TableLayoutPanel { Tag = "panel", Dock = DockStyle.Fill, ColumnCount = 8, RowCount = 1, BackColor = Theme.Panel, Padding = new Padding(6, 4, 6, 4), Margin = new Padding(0, 0, 0, 6) };
        r1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        r1.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        r1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        r1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        r1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        r1.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        r1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        r1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        r1.Controls.Add(Theme.MakeLabel("Ingresso:", false), 0, 0);
        _cbDevice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(3, 10, 3, 3) };
        Theme.StyleInput(_cbDevice);
        _cbDevice.SelectedIndexChanged += (o, e) => OpenSelectedDevice();
        r1.Controls.Add(_cbDevice, 1, 0);
        _btnRefresh = Theme.MakeButton("⟳", BtnKind.Neutral, 34); _btnRefresh.Height = 30; _btnRefresh.Margin = new Padding(3, 8, 3, 3);
        _btnRefresh.Click += (o, e) => LoadDevices();
        new ToolTip().SetToolTip(_btnRefresh, "Aggiorna elenco dispositivi");
        r1.Controls.Add(_btnRefresh, 2, 0);
        _chkMonitor = new CheckBox { Text = "Ascolta ingresso", AutoSize = true, ForeColor = Theme.Text, Checked = _s.Monitor, Margin = new Padding(10, 13, 10, 3) };
        _chkMonitor.CheckedChanged += (o, e) => { _s.Monitor = _chkMonitor.Checked; try { _capture.SetMonitor(_chkMonitor.Checked); } catch { } };
        r1.Controls.Add(_chkMonitor, 3, 0);
        _meter = new LevelMeter { Dock = DockStyle.Fill, Margin = new Padding(6, 0, 6, 0) };
        r1.Controls.Add(_meter, 4, 0);
        _lblSignal = new Label { Tag = "keep", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = Theme.TextDim, Text = "—" };
        r1.Controls.Add(_lblSignal, 5, 0);
        _btnSettings = Theme.MakeButton("⚙", BtnKind.Neutral, 40); _btnSettings.Height = 34; _btnSettings.Font = new Font("Segoe UI", 12f);
        _btnSettings.Click += (o, e) => OpenSettings();
        new ToolTip().SetToolTip(_btnSettings, "Impostazioni (destinazioni di rete, analisi buchi)");
        r1.Controls.Add(_btnSettings, 6, 0);
        _btnTheme = Theme.MakeButton("◐", BtnKind.Neutral, 40); _btnTheme.Height = 34; _btnTheme.Font = new Font("Segoe UI Symbol", 12f);
        _btnTheme.Margin = new Padding(3, 3, 0, 3);
        _btnTheme.Click += (o, e) => CycleTheme();
        r1.Controls.Add(_btnTheme, 7, 0);
        root.Controls.Add(r1, 0, 0);

        // --- riga 2: trasporto
        var r2 = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Theme.Back, Margin = new Padding(0) };
        _btnRec = Theme.MakeButton("●  REGISTRA", BtnKind.Danger, 140);
        _btnPause = Theme.MakeButton("❚❚  PAUSA", BtnKind.Neutral, 120);
        _btnStop = Theme.MakeButton("■  STOP", BtnKind.Neutral, 110);
        _btnPlay = Theme.MakeButton("▶  ASCOLTA", BtnKind.Primary, 130);
        _btnRec.Click += (o, e) => DoRecord();
        _btnPause.Click += (o, e) => DoPause();
        _btnStop.Click += (o, e) => DoStop();
        _btnPlay.Click += (o, e) => TogglePlay();
        _lblTime = new Label { Tag = "keep", Text = "00:00", AutoSize = false, Width = 150, Height = 40, Font = new Font("Consolas", 20f, FontStyle.Bold), ForeColor = Theme.Text, TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(12, 2, 6, 0) };
        _lblStatus = new Label { Tag = "keep", Text = "", AutoSize = false, Width = 420, Height = 40, ForeColor = Theme.TextDim, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(6, 2, 6, 0) };
        _btnOpen = Theme.MakeButton("Apri file…", BtnKind.Neutral, 110);
        _btnNew = Theme.MakeButton("Nuova cassetta", BtnKind.Neutral, 140);
        _btnOpen.Click += (o, e) => DoOpenFile();
        _btnNew.Click += (o, e) => DoNew();
        r2.Controls.AddRange(new Control[] { _btnRec, _btnPause, _btnStop, Spacer(14), _btnPlay, _lblTime, _lblStatus, _btnOpen, _btnNew });
        root.Controls.Add(r2, 0, 1);
        r2.Resize += (o, e) =>
        {
            int used = r2.Controls.Cast<Control>().Where(c => c != _lblStatus).Sum(c => c.Width + c.Margin.Horizontal);
            _lblStatus.Width = Math.Max(120, r2.ClientSize.Width - used - _lblStatus.Margin.Horizontal - 4);
        };

        // --- riga 3: forma d'onda
        _wave = new WaveformView { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4) };
        _wave.EditsChanged += OnEditsChanged;
        _wave.SelectionChanged += OnSelectionChanged;
        _wave.CursorMoved += () => { if (_state == State.Stopped && !_player.IsPlaying) _lblTime.Text = Theme.FormatTime(Sec(_wave.CursorFrame), true); };
        _wave.SeekRequested += f => { if (_player.IsPlaying) PlayFrom(f); };
        _wave.GapClicked += SelectGapRow;
        new ToolTip { AutoPopDelay = 15000 }.SetToolTip(_wave,
            "Click = posiziona · Trascina = seleziona · Maiusc+click = estendi selezione\n" +
            "Rotella = zoom · Maiusc+rotella = scorri · Ctrl+rotella = zoom verticale · tasto centrale trascinato = scorri\n" +
            "Fascia arancione sul righello = buco da controllare (click per selezionarlo)\n" +
            "Doppio click = divisione brano · Tasto destro = menu");
        root.Controls.Add(_wave, 0, 2);

        // --- riga 4: modifica
        var r4 = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Theme.Back, Margin = new Padding(0) };
        _btnIn = SmallBtn("⟦ Inizio  [I]", 100, () => _wave.SetIn(CurrentPos()));
        _btnOut = SmallBtn("Fine ⟧  [O]", 96, () => _wave.SetOut(CurrentPos()));
        _btnAutoTrim = SmallBtn("Togli silenzio inizio/fine", 180, DoAutoTrim);
        _btnCutSel = SmallBtn("✂ Taglia selezione  [Canc]", 190, DoCutSelection);
        _btnUndoCut = SmallBtn("↶ Annulla taglio", 124, () => _wave.UndoCut());
        _btnAddMarker = SmallBtn("+ Divisione  [M]", 124, () => _wave.AddMarker(MarkerPos()));
        _btnDetect = SmallBtn("Dividi sulle pause", 140, DoSplitOnPauses);
        _btnClearMarkers = SmallBtn("Togli divisioni", 116, () => _wave.ClearMarkers());
        _btnZoomIn = SmallBtn("+", 34, () => _wave.ZoomAt(0.5, _wave.Width / 2));
        _btnZoomOut = SmallBtn("−", 34, () => _wave.ZoomAt(2, _wave.Width / 2));
        _btnFit = SmallBtn("Tutto", 60, () =>
        {
            if (_state == State.Recording) { _wave.LiveMode = true; _wave.TickLive(); }
            else _wave.ZoomToFit();
        });
        r4.Controls.AddRange(new Control[] { _btnIn, _btnOut, _btnAutoTrim, Spacer(12), _btnCutSel, _btnUndoCut, Spacer(12), _btnAddMarker, _btnDetect, _btnClearMarkers, Spacer(12), _btnZoomIn, _btnZoomOut, _btnFit });
        root.Controls.Add(r4, 0, 3);

        // --- riga 5: buchi + brani + esportazione
        var r5 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0), BackColor = Theme.Back };
        r5.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        r5.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        r5.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        root.Controls.Add(r5, 0, 4);

        r5.Controls.Add(BuildGapPanel(), 0, 0);

        _grid = BuildTrackGrid();
        var trackPanel = new Panel { Tag = "panel", Dock = DockStyle.Fill, Margin = new Padding(8, 0, 0, 0), BackColor = Theme.Panel };
        var trackHdr = new Label { Tag = "dim", Text = "Brani (doppio click sul titolo per scriverlo)", Dock = DockStyle.Top, Height = 26, ForeColor = Theme.TextDim, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0) };
        trackPanel.Controls.Add(_grid);
        trackPanel.Controls.Add(trackHdr);
        r5.Controls.Add(trackPanel, 1, 0);

        var exp = new TableLayoutPanel { Tag = "panel", Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 5, BackColor = Theme.Panel, Padding = new Padding(8, 6, 8, 6), Margin = new Padding(8, 0, 0, 0) };
        exp.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        exp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        exp.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        exp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int i = 0; i < 4; i++) exp.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        exp.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        r5.Controls.Add(exp, 2, 0);

        _cbFormat = Combo(); _cbFormat.Items.AddRange(ExportFormat.All);
        _cbFormat.SelectedItem = ExportFormat.All.FirstOrDefault(f => f.Key == _s.Format) ?? ExportFormat.All[0];
        _cbFormat.SelectedIndexChanged += (o, e) => { _cbQuality.Enabled = ((ExportFormat)_cbFormat.SelectedItem).Key == "mp3"; };
        _cbQuality = Combo();
        _cbQuality.Items.AddRange(new object[] { "128 kbps", "192 kbps", "256 kbps", "320 kbps", "VBR alta (V0)" });
        _cbQuality.SelectedIndex = _s.Mp3Quality switch { "128" => 0, "192" => 1, "256" => 2, "V0" => 4, _ => 3 };
        _cbQuality.Enabled = ((ExportFormat)_cbFormat.SelectedItem).Key == "mp3";
        exp.Controls.Add(Theme.MakeLabel("Formato:"), 0, 0); exp.Controls.Add(_cbFormat, 1, 0);
        exp.Controls.Add(Theme.MakeLabel("Qualità:"), 2, 0); exp.Controls.Add(_cbQuality, 3, 0);

        _cbDest = Combo();
        exp.Controls.Add(Theme.MakeLabel("Salva in:"), 0, 1); exp.Controls.Add(_cbDest, 1, 1);
        exp.SetColumnSpan(_cbDest, 3);

        _txtFolder = new TextBox { Dock = DockStyle.Fill, Text = _s.LastSubfolder, Margin = new Padding(3, 6, 3, 3), PlaceholderText = "es. Rossi Mario\\Cassetta 1 (vuoto = direttamente nella destinazione)" };
        Theme.StyleInput(_txtFolder);
        exp.Controls.Add(Theme.MakeLabel("Sottocartella:"), 0, 2); exp.Controls.Add(_txtFolder, 1, 2);
        exp.SetColumnSpan(_txtFolder, 3);

        _txtName = new TextBox { Dock = DockStyle.Fill, Text = _s.LastBaseName, Margin = new Padding(3, 6, 3, 3) };
        Theme.StyleInput(_txtName);
        _chkSplit = new CheckBox { Text = "Un file per brano", AutoSize = true, Checked = _s.SplitTracks, ForeColor = Theme.Text, Margin = new Padding(8, 8, 3, 3) };
        _chkSplit.CheckedChanged += (o, e) => RefreshTracks();
        exp.Controls.Add(Theme.MakeLabel("Nome/Album:"), 0, 3); exp.Controls.Add(_txtName, 1, 3);
        exp.Controls.Add(_chkSplit, 2, 3); exp.SetColumnSpan(_chkSplit, 2);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 4, 0, 0) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _btnExport = Theme.MakeButton("⬇  ESPORTA", BtnKind.Success, 150); _btnExport.Height = 40;
        _btnExport.Click += (o, e) => DoExport();
        _progress = new ProgressBar { Dock = DockStyle.Fill, Margin = new Padding(6, 12, 3, 12), Maximum = 1000 };
        _lblExport = new Label { Tag = "dim", Dock = DockStyle.Fill, ForeColor = Theme.TextDim, Text = "", AutoEllipsis = true };
        bottom.Controls.Add(_btnExport, 0, 0);
        bottom.Controls.Add(_progress, 1, 0);
        bottom.Controls.Add(_lblExport, 0, 1); bottom.SetColumnSpan(_lblExport, 2);
        exp.Controls.Add(bottom, 0, 4); exp.SetColumnSpan(bottom, 4);

        // --- riga 6: barra inferiore (versione, aggiornamenti, disco)
        var foot = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 2, 0, 0) };
        foot.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        foot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foot.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _lnkVersion = new LinkLabel
        {
            Tag = "keep", AutoSize = true, Margin = new Padding(0, 5, 12, 0), Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Text = $"Tape2MP3 v{AppInfo.Version}" + (AppInfo.IsDevBuild ? "  (build di sviluppo)" : ""), LinkBehavior = LinkBehavior.HoverUnderline
        };
        _lnkVersion.LinkClicked += (o, e) => ShowAbout();
        new ToolTip().SetToolTip(_lnkVersion, "Informazioni sul programma");
        _lnkUpdate = new LinkLabel { Tag = "keep", AutoSize = true, Margin = new Padding(0, 5, 0, 0), Font = new Font("Segoe UI", 8.5f), Text = "", LinkBehavior = LinkBehavior.HoverUnderline };
        _lnkUpdate.LinkClicked += (o, e) =>
        {
            if (_update?.IsNewer == true && _update.Url != null)
                try { Process.Start(new ProcessStartInfo(_update.Url) { UseShellExecute = true }); } catch { }
            else _ = CheckUpdatesAsync(true);
        };
        _lblFooter = new Label { Tag = "keep", AutoSize = true, Margin = new Padding(0, 5, 0, 0), Font = new Font("Segoe UI", 8.5f), ForeColor = Theme.TextDim };
        foot.Controls.Add(_lnkVersion, 0, 0);
        foot.Controls.Add(_lnkUpdate, 1, 0);
        foot.Controls.Add(_lblFooter, 2, 0);
        root.Controls.Add(foot, 0, 5);
        StyleLinks();

        new ToolTip().SetToolTip(_btnRec, "Registra / riprendi (anche accodare il lato B)");
        new ToolTip().SetToolTip(_btnPlay, "Ascolta dal cursore, o solo la selezione  [Spazio]");
        new ToolTip().SetToolTip(_btnOpen, "Apri un file audio o una registrazione (puoi anche trascinarlo sulla finestra)");

        RefreshDestinations();
    }

    private Control BuildGapPanel()
    {
        var p = new TableLayoutPanel { Tag = "panel", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Panel, Margin = new Padding(0), Padding = new Padding(0) };
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        p.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        var hdr = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        hdr.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        hdr.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _lblGaps = new Label { Tag = "keep", Text = "Buchi da controllare", Dock = DockStyle.Fill, ForeColor = Theme.Gap, Font = new Font("Segoe UI", 9f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0), AutoEllipsis = true };
        _btnAnalyze = Theme.MakeButton("Rianalizza", BtnKind.Neutral, 90); _btnAnalyze.Height = 26; _btnAnalyze.Font = new Font("Segoe UI", 8.5f); _btnAnalyze.Margin = new Padding(2);
        _btnAnalyze.Click += async (o, e) => await RunAnalysis();
        hdr.Controls.Add(_lblGaps, 0, 0);
        hdr.Controls.Add(_btnAnalyze, 1, 0);
        p.Controls.Add(hdr, 0, 0);

        _gapGrid = MakeGrid();
        _gapGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "n", HeaderText = "#", Width = 34, ReadOnly = true });
        _gapGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "pos", HeaderText = "Dove", Width = 66, ReadOnly = true });
        _gapGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "dur", HeaderText = "Durata", Width = 58, ReadOnly = true });
        _gapGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "kind", HeaderText = "Tipo", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _gapGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "lvl", HeaderText = "Livello", Width = 62, ReadOnly = true });
        foreach (DataGridViewColumn c in _gapGrid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        _gapGrid.SelectionChanged += (o, e) =>
        {
            if (_syncingGapGrid || _gapGrid.CurrentRow == null) return;
            int i = _gapGrid.CurrentRow.Index;
            if (i >= 0 && i < _wave.Gaps.Count && i != _wave.ActiveGap) { _wave.FocusGap(i); UpdateUi(); }
        };
        _gapGrid.CellDoubleClick += (o, e) => { if (e.RowIndex >= 0) ListenGap(e.RowIndex); };
        p.Controls.Add(_gapGrid, 0, 1);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), Padding = new Padding(2, 0, 0, 0) };
        _btnPrevGap = GapBtn("◀  [P]", 62, () => GoGap(-1));
        _btnNextGap = GapBtn("[N]  ▶", 62, () => GoGap(+1));
        var listen = GapBtn("▶ Senti", 66, () => { if (_wave.ActiveGap >= 0) ListenGap(_wave.ActiveGap); });
        _btnGapCut = GapBtn("✂ Taglia", 72, GapCut);
        _btnGapSplit = GapBtn("Dividi qui", 78, GapSplit);
        _btnGapIgnore = GapBtn("Va bene così", 96, GapIgnore);
        bar.Controls.AddRange(new Control[] { _btnPrevGap, _btnNextGap, listen, _btnGapCut, _btnGapSplit, _btnGapIgnore });
        p.Controls.Add(bar, 0, 2);
        return p;
    }

    private TdButton GapBtn(string text, int w, Action a)
    {
        var b = Theme.MakeButton(text, BtnKind.Neutral, w);
        b.Height = 30; b.Font = new Font("Segoe UI", 8.5f);
        b.Margin = new Padding(2, 4, 2, 2);
        b.Click += (o, e) => a();
        return b;
    }

    private DataGridView MakeGrid()
    {
        var g = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Theme.Panel,
            BorderStyle = BorderStyle.None,
            GridColor = Theme.Border,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 24,
            EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
            Margin = new Padding(0)
        };
        Theme.StyleGrid(g);
        g.RowTemplate.Height = 22;
        return g;
    }

    private DataGridView BuildTrackGrid()
    {
        var g = MakeGrid();
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "n", HeaderText = "#", Width = 34, ReadOnly = true });
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "start", HeaderText = "Inizio", Width = 62, ReadOnly = true });
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "dur", HeaderText = "Durata", Width = 58, ReadOnly = true });
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "title", HeaderText = "Titolo", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        foreach (DataGridViewColumn c in g.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;

        g.CellEndEdit += (o, e) =>
        {
            if (e.ColumnIndex != 3) return;
            while (_titles.Count <= e.RowIndex) _titles.Add("");
            _titles[e.RowIndex] = (g.Rows[e.RowIndex].Cells[3].Value as string ?? "").Trim();
        };
        g.CellClick += (o, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex == 3) return;
            var segs = BuildSegments(forceSplit: true);
            if (e.RowIndex < segs.Count) { _wave.SetCursor(segs[e.RowIndex].StartFrame); _wave.EnsureVisible(segs[e.RowIndex].StartFrame); _wave.Invalidate(); }
        };
        g.CellDoubleClick += (o, e) =>
        {
            if (e.RowIndex < 0) return;
            if (e.ColumnIndex == 3) { g.BeginEdit(true); return; }
            var segs = BuildSegments(forceSplit: true);
            if (e.RowIndex < segs.Count) PlayRange(segs[e.RowIndex].StartFrame, segs[e.RowIndex].EndFrame);
        };
        return g;
    }

    private ComboBox Combo()
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(3, 6, 3, 3) };
        Theme.StyleInput(c);
        return c;
    }

    private static Control Spacer(int w) => new Panel { Width = w, Height = 10, Margin = new Padding(0) };

    private TdButton SmallBtn(string text, int w, Action a)
    {
        var b = Theme.MakeButton(text, BtnKind.Neutral, w);
        b.Height = 32; b.Font = new Font("Segoe UI", 9f);
        b.Margin = new Padding(2, 6, 2, 2);
        b.Click += (o, e) => a();
        return b;
    }

    private void RefreshDestinations()
    {
        _cbDest.SelectedIndexChanged -= OnDestChanged;
        _cbDest.Items.Clear();
        foreach (var d in _s.Destinations) _cbDest.Items.Add(d);
        _cbDest.Items.Add("Scegli una cartella…");
        if (_s.Destinations.Count == 0)
        {
            var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            _s.Destinations.Add(new Destination { Name = "Musica (locale)", Path = music });
            _cbDest.Items.Insert(0, _s.Destinations[0]);
        }
        _cbDest.SelectedIndex = Math.Clamp(_s.LastDestination, 0, _s.Destinations.Count - 1);
        _cbDest.SelectedIndexChanged += OnDestChanged;
    }

    private void OnDestChanged(object sender, EventArgs e)
    {
        if (_cbDest.SelectedItem is string)
        {
            using var fb = new FolderBrowserDialog { UseDescriptionForTitle = true, Description = "Cartella di destinazione" };
            if (fb.ShowDialog(this) == DialogResult.OK)
            {
                var d = new Destination { Name = Path.GetFileName(fb.SelectedPath.TrimEnd('\\')), Path = fb.SelectedPath };
                _s.Destinations.Add(d);
                _s.LastDestination = _s.Destinations.Count - 1;
                _s.Save();
            }
            RefreshDestinations();
            return;
        }
        _s.LastDestination = _cbDest.SelectedIndex;
    }

    // =====================================================================================
    // Dispositivi
    // =====================================================================================

    private void LoadDevices()
    {
        if (_state == State.Recording || _state == State.Paused) return;
        List<InputDevice> list;
        try { list = CaptureEngine.ListDevices(); }
        catch (Exception ex) { SetStatus("Impossibile elencare i dispositivi audio: " + ex.Message, Tone.Error); return; }

        _cbDevice.Items.Clear();
        foreach (var d in list) _cbDevice.Items.Add(d);

        int idx = list.FindIndex(d => d.Id == _s.DeviceId);
        if (idx < 0) idx = list.FindIndex(d => d.Name.Contains("USB", StringComparison.OrdinalIgnoreCase));
        if (idx < 0) { var def = CaptureEngine.DefaultDeviceId(); idx = list.FindIndex(d => d.Id == def); }
        if (idx < 0 && list.Count > 0) idx = 0;

        if (list.Count == 0)
        {
            _capture.Close();
            SetStatus("Nessun dispositivo di registrazione trovato. Collega il lettore USB e premi ⟳.", Tone.Warn);
            return;
        }
        _cbDevice.SelectedIndex = idx; // scatena OpenSelectedDevice
    }

    private void OpenSelectedDevice()
    {
        if (_cbDevice.SelectedItem is not InputDevice d) return;
        if (_state == State.Recording || _state == State.Paused) return;
        try
        {
            _capture.Open(d.Id);
            _capture.SetMonitor(_chkMonitor.Checked);
            _s.DeviceId = d.Id;
            _meter.Reset();
            SetStatus($"Ingresso pronto: {d.Name} ({_capture.SampleRate} Hz). Regola il volume e premi REGISTRA.", Tone.Dim);
        }
        catch (Exception ex)
        {
            SetStatus($"Impossibile aprire \"{d.Name}\": {ex.Message}", Tone.Error);
        }
        UpdateUi();
    }

    // =====================================================================================
    // Registrazione
    // =====================================================================================

    private void DoRecord()
    {
        if (!_capture.IsOpen) { SetStatus("Nessun ingresso aperto: scegli il dispositivo.", Tone.Warn); return; }
        StopPlayback();

        if (_state == State.Paused)
        {
            ApplyCaptureSettings();
            _capture.StartRecording(_writer, _peaks);
            _state = State.Recording;
            _wave.LiveMode = true;
            SetStatus("Registrazione ripresa.", Tone.Rec);
            UpdateUi();
            return;
        }

        if (_state == State.Stopped)
        {
            var r = AskAppendOrNew();
            if (r == DialogResult.Cancel) return;
            if (r == DialogResult.Yes)
            {
                if (_takeRate != _capture.SampleRate)
                {
                    MessageBox.Show(this, $"La registrazione è a {_takeRate} Hz ma l'ingresso ora è a {_capture.SampleRate} Hz: non posso accodare. Esporta questa e inizia una nuova cassetta.", "Tape2MP3", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                try { _writer = WavWriter.Append(_wavPath); }
                catch (Exception ex) { MessageBox.Show(this, "Impossibile accodare: " + ex.Message, "Tape2MP3", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                ApplyCaptureSettings();
                _capture.StartRecording(_writer, _peaks);
                _state = State.Recording;
                _exported = false;
                _wave.LiveMode = true;
                SetStatus("Registrazione accodata (lato B).", Tone.Rec);
                UpdateUi();
                return;
            }
            if (!DiscardCurrent(askIfNotExported: true)) return;
        }

        // nuova registrazione: controllo spazio (circa 11 MB al minuto a 48 kHz)
        long free = FreeSpace(AppSettings.WorkDir);
        if (free >= 0 && free < 1L << 30)
        {
            if (MessageBox.Show(this, $"Sul disco della cartella di lavoro restano solo {Theme.FormatBytes(free)} (bastano per circa {free / (_capture.SampleRate * 4L * 60):0} minuti).\nRegistrare lo stesso?",
                    "Spazio su disco", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        }
        try
        {
            Directory.CreateDirectory(AppSettings.WorkDir);
            _wavPath = Path.Combine(AppSettings.WorkDir, $"Registrazione_{DateTime.Now:yyyyMMdd_HHmmss}.wav");
            _takeRate = _capture.SampleRate;
            _writer = WavWriter.Create(_wavPath, _takeRate);
            _peaks = new PeakData(_takeRate);
            _titles.Clear();
            _exported = false;
            _analysis = null;
            _wave.SetData(_peaks, _wavPath);
            _wave.ResetEdits();
            _wave.LiveMode = true;
            ApplyCaptureSettings();
            _capture.StartRecording(_writer, _peaks);
            _state = State.Recording;
            SetStatus("In registrazione… Fai partire la cassetta. A fine lato: PAUSA, gira la cassetta, RIPRENDI.", Tone.Rec);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Impossibile iniziare la registrazione: " + ex.Message, "Tape2MP3", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _state = State.Empty;
        }
        RefreshGaps();
        UpdateUi();
    }

    private void ApplyCaptureSettings()
    {
        _capture.AutoPauseEnabled = _s.AutoPause;
        _capture.AutoPauseSeconds = _s.AutoPauseSec;
        _capture.SilenceThresholdDb = _s.SilenceDb;
    }

    private DialogResult AskAppendOrNew()
    {
        using var f = new Form
        {
            Text = "Registra", StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, BackColor = Theme.Back, ForeColor = Theme.Text, Font = Font,
            AutoScaleDimensions = new SizeF(96f, 96f), AutoScaleMode = AutoScaleMode.Dpi
        };
        f.SuspendLayout();
        f.ClientSize = new Size(480, 150);
        f.Controls.Add(new Label
        {
            Text = "C'è già una registrazione aperta.\nVuoi continuare accodando (es. il lato B) oppure iniziare una nuova cassetta?",
            Location = new Point(16, 16), Size = new Size(450, 60)
        });
        var a = Theme.MakeButton("Accoda (lato B)", BtnKind.Danger, 150); a.DialogResult = DialogResult.Yes; a.Location = new Point(16, 94);
        var n = Theme.MakeButton("Nuova cassetta", BtnKind.Neutral, 150); n.DialogResult = DialogResult.No; n.Location = new Point(172, 94);
        var c = Theme.MakeButton("Annulla", BtnKind.Neutral, 130); c.DialogResult = DialogResult.Cancel; c.Location = new Point(334, 94);
        f.Controls.AddRange(new Control[] { a, n, c });
        f.AcceptButton = a; f.CancelButton = c;
        f.ResumeLayout(false);
        f.HandleCreated += (o, e) => Theme.SetDarkTitleBar(f);
        return f.ShowDialog(this);
    }

    private void DoPause()
    {
        if (_state != State.Recording) return;
        _capture.StopRecording(); // il writer resta aperto
        _state = State.Paused;
        _wave.LiveMode = false;
        _wave.ZoomToFit();
        SetStatus("In pausa. Gira la cassetta e premi RIPRENDI per continuare, oppure STOP per finire.", Tone.Warn);
        UpdateUi();
    }

    private void OnAutoPaused()
    {
        if (_state != State.Recording) return;
        DoPause();
        SetStatus($"Pausa automatica: {_s.AutoPauseSec:0} s di silenzio (fine lato?). Gira la cassetta e premi RIPRENDI, oppure STOP.", Tone.Warn);
        SystemSounds.Exclamation.Play();
        FlashWindow();
    }

    private async void DoStop()
    {
        if (_state != State.Recording && _state != State.Paused) return;
        _capture.StopRecording();
        _writer?.Dispose();
        _writer = null;
        _state = State.Stopped;
        _wave.LiveMode = false;
        _wave.SetWavPath(_wavPath);
        _wave.ZoomToFit();
        _wave.SetCursor(0, false);
        RefreshTracks();
        UpdateUi();
        await RunAnalysis();
    }

    private bool DiscardCurrent(bool askIfNotExported)
    {
        if (_wavPath == null) return true;
        if (askIfNotExported && !_exported && _peaks != null && _peaks.Frames > 0)
        {
            var r = MessageBox.Show(this, "La registrazione corrente non è stata esportata.\nVuoi scartarla?", "Tape2MP3", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) return false;
        }
        StopPlayback();
        _writer?.Dispose(); _writer = null;
        try { File.Delete(_wavPath); } catch { }
        _wavPath = null;
        _peaks = null;
        _analysis = null;
        _titles.Clear();
        _wave.SetData(null, null);
        _wave.ResetEdits();
        _state = State.Empty;
        _lblTime.Text = "00:00";
        RefreshTracks();
        RefreshGaps();
        return true;
    }

    private void DoNew()
    {
        if (_state == State.Recording || _state == State.Paused)
        {
            if (MessageBox.Show(this, "Sei in registrazione. Fermare e scartare?", "Tape2MP3", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            _capture.StopRecording();
            _writer?.Dispose(); _writer = null;
            _exported = true; // già confermato
        }
        if (DiscardCurrent(askIfNotExported: true))
        {
            _txtName.Text = "Cassetta";
            SetStatus("Pronto per una nuova cassetta.", Tone.Dim);
        }
        UpdateUi();
    }

    // =====================================================================================
    // Apri file / recupero
    // =====================================================================================

    private async void DoOpenFile()
    {
        if (_state == State.Recording || _state == State.Paused) return;
        using var ofd = new OpenFileDialog
        {
            Title = "Apri registrazione o file audio",
            Filter = "Audio|*.wav;*.mp3;*.flac;*.m4a;*.aac;*.ogg;*.wma;*.aiff;*.aif|Tutti i file|*.*",
            InitialDirectory = AppSettings.WorkDir
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        if (!DiscardCurrentKeepIfSame(ofd.FileName)) return;
        await OpenAudio(ofd.FileName);
    }

    private bool DiscardCurrentKeepIfSame(string newPath)
    {
        if (_wavPath != null && string.Equals(Path.GetFullPath(newPath), Path.GetFullPath(_wavPath), StringComparison.OrdinalIgnoreCase))
        {
            // riapre la stessa: non cancellare
            StopPlayback();
            _wavPath = null; _peaks = null; _state = State.Empty;
            return true;
        }
        return DiscardCurrent(askIfNotExported: true);
    }

    private async Task OpenAudio(string file)
    {
        _state = State.Busy;
        UpdateUi();
        _cts = new CancellationTokenSource();
        bool ok = false;
        try
        {
            string wav = file;
            bool internalOk = false;
            if (Path.GetExtension(file).Equals(".wav", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var info = WavFile.ReadInfo(file);
                    internalOk = WavFile.IsInternalFormat(info);
                } catch { }
            }

            bool inWorkDir = Path.GetFullPath(file).StartsWith(Path.GetFullPath(AppSettings.WorkDir), StringComparison.OrdinalIgnoreCase);
            if (!internalOk || !inWorkDir)
            {
                // copia/conversione nella cartella di lavoro: l'originale non viene mai toccato
                var ff = Exporter.FindFfmpeg();
                if (ff == null) throw new Exception("ffmpeg.exe non trovato accanto al programma.");
                wav = Path.Combine(AppSettings.WorkDir, $"Import_{DateTime.Now:yyyyMMdd_HHmmss}.wav");
                SetStatus("Conversione del file in corso…", Tone.Dim);
                await Exporter.ConvertToInternalAsync(ff, file, wav, _cts.Token);
            }
            else
            {
                WavFile.RepairHeader(wav);
            }

            SetStatus("Lettura forma d'onda…", Tone.Dim);
            var prog = new Progress<double>(p => _progress.Value = (int)(Math.Clamp(p, 0, 1) * 1000));
            var peaks = await Task.Run(() => PeakData.FromFile(wav, prog, _cts.Token));
            _progress.Value = 0;

            _wavPath = wav;
            _peaks = peaks;
            _takeRate = peaks.SampleRate;
            _titles.Clear();
            _exported = false;
            _analysis = null;
            _wave.SetData(_peaks, _wavPath);
            _wave.ResetEdits();
            _wave.ZoomToFit();
            _state = State.Stopped;
            _txtName.Text = Path.GetFileNameWithoutExtension(file).StartsWith("Registrazione_") ? "Cassetta" : Path.GetFileNameWithoutExtension(file);
            SetStatus($"Aperto: {Path.GetFileName(file)} ({Theme.FormatTime(Sec(peaks.Frames))}).", Tone.Ok);
            RefreshTracks();
            ok = true;
        }
        catch (Exception ex)
        {
            _state = _wavPath != null ? State.Stopped : State.Empty;
            SetStatus("Impossibile aprire il file: " + ex.Message, Tone.Error);
        }
        finally
        {
            _progress.Value = 0;
            _cts = null;
            UpdateUi();
        }
        if (ok) await RunAnalysis();
    }

    private async void CheckRecovery()
    {
        try
        {
            var files = new DirectoryInfo(AppSettings.WorkDir).GetFiles("*.wav")
                .Where(f => f.Length > 44 + 192000) // almeno ~1 s
                .OrderByDescending(f => f.LastWriteTime).ToList();
            if (files.Count == 0) return;
            var last = files[0];
            var r = MessageBox.Show(this,
                $"È rimasta una registrazione non esportata:\n{last.Name}  ({last.LastWriteTime:dd/MM/yyyy HH:mm}, {last.Length / 1048576.0:0} MB)\n\nVuoi riaprirla?" +
                (files.Count > 1 ? $"\n\n(Ce ne sono altre {files.Count - 1}: le trovi con \"Apri file…\")" : ""),
                "Recupero registrazione", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r == DialogResult.Yes) await OpenAudio(last.FullName);
        }
        catch { }
    }

    // =====================================================================================
    // Analisi buchi
    // =====================================================================================

    private async Task RunAnalysis()
    {
        if (_state != State.Stopped || _wavPath == null) return;
        StopPlayback();
        _state = State.Busy; UpdateUi();
        _cts = new CancellationTokenSource();
        try
        {
            SetStatus("Analizzo la registrazione: misuro il fruscio e cerco i buchi…", Tone.Dim);
            var prog = new Progress<double>(p => _progress.Value = (int)(Math.Clamp(p, 0, 1) * 1000));
            string path = _wavPath;
            var a = await Task.Run(() => GapAnalyzer.Analyze(path, _s.GapMarginDb, _s.MinGapSec, _s.MinSilenceSec, _s.DetectDrops, prog, _cts.Token));
            _analysis = a;
            // non riproporre buchi già tagliati del tutto
            var gaps = a.Gaps.Where(g => !_wave.Cuts.Any(c => c.Start <= g.Start && c.End >= g.End)).ToList();
            _wave.SetGaps(gaps);
            RefreshGaps();
            int n = gaps.Count;
            SetStatus(n == 0
                ? $"Registrazione pulita: nessun buco trovato (fruscio a {a.NoiseFloorDb:0} dB). Puoi esportare."
                : $"Trovati {n} punti da controllare (arancione sul righello). Usa N / P per spostarti, poi Taglia, Dividi o \"Va bene così\".",
                n == 0 ? Tone.Ok : Tone.Gap);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { SetStatus("Errore nell'analisi: " + ex.Message, Tone.Error); }
        finally
        {
            _progress.Value = 0;
            _cts = null;
            if (_state == State.Busy) _state = State.Stopped;
            UpdateUi();
        }
    }

    private void RefreshGaps()
    {
        _syncingGapGrid = true;
        try
        {
            _gapGrid.Rows.Clear();
            var gaps = _wave.Gaps;
            for (int i = 0; i < gaps.Count; i++)
            {
                var g = gaps[i];
                int r = _gapGrid.Rows.Add((i + 1).ToString(), Theme.FormatTime(Sec(g.Start)), $"{Sec(g.Length):0.0} s", g.KindLabel, $"{g.LevelDb:0} dB");
                _gapGrid.Rows[r].DefaultCellStyle.ForeColor = g.Kind == GapKind.Calo ? Theme.GapDrop :
                    g.Kind == GapKind.Pausa ? Theme.Text : Theme.Gap;
            }
            if (_wave.ActiveGap >= 0 && _wave.ActiveGap < _gapGrid.Rows.Count)
                _gapGrid.CurrentCell = _gapGrid.Rows[_wave.ActiveGap].Cells[0];
            else _gapGrid.ClearSelection();
        }
        finally { _syncingGapGrid = false; }

        _lblGaps.ForeColor = Theme.Gap;
        if (_analysis == null) _lblGaps.Text = _peaks == null ? "Buchi da controllare" : "Buchi da controllare (analisi dopo lo STOP)";
        else _lblGaps.Text = $"Da controllare: {_wave.Gaps.Count}   ·   fruscio {_analysis.NoiseFloorDb:0} dB, musica {_analysis.MusicDb:0} dB";
    }

    private void SelectGapRow(int i)
    {
        _syncingGapGrid = true;
        try
        {
            if (i >= 0 && i < _gapGrid.Rows.Count)
            {
                _gapGrid.CurrentCell = _gapGrid.Rows[i].Cells[0];
                _gapGrid.FirstDisplayedScrollingRowIndex = Math.Max(0, i - 2);
            }
        }
        catch { }
        finally { _syncingGapGrid = false; }
        UpdateUi();
    }

    private void GoGap(int dir)
    {
        var gaps = _wave.Gaps;
        if (gaps.Count == 0 || _state != State.Stopped) return;
        int i;
        if (_wave.ActiveGap >= 0) i = _wave.ActiveGap + dir;
        else
        {
            long pos = _wave.CursorFrame;
            i = dir > 0 ? gaps.ToList().FindIndex(g => g.Start > pos) : gaps.ToList().FindLastIndex(g => g.Start < pos);
            if (i < 0) i = dir > 0 ? 0 : gaps.Count - 1;
        }
        if (i < 0 || i >= gaps.Count) { SystemSounds.Beep.Play(); return; }
        _wave.FocusGap(i);
        SelectGapRow(i);
    }

    /// <summary>Ascolta il buco con 3 secondi di contesto prima e dopo.</summary>
    private void ListenGap(int i)
    {
        if (i < 0 || i >= _wave.Gaps.Count) return;
        var g = _wave.Gaps[i];
        if (i != _wave.ActiveGap) { _wave.FocusGap(i); SelectGapRow(i); }
        long ctx = 3L * _takeRate;
        PlayRange(Math.Max(0, g.Start - ctx), Math.Min(_wave.TotalFrames, g.End + ctx));
    }

    /// <summary>Taglia la selezione corrente (se l'hai ritoccata vale quella, altrimenti il buco intero).</summary>
    private void GapCut()
    {
        int i = _wave.ActiveGap;
        if (i < 0) return;
        var g = _wave.Gaps[i];
        if (!_wave.HasSelection || _wave.SelEnd <= g.Start || _wave.SelStart >= g.End) _wave.SetSelection(g.Start, g.End);
        StopPlayback();
        _wave.CutSelection();
        // se il buco non era coperto per intero resta in lista: lo tolgo comunque, l'hai gestito tu
        int still = _wave.Gaps.ToList().FindIndex(x => x.Start == g.Start && x.End == g.End);
        if (still >= 0) _wave.RemoveGap(still);
        AfterGapAction(i);
    }

    private void GapSplit()
    {
        int i = _wave.ActiveGap;
        if (i < 0) return;
        var g = _wave.Gaps[i];
        _wave.AddMarker(g.Start + g.Length / 2);
        _wave.RemoveGap(i);
        _chkSplit.Checked = true;
        AfterGapAction(i);
    }

    private void GapIgnore()
    {
        int i = _wave.ActiveGap;
        if (i < 0) return;
        _wave.RemoveGap(i);
        AfterGapAction(i);
    }

    private void AfterGapAction(int oldIndex)
    {
        _wave.ClearSelection();
        RefreshGaps();
        if (_wave.Gaps.Count == 0)
        {
            SetStatus("Tutti i punti sono stati controllati. Puoi esportare.", Tone.Ok);
            UpdateUi();
            return;
        }
        int next = Math.Min(oldIndex, _wave.Gaps.Count - 1);
        _wave.FocusGap(next);
        SelectGapRow(next);
    }

    // =====================================================================================
    // Ascolto
    // =====================================================================================

    private void TogglePlay()
    {
        if (_player.IsPlaying) { StopPlayback(); return; }
        if (_state != State.Stopped) return;
        if (_wave.HasSelection) { PlayRange(_wave.SelStart, _wave.SelEnd); return; }
        long from = _wave.CursorFrame;
        if (from < _wave.InFrame || from >= _wave.EffectiveOut - _takeRate / 10) from = _wave.InFrame;
        PlayFrom(from);
    }

    private void PlayFrom(long from) => PlayRange(from, _wave.EffectiveOut);

    private void PlayRange(long from, long to)
    {
        if (_state != State.Stopped || _wavPath == null) return;
        try
        {
            _player.Play(_wavPath, from, to, _wave.Cuts);
            _wave.SetCursor(from, false);
            UpdateUi();
        }
        catch (Exception ex) { SetStatus("Errore di riproduzione: " + ex.Message, Tone.Error); }
    }

    private void StopPlayback()
    {
        if (!_player.IsPlaying && _wave.PlayFrame < 0) return;
        long pos = _player.CurrentFrame;
        _player.Stop();
        if (pos >= 0 && !_wave.HasSelection) _wave.SetCursor(pos, false);
        _wave.PlayFrame = -1;
        _wave.Invalidate();
        UpdateUi();
    }

    // =====================================================================================
    // Modifica
    // =====================================================================================

    private async void DoAutoTrim()
    {
        if (_state != State.Stopped) return;
        // se l'analisi c'è già basta guardare il primo e l'ultimo buco
        if (_analysis != null)
        {
            long a = 0, b = _wave.TotalFrames;
            var first = _analysis.Gaps.FirstOrDefault(g => g.Kind == GapKind.Inizio);
            var last = _analysis.Gaps.LastOrDefault(g => g.Kind == GapKind.Fine);
            long margin = _takeRate / 2;
            if (first != null) a = Math.Max(0, first.End - margin);
            if (last != null) b = Math.Min(_wave.TotalFrames, last.Start + margin);
            _wave.SetInOut(a, b);
            SetStatus($"INIZIO/FINE impostati: {Theme.FormatTime(Sec(a))} → {Theme.FormatTime(Sec(b))}. Trascina le bandierine per ritoccare.", Tone.Ok);
            return;
        }
        _state = State.Busy; UpdateUi();
        try
        {
            SetStatus("Cerco l'inizio e la fine della musica…", Tone.Dim);
            var (s, e) = await Task.Run(() => SilenceDetector.DetectContent(_wavPath, _s.SilenceDb));
            _wave.SetInOut(s, e);
            SetStatus($"INIZIO/FINE impostati: {Theme.FormatTime(Sec(s))} → {Theme.FormatTime(Sec(e))}. Trascina le bandierine per ritoccare.", Tone.Ok);
        }
        catch (Exception ex) { SetStatus("Errore: " + ex.Message, Tone.Error); }
        finally { _state = State.Stopped; UpdateUi(); }
    }

    private void DoCutSelection()
    {
        if (_state != State.Stopped || !_wave.HasSelection) return;
        StopPlayback();
        long len = _wave.SelEnd - _wave.SelStart;
        _wave.CutSelection();
        RefreshGaps();
        SetStatus($"Tagliati {Sec(len):0.0} s. Il file originale non viene toccato: \"Annulla taglio\" o tasto destro → Ripristina.", Tone.Ok);
    }

    /// <summary>Mette le divisioni al centro delle pause fra brani trovate dall'analisi.</summary>
    private void DoSplitOnPauses()
    {
        if (_state != State.Stopped) return;
        if (_analysis == null) { SetStatus("Prima serve l'analisi: premi \"Rianalizza\".", Tone.Warn); return; }
        long a = _wave.InFrame, b = _wave.EffectiveOut;
        long minTrack = (long)(_s.MinTrackSec * _takeRate);
        var cuts = new List<long>();
        long last = a;
        foreach (var g in _analysis.Gaps.Where(g => g.Kind == GapKind.Pausa && g.Start > a && g.End < b))
        {
            long m = g.Start + g.Length / 2;
            if (m - last < minTrack || b - m < minTrack) continue;
            cuts.Add(m);
            last = m;
        }
        _wave.SetMarkers(cuts);
        _chkSplit.Checked = true;
        SetStatus(cuts.Count == 0
            ? "Nessuna pausa fra brani abbastanza lunga. Aggiungi le divisioni a mano (M o doppio click)."
            : $"{cuts.Count + 1} brani. Controlla le divisioni gialle e trascinale se serve.", cuts.Count == 0 ? Tone.Warn : Tone.Ok);
    }

    private void OnEditsChanged()
    {
        if (_exported) _dirtyAfterExport = true;
        RefreshTracks();
        UpdateUi();
    }

    private void OnSelectionChanged()
    {
        if (_wave.HasSelection && _state == State.Stopped)
            _lblTime.Text = Theme.FormatTime(Sec(_wave.SelEnd - _wave.SelStart), true);
        UpdateUi();
    }

    /// <summary>Segmenti da esportare fra INIZIO e FINE, divisi dai marker.</summary>
    private List<ExportSegment> BuildSegments(bool forceSplit = false)
    {
        var list = new List<ExportSegment>();
        if (_peaks == null || _wave.TotalFrames == 0) return list;
        long a = _wave.InFrame, b = _wave.EffectiveOut;
        var cuts = (forceSplit || _chkSplit.Checked)
            ? _wave.Markers.Where(m => m > a && m < b).OrderBy(m => m).ToList()
            : new List<long>();
        var bounds = new List<long> { a };
        bounds.AddRange(cuts);
        bounds.Add(b);
        for (int i = 0; i < bounds.Count - 1; i++)
        {
            if (CutList.KeptLength(bounds[i], bounds[i + 1], _wave.Cuts) < _takeRate / 10) continue;
            list.Add(new ExportSegment
            {
                StartFrame = bounds[i],
                EndFrame = bounds[i + 1],
                TrackNo = list.Count + 1,
                Title = list.Count < _titles.Count ? _titles[list.Count] : ""
            });
        }
        return list;
    }

    private void RefreshTracks()
    {
        if (_grid.IsCurrentCellInEditMode) _grid.EndEdit();
        var segs = BuildSegments(forceSplit: true);
        _grid.Rows.Clear();
        foreach (var s in segs)
            _grid.Rows.Add(s.TrackNo.ToString("00"), Theme.FormatTime(Sec(s.StartFrame)),
                Theme.FormatTime(Sec(CutList.KeptLength(s.StartFrame, s.EndFrame, _wave.Cuts))), s.Title);
        if (!_chkSplit.Checked && segs.Count > 1)
            foreach (DataGridViewRow r in _grid.Rows) r.DefaultCellStyle.ForeColor = Theme.TextDim;
        UpdateInfo();
    }

    private void UpdateInfo()
    {
        if (_state != State.Stopped || _peaks == null) return;
        if (_exporting) return;
        var segs = BuildSegments();
        long len = CutList.KeptLength(_wave.InFrame, _wave.EffectiveOut, _wave.Cuts);
        long cut = _wave.Cuts.Sum(c => c.Length);
        _lblExport.Text = $"Totale {Theme.FormatTime(Sec(_peaks.Frames))} · da esportare {Theme.FormatTime(Sec(len))}" +
                          (cut > 0 ? $" ({_wave.Cuts.Count} tagli)" : "") + " · " +
                          (segs.Count > 1 ? $"{segs.Count} file" : "1 file unico");
    }

    // =====================================================================================
    // Esportazione
    // =====================================================================================

    private async void DoExport()
    {
        if (_exporting) { _cts?.Cancel(); return; }
        if (_state != State.Stopped || _wavPath == null) return;
        StopPlayback();
        if (_grid.IsCurrentCellInEditMode) _grid.EndEdit();

        var ff = Exporter.FindFfmpeg();
        if (ff == null)
        {
            MessageBox.Show(this, "ffmpeg.exe non trovato. Deve stare nella stessa cartella di Tape2MP3.exe.", "Tape2MP3", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (_cbDest.SelectedItem is not Destination dest) return;

        if (_wave.Gaps.Count > 0)
        {
            var r = MessageBox.Show(this, $"Ci sono ancora {_wave.Gaps.Count} punti da controllare nella lista.\nEsporto lo stesso?",
                "Tape2MP3", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) return;
        }

        var fmt = (ExportFormat)_cbFormat.SelectedItem;
        string quality = _cbQuality.SelectedIndex switch { 0 => "128", 1 => "192", 2 => "256", 4 => "V0", _ => "320" };
        string baseName = string.IsNullOrWhiteSpace(_txtName.Text) ? "Cassetta" : _txtName.Text.Trim();

        string sub = string.Join("\\", (_txtFolder.Text ?? "").Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).Select(Exporter.SafeName));
        string folder = string.IsNullOrEmpty(sub) ? dest.Path : Path.Combine(dest.Path, sub);

        var segs = BuildSegments();
        if (segs.Count == 0) return;
        var cutsCopy = _wave.Cuts.ToList();

        _s.Format = fmt.Key; _s.Mp3Quality = quality; _s.SplitTracks = _chkSplit.Checked;
        _s.LastSubfolder = _txtFolder.Text; _s.LastBaseName = baseName; _s.LastDestination = _cbDest.SelectedIndex;
        _s.Save();

        _state = State.Busy;
        _exporting = true;
        _cts = new CancellationTokenSource();
        _btnExport.Text = "✖  ANNULLA";
        UpdateUi();
        try
        {
            _lblExport.Text = "Controllo la destinazione…";
            await Task.Run(() => NetShare.EnsureReady(dest, folder));

            var prog = new Progress<(int idx, int total, double frac)>(p =>
            {
                _progress.Value = (int)Math.Clamp((p.idx + p.frac) / p.total * 1000, 0, 1000);
                _lblExport.Text = $"Esporto {p.idx + 1} di {p.total} → {folder}";
            });
            var files = await Exporter.ExportAsync(ff, _wavPath, _takeRate, segs, cutsCopy, folder, baseName, fmt, quality, baseName, prog, _cts.Token);
            _exported = true;
            _dirtyAfterExport = false;
            _lblExport.Text = $"✔ Esportati {files.Count} file in {folder}";
            SetStatus($"Esportazione completata: {files.Count} file.", Tone.Ok);
            SystemSounds.Asterisk.Play();
            if (_s.OpenFolderAfterExport)
                try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true }); } catch { }
        }
        catch (OperationCanceledException)
        {
            _lblExport.Text = "Esportazione annullata.";
        }
        catch (Exception ex)
        {
            _lblExport.Text = "Errore di esportazione.";
            MessageBox.Show(this, ex.Message, "Esportazione non riuscita", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _progress.Value = 0;
            _cts = null;
            _exporting = false;
            _state = State.Stopped;
            _btnExport.Text = "⬇  ESPORTA";
            UpdateUi();
        }
    }

    // =====================================================================================
    // Impostazioni, timer, stato
    // =====================================================================================

    private void OpenSettings()
    {
        using var f = new SettingsForm(_s);
        if (f.ShowDialog(this) == DialogResult.OK)
        {
            _s.Save();
            RefreshDestinations();
            ApplyCaptureSettings();
            var newMode = Enum.TryParse<ThemeMode>(_s.Theme, out var tm2) ? tm2 : ThemeMode.Automatico;
            if (newMode != Theme.Mode) Theme.Set(newMode);
            if (_state == State.Stopped && _analysis != null)
                SetStatus("Impostazioni salvate. Premi \"Rianalizza\" per applicarle ai buchi.", Tone.Dim);
        }
    }

    private void OnTick(object sender, EventArgs e)
    {
        if (++_tickCount % 60 == 0) UpdateFooter(); // ogni ~2 s
        if (_capture.IsOpen)
        {
            _capture.ReadLevels(out float l, out float r, out bool clip);
            _meter.Push(l, r, clip);
            UpdateSignalHint(clip);
        }

        if (_state == State.Recording || _state == State.Paused)
        {
            long fr = _writer?.Frames ?? 0;
            _lblTime.Text = Theme.FormatTime(Sec(fr));
            if (_state == State.Recording)
            {
                _lblTime.ForeColor = DateTime.Now.Millisecond < 500 ? Theme.Rec : Theme.Text;
                _wave.TickLive();
            }
        }
        else _lblTime.ForeColor = Theme.Text;

        if (_player.IsPlaying)
        {
            long pf = _player.CurrentFrame;
            if (pf >= 0)
            {
                _wave.PlayFrame = pf;
                _wave.EnsureVisible(pf);
                _wave.Invalidate();
                _lblTime.Text = Theme.FormatTime(Sec(pf), true);
            }
        }
    }

    private void UpdateSignalHint(bool clip)
    {
        float db = _meter.CurrentMaxDb;
        if (clip) { _lblSignal.Text = "TROPPO ALTO"; _lblSignal.ForeColor = Theme.Rec; _lowSince = DateTime.MaxValue; return; }
        if (db <= -59) { _lblSignal.Text = "nessun segnale"; _lblSignal.ForeColor = Theme.TextDim; return; }
        if (db < -24)
        {
            if (_lowSince == DateTime.MaxValue) _lowSince = DateTime.Now;
            if ((DateTime.Now - _lowSince).TotalSeconds > 2) { _lblSignal.Text = "livello basso"; _lblSignal.ForeColor = Theme.Warn; }
            return;
        }
        _lowSince = DateTime.MaxValue;
        _lblSignal.Text = "livello OK";
        _lblSignal.ForeColor = Theme.Ok;
    }

    private void UpdateUi()
    {
        bool rec = _state == State.Recording, paused = _state == State.Paused, stopped = _state == State.Stopped, busy = _state == State.Busy;
        bool playing = _player.IsPlaying;

        _cbDevice.Enabled = !rec && !paused && !busy;
        _btnRefresh.Enabled = _cbDevice.Enabled;
        _btnRec.Enabled = _capture.IsOpen && !rec && !busy;
        _btnRec.Text = paused ? "●  RIPRENDI" : stopped ? "●  REGISTRA +" : "●  REGISTRA";
        _btnPause.Enabled = rec;
        _btnStop.Enabled = rec || paused;
        _btnPlay.Enabled = stopped;
        _btnPlay.Text = playing ? "■  FERMA" : (_wave.HasSelection && stopped ? "▶  SELEZIONE" : "▶  ASCOLTA");
        _btnOpen.Enabled = !rec && !paused && !busy;
        _btnNew.Enabled = !busy && _state != State.Empty;
        _btnSettings.Enabled = !busy;

        foreach (var b in new[] { _btnIn, _btnOut, _btnAutoTrim, _btnAddMarker, _btnClearMarkers })
            b.Enabled = stopped;
        _btnCutSel.Enabled = stopped && _wave.HasSelection;
        _btnUndoCut.Enabled = stopped && _wave.CanUndoCut;
        _btnDetect.Enabled = stopped && _analysis != null;
        _btnZoomIn.Enabled = _btnZoomOut.Enabled = _peaks != null && !rec;
        _btnFit.Enabled = _peaks != null;

        bool hasGaps = stopped && _wave.Gaps.Count > 0;
        bool gapSel = hasGaps && _wave.ActiveGap >= 0;
        _btnAnalyze.Enabled = stopped;
        _btnPrevGap.Enabled = _btnNextGap.Enabled = hasGaps;
        _btnGapCut.Enabled = _btnGapSplit.Enabled = _btnGapIgnore.Enabled = gapSel;
        _gapGrid.Enabled = stopped;

        _btnExport.Enabled = stopped || _exporting;
        _grid.Enabled = stopped;
        UpdateInfo();
    }

    private void SetStatus(string text, Tone tone)
    {
        _statusTone = tone;
        _lblStatus.Text = text;
        _lblStatus.ForeColor = Theme.ToneColor(tone);
    }

    private double Sec(long frames) => (double)frames / Math.Max(1, _takeRate > 0 ? _takeRate : (_capture.SampleRate > 0 ? _capture.SampleRate : 48000));

    // =====================================================================================
    // Tema, versione, barra inferiore
    // =====================================================================================

    private void CycleTheme()
    {
        var next = Theme.Next(Theme.Mode);
        _s.Theme = next.ToString();
        _s.Save();
        Theme.Set(next);
        SetStatus($"Tema: {next}" + (next == ThemeMode.Automatico ? " (segue Windows)" : ""), Tone.Dim);
    }

    private void OnSystemPrefChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category != Microsoft.Win32.UserPreferenceCategory.General || Theme.Mode != ThemeMode.Automatico) return;
        if (IsHandleCreated) BeginInvoke(new Action(() => Theme.Set(ThemeMode.Automatico)));
    }

    private void OnThemeChanged()
    {
        SuspendLayout();
        Theme.ApplyTo(this);
        _lblStatus.ForeColor = Theme.ToneColor(_statusTone);
        _lblGaps.ForeColor = Theme.Gap;
        StyleLinks();
        RefreshGaps();
        RefreshTracks();
        UpdateFooter();
        ResumeLayout(true);
        Invalidate(true);
    }

    private void StyleLinks()
    {
        foreach (var l in new[] { _lnkVersion, _lnkUpdate })
        {
            l.LinkColor = l == _lnkUpdate && _update?.IsNewer == true ? Theme.Ok : Theme.Accent;
            l.ActiveLinkColor = Theme.Rec;
            l.VisitedLinkColor = l.LinkColor;
        }
        _themeTip ??= new ToolTip();
        _themeTip.SetToolTip(_btnTheme, $"Tema: {Theme.Mode} — click per cambiare (Automatico → Chiaro → Scuro)");
    }
    private ToolTip _themeTip;

    private async Task CheckUpdatesAsync(bool manual)
    {
        _lnkUpdate.Text = "controllo aggiornamenti…";
        var u = await AppInfo.CheckAsync(string.IsNullOrWhiteSpace(_s.UpdateRepo) ? AppInfo.DefaultRepo : _s.UpdateRepo);
        _update = u;
        if (u == null) _lnkUpdate.Text = manual ? "impossibile controllare gli aggiornamenti (offline o repo privato) — riprova" : "controlla aggiornamenti";
        else if (u.IsNewer)
        {
            _lnkUpdate.Text = $"⬆ Disponibile la versione {u.LatestVersion} — clicca per scaricarla";
            if (manual) SystemSounds.Asterisk.Play();
        }
        else _lnkUpdate.Text = $"✔ è l'ultima versione ({u.LatestVersion})";
        StyleLinks();
    }

    private void ShowAbout()
    {
        using var f = new AboutForm(_s, _update);
        f.ShowDialog(this);
        if (f.UpdateResult != null) { _update = f.UpdateResult; _ = CheckUpdatesAsync(false); }
    }

    private static long FreeSpace(string dir)
    {
        try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir))!).AvailableFreeSpace; }
        catch { return -1; }
    }

    private void UpdateFooter()
    {
        var parts = new List<string>();
        if (_capture.IsOpen) parts.Add($"{_capture.SampleRate / 1000.0:0.#} kHz · 16 bit stereo");
        if (_wavPath != null && File.Exists(_wavPath))
            try { parts.Add("registrazione " + Theme.FormatBytes(new FileInfo(_wavPath).Length)); } catch { }
        long free = FreeSpace(AppSettings.WorkDir);
        if (free >= 0)
        {
            int rate = _capture.SampleRate > 0 ? _capture.SampleRate : 48000;
            double hours = free / (rate * 4.0 * 3600);
            parts.Add($"disco: {Theme.FormatBytes(free)} liberi (~{(hours >= 10 ? hours.ToString("0") : hours.ToString("0.0"))} h di registrazione)");
            _lblFooter.ForeColor = free < 2L << 30 ? Theme.Warn : Theme.TextDim;
        }
        _lblFooter.Text = string.Join("   ·   ", parts);
    }

    // =====================================================================================
    // Tastiera
    // =====================================================================================

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        bool typing = ActiveControl is TextBox || ActiveControl is ComboBox || ActiveControl is NumericUpDown || _grid.IsCurrentCellInEditMode ||
                      (ActiveControl == _grid && _grid.CurrentCell?.ColumnIndex == 3 && keyData != Keys.Space);
        if (!typing && _state == State.Stopped)
        {
            switch (keyData)
            {
                case Keys.Space: TogglePlay(); return true;
                case Keys.I: _wave.SetIn(CurrentPos()); return true;
                case Keys.O: _wave.SetOut(CurrentPos()); return true;
                case Keys.M: _wave.AddMarker(MarkerPos()); return true;
                case Keys.N: GoGap(+1); return true;
                case Keys.P: GoGap(-1); return true;
                case Keys.Delete:
                case Keys.Back:
                    DoCutSelection(); return true;
                case Keys.Control | Keys.Z: _wave.UndoCut(); return true;
                case Keys.Escape: _wave.ClearSelection(); return true;
                case Keys.Home:
                    _wave.SetCursor(_wave.InFrame); _wave.EnsureVisible(_wave.InFrame);
                    if (_player.IsPlaying) PlayFrom(_wave.InFrame);
                    return true;
                case Keys.End:
                    _wave.SetCursor(_wave.EffectiveOut); _wave.EnsureVisible(_wave.EffectiveOut); return true;
            }
        }
        if (!typing)
        {
            switch (keyData)
            {
                case Keys.Oemplus:
                case Keys.Add:
                    if (_peaks != null) { _wave.ZoomAt(0.5, _wave.Width / 2); return true; }
                    break;
                case Keys.OemMinus:
                case Keys.Subtract:
                    if (_peaks != null) { _wave.ZoomAt(2, _wave.Width / 2); return true; }
                    break;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>Durante l'ascolto I/O/M agiscono sul punto che si sta sentendo.</summary>
    private long CurrentPos()
    {
        if (_player.IsPlaying) { long p = _player.CurrentFrame; if (p >= 0) return p; }
        return _wave.CursorFrame;
    }

    /// <summary>Divisione: a metà selezione se c'è, altrimenti sul punto corrente.</summary>
    private long MarkerPos() => _wave.HasSelection && !_player.IsPlaying
        ? _wave.SelStart + (_wave.SelEnd - _wave.SelStart) / 2
        : CurrentPos();

    // =====================================================================================
    // Chiusura
    // =====================================================================================

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_state == State.Recording || _state == State.Paused)
        {
            if (MessageBox.Show(this, "Sei in registrazione. Fermare e chiudere?\n(La registrazione resta recuperabile alla prossima apertura.)", "Tape2MP3",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            { e.Cancel = true; return; }
            _capture.StopRecording();
            _writer?.Dispose(); _writer = null;
        }
        else if (_state == State.Busy && _cts != null)
        {
            _cts.Cancel();
        }
        else if (_wavPath != null && (!_exported || _dirtyAfterExport))
        {
            var r = MessageBox.Show(this, "La registrazione non è stata esportata (o è cambiata dopo l'esportazione).\nChiudere comunque? Resterà recuperabile alla prossima apertura.", "Tape2MP3",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) { e.Cancel = true; return; }
        }
        else if (_wavPath != null && _exported)
        {
            // già esportata: la copia di lavoro non serve più
            _player.Stop();
            try { File.Delete(_wavPath); } catch { }
        }

        _s.WinMax = WindowState == FormWindowState.Maximized;
        if (WindowState == FormWindowState.Normal)
        {
            _s.WinX = Left; _s.WinY = Top; _s.WinW = Width; _s.WinH = Height;
        }
        _s.LastSubfolder = _txtFolder.Text;
        _s.LastBaseName = _txtName.Text;
        _s.SplitTracks = _chkSplit.Checked;
        if (_cbFormat.SelectedItem is ExportFormat f) _s.Format = f.Key;
        _s.Save();

        Theme.Changed -= OnThemeChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnSystemPrefChanged;
        _timer.Stop();
        _player.Dispose();
        _capture.Dispose();
        base.OnFormClosing(e);
    }

    // lampeggio in barra delle applicazioni per la pausa automatica
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }
    private void FlashWindow()
    {
        var fi = new FLASHWINFO { hwnd = Handle, dwFlags = 3 | 12, uCount = uint.MaxValue, dwTimeout = 0 };
        fi.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(fi);
        FlashWindowEx(ref fi);
    }
}
