# R28.4 — upstream issue review ByeDPI v0.17.3

Дата проверки: 29.10.2026.

Статус: **REVIEW / NO NEW MEMORY-SAFETY RELEASE BLOCKER CONFIRMED FOR EXACT v0.17.3 / RUNTIME STILL BLOCKED**.

## Цель

Проверить актуальные upstream issues ByeDPI перед любым переходом к runtime-lab и отделить:

- подтверждённые дефекты exact `v0.17.3`;
- исторические crash reports;
- функциональные ограничения;
- запросы новых возможностей.

Exact кандидат NetPilot остаётся:

- repository: `hufrea/byedpi`;
- tag: `v0.17.3`;
- commit: `7efde1b1296eaaa187b70e951894dde17527489c`;
- tree: `8d13f6f5939190cd27ee68696247073a00b3e7dc`;
- NetPilot patchset: `0001`–`0006`;
- `admission_status: REVIEW`;
- `release_gate: BLOCK`;
- `may_execute: false`;
- `may_integrate: false`.

## Исторический issue #213 — assertion/segfault после изменения event handling

Issue:

`hufrea/byedpi#213` — `Commit 6dca571... causes segfault`.

Симптомы в 2024 году включали `Bad file descriptor` и assertion в `add_event`.

Upstream discussion связала наблюдаемое поведение с некорректной инкрементальной сборкой: Makefile не гарантировал recompilation object files после изменения headers.

PR `#229` (`Fix building .o files in Makefile`) был merged commit:

`9d9c370d666f99434f44627c233426e4ba28de32`.

Проверена история Git:

- merge commit PR #229 является предком exact `v0.17.3`;
- `v0.17.3` находится на `85` коммитов впереди него;
- `behind_by: 0`.

Вывод для admission:

**HISTORICAL / ADDRESSED BEFORE v0.17.3**.

Это не является текущим release blocker exact `v0.17.3`, но показывает, почему controlled build NetPilot всегда делает clean/reproducible build и не доверяет stale object files.

## Исторический issue #135 — периодический Linux segfault

Issue `#135` описывал периодические segfault при использовании ByeDPI для YouTube.

Проблема:

- в issue не зафиксирована точная версия бинарника;
- maintainer запросил версию/OS/debug log;
- дополнительных диагностических данных в issue нет;
- issue позднее закрыт как `not_planned`, без технически подтверждённой root cause.

Поэтому NetPilot **не имеет оснований утверждать**, что:

- дефект присутствует в `v0.17.3`; или
- дефект гарантированно исправлен в `v0.17.3`.

Классификация:

**HISTORICAL UNRESOLVED CRASH SIGNAL / NOT PROVEN AGAINST EXACT v0.17.3**.

Admission effect:

- не создаёт самостоятельный exact-release BLOCK;
- усиливает обязательность ASan/UBSan targeted tests;
- усиливает обязательность repeated connection lifecycle/resource stress;
- будущий loopback runtime-lab должен включать controlled stop/crash/timeout cleanup и repeated open/close cycles.

## Issue #416 — HTTP CONNECT port parsing у Apple clients

Свежий issue `#416`, созданный 28.10.2026, сообщает, что в HTTP-proxy режиме ByeDPI может брать target port из `Host` header и использовать `80`, если port отсутствует там, хотя Apple clients указывают `host:port` в CONNECT request-target.

Сообщение утверждает воспроизводимость на `v0.17.3` и current main.

Классификация:

**FUNCTIONAL COMPATIBILITY LIMITATION / NOT MEMORY-SAFETY**.

Для текущего NetPilot scope:

- HTTP-proxy mode не должен объявляться поддержанным или production-ready;
- первый ByeDPI lab остаётся SOCKS/loopback-only;
- Android VPN/TUN path всё равно проходит отдельный admission;
- если HTTP CONNECT когда-либо понадобится, fix/backport должен пройти отдельный source review и regression test.

Этот issue не является основанием включать current upstream `master` вместо exact `v0.17.3`.

## Open crash/segfault search

На дату проверки отдельный поиск открытых issues по терминам `segfault`, `crash`, `Bad file descriptor`, `assert` не выявил нового конкретного open issue, явно описывающего memory-safety crash exact `v0.17.3`.

Это **не доказательство отсутствия дефектов**. GitHub issue search используется только как один источник advisory evidence и не заменяет sanitizer/fuzz/runtime testing.

## Security conclusion

После этой проверки:

- новый подтверждённый memory-safety release blocker exact `v0.17.3` не найден;
- historical crash evidence не игнорируется;
- HTTP CONNECT Apple compatibility issue зафиксирован как текущая функциональная проблема;
- full patchset compile/static/sanitizer evidence по-прежнему обязателен;
- runtime остаётся запрещён до отдельного `LAB_APPROVED`.

## Следующие gates

1. получить PASS full patchset `0001–0006` Linux/Windows compile-only;
2. получить PASS targeted Windows warning gates;
3. получить PASS PID-file/static и KAVL sanitizer evidence exact required subsets;
4. сохранить fail-closed loopback lab plan в `DRAFT_NOT_APPROVED`;
5. перед любым lab execution повторно проверить upstream issues/advisories и exact binary hashes;
6. отдельный HIGH_ASSURANCE human approval для scoped loopback-only lab.
