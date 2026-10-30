#!/usr/bin/env bash
set -euo pipefail

manifest_doc='src/NetPilot.Updater/ByeDpiPackageManifestDocument.cs'
manifest_tests='tests/NetPilot.Core.Tests/ByeDpiPackageManifestDocumentTests.cs'
builder='tools/NetPilot.ByeDpiR42PackageBuilder/Program.cs'
contract='security/native-admission/byedpi-provenance-package-r42-5.yaml'
service='src/NetPilot.Service/Program.cs'
worker='src/NetPilot.Service/PrivilegedIpcWorker.cs'
installer='installer/NetPilot.iss'

for file in "$manifest_doc" "$manifest_tests" "$builder" "$contract" "$service" "$worker" "$installer"; do
  test -f "$file"
done

bash scripts/security/r42-4-byedpi-repair-uninstall-check.sh >/tmp/netpilot-r42-5-parent.txt
for marker in \
  'R42_4_PARENT_R42_3_GUARD=PASS' \
  'R42_4_TYPED_LOCALSYSTEM_BOUNDARY=PASS' \
  'R42_4_EXACT_REPAIR_BOUNDARY=PASS' \
  'R42_4_TARGET_FREE_UNINSTALL=PASS' \
  'R42_4_CRASH_RECOVERY_SURFACE=PASS' \
  'R42_4_NO_PROCESS_OR_NETWORK_PRIMITIVES=PASS' \
  'R42_4_SERVICE_IPC_WIRING=ABSENT' \
  'R42_4_NATIVE_EXECUTION_TESTED=False' \
  'R42_4_PRODUCTION_ADMISSION=BLOCK' \
  'RESULT=R42_4_REPAIR_UNINSTALL_PASS_BLOCK'; do
  grep -Fq "$marker" /tmp/netpilot-r42-5-parent.txt
done
rm -f /tmp/netpilot-r42-5-parent.txt

echo 'R42_5_PARENT_R42_4_GUARD=PASS'

# Strict manifest document must be deterministic and reconstruct semantic manifest before trusting persisted digest.
grep -Fq 'public static byte[] Serialize(ByeDpiPackageManifest manifest)' "$manifest_doc"
grep -Fq 'public static ByeDpiPackageManifest Parse(ReadOnlyMemory<byte> json)' "$manifest_doc"
grep -Fq 'UnmappedMember' "$manifest_doc" && {
  echo 'R42.5 violation: manifest parser unexpectedly relies on serializer unmapped-member behavior.' >&2
  exit 1
} || true
grep -Fq 'ReadExactObject' "$manifest_doc"
grep -Fq 'duplicate property' "$manifest_doc"
grep -Fq 'manifest.CanonicalManifestSha256' "$manifest_doc"
grep -Fq 'persistedCanonicalDigest' "$manifest_doc"
for test_name in \
  'DeterministicRoundTripPreservesCanonicalManifest' \
  'UnknownRootPropertyIsRejected' \
  'DuplicatePropertyIsRejected' \
  'CanonicalDigestDriftIsRejected' \
  'NonCanonicalFileHashIsRejected'; do
  grep -Fq "$test_name" "$manifest_tests"
done

echo 'R42_5_STRICT_MANIFEST_DOCUMENT=PASS'

# Builder is pinned to the exact reviewed source, patchset and known deterministic Windows binary.
for exact in \
  'hufrea/byedpi' \
  '7efde1b1296eaaa187b70e951894dde17527489c' \
  '8d13f6f5939190cd27ee68696247073a00b3e7dc' \
  '342905d38be83e9d540deae14987896826a74858' \
  '866e99c3c7690326cc6e365fe02980f06ae2b394c045555674fb95db52b0dbdd' \
  '18c854c6c25dffefe4b522cf1bdf5d15278a25ca785948258bddb94393f575eb' \
  '3e9a744830082d224592d730a816c4afcf9df15a2a6ab93870409ccf5c94c068' \
  '6701c48ef00f81ca7fa95f98a1e6406f657fbdfc028371f568c7918106cd5fb7' \
  '5d10702499581eeba2f632aa506b5d6c51ceb3e8e093bff25b8dcbc821bb3738' \
  'fdc13219be9cb9d654ce7995df825decb6ee0f05e63d72481560977bbf3b3c4a' \
  '7c8f4eee9ff2624346cb886027df66378773d0af04f5f84f54c30815104b4356' \
  '136b9fdf34b68a55f0f1a6ebf16e8b61c9b88e6d15eeb923594bdd7bf0fa2a3e' \
  '778f47d3ecf2d0f6c8ce0152766c0eb51e2d6e460923227bed49844ba18c0d25'; do
  grep -Fq "$exact" "$builder"
  grep -Fq "$exact" "$contract"
done

echo 'R42_5_EXACT_REVIEWED_IDENTITIES=PASS'

# Deterministic package contains only the runtime payload; metadata remains external.
grep -Fq 'new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero)' "$builder"
grep -Fq 'WriteEntry(archive, "LICENSE", license, fixedTimestamp)' "$builder"
grep -Fq 'WriteEntry(archive, "bin/ciadpi.exe", binary, fixedTimestamp)' "$builder"
grep -Fq 'CompressionLevel.NoCompression' "$builder"
grep -Fq 'ByeDpiPackageManifestDocument.Serialize' "$builder"
grep -Fq 'VerifyZipExact' "$builder"

echo 'R42_5_DETERMINISTIC_PACKAGE_CONTRACT=PASS'

# R42.5 does not add networking or execution to production code or package builder.
for file in "$manifest_doc" "$builder"; do
  for forbidden in \
    'HttpClient' \
    'WebClient' \
    'Process.Start' \
    'ProcessStartInfo' \
    'WinDivert' \
    'cmd.exe' \
    'powershell.exe'; do
    if grep -Fq "$forbidden" "$file"; then
      echo "R42.5 violation: $file gained forbidden primitive: $forbidden" >&2
      exit 1
    fi
  done
done

echo 'R42_5_NO_PRODUCT_NETWORK_OR_EXECUTION=PASS'

# Product wiring/publication remains absent. Attestation is CI evidence, not runtime admission.
if grep -Fq 'ByeDpiPackageManifestDocument' "$service" "$worker"; then
  echo 'R42.5 violation: manifest acquisition work was wired into Service/IPC.' >&2
  exit 1
fi
if grep -Eqi 'ByeDPI|ciadpi' "$installer"; then
  echo 'R42.5 violation: installer references ByeDPI while admission is BLOCK.' >&2
  exit 1
fi

grep -Fq 'github_artifact_attestation: REQUIRED_FOR_R42_5_CI_EVIDENCE' "$contract"
grep -Fq 'attestation_is_not_security_review: true' "$contract"
grep -Fq 'immutable_release_before_future_product_acquisition: REQUIRED' "$contract"
grep -Fq 'release_publication: NOT_IMPLEMENTED' "$contract"
grep -Fq 'product_downloader: NOT_IMPLEMENTED' "$contract"
grep -Fq 'service_or_ipc_wiring: NOT_IMPLEMENTED' "$contract"
grep -Fq 'native_execution: NOT_IMPLEMENTED' "$contract"
grep -Fq 'production_admission: BLOCK' "$contract"

echo 'R42_5_RELEASE_PUBLICATION=NOT_IMPLEMENTED'
echo 'R42_5_PRODUCT_DOWNLOADER=NOT_IMPLEMENTED'
echo 'R42_5_SERVICE_IPC_WIRING=ABSENT'
echo 'R42_5_NATIVE_EXECUTION_TESTED=False'
echo 'R42_5_PRODUCTION_ADMISSION=BLOCK'
echo 'RESULT=R42_5_PROVENANCE_PACKAGE_PASS_BLOCK'
