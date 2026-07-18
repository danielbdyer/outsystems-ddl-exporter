namespace Projection.Core

/// Π-side error envelope. Names the structured failure modes the
/// `ArtifactByKind` smart constructor surfaces. `EmitError` flows through
/// FSharp.Core's two-arity `Result<'a, 'b>` (the type alias `Emitter
/// <'element>` in `Types.fs` uses `Result<ArtifactByKind<'element>,
/// EmitError>`); this coexists with `Projection.Core.Result<'a>`
/// per the arity-disambiguation note in `Result.fs`.
///
/// Per `CHAPTER_3_PRESCOPE_ARTIFACTBYKIND_REFACTOR.md` §2, the strict-
/// equality invariant produces two complementary error variants —
/// `KindNotProduced` for a key absent from the slice, `UnexpectedKind`
/// for a key absent from the source Catalog. Both surface bugs:
/// missing keys come from emitters that forgot a kind; extra keys come
/// from stale fixtures, copy-and-paste errors, or layering violations
/// (derived keys not registered on the Catalog).
type EmitError =
    /// The emitter did not produce output for an SsKey present in the
    /// source Catalog. Surfaces a missing key under strict equality.
    | KindNotProduced of SsKey
    /// The emitter produced output for an SsKey not present in the
    /// source Catalog. Surfaces a stale fixture, copy-and-paste error,
    /// or layering violation (derived keys whose registration on the
    /// Catalog is missing).
    | UnexpectedKind of SsKey
    /// Per-key rendering failed; reason is human-readable. Surfaces a
    /// structural failure on a present, expected SsKey.
    | RenderFailed of SsKey * reason: string
    /// Two or more sibling emitters under the same composition both
    /// produced populated output for the same SsKey — violates the
    /// composer's partition contract (chapter 4.1.B slice θ; per
    /// `CHAPTER_4_PRESCOPE_DATA_TRIUMVIRATE.md` §5.3). Carries the
    /// kind whose coverage overlapped + the names of the overlapping
    /// emitters (Static / MigrationDependencies / Bootstrap) so the
    /// operator diagnostic surfaces the configuration-level mismatch
    /// (e.g., a kind appearing both in `Modality.Static` AND in the
    /// migration team's pickup channel under `AllRemaining`).
    | OverlappingEmitterCoverage of SsKey * emitters: string list
    /// NM-45 — `Lifecycle.netDiff` / `Episode.netSchemaDiff` fold
    /// `CatalogDiff.compose` (the partial groupoid `⊕`) across an evolution
    /// chain. `compose` returns `None` — fail-loud, "never a silently-wrong
    /// result" — exactly when two adjacent diffs do NOT meet on the captured
    /// surface. On a well-formed monotone chain that is unreachable by
    /// construction (each edge's target IS the next edge's source). If it ever
    /// fires it is a structural violation of the chain's monotonicity, NOT an
    /// occasion to silently substitute the direct `between genesis latest`
    /// (which the fold was supposed to corroborate). This variant names that
    /// impossible state so the refusal surfaces loudly. `reason` is
    /// human-readable; never parse it.
    | NonComposableLifecycleChain of reason: string
    /// DECISIONS 2026-07-18 (#669 B-3 / EF-17; the downgrades-never-silent
    /// law joined to the board's `EmissionCompositePkFk` finding) — a
    /// reference targets a kind whose primary key is composite. The
    /// `Reference` IR carries one source column, so the emitted foreign key
    /// would reference only the target's first key column — SQL Server
    /// rejects that at deploy (`Msg 1776`), and before this refusal the
    /// publish truncated the key silently and exited clean. The publish now
    /// refuses unless the operator overlay drops the reference
    /// (`overlay.DropFk` — the board ruling's second arm). Carries the
    /// owner kind's name, the reference's name, the target kind's name,
    /// and the target key's column count.
    | CompositeKeyReferenceRefused of owner: string * reference: string * target: string * keyColumns: int
    /// DECISIONS 2026-07-18 (#669 EF-23; the fail-loudly ruling joined to
    /// the board's `EmissionTemporalDropped` finding) — a kind carries
    /// `ModalityMark.Temporal` (a system-versioned table), and the
    /// emission cannot yet render its period columns' GENERATED ALWAYS
    /// clauses — the emitted DDL would not deploy, and before this
    /// refusal the system-versioning silently vanished from the target.
    /// The publish refuses; carrying the temporal estate over remains
    /// the named backlog item. Carries the kind's name.
    | TemporalKindRefused of kind: string
    /// DECISIONS 2026-07-18 (#669 EF-20; family 4e joined to the board's
    /// `EmissionTriggerUnrewritten` finding) — a trigger definition did
    /// not parse, so its physical→logical identifier rewrite cannot run
    /// and the published body would target tables that do not exist in
    /// the renamed estate. The publish refuses rather than shipping the
    /// unrewritten body. Carries the owning kind, the trigger, and the
    /// parser's first error.
    | TriggerUnrewrittenRefused of kind: string * trigger: string * reason: string
    /// DECISIONS 2026-07-18 (#669 M-1; the downgrades-never-silent law joined
    /// to the board's `EmissionAuthoredDefault` finding) — a column carries an
    /// authored DEFAULT whose raw literal is not a parseable value of its type
    /// (`SqlLiteral.unparsableValueReason`, the SHARED predicate). The DEFAULT
    /// deploys and then fails at the first insert that relies on it; before
    /// this refusal the publish emitted it and exited clean. The publish now
    /// refuses. Carries the owning kind, the column, and the parse reason.
    | AuthoredDefaultRefused of kind: string * column: string * reason: string
    /// DECISIONS 2026-07-18 (#669 M-8 / EF-19; joined to the board's
    /// `EmissionComputedExprIdentifiers` finding) — a computed column's
    /// expression references bracketed identifiers that resolve to no column
    /// of the kind (`Kind.unresolvedComputedIdentifiers`, the SHARED
    /// predicate), so the physical→logical rewrite cannot complete and a
    /// case-sensitive target rejects the emitted expression at deploy. The
    /// publish refuses rather than shipping the unrewritable expression.
    /// Carries the owning kind, the column, and the unresolved tokens.
    | ComputedExpressionRefused of kind: string * column: string * tokens: string


/// Per-kind output indexed by SsKey root. The smart constructor
/// `ArtifactByKind.create catalog slices` enforces strict equality
/// between the slice's keyset and `Catalog.allKinds catalog`'s SsKey
/// set: every kind appears as a key, no extra keys are tolerated.
///
/// T11 (sibling-Π commutativity per `AXIOMS.md`) becomes a structural
/// consequence of construction: any two `ArtifactByKind` values built
/// from the same Catalog have equal keysets by construction. The
/// chapter-3 cross-cutting AXIOMS amendment ("T11 amended (structural
/// type encoding)") names the codification: T11 stops being a
/// substring-search property and becomes a type theorem. The substring
/// T11 enforcement at `JsonEmitterTests.fs:96-105` and
/// `RichProfilingEndToEndTests.fs:280` retires when slice 5.6 lands.
///
/// The constructor is `private` — callers cannot bypass the smart
/// constructor's invariant. Construction goes through
/// `ArtifactByKind.create`; introspection goes through
/// `ArtifactByKind.toMap`, `tryFind`, `keys`.
type ArtifactByKind<'element> = private ArtifactByKind of Map<SsKey, 'element>

[<RequireQualifiedAccess>]
module ArtifactByKind =

    /// Smart constructor — strict equality between the slice's keyset
    /// and `Catalog.allKinds`. Returns `Ok` when both subsets are
    /// empty: `missing → KindNotProduced`; `extra → UnexpectedKind`.
    /// Per the prescope §2 strict-equal decision: the type proves
    /// the keyset, including the "no extras" half.
    let create (catalog: Catalog) (slices: Map<SsKey, 'a>)
        : Result<ArtifactByKind<'a>, EmitError> =
        // PL-4 (S56) — the required keyset is the CWT-cached
        // `Catalog.kindKeySet` (one build per catalog value, not one per
        // sibling-emitter construction).
        let required = Catalog.kindKeySet catalog
        let provided =
            slices |> Map.toSeq |> Seq.map fst |> Set.ofSeq
        let missing = Set.difference required provided
        let extra = Set.difference provided required
        match Set.toList missing, Set.toList extra with
        | [], [] -> Ok (ArtifactByKind slices)
        | k :: _, _ -> Error (KindNotProduced k)
        | [], k :: _ -> Error (UnexpectedKind k)

    /// Build the artifact by rendering every catalog kind. Captures the
    /// recurring fold every sibling Π hand-rolled: walk `Catalog.allKinds`,
    /// key each kind's rendered element by its `SsKey`, and pass the
    /// resulting `Map` through the `create` smart constructor. The total
    /// keyset is `Catalog.allKinds`'s SsKey set by construction, so the
    /// strict-equality contract holds with no missing/extra keys; the
    /// `Result` therefore only ever surfaces a `RenderFailed`-class error
    /// if `render` itself fails (it does not here — `render` is total).
    ///
    /// Concept-shaped: this IS the per-kind projection of a Catalog into
    /// an `ArtifactByKind`, the artifact's defining construction. Use it
    /// wherever a sibling emitter produces exactly one element per kind.
    let perKind (catalog: Catalog) (render: Kind -> 'element)
        : Result<ArtifactByKind<'element>, EmitError> =
        Catalog.allKinds catalog
        |> List.map (fun k -> k.SsKey, render k)
        |> Map.ofList
        |> create catalog

    /// `perKind` with per-kind bench instrumentation. The `label` scopes
    /// each kind's render (drop-in for `Bench.iterMap`) so emitters that
    /// time per-kind work keep their existing observability while sharing
    /// the projection. Output is identical to `perKind`; only the bench
    /// table differs (each kind's render is timed under `label`).
    let perKindBenched (label: string) (catalog: Catalog) (render: Kind -> 'element)
        : Result<ArtifactByKind<'element>, EmitError> =
        Catalog.allKinds catalog
        |> Bench.iterMap label (fun k -> k.SsKey, render k)
        |> Map.ofList
        |> create catalog

    /// Key-preserving value rewrite (PL-4/S56): `Map.map` can neither add
    /// nor remove keys, so the PROVEN keyset carries over without a second
    /// validation pass — the smart-constructor invariant holds by
    /// construction. The rewrite-then-`create` shape this replaces
    /// re-derived the whole keyset (and carried an unreachable error arm)
    /// per rewrite.
    let mapValues (f: SsKey -> 'a -> 'b) (ArtifactByKind m) : ArtifactByKind<'b> =
        ArtifactByKind (Map.map f m)

    /// Project the underlying `Map<SsKey, 'a>`. Read-only; callers
    /// must not reconstruct an `ArtifactByKind` from this — the smart
    /// constructor is the only path that re-validates.
    let toMap (ArtifactByKind m) : Map<SsKey, 'a> = m

    /// Lookup by SsKey. `None` if the key is absent.
    let tryFind (key: SsKey) (a: ArtifactByKind<'a>) : 'a option =
        Map.tryFind key (toMap a)

    /// The SsKey set of the artifact. Equal to
    /// `Catalog.allKinds catalog |> List.map (_.SsKey) |> Set.ofList`
    /// by construction (T11 structural).
    let keys (a: ArtifactByKind<'a>) : Set<SsKey> =
        toMap a |> Map.toSeq |> Seq.map fst |> Set.ofSeq
