# Zapret Updater R9 — полный внутренний pipeline обновления

## Назначение

R9 объединяет уже проверенные слои R6, R7 и R8 в одну внутреннюю операцию:

```text
официальный GitHub release
        ↓
строгая metadata R7
        ↓
защищённая загрузка ZIP
        ↓
повторная локальная SHA-256 проверка
        ↓
безопасная распаковка R6
        ↓
проверка структуры Zapret
        ↓
остановка runtime через узкий gate
        ↓
транзакционный Apply R8
        ↓
current / previous / rollback
```

R9 пока не подключён к пользовательскому IPC и не создаёт кнопку «Обновить». Это намеренно: сначала весь pipeline должен пройти CI, Windows tests и CodeQL.

## Главное правило границы привилегий

`ApplyLatestAsync()` не принимает от GUI или другого вызывающего кода:

- URL;
- путь к ZIP;
- SHA-256;
- release ID;
- версию;
- путь staging;
- произвольные аргументы процесса.

Оркестратор сам получает latest release через `IZapretReleaseSource`. Production-реализация — `GithubZapretReleaseClient`, которая жёстко привязана к `Flowseal/zapret-discord-youtube`.

## Уникальные transaction-каталоги

Для каждой попытки создаются два случайных прямых потомка фиксированного:

```text
%ProgramFiles%\NetPilot\Engines\Zapret\.staging
```

Форма:

```text
txn-<guid>-download\
txn-<guid>-extract\
```

Загрузчик обязан вернуть ровно:

```text
txn-...-download\source.zip
```

Другой путь или другое имя отклоняется как `UntrustedDownloadedAssetPath`.

## Повторная проверка скачанного файла

Даже если release source уже выполнил SHA-256, R9 не доверяет этому слепо. Перед распаковкой он повторно проверяет:

1. что файл существует в принадлежащем операции download root;
2. что это не reparse point;
3. что размер точно совпадает с официальной metadata;
4. что SHA-256 совпадает с digest release asset.

Подмена после загрузки должна завершиться `DigestMismatch` до остановки движка.

## Минимизация простоя

Активный Zapret не останавливается во время:

- получения metadata;
- загрузки;
- SHA-256 проверки;
- распаковки;
- проверки package layout.

`IZapretUpdateRuntimeGate.EnsureStoppedForUpdateAsync()` вызывается только после успешного прохождения этих этапов.

Таким образом сетевой движок прерывается только непосредственно перед файловым переключением `current`.

## Проверка версии до скачивания

Если `current` уже существует, R9 сначала:

- проверяет структуру установленного дерева;
- проверяет manifest NetPilot;
- сравнивает версии.

Равная версия отклоняется как `VersionAlreadyInstalled`, downgrade — как `DowngradeRejected`. В обоих случаях ZIP не скачивается и runtime не останавливается.

## Runtime gate

Updater не зависит от Windows engine-проекта. Вместо этого используется узкий интерфейс:

```text
IZapretUpdateRuntimeGate
    EnsureStoppedForUpdateAsync()
```

Это не generic command callback. Реализация для Windows позже будет адаптером к уже существующему `ZapretEngineController.StopAsync()`.

R9 не решает политику автоматического перезапуска стратегии после обновления. До подключения к UI/IPC это внутренний слой без пользовательского воздействия.

## Повреждённый ZIP

Ошибки формата `ZipArchive` нормализуются в:

```text
InvalidZipArchive
```

Внутренние исключения .NET не используются как пользовательский протокол ошибок updater.

## Безопасный cleanup

После успеха или ошибки R9 пытается удалить только собственные transaction-каталоги.

Перед удалением:

- путь должен быть прямым потомком `.staging`;
- корень проверяется на reparse point;
- дерево обходится вручную сверху вниз;
- reparse point проверяется **до** помещения каталога в очередь обхода.

`SearchOption.AllDirectories` для security-sensitive cleanup больше не используется также в R8, чтобы не возникало риска обхода junction/symlink до проверки.

Если cleanup невозможно выполнить безопасно, подозрительное дерево не форсируется и не проходится по ссылке. Успешный Apply возвращает признак `TemporaryCleanupCompleted`, чтобы будущий сервисный слой мог показать/запланировать безопасную очистку.

## Тесты R9

Добавлены сценарии:

- первая установка через полный pipeline;
- upgrade и сохранение `previous`;
- одинаковая версия без download/stop;
- malformed ZIP до остановки runtime;
- повторная SHA-256 проверка после намеренной подмены;
- отказ пути asset вне transaction root;
- ошибка runtime gate без переключения `current`;
- очистка transaction staging.

## Что намеренно остаётся выключенным

R9 не включает:

- `UpdateApply` через IPC;
- `EngineInstall` через IPC;
- автоматическую установку обновлений;
- автоматический restart стратегии после обновления;
- UI подтверждения;
- постоянные системные backup;
- repair.

## Следующий этап

После зелёных CI / Windows / CodeQL:

1. добавить Windows-адаптер runtime gate к `ZapretEngineController`;
2. определить безопасную политику restart после обновления;
3. разделить UI/API на `CheckUpdate` и подтверждённый `ApplyUpdate`;
4. только после этого открыть одну высокоуровневую update capability в службе;
5. затем добавить системные backup/retention и repair.
