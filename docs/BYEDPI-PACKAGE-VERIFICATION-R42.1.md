# R42.1 — ByeDPI exact package manifest + protected staging verification

## Status

**READ-ONLY VERIFICATION IMPLEMENTED / SYNTHETIC WINDOWS LAB IMPLEMENTED / PRODUCTION ADMISSION BLOCK**

Base: R42 fail-closed package lifecycle policy.

R42.1 adds verification only. It does not add a downloader, production staging creator, installer, updater, rollback executor, Service/IPC operation or native process launch.

## Exact manifest boundary

Added:

`src/NetPilot.Updater/ByeDpiPackageManifestVerifier.cs`

The manifest is intentionally ByeDPI-specific rather than a generic user-defined package format. It requires:

- engine id exactly `byedpi`;
- architecture exactly `x64`;
- version exactly three numeric segments (`major.minor.patch`);
- executable exactly `bin/ciadpi.exe`;
- license notice exactly `LICENSE`;
- SHA-256 and exact byte size for every file;
- at most 64 files and existing global extracted-size limits.

Manifest paths reject:

- absolute paths;
- `..` and ambiguous segments;
- backslashes in canonical manifest paths;
- ADS/drive syntax (`:`);
- case collisions;
- Windows reserved device names;
- unsupported/control characters.

## Read-only staging verification

`ByeDpiPackageManifestVerifier.VerifyAsync`:

1. requires an existing staging root;
2. rejects a reparse-point root;
3. recursively enumerates one directory at a time so it never deliberately follows a discovered reparse point;
4. requires the actual file set to equal the manifest file set;
5. opens each file read-only with no write/delete sharing;
6. checks exact length;
7. computes SHA-256 from the opened file handle;
8. rechecks object type/reparse state after hashing;
9. returns a `ByeDpiVerifiedStagingTree` evidence object.

`ByeDpiVerifiedStagingTree` is **not** an execution/admission token. A separate Windows ACL/owner proof is still required, and production catalogs remain empty.

## Windows protected staging boundary

Added:

`src/NetPilot.Platform.Windows/WindowsByeDpiProtectedStagingSecurityVerifier.cs`

The verifier is read-only. It does not create directories or modify ACLs.

The transaction root is derived only as:

```text
%ProgramFiles%\NetPilot\Engines\Staging\ByeDPI\<transaction-id>
```

where transaction id is exactly 32 lowercase hex characters.

The public resolver does not accept a caller-provided base path. The ACL verifier checks the Program Files → transaction-root directory chain plus every exact manifest file and its parent directories. It reuses the established privileged-owner/untrusted-mutation-ACE rules from the immutable engine package security boundary and rejects reparse points.

## Synthetic hosted Windows lab

Added lab-only tool:

`tools/NetPilot.ByeDpiR42_1StagingLab`

Safety rules:

- refuses to run if `%ProgramFiles%\NetPilot` already exists;
- creates only workflow-owned synthetic text fixtures;
- the file named `ciadpi.exe` is deliberately text, not a PE/native executable;
- no ByeDPI binary is downloaded or launched;
- hardens the synthetic tree to privileged write ownership;
- verifies exact manifest + protected ACL positive case;
- requires fail-closed rejection after fixture byte drift;
- requires fail-closed rejection for an unmanifested extra file;
- deliberately grants ordinary Users `Modify` on staging and requires ACL rejection;
- removes only the workflow-owned synthetic `NetPilot` tree.

## Tests

Core tests cover:

- valid exact synthetic tree;
- extra file rejection;
- missing file rejection;
- same-length content/hash drift rejection;
- size mismatch rejection;
- traversal/absolute/backslash/ADS/device-name rejection;
- case-collision rejection;
- noncanonical version rejection.

The Windows hosted lab provides the real Program Files/ACL evidence separately.

## Security boundary retained

R42.1 does not authorize:

- production package acquisition;
- production staging creation/extraction;
- package promotion into `Managed`;
- installer inclusion of ByeDPI;
- Service/IPC package-management operations;
- native ByeDPI execution;
- transparent Windows routing;
- WinDivert;
- Android VPN/TUN.

Production native admission remains `BLOCK`.

## Next gate

R42.2 should implement the **transactional protected staging creator/extractor + interruption journal** against synthetic packages only. It must bind staging creation to a service-owned transaction id, use the existing safe ZIP extraction boundary, preserve same-volume constraints, and prove deterministic cleanup/recovery without Service/IPC wiring or native execution.
