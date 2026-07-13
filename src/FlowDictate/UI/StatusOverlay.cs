using System.Drawing.Drawing2D;

namespace FlowDictate.UI;

/// <summary>
/// Small floating status pill shown near the bottom-center of the screen while
/// dictating. Never activates or takes focus (that would break text insertion).
/// </summary>
public sealed class StatusOverlay : Form
{
    private enum Phase { Hidden, Listening, Processing, Done }

    private Phase _phase = Phase.Hidden;
    private string _text = "";
    private Color _dotColor = Color.Gray;
    private bool _dotVisible = true;
    private readonly System.Windows.Forms.Timer _pulseTimer;   // blinks the dot while listening
    private readonly System.Windows.Forms.Timer _autoHideTimer;

    public StatusOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(28, 28, 30);
        Size = new Size(170, 40);
        DoubleBuffered = true;

        _pulseTimer = new System.Windows.Forms.Timer { Interval = 450 };
        _pulseTimer.Tick += (_, _) => { _dotVisible = !_dotVisible; Invalidate(); };

        _autoHideTimer = new System.Windows.Forms.Timer { Interval = 1300 };
        _autoHideTimer.Tick += (_, _) => { _autoHideTimer.Stop(); HideOverlay(); };
    }

    /// <summary>Never activate — the target app must keep keyboard focus.</summary>
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 /*WS_EX_NOACTIVATE*/ | 0x80 /*WS_EX_TOOLWINDOW*/ | 0x20 /*WS_EX_TRANSPARENT: click-through*/;
            return cp;
        }
    }

    public void ShowListening(bool handsFree)
    {
        SetState(Phase.Listening, handsFree ? "Listening (hands-free)" : "Listening…", Color.FromArgb(240, 70, 70));
        _pulseTimer.Start();
    }

    public void ShowProcessing()
    {
        _pulseTimer.Stop();
        _dotVisible = true;
        SetState(Phase.Processing, "Processing…", Color.FromArgb(70, 140, 245));
    }

    /// <summary>Brief confirmation, then auto-hide. Empty text = nothing inserted.</summary>
    public void ShowDone(bool inserted)
    {
        _pulseTimer.Stop();
        _dotVisible = true;
        SetState(Phase.Done, inserted ? "Inserted ✓" : "Nothing heard", inserted ? Color.FromArgb(60, 190, 100) : Color.Gray);
        _autoHideTimer.Stop();
        _autoHideTimer.Start();
    }

    public void HideOverlay()
    {
        _pulseTimer.Stop();
        _autoHideTimer.Stop();
        _phase = Phase.Hidden;
        Hide();
    }

    private void SetState(Phase phase, string text, Color dot)
    {
        _phase = phase;
        _text = text;
        _dotColor = dot;

        using (var g = CreateGraphics())
        {
            int textWidth = (int)g.MeasureString(_text, Font).Width;
            Width = Math.Max(120, textWidth + 56);
        }

        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1040);
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Bottom - Height - 24);

        // Rounded pill shape.
        using var path = new GraphicsPath();
        int r = Height;
        path.AddArc(0, 0, r, r, 90, 180);
        path.AddArc(Width - r, 0, r, r, 270, 180);
        path.CloseFigure();
        Region = new Region(path);

        if (!Visible) Show();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (_dotVisible)
        {
            using var dot = new SolidBrush(_dotColor);
            g.FillEllipse(dot, 16, Height / 2 - 6, 12, 12);
        }
        using var textBrush = new SolidBrush(Color.Gainsboro);
        g.DrawString(_text, Font, textBrush, 38, Height / 2 - Font.Height / 2);
    }
}
