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

# align-III.10 — honest parsing. The Skip attribute is MULTILINE (backslash-
# continued strings) and the file's doc header carries a commented `[<Fact(Skip`
# exemplar, so the counts derive from COMMENT-STRIPPED text with a state machine
# reading each attribute to its `)>]` close. The first-line-only grep this
# replaced undercounted Bucket C (a continuation-line token was invisible),
# WARNed on fully-classified deferrals as "unclassified", and counted the doc
# exemplar as a deferral. Phantom rule, narrowed to attribute grain: an attr is
# a phantom iff it mentions Bucket A/B while declaring NO C/D — a C/D-classified
# rationale may narrate its promotion target ("Promoted to Bucket A when …")
# without being a claim.
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
    kind = "UNCLASSIFIED"
    if      (buf ~ /Skip = \"H-/) kind = "HORIZON"
    else if (buf ~ /Bucket C/)    kind = "C"
    else if (buf ~ /Bucket D/)    kind = "D"
    phantom = ((buf ~ /Bucket A/ || buf ~ /Bucket B/) && buf !~ /Bucket [CD]/) ? "PHANTOM" : "-"
    print kind "\t" phantom "\t" name
    pending=0
  }')"
live=$(printf '%s\n' "$stripped_ax" | grep -cE '^[[:space:]]*\[<Fact>\]' || true)
skip_total=$(printf '%s\n' "$skip_rows" | grep -c . || true)
skip_c=$(printf '%s\n' "$skip_rows" | awk -F'\t' '$1=="C"' | grep -c . || true)
skip_d=$(printf '%s\n' "$skip_rows" | awk -F'\t' '$1=="D"' | grep -c . || true)
phantom="$(printf '%s\n' "$skip_rows" | awk -F'\t' '$2=="PHANTOM" {print $3}' || true)"

# Axiom/theorem deferrals only (exempt H-NNN horizon-feature reservations).
axiom_unclassified=$(printf '%s\n' "$skip_rows" | awk -F'\t' '$1=="UNCLASSIFIED"' | grep -c . || true)

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
