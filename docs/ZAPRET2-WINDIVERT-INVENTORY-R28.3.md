# R28.3 — WinDivert 2.2.2-A x64 inventory

Дата: 28.10.2026.

Статус: **PASS FOR INVENTORY AND SIGNATURE OBSERVATION ONLY / NOT ADMITTED / DRIVER NOT LOADED / RELEASE GATE BLOCK**.

## Цель

Зафиксировать точный Windows x64 component set WinDivert, который потенциально потребуется будущему zapret2 backend, до любого запуска `winws2` и до загрузки kernel driver.

## Источник и контроль целостности

Проверен официальный архив WinDivert `2.2.2-A`.

Перед распаковкой workflow проверил SHA-256 архива:

`63cb41763bb4b20f600b6de04e991a9c2be73279e317d4d82f237b150c5f3f15`.

Workflow: `Native Candidate WinDivert Inventory`.

Run: `40726114320`.

Artifact:

- ID: `12832851711`;
- имя: `windivert-2.2.2-a-x64-inventory-evidence`;
- ZIP SHA-256: `c444ac9e5f17e0760606df17f983f6b1674d9fe42fbfe5c1755ce5aa85ce0ae8`.

## WinDivert.dll

- размер: `47616` байт;
- SHA-256: `c1e060ee19444a259b2162f8af0f3fe8c4428a1c6f694dce20de194ac8d7d9a2`;
- Authenticode: `NotSigned`.

Отсутствие Authenticode-подписи DLL не означает автоматически вредоносность, но означает, что NetPilot не может полагаться на publisher signature как на trust anchor. Для возможного production component set обязательны exact version identity и hash pinning.

## WinDivert64.sys

- размер: `94144` байт;
- SHA-256: `8da085332782708d8767bcace5327a6ec7283c17cfb85e40b03cd2323a90ddc2`;
- FileVersion: `2.2`;
- ProductVersion: `2.2`;
- CompanyName: `Basil`;
- ProductName: `WinDivert 2.2 driver`;
- Authenticode: `Valid`;
- signer thumbprint: `043589F75FCE2795E7F2CC3E526D46784D5DDAB3`;
- signer subject: `CN=成都密思听科技有限公司, O=成都密思听科技有限公司, S=四川省, C=CN, OID.2.5.4.15=Private Organization, OID.1.3.6.1.4.1.311.60.2.1.3=CN, SERIALNUMBER=91510107MA7E8Y2876`.

Валидная Authenticode-проверка означает только успешную проверку подписи Windows на протестированном runner. Она **не заменяет** отдельное человеческое решение о доверии signer chain и происхождению kernel driver.

## Что намеренно НЕ происходило

- `WinDivert.dll` не загружалась процессом NetPilot;
- `WinDivert64.sys` не устанавливался;
- драйвер не загружался;
- WinDivert service не создавался;
- сетевой трафик не перехватывался;
- firewall/routing state не менялся;
- binary inputs после инвентаризации удалялись с runner;
- в репозитории сохраняется только текстовое evidence.

Текстовая фиксация:

`security/native-admission/evidence/zapret2-r28/windivert-2.2.2-a-x64-inventory.txt`.

## Значение результата

PASS подтверждает exact inventory, SHA-256 и наблюдаемый Authenticode status двух x64 runtime компонентов официального архива WinDivert `2.2.2-A`.

PASS **не подтверждает** безопасность driver lifecycle, корректность unload/cleanup, отсутствие конфликтов с VPN/EDR/антивирусами, безопасность WinDivert filters или пригодность к production.

## Остающиеся gates

До возможного `ADMIT` необходимо:

1. отдельное человеческое принятие/отклонение signer chain kernel driver;
2. controlled driver lifecycle lab: install/load/open/close/unload/remove;
3. проверка crash cleanup и отсутствия оставшегося service/driver state;
4. exact allowlisted WinDivert filter generation без пользовательских raw filters;
5. coexistence tests с типичными VPN/security products;
6. повторный advisory/CVE/KEV review exact DLL/SYS component set;
7. HIGH_ASSURANCE review перед production activation.

Текущий статус кандидата не меняется: `REVIEW`, `release_gate: BLOCK`, `may_execute: false`, `may_integrate: false`.
