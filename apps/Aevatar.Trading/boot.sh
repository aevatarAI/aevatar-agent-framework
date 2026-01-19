#!/usr/bin/env bash
set -euo pipefail

# ------------------------------------------------------------
# Trading System - Boot Script (AppHost)
#
# 端口约定：
#   7100  - Trading API
#   5173  - Frontend (Vite dev)
#   15888 - Aspire Dashboard
#   20888 - AppHost 内部管理端口
# ------------------------------------------------------------

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APPHOST_PROJECT="$SCRIPT_DIR/src/Aevatar.Trade.AppHost/Aevatar.Trade.AppHost.csproj"
FRONTEND_DIR="$SCRIPT_DIR/frontend"

# ------------------------------------------------------------
# PATH helper: ensure npm is visible to AppHost child process
# ------------------------------------------------------------
export PATH="/opt/homebrew/bin:/usr/local/bin:/usr/bin:${PATH}"

# ------------------------------------------------------------
# kill_port: 清理占用指定端口的进程
# ------------------------------------------------------------
kill_port() {
  local port="$1"
  local pids
  pids="$(lsof -nP -iTCP:"$port" -sTCP:LISTEN -t 2>/dev/null | tr '\n' ' ' || true)"
  if [[ -z "${pids// }" ]]; then
    return 0
  fi
  echo "[boot.sh] Killing port :$port -> ${pids}"
  # shellcheck disable=SC2086
  kill ${pids} 2>/dev/null || true
  sleep 0.5
  # Force kill if still alive
  pids="$(lsof -nP -iTCP:"$port" -sTCP:LISTEN -t 2>/dev/null | tr '\n' ' ' || true)"
  if [[ -n "${pids// }" ]]; then
    echo "[boot.sh] Force killing port :$port -> ${pids}"
    # shellcheck disable=SC2086
    kill -9 ${pids} 2>/dev/null || true
  fi
}

LAUNCH_PROFILE="${LAUNCH_PROFILE:-http}"
APP_ARGS=()

usage() {
  cat <<'EOF'
Usage: ./boot.sh [--launch-profile <name>] [-- <app args>]

Environment:
  LAUNCH_PROFILE   AppHost launch profile (default: http)
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --launch-profile|-p)
      if [[ $# -lt 2 ]]; then
        echo "ERROR: --launch-profile requires a value." >&2
        exit 2
      fi
      LAUNCH_PROFILE="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    --)
      shift
      APP_ARGS+=("$@")
      break
      ;;
    *)
      APP_ARGS+=("$1")
      shift
      ;;
  esac
done

# ------------------------------------------------------------
# Kill existing processes on target ports
# ------------------------------------------------------------
echo "[boot.sh] Cleaning up existing processes..."
kill_port 20888  # AppHost internal
kill_port 7100   # Trading API
kill_port 5173   # Frontend
kill_port 15888  # Aspire Dashboard

# ------------------------------------------------------------
# Pre-flight: ensure npm exists and dependencies installed
# ------------------------------------------------------------
if ! command -v npm >/dev/null 2>&1; then
  echo "ERROR: npm not found in PATH. Please install Node.js (18+ recommended)." >&2
  exit 1
fi

if [[ -d "$FRONTEND_DIR" && ! -d "$FRONTEND_DIR/node_modules" ]]; then
  echo "node_modules missing; running npm install..."
  (cd "$FRONTEND_DIR" && npm install)
fi

if [[ ${#APP_ARGS[@]} -gt 0 ]]; then
  dotnet run --project "$APPHOST_PROJECT" --launch-profile "$LAUNCH_PROFILE" -- "${APP_ARGS[@]}"
else
  dotnet run --project "$APPHOST_PROJECT" --launch-profile "$LAUNCH_PROFILE"
fi
