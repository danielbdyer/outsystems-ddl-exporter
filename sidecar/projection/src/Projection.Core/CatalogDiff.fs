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
        Ok
            (CatalogDiff
                {
                    Source = source
                    Target = target
                    Renamed = renamed
                    Added = added
                    Removed = removed
                    Unchanged = unchanged
                })

    let source (CatalogDiff d) : Catalog = d.Source
    let target (CatalogDiff d) : Catalog = d.Target
    let renamed (CatalogDiff d) : Map<SsKey, RenameRecord> = d.Renamed
    let added (CatalogDiff d) : Set<SsKey> = d.Added
    let removed (CatalogDiff d) : Set<SsKey> = d.Removed
    let unchanged (CatalogDiff d) : Set<SsKey> = d.Unchanged

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
    let isEmpty (d: CatalogDiff) : bool =
        let (CatalogDiff data) = d
        Map.isEmpty data.Renamed
        && Set.isEmpty data.Added
        && Set.isEmpty data.Removed
