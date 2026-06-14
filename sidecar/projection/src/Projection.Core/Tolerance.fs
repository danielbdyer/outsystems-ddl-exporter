namespace Projection.Core

/// One named, empirically-grounded divergence absorbed by the canary's
/// equivalence-up-to-quotient comparator. Per `DECISIONS 2026-05-22 —
/// R6: Split-brain governance rule`, every observed divergence
/// between source-deploy and target-deploy halves of the canary's
/// round-trip property either matches a `ToleratedDivergence` or
/// fails the canary; the same surface absorbs V1↔V2 emit
/// differences during the dual-track cutover window.
///
/// **Empirical cut at chapter 4.1.A (slice α).** The STAGING.md S0.E
/// proposal sketched ~13 candidate flag names; the variants below
/// are the subset with concrete canary or emitter evidence today.
/// Per `DECISIONS 2026-05-07` — IR grows under evidence, not
/// speculation. New variants land when canary or trunk evidence
/// demands them; the closed-DU expansion empirical test (`Tolerance
/// .allKnown`) catches the omission at compile time.
///
/// **Pillar 8 four-question domain-naming analysis.** Each variant
/// names *what* the divergence IS (concept-shaped), not what the
/// comparator DOES with it (action-shaped). The accepted pattern is
/// `<Subject><Disposition>` (e.g., `HeaderCommentsOmitted` — the
/// header comments ARE omitted; the divergence IS that omission).
///
/// **`@ladder` machine tag (D1 — the self-verification meta-cell).**
/// Each variant's doc block ends with a machine-readable
/// `@ladder <VariantName> <Axis> <Disposition>` line that
/// `scripts/matrix-status.sh` parses to report each round-trip axis's
/// faithfulness rung in `NORTH_STAR.matrix.generated.md`. `Axis` is one
/// of the five round-trip axes (Schema / Data / Identity / Time /
/// Decision); `Disposition` is `OpenGap` (a closeable fidelity debt that
/// caps the axis at L2-partial — e.g. `IndexOptionsUnreflected`, retired when
/// the round-trip preserves it) or `AcceptedFaithful` (a representation-
/// only equivalence or an erasure covered by a separate witness, which
/// does not reduce faithfulness). The honesty mechanism: retiring a
/// variant deletes its tag, so the generator auto-flips the axis — no one
/// can hand-mark an axis faithful while its open tolerance still exists.
/// The generator FAILS if any live variant (per `name`) lacks a tag, so a
/// new variant cannot land untagged.
[<RequireQualifiedAccess>]
type ToleratedDivergence =
    /// V2 emits without the `/* Source: ... */` header block per
    /// `SsdtDdlEmitter.fs:94` ("Tolerance.IgnoreHeaderComments =
    /// true initially, V2 omits"). The omission is deliberate; an
    /// `EmissionPolicyPass` may surface an equivalent header in a
    /// later chapter, retiring this variant.
    /// @ladder HeaderCommentsOmitted Schema AcceptedFaithful
    | HeaderCommentsOmitted

    /// Cross-module FKs split into a `Scripts/PostDeploy/Cross
    /// ModuleForeignKeys.sql` artifact rather than inlined in each
    /// table's `CREATE TABLE` per `CHAPTER_4_PRESCOPE_SSDT_DDL
    /// _EMITTER.md:104`. V2 follows V1's inline pattern initially;
    /// if integration tests surface a deploy-order failure, V2
    /// flips to the PostDeploy split. Layout differs (file count +
    /// content placement) but `PhysicalSchema` equivalence holds
    /// (FKs deploy either way).
    /// @ladder PostDeployForeignKeysSplit Schema AcceptedFaithful
    | PostDeployForeignKeysSplit

    /// E1 (debrief G3) — non-PK index *structure* (owner + name +
    /// uniqueness + ordered key columns) IS now reflected in
    /// `PhysicalSchema.Indexes` and compared on the round-trip (retiring the
    /// prior `IndexOptionsUnreflected`, which said indexes were invisible
    /// entirely). What remains unreflected is the index *options*: the
    /// filter predicate (filtered indexes), INCLUDE columns (covering
    /// indexes), and the storage options (FILLFACTOR / PAD_INDEX / lock
    /// flags / DATA_COMPRESSION). `ReadSide.readIndexes` recovers none of
    /// these (it excludes `is_included_column` and reads no option columns),
    /// so they are symmetric-but-lost on both halves of the canary. Named
    /// here so the residual is *closed* (documented), not silent. Retiring
    /// it: extend `readIndexes` to recover the options + widen
    /// `PhysicalIndex` + ensure V2 emit preserves them round-trip.
    /// @ladder IndexOptionsUnreflected Schema OpenGap
    | IndexOptionsUnreflected

    /// NM-16 — `CatalogDiff.between` compares kinds by NAME and descends
    /// only into attributes / references / indexes / sequences; a kind's
    /// `Triggers` are NOT a diff channel. A changed / added / removed
    /// trigger produces `norm d = 0` ("idempotent redeploy") and
    /// `migrate A B` emits nothing — yet the canary's `PhysicalSchema.diff`
    /// surface CAN see trigger drift, so the two surfaces disagree on
    /// "what is a change." Named here so the kind-level trigger erasure in
    /// the `CatalogDiff` algebra is *closed* (witnessed), not silent —
    /// satisfying "nothing lost in silence" (the LIGHT route, not a full
    /// `KindFacet` diff channel). Retiring it: add a kind-trigger diff
    /// channel to `CatalogDiff.between` (mirroring the attribute facet
    /// descent) with `applyDiff` patches + a fixture.
    /// @ladder KindTriggersUnreflectedInDiff Schema OpenGap
    | KindTriggersUnreflectedInDiff

    /// NM-16 — a kind's `ColumnChecks` (table-level CHECK constraints) are
    /// not a `CatalogDiff.between` channel; a changed / added / removed
    /// CHECK produces `norm d = 0` and `migrate` emits nothing, while the
    /// physical canary can observe the change. Named here so the erasure
    /// is *closed* (witnessed), not silent. Retiring it: add a kind-CHECK
    /// diff channel with `applyDiff` patches + a fixture.
    /// @ladder KindChecksUnreflectedInDiff Schema OpenGap
    | KindChecksUnreflectedInDiff

    /// NM-16 — a kind's `Modality` (the static-vs-dynamic / population
    /// modality mark) is not a `CatalogDiff.between` channel; a modality
    /// flip produces `norm d = 0` and `migrate` emits nothing. Named here
    /// so the erasure is *closed* (witnessed), not silent. Retiring it: add
    /// a kind-modality diff channel with `applyDiff` patches + a fixture.
    /// @ladder KindModalityUnreflectedInDiff Schema OpenGap
    | KindModalityUnreflectedInDiff

    /// NM-16 — a kind's `IsActive` activation flag is not a
    /// `CatalogDiff.between` channel; activating / deactivating a kind
    /// produces `norm d = 0` and `migrate` emits nothing. Named here so the
    /// erasure is *closed* (witnessed), not silent. Retiring it: add a
    /// kind-activation diff channel with `applyDiff` patches + a fixture.
    /// @ladder KindActivationUnreflectedInDiff Schema OpenGap
    | KindActivationUnreflectedInDiff

    /// Static-entity populations (INSERT statements) are absent
    /// from `PhysicalSchema`'s comparison surface (same docstring).
    /// The data-plane axis is covered by `PhysicalSchema.Rows`
    /// when the canary opts into the data round-trip;
    /// chapter 4.1.B will retire this variant for population kinds.
    /// @ladder StaticPopulationsUnreflected Data AcceptedFaithful
    | StaticPopulationsUnreflected

    /// 6.A.4 — a genuine empty-string `Text` value and SQL `NULL` are
    /// **indistinguishable** in the transfer IR: `ReadSide.formatRawValue`
    /// maps both `DBNull` and `""` to the raw `""` (so even the canary's
    /// row-hash cannot tell them apart), and `Bulk.parseRaw` maps `""` back
    /// to `DBNull`. The net rule, made explicit: **an empty-string `Text`
    /// value normalizes to `NULL` on transfer-write.** Named here so the
    /// erasure is *closed* (documented + witnessed), not silent. Retiring it
    /// requires a read-side sentinel that distinguishes absent from
    /// empty-string end-to-end (an IR-grows-under-evidence slice; no fixture
    /// forces faithful empty-string preservation today). NB: for a NOT-NULL
    /// `Text` column an empty source value would instead fail the load —
    /// that schema-vs-data compatibility check is 6.B.1, not this tolerance.
    ///
    /// NM-18 — SCOPE: the empty raw string is the V2 IR's *universal* NULL
    /// sentinel (`SqlLiteral.ofRaw "" = NullLit` for EVERY `PrimitiveType`, by
    /// the `RawValueCodec` single-source-of-truth convention), so this erasure
    /// also covers a stored empty `TextLit ""` (`N''`) and a zero-length
    /// `BinaryLit` (`0x`) — not just the empty-string-vs-NULL ambiguity the name
    /// foregrounds. Retiring the sentinel (a faithful empty/zero-length form)
    /// is the same IR-grows-under-evidence slice.
    /// @ladder EmptyTextNormalizedToNull Data AcceptedFaithful
    | EmptyTextNormalizedToNull

    /// AC-D6 — a `char(n)` / `nchar(n)` column's stored value is ANSI
    /// **trailing-blank-padded** to its declared width, so `'foo  '` and
    /// `'foo'` are the **same stored value** under SQL Server's comparison
    /// semantics (the ANSI `<>` operator pads the shorter operand before
    /// comparing — `'foo  ' <> 'foo'` is `FALSE`). The CDC change-detection
    /// predicate (`ScriptDomBuild.perColumnChangeDetection`) compares
    /// `Target.[c] <> Source.[c]` **column-to-column** (both operands are
    /// the stored typed values, not rendered literals), so a representation-
    /// only padding difference does **not** fire CDC. Named here so the
    /// equivalence is *closed* (documented + witnessed at the literal/
    /// predicate level), not silently assumed. This is a **representation-
    /// only** tolerance: it absorbs no data difference — only the textual
    /// shape of an otherwise-equal value. Retiring it is not anticipated;
    /// it is a property of SQL Server's ANSI char semantics, not a V2 gap.
    /// @ladder CharAnsiPaddingTolerated Data AcceptedFaithful
    | CharAnsiPaddingTolerated

    /// NM-28 — a foreign key whose TARGET kind has a **composite** primary key
    /// is reflected on only its FIRST leg. `PhysicalSchema.toPhysicalForeignKeys`
    /// pairs the (single) FK source column against the target's first PK column;
    /// the second-and-later legs are not emitted, so the canary's
    /// `PhysicalSchema.ForeignKeys` set cannot observe drift in them. The cause
    /// is structural, not a coding slip: V2's `Reference` IR is **single-column
    /// per chapter 5.0** (`MetadataSnapshotRunner` `#FkColumns` note — the
    /// multi-column source columns exist in `sys.foreign_key_columns` but have no
    /// IR carrier yet), so there is no source column to pair the second target PK
    /// leg against. Named here so the residual is *closed* (documented +
    /// witnessed at construction), not silent. Retiring it: lift a composite-FK
    /// IR (the deferred chapter-5.0 refinement) so a `Reference` carries its full
    /// ordered (source, target) column list, then emit one `PhysicalForeignKey`
    /// per leg and round-trip every leg.
    /// @ladder CompositePkFkUnreflected Schema OpenGap
    | CompositePkFkUnreflected

    /// AC-D6 — a `decimal(p,s)` / `numeric(p,s)` column's stored value is a
    /// **numeric** quantity, so `1.0` and `1.00` are the **same stored
    /// value** (scale is a display/declaration concern, not a value concern;
    /// `1.0 <> 1.00` is `FALSE` under SQL Server's numeric `<>`). The CDC
    /// change-detection predicate compares `Target.[c] <> Source.[c]`
    /// column-to-column on the stored numeric values, so a representation-
    /// only scale difference (`SqlLiteral.DecimalLit "1.0"` vs `"1.00"` —
    /// which render to *different* literal TEXT) does **not** fire CDC once
    /// both literals are stored into the same `decimal` column. Named here
    /// so the equivalence is *closed* at the literal/predicate level. A
    /// **representation-only** tolerance: it absorbs no numeric difference —
    /// only the trailing-zero scale shape. Retiring it is not anticipated;
    /// it is a property of SQL Server's numeric comparison, not a V2 gap.
    /// @ladder DecimalScaleTolerated Data AcceptedFaithful
    | DecimalScaleTolerated

    // **CommentMetadataUnreflected — RETIRED at chapter 4.1.A slice 8
    // (2026-05-17).** Column / table / index descriptions and extended
    // properties now emit as `EXEC sys.sp_addextendedproperty` calls
    // via `SsdtDdlEmitter.extendedPropertyStatements`; the IR
    // (chapter A.0' slices α + ζ) carries the data and the emitter
    // consumes it. Removed from the DU per the closed-DU empirical-
    // test discipline — adding the emission retires the variant.
    // `PhysicalSchema.fs` may extend its comparison surface to cover
    // extended properties as a separate forward signal; today the
    // canary's `PhysicalSchema` diff treats descriptions as
    // out-of-comparison, but the EMITTER side is no longer silent.

/// Operations on individual `ToleratedDivergence` variants. The
/// `allKnown` accessor is the closed-DU expansion empirical-test
/// fulcrum: when a new variant lands, `allKnown` must extend, OR
/// the closed-DU coverage test fails.
[<RequireQualifiedAccess>]
module ToleratedDivergence =

    /// Closed-DU coverage function. F# exhaustiveness checks under
    /// `TreatWarningsAsErrors=true` ensure adding a variant fires
    /// at THIS site, prompting the author to extend `allKnown`.
    /// Per `DECISIONS 2026-05-13 — Closed-DU expansion: empirical
    /// confirmation`. The function is called once at module load
    /// (via `allKnown`'s construction path) so its compile-time
    /// exhaustiveness force survives the unused-binding warning.
    let private coverage : ToleratedDivergence -> ToleratedDivergence =
        function
        | ToleratedDivergence.HeaderCommentsOmitted          -> ToleratedDivergence.HeaderCommentsOmitted
        | ToleratedDivergence.PostDeployForeignKeysSplit     -> ToleratedDivergence.PostDeployForeignKeysSplit
        | ToleratedDivergence.IndexOptionsUnreflected             -> ToleratedDivergence.IndexOptionsUnreflected
        | ToleratedDivergence.KindTriggersUnreflectedInDiff  -> ToleratedDivergence.KindTriggersUnreflectedInDiff
        | ToleratedDivergence.KindChecksUnreflectedInDiff    -> ToleratedDivergence.KindChecksUnreflectedInDiff
        | ToleratedDivergence.KindModalityUnreflectedInDiff  -> ToleratedDivergence.KindModalityUnreflectedInDiff
        | ToleratedDivergence.KindActivationUnreflectedInDiff -> ToleratedDivergence.KindActivationUnreflectedInDiff
        | ToleratedDivergence.StaticPopulationsUnreflected   -> ToleratedDivergence.StaticPopulationsUnreflected
        | ToleratedDivergence.EmptyTextNormalizedToNull      -> ToleratedDivergence.EmptyTextNormalizedToNull
        | ToleratedDivergence.CompositePkFkUnreflected       -> ToleratedDivergence.CompositePkFkUnreflected
        | ToleratedDivergence.CharAnsiPaddingTolerated       -> ToleratedDivergence.CharAnsiPaddingTolerated
        | ToleratedDivergence.DecimalScaleTolerated          -> ToleratedDivergence.DecimalScaleTolerated

    /// Every empirically-grounded `ToleratedDivergence` variant.
    /// The closed-DU coverage test asserts this set has the same
    /// cardinality as the variant count; if a future variant is
    /// added without extending `allKnown`, the test fails.
    let allKnown : Set<ToleratedDivergence> =
        // Round-trip every known variant through the coverage
        // function so the compile-time exhaustiveness force binds
        // to module load. Adding a variant without a `coverage`
        // arm fires an FS0025 incomplete-match error under
        // TreatWarningsAsErrors; adding it without extending the
        // list here is caught by the runtime coverage test in
        // TolerancePropertyTests.
        Set.ofList
            [
                coverage ToleratedDivergence.HeaderCommentsOmitted
                coverage ToleratedDivergence.PostDeployForeignKeysSplit
                coverage ToleratedDivergence.IndexOptionsUnreflected
                coverage ToleratedDivergence.KindTriggersUnreflectedInDiff
                coverage ToleratedDivergence.KindChecksUnreflectedInDiff
                coverage ToleratedDivergence.KindModalityUnreflectedInDiff
                coverage ToleratedDivergence.KindActivationUnreflectedInDiff
                coverage ToleratedDivergence.StaticPopulationsUnreflected
                coverage ToleratedDivergence.EmptyTextNormalizedToNull
                coverage ToleratedDivergence.CompositePkFkUnreflected
                coverage ToleratedDivergence.CharAnsiPaddingTolerated
                coverage ToleratedDivergence.DecimalScaleTolerated
            ]

    /// Canonical string name for a divergence — the operator-facing token a
    /// per-environment config uses (Wave-3 slice 3.4). Exhaustive match: a
    /// new variant fires FS0025 here under `TreatWarningsAsErrors`, forcing
    /// the author to give it a config token. `name` is the single source of
    /// truth; `tryParse` is its inverse.
    let name (d: ToleratedDivergence) : string =
        match d with
        | ToleratedDivergence.HeaderCommentsOmitted        -> "HeaderCommentsOmitted"
        | ToleratedDivergence.PostDeployForeignKeysSplit   -> "PostDeployForeignKeysSplit"
        | ToleratedDivergence.IndexOptionsUnreflected           -> "IndexOptionsUnreflected"
        | ToleratedDivergence.KindTriggersUnreflectedInDiff   -> "KindTriggersUnreflectedInDiff"
        | ToleratedDivergence.KindChecksUnreflectedInDiff     -> "KindChecksUnreflectedInDiff"
        | ToleratedDivergence.KindModalityUnreflectedInDiff   -> "KindModalityUnreflectedInDiff"
        | ToleratedDivergence.KindActivationUnreflectedInDiff -> "KindActivationUnreflectedInDiff"
        | ToleratedDivergence.StaticPopulationsUnreflected -> "StaticPopulationsUnreflected"
        | ToleratedDivergence.EmptyTextNormalizedToNull    -> "EmptyTextNormalizedToNull"
        | ToleratedDivergence.CompositePkFkUnreflected     -> "CompositePkFkUnreflected"
        | ToleratedDivergence.CharAnsiPaddingTolerated     -> "CharAnsiPaddingTolerated"
        | ToleratedDivergence.DecimalScaleTolerated        -> "DecimalScaleTolerated"

    /// Parse a config token to its divergence, or `None` for an unrecognized
    /// token. Derived from `name` so the round-trip `name >> tryParse` is the
    /// identity on every known variant (asserted in `ToleranceTests`).
    let tryParse (token: string) : ToleratedDivergence option =
        allKnown |> Set.toList |> List.tryFind (fun d -> name d = token)

/// The equivalence-class definition for the canary's V1≈V2 and
/// source-deploy≈target-deploy comparisons. A `Tolerance` is the
/// SET of accepted divergences; membership says "this divergence
/// is expected and does not block." Per `DECISIONS 2026-05-22 — R6`:
/// the canary fails iff a divergence surfaces that is NOT in the
/// Tolerance.
///
/// **Per-environment quotient flip.** Each cutover environment
/// carries its own `Tolerance` value; DEV may accept several
/// divergences while PROD insists on `Tolerance.strict`. The
/// four-environment promotion property (R4) asserts artifacts
/// pairwise-equal across env pairs modulo their named tolerances.
///
/// **Smart-constructor encapsulation.** The underlying `Set` is
/// private; consumers go through the named operations (`with
/// Divergence`, `tolerates`, `divergences`). Per the AXIOMS.md
/// operational principle (structural-commitment-via-construction
/// -validation), every value carries its own truth.
/// Failure of `Tolerance.parse` — an environment config named a divergence
/// token V2 does not recognize (Wave-3 slice 3.4). Carries the offending
/// token so the operator sees exactly which name to fix. The **fail-closed**
/// safety property: an unrecognized token is an `Error`, never a silently-
/// ignored entry — silently widening (or narrowing) tolerance from a typo'd
/// config would corrupt the canary's R6 gate semantics.
type ToleranceError =
    | UnknownDivergence of token: string

type Tolerance = private Tolerance of Set<ToleratedDivergence>

/// Operations on `Tolerance` values. The two named constructors
/// (`strict`, `permissive`) bracket the spectrum; `withDivergence`
/// monotonically extends a tolerance set.
[<RequireQualifiedAccess>]
module Tolerance =

    /// No tolerated divergences. The strictest equivalence: source
    /// and target must match on every axis the comparator
    /// measures. The cutover ladder targets `strict` at the PROD
    /// environment per `DECISIONS 2026-05-22 — T-30 / T-15`.
    let strict : Tolerance = Tolerance Set.empty

    /// Every empirically-known divergence accepted. The most
    /// permissive equivalence; useful for the dual-track period
    /// while V2's IR matures. Cutover-ladder tightening reduces
    /// toward `strict` as new variants retire.
    let permissive : Tolerance = Tolerance ToleratedDivergence.allKnown

    /// Construct from an explicit set. Use when a per-environment
    /// configuration carries its own subset (e.g., DEV accepts
    /// HeaderCommentsOmitted + IndexOptionsUnreflected; STAGING accepts
    /// only IndexOptionsUnreflected; PROD accepts none).
    let ofSet (divergences: Set<ToleratedDivergence>) : Tolerance =
        Tolerance divergences

    /// Add one divergence to the tolerance set. Idempotent (set
    /// semantics). Returns a new `Tolerance` (immutable).
    let withDivergence (d: ToleratedDivergence) (Tolerance s) : Tolerance =
        Tolerance (Set.add d s)

    /// True iff the divergence is tolerated (i.e., does not block
    /// the canary's equivalence assertion).
    let tolerates (d: ToleratedDivergence) (Tolerance s) : bool =
        Set.contains d s

    /// Accessor: the underlying tolerated-divergence set.
    let divergences (Tolerance s) : Set<ToleratedDivergence> = s

    /// True iff zero divergences are tolerated. Equivalent to
    /// `t = Tolerance.strict` under structural equality.
    let isStrict (Tolerance s) : bool = Set.isEmpty s

    /// Parse a per-environment config — a list of divergence tokens — into a
    /// `Tolerance`, **fail-closed** (Wave-3 slice 3.4). Every non-blank token
    /// must validate against `ToleratedDivergence.allKnown`; the first
    /// unrecognized token short-circuits to `Error (UnknownDivergence token)`.
    /// Blank / whitespace-only tokens are skipped (a trailing comma in config
    /// is not an error and does not widen tolerance). The empty list parses to
    /// `strict` — the safe default. This is the operator decision surface R6's
    /// flip gate reads; an unknown name silently widening would corrupt the
    /// canary's equivalence semantics, so it MUST fail rather than default.
    let parse (tokens: string list) : Result<Tolerance, ToleranceError> =
        let rec loop (acc: Set<ToleratedDivergence>) (remaining: string list) =
            match remaining with
            | [] -> Ok (ofSet acc)
            | raw :: rest ->
                let token = raw.Trim()
                if token = "" then loop acc rest
                else
                    match ToleratedDivergence.tryParse token with
                    | Some d -> loop (Set.add d acc) rest
                    | None -> Error (UnknownDivergence token)
        loop Set.empty tokens
