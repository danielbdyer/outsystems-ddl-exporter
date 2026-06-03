namespace Projection.Core

/// Per-rename evidence carried inside `CatalogDiff`. The `PassVersion`
/// mirrors the pass-version literal that other lineage events carry
/// (see `NamingMorphism.fs:23` for the canonical pattern). It documents
/// which V2 pass produced this rename evidence — chapter 4.x cross-
/// version triage uses it when refactor-log entries from older pass
/// versions need re-evaluation. Per `CHAPTER_3_PRESCOPE_REFACTORLOG_AND_CATALOG_DIFF.md`
/// §2, this is the load-bearing record carried inside the diff's
/// `Renamed` map.
type RenameRecord =
    {
        OldName     : Name
        NewName     : Name
        PassVersion : int
    }

/// 6.A.10 — a single facet of an attribute's emitted column shape. Mirrors
/// the column-fidelity surface `PhysicalSchema.diff` compares (type,
/// nullable, PK, length, precision, scale, identity, default, computed) —
/// the facets a *minimum-viable-touch* ALTER must reproduce. Closed DU;
/// widen when a consumer needs a finer facet (ext-props, external storage).
/// Kind-level `CatalogDiff` was attribute-blind (a `TEXT → NVARCHAR(256)`
/// produced no diff signal → no ALTER → silent full-redeploy); this names
/// the changed facet so the delta is computable.
[<RequireQualifiedAccess>]
type AttributeFacet =
    | DataType
    | Nullability
    | PrimaryKey
    | Length
    | Precision
    | Scale
    | Identity
    | DefaultValue
    | Computed

/// An attribute present in BOTH source and target (same `SsKey`) whose
/// emitted column shape differs. `Facets` names every facet that changed
/// and is non-empty by construction — an attribute with no changed facet
/// is structurally `Unchanged`, never `Changed`.
type AttributeChange =
    {
        AttributeKey : SsKey
        Facets       : Set<AttributeFacet>
    }

/// Per-kind attribute-level diff for a kind present in BOTH catalogs.
/// `Added` / `Removed` are attribute `SsKey`s present in only the target /
/// source; `Renamed` carries a same-`SsKey` `Name` change (edge-case-2 of
/// the chapter-3.5 prescope — a renamed attribute on an unrenamed kind);
/// `Changed` names attributes whose emitted column shape differs facet by
/// facet. An empty `AttributeDiff` (all four empty) means the kind's
/// attributes are identical; `between` stores only non-empty diffs.
type AttributeDiff =
    {
        Added   : Set<SsKey>
        Removed : Set<SsKey>
        Renamed : Map<SsKey, RenameRecord>
        Changed : AttributeChange list
    }

// -- C1 (2026-06-02): widen the captured surface beyond column shape --------
//
// The 6.A.10 attribute surface captured kind + attribute column-shape only;
// references, indexes, and sequences rode through `applyDiff` unchanged
// (`CatalogDiff.fs:380-388`, the documented modulus). So `migrate A B`
// silently no-op'd any FK / index / sequence change between A and B. C1 adds
// the three missing change channels so the round-trip law
// `applyDiff (between A B) A = B` holds on them too. Each channel mirrors the
// `AttributeDiff` shape (Added / Removed / Renamed / Changed, keyed by SsKey),
// and applyDiff PATCHES the base record's fields / COPIES whole records from
// the recorded target — it never reconstructs via a smart constructor, so the
// `{ create … with … }` default-substitution bomb cannot bite here.

/// C1 — a single facet of a reference's emitted FK shape. `Trust` carries the
/// `IsConstraintTrusted` (WITH NOCHECK) state the Decision/Schema round-trip
/// depends on; `DbConstraint` the logical-vs-materialized distinction.
[<RequireQualifiedAccess>]
type ReferenceFacet =
    | Target
    | SourceAttribute
    | OnDelete
    | OnUpdate
    | UserFk
    | DbConstraint
    | Trust

/// A reference present in BOTH source and target (same `SsKey`) whose emitted
/// FK shape differs. `Facets` is non-empty by construction.
type ReferenceChange =
    {
        ReferenceKey : SsKey
        Facets       : Set<ReferenceFacet>
    }

/// Per-kind reference-level diff (kind present in both catalogs). Mirrors
/// `AttributeDiff`; references match by `SsKey`.
type ReferenceDiff =
    {
        Added   : Set<SsKey>
        Removed : Set<SsKey>
        Renamed : Map<SsKey, RenameRecord>
        Changed : ReferenceChange list
    }

/// C1 — a single facet of an index's emitted shape. `Options` groups the
/// `WITH (…)` knobs + the platform-auto / disabled flags so the facet set
/// stays small; `ExtendedProperties` are deliberately NOT a facet (parity with
/// the attribute surface, which also defers ext-props to 6.A.6).
[<RequireQualifiedAccess>]
type IndexFacet =
    | Columns
    | Uniqueness
    | IncludedColumns
    | Filter
    | DataSpace
    | Options

/// An index present in BOTH source and target (same `SsKey`) whose emitted
/// shape differs. `Facets` is non-empty by construction.
type IndexChange =
    {
        IndexKey : SsKey
        Facets   : Set<IndexFacet>
    }

/// Per-kind index-level diff (kind present in both catalogs). Mirrors
/// `AttributeDiff`; indexes match by `SsKey`.
type IndexDiff =
    {
        Added   : Set<SsKey>
        Removed : Set<SsKey>
        Renamed : Map<SsKey, RenameRecord>
        Changed : IndexChange list
    }

/// C1 — a single facet of a sequence's shape. `Cache` groups `CacheMode` +
/// `CacheSize`.
[<RequireQualifiedAccess>]
type SequenceFacet =
    | Schema
    | DataType
    | StartValue
    | Increment
    | Minimum
    | Maximum
    | Cycle
    | Cache

/// A sequence present in BOTH source and target (same `SsKey`) whose shape
/// differs. `Facets` is non-empty by construction.
type SequenceChange =
    {
        SequenceKey : SsKey
        Facets      : Set<SequenceFacet>
    }

/// Catalog-level sequence diff (sequences are `Catalog.Sequences`, not
/// kind-scoped). Mirrors `AttributeDiff`; sequences match by `SsKey`.
type SequenceDiff =
    {
        Added   : Set<SsKey>
        Removed : Set<SsKey>
        Renamed : Map<SsKey, RenameRecord>
        Changed : SequenceChange list
    }

/// Total decomposition of `source ∪ target` SsKeys into four pairwise-
/// disjoint partitions. The smart constructor `CatalogDiff.between`
/// enforces exhaustiveness — every `SsKey` in `Catalog.allKinds source`
/// or `Catalog.allKinds target` is in exactly one of `Renamed`,
/// `Added`, `Removed`, `Unchanged`. The type cannot inhabit an
/// inconsistent state because callers cannot construct it without
/// going through the smart constructor.
///
/// Chapter 3.5 first-slice scope: kind-level diff (the SsKey set is
/// `Catalog.allKinds`'s SsKeys, not the broader flat tree of
/// kinds + attributes + references + indexes). Attribute-level
/// renames defer to a follow-on slice under consumer pressure
/// (the audit-deferred Tier-2 #15 pattern of expanding under
/// closed-DU empirical-test discipline applies — exhaustiveness
/// errors light up at named call sites only).
///
/// Per `Types.fs` `EmitterOverDiff<'element> = CatalogDiff -> Result<
/// ArtifactByKind<'element>, EmitError>` and the chapter 3.5 prescope
/// §4: `RefactorLogEmitter` consumes a `CatalogDiff` and produces an
/// `ArtifactByKind<RefactorLogEntry list>` keyed on the *target*
/// Catalog's SsKey set. T11 (sibling-Π commutativity, structural
/// type encoding) extends to diff-typed inputs by typing
/// `ArtifactByKind` over the target Catalog rather than the source.
/// The amendment `T11 amended again (diff-typed inputs)` codifies
/// this extension.
type CatalogDiff = private CatalogDiff of CatalogDiffData

and CatalogDiffData =
    {
        Source    : Catalog
        Target    : Catalog
        Renamed   : Map<SsKey, RenameRecord>
        Added     : Set<SsKey>
        Removed   : Set<SsKey>
        Unchanged : Set<SsKey>
        /// 6.A.10 — per-kind attribute-level diffs, keyed by the kind's
        /// `SsKey`. Sparse: only kinds present in BOTH catalogs (i.e. in
        /// `Renamed ∪ Unchanged`) AND carrying at least one attribute
        /// difference appear. A kind whose attributes are identical
        /// contributes no entry. Lets a consumer compute minimum-viable
        /// touches (6.A.12 `diff → ALTER`) even when the kind name is stable.
        AttributeDiffs : Map<SsKey, AttributeDiff>
        /// C1 — per-kind reference-level diffs (sparse: only kinds in BOTH
        /// catalogs carrying at least one reference difference). Keyed by kind
        /// `SsKey`.
        ReferenceDiffs : Map<SsKey, ReferenceDiff>
        /// C1 — per-kind index-level diffs (sparse). Keyed by kind `SsKey`.
        IndexDiffs : Map<SsKey, IndexDiff>
        /// C1 — the catalog-level sequence diff. Empty (all four fields empty)
        /// when source and target carry identical sequences.
        SequenceDiff : SequenceDiff
    }

/// 6.A.7 — evidence that a name-derived (`Synthesized`) identity was
/// *probably renamed* but the SsKey-matching diff could not thread it. A
/// `Synthesized` SsKey is derived from the entity's name, so a rename
/// changes the key and the change lands in `Removed` + `Added` (not
/// `Renamed`) — A1 identity is silently lost. This pairs a removed source
/// kind with an added target kind that share a synthesis source and an
/// identical physical column set (a strong rename signal). The migrate /
/// transfer consumer renders it as the `identity.synthesizedRenameUnstable`
/// Warning; stable-key sources (`OssysOriginal` / `V1Mapped`) thread renames
/// natively and produce none.
type SynthesizedRenameWarning =
    {
        SynthesisSource : string
        SourceTable     : string
        TargetTable     : string
    }

[<RequireQualifiedAccess>]
module CatalogDiff =

    /// Pass version literal for the diff itself. Bumped when the
    /// partitioning algorithm changes meaning — e.g., when attribute-
    /// level diffing lands and the `allSsKeys` traversal expands. Per
    /// the rename-record's `PassVersion` field, this is the value
    /// stamped onto each `RenameRecord` produced by `between`.
    [<Literal>]
    let version : int = 1

    /// Per-Catalog kind-level SsKey set. First-slice scope: kinds
    /// only. Per the chapter 3.5 prescope §2 edge-case-2 design,
    /// expanding to a flat `allSsKeys` (kinds + attributes + references
    /// + indexes) is the natural follow-on when a real consumer
    /// surfaces attribute-level rename evidence.
    let private allKindKeys (c: Catalog) : Set<SsKey> =
        Catalog.allKinds c
        |> List.map (fun k -> k.SsKey)
        |> Set.ofList

    /// 6.A.10 — the facets of an attribute's emitted column shape that
    /// differ between source and target. Mirrors `PhysicalSchema.diff`'s
    /// column comparison so the diff and the canary agree on "what counts
    /// as a column change." Empty set ⇒ the attribute's shape is identical.
    let private changedFacets (s: Attribute) (t: Attribute) : Set<AttributeFacet> =
        [ if s.Type <> t.Type then AttributeFacet.DataType
          if s.Column.IsNullable <> t.Column.IsNullable then AttributeFacet.Nullability
          if s.IsPrimaryKey <> t.IsPrimaryKey then AttributeFacet.PrimaryKey
          if s.Length <> t.Length then AttributeFacet.Length
          if s.Precision <> t.Precision then AttributeFacet.Precision
          if s.Scale <> t.Scale then AttributeFacet.Scale
          if s.IsIdentity <> t.IsIdentity then AttributeFacet.Identity
          if (s.DefaultValue, s.DefaultName) <> (t.DefaultValue, t.DefaultName) then AttributeFacet.DefaultValue
          if s.Computed <> t.Computed then AttributeFacet.Computed ]
        |> Set.ofList

    let private emptyAttributeDiff : AttributeDiff =
        { Added = Set.empty; Removed = Set.empty; Renamed = Map.empty; Changed = [] }

    let private attributeDiffIsEmpty (d: AttributeDiff) : bool =
        Set.isEmpty d.Added
        && Set.isEmpty d.Removed
        && Map.isEmpty d.Renamed
        && List.isEmpty d.Changed

    /// The attribute-level diff between a kind's source and target
    /// realizations (the kind's `SsKey` is present in both catalogs).
    /// Attributes match by `SsKey`: present-in-target-only → `Added`,
    /// present-in-source-only → `Removed`; a shared `SsKey` whose `Name`
    /// differs → `Renamed` (independent of facet changes), whose emitted
    /// shape differs → `Changed`. Source-ordered `Changed` for determinism.
    let private attributeDiff (sourceKind: Kind) (targetKind: Kind) : AttributeDiff =
        let srcByKey = sourceKind.Attributes |> List.map (fun a -> a.SsKey, a) |> Map.ofList
        let tgtByKey = targetKind.Attributes |> List.map (fun a -> a.SsKey, a) |> Map.ofList
        let srcKeys = srcByKey |> Map.toSeq |> Seq.map fst |> Set.ofSeq
        let tgtKeys = tgtByKey |> Map.toSeq |> Seq.map fst |> Set.ofSeq
        let added = Set.difference tgtKeys srcKeys
        let removed = Set.difference srcKeys tgtKeys
        let renamed =
            Set.intersect srcKeys tgtKeys
            |> Set.fold
                (fun acc key ->
                    let s = Map.find key srcByKey
                    let t = Map.find key tgtByKey
                    if s.Name <> t.Name then
                        Map.add key { OldName = s.Name; NewName = t.Name; PassVersion = version } acc
                    else acc)
                Map.empty
        // Source order preserves determinism (T1) — the kind's attribute
        // list is the canonical order, not the Set's hash order.
        let changed =
            sourceKind.Attributes
            |> List.choose (fun s ->
                match Map.tryFind s.SsKey tgtByKey with
                | Some t ->
                    let facets = changedFacets s t
                    if Set.isEmpty facets then None
                    else Some { AttributeKey = s.SsKey; Facets = facets }
                | None -> None)
        { Added = added; Removed = removed; Renamed = renamed; Changed = changed }

    // -- C1: reference / index / sequence channel diffs ----------------------
    //
    // Each mirrors `attributeDiff` exactly: partition the SsKeys (Added /
    // Removed), thread same-SsKey `Name` changes into `Renamed`, and name the
    // changed facets for survivors whose shape differs. `Changed` is computed
    // in source-list order for T1 determinism (not Set hash order).

    let private emptyReferenceDiff : ReferenceDiff =
        { Added = Set.empty; Removed = Set.empty; Renamed = Map.empty; Changed = [] }

    let private referenceDiffIsEmpty (d: ReferenceDiff) : bool =
        Set.isEmpty d.Added && Set.isEmpty d.Removed && Map.isEmpty d.Renamed && List.isEmpty d.Changed

    let private changedReferenceFacets (s: Reference) (t: Reference) : Set<ReferenceFacet> =
        [ if s.TargetKind <> t.TargetKind then ReferenceFacet.Target
          if s.SourceAttribute <> t.SourceAttribute then ReferenceFacet.SourceAttribute
          if s.OnDelete <> t.OnDelete then ReferenceFacet.OnDelete
          if s.OnUpdate <> t.OnUpdate then ReferenceFacet.OnUpdate
          if s.IsUserFk <> t.IsUserFk then ReferenceFacet.UserFk
          if s.HasDbConstraint <> t.HasDbConstraint then ReferenceFacet.DbConstraint
          if s.IsConstraintTrusted <> t.IsConstraintTrusted then ReferenceFacet.Trust ]
        |> Set.ofList

    let private referenceDiff (sourceKind: Kind) (targetKind: Kind) : ReferenceDiff =
        let srcByKey = sourceKind.References |> List.map (fun r -> r.SsKey, r) |> Map.ofList
        let tgtByKey = targetKind.References |> List.map (fun r -> r.SsKey, r) |> Map.ofList
        let srcKeys = srcByKey |> Map.toSeq |> Seq.map fst |> Set.ofSeq
        let tgtKeys = tgtByKey |> Map.toSeq |> Seq.map fst |> Set.ofSeq
        let renamed =
            Set.intersect srcKeys tgtKeys
            |> Set.fold
                (fun acc key ->
                    let s = Map.find key srcByKey
                    let t = Map.find key tgtByKey
                    if s.Name <> t.Name then Map.add key { OldName = s.Name; NewName = t.Name; PassVersion = version } acc
                    else acc)
                Map.empty
        let changed =
            sourceKind.References
            |> List.choose (fun s ->
                match Map.tryFind s.SsKey tgtByKey with
                | Some t ->
                    let facets = changedReferenceFacets s t
                    if Set.isEmpty facets then None else Some { ReferenceKey = s.SsKey; Facets = facets }
                | None -> None)
        { Added = Set.difference tgtKeys srcKeys
          Removed = Set.difference srcKeys tgtKeys
          Renamed = renamed
          Changed = changed }

    let private emptyIndexDiff : IndexDiff =
        { Added = Set.empty; Removed = Set.empty; Renamed = Map.empty; Changed = [] }

    let private indexDiffIsEmpty (d: IndexDiff) : bool =
        Set.isEmpty d.Added && Set.isEmpty d.Removed && Map.isEmpty d.Renamed && List.isEmpty d.Changed

    let private changedIndexFacets (s: Index) (t: Index) : Set<IndexFacet> =
        [ if s.Columns <> t.Columns then IndexFacet.Columns
          if s.Uniqueness <> t.Uniqueness then IndexFacet.Uniqueness
          if s.IncludedColumns <> t.IncludedColumns then IndexFacet.IncludedColumns
          if s.Filter <> t.Filter then IndexFacet.Filter
          if s.DataSpace <> t.DataSpace then IndexFacet.DataSpace
          if s.IsPlatformAuto <> t.IsPlatformAuto || s.FillFactor <> t.FillFactor
             || s.IsPadded <> t.IsPadded || s.AllowRowLocks <> t.AllowRowLocks
             || s.AllowPageLocks <> t.AllowPageLocks || s.NoRecomputeStatistics <> t.NoRecomputeStatistics
             || s.IgnoreDuplicateKey <> t.IgnoreDuplicateKey || s.IsDisabled <> t.IsDisabled
             || s.DataCompression <> t.DataCompression then IndexFacet.Options ]
        |> Set.ofList

    let private indexDiff (sourceKind: Kind) (targetKind: Kind) : IndexDiff =
        let srcByKey = sourceKind.Indexes |> List.map (fun i -> i.SsKey, i) |> Map.ofList
        let tgtByKey = targetKind.Indexes |> List.map (fun i -> i.SsKey, i) |> Map.ofList
        let srcKeys = srcByKey |> Map.toSeq |> Seq.map fst |> Set.ofSeq
        let tgtKeys = tgtByKey |> Map.toSeq |> Seq.map fst |> Set.ofSeq
        let renamed =
            Set.intersect srcKeys tgtKeys
            |> Set.fold
                (fun acc key ->
                    let s = Map.find key srcByKey
                    let t = Map.find key tgtByKey
                    if s.Name <> t.Name then Map.add key { OldName = s.Name; NewName = t.Name; PassVersion = version } acc
                    else acc)
                Map.empty
        let changed =
            sourceKind.Indexes
            |> List.choose (fun s ->
                match Map.tryFind s.SsKey tgtByKey with
                | Some t ->
                    let facets = changedIndexFacets s t
                    if Set.isEmpty facets then None else Some { IndexKey = s.SsKey; Facets = facets }
                | None -> None)
        { Added = Set.difference tgtKeys srcKeys
          Removed = Set.difference srcKeys tgtKeys
          Renamed = renamed
          Changed = changed }

    let private emptySequenceDiff : SequenceDiff =
        { Added = Set.empty; Removed = Set.empty; Renamed = Map.empty; Changed = [] }

    let private sequenceDiffIsEmpty (d: SequenceDiff) : bool =
        Set.isEmpty d.Added && Set.isEmpty d.Removed && Map.isEmpty d.Renamed && List.isEmpty d.Changed

    let private changedSequenceFacets (s: Sequence) (t: Sequence) : Set<SequenceFacet> =
        [ if s.Schema <> t.Schema then SequenceFacet.Schema
          if s.DataType <> t.DataType then SequenceFacet.DataType
          if s.StartValue <> t.StartValue then SequenceFacet.StartValue
          if s.Increment <> t.Increment then SequenceFacet.Increment
          if s.Minimum <> t.Minimum then SequenceFacet.Minimum
          if s.Maximum <> t.Maximum then SequenceFacet.Maximum
          if s.IsCycleEnabled <> t.IsCycleEnabled then SequenceFacet.Cycle
          if (s.CacheMode, s.CacheSize) <> (t.CacheMode, t.CacheSize) then SequenceFacet.Cache ]
        |> Set.ofList

    let private sequenceDiffBetween (source: Catalog) (target: Catalog) : SequenceDiff =
        let srcByKey = source.Sequences |> List.map (fun s -> s.SsKey, s) |> Map.ofList
        let tgtByKey = target.Sequences |> List.map (fun s -> s.SsKey, s) |> Map.ofList
        let srcKeys = srcByKey |> Map.toSeq |> Seq.map fst |> Set.ofSeq
        let tgtKeys = tgtByKey |> Map.toSeq |> Seq.map fst |> Set.ofSeq
        let renamed =
            Set.intersect srcKeys tgtKeys
            |> Set.fold
                (fun acc key ->
                    let s = Map.find key srcByKey
                    let t = Map.find key tgtByKey
                    if s.Name <> t.Name then Map.add key { OldName = s.Name; NewName = t.Name; PassVersion = version } acc
                    else acc)
                Map.empty
        let changed =
            source.Sequences
            |> List.choose (fun s ->
                match Map.tryFind s.SsKey tgtByKey with
                | Some t ->
                    let facets = changedSequenceFacets s t
                    if Set.isEmpty facets then None else Some { SequenceKey = s.SsKey; Facets = facets }
                | None -> None)
        { Added = Set.difference tgtKeys srcKeys
          Removed = Set.difference srcKeys tgtKeys
          Renamed = renamed
          Changed = changed }

    /// Smart constructor — total partitioning of `source ∪ target`
    /// SsKeys. Every key in either Catalog is in exactly one of the
    /// four output sets. Exhaustiveness invariant verified by
    /// property test in `CatalogDiffTests.fs`.
    ///
    /// Big-O: `Catalog.allKinds` is O(N) per Catalog; `Set.ofList`
    /// is O(N log N); `Set.difference` / `Set.intersect` are
    /// O(N log N); the `Set.fold` over the intersection is
    /// O(N log N) with O(log N) `Catalog.tryFindKind` lookups.
    /// Total: O(N log N) where N = |source ∪ target|.
    ///
    /// Returns `Result<CatalogDiff, EmitError>` for shape uniformity
    /// with the rest of the emitter pipeline; the smart constructor
    /// itself is total over Catalog inputs (Catalog has no failure
    /// modes the diff would surface) — `Ok` is the only inhabited
    /// branch today. The Result wrapper preserves room for future
    /// variants (e.g., a `DiffMismatchedSchema` failure if the diff
    /// expands to detect schema-level incompatibilities).
    let between
        (source: Catalog)
        (target: Catalog)
        : Result<CatalogDiff, EmitError>
        =
        let srcKeys = allKindKeys source
        let tgtKeys = allKindKeys target
        let added = Set.difference tgtKeys srcKeys
        let removed = Set.difference srcKeys tgtKeys
        let intersect = Set.intersect srcKeys tgtKeys
        let nameOf (c: Catalog) (k: SsKey) =
            Catalog.tryFindKind k c |> Option.map (fun kd -> kd.Name)
        let renamed, unchanged =
            intersect
            |> Set.fold
                (fun (rn, un) key ->
                    match nameOf source key, nameOf target key with
                    | Some sn, Some tn when sn <> tn ->
                        let record =
                            {
                                OldName = sn
                                NewName = tn
                                PassVersion = version
                            }
                        Map.add key record rn, un
                    | _ ->
                        rn, Set.add key un)
                (Map.empty, Set.empty)
        // 6.A.10 — descend into attributes for every kind present in BOTH
        // catalogs (the intersection = Renamed ∪ Unchanged). Store only
        // non-empty diffs so an unchanged kind contributes nothing (and the
        // diff stays empty for an idempotent redeploy → CDC-silence, 6.A.13).
        let attributeDiffs =
            intersect
            |> Set.fold
                (fun acc key ->
                    match Catalog.tryFindKind key source, Catalog.tryFindKind key target with
                    | Some sk, Some tk ->
                        let d = attributeDiff sk tk
                        if attributeDiffIsEmpty d then acc else Map.add key d acc
                    | _ -> acc)
                Map.empty
        // C1 — descend into references + indexes for every kind in BOTH catalogs;
        // store only non-empty diffs (an unchanged channel contributes nothing,
        // so an idempotent redeploy stays empty → CDC-silence, 6.A.13).
        let referenceDiffs =
            intersect
            |> Set.fold
                (fun acc key ->
                    match Catalog.tryFindKind key source, Catalog.tryFindKind key target with
                    | Some sk, Some tk ->
                        let d = referenceDiff sk tk
                        if referenceDiffIsEmpty d then acc else Map.add key d acc
                    | _ -> acc)
                Map.empty
        let indexDiffs =
            intersect
            |> Set.fold
                (fun acc key ->
                    match Catalog.tryFindKind key source, Catalog.tryFindKind key target with
                    | Some sk, Some tk ->
                        let d = indexDiff sk tk
                        if indexDiffIsEmpty d then acc else Map.add key d acc
                    | _ -> acc)
                Map.empty
        // C1 — sequences are catalog-level, not kind-scoped.
        let sequenceDiff = sequenceDiffBetween source target
        Ok
            (CatalogDiff
                {
                    Source = source
                    Target = target
                    Renamed = renamed
                    Added = added
                    Removed = removed
                    Unchanged = unchanged
                    AttributeDiffs = attributeDiffs
                    ReferenceDiffs = referenceDiffs
                    IndexDiffs = indexDiffs
                    SequenceDiff = sequenceDiff
                })

    let source (CatalogDiff d) : Catalog = d.Source
    let target (CatalogDiff d) : Catalog = d.Target
    let renamed (CatalogDiff d) : Map<SsKey, RenameRecord> = d.Renamed
    let added (CatalogDiff d) : Set<SsKey> = d.Added
    let removed (CatalogDiff d) : Set<SsKey> = d.Removed
    let unchanged (CatalogDiff d) : Set<SsKey> = d.Unchanged

    /// 6.A.10 — the per-kind attribute-level diffs (sparse: only kinds with
    /// at least one attribute difference). Keyed by kind `SsKey`.
    let attributeDiffs (CatalogDiff d) : Map<SsKey, AttributeDiff> = d.AttributeDiffs

    /// The attribute-level diff for one kind, or `None` if the kind's
    /// attributes are identical (or the kind is not present in both).
    let attributeDiffOf (key: SsKey) (CatalogDiff d) : AttributeDiff option =
        Map.tryFind key d.AttributeDiffs

    /// C1 — the per-kind reference-level diffs (sparse). Keyed by kind `SsKey`.
    let referenceDiffs (CatalogDiff d) : Map<SsKey, ReferenceDiff> = d.ReferenceDiffs

    /// C1 — the reference-level diff for one kind, or `None` if identical.
    let referenceDiffOf (key: SsKey) (CatalogDiff d) : ReferenceDiff option =
        Map.tryFind key d.ReferenceDiffs

    /// C1 — the per-kind index-level diffs (sparse). Keyed by kind `SsKey`.
    let indexDiffs (CatalogDiff d) : Map<SsKey, IndexDiff> = d.IndexDiffs

    /// C1 — the index-level diff for one kind, or `None` if identical.
    let indexDiffOf (key: SsKey) (CatalogDiff d) : IndexDiff option =
        Map.tryFind key d.IndexDiffs

    /// C1 — the catalog-level sequence diff (empty when sequences are identical).
    let sequenceDiff (CatalogDiff d) : SequenceDiff = d.SequenceDiff

    /// 6.A.7 — the Synthesized-key renames the SsKey-matching diff could not
    /// thread. A `Synthesized` SsKey is name-derived, so a rename changes the
    /// key and the change appears as a `Removed` + `Added` pair, never a
    /// `Renamed` record — A1 identity is silently lost. This pairs each
    /// removed source kind whose key is `Synthesized`-rooted with an added
    /// target kind that shares the same synthesis source AND the same
    /// physical column-name set (a strong rename signal: the same shape under
    /// a different name), emitting one warning per such pair so the rename is
    /// *surfaced*, not silently re-keyed. Empty for a stable-key
    /// (`OssysOriginal` / `V1Mapped`) source — those thread renames natively
    /// via the `Renamed` map. Pure over the diff (T1 deterministic: removed
    /// and added keys are iterated in `SsKey` sort order).
    let synthesizedRenameWarnings (CatalogDiff d) : SynthesizedRenameWarning list =
        let columnSet (k: Kind) =
            k.Attributes |> List.map (fun a -> ColumnRealization.columnNameText a.Column) |> Set.ofList
        let synthKinds (keys: Set<SsKey>) (catalog: Catalog) =
            keys
            |> Set.toList
            |> List.choose (fun key ->
                if SsKey.isSynthesizedRoot key then
                    Catalog.tryFindKind key catalog |> Option.map (fun k -> key, k)
                else None)
        let removedSynth = synthKinds d.Removed d.Source
        let addedSynth = synthKinds d.Added d.Target
        removedSynth
        |> List.collect (fun (rKey, rKind) ->
            let rCols = columnSet rKind
            let rSrc = SsKey.synthesisSource rKey
            if Set.isEmpty rCols then []
            else
                addedSynth
                |> List.choose (fun (aKey, aKind) ->
                    if SsKey.synthesisSource aKey = rSrc && columnSet aKind = rCols then
                        Some
                            { SynthesisSource = (rSrc |> Option.defaultValue "")
                              SourceTable = sprintf "%s.%s" (SchemaName.value rKind.Physical.Schema) (TableName.value rKind.Physical.Table)
                              TargetTable = sprintf "%s.%s" (SchemaName.value aKind.Physical.Schema) (TableName.value aKind.Physical.Table) }
                    else None))

    /// All SsKeys in scope of the diff — `source ∪ target`. Equal
    /// (by exhaustiveness invariant) to the disjoint union of the
    /// four partitions: `domain(Renamed) ∪ Added ∪ Removed ∪ Unchanged`.
    /// Verified by property test.
    let scope (d: CatalogDiff) : Set<SsKey> =
        let (CatalogDiff data) = d
        let renamedKeys = data.Renamed |> Map.toSeq |> Seq.map fst |> Set.ofSeq
        renamedKeys
        |> Set.union data.Added
        |> Set.union data.Removed
        |> Set.union data.Unchanged

    /// `CatalogDiff` is empty iff source and target carry the same
    /// SsKey set with the same Name on every shared key. Used by the
    /// chapter 3.4 canary's `idempotentRedeploy` predicate: if a
    /// catalog's diff against itself is empty, a redeploy plan is
    /// trivially empty.
    /// 6.A.10 — an empty diff now also requires zero attribute-level
    /// changes. Before, a kind-name-stable column change (`TEXT →
    /// NVARCHAR(256)`) reported `isEmpty = true` → no ALTER → silent
    /// redeploy. With attribute descent, the diff is empty iff the catalogs
    /// are structurally identical at both the kind and attribute level —
    /// which is the predicate 6.A.13's schema CDC-silence rests on.
    /// C1 — an empty diff now also requires zero reference, index, and sequence
    /// changes. Before C1 the diff reported `isEmpty = true` while an added FK,
    /// a changed index, or a reshaped sequence rode through silently — and the
    /// 6.A.13 CDC-silence gate rests on this predicate, so it must see the full
    /// captured surface.
    let isEmpty (d: CatalogDiff) : bool =
        let (CatalogDiff data) = d
        Map.isEmpty data.Renamed
        && Set.isEmpty data.Added
        && Set.isEmpty data.Removed
        && Map.isEmpty data.AttributeDiffs
        && Map.isEmpty data.ReferenceDiffs
        && Map.isEmpty data.IndexDiffs
        && sequenceDiffIsEmpty data.SequenceDiff

    // -- 6.A.11: applyDiff — the `between` peer (H-007) -----------------------
    //
    // `between` is the *observation* (read a delta off two catalogs);
    // `applyDiff` is the *action* (transform a catalog by a delta). Together
    // they make the Time axis an evolution algebra, not a snapshot store: the
    // round-trip law `applyDiff (between A B) A = B` (modulo the captured
    // surface) is the Time faithfulness witness.
    //
    // **Faithful, not trivial.** `applyDiff base d` derives the result's
    // keyset from `base ⊖ delta` — `(keys base \ Removed) ∪ Added` — and
    // TRANSFORMS `base`'s kinds in place (rename + per-attribute facet patch).
    // It reads the recorded `target d` ONLY to source genuinely-new content
    // (an `Added` kind's full definition; the new value of a `Changed` facet).
    // So the result depends on the passed-in `base`, not just the target — a
    // diff applied to a *different* base preserves that base's extra kinds
    // (the no-cheat property test). It is NOT `fun base d -> target d`.
    //
    // **Captured surface (the law's modulus).** The diff captures kind
    // presence/name + attribute presence/name + the nine column-shape facets
    // (`AttributeFacet`). It does NOT capture references, indexes, modality,
    // module structure, or sequences — those ride through from `base`
    // unchanged. The round-trip law therefore holds for A→B evolutions within
    // the captured surface, witnessed order-insensitively by
    // `between B (applyDiff (between A B) A) |> isEmpty`. A future
    // self-contained diff (inline payloads, no stored source/target) would
    // widen the surface; deferred under IR-grows-under-evidence.

    /// Patch one column-shape facet of `dest` from `src` (the recorded
    /// target's attribute). Only the named facet moves; every other field of
    /// `dest` rides through — applyDiff's power exactly matches the facet set
    /// `between` detects, so no un-captured field is silently reconstructed.
    let private applyFacet (src: Attribute) (facet: AttributeFacet) (dest: Attribute) : Attribute =
        match facet with
        | AttributeFacet.DataType     -> { dest with Type = src.Type }
        | AttributeFacet.Nullability  -> dest |> Lens.over CatalogLenses.columnOf (fun col -> { col with IsNullable = src.Column.IsNullable })
        | AttributeFacet.PrimaryKey   -> { dest with IsPrimaryKey = src.IsPrimaryKey }
        | AttributeFacet.Length       -> { dest with Length = src.Length }
        | AttributeFacet.Precision    -> { dest with Precision = src.Precision }
        | AttributeFacet.Scale        -> { dest with Scale = src.Scale }
        | AttributeFacet.Identity     -> { dest with IsIdentity = src.IsIdentity }
        | AttributeFacet.DefaultValue -> { dest with DefaultValue = src.DefaultValue; DefaultName = src.DefaultName }
        | AttributeFacet.Computed     -> { dest with Computed = src.Computed }

    /// Apply a per-kind `AttributeDiff` to a kind's base attribute list,
    /// sourcing new attributes / new facet values from the recorded target
    /// kind. Removed attrs drop; surviving attrs are renamed (`Renamed`) and
    /// facet-patched (`Changed`); `Added` attrs append in target order.
    let private applyAttributeDiff
        (ad: AttributeDiff)
        (targetKind: Kind option)
        (baseAttrs: Attribute list)
        : Attribute list =
        let tgtAttr (key: SsKey) : Attribute option =
            targetKind |> Option.bind (fun k -> k.Attributes |> List.tryFind (fun a -> a.SsKey = key))
        let survivors =
            baseAttrs
            |> List.filter (fun a -> not (Set.contains a.SsKey ad.Removed))
            |> List.map (fun a ->
                let renamed1 =
                    match Map.tryFind a.SsKey ad.Renamed with
                    | Some r -> { a with Name = r.NewName }
                    | None -> a
                match ad.Changed |> List.tryFind (fun c -> c.AttributeKey = a.SsKey), tgtAttr a.SsKey with
                | Some change, Some src -> Set.fold (fun acc f -> applyFacet src f acc) renamed1 change.Facets
                | _ -> renamed1)
        let added =
            match targetKind with
            | Some tk -> tk.Attributes |> List.filter (fun a -> Set.contains a.SsKey ad.Added)
            | None    -> []
        survivors @ added

    // -- C1: apply the reference / index / sequence channels -----------------
    //
    // Each mirrors `applyAttributeDiff`. Changed survivors are PATCHED facet by
    // facet from the recorded target record; Added entries are COPIED whole from
    // the target. No smart-constructor reconstruction → the `{ create … with … }`
    // default-substitution bomb cannot bite (the discipline §6 of the debrief).

    let private applyReferenceFacet (src: Reference) (facet: ReferenceFacet) (dest: Reference) : Reference =
        match facet with
        | ReferenceFacet.Target          -> { dest with TargetKind = src.TargetKind }
        | ReferenceFacet.SourceAttribute -> { dest with SourceAttribute = src.SourceAttribute }
        | ReferenceFacet.OnDelete        -> { dest with OnDelete = src.OnDelete }
        | ReferenceFacet.OnUpdate        -> { dest with OnUpdate = src.OnUpdate }
        | ReferenceFacet.UserFk          -> { dest with IsUserFk = src.IsUserFk }
        | ReferenceFacet.DbConstraint    -> { dest with HasDbConstraint = src.HasDbConstraint }
        | ReferenceFacet.Trust           -> { dest with IsConstraintTrusted = src.IsConstraintTrusted }

    let private applyReferenceDiff (rd: ReferenceDiff) (targetKind: Kind option) (baseRefs: Reference list) : Reference list =
        let tgtRef (key: SsKey) : Reference option =
            targetKind |> Option.bind (fun k -> k.References |> List.tryFind (fun r -> r.SsKey = key))
        let survivors =
            baseRefs
            |> List.filter (fun r -> not (Set.contains r.SsKey rd.Removed))
            |> List.map (fun r ->
                let renamed1 =
                    match Map.tryFind r.SsKey rd.Renamed with
                    | Some rec1 -> { r with Name = rec1.NewName }
                    | None -> r
                match rd.Changed |> List.tryFind (fun c -> c.ReferenceKey = r.SsKey), tgtRef r.SsKey with
                | Some change, Some src -> Set.fold (fun acc f -> applyReferenceFacet src f acc) renamed1 change.Facets
                | _ -> renamed1)
        let added =
            match targetKind with
            | Some tk -> tk.References |> List.filter (fun r -> Set.contains r.SsKey rd.Added)
            | None    -> []
        survivors @ added

    let private applyIndexFacet (src: Index) (facet: IndexFacet) (dest: Index) : Index =
        match facet with
        | IndexFacet.Columns         -> { dest with Columns = src.Columns }
        | IndexFacet.Uniqueness      -> { dest with Uniqueness = src.Uniqueness }
        | IndexFacet.IncludedColumns -> { dest with IncludedColumns = src.IncludedColumns }
        | IndexFacet.Filter          -> { dest with Filter = src.Filter }
        | IndexFacet.DataSpace       -> { dest with DataSpace = src.DataSpace }
        | IndexFacet.Options ->
            { dest with
                IsPlatformAuto = src.IsPlatformAuto
                FillFactor = src.FillFactor
                IsPadded = src.IsPadded
                AllowRowLocks = src.AllowRowLocks
                AllowPageLocks = src.AllowPageLocks
                NoRecomputeStatistics = src.NoRecomputeStatistics
                IgnoreDuplicateKey = src.IgnoreDuplicateKey
                IsDisabled = src.IsDisabled
                DataCompression = src.DataCompression }

    let private applyIndexDiff (idd: IndexDiff) (targetKind: Kind option) (baseIndexes: Index list) : Index list =
        let tgtIndex (key: SsKey) : Index option =
            targetKind |> Option.bind (fun k -> k.Indexes |> List.tryFind (fun i -> i.SsKey = key))
        let survivors =
            baseIndexes
            |> List.filter (fun i -> not (Set.contains i.SsKey idd.Removed))
            |> List.map (fun i ->
                let renamed1 =
                    match Map.tryFind i.SsKey idd.Renamed with
                    | Some rec1 -> { i with Name = rec1.NewName }
                    | None -> i
                match idd.Changed |> List.tryFind (fun c -> c.IndexKey = i.SsKey), tgtIndex i.SsKey with
                | Some change, Some src -> Set.fold (fun acc f -> applyIndexFacet src f acc) renamed1 change.Facets
                | _ -> renamed1)
        let added =
            match targetKind with
            | Some tk -> tk.Indexes |> List.filter (fun i -> Set.contains i.SsKey idd.Added)
            | None    -> []
        survivors @ added

    let private applySequenceFacet (src: Sequence) (facet: SequenceFacet) (dest: Sequence) : Sequence =
        match facet with
        | SequenceFacet.Schema     -> { dest with Schema = src.Schema }
        | SequenceFacet.DataType   -> { dest with DataType = src.DataType }
        | SequenceFacet.StartValue -> { dest with StartValue = src.StartValue }
        | SequenceFacet.Increment  -> { dest with Increment = src.Increment }
        | SequenceFacet.Minimum    -> { dest with Minimum = src.Minimum }
        | SequenceFacet.Maximum    -> { dest with Maximum = src.Maximum }
        | SequenceFacet.Cycle      -> { dest with IsCycleEnabled = src.IsCycleEnabled }
        | SequenceFacet.Cache      -> { dest with CacheMode = src.CacheMode; CacheSize = src.CacheSize }

    let private applySequenceDiff (sd: SequenceDiff) (target: Catalog) (baseSeqs: Sequence list) : Sequence list =
        let tgtSeq (key: SsKey) : Sequence option =
            target.Sequences |> List.tryFind (fun s -> s.SsKey = key)
        let survivors =
            baseSeqs
            |> List.filter (fun s -> not (Set.contains s.SsKey sd.Removed))
            |> List.map (fun s ->
                let renamed1 =
                    match Map.tryFind s.SsKey sd.Renamed with
                    | Some rec1 -> { s with Name = rec1.NewName }
                    | None -> s
                match sd.Changed |> List.tryFind (fun c -> c.SequenceKey = s.SsKey), tgtSeq s.SsKey with
                | Some change, Some src -> Set.fold (fun acc f -> applySequenceFacet src f acc) renamed1 change.Facets
                | _ -> renamed1)
        let added = target.Sequences |> List.filter (fun s -> Set.contains s.SsKey sd.Added)
        survivors @ added

    /// 6.A.11 — apply a `CatalogDiff` to a base `Catalog`, reconstructing the
    /// target modulo the captured surface. Total: trusts the delta (no
    /// re-validation), so the round-trip law is `between B (applyDiff
    /// (between A B) A) |> isEmpty`. References / indexes / modality / module
    /// structure / sequences ride through from `base` (not captured by the
    /// diff). H-007: the `between` peer that makes Time an evolution algebra.
    let applyDiff (baseCatalog: Catalog) (d: CatalogDiff) : Catalog =
        let (CatalogDiff data) = d
        let tgt = data.Target
        let transformKind (k: Kind) : Kind =
            let renamed1 =
                match Map.tryFind k.SsKey data.Renamed with
                | Some r -> { k with Name = r.NewName }
                | None -> k
            let tgtKind = Catalog.tryFindKind k.SsKey tgt
            let withAttrs =
                match Map.tryFind k.SsKey data.AttributeDiffs with
                | Some ad -> { renamed1 with Attributes = applyAttributeDiff ad tgtKind renamed1.Attributes }
                | None -> renamed1
            let withRefs =
                match Map.tryFind k.SsKey data.ReferenceDiffs with
                | Some rd -> { withAttrs with References = applyReferenceDiff rd tgtKind withAttrs.References }
                | None -> withAttrs
            match Map.tryFind k.SsKey data.IndexDiffs with
            | Some idd -> { withRefs with Indexes = applyIndexDiff idd tgtKind withRefs.Indexes }
            | None -> withRefs
        // Transform base's modules: drop Removed kinds, transform survivors.
        let survivingKeys =
            Catalog.allKinds baseCatalog
            |> List.map (fun k -> k.SsKey)
            |> List.filter (fun key -> not (Set.contains key data.Removed))
            |> Set.ofList
        let baseModules =
            baseCatalog.Modules
            |> List.map (fun m ->
                { m with
                    Kinds =
                        m.Kinds
                        |> List.filter (fun k -> not (Set.contains k.SsKey data.Removed))
                        |> List.map transformKind })
        // Place Added kinds into their target-owning module (dedup against
        // anything already surviving in base, for robustness on a base ≠ source).
        let addedKinds =
            data.Added
            |> Set.toList
            |> List.filter (fun key -> not (Set.contains key survivingKeys))
            |> List.choose (fun key ->
                match Catalog.tryFindKind key tgt, Catalog.tryFindOwningModule key tgt with
                | Some k, Some owner -> Some (owner.SsKey, owner, k)
                | _ -> None)
        let modulesWithAdds =
            // Append each added kind to the result module that owns it in the
            // target; create the module from the target's record when absent.
            addedKinds
            |> List.fold
                (fun (mods: Module list) (ownerKey, ownerModule, kind) ->
                    if mods |> List.exists (fun m -> m.SsKey = ownerKey) then
                        mods |> List.map (fun m ->
                            if m.SsKey = ownerKey then
                                m |> Lens.over CatalogLenses.kindsOf (fun ks -> ks @ [ kind ])
                            else m)
                    else
                        mods @ [ { ownerModule with Kinds = [ kind ] } ])
                baseModules
        // C1 — apply the catalog-level sequence channel (sequences are not
        // kind-scoped, so they reconstruct at the Catalog, not inside a Kind).
        { baseCatalog with
            Modules = modulesWithAdds
            Sequences = applySequenceDiff data.SequenceDiff tgt baseCatalog.Sequences }

    // -- 6.H.3 (prework): the norm ‖·‖, the channel projection π, and compose
    //    — the derivative algebra's measurement + composition layer, made
    //    CONCRETE on the `CatalogDiff` value (per `WAVE_6_ALGEBRA.md` §12.4 —
    //    the schema-side carrier; NOT a generic `Torsor`/`Delta`). Foundational
    //    for the change-manifest (6.H.4), the `migrate` preview (6.D.1), and the
    //    temporal substrate (6.H).

    /// The per-channel move counts of a diff — the concrete schema-side channel
    /// projection π. Each field counts one move-channel; their sum is the norm
    /// ‖δ‖ (additive over the orthogonal channels, T14/T15).
    type ChannelCounts =
        {
            RenamedKinds      : int
            AddedKinds        : int
            RemovedKinds      : int
            AddedAttributes   : int
            RemovedAttributes : int
            RenamedAttributes : int
            ChangedAttributes : int
            // C1 — the reference / index / sequence channels.
            AddedReferences   : int
            RemovedReferences : int
            RenamedReferences : int
            ChangedReferences : int
            AddedIndexes      : int
            RemovedIndexes    : int
            RenamedIndexes    : int
            ChangedIndexes    : int
            AddedSequences    : int
            RemovedSequences  : int
            RenamedSequences  : int
            ChangedSequences  : int
        }

    let channelCounts (d: CatalogDiff) : ChannelCounts =
        let (CatalogDiff data) = d
        let sumAttr (f: AttributeDiff -> int) =
            data.AttributeDiffs |> Map.toSeq |> Seq.sumBy (fun (_, ad) -> f ad)
        let sumRef (f: ReferenceDiff -> int) =
            data.ReferenceDiffs |> Map.toSeq |> Seq.sumBy (fun (_, rd) -> f rd)
        let sumIdx (f: IndexDiff -> int) =
            data.IndexDiffs |> Map.toSeq |> Seq.sumBy (fun (_, idd) -> f idd)
        {
            RenamedKinds      = Map.count data.Renamed
            AddedKinds        = Set.count data.Added
            RemovedKinds      = Set.count data.Removed
            AddedAttributes   = sumAttr (fun ad -> Set.count ad.Added)
            RemovedAttributes = sumAttr (fun ad -> Set.count ad.Removed)
            RenamedAttributes = sumAttr (fun ad -> Map.count ad.Renamed)
            ChangedAttributes = sumAttr (fun ad -> List.length ad.Changed)
            AddedReferences   = sumRef (fun rd -> Set.count rd.Added)
            RemovedReferences = sumRef (fun rd -> Set.count rd.Removed)
            RenamedReferences = sumRef (fun rd -> Map.count rd.Renamed)
            ChangedReferences = sumRef (fun rd -> List.length rd.Changed)
            AddedIndexes      = sumIdx (fun idd -> Set.count idd.Added)
            RemovedIndexes    = sumIdx (fun idd -> Set.count idd.Removed)
            RenamedIndexes    = sumIdx (fun idd -> Map.count idd.Renamed)
            ChangedIndexes    = sumIdx (fun idd -> List.length idd.Changed)
            AddedSequences    = Set.count data.SequenceDiff.Added
            RemovedSequences  = Set.count data.SequenceDiff.Removed
            RenamedSequences  = Map.count data.SequenceDiff.Renamed
            ChangedSequences  = List.length data.SequenceDiff.Changed
        }

    /// The norm ‖δ‖ — total move count, the sum of the channel counts (T15,
    /// schema side). `norm (between A A) = 0`; `norm d = 0 ⟺ isEmpty d`
    /// (because `between` stores only non-empty attribute diffs, so any stored
    /// `AttributeDiff` contributes ≥ 1 to the norm).
    let norm (d: CatalogDiff) : int =
        let c = channelCounts d
        c.RenamedKinds + c.AddedKinds + c.RemovedKinds
        + c.AddedAttributes + c.RemovedAttributes + c.RenamedAttributes + c.ChangedAttributes
        + c.AddedReferences + c.RemovedReferences + c.RenamedReferences + c.ChangedReferences
        + c.AddedIndexes + c.RemovedIndexes + c.RenamedIndexes + c.ChangedIndexes
        + c.AddedSequences + c.RemovedSequences + c.RenamedSequences + c.ChangedSequences

    /// 6.H.3 — compose two consecutive diffs into their net displacement (the
    /// torsor `+`; T13 / A-Lifecycle-4). `compose d1 d2` is the delta
    /// `source d1 → target d2`, defined (`Some`) **iff** d1's target meets d2's
    /// source on the captured surface — the groupoid composition is *partial*
    /// (deltas are typed by their endpoints); a non-adjacent pair is `None`
    /// (fail-loud, never a silently-wrong result). Implemented as
    /// `between (source d1) (target d2)`, which is provably the net delta:
    /// `applyDiff (compose d1 d2) (source d1) = target d2 = applyDiff d2
    /// (applyDiff d1 (source d1))` (the functor law). **Associativity**
    /// (A-Lifecycle-4) follows — both groupings recompute
    /// `between (source d1) (target dₙ)`.
    let compose (d1: CatalogDiff) (d2: CatalogDiff) : CatalogDiff option =
        let composable =
            match between (target d1) (source d2) with
            | Ok bridge -> isEmpty bridge
            | Error _   -> false
        if not composable then None
        else
            match between (source d1) (target d2) with
            | Ok net  -> Some net
            | Error _ -> None
