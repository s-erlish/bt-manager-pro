using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BluetoothManagerPro.Infrastructure;
using BluetoothManagerPro.Models;
using BluetoothManagerPro.Services;
using BluetoothManagerPro.ViewModels;
using BluetoothManagerPro.Views;

namespace BluetoothManagerPro;

/// <summary>
/// Composition root. The object graph is small enough that wiring it by hand is clearer
/// than a container, and it keeps the app free of NuGet dependencies.
/// </summary>
public partial class App : Application
{
    private SingleInstance? _instance;
    private BluetoothDiscoveryService? _discovery;
    private RadioService? _radio;
    private MainViewModel? _viewModel;
    private TrayIconHost? _tray;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instance = SingleInstance.Acquire();
        if (!_instance.IsFirstInstance)
        {
            // Another copy is already in the tray; it will bring itself forward.
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var settingsStore = new SettingsService();
        AppSettings settings = settingsStore.Load();

        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        _discovery = new BluetoothDiscoveryService(dispatcher);
        _radio = new RadioService(dispatcher);

        _viewModel = new MainViewModel(
            _discovery,
            new ClassicBluetoothService(),
            _radio,
            new BatteryService(),
            new AutoStartService(),
            settingsStore,
            settings,
            dispatcher);

        _window = new MainWindow { DataContext = _viewModel };
        _viewModel.PairingPromptHandler = prompt => dispatcher.InvokeAsync(
            () => PairingDialog.AskAsync(prompt, _window)).Task.Unwrap();

        _tray = new TrayIconHost(_viewModel);
        _tray.ShowWindowRequested += ShowMainWindow;
        _tray.ExitRequested += () => Shutdown();

        _instance.ActivationRequested += () => dispatcher.InvokeAsync(ShowMainWindow);
        _instance.BeginListening();

        bool startHidden = e.Args.Any(a =>
            string.Equals(a, AutoStartService.TrayArgument, StringComparison.OrdinalIgnoreCase));

        if (!startHidden || !settings.StartMinimized)
        {
            ShowMainWindow();
        }

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        if (_viewModel is null)
        {
            return;
        }

        await _viewModel.InitializeAsync();
        _tray?.Refresh();
    }

    private void ShowMainWindow()
    {
        if (_window is null)
        {
            return;
        }

        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
        _window.Topmost = true;
        _window.Topmost = false;
        _window.Focus();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A failure in one device operation must not take the tray icon down with it.
        MessageBox.Show(
            e.Exception.Message,
            "Bluetooth Manager Pro",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _viewModel?.Dispose();
        _discovery?.Dispose();
        _radio?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
