# R28.3 — controlled compile-smoke ByeDPI v0.17.3

Дата: 28.10.2026.

Статус: **PASS FOR COMPILE-ONLY EVIDENCE / NOT ADMITTED / RELEASE GATE BLOCK**.

## Цель

Проверить, что точный upstream ByeDPI `v0.17.3` после минимального NetPilot hardening-патча:

- сохраняет фиксированное происхождение source/patch;
- применяет loopback-only policy к listener;
- компилируется для Linux x86-64;
- cross-компилируется для Windows x64;
- не требует запуска кандидата для получения compile evidence.

Эта проверка **не запускает `ciadpi`/`ciadpi.exe`**, не открывает SOCKS listener и не меняет маршрутизацию.

## Точная база

Upstream:

- repository: `hufrea/byedpi`;
- release: `v0.17.3`;
- commit: `7efde1b1296eaaa187b70e951894dde17527489c`;
- tree: `8d13f6f5939190cd27ee68696247073a00b3e7dc`.

NetPilot patch:

- `security/native-admission/patches/byedpi-r28/0001-loopback-only-listener.patch`;
- Git blob: `fa9ba3349792d66f40d925bc612310e8d18aae8a`;
- SHA-256 exact canonical bytes: `3e9a744830082d224592d730a816c4afcf9df15a2a6ab93870409ccf5c94c068`.

Patch делает default listener `127.0.0.1` и отклоняет пользовательский bind за пределами IPv4 `127.0.0.0/8` и IPv6 `::1`.

## Workflow

Workflow: `Native Candidate ByeDPI Compile Smoke`.

- run: `40730956091`;
- job: `122966803718`;
- head SHA: `9613ab48b10e849550f8b5a69113b88c8e890871`;
- conclusion: `success`.

Identity gate подтвердил exact commit/tree/blob/SHA. Patch применился cleanly ко всем четырём hunk'ам.

## Compile results

### Linux x86-64

- compiler: `gcc (Ubuntu 13.3.0-6ubuntu2~24.04.1) 13.3.0`;
- output: `ciadpi`;
- format: ELF 64-bit PIE x86-64;
- SHA-256: `6787ff3f5ed30f947b61860edbd7439cd6fb9d8eb21de9eb67d4d92ddd573e8a`.

### Windows x64

- compiler: `x86_64-w64-mingw32-gcc (GCC) 13-win32`;
- output: `ciadpi.exe`;
- format: PE32+ console x86-64;
- SHA-256: `1be8ab31e7089e8794e745d2ec1ee009517d7a1241cbf8e5289b6c45a26cfc58`.

Оба binaries после инвентаризации удалены с runner и не сохранены в репозитории.

## Artifact

- artifact ID: `12834388188`;
- name: `byedpi-r28-compile-smoke-evidence`;
- ZIP SHA-256: `eef60dbe551ee908e0f9e4de0a18519d63a032e0948e07e46eebf0a9725771d4`.

Текстовая фиксация сохранена в:

`security/native-admission/evidence/byedpi-r28/compile-smoke.txt`.

## Warnings, которые не закрыты этим PASS

Compile завершился успешно, но upstream compiler warnings не считаются автоматически безопасными. В частности зафиксированы:

- Linux: игнорирование результата `write()` в `init_pid_file`;
- Linux/Windows: GCC `-Warray-bounds` вокруг `kavl_itr_first_my` / `dump_cache`;
- Windows: signed/unsigned comparison в event code;
- Windows: условие `ret >= WAIT_OBJECT_0`, которое компилятор считает всегда истинным для unsigned типа.

Эти warnings требуют отдельного source review/static analyzer/sanitizer gate. Текущий compile-only PASS **не закрывает** их и не разрешает runtime.

## Что доказано

Только следующее:

1. exact source identity подтверждена;
2. exact reviewed loopback patch применим;
3. patched source компилируется в Linux x86-64 и Windows x64;
4. outputs получили фиксированные SHA-256;
5. candidate не запускался, listener не открывался, routing не менялся.

## Что НЕ доказано

Не доказаны:

- runtime loopback-only enforcement;
- отсутствие listener на wildcard/LAN интерфейсах;
- memory safety;
- корректность UDP association под нагрузкой;
- bounded connection/resource behavior;
- безопасность file/path/raw-data CLI surfaces;
- Android `VpnService`/TUN boundary;
- Windows transparent routing boundary;
- пригодность к production.

## Admission state

После этого результата статус **не меняется на ADMIT**:

- `admission_status: REVIEW`;
- `release_gate: BLOCK`;
- `may_execute: false`;
- `may_integrate: false`.

## Следующий gate

1. разобрать compiler warnings и прогнать targeted static analysis/sanitizers;
2. сделать isolated runtime loopback regression без внешнего exposure;
3. проверить resource limits/connection lifecycle/UDP state;
4. отдельно провести Android VPN/TUN admission;
5. отдельно провести Windows transparent-routing admission, если этот режим понадобится;
6. повторить advisory review и HIGH_ASSURANCE/human review перед любым `ADMIT`.
