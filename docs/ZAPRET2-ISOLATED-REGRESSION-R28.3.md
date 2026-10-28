# R28.3 — изолированная sanitizer-регрессия очереди zapret2

Дата: 28.10.2026.

Статус: **PASS_FOR_ISOLATED_PACKET_QUEUE_REGRESSION_ONLY / NOT ADMITTED / RELEASE GATE BLOCK / NATIVE ENGINE EXECUTION DISABLED**.

## Зачем проводился этот gate

Patch `0003-bounded-delayed-queue-bytes.patch` добавляет в delayed raw-packet queue отдельный байтовый бюджет и учёт `queued_bytes`. Одной успешной компиляции недостаточно: необходимо доказать, что учёт памяти корректно меняется при enqueue/dequeue/destroy, что существующий packet-count limit не потерян и что переполнение `size_t` отклоняется до выделения/копирования памяти.

Этот gate специально **не запускает `nfqws2`**, не создаёт сетевой перехват и не использует WinDivert/NFQUEUE.

## Exact candidate

- upstream: `bol-van/zapret2`;
- source commit: `4a33055448ce5f4e7acc704e1fbe0d8a8a6aa08d`;
- source parent: `221d7dff24bfec0cd5ac0a5070b8acef2008af3d`;
- source tree: `5c2a77786cc7089145abe2ca99bcc53a8ed7de15`;
- NetPilot patches: `0001–0003`;
- patch `0001` SHA-256: `af811313b37efc14f157bdc1744e9a74ea3c009b03fc1d518d9ab327a35facff`;
- patch `0002` SHA-256: `a3531211626ce602a3b5cab6c926a28e7a94ebdf2a475e3bcfa6829e64dea783`;
- patch `0003` SHA-256: `33fb3c7887e5e2cc0ad225d74318a9f2d5192179cf6b7671da75f75bd7335c68`.

## Harness

Файл:

`security/native-admission/harnesses/zapret2-r28/packet_queue_budget_harness.c`

SHA-256 harness: `7b9b1300a9ca3468900899d665476f58475fff1b5627626d6ff06b672cba2571`.

Harness компилирует только:

- сам тестовый файл;
- exact patched `nfq2/packet_queue.c`.

Он не линкует и не запускает основной executable zapret2.

Проверяемые сценарии:

1. `queued_bytes` после init равен `0`;
2. пакет, укладывающийся в byte budget, принимается;
3. пакет, превышающий byte budget, отклоняется;
4. отклонённый enqueue не меняет счётчик;
5. заполнение ровно до лимита разрешено;
6. прежний `max_packets` продолжает ограничивать количество пакетов;
7. dequeue уменьшает `queued_bytes`;
8. освободившийся byte budget можно использовать повторно;
9. destroy опустошает очередь и обнуляет accounting;
10. `SIZE_MAX - 1 + 2` отклоняется overflow guard до allocation/copy.

## Sanitizers и отсутствие сети

Harness собран GCC 13.3.0 с:

- AddressSanitizer;
- UndefinedBehaviorSanitizer;
- `-fno-sanitize-recover=all`;
- `-fno-omit-frame-pointer`;
- `-Wall -Wextra -Werror`.

При основном запуске включены leak detection и немедленное завершение при sanitizer error.

Отдельный повторный запуск выполнен под `strace -e trace=network`. Gate отклоняет результат, если видит `socket`, `connect`, `bind`, `listen`, `send*`, `recv*` и аналогичные сетевые syscalls.

`network-syscalls.log` в успешном evidence пустой.

## Результат CI

Workflow: `Native Candidate Queue Sanitizer Regression`.

- run: `40721638421`;
- head: `b56812d86458ae77ee597056e8963baf90f0990b`;
- conclusion: `success`;
- artifact: `12831750192`, `zapret2-r28-packet-queue-sanitizer-regression-evidence`;
- artifact ZIP SHA-256: `fea5258fd402e333ac66588d9967ee3d6e3168869a9e540229c947c0c2b0f096`;
- instrumented harness binary SHA-256: `0558a19d557768ff4ccb5c62370fe724aafca0b120c70e8f66283fc439bbe2ad`;
- `compile_outcome=success`;
- `regression_outcome=success`;
- `no_network_syscalls_outcome=success`;
- итог harness: `packet_queue_budget_harness: PASS`.

Evidence также явно фиксирует:

- `candidate_engine_executed=false`;
- `windivert_present=false`;
- `driver_installed=false`;
- `network_interception_enabled=false`.

Instrumented harness binary удаляется до публикации artifact; artifact содержит только текстовые evidence-файлы.

## Что этот PASS означает

PASS относится только к проверенным функциям patched packet queue и сценариям выше. Он повышает уверенность, что patch `0003`:

- корректно ведёт byte accounting в проверенных переходах;
- не убирает packet-count limit;
- не допускает проверенное арифметическое переполнение;
- в этом harness не даёт ASan/UBSan/leak ошибок;
- не выполняет сетевых операций.

## Что этот PASS НЕ означает

Он **не доказывает**:

- безопасность всего `nfqws2`;
- безопасность TLS/QUIC/DNS/parser paths;
- корректность conntrack saturation patch `0002` под нагрузкой;
- правильные production значения `max_entries` и `max_bytes`;
- безопасность Lua boundary;
- безопасность WinDivert lifecycle;
- работу Discord/YouTube;
- crash recovery полного native process;
- отсутствие всех уязвимостей.

Поэтому candidate остаётся `REVIEW`, release gate — `BLOCK`, `may_execute: false`, `may_integrate: false`.

## Следующие gates R28.3

1. завершить и оценить GCC `-fanalyzer` + sanitizer-instrumented compile;
2. сделать отдельный isolated conntrack saturation harness для `RESOURCE_EXHAUSTED -> VERDICT_PASS` semantics;
3. parser/fuzz regression для ограниченных TLS/QUIC/hostname paths;
4. собрать workload evidence для выбора production `max_entries/max_bytes`;
5. только после изолированных gates переходить к отдельному runtime lab полного процесса.
