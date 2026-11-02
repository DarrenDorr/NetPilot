#!/usr/bin/env bash
set -euo pipefail

IPC='src/NetPilot.Ipc/IpcProtocol.cs'
VALIDATOR='src/NetPilot.Ipc/IpcRequestValidator.cs'
CLIENT='src/NetPilot.Platform.Windows/WindowsPrivilegedServiceClient.cs'
PROVISIONER='src/NetPilot.Platform.Windows/WindowsByeDpiPackageProvisioning.cs'
SERVICE='src/NetPilot.Service/PrivilegedIpcWorker.cs'
PROGRAM='src/NetPilot.Service/Program.cs'
POLICY='src/NetPilot.Security/OperationPolicy.cs'
CORE_TESTS='tests/NetPilot.Core.Tests/IpcProtocolTests.cs'
WINDOWS_TESTS='tests/NetPilot.Platform.Windows.Tests/WindowsByeDpiPackageProvisioningBoundaryTests.cs'
CONTRACT='security/native-admission/byedpi-package-ipc-r42-12.yaml'
DOC='docs/BYEDPI-PACKAGE-IPC-R42-12.md'

for path in "$IPC" "$VALIDATOR" "$CLIENT" "$PROVISIONER" "$SERVICE" "$PROGRAM" "$POLICY" "$CORE_TESTS" "$WINDOWS_TESTS" "$CONTRACT" "$DOC"; do
  test -f "$path" || { echo "missing required R42.12 file: $path" >&2; exit 1; }
done

require_literal() {
  local file="$1"
  local text="$2"
  grep -Fq -- "$text" "$file" || {
    echo "R42.12 guard missing required text in $file: $text" >&2
    exit 1
  }
}

forbid_regex() {
  local file="$1"
  local regex="$2"
  if grep -Eiq -- "$regex" "$file"; then
    echo "R42.12 guard found forbidden pattern in $file: $regex" >&2
    exit 1
  fi
}

require_literal "$IPC" 'public const int Version = 5;'
require_literal "$IPC" 'public const string WindowsPipeName = "NetPilot.Service.v5";'
require_literal "$IPC" 'ByeDpiGetPackageStatus'
require_literal "$IPC" 'ByeDpiApplyReviewedPackage'
require_literal "$IPC" 'CreateByeDpiGetPackageStatus'
require_literal "$IPC" 'CreateByeDpiApplyReviewedPackage'
require_literal "$VALIDATOR" 'IpcCommand.ByeDpiGetPackageStatus => ValidateNoPayload(request)'
require_literal "$VALIDATOR" 'IpcCommand.ByeDpiApplyReviewedPackage => ValidateNoPayload(request)'
require_literal "$CLIENT" 'GetByeDpiPackageStatusAsync'
require_literal "$CLIENT" 'ApplyReviewedByeDpiPackageAsync'
require_literal "$CLIENT" 'IpcProtocol.EngineInstallTimeoutMilliseconds'

require_literal "$PROVISIONER" 'public interface IWindowsByeDpiPackageProvisioner'
require_literal "$PROVISIONER" 'NoAdmittedWindowsByeDpiPackageProvisioner'
require_literal "$PROVISIONER" 'ByeDpiPackageProvisioningNotAdmitted'
require_literal "$PROVISIONER" 'WindowsByeDpiAtomicPackagePromoter.GetState()'

require_literal "$SERVICE" 'case IpcCommand.ByeDpiGetPackageStatus:'
require_literal "$SERVICE" 'case IpcCommand.ByeDpiApplyReviewedPackage:'
require_literal "$SERVICE" 'authorizedUserStore.IsAuthorized(clientSid)'
require_literal "$SERVICE" 'operationPolicy.IsAllowed(PrivilegedOperation.EngineInstall)'
require_literal "$SERVICE" 'byeDpiPackages.ApplyReviewedPackageAsync'
require_literal "$SERVICE" 'engineLeaseActive'
require_literal "$PROGRAM" 'NoAdmittedWindowsByeDpiPackageProvisioner.Instance'
require_literal "$PROGRAM" 'OperationPolicy.CreateNoAdmittedEnginePolicy()'

require_literal "$CORE_TESTS" 'ByeDpi_package_status_and_apply_commands_have_no_user_controlled_target_payload'
require_literal "$CORE_TESTS" 'Framing_round_trip_preserves_typed_ByeDpi_package_response'
require_literal "$WINDOWS_TESTS" 'Service_package_surface_accepts_no_user_target_path_url_version_or_archive'
require_literal "$WINDOWS_TESTS" 'Production_r42_12_provisioner_rejects_package_mutation_fail_closed'

require_literal "$CONTRACT" 'status: SERVICE_IPC_PACKAGE_BOUNDARY_FAIL_CLOSED'
require_literal "$CONTRACT" 'protocol_version: 5'
require_literal "$CONTRACT" 'caller_target_version: FORBIDDEN'
require_literal "$CONTRACT" 'caller_url: FORBIDDEN'
require_literal "$CONTRACT" 'caller_path: FORBIDDEN'
require_literal "$CONTRACT" 'production_implementation: NoAdmittedWindowsByeDpiPackageProvisioner'
require_literal "$CONTRACT" 'live_network_acquisition: NOT_ADMITTED'
require_literal "$CONTRACT" 'package_mutation: BLOCK'
require_literal "$CONTRACT" 'native_execution: NOT_IMPLEMENTED'
require_literal "$CONTRACT" 'production_admission: BLOCK'

# R42.12 must not construct the live ByeDPI network stack in Service or its provisioner.
for file in "$PROVISIONER" "$PROGRAM" "$SERVICE"; do
  forbid_regex "$file" 'SocketsHttpHandler|HttpClientHandler|new[[:space:]]+HttpClient|new[[:space:]]+ByeDpiIsolatedHttpTransport|WebRequest|WebClient|TcpClient|UdpClient|Dns\.'
done

# The package-apply dispatch must never start/stop native runtime as a side effect.
apply_block="$(sed -n '/case IpcCommand.ByeDpiApplyReviewedPackage:/,/case IpcCommand.ByeDpiGetStatus:/p' "$SERVICE")"
if grep -Eq 'StartAsync|StopAsync|Process\.Start|ProcessStartInfo' <<<"$apply_block"; then
  echo 'R42.12 package-apply IPC block contains forbidden runtime execution/control call' >&2
  exit 1
fi

# Current production policy remains no-install: only legacy EngineStop is admitted.
policy_block="$(sed -n '/CreateNoAdmittedEnginePolicy()/,/]);/p' "$POLICY")"
if grep -Fq 'PrivilegedOperation.EngineInstall' <<<"$policy_block"; then
  echo 'R42.12 unexpectedly enabled EngineInstall in the production no-admit policy' >&2
  exit 1
fi
require_literal "$POLICY" 'PrivilegedOperation.EngineStop'

printf '%s\n' \
  'schema_version=1' \
  'scope=byedpi-r42.12-package-ipc-guard' \
  'result=PASS' \
  'ipc_protocol=5' \
  'package_ipc=PRESENT_FAIL_CLOSED' \
  'caller_target_payload=FORBIDDEN' \
  'production_provisioner=NO_ADMITTED' \
  'live_network_acquisition=NOT_ADMITTED' \
  'package_mutation=BLOCK' \
  'native_execution=NOT_IMPLEMENTED' \
  'production_admission=BLOCK' \
  > r42-12-package-ipc-guard-evidence.txt
