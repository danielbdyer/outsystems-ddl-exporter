#!/bin/bash
# SessionStart hook for the outsystems-ddl-exporter repository.
#
# Ensures the F# sidecar's canary infrastructure is warm at every
# session start / resume:
#   1. dotnet SDK 9.0.305 (pinned by global.json's rollForward=disable)
#   2. Docker daemon (canary tests use Testcontainers.MsSql)
#   3. mcr.microsoft.com/mssql/server:2022-latest image (~2GB)
#
# Idempotent — every step probes before acting. Honest about
# failures — Docker / pull failures log warnings but don't fail the
# session; the F# canary tests soft-skip via Deploy.Docker.isAvailable()
# when the daemon isn't reachable.
#
# Web-only execution; local sessions skip via $CLAUDE_CODE_REMOTE guard.
set -euo pipefail

# ---------------------------------------------------------------------
# Web-only guard
# ---------------------------------------------------------------------
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
    exit 0
fi

log() { printf '[session-start] %s\n' "$*" >&2; }

# ---------------------------------------------------------------------
# 1. dotnet SDK — version pinned by sidecar/projection/global.json
# ---------------------------------------------------------------------
# Required dependency. If the install fails after retries, the hook
# exits non-zero so the agent sees a hard failure at session start
# rather than discovering it later via "dotnet: command not found".
# Earlier versions of this hook soft-failed on curl/installer errors
# and unconditionally exported PATH, which masked broken state — see
# DECISIONS / hook-discipline rationale at the bottom of this file.
DOTNET_DIR="$HOME/.dotnet"
REPO="${CLAUDE_PROJECT_DIR:-/home/user/outsystems-ddl-exporter}"
GLOBAL_JSON="$REPO/sidecar/projection/global.json"
HOOK_STATUS="$HOME/.claude-projection-hook-status"

if [ ! -f "$GLOBAL_JSON" ]; then
    log "FATAL: cannot locate global.json at $GLOBAL_JSON"
    printf 'session-start FAIL %s — global.json missing\n' "$(date -u +%FT%TZ)" \
        >> "$HOOK_STATUS"
    exit 1
fi

# Parse SDK version out of global.json without a JSON dependency.
# global.json shape: { "sdk": { "version": "9.0.305", ... } }
DOTNET_VERSION="$(grep -oE '"version"[[:space:]]*:[[:space:]]*"[^"]+"' "$GLOBAL_JSON" \
    | head -n1 \
    | sed -E 's/.*"([^"]+)"$/\1/')"
if [ -z "$DOTNET_VERSION" ]; then
    log "FATAL: could not parse dotnet version out of $GLOBAL_JSON"
    printf 'session-start FAIL %s — version-parse failed\n' "$(date -u +%FT%TZ)" \
        >> "$HOOK_STATUS"
    exit 1
fi

verify_dotnet() {
    # Returns 0 iff dotnet at $DOTNET_DIR/dotnet exists, runs, and
    # lists the required SDK version. The list-sdks check is what
    # `--version` alone misses on partial installs.
    [ -x "$DOTNET_DIR/dotnet" ] || return 1
    "$DOTNET_DIR/dotnet" --list-sdks 2>/dev/null \
        | grep -q "^$DOTNET_VERSION " || return 1
}

install_dotnet() {
    # Retries the installer download + execution up to 3 times with
    # exponential backoff (2s, 4s, 8s). Logs to /tmp/dotnet-install.log
    # cumulatively across attempts. Returns 0 on success, 1 on
    # exhausted retries.
    local installer="/tmp/dotnet-install.sh"
    local installer_url="https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.sh"
    local attempt=1
    local backoff=2
    while [ "$attempt" -le 3 ]; do
        log "install attempt $attempt/3 (backoff ${backoff}s on retry)..."
        if ! curl -fsSL "$installer_url" -o "$installer" \
            >>/tmp/dotnet-install.log 2>&1; then
            log "  curl failed (attempt $attempt)"
        else
            chmod +x "$installer"
            if "$installer" --version "$DOTNET_VERSION" --install-dir "$DOTNET_DIR" \
                >>/tmp/dotnet-install.log 2>&1; then
                if verify_dotnet; then
                    return 0
                fi
                log "  installer ran but verification failed (attempt $attempt)"
            else
                log "  installer exited non-zero (attempt $attempt)"
            fi
        fi
        attempt=$((attempt + 1))
        if [ "$attempt" -le 3 ]; then
            sleep "$backoff"
            backoff=$((backoff * 2))
        fi
    done
    return 1
}

if verify_dotnet; then
    log "dotnet $DOTNET_VERSION already installed"
else
    log "installing dotnet $DOTNET_VERSION (per $GLOBAL_JSON)..."
    : > /tmp/dotnet-install.log
    if install_dotnet; then
        log "dotnet $DOTNET_VERSION installed and verified"
    else
        log "FATAL: dotnet $DOTNET_VERSION install failed after 3 attempts"
        log "       see /tmp/dotnet-install.log for full installer output"
        log "       F# builds + tests cannot run until resolved"
        printf 'session-start FAIL %s — dotnet install exhausted retries\n' \
            "$(date -u +%FT%TZ)" >> "$HOOK_STATUS"
        exit 1
    fi
fi

# Persist dotnet on PATH only after verification succeeded. Earlier
# versions exported this unconditionally, leaving a non-existent
# directory on PATH when the install silently failed.
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
    echo "export PATH=\"$DOTNET_DIR:\$PATH\"" >> "$CLAUDE_ENV_FILE"
fi
export PATH="$DOTNET_DIR:$PATH"

# ---------------------------------------------------------------------
# Subsystem state tracker. Each subsystem records one of a small,
# stable vocabulary so the comprehensive status line at end-of-hook
# is greppable. State words: ready / running / cached / missing /
# failed / not-ready / skipped. The final status-file entry is the
# session's full readiness picture — see AGENTS.md "Session-start
# status" section for the agent-side consumption pattern.
#
# The dotnet-only `session-start OK ... — dotnet <v>` line previously
# written here retired in favor of the unified end-of-hook line.
# Rationale: tail-and-grep usage on $HOOK_STATUS expects ONE entry
# per session-start, not two; a fresh agent reading "session-start OK"
# without seeing the comprehensive verdict mistakes the dotnet OK
# for full canary readiness.
DOTNET_STATE="$DOTNET_VERSION"
DOCKER_STATE="missing"
IMAGE_STATE="skipped"
WARM_STATE="skipped"

# ---------------------------------------------------------------------
# 2. Docker daemon — start if installed but not running
# ---------------------------------------------------------------------
if command -v docker >/dev/null 2>&1; then
    if docker info >/dev/null 2>&1; then
        log "Docker daemon already running"
        DOCKER_STATE="running"
    else
        log "starting Docker daemon..."
        SUDO=""
        if [ "$(id -u)" -ne 0 ] && command -v sudo >/dev/null 2>&1; then
            SUDO="sudo"
        fi
        # Background the daemon so this script doesn't block on it.
        # Daemon survives the hook's exit because nohup-style
        # detachment + redirection releases stdio.
        $SUDO sh -c 'dockerd --host=unix:///var/run/docker.sock >/tmp/dockerd.log 2>&1 &' \
            || log "WARNING: dockerd launch failed; see /tmp/dockerd.log"

        # Wait up to 20s for socket + responsive daemon.
        READY=0
        for _ in $(seq 1 20); do
            if [ -S /var/run/docker.sock ] && docker info >/dev/null 2>&1; then
                READY=1
                break
            fi
            sleep 1
        done

        if [ "$READY" -eq 1 ]; then
            log "Docker daemon ready"
            DOCKER_STATE="running"
        else
            log "WARNING: Docker daemon failed to start within 20s"
            log "         see /tmp/dockerd.log for details"
            log "         canary tests will soft-skip via Deploy.Docker.isAvailable()"
            DOCKER_STATE="failed"
        fi
    fi

    # ---------------------------------------------------------------------
    # 3. Pre-pull SQL Server image (only if Docker is up)
    # ---------------------------------------------------------------------
    SQL_IMAGE="mcr.microsoft.com/mssql/server:2022-latest"
    if docker info >/dev/null 2>&1; then
        if docker image inspect "$SQL_IMAGE" >/dev/null 2>&1; then
            log "$SQL_IMAGE already cached locally"
            IMAGE_STATE="cached"
        else
            log "pulling $SQL_IMAGE (~2GB; one-time)..."
            # Retry up to 3x for transient TLS / network hiccups.
            # Observed once: TLS clock-skew "certificate not yet valid"
            # on freshly-booted containers — a retry lets system clock
            # settle.
            ATTEMPT=1
            until [ "$ATTEMPT" -gt 3 ]; do
                if docker pull "$SQL_IMAGE" >/tmp/docker-pull.log 2>&1; then
                    log "$SQL_IMAGE pulled"
                    break
                fi
                log "pull attempt $ATTEMPT failed; retrying in 2s..."
                sleep 2
                ATTEMPT=$((ATTEMPT + 1))
            done
            if docker image inspect "$SQL_IMAGE" >/dev/null 2>&1; then
                IMAGE_STATE="cached"
            else
                log "WARNING: SQL Server image pull failed after 3 attempts"
                log "         see /tmp/docker-pull.log for details"
                log "         first canary test will retry the pull on demand"
                IMAGE_STATE="failed"
            fi
        fi
    fi

    # ---------------------------------------------------------------------
    # 4. Warm SQL Server container — paid once, reused all session
    # ---------------------------------------------------------------------
    # Per session-29 bench data, container start is ~75% of every
    # canary's wall time. Starting a single warm container at session
    # start and reusing it across all canaries collapses the per-call
    # cost from ~10s to ~1.5s (6x speedup empirically). The session-end
    # hook tears it down.
    #
    # Database-level isolation is preserved: every canary still
    # creates a fresh `Source_<guid>` / `Target_<guid>` /
    # `Projection_<guid>` database in the warm container, so the
    # run-level idempotency contract from M2 still holds.
    # Single source of truth: scripts/warm-sql.sh owns the warm container's
    # config (name / port / password / MEMORY CAPS) and creates it idempotently.
    # The hook delegates rather than duplicating `docker run` — so the caps and
    # creds can't drift between the dev script and this hook (a past drift left
    # two definitions of the container with different ports/passwords, surfacing
    # as "Login failed for user 'sa'"). The memory cap is the stability fix:
    # uncapped SQL 2022 balloons its buffer pool and the no-swap host OOM-kills
    # sqlservr, leaving the container "Up" but a zombie.
    WARM_SH="$REPO/sidecar/projection/scripts/warm-sql.sh"
    if docker info >/dev/null 2>&1 && [ -x "$WARM_SH" ]; then
        if WARM_CONN_LINE="$(bash "$WARM_SH" start 2>>/tmp/warm-sql-hook.log)"; then
            # warm-sql.sh prints `export PROJECTION_MSSQL_CONN_STR=<%q-quoted>`.
            eval "$WARM_CONN_LINE"
            WARM_STATE="ready"
            if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
                echo "$WARM_CONN_LINE" >> "$CLAUDE_ENV_FILE"
            fi
            log "warm SQL Server ready (via warm-sql.sh; PROJECTION_MSSQL_CONN_STR exported)"
        else
            log "WARNING: warm-sql.sh could not start the warm container (see /tmp/warm-sql-hook.log)"
            log "         canaries will fall back to ephemeral container per call"
            WARM_STATE="failed"
        fi
    fi
else
    log "Docker not installed; canary tests will soft-skip"
fi

# ---------------------------------------------------------------------
# Comprehensive subsystem status — single tail-friendly line for the
# agent. Verdict vocabulary:
#   READY     — every required subsystem is up; canary tests will run
#   DEGRADED  — dotnet ready, but Docker / image / warm container
#               degraded; pure-F# work fine, canary tests will soft-
#               skip via Deploy.Docker.ensureRunning()
#   FAIL      — dotnet missing (already exited above with FAIL)
# Subsystems use stable vocabulary (running / cached / ready /
# missing / failed / not-ready / skipped) so `tail $HOOK_STATUS |
# grep -oE 'docker=[a-z-]+'` is a one-liner.
if [ "$DOCKER_STATE" = "running" ] && [ "$IMAGE_STATE" = "cached" ] && [ "$WARM_STATE" = "ready" ]; then
    HOOK_VERDICT="READY"
else
    HOOK_VERDICT="DEGRADED"
fi
printf 'session-start %s %s | dotnet=%s | docker=%s | image=%s | warm=%s\n' \
    "$HOOK_VERDICT" \
    "$(date -u +%FT%TZ)" \
    "$DOTNET_STATE" \
    "$DOCKER_STATE" \
    "$IMAGE_STATE" \
    "$WARM_STATE" \
    >> "$HOOK_STATUS"

log "session-start hook complete (verdict: $HOOK_VERDICT; dotnet=$DOTNET_STATE docker=$DOCKER_STATE image=$IMAGE_STATE warm=$WARM_STATE)"

# ---------------------------------------------------------------------
# Hook discipline (do not remove without writing the rationale)
# ---------------------------------------------------------------------
#  - dotnet is REQUIRED — install failures fail the hook (exit 1).
#  - Docker / SQL Server warm container are SOFT — they have a
#    fallback in F# (canary tests skip via Deploy.Docker.isAvailable()
#    or fall back to ephemeral containers per call). Keep them as
#    `log "WARNING"` — failing the hook on Docker hiccups would block
#    pure-F# work that doesn't need the canary.
#  - PATH is exported only after `verify_dotnet` succeeds. Do NOT
#    pre-export it; an empty / nonexistent $HOME/.dotnet on PATH
#    makes `which dotnet` resolve to nothing while masking the
#    broken state.
#  - Status log at $HOOK_STATUS gives the agent a structural surface
#    to detect prior failures without spelunking /tmp logs.
#  - Version is read from sidecar/projection/global.json — the single
#    source of truth. Do NOT hard-code a version here that can drift.
