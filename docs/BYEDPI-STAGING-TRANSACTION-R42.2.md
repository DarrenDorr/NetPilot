# R42.2 — protected ByeDPI staging transaction + interruption recovery

## Decision

**IMPLEMENTED AS AN ISOLATED PACKAGE-LIFECYCLE GATE / PRODUCTION ADMISSION REMAINS BLOCK**

R42.2 adds the first mutating ByeDPI package-lifecycle boundary, but it still does not install a production package, wire the engine into `NetPilot.Service`, fetch anything from the network, promote a package into `Managed`, or execute native code.

Parent gates remain authoritative:

- R41: production readiness `NOT_READY / BLOCK`;
- R42: installer-bundled immutable package model;
- R42.1: exact manifest + read-only staging tree + Windows ACL/owner verification.

Machine-readable contract:
`security/native-admission/byedpi-staging-transaction-r42-2.yaml`

## What R42.2 adds

### Fixed LocalSystem production boundary

The production API has no caller-supplied base path. It resolves only:

```text
%ProgramFiles%\NetPilot\Engines\Staging\ByeDPI\<32-lowercase-hex-transaction-id>
```

The mutating production path requires Windows + LocalSystem. `Staging`, `ByeDPI`, `Journal` and each transaction root are protected with the existing service-only ACL model: LocalSystem + BUILTIN\Administrators, inheritance removed for the protected roots.

### Reuse of reviewed ZIP extraction

R42.2 deliberately does **not** add another archive parser. It calls the existing `SafeZipExtractor`, which already rejects:

- absolute paths and drive syntax;
- `..` traversal;
- symlink/reparse entries;
- duplicate/case-collision destinations;
- excessive archive entries;
- excessive single/total extracted size;
- excessive compression ratio.

After extraction the transaction re-runs the exact R42.1 manifest verifier and, in the real production boundary, the R42.1 Windows ACL/owner verifier.

### Path-free recovery journal

Journal location:

```text
%ProgramFiles%\NetPilot\Engines\Staging\ByeDPI\Journal\<transaction-id>.json
```

Journal schema v1 stores only:

- `schemaVersion`;
- `transactionId`;
- `manifestSha256`;
- `version`;
- `phase`;
- `updatedUtc`.

It does not store a payload/staging/absolute path, URL, command line, PID or native arguments. Recovery therefore cannot be redirected by editing a JSON path value: filesystem targets are always recomputed from a validated 32-character lowercase-hex transaction id and the fixed Program Files root.

Journal writes use a temporary file, `WriteThrough` + `Flush(true)`, followed by a same-directory replacement move. Strict recovery rejects unknown/duplicate fields, malformed IDs/digests/versions/timestamps and unknown phases.

## State machine

### `Created`

A transaction has been reserved in the journal but extraction has not yet completed. Recovery removes only the exact derived transaction tree, if present, then removes that exact journal.

### `Extracting`

The payload may be partial. Recovery performs a non-following reparse-point walk, removes only the exact derived transaction tree, then removes the exact journal.

### `Verified`

Extraction + exact manifest verification completed. In the real production boundary, Windows ACL/owner verification also completed. Recovery preserves this staging tree for a later promotion gate.

R42.2 does **not** treat `Verified` as `ADMIT` or as an execution token.

## Orphan and interrupted-write handling

- a valid transaction directory with no journal is removed as an orphan;
- an interrupted journal temp file is removed only if its filename matches the exact generated `<txid>.<guid-N>.tmp` shape;
- malformed journal names fail closed;
- unexpected files/directories at the ByeDPI staging root fail closed;
- any reparse point encountered during cleanup fails closed;
- recursive cleanup never uses an unrestricted `Directory.Delete(..., recursive: true)` production path.

## Synthetic tests

`tests/NetPilot.Platform.Windows.Tests/WindowsByeDpiProtectedStagingTransactionTests.cs` uses only synthetic non-executable bytes and covers:

1. successful extraction + exact manifest verification + `Verified` journal preservation;
2. deterministic cleanup of an `Extracting` journal without trusting a path from JSON;
3. manifest hash failure with transaction/journal cleanup;
4. ZIP traversal rejection with no transaction residue.

The test seam accepts a temporary root only as an `internal` API exposed solely to `NetPilot.Platform.Windows.Tests`. The production public API remains fixed to Program Files and LocalSystem.

## Explicitly not implemented in R42.2

- production Service/IPC wiring;
- downloader/network updater;
- package promotion into `Engines\Managed`;
- current/previous version pointer or rollback slot;
- code-signing trust decision;
- native engine launch;
- system proxy mutation;
- transparent routing;
- WinDivert;
- positive production admission catalogs.

## Next proof before R42.3

A Windows hosted lab should run the exact R42.2 checkpoint as LocalSystem against a workflow-owned Program Files tree and prove:

- service-only ACLs on journal/transaction objects;
- exact synthetic ZIP extraction and manifest/ACL verification;
- journal state `Verified` after success;
- deterministic recovery of a genuinely interrupted transaction host;
- no process launch, no network fetch, no Service/IPC product wiring;
- complete cleanup of the workflow-owned staging tree.

Only after that evidence should R42.3 implement **same-volume atomic promotion + rollback**, still without opening native production execution.

## Fail-closed markers

Static guard:

```text
scripts/security/r42-2-byedpi-staging-transaction-check.sh
```

Expected markers:

```text
R42_2_PARENT_R42_1_GUARD=PASS
R42_2_TRANSACTION_BOUNDARY=PASS
R42_2_PATH_FREE_JOURNAL=PASS
R42_2_NO_PROCESS_OR_NETWORK_PRIMITIVES=PASS
R42_2_RECOVERY_BOUNDARY=PASS
R42_2_SYNTHETIC_TEST_SURFACE=PASS
R42_2_SERVICE_IPC_WIRING=ABSENT
R42_2_PROMOTION=NOT_IMPLEMENTED
R42_2_NATIVE_EXECUTION_TESTED=False
R42_2_PRODUCTION_ADMISSION=BLOCK
RESULT=R42_2_STAGING_TRANSACTION_PASS_BLOCK
```
