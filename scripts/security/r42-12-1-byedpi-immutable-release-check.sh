#!/usr/bin/env bash
set -euo pipefail

CATALOG='src/NetPilot.Updater/ByeDpiReviewedReleaseCatalog.cs'
TESTS='tests/NetPilot.Core.Tests/ByeDpiReviewedReleaseCatalogTests.cs'
CREATE_WORKFLOW='.github/workflows/r42-12-1-create-draft-byedpi-release.yml'
VERIFY_WORKFLOW='.github/workflows/r42-12-1-verify-immutable-byedpi-release.yml'
CONTRACT='security/native-admission/byedpi-immutable-release-r42-12-1.yaml'
DOC='docs/BYEDPI-IMMUTABLE-RELEASE-R42-12-1.md'
PROGRAM='src/NetPilot.Service/Program.cs'

for path in "$CATALOG" "$TESTS" "$CREATE_WORKFLOW" "$VERIFY_WORKFLOW" "$CONTRACT" "$DOC" "$PROGRAM"; do
  test -f "$path" || { echo "missing required R42.12.1 file: $path" >&2; exit 1; }
done

require_literal() {
  local file="$1"
  local text="$2"
  grep -Fq -- "$text" "$file" || {
    echo "R42.12.1 guard missing required text in $file: $text" >&2
    exit 1
  }
}

forbid_regex() {
  local file="$1"
  local regex="$2"
  if grep -Eiq -- "$regex" "$file"; then
    echo "R42.12.1 guard found forbidden pattern in $file: $regex" >&2
    exit 1
  fi
}

require_literal "$CATALOG" 'public const string R42_5Checkpoint = "dc28fc4f0b635c34c63bbaed99ea7658d8a9c827";'
require_literal "$CATALOG" 'public const string ReleaseTag = "byedpi-v0.17.3";'
require_literal "$CATALOG" 'public const long PayloadSizeBytes = 496_964;'
require_literal "$CATALOG" 'c0fe998e3d81ccb871f09d126e7b5ee0ab82d15daf78bc44bcd63b7762313db7'
require_literal "$CATALOG" 'public const long ManifestSizeBytes = 511;'
require_literal "$CATALOG" 'eaa6f063fcb8e012810f849ed08b6bd82d4d046d4796c2fb4b903c4195985f54'
require_literal "$CATALOG" 'public const long ProvenanceSizeBytes = 1_726;'
require_literal "$CATALOG" 'e1f98760eed94f5daa7d37a868015755220cb17d389000bc49e031da511f7f31'
require_literal "$CATALOG" 'ByeDpiImmutableReleaseDocument.Serialize(Reviewed)'

require_literal "$TESTS" 'Current_descriptor_matches_exact_r42_5_distribution_identity'
require_literal "$TESTS" 'Exact_reviewed_descriptor_round_trips_through_strict_r42_6_document'

# Draft creation is manual only and must never publish from CI.
require_literal "$CREATE_WORKFLOW" 'workflow_dispatch:'
require_literal "$CREATE_WORKFLOW" '--draft'
require_literal "$CREATE_WORKFLOW" 'Require release tag and draft to be absent'
require_literal "$CREATE_WORKFLOW" 'Build package twice and require exact reviewed outputs'
require_literal "$CREATE_WORKFLOW" 'test "$(jq -r '\''.draft'\'' <<<"$release_json")" = '\''true'\'''
forbid_regex "$CREATE_WORKFLOW" '^[[:space:]]+push:|^[[:space:]]+pull_request:'
forbid_regex "$CREATE_WORKFLOW" 'gh[[:space:]]+release[[:space:]]+edit|--draft=false|make_latest|published_at'

# Post-publish verifier is also manual/read-only and must require immutable=true.
require_literal "$VERIFY_WORKFLOW" 'workflow_dispatch:'
require_literal "$VERIFY_WORKFLOW" 'permissions:'
require_literal "$VERIFY_WORKFLOW" 'contents: read'
require_literal "$VERIFY_WORKFLOW" 'test "$(jq -r '\''.immutable'\'' <<<"$release_json")" = '\''true'\'''
require_literal "$VERIFY_WORKFLOW" 'gh release verify "$RELEASE_TAG"'
require_literal "$VERIFY_WORKFLOW" 'R42_12_1_RELEASE_IMMUTABLE=true'
forbid_regex "$VERIFY_WORKFLOW" '^[[:space:]]+push:|^[[:space:]]+pull_request:|gh[[:space:]]+release[[:space:]]+create|gh[[:space:]]+release[[:space:]]+edit'

require_literal "$CONTRACT" 'status: DRAFT_PUBLICATION_READY_PUBLISH_MANUAL'
require_literal "$CONTRACT" 'automatic_publish: FORBIDDEN'
require_literal "$CONTRACT" 'draft_creation: MANUAL_WORKFLOW_DISPATCH_ONLY'
require_literal "$CONTRACT" 'repository_immutable_releases_required_before_publish: true'
require_literal "$CONTRACT" 'post_publish_immutable_field_required: true'
require_literal "$CONTRACT" 'live_network_product_acquisition: NOT_ADMITTED'
require_literal "$CONTRACT" 'production_admission: BLOCK'

# Publication readiness must not quietly replace the R42.12 no-admit Service provisioner.
require_literal "$PROGRAM" 'NoAdmittedWindowsByeDpiPackageProvisioner.Instance'
forbid_regex "$PROGRAM" 'new[[:space:]]+ByeDpiIsolatedHttpTransport|SocketsHttpHandler|HttpClientHandler'

printf '%s\n' \
  'schema_version=1' \
  'scope=byedpi-r42.12.1-immutable-release-guard' \
  'result=PASS' \
  'reviewed_release_catalog=EXACT_R42_5' \
  'draft_creation=MANUAL_ONLY' \
  'automatic_publish=FORBIDDEN' \
  'published_immutable_release=NOT_YET_VERIFIED' \
  'live_product_acquisition=NOT_ADMITTED' \
  'native_execution=NOT_IMPLEMENTED' \
  'production_admission=BLOCK' \
  > r42-12-1-immutable-release-guard-evidence.txt
