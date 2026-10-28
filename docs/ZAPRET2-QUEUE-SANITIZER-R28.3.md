# R28.3 — изолированная ASan/UBSan-проверка packet queue zapret2

Дата: 28.10.2026.

Статус: **PASS FOR ISOLATED PACKET-QUEUE SANITIZER REGRESSION ONLY / NOT ADMITTED / RELEASE GATE BLOCK**.

## Цель

Эта проверка относится только к механике очереди пакетов после применения контролируемой цепочки:

- zapret2 v1.0.6 exact release commit `221d7dff24bfec0cd5ac0a5070b8acef2008af3d`;
- exact upstream UAF fix `4a33055448ce5f4e7acc704e1fbe0d8a8a6aa08d`;
- NetPilot patches `0001`–`0004`.

Проверка не запускает `winws2`, не устанавливает WinDivert и не включает сетевой перехват.

## Workflow и результат

Workflow: `Native Candidate Queue Sanitizer Regression`.

Run: `40725210869`.

Job: `122946503505`.

Результат: `success`.

Evidence artifact:

- ID: `12831748330`;
- имя: `zapret2-r28-packet-queue-sanitizer-regression-evidence`;
- ZIP SHA-256: `c6741937eecf81cad86af184e077ec82b50353b74d8cbbdb69994e6575c0e939`.

Текстовая фиксация сохранена в:

`security/native-admission/evidence/zapret2-r28/packet-queue-sanitizer-regression.txt`.

## Что проверялось

Harness компилировался с:

- AddressSanitizer;
- UndefinedBehaviorSanitizer;
- `-fno-sanitize-recover=all`;
- `-fno-omit-frame-pointer`;
- `-Wall -Wextra -Werror`.

Regression harness завершился строкой `packet_queue_budget_harness: PASS`.

Дополнительно через `strace` проверено отсутствие сетевых системных вызовов (`socket`, `connect`, `bind`, `listen`, `sendto`, `recvfrom`, `sendmsg`, `recvmsg` и связанных вызовов) в рамках harness-run.

## Что доказано

PASS подтверждает только то, что протестированная packet-queue реализация с byte-accounting и overflow-safe проверками проходит данный изолированный ASan/UBSan regression harness без обнаруженной ошибки памяти или UB в покрытом сценарии.

Это полезное evidence для patch `0003` и общей R28.3 цепочки, но оно не является доказательством безопасности всего `nfqws2/winws2`.

## Что НЕ доказано

Эта проверка не подтверждает:

- безопасность всего parser/desync/Lua/conntrack кода;
- корректность production `max_entries` и `max_bytes`;
- устойчивость под реальным browser/YouTube/Discord/burst/hostile workload;
- безопасность WinDivert DLL/SYS и driver lifecycle;
- start/stop/crash cleanup реального процесса;
- отсутствие других memory-safety дефектов;
- пригодность кандидата к production.

## Состояние admission

После этого PASS статус кандидата не меняется:

- `admission_status: REVIEW`;
- `release_gate: BLOCK`;
- `may_execute: false`;
- `may_integrate: false`.

## Следующие обязательные gates

1. завершить full static/sanitizer compile evidence для выбранной source/patch цепочки;
2. добавить parser/fuzz regression для критичных TLS/QUIC/hostname/conntrack путей;
3. провести resource/load measurement и выбрать production budgets по evidence;
4. завершить exact WinDivert DLL/SYS inventory и lifecycle review;
5. выполнить isolated runtime lab start/stop/restart/crash/cleanup + HTTPS/YouTube/Discord TCP/UDP;
6. повторить advisory/CVE/KEV review exact component set;
7. отдельный HIGH_ASSURANCE/human review перед любым `ADMIT`.
