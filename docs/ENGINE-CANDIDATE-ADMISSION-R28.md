# R28 — Engine Candidate Admission & Fork Decision

Дата фиксации: 28.10.2026.

## Цель

R28 переводит выбор сетевого движка из уровня предположений в формальный admission-процесс. На этом этапе NetPilot **не включает ни один native engine в production runtime**. Мы фиксируем точные upstream-версии, архитектурные риски, лицензионные ограничения, supply-chain evidence и требования к возможным собственным форкам.

Исходная точка R28 — подтверждённый R27 commit `72ee704536aba5d4b5aa320eb156008e5e6d8e3c`. Повторные CI и CodeQL для R27 завершились успешно.

## Неподвижные правила безопасности

1. `REVIEW`, `UNKNOWN` и `BLOCK` никогда не означают `ADMIT`.
2. Пока в `security/native-admission` нет `admission_status: ADMIT`, production Service остаётся fail-closed: без `EngineInstall`, `EngineStart`, сетевого updater transport и реального `Process.Start` для native engine.
3. Upstream `latest` или `master` не являются источниками доверия.
4. Ни один пользовательский URL, путь, raw CLI, BAT, PowerShell или Lua не становится привилегированной командой.
5. Любой собственный форк имеет exact upstream anchor, минимальный patchset и отдельную версию NetPilot fork.
6. Обновление upstream проходит как новый reviewed delta; автоматического `upstream -> production` не будет.
7. Реальный запуск и интеграция начинаются только после полного admission конкретного component set.

## Кандидаты после R28.1

| Кандидат | Лицензия | Архитектура | Windows | Linux | UDP/Discord | Решение по форку | R28 status |
| --- | --- | --- | --- | --- | --- | --- | --- |
| zapret2 v1.0.6 + exact UAF fix | MIT | системный packet engine | да | да | да | **основной кандидат на owned hardened fork** | REVIEW / gate BLOCK |
| ByeDPI v0.17.3 + curated backports | MIT | локальный SOCKS proxy | да | да | SOCKS UDP есть; routing отдельный | **возможный минимальный fork для Android/secondary** | REVIEW / gate BLOCK |
| DPIBreak v0.6.2 | GPL-3.0-or-later | WinDivert / NFQUEUE | да | да | не покрывает полный Discord UDP scope | **upstream-first, не форкать без blocker** | REVIEW / gate BLOCK |

## 1. zapret2 — основной кандидат

zapret2 лучше всего совпадает с существующей engine-neutral архитектурой NetPilot: системный перехват, Windows/Linux и функциональность TCP/UDP, необходимая для будущих профилей `standard`, `discord`, `youtube`, `discord-youtube`.

Официальный v1.0.6 остаётся **навсегда BLOCK**: в exact release commit `221d7dff24bfec0cd5ac0a5070b8acef2008af3d` был воспроизводимый heap-use-after-free. Этот manifest не переоткрывается.

Upstream fix `4a33055448ce5f4e7acc704e1fbe0d8a8a6aa08d` является прямым дочерним коммитом release commit и узко исправляет reassembly UAF. Поэтому допустимая база кандидата:

`v1.0.6 exact commit -> exact upstream UAF fix -> минимальный NetPilot hardening patchset`.

Не используется текущий `master`.

### Результаты static review

Зафиксированы:

- `docs/ZAPRET2-STATIC-ADMISSION-R28.1.md`;
- `docs/ZAPRET2-RESOURCE-HARDENING-R28.1.md`;
- `docs/ZAPRET2-CONNTRACK-SATURATION-R28.2.md`;
- `docs/ZAPRET2-CONTROLLED-BUILD-R28.2.md`.

Остающиеся blockers:

- агрегатный conntrack/ipcache resource budget;
- отдельный byte budget delayed packet queue;
- persistent auto-hostlist выключен для первого production profile;
- experimental `ipcache-hostname` выключен;
- пользовательский Lua/raw CLI/path surface недоступны;
- controlled build с жёстким pinning toolchain/dependencies/WinDivert;
- новый Windows native inventory и runtime evidence.

**Решение R28.1:** zapret2 — единственный из трёх, для которого прямо сейчас оправдан собственный **минимальный hardened fork**, но его release/runtime не создаётся до R28.2 evidence.

## 2. ByeDPI — Android/secondary кандидат

Static review: `docs/BYEDPI-STATIC-ADMISSION-R28.1.md`.

Exact v0.17.3 имеет MIT-лицензию, Windows/Linux build, SOCKS proxy и UDP association. При этом stock configuration не подходит NetPilot:

- listener default `0.0.0.0:1080`;
- file/path/raw-data CLI (`pidfile`, `cache-dump`, `hosts`, `ipset`, `fake-data` и др.);
- SOCKS не является прозрачным system-wide Windows engine;
- transparent routing на Windows и `VpnService/TUN` на Android — отдельные security boundaries.

Положительный момент: v0.17.3 уже имеет bounded connection count — default `max_open=512`, parser требует положительное значение ниже внутреннего верхнего предела.

### Решение по post-release delta

Между `v0.17.3` и `ba532298...` — 38 commits. Это не только bugfixes: upstream добавлял external SOCKS, cache-file/group mechanics, Shadowsocks plugin, reconnect/group changes, raw proxy mode и другие новые поверхности.

Поэтому **полный post-release head отклонён как первый fork base**.

Правильная модель:

`exact v0.17.3 -> individually reviewed curated backports -> loopback-only/policy hardening -> controlled build`.

Кандидаты на отдельный patch review:

- `57f9606f...` — UDP ICMP errors не должны фатально закрывать tunnel;
- `4ce80972...` — строгая split-position/error validation;
- `a5df2608...` — TCP send error handling, но поверх поздней архитектуры, поэтому review/reimplementation;
- `ba532298...` — cleanup pointer только если соответствующая поздняя code path вообще войдёт в fork.

**Решение R28.1:** ByeDPI остаётся №2 и сильным Android-кандидатом. Если понадобится fork, он будет минимальным от exact v0.17.3, а не копией current master.

## 3. DPIBreak — secondary HTTPS/YouTube fallback

Static review: `docs/DPIBREAK-STATIC-ADMISSION-R28.1.md`.

DPIBreak v0.6.2 имеет сильные признаки release provenance:

- signed annotated tag;
- SHA-256 release assets;
- подпись checksum-файла;
- buildinfo с exact commit/toolchain;
- Windows и Linux assets.

Функционально движок ориентирован на HTTPS/TLS ClientHello, поэтому не заменяет zapret2 для полноценного Discord/UDP scope.

Дополнительные границы:

- Windows использует in-tree Rust wrapper + native WinDivert/driver;
- open issue #24 уточняет Linux NFQUEUE/GSO/MTU semantics и требует runtime matrix;
- upstream release workflow всё ещё использует floating actions/toolchain refs и скачивает WinDivert без нашего pre-use expected hash check;
- GPL-3.0-or-later требует отдельной component/license boundary относительно MIT Core.

**Решение R28.1:** не создавать форк заранее. Сначала controlled admission exact upstream release. Форк создаётся только если подтверждённый blocker нельзя решить adapter policy или upstream fix.

## Финальное решение static phase R28.1

### Приоритет 1 — zapret2

Создать и сопровождать **owned minimal hardened fork** только от exact controlled base:

`221d7dff... -> 4a330554... -> NetPilot hardening patches`.

Это основной путь Windows/Linux.

### Приоритет 2 — ByeDPI

Подготовить возможный **minimal curated fork** от exact v0.17.3 для Android/secondary use. Не использовать весь post-release master. Windows transparent routing и Android TUN/VpnService проходят отдельный admission.

### Приоритет 3 — DPIBreak

**Не форкать сейчас.** Отслеживать upstream как secondary HTTPS/YouTube fallback; выполнять controlled admission точного tagged release.

## R28.2 — controlled provenance/build gate

Документ: `docs/ZAPRET2-CONTROLLED-BUILD-R28.2.md`.

Первый R28.2 gate добавлен как:

`.github/workflows/native-candidate-provenance.yml`.

Он **не компилирует и не запускает native code**. Его задача — доказать exact zapret2 source anchor до применения собственных patch'ей:

- HEAD = `4a330554...`;
- parent = `221d7dff...`;
- tree = `5c2a7778...`;
- delta содержит ровно `nfq2/desync.c`;
- присутствуют ожидаемые строки UAF fix;
- создаётся evidence artifact с SHA-256.

Workflow имеет только `contents: read`, checkout выполняется без сохранения credentials, actions pin'нуты по full commit SHA, runner — `ubuntu-24.04`.

### Conntrack saturation semantics

Проверка exact `play_prolog()` обнаружила важную особенность: `ConntrackPoolFeed()==false` не означает pass-through. Без conntrack main path может продолжить `dp_find()` и stateless desync.

Поэтому простой patch `if cap reached -> return false` отклонён.

Новая целевая модель разделяет:

- обычное отсутствие track;
- успешный feed;
- resource exhaustion.

Только resource exhaustion должен останавливать main desync path с уже установленным `VERDICT_PASS` до `dp_find()`.

### Patch 0001

Добавлен первый behavior-preserving patch:

`security/native-admission/patches/zapret2-r28/0001-explicit-conntrack-feed-result.patch`.

Он пока **не вводит лимиты**. Он только заменяет неоднозначный `bool` на явный внутренний result `NONE/OK` и обновляет два известных call sites (`desync.c`, `lua.c`) без намеренного изменения packet behavior.

`Native Candidate Provenance` выполняет `git apply --check` этого patch против exact source tree и проверяет, что patch затрагивает только:

- `nfq2/conntrack.c`;
- `nfq2/conntrack.h`;
- `nfq2/desync.c`;
- `nfq2/lua.c`.

На текущей голове PR соответствующий workflow запущен, но его результат считается evidence только после `completed/success`; до этого patch 0001 имеет статус `APPLY_CHECK_REQUIRED`.

### Следующий R28.2 patch gate

После успешного source/patch provenance:

1. расширить result состоянием resource exhaustion;
2. добавить aggregate conntrack budget с fail-safe `VERDICT_PASS` только для нового flow при saturation;
3. добавить delayed queue byte accounting с overflow-safe проверкой;
4. `git apply --check` и source assertions выполнять до компиляции;
5. pin exact compiler/Cygwin/Lua/LuaJIT/WinDivert inputs и SHA-256 до использования;
6. собрать Windows x64 candidate в controlled CI;
7. сформировать build manifest/SBOM и SHA-256 каждого EXE/DLL/SYS;
8. проверить PE metadata/signatures/driver chain;
9. прогнать static/sanitizer/fuzz regression evidence;
10. только затем переходить к isolated runtime R28.3.

ByeDPI controlled patchset/build идёт вторым потоком после фиксации backport set. DPIBreak не требует собственного fork build на этом шаге; для него нужен controlled rebuild exact upstream release.

## R28.3 — isolated runtime evidence

Только после безопасной сборки конкретного кандидата:

- start/stop/restart;
- abnormal termination и crash cleanup;
- отсутствие оставшихся driver/firewall/listener state;
- HTTPS/YouTube;
- Discord TCP/UDP для движков, которые заявляют соответствующий scope;
- bounded resource tests;
- coexistence с типичными VPN/security products;
- повторный запуск после сбоя.

## Модель сопровождения собственных форков

Используем:

`точный upstream ref -> reviewed delta -> NetPilot patchset -> CI/security evidence -> admission manifest -> release`.

Не используем:

`upstream latest -> автоматическое скачивание -> запуск`.

Для каждого нашего форка:

- upstream source остаётся максимально близок к оригиналу;
- каждый patch имеет причину и regression test;
- upstream fixes подтягиваются только как reviewed commits/patches;
- новая сборка получает отдельную version identity и SHA-256;
- updater доверяет только admitted manifest, а не GitHub `latest`;
- NetPilot Core остаётся engine-neutral и не знает raw CLI движка.

## Что R28 намеренно не делает

- не включает `EngineStart`/`EngineInstall`;
- не добавляет `Process.Start` для новых движков;
- не скачивает native engine в LocalSystem runtime;
- не создаёт публичный SOCKS listener;
- не разрешает Lua/raw CLI/path/file конфигурацию;
- не делает автоматический fork sync;
- не считает отсутствие CVE/advisory доказательством безопасности;
- не меняет статус старого zapret2 v1.0.6 BLOCK;
- не считает R28.1/R28.2 design эквивалентом полного ADMIT;
- не сливает PR #30 в `main`.

## Текущее состояние

R28.1 static decision зафиксирован для всех трёх кандидатов. Все они остаются `REVIEW / gate BLOCK` и **не исполняются NetPilot**.

R28.2 дошёл до exact source provenance, проверки patch applicability и первого behavior-preserving patch 0001. Следующий технический шаг — получить успешное CI evidence, затем подготовить resource-exhaustion/count/byte-budget patches без нарушения проверенной fail-safe semantics.
