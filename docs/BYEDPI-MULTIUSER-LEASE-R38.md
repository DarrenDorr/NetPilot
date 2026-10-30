# R38 — ByeDPI multi-user SID lease isolation

## Status

**REAL SECOND-SID RUNTIME PASS / PRODUCTION ADMISSION BLOCK**

Base: R37 hardened crash containment PASS.

R38 verifies the product rule that the global machine engine session belongs to one authenticated Windows SID plus one engine resource. Another authenticated Windows user may observe safe status but must not steal, restart or stop the active engine.

## Product boundary under test

R35 introduced resource-aware ownership:

`(authenticated Windows SID + canonical engine resource id)`

For ByeDPI the canonical resource is `byedpi`.

Expected behavior while user A owns an active ByeDPI session:

- user B can authenticate to the same LocalSystem IPC service and receive non-sensitive `Running/Stopped` status;
- user B `ByeDpiStart` -> `EngineOwnedByAnotherUser`;
- user B `ByeDpiStop` -> `EngineOwnedByAnotherUser`;
- failed user-B operations do not release or mutate user-A lease;
- user A can subsequently stop the engine successfully;
- IPC status does not disclose owner SID, process id, executable path or listener port.

## Lab-only probe

`tools/NetPilot.ByeDpiR38LeaseClient`

Fixed modes:

- `--expect-nonowner-denied`
  - authenticated Ping;
  - requires ByeDPI `Running`;
  - requires both Start and Stop to fail with `EngineOwnedByAnotherUser`;
  - requires engine to remain `Running` afterwards.

- `--owner-stop`
  - authenticated Ping;
  - requires active ByeDPI;
  - performs owner Stop;
  - requires typed `Stopped` and no loopback listener.

The probe uses only `WindowsPrivilegedServiceClient`. It cannot launch native processes, select executable paths or construct engine arguments.

## Runtime evidence — PASS

Hosted workflow: `R38 Real Second-SID Lease Runtime Once`

- run ID: `41009587630`;
- exact product checkpoint: `61ebea0833ce845c174510274554ad1c67dce2b0`;
- hosted OS: Windows Server 2025;
- service account: LocalSystem;
- exact ByeDPI SHA-256: `866e99c3c7690326cc6e365fe02980f06ae2b394c045555674fb95db52b0dbdd`;
- package: `0.17.3-netpilot-r34-lab`;
- protected staging under `Program Files\NetPilot`;
- primary client session was non-zero;
- a temporary second local Windows account received a distinct SID;
- second-user authenticated IPC succeeded;
- second-user Start and Stop were both denied;
- the original lease and running native process survived both denied operations;
- the original owner then stopped ByeDPI successfully;
- no `ciadpi.exe` or loopback listener remained after owner Stop;
- temporary second user, service and workflow-owned protected tree were cleaned up.

Final evidence markers:

```text
PRODUCTION_ADMISSION=BLOCK
PROTECTED_PROGRAM_FILES_TREE=True
LOCALSYSTEM_SERVICE_RUNNING=True
PRIMARY_OWNER_ENGINE_RUNNING=PASS
SECOND_SID_DISTINCT=True
SECOND_USER_AUTHENTICATED_IPC=PASS
SECOND_USER_START_DENIED=PASS
SECOND_USER_STOP_DENIED=PASS
LEASE_PRESERVED=PASS
SECOND_USER_CLEANUP=PASS
PRIMARY_OWNER_STOP_AFTER_DENIAL=PASS
R38_OWNER_STOP_CLEANUP=PASS
WORKFLOW_OWNED_STATE_CLEANUP=PASS
RESULT=R38_REAL_SECOND_SID_LEASE_PASS
```

Repository evidence copy:
`security/native-admission/evidence/byedpi-r38/real-second-sid-lease.txt`

Actions artifact:

- name: `r38-byedpi-real-second-sid-lease-evidence`;
- artifact ID: `12955959862`;
- ZIP digest: `sha256:fed546422e177eae8f46612951c070e202538eaff6c0a3ea805778ea78c98b61`;
- retention: 14 days from 2026-10-30.

## Decision

R38 is **PASS** for the scoped multi-user authenticated-IPC lease isolation claim.

This does **not** turn ByeDPI into a production-admitted engine. It does not authorize transparent routing, WinDivert integration, arbitrary native arguments, or production rollout.

## Next gate

R39 should test service restart / ownership reconciliation semantics: after an abrupt or ordinary LocalSystem service restart there must be no orphan native process, no stale lease that another SID can steal, and no path that silently converts missing in-memory ownership into authorization.

## Production state

Production native admission remains **BLOCK**. Positive production engine catalogs remain empty and the normal `NetPilot.Service` remains fail-closed for native start.
