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
    /// <summary>
    /// Whether decoration may animate. Set once on each window root and inherited by
    /// everything inside, so lite mode reaches every template without any of them knowing
    /// where the setting lives.
    ///
    /// Templates pair each state trigger with a second one that carries only a Setter, so
    /// turning this off does not lose the state — it arrives without the fade.
    /// </summary>
    public static readonly DependencyProperty AnimatedProperty =
        DependencyProperty.RegisterAttached(
            "Animated",
            typeof(bool),
            typeof(ThemeProps),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>The mode in force, so a window opened later starts in it too.</summary>
    private static bool _animated = true;

    public static void SetAnimated(DependencyObject element, bool value)
        => element.SetValue(AnimatedProperty, value);

    public static bool GetAnimated(DependencyObject element)
        => (bool)element.GetValue(AnimatedProperty);

    /// <summary>
    /// Switches every open window into or out of lite mode, and records the choice for the
    /// windows that are not open yet.
    ///
    /// Property inheritance runs down one visual tree, and the tray flyout and the pairing
    /// dialog are trees of their own — so each window root is set individually rather than
    /// the app being asked to carry the value for all of them.
    /// </summary>
    public static void SetAnimatedGlobally(bool value)
    {
        _animated = value;

        if (Application.Current is not { } app)
        {
            return;
        }

        foreach (Window window in app.Windows)
        {
            SetAnimated(window, value);
        }
    }

    /// <summary>Starts a freshly built window in whatever mode is currently in force.</summary>
    public static void Adopt(Window window) => SetAnimated(window, _animated);

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

    /// <summary>
    /// Depth gradient laid over the fill. Left unset by default so buttons with no
    /// background — ghost and icon ones — do not get a gradient floating over nothing.
    /// </summary>
    public static readonly DependencyProperty ReliefBrushProperty =
        DependencyProperty.RegisterAttached(
            "ReliefBrush", typeof(Brush), typeof(ThemeProps), new PropertyMetadata(null));

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

    public static void SetReliefBrush(DependencyObject element, Brush value)
        => element.SetValue(ReliefBrushProperty, value);

    public static Brush? GetReliefBrush(DependencyObject element)
        => (Brush?)element.GetValue(ReliefBrushProperty);
}
