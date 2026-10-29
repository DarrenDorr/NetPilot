# R28.4 — draft Linux lab-plan для zapret2

Дата: 29.10.2026.

Статус: **DRAFT / EXECUTION NOT APPROVED / PRODUCTION EFFECT NONE**.

## Цель

Подготовить строго ограниченный план будущего изолированного Linux runtime-test для exact-кандидата zapret2, не разрешая фактический запуск на этом этапе.

Машиночитаемый план:

`security/native-admission/lab-plans/zapret2-linux-r28.4.yaml`

Проверяемый candidate:

- zapret2 `v1.0.6` exact release commit `221d7dff24bfec0cd5ac0a5070b8acef2008af3d`;
- exact upstream UAF fix `4a33055448ce5f4e7acc704e1fbe0d8a8a6aa08d`;
- NetPilot hardening patches `0001`–`0005`.

## Что разрешено сейчас

Только подготовка evidence и плана:

- provenance;
- compile-only;
- static/sanitizer analysis;
- parser sanitizer/fuzz;
- synthetic resource stress;
- фиксация rollback требований;
- фиксация evidence schema.

Реальный `nfqws2/winws2` **не запускается**.

## Основные fail-closed условия

План требует до любого будущего запуска:

1. pin exact runtime binary SHA-256;
2. зелёные provenance/compile/static/parser/fuzz/resource gates для exact patchset;
3. disposable Linux VM;
4. clean snapshot до privileged network changes;
5. отсутствие пользовательских cookies, токенов, ключей и рабочих данных;
6. явные ненулевые resource budgets;
7. fixed executable + typed allowlisted profile;
8. запрет arbitrary shell/raw CLI/user Lua/user paths/user URLs;
9. foreground/supervised runtime only;
10. hard timeout + process-tree cleanup;
11. NetPilot-owned временные firewall/NFQUEUE objects с уникальным prefix;
12. rollback на normal stop, crash и timeout;
13. post-test diff для rules/routes/listeners;
14. packet capture выключен по умолчанию;
15. отдельный HIGH_ASSURANCE human approval exact candidate/platform/plan.

## Стадии

### LNX-0 — identity/environment verification

Только проверка identity и baseline. Запуск движка запрещён.

Нужно зафиксировать:

- exact binary SHA-256;
- exact source/patch identities;
- disposable VM и snapshot;
- baseline process/listener/firewall/route inventory.

### LNX-1 — supervised lifecycle на synthetic/local target

Сейчас `executable_run_allowed: false`.

После отдельного approval планируется проверить:

- foreground start;
- controlled stop;
- hard timeout;
- forced termination;
- process-tree cleanup;
- firewall/NFQUEUE rollback;
- отсутствие residual listener/process/rules.

### LNX-2 — bounded resource measurement

Разблокируется только после PASS LNX-1 и нового approval.

Планируемые synthetic workloads:

- browser-shaped TCP;
- video-shaped TCP/UDP;
- realtime-shaped UDP;
- combined bounded burst;
- hostile resource-pressure workload.

Цель — получить evidence для ненулевых production-like `conntrack_max_entries`, `delayed_queue_max_packets` и `delayed_queue_max_bytes`, не выбирая числа «на глаз».

### LNX-3 — public-service compatibility

Полностью закрыта текущим draft.

Никакие Discord/YouTube/public-service тесты этим планом не разрешаются. Для них потребуются PASS предыдущих стадий, отдельный scope review и новый HIGH_ASSURANCE approval.

## Evidence schema

Разрешено хранить только operational metadata, например:

- candidate binary SHA-256;
- source/patch identities;
- profile ID;
- start/stop timestamps;
- exit code и stop reason;
- cleanup result;
- residual process/listener/rule counts;
- peak conntrack/queue counters.

Запрещено сохранять:

- packet payloads;
- credentials;
- cookies/tokens/private keys;
- полную browsing history;
- лишнюю историю remote IP/URL.

## Что этот документ НЕ означает

Этот draft не переводит candidate в `LAB_APPROVED` и тем более не означает `ADMIT`.

Даже будущий PASS lab-run:

- не меняет release gate автоматически;
- не разрешает production activation;
- требует отдельного security admission decision.

## Следующий безопасный шаг

1. завершить текущие R28.4 CI/gates exact head;
2. зафиксировать evidence Resource Stress и Protocol Parser Fuzz;
3. завершить ByeDPI static/sanitizer hardening;
4. после этого определить, какой candidate первым готов к отдельному human review на `LAB_APPROVED`.
