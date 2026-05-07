namespace Projection.Adapters.Osm

open System.Threading.Tasks
open Projection.Core

/// Boundary adapter — converts V1's `osm_model.json` snapshot shape
/// into V2's `Catalog` IR.
///
/// **V1↔V2 boundary.** V1's metadata extraction chain
/// (`outsystems_metadata_rowsets.sql` → `MetadataSnapshotRunner` →
/// `SnapshotJsonBuilder` → `osm_model.json`) is the source of truth
/// for OutSystems platform metadata. V2's adapter consumes the JSON
/// document V1 produces. The cherry-pick discipline (`HANDOFF.md`)
/// keeps the boundary as data, not typed cross-references — this
/// adapter does not depend on any V1 C# types.
///
/// **Position B for `ICatalogReader`.** Per `DECISIONS 2026-05-15 —
/// OSSYS adapter parse signature`, the entry-point shape is
/// `SnapshotSource -> Task<Result<Catalog>>`. A future
/// `ICatalogReader` interface (when a second catalog source
/// materializes) wraps this signature trivially via object expression;
/// no retrofit needed.
///
/// **Scope of this commit.** Project scaffold only. The `parse`
/// function is a stub that returns `notImplemented`. Subsequent
/// commits in the OSSYS arc fill in DTOs, JSON parsing, and V1↔V2
/// translation rules — driven by differential tests, not by
/// speculative DTO design.
[<RequireQualifiedAccess>]
module CatalogReader =

    /// The input slot on the parse function. Closed DU; a future
    /// `LiveOssysConnection` variant lands as explicit DU expansion
    /// when V2 grows a SQL-running entry point per the re-open
    /// trigger named in `DECISIONS 2026-05-15 — OSSYS adapter parse
    /// signature`. Until that trigger fires, V1's JSON chain remains
    /// the metadata producer; V2 reads its output.
    type SnapshotSource =
        /// Path to a V1-produced `osm_model.json` file on disk.
        | SnapshotFile of path: string
        /// In-memory snapshot string. Useful for tests and for
        /// pipelines that produce the snapshot in memory rather than
        /// via disk.
        | SnapshotJson of json: string

    let private notImplemented (source: SnapshotSource) : ValidationError =
        let detail =
            match source with
            | SnapshotFile path -> sprintf "SnapshotFile(%s)" path
            | SnapshotJson _    -> "SnapshotJson(<inline>)"
        ValidationError.create
            "adapter.osm.notImplemented"
            (sprintf "Projection.Adapters.Osm.CatalogReader.parse is not yet implemented. Source: %s" detail)

    /// Parse a V1 `osm_model.json` snapshot into a V2 `Catalog`.
    ///
    /// **Stub.** Returns a `notImplemented` validation error today.
    /// Subsequent commits land DTOs, JSON parsing, and V1↔V2
    /// translation rules under empirical pressure from differential
    /// tests embedding minimal V1 fixtures.
    ///
    /// Async at the boundary even though the JSON-path implementation
    /// can be synchronous; future async-by-nature variants
    /// (DACPAC unzip, eventual `LiveOssysConnection`) need the
    /// `Task<...>` shape. See `DECISIONS 2026-05-15 — OSSYS adapter
    /// parse signature` for the rationale.
    let parse (source: SnapshotSource) : Task<Result<Catalog>> =
        Task.FromResult(Result.failureOf (notImplemented source))
