# R28.14 — non-executable launch identity binding

Дата: 29.10.2026.

Статус: **DESIGN READY / BINDING CATALOG EMPTY / NO PATH / NO ARGUMENTS / NO NATIVE RUNTIME / RELEASE GATE BLOCK**.

## Цель

R28.12 описывает доверенную неисполняемую launch metadata, а R28.13 — immutable identity exact native artifact set. Эти две декларации нельзя связывать вручную в будущем adapter.

R28.14 вводит отдельный typed binding, который создаётся только если совпадают:

- backend;
- host platform;
- ожидаемая host architecture.

Binding остаётся metadata и не умеет устанавливать или запускать native-компонент.

## Новый контракт

Файл:

`src/NetPilot.Engine.Abstractions/EngineLaunchIdentityBindingContracts.cs`

Добавлены:

- `EngineLaunchIdentityBinding`;
- `IEngineLaunchIdentityBindingProvider`;
- `BuiltInEngineLaunchIdentityBindingCatalog`.

Binding содержит только:

- `EngineLaunchPlanMetadata`;
- `EngineExecutableIdentityDeclaration`;
- typed architecture;
- derived EngineId/backend/platform/profile ID.

## Fail-closed alignment

Internal constructor отклоняет binding, если:

- backend launch metadata и executable identity различаются;
- platform различается;
- executable identity architecture не равна явно ожидаемой host architecture.

Architecture передаётся отдельно как typed expected target, чтобы identity не могла сама объявить себя подходящей для любого host.

## Trusted catalog

`BuiltInEngineLaunchIdentityBindingCatalog` строит binding только из:

1. `BuiltInEngineLaunchPlanMetadataCatalog`;
2. `BuiltInEngineExecutableIdentityCatalog`.

Оба текущих upstream-пути не дают положительного production binding:

- launch metadata catalog пуст, потому что resource-budget/prerequisite catalogs пусты;
- executable identity catalog пуст, потому что NetPilot-owned exact production fork artifact ещё не создан и не admitted.

Следовательно R28.14 binding catalog также пуст.

## Отсутствующие execution surfaces

Binding не содержит:

- filesystem path или filename;
- working directory;
- command/CLI/ArgumentList;
- environment variables;
- URL;
- Lua/config/script text;
- `ProcessStartInfo`;
- service control;
- driver load/unload;
- listener/socket/filter configuration;
- runtime admission grant.

Наличие binding в будущем всё равно не даст права `EngineInstall` или `EngineStart`.

## Тесты

`tests/NetPilot.Core.Tests/EngineLaunchIdentityBindingContractTests.cs` проверяет:

- trusted binding catalog пуст;
- `TryGet` fail-closed;
- `Get` отклоняет отсутствие binding;
- public constructor отсутствует;
- provider публикует только binding object;
- публичная поверхность не содержит path/arguments/process/service/admission controls;
- согласованные backend/platform/architecture принимаются только в test-only reflection object graph;
- backend/platform mismatch отклоняется;
- architecture mismatch отклоняется.

Test-only budget/hash values не попадают в production trusted catalogs и не являются рекомендуемыми defaults.

## Runtime остаётся закрыт

R28.14 не меняет:

- `NoAdmittedEngineProfileRuntime`;
- `DenyAllEngineRuntimeAdmissionPolicy`;
- server-side `OperationPolicy.CreateNoAdmittedEnginePolicy()`;
- `NoAdmittedZapretProcessLauncher`;
- updater transport admission;
- native lab policy.

## Следующий безопасный этап

После зелёного CI следующий слой — R28.15: **protected install-location + package-layout resolution design** для будущего NetPilot-owned fork.

Он должен:

- начинать с trusted `EngineLaunchIdentityBinding`;
- разрешать только фиксированный защищённый root NetPilot;
- не принимать path от пользователя/config/network;
- отделить logical component IDs от реальных фиксированных относительных путей;
- запрещать traversal/reparse/symlink escape;
- не запускать процесс;
- не добавлять arguments.

После него отдельными gates остаются on-disk hash/layout verification, typed allowlisted arguments и только затем approved isolated lab execution.

Admission остаётся `REVIEW / BLOCK`, `may_execute:false`, `may_integrate:false`, `lab_execution_allowed:false`.
