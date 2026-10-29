# R31 — Windows First Boot

Дата: 29.10.2026.

Статус: **IMPLEMENTED / LOCAL EVIDENCE REQUIRED / NO NATIVE ENGINE ADMIT / RELEASE GATE BLOCK**.

## Цель

R31 — первый реальный запуск установленного NetPilot на обычной Windows-учётной записи.

Этот этап подтверждает только:

- проект собирается на Windows x64;
- установщик успешно создаётся;
- Inno Setup сам запрашивает UAC elevation;
- `NetPilot.Service` устанавливается и работает как `LocalSystem`;
- исходный standard-user SID становится installation owner;
- GUI/client может пройти authenticated IPC к службе;
- служба создаёт отдельный per-SID профиль;
- ordinary user не может читать service-owned профиль напрямую;
- read-only engine installation status доступен через IPC;
- uninstall/native runtime на этом этапе не тестируются автоматически.

R31 **не** разрешает запуск zapret/zapret2/ByeDPI/DPIBreak и не активирует WinDivert.

## 1. Что требуется

- Windows 10/11 x64;
- .NET 10 SDK;
- PowerShell 7 (`pwsh`);
- рабочая копия ветки `feature/engine-bootstrap-r15`;
- UAC включён;
- запуск команд из обычной пользовательской PowerShell-сессии.

Не запускайте PowerShell, Git, IDE или установщик через `Run as administrator`.

Для R31 второй Windows-пользователь не нужен.

## 2. Сборка First Boot installer

Из корня репозитория:

```powershell
pwsh -NoLogo -NoProfile -File .\scripts\windows-first-boot-build.ps1
```

Скрипт последовательно выполняет:

1. `dotnet restore`;
2. Release build solution;
3. Core/IPC tests;
4. Zapret parser/updater tests;
5. Windows security tests;
6. build read-only IPC probe;
7. сборку проверенного Inno Setup installer.

Успешный результат обязан закончиться:

```text
NATIVE_ENGINE_ADMISSION=BLOCKED
RESULT=FIRST_BOOT_BUILD_READY
```

Скрипт также печатает абсолютный путь, размер и SHA-256 установщика.

Если любой build/test step завершился ошибкой, First Boot останавливается.

## 3. Установка

Запустите полученный `NetPilot-Setup-0.1.0-alpha-r31-x64.exe` обычным двойным кликом.

Правильно:

- установщик стартует из standard-user desktop session;
- сам Inno Setup показывает настоящий Windows UAC prompt;
- в UAC можно ввести administrator credentials;
- после elevation installer через `ExecAsOriginalUser` возвращается к исходному user token для enrollment.

Неправильно:

- `Run as administrator` на Setup;
- elevated PowerShell -> запуск Setup;
- отключение UAC;
- ручная подмена SID/enrollment-файлов.

Если original-user enrollment не подтверждён, installer должен завершиться fail-closed и удалить свежую service/enrollment state.

## 4. Первый GUI запуск

После успешной установки можно открыть NetPilot обычным способом.

На R31 ожидается:

- окно NetPilot открывается без admin token;
- публичная HTTPS-диагностика доступна;
- native engine не запускается;
- статус движка сообщает, что безопасно допущенная версия пока отсутствует либо запуск заблокирован admission policy.

Открытие GUI само по себе ещё не является PASS.

## 5. Проверка установленной системы

Не закрывая обычную пользовательскую сессию, из корня репозитория выполните:

```powershell
pwsh -NoLogo -NoProfile -File .\scripts\windows-first-boot-verify.ps1
```

Verifier проверяет:

- текущий процесс не admin и не Session 0;
- `NetPilot.exe` и `NetPilot.Service.exe` установлены;
- служба существует;
- service account = `LocalSystem`;
- start mode = `Auto`;
- state = `Running`;
- `authorized-user.sid` совпадает с текущим SID;
- enrollment nonce/session удалены;
- `Ping=Ok` проходит через authenticated named pipe;
- read-only engine status проходит через тот же IPC boundary;
- per-SID profile создан службой;
- прямой filesystem-доступ standard user к этому профилю запрещён.

Для чистой R31 установки verifier также требует:

```text
ENGINE_INSTALLED=False
```

Если намеренно проверяется машина с уже существующим legacy engine state, допускается:

```powershell
pwsh -NoLogo -NoProfile -File .\scripts\windows-first-boot-verify.ps1 -AllowExistingEngine
```

Этот switch **не разрешает запуск engine** и не меняет admission policy.

## 6. Что считается PASS

PASS существует только при финальных строках:

```text
AUTHENTICATED_IPC=True
PER_SID_PROFILE_SERVICE_ONLY=True
NATIVE_ENGINE_EXECUTION_TESTED=False
NATIVE_ENGINE_ADMISSION=BLOCKED
RESULT=FIRST_BOOT_PASS
```

До получения этого результата нельзя утверждать, что R31 подтверждён на реальной машине.

## 7. Что будет после R31

После реального `FIRST_BOOT_PASS` следующий независимый этап:

**R32 — multi-user live validation**

На нём понадобятся два distinct standard-user SID и будет проверено:

- Account A и Account B оба получают `Ping=Ok`;
- каждый SID получает отдельный service-owned профиль;
- Account B не меняет installation owner;
- оба ordinary users не имеют прямого доступа к service-owned profile storage;
- global engine lease остаётся отдельным системным ресурсом.

Native engine execution и WinDivert по-прежнему остаются отдельными более поздними admission/lab этапами.
