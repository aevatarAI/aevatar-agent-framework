#!/usr/bin/env bash
set -euo pipefail

# ------------------------------------------------------------
# Paper Review - Boot Script (AppHost)
# ------------------------------------------------------------

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APPHOST_PROJECT="$SCRIPT_DIR/PaperReview.AppHost/PaperReview.AppHost.csproj"

dotnet run --project "$APPHOST_PROJECT" -- "$@"
