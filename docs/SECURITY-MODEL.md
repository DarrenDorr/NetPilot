# NetPilot security model

Security is a release requirement, not a later feature.

## Primary threats

1. **Update supply-chain compromise** — installing an unexpected or modified engine/update.
2. **Privilege escalation through the UI** — untrusted UI/config input reaching an elevated service.
3. **Command/argument injection** — a strategy or imported profile turning into arbitrary command execution.
4. **IPC impersonation** — another local process issuing privileged NetPilot commands.
5. **Path traversal / unsafe extraction** — crafted update or backup archives writing outside approved directories.
6. **Secret leakage** — proxy secrets, credentials or user data appearing in logs/backups.
7. **Malicious imports** — a `.npb`/`.npconfig` file abusing parsers or unsafe settings.
8. **Rollback failure** — an update leaving the user without a working network configuration.
9. **Dependency compromise** — unnecessary third-party packages increasing supply-chain surface.
10. **Over-broad traffic interception** — applying an engine to resources/apps the user did not intend.

## Required controls

- The desktop UI normally runs unelevated.
- Privileged operations use explicit typed commands, not shell strings.
- The privileged endpoint must authenticate/authorize the local caller and restrict its ACL/permissions.
- Engine arguments are built from validated fields and allowlisted options.
- User-created strategies cannot execute programs, PowerShell, cmd, bash or arbitrary scripts.
- GitHub repositories used for automatic update discovery are explicitly allowlisted.
- Downloaded releases are verified against upstream-provided integrity metadata when available; NetPilot releases should additionally be signed when signing infrastructure is introduced.
- Archive extraction uses canonical-path checks and rejects links/path traversal.
- Backup secrets are excluded by default. If included in a portable backup, authenticated encryption and a user password are mandatory.
- Logs contain technical events only: version, engine state, strategy identifier, diagnostic code and timing. Never traffic payloads, cookies, tokens, credentials or browsing history.
- Imported configuration is schema-versioned, size-limited and validated before it can affect engines.
- Automatic backups are created before risky changes; the last 10 are retained by default.
- Engine updates are staged and support rollback.
- Resource health checks and strategy routing are separate concepts so a health-check URL cannot silently broaden traffic interception.

## Security review gates

Before the first public executable:

- threat-model review;
- static analysis with zero known critical/high findings;
- dependency vulnerability audit;
- archive/import fuzz tests for parsers;
- IPC authorization tests;
- update-integrity negative tests;
- backup encryption tests;
- logs reviewed for sensitive-data leakage;
- uninstall/factory-reset tests.
