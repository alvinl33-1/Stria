#!/usr/bin/env bash
# Deploys one sha- tag to the hosted demo: the steps the CI deploy job runs (Sprint 5), runnable by
# hand with the same result.
#
# Nothing changes until every precondition holds: the tag is well formed, PostgreSQL is Ready (a
# stopped server fails here, with the fix, not halfway through a migration), the images are in the
# registry, and the tag running now is recorded as the rollback target. Then the migration job runs
# the new bundle, both apps move to the new tag, and hosted-smoke.sh decides. A failure after the
# first change moves every changed resource back to the recorded tag and smoke-tests that.
#
# The database is not rolled back: migrations are forward-only, so a release must not drop or
# rename anything the previous image still uses (docs/PHASE_3_PLAN.md, Step 5).
#
# Usage: scripts/deploy-demo.sh [--push] sha-<commit>
#   --push  tag and push the local images stria-{api,migrator,sample}:<tag> first (CI). Without it
#           the tag must already be in the registry (a redeploy or a manual rollback).
# Reads AZURE_RESOURCE_GROUP, AZURE_CONTAINER_REGISTRY_ENDPOINT, POSTGRES_SERVER_NAME and
# STRIA_SERVER_URL: the `demo` environment's variables in CI, `azd env get-values` by hand.
# Exit codes: 0 deployed, 1 failed (rolled back if anything changed), 2 usage, 3 the rollback did
# not verify and the demo needs a person.
set -euo pipefail

push=false
if [[ "${1:-}" == "--push" ]]; then
  push=true
  shift
fi
tag="${1:-}"
if [[ $# -ne 1 || ! "$tag" =~ ^sha-[0-9a-f]{40}$ ]]; then
  echo "usage: scripts/deploy-demo.sh [--push] sha-<40-character commit SHA>" >&2
  exit 2
fi
for name in AZURE_RESOURCE_GROUP AZURE_CONTAINER_REGISTRY_ENDPOINT POSTGRES_SERVER_NAME STRIA_SERVER_URL; do
  if [[ -z "${!name:-}" ]]; then
    echo "$name is not set." >&2
    exit 2
  fi
done
if [[ "$STRIA_SERVER_URL" != https://* ]]; then
  echo "STRIA_SERVER_URL must be the https:// address of stria-server." >&2
  exit 2
fi

rg="$AZURE_RESOURCE_GROUP"
registry="$AZURE_CONTAINER_REGISTRY_ENDPOINT"
smoke="$(dirname "$0")/hosted-smoke.sh"
# Resource and container names are fixed in infra/main.bicep and infra/modules/*.bicep.
migrate_job=stria-migrate
revision_timeout="${STRIA_REVISION_TIMEOUT:-600}"
migration_timeout="${STRIA_MIGRATION_TIMEOUT:-900}"
apps=("stria-server|stria-server|stria-api" "sample-payment-gateway|sample-payment-gateway|stria-sample")

step() { printf '\n== %s\n' "$*"; }
error() {
  if [[ "${GITHUB_ACTIONS:-}" == "true" ]]; then echo "::error::$*"; else echo "error: $*" >&2; fi
}
fail() {
  error "$*"
  exit 1
}

# set_image <job|app> <resource> <container> <image>. az waits until the change is provisioned.
set_image() {
  if [[ "$1" == job ]]; then
    az containerapp job update -g "$rg" -n "$2" --container-name "$3" --image "$4" --output none
  else
    az containerapp update -g "$rg" -n "$2" --container-name "$3" --image "$4" --output none
  fi
}

# Single-revision mode keeps the old revision serving until the new one is ready, so a smoke test
# run too early passes against the old image. Wait until the newest revision is the ready one.
wait_for_revision() {
  local app="$1" image="$2" deadline=$((SECONDS + revision_timeout)) out latest ready current state
  while ((SECONDS < deadline)); do
    # `|| 'none'`: a null field prints as an empty tsv column, and read would shift the rest left.
    out="$(az containerapp show -g "$rg" -n "$app" -o tsv --query \
      "[properties.latestRevisionName, properties.latestReadyRevisionName || 'none', properties.template.containers[0].image]")" || return 1
    read -r latest ready current <<<"$out"
    if [[ "$current" != "$image" ]]; then
      echo "$app runs $current, expected $image." >&2
      return 1
    fi
    if [[ "$latest" == "$ready" ]]; then
      echo "$app: revision $latest is ready"
      return 0
    fi
    state="$(az containerapp revision show -g "$rg" -n "$app" --revision "$latest" \
      --query properties.provisioningState -o tsv)" || return 1
    if [[ "$state" == Failed ]]; then
      echo "$app: revision $latest failed to provision." >&2
      return 1
    fi
    sleep 10
  done
  echo "$app: no ready revision with $image after ${revision_timeout}s." >&2
  return 1
}

run_migration() {
  local execution status deadline=$((SECONDS + migration_timeout))
  execution="$(az containerapp job start -g "$rg" -n "$migrate_job" --query name -o tsv)"
  echo "Started $execution"
  while :; do
    status="$(az containerapp job execution show -g "$rg" -n "$migrate_job" \
      --job-execution-name "$execution" --query properties.status -o tsv)"
    case "$status" in
      Succeeded)
        echo "$execution succeeded"
        return 0
        ;;
      Failed | Stopped | Degraded)
        fail "Migration $execution ended $status. Its logs are under $migrate_job, Execution history, in the portal."
        ;;
    esac
    ((SECONDS < deadline)) || fail "Migration $execution was still $status after ${migration_timeout}s."
    sleep 10
  done
}

changed=()      # job|app <resource> <container> <repository>, in the order they changed
rollback_tag="" # the tag that ran before this deploy

# Runs on every exit. Failures after the first change land here (set -e or fail), walk the changed
# resources back in reverse order, and smoke-test the result: only a verified rollback exits 1.
restore() {
  local status=$? failed=0 i kind name container repo
  if ((status == 0 || ${#changed[@]} == 0)); then
    exit "$status"
  fi
  set +e
  if [[ -z "$rollback_tag" ]]; then
    error "Redeploy of $tag failed and there is no earlier tag to roll back to. Check the demo now."
    exit 3
  fi
  step "Roll back to $rollback_tag"
  for ((i = ${#changed[@]} - 1; i >= 0; i--)); do
    IFS='|' read -r kind name container repo <<<"${changed[i]}"
    echo "$name -> $repo:$rollback_tag"
    set_image "$kind" "$name" "$container" "$registry/$repo:$rollback_tag" || failed=1
    if [[ "$kind" == app ]]; then
      wait_for_revision "$name" "$registry/$repo:$rollback_tag" || failed=1
    fi
  done
  if ((failed == 0)) && "$smoke" "$STRIA_SERVER_URL"; then
    error "Deploy of $tag failed. Rolled back to $rollback_tag, which passes the smoke test."
    exit 1
  fi
  error "Deploy of $tag failed and the rollback to $rollback_tag did not verify. Check the demo now."
  exit 3
}
trap restore EXIT

step "Preconditions"
pg_state="$(az postgres flexible-server show -g "$rg" -n "$POSTGRES_SERVER_NAME" --query state -o tsv)"
if [[ "$pg_state" != Ready ]]; then
  fail "PostgreSQL is $pg_state, not Ready. Start it with: az postgres flexible-server start -g $rg -n $POSTGRES_SERVER_NAME"
fi
echo "PostgreSQL is Ready"

az acr login --name "${registry%%.*}"
for repo in stria-api stria-migrator stria-sample; do
  if $push; then
    docker tag "$repo:$tag" "$registry/$repo:$tag"
    docker push --quiet "$registry/$repo:$tag"
  else
    docker manifest inspect "$registry/$repo:$tag" >/dev/null || fail "$registry/$repo:$tag is not in the registry."
    echo "Found $repo:$tag"
  fi
done

running="$(az containerapp show -g "$rg" -n stria-server --query 'properties.template.containers[0].image' -o tsv)"
rollback_tag="${running##*:}"
if [[ "$running" != "$registry/stria-api:$rollback_tag" || ! "$rollback_tag" =~ ^sha-[0-9a-f]{40}$ ]]; then
  fail "stria-server runs $running, so there is no sha- tag to roll back to."
fi
if [[ "$rollback_tag" == "$tag" ]]; then
  echo "Already on $tag: redeploying it, with no earlier tag to roll back to"
  rollback_tag=""
else
  echo "Rollback target: $rollback_tag"
fi

step "Migrate the database"
# Recorded before the update: a command that fails after Azure accepted it still changed the job.
changed+=("job|$migrate_job|migrate|stria-migrator")
set_image job "$migrate_job" migrate "$registry/stria-migrator:$tag"
run_migration

step "Update the apps"
for app in "${apps[@]}"; do
  IFS='|' read -r name container repo <<<"$app"
  changed+=("app|$name|$container|$repo")
  set_image app "$name" "$container" "$registry/$repo:$tag"
  wait_for_revision "$name" "$registry/$repo:$tag"
done

step "Smoke test"
"$smoke" "$STRIA_SERVER_URL" || fail "The smoke test failed on $tag."

step "Deployed $tag"
