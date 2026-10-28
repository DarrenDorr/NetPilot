# R28.3 — fail-closed семантика conntrack budget zapret2

Дата: 28.10.2026.

Статус: **DESIGN FIXED / REGRESSION EVIDENCE PENDING / NOT ADMITTED / RELEASE GATE BLOCK**.

## Что обнаружено

Patch `0002-bounded-conntrack-resource-result.patch` добавил aggregate `max_entries` budget и отдельный результат `CTRACK_FEED_RESOURCE_EXHAUSTED`.

При повторной проверке реального call path обнаружено важное свойство:

- `ConntrackPoolInit()` инициализирует `max_entries = 0`;
- первоначальная проверка patch `0002` трактовала `0` как отсутствие лимита;
- setter `ConntrackPoolSetMaxEntries()` существовал, но production engine ещё не имел допущенного typed adapter, который гарантированно задаёт ненулевое значение.

Следовательно, механизм ограничения существовал, но забытая/отсутствующая конфигурация могла оставить conntrack **неограниченным**. Для NetPilot это fail-open и release blocker.

## Решение

Добавлен отдельный минимальный patch:

`security/native-admission/patches/zapret2-r28/0005-fail-closed-zero-conntrack-budget.patch`

Immutable identity:

- Git blob SHA-1: `4f3582eea971885fabdd1ea6bc8423187b8e70e8`;
- SHA-256: `67cbbfa656c47b7342a8c56ab178716e7890de0f05efc25eed2cc20b299c4ed1`.

Новая семантика:

- `max_entries == 0` означает **budget не настроен**;
- existing flow lookup разрешён и не создаёт новый state;
- создание нового flow при `max_entries == 0` возвращает `CTRACK_FEED_RESOURCE_EXHAUSTED`;
- main desync path уже обрабатывает этот result как fail-safe `VERDICT_PASS` до `dp_find()`/desync;
- после явной установки ненулевого budget bounded state creation снова разрешается.

Таким образом ошибка конфигурации должна ухудшать только эффективность обхода для новых flow, а не приводить к неограниченному росту state или к блокировке пользовательского трафика.

## Почему не выбран произвольный production лимит

На этом этапе мы намеренно **не выбираем число на глаз**.

Числовой production `max_entries` должен быть получен из controlled workload evidence для как минимум:

- обычного браузинга;
- YouTube;
- Discord TCP/UDP;
- combined workload;
- burst;
- hostile/resource-pressure сценариев.

До появления этих измерений typed production adapter обязан считать ноль недопустимым значением конфигурации и не должен включать candidate runtime.

## Regression harness

`security/native-admission/harnesses/zapret2-r28/conntrack_budget_harness.c` проверяет:

1. invalid input не создаёт state;
2. при cap `3` первые три UDP flow создаются;
3. existing forward flow продолжает находиться при saturation;
4. reverse flow продолжает находиться при saturation;
5. четвёртый новый flow при cap `3` получает `RESOURCE_EXHAUSTED`;
6. при cap `0` existing flow остаётся доступным;
7. при cap `0` новый flow получает `RESOURCE_EXHAUSTED` и pool не растёт;
8. после явного cap `4` четвёртый flow снова допускается;
9. `ConntrackPoolDestroy()` освобождает весь state.

Workflow:

`.github/workflows/native-candidate-conntrack-budget-regression.yml`

Он использует ASan/UBSan и отдельно проверяет отсутствие network syscalls. Candidate engine, WinDivert и network interception не запускаются.

## Admission effect

До успешного regression + повторных compile/provenance gates:

- `admission_status: REVIEW`;
- `release_gate: BLOCK`;
- `may_execute: false`;
- `may_integrate: false`.

Даже успешный isolated regression не определит production numeric budget и не будет означать `ADMIT`.
