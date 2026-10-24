#!/usr/bin/env bash
#
# V2 sidecar perf HARNESS — the before/after optimization instrument
# (PERF_HARNESS.md §3.4; sibling of perf-gate.sh, which remains the
# REGRESSION gate — do not confuse the two: the harness is exploratory,
# per-scenario, never gating).
#
#   perf-harness.sh list                  enumerate scenarios (no run)
#   perf-harness.sh run [filter]          run fleet (or filtered subset)
#                                         -> bench/perf/<name>/<utc>.json
#   perf-harness.sh capture before [f]    run + copy snapshots to before.json
#   perf-harness.sh capture after  [f]    same -> after.json
#   perf-harness.sh diff [filter]         per-scenario before/after table
#
# Comparison method (§3.5, RESOLVED): single-run deterministic delta. A
# candidate is confirmed only if its KeyLabels move beyond ~2x run-to-run
# jitter; a scenario whose back-to-back same-code captures disagree >15%
# is NOISY — promote it to N=5 + mu/sigma (reuse perf-gate.sh aggregation)
# rather than trusting single runs. Count drift on a deterministic scenario
# voids the timing delta and is flagged loudly below.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SCEN_FILE="$ROOT/tests/Projection.Tests/PerfHarnessScenarios.fs"
PERF_DIR="$ROOT/bench/perf"

cmd="${1:-}"
filt="${2:-}"

list_scenarios() {
  # Print the full scenario spec (scale alternations like `1000|10000`
  # included — the old `[^|]*` form cut them at the first `|`), stripping
  # only the keylabels suffix. The pure-pool totality test
  # (PerfHarnessCatalogTests.fs) pins these lines against the declared
  # catalog `PerfHarnessScenarios.all` (H7).
  grep '// PERF-SCENARIO: ' "$SCEN_FILE" | sed 's/.*PERF-SCENARIO: //' | sed 's/ *| *keylabels=.*//' | sed 's/ *$//'
}

run_fleet() {
  local f="${1:-}"
  local filter="FullyQualifiedName~PerfHarness"
  if [ -n "$f" ]; then filter="FullyQualifiedName~PerfHarness: $f"; fi
  echo "perf-harness: running filter [$filter]"
  PROJECTION_RUN_PERF_HARNESS=1 PROJECTION_BENCH_DIR="$ROOT" \
    dotnet test "$ROOT/tests/Projection.Tests/Projection.Tests.fsproj" \
      --filter "$filter" --nologo -v minimal
}

latest_snapshot() { ls -1t "$1"/2*.json 2>/dev/null | head -n1 || true; }

capture() {
  local slot="$1" f="${2:-}"
  run_fleet "$f"
  local found=0
  for dir in "$PERF_DIR"/*/; do
    [ -d "$dir" ] || continue
    local name; name="$(basename "$dir")"
    if [ -n "$f" ] && [[ "$name" != *"${f// /-}"* ]]; then continue; fi
    local snap; snap="$(latest_snapshot "$dir")"
    if [ -n "$snap" ]; then
      cp "$snap" "$dir/$slot.json"
      echo "perf-harness: $name $slot <- $(basename "$snap")"
      found=1
    fi
  done
  [ "$found" = "1" ] || { echo "perf-harness: no snapshots captured (filter '$f')" >&2; exit 1; }
}

diff_runs() {
  local f="${1:-}"
  local any=0
  for dir in "$PERF_DIR"/*/; do
    [ -d "$dir" ] || continue
    local name; name="$(basename "$dir")"
    if [ -n "$f" ] && [[ "$name" != *"${f// /-}"* ]]; then continue; fi
    [ -f "$dir/before.json" ] && [ -f "$dir/after.json" ] || continue
    any=1
    python3 - "$dir/before.json" "$dir/after.json" "$name" <<'PY'
import json, sys
b = {s["Label"]: s for s in json.load(open(sys.argv[1]))["Stats"]}
a = {s["Label"]: s for s in json.load(open(sys.argv[2]))["Stats"]}
name = sys.argv[3]
print(f"\n== {name} ==")
print(f"{'label':44} {'before ms':>10} {'after ms':>10} {'delta':>8}  counts")
drift = []
for label in sorted(set(b) | set(a)):
    sb, sa = b.get(label), a.get(label)
    if sb is None or sa is None:
        print(f"{label:44} {'-' if sb is None else sb['TotalMs']:>10} "
              f"{'-' if sa is None else sa['TotalMs']:>10} {'NEW/GONE':>8}")
        continue
    tb, ta = sb["TotalMs"], sa["TotalMs"]
    pct = 0.0 if tb == 0 else (ta - tb) * 100.0 / tb
    counts = f"{sb['Count']}/{sa['Count']}"
    print(f"{label:44} {tb:>10} {ta:>10} {pct:>+7.1f}%  {counts}")
    if sb["Count"] != sa["Count"]:
        drift.append((label, sb["Count"], sa["Count"]))
    # rows/sec where the streamProbe pair exists (structural convention)
    el = label + ".elements"
    if el in b and el in a and tb > 0 and ta > 0:
        rb = b[el]["TotalMs"] * 1000.0 / tb
        ra = a[el]["TotalMs"] * 1000.0 / ta
        print(f"{'  -> ' + el + ' rows/sec':44} {rb:>10.0f} {ra:>10.0f}")
if drift:
    print("\n!! COUNT DRIFT — workload changed; the timing delta above is VOID:")
    for label, cb, ca in drift:
        print(f"   {label}: {cb} -> {ca}")
    sys.exit(2)
PY
  done
  [ "$any" = "1" ] || { echo "perf-harness: no before/after pairs found (run 'capture before' then 'capture after')" >&2; exit 1; }
}

case "$cmd" in
  list)    list_scenarios ;;
  run)     run_fleet "$filt" ;;
  capture) slot="${2:-}"; f="${3:-}"
           [ "$slot" = "before" ] || [ "$slot" = "after" ] || { echo "usage: perf-harness.sh capture before|after [filter]" >&2; exit 1; }
           capture "$slot" "$f" ;;
  diff)    diff_runs "$filt" ;;
  *)       sed -n '3,20p' "$0"; exit 1 ;;
esac
