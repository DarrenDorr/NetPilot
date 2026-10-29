# R28.19 — typed executable selection и non-starting ProcessStartInfo factory

Дата: 29.10.2026.

Статус: **NON-STARTING PROCESS METADATA / NO PROCESS.START / NO ADMIT / RELEASE GATE BLOCK**.

## Цель

R28.18 создаёт только typed argv plan. R28.19 подготавливает следующий объект Windows execution boundary — `ProcessStartInfo` — но намеренно не запускает процесс.

Новый factory не принимает executable path, working directory, raw argument string или environment от пользователя/конфига.

## Factory

Файл:

`src/NetPilot.Platform.Windows/WindowsVerifiedEngineProcessStartInfoFactory.cs`

Public API:

`WindowsVerifiedEngineProcessStartInfoFactory.Create(WindowsCompiledEngineArgumentPlan)`

Единственный вход — уже скомпилированный typed plan, связанный с живым R28.17 pre-launch handoff.

## Executable selection

Factory:

1. требует живой `WindowsCompiledEngineArgumentPlan`;
2. повторно проверяет соответствие backend/platform/architecture/profile и exact immutable identity;
3. требует ровно один `PrimaryExecutable` в immutable identity;
4. требует ровно один verified `PrimaryExecutable` в verified package;
5. сверяет logical component id и SHA-256 primary component с immutable identity;
6. получает executable path только из verified package;
7. требует, чтобы executable был strict child exact engine root;
8. working directory выводится только как verified parent directory primary executable и остаётся внутри engine root;
9. перед возвратом повторно проверяет, что R28.16 verification locks всё ещё живы.

Пользовательский path в factory не передаётся.

## ProcessStartInfo policy

Создаваемый объект имеет:

- `UseShellExecute = false`;
- `CreateNoWindow = true`;
- hidden window style;
- `ErrorDialog = false`;
- stdin/stdout/stderr не перенаправляются на этом неисполняемом этапе;
- raw `Arguments` не используется;
- каждый token добавляется только через `ArgumentList` из R28.18 compiled plan.

Factory не вызывает `Process.Start`.

## Environment policy

Inherited environment полностью очищается.

Возвращаются только два системных значения, полученные самим процессом NetPilot из Windows:

- `SystemRoot`;
- `WINDIR`.

`PATH`, `PATHEXT`, пользовательские env values и произвольные environment overrides не наследуются.

Это снижает влияние PATH/tool/search-order hijacking на будущий native launch boundary.

## Tests

Файл:

`tests/NetPilot.Platform.Windows.Tests/WindowsVerifiedEngineProcessStartInfoFactoryTests.cs`

Проверяется:

- public factory принимает только `WindowsCompiledEngineArgumentPlan`;
- `FileName` происходит из exact verified PrimaryExecutable;
- working directory происходит из verified primary parent;
- `UseShellExecute=false`;
- raw `Arguments` пуст;
- argv находится только в `ArgumentList`;
- environment содержит только `SystemRoot` и `WINDIR`;
- `PATH`/`PATHEXT` отсутствуют;
- после Dispose verified package factory завершается fail-closed;
- verified primary с неправильным component kind отклоняется;
- factory не публикует process-start operation.

Synthetic test executable не запускается.

## Чего R28.19 НЕ делает

R28.19 не:

- вызывает `Process.Start`;
- создаёт production argument plan;
- добавляет production executable identity;
- разрешает raw CLI/Lua/script/path/URL/env;
- включает legacy `ZapretLaunchPlanBuilder` в trusted production chain;
- запускает zapret2/ByeDPI/DPIBreak;
- устанавливает/загружает WinDivert;
- меняет firewall/network state;
- выдаёт `ADMIT` / `LAB_APPROVED`;
- открывает server-side `EngineStart`/`EngineInstall`.

## TOCTOU boundary

R28.19 всё ещё возвращает path-based `ProcessStartInfo`, потому что Windows `CreateProcess` в дальнейшем стартует image по path, а не по уже открытому verifier handle.

Поэтому R28.19 не утверждает, что execution boundary завершена. Verification locks R28.16 остаются обязательными, а будущий launcher должен непосредственно перед реальным стартом повторно проверить живой handoff и определить стратегию final path/hash revalidation.

## Следующий безопасный этап

После зелёного CI: **R28.20 — non-executing launch request / launcher boundary**.

До любого `Process.Start` должны быть отдельно определены:

1. final immediate pre-start revalidation;
2. admission token/lab-only gate;
3. process ownership/lifecycle/cleanup contract;
4. timeout/crash/reboot behavior;
5. WinDivert blocker disposition;
6. isolated Windows lab approval.

Production execution всё ещё `BLOCK`.
