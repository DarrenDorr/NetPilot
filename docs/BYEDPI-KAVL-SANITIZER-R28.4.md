# R28.4 — KAVL hardening ByeDPI v0.17.3

Дата: 29.10.2026.

Статус: **PASS FOR ISOLATED KAVL ASAN/UBSAN REGRESSION ONLY / NOT ADMITTED / RELEASE GATE BLOCK**.

## Причина проверки

Compile-smoke ByeDPI v0.17.3 показал GCC `-Warray-bounds` вокруг KAVL iterator, который использовал указатель `itr->stack - 1` как sentinel.

Net.Pilot не принимает one-before-array pointer как нормальную внутреннюю модель даже тогда, когда код старается не dereference такой pointer.

## Patch 0002 — iterator без one-before-array sentinel

`security/native-admission/patches/byedpi-r28/0002-kavl-iterator-no-before-array-sentinel.patch`

- Git blob SHA-1: `6f51d66597847ee49e373922e4905c80b8ee2f47`;
- SHA-256: `6701c48ef00f81ca7fa95f98a1e6406f657fbdfc028371f568c7918106cd5fb7`.

Изменение использует `top == NULL` как empty/exhausted state и больше не формирует `stack - 1`.

## Дополнительная находка — empty-tree erase UB

Первый ASan/UBSan regression после iterator hardening обнаружил отдельный upstream-дефект: вызов `kavl_erase_first()` на уже пустом дереве мог пройти в `__KAVL_ERASE` и сформировать обращение к `path[-1]`.

Это отдельный defect generic KAVL API, а не следствие patch `0002`.

В текущем ByeDPI `mem_destroy()` защищён `while (hdr->root)`, поэтому обычный destroy path не обязан вызывать erase-first на пустом дереве. Тем не менее fail-safe fork не должен сохранять заведомо UB generic path.

## Patch 0004 — empty-tree erase guard

`security/native-admission/patches/byedpi-r28/0004-kavl-empty-erase-guard.patch`

- Git blob SHA-1: `9b196b289efd9a2ea1118c5504f4a811aa0becd2`;
- SHA-256: `fdc13219be9cb9d654ce7995df825decb6ee0f05e63d72481560977bbf3b3c4a`.

Изменение минимально:

- после инициализации fake root и `cnt_`;
- если `*root_ == 0`, функция сразу возвращает `0`.

Балансировка, поиск и удаление непустого дерева не меняются.

## Gate

Workflow: `Native Candidate ByeDPI Static and Sanitizer`.

- run: `40804649207`;
- job: `123220567167`;
- head: `782994db855910b7d2d6a699cc22946bfea1cd0c`;
- conclusion: `success`.

Artifact:

- ID: `12868200056`;
- name: `byedpi-r28-kavl-sanitizer-evidence`;
- ZIP SHA-256: `7815708ee7cbb57e06b36679c89a4686f0dbf74a01029c9f8074139436bd4134`.

Evidence:

`security/native-admission/evidence/byedpi-r28/kavl-sanitizer.txt`.

## Что проверялось

### Реальный ByeDPI translation unit

После patch `0002` + `0004` реальный `mpool.c` компилируется с `-Werror=array-bounds`.

### ASan/UBSan harness

Для каждого из 40 циклов создаётся AVL-дерево из 1024 узлов и проверяются:

- empty first/next/find;
- полный in-order обход;
- exact find;
- lower-bound find;
- below-minimum / above-maximum;
- повторное создание и освобождение дерева;
- финальная проверка empty erase path.

Harness завершился `kavl_iterator_harness: PASS`.

Отдельный `strace -e trace=network` пуст: сетевые syscall отсутствуют.

## Что PASS означает

В покрытом сценарии:

- one-before-array iterator sentinel удалён;
- real `mpool.c` больше не даёт прежний array-bounds warning при усиленной проверке;
- empty-tree erase guard закрывает найденный UBSan path;
- ASan/UBSan не выявили ошибку в тестовом iterator/tree lifecycle;
- native candidate не запускался.

## Что PASS НЕ означает

Этот gate не доказывает полную безопасность всей KAVL-библиотеки или всего ByeDPI. Отдельно продолжаются:

- exact erase/erase-first baseline;
- full patchset Linux/Windows compile;
- Win64 socket handle review;
- resource/connection lifecycle review;
- будущий loopback runtime только после `LAB_APPROVED`.

## Admission effect

Статус ByeDPI не повышается:

- `admission_status: REVIEW`;
- `release_gate: BLOCK`;
- `may_execute: false`;
- `may_integrate: false`.
