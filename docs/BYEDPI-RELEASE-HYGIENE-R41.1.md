# R41.1 — ByeDPI release metadata and licensing hygiene

## Status

**RELEASE HYGIENE IMPLEMENTED / PRODUCTION ADMISSION BLOCK**

Base: R41 production-readiness review remains `NOT_READY / BLOCK`.

R41.1 deliberately performs no native execution, does not add a positive ByeDPI catalog entry, does not ship `ciadpi.exe`, and does not change normal `NetPilot.Service` no-admit wiring.

## Problems closed in this scope

R41 identified concrete release-metadata drift:

- `installer/NetPilot.iss` pointed to `raybalado989-lang/NetPilot` instead of the repository used for current review;
- `installer/README.md` and `THIRD-PARTY-NOTICES.md` described Inno Setup 7.1.0 while the executable build script pins 7.1.1;
- there was no canonical development release manifest binding repository/support/update URLs and build-tool identity;
- `THIRD-PARTY-NOTICES.md` did not contain a dedicated ByeDPI attribution section;
- the exact upstream ByeDPI MIT notice was not preserved in a release-preparation path.

R41.1 fixes those hygiene gaps without altering native admission.

## Canonical release manifest

Added:

`release/release-manifest.yaml`

It records:

- canonical repository: `https://github.com/DarrenDorr/NetPilot`;
- canonical support and releases URLs;
- default alpha/package metadata;
- Inno Setup 7.1.1 immutable release URL;
- pinned Inno SHA-256 `8a5a6711c3766a6e5fb74f280ba0e3551e719ce94538d901fdbd1f8f9920b3f0`;
- expected Inno signer `Pyrsys B.V.`;
- exact reviewed ByeDPI v0.17.3 identity and Windows x64 lab hash;
- ByeDPI `production_distribution: not_shipped`;
- ByeDPI `production_admission: BLOCK`;
- no positive catalog/installer/updater/transparent-routing authorization.

The manifest is a development release-metadata source of truth, not an admission record and not yet a signed per-release provenance manifest.

## Installer metadata synchronization

`installer/NetPilot.iss` now uses:

`https://github.com/DarrenDorr/NetPilot`

for publisher/support/update URL derivation.

The only semantic installer change in R41.1 is that repository URL. The same edit also caused formatting-only diff for one blank line and final newline; service registration, enrollment, ACL, failure recovery and uninstall behavior are unchanged.

`installer/README.md` now documents the actual pinned Inno Setup 7.1.1 build tool.

## ByeDPI license preparation

Upstream ByeDPI `main/LICENSE` was rechecked and records:

- MIT License;
- `Copyright (c) 2024 hufrea`.

The exact notice is preserved as:

`third_party/licenses/ByeDPI-MIT.txt`

`docs/THIRD-PARTY-NOTICES.md` now includes an explicit ByeDPI section, but also states that R41.1 does **not** ship ByeDPI and does **not** grant production ADMIT.

If a future production package contains the modified ByeDPI binary, the applicable MIT notice must be included with that distributed component and bound to the exact release manifest/package evidence.

## Static guard

Added:

`scripts/security/r41-1-release-metadata-hygiene-check.sh`

It requires:

- the R41 fail-closed readiness guard still passes;
- repository/support/update metadata are canonical;
- installer no longer references the stale repository;
- Inno version/hash/tool path agree between manifest, build script and current docs;
- the preserved ByeDPI MIT notice matches the expected SHA-256;
- ByeDPI remains `not_shipped / BLOCK`;
- installer still contains no ByeDPI/ciadpi reference;
- no transparent routing or WinDivert authorization is introduced.

Expected markers:

```text
R41_1_RELEASE_METADATA_SYNC=PASS
R41_1_BYEDPI_LICENSE_NOTICE=PASS
R41_1_PRODUCTION_ADMISSION=BLOCK
R41_1_NATIVE_EXECUTION_TESTED=False
RESULT=R41_1_RELEASE_HYGIENE_PASS_BLOCK
```

## What R41.1 does not close

Production admission remains blocked by the substantive R41 gates, including:

- end-user Windows traffic integration model;
- TCP-only vs UDP/DNS production scope;
- connection/resource stress and enforceable budgets;
- hung-readiness and stop-timeout escalation;
- remaining upstream warning/reliability-backport disposition;
- immutable production ByeDPI package provisioning/update/rollback;
- NetPilot release code signing;
- production ByeDPI GUI/UX and privacy-preserving diagnostics;
- final exact-package HIGH_ASSURANCE review.

## Next gate

R41.2 / R42 should design the **production package lifecycle while keeping runtime admission closed**: immutable package identity/layout, source of package bytes, protected staging/install transaction, verification handoff, rollback/downgrade/repair/uninstall behavior, and update behavior while an engine lease is active.
