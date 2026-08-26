#!/usr/bin/env bash
# THE VENDOR CHANNEL — publish the curated tree into an estate-repo clone.
#
#   publish-to-estate.sh <estate-clone-path>
#
# Stages the vendor drop (ssdt-agent-package.mjs vendor-apply — refused
# unless the drop is citation-closed), then syncs it into the clone's
# ssdt-agent/ directory as an idempotent mirror: an unchanged re-publish is
# a no-op, a changed one replaces exactly what changed and removes what the
# set no longer carries. The drop's VENDOR.json names the source monorepo
# commit, so "which version is vendored" is one file read. The script never
# commits or pushes — it prints the diff summary; the owner reviews the
# clone and raises the pull request in Azure DevOps.
#
# The Copilot editor bundle is vendored INSIDE the tree at
# ssdt-agent/copilot-package/; merging its .github/ into the estate
# repository root is the one manual step (ADOPTION.md), because .github/
# may carry estate-owned files this channel must not overwrite.
set -euo pipefail

cd "$(dirname "$0")/.."

CLONE="${1:?usage: publish-to-estate.sh <estate-clone-path>}"
[ -d "$CLONE/.git" ] || { echo "[vendor] $CLONE is not a git clone" >&2; exit 1; }

STAGE="ssdt-agent/vendor-drop"
node scripts/ssdt-agent-package.mjs vendor-apply "ssdt-agent/vendor-drop"

# A portable mirror (no rsync on Git Bash): replace the clone's ssdt-agent/
# wholesale with the staged drop. Deletions propagate because the copy IS
# the set; git shows the true per-file diff for the owner's review.
rm -rf "$CLONE/ssdt-agent"
cp -R "$STAGE" "$CLONE/ssdt-agent"

echo "[vendor] synced $(find "$STAGE" -type f | wc -l | tr -d ' ') files into $CLONE/ssdt-agent/"
echo "[vendor] source commit: $(node -e 'process.stdout.write(JSON.parse(require("fs").readFileSync(process.argv[1], "utf8")).commit)' "$STAGE/VENDOR.json")"
echo "[vendor] clone diff summary:"
git -C "$CLONE" add -A ssdt-agent >/dev/null 2>&1 || true
git -C "$CLONE" status --short -- ssdt-agent | head -40
CHANGED="$(git -C "$CLONE" status --short -- ssdt-agent | wc -l | tr -d ' ')"
if [ "$CHANGED" = "0" ]; then
    echo "[vendor] the clone already carries this drop — nothing to publish"
else
    echo "[vendor] $CHANGED path(s) changed. Review the clone, then commit and raise the pull request in Azure DevOps."
    echo "[vendor] remember the one manual step: merge ssdt-agent/copilot-package/.github/ into the repository's .github/ (see ssdt-agent/copilot-package/ADOPTION.md)."
fi
