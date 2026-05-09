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

# Capture full output to a tmp log; emit summary to context.
if [ -x "$PERF_GATE" ]; then
    "$PERF_GATE" >"$LOG_FILE" 2>&1
    GATE_EXIT=$?
else
    echo "perf-gate script missing at $PERF_GATE" >"$LOG_FILE"
    GATE_EXIT=0
fi

# Extract the last two non-empty lines (the summary + status).
# perf-gate emits its own structured one-liner on success/failure;
# we surface that as the additionalContext. Tail keeps the payload
# small — the agent doesn't need the full deploy log.
SUMMARY=$(grep -E "perf-gate|SKIP|REGRESSION|history depth" "$LOG_FILE" | tail -n 8 | sed 's/"/\\"/g; s/\t/  /g')

# Build the additionalContext payload. Use printf so newlines
# are preserved as escaped \n inside the JSON string.
CONTEXT="Stop hook (perf-gate) fired (exit ${GATE_EXIT}):\n${SUMMARY}"

# Escape newlines for JSON.
CONTEXT_JSON=$(printf '%s' "$CONTEXT" | python3 -c 'import json,sys; print(json.dumps(sys.stdin.read()))' 2>/dev/null || printf '"perf-gate-stop output (see %s)"' "$LOG_FILE")

cat <<EOF
{
  "hookSpecificOutput": {
    "hookEventName": "Stop",
    "additionalContext": ${CONTEXT_JSON}
  }
}
EOF

# Always exit 0 so a perf regression doesn't block the agent
# from completing its turn — the regression is surfaced as
# context, the agent decides whether to act.
exit 0
</content>
