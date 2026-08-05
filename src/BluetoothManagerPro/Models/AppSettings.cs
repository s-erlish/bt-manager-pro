using System.Collections.Generic;

namespace BluetoothManagerPro.Models;

/// <summary>User preferences, persisted as JSON under %APPDATA%\BluetoothManagerPro.</summary>
public sealed class AppSettings
{
    /// <summary>Id of the chosen accent; see <see cref="AccentPreset.All"/>.</summary>
    public string AccentId { get; set; } = AccentPreset.DefaultId;

    /// <summary>Dark surfaces, or light ones tinted with the accent.</summary>
    public ThemeMode ThemeMode { get; set; } = ThemeMode.Dark;

    /// <summary>
    /// Trades the decoration for a smaller footprint: no animations, no shadows, no
    /// relief, and the background polls stretched out. See <see cref="Pace"/>.
    /// </summary>
    public bool LiteMode { get; set; }

    /// <summary>Register the app in the per-user Run key so it comes up with the session.</summary>
    public bool AutoStart { get; set; }

    /// <summary>When auto-started, go straight to the tray instead of showing the window.</summary>
    public bool StartMinimized { get; set; } = true;

    /// <summary>Closing the window hides it instead of exiting.</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>Hide endpoints that advertise no friendly name — mostly beacons and sensors.</summary>
    public bool HideUnnamedDevices { get; set; } = true;

    /// <summary>Device ids pinned to the top of the list and to the tray flyout.</summary>
    public List<string> Favorites { get; set; } = new();

    /// <summary>Friendly names the user overrode, keyed by device id.</summary>
    public Dictionary<string, string> Aliases { get; set; } = new();
}
