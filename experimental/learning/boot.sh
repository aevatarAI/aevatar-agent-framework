#!/usr/bin/env bash
set -euo pipefail

# ============================================================
#  Learning System Dev Runner (API + Frontend)
#
#  GOAL:
#  - Start backend API and frontend concurrently for local development.
#  - Optionally kill any process listening on the target ports.
#  - Wait for /health before starting the frontend.
#
#  DEFAULT PORTS (repo policy):
#  - Backend:  5678
#  - Frontend: 5173 (Vite dev server)
#
#  USAGE:
#    ./boot.sh            # backend + tauri dev (default)
#    ./boot.sh --web      # backend + vite web only
#    ./boot.sh --no-kill  # do not kill ports before start
# ============================================================

LEARNING_API_PORT="${LEARNING_API_PORT:-5678}"
LEARNING_FRONTEND_PORT="${LEARNING_FRONTEND_PORT:-5173}"

FRONTEND_MODE="tauri" # tauri | web
KILL_BEFORE=1

usage() {
  cat <<'EOF'
Usage: ./boot.sh [--web|--tauri] [--no-kill]

Options:
  --web      Start Vite dev server only (npm run dev:web)
  --tauri    Start Tauri desktop dev (npm run dev) [default]
  --no-kill  Do not kill listeners on ports before starting
  -h, --help Show this help

Environment:
  LEARNING_API_PORT        Backend API port (default: 5678)
  LEARNING_FRONTEND_PORT   Frontend (Vite) port (default: 5173)
  LEARNING_NOTEBOOK_ROOT   Notebook root directory (optional; default handled by backend)

Examples:
  ./boot.sh
  ./boot.sh --web
  LEARNING_API_PORT=5679 ./boot.sh --web
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
LEARNING_DIR="$SCRIPT_DIR"
API_DIR="$LEARNING_DIR/src/Aevatar.Learning.Api"
FRONTEND_DIR="$LEARNING_DIR/frontend"

if [[ ! -d "$API_DIR" ]]; then
  echo "Backend directory not found: $API_DIR" >&2
  echo "Hint: implement tasks under experimental/learning/tasks.md first (API scaffold), then re-run." >&2
  exit 1
fi
if [[ ! -d "$FRONTEND_DIR" ]]; then
  echo "Frontend directory not found: $FRONTEND_DIR" >&2
  echo "Hint: implement tasks under experimental/learning/tasks.md first (frontend scaffold), then re-run." >&2
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
  lsof -nP -iTCP:"${port}" -sTCP:LISTEN 2>/dev/null || true

  kill -TERM ${pids} 2>/dev/null || true
  sleep 0.6

  pids="$(lsof -nP -iTCP:"${port}" -sTCP:LISTEN -t 2>/dev/null || true)"
  if [[ -n "$pids" ]]; then
    echo "Force killing listener(s) on TCP:${port}: ${pids}"
    kill -KILL ${pids} 2>/dev/null || true
  fi

  pids="$(lsof -nP -iTCP:"${port}" -sTCP:LISTEN -t 2>/dev/null || true)"
  if [[ -n "$pids" ]]; then
    echo "ERROR: TCP:${port} is still in use after kill attempts." >&2
    lsof -nP -iTCP:"${port}" -sTCP:LISTEN 2>/dev/null || true
    echo "" >&2
    echo "Hint: stop that process manually, or run with a different port:" >&2
    echo "  LEARNING_API_PORT=5679 ./boot.sh --web" >&2
    return 1
  fi
}

wait_for_http_ok() {
  local url="$1"
  local timeout_s="${2:-25}"

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

ensure_frontend_deps() {
  if ! command -v npm >/dev/null 2>&1; then
    echo "ERROR: npm not found. Please install Node.js (npm) first." >&2
    echo "Hint (macOS): https://nodejs.org/ or use your preferred package manager." >&2
    return 1
  fi

  # Common failure mode: boot.sh runs before `npm install`, causing Vite to fail loading config:
  #   Error [ERR_MODULE_NOT_FOUND]: Cannot find package 'vite' imported from .../vite.config.ts...
  #
  # Preflight: ensure node_modules exists AND vite package is installed.
  if [[ ! -d "${FRONTEND_DIR}/node_modules" ]] || [[ ! -f "${FRONTEND_DIR}/node_modules/vite/package.json" ]]; then
    echo "Installing frontend dependencies (first run)..."
    (
      cd "${FRONTEND_DIR}"
      npm install
    )
  fi
}

cleanup_ran=0
API_PID=""
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
  if [[ -n "${API_PID}" ]]; then
    kill -TERM "${API_PID}" 2>/dev/null || true
  fi

  sleep 0.3
  kill_port "${LEARNING_FRONTEND_PORT}" || true
  kill_port "${LEARNING_API_PORT}" || true
}

trap cleanup INT TERM EXIT

if [[ "${KILL_BEFORE}" -eq 1 ]]; then
  kill_port "${LEARNING_API_PORT}"
  kill_port "${LEARNING_FRONTEND_PORT}"
fi

echo "Starting backend (ASPNETCORE_URLS=http://localhost:${LEARNING_API_PORT})"
(
  cd "$API_DIR"
  ASPNETCORE_URLS="http://localhost:${LEARNING_API_PORT}" \
  LEARNING_NOTEBOOK_ROOT="${LEARNING_NOTEBOOK_ROOT:-}" \
    dotnet run --no-launch-profile
) &
API_PID="$!"

echo "Waiting for backend to become ready..."
wait_for_http_ok "http://localhost:${LEARNING_API_PORT}/health" 25

echo "Starting frontend (${FRONTEND_MODE})"
(
  cd "$FRONTEND_DIR"
  ensure_frontend_deps
  export VITE_LEARNING_API_URL="http://localhost:${LEARNING_API_PORT}"
  if [[ "${FRONTEND_MODE}" == "web" ]]; then
    npm run dev:web
  else
    npm run dev
  fi
) &
FRONTEND_PID="$!"

echo ""
echo "Backend : http://localhost:${LEARNING_API_PORT}"
echo "Frontend: http://localhost:${LEARNING_FRONTEND_PORT}"
echo "Press Ctrl+C to stop."
echo ""

# bash 3.2 friendly monitor loop: stop both if either exits.
while true; do
  if ! kill -0 "${API_PID}" 2>/dev/null; then
    echo "Backend exited."
    exit 1
  fi
  if ! kill -0 "${FRONTEND_PID}" 2>/dev/null; then
    echo "Frontend exited."
    exit 1
  fi
  sleep 0.5
done


