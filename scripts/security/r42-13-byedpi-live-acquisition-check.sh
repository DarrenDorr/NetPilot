#!/usr/bin/env bash
set -euo pipefail

HANDLER='src/NetPilot.Updater/ByeDpiLiveHttpHandlerFactory.cs'
PIPELINE='src/NetPilot.Platform.Windows/WindowsByeDpiLiveAcquisitionPipeline.cs'
HANDLER_TESTS='tests/NetPilot.Core.Tests/ByeDpiLiveHttpHandlerFactoryTests.cs'
PIPELINE_TESTS='tests/NetPilot.Platform.Windows.Tests/WindowsByeDpiLiveAcquisitionPipelineTests.cs'
LAB_PROJECT='tools/NetPilot.ByeDpiR42_13LiveAcquisitionLab/NetPilot.ByeDpiR42_13LiveAcquisitionLab.csproj'
LAB_PROGRAM='tools/NetPilot.ByeDpiR42_13LiveAcquisitionLab/Program.cs'
CONTRACT='security/native-admission/byedpi-live-acquisition-r42-13.yaml'
DOC='docs/BYEDPI-LIVE-ACQUISITION-R42-13.md'
SERVICE_PROGRAM='src/NetPilot.Service/Program.cs'
LIVE_WORKFLOW='.github/workflows/r42-13-live-acquisition-localsystem-lab.yml'
PREFLIGHT_WORKFLOW='.github/workflows/r42-13-live-acquisition-preflight-once.yml'

for path in "$HANDLER" "$PIPELINE" "$HANDLER_TESTS" "$PIPELINE_TESTS" "$LAB_PROJECT" "$LAB_PROGRAM" "$CONTRACT" "$DOC" "$SERVICE_PROGRAM" "$LIVE_WORKFLOW" "$PREFLIGHT_WORKFLOW"; do
  test -f "$path" || { echo "missing required R42.13 file: $path" >&2; exit 1; }
done

require_literal() {
  local file="$1"
  local text="$2"
  grep -Fq -- "$text" "$file" || {
    echo "R42.13 guard missing required text in $file: $text" >&2
    exit 1
  }
}

forbid_regex() {
  local file="$1"
  local regex="$2"
  if grep -Eiq -- "$regex" "$file"; then
    echo "R42.13 guard found forbidden pattern in $file: $regex" >&2
    exit 1
  fi
}

# Hardened handler must keep redirects and ambient authority disabled.
require_literal "$HANDLER" 'AllowAutoRedirect = false'
require_literal "$HANDLER" 'UseCookies = false'
require_literal "$HANDLER" 'UseProxy = false'
require_literal "$HANDLER" 'Credentials = null'
require_literal "$HANDLER" 'PreAuthenticate = false'
require_literal "$HANDLER" 'AutomaticDecompression = DecompressionMethods.None'
require_literal "$HANDLER" 'ConnectTimeout = ConnectTimeout'
require_literal "$HANDLER" 'MaxConnectionsPerServer = MaxConnectionsPerServer'
require_literal "$HANDLER" 'ActivityHeadersPropagator = null'
forbid_regex "$HANDLER" 'ServerCertificateCustomValidationCallback|RemoteCertificateValidationCallback|DangerousAcceptAnyServerCertificateValidator|ConnectCallback[[:space:]]*='

# Public live pipeline is pinned to the reviewed release catalog and inherited verified boundaries.
require_literal "$PIPELINE" 'ByeDpiReviewedReleaseCatalog.Current'
require_literal "$PIPELINE" 'ByeDpiLiveHttpHandlerFactory.Create()'
require_literal "$PIPELINE" 'ByeDpiVerifiedReleaseDownloader'
require_literal "$PIPELINE" 'WindowsByeDpiVerifiedBundleStager.StageAsync'
require_literal "$PIPELINE" 'WindowsByeDpiVerifiedStagingPromoter.PromoteAsync'
require_literal "$PIPELINE" 'ByeDpiLiveAcquisitionActiveLease'
require_literal "$PIPELINE" 'WindowsByeDpiLiveAcquisitionAction.NoChange'
forbid_regex "$PIPELINE" 'Process(Start|\.)|NamedPipe|ServiceController|Registry\.'

require_literal "$HANDLER_TESTS" 'Handler_is_fail_closed_for_redirect_credentials_cookies_proxy_and_decompression'
require_literal "$PIPELINE_TESTS" 'Synthetic_reviewed_chain_downloads_stages_and_atomically_installs'
require_literal "$PIPELINE_TESTS" 'Exact_current_version_is_noop_without_constructing_second_network_handler'
require_literal "$PIPELINE_TESTS" 'Active_engine_lease_is_rejected_before_any_network_handler_is_created'
require_literal "$PIPELINE_TESTS" 'Upgrade_uses_existing_state_and_retains_previous_version'

# Lab host may inspect process state but must never start the native engine.
require_literal "$LAB_PROGRAM" 'WindowsIdentity.GetCurrent()'
require_literal "$LAB_PROGRAM" '"S-1-5-18"'
require_literal "$LAB_PROGRAM" 'WindowsByeDpiLiveAcquisitionPipeline()'
require_literal "$LAB_PROGRAM" 'MANAGED_CIADPI_EXACT_IDENTITY=PASS'
require_literal "$LAB_PROGRAM" 'NATIVE_EXECUTION_TESTED=False'
forbid_regex "$LAB_PROGRAM" 'Process\.Start|ProcessStartInfo|CreateProcess|ShellExecute'

# Normal Service must remain no-admit and must not construct live acquisition types.
require_literal "$SERVICE_PROGRAM" 'NoAdmittedWindowsByeDpiPackageProvisioner.Instance'
require_literal "$SERVICE_PROGRAM" 'OperationPolicy.CreateNoAdmittedEnginePolicy()'
forbid_regex "$SERVICE_PROGRAM" 'ByeDpiLiveHttpHandlerFactory|WindowsByeDpiLiveAcquisitionPipeline|SocketsHttpHandler|new[[:space:]]+ByeDpiIsolatedHttpTransport'

require_literal "$CONTRACT" 'status: LAB_ONLY_BLOCKED_UNTIL_IMMUTABLE_RELEASE'
require_literal "$CONTRACT" 'automatic_redirects: false'
require_literal "$CONTRACT" 'requires_immutable_true: true'
require_literal "$CONTRACT" 'localsystem_required: true'
require_literal "$CONTRACT" 'actual_reviewed_binary_executed: false'
require_literal "$CONTRACT" 'service_registration: ABSENT'
require_literal "$CONTRACT" 'production_admission: BLOCK'

# Live workflow must be manual-only and gate on published immutable release metadata.
require_literal "$LIVE_WORKFLOW" 'workflow_dispatch:'
require_literal "$LIVE_WORKFLOW" 'immutable'
require_literal "$LIVE_WORKFLOW" 'IDENTITY_LOCALSYSTEM=PASS'
require_literal "$LIVE_WORKFLOW" 'schtasks.exe'
require_literal "$LIVE_WORKFLOW" 'R42_13_LIVE_ACQUISITION_LOCALSYSTEM_PASS_BLOCK'
forbid_regex "$LIVE_WORKFLOW" '^[[:space:]]+push:|^[[:space:]]+pull_request:'

# Ordinary preflight may observe release state, but only an exact 404 is a normal blocked prerequisite.
# Any other GitHub/API failure must remain fail-closed instead of being mislabeled as "release absent".
require_literal "$PREFLIGHT_WORKFLOW" 'Invoke-WebRequest -Uri $uri -Headers $headers -Method Get -SkipHttpErrorCheck'
require_literal "$PREFLIGHT_WORKFLOW" '200 {'
require_literal "$PREFLIGHT_WORKFLOW" '404 {'
require_literal "$PREFLIGHT_WORKFLOW" 'default {'
require_literal "$PREFLIGHT_WORKFLOW" 'R42_13_IMMUTABLE_RELEASE_PREREQUISITE=BLOCKED_RELEASE_ABSENT'
require_literal "$PREFLIGHT_WORKFLOW" 'Unexpected GitHub API status while checking R42.13 prerequisite:'

printf '%s\n' \
  'schema_version=1' \
  'scope=byedpi-r42.13-live-acquisition-guard' \
  'result=PASS' \
  'live_handler=LAB_ONLY' \
  'live_e2e=BLOCKED_UNTIL_IMMUTABLE_RELEASE' \
  'service_production_wiring=ABSENT' \
  'package_mutation_in_production=BLOCK' \
  'native_execution=NOT_IMPLEMENTED' \
  'production_admission=BLOCK' \
  > r42-13-live-acquisition-guard-evidence.txt
