# R28.4 — pre-lab readiness ByeDPI v0.17.3

Дата: 29.10.2026.

Статус: **PRE_LAB_REVIEW / DRAFT_NOT_APPROVED / EXECUTION FORBIDDEN / RELEASE GATE BLOCK**.

## Назначение

Этот документ отделяет уже закрытые compile/static/sanitizer gates от того, что ещё требуется перед первым изолированным loopback-only runtime test ByeDPI.

Compile-only PASS **не является** `LAB_APPROVED` и не является `ADMIT`.

## Exact candidate

Upstream:

- `hufrea/byedpi`;
- tag `v0.17.3`;
- commit `7efde1b1296eaaa187b70e951894dde17527489c`;
- tree `8d13f6f5939190cd27ee68696247073a00b3e7dc`.

NetPilot patchset: `0001–0007`.

Текущая identity patch `0007`:

- Git blob SHA-1: `e7b590a7d0594be0629bac221cb120c165dab8cd`;
- SHA-256: `778f47d3ecf2d0f6c8ce0152766c0eb51e2d6e460923227bed49844ba18c0d25`.

## Уже закрытые gates

### Full patchset compile-only

PASS:

- workflow run `40809161345`;
- job `123235119893`;
- artifact `12869671269`;
- artifact ZIP SHA-256 `f5ab931aff6867048a73ca81b4e50c4a090c4e366665c3f7ba1738ad637bddc7`.

Linux x86-64 controlled build:

`a54f71634020ef43421fa6b4c22800409226d29450ad143df949940c2290f33a`

Windows x64 controlled build:

`866e99c3c7690326cc6e365fe02980f06ae2b394c045555674fb95db52b0dbdd`

Оба output воспроизвелись дважды в одном controlled run.

CI binaries после hashing были удалены; они не считаются готовыми runtime artifacts.

### Targeted Windows boundaries

PASS:

- `conev.c` sign-compare gate;
- `desync.c` type-limits gate;
- `get_default_ttl()` Windows `SOCKET` source boundary;
- `ipv6_support()` Windows `SOCKET` source boundary;
- обычная Windows compilation `main.c` после source assertions.

### Targeted KAVL/PID evidence

PASS в ограниченном scope:

- KAVL ASan/UBSan regression;
- KAVL empty-erase sanitizer regression;
- PID-file static gate.

Эти PASS не расширяются автоматически на весь runtime.

## Что остаётся до `LAB_APPROVED`

### 1. Human approval exact scope

Нужно отдельное HIGH_ASSURANCE решение, в котором явно указаны:

- candidate identity;
- platform;
- exact rebuilt binary SHA-256;
- разрешённая стадия lab-plan;
- disposable environment;
- rollback procedure.

Обычное продолжение разработки или merge approval не считается таким разрешением.

### 2. Exact lab binary rebuild

Перед execution binary должен быть заново собран из exact source/patch/toolchain recipe.

Его SHA-256 должен быть явно сверён с утверждённой candidate identity.

Compile-only reference hashes сохранены только для сравнения; сами CI binaries не retained.

### 3. Disposable environment

Первый runtime допускается только в отдельной disposable lab-среде с clean snapshot.

Запрещены:

- рабочие пользовательские профили браузеров;
- cookies/tokens/private keys;
- рабочие документы;
- реальные учётные данные;
- packet capture payloads.

### 4. Loopback-only listener evidence

Нужно доказать runtime, что candidate:

- по умолчанию слушает только loopback;
- не открывает `0.0.0.0`;
- не открывает IPv6 any-address;
- не открывает LAN/non-loopback interface;
- отклоняет non-loopback bind request;
- не оставляет listener после stop/timeout/crash.

### 5. Supervised lifecycle

Отдельно проверить:

- controlled start;
- controlled stop;
- hard timeout;
- forced termination;
- process-tree cleanup;
- PID-file cleanup на применимой платформе;
- отсутствие residual process/listener.

Service installation и autostart в этом lab запрещены.

### 6. Resource lifecycle

До production admission потребуются evidence-based ненулевые budgets для:

- concurrent TCP connections;
- UDP associations;
- pending connections.

Сначала только synthetic loopback workloads:

- repeated TCP open/close;
- UDP association cycles;
- bounded bursts;
- repeated lifecycle cycles.

Числа нельзя выбирать «на глаз».

### 7. Unrelated upstream warning debt

Глобальный `-Wconversion` для всего upstream `main.c` выявляет ряд исторических numeric conversions в CLI/config code, не связанных с patches `0005/0007`.

Они не были скрыты и не объявлены автоматически уязвимостями.

Перед `ADMIT` требуется отдельная классификация этих warnings: real defect / robustness issue / benign conversion / unsupported raw CLI surface.

## Что остаётся вне первого ByeDPI lab

Первый scoped lab **не включает**:

- Windows transparent/system-wide routing;
- WFP/TUN/tun2socks integration;
- Android `VpnService`;
- Android TUN-to-SOCKS;
- Discord/YouTube public-service compatibility;
- HTTP proxy mode;
- system service/autostart;
- external SOCKS exposure.

Эти направления требуют отдельных component admissions.

## Upstream limitations already recorded

- historical issue `#213`: addressed before `v0.17.3`;
- historical issue `#135`: unresolved crash signal without proof against exact `v0.17.3`;
- issue `#416`: HTTP CONNECT Apple-client compatibility limitation; HTTP proxy mode не admitted.

## Current decision

ByeDPI теперь прошёл важный compile-only milestone и может находиться в состоянии:

**PRE_LAB_REVIEW**.

Но текущая политика остаётся:

- `admission_status: REVIEW`;
- `release_gate: BLOCK`;
- `may_execute: false`;
- `may_integrate: false`;
- lab plan `DRAFT_NOT_APPROVED`;
- `execution_allowed: false`.

Следующее повышение возможно только отдельным reviewed change после human approval exact loopback-only lab scope.
