#!/usr/bin/env bash
# Reproduce the V1 full-export vs V2 publish parity comparison against one shared OSSYS estate.
# Prereqs: warm SQL Server 2022 on localhost:11433 (scripts/warm-sql.sh), dotnet 9.0.314.
set -euo pipefail
REPO=/home/user/outsystems-ddl-exporter
C=projection-mssql-warm; PW='Projection@Strong1'
SQLCMD="/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $PW -C -I"
CONN="Server=localhost,11433;Database=ParityEstate;User Id=sa;Password=$PW;TrustServerCertificate=True;Encrypt=False"
OUT=${1:-/tmp/parity}; mkdir -p "$OUT"/{v1,v2}

# 1) Build both engines
dotnet build "$REPO/src/Osm.Cli/Osm.Cli.csproj" -c Debug
dotnet build "$REPO/sidecar/projection/src/Projection.Cli/Projection.Cli.fsproj" -c Debug
V1=$REPO/src/Osm.Cli/bin/Debug/net9.0/Osm.Cli.dll
V2=$REPO/sidecar/projection/src/Projection.Cli/bin/Debug/net9.0/projection.dll

# 2) Seed the shared estate (v2's edge-case OSSYS seed) + deterministic rows
docker exec $C $SQLCMD -Q "IF DB_ID('ParityEstate') IS NOT NULL BEGIN ALTER DATABASE ParityEstate SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ParityEstate; END; CREATE DATABASE ParityEstate;"
docker cp "$REPO/sidecar/projection/src/Projection.Adapters.OssysSql/Resources/ossys-edge-case.seed.sql" $C:/tmp/seed.sql
docker exec $C $SQLCMD -d ParityEstate -i /tmp/seed.sql -b
docker exec $C $SQLCMD -d ParityEstate -Q "SET NOCOUNT ON; INSERT INTO dbo.OSUSR_REF_COUNTRY (CODE,NAME) VALUES ('US','United States'),('CA','Canada'),('GB','United Kingdom'); INSERT INTO dbo.OSUSR_REF_CURRENCY (CODE) VALUES ('USD'),('CAD'),('GBP'); INSERT INTO dbo.OSUSR_DEF_CITY (NAME) VALUES (N'Springfield'),(N'Shelbyville'); INSERT INTO dbo.OSUSR_ABC_CUSTOMER (EMAIL,FIRSTNAME,LASTNAME,CITYID) SELECT 'a@x.com',N'Alice',N'Smith',ID FROM dbo.OSUSR_DEF_CITY WHERE NAME=N'Springfield';"

# 3) V1 full-export (live)
printf '{ "tighteningPath": "%s/config/default-tightening.json", "supplementalModels": { "includeUsers": false } }' "$REPO" > "$OUT/cfg.v1.json"
dotnet "$V1" full-export --config "$OUT/cfg.v1.json" --connection-string "$CONN" --profiler-provider sql \
  --extract-out "$OUT/v1/model.json" --profile-out "$OUT/v1/profiles" --build-out "$OUT/v1/out" --refresh-cache

# 4) V2 publish (live)
cd "$OUT/v2"; printf '%s' "$CONN" > ossys.conn
printf '{ "model": { "ossys": "file:./ossys.conn" }, "profiler": { "provider": "live" }, "environments": { "onprem": { "access": "bundle", "out": "./dist/onprem", "grant": "schema+data", "store": "./lifecycle/onprem.json" } }, "flows": { "publish": { "from": "model", "to": "onprem" } } }' > projection.json
PROJECTION_MSSQL_CONN_STR="$CONN" dotnet "$V2" publish --json

echo "V1 tree: $OUT/v1/out   V2 tree: $OUT/v2/dist/onprem"
echo "Compare per-table DDL: diff $OUT/v1/out/Modules/AppCore/dbo.Customer.sql $OUT/v2/dist/onprem/Modules/AppCore/dbo.Customer.sql"
