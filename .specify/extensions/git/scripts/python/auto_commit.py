#!/usr/bin/env python3
"""Git extension: auto_commit.py

Automatically commit changes after a Spec Kit command completes.
Python port of ``auto-commit.sh`` / ``auto-commit.ps1``.
Checks per-command config keys in git-config.yml before committing.

Usage: auto_commit.py <event_name> [generated_message]
       auto_commit.py <event_name> --message-file <path>
  e.g.: auto_commit.py after_specify
  e.g.: auto_commit.py after_specify --message-file /tmp/commit-msg.txt  (commit_style: conventional)

The message file is caller-owned: it is read but never deleted here. It must
live under the project root or the system temporary directory.
SPECIFY_INIT_DIR, when set, selects the project root and is validated before
configuration is read or Git is run.
"""

from __future__ import annotations

import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

_QUOTES = "\"'"
_COMMIT_STYLES = ("fixed", "conventional")
_SPECIFY_DIR = ".specify"
_MAX_MESSAGE_BYTES = 64 * 1024


class _ScriptError(Exception):
    """A fatal input or environment problem; the message is printed and the script exits 1."""


def _find_project_root(start: Path) -> Path | None:
    current = start
    while True:
        if (current / _SPECIFY_DIR).is_dir() or (current / ".git").exists():
            return current
        if current.parent == current:
            return None
        current = current.parent


def _resolve_init_dir(value: str) -> Path:
    """Validate an explicit SPECIFY_INIT_DIR (mirrors core resolve_specify_init_dir)."""
    candidate = Path(value).resolve()
    if not candidate.is_dir():
        raise _ScriptError(
            f"ERROR: SPECIFY_INIT_DIR does not point to an existing directory: {value}"
        )
    if not (candidate / _SPECIFY_DIR).is_dir():
        raise _ScriptError(
            "ERROR: SPECIFY_INIT_DIR is not a Spec Kit project (no .specify/ directory): "
            f"{candidate}"
        )
    return candidate


def _resolve_repo_root(script_dir: Path) -> Path:
    init_dir = os.environ.get("SPECIFY_INIT_DIR", "")
    if init_dir:
        return _resolve_init_dir(init_dir)
    return _find_project_root(script_dir) or Path.cwd()


def _locate_message_file(path_arg: str, allowed_roots: list[Path]) -> int:
    """Return the index of the first allowed root holding the file.

    Only the index is returned, never a path built from ``path_arg``, so the
    caller rebuilds the trusted root itself before validating and opening.

    Every root is tried in order, so a relative path missing under the project
    is still found in the system temporary directory. A root on another drive
    (Windows) simply does not contain the file.
    """
    inside_a_root = False
    for index, root in enumerate(allowed_roots):
        base = os.path.realpath(str(root))
        candidate = os.path.realpath(os.path.join(base, path_arg))
        try:
            contained = os.path.commonpath([base, candidate]) == base
        except ValueError:
            contained = False
        if not contained:
            continue
        inside_a_root = True
        if os.path.isfile(candidate):
            return index
    if inside_a_root:
        raise _ScriptError(f"[specify] Error: message file '{path_arg}' not found")
    raise _ScriptError(
        f"[specify] Error: message file '{path_arg}' must be inside the project "
        "or the system temporary directory"
    )


def _listed_path(base: str, relative: str) -> str:
    """Rebuild ``base/relative`` from directory listings of the trusted root.

    Each component is taken from ``os.listdir`` (matched by equality), so the
    path that is opened is built from the file system, not from the caller's
    argument. ``relative`` must already be normalised (no ``..``, no symlinks).
    """
    current = base
    for part in relative.split(os.sep):
        match = next((entry for entry in os.listdir(current) if entry == part), None)
        if match is None:
            raise _ScriptError(f"[specify] Error: message file '{relative}' not found")
        current = os.path.join(current, match)
    return current


def _read_message_file(path_arg: str, allowed_roots: list[Path]) -> tuple[str, str]:
    """Return (message, resolved path) of a caller-owned message file.

    Only a regular file of bounded size under the project root or the system
    temporary directory is accepted (symlinks resolved first). The resolved
    path is checked inside its root, then rebuilt from directory listings of
    that root before it is opened.
    """
    if not path_arg:
        raise _ScriptError("[specify] Error: --message-file requires a path argument")
    root_index = _locate_message_file(path_arg, allowed_roots)
    base = os.path.realpath(str(allowed_roots[root_index]))
    message_path = os.path.realpath(os.path.join(base, path_arg))
    if os.path.commonpath([base, message_path]) != base:
        raise _ScriptError(
            f"[specify] Error: message file '{path_arg}' must be inside the project "
            "or the system temporary directory"
        )
    message_path = _listed_path(base, os.path.relpath(message_path, base))
    if not os.path.isfile(message_path):
        raise _ScriptError(f"[specify] Error: message file '{path_arg}' not found")
    if os.path.getsize(message_path) > _MAX_MESSAGE_BYTES:
        raise _ScriptError(
            f"[specify] Error: message file '{path_arg}' is larger than "
            f"{_MAX_MESSAGE_BYTES} bytes"
        )
    try:
        with open(message_path, encoding="utf-8") as handle:
            text = handle.read(_MAX_MESSAGE_BYTES + 1)
    except (OSError, UnicodeDecodeError) as exc:
        raise _ScriptError(
            f"[specify] Error: cannot read message file '{path_arg}': {exc}"
        ) from exc
    return text.rstrip("\r\n"), message_path


def _parse_message_args(args: list[str], allowed_roots: list[Path]) -> tuple[str, str]:
    """Return (generated commit message, resolved message file path or "").

    The message comes from --message-file or a positional argument; the last
    message source wins, as in the bash twin.
    """
    generated = ""
    message_path = ""
    i = 0
    while i < len(args):
        if args[i] == "--message-file":
            generated, message_path = _read_message_file(
                args[i + 1] if i + 1 < len(args) else "", allowed_roots
            )
            i += 2
        else:
            generated = args[i]
            message_path = ""
            i += 1
    return generated, message_path


def _strip_quotes(value: str) -> str:
    """Strip surrounding whitespace, then one leading quote and all trailing quotes.

    Trimming first matters when the YAML value has trailing whitespace after a
    closing quote (``message: "Done"  ``): stripping quotes anchored to the end
    of string would leave the closing quote dangling (``Done"  ``) because the
    quote is no longer at the end. The PowerShell twin ``.Trim()``s before
    stripping, so trim here too to keep all three script variants in parity.
    """
    value = value.strip()
    if value[:1] in ("'", '"'):
        value = value[1:]
    return value.rstrip(_QUOTES)


def _value_after_colon(line: str) -> str:
    return re.sub(r"^[^:]*:\s*", "", line)


def _lower_scalar(line: str) -> str:
    return re.sub(r"\s", "", _value_after_colon(line)).lower()


def _config_records(content: str) -> list[str]:
    """Return config lines without newlines.

    A final record without a trailing newline is dropped, matching the bash
    ``while read`` loop this port mirrors.
    """
    lines: list[str] = []
    for record in content.splitlines(keepends=True):
        if not record.endswith("\n"):
            break
        lines.append(record[:-1])
    return lines


def _read_commit_style(lines: list[str]) -> str:
    """Read the top-level commit_style key (fixed | conventional); default fixed."""
    for line in lines:
        if not line.startswith("commit_style:"):
            continue
        value = _strip_quotes(re.sub(r"\s#.*$", "", _value_after_colon(line))).lower()
        if not value:
            return "fixed"
        if value in _COMMIT_STYLES:
            return value
        print(
            f"[specify] Warning: unknown commit_style '{value}' in git-config.yml "
            "(expected 'fixed' or 'conventional'); defaulting to 'fixed'",
            file=sys.stderr,
        )
        return "fixed"
    return "fixed"


def _auto_commit_block(lines: list[str]) -> list[str]:
    """Return the lines of the auto_commit: section, up to the next top-level key."""
    block: list[str] = []
    in_section = False
    for line in lines:
        if line.startswith("auto_commit:"):
            in_section = True
            continue
        if in_section and re.match(r"^[a-z]", line):
            break
        if in_section:
            block.append(line)
    return block


def _is_default_true(line: str) -> bool:
    return bool(re.match(r"^\s+default:\s", line)) and _lower_scalar(line) == "true"


def _is_sibling_key(line: str) -> bool:
    """A key at two-space indent (not four or more) ends the current event."""
    return bool(re.match(r"^\s{2}[a-z]", line)) and not re.match(r"^\s{4}", line)


def _event_lines(block: list[str], event_name: str) -> list[str]:
    """Return the lines nested under ``event_name:`` in the auto_commit block."""
    event_header = re.compile(rf"^\s+{re.escape(event_name)}:")
    lines: list[str] = []
    in_event = False
    for line in block:
        if event_header.match(line):
            in_event = True
        elif in_event and _is_sibling_key(line):
            in_event = False
        elif in_event:
            lines.append(line)
    return lines


def _parse_event_settings(block: list[str], event_name: str) -> tuple[bool, str, bool]:
    """Return (enabled, message, default_enabled) for one event in the auto_commit block."""
    default_enabled = any(_is_default_true(line) for line in block)
    enabled = False
    commit_msg = ""
    for line in _event_lines(block, event_name):
        if re.search(r"\senabled:", line):
            value = _lower_scalar(line)
            if value in ("true", "false"):
                enabled = value == "true"
        if re.search(r"\smessage:", line):
            commit_msg = _strip_quotes(_value_after_colon(line))
    return enabled, commit_msg, default_enabled


def _parse_auto_commit_config(content: str, event_name: str) -> tuple[bool, str]:
    """Return (enabled, commit_msg) for event_name, mirroring the bash line parser.

    Falls back to auto_commit.default only when the event has no key at all,
    so an explicit false always wins.
    """
    lines = _config_records(content)
    enabled, commit_msg, default_enabled = _parse_event_settings(
        _auto_commit_block(lines), event_name
    )
    if (
        not enabled
        and default_enabled
        and not re.search(rf"^\s*{re.escape(event_name)}:", content, re.MULTILINE)
    ):
        enabled = True
    return enabled, commit_msg


def _git(repo_root: Path, *args: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["git", *args], cwd=repo_root, capture_output=True, text=True
    )


def _exclude_pathspec(repo_root: Path, message_path: str) -> list[str]:
    """Pathspec keeping a caller's message file inside the project out of the commit."""
    if not message_path:
        return []
    root = os.path.realpath(repo_root)
    try:
        inside = os.path.commonpath([root, message_path]) == root
    except ValueError:
        inside = False
    if not inside:
        return []
    relative = Path(os.path.relpath(message_path, root)).as_posix()
    return [f":(exclude,literal){relative}"]


def _has_pending_changes(repo_root: Path, exclude: list[str]) -> bool:
    untracked = _git(
        repo_root, "ls-files", "--others", "--exclude-standard", "--", ".", *exclude
    ).stdout.strip()
    clean = (
        _git(repo_root, "diff", "--quiet", "HEAD", "--", ".", *exclude).returncode == 0
        and _git(repo_root, "diff", "--cached", "--quiet", "--", ".", *exclude).returncode
        == 0
    )
    return not clean or bool(untracked)


def _run_commit(repo_root: Path, commit_msg: str, exclude: list[str]) -> int:
    steps = [
        (["add", "--", ".", *exclude], "git add"),
        (["commit", "-q", "-m", commit_msg], "git commit"),
    ]
    for args, label in steps:
        result = _git(repo_root, *args)
        if result.returncode != 0:
            output = (result.stdout + result.stderr).strip()
            print(f"[specify] Error: {label} failed: {output}", file=sys.stderr)
            return 1
    return 0


def main(argv: list[str]) -> int:
    event_name = argv[0] if argv else ""
    if not event_name:
        print(f"Usage: {Path(sys.argv[0]).name} <event_name>", file=sys.stderr)
        return 1

    try:
        repo_root = _resolve_repo_root(Path(__file__).resolve().parent)
        allowed_roots = [
            Path(os.path.realpath(repo_root)),
            Path(os.path.realpath(tempfile.gettempdir())),
        ]
        generated_message, message_path = _parse_message_args(argv[1:], allowed_roots)
    except _ScriptError as exc:
        print(exc, file=sys.stderr)
        return 1

    if shutil.which("git") is None:
        print("[specify] Warning: Git not found; skipped auto-commit", file=sys.stderr)
        return 0

    if _git(repo_root, "rev-parse", "--is-inside-work-tree").returncode != 0:
        print(
            "[specify] Warning: Not a Git repository; skipped auto-commit",
            file=sys.stderr,
        )
        return 0

    config_file = repo_root / _SPECIFY_DIR / "extensions" / "git" / "git-config.yml"
    if not config_file.is_file():
        # No config file — auto-commit disabled by default
        return 0
    try:
        content = config_file.read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError):
        # Unreadable or non-UTF-8 config is treated like a missing one.
        return 0

    commit_style = _read_commit_style(_config_records(content))
    enabled, commit_msg = _parse_auto_commit_config(content, event_name)
    if not enabled:
        return 0

    # A message file inside the project is the caller's temporary file: keep it
    # out of the change check and out of staging.
    exclude = _exclude_pathspec(repo_root, message_path)
    if not _has_pending_changes(repo_root, exclude):
        print(f"[specify] No changes to commit after {event_name}", file=sys.stderr)
        return 0

    # In conventional mode the message must come from the caller; never fall
    # back to the configured fixed message.
    if commit_style == "conventional":
        if not generated_message:
            print(
                "[specify] Error: commit_style is 'conventional' but no generated commit "
                "message was supplied; aborting auto-commit (pass --message-file <path>, "
                "or a raw message as arg 2, or set commit_style: fixed)",
                file=sys.stderr,
            )
            return 1
        commit_msg = generated_message

    # Derive a human-readable command name from the event
    # e.g., after_specify -> specify, before_plan -> plan
    command_name = re.sub(r"^(after_|before_)", "", event_name)
    phase = "before" if event_name.startswith("before_") else "after"

    if not commit_msg:
        commit_msg = f"[Spec Kit] Auto-commit {phase} {command_name}"

    if _run_commit(repo_root, commit_msg, exclude) != 0:
        return 1

    print(f"[OK] Changes committed {phase} {command_name}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
