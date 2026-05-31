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
    let isEmpty (d: CatalogDiff) : bool =
        let (CatalogDiff data) = d
        Map.isEmpty data.Renamed
        && Set.isEmpty data.Added
        && Set.isEmpty data.Removed
        && Map.isEmpty data.AttributeDiffs
