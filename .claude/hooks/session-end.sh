#!/bin/bash
# SessionEnd hook for the outsystems-ddl-exporter repository.
#
# Two-step at session end:
#
#   1. **Final canary verification** — runs `projection canary`
#      against `sidecar/projection/fixtures/canary-gate.sql` (the
#      OutSystems-shaped operator-reality fixture). A green canary
#      gives operator confidence that the work shipped during the
#      session preserved the round-trip invariants. A red canary
#      surfaces regressions while context is still fresh. Runs in
#      ~1.5s against the warm container.
#
#   2. **Warm container teardown** — counterpart to the
#      SessionStart hook's container bring-up. Stops + removes the
#      `projection-mssql-warm` container so dangling SQL Server
#      processes don't accumulate across remote sessions.
#
# Both steps are best-effort and never fail the hook hard:
#   - If the warm container isn't running, canary skips with a log
#   - If the CLI dll isn't built, canary skips with a log
#   - If docker stop fails, falls back to docker rm -f
#
# Web-only execution; local sessions skip via $CLAUDE_CODE_REMOTE
# guard (matches SessionStart).
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
    exit 0
fi

log() { printf '[session-end] %s\n' "$*" >&2; }

WARM_NAME="projection-mssql-warm"
REPO="${CLAUDE_PROJECT_DIR:-/home/user/outsystems-ddl-exporter}"
FIXTURE="$REPO/sidecar/projection/fixtures/canary-gate.sql"
PROJECTION_DLL="$REPO/sidecar/projection/src/Projection.Cli/bin/Debug/net9.0/projection.dll"

# ---------------------------------------------------------------------
# 1. Final canary verification
# ---------------------------------------------------------------------
# Ensure dotnet is on PATH (SessionStart wrote `export PATH=...` to
# CLAUDE_ENV_FILE, but a hook spawned in a fresh shell may not have
# sourced it).
if [ -d "$HOME/.dotnet" ]; then
    export PATH="$HOME/.dotnet:$PATH"
fi

CANARY_RAN=0
if [ -z "${PROJECTION_MSSQL_CONN_STR:-}" ]; then
    log "skipping final canary: PROJECTION_MSSQL_CONN_STR not set (no warm container)"
elif [ ! -f "$FIXTURE" ]; then
    log "skipping final canary: fixture missing at $FIXTURE"
elif [ ! -f "$PROJECTION_DLL" ]; then
    log "skipping final canary: CLI dll not built at $PROJECTION_DLL"
    log "         (run 'dotnet build sidecar/projection/Projection.sln' first)"
elif ! command -v dotnet >/dev/null 2>&1; then
    log "skipping final canary: dotnet not on PATH"
else
    log "running final canary against $(basename "$FIXTURE")..."
    CANARY_RAN=1
    # Capture exit code; surface output verbatim so operator sees
    # bench data + canary verdict.
    set +e
    dotnet "$PROJECTION_DLL" canary "$FIXTURE" >/tmp/session-end-canary.log 2>&1
    CANARY_EXIT=$?
    set -e
    case "$CANARY_EXIT" in
        0)
            log "final canary GREEN"
            # Surface bench summary so the operator sees the perf
            # picture at session end. Skip the table headers; show
            # only the rows for compactness.
            if grep -qE '^\| ' /tmp/session-end-canary.log 2>/dev/null; then
                log "session bench (top 8 by total time):"
                grep -E '^\| [a-zA-Z]' /tmp/session-end-canary.log \
                    | head -8 \
                    | while IFS= read -r line; do
                        printf '[session-end]   %s\n' "$line" >&2
                    done
            fi
            ;;
        5)
            log "WARNING: final canary RED — PhysicalSchema diff non-empty"
            log "         see /tmp/session-end-canary.log for full diff"
            tail -25 /tmp/session-end-canary.log >&2
            ;;
        *)
            log "WARNING: final canary failed with exit $CANARY_EXIT"
            log "         see /tmp/session-end-canary.log for details"
            tail -10 /tmp/session-end-canary.log >&2
            ;;
    esac
fi

# ---------------------------------------------------------------------
# 2. Warm container teardown
# ---------------------------------------------------------------------
if command -v docker >/dev/null 2>&1; then
    if docker ps --filter "name=^${WARM_NAME}$" --format "{{.Names}}" 2>/dev/null \
        | grep -qx "$WARM_NAME"; then
        log "stopping warm SQL Server container ($WARM_NAME)..."
        # `docker stop` issues SIGTERM; container stops gracefully.
        # `--rm` flag from SessionStart removes it automatically.
        if docker stop "$WARM_NAME" >/dev/null 2>&1; then
            log "warm SQL Server container stopped"
        else
            log "WARNING: docker stop $WARM_NAME failed; trying docker rm -f"
            docker rm -f "$WARM_NAME" >/dev/null 2>&1 || true
        fi
    else
        log "no warm SQL Server container to stop"
    fi
else
    log "Docker not installed; nothing to clean up"
fi

log "session-end hook complete"
