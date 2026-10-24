namespace Projection.Targets.SSDT

open Projection.Core

/// Π_DOCKER — chapter 3.x slice δ_dock deliverable. Emits the
/// **Docker build context** the dev team consumes to stand up a
/// self-contained SQL Server image with the projected schema
/// pre-loaded. Per operator directive: "create a custom Docker
/// package that stands itself up with the loaded SQL server
/// inside of it ... single command up and my team doesn't have
/// to have the repository to pull the data fresh each time."
///
/// The emitter's output is a record of typed strings + bytes
/// the caller writes to a directory (`docker build .`-ready).
/// The CI / release-pipeline builds the resulting image once and
/// publishes to a registry; the dev team then `docker pull` +
/// `docker run` — no source checkout required.
///
/// Per pillar 8 (domain-first naming): `DockerImageContext`
/// names what is emitted — the **build context** that produces
/// a Docker image when fed to `docker build`. Concept-shaped;
/// the name carries the artifact's meaning (a recipe-with-
/// payload, not the image binary itself).
///
/// **A18 amended preserved structurally** — `emit : Catalog ->
/// Result<DockerImageContext>` (Catalog only; no Policy
/// parameter). The DacpacEmitter dependency is internal; the
/// dacpac bytes become a payload inside the build context.
///
/// **Pillar 7 (gold-standard library precedence) holds end-to-
/// end**: dacpac generation via `DacpacEmitter.emit` (DacFx
/// typed-AST); image base via `mcr.microsoft.com/mssql/server`
/// (Microsoft's canonical SQL Server image); deployment via
/// `sqlpackage` (Microsoft's canonical DACPAC deploy tool).
/// **Slice scope (chapter 3.x slice δ_dock):** static Dockerfile +
/// entrypoint + README templates wrapping the dacpac bytes. The
/// `DB_NAME` and base SQL Server image are pinned constants;
/// parameterization (per-Catalog database name; pinned SQL
/// Server version selection) lands in future slices when an
/// operator workflow demands it (IR-grows-under-evidence).
[<RequireQualifiedAccess>]
module DockerImageEmitter =

    /// Pass version. Bump when the build-context shape changes
    /// in a way that matters for cross-version comparators.
    [<Literal>]
    let version : int = 1

    /// Canonical relative-path leaves inside the Docker build
    /// context directory. The dev team's `docker build` invocation
    /// consumes a directory whose root contains these three files.
    [<Literal>]
    let DockerfilePath : string = "Dockerfile"

    [<Literal>]
    let DacpacPath : string = "catalog.dacpac"

    [<Literal>]
    let EntrypointPath : string = "entrypoint.sh"

    [<Literal>]
    let ReadmePath : string = "README.md"

    /// Database name SQL Server creates on first start. Matches
    /// `DacpacEmitter`'s `DefaultPackageName` so devs connecting
    /// via SSMS / Azure Data Studio find the schema under a
    /// predictable name.
    [<Literal>]
    let DefaultDatabaseName : string = "ProjectionCatalog"

    /// Base image — same pin as `Projection.Pipeline.Deploy`'s
    /// `DefaultImage`. Mirrors the canary's exact-match-production
    /// surface area (per `DECISIONS 2026-05-15`).
    [<Literal>]
    let BaseImage : string = "mcr.microsoft.com/mssql/server:2022-latest"

    /// Self-contained Docker build context the dev team consumes.
    /// `Dockerfile` + `EntrypointScript` + `Readme` are byte-
    /// deterministic across emit calls (constants for slice δ_dock);
    /// `DacpacBytes` is content-deterministic via DacFx round-trip
    /// (per the T1 amendment for binary emitters).
    type DockerImageContext =
        {
            /// Multi-line Dockerfile text. `docker build .` against
            /// a directory containing this file (plus the dacpac
            /// + entrypoint) produces a runnable image.
            Dockerfile : string
            /// `.dacpac` bytes produced by `DacpacEmitter.emit`.
            /// Embedded into the image at `/opt/projection/
            /// catalog.dacpac`; deployed at container start by
            /// the entrypoint script's `sqlpackage /Action:Publish`
            /// invocation.
            DacpacBytes : byte[]
            /// `entrypoint.sh` text. Starts SQL Server in the
            /// background, polls until ready, then publishes the
            /// bundled dacpac to the catalog database. Idempotent:
            /// re-running against an existing database validates
            /// the schema matches rather than re-creating.
            EntrypointScript : string
            /// `README.md` text. Operator-facing instructions for
            /// building, running, and connecting to the image.
            Readme : string
        }

    // -------------------------------------------------------------------
    // Static template content. The Dockerfile, entrypoint script, and
    // README are byte-deterministic constants for slice δ_dock — no
    // per-Catalog substitution. Future slices parameterize when a
    // consumer demands it (per IR-grows-under-evidence).
    // -------------------------------------------------------------------

    /// Dockerfile template. Layers:
    ///   1. Base: `mcr.microsoft.com/mssql/server:2022-latest`.
    ///   2. Install `sqlpackage` (Microsoft's canonical DACPAC deploy
    ///      tool) and `unzip` / `curl` build dependencies.
    ///   3. COPY the dacpac + entrypoint into `/opt/projection/`.
    ///   4. Drop back to the `mssql` user with `ACCEPT_EULA=Y`.
    ///   5. ENTRYPOINT delegates to the bundled script.
    let private dockerfileTemplate : string =
        """# Auto-generated by Projection.Targets.SSDT.DockerImageEmitter.
# Builds a self-contained SQL Server image with the projected
# schema pre-loaded. The dev team `docker pull`s this image from
# the registry and `docker run`s it; no source checkout required.

FROM mcr.microsoft.com/mssql/server:2022-latest

USER root

# Install sqlpackage (Microsoft's canonical DACPAC deploy tool)
# and the unzip/curl utilities needed to fetch it. The mssql-tools18
# package (containing sqlcmd, used by the entrypoint) is already
# present in the base image.
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        unzip \
        curl \
        ca-certificates \
    && mkdir -p /opt/sqlpackage \
    && curl -sSL -o /tmp/sqlpackage.zip https://aka.ms/sqlpackage-linux \
    && unzip /tmp/sqlpackage.zip -d /opt/sqlpackage \
    && chmod +x /opt/sqlpackage/sqlpackage \
    && rm -f /tmp/sqlpackage.zip \
    && rm -rf /var/lib/apt/lists/*

# Embed the dacpac + entrypoint into the image.
COPY catalog.dacpac /opt/projection/catalog.dacpac
COPY entrypoint.sh  /opt/projection/entrypoint.sh
RUN chmod +x /opt/projection/entrypoint.sh

USER mssql

ENV ACCEPT_EULA=Y
ENV MSSQL_PID=Developer
ENV PROJECTION_DB_NAME=ProjectionCatalog

EXPOSE 1433

ENTRYPOINT ["/opt/projection/entrypoint.sh"]
"""

    /// Entrypoint script. The startup sequence:
    ///   1. Start `sqlservr` in the background.
    ///   2. Poll `sqlcmd SELECT 1` until SQL Server accepts logins
    ///      (~5–30 s on a cold container; 60 s ceiling).
    ///   3. `sqlpackage /Action:Publish` deploys the dacpac (idempotent
    ///      — re-running validates the schema matches).
    ///   4. `wait` on the SQL Server PID so the container's main
    ///      process IS the SQL Server engine.
    let private entrypointTemplate : string =
        """#!/bin/bash
# Auto-generated by Projection.Targets.SSDT.DockerImageEmitter.
# Starts SQL Server in the background, waits for it to accept
# logins, then publishes the bundled DACPAC. Idempotent on
# restart — sqlpackage validates against the existing schema.
set -euo pipefail

DB_NAME="${PROJECTION_DB_NAME:-ProjectionCatalog}"
DACPAC_PATH="/opt/projection/catalog.dacpac"

if [ -z "${MSSQL_SA_PASSWORD:-}" ]; then
    echo "ERROR: MSSQL_SA_PASSWORD must be set (passed via 'docker run -e ...')." >&2
    exit 1
fi

# Start SQL Server in the background.
/opt/mssql/bin/sqlservr &
SQLPID=$!

# Wait for SQL Server to accept logins (60 s ceiling; ~5–30 s
# typical on a cold container).
echo "Waiting for SQL Server to accept logins..."
SQLCMD=/opt/mssql-tools18/bin/sqlcmd
READY=0
for _ in $(seq 1 30); do
    if "$SQLCMD" -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -Q "SELECT 1" >/dev/null 2>&1; then
        READY=1
        break
    fi
    sleep 2
done

if [ "$READY" -ne 1 ]; then
    echo "ERROR: SQL Server did not accept logins within 60 s." >&2
    kill "$SQLPID" || true
    exit 1
fi

# Publish the DACPAC. Idempotent — sqlpackage validates against
# any existing schema and only applies deltas.
echo "Publishing DACPAC to database '$DB_NAME'..."
/opt/sqlpackage/sqlpackage \
    /Action:Publish \
    /SourceFile:"$DACPAC_PATH" \
    /TargetServerName:localhost \
    /TargetDatabaseName:"$DB_NAME" \
    /TargetUser:sa \
    /TargetPassword:"$MSSQL_SA_PASSWORD" \
    /TargetTrustServerCertificate:True

echo "DACPAC published. Database '$DB_NAME' ready on port 1433."

# Hand back to SQL Server's main process so the container's
# lifecycle tracks the engine.
wait "$SQLPID"
"""

    /// Operator-facing README. Names build/run/connect for a dev
    /// arriving at the image without context.
    let private readmeTemplate : string =
        """# Projection schema image

A self-contained SQL Server image with the projected schema pre-loaded.
Auto-generated by `Projection.Targets.SSDT.DockerImageEmitter`; do not
hand-edit.

## Build

```bash
docker build -t projection-db:latest .
```

The build downloads `sqlpackage` (~100 MB) and bakes the bundled
`catalog.dacpac` into the image. Build time on a warm Docker host:
~2–5 minutes.

## Run

```bash
docker run --rm -d \
    -e MSSQL_SA_PASSWORD='YourStrong@Passw0rd' \
    -p 1433:1433 \
    --name projection-db \
    projection-db:latest
```

On first start the entrypoint waits ~5–30 s for SQL Server to accept
logins, then publishes the DACPAC. Database `ProjectionCatalog` is
ready on port 1433.

## Connect

| Tool | Command |
|---|---|
| sqlcmd | `sqlcmd -S localhost,1433 -U sa -P 'YourStrong@Passw0rd' -d ProjectionCatalog -C` |
| SSMS | Server: `localhost,1433`; Auth: SQL; User: `sa`; Database: `ProjectionCatalog` |
| Azure Data Studio | Same as SSMS |

## Idempotent restart

On container restart with a persisted volume, the entrypoint's
`sqlpackage /Action:Publish` re-runs and validates the existing schema
matches — no destructive re-create. Use this for daily-driver dev
loops where the data accumulates locally.
"""

    /// Emit the Docker build context. Failure modes flow through the
    /// inner `DacpacEmitter.emit` Result; the static templates do not
    /// have failure modes (compile-time constants).
    let emit (catalog: Catalog) : Result<DockerImageContext> =
        use _ = Bench.scope "emit.dockerImage.emit"
        match DacpacEmitter.emit catalog with
        | Ok dacpacBytes ->
            Result.success
                {
                    Dockerfile       = dockerfileTemplate
                    DacpacBytes      = dacpacBytes
                    EntrypointScript = entrypointTemplate
                    Readme           = readmeTemplate
                }
        | Error errs -> Error errs
