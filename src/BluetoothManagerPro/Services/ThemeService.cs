using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BluetoothManagerPro.Infrastructure;
using BluetoothManagerPro.Models;

namespace BluetoothManagerPro.Services;

/// <summary>
/// Owns the live palette.
///
/// Every colour is derived from one accent seed plus a mode, rather than being written out
/// per theme, so a new accent is one line in <see cref="AccentPreset.All"/> and cannot ship
/// an unreadable combination — text and accent tokens are pushed until they clear WCAG AA
/// against the surface they sit on.
///
/// Applying a palette replaces the brush resources outright, which is the one mechanism
/// guaranteed to push new colours through every DynamicResource reference. The cross-fade
/// rides on top as an animation from the previous colour, so the visual polish can never
/// cost correctness.
/// </summary>
public sealed class ThemeService
{
    private static readonly Duration TransitionDuration = new(TimeSpan.FromMilliseconds(520));

    private readonly ResourceDictionary _resources;

    /// <summary>Colour each key currently shows, so a change can be faded from it.</summary>
    private readonly Dictionary<string, Color> _current = new(StringComparer.Ordinal);

    private bool _initialised;

    public ThemeService(ResourceDictionary resources) => _resources = resources;

    public AccentPreset Accent { get; private set; } = AccentPreset.All[0];

    public ThemeMode Mode { get; private set; } = ThemeMode.Dark;

    /// <summary>The accent as finally rendered — after any contrast correction.</summary>
    public Color AccentColor { get; private set; } = AccentPreset.All[0].Seed;

    /// <summary>
    /// Accent as it would render on a dark surface. The tray and taskbar icons sit on Windows
    /// chrome, not on our own background, so they must not follow the light theme down into
    /// a shade that disappears against dark chrome.
    /// </summary>
    public Color ShellAccentColor => SwatchFor(Accent, ThemeMode.Dark);

    /// <summary>Raised after the palette changes, so the tray icon can be redrawn.</summary>
    public event Action? Changed;

    /// <summary>Applies a palette. The first call snaps; later ones cross-fade.</summary>
    public void Apply(AccentPreset accent, ThemeMode mode)
    {
        Accent = accent;
        Mode = mode;

        Dictionary<string, Color> palette = Build(accent.Seed, mode);
        AccentColor = palette["Brush.Accent"];

        bool animate = _initialised;
        foreach ((string key, Color color) in palette)
        {
            // A fresh brush is put in the dictionary every time rather than the existing
            // one being recoloured. Mutating a shared brush only works while nothing has
            // frozen it, and a frozen brush fails silently — the palette simply stops
            // changing. Replacing the resource is what actually forces every
            // DynamicResource reference to re-resolve, and it cannot be defeated.
            var brush = new SolidColorBrush(color);
            _resources[key] = brush;

            // The fade is a bonus on top of an already-correct value: the brush starts at
            // its target, and the animation merely walks it there from the old colour with
            // FillBehavior.Stop, so if it never runs the colour is right anyway.
            //
            // IsFrozen is checked because animating an immutable brush throws. Whether the
            // dictionary froze it on insert is a WPF implementation detail; the palette
            // must not depend on the answer, so a frozen brush simply forgoes the fade.
            if (animate && !brush.IsFrozen
                && _current.TryGetValue(key, out Color previous) && previous != color)
            {
                brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
                {
                    From = previous,
                    To = color,
                    Duration = TransitionDuration,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.Stop,
                });
            }

            _current[key] = color;
        }

        if (!_initialised)
        {
            _resources["Brush.Surface.Transparent"] = new SolidColorBrush(Colors.Transparent);
            _initialised = true;
        }

        Changed?.Invoke();
    }

    /// <summary>Renders a preset's swatch colour without disturbing the live palette.</summary>
    public static Color SwatchFor(AccentPreset preset, ThemeMode mode)
        => Build(preset.Seed, mode)["Brush.Accent"];

    private static Dictionary<string, Color> Build(Color seed, ThemeMode mode)
        => mode == ThemeMode.Dark ? BuildDark(seed) : BuildLight(seed);

    private static Dictionary<string, Color> BuildDark(Color seed)
    {
        // Surfaces carry a trace of the accent's hue instead of being neutral grey — enough
        // that a lavender theme feels lavender everywhere, not just on its buttons.
        Color Surface(double lightness) => ColorMath.WithSl(seed, 0.09, lightness);

        Color baseSurface = Surface(0.075);
        Color raised = Surface(0.105);
        Color overlay = Surface(0.135);

        // Foregrounds here are light, so the hardest surface to read against is the
        // *lightest* one they are ever drawn on, not the window background.
        Color worst = overlay;

        Color accent = ColorMath.EnsureContrast(seed, worst, 4.5);
        (double hue, double saturation, double lightness) = ColorMath.ToHsl(accent);

        return new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            ["Brush.Surface.Base"] = baseSurface,
            ["Brush.Surface.Raised"] = raised,
            ["Brush.Surface.Overlay"] = overlay,
            ["Brush.Surface.Hover"] = Surface(0.175),
            ["Brush.Surface.Pressed"] = Surface(0.215),

            ["Brush.Border.Subtle"] = Surface(0.155),
            ["Brush.Border.Default"] = Surface(0.205),
            ["Brush.Border.Strong"] = Surface(0.285),

            ["Brush.Text.Primary"] = ColorMath.WithSl(seed, 0.05, 0.95),
            ["Brush.Text.Secondary"] = ColorMath.EnsureContrast(
                ColorMath.WithSl(seed, 0.07, 0.68), worst, 4.5),
            ["Brush.Text.Tertiary"] = ColorMath.EnsureContrast(
                ColorMath.WithSl(seed, 0.07, 0.56), worst, 4.5),
            ["Brush.Text.Disabled"] = ColorMath.WithSl(seed, 0.06, 0.38),
            ["Brush.Text.OnAccent"] = OnAccent(accent),

            ["Brush.Accent"] = accent,
            ["Brush.Accent.Hover"] = ColorMath.FromHsl(hue, saturation, Math.Min(1, lightness + 0.09)),
            ["Brush.Accent.Pressed"] = ColorMath.FromHsl(hue, saturation, Math.Max(0, lightness - 0.09)),
            ["Brush.Accent.Muted"] = ColorMath.Mix(overlay, accent, 0.22),
            ["Brush.Accent.Faint"] = ColorMath.Mix(baseSurface, accent, 0.10),

            ["Brush.Positive"] = ColorMath.EnsureContrast(ColorMath.FromHsl(152, 0.47, 0.47), worst, 4.5),
            ["Brush.Negative"] = ColorMath.EnsureContrast(ColorMath.FromHsl(4, 0.68, 0.61), worst, 4.5),
            ["Brush.Negative.Muted"] = ColorMath.Mix(overlay, ColorMath.FromHsl(4, 0.68, 0.61), 0.24),
            ["Brush.Warning"] = ColorMath.EnsureContrast(ColorMath.FromHsl(42, 0.72, 0.57), worst, 4.5),
        };
    }

    private static Dictionary<string, Color> BuildLight(Color seed)
    {
        // Not white. The page is a washed-out version of the accent, which keeps the light
        // mode calm and stops it glaring the way a plain white panel does at night.
        Color baseSurface = ColorMath.WithSl(seed, 0.34, 0.935);
        Color raised = ColorMath.WithSl(seed, 0.42, 0.982);
        Color overlay = ColorMath.WithSl(seed, 0.32, 0.902);

        // Mirror image of the dark case: foregrounds are dark, so the page background —
        // the darkest surface they land on — is what they have to clear.
        Color accent = ColorMath.EnsureContrast(seed, baseSurface, 4.5);
        (double hue, double saturation, double lightness) = ColorMath.ToHsl(accent);

        return new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            ["Brush.Surface.Base"] = baseSurface,
            ["Brush.Surface.Raised"] = raised,
            ["Brush.Surface.Overlay"] = overlay,
            ["Brush.Surface.Hover"] = ColorMath.WithSl(seed, 0.30, 0.868),
            ["Brush.Surface.Pressed"] = ColorMath.WithSl(seed, 0.28, 0.826),

            ["Brush.Border.Subtle"] = ColorMath.WithSl(seed, 0.26, 0.878),
            ["Brush.Border.Default"] = ColorMath.WithSl(seed, 0.24, 0.818),
            ["Brush.Border.Strong"] = ColorMath.WithSl(seed, 0.22, 0.700),

            ["Brush.Text.Primary"] = ColorMath.WithSl(seed, 0.32, 0.130),
            ["Brush.Text.Secondary"] = ColorMath.EnsureContrast(
                ColorMath.WithSl(seed, 0.22, 0.395), baseSurface, 4.5),
            ["Brush.Text.Tertiary"] = ColorMath.EnsureContrast(
                ColorMath.WithSl(seed, 0.20, 0.470), baseSurface, 4.5),
            ["Brush.Text.Disabled"] = ColorMath.WithSl(seed, 0.16, 0.630),
            ["Brush.Text.OnAccent"] = OnAccent(accent),

            ["Brush.Accent"] = accent,
            ["Brush.Accent.Hover"] = ColorMath.FromHsl(hue, saturation, Math.Max(0, lightness - 0.08)),
            ["Brush.Accent.Pressed"] = ColorMath.FromHsl(hue, saturation, Math.Max(0, lightness - 0.16)),
            ["Brush.Accent.Muted"] = ColorMath.Mix(raised, accent, 0.16),
            ["Brush.Accent.Faint"] = ColorMath.Mix(baseSurface, accent, 0.08),

            ["Brush.Positive"] = ColorMath.EnsureContrast(ColorMath.FromHsl(152, 0.52, 0.34), baseSurface, 4.5),
            ["Brush.Negative"] = ColorMath.EnsureContrast(ColorMath.FromHsl(4, 0.62, 0.44), baseSurface, 4.5),
            ["Brush.Negative.Muted"] = ColorMath.Mix(raised, ColorMath.FromHsl(4, 0.62, 0.44), 0.14),
            ["Brush.Warning"] = ColorMath.EnsureContrast(ColorMath.FromHsl(38, 0.68, 0.38), baseSurface, 4.5),
        };
    }

    /// <summary>
    /// Text that sits on top of the accent fill. Whichever of a near-black or near-white
    /// tint reads better wins, so a pale lavender gets dark text and a deep indigo light.
    /// </summary>
    private static Color OnAccent(Color accent)
    {
        Color dark = ColorMath.WithSl(accent, 0.36, 0.075);
        Color light = ColorMath.WithSl(accent, 0.16, 0.975);
        Color best = ColorMath.Contrast(dark, accent) >= ColorMath.Contrast(light, accent) ? dark : light;

        // Picking the better of two candidates is not the same as clearing the bar.
        return ColorMath.EnsureContrast(best, accent, 4.5);
    }
}
