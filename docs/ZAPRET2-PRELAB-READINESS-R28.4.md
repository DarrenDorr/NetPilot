# R28.4 — pre-lab readiness zapret2

Дата: 29.10.2026.

Статус: **PRE_LAB_REVIEW / DRAFT_NOT_APPROVED / EXECUTION FORBIDDEN / RELEASE GATE BLOCK**.

## Назначение

Этот документ отделяет уже закрытые static/compile/sanitizer/fuzz/resource gates от того, что ещё требуется перед первым изолированным Linux runtime-test hardened zapret2 candidate.

Наличие compile/fuzz/resource PASS **не является** `LAB_APPROVED` и тем более не является `ADMIT`.

## Exact candidate

Upstream:

- repository: `bol-van/zapret2`;
- base release: `v1.0.6`;
- release commit: `221d7dff24bfec0cd5ac0a5070b8acef2008af3d`;
- exact UAF fix: `4a33055448ce5f4e7acc704e1fbe0d8a8a6aa08d`;
- fix tree: `5c2a77786cc7089145abe2ca99bcc53a8ed7de15`.

NetPilot patchset: `0001–0005`.

## Уже закрытые gates

Для exact patchset имеются PASS в ограниченном scope:

- source/patch provenance;
- Linux compile-only;
- Windows compile-only;
- static/sanitizer compile;
- protocol parser ASan/UBSan;
- bounded coverage-guided parser fuzz;
- conntrack budget regression;
- packet-queue sanitizer regression;
- synthetic resource stress;
- WinDivert inventory-only.

Ни один из этих gates не запускал candidate как production engine.

## Controlled compile reference identities

### Linux x86-64

Workflow run: `40734378302`.

Artifact:

- ID: `12837081078`;
- ZIP SHA-256: `348af9c8fcb8339a3561b368e338060263df5536dea7b8794a19a84a6c27f2c6`.

Controlled output:

- binary: `nfqws2`;
- SHA-256: `61219de00f9728d401fdb484b929aac2245d5dfebacc460f9d109f2cc721d72e`;
- size: `242824` bytes;
- candidate executed: `false`.

### Windows x64

Workflow run: `40734378565`.

Artifact:

- ID: `12836008002`;
- ZIP SHA-256: `f159c38cc1a5161a5b61c07d1d24543b58ec14fcb178e885a0b043649cd2538a`.

Controlled output:

- binary: `winws2`;
- SHA-256: `9e6ab76c661d0d2bd8a51a035452b397c11ab2978c6d53ec650d04ffde19508c`;
- size: `650240` bytes;
- Authenticode: `NotSigned`;
- candidate executed: `false`;
- WinDivert present: `false`;
- driver installed: `false`.

Эти hashes являются **reference identities**, а не заранее разрешёнными runtime artifacts.

Перед любым будущим Linux lab execution binary должен быть заново собран из exact source/patch/toolchain recipe, его SHA-256 должен быть явно записан в candidate-specific approval и отдельно одобрен человеком.

## Synthetic resource evidence

Resource Stress PASS:

- run: `40802559408`;
- artifact: `12866926284`;
- ZIP SHA-256: `41ef8ae61b65fe2bcc674b2626bbd3a0689069ad332f51d62212cb66567a8992`.

Synthetic conntrack caps, которые уже проходили stress harness:

- `64`;
- `512`;
- `2048`.

Parser fuzz PASS:

- run: `40802559290`;
- artifact: `12866891622`;
- ZIP SHA-256: `353d9d59a765d8517ad2a6bdd02a4db522b988ab92f5a111609005c5daab8155`;
- runs: `25000`;
- crash artifacts: `0`.

### Важное ограничение

Значения `64/512/2048` **не являются выбранными lab или production budgets**.

Они доказывают только bounded semantics и отсутствие обнаруженной ASan/UBSan ошибки в покрытом synthetic stress.

Поэтому в lab-plan сохраняются:

- `conntrack_max_entries: null`;
- `delayed_queue_max_packets: null`;
- `delayed_queue_max_bytes: null`;
- `production_values_selected: false`;
- `lab_values_selected: false`.

Числовые lab budgets должны быть отдельно предложены и одобрены перед executable stage. Production budgets потребуют более позднего workload evidence.

## Что остаётся до `LAB_APPROVED` Linux

### 1. Human approval exact scope

Нужно отдельное HIGH_ASSURANCE решение, в котором явно указаны:

- exact candidate identity;
- Linux platform;
- exact rebuilt binary SHA-256;
- конкретная разрешённая стадия lab-plan;
- explicit nonzero temporary lab resource budgets;
- disposable VM/snapshot;
- rollback procedure.

Обычное «продолжаем разработку», merge approval или compile PASS таким разрешением не являются.

### 2. Exact Linux lab binary rebuild

Нужно заново собрать Linux binary по pinned source/patch/toolchain recipe.

SHA-256 должен быть проверен до запуска и явно внесён в approval.

### 3. Explicit temporary lab budgets

До LNX-1/LNX-2 должны быть заданы ненулевые:

- conntrack max entries;
- delayed queue max packets;
- delayed queue max bytes.

При ошибке или отсутствии конфигурации candidate должен оставаться fail-closed и не создавать unlimited state.

### 4. Disposable environment

Только отдельная Linux VM с clean snapshot.

Запрещены:

- рабочие пользовательские данные;
- browser profiles;
- cookies/tokens/private keys;
- credentials;
- packet payload capture.

### 5. Privileged network lifecycle

Первый runtime должен проверить:

- foreground supervised start;
- controlled stop;
- timeout;
- forced termination;
- process-tree cleanup;
- временные NetPilot-owned NFQUEUE/firewall objects;
- cleanup этих objects на normal stop/crash/timeout;
- отсутствие residual routes/listeners/rules.

### 6. Fail-safe traffic path

При resource exhaustion, candidate error или cleanup path пользовательский traffic не должен зависать из-за оставшихся NetPilot-owned rules.

## Что остаётся вне первого Linux lab

Первый approved Linux lab не должен автоматически включать:

- реальные Discord/YouTube targets;
- public-service compatibility;
- production profiles;
- daemon/service/autostart;
- persistent auto-hostlist;
- experimental DNS/ipcache correlation;
- Windows WinDivert;
- production updater integration.

Эти стадии требуют отдельных evidence и approvals.

## Windows status

Windows zapret2 остаётся **BLOCK** для runtime.

Compile-only Windows output не разрешает запуск, потому что WinDivert boundary отдельно требует:

- exact DLL/SYS admission;
- driver lifecycle evidence;
- Defender/AV behavior review;
- start/stop/crash cleanup;
- отдельный HIGH_ASSURANCE Windows lab approval.

## Current decision

Zapret2 hardened candidate может оставаться в состоянии:

**PRE_LAB_REVIEW для Linux**.

Текущая политика неизменна:

- `admission_status: REVIEW`;
- `release_gate: BLOCK`;
- `may_execute: false`;
- `may_integrate: false`;
- `lab_execution_allowed: false`;
- lab plan `DRAFT_NOT_APPROVED`;
- `execution_allowed: false`.

Следующее повышение возможно только отдельным reviewed change с explicit HIGH_ASSURANCE human approval exact Linux lab scope.
