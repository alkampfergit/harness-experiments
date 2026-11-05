#!/usr/bin/env python3
"""Git-specific common helpers for the git extension.

Python port of ``git-common.sh`` / ``git-common.ps1`` — contains only
git-specific branch validation and detection logic.
"""

from __future__ import annotations

import re
import shutil
import subprocess
import sys
from pathlib import Path


def has_git(repo_root: Path | None = None) -> bool:
    """Check whether Git reports ``repo_root`` as inside a work tree.

    The Git query is authoritative: a Spec Kit project may sit in a
    subdirectory of a worktree whose ``.git`` lives in an ancestor, so a
    local ``.git`` marker is not required.
    """
    root = Path(repo_root) if repo_root is not None else Path.cwd()
    if shutil.which("git") is None:
        return False
    result = subprocess.run(
        ["git", "-C", str(root), "rev-parse", "--is-inside-work-tree"],
        capture_output=True,
        text=True,
    )
    return result.returncode == 0


def effective_branch_name(raw: str) -> str:
    """Strip a single optional path segment (e.g. gitflow "feat/004-name" -> "004-name").

    Only when the full name is exactly two slash-free segments; otherwise
    returns the raw name.
    """
    match = re.fullmatch(r"([^/]+)/([^/]+)", raw)
    if match:
        return match.group(2)
    return raw


def check_feature_branch(raw: str, has_git_repo: bool) -> bool:
    """Validate that a branch name matches the expected feature branch pattern.

    Accepts sequential (###-* with >=3 digits) or timestamp (YYYYMMDD-HHMMSS-*)
    formats, either at the start of the branch or after path-style namespace
    prefixes. Logic aligned with the bash/PowerShell twins.
    """
    if not has_git_repo:
        print(
            "[specify] Warning: Git repository not detected; skipped branch validation",
            file=sys.stderr,
        )
        return True

    branch = effective_branch_name(raw)
    feature_segment = branch.rsplit("/", 1)[-1]

    # Accept sequential prefix (3+ digits) but exclude malformed timestamps:
    # 7-or-8 digit date + 6-digit time with no trailing slug.
    # re.ASCII keeps \d limited to 0-9, matching the bash/PowerShell twins.
    is_sequential = bool(
        re.match(r"^\d{3,}-", feature_segment, re.ASCII)
        and not re.match(r"^\d{7}-\d{6}-", feature_segment, re.ASCII)
        and not re.fullmatch(r"\d{7,8}-\d{6}", feature_segment, re.ASCII)
    )
    is_timestamp = bool(re.match(r"^\d{8}-\d{6}-", feature_segment, re.ASCII))

    if not is_sequential and not is_timestamp:
        print(f"ERROR: Not on a feature branch. Current branch: {raw}", file=sys.stderr)
        print(
            "Feature branches should be named like: 001-feature-name, "
            "1234-feature-name, 20260319-143022-feature-name, or "
            "<prefix>/001-feature-name",
            file=sys.stderr,
        )
        return False

    return True
