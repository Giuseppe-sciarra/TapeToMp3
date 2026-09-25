using NAudio.Wave;

namespace Tape2MP3.Audio;

/// <summary>
/// Riproduzione del WAV di lavoro da un punto qualsiasi fino a un limite,
/// saltando le zone tagliate (così senti esattamente cosa verrà esportato).
/// </summary>
public sealed class PlaybackEngine : IDisposable
{
    private WaveOutEvent _out;
    private RangeProvider _provider;

    public bool IsPlaying => _out != null && _out.PlaybackState == PlaybackState.Playing;
    public event Action Finished;

    public void Play(string path, long fromFrame, long toFrame, IReadOnlyList<CutRange> cuts = null)
    {
        Stop();
        var info = WavFile.ReadInfo(path);
        if (toFrame <= 0 || toFrame > info.Frames) toFrame = info.Frames;
        if (fromFrame < 0) fromFrame = 0;
        cuts ??= Array.Empty<CutRange>();
        fromFrame = CutList.SkipCut(fromFrame, cuts);
        if (fromFrame >= toFrame) return;

        var parts = CutList.Keep(fromFrame, toFrame, cuts);
        if (parts.Count == 0) return;

        _provider = new RangeProvider(path, info, parts);
        _out = new WaveOutEvent { DesiredLatency = 150 };
        _out.Init(_provider);
        _out.PlaybackStopped += (s, e) =>
        {
            // ignora lo stop di una riproduzione già sostituita da una nuova
            if (_out == null || ReferenceEquals(s, _out)) Finished?.Invoke();
        };
        _out.Play();
    }

    /// <summary>Frame del file attualmente in ascolto (per la testina).</summary>
    public long CurrentFrame
    {
        get
        {
            var o = _out; var p = _provider;
            if (o == null || p == null) return -1;
            try { return p.MapPlayed(o.GetPosition() / WavFile.BytesPerFrame); }
            catch { return -1; }
        }
    }

    public void Stop()
    {
        if (_out != null)
        {
            var o = _out;
            _out = null;
            try { o.Stop(); } catch { }
            o.Dispose();
        }
        _provider?.Dispose();
        _provider = null;
    }

    public void Dispose() => Stop();

    private sealed class RangeProvider : IWaveProvider, IDisposable
    {
        private readonly FileStream _fs;
        private readonly WavFile.Info _info;
        private readonly List<CutRange> _parts;
        private readonly long[] _outStart; // frame di uscita a cui inizia ogni parte
        private int _part;
        private long _posInPart;
        public WaveFormat WaveFormat { get; }

        public RangeProvider(string path, WavFile.Info info, List<CutRange> parts)
        {
            _info = info;
            _parts = parts;
            _outStart = new long[parts.Count];
            long acc = 0;
            for (int i = 0; i < parts.Count; i++) { _outStart[i] = acc; acc += parts[i].Length; }
            WaveFormat = new WaveFormat(info.SampleRate, 16, 2);
            _fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
            Seek();
        }

        private void Seek()
        {
            if (_part < _parts.Count)
                _fs.Position = _info.DataOffset + (_parts[_part].Start + _posInPart) * WavFile.BytesPerFrame;
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            int written = 0;
            count -= count % 4;
            while (written < count && _part < _parts.Count)
            {
                long left = (_parts[_part].Length - _posInPart) * 4;
                if (left <= 0) { _part++; _posInPart = 0; Seek(); continue; }
                int want = (int)Math.Min(count - written, left);
                int n = _fs.Read(buffer, offset + written, want);
                if (n <= 0) { _part = _parts.Count; break; }
                n -= n % 4;
                written += n;
                _posInPart += n / 4;
            }
            return written;
        }

        /// <summary>Converte i frame riprodotti (in uscita) nel frame corrispondente del file.</summary>
        public long MapPlayed(long played)
        {
            int i = Array.BinarySearch(_outStart, played);
            if (i < 0) i = ~i - 1;
            i = Math.Clamp(i, 0, _parts.Count - 1);
            return Math.Min(_parts[i].End, _parts[i].Start + (played - _outStart[i]));
        }

        public void Dispose() => _fs.Dispose();
    }
}
