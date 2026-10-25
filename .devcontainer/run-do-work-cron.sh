#!/usr/bin/env bash
set -u -o pipefail

cd /workspaces/harness-experiments || exit 1

# Load the configured login-shell PATH for global Node/npm tools.
if [[ -x /usr/bin/zsh ]]; then
    exec /usr/bin/zsh -lic 'cd /workspaces/harness-experiments && exec automata do-work --silent'
fi
exec /bin/bash -lc 'cd /workspaces/harness-experiments && exec automata do-work --silent'
