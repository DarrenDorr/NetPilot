# R28.9 — fail-closed zero delayed-queue byte budget

Дата: 29.10.2026.

Статус: **PROPOSED PATCH / DEDICATED GATE PASS / LINUX+WINDOWS WHOLE-ENGINE COMPILE PASS / FULL PATCHSET REPIN PENDING / NOT ADMITTED / RELEASE GATE BLOCK**.

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
- новых CLI/config/network/runtime поверхностей patch не добавляет.

## Dedicated zero-budget gate — PASS

Workflow: `.github/workflows/native-candidate-delayed-queue-zero-budget.yml`.

Harness: `security/native-admission/harnesses/zapret2-r28/delayed_queue_zero_budget_harness.c`.

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

Первый run `40821515752` остановился до применения patch/компиляции из-за packaging/identity ошибки ожидаемого SHA-256. Git blob identity совпала. Workflow был исправлен без изменения семантики patch; успешный run выше является фактическим semantic evidence.

## Linux whole-engine compile-only — PASS

Workflow: `.github/workflows/native-candidate-compile-smoke.yml`.

Успешный run:

- run: `40822097108`;
- artifact: `12875286031`;
- artifact digest SHA-256: `ec309d25dec706cb894f9ab444bd61855d85fe7647b857873558e105607a5bc3`;
- candidate: `zapret2-v1.0.6-plus-uaf-fix-plus-netpilot-patches-0001-0006-proposed`;
- `nfqws2` SHA-256: `afda31bb33f82a45233a991815af12fe5ee5d4c07da63f98a8851eac023a9397`;
- `nfqws2` size: `242824` bytes;
- `candidate_binary_executed=false`.

Exact `0001–0006` были проверены и применены; `nfqws2` успешно собран, проинвентаризирован и удалён. Исполнение candidate не выполнялось.

## Windows whole-engine compile-only — PASS

Workflow: `.github/workflows/native-candidate-windows-compile-smoke.yml`.

Успешный run:

- run: `40822373123`;
- job: `123277527334`;
- artifact: `12874874163`;
- artifact digest SHA-256: `e1d0a38c86f12e67ea32cb2d13dba78003227b482631e73a98be8ad9aa2443cf`;
- candidate: `zapret2-v1.0.6-plus-uaf-fix-plus-netpilot-patches-0001-0006-proposed`;
- `winws2` SHA-256: `6769357c48c4d203ac1a739f1d2d8a4714f96acb7f3bc30656366154a761ee30`;
- `winws2` size: `650240` bytes;
- Authenticode: `NotSigned`;
- `cygwin1.dll` SHA-256: `ed9d5379061e5de79c3375cc62246c2142ea1c97d94f4d16bfbea6fb1b7fc889`;
- `winws2_executed=false`;
- `windivert_present=false`;
- `windivert_driver_installed=false`.

Exact `0001–0006` были проверены и применены; pinned Cygwin/LuaJIT build и `winws2` compile завершились успешно. Candidate binary после inventory удалён и не исполнялся.

## Promotion всё ещё НЕ завершён

Два whole-engine compile PASS недостаточны для изменения current patchset в candidate manifest.

До promotion требуется перепинить и повторно получить PASS для всех существующих zapret2 gates, которые применяют или идентифицируют patchset, включая как минимум:

- source provenance;
- static/analyzer + sanitizer compile;
- packet-queue sanitizer regression;
- conntrack budget regression;
- synthetic resource stress;
- protocol parser sanitizer;
- protocol parser fuzz;
- остальные policy/evidence gates, если они фиксируют exact patchset identity.

Пока хотя бы один authoritative gate всё ещё проверяет `0001–0005`, manifest не должен утверждать `0001–0006` как current patchset.

## Что patch 0006 НЕ решает

Patch `0006` не выбирает положительный lab/production byte budget и не добавляет runtime wiring для такого значения.

Даже после будущего promotion `0006` zero-budget semantics будут означать fail-closed отключение delayed packet state до явной доверенной настройки положительного budget.

Отдельно остаётся необходимо:

- спроектировать typed/trusted внутреннюю передачу положительного byte budget в новые conntrack tracks;
- выбрать временный lab budget только по утверждённой evidence-based методике;
- измерить реальные workload peaks до production значения;
- получить HIGH_ASSURANCE human approval до любого executable lab stage.

## Admission effect

На текущем этапе:

- promoted current patchset в candidate manifest остаётся `0001–0005`;
- `0006` остаётся proposed, несмотря на dedicated + Linux + Windows compile PASS;
- `admission_status: REVIEW`;
- `release_gate: BLOCK`;
- `may_execute: false`;
- `may_integrate: false`;
- Windows runtime остаётся BLOCK по WinDivert boundary;
- Linux lab execution остаётся запрещённым.
