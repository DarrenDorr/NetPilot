# R39 — ByeDPI service restart / ownership reconciliation

## Status

**REAL RESTART RUNTIME PASS / PRODUCTION ADMISSION BLOCK**

Base: R38 real second-SID lease isolation PASS.

## Security question

R37 proves a service-owned ByeDPI process is killed by the Windows Job Object if the LocalSystem host terminates abruptly. R38 proves another authenticated Windows SID cannot steal or stop a live session while the owning service instance is alive.

R39 joins those boundaries and proves the restart transition itself.

The service keeps engine ownership in memory. Losing that memory is safe only if the old native process is also gone. The verified restart semantics are:

1. user A owns and runs ByeDPI;
2. the LocalSystem lab service is terminated abruptly;
3. `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` removes the old ByeDPI process/listener;
4. the same service starts again with a new in-memory ownership store;
5. authenticated IPC reports `Stopped` — there is no active session to preserve;
6. user B with a distinct SID acquires a **new** ByeDPI session;
7. user A is now a non-owner and cannot Start/Stop user B's new session;
8. user B stops its own session and cleanup completes.

This intentionally does not persist a stale lease across service restart. A lease without a live service-owned process would create denial-of-service and misleading ownership state. Conversely, clearing ownership while an old process remains alive would create a session-steal defect. R39 requires process containment and ownership reset to agree on `Stopped` before new acquisition.

## Lab-only probe

`tools/NetPilot.ByeDpiR39RestartClient`

Fixed modes only:

- `--expect-stopped-after-restart` — authenticated Ping, typed `Stopped`, no listener;
- `--start-fresh-owner` — requires `Stopped`, typed IPC Start, then `Running` + listener;
- `--expect-nonowner-denied` — Start/Stop both require `EngineOwnedByAnotherUser`, fresh lease remains Running;
- `--fresh-owner-stop` — fresh owner stops through typed IPC and requires listener cleanup.

The probe cannot create or kill processes, control Windows services, choose executable paths, construct argv or access the native launcher.

## Compile/security preflight — PASS

Workflow: `R39 Restart Reconciliation Preflight Once`

- run ID: `41013615066`;
- exact R39 checkpoint: `99b0ef2feb4a9276a1b06420a5df53f95fc599b8`;
- Linux security/build job: PASS;
- Windows hardened-service/client/tests job: PASS;
- R35–R39 security boundaries: PASS;
- no native ByeDPI execution in preflight.

## Real hosted runtime — PASS

Workflow: `R39 Real Service Restart Reconciliation Once`

- run ID: `41013877749`;
- exact product checkpoint: `99b0ef2feb4a9276a1b06420a5df53f95fc599b8`;
- hosted OS: Windows Server 2025;
- primary interactive session: `2`;
- LocalSystem service: `NetPilot.R39.Lab`;
- protected staging: `Program Files\NetPilot`;
- exact ByeDPI SHA-256: `866e99c3c7690326cc6e365fe02980f06ae2b394c045555674fb95db52b0dbdd`;
- package: `0.17.3-netpilot-r34-lab`;
- loopback port: `39036`.

Verified markers:

```text
PRODUCTION_ADMISSION=BLOCK
PROTECTED_PROGRAM_FILES_TREE=True
LOCALSYSTEM_SERVICE_RUNNING=True
ORIGINAL_OWNER_ENGINE_RUNNING=PASS
ABRUPT_SERVICE_TERMINATION=PASS
OLD_ENGINE_KILLED_ON_SERVICE_CLOSE=PASS
OLD_LISTENER_CLEANUP=PASS
SERVICE_RESTART=PASS
POST_RESTART_STATUS_STOPPED=PASS
SECOND_SID_DISTINCT=True
SECOND_USER_FRESH_START=PASS
OLD_OWNER_DENIED_AFTER_RESTART=PASS
FRESH_LEASE_PRESERVED=PASS
SECOND_USER_FRESH_STOP=PASS
FINAL_ENGINE_CLEANUP=PASS
SECOND_USER_CLEANUP=PASS
WORKFLOW_OWNED_STATE_CLEANUP=PASS
RESULT=R39_RESTART_RECONCILIATION_PASS
```

The old owner also received typed `EngineOwnedByAnotherUser` denial for both Start and Stop after the distinct second SID acquired the fresh post-restart session.

## Evidence

Repository copy:
`security/native-admission/evidence/byedpi-r39/restart-reconciliation.txt`

Actions artifact:

- name: `r39-byedpi-restart-reconciliation-evidence`;
- artifact ID: `12958394667`;
- size: `1701` bytes;
- ZIP digest: `sha256:228a40bebfb42962ee270ac2b3322b1781a84e4c42e79b7e143f1281a9e94962`;
- expires: `2026-11-13T06:26:31Z`.

## Security boundary

`scripts/security/r39-restart-reconciliation-boundary-check.sh` requires:

- production `NetPilot.Service` remains wired to no-admit launcher/plan provider;
- the audited launcher still attaches every created native process to `WindowsKillOnCloseJob` and fails closed on containment failure;
- IPC still uses engine-aware `(SID + resourceId)` ownership;
- R39 client remains typed-IPC-only;
- production installer contains no R39 lab component.

## Decision

R39 is **PASS** for the scoped restart/ownership-reconciliation claim.

A service restart is safe for this lab-only explicit loopback-proxy composition because the old service-owned native process is removed before the new service instance can expose `Stopped` and accept a fresh lease. A former owner cannot control a fresh session subsequently acquired by another SID.

## Production state

Production native admission remains **BLOCK**. R39 is not a production admission decision and does not authorize transparent routing, WinDivert integration, arbitrary native arguments, production package rollout or enabling the native engine in the normal product service.
