using System.Windows;
using System.Windows.Media;

namespace BluetoothManagerPro.Infrastructure;

/// <summary>
/// Lets one shared control template fade between per-style colours.
///
/// Hover and press states are drawn as overlay layers whose opacity animates, rather than
/// by swapping the background brush — a brush swap cannot be eased, so it snaps. The
/// overlay needs to know which colour to fade in, and that differs per button style, so
/// each style attaches its own here and the template binds to it.
/// </summary>
public static class ThemeProps
{
    public static readonly DependencyProperty HoverBrushProperty =
        DependencyProperty.RegisterAttached(
            "HoverBrush", typeof(Brush), typeof(ThemeProps), new PropertyMetadata(null));

    public static readonly DependencyProperty PressBrushProperty =
        DependencyProperty.RegisterAttached(
            "PressBrush", typeof(Brush), typeof(ThemeProps), new PropertyMetadata(null));

    /// <summary>Border colour faded in on hover, when a style wants one.</summary>
    public static readonly DependencyProperty HoverBorderBrushProperty =
        DependencyProperty.RegisterAttached(
            "HoverBorderBrush", typeof(Brush), typeof(ThemeProps), new PropertyMetadata(null));

    public static void SetHoverBrush(DependencyObject element, Brush value)
        => element.SetValue(HoverBrushProperty, value);

    public static Brush? GetHoverBrush(DependencyObject element)
        => (Brush?)element.GetValue(HoverBrushProperty);

    public static void SetPressBrush(DependencyObject element, Brush value)
        => element.SetValue(PressBrushProperty, value);

    public static Brush? GetPressBrush(DependencyObject element)
        => (Brush?)element.GetValue(PressBrushProperty);

    public static void SetHoverBorderBrush(DependencyObject element, Brush value)
        => element.SetValue(HoverBorderBrushProperty, value);

    public static Brush? GetHoverBorderBrush(DependencyObject element)
        => (Brush?)element.GetValue(HoverBorderBrushProperty);
}
