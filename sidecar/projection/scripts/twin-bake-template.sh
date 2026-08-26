#!/usr/bin/env bash
# THE TWIN — the template bake (PROVING_SURFACE_DESIGN §5.2, plan slice C9).
#
# Freezes the BEFORE state the proving loop restores from: converge the twin
# at the estate head, mint (from the crossover-merged evidence when the
# config names merge inputs; from shape/defaults otherwise), plant and
# assert the witness pass, run the per-environment fidelity audit as the
# hard gate, stamp the template identity into [twin].[__state], BACKUP WITH
# COMPRESSION, and land the .bak beside a manifest naming everything the
# artifact depends on. The Azure DevOps nightly runs this same script; the
# GitHub bake-check runs it against the sample estate so the mechanic stays
# proven where it is developed.
#
#   usage: twin-bake-template.sh [--image] [estate-root]
#     --image      also build the image rendition: the .bak wrapped in the
#                  bake engine's own image with a restore-on-first-start
#                  entrypoint, tagged twin-template:<lane>-<commit8>-<dataFp8>.
#                  A Docker Desktop machine then runs the template with one
#                  command and no restore step:
#                    docker run -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=<pw> \
#                      -p 21433:1433 twin-template:<tag>
#     estate-root  directory carrying twin.json (default: ssdt-agent/proving-ground)
#   env:
#     TWIN_BAKE_LANE  manifest lane label (default: sample-estate)
#     TWIN_SQL_PW     twin container SA password (default: the documented local default)
#
# Refusals are loud and ordered: a configured-but-missing merge input, a
# witness assertion failure, or ANY blocking fidelity-audit verdict fails
# the bake before a byte is backed up.
set -euo pipefail

cd "$(dirname "$0")/.."

BAKE_IMAGE=false
POSITIONAL=""
for arg in "$@"; do
    case "$arg" in
        --image) BAKE_IMAGE=true ;;
        *) POSITIONAL="$arg" ;;
    esac
done
ROOT="${POSITIONAL:-ssdt-agent/proving-ground}"
LANE="${TWIN_BAKE_LANE:-sample-estate}"
CONFIG="$ROOT/twin.json"

log() { printf '\033[36m[twin-bake]\033[0m %s\n' "$1" >&2; }
die() { printf '\033[31m[twin-bake]\033[0m %s\n' "$1" >&2; exit 1; }

[ -f "$CONFIG" ] || die "no twin.json at $CONFIG"

# ---------------------------------------------------------------------------
# 0 — substrate: the docker daemon (single source of truth: warm-sql.sh).
# ---------------------------------------------------------------------------
bash scripts/warm-sql.sh daemon || die "docker daemon unavailable"

# The container coordinates and evidence paths, read from twin.json with the
# TwinConfig defaults mirrored (TwinConfig.fs: DefaultContainerName twin-mssql,
# DefaultPort 21433; the witness pair lands beside the rich pack unless
# evidence.merge.witness overrides).
CFG_JSON="$(node -e '
  const fs = require("fs");
  const c = JSON.parse(fs.readFileSync(process.argv[1], "utf8"));
  const container = c.container ?? {};
  const evidence = c.evidence ?? {};
  const merge = evidence.merge ?? null;
  const rich = evidence.rich ?? null;
  const witnessDir = merge && merge.witness ? merge.witness
    : rich ? require("path").dirname(rich) : null;
  process.stdout.write(JSON.stringify({
    name: container.name ?? "twin-mssql",
    port: container.port ?? 21433,
    merge: merge ? (merge.inputs ?? []) : null,
    report: merge ? (merge.report ?? "twin/evidence-merge.report.json") : null,
    witnessDir
  }));
' "$CONFIG")"
CNAME="$(node -e 'process.stdout.write(JSON.parse(process.argv[1]).name)' "$CFG_JSON")"
PW="${TWIN_SQL_PW:-Twin@Strong1}"
SQLCMD=(docker exec -i "$CNAME" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PW" -C -d twin)
firstRow() { tr -d '\r' | awk 'NF { print; exit }'; }

TWIN=(dotnet run --project src/Twin.Cli --)
# The estate root may arrive absolute (the ADO lane points at a sibling
# repository checkout) or repo-relative (the local and GitHub lanes).
case "$CONFIG" in
    /*) export TWIN_CONFIG="$CONFIG" ;;
    *)  export TWIN_CONFIG="$PWD/$CONFIG" ;;
esac

# ---------------------------------------------------------------------------
# 1 — converge at the estate head, then the crossover when configured, then
# a fresh deterministic mint (the merged pack sits at evidence.rich, where
# the mint already looks — zero mint changes).
# ---------------------------------------------------------------------------
log "twin up (converge at head)"
"${TWIN[@]}" up

MERGED=false
WITNESS_PLANNED=""
WITNESS_SKIPPED=""
if [ "$(node -e 'process.stdout.write(JSON.parse(process.argv[1]).merge === null ? "no" : "yes")' "$CFG_JSON")" = "yes" ]; then
    log "twin evidence merge (crossover)"
    MERGE_OUT="$("${TWIN[@]}" evidence merge)"
    printf '%s\n' "$MERGE_OUT" >&2
    MERGED=true
    WITNESS_PLANNED="$(printf '%s\n' "$MERGE_OUT" | sed -n 's/^ *\([0-9][0-9,]*\) witnesses planned.*/\1/p' | tr -d ',')"
    WITNESS_SKIPPED="$(printf '%s\n' "$MERGE_OUT" | sed -n 's/.*planned; \([0-9][0-9,]*\) skipped.*/\1/p' | tr -d ',')"
fi

log "twin seed (deterministic mint)"
"${TWIN[@]}" seed

# ---------------------------------------------------------------------------
# 2 — the witness pass: plant the recorded realities, then assert every one
# landed. The pair lives beside the rich pack (out of repo).
# ---------------------------------------------------------------------------
WITNESS_FAILURES=0
WITNESS_SQL_SHA=""
if [ "$MERGED" = true ]; then
    WITNESS_DIR="$(node -e 'process.stdout.write(JSON.parse(process.argv[1]).witnessDir ?? "")' "$CFG_JSON")"
    WITNESS_SQL="$ROOT/$WITNESS_DIR/witness.sql"
    WITNESS_ASSERT="$ROOT/$WITNESS_DIR/witness.assert.sql"
    [ -f "$WITNESS_SQL" ] || die "the merge ran but $WITNESS_SQL is absent"
    log "planting witnesses ($WITNESS_SQL)"
    "${SQLCMD[@]}" -i /dev/stdin < "$WITNESS_SQL" >/dev/null
    log "asserting witnesses"
    ASSERT_OUT="$({ echo "SET NOCOUNT ON;"; cat "$WITNESS_ASSERT"; } | "${SQLCMD[@]}" -i /dev/stdin -h -1 -W | tr -d '\r')"
    WITNESS_FAILURES="$(printf '%s\n' "$ASSERT_OUT" | awk 'NF{last=$0} END{print last}')"
    case "$WITNESS_FAILURES" in
        ''|*[!0-9]*) die "the witness assertion output did not end in a failures count: $ASSERT_OUT" ;;
    esac
    if [ "$WITNESS_FAILURES" -gt 0 ]; then
        printf '%s\n' "$ASSERT_OUT" >&2
        die "$WITNESS_FAILURES witness assertion(s) did not land; the template is not distributable"
    fi
    WITNESS_SQL_SHA="$(sha256sum "$WITNESS_SQL" | awk '{print $1}')"
    log "witnesses landed (0 failures)"
fi

# ---------------------------------------------------------------------------
# 3 — the per-environment fidelity audit: the template must be at least as
# blocking as EVERY captured environment. Any blocking verdict fails the
# bake; the advisory margins ride into the manifest.
# ---------------------------------------------------------------------------
AUDIT_JSON="null"
if [ "$MERGED" = true ]; then
    log "twin evidence audit (the operator-reality gate)"
    "${TWIN[@]}" evidence audit >&2
    AUDIT_REPORT="$ROOT/twin/evidence-audit.report.json"
    [ -f "$AUDIT_REPORT" ] || die "the audit ran but $AUDIT_REPORT is absent"
    AUDIT_FAILURES="$(node -e '
      const r = JSON.parse(require("fs").readFileSync(process.argv[1], "utf8"));
      process.stdout.write(String(r.sections.reduce((n, s) => n + s.failures, 0)));
    ' "$AUDIT_REPORT")"
    if [ "$AUDIT_FAILURES" -gt 0 ]; then
        die "the fidelity audit found $AUDIT_FAILURES blocking failure(s); re-merge and re-bake before distributing"
    fi
    AUDIT_JSON="$(node -e '
      const r = JSON.parse(require("fs").readFileSync(process.argv[1], "utf8"));
      process.stdout.write(JSON.stringify(r.sections.map(s => ({
        source: s.source, failures: s.failures, advisories: s.advisories }))));
    ' "$AUDIT_REPORT")"
    log "audit clean (0 blocking failures)"
fi

# ---------------------------------------------------------------------------
# 4 — identity: the fingerprints from [twin].[__state], the estate commit
# stamped back into it so any restored copy can answer which base it is.
# ---------------------------------------------------------------------------
COMMIT="$(git rev-parse HEAD)"
COMMIT8="$(git rev-parse --short=8 HEAD)"
BAKED_AT="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

STATE_ROW="$("${SQLCMD[@]}" -h -1 -W -s '|' -Q "SET NOCOUNT ON; SELECT ISNULL(SchemaFingerprint,''), ISNULL(DataFingerprint,''), ISNULL(Scenario,''), ISNULL(CAST(Seed AS NVARCHAR(32)),'') FROM [twin].[__state];" | firstRow)"
SCHEMA_FP="$(printf '%s' "$STATE_ROW" | cut -d'|' -f1)"
DATA_FP="$(printf '%s' "$STATE_ROW" | cut -d'|' -f2)"
SCENARIO="$(printf '%s' "$STATE_ROW" | cut -d'|' -f3)"
SEED="$(printf '%s' "$STATE_ROW" | cut -d'|' -f4)"
[ -n "$SCHEMA_FP" ] && [ -n "$DATA_FP" ] || die "the twin's __state carries no fingerprints; the mint did not complete"

log "stamping template identity ($COMMIT8 / ${DATA_FP:0:8})"
"${SQLCMD[@]}" -Q "
IF COL_LENGTH('twin.__state', 'TemplateCommit') IS NULL
    ALTER TABLE [twin].[__state] ADD [TemplateCommit] NVARCHAR(64) NULL, [TemplateBakedAtUtc] NVARCHAR(32) NULL;" >/dev/null
"${SQLCMD[@]}" -Q "UPDATE [twin].[__state] SET [TemplateCommit] = N'$COMMIT', [TemplateBakedAtUtc] = N'$BAKED_AT';" >/dev/null

ENGINE_VERSION="$("${SQLCMD[@]}" -h -1 -W -Q "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('ProductVersion') AS NVARCHAR(64));" | firstRow)"
IMAGE="$(docker inspect -f '{{.Config.Image}}' "$CNAME")"

# ---------------------------------------------------------------------------
# 5 — freeze: BACKUP WITH COMPRESSION inside the container, copied out to
# the gitignored templates directory.
# ---------------------------------------------------------------------------
TEMPLATE="twin-template-${LANE}-${COMMIT8}-${DATA_FP:0:8}"
OUT_DIR="$ROOT/templates"
mkdir -p "$OUT_DIR"
log "BACKUP DATABASE [twin] -> $TEMPLATE.bak"
docker exec "$CNAME" mkdir -p /var/opt/mssql/backup
"${SQLCMD[@]}" -Q "BACKUP DATABASE [twin] TO DISK = N'/var/opt/mssql/backup/$TEMPLATE.bak' WITH COMPRESSION, INIT;" >/dev/null
docker cp "$CNAME:/var/opt/mssql/backup/$TEMPLATE.bak" "$OUT_DIR/$TEMPLATE.bak"
BYTES="$(stat -c %s "$OUT_DIR/$TEMPLATE.bak" 2>/dev/null || stat -f %z "$OUT_DIR/$TEMPLATE.bak")"
SHA="$(sha256sum "$OUT_DIR/$TEMPLATE.bak" | awk '{print $1}')"

# ---------------------------------------------------------------------------
# 6 — the image rendition (--image): the .bak wrapped in the bake engine's
# own image with a restore-on-first-start entrypoint. The tag carries the
# template identity, and the restored copy still answers its own commit
# from [twin].[__state] — the image is a delivery form, never a second
# identity. Registry distribution is deliberately out of the bake: the tag
# lands locally, and pushing it is the distribution ring's decision.
# ---------------------------------------------------------------------------
IMG_TAG=""
IMG_ID=""
if [ "$BAKE_IMAGE" = true ]; then
    IMG_TAG="twin-template:${LANE}-${COMMIT8}-${DATA_FP:0:8}"
    # The MOVE clauses bake in from the backup's own file list, read once
    # on the bake engine — the entrypoint never parses at run time.
    FILELIST="$("${SQLCMD[@]}" -h -1 -W -s '|' -Q "SET NOCOUNT ON; RESTORE FILELISTONLY FROM DISK = N'/var/opt/mssql/backup/$TEMPLATE.bak';" | tr -d '\r')"
    DATA_LOGICAL="$(printf '%s\n' "$FILELIST" | awk -F'|' '$3=="D"{print $1}')"
    LOG_LOGICAL="$(printf '%s\n' "$FILELIST" | awk -F'|' '$3=="L"{print $1}')"
    case "$DATA_LOGICAL" in ''|*$'\n'*) die "the image rendition expects exactly one data file in the backup" ;; esac
    case "$LOG_LOGICAL"  in ''|*$'\n'*) die "the image rendition expects exactly one log file in the backup" ;; esac
    CTX="$(mktemp -d "${TMPDIR:-/tmp}/twin-image-ctx.XXXXXX")"
    trap 'rm -rf "$CTX"' EXIT
    cp "$OUT_DIR/$TEMPLATE.bak" "$CTX/template.bak"
    cat > "$CTX/entrypoint.sh" <<'ENTRYPOINT'
#!/bin/bash
# THE TWIN — template-image entrypoint (generated by twin-bake-template.sh --image).
# First start restores the baked template; later starts find the twin present.
set -e
PW="${MSSQL_SA_PASSWORD:-${SA_PASSWORD:-}}"
if [ -z "$PW" ]; then
    echo "twin-template: set MSSQL_SA_PASSWORD to run this image." >&2
    exit 1
fi
/opt/mssql/bin/sqlservr &
SQLSERVR_PID=$!
SQLCMD=/opt/mssql-tools18/bin/sqlcmd
ready=false
for _ in $(seq 1 90); do
    if "$SQLCMD" -S localhost -U sa -P "$PW" -C -Q "SELECT 1;" >/dev/null 2>&1; then ready=true; break; fi
    sleep 1
done
if [ "$ready" != true ]; then
    echo "twin-template: the engine did not become ready." >&2
    exit 1
fi
PRESENT="$("$SQLCMD" -S localhost -U sa -P "$PW" -C -h -1 -W -Q "SET NOCOUNT ON; SELECT CASE WHEN DB_ID(N'twin') IS NULL THEN 0 ELSE 1 END;" | tr -d '\r' | awk 'NF{print;exit}')"
if [ "$PRESENT" = "0" ]; then
    "$SQLCMD" -S localhost -U sa -P "$PW" -C -Q "RESTORE DATABASE [twin] FROM DISK = N'/template/template.bak' WITH MOVE N'__DATA_LOGICAL__' TO N'/var/opt/mssql/data/twin.mdf', MOVE N'__LOG_LOGICAL__' TO N'/var/opt/mssql/data/twin_log.ldf';" >/dev/null
    echo "twin-template: twin restored from the baked template (__TEMPLATE__)."
else
    echo "twin-template: twin already present; the template restore was skipped."
fi
wait "$SQLSERVR_PID"
ENTRYPOINT
    sed -e "s|__DATA_LOGICAL__|$DATA_LOGICAL|" -e "s|__LOG_LOGICAL__|$LOG_LOGICAL|" -e "s|__TEMPLATE__|$TEMPLATE|" \
        "$CTX/entrypoint.sh" > "$CTX/entrypoint.gen.sh"
    mv "$CTX/entrypoint.gen.sh" "$CTX/entrypoint.sh"
    # COPY lands root-owned and the engine runs as mssql — the chown is
    # load-bearing (the same lesson the kit's restore helper carries).
    cat > "$CTX/Dockerfile" <<DOCKERFILE
FROM $IMAGE
USER root
COPY template.bak /template/template.bak
COPY entrypoint.sh /template/entrypoint.sh
RUN chmod 0755 /template/entrypoint.sh && chown -R mssql /template
USER mssql
ENTRYPOINT ["/template/entrypoint.sh"]
DOCKERFILE
    log "docker build -> $IMG_TAG"
    docker build -t "$IMG_TAG" "$CTX" >/dev/null
    IMG_ID="$(docker images -q "$IMG_TAG")"
    rm -rf "$CTX"
    trap - EXIT
    log "image rendition built ($IMG_TAG)"
fi

# ---------------------------------------------------------------------------
# 7 — the manifest: everything the artifact's trust chain cites. Pins bake
# in from the toolchain ledger; UNPINNED rides through verbatim so a
# floating pin stays visible, never silent.
# ---------------------------------------------------------------------------
SQLPACKAGE_PIN="$(awk -F'|' '$2 ~ /^ sqlpackage $/ { gsub(/ /, "", $3); print $3; exit }' ssdt-agent/estate/toolchain.md)"
DACFX_CORPUS_PIN="$(awk -F'|' '$2 ~ /DacFx \(Twin corpus\)/ { gsub(/ /, "", $3); print $3; exit }' ssdt-agent/estate/toolchain.md)"
TOOL_VERSION="$(sed -n 's/^ *let ToolVersion = "\(.*\)"$/\1/p' src/Twin.Runtime/Runs.fs | head -1)"
REPORT_REL=""
if [ "$MERGED" = true ]; then
    REPORT_REL="$(node -e 'process.stdout.write(JSON.parse(process.argv[1]).report)' "$CFG_JSON")"
fi

TWIN_BAKE_ROOT="$ROOT" node -e '
  const fs = require("fs"); const path = require("path"); const crypto = require("crypto");
  const [out, tpl, commit, bakedAt, lane, schemaFp, dataFp, scenario, seed, engine, image,
         sqlpackagePin, dacfxPin, toolVersion, merged, cfgJson, reportRel, auditJson,
         witnessPlanned, witnessSkipped, witnessFailures, witnessSha, file, bytes, sha,
         imgTag, imgId] = process.argv.slice(1);
  const root = process.env.TWIN_BAKE_ROOT;
  let evidence = null, witness = null;
  if (merged === "true") {
    const report = fs.readFileSync(path.join(root, reportRel));
    const parsed = JSON.parse(report.toString("utf8"));
    const cfg = JSON.parse(cfgJson);
    const inputs = (cfg.merge ?? []).map(p => {
      const full = path.join(root, p);
      return { pack: p, capturedAt: fs.existsSync(full) ? fs.statSync(full).mtime.toISOString() : null };
    });
    evidence = {
      sources: parsed.inputs.map(i => i.source),
      mergeReportSha256: crypto.createHash("sha256").update(report).digest("hex"),
      inputs,
      audit: JSON.parse(auditJson)
    };
    witness = {
      planned: witnessPlanned === "" ? null : Number(witnessPlanned),
      skipped: witnessSkipped === "" ? null : Number(witnessSkipped),
      assertFailures: Number(witnessFailures),
      sqlSha256: witnessSha
    };
  }
  fs.writeFileSync(out, JSON.stringify({
    template: tpl, commit, bakedAtUtc: bakedAt, lane,
    fingerprints: { schema: schemaFp, data: dataFp },
    mint: { scenario, seed },
    engine: { sqlServer: engine, image },
    pins: { sqlpackage: sqlpackagePin, dacfxCorpus: dacfxPin, toolVersion },
    evidence, witness,
    artifact: { file, bytes: Number(bytes), sha256: sha },
    imageRendition: imgTag === "" ? null : { tag: imgTag, imageId: imgId }
  }, null, 2) + "\n");
' "$OUT_DIR/$TEMPLATE.manifest.json" "$TEMPLATE" "$COMMIT" "$BAKED_AT" "$LANE" "$SCHEMA_FP" "$DATA_FP" \
  "$SCENARIO" "$SEED" "$ENGINE_VERSION" "$IMAGE" "${SQLPACKAGE_PIN:-UNPINNED}" "${DACFX_CORPUS_PIN:-UNPINNED}" \
  "${TOOL_VERSION:-unknown}" "$MERGED" "$CFG_JSON" "$REPORT_REL" "$AUDIT_JSON" \
  "$WITNESS_PLANNED" "$WITNESS_SKIPPED" "$WITNESS_FAILURES" "$WITNESS_SQL_SHA" "$TEMPLATE.bak" "$BYTES" "$SHA" \
  "$IMG_TAG" "$IMG_ID"

# ---------------------------------------------------------------------------
# 8 — prune: the newest five templates stay; older pairs go. Under --image,
# this lane's twin-template tags with no surviving .bak go with them.
# ---------------------------------------------------------------------------
ls -t "$OUT_DIR"/*.bak 2>/dev/null | tail -n +6 | while read -r old; do
    rm -f "$old" "${old%.bak}.manifest.json"
    log "pruned $(basename "$old")"
done
if [ "$BAKE_IMAGE" = true ]; then
    docker images --format '{{.Repository}}:{{.Tag}}' twin-template 2>/dev/null | while read -r img; do
        tag="${img#twin-template:}"
        case "$tag" in
            "$LANE"-*)
                if [ ! -f "$OUT_DIR/twin-template-$tag.bak" ]; then
                    docker rmi "$img" >/dev/null 2>&1 || true
                    log "pruned image $img"
                fi ;;
        esac
    done
fi

log "template baked: $OUT_DIR/$TEMPLATE.bak ($BYTES bytes)"
log "manifest:       $OUT_DIR/$TEMPLATE.manifest.json"
if [ "$BAKE_IMAGE" = true ]; then
    log "image:          $IMG_TAG"
fi
