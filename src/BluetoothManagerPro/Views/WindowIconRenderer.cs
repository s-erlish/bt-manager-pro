using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BluetoothManagerPro.Views;

/// <summary>
/// Draws the window icon — the one Windows shows on the taskbar button and in Alt+Tab.
///
/// Separate from the tray icon: that one is a GDI+ <c>Icon</c> for the shell, this one is a
/// WPF <c>ImageSource</c> for <see cref="Window.Icon"/>. Same rune, same accent, so the app
/// carries its theme wherever Windows shows it.
/// </summary>
internal static class WindowIconRenderer
{
    private const double Grid = 24.0;
    private const double StrokeUnits = 2.4;

    /// <summary>The rune, in the same 24x24 coordinates the rest of the interface uses.</summary>
    private static readonly Geometry Rune =
        Geometry.Parse("M6.5,6.5 L17.5,17.5 L12,23 L12,1 L17.5,6.5 L6.5,17.5");

    /// <summary>Renders the icon at <paramref name="size"/> pixels square.</summary>
    public static ImageSource Render(Color color, int size = 64)
    {
        var pen = new Pen(new SolidColorBrush(color), StrokeUnits)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };

        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            // The pen is described in grid units, so scaling the context scales the stroke
            // with the geometry and the outline stays proportional at any size.
            context.PushTransform(new ScaleTransform(size / Grid, size / Grid));
            context.DrawGeometry(null, pen, Rune);
            context.Pop();
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        // Frozen so it can be handed to the window from anywhere without thread affinity.
        bitmap.Freeze();
        return bitmap;
    }
}
