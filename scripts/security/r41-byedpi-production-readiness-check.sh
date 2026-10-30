#!/usr/bin/env bash
set -euo pipefail

matrix='security/native-admission/byedpi-production-readiness-r41.yaml'
candidate='security/native-admission/byedpi-v0.17.3-fork-candidate.yaml'
service='src/NetPilot.Service/Program.cs'
argv_catalog='src/NetPilot.Engine.Abstractions/EngineArgumentPlanContracts.cs'
identity_catalog='src/NetPilot.Engine.Abstractions/EngineExecutableIdentityContracts.cs'
layout_catalog='src/NetPilot.Engine.Abstractions/EnginePackageLayoutContracts.cs'
installer='installer/NetPilot.iss'

for file in \
  "$matrix" \
  "$candidate" \
  "$service" \
  "$argv_catalog" \
  "$identity_catalog" \
  "$layout_catalog" \
  "$installer" \
  docs/BYEDPI-SERVICE-OWNED-FUNCTIONAL-R34.md \
  docs/BYEDPI-SERVICE-INTEGRATION-R35.md \
  docs/BYEDPI-LOCALSYSTEM-SERVICE-LAB-R36.md \
  docs/BYEDPI-CRASH-CONTAINMENT-R37.md \
  docs/BYEDPI-MULTIUSER-LEASE-R38.md \
  docs/BYEDPI-RESTART-RECONCILIATION-R39.md \
  docs/BYEDPI-LIFECYCLE-SOAK-R40.md \
  security/native-admission/evidence/byedpi-r40/lifecycle-soak.txt; do
  test -f "$file"
done

# R41 is review-only: the machine-readable decision must remain fail-closed.
grep -Fq 'readiness: NOT_READY' "$matrix"
grep -Fq 'production_admission: BLOCK' "$matrix"
grep -Fq 'may_execute_in_production: false' "$matrix"
grep -Fq 'may_integrate_in_production: false' "$matrix"
grep -Fq 'may_add_positive_catalog_entry: false' "$matrix"
grep -Fq 'production_service_must_remain_no_admit: true' "$matrix"
grep -Fq 'production_catalogs_must_remain_empty_during_r41: true' "$matrix"
grep -Fq 'candidate_status_must_remain_review: true' "$matrix"
grep -Fq 'candidate_release_gate_must_remain_block: true' "$matrix"
grep -Fq 'no_positive_admission_entry_in_r41: true' "$matrix"

# Candidate source policy itself must not be silently promoted by the readiness review.
grep -Fq 'admission_status: REVIEW' "$candidate"
grep -Fq 'release_gate: BLOCK' "$candidate"
grep -Fq 'may_execute: false' "$candidate"
grep -Fq 'may_integrate: false' "$candidate"

# Production Service remains physically unable to create an admitted ByeDPI process/plan.
grep -Fq 'NoAdmittedWindowsVerifiedEngineProcessLauncher.Instance' "$service"
grep -Fq 'NoAdmittedWindowsByeDpiRuntimePlanProvider.Instance' "$service"
if grep -Fq 'SystemZapretProcessLauncher()' "$service"; then
  echo 'R41 violation: production Service received executable native launcher.' >&2
  exit 1
fi

# Positive production identity/layout/argv catalogs stay empty during R41.
grep -Fq 'Array.AsReadOnly(Array.Empty<EngineArgumentPlanDeclaration>())' "$argv_catalog"
grep -Fq 'Array.AsReadOnly(Array.Empty<EngineExecutableIdentityDeclaration>())' "$identity_catalog"
grep -Fq 'Array.AsReadOnly(Array.Empty<EnginePackageLayoutDeclaration>())' "$layout_catalog"

# Production installer must not start shipping ByeDPI while readiness is BLOCK.
if grep -Eqi 'ByeDPI|ciadpi' "$installer"; then
  echo 'R41 violation: production installer references ByeDPI while production admission is BLOCK.' >&2
  exit 1
fi

# Matrix must retain every current critical/high blocker class.
for blocker in \
  windows-product-traffic-integration \
  udp-runtime-and-dns-semantics \
  connection-and-resource-budgets \
  hung-start-readiness-timeout \
  remaining-upstream-warning-and-reliability-review \
  production-package-provisioning \
  byedpi-update-and-rollback-channel \
  third-party-license-notices \
  netpilot-release-code-signing \
  product-ux-and-safe-controls \
  high-assurance-admission-review; do
  grep -Fq -- "- id: $blocker" "$matrix"
done

echo 'R41_BYEDPI_PRODUCTION_READINESS=BLOCK_CONFIRMED'
echo 'R41_PRODUCTION_SERVICE_NO_ADMIT=PASS'
echo 'R41_PRODUCTION_CATALOGS_EMPTY=PASS'
echo 'RESULT=R41_READINESS_REVIEW_PASS_BLOCK'
