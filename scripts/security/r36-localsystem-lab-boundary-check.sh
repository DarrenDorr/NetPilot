#!/usr/bin/env bash
set -euo pipefail

prod_service='src/NetPilot.Service/Program.cs'
lab_service='tools/NetPilot.ByeDpiR36ServiceLab/Program.cs'
lab_client='tools/NetPilot.ByeDpiR36LabClient/Program.cs'
installer='installer/NetPilot.iss'
argv_catalog='src/NetPilot.Engine.Abstractions/EngineArgumentPlanContracts.cs'
identity_catalog='src/NetPilot.Engine.Abstractions/EngineExecutableIdentityContracts.cs'
layout_catalog='src/NetPilot.Engine.Abstractions/EnginePackageLayoutContracts.cs'

for file in "$prod_service" "$lab_service" "$lab_client" "$installer" "$argv_catalog" "$identity_catalog" "$layout_catalog"; do
  test -f "$file"
done

# Production service remains incapable of native ByeDPI execution.
grep -Fq 'NoAdmittedWindowsVerifiedEngineProcessLauncher.Instance' "$prod_service"
grep -Fq 'NoAdmittedWindowsByeDpiRuntimePlanProvider.Instance' "$prod_service"
if grep -Fq 'SystemZapretProcessLauncher' "$prod_service"; then
  echo 'R36 boundary violation: production service received executable launcher.' >&2
  exit 1
fi

# The lab service is a separate, fixed LocalSystem-only composition.
grep -Fq 'ServiceName = "NetPilot.R36.Lab"' "$lab_service"
grep -Fq 'internal const ushort ProxyPort = 39036' "$lab_service"
grep -Fq 'identity.IsSystem' "$lab_service"
grep -Fq 'args.Length != 0' "$lab_service"
grep -Fq 'ByeDpiR34LabContract.Create(Program.ProxyPort)' "$lab_service"
grep -Fq 'IWindowsVerifiedEngineProcessLauncher, SystemZapretProcessLauncher' "$lab_service"
grep -Fq 'R36ByeDpiRuntimePlanProvider' "$lab_service"

# No user-controlled native material in the lab service provider.
if grep -Eq 'Environment\.GetEnvironmentVariable|Console\.Read|args\[[0-9]+\]|GetCommandLineArgs|http://|https://' "$lab_service"; then
  echo 'R36 boundary violation: lab service accepts dynamic runtime material.' >&2
  exit 1
fi

# Client may choose only fixed test modes and must control engine exclusively over typed IPC.
grep -Fq 'new WindowsPrivilegedServiceClient()' "$lab_client"
grep -Fq 'StartByeDpiAsync' "$lab_client"
grep -Fq 'StopByeDpiAsync' "$lab_client"
grep -Fq 'GetByeDpiStatusAsync' "$lab_client"
grep -Fq 'private const int ProxyPort = 39036' "$lab_client"
if grep -Eq 'Process\.Start|ciadpi\.exe|SystemZapretProcessLauncher|WindowsCompiledEngineArgumentPlan' "$lab_client"; then
  echo 'R36 boundary violation: lab client crossed the privileged engine boundary.' >&2
  exit 1
fi

# Production installer must not ship or register the lab host/client.
if grep -Eq 'R36|ByeDpiR36|NetPilot\.R36\.Lab' "$installer"; then
  echo 'R36 boundary violation: production installer references lab components.' >&2
  exit 1
fi

# Production catalogs remain empty.
grep -Fq 'Array.AsReadOnly(Array.Empty<EngineArgumentPlanDeclaration>())' "$argv_catalog"
grep -Fq 'Array.AsReadOnly(Array.Empty<EngineExecutableIdentityDeclaration>())' "$identity_catalog"
grep -Fq 'Array.AsReadOnly(Array.Empty<EnginePackageLayoutDeclaration>())' "$layout_catalog"

echo 'R36_LOCALSYSTEM_LAB_BOUNDARY=PASS'
