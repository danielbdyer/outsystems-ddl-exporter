namespace Twin.Runtime
// LINT-ALLOW-FILE-MUTATION: the Twin's evidence-import driver — a single mutable
//   accumulator over the imported coordinate-keyed evidence pack (an imperative
//   fold at the file-read boundary; isolated, no escape). No pure equivalent for
//   the sequential import walk.

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Adapters.Sql
open Projection.Pipeline
open Twin.Core

/// THE TWIN — the evidence lifecycle's I/O (Twin.Runtime).
///
/// `import` — for each configured source, open it, obtain its catalog in
/// its own rendition (logical: the schema read directly; physical: the
/// live OSSYS model read, whose kinds carry logical names over physical
/// realizations), restrict to the closed table set, profile through the
/// kernel's `LiveProfiler` (three queries per kind; counts exact under
/// any sampling cap), rebind to coordinates, merge (collision-refused),
/// and write the RICH pack.
///
/// `derive` — rich → shape (law 3's literal-free projection), written to
/// the committed path.
///
/// `verify` — both packs re-validated against the current estate
/// definition; the coverage report is the drift answer when the schema
/// moves ahead of the evidence.
[<RequireQualifiedAccess>]
module EvidenceImport =

    type SourceReport = {
        Source  : string
        Tables  : int
        Columns : int
    }

    type ImportReport = {
        Sources   : SourceReport list
        RichPath  : string
        FanOuts   : int
    }

    type TableCoverage = {
        Table            : string
        Tier             : string   // "rich" | "shape" | "none"
        EvidencedColumns : int
        TotalColumns     : int
    }

    type VerifyReport = {
        ShapePresent : bool
        RichPresent  : bool
        Coverage     : TableCoverage list
        Problems     : ValidationError list
    }

    let private richUnset : ValidationError =
        ValidationError.create
            "twin.evidence.richUnset"
            "Evidence import writes the rich pack, and no rich path is configured. Set evidence.rich in twin.json (an out-of-repo location), then rerun."

    let private shapeUnset : ValidationError =
        ValidationError.create
            "twin.evidence.shapeUnset"
            "Deriving the shape tier writes the committed pack, and no shape path is configured. Set evidence.shape in twin.json, then rerun."

    let private richMissing (path: string) : ValidationError =
        ValidationError.createWithMetadata
            "twin.evidence.richMissing"
            "The rich pack is not present. Run: twin evidence import"
            (Map.ofList [ "path", Some path ])

    let private noSources : ValidationError =
        ValidationError.create
            "twin.evidence.noSources"
            "No evidence sources are configured. Add evidence.sources entries to twin.json, then rerun."

    let private missingTable (source: string) (table: string) : ValidationError =
        ValidationError.createWithMetadata
            "twin.evidence.sourceMissingTable"
            "A table in the source's closed set was not found in the source database."
            (Map.ofList [ "source", Some source; "table", Some table ])

    let private ambiguousEntity (source: string) (table: string) : ValidationError =
        ValidationError.createWithMetadata
            "twin.evidence.ambiguousEntity"
            "Two entities in the physical source share this logical name; the coordinate cannot bind uniquely. Narrow the source's modules, or rename."
            (Map.ofList [ "source", Some source; "table", Some table ])

    let private resolvePath (root: string) (ref: string) : string =
        let cleaned = if ref.StartsWith "file:" then ref.Substring 5 else ref
        System.IO.Path.Combine(root, cleaned.Replace('/', System.IO.Path.DirectorySeparatorChar))

    /// Restrict a capture catalog to the kinds the closed set names, and
    /// produce the keep-map (kind → estate coordinate text). The match
    /// axis is the rendition seam: logical sources match on the physical
    /// `schema.table`; physical sources match on the logical entity name.
    let private restrict
        (source: EvidenceSource)
        (catalog: Catalog)
        : Result<Catalog * (Kind -> string option)> =
        let configured =
            source.Tables |> List.map (fun t -> TableCoordinate.key t, TableCoordinate.text t)
        let matchesOf (k: Kind) : string list =
            match source.Rendition with
            | Logical ->
                let key = TableId.normalizedKey k.Physical
                configured |> List.filter (fun (ck, _) -> ck = key) |> List.map snd
            | Physical ->
                let entity = Name.value k.Name
                configured
                |> List.filter (fun (ck, _) ->
                    // The configured coordinate's table segment against the
                    // logical entity name, case-insensitively.
                    match ck.Split '.' with
                    | [| _; table |] -> System.String.Equals(table, entity, System.StringComparison.OrdinalIgnoreCase)
                    | _ -> false)
                |> List.map snd
        let kindsWithCoord =
            Catalog.allKinds catalog
            |> List.collect (fun k -> matchesOf k |> List.map (fun coord -> coord, k))
        // Ambiguity: one coordinate matched by several kinds (physical
        // rendition, duplicate entity names).
        let ambiguities =
            kindsWithCoord
            |> List.groupBy (fun (coord, _) -> coord.ToLowerInvariant())
            |> List.filter (fun (_, g) -> List.length g > 1)
            |> List.map (fun (_, g) -> ambiguousEntity source.Name (fst (List.head g)))
        // Completeness: every configured table matched exactly once.
        let matchedKeys =
            kindsWithCoord |> List.map (fun (coord, _) -> coord.ToLowerInvariant()) |> Set.ofList
        let missing =
            configured
            |> List.filter (fun (_, text) -> not (Set.contains (text.ToLowerInvariant()) matchedKeys))
            |> List.map (fun (_, text) -> missingTable source.Name text)
        match ambiguities @ missing with
        | _ :: _ as errors -> Result.failure errors
        | [] ->
            let keptKeys = kindsWithCoord |> List.map (fun (_, k) -> k.SsKey) |> Set.ofList
            let coordByKind = kindsWithCoord |> List.map (fun (coord, k) -> k.SsKey, coord) |> Map.ofList
            let filtered =
                { catalog with
                    Modules =
                        catalog.Modules
                        |> List.map (fun m -> { m with Kinds = m.Kinds |> List.filter (fun k -> Set.contains k.SsKey keptKeys) })
                        |> List.filter (fun m -> not (List.isEmpty m.Kinds)) }
            Result.success (filtered, fun k -> Map.tryFind k.SsKey coordByKind)

    /// Import one source: open → catalog per rendition → restrict →
    /// profile → rebind.
    let private importSource (source: EvidenceSource) : Task<Result<EvidencePack>> =
        task {
            let! opened = ConnectionSpec.openSpec SubstrateRole.Source source.Name source.ConnRef
            match opened with
            | Error es -> return Result.failure es
            | Ok cnn ->
                use cnn = cnn
                let! catalogResult =
                    task {
                        match source.Rendition with
                        | Logical -> return! ReadSide.readSchema cnn
                        | Physical -> return! LiveModelRead.fromConnection cnn
                    }
                match catalogResult with
                | Error es -> return Result.failure es
                | Ok rawCatalog ->
                    match restrict source rawCatalog with
                    | Error es -> return Result.failure es
                    | Ok (restricted, keep) ->
                        // The 4.4 trap: a Static-marked kind is skipped by the
                        // profiler; the closed set is explicit, so every listed
                        // table gets evidence — strip the marks first.
                        let profileCatalog = Catalog.stripStaticPopulations restricted
                        let options =
                            { SqlProfilerOptions.defaults with
                                Sampling =
                                    match source.SampleRows with
                                    | Some cap -> SamplingPolicy.uniform (Some cap)
                                    | None -> SamplingPolicy.fullScan }
                        let! cache = LiveProfiler.captureEvidenceCacheWith options cnn profileCatalog
                        match cache with
                        | Error es -> return Result.failure es
                        | Ok cache ->
                            let profile = ProfileDerivation.attachFromCache cache profileCatalog Profile.empty
                            return Result.success (Evidence.ofProfile source.Name profileCatalog keep profile)
        }

    /// Import every configured source, merge, write the rich pack.
    let importAll (root: string) (config: TwinConfig) : Task<Result<ImportReport>> =
        task {
            match config.Evidence.RichRef, config.Evidence.Sources with
            | None, _ -> return Result.failureOf richUnset
            | _, [] -> return Result.failureOf noSources
            | Some richRef, sources ->
                let mutable failed : ValidationError list = []
                let packs = System.Collections.Generic.List<EvidencePack>()
                for source in sources do
                    if List.isEmpty failed then
                        let! pack = importSource source
                        match pack with
                        | Ok p -> packs.Add p
                        | Error es -> failed <- es
                if not (List.isEmpty failed) then return Result.failure failed
                else
                    match Evidence.merge (List.ofSeq packs) with
                    | Error es -> return Result.failure es
                    | Ok merged ->
                        let path = resolvePath root richRef
                        match System.IO.Path.GetDirectoryName path with
                        | null | "" -> ()
                        | dir -> System.IO.Directory.CreateDirectory dir |> ignore
                        System.IO.File.WriteAllText(path, Evidence.serialize merged)
                        return
                            Result.success
                                { Sources =
                                    List.ofSeq packs
                                    |> List.map (fun p ->
                                        { Source = p.Sources |> List.tryHead |> Option.defaultValue "?"
                                          Tables = List.length p.Tables
                                          Columns = p.Tables |> List.sumBy (fun t -> List.length t.Columns) })
                                  RichPath = path
                                  FanOuts = List.length merged.FanOuts }
        }

    /// Rich → shape, written to the committed path.
    let derive (root: string) (config: TwinConfig) : Task<Result<string>> =
        task {
            match config.Evidence.RichRef, config.Evidence.ShapePath with
            | None, _ -> return Result.failureOf richUnset
            | _, None -> return Result.failureOf shapeUnset
            | Some richRef, Some shapeRel ->
                let richPath = resolvePath root richRef
                if not (System.IO.File.Exists richPath) then
                    return Result.failureOf (richMissing richPath)
                else
                    match Evidence.deserialize (System.IO.File.ReadAllText richPath) with
                    | Error es -> return Result.failure es
                    | Ok rich ->
                        let shapePath = resolvePath root shapeRel
                        match System.IO.Path.GetDirectoryName shapePath with
                        | null | "" -> ()
                        | dir -> System.IO.Directory.CreateDirectory dir |> ignore
                        System.IO.File.WriteAllText(shapePath, Evidence.serialize (Evidence.deriveShape rich))
                        return Result.success shapePath
        }

    /// Both packs against the current estate: what binds, what covers,
    /// what refuses.
    let verify (root: string) (config: TwinConfig) (twinCatalog: Catalog) : VerifyReport =
        let index = CatalogIndex.ofCatalog twinCatalog
        let load (ref: string option) : (EvidencePack option * ValidationError list) =
            match ref with
            | None -> None, []
            | Some r ->
                let path = resolvePath root r
                if not (System.IO.File.Exists path) then None, []
                else
                    match Evidence.deserialize (System.IO.File.ReadAllText path) with
                    | Ok p -> Some p, (match Evidence.toProfile index p with Ok _ -> [] | Error es -> es)
                    | Error es -> None, es
        let shape, shapeProblems = load config.Evidence.ShapePath
        let rich, richProblems = load config.Evidence.RichRef
        let coveredBy (pack: EvidencePack option) (table: string) : int option =
            pack
            |> Option.bind (fun p ->
                p.Tables
                |> List.tryFind (fun t -> System.String.Equals(t.Table, table, System.StringComparison.OrdinalIgnoreCase))
                |> Option.map (fun t -> List.length t.Columns))
        let coverage =
            CatalogIndex.kinds index
            |> List.map (fun (coord, kind) ->
                let text = TableCoordinate.text coord
                let richCols = coveredBy rich text
                let shapeCols = coveredBy shape text
                { Table = text
                  Tier = (match richCols, shapeCols with Some _, _ -> "rich" | None, Some _ -> "shape" | None, None -> "none")
                  EvidencedColumns = defaultArg (match richCols with Some c -> Some c | None -> shapeCols) 0
                  TotalColumns = List.length kind.Attributes })
            |> List.sortBy (fun c -> c.Table.ToLowerInvariant())
        { ShapePresent = shape.IsSome
          RichPresent = rich.IsSome
          Coverage = coverage
          Problems = shapeProblems @ richProblems }
