using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using BluetoothManagerPro.Services;
using BluetoothManagerPro.ViewModels;

namespace BluetoothManagerPro.Views;

/// <summary>
/// Owns the notification-area icon.
///
/// WinForms' <see cref="NotifyIcon"/> is used rather than a hand-rolled Shell_NotifyIcon
/// because it re-adds itself when Explorer restarts — otherwise the icon just vanishes.
/// The menu it would draw is not used; a themed WPF flyout is shown instead.
/// </summary>
public sealed class TrayIconHost : IDisposable
{
    /// <summary>Idle and disabled shades are fixed: the taskbar is not ours to theme.</summary>
    private static readonly Color IdleColor = Color.FromArgb(0xC8, 0xC8, 0xD0);
    private static readonly Color OffColor = Color.FromArgb(0x78, 0x78, 0x82);

    private readonly NotifyIcon _icon;
    private readonly MainViewModel _viewModel;
    private readonly ThemeService _theme;

    private TrayFlyoutWindow? _flyout;
    private Icon? _current;
    private bool _disposed;

    public TrayIconHost(MainViewModel viewModel, ThemeService theme)
    {
        _viewModel = viewModel;
        _theme = theme;

        _icon = new NotifyIcon
        {
            Text = "Bluetooth Manager",
            Visible = true,
        };

        _icon.MouseClick += OnIconClicked;
        _icon.MouseDoubleClick += OnIconDoubleClicked;
        _viewModel.PropertyChanged += OnViewModelChanged;
        _theme.Changed += Refresh;

        Refresh();
    }

    /// <summary>Asked when deciding what a single click should do.</summary>
    public Func<bool>? IsMainWindowVisible { get; set; }

    public event Action? ShowWindowRequested;

    public event Action? HideWindowRequested;

    public event Action? ExitRequested;

    /// <summary>Redraws the icon for the current theme and connection state.</summary>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        int connected = _viewModel.Devices.Count(d => d.IsConnected);
        bool radioOff = !_viewModel.IsBluetoothOn && _viewModel.IsRadioAvailable;

        Color color = radioOff
            ? OffColor
            : connected > 0
                ? TrayIconRenderer.ToDrawing(_theme.ShellAccentColor)
                : IdleColor;

        Icon rendered = TrayIconRenderer.Render(SystemInformation.SmallIconSize.Width, color, radioOff);
        _icon.Icon = rendered;

        // Swap first, then release the icon the shell was using.
        _current?.Dispose();
        _current = rendered;

        string state = radioOff
            ? "Bluetooth выключен"
            : connected == 0
                ? "Нет подключённых устройств"
                : $"Подключено: {connected}";

        // NotifyIcon.Text is capped at 63 characters by the shell.
        string text = $"Bluetooth Manager — {state}";
        _icon.Text = text.Length <= 63 ? text : text[..63];
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsBluetoothOn)
            or nameof(MainViewModel.IsRadioAvailable)
            or nameof(MainViewModel.QuickAccessDevices))
        {
            Refresh();
        }
    }

    // ---- Clicks -------------------------------------------------------------

    /// <summary>
    /// Every click acts at once.
    ///
    /// Telling a single click apart from the first half of a double click means waiting out
    /// the system's double-click interval, and half a second of nothing is exactly what a
    /// tray icon must not do. So the single-click action runs immediately and the double
    /// click simply supersedes it: with the window hidden, the flyout appears and is then
    /// dismissed as the window opens — a brief flash, in exchange for the whole thing
    /// feeling instant.
    /// </summary>
    private void OnIconClicked(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            ShowFlyout();
            return;
        }

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        // With the window up, a single click puts it away; otherwise it opens the flyout.
        if (IsMainWindowVisible?.Invoke() == true)
        {
            _flyout?.Hide();
            HideWindowRequested?.Invoke();
            return;
        }

        ShowFlyout();
    }

    private void OnIconDoubleClicked(object? sender, MouseEventArgs e)
    {
        _flyout?.Hide();
        ShowWindowRequested?.Invoke();
    }

    private void ShowFlyout()
    {
        _flyout ??= new TrayFlyoutWindow { DataContext = _viewModel };
        _flyout.OpenWindowRequested -= OnOpenWindowRequested;
        _flyout.ExitRequested -= OnExitRequested;
        _flyout.OpenWindowRequested += OnOpenWindowRequested;
        _flyout.ExitRequested += OnExitRequested;

        if (_flyout.IsVisible)
        {
            _flyout.Hide();
            return;
        }

        _flyout.ShowNearCursor();
    }

    private void OnOpenWindowRequested() => ShowWindowRequested?.Invoke();

    private void OnExitRequested() => ExitRequested?.Invoke();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _viewModel.PropertyChanged -= OnViewModelChanged;
        _theme.Changed -= Refresh;

        _icon.Visible = false;
        _icon.Dispose();
        _current?.Dispose();

        if (_flyout is not null)
        {
            _flyout.Close();
            _flyout = null;
        }
    }
}
