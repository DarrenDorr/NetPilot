# R28.2 — semantics conntrack saturation для zapret2

Дата: 28.10.2026.

Проверяемая исходная точка: `bol-van/zapret2@4a33055448ce5f4e7acc704e1fbe0d8a8a6aa08d`.

Статус: **DESIGN / NOT ADMITTED / SOURCE PATCH NOT APPLIED**.

## Почему понадобился отдельный проход

R28.1 определил требование: ограничить агрегатный conntrack state и при насыщении не допускать неограниченного роста памяти. Нельзя было просто добавить проверку количества записей и вернуть `false` из `ConntrackPoolFeed()`, не проверив, что означает `false` для всех callers.

Проверка exact post-UAF-fix tree показала, что такой простой patch был бы семантически неверным.

## 1. Текущее значение `ConntrackPoolFeed() == false`

В upstream API `ConntrackPoolFeed()` возвращает `bool`.

`false` может означать как минимум:

- packet/flow не подходит для создания новой conntrack entry;
- entry не удалось создать;
- caller продолжает работу без conntrack state.

Эти состояния сейчас неразличимы.

### Main packet path (`desync.c`)

В `play_prolog()`:

1. вызывается `ConntrackPoolPurge()`;
2. вызывается `ConntrackPoolFeed()`;
3. только при `true` заполняются `ps->ctrack`, `ps->ctrack_replay` и cached profile;
4. если `false`, функция **не возвращает VERDICT_PASS автоматически**;
5. направление угадывается по interface names;
6. затем всё равно может быть найден matching desync profile через `dp_find()`;
7. packet может продолжить stateless desync processing.

Следовательно:

**`return false` из-за resource saturation НЕ эквивалентен безопасному pass-through.**

Если добавить cap и просто вернуть `false`, NetPilot не получит гарантированное fail-safe поведение — движок может продолжить desync без conntrack.

### Lua API path (`lua.c`)

`luacall_conntrack_feed()` вызывает тот же `ConntrackPoolFeed()`.

При `true` Lua получает `track, bOutgoing`; при `false` — `nil, nil`.

Для Lua API состояние «не trackable» и «ресурсный budget исчерпан» также сейчас неразличимо.

В первом production NetPilot profile пользовательский Lua запрещён, поэтому main packet path имеет более высокий приоритет; однако изменение public/internal conntrack API должно сохранить предсказуемость и для Lua call site.

## 2. Требуемое разделение результата

До введения count cap предлагается сначала выполнить отдельный behavior-preserving refactor результата feed.

Пример внутренней модели (названия не считаются окончательным ABI):

```text
CTRACK_FEED_NONE
CTRACK_FEED_OK
CTRACK_FEED_RESOURCE_EXHAUSTED
```

Смысл:

- `NONE` — обычный существующий сценарий, когда entry не найдена/не создаётся по протоколу/состоянию; main path может сохранить текущее stateless поведение;
- `OK` — entry найдена или успешно создана;
- `RESOURCE_EXHAUSTED` — новый state был бы нужен, но безопасный product budget/allocator не позволяет его создать.

Важно: `RESOURCE_EXHAUSTED` не должен маскироваться под `NONE`.

## 3. Поведение main packet path при saturation

Для NetPilot fork целевое поведение `RESOURCE_EXHAUSTED`:

1. не применять stateful или stateless desync к новому flow в этом packet path;
2. оставить `ps->verdict = VERDICT_PASS`;
3. прекратить `play_prolog()` с `false` до `dp_find()` и дальнейших desync действий;
4. не создавать partial conntrack/Lua/queue state;
5. не вытеснять случайную активную запись для освобождения места;
6. зарегистрировать только bounded diagnostic counter, без IP/hostname/payload.

Такое поведение является fail-safe для NetPilot: при исчерпании собственного внутреннего ресурса движок временно перестаёт модифицировать **новый** flow, а не рискует памятью или неполным state.

Это решение относится именно к NetPilot fork policy; upstream не обязан выбирать ту же product semantics.

## 4. Поведение существующих flow

Cap не должен ломать уже отслеживаемые connections.

Перед проверкой budget необходимо сначала выполнить обычный direct/reverse search:

- если entry уже существует — `OK`, packet продолжает использовать её;
- cap проверяется только перед созданием **новой** entry;
- purge expired/cutoff entries выполняется до feed, как сейчас;
- saturation не удаляет действующую entry.

Это предотвращает ситуацию, когда достижение лимита внезапно ломает все текущие Discord/YouTube sessions.

## 5. OOM и product saturation

Обычный product cap и allocator OOM различаются по причине, но оба не должны приводить к продолжению stateful desync с частично созданным объектом.

Минимальная модель может объединить их в `RESOURCE_EXHAUSTED` для main packet safety, при этом diagnostic counter разделяет причины внутренне:

- `conntrack_budget_rejected`;
- `conntrack_allocation_failed`.

Никакие адреса/hostname/payload в диагностике не нужны.

## 6. Почему не используем LRU eviction как первое решение

Автоматическое удаление старой, но ещё активной connection ради новой записи может:

- разрушить cached direction/profile/state;
- сломать delayed/reassembly state;
- изменить поведение существующего приложения непредсказуемо;
- создать churn под нагрузкой.

Поэтому первый NetPilot hardening design:

`purge expired/cutoff -> если существующая entry, использовать -> если нужна новая и budget исчерпан, fail-safe PASS для нового flow`.

LRU/priority admission может исследоваться позже только с отдельными нагрузочными доказательствами.

## 7. Delayed queue — отдельный byte budget

Exact `rawpacket_queue` уже имеет `max_packets`; conntrack delayed queue инициализируется с `RAW_PACKET_QUEUE_DELAYED_MAX = 64`.

Но `rawpacket_queue` хранит только `max_packets` и не учитывает суммарные bytes. Каждый enqueue выделяет `malloc(len)` и копирует packet.

Требуемый второй механизм:

- `queued_bytes`;
- `max_bytes`;
- overflow-safe проверка `len > max_bytes - queued_bytes` до allocation;
- увеличение счётчика только после успешного enqueue;
- уменьшение при dequeue;
- обнуление после destroy;
- существующий packet-count cap остаётся defense-in-depth.

Здесь также нельзя молча превращать queue failure в новый тип поведения без проверки caller paths. Existing reassembly callers уже имеют explicit failure branches; для каждого требуется regression test.

## 8. Предлагаемая patch series

Чтобы изменения были проверяемыми, hardening лучше разделить:

### Patch 0001 — explicit conntrack feed result

- заменить неоднозначный `bool` внутренним enum/result;
- сохранить текущее поведение для `NONE` и `OK`;
- обновить два известных call sites (`desync.c`, `lua.c`);
- без resource cap и без изменения packet behavior.

Цель: behavior-preserving refactor и база для тестов.

### Patch 0002 — aggregate conntrack entry budget

- добавить count/budget state;
- проверять budget только перед новой allocation;
- возвращать `RESOURCE_EXHAUSTED` при saturation/allocation failure;
- main `play_prolog()` возвращает с уже установленным `VERDICT_PASS` до `dp_find()`;
- Lua API возвращает `nil, nil` для unavailable track; при необходимости внутренний diagnostic code остаётся вне пользовательского Lua API.

Числовой production budget пока не фиксируется: механизм должен поддерживать test-configured value, а production значение выбирается после R28.2 load evidence.

### Patch 0003 — delayed queue byte budget

- добавить byte accounting;
- overflow-safe admission до `malloc(len)`;
- regression tests enqueue/dequeue/destroy/saturation;
- не ослаблять существующий 64-packet cap.

## 9. Tests до controlled native build

Минимальные unit/source tests patch series:

- existing conntrack entry работает при заполненном budget;
- новая entry получает `RESOURCE_EXHAUSTED` при заполненном budget;
- non-trackable packet остаётся `NONE`, а не saturation;
- `play_prolog()` на saturation не доходит до `dp_find()`/desync;
- purge освобождает budget;
- allocation failure не оставляет hash/Lua partial state;
- delayed queue rejects byte overflow до allocation;
- `queued_bytes` возвращается к нулю после dequeue/destroy;
- packet-count limit продолжает работать;
- Lua `conntrack_feed` не получает dangling/partial track.

## 10. Решение R28.2

Простая схема `if cap reached -> ConntrackPoolFeed returns false` **отклонена** как небезопасная по semantics.

Перед resource cap нужен explicit result channel, который отделит обычное отсутствие track от resource exhaustion. Только `RESOURCE_EXHAUSTED` должен переводить main packet path в гарантированный `VERDICT_PASS` до profile/desync processing.

Это уменьшает риск того, что hardening памяти незаметно превратится в другое сетевое поведение.

До применения и тестирования patch series статус zapret2 fork candidate остаётся **REVIEW / BLOCK**.
