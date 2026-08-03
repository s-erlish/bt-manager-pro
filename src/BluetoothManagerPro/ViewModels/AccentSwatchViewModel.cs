using System;
using System.Windows.Media;
using BluetoothManagerPro.Infrastructure;
using BluetoothManagerPro.Models;

namespace BluetoothManagerPro.ViewModels;

/// <summary>One selectable accent in the theme picker.</summary>
public sealed class AccentSwatchViewModel : ObservableObject
{
    private readonly Action<AccentPreset> _select;
    private SolidColorBrush _swatch;
    private bool _isSelected;

    public AccentSwatchViewModel(AccentPreset preset, Action<AccentPreset> select)
    {
        Preset = preset;
        _select = select;
        _swatch = new SolidColorBrush(preset.Seed);
    }

    public AccentPreset Preset { get; }

    public string Name => Preset.Name;

    /// <summary>
    /// The colour as this accent would actually render in the current mode, not the raw
    /// seed — so the dots preview what picking one really does.
    /// </summary>
    public SolidColorBrush Swatch
    {
        get => _swatch;
        private set => SetProperty(ref _swatch, value);
    }

    /// <summary>
    /// Two-way bound to the swatch's IsChecked. Selecting is what applies the theme;
    /// deselection arrives on its own when a sibling in the group is picked.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!SetProperty(ref _isSelected, value) || !value)
            {
                return;
            }

            _select(Preset);
        }
    }

    /// <summary>
    /// Repaints the preview after a light/dark switch. The brush is replaced rather than
    /// recoloured for the same reason the palette is: a frozen brush ignores mutation and
    /// fails silently.
    /// </summary>
    public void Preview(ThemeMode mode)
        => Swatch = new SolidColorBrush(Services.ThemeService.SwatchFor(Preset, mode));

    internal void SetSelectedQuietly(bool value) => SetProperty(ref _isSelected, value, nameof(IsSelected));
}
