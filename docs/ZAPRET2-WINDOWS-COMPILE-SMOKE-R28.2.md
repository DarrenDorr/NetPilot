# R28.2 — Windows x64 compile-smoke zapret2

Дата: 28.10.2026.

Статус: **PASS FOR COMPILE-ONLY EVIDENCE / NOT ADMITTED / NATIVE EXECUTION DISABLED**.

## Что проверено

Workflow `.github/workflows/native-candidate-windows-compile-smoke.yml` собрал Windows x64 `winws2.exe` из строго зафиксированной цепочки:

- upstream release base: `v1.0.6`, commit `221d7dff24bfec0cd5ac0a5070b8acef2008af3d`;
- upstream UAF fix: `4a33055448ce5f4e7acc704e1fbe0d8a8a6aa08d`;
- LuaJIT: `openresty/luajit2@dcc9c9ee67e1a5d3d636bd7745e95ddb4a1c70bc`;
- NetPilot patches: `0001`, `0002`, `0003` из `security/native-admission/patches/zapret2-r28/`;
- runner: `windows-2025`;
- Cygwin setup: `2.940`, SHA-512 `fc46e3ead0312ea632251c16334b23caab31c9b38db30db4e5418b43ffeec5c003cc867b4166c822ad107660e2ce6e3d855499d08e695dd13010f43a396`;
- установленный Cygwin runtime в этом run: `3.6.12-1`;
- GCC: `14.4.0-1`;
- make: `4.4.1-2`.

Сборка использовала hardening linker flags `-static`, `--nxcompat`, `--dynamicbase`, `--high-entropy-va`, `-pie`, `--gc-sections`, `--build-id=none`.

## Результат

Workflow run: `40718483443`.

Job: `122922566162`.

Head SHA: `66d5cd72370e1283e5fbaf59e7cced9baf0003c4`.

Artifact: `12829318738`, `zapret2-r28-windows-x64-compile-smoke-evidence`.

Artifact ZIP SHA-256: `63a445d03202d6b2e548c1dd8f7d9791863d7decaa74338cd8e8084ed5ecbc92`.

Собранный `winws2.exe`:

- размер: `650240` байт;
- SHA-256: `869d5e80ec1b5b263b27485ef7c943066becf58e2b69bb463a43bfd064edb28d`;
- Authenticode: `NotSigned`;
- FileVersion/ProductVersion: отсутствуют в PE metadata этой controlled build.

`cygwin1.dll`, использованный build environment:

- размер: `3246645` байт;
- SHA-256: `ed9d5379061e5de79c3375cc62246c2142ea1c97d94f4d16bfbea6fb1b7fc889`.

Текстовая копия evidence сохранена в `security/native-admission/evidence/zapret2-r28/windows-x64-compile-smoke.txt`, исходные checksum артефакта — в `security/native-admission/evidence/zapret2-r28/SHA256SUMS.txt`.

## Что намеренно НЕ происходило

- `winws2.exe` не запускался;
- WinDivert DLL/SYS не входили в этот compile-smoke;
- WinDivert driver не устанавливался;
- network interception не включался;
- системные сетевые правила не менялись;
- binary после инвентаризации был удалён с runner;
- binary не сохранён в репозитории и не публикуется пользователям;
- `EngineInstall` и `EngineStart` в NetPilot остаются закрытыми.

## Значение результата

PASS доказывает только следующее: exact post-UAF-fix source + NetPilot patches `0001–0003` компилируются в Windows x64 `winws2.exe` в зафиксированной CI-среде.

PASS **не доказывает** безопасность runtime, корректность resource limits под реальной нагрузкой, безопасность WinDivert boundary, отсутствие parser bugs или пригодность для production.

Поэтому admission status остаётся `REVIEW`, release gate — `BLOCK`, `may_execute: false`, `may_integrate: false`.

## Следующий gate

До ADMIT требуются как минимум:

1. sanitizer/static-analysis и parser/fuzz regression evidence;
2. измерение conntrack/delayed-queue limits на browser/YouTube/Discord/combined/burst/hostile workloads;
3. точный WinDivert DLL/SYS inventory и review подписей/хешей;
4. controlled runtime lab: start/stop/crash/cleanup, HTTPS, Discord TCP/UDP, повторный запуск;
5. повторный advisory/CVE/KEV review всего exact component set;
6. отдельное HIGH_ASSURANCE решение перед production activation.
