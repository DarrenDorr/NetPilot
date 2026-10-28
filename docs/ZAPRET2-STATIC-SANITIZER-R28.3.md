# R28.3 — static analyzer + sanitizer compile zapret2

Дата: 28.10.2026.

Статус: **PASS FOR STATIC ANALYSIS AND SANITIZER COMPILE ONLY / NOT ADMITTED / NATIVE EXECUTION DISABLED**.

## Проверяемая цепочка

- zapret2 v1.0.6 exact release commit `221d7dff24bfec0cd5ac0a5070b8acef2008af3d`;
- upstream UAF fix `4a33055448ce5f4e7acc704e1fbe0d8a8a6aa08d`;
- NetPilot patches `0001`–`0004`.

## Workflow

`Native Candidate Static and Sanitizer Compile`

Run: `40726114351`.

Job: `122949718942`.

Результат: `success`.

Artifact:

- ID: `12832897860`;
- имя: `zapret2-r28-static-sanitizer-compile-evidence`;
- ZIP SHA-256: `7566b0b62de71f966522866d976f7dd86ba264749116003912a0c265926a014c`.

Текстовая фиксация сохранена в:

`security/native-admission/evidence/zapret2-r28/static-sanitizer-compile.txt`.

## GCC static analyzer

Компилятор: GCC 13.3.0 на Ubuntu 24.04.

Сборка выполнялась с `-fanalyzer`. Следующие категории оставлены hard-fail через `-Werror`:

- use-after-free;
- double-free;
- null-dereference;
- fd-use-after-close.

Дополнительно workflow анализирует все оставшиеся `-Wanalyzer-*` предупреждения.

### Узкая allowlist daemon stdio

GCC сообщает три `-Wanalyzer-fd-leak` для `dup2(..., 0/1/2)` внутри `daemonize()`. В этом случае дескрипторы не являются случайно забытыми ресурсами: после daemonization стандартные `stdin`, `stdout`, `stderr` намеренно остаются открытыми на `/dev/null` на весь срок жизни процесса.

Поэтому workflow допускает **ровно три** таких предупреждения только из `sec.c` и только при наличии контекста `daemonize`. Любое другое `-Wanalyzer-*` предупреждение продолжает завершать gate ошибкой.

В успешном run зафиксировано:

`expected_daemon_stdio_fd_retention_warnings=3`.

Analyzer-build SHA-256: `de49a5cb2c1e2b2bc02e2c0daa84df124159001b5d1b0759180c33ee260dccce`.

## ASan / UBSan compile

Отдельно успешно собран instrumented binary с AddressSanitizer, UndefinedBehaviorSanitizer и alignment sanitizer.

Instrumented binary SHA-256: `7261c6415eafed4faf90693c8160b14756cab21de9643b3181c9daaf04810d56`.

Этот binary **не запускался**. Данный результат подтверждает только возможность sanitizer instrumentation/compile. Runtime sanitizer evidence проводится отдельными harness-тестами.

## Что результат НЕ означает

PASS не означает:

- полный аудит всего `nfqws2/winws2`;
- отсутствие неизвестных parser bugs;
- безопасный WinDivert lifecycle;
- подтверждённые production resource limits;
- проверку реального Discord/YouTube трафика;
- разрешение на запуск кандидата в NetPilot.

`admission_status` остаётся `REVIEW`, `release_gate` — `BLOCK`, `may_execute: false`, `may_integrate: false`.

## Следующие gates

1. parser/fuzz regression критичных TLS/QUIC/hostname/conntrack путей;
2. resource/load measurements и выбор production budgets по измерениям;
3. driver lifecycle lab для exact WinDivert component set;
4. isolated runtime start/stop/restart/crash/cleanup;
5. HTTPS/YouTube/Discord TCP/UDP functional matrix;
6. повторный advisory/CVE/KEV review;
7. отдельный HIGH_ASSURANCE/human review перед `ADMIT`.
