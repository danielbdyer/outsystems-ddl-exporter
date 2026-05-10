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
[<RequireQualifiedAccess>]
type ToleratedDivergence =
    /// V2 emits without the `/* Source: ... */` header block per
    /// `SsdtDdlEmitter.fs:94` ("Tolerance.IgnoreHeaderComments =
    /// true initially, V2 omits"). The omission is deliberate; an
    /// `EmissionPolicyPass` may surface an equivalent header in a
    /// later chapter, retiring this variant.
    | HeaderCommentsOmitted

    /// Cross-module FKs split into a `Scripts/PostDeploy/Cross
    /// ModuleForeignKeys.sql` artifact rather than inlined in each
    /// table's `CREATE TABLE` per `CHAPTER_4_PRESCOPE_SSDT_DDL
    /// _EMITTER.md:104`. V2 follows V1's inline pattern initially;
    /// if integration tests surface a deploy-order failure, V2
    /// flips to the PostDeploy split. Layout differs (file count +
    /// content placement) but `PhysicalSchema` equivalence holds
    /// (FKs deploy either way).
    | PostDeployForeignKeysSplit

    /// Non-PK indexes are not reflected in `PhysicalSchema`'s
    /// comparison surface per the docstring at `PhysicalSchema.fs:
    /// 44` ("What's NOT compared. ... Indexes (non-PK), ..."). A
    /// future `PhysicalSchema.Indexes` set will retire this
    /// variant when ReadSide reconstructs them and V2 emit
    /// preserves them round-trip.
    | IndexesUnreflected

    /// Static-entity populations (INSERT statements) are absent
    /// from `PhysicalSchema`'s comparison surface (same docstring).
    /// The data-plane axis is covered by `PhysicalSchema.Rows` /
    /// `RowDigests` when the canary opts into the data round-trip;
    /// chapter 4.1.B will retire this variant for population kinds.
    | StaticPopulationsUnreflected

    /// Column / table descriptions and extended properties are
    /// absent from `PhysicalSchema`'s comparison surface (same
    /// docstring at `PhysicalSchema.fs:44`). Chapter 4.1.A slice 8
    /// (extended properties; gated on chapter 3.2 SnapshotRowsets)
    /// will retire this variant when the IR carries them and the
    /// emitter emits `sp_addextendedproperty` calls.
    | CommentMetadataUnreflected

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
        | ToleratedDivergence.IndexesUnreflected             -> ToleratedDivergence.IndexesUnreflected
        | ToleratedDivergence.StaticPopulationsUnreflected   -> ToleratedDivergence.StaticPopulationsUnreflected
        | ToleratedDivergence.CommentMetadataUnreflected     -> ToleratedDivergence.CommentMetadataUnreflected

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
                coverage ToleratedDivergence.IndexesUnreflected
                coverage ToleratedDivergence.StaticPopulationsUnreflected
                coverage ToleratedDivergence.CommentMetadataUnreflected
            ]

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
    /// HeaderCommentsOmitted + IndexesUnreflected; STAGING accepts
    /// only IndexesUnreflected; PROD accepts none).
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
