using System.IO;

namespace FlowDictate.UI;

/// <summary>Simple log window for testing each pipeline stage.</summary>
public sealed class DebugWindow : Form
{
    private readonly TextBox _log;

    public DebugWindow()
    {
        Text = "FlowDictate — Debug Log";
        Width = 720;
        Height = 480;
        StartPosition = FormStartPosition.CenterScreen;

        _log = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 10f),
            BackColor = Color.FromArgb(18, 18, 18),
            ForeColor = Color.Gainsboro,
        };
        Controls.Add(_log);
    }

    private static readonly string LogFilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlowDictate", "log.txt");

    public void Log(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
            File.AppendAllText(LogFilePath, line + Environment.NewLine);
        }
        catch { /* file log is best-effort */ }

        if (IsDisposed) return;
        void Append() => _log.AppendText(line + Environment.NewLine);
        if (InvokeRequired) BeginInvoke(Append); else Append();
    }

    // Hide instead of dispose so the tray app can re-show it.
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }
}
