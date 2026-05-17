namespace Projection.Adapters.OssysSql

open System.IO
open System.Reflection

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
/// The script emits 22 result sets in a fixed order (per V1's
/// `MetadataResultSetProcessorFactory`); slice δ orchestrates the
/// enumeration via `DbDataReader.NextResultAsync`.
[<RequireQualifiedAccess>]
module MetadataExtractionSql =

    /// Embedded-resource name (assembly-qualified) for the SQL script.
    /// F#'s default behavior for `EmbeddedResource Include="Resources\X.sql"`
    /// builds the manifest name from `<RootNamespace>.<RelativePath>` with
    /// dots replacing path separators.
    [<Literal>]
    let private ResourceName = "Projection.Adapters.OssysSql.Resources.outsystems_metadata_rowsets.sql"

    /// Read the embedded SQL script as a UTF-8 string. The bytes are
    /// byte-identical to V1's source file by construction (the
    /// `EmbeddedResource` item copies the file's bytes into the assembly
    /// at build time without transformation; `EmbeddedResourceParity` test
    /// at slice α'verifies the round-trip).
    let read () : string =
        let assembly = Assembly.GetExecutingAssembly()
        use stream = assembly.GetManifestResourceStream(ResourceName)
        match stream with
        | null ->
            // Unreachable in normal builds — the embedded resource is in
            // the .fsproj's `EmbeddedResource Include` list. The defensive
            // `invalidOp` makes the unreachability structural.
            invalidOp (sprintf "MetadataExtractionSql.read: embedded resource '%s' not found (build issue)" ResourceName)
        | s ->
            use reader = new StreamReader(s, System.Text.Encoding.UTF8)
            reader.ReadToEnd()
