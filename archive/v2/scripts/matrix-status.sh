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
# "Axis|round-trip-witness-FULL-NAME|migrate(A B)-witness-FULL-NAME". A rung is
# VERIFIED iff the EXACT, FULL backtick-quoted test name exists under tests/.
# The binding is structural, not a loose substring: `witness_status` matches the
# whole name bounded by its `` `` `` delimiters (fixed-string, so the name's
# regex metacharacters — `(`, `;`, `—`, `→`, `?`, `/`, `.` — are literal), so a
# witness can NOT be satisfied by an accidental substring hit on an unrelated
# test (e.g. the bare `data canary` prefix matches eight Transfer tests; only the
# named `data canary: multi-table FK chain …` test is the Data L1 witness). Each
# name below was confirmed to resolve to exactly one test on the current tree, so
# the regenerated matrix keeps the same verdicts (L1 5/5). Matches `let ``…``` and
# `member _.``…``` forms alike. Axis names match the `@ladder` axis tokens so the
# tolerance set joins by axis.
cells='Schema|M3: V2-internal closure — programmatic Catalog round-trips through emit / deploy / read with empty PhysicalSchema diff|migrate A B canary: one execute evolves A→B across three channels; B reproduces B, data survives, re-run is idempotent
Data|data canary: multi-table FK chain round-trips with empty PhysicalSchema diff|migrate canary: executeWithData migrates the sink schema then loads rows from the source
Identity|Identity round-trip: reload preserves SsKey across emit / deploy / ReadSide|AC-X2: one-command migrate-with-data re-keys Order FKs to the Sink'"'"'s email-matched identity (fails for Map.empty)
Time|Time round-trip (replay): replayTo genesis recovers the genesis catalog|6.D.1: the full A->B loop — migrate, record, then reconstruct reproduces B (durable round-trip)
Decision|decision adjunction: emitted-then-read-back schema reproduces the DecisionOverlay|G9: migrate refuses a NOT-NULL tightening on NULL-bearing data via a pre-flight, before any ALTER'

witness_status() {
  # Exact, anchored full-name binding: the test name must appear verbatim,
  # bounded by its `` `` `` delimiters. Fixed-string (`grep -F`) so the name's
  # regex metacharacters are literal; the leading/trailing `` `` `` anchors the
  # match to a whole backtick-quoted name, defeating accidental substring hits.
  local name="$1"
  if grep -rhF "\`\`${name}\`\`" "$TESTS" >/dev/null 2>&1; then echo "VERIFIED"; else echo "OPEN"; fi
}
icon()    { case "$1" in VERIFIED) echo "✅";; *) echo "⬚";; esac; }

l1n=0; l2n=0; l3n=0; counted=0; rows=""
while IFS='|' read -r axis rtname mgname; do
  [ -z "$axis" ] && continue
  counted=$((counted+1))
  l1=$(witness_status "$rtname")
  l3=$(witness_status "$mgname")
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
  echo "> filter, debrief G4) are NOT auto-detected — they have no machine surface yet, and are"
  echo "> tracked in \`DEBRIEF_2026_06_02\` until a named diagnostic/witness lands. The 3-axis"
  echo "> Decision adjunction (debrief G12) IS now witnessed — M1 (THE VECTOR Wave 1) routes"
  echo "> FK-trust + unique-promotion through the general \`PhysicalSchema.diff\` comparator,"
  echo "> so the Decision axis is honestly faithful, not asserted."
  echo "> L3 here is \"a composition witness exists for the axis,\" not \"faithful under every"
  echo "> spanning axis\" (T-VI). The two T-VI dimensions that are NOT round-trip axes are named"
  echo "> here so the five-row ladder above is not read as the whole basis:"
  echo "> **(a) Transactionality/Rollback** — a mid-write crash is a *named* refusal"
  echo "> (\`GateLabel.MidWriteNotProtected\`, THE VECTOR Wave 2), and the compensating-undo arm is"
  echo "> now BUILT and live-witnessed (M21, 2026-06-16): a mid-deploy failure rides the groupoid"
  echo "> \`inverse\` (\`CatalogDiff.inverse\`, rename channel) to return the substrate to A"
  echo "> (\`ExecutionRolledBack\`, verified by read-back) or names the residual"
  echo "> (\`PartialWriteUnrecovered\` — refuse-don't-corrupt, never a silent partial), witnessed"
  echo "> by the \`MigrationCanaryTests\` M21 canaries on the warm container. The **data leg** has the"
  echo "> twin (M23): a failed transfer reverts the sink-minted rows by captured key — executed"
  echo "> (\`--auto-revert\`) or emitted as a precise revert script artifact — \`TransferCanaryTests\`."
  echo "> The atomic \`BEGIN TRAN\` wrapper is BUILT as an opt-in \`--atomic\` (M22) but **scoped to"
  echo "> LOCAL full-access databases** — production schema ships via ADO/Octopus/SSDT (not"
  echo "> direct-connect) and the managed cloud is DML-only, so for those targets the compensating"
  echo "> channel (M21/M23) is the arm; the estate-scale giant transaction stays gated on P7b"
  echo "> throughput. **(b) Permissions** — the A2 pre-flight *gates* on"
  echo "> grants (it refuses a write-denied sink) but grants/roles/RLS are NOT a projected axis (no"
  echo "> \`Grant\` IR facet, no \`GRANT\` in the \`Statement\` DU, no permission channel in"
  echo "> \`CatalogDiff\`, no readback): the engine can *refuse* but cannot *project / diff /"
  echo "> round-trip* a permission decision, so the gate's existence must not be read as the axis"
  echo "> being closed. The full permissions axis fires only when a flow must *publish* grants (the"
  echo "> eject). Both are out-of-ladder by construction (a non-round-trip dimension is a category"
  echo "> error in a round-trip \`ToleratedDivergence\`), named here per THE VECTOR Wave 5 honesty."
  echo
  # Deterministic footer (T1): no wall-clock stamp — the artifact is a pure
  # function of the proof surfaces, so `git diff` on it = a coverage shift, and
  # the CI currency gate (D2) is meaningful. The "when" is the git commit.
  echo "_Self-reported · gate=$tii · L2 axioms live/C/D=${live}/${c}/${d} · rungs L1/L2/L3=${l1n}/${l2n}/${l3n} of ${counted} · tolerances ${variant_count} (${open_count} open)_"
} > "$OUT"

echo "matrix-status: wrote $OUT (gate=$tii; rungs L1/L2/L3=${l1n}/${l2n}/${l3n} of ${counted}; tolerances ${variant_count}, ${open_count} open)"
