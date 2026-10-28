# R28.1 — статический admission-аудит zapret2 post-UAF-fix

Дата: 28.10.2026.

Проверяемая точка исходников: `bol-van/zapret2@4a33055448ce5f4e7acc704e1fbe0d8a8a6aa08d`.

Эта точка является прямым дочерним коммитом официального release commit v1.0.6 `221d7dff24bfec0cd5ac0a5070b8acef2008af3d` и содержит upstream fix ранее найденного heap-use-after-free. Документ не присваивает `ADMIT` и не разрешает запуск native engine.

## Итог текущего прохода

Статус: **REVIEW / release gate BLOCK**.

Исправление UAF принимается как корректный narrow upstream delta для дальнейшего исследования. Однако exact post-fix tree всё ещё содержит несколько поверхностей, которые нельзя переносить в production NetPilot без ограничения или патча.

## 1. UAF — исправление присутствует

Commit `4a330554...` после `reasm_client_cancel()` возвращает `rdata_payload` и `rlen_payload` к payload текущего пакета перед повторным `rediscover`. Тем самым ранее сохранённое представление освобождённого reassembly buffer больше не передаётся дальше по найденному пути.

Решение: **известный UAF из v1.0.6 считается исправленным в этой точке**, но это не означает безопасность остального движка.

## 2. Conntrack — нет агрегатного resource budget

`ConntrackNew()` выделяет новый `t_conntrack_pool`, добавляет его в hash и создаёт два Lua registry state object для каждого track. Новый track создаётся для TCP SYN/SYN+ACK и UDP. `ConntrackPoolPurge()` удаляет записи по состоянию/таймауту, но в проверенном коде нет общего ограничения количества entries или общего memory budget.

Риск для NetPilot: при достаточно большом количестве уникальных flows память процесса может расти до момента purge/OOM. Достижимость зависит от WinDivert/NFQUEUE filter и направления перехвата.

Предварительное hardening-решение:

- не считать уменьшение таймаутов достаточной защитой само по себе;
- определить отдельный bounded conntrack policy для NetPilot;
- при достижении предела поведение должно быть fail-safe/fail-open для пользовательского трафика, а не приводить к падению процесса;
- точный алгоритм (hard cap / bounded eviction) и значения лимитов определить после нагрузочного теста и проверки влияния на стратегии.

## 3. IPCache — нет count/memory cap, default lifetime 7200 секунд

`ipcache4Add()` / `ipcache6Add()` создают hash entries для новых адресов. `ipcache_purge()` удаляет их только по lifetime; `IPCACHE_LIFETIME` в `params.h` равен 7200 секунд. Агрегатного лимита entries/memory в проверенном коде не обнаружено.

Для NetPilot:

- функция hostname cache не должна включаться просто ради удобства;
- экспериментальный `--ipcache-hostname` остаётся **запрещённым** до отдельного решения;
- если ipcache понадобится для обязательной стратегии, потребуется собственный bounded policy.

## 4. DNS hostname correlation — недостаточна для доверенного cache

`feed_dns_response()` при включённом `params.cache_hostname` извлекает имя из question и записывает A/AAAA answer в общий ipcache. В проверенном пути нет проверки ранее наблюдавшегося DNS query по transaction ID + flow/5-tuple и нет привязки owner name каждого A/AAAA answer к доверенной цепочке question/CNAME.

Решение NetPilot: **не включать `ipcache-hostname`** в admitted profile set до отдельного патча/review. Таким образом этот путь можно исключить из production attack surface без изменения upstream-кода на первом этапе.

## 5. Auto-hostlist — packet-derived hostname попадает в постоянный файл

TLS SNI parser копирует hostname с приведением к lower-case, но не применяет строгий allowlist символов hostname. Auto-hostlist path затем передаёт `hostname` в `append_to_list_file()`, который пишет строку через `fprintf("%s\n", s)`.

Hostlist loader имеет line-oriented syntax и дополнительно определяет gzip по содержимому файла. Поэтому packet-derived control bytes и специальное содержимое нельзя считать безопасными для постоянного auto-hostlist.

Решение NetPilot для первого admitted профиля: **persistent auto-hostlist полностью отключён**. Если функция когда-либо понадобится, она проходит отдельный review с:

- canonical hostname validation;
- запретом control bytes;
- quota/rotation;
- безопасным plain-text format;
- non-fatal recovery повреждённого файла.

## 6. Lua/CLI — мощная upstream поверхность, которую NetPilot не экспортирует

Exact `nfqws2` поддерживает среди прочего:

- `--writable[=<dir>]`;
- `--blob=<name>:@<file>|0xHEX`;
- `--lua-init=@<file>|<lua_text>`;
- `--lua-desync=<function>:...`;
- hostlist/ipset file paths;
- Windows raw WinDivert filter options.

Это допустимо для универсального upstream-инструмента, но недопустимо как пользовательская privileged API NetPilot.

Решение:

- UI/IPC/config никогда не принимают raw `nfqws2` arguments;
- пользователь не передаёт Lua text, Lua path, blob path, hostlist path, ipset path или WinDivert raw filter;
- разрешены только внутренние project-owned hash-pinned Lua/data assets после admission;
- engine adapter получает типизированный `EngineProfileId` и компилирует его в закрытый allowlisted `ArgumentList`;
- неизвестный параметр/профиль → fail closed.

## 7. Что можно исключить конфигурацией, а что требует патча

### Можно безопасно исключить из первого production profile set

- `ipcache-hostname`;
- persistent auto-hostlist;
- user Lua / luaexec text;
- `--writable`;
- arbitrary blob/file/path arguments;
- raw WinDivert filters;
- server/high-load gateway mode.

### Требует дальнейшего решения/патча

- агрегатный conntrack resource budget;
- возможный bounded ipcache policy, если cache окажется нужен admitted профилям;
- точный Windows WinDivert interception scope;
- crash/failure cleanup и resource behavior под нагрузкой.

## 8. Предварительный NetPilot hardening patchset

До написания кода фиксируется минимальный принцип: **не форкать upstream ради функций, которые можно просто не включать**.

Предлагаемый patchset должен быть узким:

1. upstream UAF fix `4a330554...` — обязательная база;
2. bounded conntrack protection с измеряемым fail-safe поведением;
3. bounded ipcache только если он реально нужен нашим профилям;
4. build/version identity NetPilot fork;
5. тесты, подтверждающие, что опасные Lua/file/raw-filter поверхности недостижимы через NetPilot adapter.

Auto-hostlist и experimental DNS hostname cache на первом этапе лучше **не патчить, а не включать вообще**. Это сокращает объём собственного форка и стоимость дальнейшего сопровождения.

## 9. Supply-chain exact post-fix build

В source tree `4a330554...` готового post-fix Windows binary нет: каталог `binaries` прямо указывает, что бинарные файлы публикуются только в releases либо собираются из исходников. Следовательно, exact post-UAF-fix candidate для NetPilot в любом случае требует собственной controlled build.

Upstream GitHub Actions полезен как reference build recipe, но не принимается как наша trust policy без изменений. В проверенном workflow:

- используются плавающие `actions/checkout@v4`, `actions/upload-artifact@v4`, `actions/cache/restore@v4` и `cygwin/cygwin-install-action@v4`, а не immutable action commit SHA;
- Windows build скачивает LuaJIT archive по HTTPS и собирает его локально;
- Windows packaging скачивает exact WinDivert `2.2.2-A` по URL, но в самом recipe перед использованием не сверяет ожидаемый SHA-256;
- Cygwin runtime и `killall.exe` берутся из подготовленной Cygwin environment/package flow;
- другие platform jobs также используют внешние archives/toolchains, местами через `latest` discovery.

Это не считается finding против upstream-проекта: его CI решает другую задачу. Но для NetPilot fork admission требования строже.

Controlled NetPilot build должен:

1. pin GitHub Actions по immutable full commit SHA;
2. pin exact Cygwin packages/toolchain identity;
3. pin Lua/LuaJIT source version и SHA-256;
4. pin WinDivert exact version/archive SHA-256 и отдельно проверить DLL/SYS identity/signature;
5. не использовать `latest` как источник доверия;
6. сохранять build manifest: source commit, compiler/toolchain, dependency versions/hashes, output hashes;
7. не публиковать artifact как admitted до независимого сравнения inventory и security evidence.

## 10. Следующие проверки до кода форка

1. Завершить exact Windows/Cygwin dependency inventory для post-fix build и выбрать пинованные источники.
2. Проверить WinDivert filter construction и определить минимальный необходимый inbound/outbound scope для Discord/YouTube.
3. Спроектировать conntrack cap и нагрузочный тест до выбора числового значения.
4. Собрать exact source tree в controlled CI с sanitizers/static analysis там, где это возможно.
5. После этого решить, создаём ли отдельный `NetPilot-zapret2` fork repository.

## Решение R28.1 на текущей точке

**zapret2 post-UAF-fix остаётся кандидатом №1**, но `ADMIT` пока не получает.

Главная причина оставшегося BLOCK-gate теперь не известный UAF (он исправлен), а необходимость доказать bounded resource behavior, зафиксировать воспроизводимый/pinned build chain и сформировать минимальный закрытый NetPilot profile/CLI boundary.
