#!/usr/bin/env bash
#
# V2 sidecar — install pre-commit hook into the local repo's
# `.git/hooks/`. The hook content lives at
# `sidecar/projection/scripts/git-hooks/pre-commit` (version-
# controlled); this installer creates a symlink (or copy if the
# filesystem doesn't support symlinks) into the per-clone
# `.git/hooks/` directory.
#
# Per the FP strict-mode discipline (`DECISIONS 2026-05-09`), the
# pre-commit hook is the local guardrail that runs the
# `lint-discipline.sh` script before every commit. CI runs the
# same script in `.github/workflows/lint-projection.yml` for
# defense-in-depth; the hook catches violations earlier (no
# round-trip to GitHub).
#
# Usage:
#
#   sidecar/projection/scripts/install-hooks.sh
#
# Idempotent: safe to re-run.

set -euo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel)"
GIT_HOOKS="$REPO_ROOT/.git/hooks"
SOURCE_HOOK="$REPO_ROOT/sidecar/projection/scripts/git-hooks/pre-commit"
TARGET_HOOK="$GIT_HOOKS/pre-commit"

if [[ ! -d "$GIT_HOOKS" ]]; then
    echo "install-hooks: $GIT_HOOKS does not exist (not a git checkout?)" >&2
    exit 1
fi

if [[ ! -f "$SOURCE_HOOK" ]]; then
    echo "install-hooks: source hook missing at $SOURCE_HOOK" >&2
    exit 1
fi

# Symlink if possible; copy otherwise.
if ln -sf "$SOURCE_HOOK" "$TARGET_HOOK" 2>/dev/null; then
    chmod +x "$SOURCE_HOOK"
    echo "install-hooks: symlinked $TARGET_HOOK -> $SOURCE_HOOK"
else
    cp "$SOURCE_HOOK" "$TARGET_HOOK"
    chmod +x "$TARGET_HOOK"
    echo "install-hooks: copied $SOURCE_HOOK -> $TARGET_HOOK"
fi

echo ""
echo "Pre-commit hook installed. The hook runs"
echo "  $SOURCE_HOOK"
echo "before each commit and blocks on lint-discipline violations."
echo ""
echo "To bypass for a single commit (explicit deviance):"
echo "  git commit --no-verify"
