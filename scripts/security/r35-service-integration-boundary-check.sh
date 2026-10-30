#!/usr/bin/env bash
set -euo pipefail

program='src/NetPilot.Service/Program.cs'
worker='src/NetPilot.Service/PrivilegedIpcWorker.cs'
session='src/NetPilot.Platform.Windows/WindowsByeDpiRuntimeSession.cs'
argv_catalog='src/NetPilot.Engine.Abstractions/EngineArgumentPlanContracts.cs'

for file in "$program" "$worker" "$session" "$argv_catalog"; do
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

# R35 pre-admission scaffold must not yet expose ByeDPI commands over authenticated IPC.
if grep -Fq 'ByeDpi' "$worker"; then
  echo 'R35 boundary violation: ByeDPI IPC dispatch was exposed before R34 runtime evidence/admission review.' >&2
  exit 1
fi

# The service integration wrapper itself must never own process creation.
if grep -Eq 'Process\.Start|System\.Diagnostics' "$session"; then
  echo 'R35 boundary violation: service runtime session gained a process-creation surface.' >&2
  exit 1
fi

# Production typed plan catalog remains empty until an explicit admission decision.
grep -Fq 'Array.AsReadOnly(Array.Empty<EngineArgumentPlanDeclaration>())' "$argv_catalog"

echo 'R35_SERVICE_INTEGRATION_BOUNDARY=PASS'
