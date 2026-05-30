#!/usr/bin/env bash
#
# verifiability-gate.sh — EXECUTION_PLAN slice E1; NORTH_STAR criterion 3 (T-II,
# executable-axiom totality).
#
# Enforces the single audit surface `tests/Projection.Tests/AxiomTests.fs` to be
# internally honest about its own coverage, so no surface can claim a coverage
# bucket the tests do not support (the "phantom-Bucket-A" defect class — a
# claimed-verified axiom with no live witness, e.g. the historical DACPAC-L3-S2).
#
# Invariant (two-way, derived from the file's own bucket legend):
#   * [bucket A] / [bucket B]  MUST be a live [<Fact>]            (a green witness exists)
#   * [bucket C] / [bucket D]  MUST be [<Fact(Skip = "...")>]     (an honest deferral w/ trigger)
#
# Pure bash + awk; no dotnet required (mirrors scripts/lint-discipline.sh). Wire
# into CI alongside the lint gate. Exit 0 = honest; exit 1 = drift; exit 2 = setup error.
#
# The L3 product-axiom buckets live in PRODUCT_AXIOMS.md / the verifiability-triangle
# audit (prose surfaces); making AxiomTests.fs the gated single-source-of-truth and
# generating those surfaces from it (E2/E5) is how the phantom class is closed for good.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
AX="$ROOT/tests/Projection.Tests/AxiomTests.fs"
[ -f "$AX" ] || { echo "verifiability-gate: AxiomTests.fs not found at $AX" >&2; exit 2; }

violations="$(awk '
  /\[<Fact\(Skip/ { state="skip"; next }
  /\[<Fact>\]/    { state="fact"; next }
  /^[[:space:]]*let / && /\[bucket [A-D]\]/ {
     b=$0;  sub(/.*\[bucket /,"",b);  sub(/\].*/,"",b)
     id=$0; sub(/.*``/,"",id);        sub(/:.*/,"",id); gsub(/[[:space:]]/,"",id)
     if ((b=="A" || b=="B") && state!="fact")
        printf("FAIL  %-12s bucket %s claims verified/convention but is Skipped — no live witness\n", id, b)
     if ((b=="C" || b=="D") && state!="skip")
        printf("FAIL  %-12s bucket %s claims unverified but is a live Fact — mis-bucketed or no-op\n", id, b)
     state=""
  }
' "$AX")"

a=$(grep -c '\[bucket A\]' "$AX" || true)
b=$(grep -c '\[bucket B\]' "$AX" || true)
c=$(grep -c '\[bucket C\]' "$AX" || true)
d=$(grep -c '\[bucket D\]' "$AX" || true)

echo "verifiability-gate — AxiomTests.fs coverage: A=$a B=$b C=$c D=$d (total $((a+b+c+d)))"

if [ -n "$violations" ]; then
  echo
  echo "$violations"
  echo
  echo "DRIFT: $(printf '%s\n' "$violations" | grep -c '^FAIL') axiom(s) claim a bucket their decorator does not support."
  echo "Fix: ship the witness (flip Skip->Fact) or correct the [bucket X] tag in the same commit."
  exit 1
fi

echo "OK: every bucket-A/B axiom has a live witness; every bucket-C/D axiom is an honest deferral."
exit 0
