# R28.3 — WinDivert boundary для Windows-кандидата zapret2

Дата фиксации: 28.10.2026.

Статус: **PROVENANCE/INVENTORY PASS / WINDOWS RUNTIME BLOCK**.

## Цель

Этот документ фиксирует отдельную trust boundary WinDivert для Windows-варианта zapret2. Успешная сборка `winws2.exe` сама по себе недостаточна: production component set включает как минимум `WinDivert.dll` и kernel driver `WinDivert64.sys`, поэтому драйверная часть проходит отдельный admission.

На этом этапе WinDivert **не загружается, не устанавливается и не запускается** в NetPilot.

## Exact upstream asset

Проверяемый upstream release:

- repository: `basil00/WinDivert`;
- tag: `v2.2.2`;
- GitHub release asset: `WinDivert-2.2.2-A.zip`;
- release asset ID: `78510020`;
- upstream asset size: `405137` bytes;
- SHA-256 controlled intake: `63cb41763bb4b20f600b6de04e991a9c2be73279e317d4d82f237b150c5f3f15`.

Этот SHA-256 совпадает с independently recorded hash официального `WinDivert-2.2.2-A.zip`, поэтому на текущем этапе признаков подмены архива не обнаружено.

GitHub release API не публикует digest для этого старого asset, поэтому SHA-256 остаётся нашей immutable admission identity и должен проверяться **до распаковки/использования**.

## Controlled x64 inventory

Workflow: `Native Candidate WinDivert Inventory`.

Run: `40726114320`.

Artifact: `12832851711`, `windivert-2.2.2-a-x64-inventory-evidence`.

Artifact ZIP SHA-256: `c444ac9e5f17e0760606df17f983f6b1674d9fe42fbfe5c1755ce5aa85ce0ae8`.

Текстовая фиксация:

`security/native-admission/evidence/zapret2-r28/windivert-2.2.2-a-x64-inventory.txt`.

### WinDivert.dll

- size: `47616` bytes;
- SHA-256: `c1e060ee19444a259b2162f8af0f3fe8c4428a1c6f694dce20de194ac8d7d9a2`;
- Authenticode: `NotSigned`.

`NotSigned` не означает автоматически вредоносность, но означает, что доверие к DLL должно строиться на exact archive provenance + immutable hash, а не на подписи файла.

### WinDivert64.sys

- size: `94144` bytes;
- SHA-256: `8da085332782708d8767bcace5327a6ec7283c17cfb85e40b03cd2323a90ddc2`;
- FileVersion/ProductVersion: `2.2`;
- Authenticode: `Valid`;
- signer subject: `CN=成都密思听科技有限公司, O=成都密思听科技有限公司, S=四川省, C=CN, ...`;
- signer thumbprint: `043589F75FCE2795E7F2CC3E526D46784D5DDAB3`.

Та же driver SHA-256 и тот же signer thumbprint опубликованы в текущем upstream issue #411 для официального `WinDivert-2.2.2-A.zip`. Это дополнительно подтверждает, что наблюдаемый подписант не является уникальной аномалией нашей копии.

## Лицензия

WinDivert dual-licensed: по выбору пользователя лицензии — LGPL v3 либо GPL v2. Для NetPilot предпочтительна отдельная component boundary с неизменённым upstream WinDivert и соблюдением LGPL v3 obligations; точные release/package notices должны войти в будущий installer/release compliance review.

## BLOCKER WD-RUNTIME-001 — driver-load initialization race / BSOD

Upstream issue `basil00/WinDivert#408` (open, 09.08.2026) описывает воспроизводимую гонку при первой загрузке драйвера:

1. первый `WinDivertOpen()` инициирует установку/запуск driver service;
2. device/symbolic link становятся доступны до окончания `windivert_reflect_init()`;
3. параллельный `WinDivertOpen()` может пройти в `IOCTL_WINDIVERT_STARTUP` до инициализации reflect list heads;
4. возможен bugcheck `0xD1`.

Автор отчёта указывает, что затронутые строки идентичны в `v2.2.2` и current master.

**Release effect для NetPilot: BLOCK.**

Мы не считаем достаточным предположение «NetPilot сам открывает драйвер только один раз». На машине могут одновременно работать другие приложения, использующие WinDivert, поэтому безопасность должна быть доказана для реального shared-host lifecycle.

До снятия blocker требуется одно из следующего:

- reviewed upstream fix + новый exact component admission;
- либо доказанная безопасная lifecycle-модель, исключающая race и проверенная concurrent-open stress в изолированной Windows VM;
- либо отказ от WinDivert для production Windows backend.

Без этого NetPilot не должен загружать `WinDivert64.sys` на пользовательской машине.

## BLOCKER WD-LIFECYCLE-002 — stop при чужих открытых handles

Upstream issue `basil00/WinDivert#406` (open, 14.07.2026) описывает состояние, при котором остановка WinDivert service при наличии открытого handle в другом процессе оставляет последующие `WinDivertOpen()` с `ERROR_SERVICE_DISABLED (1058)` до дополнительной остановки/очистки состояния.

Практическое следствие для NetPilot:

- нельзя безусловно `sc stop`/удалять WinDivert service только потому, что завершился наш engine;
- cleanup должен учитывать shared use другими программами;
- update/uninstall не должен повреждать чужую активную WinDivert-сессию;
- ownership/lifecycle policy должна быть отдельно протестирована.

**Release effect: BLOCK для автоматического stop/remove lifecycle до отдельного design + runtime evidence.**

## REVIEW WD-DEFENDER-003 — текущая Defender detection

Upstream issue `basil00/WinDivert#411` открыт 17.10.2026 по официальному `WinDivert-2.2.2-A.zip` с тем же archive SHA-256 `63cb4176...`.

В отчёте Microsoft Defender определил сам ZIP как `Misleading:Win32/Malgent!AMTB` до загрузки DLL/driver. На момент нашей фиксации в issue нет подтверждённого artifact-specific решения Microsoft или reviewed replacement от upstream maintainer.

Это **не доказательство вредоносности WinDivert**, но и не основание игнорировать сигнал.

Политика NetPilot:

- не отключать Defender;
- не добавлять исключения автоматически;
- не восстанавливать quarantined component автоматически;
- не отправлять пользовательские файлы сторонним сканерам без отдельного согласия;
- до production admission получить актуальное artifact-specific решение либо использовать новый reviewed component set.

## Что уже PASS

- точный официальный release asset идентифицирован;
- archive SHA-256 зафиксирован;
- DLL/SYS inventory зафиксирован;
- driver Authenticode status и signer зафиксированы;
- binary/driver не исполнялись в inventory workflow;
- driver не устанавливался и не загружался.

## Что НЕ PASS

- driver runtime safety;
- concurrent open/load behavior;
- coexistence с другими WinDivert-приложениями;
- stop/uninstall lifecycle;
- crash cleanup;
- Defender artifact disposition;
- production signing/provenance policy полного component set.

## Решение R28.3

На текущем этапе:

- `WinDivert 2.2.2-A provenance/inventory = PASS FOR INVENTORY ONLY`;
- `WinDivert 2.2.2-A runtime = BLOCK`;
- `zapret2 Windows candidate = REVIEW / release_gate BLOCK`;
- `may_execute = false`;
- `may_integrate = false`.

Linux zapret2 admission не зависит от WinDivert, поэтому может продолжаться отдельно.

Для Windows параллельно необходимо держать резервную архитектурную ветку без WinDivert (например, proxy/TUN-путь), чтобы один kernel-driver blocker не остановил весь NetPilot.
