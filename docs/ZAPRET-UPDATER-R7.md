# Zapret Updater R7 — metadata GitHub и защищённая загрузка

## Назначение

R7 добавляет первый сетевой компонент updater для Zapret. Он намеренно остаётся узким: получает metadata только официального Flowseal release и скачивает только выбранный ZIP asset, после чего обязательно сверяет размер и SHA-256.

R7 ещё не переключает установленный `current` и не выдаёт IPC-команду установки/обновления.

## Источник metadata

Разрешён ровно один endpoint:

```text
https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest
```

Нет пользовательского URL, зеркала или источника из `.npconfig`.

HTTP-клиент работает:

- без cookies;
- без credentials;
- без GitHub token;
- без pre-authentication;
- без автоматической декомпрессии;
- без автоматических redirect;
- с ограничением заголовков;
- с connect timeout и общим network timeout.

Для публичного репозитория GitHub authentication не требуется.

## Проверка релиза

Metadata принимается только если:

- JSON не превышает 1 МиБ;
- `draft == false`;
- `prerelease == false`;
- `immutable == true`;
- `tag_name` присутствует;
- `html_url` точно соответствует официальной странице релиза Flowseal;
- список assets ограниченного размера;
- найден ровно один ZIP с именем `zapret-discord-youtube-<tag>.zip`;
- `state == uploaded`;
- `content_type == application/zip`;
- размер входит в лимит R6;
- есть корректный SHA-256 digest;
- API URL asset точно принадлежит `Flowseal/zapret-discord-youtube` и содержит тот же asset ID;
- `browser_download_url` проходит allowlist R6.

Неизвестные дополнительные поля GitHub не используются как доверенные решения.

## Загрузка asset

NetPilot не делает обычный GET по произвольному `browser_download_url`.

Используется точный API endpoint по проверенному ID:

```text
https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/assets/<asset-id>
```

Заголовок `Accept: application/octet-stream`.

Согласно документации GitHub endpoint публичного release asset может вернуть бинарные данные (`200`) либо redirect (`302`).

Автоматический redirect отключён.

Если получен `302`, NetPilot вручную принимает только:

```text
https://release-assets.githubusercontent.com/...
```

У redirect обязательно:

- HTTPS;
- стандартный порт;
- отсутствие `userinfo`;
- не более одного redirect.

Query у `release-assets.githubusercontent.com` допускается, поскольку GitHub использует подписанные временные параметры загрузки. Эти параметры не сохраняются в логах.

Любой другой хост или дополнительный redirect приводит к отказу.

## Ограничение данных

Перед записью проверяется `Content-Length`, если сервер его указал.

Поток копируется с жёстким лимитом:

- нельзя скачать больше размера из metadata;
- нельзя скачать больше глобального лимита R6;
- нельзя скачать меньше заявленного размера и считать файл успешным.

Файл создаётся как `source.zip` через `FileMode.CreateNew` в пустом download staging.

Частичный файл удаляется после ошибки.

После завершения файл повторно открывается только для чтения и проходит `Sha256Verifier` R6.

До успешного SHA-256 файл не считается доверенным входом для распаковки.

## Приватность

Updater не отправляет:

- NetPilot настройки;
- список ресурсов пользователя;
- стратегии пользователя;
- диагностику;
- логи;
- SID;
- данные ChatGPT/OpenAI;
- cookies браузера;
- токены GitHub.

Сетевой запрос содержит только стандартные технические HTTP-заголовки updater и обращение к публичному GitHub endpoint.

## Следующий этап

R8 должен построить транзакционную установку поверх R6/R7:

1. отдельный защищённый download staging;
2. SHA-256;
3. отдельный extraction staging;
4. `SafeZipExtractor`;
5. `ZapretPackageLayoutValidator`;
6. остановка активного Zapret;
7. подготовка rollback предыдущего `current`;
8. переключение только реальных каталогов без junction/symlink;
9. повторная проверка нового `current`;
10. автоматический rollback при ошибке переключения;
11. сохранение системной rollback-копии без пользовательских секретов.

Только после R8 можно добавлять привилегированную IPC-команду установки/обновления движка.
