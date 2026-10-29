# R28.13 — immutable executable identity boundary

Дата: 29.10.2026.

Статус: **DESIGN READY / TRUSTED CATALOG EMPTY / NO PATH / NO ARGUMENTS / NO NATIVE RUNTIME / RELEASE GATE BLOCK**.

## Цель

R28.12 ввёл неисполняемую launch-plan metadata: trusted runtime prerequisites + встроенный профиль. Следующий слой должен описать exact native artifact, но не место его установки и не способ запуска.

R28.13 вводит отдельную immutable executable identity boundary. Она отвечает только на вопрос: **какой exact package/component set может рассматриваться следующими security gates**.

## Новый контракт

Файл:

`src/NetPilot.Engine.Abstractions/EngineExecutableIdentityContracts.cs`

Добавлены:

- `EngineBinaryArchitecture`;
- `EngineExecutableComponentKind`;
- `EngineExecutableComponentIdentity`;
- `EngineExecutableIdentityDeclaration`;
- `IEngineExecutableIdentityProvider`;
- `BuiltInEngineExecutableIdentityCatalog`.

## Что содержит identity

`EngineExecutableIdentityDeclaration` содержит только:

- typed backend kind;
- typed host platform;
- typed architecture;
- canonical version token;
- package SHA-256;
- layout SHA-256;
- provenance SHA-256;
- read-only список логических component identities.

Каждый component identity содержит только:

- canonical logical `ComponentId`;
- typed component kind;
- SHA-256.

`ComponentId` не является filename или filesystem path.

## Что намеренно отсутствует

R28.13 identity не содержит:

- executable path;
- filename;
- working directory;
- `ProcessStartInfo`;
- command line;
- `ArgumentList`;
- environment variables;
- URL;
- Lua/config/script text;
- service control;
- driver load/unload control;
- socket/listener/filter controls;
- runtime admission grant.

Поэтому даже валидная identity сама по себе недостаточна для запуска процесса.

## Immutable/fail-closed правила

- SHA-256 принимается только как 64 lowercase hex symbols;
- component id допускает только lowercase ASCII letters/digits и одиночные дефисы;
- component id не может содержать `.`, `/`, `\\`, `:`, whitespace или другие path-like symbols;
- component list ограничен 1..16 элементами;
- component ids уникальны;
- identity обязана иметь ровно один `PrimaryExecutable`;
- входной список копируется в read-only snapshot;
- constructors identity/component не public.

## Trusted catalog

`BuiltInEngineExecutableIdentityCatalog` в R28.13 намеренно пуст.

Причина: собственные production artifacts `NetPilot-zapret2` и `NetPilot-ByeDPI` ещё не созданы и не прошли exact binary admission.

Следовательно:

- `All` пуст;
- `TryGet(...)` возвращает `false`;
- `Get(...)` отклоняет запрос;
- никакой upstream binary автоматически не получает trusted identity;
- compile-smoke artifacts не становятся production identity.

## Важная legacy-граница

Существующий updater/installer path относится к более ранней legacy Zapret архитектуре:

- `EngineInstallationManifest.CreateZapret(...)` привязан к `TrustedUpstreams.FlowsealZapret`;
- Windows location использует legacy root `Engines/Zapret/current`;
- legacy launch builder ожидает `winws.exe` и template arguments.

Эти структуры **не должны автоматически использоваться** для будущего `NetPilot-zapret2` fork.

Будущий fork должен получить собственные:

- canonical backend/package identity;
- package layout admission;
- protected install-location resolution;
- exact component hash verification.

## Тесты

`tests/NetPilot.Core.Tests/EngineExecutableIdentityContractTests.cs` проверяет:

- trusted catalog пуст до собственного verified build;
- `TryGet` fail-closed;
- `Get` отклоняет отсутствие identity;
- public constructors отсутствуют;
- provider публикует только trusted identity object;
- public properties не содержат path/filename/argument/command/URL/process/service/environment/script surfaces;
- malformed/noncanonical SHA-256 отклоняется;
- path-like component ids отклоняются;
- требуется ровно один primary executable;
- duplicate component ids отклоняются;
- component collection сохраняется как snapshot и не зависит от последующей мутации исходного списка.

Используемые в unit tests фиктивные SHA-256 не являются реальными artifact identities и не попадают в trusted catalog.

## Runtime admission остаётся отдельно

R28.13 не меняет:

- `NoAdmittedEngineProfileRuntime`;
- `DenyAllEngineRuntimeAdmissionPolicy`;
- server-side no-admit `OperationPolicy`;
- `NoAdmittedZapretProcessLauncher`;
- updater transport admission;
- lab policy.

`EngineInstall` и `EngineStart` остаются закрыты.

## Что R28.13 не делает

R28.13 не:

- создаёт fork repository;
- выбирает production binary;
- записывает real binary hashes в trusted catalog;
- определяет filesystem path;
- устанавливает package;
- проверяет файл на диске;
- создаёт arguments;
- вызывает `Process.Start`;
- включает WinDivert/NFQUEUE/firewall;
- меняет runtime admission.

## Следующий безопасный этап

После зелёного CI: R28.14 — **non-executable launch identity binding**.

Он должен связать `EngineLaunchPlanMetadata` и `EngineExecutableIdentityDeclaration` только при совпадении backend/platform и требуемой архитектуры, оставаясь без filesystem path и arguments.

Далее отдельными gates должны идти:

1. protected install-location/package-layout resolution;
2. on-disk exact hash/layout verification;
3. typed allowlisted arguments;
4. только затем isolated lab execution после отдельного `LAB_APPROVED`.

Admission после R28.13 остаётся:

- `REVIEW`;
- `BLOCK`;
- `may_execute: false`;
- `may_integrate: false`;
- `lab_execution_allowed: false`.
