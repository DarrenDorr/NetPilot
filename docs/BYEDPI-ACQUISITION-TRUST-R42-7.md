# R42.7 — ByeDPI acquisition trust policy

## Назначение

R42.7 определяет fail-closed policy для будущего получения immutable ByeDPI package, не добавляя реальный downloader, HTTP transport, запись на диск, Service/IPC wiring или native execution.

Parent boundary: R42.6 immutable distribution descriptor.

## Что разрешено policy

Policy принимает только:

- уже проверенный `ByeDpiImmutableReleaseDescriptor` из R42.6;
- типизированные release metadata от будущего узкого parser boundary;
- необязательную текущую установленную canonical version для anti-downgrade проверки.

Результат — read-only `ByeDpiAcquisitionPlan` с exact tag endpoint, exact asset API endpoints, exact sizes/SHA-256 и фиксированными network/resource limits.

## Release discovery

Разрешён только exact tag endpoint:

`https://api.github.com/repos/DarrenDorr/NetPilot/releases/tags/byedpi-v0.17.3`

Mutable `releases/latest` запрещён. Caller-selected arbitrary URL или arbitrary version не являются частью API policy.

## Release state

Future parser обязан представить типизированные факты. Policy принимает релиз только если:

- `draft=false`;
- `prerelease=false`;
- `immutable=true`;
- tag полностью совпадает с R42.6 descriptor;
- release id положительный.

## Asset selection

Разрешены ровно три assets из immutable descriptor:

1. payload ZIP;
2. manifest JSON;
3. provenance JSON.

Для каждого asset должны одновременно совпасть:

- canonical name;
- exact byte size;
- exact lowercase SHA-256;
- положительный GitHub asset id;
- exact API URI `https://api.github.com/repos/DarrenDorr/NetPilot/releases/assets/<id>`.

Extra asset, missing asset, duplicate expected asset или любое identity drift блокируют acquisition plan.

## Redirect policy

Automatic redirects должны быть отключены в будущем transport.

Разрешён максимум один вручную проверенный redirect к:

`https://release-assets.githubusercontent.com/...`

Запрещены custom port, userinfo, fragment, другой origin и второй redirect hop. Signed query parameters GitHub storage разрешены.

## Resource limits

R42.7 фиксирует policy limits, которые обязан применять будущий transport:

- release metadata: максимум 256 KiB;
- network timeout: 20 seconds;
- redirects: максимум 1;
- суммарный объём трёх assets: максимум 64 MiB;
- фактический Content-Length/streamed byte count должен совпадать с exact descriptor size;
- SHA-256 каждого сохранённого asset должен быть проверен до передачи в staging boundary.

## Anti-downgrade

Installed version принимается только в canonical `major.minor.patch` формате.

- target ниже installed version — BLOCK;
- target равен installed version — разрешён как будущий repair acquisition;
- target выше installed version — разрешён как будущий upgrade acquisition.

Policy не предоставляет arbitrary version selector.

## Что R42.7 намеренно НЕ делает

R42.7 не реализует:

- ByeDPI GitHub JSON parser;
- `HttpClient` или другой network transport;
- скачивание bytes;
- запись package на filesystem;
- staging/promotion;
- Service/IPC operations;
- installer integration;
- запуск `ciadpi.exe`;
- positive production admission.

`PRODUCT_DOWNLOADER=NOT_IMPLEMENTED`

`NETWORK_ACQUISITION=NOT_IMPLEMENTED`

`NATIVE_EXECUTION_TESTED=False`

`PRODUCTION_ADMISSION=BLOCK`

## Следующий gate

R42.8 должен добавить узкий exact-tag metadata parser и transport contract/fixture validation. Даже на R42.8 native execution и production admission должны оставаться закрытыми. Реальный product downloader допускается только после отдельного review его origin, redirect, byte-count, hash-verification, temporary-file и cleanup semantics.
