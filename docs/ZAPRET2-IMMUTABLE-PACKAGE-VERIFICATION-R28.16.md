# R28.16 — on-disk immutable component verification

Дата: 29.10.2026.

Статус: **DESIGN + NON-EXECUTING VERIFIER / TRUSTED PRODUCTION CATALOGS EMPTY / NO ARGUMENTS / NO PROCESS START / NO DRIVER LOAD / RELEASE GATE BLOCK**.

## Цель

R28.15 определил typed package layout и fixed Windows managed install root, но намеренно не доверял уже существующей файловой системе.

R28.16 добавляет отдельный on-disk verification gate непосредственно перед любым будущим consumer native package.

Verifier не выдаёт runtime admission и не запускает native component.

## Public boundary

Файл:

`src/NetPilot.Platform.Windows/WindowsImmutableEnginePackageVerifier.cs`

Public API:

`WindowsImmutableEnginePackageVerifier.Verify(EngineLaunchIdentityBinding, EnginePackageLayoutDeclaration)`

Public API не принимает:

- base path;
- filename;
- working directory;
- URL;
- command line;
- ArgumentList;
- environment;
- process/service/driver controls.

Absolute paths по-прежнему выводятся только через `WindowsProtectedEngineInstallLocationResolver`.

## Что проверяется

Для exact immutable identity + package layout verifier требует:

1. managed engine root существует;
2. resolved component set точно совпадает с immutable identity;
3. каждый resolved path точно соответствует trusted relative path;
4. component остаётся строго внутри exact engine root;
5. package tree не содержит extra files или extra directories;
6. package tree не содержит reparse point / junction / symbolic-link entries;
7. component существует и является обычным файлом;
8. Win32 final path открытого file handle совпадает с exact expected path;
9. `NumberOfLinks == 1`, поэтому hardlinked component отклоняется;
10. SHA-256 component точно совпадает с `EngineExecutableComponentIdentity.Sha256`;
11. file identity/size/last-write/link-count не меняются во время hashing;
12. SHA-256 вычисляется дважды на том же открытом handle;
13. exact package tree повторно проверяется после открытия всех component handles.

## TOCTOU hardening

Успешный результат представлен типом `WindowsVerifiedEnginePackage`.

Он удерживает открытые read-only handles с `FileShare.Read` до `Dispose()`.

Следствие: после успешной verification новые opens с write/delete access не могут заменить или удалить уже проверенный component, пока verification object жив.

Это уменьшает окно подмены между verification и будущим consumer.

Ограничения честно сохраняются:

- R28.16 не доказывает безопасность ACL/owner существующего managed root;
- уже существующий до verification writable handle является отдельной boundary;
- будущий launcher обязан принимать именно живой verified package и проверять, что verification locks не освобождены;
- filesystem ACL/owner и launch handoff остаются отдельными gates.

## Result types

Добавлены:

- `WindowsVerifiedEngineComponent`;
- `WindowsVerifiedEnginePackage`;
- `WindowsEnginePackageVerificationException`.

`WindowsVerifiedEngineComponent` и `WindowsVerifiedEnginePackage` не имеют public constructors.

`WindowsVerifiedEnginePackage` содержит только проверенную identity/layout/component metadata и удерживаемые verifier handles. Он не является execution token или admission grant.

## Fail-closed причины

Verifier имеет отдельные коды для основных отказов, включая:

- `PackageIdentityMismatch`;
- `EngineRootMissing`;
- `ResolvedComponentSetMismatch`;
- `ResolvedPathMismatch`;
- `ResolvedPathOutsideEngineRoot`;
- `UnexpectedPackageEntry`;
- `UnsafeReparsePoint`;
- `ComponentMissing`;
- `ComponentNotRegularFile`;
- `ComponentHardlinkRejected`;
- `ComponentFinalPathMismatch`;
- `ComponentHashMismatch`;
- `ComponentChangedDuringVerification`;
- `PackageAccessDenied`;
- `PackageIoError`.

При любой ошибке уже открытые handles закрываются, а verified package не создаётся.

## Тесты

Файл:

`tests/NetPilot.Platform.Windows.Tests/WindowsImmutableEnginePackageVerifierTests.cs`

Windows tests проверяют:

- public API не принимает string path;
- verified result types нельзя создать через public constructor;
- exact component успешно проходит verification;
- живой verified package блокирует новые write/delete opens;
- после `Dispose()` verification lock освобождается;
- hash mismatch отклоняется;
- missing component отклоняется;
- extra package file отклоняется;
- hardlinked component отклоняется;
- forged resolved path, не совпадающий с trusted layout, отклоняется.

Тесты используют internal/reflection seam только на временном каталоге. Production public API такого seam не предоставляет.

## Чего R28.16 НЕ делает

R28.16 не:

- скачивает package;
- создаёт/копирует native files;
- меняет ACL;
- назначает owner;
- создаёт ArgumentList;
- вызывает `Process.Start`;
- запускает `winws2`/zapret2/ByeDPI/DPIBreak;
- устанавливает или загружает WinDivert;
- меняет firewall/network state;
- выдаёт `ADMIT`;
- меняет `DenyAllEngineRuntimeAdmissionPolicy`;
- меняет server-side `CreateNoAdmittedEnginePolicy()`;
- меняет `NoAdmittedZapretProcessLauncher`.

Production trusted executable/package catalogs остаются пустыми до появления NetPilot-owned fork artifact.

## Следующий безопасный этап

После зелёного CI: **R28.17 — protected install ACL/owner verification + verified-package handoff contract**.

Он должен:

1. доказать, что managed root/package directories и files не user-writable;
2. проверить ожидаемого owner/ACL без автоматического ослабления прав;
3. привязать будущий launcher к живому `WindowsVerifiedEnginePackage`;
4. запретить launch после `Dispose()` verification locks;
5. по-прежнему не добавлять arbitrary paths/arguments;
6. не разрешать native execution до отдельного `LAB_APPROVED`.

Только после этого имеет смысл вводить typed allowlisted argument compiler.

Admission остаётся `REVIEW / BLOCK`, `may_execute:false`, `may_integrate:false`, `lab_execution_allowed:false`.
