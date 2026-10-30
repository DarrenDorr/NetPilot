# ByeDPI R42.6 — Immutable Distribution Release Boundary

## Decision

R42.6 defines the immutable distribution identity that must exist before any future product downloader is allowed.

This milestone does **not** publish a release, download package bytes, wire Service/IPC, modify the installer, execute ByeDPI, or change production admission.

`PRODUCTION_ADMISSION=BLOCK`

## Parent evidence

R42.6 is built directly on R42.5 checkpoint:

`dc28fc4f0b635c34c63bbaed99ea7658d8a9c827`

R42.5 already pins the reviewed upstream source, reviewed patchset, deterministic Windows binary, LICENSE, deterministic package ZIP, manifest document and provenance document.

## Immutable release identity

The only accepted distribution identity in this gate is:

- repository: `DarrenDorr/NetPilot`;
- engine: `byedpi`;
- architecture: `x64`;
- version: `0.17.3`;
- canonical tag: `byedpi-v0.17.3`;
- reviewed upstream commit: `7efde1b1296eaaa187b70e951894dde17527489c`;
- reviewed upstream tree: `8d13f6f5939190cd27ee68696247073a00b3e7dc`.

The release descriptor contains exactly three immutable assets:

1. `NetPilot-ByeDPI-0.17.3-x64.zip`
2. `NetPilot-ByeDPI-0.17.3-x64.manifest.json`
3. `NetPilot-ByeDPI-0.17.3-x64.provenance.json`

Each asset requires an exact positive byte size and lowercase SHA-256 identity.

## Fail-closed rules

`ByeDpiImmutableReleaseDocument` rejects:

- unknown fields;
- duplicate fields;
- repository owner/name drift;
- engine or architecture drift;
- non-canonical `major.minor.patch` versions;
- version/tag mismatch;
- source commit/tree identities that are not exact lowercase 40-hex values;
- unexpected asset names;
- non-positive asset sizes;
- non-canonical SHA-256 values;
- duplicate SHA-256 identities between the three assets.

The descriptor is deterministic: serializing the same trusted descriptor twice must produce identical bytes.

## Security boundary

R42.6 deliberately contains no HTTP client, release discovery, GitHub API client, downloader, upload/publish action, filesystem package mutation, process launch, WinDivert authorization, Service/IPC wiring or installer integration.

The future acquisition layer must consume a separately reviewed immutable release identity rather than trusting mutable “latest” metadata, arbitrary asset names, caller-supplied URLs or unpinned release contents.

A GitHub tag or release page by itself is not sufficient trust evidence. A later acquisition gate must independently verify the exact descriptor and downloaded bytes before handing anything to the already-protected staging boundary.

## Next gate

The next safe milestone is R42.7: a **read-only acquisition/trust policy** for an immutable release descriptor. It should define allowed origin, redirect policy, transport limits, exact asset selection, digest verification and anti-downgrade behavior before implementing any real network downloader.

Until that gate is separately reviewed and proven:

- `RELEASE_CREATION_OR_UPLOAD=NOT_IMPLEMENTED`
- `PRODUCT_DOWNLOADER=NOT_IMPLEMENTED`
- `NETWORK_ACQUISITION=NOT_IMPLEMENTED`
- `NATIVE_EXECUTION_TESTED=False`
- `PRODUCTION_ADMISSION=BLOCK`
