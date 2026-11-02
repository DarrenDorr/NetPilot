#!/usr/bin/env bash
set -euo pipefail

SOURCE='src/NetPilot.Platform.Windows/WindowsByeDpiVerifiedBundleStager.cs'
TESTS='tests/NetPilot.Platform.Windows.Tests/WindowsByeDpiVerifiedBundleStagerTests.cs'
CONTRACT='security/native-admission/byedpi-verified-staging-r42-11.yaml'
DOC='docs/BYEDPI-VERIFIED-STAGING-R42-11.md'

for path in "$SOURCE" "$TESTS" "$CONTRACT" "$DOC"; do
  test -f "$path" || { echo "missing required R42.11 file: $path" >&2; exit 1; }
done

require_literal() {
  local file="$1"
  local text="$2"
  grep -Fq -- "$text" "$file" || {
    echo "R42.11 guard missing required text in $file: $text" >&2
    exit 1
  }
}

forbid_regex() {
  local file="$1"
  local regex="$2"
  if grep -Eiq -- "$regex" "$file"; then
    echo "R42.11 guard found forbidden pattern in $file: $regex" >&2
    exit 1
  fi
}

require_literal "$SOURCE" 'ByeDpiImmutableReleaseDocument.Serialize'
require_literal "$SOURCE" 'CopyPayloadBytes'
require_literal "$SOURCE" 'CopyManifestDocumentBytes'
require_literal "$SOURCE" 'CopyProvenanceBytes'
require_literal "$SOURCE" 'CryptographicOperations.FixedTimeEquals'
require_literal "$SOURCE" 'ByeDpiPackageManifestDocument.Parse'
require_literal "$SOURCE" 'WindowsByeDpiProtectedStagingTransaction.StageAndVerifyAsync'
require_literal "$SOURCE" 'WindowsByeDpiProtectedStagingTransaction.StageAndVerifyForTestingAsync'

require_literal "$TESTS" 'VerifiedDownloaderBundleStagesThroughExistingProtectedTransactionBoundary'
require_literal "$TESTS" 'BundleExportCopiesCannotMutateBytesLaterUsedForStaging'
require_literal "$TESTS" 'PackageTreeDriftIsRejectedByExistingStagingVerificationAndLeavesNoTransactionResidue'
require_literal "$TESTS" 'VerifiedBundleAggregateIdentityMatchesImmutableDescriptorAssets'
require_literal "$TESTS" 'StubHandler'

require_literal "$CONTRACT" 'status: VERIFIED_PROTECTED_STAGING_BOUNDARY_ONLY'
require_literal "$CONTRACT" 'existing_r42_2_transaction_only: true'
require_literal "$CONTRACT" 'new_filesystem_primitives_in_bridge: false'
require_literal "$CONTRACT" 'filesystem_write: EXISTING_PROTECTED_STAGING_TRANSACTION_ONLY'
require_literal "$CONTRACT" 'package_promotion: NOT_IMPLEMENTED'
require_literal "$CONTRACT" 'service_ipc_wiring: ABSENT'
require_literal "$CONTRACT" 'native_execution: NOT_IMPLEMENTED'
require_literal "$CONTRACT" 'production_admission: BLOCK'

# The R42.11 bridge must not grow a second filesystem/extraction implementation.
forbid_regex "$SOURCE" 'FileStream|\bFile\.|\bDirectory\.|\bPath\.|SafeZipExtractor|ZipArchive|FileSystemAcl|Registry\.'

# No new network stack, retry layer, service/IPC or native process surface.
forbid_regex "$SOURCE" 'SocketsHttpHandler|HttpClientHandler|new[[:space:]]+HttpClient|WebRequest|WebClient|TcpClient|UdpClient|Dns\.|Polly|Retry|NamedPipe|ServiceController|Process(Start|\.)'

# Production mutation is allowed only through the exact inherited staging call.
count="$(grep -Fc 'WindowsByeDpiProtectedStagingTransaction.StageAndVerifyAsync' "$SOURCE")"
if [[ "$count" != '1' ]]; then
  echo "R42.11 guard requires exactly one production protected staging invocation; found $count" >&2
  exit 1
fi

# The focused test transport stays fake/in-memory; filesystem use is test-only staging evidence.
forbid_regex "$TESTS" 'SocketsHttpHandler|HttpClientHandler|WebRequest|WebClient|TcpClient|UdpClient|Dns\.'

printf '%s\n' \
  'schema_version=1' \
  'scope=byedpi-r42.11-verified-staging-guard' \
  'result=PASS' \
  'verified_staging=EXISTING_PROTECTED_TRANSACTION_ONLY' \
  'new_filesystem_primitives_in_bridge=false' \
  'package_promotion=NOT_IMPLEMENTED' \
  'service_ipc_wiring=ABSENT' \
  'native_execution=NOT_IMPLEMENTED' \
  'production_admission=BLOCK' \
  > r42-11-verified-staging-guard-evidence.txt
