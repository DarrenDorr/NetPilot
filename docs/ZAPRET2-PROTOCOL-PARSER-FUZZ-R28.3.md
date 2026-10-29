# R28.3 — coverage-guided fuzzing protocol parser zapret2

Дата: 29.10.2026.

Статус: **PASS FOR BOUNDED COVERAGE-GUIDED PROTOCOL PARSER FUZZ ONLY / NOT ADMITTED / RELEASE GATE BLOCK**.

## Цель

Дополнить deterministic ASan/UBSan parser regression отдельным coverage-guided fuzz gate на libFuzzer, не запуская `nfqws2/winws2`, WinDivert или сетевой перехват.

Проверяемая цепочка:

- zapret2 v1.0.6 exact release commit `221d7dff24bfec0cd5ac0a5070b8acef2008af3d`;
- exact upstream UAF fix `4a33055448ce5f4e7acc704e1fbe0d8a8a6aa08d`;
- source tree `5c2a77786cc7089145abe2ca99bcc53a8ed7de15`;
- NetPilot patches `0001`–`0005`;
- LuaJIT headers commit `dcc9c9ee67e1a5d3d636bd7745e95ddb4a1c70bc`.

## Linker boundary

Первый fuzz workflow не дошёл до исполнения: Clang/libFuzzer удержал дополнительные секции `protocol.c`, из-за чего linker потребовал символы, которые deterministic GCC harness удалял через section GC.

Исправление не ослабило fuzz target:

- подключён exact crypto source set из того же pinned zapret2 tree;
- `params` и `tld` определены только для удовлетворения retained TLS mutation sections;
- `DLOG` / `DLOG_ERR` реализованы как **abort-on-use stubs**.

Если retained logging/TLS-mutation path неожиданно станет достижимым из fuzzed parser entry points, тест аварийно завершится вместо молчаливого сокрытия зависимости.

## Успешный gate

Workflow: `Native Candidate Protocol Parser Fuzz`.

- run: `40802559290`;
- head: `646be8205092584b775903eb646a3d9e21560fae`;
- conclusion: `success`.

Artifact:

- ID: `12866891622`;
- name: `zapret2-r28-protocol-parser-fuzz-evidence`;
- ZIP SHA-256: `353d9d59a765d8517ad2a6bdd02a4db522b988ab92f5a111609005c5daab8155`.

Текстовая фиксация:

`security/native-admission/evidence/zapret2-r28/protocol-parser-fuzz.txt`.

## Конфигурация

- compiler: Clang 18.1.3;
- sanitizers: libFuzzer + ASan + UBSan;
- runs: `25000`;
- seed: `1337`;
- max input length: `2048` bytes;
- controlled initial corpus: TLS ClientHello with SNI `example.com`, HTTP request, empty input.

Проверяются те же parser families, что и в deterministic gate: TLS, HTTP, QUIC, DNS, Discord IP discovery, STUN, DTLS, WireGuard, DHT, MTProto, BitTorrent и uTP/BT.

## Результат

libFuzzer:

- executed units: `25000`;
- new corpus units: `374`;
- crash artifacts: `0`;
- peak RSS: `51 MB`;
- ASan/UBSan failure: не обнаружено в этом bounded run.

Отдельный network-syscall run завершился с пустым `strace -e trace=network` log.

## Что PASS означает

В рамках данного bounded coverage-guided run не найдено memory-safety/UB crash в покрытых parser entry points, fuzz target не вышел в сеть, а abort-on-use linker stubs не стали достижимыми.

Это усиливает parser evidence поверх deterministic regression, но не означает, что 25 000 executions обеспечивают исчерпывающее fuzz-покрытие.

## Что PASS НЕ означает

Не доказаны:

- отсутствие всех parser bugs;
- безопасность Lua/desync/conntrack/process/runtime subsystem целиком;
- correctness реального QUIC decryption/desync под сетевой нагрузкой;
- WinDivert/NFQUEUE lifecycle;
- production profile behavior;
- отсутствие дефектов, требующих намного более длинных fuzz campaigns.

## Admission effect

Статус кандидата остаётся:

- `admission_status: REVIEW`;
- `release_gate: BLOCK`;
- `may_execute: false`;
- `may_integrate: false`.

Перед `ADMIT` по-прежнему обязательны platform-specific isolated runtime tests, реальные workload measurements для resource budgets, полный component-set/advisory review и отдельный HIGH_ASSURANCE/human review.
