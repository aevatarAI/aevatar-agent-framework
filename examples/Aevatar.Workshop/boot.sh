#!/bin/zsh
set -euo pipefail

ROOT="/Users/zhaoyiqi/Code/aevatar-agent-framework"
PORT="5691"
PROJECT="$ROOT/examples/Aevatar.Workshop/Aevatar.Workshop.csproj"

echo "[boot] Killing any process on :$PORT ..."
PIDS=$(lsof -ti tcp:"$PORT" || true)
if [[ -n "$PIDS" ]]; then
  echo "$PIDS" | xargs kill -9
  echo "[boot] Killed: $PIDS"
else
  echo "[boot] No process bound to :$PORT"
fi

echo "[boot] Starting Aevatar.Workshop on http://127.0.0.1:$PORT"
exec dotnet run --project "$PROJECT"
