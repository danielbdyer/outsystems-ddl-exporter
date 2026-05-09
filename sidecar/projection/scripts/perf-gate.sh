#!/usr/bin/env bash
#
# V2 sidecar perf-regression gate — runs the canary, captures the
# resulting bench JSON, and compares against the committed baseline.
# Fails (exit 1) if any per-label `TotalMs` exceeds the baseline value
# by more than `BENCH_TOLERANCE` (default `1.5` ×). Soft-skips (exit 0)
# when Docker is unreachable so CI / pre-commit on Docker-less hosts
# don't hard-block.
#
# Usage:
#   sidecar/projection/scripts/perf-gate.sh                     # default canary-gate.sql, default tolerance
#   BENCH_TOLERANCE=2.0 sidecar/projection/scripts/perf-gate.sh # looser
#   PERF_GATE_RECORD=1 sidecar/projection/scripts/perf-gate.sh  # record new baseline (don't gate)
#
# The baseline lives at sidecar/projection/bench/baseline-canary.json
# and is committed to the repo. Update via PERF_GATE_RECORD=1 + commit
# the resulting JSON when the codebase legitimately changes the perf
# floor (e.g., a new pass adds work; a new emitter expands per-kind
# emission).
#
# Per the iterator-logging-as-first-class-outcome discipline
# (DECISIONS / CLAUDE.md): the canary's bench surface IS the perf
# evidence; this gate makes regression detection structural rather
# than aspirational.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
BENCH_DIR="$ROOT/bench"
BASELINE_PATH="$BENCH_DIR/baseline-canary.json"
FIXTURE="$ROOT/fixtures/canary-gate.sql"
CLI_DLL="$ROOT/src/Projection.Cli/bin/Release/net9.0/projection.dll"
TOLERANCE="${BENCH_TOLERANCE:-1.5}"
RECORD="${PERF_GATE_RECORD:-0}"

log() { printf '[perf-gate] %s\n' "$*" >&2; }

# ---------------------------------------------------------------------
# Soft-skip conditions
# ---------------------------------------------------------------------

if ! command -v docker >/dev/null 2>&1; then
    log "SKIP: docker not installed"
    exit 0
fi

if ! docker info >/dev/null 2>&1; then
    log "SKIP: docker daemon not reachable"
    exit 0
fi

if ! command -v dotnet >/dev/null 2>&1; then
    log "SKIP: dotnet not on PATH (check session-start hook)"
    exit 0
fi

# ---------------------------------------------------------------------
# Build the CLI if needed
# ---------------------------------------------------------------------

if [[ ! -f "$CLI_DLL" ]]; then
    log "building Projection.Cli (Release)..."
    if ! dotnet build "$ROOT/src/Projection.Cli" -c Release --nologo >/tmp/perf-gate-build.log 2>&1; then
        log "FATAL: dotnet build failed; see /tmp/perf-gate-build.log"
        exit 1
    fi
fi

# ---------------------------------------------------------------------
# Run the canary
# ---------------------------------------------------------------------

if [[ ! -f "$FIXTURE" ]]; then
    log "FATAL: canary fixture missing at $FIXTURE"
    exit 1
fi

log "running canary against $(basename "$FIXTURE")..."
cd "$ROOT"
set +e
dotnet "$CLI_DLL" canary "$FIXTURE" >/tmp/perf-gate-canary.log 2>&1
CANARY_EXIT=$?
set -e

case "$CANARY_EXIT" in
    0)
        log "canary GREEN"
        ;;
    5)
        log "FATAL: canary RED — PhysicalSchema diff non-empty"
        log "       see /tmp/perf-gate-canary.log"
        tail -25 /tmp/perf-gate-canary.log >&2
        exit 1
        ;;
    *)
        log "FATAL: canary failed with exit $CANARY_EXIT"
        log "       see /tmp/perf-gate-canary.log"
        tail -10 /tmp/perf-gate-canary.log >&2
        exit 1
        ;;
esac

# ---------------------------------------------------------------------
# Locate the most recent canary bench snapshot
# ---------------------------------------------------------------------

LATEST_SNAPSHOT="$(ls -1t "$ROOT/bench/canary"/*.json 2>/dev/null | head -n1 || true)"

if [[ -z "$LATEST_SNAPSHOT" ]]; then
    log "FATAL: no canary bench snapshot found under $ROOT/bench/canary/"
    log "       (check that BenchSink.persistJson ran)"
    exit 1
fi

log "snapshot: $LATEST_SNAPSHOT"

# ---------------------------------------------------------------------
# Record-mode: write baseline and exit
# ---------------------------------------------------------------------

if [[ "$RECORD" == "1" ]]; then
    mkdir -p "$BENCH_DIR"
    cp "$LATEST_SNAPSHOT" "$BASELINE_PATH"
    log "RECORDED baseline → $BASELINE_PATH"
    log "Commit it to the repo when satisfied with the perf floor."
    exit 0
fi

# ---------------------------------------------------------------------
# Compare against baseline
# ---------------------------------------------------------------------

if [[ ! -f "$BASELINE_PATH" ]]; then
    log "no baseline at $BASELINE_PATH — recording first run"
    log "(re-run with PERF_GATE_RECORD=1 to seed; commit the file)"
    log "skipping comparison; exit 0"
    exit 0
fi

# Compare per-label TotalMs via Python (jq might not be installed everywhere).
python3 - "$BASELINE_PATH" "$LATEST_SNAPSHOT" "$TOLERANCE" <<'PYEOF'
import json
import sys

baseline_path, latest_path, tolerance = sys.argv[1], sys.argv[2], float(sys.argv[3])

def load(path):
    with open(path) as f:
        run = json.load(f)
    return {s["Label"]: s["TotalMs"] for s in run["Stats"]}

baseline = load(baseline_path)
latest   = load(latest_path)

regressions = []
new_labels  = []
removed     = []
for label, latest_ms in sorted(latest.items()):
    if label not in baseline:
        new_labels.append((label, latest_ms))
        continue
    base_ms = baseline[label]
    # Skip labels with negligible base time (under 5ms) — noise dominates.
    if base_ms < 5:
        continue
    ratio = latest_ms / max(1, base_ms)
    if ratio > tolerance:
        regressions.append((label, base_ms, latest_ms, ratio))

for label in sorted(set(baseline) - set(latest)):
    removed.append((label, baseline[label]))

if regressions:
    print(f"REGRESSION beyond {tolerance}x in {len(regressions)} label(s):")
    print(f"  {'Label':50s} {'Baseline':>10s} {'Latest':>10s} {'Ratio':>8s}")
    for label, b, l, r in regressions:
        print(f"  {label:50s} {b:10d} {l:10d} {r:7.2f}x")
    sys.exit(1)

if new_labels:
    print(f"NEW labels (not in baseline) — re-run with PERF_GATE_RECORD=1 if intentional:")
    for label, ms in new_labels[:10]:
        print(f"  + {label} ({ms} ms)")

if removed:
    print(f"REMOVED labels (in baseline, not in latest) — likely a refactor:")
    for label, ms in removed[:10]:
        print(f"  - {label} (was {ms} ms)")

print(f"perf-gate: clean ({len(latest)} labels checked; tolerance={tolerance}x)")
sys.exit(0)
PYEOF
PYTHON_EXIT=$?

if [[ "$PYTHON_EXIT" -ne 0 ]]; then
    log "perf-gate FAILED — see regressions above"
    exit 1
fi

log "perf-gate clean"
exit 0
