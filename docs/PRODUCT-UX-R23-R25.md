# Net.Pilot — Product UX R23–R25

Статус: реализовано в ветке `feature/engine-bootstrap-r15`.

## R23 — управление локальной историей диагностики

История диагностики остаётся ограниченным in-memory буфером и существует только в процессе GUI.

Добавлено:
- счётчик записей в памяти;
- кнопка «Очистить историю» в расширенном режиме;
- команда недоступна, когда история пуста;
- очистка удаляет внутренний буфер и отображаемые записи;
- после закрытия приложения история исчезает автоматически.

Не добавлено:
- экспорт;
- сохранение на диск;
- отправка на сервер;
- телеметрия;
- URL, IP, cookie, response body, exception message или содержимое трафика.

Любая будущая функция экспорта должна проходить отдельный privacy/security review и не считается продолжением текущей функции автоматически.

## R24 — read-only каталог продуктовых профилей

В расширенном режиме GUI показывает встроенные профили Net.Pilot:
- `standard` — «Стандартный»;
- `discord` — «Discord»;
- `youtube` — «YouTube»;
- `discord-youtube` — «Discord + YouTube».

Панель не содержит raw CLI, пути, Lua/BAT/PowerShell, URL или параметры конкретного native engine.

До появления допущенного (`ADMIT`) движка профили показываются как недоступные. GUI не предоставляет обходную кнопку запуска.

После появления admitted runtime та же модель сможет отображать `Доступен` / `Активен`, не меняя пользовательский контракт на Zapret-специфичный.

## R25 — Profile-only App API

Публичный контракт `IDashboardService` переведён с legacy Strategy-терминов на engine-neutral Profile API.

Наружу из App service layer теперь выходят:
- `ProfileSelectionResult`;
- `ProfileSelectionStatus`;
- `ProfileRuntimeState`;
- `ProfileOperationException`;
- `FindWorkingProfileAsync`;
- `GetProfileRuntimeStateAsync`.

Legacy `StrategySelectionEngine`, `IStrategyRuntime`, `StrategySelectionResult` и `StrategyRuntimeState` пока остаются только внутри реализации `DashboardService` как compatibility bridge к существующей Automation.

`MainWindowViewModel` и XAML больше не должны знать о legacy Automation/Strategy API.

## Зафиксированные проверки R25

1. Reflection-test проверяет публичные методы `IDashboardService` и запрещает:
   - методы с `Strategy` в имени;
   - типы из namespace `NetPilot.Automation` в return/parameter contract;
   - типы с `Strategy` в имени в публичном contract.

2. `scripts/security-policy-check.sh` запрещает в `src/NetPilot.App/ViewModels` и `src/NetPilot.App/Views`:
   - прямую зависимость от `NetPilot.Automation`;
   - `StrategySelection*`;
   - `StrategyRuntime*`;
   - старые `FindWorkingStrategy` / `GetStrategyRuntime` / `FindStrategyCommand`.

Внутренний bridge в `DashboardService` остаётся допустимым до отдельной миграции Automation.

## Что R23–R25 не меняют

- IPC v3 не изменён;
- `NetPilot.Service` не получает `EngineInstall`/`EngineStart` без ADMIT;
- no-ADMIT updater остаётся без сетевого transport;
- no-ADMIT service остаётся без рабочего `Process.Start` launcher;
- zapret2 v1.0.6 остаётся `BLOCK`;
- случайный upstream `master` не является заменой native admission;
- merge/release в `main` по-прежнему требует HIGH_ASSURANCE review и реального admitted component set для native-функций.

## Следующий этап

После подтверждения CI/CodeQL для R25 можно продолжать release-quality Windows UX и installer/recovery сценарии, сохраняя engine-neutral App contract. Реальный native adapter подключается только после нового fixed tagged release, полного admission и явного статуса `ADMIT`.
