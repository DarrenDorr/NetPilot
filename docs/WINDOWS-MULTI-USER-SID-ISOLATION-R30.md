# R30 — Windows multi-user SID isolation + global engine lease

Дата: 29.10.2026.

Статус: **IMPLEMENTED IN FEATURE BRANCH / HOSTED CI EVIDENCE PENDING / NO NATIVE ENGINE ADMIT / RELEASE GATE BLOCK**.

## Цель

NetPilot не должен быть программой только для одного Windows-пользователя. После безопасной первичной установки любой проверенный интерактивный Windows SID может открыть NetPilot и работать со своим отдельным пользовательским состоянием.

При этом системные ресурсы остаются общими для машины и не могут одновременно управляться разными пользователями.

## Модель ролей

### Installation owner

Первый standard-user SID, который проходит реальную Inno UAC / `ExecAsOriginalUser` регистрацию, сохраняется в:

`Config/authorized-user.sid`

Этот SID больше не является единственным пользователем NetPilot. Он является владельцем **машинной установки** и сохраняет право на machine-level provisioning, например установку системного engine package.

Другой пользователь не может заменить installation owner обычным IPC-запросом.

### Runtime user

Любой клиент, который прошёл production named-pipe authentication как реальный интерактивный Windows SID, после завершения первичного enrollment получает собственный профиль:

`Users/<Windows SID>/`

Имя Windows-пользователя не используется как security identity и не является частью пути. Источником identity является только SID из проверенного Windows token named-pipe peer.

Служебные SID (`LocalSystem`, `LocalService`, `NetworkService`, Anonymous) пользовательский профиль получить не могут.

## IPC boundary

Не меняется существующая production-проверка:

- локальный named pipe;
- owner pipe ACL — LocalSystem;
- Network SID запрещён;
- доступ к каналу только Interactive SID / LocalSystem / Administrators согласно ACL;
- service-side peer authentication повторно проверяет PID, Session ID, interactive token и SID;
- Session 0, anonymous/guest и service-account client отклоняются fail-closed.

R30 не превращает pipe в `Authenticated Users` и не принимает SID из пользовательского payload.

## Per-SID profile isolation

`WindowsUserProfileStore` создаёт отдельный fixed root только после успешной peer authentication:

`<NetPilot installation>/Users/<validated SID>`

Контракт:

- SID обязан быть валидным Windows `SecurityIdentifier`;
- path traversal через SID невозможен;
- service/anonymous SID запрещены;
- profile root обязан быть strict child каталога `Users`;
- reparse point в installation/users/profile path отклоняется fail-closed;
- повторное обращение того же SID возвращает тот же профиль;
- разные SID получают разные profile roots.

## Service-owned filesystem ACL

Пользовательский профиль не является каталогом, который Windows-пользователь должен читать напрямую.

`WindowsUserProfileDirectorySecurity` выполняется только внутри LocalSystem-службы и применяет protected DACL:

- `NT AUTHORITY\SYSTEM` — Full Control;
- `BUILTIN\Administrators` — Full Control;
- наследуемые правила удаляются;
- другие ACL-записи fail-closed не допускаются.

Обычный пользователь работает со своим состоянием только через authenticated IPC. Это не позволяет Account B читать профиль Account A напрямую и одновременно не требует давать обычным пользователям write-доступ под `Program Files`.

Live IPC lab после успешного `Ping=Ok` дополнительно пытается открыть собственный SID profile напрямую. PASS возможен только при:

`PROFILE_DIRECT_ACCESS=DENIED`

Если профиль отсутствует, не защищён ACL или стандартный пользователь способен перечислить его содержимое, lab client завершится ошибкой.

На R30 в profile root пока не помещается credential material. Любое будущее сохранение токенов/секретов потребует отдельного at-rest review; SID isolation сама по себе не заменяет secret protection.

## Machine-level provisioning

Машинный engine package является общим ресурсом Windows.

Поэтому `ZapretInstallLatest` дополнительно требует, чтобы authenticated client SID совпадал с installation owner SID.

Обычный runtime user может видеть состояние установленного engine и в будущем пользоваться admitted runtime, но не может через обычный IPC заменить machine-level installation owner или самовольно выполнить provisioning.

## Global engine lease

Существующий `UserResourceOwnership` используется как global engine lease.

Правила:

1. Если engine свободен, authenticated runtime user может получить lease при `Start`.
2. Тот же SID может продолжать управлять своей активной engine-сессией.
3. Другой SID не может перехватить или остановить активную сессию.
4. Для другого SID ответ не раскрывает имя, SID или профиль владельца; возвращается только `EngineOwnedByAnotherUser` с нейтральным сообщением.
5. При остановленном engine lease очищается.
6. Стратегия активной сессии показывается только SID-владельцу lease; другой пользователь видит только общий running/stopped state.

## Изменение R29 semantics

R29 первоначально ожидал:

`Account A -> Ping=Ok`

`Account B -> UnauthorizedUser`

R30 заменяет это на:

`Account A -> Ping=Ok -> service-owned Users/<SID-A>`

`Account B -> Ping=Ok -> service-owned Users/<SID-B>`

Дополнительно lab обязан подтвердить:

- SID-A != SID-B;
- оба пользователя standard-user;
- оба получают `PROFILE_DIRECT_ACCESS=DENIED`;
- owner SID после обращения Account B не изменился;
- deterministic profile roots различаются по SID;
- uninstall удаляет service, owner enrollment state и per-SID profile state.

Таким образом второй пользователь теперь проверяет multi-user isolation, а не запрет доступа.

## Uninstall

Установщик создаёт системный `Users` root, а runtime-профили создаёт только LocalSystem-служба.

При удалении:

- служба сначала безопасно останавливается и удаляется;
- `Config` удаляется;
- `Engines` удаляется;
- весь `Users` tree удаляется повышенным uninstaller;
- live lab проверяет отсутствие `Program Files\NetPilot\Users` после uninstall.

## Что R30 не разрешает

R30 не:

- выдаёт `ADMIT` zapret2/ByeDPI/DPIBreak;
- включает production `EngineStart`;
- устанавливает или загружает WinDivert;
- ослабляет `OperationPolicy.CreateNoAdmittedEnginePolicy()`;
- добавляет raw command/path/URL/arguments в IPC;
- разрешает пользователю управлять engine lease другого SID;
- решает production signing/Defender/WinDivert blockers.

До отдельного native admission production runtime остаётся fail-closed.

## Validation status

Код R30 интегрирован в `feature/engine-bootstrap-r15`.

Автоматический GitHub Actions run в текущей сессии не считается выполненным: изменения и временные validation PR были созданы через GitHub App connector, а GitHub не создал для них workflow runs. Два временных validation PR закрыты без merge.

Поэтому до фактического hosted/self-hosted evidence нельзя заявлять CI PASS для текущего R30 HEAD.

## Следующие проверки

Перед признанием R30 готовым нужны:

1. CI + CodeQL + Windows unit/security tests на текущем HEAD;
2. реальный self-hosted Windows UAC lab с двумя standard-user SID;
3. подтверждение owner SID immutability;
4. подтверждение двух разных service-owned per-SID profile roots и direct-access denial;
5. uninstall cleanup;
6. отдельный test global engine lease после появления разрешённого isolated native lab runtime.
