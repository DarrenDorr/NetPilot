# R28 — Engine Candidate Admission & Fork Decision

Дата фиксации: 28.10.2026.

## Цель

R28 переводит выбор сетевого движка из уровня предположений в формальный admission-процесс. На этом этапе NetPilot **не включает ни один native engine в production runtime**. Мы фиксируем точные upstream-версии, архитектурные риски, лицензионные ограничения, supply-chain evidence и требования к возможным собственным форкам.

Исходная точка R28 — подтверждённый R27 commit `72ee704536aba5d4b5aa320eb156008e5e6d8e3c`. Повторные CI и CodeQL для этой точки завершились успешно.

## Неподвижные правила безопасности

1. `REVIEW`, `UNKNOWN` и `BLOCK` никогда не означают `ADMIT`.
2. Пока в `security/native-admission` нет `admission_status: ADMIT`, production Service остаётся fail-closed: без `EngineInstall`, `EngineStart`, сетевого updater transport и реального `Process.Start` для native engine.
3. Upstream `latest` или `master` не являются источниками доверия.
4. Ни один пользовательский URL, путь, raw CLI, BAT, PowerShell или Lua не становится привилегированной командой.
5. Любой собственный форк имеет exact upstream anchor, минимальный patchset и отдельную версию NetPilot fork.
6. Обновление upstream проходит как новый reviewed delta; автоматического `upstream -> production` не будет.
7. Реальный запуск и интеграция начинаются только после полного admission конкретного component set.

## Кандидаты

| Кандидат | Лицензия | Архитектура | Windows | Linux | UDP/Discord | Роль в NetPilot | R28 status |
| --- | --- | --- | --- | --- | --- | --- | --- |
| zapret2 v1.0.6 + exact UAF fix | MIT | системный packet engine | да | да | да | основной кандидат Windows/Linux | REVIEW / gate BLOCK |
| ByeDPI v0.17.3 + post-release delta | MIT | локальный SOCKS proxy | да | да | SOCKS UDP есть, но Windows routing отдельный | Android/secondary | REVIEW / gate BLOCK |
| DPIBreak v0.6.2 | GPL-3.0-or-later | WinDivert / NFQUEUE | да | да | не покрывает полный Discord UDP scope | HTTPS/YouTube fallback | REVIEW / gate BLOCK |

## 1. zapret2 — основной кандидат

### Почему он впереди

zapret2 лучше всего совпадает с существующей engine-neutral архитектурой NetPilot: системный перехват, Windows/Linux и функциональность TCP/UDP, необходимая для будущих профилей `standard`, `discord`, `youtube`, `discord-youtube`.

Официальный v1.0.6 остаётся **навсегда BLOCK**: в exact release commit `221d7dff24bfec0cd5ac0a5070b8acef2008af3d` был воспроизводимый heap-use-after-free. Этот manifest не изменяем и не «переоткрываем».

### Почему появился отдельный fork candidate

Upstream fix `4a33055448ce5f4e7acc704e1fbe0d8a8a6aa08d` является прямым дочерним коммитом v1.0.6 release commit и изменяет только путь reassembly в `nfq2/desync.c`. Поэтому у нас есть узкая и проверяемая база:

`v1.0.6 exact commit -> exact upstream UAF fix -> минимальный NetPilot hardening patchset`.

Это значительно безопаснее, чем брать текущий постоянно меняющийся `master`.

### Что ещё блокирует ADMIT

- нужен product-level предел conntrack/ipcache и безопасное поведение при насыщении;
- persistent auto-hostlist из packet-derived hostname должен быть отключён или отдельно защищён;
- экспериментальный `ipcache-hostname` остаётся выключенным без отдельного review;
- пользовательский Lua, `luaexec`, raw CLI и произвольные пути запрещены;
- WinDivert/Cygwin и итоговый patched binary требуют нового native inventory;
- нужен controlled build, static/sanitizer/fuzz review и изолированные runtime tests.

**Предварительное решение:** первый кандидат на собственный hardened fork, но пока не ADMIT.

## 2. ByeDPI — сильный кандидат для Android и secondary backend

ByeDPI v0.17.3 имеет MIT-лицензию и компактную proxy-модель, удобную для TUN/VpnService архитектуры. При этом неизменённый upstream нельзя просто встроить в NetPilot.

### Критические архитектурные ограничения

- по документации v0.17.3 SOCKS listener по умолчанию — `0.0.0.0:1080`;
- NetPilot должен принудительно использовать только loopback и никогда не открывать unauthenticated proxy в LAN/WAN;
- upstream CLI принимает file/path/raw-data параметры (`pidfile`, `cache-dump`, `hosts`, `ipset`, `fake-data` и др.); в NetPilot они не должны приходить от пользователя/конфига;
- SOCKS proxy сам по себе не является прозрачным system-wide Windows engine; Discord/UDP на Windows требует отдельного проверенного routing layer.

### Важный post-release delta

Тег v0.17.3 — commit `7efde1b1296eaaa187b70e951894dde17527489c`. До рассмотренной точки `ba532298de7b28cfe854aea83d061369d13ca290` после него накопилось 38 commits, включая исправления UDP tunnel closure, send/error handling, input validation и cleanup памяти.

Поэтому stock v0.17.3 не получает ADMIT только потому, что это последний tag. Сначала нужно решить, какие исправления обязательны и какая exact post-release point станет fork base.

**Предварительное решение:** №2 для глубокого review; особенно перспективен для Android, но Windows system-wide routing считается отдельной security boundary.

## 3. DPIBreak — современный secondary HTTPS engine

DPIBreak v0.6.2 имеет несколько сильных supply-chain признаков:

- signed annotated tag;
- SHA-256 release assets;
- отдельный `sha256sum.txt.sig`;
- buildinfo с exact commit/toolchain;
- Windows и Linux release assets.

Релиз v0.6.2 также включает исправления WinDivert cleanup и memory-related bug в собственном Rust WinDivert wrapper.

### Почему он не становится основным

Документированный scope DPIBreak — HTTPS/TLS ClientHello. Сам upstream отдельно отмечает, что zapret шире и поддерживает UDP-сценарии вроде Discord/WireGuard. Поэтому DPIBreak разумен как fallback для HTTPS/YouTube, но не закрывает текущую продуктовую цель `discord`/UDP как единственный engine.

Дополнительно:

- open issue #24 требует проверки Linux NFQUEUE segmentation/reassembly semantics и отдельно Windows WinDivert behavior;
- WinDivert 2.2.2-A и release Windows native inventory ещё должны пройти наш admission;
- GPL-3.0-or-later требует держать fork/компонент отдельно от MIT Core и выполнять GPL obligations;
- NetPilot не будет предлагать пользователю обходить SmartScreen/Defender предупреждения — для нашей поставки нужен собственный проверяемый release/signing process.

**Предварительное решение:** №3, secondary/fallback, не основной backend.

## Порядок R28

### R28.1 — static source admission

1. **zapret2 post-UAF-fix** — полный source review exact tree `4a330554...`, residual issue review, hardening patch design.
2. **ByeDPI** — полный diff review `v0.17.3..ba532298...`, выбор exact fork anchor, listener/path/resource hardening design.
3. **DPIBreak v0.6.2** — Cargo/dependency/source review, WinDivert wrapper, issue #24 и license boundary.

### R28.2 — controlled build evidence

Для кандидата, который проходит R28.1:

- сборка из exact source commit;
- dependency lock/inventory;
- SHA-256 всех native artifacts;
- PE/signature metadata на Windows;
- reproducibility evidence, где достижимо;
- static analysis / sanitizer / fuzz regression tests.

### R28.3 — isolated runtime evidence

Только после безопасной сборки:

- start/stop/restart;
- abnormal termination и crash cleanup;
- отсутствие оставшихся driver/firewall/listener state;
- HTTPS/YouTube;
- Discord TCP/UDP для движков, которые заявляют соответствующий scope;
- bounded resource tests;
- coexistence с типичными VPN/security products;
- повторный запуск после сбоя.

## Модель собственного форка

Для выбранных проектов используем схему:

`точный upstream ref -> reviewed delta -> NetPilot patchset -> CI/security evidence -> admission manifest -> release`.

Не используем схему:

`upstream latest -> автоматическое скачивание -> запуск`.

Рекомендуемая структура будущего fork lifecycle:

- upstream source остаётся максимально близок к оригиналу;
- наши изменения минимальны и документированы;
- каждый security patch имеет причину и test;
- upstream fixes подтягиваются только выбранными commits или reviewed merge;
- каждая новая сборка получает собственный immutable version identity и SHA-256;
- NetPilot Core остаётся engine-neutral и не знает raw CLI конкретного движка.

## Решение R28 на текущем этапе

Приоритет глубокого аудита:

1. **zapret2 v1.0.6 + exact UAF fix** — кандидат на основной hardened fork Windows/Linux.
2. **ByeDPI** — кандидат на secondary/Android fork после review post-release delta.
3. **DPIBreak v0.6.2** — secondary HTTPS/YouTube fallback с отдельной GPL boundary.

Ни один из трёх пока не получил `ADMIT`. Production runtime NetPilot остаётся в прежнем fail-closed состоянии.

## Что R28 намеренно не делает

- не включает `EngineStart`/`EngineInstall`;
- не добавляет `Process.Start` для новых движков;
- не скачивает native engine в LocalSystem runtime;
- не создаёт публичный SOCKS listener;
- не разрешает Lua/raw CLI/path/file конфигурацию;
- не делает автоматический fork sync;
- не считает отсутствие CVE/advisory доказательством безопасности;
- не меняет статус старого zapret2 v1.0.6 BLOCK;
- не сливает PR #30 в `main`.
