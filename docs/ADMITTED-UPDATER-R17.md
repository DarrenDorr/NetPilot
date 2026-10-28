# Admitted Updater R17 — установка только локально допущенного релиза

Дата: 28.10.2026
Статус: R17.1–R17.2 реализованы; каталог допущенных native-релизов пока пуст.

## Цель

Net.Pilot не должен превращать новый upstream release в доверенный компонент автоматически.

Наличие релиза на GitHub, корректный HTTPS, официальный репозиторий и совпадающий SHA-256 подтверждают происхождение и целостность конкретного файла, но сами по себе не являются security admission.

Поэтому цепочка доверия разделена:

1. **локальный admission catalog** решает, какой exact release разрешён;
2. **release discovery** может только сообщить, что upstream опубликовал новую версию;
3. **download transport** умеет только скачать уже выбранный exact asset;
4. **transactional installer** применяет только ранее допущенную и повторно проверенную identity.

Сетевая metadata не может повысить статус неизвестного native-компонента до `ADMIT`.

## Архитектура

Привилегированная цепочка установки:

`NetPilot.Service`
→ `AdmittedZapretReleaseSource`
→ `IZapretReleaseAdmissionCatalog`
→ exact admitted release identity
→ `IZapretReleaseTransport`
→ `GithubZapretReleaseClient` как download transport
→ повторная проверка размера/SHA-256
→ безопасная распаковка
→ transactional installer.

`IZapretReleaseTransport` намеренно содержит только `DownloadVerifiedAssetAsync`. В нём нет API `Latest`, discovery или metadata lookup.

Существующий transactional installer, anti-downgrade, staging, Zip Slip/reparse protections, rollback/restore и повторная hash-проверка не ослабляются.

## R17.1 — локальный admission source

Добавлены:

- `IZapretReleaseAdmissionCatalog`;
- `NoAdmittedZapretReleaseCatalog`;
- `AdmittedZapretReleaseSource`.

`AdmittedZapretReleaseSource` возвращает только identity из локального catalog. Если catalog пуст, операция завершается кодом `NoAdmittedEngineRelease` до сетевого metadata/download вызова.

Перед передачей release в transport сравниваются:

- release ID;
- asset ID;
- версия;
- filename;
- размер;
- SHA-256;
- download URI.

Несовпадение → `ReleaseNotAdmitted`.

## R17.2 — разделение discovery и download transport

Добавлен узкий `IZapretReleaseTransport`.

`AdmittedZapretReleaseSource` зависит именно от него, а не от discovery-capable source. Поэтому обычный код admission-wrapper не имеет метода, которым можно случайно запросить upstream `latest`.

Production DI отдельно регистрирует:

- `GithubZapretReleaseClient`;
- тот же объект как `IZapretReleaseTransport`;
- локальный admission catalog;
- `AdmittedZapretReleaseSource` как источник для privileged updater.

GitHub-клиент не регистрируется напрямую как доверенный `IZapretReleaseSource`.

## Что входит в release identity

Для legacy Flowseal/Zapret release доверенная identity включает как минимум:

- upstream repository;
- release/tag version;
- release ID;
- release page;
- asset ID;
- точное имя ZIP;
- размер ZIP;
- SHA-256 ZIP.

Изменение любого из этих значений означает другую identity и требует повторного admission.

## Fail-closed состояние сейчас

На 28.10.2026 ни один исследованный native component set не имеет статуса `ADMIT`:

- Flowseal `1.10.4` / zapret1 `v72.9` — `BLOCK`;
- zapret2 `v1.0.6` — `BLOCK`.

Поэтому production DI использует `NoAdmittedZapretReleaseCatalog`.

Это второй независимый рубеж поверх `OperationPolicy`, который уже запрещает `EngineInstall` и `EngineStart` при отсутствии `ADMIT`.

Даже если capability установки будет ошибочно включена будущим рефакторингом, пустой admission catalog не позволит updater выбрать release для скачивания.

## Почему upstream `/releases/latest` не является install trust source

Upstream discovery разрешается только как информационная операция: например, чтобы позже показать «доступна новая версия, требуется проверка».

Новая версия не становится trusted автоматически.

Privileged install path должен получать exact identity только из локального admission catalog. Удалённый YAML/JSON, release metadata, GUI, IPC, URL пользователя или LLM не могут добавить новый trusted native release.

## Запрещено

R17 запрещает:

- автоматически устанавливать upstream `latest`;
- считать новый тег доверенным без admission;
- принимать release identity из GUI, IPC, сети, удалённого YAML/JSON или LLM;
- принимать пользовательский URL, asset ID, digest или путь к архиву;
- менять trusted release только потому, что upstream опубликовал новую версию;
- обходить `BLOCK` сборкой случайного upstream `master`.

## CI-инварианты

`scripts/security-policy-check.sh` проверяет:

- `GithubZapretReleaseClient` не зарегистрирован напрямую как доверенный `IZapretReleaseSource`;
- GitHub download зарегистрирован через узкий `IZapretReleaseTransport`;
- privileged updater проходит через `AdmittedZapretReleaseSource`;
- admission-wrapper получает `IZapretReleaseTransport`, а не discovery-capable source;
- при отсутствии `ADMIT` используется `NoAdmittedZapretReleaseCatalog`;
- при отсутствии `ADMIT` `EngineInstall` и `EngineStart` не выдаются службе;
- общий Avalonia `NetPilot.App` не зависит от Windows IPC.

## Тесты

R17 проверяет:

- пустой catalog → `NoAdmittedEngineRelease`, transport не вызывается;
- admitted identity возвращается без upstream metadata lookup;
- несовпадающая identity отклоняется до download;
- только exact admitted identity передаётся download transport;
- `IZapretReleaseTransport` не содержит discovery/latest API;
- constructor admission-wrapper принимает только narrow transport + admission catalog.

## Следующие этапы

### R17.3 — точные имена privileged API

Убрать legacy naming из install path:

- `GetLatestReleaseAsync` в admission source заменить на `GetAdmittedReleaseAsync`;
- `ApplyLatestAsync` заменить на `ApplyAdmittedAsync`;
- upstream discovery оставить отдельной read-only capability.

IPC v3 при этом не переименовывать без отдельной versioned migration.

### Первый реальный admitted release

Только после появления нового fixed tagged-кандидата и полного native admission:

1. зафиксировать exact release identity в локальном catalog;
2. проверить native inventory, provenance, signer/digests и актуальные advisories;
3. провести parser/runtime review;
4. определить минимальный необходимый component set;
5. только после `ADMIT` разрешить `EngineInstall`/`EngineStart` для конкретной identity.

## Security invariants

R17 усиливает:

- `NETP-INV-003` — native-компонент имеет immutable identity;
- `NETP-INV-004` — updater не превращает сеть/config в execution trust;
- `NETP-INV-013` — новый engine/release требует нового admission;
- `NETP-INV-016` — отсутствие admission приводит к безопасному отказу.

## Итог

Новый upstream release может быть **обнаружен**, но не может быть **установлен**, пока security admission не переведёт конкретную immutable identity в `ADMIT`.
