#!/usr/bin/env bash
#
# verifiability-gate.sh — EXECUTION_PLAN slice E1; NORTH_STAR criterion 3 (T-II,
# executable-axiom totality).
#
# Enforces the single audit surface `tests/Projection.Tests/AxiomTests.fs` to be
# honest about its own coverage, so no surface can claim a coverage bucket the tests
# do not support (the "phantom-Bucket-A" defect class — a claimed-verified axiom with
# no live witness, e.g. the historical DACPAC-L3-S2).
#
# AxiomTests.fs encodes the verifiability-triangle bucket per entry as:
#   * Bucket A (verified)   — a live `[<Fact>]` / `[<Property>]` whose name says
#                             "verified by <Test>" (delegates to a real test).
#   * Bucket B (convention) — a live `[<Fact>]` whose name says "(convention-enforced)".
#   * Bucket C / D (deferred) — a `[<Fact(Skip = "... Bucket C|D ...")>]` whose
#                             rationale names the bucket and the promotion trigger.
#   * Horizon stubs         — `[<Fact(Skip = "H-NNN ...")>]` reserve a *future feature*
#                             (HORIZON.md), not a bucketed axiom; exempt from the bucket rule.
#
# The honesty contract (the hard gate):
#   NO deferral (Skip) may claim "Bucket A" or "Bucket B" — a deferral that claims
#   verified is the phantom defect.                                          -> FAIL
# Advisory (does not fail the build):
#   A non-horizon (axiom/theorem) deferral that names no bucket is surfaced as a WARN
#   so it can be classified, but it is not a phantom and does not block.
#
# Pure bash + grep; no dotnet required (mirrors scripts/lint-discipline.sh). Wire into
# CI alongside the lint gate. Exit 0 = honest; 1 = phantom drift; 2 = setup error.
#
# The L3 product-axiom buckets live in PRODUCT_AXIOMS.md / the verifiability-triangle
# audit (prose surfaces); making AxiomTests.fs the gated single-source-of-truth and
# generating those surfaces from it (E2/E5) is how the phantom class is closed for good.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
AX="$ROOT/tests/Projection.Tests/AxiomTests.fs"
[ -f "$AX" ] || { echo "verifiability-gate: AxiomTests.fs not found at $AX" >&2; exit 2; }

skips="$(grep -E '\[<Fact\(Skip' "$AX" || true)"
live=$(grep -cE '^[[:space:]]*\[<Fact>\]' "$AX" || true)
skip_total=$(printf '%s\n' "$skips" | grep -c . || true)
skip_c=$(printf '%s\n' "$skips" | grep -cE 'Bucket C' || true)
skip_d=$(printf '%s\n' "$skips" | grep -cE 'Bucket D' || true)
phantom="$(printf '%s\n' "$skips" | grep -E 'Bucket A|Bucket B' || true)"

# Axiom/theorem deferrals only (exempt H-NNN horizon-feature reservations).
axiom_skips="$(printf '%s\n' "$skips" | grep -vE 'Skip = "H-' || true)"
axiom_skip_total=$(printf '%s\n' "$axiom_skips" | grep -c . || true)
axiom_classified=$(printf '%s\n' "$axiom_skips" | grep -cE 'Bucket [A-D]' || true)
axiom_unclassified=$(( axiom_skip_total - axiom_classified ))

echo "verifiability-gate — AxiomTests.fs: ${live} live (verified/convention) + ${skip_total} deferred (axiom buckets C=${skip_c}, D=${skip_d}; horizon stubs exempt)"

if [ -n "$phantom" ]; then
  echo
  echo "FAIL: $(printf '%s\n' "$phantom" | grep -c .) deferral(s) claim Bucket A/B (deferred yet claimed-verified — the phantom defect):"
  printf '%s\n' "$phantom" | sed 's/^/    /'
  echo
  echo "Fix: ship the witness (flip Skip->[<Fact>] with 'verified by') or correct the Skip rationale's bucket."
  exit 1
fi

if [ "$axiom_unclassified" -gt 0 ]; then
  echo "WARN: ${axiom_unclassified} axiom/theorem deferral(s) name no bucket — classify them C/D (advisory; not a phantom)." >&2
fi

echo "OK: no deferral claims verified (zero phantom Bucket-A/B). The surface is honest about its own coverage."
exit 0
