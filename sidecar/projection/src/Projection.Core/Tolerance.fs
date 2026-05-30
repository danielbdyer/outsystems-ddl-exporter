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
        | ToleratedDivergence.IndexesUnreflected             -> ToleratedDivergence.IndexesUnreflected
        | ToleratedDivergence.StaticPopulationsUnreflected   -> ToleratedDivergence.StaticPopulationsUnreflected

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
        | ToleratedDivergence.IndexesUnreflected           -> "IndexesUnreflected"
        | ToleratedDivergence.StaticPopulationsUnreflected -> "StaticPopulationsUnreflected"

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
