#!/usr/bin/env bash
set -u -o pipefail

REPO_DIR="/workspaces/harness-experiments"
SETUP_BRANCH="develop"

cd "$REPO_DIR" || exit 1

# Serialize cron invocations: hold an exclusive lock for the whole run so that two
# runners never switch branches or start ticks concurrently. Sampling Automata's lock
# state below is only a guard against a manually started tick.
exec 9>"$REPO_DIR/.git/polifemo-do-work.lock"
if ! flock -n 9; then
    printf 'Another do-work runner holds the checkout lock; skipping.\n'
    exit 0
fi

# do-work can leave the shared checkout on its base branch. Check its global
# lock before switching; the JSON report remains parseable even on a base
# branch where this feature's .automata/config.json is absent.
lock_state() {
    local report
    report=$(/usr/bin/zsh -lic 'cd /workspaces/harness-experiments && automata do-work --check --no-fetch --json' 2>/dev/null) || true
    printf '%s' "$report" | python3 -c 'import json,sys
try:
    data=json.load(sys.stdin)
    lock=data["sections"]["lock"]["data"]
    print("busy" if lock.get("tickInFlight") else "free")
except Exception:
    print("unknown")'
    return 0
}

state=$(lock_state)
if [[ "$state" == "busy" ]]; then
    printf 'Automata tick already active; skipping branch restore/start.\n'
    exit 0
fi
if [[ "$state" != "free" ]]; then
    printf 'Cannot verify Automata lock; refusing to switch branches.\n' >&2
    exit 1
fi

if [[ -n "$(git status --porcelain --untracked-files=all)" ]]; then
    printf 'Worktree is dirty; refusing to switch to setup branch.\n' >&2
    git status --short --branch >&2
    exit 1
fi

if [[ "$(git branch --show-current)" != "$SETUP_BRANCH" ]]; then
    git switch "$SETUP_BRANCH" || exit 1
fi

if [[ -x /usr/bin/zsh ]]; then
    /usr/bin/zsh -lic 'cd /workspaces/harness-experiments && exec automata do-work --silent'
else
    /bin/bash -lc 'cd /workspaces/harness-experiments && exec automata do-work --silent'
fi
run_status=$?

# Restore the config/setup branch only after the run released its lock and
# left a clean checkout. Preserve any agent edits for inspection otherwise.
state=$(lock_state)
if [[ "$state" == "free" && -z "$(git status --porcelain --untracked-files=all)" && "$(git branch --show-current)" != "$SETUP_BRANCH" ]]; then
    git switch "$SETUP_BRANCH" || printf 'Could not restore setup branch; leaving current branch in place.\n' >&2
elif [[ "$state" != "free" ]]; then
    printf 'Automata lock remains active; leaving checkout untouched.\n' >&2
elif [[ -n "$(git status --porcelain --untracked-files=all)" ]]; then
    printf 'Worktree has changes after tick; leaving it untouched for inspection.\n' >&2
fi

exit "$run_status"
