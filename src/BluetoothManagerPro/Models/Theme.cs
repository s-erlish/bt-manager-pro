using System.Collections.Generic;
using System.Windows.Media;

namespace BluetoothManagerPro.Models;

/// <summary>Dark surfaces, or light surfaces tinted with the accent.</summary>
public enum ThemeMode
{
    Dark,
    Light,
}

/// <summary>
/// One selectable accent. The rest of the palette is derived from <see cref="Seed"/>,
/// so adding a theme means adding a line here and nothing else.
/// </summary>
public sealed record AccentPreset(string Id, string Name, Color Seed)
{
    /// <summary>
    /// Deliberately muted, slightly greyed hues spread around the wheel — saturated
    /// primaries would fight the interface rather than sit inside it.
    /// </summary>
    public static IReadOnlyList<AccentPreset> All { get; } = new[]
    {
        new AccentPreset("ochre", "Охра", Color.FromRgb(0xD0, 0x8A, 0x2E)),
        new AccentPreset("lavender", "Лаванда", Color.FromRgb(0xAE, 0x9B, 0xE0)),
        new AccentPreset("sage", "Шалфей", Color.FromRgb(0x8C, 0xAE, 0x93)),
        new AccentPreset("teal", "Морская волна", Color.FromRgb(0x5D, 0xA9, 0xA4)),
        new AccentPreset("rose", "Пыльная роза", Color.FromRgb(0xD0, 0x8F, 0xA0)),
        new AccentPreset("indigo", "Индиго", Color.FromRgb(0x80, 0x92, 0xDE)),
    };

    public const string DefaultId = "ochre";

    public static AccentPreset Resolve(string? id)
    {
        foreach (AccentPreset preset in All)
        {
            if (string.Equals(preset.Id, id, System.StringComparison.OrdinalIgnoreCase))
            {
                return preset;
            }
        }

        return All[0];
    }
}
