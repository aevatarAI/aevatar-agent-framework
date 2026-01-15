#!/usr/bin/env bash
set -euo pipefail

# ------------------------------------------------------------
# Aevatar Kit - Boot Script (API)
# ------------------------------------------------------------

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
API_PROJECT="$SCRIPT_DIR/src/AevatarKit.Api/AevatarKit.Api.csproj"

dotnet run --project "$API_PROJECT" -- "$@"
