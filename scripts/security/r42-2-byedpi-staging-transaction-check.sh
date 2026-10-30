#!/usr/bin/env bash
set -euo pipefail

transaction='src/NetPilot.Platform.Windows/WindowsByeDpiProtectedStagingTransaction.cs'
tests='tests/NetPilot.Platform.Windows.Tests/WindowsByeDpiProtectedStagingTransactionTests.cs'
contract='security/native-admission/byedpi-staging-transaction-r42-2.yaml'
service='src/NetPilot.Service/Program.cs'
worker='src/NetPilot.Service/PrivilegedIpcWorker.cs'
installer='installer/NetPilot.iss'

for file in "$transaction" "$tests" "$contract" "$service" "$worker" "$installer"; do
  test -f "$file"
done

# Parent R42.1 exact-package and production fail-closed boundary must still pass.
bash scripts/security/r42-1-byedpi-package-verification-check.sh >/tmp/netpilot-r42-2-parent.txt
for marker in \
  'R42_1_PARENT_FAIL_CLOSED_GUARD=PASS' \
  'R42_1_PRODUCT_VERIFIERS_READ_ONLY=PASS' \
  'R42_1_WINDOWS_PROTECTED_STAGING_BOUNDARY=PASS' \
  'R42_1_SERVICE_IPC_WIRING=ABSENT' \
  'R42_1_NATIVE_EXECUTION_TESTED=False' \
  'R42_1_PRODUCTION_ADMISSION=BLOCK' \
  'RESULT=R42_1_EXACT_PACKAGE_VERIFICATION_PASS_BLOCK'; do
  grep -Fq "$marker" /tmp/netpilot-r42-2-parent.txt
done
rm -f /tmp/netpilot-r42-2-parent.txt

echo 'R42_2_PARENT_R42_1_GUARD=PASS'

# Transaction must reuse the already reviewed extraction/verification boundaries.
grep -Fq 'SafeZipExtractor.ExtractAsync' "$transaction"
grep -Fq 'ByeDpiPackageManifestVerifier.VerifyAsync' "$transaction"
grep -Fq 'WindowsByeDpiProtectedStagingSecurityVerifier.Verify' "$transaction"
grep -Fq 'EnsureWindowsAndLocalSystem' "$transaction"
grep -Fq '"NetPilot", "Engines", "Staging", "ByeDPI"' "$transaction"
grep -Fq 'WindowsUserProfileDirectorySecurity.CreateServiceOnlyAcl' "$transaction"
grep -Fq 'FileOptions.WriteThrough' "$transaction"
grep -Fq 'File.Move(tempPath, journalPath, overwrite: true)' "$transaction"

echo 'R42_2_TRANSACTION_BOUNDARY=PASS'

# The recovery journal is deliberately path-free: exact paths are derived only from transactionId.
for field in \
  'schemaVersion = record.SchemaVersion' \
  'transactionId = record.TransactionId' \
  'manifestSha256 = record.ManifestSha256' \
  'version = record.Version' \
  'phase = record.Phase.ToString()' \
  'updatedUtc = record.UpdatedUtc.ToString'; do
  grep -Fq "$field" "$transaction"
done
for forbidden_journal_field in \
  'payloadPath =' \
  'stagingPath =' \
  'absolutePath =' \
  'downloadUrl =' \
  'commandLine =' \
  'processId ='; do
  if grep -Fq "$forbidden_journal_field" "$transaction"; then
    echo "R42.2 violation: recovery journal gained unsafe field: $forbidden_journal_field" >&2
    exit 1
  fi
done

echo 'R42_2_PATH_FREE_JOURNAL=PASS'

# New transaction code may mutate only its protected staging tree; it must not gain execution/network primitives.
for forbidden in \
  'Process.Start' \
  'ProcessStartInfo' \
  'HttpClient' \
  'WebClient' \
  'Invoke-WebRequest' \
  'WinDivert'; do
  if grep -Fq "$forbidden" "$transaction"; then
    echo "R42.2 violation: transaction gained forbidden primitive: $forbidden" >&2
    exit 1
  fi
done

echo 'R42_2_NO_PROCESS_OR_NETWORK_PRIMITIVES=PASS'

# Recovery must reject reparse points and derive all deletion targets from validated lowercase-hex ids.
grep -Fq 'TransactionIdLength = 32' "$transaction"
grep -Fq 'ValidateTransactionId(transactionId)' "$transaction"
grep -Fq 'DeleteTransactionRoot(normalizedBase, transactionId)' "$transaction"
grep -Fq 'RejectReparsePoint(fullPath' "$transaction"
grep -Fq 'recursive: false' "$transaction"

echo 'R42_2_RECOVERY_BOUNDARY=PASS'

# Synthetic tests cover success, interrupted recovery, manifest failure and archive traversal cleanup.
for test_name in \
  'SyntheticPackageStagesAndVerifiedJournalIsPreserved' \
  'ExtractingJournalIsRecoveredWithoutTrustingAPathFromJson' \
  'ManifestFailureRemovesPayloadAndJournal' \
  'ArchiveTraversalFailureLeavesNoTransactionResidue'; do
  grep -Fq "$test_name" "$tests"
done
grep -Fq 'synthetic-not-executable-r42-2' "$tests"

echo 'R42_2_SYNTHETIC_TEST_SURFACE=PASS'

# Product wiring must remain absent; this gate is isolated package-lifecycle work only.
if grep -Fq 'WindowsByeDpiProtectedStagingTransaction' "$service" "$worker"; then
  echo 'R42.2 violation: staging transaction was wired into production Service/IPC.' >&2
  exit 1
fi
if grep -Eqi 'ByeDPI|ciadpi' "$installer"; then
  echo 'R42.2 violation: production installer references ByeDPI while admission is BLOCK.' >&2
  exit 1
fi

grep -Fq 'same_volume_promotion: NOT_IMPLEMENTED' "$contract"
grep -Fq 'production_service_or_ipc_wiring: NOT_IMPLEMENTED' "$contract"
grep -Fq 'runtime_network_acquisition: NOT_IMPLEMENTED' "$contract"
grep -Fq 'native_execution: NOT_IMPLEMENTED' "$contract"
grep -Fq 'production_admission: BLOCK' "$contract"

echo 'R42_2_SERVICE_IPC_WIRING=ABSENT'
echo 'R42_2_PROMOTION=NOT_IMPLEMENTED'
echo 'R42_2_NATIVE_EXECUTION_TESTED=False'
echo 'R42_2_PRODUCTION_ADMISSION=BLOCK'
echo 'RESULT=R42_2_STAGING_TRANSACTION_PASS_BLOCK'
