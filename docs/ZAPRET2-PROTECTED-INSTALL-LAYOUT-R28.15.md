# R28.15 — protected package layout and Windows install-location boundary

Дата: 29.10.2026.

Статус: **DESIGN READY / TRUSTED PACKAGE LAYOUT CATALOG EMPTY / NO FILE WRITES / NO ARGUMENTS / NO NATIVE RUNTIME / RELEASE GATE BLOCK**.

## Цель

R28.14 связал trusted launch metadata с immutable executable identity, но намеренно не определял, где package находится на диске.

R28.15 добавляет следующий отдельный слой:

1. typed trusted package layout — logical component id → фиксированный относительный path;
2. Windows protected install-location resolver — deterministic absolute paths только под системным managed root NetPilot.

Этот слой ничего не скачивает, не создаёт и не запускает.

## Package-layout contract

Файл:

`src/NetPilot.Engine.Abstractions/EnginePackageLayoutContracts.cs`

Добавлены:

- `EnginePackageComponentLayout`;
- `EnginePackageLayoutDeclaration`;
- `IEnginePackageLayoutProvider`;
- `BuiltInEnginePackageLayoutCatalog`.

`EnginePackageLayoutDeclaration` обязан описывать **ровно тот же набор logical component IDs**, который присутствует в `EngineExecutableIdentityDeclaration`:

- нельзя пропустить component;
- нельзя добавить неизвестный component;
- duplicate IDs запрещены;
- input list копируется в read-only snapshot.

Public constructors отсутствуют.

## Relative-path policy

Relative package path:

- не может быть absolute/rooted;
- не может начинаться/заканчиваться `/`;
- не содержит `\\` или `:`;
- не содержит `//`;
- не содержит control characters;
- не содержит `.` или `..` как сегмент;
- ограничен 16 сегментами, 64 символами на сегмент и 256 символами суммарно;
- допускает только ASCII letters/digits и `- _ .` внутри сегмента;
- trailing-dot сегменты запрещены;
- Windows reserved device aliases (`CON`, `PRN`, `AUX`, `NUL`, `COM1..9`, `LPT1..9`, включая варианты с extension) запрещены.

Catalog в R28.15 намеренно пуст: exact package layout будущего `NetPilot-zapret2` ещё не существует и не может быть угадан из upstream package.

## Дополнительное hardening R28.13 identity

При подготовке install-location выявлено, что version token metadata мог быть `.` / `..` или завершаться точкой. До R28.15 это не было executable path и не давало runtime effect, но для будущего directory segment такая форма неоднозначна.

`EngineExecutableIdentityDeclaration` дополнительно отклоняет:

- `.`;
- `..`;
- version token с trailing dot;
- path separators по прежней allowlist-политике.

Windows resolver отдельно отклоняет Windows device-name aliases в version/engine path segment.

## Windows protected install root

Файл:

`src/NetPilot.Platform.Windows/WindowsProtectedEngineInstallLocation.cs`

Public resolver имеет только сигнатуру:

`Resolve(EngineLaunchIdentityBinding, EnginePackageLayoutDeclaration)`

Он **не принимает string base path**, filename, working directory, URL или user/config path.

Absolute root выводится только из системного `Environment.SpecialFolder.ProgramFiles` и фиксированной структуры:

`Program Files/NetPilot/Engines/Managed/<EngineId>/<Version>/<Architecture>`

Это намеренно отдельный namespace от legacy:

`Program Files/NetPilot/Engines/Zapret/current`

Следовательно будущий `NetPilot-zapret2` не наследует legacy Flowseal/zapret1 identity/location автоматически.

Resolver дополнительно требует:

- binding platform = Windows;
- package layout ссылается **на тот же exact immutable identity object**, что binding;
- architecture является typed `x64` или `arm64`;
- engine/version path segments проходят fixed validation;
- каждый component path после `Path.GetFullPath` остаётся строго внутри exact engine root.

## Что resolver НЕ делает

`WindowsProtectedEngineInstallLocationResolver`:

- не создаёт directories;
- не создаёт/копирует files;
- не скачивает package;
- не меняет ACL;
- не проверяет hash содержимого файла;
- не вызывает `Process.Start`;
- не создаёт ArgumentList;
- не устанавливает/загружает driver;
- не включает WinDivert;
- не меняет network/firewall state;
- не выдаёт runtime admission.

## Reparse/symlink boundary

R28.15 структурно исключает traversal через metadata (`..`, absolute path, separator aliases), но **не объявляет существующий filesystem доверенным**.

Reparse point / symlink / hardlink / TOCTOU проверка должна выполняться в следующем on-disk verification gate непосредственно перед любым использованием файлов. R28.15 не делает ложного заявления, что чистая строковая нормализация может доказать безопасность уже существующей файловой системы.

## Тесты

Core tests проверяют:

- trusted package-layout catalog пуст;
- public constructors отсутствуют;
- provider публикует только package layout;
- traversal/rooted/backslash/colon/double-separator отклоняются;
- Windows reserved device names и trailing dot отклоняются;
- layout обязан точно совпасть с component set immutable identity;
- layout является snapshot.

Windows tests проверяют:

- public resolver не принимает string path;
- root всегда находится под fixed `Program Files/NetPilot/Engines/Managed`;
- component path остаётся внутри exact version/architecture root;
- resolver не создаёт directory;
- layout от другой immutable identity отклоняется;
- non-Windows binding отклоняется;
- Windows device-name version segment отклоняется.

## Runtime/admission остаются неизменными

R28.15 не меняет:

- `NoAdmittedEngineProfileRuntime`;
- `DenyAllEngineRuntimeAdmissionPolicy`;
- server-side no-admit `OperationPolicy`;
- `NoAdmittedZapretProcessLauncher`;
- updater transport admission;
- lab policy.

Trusted catalogs всё ещё не содержат production package/budget/identity values.

## Следующий безопасный этап

После зелёного CI: **R28.16 — on-disk immutable component verification design**.

Он должен проверять перед любым будущим launch:

- exact trusted root/layout;
- отсутствие unsafe reparse/symlink traversal в существующей path chain;
- тип файла;
- exact component SHA-256;
- exact package/layout identity;
- fail-closed при missing/extra/substituted component;
- никаких execution rights и никаких ArgumentList.

Только после этого отдельным gate может появиться typed allowlisted argument compiler; native execution остаётся последующим isolated LAB_APPROVED этапом.

Admission остаётся `REVIEW / BLOCK`, `may_execute:false`, `may_integrate:false`, `lab_execution_allowed:false`.
