#!/usr/bin/env bash
set -euo pipefail

# ------------------------------------------------------------
# Cognitive Mesh - Boot Script (AppHost)
# ------------------------------------------------------------

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APPHOST_PROJECT="$SCRIPT_DIR/CognitiveMesh.AppHost/CognitiveMesh.AppHost.csproj"

dotnet run --project "$APPHOST_PROJECT" -- "$@"
