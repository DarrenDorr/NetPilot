# R37 — ByeDPI abrupt LocalSystem service crash containment

## Status

**CRASH FINDING CONFIRMED / JOB OBJECT HARDENING IMPLEMENTED / PREFLIGHT PENDING / PRODUCTION ADMISSION BLOCK**

Base evidence: R36 installed LocalSystem + authenticated IPC PASS.

## Observation — finding confirmed

Pinned observation run `41007700773` used the exact validated R36 product checkpoint and exact R34-tested ByeDPI binary.

The lab:

1. staged the isolated R36 service and exact candidate under protected Program Files;
2. registered/started `NetPilot.R36.Lab` as LocalSystem;
3. enrolled/authenticated the interactive client through IPC v4;
4. started ByeDPI through typed IPC;
5. proved `ciadpi.exe` was a direct child of the LocalSystem service process and owned by SYSTEM;
6. terminated the service process abruptly with `Stop-Process -Force`;
7. observed the engine process/listener after the service process had exited.

Result:

- `ABRUPT_CRASH_RESIDUAL_CIADPI=True`;
- `ABRUPT_CRASH_RESIDUAL_LISTENER=True`;
- `CRASH_CONTAINMENT=NEEDS_HARDENING`;
- `RESULT=R37_CRASH_CONTAINMENT_FINDING`.

Evidence artifact:
- id `12956536478`;
- name `r37-byedpi-abrupt-service-crash-observation`;
- ZIP SHA-256 `b1a862edef4228148af16b7227ee38918c1ece475dd7bfdf3b95ff8959a40fec`.

Repository evidence:
`security/native-admission/evidence/byedpi-r37/abrupt-service-crash-observation.txt`.

This proves graceful `Stop-Service` cleanup from R36 is not sufficient for crash safety.

## Hardening implementation

R37 adds `WindowsKillOnCloseJob` in the Windows platform boundary.

The containment contract uses a Windows Job Object configured with:

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

Because the Job Object handle is owned by the service process, abrupt termination of that service should cause Windows itself to close the handle and terminate assigned engine processes, without relying on managed `Dispose`/host shutdown.

## Tests and policy gates

Added:

- `tests/NetPilot.Platform.Windows.Tests/WindowsKillOnCloseJobTests.cs` — attaches a real harmless Windows process to the job and requires job disposal to terminate it;
- `scripts/security/r37-crash-containment-boundary-check.sh` — enforces:
  - production Service remains no-admit;
  - only one `Process.Start` exists in the audited launcher;
  - launcher requires `WindowsKillOnCloseJob.Attach(process)`;
  - containment failures map to `ProcessContainmentFailed`;
  - uncontained process cleanup exists;
  - exact kill-on-close constant/API boundary remains present;
  - the Job Object helper cannot become another process-launch surface.

## Required next gates

Before the finding can be considered remediated:

1. full security/build preflight must pass on the exact R37 hardening checkpoint;
2. Windows unit test must prove kill-on-close behavior;
3. the isolated installed LocalSystem crash lab must be repeated with the hardened code;
4. after forced service termination the lab must require:
   - `RESIDUAL_CIADPI=False`;
   - `RESIDUAL_LISTENER=False`;
   - `CRASH_CONTAINMENT=PASS`.

Even a successful R37 remediation does not grant production engine admission. Multi-user second-SID evidence, reboot/recovery evidence and the separate transparent-routing/WinDivert decision remain later gates.
