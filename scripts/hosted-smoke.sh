#!/usr/bin/env bash
# Smoke test for the hosted demo: the checks a deploy must pass before it counts (Sprint 5).
#
# The same idea as compose-smoke.sh, over the public HTTPS address, plus what only the hosted setup
# can prove: sign-in keeps https behind the ingress (forwarded headers), and the internal sample
# service can't be reached from outside. Needs no sign-in and no data-plane key.
#
# The database check from the README's Verify list is left out on purpose: GitHub's runners run in
# Azure, and PostgreSQL's AllowAzureServices rule admits any Azure address, so a TCP check from a
# runner reaches the port. Entra-only authentication is the control there, not the network.
#
# Usage: scripts/hosted-smoke.sh https://stria-server.<environment domain>
set -euo pipefail

STRIA_URL="${1:?usage: scripts/hosted-smoke.sh https://stria-server.<environment domain>}"
STRIA_URL="${STRIA_URL%/}"
if [[ "$STRIA_URL" != https://* ]]; then
  echo "Use the https:// address of stria-server." >&2
  exit 2
fi
host="${STRIA_URL#https://}"
# Container Apps gives an internal app the name <app>.internal.<environment domain>.
SAMPLE_URL="${SAMPLE_URL:-https://sample-payment-gateway.internal.${host#*.}}"

failures=0

# expect <name> <expected status...> -- <curl args...>
expect() {
  local name="$1"; shift
  local expected=()
  while [[ "$1" != "--" ]]; do expected+=("$1"); shift; done
  shift
  local status
  status="$(curl -s -o /dev/null -w '%{http_code}' --max-time 30 "$@" || true)"
  for code in "${expected[@]}"; do
    if [[ "$status" == "$code" ]]; then
      printf 'ok    %-58s %s\n' "$name" "$status"
      return
    fi
  done
  printf 'FAIL  %-58s %s (expected %s)\n' "$name" "$status" "${expected[*]}"
  failures=$((failures + 1))
}

json=(-H 'Content-Type: application/json')

# The app scales to zero and a new revision starts cold, so wait for readiness first.
ready_timeout="${STRIA_READY_TIMEOUT:-180}"
waited=0
until [[ "$(curl -s -o /dev/null -w '%{http_code}' --max-time 30 "$STRIA_URL/readyz" || true)" == "200" ]]; do
  if ((waited >= ready_timeout)); then
    echo "Stria was not ready at $STRIA_URL/readyz after ${ready_timeout}s." >&2
    exit 1
  fi
  sleep 5
  waited=$((waited + 5))
done

echo "== Health"
expect "GET /healthz" 200 -- "$STRIA_URL/healthz"
expect "GET /readyz" 200 -- "$STRIA_URL/readyz"

echo "== Anonymous callers are rejected"
expect "GET /api/v1/chaos/faults" 401 -- "$STRIA_URL/api/v1/chaos/faults"
expect "GET /api/v1/chaos/kill-switch" 401 -- "$STRIA_URL/api/v1/chaos/kill-switch"
expect "POST /api/v1/chaos/inject" 401 -- -X POST "${json[@]}" \
  -d '{"targetService":"PaymentGateway","faultType":"Latency","latencyMs":100}' "$STRIA_URL/api/v1/chaos/inject"
expect "POST /api/v1/chaos/kill-switch/engage" 401 -- -X POST "${json[@]}" -d '{"reason":"smoke"}' \
  "$STRIA_URL/api/v1/chaos/kill-switch/engage"
expect "GET /api/v1/chaos/directives/PaymentGateway" 401 -- "$STRIA_URL/api/v1/chaos/directives/PaymentGateway"

echo "== Sign-in stays on https behind the ingress"
location="$(curl -s -o /dev/null -w '%{redirect_url}' --max-time 30 "$STRIA_URL/" || true)"
if [[ "$location" == https://login.microsoftonline.com/* && "$location" == *"redirect_uri=https%3A%2F%2F${host}%2Fsignin-oidc"* ]]; then
  echo "ok    / redirects to Entra with an https redirect_uri"
else
  echo "FAIL  / does not redirect to Entra with redirect_uri=https://${host}/signin-oidc"
  failures=$((failures + 1))
fi

echo "== Browser hardening"
headers="$(curl -s -D - -o /dev/null --max-time 30 "$STRIA_URL/access-denied" || true)"
if grep -qi "^content-security-policy: .*'nonce-" <<<"$headers"; then
  echo "ok    CSP is enforced with a nonce"
else
  echo "FAIL  CSP is not enforced with a nonce"; failures=$((failures + 1))
fi

echo "== Internal services are not public"
status="$(curl -s -o /dev/null -w '%{http_code}' --max-time 15 "$SAMPLE_URL/healthz" || true)"
if [[ "$status" == "200" ]]; then
  echo "FAIL  $SAMPLE_URL answers from outside (200)"; failures=$((failures + 1))
else
  printf 'ok    %-58s %s\n' "sample service unreachable from outside" "$status"
fi

echo
if ((failures > 0)); then
  echo "$failures check(s) failed."
  exit 1
fi
echo "All checks passed."
