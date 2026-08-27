#!/usr/bin/env bash
# THE ESTATE KIT — shared bash plumbing (sourced by the kit scripts; not run).
#
# The bash lane targets a CONTAINERIZED local engine: sqlcmd runs inside the
# container (the host needs none installed), file transfer rides docker cp,
# and sqlpackage runs on the host against the mapped port. The PowerShell
# twins target native Windows engines (LocalDB / Developer) with native
# sqlcmd. Both lanes print the same verdict lines.
#
# Defaults mirror the Twin's own local conventions (TwinConfig.fs):
#   container twin-mssql · port 21433 · sa / the documented local default.

KIT_CONTAINER="${KIT_SQL_CONTAINER:-twin-mssql}"
KIT_SERVER="${KIT_SQL_SERVER:-localhost,21433}"
KIT_PASSWORD="${KIT_SQL_PASSWORD:-Twin@Strong1}"

kit_log()  { printf '\033[36m[estate-kit]\033[0m %s\n' "$1" >&2; }
kit_pass() { printf '\033[32m[estate-kit]\033[0m PASS — %s\n' "$1"; }
kit_fail() { printf '\033[31m[estate-kit]\033[0m FAIL — %s\n' "$1"; KIT_FAILURES=$((${KIT_FAILURES:-0}+1)); }
kit_die()  { printf '\033[31m[estate-kit]\033[0m %s\n' "$1" >&2; exit 1; }

# sqlcmd inside the container. Usage: kit_sql [-d db] -Q "<sql>"  (or -i file via stdin).
kit_sql() {
    docker exec -i "$KIT_CONTAINER" /opt/mssql-tools18/bin/sqlcmd \
        -S localhost -U sa -P "$KIT_PASSWORD" -C "$@"
}

# One scalar, headerless and trimmed.
kit_scalar() {
    kit_sql -h -1 -W "$@" | tr -d '\r' | awk 'NF { print; exit }'
}

# The template's identity, read from the restored copy's own state row.
kit_identity() {
    local db="$1"
    kit_scalar -d "$db" -Q "SET NOCOUNT ON; SELECT CONCAT('commit=', ISNULL(TemplateCommit, '(unstamped)'), ' baked=', ISNULL(TemplateBakedAtUtc, '-'), ' data=', LEFT(ISNULL(DataFingerprint, '-'), 8)) FROM [twin].[__state];"
}

# Order-independent content digest over every user table — the no-op
# comparator (counts + BINARY_CHECKSUM aggregate; the stricter per-row
# SHA2 form is the proving loop's own tool, not the kit smoke's).
kit_digest() {
    local db="$1"
    kit_scalar -d "$db" -Q "SET NOCOUNT ON;
DECLARE @acc BIGINT = 0, @sql NVARCHAR(MAX);
DECLARE @t TABLE (id INT IDENTITY, qn NVARCHAR(512));
INSERT INTO @t (qn)
SELECT QUOTENAME(s.name) + '.' + QUOTENAME(t.name)
FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name NOT IN ('twin') ORDER BY s.name, t.name;
DECLARE @i INT = 1, @n INT = (SELECT COUNT(*) FROM @t), @qn NVARCHAR(512), @one BIGINT;
WHILE @i <= @n
BEGIN
    SELECT @qn = qn FROM @t WHERE id = @i;
    SET @sql = N'SELECT @r = ISNULL(SUM(CONVERT(BIGINT, BINARY_CHECKSUM(*))), 0) + COUNT_BIG(*) FROM ' + @qn;
    EXEC sp_executesql @sql, N'@r BIGINT OUTPUT', @r = @one OUTPUT;
    SET @acc = @acc ^ @one;
    SET @i = @i + 1;
END
SELECT CONVERT(NVARCHAR(32), @acc);"
}

# The newest template pair in a directory. Sets KIT_TEMPLATE_BAK/_MANIFEST.
kit_newest_template() {
    local dir="$1"
    KIT_TEMPLATE_BAK="$(ls -t "$dir"/twin-template-*.bak 2>/dev/null | head -1 || true)"
    [ -n "$KIT_TEMPLATE_BAK" ] || kit_die "no twin-template-*.bak under $dir"
    KIT_TEMPLATE_MANIFEST="${KIT_TEMPLATE_BAK%.bak}.manifest.json"
    [ -f "$KIT_TEMPLATE_MANIFEST" ] || kit_die "no manifest beside $(basename "$KIT_TEMPLATE_BAK")"
}

# Verify a template pair: the manifest's sha256 and byte count against the
# artifact. Refuses on mismatch — a torn copy never restores.
kit_verify_template() {
    local bak="$1" manifest="$2"
    local expected actual bytes
    expected="$(node -e 'process.stdout.write(JSON.parse(require("fs").readFileSync(process.argv[1], "utf8")).artifact.sha256)' "$manifest")"
    actual="$(sha256sum "$bak" | awk '{print $1}')"
    [ "$expected" = "$actual" ] || kit_die "sha256 mismatch on $(basename "$bak"): manifest $expected, artifact $actual"
    bytes="$(node -e 'process.stdout.write(String(JSON.parse(require("fs").readFileSync(process.argv[1], "utf8")).artifact.bytes))' "$manifest")"
    [ "$bytes" = "$(stat -c %s "$bak" 2>/dev/null || stat -f %z "$bak")" ] || kit_die "byte-count mismatch on $(basename "$bak")"
}

# Restore a template .bak (host path) into the container as $2. Idempotent:
# drop-if-exists first (PROTOCOL discipline), logical names read from the
# backup itself. `docker cp` lands the file root-owned, which the engine's
# mssql user cannot read (OS error 5) — the chown is load-bearing.
kit_restore() {
    local bak="$1" db="$2"
    local base
    base="$(basename "$bak")"
    docker cp "$bak" "$KIT_CONTAINER:/var/opt/mssql/backup-in-$base" >/dev/null
    docker exec -u root "$KIT_CONTAINER" chown mssql "/var/opt/mssql/backup-in-$base"
    kit_sql -Q "IF DB_ID(N'$db') IS NOT NULL BEGIN ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$db]; END;" >/dev/null
    local names data_logical log_logical
    names="$(kit_sql -h -1 -W -s '|' -Q "SET NOCOUNT ON; RESTORE FILELISTONLY FROM DISK = N'/var/opt/mssql/backup-in-$base';" | tr -d '\r' | awk -F'|' 'NF { print $1 "|" $3 }')"
    data_logical="$(printf '%s\n' "$names" | awk -F'|' '$2 == "D" { print $1; exit }')"
    log_logical="$(printf '%s\n' "$names" | awk -F'|' '$2 == "L" { print $1; exit }')"
    [ -n "$data_logical" ] && [ -n "$log_logical" ] || kit_die "could not read the backup's logical file names"
    kit_sql -Q "RESTORE DATABASE [$db] FROM DISK = N'/var/opt/mssql/backup-in-$base' WITH MOVE N'$data_logical' TO N'/var/opt/mssql/data/$db.mdf', MOVE N'$log_logical' TO N'/var/opt/mssql/data/$db.ldf';" >/dev/null
    docker exec -u root "$KIT_CONTAINER" rm -f "/var/opt/mssql/backup-in-$base"
}
