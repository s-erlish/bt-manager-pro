using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace BluetoothManagerPro.Views;

/// <summary>
/// Draws the notification-area icon at runtime.
///
/// It has to be drawn rather than loaded, because the icon takes the colour of whichever
/// theme the user picked. Same 24x24 rune the rest of the interface uses, stroked with
/// round caps so it stays legible down to 16 px.
/// </summary>
internal static class TrayIconRenderer
{
    private const float Grid = 24f;
    private const float StrokeUnits = 2.4f;

    private static readonly PointF[] Rune =
    {
        new(6.5f, 6.5f),
        new(17.5f, 17.5f),
        new(12f, 23f),
        new(12f, 1f),
        new(17.5f, 6.5f),
        new(6.5f, 17.5f),
    };

    private static readonly PointF SlashFrom = new(3.5f, 20.5f);
    private static readonly PointF SlashTo = new(20.5f, 3.5f);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>Renders the rune. The caller owns the returned icon and must dispose it.</summary>
    public static Icon Render(int size, Color color, bool slashed)
    {
        size = Math.Max(16, size);

        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            float scale = size / Grid;
            using var pen = new Pen(color, StrokeUnits * scale)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };

            var points = new PointF[Rune.Length];
            for (int i = 0; i < Rune.Length; i++)
            {
                points[i] = new PointF(Rune[i].X * scale, Rune[i].Y * scale);
            }

            graphics.DrawLines(pen, points);

            if (slashed)
            {
                graphics.DrawLine(pen,
                    SlashFrom.X * scale, SlashFrom.Y * scale,
                    SlashTo.X * scale, SlashTo.Y * scale);
            }
        }

        // GetHicon hands back a handle this process owns; Icon.FromHandle does not take
        // ownership, so the icon is cloned into managed data and the handle released.
        IntPtr handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    /// <summary>Converts a WPF colour to the GDI+ one the drawing surface expects.</summary>
    public static Color ToDrawing(System.Windows.Media.Color color)
        => Color.FromArgb(color.A, color.R, color.G, color.B);
}
