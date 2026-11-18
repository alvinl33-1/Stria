#!/usr/bin/env bash
# Smoke test for a running Compose stack (`docker compose up --build -d`).
#
# Covers everything that needs no Entra sign-in: health, anonymous rejection on every control
# and data-plane route, data-plane key isolation, the enforced CSP, and the Blazor script.
# Signed-in flows (roles, injection, kill switch) are checked by hand; see docs/PHASE_2_HANDOFF.md.
#
# Usage: scripts/compose-smoke.sh   (reads STRIA_PAYMENTGATEWAY_KEY from the environment or .env)
set -euo pipefail

STRIA_URL="${STRIA_URL:-http://localhost:8080}"
SAMPLE_URL="${SAMPLE_URL:-http://localhost:8081}"

if [[ -z "${STRIA_PAYMENTGATEWAY_KEY:-}" && -f .env ]]; then
  STRIA_PAYMENTGATEWAY_KEY="$(grep -E '^STRIA_PAYMENTGATEWAY_KEY=' .env | head -1 | cut -d= -f2- | tr -d '"')"
fi
: "${STRIA_PAYMENTGATEWAY_KEY:?set STRIA_PAYMENTGATEWAY_KEY or run from the repository root with .env}"

failures=0

# expect <name> <expected status...> -- <curl args...>
expect() {
  local name="$1"; shift
  local expected=()
  while [[ "$1" != "--" ]]; do expected+=("$1"); shift; done
  shift
  local status
  status="$(curl -s -o /dev/null -w '%{http_code}' "$@" || true)"
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
key=(-H "X-Stria-Service-Key: ${STRIA_PAYMENTGATEWAY_KEY}")

echo "== Health"
expect "GET /healthz" 200 -- "$STRIA_URL/healthz"
expect "GET /readyz" 200 -- "$STRIA_URL/readyz"
expect "GET sample /work" 200 -- "$SAMPLE_URL/work"

echo "== Anonymous callers are rejected"
expect "GET /api/v1/chaos/faults" 401 -- "$STRIA_URL/api/v1/chaos/faults"
expect "GET /api/v1/chaos/kill-switch" 401 -- "$STRIA_URL/api/v1/chaos/kill-switch"
expect "POST /api/v1/chaos/inject" 401 -- -X POST "${json[@]}" \
  -d '{"targetService":"PaymentGateway","faultType":"Latency","latencyMs":100}' "$STRIA_URL/api/v1/chaos/inject"
expect "POST /api/v1/chaos/kill-switch/engage" 401 -- -X POST "${json[@]}" -d '{"reason":"smoke"}' \
  "$STRIA_URL/api/v1/chaos/kill-switch/engage"
expect "POST /api/v1/chaos/kill-switch/release" 401 -- -X POST "${json[@]}" -d '{"reason":"smoke"}' \
  "$STRIA_URL/api/v1/chaos/kill-switch/release"
expect "GET /api/v1/chaos/directives/PaymentGateway" 401 -- "$STRIA_URL/api/v1/chaos/directives/PaymentGateway"

echo "== Data-plane key isolation"
expect "own directive with own key" 200 204 -- "${key[@]}" "$STRIA_URL/api/v1/chaos/directives/PaymentGateway"
expect "another service's directive" 403 -- "${key[@]}" "$STRIA_URL/api/v1/chaos/directives/AuthService"
expect "control plane with a data-plane key" 401 -- "${key[@]}" "$STRIA_URL/api/v1/chaos/faults"
expect "kill switch with a data-plane key" 401 -- -X POST "${key[@]}" "${json[@]}" -d '{"reason":"smoke"}' \
  "$STRIA_URL/api/v1/chaos/kill-switch/engage"

echo "== Browser hardening"
headers="$(curl -s -D - -o /dev/null "$STRIA_URL/access-denied")"
if grep -qi "^content-security-policy: .*'nonce-" <<<"$headers"; then
  echo "ok    CSP is enforced with a nonce"
else
  echo "FAIL  CSP is not enforced with a nonce"; failures=$((failures + 1))
fi
asset="$(curl -s "$STRIA_URL/access-denied" | grep -o '_framework/blazor\.web\.[a-z0-9]*\.js' | head -1 || true)"
if [[ -n "$asset" ]]; then
  expect "GET $asset" 200 -- "$STRIA_URL/$asset"
else
  echo "FAIL  page does not reference _framework/blazor.web.js"; failures=$((failures + 1))
fi

echo
if ((failures > 0)); then
  echo "$failures check(s) failed."
  exit 1
fi
echo "All checks passed."
