# R38 — ByeDPI multi-user SID lease isolation

## Status

**LAB PROBE IMPLEMENTED / RUNTIME EVIDENCE PENDING / PRODUCTION ADMISSION BLOCK**

Base: R37 hardened crash containment PASS.

R38 tests the product rule that the global machine engine session belongs to one authenticated Windows SID plus one engine resource. Another authenticated Windows user may observe safe status but must not steal, restart or stop the active engine.

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
- no owner SID, process id, path or listener port is disclosed through IPC.

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

## Planned runtime evidence

A pinned hosted Windows lab should:

1. use the hardened R37 code and exact R34-tested ByeDPI candidate;
2. install the isolated LocalSystem lab service under protected Program Files;
3. primary interactive Windows user enrolls and starts ByeDPI through authenticated IPC;
4. create a temporary second local Windows user with a random ephemeral password;
5. run the R38 non-owner probe under that second user's token in a non-zero Windows session;
6. require authenticated IPC success but Start/Stop denial;
7. verify engine remains running and still belongs to the first user lease;
8. first user performs owner Stop and cleanup;
9. remove the temporary user and workflow-owned service/tree;
10. upload text-only evidence with no passwords and no owner SID disclosure.

If the second-user process cannot obtain a non-zero interactive session on the hosted runner, the workflow must fail closed rather than treating a Session 0 token as valid multi-user evidence.

## Production state

Production native admission remains BLOCK. R38 is evidence for SID/lease isolation only; it does not authorize a production engine package or transparent routing.
