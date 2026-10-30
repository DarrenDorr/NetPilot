# R42.4 — ByeDPI Repair + Uninstall

R42.4 extends the fail-closed package lifecycle after R42.3 atomic promotion/rollback. It still does **not** admit or execute ByeDPI.

## Repair

Repair is only for the exact currently managed version. The caller must provide a typed `WindowsByeDpiVerifiedPromotionSource` produced by the existing R42.2/R42.3 protected staging boundary. Before any move, R42.4 requires:

- no active engine lease;
- no pending R42.3 promotion transaction;
- no pending R42.4 maintenance transaction;
- same-volume staging, managed and maintenance roots;
- exact version match with current package state;
- exact staged manifest digest match with the persisted canonical manifest for that current version;
- a fresh exact staging tree verification; and
- protected staging ACL verification in production.

The current `x64` package directory is atomically moved to a service-owned repair backup. The verified staging directory is then atomically moved into the canonical managed location and reverified. Only after successful re-verification is the backup removed and the staging journal consumed.

Crash recovery is deterministic:

- before any move: abort and preserve staging;
- after current package was moved out: restore the trusted backup;
- after replacement moved in: verify the replacement; keep it only if exact verification passes, otherwise restore backup.

Repair never changes the current/previous version state.

## Uninstall

Uninstall accepts no path, version or target package. It requires no active engine lease and a valid current managed state. The whole fixed `Managed/byedpi` root is atomically moved, on the same volume, to `Engines/Maintenance/ByeDPI/UninstallTombstones/<transaction>/byedpi`.

The recovery journal is outside the moved managed tree. After the atomic move the tombstone is recursively deleted only after the tree has been checked for reparse points and, in production, protected ACL/owner state.

Crash recovery either aborts a pre-move uninstall or completes deletion of an already-moved tombstone. It never accepts a caller-supplied deletion path.

## Explicit non-goals

R42.4 does not add:

- Service/IPC commands;
- downloader/network acquisition;
- installer integration;
- native process launch;
- runtime profile wiring;
- production runtime admission.

`NATIVE_EXECUTION_TESTED=False`

`PRODUCTION_ADMISSION=BLOCK`
