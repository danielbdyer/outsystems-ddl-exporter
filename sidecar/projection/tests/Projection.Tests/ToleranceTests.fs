module Projection.Tests.ToleranceTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core

// ---------------------------------------------------------------------------
// M4 slice α — Tolerance taxonomy typed DU + Set encoding.
//
// Per `DECISIONS 2026-05-22 — R6: Split-brain governance rule`, the
// Tolerance is the equivalence-class definition the canary uses to
// absorb empirically-grounded divergences between source and target
// halves of the round-trip property (and between V1 and V2 emit
// during the dual-track cutover window).
//
// Tests cover three contracts:
//   1. Smart-constructor invariants (`strict` / `permissive` / `with
//      Divergence` / `tolerates` / `divergences` / `isStrict`).
//   2. Closed-DU expansion empirical-test discipline — `allKnown` is
//      complete (its cardinality matches the variant count).
//   3. Algebraic-monotonicity properties (FsCheck): `withDivergence`
//      is monotonic; `tolerates` agrees with set membership.
// ---------------------------------------------------------------------------

/// FsCheck arbitrary instance for `ToleratedDivergence`. Generates
/// uniformly across the closed-DU; if a new variant lands, this
/// generator must extend (which the closed-DU coverage discipline
/// enforces at compile time via the `match` below).
type ToleratedDivergenceGen =
    static member Arbitrary : Arbitrary<ToleratedDivergence> =
        Gen.elements
            [
                ToleratedDivergence.HeaderCommentsOmitted
                ToleratedDivergence.PostDeployForeignKeysSplit
                ToleratedDivergence.IndexOptionsUnreflected
                ToleratedDivergence.StaticPopulationsUnreflected
                ToleratedDivergence.EmptyTextNormalizedToNull
                ToleratedDivergence.CompositePkFkUnreflected
                ToleratedDivergence.CharAnsiPaddingTolerated
                ToleratedDivergence.DecimalScaleTolerated
                ToleratedDivergence.FkTrustNotRestoredOnBulkLoad
                ToleratedDivergence.TriggerBodyUnparsedDropped
                ToleratedDivergence.BooleanCanonicalizationTolerated
                ToleratedDivergence.DateTimeTickPrecisionTolerated
                ToleratedDivergence.IntegerWidthNormalized
            ]
        |> Arb.fromGen

[<Fact>]
let ``Tolerance.strict has zero divergences`` () =
    Assert.True (Tolerance.isStrict Tolerance.strict)
    Assert.Equal (0, Set.count (Tolerance.divergences Tolerance.strict))

[<Fact>]
let ``Tolerance.permissive accepts every empirically-known divergence`` () =
    let permissive = Tolerance.divergences Tolerance.permissive
    Assert.Equal<Set<ToleratedDivergence>> (ToleratedDivergence.allKnown, permissive)

[<Fact>]
let ``Tolerance.permissive is not strict`` () =
    Assert.False (Tolerance.isStrict Tolerance.permissive)

[<Fact>]
let ``Closed-DU coverage: ToleratedDivergence.allKnown contains ten variants (option C added FkTrustNotRestoredOnBulkLoad)`` () =
    // Per the closed-DU expansion empirical-test discipline (`DECISIONS
    // 2026-05-13`): when a new ToleratedDivergence variant lands, this
    // count assertion fires until allKnown is extended. The companion
    // compile-time forcing function (`coverage` in Tolerance.fs) catches
    // the omission earlier — under TreatWarningsAsErrors, an unmatched
    // variant fires FS0025. This runtime test is the second-line guard.
    //
    // **Chapter 4.1.A slice 8 retirement (2026-05-17).** Was 5
    // variants; CommentMetadataUnreflected retired when
    // SsdtDdlEmitter.extendedPropertyStatements began emitting
    // sp_addextendedproperty calls. **6.A.4 (2026-06-02):** back to 5 —
    // EmptyTextNormalizedToNull names the empty-string-Text→NULL transfer
    // normalization (closed, not silent). **AC-D6 (NEITHER→HELD):** 7 —
    // CharAnsiPaddingTolerated + DecimalScaleTolerated name the
    // representation-only differences ('foo  '≈'foo', 1.0≈1.00) that do
    // NOT fire CDC under SQL Server's ANSI-pad / numeric comparison.
    // **NM-16 (2026-06-13):** 11 — KindTriggersUnreflectedInDiff /
    // KindChecksUnreflectedInDiff / KindModalityUnreflectedInDiff /
    // KindActivationUnreflectedInDiff name the kind-level facets the
    // `CatalogDiff.between` algebra erases (a changed trigger / CHECK /
    // modality / IsActive yields norm=0, "idempotent redeploy", emits
    // nothing) — the SILENT erasure is now WITNESSED.
    // **NM-28 (2026-06-14):** 12 — CompositePkFkUnreflected names the
    // composite-target-PK FK whose second-and-later legs the single-column
    // Reference IR cannot reflect (only the first leg round-trips).
    // **NM-17 (2026-06-14):** back to 8 — the four NM-16 kind-facet
    // diff-erasure tolerances are RETIRED (now a real `KindFacet` diff
    // channel in `CatalogDiff`, not an erased tolerance).
    // **M1′ + M2 (THE VECTOR, Wave 0, 2026-06-15):** 11 — the two Decision-axis
    // tolerances `FkTrustUnreflected` + `UniquePromotionUnreflected` name the
    // FK-trust / unique-promotion sub-axes the round-trip comparator cannot yet
    // observe (over-claim correction — Decision drops to ◑ L2-partial; both
    // auto-retire when M1 lands), and `TriggerBodyUnparsedDropped` names the
    // formerly-silent CreateTrigger text-render drop (Schema OpenGap).
    // **M1 (THE VECTOR, Wave 1, 2026-06-15):** back to 9 — `FkTrustUnreflected`
    // and `UniquePromotionUnreflected` are RETIRED. `PhysicalForeignKey
    // .IsTrusted` + the overlay-aware `PhysicalSchema.ofCatalogWith` route the
    // FK-trust / unique-promotion decisions through the general comparator
    // (witnessed Docker-real by the M1 decision-readback test), so the Decision
    // axis flips back from ◑ L2-partial to ✅ faithful.
    // **Option C (Wave 1 follow-on, 2026-06-15):** 10 — `FkTrustNotRestoredOnBulkLoad`
    // (Decision, AcceptedFaithful) names the transfer-leg OPT-OUT: `Transfer.Execute`
    // re-trusts the sink's FKs by default, so this fires only when the operator
    // sets `WriteOptions.RetrustForeignKeys = false`. AcceptedFaithful, so the
    // Decision axis stays ✅ and the open-gap count stays 3.
    // **T17/B4b (2026-07-15):** 13 — the row-fidelity comparator's three
    // canonical-form erasures (BooleanCanonicalizationTolerated /
    // DateTimeTickPrecisionTolerated / IntegerWidthNormalized), all Data
    // AcceptedFaithful, so the open-gap count stays 3.
    Assert.Equal (13, Set.count ToleratedDivergence.allKnown)

[<Fact>]
let ``Tolerance.ofSet round-trips through divergences`` () =
    let s = Set.ofList [ ToleratedDivergence.HeaderCommentsOmitted; ToleratedDivergence.IndexOptionsUnreflected ]
    let t = Tolerance.ofSet s
    Assert.Equal<Set<ToleratedDivergence>> (s, Tolerance.divergences t)

[<Fact>]
let ``Tolerance.withDivergence on strict yields a singleton tolerance`` () =
    let t = Tolerance.strict |> Tolerance.withDivergence ToleratedDivergence.HeaderCommentsOmitted
    Assert.True (Tolerance.tolerates ToleratedDivergence.HeaderCommentsOmitted t)
    Assert.False (Tolerance.tolerates ToleratedDivergence.IndexOptionsUnreflected t)
    Assert.Equal (1, Set.count (Tolerance.divergences t))

[<Fact>]
let ``Tolerance.withDivergence is idempotent (set semantics)`` () =
    let once =
        Tolerance.strict
        |> Tolerance.withDivergence ToleratedDivergence.HeaderCommentsOmitted
    let twice =
        once |> Tolerance.withDivergence ToleratedDivergence.HeaderCommentsOmitted
    Assert.Equal<Set<ToleratedDivergence>>
        (Tolerance.divergences once, Tolerance.divergences twice)

[<Property(Arbitrary = [| typeof<ToleratedDivergenceGen> |])>]
let ``Property: Tolerance.withDivergence is monotonic (adding never removes)`` (d: ToleratedDivergence) (s: Set<ToleratedDivergence>) =
    let before = Tolerance.ofSet s
    let after = Tolerance.withDivergence d before
    // Every divergence tolerated before is still tolerated after.
    s
    |> Set.forall (fun existing -> Tolerance.tolerates existing after)

[<Property(Arbitrary = [| typeof<ToleratedDivergenceGen> |])>]
let ``Property: Tolerance.tolerates agrees with set membership`` (d: ToleratedDivergence) (s: Set<ToleratedDivergence>) =
    let t = Tolerance.ofSet s
    Tolerance.tolerates d t = Set.contains d s

[<Property(Arbitrary = [| typeof<ToleratedDivergenceGen> |])>]
let ``Property: Tolerance.permissive tolerates every variant`` (d: ToleratedDivergence) =
    Tolerance.tolerates d Tolerance.permissive

[<Property(Arbitrary = [| typeof<ToleratedDivergenceGen> |])>]
let ``Property: Tolerance.strict tolerates no variant`` (d: ToleratedDivergence) =
    not (Tolerance.tolerates d Tolerance.strict)

[<Fact>]
let ``Compare<Tolerance> is inhabited (S0.A type-pattern instantiation)`` () =
    // S0.A's `Compare<'tolerance>` is the canary's comparator pattern.
    // M4 slice α makes `Tolerance` the canonical instantiation; this
    // test confirms the type plugs in. Slice β adds the actual quotient
    // operator that consumes the Tolerance to filter PhysicalSchema diffs.
    let stub : Compare<Tolerance> =
        fun _tolerance _left _right -> Diff.Pending
    Assert.NotNull (stub :> obj)

// ---------------------------------------------------------------------------
// Wave-3 slice 3.4 — per-environment Tolerance config, FAIL-CLOSED.
// `Tolerance.parse` turns a list of operator-supplied divergence tokens into a
// Tolerance; an unrecognized token is an Error (never silently ignored), so a
// typo'd config cannot widen the canary's R6 equivalence semantics.
// ---------------------------------------------------------------------------

[<Property>]
let ``3.4: every ToleratedDivergence name round-trips through tryParse`` () =
    ToleratedDivergence.allKnown
    |> Set.forall (fun d -> ToleratedDivergence.tryParse (ToleratedDivergence.name d) = Some d)

// ---------------------------------------------------------------------------
// NM-17 — the four NM-16 kind-facet diff-erasure tolerances
// (KindTriggers/Checks/Modality/Activation UnreflectedInDiff) are RETIRED:
// `CatalogDiff` now reflects modality / triggers / CHECKs / activation as a
// real `KindFacet` diff channel, so the erasure they named no longer exists.
// A retired OpenGap leaves no DU variant behind — its config token no longer
// parses and it is absent from `allKnown`.
// ---------------------------------------------------------------------------

[<Fact>]
let ``NM-17: the four NM-16 kind-facet diff-erasure tolerance tokens are retired (no longer parse)`` () =
    let retiredTokens =
        [ "KindTriggersUnreflectedInDiff"
          "KindChecksUnreflectedInDiff"
          "KindModalityUnreflectedInDiff"
          "KindActivationUnreflectedInDiff" ]
    for token in retiredTokens do
        // The token no longer maps to any variant (fail-closed parse).
        Assert.Equal<ToleratedDivergence option>(None, ToleratedDivergence.tryParse token)
    // None of the surviving tolerances carries a retired token.
    let liveNames = ToleratedDivergence.allKnown |> Set.toList |> List.map ToleratedDivergence.name |> Set.ofList
    for token in retiredTokens do
        Assert.False(Set.contains token liveNames, sprintf "%s must be absent from allKnown" token)

[<Fact>]
let ``3.4: parse accepts every known token and yields a tolerance that tolerates them`` () =
    let tokens = ToleratedDivergence.allKnown |> Set.toList |> List.map ToleratedDivergence.name
    match Tolerance.parse tokens with
    | Ok t ->
        Assert.Equal<Set<ToleratedDivergence>>(ToleratedDivergence.allKnown, Tolerance.divergences t)
    | Error e -> Assert.Fail(sprintf "expected Ok, got %A" e)

[<Fact>]
let ``3.4: an unknown tolerance name fails closed`` () =
    match Tolerance.parse [ "HeaderCommentsOmitted"; "NotARealDivergence" ] with
    | Ok t -> Assert.Fail(sprintf "expected fail-closed Error; got Ok %A" (Tolerance.divergences t))
    | Error (UnknownDivergence token) -> Assert.Equal("NotARealDivergence", token)

[<Fact>]
let ``3.4: empty config parses to strict (safe default); blank tokens are skipped`` () =
    match Tolerance.parse [] with
    | Ok t -> Assert.True(Tolerance.isStrict t)
    | Error e -> Assert.Fail(sprintf "%A" e)
    match Tolerance.parse [ "  "; "IndexOptionsUnreflected"; "" ] with
    | Ok t ->
        Assert.True(Tolerance.tolerates ToleratedDivergence.IndexOptionsUnreflected t)
        Assert.Equal(1, Set.count (Tolerance.divergences t))
    | Error e -> Assert.Fail(sprintf "%A" e)

[<Fact>]
let ``3.4: DEV tolerating HeaderCommentsOmitted passes the divergence; PROD strict fails it`` () =
    let dev =
        match Tolerance.parse [ "HeaderCommentsOmitted" ] with
        | Ok t -> t | Error e -> failwithf "%A" e
    let prod =
        match Tolerance.parse [] with
        | Ok t -> t | Error e -> failwithf "%A" e
    // DEV accepts the divergence (does not block); PROD strict does not.
    Assert.True(Tolerance.tolerates ToleratedDivergence.HeaderCommentsOmitted dev)
    Assert.False(Tolerance.tolerates ToleratedDivergence.HeaderCommentsOmitted prod)

// ---------------------------------------------------------------------------
// AC-D6 (NEITHER→HELD) — representation-only differences are NAMED tolerances
// that do NOT fire CDC.
//
// **The recon finding: SQL-native, not normalization-needed.** The CDC
// change-detection predicate (`ScriptDomBuild.perColumnChangeDetection`,
// line 763) emits `Target.[c] <> Source.[c]` where BOTH operands are
// **column references** — the stored typed values in the target table vs the
// source table — NOT rendered SQL literals. SQL Server's `<>` on those
// columns therefore applies the column-type's native comparison semantics:
//   - For `char(n)` / `nchar(n)`: ANSI trailing-blank padding — the shorter
//     operand is space-padded to the declared width before comparison, so
//     `'foo  ' <> 'foo'` is FALSE. The padding difference is representation-
//     only; the predicate does not fire.
//   - For `decimal(p,s)` / `numeric(p,s)`: numeric comparison — scale is a
//     declaration concern, not a value concern, so `1.0 <> 1.00` is FALSE.
//     The trailing-zero difference is representation-only; the predicate does
//     not fire.
// `SqlLiteral` renders literals only for DEFAULT clauses / static-seed VALUES
// tuples (the INSERT side), never for the column-vs-column CDC comparison.
// So D6 is "name the tolerances + a discriminating test", NOT "normalize the
// literal rendering". These tests anchor the two named tolerances to that SQL
// semantics; the literal-level discriminating witnesses live in
// `SqlLiteralTests.fs` (Decimal `"1.0"`/`"1.00"` render to DIFFERENT TEXT yet
// store to the SAME numeric column value; char-typed padded/unpadded raw
// render equivalently up to the trailing blanks the column re-pads).
// ---------------------------------------------------------------------------

[<Fact>]
let ``AC-D6: CharAnsiPaddingTolerated and DecimalScaleTolerated are named, parseable tolerances`` () =
    // The two representation-only tolerances exist as named DU variants with
    // config tokens (the operator-facing surface), so a per-environment
    // Tolerance can name them explicitly and the canary's R6 gate absorbs the
    // representation-only difference rather than failing on it.
    Assert.Equal("CharAnsiPaddingTolerated", ToleratedDivergence.name ToleratedDivergence.CharAnsiPaddingTolerated)
    Assert.Equal("DecimalScaleTolerated", ToleratedDivergence.name ToleratedDivergence.DecimalScaleTolerated)
    Assert.Equal(Some ToleratedDivergence.CharAnsiPaddingTolerated, ToleratedDivergence.tryParse "CharAnsiPaddingTolerated")
    Assert.Equal(Some ToleratedDivergence.DecimalScaleTolerated, ToleratedDivergence.tryParse "DecimalScaleTolerated")

[<Fact>]
let ``AC-D6: a representation-tolerant environment passes Char/Decimal divergences; strict does not`` () =
    // DISCRIMINATING: the tolerance is a real gate decision, not a no-op. An
    // environment that names the two representation tolerances passes them
    // (does not block the canary); a strict environment (PROD) does not. A
    // mislabeled-but-wrong implementation that dropped the tolerance from
    // `allKnown` would fail `parse` (fail-closed UnknownDivergence) here.
    let tolerant =
        match Tolerance.parse [ "CharAnsiPaddingTolerated"; "DecimalScaleTolerated" ] with
        | Ok t -> t | Error e -> failwithf "%A" e
    let strict =
        match Tolerance.parse [] with
        | Ok t -> t | Error e -> failwithf "%A" e
    Assert.True(Tolerance.tolerates ToleratedDivergence.CharAnsiPaddingTolerated tolerant)
    Assert.True(Tolerance.tolerates ToleratedDivergence.DecimalScaleTolerated tolerant)
    Assert.False(Tolerance.tolerates ToleratedDivergence.CharAnsiPaddingTolerated strict)
    Assert.False(Tolerance.tolerates ToleratedDivergence.DecimalScaleTolerated strict)

// ---------------------------------------------------------------------------
// M2 (THE VECTOR, Wave 0 honesty) — `TriggerBodyUnparsedDropped` is named,
// parseable, and present in the closed set. It names the formerly-silent
// CreateTrigger text-render drop (Schema OpenGap), retired when a faithful
// trigger body round-trips. (M1′'s two Decision-axis tolerances that this test
// formerly also covered — `FkTrustUnreflected` / `UniquePromotionUnreflected` —
// were RETIRED by M1, THE VECTOR Wave 1, 2026-06-15: their round-trip is now
// witnessed through the general comparator — see the M1 decision-readback
// canary in `CanaryRoundTripTests` — so the Decision axis is honestly faithful.)
// ---------------------------------------------------------------------------

[<Fact>]
let ``M2: the trigger-body tolerance is named, parseable, and in allKnown`` () =
    // DISCRIMINATING: must be in the closed set AND round-trip through the
    // operator-facing token surface (name ⇒ tryParse). A mislabeled-but-wrong
    // implementation that dropped it from `allKnown`/`name` would fail here.
    let d = ToleratedDivergence.TriggerBodyUnparsedDropped
    Assert.True(Set.contains d ToleratedDivergence.allKnown, sprintf "%A must be in allKnown" d)
    Assert.Equal<ToleratedDivergence option>(Some d, ToleratedDivergence.tryParse (ToleratedDivergence.name d))
    Assert.Equal("TriggerBodyUnparsedDropped", ToleratedDivergence.name d)

// ---------------------------------------------------------------------------
// matchedResidual — the per-run residual the canary collector resolves
// against the configured tolerance (closes the NM-32/33 provenance FLAG).
// ---------------------------------------------------------------------------

[<Fact>]
let ``matchedResidual: the accepted-AND-fired intersection is the run's residual`` () =
    // Configured to accept empty-text + char-padding; the canary observed
    // empty-text + decimal-scale firing. The residual is the intersection:
    // empty-text alone (accepted AND fired).
    let configured =
        Tolerance.ofSet (Set.ofList
            [ ToleratedDivergence.EmptyTextNormalizedToNull
              ToleratedDivergence.CharAnsiPaddingTolerated ])
    let observed =
        Set.ofList
            [ ToleratedDivergence.EmptyTextNormalizedToNull
              ToleratedDivergence.DecimalScaleTolerated ]
    let residual = Tolerance.matchedResidual observed configured
    Assert.Equal<Set<ToleratedDivergence>>(
        Set.ofList [ ToleratedDivergence.EmptyTextNormalizedToNull ],
        Tolerance.divergences residual)

[<Fact>]
let ``matchedResidual: nothing observed resolves to strict`` () =
    let configured = Tolerance.ofSet (Set.ofList [ ToleratedDivergence.EmptyTextNormalizedToNull ])
    Assert.True(Tolerance.isStrict (Tolerance.matchedResidual Set.empty configured))
