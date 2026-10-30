#!/usr/bin/env bash
set -euo pipefail

policy='src/NetPilot.Updater/ByeDpiAcquisitionTrustPolicy.cs'
tests='tests/NetPilot.Core.Tests/ByeDpiAcquisitionTrustPolicyTests.cs'
contract='security/native-admission/byedpi-acquisition-trust-r42-7.yaml'
service='src/NetPilot.Service/Program.cs'
worker='src/NetPilot.Service/PrivilegedIpcWorker.cs'
installer='installer/NetPilot.iss'

for file in "$policy" "$tests" "$contract" "$service" "$worker" "$installer"; do
  test -f "$file"
done

bash scripts/security/r42-6-byedpi-immutable-distribution-check.sh >/tmp/netpilot-r42-7-parent.txt
for marker in \
  'R42_6_PARENT_R42_5_GUARD=PASS' \
  'R42_6_EXACT_IMMUTABLE_RELEASE_IDENTITY=PASS' \
  'R42_6_STRICT_DISTRIBUTION_DOCUMENT=PASS' \
  'R42_6_NO_NETWORK_OR_EXECUTION_PRIMITIVES=PASS' \
  'R42_6_PRODUCT_DOWNLOADER=NOT_IMPLEMENTED' \
  'R42_6_NETWORK_ACQUISITION=NOT_IMPLEMENTED' \
  'R42_6_PRODUCTION_ADMISSION=BLOCK' \
  'RESULT=R42_6_IMMUTABLE_DISTRIBUTION_PASS_BLOCK'; do
  grep -Fq "$marker" /tmp/netpilot-r42-7-parent.txt
done
rm -f /tmp/netpilot-r42-7-parent.txt

echo 'R42_7_PARENT_R42_6_GUARD=PASS'

# Exact tag-only GitHub API plan; mutable latest discovery must not appear.
for marker in \
  'releases/tags/' \
  'RequiredRepositoryOwner' \
  'RequiredRepositoryName' \
  'GetReleaseByTagApiUri' \
  'GetAssetApiUri'; do
  grep -Fq "$marker" "$policy"
done
if grep -Fq '/releases/latest' "$policy"; then
  echo 'R42.7 violation: mutable latest release discovery entered ByeDPI acquisition policy.' >&2
  exit 1
fi

echo 'R42_7_EXACT_TAG_DISCOVERY_POLICY=PASS'

# Release metadata is fail-closed: stable + immutable and exactly the R42.6 asset set.
for marker in \
  'metadata.Draft' \
  'metadata.Prerelease' \
  '!metadata.Immutable' \
  'metadata.Assets.Count != expectedAssets.Length' \
  'matches.Length != 1' \
  'candidate.SizeBytes != expected.SizeBytes' \
  'candidate.Sha256Hex' \
  'candidate.AssetApiUri'; do
  grep -Fq "$marker" "$policy"
done

echo 'R42_7_EXACT_IMMUTABLE_ASSET_POLICY=PASS'

# Fixed origin, one redirect hop, bounded metadata/time/aggregate size.
for marker in \
  'release-assets.githubusercontent.com' \
  'MaxReleaseMetadataBytes = 256 * 1024' \
  'NetworkTimeoutSeconds = 20' \
  'MaxRedirects = 1' \
  'MaxTotalAssetBytes = 64L * 1024 * 1024' \
  'redirectNumber != 1' \
  '!uri.IsDefaultPort' \
  '!string.IsNullOrEmpty(uri.UserInfo)' \
  '!string.IsNullOrEmpty(uri.Fragment)'; do
  grep -Fq "$marker" "$policy"
done

echo 'R42_7_ORIGIN_REDIRECT_RESOURCE_POLICY=PASS'

# Downgrade is rejected; equal version remains available for exact-current repair acquisition.
for marker in \
  'ValidateAntiDowngrade' \
  'ByeDpiAcquisitionDowngradeBlocked' \
  'Compare(target, current) < 0' \
  'ParseCanonicalVersion'; do
  grep -Fq "$marker" "$policy"
done

echo 'R42_7_ANTI_DOWNGRADE_POLICY=PASS'

# Focused negative tests must remain present.
for test_name in \
  'CreatePlanUsesExactTagEndpointAndImmutableAssets' \
  'MutableReleaseIsRejected' \
  'DraftOrPrereleaseIsRejected' \
  'ReleaseTagDriftIsRejected' \
  'ExtraReleaseAssetIsRejected' \
  'AssetDigestDriftIsRejected' \
  'AssetSizeDriftIsRejected' \
  'AssetApiUriDriftIsRejected' \
  'DowngradeBelowInstalledVersionIsRejected' \
  'EqualInstalledVersionIsAllowedForFutureRepairAcquisition' \
  'NonCanonicalInstalledVersionIsRejected' \
  'TrustedSingleGithubAssetRedirectIsAccepted' \
  'UntrustedRedirectOriginIsRejected' \
  'AdditionalRedirectHopIsRejected'; do
  grep -Fq "$test_name" "$tests"
done

echo 'R42_7_NEGATIVE_TEST_SURFACE=PASS'

# R42.7 is pure policy only: no network, filesystem mutation or process execution primitives.
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
  if grep -Fq "$forbidden" "$policy"; then
    echo "R42.7 violation: pure acquisition policy gained forbidden primitive: $forbidden" >&2
    exit 1
  fi
done

echo 'R42_7_NO_NETWORK_FILESYSTEM_OR_EXECUTION_PRIMITIVES=PASS'

if grep -Fq 'ByeDpiAcquisitionTrustPolicy' "$service" "$worker"; then
  echo 'R42.7 violation: acquisition policy was wired into Service/IPC.' >&2
  exit 1
fi
if grep -Eqi 'ByeDPI|ciadpi' "$installer"; then
  echo 'R42.7 violation: installer references ByeDPI while production admission is BLOCK.' >&2
  exit 1
fi

grep -Fq 'mutable_latest_endpoint: FORBIDDEN' "$contract"
grep -Fq 'product_downloader: NOT_IMPLEMENTED' "$contract"
grep -Fq 'network_acquisition: NOT_IMPLEMENTED' "$contract"
grep -Fq 'filesystem_write: NOT_IMPLEMENTED' "$contract"
grep -Fq 'service_or_ipc_wiring: NOT_IMPLEMENTED' "$contract"
grep -Fq 'native_execution: NOT_IMPLEMENTED' "$contract"
grep -Fq 'production_admission: BLOCK' "$contract"

echo 'R42_7_PRODUCT_DOWNLOADER=NOT_IMPLEMENTED'
echo 'R42_7_NETWORK_ACQUISITION=NOT_IMPLEMENTED'
echo 'R42_7_FILESYSTEM_WRITE=NOT_IMPLEMENTED'
echo 'R42_7_SERVICE_IPC_WIRING=ABSENT'
echo 'R42_7_NATIVE_EXECUTION_TESTED=False'
echo 'R42_7_PRODUCTION_ADMISSION=BLOCK'
echo 'RESULT=R42_7_ACQUISITION_TRUST_POLICY_PASS_BLOCK'
