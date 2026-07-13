namespace FlowDictate.Cleanup;

/// <summary>
/// Transforms a raw transcript into clean, insert-ready text:
/// remove fillers, punctuate, capitalize, resolve self-corrections.
/// Implementations: ClaudeCleaner (Anthropic API), RuleBasedCleaner (offline fallback).
/// </summary>
public interface ITextCleaner
{
    string Name { get; }

    Task<string> CleanAsync(string rawTranscript, CancellationToken ct = default);
}

/// <summary>
/// Cleaners that can act on selected text: "make this more concise" transforms the
/// selection; plain speech is treated as dictation that replaces it.
/// </summary>
public interface ISelectionAwareCleaner : ITextCleaner
{
    Task<string> TransformSelectionAsync(string rawTranscript, string selectedText, CancellationToken ct = default);
}
