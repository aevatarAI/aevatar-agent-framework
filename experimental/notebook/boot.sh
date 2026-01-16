#!/usr/bin/env bash
set -euo pipefail

# ============================================================
#  Notebook Dev Runner (API + legacy UI)
#
#  GOAL:
#  - Start .NET API for local development.
#  - The API serves the NotebookLM-like UI from wwwroot by default.
#  - Before starting, optionally kill any process listening on the target ports.
#
#  DEFAULT PORTS (repo policy):
#  - Backend (API + legacy UI): 5678
#
#  USAGE:
#    ./boot.sh           # api (serves legacy NotebookLM-like UI)
#    ./boot.sh --no-kill # do not kill ports before start
#
#  NOTES:
#  - macOS/Linux friendly (uses lsof).
#  - Ctrl+C will stop both processes.
# ============================================================

API_PORT="${API_PORT:-5678}"
KILL_BEFORE=1

usage() {
  cat <<'EOF'
Usage: ./boot.sh [--no-kill]

Options:
  --no-kill  Do not kill listeners on ports before starting
  -h, --help Show this help

Environment:
  API_PORT         Backend port (default: 5678)
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --no-kill) KILL_BEFORE=0; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown arg: $1" >&2; usage; exit 2 ;;
  esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
API_PROJECT="${SCRIPT_DIR}/src/Aevatar.Notebook.Api/Aevatar.Notebook.Api.csproj"

if [[ ! -f "${API_PROJECT}" ]]; then
  echo "API project not found: ${API_PROJECT}" >&2
  exit 1
fi

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
API_PID=""
FRONTEND_PID=""

cleanup() {
  if [[ "${cleanup_ran}" -eq 1 ]]; then
    return 0
  fi
  cleanup_ran=1

  echo ""
  echo "Stopping processes..."
  if [[ -n "${API_PID}" ]]; then
    kill -TERM "${API_PID}" 2>/dev/null || true
  fi

  sleep 0.3
  kill_port "${API_PORT}" || true
}

trap cleanup INT TERM EXIT

if [[ "${KILL_BEFORE}" -eq 1 ]]; then
  kill_port "${API_PORT}"
fi

echo "Starting API (ASPNETCORE_URLS=http://localhost:${API_PORT})"
(
  cd "${REPO_ROOT}"
  ASPNETCORE_URLS="http://localhost:${API_PORT}" \
    dotnet run --project "${API_PROJECT}" --no-launch-profile
) &
API_PID="$!"

echo "Waiting for API to become ready..."
wait_for_http_ok "http://localhost:${API_PORT}/health" 25

echo ""
echo "Notebook UI: http://localhost:${API_PORT}"
echo "Press Ctrl+C to stop."
echo ""

while true; do
  if ! kill -0 "${API_PID}" 2>/dev/null; then
    echo "API exited."
    exit 1
  fi
  sleep 0.5
done


