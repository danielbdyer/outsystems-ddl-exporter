namespace Projection.Core

/// A fail-loud reason `migrate A B` must refuse **before any write** — the
/// safety gate of the L3 composition. The migration computes the displacement
/// `B ⊖ A` and emits only the *minimum-viable* touches; a **destructive** touch
/// (dropping a kind or attribute) is data loss the displacement cannot undo, so
/// it is refused unless the operator opts in (`allowDrops`). A rename is *not* a
/// violation — it is the data-preserving RefactorLog channel (`‖rename‖_data = 0`,
/// A43), never a drop+add.
type MigrationViolation =
    | WouldDropKind of key: SsKey * name: Name
    | WouldDropAttribute of kind: SsKey * attribute: SsKey

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
        Violations : MigrationViolation list
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

    /// The destructive touches in a displacement — dropped kinds (named from the
    /// source catalog) and dropped attributes. These are the gate `allowDrops`
    /// opens; with it closed, a plan carrying any of them is unsafe and refuses.
    let private violationsOf (source: Catalog) (diff: CatalogDiff) : MigrationViolation list =
        // A removed kind is present in the source by definition (Removed = in
        // source, not in target), so we map over source kinds — no lookup miss.
        let removedSet = CatalogDiff.removed diff
        let kindDrops =
            Catalog.allKinds source
            |> List.filter (fun k -> Set.contains k.SsKey removedSet)
            |> List.sortBy (fun k -> SsKey.rootOriginal k.SsKey)
            |> List.map (fun k -> WouldDropKind (k.SsKey, k.Name))
        let attributeDrops =
            CatalogDiff.attributeDiffs diff
            |> Map.toList
            |> List.sortBy (fun (k, _) -> SsKey.rootOriginal k)
            |> List.collect (fun (kindKey, ad) ->
                ad.Removed |> Set.toList |> List.map (fun attrKey -> WouldDropAttribute (kindKey, attrKey)))
        kindDrops @ attributeDrops

    /// Plan the migration `A → B`: compute the displacement, the preview (what it
    /// will touch), and the violations (what it refuses). Pure — no I/O, no
    /// write. With `allowDrops = false` the plan carries every destructive touch
    /// as a violation; with `true` the violation list is empty (the operator
    /// accepts the data loss). Threads the Π-side `EmitError` (`between`'s error).
    let plan (allowDrops: bool) (source: Catalog) (target: Catalog) : Result<MigrationPlan, EmitError> =
        match CatalogDiff.between source target with
        | Error e -> Error e
        | Ok diff ->
            let violations = if allowDrops then [] else violationsOf source diff
            Ok { Diff = diff; Preview = previewOf diff; Violations = violations }

    /// True iff the plan carries no fail-loud violations — safe to execute.
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
