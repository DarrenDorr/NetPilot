# R42 — ByeDPI production package lifecycle design

## Status

**NON-EXECUTING POLICY IMPLEMENTED / FILESYSTEM MUTATOR NOT IMPLEMENTED / PRODUCTION ADMISSION BLOCK**

Base: R41.1 release metadata/licensing hygiene PASS while production native admission remains BLOCK.

R42 defines the package lifecycle before any production package installer/updater is allowed to exist. It intentionally adds no network fetcher, no filesystem mutator, no IPC operation and no native process execution.

## Why a separate ByeDPI lifecycle

The existing `ZapretTransactionalInstaller` is useful evidence for transaction patterns, but ByeDPI has a different immutable package identity and should not inherit Zapret-specific release/network assumptions by accident.

R42 therefore introduces a pure policy boundary first:

`src/NetPilot.Updater/ByeDpiPackageLifecyclePolicy.cs`

The policy accepts only typed state facts. It accepts **no path, URL, shell command or executable arguments**.

## Allowed lifecycle operations

### Install

Allowed only when:

- no current ByeDPI package is installed;
- target version is canonical;
- target package has already passed exact verification;
- staging is protected/service-owned;
- staging and final root are on the same volume;
- no ByeDPI engine lease is active.

### Upgrade

In addition to the package boundary above:

- a current version must exist;
- target must be strictly newer;
- equal-version replacement is not an upgrade;
- arbitrary downgrade is rejected.

### Repair

- current version must exist;
- target version must be exactly the current version;
- replacement bytes must independently pass the same verification boundary;
- repair cannot be used to smuggle a version transition.

### Rollback

- current version must exist;
- exactly one previously verified version may be retained;
- rollback target must equal that retained previous version;
- arbitrary historical downgrade is not accepted.

### Uninstall

- current version must exist;
- uninstall accepts no target package/payload;
- active engine lease still blocks the operation.

## Active lease rule

R42 chooses the stricter product behavior:

> Package mutation never silently stops a user's active engine session.

If an engine lease is active, install/upgrade/repair/rollback/uninstall all fail closed with `BlockActiveEngineLease`. A future UI may tell the owning user to stop the engine first, but the package manager itself does not steal the lease or auto-stop it.

## Planned protected filesystem layout

Final immutable package root remains compatible with the existing protected resolver:

```text
%ProgramFiles%\NetPilot\Engines\Managed\byedpi\<version>\x64\
  bin\ciadpi.exe
  ... exact admitted package files only ...
```

Rules:

- no mutable `current` symlink/junction;
- no reparse points inside package/staging trees;
- no user-chosen base directory;
- no user-writable staging;
- package promotion must be same-volume so the implementation can use an atomic directory transition rather than copy-while-active;
- exact package hash and exact per-file manifest are required before promotion and rechecked after promotion;
- applicable license notice travels with the package metadata.

R42 does **not** implement those filesystem mutations yet; it specifies the contract the implementation must satisfy.

## Future acquisition boundary

A future downloader/provisioner must not accept a raw URL from GUI/IPC. Package bytes must be selected from a release-specific trusted manifest with an exact asset hash and an allowlisted redirect/source policy.

Engine start must never perform a network download implicitly.

R42 does not implement network acquisition.

## Interrupted transaction model

The next implementation gate must include:

1. one engine-specific mutation lock;
2. protected staging transaction id owned by the service;
3. full verification before promotion;
4. atomic same-volume promotion;
5. post-promotion verification;
6. one retained verified previous version;
7. crash/interruption journal sufficient to distinguish staged, promoted and rollback-pending state;
8. deterministic recovery that never treats an unverified directory as active.

## Tests

`tests/NetPilot.Core.Tests/ByeDpiPackageLifecyclePolicyTests.cs` covers:

- every operation blocked under active lease;
- verified/protected/same-volume install requirements;
- upgrade monotonicity;
- exact-version repair;
- exact retained-version rollback;
- uninstall without payload;
- rejection of non-canonical target versions.

These are pure managed tests and do not execute ByeDPI.

## Security boundary retained

R42 must keep all of the following true:

- normal `NetPilot.Service` uses no-admit runtime components;
- production executable/package catalogs remain empty;
- installer does not ship `ciadpi.exe`;
- no new ByeDPI IPC package-management operation exists;
- no new ByeDPI network updater exists;
- no transparent Windows routing or WinDivert authorization exists;
- production admission remains `BLOCK`.

## Next gate

R42.1 should implement **protected package staging + exact package manifest verification only**, still without service/IPC wiring or native execution. After that, a separate transaction/recovery gate can implement mutation semantics and exercise them on isolated synthetic packages before any real ByeDPI production package is admitted.
