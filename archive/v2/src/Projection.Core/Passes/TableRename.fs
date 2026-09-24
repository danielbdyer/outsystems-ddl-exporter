namespace Projection.Core.Passes

// LINT-ALLOW-FILE: pass-driver diagnostic prose + structured key projection. The table-rename
//   pass renders operator-facing validation messages and `schema.table` /
//   `module::entity` rename keys via `sprintf`; the structural rename output
//   is fully typed. Only the message/key text surface uses sprintf, per
//   `DECISIONS 2026-05-09 — Built-in obligation`.

open Projection.Core

/// Operator-supplied physical-realization rewrites applied as a
/// pre-emit Catalog transform. Per V2_PRODUCTION_CUTOVER.md §5.6 and
/// the 2026-05-12 architecture audit:
///
/// 1. Rename rewrites `Kind.Physical` (`TableId`) only. `Kind.SsKey`
///    is preserved (A1: identity survives rename).
/// 2. `Reference.TargetKind` carries `SsKey`, never physical names,
///    so cross-Kind references are automatically rename-safe; the
///    pass does not rewrite References.
/// 3. `TopologicalOrderPass` reads SsKeys only, so rename is
///    structurally orthogonal to topological order (R11 dissolves).
/// 4. Catalog rewrite uses `CatalogTraversal.mapKinds` (the existing
///    primitive established by `NormalizeStaticPopulations` / used by
///    `SymmetricClosure`'s tilted variant). No need for a new
///    traversal helper at this consumer count.
///
/// Validation is fail-fast via `Result<_>`: source-not-found, source-
/// ambiguous, source-duplicate-in-spec-list, and target-collision
/// each surface as structured errors before any rewrite occurs.
[<RequireQualifiedAccess>]
module TableRename =

    /// Pass version. Bump when rewrite semantics change (e.g., a new
    /// payload variant for `TransformKind` lands).
    [<Literal>]
    let version : int = 1

    [<Literal>]
    let private passName : string = "tableRename"

    /// The key identifying which Kind a rename targets. Logical form
    /// (`Module::Entity`) matches by presentation Names; physical
    /// form (`schema.table`) matches the Kind's current `Physical`
    /// coordinate.
    type RenameKey =
        | Logical of moduleName: Name * entityName: Name
        | Physical of source: TableId

    /// One rename: rewrite the Kind matching `Key` to have a new
    /// `Target` physical coordinate. SsKey is preserved; references
    /// to the renamed Kind continue to resolve via SsKey.
    type RenameSpec = {
        Key    : RenameKey
        Target : TableId
    }

    // -----------------------------------------------------------------------
    // Error construction. Codes follow the `rename.<problem>` convention.
    // -----------------------------------------------------------------------

    let private renameError (code: string) (message: string) : ValidationError =
        ValidationError.create (sprintf "rename.%s" code) message

    let private describeKey (key: RenameKey) : string =
        match key with
        | Logical (m, e) -> sprintf "%s::%s" (Name.value m) (Name.value e)
        | Physical t     -> sprintf "%s.%s" (SchemaName.value t.Schema) (TableName.value t.Table)

    let private describeTarget (t: TableId) : string =
        sprintf "%s.%s" (SchemaName.value t.Schema) (TableName.value t.Table)

    // -----------------------------------------------------------------------
    // Resolution. Each RenameKey resolves to exactly one Kind's SsKey.
    // -----------------------------------------------------------------------

    let private matchesLogical (moduleName: Name) (entityName: Name) (c: Catalog) : Kind list =
        c.Modules
        |> List.collect (fun m ->
            if m.Name = moduleName then
                m.Kinds |> List.filter (fun k -> k.Name = entityName)
            else [])

    let private matchesPhysical (source: TableId) (c: Catalog) : Kind list =
        Catalog.allKinds c
        |> List.filter (fun k -> k.Physical = source)

    let private resolveKey (c: Catalog) (key: RenameKey) : Result<SsKey> =
        let candidates =
            match key with
            | Logical (m, e) -> matchesLogical m e c
            | Physical t     -> matchesPhysical t c
        match candidates with
        | [single] -> Result.success single.SsKey
        | []       ->
            Result.failureOf (
                renameError
                    "sourceNotFound"
                    (sprintf "Rename source '%s' did not match any kind in the catalog." (describeKey key)))
        | many ->
            Result.failureOf (
                renameError
                    "sourceAmbiguous"
                    (sprintf
                        "Rename source '%s' matched %d kinds; expected exactly one."
                        (describeKey key)
                        (List.length many)))

    // -----------------------------------------------------------------------
    // Collect renames into a Map<SsKey, TableId> after validating:
    //   - each spec resolves to a single Kind
    //   - no two specs target the same source Kind (sourceDuplicate)
    //   - no two specs map to the same target TableId (targetCollision)
    // Errors are aggregated so the operator sees every malformed entry
    // in one pass.
    // -----------------------------------------------------------------------

    let private collectRenames (c: Catalog) (specs: RenameSpec list) : Result<Map<SsKey, TableId>> =
        let resolved =
            specs
            |> List.map (fun spec ->
                resolveKey c spec.Key
                |> Result.map (fun key -> key, spec))
            |> Result.aggregate
        match resolved with
        | Error es -> Error es
        | Ok pairs ->
            let sourceDuplicates =
                pairs
                |> List.groupBy fst
                |> List.filter (fun (_, xs) -> List.length xs > 1)
                |> List.map (fun (sourceKey, _) ->
                    renameError
                        "sourceDuplicate"
                        (sprintf
                            "Multiple rename specs target the same kind (SsKey root '%s')."
                            (SsKey.rootOriginal sourceKey)))
            let targetCollisions =
                pairs
                |> List.groupBy (fun (_, spec) -> spec.Target)
                |> List.filter (fun (_, xs) -> List.length xs > 1)
                |> List.map (fun (target, _) ->
                    renameError
                        "targetCollision"
                        (sprintf
                            "Multiple rename specs map to the same target '%s'."
                            (describeTarget target)))
            match sourceDuplicates @ targetCollisions with
            | [] ->
                pairs
                |> List.map (fun (key, spec) -> key, spec.Target)
                |> Map.ofList
                |> Result.success
            | errs -> Error errs

    // -----------------------------------------------------------------------
    // Lineage event per rewrite. Carries a typed `PhysicalRename`
    // payload (`before` / `after` TableId pair). Audit readers and
    // diff tools pattern-match on the typed value structurally rather
    // than re-rendering a string. A no-op rename (Before = After) is
    // suppressed at the visitor before the event is constructed.
    // -----------------------------------------------------------------------

    /// Pillar 9 (chapter A.4.7 slice α): table-rename consumes
    /// operator-supplied rename specs and rewrites the physical
    /// realization (`Kind.Physical` only; identity untouched per A1).
    /// Operator intent on the Emission axis — the operator selects
    /// what physical form a kind takes in emitted output. Lands as
    /// registered overlay.
    let private classification : Classification = OperatorIntent Emission

    let private physicallyRenamedEvent (key: SsKey) (before: TableId) (after: TableId) : LineageEvent =
        LineageEvent.forPass passName version classification key
            (PhysicallyRenamed { Before = before; After = after })

    /// Run the pass. Empty spec list short-circuits to a pass-through
    /// with no lineage events. Otherwise validates every spec first
    /// (fail-fast via `Result`), then applies the rewrite through
    /// `CatalogTraversal.mapKinds`, emitting one `PhysicallyRenamed`
    /// event per rewritten kind. No-op renames (target equals the
    /// current physical realization) emit no event.
    // Chapter A.4.7' slice η: `let run` is private; canonical surface is `TableRename.registered.Run`
    let private run (specs: RenameSpec list) (c: Catalog) : Result<Lineage<Catalog>> =
        use _ = Bench.scope "passes.tableRename"
        match specs with
        | [] -> Result.success (Lineage.ofValueAndEvents [] c)
        | _ ->
            match collectRenames c specs with
            | Error es -> Error es
            | Ok renameMap ->
                c
                |> CatalogTraversal.mapKinds (fun events k ->
                    match Map.tryFind k.SsKey renameMap with
                    | Some target when target <> k.Physical ->
                        LineageBuffer.add (physicallyRenamedEvent k.SsKey k.Physical target) events
                        Some { k with Physical = target }
                    | _ -> Some k)
                |> Result.success

    /// Chapter A.4.7 slice γ — factory. Captures operator-supplied
    /// `RenameSpec list` in closure. Single `OperatorIntent Emission`
    /// site — operator chooses what physical form a kind takes in
    /// emitted output. The pass returns `Result<Lineage<Catalog>>`
    /// (validation against the catalog can fail); the Run closure
    /// wraps the Result so the canonical `Lineage<Diagnostics<Catalog>>`
    /// shape holds: on Ok, the lineage flows through with empty
    /// Diagnostics; on Error, the input Catalog passes through
    /// unchanged and the ValidationErrors surface as Diagnostics
    /// entries with `Severity = Error` for downstream observer
    /// inspection.
    let registered (specs: RenameSpec list) : RegisteredTransform<Catalog, Catalog> =
        { Name = passName
          Domain = Schema
          StageBinding = Pass
          Sites =
            [ { SiteName = "rename"
                Classification = classification
                Rationale = "Apply operator-supplied rename specs to kinds' physical realization (Kind.Physical only; identity untouched per A1). Operator chose what physical form each kind takes; lands as Emission-axis overlay." } ]
          Run =
            fun c ->
                match run specs c with
                | Ok lineage -> lineage |> Lineage.map Diagnostics.ofValue
                | Error errs ->
                    let entries =
                        errs
                        |> List.map (fun e ->
                            { Source = passName
                              Severity = DiagnosticSeverity.Error
                              Code = e.Code
                              Message = e.Message
                              SsKey = None
                              Metadata = Map.empty
                              SuggestedConfig = None })
                    Lineage.ofValue (Diagnostics.tellMany entries (Diagnostics.ofValue c))
          Status = Active }
