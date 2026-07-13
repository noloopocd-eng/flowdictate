using System.IO;
using System.Text;
using Whisper.net;

namespace FlowDictate.Transcription;

/// <summary>
/// On-device transcription via Whisper.net (whisper.cpp). Private, free, offline.
/// Requires a GGML model file (e.g. ggml-base.en.bin) — path supplied by settings.
/// </summary>
public sealed class WhisperTranscriber : ITranscriber
{
    private readonly string _modelPath;
    private readonly string _language;
    private readonly string? _vocabularyHint;
    private WhisperFactory? _factory;

    public string Name => $"Whisper ({Path.GetFileNameWithoutExtension(_modelPath)})";

    public WhisperTranscriber(string modelPath, string language = "en", IReadOnlyList<string>? vocabulary = null)
    {
        _modelPath = modelPath;
        _language = language;
        // An initial prompt biases Whisper toward these spellings when the audio is ambiguous.
        _vocabularyHint = vocabulary is { Count: > 0 } ? "Glossary: " + string.Join(", ", vocabulary) + "." : null;
    }

    public Task InitializeAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        if (!File.Exists(_modelPath))
            throw new FileNotFoundException(
                $"Whisper model not found: {_modelPath}. Download a GGML model (e.g. ggml-base.en.bin) first.",
                _modelPath);
        _factory = WhisperFactory.FromPath(_modelPath);
    }, ct);

    public async Task<string> TranscribeAsync(float[] samples16kMono, CancellationToken ct = default)
    {
        if (_factory is null)
            throw new InvalidOperationException("Call InitializeAsync first.");
        if (samples16kMono.Length < Audio.AudioRecorder.SampleRate / 4) // < 250 ms — nothing to do
            return string.Empty;

        var builder = _factory.CreateBuilder()
            .WithLanguage(_language)
            .WithProbabilities()
            .WithThreads(Math.Max(2, Environment.ProcessorCount / 2));
        if (_vocabularyHint is not null) builder = builder.WithPrompt(_vocabularyHint);
        await using var processor = builder.Build();

        var sb = new StringBuilder();
        await foreach (var segment in processor.ProcessAsync(samples16kMono, ct))
        {
            // Whisper hallucinates on non-speech audio. Drop segments it itself flags as
            // probably-not-speech, and its telltale sound-effect artifacts like "[MUSIC]".
            if (segment.NoSpeechProbability > 0.6f) continue;
            string t = segment.Text.Trim();
            if (t.Length >= 2 && (t[0] == '[' && t[^1] == ']' || t[0] == '(' && t[^1] == ')' || t[0] == '♪')) continue;
            sb.Append(segment.Text);
        }

        return sb.ToString().Trim();
    }

    public void Dispose() => _factory?.Dispose();
}
