using System.Text.RegularExpressions;

namespace FlowDictate.Cleanup;

/// <summary>
/// Offline fallback cleaner: strips filler words, fixes spacing/capitalization,
/// ensures terminal punctuation. No self-correction resolution (that needs the LLM).
/// </summary>
public sealed partial class RuleBasedCleaner : ITextCleaner
{
    public string Name => "Rule-based (offline)";

    [GeneratedRegex(@"\b(um+|uh+|erm+|hmm+|you know,?|i mean,?|like,)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex FillerWords();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex ExtraSpaces();

    [GeneratedRegex(@"\s+([,.!?;:])")]
    private static partial Regex SpaceBeforePunct();

    public Task<string> CleanAsync(string rawTranscript, CancellationToken ct = default)
    {
        string text = rawTranscript.Trim();
        if (text.Length == 0) return Task.FromResult(text);

        text = FillerWords().Replace(text, "");
        text = SpaceBeforePunct().Replace(text, "$1");
        text = ExtraSpaces().Replace(text, " ").Trim();
        if (text.Length == 0) return Task.FromResult(text);

        // Capitalize first letter; add terminal punctuation if missing.
        text = char.ToUpper(text[0]) + text[1..];
        if (!".!?".Contains(text[^1])) text += ".";

        return Task.FromResult(text);
    }
}
