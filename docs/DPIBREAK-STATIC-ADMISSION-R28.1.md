# R28.1 — статический admission-аудит DPIBreak v0.6.2

Дата: 28.10.2026.

Проверяемая точка: `dilluti0n/dpibreak@v0.6.2`, release commit `334285b60aabd5a32a83a65211ac60bce968b211`.

Этот документ **не присваивает ADMIT**, не разрешает запуск DPIBreak из NetPilot и не разрешает автоматическое обновление с upstream.

## Итог текущего прохода

Статус: **REVIEW / release gate BLOCK**.

DPIBreak остаётся хорошим secondary/fallback кандидатом для HTTPS/YouTube на Windows и Linux. У проекта есть сильные признаки release provenance: signed annotated tag, SHA-256 assets, подпись checksum-файла и buildinfo с exact source/toolchain. Но это не заменяет security admission: Windows путь включает WinDivert/FFI/driver boundary, Linux имеет открытый вопрос NFQUEUE/GSO semantics, а функциональный scope движка существенно уже zapret2 и не закрывает полноценный Discord/UDP профиль.

Предварительная fork-стратегия: **upstream-first, без собственного форка по умолчанию**. Форк имеет смысл только если конкретный NetPilot blocker нельзя решить adapter policy или upstream fix.

## 1. Функциональный scope

README exact v0.6.2 прямо описывает DPIBreak как инструмент для обхода DPI на HTTPS connections путём изменения исходящего TLS ClientHello. Движок перехватывает только небольшие пакеты, необходимые для TLS/SNI manipulation; остальной трафик должен проходить без обработки.

Это хороший дизайн для низкого overhead и ограниченной attack surface, но он означает продуктовый предел:

- HTTPS/браузинг — релевантно;
- YouTube HTTPS path — релевантно;
- полный Discord TCP/UDP/voice scope — не доказан и не заявлен как основная цель;
- WireGuard/произвольные UDP protocol scenarios — не являются текущим scope.

Решение: DPIBreak не рассматривается как единственный основной engine NetPilot. Его роль — secondary HTTPS/YouTube fallback.

## 2. Rust — плюс, но не автоматический security proof

Core написан на Rust, release profile использует `panic = "abort"` и LTO. Это уменьшает ряд типичных memory-safety рисков в безопасном Rust-коде.

Но Windows path использует in-tree `windivert` crate, который является wrapper вокруг `windivert-sys`/native WinDivert и системных Windows API. Следовательно, остаются:

- FFI boundary;
- native DLL/SYS;
- kernel driver;
- service lifecycle;
- packet parsing correctness;
- unsafe blocks/dependency risks;
- supply-chain dependencies.

Поэтому фраза «на Rust» учитывается как положительный фактор, но не меняет admission gate.

## 3. Dependency boundary

`Cargo.toml` v0.6.2 фиксирует package version 0.6.2, Rust edition 2024 и `rust-version = 1.88`.

Основные dependencies включают:

- `anyhow`;
- `etherparse`;
- Linux: `nfq-updated`, `socket2`, `daemonize`, `libc`, `thiserror`;
- Windows: in-tree `windivert`, `windows-services`, `windows`;
- build dependency `winres`.

In-tree `crates/windivert` в свою очередь использует:

- `windivert-sys 0.10.0`;
- `etherparse 0.13`;
- `windows 0.48`;
- `thiserror`.

До ADMIT требуется exact `Cargo.lock` inventory, advisory review и проверка unsafe/FFI surfaces. Одного `Cargo.toml` недостаточно.

## 4. Windows WinDivert boundary

DPIBreak v0.6.2 использует WinDivert 2.2.2-A. Release notes указывают, что в этой версии были исправлены dangling pointer в `windivert` crate и cleanup WinDivert service при завершении процесса.

Это положительное evidence наличия upstream fixes, но NetPilot всё равно должен независимо проверить exact release component set:

- `dpibreak.exe` SHA-256/PE metadata/Authentiсode;
- `WinDivert.dll` SHA-256 и происхождение;
- `WinDivert64.sys` SHA-256, подпись и certificate chain;
- DLL search path;
- service/driver ACL;
- start/stop/restart;
- abnormal termination;
- update/uninstall cleanup;
- отсутствие оставшегося WinDivert service/driver state после завершения.

NetPilot не будет повторять upstream UX, предлагающий пользователю вручную обходить SmartScreen предупреждение. Наш admitted component должен поставляться через собственную проверенную install/signing chain.

## 5. Open issue #24 — Linux NFQUEUE/GSO semantics

Issue #24 остаётся открытым. Первоначально наблюдалось, что последующие TCP segments одной TLS connection попадали в обработчик и рассматривались как отдельные ClientHello.

Upstream позже уточнил причину: default NFQUEUE behavior может нормализовать/сегментировать GSO packet перед userspace. `NFQA_CFG_F_GSO` позволяет изменить это поведение, но тогда userspace может получить packet больше MTU; при отправке через raw socket возможен `EMSGSIZE`, если split order не учитывает MTU.

Это не выглядит как доказанная memory-safety vulnerability, но является correctness/failure-mode проблемой для packet engine.

Для NetPilot Linux admission требуется отдельный тестовый matrix:

- GSO on/off;
- packet > MTU;
- fragmented/segmented ClientHello;
- exact split strategies;
- fail-open behavior при `EMSGSIZE`;
- отсутствие packet loss/stall;
- cleanup NFQUEUE/firewall state после crash.

Windows WinDivert behavior нельзя считать автоматически таким же; его нужно измерить отдельно.

## 6. Supply-chain: сильные release artifacts, но build recipe требует hardening

Положительные признаки v0.6.2:

- signed annotated tag;
- Windows/Linux assets имеют SHA-256;
- release содержит `sha256sum.txt.sig`;
- buildinfo фиксирует commit/toolchain;
- release build автоматизирован.

При этом exact upstream `release.yml` не соответствует строгой trust policy NetPilot:

- `actions/checkout@v4`, `actions/upload-artifact@v4`, `actions/download-artifact@v4`, `softprops/action-gh-release@v2` — floating major refs, а не immutable full commit SHA;
- используется `dtolnay/rust-toolchain@stable`/`toolchain: stable`;
- Windows job скачивает `WinDivert-2.2.2-A.zip` по HTTPS и перед распаковкой не сравнивает заранее зафиксированный expected SHA-256;
- `windows-latest`/`ubuntu-latest` являются плавающей runner environment;
- winget publishing скачивает `wingetcreate/latest`.

Это **не finding против upstream**: проект может считать этот уровень достаточным. Но NetPilot controlled build должен быть строже.

Требования NetPilot:

1. pin GitHub Actions по full immutable SHA;
2. pin exact Rust toolchain и target;
3. использовать `Cargo.lock --locked` для build;
4. pin WinDivert archive SHA-256 до распаковки/использования;
5. сохранить SBOM/build manifest;
6. записать source commit, toolchain, dependency lock hash, input hashes и output hashes;
7. не использовать `latest` как trust input;
8. сравнить собственный build с upstream artifact на уровне inventory/behavior, даже если byte-for-byte reproducibility недостижима.

## 7. Licensing boundary

DPIBreak имеет `GPL-3.0-or-later`. NetPilot Core — MIT.

Это не запрещает использовать DPIBreak как отдельный executable/component, но требует аккуратной модели распространения:

- DPIBreak/fork остаётся GPL-3.0-or-later;
- соответствующий source и notices доступны пользователю;
- GPL implementation не копируется в MIT Core без отдельного legal review;
- adapter/API boundary документируется;
- если создаётся fork, все наши изменения к DPIBreak публикуются с выполнением GPL obligations.

Поэтому собственный fork DPIBreak имеет более высокую стоимость сопровождения, чем MIT zapret2/ByeDPI. При активном upstream нет причины создавать его заранее.

## 8. Почему пока не форкаем DPIBreak

На текущем этапе у нас не найден NetPilot-specific blocker, который обязательно требует изменения upstream source.

Большую часть наших требований можно обеспечить внешней интеграцией:

- typed allowlisted profile;
- fixed executable identity;
- hash pinning;
- закрытый updater;
- ProgramFiles/ACL;
- проверенный start/stop lifecycle;
- отсутствие raw CLI в UI/IPC.

А открытый Linux GSO/NFQUEUE вопрос логичнее сначала проверить на exact release/upstream fixes, а не создавать расходящийся fork.

Решение: **upstream-first tracking**. Fork создаётся только при одном из условий:

1. подтверждённый security/correctness blocker не исправляется upstream;
2. нужен минимальный hardening patch, который невозможно обеспечить adapter boundary;
3. нужна критичная для NetPilot cleanup/fail-safe гарантия, отсутствующая upstream.

## 9. Что нужно проверить в R28.2

До любого ADMIT:

- exact `Cargo.lock` inventory;
- cargo advisory/security review;
- `unsafe`/FFI search и ручной анализ WinDivert wrapper;
- controlled Windows/Linux build;
- native inventory Windows package;
- PE/Authentiсode/signature chain;
- WinDivert service/driver lifecycle;
- crash/kill/power-interruption cleanup simulation;
- Linux NFQUEUE/GSO/MTU regression tests;
- Windows ClientHello segmentation behavior;
- HTTPS/YouTube functional tests;
- coexistence с распространёнными VPN/security products;
- bounded CPU/memory tests под burst traffic.

## 10. Product integration policy

Если exact DPIBreak component когда-либо получает `ADMIT`, NetPilot всё равно не передаёт пользователю raw CLI.

Допускается только:

`EngineProfileId -> внутренний typed plan -> фиксированный ArgumentList`.

Не допускаются:

- arbitrary executable/path;
- arbitrary segment order из пользовательского config без allowlist;
- shell command;
- BAT/PowerShell;
- произвольный driver/service name;
- upstream latest download at runtime.

## Решение R28.1 по DPIBreak

**DPIBreak v0.6.2 сохраняется как кандидат №3 — secondary HTTPS/YouTube fallback.**

Его supply-chain оформление лучше многих рассмотренных аналогов, а Rust уменьшает часть рисков. Но функциональный scope уже zapret2, Windows зависит от WinDivert native boundary, Linux issue #24 требует runtime evidence, а GPL делает ненужный ранний fork менее выгодным.

На текущем этапе стратегия: **не форкать; отслеживать upstream и провести controlled admission exact v0.6.2/актуального следующего tagged release перед интеграцией**. Статус остаётся **REVIEW / BLOCK**.
