#!/usr/bin/env bash
#
# V2 sidecar formal-verification runner — the rung-4/rung-5 gate
# (FORMAL_METHODS.md §4).
#
# WHAT THIS RUNS
#   1. Alloy 6 bounded model checks over formal/alloy/*.als (rung 4:
#      the temporal machines — Lifecycle, Approval, CutoverLadder —
#      and the structural Catalog spec). Every command in every spec
#      carries an explicit `expect` annotation; this script FAILS on
#      any expectation mismatch:
#        - a `check ... expect 0` that finds a counterexample means a
#          LAW BROKE;
#        - a `run ... expect 1` that goes UNSAT means the model got
#          over-constrained (vacuity) — equally a failure;
#        - a Part VI illegal-state witness (`run ... expect 1` in
#          Catalog.als) that goes UNSAT means a representable-illegal
#          state got CLOSED — good news, but the spec ledger must be
#          updated in the same commit, so the mismatch still fails.
#   2. Dafny proofs over formal/dafny/*.dfy (rung 5: the change
#      algebra's groupoid laws + the lineage writer-monad laws).
#   3. The anti-drift existence gate: every formal artifact cited in
#      FORMAL_METHODS.md §2 must exist on the tree.
#
# TOOLING (fetched on demand into formal/.tools, never committed)
#   - Alloy: org.alloytools.alloy.dist 6.2.0 from Maven Central,
#     sha256-pinned below. Requires Java 11+.
#   - Dafny: dotnet tool, version-pinned below. Requires the repo's
#     .NET SDK (global.json).
#   - Z3: the `z3` executable; `pip install z3-solver` provides one if
#     the host has none.
#
# Usage:
#   scripts/model-check.sh          # everything
#   scripts/model-check.sh alloy    # rung-4 specs only
#   scripts/model-check.sh dafny    # rung-5 proofs only

set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(dirname "$here")"
formal="$root/formal"
tools="$formal/.tools"
outdir="${MODEL_CHECK_OUT:-$(mktemp -d)}"

ALLOY_VERSION="6.2.0"
ALLOY_SHA256="6037cbeee0e8423c1c468447ed10f5fcf2f2743a2ffc39cb1c81f2905c0fdb9d"
ALLOY_URL="https://repo1.maven.org/maven2/org/alloytools/org.alloytools.alloy.dist/${ALLOY_VERSION}/org.alloytools.alloy.dist-${ALLOY_VERSION}.jar"
ALLOY_JAR="$tools/org.alloytools.alloy.dist.jar"

DAFNY_VERSION="4.11.0"
DAFNY_BIN="$tools/dafny/dafny"

mode="${1:-all}"
fail=0

log() { printf '\033[36m[model-check]\033[0m %s\n' "$*"; }
bad() { printf '\033[31m[model-check]\033[0m %s\n' "$*"; fail=1; }

fetch_alloy() {
  if [[ ! -f "$ALLOY_JAR" ]]; then
    log "fetching Alloy ${ALLOY_VERSION} from Maven Central..."
    mkdir -p "$tools"
    curl -sSL -o "$ALLOY_JAR" "$ALLOY_URL"
  fi
  echo "${ALLOY_SHA256}  ${ALLOY_JAR}" | sha256sum -c - >/dev/null \
    || { bad "Alloy jar sha256 mismatch — refusing to run an unpinned tool"; exit 1; }
}

fetch_dafny() {
  if [[ ! -x "$DAFNY_BIN" ]]; then
    log "installing Dafny ${DAFNY_VERSION} (dotnet tool)..."
    dotnet tool install --tool-path "$tools/dafny" dafny --version "$DAFNY_VERSION" >/dev/null
  fi
}

find_z3() {
  if command -v z3 >/dev/null; then command -v z3; return; fi
  if [[ -x /usr/local/bin/z3 ]]; then echo /usr/local/bin/z3; return; fi
  log "no z3 on PATH; installing via pip (z3-solver)..."
  pip install --quiet z3-solver
  command -v z3
}

# Parse an alloy exec receipt.json: every command's outcome must match
# its expectation. `check` commands expect 0 solutions by default (the
# receipt serializes `expect 0` as null); `run` commands must carry an
# explicit `expect` annotation. Emits one line per mismatch.
check_receipt() {
  python3 - "$1" <<'PY'
import json, sys
receipt = json.load(open(sys.argv[1]))
mismatches = 0
for name, cmd in receipt.get("commands", {}).items():
    kind = cmd.get("type", "?")
    expects = cmd.get("expects")
    if expects in (None, -1):
        if kind == "check":
            expects = 0  # a check's law must have no counterexample
        else:
            print(f"UNANNOTATED {kind} {name}: run commands must carry an explicit `expect`")
            mismatches += 1
            continue
    sat = 1 if cmd.get("solution") else 0
    if sat != expects:
        outcome = "SAT (instance/counterexample found)" if sat else "UNSAT (none found)"
        print(f"MISMATCH {kind} {name}: expected expect={expects}, got {outcome}")
        mismatches += 1
sys.exit(1 if mismatches else 0)
PY
}

run_alloy() {
  fetch_alloy
  local spec base specout
  for spec in "$formal"/alloy/*.als; do
    base="$(basename "$spec" .als)"
    specout="$outdir/$base"
    log "alloy: $base"
    if ! java -jar "$ALLOY_JAR" exec -f -o "$specout" "$spec" 2>&1 \
        | grep -vE "^(Picked up|$)" | sed 's/^/    /'; then
      bad "alloy exec failed for $base"
      continue
    fi
    if [[ ! -f "$specout/receipt.json" ]]; then
      bad "$base produced no receipt.json"
      continue
    fi
    local verdict=0 receipt_report=""
    receipt_report="$(check_receipt "$specout/receipt.json")" || verdict=$?
    [[ -n "$receipt_report" ]] && printf '%s\n' "$receipt_report" | sed 's/^/    /'
    if [[ $verdict -ne 0 ]]; then
      bad "$base: expectation mismatch (see above)"
    fi
  done
}

run_dafny() {
  fetch_dafny
  local z3 dfy
  z3="$(find_z3)"
  export DOTNET_ROOT="${DOTNET_ROOT:-$(dirname "$(readlink -f "$(command -v dotnet)")")}"
  for dfy in "$formal"/dafny/*.dfy; do
    log "dafny: $(basename "$dfy")"
    if ! "$DAFNY_BIN" verify --solver-path "$z3" "$dfy" 2>&1 | sed 's/^/    /' \
        | grep -q "0 errors"; then
      bad "dafny verification failed for $(basename "$dfy")"
    fi
  done
}

check_citations() {
  # Anti-drift: every formal/ path named in FORMAL_METHODS.md §2 exists.
  local missing=0 path
  while IFS= read -r path; do
    if [[ ! -f "$root/$path" ]]; then
      bad "FORMAL_METHODS.md cites $path but it does not exist"
      missing=1
    fi
  done < <(grep -oE 'formal/(alloy|dafny|tla|fstar)/[A-Za-z0-9._-]+' "$root/FORMAL_METHODS.md" | sort -u)
  [[ $missing -eq 0 ]] && log "citation gate: every cited formal artifact exists"
}

case "$mode" in
  alloy) run_alloy ;;
  dafny) run_dafny ;;
  all)   run_alloy; run_dafny; check_citations ;;
  *) echo "usage: $0 [all|alloy|dafny]" >&2; exit 2 ;;
esac

if [[ $fail -ne 0 ]]; then
  bad "FORMAL VERIFICATION FAILED"
  exit 1
fi
log "all formal checks green"
