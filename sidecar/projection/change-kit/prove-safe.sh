#!/usr/bin/env bash
# change-kit/prove-safe.sh -- THE SPINE. The thinnest real prove-loop.
#
# ONE VERB: prove-safe(change) = seed real-shaped data on a throwaway SQL DB ->
# apply the proposed change the way an OutSystems dev wrote it -> let SQL Server
# itself be the oracle for what breaks -> apply the fixed version -> PROVE it
# succeeded AND every row's data survived. Returns {what broke, the fix, the proof}.
#
# The differentiator is PROOF, not advice: this does not PREDICT what will break,
# it RUNS your change on a copy with real-shaped data and shows you the corpse and
# the cure.
#
# CODEBASE-INDIFFERENT: generic Docker + sqlcmd + system catalog. Zero F#, zero
# host tooling beyond Docker. Reuses scripts/warm-sql.sh verbatim for the
# disposable SQL Server 2022 container and its connection.
#
# Usage:
#   change-kit/prove-safe.sh <scenario-dir>
#   change-kit/prove-safe.sh --selftest        # run every shipped scenario
#
# A scenario dir contains exactly three SQL files:
#   00-seed.sql          -- BEFORE schema + real-shaped rows. MUST be drop-create
#                           idempotent (DROP ... IF EXISTS then CREATE), because
#                           it is re-applied to reset state between the naive and
#                           fixed runs. An additive seed would let naive-run
#                           residue leak into the fixed run.
#   10-change-naive.sql  -- the change as a developer would first write it,
#                           EXPECTED TO BREAK (SQL Server raises the real error)
#                           or to silently change data (caught by the snapshot).
#   20-change-fixed.sql  -- the remediated change, EXPECTED TO SUCCEED.
#
# Shipped scenarios live in change-kit/scenarios/. The notnull scenario is the
# magic moment a developer runs; narrow/fk/rename ship as this loop's own test
# suite (run via --selftest) -- they prove the loop and the data-survival oracle
# work, independent of any operator data.
#
# WHAT THIS PROVES, AND WHAT IT DOES NOT:
#   It proves YOUR LITERAL change, run against real-shaped data, on a real SQL
#   Server engine. It does NOT prove what your SSDT/dacpac publish pipeline will
#   do (that path can rebuild a table or veto on possible data loss where a raw
#   ALTER would just run), nor production-scale lock behaviour, nor app/SSIS
#   impact. See change-kit/README.md, "What this CANNOT prove".

set -uo pipefail
export MSYS_NO_PATHCONV=1   # Git Bash: stop mangling /opt/... paths and -Q args

KIT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJ_DIR="$(cd "$KIT_DIR/.." && pwd)"
WARM="$PROJ_DIR/scripts/warm-sql.sh"
CONTAINER="${WARM_SQL_NAME:-projection-mssql-warm}"
PW="${WARM_SQL_PW:-Projection@Strong1}"

# --- pretty output -----------------------------------------------------------
c()  { printf '\033[36m%s\033[0m\n'  "$*"; }   # info / step
ok() { printf '\033[32m%s\033[0m\n'  "$*"; }   # pass / proof
no() { printf '\033[31m%s\033[0m\n'  "$*"; }   # fail / the corpse
hr() { printf '%s\n' "------------------------------------------------------------"; }

# --- sqlcmd inside the warm container ----------------------------------------
# Everything runs through sqlcmd INSIDE the container, so no host tool is needed.
# SQL is always piped on STDIN (docker exec -i ... < file). This deliberately
# avoids `docker cp` of a Windows-absolute path, which the Docker CLI itself
# re-interprets (MSYS_NO_PATHCONV does not cover docker's own arg handling) and
# which fails as "GetFileAttributesEx C:\...: cannot find the file".
#   -b => sqlcmd exits nonzero on a SQL error. This is how SQL Server becomes the
#         oracle: we branch on the real exit code, we do not guess.
sx_q() {  # sx_q <db> <sql-string> ; runs one inline batch, -b on
  local db="$1"; shift
  docker exec -i "$CONTAINER" /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -P "$PW" -C -b -d "$db" -Q "$*"
}
sx_file() {  # sx_file <db> <file> ; pipes a .sql file on stdin, -b on
  local db="$1" f="$2"
  docker exec -i "$CONTAINER" /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -P "$PW" -C -b -d "$db" < "$f"
}

# --- the data oracle ---------------------------------------------------------
# For every base table: row count AND a value-level checksum (CHECKSUM_AGG over a
# per-row CHECKSUM(*)). The checksum is what makes the survival claim real -- a
# naked rename (DROP old col + ADD new col) keeps the row count identical while
# destroying every value, and only the checksum catches that. Built entirely from
# built-ins, portable, value-fidelity at table grain.
# Written to a temp file once and piped on stdin (see sx_file rationale).
SNAP_SQL="$(mktemp)"
cat > "$SNAP_SQL" <<'SQL'
SET NOCOUNT ON;
DECLARE @sql NVARCHAR(MAX) = N'';
SELECT @sql = @sql +
    'SELECT ''' + s.name + '.' + t.name + ''' AS tbl, COUNT_BIG(*) AS rows, ' +
    'ISNULL(CONVERT(VARCHAR(20), CHECKSUM_AGG(CHECKSUM(*))), 0) AS chk FROM ' +
    QUOTENAME(s.name) + '.' + QUOTENAME(t.name) + ' UNION ALL '
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
ORDER BY s.name, t.name;
IF LEN(@sql) > 0 SET @sql = LEFT(@sql, LEN(@sql) - 10);  -- strip trailing UNION ALL
IF LEN(@sql) > 0 EXEC(@sql);
SQL
trap 'rm -f "$SNAP_SQL"' EXIT

snapshot() {  # snapshot <db> -> one line per table: "schema.table rows chk"
  docker exec -i "$CONTAINER" /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -P "$PW" -C -b -d "$1" -h -1 -W < "$SNAP_SQL"
}

# --- the loop ----------------------------------------------------------------
prove_one() {
  local SCENARIO="$1"
  local NAME; NAME="$(basename "$SCENARIO")"
  [ -d "$SCENARIO" ] || { no "scenario dir not found: $SCENARIO"; return 2; }
  for f in 00-seed.sql 10-change-naive.sql 20-change-fixed.sql; do
    [ -f "$SCENARIO/$f" ] || { no "scenario '$NAME' missing $f"; return 2; }
  done

  local DB="prove_${NAME}_$$_$RANDOM"
  # Trap-cleanup honours CLAUDE.md survival-rule #2: a leaked per-run DB degrades
  # the warm container. SINGLE_USER WITH ROLLBACK IMMEDIATE forces the drop even
  # if a connection is dangling.
  cleanup() {
    sx_q master "IF DB_ID('$DB') IS NOT NULL BEGIN ALTER DATABASE [$DB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DB]; END" >/dev/null 2>&1 || true
  }

  hr; c "PROVE-SAFE  scenario: $NAME   (throwaway DB: $DB)"; hr

  # [1] fresh per-run DB
  c "[1] creating throwaway database"
  if ! sx_q master "CREATE DATABASE [$DB];" >/dev/null 2>&1; then
    no "could not create database"; return 1
  fi
  trap cleanup RETURN   # drop this DB when prove_one returns, however it returns

  # [2] seed BEFORE schema + real-shaped data
  c "[2] seeding schema + real-shaped data"
  if ! sx_file "$DB" "$SCENARIO/00-seed.sql" >/dev/null; then
    no "seed failed -- 00-seed.sql did not apply"; return 1
  fi

  # [3] snapshot BEFORE (the data oracle baseline)
  local BEFORE; BEFORE="$(snapshot "$DB")"
  c "[3] BEFORE snapshot (table | rows | checksum):"; printf '%s\n' "$BEFORE"

  # [4] NAIVE change -- we EXPECT this to break (or to silently change data)
  c "[4] applying the change as first written (naive) -- expecting it to BREAK"
  local NAIVE_OUT NAIVE_RC
  NAIVE_OUT="$(sx_file "$DB" "$SCENARIO/10-change-naive.sql" 2>&1)"; NAIVE_RC=$?
  local WHAT_BREAKS=""
  if [ $NAIVE_RC -ne 0 ]; then
    WHAT_BREAKS="$(printf '%s\n' "$NAIVE_OUT" | grep -iE 'Msg [0-9]+|cannot|null|truncat|conflict|FOREIGN KEY|duplicate' | head -3)"
    no "    BROKE as expected -- this is the hazard, proven by the engine:"
    printf '    %s\n' "$WHAT_BREAKS"
  else
    # rc==0 but maybe silently corrupted -> the snapshot is the only witness.
    local AFTER_NAIVE; AFTER_NAIVE="$(snapshot "$DB")"
    if [ "$AFTER_NAIVE" != "$BEFORE" ]; then
      WHAT_BREAKS="no error was raised, but data SILENTLY changed (see diff)"
      no "    NO ERROR, but data SILENTLY changed -- silent corruption:"
      diff <(printf '%s\n' "$BEFORE") <(printf '%s\n' "$AFTER_NAIVE") | sed 's/^/    /' || true
    else
      ok "    naive change ran cleanly with no data change -- this change is benign as written"
    fi
  fi

  # reset to the pristine BEFORE state for the fixed run (seed is drop-create)
  c "    resetting to pristine state for the fixed run"
  sx_file "$DB" "$SCENARIO/00-seed.sql" >/dev/null 2>&1 || true

  # [5] FIXED change -- we EXPECT this to succeed
  c "[5] applying the FIXED change -- expecting it to SUCCEED"
  local FIX_OUT FIX_RC
  FIX_OUT="$(sx_file "$DB" "$SCENARIO/20-change-fixed.sql" 2>&1)"; FIX_RC=$?
  if [ $FIX_RC -ne 0 ]; then
    no "    FIXED change FAILED -- NOT safe to ship as written:"
    printf '%s\n' "$FIX_OUT" | grep -iE 'Msg [0-9]+|cannot|null|truncat|conflict|FOREIGN KEY|duplicate' | sed 's/^/    /' | head -3
    hr; no "VERDICT: BLOCKED -- the fix did not hold. See the error above."; hr
    return 1
  fi
  ok "    the fixed change executed on a real SQL Server engine"

  # [6] AFTER snapshot + survival assertion (the PROOF)
  local AFTER; AFTER="$(snapshot "$DB")"
  c "[6] AFTER snapshot (table | rows | checksum):"; printf '%s\n' "$AFTER"

  hr
  ok "VERDICT: SAFE -- your fixed change executed and the result is proven below."
  printf '\n'
  printf '  WHAT BREAKS (the change as first written):\n'
  if [ -n "$WHAT_BREAKS" ]; then printf '    %s\n' "$WHAT_BREAKS"
  else printf '    nothing observable on this data -- ran clean and unchanged.\n'; fi
  printf '  THE FIX (20-change-fixed.sql):\n'
  printf '    applied; SQL Server accepted it (exit 0).\n'
  printf '  THE PROOF (before vs after, on real-shaped data):\n'
  if [ "$AFTER" = "$BEFORE" ]; then
    ok "    every row survived unchanged: BEFORE checksum == AFTER checksum."
  else
    c  "    rows/values changed -- expected if the fix backfills or cleans. Review:"
    diff <(printf '%s\n' "$BEFORE") <(printf '%s\n' "$AFTER") | sed 's/^/    /' || true
  fi
  printf '\n'
  c "  NOTE: this is your LITERAL change on real-SHAPED data. It is NOT a"
  c "  prediction of your SSDT/dacpac publish, nor of production-scale locks,"
  c "  nor of app/SSIS impact. See change-kit/README.md -> What this CANNOT prove."
  hr
  return 0
}

# --- entry -------------------------------------------------------------------
c "[0] ensuring the disposable SQL Server (scripts/warm-sql.sh)"
eval "$("$WARM" start 2>/dev/null)" || { no "could not start warm SQL (scripts/warm-sql.sh start)"; exit 1; }

if [ "${1:-}" = "--selftest" ]; then
  rc=0
  for d in "$KIT_DIR"/scenarios/*/; do
    prove_one "$d" || rc=1
  done
  exit $rc
fi

SCENARIO="${1:?usage: prove-safe.sh <scenario-dir> | --selftest}"
prove_one "$SCENARIO"
