# R28.12 — non-executable typed launch-plan metadata boundary

Дата: 29.10.2026.

Статус: **NON-EXECUTABLE METADATA DESIGN ONLY / CATALOG EMPTY / NO NATIVE RUNTIME / RELEASE GATE BLOCK**.

## Цель

R28.11 объединил trusted backend capabilities и trusted positive resource budgets в единый runtime prerequisite object. Следующий слой не должен сразу переходить к executable/path/ArgumentList.

R28.12 вводит промежуточную **неисполняемую launch-plan metadata**, которая связывает:

1. полный trusted runtime prerequisite set;
2. один встроенный `EngineProfileId`, действительно поддерживаемый backend capabilities.

Metadata не умеет запускать процесс и не содержит информации, достаточной для запуска native-компонента.

## Новый контракт

Файл:

`src/NetPilot.Engine.Abstractions/EngineLaunchPlanMetadataContracts.cs`

Добавлены:

- `EngineLaunchPlanMetadata`;
- `IEngineLaunchPlanMetadataProvider`;
- `BuiltInEngineLaunchPlanMetadataCatalog`.

`EngineLaunchPlanMetadata` содержит только:

- `EngineRuntimePrerequisiteDeclaration`;
- `EngineProfileId`;
- derived typed backend kind;
- derived typed host platform.

В объекте намеренно отсутствуют:

- executable identity;
- filesystem path;
- working directory;
- command line;
- `ArgumentList`;
- environment variables;
- URL;
- Lua/config/script text;
- process/service controls;
- driver controls;
- socket/network controls;
- runtime admission grant.

## Mandatory trusted prerequisite

Metadata нельзя построить из отдельных weak flags или numeric budgets.

Internal constructor требует готовый `EngineRuntimePrerequisiteDeclaration`. Следовательно, before-admit chain выглядит так:

`trusted capability + trusted positive budget -> trusted prerequisite -> profile-compatible non-executable launch metadata`

И только после этого в будущих отдельных gates могут рассматриваться immutable executable identity и allowlisted arguments.

## Profile capability enforcement

Constructor и catalog проверяют встроенную capability policy.

Если backend не покрывает требования профиля, metadata не создаётся.

Например, ByeDPI/Windows имеет только explicit proxy TCP+UDP declaration и поэтому не может получить metadata для полнофункционального `youtube` или `discord` profile, которым требуется transparent capability.

## Fail-closed catalog

`BuiltInEngineLaunchPlanMetadataCatalog` строится только поверх `BuiltInEngineRuntimePrerequisiteCatalog`.

Поскольку R28.10/R28.11 trusted resource-budget/prerequisite catalogs пока пусты, в R28.12:

- launch metadata catalog также пуст;
- `TryGet(...)` возвращает `false`;
- `Get(...)` бросает `InvalidOperationException`;
- ни один backend/profile не получает launch metadata автоматически.

## Тесты

`tests/NetPilot.Core.Tests/EngineLaunchPlanMetadataContractTests.cs` проверяет:

- catalog пуст без полного prerequisite set;
- `TryGet` fail-closed;
- `Get` отклоняет отсутствие metadata;
- public constructor отсутствует;
- provider возвращает только non-executable metadata;
- публичные свойства не содержат string/path/executable/argument/command/URL/environment/process/service/driver/socket surfaces;
- internal constructor отклоняет profile, не покрываемый trusted capabilities.

Для последнего test-only сценария через reflection создаётся временный положительный budget `1/1` только внутри unit test. Эти значения не записываются в trusted catalog, не являются lab/production defaults и не используются runtime-кодом.

## Runtime admission остаётся отдельной границей

R28.12 не меняет production composition:

- `NoAdmittedEngineProfileRuntime`;
- `DenyAllEngineRuntimeAdmissionPolicy.Instance`;
- `OperationPolicy.CreateNoAdmittedEnginePolicy()`;
- `NoAdmittedZapretProcessLauncher`;
- no-admitted updater transport.

Даже если в будущем появится launch metadata, она сама по себе не разрешает `EngineInstall` или `EngineStart`.

## Что R28.12 не делает

R28.12 не:

- выбирает resource budgets;
- добавляет trusted budget/prerequisite entries;
- определяет executable hash/path;
- строит реальные arguments;
- вызывает `Process.Start`;
- запускает native engine;
- устанавливает или загружает WinDivert;
- включает NFQUEUE/firewall;
- создаёт listener/socket;
- меняет admission policy.

## Следующий безопасный этап

После зелёного CI возможен R28.13: **immutable executable identity design**.

Он должен определить только доверенную immutable identity будущего binary/component set (version/hash/layout/provenance) без Process.Start и без user/config-defined path. Реальная allowlisted `ArgumentList` и executable runtime должны оставаться ещё более поздними отдельными gates.

Admission после R28.12 остаётся:

- `REVIEW`;
- `BLOCK`;
- `may_execute: false`;
- `may_integrate: false`;
- `lab_execution_allowed: false`.
