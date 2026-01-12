#!/usr/bin/env bash
set -euo pipefail

# ============================================================================
#  trade/start.sh
#  - 一键清理旧的 trade system 进程（AppHost / Trading API / Frontend）
#  - 然后启动新的 Aspire AppHost
#
#  macOS 依赖：
#  - lsof, kill, dotnet, npm
#
#  用法：
#    chmod +x ./start.sh
#    ./start.sh                # 默认 --no-build（更快）
#    ./start.sh --build        # 先 build 再启动
#
#  说明：
#  - 通过端口清理，避免误杀无关 dotnet 进程。
#  - 端口约定（默认）：
#    - 20888: AppHost 内部管理端口（Aspire orchestration）
#    - 15888: Aspire Dashboard
#    - 7100 : Trading API
#    - 5173 : Frontend (Vite dev)
# ============================================================================

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

BUILD=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --build)
      BUILD=1
      shift
      ;;
    --no-build)
      BUILD=0
      shift
      ;;
    *)
      echo "Unknown arg: $1"
      echo "Usage: ./start.sh [--build|--no-build]"
      exit 2
      ;;
  esac
done

kill_port() {
  local port="$1"
  local pids
  pids="$(lsof -nP -iTCP:"$port" -sTCP:LISTEN -t 2>/dev/null | tr '\n' ' ' || true)"
  if [[ -z "${pids// }" ]]; then
    return 0
  fi

  echo "[start.sh] Killing port :$port -> ${pids}"
  # shellcheck disable=SC2086
  kill ${pids} 2>/dev/null || true

  # Give it a moment to exit gracefully.
  sleep 0.6

  pids="$(lsof -nP -iTCP:"$port" -sTCP:LISTEN -t 2>/dev/null | tr '\n' ' ' || true)"
  if [[ -n "${pids// }" ]]; then
    echo "[start.sh] Force killing port :$port -> ${pids}"
    # shellcheck disable=SC2086
    kill -9 ${pids} 2>/dev/null || true
  fi
}

echo "[start.sh] == Stop existing trade system =="
# Kill AppHost first (it should stop the whole orchestration), then cleanup any leftovers.
kill_port 20888
kill_port 7100
kill_port 5173
kill_port 15888

if [[ "$BUILD" -eq 1 ]]; then
  echo "[start.sh] == Build =="
  cd "$ROOT_DIR"
  dotnet build ./Aevatar.Trade.Api/Aevatar.Trade.Api.csproj -v minimal
fi

echo "[start.sh] == Start Aspire AppHost =="
cd "$ROOT_DIR"
if [[ "$BUILD" -eq 1 ]]; then
  exec dotnet run --project ./Aevatar.Trade.AppHost/Aevatar.Trade.AppHost.csproj
else
  exec dotnet run --project ./Aevatar.Trade.AppHost/Aevatar.Trade.AppHost.csproj --no-build
fi


