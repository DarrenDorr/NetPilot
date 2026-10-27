# NetPilot

**NetPilot** is an open-source network access manager designed around a simple principle: ordinary users should see whether their services work, while technical routing, engines and strategies stay in an optional advanced mode.

> Status: early architecture/bootstrap stage. No network-bypass engine is bundled or active yet.

## Goals

- Simple default UI: service status first, technical details optional.
- Separate engine model: Zapret, Telegram proxy and future engines remain isolated components.
- Windows first, with the core designed for Linux and Android from day one.
- No hidden telemetry and no automatic upload of logs or user data.
- Updates only from explicitly trusted upstreams, with integrity verification before installation.
- User strategies represented as structured data, not executable shell scripts.
- Backup, restore, repair and factory-reset flows built into the architecture.

## Planned platforms

1. Windows 10/11 x64
2. Linux
3. Android

## Technology

- C# / .NET 10 LTS
- Avalonia 12.1.x
- MVVM-oriented architecture
- Platform adapters for privileged/network-specific operations

## Planned upstream integrations

NetPilot is an independent project and is **not an official Flowseal product**.

- Flowseal / zapret-discord-youtube: https://github.com/Flowseal/zapret-discord-youtube
- Flowseal / tg-ws-proxy: https://github.com/Flowseal/tg-ws-proxy
- bol-van / zapret: https://github.com/bol-van/zapret
- WinDivert: https://github.com/basil00/WinDivert

Licenses and attribution for bundled third-party components will be retained and surfaced in the application.

## Security principles

- UI runs without permanent administrator privileges.
- Privileged operations are isolated behind a narrow platform service/API.
- No API for arbitrary shell-command execution.
- Update sources are allowlisted.
- Backups exclude secrets by default.
- Logs must not contain traffic content, credentials, tokens, cookies or browsing history.

See [docs/SECURITY-MODEL.md](docs/SECURITY-MODEL.md).

## License

NetPilot is licensed under the MIT License. See [LICENSE](LICENSE).
