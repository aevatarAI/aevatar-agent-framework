#!/usr/bin/env bash
set -euo pipefail

# ============================================================
#  Aevatar.Secrets.Api - restart helper
#
#  What it does:
#  - Kill processes listening on ports 6667 and 6677 (TERM then KILL if needed)
#  - Start Aevatar.Secrets.Api, forcing ASPNETCORE_URLS to those ports
#
#  Usage:
#    ./restart.sh
#    ./restart.sh --no-build
#    ./restart.sh --release
#    ./restart.sh --kill-only
#
#  Notes:
#  - Chrome treats 6667 as an "unsafe port" and may show ERR_UNSAFE_PORT.
#    Use http://localhost:6677 in browsers.
# ============================================================

PORTS=(6667 6677)

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_PATH="$SCRIPT_DIR/Aevatar.Secrets.Api.csproj"

KILL_ONLY=0
NO_BUILD=0
CONFIGURATION="Debug"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --kill-only)
      KILL_ONLY=1
      shift
      ;;
    --no-build)
      NO_BUILD=1
      shift
      ;;
    --release)
      CONFIGURATION="Release"
      shift
      ;;
    -h|--help)
      sed -n '1,120p' "$0"
      exit 0
      ;;
    *)
      echo "Unknown arg: $1"
      echo "Try: $0 --help"
      exit 2
      ;;
  esac
done

port_pids() {
  local port="$1"
  # -nP: no DNS, show numeric ports; -t: pid only
  lsof -nP -iTCP:"$port" -sTCP:LISTEN -t 2>/dev/null || true
}

kill_port() {
  local port="$1"
  local pids
  pids="$(port_pids "$port")"
  if [[ -z "${pids//$'\n'/}" ]]; then
    echo "[ok] port $port: no listener"
    return 0
  fi

  echo "[..] port $port: terminating listener pid(s): ${pids//$'\n'/ }"
  # shellcheck disable=SC2086
  kill -TERM $pids 2>/dev/null || true

  # Wait up to ~4s for graceful exit.
  for _ in {1..20}; do
    if [[ -z "$(port_pids "$port")" ]]; then
      echo "[ok] port $port: released"
      return 0
    fi
    sleep 0.2
  done

  pids="$(port_pids "$port")"
  if [[ -n "${pids//$'\n'/}" ]]; then
    echo "[!!] port $port: force killing pid(s): ${pids//$'\n'/ }"
    # shellcheck disable=SC2086
    kill -KILL $pids 2>/dev/null || true
  fi

  # Best-effort final check.
  if [[ -z "$(port_pids "$port")" ]]; then
    echo "[ok] port $port: released"
  else
    echo "[warn] port $port: still appears to have a listener (check permissions or SIP restrictions)"
  fi
}

for p in "${PORTS[@]}"; do
  kill_port "$p"
done

if [[ "$KILL_ONLY" == "1" ]]; then
  exit 0
fi

export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://localhost:6667;http://localhost:6677}"

echo "[..] starting Aevatar.Secrets.Api"
echo "     project: $PROJECT_PATH"
echo "     urls:    $ASPNETCORE_URLS"
echo "     config:  $CONFIGURATION"

DOTNET_ARGS=(run --project "$PROJECT_PATH" -c "$CONFIGURATION")
if [[ "$NO_BUILD" == "1" ]]; then
  DOTNET_ARGS+=(--no-build)
fi

exec dotnet "${DOTNET_ARGS[@]}"


