#!/bin/bash
# PreToolUse hook — Docker state probe + auto-repair for canary-track
# Bash commands. Fires before every Bash tool call; cheaply ignores
# commands that don't touch infrastructure; for commands that DO
# (dotnet test, docker *, *canary*), runs `docker info` with a tight
# timeout, attempts a daemon bring-up if it's down, and reports the
# state back to the agent via `additionalContext`.
#
# Why this exists (per session-15 internal diagnosis): the agent
# repeatedly diagnosed test hangs as "Docker unavailable" without
# verifying. The session-start hook already writes a status line to
# `$HOME/.claude-projection-hook-status`, but that requires the
# agent to remember to look. This PreToolUse hook makes the
# verification automatic and unmissable: every infra-relevant tool
# call surfaces fresh Docker state in the agent's view.
#
# Performance: fast-path (command isn't infra-relevant) is a single
# bash `case` match — sub-millisecond. Slow-path runs `timeout 1
# docker info` which is ~10ms when the daemon is up. Bring-up
# attempt is best-effort with a 5 s ceiling (pairs with the F#
# `Deploy.Docker.BringupBudgetMs = 5000`).
#
# This hook NEVER blocks a tool call. Exit 0 always. The
# additionalContext output is the agent-facing surface.

set -uo pipefail

# Read the tool-call JSON on stdin. We need `tool_input.command`.
INPUT="$(cat || true)"

# Extract the command field via a tolerant grep+sed (avoid jq dep).
CMD="$(printf '%s' "$INPUT" | grep -oE '"command"[[:space:]]*:[[:space:]]*"[^"]*"' | head -n1 | sed -E 's/.*"([^"]*)"$/\1/')"

# Fast-path filter: only do the probe when the command touches
# infrastructure that depends on Docker. This case statement is the
# performance-critical surface — keep the patterns tight.
case "$CMD" in
    *"dotnet test"* | *"docker "* | *"docker-compose"* \
    | *"canary"* | *"Canary"* \
    | *"Testcontainers"* | *"sqlcmd"* | *"mssql"*)
        ;;
    *)
        # Not an infra-relevant command. Exit 0 silently; agent
        # output is not modified.
        exit 0
        ;;
esac

# ----------------------------------------------------------------
# Docker probe + auto-repair
# ----------------------------------------------------------------
probe_docker() {
    timeout 1 docker info >/dev/null 2>&1
}

ensure_dockerd() {
    # Best-effort daemon spawn. Pairs with `session-start.sh`.
    # `pgrep dockerd` is the cheapest signal that something is
    # already trying. If a daemon is launching but the socket isn't
    # listening yet, the poll-until-ready loop below catches it.
    if ! pgrep -f dockerd >/dev/null 2>&1; then
        sudo dockerd >/tmp/dockerd-hook.log 2>&1 &
        disown 2>/dev/null || true
    fi
    # Poll up to 5 s for the daemon to become responsive.
    local i=0
    while [ "$i" -lt 25 ]; do
        if probe_docker; then return 0; fi
        sleep 0.2
        i=$((i + 1))
    done
    return 1
}

# Single quick probe.
if probe_docker; then
    DOCKER_STATE="up"
    DOCKER_DETAIL="$(docker ps --format '{{.Names}}:{{.Status}}' 2>/dev/null | tr '\n' ',' | sed 's/,$//')"
    if [ -z "$DOCKER_DETAIL" ]; then
        DOCKER_DETAIL="(no containers running)"
    fi
else
    if ensure_dockerd; then
        DOCKER_STATE="up (auto-repaired)"
        DOCKER_DETAIL="$(docker ps --format '{{.Names}}:{{.Status}}' 2>/dev/null | tr '\n' ',' | sed 's/,$//')"
        if [ -z "$DOCKER_DETAIL" ]; then
            DOCKER_DETAIL="(no containers running)"
        fi
    else
        DOCKER_STATE="down (auto-repair failed)"
        DOCKER_DETAIL="see /tmp/dockerd-hook.log"
    fi
fi

# Tail the session-start hook status for one-line context. Cheap.
HOOK_STATUS="$HOME/.claude-projection-hook-status"
if [ -f "$HOOK_STATUS" ]; then
    LAST_HOOK_LINE="$(tail -n1 "$HOOK_STATUS" 2>/dev/null || true)"
else
    LAST_HOOK_LINE="(no session-start status file)"
fi

# Output additionalContext via Claude Code's PreToolUse JSON format.
# Per the hook spec, `hookSpecificOutput.additionalContext` is
# surfaced to the agent.
cat <<JSON
{
  "hookSpecificOutput": {
    "hookEventName": "PreToolUse",
    "additionalContext": "DOCKER PROBE (auto-fired before infra-relevant Bash):\n  state: $DOCKER_STATE\n  containers: $DOCKER_DETAIL\n  last session-start: $LAST_HOOK_LINE\n  → If a canary test slows down or skips, this is the ground truth — do not assume otherwise without re-probing."
  }
}
JSON

exit 0
