using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace BluetoothManagerPro.Services;

/// <summary>
/// Best-effort battery reporting.
///
/// Windows has no single API for this. Two sources are combined:
///   * LE devices — the standard GATT Battery Service (0x180F / characteristic 0x2A19).
///   * BR/EDR devices — the undocumented-but-stable PnP property
///     {104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2, which is what the Settings app itself
///     shows for hands-free headsets.
///
/// Plenty of devices report neither. Callers must treat a null result as normal.
/// </summary>
public sealed class BatteryService
{
    private const string BatteryProperty = BluetoothDiscoveryService.PropBatteryLevel;
    private const string ContainerIdProperty = "System.Devices.ContainerId";
    private const string InstanceIdProperty = "System.Devices.DeviceInstanceId";

    /// <summary>Reads the GATT battery level of a connected LE device.</summary>
    public async Task<int?> ReadLowEnergyAsync(string deviceId)
    {
        BluetoothLEDevice? device = null;
        GattDeviceService? service = null;

        try
        {
            device = await BluetoothLEDevice.FromIdAsync(deviceId);
            if (device is null)
            {
                return null;
            }

            GattDeviceServicesResult services =
                await device.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Uncached);
            if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0)
            {
                return null;
            }

            service = services.Services[0];
            GattCharacteristicsResult characteristics = await service.GetCharacteristicsForUuidAsync(
                GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Uncached);
            if (characteristics.Status != GattCommunicationStatus.Success || characteristics.Characteristics.Count == 0)
            {
                return null;
            }

            GattReadResult read = await characteristics.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Uncached);
            if (read.Status != GattCommunicationStatus.Success || read.Value is null || read.Value.Length == 0)
            {
                return null;
            }

            using var reader = DataReader.FromBuffer(read.Value);
            return reader.ReadByte();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GATT battery read failed for {deviceId}: {ex.Message}");
            return null;
        }
        finally
        {
            service?.Dispose();
            device?.Dispose();
        }
    }

    /// <summary>
    /// Sweeps the PnP device tree for hands-free battery levels, keyed by container id
    /// so callers can join them against association endpoints.
    ///
    /// The narrow BTHENUM query is the only one that runs in the normal case. The wide
    /// query — every device node on the machine — is reserved for the one situation it was
    /// meant for: a Windows build that rejects the "starts with" operator outright, which
    /// shows up as an exception. An empty result is not that. Retrying wide whenever the
    /// narrow sweep came back empty meant enumerating the entire device tree every minute
    /// on every machine whose headset does not report a battery level, which is most of
    /// them, to arrive at the same empty answer.
    /// </summary>
    public async Task<Dictionary<Guid, int>> ReadClassicByContainerAsync()
    {
        try
        {
            return await SweepAsync($"{InstanceIdProperty}:~<\"BTHENUM\"");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Filtered battery sweep rejected ({ex.Message}); retrying across all devices.");
        }

        try
        {
            return await SweepAsync(string.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Classic battery sweep failed: {ex.Message}");
            return new Dictionary<Guid, int>();
        }
    }

    private static async Task<Dictionary<Guid, int>> SweepAsync(string filter)
    {
        var result = new Dictionary<Guid, int>();

        DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(
            filter,
            new[] { BatteryProperty, ContainerIdProperty },
            DeviceInformationKind.Device);

        foreach (DeviceInformation device in devices)
        {
            if (!TryReadPercent(device, out int percent))
            {
                continue;
            }

            if (!device.Properties.TryGetValue(ContainerIdProperty, out object? container) ||
                container is not Guid containerId || containerId == Guid.Empty)
            {
                continue;
            }

            result[containerId] = percent;
        }

        return result;
    }

    private static bool TryReadPercent(DeviceInformation device, out int percent)
    {
        percent = 0;
        if (!device.Properties.TryGetValue(BatteryProperty, out object? raw) || raw is null)
        {
            return false;
        }

        percent = raw switch
        {
            byte b => b,
            int i => i,
            uint u => (int)u,
            _ => -1,
        };

        return percent is >= 0 and <= 100;
    }
}
