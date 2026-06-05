#!/usr/bin/env bash
#
# V2 sidecar perf-regression gate — runs the operator-reality canary
# (6.25k rows × 150 tables × variegated, the production-shape baseline),
# captures the resulting bench JSON, and gates per-label TotalMs
# against a tracked μ+σ statistical baseline.
#
# Two tiers (first-principles fix, 2026-06-05). The HARD gate (can fail the
# run) covers what V2's CODE controls: the deterministic count/size labels
# (`.elements` / `.batchSize` / `.bytes` — a count change is a real volume
# regression) and CPU-bound times. The SOFT tier (reported, never fails) covers
# I/O-bound wall-times (Docker container acquisition, SQL deploy / read
# round-trips — `PERF_GATE_IO_TIME_PREFIXES`): on a shared / no-swap host these
# vary >2× with load and were false-tripping the gate while the canary itself
# stayed GREEN. The canary's GREEN/RED fidelity verdict is untouched — only the
# per-label TIMING gate is tiered.
#
# Operator decision (2026-05-09): schema-only canary-gate.sql is
# inappropriate for the production-use-case baseline. The Stop hook +
# pre-commit gate must exercise the production envelope (150 tables,
# 6.25k rows, variegated FK density; tuned down 2026-05-20 from
# 300 tables × 50k rows to reduce agent-loop friction while preserving
# the FK-density envelope at the smaller scale — see DECISIONS
# 2026-05-20 (canary volume reduction)) so feature additions can't
# silently regress under operator-reality conditions.
#
# Soft-skips (exit 0) when Docker / dotnet are unreachable so docs-
# only commits and dev hosts without the canary stack don't hard-
# block.
#
# Usage:
#   sidecar/projection/scripts/perf-gate.sh                       # default: gate
#   BENCH_K_SIGMA=5.0 sidecar/projection/scripts/perf-gate.sh     # adjust gate width
#   PERF_GATE_RECORD=1 sidecar/projection/scripts/perf-gate.sh    # re-record baseline (N warm runs)
#   PERF_GATE_RECORD=1 BENCH_RECORD_RUNS=10                       # more samples → tighter μ+σ
#
# Per the iterator-logging-as-first-class-outcome discipline
# (DECISIONS pillar-7-perf-clause): the canary's bench surface IS the
# perf evidence; this gate makes regression detection structural.
#
# Statistical model (DECISIONS 2026-05-10 — μ+σ statistical baseline):
#   - The committed `bench/baseline-canary.json` carries per-label
#     `MeanMs` + `StdevMs` + `SampleCount` computed from N≥5 warm
#     captures. The baseline IS the model; there is no rolling
#     history accumulator.
#   - Per-label threshold = MeanMs + K × StdevMs. Default K=5.0 to
#     absorb cross-machine timing variance (CI ↔ dev laptop).
#   - New labels (not in baseline) pass with a soft warning — they
#     join the baseline at the next `PERF_GATE_RECORD=1` cycle.
#   - Per-label minimum filter: baselines with MeanMs < 5 ms skipped
#     (noise) — applies symmetrically at record + gate time.
#   - Re-record the baseline (`PERF_GATE_RECORD=1`) when the perf
#     floor legitimately changes (algorithmic improvement; new
#     workload axis); pair with a DECISIONS amendment naming the
#     new floor's rationale.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
BENCH_DIR="$ROOT/bench"
BASELINE_PATH="$BENCH_DIR/baseline-canary.json"
TEST_FILTER="${PERF_GATE_TEST_FILTER:-FullyQualifiedName~Operator-reality}"
TEST_PROJECT="$ROOT/tests/Projection.Tests/Projection.Tests.fsproj"
K_SIGMA="${BENCH_K_SIGMA:-5.0}"
RECORD="${PERF_GATE_RECORD:-0}"
RECORD_RUNS="${BENCH_RECORD_RUNS:-5}"
MIN_MS="${BENCH_MIN_MS:-5}"
# Min-σ prior: σ_effective = max(σ_observed, μ × MIN_RELATIVE_STDEV).
# Bayesian floor on the σ estimate — at N=5, σ_observed often
# underestimates true population σ (especially for I/O-bound labels
# whose run-to-run variance is dominated by Docker / network jitter).
# Default 0.20 (20% relative σ floor); tighten when many samples
# accumulate and σ_observed is trustworthy.
MIN_RELATIVE_STDEV="${BENCH_MIN_RELATIVE_STDEV:-0.20}"
# I/O-bound TIME-label prefixes (first-principles tiering, 2026-06-05). Wall-time
# labels under these prefixes are dominated by host I/O load, not V2 code — on a
# shared / no-swap host they vary >2× and false-trip the gate. REPORTED, not
# gated. `deploy.` = Docker container acquisition + SQL deploy; `readside.` = the
# WHOLE SQL read-back adapter (all its timing is round-trip-bound); `fixture
# .bulkLoader` = the bulk seed insert. Count/size labels (`.elements`/`.batchSize`
# /`.bytes`) under these prefixes stay HARD-gated — a count change is a real
# volume regression.
IO_TIME_PREFIXES="${PERF_GATE_IO_TIME_PREFIXES:-deploy.,readside.,fixture.bulkLoader}"
# Small TIME labels (μ below this) are dominated by host CPU-SCHEDULING jitter on
# a contended box — a 5 ms op routinely measures 20 ms when 4 cores run builds +
# tests + SQL + the canary at once (I/O or not). REPORTED, not gated. Count/size
# labels ignore this floor (deterministic); substantial-time labels keep the hard
# gate. A perf gate can only reliably catch regressions on work measurable above
# the host's noise floor. (Distinct from MIN_MS, which drops sub-MIN_MS labels
# from the baseline entirely.)
HARD_MIN_MS="${PERF_GATE_HARD_MIN_MS:-50}"

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
# Build the test project if needed
# ---------------------------------------------------------------------

TEST_DLL="$ROOT/tests/Projection.Tests/bin/Release/net9.0/Projection.Tests.dll"
if [[ ! -f "$TEST_DLL" ]]; then
    log "building Projection.Tests (Release)..."
    if ! dotnet build "$TEST_PROJECT" -c Release --nologo >/tmp/perf-gate-build.log 2>&1; then
        log "FATAL: dotnet build failed; see /tmp/perf-gate-build.log"
        exit 1
    fi
fi

# ---------------------------------------------------------------------
# Run the operator-reality canary one time → fresh bench/canary/<utc>.json
# ---------------------------------------------------------------------

run_canary() {
    log "running operator-reality canary (6.25k rows × 150 tables, variegated)..."
    cd "$ROOT"
    set +e
    PROJECTION_BENCH_DIR="$ROOT" dotnet test "$TEST_PROJECT" \
        -c Release --no-build \
        --filter "$TEST_FILTER" \
        --logger "console;verbosity=normal" \
        >/tmp/perf-gate-canary.log 2>&1
    local exit_code=$?
    set -e

    if [[ "$exit_code" -ne 0 ]]; then
        log "FATAL: operator-reality canary failed with exit $exit_code"
        log "       see /tmp/perf-gate-canary.log"
        tail -25 /tmp/perf-gate-canary.log >&2
        exit 1
    fi

    if grep -q "SKIP operator-reality canary" /tmp/perf-gate-canary.log; then
        log "SKIP: Docker daemon not reachable inside test process"
        exit 0
    fi

    local snapshot
    snapshot="$(ls -1t "$ROOT/bench/canary"/*.json 2>/dev/null | head -n1 || true)"

    if [[ -z "$snapshot" ]]; then
        log "FATAL: no canary bench snapshot found under $ROOT/bench/canary/"
        log "       (check that BenchSink.persistJson ran)"
        exit 1
    fi

    log "operator-reality canary GREEN — $snapshot"
    printf '%s' "$snapshot"
}

# ---------------------------------------------------------------------
# Record-mode: capture N warm runs, aggregate per-label μ+σ, write baseline
# ---------------------------------------------------------------------

if [[ "$RECORD" == "1" ]]; then
    log "RECORD mode — capturing $RECORD_RUNS warm runs to seed the μ+σ baseline"
    SNAPSHOTS=()
    for i in $(seq 1 "$RECORD_RUNS"); do
        log "  capture $i/$RECORD_RUNS..."
        snap="$(run_canary)"
        SNAPSHOTS+=("$snap")
    done

    mkdir -p "$BENCH_DIR"

    python3 - "$BASELINE_PATH" "$MIN_MS" "${SNAPSHOTS[@]}" <<'PYEOF'
import json
import math
import sys
import datetime

baseline_path = sys.argv[1]
min_ms = float(sys.argv[2])
snapshot_paths = sys.argv[3:]

# Per-label samples: { label : [TotalMs, ...] }
samples: dict[str, list[float]] = {}
for path in snapshot_paths:
    with open(path) as f:
        run = json.load(f)
    for stat in run["Stats"]:
        samples.setdefault(stat["Label"], []).append(float(stat["TotalMs"]))

# Aggregate per-label μ+σ. SampleCount is per-label (a label may be
# absent from one snapshot if its code path didn't fire that run).
stats = []
for label in sorted(samples):
    values = samples[label]
    n = len(values)
    mean = sum(values) / n
    if mean < min_ms:
        continue  # noise filter
    if n > 1:
        variance = sum((v - mean) ** 2 for v in values) / (n - 1)
        stdev = math.sqrt(variance)
    else:
        stdev = 0.0
    stats.append({
        "Label":       label,
        "SampleCount": n,
        "MeanMs":      round(mean, 1),
        "StdevMs":     round(stdev, 1),
    })

baseline = {
    "RecordedAtUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z"),
    "Tag":           "operatorReality",
    "Runs":          len(snapshot_paths),
    "Stats":         stats,
}

with open(baseline_path, "w") as f:
    json.dump(baseline, f, indent=2)
    f.write("\n")

print(f"perf-gate: recorded baseline → {baseline_path}")
print(f"perf-gate: {len(stats)} labels × {len(snapshot_paths)} runs")
print(f"perf-gate: top-5 by MeanMs:")
for s in sorted(stats, key=lambda s: -s["MeanMs"])[:5]:
    print(f"  {s['Label']:60s}  μ={s['MeanMs']:8.1f}ms  σ={s['StdevMs']:8.1f}ms  n={s['SampleCount']}")
PYEOF

    log "RECORD complete. Commit baseline-canary.json with a DECISIONS"
    log "amendment naming why the floor changed."
    exit 0
fi

# ---------------------------------------------------------------------
# Default mode: one canary run, gate against committed baseline
# ---------------------------------------------------------------------

LATEST_SNAPSHOT="$(run_canary)"

if [[ ! -f "$BASELINE_PATH" ]]; then
    log "WARN: no baseline at $BASELINE_PATH"
    log "      run \`PERF_GATE_RECORD=1\` to seed it. Skipping gate."
    exit 0
fi

python3 - "$LATEST_SNAPSHOT" "$BASELINE_PATH" "$K_SIGMA" "$MIN_MS" "$MIN_RELATIVE_STDEV" "$IO_TIME_PREFIXES" "$HARD_MIN_MS" <<'PYEOF'
import json
import sys

(latest_path, baseline_path, k_sigma_str, min_ms_str, min_rel_stdev_str, io_prefixes_str, hard_min_ms_str) = sys.argv[1:]
k_sigma = float(k_sigma_str)
min_ms = float(min_ms_str)
min_rel_stdev = float(min_rel_stdev_str)
hard_min_ms = float(hard_min_ms_str)

# Tiering: I/O-bound TIME labels are reported but never fail the gate (host-load
# noise). Count/size labels (deterministic) and CPU-bound times keep the hard
# gate. A count label under an I/O prefix (e.g. deploy.*.elements) is still a
# COUNT, so it stays hard-gated — the suffix check wins.
IO_TIME_PREFIXES = tuple(p for p in io_prefixes_str.split(",") if p)
COUNT_SUFFIXES = (".elements", ".batchSize", ".bytes")


def is_count(label):
    return label.endswith(COUNT_SUFFIXES)


def is_io_time(label):
    return (not is_count(label)) and label.startswith(IO_TIME_PREFIXES)


def load_run(path):
    with open(path) as f:
        run = json.load(f)
    return {s["Label"]: float(s["TotalMs"]) for s in run["Stats"]}


def load_baseline(path):
    with open(path) as f:
        b = json.load(f)
    return {
        s["Label"]: (float(s["MeanMs"]), float(s["StdevMs"]), int(s["SampleCount"]))
        for s in b["Stats"]
    }


def effective_stdev(mean: float, stdev_observed: float, min_rel: float) -> float:
    """Bayesian floor on σ. At N=5, σ_observed often underestimates
    true population σ. Treat σ as at least `mean × min_rel` (default 20%)."""
    return max(stdev_observed, mean * min_rel)


latest = load_run(latest_path)
baseline = load_baseline(baseline_path)

regressions = []   # hard — V2 code controls these (counts + CPU times)
io_overruns = []   # soft — Docker/SQL wall-time, host-load-variable
new_labels = []
checked = 0

for label, latest_ms in sorted(latest.items()):
    if label not in baseline:
        new_labels.append((label, latest_ms))
        continue
    mean, stdev_obs, n = baseline[label]
    if mean < min_ms:
        continue
    stdev = effective_stdev(mean, stdev_obs, min_rel_stdev)
    threshold = mean + k_sigma * stdev
    checked += 1
    if latest_ms > threshold:
        row = (label, mean, stdev_obs, stdev, n, latest_ms, threshold)
        # Soft (reported, not gated) iff a TIME label that is either I/O-bound or
        # small enough to be host-contention noise. Counts are never soft.
        soft = (not is_count(label)) and (is_io_time(label) or mean < hard_min_ms)
        (io_overruns if soft else regressions).append(row)


def print_table(rows):
    print(f"  {'Label':50s} {'N':>3s} {'Mean':>9s} {'σ(obs)':>8s} {'σ(eff)':>8s} {'Threshold':>10s} {'Latest':>10s}")
    for label, mean, stdev_obs, stdev_eff, n, latest_ms, threshold in rows:
        print(f"  {label:50s} {n:>3d} {mean:>9.1f} {stdev_obs:>8.1f} {stdev_eff:>8.1f} {threshold:>10.1f} {latest_ms:>10.1f}")


# I/O-bound overruns: reported, never failing (host-load noise, not a code regression).
if io_overruns:
    print()
    print(f"{len(io_overruns)} label(s) over threshold but NOT gated "
          f"(I/O wall-time or sub-{hard_min_ms:.0f}ms — host I/O / CPU-contention noise):")
    print_table(io_overruns)

if regressions:
    print()
    print(f"REGRESSION in {len(regressions)} code-controlled label(s) (gate = μ + {k_sigma}σ_effective; "
          f"σ_effective = max(σ, μ×{min_rel_stdev})):")
    print_table(regressions)
    print()
    print("If this is an intended floor shift, re-record the baseline:")
    print("  PERF_GATE_RECORD=1 sidecar/projection/scripts/perf-gate.sh")
    print("Pair with a DECISIONS amendment naming the new floor's rationale.")
    sys.exit(1)

print(f"perf-gate: clean — {checked} labels gated against μ+{k_sigma}σ_eff baseline (σ floor = μ×{min_rel_stdev}); "
      f"{len(io_overruns)} over-threshold-but-soft (I/O / small-op noise); {len(new_labels)} new label(s)")
if new_labels:
    print("  new this run:")
    for label, ms in new_labels[:5]:
        print(f"    + {label}  ({ms:.1f} ms)")
    if len(new_labels) > 5:
        print(f"    + … {len(new_labels) - 5} more")
sys.exit(0)
PYEOF

log "perf-gate clean"
exit 0
