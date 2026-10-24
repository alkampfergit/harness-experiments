#!/usr/bin/env bash
# Read-only health check for the in-container Automata do-work scheduler.
set -uo pipefail

REPO_DIR="${AUTOMATA_REPO_DIR:-$(git rev-parse --show-toplevel 2>/dev/null || pwd)}"
FAIL=0
ok() { printf 'OK: %s\n' "$1"; }
fail() { printf 'FAIL: %s\n' "$1"; FAIL=1; }

cd "$REPO_DIR" || { fail "cannot enter $REPO_DIR"; exit 1; }
printf 'Automata cron health: %s\nRepo: %s\n' "$(date -Is)" "$REPO_DIR"

pgrep -x cron >/dev/null 2>&1 && ok 'cron daemon running' || fail 'cron daemon not running'
[[ -x /usr/local/bin/automata-do-work-cron ]] && ok 'runner installed' || fail 'runner missing'
grep -Fq '/usr/local/bin/automata-do-work-cron' /etc/cron.d/automata-do-work 2>/dev/null && ok 'cron entry installed' || fail 'cron entry missing'
command -v automata >/dev/null 2>&1 && ok "Automata $(automata --version 2>/dev/null) available" || fail 'Automata CLI unavailable'

LOG="$HOME/.local/state/automata-do-work/cron.log"
if [[ -f "$LOG" ]]; then
    AGE=$(python3 -c 'import os,sys,time; print(int(time.time()-os.path.getmtime(sys.argv[1])))' "$LOG")
    ok "cron log exists; last write ${AGE}s ago"
else
    fail 'cron log not written yet'
fi

if [[ -f /etc/cron.d/automata-do-work ]]; then
    grep -Fq 'vscode' /etc/cron.d/automata-do-work && ok 'cron runs as vscode' || fail 'cron user field is not vscode'
fi

if [[ -n "$(git status --porcelain)" ]]; then
    fail 'worktree is dirty; Automata may skip or rescue it'
else
    ok 'worktree clean'
fi

git config user.name >/dev/null && git config user.email >/dev/null && ok 'Git author configured' || fail 'Git author missing'

if [[ "$FAIL" == 0 ]]; then
    printf 'RESULT: healthy\n'
else
    printf 'RESULT: unhealthy\n'
fi
exit "$FAIL"
