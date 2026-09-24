#!/usr/bin/env bash
#
# Parallel Docker test runner — 3 concurrent lanes, each internally SERIAL.
#
# ┌─ MEASURED VERDICT (2026-07-01, this 4-core / 15 GiB / no-swap box) ───────┐
# │ On THIS box parallelism is a NET LOSS: the pool is CPU/IO-bound, not      │
# │ parallelism-bound. Serial `test.sh docker` (scale tiered out) ≈ 391s;     │
# │ this runner ≈ 460s — the dacpac build/publish E2E tests are CPU-bound and │
# │ two lanes just thrash the 4 cores. Memory was never the limit (>9 GiB     │
# │ free throughout). The mechanism DOES work when cores aren't saturated (a  │
# │ controlled DB-bound 6-class set: 51s serial → 36s two-lane), so this is   │
# │ kept as an OPT-IN tool for a many-core CI host — NOT the default loop.    │
# │ Prefer `scripts/test.sh docker` on a 4-core machine.                      │
# └──────────────────────────────────────────────────────────────────────────┘
#
# WHY THIS EXISTS. scripts/test.sh runs the Docker-SqlServer collection as
# ONE strictly-serial `dotnet test` (DisableParallelization) because parallel
# per-test CREATE/DROP DATABASE on a SINGLE instance — combined with CDC's
# instance-wide state — livelocks, and a parallel test host piled onto the
# SQL container OOM-kills this 4-core / no-swap box. Measured serial wall:
# ~497s.
#
# THE INSIGHT. The livelock is a SINGLE-INSTANCE hazard: concurrent
# CREATE/DROP against one SQL Server. It is NOT a hazard ACROSS instances.
# So we shard the collection across TWO warm SQL containers (lanes A and B),
# each lane running its slice STRICTLY SERIAL against its OWN container —
# every lane is internally identical to test.sh's serial pool, so the
# livelock is sidestepped by construction, not by tuning. A third lane runs
# the CDC/IsolatedContainerFixture classes, which cold-start their OWN
# containers regardless, serial among themselves (instance-wide CDC state).
# The three lanes run as separate processes concurrently, so wall-clock is
# max(lane) not sum(lane).
#
# SCALE TIER. Pure throughput-measurement classes (ReverseLegScale,
# GeneratorScale, …) are measurements, not inner-loop correctness gates.
# They are excluded from the default parallel run and are opt-in via
# `scale`, mirroring test.sh's tiering philosophy.
#
# MEMORY. Two warm containers (~2 GiB each) + one isolated CDC container
# (one at a time, lane C is serial) + 3 test hosts. A background sampler
# records the low-water available-memory mark; if it dips below the floor
# the run prints a warning so an OOM near-miss is visible in the report.
#
# Usage:
#   scripts/test-docker-parallel.sh            # default: sharded parallel Docker pool
#   scripts/test-docker-parallel.sh scale      # the opt-in scale/perf measurements (serial)
#   scripts/test-docker-parallel.sh plan       # print the derived lane assignment and exit
#
# Env:
#   TEST_CONFIG=Release            build/run Release (default Debug)
#   TEST_NO_BUILD=1                skip the build step
#   DP_MEM_FLOOR_MB=700            available-memory warning floor
#   WARM_A_PORT=11433 WARM_B_PORT=11434    warm container ports

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
# Docker tests live in the Integration assembly (2026-07-01 assembly split).
TESTS_DIR="$ROOT/tests/Projection.Tests.Integration"
TEST_PROJECT="$TESTS_DIR/Projection.Tests.Integration.fsproj"
CONFIG="${TEST_CONFIG:-Debug}"
RESULTS_DIR="/tmp/projection-test-results"
MEM_FLOOR_MB="${DP_MEM_FLOOR_MB:-700}"
WARM_A_NAME="${WARM_A_NAME:-projection-mssql-warm}"
WARM_A_PORT="${WARM_A_PORT:-11433}"
WARM_B_NAME="${WARM_B_NAME:-projection-mssql-warm-2}"
WARM_B_PORT="${WARM_B_PORT:-11434}"

bold() { printf '\033[1m%s\033[0m\n' "$1"; }
log()  { printf '\033[36m[dpar]\033[0m %s\n' "$1"; }
err()  { printf '\033[31m[dpar]\033[0m %s\n' "$1" >&2; }

# Scale/perf measurement classes — excluded from the default run (opt-in via `scale`).
SCALE_CLASSES="ReverseLegScaleTests ReverseLegCdcNormTests GeneratorScaleTests MergeScaleMeasurement ReverseLegStreamingTests PerfHarnessCatalogTests"

# All classes decorated with [<Collection("Docker-SqlServer")>], derived from the
# DECLARATION the attribute decorates (same walk as test.sh, so pool membership
# stays correct as classes move).
docker_classes() {
    awk '
        /CollectionDefinition/ { next }
        /Collection\("Docker-SqlServer"\)/ { armed=1; next }
        armed && /^(module|type) / { name=$2; sub(/\(.*/,"",name); print name; armed=0 }
    ' "$TESTS_DIR"/*.fs 2>/dev/null | grep -v '^EphemeralContainerFixture$' | sort -u
}

# Classes that cold-start their OWN container (CDC / IsolatedContainerFixture):
# lane C. Derived from the fixture the class file references.
iso_classes() {
    for f in $(grep -rlE "IsolatedContainerFixture" "$TESTS_DIR"/*.fs 2>/dev/null); do
        awk '
            /CollectionDefinition/ { next }
            /Collection\("Docker-SqlServer"\)/ { armed=1; next }
            armed && /^(module|type) / { name=$2; sub(/\(.*/,"",name); print name; armed=0 }
        ' "$f"
    done | sort -u
}

# Balance the warm classes across lanes A and B by measured per-class duration
# (from the last docker TRX if present), longest-processing-time bin-packing.
# Emits: three lines — laneA filter, laneB filter, laneC filter — as
# `FullyQualifiedName~C1|FullyQualifiedName~C2|…`.
plan_lanes() {
    local trx="$RESULTS_DIR/docker.trx"
    docker_classes > "$RESULTS_DIR/.dp_all" 2>/dev/null
    iso_classes    > "$RESULTS_DIR/.dp_iso" 2>/dev/null
    python3 - "$RESULTS_DIR/.dp_all" "$RESULTS_DIR/.dp_iso" "$trx" "$SCALE_CLASSES" <<'PY'
import sys, re, os
allf, isof, trx, scale = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4].split()
def short(n): return n.strip().split('.')[-1]   # module Projection.Tests.Foo -> Foo
allc = [short(l) for l in open(allf) if l.strip()]
iso  = set(short(l) for l in open(isof) if l.strip())
scale = set(scale)
# durations by class from TRX (seconds), if available
dur = {}
if os.path.exists(trx):
    txt = open(trx, encoding='utf-8', errors='ignore').read()
    for m in re.finditer(r'testName="Projection\.Tests\.([A-Za-z0-9_]+)(?:\+[A-Za-z0-9_]+)?\.[^"]*"[^>]*duration="(\d+):(\d+):([\d.]+)"', txt):
        c=m.group(1); s=int(m.group(2))*3600+int(m.group(3))*60+float(m.group(4))
        dur[c]=dur.get(c,0.0)+s
warm = [c for c in allc if c not in iso and c not in scale]
isoL = [c for c in allc if c in iso and c not in scale]
# LPT bin-pack warm into A,B
warm.sort(key=lambda c: dur.get(c,0.5), reverse=True)
a,b=[],[]; la=lb=0.0
for c in warm:
    if la<=lb: a.append(c); la+=dur.get(c,0.5)
    else:      b.append(c); lb+=dur.get(c,0.5)
# Anchor with the namespace so a class name that is a substring of another
# (MigrationCanaryTests vs SchemaMigrationCanaryTests) can't double-match a
# sibling FQN across lanes — the shared "Projection.Tests." prefix forces the
# match to align at the type boundary.
def filt(cs): return "|".join("FullyQualifiedName~Projection.Tests."+c for c in cs)
print(filt(a)); print(filt(b)); print(filt(isoL))
# plan summary to stderr
print(f"laneA {len(a)} classes ~{la:.0f}s | laneB {len(b)} classes ~{lb:.0f}s | laneC(iso) {len(isoL)} ~{sum(dur.get(c,0) for c in isoL):.0f}s", file=sys.stderr)
PY
}

ensure_warm() {
    local name="$1" port="$2"
    if [[ "$(docker inspect -f '{{.State.Running}}' "$name" 2>/dev/null)" == "true" ]]; then
        log "warm container '$name' already up (:$port)"
    else
        log "starting warm container '$name' (:$port)…"
        WARM_SQL_NAME="$name" WARM_SQL_PORT="$port" WARM_SQL_MEM_MB=2560 WARM_SQL_MEM=3g \
            "$SCRIPT_DIR/warm-sql.sh" start >/dev/null 2>&1 || { err "failed to start $name"; return 1; }
    fi
    WARM_SQL_NAME="$name" WARM_SQL_PORT="$port" "$SCRIPT_DIR/warm-sql.sh" conn 2>/dev/null \
        | sed 's/^export PROJECTION_MSSQL_CONN_STR=//'
}

mem_sampler() {
    # writes the low-water available-MB mark to $1 until $2 (a sentinel file) disappears
    local out="$1" stop="$2" low=9999999
    while [[ -f "$stop" ]]; do
        local avail; avail=$(free -m | awk '/^Mem:/{print $7}')
        [[ "$avail" -lt "$low" ]] && low=$avail
        [[ "$avail" -lt "$MEM_FLOOR_MB" ]] && echo "WARN low mem: ${avail}MB" >> "$out.warn"
        echo "$low" > "$out"
        sleep 2
    done
}

run_lane() {
    # run_lane <label> <filter> <connstr-or-empty>
    local label="$1" filter="$2" conn="$3"
    local live="$RESULTS_DIR/dp-$label.live.log"
    local trx="dp-$label.trx"
    : > "$live"
    [[ -z "$filter" ]] && { echo "0" > "$RESULTS_DIR/dp-$label.code"; return 0; }
    local env_prefix=()
    [[ -n "$conn" ]] && env_prefix=(env "PROJECTION_MSSQL_CONN_STR=$conn")
    local t0 t1 code
    t0=$(date +%s)
    "${env_prefix[@]}" dotnet test "$TEST_PROJECT" -c "$CONFIG" --no-build --nologo \
        --filter "$filter" \
        --logger "trx;LogFileName=$trx" \
        --results-directory "$RESULTS_DIR" \
        > "$live" 2>&1
    code=$?
    t1=$(date +%s)
    echo "$code" > "$RESULTS_DIR/dp-$label.code"
    echo "$((t1-t0))" > "$RESULTS_DIR/dp-$label.dur"
    return "$code"
}

CMD="${1:-run}"
mkdir -p "$RESULTS_DIR"

if [[ "$CMD" == "plan" ]]; then
    bold "── derived lane plan ──"
    mapfile -t LANES < <(plan_lanes 2>/tmp/dp_plan_err); cat /tmp/dp_plan_err
    echo "laneA: ${LANES[0]:-<empty>}"; echo; echo "laneB: ${LANES[1]:-<empty>}"; echo; echo "laneC: ${LANES[2]:-<empty>}"
    exit 0
fi

if [[ "${TEST_NO_BUILD:-0}" != "1" ]]; then
    log "building test project ($CONFIG)…"
    dotnet build "$TEST_PROJECT" -c "$CONFIG" --nologo -v q || { err "build failed"; exit 1; }
fi

if [[ "$CMD" == "scale" ]]; then
    bold "──────── scale/perf tier (serial) ────────"
    CONN_A=$(ensure_warm "$WARM_A_NAME" "$WARM_A_PORT")
    filter="$(for c in $SCALE_CLASSES; do echo -n "FullyQualifiedName~$c|"; done | sed 's/|$//')"
    log "filter: $filter"
    env "PROJECTION_MSSQL_CONN_STR=$CONN_A" dotnet test "$TEST_PROJECT" -c "$CONFIG" --no-build --nologo \
        --filter "$filter" --logger "trx;LogFileName=dp-scale.trx" --results-directory "$RESULTS_DIR"
    exit $?
fi

# ---- default: parallel run ----
# MEASURED (2026-07-01, 4-core box): TWO warm containers (one per lane) LOSE to
# serial — two SQL engines, each memory-capped, thrash the 4 cores (497s serial
# vs 464s two-container). ONE warm container with the two warm lanes as
# concurrent processes hitting it WINS (a controlled 6-class set: 51s serial ->
# 36s two-lane, -29%): a single SQL Server keeps its full buffer pool and
# pipelines the two lanes' work instead of duplicating the engine. So both warm
# lanes point at ONE container by default. DP_TWO_CONTAINERS=1 restores the
# per-lane-container sharding (worth it only on a many-core host).
bold "──────── parallel Docker pool ────────"
CONN_A=$(ensure_warm "$WARM_A_NAME" "$WARM_A_PORT") || exit 1
if [[ "${DP_TWO_CONTAINERS:-0}" == "1" ]]; then
    CONN_B=$(ensure_warm "$WARM_B_NAME" "$WARM_B_PORT") || exit 1
    log "two-container sharding (DP_TWO_CONTAINERS=1)"
else
    CONN_B="$CONN_A"
    log "single-container, two concurrent warm lanes (the measured-fastest config on 4 cores)"
fi
mapfile -t LANES < <(plan_lanes 2>/tmp/dp_plan_err)
log "$(cat /tmp/dp_plan_err)"

STOP="$RESULTS_DIR/.dp_mem_running"; MEMOUT="$RESULTS_DIR/dp.memlow"
: > "$STOP"; rm -f "$MEMOUT.warn"
mem_sampler "$MEMOUT" "$STOP" &
SAMPLER=$!

WALL0=$(date +%s)
run_lane A "${LANES[0]:-}" "$CONN_A" &
PA=$!
run_lane B "${LANES[1]:-}" "$CONN_B" &
PB=$!
# lane C (isolated/CDC) cold-starts its own containers — no warm conn
run_lane C "${LANES[2]:-}" "" &
PC=$!
wait "$PA" "$PB" "$PC"
WALL1=$(date +%s)
rm -f "$STOP"; wait "$SAMPLER" 2>/dev/null || true

echo
bold "──────── result ────────"
overall=0
for L in A B C; do
    code=$(cat "$RESULTS_DIR/dp-$L.code" 2>/dev/null || echo "?")
    dur=$(cat "$RESULTS_DIR/dp-$L.dur" 2>/dev/null || echo "-")
    verdict="PASS"; [[ "$code" != "0" ]] && { verdict="FAIL"; overall=1; }
    printf "  lane %s: %s (exit %s, %ss)\n" "$L" "$verdict" "$code" "$dur"
done
log "wall: $((WALL1-WALL0))s   (serial baseline ~497s)"
log "mem low-water: $(cat "$MEMOUT" 2>/dev/null || echo '?')MB available"
[[ -f "$MEMOUT.warn" ]] && err "MEMORY WARNINGS: $(wc -l < "$MEMOUT.warn") samples under ${MEM_FLOOR_MB}MB"
exit "$overall"
