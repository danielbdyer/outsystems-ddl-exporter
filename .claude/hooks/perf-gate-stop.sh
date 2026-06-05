#!/bin/bash
# Stop hook — runs the V2 sidecar perf-gate after every agent
# message and surfaces the result via `hookSpecificOutput
# .additionalContext` so the agent sees and narrates the perf
# summary in its next response.
#
# Per the user's 2026-05-09 directive ("normalize unexpectedly
# mentioning performance" + "report 'my stop hook just fired'
# at the end of each message series"): the Stop hook IS the
# canary on every agent stop. The agent reads the
# additionalContext and includes the result in its summary.
#
# Soft-skip path: when Docker / dotnet are unreachable, the
# perf-gate exits 0 with a "SKIP" log line; this wrapper still
# emits the JSON envelope (with the SKIP line in additionalContext)
# so the agent knows the hook fired but couldn't gate.

set -uo pipefail

# Ensure dotnet on PATH (SessionStart wrote it but a fresh shell
# may not have inherited).
if [ -d "$HOME/.dotnet" ]; then
    export PATH="$HOME/.dotnet:$PATH"
fi

REPO="${CLAUDE_PROJECT_DIR:-/home/user/outsystems-ddl-exporter}"
PERF_GATE="$REPO/sidecar/projection/scripts/perf-gate.sh"
LOG_FILE="/tmp/claude-perf-gate-stop.log"

# Reuse the warm SQL container if it's up (capped + ready) instead of letting the
# canary cold-start an uncapped ephemeral every fire — slow, and flaky under
# load (a transient login failure surfaces as a canary FATAL). A normal session
# inherits PROJECTION_MSSQL_CONN_STR from the SessionStart hook; this is the
# fallback when it isn't in the hook's environment.
WARM_SH="$REPO/sidecar/projection/scripts/warm-sql.sh"
if [ -z "${PROJECTION_MSSQL_CONN_STR:-}" ] && [ -x "$WARM_SH" ] \
   && [ "$(docker inspect -f '{{.State.Running}}' projection-mssql-warm 2>/dev/null)" = "true" ]; then
    eval "$(bash "$WARM_SH" conn 2>/dev/null)"
fi

# Capture full output to a tmp log; emit summary to context.
if [ -x "$PERF_GATE" ]; then
    "$PERF_GATE" >"$LOG_FILE" 2>&1
    GATE_EXIT=$?
else
    echo "perf-gate script missing at $PERF_GATE" >"$LOG_FILE"
    GATE_EXIT=0
fi

# Surface the result to the agent ONLY when it is ACTIONABLE — a real
# regression, a skip (the gate couldn't run), or a fatal. On a CLEAN run there
# is nothing to say, so emit NO output: the Stop completes and the thread closes
# instead of re-prompting the agent.
#
# WHY (the "Standing by" loop, diagnosed 2026-06-05). The hook fires on EVERY
# stop. When it emitted `additionalContext` UNCONDITIONALLY, that context was
# delivered to the agent as a fresh turn ("narrate the perf result"); the agent
# answered, which is itself a stop, which re-fired the hook, which re-injected
# context — an unbounded loop the agent could only break by going silent. A
# normal local session has a human as the next actor, so it surfaces once and
# waits; here nothing sits between the agent's answer and the next stop, so the
# unconditional injection feeds itself.
#
# The lifecycle enablement is PRESERVED: the canary STILL RUNS on every stop
# (the perf-awareness forcing function is unchanged). The hook now just follows
# the logging contract's own discipline (§13.12 — emit nothing when there is
# nothing to say), speaking only when the result is actionable.
if grep -qE 'REGRESSION|SKIP|FATAL|did not become ready' "$LOG_FILE"; then
    SUMMARY=$(grep -E "perf-gate|SKIP|REGRESSION|FATAL|history depth" "$LOG_FILE" | tail -n 8 | sed 's/"/\\"/g; s/\t/  /g')
    CONTEXT="Stop hook (perf-gate) fired (exit ${GATE_EXIT}):\n${SUMMARY}"
    CONTEXT_JSON=$(printf '%s' "$CONTEXT" | python3 -c 'import json,sys; print(json.dumps(sys.stdin.read()))' 2>/dev/null || printf '"perf-gate-stop output (see %s)"' "$LOG_FILE")
    cat <<EOF
{
  "hookSpecificOutput": {
    "hookEventName": "Stop",
    "additionalContext": ${CONTEXT_JSON}
  }
}
EOF
fi

# Always exit 0 — a regression is surfaced as context (above), never a hard
# block; a clean run is silent so the turn can close normally.
exit 0
