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
# 1. dotnet SDK 9.0.305
# ---------------------------------------------------------------------
DOTNET_DIR="$HOME/.dotnet"
DOTNET_VERSION="9.0.305"

if [ -x "$DOTNET_DIR/dotnet" ] && \
   "$DOTNET_DIR/dotnet" --list-sdks 2>/dev/null | grep -q "^$DOTNET_VERSION "; then
    log "dotnet $DOTNET_VERSION already installed"
else
    log "installing dotnet $DOTNET_VERSION..."
    INSTALLER="/tmp/dotnet-install.sh"
    if curl -fsSL \
        "https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.sh" \
        -o "$INSTALLER"; then
        chmod +x "$INSTALLER"
        if "$INSTALLER" --version "$DOTNET_VERSION" --install-dir "$DOTNET_DIR" \
            >/tmp/dotnet-install.log 2>&1; then
            log "dotnet $DOTNET_VERSION installed"
        else
            log "WARNING: dotnet install failed; see /tmp/dotnet-install.log"
            log "         F# builds will fail until resolved manually"
        fi
    else
        log "WARNING: failed to download dotnet installer (network?)"
    fi
fi

# Persist dotnet on PATH for the agent's subsequent shell calls.
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
    echo "export PATH=\"$DOTNET_DIR:\$PATH\"" >> "$CLAUDE_ENV_FILE"
fi
export PATH="$DOTNET_DIR:$PATH"

# ---------------------------------------------------------------------
# 2. Docker daemon — start if installed but not running
# ---------------------------------------------------------------------
if command -v docker >/dev/null 2>&1; then
    if docker info >/dev/null 2>&1; then
        log "Docker daemon already running"
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
        else
            log "WARNING: Docker daemon failed to start within 20s"
            log "         see /tmp/dockerd.log for details"
            log "         canary tests will soft-skip via Deploy.Docker.isAvailable()"
        fi
    fi

    # ---------------------------------------------------------------------
    # 3. Pre-pull SQL Server image (only if Docker is up)
    # ---------------------------------------------------------------------
    if docker info >/dev/null 2>&1; then
        SQL_IMAGE="mcr.microsoft.com/mssql/server:2022-latest"
        if docker image inspect "$SQL_IMAGE" >/dev/null 2>&1; then
            log "$SQL_IMAGE already cached locally"
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
            if ! docker image inspect "$SQL_IMAGE" >/dev/null 2>&1; then
                log "WARNING: SQL Server image pull failed after 3 attempts"
                log "         see /tmp/docker-pull.log for details"
                log "         first canary test will retry the pull on demand"
            fi
        fi
    fi
else
    log "Docker not installed; canary tests will soft-skip"
fi

log "session-start hook complete"
