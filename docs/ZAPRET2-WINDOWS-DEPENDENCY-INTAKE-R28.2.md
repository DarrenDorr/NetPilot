# R28.2 — Windows dependency intake для zapret2 candidate

Дата: 28.10.2026.

Статус: **IN PROGRESS / NOT ADMITTED / ENGINE EXECUTION DISABLED**.

## Зачем отдельный gate

Upstream Windows build zapret2 использует Cygwin, LuaJIT и WinDivert. Оригинальный workflow полезен как reference, но для NetPilot недостаточно строг: часть GitHub Actions закреплена floating tags, LuaJIT и WinDivert скачиваются без NetPilot expected-hash gate, а upstream package включает утилиты, которые нашему минимальному runtime не нужны.

Поэтому Windows build разделён минимум на два этапа:

1. dependency intake — проверить источники и зафиксировать identities без сборки/запуска engine;
2. controlled build — только после review intake evidence собрать минимальный x64 component set и провести inventory.

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
- Cygwin setup metadata signature check включён;
- устанавливаются только build dependencies `gcc-core`, `make`, `zlib-devel` плюс базовый runtime, необходимый самому Cygwin;
- после установки фиксируются фактические версии пакетов и SHA-256 `cygwin1.dll`;
- WinDivert 2.2.2-A скачивается только по HTTPS и до любой распаковки сверяется с expected SHA-256 `63cb41763bb4b20f600b6de04e991a9c2be73279e317d4d82f237b150c5f3f15`;
- WinDivert archive после evidence удаляется;
- драйвер не извлекается, не устанавливается и не загружается;
- zapret2 не компилируется и не запускается;
- LuaJIT не компилируется и не запускается;
- в artifact попадает только текстовый evidence.

## Почему Cygwin пока не считается fully immutable toolchain

Подпись `setup.ini` защищает доверие к metadata/repository, но dependency intake всё ещё получает актуальные версии пакетов из signed Cygwin repository. Это лучше upstream time-machine сценария с `check-sig: false`, но ещё не означает воспроизводимый build toolchain.

После первого успешного intake мы должны:

1. записать exact package versions;
2. определить способ повторяемого получения тех же package artifacts или создать проверенный build-toolchain snapshot;
3. закрепить SHA-256/runtime identity такого snapshot;
4. только после этого использовать его для candidate release build.

Если такой snapshot нельзя получить с достаточной provenance, Windows candidate остаётся `REVIEW/BLOCK`, даже если one-off сборка технически успешна.

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

Следующий gate после успешного intake — reviewed controlled Windows x64 build без выполнения candidate binary.
