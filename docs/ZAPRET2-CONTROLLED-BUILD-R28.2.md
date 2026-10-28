# R28.2 — controlled provenance/build gate zapret2

Дата: 28.10.2026.

Статус: **COMPILE/PROVENANCE EVIDENCE PASS / NOT ADMITTED / RELEASE GATE BLOCK / NATIVE EXECUTION DISABLED**.

## Цель

R28.2 переводит выбранный zapret2 fork candidate из статического source review в контролируемую цепочку происхождения, hardening patchset и compile-only evidence.

R28.2 **не разрешает запуск движка**. Успешная сборка доказывает только применимость exact source + reviewed patches + зафиксированных build inputs. Runtime, parser safety, resource saturation под реальной нагрузкой и WinDivert lifecycle остаются отдельными gates.

## Exact source anchor

База кандидата:

- upstream: `bol-van/zapret2`;
- release: `v1.0.6`;
- release commit: `221d7dff24bfec0cd5ac0a5070b8acef2008af3d`;
- required UAF fix: `4a33055448ce5f4e7acc704e1fbe0d8a8a6aa08d`;
- expected source tree after fix: `5c2a77786cc7089145abe2ca99bcc53a8ed7de15`.

UAF fix является прямым дочерним коммитом exact v1.0.6 release commit. Текущий upstream `master` **не используется** как база и не может автоматически сливаться в NetPilot fork.

## Provenance gate

`.github/workflows/native-candidate-provenance.yml` проверяет exact commit/parent/tree, единственный upstream delta `nfq2/desync.c`, ожидаемые строки UAF fix, применимость NetPilot patchset и формирует SHA-256 evidence.

Ключевой ранее зафиксированный результат:

- run: `40708142913`;
- artifact: `12824806940`, `zapret2-r28-source-provenance`;
- artifact ZIP SHA-256: `700962c2acb0d9873836bb371a7a6d51459c0951d9796bd5065fd6678787b7b6`;
- source anchor SHA-256: `dfd059a545ef2d7cbe9e322ab6d5a993a75ab45452cf964c24d3f47e9274e369`;
- upstream UAF patch SHA-256: `a6cc3426bd38bf4c38d6c87edec64b3436386a95a2d5cd7d9d935d5d960df38e`.

Workflow не запускает zapret2 и не устанавливает WinDivert.

## NetPilot patch series

Кандидат сейчас строится из трёх reviewable patches.

### 0001 — explicit conntrack feed result

`security/native-admission/patches/zapret2-r28/0001-explicit-conntrack-feed-result.patch`

SHA-256: `af811313b37efc14f157bdc1744e9a74ea3c009b03fc1d518d9ab327a35facff`.

Это behavior-preserving refactor: неоднозначный `bool` заменён внутренним result type без ввода resource cap.

### 0002 — aggregate conntrack budget + RESOURCE_EXHAUSTED

`security/native-admission/patches/zapret2-r28/0002-bounded-conntrack-resource-result.patch`

SHA-256: `a3531211626ce602a3b5cab6c926a28e7a94ebdf2a475e3bcfa6829e64dea783`.

Patch добавляет `max_entries`, отдельный `CTRACK_FEED_RESOURCE_EXHAUSTED` и fail-safe `VERDICT_PASS` до `dp_find()`/desync для нового flow при исчерпании ресурса.

Важно: `max_entries` по умолчанию равен `0`, поэтому **production числовой лимит ещё не выбран и не admitted**. Его нельзя задавать «на глаз» — требуется workload evidence.

### 0003 — delayed queue byte budget

`security/native-admission/patches/zapret2-r28/0003-bounded-delayed-queue-bytes.patch`

SHA-256: `33fb3c7887e5e2cc0ad225d74318a9f2d5192179cf6b7671da75f75bd7335c68`.

Patch добавляет `max_bytes`, `queued_bytes`, overflow-safe проверку `SIZE_MAX`, учёт dequeue/destroy и сохраняет существующий packet-count bound.

Важно: `max_bytes` по умолчанию равен `0`, поэтому **production byte budget пока не активирован и не admitted**. Требуются regression/load tests и измерения.

## Linux compile-smoke

Workflow `Native Candidate Compile Smoke` на `ubuntu-24.04` успешно скомпилировал exact source + patches `0001–0003` без разрешения production runtime.

- run: `40718483479`;
- artifact: `12828479247`, `zapret2-r28-compile-smoke-evidence`;
- artifact ZIP SHA-256: `89e32d05502d65a71c9038c369d0fe0c7eaf89230f02431cdbfe4afaf214716b`;
- compiler: GCC `13.3.0`;
- make: `4.3`;
- LuaJIT package: `2.1.1703358377`;
- compiled binary SHA-256: `8bdd22a0f51b7f8c8f2c5e3f7e56536842fec49bcda15bf98c1c574e2dc4adcb`;
- binary size: `242824` bytes.

Это compile-only evidence, не runtime admission.

## Windows dependency intake

Workflow `Native Candidate Windows Dependency Intake` зафиксировал build inputs до запуска native engine:

- run: `40718483411`;
- artifact: `12830025932`, `zapret2-r28-windows-dependency-intake`;
- artifact ZIP SHA-256: `4bb4106aab0f2f2afb78d651b51f8ad798ea2e3a4b117f1e1882bfeccb0d0ee4`;
- zapret2 commit/tree: `4a330554...` / `5c2a7778...`;
- LuaJIT commit: `dcc9c9ee67e1a5d3d636bd7745e95ddb4a1c70bc`;
- LuaJIT tree: `67f2346f2e4a72f7cafd3c872d70bd89fb27bcd4`;
- runner: `windows-2025`;
- Cygwin setup: `2.940`;
- Cygwin runtime: `3.6.12-1`;
- GCC core: `14.4.0-1`;
- make: `4.4.1-2`;
- zlib-devel: `1.3.2-1`;
- `cygwin1.dll` SHA-256: `ed9d5379061e5de79c3375cc62246c2142ea1c97d94f4d16bfbea6fb1b7fc889`.

На этом этапе native engine не компилировался/не запускался, WinDivert driver не устанавливался.

## Cygwin setup provenance

Отдельный gate проверил подпись bootstrap-файла Cygwin:

- run: `40718483448`;
- artifact: `12829701507`, `cygwin-setup-2.940-provenance-evidence`;
- artifact ZIP SHA-256: `5585ebc4461079368118df8cf6c8b5fc13cc67bb306db30126844dcb3eabf9a0`;
- setup version: `2.940`;
- setup SHA-512: `fc46e3ead0312ea632251c16334b23caab31c9b38db30db4e5418b43ffeec5c003cc867b4166c8222a622ad107660e2ce6e3d855499d08e695dd13010f43a396`;
- signing key fingerprint: `56405CF6FCC81574682A5D561A698DE9E2E56300`;
- GPG result: valid/good signature for `Cygwin <cygwin@cygwin.com>`.

`TRUST_UNDEFINED` в локальном GPG trustdb не трактуется как автоматическое доверие личности ключа; fingerprint должен оставаться явно pinned/accepted policy input.

## Windows x64 compile-smoke

Подробный отдельный документ: `docs/ZAPRET2-WINDOWS-COMPILE-SMOKE-R28.2.md`.

Успешный controlled build:

- run: `40718483443`;
- job: `122922566162`;
- head: `66d5cd72370e1283e5fbaf59e7cced9baf0003c4`;
- artifact: `12829318738`, `zapret2-r28-windows-x64-compile-smoke-evidence`;
- artifact ZIP SHA-256: `63a445d03202d6b2e548c1dd8f7d9791863d7decaa74338cd8e8084ed5ecbc92`.

Собранный `winws2.exe`:

- size: `650240` bytes;
- SHA-256: `869d5e80ec1b5b263b27485ef7c943066becf58e2b69bb463a43bfd064edb28d`;
- Authenticode: `NotSigned`;
- FileVersion/ProductVersion: отсутствуют.

Сборочный `cygwin1.dll`:

- size: `3246645` bytes;
- SHA-256: `ed9d5379061e5de79c3375cc62246c2142ea1c97d94f4d16bfbea6fb1b7fc889`.

`winws2.exe` **не запускался**, WinDivert не присутствовал в compile-smoke job, driver не устанавливался, binary после инвентаризации удалён с runner и не публикуется пользователям.

## WinDivert inventory

Отдельный inventory gate проверил exact WinDivert `2.2.2-A` без установки/загрузки драйвера:

- run: `40718483477`;
- artifact: `12829427887`, `windivert-2.2.2-a-x64-inventory-evidence`;
- artifact ZIP SHA-256: `9bf2e8fa898154d25858f7621b1a1fe3d2cb4ccc82091c13853478040ba070c3`;
- archive SHA-256: `63cb41763bb4b20f600b6de04e991a9c2be73279e317d4d82f237b150c5f3f15`.

`WinDivert.dll`:

- size: `47616`;
- SHA-256: `c1e060ee19444a259b2162f8af0f3fe8c4428a1c6f694dce20de194ac8d7d9a2`;
- Authenticode: `NotSigned`.

`WinDivert64.sys`:

- size: `94144`;
- SHA-256: `8da085332782708d8767bcace5327a6ec7283c17cfb85e40b03cd2323a90ddc2`;
- file/product version: `2.2`;
- Authenticode: `Valid`;
- signer thumbprint: `043589F75FCE2795E7F2CC3E526D46784D5DDAB3`.

Наличие валидной подписи не заменяет human acceptance signer chain и driver lifecycle review.

## Что доказано на текущем этапе

Доказано:

1. exact post-UAF source anchor известен;
2. patches `0001–0003` применимы и компилируются на Linux/Windows;
3. Windows build inputs и выходной `winws2.exe` имеют точные hashes;
4. Cygwin bootstrap signature и fingerprint зафиксированы;
5. WinDivert DLL/SYS проинвентаризированы без установки драйвера;
6. CI/CodeQL и candidate workflows на проверенной голове завершались успешно.

**Не доказано:** runtime safety, parser/fuzz robustness, корректные production resource limits, поведение Discord/YouTube, crash cleanup, WinDivert service/driver lifecycle и отсутствие новых advisory для полного exact component set.

## Что R28.2 НЕ разрешает

- production `EngineInstall`;
- production `EngineStart`;
- запуск candidate через LocalSystem service;
- установку/загрузку WinDivert driver;
- добавление binary в updater admission catalog;
- использование raw CLI или пользовательского Lua;
- автоматическое принятие upstream `master`;
- публикацию candidate пользователям;
- смену `admission_status` на `ADMIT`.

Admission остаётся **REVIEW**, release gate — **BLOCK**, `may_execute: false`, `may_integrate: false`.

## Следующий gate — R28.3

До ADMIT нужны как минимум:

1. sanitizer/static-analysis и parser/fuzz regression evidence;
2. regression tests resource saturation: conntrack `RESOURCE_EXHAUSTED`, delayed queue enqueue/dequeue/destroy/overflow;
3. измерения browser/YouTube/Discord/combined/burst/hostile workloads для выбора `max_entries` и `max_bytes`;
4. отдельный review WinDivert signer/driver lifecycle и актуальных CVE/advisory/KEV для exact component set;
5. только после этого — **изолированный** runtime lab: start/stop/crash/cleanup, HTTPS, Discord TCP/UDP, повторный запуск;
6. отдельное HIGH_ASSURANCE решение перед любым production activation.

R28.2 не является ADMIT. Успешная компиляция не должна автоматически открывать runtime boundary.
