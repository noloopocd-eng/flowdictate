using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FlowDictate.Hotkeys;

/// <summary>
/// Global hotkey monitor built on a WH_KEYBOARD_LL hook.
///
/// Semantics (Wispr-style, single configurable key):
///   - Hold the key (&gt;= TapThresholdMs) and release  -> push-to-talk: Started ... Stopped(commit)
///   - Double-tap the key                               -> hands-free: Started ... (tap again) ... Stopped(commit)
///   - Single short tap                                 -> Stopped(cancel) — too short to mean anything
///
/// Recording starts on the *first* key-down so no speech is lost while the
/// state machine decides which mode the user meant.
/// </summary>
public sealed class HotkeyMonitor : IDisposable
{
    private const int TapThresholdMs = 300;
    private const int DoubleTapWindowMs = 400;

    public event Action<ListeningMode>? Started;
    public event Action<bool /*commit*/>? Stopped;

    /// <summary>Virtual-key code of the hotkey. Default: CapsLock.</summary>
    public int VirtualKey { get; set; } = 0x14; // VK_CAPITAL

    /// <summary>
    /// Swallow the key so its normal function (e.g. toggling caps) never fires.
    /// Shift+key is always passed through, preserving access to the original function.
    /// </summary>
    public bool SuppressKey { get; set; } = true;

    private enum State { Idle, PushHeld, TapPending, HandsFree, StoppingHeld }
    public enum ListeningMode { PushToTalk, HandsFree }

    private State _state = State.Idle;
    private long _downTimestamp;
    private bool _keyIsDown; // suppress auto-repeat WM_KEYDOWN
    private bool _toggleSnapshot; // caps/num/scroll toggle state at session start
    private readonly System.Windows.Forms.Timer _tapTimer;
    private readonly System.Windows.Forms.Timer _toggleFixTimer;

    /// <summary>Marks keystrokes we inject ourselves so the hook passes them through.</summary>
    private const int SelfInjectionMarker = 0x464C4457; // "FLDW"

    private bool IsToggleKey => VirtualKey is 0x14 or 0x90 or 0x91; // caps, num, scroll

    private bool ToggleState => (Native.GetKeyState(VirtualKey) & 1) != 0;

    private IntPtr _hookHandle = IntPtr.Zero;
    private readonly Native.LowLevelKeyboardProc _hookProc; // kept alive for the GC

    public HotkeyMonitor()
    {
        _hookProc = HookCallback;
        _tapTimer = new System.Windows.Forms.Timer { Interval = DoubleTapWindowMs };
        _tapTimer.Tick += (_, _) =>
        {
            _tapTimer.Stop();
            if (_state == State.TapPending)
            {
                _state = State.Idle;
                Stopped?.Invoke(false); // lone short tap: cancel
                ScheduleToggleFix();
            }
        };
        _toggleFixTimer = new System.Windows.Forms.Timer { Interval = 150 };
        _toggleFixTimer.Tick += (_, _) =>
        {
            _toggleFixTimer.Stop();
            if (IsToggleKey && ToggleState != _toggleSnapshot)
                InjectMarkedKeyPress(); // flip the toggle back to its pre-session state
        };
    }

    /// <summary>
    /// Suppressing a toggle key via the hook doesn't reliably stop the OS toggle state
    /// from flipping. After each dictation session, restore it to the pre-session value.
    /// </summary>
    private void ScheduleToggleFix()
    {
        if (!IsToggleKey || !SuppressKey) return;
        _toggleFixTimer.Stop();
        _toggleFixTimer.Start();
    }

    private void InjectMarkedKeyPress()
    {
        var inputs = new Native.INPUT[2];
        inputs[0] = Native.MakeKey((ushort)VirtualKey, 0, SelfInjectionMarker);
        inputs[1] = Native.MakeKey((ushort)VirtualKey, Native.KEYEVENTF_KEYUP, SelfInjectionMarker);
        Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Native.INPUT>());
    }

    /// <summary>Install the hook. Must be called from a thread with a message loop (the UI thread).</summary>
    public void Start()
    {
        if (_hookHandle != IntPtr.Zero) return;
        _hookHandle = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _hookProc,
            Native.GetModuleHandle(null), 0);
        if (_hookHandle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowsHookEx failed");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            // Our own corrective injections pass through untouched.
            if (info.dwExtraInfo == (IntPtr)SelfInjectionMarker)
                return Native.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            // Shift+hotkey passes through untouched (e.g. Shift+CapsLock still toggles caps).
            if (info.vkCode == VirtualKey && (Native.GetKeyState(0x10 /*VK_SHIFT*/) & 0x8000) == 0)
            {
                int msg = (int)wParam;
                // Exceptions must never escape a low-level hook callback — they kill the process.
                try
                {
                    if (msg is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN)
                    {
                        if (!_keyIsDown) // ignore auto-repeat
                        {
                            _keyIsDown = true;
                            OnKeyDown();
                        }
                    }
                    else if (msg is Native.WM_KEYUP or Native.WM_SYSKEYUP)
                    {
                        _keyIsDown = false;
                        OnKeyUp();
                    }
                }
                catch
                {
                    _state = State.Idle; // reset so a failed handler can't wedge the state machine
                }
                if (SuppressKey) return (IntPtr)1; // eat the key: don't toggle caps / reach other apps
            }
        }
        return Native.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void OnKeyDown()
    {
        switch (_state)
        {
            case State.Idle:
                _downTimestamp = Stopwatch.GetTimestamp();
                _toggleSnapshot = IsToggleKey && ToggleState; // pre-session toggle state
                _state = State.PushHeld;
                Started?.Invoke(ListeningMode.PushToTalk);
                break;

            case State.TapPending: // second tap -> hands-free confirmed
                _tapTimer.Stop();
                _state = State.HandsFree;
                Started?.Invoke(ListeningMode.HandsFree); // upgrade notification
                break;

            case State.HandsFree: // any press while hands-free begins the stop
                _state = State.StoppingHeld;
                break;
        }
    }

    private void OnKeyUp()
    {
        switch (_state)
        {
            case State.PushHeld:
                double heldMs = Stopwatch.GetElapsedTime(_downTimestamp).TotalMilliseconds;
                if (heldMs >= TapThresholdMs)
                {
                    _state = State.Idle;
                    Stopped?.Invoke(true); // push-to-talk commit
                    ScheduleToggleFix();
                }
                else
                {
                    _state = State.TapPending; // might be first half of a double-tap
                    _tapTimer.Start();
                }
                break;

            case State.StoppingHeld:
                _state = State.Idle;
                Stopped?.Invoke(true); // hands-free commit
                ScheduleToggleFix();
                break;

            // HandsFree: release of the second tap — stay listening.
        }
    }

    public void Dispose()
    {
        _tapTimer.Dispose();
        _toggleFixTimer.Dispose();
        if (_hookHandle != IntPtr.Zero)
        {
            Native.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private static class Native
    {
        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public int vkCode;
            public int scanCode;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int nVirtKey);

        public const uint KEYEVENTF_KEYUP = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public KEYBDINPUT ki;
            private readonly ulong _padding; // pad to the size of the full INPUT union (MOUSEINPUT is larger)
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public static INPUT MakeKey(ushort vk, uint flags, int extraInfo) => new()
        {
            type = 1, // INPUT_KEYBOARD
            ki = new KEYBDINPUT { wVk = vk, dwFlags = flags, dwExtraInfo = (IntPtr)extraInfo },
        };

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    }
}
