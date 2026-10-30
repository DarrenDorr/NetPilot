# R37 — ByeDPI abrupt LocalSystem service crash containment

## Status

**CRASH FINDING REMEDIATED / JOB OBJECT HARDENING PREFLIGHT PASS / HARDENED ABRUPT-CRASH RUNTIME PASS / PRODUCTION ADMISSION BLOCK**

Base evidence: R36 installed LocalSystem + authenticated IPC PASS.

Exact hardened product-code checkpoint:
`f3717b773cac6316b133ac6a52269db8b26466cf`.

## Observation — finding confirmed before hardening

Pinned observation run `41007700773` used the exact validated R36 product checkpoint and exact R34-tested ByeDPI binary.

After authenticated IPC start, the LocalSystem service process was terminated abruptly with `Stop-Process -Force`.

Pre-hardening result:

- `ABRUPT_CRASH_RESIDUAL_CIADPI=True`;
- `ABRUPT_CRASH_RESIDUAL_LISTENER=True`;
- `CRASH_CONTAINMENT=NEEDS_HARDENING`;
- `RESULT=R37_CRASH_CONTAINMENT_FINDING`.

Repository evidence:
`security/native-admission/evidence/byedpi-r37/abrupt-service-crash-observation.txt`.

This proved graceful `Stop-Service` cleanup from R36 was not sufficient for crash safety.

## Hardening

R37 adds `WindowsKillOnCloseJob` in the Windows platform boundary.

The containment contract uses Windows Job Objects configured with:

`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` (`0x00002000`).

The single audited `SystemZapretProcessLauncher` still contains exactly one `Process.Start`. Immediately after process creation it must:

1. create the Job Object;
2. enable kill-on-close;
3. assign the newly-created engine process to the job;
4. retain the job handle for the managed-process lifetime.

If job creation, policy configuration or assignment fails, launch fails closed:

- the just-created process is killed best-effort;
- no uncontained engine is returned to the caller;
- the caller receives `ProcessContainmentFailed`.

If the service process terminates abruptly, Windows closes the service-owned job handle and terminates assigned engine processes without relying on managed `Dispose` or graceful host shutdown.

## Security/build preflight — PASS

The first preflight attempt, run `41008315384`, passed all security gates but found a compile-only naming collision (`CS0102`) between the Job Object information-class constant and struct. No security policy was weakened.

The constant was renamed to `JobObjectExtendedLimitInformationClass` with no semantic change.

Successful pinned preflight run: `41008630281` on exact checkpoint `f3717b773cac6316b133ac6a52269db8b26466cf`.

Confirmed:

- security policy: PASS;
- runtime admission policy: PASS;
- R35 service-integration boundary: PASS;
- R36 LocalSystem lab boundary: PASS;
- R37 crash-containment boundary: PASS;
- Linux full solution build: PASS;
- Core tests: PASS;
- Windows tests build: PASS;
- hardened R36 lab service build: PASS;
- R36 IPC client build: PASS;
- real Windows kill-on-close Job Object unit test: PASS.

## Hardened abrupt-crash runtime — PASS

Pinned run: `41008867818`.

Evidence artifact:
- id `12956593465`;
- name `r37-byedpi-hardened-crash-containment-evidence`;
- ZIP SHA-256 `01c914a0ec5b764cc5571143aab866471bd8fa8b5b9928fe2b837367f3ee44e8`.

Exact ByeDPI candidate SHA-256 remained:
`866e99c3c7690326cc6e365fe02980f06ae2b394c045555674fb95db52b0dbdd`.

Reviewed evidence proves:

- protected Program Files staging: PASS;
- LocalSystem service start: PASS;
- enrollment/authenticated IPC: PASS;
- IPC engine start: PASS;
- `ciadpi.exe` direct child of service: true;
- `ciadpi.exe` owner: SYSTEM;
- LocalSystem service process forcibly terminated: true;
- `ABRUPT_CRASH_RESIDUAL_CIADPI=False`;
- `ABRUPT_CRASH_RESIDUAL_LISTENER=False`;
- `CRASH_CONTAINMENT=PASS`;
- final result: `R37_HARDENED_CRASH_CONTAINMENT_PASS`;
- workflow-owned cleanup: PASS.

Repository evidence:
`security/native-admission/evidence/byedpi-r37/hardened-abrupt-service-crash-pass.txt`.

## Decision

The R37 Job Object hardening **remediates the previously confirmed orphan process/listener defect in the tested installed LocalSystem + authenticated IPC boundary**.

`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` is now a required security invariant for every native process accepted by the single audited launcher.

Production native engine admission remains **BLOCK**.

## Remaining gates

R37 does not yet prove:

- a second independent interactive Windows SID cannot steal or stop an active engine session;
- reboot/startup recovery for an admitted installed engine;
- production package/update/rollback admission for ByeDPI;
- system-wide transparent routing;
- WinDivert safety;
- production engine admission.

The next logical evidence gate is the real second-SID/multi-user lease test, followed by reboot/recovery and a scoped admission decision for the explicit-proxy engine path.
