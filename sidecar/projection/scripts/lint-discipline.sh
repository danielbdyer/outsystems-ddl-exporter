#!/usr/bin/env bash
#
# Sidecar V2 — lint-discipline guardrail.
#
# Greps the V2 production source tree for patterns the codebase has
# committed to avoid:
#
#   * `System.Text.RegularExpressions` — banned namespace
#     (per `DECISIONS 2026-05-09 — No-string-concatenation /
#     no-regex discipline`).
#   * `sprintf` / `printfn` / `printf` in `Projection.Core/` —
#     Core's purity / structured-emission discipline.
#   * String-`+` heuristic — `"x" + y` and `x + "y"` style patterns
#     in production code.
#   * `String.Format(` — banned alternative path.
#   * `Guid.NewGuid()` outside the reified `DatabaseNameGenerator`
#     seam in `Deploy.fs`.
#   * `DateTime.Now` / `DateTime.UtcNow` outside `Bench.fs` (Core's
#     no-time discipline).
#   * `Random.` outside test fixtures.
#
# Per-line allowlist via comment marker  // LINT-ALLOW: <reason>
# on the offending line. Any new violation requires either the
# marker (with rationale) or a paired DECISIONS amendment.
#
# Exit code 0 on clean; 1 on at least one violation.
#
# Usage:
#   sidecar/projection/scripts/lint-discipline.sh [--ci]
#
# `--ci` forces colorless output suitable for CI logs.

set -euo pipefail

# ---------------------------------------------------------------------------
# Locate the V2 sidecar tree relative to this script.
# ---------------------------------------------------------------------------

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SRC="$ROOT/src"
CORE="$SRC/Projection.Core"
PIPELINE="$SRC/Projection.Pipeline"

if [[ ! -d "$SRC" ]]; then
    echo "lint-discipline: cannot find source tree at $SRC" >&2
    exit 2
fi

# ---------------------------------------------------------------------------
# Output formatting.
# ---------------------------------------------------------------------------

USE_COLOR=1
for arg in "$@"; do
    case "$arg" in
        --ci|--no-color) USE_COLOR=0 ;;
    esac
done

if [[ "$USE_COLOR" -eq 1 ]] && [[ -t 1 ]]; then
    RED=$'\033[0;31m'
    YELLOW=$'\033[0;33m'
    GREEN=$'\033[0;32m'
    RESET=$'\033[0m'
else
    RED=""; YELLOW=""; GREEN=""; RESET=""
fi

violations=0

report_violation() {
    local rule="$1"
    local file="$2"
    local line="$3"
    local content="$4"
    echo "${RED}[$rule]${RESET} $file:$line  $content"
    violations=$((violations + 1))
}

# ---------------------------------------------------------------------------
# Helper: grep over a path glob, exclude lint-allow lines, then filter.
# Args: rule path pattern
# ---------------------------------------------------------------------------

is_comment_line() {
    # F# comment-line heuristic: trimmed content starts with `//` or
    # `///`. (Block comments `(* ... *)` are not handled; F# code
    # rarely uses them and they don't appear in the V2 codebase.)
    # Also skips lines whose only F# code is preceded by a comment;
    # we trade a small false-negative rate for robustness against
    # comment hits dominating the report.
    local content="$1"
    local trimmed
    trimmed="$(printf '%s' "$content" | sed -E 's/^[[:space:]]+//')"
    [[ "$trimmed" == //* ]]
}

# Returns 0 (true) if the file carries a top-of-file
# `// LINT-ALLOW-FILE: <rationale>` marker. Used for files whose
# entire purpose is operator-facing rendering (Bench output,
# PhysicalSchema diff prose, etc.) where format strings are the
# discipline's allowed exception.
file_has_allowlist_marker() {
    local file="$1"
    head -n 30 "$file" 2>/dev/null | grep -q 'LINT-ALLOW-FILE'
}

scan() {
    local rule="$1"
    local path="$2"
    local pattern="$3"
    if [[ ! -e "$path" ]]; then
        return 0
    fi
    # Use grep -rEn with --include='*.fs' so we only catch F# source
    # (skip generated `.fsproj`, build outputs, etc.).
    while IFS= read -r hit; do
        # `hit` shape: <file>:<line>:<content>
        local file_part line_part rest
        file_part="${hit%%:*}"
        rest="${hit#*:}"
        line_part="${rest%%:*}"
        content="${rest#*:}"
        # File-level allowlist via `// LINT-ALLOW-FILE` marker (top
        # of file). Operator-facing rendering files exempt en masse.
        if file_has_allowlist_marker "$file_part"; then
            continue
        fi
        # Per-line allowlist via `// LINT-ALLOW` marker.
        if printf '%s' "$content" | grep -q 'LINT-ALLOW'; then
            continue
        fi
        # Skip F# comment lines (false positives where the discipline
        # is being discussed in prose).
        if is_comment_line "$content"; then
            continue
        fi
        report_violation "$rule" "$file_part" "$line_part" "$content"
    done < <(grep -rEn --include='*.fs' "$pattern" "$path" 2>/dev/null || true)
}

# ---------------------------------------------------------------------------
# Rule 1 — banned namespace: System.Text.RegularExpressions
# ---------------------------------------------------------------------------

scan "regex-banned" "$SRC" 'System\.Text\.RegularExpressions'

# ---------------------------------------------------------------------------
# Rule 2 — sprintf / printfn / printf in Projection.Core (purity).
# Adapters at the boundary may use these for diagnostic strings.
# ---------------------------------------------------------------------------

scan "core-sprintf" "$CORE" '\b(sprintf|printfn|printf)\b'

# ---------------------------------------------------------------------------
# Rule 3 — string-+ heuristic. Catches `"x" + y` and `x + "y"` shapes.
# Adapters have legacy patterns; scope to Core for tightness.
# ---------------------------------------------------------------------------

scan "string-plus" "$CORE" '"\s*\+|\+\s*"'

# ---------------------------------------------------------------------------
# Rule 4 — banned alternative: String.Format(
# ---------------------------------------------------------------------------

scan "string-format" "$SRC" 'String\.Format\('

# ---------------------------------------------------------------------------
# Rule 5 — Guid.NewGuid() outside the reified DatabaseNameGenerator
# seam (Deploy.fs is the only allowed site; the generator marks the
# non-determinism boundary).
# ---------------------------------------------------------------------------

# Inline allowlist convention: only the `DatabaseNameGenerator.guidBased`
# binding may invoke `Guid.NewGuid`. All other production-code occurrences
# fail the rule.
while IFS= read -r hit; do
    file_part="${hit%%:*}"
    rest="${hit#*:}"
    line_part="${rest%%:*}"
    content="${rest#*:}"
    if printf '%s' "$content" | grep -q 'LINT-ALLOW'; then
        continue
    fi
    if is_comment_line "$content"; then
        continue
    fi
    # The reified `DatabaseNameGenerator.guidBased` binding in
    # `Deploy.fs` is the sanctioned non-determinism boundary; the
    # binding's declaration site is allowlisted by structural
    # context. Match the surrounding `let guidBased` binding.
    report_violation "guid-newguid" "$file_part" "$line_part" "$content"
done < <(grep -rEn --include='*.fs' --before-context=2 'Guid\.NewGuid\s*\(\)' "$SRC" 2>/dev/null \
    | grep -v -- '--' \
    | awk -F: 'BEGIN { skip=0 } /guidBased/ { skip=1; next } /Guid\.NewGuid/ { if (skip) { skip=0; next } print } { skip=0 }' || true)

# ---------------------------------------------------------------------------
# Rule 6 — DateTime.Now / DateTime.UtcNow outside Bench.fs.
# Core's no-time discipline — clock values enter at the boundary.
# ---------------------------------------------------------------------------

while IFS= read -r hit; do
    file_part="${hit%%:*}"
    rest="${hit#*:}"
    line_part="${rest%%:*}"
    content="${rest#*:}"
    # Bench is the sanctioned timing surface.
    if [[ "$file_part" == *"/Projection.Core/Bench.fs" ]]; then
        continue
    fi
    if printf '%s' "$content" | grep -q 'LINT-ALLOW'; then
        continue
    fi
    report_violation "datetime-now" "$file_part" "$line_part" "$content"
done < <(grep -rEn --include='*.fs' 'DateTime\.(Now|UtcNow)' "$SRC" 2>/dev/null || true)

# ---------------------------------------------------------------------------
# Rule 7 — Random. outside test fixtures (production code is deterministic).
# ---------------------------------------------------------------------------

scan "random-banned" "$SRC" '\bSystem\.Random\b|\bnew Random\(\)|\bRandom\(\)'

# ---------------------------------------------------------------------------
# Rule 8 — `let mutable` outside files marked `LINT-ALLOW-FILE-MUTATION`.
# F#'s default is immutable; mutation requires explicit annotation. The
# rule enforces that mutation is reified at the file level (via the
# top-of-file marker) so mutation-justified files are explicit, not
# accidental. Per `DECISIONS 2026-05-09 — FP strict mode discipline`.
# ---------------------------------------------------------------------------

scan_mutation() {
    local rule="$1"
    local pattern="$2"
    while IFS= read -r hit; do
        local file_part line_part rest
        file_part="${hit%%:*}"
        rest="${hit#*:}"
        line_part="${rest%%:*}"
        content="${rest#*:}"
        # File-level mutation allowlist: `LINT-ALLOW-FILE-MUTATION`.
        # Files whose top-of-file declares this marker are exempt
        # (audit-justified mutation sites).
        if head -n 30 "$file_part" 2>/dev/null \
                | grep -q 'LINT-ALLOW-FILE-MUTATION\|LINT-ALLOW-FILE'; then
            continue
        fi
        if printf '%s' "$content" | grep -q 'LINT-ALLOW'; then
            continue
        fi
        if is_comment_line "$content"; then
            continue
        fi
        report_violation "$rule" "$file_part" "$line_part" "$content"
    done < <(grep -rEn --include='*.fs' "$pattern" "$SRC" 2>/dev/null || true)
}

scan_mutation "let-mutable" '\blet mutable\b'

# ---------------------------------------------------------------------------
# Rule 9 — Mutable BCL collections (`ResizeArray<`, `Dictionary<`,
# `HashSet<`, `Stack<`, `Queue<`, `ConcurrentDictionary<`, etc.) outside
# `LINT-ALLOW-FILE-MUTATION` files. These are legitimate inside tight,
# function-local algorithm bodies (Tarjan SCC, Kahn's, hot bench
# accumulators); the file-level marker reifies that allowance.
# ---------------------------------------------------------------------------

scan_mutation "mutable-collection" '\b(ResizeArray|Dictionary|HashSet|Stack|Queue|ConcurrentDictionary|ConcurrentQueue|ConcurrentBag)\s*<'

# ---------------------------------------------------------------------------
# Rule 10 — `<-` assignment outside `LINT-ALLOW-FILE-MUTATION` files.
# Mutating an existing binding requires explicit allowance. `<-` is the
# `member val ... with get, set` setter and the `let mutable` reassignment
# — both are mutation operations that should be visible at the file
# level.
# ---------------------------------------------------------------------------

scan_mutation "set-assign" '<-'

# ---------------------------------------------------------------------------
# Reporting.
# ---------------------------------------------------------------------------

if [[ "$violations" -eq 0 ]]; then
    echo "${GREEN}lint-discipline: clean ($SRC).${RESET}"
    exit 0
else
    echo ""
    echo "${YELLOW}lint-discipline: $violations violation(s).${RESET}"
    echo ""
    echo "Fix options per violation:"
    echo "  1. Refactor to BCL typed builder (XmlWriter / Utf8JsonWriter /"
    echo "     String.Concat / String.concat / typed AST)."
    echo "  2. Add per-line marker  // LINT-ALLOW: <rationale>"
    echo "     to the offending line if the discipline genuinely doesn't apply."
    echo "  3. Open a paired DECISIONS amendment naming why the discipline is"
    echo "     being relaxed at this site."
    echo ""
    echo "Reference: DECISIONS 2026-05-09 — No-string-concatenation /"
    echo "no-regex discipline; Built-in obligation; Lint guardrail."
    exit 1
fi
