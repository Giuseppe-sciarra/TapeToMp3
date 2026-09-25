using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Tape2MP3.Audio;

public sealed class InputDevice
{
    public string Id { get; init; }
    public string Name { get; init; }
    public override string ToString() => Name;
}

/// <summary>
/// Cattura WASAPI sempre attiva sul dispositivo scelto: alimenta i VU meter anche
/// quando non si registra (così regoli il volume prima di partire), e scrive su WAV
/// solo quando la registrazione è attiva. Converte qualsiasi formato in PCM16 stereo.
/// </summary>
public sealed class CaptureEngine : IDisposable
{
    private WasapiCapture _capture;
    private MMDevice _device;
    private WaveFormat _srcFormat;
    private bool _srcFloat;
    private byte[] _conv = new byte[0];

    private readonly object _writeLock = new();
    private WavWriter _writer;
    private PeakData _peaks;
    private DateTime _lastFlush;

    private WaveOutEvent _monitorOut;
    private BufferedWaveProvider _monitorBuf;

    // livelli (letti dal thread UI)
    private float _peakL, _peakR;
    private int _clipFlag;

    // auto-pausa a fine lato
    private double _silentSeconds;
    private double _recordedSecondsThisRun;

    public int SampleRate { get; private set; }
    public bool IsOpen => _capture != null;
    public bool IsRecording { get; private set; }
    public long RecordedFrames { get { lock (_writeLock) return _writer?.Frames ?? 0; } }

    public bool AutoPauseEnabled { get; set; }
    public double AutoPauseSeconds { get; set; } = 20;
    public double SilenceThresholdDb { get; set; } = -45;

    /// <summary>Scatta dal thread di cattura quando la registrazione si mette in pausa da sola.</summary>
    public event Action AutoPaused;
    /// <summary>Scatta se il dispositivo si scollega o va in errore.</summary>
    public event Action<Exception> DeviceStopped;

    public static List<InputDevice> ListDevices()
    {
        var list = new List<InputDevice>();
        using var en = new MMDeviceEnumerator();
        foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            list.Add(new InputDevice { Id = d.ID, Name = d.FriendlyName });
            d.Dispose();
        }
        return list;
    }

    public static string DefaultDeviceId()
    {
        try
        {
            using var en = new MMDeviceEnumerator();
            using var d = en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            return d.ID;
        }
        catch { return null; }
    }

    public void Open(string deviceId)
    {
        Close();
        using var en = new MMDeviceEnumerator();
        _device = en.GetDevice(deviceId);
        _capture = new WasapiCapture(_device, true, 50);
        _srcFormat = _capture.WaveFormat;
        _srcFloat = _srcFormat.Encoding == WaveFormatEncoding.IeeeFloat ||
                    (_srcFormat is WaveFormatExtensible ext && ext.SubFormat == NAudio.Dmo.AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT);
        SampleRate = _srcFormat.SampleRate;
        _capture.DataAvailable += OnData;
        _capture.RecordingStopped += OnStopped;
        _capture.StartRecording();
    }

    public void Close()
    {
        StopRecording();
        SetMonitor(false);
        if (_capture != null)
        {
            _capture.DataAvailable -= OnData;
            _capture.RecordingStopped -= OnStopped;
            try { _capture.StopRecording(); } catch { }
            _capture.Dispose();
            _capture = null;
        }
        _device?.Dispose();
        _device = null;
    }

    /// <summary>Inizia (o riprende) la scrittura su file. peaks viene aggiornata in tempo reale.</summary>
    public void StartRecording(WavWriter writer, PeakData peaks)
    {
        lock (_writeLock)
        {
            _writer = writer;
            _peaks = peaks;
            _lastFlush = DateTime.UtcNow;
            _silentSeconds = 0;
            _recordedSecondsThisRun = 0;
            IsRecording = true;
        }
    }

    /// <summary>Ferma la scrittura e restituisce il writer (che il chiamante chiude o riusa).</summary>
    public WavWriter StopRecording()
    {
        lock (_writeLock)
        {
            IsRecording = false;
            var w = _writer;
            _writer = null;
            _peaks = null;
            w?.Flush();
            return w;
        }
    }

    public void SetMonitor(bool on)
    {
        if (on && _monitorOut == null && SampleRate > 0)
        {
            _monitorBuf = new BufferedWaveProvider(new WaveFormat(SampleRate, 16, 2))
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromSeconds(1)
            };
            _monitorOut = new WaveOutEvent { DesiredLatency = 120 };
            _monitorOut.Init(_monitorBuf);
            _monitorOut.Play();
        }
        else if (!on && _monitorOut != null)
        {
            try { _monitorOut.Stop(); } catch { }
            _monitorOut.Dispose();
            _monitorOut = null;
            _monitorBuf = null;
        }
    }

    public bool MonitorOn => _monitorOut != null;

    /// <summary>Legge e azzera i picchi accumulati dall'ultima chiamata (0..1).</summary>
    public void ReadLevels(out float l, out float r, out bool clip)
    {
        l = Interlocked.Exchange(ref _peakL, 0f);
        r = Interlocked.Exchange(ref _peakR, 0f);
        clip = Interlocked.Exchange(ref _clipFlag, 0) != 0;
    }

    private void OnStopped(object sender, StoppedEventArgs e)
    {
        if (e.Exception != null) DeviceStopped?.Invoke(e.Exception);
    }

    private void OnData(object sender, WaveInEventArgs e)
    {
        int n = Convert(e.Buffer, e.BytesRecorded);
        if (n <= 0) return;

        // livelli
        short pl = 0, pr = 0;
        for (int i = 0; i + 3 < n; i += 4)
        {
            short l = (short)(_conv[i] | (_conv[i + 1] << 8));
            short r = (short)(_conv[i + 2] | (_conv[i + 3] << 8));
            int al = l == short.MinValue ? short.MaxValue : Math.Abs(l);
            int ar = r == short.MinValue ? short.MaxValue : Math.Abs(r);
            if (al > pl) pl = (short)al;
            if (ar > pr) pr = (short)ar;
        }
        float fl = pl / 32767f, fr = pr / 32767f;
        if (fl > _peakL) _peakL = fl;
        if (fr > _peakR) _peakR = fr;
        if (pl >= 32700 || pr >= 32700) Interlocked.Exchange(ref _clipFlag, 1);

        _monitorBuf?.AddSamples(_conv, 0, n);

        bool fireAutoPause = false;
        lock (_writeLock)
        {
            if (IsRecording && _writer != null)
            {
                _writer.Write(_conv, 0, n);
                _peaks?.Append(_conv, 0, n);

                double sec = (double)(n / 4) / SampleRate;
                _recordedSecondsThisRun += sec;
                double thr = Math.Pow(10, SilenceThresholdDb / 20.0);
                if (Math.Max(fl, fr) < thr) _silentSeconds += sec; else _silentSeconds = 0;

                if ((DateTime.UtcNow - _lastFlush).TotalSeconds >= 5)
                {
                    _writer.Flush();
                    _lastFlush = DateTime.UtcNow;
                }

                if (AutoPauseEnabled && _silentSeconds >= AutoPauseSeconds && _recordedSecondsThisRun > AutoPauseSeconds + 10)
                {
                    IsRecording = false;
                    fireAutoPause = true;
                }
            }
        }
        if (fireAutoPause) AutoPaused?.Invoke();
    }

    /// <summary>Converte il buffer del dispositivo in PCM16 stereo dentro _conv. Ritorna i byte validi.</summary>
    private int Convert(byte[] src, int bytes)
    {
        int ch = _srcFormat.Channels;
        int bits = _srcFormat.BitsPerSample;
        int bpf = ch * bits / 8;
        if (bpf == 0) return 0;
        int frames = bytes / bpf;
        int outBytes = frames * 4;
        if (_conv.Length < outBytes) _conv = new byte[outBytes * 2];

        int o = 0;
        for (int f = 0; f < frames; f++)
        {
            int baseIdx = f * bpf;
            short l = ReadSample(src, baseIdx, bits);
            short r = ch > 1 ? ReadSample(src, baseIdx + bits / 8, bits) : l;
            _conv[o++] = (byte)l; _conv[o++] = (byte)(l >> 8);
            _conv[o++] = (byte)r; _conv[o++] = (byte)(r >> 8);
        }
        return outBytes;
    }

    private short ReadSample(byte[] b, int i, int bits)
    {
        if (_srcFloat)
        {
            if (bits == 32)
            {
                float v = BitConverter.ToSingle(b, i);
                return ToShort(v);
            }
            if (bits == 64)
            {
                double v = BitConverter.ToDouble(b, i);
                return ToShort((float)v);
            }
            return 0;
        }
        switch (bits)
        {
            case 16: return (short)(b[i] | (b[i + 1] << 8));
            case 24: return (short)(b[i + 1] | (b[i + 2] << 8));
            case 32: return (short)(b[i + 2] | (b[i + 3] << 8));
            case 8: return (short)((b[i] - 128) << 8);
            default: return 0;
        }
    }

    private static short ToShort(float v)
    {
        if (float.IsNaN(v)) return 0;
        v *= 32767f;
        if (v > 32767f) return 32767;
        if (v < -32768f) return -32768;
        return (short)Math.Round(v);
    }

    public void Dispose() => Close();
}
