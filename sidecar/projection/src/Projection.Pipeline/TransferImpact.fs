namespace Projection.Pipeline

open Projection.Core

/// THE TRANSFER IMPACT MODEL (2026-07-09, the precise-impact program) — the
/// operator's answer to "show me EXACTLY what happens to the data", not "trust
/// that `tables` + `supportingScope` are blessed". Where the go board's forecast
/// gives per-table BEFORE→AFTER counts and first-N flat previews, this denormalizes
/// each connected SEGMENT of the transfer graph into nested DOCUMENTS — a root
/// entity with its owned children conjoined beneath it and its referenced parents
/// inlined — and classifies every row against the sink's current state
/// (added / deleted / changed / unchanged). Delta rows are carried in full; the
/// unchanged remainder is a COUNT, so the artifact stays precise without dumping
/// hundreds of inline rows. Pure: the face supplies the before (sink) and after
/// (source) rows the dry run already read; the renderer (`TransferImpactView`)
/// turns this model into the self-contained HTML artifact + the JSON twin.
[<RequireQualifiedAccess>]
module TransferImpact =

    /// How a row changes between the sink's current state and the transfer's result.
    [<RequireQualifiedAccess>]
    type ChangeKind =
        /// Present in the source (after), absent from the sink (before).
        | Added
        /// Present in the sink (before), removed by the transfer (a wipe deletes it,
        /// or a converge-delete). Only a destructive strategy produces deletions.
        | Deleted
        /// Matched by business key on both sides; one or more non-key columns differ.
        | Changed
        /// Matched by business key on both sides; every compared column agrees.
        | Unchanged

    /// One non-key column that differs on a matched (Changed) row.
    type FieldDiff =
        { Column : Name
          Before : string
          After  : string }

    /// One denormalized entity instance and everything conjoined to it — the
    /// "document" unit. `Children` nests OWNED children (kinds whose FK points at
    /// this one) by relationship label; `Refs` inlines the REFERENCED parents this
    /// entity points at (typically reconciled reference data — matched, not copied).
    type EntityNode =
        { Kind       : SsKey
          /// The identifying key value shown to the operator (the sink PK, or the
          /// business key when the kind is matched by one).
          KeyValue   : string
          /// Display columns (non-key), in attribute order.
          Attributes : (Name * string) list
          Change     : ChangeKind
          /// Populated for `Changed` — the columns that differ, with before/after.
          Diffs      : FieldDiff list
          /// Inlined referenced parents: (relationship label, resolved parent display).
          Refs       : (string * string) list
          /// Owned children nested by relationship label.
          Children   : (string * EntityNode list) list }

    /// Per-table before/after tally — the context that keeps the delta honest
    /// without listing the unchanged rows.
    type TableContext =
        { Kind       : SsKey
          SinkBefore : int
          Added      : int
          Deleted    : int
          Changed    : int
          Unchanged  : int }

    /// The relational ROLE a table plays in the transfer — the `supportingScope`
    /// variety (`static-lookup` / `existing-reference` / … / `payload` for a
    /// declared table), the operator's `reason`, the `guarantee` that role earns
    /// (`SupportingScope.guaranteeOf`), the business key it is matched by, and —
    /// for reference/lookup kinds — the 1:1 IDENTITY verdict (`Some "identical
    /// (168/168 by IsoCode)"` or a drift summary). This is what lets the artifact
    /// answer "make sense of the variety" and "why the 1:1 check was necessary".
    type RelationalRole =
        { Variety   : string
          Reason    : string
          Guarantee : string
          Key       : string option
          Verdict   : string option }

    /// The default role for a declared payload table (no supportingScope entry).
    let payloadRole : RelationalRole =
        { Variety = "payload"; Reason = "declared in `tables`"; Guarantee = ""; Key = None; Verdict = None }

    /// One row of the summary matrix — a table, its relational role, and its delta.
    type SummaryRow =
        { Kind : SsKey; Role : RelationalRole; Context : TableContext }

    /// One connected component of the transfer graph, denormalized.
    type Segment =
        { /// The kinds in this component, parents-before-children where derivable.
          Members   : SsKey list
          /// The root kinds (top of the owned hierarchy — they own children and
          /// point only at reference data, not at other payload).
          Roots     : SsKey list
          /// The changed root documents, denormalized (unchanged roots are counted
          /// in `Context`, never listed).
          Documents : EntityNode list
          Context   : TableContext list }

    type Totals =
        { Added : int; Deleted : int; Changed : int; Unchanged : int }

    type Impact =
        { Flow     : string
          Strategy : string
          /// The summary matrix — every tracked + supporting table with its
          /// relational role and delta, for the scannable variety-grouped index.
          Summary  : SummaryRow list
          Segments : Segment list
          Totals   : Totals }

    // -- the inputs the face assembles from the dry run -----------------------

    /// Everything the pure build consumes. `Before` is the sink's current rows per
    /// kind (the dry run's read); `After` is the rows the transfer would load
    /// (the source, in display space). `Wiped` names the kinds a destructive
    /// strategy deletes; `BusinessKeys` names the column a kind is matched by when
    /// it has a stable one (reconciled / static-lookup / existing-reference) — the
    /// key that lets before↔after match into `Changed`/`Unchanged` rather than a
    /// blunt delete-all + add-all.
    type Inputs =
        { Catalog      : Catalog
          Scope        : Set<SsKey>
          Reconciled   : Set<SsKey>
          Wiped        : Set<SsKey>
          BusinessKeys : Map<SsKey, Name>
          Before       : Map<SsKey, StaticRow list>
          After        : Map<SsKey, StaticRow list>
          /// Audit columns excluded from the change compare (the flow's reconcileIgnore).
          Ignore       : Set<Name>
          /// The relational role per kind (from `supportingScope` + the static-lookup
          /// verdict); a kind absent from the map is a plain payload table.
          Roles        : Map<SsKey, RelationalRole> }

    // -- small catalog helpers ------------------------------------------------

    let private pkNameOf (catalog: Catalog) (kind: SsKey) : Name option =
        Catalog.tryFindKind kind catalog
        |> Option.bind (fun k -> match Kind.primaryKey k with pk :: _ -> Some pk.Name | [] -> None)

    let private nameOf (catalog: Catalog) (kind: SsKey) : string =
        match Catalog.tryFindKind kind catalog with
        | Some k -> Name.value k.Name
        | None   -> SsKey.rootOriginal kind

    /// The in-scope FK edges of a kind: (relationship label, FK column name, target kind).
    let private edgesOf (catalog: Catalog) (scope: Set<SsKey>) (kind: SsKey) : (string * Name * SsKey) list =
        match Catalog.tryFindKind kind catalog with
        | None -> []
        | Some k ->
            k.References
            |> List.filter (fun r -> Set.contains r.TargetKind scope)
            |> List.choose (fun r ->
                let col =
                    k.Attributes
                    |> List.tryFind (fun a -> a.SsKey = r.SourceAttribute)
                    |> Option.map (fun a -> a.Name)
                col |> Option.map (fun c -> Name.value r.Name, c, r.TargetKind))

    let private valueOf (col: Name) (row: StaticRow) : string =
        Map.tryFind col row.Values |> Option.defaultValue ""

    // -- 1. segmentation: weakly-connected components over the FK graph -------

    /// Group the scope kinds into weakly-connected components (an FK edge in either
    /// direction joins two kinds). Deterministic: components and their members are
    /// sorted by SsKey root.
    let segmentKinds (catalog: Catalog) (scope: Set<SsKey>) : SsKey list list =
        let kinds = scope |> Set.toList
        // adjacency (undirected) over in-scope FK edges
        let neighbours =
            kinds
            |> List.map (fun k ->
                let outs = edgesOf catalog scope k |> List.map (fun (_, _, t) -> t)
                let ins  =
                    kinds |> List.filter (fun other ->
                        edgesOf catalog scope other |> List.exists (fun (_, _, t) -> t = k))
                k, Set.ofList (outs @ ins))
            |> Map.ofList
        let mutable seen = Set.empty
        let mutable comps : SsKey list list = []
        for start in kinds do
            if not (Set.contains start seen) then
                // BFS
                let mutable frontier = [ start ]
                let mutable comp = []
                seen <- Set.add start seen
                while not (List.isEmpty frontier) do
                    let n = List.head frontier
                    frontier <- List.tail frontier
                    comp <- n :: comp
                    for nb in Map.tryFind n neighbours |> Option.defaultValue Set.empty do
                        if not (Set.contains nb seen) then
                            seen <- Set.add nb seen
                            frontier <- nb :: frontier
                comps <- (comp |> List.sortBy SsKey.rootOriginal) :: comps
        comps |> List.sortBy (fun c -> c |> List.map SsKey.rootOriginal |> List.min)

    // -- 2. classification: add / delete / change / unchanged per kind --------

    /// Classify one kind's rows into (row, change, diffs). A kind with a business
    /// key matches before↔after by that key (→ Changed / Unchanged / Added, plus
    /// Deleted for before-only rows under a wipe). A kind WITHOUT one cannot match
    /// stably (sink-minted surrogates), so every source row is an Add and — under a
    /// wipe — every sink row a Delete. The `after` row carries the display body for
    /// Added/Changed; the `before` row for Deleted.
    let classifyKind (inputs: Inputs) (kind: SsKey) : (StaticRow * ChangeKind * FieldDiff list) list =
        let before = Map.tryFind kind inputs.Before |> Option.defaultValue []
        let after  = Map.tryFind kind inputs.After  |> Option.defaultValue []
        let wiped  = Set.contains kind inputs.Wiped
        // The surrogate PK is environment-specific (sink-minted), so it is never a
        // "change" — excluded from the compare alongside the business key + ignore.
        let pk = pkNameOf inputs.Catalog kind
        match Map.tryFind kind inputs.BusinessKeys with
        | Some bk ->
            let keyOf (r: StaticRow) = Map.tryFind bk r.Values |> Option.filter (fun v -> not (System.String.IsNullOrWhiteSpace v))
            let beforeByKey = before |> List.choose (fun r -> keyOf r |> Option.map (fun k -> k, r)) |> List.rev |> Map.ofList
            let afterKeys   = after  |> List.choose keyOf |> Set.ofList
            let matchedOrAdded =
                after
                |> List.choose (fun a ->
                    match keyOf a with
                    | None -> Some (a, ChangeKind.Added, [])
                    | Some k ->
                        match Map.tryFind k beforeByKey with
                        | None -> Some (a, ChangeKind.Added, [])
                        | Some b ->
                            let cols =
                                Set.union
                                    (a.Values |> Map.toList |> List.map fst |> Set.ofList)
                                    (b.Values |> Map.toList |> List.map fst |> Set.ofList)
                            let diffs =
                                cols
                                |> Set.toList
                                |> List.filter (fun c -> c <> bk && (match pk with Some p -> c <> p | None -> true) && not (Set.contains c inputs.Ignore))
                                |> List.choose (fun c ->
                                    let bv, av = valueOf c b, valueOf c a
                                    if bv = av then None else Some { Column = c; Before = bv; After = av })
                                |> List.sortBy (fun d -> Name.value d.Column)
                            Some (a, (if List.isEmpty diffs then ChangeKind.Unchanged else ChangeKind.Changed), diffs))
            let deleted =
                if not wiped then []
                else before |> List.choose (fun b -> match keyOf b with Some k when not (Set.contains k afterKeys) -> Some (b, ChangeKind.Deleted, []) | _ -> None)
            matchedOrAdded @ deleted
        | None ->
            let adds = after |> List.map (fun a -> a, ChangeKind.Added, [])
            let dels = if wiped then before |> List.map (fun b -> b, ChangeKind.Deleted, []) else []
            adds @ dels

    // -- 3. denormalization: nested documents per changed root ----------------

    /// The root kinds of a segment: those that own children yet point (via FK) only
    /// at reference data (reconciled kinds), never at another payload kind — the top
    /// of the owned hierarchy. A segment of one kind is its own root.
    let private rootsOf (catalog: Catalog) (scope: Set<SsKey>) (reconciled: Set<SsKey>) (members: SsKey list) : SsKey list =
        let memberSet = Set.ofList members
        let pointsAtPayload (k: SsKey) =
            edgesOf catalog scope k
            |> List.exists (fun (_, _, t) -> Set.contains t memberSet && not (Set.contains t reconciled))
        match members |> List.filter (fun k -> not (Set.contains k reconciled) && not (pointsAtPayload k)) with
        | []    -> members |> List.filter (fun k -> not (Set.contains k reconciled))   // a cycle — every payload kind is a root
        | roots -> roots

    /// Build the denormalized document for one parent ROW: attach the child rows
    /// whose FK column resolves to this row's PK, recursively, and inline the
    /// reconciled parents this row references. `changeOf` maps a row to its verdict.
    let rec private documentOf
        (inputs: Inputs)
        (rowsByKind: Map<SsKey, (StaticRow * ChangeKind * FieldDiff list) list>)
        (depthGuard: Set<SsKey>)
        (kind: SsKey)
        (row: StaticRow, change: ChangeKind, diffs: FieldDiff list)
        : EntityNode =
        let pk = pkNameOf inputs.Catalog kind
        let keyVal =
            match Map.tryFind kind inputs.BusinessKeys with
            | Some bk -> valueOf bk row
            | None    -> pk |> Option.map (fun p -> valueOf p row) |> Option.defaultValue ""
        let attrs =
            row.Values
            |> Map.toList
            |> List.filter (fun (c, _) -> (match pk with Some p -> c <> p | None -> true) && not (Set.contains c inputs.Ignore))
        // Inline the referenced parents (reconciled refs) this row points at.
        let refs =
            edgesOf inputs.Catalog inputs.Scope kind
            |> List.filter (fun (_, _, t) -> Set.contains t inputs.Reconciled)
            |> List.choose (fun (label, fkCol, target) ->
                let fkVal = valueOf fkCol row
                if System.String.IsNullOrWhiteSpace fkVal then None
                else
                    // Resolve the parent's display by matching its PK to the FK value.
                    let parentDisplay =
                        match pkNameOf inputs.Catalog target with
                        | Some ppk ->
                            Map.tryFind target inputs.Before |> Option.defaultValue []
                            |> List.tryFind (fun pr -> valueOf ppk pr = fkVal)
                            |> Option.map (fun pr ->
                                let firstText =
                                    pr.Values |> Map.toList |> List.filter (fun (c, _) -> c <> ppk) |> List.tryHead
                                    |> Option.map snd |> Option.defaultValue fkVal
                                sprintf "%s %s (matched, kept)" (nameOf inputs.Catalog target) firstText)
                        | None -> None
                    parentDisplay |> Option.map (fun d -> label, d))
        // Nest the owned children: kinds (in scope, not reconciled) whose FK points
        // at THIS kind, joined row-wise by FK value = this row's PK.
        let children =
            if Set.contains kind depthGuard then []
            else
                inputs.Scope
                |> Set.toList
                |> List.filter (fun childKind -> not (Set.contains childKind inputs.Reconciled))
                |> List.collect (fun childKind ->
                    edgesOf inputs.Catalog inputs.Scope childKind
                    |> List.filter (fun (_, _, t) -> t = kind)
                    |> List.map (fun (label, fkCol, _) -> label, childKind, fkCol))
                |> List.choose (fun (label, childKind, fkCol) ->
                    match pk with
                    | None -> None
                    | Some p ->
                        let parentKey = valueOf p row
                        let childRows =
                            Map.tryFind childKind rowsByKind |> Option.defaultValue []
                            |> List.filter (fun (cr, _, _) -> valueOf fkCol cr = parentKey)
                        if List.isEmpty childRows then None
                        else
                            let nested =
                                childRows
                                |> List.map (documentOf inputs rowsByKind (Set.add kind depthGuard) childKind)
                            Some (label, nested))
        { Kind = kind
          KeyValue = keyVal
          Attributes = attrs
          Change = change
          Diffs = diffs
          Refs = refs
          Children = children }

    // -- 4. assembly ----------------------------------------------------------

    let build (flow: string) (strategy: string) (inputs: Inputs) : Impact =
        let rowsByKind =
            inputs.Scope |> Set.toList |> List.map (fun k -> k, classifyKind inputs k) |> Map.ofList
        let contextOf (kind: SsKey) : TableContext =
            let rows = Map.tryFind kind rowsByKind |> Option.defaultValue []
            let count c = rows |> List.filter (fun (_, ck, _) -> ck = c) |> List.length
            { Kind = kind
              SinkBefore = Map.tryFind kind inputs.Before |> Option.defaultValue [] |> List.length
              Added = count ChangeKind.Added
              Deleted = count ChangeKind.Deleted
              Changed = count ChangeKind.Changed
              Unchanged = count ChangeKind.Unchanged }
        let segments =
            segmentKinds inputs.Catalog inputs.Scope
            |> List.map (fun members ->
                let roots = rootsOf inputs.Catalog inputs.Scope inputs.Reconciled members
                // A changed root is one whose own row changed OR that has changed
                // descendants — but slice 1 documents every non-Unchanged root row.
                let documents =
                    roots
                    |> List.collect (fun rk ->
                        Map.tryFind rk rowsByKind |> Option.defaultValue []
                        |> List.filter (fun (_, ck, _) -> ck <> ChangeKind.Unchanged)
                        |> List.map (documentOf inputs rowsByKind Set.empty rk))
                { Members = members
                  Roots = roots
                  Documents = documents
                  Context = members |> List.map contextOf })
        let sumBy f = segments |> List.collect (fun s -> s.Context) |> List.sumBy f
        // The summary matrix — every scope kind with its role + delta, sorted by
        // variety (payload first, then the reference/dependent families) then name.
        let varietyRank (v: string) =
            match v with
            | "payload" -> 0 | "static-lookup" -> 1 | "existing-reference" -> 2
            | "reference-seed" -> 3 | "shared-anchor" -> 4 | "owned-child" -> 5
            | "blocked-dependent" -> 6 | _ -> 7
        let summary =
            inputs.Scope
            |> Set.toList
            |> List.map (fun k ->
                let role = Map.tryFind k inputs.Roles |> Option.defaultValue payloadRole
                { Kind = k; Role = role; Context = contextOf k })
            |> List.sortBy (fun r -> varietyRank r.Role.Variety, nameOf inputs.Catalog r.Kind)
        { Flow = flow
          Strategy = strategy
          Summary = summary
          Segments = segments
          Totals =
            { Added = sumBy (fun c -> c.Added)
              Deleted = sumBy (fun c -> c.Deleted)
              Changed = sumBy (fun c -> c.Changed)
              Unchanged = sumBy (fun c -> c.Unchanged) } }
