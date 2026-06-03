namespace Projection.Core

/// A single **destructive removal** a displacement `B ⊖ A` would perform — the
/// `Remove` move (`WAVE_6_ONTOLOGY.md` §5). Each is data loss the displacement
/// cannot undo. The **complete** enumeration across every `CatalogDiff` channel
/// (P-GATE — the gate set must span every way the plan loses): dropped kinds,
/// attributes, references (FK), indexes, and sequences. A rename is *not* a loss
/// — it is the data-preserving RefactorLog channel (`‖rename‖_data = 0`, A43),
/// never a drop+add. (Reshape that narrows is a *warning*, not a loss — the
/// rows survive; only `Remove` deallocates.)
/// `[<RequireQualifiedAccess>]` — `DropIndex` / `DropSequence` collide with the
/// `Statement` DU's imperative-DDL variants of the same name (C1).
[<RequireQualifiedAccess>]
type SchemaLoss =
    | DropKind of kind: SsKey
    | DropAttribute of kind: SsKey * attribute: SsKey
    | DropReference of kind: SsKey * reference: SsKey
    | DropIndex of kind: SsKey * index: SsKey
    | DropSequence of sequence: SsKey

/// The operator's **declared-loss gate** input (`WAVE_6_ONTOLOGY.md` §5 Remove
/// cell — "destructive → refuse (declared-loss gate)"). The default refuses
/// every destructive removal; the operator *declares* the losses they accept,
/// by their rendered handle (`Migration.lossToken`), and an **undeclared** removal
/// refuses fail-loud. Granular + auditable — the "prove it / PR-reviewed"
/// premise (§2), not a blanket toggle.
///   - `DeclareNone` — accept no loss (the safe default; refuse every removal).
///   - `DeclareAll` — accept every loss (the prior blanket `allowDrops = true`).
///   - `DeclareThese ids` — accept exactly the removals whose `lossId` is in
///     `ids`; any other removal refuses.
type LossDeclaration =
    | DeclareNone
    | DeclareAll
    | DeclareThese of Set<string>

[<RequireQualifiedAccess>]
module LossDeclaration =

    /// The two extremes of the prior boolean gate: `true` accepts every loss
    /// (`DeclareAll`), `false` accepts none (`DeclareNone`). The bridge for
    /// callers that still carry a blanket `allowDrops` flag.
    let ofAllowDrops (allow: bool) : LossDeclaration = if allow then DeclareAll else DeclareNone

    /// Whether the declaration permits *any* destructive emission. Once the gate
    /// has cleared every undeclared loss (the plan is safe), this is the signal
    /// to the imperative emitter that the remaining drops are sanctioned.
    let permitsDrops (declaration: LossDeclaration) : bool =
        match declaration with
        | DeclareNone -> false
        | DeclareAll | DeclareThese _ -> true

/// The pre-execution view of `migrate A B` — *what the migration will touch*,
/// derived purely from the displacement. It is the change-manifest of δ at
/// **plan** time (its post-execution peer, `ChangeManifest`, additionally
/// carries the realized refactorlog reference + CDC count once the run lands).
/// Minimum-viable-touches: a kind whose shape is unchanged contributes nothing;
/// a renamed kind is a RefactorLog move, never a drop+add; a reshaped attribute
/// names only the facets that changed (the ALTER touches one column, not the
/// table).
type MigrationPreview =
    {
        Channels           : CatalogDiff.ChannelCounts
        Norm               : int
        /// Kind renames (key, from, to) — the data-preserving RefactorLog channel.
        RenamedKinds       : (SsKey * Name * Name) list
        AddedKinds         : SsKey list
        RemovedKinds       : SsKey list
        /// Per-attribute reshapes (kindKey, attrKey, facets) — the ALTER channel.
        ReshapedAttributes : (SsKey * SsKey * Set<AttributeFacet>) list
    }

/// The plan `migrate A B` produces **before it touches anything**: the
/// displacement `B ⊖ A`, the preview (what it will touch), and the violations
/// (what it refuses). The dry-run IS this value — the operator inspects it,
/// confirms the touches are minimal and the drops are intended, then executes.
/// This is the L3 bullseye reified: `migrate A B = emit(B ⊖ A)` (T16, the master
/// equation), composed under the change algebra (T13/T14) and gated fail-loud.
type MigrationPlan =
    {
        Diff       : CatalogDiff
        Preview    : MigrationPreview
        Violations : SchemaLoss list
    }

[<RequireQualifiedAccess>]
module Migration =

    let private previewOf (diff: CatalogDiff) : MigrationPreview =
        let renamed =
            CatalogDiff.renamed diff
            |> Map.toList
            |> List.sortBy (fun (k, _) -> SsKey.rootOriginal k)
            |> List.map (fun (k, r) -> (k, r.OldName, r.NewName))
        let reshaped =
            CatalogDiff.attributeDiffs diff
            |> Map.toList
            |> List.sortBy (fun (k, _) -> SsKey.rootOriginal k)
            |> List.collect (fun (kindKey, ad) ->
                ad.Changed
                |> List.sortBy (fun c -> SsKey.rootOriginal c.AttributeKey)
                |> List.map (fun c -> (kindKey, c.AttributeKey, c.Facets)))
        { Channels = CatalogDiff.channelCounts diff
          Norm = CatalogDiff.norm diff
          RenamedKinds = renamed
          AddedKinds = CatalogDiff.added diff |> Set.toList
          RemovedKinds = CatalogDiff.removed diff |> Set.toList
          ReshapedAttributes = reshaped }

    /// The **complete** destructive-removal enumeration of a displacement — every
    /// `Remove` move across every `CatalogDiff` channel (P-GATE: the gate set must
    /// span every way the plan loses). Dropped kinds, attributes, references (FK),
    /// indexes, and sequences. Sorted deterministically (T1). This is the set the
    /// declared-loss gate ranges over; `undeclaredLosses` filters it by the
    /// operator's `LossDeclaration`.
    /// Deterministic total order over losses (T1): channel rank, then the
    /// `SsKey` roots. Keeps the kind / attribute / reference / index / sequence
    /// families grouped and stable across runs.
    let private lossSortKey (loss: SchemaLoss) : int * string * string =
        match loss with
        | SchemaLoss.DropKind k           -> 0, SsKey.rootOriginal k, ""
        | SchemaLoss.DropAttribute (k, a) -> 1, SsKey.rootOriginal k, SsKey.rootOriginal a
        | SchemaLoss.DropReference (k, r) -> 2, SsKey.rootOriginal k, SsKey.rootOriginal r
        | SchemaLoss.DropIndex (k, i)     -> 3, SsKey.rootOriginal k, SsKey.rootOriginal i
        | SchemaLoss.DropSequence s       -> 4, SsKey.rootOriginal s, ""

    let destructiveLosses (diff: CatalogDiff) : SchemaLoss list =
        let bySs sel = List.sortBy sel
        let kindDrops =
            CatalogDiff.removed diff
            |> Set.toList
            |> List.map SchemaLoss.DropKind
        let attributeDrops =
            CatalogDiff.attributeDiffs diff
            |> Map.toList
            |> List.collect (fun (kindKey, ad) ->
                ad.Removed |> Set.toList |> List.map (fun a -> SchemaLoss.DropAttribute (kindKey, a)))
        let referenceDrops =
            CatalogDiff.referenceDiffs diff
            |> Map.toList
            |> List.collect (fun (kindKey, rd) ->
                rd.Removed |> Set.toList |> List.map (fun r -> SchemaLoss.DropReference (kindKey, r)))
        let indexDrops =
            CatalogDiff.indexDiffs diff
            |> Map.toList
            |> List.collect (fun (kindKey, idx) ->
                idx.Removed |> Set.toList |> List.map (fun i -> SchemaLoss.DropIndex (kindKey, i)))
        let sequenceDrops =
            (CatalogDiff.sequenceDiff diff).Removed
            |> Set.toList
            |> List.map SchemaLoss.DropSequence
        kindDrops @ attributeDrops @ referenceDrops @ indexDrops @ sequenceDrops
        |> bySs lossSortKey

    /// The copy-pasteable handle for a loss — the form the refusal prints and
    /// `--declare-drop` matches. **Identity-based** (the `SsKey` roots), so it is
    /// source-independent: the same token renders from a diff, a refusal, or a
    /// CLI flag without a catalog in hand (P-ID — identity is the match key, never
    /// the name). `kind:<k>` / `attr:<k>.<a>` / `fk:<k>.<r>` / `index:<k>.<i>` /
    /// `seq:<s>`.
    let lossToken (loss: SchemaLoss) : string =
        match loss with
        | SchemaLoss.DropKind k           -> sprintf "kind:%s" (SsKey.rootOriginal k)
        | SchemaLoss.DropAttribute (k, a) -> sprintf "attr:%s.%s" (SsKey.rootOriginal k) (SsKey.rootOriginal a)
        | SchemaLoss.DropReference (k, r) -> sprintf "fk:%s.%s" (SsKey.rootOriginal k) (SsKey.rootOriginal r)
        | SchemaLoss.DropIndex (k, i)     -> sprintf "index:%s.%s" (SsKey.rootOriginal k) (SsKey.rootOriginal i)
        | SchemaLoss.DropSequence s       -> sprintf "seq:%s" (SsKey.rootOriginal s)

    /// The declared-loss gate (`WAVE_6_ONTOLOGY.md` §5 Remove cell). The
    /// **undeclared** destructive removals — the losses the operator has *not*
    /// accepted. A plan carrying any of them is unsafe and refuses fail-loud; the
    /// operator declares them (by `lossToken`) to proceed. Granularity is enforced
    /// here, engine-side: with `DeclareThese`, a removal not in the declared set
    /// stays undeclared (refuses), so the declarative deploy's coarse drop levers
    /// are only ever authorized once *every* actual drop is covered.
    let undeclaredLosses (declaration: LossDeclaration) (diff: CatalogDiff) : SchemaLoss list =
        match declaration with
        | DeclareAll -> []
        | DeclareNone -> destructiveLosses diff
        | DeclareThese declared ->
            destructiveLosses diff
            |> List.filter (fun loss -> not (Set.contains (lossToken loss) declared))

    /// Plan the migration `A → B`: compute the displacement, the preview (what it
    /// will touch), and the violations (the **undeclared** destructive removals it
    /// refuses). Pure — no I/O, no write. The `declaration` is the declared-loss
    /// gate's input: `DeclareNone` (the safe default) carries every destructive
    /// removal as a violation; `DeclareAll` carries none; `DeclareThese` carries
    /// exactly the removals the operator did not name. Threads the Π-side
    /// `EmitError` (`between`'s error).
    let plan (declaration: LossDeclaration) (source: Catalog) (target: Catalog) : Result<MigrationPlan, EmitError> =
        match CatalogDiff.between source target with
        | Error e -> Error e
        | Ok diff ->
            Ok { Diff = diff; Preview = previewOf diff; Violations = undeclaredLosses declaration diff }

    /// True iff the plan carries no undeclared losses — safe to execute / publish.
    let isSafe (p: MigrationPlan) : bool = List.isEmpty p.Violations

    /// True iff the displacement is empty — an **idempotent** migration (zero
    /// minimum-viable touches; the redeploy is a no-op, CDC-silent). `migrate A A`
    /// is always idempotent.
    let isIdempotent (p: MigrationPlan) : bool = CatalogDiff.isEmpty p.Diff

    /// The structural execution: apply the plan's displacement to the source
    /// catalog. **T16 — the Project square / master equation:**
    /// `applyTo (plan A B) A ≡ B` modulo the diff's captured surface — i.e.
    /// `migrate A B` reproduces B. This is the algebraic heart the live
    /// executor (`MigrationRun`) realizes against real substrates; it is the
    /// round-trip the A→B canary asserts.
    let applyTo (p: MigrationPlan) (source: Catalog) : Catalog =
        CatalogDiff.applyDiff source p.Diff

    /// Record the completed migration as the next `Episode` on a timeline — the
    /// durable provenance of the run (6.H). The migration's **target** becomes
    /// the new schema plane; the realized data movement (`DataObservation`) and
    /// the emitted refactorlog reference are supplied by the executor once the
    /// write lands. (`Profile.empty` — the statistical profile is a per-run
    /// input, not durable provenance, §12.4.)
    let toEpisode
        (coordinate: EpisodeCoordinate)
        (refactorLogRef: string option)
        (data: DataObservation)
        (p: MigrationPlan)
        : Episode =
        Episode.create coordinate (CatalogDiff.target p.Diff) Profile.empty refactorLogRef data
