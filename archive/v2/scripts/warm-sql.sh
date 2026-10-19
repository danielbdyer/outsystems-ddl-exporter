#!/usr/bin/env bash
#
# Warm SQL Server container for the Docker-pool dev loop.
#
# WHY THIS EXISTS (2026-06-04). The Docker test pool's per-test-class
# fixtures (`EphemeralContainerFixture`) cold-start a fresh
# Testcontainers SQL Server per class (~30-36s each, ×13 fixture
# classes). `Deploy.acquireContainer` / `useContainer` honor the
# `PROJECTION_MSSQL_CONN_STR` env var: when it points at a reachable
# SQL Server, every class reuses THAT instance (per-test isolation
# still holds — each test creates its own `<prefix>_<guid>` database).
# This script owns the warm container's lifecycle so the cold-start is
# paid ONCE per dev session, not once per class per run.
#
# The CDC test classes deliberately bypass the warm shortcut
# (`IsolatedContainerFixture`) — they pollute instance-wide
# `master.sys.databases.is_cdc_enabled` state and keep their own
# dedicated containers. So a warm-pointed `docker` pool still
# cold-starts the 4 CDC classes; everything else reuses the warm one.
#
# Usage:
#   eval "$(scripts/warm-sql.sh start)"   # start + export the env var
#   scripts/warm-sql.sh status            # container + readiness
#   scripts/warm-sql.sh conn              # print `export PROJECTION_MSSQL_CONN_STR=...`
#   scripts/warm-sql.sh stop              # remove the container
#   scripts/warm-sql.sh restart           # stop + start (clean instance)
#
# Env overrides:
#   WARM_SQL_NAME   (default projection-mssql-warm)
#   WARM_SQL_PORT   (default 11433)
#   WARM_SQL_PW     (default Projection@Strong1 — meets SQL complexity)
#   WARM_SQL_IMAGE  (default mcr.microsoft.com/mssql/server:2022-latest)
#   WARM_SQL_MEM_MB (default 3072 — SQL Server buffer-pool cap)
#   WARM_SQL_MEM    (default 4g — Docker container memory cap, above the SQL cap)
#
# This script is the SINGLE SOURCE OF TRUTH for the warm container's config
# (name / port / password / memory caps). The SessionStart hook delegates here
# rather than duplicating `docker run` — so the config can't drift (a past drift
# left two definitions of `projection-mssql-warm` with different ports/passwords,
# surfacing as "Login failed for user 'sa'").

set -uo pipefail

NAME="${WARM_SQL_NAME:-projection-mssql-warm}"
PORT="${WARM_SQL_PORT:-11433}"
PW="${WARM_SQL_PW:-Projection@Strong1}"
IMAGE="${WARM_SQL_IMAGE:-mcr.microsoft.com/mssql/server:2022-latest}"
# Memory caps (first-principles stability fix, 2026-06-05). SQL Server 2022
# without MSSQL_MEMORY_LIMIT_MB grows its buffer pool toward ~80% of host RAM; on
# a no-swap host it then starves builds + the test pool and the kernel OOM-kills
# sqlservr, leaving the container "Up" but SQL a zombie (the recurring "could not
# open a connection to SQL Server"). The SQL cap sits BELOW the Docker container
# cap so SQL stays bounded and Docker contains any overrun to the container, not
# the host. 3 GB buffer pool is ample for the 150-table / 6.25k-row
# operator-reality envelope (<1M small rows).
MEM_MB="${WARM_SQL_MEM_MB:-3072}"
MEM="${WARM_SQL_MEM:-4g}"
CONN="Server=localhost,${PORT};User Id=sa;Password=${PW};TrustServerCertificate=True;Encrypt=False"

log() { printf '\033[36m[warm-sql]\033[0m %s\n' "$1" >&2; }
err() { printf '\033[31m[warm-sql]\033[0m %s\n' "$1" >&2; }

conn_line() { printf 'export PROJECTION_MSSQL_CONN_STR=%q\n' "$CONN"; }

ensure_daemon() {
    if docker version >/dev/null 2>&1; then return 0; fi
    log "docker daemon not responding; attempting bring-up..."
    if command -v sudo >/dev/null 2>&1; then sudo dockerd >/tmp/dockerd.log 2>&1 & else dockerd >/tmp/dockerd.log 2>&1 & fi
    for _ in $(seq 1 25); do docker version >/dev/null 2>&1 && return 0; sleep 1; done
    err "could not bring up docker daemon (see /tmp/dockerd.log)"; return 1
}

is_running() { [ "$(docker inspect -f '{{.State.Running}}' "$NAME" 2>/dev/null)" = "true" ]; }

wait_ready() {
    log "waiting for SQL Server readiness on $NAME..."
    for i in $(seq 1 45); do
        if docker exec "$NAME" /opt/mssql-tools18/bin/sqlcmd \
                -S localhost -U sa -P "$PW" -C -Q "SELECT 1" >/dev/null 2>&1; then
            log "READY (~$((i*2))s)"; return 0
        fi
        sleep 2
    done
    err "NOT READY after 90s"; docker logs --tail 15 "$NAME" >&2; return 1
}

cmd_start() {
    ensure_daemon || return 1
    if is_running; then
        log "already running ($NAME on :$PORT)"
        wait_ready || return 1
    else
        docker rm -f "$NAME" >/dev/null 2>&1 || true
        if ! docker image inspect "$IMAGE" >/dev/null 2>&1; then
            log "pulling $IMAGE (first run only)..."
            docker pull "$IMAGE" >/dev/null 2>&1 || { err "image pull failed"; return 1; }
        fi
        log "starting $NAME ($IMAGE) on :$PORT (SQL cap ${MEM_MB}MB / container $MEM)..."
        docker run -d --name "$NAME" \
            --memory="$MEM" --memory-swap="$MEM" \
            -e ACCEPT_EULA=Y -e "MSSQL_SA_PASSWORD=$PW" \
            -e "MSSQL_MEMORY_LIMIT_MB=$MEM_MB" \
            -p "${PORT}:1433" "$IMAGE" >/dev/null || { err "docker run failed"; return 1; }
        wait_ready || return 1
    fi
    conn_line
}

cmd_status() {
    if is_running; then
        log "running: $(docker ps --filter "name=$NAME" --format '{{.Status}}') on :$PORT"
        if docker exec "$NAME" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PW" -C -Q "SELECT 1" >/dev/null 2>&1; then
            log "SQL Server: accepting connections"
        else
            log "SQL Server: NOT ready"
        fi
        conn_line
    else
        log "not running (start with: eval \"\$(scripts/warm-sql.sh start)\")"
        return 1
    fi
}

case "${1:-start}" in
    start)   cmd_start ;;
    stop)    docker rm -f "$NAME" >/dev/null 2>&1 && log "removed $NAME" || log "not present" ;;
    restart) docker rm -f "$NAME" >/dev/null 2>&1 || true; cmd_start ;;
    status)  cmd_status ;;
    conn)    conn_line ;;
    *) err "usage: warm-sql.sh [start|stop|restart|status|conn]"; exit 2 ;;
esac
