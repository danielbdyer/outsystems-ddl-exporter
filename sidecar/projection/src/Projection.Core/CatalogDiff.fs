namespace Projection.Core

// LINT-ALLOW-FILE: rename-evidence terminal-text projection. The `SourceTable` / `TargetTable`
//   string fields are `schema.table` qualified-name projections rendered via
//   `sprintf` at the diff boundary; the structural diff output is fully typed.
//   Per `DECISIONS 2026-05-09 — Built-in obligation`.

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

/// The structurally-identical carrier behind every per-channel diff
/// (attributes / references / indexes / sequences). `Added` / `Removed`
/// are entity `SsKey`s present in only the target / source; `Renamed`
/// carries a same-`SsKey` `Name` change; `Reshaped` names survivors whose
/// emitted shape differs, the channel's own change-evidence type filling
/// `'change`. The four channels were four byte-identical records before;
/// they are now one generic with four named instantiations (the aliases
/// below), so field access and the public type names are unchanged for
/// every consumer. (`Reshaped` was `Changed` before §3.3/§6 — the diff and
/// migration layers now speak one word for the reshape axis.)
type ChannelDiff<'change> =
    {
        Added    : Set<SsKey>
        Removed  : Set<SsKey>
        Renamed  : Map<SsKey, RenameRecord>
        Reshaped : 'change list
    }

/// Per-kind attribute-level diff for a kind present in BOTH catalogs.
/// `Added` / `Removed` are attribute `SsKey`s present in only the target /
/// source; `Renamed` carries a same-`SsKey` `Name` change (edge-case-2 of
/// the chapter-3.5 prescope — a renamed attribute on an unrenamed kind);
/// `Reshaped` names attributes whose emitted column shape differs facet by
/// facet. An empty `AttributeDiff` (all four empty) means the kind's
/// attributes are identical; `between` stores only non-empty diffs.
type AttributeDiff = ChannelDiff<AttributeChange>

// -- C1 (2026-06-02): widen the captured surface beyond column shape --------
//
// The 6.A.10 attribute surface captured kind + attribute column-shape only;
// references, indexes, and sequences rode through `applyDiff` unchanged
// (`CatalogDiff.fs:380-388`, the documented modulus). So `migrate A B`
// silently no-op'd any FK / index / sequence change between A and B. C1 adds
// the three missing change channels so the round-trip law
// `applyDiff (between A B) A = B` holds on them too. Each channel mirrors the
// `AttributeDiff` shape (Added / Removed / Renamed / Reshaped, keyed by SsKey),
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
type ReferenceDiff = ChannelDiff<ReferenceChange>

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
type IndexDiff = ChannelDiff<IndexChange>

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
type SequenceDiff = ChannelDiff<SequenceChange>

/// NM-17 — a single kind-OWN facet that `CatalogDiff` previously erased.
/// The attribute / reference / index / sequence channels cover a kind's
/// *children*; this channel covers the kind's own shape beyond presence/name:
/// its population `Modality`, `Triggers`, table-level `ColumnChecks`, and the
/// `IsActive` activation flag. NM-16 took the LIGHT route — naming these
/// erasures as `ToleratedDivergence`s so `norm d = 0` was *witnessed*, not
/// silent; NM-17 is the HEAVY route that reflects them as a real diff channel,
/// retiring those tolerances and restoring the agreement between `migrate`
/// (the `CatalogDiff` algebra) and the canary's `PhysicalSchema.diff`. Closed
/// DU; widen when a further kind-own field (`Description` / ext-props / module
/// structure — still unwitnessed residual) gets a consumer.
[<RequireQualifiedAccess>]
type KindFacet =
    | Modality
    | Triggers
    | ColumnChecks
    | IsActive

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
        /// NM-17 — per-kind kind-OWN facet diffs (sparse: only kinds present in
        /// BOTH catalogs whose `Modality` / `Triggers` / `ColumnChecks` /
        /// `IsActive` differ). Keyed by kind `SsKey`; every stored set is
        /// non-empty. Mirrors `AttributeDiffs`' sparsity contract.
        KindFacetDiffs : Map<SsKey, Set<KindFacet>>
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
/// The provenance of a synthesized SsKey behind a rename signal. `Known` names
/// it; `Unknown` makes the erasure EXPLICIT (recon #24 — was a bare `string`
/// with a silent `Option.defaultValue ""`, so a `None` source rendered as the
/// empty string, indistinguishable from a genuinely empty name).
[<RequireQualifiedAccess>]
type RenameSynthesisSource =
    | Known of source: string
    | Unknown

[<RequireQualifiedAccess>]
module RenameSynthesisSource =
    /// The operator-facing text for the source — `Unknown` voices as the named
    /// "unknown" rather than an empty string (the erasure stays visible).
    let text (s: RenameSynthesisSource) : string =
        match s with
        | RenameSynthesisSource.Known src -> src
        | RenameSynthesisSource.Unknown   -> "unknown"

type SynthesizedRenameWarning =
    {
        SynthesisSource : RenameSynthesisSource
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

    // -- Facet-as-lens reification (continues M7 one level down) -------------
    //
    // A facet of an entity's emitted shape is the unit the diff DETECTS and
    // APPLIES. The detector (`get s <> get t`) and the applier (`set (get src)
    // dest`) are the two halves of ONE fact: a focus on a field. M7 already
    // reified the channel TRAVERSAL as data (one `buildChannel` / `applyChannel`
    // over a `ChannelSpec`); the per-facet detect/apply pair was the residue it
    // left as five hand-written detector functions + five hand-written applier
    // functions, kept in lockstep by the comment "applyDiff's power exactly
    // matches the facet set `between` detects." Reifying each facet as a `Facet`
    // (a tagged lens) makes detection and application read the SAME table, so
    // that invariant — no un-captured field silently reconstructed — is
    // STRUCTURAL, not a hand-maintained correspondence between two match trees.

    /// A facet of an entity's emitted shape: its `Tag`, whether two entities
    /// `Differs` on it, and how to `Apply` it (copy `src`'s projection onto
    /// `dest`). For the common case both halves derive from ONE lens via
    /// `Facet.ofLens`, so they cannot desynchronize.
    [<NoEquality; NoComparison>]
    type private Facet<'entity, 'tag> =
        { Tag     : 'tag
          Differs : 'entity -> 'entity -> bool
          Apply   : 'entity -> 'entity -> 'entity }

    [<RequireQualifiedAccess>]
    module private Facet =

        /// An inline lens onto a field (or a tuple of fields, for compound
        /// facets) — local to the facet tables, deliberately NOT promoted to
        /// `CatalogLenses`: these foci have exactly two consumers (this table's
        /// detect + apply), so promoting them would be the over-extraction the
        /// `columnOf` docstring warns against.
        let lens (get: 'entity -> 'field) (set: 'field -> 'entity -> 'entity) : Lens<'entity, 'field> =
            { Get = get; Set = set }

        /// The common case: a facet IS a lens. Detect = `get s <> get t`;
        /// apply = `set (get src) dest`. The same lens drives both halves, so
        /// a facet can never detect on one field and reconstruct another.
        let ofLens (tag: 'tag) (l: Lens<'entity, 'field>) : Facet<'entity, 'tag> when 'field : equality =
            { Tag     = tag
              Differs = fun s t -> Lens.get l s <> Lens.get l t
              Apply   = fun src dest -> Lens.set l (Lens.get l src) dest }

        /// The changed facets between `s` and `t` over a facet table.
        let changed (table: Facet<'entity, 'tag> list) (s: 'entity) (t: 'entity) : Set<'tag> when 'tag : comparison =
            table
            |> List.choose (fun f -> if f.Differs s t then Some f.Tag else None)
            |> Set.ofList

        /// Apply the facets named in `tags` from `src` onto `dest`, folding in
        /// TABLE order — which is declaration order, matching the prior
        /// `Set.fold` over the facet set (Set iterates DU-declaration order).
        /// This preserves the Reference channel's `DbConstraint`-before-`Trust`
        /// co-occurrence requirement.
        let applyAll (table: Facet<'entity, 'tag> list) (src: 'entity) (tags: Set<'tag>) (dest: 'entity) : 'entity when 'tag : comparison =
            table |> List.fold (fun acc f -> if Set.contains f.Tag tags then f.Apply src acc else acc) dest

    /// 6.A.10 — the attribute column-shape facets. Mirrors `PhysicalSchema.diff`'s
    /// column comparison so the diff and the canary agree on "what counts as a
    /// column change." Each row is a lens; `Facet.changed`/`Facet.applyAll`
    /// derive the detector and the patcher. `Nullability` composes through
    /// `CatalogLenses.columnOf`; `DefaultValue` is a tuple lens (value + name).
    let private attributeFacets : Facet<Attribute, AttributeFacet> list =
        [ Facet.ofLens AttributeFacet.DataType    (Facet.lens (fun (a: Attribute) -> a.Type) (fun v a -> { a with Type = v }))
          Facet.ofLens AttributeFacet.Nullability (Lens.compose CatalogLenses.columnOf (Facet.lens (fun (c: ColumnRealization) -> c.IsNullable) (fun v c -> { c with IsNullable = v })))
          Facet.ofLens AttributeFacet.PrimaryKey  (Facet.lens (fun (a: Attribute) -> a.IsPrimaryKey) (fun v a -> { a with IsPrimaryKey = v }))
          Facet.ofLens AttributeFacet.Length      (Facet.lens (fun (a: Attribute) -> a.Length) (fun v a -> { a with Length = v }))
          Facet.ofLens AttributeFacet.Precision   (Facet.lens (fun (a: Attribute) -> a.Precision) (fun v a -> { a with Precision = v }))
          Facet.ofLens AttributeFacet.Scale       (Facet.lens (fun (a: Attribute) -> a.Scale) (fun v a -> { a with Scale = v }))
          Facet.ofLens AttributeFacet.Identity    (Facet.lens (fun (a: Attribute) -> a.IsIdentity) (fun v a -> { a with IsIdentity = v }))
          Facet.ofLens AttributeFacet.DefaultValue (Facet.lens (fun (a: Attribute) -> (a.DefaultValue, a.DefaultName)) (fun (dv, dn) a -> { a with DefaultValue = dv; DefaultName = dn }))
          Facet.ofLens AttributeFacet.Computed    (Facet.lens (fun (a: Attribute) -> a.Computed) (fun v a -> { a with Computed = v })) ]

    /// The empty channel diff — all four fields empty — over any change type.
    let private emptyChannelDiff<'change> : ChannelDiff<'change> =
        { Added = Set.empty; Removed = Set.empty; Renamed = Map.empty; Reshaped = [] }

    /// A channel diff is empty iff all four fields are empty. One predicate
    /// for every channel (the four byte-identical `*DiffIsEmpty` collapsed).
    let private channelDiffIsEmpty (d: ChannelDiff<'change>) : bool =
        Set.isEmpty d.Added
        && Set.isEmpty d.Removed
        && Map.isEmpty d.Renamed
        && List.isEmpty d.Reshaped

    // -- M7: the ChannelSpec — the descriptor IS data, traversed once --------
    //
    // The four per-channel diffs (attributes / references / indexes /
    // sequences) were four byte-identical builders + four byte-identical apply
    // patchers, differing ONLY in their entity accessor, facet detector, change
    // constructor, and change-key accessor (the source comment read "mirrors
    // `attributeDiff` EXACTLY"). M7 reifies that variation as a value: one
    // `ChannelSpec` per channel, ONE generic `buildChannel` fold over it, ONE
    // generic `applyChannel` patcher. ASYMMETRY: the sequence channel is
    // CATALOG-level (`'container = Catalog`), the other three KIND-level
    // (`'container = Kind`); the spec's container is generic, so each spec
    // supplies its own `entitiesOf` and the build/apply folds are container-
    // agnostic. The facet detectors (`changedFacets` etc., kept below) are the
    // only per-channel logic that genuinely varies; everything else is one fold.

    /// C1 — the reference FK-shape facets. The first five are plain lenses.
    /// `DbConstraint` / `Trust` are the ONE named exception to facet-as-lens:
    /// they don't focus a stored field — they project the `ConstraintState` DU
    /// to its two boolean dimensions and RENORMALIZE via `ofLegacyBooleans`, so
    /// a per-dimension lens would fail the Set-Get law (a lone `Trust` never
    /// targets a `NoDbConstraint` dest — co-occurrence + `DbConstraint`-first
    /// ordering is what makes the combined transition reconstruct). They stay
    /// hand-written `Differs`/`Apply` entries, but live IN this uniform table
    /// rather than scattered across two match expressions; table order keeps
    /// `DbConstraint` before `Trust` (declaration order).
    let private referenceFacets : Facet<Reference, ReferenceFacet> list =
        [ Facet.ofLens ReferenceFacet.Target          (Facet.lens (fun (r: Reference) -> r.TargetKind) (fun v r -> { r with TargetKind = v }))
          Facet.ofLens ReferenceFacet.SourceAttribute (Facet.lens (fun (r: Reference) -> r.SourceAttribute) (fun v r -> { r with SourceAttribute = v }))
          Facet.ofLens ReferenceFacet.OnDelete         (Facet.lens (fun (r: Reference) -> r.OnDelete) (fun v r -> { r with OnDelete = v }))
          Facet.ofLens ReferenceFacet.OnUpdate         (Facet.lens (fun (r: Reference) -> r.OnUpdate) (fun v r -> { r with OnUpdate = v }))
          Facet.ofLens ReferenceFacet.UserFk           (Facet.lens (fun (r: Reference) -> r.IsUserFk) (fun v r -> { r with IsUserFk = v }))
          { Tag     = ReferenceFacet.DbConstraint
            Differs = fun s t -> Reference.hasDbConstraint s <> Reference.hasDbConstraint t
            Apply   = fun src dest -> { dest with ConstraintState = ConstraintState.ofLegacyBooleans (Reference.hasDbConstraint src) (Reference.isConstraintTrusted dest) } }
          { Tag     = ReferenceFacet.Trust
            Differs = fun s t -> Reference.isConstraintTrusted s <> Reference.isConstraintTrusted t
            Apply   = fun src dest -> { dest with ConstraintState = ConstraintState.ofLegacyBooleans (Reference.hasDbConstraint dest) (Reference.isConstraintTrusted src) } } ]

    /// C1 — the index-shape facets. `Options` groups the nine `WITH (…)` knobs +
    /// platform-auto / disabled flags into one tuple lens (equality on the tuple
    /// is exactly the prior OR-of-inequalities; the setter splats all nine back).
    let private indexFacets : Facet<Index, IndexFacet> list =
        [ Facet.ofLens IndexFacet.Columns         (Facet.lens (fun (i: Index) -> i.Columns) (fun v i -> { i with Columns = v }))
          Facet.ofLens IndexFacet.Uniqueness      (Facet.lens (fun (i: Index) -> i.Uniqueness) (fun v i -> { i with Uniqueness = v }))
          Facet.ofLens IndexFacet.IncludedColumns (Facet.lens (fun (i: Index) -> i.IncludedColumns) (fun v i -> { i with IncludedColumns = v }))
          Facet.ofLens IndexFacet.Filter          (Facet.lens (fun (i: Index) -> i.Filter) (fun v i -> { i with Filter = v }))
          Facet.ofLens IndexFacet.DataSpace       (Facet.lens (fun (i: Index) -> i.DataSpace) (fun v i -> { i with DataSpace = v }))
          Facet.ofLens IndexFacet.Options
            (Facet.lens
                (fun (i: Index) -> (i.IsPlatformAuto, i.FillFactor, i.IsPadded, i.AllowRowLocks, i.AllowPageLocks, i.NoRecomputeStatistics, i.IgnoreDuplicateKey, i.IsDisabled, i.DataCompression))
                (fun (pa, ff, pad, arl, apl, nrs, idk, dis, dc) i ->
                    { i with
                        IsPlatformAuto = pa; FillFactor = ff; IsPadded = pad
                        AllowRowLocks = arl; AllowPageLocks = apl; NoRecomputeStatistics = nrs
                        IgnoreDuplicateKey = idk; IsDisabled = dis; DataCompression = dc })) ]

    /// NM-17 — the kind-OWN facets (population modality / triggers / table-level
    /// CHECKs / activation). Not a `ChannelSpec` channel (these are a
    /// `Set<KindFacet>`, applied directly in `applyDiff`), but the same
    /// facet-table shape; `Facet.changed`/`Facet.applyAll` drive it.
    let private kindFacets : Facet<Kind, KindFacet> list =
        [ Facet.ofLens KindFacet.Modality     (Facet.lens (fun (k: Kind) -> k.Modality) (fun v k -> { k with Modality = v }))
          Facet.ofLens KindFacet.Triggers     (Facet.lens (fun (k: Kind) -> k.Triggers) (fun v k -> { k with Triggers = v }))
          Facet.ofLens KindFacet.ColumnChecks (Facet.lens (fun (k: Kind) -> k.ColumnChecks) (fun v k -> { k with ColumnChecks = v }))
          Facet.ofLens KindFacet.IsActive     (Facet.lens (fun (k: Kind) -> k.IsActive) (fun v k -> { k with IsActive = v })) ]

    /// C1 — the sequence-shape facets. `Cache` groups `CacheMode` + `CacheSize`
    /// into one tuple lens.
    let private sequenceFacets : Facet<Sequence, SequenceFacet> list =
        [ Facet.ofLens SequenceFacet.Schema     (Facet.lens (fun (s: Sequence) -> s.Schema) (fun v s -> { s with Schema = v }))
          Facet.ofLens SequenceFacet.DataType   (Facet.lens (fun (s: Sequence) -> s.DataType) (fun v s -> { s with DataType = v }))
          Facet.ofLens SequenceFacet.StartValue (Facet.lens (fun (s: Sequence) -> s.StartValue) (fun v s -> { s with StartValue = v }))
          Facet.ofLens SequenceFacet.Increment  (Facet.lens (fun (s: Sequence) -> s.Increment) (fun v s -> { s with Increment = v }))
          Facet.ofLens SequenceFacet.Minimum    (Facet.lens (fun (s: Sequence) -> s.Minimum) (fun v s -> { s with Minimum = v }))
          Facet.ofLens SequenceFacet.Maximum    (Facet.lens (fun (s: Sequence) -> s.Maximum) (fun v s -> { s with Maximum = v }))
          Facet.ofLens SequenceFacet.Cycle      (Facet.lens (fun (s: Sequence) -> s.IsCycleEnabled) (fun v s -> { s with IsCycleEnabled = v }))
          Facet.ofLens SequenceFacet.Cache      (Facet.lens (fun (s: Sequence) -> (s.CacheMode, s.CacheSize)) (fun (cm, cs) s -> { s with CacheMode = cm; CacheSize = cs })) ]

    // The per-facet APPLIERS are no longer separate match expressions — each
    // facet's `Apply` is the `set (get src) dest` half of its lens in the tables
    // above. `Facet.applyAll` patches a `dest` from a `src` over a facet table;
    // because detection and application read the SAME table, no un-captured field
    // can be silently reconstructed (the C1 invariant, now structural).

    /// M7 — the kind-scoped channel reified as data: the four accessors that
    /// vary per channel, plus the facet detector, change constructor /
    /// destructor, and rename-applier. `'container` is `Kind` for the
    /// attribute / reference / index channels and `Catalog` for sequences;
    /// `entitiesOf` projects the channel's entity list out of either. One spec
    /// value drives both `buildChannel` (observation) and `applyChannel`
    /// (action), so the two stay structurally locked together by construction.
    type private ChannelSpec<'container, 'entity, 'facet, 'change
                                when 'facet: comparison> =
        {
            /// Project the channel's entity list out of its container.
            entitiesOf    : 'container -> 'entity list
            /// The entity's stable identity (the diff matches on it).
            keyOf         : 'entity -> SsKey
            /// The entity's logical name (a `Name` change ⇒ `Renamed`).
            nameOf        : 'entity -> Name
            /// The facet table — the SINGLE source for both detection
            /// (`Facet.changed`) and application (`Facet.applyAll`). Because
            /// build and apply read the same list, they cannot desynchronize.
            facets        : Facet<'entity, 'facet> list
            /// Construct the channel's change-evidence record.
            mkChange      : SsKey -> Set<'facet> -> 'change
            /// The key a change-evidence record is keyed by.
            keyOfChange   : 'change -> SsKey
            /// The facet set a change-evidence record carries.
            facetsOf      : 'change -> Set<'facet>
            /// Rename an entity to a new `Name` (every other field rides through).
            renameTo      : Name -> 'entity -> 'entity
        }

    /// M7 — ONE channel-diff fold, driven by a `ChannelSpec`. Replaces the four
    /// byte-identical `attributeDiff` / `referenceDiff` / `indexDiff` /
    /// `sequenceDiffBetween` builders. Entities match by `SsKey`: present-in-
    /// target-only → `Added`, present-in-source-only → `Removed`; a shared
    /// `SsKey` whose `Name` differs → `Renamed` (independent of facet changes);
    /// a shared `SsKey` whose emitted shape differs → `Reshaped`. The `Reshaped`
    /// list is built in SOURCE-LIST order (the canonical order, not the Set's
    /// hash order) to preserve T1 determinism.
    let private buildChannel
        (spec: ChannelSpec<'container, 'entity, 'facet, 'change>)
        (source: 'container)
        (target: 'container)
        : ChannelDiff<'change>
        =
        let srcEntities = spec.entitiesOf source
        let tgtByKey = spec.entitiesOf target |> List.map (fun e -> spec.keyOf e, e) |> Map.ofList
        let srcKeys = srcEntities |> List.map spec.keyOf |> Set.ofList
        let tgtKeys = tgtByKey |> Map.toSeq |> Seq.map fst |> Set.ofSeq
        let srcByKey = srcEntities |> List.map (fun e -> spec.keyOf e, e) |> Map.ofList
        let renamed =
            Set.intersect srcKeys tgtKeys
            |> Set.fold
                (fun acc key ->
                    let s = Map.find key srcByKey
                    let t = Map.find key tgtByKey
                    if spec.nameOf s <> spec.nameOf t then
                        Map.add key { OldName = spec.nameOf s; NewName = spec.nameOf t; PassVersion = version } acc
                    else acc)
                Map.empty
        // Source order preserves determinism (T1) — the container's entity
        // list is the canonical order, not the Set's hash order.
        let reshaped =
            srcEntities
            |> List.choose (fun s ->
                match Map.tryFind (spec.keyOf s) tgtByKey with
                | Some t ->
                    let facets = Facet.changed spec.facets s t
                    if Set.isEmpty facets then None
                    else Some (spec.mkChange (spec.keyOf s) facets)
                | None -> None)
        { Added = Set.difference tgtKeys srcKeys
          Removed = Set.difference srcKeys tgtKeys
          Renamed = renamed
          Reshaped = reshaped }

    let private attributeSpec : ChannelSpec<Kind, Attribute, AttributeFacet, AttributeChange> =
        { entitiesOf = fun (k: Kind) -> k.Attributes
          keyOf = fun a -> a.SsKey
          nameOf = fun a -> a.Name
          facets = attributeFacets
          mkChange = fun key facets -> { AttributeKey = key; Facets = facets }
          keyOfChange = fun c -> c.AttributeKey
          facetsOf = fun c -> c.Facets
          renameTo = fun n a -> { a with Name = n } }

    let private referenceSpec : ChannelSpec<Kind, Reference, ReferenceFacet, ReferenceChange> =
        { entitiesOf = fun (k: Kind) -> k.References
          keyOf = fun r -> r.SsKey
          nameOf = fun r -> r.Name
          facets = referenceFacets
          mkChange = fun key facets -> { ReferenceKey = key; Facets = facets }
          keyOfChange = fun c -> c.ReferenceKey
          facetsOf = fun c -> c.Facets
          renameTo = fun n r -> { r with Name = n } }

    let private indexSpec : ChannelSpec<Kind, Index, IndexFacet, IndexChange> =
        { entitiesOf = fun (k: Kind) -> k.Indexes
          keyOf = fun i -> i.SsKey
          nameOf = fun i -> i.Name
          facets = indexFacets
          mkChange = fun key facets -> { IndexKey = key; Facets = facets }
          keyOfChange = fun c -> c.IndexKey
          facetsOf = fun c -> c.Facets
          renameTo = fun n i -> { i with Name = n } }

    let private sequenceSpec : ChannelSpec<Catalog, Sequence, SequenceFacet, SequenceChange> =
        { entitiesOf = fun (c: Catalog) -> c.Sequences
          keyOf = fun s -> s.SsKey
          nameOf = fun s -> s.Name
          facets = sequenceFacets
          mkChange = fun key facets -> { SequenceKey = key; Facets = facets }
          keyOfChange = fun c -> c.SequenceKey
          facetsOf = fun c -> c.Facets
          renameTo = fun n s -> { s with Name = n } }

    let private attributeDiff (sourceKind: Kind) (targetKind: Kind) : AttributeDiff =
        buildChannel attributeSpec sourceKind targetKind

    let private referenceDiff (sourceKind: Kind) (targetKind: Kind) : ReferenceDiff =
        buildChannel referenceSpec sourceKind targetKind

    let private indexDiff (sourceKind: Kind) (targetKind: Kind) : IndexDiff =
        buildChannel indexSpec sourceKind targetKind

    let private sequenceDiffBetween (source: Catalog) (target: Catalog) : SequenceDiff =
        buildChannel sequenceSpec source target

    /// M7 — ONE channel-apply patcher, driven by the SAME `ChannelSpec` as
    /// `buildChannel`. Replaces the four byte-identical `applyAttributeDiff` /
    /// `applyReferenceDiff` / `applyIndexDiff` / `applySequenceDiff` patchers.
    /// Removed entities drop; surviving entities are renamed (`Renamed`) and
    /// facet-patched (`Reshaped`) from the recorded target; `Added` entries are
    /// COPIED whole from the target container, appended in target order. New
    /// facet values + new entities source from `targetContainer` (the recorded
    /// target Kind / Catalog); `None` (no recorded target) contributes no adds.
    /// No smart-constructor reconstruction → the `{ create … with … }` default-
    /// substitution bomb cannot bite.
    let private applyChannel
        (spec: ChannelSpec<'container, 'entity, 'facet, 'change>)
        (d: ChannelDiff<'change>)
        (targetContainer: 'container option)
        (baseEntities: 'entity list)
        : 'entity list
        =
        let tgtEntities = targetContainer |> Option.map spec.entitiesOf |> Option.defaultValue []
        let tgtByKey (key: SsKey) : 'entity option =
            tgtEntities |> List.tryFind (fun e -> spec.keyOf e = key)
        let survivors =
            baseEntities
            |> List.filter (fun e -> not (Set.contains (spec.keyOf e) d.Removed))
            |> List.map (fun e ->
                let renamed1 =
                    match Map.tryFind (spec.keyOf e) d.Renamed with
                    | Some r -> spec.renameTo r.NewName e
                    | None -> e
                match d.Reshaped |> List.tryFind (fun c -> spec.keyOfChange c = spec.keyOf e), tgtByKey (spec.keyOf e) with
                | Some change, Some src -> Facet.applyAll spec.facets src (spec.facetsOf change) renamed1
                | _ -> renamed1)
        let added = tgtEntities |> List.filter (fun e -> Set.contains (spec.keyOf e) d.Added)
        survivors @ added

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
    /// Returns a `CatalogDiff` directly — the constructor is **total**.
    /// It cannot fail over any pair of Catalog inputs (Catalog has no
    /// failure modes the diff would surface), so the displacement is the
    /// only inhabited result; there is no `Error` branch to thread. The
    /// vestigial `Result<CatalogDiff, EmitError>` wrapper (carried for an
    /// imagined future `DiffMismatchedSchema` failure that never arrived)
    /// is dropped: `compose`, `inverse`, and the groupoid laws read
    /// cleanly off a total displacement, and call sites that genuinely
    /// thread an `EmitError` `Ok`-wrap at their own boundary.
    let between
        (source: Catalog)
        (target: Catalog)
        : CatalogDiff
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
                        if channelDiffIsEmpty d then acc else Map.add key d acc
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
                        if channelDiffIsEmpty d then acc else Map.add key d acc
                    | _ -> acc)
                Map.empty
        let indexDiffs =
            intersect
            |> Set.fold
                (fun acc key ->
                    match Catalog.tryFindKind key source, Catalog.tryFindKind key target with
                    | Some sk, Some tk ->
                        let d = indexDiff sk tk
                        if channelDiffIsEmpty d then acc else Map.add key d acc
                    | _ -> acc)
                Map.empty
        // NM-17 — descend into each kind's OWN facets (modality / triggers /
        // CHECKs / activation) for every kind in BOTH catalogs; store only
        // non-empty sets (an unchanged kind contributes nothing, so an
        // idempotent redeploy stays empty → CDC-silence). Closes the NM-16
        // tolerance erasure: these were named-but-unreflected; now reflected.
        let kindFacetDiffs =
            intersect
            |> Set.fold
                (fun acc key ->
                    match Catalog.tryFindKind key source, Catalog.tryFindKind key target with
                    | Some sk, Some tk ->
                        let facets = Facet.changed kindFacets sk tk
                        if Set.isEmpty facets then acc else Map.add key facets acc
                    | _ -> acc)
                Map.empty
        // C1 — sequences are catalog-level, not kind-scoped.
        let sequenceDiff = sequenceDiffBetween source target
        CatalogDiff
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
                KindFacetDiffs = kindFacetDiffs
            }

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

    /// NM-17 — the per-kind kind-OWN facet diffs (sparse: only kinds present in
    /// both catalogs with a changed `Modality` / `Triggers` / `ColumnChecks` /
    /// `IsActive`). Keyed by kind `SsKey`.
    let kindFacetDiffs (CatalogDiff d) : Map<SsKey, Set<KindFacet>> = d.KindFacetDiffs

    /// NM-17 — the kind-facet diff for one kind, or `None` if the kind's own
    /// facets are identical (or the kind is not present in both catalogs).
    let kindFacetDiffOf (key: SsKey) (CatalogDiff d) : Set<KindFacet> option =
        Map.tryFind key d.KindFacetDiffs

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
                            { SynthesisSource = (match rSrc with Some s -> RenameSynthesisSource.Known s | None -> RenameSynthesisSource.Unknown)
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
        && channelDiffIsEmpty data.SequenceDiff
        && Map.isEmpty data.KindFacetDiffs

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
    // presence/name + the kind-OWN facets `Modality` / `Triggers` /
    // `ColumnChecks` / `IsActive` (`KindFacet`, NM-17) + attribute
    // presence/name + the nine column-shape facets (`AttributeFacet`) +
    // references + indexes + sequences (the C1 channels, each with its own
    // `apply*Diff` patch). (NM-16 took the LIGHT route — naming the trigger /
    // CHECK / modality / activation erasure as `ToleratedDivergence`s so the
    // gap was witnessed, not silent; NM-17, 2026-06-14, took the HEAVY route:
    // the `KindFacet` diff channel above reflects them for real, so the
    // `CatalogDiff` algebra and the canary's `PhysicalSchema.diff` now AGREE on
    // "what is a change," and those four tolerances are retired.) It does NOT
    // capture kind-level `Description`, `ExtendedProperties`, or module
    // structure — those ride through from `base` unchanged and remain
    // unwitnessed residual. The round-trip law therefore holds for
    // A→B evolutions within the captured surface, witnessed order-insensitively
    // by `between B (applyDiff (between A B) A) |> isEmpty`. A future
    // self-contained diff (inline payloads, no stored source/target) would
    // widen the surface; deferred under IR-grows-under-evidence.

    /// NM-17 — patch one kind-OWN facet of `dest` from `src` (the recorded
    // The kind-OWN facet applier is `Facet.applyAll kindFacets` (the apply half
    // of the `kindFacets` table defined above) — see `applyDiff` below.

    /// 6.A.11 — apply a `CatalogDiff` to a base `Catalog`, reconstructing the
    /// target modulo the captured surface. Total: trusts the delta (no
    /// re-validation), so the round-trip law is `between B (applyDiff
    /// (between A B) A) |> isEmpty`. Attributes / references / indexes /
    /// sequences (C1) and the kind-OWN facets modality / triggers / CHECKs /
    /// activation (NM-17) are all captured and patched; only kind `Description`
    /// / ext-props / module structure ride through from `base` (the remaining
    /// residual). H-007: the `between` peer that makes Time an evolution algebra.
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
                | Some ad -> { renamed1 with Attributes = applyChannel attributeSpec ad tgtKind renamed1.Attributes }
                | None -> renamed1
            let withRefs =
                match Map.tryFind k.SsKey data.ReferenceDiffs with
                | Some rd -> { withAttrs with References = applyChannel referenceSpec rd tgtKind withAttrs.References }
                | None -> withAttrs
            let withIndexes =
                match Map.tryFind k.SsKey data.IndexDiffs with
                | Some idd -> { withRefs with Indexes = applyChannel indexSpec idd tgtKind withRefs.Indexes }
                | None -> withRefs
            // NM-17 — patch the kind's own facets from the recorded target.
            match Map.tryFind k.SsKey data.KindFacetDiffs, tgtKind with
            | Some facets, Some tk -> Facet.applyAll kindFacets tk facets withIndexes
            | _ -> withIndexes
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
            Sequences = applyChannel sequenceSpec data.SequenceDiff (Some tgt) baseCatalog.Sequences }

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
            // NM-17 — kinds present in both catalogs whose OWN facets changed
            // (modality / triggers / CHECKs / activation). One move per kind,
            // mirroring `ChangedAttributes` (entity-count, not facet-count).
            ChangedKinds      : int
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
            ChangedKinds      = Map.count data.KindFacetDiffs
            AddedAttributes   = sumAttr (fun ad -> Set.count ad.Added)
            RemovedAttributes = sumAttr (fun ad -> Set.count ad.Removed)
            RenamedAttributes = sumAttr (fun ad -> Map.count ad.Renamed)
            ChangedAttributes = sumAttr (fun ad -> List.length ad.Reshaped)
            AddedReferences   = sumRef (fun rd -> Set.count rd.Added)
            RemovedReferences = sumRef (fun rd -> Set.count rd.Removed)
            RenamedReferences = sumRef (fun rd -> Map.count rd.Renamed)
            ChangedReferences = sumRef (fun rd -> List.length rd.Reshaped)
            AddedIndexes      = sumIdx (fun idd -> Set.count idd.Added)
            RemovedIndexes    = sumIdx (fun idd -> Set.count idd.Removed)
            RenamedIndexes    = sumIdx (fun idd -> Map.count idd.Renamed)
            ChangedIndexes    = sumIdx (fun idd -> List.length idd.Reshaped)
            AddedSequences    = Set.count data.SequenceDiff.Added
            RemovedSequences  = Set.count data.SequenceDiff.Removed
            RenamedSequences  = Map.count data.SequenceDiff.Renamed
            ChangedSequences  = List.length data.SequenceDiff.Reshaped
        }

    /// The norm ‖δ‖ — total move count, the sum of the channel counts (T15,
    /// schema side). `norm (between A A) = 0`; `norm d = 0 ⟺ isEmpty d`
    /// (because `between` stores only non-empty attribute diffs, so any stored
    /// `AttributeDiff` contributes ≥ 1 to the norm).
    let norm (d: CatalogDiff) : int =
        let c = channelCounts d
        c.RenamedKinds + c.AddedKinds + c.RemovedKinds + c.ChangedKinds
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
        let composable = isEmpty (between (target d1) (source d2))
        if not composable then None else Some (between (source d1) (target d2))

    /// M12 — the groupoid inverse: the displacement that returns target to
    /// source. `inverse d = between (target d) (source d)`. By the round-trip
    /// law (`applyDiff` / `between` peer), applying `inverse d` to `target d`
    /// reproduces `source d` modulo the captured surface; and `compose d
    /// (inverse d)` is the identity at `source d` (the groupoid law). The
    /// inverse always exists — `between` is total — so the partial groupoid's
    /// `Some`-side is closed under inversion.
    let inverse (d: CatalogDiff) : CatalogDiff = between (target d) (source d)
