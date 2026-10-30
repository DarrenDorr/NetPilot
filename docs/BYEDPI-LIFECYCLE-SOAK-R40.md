# R40 — ByeDPI lifecycle soak / state-drift gate

## Status

**REAL LIFECYCLE SOAK PASS / PRODUCTION ADMISSION BLOCK**

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

## Final preflight — PASS

Workflow: `R40 Lifecycle Soak Preflight Once`

- run ID: `41015060927`;
- exact R40 product checkpoint: `e0bd52524f54f6963c8827d9f56da8c185331b45`;
- Linux R35–R40 security boundaries: PASS;
- R40 PowerShell parser: PASS;
- hardened R36 LocalSystem lab service build: PASS;
- R36 authenticated IPC client build: PASS;
- R39 restart/ownership client build: PASS;
- Windows platform tests: PASS;
- native ByeDPI execution was not performed in preflight.

## Real hosted soak — PASS

Workflow: `R40 Real Lifecycle Soak Once`

- run ID: `41015307327`;
- exact product checkpoint: `e0bd52524f54f6963c8827d9f56da8c185331b45`;
- hosted OS: Windows Server 2025;
- primary interactive session: `2`;
- LocalSystem service: `NetPilot.R40.Lab`;
- protected staging: `Program Files\NetPilot`;
- exact ByeDPI SHA-256: `866e99c3c7690326cc6e365fe02980f06ae2b394c045555674fb95db52b0dbdd`;
- package: `0.17.3-netpilot-r34-lab`;
- loopback port: `39036`.

### Graceful soak — 8/8 PASS

Every cycle completed the full authenticated IPC + SOCKS5 -> synthetic local HTTP path:

`Stopped -> Start -> Running -> SOCKS/HTTP -> Stop -> Stopped`

All eight cycle markers are PASS, followed by:

```text
R40_GRACEFUL_SOAK=PASS
R40_GRACEFUL_FINAL_RESIDUE_ZERO=True
RESULT=R40_GRACEFUL_SOAK_PASS
```

No residual `ciadpi.exe` process or loopback listener remained after any graceful cycle.

### Crash/restart soak — 4/4 PASS

For each of four cycles:

1. authenticated IPC started the exact reviewed ByeDPI candidate;
2. exactly one service-owned process/listener was present;
3. the exact LocalSystem service PID executable path was verified;
4. the service process was terminated abruptly;
5. Windows Job Object containment removed the old native process/listener;
6. the service restarted;
7. authenticated IPC returned typed `Stopped` with no listener.

Final marker:

```text
R40_CRASH_RESTART_SOAK=PASS
```

### Distinct-SID ownership handoff soak — 4/4 PASS

A temporary second Windows user was created with a SID distinct from the primary SID. In every one of four handoff cycles:

1. the second SID authenticated and acquired a fresh ByeDPI session;
2. primary/former owner Start was denied with `EngineOwnedByAnotherUser`;
3. primary/former owner Stop was denied with `EngineOwnedByAnotherUser`;
4. the second-SID lease remained Running after both denied operations;
5. the second SID stopped its own session;
6. process/listener cleanup completed.

Final markers:

```text
R40_SECOND_SID_DISTINCT=True
R40_SID_HANDOFF_SOAK=PASS
```

## State-drift checks — PASS

After the repeated lifecycle operations:

- exactly two service-owned SID profile directories existed: primary SID + temporary second SID;
- no unexpected third profile appeared;
- enrollment nonce/session were absent after completed enrollment;
- final `ciadpi.exe` count was zero;
- final listener count on `39036` was zero;
- temporary second Windows account was removed;
- workflow-owned service and protected lab tree were removed.

Final runtime markers:

```text
PRODUCTION_ADMISSION=BLOCK
PROTECTED_PROGRAM_FILES_TREE=True
LOCALSYSTEM_SERVICE_RUNNING=True
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

## Evidence

Repository copy:
`security/native-admission/evidence/byedpi-r40/lifecycle-soak.txt`

Actions artifact:

- name: `r40-byedpi-lifecycle-soak-evidence`;
- artifact ID: `12958788353`;
- size: `5475` bytes;
- ZIP digest: `sha256:322a4db568c0dbf2eb9f69b12a9e69303b80aacb0b684f6841d31f27aebda75c`;
- created: `2026-10-30T06:42:04Z`;
- expires: `2026-11-13T06:42:04Z`.

The artifact contains the aggregate runtime evidence plus per-cycle graceful, crash-start, crash-reconcile, second-SID start, former-owner denial and second-SID stop evidence files.

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

## Decision

R40 is **PASS** for the scoped repeated lifecycle/state-drift claim.

Across 16 repeated lifecycle transitions (8 graceful functional cycles, 4 abrupt crash/restart reconciliations and 4 distinct-SID ownership handoffs), no process/listener/lease/profile/enrollment state drift was observed within this lab-only explicit loopback-proxy composition.

This result increases confidence in the already-reviewed lifecycle boundaries, but it is **not** a production-admission decision.

## Next gate

R41 should be a production-admission **readiness review matrix**, not another feature-enablement step. It should consolidate R34–R40 evidence and explicitly enumerate every remaining blocker for a production ByeDPI decision: provenance/update/install path, licensing/notices, production package lifecycle, recovery/UX, telemetry/logging privacy, release signing/trust, operational support, and any still-unresolved platform or policy boundary. R41 must keep production admission BLOCK unless every required production gate is independently satisfied.

## Production state

Production native admission remains **BLOCK**. R40 does not authorize production native execution, transparent routing, WinDivert integration, arbitrary native arguments, production package rollout or enabling ByeDPI in the normal `NetPilot.Service`.
