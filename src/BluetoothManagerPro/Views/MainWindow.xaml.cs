using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BluetoothManagerPro.Infrastructure;
using BluetoothManagerPro.Interop;
using BluetoothManagerPro.Services;
using BluetoothManagerPro.ViewModels;

namespace BluetoothManagerPro.Views;

public partial class MainWindow : Window
{
    /// <summary>
    /// Half of a tab switch: the content fades out over this, then back in over the same
    /// again. The scan bar's journey is timed against it — see <see cref="SlideScanBar"/>.
    /// </summary>
    private static readonly TimeSpan FadeHalf = TimeSpan.FromMilliseconds(500);

    /// <summary>Used until the layout has been measured once.</summary>
    private const double ScanHomeFallback = 168;

    private readonly TranslateTransform _scanShift = new();

    private ThemeService? _theme;
    private MainViewModel? _model;
    private DispatcherTimer? _paneSwap;

    private double _scanHome = ScanHomeFallback;
    private bool _showingDevices = true;
    private bool _switching;
    private bool _measuring;

    public MainWindow()
    {
        InitializeComponent();

        // Built here rather than in XAML so there is no question of it being frozen.
        _scanShift.Y = ScanHomeFallback;
        ScanOverlay.RenderTransform = _scanShift;

        SourceInitialized += (_, _) => ApplyFrame();
        DataContextChanged += OnDataContextChanged;

        // Re-measure only when something that could have moved the bar has happened; see
        // OnLayoutUpdated for why this is not simply left subscribed.
        Loaded += (_, _) => BeginMeasuring();
        SizeChanged += (_, _) => BeginMeasuring();
        ScanSpacer.IsVisibleChanged += (_, _) => BeginMeasuring();
    }

    /// <summary>Lets the window frame and icon follow the palette, which WPF cannot style itself.</summary>
    public void UseTheme(ThemeService theme)
    {
        _theme = theme;
        theme.Changed += ApplyFrame;
        ApplyFrame();
    }

    private void ApplyFrame()
    {
        Color accent = _theme?.ShellAccentColor ?? Colors.Gray;

        // The taskbar button, Alt+Tab and the window list all draw this.
        Icon = WindowIconRenderer.Render(accent);

        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        // WindowStyle=None already removes the caption, but the frame DWM draws around the
        // window is still light by default on Windows 10 — and its border colour is a
        // Win32 attribute, so it has to be pushed on every theme change.
        int bgr = accent.B << 16 | accent.G << 8 | accent.R;
        WindowNative.ApplyDarkFrame(handle, bgr, _theme?.Mode != Models.ThemeMode.Light);
    }

    // ---- Tab choreography ---------------------------------------------------

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_model is not null)
        {
            _model.PropertyChanged -= OnModelChanged;
        }

        _model = e.NewValue as MainViewModel;
        if (_model is not null)
        {
            _model.PropertyChanged += OnModelChanged;
            _showingDevices = _model.IsDevicesTab;
            ShowPane(_showingDevices);
            ApplyAppearance(_model.IsAnimated);
        }
    }

    /// <summary>
    /// Pushes lite mode out to every window.
    ///
    /// This window is set directly as well as through the sweep: the DataContext arrives
    /// during construction, before the Application knows the window exists, so the sweep on
    /// its own would set every window except the one asking.
    /// </summary>
    private void ApplyAppearance(bool animated)
    {
        ThemeProps.SetAnimated(this, animated);
        ThemeProps.SetAnimatedGlobally(animated);
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsDevicesTab):
            case nameof(MainViewModel.IsSettingsTab):
                StartTabSwitch();
                break;

            case nameof(MainViewModel.IsScanning):
                // A scan starting mid-tab has no journey to make; it just appears in place.
                if (!_switching)
                {
                    SlideScanBar(_showingDevices, animate: false);
                }

                break;

            case nameof(MainViewModel.IsAnimated):
                ApplyAppearance(_model!.IsAnimated);

                // The bar's position is carried by an animation that holds its end value.
                // Re-seating it means a mode change caught mid-journey cannot strand it.
                SlideScanBar(_showingDevices, animate: false);
                break;
        }
    }

    /// <summary>
    /// Dissolves one pane into the other while the scan bar travels across the window.
    ///
    /// The timing is the point. The content fades out over <see cref="FadeHalf"/> and back
    /// in over the same again, while the bar travels across the entire second — so it is in
    /// motion the whole time the switch lasts, and because the easing is symmetric it stands
    /// exactly midway along its path at the instant the window is empty.
    /// </summary>
    private void StartTabSwitch()
    {
        if (_model is null || _model.IsDevicesTab == _showingDevices)
        {
            return;
        }

        _showingDevices = _model.IsDevicesTab;

        if (!_model.IsAnimated)
        {
            // Lite mode: the panes simply exchange places. Any fade left over from a switch
            // that was under way is released first, or the host would stay part-faded.
            ContentHost.BeginAnimation(OpacityProperty, null);
            _paneSwap?.Stop();
            _paneSwap = null;
            _switching = false;
            ShowPane(_showingDevices);
            SlideScanBar(_showingDevices, animate: false);
            return;
        }

        _switching = true;

        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var fade = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(FadeHalf), ease));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(FadeHalf + FadeHalf), ease));
        fade.Completed += (_, _) => _switching = false;
        ContentHost.BeginAnimation(OpacityProperty, fade);

        // Swap at the trough, where the content is invisible and the cut cannot be seen.
        _paneSwap?.Stop();
        _paneSwap = new DispatcherTimer(FadeHalf, DispatcherPriority.Normal, OnPaneSwap, Dispatcher);

        SlideScanBar(_showingDevices, animate: true);
    }

    private void OnPaneSwap(object? sender, EventArgs e)
    {
        _paneSwap?.Stop();
        _paneSwap = null;
        ShowPane(_showingDevices);
    }

    private void ShowPane(bool devices)
    {
        DevicesPane.Visibility = devices ? Visibility.Visible : Visibility.Collapsed;
        SettingsPane.Visibility = devices ? Visibility.Collapsed : Visibility.Visible;

        if (devices)
        {
            BeginMeasuring();
        }
    }

    private void SlideScanBar(bool devices, bool animate)
    {
        double target = devices ? _scanHome : ScanTopY;

        if (!animate)
        {
            // Clearing the animation first, or the assignment below is ignored.
            _scanShift.BeginAnimation(TranslateTransform.YProperty, null);
            _scanShift.Y = target;
            return;
        }

        _scanShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation
        {
            To = target,
            Duration = new Duration(FadeHalf + FadeHalf),
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.HoldEnd,
        });
    }

    /// <summary>Where the bar rests on the settings tab: tucked just under the title bar.</summary>
    private double ScanTopY => TitleBar.ActualHeight + 6;

    /// <summary>
    /// Subscribes to layout passes until the bar's resting position has been measured once.
    ///
    /// LayoutUpdated fires for every layout pass anywhere in the window, and the handler
    /// walks a transform up the visual tree, so leaving it subscribed put that work on a
    /// path that runs constantly. It is armed instead by the three things that can actually
    /// move the strip — the window loading, the window resizing, the strip appearing — and
    /// releases itself the moment it has an answer.
    /// </summary>
    private void BeginMeasuring()
    {
        if (_measuring)
        {
            return;
        }

        _measuring = true;
        LayoutUpdated += OnLayoutUpdated;
    }

    private void EndMeasuring()
    {
        if (!_measuring)
        {
            return;
        }

        _measuring = false;
        LayoutUpdated -= OnLayoutUpdated;
    }

    /// <summary>
    /// Learns where the bar belongs on the devices tab by measuring the strip reserved for
    /// it, rather than hard-coding a figure that quietly rots when the layout above changes.
    /// </summary>
    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (_switching)
        {
            return;
        }

        if (!ScanSpacer.IsVisible || DevicesPane.Visibility != Visibility.Visible)
        {
            // Nothing to measure against right now; wait to be armed again.
            EndMeasuring();
            return;
        }

        try
        {
            Point anchor = ScanSpacer.TranslatePoint(new Point(0, ScanSpacer.ActualHeight - 2), this);
            EndMeasuring();

            if (Math.Abs(anchor.Y - _scanHome) < 0.5)
            {
                return;
            }

            _scanHome = anchor.Y;

            // A render transform does not invalidate layout, so this cannot loop.
            if (_showingDevices)
            {
                SlideScanBar(true, animate: false);
            }
        }
        catch (InvalidOperationException)
        {
            // The spacer is not connected to this window's visual tree yet — stay armed.
        }
    }

    // ---- Chrome -------------------------------------------------------------

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

        if (_model is not null)
        {
            _model.PropertyChanged -= OnModelChanged;
        }

        base.OnClosing(e);
        Application.Current.Shutdown();
    }
}
