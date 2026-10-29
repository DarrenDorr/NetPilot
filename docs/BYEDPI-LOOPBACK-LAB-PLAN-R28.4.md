# R28.4 — draft loopback lab-plan ByeDPI v0.17.3

Дата: 29.10.2026.

Статус: **DRAFT / EXECUTION NOT APPROVED / PRODUCTION EFFECT NONE**.

## Цель

Подготовить безопасный план будущего изолированного runtime-test exact ByeDPI `v0.17.3` после NetPilot hardening patches `0001`–`0006`.

Машиночитаемый план:

`security/native-admission/lab-plans/byedpi-loopback-r28.4.yaml`

Этот документ не разрешает запуск `ciadpi`/`ciadpi.exe`.

## Проверяемый candidate

Upstream:

- repository: `hufrea/byedpi`;
- tag: `v0.17.3`;
- commit: `7efde1b1296eaaa187b70e951894dde17527489c`;
- tree: `8d13f6f5939190cd27ee68696247073a00b3e7dc`.

NetPilot patchset:

1. loopback-only listener;
2. KAVL iterator без one-before-array sentinel;
3. fail-safe PID-file write/lock handling;
4. empty-tree KAVL erase guard;
5. Win64 SOCKET narrowing guard;
6. корректная классификация `WaitForMultipleObjects` result range.

## Listener policy

Будущий lab обязан доказать, что candidate:

- по умолчанию слушает только `127.0.0.1`;
- допускает только IPv4 loopback `127.0.0.0/8`;
- допускает `::1`, если IPv6 loopback поддержан выбранной сборкой;
- не слушает `0.0.0.0`;
- не слушает IPv6 any-address;
- не слушает LAN/non-loopback интерфейс;
- отклоняет попытку non-loopback bind;
- после stop/crash/timeout не оставляет residual listener.

## Что требуется до первого запуска

До перевода плана из draft должны быть зелёными:

- exact source/patch identity;
- full patchset Linux compile;
- full patchset Windows x64 compile;
- targeted Windows warning gates;
- PID-file static gate;
- KAVL sanitizer gate;
- дополнительные static/sanitizer gates exact patchset.

Также обязательны:

- disposable lab;
- clean snapshot;
- отсутствие пользовательских данных, cookies, tokens и private keys;
- exact runtime binary SHA-256;
- отдельный HIGH_ASSURANCE human approval.

## Стадии

### BYE-0 — identity/environment verification

Запуск запрещён. Фиксируются exact source/patch/build identities и baseline process/listener inventory.

### BYE-1 — loopback listener regression

Сейчас `executable_run_allowed: false`.

После отдельного approval планируется проверить default/explicit loopback bind и обязательное отклонение wildcard/LAN bind.

### BYE-2 — supervised lifecycle

Разрешается только после PASS BYE-1 и нового approval.

Планируются controlled stop, timeout, forced termination, process-tree cleanup, PID-file cleanup и residual-listener check.

### BYE-3 — bounded resource lifecycle

Разрешается только после PASS предыдущих стадий и нового approval.

Только synthetic loopback workloads:

- TCP connection cycles;
- UDP association cycles;
- bounded burst;
- repeated open/close.

Numeric connection/UDP budgets нельзя выбирать «на глаз» — они должны появиться из evidence.

### BYE-4 — platform routing integration

Полностью вне текущего плана.

Этот draft **не разрешает**:

- Windows system-wide transparent routing;
- Android `VpnService`/TUN;
- routing traffic других приложений через candidate;
- production integration.

Для этих компонентов нужен отдельный admission.

## Evidence privacy

Разрешается сохранять только operational metadata:

- platform;
- exact binary SHA-256;
- source/patch identities;
- profile ID;
- наблюдавшиеся bound addresses;
- residual process/listener counts;
- exit/cleanup results;
- peak TCP/UDP state counters.

Запрещается сохранять:

- proxied packet/application payloads;
- cookies, credentials, tokens, private keys;
- browsing history;
- лишнюю историю URL/IP.

## Admission effect

Даже будущий успешный lab-run:

- не переводит candidate автоматически в `ADMIT`;
- не меняет `release_gate: BLOCK` автоматически;
- не разрешает production activation;
- требует отдельного security admission decision.

## Следующий безопасный шаг

Сначала дождаться зелёного полного R28.4 patchset `0001`–`0006` compile/static/sanitizer evidence. Только после этого candidate может быть рассмотрен человеком на отдельный `LAB_APPROVED` для loopback-only boundary.
