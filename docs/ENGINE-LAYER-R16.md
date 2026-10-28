# Engine Layer R16 — независимый слой движков Net.Pilot

Дата: 28.10.2026
Статус: R16.1–R16.4 реализованы; runtime и GUI переведены в fail-closed режим до появления admitted-движка.

## Цель

Net.Pilot не должен быть оболочкой над одной конкретной сборкой Zapret, Flowseal или zapret2. Пользователь работает с понятными профилями Net.Pilot, а конкретный сетевой движок остаётся внутренней заменяемой реализацией.

Встроенные продуктовые профили:

- `standard`;
- `discord`;
- `youtube`;
- `discord-youtube`.

`auto` намеренно не является профилем исполнения: автоматический выбор — отдельная логика Automation поверх допустимых профилей.

Это пользовательские намерения, а не команды движка.

## Архитектурная граница

GUI / Automation
→ `EngineProfileId`
→ `BuiltInEngineProfileCatalog`
→ `BuiltInEngineProfileCompiler`
→ `BuiltInEngineProfilePlan`
→ `IEngineProfileRuntime`
→ локальная реализация конкретного admitted-движка
→ engine-specific deterministic compiler
→ фиксированный executable + проверенный массив аргументов
→ привилегированный runtime.

Внешний ввод не должен перескакивать через эти уровни.

## Что запрещено

Публичный контракт профилей Net.Pilot не должен принимать или хранить:

- произвольную командную строку;
- путь к EXE/BAT/CMD/PowerShell;
- путь к Lua-файлу;
- Lua-код;
- `--lua-init` из пользовательской конфигурации;
- `luaexec:code` из пользовательской конфигурации;
- произвольный URL загрузки;
- произвольные аргументы процесса;
- имя исполняемого файла, присланное извне.

Неизвестный `EngineProfileId` обязан отклоняться fail-closed.

## R16.1 — профильный контракт

Добавлены `EngineProfileId`, `EngineProfileDescriptor`, `EngineProfileRuntimeState`, `EngineProfileRuntimeException` и `IEngineProfileRuntime`.

`ActivateProfileAsync` принимает только `EngineProfileId` и `CancellationToken`. Публичный descriptor не содержит `Arguments`, `Command`, `Script`, `Lua`, `Path` или `Url`.

## R16.2 — переходный Automation adapter

Добавлен `EngineProfileStrategyRuntimeAdapter`. Существующий selector пока сохраняется ради совместимости R15, но при переходе через новый adapter строка обязана повторно пройти `EngineProfileId.TryParse`.

Строки вроде `--help`, `../profile`, `profile.lua`, `luaexec:code`, `x=1` и `a;b` не могут пройти через адаптер к движку.

Legacy `WindowsZapretStrategyRuntime` пока не подменяется новым путём.

## R16.3 — встроенный каталог профилей

`BuiltInEngineProfileCatalog`, schema version `1`, хранится в исходном коде и read-only.

Он содержит только ID и отображаемое имя. Не содержит raw args, путей, URL, Lua/BAT-кода или имён executable. Синтаксически корректный неизвестный ID не становится доверенным.

## R16.4 — типизированный план намерения

Добавлены `BuiltInEngineProfileKind`, `BuiltInEngineProfilePlan` и `BuiltInEngineProfileCompiler`.

Компилятор детерминированно отображает только четыре встроенных профиля в enum-план. Неизвестный ID → `EngineProfileRuntimeException` с кодом `UnknownProfile`.

`BuiltInEngineProfilePlan` содержит только `ProfileId` и `Kind`. В нём нет executable, пути, arguments, command, Lua, script или URL. Его constructor не является публичным.

Это ещё не process launch plan. Конкретный движок в будущем обязан отдельно преобразовать этот типизированный plan в собственный внутренний план запуска и пройти security review.

## Runtime admission gate

На 28.10.2026 ни один исследованный native component set не имеет статуса `ADMIT`.

Поэтому `NetPilot.Service` использует `OperationPolicy.CreateNoAdmittedEnginePolicy()`:

- `EngineInstall` — запрещён;
- `EngineStart` — запрещён;
- `EngineStop` — разрешён только для безопасной остановки уже существующего legacy-процесса;
- остальные привилегированные операции остаются запрещёнными, если не разрешены отдельно.

Это не только договорённость в документации. `scripts/security-policy-check.sh` проверяет native-admission manifests и при отсутствии `ADMIT` завершает CI ошибкой, если служба перестала использовать fail-closed policy или снова получила прямые capabilities `EngineInstall`/`EngineStart`.

## GUI admission state

GUI больше не предлагает установку или подбор профиля, если текущая сборка не имеет admitted runtime capability.

`EngineInstallationState` безопасно по умолчанию имеет `CanInstall=false` и `CanStart=false`.

Если движок отсутствует, пользователь видит, что безопасная версия пока не допущена. Если на ПК остался legacy-движок, интерфейс может показать факт его установки, но запуск остаётся заблокированным.

IPC v3 при этом не расширялся новыми полями: строгая JSON-схема и имя pipe не менялись.

## Текущее состояние движков

### Flowseal / zapret1

Flowseal `1.10.4` использует zapret1 `v72.9`.

Native admission: **`BLOCK`**.

Причины зафиксированы в `security/native-admission/flowseal-zapret-1.10.4.yaml`. Flowseal `1.10.4` остаётся последним опубликованным релизом и текущим `main` upstream на 28.10.2026.

### zapret2 v1.0.6

`bol-van/zapret2 v1.0.6` также имеет native admission **`BLOCK`**.

Upstream issue #335 воспроизводит heap-use-after-free на точном release commit `221d7dff24bfec0cd5ac0a5070b8acef2008af3d`. Исправление появилось только после релиза в commit `4a33055448ce5f4e7acc704e1fbe0d8a8a6aa08d` (`nfqws2: fix UAF`).

Новый опубликованный тег, содержащий это исправление, на 28.10.2026 отсутствует. Поэтому исправление в `master` не изменяет решение по уже выпущенному ZIP `v1.0.6`.

Кроме UAF, issue #335 описывает дополнительные риски по resource exhaustion и обработке packet-derived данных. Для будущего кандидата они должны быть либо устранены upstream, либо явно исключены/ограничены нашей runtime-моделью и повторно проверены.

Запрещено просто собирать случайный `master` и считать его заменой release admission: такой путь переносит на Net.Pilot ответственность за toolchain, зависимости, provenance и воспроизводимость сборки.

## Следующий native-кандидат

Новый engine-specific adapter нельзя подключать, пока нет component set со статусом `ADMIT`.

Следующий кандидат должен начинать admission заново с нового точного fixed-тега:

1. точный tag/commit и архив;
2. SHA-256 архива;
3. полный EXE/DLL/SYS inventory;
4. SHA-256 каждого runtime-компонента;
5. подписи/издатели;
6. текущий upstream issue/advisory/CVE/NVD/OSV/CISA KEV review;
7. подтверждение наличия UAF-fix;
8. повторная проверка Cygwin и WinDivert;
9. проверка cleanup/rollback;
10. только после этого — решение `ADMIT` или `BLOCK`.

Доказательства от предыдущего релиза не наследуются автоматически.

## Следующие этапы R16

1. Дождаться нового fixed native-кандидата и провести admission с нуля.
2. После `ADMIT` реализовать **R16.5 — engine-specific plan boundary**:
   - отдельный внутренний adapter;
   - только фиксированные executable identities;
   - только allowlisted typed параметры;
   - никаких raw args;
   - неизвестный/неподдерживаемый профиль → fail-closed.
3. После появления admitted runtime завершить **R16.6 — пользовательский профильный UI** без раскрытия пользователю `winws`, WinDivert, Lua/BAT как обязательных сущностей.

## Security invariants

R16 поддерживает:

- `NETP-INV-002` — IPC не принимает произвольные команды;
- `NETP-INV-003` — исполняемый компонент выбирается только из admitted inventory;
- `NETP-INV-005` — конфигурация остаётся данными, а не языком исполнения;
- `NETP-INV-006` — фиксированный executable + валидированный массив аргументов;
- `NETP-INV-013` — смена движка требует нового admission;
- `NETP-INV-016` — неизвестное или неподтверждённое состояние завершается безопасным отказом.

## Важная граница

Текущее состояние намеренно функционально ограничено: Net.Pilot может продолжать проверять публичную доступность сервисов и показывать состояние, но не должен устанавливать или запускать native engine, пока не появится component set со статусом `ADMIT`.
