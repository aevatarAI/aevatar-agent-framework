#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CLI_DIR="${SCRIPT_DIR}/src/Aevatar.Platform.Cli"
PKG_DIR="${CLI_DIR}/bin/Release"

dotnet pack "${CLI_DIR}" -c Release
dotnet tool uninstall --global aevatar || true
dotnet tool install --global --add-source "${PKG_DIR}" aevatar
