using System.Drawing.Drawing2D;

namespace FlowDictate.UI;

/// <summary>Generates simple colored-dot tray icons at runtime (no asset files needed).</summary>
public static class TrayIcons
{
    public static Icon Idle { get; } = Make(Color.FromArgb(140, 140, 140));
    public static Icon Listening { get; } = Make(Color.FromArgb(232, 65, 66));
    public static Icon Processing { get; } = Make(Color.FromArgb(64, 132, 244));

    private static Icon Make(Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var fill = new SolidBrush(color);
            // A microphone-ish capsule + stand.
            g.FillEllipse(fill, 10, 3, 12, 18);
            using var pen = new Pen(color, 3);
            g.DrawArc(pen, 6, 10, 20, 14, 0, 180);
            g.DrawLine(pen, 16, 24, 16, 29);
            g.DrawLine(pen, 10, 29, 22, 29);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
