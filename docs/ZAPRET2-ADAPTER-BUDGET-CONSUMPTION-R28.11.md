# R28.11 — typed adapter resource-budget consumption boundary

Дата: 29.10.2026.

Статус: **COMPOSITE TRUSTED PREREQUISITE DESIGN ONLY / CATALOG EMPTY / NO NATIVE RUNTIME / RELEASE GATE BLOCK**.

## Цель

R28.10 ввёл отдельную typed/trusted resource-budget declaration, но будущий adapter не должен самостоятельно склеивать независимые capability-флаги и numeric budgets.

R28.11 создаёт составную prerequisite boundary: stateful adapter должен получать единый доверенный объект только после того, как для одной и той же пары backend/platform одновременно существуют:

1. trusted backend capability declaration;
2. trusted positive resource-budget declaration.

Runtime admission при этом остаётся отдельной третьей границей и не входит в prerequisite object.

## Новый контракт

Файл:

`src/NetPilot.Engine.Abstractions/EngineRuntimePrerequisiteContracts.cs`

Добавлены:

- `EngineRuntimePrerequisiteDeclaration`;
- `IEngineRuntimePrerequisiteProvider`;
- `BuiltInEngineRuntimePrerequisiteCatalog`.

Составная declaration содержит только:

- canonical `EngineId`;
- backend kind;
- host platform;
- trusted `EngineBackendCapabilityDeclaration`;
- trusted `EngineResourceBudgetDeclaration`.

В ней нет:

- executable/path;
- process start info;
- raw arguments/command line;
- URL;
- Lua/config/script text;
- service/driver controls;
- admission grant.

## Fail-closed composition

`BuiltInEngineRuntimePrerequisiteCatalog.TryGet(...)` возвращает declaration только если оба trusted каталога имеют запись для одной backend/platform пары.

Если capability существует, а resource budget отсутствует, результат — `false`.

Если полного prerequisite set нет, `Get(...)` бросает `InvalidOperationException`.

В R28.11 `BuiltInEngineResourceBudgetCatalog` по-прежнему пуст, поэтому:

- prerequisite catalog также пуст;
- zapret2/Windows не получает prerequisite;
- zapret2/Linux не получает prerequisite;
- ByeDPI/Windows не получает prerequisite;
- ни один будущий stateful adapter не получает готовую trusted metadata-комбинацию.

## Identity alignment

Internal constructor `EngineRuntimePrerequisiteDeclaration` проверяет, что capability и resource-budget declarations имеют одинаковые:

- backend;
- platform.

Mismatched Windows/Linux или разные backend отклоняются.

Конструктор не public. Внешний runtime не должен собирать составной объект из произвольных значений.

## Runtime admission остаётся отдельно

Даже после будущего появления полного prerequisite set он не означает `ADMIT`.

Production activation по-прежнему требует отдельный `IEngineRuntimeAdmissionPolicy`.

Сегодня production composition остаётся:

- `NoAdmittedEngineProfileRuntime`;
- `DenyAllEngineRuntimeAdmissionPolicy.Instance`;
- server-side `OperationPolicy.CreateNoAdmittedEnginePolicy()`;
- `NoAdmittedZapretProcessLauncher`;
- no-admitted release transport.

Поэтому capability + budget metadata не могут сами открыть native runtime.

## Тесты

`tests/NetPilot.Core.Tests/EngineRuntimePrerequisiteContractTests.cs` проверяет:

- prerequisite catalog пуст без promoted budgets;
- `TryGet` fail-closed для известных backend/platform пар;
- `Get` отклоняет incomplete set;
- public constructor отсутствует;
- provider возвращает один composite trusted object, а не weak независимые поля;
- declaration не содержит execution/process/driver/path/command/URL surfaces;
- internal constructor отклоняет backend/platform mismatch.

Для mismatch-теста используются локальные test-only положительные значения через reflection; они не являются lab или production defaults и не попадают в trusted catalog.

## Что R28.11 не делает

R28.11 не:

- выбирает numeric budgets;
- добавляет записи в trusted budget catalog;
- создаёт zapret2 executable adapter;
- строит process arguments;
- запускает candidate engine;
- включает WinDivert;
- включает NFQUEUE/firewall;
- меняет `OperationPolicy`;
- меняет runtime admission;
- разрешает EngineInstall/EngineStart.

## Следующий шаг

После зелёного CI безопасный следующий этап — R28.12: определить **non-executable typed launch-plan metadata boundary** для admitted-in-future adapter, где prerequisite declaration является обязательным входом, но executable identity/ArgumentList остаются отдельным последующим gate и production composition остаётся deny-all.

Admission остаётся:

- `REVIEW`;
- `BLOCK`;
- `may_execute: false`;
- `may_integrate: false`;
- `lab_execution_allowed: false`.
