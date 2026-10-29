# R28.4 — hardening Windows socket/event boundary ByeDPI

Дата: 29.10.2026.

Статус: **DESIGN FIXED / COMPILE EVIDENCE PENDING / NOT ADMITTED / RELEASE GATE BLOCK**.

## Контекст

При разборе предупреждений Windows cross-compile для exact ByeDPI `v0.17.3` обнаружены два независимых участка, которые нельзя было закрыть простым приведением типов ради тишины компилятора.

Проверяемая upstream база:

- repository: `hufrea/byedpi`;
- tag: `v0.17.3`;
- commit: `7efde1b1296eaaa187b70e951894dde17527489c`;
- tree: `8d13f6f5939190cd27ee68696247073a00b3e7dc`.

## 1. Win64 SOCKET -> int boundary

В upstream `v0.17.3` event pool хранит `struct eval.fd` как `int`, а `add_event()`/`add_pair()` также принимают `int`.

На Windows функция `socket()` и `accept()` возвращает `SOCKET`, который является pointer-sized unsigned типом. Исторический код помещал результат в `int` до какой-либо проверки представимости. На Win64 это создаёт потенциальное сужение значения handle.

Исторический warning в `conev.c`:

`comparison of integer expressions of different signedness: int and SOCKET`

оказался не просто косметическим warning, а указателем на эту границу представления.

### Решение

Добавлен patch:

`security/native-admission/patches/byedpi-r28/0005-windows-socket-handle-narrowing-guard.patch`

Identity:

- Git blob SHA-1: `fb8a7f57dc577e9ee8664adb41d264a64dc81a0d`;
- SHA-256: `3b8034c4cecdcd7b1551fbdf7b99f0fbdf82b00c0a7613046a06aa8b5182d050`.

Семантика на Windows:

1. `INVALID_SOCKET` отклоняется;
2. если `SOCKET > INT_MAX`, handle закрывается до сужения, устанавливается socket error и операция завершается fail-closed;
3. только представимое значение приводится к `int` и может попасть в существующий event pool;
4. сравнение с `WSAPoll` descriptor выполняется как `SOCKET` только после гарантии ingress-представимости.

Это намеренно минимальный patch: upstream int-based event-pool ABI пока не переписывается целиком.

## 2. WaitForMultipleObjects result range

Исторический Windows build давал warning в `desync.c`:

`comparison of unsigned expression in >= 0 is always true`.

Код проверял:

`ret >= WAIT_OBJECT_0 && ret < WAIT_OBJECT_0 + MAX_TF`

`ret` имеет тип `DWORD`, а `WAIT_OBJECT_0` равен нулю, поэтому первая часть не добавляет защиты.

### Решение

Добавлен patch:

`security/native-admission/patches/byedpi-r28/0006-windows-wait-result-range.patch`

Identity:

- Git blob SHA-1: `0815be26534c291848a8b16bdedb07a6ab1e485f`;
- SHA-256: `136b9fdf34b68a55f0f1a6ebf16e8b61c9b88e6d15eeb923594bdd7bf0fa2a3e`.

После patch индексируются только значения ниже `WAIT_OBJECT_0 + MAX_TF`, то есть успешные object-result indices `0..MAX_TF-1`. `WAIT_TIMEOUT`, `WAIT_FAILED` и abandoned-result диапазон не используются как индекс.

## Compile gate

`.github/workflows/native-candidate-byedpi-compile-smoke.yml` переведён на полный patchset `0001–0006`.

Кроме двух воспроизводимых полных сборок Linux x86-64 и Windows x64, добавлены targeted Windows gates:

- `conev.c` компилируется с `-Werror=sign-compare`;
- `desync.c` компилируется с `-Werror=type-limits`.

Это должно подтвердить именно устранение обнаруженных границ, а не просто общий факт компиляции.

## Что пока НЕ доказано

До успешного CI-run не считаются доказанными:

- применимость полной цепочки `0001–0006`;
- отсутствие новых compile regressions;
- reproducible output hashes полного hardened build;
- runtime поведение Windows socket ingress;
- runtime cleanup и listener isolation.

## Admission effect

Статус ByeDPI не повышается:

- `admission_status: REVIEW`;
- `release_gate: BLOCK`;
- `may_execute: false`;
- `may_integrate: false`.

Следующий обязательный шаг — успешный full patchset compile/static gate. Только после этого можно переходить к изолированному loopback runtime lab по R28.4 lab-execution policy.
