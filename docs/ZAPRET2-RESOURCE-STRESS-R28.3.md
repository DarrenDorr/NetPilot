# R28.3 — synthetic resource stress zapret2

Дата: 29.10.2026.

Статус: **PASS FOR ISOLATED SYNTHETIC RESOURCE STRESS ONLY / NOT ADMITTED / RELEASE GATE BLOCK**.

## Цель

Проверить, что контролируемый zapret2-кандидат после NetPilot patches `0001`–`0005` сохраняет bounded/fail-closed семантику conntrack и delayed packet queue при многократном искусственном насыщении.

Проверка намеренно не запускает `nfqws2/winws2`, не загружает WinDivert, не устанавливает драйвер и не включает сетевой перехват.

Проверяемая база:

- zapret2 v1.0.6 commit `221d7dff24bfec0cd5ac0a5070b8acef2008af3d`;
- exact upstream UAF fix `4a33055448ce5f4e7acc704e1fbe0d8a8a6aa08d`;
- source tree `5c2a77786cc7089145abe2ca99bcc53a8ed7de15`;
- NetPilot patches `0001`–`0005`;
- resource harness blob `9cb70ace93bbda943495117123a2b5dcfbd30cd7`.

## Результат

Workflow: `Native Candidate Resource Stress`.

- run: `40802559408`;
- job: `123213833865`;
- head: `646be8205092584b775903eb646a3d9e21560fae`;
- conclusion: `success`.

Artifact:

- ID: `12866926284`;
- name: `zapret2-r28-resource-stress-evidence`;
- ZIP SHA-256: `41ef8ae61b65fe2bcc674b2626bbd3a0689069ad332f51d62212cb66567a8992`.

Текстовая фиксация сохранена в:

`security/native-admission/evidence/zapret2-r28/resource-stress.txt`.

## Что проверялось

Harness собран с AddressSanitizer и UndefinedBehaviorSanitizer.

### Conntrack

Проверены caps:

- `64`;
- `512`;
- `2048`.

Для каждого cap:

1. state создаётся ровно до установленного budget;
2. после saturation выполняется ещё столько же попыток создать новые flow;
3. каждая сверхлимитная попытка обязана вернуть `CTRACK_FEED_RESOURCE_EXHAUSTED`;
4. размер pool не должен увеличиваться выше cap;
5. ранее существующий flow продолжает находиться при saturation;
6. после `max_entries = 0` новый state не создаётся;
7. `ConntrackPoolDestroy()` обязан освободить весь state.

### Delayed packet queue

Проверены две модели:

- packet cap `64`, packet size `1500`, суммарно `96000` bytes, 200 циклов saturation/drain;
- byte cap `4096`, четыре пакета по `1024`, 500 циклов saturation/drain.

После каждого drain счётчик `queued_bytes` обязан возвращаться в `0`; enqueue сверх packet/byte budget обязан отклоняться без изменения accounting.

## Наблюдаемые ресурсы

В instrumented ASan/UBSan run:

- elapsed wall time: около `0.04 s`;
- maximum resident set size: `48068 KiB`;
- socket messages sent: `0`;
- socket messages received: `0`;
- exit status: `0`.

Через отдельный `strace -e trace=network` также подтверждено отсутствие сетевых syscall в harness-run.

Эти числа относятся к короткому synthetic sanitizer-harness на GitHub-hosted Ubuntu runner и **не являются оценкой production-потребления памяти самого движка**.

## Что PASS означает

Под покрытой синтетической нагрузкой проверенные механизмы:

- не превысили заданные conntrack caps;
- сохранили fail-closed поведение при нулевом/исчерпанном budget;
- не нарушили packet/byte accounting очереди;
- корректно освобождали тестовый state;
- не показали ASan/UBSan ошибок;
- не выполняли сетевые операции.

## Что PASS НЕ означает

Этот gate не определяет production значения `max_entries` или `max_bytes`.

Он не моделирует реальные:

- браузинг;
- YouTube;
- Discord TCP/UDP;
- combined workload;
- burst на реальной сети;
- WinDivert/NFQUEUE lifecycle;
- start/stop/crash cleanup процесса.

Числовые production budgets должны быть выбраны только после controlled workload measurements. До этого typed production adapter не должен активировать candidate runtime.

## Admission effect

Статус кандидата не повышается:

- `admission_status: REVIEW`;
- `release_gate: BLOCK`;
- `may_execute: false`;
- `may_integrate: false`.

Следующие обязательные gates: coverage-guided parser fuzzing, platform-specific isolated runtime evidence, production workload measurements, component-set/advisory review и отдельный HIGH_ASSURANCE/human review.
