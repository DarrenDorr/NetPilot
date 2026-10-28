# Security checklist Windows Installer R14

Этот checklist обязателен для каждого изменения установщика NetPilot.

- [x] Установка только с административными правами.
- [x] Фиксированный каталог `%ProgramFiles%\NetPilot`.
- [x] Запрет UNC-пути установки.
- [x] Только Windows x64 (`x64os`).
- [x] GUI и служба публикуются self-contained; внешний .NET Runtime не скачивается установщиком.
- [x] Управление службой выполняется через абсолютный `%SystemRoot%\System32\sc.exe`, без `cmd.exe`/PowerShell в runtime-установщике.
- [x] Служба останавливается до замены файлов.
- [x] Если служба не остановлена в ограниченный срок, установка завершается ошибкой.
- [x] `binPath` службы всегда задаётся абсолютным путём внутри `{app}\Service`.
- [x] Служба работает под `LocalSystem` только как минимальный привилегированный broker.
- [x] SCM ACL службы ограничен `LocalSystem` и локальными администраторами.
- [x] Named Pipe ACL не использует `Authenticated Users`; доступ получают интерактивные пользователи, Administrators и SYSTEM, после чего работает дополнительная SID/session/token проверка.
- [x] Пользовательские данные не создаются установщиком в пользовательском профиле при административной установке.
- [x] Обычный uninstall не удаляет `%LocalAppData%\NetPilot`.
- [x] Динамически установленный системный движок удаляется при uninstall.
- [x] Inno Setup закреплён на конкретной версии и immutable release.
- [x] SHA-256 Inno Setup закреплён в build script.
- [x] Authenticode Inno Setup проверяется до запуска.
- [x] GitHub CI выполняет реальную install/start/uninstall smoke-проверку.
- [ ] Публичный NetPilot code-signing сертификат — отсутствует на этапе alpha.
- [ ] Финальный аудит DLL search order / service binary permissions / reparse points — перед публичным релизом.
- [ ] Полная проверка upgrade с предыдущей установленной alpha-версии — до первой обновляемой публичной alpha.
