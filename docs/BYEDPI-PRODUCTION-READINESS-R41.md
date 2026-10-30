# R41 — ByeDPI production-admission readiness review

## Decision

**NOT READY / PRODUCTION ADMISSION BLOCK**

R34–R40 materially increased confidence in the exact Windows x64 ByeDPI candidate, but they proved a **lab-only loopback explicit-proxy lifecycle**, not a complete production product/release path.

R41 intentionally does not create a positive production identity, package layout, argument plan, launcher, updater entry or installer payload.

Machine-readable decision:
`security/native-admission/byedpi-production-readiness-r41.yaml`

## Exact candidate reviewed

- upstream: `hufrea/byedpi`;
- release: `v0.17.3`;
- commit: `7efde1b1296eaaa187b70e951894dde17527489c`;
- curated NetPilot patchset: `0001-0007`;
- exact Windows x64 SHA-256: `866e99c3c7690326cc6e365fe02980f06ae2b394c045555674fb95db52b0dbdd`;
- upstream license recorded by candidate metadata: MIT;
- public release recheck on 2026-10-30 still identified `v0.17.3` as the latest published release.

The candidate remains:

```text
admission_status: REVIEW
release_gate: BLOCK
may_execute: false
may_integrate: false
```

## What is now proven

### Source and binary identity

PASS, scoped to the exact candidate:

- release commit/tree pinned;
- seven curated patches pinned by content identity;
- Linux/Windows compile evidence;
- Windows build reproducibility within the controlled build run;
- exact Windows binary hash pinned;
- targeted KAVL/Windows handle/aux-socket hardening evidence.

Remaining broad upstream warning/reliability review is kept separate from these scoped passes.

### Native input boundary

PASS for the current NetPilot path:

- typed argument plan only;
- no generic string-value token;
- no raw CLI/script/path/URL/env input from UI/IPC;
- fixed loopback address and typed numeric values in the reviewed lab plan.

### Service-owned lifecycle

PASS across R34–R40:

- R34: exact service-owned functional SOCKS5 -> local HTTP cycles;
- R35: authenticated IPC v4 and `(SID + engine resource)` global lease;
- R36: installed LocalSystem lab-service path;
- R37: Windows Job Object crash containment;
- R38: real second-SID non-owner denial;
- R39: service crash/restart ownership reconciliation;
- R40: 8 graceful cycles + 4 crash/restart cycles + 4 SID handoffs without observed state drift.

These passes support a narrow statement: the exact candidate can be supervised safely inside the reviewed **lab-only Windows loopback explicit-proxy boundary**.

## Why production is still BLOCK

### P0 — Windows traffic integration is not admitted

ByeDPI's direct Windows capability is explicit proxy, not transparent packet interception. NetPilot currently has no separately admitted Windows traffic-routing layer that would make ordinary desktop applications use this proxy safely.

A ByeDPI admission must not smuggle in any of the following as an implicit implementation detail:

- system proxy mutation;
- transparent routing;
- WinDivert;
- firewall/network-stack changes;
- application-specific proxy injection.

Each traffic-integration approach needs its own threat model and, where privileged/native, its own admission evidence.

### P0 — No production package lifecycle exists

The production installer does not ship ByeDPI. The normal LocalSystem service receives both a no-admit native launcher and a no-admit ByeDPI plan provider.

Before admission there must be one exact production distribution model, for example:

1. installer-bundled immutable candidate with no runtime network updater; or
2. separately admitted signed/pinned package updater.

Whichever model is chosen must define:

- manifest identity;
- protected staging/install transaction;
- exact hash verification;
- ACL/owner verification;
- rollback/downgrade behavior;
- update behavior when another SID owns the engine;
- recovery after interrupted update;
- removal/uninstall behavior.

Existing Zapret updater code does not satisfy this requirement for ByeDPI.

### P0 — Final high-assurance admission review is still required

The candidate policy explicitly requires advisory review and a HIGH_ASSURANCE human review before `ADMIT`.

That review must evaluate the **final production composition**, not the R34 lab composition. It therefore cannot occur until package/install, traffic integration, product UX, signing and remaining runtime/resource gates are complete.

### P1 — UDP/DNS production scope is not proven

The capability declaration advertises `ExplicitProxyTcp | ExplicitProxyUdp`, while R34–R40 functional runtime evidence exercised TCP SOCKS5 to local HTTP.

Before production, choose one:

- first admission is explicitly TCP-only and product capability/profile declarations are narrowed accordingly; or
- UDP and DNS behavior receive separate functional, malformed-input, cleanup, crash, ownership and resource evidence.

The current evidence must not be used to claim UDP readiness.

### P1 — Resource/concurrency budgets are not production policy

The R34 contract carries framework budget fields, but ByeDPI does not consume those placeholder values. Production limits for connection state, UDP state, memory, handles and concurrency are not yet measured/enforced.

Required next evidence:

- bounded concurrent connection stress;
- long-enough resource soak;
- handle/process/memory observation;
- explicit maximums or defensible external bounds;
- fail-closed behavior at saturation.

R40 is a lifecycle soak, not a resource-exhaustion soak.

### P1 — Hung readiness / timeout behavior needs a dedicated gate

R34–R40 prove successful starts/stops and abrupt host failure containment. They do not yet prove the production response to a child that:

- starts but never opens the expected listener;
- stays alive but is unhealthy;
- ignores graceful stop;
- exceeds a fixed readiness/stop timeout.

Production must have bounded readiness and stop deadlines with a typed fail-closed escalation path.

### P1 — Remaining upstream warning/reliability debt

The candidate review still records:

- unrelated broad numeric-conversion warning debt;
- selected reliability backports that are `REVIEW_NOT_APPLIED` or `REVIEW_OR_REIMPLEMENT_NOT_APPLIED`.

Each item must be classified. A backport must either be applied with regression evidence or rejected with an explicit high-assurance rationale. Release review must not simply suppress these warnings.

### P1 — Third-party notices are not release-ready

`docs/THIRD-PARTY-NOTICES.md` currently does not list ByeDPI because ByeDPI is not distributed yet. Any release containing the modified binary must include exact attribution and the applicable original MIT license/copyright notice.

There is also existing release-documentation drift that should be cleaned before native production distribution:

- `THIRD-PARTY-NOTICES.md` describes Inno Setup 7.1.0;
- `scripts/build-installer.ps1` currently pins Inno Setup 7.1.1;
- installer metadata points at a different repository path than the repository used for this review.

A single release manifest should become the source of truth for product version, build-tool version, third-party component identities, hashes and support URLs.

### P1 — NetPilot release code signing is not implemented

The build validates the Authenticode signature of the downloaded Inno Setup tool, but it does not sign NetPilot application/service/native/installer release outputs with a NetPilot release identity.

Before native production rollout define:

- code-signing certificate/key custody;
- CI signing boundary;
- verification after signing;
- timestamp policy;
- certificate rotation/revocation procedure;
- behavior when signatures are missing or invalid.

### P1 — Product UX is not wired for ByeDPI

The typed service IPC exists, but the production GUI does not yet expose a reviewed ByeDPI product contract.

Required UX behavior includes:

- explicit Start/Stop/status;
- no raw native tuning fields;
- ownership-safe messages when another SID owns the engine;
- recovery instructions after service/engine failure;
- clear distinction between engine status and actual service reachability;
- no claim that a local proxy automatically fixes desktop traffic until traffic integration exists.

### P2 — Production diagnostics/support contract is incomplete

Lab artifacts are evidence, not a user support system. A production native component needs bounded privacy-preserving diagnostics:

- stable failure reason codes;
- local log size/retention limits;
- no traffic payload logging;
- no credentials/secrets;
- no disclosure of another user's SID;
- support export with explicit redaction rules.

## Recommended first production scope, if review later resumes

The narrowest defensible target is:

**Windows x64 + loopback-only explicit proxy + TCP-only + fixed typed profile(s)**

Keep out of the first admission unless separately reviewed:

- UDP;
- DNS interception/proxying;
- system proxy mutation;
- transparent routing;
- WinDivert;
- arbitrary user native options;
- user-supplied native paths/files/payloads;
- automatic updates from upstream master/latest.

This scope is a review recommendation only. It is not admitted by R41.

## Work order after R41

### R41.1 — release metadata/licensing hygiene

Can be done without native execution:

- fix Inno version drift in third-party notices;
- establish canonical repository/support URLs;
- add a release-manifest source of truth;
- prepare ByeDPI attribution/license packaging design without adding the binary to production installer.

### R41.2 — production package lifecycle

Design and test immutable installation/update/rollback without opening runtime admission.

### R41.3 — runtime readiness/resource gate

Add:

- readiness timeout;
- stop timeout/escalation;
- connection/resource stress;
- TCP-only scope decision or separate UDP/DNS test plan.

### R41.4 — Windows traffic integration + UX contract

Select how end-user applications will actually use the explicit proxy. Keep routing/system mutations separately admitted.

### R41.5 — release trust + diagnostics

Add NetPilot code-signing pipeline and privacy-preserving production support diagnostics.

### R41.6 — final exact-package high-assurance review

Only after all prior blockers are closed should a reviewer decide whether a positive production identity/layout/argument-plan admission entry is justified.

## Fail-closed R41 invariants

During R41:

- candidate `admission_status` stays `REVIEW`;
- candidate `release_gate` stays `BLOCK`;
- `may_execute` and `may_integrate` stay false;
- production service stays wired to no-admit ByeDPI launcher/provider;
- production identity/layout/argument-plan catalogs stay empty;
- production installer contains no ByeDPI/ciadpi payload;
- no positive production admission entry is created.

Static guard:
`scripts/security/r41-byedpi-production-readiness-check.sh`

Expected result:

```text
R41_BYEDPI_PRODUCTION_READINESS=BLOCK_CONFIRMED
R41_PRODUCTION_SERVICE_NO_ADMIT=PASS
R41_PRODUCTION_CATALOGS_EMPTY=PASS
RESULT=R41_READINESS_REVIEW_PASS_BLOCK
```
