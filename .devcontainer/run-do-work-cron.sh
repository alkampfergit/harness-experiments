#!/usr/bin/env bash
set -uo pipefail

STATE_FILE="$HOME/.local/state/automata-do-work/runner.env"
if [[ ! -r "$STATE_FILE" ]]; then
    printf 'Automata cron state is missing: %s\n' "$STATE_FILE" >&2
    exit 1
fi
# This file is written by the checked-in start hook for the active worktree.
source "$STATE_FILE"
: "${AUTOMATA_REPO_DIR:?missing repo path}"
: "${AUTOMATA_SETUP_BRANCH:?missing setup branch}"
export AUTOMATA_REPO_DIR AUTOMATA_SETUP_BRANCH

cd "$AUTOMATA_REPO_DIR" || exit 1
CURRENT_BRANCH="$(git branch --show-current)"
if [[ "$CURRENT_BRANCH" != "$AUTOMATA_SETUP_BRANCH" ]]; then
    if [[ -n "$(git status --porcelain)" ]]; then
        printf 'Cannot restore Automata setup branch; checkout is dirty on %s\n' "$CURRENT_BRANCH" >&2
        exit 1
    fi
    git switch "$AUTOMATA_SETUP_BRANCH" || exit 1
fi

set +e
if [[ -x /usr/bin/zsh ]]; then
    /usr/bin/zsh -lic 'cd "$AUTOMATA_REPO_DIR" && automata do-work --silent'
else
    /bin/bash -lc 'cd "$AUTOMATA_REPO_DIR" && automata do-work --silent'
fi
STATUS=$?
set -e

CURRENT_BRANCH="$(git branch --show-current 2>/dev/null || true)"
if [[ "$CURRENT_BRANCH" != "$AUTOMATA_SETUP_BRANCH" ]]; then
    if [[ -z "$(git status --porcelain)" ]]; then
        if git switch "$AUTOMATA_SETUP_BRANCH"; then
            printf 'Restored Automata setup branch after tick.\n'
        else
            printf 'Failed to restore Automata setup branch %s.\n' "$AUTOMATA_SETUP_BRANCH" >&2
            STATUS=1
        fi
    else
        printf 'Did not switch from %s: worktree is dirty.\n' "$CURRENT_BRANCH" >&2
        STATUS=1
    fi
fi
exit "$STATUS"
