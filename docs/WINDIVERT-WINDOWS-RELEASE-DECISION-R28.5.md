# R28.5 — WinDivert boundary для Windows-релиза Net.Pilot

Дата: 29.10.2026.

Статус: **BLOCK / DESIGN REVIEW ONLY / DRIVER RUNTIME ЗАПРЕЩЁН**.

## Цель

Зафиксировать отдельное решение по WinDivert как kernel-mode dependency для Windows-версии Net.Pilot. Этот документ не разрешает установку, загрузку или тестовый запуск драйвера.

## Upstream identity

Последний опубликованный upstream release на дату проверки:

- WinDivert `v2.2.2`;
- release commit: `1789526ecfb9ff5397c94f9f54c1a3dc2fb60440`;
- release tree: `214b01fe064d851b7ba037fb5b9c7b8fd890c55a`;
- официальный release asset: `WinDivert-2.2.2-A.zip`.

Лицензия upstream: dual-license по выбору LGPLv3 или GPLv2. Для возможного отдельного Net.Pilot fork предполагается рассматривать LGPLv3 boundary; MIT-core Net.Pilot не должен поглощать изменённый WinDivert source.

## Текущие блокеры

### WD-RUNTIME-001 / upstream issue #408

Upstream issue #408 остаётся открытым. Описана race при первой загрузке драйвера: device/symlink становятся доступны до полной инициализации reflect state, после чего concurrent `WinDivertOpen()` может привести к использованию неинициализированных `reflect_lock` / list heads и bugcheck `0xD1`.

Повторная проверка current upstream source на commit `97101072dbe31d744ca3429da389350a3df39e18` подтверждает порядок:

1. создаётся control device;
2. создаётся default I/O queue;
3. создаётся symbolic link;
4. вызывается `WdfControlFinishInitializing(device)`;
5. только после этого выполняется длительная настройка injection handles / WFP sublayers;
6. `windivert_reflect_init((WDFOBJECT)device)` вызывается в самом конце успешной настройки.

`windivert_reflect_init()` инициализирует `reflect_lock`, три list heads и work item, после чего выставляет `reflect_inited = TRUE`. При этом `windivert_reflect_open_event()` использует `reflect_lock`, `reflect_event_queue` и `reflect_worker` без предварительной fail-closed проверки `reflect_inited`.

**Решение Net.Pilot:** Windows runtime на exact v2.2.2 и на непроверенном current master запрещён.

### Предпочтительный source-level fix design для #408

Наиболее узкий и архитектурно чистый candidate fix — **не публиковать control device для I/O до завершения всей инициализации драйвера**.

Предлагаемый порядок для будущего отдельного NetPilot-owned fork, если он вообще потребуется:

1. сохранить `WdfDeviceCreate`, I/O queue и `WdfDeviceCreateSymbolicLink` в существующей фазе создания control device;
2. убрать ранний вызов `WdfControlFinishInitializing(device)` из этой фазы;
3. завершить создание injection handles, buffer pools и WFP transaction;
4. успешно выполнить `windivert_reflect_init((WDFOBJECT)device)`;
5. только после успешной инициализации всех зависимостей вызвать `WdfControlFinishInitializing(device)` как финальную публикацию control device;
6. при любой ошибке до этого шага уйти в существующий cleanup без публикации устройства для пользовательского I/O.

Почему этот вариант предпочтителен:

- KMDF документирует, что система не отправляет I/O/WMI requests в control device до `WdfControlFinishInitializing`;
- сохраняется существующий parent `device` для `WdfWorkItemCreate`, поэтому не требуется искусственно разделять lifetime reflect worker;
- закрывается не только конкретное обращение к `reflect_event_queue`, а сам класс раннего пользовательского I/O до завершения `DriverEntry`;
- не требуется молча отбрасывать REFLECT open/close events и создавать асимметричное состояние;
- изменение локализовано в порядке публикации control device и должно быть отдельно проверено WDF/driver review.

Defense-in-depth проверка `reflect_inited` в reflect paths может рассматриваться отдельно, но не должна заменять корректный initialization barrier и не должна молча скрывать open/close event semantics.

### Что требуется доказать до применения такого fix

До появления реального fork/patch обязательны:

- независимый HIGH_ASSURANCE review порядка KMDF lifecycle;
- проверка всех error/unload paths при control device, который ещё не прошёл `WdfControlFinishInitializing`;
- compile/static analysis с WDK/KMDF без загрузки драйвера;
- driver-verifier/HLK design review;
- только после отдельного `LAB_APPROVED` — isolated first-load concurrency/lifecycle regression на disposable Windows VM;
- отсутствие изменения публичной фильтрационной семантики WinDivert за пределами initialization barrier.

Текущий статус этого design: **RECOMMENDED_FOR_REVIEW / NOT_APPLIED / NO_FORK_CREATED**.

### WD-LIFECYCLE-002 / upstream issue #406

Upstream issue #406 остаётся открытым: global service stop при чужих открытых WinDivert handles может оставить состояние `ERROR_SERVICE_DISABLED (1058)` и блокировать повторное открытие/обновление driver file.

**Решение Net.Pilot:** приложение не должно останавливать/удалять глобальный WinDivert service, если оно не может доказать исключительное владение lifecycle. Никакого forced stop/remove чужого service.

### WD-SIGNING-003 / production signing

Модифицированный kernel-mode driver нельзя считать готовым к публичному Windows-релизу только после локальной сборки. Для современного Windows production path требуется Microsoft driver signing через Hardware Dev Center; production distribution должна учитывать актуальные WHCP/HLK требования. Test/attestation сценарии не заменяют production release decision.

**Решение Net.Pilot:** unsigned/self-signed driver не допускается в production и не рассматривается как обходной путь.

## Допустимые пути закрытия blocker

### Путь A — предпочтительный

Upstream выпускает новый exact tagged release, в котором:

1. #408 исправлен и fix подтверждён source review;
2. lifecycle #406 не требует unsafe global service stop со стороны Net.Pilot;
3. release содержит допустимый подписанный component set;
4. exact DLL/SYS hashes, signatures и provenance проходят новый admission.

После этого новый release рассматривается с нуля; автоматического доверия к `latest` нет.

### Путь B — собственный WinDivert fork

Разрешён только если Путь A недоступен и fork действительно нужен для Windows v1.0.

До создания production fork обязательны:

1. минимальный source-level fix design для #408;
2. независимый HIGH_ASSURANCE human review kernel change;
3. compile/static analysis без driver execution;
4. отдельная license/distribution схема LGPLv3 boundary;
5. подтверждённая возможность Microsoft production driver signing;
6. отдельный Net.Pilot-owned repository; upstream остаётся read-only;
7. собственное имя/версия fork, без выдачи за официальный upstream build.

Только после этого можно готовить candidate-specific isolated driver lab-plan.

## Что запрещено

До закрытия данного gate запрещено:

- устанавливать/загружать модифицированный WinDivert driver на пользовательских ПК;
- отключать Secure Boot ради production-сценария;
- отключать Microsoft Defender или добавлять exclusions для обхода проверки;
- использовать test-signing как production решение;
- force-stop/remove чужой WinDivert service;
- автоматически брать `master` или GitHub `latest` как доверенную версию;
- включать Windows zapret2/DPIBreak production runtime только потому, что user-mode engine прошёл compile/sanitizer gates.

## Admission effect

На текущем этапе:

- Windows zapret2: `BLOCK`;
- Windows DPIBreak: `BLOCK`;
- WinDivert runtime: `BLOCK`;
- production driver signing: `NOT_READY`;
- #408 source fix: `RECOMMENDED_FOR_REVIEW / NOT_APPLIED`;
- WinDivert fork created: `false`;
- driver execution: `false`;
- production effect: `NONE`.

Этот документ является только release-decision boundary и не является разрешением на runtime.
