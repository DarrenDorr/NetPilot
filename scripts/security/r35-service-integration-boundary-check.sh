#!/usr/bin/env bash
set -euo pipefail

program='src/NetPilot.Service/Program.cs'
worker='src/NetPilot.Service/PrivilegedIpcWorker.cs'
session='src/NetPilot.Platform.Windows/WindowsByeDpiRuntimeSession.cs'
protocol='src/NetPilot.Ipc/IpcProtocol.cs'
validator='src/NetPilot.Ipc/IpcRequestValidator.cs'
ownership='src/NetPilot.Security/UserResourceOwnership.cs'
argv_catalog='src/NetPilot.Engine.Abstractions/EngineArgumentPlanContracts.cs'

for file in "$program" "$worker" "$session" "$protocol" "$validator" "$ownership" "$argv_catalog"; do
  test -f "$file"
done

grep -Fq 'NoAdmittedWindowsVerifiedEngineProcessLauncher.Instance' "$program"
grep -Fq 'NoAdmittedWindowsByeDpiRuntimePlanProvider.Instance' "$program"
grep -Fq 'WindowsByeDpiRuntimeSession' "$program"

# Production Service must not receive the executable system launcher before admission.
if grep -Fq 'SystemZapretProcessLauncher' "$program"; then
  echo 'R35 boundary violation: production Service references the executable system launcher.' >&2
  exit 1
fi

# R34 PASS allows a typed authenticated IPC surface, but not arbitrary payload.
grep -Fq 'ByeDpiGetStatus' "$protocol"
grep -Fq 'ByeDpiStart' "$protocol"
grep -Fq 'ByeDpiStop' "$protocol"
grep -Fq 'CreateByeDpiGetStatus()' "$protocol"
grep -Fq 'CreateByeDpiStart()' "$protocol"
grep -Fq 'CreateByeDpiStop()' "$protocol"
grep -Fq 'IpcCommand.ByeDpiGetStatus => ValidateNoPayload(request)' "$validator"
grep -Fq 'IpcCommand.ByeDpiStart => ValidateNoPayload(request)' "$validator"
grep -Fq 'IpcCommand.ByeDpiStop => ValidateNoPayload(request)' "$validator"
grep -Fq 'case IpcCommand.ByeDpiGetStatus:' "$worker"
grep -Fq 'case IpcCommand.ByeDpiStart:' "$worker"
grep -Fq 'case IpcCommand.ByeDpiStop:' "$worker"

# IPC must never expose native process identity or listener details in the ByeDPI DTO.
if grep -Eq 'record IpcByeDpiStatus\([^)]*(ProcessId|Port|Path|Sid|Owner)' "$protocol"; then
  echo 'R35 boundary violation: ByeDPI IPC status exposes privileged runtime details.' >&2
  exit 1
fi

# Cross-engine lease must be resource-scoped; legacy no-resource operations are Zapret-only.
grep -Fq 'LegacyZapretResourceId = "zapret"' "$ownership"
grep -Fq 'TryAcquire(clientSid, ByeDpiResourceId)' "$worker"
grep -Fq 'Clear(ByeDpiResourceId)' "$worker"

# The service integration wrapper itself must never own process creation.
if grep -Eq 'Process\.Start|System\.Diagnostics' "$session"; then
  echo 'R35 boundary violation: service runtime session gained a process-creation surface.' >&2
  exit 1
fi

# No raw runtime material may enter the plan provider contract.
if grep -Eq 'CreatePlan\([^)]*(string|path|argv|url|environment|port|address)' "$session"; then
  echo 'R35 boundary violation: ByeDPI plan provider accepts runtime-controlled material.' >&2
  exit 1
fi

# Production typed plan catalog remains empty until an explicit admission decision.
grep -Fq 'Array.AsReadOnly(Array.Empty<EngineArgumentPlanDeclaration>())' "$argv_catalog"

echo 'R35_SERVICE_INTEGRATION_BOUNDARY=PASS'
