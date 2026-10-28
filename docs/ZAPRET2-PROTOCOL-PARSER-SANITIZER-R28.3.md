# R28.3 — изолированная ASan/UBSan-проверка protocol parser zapret2

Дата: 28.10.2026.

Статус: **PASS FOR ISOLATED PROTOCOL-PARSER SANITIZER REGRESSION ONLY / NOT ADMITTED / RELEASE GATE BLOCK**.

## Цель

Проверить критичные parser entry points exact-кандидата zapret2 без запуска `winws2`, без WinDivert и без сетевого перехвата.

Проверяемая цепочка:

- zapret2 v1.0.6 exact release commit `221d7dff24bfec0cd5ac0a5070b8acef2008af3d`;
- exact upstream UAF fix `4a33055448ce5f4e7acc704e1fbe0d8a8a6aa08d`;
- NetPilot patches `0001`–`0004`;
- LuaJIT headers exact commit `dcc9c9ee67e1a5d3d636bd7745e95ddb4a1c70bc` только как compile-time headers для покрываемого кода.

## Важное исправление тестового fixture

Первый запуск gate корректно завершился ошибкой `known-good TLS ClientHello was not recognized`.

Разбор показал, что причина находилась не в parser zapret2, а в тестовом fixture NetPilot: TLS record и handshake объявляли 72 байта полного ClientHello, но массив содержал только 68 байт — в 32-байтовом поле `Random` не хватало четырёх байтов.

Fixture исправлен commit `52d679013c66d959cfc70606ed974ee524588717`; дополнительно добавлена явная проверка `sizeof(tls_client_hello) == 72`. Ожидания parser не ослаблялись и исходники zapret2 ради прохождения этого теста не менялись.

## Успешный gate

Workflow: `Native Candidate Protocol Parser Sanitizer`.

- run: `40730430434`;
- job: `122964950398`;
- head: `52d679013c66d959cfc70606ed974ee524588717`;
- conclusion: `success`.

Evidence artifact:

- ID: `12835176063`;
- name: `zapret2-r28-protocol-parser-sanitizer-evidence`;
- ZIP SHA-256: `d98a85f30149672c838893b3a2ba441271ec30cbf8be97efff99aa625fb68179`.

Текстовая фиксация сохранена в `security/native-admission/evidence/zapret2-r28/protocol-parser-sanitizer-regression.txt`.

## Покрытие

Harness собран с AddressSanitizer и UndefinedBehaviorSanitizer и проходит:

- валидный TLS ClientHello с SNI `example.com`;
- все последовательные усечения этого ClientHello;
- короткие входы из нулей, `0xff` и последовательных байтов;
- 12 000 детерминированных псевдослучайных входов до 2048 байт;
- entry points TLS, HTTP, QUIC, DNS, Discord IP discovery, STUN, DTLS, WireGuard, DHT, MTProto, BitTorrent и uTP/BT.

Для возвращаемых parser указателей/диапазонов и marker positions выполняются дополнительные bounds assertions.

Через `strace` подтверждено отсутствие сетевых syscall во время harness-run.

## Что PASS означает

В покрытом deterministic corpus ASan/UBSan не обнаружили memory-safety/UB ошибки, harness завершился `PASS`, а parser не вышел в сеть.

Это существенно повышает качество evidence для parser boundary, но **не является доказательством отсутствия всех parser bugs** и не заменяет coverage-guided fuzzing.

## Компиляторные предупреждения upstream

При сборке `protocol.c`/`helpers.c` остаются предупреждения upstream, включая precedence `&&/||`, signed/unsigned comparisons, unused variables/functions и implicit fallthrough. Они не были автоматически объявлены уязвимостями и не скрываются из evidence. Их требуется отдельно классифицировать при дальнейшем hardening review.

## Состояние admission

Статус кандидата не повышается:

- `admission_status: REVIEW`;
- `release_gate: BLOCK`;
- `may_execute: false`;
- `may_integrate: false`.

## Следующие gates

1. resource/load regression для conntrack и delayed queue с измерением saturation semantics;
2. coverage-guided/parser fuzzing с сохранённым seed corpus;
3. isolated runtime lab start/stop/restart/crash/cleanup;
4. HTTPS/YouTube и Discord TCP/UDP только в изолированной runtime-среде;
5. повторный advisory/CVE/KEV review exact component set;
6. HIGH_ASSURANCE/human review перед возможным `ADMIT`.
