namespace Projection.Pipeline

open Projection.Core

/// SUPPORTING SCOPE (2026-07-08, the business-intent program) — the typed
/// vocabulary for the SUPPORTING (non-payload) rows a partial transfer
/// touches. The flat `tables` list is the payload; `supportingScope`
/// declares WHY each additional table is in play, in BUSINESS terms, and
/// the engine VERIFIES the declared intent against the actual relationship
/// graph. Two families, opposite edge directions:
///
///   References — the payload points AT these (OUTBOUND escaping edges):
///     existing-reference · reference-seed · shared-anchor · static-lookup
///   Dependents — these point AT the payload (INBOUND edges):
///     owned-child · blocked-dependent
///
/// The surface is CANONICAL: the terse `reconcile: ["T:Col"]` strings still
/// parse but resolve INTO this same model (`ofReconcileEntries`) — one
/// conceptual surface, one resolution pipeline. Each relationship DESUGARS
/// onto a primitive that already exists (a load-set addition, a reconcile
/// strategy, an acknowledged exclusion) so the engine's write plane learns
/// only two genuinely-new behaviors (seed-insert-missing; the inbound-orphan
/// gate); everything else is the existing algebra, re-narrated by intent.
[<RequireQualifiedAccess>]
module SupportingScope =

    /// WHY a supporting table is touched. Closed so desugar + verify are
    /// total; per-relationship payload rides INSIDE the case (an owned-child
    /// cannot exist without its parent; a shared-anchor without its anchor).
    [<RequireQualifiedAccess>]
    type SupportingRelationship =
        // References — the payload points AT these.
        /// Match existing target rows by a business `key`; never copy.
        | ExistingReference of key: string
        /// Copy the referenced rows so the FK survives — insert only the rows
        /// the target lacks (the seed-insert-missing write policy).
        | ReferenceSeed
        /// Re-point every payload reference to ONE designated target row
        /// (`anchor`); an optional `key` matches dynamically first, then pins
        /// the rest (the match-then-pin composite).
        | SharedAnchor of anchor: string * key: string option
        /// A reference table both environments carry identically by natural
        /// `key` — match, and hold every matched pair to ZERO divergence (a
        /// drifted lookup is a real integrity fault, not an advisory).
        | StaticLookup of key: string
        // Dependents — these point AT the payload.
        /// The rows are part of the parent's business definition (`ofParent`);
        /// copied along, wiped with the parent under replace. The child→parent
        /// edge is expected to carry OutSystems Delete Rule = Delete (Cascade).
        | OwnedChild of ofParent: string
        /// Discovered dependents deliberately NOT harvested — a confirmed
        /// negative invariant (environment-specific data). Acknowledged so a
        /// replace-wipe does not refuse on their account.
        | BlockedDependent of ofParent: string

    /// One authored `supportingScope` entry — business vocabulary over string
    /// coordinates (`Module.Entity`), mirroring `Flow.Reconcile`'s raw-string
    /// layer; SsKeys resolve later against a live catalog.
    type SupportingScopeEntry =
        { Table        : string
          Relationship : SupportingRelationship
          /// The business intent, in the operator's words. Required — the
          /// surface exists to record WHY, and the board echoes it.
          Reason       : string }

    /// The desugared projection onto the EXISTING transfer mechanics
    /// (catalog-bound, SsKey-typed) — the board's source of truth for
    /// verification, completeness, and the write-policy inputs.
    type ResolvedSupportingScope =
        { /// reference-seed + owned-child — added to the written load set.
          LoadSetAdditions       : Set<SsKey>
          /// The subset of `LoadSetAdditions` that gets the seed-insert-missing
          /// write policy (reference-seed only).
          SeedKinds              : Set<SsKey>
          /// existing-reference + shared-anchor + static-lookup.
          ReconcileAdditions     : Map<SsKey, ReconciliationStrategy>
          /// The subset of `ReconcileAdditions` held to zero divergence.
          StaticLookupKinds      : Set<SsKey>
          /// blocked-dependent — suppresses the inbound-orphan wipe refusal.
          AcknowledgedExclusions : Set<SsKey>
          /// (child, parent) for every owned-child — cascade-verified.
          OwnedChildEdges        : (SsKey * SsKey) list }

    let empty : ResolvedSupportingScope =
        { LoadSetAdditions = Set.empty; SeedKinds = Set.empty
          ReconcileAdditions = Map.empty; StaticLookupKinds = Set.empty
          AcknowledgedExclusions = Set.empty; OwnedChildEdges = [] }

    /// The verdict of checking one declared intent against the real graph.
    [<RequireQualifiedAccess>]
    type ScopeClaimVerdict =
        /// The graph bears out the declaration — the note says what was proven.
        | Confirmed of note: string
        /// The graph contradicts the declaration: what is wrong + the remedy.
        | Contradicted of reason: string * remedy: string

    // -- the canonical bridge: terse `reconcile` strings ARE supporting scope --

    /// Project the parsed terse `reconcile` entries into the unified model.
    /// `T:Col` is an existing-reference; `T:=key` a shared-anchor; `T:Col:=key`
    /// a match-then-pin shared-anchor. The `reason` is synthesized (the terse
    /// form carries none); an explicit `supportingScope` entry supersedes.
    let ofReconcileEntries (entries: TransferSpec.ReconcileEntry list) : SupportingScopeEntry list =
        entries
        |> List.map (fun e ->
            let relationship =
                match e.Rule with
                | TransferSpec.MatchColumn col          -> SupportingRelationship.ExistingReference col
                | TransferSpec.AssignAllTo key          -> SupportingRelationship.SharedAnchor (key, None)
                | TransferSpec.MatchThenAssign (col, k) -> SupportingRelationship.SharedAnchor (k, Some col)
            { Table = e.Table; Relationship = relationship; Reason = "reconcile rule (terse form)" })

    // -- desugar onto the existing STRING vocabularies (the engine path) -------

    /// The reconcile-string form of a reference-family entry (feeds the
    /// existing `TransferSpec.parseReconcileSpec` grammar).
    let private reconcileStringOf (table: string) (r: SupportingRelationship) : string option =
        match r with
        | SupportingRelationship.ExistingReference key -> Some (sprintf "%s:%s" table key)
        | SupportingRelationship.StaticLookup key      -> Some (sprintf "%s:%s" table key)
        | SupportingRelationship.SharedAnchor (anchor, None)     -> Some (sprintf "%s:=%s" table anchor)
        | SupportingRelationship.SharedAnchor (anchor, Some key) -> Some (sprintf "%s:%s:=%s" table key anchor)
        | _ -> None

    /// Desugar to the already-threaded engine inputs — appended to the flow's
    /// `tables` / `reconcile` string lists, so the reconcile-map and load-set
    /// portions need NO new engine field (the reconcileIgnore-discipline
    /// minimum). `Acknowledged` and the seed marking ride the typed `resolve`.
    let desugarToStrings (entries: SupportingScopeEntry list) : {| ExtraTables: string list; ExtraReconcile: string list; Acknowledged: string list |} =
        let extraTables =
            entries |> List.choose (fun e ->
                match e.Relationship with
                | SupportingRelationship.ReferenceSeed
                | SupportingRelationship.OwnedChild _ -> Some e.Table
                | _ -> None)
        let extraReconcile =
            entries |> List.choose (fun e -> reconcileStringOf e.Table e.Relationship)
        let acknowledged =
            entries |> List.choose (fun e ->
                match e.Relationship with
                | SupportingRelationship.BlockedDependent _ -> Some e.Table
                | _ -> None)
        {| ExtraTables = extraTables; ExtraReconcile = extraReconcile; Acknowledged = acknowledged |}

    // -- catalog-bound resolution ---------------------------------------------

    /// Resolve a `Module.Entity` (or physical) table string to its kind —
    /// the same espace-safe logical-first, physical-fallback rule the reconcile
    /// resolver applies (kept local so the bridge and the resolver agree).
    let tryResolveTable (catalog: Catalog) (table: string) : Kind option =
        let physical () =
            Catalog.allKinds catalog
            |> List.tryFind (fun k -> TableId.tableTextEquals table k.Physical)
        match table.IndexOf '.' with
        | dot when dot > 0 ->
            match CatalogResolution.tryKindByLogical catalog (table.Substring(0, dot)) (table.Substring(dot + 1)) with
            | Some key -> Catalog.allKinds catalog |> List.tryFind (fun k -> k.SsKey = key)
            | None -> physical ()
        | _ -> physical ()

    let private notFound (table: string) : ValidationError =
        ValidationError.create "transfer.supportingScope.tableNotFound"
            (sprintf "supportingScope: no table found for '%s' (tried logical Module.Entity and physical name). Use the logical 'Module.Entity' form for a peer transfer." table)

    /// The typed projection — resolves every entry against the catalog and
    /// composes with the existing payload load set. Aggregates conflicts.
    /// The reconcile strategies mirror `TransferSpec.resolveReconciliation`'s
    /// algebra exactly, so the string and typed paths agree by construction.
    let resolve (catalog: Catalog) (entries: SupportingScopeEntry list) : Result<ResolvedSupportingScope> =
        let resolveColumn (kind: Kind) (col: string) : Result<Name> =
            kind.Attributes
            |> List.tryFind (fun a -> System.String.Equals(Name.value a.Name, col, System.StringComparison.OrdinalIgnoreCase))
            |> Option.orElseWith (fun () -> kind.Attributes |> List.tryFind (fun a -> ColumnRealization.columnNameEquals col a.Column))
            |> function
               | Some a -> Result.success a.Name
               | None ->
                   Result.failureOf
                       (ValidationError.create "transfer.supportingScope.columnNotFound"
                           (sprintf "supportingScope: table '%s' has no attribute with name/column '%s'." (Name.value kind.Name) col))
        // Per-entry resolution to a partial ResolvedSupportingScope fragment.
        let resolveOne (e: SupportingScopeEntry) : Result<ResolvedSupportingScope> =
            match tryResolveTable catalog e.Table with
            | None -> Result.failureOf (notFound e.Table)
            | Some kind ->
                let key = kind.SsKey
                match e.Relationship with
                | SupportingRelationship.ReferenceSeed ->
                    Result.success { empty with LoadSetAdditions = Set.singleton key; SeedKinds = Set.singleton key }
                | SupportingRelationship.OwnedChild ofParent ->
                    match tryResolveTable catalog ofParent with
                    | None -> Result.failureOf (notFound ofParent)
                    | Some parent ->
                        Result.success { empty with LoadSetAdditions = Set.singleton key; OwnedChildEdges = [ key, parent.SsKey ] }
                | SupportingRelationship.BlockedDependent ofParent ->
                    match tryResolveTable catalog ofParent with
                    | None -> Result.failureOf (notFound ofParent)
                    | Some _ -> Result.success { empty with AcknowledgedExclusions = Set.singleton key }
                | SupportingRelationship.ExistingReference col ->
                    resolveColumn kind col
                    |> Result.map (fun name -> { empty with ReconcileAdditions = Map.ofList [ key, ReconciliationStrategy.MatchByColumn name ] })
                | SupportingRelationship.StaticLookup col ->
                    resolveColumn kind col
                    |> Result.map (fun name ->
                        { empty with
                            ReconcileAdditions = Map.ofList [ key, ReconciliationStrategy.MatchByColumn name ]
                            StaticLookupKinds = Set.singleton key })
                | SupportingRelationship.SharedAnchor (anchor, None) ->
                    Result.success
                        { empty with
                            ReconcileAdditions =
                                Map.ofList [ key, ReconciliationStrategy.FallbackToAssigned (AssignedKey.ofString anchor, ReconciliationStrategy.ManualOverride Map.empty) ] }
                | SupportingRelationship.SharedAnchor (anchor, Some col) ->
                    resolveColumn kind col
                    |> Result.map (fun name ->
                        { empty with
                            ReconcileAdditions =
                                Map.ofList [ key, ReconciliationStrategy.FallbackToAssigned (AssignedKey.ofString anchor, ReconciliationStrategy.MatchByColumn name) ] })
        let resolved = entries |> List.map resolveOne
        let errors = resolved |> List.collect (function Ok _ -> [] | Error es -> es)
        if not (List.isEmpty errors) then Result.failure errors
        else
            // Merge the fragments; a table appearing in two buckets (a
            // resolution-level duplicate the parser also rejects) collapses
            // cleanly because each entry contributes one kind to one bucket.
            let merged =
                resolved
                |> List.choose (function Ok r -> Some r | _ -> None)
                |> List.fold (fun acc r ->
                    { LoadSetAdditions = Set.union acc.LoadSetAdditions r.LoadSetAdditions
                      SeedKinds = Set.union acc.SeedKinds r.SeedKinds
                      ReconcileAdditions = Map.fold (fun m k v -> Map.add k v m) acc.ReconcileAdditions r.ReconcileAdditions
                      StaticLookupKinds = Set.union acc.StaticLookupKinds r.StaticLookupKinds
                      AcknowledgedExclusions = Set.union acc.AcknowledgedExclusions r.AcknowledgedExclusions
                      OwnedChildEdges = acc.OwnedChildEdges @ r.OwnedChildEdges }) empty
            Result.success merged

    // -- verification: the declared intent vs the actual graph ----------------

    /// Check each entry's structural claim against the catalog graph (PURE —
    /// the live business-key evidence for existing-reference / static-lookup is
    /// a separate probe the go-board face layers on). `payload` is the resolved
    /// `tables` load set; `reconciled` is its reconcile keyset.
    let verify
        (catalog: Catalog)
        (payload: Set<SsKey>)
        (reconciled: Set<SsKey>)
        (entries: SupportingScopeEntry list)
        : (SupportingScopeEntry * ScopeClaimVerdict) list =
        // The escaping targets the payload reaches (reference-family must hit one).
        let escapingTargets =
            TransferSubset.escapingEdges catalog payload reconciled
            |> List.map (fun (_, _, target) -> target.SsKey)
            |> Set.ofList
        // The inbound dependents pointing at the payload (dependent-family must be one).
        let dependentSources =
            TransferSubset.dependentEdges catalog payload
            |> List.map (fun (source, _, _) -> source.SsKey)
            |> Set.ofList
        let referenceClaim (kind: Kind) : ScopeClaimVerdict =
            if Set.contains kind.SsKey escapingTargets
            then ScopeClaimVerdict.Confirmed "a payload relationship points at this table."
            else ScopeClaimVerdict.Contradicted
                    (sprintf "no relationship in the payload points at %s." (Name.value kind.Name),
                     "remove the entry, or add the referencing table to `tables`.")
        entries
        |> List.map (fun e ->
            let verdict =
                match tryResolveTable catalog e.Table with
                | None ->
                    ScopeClaimVerdict.Contradicted
                        (sprintf "the table '%s' is not in the model." e.Table, "correct the Module.Entity name.")
                | Some kind ->
                    match e.Relationship with
                    | SupportingRelationship.ExistingReference _
                    | SupportingRelationship.ReferenceSeed
                    | SupportingRelationship.SharedAnchor _
                    | SupportingRelationship.StaticLookup _ -> referenceClaim kind
                    | SupportingRelationship.OwnedChild ofParent ->
                        match tryResolveTable catalog ofParent with
                        | None -> ScopeClaimVerdict.Contradicted (sprintf "the parent '%s' is not in the model." ofParent, "correct the `of` name.")
                        | Some parent ->
                            let cascadeEdge =
                                kind.References
                                |> List.exists (fun r -> r.TargetKind = parent.SsKey && CycleResolution.classify kind r = EdgeStrength.Cascade)
                            let anyEdge = kind.References |> List.exists (fun r -> r.TargetKind = parent.SsKey)
                            if cascadeEdge then
                                ScopeClaimVerdict.Confirmed (sprintf "owned by %s (delete-rule cascade); wiped with the parent under replace." (Name.value parent.Name))
                            elif anyEdge then
                                ScopeClaimVerdict.Contradicted
                                    (sprintf "%s references %s, but the delete rule protects the rows rather than owning them." (Name.value kind.Name) (Name.value parent.Name),
                                     "declare it a reference (existing-reference / reference-seed), or set the OutSystems delete rule to Delete.")
                            else
                                ScopeClaimVerdict.Contradicted
                                    (sprintf "%s has no relationship to %s." (Name.value kind.Name) (Name.value parent.Name),
                                     "correct the `of`, or remove the entry.")
                    | SupportingRelationship.BlockedDependent ofParent ->
                        match tryResolveTable catalog ofParent with
                        | None -> ScopeClaimVerdict.Contradicted (sprintf "the parent '%s' is not in the model." ofParent, "correct the `of` name.")
                        | Some parent ->
                            if Set.contains kind.SsKey dependentSources then
                                ScopeClaimVerdict.Confirmed (sprintf "a real dependent of %s, deliberately not harvested." (Name.value parent.Name))
                            else
                                ScopeClaimVerdict.Contradicted
                                    (sprintf "%s does not depend on the payload — the exclusion is vacuous." (Name.value kind.Name),
                                     "remove the entry; nothing pulls this table in.")
            e, verdict)

    /// Escaping targets NOT covered by the payload, its reconcile set, or the
    /// reference-family entries — every one is an unclassified relationship the
    /// live run would refuse. Empty means the escape set is fully accounted.
    let unaccountedEscapes
        (catalog: Catalog)
        (payload: Set<SsKey>)
        (reconciled: Set<SsKey>)
        (resolved: ResolvedSupportingScope)
        : (Kind * Reference * Kind) list =
        let covered =
            Set.unionMany
                [ payload; reconciled; resolved.LoadSetAdditions
                  resolved.ReconcileAdditions |> Map.toSeq |> Seq.map fst |> Set.ofSeq ]
        TransferSubset.escapingEdges catalog payload reconciled
        |> List.filter (fun (_, _, target) -> not (Set.contains target.SsKey covered))

    /// Inbound dependents NOT covered by the payload, an owned-child copy, or a
    /// blocked-dependent acknowledgement — under a replace-wipe each would be
    /// orphaned (FK 547). Empty means every dependent is classified.
    let unaccountedDependents
        (catalog: Catalog)
        (payload: Set<SsKey>)
        (resolved: ResolvedSupportingScope)
        : (Kind * Reference * Kind) list =
        let covered = Set.unionMany [ payload; resolved.LoadSetAdditions; resolved.AcknowledgedExclusions ]
        TransferSubset.dependentEdges catalog payload
        |> List.filter (fun (source, _, _) -> not (Set.contains source.SsKey covered))
