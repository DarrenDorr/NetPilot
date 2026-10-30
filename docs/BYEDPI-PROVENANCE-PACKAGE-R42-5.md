# R42.5 — ByeDPI Build Provenance + Deterministic Package Contract

R42.5 deliberately does **not** add a product downloader. The goal is to establish a reproducible, reviewable package identity first, so a later acquisition layer has something narrow and trustworthy to consume.

## Reviewed source identity

The package builder accepts only the already-reviewed ByeDPI source identity used by the earlier isolated labs:

- repository: `hufrea/byedpi`
- commit: `7efde1b1296eaaa187b70e951894dde17527489c`
- tree: `8d13f6f5939190cd27ee68696247073a00b3e7dc`
- package version: `0.17.3`
- Windows x64 deterministic candidate SHA-256: `866e99c3c7690326cc6e365fe02980f06ae2b394c045555674fb95db52b0dbdd`
- LICENSE blob: `342905d38be83e9d540deae14987896826a74858`
- LICENSE SHA-256: `18c854c6c25dffefe4b522cf1bdf5d15278a25ca785948258bddb94393f575eb`

The exact seven reviewed NetPilot patches are also pinned by SHA-256 in the R42.5 security contract and package builder.

## Deterministic payload

The CI-only builder produces three files:

- `NetPilot-ByeDPI-0.17.3-x64.zip`
- `NetPilot-ByeDPI-0.17.3-x64.manifest.json`
- `NetPilot-ByeDPI-0.17.3-x64.provenance.json`

The payload ZIP contains only:

- `LICENSE`
- `bin/ciadpi.exe`

ZIP entry order, compression mode and timestamps are fixed. The manifest is external so its own digest never becomes a recursive payload dependency.

## Strict manifest document

`ByeDpiPackageManifestDocument` is the first reusable production-adjacent part of R42.5. It has no network or process APIs. It serializes and parses the existing in-memory `ByeDpiPackageManifest` with a strict schema.

It rejects:

- unknown JSON fields;
- duplicate fields;
- missing required fields;
- non-canonical lowercase SHA-256 values;
- unsupported schema versions;
- semantic manifest violations; and
- any mismatch between the persisted canonical digest and the digest reconstructed from the parsed manifest.

## Provenance evidence

R42.5 CI must build the package from the exact reviewed source/patchset and generate GitHub artifact attestation evidence for the package outputs. Artifact attestation links the artifact to repository/workflow/commit provenance; it is **additional supply-chain evidence, not a substitute for code review or runtime admission**.

Before any future product acquisition is allowed, the publication path must use an immutable release (or an equivalently reviewed immutable distribution boundary) and the product-side trust policy must be designed separately.

## Explicit non-goals

R42.5 does not add:

- product runtime downloading;
- release publication;
- Service/IPC commands;
- installer integration;
- native execution;
- production runtime admission.

`NATIVE_EXECUTION_TESTED=False`

`PRODUCTION_ADMISSION=BLOCK`
