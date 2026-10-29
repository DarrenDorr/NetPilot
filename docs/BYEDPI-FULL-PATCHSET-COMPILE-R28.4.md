# R28.4 — full patchset compile-only gate ByeDPI v0.17.3

Дата: 29.10.2026.

Статус: **PASS FOR FULL PATCHSET `0001–0007` COMPILE-ONLY / NOT ADMITTED / RELEASE GATE BLOCK**.

## Цель

Подтвердить, что exact ByeDPI `v0.17.3` после применения полного текущего NetPilot hardening patchset `0001–0007`:

- проходит immutable source/patch identity gate;
- применяет patches последовательно к exact upstream source;
- проходит targeted Windows warning/source-boundary проверки;
- воспроизводимо компилируется для Linux x86-64;
- воспроизводимо cross-компилируется для Windows x64;
- не требует запуска candidate для получения evidence.

Ни `ciadpi`, ни `ciadpi.exe` в этой проверке **не запускались**.

## Exact source

Upstream:

- repository: `hufrea/byedpi`;
- tag: `v0.17.3`;
- commit: `7efde1b1296eaaa187b70e951894dde17527489c`;
- tree: `8d13f6f5939190cd27ee68696247073a00b3e7dc`.

Текущий NetPilot patchset:

1. `0001-loopback-only-listener.patch`;
2. `0002-kavl-iterator-no-before-array-sentinel.patch`;
3. `0003-pid-file-fail-safe-write.patch`;
4. `0004-kavl-empty-erase-guard.patch`;
5. `0005-windows-socket-handle-narrowing-guard.patch`;
6. `0006-windows-wait-result-range.patch`;
7. `0007-windows-aux-socket-probes.patch`.

Актуальная identity `0007`:

- Git blob SHA-1: `e7b590a7d0594be0629bac221cb120c165dab8cd`;
- SHA-256: `778f47d3ecf2d0f6c8ce0152766c0eb51e2d6e460923227bed49844ba18c0d25`.

## Workflow

Workflow: `Native Candidate ByeDPI Compile Smoke`.

- run: `40809161345`;
- job: `123235119893`;
- head SHA: `27a64c64868fc7d4d09b0f165f48df9431485f23`;
- conclusion: `success`.

Artifact:

- ID: `12869671269`;
- name: `byedpi-r28-compile-smoke-evidence`;
- ZIP SHA-256: `f5ab931aff6867048a73ca81b4e50c4a090c4e366665c3f7ba1738ad637bddc7`.

Текстовая фиксация сохранена в:

`security/native-admission/evidence/byedpi-r28/compile-smoke-0001-0007.txt`.

## Targeted Windows boundary gate

Проверка сохранила строгие targeted gates:

- `conev.c` компилируется с `-Werror=sign-compare`;
- `desync.c` компилируется с `-Werror=type-limits`;
- `main.c` проверяется структурными assertions для двух auxiliary socket probes:
  - `get_default_ttl()`;
  - `ipv6_support()`;
- после assertions `main.c` также компилируется Windows toolchain без candidate execution.

Первый вариант validator имел ложное срабатывание: он видел разрешённый Unix `int fd` внутри `#else` и ошибочно объявлял его Windows narrowing. Это исправлено без ослабления требований к Windows branch: обязательные `#ifdef _WIN32 → SOCKET → INVALID_SOCKET` assertions сохранены.

Глобальный `-Werror=conversion` на весь upstream `main.c` намеренно не используется как proof patch `0007`, поскольку он поднимает множество pre-existing numeric conversion warnings в несвязанных CLI/config путях. Эти warnings остаются отдельным review debt.

## Linux x86-64

Compiler:

`gcc (Ubuntu 13.3.0-6ubuntu2~24.04.1) 13.3.0`

Output format:

`ELF 64-bit LSB PIE x86-64`

Обе последовательные clean build дали один SHA-256:

`a54f71634020ef43421fa6b4c22800409226d29450ad143df949940c2290f33a`

Результат:

**REPRODUCIBLE IN THIS CONTROLLED RUN**.

## Windows x64

Compiler:

`x86_64-w64-mingw32-gcc (GCC) 13-win32`

Output format:

`PE32+ executable (console) x86-64`

Для linker использован deterministic flag:

`--no-insert-timestamp`

Обе последовательные clean build дали один SHA-256:

`866e99c3c7690326cc6e365fe02980f06ae2b394c045555674fb95db52b0dbdd`

Результат:

**REPRODUCIBLE IN THIS CONTROLLED RUN**.

## Что этот PASS доказывает

Только следующее:

1. exact source identity совпадает;
2. exact canonical bytes patches `0001–0007` проходят identity verification;
3. patches последовательно применяются к exact source;
4. targeted Windows handle/wait/source-boundary gates проходят;
5. Linux x86-64 output воспроизводим между двумя clean build в этом runner;
6. Windows x64 output воспроизводим между двумя clean build в этом runner;
7. candidate не запускался;
8. listener не открывался;
9. routing не менялся.

## Что этот PASS НЕ доказывает

Не доказаны:

- runtime loopback-only enforcement;
- отсутствие wildcard/LAN listener при реальном запуске;
- runtime socket/UDP lifecycle;
- устойчивость под repeated connection churn;
- bounded connection/UDP state policy;
- Android `VpnService`/TUN boundary;
- Windows transparent routing boundary;
- пригодность к production;
- отсутствие всех memory-safety/logic defects.

## Admission effect

После этого PASS статус остаётся:

- `admission_status: REVIEW`;
- `release_gate: BLOCK`;
- `may_execute: false`;
- `may_integrate: false`.

Этот результат закрывает только **full patchset compile-only gate**.

Следующий этап может быть только рассмотрением candidate на scoped loopback-only `LAB_APPROVED` по существующему draft plan, после завершения оставшихся pre-lab evidence и отдельного HIGH_ASSURANCE human review.
