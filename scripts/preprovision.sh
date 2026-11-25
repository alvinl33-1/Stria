#!/usr/bin/env bash
# azd preprovision hook (ticket 5.3): sets STRIA_IMAGE_TAG to the tag the hosted demo runs, so
# `azd provision` repeats the deployed images instead of rolling them back. Images belong to the
# deploy path (deploy-demo.sh); provisioning owns everything else (lecture 13).
#
# It only reads Azure and writes one azd environment value. It fails, which stops the provision,
# whenever it can't be sure: Azure unreadable, PostgreSQL not Ready, or images that aren't one
# sha- tag. A resource that doesn't exist yet (the first two provisions) is normal.
#
# azd runs it as `bash <file>` with the azd environment's values exported, and the nested
# `azd env set` writes to that same environment. Bash 3.2 compatible (macOS /bin/bash).
# By hand: azd hooks run preprovision
set -euo pipefail

rg="${AZURE_RESOURCE_GROUP:-}"
registry="${AZURE_CONTAINER_REGISTRY_ENDPOINT:-}"
postgres="${POSTGRES_SERVER_NAME:-}"
current="${STRIA_IMAGE_TAG:-}"
err="$(mktemp)"
trap 'rm -f "$err"' EXIT

fail() {
  echo "preprovision: $*" >&2
  exit 1
}

# read_or_missing <az args...>: prints the value, or `missing` when the resource or its group
# doesn't exist yet. az exits 1 for every failure, so not-found is recognised by its error code;
# anything else (signed out, wrong subscription, network) stops the provision.
read_or_missing() {
  local out
  if out="$(az "$@" --only-show-errors -o tsv 2>"$err")"; then
    printf '%s\n' "$out"
  elif grep -Eq '\((ResourceNotFound|ResourceGroupNotFound)\)' "$err"; then
    echo missing
  else
    cat "$err" >&2
    fail "could not read Azure (az $1 $2 …). Check az login and the subscription."
  fi
}

if [[ -z "$rg" ]]; then
  echo "No resource group yet: STRIA_IMAGE_TAG stays '$current'."
  exit 0
fi

# Provisioning fails on a stopped server anyway (ServerStoppedError), but only after the preview.
if [[ -n "$postgres" ]]; then
  state="$(read_or_missing postgres flexible-server show -g "$rg" -n "$postgres" --query state)"
  if [[ "$state" != Ready && "$state" != missing ]]; then
    fail "PostgreSQL is $state, not Ready. Start it with: az postgres flexible-server start -g $rg -n $postgres"
  fi
fi

# Every existing image must be <registry>/<repo>:sha-<commit> with one shared tag. Which tag is
# right after a half-finished deploy is a person's decision, so a disagreement fails.
tag=""
for entry in app/stria-server/stria-api app/sample-payment-gateway/stria-sample job/stria-migrate/stria-migrator; do
  IFS=/ read -r kind name repo <<<"$entry"
  if [[ "$kind" == job ]]; then
    image="$(read_or_missing containerapp job show -g "$rg" -n "$name" --query 'properties.template.containers[0].image')"
  else
    image="$(read_or_missing containerapp show -g "$rg" -n "$name" --query 'properties.template.containers[0].image')"
  fi
  if [[ "$image" == missing ]]; then
    echo "$name: not created yet"
    continue
  fi
  echo "$name: $image"
  this="${image##*:}"
  if [[ "$image" != "$registry/$repo:$this" || ! "$this" =~ ^sha-[0-9a-f]{40}$ ]]; then
    fail "$name runs $image, not $registry/$repo:sha-<commit>."
  fi
  if [[ -n "$tag" && "$this" != "$tag" ]]; then
    fail "the images disagree ($tag, $this). Deploy one tag with deploy.yml (README, Roll back), then provision."
  fi
  tag="$this"
done

if [[ -z "$tag" ]]; then
  echo "No app or job yet: STRIA_IMAGE_TAG stays '$current'."
elif [[ "$tag" == "$current" ]]; then
  echo "STRIA_IMAGE_TAG already is the running $tag."
else
  echo "STRIA_IMAGE_TAG was '$current', running is $tag: set to the running tag."
  azd env set STRIA_IMAGE_TAG "$tag"
fi
