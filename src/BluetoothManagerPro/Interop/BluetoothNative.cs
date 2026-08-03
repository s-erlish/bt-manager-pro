using System;
using System.Runtime.InteropServices;

namespace BluetoothManagerPro.Interop;

/// <summary>
/// P/Invoke surface for the classic (BR/EDR) Bluetooth stack in <c>bthprops.cpl</c>.
///
/// WinRT covers discovery, pairing and BLE well, but it exposes no way to connect or
/// disconnect a *classic* device — the thing you actually want when switching a headset
/// between machines. <c>BluetoothSetServiceState</c> is the documented Win32 route, and
/// <c>BluetoothFindFirstDevice</c> is the only reliable source of <c>fConnected</c> for
/// BR/EDR devices. Both are used from <see cref="Services.ClassicBluetoothService"/>.
/// </summary>
internal static class BluetoothNative
{
    private const string BthPropsDll = "bthprops.cpl";

    public const int ERROR_SUCCESS = 0;
    public const int BLUETOOTH_MAX_NAME_SIZE = 248;

    public const uint BLUETOOTH_SERVICE_DISABLE = 0x00;
    public const uint BLUETOOTH_SERVICE_ENABLE = 0x01;

    // Bluetooth SDP service class UUIDs (base 0000xxxx-0000-1000-8000-00805F9B34FB).
    public static readonly Guid AudioSink = new("0000110B-0000-1000-8000-00805F9B34FB");
    public static readonly Guid Handsfree = new("0000111E-0000-1000-8000-00805F9B34FB");
    public static readonly Guid Headset = new("00001108-0000-1000-8000-00805F9B34FB");
    public static readonly Guid AvRemoteControl = new("0000110E-0000-1000-8000-00805F9B34FB");
    public static readonly Guid HumanInterfaceDevice = new("00001124-0000-1000-8000-00805F9B34FB");
    public static readonly Guid SerialPort = new("00001101-0000-1000-8000-00805F9B34FB");

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEMTIME
    {
        public ushort wYear;
        public ushort wMonth;
        public ushort wDayOfWeek;
        public ushort wDay;
        public ushort wHour;
        public ushort wMinute;
        public ushort wSecond;
        public ushort wMilliseconds;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct BLUETOOTH_DEVICE_INFO
    {
        public uint dwSize;
        public ulong Address;
        public uint ulClassofDevice;
        [MarshalAs(UnmanagedType.Bool)] public bool fConnected;
        [MarshalAs(UnmanagedType.Bool)] public bool fRemembered;
        [MarshalAs(UnmanagedType.Bool)] public bool fAuthenticated;
        public SYSTEMTIME stLastSeen;
        public SYSTEMTIME stLastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = BLUETOOTH_MAX_NAME_SIZE)]
        public string szName;

        public static BLUETOOTH_DEVICE_INFO Create() => new()
        {
            dwSize = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>(),
            szName = string.Empty,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BLUETOOTH_DEVICE_SEARCH_PARAMS
    {
        public uint dwSize;
        [MarshalAs(UnmanagedType.Bool)] public bool fReturnAuthenticated;
        [MarshalAs(UnmanagedType.Bool)] public bool fReturnRemembered;
        [MarshalAs(UnmanagedType.Bool)] public bool fReturnUnknown;
        [MarshalAs(UnmanagedType.Bool)] public bool fReturnConnected;
        [MarshalAs(UnmanagedType.Bool)] public bool fIssueInquiry;
        public byte cTimeoutMultiplier;
        public IntPtr hRadio;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BLUETOOTH_FIND_RADIO_PARAMS
    {
        public uint dwSize;
    }

    // ---- Radios -------------------------------------------------------------

    [DllImport(BthPropsDll, SetLastError = true)]
    public static extern IntPtr BluetoothFindFirstRadio(
        ref BLUETOOTH_FIND_RADIO_PARAMS pbtfrp, out IntPtr phRadio);

    [DllImport(BthPropsDll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BluetoothFindNextRadio(IntPtr hFind, out IntPtr phRadio);

    [DllImport(BthPropsDll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BluetoothFindRadioClose(IntPtr hFind);

    // ---- Devices ------------------------------------------------------------

    [DllImport(BthPropsDll, SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr BluetoothFindFirstDevice(
        ref BLUETOOTH_DEVICE_SEARCH_PARAMS pbtsp, ref BLUETOOTH_DEVICE_INFO pbtdi);

    [DllImport(BthPropsDll, SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BluetoothFindNextDevice(IntPtr hFind, ref BLUETOOTH_DEVICE_INFO pbtdi);

    [DllImport(BthPropsDll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BluetoothFindDeviceClose(IntPtr hFind);

    [DllImport(BthPropsDll, CharSet = CharSet.Unicode)]
    public static extern int BluetoothGetDeviceInfo(IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO pbtdi);

    /// <summary>Enables or disables an SDP service on a paired device — the connect/disconnect lever.</summary>
    [DllImport(BthPropsDll, CharSet = CharSet.Unicode)]
    public static extern int BluetoothSetServiceState(
        IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO pbtdi, ref Guid pGuidService, uint dwServiceFlags);

    [DllImport(BthPropsDll)]
    public static extern int BluetoothRemoveDevice(ref ulong pAddress);

    /// <summary>
    /// Two-call pattern: pass a null buffer to learn the service count (ERROR_MORE_DATA),
    /// then call again with an array of that size.
    /// </summary>
    [DllImport(BthPropsDll)]
    public static extern int BluetoothEnumerateInstalledServices(
        IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO pbtdi, ref uint pcServiceInout,
        [Out] Guid[]? pGuidServices);

    public const int ERROR_MORE_DATA = 234;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    // ---- Force link teardown ------------------------------------------------

    /// <summary>
    /// CTL_CODE(FILE_DEVICE_BLUETOOTH = 0x41, 0x03, METHOD_BUFFERED, FILE_ANY_ACCESS).
    /// Drops the ACL to a device immediately. Windows will re-establish it if the
    /// device's services are still enabled, so this is a last resort, not "disconnect".
    /// </summary>
    public const uint IOCTL_BTH_DISCONNECT_DEVICE = 0x0041000C;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeviceIoControl(
        IntPtr hDevice, uint dwIoControlCode,
        ref ulong lpInBuffer, uint nInBufferSize,
        IntPtr lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, IntPtr lpOverlapped);
}
