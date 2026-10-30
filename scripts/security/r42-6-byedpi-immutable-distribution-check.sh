#!/usr/bin/env bash
set -euo pipefail

doc='src/NetPilot.Updater/ByeDpiImmutableReleaseDocument.cs'
tests='tests/NetPilot.Core.Tests/ByeDpiImmutableReleaseDocumentTests.cs'
contract='security/native-admission/byedpi-immutable-distribution-r42-6.yaml'
service='src/NetPilot.Service/Program.cs'
worker='src/NetPilot.Service/PrivilegedIpcWorker.cs'
installer='installer/NetPilot.iss'

for file in "$doc" "$tests" "$contract" "$service" "$worker" "$installer"; do
  test -f "$file"
done

bash scripts/security/r42-5-byedpi-provenance-package-check.sh >/tmp/netpilot-r42-6-parent.txt
for marker in \
  'R42_5_PARENT_R42_4_GUARD=PASS' \
  'R42_5_STRICT_MANIFEST_DOCUMENT=PASS' \
  'R42_5_EXACT_REVIEWED_IDENTITIES=PASS' \
  'R42_5_DETERMINISTIC_PACKAGE_CONTRACT=PASS' \
  'R42_5_NO_PRODUCT_NETWORK_OR_EXECUTION=PASS' \
  'R42_5_RELEASE_PUBLICATION=NOT_IMPLEMENTED' \
  'R42_5_PRODUCT_DOWNLOADER=NOT_IMPLEMENTED' \
  'R42_5_PRODUCTION_ADMISSION=BLOCK' \
  'RESULT=R42_5_PROVENANCE_PACKAGE_PASS_BLOCK'; do
  grep -Fq "$marker" /tmp/netpilot-r42-6-parent.txt
done
rm -f /tmp/netpilot-r42-6-parent.txt

echo 'R42_6_PARENT_R42_5_GUARD=PASS'

for exact in \
  'DarrenDorr' \
  'NetPilot' \
  'byedpi-v0.17.3' \
  '7efde1b1296eaaa187b70e951894dde17527489c' \
  '8d13f6f5939190cd27ee68696247073a00b3e7dc' \
  'NetPilot-ByeDPI-0.17.3-x64.zip' \
  'c0fe998e3d81ccb871f09d126e7b5ee0ab82d15daf78bc44bcd63b7762313db7' \
  'NetPilot-ByeDPI-0.17.3-x64.manifest.json' \
  'eaa6f063fcb8e012810f849ed08b6bd82d4d046d4796c2fb4b903c4195985f54' \
  'NetPilot-ByeDPI-0.17.3-x64.provenance.json' \
  'e1f98760eed94f5daa7d37a868015755220cb17d389000bc49e031da511f7f31'; do
  grep -Fq "$exact" "$contract"
done

echo 'R42_6_EXACT_IMMUTABLE_RELEASE_IDENTITY=PASS'

for marker in \
  'ReadExactObject' \
  'duplicate property' \
  'ValidateCanonicalVersion' \
  'ValidateLowercaseHex' \
  'RequiredRepositoryOwner = "DarrenDorr"' \
  'RequiredRepositoryName = "NetPilot"'; do
  grep -Fq "$marker" "$doc"
done

for test_name in \
  'DeterministicRoundTripPreservesExactReleaseIdentity' \
  'UnknownRootPropertyIsRejected' \
  'DuplicatePropertyIsRejected' \
  'RepositoryDriftIsRejected' \
  'ReleaseTagDriftIsRejected' \
  'NonCanonicalVersionIsRejected' \
  'AssetNameDriftIsRejected' \
  'UppercaseAssetHashIsRejected' \
  'NonPositiveAssetSizeIsRejected'; do
  grep -Fq "$test_name" "$tests"
done

echo 'R42_6_STRICT_DISTRIBUTION_DOCUMENT=PASS'

for forbidden in \
  'HttpClient' \
  'WebClient' \
  'Process.Start' \
  'ProcessStartInfo' \
  'WinDivert' \
  'Download' \
  'Upload'; do
  if grep -Fq "$forbidden" "$doc"; then
    echo "R42.6 violation: immutable descriptor gained forbidden primitive: $forbidden" >&2
    exit 1
  fi
done

echo 'R42_6_NO_NETWORK_OR_EXECUTION_PRIMITIVES=PASS'

if grep -Fq 'ByeDpiImmutableReleaseDocument' "$service" "$worker"; then
  echo 'R42.6 violation: immutable release descriptor was wired into Service/IPC.' >&2
  exit 1
fi
if grep -Eqi 'ByeDPI|ciadpi' "$installer"; then
  echo 'R42.6 violation: installer references ByeDPI while production admission is BLOCK.' >&2
  exit 1
fi

grep -Fq 'release_creation_or_upload: NOT_IMPLEMENTED' "$contract"
grep -Fq 'product_downloader: NOT_IMPLEMENTED' "$contract"
grep -Fq 'network_acquisition: NOT_IMPLEMENTED' "$contract"
grep -Fq 'service_or_ipc_wiring: NOT_IMPLEMENTED' "$contract"
grep -Fq 'native_execution: NOT_IMPLEMENTED' "$contract"
grep -Fq 'production_admission: BLOCK' "$contract"

echo 'R42_6_RELEASE_CREATION_OR_UPLOAD=NOT_IMPLEMENTED'
echo 'R42_6_PRODUCT_DOWNLOADER=NOT_IMPLEMENTED'
echo 'R42_6_NETWORK_ACQUISITION=NOT_IMPLEMENTED'
echo 'R42_6_SERVICE_IPC_WIRING=ABSENT'
echo 'R42_6_NATIVE_EXECUTION_TESTED=False'
echo 'R42_6_PRODUCTION_ADMISSION=BLOCK'
echo 'RESULT=R42_6_IMMUTABLE_DISTRIBUTION_PASS_BLOCK'
