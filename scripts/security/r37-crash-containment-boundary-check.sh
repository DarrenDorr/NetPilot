#!/usr/bin/env bash
set -euo pipefail

launcher='src/NetPilot.Engine.Zapret.Windows/SystemZapretProcessLauncher.cs'
job='src/NetPilot.Platform.Windows/WindowsKillOnCloseJob.cs'
service='src/NetPilot.Service/Program.cs'

for file in "$launcher" "$job" "$service"; do
  test -f "$file"
done

# Production service remains no-admit while crash containment is being hardened.
grep -Fq 'NoAdmittedWindowsVerifiedEngineProcessLauncher.Instance' "$service"
grep -Fq 'NoAdmittedWindowsByeDpiRuntimePlanProvider.Instance' "$service"
if grep -Fq 'SystemZapretProcessLauncher' "$service"; then
  echo 'R37 boundary violation: production service received executable launcher.' >&2
  exit 1
fi

# Preserve the single audited native process-creation point.
process_pattern='Process[[:space:]]*\.Start[[:space:]]*\('
all_matches="$(grep -RniE --include='*.cs' --exclude-dir=bin --exclude-dir=obj "$process_pattern" src 2>/dev/null || true)"
unexpected="$(printf '%s\n' "$all_matches" | grep -v "^${launcher}:" || true)"
if [[ -n "$unexpected" ]]; then
  printf '%s\n' "$unexpected"
  echo 'R37 boundary violation: additional Process.Start exists outside audited launcher.' >&2
  exit 1
fi
if [[ "$(grep -cE "$process_pattern" "$launcher" || true)" -ne 1 ]]; then
  echo 'R37 boundary violation: audited launcher must contain exactly one Process.Start.' >&2
  exit 1
fi

# Every accepted launch must cross the kill-on-owner-close boundary.
grep -Fq 'StartContainedProcess(startInfo)' "$launcher"
grep -Fq 'WindowsKillOnCloseJob.Attach(process)' "$launcher"
grep -Fq 'ProcessContainmentFailed' "$launcher"
grep -Fq 'TerminateUncontainedProcessBestEffort(process)' "$launcher"

# The containment primitive must remain the exact kernel Job Object policy.
grep -Fq 'JobObjectLimitKillOnJobClose = 0x00002000' "$job"
grep -Fq 'CreateJobObjectW' "$job"
grep -Fq 'SetInformationJobObject' "$job"
grep -Fq 'AssignProcessToJobObject' "$job"
grep -Fq 'JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE' "$job"

# Containment helper must not become a second process-launch surface.
if grep -Eq 'Process[[:space:]]*\.Start[[:space:]]*\(' "$job"; then
  echo 'R37 boundary violation: Job Object helper gained Process.Start.' >&2
  exit 1
fi

# Fail closed: if job creation/policy/assignment fails, no uncontained engine may continue.
grep -Fq 'JobCreateFailed' "$job"
grep -Fq 'JobPolicyFailed' "$job"
grep -Fq 'JobAssignmentFailed' "$job"
grep -Fq 'process.Kill(entireProcessTree: true)' "$launcher"

echo 'R37_CRASH_CONTAINMENT_BOUNDARY=PASS'
