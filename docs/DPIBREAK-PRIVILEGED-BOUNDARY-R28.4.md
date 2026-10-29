# R28.4 — privileged boundary DPIBreak v0.6.2

Дата: 29.10.2026.

Статус: **REVIEW / WINDOWS RELEASE BLOCKED / LINUX PRE-LAB REVIEW / NO NATIVE EXECUTION AUTHORIZED**.

Проверяемая точка:

- repository: `dilluti0n/dpibreak`;
- tag: `v0.6.2`;
- commit: `334285b60aabd5a32a83a65211ac60bce968b211`;
- tree: `7e95a17933fe9fda521865b1317c4f9f75d6c58c`.

Этот документ не разрешает запуск DPIBreak, WinDivert, NFQUEUE, `nft`, `iptables` или `modprobe` из Net.Pilot.

## 1. Linux: пользовательский `--nft-command` внутри root-процесса

Exact v0.6.2:

- требует root в Linux bootstrap;
- принимает `--nft-command <string>`;
- сохраняет это значение в `OPT_NFT_COMMAND`;
- `rules::nft()` передаёт его как program в `std::process::Command::new(program)`;
- rule text передаётся через stdin, то есть это не shell-string concatenation.

Важно правильно классифицировать finding.

Это **не shell injection**: значение не передаётся через `/bin/sh -c` и отдельные аргументы не склеиваются в shell command line.

Но это privileged executable-selection surface: пользователь, имеющий возможность формировать CLI root-процесса, может выбрать другой executable вместо `nft`.

Для самостоятельного upstream CLI это power-user capability. Для Net.Pilot она запрещена нашими инвариантами.

### Net.Pilot policy

- UI/config/IPC никогда не принимают `--nft-command`;
- raw CLI не передаётся native candidate;
- privileged adapter использует фиксированный project-reviewed executable identity;
- пользовательский path/program name не допускается;
- любые будущие изменения этого правила требуют HIGH_ASSURANCE review.

На текущем этапе этот finding можно закрывать adapter boundary без Linux fork, если typed adapter действительно не предоставляет такую поверхность.

## 2. Linux: firewall lifecycle принадлежит самому DPIBreak

`src/platform/linux.rs::run()` перед запуском выполняет cleanup предыдущих rules, затем вызывает `rules::install()`.

`rules.rs` создаёт собственные nftables/iptables objects и удаляет их через `Drop for InstalledRules`. Также код может загружать `xt_u32` через фиксированный `modprobe` и при определённых условиях выгружать модуль при cleanup.

Положительная сторона:

- `iptables`, `ip6tables` и `modprobe` зафиксированы в исходнике;
- нет shell concatenation;
- есть cleanup-механизмы.

Но Net.Pilot всё равно требует lab evidence:

- exact rule inventory;
- ownership/naming;
- startup после предыдущего crash;
- normal stop;
- forced kill/timeout;
- partial install failure;
- reboot recovery;
- before/after firewall/routing diff;
- отсутствие удаления/изменения чужих rules/modules.

Open upstream issue #28 подтверждает, что hard-coded firewall bootstrap сам upstream считает отдельной архитектурной проблемой.

## 3. Windows: unconditional global WinDivert service stop

Exact v0.6.2 после закрытия собственных handles выполняет:

`windivert::WinDivert::uninstall()`.

In-tree wrapper реализует `uninstall()` через Windows Service Control Manager и отправляет `SERVICE_CONTROL_STOP` службе с глобальным именем `WinDivert`.

Это нельзя считать безопасным ownership model для Net.Pilot.

### Почему это release blocker

Open WinDivert issue #406 прямо описывает сценарий:

- один процесс ещё держит WinDivert handle;
- другой процесс останавливает service;
- последующие `WinDivertOpen()` могут получать `ERROR_SERVICE_DISABLED (1058)`;
- состояние способно сохраняться даже после завершения процессов до дополнительного service action.

В issue отдельно отмечено, что приложению небезопасно останавливать service на exit, потому что оно не владеет handles других процессов.

Следовательно, Net.Pilot не должен запускать exact Windows DPIBreak v0.6.2 с этой lifecycle semantics.

## 4. Windows: upstream master пока не устранил lifecycle behavior

Проверка актуального `master` на 29.10.2026 показывает, что `src/platform/windows.rs` всё ещё безусловно вызывает `WinDivert::uninstall()` после `cleanup_all()` / `close_send_handle()`.

Последний опубликованный stable release по-прежнему `v0.6.2`.

Поэтому ждать уже существующий tagged fix сейчас не на что.

## 5. Windows: WinDivert driver race остаётся внешним blocker

Open WinDivert issue #408 описывает driver-load initialization race в WinDivert 2.2.2/current master с возможным bugcheck `0xD1` при конкурентном первом `WinDivertOpen()` во время загрузки driver.

Это blocker ниже уровня DPIBreak.

Даже если мы исправим DPIBreak service cleanup, Windows runtime нельзя считать допустимым, пока exact выбранный WinDivert component set не закрывает #408 или мы не имеем отдельно проверенный безопасный вариант driver lifecycle.

Net.Pilot не будет пытаться обходить этот blocker последовательными retry, отключением Defender или другими скрывающими проблему workaround.

## 6. Windows wrapper: дополнительные review findings

### 6.1 Excessive SCM/service access

In-tree `WinDivert::uninstall()` использует `SC_MANAGER_ALL_ACCESS` для открытия SCM и service перед `SERVICE_CONTROL_STOP`.

Для Net.Pilot это чрезмерная privilege request. Если lifecycle code когда-либо сохраняется в нашем fork, права должны быть минимизированы до конкретно необходимых операций.

### 6.2 Cleanup on error

Текущая реализация возвращает ошибку сразу после неуспешного `ControlService`. На таком пути `CloseServiceHandle(service)` и `CloseServiceHandle(manager)` ниже по функции не выполняются.

Это bounded process-lifetime handle leak, но одновременно признак того, что service lifecycle code требует RAII/finally-style cleanup перед admission.

### 6.3 TLS slot lifecycle

`WinDivert::new()` вызывает `TlsAlloc()`. В in-tree wrapper exact v0.6.2 не найден `TlsFree`.

Это не доказательство exploitable vulnerability и может быть ограничено временем жизни процесса, но ресурсная lifecycle semantics должна быть исправлена или отдельно обоснована перед использованием wrapper как долгоживущего компонента.

### 6.4 Нет общего `Drop for WinDivert`

В exact wrapper не найден общий `Drop` implementation для `WinDivert`; закрытие handles выполняется вручную через `close()` в вызывающем коде.

Для Net.Pilot это повышает важность проверки всех early-return/error/crash paths.

### 6.5 Batch receive robustness

`recv_ex()` использует длины/адреса, возвращённые native FFI, затем выполняет slicing и parser operations, включая `expect`/`unwrap`/`split_at`.

Основной DPIBreak v0.6.2 использует single-packet `recv()`, а не `recv_ex()`, поэтому это не текущий main execution path кандидата. Но если Net.Pilot fork или будущий upstream начнёт использовать batch API, потребуется отдельный malformed/native-output robustness gate.

## 7. Решение по fork strategy

Ранее стратегия была `UPSTREAM_FIRST_NO_FORK_UNLESS_BLOCKER`.

После source review она уточняется:

### Linux

**UPSTREAM_FIRST**.

Пока не найден обязательный source patch, если Net.Pilot adapter:

- не передаёт `--nft-command`;
- использует только typed allowlisted options;
- отдельно доказывает reversible firewall/NFQUEUE lifecycle в lab.

### Windows

**MINIMAL_HARDENED_FORK_OR_UPSTREAM_FIX REQUIRED BEFORE LAB**.

Минимальный Windows patchset должен как минимум:

1. убрать unconditional stop/uninstall глобальной `WinDivert` service;
2. не считать service owned только потому, что DPIBreak открыл WinDivert handle;
3. минимизировать SCM/service access rights;
4. гарантировать handle cleanup на success/error paths;
5. определить TLS-slot cleanup или доказать безопасный bounded lifecycle;
6. не включать upstream daemon/service mode в Net.Pilot integration;
7. сохранить fixed internal WinDivert filters и typed Net.Pilot profile boundary.

Однако создавать production-ready Windows fork прямо сейчас преждевременно: WinDivert #408 остаётся отдельным kernel-driver blocker. Сначала fork patch можно подготовить и compile-test, но runtime остаётся запрещён.

## 8. Admission effect

После этого review:

- DPIBreak overall: `REVIEW`;
- release gate: `BLOCK`;
- `may_execute: false`;
- `may_integrate: false`;
- Linux: `PRE_LAB_REVIEW`, draft plan non-executable;
- Windows: `BLOCK`, source hardening + fixed/admitted WinDivert required;
- Android: не выбран для текущего DPIBreak path.

Ни один PASS provenance/compile/static gate не разрешает candidate runtime автоматически.
