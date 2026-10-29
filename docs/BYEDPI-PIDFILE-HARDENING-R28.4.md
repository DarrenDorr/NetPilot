# R28.4 — fail-safe PID-file hardening ByeDPI v0.17.3

Дата: 29.10.2026.

Статус: **PASS FOR STATIC/COMPILE HARDENING ONLY / NOT ADMITTED / RELEASE GATE BLOCK**.

## Что было обнаружено

В exact upstream ByeDPI `v0.17.3` Linux/BSD PID-file path имел несколько reliability-проблем:

- при неуспешном `fcntl(F_SETLK)` открытый descriptor не закрывался;
- после успешного lock старое содержимое файла не очищалось;
- результат `write()` игнорировался, short write/EINTR не обрабатывались;
- `params.pid_file` публиковался до подтверждённой полной записи PID;
- cleanup закрывал descriptor только при `pid_fd > 0` и не сбрасывал state.

Это не доказанная security-уязвимость, но такой lifecycle не соответствует fail-safe требованиям Net.Pilot.

## Patch

`security/native-admission/patches/byedpi-r28/0003-pid-file-fail-safe-write.patch`

Immutable identity:

- Git blob SHA-1: `d56d0f8659a49c48c923b6af85a523a952e77e0c`;
- SHA-256: `5d10702499581eeba2f632aa506b5d6c51ceb3e8e093bff25b8dcbc821bb3738`.

Новая последовательность:

1. открыть PID-file без `O_TRUNC`;
2. получить `F_SETLK`;
3. только после успешного lock выполнить `ftruncate(0)` и `lseek(0)`;
4. проверить длину `snprintf`;
5. выполнить полный write-loop;
6. при `EINTR` повторить write;
7. zero/failed write считать ошибкой;
8. только после полной записи публиковать `params.pid_file`;
9. при любой ошибке закрывать descriptor и возвращать state в `pid_fd = -1`;
10. cleanup сбрасывает и descriptor, и `pid_file`.

Важно: файл намеренно **не truncate’ится до получения lock**, чтобы второй экземпляр не мог испортить PID-file уже работающего процесса.

## Gate

Workflow: `Native Candidate ByeDPI PID File Static`.

- run: `40804122796`;
- head: `6123bad3b16cad16b0ce9614f135beb6406e56b3`;
- conclusion: `success`.

Artifact:

- ID: `12867054119`;
- name: `byedpi-r28-pidfile-static-evidence`;
- ZIP SHA-256: `4210f83da85f2331729c4b728246e12f9df9e6ac3276e3e532b218e6b9893c56`.

Evidence сохранено в:

`security/native-admission/evidence/byedpi-r28/pidfile-static.txt`.

## Что проверялось

Workflow применяет exact chain `0001 -> 0002 -> 0003` к pinned source tree `8d13f6f...`.

Далее проверяются source anchors:

- lock расположен раньше truncate;
- присутствуют truncate + seek;
- присутствует полный write-loop;
- присутствует обработка `EINTR`;
- старый unchecked `write(...strlen(...))` отсутствует;
- `params.pid_file = fname` находится только после write-loop.

Реальный patched `main.c` компилируется GCC с `-Werror=unused-result`.

## Что PASS означает

Patch применим к exact source, требуемая fail-safe структура присутствует, а patched translation unit компилируется без игнорирования результата `write()`.

## Что PASS НЕ означает

Этот gate не открывал PID-file и не выполнял candidate binary. Поэтому ещё не доказаны:

- реальный inter-process lock lifecycle;
- поведение при двух одновременно стартующих процессах;
- реакция на filesystem errors/permissions/full disk в runtime;
- cleanup после kill/crash;
- daemon lifecycle в целом.

Эти сценарии относятся к будущему отдельному Linux lab-runtime после `LAB_APPROVED`.

## Admission effect

ByeDPI остаётся:

- `admission_status: REVIEW`;
- `release_gate: BLOCK`;
- `may_execute: false`;
- `may_integrate: false`.

Дополнительно R28.4 уже выявил отдельный KAVL erase UBSan blocker и потенциальный Win64 `SOCKET -> int` truncation boundary; они рассматриваются независимо.
