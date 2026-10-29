# R30.1 — подготовка self-hosted Windows multi-user UAC lab

Дата: 29.10.2026.

Статус: **SETUP GUIDE / NO NATIVE ENGINE ADMIT / RELEASE GATE BLOCK**.

Эта инструкция готовит Windows-машину для проверки R30:

`standard user A -> Inno UAC -> LocalSystem service -> SID-A profile`

и затем:

`standard user B -> authenticated IPC -> SID-B profile`

Оба пользователя должны работать с NetPilot, но их service-owned профили изолированы, а machine-level installation owner остаётся неизменным.

## 1. Что этот lab проверяет

R30 lab подтверждает:

- настоящий UAC boundary;
- LocalSystem-службу;
- installation owner SID;
- authenticated IPC для двух разных standard-user SID;
- отдельный профиль для каждого SID;
- отсутствие прямого файлового доступа standard user к service-owned profile;
- неизменность installation owner после входа второго пользователя;
- cleanup после uninstall.

Он не выдаёт `ADMIT` native engine и не запускает WinDivert/zapret2/ByeDPI/DPIBreak.

## 2. Требования к машине

Используйте отдельную Windows 10/11 x64 VM или тестовую машину без рабочей установки NetPilot.

Нужно:

- UAC `EnableLUA=1`;
- PowerShell 7 (`pwsh`);
- Git for Windows;
- доступ к GitHub по HTTPS;
- интерактивный GitHub Actions runner;
- runner не должен работать как Windows service;
- runner не должен запускаться из elevated/admin token;
- оператор должен присутствовать для настоящего UAC prompt.

Не отключайте Defender, Secure Boot, UAC или другие системные защиты ради прохождения теста.

## 3. Учётные записи

Для полного multi-user evidence нужны:

1. **Account A** — standard user, под которым интерактивно работает runner. После установки становится installation owner.
2. **Account B** — другой локальный standard user с отдельным SID. Он должен получить собственный runtime profile, а не `UnauthorizedUser`.
3. **Administrator credential** — отдельная административная учётная запись только для настоящего UAC prompt установки/удаления.

Account A и Account B не должны входить в Administrators.

Для простого первого запуска GUI второй пользователь не обязателен. Он нужен именно для полного R30 multi-user security evidence.

## 4. Получение рабочей ветки

Под Account A:

```powershell
cd C:\
git clone https://github.com/DarrenDorr/NetPilot.git C:\NetPilot-Lab
cd C:\NetPilot-Lab
git switch feature/engine-bootstrap-r15
```

Если репозиторий уже существует:

```powershell
cd C:\NetPilot-Lab
git fetch origin
git switch feature/engine-bootstrap-r15
git pull --ff-only
```

## 5. Preflight

Запускайте из обычной, не elevated PowerShell 7 сессии Account A:

```powershell
pwsh -NoLogo -NoProfile -File .\scripts\windows-uac-lab-preflight.ps1 -OtherUser '.\NetPilotLabB'
```

Preflight ничего не устанавливает и не меняет системные настройки.

Он проверяет:

- Windows x64;
- standard-user token;
- не LocalSystem;
- не Session 0;
- UAC;
- PowerShell/Git/.NET availability;
- Secondary Logon;
- существование и SID Account B;
- что Account B не admin;
- отсутствие старой службы/установки NetPilot;
- доступ к GitHub.

До запуска lab не должно быть `FAIL`.

## 6. Self-hosted runner

В GitHub:

`NetPilot -> Settings -> Actions -> Runners -> New self-hosted runner -> Windows x64`

Выполняйте команды регистрации, которые GitHub покажет именно вашему репозиторию.

Рекомендуемый каталог:

```text
C:\NetPilot-UacRunner
```

Добавьте label:

```text
netpilot-uac-lab
```

Не устанавливайте runner как Windows service.

Запускать интерактивно под Account A:

```cmd
run.cmd
```

Workflow ожидает labels:

```text
self-hosted
windows
netpilot-uac-lab
```

После регистрации повторите preflight:

```powershell
pwsh -NoLogo -NoProfile -File .\scripts\windows-uac-lab-preflight.ps1 `
  -OtherUser '.\NetPilotLabB' `
  -RunnerRoot 'C:\NetPilot-UacRunner'
```

## 7. Secrets для Account B

В GitHub:

`Settings -> Secrets and variables -> Actions`

Создайте repository secrets:

```text
NETPILOT_UAC_LAB_OTHER_USER
NETPILOT_UAC_LAB_OTHER_PASSWORD
```

Для локальной учётной записи можно использовать:

```text
.\NetPilotLabB
```

Пароль Account B не присылайте в ChatGPT, issue, PR, commit или лог.

Workflow использует его только для `PSCredential`, не передаёт через command line и удаляет password environment variable до запуска дочернего процесса.

## 8. Запуск workflow

Workflow уже присутствует в default branch `main`, поэтому GitHub показывает его в Actions.

1. Откройте `Actions`.
2. Выберите **Windows UAC Security Gate**.
3. Нажмите `Run workflow`.
4. Выберите branch `feature/engine-bootstrap-r15`.
5. Запустите workflow.
6. Не закрывайте окно `run.cmd`.
7. Когда Inno Setup запросит elevation, введите отдельные administrator credentials в настоящий Windows UAC prompt.

Не запускайте PowerShell, runner или Setup через `Run as administrator` заранее.

## 9. Ожидаемый R30 PASS

PASS допустим только если workflow целиком успешен и доказано:

- Account A — standard user;
- UAC включён;
- Inno Setup сам запросил elevation;
- `NetPilot.Service` работает как LocalSystem;
- `authorized-user.sid` соответствует SID-A;
- Account A получает `Ping=Ok`;
- Account A получает `PROFILE_DIRECT_ACCESS=DENIED`;
- Account B имеет другой standard-user SID;
- Account B также получает `Ping=Ok`;
- Account B также получает `PROFILE_DIRECT_ACCESS=DENIED`;
- SID installation owner после Account B не меняется;
- uninstall удаляет service, enrollment state и `Users` tree.

`PROFILE_DIRECT_ACCESS=DENIED` является ожидаемым результатом: пользовательские данные обслуживаются LocalSystem-службой через authenticated IPC, а не читаются приложением напрямую из `Program Files`.

## 10. После R30 PASS

R30 закрывает только Windows UAC / LocalSystem / multi-SID isolation boundary.

Далее остаются отдельные блоки:

1. WinDivert Windows boundary;
2. isolated native-engine runtime lab;
3. production resource sizing;
4. NetPilot-owned zapret2 fork artifact + exact admission;
5. global engine lease live test;
6. start/stop/crash/timeout/cleanup/reboot evidence.

До их закрытия production native runtime остаётся fail-closed.
