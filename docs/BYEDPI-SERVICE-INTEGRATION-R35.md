# R35 — ByeDPI service integration and authenticated IPC

## Status

**SERVICE + IPC SCAFFOLD PASS / PRODUCTION NATIVE EXECUTION BLOCK**

R34 exact runtime-tested checkpoint: `42a7f467bf822f76f0a67a3b31d3c7b8174f3648`.

R35 validated IPC checkpoint: `f3b7be600ca90332ac2b5c1b46877ba97a9a2bd1`.

R34 functional runtime evidence is PASS in its scoped hosted service-owned-controller boundary. R35 now connects that architecture to the authenticated service protocol while deliberately keeping normal production native execution disabled.

## R34 prerequisite — PASS

Pinned runtime run `41004146617` proved the exact ByeDPI candidate through:

`immutable package -> protected ACL/owner -> typed argv -> audited launcher -> service-owned controller -> SOCKS5 -> local HTTP`

Confirmed:

- two SOCKS5 -> local HTTP cycles PASS;
- stop/restart cleanup PASS;
- crash reconciliation PASS;
- final controller state `Stopped`;
- system proxy unchanged;
- WinDivert unchanged/not requested;
- no residual `ciadpi.exe` or proxy listener;
- production admission remained BLOCK.

## What R35 adds

- `IWindowsByeDpiRuntimePlanProvider` — zero-input provider contract for a fully verified and compiled ByeDPI plan.
- `NoAdmittedWindowsByeDpiRuntimePlanProvider` — production default that always rejects plan creation.
- `WindowsByeDpiRuntimeSession` — service-facing status/start/stop wrapper over the bounded `WindowsByeDpiServiceOwnedProcessController`.
- Production DI registration in `NetPilot.Service`.
- IPC protocol v4 / pipe `NetPilot.Service.v4`.
- Typed authenticated commands:
  - `ByeDpiGetStatus`;
  - `ByeDpiStart`;
  - `ByeDpiStop`.
- `IpcByeDpiStatus` exposes only `Running/Stopped`; it does not expose PID, listener port, path, owner SID or other privileged runtime details.
- All ByeDPI IPC commands are no-payload commands. GUI/client cannot supply raw CLI, path, URL, environment, bind address, arbitrary port or engine strategy text.
- Global machine engine lease is now bound to `(Windows SID + engine resource id)`.
- Legacy Zapret lease methods are explicitly pinned to resource `zapret`; new ByeDPI operations use resource `byedpi`.
- Resource-scoped reconciliation prevents a stopped ByeDPI session from clearing a live Zapret lease and vice versa.
- Regression tests keep unrelated Zapret test clients fail-closed for the new ByeDPI methods.
- `scripts/security/r35-service-integration-boundary-check.sh` enforces the new boundary.

## Two independent production execution locks

R35 intentionally keeps two independent blockers in the normal service composition:

1. `IWindowsVerifiedEngineProcessLauncher` resolves to `NoAdmittedWindowsVerifiedEngineProcessLauncher`.
2. `IWindowsByeDpiRuntimePlanProvider` resolves to `NoAdmittedWindowsByeDpiRuntimePlanProvider`.

The ordinary installed NetPilot service therefore cannot create a ByeDPI native process even though authenticated IPC commands now exist.

`ByeDpiStart` additionally passes through the normal `OperationPolicy.EngineStart` gate and global engine lease before the runtime session can be reached.

## R35 validation — PASS

Pinned preflight run `41005023173` on checkpoint `f3b7be600ca90332ac2b5c1b46877ba97a9a2bd1`:

- security policy: PASS;
- runtime admission composition: PASS;
- R35 service integration boundary: PASS;
- Linux full solution restore/build: PASS;
- Core/IPC tests: PASS;
- Windows service + GUI restore/build: PASS;
- Windows platform tests: PASS;
- ByeDPI native execution: NOT PERFORMED;
- production runtime admission: BLOCK.

An earlier preflight run `41004809863` correctly failed because an existing Zapret test fake had not implemented the newly extended `IPrivilegedServiceClient`. The fake was updated with explicit fail-closed ByeDPI methods; the complete follow-up run above then passed. No security gate was weakened to fix the failure.

## What R35 still does not prove

R35 does not yet prove:

- execution of the exact ByeDPI candidate inside the actually installed LocalSystem `NetPilot.Service` process tree;
- real authenticated IPC start/stop/status controlling that native process;
- multi-user SID/lease behavior while a real ByeDPI process is running;
- restart/reboot recovery of an admitted installed-service runtime;
- system-wide transparent routing;
- WinDivert safety;
- production engine admission.

## Next gate

The next scoped gate should be an isolated **installed LocalSystem service lab composition** that uses the exact R34-tested binary/package/typed plan while remaining separate from normal production DI and normal installer release behavior.

That lab must prove at minimum:

1. authenticated standard-user IPC reaches the LocalSystem service;
2. only the owning SID can stop the active engine session;
3. a second SID cannot steal the global lease;
4. service stop/crash/restart leaves no orphan process/listener;
5. no raw engine material crosses IPC;
6. production no-admit composition remains unchanged outside the lab;
7. no system proxy or WinDivert mutation occurs.

Only after that evidence should a separate explicit admission decision consider replacing either no-admit production registration.
