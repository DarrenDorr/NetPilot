# R28.2 — Windows dependency intake для zapret2 candidate

Дата: 28.10.2026.

Статус: **DEPENDENCY INTAKE PASS / NOT ADMITTED / ENGINE EXECUTION DISABLED**.

## Зачем отдельный gate

Upstream Windows build zapret2 использует Cygwin, LuaJIT и WinDivert. Оригинальный workflow полезен как reference, но для NetPilot недостаточно строг: часть GitHub Actions закреплена floating tags, LuaJIT и WinDivert скачиваются без NetPilot expected-hash gate, а upstream package включает утилиты, которые нашему минимальному runtime не нужны.

Поэтому Windows build разделён минимум на два этапа:

1. dependency intake — проверить источники и зафиксировать identities без сборки/запуска engine;
2. controlled build — только после review intake evidence собрать минимальный x64 component set и провести inventory.

## Успешный intake

Workflow `Native Candidate Windows Dependency Intake`, run `40713163192`, завершился `success`.

Зафиксированы:

- zapret2 commit `4a33055448ce5f4e7acc704e1fbe0d8a8a6aa08d`, tree `5c2a77786cc7089145abe2ca99bcc53a8ed7de15`;
- LuaJIT commit `dcc9c9ee67e1a5d3d636bd7745e95ddb4a1c70bc`, tree `67f2346f2e4a72f7cafd3c872d70bd89fb27bcd4`;
- Cygwin `3.6.12-1`;
- `gcc-core 14.4.0-1`;
- `make 4.4.1-2`;
- `zlib-devel 1.3.2-1`;
- `cygwin1.dll`: SHA-256 `ed9d5379061e5de79c3375cc62246c2142ea1c97d94f4d16bfbea6fb1b7fc889`, размер `3246645` bytes;
- WinDivert `2.2.2-A` archive: SHA-256 `63cb41763bb4b20f600b6de04e991a9c2be73279e317d4d82f237b150c5f3f15`, размер `405137` bytes.

WinDivert в intake не распаковывался, driver не устанавливался и не загружался. zapret2/LuaJIT не исполнялись.

Evidence: `security/native-admission/evidence/zapret2-r28.2-windows-dependency-intake.yaml`.

## Workflow

`.github/workflows/native-candidate-windows-dependency-intake.yml`

Свойства:

- runner: `windows-2025`;
- permissions: `contents: read`;
- checkout action закреплён full SHA;
- credentials после checkout не сохраняются;
- zapret2 source закреплён на `4a33055448ce5f4e7acc704e1fbe0d8a8a6aa08d`;
- проверяется parent exact v1.0.6 `221d7dff24bfec0cd5ac0a5070b8acef2008af3d`;
- LuaJIT закреплён exact commit `dcc9c9ee67e1a5d3d636bd7745e95ddb4a1c70bc`, соответствующий tag `v2.1-20250826`;
- Cygwin install action закреплён commit `006ad0b0946ca6d0a3ea2d4437677fa767392401`;
- Cygwin mirror принудительно HTTPS: `https://mirrors.kernel.org/sourceware/cygwin/`;
- Cygwin repository metadata signature check включён;
- устанавливаются только запрошенные build dependencies `gcc-core`, `make`, `zlib-devel` плюс их Cygwin dependencies;
- после установки фиксируются фактические версии пакетов и SHA-256 `cygwin1.dll`;
- WinDivert 2.2.2-A скачивается только по HTTPS и до любой распаковки сверяется с expected SHA-256;
- WinDivert archive после evidence удаляется;
- драйвер не извлекается, не устанавливается и не загружается;
- zapret2 не компилируется и не запускается;
- LuaJIT не компилируется и не запускается;
- в artifact попадает только текстовый evidence.

## Отдельный trust gap: Cygwin setup.exe

В успешном intake обнаружено, что `cygwin/cygwin-install-action` сам скачивает `https://cygwin.com/setup-x86_64.exe` и затем запускает его. HTTPS защищает transport, а сам setup затем проверяет подпись repository metadata и SHA-512 пакетов, однако action не делает независимую NetPilot-проверку подписи/hash самого `setup-x86_64.exe` **до его исполнения**.

Для release-grade controlled build это недостаточно.

На 28.10.2026 официальный Cygwin setup — `2.940`. NetPilot фиксирует:

- filename: `setup-2.940.x86_64.exe`;
- SHA-512: `fc46e3ead0312ea632251c16334b23caab31c9b38db30db4e5418b43ffeec5c003cc867b4166c8222a622ad107660e2ce6e3d855499d08e695dd13010f43a396`;
- ожидаемый Cygwin signing-key fingerprint: `56405CF6FCC81574682A5D561A698DE9E2E56300`.

Добавлен отдельный `.github/workflows/native-candidate-cygwin-setup-provenance.yml`, который скачивает exact setup + `.sig` + Cygwin public key, проверяет hardcoded SHA-512, ожидаемый fingerprint и GPG signature, затем удаляет EXE **без запуска**. Только после PASS этого provenance-gate exact setup identity может использоваться как вход в будущий release-grade bootstrap.

Даже это ещё не делает Cygwin toolchain полностью воспроизводимым: необходимо также заморозить/аттестовать exact package artifacts или иной immutable package snapshot.

## Почему Cygwin пока не считается fully immutable toolchain

Подпись repository metadata защищает список пакетов, их размеры и SHA-512, но dependency intake всё ещё получает актуальные версии пакетов из signed Cygwin repository. Это не означает воспроизводимый build toolchain.

Перед release-grade build необходимо:

1. использовать только предварительно проверенный exact Cygwin setup identity;
2. записать exact package versions;
3. получить повторяемый immutable snapshot exact package artifacts либо эквивалентный attestable build input;
4. закрепить hashes такого snapshot;
5. только после этого использовать его для candidate release build.

Если такой snapshot нельзя получить с достаточной provenance, Windows candidate остаётся `REVIEW/BLOCK`, даже если compile-smoke технически успешен.

## WinDivert

Для intake используется официальный `WinDivert-2.2.2-A.zip` с expected SHA-256:

`63cb41763bb4b20f600b6de04e991a9c2be73279e317d4d82f237b150c5f3f15`.

Это только identity gate. Сам WinDivert DLL/SYS получит отдельный inventory после controlled extraction. Driver signature и exact bytes должны быть проверены отдельно. Никакая антивирусная блокировка не обходится: если Windows Defender/runner блокирует официальный archive, workflow должен fail closed и это становится отдельным review finding.

## Минимальный будущий Windows runtime

Первый candidate build не должен включать upstream utility set целиком. Предварительно допустимый inventory ограничивается необходимыми компонентами:

- `winws2.exe`;
- `cygwin1.dll`, если текущая Windows build architecture без него невозможна;
- `WinDivert.dll`;
- `WinDivert64.sys`.

Не включать без отдельного обоснования/admission:

- `ip2net.exe`;
- `mdig.exe`;
- `killall.exe`;
- x86 binaries на x64-only Windows target;
- BAT/PowerShell/upstream launcher scripts.

## Что этот gate не доказывает

Успешный intake не означает:

- `ADMIT`;
- безопасность Cygwin toolchain;
- безопасность WinDivert;
- reproducible build;
- корректность patch 0001–0003 на Windows;
- runtime correctness;
- разрешение `EngineInstall` или `EngineStart`.

Следующий gate — Windows x64 compile-smoke без выполнения candidate binary, затем отдельный WinDivert DLL/SYS inventory без загрузки driver.
