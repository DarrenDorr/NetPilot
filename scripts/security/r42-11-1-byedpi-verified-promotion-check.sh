#!/usr/bin/env bash
set -euo pipefail

SOURCE='src/NetPilot.Platform.Windows/WindowsByeDpiVerifiedStagingPromoter.cs'
TESTS='tests/NetPilot.Platform.Windows.Tests/WindowsByeDpiVerifiedStagingPromoterTests.cs'
CONTRACT='security/native-admission/byedpi-verified-promotion-r42-11-1.yaml'
DOC='docs/BYEDPI-VERIFIED-PROMOTION-R42-11-1.md'

for path in "$SOURCE" "$TESTS" "$CONTRACT" "$DOC"; do
  test -f "$path" || { echo "missing required R42.11.1 file: $path" >&2; exit 1; }
done

require_literal() {
  local file="$1"
  local text="$2"
  grep -Fq -- "$text" "$file" || {
    echo "R42.11.1 guard missing required text in $file: $text" >&2
    exit 1
  }
}

forbid_regex() {
  local file="$1"
  local regex="$2"
  if grep -Eiq -- "$regex" "$file"; then
    echo "R42.11.1 guard found forbidden pattern in $file: $regex" >&2
    exit 1
  fi
}

require_literal "$SOURCE" 'ByeDpiImmutableReleaseDocument.Serialize'
require_literal "$SOURCE" 'CopyManifestDocumentBytes'
require_literal "$SOURCE" 'CryptographicOperations.FixedTimeEquals'
require_literal "$SOURCE" 'ByeDpiPackageManifestDocument.Parse'
require_literal "$SOURCE" 'WindowsByeDpiPromotionSourceVerifier.VerifyAsync'
require_literal "$SOURCE" 'WindowsByeDpiAtomicPackagePromoter.PromoteAsync'
require_literal "$SOURCE" 'WindowsByeDpiPromotionSourceVerifier.VerifyForTestingAsync'
require_literal "$SOURCE" 'WindowsByeDpiAtomicPackagePromoter.PromoteForTestingAsync'
require_literal "$SOURCE" 'ByeDpiPackageLifecycleOperation.Install'
require_literal "$SOURCE" 'ByeDpiPackageLifecycleOperation.Upgrade'

require_literal "$TESTS" 'InstallPromotesFullVerifiedDownloadAndStagingChainIntoManagedState'
require_literal "$TESTS" 'InstallThenUpgradeThroughVerifiedPathRetainsPreviousImmutableVersion'
require_literal "$TESTS" 'ActiveEngineLeaseBlocksPromotionAndPreservesVerifiedStaging'
require_literal "$TESTS" 'StagedTreeMutationIsRejectedBeforeAtomicPromotion'
require_literal "$TESTS" 'BridgeRejectsOperationsOutsideInstallUpgradeBoundary'
require_literal "$TESTS" 'StubHandler'

require_literal "$CONTRACT" 'status: VERIFIED_ATOMIC_PROMOTION_BOUNDARY_ONLY'
require_literal "$CONTRACT" 'existing_r42_3_source_verifier_only: true'
require_literal "$CONTRACT" 'existing_r42_3_atomic_promoter_only: true'
require_literal "$CONTRACT" 'new_filesystem_primitives_in_bridge: false'
require_literal "$CONTRACT" 'filesystem_write: EXISTING_R42_3_ATOMIC_PROMOTER_ONLY'
require_literal "$CONTRACT" 'service_ipc_wiring: ABSENT'
require_literal "$CONTRACT" 'native_execution: NOT_IMPLEMENTED'
require_literal "$CONTRACT" 'production_admission: BLOCK'

# No new filesystem, archive, network, Service/IPC or native process primitives in the bridge.
forbid_regex "$SOURCE" 'FileStream|\bFile\.|\bDirectory\.|\bPath\.|SafeZipExtractor|ZipArchive|FileSystemAcl|Registry\.|SocketsHttpHandler|HttpClientHandler|new[[:space:]]+HttpClient|WebRequest|WebClient|TcpClient|UdpClient|Dns\.|NamedPipe|ServiceController|Process(Start|\.)'

# The bridge must have exactly one production source-verification call and one production promotion call.
source_count="$(grep -Ec '^[[:space:]]*var source = await WindowsByeDpiPromotionSourceVerifier\.VerifyAsync\(' "$SOURCE")"
promote_count="$(grep -Ec '^[[:space:]]*return await WindowsByeDpiAtomicPackagePromoter\.PromoteAsync\(' "$SOURCE")"
if [[ "$source_count" != '1' || "$promote_count" != '1' ]]; then
  echo "R42.11.1 guard requires exactly one production verifier and one production promoter call; verifier=$source_count promoter=$promote_count" >&2
  exit 1
fi

# Focused tests may use temporary filesystem state, but must keep transport fake/in-memory.
forbid_regex "$TESTS" 'SocketsHttpHandler|HttpClientHandler|WebRequest|WebClient|TcpClient|UdpClient|Dns\.'

printf '%s\n' \
  'schema_version=1' \
  'scope=byedpi-r42.11.1-verified-promotion-guard' \
  'result=PASS' \
  'verified_promotion=EXISTING_R42_3_BOUNDARY_ONLY' \
  'new_filesystem_primitives_in_bridge=false' \
  'service_ipc_wiring=ABSENT' \
  'native_execution=NOT_IMPLEMENTED' \
  'production_admission=BLOCK' \
  > r42-11-1-verified-promotion-guard-evidence.txt
