# NetPilot architecture (bootstrap)

## Product model

NetPilot has two user experiences:

- **Simple mode**: status of Discord, YouTube, Telegram and user-added resources.
- **Advanced mode**: engines, strategies, laboratory, diagnostics, logs, sources and update details.

The simple mode must never require knowledge of WinDivert, winws, MTProto or strategy syntax.

## Layering

### NetPilot.App
Cross-platform Avalonia UI. It must not perform privileged network/system changes directly.

### NetPilot.Core
Platform-neutral models and orchestration: resources, health status, profiles and later strategy scoring.

### NetPilot.Engine.Abstractions
Common contract for network engines. Engine implementations must be replaceable without changing Core.

### NetPilot.Platform.Abstractions
Capabilities supplied by Windows/Linux/Android adapters.

### NetPilot.Platform.Windows
First platform adapter. Later Windows privileged operations will be moved behind a dedicated service boundary.

### NetPilot.Security
Validation and authorization primitives. This layer intentionally exposes capabilities/structured operations instead of arbitrary process or shell execution.

### NetPilot.Backup
Backup manifest, import/export and later authenticated encryption for secret-containing portable backups.

### NetPilot.Updater
Trusted upstream definitions, release metadata verification and staged/rollback updates.

## Planned platform architecture

Windows:

NetPilot.App -> authenticated local IPC -> NetPilot.Service -> Windows engine adapters

Linux:

NetPilot.App -> authenticated local IPC -> NetPilot daemon/systemd -> Linux engine adapters

Android:

NetPilot.App -> Android foreground service / VpnService where required -> Android engine adapters

## Strategy model

Official upstream BAT/shell files may be parsed as an input source, but NetPilot's internal strategy representation must be structured data. User strategies must never become arbitrary executable scripts.

## Data privacy

All configuration, strategy history, health results and backups are local by default. No telemetry or diagnostic upload occurs without explicit user action.
