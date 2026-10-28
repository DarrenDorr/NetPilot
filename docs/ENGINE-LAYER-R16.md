# Engine Layer R16 — независимый слой движков Net.Pilot

Дата: 28.10.2026
Статус: R16.1–R16.4 реализованы; production runtime нового движка ещё не подключён.

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

`BuiltInEngineProfilePlan` содержит только:

- `ProfileId`;
- `Kind`.

В нём нет executable, пути, arguments, command, Lua, script или URL. Его constructor не является публичным: внешний код не может самостоятельно сконструировать произвольный план.

Это ещё не process launch plan. Конкретный движок в будущем обязан отдельно преобразовать этот типизированный plan в собственный внутренний план запуска и пройти security review.

## Текущее состояние движков

### Flowseal / zapret1

Flowseal `1.10.4` использует zapret1 `v72.9`.

Native admission: `BLOCK`.

### zapret2

`bol-van/zapret2 v1.0.6` рассматривается только как кандидат внутреннего движка.

Native admission: `REVIEW`, release gate: `BLOCK` до завершения проверки.

Zapret2 не является пользовательской функцией Net.Pilot. Его Lua-возможности не должны становиться пользовательской/config execution surface.

## Следующие этапы

1. **R16.5 — engine-specific plan boundary**
   - отдельный внутренний adapter для конкретного admitted engine;
   - только фиксированные executable identities;
   - только allowlisted typed параметры;
   - никаких raw args;
   - неизвестный/неподдерживаемый профиль → fail-closed.

2. **R16.6 — GUI**
   - пользователь видит понятный профиль и статус;
   - детали `winws`, WinDivert, Lua/BAT скрыты как внутреннее устройство.

3. До подключения runtime завершить native admission выбранного engine component set.

## Security invariants

R16 поддерживает:

- `NETP-INV-002` — IPC не принимает произвольные команды;
- `NETP-INV-003` — исполняемый компонент выбирается только из admitted inventory;
- `NETP-INV-005` — конфигурация остаётся данными, а не языком исполнения;
- `NETP-INV-006` — фиксированный executable + валидированный массив аргументов;
- `NETP-INV-013` — смена движка требует нового admission;
- `NETP-INV-016` — неизвестное состояние завершается безопасным отказом.

## Важная граница

R16.1–R16.4 не разрешают установку или запуск zapret2 и не снимают BLOCK с zapret1.

До завершения admission runtime нового движка остаётся fail-closed.
