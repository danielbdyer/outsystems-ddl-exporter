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
#      deferred C/D), PLUS the per-bucket deferred-entry list generated from
#      the Skip attributes' own `Bucket` tokens (align-III.10 — the summary
#      cannot drift from the attributes because it IS the attributes).
#   2. The §1 round-trip *ladder* matrix. For each axis the generator reports
#      three rungs, each derived from the proof — never asserted by hand:
#        - **L1 (witness present)** — a test SELF-DECLARES as the axis's
#          round-trip witness via a `// @axis <Axis> roundtrip` tag on the
#          line above its backtick-quoted name (align-III.10; the tag rides
#          the test, so a rename keeps the binding and a deletion opens the
#          cell — the generator no longer hard-codes test names).
#        - **L2 (faithful)** — no *open* named tolerance sits on the axis. The
#          proof surface is `Tolerance.fs`'s `@ladder` tags: a variant tagged
#          `OpenGap` (a closeable fidelity debt) caps its axis at L2-partial;
#          `AcceptedFaithful` variants (representation-only, or covered by a
#          separate witness) do not. Retiring a variant deletes its tag, so the
#          axis auto-flips to faithful — L2 cannot be hand-marked.
#        - **L3 (composed)** — a `// @axis <Axis> migrate`-tagged witness
#          exists (the axis participates in the one-command migration).
#   3. Two cross-checks, each exit 3 on drift:
#        - every live `ToleratedDivergence` variant carries exactly one
#          `@ladder` tag, AND every tag's axis/disposition tokens are drawn
#          from the known vocabularies (align-III.10 — a typo'd axis used to
#          silently detach the tolerance from its axis, an over-claim);
#        - every (axis × rung) has AT MOST one `@axis` witness tag (two
#          claimants is ambiguity, not coverage).
#
# Honesty mechanism (the whole point of D1): a human cannot mark a cell green —
# the witness tag must ride a real test (L1/L3) and the open tolerance must be
# retired in code (L2). The generator UNDER-claims; it never over-claims.
# align-III.10 extended the honesty to the generator's OWN parsing: the Skip
# attribute is MULTILINE (backslash-continued strings) and the file carries a
# commented Skip exemplar in its doc header, so the bucket counts are derived
# from comment-stripped text with a state machine that reads each attribute to
# its `)>]` close — the first-line-only grep this replaced undercounted Bucket
# C (6 of the true 9) and counted the doc-comment decoy as a deferral.
#
# Scope honesty: L2 here is "no open *named* tolerance on the axis." Silent
# drops with no named surface (e.g. the cross-schema FK filter, debrief G4) and
# unwitnessed sub-axes are NOT auto-detected — they have no machine surface
# yet. "Witness/tolerance-present ≠ feature-complete."
#
# Pure bash + grep/awk; no dotnet required (mirrors scripts/verifiability-gate.sh
# + scripts/lint-discipline.sh). Run at chapter close; wire into CI alongside the
# lint + verifiability gates. A non-empty `git diff` on the generated file = a
# coverage shift. Exit 0 = wrote the matrix; 2 = setup error; 3 = a drifted
# `@ladder` / `@axis` surface (a cross-check failed).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
AX="$ROOT/tests/Projection.Tests/AxiomTests.fs"
TOL="$ROOT/src/Projection.Core/Tolerance.fs"
TESTS="$ROOT/tests"
OUT="$ROOT/NORTH_STAR.matrix.generated.md"
[ -f "$AX" ]  || { echo "matrix-status: AxiomTests.fs not found at $AX" >&2; exit 2; }
[ -f "$TOL" ] || { echo "matrix-status: Tolerance.fs not found at $TOL" >&2; exit 2; }

# --- T-II: executable-axiom rollup (align-III.10: honest multiline parsing) --
# Comment-strip first (the doc header carries a `[<Fact(Skip` exemplar), then
# read each Skip attribute to its `)>]` close and take the entry's test name
# from the next backtick-quoted line. Output: `<bucket>\t<name>` rows where
# bucket ∈ C | D | HORIZON | UNCLASSIFIED.
stripped_ax="$(grep -vE '^[[:space:]]*//' "$AX")"
skip_rows="$(printf '%s\n' "$stripped_ax" | awk '
  /\[<Fact\(Skip/ { inskip=1; buf="" }
  inskip {
    buf = buf $0 "\n"
    if (/\)>\]/) { pending=1; inskip=0 }
    next
  }
  pending && /``/ {
    name=$0; sub(/^[^`]*``/, "", name); sub(/``.*$/, "", name)
    bucket = "UNCLASSIFIED"
    if      (buf ~ /Skip = \"H-/) bucket = "HORIZON"
    else if (buf ~ /Bucket C/)    bucket = "C"
    else if (buf ~ /Bucket D/)    bucket = "D"
    print bucket "\t" name
    pending=0
  }')"
live=$(printf '%s\n' "$stripped_ax" | grep -cE '^[[:space:]]*\[<Fact>\]' || true)
skip_total=$(printf '%s\n' "$skip_rows" | grep -c . || true)
c=$(printf '%s\n' "$skip_rows" | awk -F'\t' '$1=="C"' | grep -c . || true)
d=$(printf '%s\n' "$skip_rows" | awk -F'\t' '$1=="D"' | grep -c . || true)
total=$(( live + skip_total ))

bucket_list() {
  # Markdown bullet rows for one bucket's entries, name-quoted.
  printf '%s\n' "$skip_rows" | awk -F'\t' -v b="$1" '$1==b {print "- `" $2 "`"}'
}

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

# --- align-III.10: @ladder TOKEN validation --------------------------------
# A tag whose axis is not a known ladder axis silently detached its variant
# from every axis (the tolerance vanished from the L2 check — an over-claim);
# an unknown disposition token likewise fell through the OpenGap filter. Both
# now refuse by name.
AXES="Schema Data Identity Time Decision"
bad_tags="$(printf '%s\n' "$ladder_tags" | awk -v axes="$AXES" '
  BEGIN { n=split(axes, a, " "); for (i=1; i<=n; i++) ok[a[i]]=1 }
  NF {
    if (!(($2) in ok))                                  { print $0 " (unknown axis)" ; next }
    if ($3 != "OpenGap" && $3 != "AcceptedFaithful")    { print $0 " (unknown disposition)" }
  }')"
if [ -n "$bad_tags" ]; then
  echo "matrix-status: @ladder token drift in Tolerance.fs — unknown axis/disposition token(s):" >&2
  printf '%s\n' "$bad_tags" | sed 's/^/    @ladder /' >&2
  echo "  Known axes: ${AXES}. Known dispositions: OpenGap | AcceptedFaithful." >&2
  exit 3
fi

open_for_axis() {
  # Variant names tagged OpenGap on the given axis, space-joined (or "").
  printf '%s\n' "$ladder_tags" | awk -v ax="$1" '$2==ax && $3=="OpenGap" {print $1}' | paste -sd' ' -
}

# --- T-I: the round-trip ladder matrix (@axis self-declaration) ------------
# align-III.10: the witnesses SELF-DECLARE. A test claims an (axis, rung) cell
# by carrying `// @axis <Axis> <roundtrip|migrate>` on the line directly above
# its backtick-quoted name. The generator discovers the name; it no longer
# hard-codes it, so a rename travels with the test and a deletion opens the
# cell (under-claim). Two claimants for one cell is ambiguity — exit 3.
axis_witness() {
  local axis="$1" rung="$2"
  local tag_re="^[[:space:]]*// @axis ${axis} ${rung}[[:space:]]*$"
  local count
  count=$(grep -rE --include='*.fs' -c "$tag_re" "$TESTS" 2>/dev/null | awk -F: '{s+=$NF} END{print s+0}')
  if [ "$count" -gt 1 ]; then
    echo "matrix-status: ambiguous @axis tag — ${count} tests claim '@axis ${axis} ${rung}' (exactly one may)." >&2
    grep -rlE --include='*.fs' "$tag_re" "$TESTS" | sed 's/^/    /' >&2
    exit 3
  fi
  [ "$count" -eq 0 ] && { echo ""; return; }
  grep -rhE --include='*.fs' -A3 "$tag_re" "$TESTS" 2>/dev/null \
    | grep -m1 -oE '``[^`]+``' | sed 's/^``//; s/``$//'
}

l1n=0; l2n=0; l3n=0; counted=0; rows=""
for axis in $AXES; do
  counted=$((counted+1))
  rtname="$(axis_witness "$axis" roundtrip)"
  mgname="$(axis_witness "$axis" migrate)"
  if [ -n "$rtname" ]; then l1="VERIFIED"; l1n=$((l1n+1)); else l1="OPEN"; fi
  if [ -n "$mgname" ]; then l3="VERIFIED"; l3n=$((l3n+1)); else l3="OPEN"; fi
  opens="$(open_for_axis "$axis")"

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

  case "$l1" in VERIFIED) i1="✅";; *) i1="⬚";; esac
  case "$l3" in VERIFIED) i3="✅";; *) i3="⬚";; esac
  rows+="| **$axis** | $i1 | $l2cell | $i3 | $opencell | $level |"$'\n'
done

variant_count=$(printf '%s\n' "$variant_names" | grep -c . || true)
open_count=$(printf '%s\n' "$ladder_tags" | awk '$3=="OpenGap"' | grep -c . || true)

{
  echo "<!-- GENERATED by scripts/matrix-status.sh — DO NOT EDIT BY HAND."
  echo "     A non-empty git diff on this file at chapter close = a coverage shift."
  echo "     Regenerate: ./scripts/matrix-status.sh -->"
  echo
  echo "# NORTH STAR — Matrix Status (generated)"
  echo
  echo "_Derived from \`tests/Projection.Tests/AxiomTests.fs\` + \`src/Projection.Core/Tolerance.fs\` (the \`@ladder\` tags) + the test tree's \`@axis\` witness tags. The §1 bullseye, self-reported at the **ladder level**._"
  echo
  echo "## T-II — Executable-axiom totality (L2 formal axioms)"
  echo
  echo "| Class | Meaning | Count |"
  echo "|---|---|---:|"
  echo "| Live | verified (\"verified by …\") or convention-enforced \`[<Fact>]\` | $live |"
  echo "| Deferred C | weakness — \`[<Fact(Skip … Bucket C …)>]\` | $c |"
  echo "| Deferred D | unnamed/unbacked — \`[<Fact(Skip … Bucket D …)>]\` | $d |"
  echo "| Horizon stubs | future-feature reservations (\`Skip = \"H-…\"\`; bucket-exempt) | $(printf '%s\n' "$skip_rows" | awk -F'\t' '$1=="HORIZON"' | grep -c . || true) |"
  echo "| **total axiom entries** | | **$total** |"
  echo
  echo "**Verifiability gate: \`$tii\`** — no deferral claims verified (no phantom Bucket-A/B); every deferral names its bucket."
  echo
  echo "### Deferred entries (generated from the Skip attributes' own \`Bucket\` tokens; align-III.10)"
  echo
  echo "**Bucket C (weakness, promotion trigger named in each Skip):**"
  echo
  bucket_list "C"
  echo
  echo "**Bucket D (unnamed/unbacked):**"
  echo
  bucket_list "D"
  if [ -n "$(bucket_list UNCLASSIFIED)" ]; then
    echo
    echo "**UNCLASSIFIED axiom/theorem deferrals (classify C/D — the gate WARNs on these):**"
    echo
    bucket_list "UNCLASSIFIED"
  fi
  echo
  echo "## T-I — Round-trip ladder (the §1 bullseye matrix)"
  echo
  echo "Each axis carries three rungs, each derived from the proof — never hand-asserted."
  echo "**L1** = a \`// @axis <Axis> roundtrip\`-tagged witness test exists. **L2** = no *open*"
  echo "named tolerance sits on the axis (an \`@ladder … OpenGap\` variant in \`Tolerance.fs\`"
  echo "caps the axis at L2-partial; retiring the variant in code auto-flips it). **L3** = a"
  echo "\`// @axis <Axis> migrate\`-tagged witness covers the axis. The **Ladder** column is"
  echo "the honest weakest-rung summary."
  echo
  echo "| Axis | L1 witness | L2 faithful | L3 composed | Open tolerances | Ladder |"
  echo "|---|:--:|:--:|:--:|---|---|"
  printf '%s' "$rows"
  echo
  echo "**Rungs reached: L1 $l1n/$counted · L2 $l2n/$counted · L3 $l3n/$counted.** Tolerance set:"
  echo "$variant_count named, of which **$open_count open** (\`OpenGap\`). A cell cannot be"
  echo "hand-marked: L1/L3 require the \`@axis\`-tagged witness test to exist; L2 requires the"
  echo "open tolerance to be retired from \`Tolerance.fs\`. The generator under-claims; it"
  echo "never over-claims."
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
