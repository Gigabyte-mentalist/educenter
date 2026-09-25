#!/usr/bin/env bash
# Publishes the gateway on the public internet through Tailscale Funnel — or takes it off again.
#
#   deploy/scripts/funnel.sh on        publish  https://<this-machine>.<tailnet>.ts.net/  ->  the gateway
#   deploy/scripts/funnel.sh off       unpublish (the stack keeps running, reachable from this machine only)
#   deploy/scripts/funnel.sh status
#   deploy/scripts/funnel.sh test-on   the same gateway on https://…:8443, visible to your tailnet only — for checking
#                                      the deployment before anyone outside can see it (`test-off` removes it)
#
# `on` REPLACES whatever else this machine currently serves: Funnel/Serve config is one table per machine. The previous
# configuration is saved to deploy/secrets/previous-serve-config.json first, and `on` refuses to run over a
# configuration that is not ours unless FORCE=1 is set.
set -euo pipefail

cd "$(dirname "$0")/.."

PORT="$(grep -E '^GATEWAY_PORT=' .env 2>/dev/null | cut -d= -f2 || true)"; PORT="${PORT:-8088}"
TARGET="http://127.0.0.1:${PORT}"

die() { echo "error: $*" >&2; exit 1; }
command -v tailscale >/dev/null || die "tailscale is not installed or not on PATH"

gateway_up() { curl -fsS "http://127.0.0.1:${PORT}/gateway-health" >/dev/null; }

case "${1:-status}" in
  on)
    gateway_up || die "the gateway is not answering on ${TARGET} — run deploy/scripts/deploy.sh first"

    current="$(tailscale serve status --json 2>/dev/null || echo '{}')"
    mkdir -p secrets
    echo "$current" > secrets/previous-serve-config.json

    if [[ "$current" != "{}" && "$current" != *"127.0.0.1:${PORT}"* && "${FORCE:-0}" != "1" ]]; then
      echo "This machine already publishes something else:" >&2
      tailscale serve status >&2 || true
      die "refusing to replace it. Saved a copy to deploy/secrets/previous-serve-config.json. Re-run with FORCE=1 to replace it."
    fi

    tailscale serve reset
    tailscale funnel --bg "$TARGET"
    echo
    tailscale funnel status
    ;;
  off)
    tailscale funnel reset
    tailscale serve reset
    echo "Unpublished. The stack is still running on ${TARGET} (this machine only)."
    ;;
  test-on)
    gateway_up || die "the gateway is not answering on ${TARGET} — run deploy/scripts/deploy.sh first"
    tailscale serve --bg --https=8443 "$TARGET"
    tailscale serve status
    ;;
  test-off)
    tailscale serve --https=8443 off
    ;;
  status)
    tailscale funnel status
    ;;
  *)
    die "unknown command '$1' (on | off | status | test-on | test-off)"
    ;;
esac
