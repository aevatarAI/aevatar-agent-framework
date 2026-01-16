#!/usr/bin/env bash
set -euo pipefail

# ------------------------------------------------------------
# Promote an experimental app into /apps
# ------------------------------------------------------------

usage() {
  cat <<'USAGE'
Usage:
  ./experimental/promote.sh <app-name> [--copy]

Options:
  --copy   Copy instead of move (default: move)

Examples:
  ./experimental/promote.sh notebook
  ./experimental/promote.sh notebook --copy
USAGE
}

if [[ $# -lt 1 ]]; then
  usage
  exit 2
fi

APP_NAME="$1"
shift || true

MODE="move"
if [[ ${1:-} == "--copy" ]]; then
  MODE="copy"
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
EXPERIMENTAL_DIR="${REPO_ROOT}/experimental/${APP_NAME}"
APPS_DIR="${REPO_ROOT}/apps/${APP_NAME}"

if [[ ! -d "${EXPERIMENTAL_DIR}" ]]; then
  echo "ERROR: not found: ${EXPERIMENTAL_DIR}" >&2
  exit 1
fi

if [[ -e "${APPS_DIR}" ]]; then
  echo "ERROR: target exists: ${APPS_DIR}" >&2
  exit 1
fi

if [[ "${MODE}" == "copy" ]]; then
  cp -R "${EXPERIMENTAL_DIR}" "${APPS_DIR}"
  echo "Copied ${EXPERIMENTAL_DIR} -> ${APPS_DIR}"
  exit 0
fi

if command -v git >/dev/null 2>&1 && git -C "${REPO_ROOT}" rev-parse --git-dir >/dev/null 2>&1; then
  git -C "${REPO_ROOT}" mv "experimental/${APP_NAME}" "apps/${APP_NAME}"
else
  mv "${EXPERIMENTAL_DIR}" "${APPS_DIR}"
fi

echo "Moved ${EXPERIMENTAL_DIR} -> ${APPS_DIR}"
