# R42.12 — ByeDPI Service/IPC package boundary

## Purpose

R42.12 introduces the privileged IPC shape for ByeDPI package status and package provisioning without enabling live acquisition or native execution in production.

The gate deliberately separates package lifecycle from the already existing ByeDPI runtime `GetStatus/Start/Stop` commands. Applying a package is not equivalent to starting the engine and cannot change runtime admission.

## Parent

R42.11.1 checkpoint:

`0b371240b4699205346e8a89c2e75c4e1571cde3`

The parent chain already proves, with synthetic non-PE data, that a verified bundle can flow through protected staging and the existing atomic Install/Upgrade promotion boundary while native execution remains blocked.

## IPC v5

The protocol is intentionally bumped to version 5 with pipe identity:

`NetPilot.Service.v5`

R42.12 adds two no-payload commands:

- `ByeDpiGetPackageStatus`;
- `ByeDpiApplyReviewedPackage`.

The request model still exposes no field for a ByeDPI version, URL, path, archive, executable or command line. A client therefore cannot redirect the privileged service to arbitrary package material.

Package responses expose only typed state:

- installed/not installed;
- current canonical version;
- retained previous canonical version;
- Install/Upgrade operation result.

No managed/staging path is exposed to the user IPC.

## Privileged service rules

`ByeDpiGetPackageStatus` is read-only and goes through `IWindowsByeDpiPackageProvisioner.GetStatusAsync()`.

`ByeDpiApplyReviewedPackage` requires all of the following before package mutation can even be requested:

1. an authenticated named-pipe peer under the existing Windows IPC boundary;
2. completed NetPilot enrollment;
3. the caller SID must be the installation owner;
4. `PrivilegedOperation.EngineInstall` must be explicitly allowed by `OperationPolicy`;
5. the service reads the ByeDPI runtime state and passes the engine-lease fact into the typed provisioner;
6. the operation uses the bounded engine-install timeout.

The provisioner surface itself accepts only `engineLeaseActive` plus cancellation. It does not accept a caller-controlled target version, URL, path or archive.

## Production remains fail-closed

R42.12 production DI registers:

`NoAdmittedWindowsByeDpiPackageProvisioner.Instance`

This implementation exposes protected package status through the existing R42.3 managed-state boundary, but every package mutation throws typed `ByeDpiPackageProvisioningNotAdmitted`.

There are therefore two independent blockers in the production service:

- the current `CreateNoAdmittedEnginePolicy()` does not allow `EngineInstall`;
- even if the policy were changed accidentally, the injected package provisioner still has no live transport and rejects mutation.

The service also retains the existing no-admitted ByeDPI process launcher and no-admitted runtime-plan provider. R42.12 does not alter either runtime gate.

## Network boundary

R42.12 does not construct `SocketsHttpHandler`, `HttpClientHandler`, `HttpClient` or a live `ByeDpiIsolatedHttpTransport` in `NetPilot.Service`.

The verified R42.10/R42.11/R42.11.1 chain remains available for a later explicitly injected provisioner, but production Service does not receive a live network handler in this gate.

A later Windows E2E gate must separately review:

- construction/configuration of the live handler with automatic redirects disabled;
- real GitHub exact-tag metadata and immutable asset retrieval;
- TLS/DNS/network failure behavior;
- actual LocalSystem protected staging/promotion with the reviewed artifact;
- explicit package-provisioning policy enablement.

## Tests

R42.12 tests verify:

- protocol v5 and the dedicated pipe identity;
- both package commands validate only with zero user target payload;
- typed package response framing round-trips;
- the package provisioner interface has no `string`, `Uri`, `Stream` or `byte[]` target inputs;
- production `NoAdmittedWindowsByeDpiPackageProvisioner` rejects mutation for both inactive and active engine-lease facts;
- existing Zapret adapter test doubles remain fail-closed for the new ByeDPI package methods.

## Deliberately excluded

- live ByeDPI HTTP handler construction;
- real network acquisition;
- production package Install/Upgrade enablement;
- caller-selected package version;
- caller URL/path/archive input;
- package repair/rollback/uninstall IPC;
- automatic engine start after package changes;
- native ByeDPI execution;
- WinDivert execution;
- production runtime admission.

## Security position

`SERVICE_IPC_PACKAGE_BOUNDARY=PRESENT_FAIL_CLOSED`

`LIVE_NETWORK_ACQUISITION=NOT_ADMITTED`

`PACKAGE_MUTATION=BLOCK`

`NATIVE_EXECUTION=NOT_IMPLEMENTED`

`PRODUCTION_ADMISSION=BLOCK`
