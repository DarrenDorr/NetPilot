# R37 — ByeDPI abrupt LocalSystem service crash containment

## Status

**OBSERVATION GATE PREPARED / NO PRODUCT HARDENING YET / PRODUCTION ADMISSION BLOCK**

Base evidence: R36 installed LocalSystem + authenticated IPC PASS.

R37 first measures the current behavior when the LocalSystem service process is terminated abruptly while the service-owned ByeDPI process is active. The observation gate intentionally makes no production-code changes before evidence exists.

## Question under test

Does the current `SystemZapretProcessLauncher` / `WindowsByeDpiServiceOwnedProcessController` lifecycle guarantee that `ciadpi.exe` and its loopback listener disappear when the owning Windows service process is terminated without graceful host disposal?

## Observation design

The isolated hosted Windows lab will:

1. use the exact R36 product-code checkpoint and exact R34-tested ByeDPI binary hash;
2. stage the separate R36 lab service and candidate under protected Program Files;
3. register/start `NetPilot.R36.Lab` as LocalSystem;
4. enroll/authenticate the interactive client through IPC v4;
5. start ByeDPI through typed IPC and verify service parent/SYSTEM ownership;
6. terminate the LocalSystem service process abruptly with `Stop-Process -Force`;
7. observe whether `ciadpi.exe` and port 39036 remain after the service process is gone;
8. record either `CRASH_CONTAINMENT=PASS` or `CRASH_CONTAINMENT=NEEDS_HARDENING`;
9. force-clean only workflow-owned residue and remove the workflow-owned service/tree.

The workflow remains successful if it safely captures a residual-process finding. A finding is not a security PASS; it is evidence that R37 hardening is required.

## Expected next step if residual process exists

Design a Windows kill-on-owner-lifetime containment boundary, most likely a restricted Job Object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, while preserving the single audited native process-creation boundary and without adding a second raw process-launch path.

Any such hardening requires its own compile/security tests and a repeated abrupt-crash runtime gate.
