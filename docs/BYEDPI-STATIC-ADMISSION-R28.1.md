# R28.1 — статический admission-аудит ByeDPI v0.17.3

Дата: 28.10.2026.

Проверяемая базовая точка: `hufrea/byedpi@v0.17.3`, commit `7efde1b1296eaaa187b70e951894dde17527489c`.

Рассмотренный post-release диапазон: `v0.17.3..ba532298de7b28cfe854aea83d061369d13ca290` — 38 upstream commits.

Этот документ **не присваивает ADMIT**, не разрешает запуск ByeDPI из NetPilot и не разрешает автоматическое подтягивание upstream `master`.

## Итог текущего прохода

Статус: **REVIEW / release gate BLOCK**.

ByeDPI остаётся сильным кандидатом на Android/secondary backend благодаря MIT-лицензии, компактной C-кодовой базе, SOCKS-модели и поддержке UDP association. Однако неизменённый upstream v0.17.3 нельзя включить в production NetPilot: его сетевой listener по умолчанию открыт на всех интерфейсах, CLI принимает пользовательские файлы/пути/raw data, а system-wide маршрутизация на Windows требует отдельной доверенной границы.

Главный вывод review post-release delta: **не брать все 38 commits целиком**. После v0.17.3 upstream добавлял не только исправления, но и новые возможности и поверхности — external SOCKS, cache file mechanics, Shadowsocks plugin, новые group/cache режимы, raw proxy mode и другие архитектурные изменения. Для первого NetPilot-кандидата безопаснее exact v0.17.3 + минимальный набор отдельно проверенных backport-патчей.

## 1. Listener — v0.17.3 по умолчанию не подходит для NetPilot

В exact `main.c` v0.17.3 документация CLI указывает:

- `--ip` — default `0.0.0.0`;
- `--port` — default `1080`.

Для универсальной CLI-утилиты это допустимая upstream-модель, но для NetPilot означает ненужный unauthenticated SOCKS listener на LAN/WAN-интерфейсах.

Решение NetPilot:

- production adapter всегда задаёт loopback bind явно;
- Windows — `127.0.0.1` и/или отдельно проверенный `::1`;
- Android — listener доступен только внутри локального VPN/TUN routing design;
- UI, IPC и config не содержат параметра «слушать внешний интерфейс»;
- `0.0.0.0`, `::`, LAN IP и произвольный bind address запрещены;
- regression test должен подтверждать отсутствие listener на внешних интерфейсах.

Это обязательный blocker для неизменённой интеграции upstream.

## 2. Connection limit — уже bounded, но policy остаётся внутренней

Exact v0.17.3 имеет:

- default `max_open = 512`;
- CLI `--max-conn`;
- parser принимает только значение `> 0` и `< 0xffff/2`.

Это положительный факт: stock v0.17.3 не использует очевидный режим `0 = unlimited` для connection count.

Но NetPilot всё равно не передаёт пользователю raw `--max-conn` и не наследует автоматически upstream default как продуктовый security budget.

До ADMIT необходимо измерить:

- обычный браузинг;
- YouTube 1080p/4K;
- Discord text/voice при использовании UDP path;
- комбинированную нагрузку;
- burst новых соединений;
- поведение при достижении лимита;
- память на TCP/UDP state и buffers;
- корректное освобождение state после закрытия/crash.

Значение NetPilot limit выбирается как внутренняя typed policy и не становится произвольным пользовательским CLI.

## 3. File/path/raw-data CLI — запрещён на privileged boundary

Exact v0.17.3 поддерживает, среди прочего:

- `--pidfile <filename>`;
- `--cache-dump <file|->`;
- `--hosts <file|:str>`;
- `--ipset <file|:str>`;
- `--fake-data <file|:str>`;
- произвольные desync offsets/strings и другие параметры стратегии.

NetPilot не будет экспортировать эту поверхность через GUI/IPC/config.

Первый admitted adapter должен получать только typed `EngineProfileId`/внутренний plan и формировать фиксированный `ArgumentList` из allowlist.

Правила:

- пользовательские paths/files/raw-data запрещены;
- project-owned data допускаются только после hash pinning и admission;
- никакого shell concatenation;
- неизвестный профиль/параметр → fail closed;
- debug/output не должен содержать секреты, полные пользовательские пути или сетевой payload.

## 4. Windows system-wide routing — отдельный security boundary

ByeDPI — локальный SOCKS proxy. Сам запуск ByeDPI не делает его прозрачным system-wide engine Windows.

Чтобы профиль NetPilot `discord`/`discord-youtube` реально покрывал приложения, которые не умеют самостоятельно использовать SOCKS, потребуется отдельный routing layer — например TUN/tun2socks либо другой проверенный Windows mechanism.

Этот routing layer **не считается частью ByeDPI admission автоматически**. Для него нужен отдельный review:

- происхождение и лицензия;
- привилегии;
- DNS/UDP semantics;
- loop prevention;
- route/adapter cleanup;
- crash recovery;
- отсутствие произвольной маршрутизации от пользователя;
- отсутствие открытого proxy/listener.

Поэтому ByeDPI не заменяет zapret2 как основной Windows backend на текущем этапе.

## 5. Android — сильная архитектурная позиция, но отдельный admission

Proxy-модель ByeDPI естественно сочетается с Android `VpnService` + TUN routing без root. Это делает проект сильным кандидатом для будущего Android NetPilot.

Но desktop admission нельзя автоматически переносить на Android. Отдельно должны быть проверены:

- `VpnService` lifecycle;
- TUN-to-SOCKS component;
- защита от routing loops;
- DNS leakage;
- UDP association;
- foreground service/OS lifecycle;
- shutdown/restart cleanup;
- отсутствие внешнего listener;
- native ABI builds и supply-chain.

## 6. Почему нельзя взять post-release head `ba532298...` целиком

Сравнение `v0.17.3..ba532298...` показывает 38 commits. Среди них есть полезные bugfixes, но одновременно происходили существенные архитектурные изменения:

- `External socks`;
- загрузка cache file;
- отдельные cache/group настройки;
- Shadowsocks plugin;
- новые reconnect/group механизмы;
- raw proxy mode;
- изменения структуры main/strategy logic.

Если взять head целиком, мы увеличим attack surface и объём кода для доказательства безопасности значительно сильнее, чем требуется для первого NetPilot backend.

Решение: **baseline = exact v0.17.3**, а post-release изменения рассматриваются как отдельные patch candidates.

## 7. Кандидаты на минимальные backport-патчи

Ни один commit ниже не cherry-pick'ится автоматически. Сначала проверяется его patch и применимость к exact v0.17.3; если между release и fix произошёл рефакторинг, в NetPilot fork переносится минимальный эквивалент исправления с отдельным тестом.

### 7.1 `57f9606f...` — UDP ICMP error handling

Commit `57f9606feba6e06f9974e8502a21ce6672251548` меняет `on_udp_tunnel()` так, чтобы `EMSGSIZE`/`ECONNREFUSED`, возникающие в UDP path, не закрывали весь tunnel как fatal error.

Для NetPilot это релевантно, если ByeDPI будет использоваться для UDP/Android. Exact v0.17.3 имеет соответствующий старый путь, где `recvfrom/send` error приводит к `return -1`.

Статус: **высокий приоритет для controlled backport**, но только после платформенных regression tests.

### 7.2 `4ce80972...` — input/error validation

Commit `4ce80972fafe055cd64b857a6a48333179636ae9`:

- отклоняет split position с непарсенным хвостом;
- исправляет использование аргумента `fname` для pidfile;
- проверяет ошибку `write()` pidfile.

Даже если raw split CLI не будет доступен пользователю NetPilot, строгая валидация полезна как defense-in-depth для project-owned profile compiler.

Статус: **кандидат на backport/эквивалентный patch**, не обязательное автоматическое включение всего commit chain.

### 7.3 `a5df2608...` — TCP send error handling

Commit `a5df2608ab99cac9d5b21d57d08e674af7852726` добавляет обработку ошибок `send()` и унифицирует переходы для reset/refused/timeout/unreachable.

Patch сделан уже поверх более поздней архитектуры. Поэтому его нельзя считать безопасным clean cherry-pick на v0.17.3 без dependency/diff review.

Статус: **review/reimplementation candidate**.

### 7.4 `ba532298...` — cleanup pointer

Commit `ba532298de7b28cfe854aea83d061369d13ca290` сохраняет pointer в список `need_free` перед разбором hosts data.

Этот код относится к post-release механике, которая сама менялась после v0.17.3. Он не является основанием тащить весь post-release head и не считается автоматически применимым к release baseline.

Статус: **review only; применять только если соответствующая code path войдёт в выбранный fork tree**.

## 8. Предлагаемый fork model ByeDPI

Если ByeDPI проходит дальнейший admission, первый fork строится так:

`v0.17.3 exact commit -> минимальные проверенные backports -> NetPilot loopback/policy hardening -> controlled build -> tests -> admission`.

Не так:

`v0.17.3 -> merge 38 commits -> current master`.

Минимальный NetPilot patchset должен включать только то, что нельзя надёжно обеспечить adapter policy:

1. собственную version identity (`17.3-np.N` или эквивалент);
2. при необходимости compile/runtime guard, запрещающий non-loopback listener в NetPilot build;
3. выбранные reliability/security fixes после отдельного patch review;
4. тесты resource cleanup и UDP error behavior.

File/path/raw strategy surface лучше закрывать на adapter boundary, а не массово вырезать из upstream source, если она остаётся недостижимой в NetPilot build/runtime.

## 9. Supply-chain требования

До ADMIT необходимо:

- собрать exact source tree в controlled CI;
- pin compiler/toolchain и build actions;
- зафиксировать dependency/toolchain identity;
- получить SHA-256 итогового Windows/Linux/Android native artifact;
- провести PE/signature metadata review для Windows;
- использовать собственный immutable release identity;
- не доверять GitHub `latest`/mutable release как runtime source;
- updater NetPilot получает только заранее admitted exact asset metadata.

## 10. Следующие проверки

1. Выбрать минимальный backport set против exact v0.17.3 и проверить каждый patch независимо.
2. Добавить loopback-only regression harness.
3. Проверить parser/strategy inputs sanitizers/fuzz, где возможно.
4. Провести нагрузочный тест connection/UDP state и выбрать product limit.
5. Спроектировать отдельно Android VpnService/TUN path.
6. Не проектировать Windows transparent routing как скрытое продолжение ByeDPI — оформить его отдельным admission component.

## Решение R28.1 по ByeDPI

**ByeDPI остаётся кандидатом №2 и сильнейшим текущим кандидатом для Android/secondary proxy backend.**

Полный post-release head `ba532298...` как fork base **отклонён**: delta слишком широк и включает новые возможности, не нужные первому NetPilot release.

Предпочтительная стратегия — exact v0.17.3 + curated bugfix backports + loopback-only hardening. До controlled build, sanitizer/runtime evidence и отдельного routing admission статус остаётся **REVIEW / BLOCK**.
