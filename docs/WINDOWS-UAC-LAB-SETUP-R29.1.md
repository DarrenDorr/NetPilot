# R29.1 — подготовка self-hosted Windows UAC lab

Дата: 29.10.2026.

Статус: **SETUP GUIDE / NO NATIVE ENGINE EXECUTION / RELEASE GATE BLOCK**.

Этот документ описывает только подготовку изолированной Windows-машины для R29 UAC / LocalSystem / cross-SID IPC проверки. Он не разрешает `EngineStart`, не запускает zapret2/ByeDPI/DPIBreak и не устанавливает WinDivert.

## 1. Требования к машине

Используйте отдельную Windows 10/11 x64 VM или тестовую машину, на которой нет рабочей установки NetPilot.

Обязательно:

- UAC включён (`EnableLUA=1`);
- есть доступ к GitHub по HTTPS;
- установлен PowerShell 7 (`pwsh`);
- рекомендуется Git for Windows;
- GitHub Actions runner запускается **интерактивно**, а не как Windows service;
- runner запускается обычным пользователем без административного token;
- во время workflow оператор присутствует у машины, потому что Inno Setup должен показать настоящий UAC prompt.

Не отключайте UAC, Secure Boot, Defender и другие системные защиты ради теста.

## 2. Учётные записи

Нужны три роли, две из которых обязательно являются отдельными standard-user SID:

1. **Account A** — обычный пользователь, под которым интерактивно работает GitHub runner. Этот SID должен стать владельцем установки NetPilot.
2. **Account B** — отдельный локальный обычный пользователь. Используется только для отрицательной IPC-проверки `UnauthorizedUser`.
3. **Administrator credential** — существующая административная учётная запись, пароль которой оператор вводит только в настоящий Windows UAC prompt при установке/удалении. Она не должна использоваться для запуска runner.

Account B рекомендуется создать заранее через Windows Settings и один раз проверить вход в систему. Не добавляйте Account A или Account B в локальную группу Administrators.

Пароль Account B не записывается в репозиторий, workflow или командную строку.

## 3. Локальный preflight до регистрации runner

В ветке `feature/engine-bootstrap-r15` есть read-only скрипт:

```powershell
scripts/windows-uac-lab-preflight.ps1
```

Он ничего не устанавливает и не меняет системные настройки.

Запускать из обычной PowerShell 7 сессии Account A:

```powershell
pwsh -NoLogo -NoProfile -File .\scripts\windows-uac-lab-preflight.ps1 -OtherUser '.\NetPilotLabB'
```

Ожидаемый итог:

```text
RESULT=READY_FOR_R29_RUNNER_SETUP
```

Любой `RESULT=BLOCKED` нужно исправить до запуска R29.

## 4. Регистрация GitHub Actions runner

Откройте репозиторий NetPilot в GitHub:

`Settings -> Actions -> Runners -> New self-hosted runner`

Выберите Windows x64 и выполняйте команды, которые GitHub показывает именно для этого репозитория.

Правила:

- registration token не копировать в issue, PR, документацию или чат;
- распаковать runner в отдельный каталог, например `C:\NetPilot-UacRunner`;
- при `config.cmd` добавить label:

```text
netpilot-uac-lab
```

- **не устанавливать runner как Windows service**;
- после конфигурации запускать его из интерактивной сессии Account A:

```cmd
run.cmd
```

Workflow требует labels:

```text
self-hosted
windows
netpilot-uac-lab
```

После регистрации можно повторить preflight с проверкой каталога runner:

```powershell
pwsh -NoLogo -NoProfile -File .\scripts\windows-uac-lab-preflight.ps1 -OtherUser '.\NetPilotLabB' -RunnerRoot 'C:\NetPilot-UacRunner'
```

## 5. GitHub Actions secrets для Account B

В GitHub:

`Settings -> Secrets and variables -> Actions`

Создайте два repository secret:

```text
NETPILOT_UAC_LAB_OTHER_USER
NETPILOT_UAC_LAB_OTHER_PASSWORD
```

Рекомендуемое значение пользователя:

```text
.\NetPilotLabB
```

Второй secret содержит пароль Account B.

Не присылайте пароль в ChatGPT, issue, PR, commit или лог workflow.

Workflow читает пароль только для создания `PSCredential`, удаляет его из step environment до запуска дочернего процесса и не передаёт пароль через command line.

## 6. Перед запуском

Проверьте:

- runner отображается в GitHub как `Idle`;
- интерактивная сессия Account A остаётся открытой;
- `run.cmd` продолжает работать в окне;
- Account A не admin;
- Account B не admin и имеет другой SID;
- `NetPilot.Service` отсутствует;
- `C:\Program Files\NetPilot` отсутствует;
- оператор может ввести отдельные administrator credentials в UAC prompt.

## 7. Запуск R29

После попадания `.github/workflows/windows-uac-security.yml` в default branch `main`:

1. открыть `Actions`;
2. выбрать **Windows UAC Security Gate**;
3. нажать `Run workflow`;
4. в качестве branch выбрать `feature/engine-bootstrap-r15`;
5. запустить workflow;
6. не закрывать интерактивный runner;
7. обработать настоящий UAC prompt на secure desktop, когда он появится.

Не используйте `Run as administrator` для PowerShell или `run.cmd`.

## 8. Что считается PASS

PASS можно заявить только если весь workflow завершился успешно и одновременно доказано:

- runner стартовал как standard user;
- UAC включён;
- Inno Setup сам запросил elevation;
- служба зарегистрирована как LocalSystem;
- owner SID совпал с Account A;
- Account A получил `Ping=Ok`;
- Account B получил exact `UnauthorizedUser`;
- Account B имеет другой standard-user SID;
- uninstall удалил service и authorization state.

Если workflow не дошёл до конца, результат не является PASS даже при успешных unit tests.

## 9. После PASS

R29 PASS закрывает только Windows UAC / LocalSystem / SID IPC boundary.

Следующие независимые blockers:

1. WinDivert Windows boundary;
2. isolated native-engine runtime lab;
3. production resource sizing;
4. NetPilot-owned zapret2 fork artifact и повторный exact admission;
5. start/stop/crash/timeout/cleanup/reboot evidence.

До их закрытия production native runtime остаётся fail-closed.
