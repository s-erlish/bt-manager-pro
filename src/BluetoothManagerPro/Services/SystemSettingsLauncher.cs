using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BluetoothManagerPro.Services;

/// <summary>One-click shortcuts into the Windows pages that Bluetooth settings are scattered across.</summary>
public sealed class SystemSettingsLauncher
{
    /// <summary>The entries offered in the app's "Windows settings" menu, in order.</summary>
    public static IReadOnlyList<SystemSettingsEntry> Entries { get; } = new[]
    {
        new SystemSettingsEntry("Bluetooth и устройства", "ms-settings:bluetooth"),
        new SystemSettingsEntry("Звук", "ms-settings:sound"),
        new SystemSettingsEntry("Громкость приложений", "ms-settings:apps-volume"),
        new SystemSettingsEntry("Принтеры и сканеры", "ms-settings:printers"),
        new SystemSettingsEntry("Режим «в самолёте»", "ms-settings:network-airplanemode"),
        new SystemSettingsEntry("Приложения в автозагрузке", "ms-settings:startupapps"),
        new SystemSettingsEntry("Устройства и принтеры", "shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}"),
        new SystemSettingsEntry("Диспетчер устройств", "devmgmt.msc"),
    };

    public static bool Launch(string target)
    {
        try
        {
            // UseShellExecute is what lets a protocol URI, a shell folder GUID and an .msc
            // console all go through the same call.
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not launch '{target}': {ex.Message}");
            return false;
        }
    }
}

/// <summary>A labelled shell target shown in the settings menu.</summary>
public sealed record SystemSettingsEntry(string Title, string Target);
