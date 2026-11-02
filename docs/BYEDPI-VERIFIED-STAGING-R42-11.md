# R42.11 — ByeDPI verified bundle to protected staging

## Purpose

R42.11 connects the already verified R42.10 in-memory ByeDPI release bundle to the already reviewed R42.2 protected Windows staging transaction.

The gate is intentionally narrow. It does not add another extractor, arbitrary filesystem writer, package promotion, Service/IPC wiring, installer integration, native execution, WinDivert execution or production admission.

## Parent boundary

R42.10 checkpoint:

`462064d565f7707df320a5d9a73253b8e9006ddb`

R42.10 already requires exact immutable release metadata, exactly three expected assets, transport size/SHA-256 verification, downloader re-verification, strict package manifest parsing and anti-downgrade enforcement.

## R42.11 bridge behavior

`WindowsByeDpiVerifiedBundleStager` performs a final fail-closed check before filesystem mutation:

1. re-validates the immutable release descriptor;
2. obtains defensive copies of payload, manifest document and provenance bytes;
3. re-verifies exact size and SHA-256 for all three immutable assets;
4. re-checks aggregate byte identity and the fixed acquisition cap;
5. re-parses the downloaded package manifest document;
6. confirms the re-parsed manifest identity and file list still match the bundle manifest;
7. wraps the verified payload ZIP in a read-only memory stream;
8. calls only `WindowsByeDpiProtectedStagingTransaction.StageAndVerifyAsync` for production staging.

The bridge contains no `File`, `Directory`, `Path`, `FileStream`, `SafeZipExtractor`, `HttpClient`, process, service or IPC operations.

## Inherited staging guarantees

R42.11 deliberately reuses the R42.2 transaction rather than reimplementing it. Therefore staging keeps the existing protections:

- fixed `%ProgramFiles%\\NetPilot\\Engines\\Staging\\ByeDPI` production boundary;
- LocalSystem requirement for production mutation;
- protected service-only ACLs;
- safe ZIP extraction;
- exact package manifest verification after extraction;
- reparse-point/path-boundary checks;
- path-free recovery journal;
- deterministic cleanup after failed extraction or verification;
- verified staging transaction result without package promotion.

## Focused integration tests

The R42.11 Windows tests use an in-memory fake HTTP handler and synthetic non-PE package bytes. They validate:

- a full R42.10 verified-download → R42.11 staging chain;
- defensive bundle export copies cannot mutate the bytes later staged;
- a ZIP whose outer immutable asset hash is valid but whose extracted tree does not match the package manifest is rejected by the inherited staging verifier;
- failed tree verification removes transaction and journal residue;
- aggregate bundle identities still match the immutable release descriptor.

No live network request is required by these tests and no `ciadpi.exe` process is launched.

## Deliberately excluded

- live handler construction;
- arbitrary filesystem writes;
- a second ZIP extractor;
- staging outside the existing protected transaction;
- promotion into `Engines\\Managed`;
- current-version activation;
- repair/rollback/uninstall orchestration from the downloader path;
- Service/IPC wiring;
- installer integration;
- native ByeDPI execution;
- WinDivert execution;
- production admission.

## Security position

`VERIFIED_STAGING=EXISTING_PROTECTED_TRANSACTION_ONLY`

`PACKAGE_PROMOTION=NOT_IMPLEMENTED`

`SERVICE_IPC_WIRING=ABSENT`

`NATIVE_EXECUTION=NOT_IMPLEMENTED`

`PRODUCTION_ADMISSION=BLOCK`

A verified staging tree remains data under a protected transaction boundary. It is not an execution authorization or production-admission token.
