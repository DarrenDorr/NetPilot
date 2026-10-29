# R28.18 — typed allowlisted argument plan без execution

Дата: 29.10.2026.

Статус: **NON-EXECUTING ARGUMENT CONTRACT / PRODUCTION CATALOG EMPTY / NO RAW CLI / NO PROCESS START / RELEASE GATE BLOCK**.

## Цель

R28.17 создал живой pre-launch handoff после immutable package + ACL/owner verification. Следующая граница — не допустить возврата к произвольной command line при подготовке будущего native launcher.

R28.18 вводит typed argv contract, но не запускает процесс.

## Engine-neutral typed token model

Файл:

`src/NetPilot.Engine.Abstractions/EngineArgumentPlanContracts.cs`

Поддерживаются только следующие источники значения:

- `Flag` — канонический флаг без значения;
- `FixedPositiveUInt` — internal положительное целое;
- `ConntrackMaxEntries` — значение только из trusted `EngineResourceBudgetDeclaration`;
- `DelayedQueueMaxBytes` — значение только из trusted resource budget;
- `VerifiedComponentPath` — путь только по logical component id exact immutable identity.

Generic string-value source намеренно отсутствует.

Поэтому через этот контракт нельзя представить пользовательские:

- raw CLI;
- Lua/script/code;
- произвольный path/filename;
- URL;
- environment value;
- shell command.

`OptionName` обязан быть каноническим `--lower-ascii-token` без `=`, `/`, `\\`, `:`, whitespace или shell punctuation.

Любые будущие строковые режимы конкретного backend должны получить отдельные typed enum/value contracts; их нельзя добавлять как generic string.

## Trusted declaration

`EngineArgumentPlanDeclaration`:

- не имеет public constructor;
- связан с exact `EngineLaunchIdentityBinding`;
- содержит от 1 до 128 typed tokens;
- `VerifiedComponentPath` обязан ссылаться на component id внутри exact immutable identity;
- список immutable/read-only.

`BuiltInEngineArgumentPlanCatalog` в R28.18 **намеренно пуст**.

До появления exact NetPilot-owned fork artifact ни одного production argv plan не существует.

## Windows compiler

Файл:

`src/NetPilot.Platform.Windows/WindowsTypedEngineArgumentPlan.cs`

Public compiler принимает только:

- `WindowsVerifiedEnginePreLaunchHandoff`;
- `EngineArgumentPlanDeclaration`.

Он не принимает string path, raw argument list, URL, environment или executable.

Перед и после compilation вызывается `preLaunchHandoff.EnsureReady()`.

Compiler требует совпадение:

- backend;
- platform;
- architecture;
- profile;
- exact immutable executable identity reference.

Dynamic values разрешаются только из already trusted state:

- resource budget из launch binding;
- exact component path из `WindowsVerifiedEnginePackage`.

Результат `WindowsCompiledEngineArgumentPlan`:

- не имеет public constructor;
- содержит read-only argv strings;
- остаётся связан с живым pre-launch handoff;
- после Dispose verified package `EnsureReady()` завершается fail-closed.

Размер ограничен:

- один resolved token: максимум 16 KiB;
- общий argv payload: максимум 64 KiB.

NUL/CR/LF запрещены.

## Тесты

### Core

`tests/NetPilot.Core.Tests/EngineArgumentPlanContractsTests.cs`

Проверяется:

- production catalog пуст;
- trusted types нельзя создать public constructor;
- option name с raw value/path/URL/whitespace/shell syntax отклоняется;
- fixed numeric zero отклоняется;
- component id вне immutable identity отклоняется;
- valid declaration является read-only.

### Windows

`tests/NetPilot.Platform.Windows.Tests/WindowsTypedEngineArgumentPlanTests.cs`

Проверяется:

- public compiler не принимает string/raw CLI input;
- synthetic typed sources компилируются в отдельные argv tokens;
- budget values берутся из trusted declaration;
- component path берётся только из verified package;
- compiled list read-only;
- после Dispose package compiled plan становится unusable;
- plan другой immutable identity binding отклоняется.

Synthetic test options не являются production zapret2 configuration и не добавляются в trusted catalog.

## Что R28.18 НЕ делает

R28.18 не:

- добавляет production argument plans;
- переносит старые BAT/Lua/strategy files в новую цепочку;
- принимает raw CLI пользователя;
- создаёт `ProcessStartInfo`;
- вызывает `Process.Start`;
- выбирает executable по path string пользователя;
- запускает zapret2/ByeDPI/DPIBreak;
- устанавливает/загружает WinDivert;
- меняет firewall/network state;
- выдаёт `ADMIT` / `LAB_APPROVED`;
- открывает `EngineStart`/`EngineInstall`.

Legacy `ZapretLaunchPlanBuilder` не становится доверенной production boundary автоматически. Его eventual retirement/replacement является отдельным migration gate.

## Следующий безопасный этап

После зелёного CI: **R28.19 — typed executable selection + non-starting ProcessStartInfo factory**.

Условия:

1. executable может быть только exact `PrimaryExecutable` из живого verified package;
2. arguments только из `WindowsCompiledEngineArgumentPlan`;
3. `UseShellExecute=false`;
4. `ArgumentList` вместо raw command line;
5. fixed working directory из verified package;
6. environment allowlist/clear policy;
7. factory создаёт metadata/`ProcessStartInfo`, но **не вызывает `Process.Start`**;
8. production runtime admission остаётся deny-all.

Native execution разрешать рано: WinDivert и isolated lab gates по-прежнему BLOCK.
