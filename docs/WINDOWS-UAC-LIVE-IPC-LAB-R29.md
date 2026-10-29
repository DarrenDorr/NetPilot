# R29 — Windows UAC / LocalSystem / cross-SID live IPC lab

Дата подготовки: 29.10.2026.

Статус: **LAB HARNESS READY / SELF-HOSTED EXECUTION NOT YET PERFORMED / NO NATIVE ENGINE EXECUTION / RELEASE GATE BLOCK**.

## Зачем нужен этот lab

Hosted CI уже доказывает сборку, unit/security tests и создание установщика, но не может доказать реальную Windows-границу:

`standard user -> Inno UAC -> LocalSystem service -> original user SID -> authenticated named-pipe IPC`.

Ранее `windows-uac-security.yml` проверял реальный UAC/install/SID lifecycle, но не имел отдельного live negative test для второго Windows SID.

R29 закрывает именно эту недостающую автоматизацию.

## Lab-only IPC client

Добавлен инструмент:

`tools/NetPilot.WindowsIpcLabClient`

Он:

- не входит в installer/product payload;
- не запускает native engine;
- не устанавливает и не обновляет engine;
- не меняет сеть/firewall/driver state;
- отправляет только fixed `Ping` через существующий `WindowsPrivilegedServiceClient`;
- самостоятельно аутентифицирует server boundary тем же production client code;
- не принимает SID, pipe name, command, path, URL или payload от пользователя;
- поддерживает только два фиксированных lab mode:
  - `--expect-authorized`;
  - `--expect-denied`.

Клиент fail-closed запрещает запуск под LocalSystem, Session 0 или administrator token.

## Hosted CI boundary

Обычный `CI` теперь только **компилирует** lab client на `windows-2025`.

Hosted runner не исполняет его, потому что там нет настоящей установленной LocalSystem-службы и реальной UAC/SID enrollment boundary.

Это предотвращает ложный PASS.

## Self-hosted lab requirements

Runner labels:

- `self-hosted`;
- `windows`;
- `netpilot-uac-lab`.

Runner должен быть:

1. отдельной изолированной Windows VM/машиной;
2. запущен интерактивно обычным пользователем;
3. не как Windows service;
4. не LocalSystem;
5. не administrator token;
6. UAC `EnableLUA=1`.

Нужно **два разных заранее созданных standard-user Windows account**:

- Account A — пользователь, от которого интерактивно запущен GitHub runner и который становится владельцем установки;
- Account B — отдельный standard user для отрицательного SID-теста.

Account B не должен быть администратором и не должен иметь тот же SID.

## Required GitHub Actions secrets

Для Account B на репозитории/окружении lab задаются:

- `NETPILOT_UAC_LAB_OTHER_USER`;
- `NETPILOT_UAC_LAB_OTHER_PASSWORD`.

Пароль:

- не передаётся в command line;
- не печатается;
- читается workflow только для создания `PSCredential`;
- удаляется из step environment до запуска second-user process.

Lab client сам пароль не получает и не обрабатывает.

## Что делает workflow

`.github/workflows/windows-uac-security.yml` остаётся только manual `workflow_dispatch` и выполняет:

1. проверку standard-user interactive runner + UAC;
2. unit/security tests;
3. publish self-contained lab client во временный каталог `C:\Users\Public`;
4. build installer;
5. запуск Setup без PowerShell `-Verb RunAs` — elevation запрашивает сам Inno Setup;
6. проверку LocalSystem service;
7. проверку, что `authorized-user.sid` равен исходному Account A SID;
8. проверку удаления enrollment nonce/session artifacts;
9. live IPC Account A -> `Ping=Ok`;
10. live IPC Account B -> exact `UnauthorizedUser`;
11. подтверждение, что Account B SID отличается от Account A и Account B не admin;
12. удаление lab client;
13. uninstall через реальную UAC boundary;
14. проверку отсутствия service/authorization state после uninstall.

## PASS criteria

Полный UAC/LocalSystem/SID IPC gate можно считать PASS только если:

- workflow запущен на isolated self-hosted Windows lab;
- все preconditions выполнены;
- реальный Inno UAC завершён успешно;
- Account A SID сохранён как owner;
- production LocalSystem service работает;
- Account A live Ping возвращает `Ok`;
- Account B live Ping возвращает именно `UnauthorizedUser`;
- Account B является другим standard-user SID;
- uninstall удаляет service и authorization state;
- никакой test-only SID/UAC bypass не использовался.

Наличие YAML/кода без фактического self-hosted run **не является PASS**.

## Что этот lab НЕ доказывает

R29 UAC lab не:

- разрешает `EngineStart`;
- запускает zapret2/ByeDPI/DPIBreak;
- устанавливает/загружает WinDivert;
- выбирает production resource budgets;
- выдаёт `ADMIT` или `LAB_APPROVED` native engine;
- решает WinDivert runtime/signing blockers.

Service по-прежнему должен работать с `OperationPolicy.CreateNoAdmittedEnginePolicy()` и `NoAdmittedZapretProcessLauncher`.

## Следующий реальный шаг после фактического PASS

После успешного self-hosted UAC lab отдельно закрываются:

1. WinDivert Windows boundary;
2. native-engine isolated runtime lab;
3. production resource sizing;
4. NetPilot-owned zapret2 fork artifact + повторный exact admission;
5. final start/stop/crash/timeout/cleanup/reboot evidence.
