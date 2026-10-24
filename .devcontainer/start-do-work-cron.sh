#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CRON_ENTRY="$REPO_DIR/.devcontainer/automata-do-work.cron"
RUNNER="$REPO_DIR/.devcontainer/run-do-work-cron.sh"
INSTALLED_RUNNER=/usr/local/bin/automata-do-work-cron
STATE_DIR="$HOME/.local/state/automata-do-work"
SETUP_BRANCH="$(git -C "$REPO_DIR" branch --show-current)"

if [[ -z "$SETUP_BRANCH" ]]; then
    printf 'Cannot install Automata cron from a detached checkout.\n' >&2
    exit 1
fi
if [[ ! -x /usr/sbin/cron ]]; then
    sudo apt-get update
    sudo apt-get install -y cron
fi

install -d -m 0700 "$STATE_DIR"
printf 'AUTOMATA_REPO_DIR=%q\nAUTOMATA_SETUP_BRANCH=%q\n' "$REPO_DIR" "$SETUP_BRANCH" > "$STATE_DIR/runner.env"
chmod 0600 "$STATE_DIR/runner.env"
sudo install -m 0755 "$RUNNER" "$INSTALLED_RUNNER"
sudo install -m 0644 "$CRON_ENTRY" /etc/cron.d/automata-do-work

if ! pgrep -x cron >/dev/null 2>&1; then
    sudo /usr/sbin/cron
fi

printf 'cron installed and running; entry: /etc/cron.d/automata-do-work\n'
printf 'repo: %s; setup branch: %s\n' "$REPO_DIR" "$SETUP_BRANCH"
