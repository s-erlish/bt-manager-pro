using System;
using System.Runtime.InteropServices;

namespace BluetoothManagerPro.Interop;

/// <summary>Window-chrome and global-hotkey interop.</summary>
internal static class WindowNative
{
    // ---- DWM ---------------------------------------------------------------

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_PRE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;

    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>
    /// Asks DWM for a dark, rounded frame. Every attribute here is best-effort:
    /// the constants land in different Windows 10 builds and are ignored before them.
    /// </summary>
    public static void ApplyDarkFrame(IntPtr hwnd, int borderColorBgr, bool dark = true)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        int on = dark ? 1 : 0;
        // Windows 10 2004+ uses 20; 1809..1909 shipped the same flag as 19.
        if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_PRE_20H1, ref on, sizeof(int));
        }

        int corner = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

        int border = borderColorBgr;
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
    }

    // ---- Foreground / focus -------------------------------------------------

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    // ---- Working set --------------------------------------------------------

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimum, IntPtr maximum);

    /// <summary>
    /// Asks Windows to take back the pages the process is no longer touching.
    ///
    /// Passing -1 for both bounds is the documented way to say "trim to what is actually in
    /// use"; it does not free anything or cap the process, it moves cold pages out of the
    /// working set and lets them come back on demand. Worth doing exactly once, after the
    /// window has been put away and WPF has released its render resources — a tray app then
    /// sits on a working set that reflects what it is really doing rather than the high
    /// water mark of the last time its interface was on screen.
    /// </summary>
    public static void TrimWorkingSet()
        => SetProcessWorkingSetSize(GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1));
}
