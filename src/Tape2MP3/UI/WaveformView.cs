using Tape2MP3.Audio;

namespace Tape2MP3.UI;

/// <summary>
/// Forma d'onda stereo con righello, zoom, cursore, selezione, punti di INIZIO/FINE,
/// divisioni fra i brani, buchi segnalati dall'analisi e zone tagliate (non distruttive).
///  - click: sposta il cursore          - trascina: seleziona          - tasto centrale trascinato: scorre
///  - rotella: zoom                     - Maiusc+rotella: scorre        - Ctrl+rotella: zoom verticale
///  - trascina bandierine/divisioni/bordi selezione                     - doppio click: aggiunge divisione
///  - click sulla fascia arancione del righello: seleziona quel buco    - tasto destro: menu
/// </summary>
public sealed class WaveformView : Control
{
    private const int RulerH = 26;
    private const int GapBarH = 6;
    private const int HitPx = 6;

    private readonly HScrollBar _scroll;
    private readonly ContextMenuStrip _menu;
    private long _menuFrame;
    private int _menuMarkerIdx = -1;
    private int _menuCutIdx = -1;
    private ToolStripMenuItem _miCut, _miRestore, _miRemoveMarker, _miSelInOut, _miSplitSel;

    private PeakData _peaks;
    private string _wavPath;
    private double _spp = 1000;      // frame per pixel
    private double _viewStart;       // primo frame visibile
    private float _vZoom = 1f;

    private readonly List<long> _markers = new();
    private List<Gap> _gaps = new();
    private List<CutRange> _cuts = new();
    private readonly Stack<List<CutRange>> _undo = new();

    // drag
    private enum DragKind { None, Pan, Select, SelStartEdge, SelEndEdge, In, Out, Marker }
    private DragKind _drag;
    private int _dragMarker = -1;
    private int _dragStartX;
    private long _dragStartFrame;
    private double _dragStartView;
    private bool _dragMoved;

    // cache lettura campioni per zoom ravvicinato
    private short[] _rawCache;
    private long _rawCacheStart = -1;
    private int _rawCacheFrames;

    public bool LiveMode { get; set; }
    public double LiveWindowSec { get; set; } = 30;

    public long CursorFrame { get; private set; }
    public long PlayFrame { get; set; } = -1;
    public long InFrame { get; private set; }
    public long OutFrame { get; private set; } = -1; // -1 = fine registrazione
    public long SelStart { get; private set; } = -1;
    public long SelEnd { get; private set; } = -1;
    public bool HasSelection => SelStart >= 0 && SelEnd > SelStart;
    public int ActiveGap { get; private set; } = -1;

    public IReadOnlyList<long> Markers => _markers;
    public IReadOnlyList<Gap> Gaps => _gaps;
    public IReadOnlyList<CutRange> Cuts => _cuts;
    public bool CanUndoCut => _undo.Count > 0;
    public long TotalFrames => _peaks?.Frames ?? 0;
    public int SampleRate => _peaks?.SampleRate ?? 48000;
    public long EffectiveOut => OutFrame < 0 || OutFrame > TotalFrames ? TotalFrames : OutFrame;

    public event Action CursorMoved;
    public event Action EditsChanged;
    public event Action SelectionChanged;
    public event Action<int> GapClicked;
    /// <summary>Richiesta di riproduzione dal punto cliccato (click mentre si ascolta).</summary>
    public event Action<long> SeekRequested;

    public WaveformView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Theme.Back;
        TabStop = true;

        _scroll = new HScrollBar { Dock = DockStyle.Bottom, Height = 16, SmallChange = 20, LargeChange = 200 };
        _scroll.Scroll += (s, e) => { _viewStart = e.NewValue * _spp; LiveMode = false; Invalidate(); };
        Controls.Add(_scroll);

        _menu = new ContextMenuStrip();
        _miCut = new ToolStripMenuItem("✂  Taglia la selezione  (Canc)", null, (s, e) => CutSelection());
        _miSplitSel = new ToolStripMenuItem("Dividi brano a metà della selezione", null, (s, e) => { if (HasSelection) AddMarker(SelStart + (SelEnd - SelStart) / 2); });
        _miSelInOut = new ToolStripMenuItem("Usa la selezione come INIZIO/FINE", null, (s, e) => { if (HasSelection) SetInOut(SelStart, SelEnd); });
        _miRestore = new ToolStripMenuItem("↶  Ripristina questo taglio", null, (s, e) => { if (_menuCutIdx >= 0) RestoreCut(_menuCutIdx); });
        _menu.Items.Add(_miCut);
        _menu.Items.Add(_miSplitSel);
        _menu.Items.Add(_miSelInOut);
        _menu.Items.Add(_miRestore);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Imposta INIZIO qui", null, (s, e) => SetIn(_menuFrame));
        _menu.Items.Add("Imposta FINE qui", null, (s, e) => SetOut(_menuFrame));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Aggiungi divisione brano qui", null, (s, e) => AddMarker(_menuFrame));
        _miRemoveMarker = new ToolStripMenuItem("Rimuovi questa divisione", null, (s, e) => { if (_menuMarkerIdx >= 0) RemoveMarkerAt(_menuMarkerIdx); });
        _menu.Items.Add(_miRemoveMarker);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Adatta alla finestra", null, (s, e) => ZoomToFit());
        _menu.Opening += (s, e) =>
        {
            _miCut.Enabled = HasSelection;
            _miSplitSel.Enabled = HasSelection;
            _miSelInOut.Enabled = HasSelection;
            _miRestore.Visible = _menuCutIdx >= 0;
            _miRemoveMarker.Enabled = _menuMarkerIdx >= 0;
        };
    }

    private int WaveTop => RulerH;
    private int WaveHeight => Math.Max(10, ClientSize.Height - RulerH - _scroll.Height);

    // ---------------------------------------------------------------- dati

    public void SetData(PeakData peaks, string wavPath)
    {
        _peaks = peaks;
        _wavPath = wavPath;
        _rawCacheStart = -1;
        Invalidate();
    }

    public void ResetEdits()
    {
        _markers.Clear();
        _gaps = new List<Gap>();
        _cuts = new List<CutRange>();
        _undo.Clear();
        ActiveGap = -1;
        InFrame = 0;
        OutFrame = -1;
        CursorFrame = 0;
        PlayFrame = -1;
        SelStart = SelEnd = -1;
        EditsChanged?.Invoke();
        SelectionChanged?.Invoke();
        Invalidate();
    }

    public void SetWavPath(string p) { _wavPath = p; _rawCacheStart = -1; }

    public void SetIn(long f)
    {
        f = Math.Clamp(f, 0, TotalFrames);
        if (f >= EffectiveOut) f = Math.Max(0, EffectiveOut - SampleRate);
        InFrame = f;
        EditsChanged?.Invoke();
        Invalidate();
    }

    public void SetOut(long f)
    {
        f = Math.Clamp(f, 0, TotalFrames);
        if (f <= InFrame) f = Math.Min(TotalFrames, InFrame + SampleRate);
        OutFrame = f >= TotalFrames ? -1 : f;
        EditsChanged?.Invoke();
        Invalidate();
    }

    public void SetInOut(long inF, long outF)
    {
        InFrame = Math.Clamp(inF, 0, TotalFrames);
        OutFrame = outF >= TotalFrames ? -1 : Math.Max(InFrame + 1, outF);
        EditsChanged?.Invoke();
        Invalidate();
    }

    public void AddMarker(long f)
    {
        if (f <= 0 || f >= TotalFrames) return;
        long minGap = SampleRate; // almeno 1 s dalle altre divisioni
        if (_markers.Any(m => Math.Abs(m - f) < minGap)) return;
        _markers.Add(f);
        _markers.Sort();
        EditsChanged?.Invoke();
        Invalidate();
    }

    public void SetMarkers(IEnumerable<long> list)
    {
        _markers.Clear();
        _markers.AddRange(list.Where(m => m > 0 && m < TotalFrames).Distinct().OrderBy(x => x));
        EditsChanged?.Invoke();
        Invalidate();
    }

    public void RemoveMarkerAt(int idx)
    {
        if (idx < 0 || idx >= _markers.Count) return;
        _markers.RemoveAt(idx);
        EditsChanged?.Invoke();
        Invalidate();
    }

    public void ClearMarkers()
    {
        _markers.Clear();
        EditsChanged?.Invoke();
        Invalidate();
    }

    public int MarkerNear(long frame, double toleranceFrames)
    {
        int best = -1; double bd = toleranceFrames;
        for (int i = 0; i < _markers.Count; i++)
        {
            double d = Math.Abs(_markers[i] - frame);
            if (d <= bd) { bd = d; best = i; }
        }
        return best;
    }

    public void SetCursor(long f, bool raise = true)
    {
        CursorFrame = Math.Clamp(f, 0, TotalFrames);
        if (raise) CursorMoved?.Invoke();
        Invalidate();
    }

    // --- selezione

    public void SetSelection(long a, long b)
    {
        if (b < a) (a, b) = (b, a);
        SelStart = Math.Clamp(a, 0, TotalFrames);
        SelEnd = Math.Clamp(b, 0, TotalFrames);
        if (SelEnd <= SelStart) SelStart = SelEnd = -1;
        SelectionChanged?.Invoke();
        Invalidate();
    }

    public void ClearSelection()
    {
        if (SelStart < 0) return;
        SelStart = SelEnd = -1;
        SelectionChanged?.Invoke();
        Invalidate();
    }

    // --- buchi

    public void SetGaps(List<Gap> gaps)
    {
        _gaps = gaps ?? new List<Gap>();
        ActiveGap = -1;
        Invalidate();
    }

    public void RemoveGap(int i)
    {
        if (i < 0 || i >= _gaps.Count) return;
        _gaps.RemoveAt(i);
        if (ActiveGap == i) ActiveGap = -1;
        else if (ActiveGap > i) ActiveGap--;
        Invalidate();
    }

    /// <summary>Evidenzia il buco, lo seleziona e lo porta al centro della vista con un po' di contesto.</summary>
    public void FocusGap(int i)
    {
        if (i < 0 || i >= _gaps.Count) return;
        ActiveGap = i;
        var g = _gaps[i];
        SetSelection(g.Start, g.End);
        CursorFrame = g.Start;

        // zoom: il buco + 4 s di contesto per lato, se la vista attuale è troppo larga o troppo stretta
        double ctx = 4.0 * SampleRate;
        double want = g.Length + 2 * ctx;
        double wpx = Math.Max(100, ClientSize.Width);
        double curW = wpx * _spp;
        if (curW > want * 6 || curW < want)
        {
            LiveMode = false;
            _spp = Math.Max(1, want / wpx);
        }
        _viewStart = g.Start + g.Length / 2.0 - wpx * _spp / 2;
        ClampView();
        UpdateScroll();
        CursorMoved?.Invoke();
        Invalidate();
    }

    public int GapAt(long frame)
    {
        for (int i = 0; i < _gaps.Count; i++) if (frame >= _gaps[i].Start && frame < _gaps[i].End) return i;
        return -1;
    }

    // --- tagli (non distruttivi)

    public bool CutSelection()
    {
        if (!HasSelection) return false;
        _undo.Push(new List<CutRange>(_cuts));
        _cuts.Add(new CutRange(SelStart, SelEnd));
        _cuts = CutList.Normalize(_cuts);
        // i buchi completamente tagliati spariscono dalla lista
        for (int i = _gaps.Count - 1; i >= 0; i--)
            if (_gaps[i].Start >= SelStart && _gaps[i].End <= SelEnd) RemoveGap(i);
        CursorFrame = SelEnd;
        SelStart = SelEnd = -1;
        SelectionChanged?.Invoke();
        EditsChanged?.Invoke();
        Invalidate();
        return true;
    }

    public void UndoCut()
    {
        if (_undo.Count == 0) return;
        _cuts = _undo.Pop();
        EditsChanged?.Invoke();
        Invalidate();
    }

    public void RestoreCut(int idx)
    {
        if (idx < 0 || idx >= _cuts.Count) return;
        _undo.Push(new List<CutRange>(_cuts));
        _cuts.RemoveAt(idx);
        EditsChanged?.Invoke();
        Invalidate();
    }

    private int CutAt(long frame)
    {
        for (int i = 0; i < _cuts.Count; i++) if (_cuts[i].Contains(frame)) return i;
        return -1;
    }

    // ---------------------------------------------------------------- vista

    public void ZoomToFit()
    {
        LiveMode = false;
        long tot = Math.Max(TotalFrames, SampleRate);
        _spp = Math.Max(1, (double)tot / Math.Max(100, ClientSize.Width - 2));
        _viewStart = 0;
        UpdateScroll();
        Invalidate();
    }

    public void ZoomAt(double factor, int x)
    {
        LiveMode = false;
        double frameAtX = _viewStart + x * _spp;
        double maxSpp = Math.Max(1, (double)Math.Max(TotalFrames, SampleRate) / Math.Max(100, ClientSize.Width - 2));
        _spp = Math.Clamp(_spp * factor, 1, maxSpp * 1.0001);
        _viewStart = frameAtX - x * _spp;
        ClampView();
        UpdateScroll();
        Invalidate();
    }

    public void EnsureVisible(long frame)
    {
        double w = ClientSize.Width * _spp;
        if (frame < _viewStart || frame > _viewStart + w)
        {
            _viewStart = frame - w * 0.1;
            ClampView();
            UpdateScroll();
        }
        else if (frame > _viewStart + w * 0.95)
        {
            // pagina avanti durante la riproduzione
            _viewStart = frame - w * 0.05;
            ClampView();
            UpdateScroll();
        }
    }

    private void ClampView()
    {
        double w = ClientSize.Width * _spp;
        double max = Math.Max(0, TotalFrames - w);
        _viewStart = Math.Clamp(_viewStart, 0, max);
    }

    private void UpdateScroll()
    {
        int w = Math.Max(1, ClientSize.Width);
        long totalPx = (long)Math.Ceiling(TotalFrames / _spp);
        if (totalPx <= w || LiveMode)
        {
            _scroll.Enabled = false;
            _scroll.Value = 0;
            return;
        }
        _scroll.Enabled = true;
        _scroll.Minimum = 0;
        _scroll.LargeChange = w;
        _scroll.SmallChange = Math.Max(1, w / 20);
        _scroll.Maximum = (int)Math.Min(int.MaxValue - 1, totalPx);
        int v = (int)Math.Clamp(_viewStart / _spp, 0, Math.Max(0, _scroll.Maximum - w + 1));
        _scroll.Value = v;
    }

    /// <summary>In registrazione: segue la coda. Chiamare dal timer UI.</summary>
    public void TickLive()
    {
        if (!LiveMode) return;
        long tot = TotalFrames;
        double win = LiveWindowSec * SampleRate;
        _spp = Math.Max(1, win / Math.Max(100, ClientSize.Width));
        _viewStart = Math.Max(0, tot - win * 0.97);
        UpdateScroll();
        Invalidate();
    }

    private double XToFrame(int x) => _viewStart + x * _spp;
    private float FrameToX(double f) => (float)((f - _viewStart) / _spp);

    // ---------------------------------------------------------------- input

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        base.OnMouseDown(e);
        if (_peaks == null || TotalFrames == 0) return;
        long frame = (long)Math.Clamp(XToFrame(e.X), 0, TotalFrames);

        if (e.Button == MouseButtons.Right)
        {
            _menuFrame = frame;
            _menuMarkerIdx = MarkerNear(frame, HitPx * _spp);
            _menuCutIdx = CutAt(frame);
            _menu.Show(this, e.Location);
            return;
        }

        _dragMoved = false;
        _dragStartX = e.X;
        _dragStartFrame = frame;
        _dragStartView = _viewStart;

        if (e.Button == MouseButtons.Middle) { _drag = DragKind.Pan; Cursor = Cursors.Hand; return; }
        if (e.Button != MouseButtons.Left) return;

        // click sulla fascia dei buchi nel righello
        if (e.Y < RulerH)
        {
            int gi = GapAt(frame);
            if (gi >= 0) { FocusGap(gi); GapClicked?.Invoke(gi); _drag = DragKind.None; return; }
        }

        if (Math.Abs(FrameToX(InFrame) - e.X) <= HitPx) { _drag = DragKind.In; return; }
        if (Math.Abs(FrameToX(EffectiveOut) - e.X) <= HitPx) { _drag = DragKind.Out; return; }
        int mi = MarkerNear(frame, HitPx * _spp);
        if (mi >= 0) { _drag = DragKind.Marker; _dragMarker = mi; return; }
        if (HasSelection && Math.Abs(FrameToX(SelStart) - e.X) <= HitPx) { _drag = DragKind.SelStartEdge; return; }
        if (HasSelection && Math.Abs(FrameToX(SelEnd) - e.X) <= HitPx) { _drag = DragKind.SelEndEdge; return; }

        if ((ModifierKeys & Keys.Shift) != 0 && HasSelection)
        {
            // Maiusc+click: estende la selezione
            if (frame < SelStart) SetSelection(frame, SelEnd); else SetSelection(SelStart, frame);
            _drag = DragKind.None;
            return;
        }
        _drag = DragKind.Select;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_peaks == null) return;
        long frame = (long)Math.Clamp(XToFrame(e.X), 0, TotalFrames);

        switch (_drag)
        {
            case DragKind.None:
                bool onHandle = Math.Abs(FrameToX(InFrame) - e.X) <= HitPx ||
                                Math.Abs(FrameToX(EffectiveOut) - e.X) <= HitPx ||
                                MarkerNear(frame, HitPx * _spp) >= 0 ||
                                (HasSelection && (Math.Abs(FrameToX(SelStart) - e.X) <= HitPx || Math.Abs(FrameToX(SelEnd) - e.X) <= HitPx));
                Cursor = onHandle ? Cursors.SizeWE : (e.Y < RulerH && GapAt(frame) >= 0 ? Cursors.Hand : Cursors.IBeam);
                break;
            case DragKind.Pan:
                if (Math.Abs(e.X - _dragStartX) > 2) _dragMoved = true;
                LiveMode = false;
                _viewStart = _dragStartView - (e.X - _dragStartX) * _spp;
                ClampView(); UpdateScroll(); Invalidate();
                break;
            case DragKind.Select:
                if (Math.Abs(e.X - _dragStartX) > 3) _dragMoved = true;
                if (_dragMoved)
                {
                    long a = Math.Min(_dragStartFrame, frame), b = Math.Max(_dragStartFrame, frame);
                    SelStart = a; SelEnd = b;
                    AutoScrollEdge(e.X);
                    Invalidate();
                }
                break;
            case DragKind.SelStartEdge:
                _dragMoved = true;
                SelStart = Math.Min(frame, SelEnd - 1);
                Invalidate();
                break;
            case DragKind.SelEndEdge:
                _dragMoved = true;
                SelEnd = Math.Max(frame, SelStart + 1);
                Invalidate();
                break;
            case DragKind.In:
                _dragMoved = true;
                InFrame = Math.Clamp(frame, 0, Math.Max(0, EffectiveOut - SampleRate / 10));
                Invalidate();
                break;
            case DragKind.Out:
                _dragMoved = true;
                long o = Math.Clamp(frame, InFrame + SampleRate / 10, TotalFrames);
                OutFrame = o >= TotalFrames ? -1 : o;
                Invalidate();
                break;
            case DragKind.Marker:
                _dragMoved = true;
                if (_dragMarker >= 0 && _dragMarker < _markers.Count)
                {
                    _markers[_dragMarker] = Math.Clamp(frame, 1, TotalFrames - 1);
                    Invalidate();
                }
                break;
        }
    }

    private void AutoScrollEdge(int x)
    {
        int w = ClientSize.Width;
        if (x > w - 10) { _viewStart += w * _spp / 30; ClampView(); UpdateScroll(); }
        else if (x < 10) { _viewStart -= w * _spp / 30; ClampView(); UpdateScroll(); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var kind = _drag;
        _drag = DragKind.None;
        if (_peaks == null) return;

        switch (kind)
        {
            case DragKind.Select:
                if (!_dragMoved)
                {
                    long f = (long)Math.Clamp(XToFrame(e.X), 0, TotalFrames);
                    ClearSelection();
                    SetCursor(f);
                    SeekRequested?.Invoke(f);
                }
                else
                {
                    CursorFrame = SelStart;
                    SelectionChanged?.Invoke();
                    CursorMoved?.Invoke();
                }
                break;
            case DragKind.SelStartEdge:
            case DragKind.SelEndEdge:
                SelectionChanged?.Invoke();
                break;
            case DragKind.Marker:
                _markers.Sort();
                _dragMarker = -1;
                EditsChanged?.Invoke();
                Invalidate();
                break;
            case DragKind.In:
            case DragKind.Out:
                EditsChanged?.Invoke();
                break;
        }
        Cursor = Cursors.IBeam;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button != MouseButtons.Left || _peaks == null || e.Y < RulerH) return;
        long f = (long)Math.Clamp(XToFrame(e.X), 0, TotalFrames);
        if (MarkerNear(f, HitPx * _spp) < 0) AddMarker(f);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_peaks == null) return;
        if ((ModifierKeys & Keys.Control) != 0)
        {
            _vZoom = Math.Clamp(e.Delta > 0 ? _vZoom * 2 : _vZoom / 2, 1f, 16f);
            Invalidate();
        }
        else if ((ModifierKeys & Keys.Shift) != 0)
        {
            LiveMode = false;
            _viewStart -= Math.Sign(e.Delta) * ClientSize.Width * _spp / 8;
            ClampView(); UpdateScroll(); Invalidate();
        }
        else
        {
            ZoomAt(e.Delta > 0 ? 0.7 : 1 / 0.7, e.X);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ClampView();
        UpdateScroll();
    }

    // ---------------------------------------------------------------- disegno

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        int w = ClientSize.Width;
        int top = WaveTop, h = WaveHeight;
        int laneH = h / 2;

        using (var rb = new SolidBrush(Theme.Panel)) g.FillRectangle(rb, 0, 0, w, RulerH);

        if (_peaks == null || TotalFrames == 0)
        {
            using var f = new Font("Segoe UI", 12f);
            using var b = new SolidBrush(Theme.TextDim);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("Scegli l'ingresso, controlla il livello e premi  ●  REGISTRA", f, b, new RectangleF(0, top, w, h), sf);
            return;
        }

        DrawRuler(g, w);

        // corsie
        using (var lane = new SolidBrush(Color.FromArgb(22, 22, 26)))
        {
            g.FillRectangle(lane, 0, top, w, laneH - 1);
            g.FillRectangle(lane, 0, top + laneH, w, laneH - 1);
        }
        using (var sep = new Pen(Theme.Border)) g.DrawLine(sep, 0, top + laneH - 1, w, top + laneH - 1);

        // guide -6 dB e centro
        using (var guide = new Pen(Color.FromArgb(40, 40, 48)))
        {
            for (int ch = 0; ch < 2; ch++)
            {
                int cy = top + ch * laneH + laneH / 2;
                float g6 = (laneH / 2f - 2) * 0.5f * _vZoom;
                g.DrawLine(guide, 0, cy, w, cy);
                if (g6 < laneH / 2f) { g.DrawLine(guide, 0, cy - g6, w, cy - g6); g.DrawLine(guide, 0, cy + g6, w, cy + g6); }
            }
        }

        // buchi segnalati (sotto la forma d'onda)
        DrawGaps(g, w, top, h);

        DrawWave(g, w, top, laneH);

        // zone tagliate
        DrawCuts(g, w, top, h);

        // zone escluse (prima dell'inizio e dopo la fine)
        using (var shade = new SolidBrush(Color.FromArgb(150, 10, 10, 12)))
        {
            float xin = FrameToX(InFrame);
            if (xin > 0) g.FillRectangle(shade, 0, top, Math.Min(w, xin), h);
            float xout = FrameToX(EffectiveOut);
            if (xout < w) g.FillRectangle(shade, Math.Max(0, xout), top, w - Math.Max(0, xout), h);
        }

        // selezione
        if (HasSelection)
        {
            float x1 = FrameToX(SelStart), x2 = FrameToX(SelEnd);
            if (x2 >= 0 && x1 <= w)
            {
                float a = Math.Max(0, x1), b = Math.Min(w, x2);
                using var sb = new SolidBrush(Color.FromArgb(55, 120, 170, 255));
                g.FillRectangle(sb, a, top, Math.Max(1, b - a), h);
                using var sp = new Pen(Color.FromArgb(200, 140, 190, 255));
                g.DrawLine(sp, x1, top, x1, top + h);
                g.DrawLine(sp, x2, top, x2, top + h);
            }
        }

        using var small = new Font("Segoe UI", 8f, FontStyle.Bold);

        // divisioni brani
        using (var mp = new Pen(Theme.Marker, 1.5f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
        using (var mb = new SolidBrush(Theme.Marker))
        {
            int trackNo = 1;
            for (int i = 0; i < _markers.Count; i++)
            {
                long m = _markers[i];
                bool inside = m > InFrame && m < EffectiveOut;
                if (inside) trackNo++;
                float x = FrameToX(m);
                if (x < -20 || x > w + 20) continue;
                g.DrawLine(mp, x, top, x, top + h);
                var tri = new[] { new PointF(x - 6, RulerH - 12), new PointF(x + 6, RulerH - 12), new PointF(x, RulerH) };
                g.FillPolygon(mb, tri);
                if (inside) g.DrawString(trackNo.ToString(), small, mb, x + 3, top + 2);
            }
            if (_markers.Count > 0)
            {
                float x1 = FrameToX(InFrame);
                if (x1 >= -10 && x1 < w) g.DrawString("1", small, mb, Math.Max(0, x1) + 3, top + 2);
            }
        }

        // INIZIO / FINE
        DrawFlag(g, FrameToX(InFrame), top, h, Theme.InColor, "INIZIO", true, small);
        DrawFlag(g, FrameToX(EffectiveOut), top, h, Theme.OutColor, "FINE", false, small);

        // cursore
        float cx = FrameToX(CursorFrame);
        if (cx >= 0 && cx <= w) using (var cp = new Pen(Color.FromArgb(220, 255, 255, 255))) g.DrawLine(cp, cx, 0, cx, top + h);

        // testina di riproduzione
        if (PlayFrame >= 0)
        {
            float px = FrameToX(PlayFrame);
            if (px >= 0 && px <= w) using (var pp = new Pen(Theme.PlayHead, 2)) g.DrawLine(pp, px, 0, px, top + h);
        }

        if (_vZoom > 1)
        {
            using var zb = new SolidBrush(Theme.Warn);
            g.DrawString($"Zoom verticale x{_vZoom:0}", small, zb, w - 130, top + 4);
        }
    }

    private void DrawGaps(Graphics g, int w, int top, int h)
    {
        if (_gaps.Count == 0) return;
        for (int i = 0; i < _gaps.Count; i++)
        {
            var gp = _gaps[i];
            float x1 = FrameToX(gp.Start), x2 = FrameToX(gp.End);
            if (x2 < 0 || x1 > w) continue;
            float a = Math.Max(0, x1), b = Math.Min(w, x2);
            float bw = Math.Max(2, b - a);
            bool active = i == ActiveGap;
            var col = gp.Kind == GapKind.Calo ? Theme.GapDrop : Theme.Gap;
            using (var lb = new SolidBrush(Color.FromArgb(active ? 70 : 34, col))) g.FillRectangle(lb, a, top, bw, h);
            using (var rb = new SolidBrush(Color.FromArgb(active ? 255 : 200, col))) g.FillRectangle(rb, a, RulerH - GapBarH, bw, GapBarH);
            if (active)
                using (var op = new Pen(col, 2)) g.DrawRectangle(op, a, top + 1, bw, h - 2);
        }
    }

    private void DrawCuts(Graphics g, int w, int top, int h)
    {
        if (_cuts.Count == 0) return;
        using var hatch = new System.Drawing.Drawing2D.HatchBrush(System.Drawing.Drawing2D.HatchStyle.WideDownwardDiagonal,
            Color.FromArgb(150, 200, 50, 50), Color.FromArgb(170, 20, 10, 12));
        using var f = new Font("Segoe UI", 8f, FontStyle.Bold);
        using var tb = new SolidBrush(Color.FromArgb(255, 150, 150));
        foreach (var c in _cuts)
        {
            float x1 = FrameToX(c.Start), x2 = FrameToX(c.End);
            if (x2 < 0 || x1 > w) continue;
            float a = Math.Max(0, x1), b = Math.Min(w, x2);
            g.FillRectangle(hatch, a, top, Math.Max(2, b - a), h);
            if (b - a > 70) g.DrawString("✂ tagliato", f, tb, a + 4, top + h / 2f - 7);
        }
    }

    private void DrawFlag(Graphics g, float x, int top, int h, Color c, string label, bool left, Font font)
    {
        if (x < -60 || x > ClientSize.Width + 60) return;
        using var p = new Pen(c, 2);
        using var b = new SolidBrush(c);
        g.DrawLine(p, x, top, x, top + h);
        var sz = g.MeasureString(label, font);
        float fx = left ? x : x - sz.Width - 6;
        var r = new RectangleF(fx, top + h - sz.Height - 6, sz.Width + 6, sz.Height + 2);
        g.FillRectangle(b, r);
        g.DrawString(label, font, Brushes.Black, r.X + 3, r.Y + 1);
    }

    private void DrawRuler(Graphics g, int w)
    {
        double secPerPx = _spp / SampleRate;
        double[] steps = { 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600 };
        double step = steps.FirstOrDefault(s => s / secPerPx >= 80);
        if (step == 0) step = 3600;
        double t0 = _viewStart / SampleRate;
        double first = Math.Floor(t0 / step) * step;
        using var f = new Font("Segoe UI", 7.5f);
        using var tb = new SolidBrush(Theme.TextDim);
        using var tp = new Pen(Theme.Border);
        for (double t = first; ; t += step)
        {
            float x = (float)((t * SampleRate - _viewStart) / _spp);
            if (x > w) break;
            if (x < -80) continue;
            g.DrawLine(tp, x, RulerH - 10, x, RulerH);
            g.DrawString(Theme.FormatTime(t, step < 1), f, tb, x + 2, 1);
            float half = (float)(step / 2 * SampleRate / _spp);
            g.DrawLine(tp, x + half, RulerH - 5, x + half, RulerH);
        }
    }

    private void DrawWave(Graphics g, int w, int top, int laneH)
    {
        float half = laneH / 2f - 2;
        using var pen = new Pen(Theme.Wave);
        using var penOut = new Pen(Theme.WaveOut);
        using var clipPen = new Pen(Theme.WaveClip);

        bool raw = _spp < PeakData.Block && !LiveMode && _wavPath != null;
        if (raw) LoadRaw((long)_viewStart, (int)Math.Ceiling(w * _spp) + 2);

        long inF = InFrame, outF = EffectiveOut;
        for (int x = 0; x < w; x++)
        {
            long f0 = (long)(_viewStart + x * _spp);
            long f1 = (long)(_viewStart + (x + 1) * _spp);
            if (f1 <= f0) f1 = f0 + 1;
            if (f0 >= TotalFrames) break;

            short mnL, mxL, mnR, mxR;
            bool ok = raw ? RawRange(f0, f1, out mnL, out mxL, out mnR, out mxR)
                          : _peaks.GetRange(f0, f1, out mnL, out mxL, out mnR, out mxR);
            if (!ok) continue;

            bool inside = f1 > inF && f0 < outF;
            var p = inside ? pen : penOut;
            DrawCol(g, x, top + laneH / 2f, half, mnL, mxL, p, clipPen);
            DrawCol(g, x, top + laneH + laneH / 2f, half, mnR, mxR, p, clipPen);
        }
    }

    private void DrawCol(Graphics g, int x, float cy, float half, short mn, short mx, Pen p, Pen clip)
    {
        float y1 = cy - Math.Min(1f, mx / 32768f * _vZoom) * half;
        float y2 = cy - Math.Max(-1f, mn / 32768f * _vZoom) * half;
        if (y2 - y1 < 1) y2 = y1 + 1;
        bool clipped = mx >= 32700 || mn <= -32700;
        g.DrawLine(clipped ? clip : p, x, y1, x, y2);
    }

    private void LoadRaw(long start, int frames)
    {
        if (_rawCacheStart >= 0 && start >= _rawCacheStart && start + frames <= _rawCacheStart + _rawCacheFrames) return;
        try
        {
            var info = WavFile.ReadInfo(_wavPath);
            long s = Math.Max(0, start - frames);
            int n = (int)Math.Min(frames * 3L, info.Frames - s);
            if (n <= 0) { _rawCacheStart = -1; return; }
            var bytes = new byte[n * 4];
            using var fs = new FileStream(_wavPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            fs.Position = info.DataOffset + s * 4;
            int got = 0;
            while (got < bytes.Length) { int r = fs.Read(bytes, got, bytes.Length - got); if (r <= 0) break; got += r; }
            n = got / 4;
            _rawCache = new short[n * 2];
            Buffer.BlockCopy(bytes, 0, _rawCache, 0, n * 4);
            _rawCacheStart = s;
            _rawCacheFrames = n;
        }
        catch { _rawCacheStart = -1; }
    }

    private bool RawRange(long f0, long f1, out short mnL, out short mxL, out short mnR, out short mxR)
    {
        mnL = short.MaxValue; mxL = short.MinValue; mnR = short.MaxValue; mxR = short.MinValue;
        if (_rawCacheStart < 0) return _peaks.GetRange(f0, f1, out mnL, out mxL, out mnR, out mxR);
        long a = Math.Max(f0, _rawCacheStart), b = Math.Min(f1, _rawCacheStart + _rawCacheFrames);
        if (b <= a) return false;
        for (long f = a; f < b; f++)
        {
            int i = (int)(f - _rawCacheStart) * 2;
            short l = _rawCache[i], r = _rawCache[i + 1];
            if (l < mnL) mnL = l; if (l > mxL) mxL = l;
            if (r < mnR) mnR = r; if (r > mxR) mxR = r;
        }
        return true;
    }
}
