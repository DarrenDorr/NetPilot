# R28.9 — fail-closed zero delayed-queue byte budget

Дата: 29.10.2026.

Статус: **PROPOSED PATCH / DEDICATED GATE PASS / WHOLE-ENGINE COMPILE PENDING / NOT ADMITTED / RELEASE GATE BLOCK**.

## Что обнаружено

В patch `0003-bounded-delayed-queue-bytes.patch` добавлены `max_bytes` и `queued_bytes`, однако `rawpacket_queue_init()` инициализирует `max_bytes = 0`, а проверка enqueue имела вид:

```c
if (q->max_bytes && new_queued_bytes > q->max_bytes) return NULL;
```

Следствие: `max_bytes == 0` означает отсутствие byte-limit.

Synthetic harnesses ранее явно вызывали `rawpacket_queue_set_max_bytes(...)`, поэтому они доказали корректность accounting и saturation при **заданном положительном budget**, но не доказали bounded-byte semantics фактического runtime до явной настройки budget.

Exact upstream `ConntrackInitTrack()` создаёт delayed queue через `rawpacket_queue_init(&t->delayed, RAW_PACKET_QUEUE_DELAYED_MAX)`; отдельная runtime-настройка byte budget в текущем promoted patchset `0001–0005` не подключена.

Production/native runtime по-прежнему запрещён, поэтому обнаруженный gap не означает, что пользовательский runtime уже работал с unlimited queue. Но до исправления нельзя считать delayed-byte bound готовым для будущего runtime lab.

## Proposed patch 0006

Файл:

`security/native-admission/patches/zapret2-r28/0006-fail-closed-zero-delayed-queue-budget.patch`

Immutable identity:

- Git blob: `2a5165f5b562635dace89ae5069bdb791b69668c`;
- SHA-256: `f5f3c01c73db3584513adc345da838bef6035c95a59762c469a386c7ad201d97`.

Изменение минимальное:

```c
if (!q->max_bytes || new_queued_bytes > q->max_bytes) return NULL;
```

Новая семантика:

- `max_bytes == 0` → byte budget не настроен, delayed packet state не создаётся;
- положительный `max_bytes` → enqueue допускается только внутри установленного byte budget;
- packet-count cap сохраняется независимо;
- overflow guard сохраняется;
- никаких новых CLI/config/network/runtime поверхностей patch не добавляет.

## Dedicated gate — PASS

Workflow:

`.github/workflows/native-candidate-delayed-queue-zero-budget.yml`

Harness:

`security/native-admission/harnesses/zapret2-r28/delayed_queue_zero_budget_harness.c`

Успешный run:

- run: `40821736780`;
- job: `123275527526`;
- artifact: `12874324817`;
- artifact digest SHA-256: `7940463b0dca2601e8bacc0a00a3f40c4ae2abad8939817406e9c8746df818a3`;
- harness SHA-256: `9aec197b0cdc168e3a1f85823be4b27032271046c807427bceb08a3d17d0e16c`;
- instrumented harness binary SHA-256: `8bc390536d629a001ef828401a1c11dba6ed37f8adcbf0292fbd2d9bb935307e`.

Доказано в scope dedicated gate:

- exact upstream/UAF-fix lineage проверен;
- patches `0001–0006` применяются через `git apply --check`;
- zero/unconfigured budget отклоняет delayed enqueue без изменения accounting;
- положительный budget принимает данные только внутри установленного лимита;
- ASan/UBSan regression: PASS;
- network syscalls: отсутствуют;
- candidate engine не запускался;
- WinDivert/driver/NFQUEUE/firewall не использовались;
- instrumented harness binary удалён после evidence.

Первый run `40821515752` остановился до применения patch/компиляции из-за преждевременно закреплённого SHA-256 текста `0006`, рассчитанного до записи окончательного Git blob. Git blob identity совпала. Workflow был исправлен без изменения семантики patch, а успешный run выше вычислил и зафиксировал exact SHA-256 checkout-содержимого.

## Следующий promotion gate

Dedicated harness PASS недостаточен для promotion в current patchset.

Следующий этап:

1. Linux whole-engine compile-only на exact `0001–0006`, без исполнения `nfqws2`;
2. Windows whole-engine compile-only на exact `0001–0006`, без исполнения `winws2`;
3. после обоих PASS — повторное перепинивание остальных static/sanitizer/fuzz/resource/provenance gates на `0001–0006`;
4. только после полного green patchset — обновление candidate manifest с promoted `0006`.

## Что этот patch НЕ решает

Patch `0006` пока **не выбирает** положительный lab/production byte budget и не добавляет runtime wiring для такого значения.

После promotion остаются отдельные задачи:

- спроектировать trusted внутреннюю передачу положительного byte budget в новые conntrack tracks;
- выбрать временный lab budget только по утверждённой evidence-based методике;
- отдельно измерить реальные workload peaks до production значения;
- получить HIGH_ASSURANCE human approval до любого executable lab stage.

## Admission effect

На текущем этапе:

- promoted current patchset остаётся `0001–0005`;
- `0006` остаётся proposed, хотя dedicated gate уже PASS;
- `admission_status: REVIEW`;
- `release_gate: BLOCK`;
- `may_execute: false`;
- `may_integrate: false`;
- Linux lab execution остаётся запрещённым.
