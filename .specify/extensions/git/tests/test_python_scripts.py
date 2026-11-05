"""Focused tests for the Python git-extension scripts.

Every repository here is a throwaway copy under pytest's tmp_path. Auto-commit
runs are pointed at that copy with SPECIFY_INIT_DIR, so the real repository is
never touched. No network access is used.
"""

from __future__ import annotations

import importlib.util
import os
import subprocess
import sys
from pathlib import Path
from types import ModuleType

import pytest

SCRIPTS = Path(__file__).resolve().parents[1] / "scripts" / "python"
AUTO_COMMIT = SCRIPTS / "auto_commit.py"

_GIT_ENV = {
    **os.environ,
    "GIT_AUTHOR_NAME": "Test",
    "GIT_AUTHOR_EMAIL": "test@example.invalid",
    "GIT_COMMITTER_NAME": "Test",
    "GIT_COMMITTER_EMAIL": "test@example.invalid",
    "GIT_CONFIG_GLOBAL": os.devnull,
    "GIT_CONFIG_NOSYSTEM": "1",
    "GIT_TERMINAL_PROMPT": "0",
}


def _load(module_name: str) -> ModuleType:
    spec = importlib.util.spec_from_file_location(module_name, SCRIPTS / f"{module_name}.py")
    assert spec is not None
    assert spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    # Registered before execution: dataclasses look the module up by name.
    sys.modules[module_name] = module
    spec.loader.exec_module(module)
    return module


def _git(repo: Path, *args: str) -> str:
    result = subprocess.run(
        ["git", *args], cwd=repo, capture_output=True, text=True, env=_GIT_ENV, check=True
    )
    return result.stdout.strip()


def _make_project(tmp_path: Path, config_text: str) -> Path:
    """A throwaway Spec Kit project: one committed file plus one untracked change."""
    project = tmp_path / "project"
    config_dir = project / ".specify" / "extensions" / "git"
    config_dir.mkdir(parents=True)
    (config_dir / "git-config.yml").write_text(config_text, encoding="utf-8")
    _git(project, "init", "-q")
    _git(project, "add", ".")
    _git(project, "commit", "-q", "-m", "init")
    (project / "feature.txt").write_text("change\n", encoding="utf-8")
    return project


def _run_auto_commit(init_dir: Path, cwd: Path, *args: str) -> subprocess.CompletedProcess[str]:
    env = {**_GIT_ENV, "SPECIFY_INIT_DIR": str(init_dir)}
    return subprocess.run(
        [sys.executable, str(AUTO_COMMIT), "after_specify", *args],
        cwd=cwd,
        capture_output=True,
        text=True,
        env=env,
    )


CONVENTIONAL_CONFIG = (
    "commit_style: conventional\n"
    "auto_commit:\n"
    "  default: false\n"
    "  after_specify:\n"
    "    enabled: true\n"
    '    message: "[Spec Kit] Fixed message"\n'
)

FIXED_CONFIG = CONVENTIONAL_CONFIG.replace("commit_style: conventional", "commit_style: fixed")


def test_conventional_mode_without_message_fails_before_commit(tmp_path: Path) -> None:
    project = _make_project(tmp_path, CONVENTIONAL_CONFIG)

    result = _run_auto_commit(project, tmp_path)

    assert result.returncode == 1
    assert "no generated commit message" in result.stderr
    assert _git(project, "rev-list", "--count", "HEAD") == "1"
    assert _git(project, "status", "--porcelain") == "?? feature.txt"


def test_conventional_mode_uses_message_file_and_keeps_it(tmp_path: Path) -> None:
    project = _make_project(tmp_path, CONVENTIONAL_CONFIG)
    message_file = tmp_path / "commit-msg.txt"
    message_file.write_text("feat: add thing\n", encoding="utf-8")

    result = _run_auto_commit(project, tmp_path, "--message-file", str(message_file))

    assert result.returncode == 0, result.stderr
    assert _git(project, "log", "-1", "--format=%s") == "feat: add thing"
    assert message_file.exists(), "the caller-owned message file must not be deleted"


def test_conventional_mode_accepts_positional_message(tmp_path: Path) -> None:
    project = _make_project(tmp_path, CONVENTIONAL_CONFIG)

    result = _run_auto_commit(project, tmp_path, "docs: explain thing")

    assert result.returncode == 0, result.stderr
    assert _git(project, "log", "-1", "--format=%s") == "docs: explain thing"


def test_fixed_mode_uses_configured_message(tmp_path: Path) -> None:
    project = _make_project(tmp_path, FIXED_CONFIG)

    result = _run_auto_commit(project, tmp_path)

    assert result.returncode == 0, result.stderr
    assert _git(project, "log", "-1", "--format=%s") == "[Spec Kit] Fixed message"


def test_specify_init_dir_selects_the_target_project(tmp_path: Path) -> None:
    project = _make_project(tmp_path, FIXED_CONFIG)
    unrelated = tmp_path / "unrelated"
    unrelated.mkdir()

    result = _run_auto_commit(project, unrelated)

    assert result.returncode == 0, result.stderr
    assert _git(project, "rev-list", "--count", "HEAD") == "2"


def test_specify_init_dir_must_be_a_spec_kit_project(tmp_path: Path) -> None:
    plain = tmp_path / "plain"
    plain.mkdir()

    result = _run_auto_commit(plain, tmp_path)

    assert result.returncode == 1
    assert "no .specify/ directory" in result.stderr


def test_has_git_accepts_project_inside_worktree_subdirectory(tmp_path: Path) -> None:
    repo = tmp_path / "repo"
    repo.mkdir()
    _git(repo, "init", "-q")
    project = repo / "apps" / "web"
    (project / ".specify").mkdir(parents=True)
    assert not (project / ".git").exists()

    create_branch = _load("create_new_feature_branch")
    git_common = _load("git_common")

    assert create_branch._local_has_git(project) is True
    assert git_common.has_git(project) is True


def test_project_root_is_preferred_over_git_toplevel(tmp_path: Path) -> None:
    repo = tmp_path / "repo"
    repo.mkdir()
    _git(repo, "init", "-q")
    project = repo / "apps" / "web"
    (project / ".specify").mkdir(parents=True)

    create_branch = _load("create_new_feature_branch")

    assert create_branch._resolve_repo_root(None, project) == project


def test_fetch_runs_only_when_opted_in(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> None:
    create_branch = _load("create_new_feature_branch")
    calls: list[tuple[str, ...]] = []

    def record(repo_root: Path, *args: str, env_extra: dict | None = None):
        calls.append(args)
        return subprocess.CompletedProcess(args, 0, "", "")

    monkeypatch.setattr(create_branch, "_run_git", record)
    specs = tmp_path / "specs"

    create_branch.check_existing_branches(
        tmp_path, specs, fetch_remotes=False, include_remote_refs=False, scope_prefix=""
    )
    assert not any(args[:1] == ("fetch",) for args in calls)

    calls.clear()
    create_branch.check_existing_branches(
        tmp_path, specs, fetch_remotes=True, include_remote_refs=False, scope_prefix=""
    )
    assert ("fetch", "--all", "--prune") in calls


def test_fetch_remotes_config_key_is_read(tmp_path: Path) -> None:
    create_branch = _load("create_new_feature_branch")
    config = tmp_path / "git-config.yml"

    config.write_text("fetch_remotes: true  # opt in\n", encoding="utf-8")
    assert create_branch.read_git_config_value(config, "fetch_remotes").lower() == "true"

    config.write_text("branch_numbering: sequential\n", encoding="utf-8")
    assert create_branch.read_git_config_value(config, "fetch_remotes") == ""


def test_message_file_outside_allowed_roots_is_rejected(tmp_path: Path) -> None:
    auto_commit = _load("auto_commit")
    project = tmp_path / "project"
    project.mkdir()
    outside = tmp_path / "outside.txt"
    outside.write_text("feat: nope\n", encoding="utf-8")

    with pytest.raises(auto_commit._ScriptError, match="must be inside the project"):
        auto_commit._read_message_file(str(outside), [project])


def test_message_file_inside_allowed_root_is_read(tmp_path: Path) -> None:
    auto_commit = _load("auto_commit")
    message = tmp_path / "msg.txt"
    message.write_text("feat: yes\n", encoding="utf-8")

    assert auto_commit._read_message_file(str(message), [tmp_path]) == "feat: yes"
