#!/usr/bin/env bash
set -euo pipefail

fail=0

forbidden_runtime_refs="$(grep -RniE \
  --include='*.cs' --include='*.axaml' --include='*.csproj' \
  --exclude-dir=bin --exclude-dir=obj \
  'ByeDpiR34LabContract|EngineLabProfileCatalog|NetPilot\.ByeDpiR34Lab' \
  src/NetPilot.App src/NetPilot.App.Windows src/NetPilot.Automation src/NetPilot.Service installer \
  2>/dev/null || true)"

if [[ -n "$forbidden_runtime_refs" ]]; then
  printf '%s\n' "$forbidden_runtime_refs"
  echo 'R34 SECURITY FAIL: lab-only contract leaked into production App/Automation/Service/installer.'
  fail=1
fi

lab_tool='tools/NetPilot.ByeDpiR34Lab/Program.cs'
if [[ ! -f "$lab_tool" ]]; then
  echo 'R34 SECURITY FAIL: lab host is missing.'
  fail=1
else
  if grep -qE 'Process[[:space:]]*\.Start[[:space:]]*\(' "$lab_tool"; then
    echo 'R34 SECURITY FAIL: lab host must use the single audited SystemZapretProcessLauncher boundary.'
    fail=1
  fi

  for forbidden in \
    'cmd\.exe' \
    'powershell\.exe' \
    'pwsh\.exe' \
    'UseShellExecute[[:space:]]*=[[:space:]]*true' \
    'WinDivert(Open|Close|Send|Recv)' \
    'InternetSetOption' \
    'Registry\.' \
    'netsh'; do
    if grep -qE "$forbidden" "$lab_tool"; then
      echo "R34 SECURITY FAIL: forbidden lab-host surface matched: $forbidden"
      fail=1
    fi
  done

  if ! grep -qF 'Environment.GetCommandLineArgs().Length != 1' "$lab_tool"; then
    echo 'R34 SECURITY FAIL: lab host must reject all command-line arguments.'
    fail=1
  fi
fi

for catalog_file in \
  src/NetPilot.Engine.Abstractions/EngineExecutableIdentityContracts.cs \
  src/NetPilot.Engine.Abstractions/EnginePackageLayoutContracts.cs \
  src/NetPilot.Engine.Abstractions/EngineResourceBudgetContracts.cs \
  src/NetPilot.Engine.Abstractions/EngineArgumentPlanContracts.cs; do
  if ! grep -q 'Array\.Empty<' "$catalog_file"; then
    echo "R34 SECURITY FAIL: production promotion catalog no longer appears empty: $catalog_file"
    fail=1
  fi
done

if [[ "$fail" -ne 0 ]]; then
  exit 1
fi

echo 'R34 lab boundary check passed.'
