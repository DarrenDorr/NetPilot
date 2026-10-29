# R28.4 — статический admission-аудит DPIBreak v0.6.2

Дата актуализации: 29.10.2026.

Проверяемая точка: `dilluti0n/dpibreak@v0.6.2`.

Exact identity:

- annotated tag object: `7601532147fe92130964e49a644d3207e74b8a51`;
- release commit: `334285b60aabd5a32a83a65211ac60bce968b211`;
- source tree: `7e95a17933fe9fda521865b1317c4f9f75d6c58c`;
- Cargo.lock Git blob: `e3adcd1b21711e7e754878b15625076c7bcaf7e9`.

Этот документ **не присваивает ADMIT**, не разрешает запуск DPIBreak из Net.Pilot, не разрешает загрузку WinDivert, создание NFQUEUE/firewall rules или автоматическое обновление с upstream.

## Итог

Статус: **REVIEW / release gate BLOCK / may_execute=false / may_integrate=false**.

DPIBreak сохраняется как сильный резервный Windows/Linux кандидат для HTTPS/YouTube-сценариев, но не как основной движок. У проекта хороший release provenance, активный upstream и Rust core, однако текущий функциональный scope уже zapret2, QUIC support ещё не реализован, Linux имеет открытые NFQUEUE/firewall вопросы, а Windows требует отдельного WinDivert admission.

Предварительная fork-стратегия: **upstream-first, без собственного форка по умолчанию**. Форк создаётся только для конкретного подтверждённого blocker/hardening fix, который нельзя закрыть adapter policy или получить upstream вовремя.

## 1. Release provenance

Для v0.6.2 подтверждено:

- GitHub-verified signed annotated tag;
- tag указывает на exact commit `334285b60aab...`;
- Windows asset `dpibreak-0.6.2-x86_64-pc-windows-msvc.zip` имеет SHA-256 `606a2b4179b423ab9d4b0f0210dedbb3fc9e7c09af52949164893bd53a7dda6f`;
- Linux asset `dpibreak-0.6.2-x86_64-unknown-linux-musl.tar.gz` имеет SHA-256 `bb19b1f9716efa15b801c2ad53f787412b44d59123b81b740f569eda2abd3f9a`;
- `sha256sum.txt` имеет SHA-256 `d7adc8616e884d4d43b573c30d7ac70517553320717491ac310646943d72fdb7`;
- отдельный `sha256sum.txt.sig` имеет SHA-256 `d56289ce991a2d58d62676b080c57aea31b3c2a1677df48f7f0a05f13e1af795`;
- release buildinfo указывает exact source commit и toolchain.

Важно: сам release commit в GitHub commit verification отображается unsigned. Поэтому нашим provenance anchor является подтверждённый signed annotated tag + exact commit/tree + самостоятельно закреплённые hashes release assets.

## 2. Функциональная роль

DPIBreak ориентирован на системное изменение трафика вокруг TLS/HTTPS и перехватывает ограниченный набор пакетов для обработки ClientHello.

Для Net.Pilot это означает:

- HTTPS/браузинг — релевантно;
- YouTube HTTPS — потенциально релевантно;
- QUIC/HTTP3 — **не считать поддержанным**, пока upstream issue #32 остаётся открытым;
- полный Discord TCP/UDP/voice — не доказан;
- forwarded VPN/WireGuard traffic — требует отдельного routing/firewall review.

Поэтому текущая роль: **secondary HTTPS/YouTube fallback**.

## 3. Rust и native boundaries

Core написан на Rust, package использует edition 2024, `rust-version = 1.88`, release profile с LTO и `panic = "abort"`.

Это положительный фактор, но не security proof. Остаются:

- unsafe/FFI surfaces;
- in-tree `windivert` crate;
- native WinDivert DLL/SYS и kernel driver;
- Windows service lifecycle;
- Linux NFQUEUE;
- nftables/iptables mutation;
- packet parsing correctness;
- dependency/supply-chain risks.

`crates/windivert` имеет лицензию LGPL-3.0-or-later и зависит, в частности, от `windivert-sys 0.10.0`. Основной DPIBreak имеет GPL-3.0-or-later.

## 4. Актуальные upstream issues

### #24 — NFQUEUE / GSO semantics

Issue остаётся open/bug. Upstream описывает ситуацию, когда NFQUEUE может сегментировать GSO TCP packet перед userspace, а последующие сегменты той же connection попадают в обработку, рассчитанную на ClientHello.

Для Net.Pilot это correctness/failure-mode blocker для Linux lab, пока не проверены:

- GSO on/off;
- packet > MTU;
- fragmented/segmented ClientHello;
- `EMSGSIZE`/fail-safe path;
- отсутствие потери/stall трафика;
- cleanup NFQUEUE/firewall state после crash/timeout.

Windows segmentation behavior нельзя автоматически считать таким же.

### #26 — локальные HTTPS-сервисы

Open issue сообщает, что агрессивный режим может приводить к `SSL_ERROR_RX_MALFORMED_SERVER_HELLO` для локально размещённого HTTPS-сервиса и предлагает domain exclusion.

Следствие для Net.Pilot: broad/universal profile нельзя считать безопасным по умолчанию. Перед таким профилем нужна typed local/LAN exclusion policy и regression evidence.

### #28 — автоматические nft/iptables rules

Open issue от upstream предлагает возможность отключить встроенное выполнение `nft`/`iptables`, отмечая maintainability/user-experience проблемы текущей hard-coded модели.

Для нашего Linux lab это означает обязательные:

- exact rule inventory;
- NetPilot-owned временные имена;
- before/after diff;
- crash/timeout rollback;
- fail-safe traffic path;
- отсутствие чужих rule modifications.

### #31 — WireGuard compatibility

Open issue имеет labels `workaround` и `compatibility`: forwarded WireGuard client traffic не обрабатывается автоматически текущей конфигурацией.

Мы рассматриваем VPN coexistence и forwarded-client routing как отдельный test matrix, не как гарантированную capability.

### #32 — QUIC support

Issue открыт самим upstream как enhancement.

Следствие: DPIBreak нельзя использовать как основание утверждения, что Net.Pilot уже закрывает QUIC/HTTP3 или Discord UDP.

## 5. Windows WinDivert boundary

Release notes v0.6.2 заявляют исправления:

- dangling pointer в Windows `windivert` crate;
- stop WinDivert service при завершении для освобождения driver file;
- связанный cleanup update path.

Это полезные upstream fixes, но перед любым Windows runtime Net.Pilot обязан независимо получить exact component inventory:

- `dpibreak.exe` hash/PE metadata/Authentiсode;
- exact WinDivert DLL/SYS version и SHA-256;
- driver signature/certificate chain;
- DLL search path;
- service/driver ACL/lifecycle;
- start/stop/restart/crash/update/uninstall/reboot cleanup.

Net.Pilot не будет предлагать отключать Defender, добавлять exclusions или обходить SmartScreen как способ admission.

## 6. Dependency provenance gate

Добавлен workflow:

`.github/workflows/native-candidate-dpibreak-provenance.yml`

Он **не собирает и не запускает DPIBreak**.

Gate проверяет:

1. exact commit/tree;
2. annotated tag object и peeled commit mapping;
3. exact Cargo.lock Git blob;
4. package/license/rust-version markers;
5. полный Cargo.lock через Python `tomllib`;
6. registry dependencies обязаны иметь checksum;
7. неожиданные git/path sources fail closed;
8. разрешены только ожидаемые local workspace packages `dpibreak` и `windivert`;
9. сохраняются text-only evidence и SHA-256 входных manifests.

Любая будущая версия/lock drift требует нового admission delta.

## 7. Supply-chain policy

Upstream release automation рассматривается как источник полезного evidence, но не как доверенный runtime updater Net.Pilot.

Наш controlled pipeline должен:

- pin GitHub Actions по immutable full SHA;
- pin exact source commit/tree/tag mapping;
- использовать exact Cargo.lock/`--locked`;
- pin Rust toolchain;
- pin native dependency archives до использования;
- сохранять SBOM/build manifest/input/output hashes;
- никогда не превращать `latest` в trust decision.

## 8. Licensing boundary

DPIBreak — GPL-3.0-or-later, Net.Pilot Core — MIT.

Текущая политика:

- не копировать GPL implementation в MIT Core;
- держать DPIBreak/fork отдельным компонентом;
- сохранять copyright/license notices;
- выполнять source/distribution obligations выбранной модели;
- при создании fork он остаётся GPL-3.0-or-later;
- adapter/API boundary документируется отдельно.

Это делает ранний собственный fork дороже по сопровождению, чем MIT zapret2/ByeDPI, поэтому без конкретного blocker мы его не создаём.

## 9. Что нужно до любого lab/runtime

- PASS source/dependency provenance exact v0.6.2;
- exact Cargo.lock/advisory review;
- manual review unsafe/FFI surfaces;
- controlled compile-only Linux/Windows;
- exact Windows WinDivert inventory;
- Linux NFQUEUE/GSO/MTU design + regression plan;
- reversible nftables/iptables lifecycle design;
- local/LAN exclusion behavior;
- VPN/WireGuard coexistence scope;
- bounded resource tests;
- candidate-specific `DRAFT_NOT_APPROVED` lab plan;
- отдельный HIGH_ASSURANCE human approval.

Даже успешный lab PASS не будет автоматически означать `ADMIT`.

## Решение

**DPIBreak v0.6.2 остаётся кандидатом №3 — reserve/secondary Windows/Linux HTTPS backend.**

Причины сохранить его в shortlist:

- активный upstream;
- signed release provenance;
- Rust core;
- Windows/Linux system-wide модель;
- заметная работа upstream над cleanup/robustness.

Причины не делать его primary сейчас:

- QUIC support отсутствует;
- полный Discord/UDP scope не доказан;
- открыты Linux NFQUEUE/GSO и firewall lifecycle вопросы;
- local HTTPS compatibility требует exclusions;
- Windows всё равно зависит от kernel-driver boundary;
- GPL добавляет отдельную distribution/fork boundary.

Статус остаётся **REVIEW / BLOCK**. Ни один DPIBreak binary/native component пока не разрешён к запуску или интеграции в production Net.Pilot.
