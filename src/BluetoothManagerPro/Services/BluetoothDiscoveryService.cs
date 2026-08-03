using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using BluetoothManagerPro.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Foundation;

namespace BluetoothManagerPro.Services;

/// <summary>
/// Discovery and pairing over WinRT.
///
/// Two watchers, deliberately:
///   * a <i>passive</i> one over both Bluetooth transports that runs the whole time.
///     It reports paired devices, live connection state and LE advertisements without
///     touching the air.
///   * an <i>inquiry</i> one, started only when the user presses Search. BR/EDR inquiry
///     saturates the 2.4 GHz band and audibly stutters connected headsets, so it runs
///     for <see cref="InquirySeconds"/> and stops itself.
///
/// All events are re-raised on the UI thread.
/// </summary>
public sealed class BluetoothDiscoveryService : IDisposable
{
    /// <summary>How long an explicit scan is allowed to hold the radio in inquiry.</summary>
    public const int InquirySeconds = 30;

    // Association-endpoint protocol ids for the two Bluetooth transports.
    private const string ClassicProtocolId = "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}";
    private const string LowEnergyProtocolId = "{bb7bb05e-5972-42b5-94fc-76eaa7084d49}";

    private const string PropAddress = "System.Devices.Aep.DeviceAddress";
    private const string PropIsConnected = "System.Devices.Aep.IsConnected";
    private const string PropIsPaired = "System.Devices.Aep.IsPaired";
    private const string PropIsPresent = "System.Devices.Aep.IsPresent";
    private const string PropSignalStrength = "System.Devices.Aep.SignalStrength";
    private const string PropProtocolId = "System.Devices.Aep.ProtocolId";
    private const string PropContainerId = "System.Devices.Aep.ContainerId";
    private const string PropCodMajor = "System.Devices.Aep.Bluetooth.Cod.Major";
    private const string PropCodMinor = "System.Devices.Aep.Bluetooth.Cod.Minor";

    /// <summary>The property Windows itself uses for the battery pill in Settings.</summary>
    internal const string PropBatteryLevel = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    private static readonly string[] RequestedProperties =
    {
        PropAddress, PropIsConnected, PropIsPaired, PropIsPresent, PropSignalStrength,
        PropProtocolId, PropContainerId, PropCodMajor, PropCodMinor, PropBatteryLevel,
    };

    /// <summary>Dropped to this set if the platform rejects one of the optional properties above.</summary>
    private static readonly string[] MinimalProperties =
    {
        PropAddress, PropIsConnected, PropIsPaired, PropProtocolId,
    };

    private static readonly TimeSpan PairingTimeout = TimeSpan.FromSeconds(60);

    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<string, DeviceInformation> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    private DeviceWatcher? _passive;
    private DeviceWatcher? _inquiry;
    private DispatcherTimer? _inquiryTimer;
    private bool _disposed;

    public BluetoothDiscoveryService(Dispatcher dispatcher) => _dispatcher = dispatcher;

    /// <summary>Raised on the UI thread when a device appears or any of its properties change.</summary>
    public event Action<DeviceSnapshot>? DeviceChanged;

    /// <summary>Raised on the UI thread when an endpoint is removed by the passive watcher.</summary>
    public event Action<string>? DeviceRemoved;

    /// <summary>Raised on the UI thread when an active scan starts or finishes.</summary>
    public event Action<bool>? ScanningChanged;

    public bool IsScanning => _inquiry is not null;

    /// <summary>True when the passive watcher could not be created at all — no usable stack.</summary>
    public bool IsUnavailable { get; private set; }

    // ---- Passive watch ------------------------------------------------------

    public void Start()
    {
        if (_disposed || _passive is not null)
        {
            return;
        }

        string selector =
            $"({PropProtocolId}:=\"{ClassicProtocolId}\" OR {PropProtocolId}:=\"{LowEnergyProtocolId}\")";

        _passive = CreateWatcher(selector);
        if (_passive is null)
        {
            IsUnavailable = true;
            return;
        }

        IsUnavailable = false;
        _passive.Added += OnAdded;
        _passive.Updated += OnUpdated;
        _passive.Removed += OnRemoved;
        _passive.Start();
    }

    /// <summary>
    /// Tears the passive watcher down and builds a fresh one. Needed after the radio is
    /// power-cycled, because every watcher dies silently when the radio goes away.
    /// </summary>
    public void Restart()
    {
        StopInquiry();
        Detach(ref _passive, passive: true);

        lock (_sync)
        {
            _cache.Clear();
        }

        Start();
    }

    // ---- Active scan --------------------------------------------------------

    /// <summary>Runs a BR/EDR inquiry for <see cref="InquirySeconds"/> to surface new devices.</summary>
    public void StartInquiry()
    {
        if (_disposed || _inquiry is not null)
        {
            return;
        }

        // This selector is the one that sets Aep.Bluetooth.IssueInquiry, which is what
        // actually makes the radio scan for unpaired classic devices.
        _inquiry = CreateWatcher(BluetoothDevice.GetDeviceSelectorFromPairingState(false));
        if (_inquiry is null)
        {
            return;
        }

        _inquiry.Added += OnAdded;
        _inquiry.Updated += OnUpdated;
        _inquiry.EnumerationCompleted += OnInquiryFinished;
        _inquiry.Stopped += OnInquiryFinished;
        _inquiry.Start();

        _inquiryTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(InquirySeconds), DispatcherPriority.Normal, (_, _) => StopInquiry(), _dispatcher);

        ScanningChanged?.Invoke(true);
    }

    public void StopInquiry()
    {
        _inquiryTimer?.Stop();
        _inquiryTimer = null;

        if (_inquiry is null)
        {
            return;
        }

        Detach(ref _inquiry, passive: false);
        ScanningChanged?.Invoke(false);
    }

    private void OnInquiryFinished(DeviceWatcher sender, object args)
        => _dispatcher.InvokeAsync(StopInquiry);

    // ---- Watcher plumbing ---------------------------------------------------

    private static DeviceWatcher? CreateWatcher(string selector)
    {
        try
        {
            return DeviceInformation.CreateWatcher(
                selector, RequestedProperties, DeviceInformationKind.AssociationEndpoint);
        }
        catch (Exception ex)
        {
            // A property this build does not know about makes CreateWatcher throw outright.
            Debug.WriteLine($"Full watcher rejected ({ex.Message}); retrying with minimal properties.");
            try
            {
                return DeviceInformation.CreateWatcher(
                    selector, MinimalProperties, DeviceInformationKind.AssociationEndpoint);
            }
            catch (Exception inner)
            {
                Debug.WriteLine($"Bluetooth watcher unavailable: {inner.Message}");
                return null;
            }
        }
    }

    private void Detach(ref DeviceWatcher? field, bool passive)
    {
        DeviceWatcher? watcher = field;
        field = null;
        if (watcher is null)
        {
            return;
        }

        watcher.Added -= OnAdded;
        watcher.Updated -= OnUpdated;
        if (passive)
        {
            watcher.Removed -= OnRemoved;
        }
        else
        {
            watcher.EnumerationCompleted -= OnInquiryFinished;
            watcher.Stopped -= OnInquiryFinished;
        }

        try
        {
            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            {
                watcher.Stop();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Watcher stop failed: {ex.Message}");
        }
    }

    private void OnAdded(DeviceWatcher sender, DeviceInformation info)
    {
        lock (_sync)
        {
            _cache[info.Id] = info;
        }

        Publish(info);
    }

    private void OnUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        DeviceInformation? info;
        lock (_sync)
        {
            if (!_cache.TryGetValue(update.Id, out info))
            {
                return;
            }

            // An update carries only the changed properties, so it has to be folded into
            // the cached object rather than read on its own.
            info.Update(update);
        }

        Publish(info);
    }

    private void OnRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        lock (_sync)
        {
            _cache.Remove(update.Id);
        }

        _dispatcher.InvokeAsync(() => DeviceRemoved?.Invoke(update.Id));
    }

    private void Publish(DeviceInformation info)
    {
        DeviceSnapshot snapshot = ToSnapshot(info);
        _dispatcher.InvokeAsync(() => DeviceChanged?.Invoke(snapshot));
    }

    private static DeviceSnapshot ToSnapshot(DeviceInformation info)
    {
        var properties = info.Properties;

        string protocol = Read<string>(properties, PropProtocolId) ?? string.Empty;
        DeviceTransport transport = protocol.Equals(LowEnergyProtocolId, StringComparison.OrdinalIgnoreCase)
            ? DeviceTransport.LowEnergy
            : DeviceTransport.Classic;

        uint major = Read<uint?>(properties, PropCodMajor) ?? 0;
        uint minor = Read<uint?>(properties, PropCodMinor) ?? 0;
        uint classOfDevice = (major << 8) | (minor << 2);

        string name = string.IsNullOrWhiteSpace(info.Name) ? string.Empty : info.Name.Trim();

        return new DeviceSnapshot
        {
            Id = info.Id,
            Name = name,
            Address = DeviceSnapshot.ParseAddress(Read<string>(properties, PropAddress)),
            ContainerId = Read<Guid?>(properties, PropContainerId) ?? Guid.Empty,
            Transport = transport,
            ClassOfDevice = classOfDevice,
            Category = ClassOfDeviceMap.Refine(ClassOfDeviceMap.ToCategory(classOfDevice), name),
            IsPaired = info.Pairing.IsPaired,
            CanPair = info.Pairing.CanPair,
            IsPresent = Read<bool?>(properties, PropIsPresent) ?? true,
            IsConnected = Read<bool?>(properties, PropIsConnected) ?? false,
            SignalStrength = Read<int?>(properties, PropSignalStrength),
            BatteryPercent = ReadBattery(properties),
            LastSeen = DateTimeOffset.Now,
        };
    }

    private static int? ReadBattery(IReadOnlyDictionary<string, object> properties)
    {
        object? raw = properties.TryGetValue(PropBatteryLevel, out object? value) ? value : null;
        int percent = raw switch
        {
            byte b => b,
            int i => i,
            uint u => (int)u,
            _ => -1,
        };

        return percent is >= 0 and <= 100 ? percent : null;
    }

    private static T? Read<T>(IReadOnlyDictionary<string, object> properties, string key)
    {
        if (!properties.TryGetValue(key, out object? value) || value is null)
        {
            return default;
        }

        try
        {
            if (value is T typed)
            {
                return typed;
            }

            Type target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            return (T)Convert.ChangeType(value, target);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            return default;
        }
    }

    // ---- Pairing ------------------------------------------------------------

    /// <summary>
    /// Pairs a device, delegating any PIN or confirmation step to <paramref name="prompt"/>.
    ///
    /// Only the custom ceremony is used: <c>DeviceInformationPairing.PairAsync</c> raises
    /// system UI that needs a CoreWindow and is unsupported in a desktop process.
    /// </summary>
    public async Task<OperationResult> PairAsync(string deviceId, Func<PairingPrompt, Task<PairingAnswer>> prompt)
    {
        DeviceInformation info;
        try
        {
            info = await DeviceInformation.CreateFromIdAsync(deviceId);
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Устройство недоступно: {ex.Message}");
        }

        if (info.Pairing.IsPaired)
        {
            return OperationResult.Ok();
        }

        if (!info.Pairing.CanPair)
        {
            return OperationResult.Fail("Это устройство не поддерживает сопряжение.");
        }

        DeviceInformationCustomPairing custom = info.Pairing.Custom;
        string deviceName = string.IsNullOrWhiteSpace(info.Name) ? "устройство" : info.Name;

        async void OnPairingRequested(DeviceInformationCustomPairing sender, DevicePairingRequestedEventArgs args)
        {
            // ConfirmOnly is the one ceremony that must not take a deferral.
            if (args.PairingKind == DevicePairingKinds.ConfirmOnly)
            {
                args.Accept();
                return;
            }

            Deferral deferral = args.GetDeferral();
            try
            {
                switch (args.PairingKind)
                {
                    case DevicePairingKinds.DisplayPin:
                        // Accept first, then show the code: the ceremony must not wait on the user.
                        args.Accept();
                        await prompt(new PairingPrompt(deviceName, args.PairingKind, args.Pin));
                        break;

                    case DevicePairingKinds.ConfirmPinMatch:
                    {
                        PairingAnswer answer = await prompt(
                            new PairingPrompt(deviceName, args.PairingKind, args.Pin));
                        if (answer.Accepted)
                        {
                            args.Accept();
                        }

                        break;
                    }

                    case DevicePairingKinds.ProvidePin:
                    {
                        PairingAnswer answer = await prompt(
                            new PairingPrompt(deviceName, args.PairingKind, null));
                        if (answer.Accepted && !string.IsNullOrEmpty(answer.Pin))
                        {
                            args.Accept(answer.Pin);
                        }

                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Pairing prompt failed: {ex.Message}");
            }
            finally
            {
                deferral.Complete();
            }
        }

        custom.PairingRequested += OnPairingRequested;
        using var timeout = new CancellationTokenSource(PairingTimeout);
        try
        {
            // Every ceremony must be offered: a device asking for one we did not declare
            // fails with RequiredHandlerNotRegistered.
            const DevicePairingKinds kinds = DevicePairingKinds.ConfirmOnly
                                             | DevicePairingKinds.DisplayPin
                                             | DevicePairingKinds.ProvidePin
                                             | DevicePairingKinds.ConfirmPinMatch;

            DevicePairingResult result = await custom
                .PairAsync(kinds, DevicePairingProtectionLevel.Default)
                .AsTask(timeout.Token);

            return result.Status switch
            {
                DevicePairingResultStatus.Paired => OperationResult.Ok(),
                DevicePairingResultStatus.AlreadyPaired => OperationResult.Ok(),
                _ => OperationResult.Fail(Describe(result.Status)),
            };
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Fail("Истекло время ожидания сопряжения.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Сопряжение не удалось: {ex.Message}");
        }
        finally
        {
            custom.PairingRequested -= OnPairingRequested;
        }
    }

    /// <summary>Removes the pairing for a single association endpoint.</summary>
    public async Task<OperationResult> UnpairAsync(string deviceId)
    {
        try
        {
            DeviceInformation info = await DeviceInformation.CreateFromIdAsync(deviceId);
            if (!info.Pairing.IsPaired)
            {
                return OperationResult.Ok();
            }

            DeviceUnpairingResult result = await info.Pairing.UnpairAsync();
            return result.Status switch
            {
                DeviceUnpairingResultStatus.Unpaired => OperationResult.Ok(),
                DeviceUnpairingResultStatus.AlreadyUnpaired => OperationResult.Ok(),
                DeviceUnpairingResultStatus.AccessDenied => OperationResult.Fail("Нет доступа для удаления устройства."),
                DeviceUnpairingResultStatus.OperationAlreadyInProgress =>
                    OperationResult.Fail("Удаление уже выполняется."),
                _ => OperationResult.Fail("Не удалось удалить сопряжение."),
            };
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Не удалось удалить сопряжение: {ex.Message}");
        }
    }

    private static string Describe(DevicePairingResultStatus status) => status switch
    {
        DevicePairingResultStatus.NotReadyToPair => "Устройство не готово к сопряжению.",
        DevicePairingResultStatus.NotPaired => "Сопряжение не выполнено.",
        DevicePairingResultStatus.ConnectionRejected => "Устройство отклонило подключение.",
        DevicePairingResultStatus.TooManyConnections => "Слишком много активных подключений.",
        DevicePairingResultStatus.HardwareFailure => "Сбой Bluetooth-адаптера.",
        DevicePairingResultStatus.AuthenticationTimeout => "Истекло время ожидания подтверждения.",
        DevicePairingResultStatus.AuthenticationNotAllowed => "Способ проверки подлинности не поддерживается.",
        DevicePairingResultStatus.AuthenticationFailure => "Неверный PIN-код.",
        DevicePairingResultStatus.NoSupportedProfiles => "У устройства нет поддерживаемых профилей.",
        DevicePairingResultStatus.ProtectionLevelCouldNotBeMet => "Не удалось обеспечить требуемый уровень защиты.",
        DevicePairingResultStatus.AccessDenied => "Отказано в доступе.",
        DevicePairingResultStatus.InvalidCeremonyData => "Неверные данные подтверждения.",
        DevicePairingResultStatus.PairingCanceled => "Сопряжение отменено.",
        DevicePairingResultStatus.OperationAlreadyInProgress => "Сопряжение уже выполняется.",
        DevicePairingResultStatus.RequiredHandlerNotRegistered => "Не зарегистрирован обработчик подтверждения.",
        DevicePairingResultStatus.RejectedByHandler => "Подтверждение отклонено.",
        DevicePairingResultStatus.RemoteDeviceHasAssociation => "Устройство уже сопряжено с другой системой.",
        _ => "Не удалось выполнить сопряжение.",
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _inquiryTimer?.Stop();
        _inquiryTimer = null;
        Detach(ref _inquiry, passive: false);
        Detach(ref _passive, passive: true);

        lock (_sync)
        {
            _cache.Clear();
        }
    }
}

/// <summary>What the pairing ceremony needs the user to confirm.</summary>
public readonly record struct PairingPrompt(string DeviceName, DevicePairingKinds Kind, string? Pin);

/// <summary>The user's answer to a <see cref="PairingPrompt"/>.</summary>
public readonly record struct PairingAnswer(bool Accepted, string? Pin)
{
    public static PairingAnswer Yes(string? pin = null) => new(true, pin);

    public static PairingAnswer No() => new(false, null);
}
