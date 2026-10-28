# Zapret Engine Integration R5

## Назначение

R5 связывает безопасную цепочку Zapret R1–R4 с локальным IPC, но сохраняет принцип минимального доверия к GUI.

Клиент может отправлять только высокоуровневые команды:

- получить список стратегий;
- получить состояние Zapret;
- запросить запуск по безопасному `StrategyId`;
- запросить остановку.

## IPC v2

Протокол повышен до версии 2, а Windows Named Pipe — до `NetPilot.Service.v2`.

Для Zapret добавлены команды:

```text
ZapretListStrategies
ZapretGetStatus
ZapretStart
ZapretStop
```

`ZapretStart` содержит только:

```text
StrategyId
```

В запросе отсутствуют и не допускаются поля:

- путь к `winws.exe`;
- `EngineRoot`;
- рабочий каталог;
- raw `Arguments` / `ArgumentList`;
- BAT-код;
- PID для запуска/остановки;
- произвольная команда shell.

Это закреплено автоматическим тестом контракта.

## StrategyId

IPC принимает только нормализованный идентификатор формата:

```text
[A-Z0-9][A-Z0-9_]{0,47}
```

Примеры:

```text
ALT11
SIMPLE_FAKE
FAKE_TLS_AUTO
```

Пути, пробелы, строчные варианты, shell-аргументы и другие произвольные строки отклоняются ещё до передачи запроса обработчику.

## Service-side resolution

`NetPilot.Service` самостоятельно:

1. получает зарегистрированный защищённый engine root через `IZapretInstallationLocator`;
2. обнаруживает official `general*.bat` внутри этого root;
3. повторно читает и парсит только безопасные стратегии;
4. сопоставляет `StrategyId` с service-side каталогом;
5. получает runtime-параметры из `IZapretRuntimeConfigurationProvider`;
6. строит `ZapretLaunchPlan`;
7. передаёт его контролируемому runner.

GUI не может заменить ни один из этих шагов своими путями или raw-аргументами.

## Fail-closed до установщика

Установщик и защищённый machine-level config ещё не реализованы.

Поэтому production DI R5 использует:

- `UnconfiguredZapretInstallationLocator`;
- `UnconfiguredZapretRuntimeConfigurationProvider`.

Они не берут путь из:

- UI;
- переменных окружения;
- текущего каталога;
- PATH;
- импортированного пользовательского файла.

До будущего installer-этапа каталог стратегий отвечает `EngineNotInstalled`, а запуск не может получить runtime-конфигурацию.

## Read-only и изменяющие команды

После успешной SID/Session авторизации IPC:

- `ZapretListStrategies` — read-only;
- `ZapretGetStatus` — read-only;
- `ZapretStart` — требует разрешения `PrivilegedOperation.EngineStart`;
- `ZapretStop` — требует разрешения `PrivilegedOperation.EngineStop`.

`OperationPolicy` по-прежнему работает как `default deny`, поэтому Start/Stop в production R5 всё ещё возвращают `OperationDenied`.

Сам факт существования audited runner не делает его доступным GUI.

## Ответы

Ответ может содержать только безопасные данные состояния:

- список `Id + DisplayName`;
- `Stopped/Running`;
- ID собственной стратегии;
- PID только собственного процесса NetPilot runner для диагностики.

Клиент никогда не передаёт PID обратно для Stop: служба останавливает только сохранённый собственный process handle.

## Защита каталога

Повреждённая или не прошедшая parser стратегия пропускается и не получает право на запуск.

Если два official-файла нормализуются в один ID, например:

```text
A B  -> A_B
A-B  -> A_B
```

служба fail-closed возвращает `DuplicateStrategyId` вместо выбора одного из файлов.

## Следующий этап

Следующий этап — подготовка защищённой установки движка и machine-level конфигурации:

1. installer создаёт engine root в `%ProgramFiles%\NetPilot`;
2. обычный пользователь не имеет права записи в binaries/official strategies;
3. machine config указывает service-approved установленную версию/root;
4. проверяется целостность upstream-пакета;
5. runtime GameFilter/config хранится в защищённой структурированной форме;
6. только после этих проверок можно отдельно разрешить `EngineStart/EngineStop` в production `OperationPolicy`.
