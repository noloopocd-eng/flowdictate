using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlowDictate.Core;

/// <summary>Persisted settings at %APPDATA%\FlowDictate\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>Virtual-key code of the dictation hotkey. Default 0x14 = CapsLock.</summary>
    public int HotkeyVirtualKey { get; set; } = 0x14;

    /// <summary>Human-readable name of the configured hotkey.</summary>
    [JsonIgnore]
    public string HotkeyName => HotkeyVirtualKey switch
    {
        0x14 => "CapsLock",
        0xA2 => "Left Ctrl",
        0xA3 => "Right Ctrl",
        0xA4 => "Left Alt",
        0xA5 => "Right Alt",
        _ => ((Keys)HotkeyVirtualKey).ToString(),
    };

    /// <summary>Modifier keys keep their normal function; dedicated keys (CapsLock, F-keys) are swallowed.</summary>
    [JsonIgnore]
    public bool HotkeyShouldBeSuppressed => HotkeyVirtualKey is not (>= 0xA0 and <= 0xA5 or 0x5B or 0x5C);

    /// <summary>Path to the Whisper GGML model file.</summary>
    public string WhisperModelPath { get; set; } =
        Path.Combine(AppDataDir, "models", "ggml-base.en.bin");

    public string Language { get; set; } = "en";

    /// <summary>Anthropic API key. If empty, ANTHROPIC_API_KEY env var is used; if neither, rule-based cleanup.</summary>
    public string AnthropicApiKey { get; set; } = "";

    public string ClaudeModel { get; set; } = "claude-opus-4-8";

    /// <summary>Skip the AI cleanup pass entirely (insert lightly-cleaned raw transcript).</summary>
    public bool AiCleanupEnabled { get; set; } = true;

    /// <summary>Show the debug log window when the app starts (off for daily use).</summary>
    public bool ShowDebugWindowOnStartup { get; set; } = false;

    /// <summary>Show the floating listening/processing pill during dictation.</summary>
    public bool ShowStatusOverlay { get; set; } = true;

    /// <summary>
    /// Names and jargon the recognizer keeps getting wrong. Fed to Whisper as a
    /// recognition hint and to the AI cleanup as the authoritative spelling.
    /// </summary>
    public List<string> CustomDictionary { get; set; } = new();

    /// <summary>
    /// Foreground-app process name (substring, lowercase) -> tone hint for the AI cleanup.
    /// E.g. dictating into Outlook leans formal, Slack leans casual.
    /// </summary>
    /// <summary>
    /// When UIA can't read the selection (Electron apps), sample it by synthesizing
    /// Ctrl+C and reading the clipboard (restored afterwards). Skipped in terminals.
    /// </summary>
    public bool ClipboardSelectionFallback { get; set; } = true;

    public Dictionary<string, string> AppToneMap { get; set; } = new()
    {
        ["outlook"] = "formal, professional",
        ["olk"] = "formal, professional",
        ["thunderbird"] = "formal, professional",
        ["slack"] = "casual, friendly",
        ["discord"] = "casual, relaxed",
        ["whatsapp"] = "casual",
        ["telegram"] = "casual",
    };

    [JsonIgnore]
    public string ResolvedApiKey =>
        !string.IsNullOrWhiteSpace(AnthropicApiKey)
            ? AnthropicApiKey
            : Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";

    public static string AppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlowDictate");

    private static string SettingsPath => Path.Combine(AppDataDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
                if (s.HotkeyVirtualKey == 0xA3) // migrate old Right Ctrl default (absent on many laptops)
                {
                    s.HotkeyVirtualKey = 0x14;
                    s.Save();
                }
                return s;
            }
        }
        catch { /* corrupt settings — fall back to defaults */ }
        var fresh = new AppSettings();
        fresh.Save();
        return fresh;
    }

    public void Save()
    {
        Directory.CreateDirectory(AppDataDir);
        File.WriteAllText(SettingsPath,
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
