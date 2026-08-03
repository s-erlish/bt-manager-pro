using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BluetoothManagerPro.Interop;

namespace BluetoothManagerPro.Views;

/// <summary>
/// The tray menu. A real WPF window rather than a WinForms ContextMenuStrip, because the
/// strip cannot be themed to match the rest of the app without a custom renderer.
/// </summary>
public partial class TrayFlyoutWindow : Window
{
    public TrayFlyoutWindow()
    {
        InitializeComponent();
        Deactivated += (_, _) => Hide();
    }

    /// <summary>Requested by the host when the user picks "open the main window".</summary>
    public event Action? OpenWindowRequested;

    public event Action? ExitRequested;

    /// <summary>Pops the flyout into the working-area corner nearest the mouse.</summary>
    public void ShowNearCursor()
    {
        // Clear any held animation first, otherwise the assignment below is ignored.
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Show();
        UpdateLayout();

        System.Drawing.Point cursor = System.Windows.Forms.Control.MousePosition;
        System.Drawing.Rectangle work = System.Windows.Forms.Screen.FromPoint(cursor).WorkingArea;

        // Screen coordinates are physical pixels; WPF positions in DIPs.
        Matrix fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
                            ?? Matrix.Identity;
        Point topLeft = fromDevice.Transform(new Point(work.Left, work.Top));
        Point bottomRight = fromDevice.Transform(new Point(work.Right, work.Bottom));
        Point pointer = fromDevice.Transform(new Point(cursor.X, cursor.Y));

        bool right = pointer.X > (topLeft.X + bottomRight.X) / 2;
        bool bottom = pointer.Y > (topLeft.Y + bottomRight.Y) / 2;

        Left = right ? bottomRight.X - ActualWidth : topLeft.X;
        Top = bottom ? bottomRight.Y - ActualHeight : topLeft.Y;

        Activate();
        WindowNative.SetForegroundWindow(new WindowInteropHelper(this).Handle);

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    private void OnOpenWindowClick(object sender, RoutedEventArgs e)
    {
        Hide();
        OpenWindowRequested?.Invoke();
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        Hide();
        ExitRequested?.Invoke();
    }
}
