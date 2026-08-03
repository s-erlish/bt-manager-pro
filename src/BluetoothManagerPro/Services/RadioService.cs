using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using BluetoothManagerPro.Models;
using Windows.Devices.Radios;

namespace BluetoothManagerPro.Services;

/// <summary>
/// Turns the Bluetooth radio on and off through <see cref="Radio"/>.
///
/// Access has to be requested once per process. In an unpackaged desktop app the
/// request is normally granted silently, but on managed machines it can come back
/// denied — in that case the UI falls back to opening the Windows settings page.
/// </summary>
public sealed class RadioService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private Radio? _radio;
    private bool _accessGranted;

    public RadioService(Dispatcher dispatcher) => _dispatcher = dispatcher;

    /// <summary>Raised on the UI thread whenever the radio is switched on or off.</summary>
    public event Action<bool>? StateChanged;

    public bool IsAvailable => _radio is not null;

    public bool IsOn => _radio?.State == RadioState.On;

    /// <summary>True when the process may change the radio state, not just read it.</summary>
    public bool CanToggle => _accessGranted && _radio is not null;

    public async Task InitializeAsync()
    {
        // Access and enumeration are requested separately on purpose: RequestAccessAsync
        // belongs to the family of WinRT calls Microsoft documents as unsupported in
        // desktop processes, and a throw there must not cost us the radio itself.
        try
        {
            _accessGranted = await Radio.RequestAccessAsync() == RadioAccessStatus.Allowed;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Radio access request failed: {ex.Message}");
            _accessGranted = false;
        }

        try
        {
            var radios = await Radio.GetRadiosAsync();

            // GetRadiosAsync returns nothing to a 32-bit process on 64-bit Windows,
            // which is why the project pins PlatformTarget to x64.
            _radio = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);

            if (_radio is not null)
            {
                _radio.StateChanged += OnRadioStateChanged;
            }
        }
        catch (Exception ex)
        {
            // No radio, no driver, or the API is missing on this build — degrade quietly.
            Debug.WriteLine($"Radio enumeration failed: {ex.Message}");
            _radio = null;
        }
    }

    public async Task<OperationResult> SetStateAsync(bool on)
    {
        if (_radio is null)
        {
            return OperationResult.Fail("Bluetooth-адаптер не найден.");
        }

        // A denied access request is not proof that SetStateAsync will fail — in an
        // unpackaged process it usually succeeds anyway — so try regardless and let the
        // real return value decide.
        try
        {
            RadioAccessStatus status = await _radio.SetStateAsync(on ? RadioState.On : RadioState.Off);
            return status switch
            {
                RadioAccessStatus.Allowed => OperationResult.Ok(),
                RadioAccessStatus.DeniedByUser => OperationResult.Fail("Доступ к адаптеру запрещён пользователем."),

                // Almost always the hardware switch or aeroplane mode.
                RadioAccessStatus.DeniedBySystem => OperationResult.Fail(
                    "Адаптер заблокирован системой — проверьте режим «в самолёте» или аппаратный переключатель."),
                _ => OperationResult.Fail("Не удалось изменить состояние адаптера."),
            };
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Не удалось изменить состояние адаптера: {ex.Message}");
        }
    }

    private void OnRadioStateChanged(Radio sender, object args)
    {
        bool on = sender.State == RadioState.On;
        _dispatcher.InvokeAsync(() => StateChanged?.Invoke(on));
    }

    public void Dispose()
    {
        if (_radio is not null)
        {
            _radio.StateChanged -= OnRadioStateChanged;
            _radio = null;
        }
    }
}
