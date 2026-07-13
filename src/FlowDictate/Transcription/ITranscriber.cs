namespace FlowDictate.Transcription;

/// <summary>
/// Speech-to-text abstraction. Implementations: WhisperTranscriber (on-device, default);
/// future: cloud APIs. Input is 16 kHz mono float PCM in [-1, 1].
/// </summary>
public interface ITranscriber : IDisposable
{
    /// <summary>Human-readable name for logs/settings.</summary>
    string Name { get; }

    /// <summary>Load models etc. May be slow; call once at startup off the UI thread.</summary>
    Task InitializeAsync(CancellationToken ct = default);

    Task<string> TranscribeAsync(float[] samples16kMono, CancellationToken ct = default);
}
