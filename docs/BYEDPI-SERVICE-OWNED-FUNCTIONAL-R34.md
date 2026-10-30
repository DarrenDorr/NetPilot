# R34 — ByeDPI service-owned synthetic SOCKS/HTTP functional lab

Status: **PREFLIGHT PASS / HOSTED RUNTIME EVIDENCE PENDING / PRODUCTION ADMISSION BLOCK**

## Purpose

R34 validates the next controlled integration layer after the R32 direct loopback runtime lab and the R33 typed/service-owned process boundary.

The R34 target path is:

```text
exact reviewed ByeDPI candidate
  -> fixed Program Files managed package
  -> immutable SHA-256 verification + read locks
  -> ACL/owner verification
  -> verified pre-launch handoff
  -> typed argv compiler
  -> single audited SystemZapretProcessLauncher
  -> WindowsByeDpiServiceOwnedProcessController
  -> SOCKS5 CONNECT
  -> synthetic HTTP server on 127.0.0.1
```

R34 does not add production admission and does not expose user-facing `EngineStart`.

## Exact candidate

The lab contract is pinned to the same reviewed Windows x64 candidate that passed R32:

- ByeDPI source commit: `7efde1b1296eaaa187b70e951894dde17527489c`;
- candidate SHA-256: `866e99c3c7690326cc6e365fe02980f06ae2b394c045555674fb95db52b0dbdd`;
- lab package version: `0.17.3-netpilot-r34-lab`;
- fixed component layout: `bin/ciadpi.exe`.

The resolved managed location is therefore:

```text
%ProgramFiles%\NetPilot\Engines\Managed\byedpi\0.17.3-netpilot-r34-lab\x64\bin\ciadpi.exe
```

The lab host does not accept an executable path from command-line, environment, IPC or GUI.

## Capability model

ByeDPI on Windows is an explicit-proxy backend. Existing NetPilot product profiles require transparent capabilities, so R34 does not pretend that ByeDPI implements the user-facing `standard`, `discord`, `youtube` or `discord-youtube` profiles.

A separate trusted lab-only profile is used:

```text
lab-byedpi-loopback-r34
```

It requires only `ExplicitProxyTcp` and is deliberately absent from `BuiltInEngineProfileCatalog`. Product profile enumeration remains unchanged.

## Typed argv

The only R34 executable shape is:

```text
-i 127.0.0.1 -p <ephemeral-unprivileged-port> -N -U -c 8 -x 0
```

The port is selected internally by the lab host and represented as a typed positive integer. The host accepts no command-line arguments.

No generic hostname, IP, URL, filesystem path, config string, script, environment value or raw command-line fragment is representable through the R34 contract.

## Functional cycles

The runtime lab performs:

1. verified launch and transition to `Running`;
2. loopback-listener verification;
3. SOCKS5 no-auth negotiation;
4. SOCKS5 `CONNECT` to a synthetic HTTP server bound to `127.0.0.1`;
5. HTTP `GET /r34` and exact marker response;
6. controlled `Stop` and listener cleanup;
7. a second complete start/traffic/stop cycle;
8. a crash simulation by terminating the exact controller-owned PID;
9. controller reconciliation back to `Stopped`;
10. final residual-process/listener cleanup checks.

The synthetic HTTP target is local. R34 does not require Internet traffic and must not use an external target as proof of correctness.

## Process-creation boundary

The lab host does not call `Process.Start`.

All native creation passes through the existing audited `SystemZapretProcessLauncher`, where NetPilot maintains a single `Process.Start` site. The lab host may terminate only the exact PID returned by the service-owned controller during the dedicated crash-reconciliation test.

## Filesystem security boundary

Before native execution, the hosted lab stages the exact candidate into the fixed managed Program Files location and applies a protected ACL only to the ephemeral NetPilot tree.

The verification chain requires:

- privileged owner (`SYSTEM`, `BUILTIN\Administrators`, or `TrustedInstaller`);
- no unprivileged write/delete/ACL/ownership mutation rights;
- no reparse points in the managed chain;
- exact component SHA-256;
- verification read locks held through launch handoff.

The workflow must not modify the ACL of the general `%ProgramFiles%` directory.

## Production isolation

R34 keeps these production catalogs empty:

- `BuiltInEngineExecutableIdentityCatalog`;
- `BuiltInEnginePackageLayoutCatalog`;
- `BuiltInEngineResourceBudgetCatalog`;
- `BuiltInEngineRuntimePrerequisiteCatalog`;
- `BuiltInEngineLaunchPlanMetadataCatalog`;
- `BuiltInEngineLaunchIdentityBindingCatalog`;
- `BuiltInEngineArgumentPlanCatalog`.

The R34 lab contract is forbidden in App, App.Windows, Automation, Service and installer composition by `scripts/security/r34-lab-boundary-check.sh`.

Production Service DI therefore remains fail-closed and does not receive the system process launcher for ByeDPI.

## Preflight evidence

R34 preflight run `41001769948` passed both jobs:

- Linux security policy + R34 boundary policy + solution build + Core contract tests: PASS;
- Windows process-boundary tests + R34 lab-host restore/build without native execution: PASS.

The general CI run `41001769955` also passed the R34 security/build/test portion; its normal installer build is independent of the R34 native runtime gate.

## What R34 does NOT prove

Even after hosted runtime PASS, R34 does not by itself prove:

- real `NetPilot.Service` execution as LocalSystem;
- authenticated production named-pipe IPC;
- multi-user SID/lease behavior with ByeDPI;
- product-profile routing or system-wide traffic interception;
- WinDivert compatibility;
- production signing/Defender disposition;
- native production admission.

Those are separate later gates. In particular, service-owned here means the same controller/lifecycle boundary that the service will own; the R34 hosted lab process itself is not the installed LocalSystem service.

## PASS markers

The runtime host must produce all of the following:

```text
TYPED_ARGV=PASS
ROUND1_SOCKS_HTTP=PASS
ROUND1_CLEANUP=PASS
ROUND2_SOCKS_HTTP=PASS
ROUND2_CLEANUP=PASS
CRASH_RECONCILE=PASS
FINAL_STATE=STOPPED
SYSTEM_PROXY_MUTATION=NOT_REQUESTED
WINDIVERT=NOT_REQUESTED
PRODUCTION_ADMISSION=BLOCK
RESULT=BYEDPI_SERVICE_OWNED_FUNCTIONAL_R34_PASS
```

The hosted workflow additionally verifies that proxy settings and WinDivert/system-driver state did not change and that no residual `ciadpi` process remains.

Until that evidence exists, R34 status remains **HOSTED RUNTIME EVIDENCE PENDING**.
