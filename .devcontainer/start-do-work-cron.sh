#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CRON_ENTRY="$REPO_DIR/.devcontainer/automata-do-work.cron"
RUNNER="$REPO_DIR/.devcontainer/run-do-work-cron.sh"
INSTALLED_RUNNER=/usr/local/bin/automata-do-work-cron

if [[ ! -x /usr/sbin/cron ]]; then
    sudo apt-get update
    sudo apt-get install -y cron
fi

sudo ln -sfn "$RUNNER" "$INSTALLED_RUNNER"
sudo install -m 0644 "$CRON_ENTRY" /etc/cron.d/automata-do-work
install -d -m 0700 "$HOME/.local/state/automata-do-work"

if ! pgrep -x cron >/dev/null 2>&1; then
    sudo /usr/sbin/cron
fi

printf 'cron installed and running; entry: /etc/cron.d/automata-do-work\n'
