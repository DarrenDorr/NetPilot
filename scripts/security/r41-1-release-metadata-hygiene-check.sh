#!/usr/bin/env bash
set -euo pipefail

manifest='release/release-manifest.yaml'
build='scripts/build-installer.ps1'
installer='installer/NetPilot.iss'
installer_readme='installer/README.md'
notices='docs/THIRD-PARTY-NOTICES.md'
byedpi_license='third_party/licenses/ByeDPI-MIT.txt'
r41_guard='scripts/security/r41-byedpi-production-readiness-check.sh'

for file in "$manifest" "$build" "$installer" "$installer_readme" "$notices" "$byedpi_license" "$r41_guard"; do
  test -f "$file"
done

# R41 fail-closed admission boundary remains a prerequisite.
bash "$r41_guard" >/tmp/netpilot-r41-guard.txt
for marker in \
  'R41_BYEDPI_PRODUCTION_READINESS=BLOCK_CONFIRMED' \
  'R41_PRODUCTION_SERVICE_NO_ADMIT=PASS' \
  'R41_PRODUCTION_CATALOGS_EMPTY=PASS' \
  'RESULT=R41_READINESS_REVIEW_PASS_BLOCK'; do
  grep -Fq "$marker" /tmp/netpilot-r41-guard.txt
done
rm -f /tmp/netpilot-r41-guard.txt

# Canonical repository/support/update metadata.
grep -Fq 'repository_url: https://github.com/DarrenDorr/NetPilot' "$manifest"
grep -Fq 'support_url: https://github.com/DarrenDorr/NetPilot/issues' "$manifest"
grep -Fq 'updates_url: https://github.com/DarrenDorr/NetPilot/releases' "$manifest"
grep -Fq '#define ProjectUrl "https://github.com/DarrenDorr/NetPilot"' "$installer"
if grep -Fq 'raybalado989-lang/NetPilot' "$installer"; then
  echo 'R41.1 violation: stale installer repository URL remains.' >&2
  exit 1
fi

# Inno Setup identity must match manifest, executable build script and current installer docs.
grep -Fq 'version: 7.1.1' "$manifest"
grep -Fq 'sha256: 8a5a6711c3766a6e5fb74f280ba0e3551e719ce94538d901fdbd1f8f9920b3f0' "$manifest"
grep -Fq 'innosetup-7.1.1-x64.exe' "$manifest"
grep -Fq '$innoInstallDir = Join-Path $toolsDir "inno-7.1.1"' "$build"
grep -Fq '$innoInstaller = Join-Path $toolsDir "innosetup-7.1.1-x64.exe"' "$build"
grep -Fq '$innoSha256 = "8a5a6711c3766a6e5fb74f280ba0e3551e719ce94538d901fdbd1f8f9920b3f0"' "$build"
grep -Fq 'Inno Setup 7.1.1 x64' "$installer_readme"
grep -Fq 'Закреплённая версия сборочного инструмента: 7.1.1 x64' "$notices"

# Exact upstream ByeDPI MIT notice is preserved for later release packaging design.
grep -Fq 'license: MIT' "$manifest"
grep -Fq 'copyright: Copyright (c) 2024 hufrea' "$manifest"
grep -Fq 'production_distribution: not_shipped' "$manifest"
grep -Fq 'production_admission: BLOCK' "$manifest"
grep -Fq 'Copyright (c) 2024 hufrea' "$byedpi_license"
actual_license_sha="$(sha256sum "$byedpi_license" | awk '{print $1}')"
if [[ "$actual_license_sha" != '18c854c6c25dffefe4b522cf1bdf5d15278a25ca785948258bddb94393f575eb' ]]; then
  echo "R41.1 violation: ByeDPI MIT notice hash drift: $actual_license_sha" >&2
  exit 1
fi
grep -Fq 'На этапе R41.1 ByeDPI **не входит в production installer и не имеет production ADMIT**.' "$notices"

# Release hygiene must not become product activation.
grep -Fq 'byedpi_positive_catalog_entry: false' "$manifest"
grep -Fq 'byedpi_installer_payload: false' "$manifest"
grep -Fq 'byedpi_runtime_network_updater: false' "$manifest"
grep -Fq 'transparent_windows_routing_authorized: false' "$manifest"
grep -Fq 'windivert_authorized_by_byedpi: false' "$manifest"
if grep -Eqi 'ByeDPI|ciadpi' "$installer"; then
  echo 'R41.1 violation: installer began shipping/referencing ByeDPI while admission is BLOCK.' >&2
  exit 1
fi

echo 'R41_1_RELEASE_METADATA_SYNC=PASS'
echo 'R41_1_BYEDPI_LICENSE_NOTICE=PASS'
echo 'R41_1_PRODUCTION_ADMISSION=BLOCK'
echo 'R41_1_NATIVE_EXECUTION_TESTED=False'
echo 'RESULT=R41_1_RELEASE_HYGIENE_PASS_BLOCK'
