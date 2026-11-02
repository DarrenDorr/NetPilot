# R42.10 — ByeDPI verified downloader boundary

R42.10 composes the already isolated R42.9 HTTP transport with the R42.7 acquisition trust policy and returns a verified in-memory release bundle. It is still a fail-closed pre-staging gate.

## Included

- exact immutable release metadata retrieval through `ByeDpiIsolatedHttpTransport`;
- R42.7 plan creation and anti-downgrade enforcement;
- exactly three immutable release assets: payload ZIP, package manifest and provenance document;
- transport-level exact length + SHA-256 verification inherited from R42.9;
- independent downloader-level exact length + SHA-256 re-verification;
- fixed 64 MiB aggregate asset cap inherited from R42.7;
- strict package manifest parsing;
- exact manifest `engineId` / version / architecture match against the immutable release descriptor;
- defensive in-memory bundle copies;
- fake/in-memory focused tests only.

## Explicitly excluded

R42.10 does **not** introduce:

- construction of a live network handler;
- filesystem writes or temporary download files;
- ZIP extraction;
- protected staging invocation;
- package promotion, rollback, repair or uninstall wiring;
- Service/IPC integration;
- installer integration;
- native ByeDPI execution;
- WinDivert execution;
- production runtime admission.

The returned `ByeDpiVerifiedReleaseBundle` is data only. It is not a staging token, admission token, engine lease or execution authorization.

## Next gate

R42.11 may connect this exact verified byte bundle to the already-reviewed protected staging transaction, but only through a separate reviewed boundary with deterministic cleanup/recovery and with native execution still blocked.

`VERIFIED_BUNDLE=IN_MEMORY_ONLY`

`FILESYSTEM_WRITE=NOT_IMPLEMENTED`

`STAGING_INVOCATION=NOT_IMPLEMENTED`

`SERVICE_IPC_WIRING=ABSENT`

`NATIVE_EXECUTION=NOT_IMPLEMENTED`

`PRODUCTION_ADMISSION=BLOCK`
