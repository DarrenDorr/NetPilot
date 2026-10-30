# ByeDPI R42.3 — atomic promotion and rollback

R42.3 extends the already verified R42.2 staging boundary without changing production runtime admission.

## What is implemented

- A staged ByeDPI transaction is revalidated from the fixed R42.2 journal, exact manifest, exact file tree and Windows ACL/owner evidence before it can become a promotion source.
- Install and upgrade use a same-volume directory rename from `%ProgramFiles%\NetPilot\Engines\Staging\ByeDPI\<transactionId>` into the immutable managed layout `%ProgramFiles%\NetPilot\Engines\Managed\byedpi\<version>\x64`.
- Existing managed versions are never overwritten.
- The exact package manifest and protected ACL/owner boundary are verified again after the move and before protected package state is committed.
- Upgrade keeps the previous immutable package and records it as `retainedPreviousVersion`.
- Rollback accepts no caller-supplied target version or path. It can only swap to the exact retained previous version stored in protected state after re-verifying that package.
- Promotion and rollback are blocked while an engine lease is active.
- Protected state and recovery journals are written with same-volume atomic file replacement.
- Recovery derives filesystem targets from validated transaction IDs and canonical versions rather than journaled absolute paths.

## Deliberately not implemented in R42.3

- repair
- uninstall
- network acquisition/downloader wiring
- production Service/IPC commands
- installer payload integration
- native ByeDPI execution
- production runtime admission

## Security result

R42.3 remains `PRODUCTION_ADMISSION=BLOCK`. Passing this milestone means only that the service-owned package lifecycle can safely promote and roll back synthetic/verified package trees. It does not authorize `ciadpi.exe` to execute.
