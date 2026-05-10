#!/usr/bin/env bash
#
# Sidecar V2 — lint-discipline guardrail.
#
# Greps the V2 production source tree for patterns the codebase has
# committed to avoid. Per the FP strict-mode + hexagonal-architecture
# disciplines (`DECISIONS 2026-05-09`), the default is **explicit
# acknowledgement of deviance** — every legitimate exception carries
# either a per-line `LINT-ALLOW: <rationale>` marker or a top-of-file
# `LINT-ALLOW-FILE: <rationale>` / `LINT-ALLOW-FILE-MUTATION:
# <rationale>` marker.
#
# Build outputs (`obj/`, `bin/`) are excluded from every scan — the
# generated `*.AssemblyInfo.fs` files would otherwise dominate.
#
# Per-line allowlist via comment marker  // LINT-ALLOW: <reason>
# on the offending line. File-level via top-of-file
# `// LINT-ALLOW-FILE: <reason>` (covers all rules) or
# `// LINT-ALLOW-FILE-MUTATION: <reason>` (mutation rules only).
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

if [[ ! -d "$SRC" ]]; then
    echo "lint-discipline: cannot find source tree at $SRC" >&2
    exit 2
fi

# `grep` flags reused on every scan: F# source only; skip generated
# build outputs (`obj/`, `bin/`) and `.git`.
GREP_FLAGS=(
    -rEn
    --include='*.fs'
    --exclude-dir='obj'
    --exclude-dir='bin'
    --exclude-dir='.git'
)

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
# Helper functions.
# ---------------------------------------------------------------------------

is_comment_line() {
    # F# comment-line heuristic: trimmed content starts with `//` or
    # `///`. (Block comments `(* ... *)` are not handled; F# code
    # rarely uses them and they don't appear in the V2 codebase.)
    local content="$1"
    local trimmed
    trimmed="$(printf '%s' "$content" | sed -E 's/^[[:space:]]+//')"
    [[ "$trimmed" == //* ]]
}

# Returns 0 (true) if the file carries any top-of-file `LINT-ALLOW-FILE`
# or `LINT-ALLOW-FILE-MUTATION` marker. The full-allowlist marker
# (`LINT-ALLOW-FILE`) covers all rules; the mutation-only marker
# (`LINT-ALLOW-FILE-MUTATION`) covers only the mutation-class rules.
file_has_allowlist_marker() {
    local file="$1"
    head -n 30 "$file" 2>/dev/null | grep -q 'LINT-ALLOW-FILE'
}

# Returns 0 (true) if the file carries any top-of-file mutation-allow
# marker (either `LINT-ALLOW-FILE-MUTATION` or `LINT-ALLOW-FILE`).
file_has_mutation_marker() {
    local file="$1"
    head -n 30 "$file" 2>/dev/null \
        | grep -q 'LINT-ALLOW-FILE-MUTATION\|LINT-ALLOW-FILE'
}

scan() {
    # Scope-restricted scan with file-level + per-line allowlist.
    # Args: rule-name path pattern
    local rule="$1"
    local path="$2"
    local pattern="$3"
    if [[ ! -e "$path" ]]; then
        return 0
    fi
    while IFS= read -r hit; do
        local file_part line_part rest content
        file_part="${hit%%:*}"
        rest="${hit#*:}"
        line_part="${rest%%:*}"
        content="${rest#*:}"
        if file_has_allowlist_marker "$file_part"; then continue; fi
        if printf '%s' "$content" | grep -q 'LINT-ALLOW'; then continue; fi
        if is_comment_line "$content"; then continue; fi
        report_violation "$rule" "$file_part" "$line_part" "$content"
    done < <(grep "${GREP_FLAGS[@]}" "$pattern" "$path" 2>/dev/null || true)
}

scan_mutation() {
    # Mutation-class scan: same as `scan` but also accepts the
    # mutation-only file marker.
    local rule="$1"
    local pattern="$2"
    while IFS= read -r hit; do
        local file_part line_part rest content
        file_part="${hit%%:*}"
        rest="${hit#*:}"
        line_part="${rest%%:*}"
        content="${rest#*:}"
        if file_has_mutation_marker "$file_part"; then continue; fi
        if printf '%s' "$content" | grep -q 'LINT-ALLOW'; then continue; fi
        if is_comment_line "$content"; then continue; fi
        report_violation "$rule" "$file_part" "$line_part" "$content"
    done < <(grep "${GREP_FLAGS[@]}" "$pattern" "$SRC" 2>/dev/null || true)
}

# ---------------------------------------------------------------------------
# Rule 1 — banned namespace: System.Text.RegularExpressions
# ---------------------------------------------------------------------------

scan "regex-banned" "$SRC" 'System\.Text\.RegularExpressions'

# ---------------------------------------------------------------------------
# Rule 2 — sprintf / printfn / printf in Projection.Core (purity).
# Adapters at the boundary may use these for diagnostic strings; if a
# specific adapter file emits SQL or human-readable diagnostic prose
# via sprintf, opt in to a `LINT-ALLOW-FILE` marker.
# ---------------------------------------------------------------------------

scan "core-sprintf" "$CORE" '\b(sprintf|printfn|printf)\b'

# ---------------------------------------------------------------------------
# Rule 3 — string-+ heuristic. Catches `"x" + y` and `x + "y"` shapes
# anywhere in production. Per chapter 3.5 deep audit (2026-05-09 hard
# line) — broadened from Core-only to all `src/`. The `+` operator on
# strings is the most-brittle concatenation form.
# ---------------------------------------------------------------------------

scan "string-plus" "$SRC" '"\s*\+|\+\s*"'

# ---------------------------------------------------------------------------
# Rule 4 — banned alternative: String.Format(. Whole src/.
# ---------------------------------------------------------------------------

scan "string-format" "$SRC" 'String\.Format\('

# ---------------------------------------------------------------------------
# Rule 5 — Guid.NewGuid() outside the reified DatabaseNameGenerator
# seam (Deploy.fs is the only allowed site; the generator marks the
# non-determinism boundary).
# ---------------------------------------------------------------------------

while IFS= read -r hit; do
    file_part="${hit%%:*}"
    rest="${hit#*:}"
    line_part="${rest%%:*}"
    content="${rest#*:}"
    if printf '%s' "$content" | grep -q 'LINT-ALLOW'; then continue; fi
    if is_comment_line "$content"; then continue; fi
    report_violation "guid-newguid" "$file_part" "$line_part" "$content"
done < <(grep "${GREP_FLAGS[@]}" --before-context=2 'Guid\.NewGuid\s*\(\)' "$SRC" 2>/dev/null \
    | grep -v -- '--' \
    | awk -F: 'BEGIN { skip=0 } /guidBased/ { skip=1; next } /Guid\.NewGuid/ { if (skip) { skip=0; next } print } { skip=0 }' || true)

# ---------------------------------------------------------------------------
# Rule 6 — DateTime.Now / DateTime.UtcNow outside Bench.fs.
# ---------------------------------------------------------------------------

while IFS= read -r hit; do
    file_part="${hit%%:*}"
    rest="${hit#*:}"
    line_part="${rest%%:*}"
    content="${rest#*:}"
    if [[ "$file_part" == *"/Projection.Core/Bench.fs" ]]; then continue; fi
    if printf '%s' "$content" | grep -q 'LINT-ALLOW'; then continue; fi
    if is_comment_line "$content"; then continue; fi
    report_violation "datetime-now" "$file_part" "$line_part" "$content"
done < <(grep "${GREP_FLAGS[@]}" 'DateTime\.(Now|UtcNow)' "$SRC" 2>/dev/null || true)

# ---------------------------------------------------------------------------
# Rule 7 — Random. outside test fixtures (production code is deterministic).
# ---------------------------------------------------------------------------

scan "random-banned" "$SRC" '\bSystem\.Random\b|\bnew Random\(\)|\bRandom\(\)'

# ---------------------------------------------------------------------------
# Rules 8 / 9 / 10 — mutation rules.
# ---------------------------------------------------------------------------

scan_mutation "let-mutable" '\blet mutable\b'
scan_mutation "mutable-collection" '\b(ResizeArray|Dictionary|HashSet|Stack|Queue|ConcurrentDictionary|ConcurrentQueue|ConcurrentBag)\s*<'
scan_mutation "set-assign" '<-'

# ---------------------------------------------------------------------------
# Rule 11 — failwith / failwithf in Core (typed-error discipline).
# ---------------------------------------------------------------------------

scan "core-failwith" "$CORE" '\bfailwith[f]?\b'

# ---------------------------------------------------------------------------
# Rule 12 — async { / task { in Core (sync-by-design).
# ---------------------------------------------------------------------------

scan "core-async-block" "$CORE" 'async \{|task \{'

# ---------------------------------------------------------------------------
# Rule 13 — concurrency primitives in Core.
# ---------------------------------------------------------------------------

scan "core-concurrency-primitive" "$CORE" 'Task\.(Run|Delay|WaitAll|WaitAny|Wait)|Thread\.Sleep'

# ---------------------------------------------------------------------------
# Rule 14 — box / unbox anywhere in production (no type erasure).
# ---------------------------------------------------------------------------

scan "type-erasure" "$SRC" '\b(box|unbox)\b'

# ---------------------------------------------------------------------------
# Rule 15 — mutable record fields (immutability discipline).
# ---------------------------------------------------------------------------

scan "mutable-record-field" "$SRC" '\bmutable [A-Z][a-zA-Z]+\s*:'

# ---------------------------------------------------------------------------
# Rule 16 — `#nowarn` directive banned anywhere in production.
# `#nowarn` silences compiler warnings; the discipline is to fix the
# underlying issue or to elevate via DECISIONS amendment, not to
# selectively silence. Per the FP strict-mode discipline.
# ---------------------------------------------------------------------------

scan "nowarn-banned" "$SRC" '^#nowarn'

# ---------------------------------------------------------------------------
# Rule 17 — `open System.Reflection` banned in production code.
# Reflection is consciously deferred (per `CLAUDE.md` F# feature surface);
# generated `obj/*.AssemblyInfo.fs` files are excluded by GREP_FLAGS.
# ---------------------------------------------------------------------------

scan "reflection-banned" "$SRC" 'open System\.Reflection'

# ---------------------------------------------------------------------------
# Rule 18 — `obj`-typed parameters / returns banned in production.
# F# closed-DU + generic dispatch is the structural alternative. `obj`
# is type erasure dressed as a parameter.
# ---------------------------------------------------------------------------

scan "obj-typed" "$SRC" ':[[:space:]]*obj[[:space:]]*[)\->]|:[[:space:]]*obj[[:space:]]*$'

# ---------------------------------------------------------------------------
# Rule 18b — `System.String.Concat` banned anywhere in production
# without explicit `LINT-ALLOW`. Per the supreme operating discipline
# (`DECISIONS 2026-05-09 — Operating discipline supremacy`):
# `System.String.Concat` (any overload) is a string-concatenation
# primitive. Even though it's a BCL function with no format-string
# semantics, its purpose is *string concatenation* — which is
# brittle and at odds with the data-structure-oriented epistemic
# stance. New code must use a typed structured carrier (typed DU
# field, typed record, typed AST + canonical renderer) and let the
# string emerge ONLY at the absolute terminal boundary (e.g.,
# `XmlWriter` / `Utf8JsonWriter` / `Sql160ScriptGenerator` /
# `XmlWriter.WriteString` etc.). Existing `String.Concat` sites
# carry `// LINT-ALLOW: terminal text-emission boundary` markers
# documenting the boundary that justifies the use.
# ---------------------------------------------------------------------------

scan "string-concat" "$SRC" 'System\.String\.Concat\s*\(|^[[:space:]]+String\.Concat\s*\('

# ---------------------------------------------------------------------------
# Rule 18c — F#'s `String.concat` (lowercase) banned anywhere in
# production without explicit `LINT-ALLOW`. Per the chapter-3.5
# user-hard-line-refinement (2026-05-09): `String.concat` is *also*
# string-concatenation by another spelling. Even though it's the BCL
# collection joiner with explicit separator and typed segment list,
# the discipline says to prefer NON-concatenation alternatives
# whenever a use-case-specific library, BCL primitive, or
# data-structure-oriented refactor exists. New uses must justify per
# per-site analysis (alternatives considered + why concat was
# adopted).
# ---------------------------------------------------------------------------

scan "fsharp-string-concat" "$SRC" '\bString\.concat\b'

# ---------------------------------------------------------------------------
# Rule 18d — F# 9 interpolated strings (`$"..."`) banned in production.
# Per chapter 3.5 deep audit (2026-05-09 hard line) — interpolated
# strings are *also* string-concatenation by another spelling.
# Additionally they default to `CultureInfo.CurrentCulture` for typed
# value rendering, which violates T1 byte-determinism on numeric
# types. Use typed builders / BCL writers / `Inv.*` helpers instead.
# ---------------------------------------------------------------------------

scan "interpolated-string" "$SRC" '\$"'

# ---------------------------------------------------------------------------
# Rule 18e — `String.Join` (BCL collection joiner) banned anywhere in
# production without explicit `LINT-ALLOW`. Per chapter 3.5 deep audit
# (2026-05-09 hard line) — same family as `String.concat` / `String
# .Concat`; the BCL spelling does not exempt the discipline. Each use
# requires per-site analysis (alternatives considered + why join was
# adopted).
# ---------------------------------------------------------------------------

scan "string-join" "$SRC" 'System\.String\.Join\s*\(|\bString\.Join\s*\('

# ---------------------------------------------------------------------------
# Rule 19 — `xs @ [x]` Big-O anti-pattern banned in production.
# `List.append xs [x]` is O(N) per call; iterated O(N²). Idiom is
# `x :: xs` then `List.rev` at the end, OR `Result.aggregate` (V2's
# reified accumulator), OR `ResizeArray` inside a justified mutation
# scope. Per the audit's Big-O Tier-1 finding (chapter-3.1 close).
#
# Writer-monad bind operations (`Lineage.fs`, `Diagnostics.fs`) carry
# a per-line LINT-ALLOW: the writer's `tell` is an algebraic primitive
# whose append-at-end shape is the monad's law (left identity); the
# trail length is bounded per-event, not per-element-of-a-fold.
# ---------------------------------------------------------------------------

scan "big-o-list-append-singleton" "$SRC" '@ \[[a-zA-Z_]+\]'

# ---------------------------------------------------------------------------
# Rules 20 / 21 / 22 — Hexagonal-coupling rules.
#
# Per `CLAUDE.md` / `VISION.md`, V2 is hexagonal:
#   Core <- Adapters / Targets <- Pipeline <- CLI
#
# Dependency direction is one-way; each layer opens only its layer or
# below. The rules ban the violations; allowed opens (System.*,
# Projection.Core, etc.) pass through.
# ---------------------------------------------------------------------------

# Rule 20: Core files cannot `open Projection.<X>` for any X — Core has
# no V2 dependencies. (Internal `open Projection.Core.X` for nested
# modules is fine; we ban `open Projection.<other-project>`.)

while IFS= read -r hit; do
    file_part="${hit%%:*}"
    rest="${hit#*:}"
    line_part="${rest%%:*}"
    content="${rest#*:}"
    if file_has_allowlist_marker "$file_part"; then continue; fi
    if printf '%s' "$content" | grep -q 'LINT-ALLOW'; then continue; fi
    if is_comment_line "$content"; then continue; fi
    report_violation "hex-core-coupling" "$file_part" "$line_part" "$content"
done < <(grep "${GREP_FLAGS[@]}" 'open Projection\.(Adapters|Targets|Pipeline|Cli)' "$CORE" 2>/dev/null || true)

# Rule 21: Targets cannot `open Projection.Adapters.*` / `open
# Projection.Pipeline` / `open Projection.Cli`. Targets are
# horizontal siblings; cross-target coupling is also banned.

for targetDir in "$SRC"/Projection.Targets.*; do
    [[ -d "$targetDir" ]] || continue
    targetName="$(basename "$targetDir")"
    while IFS= read -r hit; do
        file_part="${hit%%:*}"
        rest="${hit#*:}"
        line_part="${rest%%:*}"
        content="${rest#*:}"
        # Allow self-referential `open Projection.Targets.<self>`
        # (rare; only happens if a sibling file in the same target
        # opens its own namespace).
        selfNs="open ${targetName//./.}"
        if printf '%s' "$content" | grep -q "$selfNs"; then continue; fi
        if file_has_allowlist_marker "$file_part"; then continue; fi
        if printf '%s' "$content" | grep -q 'LINT-ALLOW'; then continue; fi
        if is_comment_line "$content"; then continue; fi
        report_violation "hex-target-coupling" "$file_part" "$line_part" "$content"
    done < <(grep "${GREP_FLAGS[@]}" \
        'open Projection\.(Adapters|Pipeline|Cli|Targets\.)' "$targetDir" 2>/dev/null || true)
done

# Rule 22: Adapters cannot `open Projection.Targets.*` / `open
# Projection.Pipeline` / `open Projection.Cli`.

for adapterDir in "$SRC"/Projection.Adapters.*; do
    [[ -d "$adapterDir" ]] || continue
    adapterName="$(basename "$adapterDir")"
    while IFS= read -r hit; do
        file_part="${hit%%:*}"
        rest="${hit#*:}"
        line_part="${rest%%:*}"
        content="${rest#*:}"
        selfNs="open ${adapterName//./.}"
        if printf '%s' "$content" | grep -q "$selfNs"; then continue; fi
        if file_has_allowlist_marker "$file_part"; then continue; fi
        if printf '%s' "$content" | grep -q 'LINT-ALLOW'; then continue; fi
        if is_comment_line "$content"; then continue; fi
        report_violation "hex-adapter-coupling" "$file_part" "$line_part" "$content"
    done < <(grep "${GREP_FLAGS[@]}" \
        'open Projection\.(Targets|Pipeline|Cli|Adapters\.)' "$adapterDir" 2>/dev/null || true)
done

# ---------------------------------------------------------------------------
# Rule 27 — LINT-ALLOW substantive-rationale audit (chapter-3.7 codification).
#
# Per `DECISIONS 2026-05-10 — LINT-ALLOW substantive-rationale discipline`:
# every per-line `LINT-ALLOW` marker on a string-composition / built-in-
# substitute site MUST embody the four-question analysis (use-case-
# specific library / availability / cost / structural reason). The named
# failure mode is **performance-of-compliance** — a marker shaped like
# an audit trail without the substance.
#
# Heuristics can't reliably distinguish performance-of-compliance from
# real substance; the discipline document does. This rule does two
# best-effort things:
#
#   A. **Inventory.** At the end of every clean run, print every
#      per-line concat-aversion LINT-ALLOW grouped by file. Makes the
#      markers visible at the discipline-review surface (chapter close,
#      PR review, fresh-agent orientation).
#
#   B. **Soft floor.** Per-line concat-aversion markers must satisfy
#      BOTH:
#        - At least 30 chars after the colon (catches one-word
#          shortcuts like "// LINT-ALLOW: ok" or "// LINT-ALLOW: typed").
#        - Contain at least one substantive-vocabulary token from the
#          established discipline lexicon (terminal / boundary /
#          primitive / round-trip / considered / alternative /
#          gold-standard / escape / irreducible / no [BCL/vendor/
#          use-case-specific]).
#      Both conditions must fail for the marker to be flagged. The
#      27-marker baseline at chapter-3.7 codification all pass.
# ---------------------------------------------------------------------------

# Concat-aversion patterns (matches Rules 18b/c/d/e + sprintf/Format).
CONCAT_PATTERN='String\.Concat|String\.concat|String\.Format|String\.Join|sprintf|\$"'

# Substantive-vocabulary tokens (case-insensitive) — the discipline lexicon.
SUBSTANTIVE_TOKENS='terminal|boundary|primitive|round-trip|considered|alternative|gold-standard|escape|irreducible|no [Bb]CL|no vendor|no use-case-specific'

# Inventory accumulator (newline-delimited "file:line  rationale").
inventory=""
floor_violations=0

while IFS= read -r hit; do
    file_part="${hit%%:*}"
    rest="${hit#*:}"
    line_part="${rest%%:*}"
    content="${rest#*:}"

    # Skip lines that aren't actually concat-aversion sites (the broader
    # grep matched `LINT-ALLOW` anywhere, not just on concat sites).
    if ! printf '%s' "$content" | grep -qE "$CONCAT_PATTERN"; then continue; fi

    # File-level LINT-ALLOW-FILE markers carry the analysis at the
    # top-of-file boundary — per-line markers in those files don't
    # need the substantive-rationale floor (the file allow is the
    # discipline's audit surface for the whole file).
    if file_has_allowlist_marker "$file_part"; then continue; fi

    # Extract the rationale text after `LINT-ALLOW:`.
    rationale="$(printf '%s' "$content" | sed -nE 's/.*LINT-ALLOW:[[:space:]]*(.*)$/\1/p')"
    rationale_len=${#rationale}

    # Inventory entry (always — substance lives in the discipline doc;
    # the inventory is the audit-review surface).
    inventory+="${file_part}:${line_part}  ${rationale}"$'\n'

    # Soft floor: short marker AND no substantive token → flag.
    if [[ "$rationale_len" -lt 30 ]] \
        && ! printf '%s' "$rationale" | grep -qiE "$SUBSTANTIVE_TOKENS"; then
        report_violation "lint-allow-substantive-rationale" "$file_part" "$line_part" \
            "$content"
        floor_violations=$((floor_violations + 1))
    fi
done < <(grep "${GREP_FLAGS[@]}" 'LINT-ALLOW:' "$SRC" 2>/dev/null || true)

# ---------------------------------------------------------------------------
# Reporting.
# ---------------------------------------------------------------------------

print_lint_allow_inventory() {
    if [[ -z "$inventory" ]]; then return; fi
    echo ""
    echo "${YELLOW}LINT-ALLOW audit-trail inventory${RESET} (concat-aversion sites; per DECISIONS 2026-05-10):"
    printf '%s' "$inventory" | sed 's/^/  /'
    echo ""
    echo "  Per the LINT-ALLOW substantive-rationale discipline:"
    echo "  every marker MUST embody the four-question analysis ─"
    echo "    1. What is the use-case-specific library?"
    echo "    2. Is it already in the codebase?"
    echo "    3. What is the cost of using it?"
    echo "    4. Is there a structural reason it doesn't apply?"
    echo "  If #4 is \"no\", there is no shortcut — there is the work."
    echo "  Performance-of-compliance is the named failure mode (a marker"
    echo "  shaped like an audit trail without substance)."
    echo ""
    echo "  See:   sidecar/projection/PLAYBOOK.md → \"When you reach for a"
    echo "         string-composition primitive\" decision tree."
    echo "  Why:   sidecar/projection/DECISIONS.md → 2026-05-10 entry."
}

if [[ "$violations" -eq 0 ]]; then
    echo "${GREEN}lint-discipline: clean ($SRC).${RESET}"
    print_lint_allow_inventory
    exit 0
else
    echo ""
    echo "${YELLOW}lint-discipline: $violations violation(s).${RESET}"
    echo ""
    echo "Fix options per violation:"
    echo "  1. Refactor to BCL typed builder / pure F# fold / typed seam."
    echo "  2. Add per-line marker  // LINT-ALLOW: <rationale>"
    echo "     to the offending line if the discipline genuinely doesn't apply."
    echo "  3. Add top-of-file marker  // LINT-ALLOW-FILE: <rationale>"
    echo "     (covers all rules) or  // LINT-ALLOW-FILE-MUTATION: <rationale>"
    echo "     (covers mutation rules only) for whole-file exemptions."
    echo "  4. Open a paired DECISIONS amendment naming why the discipline is"
    echo "     being relaxed at this site."
    echo ""
    echo "Per-line concat-aversion markers (Rule 27 substantive-rationale):"
    echo "  Must be ≥30 chars after the colon AND contain at least one"
    echo "  substantive-vocabulary token (terminal / boundary / primitive /"
    echo "  round-trip / considered / alternative / gold-standard / escape /"
    echo "  irreducible / no [BCL/vendor/use-case-specific])."
    echo "  Substance is in the DECISIONS 2026-05-10 entry; the four-question"
    echo "  analysis is the structural prerequisite."
    echo ""
    echo "Reference: DECISIONS 2026-05-09 — No-string-concatenation /"
    echo "no-regex discipline; Built-in obligation; FP strict mode;"
    echo "Reified-primitive pattern; Hexagonal-coupling rules."
    echo "Reference: DECISIONS 2026-05-10 — LINT-ALLOW substantive-rationale."
    print_lint_allow_inventory
    exit 1
fi
