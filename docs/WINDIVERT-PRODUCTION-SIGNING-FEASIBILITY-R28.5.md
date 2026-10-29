# R28.5 — feasibility production signing для возможного NetPilot-WinDivert fork

Дата: 29.10.2026.

Статус: **FEASIBILITY REVIEW / FORK НЕ СОЗДАН / DRIVER RUNTIME ЗАПРЕЩЁН**.

## Вывод

Собственный исправленный WinDivert fork технически возможен, но его нельзя рассматривать как обычную bundled DLL-зависимость. Для публичного Windows-релиза это отдельный kernel-driver product/release pipeline.

По актуальной документации Microsoft:

- kernel-mode drivers для современных Windows должны получать подпись через Windows Hardware Dev Center;
- для регистрации в Windows Hardware Developer Program требуется EV code-signing certificate;
- HLK-tested / WHCP submission является рекомендуемым production-путём;
- attestation signing сейчас относится к testing-сценариям и не даёт retail Windows Update publication;
- каждая новая Production-сборка driver package должна снова получить Microsoft signature.

Следовательно, локальная сборка, self-signing, test-signing или простая Authenticode-подпись не являются достаточным production-путём для Net.Pilot.

## Что это означает для Net.Pilot

### Путь A — предпочтительный для Windows v1.0

Использовать только новый исправленный official WinDivert release, если upstream:

1. закрывает initialization race #408;
2. предоставляет приемлемую lifecycle-модель относительно #406;
3. выпускает exact tagged component set;
4. component set имеет приемлемый Microsoft-signing/provenance status;
5. новый release повторно проходит Net.Pilot admission.

До появления такого exact release автоматическое доверие к `master`/`latest` запрещено.

### Путь B — собственный NetPilot-WinDivert fork

Создавать только после отдельного GO решения, когда подтверждены одновременно:

- необходимость собственного kernel fix для Windows v1.0;
- HIGH_ASSURANCE review точного WDF/KMDF fix design;
- возможность получить/использовать EV code-signing certificate;
- возможность зарегистрироваться и работать через Microsoft Hardware Developer Program;
- выбран production signing route, предпочтительно HLK/WHCP;
- готова отдельная LGPLv3 distribution/compliance boundary;
- есть отдельный NetPilot-owned repository и signing-key isolation plan.

До выполнения этих условий fork не создаётся.

## Предварительный fix design #408

См. `docs/WINDIVERT-WINDOWS-RELEASE-DECISION-R28.5.md`.

Предпочтительный design: использовать `WdfControlFinishInitializing(device)` как настоящий initialization barrier — публиковать control device для I/O только после WFP setup и успешного `windivert_reflect_init(device)`.

Статус: **RECOMMENDED_FOR_REVIEW / NOT_APPLIED**.

## Почему нельзя просто оставить WinDivert 2.2.2-A

Текущий exact official driver имеет валидную наблюдаемую подпись, но это не закрывает runtime defect. Upstream issue #408 остаётся открытым и описывает race, способную вызвать bugcheck `0xD1` при concurrent first-load/open. Подпись подтверждает происхождение/целостность подписанного файла, но не доказывает отсутствие runtime defects.

Отдельно issue #411 оставляет unresolved Defender disposition для exact official archive, а issue #406 оставляет shared-service lifecycle risk.

## Почему нельзя решить это настройкой пользователя

Net.Pilot не должен требовать от пользователя:

- отключать Secure Boot;
- включать Windows test-signing mode;
- отключать Defender;
- добавлять Defender exclusions;
- устанавливать self-signed kernel driver;
- вручную force-stop/remove чужой WinDivert service.

Такие шаги не являются допустимым production UX или security boundary.

## Резервный путь без собственного kernel driver

Параллельно архитектура Net.Pilot должна сохранять backend, не требующий нашего собственного kernel-mode driver. Текущий кандидат — hardened ByeDPI как loopback-only user-mode proxy. Однако он не является автоматически system-wide transparent replacement для WinDivert и отдельно не решает Discord UDP/TUN/routing. Любой TUN/transparent routing component также должен проходить свой admission.

## Release effect

На текущем этапе:

- `NetPilot-WinDivert fork created = false`;
- `production signing ready = false`;
- `Windows zapret2 runtime = BLOCK`;
- `Windows DPIBreak runtime = BLOCK`;
- `official WinDivert 2.2.2-A inventory = PASS_FOR_INVENTORY_ONLY`;
- `driver execution = false`;
- `release_gate = BLOCK`.

Следующее GO/NO-GO решение по собственному WinDivert fork принимается только после подтверждения реальной организационной/signing возможности и независимого review kernel fix design.
