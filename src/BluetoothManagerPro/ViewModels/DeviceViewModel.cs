using System;
using System.Collections.Generic;
using BluetoothManagerPro.Infrastructure;
using BluetoothManagerPro.Models;

namespace BluetoothManagerPro.ViewModels;

/// <summary>
/// One physical device in the list.
///
/// A modern headset publishes two association endpoints — one BR/EDR, one LE — and
/// Windows shows both. Here they are folded into a single row keyed by container id,
/// with the endpoint ids kept so unpair can remove every one of them.
///
/// Everything derived from the endpoints is folded once, in <see cref="Recompute"/>, and
/// cached in fields. These properties are read by the collection view's filter and sort on
/// every refresh and by the connection poll on every tick, so re-deriving them per access
/// put LINQ chains on a path that runs constantly.
/// </summary>
public sealed class DeviceViewModel : ObservableObject
{
    private readonly Dictionary<string, DeviceSnapshot> _endpoints = new(StringComparer.OrdinalIgnoreCase);

    private string _name = string.Empty;
    private string? _alias;
    private DeviceCategory _category;
    private bool _isPaired;
    private bool _isConnected;
    private bool _isFavorite;
    private bool _isBusy;
    private int? _batteryPercent;
    private int? _signalStrength;
    private ulong _address;

    private string _primaryEndpointId;
    private bool _hasClassicEndpoint;
    private bool _isPresent = true;

    public DeviceViewModel(string key, DeviceSnapshot first)
    {
        Key = key;
        _primaryEndpointId = key;
        Apply(first);
    }

    /// <summary>Stable identity across endpoints: container id, else address, else endpoint id.</summary>
    public string Key { get; }

    public IReadOnlyCollection<string> EndpointIds => _endpoints.Keys;

    /// <summary>The endpoint to drive pairing through — BR/EDR first, since it carries the profiles.</summary>
    public string PrimaryEndpointId => _primaryEndpointId;

    public ulong Address
    {
        get => _address;
        private set => SetProperty(ref _address, value);
    }

    public string Name
    {
        get => _name;
        private set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    /// <summary>User-supplied name, shown instead of the advertised one when set.</summary>
    public string? Alias
    {
        get => _alias;
        set
        {
            if (SetProperty(ref _alias, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string DisplayName => string.IsNullOrWhiteSpace(Alias)
        ? (string.IsNullOrWhiteSpace(Name) ? "Неизвестное устройство" : Name)
        : Alias!;

    public bool HasName => !string.IsNullOrWhiteSpace(Name);

    public DeviceCategory Category
    {
        get => _category;
        private set => SetProperty(ref _category, value);
    }

    public bool IsPaired
    {
        get => _isPaired;
        private set
        {
            if (SetProperty(ref _isPaired, value))
            {
                RaiseDerived();
            }
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (SetProperty(ref _isConnected, value))
            {
                RaiseDerived();
            }
        }
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (SetProperty(ref _isFavorite, value))
            {
                OnPropertyChanged(nameof(SortRank));
            }
        }
    }

    /// <summary>True while an operation on this device is in flight; the row shows a spinner.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public int? BatteryPercent
    {
        get => _batteryPercent;
        set
        {
            if (SetProperty(ref _batteryPercent, value))
            {
                OnPropertyChanged(nameof(HasBattery));
                OnPropertyChanged(nameof(BatteryText));
                OnPropertyChanged(nameof(IsBatteryLow));
            }
        }
    }

    public bool HasBattery => BatteryPercent is not null;

    public bool IsBatteryLow => BatteryPercent is >= 0 and <= 20;

    public string BatteryText => BatteryPercent is { } percent ? $"{percent}%" : string.Empty;

    public int? SignalStrength
    {
        get => _signalStrength;
        set
        {
            if (SetProperty(ref _signalStrength, value))
            {
                OnPropertyChanged(nameof(HasSignal));
                OnPropertyChanged(nameof(SignalBars));
            }
        }
    }

    public bool HasSignal => SignalStrength is not null && !IsConnected;

    /// <summary>RSSI in dBm mapped onto a 1..4 scale for the bar indicator.</summary>
    public int SignalBars => DeviceSnapshot.ToBars(SignalStrength);

    public bool HasClassicEndpoint => _hasClassicEndpoint;

    public string StatusText => IsConnected
        ? "Подключено"
        : IsPaired ? "Сопряжено" : "Доступно для сопряжения";

    /// <summary>Heading the row is grouped under.</summary>
    public string GroupName => IsConnected
        ? "Подключённые"
        : IsPaired ? "Сопряжённые" : "Найденные поблизости";

    /// <summary>Connected first, then favourites, then paired, then everything else.</summary>
    public int SortRank => IsConnected ? 0 : IsFavorite ? 1 : IsPaired ? 2 : 3;

    /// <summary>Folds one endpoint's state into this row. Returns true when anything changed.</summary>
    public bool Apply(DeviceSnapshot snapshot)
    {
        _endpoints[snapshot.Id] = snapshot;
        return Recompute();
    }

    /// <summary>Drops an endpoint. Returns false once the device has no endpoints left.</summary>
    public bool RemoveEndpoint(string endpointId)
    {
        _endpoints.Remove(endpointId);
        if (_endpoints.Count == 0)
        {
            return false;
        }

        Recompute();
        return true;
    }

    /// <summary>
    /// Folds every endpoint into the row's state in a single pass. Written as one loop
    /// rather than a stack of LINQ chains because it runs for every accepted watcher
    /// update, and each chain there was an enumerator plus a closure per call.
    /// </summary>
    private bool Recompute()
    {
        if (_endpoints.Count == 0)
        {
            return false;
        }

        bool paired = false;
        bool connected = false;
        bool present = false;
        bool classic = false;
        string? name = null;
        ulong address = 0;
        DeviceCategory category = DeviceCategory.Unknown;
        int? battery = null;
        int? signal = null;
        string? primary = null;
        bool primaryIsClassic = false;

        foreach (DeviceSnapshot endpoint in _endpoints.Values)
        {
            paired |= endpoint.IsPaired;
            connected |= endpoint.IsConnected;
            present |= endpoint.IsPresent;

            bool isClassic = endpoint.Transport == DeviceTransport.Classic;
            classic |= isClassic;

            // BR/EDR first: it is the endpoint that carries the profiles.
            if (primary is null || (isClassic && !primaryIsClassic))
            {
                primary = endpoint.Id;
                primaryIsClassic = isClassic;
            }

            if (name is null && !string.IsNullOrWhiteSpace(endpoint.Name))
            {
                name = endpoint.Name;
            }

            if (address == 0)
            {
                address = endpoint.Address;
            }

            if (category == DeviceCategory.Unknown)
            {
                category = endpoint.Category;
            }

            battery ??= endpoint.BatteryPercent;

            // Signal only means something for an endpoint that is still advertising.
            if (signal is null && !isClassic)
            {
                signal = endpoint.SignalStrength;
            }
        }

        bool ordering = paired != _isPaired || connected != _isConnected;

        _primaryEndpointId = primary ?? Key;
        _hasClassicEndpoint = classic;

        if (_isPresent != present)
        {
            _isPresent = present;
            OnPropertyChanged(nameof(IsPresent));
        }

        // Identity is sticky. A device usually carries its name, address and class on one
        // endpoint only, and that endpoint goes away first when the device disconnects —
        // so recomputing these from whatever is left would rename a known speaker to
        // "Неизвестное устройство" and, worse, zero its address, after which the classic
        // sweep can no longer find it to correct a stale connected flag.
        if (name is not null)
        {
            Name = name;
        }

        if (address != 0)
        {
            Address = address;
        }

        if (category != DeviceCategory.Unknown)
        {
            Category = category;
        }

        IsPaired = paired;
        IsConnected = connected;

        // Same reasoning: never clear a battery reading because another endpoint lacks one.
        if (battery is not null)
        {
            BatteryPercent = battery;
        }

        SignalStrength = signal;

        return ordering;
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(GroupName));
        OnPropertyChanged(nameof(SortRank));
        OnPropertyChanged(nameof(HasSignal));
        OnPropertyChanged(nameof(IsPresent));
        OnPropertyChanged(nameof(ConnectionActionText));
    }

    public string ConnectionActionText => IsConnected ? "Отключить" : "Подключить";

    /// <summary>False once every endpoint has gone out of range.</summary>
    public bool IsPresent => _isPresent;

    /// <summary>Builds the merge key for a snapshot: container id, else address, else endpoint id.</summary>
    public static string KeyFor(DeviceSnapshot snapshot)
    {
        if (snapshot.ContainerId != Guid.Empty)
        {
            return snapshot.ContainerId.ToString("N");
        }

        return snapshot.Address != 0 ? snapshot.Address.ToString("X12") : snapshot.Id;
    }
}
