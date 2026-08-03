using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using BluetoothManagerPro.Infrastructure;
using BluetoothManagerPro.Models;
using BluetoothManagerPro.Services;

namespace BluetoothManagerPro.ViewModels;

/// <summary>Drives the main window and the tray flyout — they share one list.</summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan ConnectionPollInterval = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan BatteryPollInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ResortDelay = TimeSpan.FromMilliseconds(250);

    private readonly BluetoothDiscoveryService _discovery;
    private readonly ClassicBluetoothService _classic;
    private readonly RadioService _radio;
    private readonly BatteryService _battery;
    private readonly AutoStartService _autoStart;
    private readonly SettingsService _settingsStore;
    private readonly ThemeService _theme;
    private readonly AppSettings _settings;

    private readonly Dictionary<string, DeviceViewModel> _byKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _endpointToKey = new(StringComparer.OrdinalIgnoreCase);

    private readonly DispatcherTimer _connectionTimer;
    private readonly DispatcherTimer _batteryTimer;
    private readonly DispatcherTimer _resortTimer;

    private string _searchText = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isBluetoothOn;
    private bool _isRadioAvailable;
    private bool _isScanning;
    private bool _isSettingsOpen;

    public MainViewModel(
        BluetoothDiscoveryService discovery,
        ClassicBluetoothService classic,
        RadioService radio,
        BatteryService battery,
        AutoStartService autoStart,
        SettingsService settingsStore,
        ThemeService theme,
        AppSettings settings,
        Dispatcher dispatcher)
    {
        _discovery = discovery;
        _classic = classic;
        _radio = radio;
        _battery = battery;
        _autoStart = autoStart;
        _settingsStore = settingsStore;
        _theme = theme;
        _settings = settings;

        Accents = AccentPreset.All.Select(p => new AccentSwatchViewModel(p, ApplyAccent)).ToArray();
        SyncThemeSelection();

        Devices = new ObservableCollection<DeviceViewModel>();
        DevicesView = (ListCollectionView)CollectionViewSource.GetDefaultView(Devices);
        DevicesView.CustomSort = new DeviceComparer();
        DevicesView.Filter = FilterDevice;
        DevicesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DeviceViewModel.GroupName)));

        _discovery.DeviceChanged += OnDeviceChanged;
        _discovery.DeviceRemoved += OnDeviceRemoved;
        _discovery.ScanningChanged += on => IsScanning = on;
        _radio.StateChanged += OnRadioStateChanged;

        ScanCommand = new RelayCommand(StartScan, () => !IsScanning && IsBluetoothOn);
        ToggleConnectionCommand = new AsyncRelayCommand(ToggleConnectionAsync, CanOperate);
        UnpairCommand = new AsyncRelayCommand(UnpairAsync, CanOperate);
        PairCommand = new AsyncRelayCommand(PairAsync, CanOperate);
        ToggleFavoriteCommand = new RelayCommand(ToggleFavorite, CanOperate);
        OpenSystemSettingsCommand = new RelayCommand(OpenSystemSettings);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);

        // This DispatcherTimer overload starts the timer immediately, so stop each one
        // until InitializeAsync has had a chance to bring the services up.
        _connectionTimer = new DispatcherTimer(ConnectionPollInterval, DispatcherPriority.Background,
            async (_, _) => await RefreshClassicStateAsync(), dispatcher);
        _connectionTimer.Stop();
        _batteryTimer = new DispatcherTimer(BatteryPollInterval, DispatcherPriority.Background,
            async (_, _) => await RefreshBatteryAsync(), dispatcher);
        _batteryTimer.Stop();

        // Re-sorting on every watcher update makes the list jump under the cursor;
        // coalesce the churn into one refresh.
        _resortTimer = new DispatcherTimer(ResortDelay, DispatcherPriority.Background, (_, _) =>
        {
            _resortTimer!.Stop();
            DevicesView.Refresh();
            OnPropertyChanged(nameof(IsListEmpty));
        }, dispatcher);
        _resortTimer.Stop();
    }

    public ObservableCollection<DeviceViewModel> Devices { get; }

    public ListCollectionView DevicesView { get; }

    /// <summary>Favourites, connected first — this is what the tray flyout shows.</summary>
    public IReadOnlyList<DeviceViewModel> QuickAccessDevices =>
        Devices.Where(d => (d.IsFavorite || d.IsConnected) && IsWorthShowing(d))
               .OrderBy(d => d.IsConnected ? 0 : 1)
               .ThenBy(d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase)
               .Take(6)
               .ToArray();

    public bool HasQuickAccessDevices => QuickAccessDevices.Count > 0;

    public IReadOnlyList<SystemSettingsEntry> SystemSettings => SystemSettingsLauncher.Entries;

    public ICommand ScanCommand { get; }

    public ICommand ToggleConnectionCommand { get; }

    public ICommand UnpairCommand { get; }

    public ICommand PairCommand { get; }

    public ICommand ToggleFavoriteCommand { get; }

    public ICommand OpenSystemSettingsCommand { get; }

    public ICommand ClearSearchCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                OnPropertyChanged(nameof(HasSearchText));
                DevicesView.Refresh();
                OnPropertyChanged(nameof(IsListEmpty));
            }
        }
    }

    public bool HasSearchText => !string.IsNullOrEmpty(SearchText);

    public bool IsListEmpty => DevicesView.IsEmpty;

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    public bool IsBluetoothOn
    {
        get => _isBluetoothOn;
        private set
        {
            if (SetProperty(ref _isBluetoothOn, value))
            {
                OnPropertyChanged(nameof(RadioStatusText));
                OnPropertyChanged(nameof(RadioSwitch));
            }
        }
    }

    /// <summary>
    /// Two-way face of <see cref="IsBluetoothOn"/> for the switch. A ToggleButton writes
    /// IsChecked as a local value on click, which would tear down a one-way binding — so
    /// the write has to be accepted here and reconciled against what the radio actually did.
    /// </summary>
    public bool RadioSwitch
    {
        get => _isBluetoothOn;
        set
        {
            if (value == _isBluetoothOn)
            {
                return;
            }

            _ = ApplyRadioAsync(value);
        }
    }

    public bool IsRadioAvailable
    {
        get => _isRadioAvailable;
        private set => SetProperty(ref _isRadioAvailable, value);
    }

    public string RadioStatusText => IsBluetoothOn ? "Bluetooth включён" : "Bluetooth выключен";

    public bool IsScanning
    {
        get => _isScanning;
        private set => SetProperty(ref _isScanning, value);
    }

    // ---- Tabs ---------------------------------------------------------------

    /// <summary>
    /// The two bottom tabs. Both are two-way bound to their RadioButton, and each keeps
    /// the other honest so the content switches even when navigated by keyboard.
    /// </summary>
    public bool IsDevicesTab
    {
        get => !_isSettingsOpen;
        set
        {
            if (value && SetProperty(ref _isSettingsOpen, false))
            {
                OnPropertyChanged(nameof(IsSettingsTab));
            }
        }
    }

    public bool IsSettingsTab
    {
        get => _isSettingsOpen;
        set
        {
            if (value && SetProperty(ref _isSettingsOpen, true))
            {
                OnPropertyChanged(nameof(IsDevicesTab));
            }
        }
    }

    // ---- Theme --------------------------------------------------------------

    /// <summary>The accent picker, ordered as declared.</summary>
    public IReadOnlyList<AccentSwatchViewModel> Accents { get; }

    /// <summary>
    /// Light mode is a washed-out tint of the accent rather than white — the point is a
    /// bright theme that is still calm to look at.
    /// </summary>
    public bool IsLightTheme
    {
        get => _settings.ThemeMode == ThemeMode.Light;
        set
        {
            ThemeMode mode = value ? ThemeMode.Light : ThemeMode.Dark;
            if (_settings.ThemeMode == mode)
            {
                return;
            }

            _settings.ThemeMode = mode;
            _theme.Apply(AccentPreset.Resolve(_settings.AccentId), mode);
            foreach (AccentSwatchViewModel swatch in Accents)
            {
                swatch.Preview(mode);
            }

            Persist();
            OnPropertyChanged();
        }
    }

    public string AppFooter => AppInfo.Footer;

    public bool AutoStart
    {
        get => _settings.AutoStart;
        set
        {
            if (_settings.AutoStart == value)
            {
                return;
            }

            if (!_autoStart.TrySetEnabled(value))
            {
                StatusMessage = "Не удалось изменить автозапуск.";
                return;
            }

            _settings.AutoStart = value;
            Persist();
            OnPropertyChanged();
        }
    }

    public bool CloseToTray
    {
        get => _settings.CloseToTray;
        set
        {
            if (_settings.CloseToTray == value)
            {
                return;
            }

            _settings.CloseToTray = value;
            Persist();
            OnPropertyChanged();
        }
    }

    public bool StartMinimized
    {
        get => _settings.StartMinimized;
        set
        {
            if (_settings.StartMinimized == value)
            {
                return;
            }

            _settings.StartMinimized = value;
            Persist();
            OnPropertyChanged();
        }
    }

    public bool HideUnnamedDevices
    {
        get => _settings.HideUnnamedDevices;
        set
        {
            if (_settings.HideUnnamedDevices == value)
            {
                return;
            }

            _settings.HideUnnamedDevices = value;
            Persist();
            OnPropertyChanged();
            DevicesView.Refresh();
        }
    }

    /// <summary>Raised when a pairing ceremony needs the user. The view supplies the dialog.</summary>
    public Func<PairingPrompt, Task<PairingAnswer>>? PairingPromptHandler { get; set; }

    // ---- Lifetime -----------------------------------------------------------

    public async Task InitializeAsync()
    {
        // Reconcile the checkbox with what Windows will actually do at logon.
        bool registered = _autoStart.IsEnabled;
        if (registered != _settings.AutoStart)
        {
            _settings.AutoStart = registered;
            OnPropertyChanged(nameof(AutoStart));
            Persist();
        }

        await _radio.InitializeAsync();
        IsRadioAvailable = _radio.IsAvailable;
        IsBluetoothOn = _radio.IsOn;

        if (!IsRadioAvailable && !_classic.HasRadio())
        {
            StatusMessage = "Bluetooth-адаптер не найден.";
        }

        _discovery.Start();
        if (_discovery.IsUnavailable)
        {
            StatusMessage = "Не удалось получить доступ к Bluetooth. Откройте параметры Windows.";
        }

        _connectionTimer.Start();
        _batteryTimer.Start();

        await RefreshClassicStateAsync();
        await RefreshBatteryAsync();
    }

    // ---- Watcher plumbing ---------------------------------------------------

    private void OnDeviceChanged(DeviceSnapshot snapshot)
    {
        string key = DeviceViewModel.KeyFor(snapshot);
        _endpointToKey[snapshot.Id] = key;

        if (_byKey.TryGetValue(key, out DeviceViewModel? existing))
        {
            if (existing.Apply(snapshot))
            {
                ScheduleResort();
            }

            return;
        }

        var device = new DeviceViewModel(key, snapshot)
        {
            IsFavorite = _settings.Favorites.Contains(key, StringComparer.OrdinalIgnoreCase),
            Alias = _settings.Aliases.TryGetValue(key, out string? alias) ? alias : null,
        };

        _byKey[key] = device;
        Devices.Add(device);
        ScheduleResort();
        OnPropertyChanged(nameof(IsListEmpty));
        OnPropertyChanged(nameof(QuickAccessDevices));
    }

    private void OnDeviceRemoved(string endpointId)
    {
        if (!_endpointToKey.TryGetValue(endpointId, out string? key) ||
            !_byKey.TryGetValue(key, out DeviceViewModel? device))
        {
            return;
        }

        _endpointToKey.Remove(endpointId);

        // Keep paired devices on screen even when they stop advertising — that is the
        // whole point of the list. Only drop endpoints we discovered opportunistically.
        if (!device.RemoveEndpoint(endpointId) || (!device.IsPaired && !device.IsConnected))
        {
            _byKey.Remove(key);
            Devices.Remove(device);
            OnPropertyChanged(nameof(IsListEmpty));
            OnPropertyChanged(nameof(QuickAccessDevices));
        }
    }

    private void OnRadioStateChanged(bool on)
    {
        IsBluetoothOn = on;

        // Every watcher dies silently when the radio goes away, so rebuild them.
        _discovery.Restart();
        if (!on)
        {
            foreach (DeviceViewModel device in Devices)
            {
                device.IsConnected = false;
            }
        }

        ScheduleResort();
    }

    private void ScheduleResort()
    {
        _resortTimer.Stop();
        _resortTimer.Start();
        OnPropertyChanged(nameof(QuickAccessDevices));
        OnPropertyChanged(nameof(HasQuickAccessDevices));
    }

    private bool FilterDevice(object item)
        => item is DeviceViewModel device && IsWorthShowing(device) && MatchesSearch(device);

    /// <summary>
    /// Whether the row represents something the user would recognise. Kept separate from
    /// the search text so the tray flyout can apply it too — the flyout must not inherit
    /// whatever is typed in the main window's search box.
    /// </summary>
    private bool IsWorthShowing(DeviceViewModel device)
        => device.IsPaired || (device.IsPresent && (!HideUnnamedDevices || device.HasName));

    private bool MatchesSearch(DeviceViewModel device)
        => string.IsNullOrWhiteSpace(SearchText)
           || device.DisplayName.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase);

    // ---- Polling ------------------------------------------------------------

    /// <summary>
    /// The AEP IsConnected flag lags for BR/EDR devices, so the authoritative state comes
    /// from the classic stack. This also fills in connection state when no LE endpoint exists.
    /// </summary>
    private async Task RefreshClassicStateAsync()
    {
        Dictionary<ulong, ClassicDeviceState> states;
        try
        {
            states = await _classic.GetKnownDevicesAsync();
        }
        catch (Exception)
        {
            return;
        }

        // An empty sweep cannot be told apart from a radio that was momentarily busy, and
        // acting on it would flicker every device to disconnected.
        if (states.Count == 0)
        {
            return;
        }

        bool ordering = false;
        foreach (DeviceViewModel device in Devices)
        {
            bool connected;
            if (states.TryGetValue(device.Address, out ClassicDeviceState state))
            {
                connected = state.IsConnected;
            }
            else if (device.Address != 0 && device.HasClassicEndpoint)
            {
                // The sweep reports every remembered BR/EDR device, so one that is missing
                // from it is not connected. Without this a device that drops off the
                // classic stack keeps whatever flag it had and stays stuck on "Подключено".
                connected = false;
            }
            else
            {
                // LE-only endpoints never appear in the classic sweep; leave them alone.
                continue;
            }

            if (device.IsConnected != connected)
            {
                device.IsConnected = connected;
                ordering = true;
            }
        }

        if (ordering)
        {
            ScheduleResort();
        }
    }

    private async Task RefreshBatteryAsync()
    {
        try
        {
            Dictionary<Guid, int> byContainer = await _battery.ReadClassicByContainerAsync();
            foreach (DeviceViewModel device in Devices)
            {
                if (Guid.TryParseExact(device.Key, "N", out Guid container) &&
                    byContainer.TryGetValue(container, out int percent))
                {
                    device.BatteryPercent = percent;
                }
            }

            // GATT reads open a connection, so only ask devices that are already connected.
            foreach (DeviceViewModel device in Devices.Where(d => d.IsConnected && d.BatteryPercent is null).ToArray())
            {
                int? level = await _battery.ReadLowEnergyAsync(device.PrimaryEndpointId);
                if (level is not null)
                {
                    device.BatteryPercent = level;
                }
            }
        }
        catch (Exception)
        {
            // Battery is best effort by definition; never surface a failure here.
        }
    }

    // ---- Commands -----------------------------------------------------------

    private static bool CanOperate(object? parameter) => parameter is DeviceViewModel;

    private void StartScan()
    {
        StatusMessage = string.Empty;
        _discovery.StartInquiry();
    }

    private async Task ApplyRadioAsync(bool on)
    {
        OperationResult result = await _radio.SetStateAsync(on);
        if (result.Success)
        {
            StatusMessage = string.Empty;
            return;
        }

        StatusMessage = result.Message;

        // Snap the switch back to the radio's real state.
        OnPropertyChanged(nameof(RadioSwitch));
    }

    private async Task ToggleConnectionAsync(object? parameter)
    {
        if (parameter is not DeviceViewModel device)
        {
            return;
        }

        if (!device.IsPaired)
        {
            await PairAsync(device);
            return;
        }

        if (device.Address == 0 || !device.HasClassicEndpoint)
        {
            // LE-only peripherals have no service state to flip: Windows connects them
            // on demand when an app opens a GATT session.
            StatusMessage = "Устройство Bluetooth LE подключается автоматически при обращении к нему.";
            return;
        }

        bool connect = !device.IsConnected;
        device.IsBusy = true;
        try
        {
            OperationResult result = await _classic.SetConnectedAsync(device.Address, device.Category, connect);
            StatusMessage = result.Success ? string.Empty : result.Message;

            if (result.Success)
            {
                device.IsConnected = connect;
                ScheduleResort();
            }
        }
        finally
        {
            device.IsBusy = false;
        }

        await RefreshClassicStateAsync();
    }

    private async Task PairAsync(object? parameter)
    {
        if (parameter is not DeviceViewModel device)
        {
            return;
        }

        device.IsBusy = true;
        try
        {
            Func<PairingPrompt, Task<PairingAnswer>> prompt =
                PairingPromptHandler ?? (_ => Task.FromResult(PairingAnswer.Yes()));

            OperationResult result = await _discovery.PairAsync(device.PrimaryEndpointId, prompt);
            StatusMessage = result.Success ? $"«{device.DisplayName}» сопряжено." : result.Message;
        }
        finally
        {
            device.IsBusy = false;
        }
    }

    private async Task UnpairAsync(object? parameter)
    {
        if (parameter is not DeviceViewModel device)
        {
            return;
        }

        device.IsBusy = true;
        try
        {
            // Remove every endpoint: leaving the LE half paired makes the device reappear.
            var failures = new List<string>();
            foreach (string endpointId in device.EndpointIds.ToArray())
            {
                OperationResult result = await _discovery.UnpairAsync(endpointId);
                if (!result.Success)
                {
                    failures.Add(result.Message);
                }
            }

            if (failures.Count > 0 && device.Address != 0)
            {
                // Last resort for endpoints WinRT will not let go of.
                OperationResult fallback = await _classic.RemoveAsync(device.Address);
                if (fallback.Success)
                {
                    failures.Clear();
                }
            }

            StatusMessage = failures.Count == 0
                ? $"«{device.DisplayName}» удалено."
                : failures[0];

            if (failures.Count == 0)
            {
                _byKey.Remove(device.Key);
                Devices.Remove(device);
                _settings.Favorites.RemoveAll(f => string.Equals(f, device.Key, StringComparison.OrdinalIgnoreCase));
                Persist();
                OnPropertyChanged(nameof(IsListEmpty));
                OnPropertyChanged(nameof(QuickAccessDevices));
            }
        }
        finally
        {
            device.IsBusy = false;
        }
    }

    private void ToggleFavorite(object? parameter)
    {
        if (parameter is not DeviceViewModel device)
        {
            return;
        }

        device.IsFavorite = !device.IsFavorite;
        _settings.Favorites.RemoveAll(f => string.Equals(f, device.Key, StringComparison.OrdinalIgnoreCase));
        if (device.IsFavorite)
        {
            _settings.Favorites.Add(device.Key);
        }

        Persist();
        ScheduleResort();
    }

    private void ApplyAccent(AccentPreset preset)
    {
        if (string.Equals(_settings.AccentId, preset.Id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _settings.AccentId = preset.Id;
        _theme.Apply(preset, _settings.ThemeMode);
        Persist();
    }

    /// <summary>Points the picker at whatever is stored, without re-applying the theme.</summary>
    private void SyncThemeSelection()
    {
        AccentPreset current = AccentPreset.Resolve(_settings.AccentId);
        foreach (AccentSwatchViewModel swatch in Accents)
        {
            swatch.SetSelectedQuietly(swatch.Preset.Id == current.Id);
            swatch.Preview(_settings.ThemeMode);
        }
    }

    private void OpenSystemSettings(object? parameter)
    {
        string target = parameter as string ?? "ms-settings:bluetooth";
        if (!SystemSettingsLauncher.Launch(target))
        {
            StatusMessage = "Не удалось открыть параметры Windows.";
        }
    }

    private void Persist() => _settingsStore.Save(_settings);

    public void Dispose()
    {
        _connectionTimer.Stop();
        _batteryTimer.Stop();
        _resortTimer.Stop();

        _discovery.DeviceChanged -= OnDeviceChanged;
        _discovery.DeviceRemoved -= OnDeviceRemoved;
        _radio.StateChanged -= OnRadioStateChanged;
    }

    /// <summary>Connected, then favourites, then paired, then the rest; alphabetical within a rank.</summary>
    private sealed class DeviceComparer : IComparer
    {
        public int Compare(object? x, object? y)
        {
            if (x is not DeviceViewModel left || y is not DeviceViewModel right)
            {
                return 0;
            }

            int rank = left.SortRank.CompareTo(right.SortRank);
            return rank != 0
                ? rank
                : string.Compare(left.DisplayName, right.DisplayName, StringComparison.CurrentCultureIgnoreCase);
        }
    }
}
