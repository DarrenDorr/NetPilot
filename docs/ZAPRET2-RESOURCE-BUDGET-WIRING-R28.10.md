# R28.10 — typed/trusted resource-budget wiring design

Дата: 29.10.2026.

Статус: **DESIGN + TYPED CONTRACTS ONLY / TRUSTED CATALOG EMPTY / NO NATIVE RUNTIME / RELEASE GATE BLOCK**.

## Цель

После R28.9 current review patchset zapret2 `0001–0006` имеет fail-closed семантику для двух критичных unconfigured resource budgets:

- conntrack entry budget;
- delayed packet queue byte budget.

Ноль в обоих случаях должен оставаться состоянием «не настроено / fail-closed», а не означать unlimited state.

R28.10 создаёт только безопасный типизированный путь для будущих положительных значений. Никакие production или lab числа на этом этапе не выбираются.

## Новый контракт

Файл:

`src/NetPilot.Engine.Abstractions/EngineResourceBudgetContracts.cs`

Добавлены:

- `EngineResourceBudgetDeclaration`;
- `IEngineResourceBudgetProvider`;
- `BuiltInEngineResourceBudgetCatalog`.

`EngineResourceBudgetDeclaration` содержит только:

- backend kind;
- host platform;
- положительный `ConntrackMaxEntries`;
- положительный `DelayedQueueMaxBytes`.

В declaration намеренно отсутствуют:

- executable/path;
- command/arguments/raw CLI;
- URL;
- Lua/script/config text;
- admission state;
- process/service/driver controls.

## Почему конструктор internal

Resource budget нельзя принимать как произвольную пользовательскую настройку или вручную созданный runtime-флаг.

Конструктор declaration не public. Внешний runtime должен получать объект из доверенного встроенного каталога после отдельного review/promotion.

Это не является абсолютной sandbox-границей внутри самого assembly, но существенно уменьшает accidental misuse и делает будущую promotion отдельным code-review изменением.

## Почему каталог пуст

`BuiltInEngineResourceBudgetCatalog` в R28.10 содержит **0 declarations**.

Причина: у проекта ещё нет утверждённых workload evidence, позволяющих честно выбрать:

- положительный conntrack limit;
- положительный delayed-queue byte limit.

Поэтому:

- `TryGet(...)` возвращает `false`;
- `Get(...)` fail-closed бросает `InvalidOperationException`;
- никаких defaults/fallbacks нет;
- «разумные на глаз» значения не вводятся.

## Нулевая семантика

Trusted declaration не позволяет представить:

- `ConntrackMaxEntries == 0`;
- `DelayedQueueMaxBytes == 0`.

Ноль остаётся только native unconfigured/fail-closed состоянием patchset `0001–0006` и не может быть принят как доверенный runtime budget.

## Что тестируется

`tests/NetPilot.Core.Tests/EngineResourceBudgetContractTests.cs` проверяет:

- trusted catalog пуст;
- известные backend/platform пары не получают budget автоматически;
- `Get(...)` fail-closed без promoted evidence;
- у declaration отсутствует public constructor;
- публичная metadata не содержит string/path/argument/command/URL surfaces;
- provider возвращает только trusted declaration, а не отдельные weak numeric fields.

## Security boundary

R28.10 **не меняет** текущую runtime composition:

- `NoAdmittedEngineProfileRuntime` остаётся production runtime;
- `DenyAllEngineRuntimeAdmissionPolicy` остаётся production admission policy;
- `OperationPolicy` не разрешает `EngineInstall`/`EngineStart`;
- `NoAdmittedZapretProcessLauncher` остаётся fail-closed;
- WinDivert driver load запрещён;
- Linux NFQUEUE runtime не запускается.

Capability declaration, resource-budget declaration и runtime admission являются тремя разными слоями. Наличие первых двух в будущем не должно автоматически означать третий.

## Что требуется перед добавлением первой declaration

До первой записи в `BuiltInEngineResourceBudgetCatalog` необходимо:

1. утвердить отдельный isolated workload-lab plan;
2. определить безопасный способ измерения state/queue peaks без внешнего неавторизованного трафика;
3. получить evidence для browser/YouTube/Discord/combined/burst сценариев;
4. выбрать значения с документированным запасом и верхними эксплуатационными границами;
5. повторить resource stress/regression с выбранными значениями;
6. связать declaration с exact admitted component identity;
7. пройти отдельный HIGH_ASSURANCE human review.

## Admission effect

R28.10 не меняет admission:

- `admission_status: REVIEW`;
- `release_gate: BLOCK`;
- `may_execute: false`;
- `may_integrate: false`;
- `lab_execution_allowed: false`.

Следующий шаг после зелёного CI R28.10 — проверить, где именно будущий zapret2 adapter должен потреблять trusted budget declaration, не создавая executable/native path до отдельного ADMIT.
