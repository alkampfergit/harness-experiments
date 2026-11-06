#!/usr/bin/env python3
"""Git extension: create_new_feature_branch.py

Creates a git feature branch only. The feature directory and spec file are
created by the core create-new-feature script. Python port of
``create-new-feature-branch.sh`` / ``create-new-feature-branch.ps1``.

Loads the core Python helpers from the project's installed scripts when
available, falling back to the minimal git helpers next to this script.
"""

from __future__ import annotations

import importlib.util
import json
import os
import re
import shutil
import subprocess
import sys
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
MAX_BRANCH_LENGTH = 244  # GitHub enforces a 244-byte limit on branch names
NUMBER_TOKEN = "{number}"

# re.ASCII keeps \d to 0-9 so these match the bash/PowerShell twins exactly.
_DIGITS_RE = re.compile(r"\d+", re.ASCII)
_LEADING_DIGITS_RE = re.compile(r"^\d+", re.ASCII)
_LEADING_NUMBER_DASH_RE = re.compile(r"^\d+-", re.ASCII)
_SPEC_DIR_RE = re.compile(r"^\d{3,}-", re.ASCII)
_SEQUENTIAL_RE = re.compile(r"^(\d{3,})-", re.ASCII)
_TIMESTAMP_RE = re.compile(r"^\d{8}-\d{6}-", re.ASCII)
_MALFORMED_TIMESTAMP_PREFIX_RE = re.compile(r"^\d{7}-\d{6}-", re.ASCII)
_MALFORMED_TIMESTAMP_RE = re.compile(r"\d{7,8}-\d{6}", re.ASCII)

USAGE = (
    "Usage: create_new_feature_branch.py [--json] [--dry-run] "
    "[--allow-existing-branch] [--short-name <name>] [--number N] "
    "[--timestamp] <feature_description>"
)

HELP_TEXT = f"""{USAGE}

Options:
  --json              Output in JSON format
  --dry-run           Compute branch name without creating the branch
  --allow-existing-branch  Switch to branch if it already exists instead of failing
  --short-name <name> Provide a custom short name (2-4 words) for the branch
  --number N          Specify branch number manually (overrides auto-detection)
  --timestamp         Use timestamp prefix (YYYYMMDD-HHMMSS) instead of sequential numbering
  --help, -h          Show this help message

Environment variables:
  GIT_BRANCH_NAME     Use this exact branch name, bypassing all prefix/suffix generation

Configuration:
  branch_template     Optional git-config.yml template with {{author}}, {{app}}, {{number}}, {{slug}}
  branch_prefix       Optional shorthand namespace expanded before {{number}}-{{slug}}
  fetch_remotes       Set to true to run `git fetch --all --prune` before numbering (default false)

Examples:
  create_new_feature_branch.py 'Add user authentication system' --short-name 'user-auth'
  create_new_feature_branch.py 'Implement OAuth2 integration for API' --number 5
  create_new_feature_branch.py --timestamp --short-name 'user-auth' 'Add user authentication'
  GIT_BRANCH_NAME=my-branch create_new_feature_branch.py 'feature description'
"""

STOP_WORDS = frozenset(
    "i a an the to for of in on at by with from is are was were be been being "
    "have has had do does did will would should could can may might must shall "
    "this that these those my your our their want need add get set".split()
)

_BOOL_FLAGS = {
    "--json": "json_mode",
    "--dry-run": "dry_run",
    "--allow-existing-branch": "allow_existing",
    "--timestamp": "use_timestamp",
}


def _err(message: str) -> None:
    print(message, file=sys.stderr)


def _run_git(repo_root: Path, *args: str, env_extra: dict | None = None):
    env = {**os.environ, **(env_extra or {})}
    return subprocess.run(
        ["git", *args], cwd=repo_root, capture_output=True, text=True, env=env
    )


def _persist_hint(var_name: str, value: str) -> str:
    """Shell-appropriate guidance for persisting an env var in the caller's shell."""
    if os.name == "nt":
        escaped_value = value.replace("'", "''")
        return f"$env:{var_name} = '{escaped_value}'"
    escaped_value = re.sub(r"([^\w@%+=:,./-])", r"\\\1", value)
    return f"export {var_name}={escaped_value}"


@dataclass
class Args:
    json_mode: bool = False
    dry_run: bool = False
    allow_existing: bool = False
    short_name: str = ""
    branch_number: str = ""
    use_timestamp: bool = False
    description_parts: list[str] = field(default_factory=list)


def _option_value(argv: list[str], index: int, option: str) -> str:
    if index + 1 >= len(argv) or argv[index + 1].startswith("--"):
        _err(f"Error: {option} requires a value")
        raise SystemExit(1)
    return argv[index + 1]


def parse_args(argv: list[str]) -> Args:
    args = Args()
    i = 0
    while i < len(argv):
        arg = argv[i]
        if arg in _BOOL_FLAGS:
            setattr(args, _BOOL_FLAGS[arg], True)
        elif arg == "--short-name":
            args.short_name = _option_value(argv, i, arg)
            i += 1
        elif arg == "--number":
            args.branch_number = _option_value(argv, i, arg)
            i += 1
            if not _DIGITS_RE.fullmatch(args.branch_number):
                _err("Error: --number must be a non-negative integer")
                raise SystemExit(1)
        elif arg in ("--help", "-h"):
            print(HELP_TEXT)
            raise SystemExit(0)
        else:
            args.description_parts.append(arg)
        i += 1
    return args


# ── Core helpers loading ─────────────────────────────────────────────────────


def _find_project_root(start: Path) -> Path | None:
    current = start
    while True:
        if (current / ".specify").is_dir() or (current / ".git").exists():
            return current
        if current.parent == current:
            return None
        current = current.parent


def _load_core_common(project_root: Path | None):
    """Load the core common.py from the project's installed scripts.

    Search locations in priority order, mirroring the bash script:
     1. .specify/scripts/python/common.py (installed project)
     2. scripts/python/common.py (source checkout fallback)
    Returns the loaded module or None.
    """
    if project_root is None:
        return None
    for relative in (".specify/scripts/python/common.py", "scripts/python/common.py"):
        candidate = project_root / relative
        if candidate.is_file():
            spec = importlib.util.spec_from_file_location("speckit_core_common", candidate)
            if spec is None or spec.loader is None:
                continue
            module = importlib.util.module_from_spec(spec)
            sys.modules[spec.name] = module
            spec.loader.exec_module(module)
            return module
    return None


def _resolve_repo_root(core, project_root: Path | None) -> Path | None:
    """Pick the repository root: core helper, then the discovered project, then Git's top level."""
    if core is not None and hasattr(core, "get_repo_root"):
        # Pass script path so cwd-outside-repo callers land on the same
        # fallback the bash twin does. Older cores don't accept the kwarg —
        # fall back to the no-arg call for compatibility.
        try:
            return Path(core.get_repo_root(script_file=Path(__file__)))
        except TypeError:
            return Path(core.get_repo_root())
    # The discovered Spec Kit project wins over Git's top level: in a monorepo
    # (apps/web/.specify inside a larger repo) the project's own config and specs
    # must be used, not the parent repository's.
    if project_root is not None:
        return project_root
    toplevel = _git_lines(Path.cwd(), "rev-parse", "--show-toplevel")
    if toplevel:
        return Path(toplevel[0])
    return None


def _local_has_git(repo_root: Path) -> bool:
    """True when Git reports repo_root inside a work tree.

    The Git query is authoritative: a project in a subdirectory of a worktree
    has no local .git marker, so a marker check would wrongly skip branching.
    """
    if shutil.which("git") is None:
        return False
    return _run_git(repo_root, "rev-parse", "--is-inside-work-tree").returncode == 0


# ── Numbering ────────────────────────────────────────────────────────────────


def get_highest_from_specs(specs_dir: Path) -> int:
    highest = 0
    if specs_dir.is_dir():
        for entry in specs_dir.iterdir():
            if not entry.is_dir():
                continue
            name = entry.name
            # Match sequential prefixes (>=3 digits), but skip timestamp dirs.
            if _SPEC_DIR_RE.match(name) and not _TIMESTAMP_RE.match(name):
                highest = max(highest, int(_LEADING_DIGITS_RE.match(name).group(0)))
    return highest


def _is_sequential_branch_segment(name: str) -> bool:
    """Sequential (###-*) and not a timestamp or a malformed timestamp."""
    return (
        bool(_SEQUENTIAL_RE.match(name))
        and not _TIMESTAMP_RE.match(name)
        and not _MALFORMED_TIMESTAMP_PREFIX_RE.match(name)
        and not _MALFORMED_TIMESTAMP_RE.fullmatch(name)
    )


def _extract_highest_number(names: list[str], scope_prefix: str) -> int:
    """Extract the highest sequential feature number from a list of ref names."""
    highest = 0
    for name in names:
        if not name:
            continue
        if scope_prefix:
            if not name.startswith(scope_prefix):
                continue
            name = name[len(scope_prefix) :]
        name = name.rsplit("/", 1)[-1]
        if _is_sequential_branch_segment(name):
            highest = max(highest, int(_SEQUENTIAL_RE.match(name).group(1)))
    return highest


def _git_lines(repo_root: Path, *args: str, env_extra: dict | None = None) -> list[str]:
    if shutil.which("git") is None:
        return []
    result = _run_git(repo_root, *args, env_extra=env_extra)
    if result.returncode != 0:
        return []
    return result.stdout.splitlines()


def get_highest_from_branches(repo_root: Path, scope_prefix: str) -> int:
    names = []
    for line in _git_lines(repo_root, "branch", "-a"):
        line = re.sub(r"^[+*]\s+", "", line)
        line = line.lstrip()
        line = re.sub(r"^remotes/[^/]*/", "", line)
        names.append(line)
    return _extract_highest_number(names, scope_prefix)


def get_highest_from_remote_refs(repo_root: Path, scope_prefix: str) -> int:
    """Highest number from remote branches without fetching (side-effect-free)."""
    highest = 0
    for remote in _git_lines(repo_root, "remote"):
        refs = _git_lines(
            repo_root,
            "ls-remote",
            "--heads",
            remote,
            env_extra={"GIT_TERMINAL_PROMPT": "0"},
        )
        names = [ref.rsplit("refs/heads/", 1)[-1] for ref in refs]
        highest = max(highest, _extract_highest_number(names, scope_prefix))
    return highest


def check_existing_branches(
    repo_root: Path,
    specs_dir: Path,
    *,
    fetch_remotes: bool,
    include_remote_refs: bool,
    scope_prefix: str,
) -> int:
    """Return the next available feature number.

    fetch_remotes runs `git fetch --all --prune` first (opt-in via git-config.yml).
    include_remote_refs also reads remote heads with `git ls-remote` (dry-run only).
    """
    if fetch_remotes:
        _run_git(repo_root, "fetch", "--all", "--prune")
    highest_branch = get_highest_from_branches(repo_root, scope_prefix)
    if include_remote_refs:
        highest_branch = max(
            highest_branch, get_highest_from_remote_refs(repo_root, scope_prefix)
        )
    return max(highest_branch, get_highest_from_specs(specs_dir)) + 1


# ── Branch naming ────────────────────────────────────────────────────────────


def clean_branch_name(name: str) -> str:
    name = re.sub(r"[^a-z0-9]", "-", name.lower())
    name = re.sub(r"-+", "-", name)
    return name.strip("-")


def _appears_as_acronym(word: str, description: str) -> bool:
    """True when a short word appears uppercased in the description (e.g. "API", "DB").

    The boundaries are spelled out as ASCII rather than using \\b: \\b is
    Unicode-aware on str, so "éDBé cache" would hide the acronym behind
    a non-ASCII word character, while the bash twin's `grep -qw` runs under
    LC_ALL=C and sees a boundary there.
    """
    return (
        re.search(
            rf"(?<![0-9A-Za-z_]){re.escape(word.upper())}(?![0-9A-Za-z_])",
            description,
        )
        is not None
    )


def generate_branch_name(description: str) -> str:
    """Generate a branch suffix from the description with stop word filtering."""
    clean_name = re.sub(r"[^a-z0-9]", " ", description.lower())

    meaningful_words = []
    for word in clean_name.split():
        if word in STOP_WORDS:
            continue
        # Keep short words only when they appear uppercased in the original
        # description (acronyms like "API" or "DB").
        if len(word) >= 3 or _appears_as_acronym(word, description):
            meaningful_words.append(word)

    if meaningful_words:
        max_words = 4 if len(meaningful_words) == 4 else 3
        return "-".join(meaningful_words[:max_words])

    cleaned = clean_branch_name(description)
    return "-".join([part for part in cleaned.split("-") if part][:3])


def branch_token(value: str, fallback: str) -> str:
    cleaned = clean_branch_name(value)
    return cleaned if cleaned else fallback


def get_author_token(repo_root: Path) -> str:
    author = ""
    if shutil.which("git") is not None:
        lines = _git_lines(repo_root, "config", "user.name")
        author = lines[0] if lines else ""
        if not author:
            lines = _git_lines(repo_root, "config", "user.email")
            email = lines[0] if lines else ""
            author = email.split("@")[0]
    if not author:
        author = os.environ.get("USER") or os.environ.get("USERNAME") or "unknown"
    return branch_token(author, "unknown")


def get_app_token(repo_root: Path) -> str:
    return branch_token(repo_root.name, "app")


def _strip_outer_quotes(value: str) -> str:
    """Remove a leading and/or trailing double quote, then the same for single quotes."""
    value = value.removeprefix('"').removesuffix('"')
    return value.removeprefix("'").removesuffix("'")


def read_git_config_value(config_file: Path, key: str) -> str:
    if not config_file.is_file():
        return ""
    try:
        lines = config_file.read_text(encoding="utf-8").splitlines()
    except (OSError, UnicodeDecodeError):
        return ""
    for line in lines:
        if re.match(rf"^\s*{re.escape(key)}:", line):
            value = re.sub(rf"^\s*{re.escape(key)}:\s*", "", line)
            value = re.sub(r"\s#.*$", "", value)
            return _strip_outer_quotes(value.strip())
    return ""


def resolve_branch_template(config_file: Path) -> str:
    template = read_git_config_value(config_file, "branch_template")
    if template:
        return template

    prefix = read_git_config_value(config_file, "branch_prefix")
    if not prefix:
        return ""
    if prefix.endswith("/"):
        return f"{prefix}{{number}}-{{slug}}"
    return f"{prefix}/{{number}}-{{slug}}"


def validate_branch_template(template: str) -> None:
    if not template:
        return
    if NUMBER_TOKEN not in template:
        _err(
            "Error: branch_template must include the {number} token so generated "
            "branches remain valid feature branches."
        )
        raise SystemExit(1)
    slug_index = template.find("{slug}")
    if slug_index != -1 and NUMBER_TOKEN in template[slug_index:]:
        _err(
            "Error: branch_template must not place {slug} before {number}; "
            "use {slug} only in the final feature segment."
        )
        raise SystemExit(1)
    feature_segment = template.rsplit("/", 1)[-1]
    if not feature_segment.startswith(f"{NUMBER_TOKEN}-"):
        _err(
            "Error: branch_template must put {number}- at the start of the final "
            "path segment so generated branches remain valid feature branches."
        )
        raise SystemExit(1)


def render_branch_template(
    template: str, feature_num: str, branch_suffix: str, author_token: str, app_token: str
) -> str:
    rendered = template
    rendered = rendered.replace("{author}", author_token)
    rendered = rendered.replace("{app}", app_token)
    rendered = rendered.replace(NUMBER_TOKEN, feature_num)
    rendered = rendered.replace("{slug}", branch_suffix)
    return rendered


def extract_feature_num_from_branch(branch_name: str) -> str:
    feature_segment = branch_name.rsplit("/", 1)[-1]
    match = _TIMESTAMP_RE.match(feature_segment)
    if match:
        return match.group(0).rstrip("-")
    match = _LEADING_NUMBER_DASH_RE.match(feature_segment)
    if match:
        return match.group(0).rstrip("-")
    return branch_name


def _byte_length(value: str) -> int:
    return len(value.encode("utf-8"))


@dataclass
class _Naming:
    """Branch-name template plus the author/app tokens it may reference."""

    branch_template: str
    author_token: str
    app_token: str

    def build(self, feature_num: str, branch_suffix: str) -> str:
        if self.branch_template:
            return render_branch_template(
                self.branch_template, feature_num, branch_suffix, self.author_token, self.app_token
            )
        return f"{feature_num}-{branch_suffix}"

    def scope_prefix(self, branch_suffix: str) -> str:
        if not self.branch_template:
            return ""
        prefix_template = self.branch_template.split(NUMBER_TOKEN)[0]
        return render_branch_template(
            prefix_template, "", branch_suffix, self.author_token, self.app_token
        )


# ── Branch name computation ──────────────────────────────────────────────────


def _next_branch_number(
    args: Args,
    has_git_repo: bool,
    repo_root: Path,
    specs_dir: Path,
    scope_prefix: str,
    fetch_remotes: bool,
) -> int:
    if not has_git_repo:
        return get_highest_from_specs(specs_dir) + 1
    if args.dry_run:
        # Dry-run never fetches. Remote heads are read (read-only) only when the
        # opt-in fetch_remotes setting is true, matching the bash script.
        return check_existing_branches(
            repo_root,
            specs_dir,
            fetch_remotes=False,
            include_remote_refs=fetch_remotes,
            scope_prefix=scope_prefix,
        )
    return check_existing_branches(
        repo_root,
        specs_dir,
        fetch_remotes=fetch_remotes,
        include_remote_refs=False,
        scope_prefix=scope_prefix,
    )


def _compute_branch_name(
    args: Args,
    description: str,
    naming: _Naming,
    repo_root: Path,
    specs_dir: Path,
    has_git_repo: bool,
    fetch_remotes: bool,
) -> tuple[str, str]:
    """Return (branch_name, feature_num)."""
    env_branch_name = os.environ.get("GIT_BRANCH_NAME", "")
    if env_branch_name:
        if _byte_length(env_branch_name) > MAX_BRANCH_LENGTH:
            _err(
                "Error: GIT_BRANCH_NAME must be 244 bytes or fewer in UTF-8. "
                f"Provided value is {_byte_length(env_branch_name)} bytes."
            )
            raise SystemExit(1)
        return env_branch_name, extract_feature_num_from_branch(env_branch_name)

    branch_suffix = (
        clean_branch_name(args.short_name)
        if args.short_name
        else generate_branch_name(description)
    )

    branch_number = args.branch_number
    if args.use_timestamp and branch_number:
        _err("[specify] Warning: --number is ignored when --timestamp is used")
        branch_number = ""

    if args.use_timestamp:
        feature_num = datetime.now().strftime("%Y%m%d-%H%M%S")
    else:
        if not branch_number:
            branch_number = str(
                _next_branch_number(
                    args,
                    has_git_repo,
                    repo_root,
                    specs_dir,
                    naming.scope_prefix(branch_suffix),
                    fetch_remotes,
                )
            )
        feature_num = f"{int(branch_number):03d}"

    branch_name = naming.build(feature_num, branch_suffix)
    if _byte_length(branch_name) > MAX_BRANCH_LENGTH:
        branch_name = _fit_to_byte_limit(naming, feature_num, branch_suffix, branch_name)
    return branch_name, feature_num


def _fit_to_byte_limit(
    naming: _Naming, feature_num: str, branch_suffix: str, branch_name: str
) -> str:
    """Shorten the slug until the branch fits GitHub's 244-byte limit."""
    original_branch_name = branch_name
    truncated_suffix = branch_suffix
    while _byte_length(branch_name) > MAX_BRANCH_LENGTH and truncated_suffix:
        truncated_suffix = truncated_suffix[:-1].rstrip("-")
        branch_name = naming.build(feature_num, truncated_suffix)
    if _byte_length(branch_name) > MAX_BRANCH_LENGTH:
        _err("Error: Branch template prefix exceeds GitHub's 244-byte branch name limit.")
        raise SystemExit(1)

    _err("[specify] Warning: Branch name exceeded GitHub's 244-byte limit")
    _err(
        f"[specify] Original: {original_branch_name} "
        f"({_byte_length(original_branch_name)} bytes)"
    )
    _err(f"[specify] Truncated to: {branch_name} ({_byte_length(branch_name)} bytes)")
    return branch_name


# ── Branch creation ──────────────────────────────────────────────────────────


def _switch_to_existing_branch(repo_root: Path, branch_name: str) -> None:
    switch = _run_git(repo_root, "checkout", "-q", branch_name)
    if switch.returncode != 0:
        _err(
            f"Error: Failed to switch to existing branch '{branch_name}'. "
            "Please resolve any local changes or conflicts and try again."
        )
        if switch.stderr.strip():
            _err(switch.stderr.strip())
        raise SystemExit(1)


def _create_git_branch(repo_root: Path, branch_name: str, args: Args) -> None:
    """Create and check out branch_name; exits 1 on failure."""
    create = _run_git(repo_root, "checkout", "-q", "-b", branch_name)
    if create.returncode == 0:
        return

    current_branch_lines = _git_lines(repo_root, "rev-parse", "--abbrev-ref", "HEAD")
    current_branch = current_branch_lines[0] if current_branch_lines else ""
    branch_exists = bool(_git_lines(repo_root, "branch", "--list", branch_name))
    if not branch_exists:
        _err(f"Error: Failed to create git branch '{branch_name}'.")
        _err(create.stderr.strip() or "Please check your git configuration and try again.")
        raise SystemExit(1)

    if args.allow_existing:
        if current_branch != branch_name:
            _switch_to_existing_branch(repo_root, branch_name)
        return
    if args.use_timestamp:
        _err(
            f"Error: Branch '{branch_name}' already exists. Rerun to get "
            "a new timestamp or use a different --short-name."
        )
    else:
        _err(
            f"Error: Branch '{branch_name}' already exists. Please use a "
            "different feature name or specify a different number with --number."
        )
    raise SystemExit(1)


def _emit_result(args: Args, branch_name: str, feature_num: str) -> None:
    if args.json_mode:
        payload: dict[str, object] = {
            "BRANCH_NAME": branch_name,
            "FEATURE_NUM": feature_num,
        }
        if args.dry_run:
            payload["DRY_RUN"] = True
        print(json.dumps(payload, ensure_ascii=False, separators=(",", ":")))
        return
    print(f"BRANCH_NAME: {branch_name}")
    print(f"FEATURE_NUM: {feature_num}")
    if not args.dry_run:
        print(
            "# To persist in your shell: "
            f"{_persist_hint('SPECIFY_FEATURE', branch_name)}"
        )


# ── Main ─────────────────────────────────────────────────────────────────────


def main(argv: list[str]) -> int:
    args = parse_args(argv)

    feature_description = " ".join(args.description_parts)
    if not feature_description:
        _err(USAGE)
        return 1
    feature_description = feature_description.strip()
    if not feature_description:
        _err("Error: Feature description cannot be empty or contain only whitespace")
        return 1

    project_root = _find_project_root(SCRIPT_DIR)
    core = _load_core_common(project_root)

    # SPECIFY_INIT_DIR is resolved (and validated) by the core resolver. If the
    # core helpers were not found, refuse rather than silently falling back to
    # the wrong root.
    if os.environ.get("SPECIFY_INIT_DIR") and (
        core is None or not hasattr(core, "resolve_specify_init_dir")
    ):
        _err(
            "Error: SPECIFY_INIT_DIR requires updated Spec Kit core scripts "
            "(common.py with resolve_specify_init_dir), which were not found."
        )
        return 1

    repo_root = _resolve_repo_root(core, project_root)
    if repo_root is None:
        _err("Error: Could not determine repository root.")
        return 1

    has_git_repo = _local_has_git(repo_root)
    specs_dir = repo_root / "specs"
    config_file = repo_root / ".specify" / "extensions" / "git" / "git-config.yml"

    naming = _Naming(
        branch_template=resolve_branch_template(config_file),
        author_token=get_author_token(repo_root),
        app_token=get_app_token(repo_root),
    )
    validate_branch_template(naming.branch_template)
    # Remote fetching is opt-in; by default numbering uses local refs only.
    fetch_remotes = read_git_config_value(config_file, "fetch_remotes").lower() == "true"

    branch_name, feature_num = _compute_branch_name(
        args,
        feature_description,
        naming,
        repo_root,
        specs_dir,
        has_git_repo,
        fetch_remotes,
    )

    if not args.dry_run:
        if has_git_repo:
            _create_git_branch(repo_root, branch_name, args)
        else:
            _err(
                "[specify] Warning: Git repository not detected; skipped branch "
                f"creation for {branch_name}"
            )
        _err(f"# To persist: {_persist_hint('SPECIFY_FEATURE', branch_name)}")

    _emit_result(args, branch_name, feature_num)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
