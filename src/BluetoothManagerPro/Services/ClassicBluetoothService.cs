using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BluetoothManagerPro.Interop;
using BluetoothManagerPro.Models;

namespace BluetoothManagerPro.Services;

/// <summary>
/// Everything WinRT will not do for BR/EDR devices: report whether they are actually
/// connected, and connect or disconnect them without touching the pairing.
///
/// The mechanism is <c>BluetoothSetServiceState</c>, which enables or disables the
/// device's SDP services on a radio. For an audio headset, enabling A2DP + hands-free
/// is what "Connect" does in the Windows Settings UI; disabling them is "Disconnect".
/// </summary>
public sealed class ClassicBluetoothService
{
    /// <summary>Reads the connection state of every remembered BR/EDR device, keyed by address.</summary>
    public Task<Dictionary<ulong, ClassicDeviceState>> GetKnownDevicesAsync()
        => Task.Run(GetKnownDevices);

    public Task<OperationResult> SetConnectedAsync(ulong address, DeviceCategory category, bool connect)
        => Task.Run(() => SetConnected(address, category, connect));

    public Task<OperationResult> RemoveAsync(ulong address)
        => Task.Run(() => Remove(address));

    /// <summary>True when at least one Bluetooth radio is present on the machine.</summary>
    public bool HasRadio()
    {
        IntPtr find = FindFirstRadio(out IntPtr radio);
        if (find == IntPtr.Zero)
        {
            return false;
        }

        BluetoothNative.CloseHandle(radio);
        BluetoothNative.BluetoothFindRadioClose(find);
        return true;
    }

    private static Dictionary<ulong, ClassicDeviceState> GetKnownDevices()
    {
        var result = new Dictionary<ulong, ClassicDeviceState>();

        ForEachRadio(radio =>
        {
            var search = new BluetoothNative.BLUETOOTH_DEVICE_SEARCH_PARAMS
            {
                dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<BluetoothNative.BLUETOOTH_DEVICE_SEARCH_PARAMS>(),
                fReturnAuthenticated = true,
                fReturnRemembered = true,
                fReturnConnected = true,
                fReturnUnknown = false,
                fIssueInquiry = false,   // never block the UI on a 10s inquiry
                cTimeoutMultiplier = 0,
                hRadio = radio,
            };

            var info = BluetoothNative.BLUETOOTH_DEVICE_INFO.Create();
            IntPtr find = BluetoothNative.BluetoothFindFirstDevice(ref search, ref info);
            if (find == IntPtr.Zero)
            {
                return;
            }

            try
            {
                do
                {
                    result[info.Address] = new ClassicDeviceState(
                        info.Address,
                        info.szName?.Trim() ?? string.Empty,
                        info.ulClassofDevice,
                        info.fConnected,
                        info.fAuthenticated);

                    info = BluetoothNative.BLUETOOTH_DEVICE_INFO.Create();
                }
                while (BluetoothNative.BluetoothFindNextDevice(find, ref info));
            }
            finally
            {
                BluetoothNative.BluetoothFindDeviceClose(find);
            }
        });

        return result;
    }

    private static OperationResult SetConnected(ulong address, DeviceCategory category, bool connect)
    {
        bool anyRadio = false;
        bool anySuccess = false;
        int lastError = 0;

        ForEachRadio(radio =>
        {
            anyRadio = true;

            var info = BluetoothNative.BLUETOOTH_DEVICE_INFO.Create();
            info.Address = address;
            if (BluetoothNative.BluetoothGetDeviceInfo(radio, ref info) != BluetoothNative.ERROR_SUCCESS)
            {
                return; // device is not known to this radio
            }

            if (!info.fAuthenticated)
            {
                lastError = -1;
                return;
            }

            uint flag = connect ? BluetoothNative.BLUETOOTH_SERVICE_ENABLE : BluetoothNative.BLUETOOTH_SERVICE_DISABLE;

            foreach (Guid service in ServicesFor(radio, ref info, category))
            {
                Guid guid = service;
                int status = BluetoothNative.BluetoothSetServiceState(radio, ref info, ref guid, flag);

                // A service that is already in the requested state answers with an
                // invalid-argument code. That is the desired outcome, not a failure.
                if (status == BluetoothNative.ERROR_SUCCESS || IsAlreadyInState(status))
                {
                    anySuccess = true;
                }
                else
                {
                    lastError = status;
                }
            }

            // Disabling every service is what makes Windows tear the link down, but some
            // stacks keep it up. Force the ACL closed as a fallback.
            if (!connect && info.fConnected)
            {
                ForceDisconnect(radio, address);
            }
        });

        if (!anyRadio)
        {
            return OperationResult.Fail("Bluetooth-адаптер не найден.");
        }

        if (anySuccess)
        {
            return OperationResult.Ok();
        }

        if (lastError == -1)
        {
            return OperationResult.Fail("Устройство не сопряжено — сначала выполните сопряжение.");
        }

        string reason = lastError == 0
            ? "адаптер не знает это устройство"
            : new Win32Exception(lastError).Message;
        return OperationResult.Fail($"Не удалось {(connect ? "подключить" : "отключить")} устройство: {reason}");
    }

    /// <summary>ERROR_INVALID_PARAMETER / E_INVALIDARG — the service was already set that way.</summary>
    private static bool IsAlreadyInState(int status)
        => status == 87 || status == unchecked((int)0x80070057);

    private static void ForceDisconnect(IntPtr radio, ulong address)
    {
        ulong target = address;
        if (!BluetoothNative.DeviceIoControl(
                radio, BluetoothNative.IOCTL_BTH_DISCONNECT_DEVICE,
                ref target, sizeof(ulong), IntPtr.Zero, 0, out _, IntPtr.Zero))
        {
            Debug.WriteLine($"Force disconnect failed: {Marshal.GetLastWin32Error()}");
        }
    }

    private static OperationResult Remove(ulong address)
    {
        ulong addr = address;
        int status = BluetoothNative.BluetoothRemoveDevice(ref addr);
        return status == BluetoothNative.ERROR_SUCCESS
            ? OperationResult.Ok()
            : OperationResult.Fail(new Win32Exception(status).Message);
    }

    /// <summary>
    /// Prefers the services Windows actually installed for the device; falls back to the
    /// profiles implied by the device class when enumeration comes back empty.
    /// </summary>
    private static IReadOnlyList<Guid> ServicesFor(
        IntPtr radio, ref BluetoothNative.BLUETOOTH_DEVICE_INFO info, DeviceCategory category)
    {
        uint count = 0;
        int status = BluetoothNative.BluetoothEnumerateInstalledServices(radio, ref info, ref count, null);

        if ((status == BluetoothNative.ERROR_SUCCESS || status == BluetoothNative.ERROR_MORE_DATA) && count > 0)
        {
            var services = new Guid[count];
            status = BluetoothNative.BluetoothEnumerateInstalledServices(radio, ref info, ref count, services);
            if (status == BluetoothNative.ERROR_SUCCESS && count > 0)
            {
                Array.Resize(ref services, (int)count);
                return services;
            }
        }

        return FallbackServices(category);
    }

    private static IReadOnlyList<Guid> FallbackServices(DeviceCategory category) => category switch
    {
        DeviceCategory.Headphones or DeviceCategory.Speaker or DeviceCategory.Microphone => new[]
        {
            BluetoothNative.AudioSink,
            BluetoothNative.Handsfree,
            BluetoothNative.Headset,
            BluetoothNative.AvRemoteControl,
        },
        DeviceCategory.Mouse or DeviceCategory.Keyboard or DeviceCategory.Gamepad => new[]
        {
            BluetoothNative.HumanInterfaceDevice,
        },
        _ => new[]
        {
            BluetoothNative.AudioSink,
            BluetoothNative.Handsfree,
            BluetoothNative.HumanInterfaceDevice,
            BluetoothNative.SerialPort,
        },
    };

    private static IntPtr FindFirstRadio(out IntPtr radio)
    {
        var parameters = new BluetoothNative.BLUETOOTH_FIND_RADIO_PARAMS
        {
            dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<BluetoothNative.BLUETOOTH_FIND_RADIO_PARAMS>(),
        };

        return BluetoothNative.BluetoothFindFirstRadio(ref parameters, out radio);
    }

    /// <summary>Runs <paramref name="action"/> against every radio handle, closing each afterwards.</summary>
    private static void ForEachRadio(Action<IntPtr> action)
    {
        IntPtr find = FindFirstRadio(out IntPtr radio);
        if (find == IntPtr.Zero)
        {
            return;
        }

        try
        {
            do
            {
                try
                {
                    action(radio);
                }
                finally
                {
                    BluetoothNative.CloseHandle(radio);
                }
            }
            while (BluetoothNative.BluetoothFindNextRadio(find, out radio));
        }
        finally
        {
            BluetoothNative.BluetoothFindRadioClose(find);
        }
    }
}

/// <summary>What the classic stack knows about one remembered device.</summary>
public readonly record struct ClassicDeviceState(
    ulong Address,
    string Name,
    uint ClassOfDevice,
    bool IsConnected,
    bool IsAuthenticated);
