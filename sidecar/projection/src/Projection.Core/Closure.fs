namespace Projection.Core

/// **Use-case-scoped referential closure** — the data-portability front-end
/// (the "Fully-Qualified Ask"). Given a curated set of root rows, accumulate
/// every row those rows referentially *require* (their parents, transitively),
/// producing a self-contained, referentially-closed fragment that is itself a
/// valid database (every populated FK resolves within the fragment).
///
/// **PURE — zero I/O (CLAUDE.md §5).** The walk is inherently iterative I/O
/// (each hop's parent-key set depends on the prior hop's *fetched* rows, read
/// live by a scoped `SELECT`). It is factored as the `EvidenceCache`/
/// `LiveProfiler` "discover-once, derive-pure" shape: this module is a **pure
/// planner** that, given the rows accumulated so far, emits the next batch of
/// scoped row-fetches (`RowKeyFetch`); an adapter *oracle* executes them and
/// folds the rows back through `step`. The fixed point is reached when `step`
/// returns no further fetches. The whole engine is therefore unit-testable
/// against a `Map`-backed fake oracle — no database.
///
/// **Slice 1a (foundation).** Single-PK kinds, UP closure: follow every
/// *populated* parent FK (the referential-completeness guarantee — a populated
/// FK whose parent is absent would be a dangling reference). Per-relationship
/// traversal directives (up / down-bounded / stop / predicated), the closure
/// report, and composite keys land in later slices; the directive layer will
/// *override* this default-close-all-parents behaviour, never weaken the
/// completeness floor.
[<RequireQualifiedAccess>]
module Closure =

    /// A request to fetch the rows of one `Kind` whose primary-key value is in
    /// `Keys`. `KeyColumn` is the parent's PK **attribute `Name`** (the key
    /// space `StaticRow.Values` speaks; the adapter resolves it to the physical
    /// column and renders `SELECT … WHERE <col> IN (Keys)`). `Keys` is a set so
    /// the planner de-duplicates the IN-list before it ever reaches the wire.
    type RowKeyFetch =
        { Kind      : SsKey
          KeyColumn : Name
          Keys      : Set<string> }

    /// The rows an oracle fetched for one kind, fed back into `step`.
    type FetchedRows =
        { Kind : SsKey
          Rows : StaticRow list }

    /// The accumulating closed row-set.
    ///   * `Rows` — per kind, `pkValue → row`. The PK value is the FK join
    ///     handle, so keying by it de-duplicates rows reached via multiple
    ///     paths (FR2: "dedup rows reached by multiple paths") structurally.
    ///   * `Requested` — per kind, the PK values already placed in a fetch.
    ///     The fixed point never re-requests a key that is already closed *or*
    ///     in flight; crucially, a requested key that returns **no** row (a
    ///     dangling parent) stays here and is never re-requested, so the walk
    ///     terminates instead of spinning. Slice 2's closure report reads
    ///     `Requested \ Rows` to name the dangling mandatory parents.
    type ClosureState =
        { Rows      : Map<SsKey, Map<string, StaticRow>>
          Requested : Map<SsKey, Set<string>> }

    /// The empty closed set — the walk's starting point. The driver feeds the
    /// root rows to `step empty` as the first `FetchedRows`.
    let empty : ClosureState =
        { Rows = Map.empty; Requested = Map.empty }

    /// The single-column primary-key attribute `Name` of a kind, if it has
    /// exactly the OutSystems-canonical single PK. `None` for a PK-less kind
    /// or a composite key (composite keys are Slice 5 territory) — such a kind
    /// is not *followed into* in Slice 1a (no fetch is emitted for it), which
    /// Slice 2's report surfaces if a populated FK needed it.
    let private pkColumnName (k: Kind) : Name option =
        match Kind.primaryKey k with
        | [ a ] -> Some a.Name
        | _     -> None

    /// The PK-value dedup handle of a row under a kind's PK name.
    let private rowKeyOf (pkName: Name) (row: StaticRow) : string =
        StaticRow.valueOrEmpty pkName row

    /// Fold one kind's freshly-fetched rows into the state, returning the
    /// updated state and the rows that were *new* (not already closed) — only
    /// new rows drive the next hop's fetches.
    let private foldFetched
        (catalog: Catalog)
        (state: ClosureState)
        (fetched: FetchedRows)
        : ClosureState * (Kind * StaticRow list) option =
        match Catalog.tryFindKind fetched.Kind catalog with
        | None ->
            // A fetch for a kind absent from the catalog: nothing to fold and
            // nothing to follow. (Unreachable for adapter-emitted fetches,
            // whose targets are catalog references; total for a fake oracle.)
            state, None
        | Some kind ->
            match pkColumnName kind with
            | None ->
                // PK-less / composite kind: cannot dedup by PK value in 1a.
                // Emit no onward fetches for it (Slice 2's report names a
                // populated FK that needed an unfollowable parent).
                state, Some (kind, [])
            | Some pkName ->
                let existing =
                    state.Rows |> Map.tryFind fetched.Kind |> Option.defaultValue Map.empty
                let folded, fresh =
                    fetched.Rows
                    |> List.fold
                        (fun (acc: Map<string, StaticRow>, fresh) row ->
                            let key = rowKeyOf pkName row
                            if Map.containsKey key acc then acc, fresh
                            else Map.add key row acc, row :: fresh)
                        (existing, [])
                { state with Rows = Map.add fetched.Kind folded state.Rows },
                Some (kind, List.rev fresh)

    /// From a batch of freshly-closed rows, derive the next hop's fetches: for
    /// each populated FK on each new row, request its parent by PK value,
    /// excluding any key already closed or already requested.
    let private nextFetches
        (catalog: Catalog)
        (state: ClosureState)
        (newRowsByKind: (Kind * StaticRow list) list)
        : ClosureState * RowKeyFetch list =
        // Gather candidate (targetKind, parentKeyValue) demands.
        let demands =
            [ for (kind, rows) in newRowsByKind do
                for r in kind.References do
                    match Kind.tryFindAttribute r.SourceAttribute kind with
                    | None -> ()
                    | Some fkAttr ->
                        for row in rows do
                            let v = StaticRow.valueOrEmpty fkAttr.Name row
                            if v <> "" then yield r.TargetKind, v ]
        // Group by target kind, subtract what is already closed / in flight.
        let byTarget =
            demands
            |> List.fold
                (fun (acc: Map<SsKey, Set<string>>) (target, v) ->
                    let cur = Map.tryFind target acc |> Option.defaultValue Set.empty
                    Map.add target (Set.add v cur) acc)
                Map.empty
        byTarget
        |> Map.fold
            (fun (st, fetches) target candidateKeys ->
                match Catalog.tryFindKind target catalog with
                | None -> st, fetches
                | Some parent ->
                    match pkColumnName parent with
                    | None -> st, fetches
                    | Some parentPk ->
                        let closed =
                            st.Rows |> Map.tryFind target |> Option.defaultValue Map.empty
                            |> fun m -> m |> Map.toSeq |> Seq.map fst |> Set.ofSeq
                        let requested =
                            st.Requested |> Map.tryFind target |> Option.defaultValue Set.empty
                        let fresh = Set.difference candidateKeys (Set.union closed requested)
                        if Set.isEmpty fresh then st, fetches
                        else
                            let st' =
                                { st with
                                    Requested =
                                        Map.add target (Set.union requested fresh) st.Requested }
                            st', { Kind = target; KeyColumn = parentPk; Keys = fresh } :: fetches)
            (state, [])

    /// One closure step: fold the oracle's fetched rows into the state, then
    /// emit the next batch of parent fetches. The driver loops `oracle ∘ step`
    /// until the returned fetch list is empty — that empty list **is** the
    /// referential fixed point.
    let step
        (catalog: Catalog)
        (state: ClosureState)
        (fetched: FetchedRows list)
        : ClosureState * RowKeyFetch list =
        let state1, newRows =
            fetched
            |> List.fold
                (fun (st, acc) fr ->
                    match foldFetched catalog st fr with
                    | st', Some kindRows -> st', kindRows :: acc
                    | st', None          -> st', acc)
                (state, [])
        nextFetches catalog state1 newRows

    /// Materialize the closed set as the `Map<SsKey, StaticRow list>` the load
    /// engine consumes (the same shape `DataLoadPlan.build` takes — the closed
    /// row-set plugs in at the altitude σ does in `Transfer.runSynthetic`).
    /// Rows are sorted by their stable `Identifier` so the materialization is
    /// deterministic (T1: byte-identical from byte-identical input).
    let materialize (state: ClosureState) : Map<SsKey, StaticRow list> =
        state.Rows
        |> Map.map (fun _ rowsByKey ->
            rowsByKey
            |> Map.toList
            |> List.map snd
            |> List.sortBy (fun r -> r.Identifier))

    /// The total row count across the closed set — a cheap closure summary
    /// (the structured `ClosureReport` is Slice 2).
    let rowCount (state: ClosureState) : int =
        state.Rows |> Map.fold (fun n _ m -> n + Map.count m) 0
