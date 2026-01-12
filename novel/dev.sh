#!/usr/bin/env bash
set -euo pipefail

# ============================================================
#  Novel Dev Runner (Sidecar + Frontend)
#
#  GOAL:
#  - Start .NET sidecar and frontend concurrently for local development.
#  - Before starting, kill any process listening on the target ports.
#
#  DEFAULT PORTS (repo policy):
#  - Sidecar:  5678
#  - Frontend: 5173 (Vite dev server)
#
#  USAGE:
#    ./novel/dev.sh            # sidecar + tauri dev (default)
#    ./novel/dev.sh --web      # sidecar + vite web only
#    ./novel/dev.sh --no-kill  # do not kill ports before start
#
#  NOTES:
#  - This script is macOS/Linux friendly (uses lsof).
#  - Ctrl+C will stop both processes.
# ============================================================

SIDECAR_PORT="${SIDECAR_PORT:-5678}"
FRONTEND_PORT="${FRONTEND_PORT:-5173}"

FRONTEND_MODE="tauri" # tauri | web
KILL_BEFORE=1

usage() {
  cat <<'EOF'
Usage: ./novel/dev.sh [--web|--tauri] [--no-kill]

Options:
  --web      Start Vite dev server only (npm run dev:web)
  --tauri    Start Tauri desktop dev (npm run dev) [default]
  --no-kill  Do not kill listeners on ports before starting
  -h, --help Show this help

Environment:
  SIDECAR_PORT     Sidecar port (default: 5678)
  FRONTEND_PORT    Frontend (Vite) port to kill (default: 5173)

Examples:
  ./novel/dev.sh
  ./novel/dev.sh --web
  SIDECAR_PORT=5679 ./novel/dev.sh --web
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --web) FRONTEND_MODE="web"; shift ;;
    --tauri) FRONTEND_MODE="tauri"; shift ;;
    --no-kill) KILL_BEFORE=0; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown arg: $1" >&2; usage; exit 2 ;;
  esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
NOVEL_DIR="$SCRIPT_DIR"
SIDECAR_DIR="$NOVEL_DIR/src/Aevatar.Novel.Sidecar"
FRONTEND_DIR="$NOVEL_DIR/frontend"

if [[ ! -d "$SIDECAR_DIR" ]]; then
  echo "Sidecar directory not found: $SIDECAR_DIR" >&2
  exit 1
fi
if [[ ! -d "$FRONTEND_DIR" ]]; then
  echo "Frontend directory not found: $FRONTEND_DIR" >&2
  exit 1
fi

kill_port() {
  local port="$1"
  local pids=""

  if ! command -v lsof >/dev/null 2>&1; then
    echo "ERROR: lsof not found; cannot kill port ${port}." >&2
    return 1
  fi

  # Only kill LISTENers on TCP:PORT.
  pids="$(lsof -nP -iTCP:"${port}" -sTCP:LISTEN -t 2>/dev/null || true)"
  if [[ -z "$pids" ]]; then
    return 0
  fi

  echo "Killing listener(s) on TCP:${port}: ${pids}"
  # Print who is holding the port (useful when kill fails due to permissions).
  lsof -nP -iTCP:"${port}" -sTCP:LISTEN 2>/dev/null || true

  # Try graceful first.
  kill -TERM ${pids} 2>/dev/null || true
  sleep 0.6

  # Still alive? Force kill.
  pids="$(lsof -nP -iTCP:"${port}" -sTCP:LISTEN -t 2>/dev/null || true)"
  if [[ -n "$pids" ]]; then
    echo "Force killing listener(s) on TCP:${port}: ${pids}"
    kill -KILL ${pids} 2>/dev/null || true
  fi

  # Verify port is free; if not, fail fast with diagnostics.
  pids="$(lsof -nP -iTCP:"${port}" -sTCP:LISTEN -t 2>/dev/null || true)"
  if [[ -n "$pids" ]]; then
    echo "ERROR: TCP:${port} is still in use after kill attempts." >&2
    lsof -nP -iTCP:"${port}" -sTCP:LISTEN 2>/dev/null || true
    echo "" >&2
    echo "Hint: stop that process manually, or run with a different port:" >&2
    echo "  SIDECAR_PORT=5679 ./novel/dev.sh" >&2
    return 1
  fi
}

wait_for_http_ok() {
  local url="$1"
  local timeout_s="${2:-20}"

  if ! command -v curl >/dev/null 2>&1; then
    echo "WARN: curl not found; skip waiting for ${url}"
    return 0
  fi

  local start="${SECONDS}"
  while true; do
    if curl -fsS "${url}" >/dev/null 2>&1; then
      return 0
    fi
    if (( SECONDS - start >= timeout_s )); then
      echo "WARN: timeout waiting for ${url} (${timeout_s}s); starting frontend anyway."
      return 0
    fi
    sleep 0.3
  done
}

cleanup_ran=0
SIDECAR_PID=""
FRONTEND_PID=""

cleanup() {
  if [[ "$cleanup_ran" -eq 1 ]]; then
    return 0
  fi
  cleanup_ran=1

  echo ""
  echo "Stopping dev processes..."

  if [[ -n "${FRONTEND_PID}" ]]; then
    kill -TERM "${FRONTEND_PID}" 2>/dev/null || true
  fi
  if [[ -n "${SIDECAR_PID}" ]]; then
    kill -TERM "${SIDECAR_PID}" 2>/dev/null || true
  fi

  sleep 0.3
  # Ensure ports are released (covers child processes).
  kill_port "${FRONTEND_PORT}" || true
  kill_port "${SIDECAR_PORT}" || true
}

trap cleanup INT TERM EXIT

if [[ "${KILL_BEFORE}" -eq 1 ]]; then
  kill_port "${SIDECAR_PORT}"
  kill_port "${FRONTEND_PORT}"
fi

echo "Starting sidecar (ASPNETCORE_URLS=http://localhost:${SIDECAR_PORT})"
(
  cd "$SIDECAR_DIR"
  ASPNETCORE_URLS="http://localhost:${SIDECAR_PORT}" \
    dotnet run --no-launch-profile
) &
SIDECAR_PID="$!"

echo "Waiting for sidecar to become ready..."
wait_for_http_ok "http://localhost:${SIDECAR_PORT}/health" 25

echo "Starting frontend (${FRONTEND_MODE})"
(
  cd "$FRONTEND_DIR"
  export VITE_NOVEL_SIDECAR_URL="http://localhost:${SIDECAR_PORT}"
  if [[ "${FRONTEND_MODE}" == "web" ]]; then
    npm run dev:web
  else
    npm run dev
  fi
) &
FRONTEND_PID="$!"

echo ""
echo "Sidecar : http://localhost:${SIDECAR_PORT}"
echo "Frontend: http://localhost:${FRONTEND_PORT}"
echo "Press Ctrl+C to stop."
echo ""

# bash 3.2 friendly monitor loop: stop both if either exits.
while true; do
  if ! kill -0 "${SIDECAR_PID}" 2>/dev/null; then
    echo "Sidecar exited."
    exit 1
  fi
  if ! kill -0 "${FRONTEND_PID}" 2>/dev/null; then
    echo "Frontend exited."
    exit 1
  fi
  sleep 0.5
done


