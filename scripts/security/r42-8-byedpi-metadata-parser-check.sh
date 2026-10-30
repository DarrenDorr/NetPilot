#!/usr/bin/env bash
set -euo pipefail

parser='src/NetPilot.Updater/GithubByeDpiReleaseParser.cs'
response_policy='src/NetPilot.Updater/ByeDpiTransportResponsePolicy.cs'
parser_tests='tests/NetPilot.Core.Tests/GithubByeDpiReleaseParserTests.cs'
response_tests='tests/NetPilot.Core.Tests/ByeDpiTransportResponsePolicyTests.cs'
contract='security/native-admission/byedpi-metadata-parser-r42-8.yaml'
service='src/NetPilot.Service/Program.cs'
worker='src/NetPilot.Service/PrivilegedIpcWorker.cs'
installer='installer/NetPilot.iss'

for file in "$parser" "$response_policy" "$parser_tests" "$response_tests" "$contract" "$service" "$worker" "$installer"; do
  test -f "$file"
done

bash scripts/security/r42-7-byedpi-acquisition-trust-check.sh >/tmp/netpilot-r42-8-parent.txt
for marker in \
  'R42_7_PARENT_R42_6_GUARD=PASS' \
  'R42_7_EXACT_TAG_DISCOVERY_POLICY=PASS' \
  'R42_7_EXACT_IMMUTABLE_ASSET_POLICY=PASS' \
  'R42_7_PINNED_GITHUB_API_CONTRACT=PASS' \
  'R42_7_ORIGIN_REDIRECT_RESOURCE_POLICY=PASS' \
  'R42_7_ANTI_DOWNGRADE_POLICY=PASS' \
  'R42_7_NO_NETWORK_FILESYSTEM_OR_EXECUTION_PRIMITIVES=PASS' \
  'R42_7_PRODUCT_DOWNLOADER=NOT_IMPLEMENTED' \
  'R42_7_NETWORK_ACQUISITION=NOT_IMPLEMENTED' \
  'R42_7_FILESYSTEM_WRITE=NOT_IMPLEMENTED' \
  'R42_7_PRODUCTION_ADMISSION=BLOCK' \
  'RESULT=R42_7_ACQUISITION_TRUST_POLICY_PASS_BLOCK'; do
  grep -Fq "$marker" /tmp/netpilot-r42-8-parent.txt
done
rm -f /tmp/netpilot-r42-8-parent.txt

echo 'R42_8_PARENT_R42_7_GUARD=PASS'

# Parser input is bounded strict JSON and all duplicate properties are rejected.
for marker in \
  'MaxAssetsInMetadata = 16' \
  'MaxReleaseMetadataBytes' \
  'AllowTrailingCommas = false' \
  'CommentHandling = JsonCommentHandling.Disallow' \
  'MaxDepth = 16' \
  'values.TryAdd(property.Name, property.Value)' \
  'duplicate property'; do
  grep -Fq "$marker" "$parser"
done

echo 'R42_8_BOUNDED_DUPLICATE_SAFE_JSON=PASS'

# Security-relevant GitHub fields are required and typed.
for marker in \
  '"id"' \
  '"tag_name"' \
  '"draft"' \
  '"prerelease"' \
  '"immutable"' \
  '"assets"' \
  '"name"' \
  '"state"' \
  '"size"' \
  '"digest"' \
  '"url"' \
  'PositiveInt64' \
  'RequiredBoolean' \
  'RequiredAbsoluteUri'; do
  grep -Fq "$marker" "$parser"
done

grep -Fq 'unknown_properties: TOLERATE_WITHOUT_SEMANTIC_EFFECT' "$contract"

echo 'R42_8_REQUIRED_TYPED_METADATA_FIELDS=PASS'

# Only exact GitHub sha256:<64 lowercase hex> asset digests become typed SHA-256.
for marker in \
  'const string prefix = "sha256:"' \
  'prefix.Length + 64' \
  'lowercase SHA-256 hex'; do
  grep -Fq "$marker" "$parser"
done

echo 'R42_8_EXACT_SHA256_DIGEST_PARSER=PASS'

# Parsed facts must compose with R42.7 trust policy before becoming an acquisition plan.
grep -Fq 'ByeDpiAcquisitionReleaseMetadata' "$parser"
grep -Fq 'ByeDpiAcquisitionTrustPolicy.CreatePlan' "$parser_tests"
grep -Fq 'r42_7_policy_evaluation_after_parse: REQUIRED' "$contract"

echo 'R42_8_R42_7_POLICY_COMPOSITION=PASS'

# Pure response policy permits only metadata 200, direct asset 200 or one validated 302 -> terminal 200.
for marker in \
  'response.StatusCode != 200' \
  'response.StatusCode == 200' \
  'response.StatusCode == 302' \
  'ValidateReleaseAssetRedirect(response.Location, 1)' \
  'ValidateOptionalExactContentLength' \
  'RejectUnexpectedLocation'; do
  grep -Fq "$marker" "$response_policy"
done
for marker in \
  'direct_asset_response_200: ALLOW' \
  'release_asset_redirect_302: ALLOW_AFTER_POLICY_VALIDATION' \
  'other_redirects: REJECT' \
  'content_length_if_present: MUST_EQUAL_IMMUTABLE_DESCRIPTOR_SIZE'; do
  grep -Fq "$marker" "$contract"
done

echo 'R42_8_PURE_TRANSPORT_RESPONSE_POLICY=PASS'

# Focused fixture/negative tests must remain present.
for test_name in \
  'ExactFixtureParsesAndCreatesTrustedPlan' \
  'UnknownGithubFieldsAreToleratedWithoutChangingRequiredMeaning' \
  'DuplicateRootPropertyIsRejected' \
  'DuplicateAssetPropertyIsRejected' \
  'MissingRequiredRootPropertyIsRejected' \
  'UnsupportedDigestAlgorithmIsRejected' \
  'UppercaseDigestIsRejected' \
  'NonHttpsAssetApiUriIsRejected' \
  'NonPositiveAssetSizeIsRejected' \
  'TooManyAssetsAreRejectedBeforePolicyEvaluation' \
  'OversizedMetadataIsRejected' \
  'MalformedJsonIsRejectedWithTypedCode'; do
  grep -Fq "$test_name" "$parser_tests"
done
for test_name in \
  'MetadataHttp200WithinLimitIsAccepted' \
  'MetadataRedirectOrOversizeIsRejected' \
  'DirectAssetHttp200WithExactLengthIsAccepted' \
  'DirectAssetLengthMismatchIsRejected' \
  'TrustedSingleAssetRedirectIsReturned' \
  'MissingOrUntrustedAssetRedirectLocationIsRejected' \
  'RedirectedAssetMustTerminateAtHttp200' \
  'Http200WithUnexpectedLocationIsRejected' \
  'UnsupportedAssetStatusIsRejected'; do
  grep -Fq "$test_name" "$response_tests"
done

echo 'R42_8_FIXTURE_AND_NEGATIVE_TEST_SURFACE=PASS'

# R42.8 remains parser/policy only: no network, filesystem mutation or process execution primitives.
for file in "$parser" "$response_policy"; do
  for forbidden in \
    'HttpClient' \
    'WebClient' \
    'HttpRequestMessage' \
    'SocketsHttpHandler' \
    'FileStream' \
    'File.Write' \
    'Directory.Create' \
    'Process.Start' \
    'ProcessStartInfo' \
    'WinDivert'; do
    if grep -Fq "$forbidden" "$file"; then
      echo "R42.8 violation: $file gained forbidden primitive: $forbidden" >&2
      exit 1
    fi
  done
done

echo 'R42_8_NO_NETWORK_FILESYSTEM_OR_EXECUTION_PRIMITIVES=PASS'

if grep -Fq 'GithubByeDpiReleaseParser' "$service" "$worker" \
  || grep -Fq 'ByeDpiTransportResponsePolicy' "$service" "$worker"; then
  echo 'R42.8 violation: parser/response policy was wired into Service/IPC.' >&2
  exit 1
fi
if grep -Eqi 'ByeDPI|ciadpi' "$installer"; then
  echo 'R42.8 violation: installer references ByeDPI while production admission is BLOCK.' >&2
  exit 1
fi

grep -Fq 'http_transport: NOT_IMPLEMENTED' "$contract"
grep -Fq 'product_downloader: NOT_IMPLEMENTED' "$contract"
grep -Fq 'network_acquisition: NOT_IMPLEMENTED' "$contract"
grep -Fq 'filesystem_write: NOT_IMPLEMENTED' "$contract"
grep -Fq 'staging_invocation: NOT_IMPLEMENTED' "$contract"
grep -Fq 'service_or_ipc_wiring: NOT_IMPLEMENTED' "$contract"
grep -Fq 'native_execution: NOT_IMPLEMENTED' "$contract"
grep -Fq 'production_admission: BLOCK' "$contract"

echo 'R42_8_HTTP_TRANSPORT=NOT_IMPLEMENTED'
echo 'R42_8_PRODUCT_DOWNLOADER=NOT_IMPLEMENTED'
echo 'R42_8_NETWORK_ACQUISITION=NOT_IMPLEMENTED'
echo 'R42_8_FILESYSTEM_WRITE=NOT_IMPLEMENTED'
echo 'R42_8_SERVICE_IPC_WIRING=ABSENT'
echo 'R42_8_NATIVE_EXECUTION_TESTED=False'
echo 'R42_8_PRODUCTION_ADMISSION=BLOCK'
echo 'RESULT=R42_8_METADATA_PARSER_RESPONSE_POLICY_PASS_BLOCK'
