# R40 — ByeDPI lifecycle soak / state-drift gate

## Status

**SPLIT SOAK IMPLEMENTED / FINAL PREFLIGHT PENDING / RUNTIME EVIDENCE PENDING / PRODUCTION ADMISSION BLOCK**

Base: R39 real service restart / ownership reconciliation PASS.

## Goal

R34–R39 prove individual lifecycle/security transitions. R40 repeats those transitions enough times to detect state drift that a single happy-path run may miss: residual native processes/listeners, stale ownership, broken crash cleanup, unexpected per-SID profile growth, or enrollment artifact resurrection.

R40 does not add a new native launcher, IPC command, production catalog entry, installer component or production runtime capability.

## Split orchestration

The committed branch-side runner intentionally owns only the non-privileged repeated typed-IPC portion. Administrative service termination and temporary-user creation remain in the one-shot hosted workflow, outside the product/runtime surface.

`scripts/security/r40-lifecycle-soak.ps1` accepts **no command-line arguments** and performs:

- 8 full graceful functional cycles through the existing R36 authenticated IPC client;
- fixed service `NetPilot.R40.Lab`;
- fixed loopback port `39036`;
- after every cycle, zero `ciadpi.exe` processes and zero listener residue.

The hosted one-shot workflow additionally performs:

- 4 abrupt LocalSystem service termination/restart cycles using the already-audited R37/R39 containment/reconciliation path;
- 4 ownership handoff cycles between two distinct Windows SIDs using the existing R39 typed IPC client;
- exact per-SID profile-set validation;
- enrollment nonce/session cleanup validation;
- final process/listener/workflow-owned-state cleanup.

## Graceful cycles

Each committed-runner cycle invokes the existing R36 authenticated IPC client with its full SOCKS5 -> synthetic local HTTP path:

`Stopped -> Start -> Running -> SOCKS/HTTP -> Stop -> Stopped`

After every cycle:

- zero `ciadpi.exe` processes;
- zero listener on 39036.

## Hosted crash/restart cycles

Each cycle must:

1. start ByeDPI through existing R36 typed IPC;
2. prove exactly one native process/listener;
3. verify the exact LocalSystem service PID executable path;
4. terminate that lab service process abruptly;
5. require Job Object cleanup of process/listener;
6. restart the service;
7. require the existing R39 authenticated IPC client to observe typed `Stopped` and no listener.

## Hosted SID handoff cycles

A distinct temporary Windows SID is created once by the hosted lab. For every handoff cycle:

1. second SID acquires a fresh session through existing R39 typed IPC;
2. primary SID Start and Stop are both denied with `EngineOwnedByAnotherUser`;
3. second-SID lease remains Running;
4. second SID stops its own session;
5. zero native process/listener residue remains.

## State-drift checks

At the end of soak:

- exactly two service-owned profile directories must exist: primary SID + second SID;
- no unexpected third SID profile may appear;
- enrollment nonce/session must remain absent after completed enrollment;
- final `ciadpi.exe` count must be zero;
- final listener count on 39036 must be zero;
- temporary second Windows account is removed;
- workflow-owned protected tree is removed by final CI cleanup.

The expected second-SID profile directory is intended service-owned per-SID state, not an error. The entire lab tree is deleted after evidence collection.

## Security boundary

`scripts/security/r40-lifecycle-soak-boundary-check.sh` requires:

- production service remains wired to no-admit launcher/plan provider;
- mandatory Windows Job Object crash containment remains present;
- engine-aware `(SID + resourceId)` ownership remains present;
- committed soak runner accepts no arguments and keeps 8 graceful cycles/service/port fixed;
- committed runner uses only the existing R36 typed IPC client;
- direct native launcher/argv construction is forbidden;
- system proxy, WinDivert and firewall/network mutation are forbidden;
- production installer must not reference R40 lab assets.

## Planned evidence markers

```text
PRODUCTION_ADMISSION=BLOCK
R40_GRACEFUL_CYCLES=8
R40_CRASH_CYCLES=4
R40_HANDOFF_CYCLES=4
R40_GRACEFUL_SOAK=PASS
R40_CRASH_RESTART_SOAK=PASS
R40_SECOND_SID_DISTINCT=True
R40_SID_HANDOFF_SOAK=PASS
R40_PROFILE_SET_EXACT=True
R40_ENROLLMENT_ARTIFACTS_CLEAN=True
R40_FINAL_ENGINE_RESIDUE_ZERO=True
R40_SECOND_USER_CLEANUP=PASS
WORKFLOW_OWNED_STATE_CLEANUP=PASS
RESULT=R40_LIFECYCLE_SOAK_PASS
```

## Production state

Production native admission remains **BLOCK**. R40 is a lab-only confidence/stability gate and cannot by itself authorize native production execution or release rollout.
