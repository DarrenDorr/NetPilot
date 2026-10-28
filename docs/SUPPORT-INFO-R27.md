# R27 — локальные сведения для поддержки

## Цель

Показывать в расширенном режиме минимальный набор технических сведений, который помогает идентифицировать сборку NetPilot при разборе проблем, не расширяя privileged boundary и не собирая пользовательские данные.

## Что отображается

Только три значения:

- версия NetPilot;
- платформа;
- архитектура процесса.

Для Windows host версия берётся из `AssemblyInformationalVersion` исполняемого `NetPilot.exe`. При сборке установщика это значение задаётся существующим параметром `-Version` в `scripts/build-installer.ps1`.

Платформа берётся из `WindowsPlatformInfo`, архитектура — из `RuntimeInformation.ProcessArchitecture`.

## Что намеренно не входит

R27 не читает и не показывает:

- имя пользователя Windows;
- SID;
- имя компьютера;
- IP-адреса;
- сетевые интерфейсы;
- пути к профилю пользователя или файлам;
- содержимое конфигурации;
- историю браузера, cookie, токены или аккаунты;
- сведения ChatGPT/OpenAI;
- содержимое сетевого трафика.

Сведения не записываются на диск и не отправляются автоматически.

## Архитектурная граница

`ApplicationSupportInfo` находится в общем `NetPilot.App` и не зависит от Windows API.

Windows-specific значения формируются только в `NetPilot.App.Windows` и передаются через `AppComposition`. `IDashboardService`, IPC v3 и LocalSystem-служба для R27 не меняются.

## Ограничение данных

`ApplicationSupportInfo` допускает только три публичных поля:

- `Version`;
- `Platform`;
- `Architecture`.

Каждое значение:

- приводится к одной строке;
- очищается от управляющих символов;
- ограничивается 128 символами;
- при пустом значении заменяется на `неизвестно`.

Unit-test закрепляет whitelist публичных полей, чтобы support-схема не расширялась случайно.

## UI

Сведения видны только в уже существующем блоке `Техническая информация` расширенного режима.

Рядом явно указано: сведения локальные и NetPilot не отправляет их автоматически.

## Проверки

Для commit `04296d90921035a9186aa071b6e37c618174bbf6` подтверждены:

- security-policy — PASS;
- Linux build — PASS;
- Core/IPC tests — PASS;
- Zapret parser tests — PASS;
- Windows security build/tests — PASS;
- Windows GUI build / Avalonia XAML — PASS;
- verified Inno Setup installer build — PASS;
- installer artifact sanity — PASS;
- CodeQL — PASS.

R27 не изменяет native admission: пока нет component set со статусом `ADMIT`, native install/start остаются fail-closed.
