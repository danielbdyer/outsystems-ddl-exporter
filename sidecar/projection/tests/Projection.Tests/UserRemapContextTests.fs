module Projection.Tests.UserRemapContextTests

open Xunit
open Projection.Core

// ---------------------------------------------------------------------------
// Chapter 4.2 slice γ — UserRemapContext shape + smart constructor +
// module accessors + RemapDiagnostic DU.
//
// Per `CHAPTER_4_PRESCOPE_USERFK_REFLOW.md` §4 + §7 slice 3: the
// structural shape (`{ Mapping; Unmatched; Diagnostics }`) replaces the
// chapter-4.1.B slice ζ placeholder (`Map<SsKey, Map<int64, int64>>`).
// Smart-constructor invariant: `Mapping.Keys ∩ Unmatched = ∅`
// (a source user is either matched or unmatched, never both). The
// invariant rides on every value per the structural-commitment-via-
// construction-validation operational principle.
// ---------------------------------------------------------------------------

let private mustOk r =
    match r with
    | Ok v -> v
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        invalidOp (sprintf "fixture: %s" codes)

let private mkSsKey (parts: string list) : SsKey =
    SsKey.synthesizedComposite "OS_TEST" parts |> mustOk

// ---------------------------------------------------------------------------
// UserRemapContext.empty + accessors.
// ---------------------------------------------------------------------------

[<Fact>]
let ``UserRemapContext.empty has empty Mapping, Unmatched, Diagnostics`` () =
    let ctx = UserRemapContext.empty
    Assert.True (Map.isEmpty ctx.Mapping)
    Assert.True (Set.isEmpty ctx.Unmatched)
    Assert.Empty ctx.Diagnostics

[<Fact>]
let ``UserRemapContext.isFullyMapped true when Unmatched is empty`` () =
    Assert.True (UserRemapContext.isFullyMapped UserRemapContext.empty)

[<Fact>]
let ``UserRemapContext.isFullyMapped false when Unmatched is non-empty`` () =
    let ctx =
        UserRemapContext.create
            Map.empty
            (Set.ofList [ SourceUserId.ofInt 1 ])
            [ NoEmail (SourceUserId.ofInt 1) ]
        |> mustOk
    Assert.False (UserRemapContext.isFullyMapped ctx)

[<Fact>]
let ``UserRemapContext.unmatchedCount returns Set.count of Unmatched`` () =
    let ctx =
        UserRemapContext.create
            Map.empty
            (Set.ofList [ SourceUserId.ofInt 1; SourceUserId.ofInt 2; SourceUserId.ofInt 3 ])
            [ NoEmail (SourceUserId.ofInt 1)
              NoEmail (SourceUserId.ofInt 2)
              NoEmail (SourceUserId.ofInt 3) ]
        |> mustOk
    Assert.Equal (3, UserRemapContext.unmatchedCount ctx)

[<Fact>]
let ``UserRemapContext.tryFindTarget returns Some for mapped source`` () =
    let s = SourceUserId.ofInt 1
    let t = TargetUserId.ofInt 100
    let ctx =
        UserRemapContext.create
            (Map.ofList [ s, t ])
            Set.empty
            []
        |> mustOk
    Assert.Equal<TargetUserId option> (Some t, UserRemapContext.tryFindTarget s ctx)

[<Fact>]
let ``UserRemapContext.tryFindTarget returns None for unmapped source`` () =
    let s = SourceUserId.ofInt 1
    Assert.Equal<TargetUserId option> (None, UserRemapContext.tryFindTarget s UserRemapContext.empty)

// ---------------------------------------------------------------------------
// Smart-constructor invariant: Mapping.Keys ∩ Unmatched = ∅.
// ---------------------------------------------------------------------------

[<Fact>]
let ``UserRemapContext.create succeeds when Mapping and Unmatched are disjoint`` () =
    let s1 = SourceUserId.ofInt 1
    let s2 = SourceUserId.ofInt 2
    let mapping = Map.ofList [ s1, TargetUserId.ofInt 100 ]
    let unmatched = Set.ofList [ s2 ]
    match UserRemapContext.create mapping unmatched [] with
    | Ok ctx ->
        Assert.Equal (1, Map.count ctx.Mapping)
        Assert.Equal (1, Set.count ctx.Unmatched)
    | Error _ -> Assert.Fail "expected Ok for disjoint Mapping/Unmatched"

[<Fact>]
let ``UserRemapContext.create fails when a source user is in BOTH Mapping and Unmatched`` () =
    let s1 = SourceUserId.ofInt 1
    let mapping = Map.ofList [ s1, TargetUserId.ofInt 100 ]
    let unmatched = Set.ofList [ s1 ]
    match UserRemapContext.create mapping unmatched [] with
    | Error es ->
        Assert.NotEmpty es
        Assert.Equal<string> ("userRemapContext.overlap", (List.head es).Code)
    | Ok _ -> Assert.Fail "expected Error for overlapping source user"

[<Fact>]
let ``UserRemapContext.create reports every overlapping source user (validation-style accumulation)`` () =
    let s1 = SourceUserId.ofInt 1
    let s2 = SourceUserId.ofInt 2
    let s3 = SourceUserId.ofInt 3
    let mapping =
        Map.ofList
            [ s1, TargetUserId.ofInt 100
              s2, TargetUserId.ofInt 200
              s3, TargetUserId.ofInt 300 ]
    let unmatched = Set.ofList [ s1; s2 ]  // both overlap
    match UserRemapContext.create mapping unmatched [] with
    | Error es ->
        // Validation-style: one error per overlap, not just the first.
        Assert.Equal (2, List.length es)
        for e in es do
            Assert.Equal<string> ("userRemapContext.overlap", e.Code)
    | Ok _ -> Assert.Fail "expected Error reporting both overlaps"

// ---------------------------------------------------------------------------
// RemapDiagnostic DU exhaustiveness.
// ---------------------------------------------------------------------------

[<Fact>]
let ``RemapDiagnostic DU has five variants (compile-time exhaustiveness)`` () =
    let s = SourceUserId.ofInt 1
    let key = mkSsKey ["U"; "1"]
    let email = Email.create "alice@example.com" |> mustOk
    let label (d: RemapDiagnostic) : string =
        match d with
        | NoEmail _              -> "NoEmail"
        | EmailDidNotMatch _     -> "EmailDidNotMatch"
        | SsKeyDidNotMatch _     -> "SsKeyDidNotMatch"
        | OverrideMissing _      -> "OverrideMissing"
        | NoFallbackConfigured _ -> "NoFallbackConfigured"
    Assert.Equal<string> ("NoEmail",              label (NoEmail s))
    Assert.Equal<string> ("EmailDidNotMatch",     label (EmailDidNotMatch (s, email)))
    Assert.Equal<string> ("SsKeyDidNotMatch",     label (SsKeyDidNotMatch (s, key)))
    Assert.Equal<string> ("OverrideMissing",      label (OverrideMissing s))
    Assert.Equal<string> ("NoFallbackConfigured", label (NoFallbackConfigured s))
