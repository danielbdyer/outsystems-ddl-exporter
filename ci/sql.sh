#!/usr/bin/env bash
# The machine's shared SQL Server for tests and sessions (V3_MILESTONES.md WP 0.7, section 1 fact 12): one container, estate-sql,
# from the image pinned by tag and digest, SQL Server Agent on (CDC needs it), published on 127.0.0.1 only. Its SA password
# is generated once per machine and kept only in ~/.estate/sql.env; nothing here prints it. ci/sql.ps1 is the same on Windows.
#   ci/sql.sh up     pull the image when absent, create or start the container, wait until SQL Server answers
#   ci/sql.sh down   remove the container; the password stays for the next up
#   ci/sql.sh conn   a command that sets ESTATE_SQL, for eval "$(ci/sql.sh conn)"; it names the file, never the password
set -euo pipefail

image='mcr.microsoft.com/mssql/server:2022-latest@sha256:4402d880dd4c34bfa7d8705e56a86cd6c88da80a1f6bbbe741f999e76264a090'
name=estate-sql
dir="$HOME/.estate"
env="$dir/sql.env"
lock="$dir/sql.lock"

fail() { echo "ci/sql.sh: $1" >&2; exit "${2:-1}"; }
value() { if [ -f "$env" ]; then sed -n "s/^$1=//p" "$env" | tr -d '\r'; fi; }
state() { docker container inspect --format '{{.State.Status}}' "$name" 2>/dev/null || true; }

# One up at a time per machine, so two runs never make two passwords for one container: the lock is a file created
# exclusively (noclobber here, CreateNew in ci/sql.ps1). A lock older than ten minutes was left by a killed run.
lock() {
  mkdir -p "$dir"
  for _ in $(seq 1 180); do
    if (set -C; : > "$lock") 2>/dev/null; then trap 'rm -f "$lock"' EXIT; return 0; fi
    if [ -n "$(find "$lock" -mmin +10 2>/dev/null)" ]; then rm -f "$lock"; fi
    sleep 1
  done
  fail "$lock has been held for three minutes; remove it if no other ci/sql run is going"
}

up() {
  docker info >/dev/null 2>&1 || fail "Docker does not answer (docker info): start Docker, or set ESTATE_SQL to another SQL Server" 4
  lock
  if [ -z "$(state)" ]; then
    password="$(value MSSQL_SA_PASSWORD)"
    [ -n "$password" ] || password="Est!$(od -An -tx1 -N16 /dev/urandom | tr -d ' \n')"
    (umask 077; printf 'MSSQL_SA_PASSWORD=%s\nESTATE_SQL_PORT=%s\n' "$password" "${ESTATE_SQL_PORT:-11433}" > "$env")
    docker image inspect "$image" >/dev/null 2>&1 || docker pull "$image" >&2
    if ! docker run -d --name "$name" --env-file "$env" -e ACCEPT_EULA=Y -e MSSQL_AGENT_ENABLED=true \
      -p "127.0.0.1:$(value ESTATE_SQL_PORT):1433" "$image" >/dev/null; then
      docker rm -f "$name" >/dev/null 2>&1 || true
      fail "$name did not start on 127.0.0.1:$(value ESTATE_SQL_PORT); if the port is taken: ESTATE_SQL_PORT=<a free port> ci/sql.sh up"
    fi
  elif [ -z "$(value MSSQL_SA_PASSWORD)" ]; then
    fail "$name exists but $env holds no password for it: ci/sql.sh down, then ci/sql.sh up" 6
  elif [ "$(state)" != running ]; then
    docker start "$name" >/dev/null
  fi

  # The password stays inside the container: sqlcmd's SELECT 1 runs there and reads the container's own environment.
  # MSYS_NO_PATHCONV keeps Git Bash from rewriting the container's paths as Windows ones.
  for _ in $(seq 1 90); do
    [ "$(state)" = running ] || { docker logs --tail 20 "$name" >&2; fail "$name stopped; its last lines are above"; }
    if MSYS_NO_PATHCONV=1 docker exec "$name" /bin/sh -c '/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -b -Q "SELECT 1" >/dev/null 2>&1'; then
      echo "$name: SQL Server on 127.0.0.1,$(value ESTATE_SQL_PORT); the SA password is in $env"
      return 0
    fi
    sleep 2
  done
  fail "SQL Server in $name did not answer within three minutes"
}

down() {
  lock
  docker rm -f "$name" >/dev/null 2>&1 || true
  echo "$name: removed; $env keeps the password for the next up"
}

# A command, not the secret: it reads the password from the file only when it is evaluated, so no output of
# this script, captured or not, ever holds it.
conn() {
  [ -n "$(value MSSQL_SA_PASSWORD)" ] || fail "$env holds no password: ci/sql.sh up first"
  cat <<EOF
export ESTATE_SQL="Server=127.0.0.1,$(value ESTATE_SQL_PORT);User ID=sa;TrustServerCertificate=True;Password=\$(sed -n 's/^MSSQL_SA_PASSWORD=//p' '$env' | tr -d '\\r')"
EOF
}

case "${1:-}" in
  up | down | conn) "$1" ;;
  *) fail "usage: ci/sql.sh up | down | conn" ;;
esac
