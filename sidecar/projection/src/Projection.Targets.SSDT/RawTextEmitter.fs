namespace Projection.Targets.SSDT

open System
open System.Text
open Projection.Core
open Projection.Core.Passes

/// Raw .sql-style text emitter — the first sibling Π for V2. The output
/// is diffable text reflecting every kind, attribute, and reference in
/// the catalog. This is the synthetic-fixture milestone form per
/// DECISIONS.md (2026-05-06 — Π_SSDT first emission target is raw
/// .sql-style text); DacFx-backed real SSDT artifacts arrive later.
///
/// Π is mechanical (A18): no policy parameter enters this module. The
/// type-correspondence mapping below is the synthetic-milestone default;
/// when Policy lands as a structured input it will replace these
/// hard-codes.
[<RequireQualifiedAccess>]
module RawTextEmitter =

    /// Emitter version. The lineage / output may change shape across
    /// versions; bump when the textual layout or the synthetic type map
    /// is altered.
    [<Literal>]
    let version : int = 1

    // -----------------------------------------------------------------------
    // Synthetic-milestone defaults. These belong in Policy when Policy
    // lands; for now they are constants here so Π stays mechanical.
    // -----------------------------------------------------------------------

    let private defaultSqlType (t: PrimitiveType) : string =
        match t with
        | Integer  -> "INT"
        | Decimal  -> "DECIMAL(18, 4)"
        | Text     -> "NVARCHAR(MAX)"
        | Boolean  -> "BIT"
        | DateTime -> "DATETIME2"
        | Date     -> "DATE"
        | Time     -> "TIME"
        | Binary   -> "VARBINARY(MAX)"
        | Guid     -> "UNIQUEIDENTIFIER"

    /// Per session-32 — render a column's SQL type with length /
    /// precision / scale honored when the IR carries them. Falls
    /// back to `defaultSqlType` for types where length / precision
    /// don't apply, or when the IR fields are `None`.
    let private columnSqlType (a: Attribute) : string =
        match a.Type with
        | Text ->
            match a.Length with
            | Some n when n > 0 -> sprintf "NVARCHAR(%d)" n
            | _ -> "NVARCHAR(MAX)"
        | Binary ->
            match a.Length with
            | Some n when n > 0 -> sprintf "VARBINARY(%d)" n
            | _ -> "VARBINARY(MAX)"
        | Decimal ->
            match a.Precision, a.Scale with
            | Some p, Some s -> sprintf "DECIMAL(%d, %d)" p s
            | Some p, None -> sprintf "DECIMAL(%d, 0)" p
            | _ -> "DECIMAL(18, 4)"
        | other -> defaultSqlType other

    let private renderAction (a: ReferenceAction) : string =
        match a with
        | NoAction -> "NO ACTION"
        | Cascade  -> "CASCADE"
        | SetNull  -> "SET NULL"
        | Restrict -> "NO ACTION"  // SQL Server: Restrict is encoded as NO ACTION

    let private quote (s: string) : string = sprintf "[%s]" s

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

    let private rootKey (k: SsKey) : string = SsKey.rootOriginal k

    // -----------------------------------------------------------------------
    // Per-element rendering. Each function takes a StringBuilder so the
    // emitter is allocation-friendly without sacrificing purity.
    // -----------------------------------------------------------------------

    let private renderAttribute (sb: StringBuilder) (a: Attribute) : unit =
        let name = quote a.Column.ColumnName
        let typ = defaultSqlType a.Type
        let nullness = if a.Column.IsNullable then "NULL" else "NOT NULL"
        sb.Append("    ").Append(name).Append(' ').Append(typ).Append(' ').Append(nullness)
            .Append("  -- ").Append(Name.value a.Name).Append(" (").Append(rootKey a.SsKey).Append(')')
        |> ignore

    let private renderKindHeader (sb: StringBuilder) (k: Kind) : unit =
        sb.Append("-- Kind: ").Append(Name.value k.Name)
            .Append(" (").Append(rootKey k.SsKey).Append(") origin=")
            .Append(originLabel k.Origin) |> ignore
        if not (List.isEmpty k.Modality) then
            let labels = k.Modality |> List.map modalityLabel |> String.concat ", "
            sb.Append(" modality=[").Append(labels).Append(']') |> ignore
        sb.AppendLine() |> ignore

    /// Resolve a Reference into the inline FK clause string (no
    /// trailing comma; caller positions). Returns `None` when the
    /// target Kind isn't in the catalog (dangling reference) — the
    /// caller emits a comment marker instead of a half-formed clause.
    let private inlineFkClause (catalog: Catalog) (k: Kind) (r: Reference) : string option =
        let sourceColumnOpt =
            k.Attributes
            |> List.tryFind (fun a -> a.SsKey = r.SourceAttribute)
            |> Option.map (fun a -> a.Column.ColumnName)
        match sourceColumnOpt, Catalog.tryFindKind r.TargetKind catalog with
        | Some sourceColumn, Some target ->
            let targetPkOpt =
                target.Attributes
                |> List.tryFind (fun a -> a.IsPrimaryKey)
                |> Option.map (fun a -> a.Column.ColumnName)
            match targetPkOpt with
            | Some targetPk ->
                Some
                    (sprintf
                        "    CONSTRAINT %s FOREIGN KEY (%s) REFERENCES %s.%s (%s)"
                        (quote (sprintf "FK_%s" (rootKey r.SsKey)))
                        (quote sourceColumn)
                        (quote target.Physical.Schema)
                        (quote target.Physical.Table)
                        (quote targetPk))
            | None -> None
        | _ -> None

    let private renderTable (catalog: Catalog) (sb: StringBuilder) (k: Kind) : unit =
        use _ = Bench.scope "emit.rawText.kind"
        renderKindHeader sb k
        let qualified =
            sprintf "%s.%s" (quote k.Physical.Schema) (quote k.Physical.Table)
        sb.Append("CREATE TABLE ").Append(qualified).AppendLine(" (") |> ignore
        // PK constraint emission (M3 prep): if any attributes carry
        // IsPrimaryKey, append a trailing CONSTRAINT clause so the
        // deployed table actually carries a PRIMARY KEY.
        let pkColumns =
            k.Attributes
            |> List.filter (fun a -> a.IsPrimaryKey)
            |> List.map (fun a -> a.Column.ColumnName)
        let hasPkConstraint = not (List.isEmpty pkColumns)
        // Per the V2 backlog: FKs are inline within the CREATE TABLE
        // statement (not trailing ALTER TABLE batch). The emitter's
        // outer loop sorts kinds in topological order so a FK's
        // target Kind has already emitted by the time the source
        // CREATE TABLE runs (self-references are SQL-Server-legal
        // inline; inter-table cycles need a separate slice).
        let fkClauses =
            k.References
            |> Bench.iterMap "emit.rawText.reference" (inlineFkClause catalog k)
            |> List.choose id
        let hasFkClauses = not (List.isEmpty fkClauses)
        let lastColumnIdx = k.Attributes.Length - 1
        k.Attributes
        |> Bench.iteriDo "emit.rawText.attribute" (fun i a ->
            let name = quote a.Column.ColumnName
            let typ = columnSqlType a
            let identityClause = if a.IsIdentity then " IDENTITY(1,1)" else ""
            let nullness = if a.Column.IsNullable then "NULL" else "NOT NULL"
            let needsComma = i < lastColumnIdx || hasPkConstraint || hasFkClauses
            let sep = if needsComma then "," else ""
            let pkTag = if a.IsPrimaryKey then " PK" else ""
            sb.Append("    ").Append(name).Append(' ').Append(typ)
                .Append(identityClause).Append(' ').Append(nullness)
                .Append(sep).Append("  -- ").Append(Name.value a.Name).Append(" (").Append(rootKey a.SsKey).Append(')')
                .Append(pkTag).AppendLine() |> ignore)
        if hasPkConstraint then
            let pkColumnList = pkColumns |> List.map quote |> String.concat ", "
            let pkConstraintName =
                sprintf "PK_%s_%s" k.Physical.Schema k.Physical.Table
            let pkSep = if hasFkClauses then "," else ""
            sb.Append("    CONSTRAINT ").Append(quote pkConstraintName)
                .Append(" PRIMARY KEY (").Append(pkColumnList).Append(")").AppendLine(pkSep)
            |> ignore
        // Inline FK clauses (one per Reference). Each except the
        // last gets a trailing comma.
        if hasFkClauses then
            let lastFkIdx = fkClauses.Length - 1
            fkClauses
            |> List.iteri (fun i clause ->
                let sep = if i < lastFkIdx then "," else ""
                sb.Append(clause).AppendLine(sep) |> ignore)
        sb.AppendLine(");") |> ignore

    let private renderStaticPopulations (sb: StringBuilder) (k: Kind) : unit =
        k.Modality
        |> Bench.iterDo "emit.rawText.modality" (fun m ->
            match m with
            | Static rows ->
                sb.Append("-- Static populations: ").Append(rows.Length).AppendLine(" rows") |> ignore
                rows
                |> Bench.iterDo "emit.rawText.staticRow" (fun row ->
                    sb.Append("--   ").Append(rootKey row.Identifier) |> ignore
                    let pairs =
                        row.Values
                        |> Map.toList
                        |> List.map (fun (n, v) -> sprintf "%s=%s" (Name.value n) v)
                        |> String.concat ", "
                    if pairs <> "" then
                        sb.Append(" { ").Append(pairs).Append(" }") |> ignore
                    sb.AppendLine() |> ignore)
            | _ -> ())

    let private renderModule (sb: StringBuilder) (catalog: Catalog) (m: Module) : unit =
        use _ = Bench.scope "emit.rawText.module"
        sb.AppendLine() |> ignore
        sb.Append("-- Module: ").Append(Name.value m.Name)
            .Append(" (").Append(rootKey m.SsKey).Append(')').AppendLine() |> ignore
        m.Kinds
        |> Bench.iterDo "emit.rawText.moduleKind" (fun k ->
            sb.AppendLine() |> ignore
            renderTable catalog sb k
            renderStaticPopulations sb k)

    // -----------------------------------------------------------------------
    // Public surface.
    // -----------------------------------------------------------------------

    /// Emitter-local topological sort. Returns kinds in an order
    /// such that each kind's FK targets appear earlier (or in the
    /// same kind, for self-references which are SQL-Server-legal
    /// inline). Differs from `TopologicalOrderPass` in that
    /// self-edges are ignored — the pass's broader use case treats
    /// self-loops as cycles needing resolution, but for inline FK
    /// emission they're fine.
    ///
    /// Inter-table cycles (A → B → A) still aren't handled; those
    /// cyclic kinds emit at the tail in alphabetical SsKey order,
    /// and their inline FKs will fail at deploy time. The OutSystems
    /// patterns the canary targets don't have inter-table cycles;
    /// if a fixture surfaces one, this emitter will need a second
    /// ALTER-TABLE-fallback pass for the cycle members.
    let private emissionOrder (catalog: Catalog) : Kind list =
        use _ = Bench.scope "emit.rawText.topologicalSort"
        let allKinds = Catalog.allKinds catalog
        let kindByKey = allKinds |> List.map (fun k -> k.SsKey, k) |> Map.ofList
        let presentKeys = allKinds |> List.map (fun k -> k.SsKey) |> Set.ofList
        // Adjacency: target → sources-that-depend-on-target.
        // Indegree: source → count of targets it depends on.
        // Self-edges (source = target) skipped: SQL allows self-FKs
        // inline.
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
        // Kahn's: pop ready (indegree 0) in SsKey order, decrement
        // children's indegree, repeat.
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
        // Cyclic remainder (inter-table cycles) at the tail.
        let emitted = Set.ofSeq result
        let remainder =
            allKinds
            |> List.map (fun k -> k.SsKey)
            |> List.filter (fun k -> not (Set.contains k emitted))
            |> List.sort
        let allOrdered = (List.ofSeq result) @ remainder
        allOrdered
        |> List.choose (fun key -> Map.tryFind key kindByKey)

    /// Emit the catalog as raw .sql-style text. Output is deterministic:
    /// for any byte-identical input catalog, output is byte-identical
    /// across runs (T1).
    ///
    /// **Inline FK constraints + topological emission order.** Per
    /// the V2 backlog's "FKs created inline" decision, FK
    /// constraints emit *inside* their owning CREATE TABLE (as
    /// `CONSTRAINT [FK_…] FOREIGN KEY […] REFERENCES …` clauses)
    /// rather than as trailing `ALTER TABLE` statements. To support
    /// inline FKs without forward-reference deploy failures, kinds
    /// are emitted in topological order — every FK target is
    /// already declared by the time its source's CREATE TABLE runs.
    /// Self-references (a kind's FK to its own PK) are SQL-Server-
    /// legal inline; the emitter's topological sort skips self-edges
    /// so self-FK kinds emit in their normal position.
    ///
    /// Per A33 (Schema-Data Ordering Law), schema emission's
    /// canonical order is deterministic; this Kahn's algorithm uses
    /// SsKey-stable tiebreaking, so the order is itself deterministic
    /// — preserving the diff-stability property A33 requires.
    let emit (catalog: Catalog) : string =
        use _ = Bench.scope "emit.rawText.emit"
        let sb = StringBuilder(2048)
        sb.Append("-- Generated by Projection.Targets.SSDT.RawTextEmitter v")
            .Append(version).AppendLine() |> ignore
        sb.AppendLine("-- Project = Π_SSDT ∘ E") |> ignore
        sb.AppendLine("-- (synthetic-milestone form: raw text, dependency-free)") |> ignore
        let kindsByModuleKey : Map<SsKey, Module> =
            catalog.Modules
            |> List.collect (fun m -> m.Kinds |> List.map (fun k -> k.SsKey, m))
            |> Map.ofList
        let mutable currentModuleKey : SsKey option = None
        for k in emissionOrder catalog do
            let moduleKey =
                match Map.tryFind k.SsKey kindsByModuleKey with
                | Some m -> Some m.SsKey
                | None -> None
            match moduleKey, currentModuleKey with
            | Some mk, prev when Some mk <> prev ->
                let m = kindsByModuleKey[k.SsKey]
                sb.AppendLine() |> ignore
                sb.Append("-- Module: ").Append(Name.value m.Name)
                    .Append(" (").Append(rootKey m.SsKey).Append(')').AppendLine() |> ignore
                currentModuleKey <- Some mk
            | _ -> ()
            sb.AppendLine() |> ignore
            renderTable catalog sb k
            renderStaticPopulations sb k
        sb.ToString()
