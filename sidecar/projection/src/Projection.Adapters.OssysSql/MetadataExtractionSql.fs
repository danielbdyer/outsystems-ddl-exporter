namespace Projection.Adapters.OssysSql

open System.IO
open System.Reflection  // LINT-ALLOW: embedded-resource manifest loading at the adapter boundary (Assembly.GetManifestResourceStream for the carbon-copied OSSYS rowsets SQL); reflection is deferred from Core, not from boundary adapters; no type-scanning / attribute-discovery — resource access only

/// V2's offline-doable inheritance from V1's metadata-extraction chain.
///
/// **Carbon-copy citation:** the embedded resource
/// `Resources/outsystems_metadata_rowsets.sql` is byte-identical to V1's
/// `src/AdvancedSql/outsystems_metadata_rowsets.sql`, copied on
/// 2026-05-17 per `DECISIONS 2026-05-16 (later) — V2 self-containment +
/// carbon-copy editorial inheritance`. The SQL is the truth; V2 inherits
/// it verbatim. The C# plumbing surrounding the SQL in V1 (`Osm.Pipeline
/// .SqlExtraction.*`; ~55 files) is rewritten in F# at copy-time per the
/// chapter-5.0 open Q1 — sibling to `Projection.Adapters.Sql.ReadSide`
/// and `Projection.Adapters.Osm.CatalogReader`. See `ADMIRE.md` 2026-05-13
/// for the donor entry; `CHAPTER_5_0_OPEN.md` for the slice arc.
///
/// **The SQL contract (parametric, side-effect-free SQL).** The script
/// declares five parameters: `@ModuleNamesCsv` (NVARCHAR(MAX); CSV of
/// module names; empty/null = all modules), `@IncludeSystem` (BIT),
/// `@IncludeInactive` (BIT), `@OnlyActiveAttributes` (BIT),
/// `@EntityFilterJson` (NVARCHAR(MAX); per-module entity allow-list).
/// The script emits `MetadataSnapshotRunner.ExpectedResultSets` result
/// sets in a fixed order (the count lives on that constant alone — a
/// number restated here drifted once already; sink chapter open,
/// 2026-08-15); the runner orchestrates the enumeration via
/// `DbDataReader.NextResultAsync`.
[<RequireQualifiedAccess>]
module MetadataExtractionSql =

    /// Embedded-resource name (assembly-qualified) for the SQL script.
    /// F#'s default behavior for `EmbeddedResource Include="Resources\X.sql"`
    /// builds the manifest name from `<RootNamespace>.<RelativePath>` with
    /// dots replacing path separators.
    [<Literal>]
    let private ResourceName = "Projection.Adapters.OssysSql.Resources.outsystems_metadata_rowsets.sql"

    /// Embedded-resource name for the synthetic OSSYS edge-case seed
    /// fixture (chapter 5.0 slice β). Carbon-copied from V1's
    /// `tests/Fixtures/sql/model.edge-case.seed.sql` (2026-05-17).
    /// Creates synthetic `dbo.ossys_Espace` / `dbo.ossys_Entity` /
    /// `dbo.ossys_Entity_Attr` tables, populates them with deterministic
    /// edge-case data (3 modules, 5 entities, 16 attributes; FK, partition,
    /// trigger, disabled-index, cross-schema, system-module, default-
    /// constraint shapes), and creates corresponding physical tables.
    /// V2's canary exercises end-to-end: bootstrap → rowsets SQL →
    /// V2 runner → Catalog → SSDT emit → deploy → readback → diff.
    [<Literal>]
    let private FixtureResourceName = "Projection.Adapters.OssysSql.Resources.ossys-edge-case.seed.sql"

    /// Embedded-resource name for the data-sink chapter's lifecycle seed
    /// (S1, 2026-08-15; CHAPTER_SINK_OPEN.md). Authored fresh for V2 (no
    /// V1 donor). A SEPARATE resource from the edge-case seed — 14
    /// consumer files pin that seed's exact counts, so the post-cutover
    /// lifecycle shapes (tombstoned entity with a surviving table; the
    /// tombstone ↔ extension re-registration pair; the two-active-claims
    /// contested pair; the orphan physical table) live here instead.
    [<Literal>]
    let private LifecycleSeedResourceName = "Projection.Adapters.OssysSql.Resources.ossys-lifecycle.seed.sql"

    let private readResource (resourceName: string) : string =
        let assembly = Assembly.GetExecutingAssembly()
        use stream = assembly.GetManifestResourceStream(resourceName)
        match stream with
        | null ->
            // Unreachable in normal builds — the embedded resource is in
            // the .fsproj's `EmbeddedResource Include` list. The defensive
            // `invalidOp` makes the unreachability structural.
            invalidOp (sprintf "MetadataExtractionSql.readResource: embedded resource '%s' not found (build issue)" resourceName)
        | s ->
            use reader = new StreamReader(s, System.Text.Encoding.UTF8)
            reader.ReadToEnd()

    /// Read V1's `outsystems_metadata_rowsets.sql` as a UTF-8 string.
    /// Byte-identical to V1's source file by construction.
    let read () : string =
        readResource ResourceName

    /// Read V2's synthetic OSSYS edge-case seed fixture as a UTF-8
    /// string. Byte-identical to V1's `model.edge-case.seed.sql` by
    /// construction. Chapter 5.0 slice β — the canary mockup donor.
    let readEdgeCaseSeed () : string =
        readResource FixtureResourceName

    /// Read the data-sink chapter's lifecycle seed as a UTF-8 string —
    /// the post-cutover estate shapes (tombstone / re-registration pair /
    /// contested pair / orphan table) the sink's recoverability and
    /// adjudication witnesses run against. Sink chapter S1.
    let readLifecycleSeed () : string =
        readResource LifecycleSeedResourceName
