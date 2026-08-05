# Грабли Windows API

Каждый пункт здесь — либо ошибка, которую легко сделать и трудно заметить, либо ограничение,
которое нельзя обойти. Собрано по документации Microsoft Learn и отражено в коде.

## WinRT в desktop-приложении

WinRT работает в unpackaged WPF без манифеста и без пакета. Достаточно TFM с версией Windows —
`net8.0-windows10.0.19041.0`; SDK сам подтянет `Microsoft.Windows.SDK.NET.Ref`.
`Microsoft.Windows.CsWinRT` подключать **не нужно** — он для собственных проекций.

Но три вещи ломаются молча:

1. **`DeviceInformationPairing.PairAsync()` не поддерживается в desktop.** Он поднимает
   системный UI, которому нужен `CoreWindow`. Работает только `Pairing.Custom` — своя церемония.
   → `BluetoothDiscoveryService.PairAsync`

2. **`Radio.GetRadiosAsync()` возвращает пустой список 32-разрядному процессу на 64-разрядной
   Windows.** Не исключение, не ошибка — просто пусто, и тумблер перестаёт работать.
   → `<PlatformTarget>x64</PlatformTarget>`

3. **`Radio.RequestAccessAsync()` формально относится к семейству `RequestXxxAsync`,
   не поддерживаемому в desktop.** На практике возвращает `Allowed`. Код вызывает его отдельно
   от `GetRadiosAsync()` и не считает отказ приговором: `SetStateAsync` всё равно пробуется.
   → `RadioService.InitializeAsync`

## Перечисление устройств

Один watcher по обоим протоколам покрывает и BR/EDR, и LE, и спаренные, и найденные рядом:

```
(System.Devices.Aep.ProtocolId:="{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}"    // BR/EDR
 OR System.Devices.Aep.ProtocolId:="{bb7bb05e-5972-42b5-94fc-76eaa7084d49}") // LE
```

- `DeviceInformationKind.AssociationEndpoint` — обязательно.
- Свойства нужно запрашивать явно, иначе `Properties` будет почти пуст. Неизвестное имя
  свойства заставляет `CreateWatcher` бросить исключение — поэтому есть запасной,
  минимальный набор.
- **`Updated` несёт только изменившиеся свойства.** Надо хранить кэш `DeviceInformation`
  по `Id` и звать `cached.Update(update)`, а не собирать объект заново.
- События приходят из пула потоков — любые правки коллекций только через `Dispatcher`.
- Повторный `Start()` на watcher'е в состоянии `Stopping` бросает исключение.
- `BluetoothDevice.GetDeviceSelectorFromPairingState(false)` — единственный селектор,
  который включает `IssueInquiry` и реально сканирует эфир. Держать его постоянно нельзя:
  подключённые наушники начинают заикаться.

## Сопряжение

```csharp
var custom = info.Pairing.Custom;
custom.PairingRequested += handler;
var result = await custom.PairAsync(
    DevicePairingKinds.ConfirmOnly | DevicePairingKinds.DisplayPin |
    DevicePairingKinds.ProvidePin  | DevicePairingKinds.ConfirmPinMatch,
    DevicePairingProtectionLevel.Default);
```

- **Передавать все четыре церемонии.** Устройство, запросившее ту, которой нет в маске,
  падает с `RequiredHandlerNotRegistered`.
- **`Accept()` строго до `deferral.Complete()`.** Наоборот — `InvalidCeremonyData` или зависание.
- **Для `DisplayPin` вызывать `Accept()` до показа кода**, а не после: церемония не должна
  ждать пользователя.
- `ConfirmOnly` вообще не нуждается в deferral.
- `DevicePairingProtectionLevel.Default` — правильный выбор.
  `EncryptionAndAuthentication` отваливается на «Just Works»-периферии.
- `PairAsync` может висеть минутами — оборачивается своим таймаутом (здесь 60 с).

## Удаление сопряжения

`DeviceInformation.Pairing.UnpairAsync()` в desktop поддерживается.

**Грабля:** у современных наушников и мышей две конечные точки — BR/EDR и LE. Удалять надо
**обе**, иначе устройство остаётся в системе. Запасной путь для того, что WinRT не отпускает —
Win32 `BluetoothRemoveDevice`.

## Подключение и отключение классических устройств

Прямого WinRT API нет. `BluetoothDevice` умеет только читать SDP.

Рабочий путь — `BluetoothSetServiceState` из `bthprops.cpl`: включение или отключение
SDP-профилей устройства. Это ровно то, что делают «Параметры» Windows.

1. `BluetoothFindFirstRadio` → handle адаптера.
2. `BLUETOOTH_DEVICE_INFO` с заполненным `Address`, затем `BluetoothGetDeviceInfo` дозаполнит остальное.
3. `BluetoothEnumerateInstalledServices` — узнать реальный набор профилей устройства.
4. `BluetoothSetServiceState(..., BLUETOOTH_SERVICE_ENABLE | DISABLE)` по каждому.

Гарнитура: A2DP Sink `0000110B`, Handsfree `0000111E`, AVRCP `0000110E`.
HID: `00001124`. Все с базой `-0000-1000-8000-00805F9B34FB`.

**Грабли:**
- `dwSize` в `BLUETOOTH_DEVICE_INFO` должен быть 560 на x64. Только `Marshal.SizeOf<T>()`,
  никаких констант — иначе `ERROR_INVALID_PARAMETER (87)`.
- `ERROR_INVALID_PARAMETER` / `E_INVALIDARG` возвращается и когда профиль **уже** в нужном
  состоянии. Это успех, а не ошибка.
- Линк рвётся, только когда отключены **все** профили устройства.
- Права администратора не нужны, но `ERROR_ACCESS_DENIED (5)` обрабатывать надо.

Крайняя мера — `DeviceIoControl` с `IOCTL_BTH_DISCONNECT_DEVICE` (`0x0041000C`): рвёт ACL
немедленно. Windows переподключит устройство, если профили остались включёнными, поэтому
это дополнение к шагу 4, а не замена.

**Bluetooth LE отключить нельзя.** Соединение живёт, пока кто-то держит `GattDeviceService`
или `GattSession`. Чужую сессию не разорвать.

## Заряд батареи

Единого API нет. Два источника:

- **LE** — стандартный GATT Battery Service `0x180F`, характеристика `0x2A19`.
  `GattDeviceService` и `BluetoothLEDevice` обязательно освобождать.
  `GattSession.MaintainConnection = true` **не ставить** — держит соединение и сажает батарею гарнитуры.
- **BR/EDR** — свойство PnP `{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2` (byte, проценты).
  Это то, что показывают сами «Параметры» Windows. Ищется по `System.Devices.ContainerId`.

Покрытие — примерно половина гарнитур. Значение может появляться только при подключении.
В UI при отсутствии данных не показывается ничего.

## RSSI

`System.Devices.Aep.SignalStrength` (Int32, dBm) приходит для LE-устройств, пока они
рекламируются. Для BR/EDR — только во время inquiry. **У подключённого классического
устройства RSSI недоступен**, а подключённое LE-устройство перестаёт рекламироваться.
Поэтому индикатор сигнала показывается только там, где значение действительно есть.

## Автозапуск

`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` — прозрачно (видно в Диспетчере задач),
не требует прав администратора.

**Грабля, которую пропускают почти все:** если пользователь выключит автозапуск в
Диспетчере задач, значение в `Run` **останется**, а решение запишется в
`HKCU\...\Explorer\StartupApproved\Run` — 12 байт, у которых нулевой бит первого байта
означает «выключено». Чекбокс, читающий только `Run`, показывает неправду.
→ `AutoStartService.IsApproved`

**Вторая грабля:** при `PublishSingleFile` `Assembly.Location` возвращает пустую строку.
Путь к exe брать только из `Environment.ProcessPath`.

## Трей

`System.Windows.Forms.NotifyIcon` сам пересоздаёт иконку по сообщению `TaskbarCreated`.
Самодельный `Shell_NotifyIcon` этого не делает, и иконка исчезает после перезапуска
explorer.exe. `NotifyIcon.Text` обрезается оболочкой на 63 символах.

В `App.xaml` обязателен `ShutdownMode="OnExplicitShutdown"`, иначе закрытие окна убьёт
приложение вместе с треем.

## WPF

- Клик по `ToggleButton` записывает `IsChecked` как локальное значение и **стирает
  односторонний Binding**. Либо `Mode=TwoWay` с проверкой в сеттере, либо обычная кнопка
  плюс `DataTrigger` для состояния.
- Относительный `Source` внутри `MergedDictionaries` разрешается относительно корня
  приложения, а не папки файла. Надёжно — `pack://application:,,,/Themes/Palette.xaml`.
- Ошибочный ключ в `{StaticResource}` компилируется без ошибок и падает при запуске.
  → `tools/check_xaml_resources.py`
- `BeginAnimation(prop, null)` перед присваиванием свойства, у которого держится анимация,
  иначе присваивание игнорируется.
- `UseWPF` + `UseWindowsForms` в одном проекте добавляют `System.Windows.Forms` и
  `System.Drawing` в implicit usings, после чего `Application`, `Point`, `Brush` и
  `KeyEventArgs` становятся неоднозначными. Убирается через `<Using Remove="..." />`.
