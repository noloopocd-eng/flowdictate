using System.Threading;
using System.Windows.Automation;

namespace FlowDictate.Insertion;

/// <summary>Reads the currently selected text in the focused app via UI Automation.</summary>
public static class SelectionReader
{
    private const int MaxSelectionChars = 8000; // keep prompts bounded

    /// <summary>Apps where synthesizing Ctrl+C is dangerous (it interrupts processes).</summary>
    private static readonly string[] TerminalApps =
        { "windowsterminal", "openconsole", "conhost", "cmd", "powershell", "pwsh", "wt", "alacritty", "wezterm", "mintty" };

    /// <summary>
    /// Read the selection: UIA first; optionally fall back to sampling the clipboard with a
    /// synthesized Ctrl+C (needed for Electron apps, which hide their selection from UIA).
    /// Must run on the UI (STA) thread.
    /// </summary>
    public static string TryGetSelectedText(bool allowClipboardFallback, string foregroundApp)
    {
        string viaUia = TryGetSelectedText();
        if (viaUia.Length > 0 || !allowClipboardFallback) return viaUia;
        if (TerminalApps.Any(t => foregroundApp.Contains(t, StringComparison.OrdinalIgnoreCase))) return "";

        string? previous = null;
        try { if (Clipboard.ContainsText()) previous = Clipboard.GetText(); } catch { }
        try { Clipboard.Clear(); } catch { return ""; }

        TextInserter.SendCtrl(0x43 /*C*/);
        Thread.Sleep(150); // give the app time to service the copy

        string selection = "";
        try { if (Clipboard.ContainsText()) selection = Clipboard.GetText().Trim(); } catch { }

        try
        {
            if (previous is not null) Clipboard.SetText(previous);
            else Clipboard.Clear();
        }
        catch { }

        if (selection.Length > MaxSelectionChars) selection = selection[..MaxSelectionChars];
        return selection;
    }

    /// <returns>Selected text, or "" if none / unsupported by the app.</returns>
    public static string TryGetSelectedText()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element is null) return "";
            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out object? patternObj) ||
                patternObj is not TextPattern textPattern)
                return "";

            var selections = textPattern.GetSelection();
            if (selections.Length == 0) return "";
            return selections[0].GetText(MaxSelectionChars).Trim();
        }
        catch
        {
            return "";
        }
    }
}
