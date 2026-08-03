using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using BluetoothManagerPro.Interop;
using BluetoothManagerPro.Services;
using BluetoothManagerPro.ViewModels;

namespace BluetoothManagerPro.Views;

public partial class MainWindow : Window
{
    private ThemeService? _theme;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    /// <summary>Lets the window frame follow the palette, which WPF cannot style itself.</summary>
    public void UseTheme(ThemeService theme)
    {
        _theme = theme;
        theme.Changed += ApplyFrame;
    }

    private void OnSourceInitialized(object? sender, EventArgs e) => ApplyFrame();

    private void ApplyFrame()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        // WindowStyle=None already removes the caption, but the frame DWM draws around the
        // window is still light by default on Windows 10 — and its border colour is a
        // Win32 attribute, so it has to be pushed on every theme change.
        Color accent = _theme?.AccentColor ?? Colors.Gray;
        int bgr = accent.B << 16 | accent.G << 8 | accent.R;
        WindowNative.ApplyDarkFrame(handle, bgr, _theme?.Mode != Models.ThemeMode.Light);
    }

    private void OnMinimiseClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        // The app lives in the tray; closing the window is "put it away", not "quit",
        // unless the user turned that off in settings.
        if (DataContext is MainViewModel { CloseToTray: true })
        {
            e.Cancel = true;
            Hide();
            return;
        }

        if (_theme is not null)
        {
            _theme.Changed -= ApplyFrame;
        }

        base.OnClosing(e);
        Application.Current.Shutdown();
    }
}
