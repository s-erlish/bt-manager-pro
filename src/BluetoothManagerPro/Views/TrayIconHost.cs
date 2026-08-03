using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
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
    private readonly NotifyIcon _icon;
    private readonly MainViewModel _viewModel;
    private readonly Icon? _iconActive;
    private readonly Icon? _iconIdle;
    private readonly Icon? _iconOff;

    private TrayFlyoutWindow? _flyout;
    private bool _disposed;

    public TrayIconHost(MainViewModel viewModel)
    {
        _viewModel = viewModel;

        _iconActive = Load("tray-active.ico");
        _iconIdle = Load("tray-idle.ico");
        _iconOff = Load("tray-off.ico");

        _icon = new NotifyIcon
        {
            Icon = _iconIdle ?? SystemIcons.Application,
            Text = "Bluetooth Manager",
            Visible = true,
        };

        _icon.MouseClick += OnIconClicked;
        _icon.MouseDoubleClick += (_, _) => ShowWindowRequested?.Invoke();
        _viewModel.PropertyChanged += OnViewModelChanged;
    }

    public event Action? ShowWindowRequested;

    public event Action? ExitRequested;

    /// <summary>Re-reads the view model and updates the icon and tooltip.</summary>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        int connected = _viewModel.Devices.Count(d => d.IsConnected);

        _icon.Icon = !_viewModel.IsBluetoothOn && _viewModel.IsRadioAvailable
            ? _iconOff ?? SystemIcons.Application
            : connected > 0
                ? _iconActive ?? SystemIcons.Application
                : _iconIdle ?? SystemIcons.Application;

        string state = !_viewModel.IsBluetoothOn && _viewModel.IsRadioAvailable
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

    private void OnIconClicked(object? sender, MouseEventArgs e)
    {
        if (e.Button is not (MouseButtons.Left or MouseButtons.Right))
        {
            return;
        }

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

    /// <summary>Loads an icon that was compiled in as a WPF resource.</summary>
    private static Icon? Load(string fileName)
    {
        try
        {
            var uri = new Uri($"pack://application:,,,/Assets/{fileName}", UriKind.Absolute);
            System.Windows.Resources.StreamResourceInfo? info = System.Windows.Application.GetResourceStream(uri);
            if (info is null)
            {
                return null;
            }

            using System.IO.Stream stream = info.Stream;
            // Ask for the shell's small-icon size so the right frame is picked on high DPI.
            return new Icon(stream, SystemInformation.SmallIconSize);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Tray icon '{fileName}' could not be loaded: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _viewModel.PropertyChanged -= OnViewModelChanged;
        _icon.Visible = false;
        _icon.Dispose();
        _iconActive?.Dispose();
        _iconIdle?.Dispose();
        _iconOff?.Dispose();

        if (_flyout is not null)
        {
            _flyout.Close();
            _flyout = null;
        }
    }
}
