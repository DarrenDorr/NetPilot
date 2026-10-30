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

# Mandatory crash containment remains present.
grep -Fq 'WindowsKillOnCloseJob.Attach(process)' "$launcher"
grep -Fq 'ProcessContainmentFailed' "$launcher"

# Engine-aware ownership remains present.
grep -Fq 'TryAcquire(clientSid, ByeDpiResourceId)' "$worker"
grep -Fq 'IsOwnedBy(clientSid, ByeDpiResourceId)' "$worker"
grep -Fq 'EngineOwnedByAnotherUser' "$worker"

# R40 load shape is fixed and cannot be widened through runtime arguments.
grep -Fq "if (\$args.Count -ne 0)" "$soak"
grep -Fq '\$gracefulCycles = 8' "$soak"
grep -Fq '\$crashCycles = 4' "$soak"
grep -Fq '\$handoffCycles = 4' "$soak"
grep -Fq "\$serviceName = 'NetPilot.R40.Lab'" "$soak"
grep -Fq "\$secondUserName = 'NetPilotR40B'" "$soak"
grep -Fq '\$proxyPort = 39036' "$soak"

# Soak must use existing typed IPC clients; it must not launch ciadpi directly or construct argv.
grep -Fq 'NetPilot.ByeDpiR36LabClient.exe' "$soak"
grep -Fq 'NetPilot.ByeDpiR39RestartClient.exe' "$soak"
if grep -Eq 'ciadpi\.exe["'"']|SystemZapretProcessLauncher|WindowsCompiledEngineArgumentPlan|ProcessStartInfo|--bind|--port|-i[[:space:]]+127\.0\.0\.1|-p[[:space:]]+39036' "$soak"; then
  echo 'R40 boundary violation: soak script crossed direct native launch/argv boundary.' >&2
  exit 1
fi

# No proxy/WinDivert/system-network mutation is permitted in this explicit loopback soak.
if grep -Eqi 'WinDivert|Internet Settings|ProxyEnable|ProxyServer|netsh[[:space:]].*winhttp|Set-NetIP|New-NetFirewall|Set-NetFirewall' "$soak"; then
  echo 'R40 boundary violation: soak script attempts forbidden system network mutation.' >&2
  exit 1
fi

# Lab assets must never ship in production installer.
if grep -Eqi 'R40|lifecycle-soak|ByeDpiR39RestartClient|ByeDpiR36LabClient' "$installer"; then
  echo 'R40 boundary violation: production installer references lab-only assets.' >&2
  exit 1
fi

echo 'R40_LIFECYCLE_SOAK_BOUNDARY=PASS'
