#!/usr/bin/env bash
set -euo pipefail

promotion='src/NetPilot.Platform.Windows/WindowsByeDpiAtomicPackagePromotion.cs'
tests='tests/NetPilot.Platform.Windows.Tests/WindowsByeDpiAtomicPackagePromotionTests.cs'
contract='security/native-admission/byedpi-atomic-promotion-r42-3.yaml'
service='src/NetPilot.Service/Program.cs'
worker='src/NetPilot.Service/PrivilegedIpcWorker.cs'
installer='installer/NetPilot.iss'

for file in "$promotion" "$tests" "$contract" "$service" "$worker" "$installer"; do
  test -f "$file"
done

# R42.2 must remain fail-closed before R42.3 is considered.
bash scripts/security/r42-2-byedpi-staging-transaction-check.sh >/tmp/netpilot-r42-3-parent.txt
for marker in \
  'R42_2_PARENT_R42_1_GUARD=PASS' \
  'R42_2_TRANSACTION_BOUNDARY=PASS' \
  'R42_2_PATH_FREE_JOURNAL=PASS' \
  'R42_2_NO_PROCESS_OR_NETWORK_PRIMITIVES=PASS' \
  'R42_2_RECOVERY_BOUNDARY=PASS' \
  'R42_2_SERVICE_IPC_WIRING=ABSENT' \
  'R42_2_NATIVE_EXECUTION_TESTED=False' \
  'R42_2_PRODUCTION_ADMISSION=BLOCK' \
  'RESULT=R42_2_STAGING_TRANSACTION_PASS_BLOCK'; do
  grep -Fq "$marker" /tmp/netpilot-r42-3-parent.txt
done
rm -f /tmp/netpilot-r42-3-parent.txt

echo 'R42_3_PARENT_R42_2_GUARD=PASS'

# Typed source must be revalidated from fixed R42.2 evidence, not trusted by raw path.
grep -Fq 'WindowsByeDpiVerifiedPromotionSource' "$promotion"
grep -Fq 'ValidateVerifiedStagingJournal' "$promotion"
grep -Fq 'ByeDpiPackageManifestVerifier.VerifyAsync' "$promotion"
grep -Fq 'WindowsByeDpiProtectedStagingSecurityVerifier.Verify' "$promotion"
grep -Fq 'EnsureWindowsAndLocalSystem' "$promotion"
if grep -Eq 'public static .*PromoteAsync\([^)]*string' "$promotion"; then
  echo 'R42.3 violation: public promotion API accepts a raw string.' >&2
  exit 1
fi

echo 'R42_3_TYPED_PROMOTION_SOURCE=PASS'

# Atomic promotion is a same-volume directory rename into canonical immutable layout.
grep -Fq 'Directory.Move(source.StagingRoot, targetRoot)' "$promotion"
grep -Fq 'SameVolume(normalizedManagedBase, source.StagingRoot)' "$promotion"
grep -Fq '"Managed"' "$promotion"
grep -Fq 'ByeDpiPackageManifest.RequiredEngineId' "$promotion"
grep -Fq 'ByeDpiPackageManifest.RequiredArchitecture' "$promotion"
grep -Fq 'ByeDpiPromotionTargetAlreadyExists' "$promotion"
grep -Fq 'ByeDpiPackageLifecyclePolicy.Evaluate' "$promotion"

echo 'R42_3_ATOMIC_IMMUTABLE_PROMOTION=PASS'

# Moved package must be reverified before protected state can commit.
grep -Fq 'VerifyManagedPackageAsync' "$promotion"
grep -Fq 'WriteCanonicalManifest' "$promotion"
grep -Fq 'WriteState' "$promotion"
grep -Fq 'WindowsProtectedEngineSecurityVerifier.EvaluateSecurityDescriptor' "$promotion"
grep -Fq 'File.Replace(tempPath, finalPath' "$promotion"

echo 'R42_3_POST_MOVE_REVERIFY_AND_STATE=PASS'

# Rollback must be target-free and only use retained previous protected state.
grep -Fq 'public static Task<WindowsByeDpiPromotionResult> RollbackAsync(' "$promotion"
grep -Fq 'before.RetainedPreviousVersion' "$promotion"
grep -Fq 'new WindowsByeDpiManagedPackageState(' "$promotion"
if grep -Eq 'RollbackAsync\([^)]*string' "$promotion"; then
  echo 'R42.3 violation: rollback accepts caller-selected string target.' >&2
  exit 1
fi

echo 'R42_3_TARGET_FREE_ROLLBACK=PASS'

# Recovery journal must remain path-free and deterministic.
for forbidden_field in \
  'StagingRoot { get; set;' \
  'ManagedPackageRoot { get; set;' \
  'AbsolutePath { get; set;' \
  'DownloadUrl { get; set;' \
  'CommandLine { get; set;' \
  'ProcessId { get; set;'; do
  if grep -Fq "$forbidden_field" "$promotion"; then
    echo "R42.3 violation: recovery metadata gained unsafe field: $forbidden_field" >&2
    exit 1
  fi
done
grep -Fq 'PromotionJournalRecord' "$promotion"
grep -Fq 'RecoverIncompleteOperationsAsync' "$promotion"
grep -Fq 'EnsureStateIsPriorOrDesired' "$promotion"

echo 'R42_3_RECOVERY_BOUNDARY=PASS'

# No process/network/native execution primitives may enter R42.3 package lifecycle code.
for forbidden in \
  'Process.Start' \
  'ProcessStartInfo' \
  'HttpClient' \
  'WebClient' \
  'Invoke-WebRequest' \
  'WinDivert'; do
  if grep -Fq "$forbidden" "$promotion"; then
    echo "R42.3 violation: promotion gained forbidden primitive: $forbidden" >&2
    exit 1
  fi
done

echo 'R42_3_NO_PROCESS_OR_NETWORK_PRIMITIVES=PASS'

# Product wiring remains absent and installer remains native-payload free.
if grep -Fq 'WindowsByeDpiAtomicPackagePromoter' "$service" "$worker"; then
  echo 'R42.3 violation: atomic promotion was wired into production Service/IPC.' >&2
  exit 1
fi
if grep -Eqi 'ByeDPI|ciadpi' "$installer"; then
  echo 'R42.3 violation: production installer references ByeDPI while admission is BLOCK.' >&2
  exit 1
fi

echo 'R42_3_SERVICE_IPC_WIRING=ABSENT'

# Synthetic test surface must cover install, upgrade, rollback and fail-closed lease/overwrite gates.
for test_name in \
  'InstallAtomicallyPromotesVerifiedTransactionAndCommitsState' \
  'UpgradeRetainsPreviousImmutableVersionAndRollbackSwapsOnlyTrustedState' \
  'ActiveEngineLeaseBlocksPromotionBeforePackageMove' \
  'ExistingImmutableTargetIsNeverOverwritten' \
  'RollbackIsTargetFreeAndBlockedWhenLeaseIsActive'; do
  grep -Fq "$test_name" "$tests"
done
grep -Fq 'synthetic-r42-3-not-executable' "$tests"

echo 'R42_3_SYNTHETIC_TEST_SURFACE=PASS'

grep -Fq 'repair: DEFERRED' "$contract"
grep -Fq 'uninstall: DEFERRED' "$contract"
grep -Fq 'production_service_or_ipc_wiring: NOT_IMPLEMENTED' "$contract"
grep -Fq 'runtime_network_acquisition: NOT_IMPLEMENTED' "$contract"
grep -Fq 'native_execution: NOT_IMPLEMENTED' "$contract"
grep -Fq 'production_admission: BLOCK' "$contract"

echo 'R42_3_REPAIR_UNINSTALL=DEFERRED'
echo 'R42_3_NATIVE_EXECUTION_TESTED=False'
echo 'R42_3_PRODUCTION_ADMISSION=BLOCK'
echo 'RESULT=R42_3_ATOMIC_PROMOTION_ROLLBACK_PASS_BLOCK'
