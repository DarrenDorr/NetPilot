# R28.9 — fail-closed zero delayed-queue byte budget

Дата: 29.10.2026.

Статус: **PROPOSED PATCH / DEDICATED GATE PENDING / NOT ADMITTED / RELEASE GATE BLOCK**.

## Что обнаружено

В patch `0003-bounded-delayed-queue-bytes.patch` добавлены `max_bytes` и `queued_bytes`, однако `rawpacket_queue_init()` инициализирует `max_bytes = 0`, а проверка enqueue имела вид:

```c
if (q->max_bytes && new_queued_bytes > q->max_bytes) return NULL;
```

Следствие: `max_bytes == 0` означает отсутствие byte-limit.

Synthetic harnesses ранее явно вызывали `rawpacket_queue_set_max_bytes(...)`, поэтому они доказали корректность accounting и saturation при **заданном положительном budget**, но не доказали bounded-byte semantics фактического runtime до явной настройки budget.

Exact upstream `ConntrackInitTrack()` создаёт delayed queue через `rawpacket_queue_init(&t->delayed, RAW_PACKET_QUEUE_DELAYED_MAX)`; отдельная runtime-настройка byte budget в текущем patchset `0001–0005` не подключена.

Это не означает, что candidate запускался с unlimited queue: production/native runtime по-прежнему запрещён. Но до исправления нельзя считать delayed-byte bound готовым для будущего runtime lab.

## Proposed patch 0006

Файл:

`security/native-admission/patches/zapret2-r28/0006-fail-closed-zero-delayed-queue-budget.patch`

SHA-256:

`a507b5619beccd27c1dd06576c203d2badb8857655192649c8ae455299701ccb`

Git blob:

`2a5165f5b562635dace89ae5069bdb791b69668c`

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

## Почему это fail-closed

До выбора доказанного положительного lab/production byte budget отсутствие настройки не должно означать «безлимитно».

Нулевое значение намеренно отключает создание delayed packet state. Это может уменьшить эффективность конкретных desync/reassembly сценариев, но не расширяет расход памяти и не превращает unconfigured state в unlimited resource grant.

## Dedicated gate

Workflow:

`.github/workflows/native-candidate-delayed-queue-zero-budget.yml`

Harness:

`security/native-admission/harnesses/zapret2-r28/delayed_queue_zero_budget_harness.c`

Gate обязан:

1. проверить exact upstream source/UAF-fix lineage;
2. проверить immutable identities patches `0001–0006` и harness;
3. применить patches `0001–0006` через `git apply --check`;
4. собрать только isolated packet-queue harness с ASan/UBSan и `-Werror`;
5. доказать, что zero/unconfigured byte budget отклоняет enqueue без изменения accounting;
6. доказать, что положительный budget по-прежнему принимает данные внутри лимита и отклоняет превышение;
7. выполнить отдельный `strace -e trace=network` и потребовать отсутствие network syscalls;
8. не запускать `nfqws2/winws2`, не загружать WinDivert/NFQUEUE и не менять firewall;
9. удалить instrumented harness binary после формирования text-only evidence.

## Что этот patch НЕ решает

Patch `0006` пока **не выбирает** положительный lab/production byte budget и не добавляет runtime wiring для такого значения.

После PASS dedicated gate остаются отдельные задачи:

- спроектировать trusted внутреннюю передачу положительного byte budget в новые conntrack tracks;
- выбрать временный lab budget только по утверждённой evidence-based методике;
- отдельно измерить реальные workload peaks до production значения;
- повторить compile/static/sanitizer/fuzz/resource gates уже на promoted patchset `0001–0006`;
- получить HIGH_ASSURANCE human approval до любого executable lab stage.

## Admission effect

Пока dedicated gate не завершён успешно:

- current patchset остаётся `0001–0005`;
- `0006` считается только proposed;
- `admission_status: REVIEW`;
- `release_gate: BLOCK`;
- `may_execute: false`;
- `may_integrate: false`;
- Linux lab execution остаётся запрещённым.
