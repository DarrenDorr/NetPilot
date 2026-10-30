# R36 — ByeDPI installed LocalSystem service lab

## Status

**COMPILE/SECURITY PREFLIGHT PASS / INSTALLED LOCALSYSTEM + AUTHENTICATED IPC RUNTIME PASS / PRODUCTION ADMISSION BLOCK**

Base: R35 authenticated IPC/service scaffold.

Exact product-code checkpoint validated by R36:
`aa2711ae1bce1425306aad3cd606295c7b750c90`.

R36 remains a separate lab-only composition. It does not add a runtime switch, environment variable or hidden admission path to the production `NetPilot.Service`.

## Lab components

- `tools/NetPilot.ByeDpiR36ServiceLab`
  - fixed service `NetPilot.R36.Lab`;
  - Windows x64 + LocalSystem only;
  - no command-line arguments;
  - exact R34 contract, fixed loopback proxy port `39036`;
  - immutable verification handles held for full provider lifetime;
  - same `PrivilegedIpcWorker` / IPC v4 as product service;
  - only EngineStart/EngineStop enabled in lab composition;
  - Zapret remains no-admit.

- `tools/NetPilot.ByeDpiR36LabClient`
  - interactive Windows client;
  - uses `WindowsPrivilegedServiceClient` only;
  - existing nonce/session enrollment;
  - typed ByeDPI Status/Start/Stop only;
  - no executable path/native argv access;
  - full SOCKS5 -> local HTTP functional cycle;
  - fixed service-stop and status-only lifecycle modes.

## Production separation

Normal `NetPilot.Service` remains composed with:

- `NoAdmittedWindowsVerifiedEngineProcessLauncher`;
- `NoAdmittedWindowsByeDpiRuntimePlanProvider`.

The production installer does not package/register R36 lab executables. Production native engine catalogs remain empty.

## Compile/security preflight — PASS

Pinned run `41005736327` on checkpoint `aa2711ae1bce1425306aad3cd606295c7b750c90`:

- security policy: PASS;
- runtime admission policy: PASS;
- R35 service-integration boundary: PASS;
- R36 lab-isolation boundary: PASS;
- Linux solution restore/build: PASS;
- Windows R36 service/client build: PASS;
- Windows platform tests: PASS;
- native execution: NOT PERFORMED.

## Installed LocalSystem runtime — PASS

The first runtime attempt, run `41006065721`, rebuilt and verified the exact candidate and completed protected staging, but failed **before service start** because the PowerShell `sc.exe create` call packaged `start=` incorrectly (`ERROR: Invalid start= field`). ByeDPI did not execute in that failed attempt, and workflow-owned cleanup passed.

A one-shot retry changed only the service-registration mechanism to `New-Service`; product code and the exact R36 checkpoint were unchanged.

Successful retry run: `41007056260`.

Evidence artifact:
- artifact id: `12956356229`;
- artifact name: `r36-byedpi-installed-localsystem-retry-evidence`;
- ZIP SHA-256: `6d7279ca0190871dc4255ad0ed64ca90963aecc2e078f31a4aa795751129e24b`.

Exact candidate SHA-256 remained:
`866e99c3c7690326cc6e365fe02980f06ae2b394c045555674fb95db52b0dbdd`.

Reviewed evidence proves:

- protected Program Files staging: PASS;
- Windows service account: `LocalSystem`;
- LocalSystem service reached Running;
- enrollment: PASS;
- authenticated IPC v4: PASS;
- IPC Start: PASS;
- SOCKS5 -> local HTTP through service-owned ByeDPI: PASS;
- IPC Stop cleanup: PASS;
- `ciadpi.exe` is a direct child of the R36 service process;
- `ciadpi.exe` owner is SYSTEM;
- stopping the Windows service while ByeDPI is running leaves no process/listener: PASS;
- service restart returns typed ByeDPI status `Stopped`;
- system proxy unchanged;
- WinDivert state unchanged/not present;
- no residual `ciadpi.exe`;
- no residual proxy listener;
- workflow-owned service/tree cleanup: PASS;
- final result: `BYEDPI_INSTALLED_LOCALSYSTEM_R36_PASS`.

Repository evidence:
`security/native-admission/evidence/byedpi-r36/installed-localsystem-ipc-runtime.txt`.

## Decision

R36 is **PASS in the scoped installed LocalSystem + authenticated IPC boundary**.

This permits moving to the next evidence/admission stage. It does **not** by itself authorize production native execution.

## Still not proven

R36 does not yet prove:

- abrupt termination/crash of the LocalSystem service while ByeDPI is active;
- real second interactive Windows SID attempting to steal/stop an active engine lease;
- reboot recovery of an admitted production engine;
- system-wide transparent routing;
- WinDivert safety;
- production engine admission.

Those remain explicit later gates.
