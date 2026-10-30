# R36 — ByeDPI installed LocalSystem service lab

## Status

**IMPLEMENTATION PREPARED / COMPILE PREFLIGHT PENDING / PRODUCTION ADMISSION BLOCK**

Base: R35 authenticated IPC/service scaffold.

R36 is deliberately implemented as separate lab-only executables. It does not add a runtime switch, environment variable or hidden admission path to the production `NetPilot.Service`.

## Lab components

- `tools/NetPilot.ByeDpiR36ServiceLab`
  - fixed Windows service name `NetPilot.R36.Lab`;
  - requires Windows x64 and LocalSystem identity;
  - accepts no command-line arguments;
  - uses exact R34 contract and fixed loopback proxy port `39036`;
  - holds immutable verification file handles for the full plan-provider lifetime;
  - uses the same `PrivilegedIpcWorker` and IPC v4 as the product service;
  - allows only `EngineStart` and `EngineStop` in this isolated composition;
  - Zapret remains no-admit.

- `tools/NetPilot.ByeDpiR36LabClient`
  - runs in an interactive Windows session;
  - uses `WindowsPrivilegedServiceClient` only;
  - can enroll the current authenticated user through the existing nonce/session flow;
  - controls ByeDPI only through typed IPC v4;
  - knows only the fixed lab listener port, never executable path or native argv;
  - full mode performs `Ping -> Status -> Start -> SOCKS5/local HTTP -> Stop`;
  - fixed `--start-for-service-stop` mode leaves the owned process running so workflow can verify graceful service-stop cleanup;
  - fixed `--status-only` mode reads only typed status.

## Production separation

Normal `NetPilot.Service` remains composed with:

- `NoAdmittedWindowsVerifiedEngineProcessLauncher`;
- `NoAdmittedWindowsByeDpiRuntimePlanProvider`.

The production installer must not package, install or register either R36 lab executable.

Production native engine catalogs remain empty.

## Planned hosted runtime evidence

After compile/security preflight, a dedicated pinned Windows workflow may:

1. rebuild the exact R34 candidate and require its exact SHA-256;
2. publish R36 service/client;
3. refuse any pre-existing `NetPilot.Service`/`NetPilot.R36.Lab` service or NetPilot Program Files root;
4. stage service + exact candidate only under a protected `Program Files\NetPilot` tree;
5. prepare one-time enrollment session state;
6. register `NetPilot.R36.Lab` as LocalSystem and start it;
7. run the interactive IPC client and prove real LocalSystem service-controlled SOCKS/HTTP traffic;
8. start ByeDPI again via IPC, stop the Windows service and prove no residual `ciadpi.exe`/listener;
9. restart the service and prove typed status is `Stopped`;
10. verify system proxy and WinDivert state unchanged;
11. delete only workflow-owned service/tree and upload text evidence.

## Not yet proven

R36 does not become PASS until the pinned hosted runtime workflow succeeds and evidence is reviewed. The current design also does not yet prove abrupt service-process termination cleanup or a second interactive SID; those remain explicit later evidence gates unless added safely to R36.x.
