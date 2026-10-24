#!/usr/bin/env bash
set -euo pipefail

SCRIPT_PATH="$(readlink -f "${BASH_SOURCE[0]}")"
REPO_DIR="$(cd "$(dirname "$SCRIPT_PATH")/.." && pwd)"
export AUTOMATA_REPO_DIR="$REPO_DIR"

cd "$REPO_DIR"
if [[ -x /usr/bin/zsh ]]; then
    exec /usr/bin/zsh -lic 'cd "$AUTOMATA_REPO_DIR" && exec automata do-work --silent'
fi
exec /bin/bash -lc 'cd "$AUTOMATA_REPO_DIR" && exec automata do-work --silent'
