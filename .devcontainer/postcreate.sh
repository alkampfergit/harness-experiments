#!/bin/bash
# Post-creation setup script for the harness-experiments devcontainer.
# Installs agent CLIs and helper tools on top of the .NET 10 base image.
# Tool installs are best-effort: a failing tool is reported but does not
# abort container creation.

set -euo pipefail

# Every download below is an install script or a release tarball, so pin the
# transport: a redirect must not be able to downgrade the fetch to plaintext.
CURL_TLS_OPTS=(--proto '=https' --tlsv1.2)

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

echo "=========================================="
echo "Starting devcontainer post-creation setup"
echo "=========================================="

# Fix apt sources issue with yarn
echo "Cleaning up apt sources..."
sudo rm -f /etc/apt/sources.list.d/yarn.list

echo "Installing system dependencies..."
sudo apt-get update
sudo apt-get install -y git-flow sqlite3 jq

echo "Configuring git aliases..."
bash "$REPO_DIR/.devcontainer/setup-git-aliases.sh"

# Claude Code CLI via the official native installer (auto-updates)
echo "Installing Claude Code CLI..."
curl "${CURL_TLS_OPTS[@]}" -fsSL https://claude.ai/install.sh | bash || true
export PATH="$HOME/.local/bin:$PATH"

# CLI tools distributed via npm
if command -v npm >/dev/null 2>&1; then
    echo "Installing OpenAI Codex..."
    npm install -g --ignore-scripts @openai/codex || true
    echo "Installing automata-cli..."
    npm install -g --ignore-scripts automata-cli || true
else
    echo "npm not available, skipping npm-based CLI installs."
fi

# uv (also used for the Python embedding sidecar in tools/) and GitHub spec-kit
if ! command -v uv >/dev/null 2>&1; then
    echo "Installing uv..."
    curl "${CURL_TLS_OPTS[@]}" -LsSf https://astral.sh/uv/install.sh | sh || true
    export PATH="$HOME/.local/bin:$PATH"
else
    echo "uv already installed, skipping."
fi

# spec-kit is published only as a git source, so uv has to build it.
if command -v uv >/dev/null 2>&1; then
    echo "Installing github spec-kit via uv..."
    uv tool install specify-cli --from git+https://github.com/github/spec-kit.git || true # NOSONAR
else
    echo "uv not available, cannot install spec-kit."
fi

# tokensave: code graph used both by agents and as the parser for the
# tokensave-bridge experiment.
echo "Installing tokensave..."
TOKENSAVE_TAG=$(curl "${CURL_TLS_OPTS[@]}" -sI https://github.com/aovestdipaperino/tokensave/releases/latest | grep -i '^location:' | sed 's|.*/tag/||;s/\r//' || true)
if [[ -n "$TOKENSAVE_TAG" ]]; then
    ARCH=$(uname -m)
    if [[ "$ARCH" = "aarch64" || "$ARCH" = "arm64" ]]; then
        TOKENSAVE_ARCH="aarch64-linux"
    else
        TOKENSAVE_ARCH="x86_64-linux"
    fi
    TOKENSAVE_URL="https://github.com/aovestdipaperino/tokensave/releases/download/${TOKENSAVE_TAG}/tokensave-${TOKENSAVE_TAG}-${TOKENSAVE_ARCH}.tar.gz"
    echo "  Downloading tokensave ${TOKENSAVE_TAG#v} (${TOKENSAVE_ARCH})..."
    TMP_DIR=$(mktemp -d)
    if curl "${CURL_TLS_OPTS[@]}" -fsSL "$TOKENSAVE_URL" -o "$TMP_DIR/tokensave.tar.gz" \
        && tar xzf "$TMP_DIR/tokensave.tar.gz" -C "$TMP_DIR" \
        && [[ -f "$TMP_DIR/tokensave" ]]; then
        sudo mv "$TMP_DIR/tokensave" /usr/local/bin/tokensave
    fi
    rm -rf "$TMP_DIR"
fi

if command -v tokensave >/dev/null 2>&1; then
    echo "  tokensave $(tokensave --version) installed."
    # `tokensave install` prompts for the global git hooks; `--git-hook yes`
    # answers up front so the non-interactive postCreateCommand cannot hang.
    # The hooks are what keep the index fresh after commits and checkouts.
    echo "  Configuring tokensave for Claude Code..."
    tokensave install --agent claude --git-hook yes </dev/null || true
    echo "  Configuring tokensave for Codex CLI..."
    tokensave install --agent codex --git-hook yes </dev/null || true
    # `init` is the one-time per-project opt-in; `sync` alone is a no-op on a
    # fresh clone. samples/ is git-ignored, so it is not part of this index.
    echo "  Indexing repository..."
    tokensave init "$REPO_DIR" || true
    tokensave status "$REPO_DIR" || true
else
    echo "tokensave install failed or is unavailable, skipping configuration."
fi

# Homebrew and rtk
append_if_missing() {
    local line="$1"
    local file="$2"

    touch "$file"
    if ! grep -Fqx "$line" "$file"; then
        echo "$line" >>"$file"
    fi
}

if ! command -v brew >/dev/null 2>&1; then
    echo "Installing Homebrew..."
    NONINTERACTIVE=1 bash -c "$(curl "${CURL_TLS_OPTS[@]}" -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)" || true
else
    echo "Homebrew already installed, skipping."
fi

if [[ -x /home/linuxbrew/.linuxbrew/bin/brew ]]; then
    BREW_BIN="/home/linuxbrew/.linuxbrew/bin/brew"
else
    BREW_BIN=""
fi

if [[ -n "$BREW_BIN" ]]; then
    BREW_SHELLENV_LINE="eval \"\$($BREW_BIN shellenv)\""
    append_if_missing "$BREW_SHELLENV_LINE" "$HOME/.zprofile"
    append_if_missing "$BREW_SHELLENV_LINE" "$HOME/.zshrc"
    append_if_missing "$BREW_SHELLENV_LINE" "$HOME/.bashrc"
    eval "$("$BREW_BIN" shellenv)"

    if brew list --formula rtk >/dev/null 2>&1; then
        echo "rtk already installed, skipping."
    else
        echo "Installing rtk via Homebrew..."
        brew install rtk || true
    fi

    if command -v rtk >/dev/null 2>&1; then
        echo "Configuring rtk for Claude Code..."
        rtk init --global --auto-patch || true
        echo "Configuring rtk for Codex CLI..."
        rtk init --global --codex --auto-patch || true
    else
        echo "rtk install failed or is unavailable, skipping configuration."
    fi
else
    echo "Homebrew install did not expose brew on a known path, skipping rtk."
fi

# Shared agent plugin marketplace for Claude Code and Codex (user scope)
AGENT_PLUGINS_MARKETPLACE="https://github.com/alkampfergit/agent-plugins-base.git"

if command -v claude >/dev/null 2>&1; then
    echo "Registering agent-plugins-base marketplace for Claude Code..."
    claude plugin marketplace add "$AGENT_PLUGINS_MARKETPLACE" </dev/null || true
    claude plugin install github-alk@agent-plugins-base </dev/null || true
else
    echo "claude CLI not available, skipping Claude Code marketplace setup."
fi

if command -v codex >/dev/null 2>&1; then
    echo "Registering agent-plugins-base marketplace for Codex..."
    codex plugin marketplace add "$AGENT_PLUGINS_MARKETPLACE" </dev/null || true
    codex plugin add github-alk@agent-plugins-base </dev/null || true
else
    echo "codex CLI not available, skipping Codex marketplace setup."
fi

echo "=========================================="
echo "Devcontainer post-creation setup complete"
echo "=========================================="
