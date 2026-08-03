# Архитектура

## Раскладка

```
src/BluetoothManagerPro/
├── App.xaml(.cs)              composition root: сервисы, трей, single instance
├── app.manifest               PerMonitorV2 DPI, asInvoker, supportedOS Win10/11
├── Assets/*.ico               иконки (генерируются tools/make_icons.py)
├── Themes/
│   ├── Palette.xaml           цвета, радиусы, типографика — только токены
│   ├── Icons.xaml             геометрии 24×24, обводкой
│   └── Controls.xaml          стили и шаблоны контролов
├── Models/                    DTO и чистая логика без Windows API
├── Interop/                   P/Invoke: bthprops.cpl, dwmapi, user32
├── Services/                  всё, что разговаривает с Windows
├── ViewModels/                состояние и команды
├── Views/                     окна, диалог сопряжения, трей
└── Infrastructure/            MVVM-минимум, конвертеры, single instance
```

## Слои

```
Views ──binding──► ViewModels ──► Services ──► WinRT / Win32
                                     │
                                  Models
```

**MVVM без фреймворка.** `ObservableObject` и `RelayCommand` — около 150 строк в
`Infrastructure/`. `CommunityToolkit.Mvvm` не подключён сознательно: на шесть view-model
он не окупается, тянет source generator (у которого есть известные трения с
`MarkupCompilePass1` в WPF) и ломает офлайн-сборку.

**DI-контейнера нет.** Граф собирается вручную в `App.OnStartup` — это десяток `new`.

## Ключевое решение: два источника правды

WinRT удобен, но не покрывает классический Bluetooth целиком. Поэтому:

| Задача | Кто отвечает |
|---|---|
| Обнаружение устройств, свойства, RSSI | `BluetoothDiscoveryService` (WinRT `DeviceWatcher`) |
| Сопряжение и удаление сопряжения | `BluetoothDiscoveryService` (WinRT custom pairing) |
| Вкл/выкл адаптера | `RadioService` (WinRT `Radio`) |
| **Реальное состояние подключения BR/EDR** | `ClassicBluetoothService` (Win32 `BluetoothFindFirstDevice`) |
| **Подключить / отключить BR/EDR** | `ClassicBluetoothService` (Win32 `BluetoothSetServiceState`) |
| Заряд батареи | `BatteryService` (GATT 0x180F + PnP-свойство) |

`MainViewModel` сводит оба источника: список строится из WinRT-событий, а флаг
«подключено» для классических устройств каждые 4 секунды перечитывается из Win32 —
свойство `System.Devices.Aep.IsConnected` для BR/EDR отстаёт.

## Два наблюдателя вместо одного

`BluetoothDiscoveryService` держит **пассивный** watcher по обоим транспортам —
он работает всё время и не трогает эфир — и поднимает **inquiry**-watcher только по кнопке
«Поиск», на 30 секунд. Постоянный inquiry по классическому Bluetooth забивает диапазон
2,4 ГГц и слышимо заикает подключённые наушники.

## Склейка конечных точек

Современная гарнитура публикует две association endpoint — BR/EDR и LE — и Windows
показывает обе. `DeviceViewModel` объединяет их по `System.Devices.Aep.ContainerId`
(запасной ключ — MAC-адрес), храня список идентификаторов: при удалении сопряжения нужно
убрать **все** конечные точки, иначе устройство вернётся в список.

## Потоки

События `DeviceWatcher` и `Radio.StateChanged` приходят из пула потоков WinRT. Сервисы
получают `Dispatcher` в конструкторе и переотправляют свои события через `InvokeAsync`,
поэтому view-model и коллекции живут строго на UI-потоке.

Диалог сопряжения показывается из обработчика `PairingRequested`, который выполняется на
фоновом потоке: он берёт `Deferral`, уходит на UI-поток через `InvokeAsync` (никогда —
через синхронный `Invoke`, это дедлок) и завершает deferral после ответа пользователя.

## Трей

`System.Windows.Forms.NotifyIcon` — только иконка и клики. Меню рисует собственное
WPF-окно `TrayFlyoutWindow`: `ContextMenuStrip` из WinForms не привести к тёмной теме без
собственного `ToolStripRenderer`. WinForms-версия выбрана вместо самодельного
`Shell_NotifyIcon` потому, что она сама обрабатывает сообщение `TaskbarCreated` — иначе
иконка исчезает после перезапуска explorer.exe.
