namespace Projection.Targets.SSDT

// LINT-ALLOW-FILE-MUTATION: Function-local currentModuleKey for
//   module-header transition detection inside the per-kind composer.
//   seq-local; pure output yielded. Per audit Lens-2 Tier-3
//   (justified).

open Projection.Core
open Projection.Core.Passes

/// Π_SSDT — the first sibling Π for V2. Per session-34, Π's
/// canonical output is a `seq<Statement>` (typed, deterministic,
/// lazy); `Render.toText` is the .sql-text realization,
/// `Deploy.executeStream` is the bulk-aware execution realization.
/// Both consume the same statement stream, so the algebra (A18 / T1
/// / T11) holds at the stream level — bulk-vs-incremental deploy is
/// realization-layer policy, invisible to Π.
///
/// Per A18, no policy parameter enters this module. The
/// type-correspondence mapping lives in `Render.columnSqlType` and
/// is shared between the two realizations so emit-time text and
/// deploy-time SQL never drift.
[<RequireQualifiedAccess>]
module RawTextEmitter =

    /// Emitter version. Bump when the textual layout or the synthetic
    /// type map alters; the version banner emits as the first
    /// `Comment` in the statement stream.
    [<Literal>]
    let version : int = 2

    let private rootKey (k: SsKey) : string = SsKey.rootOriginal k

    let private originLabel (o: Origin) : string =
        match o with
        | OsNative                     -> "OsNative"
        | ExternalViaIntegrationStudio -> "ExternalViaIS"
        | ExternalDirect               -> "ExternalDirect"

    let private modalityLabel (m: ModalityMark) : string =
        match m with
        | Static rows   -> sprintf "Static(%d)" rows.Length
        | TenantScoped  -> "TenantScoped"
        | SoftDeletable -> "SoftDeletable"

    let private toTableId (k: Kind) : TableId =
        { Schema = k.Physical.Schema; Table = k.Physical.Table }

    let private toReferenceActionSql (a: ReferenceAction) : ReferenceActionSql =
        match a with
        | NoAction -> NoActionSql
        | Cascade  -> CascadeSql
        | SetNull  -> SetNullSql
        | Restrict -> NoActionSql

    let private columnDef (a: Attribute) : ColumnDef =
        {
            Name = a.Column.ColumnName
            Type = a.Type
            Length = a.Length
            Precision = a.Precision
            Scale = a.Scale
            Nullable = a.Column.IsNullable
            IsIdentity = a.IsIdentity
            IsPrimaryKey = a.IsPrimaryKey
            Provenance = sprintf "%s (%s)" (Name.value a.Name) (rootKey a.SsKey)
        }

    let private kindHeaderText (k: Kind) : string =
        let baseLine =
            sprintf
                "Kind: %s (%s) origin=%s"
                (Name.value k.Name) (rootKey k.SsKey) (originLabel k.Origin)
        if List.isEmpty k.Modality then baseLine
        else
            let labels = k.Modality |> List.map modalityLabel |> String.concat ", "
            sprintf "%s modality=[%s]" baseLine labels

    let private moduleHeaderText (m: Module) : string =
        sprintf "Module: %s (%s)" (Name.value m.Name) (rootKey m.SsKey)

    /// Per session-35 — `targetByKey` and `pkAttrByKey` lifted to
    /// `Map` once at `statements` entry rather than scanning the
    /// catalog linearly per reference. At 300 kinds × ~5 refs each
    /// the savings dwarf the per-row emit cost; FK projection drops
    /// from O(K · R) catalog scans to O(R) hash lookups.
    let private fkDef
        (targetByKey: Map<SsKey, Kind>)
        (pkAttrByKey: Map<SsKey, Attribute>)
        (k: Kind)
        (r: Reference)
        : ForeignKeyDef option =
        use _ = Bench.scope "emit.rawText.reference"
        let sourceColumnOpt =
            k.Attributes
            |> List.tryFind (fun a -> a.SsKey = r.SourceAttribute)
            |> Option.map (fun a -> a.Column.ColumnName)
        match sourceColumnOpt,
              Map.tryFind r.TargetKind targetByKey,
              Map.tryFind r.TargetKind pkAttrByKey with
        | Some sourceColumn, Some target, Some pkAttr ->
            Some
                {
                    Name = sprintf "FK_%s" (rootKey r.SsKey)
                    SourceColumn = sourceColumn
                    Target = toTableId target
                    TargetColumn = pkAttr.Column.ColumnName
                    OnDelete = toReferenceActionSql r.OnDelete
                }
        | _ -> None

    let private pkDef (k: Kind) : PrimaryKeyDef option =
        let pkColumns =
            k.Attributes
            |> List.filter (fun a -> a.IsPrimaryKey)
            |> List.map (fun a -> a.Column.ColumnName)
        if List.isEmpty pkColumns then None
        else
            Some
                {
                    Name = sprintf "PK_%s_%s" k.Physical.Schema k.Physical.Table
                    Columns = pkColumns
                }

    let private cellValue (a: Attribute) (raw: string) : CellValue =
        { Column = a.Column.ColumnName; Type = a.Type; Raw = raw }

    /// Emit one row's INSERT statement (or skip if no values present
    /// for any attribute). Per session-33 — partial fixtures (e.g.,
    /// inline-fixture rows that omit IDENTITY columns) emit only the
    /// columns whose values the row carries.
    let private rowToInsert (k: Kind) (row: StaticRow) : Statement option =
        use _ = Bench.scope "emit.rawText.staticRow"
        let values =
            k.Attributes
            |> List.choose (fun a ->
                Map.tryFind a.Name row.Values
                |> Option.map (cellValue a))
        if List.isEmpty values then None
        else Some (InsertRow (toTableId k, values))

    let private rowStatements (k: Kind) : seq<Statement> =
        seq {
            for m in k.Modality do
                match m with
                | Static rows ->
                    yield Comment (sprintf "Static populations: %d rows" rows.Length)
                    if not (List.isEmpty rows) then
                        let table = toTableId k
                        let hasIdentity = k.Attributes |> List.exists (fun a -> a.IsIdentity)
                        if hasIdentity then yield SetIdentityInsert (table, true)
                        for row in rows do
                            match rowToInsert k row with
                            | Some s -> yield s
                            | None -> ()
                        if hasIdentity then yield SetIdentityInsert (table, false)
                | _ -> ()
        }

    let private kindStatements
        (targetByKey: Map<SsKey, Kind>)
        (pkAttrByKey: Map<SsKey, Attribute>)
        (k: Kind)
        : seq<Statement> =
        seq {
            use _ = Bench.scope "emit.rawText.kind"
            yield Blank
            yield Comment (kindHeaderText k)
            let columns = k.Attributes |> List.map columnDef
            let pk = pkDef k
            let fks = k.References |> List.choose (fkDef targetByKey pkAttrByKey k)
            yield CreateTable (toTableId k, columns, pk, fks)
            yield! rowStatements k
        }

    /// Emitter's view of topological order: returns kinds with FK
    /// targets earlier (cycles resolved or alphabetical-fallback per
    /// `TopologicalOrderPass`). Per session-36 audit (Agent 4 #6) —
    /// the duplicate Kahn implementation that previously lived here
    /// retired in favor of consuming the pass with `SkipSelfEdges`.
    /// Self-FKs are SQL-Server-legal inline; passing `SkipSelfEdges`
    /// keeps a self-FK kind in its natural topological position.
    /// A33 (deterministic-ordered schema emission) is satisfied
    /// structurally — same algorithm, two projections.
    let private emissionOrder (catalog: Catalog) : Kind list =
        use _ = Bench.scope "emit.rawText.topologicalSort"
        let order =
            (TopologicalOrderPass.runWith SkipSelfEdges catalog).Value.Order
        let kindByKey =
            Catalog.allKinds catalog
            |> List.map (fun k -> k.SsKey, k)
            |> Map.ofList
        order |> List.choose (fun key -> Map.tryFind key kindByKey)

    let private moduleByKindKey (catalog: Catalog) : Map<SsKey, Module> =
        catalog.Modules
        |> List.collect (fun m -> m.Kinds |> List.map (fun k -> k.SsKey, m))
        |> Map.ofList

    /// Π port realization (chapter 3.5 slice α). Per-kind statement
    /// slice: `Blank; Comment kindHeader; CreateTable …; …rowStatements`.
    /// Materialized as `Statement list` so the per-kind value sits in
    /// a `Map<SsKey, _>` cleanly; the whole-catalog stream re-acquires
    /// laziness via `yield! list` at composition time. Bench scope
    /// covers per-kind materialization (one event per kind, not per
    /// statement), matching the legacy `Bench.scope "emit.rawText.kind"`
    /// timing semantics.
    let private kindSlice
        (targetByKey: Map<SsKey, Kind>)
        (pkAttrByKey: Map<SsKey, Attribute>)
        (k: Kind)
        : Statement list =
        kindStatements targetByKey pkAttrByKey k |> List.ofSeq

    /// Π port realization. The canonical Π_SSDT shape: every Catalog
    /// kind is mapped to its per-kind statement slice; T11 is a
    /// structural consequence of `ArtifactByKind.create`'s strict-
    /// equality smart constructor — any two `ArtifactByKind` values
    /// built from the same Catalog have equal keysets by construction.
    /// The legacy `statements` and `emit` realizations route through
    /// this primitive so the typed seam is the canonical path.
    ///
    /// Per A18 — `Catalog` only; no Profile, no Policy. Per A35 — the
    /// per-kind value is a typed `Statement list` (deterministic; the
    /// catalog-level stream is reconstituted by `Render.toText` ∘
    /// `composeFromArtifact`).
    let emitSlices : Emitter<Statement list> = fun catalog ->
        use _ = Bench.scope "emit.rawText.emitSlices"
        let allKinds = Catalog.allKinds catalog
        // Per session-35 — lift `(targetByKey, pkAttrByKey)` once so
        // FK resolution is O(1) hash lookup per reference instead of
        // O(K) catalog scan. The lift sits inside `emitSlices` so the
        // entire pre-computation flows through the canonical port.
        let targetByKey =
            allKinds |> List.map (fun k -> k.SsKey, k) |> Map.ofList
        let pkAttrByKey =
            allKinds
            |> List.choose (fun k ->
                k.Attributes
                |> List.tryFind (fun a -> a.IsPrimaryKey)
                |> Option.map (fun pk -> k.SsKey, pk))
            |> Map.ofList
        let slices =
            allKinds
            |> List.map (fun k -> k.SsKey, kindSlice targetByKey pkAttrByKey k)
            |> Map.ofList
        ArtifactByKind.create catalog slices

    /// Compose a per-kind artifact into the catalog-level statement
    /// stream. Order: file preamble + (module header on transition,
    /// per-kind slice) for each kind in topological-sort order. Module
    /// boundary detection consults the catalog's module structure;
    /// topological order comes from `TopologicalOrderPass.runWith
    /// SkipSelfEdges` (per A40 / chapter-3.1 SelfLoopPolicy
    /// harmonization). The composition is total over `ArtifactByKind`'s
    /// keyset by construction — every Catalog kind has a slice (T11),
    /// so the inner `Map.tryFind` cannot return `None`.
    let private composeFromArtifact
        (catalog: Catalog)
        (artifact: ArtifactByKind<Statement list>)
        : seq<Statement> =
        seq {
            yield Comment
                (sprintf
                    "Generated by Projection.Targets.SSDT.RawTextEmitter v%d"
                    version)
            yield Comment "Project = Π_SSDT ∘ E"
            yield Comment "(synthetic-milestone form: typed statement stream)"
            let slices = ArtifactByKind.toMap artifact
            let order =
                (TopologicalOrderPass.runWith SkipSelfEdges catalog).Value.Order
            let modByKey = moduleByKindKey catalog
            let mutable currentModuleKey : SsKey option = None
            for kindKey in order do
                match Map.tryFind kindKey modByKey with
                | Some m when Some m.SsKey <> currentModuleKey ->
                    yield Blank
                    yield Comment (moduleHeaderText m)
                    currentModuleKey <- Some m.SsKey
                | _ -> ()
                match Map.tryFind kindKey slices with
                | Some kindStmts -> yield! kindStmts
                | None -> ()  // unreachable: T11 guarantees keyset equality
        }

    /// Π's canonical statement stream — composes through the typed
    /// `emitSlices` port so the seam is exercised even when callers
    /// want the catalog-flat shape. Per A18 + A35, the same Catalog
    /// produces the same stream byte-for-byte across runs (T1
    /// strengthened to statement-level determinism). The
    /// `ArtifactByKind` smart constructor cannot fail when fed
    /// `Catalog.allKinds`'s own keys; an `Error` here is a Core
    /// invariant breach surfaced at the composition site.
    let statements (catalog: Catalog) : seq<Statement> =
        seq {
            use _ = Bench.scope "emit.rawText.statements"
            match emitSlices catalog with
            | Ok artifact ->
                yield! composeFromArtifact catalog artifact
            | Error err ->
                invalidOp
                    (sprintf
                        "RawTextEmitter.statements: ArtifactByKind invariant breach: %A"
                        err)
        }

    /// Text realization — `statements >> Render.toText`, stream-probed
    /// for throughput observability. The Π port produces the typed
    /// `Statement list` slices via `emitSlices`; this helper composes
    /// the typed stream with the text renderer in one call. T1 byte-
    /// determinism holds because `statements` is deterministic and
    /// `Render.toText` is deterministic in its input.
    let emit (catalog: Catalog) : string =
        use _ = Bench.scope "emit.rawText.emit"
        statements catalog
        |> Bench.streamProbe "emit.rawText.statementStream"
        |> Render.toText
