# R42.11.1 — ByeDPI verified staging to atomic promotion

## Purpose

R42.11.1 completes the package-placement half of the verified acquisition chain without opening native runtime admission.

It connects an R42.11 verified protected staging transaction to the already-reviewed R42.3 atomic package promotion boundary. The bridge does not implement its own filesystem mutation and accepts no raw managed target path.

## Parent boundary

R42.11 checkpoint:

`6ed8736504bb2b675c67c04b19dafd5b34a9be51`

R42.11 already proves the downloaded immutable release bundle can be transferred into the existing protected staging transaction while retaining exact manifest verification, cleanup/recovery and `NATIVE_EXECUTION=NOT_IMPLEMENTED`.

## R42.11.1 bridge behavior

`WindowsByeDpiVerifiedStagingPromoter`:

1. accepts an R42.10 verified bundle plus an R42.11 staging result;
2. permits only typed `Install` or `Upgrade` lifecycle operations;
3. re-validates the immutable release descriptor;
4. re-checks exact manifest-document size and SHA-256;
5. re-parses the manifest document;
6. verifies the re-parsed manifest still matches the bundle descriptor/manifest;
7. requires the staging result manifest digest and verified file count to match;
8. obtains a `WindowsByeDpiVerifiedPromotionSource` only through `WindowsByeDpiPromotionSourceVerifier`;
9. invokes atomic placement only through `WindowsByeDpiAtomicPackagePromoter`.

The bridge itself contains no `File`, `Directory`, `Path`, `FileStream`, ZIP extraction, HTTP client, Service/IPC or process-launch primitive.

## Inherited R42.3 guarantees

The existing source verifier and atomic promoter retain responsibility for:

- exact staging transaction-id and fixed staging root reconstruction;
- exact Verified journal validation;
- exact staged file-tree re-verification;
- protected ACL/owner evidence in production;
- same-volume requirement before atomic move;
- lifecycle-policy evaluation;
- active engine lease blocking package mutation;
- immutable managed version roots;
- no overwrite of an existing immutable target;
- retained previous version during upgrade;
- crash-recovery journal/state transitions;
- target-free rollback through the separate existing API.

R42.11.1 intentionally does not expose rollback through the acquisition/promotion bridge. Repair, rollback and uninstall remain separate maintenance operations under their existing reviewed APIs.

## Focused integration tests

Synthetic Windows tests cover the complete managed package path without executing native code:

- verified fake-handler download → protected staging → atomic Install;
- verified Install followed by verified Upgrade with retained previous version;
- active engine lease blocks promotion and preserves the verified staging tree;
- post-staging file mutation is rejected by the inherited promotion-source re-verifier before atomic move;
- Repair/Rollback/Uninstall are rejected by the narrow bridge.

The synthetic `bin/ciadpi.exe` fixture is plain non-executable test data. CI additionally checks that no `ciadpi` process appears.

## Deliberately excluded

- live network-handler construction;
- arbitrary filesystem mutation;
- a second promotion implementation;
- raw target paths;
- acquisition-driven repair;
- acquisition-driven rollback;
- acquisition-driven uninstall;
- Service/IPC product wiring;
- installer integration;
- native ByeDPI execution;
- WinDivert execution;
- production runtime admission.

## Security position

`VERIFIED_PROMOTION=EXISTING_R42_3_BOUNDARY_ONLY`

`SERVICE_IPC_WIRING=ABSENT`

`NATIVE_EXECUTION=NOT_IMPLEMENTED`

`PRODUCTION_ADMISSION=BLOCK`

A successfully promoted managed package is still only a verified package at rest. It is not a native-execution authorization and does not change the production admission policy.
