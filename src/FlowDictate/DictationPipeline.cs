using System.Diagnostics;
using FlowDictate.Audio;
using FlowDictate.Cleanup;
using FlowDictate.Core;
using FlowDictate.Insertion;
using FlowDictate.Transcription;

namespace FlowDictate;

/// <summary>
/// The end-to-end flow: record while the hotkey is held -> transcribe (on-device Whisper)
/// -> clean (Claude or rules) -> insert at cursor. Reports stage timings for the latency budget.
/// </summary>
public sealed class DictationPipeline : IDisposable
{
    private readonly AudioRecorder _recorder = new();
    private readonly ITranscriber _transcriber;
    private readonly ITextCleaner _cleaner;
    private readonly Action<string> _log;
    private readonly SynchronizationContext _uiContext;

    public bool IsReady { get; private set; }

    /// <summary>Text that was selected in the target app when the session started ("" = none).</summary>
    private string _selectionContext = "";

    public void SetSelectionContext(string selectedText) => _selectionContext = selectedText;

    /// <summary>Capture the target app and apply its tone mapping (app-aware tone).</summary>
    public void CaptureTargetApp(AppSettings settings)
    {
        if (_cleaner is not ClaudeCleaner claude) return;
        string app = ForegroundApp.GetProcessName();
        claude.ToneHint = null;
        if (app.Length == 0) return;
        foreach (var (key, tone) in settings.AppToneMap)
        {
            if (app.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                claude.ToneHint = tone;
                _log($"Target app '{app}' -> tone: {tone}");
                return;
            }
        }
    }

    public event Action? ProcessingStarted;
    public event Action<string>? Completed; // final inserted text ("" if nothing); raised on the UI thread

    private void RaiseCompleted(string text) => _uiContext.Post(_ => Completed?.Invoke(text), null);

    public DictationPipeline(AppSettings settings, Action<string> log, SynchronizationContext uiContext)
    {
        _log = log;
        _uiContext = uiContext;
        _transcriber = new WhisperTranscriber(settings.WhisperModelPath, settings.Language, settings.CustomDictionary);

        string apiKey = settings.ResolvedApiKey;
        if (settings.AiCleanupEnabled && apiKey.Length > 0)
            _cleaner = new ClaudeCleaner(apiKey, settings.ClaudeModel, log, settings.CustomDictionary);
        else
            _cleaner = new RuleBasedCleaner();

        _log($"Transcriber: {_transcriber.Name} | Cleaner: {_cleaner.Name}");
        _log($"Mic devices: {AudioRecorder.DescribeDevices()}");
    }

    public async Task InitializeAsync()
    {
        var sw = Stopwatch.StartNew();
        await _transcriber.InitializeAsync();
        IsReady = true;
        _log($"Whisper model loaded in {sw.ElapsedMilliseconds} ms. Ready.");
        try
        {
            _recorder.EnsureHot();
            _log("Mic stream warm — 0.4s pre-roll active (first words won't be clipped).");
        }
        catch (Exception ex)
        {
            _log($"MIC WARN: could not warm mic ({ex.Message}) — will retry on first use.");
        }
    }

    public void StartRecording()
    {
        if (!IsReady)
        {
            _log("Not ready yet (model still loading) — ignoring hotkey.");
            return;
        }
        try
        {
            _recorder.Start();
            _log("Recording...");
        }
        catch (Exception ex)
        {
            _log($"MIC ERROR: {ex.Message}");
        }
    }

    public void CancelRecording()
    {
        _recorder.Stop();
        _log("Recording cancelled.");
    }

    public async Task StopAndProcessAsync()
    {
        float[] samples = _recorder.Stop();
        double seconds = samples.Length / (double)AudioRecorder.SampleRate;
        _log($"Recording stopped: {seconds:F1}s of audio.");
        if (seconds < 0.3) { RaiseCompleted(""); return; }

        // Silence gate: Whisper hallucinates text on near-silent audio, which would
        // insert garbage at the cursor. Skip clips with no speech-like energy.
        double rms = Math.Sqrt(samples.Average(s => (double)s * s));
        float peak = samples.Max(Math.Abs);
        if (rms < 0.0025 && peak < 0.02)
        {
            _log($"No speech detected (rms={rms:F4}, peak={peak:F3}) — skipping.");
            RaiseCompleted("");
            return;
        }

        ProcessingStarted?.Invoke();
        var total = Stopwatch.StartNew();
        try
        {
            var sw = Stopwatch.StartNew();
            string raw = await _transcriber.TranscribeAsync(samples);
            long transcribeMs = sw.ElapsedMilliseconds;
            _log($"Transcript ({transcribeMs} ms): \"{raw}\"");
            if (string.IsNullOrWhiteSpace(raw)) { RaiseCompleted(""); return; }

            sw.Restart();
            string cleaned;
            if (_selectionContext.Length > 0 && _cleaner is ISelectionAwareCleaner selectionCleaner)
            {
                _log($"Selection active ({_selectionContext.Length} chars) — command mode.");
                cleaned = await selectionCleaner.TransformSelectionAsync(raw, _selectionContext);
            }
            else
            {
                cleaned = await _cleaner.CleanAsync(raw);
            }
            long cleanMs = sw.ElapsedMilliseconds;
            _log($"Cleaned ({cleanMs} ms): \"{cleaned}\"");
            if (string.IsNullOrWhiteSpace(cleaned)) { RaiseCompleted(""); return; }

            // Insertion must run on the UI (STA) thread for clipboard access.
            _uiContext.Post(_ =>
            {
                string strategy = TextInserter.InsertAtCursor(cleaned);
                _log($"Inserted via {strategy}. Total release-to-insert: {total.ElapsedMilliseconds} ms.");
                Completed?.Invoke(cleaned);
            }, null);
        }
        catch (Exception ex)
        {
            _log($"Pipeline error: {ex.Message}");
            RaiseCompleted("");
        }
    }

    public void Dispose()
    {
        _recorder.Dispose();
        _transcriber.Dispose();
    }
}
