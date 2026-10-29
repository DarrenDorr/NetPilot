# R28.4 — hardening вспомогательных Windows SOCKET probes ByeDPI

Дата: 29.10.2026.

Статус: **DESIGN FIXED / COMPILE EVIDENCE PENDING / NOT ADMITTED / RELEASE GATE BLOCK**.

## Что обнаружено

После исправления основной Win64 границы `SOCKET -> int` в patch `0005` выполнен повторный поиск всех вызовов `socket()`/`accept()` exact ByeDPI `v0.17.3`.

Основной event-pool ingress уже покрывался `0005`, однако в `main.c` остались ещё два вспомогательных пути:

- `get_default_ttl()`;
- `ipv6_support()`.

Upstream хранит результат `socket()` в локальной переменной `int`.

На Windows `socket()` возвращает `SOCKET`, который является pointer-sized unsigned handle. Поэтому даже если такой socket не попадает в event-pool, раннее присваивание в `int` остаётся некорректной Win64 границей представления.

Практический риск здесь уже уже, чем у event-pool:

- неверный handle может попасть в `getsockopt()`;
- `close/closesocket()` может получить уже усечённое значение;
- исходный socket handle потенциально может остаться незакрытым;
- результат probe может стать ложным.

Это не доказанная эксплуатационная уязвимость, но оставлять известное narrowing в hardened fork нельзя.

## Решение

Добавлен отдельный минимальный patch:

`security/native-admission/patches/byedpi-r28/0007-windows-aux-socket-probes.patch`

Immutable identity:

- Git blob SHA-1: `e1e7cf53a87e9f51224147d7b1d4faf39dcf8ef6`;
- SHA-256: `ddfe7a3b6280c55dc89afd99ea89badea2f30348031f4f93a6ca6debcf939897`.

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

## Compile gate

Full-patchset ByeDPI теперь определяется как `0001–0007`.

Workflow:

`.github/workflows/native-candidate-byedpi-compile-smoke.yml`

дополнительно требует:

- exact Git blob/SHA-256 `0007`;
- успешное применение `0007` к exact `v0.17.3`;
- наличие Windows `SOCKET` declarations в обоих probe после patch;
- targeted compile `main.c` с усиленными conversion/sign warnings;
- две воспроизводимые Linux build;
- две воспроизводимые Windows x64 build;
- candidate binary не запускается.

Evidence schema для нового patchset повышена до `10`, а scope изменён на:

`byedpi-v0.17.3-netpilot-hardening-0001-0007-compile-only`.

## Admission effect

Patch `0007` **не разрешает runtime**.

До зелёного full-patchset gate сохраняются:

- `admission_status: REVIEW`;
- `release_gate: BLOCK`;
- `may_execute: false`;
- `may_integrate: false`.

Даже после compile PASS candidate сможет перейти только к рассмотрению `LAB_APPROVED` по отдельному loopback-only plan и только после HIGH_ASSURANCE human review.
