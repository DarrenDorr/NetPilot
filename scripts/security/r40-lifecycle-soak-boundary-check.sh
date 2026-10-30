#!/usr/bin/env bash
set -euo pipefail

soak='scripts/security/r40-lifecycle-soak.ps1'
prod='src/NetPilot.Service/Program.cs'
installer='installer/NetPilot.iss'
launcher='src/NetPilot.Engine.Zapret.Windows/SystemZapretProcessLauncher.cs'
worker='src/NetPilot.Service/PrivilegedIpcWorker.cs'

for file in "$soak" "$prod" "$installer" "$launcher" "$worker"; do
  test -f "$file"
done

# Production remains fail-closed.
grep -Fq 'NoAdmittedWindowsVerifiedEngineProcessLauncher.Instance' "$prod"
grep -Fq 'NoAdmittedWindowsByeDpiRuntimePlanProvider.Instance' "$prod"

# Mandatory crash containment remains present for hosted crash/restart soak stages.
grep -Fq 'WindowsKillOnCloseJob.Attach(process)' "$launcher"
grep -Fq 'ProcessContainmentFailed' "$launcher"

# Engine-aware ownership remains present for hosted second-SID handoff stages.
grep -Fq 'TryAcquire(clientSid, ByeDpiResourceId)' "$worker"
grep -Fq 'IsOwnedBy(clientSid, ByeDpiResourceId)' "$worker"
grep -Fq 'EngineOwnedByAnotherUser' "$worker"

# The committed soak runner is fixed and cannot be widened through runtime arguments.
grep -Fq 'if ($args.Count -ne 0)' "$soak"
grep -Fq '$gracefulCycles = 8' "$soak"
grep -Fq '$serviceName = '\''NetPilot.R40.Lab'\''' "$soak"
grep -Fq '$proxyPort = 39036' "$soak"
grep -Fq 'NetPilot.ByeDpiR36LabClient.exe' "$soak"

# Committed runner is typed-client-only; direct native launch/argv construction is forbidden.
if grep -Eqi 'SystemZapretProcessLauncher|WindowsCompiledEngineArgumentPlan|ProcessStartInfo|Start-Process|&[[:space:]][^[:space:]]*ciadpi\.exe|--bind|--port' "$soak"; then
  echo 'R40 boundary violation: committed soak runner crossed direct native launch/argv boundary.' >&2
  exit 1
fi

# No proxy/WinDivert/system-network mutation is permitted in this explicit loopback soak.
if grep -Eqi 'WinDivert|Internet Settings|ProxyEnable|ProxyServer|netsh[[:space:]].*winhttp|Set-NetIP|New-NetFirewall|Set-NetFirewall' "$soak"; then
  echo 'R40 boundary violation: committed soak runner attempts forbidden system network mutation.' >&2
  exit 1
fi

# Lab assets must never ship in production installer.
if grep -Eqi 'R40|lifecycle-soak|ByeDpiR39RestartClient|ByeDpiR36LabClient' "$installer"; then
  echo 'R40 boundary violation: production installer references lab-only assets.' >&2
  exit 1
fi

echo 'R40_LIFECYCLE_SOAK_BOUNDARY=PASS'
