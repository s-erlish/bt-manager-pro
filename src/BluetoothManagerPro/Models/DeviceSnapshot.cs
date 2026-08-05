using System;
using System.Text;

namespace BluetoothManagerPro.Models;

/// <summary>
/// Immutable view of one Bluetooth endpoint at a point in time, assembled from the
/// WinRT association endpoint plus whatever the Win32 stack could add.
/// </summary>
public sealed class DeviceSnapshot
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>48-bit BR/EDR address, or 0 when the endpoint did not report one.</summary>
    public ulong Address { get; init; }

    /// <summary>
    /// Groups the endpoints of one physical device. A modern headset publishes both a
    /// BR/EDR and an LE endpoint; they share a container id, and the UI merges them.
    /// </summary>
    public Guid ContainerId { get; init; }

    public DeviceTransport Transport { get; init; }

    public DeviceCategory Category { get; init; }

    public uint ClassOfDevice { get; init; }

    public bool IsPaired { get; init; }

    public bool CanPair { get; init; }

    public bool IsConnected { get; init; }

    /// <summary>False once Windows decides the endpoint is out of range.</summary>
    public bool IsPresent { get; init; } = true;

    /// <summary>Raw RSSI in dBm when advertised (BLE only), otherwise null.</summary>
    public int? SignalStrength { get; init; }

    public int? BatteryPercent { get; init; }

    public DateTimeOffset LastSeen { get; init; }

    public string AddressText => FormatAddress(Address);

    /// <summary>RSSI mapped onto the 0..4 scale the list actually draws.</summary>
    public int SignalBars => ToBars(SignalStrength);

    /// <summary>Shared by the snapshot and the row so both agree on what a change is.</summary>
    public static int ToBars(int? rssi) => rssi switch
    {
        null => 0,
        >= -55 => 4,
        >= -67 => 3,
        >= -80 => 2,
        _ => 1,
    };

    /// <summary>
    /// True when <paramref name="other"/> would draw exactly the same row.
    ///
    /// The passive watcher re-reports an advertising LE endpoint several times a second,
    /// and every one of those used to cost a dispatcher hop, a view re-sort and a tray-icon
    /// redraw. Nearly all of them differ only in <see cref="LastSeen"/> and by a decibel or
    /// two of RSSI, so time is ignored outright and signal is compared as bars — the only
    /// part of it the interface ever shows.
    /// </summary>
    public bool RendersSameAs(DeviceSnapshot other)
        => Address == other.Address
           && ContainerId == other.ContainerId
           && Transport == other.Transport
           && Category == other.Category
           && ClassOfDevice == other.ClassOfDevice
           && IsPaired == other.IsPaired
           && CanPair == other.CanPair
           && IsConnected == other.IsConnected
           && IsPresent == other.IsPresent
           && BatteryPercent == other.BatteryPercent
           && SignalBars == other.SignalBars
           && string.Equals(Name, other.Name, StringComparison.Ordinal);

    /// <summary>Renders a 48-bit address MSB-first, e.g. <c>A4:C1:38:0F:2B:9E</c>.</summary>
    public static string FormatAddress(ulong address)
    {
        if (address == 0)
        {
            return string.Empty;
        }

        var text = new StringBuilder(17);
        for (int shift = 40; shift >= 0; shift -= 8)
        {
            if (text.Length > 0)
            {
                text.Append(':');
            }

            text.Append(((byte)(address >> shift)).ToString("X2"));
        }

        return text.ToString();
    }

    /// <summary>Parses the <c>aa:bb:cc:dd:ee:ff</c> form WinRT reports for an endpoint.</summary>
    public static ulong ParseAddress(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        ulong result = 0;
        int nibbles = 0;
        foreach (char c in text)
        {
            int value = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'f' => c - 'a' + 10,
                >= 'A' and <= 'F' => c - 'A' + 10,
                _ => -1,
            };

            if (value < 0)
            {
                continue;
            }

            result = (result << 4) | (uint)value;
            nibbles++;
        }

        return nibbles == 12 ? result : 0;
    }

    public override string ToString() => $"{Name} ({Id})";
}
