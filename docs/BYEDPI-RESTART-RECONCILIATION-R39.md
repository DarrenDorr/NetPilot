# R39 — ByeDPI service restart / ownership reconciliation

## Status

**LAB PROBE IMPLEMENTED / PREFLIGHT PENDING / RUNTIME EVIDENCE PENDING / PRODUCTION ADMISSION BLOCK**

Base: R38 real second-SID lease isolation PASS.

## Security question

R37 proves a service-owned ByeDPI process is killed by the Windows Job Object if the LocalSystem host terminates abruptly. R38 proves another authenticated Windows SID cannot steal or stop a live session while the owning service instance is alive.

R39 joins those boundaries and tests the restart transition itself.

The service keeps engine ownership in memory. Losing that memory is safe only if the old native process is also gone. Therefore the intended restart semantics are:

1. user A owns and runs ByeDPI;
2. the LocalSystem lab service is terminated abruptly;
3. `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` removes the old ByeDPI process/listener;
4. the same service starts again with a new in-memory ownership store;
5. authenticated IPC reports `Stopped` — there is no active session to preserve;
6. user B may acquire a **new** ByeDPI session;
7. user A is now a non-owner and cannot Start/Stop user B's new session;
8. user B can stop its own session and cleanup completes.

This intentionally does not persist a stale lease across service restart. A lease without a live service-owned process would create denial-of-service and misleading ownership state. Conversely, clearing ownership while an old process remains alive would create a session-steal defect. R39 requires both process containment and ownership reset to agree on `Stopped` before new acquisition.

## Lab-only probe

`tools/NetPilot.ByeDpiR39RestartClient`

Fixed modes only:

- `--expect-stopped-after-restart`
  - authenticated Ping;
  - requires typed ByeDPI `Stopped`;
  - requires no loopback listener.

- `--start-fresh-owner`
  - requires `Stopped` before acquisition;
  - typed IPC Start only;
  - requires `Running` and loopback listener.

- `--expect-nonowner-denied`
  - requires a running fresh session owned by the other SID;
  - Start and Stop must both fail with `EngineOwnedByAnotherUser`;
  - running state must remain unchanged.

- `--fresh-owner-stop`
  - fresh owner stops through typed IPC;
  - requires `Stopped` and listener cleanup.

The probe cannot create or kill processes, control Windows services, choose executable paths, construct argv or access the native launcher.

## Planned hosted runtime

A pinned Windows Server 2025 lab should:

1. checkout an exact R39 checkpoint and verify the exact reviewed R34 ByeDPI hash;
2. publish the existing hardened lab LocalSystem host and R39 client;
3. stage service/client/engine under a protected `Program Files\NetPilot` tree;
4. enroll primary interactive user A and start ByeDPI;
5. verify exactly one SYSTEM-owned `ciadpi.exe` and the loopback listener;
6. terminate the LocalSystem service process abruptly;
7. require old `ciadpi.exe` and listener to disappear before service restart;
8. restart the same service and require user A to observe `Stopped` through authenticated IPC;
9. create temporary user B with a distinct SID and non-zero session/token;
10. user B starts a fresh session through authenticated IPC;
11. user A must fail Start/Stop with `EngineOwnedByAnotherUser` while B's process remains running;
12. user B stops its session;
13. require zero residual process/listener and remove all workflow-owned state.

The run must fail closed if any prerequisite, SID/session distinction, containment cleanup, status transition or lease denial cannot be proven.

## Security boundary

`scripts/security/r39-restart-reconciliation-boundary-check.sh`

The gate requires:

- production `NetPilot.Service` remains wired to no-admit launcher/plan provider;
- the audited launcher still attaches every created native process to `WindowsKillOnCloseJob` and fails closed on containment failure;
- IPC still uses engine-aware `(SID + resourceId)` ownership;
- R39 client remains typed-IPC-only;
- production installer contains no R39 lab component.

## Production state

Production native admission remains **BLOCK**. R39 is restart/reconciliation evidence for the lab-only explicit loopback-proxy path and is not a production admission decision.
