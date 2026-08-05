using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BluetoothManagerPro.Infrastructure;
using BluetoothManagerPro.Interop;
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
    /// <summary>
    /// How long the window stays away before its memory is handed back. Long enough that
    /// flicking the window shut and open again does not pay for a collection.
    /// </summary>
    private static readonly TimeSpan TrimDelay = TimeSpan.FromSeconds(5);

    private SingleInstance? _instance;
    private ThemeService? _theme;
    private BluetoothDiscoveryService? _discovery;
    private RadioService? _radio;
    private MainViewModel? _viewModel;
    private TrayIconHost? _tray;
    private MainWindow? _window;
    private DispatcherTimer? _trim;

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

        // The palette has to exist before any window is built, since every surface binds
        // to it dynamically.
        _theme = new ThemeService(Resources);
        _theme.Apply(AccentPreset.Resolve(settings.AccentId), settings.ThemeMode);

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
            _theme,
            settings,
            dispatcher);

        _window = new MainWindow { DataContext = _viewModel };
        _window.UseTheme(_theme);
        _window.IsVisibleChanged += OnWindowVisibilityChanged;
        _viewModel.PairingPromptHandler = prompt => dispatcher.InvokeAsync(
            () => PairingDialog.AskAsync(prompt, _window)).Task.Unwrap();

        _tray = new TrayIconHost(_viewModel, _theme);
        _tray.IsMainWindowVisible = () => _window?.IsVisible == true;
        _tray.ShowWindowRequested += ShowMainWindow;
        _tray.HideWindowRequested += () => _window?.Hide();
        _tray.ExitRequested += () => Shutdown();

        _instance.ActivationRequested += () => dispatcher.InvokeAsync(ShowMainWindow);
        _instance.BeginListening();

        bool startHidden = e.Args.Any(a =>
            string.Equals(a, AutoStartService.TrayArgument, StringComparison.OrdinalIgnoreCase));

        if (!startHidden || !settings.StartMinimized)
        {
            ShowMainWindow();
        }
        else
        {
            // Started straight into the tray: the model has to be told, or it would poll at
            // the pace of a window that is on screen for the whole session.
            _viewModel.IsWindowVisible = false;
        }

        _ = InitializeAsync();
    }

    /// <summary>
    /// Follows the window between the screen and the tray, which is what decides how hard
    /// the app works, and hands its memory back a few seconds after it goes away.
    /// </summary>
    private void OnWindowVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        bool visible = _window?.IsVisible == true;
        if (_viewModel is not null)
        {
            _viewModel.IsWindowVisible = visible;
        }

        _trim?.Stop();
        _trim = null;

        if (visible)
        {
            return;
        }

        _trim = new DispatcherTimer(TrimDelay, DispatcherPriority.ApplicationIdle, (_, _) =>
        {
            _trim?.Stop();
            _trim = null;
            ReleaseMemory();
        }, Dispatcher);
    }

    /// <summary>
    /// Compacts the heap and returns the pages once the window is put away.
    ///
    /// A forced collection is normally the wrong instinct, but the shape here is the one it
    /// was made for: the interface has just been torn down, releasing everything WPF built
    /// for it, and the process is about to sit idle for hours. Left alone the app would hold
    /// the high-water mark of the last time its window was open for as long as it ran.
    /// </summary>
    private static void ReleaseMemory()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        WindowNative.TrimWorkingSet();
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
        _trim?.Stop();
        if (_window is not null)
        {
            _window.IsVisibleChanged -= OnWindowVisibilityChanged;
        }

        _tray?.Dispose();
        _viewModel?.Dispose();
        _discovery?.Dispose();
        _radio?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
