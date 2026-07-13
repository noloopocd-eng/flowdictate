using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FlowDictate.Core;

/// <summary>Identifies the app that will receive the dictated text.</summary>
public static class ForegroundApp
{
    public static string GetProcessName()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return "";
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return "";
            return Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant();
        }
        catch
        {
            return "";
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
