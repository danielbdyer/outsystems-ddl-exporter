#!/usr/bin/env bash
#
# V2 sidecar developer test runner — tiered, streaming, and crash-safe.
#
# WHY THIS EXISTS (binary-search diagnosis, 2026-05-24):
#   The test suite splits into two pools with very different cost:
#     - PURE pool  : ~2540 tests, parallel, ~25s wall.
#     - DOCKER pool : ~110 tests in the `Docker-SqlServer` collection,
#                     run STRICTLY SERIAL (DisableParallelization — it
#                     prevents SQL-Server-instance livelock from
#                     concurrent CREATE/DROP DATABASE + CDC's
#                     instance-wide effects), ~4m14s wall.
#   A single `dotnet test` runs BOTH concurrently (parallel pure pool
#   alongside the serial Docker collection) PLUS the SQL container — on
#   a 4-core / 15 GiB / NO-SWAP host. That over-subscribes memory/CPU
#   until the test host is OOM-killed mid-run ("the active test run was
#   aborted because the host process exited unexpectedly" + a ~700 MB
#   hang dump). That is the "tests time out / go unresponsive / we never
#   recover the next step" failure mode.
#
# THE FIX (this script): never run the two pools concurrently. Run each
# in its own sequential `dotnet test` process. The inner dev loop uses
# `fast` (pure only, ~25s); the Docker pool is opt-in; `all` runs them
# one-after-another so the full suite completes without OOM.
#
# DX: streams per-test results live (no buffered `-v q | tail` that
# looks hung), prints per-phase timing, and on failure surfaces the
# failed test names from the TRX (the test-failure capture protocol).
#
# Usage:
#   scripts/test.sh                # fast (pure pool) — the default loop
#   scripts/test.sh fast           # pure pool only (~25s)
#   scripts/test.sh docker         # Docker-SqlServer collection (serial)
#   scripts/test.sh canary         # round-trip canaries only (Docker subset)
#   scripts/test.sh focus <pat>    # one class/method by FullyQualifiedName~<pat>
#   scripts/test.sh all            # fast THEN docker, sequential (crash-safe)
#   scripts/test.sh list           # show the derived pool filters and exit
#   scripts/test.sh fast -- <args> # pass extra args through to dotnet test
#
# WARM CONTAINER (Docker-pool dev loop). `docker` / `canary` / `focus` /
# `all` auto-detect a running `projection-mssql-warm` container
# (scripts/warm-sql.sh) and point the fixture classes at it via
# PROJECTION_MSSQL_CONN_STR — every non-CDC class reuses ONE instance
# instead of cold-starting per class. Start it once per session:
#   eval "$(scripts/warm-sql.sh start)"
# then iterate, e.g.: scripts/test.sh focus MigrationCanaryTests
# CDC classes (IsolatedContainerFixture) cold-start regardless.
#
# Env:
#   TEST_NO_BUILD=1   skip the build step (DLL already current)
#   TEST_CONFIG=Release   build/run Release instead of Debug
#   PROJECTION_MSSQL_CONN_STR   explicit warm master conn (overrides autodetect)

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
TESTS_DIR="$ROOT/tests/Projection.Tests"
TEST_PROJECT="$TESTS_DIR/Projection.Tests.fsproj"
CONFIG="${TEST_CONFIG:-Debug}"
RESULTS_DIR="/tmp/projection-test-results"

bold() { printf '\033[1m%s\033[0m\n' "$1"; }
log()  { printf '\033[36m[test]\033[0m %s\n' "$1"; }
err()  { printf '\033[31m[test]\033[0m %s\n' "$1" >&2; }

# Derive the Docker-collection test classes from the source markers, so the
# pool split stays correct as classes are added/removed. One file = one class
# (the module/type name matches the file name by convention); the ephemeral
# container fixture is infra, not a test class.
docker_classes() {
    grep -rl 'Collection("Docker-SqlServer")' "$TESTS_DIR"/*.fs 2>/dev/null \
        | xargs -n1 basename \
        | sed 's/\.fs$//' \
        | grep -v '^EphemeralContainerFixture$' \
        | sort -u
}

# `FullyQualifiedName~A|FullyQualifiedName~B|...` — selects the Docker pool.
docker_filter() {
    docker_classes | sed 's/^/FullyQualifiedName~/' | paste -sd '|' -
}

# `FullyQualifiedName!~A&FullyQualifiedName!~B&...` — selects the pure pool.
pure_filter() {
    docker_classes | sed 's/^/FullyQualifiedName!~/' | paste -sd '&' -
}

# If PROJECTION_MSSQL_CONN_STR is unset but the warm container
# (scripts/warm-sql.sh) is running, point the Docker pool at it so the
# fixture classes reuse one instance instead of cold-starting per class.
# CDC classes (IsolatedContainerFixture) bypass this and cold-start
# regardless. No-op for the pure pool.
warm_autodetect() {
    if [[ -n "${PROJECTION_MSSQL_CONN_STR:-}" ]]; then
        log "warm: using PROJECTION_MSSQL_CONN_STR from env"
        return 0
    fi
    local name="${WARM_SQL_NAME:-projection-mssql-warm}"
    if [[ "$(docker inspect -f '{{.State.Running}}' "$name" 2>/dev/null)" == "true" ]]; then
        local conn
        conn="$(WARM_SQL_NAME="$name" "$SCRIPT_DIR/warm-sql.sh" conn 2>/dev/null \
                | sed "s/^export PROJECTION_MSSQL_CONN_STR=//")"
        # warm-sql.sh prints a %q-quoted value; strip one layer of quoting.
        eval "export PROJECTION_MSSQL_CONN_STR=$conn"
        log "warm: auto-detected container '$name' → reusing it"
    else
        log "warm: no warm container running; classes cold-start per class"
        log "      (eval \"\$(scripts/warm-sql.sh start)\" to speed up the Docker pool)"
    fi
}

build_once() {
    if [[ "${TEST_NO_BUILD:-0}" == "1" ]]; then
        log "skipping build (TEST_NO_BUILD=1)"
        return 0
    fi
    log "building Projection.Tests ($CONFIG)..."
    local t0 t1
    t0=$(date +%s)
    if ! dotnet build "$TEST_PROJECT" -c "$CONFIG" --nologo -v q; then
        err "build failed"
        exit 1
    fi
    t1=$(date +%s)
    log "build ok ($((t1 - t0))s)"
}

# run_pool <label> <filter> [maxParallelThreads]
#   One sequential dotnet test process, streamed. `maxParallelThreads`,
#   when set, is passed as an xUnit RunSetting (default: xUnit's own
#   default — core count).
#
#   Parallelism note: the sync-over-async deadlock that used to wedge the
#   pure pool is fixed at the source — test bodies route their blocking
#   waits through `TaskSync.run` (Task.Run offload, so continuations
#   resume off xUnit's capped sync context). Bounded parallelism is
#   therefore safe; no `-1` workaround is needed. The two pools are still
#   run as separate sequential processes (see dispatch below) so the
#   parallel pure pool never piles onto the serial Docker pool + SQL
#   container and OOM-kills the host on this 4-core / no-swap box.
run_pool() {
    local label="$1" filter="$2" maxpar="${3:-}"
    local trx="$RESULTS_DIR/$label.trx"
    rm -rf "$RESULTS_DIR/$label"
    bold "──────── $label pool ────────"
    [[ -n "$filter" ]] && log "filter: $filter"
    [[ -n "$maxpar" ]] && log "maxParallelThreads: $maxpar"
    local t0 t1 code
    local filter_args=()
    [[ -n "$filter" ]] && filter_args=(--filter "$filter")
    # RunSettings (after `--`): the parallelism setting + any user extras.
    local runsettings=()
    [[ -n "$maxpar" ]] && runsettings+=("xUnit.MaxParallelThreads=$maxpar")
    [[ ${#EXTRA_ARGS[@]} -gt 0 ]] && runsettings+=("${EXTRA_ARGS[@]}")
    local sep=()
    [[ ${#runsettings[@]} -gt 0 ]] && sep=(--)
    t0=$(date +%s)
    # console;verbosity=normal streams per-test results live (progress is
    # visible, so a long run never *looks* hung); trx is the structured
    # ground truth for failure extraction.
    dotnet test "$TEST_PROJECT" -c "$CONFIG" --no-build --nologo \
        "${filter_args[@]}" \
        --logger "console;verbosity=normal" \
        --logger "trx;LogFileName=$label.trx" \
        --results-directory "$RESULTS_DIR" \
        "${sep[@]}" "${runsettings[@]}"
    code=$?
    t1=$(date +%s)
    if [[ "$code" -ne 0 ]]; then
        err "$label pool FAILED (exit $code, $((t1 - t0))s)"
        if [[ -f "$trx" ]]; then
            err "failed tests:"
            grep -oE 'testName="[^"]*"' "$trx" 2>/dev/null \
                | sed 's/testName="/  /; s/"$//' | sort -u >&2 || true
            # Narrow to actual failures.
            err "(outcome=Failed names:)"
            grep -B1 'outcome="Failed"' "$trx" 2>/dev/null \
                | grep -oE 'testName="[^"]*"' | sed 's/testName="/  /; s/"$//' | sort -u >&2 || true
        fi
    else
        log "$label pool passed ($((t1 - t0))s)"
    fi
    return "$code"
}

CMD="${1:-fast}"
shift || true
# `focus <pattern>` takes a positional filter pattern before any `-- <args>`.
FOCUS_ARG=""
if [[ "$CMD" == "focus" ]]; then
    FOCUS_ARG="${1:-}"
    shift || true
fi
# Everything after a literal `--` is forwarded to dotnet test.
EXTRA_ARGS=()
if [[ "${1:-}" == "--" ]]; then
    shift
    EXTRA_ARGS=("$@")
fi

case "$CMD" in
    list)
        bold "Docker pool classes:"; docker_classes | sed 's/^/  /'
        echo; bold "pure filter:";   pure_filter
        echo; bold "docker filter:"; docker_filter
        exit 0
        ;;
    focus)
        # Single-class / single-method iteration against the warm
        # container. Pattern is a FullyQualifiedName substring, e.g.
        #   scripts/test.sh focus MigrationCanaryTests
        #   scripts/test.sh focus 'MigrationCanaryTests.migrate A to B'
        if [[ -z "$FOCUS_ARG" ]]; then
            err "focus needs a pattern: test.sh focus <FullyQualifiedName-substring>"
            exit 2
        fi
        warm_autodetect
        build_once
        run_pool focus "FullyQualifiedName~$FOCUS_ARG"
        exit $?
        ;;
    fast)
        build_once
        run_pool fast "$(pure_filter)"
        exit $?
        ;;
    docker)
        warm_autodetect
        build_once
        run_pool docker "$(docker_filter)"
        exit $?
        ;;
    canary)
        warm_autodetect
        build_once
        run_pool canary "FullyQualifiedName~Canary"
        exit $?
        ;;
    all)
        warm_autodetect
        build_once
        # Sequential, separate processes — pools never run concurrently, so
        # the host is never over-subscribed (the OOM-crash fix).
        run_pool fast "$(pure_filter)";     fast_code=$?
        run_pool docker "$(docker_filter)"; docker_code=$?
        echo
        bold "──────── summary ────────"
        log "fast:   exit $fast_code"
        log "docker: exit $docker_code"
        [[ "$fast_code" -eq 0 && "$docker_code" -eq 0 ]] && exit 0 || exit 1
        ;;
    *)
        err "unknown command: $CMD"
        err "usage: test.sh [fast|docker|canary|all|list] [-- <dotnet test args>]"
        err "       test.sh focus <FullyQualifiedName-substring> [-- <dotnet test args>]"
        exit 2
        ;;
esac
