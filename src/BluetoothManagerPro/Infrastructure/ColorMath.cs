using System;
using System.Windows.Media;

namespace BluetoothManagerPro.Infrastructure;

/// <summary>
/// Colour helpers for building themes from a single accent.
///
/// Palettes here are computed rather than hand-picked, so every accent gets surfaces and
/// text that are guaranteed readable instead of hand-tuned per theme and hoped for. The
/// contrast maths is WCAG 2.1: relative luminance, then (L1 + 0.05) / (L2 + 0.05).
/// </summary>
public static class ColorMath
{
    /// <summary>Converts sRGB to hue (0..360), saturation and lightness (both 0..1).</summary>
    public static (double H, double S, double L) ToHsl(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double lightness = (max + min) / 2.0;

        if (Math.Abs(max - min) < 1e-9)
        {
            return (0, 0, lightness);
        }

        double delta = max - min;
        double saturation = lightness > 0.5 ? delta / (2.0 - max - min) : delta / (max + min);

        double hue;
        if (Math.Abs(max - r) < 1e-9)
        {
            hue = (g - b) / delta + (g < b ? 6 : 0);
        }
        else if (Math.Abs(max - g) < 1e-9)
        {
            hue = (b - r) / delta + 2;
        }
        else
        {
            hue = (r - g) / delta + 4;
        }

        return (hue * 60.0, saturation, lightness);
    }

    public static Color FromHsl(double hue, double saturation, double lightness)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        lightness = Math.Clamp(lightness, 0, 1);

        if (saturation < 1e-9)
        {
            byte grey = ToByte(lightness);
            return Color.FromRgb(grey, grey, grey);
        }

        double q = lightness < 0.5
            ? lightness * (1 + saturation)
            : lightness + saturation - lightness * saturation;
        double p = 2 * lightness - q;
        double h = hue / 360.0;

        return Color.FromRgb(
            ToByte(HueToChannel(p, q, h + 1.0 / 3.0)),
            ToByte(HueToChannel(p, q, h)),
            ToByte(HueToChannel(p, q, h - 1.0 / 3.0)));
    }

    private static double HueToChannel(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
        return p;
    }

    /// <summary>WCAG relative luminance.</summary>
    public static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            double c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    /// <summary>WCAG contrast ratio, 1.0 (identical) to 21.0 (black on white).</summary>
    public static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return la > lb ? (la + 0.05) / (lb + 0.05) : (lb + 0.05) / (la + 0.05);
    }

    /// <summary>
    /// Walks <paramref name="color"/>'s lightness until it clears <paramref name="target"/>
    /// contrast against <paramref name="background"/>, keeping its hue. This is what lets an
    /// arbitrary accent be dropped in without checking every combination by hand.
    /// </summary>
    public static Color EnsureContrast(Color color, Color background, double target)
    {
        if (Contrast(color, background) >= target)
        {
            return color;
        }

        (double hue, double saturation, double lightness) = ToHsl(color);

        // Move away from the background: lighten on dark, darken on light.
        double direction = Luminance(background) < 0.5 ? 1 : -1;

        for (int step = 1; step <= 100; step++)
        {
            double candidateLightness = lightness + direction * step * 0.01;
            if (candidateLightness is < 0 or > 1)
            {
                break;
            }

            Color candidate = FromHsl(hue, saturation, candidateLightness);
            if (Contrast(candidate, background) >= target)
            {
                return candidate;
            }
        }

        // Nothing in this hue reaches the target; fall back to plain black or white.
        return direction > 0 ? Colors.White : Colors.Black;
    }

    /// <summary>Blends two colours, <paramref name="amount"/> 0 gives <paramref name="from"/>.</summary>
    public static Color Mix(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(from.R + (to.R - from.R) * amount),
            (byte)Math.Round(from.G + (to.G - from.G) * amount),
            (byte)Math.Round(from.B + (to.B - from.B) * amount));
    }

    /// <summary>Rebuilds a colour with the given saturation and lightness, keeping its hue.</summary>
    public static Color WithSl(Color color, double saturation, double lightness)
    {
        (double hue, _, _) = ToHsl(color);
        return FromHsl(hue, saturation, lightness);
    }

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value * 255.0), 0, 255);
}
