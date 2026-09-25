#!/usr/bin/env bash
# Black-box security check of a running deployment: talks to it the way an outsider would and fails on anything that
# should not be possible. Safe to run at any time (it only sends requests; it changes no data).
#
#   deploy/scripts/verify.sh                       the local gateway, http://127.0.0.1:8088
#   deploy/scripts/verify.sh https://name.ts.net   a published address (e.g. the Funnel URL, or :8443 for test-on)
#
# Exit status 0 = every check passed.
set -uo pipefail

cd "$(dirname "$0")/.."

PORT="$(grep -E '^GATEWAY_PORT=' .env 2>/dev/null | cut -d= -f2 || true)"; PORT="${PORT:-8088}"
PUBLIC_HOST="$(grep -E '^PUBLIC_HOST=' .env 2>/dev/null | cut -d= -f2 || true)"
BASE="${1:-http://127.0.0.1:${PORT}}"
CURL=(curl -sS --max-time 20)
# Against the local gateway the Host header has to be the public name, as it would be behind Funnel.
[[ "$BASE" == http://127.0.0.1* && -n "$PUBLIC_HOST" ]] && CURL+=(-H "Host: ${PUBLIC_HOST}")

pass=0; fail=0
ok()   { pass=$((pass + 1)); printf '  \033[32mPASS\033[0m  %s\n' "$1"; }
bad()  { fail=$((fail + 1)); printf '  \033[31mFAIL\033[0m  %s\n' "$1"; }
check(){ if eval "$2"; then ok "$1"; else bad "$1"; fi; }

# curl's exit status is not trusted here (Windows builds report a write error for -o /dev/null even on success):
# only an empty reply means the request itself failed.
status() { local code; code="$("${CURL[@]}" -o /dev/null -w '%{http_code}' "$@" 2>/dev/null)" || true; echo "${code:-000}"; }
headers() { "${CURL[@]}" -D - -o /dev/null "$@" 2>/dev/null | tr -d '\r' || true; }

echo "Checking $BASE"

echo "Transport and headers"
H="$(headers "$BASE/login")"
check "the sign-in page loads"                                   '[[ "$(status "$BASE/login")" == 200 ]]'
check "HSTS is set"                                              'grep -qi "^strict-transport-security: max-age=" <<<"$H"'
check "content is not sniffed (nosniff)"                         'grep -qi "^x-content-type-options: nosniff" <<<"$H"'
check "the site cannot be framed"                                'grep -qi "^x-frame-options: deny" <<<"$H"'
check "referrer policy is set"                                   'grep -qi "^referrer-policy:" <<<"$H"'
check "permissions policy limits device access"                  'grep -qi "^permissions-policy:.*camera=()" <<<"$H"'
check "CSP is set with a nonce and no unsafe-inline scripts"     'grep -i "^content-security-policy:" <<<"$H" | grep -q "script-src [^;]*nonce-" && ! grep -i "^content-security-policy:" <<<"$H" | grep -q "script-src [^;]*unsafe-inline"'
check "CSP forbids framing and plugins"                          'grep -i "^content-security-policy:" <<<"$H" | grep -q "frame-ancestors .none." && grep -i "^content-security-policy:" <<<"$H" | grep -q "object-src .none."'
check "no Server / X-Powered-By fingerprint"                     '! grep -qiE "^(server|x-powered-by):" <<<"$H"'

echo "Anonymous access"
for path in /api/users /api/students /api/leads /api/finance/invoices /api/system/metrics /api/me/permissions; do
  check "GET $path without a token is refused"                   '[[ "$(status "$BASE$path")" == 401 ]]'
done
check "a request for another host name is refused"               '[[ "$(curl -sS --max-time 20 -o /dev/null -w "%{http_code}" -H "Host: evil.example" "$BASE/login" 2>/dev/null)" == 421 ]]'
check "TRACE is refused"                                         '[[ "$(status -X TRACE "$BASE/")" == 405 ]]'
for path in /hangfire /openapi/v1.json /swagger /healthz /api/healthz /.env /.git/config /appsettings.json; do
  check "$path is not served"                                    '[[ "$(status "$BASE$path")" =~ ^(404|401|421)$ ]]'
done

echo "Credentials"
login_status() { status -X POST -H 'Content-Type: application/json' -d "{\"email\":\"$1\",\"password\":\"$2\"}" "$BASE/api/auth/login"; }
for account in superadmin admin operator teacher support student parent developer; do
  check "$account@demo-center.test refuses the published password Passw0rd!"  '[[ "$(login_status "$account@demo-center.test" "Passw0rd!")" == 401 ]]'
done

# A token signed with the key that is committed in the repository must be worthless.
DEV_KEY="$(python - <<'PY' 2>/dev/null
import json
print(json.load(open("../backend/src/EduCenter.Api/appsettings.json", encoding="utf-8"))["Jwt"]["SigningKey"])
PY
)"
forge() { python - "$1" "$2" <<'PY'
import base64, hashlib, hmac, json, sys, time
alg, key = sys.argv[1], sys.argv[2]
b64 = lambda b: base64.urlsafe_b64encode(b).rstrip(b"=").decode()
header = b64(json.dumps({"alg": alg, "typ": "JWT"}).encode())
body = b64(json.dumps({
    "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier": "00000000-0000-0000-0000-000000000001",
    "org": "00000000-0000-0000-0000-000000000001",
    "http://schemas.microsoft.com/ws/2008/06/identity/claims/role": "SuperAdmin",
    "exp": int(time.time()) + 3600}).encode())
sig = b64(hmac.new(key.encode(), f"{header}.{body}".encode(), hashlib.sha256).digest()) if alg != "none" else ""
print(f"{header}.{body}.{sig}")
PY
}
if [[ -n "$DEV_KEY" ]]; then
  check "a token signed with the repository's dev key is refused"  '[[ "$(status -H "Authorization: Bearer $(forge HS256 "$DEV_KEY")" "$BASE/api/users")" == 401 ]]'
fi
check "an unsigned (alg=none) token is refused"                  '[[ "$(status -H "Authorization: Bearer $(forge none x)" "$BASE/api/users")" == 401 ]]'
check "garbage in the Authorization header is refused"           '[[ "$(status -H "Authorization: Bearer not.a.token" "$BASE/api/users")" == 401 ]]'

echo "Cross-origin and abuse"
PREFLIGHT="$(headers -X OPTIONS -H 'Origin: https://evil.example' -H 'Access-Control-Request-Method: POST' -H 'Access-Control-Request-Headers: content-type,authorization' "$BASE/api/auth/login")"
check "a foreign origin gets no CORS permission"                 '! grep -qi "^access-control-allow-origin:" <<<"$PREFLIGHT"'
BIG="$(head -c 20000 /dev/zero | tr '\0' 'a')"
check "an oversized sign-in body is refused (413)"               '[[ "$(status -X POST -H "Content-Type: application/json" -d "{\"email\":\"$BIG\",\"password\":\"x\"}" "$BASE/api/auth/login")" == 413 ]]'
PROBE="verify-$RANDOM$RANDOM@probe.invalid"
last=000; for _ in 1 2 3 4 5 6 7; do last="$(login_status "$PROBE" "wrong-password")"; done
check "repeated wrong passwords lock the account (429)"          '[[ "$last" == 429 ]]'

echo "Network exposure (this machine)"
if command -v docker >/dev/null 2>&1 && docker inspect educenter-prod-gateway-1 >/dev/null 2>&1; then
  published="$(docker ps --filter "name=educenter-prod-" --format '{{.Names}} {{.Ports}}' | grep -c -- '->' || true)"
  check "only the gateway publishes a port"                      '[[ "$published" == 1 ]]'
  check "the gateway port is bound to loopback only"             'docker port educenter-prod-gateway-1 | grep -q "^8080/tcp -> 127.0.0.1:"'
  check "the database has no published port"                     '[[ -z "$(docker port educenter-prod-db-1 2>/dev/null)" ]]'
  check "the api container cannot write to its own filesystem"   '[[ "$(docker inspect -f "{{.HostConfig.ReadonlyRootfs}}" educenter-prod-api-1)" == true ]]'
  check "the api does not run as root"                           '[[ "$(docker exec educenter-prod-api-1 id -u)" != 0 ]]'
  check "the database is on a network with no way out"           '[[ "$(docker network inspect educenter-prod_backend -f "{{.Internal}}")" == true ]]'
fi

echo
echo "$pass passed, $fail failed"
[[ "$fail" == 0 ]]
