# R28.2 — controlled provenance/build gate zapret2

Дата: 28.10.2026.

Статус: **IN PROGRESS / NOT ADMITTED / NATIVE EXECUTION DISABLED**.

## Цель

R28.2 переводит выбранный zapret2 fork candidate из статического source review в контролируемую цепочку происхождения и сборки.

Первый шаг намеренно **не компилирует и не запускает** native engine. Сначала CI должен доказать exact source anchor и сформировать evidence, на котором затем строится NetPilot hardening patchset.

## Exact source anchor

База:

- upstream: `bol-van/zapret2`;
- release: `v1.0.6`;
- release commit: `221d7dff24bfec0cd5ac0a5070b8acef2008af3d`;
- required UAF fix: `4a33055448ce5f4e7acc704e1fbe0d8a8a6aa08d`;
- expected source tree after fix: `5c2a77786cc7089145abe2ca99bcc53a8ed7de15`.

Git commit object подтверждает, что UAF fix имеет ровно одного родителя — exact v1.0.6 release commit.

## Provenance workflow

Добавлен workflow:

`.github/workflows/native-candidate-provenance.yml`

Он разрешён для `workflow_dispatch` и relevant PR changes и имеет только:

`permissions: contents: read`.

### Что workflow проверяет

1. Checkout NetPilot выполняется без сохранения credentials.
2. External source checkout фиксирован на exact commit `4a330554...`.
3. Проверяется exact HEAD SHA.
4. Проверяется exact parent SHA `221d7dff...`.
5. Проверяется exact tree SHA `5c2a7778...`.
6. Проверяется, что delta parent→fix содержит ровно один файл: `nfq2/desync.c`.
7. Проверяется наличие ключевых строк fix path:
   - переход на `reasm_cancel_rediscover`;
   - восстановление `rdata_payload` на текущий packet;
   - восстановление `rlen_payload` на текущий packet.
8. Формируется text evidence с source/tree/parent/changed-file/hash metadata.
9. Формируется exact upstream UAF patch для evidence.
10. Evidence files получают SHA-256 и загружаются как CI artifact.

Workflow **не запускает zapret2**, не устанавливает WinDivert, не загружает release binary и не меняет network state.

## Immutable GitHub Actions

В provenance workflow actions закреплены по full commit SHA, а не floating tags:

- `actions/checkout` commit `11bd71901bbe5b1630ceea73d27597364c9af683` (tag v4.2.2 использован только как human-readable provenance note);
- `actions/upload-artifact` commit `cf430e030ddbb5b0abf93d22962f4752f3646cd9` (tag v7.0.2 — human-readable note).

Runner фиксирован как `ubuntu-24.04`, а не `ubuntu-latest`.

Это ещё не делает весь будущий native build reproducible, но не допускает лишний floating trust уже на source-provenance gate.

## Почему отдельный fork repository ещё не является admission evidence

Сам факт GitHub fork ничего не доказывает о безопасности. Для NetPilot важнее:

- exact upstream anchor;
- точный patchset;
- контролируемая сборка;
- dependency inventory;
- output hashes;
- tests;
- admission manifest.

Поэтому до появления собственного release binary patch provenance может храниться отдельно от физического fork repository. Когда отдельный fork создаётся, он должен воспроизвести ту же цепочку:

`221d7dff... -> 4a330554... -> reviewed NetPilot patches`.

Нельзя создавать fork от current `master` и затем пытаться доказать, какие изменения туда попали.

## Следующий patch gate

Первый NetPilot-specific source patch должен решать **bounded resource behavior**, описанный в `docs/ZAPRET2-RESOURCE-HARDENING-R28.1.md`.

Перед написанием production patch требуется закрепить механизмы отдельно от числовых production thresholds:

1. aggregate conntrack entry budget;
2. delayed packet queue byte accounting;
3. overflow-safe size checks до allocation/copy;
4. безопасное поведение при достижении budget: сеть не должна оставаться в зависшем или частично перехваченном состоянии; конкретный `pass-through` для нового flow допускается только после проверки точной семантики всех вызовов `ConntrackPoolFeed`;
5. отсутствие произвольного LRU eviction активных tracks;
6. metrics/tests для saturation path.

Числовые production limits не выбираются «на глаз». Сначала механизмы должны пройти controlled tests на browser/YouTube/Discord/combined/burst/hostile workloads.

## Controlled build requirements

После source-anchor и patch applicability gate Windows build должен выполняться только с заранее известными inputs.

Обязательные inputs:

- exact source commit/tree;
- exact NetPilot patchset hash;
- exact Cygwin/toolchain packages;
- exact Lua/LuaJIT source/archive hash, если компонент требуется build/runtime;
- exact WinDivert version/archive hash до распаковки;
- full-SHA pinned build actions;
- explicit runner/toolchain metadata.

Build не должен доверять:

- `latest`;
- mutable GitHub release metadata без pinned digest;
- произвольному URL из config;
- dependency download без expected identity.

## Build outputs

Для каждого candidate build создаётся manifest минимум с:

- NetPilot candidate version;
- upstream release SHA;
- upstream fix SHA;
- source tree SHA;
- patchset SHA-256;
- compiler/toolchain identity;
- dependency identities/hashes;
- output filenames/sizes/SHA-256;
- PE metadata;
- Authenticode status/signers для Windows native files;
- WinDivert driver identity;
- CI run identity.

Пока такой manifest не сформирован и не reviewed, binary остаётся **UNADMITTED**.

## Что R28.2 пока не разрешает

- production `EngineInstall`;
- production `EngineStart`;
- запуск candidate binary в CI на privileged/network path;
- установку WinDivert;
- загрузку component в NetPilot updater catalog;
- доверие current upstream master;
- создание release для пользователя;
- смену admission status на `ADMIT`.

## Следующие действия R28.2

1. Получить CI evidence от `Native Candidate Provenance`.
2. Зафиксировать его run/artifact identity.
3. Проверить точную семантику всех `ConntrackPoolFeed` call sites и только после этого подготовить bounded-resource patch как отдельный минимальный patch series.
4. Добавить `git apply --check`/source assertions к exact tree до компиляции.
5. Зафиксировать controlled Windows build inputs и hashes.
6. Собрать candidate без его запуска.
7. Провести native inventory и only-after-build security review.

Только после этого можно переходить к sanitizer/fuzz и isolated runtime R28.3.
