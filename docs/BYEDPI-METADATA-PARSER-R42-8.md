# R42.8 — ByeDPI exact-tag metadata parser + transport response contract

## Назначение

R42.8 добавляет следующий неисполняемый gate после R42.7:

1. bounded parser raw GitHub exact-tag release JSON в `ByeDpiAcquisitionReleaseMetadata`;
2. pure policy для формы будущих HTTP responses.

R42.8 не выполняет HTTP-запросы, не скачивает bytes, не пишет package на диск, не вызывает staging и не запускает native code.

Parent checkpoint: `cf6fbd7bf15325f8538553454a59d3e60b134565`.

## Parser boundary

`GithubByeDpiReleaseParser` принимает только bounded raw JSON bytes:

- максимум 256 KiB;
- JSON depth максимум 16;
- trailing commas запрещены;
- comments запрещены;
- массив assets — от 1 до 16 элементов;
- duplicate JSON properties запрещены во всём root и каждом asset object.

Unknown GitHub properties допускаются для совместимости с расширением REST schema, но они не участвуют в security decision. Duplicate-property rejection не позволяет неизвестному/известному полю переопределить уже прочитанное значение.

## Required release fields

Parser требует и типизирует:

- `id` — положительный Int64;
- `tag_name` — bounded non-empty string;
- `draft` — boolean;
- `prerelease` — boolean;
- `immutable` — boolean;
- `assets` — bounded array.

Parser не решает, доверять ли release state. После parsing обязательно применяется R42.7 `ByeDpiAcquisitionTrustPolicy`, которая требует exact tag, `draft=false`, `prerelease=false`, `immutable=true` и anti-downgrade.

## Required asset fields

Каждый asset обязан иметь:

- `id` — положительный Int64;
- `name` — bounded non-empty string;
- `state` — bounded non-empty string;
- `size` — положительный Int64;
- `digest` — только exact `sha256:<64 lowercase hex>`;
- `url` — absolute HTTPS URI с default port, без userinfo и fragment.

Parser преобразует GitHub `digest` в lowercase SHA-256 hex, после чего R42.7 policy повторно проверяет exact asset name, state `uploaded`, size, SHA-256, unique asset ID и exact `DarrenDorr/NetPilot` asset API URI.

## Parser → trust policy composition

Успешный parse сам по себе не означает trusted release.

Обязательная цепочка будущего acquisition слоя:

`raw JSON -> GithubByeDpiReleaseParser -> ByeDpiAcquisitionTrustPolicy.CreatePlan -> transport`

Таким образом structurally valid, но mutable/prerelease/wrong-tag/extra-asset/hash-drift metadata блокируется уже R42.7 policy.

## Pure transport response contract

`ByeDpiTransportResponsePolicy` принимает только typed response facts: status code, optional Content-Length и optional Location. Он не имеет network API.

### Exact-tag metadata response

- разрешён только HTTP `200`;
- redirect запрещён;
- `Location` при `200` запрещён;
- Content-Length, если присутствует, должен быть `> 0` и `<= 256 KiB`;
- отсутствие Content-Length разрешено только при будущем bounded streaming с жёстким byte cap.

### GitHub release asset API response

Допускаются только два варианта:

1. direct HTTP `200` — body идёт сразу;
2. HTTP `302` — ровно один redirect, Location проходит R42.7 validation для `release-assets.githubusercontent.com`.

Для direct `200` Content-Length, если присутствует, обязан точно совпадать с immutable descriptor size. Если header отсутствует, будущий transport обязан считать streamed bytes и завершить ровно на expected size.

### Redirected asset response

- только HTTP `200`;
- второй redirect запрещён;
- `Location` при `200` запрещён;
- Content-Length, если присутствует, обязан совпасть с expected size;
- отсутствие Content-Length требует exact streamed byte count.

## Что R42.8 намеренно НЕ делает

- не создаёт HTTP client/handler;
- не выполняет DNS/TLS/network I/O;
- не сохраняет metadata/assets;
- не создаёт temporary files;
- не вызывает R42.2 staging transaction;
- не добавляет Service/IPC operations;
- не меняет installer;
- не запускает `ciadpi.exe`;
- не создаёт positive production admission.

`HTTP_TRANSPORT=NOT_IMPLEMENTED`

`PRODUCT_DOWNLOADER=NOT_IMPLEMENTED`

`NETWORK_ACQUISITION=NOT_IMPLEMENTED`

`FILESYSTEM_WRITE=NOT_IMPLEMENTED`

`NATIVE_EXECUTION_TESTED=False`

`PRODUCTION_ADMISSION=BLOCK`

## Следующий gate

R42.9 — изолированный bounded HTTP transport с fake/in-memory handler tests, без product wiring и без native execution. Он должен доказать pinned GitHub API headers, disabled automatic redirects, total timeout/cancellation, metadata byte cap, exact asset stream length/hash verification и deterministic disposal/cleanup до появления реального product acquisition path.
