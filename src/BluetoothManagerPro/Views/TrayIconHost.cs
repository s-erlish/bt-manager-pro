using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Threading;
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
    private readonly DispatcherTimer _clickTimer;

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

        // A double click always fires the single-click event first, so the single-click
        // action waits out the system's double-click window before committing.
        _clickTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(SystemInformation.DoubleClickTime + 40),
        };
        _clickTimer.Tick += OnClickTimerElapsed;

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
                ? TrayIconRenderer.ToDrawing(_theme.TrayAccentColor)
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

    private void OnIconClicked(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            // Right click has no double-click meaning, so it can act immediately.
            ShowFlyout();
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            _clickTimer.Stop();
            _clickTimer.Start();
        }
    }

    private void OnIconDoubleClicked(object? sender, MouseEventArgs e)
    {
        _clickTimer.Stop();
        _flyout?.Hide();
        ShowWindowRequested?.Invoke();
    }

    private void OnClickTimerElapsed(object? sender, EventArgs e)
    {
        _clickTimer.Stop();

        // With the window up, a single click puts it away; otherwise it opens the flyout.
        if (IsMainWindowVisible?.Invoke() == true)
        {
            HideWindowRequested?.Invoke();
            return;
        }

        ShowFlyout();
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
        _clickTimer.Stop();
        _clickTimer.Tick -= OnClickTimerElapsed;
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
