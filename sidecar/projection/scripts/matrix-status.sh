#!/usr/bin/env bash
#
# matrix-status.sh — EXECUTION_PLAN slice 6.E.1 / debrief D1 (the self-
# verification meta-cell). Makes NORTH_STAR.md §1 self-reporting at the
# *ladder level* (NORTH_STAR criterion 5, documentation totality / T-IV).
#
# Derives, from the code, three things and writes them to
# NORTH_STAR.matrix.generated.md:
#   1. The L2 executable-axiom rollup + the T-II gate verdict, machine-derived
#      from tests/Projection.Tests/AxiomTests.fs (live verified/convention vs
#      deferred C/D).
#   2. The §1 round-trip *ladder* matrix. For each axis the generator reports
#      three rungs, each derived from the proof — never asserted by hand:
#        - **L1 (witness present)** — a backtick-quoted round-trip test by the
#          cell's witness name exists in the tree.
#        - **L2 (faithful)** — no *open* named tolerance sits on the axis. The
#          proof surface is `Tolerance.fs`'s `@ladder` tags: a variant tagged
#          `OpenGap` (a closeable fidelity debt) caps its axis at L2-partial;
#          `AcceptedFaithful` variants (representation-only, or covered by a
#          separate witness) do not. Retiring a variant deletes its tag, so the
#          axis auto-flips to faithful — L2 cannot be hand-marked.
#        - **L3 (composed)** — a backtick-quoted `migrate A B` witness covering
#          the axis exists (the axis participates in the one-command migration).
#   3. The cross-check that every live `ToleratedDivergence` variant (per the
#      `name` single-source-of-truth) carries exactly one `@ladder` tag — a new
#      variant cannot land untagged, and a renamed variant fails fast.
#
# Honesty mechanism (the whole point of D1): a human cannot mark a cell green —
# the witness test must exist (L1/L3) and the open tolerance must be retired in
# code (L2). The generator UNDER-claims; it never over-claims.
#
# Scope honesty: L2 here is "no open *named* tolerance on the axis." Silent
# drops with no named surface (e.g. the cross-schema FK filter, debrief G4) and
# unwitnessed sub-axes (e.g. the 3-axis Decision adjunction, debrief G12) are
# NOT auto-detected — they have no machine surface yet. They are tracked in the
# debrief until E2/F2 give them a named diagnostic/witness, at which point they
# too become machine-visible. "Witness/tolerance-present ≠ feature-complete."
#
# Pure bash + grep; no dotnet required (mirrors scripts/verifiability-gate.sh +
# scripts/lint-discipline.sh). Run at chapter close; wire into CI alongside the
# lint + verifiability gates. A non-empty `git diff` on the generated file = a
# coverage shift. Exit 0 = wrote the matrix; 2 = setup error; 3 = an untagged /
# drifted tolerance variant (the cross-check failed).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
AX="$ROOT/tests/Projection.Tests/AxiomTests.fs"
TOL="$ROOT/src/Projection.Core/Tolerance.fs"
TESTS="$ROOT/tests"
OUT="$ROOT/NORTH_STAR.matrix.generated.md"
[ -f "$AX" ]  || { echo "matrix-status: AxiomTests.fs not found at $AX" >&2; exit 2; }
[ -f "$TOL" ] || { echo "matrix-status: Tolerance.fs not found at $TOL" >&2; exit 2; }

# --- T-II: executable-axiom rollup (unchanged) -----------------------------
skips="$(grep -E '\[<Fact\(Skip' "$AX" || true)"
live=$(grep -cE '^[[:space:]]*\[<Fact>\]' "$AX" || true)
skip_total=$(printf '%s\n' "$skips" | grep -c . || true)
c=$(printf '%s\n' "$skips" | grep -cE 'Bucket C' || true)
d=$(printf '%s\n' "$skips" | grep -cE 'Bucket D' || true)
total=$(( live + skip_total ))

if "$ROOT/scripts/verifiability-gate.sh" >/dev/null 2>&1; then tii="PASS"; else tii="FAIL"; fi

# --- Tolerance cross-check: every live variant carries one @ladder tag -----
# `name` is the single source of truth for the live variant set (Tolerance.fs
# docstring); the type's `-> "Variant"` arms are the only `-> "..."` literals.
variant_names="$(grep -oE 'ToleratedDivergence\.[A-Za-z0-9]+ +-> +"[A-Za-z0-9]+"' "$TOL" | grep -oE '"[A-Za-z0-9]+"' | tr -d '"' | sort -u)"
# Each variant's doc block ends with `@ladder <Variant> <Axis> <Disposition>`.
ladder_tags="$(grep -oE '@ladder [A-Za-z0-9]+ [A-Za-z]+ [A-Za-z]+' "$TOL" | sed 's/@ladder //' || true)"
tag_names="$(printf '%s\n' "$ladder_tags" | awk 'NF{print $1}' | sort -u)"

missing="$(comm -23 <(printf '%s\n' "$variant_names") <(printf '%s\n' "$tag_names") || true)"
orphan="$(comm -13 <(printf '%s\n' "$variant_names") <(printf '%s\n' "$tag_names") || true)"
if [ -n "$missing" ] || [ -n "$orphan" ]; then
  echo "matrix-status: @ladder tag drift in Tolerance.fs (the ladder cross-check)." >&2
  [ -n "$missing" ] && { echo "  live variant(s) with NO @ladder tag:" >&2; printf '%s\n' "$missing" | sed 's/^/    /' >&2; }
  [ -n "$orphan" ]  && { echo "  @ladder tag(s) with no live variant (rename/retire drift):" >&2; printf '%s\n' "$orphan" | sed 's/^/    /' >&2; }
  echo "  Fix: give every live variant exactly one '@ladder <Variant> <Axis> <Disposition>' doc line." >&2
  exit 3
fi

open_for_axis() {
  # Variant names tagged OpenGap on the given axis, space-joined (or "").
  printf '%s\n' "$ladder_tags" | awk -v ax="$1" '$2==ax && $3=="OpenGap" {print $1}' | paste -sd' ' -
}

# --- T-I: the round-trip ladder matrix -------------------------------------
# "Axis|round-trip-witness-substring|migrate(A B)-witness-substring". A rung is
# VERIFIED iff a backtick-quoted test name containing the substring exists under
# tests/ (matches `let ``...``` and `member _.``...``` forms). Axis names match
# the `@ladder` axis tokens so the tolerance set joins by axis.
cells='Schema|PhysicalSchema diff|one execute evolves
Data|data canary|executeWithData migrates the sink schema
Identity|reload preserves SsKey|migrate-with-data re-keys
Time|replayTo genesis|the full A->B loop
Decision|reproduces the DecisionOverlay|migrate refuses a NOT-NULL tightening'

witness_status() {
  local pat="$1"
  if grep -rhE "\`\`[^\`]*${pat}" "$TESTS" >/dev/null 2>&1; then echo "VERIFIED"; else echo "OPEN"; fi
}
icon()    { case "$1" in VERIFIED) echo "✅";; *) echo "⬚";; esac; }

l1n=0; l2n=0; l3n=0; counted=0; rows=""
while IFS='|' read -r axis rtpat mgpat; do
  [ -z "$axis" ] && continue
  counted=$((counted+1))
  l1=$(witness_status "$rtpat")
  l3=$(witness_status "$mgpat")
  opens="$(open_for_axis "$axis")"
  [ "$l1" = "VERIFIED" ] && l1n=$((l1n+1))
  [ "$l3" = "VERIFIED" ] && l3n=$((l3n+1))

  if [ -n "$opens" ]; then
    l2cell="◑ L2-partial"
    opencell="\`$(printf '%s' "$opens" | sed 's/ /`, `/g')\`"
  else
    l2cell="✅ faithful"; l2n=$((l2n+1)); opencell="—"
  fi

  if   [ "$l1" != "VERIFIED" ]; then level="⬚ L0"
  elif [ -n "$opens" ];         then level="◑ L2-partial"
  elif [ "$l3" != "VERIFIED" ]; then level="✅ L2"
  else                               level="✅ L3"
  fi

  rows+="| **$axis** | $(icon "$l1") | $l2cell | $(icon "$l3") | $opencell | $level |"$'\n'
done <<< "$cells"

variant_count=$(printf '%s\n' "$variant_names" | grep -c . || true)
open_count=$(printf '%s\n' "$ladder_tags" | awk '$3=="OpenGap"' | grep -c . || true)

{
  echo "<!-- GENERATED by scripts/matrix-status.sh — DO NOT EDIT BY HAND."
  echo "     A non-empty git diff on this file at chapter close = a coverage shift."
  echo "     Regenerate: ./scripts/matrix-status.sh -->"
  echo
  echo "# NORTH STAR — Matrix Status (generated)"
  echo
  echo "_Derived from \`tests/Projection.Tests/AxiomTests.fs\` + \`src/Projection.Core/Tolerance.fs\` (the \`@ladder\` tags) + the test tree. The §1 bullseye, self-reported at the **ladder level**._"
  echo
  echo "## T-II — Executable-axiom totality (L2 formal axioms)"
  echo
  echo "| Class | Meaning | Count |"
  echo "|---|---|---:|"
  echo "| Live | verified (\"verified by …\") or convention-enforced \`[<Fact>]\` | $live |"
  echo "| Deferred C | weakness — \`[<Fact(Skip … Bucket C …)>]\` | $c |"
  echo "| Deferred D | unnamed/unbacked — \`[<Fact(Skip … Bucket D …)>]\` | $d |"
  echo "| **total axiom entries** | | **$total** |"
  echo
  echo "**Verifiability gate: \`$tii\`** — no deferral claims verified (no phantom Bucket-A/B); every deferral names its bucket."
  echo
  echo "## T-I — Round-trip ladder (the §1 bullseye matrix)"
  echo
  echo "Each axis carries three rungs, each derived from the proof — never hand-asserted."
  echo "**L1** = a round-trip witness test exists. **L2** = no *open* named tolerance sits"
  echo "on the axis (an \`@ladder … OpenGap\` variant in \`Tolerance.fs\` caps the axis at"
  echo "L2-partial; retiring the variant in code auto-flips it). **L3** = a \`migrate A B\`"
  echo "witness covers the axis. The **Ladder** column is the honest weakest-rung summary."
  echo
  echo "| Axis | L1 witness | L2 faithful | L3 composed | Open tolerances | Ladder |"
  echo "|---|:--:|:--:|:--:|---|---|"
  printf '%s' "$rows"
  echo
  echo "**Rungs reached: L1 $l1n/$counted · L2 $l2n/$counted · L3 $l3n/$counted.** Tolerance set:"
  echo "$variant_count named, of which **$open_count open** (\`OpenGap\`). A cell cannot be"
  echo "hand-marked: L1/L3 require the witness test to exist; L2 requires the open tolerance"
  echo "to be retired from \`Tolerance.fs\`. The generator under-claims; it never over-claims."
  echo
  echo "> **Witness/tolerance-present ≠ feature-complete.** L2 here is \"no open *named*"
  echo "> tolerance on the axis.\" Silent drops with no named surface (the cross-schema FK"
  echo "> filter, debrief G4) and unwitnessed sub-axes (the 3-axis Decision adjunction,"
  echo "> debrief G12) are NOT auto-detected — they have no machine surface yet, and are"
  echo "> tracked in \`DEBRIEF_2026_06_02\` until E2/F2 give them a named diagnostic/witness."
  echo "> L3 here is \"a composition witness exists for the axis,\" not \"faithful under every"
  echo "> spanning axis\" (T-VI atomicity/permissions ride the debrief until cluster A names them)."
  echo
  # Deterministic footer (T1): no wall-clock stamp — the artifact is a pure
  # function of the proof surfaces, so `git diff` on it = a coverage shift, and
  # the CI currency gate (D2) is meaningful. The "when" is the git commit.
  echo "_Self-reported · gate=$tii · L2 axioms live/C/D=${live}/${c}/${d} · rungs L1/L2/L3=${l1n}/${l2n}/${l3n} of ${counted} · tolerances ${variant_count} (${open_count} open)_"
} > "$OUT"

echo "matrix-status: wrote $OUT (gate=$tii; rungs L1/L2/L3=${l1n}/${l2n}/${l3n} of ${counted}; tolerances ${variant_count}, ${open_count} open)"
