#!/usr/bin/env bash
set -euo pipefail

SOURCE='src/NetPilot.Updater/ByeDpiIsolatedHttpTransport.cs'
POLICY='src/NetPilot.Updater/ByeDpiTransportResponsePolicy.cs'
TESTS='tests/NetPilot.Core.Tests/ByeDpiIsolatedHttpTransportTests.cs'
CONTRACT='security/native-admission/byedpi-isolated-http-r42-9.yaml'
DOC='docs/BYEDPI-ISOLATED-HTTP-R42-9.md'

for path in "$SOURCE" "$POLICY" "$TESTS" "$CONTRACT" "$DOC"; do
  test -f "$path" || { echo "missing required R42.9 file: $path" >&2; exit 1; }
done

require_literal() {
  local file="$1"
  local text="$2"
  grep -Fq -- "$text" "$file" || {
    echo "R42.9 guard missing required text in $file: $text" >&2
    exit 1
  }
}

forbid_regex() {
  local file="$1"
  local regex="$2"
  if grep -Eiq -- "$regex" "$file"; then
    echo "R42.9 guard found forbidden pattern in $file: $regex" >&2
    exit 1
  fi
}

require_literal "$SOURCE" 'HttpCompletionOption.ResponseHeadersRead'
require_literal "$SOURCE" 'IncrementalHash.CreateHash(HashAlgorithmName.SHA256)'
require_literal "$SOURCE" 'CryptographicOperations.FixedTimeEquals'
require_literal "$SOURCE" 'ByeDpiAcquisitionTrustPolicy.MaxReleaseMetadataBytes'
require_literal "$SOURCE" 'ByeDpiAcquisitionTrustPolicy.ValidateReleaseAssetRedirect'
require_literal "$SOURCE" 'X-GitHub-Api-Version'
require_literal "$SOURCE" 'hidden automatic redirect'
require_literal "$SOURCE" 'Timeout.InfiniteTimeSpan'
require_literal "$POLICY" 'HTTP 302 response must not carry an asset Content-Length'

require_literal "$CONTRACT" 'status: ISOLATED_TEST_BOUNDARY_ONLY'
require_literal "$CONTRACT" 'live_network_tested: false'
require_literal "$CONTRACT" 'product_downloader: NOT_IMPLEMENTED'
require_literal "$CONTRACT" 'filesystem_write: NOT_IMPLEMENTED'
require_literal "$CONTRACT" 'service_ipc_wiring: ABSENT'
require_literal "$CONTRACT" 'native_execution: NOT_IMPLEMENTED'
require_literal "$CONTRACT" 'production_admission: BLOCK'
require_literal "$CONTRACT" "api_version_header: '2026-03-10'"
require_literal "$CONTRACT" 'maximum_redirects: 1'
require_literal "$CONTRACT" 'total_timeout_seconds: 20'
require_literal "$CONTRACT" 'sha256_verified_while_streaming: true'

# R42.9 may use only an injected handler. It must not instantiate a live network stack.
forbid_regex "$SOURCE" 'SocketsHttpHandler|HttpClientHandler|WebRequest|WebClient|TcpClient|UdpClient|Dns\.'

# No file/staging/process/service/IPC integration in this gate.
forbid_regex "$SOURCE" 'FileStream|\bFile\.|\bDirectory\.|\bPath\.|Process(Start|\.)|NamedPipe|ServiceController|Registry\.'

# No retry/backoff layer and no credential-bearing request surface.
forbid_regex "$SOURCE" 'Polly|Retry|AuthorizationHeaderValue|NetworkCredential|CredentialCache|CookieContainer'

# Focused tests must remain fake/in-memory and must not create a live handler either.
forbid_regex "$TESTS" 'SocketsHttpHandler|HttpClientHandler|WebRequest|WebClient|TcpClient|UdpClient|Dns\.'
require_literal "$TESTS" 'StubHandler'
require_literal "$TESTS" 'Assert.Null(observed.Headers.Authorization)'
require_literal "$TESTS" 'Assert.False(observed.Headers.Contains("Cookie"))'

printf '%s\n' \
  'schema_version=1' \
  'scope=byedpi-r42.9-isolated-http-guard' \
  'result=PASS' \
  'http_transport=ISOLATED_TEST_BOUNDARY_ONLY' \
  'live_network_tested=false' \
  'product_downloader=NOT_IMPLEMENTED' \
  'filesystem_write=NOT_IMPLEMENTED' \
  'service_ipc_wiring=ABSENT' \
  'native_execution=NOT_IMPLEMENTED' \
  'production_admission=BLOCK' \
  > r42-9-isolated-http-guard-evidence.txt
