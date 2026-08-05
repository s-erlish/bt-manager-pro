using System;

namespace BluetoothManagerPro.Models;

/// <summary>
/// How often the background work runs.
///
/// The app spends nearly all its life in the tray with nobody looking at it, and the only
/// thing that reads the connection state then is the tray icon and its tooltip. Polling on
/// the same schedule as a visible window meant paying for a Win32 device sweep every four
/// seconds forever, which is most of what an idle process was costing.
///
/// Both dimensions are honest trade-offs, not guesses: hidden windows lose nothing but the
/// latency of a tooltip, and every transition back to visible forces an immediate refresh,
/// so a slow pace is never what the user sees.
/// </summary>
public readonly record struct Pace(TimeSpan Connection, TimeSpan Battery)
{
    /// <summary>Window on screen, full decoration — responsive enough to feel live.</summary>
    private static readonly Pace Active = new(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(60));

    /// <summary>Window hidden. Only the tray icon consumes this.</summary>
    private static readonly Pace Background = new(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(300));

    /// <summary>Lite mode with the window up.</summary>
    private static readonly Pace LiteActive = new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(300));

    /// <summary>Lite mode in the tray: as close to doing nothing as the app can get.</summary>
    private static readonly Pace LiteBackground = new(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(900));

    public static Pace For(bool windowVisible, bool liteMode) => (windowVisible, liteMode) switch
    {
        (true, false) => Active,
        (false, false) => Background,
        (true, true) => LiteActive,
        (false, true) => LiteBackground,
    };
}
