#!/usr/bin/env bash
set -euo pipefail

maintenance='src/NetPilot.Platform.Windows/WindowsByeDpiPackageMaintenance.cs'
tests='tests/NetPilot.Platform.Windows.Tests/WindowsByeDpiPackageMaintenanceTests.cs'
contract='security/native-admission/byedpi-repair-uninstall-r42-4.yaml'
service='src/NetPilot.Service/Program.cs'
worker='src/NetPilot.Service/PrivilegedIpcWorker.cs'
installer='installer/NetPilot.iss'

for file in "$maintenance" "$tests" "$contract" "$service" "$worker" "$installer"; do
  test -f "$file"
done

bash scripts/security/r42-3-byedpi-atomic-promotion-check.sh >/tmp/netpilot-r42-4-parent.txt
for marker in \
  'R42_3_PARENT_R42_2_GUARD=PASS' \
  'R42_3_TYPED_SOURCE_REVALIDATION=PASS' \
  'R42_3_TARGET_FREE_ROLLBACK=PASS' \
  'R42_3_NO_PROCESS_OR_NETWORK_PRIMITIVES=PASS' \
  'R42_3_SERVICE_IPC_WIRING=ABSENT' \
  'R42_3_NATIVE_EXECUTION_TESTED=False' \
  'R42_3_PRODUCTION_ADMISSION=BLOCK' \
  'RESULT=R42_3_ATOMIC_PROMOTION_PASS_BLOCK'; do
  grep -Fq "$marker" /tmp/netpilot-r42-4-parent.txt
done
rm -f /tmp/netpilot-r42-4-parent.txt

echo 'R42_4_PARENT_R42_3_GUARD=PASS'

# Public production boundary remains typed and LocalSystem-only.
grep -Fq 'public static Task<WindowsByeDpiRepairResult> RepairAsync(' "$maintenance"
grep -Fq 'WindowsByeDpiVerifiedPromotionSource source' "$maintenance"
grep -Fq 'public static Task<WindowsByeDpiUninstallResult> UninstallAsync(' "$maintenance"
grep -Fq 'WindowsByeDpiPromotionSourceVerifier.EnsureWindowsAndLocalSystem();' "$maintenance"
grep -Fq 'source.ProductionProtected' "$maintenance"
grep -Fq 'ByeDpiPackageLifecycleOperation.Repair' "$maintenance"
grep -Fq 'ByeDpiPackageLifecycleOperation.Uninstall' "$maintenance"

echo 'R42_4_TYPED_LOCALSYSTEM_BOUNDARY=PASS'

# Repair must be exact-current/canonical only and use an external protected backup.
grep -Fq 'canonicalManifest.CanonicalManifestSha256' "$maintenance"
grep -Fq 'source.ManifestSha256' "$maintenance"
grep -Fq 'Directory.Move(currentRoot, backupRoot)' "$maintenance"
grep -Fq 'Directory.Move(source.StagingRoot, currentRoot)' "$maintenance"
grep -Fq 'RepairBackups' "$maintenance"
grep -Fq 'VerifyExactManagedPackageAsync' "$maintenance"

echo 'R42_4_EXACT_REPAIR_BOUNDARY=PASS'

# Uninstall is target-free and atomically moves the fixed managed root to a service-owned tombstone.
grep -Fq 'Directory.Move(managed, tombstoneRoot)' "$maintenance"
grep -Fq 'UninstallTombstones' "$maintenance"
grep -Fq 'TargetVersion: null' "$maintenance"
if grep -E 'UninstallAsync\([^)]*(string|path|target)' "$maintenance" >/dev/null; then
  echo 'R42.4 violation: uninstall public API gained raw target/path input.' >&2
  exit 1
fi

echo 'R42_4_TARGET_FREE_UNINSTALL=PASS'

# Crash recovery must cover all mutation boundaries.
for marker in \
  'AfterRepairCurrentMove' \
  'AfterRepairReplacementMove' \
  'AfterUninstallManagedMove' \
  'RecoverRepairAsync' \
  'RecoverUninstall'; do
  grep -Fq "$marker" "$maintenance"
done
for test_name in \
  'RecoveryRestoresOriginalPackageAfterCrashFollowingCurrentMove' \
  'RecoveryCompletesValidRepairAfterReplacementMove' \
  'RecoveryCompletesUninstallAfterManagedRootWasAtomicallyMoved'; do
  grep -Fq "$test_name" "$tests"
done

echo 'R42_4_CRASH_RECOVERY_SURFACE=PASS'

# Test-only fault injection must never be public.
grep -Fq 'internal enum WindowsByeDpiMaintenanceTestFaultPoint' "$maintenance"
if grep -Fq 'public enum WindowsByeDpiMaintenanceTestFaultPoint' "$maintenance"; then
  echo 'R42.4 violation: synthetic fault seam became public.' >&2
  exit 1
fi

echo 'R42_4_TEST_FAULT_SEAM_INTERNAL=PASS'

# New production boundary may mutate only fixed package-maintenance trees; no execution/network primitives.
for forbidden in \
  'Process.Start' \
  'ProcessStartInfo' \
  'HttpClient' \
  'WebClient' \
  'Invoke-WebRequest' \
  'WinDivert' \
  'cmd.exe' \
  'powershell.exe'; do
  if grep -Fq "$forbidden" "$maintenance"; then
    echo "R42.4 violation: maintenance gained forbidden primitive: $forbidden" >&2
    exit 1
  fi
done

echo 'R42_4_NO_PROCESS_OR_NETWORK_PRIMITIVES=PASS'

# Product wiring remains absent while production admission is blocked.
if grep -Fq 'WindowsByeDpiPackageMaintenance' "$service" "$worker"; then
  echo 'R42.4 violation: maintenance was wired into production Service/IPC.' >&2
  exit 1
fi
if grep -Eqi 'ByeDPI|ciadpi' "$installer"; then
  echo 'R42.4 violation: production installer references ByeDPI while admission is BLOCK.' >&2
  exit 1
fi

grep -Fq 'service_or_ipc_wiring: NOT_IMPLEMENTED' "$contract"
grep -Fq 'runtime_network_acquisition: NOT_IMPLEMENTED' "$contract"
grep -Fq 'native_execution: NOT_IMPLEMENTED' "$contract"
grep -Fq 'production_admission: BLOCK' "$contract"

echo 'R42_4_SERVICE_IPC_WIRING=ABSENT'
echo 'R42_4_NATIVE_EXECUTION_TESTED=False'
echo 'R42_4_PRODUCTION_ADMISSION=BLOCK'
echo 'RESULT=R42_4_REPAIR_UNINSTALL_PASS_BLOCK'
