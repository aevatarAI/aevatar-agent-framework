#!/usr/bin/env bash
set -euo pipefail

# ------------------------------------------------------------
# Trading System - Boot Script (AppHost)
# ------------------------------------------------------------

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APPHOST_PROJECT="$SCRIPT_DIR/src/Aevatar.Trade.AppHost/Aevatar.Trade.AppHost.csproj"

dotnet run --project "$APPHOST_PROJECT" -- "$@"
