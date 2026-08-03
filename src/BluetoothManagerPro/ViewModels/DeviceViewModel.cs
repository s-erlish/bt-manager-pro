using System;
using System.Collections.Generic;
using System.Linq;
using BluetoothManagerPro.Infrastructure;
using BluetoothManagerPro.Models;

namespace BluetoothManagerPro.ViewModels;

/// <summary>
/// One physical device in the list.
///
/// A modern headset publishes two association endpoints — one BR/EDR, one LE — and
/// Windows shows both. Here they are folded into a single row keyed by container id,
/// with the endpoint ids kept so unpair can remove every one of them.
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

    public DeviceViewModel(string key, DeviceSnapshot first)
    {
        Key = key;
        Apply(first);
    }

    /// <summary>Stable identity across endpoints: container id, else address, else endpoint id.</summary>
    public string Key { get; }

    public IReadOnlyCollection<string> EndpointIds => _endpoints.Keys;

    /// <summary>The endpoint to drive pairing through — BR/EDR first, since it carries the profiles.</summary>
    public string PrimaryEndpointId =>
        _endpoints.Values.OrderBy(e => e.Transport == DeviceTransport.Classic ? 0 : 1)
                         .Select(e => e.Id)
                         .FirstOrDefault() ?? Key;

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
    public int SignalBars => SignalStrength switch
    {
        null => 0,
        >= -55 => 4,
        >= -67 => 3,
        >= -80 => 2,
        _ => 1,
    };

    public bool HasClassicEndpoint => _endpoints.Values.Any(e => e.Transport == DeviceTransport.Classic);

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

    private bool Recompute()
    {
        DeviceSnapshot[] all = _endpoints.Values.ToArray();
        if (all.Length == 0)
        {
            return false;
        }

        bool paired = all.Any(e => e.IsPaired);
        bool connected = all.Any(e => e.IsConnected);
        bool ordering = paired != _isPaired || connected != _isConnected;

        Name = all.Select(e => e.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? string.Empty;
        Category = all.Select(e => e.Category).FirstOrDefault(c => c != DeviceCategory.Unknown);
        Address = all.Select(e => e.Address).FirstOrDefault(a => a != 0);
        IsPaired = paired;
        IsConnected = connected;

        // Never clear a battery reading just because the other endpoint does not carry one.
        int? battery = all.Select(e => e.BatteryPercent).FirstOrDefault(b => b is not null);
        if (battery is not null)
        {
            BatteryPercent = battery;
        }

        // Signal only means something for an endpoint that is still advertising.
        SignalStrength = all.Where(e => e.Transport == DeviceTransport.LowEnergy)
                            .Select(e => e.SignalStrength)
                            .FirstOrDefault(s => s is not null);

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
    public bool IsPresent => _endpoints.Values.Any(e => e.IsPresent);

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
