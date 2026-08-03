using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace BluetoothManagerPro.Services;

/// <summary>
/// Per-user auto-start through the Run key.
///
/// The Run value alone does not tell the truth: when the user disables the entry from
/// Task Manager's Startup tab, Windows leaves the value in place and records the decision
/// under Explorer\StartupApproved\Run instead. A checkbox that reads only the Run key
/// therefore shows "on" for an entry Windows will never launch — so both are read here.
/// No elevation is needed for any of this.
/// </summary>
public sealed class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "BluetoothManagerPro";

    /// <summary>Argument the launcher passes so an auto-started instance goes straight to the tray.</summary>
    public const string TrayArgument = "--tray";

    public bool IsEnabled
    {
        get
        {
            try
            {
                using RegistryKey? run = Registry.CurrentUser.OpenSubKey(RunKey);
                if (run?.GetValue(ValueName) is not string command || command.Length == 0)
                {
                    return false;
                }

                return IsApproved();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Auto-start read failed: {ex.Message}");
                return false;
            }
        }
    }

    public bool TrySetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
                                    ?? throw new InvalidOperationException("Run key unavailable.");

            if (enabled)
            {
                run.SetValue(ValueName, $"\"{ExecutablePath}\" {TrayArgument}", RegistryValueKind.String);
                ClearDisapproval();
            }
            else
            {
                run.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Auto-start write failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Path to the running executable. <c>Assembly.Location</c> is empty in a single-file
    /// publish, so it must not be used here.
    /// </summary>
    private static string ExecutablePath =>
        Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;

    /// <summary>
    /// Reads the StartupApproved blob. Byte 0 carries the state: bit 0 set means the user
    /// turned the entry off. A missing value means "never touched", i.e. enabled.
    /// </summary>
    private static bool IsApproved()
    {
        using RegistryKey? approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        if (approved?.GetValue(ValueName) is not byte[] state || state.Length == 0)
        {
            return true;
        }

        return (state[0] & 1) == 0;
    }

    /// <summary>Drops a previous "disabled by user" record so enabling actually takes effect.</summary>
    private static void ClearDisapproval()
    {
        try
        {
            using RegistryKey? approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
            approved?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not clear StartupApproved: {ex.Message}");
        }
    }
}
