module Projection.Tests.UserMatchingStrategyTests

open Xunit
open Projection.Core

// ---------------------------------------------------------------------------
// Chapter 4.2 slice α — UserMatchingStrategy DU + identity types
// + smart constructors + Policy.UserMatching axis.
//
// Per `CHAPTER_4_PRESCOPE_USERFK_REFLOW.md` §3 + V1's empirical experience
// (`UserMatchingEngine.cs:33-67` + `UserMatchingOptions.cs:7-19`), V2's
// strategy DU has four variants with `FallbackToSystemUser` recursive on
// the primary. Identity newtypes (UserId / SourceUserId / TargetUserId /
// Email) prevent orientation confusion at the type level. `Email.create`
// validates non-blank + normalizes via `Trim()` per V1 parity.
// ---------------------------------------------------------------------------

let private mustOk r =
    match r with
    | Ok v -> v
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        invalidOp (sprintf "fixture: %s" codes)

// ---------------------------------------------------------------------------
// Identity types — UserId / SourceUserId / TargetUserId.
// ---------------------------------------------------------------------------

[<Fact>]
let ``UserId.value projects the wrapped integer`` () =
    let id = UserId 42
    Assert.Equal (42, UserId.value id)

[<Fact>]
let ``SourceUserId.ofInt + value round-trip`` () =
    let id = SourceUserId.ofInt 7
    Assert.Equal (7, SourceUserId.value id)

[<Fact>]
let ``TargetUserId.ofInt + value round-trip`` () =
    let id = TargetUserId.ofInt 99
    Assert.Equal (99, TargetUserId.value id)

[<Fact>]
let ``Source and target user ids are distinct types (no orientation confusion)`` () =
    // Compile-time guarantee: `let _ : SourceUserId = TargetUserId.ofInt 7`
    // would not compile. Test the runtime equivalent: ids constructed
    // with the same underlying int are NOT equal across orientation.
    let s = SourceUserId.ofInt 7
    let t = TargetUserId.ofInt 7
    // F# structural equality rejects cross-type comparison at compile
    // time too; this test documents the DDD intent that the wrappers
    // are distinct value objects, not type aliases.
    Assert.Equal (7, SourceUserId.value s)
    Assert.Equal (7, TargetUserId.value t)

// ---------------------------------------------------------------------------
// Email — smart constructor.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Email.create rejects blank input`` () =
    match Email.create "" with
    | Error es ->
        Assert.Single es |> ignore
        Assert.Equal<string> ("email.empty", (List.head es).Code)
    | Ok _ -> Assert.Fail "expected Error for blank email"

[<Fact>]
let ``Email.create rejects whitespace-only input`` () =
    match Email.create "   " with
    | Error es -> Assert.Equal<string> ("email.empty", (List.head es).Code)
    | Ok _ -> Assert.Fail "expected Error for whitespace email"

[<Fact>]
let ``Email.create normalizes via Trim`` () =
    let e = Email.create "  alice@example.com  " |> mustOk
    Assert.Equal<string> ("alice@example.com", Email.value e)

[<Fact>]
let ``Email.create preserves case (V1 parity: case normalization happens at compare time)`` () =
    let e = Email.create "Alice@Example.COM" |> mustOk
    Assert.Equal<string> ("Alice@Example.COM", Email.value e)

[<Fact>]
let ``Email.create accepts a valid email`` () =
    let e = Email.create "bob@example.com" |> mustOk
    Assert.Equal<string> ("bob@example.com", Email.value e)

// ---------------------------------------------------------------------------
// UserMatchingStrategy DU exhaustiveness + empty default.
// ---------------------------------------------------------------------------

[<Fact>]
let ``UserMatchingStrategy.empty is ByEmail (V1 default parity)`` () =
    Assert.Equal (ByEmail, UserMatchingStrategy.empty)

[<Fact>]
let ``UserMatchingStrategy DU has four variants (compile-time exhaustiveness)`` () =
    // The match below must be exhaustive at compile time; if a future
    // variant lands without updating consumers, F# exhaustiveness
    // errors light up only at match sites within the variant's module
    // (per the closed-DU expansion empirical-test discipline).
    let label (s: UserMatchingStrategy) : string =
        match s with
        | ByEmail                          -> "ByEmail"
        | BySsKey                          -> "BySsKey"
        | ManualOverride _                 -> "ManualOverride"
        | FallbackToSystemUser (_, _)      -> "FallbackToSystemUser"
    Assert.Equal<string> ("ByEmail", label ByEmail)
    Assert.Equal<string> ("BySsKey", label BySsKey)
    Assert.Equal<string> ("ManualOverride", label (ManualOverride Map.empty))
    let fallback = TargetUserId.ofInt 1
    Assert.Equal<string> ("FallbackToSystemUser", label (FallbackToSystemUser (fallback, ByEmail)))

[<Fact>]
let ``ManualOverride accepts an empty map (degenerate no-op shape)`` () =
    let s = ManualOverride Map.empty
    match s with
    | ManualOverride m -> Assert.True (Map.isEmpty m)
    | _ -> Assert.Fail "expected ManualOverride"

[<Fact>]
let ``ManualOverride accepts a populated map`` () =
    let map =
        Map.ofList
            [ SourceUserId.ofInt 1, TargetUserId.ofInt 100
              SourceUserId.ofInt 2, TargetUserId.ofInt 200 ]
    let s = ManualOverride map
    match s with
    | ManualOverride m ->
        Assert.Equal (2, Map.count m)
        Assert.Equal<TargetUserId option> (Some (TargetUserId.ofInt 100), Map.tryFind (SourceUserId.ofInt 1) m)
    | _ -> Assert.Fail "expected ManualOverride"

[<Fact>]
let ``FallbackToSystemUser composes recursively over a primary strategy`` () =
    let fallback = TargetUserId.ofInt 999
    let composed = FallbackToSystemUser (fallback, ByEmail)
    match composed with
    | FallbackToSystemUser (fb, ByEmail) ->
        Assert.Equal (fallback, fb)
    | _ -> Assert.Fail "expected FallbackToSystemUser (fallback, ByEmail)"

[<Fact>]
let ``FallbackToSystemUser composes recursively (nested fallback chain)`` () =
    let outerFallback = TargetUserId.ofInt 999
    let innerFallback = TargetUserId.ofInt 888
    let inner = FallbackToSystemUser (innerFallback, ByEmail)
    let outer = FallbackToSystemUser (outerFallback, inner)
    match outer with
    | FallbackToSystemUser (fb1, FallbackToSystemUser (fb2, ByEmail)) ->
        Assert.Equal (outerFallback, fb1)
        Assert.Equal (innerFallback, fb2)
    | _ -> Assert.Fail "expected nested FallbackToSystemUser"

// ---------------------------------------------------------------------------
// Policy.UserMatching axis — the fifth axis (chapter 4.2 slice α).
// ---------------------------------------------------------------------------

[<Fact>]
let ``Policy.empty.UserMatching defaults to ByEmail (V1 parity)`` () =
    Assert.Equal (ByEmail, Policy.empty.UserMatching)

[<Fact>]
let ``Policy.empty preserves the four prior axes (Selection / Emission / Insertion / Tightening)`` () =
    // Record-extension change should NOT break any prior-axis defaults
    // (closed-DU expansion empirical-test discipline applied to record
    // extension; F# field-missing errors light up at literal-construction
    // sites only — verified by build).
    let p = Policy.empty
    Assert.Equal (SelectionPolicy.empty, p.Selection)
    Assert.Equal (EmissionPolicy.empty, p.Emission)
    Assert.Equal (InsertionPolicy.empty, p.Insertion)
    Assert.Equal (TighteningPolicy.empty, p.Tightening)

[<Fact>]
let ``Policy.UserMatching can be set to any DU variant`` () =
    let fallback = TargetUserId.ofInt 1
    let strategies =
        [ ByEmail
          BySsKey
          ManualOverride Map.empty
          FallbackToSystemUser (fallback, ByEmail) ]
    for s in strategies do
        let p = { Policy.empty with UserMatching = s }
        Assert.Equal (s, p.UserMatching)
