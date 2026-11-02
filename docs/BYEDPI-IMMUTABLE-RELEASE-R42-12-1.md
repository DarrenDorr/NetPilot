# R42.12.1 — ByeDPI immutable release publication readiness

## Why this gate exists

R42.12 completed the fail-closed Service/IPC package boundary, but a real live acquisition test cannot yet be honest because the exact GitHub release required by R42.7 does not exist.

The reviewed tag is:

`byedpi-v0.17.3`

R42.12.1 turns the deterministic R42.5 package into a publication-ready, reproducible release boundary without automatically publishing a mutable release.

## Exact reviewed release identity

The first production descriptor is now centralized in `ByeDpiReviewedReleaseCatalog`.

Repository: `DarrenDorr/NetPilot`

Release tag: `byedpi-v0.17.3`

Release target checkpoint: `dc28fc4f0b635c34c63bbaed99ea7658d8a9c827`

Reviewed upstream commit: `7efde1b1296eaaa187b70e951894dde17527489c`

Reviewed upstream tree: `8d13f6f5939190cd27ee68696247073a00b3e7dc`

Exact assets:

1. `NetPilot-ByeDPI-0.17.3-x64.zip`
   - size: `496964`
   - SHA-256: `c0fe998e3d81ccb871f09d126e7b5ee0ab82d15daf78bc44bcd63b7762313db7`
2. `NetPilot-ByeDPI-0.17.3-x64.manifest.json`
   - size: `511`
   - SHA-256: `eaa6f063fcb8e012810f849ed08b6bd82d4d046d4796c2fb4b903c4195985f54`
3. `NetPilot-ByeDPI-0.17.3-x64.provenance.json`
   - size: `1726`
   - SHA-256: `e1f98760eed94f5daa7d37a868015755220cb17d389000bc49e031da511f7f31`

These are the exact bytes reproduced by the R42.5 deterministic package builder and independently recovered from the still-valid R42.5 evidence artifact before its retention expiry.

## Draft-only creation workflow

`.github/workflows/r42-12-1-create-draft-byedpi-release.yml` is deliberately `workflow_dispatch` only.

It:

1. checks out the exact R42.5 NetPilot checkpoint;
2. checks out the exact reviewed upstream ByeDPI commit/tree;
3. verifies the exact LICENSE blob/hash and all seven reviewed patch hashes;
4. applies the reviewed patches;
5. rebuilds the Windows candidate without executing it;
6. requires the exact known `ciadpi.exe` SHA-256 and size;
7. builds the deterministic package twice;
8. requires byte-identical outputs;
9. requires the exact three reviewed release asset names, sizes and SHA-256 values;
10. creates a GitHub release as a **draft only** with all three assets;
11. re-reads the draft and requires `draft=true`, `prerelease=false` and exactly three assets.

The workflow has no publish step.

## Why publishing is a separate manual gate

GitHub immutable releases protect the release tag and release assets only after publication. Repository release immutability must therefore be enabled before the reviewed draft is published. GitHub recommends attaching all assets to a draft first and publishing only after the draft is complete.

R42.12.1 intentionally does not try to enable repository administration settings from CI and does not publish automatically. This avoids creating a public mutable release if the repository setting is not yet enabled.

## Required publication sequence

After this branch is reviewed/merged:

1. Enable release immutability for `DarrenDorr/NetPilot` in the repository Releases settings if it is not already enabled.
2. Run **R42.12.1 Create Exact ByeDPI Draft Release** manually.
3. Inspect the draft and confirm exactly the three reviewed assets are present.
4. Publish the draft through GitHub while release immutability is enabled.
5. Run **R42.12.1 Verify Published Immutable ByeDPI Release**.
6. Do not proceed to live product acquisition unless that verification workflow is green.

## Post-publish verifier

`.github/workflows/r42-12-1-verify-immutable-byedpi-release.yml` is read-only and manual.

It requires:

- exact tag `byedpi-v0.17.3`;
- `draft=false`;
- `prerelease=false`;
- `immutable=true`;
- exactly three assets;
- each asset state `uploaded`;
- exact asset name/size/GitHub digest;
- downloaded bytes match the same exact size/SHA-256 values;
- GitHub immutable release verification succeeds.

Only that evidence closes the publication prerequisite for R42.13.

## No release/runtime conflation

Publishing this package does not mean the native engine is production-admitted.

R42.12 production Service remains fail-closed:

- package provisioning policy is blocked;
- the provisioner is still `NoAdmittedWindowsByeDpiPackageProvisioner`;
- live network acquisition is not wired;
- the ByeDPI process launcher and runtime-plan provider remain no-admit;
- native execution remains blocked.

## Security position

`DRAFT_PUBLICATION_READY=true`

`AUTOMATIC_PUBLISH=FORBIDDEN`

`PUBLISHED_IMMUTABLE_RELEASE=NOT_YET_VERIFIED`

`LIVE_NETWORK_PRODUCT_ACQUISITION=NOT_ADMITTED`

`NATIVE_EXECUTION=NOT_IMPLEMENTED`

`PRODUCTION_ADMISSION=BLOCK`
