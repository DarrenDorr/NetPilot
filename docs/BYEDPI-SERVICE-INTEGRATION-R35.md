# R35 — ByeDPI service integration scaffold

## Status

**IMPLEMENTED SCAFFOLD / PRODUCTION ADMISSION BLOCK / IPC NOT YET EXPOSED**

Base checkpoint: R34 `42a7f467bf822f76f0a67a3b31d3c7b8174f3648`.

R35 deliberately starts before the manual R34 functional runtime workflow is accepted as PASS. It prepares the LocalSystem service composition without authorizing ByeDPI execution.

## What R35 adds

- `IWindowsByeDpiRuntimePlanProvider` — zero-input provider contract for a fully verified and compiled ByeDPI plan.
- `NoAdmittedWindowsByeDpiRuntimePlanProvider` — production default that always rejects plan creation.
- `WindowsByeDpiRuntimeSession` — service-facing status/start/stop wrapper over the bounded `WindowsByeDpiServiceOwnedProcessController`.
- Production DI registration in `NetPilot.Service`.
- Regression tests proving status/stop remain safe while start fails closed.
- `scripts/security/r35-service-integration-boundary-check.sh`.

## Two independent production execution locks

R35 intentionally keeps two independent blockers in the production service composition:

1. `IWindowsVerifiedEngineProcessLauncher` resolves to `NoAdmittedWindowsVerifiedEngineProcessLauncher`.
2. `IWindowsByeDpiRuntimePlanProvider` resolves to `NoAdmittedWindowsByeDpiRuntimePlanProvider`.

Therefore an accidental call to the R35 runtime session cannot create a native process even if one of the two boundaries is incorrectly reached.

## IPC boundary

R35 scaffold does **not** add ByeDPI IPC commands yet. `PrivilegedIpcWorker` remains unchanged for ByeDPI.

The authenticated IPC start/stop/status surface is a later R35 sub-gate after:

- manual R34 functional runtime PASS;
- evidence review;
- explicit decision to admit the exact lab-tested runtime chain for a scoped service integration test.

When IPC is added, it must reuse the existing authenticated Windows SID model and global engine lease. It must not accept raw command line, paths, URLs, environment values, bind addresses or arbitrary ports from the GUI/client.

## What this does not prove

R35 scaffold does not prove:

- R34 runtime PASS;
- execution under installed LocalSystem `NetPilot.Service`;
- authenticated IPC control of a real ByeDPI process;
- multi-user engine ownership with a real native process;
- reboot/crash recovery of an admitted service runtime;
- transparent routing;
- WinDivert safety;
- production engine admission.

## Promotion rule

Do not replace either no-admit production registration until the corresponding evidence gate has passed and the exact binary/package/plan identities are pinned in an explicit admission decision.
