#!/usr/bin/env bash
set -euo pipefail

# ------------------------------------------------------------
# Axiom Reasoning - Boot Script (AppHost)
# ------------------------------------------------------------

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APPHOST_PROJECT="$SCRIPT_DIR/AxiomReasoning.AppHost/AxiomReasoning.AppHost.csproj"

dotnet run --project "$APPHOST_PROJECT" -- "$@"
