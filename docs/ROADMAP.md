# NetPilot roadmap

## Phase 0 — foundation

- Repository, license and architecture.
- Cross-platform Core and Avalonia shell.
- Security model and typed privileged-operation boundary.
- CI build/test/audit baseline.

## Phase 1 — Windows MVP

- Simple dashboard and system tray.
- Resource health model: Discord, YouTube and Telegram.
- Windows privileged service with authenticated local IPC.
- Flowseal Zapret engine discovery/update integration.
- Last-known-working strategy selection.
- TG WS Proxy as a separate engine.
- Diagnostics and transparent source/license pages.

## Phase 2 — reliability

- Automatic failover after confirmed repeated failures.
- Backup/restore and repair flows.
- Engine rollback after failed updates.
- Factory reset and engine reinstall.
- Full appearance/accessibility settings.

## Phase 3 — strategy laboratory

- Clone/edit structured strategies.
- Test and compare strategies.
- Per-resource scoring.
- Version history for user strategies.
- Controlled strategy variant generation.

## Phase 4 — Linux

- Linux daemon/service boundary.
- Linux-specific engine adapters.
- Desktop packaging after supported distributions are selected.

## Phase 5 — Android

- Android UI adaptation.
- Foreground service / VpnService architecture where required.
- Per-app routing where technically appropriate.
- Mobile-specific strategy/engine adapters.
