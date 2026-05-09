#!/bin/bash
# SessionEnd hook for the outsystems-ddl-exporter repository.
#
# Counterpart to .claude/hooks/session-start.sh. Tears down the
# session-scoped warm SQL Server container that the SessionStart
# hook started, so dangling SQL Server processes don't accumulate
# across remote sessions.
#
# Per session-29 operator framing — and pairs with the warm-
# container fast lane in `Projection.Pipeline.Deploy`. The
# SessionStart hook starts the container; this hook stops it.
# Each session pays exactly one container-boot cost, no more,
# no less.
#
# Web-only execution; local sessions skip via $CLAUDE_CODE_REMOTE
# guard (matches SessionStart).
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
    exit 0
fi

log() { printf '[session-end] %s\n' "$*" >&2; }

WARM_NAME="projection-mssql-warm"

if command -v docker >/dev/null 2>&1; then
    if docker ps --filter "name=^${WARM_NAME}$" --format "{{.Names}}" 2>/dev/null \
        | grep -qx "$WARM_NAME"; then
        log "stopping warm SQL Server container ($WARM_NAME)..."
        # `docker stop` issues SIGTERM, container stops gracefully,
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
