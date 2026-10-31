# R42.9 — ByeDPI isolated bounded HTTP transport

Дата: 31.10.2026.

Статус: **ISOLATED TEST BOUNDARY ONLY / LIVE NETWORK NOT TESTED / PRODUCTION BLOCK**.

## Цель

R42.9 добавляет минимальный HTTP transport между уже существующими R42.7/R42.8 trust contracts и будущим downloader. На этом этапе transport существует только как отдельный компонент с injected `HttpMessageHandler` и проверяется fake/in-memory handler'ами.

R42.9 намеренно не подключён к UI, updater orchestration, filesystem, staging, Service/IPC, installer или native ByeDPI runtime.

## Родительские границы

- R42.7 immutable acquisition trust plan: checkpoint `cf6fbd7bf15325f8538553454a59d3e60b134565`.
- R42.8 exact-tag parser/response contract: checkpoint `3fa46206c19e6be32116bf4ff05d03fb95092665`.
- R42.9 не изменяет admission status ByeDPI и не создаёт production downloader.

## Metadata request

`ByeDpiIsolatedHttpTransport.FetchReleaseMetadataAsync()` принимает уже валидируемый immutable descriptor и строит только exact-tag endpoint:

`https://api.github.com/repos/DarrenDorr/NetPilot/releases/tags/<exact-tag>`

Запрос фиксирует:

- `Accept: application/vnd.github+json`;
- `X-GitHub-Api-Version: 2026-03-10`;
- `User-Agent: NetPilot-ByeDPI-Acquisition`;
- без `Authorization` и cookies.

Metadata response должен быть HTTP 200, без `Location`, с допустимым JSON content type. `Content-Length`, если присутствует, обязан быть положительным и не больше 256 KiB. Даже при отсутствии `Content-Length` фактический stream читается с hard cap 256 KiB, после чего передаётся в строгий `GithubByeDpiReleaseParser`.

## Asset request

Asset может быть запрошен только как точный элемент уже созданного R42.7 `ByeDpiAcquisitionPlan`. Transport повторно проверяет immutable plan constants, asset membership, exact GitHub asset API URI, размер и lowercase SHA-256.

Первый asset API response допускает только:

- HTTP 200 — прямой body;
- HTTP 302 — ровно один redirect на `https://release-assets.githubusercontent.com/...`.

Для 302 дополнительно запрещён `Content-Length`: redirect response не может выглядеть как asset payload.

## Redirect isolation

Automatic redirect не считается доверенным. Transport сравнивает URI исходного request с URI, зафиксированным в response; если handler скрыто выполнил redirect и вернул конечный response, transport fail-closed отклоняет его.

Разрешённый redirect выполняется отдельным request. На него не переносятся:

- GitHub API version header;
- GitHub API `Accept` header;
- `Authorization`;
- cookies.

Передаётся только фиксированный NetPilot User-Agent. Второй redirect запрещён.

## Streaming integrity

Финальный asset body читается только в память и ограничен immutable expected size. Во время чтения:

1. каждый chunk прибавляется к точному byte count;
2. overrun немедленно отклоняется;
3. SHA-256 обновляется через `IncrementalHash`;
4. EOF до expected size считается underrun;
5. конечный hash сравнивается с immutable SHA-256 через `CryptographicOperations.FixedTimeEquals`.

Таким образом отсутствие либо ложное значение `Content-Length` не позволяет обойти проверку фактической длины или hash.

## Timeout и cancellation

Transport использует один общий timeout на операцию. Default и максимальное значение — 20 секунд, как закреплено R42.7. Test-only/consumer override может только уменьшить этот лимит.

Internal timeout преобразуется в typed `UpdateSecurityException` с code `ByeDpiHttpTransportTimeout`. Внешний caller cancellation остаётся `OperationCanceledException` и не маскируется под timeout.

Retries/backoff в R42.9 отсутствуют.

## Что тестируется

Focused test matrix включает:

- exact-tag URI и pinned headers;
- отсутствие Authorization/cookies;
- direct 200 asset;
- один trusted 302 redirect;
- untrusted redirect;
- second redirect;
- 302 с Content-Length;
- wrong final Content-Length;
- stream overrun/underrun;
- exact-length hash mismatch;
- metadata stream >256 KiB без Content-Length;
- internal timeout;
- caller cancellation;
- hidden automatic redirect.

Все тесты используют только `StubHandler`/in-memory content. Live DNS/TLS/network I/O в R42.9 CI не выполняется.

## Запрещённые интеграции

В этом gate отсутствуют и остаются запрещены:

- `SocketsHttpHandler` / `HttpClientHandler` production construction;
- live network tests;
- filesystem write/temp/download path;
- staging/promote/rollback invocation;
- Service/IPC wiring;
- installer wiring;
- process/native ByeDPI execution;
- production catalog/admission change.

## Release effect

`HTTP_TRANSPORT=ISOLATED_TEST_BOUNDARY_ONLY`

`LIVE_NETWORK_TESTED=False`

`PRODUCT_DOWNLOADER=NOT_IMPLEMENTED`

`FILESYSTEM_WRITE=NOT_IMPLEMENTED`

`SERVICE_IPC_WIRING=ABSENT`

`NATIVE_EXECUTION=NOT_IMPLEMENTED`

`PRODUCTION_ADMISSION=BLOCK`

Следующий gate после успешного R42.9 — безопасная реальная acquisition/download boundary: exact metadata + asset retrieval с теми же immutable identities, protected temporary/staging lifecycle и без native execution до отдельного runtime gate.
