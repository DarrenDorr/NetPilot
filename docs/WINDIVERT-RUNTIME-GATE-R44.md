# R44 — WinDivert Windows runtime blocker closure gate

Дата: 31.10.2026.

Статус: **BLOCKED_UPSTREAM / PRE-LAB ONLY / DRIVER EXECUTION FORBIDDEN**.

## Зачем нужен R44

R43 завершил NetPilot-owned boundary для zapret2: точный source checkpoint закреплён, patchset контролируется, Windows x64 build воспроизводим побитно. Это всё ещё не делает Windows runtime безопасным, потому что `winws2` зависит от WinDivert kernel driver.

R44 отделяет три вещи:

1. user-mode zapret2 source/build trust — уже подтверждён scoped evidence;
2. WinDivert driver/runtime trust — всё ещё BLOCK;
3. будущий Windows lab — только после закрытия blockers и отдельного HIGH_ASSURANCE approval.

## Точная zapret2 identity

NetPilot-owned fork:

- repository: `DarrenDorr/NetPilot-zapret2`;
- authoritative branch: `netpilot/reviewed-v1.0.6-uaf-r43`;
- checkpoint: `6207d7e6bae80b8ac1d0e84f6777caff20881890`;
- deterministic Windows x64 output SHA-256: `2a21810cbcf53321abe16c49ca2129adbb8391a6ed97c20a91a874fab8b7237a`;
- output size: `650240` bytes.

Compile evidence не является разрешением на запуск.

## Upstream WinDivert snapshot на 31.10.2026

На дату R44 последним опубликованным release остаётся WinDivert `v2.2.2` с asset `WinDivert-2.2.2-A.zip`.

NetPilot controlled intake identity:

- release asset ID: `78510020`;
- size: `405137` bytes;
- SHA-256: `63cb41763bb4b20f600b6de04e991a9c2be73279e317d4d82f237b150c5f3f15`.

Нового exact tagged release, закрывающего текущие blockers, нет.

## Три активных blocker

### WD-RUNTIME-001 — issue #408

Upstream issue #408 остаётся OPEN. Описана first-load race: control device становится доступен раньше полной инициализации reflect state, и concurrent `WinDivertOpen()` может привести к kernel bugcheck `0xD1`.

Для NetPilot это означает: exact v2.2.2 нельзя загружать даже только ради проверки на пользовательском ПК.

### WD-LIFECYCLE-002 — issue #406

Upstream issue #406 остаётся OPEN. Если глобальный WinDivert service останавливается при наличии чужого открытого handle, последующие `WinDivertOpen()` могут получать `ERROR_SERVICE_DISABLED (1058)`.

NetPilot не должен считать глобальный WinDivert service своей собственностью и не должен force-stop/remove чужой service при stop/update/uninstall.

### WD-DEFENDER-003 — issue #411

Upstream issue #411 остаётся OPEN. Для exact official archive есть повторные сообщения Defender `Misleading:Win32/Malgent!AMTB`, а artifact-specific maintainer/Microsoft disposition не зафиксирован.

NetPilot не отключает Defender, не создаёт exclusions и не восстанавливает quarantined artifacts автоматически.

## Что R44 делает сейчас

Добавлены:

- `security/native-admission/windivert-runtime-gate-r44.yaml` — machine-readable blocker contract;
- `security/native-admission/lab-plans/zapret2-windows-windivert-r44.yaml` — будущий disposable-VM lab plan, но в состоянии `DRAFT_BLOCKED_UPSTREAM`;
- `.github/workflows/windivert-runtime-gate-r44.yml` — fail-closed CI policy.

CI проверяет, что:

- runtime и production admission остаются BLOCK;
- `driver_load_allowed=false`;
- lab execution остаётся false;
- GitHub-hosted execution запрещён;
- self-hosted execution тоже не включён до отдельного approval;
- Defender exclusions, test-signing и force-stop/remove foreign WinDivert service не появляются как обходной путь;
- exact zapret2 checkpoint и deterministic output hash не меняются незаметно.

## Почему мы пока не создаём runtime workflow

Настоящий runtime workflow сейчас был бы ложной готовностью: policy R28.4 требует, чтобы внутри реально исполняемой boundary не оставалось unresolved blocker. В нашем случае все три blockers относятся непосредственно к WinDivert driver/runtime boundary.

Поэтому R44 не содержит job, который вызывает `winws2.exe`, `WinDivertOpen`, устанавливает service или загружает `WinDivert64.sys`.

## Будущий lab после снятия blockers

Когда появится подходящий exact component set, lab разрешается только после отдельного review и обновления R44 identity. Минимальная среда:

- disposable Windows VM;
- clean snapshot до любых privileged network changes;
- физический пользовательский ПК и production host запрещены;
- Secure Boot остаётся включён;
- Microsoft Defender остаётся включён без exclusions;
- никакие browser profiles, cookies, tokens, private keys или рабочие данные не присутствуют;
- exact archive/DLL/SYS hashes и signatures фиксируются до первого load;
- снимается baseline service/driver/process/listener/firewall/routing state.

Планируемая последовательность после approval:

1. single controlled first open;
2. concurrent first-open regression для класса #408;
3. repeated open/close;
4. coexistence с независимо принадлежащим WinDivert handle;
5. normal stop без global foreign-service stop;
6. candidate crash cleanup;
7. hard-timeout cleanup;
8. reboot recovery;
9. uninstall cleanup без разрушения чужого WinDivert lifecycle.

Каждый этап должен быть supervised, bounded по времени и иметь rollback/snapshot semantics.

## Условия перехода из BLOCK

R44 можно перевести только в `PRE_LAB_REVIEW`, а не сразу в ADMIT, когда одновременно выполнены условия:

- выбран новый exact fixed component set;
- source review подтверждает закрытие initialization-race класса #408;
- lifecycle design не требует exclusive ownership глобального WinDivert service;
- Defender disposition приемлем для exact artifact либо используется reviewed replacement;
- driver compile/static evidence зелёный без загрузки драйвера;
- exact hashes/signatures закреплены;
- lab-plan обновлён под этот component set;
- записано explicit HIGH_ASSURANCE human approval.

Даже успешный lab не меняет production admission автоматически.

## Текущий release effect

- `WINDIVERT_RUNTIME=BLOCK`;
- `WINDOWS_ZAPRET2_LAB_EXECUTION=false`;
- `WINDOWS_ZAPRET2_PRODUCTION_ADMISSION=BLOCK`;
- `DRIVER_LOAD_ALLOWED=false`;
- `PRODUCT_INTEGRATION=false`.

R44 не разрешает native execution и не ослабляет Windows security controls.
