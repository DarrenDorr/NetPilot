#!/usr/bin/env bash
set -euo pipefail

manifest_verifier='src/NetPilot.Updater/ByeDpiPackageManifestVerifier.cs'
windows_verifier='src/NetPilot.Platform.Windows/WindowsByeDpiProtectedStagingSecurityVerifier.cs'
core_tests='tests/NetPilot.Core.Tests/ByeDpiPackageManifestVerifierTests.cs'
lab='tools/NetPilot.ByeDpiR42_1StagingLab/Program.cs'
contract='security/native-admission/byedpi-package-verification-r42-1.yaml'
service='src/NetPilot.Service/Program.cs'
worker='src/NetPilot.Service/PrivilegedIpcWorker.cs'
installer='installer/NetPilot.iss'

for file in "$manifest_verifier" "$windows_verifier" "$core_tests" "$lab" "$contract" "$service" "$worker" "$installer"; do
  test -f "$file"
done

# Inherit the already validated R42 -> R41.1 production fail-closed boundary.
bash scripts/security/r42-byedpi-package-lifecycle-check.sh >/tmp/netpilot-r42-1-parent.txt
for marker in \
  'R42_PARENT_FAIL_CLOSED_GUARD=PASS' \
  'R42_PRODUCT_WIRING_ABSENT=PASS' \
  'R42_NATIVE_EXECUTION_TESTED=False' \
  'R42_PRODUCTION_ADMISSION=BLOCK' \
  'RESULT=R42_PACKAGE_LIFECYCLE_DESIGN_PASS_BLOCK'; do
  grep -Fq "$marker" /tmp/netpilot-r42-1-parent.txt
done
rm -f /tmp/netpilot-r42-1-parent.txt

echo 'R42_1_PARENT_FAIL_CLOSED_GUARD=PASS'

# Exact manifest identity is fixed and not generic/user-selected.
grep -Fq 'RequiredEngineId = "byedpi"' "$manifest_verifier"
grep -Fq 'RequiredArchitecture = "x64"' "$manifest_verifier"
grep -Fq 'RequiredExecutableRelativePath = "bin/ciadpi.exe"' "$manifest_verifier"
grep -Fq 'RequiredLicenseRelativePath = "LICENSE"' "$manifest_verifier"
grep -Fq 'MaxManifestFiles = 64' "$manifest_verifier"
grep -Fq 'SHA256.HashDataAsync' "$manifest_verifier"
grep -Fq 'ByeDpiManifestFileSetMismatch' "$manifest_verifier"
grep -Fq 'ByeDpiManifestHashMismatch' "$manifest_verifier"
grep -Fq 'ByeDpiStagingReparsePoint' "$manifest_verifier"

# Production verifier must remain read-only: mutation belongs only to future isolated transaction gates.
for forbidden in \
  'Directory.CreateDirectory' \
  'Directory.Delete' \
  'File.Delete' \
  'File.Write' \
  'FileMode.Create' \
  'FileSystemAclExtensions.SetAccessControl' \
  'Process.Start' \
  'ProcessStartInfo' \
  'HttpClient' \
  'WebClient'; do
  if grep -Fq "$forbidden" "$manifest_verifier" "$windows_verifier"; then
    echo "R42.1 violation: product verifier contains mutation/network/process primitive: $forbidden" >&2
    exit 1
  fi
done

echo 'R42_1_PRODUCT_VERIFIERS_READ_ONLY=PASS'

# Windows protected staging root must be fixed and ACL verification read-only.
grep -Fq '"NetPilot", "Engines", "Staging", "ByeDPI"' "$windows_verifier"
grep -Fq 'TransactionIdLength = 32' "$windows_verifier"
grep -Fq 'WindowsProtectedEngineSecurityVerifier.EvaluateSecurityDescriptor' "$windows_verifier"
grep -Fq 'ByeDpiStagingReparsePointRejected' "$windows_verifier"
grep -Fq 'UntrustedMutationAcl' 'src/NetPilot.Platform.Windows/WindowsProtectedEngineSecurity.cs'

echo 'R42_1_WINDOWS_PROTECTED_STAGING_BOUNDARY=PASS'

# Core negative tests must cover exact-tree and path ambiguity cases.
for test_name in \
  'Exact_synthetic_tree_matches_manifest' \
  'Extra_unmanifested_file_is_rejected' \
  'Missing_manifest_file_is_rejected' \
  'Hash_mismatch_is_rejected' \
  'Size_mismatch_is_rejected_before_hash_acceptance' \
  'Manifest_rejects_ambiguous_or_Windows_unsafe_paths' \
  'Manifest_rejects_case_collisions' \
  'Manifest_version_is_exact_three_part_numeric_identity'; do
  grep -Fq "$test_name" "$core_tests"
done

echo 'R42_1_CORE_NEGATIVE_TEST_SURFACE=PASS'

# Hosted lab must refuse a pre-existing product tree and use synthetic bytes only.
grep -Fq 'refuses to modify a pre-existing Program Files\\NetPilot tree' "$lab"
grep -Fq 'synthetic non-PE ByeDPI package fixture' "$lab"
grep -Fq 'R42_1_USER_WRITABLE_ACL_REJECTED=PASS' "$lab"
grep -Fq 'R42_1_NATIVE_EXECUTION_TESTED=False' "$lab"

echo 'R42_1_SYNTHETIC_LAB_BOUNDARY=PASS'

# Contract and production wiring remain closed.
grep -Fq 'production_package_mutator: NOT_IMPLEMENTED' "$contract"
grep -Fq 'production_network_acquisition: NOT_IMPLEMENTED' "$contract"
grep -Fq 'production_service_or_ipc_wiring: NOT_IMPLEMENTED' "$contract"
grep -Fq 'native_execution: NOT_IMPLEMENTED' "$contract"
grep -Fq 'production_admission: BLOCK' "$contract"

if grep -Eqi 'ByeDpiPackageManifestVerifier|WindowsByeDpiProtectedStagingSecurityVerifier' "$service" "$worker"; then
  echo 'R42.1 violation: package verification was wired into production Service/IPC.' >&2
  exit 1
fi
if grep -Eqi 'ByeDPI|ciadpi' "$installer"; then
  echo 'R42.1 violation: production installer references ByeDPI while admission is BLOCK.' >&2
  exit 1
fi

echo 'R42_1_SERVICE_IPC_WIRING=ABSENT'
echo 'R42_1_PRODUCTION_PACKAGE_MUTATOR=ABSENT'
echo 'R42_1_PRODUCTION_NETWORK_FETCHER=ABSENT'
echo 'R42_1_NATIVE_EXECUTION_TESTED=False'
echo 'R42_1_PRODUCTION_ADMISSION=BLOCK'
echo 'RESULT=R42_1_EXACT_PACKAGE_VERIFICATION_PASS_BLOCK'
