# Сборка

## Что нужно

Только **.NET 8 SDK** — [dotnet.microsoft.com/download/dotnet/8.0](https://dotnet.microsoft.com/download/dotnet/8.0).
Visual Studio не требуется. Пакеты NuGet проект не использует, поэтому сборка работает и без интернета
(после того как SDK один раз скачал targeting packs).

Проверка:

```powershell
dotnet --version   # ожидается 8.0.x или новее
```

## Не собирать вовсе

Каждый прогон CI выкладывает `BluetoothManagerPro-win-x64.zip` в Artifacts — внутри
самодостаточный `.exe`, README и лицензия. Тег вида `v1.0.0` дополнительно публикует
тот же архив в Releases, откуда он качается по постоянной ссылке и без входа в GitHub.

## Быстрый способ

```powershell
.\build.ps1                 # ~25 МБ, нужен .NET 8 Desktop Runtime на целевой машине
.\build.ps1 -SelfContained  # ~70 МБ, ничего доустанавливать не нужно
.\build.ps1 -Runtime win-arm64 -SelfContained
```

Результат — один файл `publish\<rid>\BluetoothManagerPro.exe`.

## Вручную

```powershell
dotnet publish src\BluetoothManagerPro\BluetoothManagerPro.csproj `
    -c Release -r win-x64 --self-contained true `
    -o publish\win-x64
```

## Почему обязательно x64

`Radio.GetRadiosAsync()` возвращает **пустой список** 32-разрядному процессу на 64-разрядной
Windows — тумблер Bluetooth молча перестаёт работать. Поэтому в `.csproj` зафиксировано
`<PlatformTarget>x64</PlatformTarget>`, а публиковать нужно с `-r win-x64` или `-r win-arm64`.

## Чего в проекте намеренно нет

| | Почему |
|---|---|
| `PublishTrimmed` | WPF не поддерживает trimming — приложение падает или неверно рисует |
| `PublishReadyToRun` | известные проблемы с WPF в .NET 8 |
| NativeAOT | для WPF невозможен |
| Пакеты NuGet | всё нужное есть в `Microsoft.WindowsDesktop.App` и WinRT-проекции из SDK |

## Сборка на Linux или macOS

Скомпилировать проект можно и не на Windows — в `.csproj` включено
`<EnableWindowsTargeting>true</EnableWindowsTargeting>`:

```bash
dotnet build src/BluetoothManagerPro/BluetoothManagerPro.csproj -c Release
```

Это проверяет и C#, и разметку XAML. Запустить приложение так, разумеется, нельзя.

## Проверки, которые стоит прогнать перед коммитом

```bash
dotnet build src/BluetoothManagerPro/BluetoothManagerPro.csproj -c Release
python3 tools/check_xaml_resources.py
```

Второй скрипт ищет опечатки в `{StaticResource ...}`. Компилятор XAML их не ловит: неверный
ключ собирается без ошибок и выбрасывает исключение уже при запуске.

## Иконки

`src/BluetoothManagerPro/Assets/*.ico` генерируются из кода — бинарники в репозитории
можно перегенерировать после правки цвета:

```bash
python3 tools/make_icons.py
```

Скрипту не нужны сторонние библиотеки: растеризация и запись ICO/PNG написаны на голом Python.
