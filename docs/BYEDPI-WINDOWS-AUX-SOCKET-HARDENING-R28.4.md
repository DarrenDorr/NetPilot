# R28.4 — hardening вспомогательных Windows SOCKET probes ByeDPI

Дата: 29.10.2026.

Статус: **DESIGN FIXED / REBASED ON HARDENED MAIN / COMPILE EVIDENCE PENDING / NOT ADMITTED / RELEASE GATE BLOCK**.

## Что обнаружено

После исправления основной Win64 границы `SOCKET -> int` в patch `0005` выполнен повторный поиск всех вызовов `socket()`/`accept()` exact ByeDPI `v0.17.3`.

Основной event-pool ingress уже покрывался `0005`, однако в `main.c` остались ещё два вспомогательных пути:

- `get_default_ttl()`;
- `ipv6_support()`.

Upstream хранит результат `socket()` в локальной переменной `int`.

На Windows `socket()` возвращает `SOCKET`, который является pointer-sized unsigned handle. Поэтому даже если такой socket не попадает в event-pool, раннее присваивание в `int` остаётся некорректной Win64 границей представления.

Практический риск здесь уже, чем у event-pool:

- неверный handle может попасть в `getsockopt()`;
- `close/closesocket()` может получить уже усечённое значение;
- исходный socket handle потенциально может остаться незакрытым;
- результат probe может стать ложным.

Это не доказанная эксплуатационная уязвимость, но оставлять известное narrowing в hardened fork нельзя.

## Решение

Добавлен отдельный минимальный patch:

`security/native-admission/patches/byedpi-r28/0007-windows-aux-socket-probes.patch`

Текущая immutable identity после rebasing на состояние `main.c`, сформированное patches `0001–0006`:

- Git blob SHA-1: `e7b590a7d0594be0629bac221cb120c165dab8cd`;
- SHA-256: `778f47d3ecf2d0f6c8ce0152766c0eb51e2d6e460923227bed49844ba18c0d25`.

Предыдущие identities `e1e7cf53…` и связанные с ним SHA считаются **устаревшими** и не должны использоваться для approval/evidence.

## Новая семантика

### Windows

Оба probe используют:

`SOCKET fd = socket(...)`

Ошибка проверяется через:

`fd == INVALID_SOCKET`

После этого `getsockopt()` и `closesocket()` получают исходный pointer-sized handle без предварительного narrowing.

### Linux/Unix

Поведение не меняется:

- локальный descriptor остаётся `int`;
- ошибка проверяется через `fd < 0`.

## Почему это отдельный patch

`0005` закрывает более важную границу, где Windows socket handles попадают в существующий int-based event-pool ABI.

`0007` относится только к двум вспомогательным probe, которые не входят в event-pool.

Разделение позволяет:

- отдельно отслеживать причину и identity каждого изменения;
- не раздувать уже проверяемый patch `0005`;
- при upstream update понимать, какая именно граница была исправлена upstream и какой patch можно удалить.

## Почему patch пришлось rebasing

Первоначальная версия `0007` была подготовлена против чистого upstream `main.c`.

Full-patchset gate применяет patches последовательно, и к моменту `0007` `main.c` уже изменён `0001`/`0003`. Поэтому первая цепочка корректно остановилась на `git apply --check`.

Patch был перебазирован на exact hardened state после `0001–0006`. Логика Windows/Unix веток не изменилась; изменилась только applicability context и, соответственно, immutable identity.

## Compile gate

Full-patchset ByeDPI определяется как `0001–0007`.

Workflow:

`.github/workflows/native-candidate-byedpi-compile-smoke.yml`

требует:

- exact Git blob/SHA-256 `0007`;
- успешное последовательное применение `0001–0007`;
- точные source-boundary assertions для `get_default_ttl()` и `ipv6_support()`;
- targeted `conev.c` gate с `-Werror=sign-compare`;
- targeted `desync.c` gate с `-Werror=type-limits`;
- обычную Windows compile-проверку `main.c` после source assertions;
- две воспроизводимые Linux build;
- две воспроизводимые Windows x64 build;
- candidate binary не запускается.

Глобальный `-Werror=conversion` для всего upstream `main.c` больше не используется как доказательство patch `0007`, потому что он ловит множество исторических numeric conversions в несвязанных CLI/config путях. Эти warnings не скрыты: они остаются отдельным review debt.

Evidence schema текущего gate: `11`.

Scope:

`byedpi-v0.17.3-netpilot-hardening-0001-0007-compile-only`.

## Admission effect

Patch `0007` **не разрешает runtime**.

До зелёного full-patchset gate сохраняются:

- `admission_status: REVIEW`;
- `release_gate: BLOCK`;
- `may_execute: false`;
- `may_integrate: false`.

Даже после compile PASS candidate сможет перейти только к рассмотрению `LAB_APPROVED` по отдельному loopback-only plan и только после HIGH_ASSURANCE human review.
