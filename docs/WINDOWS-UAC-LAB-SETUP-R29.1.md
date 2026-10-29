# R29.1 — историческая инструкция single-owner Windows UAC lab

Дата: 29.10.2026.

Статус: **SUPERSEDED BY R30 / DO NOT USE FOR CURRENT LAB**.

Эта инструкция описывала старую single-owner модель, в которой второй Windows SID ожидался как `UnauthorizedUser`.

После принятия R30 это поведение больше не является целевым.

Актуальная модель:

- Account A — installation owner и обычный runtime user;
- Account B — отдельный полноценный runtime user;
- оба проходят authenticated IPC;
- каждый получает отдельный service-owned SID profile;
- обычные пользователи не имеют прямого файлового доступа к `Program Files\NetPilot\Users\<SID>`;
- machine-level provisioning остаётся за installation owner;
- global engine lease не позволяет одному SID перехватить активную engine-session другого SID.

Для текущего lab используйте:

- `docs/WINDOWS-UAC-LAB-SETUP-R30.1.md`;
- `docs/WINDOWS-MULTI-USER-SID-ISOLATION-R30.md`;
- `.github/workflows/windows-uac-security.yml` из актуальной `feature/engine-bootstrap-r15`.

R29 сохраняется только как исторический checkpoint. Он не должен использоваться для вынесения PASS/FAIL по текущей multi-user архитектуре.
