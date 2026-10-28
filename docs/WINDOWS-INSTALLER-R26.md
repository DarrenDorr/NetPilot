# Net.Pilot — Windows Installer Security R26

Статус: реализовано в ветке `feature/engine-bootstrap-r15`.

## Цель R26

Усилить первичную Windows-установку и удаление Net.Pilot так, чтобы служба LocalSystem не оставалась зарегистрированной при частичной/неуспешной установке, не получала автозапуск до успешной привязки пользователя и не могла закрепить неожиданную elevated/admin identity.

## R26.1 — fail-safe lifecycle службы

Удалён подход `RemoveServiceBestEffort`.

Перед удалением службы установщик теперь:
1. переводит её в `start= disabled`;
2. подтверждает остановку;
3. выполняет `sc delete`;
4. ожидает фактического исчезновения записи службы из SCM.

Если uninstall не может подтвердить безопасное удаление службы, удаление файлов останавливается. Это защищает от состояния, когда LocalSystem-служба остаётся зарегистрированной, а каталог программы уже частично удалён.

При ошибке первичного enrollment применяется тот же строгий cleanup.

## R26.2 — auto-start только после enrollment

На первичной установке последовательность теперь следующая:

`service start=demand → ручной старт → enrollment SID → service start=auto → recovery policy`

То есть до успешного enrollment служба не регистрируется как автоматически запускаемая.

После успешной привязки владельца включаются:
- `start= auto`;
- service failure/restart policy.

Если enrollment или финализация runtime policy завершаются ошибкой, свежая установка очищает:
- регистрацию службы;
- `authorized-user.sid`;
- `enrollment.nonce`;
- `enrollment.session`.

При обновлении существующая привязка владельца не заменяется.

## R26.3 — запрет elevated enrollment helper

Скрытый путь:

`NetPilot.exe --complete-install-enrollment <nonce>`

теперь проверяет текущий Windows token через `TokenElevation`.

Разрешено только состояние:
- `NotElevated`.

Запрещены fail-closed:
- `Elevated`;
- `Unknown`.

Это важно для сценария, когда пользователь вручную запускает Setup через «Запуск от имени администратора». В таком режиме Inno Setup не обязан иметь исходный non-elevated token для `ExecAsOriginalUser`; Net.Pilot поэтому не разрешает enrollment из elevated helper и не должен тихо привязывать установку к admin SID.

Обычный запуск GUI этой проверкой не ограничивается. Ограничение действует только для installer enrollment helper.

## Исправление self-hosted UAC evidence workflow

Предыдущий вариант manual workflow запускал Setup через PowerShell `-Verb RunAs`. Такой запуск заранее повышает процесс Setup и разрушает смысл проверки original-user boundary.

Теперь workflow:
1. стартует runner из интерактивной standard-user сессии;
2. запускает Setup без `-Verb RunAs`;
3. ожидает, что elevation запросит сам Inno Setup (`PrivilegesRequired=admin`);
4. после установки проверяет:
   - служба работает от `LocalSystem`;
   - после успешного enrollment `StartMode=Auto`;
   - служба `Running`;
   - `authorized-user.sid` совпадает с SID исходного standard user;
   - nonce/session markers удалены;
5. uninstall также запускается без предварительного PowerShell elevation.

### Важно

Наличие исправленного workflow не является доказательством PASS само по себе.

Реальная граница:

`standard user → Inno UAC → LocalSystem service → original-user SID binding`

считается подтверждённой только после ручного запуска workflow на изолированной интерактивной Windows VM с label `netpilot-uac-lab` и реальным UAC prompt.

Hosted Windows CI этого не доказывает.

## CI-инварианты R26

`scripts/security-policy-check.sh` теперь запрещает:
- возвращать `RemoveServiceBestEffort`;
- убирать проверяемый `DisableAndRemoveService`;
- убирать `start= disabled` из cleanup;
- убирать очистку свежих enrollment artifacts;
- жёстко регистрировать первичную службу как `start= auto obj= LocalSystem`;
- убирать `demand` до первичного enrollment;
- убирать финализацию `start= auto` после enrollment;
- разрешать enrollment token, отличный от явно `NotElevated`;
- реально запускать Setup в UAC evidence workflow через `Start-Process ... -Verb RunAs`.

## Подтверждённые автоматические проверки

Для кода R26 подтверждены:
- security-policy-check — PASS;
- .NET build — PASS;
- Core/IPC tests — PASS;
- Windows security tests — PASS;
- `TokenElevation` test — PASS;
- Windows GUI build — PASS;
- verified Inno Setup 7.1.0 installer build — PASS;
- installer artifact sanity — PASS;
- CodeQL — PASS.

Эти проверки не заменяют реальный self-hosted UAC evidence run.

## Что R26 не меняет

- IPC v3 не изменён;
- native engine по-прежнему не имеет `ADMIT`;
- `EngineInstall`/`EngineStart` без ADMIT запрещены;
- no-ADMIT updater transport остаётся без сети;
- no-ADMIT process launcher остаётся без `Process.Start`;
- произвольные Lua/BAT/PowerShell/raw CLI/URL/path не разрешены;
- merge/release в `main` по-прежнему требует HIGH_ASSURANCE review.

## Следующий этап

Следующий безопасный продуктовый этап — release-quality Windows UX и информация о версии/диагностике, не расширяющая privileged boundary. Реальный native adapter подключается только после нового fixed tagged-кандидата, полного native admission и явного статуса `ADMIT`.
