#!/usr/bin/env bash
set -euo pipefail

SOURCE='src/NetPilot.Updater/ByeDpiVerifiedReleaseDownloader.cs'
TESTS='tests/NetPilot.Core.Tests/ByeDpiVerifiedReleaseDownloaderTests.cs'
CONTRACT='security/native-admission/byedpi-verified-downloader-r42-10.yaml'
DOC='docs/BYEDPI-VERIFIED-DOWNLOADER-R42-10.md'

for path in "$SOURCE" "$TESTS" "$CONTRACT" "$DOC"; do
  test -f "$path" || { echo "missing required R42.10 file: $path" >&2; exit 1; }
done

require_literal() {
  local file="$1"
  local text="$2"
  grep -Fq -- "$text" "$file" || {
    echo "R42.10 guard missing required text in $file: $text" >&2
    exit 1
  }
}

forbid_regex() {
  local file="$1"
  local regex="$2"
  if grep -Eiq -- "$regex" "$file"; then
    echo "R42.10 guard found forbidden pattern in $file: $regex" >&2
    exit 1
  fi
}

require_literal "$SOURCE" 'ByeDpiAcquisitionTrustPolicy.CreatePlan'
require_literal "$SOURCE" 'FetchReleaseMetadataAsync'
require_literal "$SOURCE" 'FetchVerifiedAssetAsync'
require_literal "$SOURCE" 'ByeDpiPackageManifestDocument.Parse'
require_literal "$SOURCE" 'CryptographicOperations.FixedTimeEquals'
require_literal "$SOURCE" 'plan.Assets.Count != 3'
require_literal "$SOURCE" 'MaxTotalAssetBytes'
require_literal "$SOURCE" 'CopyPayloadBytes'

require_literal "$TESTS" 'DownloadsExactlyThreeVerifiedAssetsAndReturnsDefensiveBundle'
require_literal "$TESTS" 'DowngradeIsBlockedBeforeAnyAssetRequest'
require_literal "$TESTS" 'HashValidButMalformedManifestIsRejectedAfterTransportVerification'
require_literal "$TESTS" 'ManifestIdentityMustMatchImmutableReleaseDescriptor'
require_literal "$TESTS" 'AssetHashFailureNeverProducesBundle'
require_literal "$TESTS" 'StubHandler'

require_literal "$CONTRACT" 'status: VERIFIED_IN_MEMORY_BOUNDARY_ONLY'
require_literal "$CONTRACT" 'expected_asset_count: 3'
require_literal "$CONTRACT" 'downloader_reverification: true'
require_literal "$CONTRACT" 'filesystem_write: NOT_IMPLEMENTED'
require_literal "$CONTRACT" 'staging_invocation: NOT_IMPLEMENTED'
require_literal "$CONTRACT" 'service_ipc_wiring: ABSENT'
require_literal "$CONTRACT" 'native_execution: NOT_IMPLEMENTED'
require_literal "$CONTRACT" 'production_admission: BLOCK'

# R42.10 may only compose the already-isolated R42.9 transport.
forbid_regex "$SOURCE" 'SocketsHttpHandler|HttpClientHandler|new[[:space:]]+HttpClient|WebRequest|WebClient|TcpClient|UdpClient|Dns\.'

# No filesystem, extraction/staging, service/IPC or process/native execution at this gate.
forbid_regex "$SOURCE" 'FileStream|\bFile\.|\bDirectory\.|\bPath\.|SafeZipExtractor|WindowsByeDpiProtectedStagingTransaction|Process(Start|\.)|NamedPipe|ServiceController|Registry\.'

# Focused tests remain fake/in-memory only.
forbid_regex "$TESTS" 'SocketsHttpHandler|HttpClientHandler|WebRequest|WebClient|TcpClient|UdpClient|Dns\.'

printf '%s\n' \
  'schema_version=1' \
  'scope=byedpi-r42.10-verified-downloader-guard' \
  'result=PASS' \
  'verified_bundle=IN_MEMORY_ONLY' \
  'filesystem_write=NOT_IMPLEMENTED' \
  'staging_invocation=NOT_IMPLEMENTED' \
  'service_ipc_wiring=ABSENT' \
  'native_execution=NOT_IMPLEMENTED' \
  'production_admission=BLOCK' \
  > r42-10-verified-downloader-guard-evidence.txt
