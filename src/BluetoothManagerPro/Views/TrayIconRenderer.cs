using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace BluetoothManagerPro.Views;

/// <summary>
/// Draws the notification-area icon at runtime.
///
/// It has to be drawn rather than loaded, because the icon takes the colour of whichever
/// theme the user picked. Same 24x24 rune the rest of the interface uses, stroked with
/// round caps so it stays legible down to 16 px.
///
/// The result is assembled into an .ico in memory and handed to <c>new Icon(Stream)</c>
/// rather than going through <c>GetHicon</c>. A handle-backed icon depends on exactly what
/// <c>Icon.Clone</c> copies and on the handle outliving the shell's use of it — questions
/// with no good answer — whereas an icon built from a stream owns managed data outright.
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

    /// <summary>Renders the rune. The caller owns the returned icon and must dispose it.</summary>
    public static Icon Render(int size, Color color, bool slashed)
    {
        size = Math.Clamp(size, 16, 128);

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

        using var stream = new MemoryStream();
        WriteIcon(stream, bitmap);
        stream.Position = 0;
        return new Icon(stream);
    }

    /// <summary>Converts a WPF colour to the GDI+ one the drawing surface expects.</summary>
    public static Color ToDrawing(System.Windows.Media.Color color)
        => Color.FromArgb(color.A, color.R, color.G, color.B);

    /// <summary>
    /// Writes a single-image .ico: directory, one entry, then a DIB holding a bottom-up
    /// BGRA bitmap followed by an all-zero AND mask (transparency comes from the alpha
    /// channel, so the mask is only there because the format demands it).
    /// </summary>
    private static void WriteIcon(Stream stream, Bitmap bitmap)
    {
        int size = bitmap.Width;
        byte[] pixels = ReadPixels(bitmap);

        int maskStride = (size + 31) / 32 * 4;
        int dibLength = 40 + (size * size * 4) + (maskStride * size);

        var writer = new BinaryWriter(stream);

        // ICONDIR
        writer.Write((ushort)0);            // reserved
        writer.Write((ushort)1);            // type: icon
        writer.Write((ushort)1);            // image count

        // ICONDIRENTRY — 0 means 256 in the byte-sized dimensions.
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)0);              // palette size
        writer.Write((byte)0);              // reserved
        writer.Write((ushort)1);            // colour planes
        writer.Write((ushort)32);           // bits per pixel
        writer.Write(dibLength);
        writer.Write(22);                   // offset: 6 + 16

        // BITMAPINFOHEADER — height is doubled to cover image plus mask.
        writer.Write(40);
        writer.Write(size);
        writer.Write(size * 2);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);                    // BI_RGB
        writer.Write(0);                    // image size, may be zero for BI_RGB
        writer.Write(0);                    // x pixels per metre
        writer.Write(0);                    // y pixels per metre
        writer.Write(0);                    // colours used
        writer.Write(0);                    // colours important

        // DIB rows run bottom-up.
        int stride = size * 4;
        for (int y = size - 1; y >= 0; y--)
        {
            writer.Write(pixels, y * stride, stride);
        }

        writer.Write(new byte[maskStride * size]);
        writer.Flush();
    }

    /// <summary>Copies the bitmap's BGRA bytes out as a tightly packed top-down buffer.</summary>
    private static byte[] ReadPixels(Bitmap bitmap)
    {
        var area = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = bitmap.Width * 4;
            var pixels = new byte[stride * bitmap.Height];
            for (int y = 0; y < bitmap.Height; y++)
            {
                // Stride can exceed the row width, so rows are copied one at a time.
                Marshal.Copy(data.Scan0 + (y * data.Stride), pixels, y * stride, stride);
            }

            return pixels;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
