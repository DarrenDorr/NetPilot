# R28.9 — fail-closed zero delayed-queue byte budget и promotion patchset 0001–0006

Дата: 29.10.2026.

Статус: **PATCH 0006 PROMOTED TO CURRENT REVIEW PATCHSET / ALL AUTHORITATIVE GATES PASS / NOT ADMITTED / RELEASE GATE BLOCK**.

## Что обнаружено

В patch `0003-bounded-delayed-queue-bytes.patch` были добавлены `max_bytes` и `queued_bytes`, однако исходная проверка enqueue имела вид:

```c
if (q->max_bytes && new_queued_bytes > q->max_bytes) return NULL;
```

При `max_bytes == 0` это означало отсутствие byte-limit. Synthetic harnesses предыдущих этапов явно устанавливали положительный budget и поэтому не доказывали fail-closed семантику фактического unconfigured/zero состояния.

Production/native runtime всё это время оставался запрещённым, поэтому найденный gap не означает, что пользовательский production runtime уже работал с unlimited queue. Но gap являлся release/admission blocker для будущего runtime lab.

## Patch 0006

Файл:

`security/native-admission/patches/zapret2-r28/0006-fail-closed-zero-delayed-queue-budget.patch`

Immutable identity:

- Git blob: `2a5165f5b562635dace89ae5069bdb791b69668c`;
- SHA-256: `f5f3c01c73db3584513adc345da838bef6035c95a59762c469a386c7ad201d97`.

Минимальное изменение:

```c
if (!q->max_bytes || new_queued_bytes > q->max_bytes) return NULL;
```

Новая семантика:

- `max_bytes == 0` → byte budget не настроен, новый delayed packet state отклоняется;
- положительный `max_bytes` → enqueue разрешён только внутри установленного byte budget;
- packet-count cap сохраняется;
- overflow guard сохраняется;
- новых CLI/config/network/runtime поверхностей patch не добавляет.

## Dedicated zero-budget gate — PASS

Workflow:

`.github/workflows/native-candidate-delayed-queue-zero-budget.yml`

Harness:

`security/native-admission/harnesses/zapret2-r28/delayed_queue_zero_budget_harness.c`

Dedicated semantic evidence подтвердило:

- exact upstream/UAF-fix lineage;
- применение patches `0001–0006` через `git apply --check`;
- zero/unconfigured budget отклоняет enqueue без изменения accounting;
- положительный budget принимает данные только внутри лимита;
- ASan/UBSan regression PASS;
- network syscalls отсутствуют;
- candidate engine не запускался;
- WinDivert/driver/NFQUEUE/firewall не использовались.

Первый dedicated run имел packaging/identity ошибку ожидаемого SHA-256 и не дошёл до semantic execution. После исправления workflow semantic gate прошёл; семантика patch при этом не изменялась.

## Первичные whole-engine compile-only evidence — PASS

До полного repin отдельно были получены:

### Linux

- run: `40822097108`;
- artifact: `12875286031`;
- artifact digest SHA-256: `ec309d25dec706cb894f9ab444bd61855d85fe7647b857873558e105607a5bc3`;
- candidate: `zapret2-v1.0.6-plus-uaf-fix-plus-netpilot-patches-0001-0006-proposed`;
- `nfqws2` SHA-256: `afda31bb33f82a45233a991815af12fe5ee5d4c07da63f98a8851eac023a9397`;
- candidate binary не исполнялся.

### Windows

- run: `40822373123`;
- artifact: `12874874163`;
- artifact digest SHA-256: `e1d0a38c86f12e67ea32cb2d13dba78003227b482631e73a98be8ad9aa2443cf`;
- candidate: `zapret2-v1.0.6-plus-uaf-fix-plus-netpilot-patches-0001-0006-proposed`;
- `winws2` SHA-256: `6769357c48c4d203ac1a739f1d2d8a4714f96acb7f3bc30656366154a761ee30`;
- Authenticode: `NotSigned`;
- WinDivert отсутствовал и driver не устанавливался;
- candidate binary не исполнялся.

Эти два compile PASS сами по себе ещё не являлись promotion.

## Full patchset repin — PASS

После добавления `0006` были перепинены authoritative zapret2 workflows, которые применяют или идентифицируют patchset.

Authoritative head:

`1b0153f88423828a49cd4bda6502f2ff4d3a79e7`

На этом head весь PR workflow-набор завершён `success`: **26/26**.

### Current `0001–0006` evidence

| Gate | Run | Artifact | Artifact SHA-256 |
|---|---:|---:|---|
| Source provenance | `40824094103` | `12875723177` | `423dc9a7866e3f5c3976e94362eebd9655a49cb00c4cc078310e125d7c62637d` |
| Linux compile smoke | `40824094110` | `12876185338` | `de82f78424ac7c5a6dee16370f4c5529297c9121aa327a7a96a499d725b6ffaf` |
| Windows compile smoke | `40824094078` | `12876191346` | `e7984d36852c95fad3b595d1cca20137496e4bab446e311f2a4b079f7c7e47ee` |
| Packet queue sanitizer | `40824094134` | `12875353065` | `719dafe99a716cc66b3da2cbf28709f4f5a2183ad0b647abd59a7d30c2e90deb` |
| Static/sanitizer compile | `40824094083` | `12875643418` | `9e1cfcd2ddf73b63fe6108c7dab0951217c2e0dc4094da5171f2cd11b4e70650` |
| Protocol parser sanitizer | `40824093994` | `12875278216` | `ba1b396d78d08218aff47356e67cdf2d2e9fc5ed64547086e96bc1ea9f691a8f` |
| Protocol parser fuzz | `40824094120` | `12875153542` | `96fb0a107ef6207faa7de03d279b2365d4a1cbaf11bfc69aead22d60cceada3a` |
| Conntrack budget regression | `40824094128` | `12875643288` | `6f3526d589e55e9c265ccdde264daa9e5cda8fc7d4e8275585fd3e8fe3729592` |
| Synthetic resource stress | `40824094104` | `12876131152` | `9f962eccbbad7fda2dc01ce2baa7b058cb9c7dc1c1bd123857f627551a068a5e` |
| Delayed queue zero budget | `40824094087` | `12875673331` | `78de9e162163f989b815404f86c1145605fc671843a9fec8783172dd5264a777` |

Кроме zapret2-specific gates на том же head прошли основной `CI`, CodeQL, Windows dependency intake, WinDivert inventory, ByeDPI/DPIBreak scoped gates и lab-policy checks.

## Решение по promotion

Patch `0006` переводится из `PROPOSED` в **current review patchset**.

Canonical patchset теперь:

`0001–0006`

Это решение означает только следующее:

- exact patch identity согласована с authoritative CI;
- current candidate manifest может ссылаться на `0001–0006`;
- дальнейший design/static/lab planning должен использовать `0001–0006`.

Это **не** означает:

- `ADMIT`;
- разрешение `EngineInstall`;
- разрешение `EngineStart`;
- разрешение packet interception;
- разрешение WinDivert driver load;
- разрешение Linux NFQUEUE runtime;
- разрешение production integration.

## Что patch 0006 не решает

Zero-budget fail-closed semantics не выбирает положительный `max_bytes`.

До любого executable lab необходимо отдельно:

- спроектировать typed/trusted внутреннюю передачу положительных conntrack и delayed-queue budgets;
- исключить произвольные значения из user/config/raw CLI поверхности;
- определить bounds и fail-closed validation;
- выбрать lab budget только по утверждённой evidence-based методике;
- измерить application workload peaks в изолированной среде;
- получить отдельное HIGH_ASSURANCE human approval для executable lab.

## Текущая admission-позиция

После promotion `0006` остаётся:

- `admission_status: REVIEW`;
- `release_gate: BLOCK`;
- `may_execute: false`;
- `may_integrate: false`;
- `lab_execution_allowed: false`;
- Windows runtime: BLOCK из-за WinDivert runtime/lifecycle/signing boundary;
- Linux runtime: REVIEW, но executable lab пока не разрешён;
- production resource budgets: не выбраны.

Следующий безопасный этап: **R28.10 typed/trusted resource-budget wiring design**, без native execution.
