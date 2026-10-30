#!/usr/bin/env bash
set -euo pipefail

policy='src/NetPilot.Updater/ByeDpiPackageLifecyclePolicy.cs'
tests='tests/NetPilot.Core.Tests/ByeDpiPackageLifecyclePolicyTests.cs'
contract='security/native-admission/byedpi-package-lifecycle-r42.yaml'
service='src/NetPilot.Service/Program.cs'
worker='src/NetPilot.Service/PrivilegedIpcWorker.cs'
installer='installer/NetPilot.iss'
release_manifest='release/release-manifest.yaml'

for file in "$policy" "$tests" "$contract" "$service" "$worker" "$installer" "$release_manifest"; do
  test -f "$file"
done

# Reuse the already validated R41.1 -> R41 fail-closed production boundary.
bash scripts/security/r41-1-release-metadata-hygiene-check.sh >/tmp/netpilot-r42-parent-guard.txt
for marker in \
  'R41_1_RELEASE_METADATA_SYNC=PASS' \
  'R41_1_BYEDPI_LICENSE_NOTICE=PASS' \
  'R41_1_PRODUCTION_ADMISSION=BLOCK' \
  'RESULT=R41_1_RELEASE_HYGIENE_PASS_BLOCK'; do
  grep -Fq "$marker" /tmp/netpilot-r42-parent-guard.txt
done
rm -f /tmp/netpilot-r42-parent-guard.txt

echo 'R42_PARENT_FAIL_CLOSED_GUARD=PASS'

# New R42 boundary must remain pure/non-executing and path/URL-free.
grep -Fq 'public static class ByeDpiPackageLifecyclePolicy' "$policy"
grep -Fq 'BlockActiveEngineLease' "$policy"
grep -Fq 'BlockPackageNotVerified' "$policy"
grep -Fq 'BlockUnprotectedStaging' "$policy"
grep -Fq 'BlockCrossVolumeStaging' "$policy"
grep -Fq 'BlockUpgradeNotNewer' "$policy"
grep -Fq 'BlockRepairVersionMismatch' "$policy"
grep -Fq 'BlockRollbackTargetMismatch' "$policy"

for forbidden in \
  'Process.Start' \
  'ProcessStartInfo' \
  'HttpClient' \
  'WebClient' \
  'File.' \
  'Directory.' \
  'Path.' \
  'Uri' \
  'http://' \
  'https://' \
  'ciadpi.exe'; do
  if grep -Fq "$forbidden" "$policy"; then
    echo "R42 violation: lifecycle policy contains forbidden runtime/filesystem/network primitive: $forbidden" >&2
    exit 1
  fi
done

echo 'R42_PURE_POLICY_SURFACE=PASS'

# Tests must explicitly prove lease blocking and constrained transitions.
grep -Fq 'Any_mutation_is_blocked_while_engine_lease_is_active' "$tests"
grep -Fq 'Install_requires_verified_protected_same_volume_package' "$tests"
grep -Fq 'Upgrade_requires_strictly_newer_canonical_version' "$tests"
grep -Fq 'Repair_is_exact_version_only' "$tests"
grep -Fq 'Rollback_is_only_to_exact_retained_previous_version' "$tests"
grep -Fq 'Uninstall_requires_installed_package_and_rejects_target_payload' "$tests"

echo 'R42_REQUIRED_POLICY_TESTS=PASS'

# Design manifest remains explicitly non-admitted and non-wired.
grep -Fq 'package_mutation_runtime: NOT_IMPLEMENTED' "$contract"
grep -Fq 'package_network_acquisition: NOT_IMPLEMENTED' "$contract"
grep -Fq 'production_admission: BLOCK' "$contract"
grep -Fq 'filesystem_mutator: absent' "$contract"
grep -Fq 'network_fetcher: absent' "$contract"
grep -Fq 'service_registration: absent' "$contract"
grep -Fq 'ipc_operation: absent' "$contract"
grep -Fq 'native_process_launch: absent' "$contract"

# R42 policy must not be registered into product service/IPC yet.
if grep -Fq 'ByeDpiPackageLifecyclePolicy' "$service" "$worker"; then
  echo 'R42 violation: package lifecycle policy was wired into production service/IPC.' >&2
  exit 1
fi
if grep -Eqi 'ByeDPI|ciadpi' "$installer"; then
  echo 'R42 violation: production installer references ByeDPI while admission is BLOCK.' >&2
  exit 1
fi

echo 'R42_PRODUCT_WIRING_ABSENT=PASS'

# Existing release/admission boundary remains closed.
grep -Fq 'production_distribution: not_shipped' "$release_manifest"
grep -Fq 'production_admission: BLOCK' "$release_manifest"
grep -Fq 'byedpi_positive_catalog_entry: false' "$release_manifest"
grep -Fq 'byedpi_installer_payload: false' "$release_manifest"
grep -Fq 'byedpi_runtime_network_updater: false' "$release_manifest"

echo 'R42_POLICY_BOUNDARY=PASS'
echo 'R42_ACTIVE_LEASE_MUTATION_BLOCK=PASS'
echo 'R42_SERVICE_IPC_WIRING=ABSENT'
echo 'R42_FILESYSTEM_MUTATOR=ABSENT'
echo 'R42_NETWORK_FETCHER=ABSENT'
echo 'R42_NATIVE_EXECUTION_TESTED=False'
echo 'R42_PRODUCTION_ADMISSION=BLOCK'
echo 'RESULT=R42_PACKAGE_LIFECYCLE_DESIGN_PASS_BLOCK'
