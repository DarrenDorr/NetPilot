#!/usr/bin/env bash
set -euo pipefail

client='tools/NetPilot.ByeDpiR39RestartClient/Program.cs'
launcher='src/NetPilot.Engine.Zapret.Windows/SystemZapretProcessLauncher.cs'
worker='src/NetPilot.Service/PrivilegedIpcWorker.cs'
ownership='src/NetPilot.Security/UserResourceOwnership.cs'
prod='src/NetPilot.Service/Program.cs'
installer='installer/NetPilot.iss'

for file in "$client" "$launcher" "$worker" "$ownership" "$prod" "$installer"; do
  test -f "$file"
done

# Production execution remains fail-closed.
grep -Fq 'NoAdmittedWindowsVerifiedEngineProcessLauncher.Instance' "$prod"
grep -Fq 'NoAdmittedWindowsByeDpiRuntimePlanProvider.Instance' "$prod"

# Restart safety still depends on mandatory OS process containment.
grep -Fq 'WindowsKillOnCloseJob.Attach(process)' "$launcher"
grep -Fq 'ProcessContainmentFailed' "$launcher"
grep -Fq 'TerminateUncontainedProcessBestEffort(process)' "$launcher"

# Engine-aware ownership is still the only IPC lease boundary.
grep -Fq 'TryAcquire(clientSid, ByeDpiResourceId)' "$worker"
grep -Fq 'IsOwnedBy(clientSid, ByeDpiResourceId)' "$worker"
grep -Fq 'EngineOwnedByAnotherUser' "$worker"
grep -Fq 'Clear(ByeDpiResourceId)' "$worker"
grep -Fq 'LegacyZapretResourceId = "zapret"' "$ownership"

# R39 probe is typed IPC-only. Service/process control belongs to the external lab workflow.
grep -Fq 'new WindowsPrivilegedServiceClient()' "$client"
grep -Fq 'GetByeDpiStatusAsync' "$client"
grep -Fq 'StartByeDpiAsync' "$client"
grep -Fq 'StopByeDpiAsync' "$client"
grep -Fq 'EngineOwnedByAnotherUser' "$client"
if grep -Eq 'Process\.Start|Process\.Kill|Stop-Process|Start-Service|Stop-Service|Restart-Service|ServiceController|ciadpi\.exe|SystemZapretProcessLauncher|WindowsCompiledEngineArgumentPlan|WindowsKillOnCloseJob' "$client"; then
  echo 'R39 boundary violation: restart probe crossed service/native process boundary.' >&2
  exit 1
fi

# Lab-only restart probe must never ship in the production installer.
if grep -Eq 'R39RestartClient|ByeDpiR39|R39_POST_RESTART|R39_FRESH_OWNER' "$installer"; then
  echo 'R39 boundary violation: production installer references restart reconciliation probe.' >&2
  exit 1
fi

echo 'R39_RESTART_RECONCILIATION_BOUNDARY=PASS'
