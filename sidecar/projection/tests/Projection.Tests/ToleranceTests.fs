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
                ToleratedDivergence.IndexesUnreflected
                ToleratedDivergence.StaticPopulationsUnreflected
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
let ``Closed-DU coverage: ToleratedDivergence.allKnown contains five variants (6.A.4 EmptyTextNormalizedToNull added)`` () =
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
    // normalization (closed, not silent).
    Assert.Equal (5, Set.count ToleratedDivergence.allKnown)

[<Fact>]
let ``Tolerance.ofSet round-trips through divergences`` () =
    let s = Set.ofList [ ToleratedDivergence.HeaderCommentsOmitted; ToleratedDivergence.IndexesUnreflected ]
    let t = Tolerance.ofSet s
    Assert.Equal<Set<ToleratedDivergence>> (s, Tolerance.divergences t)

[<Fact>]
let ``Tolerance.withDivergence on strict yields a singleton tolerance`` () =
    let t = Tolerance.strict |> Tolerance.withDivergence ToleratedDivergence.HeaderCommentsOmitted
    Assert.True (Tolerance.tolerates ToleratedDivergence.HeaderCommentsOmitted t)
    Assert.False (Tolerance.tolerates ToleratedDivergence.IndexesUnreflected t)
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
    match Tolerance.parse [ "  "; "IndexesUnreflected"; "" ] with
    | Ok t ->
        Assert.True(Tolerance.tolerates ToleratedDivergence.IndexesUnreflected t)
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
