#!/usr/bin/env bash
set -euo pipefail

# ============================================================
#  Vibe Researching - Boot Script (Backend + Frontend)
#
#  DEFAULT PORTS:
#  - Backend:  5678  (HTTP, VibeResearching API with ABP Identity)
#  - Frontend: 5173  (Vite dev server)
#
#  USAGE:
#    ./boot.sh                    # Start backend + frontend
#    ./boot.sh --backend-only     # Start only backend
#    ./boot.sh --frontend-only    # Start only frontend
#    ./boot.sh --no-kill          # Don't kill existing processes
# ============================================================

BACKEND_PORT="${BACKEND_PORT:-5678}"
FRONTEND_PORT="${FRONTEND_PORT:-5173}"

# MongoDB (required for ABP Identity)
MONGODB_CONNECTION_STRING="${MONGODB_CONNECTION_STRING:-mongodb://localhost:27017}"

# Neo4j (optional, for knowledge graph)
NEO4J_URI="${NEO4J_URI:-bolt://localhost:7687}"
NEO4J_USERNAME="${NEO4J_USERNAME:-neo4j}"
NEO4J_PASSWORD="${NEO4J_PASSWORD:-}"

KILL_BEFORE=1
RUN_BACKEND=1
RUN_FRONTEND=1

usage() {
  cat <<'EOF'
Usage: ./boot.sh [OPTIONS]

Environment:
  BACKEND_PORT                Backend port (default: 5678)
  FRONTEND_PORT               Frontend (Vite) port (default: 5173)
  MONGODB_CONNECTION_STRING   MongoDB URI (default: mongodb://localhost:27017)
  NEO4J_URI                   Neo4j bolt URI (default: bolt://localhost:7687)
  NEO4J_USERNAME              Neo4j username (default: neo4j)
  NEO4J_PASSWORD              Neo4j password (required for Neo4j features)

Options:
  --backend-only     Only start the backend
  --frontend-only    Only start the frontend
  --no-kill          Do not kill listeners on ports before starting
  -h, --help         Show this help

Examples:
  ./boot.sh                    # Backend (5678) + Frontend (5173)
  ./boot.sh --backend-only     # Only Backend (5678)
  ./boot.sh --frontend-only    # Only Frontend (5173)

  # With custom MongoDB:
  MONGODB_CONNECTION_STRING="mongodb://user:pass@host:27017" ./boot.sh
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --no-kill) KILL_BEFORE=0; shift ;;
    --backend-only) RUN_FRONTEND=0; shift ;;
    --frontend-only) RUN_BACKEND=0; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown arg: $1" >&2; usage; exit 2 ;;
  esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BACKEND_DIR="$SCRIPT_DIR/src/host/HttpApi.Host"
FRONTEND_DIR="$SCRIPT_DIR/sisyphus-frontend"

kill_port() {
  local port="$1"
  local pids=""

  if ! command -v lsof >/dev/null 2>&1; then
    echo "ERROR: lsof not found; cannot kill port ${port}." >&2
    return 1
  fi

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
      echo "WARN: timeout waiting for ${url} (${timeout_s}s); continuing anyway."
      return 0
    fi
    sleep 0.3
  done
}

cleanup_ran=0
BACKEND_PID=""
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
  if [[ -n "${BACKEND_PID}" ]]; then
    kill -TERM "${BACKEND_PID}" 2>/dev/null || true
  fi

  sleep 0.3
  [[ "${RUN_FRONTEND}" -eq 1 ]] && kill_port "${FRONTEND_PORT}" || true
  [[ "${RUN_BACKEND}" -eq 1 ]] && kill_port "${BACKEND_PORT}" || true
}

trap cleanup INT TERM EXIT

# Kill ports before starting
if [[ "${KILL_BEFORE}" -eq 1 ]]; then
  [[ "${RUN_BACKEND}" -eq 1 ]] && kill_port "${BACKEND_PORT}"
  [[ "${RUN_FRONTEND}" -eq 1 ]] && kill_port "${FRONTEND_PORT}"
fi

# ============================================================
# Start Backend (VibeResearching HttpApi.Host with ABP Identity)
# ============================================================
if [[ "${RUN_BACKEND}" -eq 1 ]]; then
  echo "Starting backend (http://localhost:${BACKEND_PORT})"
  (
    cd "$BACKEND_DIR"
    export MONGODB_CONNECTION_STRING="${MONGODB_CONNECTION_STRING}"
    export NEO4J_URI="${NEO4J_URI}"
    export NEO4J_USERNAME="${NEO4J_USERNAME}"
    export NEO4J_PASSWORD="${NEO4J_PASSWORD}"
    ASPNETCORE_URLS="http://localhost:${BACKEND_PORT}" \
      dotnet run --no-launch-profile
  ) &
  BACKEND_PID="$!"

  echo "Waiting for backend to become ready..."
  wait_for_http_ok "http://localhost:${BACKEND_PORT}/health" 30
fi

# ============================================================
# Start Frontend (Vite)
# ============================================================
if [[ "${RUN_FRONTEND}" -eq 1 ]]; then
  if ! command -v npm >/dev/null 2>&1; then
    echo "ERROR: npm command not found." >&2
    echo "Please install Node.js and npm first:" >&2
    echo "  - macOS: brew install node" >&2
    echo "  - Or visit: https://nodejs.org/" >&2
    echo "" >&2
    echo "Skipping frontend startup. Use --backend-only to suppress this message." >&2
    RUN_FRONTEND=0
  else
    echo "Starting frontend (http://localhost:${FRONTEND_PORT})"
    (
      cd "$FRONTEND_DIR"
      # NOTE: VITE_API_BASE_URL is intentionally NOT set here.
      # Empty value makes frontend use Vite proxy (/api/* -> backend),
      # which correctly handles cookies for authentication.
      # BACKEND_PORT is used by vite.config.ts to configure proxy target.
      export BACKEND_PORT="${BACKEND_PORT}"
      export PORT="${FRONTEND_PORT}"

      if [[ ! -d "node_modules" ]]; then
        echo "Frontend: Installing npm dependencies..."
        npm install
      fi

      npm run dev -- --port "${FRONTEND_PORT}"
    ) &
    FRONTEND_PID="$!"
  fi
fi

# ============================================================
# Summary
# ============================================================
echo ""
echo "============================================================"
[[ "${RUN_BACKEND}" -eq 1 ]] && echo "  Backend  : http://localhost:${BACKEND_PORT}"
[[ "${RUN_FRONTEND}" -eq 1 ]] && echo "  Frontend : http://localhost:${FRONTEND_PORT}"
echo "============================================================"
echo "  Press Ctrl+C to stop."
echo ""

# Monitor loop: stop if any running process exits
while true; do
  if [[ "${RUN_BACKEND}" -eq 1 ]] && [[ -n "${BACKEND_PID}" ]]; then
    if ! kill -0 "${BACKEND_PID}" 2>/dev/null; then
      echo "Backend exited."
      exit 1
    fi
  fi
  if [[ "${RUN_FRONTEND}" -eq 1 ]] && [[ -n "${FRONTEND_PID}" ]]; then
    if ! kill -0 "${FRONTEND_PID}" 2>/dev/null; then
      echo "Frontend exited."
      exit 1
    fi
  fi
  sleep 0.5
done
