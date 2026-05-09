#!/usr/bin/env bash
#
# V2 sidecar perf-regression gate — runs the canary, captures the
# resulting bench JSON, persists into a rolling history, and gates
# on per-label statistical outliers (mean + K × std-dev across the
# last N runs).
#
# Soft-skips (exit 0) when Docker / dotnet are unreachable so docs-
# only commits and dev hosts without the canary stack don't hard-
# block.
#
# Usage:
#   sidecar/projection/scripts/perf-gate.sh                       # default: gate
#   BENCH_K_SIGMA=2.0 sidecar/projection/scripts/perf-gate.sh     # tighter
#   BENCH_TOLERANCE=2.0 sidecar/projection/scripts/perf-gate.sh   # loosen flat-tolerance fallback
#   PERF_GATE_RECORD=1 sidecar/projection/scripts/perf-gate.sh    # record new baseline + clear history
#
# Per the iterator-logging-as-first-class-outcome discipline
# (DECISIONS pillar-7-perf-clause): the canary's bench surface IS the
# perf evidence; this gate makes regression detection structural.
#
# Statistical model:
#   - Each run appends a JSON snapshot to bench/history-canary.jsonl
#   - History trimmed to the last MAX_HISTORY=20 runs
#   - Per-label threshold = mean + K_SIGMA × std-dev (default K=3.0,
#     a 99.7% one-tailed bound under normal-ish iid samples)
#   - When history < MIN_SAMPLES (default 5), falls back to flat
#     `mean × BENCH_TOLERANCE` (default 1.5×) — the chapter-3.6
#     simple gate, retained as the warm-up phase
#   - Per-label minimum filter: labels with mean < 5 ms skipped (noise)

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
BENCH_DIR="$ROOT/bench"
BASELINE_PATH="$BENCH_DIR/baseline-canary.json"
HISTORY_PATH="$BENCH_DIR/history-canary.jsonl"
FIXTURE="$ROOT/fixtures/canary-gate.sql"
CLI_DLL="$ROOT/src/Projection.Cli/bin/Release/net9.0/projection.dll"
TOLERANCE="${BENCH_TOLERANCE:-1.5}"
K_SIGMA="${BENCH_K_SIGMA:-3.0}"
RECORD="${PERF_GATE_RECORD:-0}"
MAX_HISTORY="${BENCH_MAX_HISTORY:-20}"
MIN_SAMPLES="${BENCH_MIN_SAMPLES:-5}"
MIN_MS="${BENCH_MIN_MS:-5}"

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
# Record-mode: write baseline, clear history, exit
# ---------------------------------------------------------------------

if [[ "$RECORD" == "1" ]]; then
    mkdir -p "$BENCH_DIR"
    cp "$LATEST_SNAPSHOT" "$BASELINE_PATH"
    : > "$HISTORY_PATH"
    log "RECORDED baseline → $BASELINE_PATH"
    log "RESET history    → $HISTORY_PATH (cleared)"
    log "Commit baseline-canary.json + (optional) history-canary.jsonl"
    log "when satisfied with the perf floor."
    exit 0
fi

# ---------------------------------------------------------------------
# Append to history + statistical gate
# ---------------------------------------------------------------------

mkdir -p "$BENCH_DIR"

python3 - "$LATEST_SNAPSHOT" "$HISTORY_PATH" "$BASELINE_PATH" \
        "$MAX_HISTORY" "$MIN_SAMPLES" "$MIN_MS" "$K_SIGMA" "$TOLERANCE" <<'PYEOF'
import json
import math
import sys

(latest_path, history_path, baseline_path,
 max_history, min_samples, min_ms, k_sigma, tolerance) = sys.argv[1:]
max_history = int(max_history)
min_samples = int(min_samples)
min_ms = float(min_ms)
k_sigma = float(k_sigma)
tolerance = float(tolerance)


def load_run(path):
    with open(path) as f:
        run = json.load(f)
    return {s["Label"]: s["TotalMs"] for s in run["Stats"]}


def load_history(path):
    runs = []
    try:
        with open(path) as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue
                runs.append(json.loads(line))
    except FileNotFoundError:
        pass
    return runs


def append_history(path, snapshot, max_history):
    history = load_history(path)
    history.append(snapshot)
    history = history[-max_history:]
    with open(path, "w") as f:
        for snap in history:
            f.write(json.dumps(snap, separators=(",", ":")))
            f.write("\n")
    return history


def per_label_stats(history):
    """Return {label: (mean, std_dev, n)} across history."""
    samples = {}
    for snap in history:
        for label, ms in snap.items():
            samples.setdefault(label, []).append(ms)
    stats = {}
    for label, values in samples.items():
        n = len(values)
        mean = sum(values) / n
        if n > 1:
            variance = sum((v - mean) ** 2 for v in values) / (n - 1)
            std = math.sqrt(variance)
        else:
            std = 0.0
        stats[label] = (mean, std, n)
    return stats


# Load latest + append to history.
latest = load_run(latest_path)
history = append_history(history_path, latest, max_history)

print(f"perf-gate: history depth = {len(history)} (max {max_history})")

stats = per_label_stats(history[:-1])  # baseline = history before this run

# Two modes:
#   warm-up (n < min_samples): flat-tolerance vs running mean
#   statistical (n >= min_samples): mean + k_sigma * std
def threshold_flat(mean):
    return mean * tolerance


def threshold_sigma(mean, std):
    return mean + k_sigma * std


regressions = []
warmup_label_count = 0
gated_label_count = 0
new_labels = []

for label, latest_ms in sorted(latest.items()):
    if label not in stats:
        new_labels.append((label, latest_ms))
        continue
    mean, std, n = stats[label]
    if mean < min_ms:
        continue  # noise filter
    if n < min_samples:
        warmup_label_count += 1
        thresh = threshold_flat(mean)
        mode = f"warmup×{tolerance}"
    else:
        gated_label_count += 1
        thresh = threshold_sigma(mean, std)
        mode = f"μ+{k_sigma}σ"
    if latest_ms > thresh:
        regressions.append((label, mean, std, latest_ms, thresh, mode))

if regressions:
    print()
    print(f"REGRESSION in {len(regressions)} label(s):")
    print(f"  {'Label':50s} {'Mean':>8s} {'StdDev':>8s} {'Thresh':>8s} {'Latest':>8s} {'Mode':>14s}")
    for label, mean, std, latest_ms, thresh, mode in regressions:
        print(f"  {label:50s} {mean:8.0f} {std:8.0f} {thresh:8.0f} {latest_ms:8d} {mode:>14s}")
    sys.exit(1)

print(f"perf-gate: clean — {gated_label_count} sigma-gated labels, "
      f"{warmup_label_count} warm-up labels (need {min_samples} samples), "
      f"{len(new_labels)} new labels (will join history next run)")

if new_labels:
    print(f"  new this run: {len(new_labels)} label(s)")
    for label, ms in new_labels[:5]:
        print(f"    + {label}  ({ms} ms)")

sys.exit(0)
PYEOF
PYTHON_EXIT=$?

if [[ "$PYTHON_EXIT" -ne 0 ]]; then
    log "perf-gate FAILED — see regressions above"
    exit 1
fi

log "perf-gate clean"
exit 0
