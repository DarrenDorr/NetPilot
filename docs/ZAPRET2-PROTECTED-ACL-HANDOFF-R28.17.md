# R28.17 — protected install ACL/owner verification и verified-package handoff

Дата: 29.10.2026.

Статус: **NON-EXECUTING SECURITY GATE / NO ADMIT / NO ARGUMENTS / NO PROCESS START / RELEASE GATE BLOCK**.

## Цель

R28.16 доказал exact layout/hash/file identity и удерживает verification locks, но намеренно не утверждал, что существующий managed package защищён корректными Windows ACL/owner.

R28.17 добавляет отдельный read-only ACL/owner gate и неисполняемый handoff для будущего launcher boundary.

## ACL/owner verifier

Файл:

`src/NetPilot.Platform.Windows/WindowsProtectedEngineSecurity.cs`

Public API:

`WindowsProtectedEngineSecurityVerifier.Verify(WindowsVerifiedEnginePackage)`

Public API не принимает arbitrary path, SID, ACL text, command line, executable или URL.

Verifier требует, чтобы R28.16 verification locks всё ещё были живы до и после ACL проверки.

Проверяется security chain от системного `Program Files` до exact managed engine root, component subdirectories и самих component files.

Для каждого объекта:

1. объект существует и имеет ожидаемый тип;
2. reparse point запрещён;
3. owner SID должен быть одним из privileged identities:
   - LocalSystem;
   - BUILTIN\\Administrators;
   - NT SERVICE\\TrustedInstaller (stable service SID);
4. Null DACL запрещён;
5. non-canonical DACL отклоняется fail-closed;
6. любой Allow ACE с write/delete/change-permissions/take-ownership правами разрешён только privileged principal;
7. CREATOR OWNER / OWNER RIGHTS допускаются только когда уже проверенный owner привилегирован;
8. untrusted mutation ACE отклоняется даже если он inheritance-only, чтобы unsafe write inheritance не считалась допустимой.

Verifier **не изменяет ACL/owner** и не пытается автоматически «исправить» небезопасный filesystem object.

## Result

`WindowsProtectedEngineSecurityVerification`:

- не имеет public constructor;
- создаётся только после успешной ACL/owner проверки;
- связан с exact живым `WindowsVerifiedEnginePackage`;
- не является admission или execution token.

## Pre-launch handoff

Добавлен `WindowsVerifiedEnginePreLaunchHandoff` и factory.

Handoff связывает только:

- `EngineLaunchIdentityBinding`;
- exact живой `WindowsVerifiedEnginePackage`;
- ACL/owner verification того же package.

Factory fail-closed проверяет:

- Windows platform;
- reference identity exact executable identity;
- security verification относится к тому же verified package;
- R28.16 verification locks ещё удерживаются.

После `WindowsVerifiedEnginePackage.Dispose()` `EnsureReady()` и создание нового handoff должны завершаться отказом.

Handoff не содержит ArgumentList, arbitrary path input, environment, service/driver/network controls и не вызывает `Process.Start`.

## Тесты

Файл:

`tests/NetPilot.Platform.Windows.Tests/WindowsProtectedEngineSecurityTests.cs`

Проверяется:

- public ACL verifier принимает только typed verified package, а не string path;
- result/handoff types нельзя создать через public constructor;
- privileged owner + SYSTEM/Administrators full control + Users read/execute проходит policy;
- Users/непривилегированный principal с Modify отклоняется;
- непривилегированный owner отклоняется;
- CREATOR OWNER inheritance допускается только поверх privileged owner;
- matching live package создаёт handoff;
- после Dispose handoff становится unusable;
- security verification другого package отклоняется;
- другая immutable identity отклоняется.

## Что R28.17 НЕ делает

R28.17 не:

- создаёт или меняет ACL;
- скачивает/устанавливает engine package;
- создаёт trusted production identity/catalog entry;
- создаёт ArgumentList;
- вызывает `Process.Start`;
- запускает zapret2/ByeDPI/DPIBreak;
- устанавливает/загружает WinDivert;
- меняет firewall/network state;
- выдаёт `ADMIT` или `LAB_APPROVED`;
- меняет `DenyAllEngineRuntimeAdmissionPolicy`;
- меняет server-side `CreateNoAdmittedEnginePolicy()`;
- меняет `NoAdmittedZapretProcessLauncher`.

## Ограничения

ACL verification снижает риск user-writable package, но не является защитой от уже привилегированного администратора/SYSTEM/TrustedInstaller. Привилегированный локальный администратор остаётся вне границы, которую можно надёжно исключить обычной user-mode программой.

R28.16 отдельно отмечает риск writable handle, открытого **до** verification. R28.17 не маскирует это ограничение: будущий isolated lab должен дополнительно проверять installer lifecycle и отсутствие небезопасных открытых handles/ACL transitions.

## Следующий безопасный этап

После зелёного CI: **R28.18 — typed allowlisted argument-plan contract без execution**.

Он должен:

1. строить arguments только из trusted profile/launch metadata;
2. не принимать raw CLI/Lua/script/path/URL/env от пользователя;
3. не содержать `Process.Start`;
4. быть связан с exact `WindowsVerifiedEnginePreLaunchHandoff`;
5. оставаться недоступным production catalog до NetPilot-owned fork artifact и отдельного admission.

Только после этого можно проектировать отдельный launcher handoff в isolated lab; native execution по-прежнему запрещён.
