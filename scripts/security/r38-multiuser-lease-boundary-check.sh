#!/usr/bin/env bash
set -euo pipefail

client='tools/NetPilot.ByeDpiR38LeaseClient/Program.cs'
worker='src/NetPilot.Service/PrivilegedIpcWorker.cs'
ownership='src/NetPilot.Security/UserResourceOwnership.cs'
prod='src/NetPilot.Service/Program.cs'
installer='installer/NetPilot.iss'

for file in "$client" "$worker" "$ownership" "$prod" "$installer"; do
  test -f "$file"
done

# Product execution remains no-admit.
grep -Fq 'NoAdmittedWindowsVerifiedEngineProcessLauncher.Instance' "$prod"
grep -Fq 'NoAdmittedWindowsByeDpiRuntimePlanProvider.Instance' "$prod"

# Engine-aware lease is the product boundary under test.
grep -Fq 'TryAcquire(clientSid, ByeDpiResourceId)' "$worker"
grep -Fq 'IsOwnedBy(clientSid, ByeDpiResourceId)' "$worker"
grep -Fq 'EngineOwnedByAnotherUser' "$worker"
grep -Fq 'LegacyZapretResourceId = "zapret"' "$ownership"
grep -Fq 'Clear(ByeDpiResourceId)' "$worker"

# Probe is typed IPC-only and does not cross the native/process boundary.
grep -Fq 'new WindowsPrivilegedServiceClient()' "$client"
grep -Fq 'GetByeDpiStatusAsync' "$client"
grep -Fq 'StartByeDpiAsync' "$client"
grep -Fq 'StopByeDpiAsync' "$client"
grep -Fq 'EngineOwnedByAnotherUser' "$client"
if grep -Eq 'Process\.Start|ciadpi\.exe|SystemZapretProcessLauncher|WindowsCompiledEngineArgumentPlan|WindowsKillOnCloseJob' "$client"; then
  echo 'R38 boundary violation: multi-user probe crossed privileged/native process boundary.' >&2
  exit 1
fi

# Lab probe must never leak into release installer.
if grep -Eq 'R38LeaseClient|ByeDpiR38|R38_NONOWNER' "$installer"; then
  echo 'R38 boundary violation: production installer references multi-user lab probe.' >&2
  exit 1
fi

echo 'R38_MULTIUSER_LEASE_BOUNDARY=PASS'
