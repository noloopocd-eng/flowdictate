using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace FlowDictate.Insertion;

/// <summary>
/// Inserts text at the current cursor position in whatever app has focus.
/// Strategy 1: Windows UI Automation — replace the focused element's value with
///             prefix + text + suffix computed from the current selection (no clipboard touched).
/// Strategy 2: clipboard-swap + synthesized Ctrl+V, restoring the previous clipboard text.
/// Must be called on the UI (STA) thread — both UIA and the Clipboard API require it.
/// </summary>
public static class TextInserter
{
    /// <returns>Name of the strategy used, for the debug log.</returns>
    public static string InsertAtCursor(string text)
    {
        if (string.IsNullOrEmpty(text)) return "nothing to insert";

        try
        {
            if (TryUiaInsert(text)) return "UI Automation";
        }
        catch { /* UIA not supported by target — fall through */ }

        ClipboardPaste(text);
        return "clipboard paste";
    }

    /// <summary>
    /// Direct insertion via UIA: works for plain edit/text boxes that expose both
    /// TextPattern (to locate the caret/selection) and a writable ValuePattern.
    /// Rich editors (Word, VS Code) don't allow this — they use the paste fallback.
    /// </summary>
    private static bool TryUiaInsert(string text)
    {
        var element = AutomationElement.FocusedElement;
        if (element is null) return false;

        if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valueObj) ||
            valueObj is not ValuePattern value || value.Current.IsReadOnly)
            return false;

        if (!element.TryGetCurrentPattern(TextPattern.Pattern, out object? textObj) ||
            textObj is not TextPattern textPattern)
            return false;

        var selections = textPattern.GetSelection();
        if (selections.Length == 0) return false;
        var selection = selections[0];
        var document = textPattern.DocumentRange;

        // prefix = document start -> selection start; suffix = selection end -> document end.
        var prefixRange = document.Clone();
        prefixRange.MoveEndpointByRange(TextPatternRangeEndpoint.End, selection, TextPatternRangeEndpoint.Start);
        var suffixRange = document.Clone();
        suffixRange.MoveEndpointByRange(TextPatternRangeEndpoint.Start, selection, TextPatternRangeEndpoint.End);

        string prefix = prefixRange.GetText(int.MaxValue);
        string suffix = suffixRange.GetText(int.MaxValue);

        value.SetValue(prefix + text + suffix);
        return true;
    }

    private static void ClipboardPaste(string text)
    {
        // Preserve existing clipboard text (images/files are not preserved — MVP limitation).
        string? previous = null;
        try
        {
            if (Clipboard.ContainsText()) previous = Clipboard.GetText();
        }
        catch { /* clipboard busy — proceed without restore */ }

        Clipboard.SetText(text);
        SendCtrlV();

        // Give the target app time to read the clipboard before restoring.
        if (previous is not null)
        {
            var restoreTimer = new System.Windows.Forms.Timer { Interval = 300 };
            restoreTimer.Tick += (_, _) =>
            {
                restoreTimer.Dispose();
                try { Clipboard.SetText(previous); } catch { }
            };
            restoreTimer.Start();
        }
    }

    private static void SendCtrlV() => SendCtrl(0x56 /*V*/);

    /// <summary>Synthesize Ctrl+key (used for paste and for selection sampling via Ctrl+C).</summary>
    internal static void SendCtrl(ushort vk)
    {
        const ushort VK_LCONTROL = 0xA2; // left ctrl — the hotkey is a different key, so no interference
        const uint KEYEVENTF_KEYUP = 0x0002;

        var inputs = new INPUT[4];
        inputs[0] = Key(VK_LCONTROL, 0);
        inputs[1] = Key(vk, 0);
        inputs[2] = Key(vk, KEYEVENTF_KEYUP);
        inputs[3] = Key(VK_LCONTROL, KEYEVENTF_KEYUP);
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT Key(ushort vk, uint flags) => new()
    {
        type = 1, // INPUT_KEYBOARD
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags } },
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}
