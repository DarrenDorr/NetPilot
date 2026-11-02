# R42.13 — ByeDPI live acquisition / LocalSystem E2E lab gate

## Purpose

R42.13 is the first gate that is allowed to construct a real HTTP handler for the reviewed ByeDPI package path. It is intentionally lab-only and does not alter normal `NetPilot.Service` dependency injection or runtime admission.

The gate exists to prove the complete package path with the real immutable GitHub release:

`exact GitHub tag -> bounded metadata -> exact three assets -> SHA-256 verification -> protected staging -> atomic managed install`

The real reviewed `ciadpi.exe` may be downloaded and stored in the managed package tree, but it is not executed.

## Parent

R42.12.1 checkpoint:

`6c0fe041f8236b5cf472669864cf55f4a348a315`

R42.12.1 establishes the exact reviewed release catalog and safe draft/publication workflows. The live lab must not run successfully until `byedpi-v0.17.3` exists as a published immutable GitHub release and its exact three assets match the reviewed catalog.

## Hardened live handler

`ByeDpiLiveHttpHandlerFactory` creates `SocketsHttpHandler` with the following fixed behavior:

- `AllowAutoRedirect=false` — redirect handling remains visible to R42.9 transport policy;
- `UseCookies=false`;
- `UseProxy=false` — no ambient/system proxy credential or routing surface;
- no request credentials;
- `PreAuthenticate=false`;
- `AutomaticDecompression=None` — byte counts and hashes apply to the exact response body;
- connect timeout 10 seconds, within the R42.7 20-second total transport timeout;
- bounded connection pool lifetime/idle time and at most two connections per server;
- no custom connection callback;
- no plaintext stream filter;
- no activity-header propagation;
- no custom TLS certificate validation callback is installed; platform TLS validation remains in force.

Automatic redirects are disabled because the R42.7/R42.9 design permits only a direct asset response or exactly one manually validated GitHub release-asset redirect. .NET otherwise follows redirects automatically by default, which would hide the redirect boundary from the trust policy.

## Lab pipeline

`WindowsByeDpiLiveAcquisitionPipeline.ApplyReviewedPackageAsync` is the production-path composition used only by the R42.13 lab host. The public method:

1. obtains only `ByeDpiReviewedReleaseCatalog.Current`;
2. reads protected managed package state;
3. rejects an active engine lease before constructing any network handler;
4. returns a no-op when the exact reviewed version is already current, also before constructing a handler;
5. creates the hardened live handler;
6. uses `ByeDpiIsolatedHttpTransport` and `ByeDpiVerifiedReleaseDownloader`;
7. invokes the existing R42.11 protected staging bridge;
8. invokes the existing R42.11.1 atomic promotion bridge;
9. requires the resulting current version to equal the exact reviewed catalog version.

The synthetic test seam accepts a test descriptor and test staging/managed roots, but it is internal and is not used by the live lab or Service.

## Synthetic preflight

Normal push/PR CI does not require a published release and does not perform live product acquisition. It verifies:

- hardened handler configuration;
- full solution compilation;
- the R42.13 lab-host project compiles;
- synthetic fake-handler Install path;
- exact-current no-op before a second handler is created;
- active engine lease rejection before any handler is created;
- synthetic Upgrade with retained previous version;
- production `NetPilot.Service` still references `NoAdmittedWindowsByeDpiPackageProvisioner` and does not construct the live handler/pipeline.

## Manual live LocalSystem lab

The live workflow is `workflow_dispatch` only. Before creating any lab state it requires the exact GitHub release to be:

- tag `byedpi-v0.17.3`;
- `draft=false`;
- `prerelease=false`;
- `immutable=true`;
- exactly three assets with the reviewed names, sizes and GitHub SHA-256 digests.

The Windows hosted runner must start with no `%ProgramFiles%\NetPilot` tree and no `ciadpi` process. The workflow creates a run-specific ownership marker, publishes the isolated lab host, and runs that host as `LocalSystem` using a one-shot scheduled task.

The LocalSystem host then performs the real live acquisition and production staging/promotion APIs. It requires:

- LocalSystem SID `S-1-5-18`;
- matching lab ownership marker;
- empty managed ByeDPI state;
- successful exact-tag acquisition;
- successful verified downloader;
- successful protected staging;
- successful atomic Install;
- protected managed state `0.17.3`;
- managed `bin/ciadpi.exe` exactly 495,685 bytes with SHA-256 `866e99c3c7690326cc6e365fe02980f06ae2b394c045555674fb95db52b0dbdd`;
- managed `LICENSE` exactly 1,063 bytes with SHA-256 `18c854c6c25dffefe4b522cf1bdf5d15278a25ca785948258bddb94393f575eb`;
- no `ciadpi` process before or after the acquisition.

After evidence is captured, the workflow deletes only the lab-owned Program Files tree whose ownership marker still matches the current run.

## Production remains blocked

R42.13 does not register `WindowsByeDpiLiveAcquisitionPipeline` or `ByeDpiLiveHttpHandlerFactory` in normal `NetPilot.Service`.

Production remains protected by all prior blockers:

- `OperationPolicy.CreateNoAdmittedEnginePolicy()` does not allow package install;
- `NoAdmittedWindowsByeDpiPackageProvisioner` remains registered;
- no-admit native process launcher remains registered;
- no-admit runtime plan provider remains registered;
- ByeDPI package apply IPC remains fail-closed;
- no native engine execution is performed by this gate;
- WinDivert remains outside this gate.

## Current external blocker

At the time R42.13 code is prepared, `byedpi-v0.17.3` is not yet publicly published. Therefore the manual live workflow is expected to refuse execution until the R42.12.1 publication sequence has completed and the immutable-release verifier is green.

This is intentional. A synthetic or mutable substitute must not be treated as live E2E evidence.

## Security position

`LIVE_HANDLER=LAB_ONLY`

`LIVE_E2E=BLOCKED_UNTIL_PUBLISHED_IMMUTABLE_RELEASE`

`SERVICE_PRODUCTION_WIRING=ABSENT`

`PACKAGE_MUTATION_IN_PRODUCTION=BLOCK`

`NATIVE_EXECUTION=NOT_IMPLEMENTED`

`PRODUCTION_ADMISSION=BLOCK`
