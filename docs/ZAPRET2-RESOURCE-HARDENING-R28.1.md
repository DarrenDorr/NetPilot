# R28.1 — проект bounded-resource hardening для zapret2

Дата: 28.10.2026.

Основа анализа: `bol-van/zapret2@4a33055448ce5f4e7acc704e1fbe0d8a8a6aa08d`.

Статус: **DESIGN ONLY / NOT ADMITTED / NOT IMPLEMENTED**.

Этот документ определяет требования к минимальному NetPilot patchset. Он не включает native engine в runtime и не выбирает окончательные числовые лимиты без нагрузочных тестов.

## 1. Почему одного conntrack count cap недостаточно

В exact source tree:

- conntrack pool создаёт отдельную запись для новых TCP SYN/SYN+ACK и UDP flows;
- каждая запись содержит Lua state references, hostname/state и reassembly state;
- TCP/UDP reassembly ограничен на уровне desync значением 16 KiB;
- delayed raw packet queue имеет предел 64 пакета на track;
- каждый delayed packet делает отдельный `malloc(len)` и копирует полный пакет;
- у очереди есть count limit, но нет byte limit.

Следовательно, `max_connections=N` сам по себе ограничивает количество объектов, но не даёт достаточно узкой верхней границы памяти: несколько активных reassembly sessions могут одновременно держать значительные packet copies.

## 2. Цель NetPilot hardening

Нужно получить следующие инварианты:

1. число conntrack entries имеет конечный production cap;
2. объём delayed packet bytes на один track имеет конечный cap;
3. reassembly size остаётся bounded;
4. достижение любого resource limit не приводит к OOM/crash;
5. существующие отслеживаемые соединения не выбрасываются случайным LRU только ради принятия новых hostile flows;
6. новый flow, которому нельзя безопасно выделить state, проходит **без изменения пакета** (fail-open для пользовательского соединения), а не блокируется;
7. после purge освобождённая ёмкость снова доступна;
8. saturation не пишет IP/hostname/packet contents в постоянные логи;
9. лимиты нельзя отключить через обычный NetPilot UI/config/IPC.

## 3. Предлагаемая модель

### 3.1 Conntrack entry cap

Добавить production limit `max_entries` в `t_conntrack` либо эквивалентный внутренний policy object.

Порядок при новом packet:

1. сначала искать уже существующий track;
2. если track найден — обновлять его независимо от текущего count cap;
3. если требуется новый track — выполнить обычный purge;
4. если после purge `entry_count >= max_entries`, **не создавать запись**;
5. вернуть вызывающему коду отдельный результат `SATURATED`, отличный от обычного `MISS/INVALID/OOM`.

Почему нельзя просто удалить «самую старую» запись: eviction активного connection state может изменить работу desync посреди соединения и создать трудно воспроизводимые сетевые ошибки.

### 3.2 Явный fail-open при saturation

Сейчас отсутствие conntrack может приводить к fallback-логике определения направления и продолжению части desync. Для security boundary этого недостаточно: saturation должен иметь отдельную семантику.

При `SATURATED` NetPilot fork должен завершать обработку текущего пакета с `VERDICT_PASS`, без Lua/desync/reassembly для нового flow.

Существующие tracked flows продолжают обрабатываться штатно.

Таким образом resource exhaustion ухудшает только эффективность обхода для новых соединений, но не превращается в отказ обычной сети.

### 3.3 Delayed queue byte cap

Расширить `rawpacket_queue` дополнительным счётчиком:

- `bytes_queued`;
- `max_bytes`.

`rawpacket_queue()` перед `malloc(len)` проверяет одновременно:

- packet count limit;
- `len <= max_bytes - bytes_queued` без integer overflow.

При dequeue/free счётчик уменьшается строго на сохранённый `rp->len`.

При превышении byte cap функция возвращает `NULL`; существующий reassembly error path должен быть проверен так, чтобы освобождать state и пропускать трафик безопасно.

### 3.4 Reassembly

Текущий TCP/UDP desync reassembly использует предел 16 KiB. В первом NetPilot fork увеличивать этот предел запрещено без отдельного review.

Нужно добавить regression tests, что malformed length/fragment sequence не позволяет:

- выйти за 16 KiB reassembly allocation;
- переполнить arithmetic size counters;
- обойти delayed byte cap через много маленьких или один большой packet.

## 4. Числовые значения

В этом документе **намеренно не фиксируются** production значения `max_entries` и `max_queue_bytes`.

Выбор по принципу «4096 выглядит нормально» не является evidence.

Перед выбором значений нужно измерить минимум:

- idle browser workload;
- YouTube 1080p/4K;
- Discord text + voice + screen/video path, где применимо;
- одновременную работу браузера, Discord и типичной фоновой Windows-нагрузки;
- burst из большого количества коротких TCP/UDP flows;
- hostile synthetic unique-flow load;
- несколько параллельных fragmented TLS/QUIC handshakes.

Для каждого сценария фиксируются:

- peak conntrack entries;
- peak delayed packets;
- peak delayed bytes;
- process private working set;
- число saturation events;
- влияние на latency/throughput;
- корректность cleanup после stop/crash.

Production cap выбирается с запасом от измеренного нормального peak и отдельно проверяется на hostile workload.

## 5. API patch design

Предпочтительно не ломать upstream API больше необходимого.

Возможный минимальный вариант:

```text
ConntrackPoolFeedBounded(...) -> enum
  OK
  NO_TRACK
  SATURATED
```

Старый `ConntrackPoolFeed()` может остаться internal compatibility wrapper, если это уменьшит diff.

Для NetPilot packet path используется bounded API, чтобы `SATURATED` нельзя было спутать с обычным отсутствием track.

Для queue:

```text
rawpacket_queue_init(q, max_packets, max_bytes)
```

либо отдельный setter, если это делает upstream diff меньше. Production NetPilot всегда задаёт ненулевой byte cap.

## 6. Откуда берутся лимиты

Пользователь **не** задаёт их как raw CLI.

Допустимые варианты после тестов:

- compile-time NetPilot policy constants;
- либо внутренний typed engine policy, где значения заданы приложением и проходят узкий numeric range validator.

Нельзя:

- читать их из произвольного BAT/Lua;
- принимать raw `--conntrack-*` строку через IPC;
- позволять `0 = unlimited` в production profile;
- менять policy из скачанного strategy/config файла.

## 7. Тесты, обязательные до ADMIT

### Unit/source-level

1. entry cap `N`: N новых flows создаются, N+1 получает `SATURATED`;
2. существующий flow продолжает обновляться при полном pool;
3. purge освобождает слот и следующий flow снова допускается;
4. delayed queue отклоняет packet при превышении byte cap до allocation/copy;
5. dequeue/destroy корректно возвращают `bytes_queued` к нулю;
6. integer-overflow cases для `bytes_queued + len` fail closed;
7. allocation failure и saturation ведут к безопасному PASS, не DROP/crash;
8. TCP и UDP пути проверяются отдельно.

### Sanitizer/fuzz

- ASan/UBSan на Linux build;
- replay regression для ранее найденного UAF;
- malformed TLS/QUIC fragments;
- random packets на границе cap;
- repeated saturation/purge cycles.

### Windows isolated runtime

- WinDivert start/stop;
- saturation при TCP/UDP burst;
- нормальный интернет остаётся доступен во время saturation;
- после stop/crash нет оставшегося packet interception state;
- повторный запуск восстанавливает нормальную работу.

## 8. Что этот patch не должен делать

- не добавлять телеметрию;
- не логировать адреса/пакеты для статистики saturation;
- не добавлять удалённое управление;
- не добавлять dynamic script/config execution;
- не менять upstream Lua semantics без необходимости;
- не включать auto-hostlist или `ipcache-hostname`;
- не расширять WinDivert filter scope;
- не превращать NetPilot fork в отдельный большой feature fork.

## 9. Решение

Для zapret2-кандидата требуется **не один, а минимум два связанных resource bounds**:

1. bounded conntrack entries;
2. bounded delayed bytes per tracked connection.

Вместе с существующим 16 KiB reassembly limit это позволяет получить доказуемо более узкую верхнюю границу stateful memory, чем один timeout или один connection-count cap.

Следующий шаг — измерительный harness/controlled build. Только после реальных peak-данных выбираются production значения и пишется patch в отдельном NetPilot-zapret2 fork.
