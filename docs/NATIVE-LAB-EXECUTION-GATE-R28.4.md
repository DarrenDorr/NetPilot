# R28.4 — допуск native-кандидата к изолированному lab-runtime

Дата: 29.10.2026.

Статус: **POLICY ACTIVE / НИ ОДИН NATIVE ENGINE ЕЩЁ НЕ РАЗРЕШЁН К PRODUCTION-ЗАПУСКУ**.

## Зачем нужен отдельный lab-gate

До R28.4 у Net.Pilot уже есть сильные compile/static/sanitizer/fuzz/resource evidence, но это всё ещё не означает, что настоящий `nfqws2/winws2`, ByeDPI или другой native engine можно запускать на пользовательском ПК.

Мы разделяем три разных состояния:

1. `REVIEW/BLOCK` — кандидат изучается, запуск запрещён;
2. `LAB_APPROVED` — конкретная версия может быть запущена только по отдельному утверждённому плану в изолированной тестовой среде;
3. `ADMIT` — отдельное решение о возможности production-интеграции.

`LAB_APPROVED` **никогда автоматически не означает `ADMIT`**.

Машиночитаемая политика хранится в:

`security/native-admission/lab-execution-policy-r28.4.yaml`.

## Обязательные условия до первого реального запуска

Для конкретного candidate/platform должны быть одновременно выполнены следующие условия:

- точная source/release identity закреплена;
- точный NetPilot patchset закреплён Git/SHA-256 идентичностями;
- provenance/compile gates зелёные;
- относящиеся к boundary static/sanitizer/parser/resource gates зелёные;
- нет открытого release-blocker внутри границы, которую планируется реально исполнять;
- executable и аргументы заранее фиксированы, arbitrary shell/raw CLI запрещены;
- пользовательские Lua/path/URL не допускаются;
- persistent auto-hostlist и experimental ipcache-hostname выключены;
- resource budgets заданы явно и не равны нулю;
- среда не содержит пользовательских cookies/tokens/private keys/рабочих данных;
- есть snapshot/restore point;
- до теста снимается состояние firewall/routing/network rules;
- cleanup после normal stop/crash/timeout заранее определён;
- известен точный outbound scope теста;
- packet capture по умолчанию выключен;
- текстовые evidence не содержат payload, credentials и лишнюю историю IP/URL;
- для exact candidate/platform/lab-plan получено отдельное **HIGH_ASSURANCE human approval**.

## Ограничения самого lab-runtime

Даже после такого approval:

- не устанавливаем автозапуск;
- не превращаем candidate в постоянную службу;
- запускаем процесс только foreground/supervised;
- используем hard timeout;
- после теста проверяем отсутствие дочерних процессов и listeners;
- возвращаем firewall/routing в исходное состояние;
- сравниваем network state до/после;
- privileged network component дополнительно проверяем после reboot;
- сохраняем hashes binaries/config profile, но не сетевые payload.

## Linux

Текущее состояние: **PRE-LAB REVIEW**.

Главная privileged boundary — NFQUEUE + временное изменение firewall/routing.

Перед первым Linux runtime-test нужно отдельно утвердить план, где:

- используется disposable VM;
- создаются только временные NetPilot-owned nftables/iptables objects с уникальными именами;
- exact rules сохраняются до запуска;
- crash/kill/timeout не должен оставлять трафик заблокированным;
- после теста подтверждается полное удаление наших rules/queues;
- первый runtime запускается на synthetic/local target, а не сразу на Discord/YouTube.

Таким образом Linux-кандидат технически ближе к lab-runtime, чем Windows, но **ещё не разрешён к запуску**.

## Windows

Текущее состояние: **BLOCK**.

Причина не в compile/sanitizer zapret2: эти gates уже проходят. Блокирующая граница — WinDivert kernel driver/component lifecycle.

До отдельного решения запрещена установка/загрузка WinDivert, пока остаются актуальными:

- `WD-RUNTIME-001` — upstream issue #408: driver-load initialization race / возможный BSOD `0xD1`;
- `WD-LIFECYCLE-002` — shared-handle lifecycle / `ERROR_SERVICE_DISABLED 1058` и потенциально заблокированный driver file;
- `WD-DEFENDER-003` — unresolved Defender report для exact `WinDivert-2.2.2-A` component set.

Мы не будем обходить эти ограничения отключением Defender, exclusions или принудительным stop/remove чужого WinDivert service.

## Что R28.4 разрешает прямо сейчас

Разрешены только дальнейшие **неисполняющие native engine** проверки:

- статический анализ;
- compile-only;
- ASan/UBSan harnesses;
- coverage-guided fuzzing изолированных функций;
- synthetic resource tests;
- provenance/SBOM/advisory review;
- подготовка lab-plan.

## Что R28.4 не разрешает

Без отдельного candidate-specific `LAB_APPROVED` нельзя:

- запускать `nfqws2/winws2`;
- запускать ByeDPI как SOCKS proxy;
- устанавливать/загружать WinDivert;
- создавать NFQUEUE/firewall interception rules;
- проверять реальные Discord/YouTube profiles через native candidate;
- включать engine в Net.Pilot production runtime.

## Следующий шаг

Для zapret2: подготовить отдельный Linux lab-plan с rollback/crash semantics и workload measurement design, но не выполнять его до HIGH_ASSURANCE approval.

Для ByeDPI: продолжить static/sanitizer hardening (`KAVL`, PID-file, Windows warnings), затем отдельно сформировать loopback-only runtime plan.

Windows zapret2 остаётся BLOCK до закрытия WinDivert boundary.
