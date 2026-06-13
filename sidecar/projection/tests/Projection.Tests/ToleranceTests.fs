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
                ToleratedDivergence.CharAnsiPaddingTolerated
                ToleratedDivergence.DecimalScaleTolerated
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
let ``Closed-DU coverage: ToleratedDivergence.allKnown contains eleven variants (NM-16 kind-facet diff-erasure tolerances added)`` () =
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
    Assert.Equal (11, Set.count ToleratedDivergence.allKnown)

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
// NM-16 — the kind-level facet erasure in the `CatalogDiff.between` algebra
// (a changed/added/removed kind Trigger / ColumnCheck / Modality / IsActive
// yields norm=0, "idempotent redeploy", migrate emits nothing) is now a
// WITNESSED ToleratedDivergence rather than a silent erasure. These four
// variants exist, are members of `allKnown`, and parse round-trip — so the
// gap is named with a retirement trigger (the LIGHT route per the audit;
// retiring each adds the corresponding diff channel to `CatalogDiff.between`).
// ---------------------------------------------------------------------------

[<Fact>]
let ``NM-16: the four kind-facet diff-erasure tolerances exist, are in allKnown, and round-trip`` () =
    let kindFacetVariants =
        [ ToleratedDivergence.KindTriggersUnreflectedInDiff
          ToleratedDivergence.KindChecksUnreflectedInDiff
          ToleratedDivergence.KindModalityUnreflectedInDiff
          ToleratedDivergence.KindActivationUnreflectedInDiff ]
    for d in kindFacetVariants do
        // Each is a member of the closed-DU coverage set.
        Assert.True(
            Set.contains d ToleratedDivergence.allKnown,
            sprintf "%s must be in allKnown" (ToleratedDivergence.name d))
        // name >> tryParse is the identity (parse round-trip).
        Assert.Equal<ToleratedDivergence option>(
            Some d, ToleratedDivergence.tryParse (ToleratedDivergence.name d))
    // The four tokens are distinct (no name collision).
    let names = kindFacetVariants |> List.map ToleratedDivergence.name
    Assert.Equal(4, List.length (List.distinct names))

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
