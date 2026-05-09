namespace Projection.Targets.SSDT

open Projection.Core

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

    /// Emitter-local topological sort. Returns kinds in an order
    /// such that each kind's FK targets appear earlier (or in the
    /// same kind, for self-references which are SQL-Server-legal
    /// inline). Self-edges skipped; inter-table cycles emit at the
    /// tail in alphabetical SsKey order. Per session-31 / A33 —
    /// SsKey-stable tiebreaking keeps the order itself deterministic.
    let private emissionOrder (catalog: Catalog) : Kind list =
        use _ = Bench.scope "emit.rawText.topologicalSort"
        let allKinds = Catalog.allKinds catalog
        let kindByKey = allKinds |> List.map (fun k -> k.SsKey, k) |> Map.ofList
        let presentKeys = allKinds |> List.map (fun k -> k.SsKey) |> Set.ofList
        let adjacency, indegree =
            allKinds
            |> List.fold
                (fun (adj: Map<SsKey, SsKey list>, ind: Map<SsKey, int>) k ->
                    k.References
                    |> List.fold
                        (fun (a: Map<SsKey, SsKey list>, i: Map<SsKey, int>) r ->
                            if
                                r.TargetKind = k.SsKey
                                || not (Set.contains r.TargetKind presentKeys)
                            then
                                a, i
                            else
                                let children =
                                    Map.tryFind r.TargetKind a |> Option.defaultValue []
                                let updatedAdj =
                                    a |> Map.add r.TargetKind (k.SsKey :: children)
                                let currentInd =
                                    Map.tryFind k.SsKey i |> Option.defaultValue 0
                                let updatedInd =
                                    i |> Map.add k.SsKey (currentInd + 1)
                                updatedAdj, updatedInd)
                        (adj, ind))
                (Map.empty, allKinds |> List.map (fun k -> k.SsKey, 0) |> Map.ofList)
        let mutable currentIndegree = indegree
        let mutable ready =
            allKinds
            |> List.map (fun k -> k.SsKey)
            |> List.filter (fun n ->
                Map.tryFind n currentIndegree |> Option.defaultValue 0 = 0)
            |> List.sort
        let result = ResizeArray<SsKey>()
        while not (List.isEmpty ready) do
            let head = List.head ready
            ready <- List.tail ready
            result.Add head
            let children = Map.tryFind head adjacency |> Option.defaultValue []
            for child in children do
                let childInd =
                    (Map.tryFind child currentIndegree |> Option.defaultValue 0) - 1
                currentIndegree <- Map.add child childInd currentIndegree
                if childInd = 0 then
                    ready <- (child :: ready) |> List.sort
        let emitted = Set.ofSeq result
        let remainder =
            allKinds
            |> List.map (fun k -> k.SsKey)
            |> List.filter (fun k -> not (Set.contains k emitted))
            |> List.sort
        let allOrdered = (List.ofSeq result) @ remainder
        allOrdered
        |> List.choose (fun key -> Map.tryFind key kindByKey)

    let private moduleByKindKey (catalog: Catalog) : Map<SsKey, Module> =
        catalog.Modules
        |> List.collect (fun m -> m.Kinds |> List.map (fun k -> k.SsKey, m))
        |> Map.ofList

    /// Π's canonical statement stream. Pure, lazy, deterministic.
    /// Consumers iterate as needed; realization layers
    /// (`Render.toText`, `Deploy.executeStream`) choose how each
    /// statement materializes. Module / kind grouping comments emit
    /// as `Comment`; vertical spacing as `Blank`. Per A18 + A35,
    /// no policy enters; the same Catalog produces the same stream
    /// byte-for-byte across runs (T1 strengthened to statement-level
    /// determinism).
    let statements (catalog: Catalog) : seq<Statement> =
        seq {
            use _ = Bench.scope "emit.rawText.statements"
            yield Comment
                (sprintf
                    "Generated by Projection.Targets.SSDT.RawTextEmitter v%d"
                    version)
            yield Comment "Project = Π_SSDT ∘ E"
            yield Comment "(synthetic-milestone form: typed statement stream)"
            // Per session-35 — lift `(targetByKey, pkAttrByKey)`
            // once for the whole emission so FK resolution is
            // O(1) hash lookup per reference instead of O(K)
            // catalog scan. At 300 kinds × 1500 refs the saving
            // is ~450k ops per emit.
            let allKinds = Catalog.allKinds catalog
            let targetByKey =
                allKinds |> List.map (fun k -> k.SsKey, k) |> Map.ofList
            let pkAttrByKey =
                allKinds
                |> List.choose (fun k ->
                    k.Attributes
                    |> List.tryFind (fun a -> a.IsPrimaryKey)
                    |> Option.map (fun pk -> k.SsKey, pk))
                |> Map.ofList
            let modByKey = moduleByKindKey catalog
            let mutable currentModuleKey : SsKey option = None
            for k in emissionOrder catalog do
                let moduleKey =
                    match Map.tryFind k.SsKey modByKey with
                    | Some m -> Some m.SsKey
                    | None -> None
                match moduleKey, currentModuleKey with
                | Some mk, prev when Some mk <> prev ->
                    let m = modByKey[k.SsKey]
                    yield Blank
                    yield Comment (moduleHeaderText m)
                    currentModuleKey <- Some mk
                | _ -> ()
                yield! kindStatements targetByKey pkAttrByKey k
        }

    /// Back-compat .sql-text realization — `statements >> Render.toText`,
    /// stream-probed for throughput observability. T1 byte-determinism
    /// holds because `statements` is deterministic and `Render.toText`
    /// is deterministic in its input.
    let emit (catalog: Catalog) : string =
        use _ = Bench.scope "emit.rawText.emit"
        statements catalog
        |> Bench.streamProbe "emit.rawText.statementStream"
        |> Render.toText
