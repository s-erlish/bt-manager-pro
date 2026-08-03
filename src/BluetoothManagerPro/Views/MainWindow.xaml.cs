using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using BluetoothManagerPro.Interop;
using BluetoothManagerPro.ViewModels;

namespace BluetoothManagerPro.Views;

public partial class MainWindow : Window
{
    /// <summary>Border colour handed to DWM, in 0x00BBGGRR — the ochre accent.</summary>
    private const int AccentBorderBgr = 0x002E8AD0;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // WindowStyle=None already removes the caption, but the frame DWM draws around
        // the window is still light by default on Windows 10.
        WindowNative.ApplyDarkFrame(new WindowInteropHelper(this).Handle, AccentBorderBgr);
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

        base.OnClosing(e);
        Application.Current.Shutdown();
    }
}
