#!/usr/bin/env bash
set -euo pipefail

# ============================================================
#  boot.sh (Aevatar.MakerSystem)
#  - Launch MakerSystem AppHost
# ============================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

exec dotnet run --project "$SCRIPT_DIR/MakerSystem.AppHost/MakerSystem.AppHost.csproj" "$@"
