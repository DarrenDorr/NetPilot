# R20 — уменьшение attack surface службы без ADMIT

## Статус

R20 усиливает fail-closed режим `NetPilot.Service` для состояния, когда native admission не содержит ни одного component set со статусом `ADMIT`.

До R20 операции `EngineInstall` и `EngineStart` уже были запрещены `OperationPolicy`, updater использовал пустой admission catalog, а GUI — `NoAdmittedEngineProfileRuntime`. Однако production DI LocalSystem-службы всё ещё создавал реальные инфраструктурные объекты, которые теоретически умели скачать release или запустить процесс, если бы другой защитный слой в будущем был случайно ослаблен.

R20 убирает эти возможности из production composition до появления реального `ADMIT`.

## R20.1 — несетевой updater transport

Добавлен:

`NoAdmittedZapretReleaseTransport`

Он реализует узкий `IZapretReleaseTransport`, но:

- не содержит `HttpClient`;
- не содержит URL;
- не выполняет DNS;
- не выполняет HTTP/HTTPS;
- не читает и не создаёт download path;
- на любой вызов возвращает `NoAdmittedEngineRelease`.

При отсутствии `ADMIT` `NetPilot.Service` больше не регистрирует `GithubZapretReleaseClient`.

Таким образом, даже если кто-то ошибочно вызовет updater ниже слоя admission catalog, LocalSystem-процесс не получает сетевого transport для скачивания native-кода.

## R20.2 — fail-closed process launcher

Добавлен:

`NoAdmittedZapretProcessLauncher`

Он реализует `IZapretProcessLauncher`, но:

- не содержит `System.Diagnostics.Process`;
- не вызывает `Process.Start`;
- не имеет публичного конструктора;
- не содержит изменяемого instance state;
- на любую попытку запуска отвечает `NoAdmittedEngine`.

При отсутствии `ADMIT` production DI службы использует именно этот launcher.

`SystemZapretProcessLauncher` остаётся в исходном коде как отдельно проверяемая реализация для будущего admitted runtime и тестов, но LocalSystem-служба в текущей конфигурации его не получает.

## Что происходит с EngineStop

`EngineStop` остаётся разрешённым в `CreateNoAdmittedEnginePolicy()`.

Это не возвращает право на запуск. `ZapretProcessRunner.StopAsync()` может работать только с процессом, которым конкретный экземпляр runner уже владеет в памяти. Наличие stop-capability не требует рабочего system launcher.

R20 не добавляет поиск и завершение произвольных процессов по имени, PID из пользовательского ввода или пути.

## Текущие независимые рубежи

При отсутствии `ADMIT` одновременно действуют:

1. GUI использует `NoAdmittedEngineProfileRuntime`;
2. Service policy запрещает `EngineInstall` и `EngineStart`;
3. updater использует пустой `NoAdmittedZapretReleaseCatalog`;
4. updater transport — `NoAdmittedZapretReleaseTransport`, без сети;
5. process launcher — `NoAdmittedZapretProcessLauncher`, без `Process.Start`;
6. CI проверяет production wiring и не позволяет незаметно вернуть реальные transport/launcher.

Это defence in depth: ошибка одного слоя не должна автоматически давать LocalSystem-службе возможность скачать или запустить native-код.

## Что потребуется после появления ADMIT

Наличие нового upstream release само по себе ничего не меняет.

Чтобы вернуть реальный download/process path, потребуется отдельное изменение production DI после:

1. нового native admission с точной release identity;
2. статуса `ADMIT` для полного component set;
3. проверки EXE/DLL/SYS и зависимостей;
4. проверки updater identity;
5. проверки typed profile compiler;
6. HIGH_ASSURANCE review privileged path;
7. CI-тестов admitted wiring.

До этого реальные GitHub transport и system process launcher не должны автоматически появляться в LocalSystem-службе.
