#!/usr/bin/env bash
# The machine's shared SQL Server for tests and sessions on Linux (DECISIONS.md, 2026-10-19): SQL Server 2022 Express,
# installed natively from packages.microsoft.com and run as a process by the mssql user, listening on 127.0.0.1 only; no
# container and no image. It has no SQL Server Agent, so CDC is never proven here. Its SA password is generated once per machine
# and kept only in ~/.dbchange/sql.env, mode 0600, beside the port; nothing here prints it. ci/sql.ps1 starts LocalDB on Windows.
#   ci/sql.sh up     install the pinned package where mssql-server is absent (an installed SQL Server 16 or later is kept, an
#                    older one refused, and one installed or running without sql.env refused as set up by something else),
#                    start sqlservr when it is not running, wait until it answers sa with sql.env's password
#   ci/sql.sh down   stop sqlservr; the databases and the password stay for the next up
#   ci/sql.sh conn   a command that sets DBCHANGE_SQL, for eval "$(ci/sql.sh conn)"; it names the file, never the password
set -euo pipefail

# SQL Server 2022 publishes packages for Ubuntu 22.04 only. On 24.04 (this repository's cloud sessions and ubuntu-latest) the
# 22.04 package installs and lacks one library, libldap-2.5.so.0, which 24.04 replaced with 2.6; the 22.04 archive's
# libldap-2.5-0 is installed beside it, pinned by its SHA-256. It is fetched from Launchpad's librarian, which keeps a build
# after a later one supersedes it in jammy-updates; archive.ubuntu.com's pool drops it then.
version='16.0.4305.5-1'
ldap='https://launchpad.net/ubuntu/+archive/primary/+files/libldap-2.5-0_2.5.20+dfsg-0ubuntu0.22.04.1_amd64.deb'
ldap_sha256='a3ee4ac116a2c50c9ddfe9de91c0781a50704244771474334340cdce02f5cd4f'
microsoft_key='BC528686B50D79E339D3721CEB3E94ADBE1229CF'
sqlservr=/opt/mssql/bin/sqlservr
sqlcmd=/opt/mssql-tools18/bin/sqlcmd
dir="$HOME/.dbchange"
env="$dir/sql.env"
lock="$dir/sql.lock"
log="$dir/sqlservr.log"

fail() { echo "ci/sql.sh: $1" >&2; exit "${2:-1}"; }
value() { if [ -f "$env" ]; then sed -n "s/^$1=//p" "$env" | tr -d '\r'; fi; }
root() { if [ "$(id -u)" = 0 ]; then "$@"; else sudo "$@"; fi; }
running() { pgrep -u mssql -x sqlservr >/dev/null 2>&1; }
# The installed mssql-server's version, empty where none is installed (a package removed with its configuration kept is none).
# dpkg-query exits 1 for a package it does not know, which pipefail would carry into `had="$(installed)"` and errexit end silently.
installed() { { dpkg-query -W -f '${Status} ${Version}' mssql-server 2>/dev/null || true; } | sed -n 's/^install ok installed //p'; }

# One up at a time per machine, so two runs never make two passwords for one server: the lock is a file created
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

# SELECT 1 as sa; SQLCMDPASSWORD hands sqlcmd the password through its environment, never its command line.
answers() {
  SQLCMDPASSWORD="$(value MSSQL_SA_PASSWORD)" "$sqlcmd" -C -S "127.0.0.1,$(value DBCHANGE_SQL_PORT)" -U sa -l 5 -b \
    -Q 'SELECT 1' >/dev/null 2>&1
}

# An installed SQL Server is never replaced: one at major version 16 or later is the window R5 accepts and is kept, and an
# older one is refused, since installing over it would downgrade or upgrade a server this script did not install.
install() {
  local release had
  had="$(installed)"
  if [ -n "$had" ]; then
    [ "${had%%.*}" -ge 16 ] 2>/dev/null || fail "mssql-server $had is installed and dbchange needs SQL Server 16 (2022) or later;\
 this script installs $version only where mssql-server is absent: upgrade that server, or set DBCHANGE_SQL to another one"
    [ -x "$sqlcmd" ] && return 0
  fi
  release="$(. /etc/os-release && echo "$VERSION_ID")"
  case "$release" in 22.04 | 24.04) ;; *) fail "Ubuntu $release: SQL Server 2022 is installed here on Ubuntu 22.04 or 24.04 only" ;; esac
  echo "ci/sql.sh: installing ${had:+sqlcmd beside mssql-server $had}${had:-SQL Server $version and sqlcmd}" \
    "from packages.microsoft.com" >&2
  local keyring=/usr/share/keyrings/microsoft-prod.gpg repo=https://packages.microsoft.com
  if [ ! -f "$keyring" ]; then
    # apt verifies the pinned package against this key, so the key is trusted only with Microsoft's release-signing fingerprint.
    local asc held
    asc="$(curl -fsSL "$repo/keys/microsoft.asc")"
    held="$(GNUPGHOME="$(mktemp -d)" gpg --show-keys --with-colons <<<"$asc" 2>/dev/null | sed -n 's/^fpr:*\([0-9A-F]*\):$/\1/p')"
    [ "$held" = "$microsoft_key" ] || fail "$repo/keys/microsoft.asc holds the key ${held:-none}, not $microsoft_key, Microsoft's\
 release-signing key; nothing was installed"
    gpg --dearmor <<<"$asc" | root tee "$keyring" >/dev/null
  fi
  echo "deb [arch=amd64 signed-by=$keyring] $repo/ubuntu/22.04/mssql-server-2022 jammy main" |
    root tee /etc/apt/sources.list.d/mssql-server-2022.list >/dev/null
  echo "deb [arch=amd64 signed-by=$keyring] $repo/ubuntu/$release/prod $(. /etc/os-release && echo "$VERSION_CODENAME") main" |
    root tee /etc/apt/sources.list.d/microsoft-prod.list >/dev/null
  root apt-get update -q >&2
  if [ -n "$had" ]; then
    root env DEBIAN_FRONTEND=noninteractive ACCEPT_EULA=Y apt-get install -y -q mssql-tools18 >&2
    return 0
  fi
  if [ "$release" = 24.04 ] && ! dpkg-query -W libldap-2.5-0 >/dev/null 2>&1; then
    local deb
    deb="$(mktemp -d)/libldap-2.5-0.deb"
    curl -fsSL "$ldap" -o "$deb"
    echo "$ldap_sha256  $deb" | sha256sum -c --quiet - || fail "$ldap does not have the pinned SHA-256 $ldap_sha256"
    root env DEBIAN_FRONTEND=noninteractive apt-get install -y -q "$deb" >&2
  fi
  root env DEBIAN_FRONTEND=noninteractive ACCEPT_EULA=Y apt-get install -y -q "mssql-server=$version" mssql-tools18 >&2
}

# sqlservr runs in its own session as the mssql user, so it outlives this script. Its settings come from its environment, as in the
# documented MSSQL_* variables; the SA password sets the sa login when sqlservr first creates master, and is ignored after. Every
# other variable of the caller's environment is unset first, so sqlservr holds no token or key of the session that started it
# (VALUES.md X1), and the password reaches it through the environment alone, never a command line.
start() {
  local keep=ACCEPT_EULA,MSSQL_PID,MSSQL_SA_PASSWORD,MSSQL_TCP_PORT,MSSQL_IP_ADDRESS
  : > "$log"
  (
    local password port name
    password="$(value MSSQL_SA_PASSWORD)" port="$(value DBCHANGE_SQL_PORT)"
    for name in $(compgen -e); do unset "$name" 2>/dev/null || true; done
    export PATH=/usr/sbin:/usr/bin:/sbin:/bin HOME=/var/opt/mssql ACCEPT_EULA=Y MSSQL_PID=Express MSSQL_IP_ADDRESS=127.0.0.1
    export MSSQL_SA_PASSWORD="$password" MSSQL_TCP_PORT="$port"
    cd /
    if [ "$(id -u)" = 0 ]; then
      setsid setpriv --reuid=mssql --regid=mssql --init-groups -- "$sqlservr" >>"$log" 2>&1 </dev/null &
    else
      setsid sudo --preserve-env="$keep" -u mssql -- "$sqlservr" >>"$log" 2>&1 </dev/null &
    fi
  )
}

up() {
  lock
  # A SQL Server installed or running while sql.env holds no password was set up by something else, with an SA password and a port
  # this script does not know; writing a new password would not change its sa login, and up would wait three minutes to say so.
  local had found
  had="$(installed)"
  if [ -n "$had" ]; then found="mssql-server $had is installed"; else found="sqlservr is running"; fi
  if [ -z "$(value MSSQL_SA_PASSWORD)" ] && { [ -n "$had" ] || running; }; then
    fail "$found and $env holds no SA password, so this script did not\
 set that server up: set DBCHANGE_SQL to a connection string for it, or stop and remove it (apt-get purge mssql-server)\
 and run ci/sql.sh up again"
  fi
  install
  if [ -z "$(value MSSQL_SA_PASSWORD)" ]; then
    # umask sets the mode of a new file; chmod sets that of one an earlier run wrote, so its owner alone reads it either way.
    (umask 077; printf 'MSSQL_SA_PASSWORD=%s\nDBCHANGE_SQL_PORT=%s\n' "Est!$(od -An -tx1 -N16 /dev/urandom | tr -d ' \n')" \
      "${DBCHANGE_SQL_PORT:-11434}" > "$env")
  fi
  chmod 600 "$env"
  running || start
  for _ in $(seq 1 90); do
    if answers; then
      echo "SQL Server $(installed) on 127.0.0.1,$(value DBCHANGE_SQL_PORT); the SA password is in $env"
      return 0
    fi
    running || { tail -n 20 "$log" >&2; fail "sqlservr stopped; the last lines of $log are above"; }
    sleep 2
  done
  fail "SQL Server on 127.0.0.1,$(value DBCHANGE_SQL_PORT) did not answer sa with the password in $env within three minutes;\
 if another program holds the port: ci/sql.sh down, then DBCHANGE_SQL_PORT=<a free port> in $env, then ci/sql.sh up"
}

down() {
  lock
  if running && [ -x "$sqlcmd" ]; then
    SQLCMDPASSWORD="$(value MSSQL_SA_PASSWORD)" "$sqlcmd" -C -S "127.0.0.1,$(value DBCHANGE_SQL_PORT)" -U sa -l 5 \
      -Q 'SHUTDOWN' >/dev/null 2>&1 || true
  fi
  for _ in $(seq 1 30); do running || break; sleep 1; done
  if running; then root pkill -u mssql -x sqlservr || true; fi
  echo "sqlservr: stopped; its databases and $env stay for the next up"
}

# A command, not the secret: it reads the password from the file only when it is evaluated, so no output of
# this script, captured or not, ever holds it.
conn() {
  [ -n "$(value MSSQL_SA_PASSWORD)" ] || fail "$env holds no password: ci/sql.sh up first"
  local reader="sed -n 's/^MSSQL_SA_PASSWORD=//p' '$env' | tr -d '\\r'"
  printf 'export DBCHANGE_SQL="Server=127.0.0.1,%s;User ID=sa;TrustServerCertificate=True;Password=$(%s)"\n' \
    "$(value DBCHANGE_SQL_PORT)" "$reader"
}

case "${1:-}" in
  up | down | conn) "$1" ;;
  *) fail "usage: ci/sql.sh up | down | conn" ;;
esac
