using NAudio.Wave;

namespace FlowDictate.Audio;

/// <summary>
/// Microphone capture, normalized to 16 kHz mono floats (what Whisper expects).
///
/// The capture stream is kept running ("hot") with a rolling ring buffer so a
/// dictation session includes ~0.4s of audio from BEFORE the hotkey press —
/// otherwise the first spoken word gets clipped by device-open latency.
/// Audio is only retained for the ring duration and never leaves the machine
/// unless a session is active.
/// </summary>
public sealed class AudioRecorder : IDisposable
{
    public const int SampleRate = 16000;
    private const int PreRollMs = 400;
    private const int RingSeconds = 1;

    private static readonly (int rate, int channels)[] FormatCandidates =
    {
        (16000, 1), (48000, 1), (44100, 1), (48000, 2), (44100, 2),
    };

    private WaveInEvent? _waveIn;
    private readonly object _lock = new();
    private bool _hot;        // capture stream running
    private bool _recording;  // session active (samples being retained)
    private bool _disposed;
    private int _captureRate = SampleRate;
    private int _captureChannels = 1;

    private float[] _ring = Array.Empty<float>();
    private int _ringPos;
    private bool _ringWrapped;
    private readonly List<float> _samples = new(SampleRate * 30);

    public static string DescribeDevices()
    {
        int n = WaveInEvent.DeviceCount;
        if (n == 0) return "no capture devices";
        var names = new List<string>();
        for (int i = 0; i < n; i++) names.Add(WaveInEvent.GetCapabilities(i).ProductName);
        return string.Join("; ", names);
    }

    /// <summary>Open the device and start the hot stream. Safe to call repeatedly.</summary>
    /// <exception cref="InvalidOperationException">No capture device could be opened.</exception>
    public void EnsureHot()
    {
        lock (_lock)
        {
            if (_hot || _disposed) return;
            Exception? last = null;
            foreach (var (rate, channels) in FormatCandidates)
            {
                WaveInEvent? waveIn = null;
                try
                {
                    waveIn = new WaveInEvent
                    {
                        WaveFormat = new WaveFormat(rate, 16, channels),
                        BufferMilliseconds = 50,
                    };
                    waveIn.DataAvailable += OnData;
                    waveIn.RecordingStopped += OnRecordingStopped;
                    waveIn.StartRecording();
                    _waveIn = waveIn;
                    _captureRate = rate;
                    _captureChannels = channels;
                    _ring = new float[rate * channels * RingSeconds];
                    _ringPos = 0;
                    _ringWrapped = false;
                    _hot = true;
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    waveIn?.Dispose();
                }
            }
            throw new InvalidOperationException(
                $"Could not open microphone (devices: {DescribeDevices()}). Last error: {last?.Message}", last);
        }
    }

    /// <summary>Begin a session: retain samples from PreRollMs ago onward.</summary>
    public void Start()
    {
        lock (_lock)
        {
            if (_recording) return;
            if (!_hot) EnsureHot(); // lock is reentrant on this thread
            _samples.Clear();

            // Seed with the last PreRollMs of ring audio (frame-aligned).
            int want = (int)((long)PreRollMs * _captureRate / 1000) * _captureChannels;
            int available = _ringWrapped ? _ring.Length : _ringPos;
            want = Math.Min(want, available);
            int start = (_ringPos - want + _ring.Length) % Math.Max(1, _ring.Length);
            for (int i = 0; i < want; i++)
                _samples.Add(_ring[(start + i) % _ring.Length]);

            _recording = true;
        }
    }

    /// <summary>End the session and return 16 kHz mono samples. The hot stream keeps running.</summary>
    public float[] Stop()
    {
        List<float> captured;
        int rate, channels;
        lock (_lock)
        {
            if (!_recording) return Array.Empty<float>();
            _recording = false;
            captured = new List<float>(_samples);
            _samples.Clear();
            rate = _captureRate;
            channels = _captureChannels;
        }
        return Normalize(captured, rate, channels);
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        lock (_lock)
        {
            if (!_hot) return;
            for (int i = 0; i + 1 < e.BytesRecorded; i += 2)
            {
                float v = BitConverter.ToInt16(e.Buffer, i) / 32768f;
                _ring[_ringPos] = v;
                _ringPos++;
                if (_ringPos >= _ring.Length) { _ringPos = 0; _ringWrapped = true; }
                if (_recording) _samples.Add(v);
            }
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        // Fires on device failure/unplug (or our own dispose). Reset so the next
        // session reopens the device. Safe to dispose here: the thread has ended.
        lock (_lock)
        {
            if (sender is WaveInEvent w && ReferenceEquals(w, _waveIn))
            {
                _hot = false;
                _recording = false;
                _waveIn = null;
                try { w.Dispose(); } catch { }
            }
        }
    }

    /// <summary>Downmix to mono and linearly resample to 16 kHz.</summary>
    private static float[] Normalize(List<float> interleaved, int rate, int channels)
    {
        float[] mono;
        if (channels == 1)
        {
            mono = interleaved.ToArray();
        }
        else
        {
            mono = new float[interleaved.Count / channels];
            for (int i = 0; i < mono.Length; i++)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++) sum += interleaved[i * channels + c];
                mono[i] = sum / channels;
            }
        }

        if (rate == SampleRate) return mono;

        double ratio = rate / (double)SampleRate;
        var resampled = new float[(int)(mono.Length / ratio)];
        for (int i = 0; i < resampled.Length; i++)
        {
            double src = i * ratio;
            int i0 = (int)src;
            int i1 = Math.Min(i0 + 1, mono.Length - 1);
            double frac = src - i0;
            resampled[i] = (float)(mono[i0] * (1 - frac) + mono[i1] * frac);
        }
        return resampled;
    }

    public void Dispose()
    {
        WaveInEvent? waveIn;
        lock (_lock)
        {
            _disposed = true;
            _hot = false;
            _recording = false;
            waveIn = _waveIn;
            _waveIn = null;
        }
        if (waveIn is not null)
        {
            waveIn.DataAvailable -= OnData;
            waveIn.RecordingStopped -= OnRecordingStopped;
            // Deferred dispose: never free buffers while the record thread is mid-loop.
            waveIn.RecordingStopped += (_, _) => waveIn.Dispose();
            try { waveIn.StopRecording(); }
            catch { try { waveIn.Dispose(); } catch { } }
        }
    }
}
