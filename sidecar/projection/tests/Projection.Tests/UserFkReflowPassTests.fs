module Projection.Tests.UserFkReflowPassTests

open Xunit
open Projection.Core
open Projection.Core.Passes

// ---------------------------------------------------------------------------
// Chapter 4.2 slice δ — UserFkReflowPass.discover (ByEmail strategy).
//
// Per `CHAPTER_4_PRESCOPE_USERFK_REFLOW.md` §4 + §6 + §7 slice 4: the
// discovery pass walks the source population, applies the matching
// strategy against the target population, and produces
// `Lineage<Diagnostics<UserRemapContext>>` — one `Annotated` lineage
// event per matched user, one `Warning` diagnostic per unmatched user.
//
// Slice δ scope: ByEmail strategy real; BySsKey / ManualOverride /
// FallbackToSystemUser emit a deferred-strategy `Error` diagnostic per
// total-decisions discipline (slice ε retires the deferred emissions
// with real implementations).
// ---------------------------------------------------------------------------

let private mustOk r =
    match r with
    | Ok v -> v
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        invalidOp (sprintf "fixture: %s" codes)

let private mkSsKey (parts: string list) : SsKey =
    SsKey.synthesizedComposite "OS_TEST" parts |> mustOk

let private mkEmail (raw: string) : Email =
    Email.create raw |> mustOk

let private mkSourceUser (id: int) (sskeyParts: string list) (email: string option) : UserAttributes<SourceUserId> =
    UserAttributes.create
        (SourceUserId.ofInt id)
        (mkSsKey sskeyParts)
        (email |> Option.map mkEmail)

let private mkTargetUser (id: int) (sskeyParts: string list) (email: string option) : UserAttributes<TargetUserId> =
    UserAttributes.create
        (TargetUserId.ofInt id)
        (mkSsKey sskeyParts)
        (email |> Option.map mkEmail)

let private srcs (users: UserAttributes<SourceUserId> list) : UserPopulation<SourceUserId> =
    UserPopulation.create users

let private tgts (users: UserAttributes<TargetUserId> list) : UserPopulation<TargetUserId> =
    UserPopulation.create users

// ---------------------------------------------------------------------------
// Empty populations: zero result, empty trail, empty diagnostics.
// ---------------------------------------------------------------------------

[<Fact>]
let ``discover: empty source + empty target produces empty UserRemapContext`` () =
    let result =
        UserFkReflowPass.discover
            UserPopulation.empty UserPopulation.empty ByEmail
    let ctx = result.Value.Value
    Assert.True (Map.isEmpty ctx.Mapping)
    Assert.True (Set.isEmpty ctx.Unmatched)
    Assert.Empty ctx.Diagnostics
    Assert.Empty result.Trail
    Assert.Empty result.Value.Entries

// ---------------------------------------------------------------------------
// ByEmail — happy path: matched user yields Mapping entry + one Annotated
// lineage event; zero diagnostics.
// ---------------------------------------------------------------------------

[<Fact>]
let ``ByEmail: one source + matching target yields one mapping`` () =
    let source = mkSourceUser 1 ["U"; "S1"] (Some "alice@example.com")
    let target = mkTargetUser 100 ["U"; "T100"] (Some "alice@example.com")
    let result =
        UserFkReflowPass.discover (srcs [ source ]) (tgts [ target ]) ByEmail
    let ctx = result.Value.Value
    Assert.Equal (1, Map.count ctx.Mapping)
    Assert.Equal<TargetUserId option> (Some (TargetUserId.ofInt 100), Map.tryFind (SourceUserId.ofInt 1) ctx.Mapping)
    Assert.True (Set.isEmpty ctx.Unmatched)

[<Fact>]
let ``ByEmail: matched user emits one Annotated lineage event`` () =
    let source = mkSourceUser 1 ["U"; "S1"] (Some "alice@example.com")
    let target = mkTargetUser 100 ["U"; "T100"] (Some "alice@example.com")
    let result =
        UserFkReflowPass.discover (srcs [ source ]) (tgts [ target ]) ByEmail
    Assert.Equal (1, List.length result.Trail)
    let event = List.head result.Trail
    Assert.Equal<string> ("userFkReflow", event.PassName)
    match event.TransformKind with
    | Annotated (Label label) -> Assert.Equal<string> ("userFkReflow.matched-by-ByEmail", label)
    | other                   -> Assert.Fail (sprintf "expected Annotated (Label _), got %A" other)

[<Fact>]
let ``ByEmail: matched user emits zero diagnostics`` () =
    let source = mkSourceUser 1 ["U"; "S1"] (Some "alice@example.com")
    let target = mkTargetUser 100 ["U"; "T100"] (Some "alice@example.com")
    let result =
        UserFkReflowPass.discover (srcs [ source ]) (tgts [ target ]) ByEmail
    Assert.Empty result.Value.Entries

// ---------------------------------------------------------------------------
// ByEmail — miss cases: each emits exactly one Warning + one RemapDiagnostic.
// ---------------------------------------------------------------------------

[<Fact>]
let ``ByEmail: source with no email yields NoEmail diagnostic`` () =
    let source = mkSourceUser 1 ["U"; "S1"] None
    let result =
        UserFkReflowPass.discover (srcs [ source ]) UserPopulation.empty ByEmail
    let ctx = result.Value.Value
    Assert.Equal (0, Map.count ctx.Mapping)
    Assert.True (Set.contains (SourceUserId.ofInt 1) ctx.Unmatched)
    Assert.Equal (1, List.length ctx.Diagnostics)
    match List.head ctx.Diagnostics with
    | NoEmail src -> Assert.Equal (SourceUserId.ofInt 1, src)
    | other       -> Assert.Fail (sprintf "expected NoEmail, got %A" other)

[<Fact>]
let ``ByEmail: source email with no target match yields EmailDidNotMatch diagnostic`` () =
    let source = mkSourceUser 1 ["U"; "S1"] (Some "alice@example.com")
    let target = mkTargetUser 100 ["U"; "T100"] (Some "bob@example.com")
    let result =
        UserFkReflowPass.discover (srcs [ source ]) (tgts [ target ]) ByEmail
    let ctx = result.Value.Value
    Assert.True (Set.contains (SourceUserId.ofInt 1) ctx.Unmatched)
    match List.head ctx.Diagnostics with
    | EmailDidNotMatch (src, email) ->
        Assert.Equal (SourceUserId.ofInt 1, src)
        Assert.Equal<string> ("alice@example.com", Email.value email)
    | other -> Assert.Fail (sprintf "expected EmailDidNotMatch, got %A" other)

[<Fact>]
let ``ByEmail: every unmatched source emits exactly one Warning entry`` () =
    let source = mkSourceUser 1 ["U"; "S1"] (Some "alice@example.com")
    let result =
        UserFkReflowPass.discover (srcs [ source ]) UserPopulation.empty ByEmail
    Assert.Equal (1, List.length result.Value.Entries)
    let entry = List.head result.Value.Entries
    Assert.Equal (DiagnosticSeverity.Warning, entry.Severity)
    Assert.Equal<string> ("userFkReflow.emailDidNotMatch", entry.Code)
    Assert.Equal<string> ("userFkReflow", entry.Source)

// ---------------------------------------------------------------------------
// ByEmail — case-insensitive matching + Trim() normalization.
// ---------------------------------------------------------------------------

[<Fact>]
let ``ByEmail: matching is case-insensitive (V1 OrdinalIgnoreCase parity)`` () =
    let source = mkSourceUser 1 ["U"; "S1"] (Some "Alice@Example.COM")
    let target = mkTargetUser 100 ["U"; "T100"] (Some "alice@example.com")
    let result =
        UserFkReflowPass.discover (srcs [ source ]) (tgts [ target ]) ByEmail
    let ctx = result.Value.Value
    Assert.Equal<TargetUserId option> (Some (TargetUserId.ofInt 100), Map.tryFind (SourceUserId.ofInt 1) ctx.Mapping)

[<Fact>]
let ``ByEmail: matching honors Trim() normalization at Email.create`` () =
    let source = mkSourceUser 1 ["U"; "S1"] (Some "  alice@example.com  ")
    let target = mkTargetUser 100 ["U"; "T100"] (Some "alice@example.com")
    let result =
        UserFkReflowPass.discover (srcs [ source ]) (tgts [ target ]) ByEmail
    let ctx = result.Value.Value
    Assert.Equal<TargetUserId option> (Some (TargetUserId.ofInt 100), Map.tryFind (SourceUserId.ofInt 1) ctx.Mapping)

// ---------------------------------------------------------------------------
// Determinism: T1 byte-determinism + permutation invariance.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: discover is byte-deterministic on repeat invocation`` () =
    let sources =
        [ mkSourceUser 1 ["U"; "S1"] (Some "alice@example.com")
          mkSourceUser 2 ["U"; "S2"] (Some "bob@example.com")
          mkSourceUser 3 ["U"; "S3"] None ]
    let targets =
        [ mkTargetUser 100 ["U"; "T100"] (Some "alice@example.com") ]
    let r1 = UserFkReflowPass.discover (srcs sources) (tgts targets) ByEmail
    let r2 = UserFkReflowPass.discover (srcs sources) (tgts targets) ByEmail
    Assert.Equal<Map<SourceUserId, TargetUserId>> (r1.Value.Value.Mapping, r2.Value.Value.Mapping)
    Assert.Equal<Set<SourceUserId>> (r1.Value.Value.Unmatched, r2.Value.Value.Unmatched)
    Assert.Equal<RemapDiagnostic list> (r1.Value.Value.Diagnostics, r2.Value.Value.Diagnostics)
    Assert.Equal<LineageEvent list> (r1.Trail, r2.Trail)
    Assert.Equal<DiagnosticEntry list> (r1.Value.Entries, r2.Value.Entries)

[<Fact>]
let ``T1: discover is permutation-invariant on the source list (SsKey-sorted iteration)`` () =
    let s1 = mkSourceUser 1 ["U"; "S1"] (Some "alice@example.com")
    let s2 = mkSourceUser 2 ["U"; "S2"] (Some "bob@example.com")
    let s3 = mkSourceUser 3 ["U"; "S3"] (Some "carol@example.com")
    let targets =
        [ mkTargetUser 100 ["U"; "T100"] (Some "alice@example.com")
          mkTargetUser 200 ["U"; "T200"] (Some "bob@example.com")
          mkTargetUser 300 ["U"; "T300"] (Some "carol@example.com") ]
    let r1 = UserFkReflowPass.discover (srcs [ s1; s2; s3 ]) (tgts targets) ByEmail
    let r2 = UserFkReflowPass.discover (srcs [ s3; s1; s2 ]) (tgts targets) ByEmail
    Assert.Equal<Map<SourceUserId, TargetUserId>> (r1.Value.Value.Mapping, r2.Value.Value.Mapping)
    Assert.Equal<LineageEvent list> (r1.Trail, r2.Trail)
    Assert.Equal<DiagnosticEntry list> (r1.Value.Entries, r2.Value.Entries)

// ---------------------------------------------------------------------------
// run entry point (canonical Catalog × Policy × Profile signature).
// ---------------------------------------------------------------------------

[<Fact>]
let ``run: reads Profile.SourceUsers + Profile.TargetUsers + Policy.UserMatching`` () =
    let source = mkSourceUser 1 ["U"; "S1"] (Some "alice@example.com")
    let target = mkTargetUser 100 ["U"; "T100"] (Some "alice@example.com")
    let profile =
        { Profile.empty with
            SourceUsers = srcs [ source ]
            TargetUsers = tgts [ target ] }
    let policy = { Policy.empty with UserMatching = ByEmail }
    let catalog = { Modules = []; Triggers = []  }
    let result = UserFkReflowPass.run catalog policy profile
    Assert.Equal (1, Map.count result.Value.Value.Mapping)

// ---------------------------------------------------------------------------
// A32 — discovered value visible to (future) emitter consumers.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A32: discover output flows through UserRemapContext smart-constructor invariant`` () =
    // The smart-constructor invariant (Mapping.Keys ∩ Unmatched = ∅)
    // is enforced by the pass's single-pass walk: each source user is
    // added to AT MOST ONE of Mapping/Unmatched. Verify by walking a
    // mixed population (some matched, some unmatched) and asserting
    // disjointness.
    let sources =
        [ mkSourceUser 1 ["U"; "S1"] (Some "alice@example.com")
          mkSourceUser 2 ["U"; "S2"] (Some "bob@example.com")
          mkSourceUser 3 ["U"; "S3"] None ]
    let targets =
        [ mkTargetUser 100 ["U"; "T100"] (Some "alice@example.com") ]
    let result = UserFkReflowPass.discover (srcs sources) (tgts targets) ByEmail
    let ctx = result.Value.Value
    let mappingKeys = ctx.Mapping |> Map.toSeq |> Seq.map fst |> Set.ofSeq
    Assert.True (Set.isEmpty (Set.intersect mappingKeys ctx.Unmatched))

// ---------------------------------------------------------------------------
// Slice ε — BySsKey strategy.
// ---------------------------------------------------------------------------

[<Fact>]
let ``BySsKey: one source + matching SsKey target yields one mapping`` () =
    let sharedKey = mkSsKey ["U"; "GUID-1"]
    let source =
        UserAttributes.create (SourceUserId.ofInt 1) sharedKey (Some (mkEmail "alice@source.com"))
    let target =
        UserAttributes.create (TargetUserId.ofInt 100) sharedKey (Some (mkEmail "alice@target.com"))
    let result =
        UserFkReflowPass.discover (srcs [ source ]) (tgts [ target ]) BySsKey
    let ctx = result.Value.Value
    Assert.Equal<TargetUserId option> (Some (TargetUserId.ofInt 100), Map.tryFind (SourceUserId.ofInt 1) ctx.Mapping)
    Assert.True (Set.isEmpty ctx.Unmatched)

[<Fact>]
let ``BySsKey: emits matched-by-BySsKey lineage label`` () =
    let sharedKey = mkSsKey ["U"; "GUID-1"]
    let source =
        UserAttributes.create (SourceUserId.ofInt 1) sharedKey None
    let target =
        UserAttributes.create (TargetUserId.ofInt 100) sharedKey None
    let result =
        UserFkReflowPass.discover (srcs [ source ]) (tgts [ target ]) BySsKey
    let event = List.head result.Trail
    match event.TransformKind with
    | Annotated (Label label) -> Assert.Equal<string> ("userFkReflow.matched-by-BySsKey", label)
    | other                   -> Assert.Fail (sprintf "expected Annotated (Label _), got %A" other)

[<Fact>]
let ``BySsKey: source SsKey absent from target yields SsKeyDidNotMatch diagnostic`` () =
    let source =
        UserAttributes.create
            (SourceUserId.ofInt 1) (mkSsKey ["U"; "GUID-source"]) None
    let target =
        UserAttributes.create
            (TargetUserId.ofInt 100) (mkSsKey ["U"; "GUID-target"]) None
    let result =
        UserFkReflowPass.discover (srcs [ source ]) (tgts [ target ]) BySsKey
    Assert.True (Set.contains (SourceUserId.ofInt 1) result.Value.Value.Unmatched)
    match List.head result.Value.Value.Diagnostics with
    | SsKeyDidNotMatch (src, _) -> Assert.Equal (SourceUserId.ofInt 1, src)
    | other                     -> Assert.Fail (sprintf "expected SsKeyDidNotMatch, got %A" other)

// ---------------------------------------------------------------------------
// Slice ε — ManualOverride strategy.
// ---------------------------------------------------------------------------

[<Fact>]
let ``ManualOverride: source in override map yields one mapping`` () =
    let source = mkSourceUser 1 ["U"; "S1"] None
    let overrideMap =
        Map.ofList [ SourceUserId.ofInt 1, TargetUserId.ofInt 100 ]
    let result =
        UserFkReflowPass.discover
            (srcs [ source ]) UserPopulation.empty
            (ManualOverride overrideMap)
    let ctx = result.Value.Value
    Assert.Equal<TargetUserId option> (Some (TargetUserId.ofInt 100), Map.tryFind (SourceUserId.ofInt 1) ctx.Mapping)
    Assert.True (Set.isEmpty ctx.Unmatched)

[<Fact>]
let ``ManualOverride: emits matched-by-ManualOverride lineage label`` () =
    let source = mkSourceUser 1 ["U"; "S1"] None
    let overrideMap =
        Map.ofList [ SourceUserId.ofInt 1, TargetUserId.ofInt 100 ]
    let result =
        UserFkReflowPass.discover
            (srcs [ source ]) UserPopulation.empty
            (ManualOverride overrideMap)
    match (List.head result.Trail).TransformKind with
    | Annotated (Label label) -> Assert.Equal<string> ("userFkReflow.matched-by-ManualOverride", label)
    | other                   -> Assert.Fail (sprintf "expected Annotated (Label _), got %A" other)

[<Fact>]
let ``ManualOverride: source absent from map yields OverrideMissing diagnostic`` () =
    let source = mkSourceUser 1 ["U"; "S1"] None
    let result =
        UserFkReflowPass.discover
            (srcs [ source ]) UserPopulation.empty
            (ManualOverride Map.empty)
    Assert.True (Set.contains (SourceUserId.ofInt 1) result.Value.Value.Unmatched)
    match List.head result.Value.Value.Diagnostics with
    | OverrideMissing src -> Assert.Equal (SourceUserId.ofInt 1, src)
    | other               -> Assert.Fail (sprintf "expected OverrideMissing, got %A" other)

// ---------------------------------------------------------------------------
// Slice ε — FallbackToSystemUser strategy (composition + safety-net guarantee).
// ---------------------------------------------------------------------------

[<Fact>]
let ``FallbackToSystemUser: primary match yields matched-by-FallbackToSystemUser.primary label`` () =
    let source = mkSourceUser 1 ["U"; "S1"] (Some "alice@example.com")
    let target = mkTargetUser 100 ["U"; "T100"] (Some "alice@example.com")
    let fallback = TargetUserId.ofInt 999
    let result =
        UserFkReflowPass.discover
            (srcs [ source ]) (tgts [ target ])
            (FallbackToSystemUser (fallback, ByEmail))
    let ctx = result.Value.Value
    // Primary matched → target = 100 (not the fallback).
    Assert.Equal<TargetUserId option> (Some (TargetUserId.ofInt 100), Map.tryFind (SourceUserId.ofInt 1) ctx.Mapping)
    match (List.head result.Trail).TransformKind with
    | Annotated (Label label) -> Assert.Equal<string> ("userFkReflow.matched-by-FallbackToSystemUser.primary", label)
    | other                   -> Assert.Fail (sprintf "expected primary label, got %A" other)

[<Fact>]
let ``FallbackToSystemUser: primary miss yields fallback match + matched-by-FallbackToSystemUser.fallback label`` () =
    // Source has no email match in target population → primary
    // ByEmail fails → fallback target is applied.
    let source = mkSourceUser 1 ["U"; "S1"] (Some "alice@example.com")
    let fallback = TargetUserId.ofInt 999
    let result =
        UserFkReflowPass.discover
            (srcs [ source ]) UserPopulation.empty
            (FallbackToSystemUser (fallback, ByEmail))
    let ctx = result.Value.Value
    // Fallback applied → target = 999.
    Assert.Equal<TargetUserId option> (Some (TargetUserId.ofInt 999), Map.tryFind (SourceUserId.ofInt 1) ctx.Mapping)
    match (List.head result.Trail).TransformKind with
    | Annotated (Label label) -> Assert.Equal<string> ("userFkReflow.matched-by-FallbackToSystemUser.fallback", label)
    | other                   -> Assert.Fail (sprintf "expected fallback label, got %A" other)

[<Fact>]
let ``FallbackToSystemUser: structurally guarantees Set.isEmpty Unmatched (safety net)`` () =
    // Pre-scope §3 promise: FallbackToSystemUser produces
    // Set.isEmpty Unmatched. Mixed source population — some have
    // emails matching, some don't — but the fallback catches
    // every miss.
    let sources =
        [ mkSourceUser 1 ["U"; "S1"] (Some "alice@example.com")
          mkSourceUser 2 ["U"; "S2"] None
          mkSourceUser 3 ["U"; "S3"] (Some "absent@example.com") ]
    let targets =
        [ mkTargetUser 100 ["U"; "T100"] (Some "alice@example.com") ]
    let fallback = TargetUserId.ofInt 999
    let result =
        UserFkReflowPass.discover
            (srcs sources) (tgts targets)
            (FallbackToSystemUser (fallback, ByEmail))
    Assert.True (UserRemapContext.isFullyMapped result.Value.Value)
    // Every source mapped: alice → 100; bob → 999; carol → 999.
    Assert.Equal (3, Map.count result.Value.Value.Mapping)

[<Fact>]
let ``FallbackToSystemUser: nested fallback chain composes (outer fallback catches inner miss)`` () =
    // Outer = Fallback (999, Inner)
    // Inner = Fallback (888, ByEmail)
    // Source has no email match → Inner.ByEmail misses → Inner.fallback
    // applies → outer sees a Match → outer reports primary-matched
    // (carrying the inner-fallback target 888).
    let source = mkSourceUser 1 ["U"; "S1"] (Some "alice@example.com")
    let outerFallback = TargetUserId.ofInt 999
    let innerFallback = TargetUserId.ofInt 888
    let strategy =
        FallbackToSystemUser (outerFallback, FallbackToSystemUser (innerFallback, ByEmail))
    let result =
        UserFkReflowPass.discover (srcs [ source ]) UserPopulation.empty strategy
    let ctx = result.Value.Value
    // Inner fallback (888) wins (outer never fires because inner
    // returns Matched).
    Assert.Equal<TargetUserId option> (Some (TargetUserId.ofInt 888), Map.tryFind (SourceUserId.ofInt 1) ctx.Mapping)
    Assert.True (UserRemapContext.isFullyMapped ctx)

// ---------------------------------------------------------------------------
// Slice ε — heterogeneous emission across strategies (closed-DU coverage).
// ---------------------------------------------------------------------------

[<Fact>]
let ``Slice ε: all four strategy variants produce decisions (closed-DU coverage)`` () =
    // Closed-DU expansion empirical-test: every variant of
    // UserMatchingStrategy is handled by the pass without a
    // deferred-strategy diagnostic. Verify by sweeping every
    // variant with a one-source population and checking the
    // result contains either a Mapping entry or an Unmatched
    // entry (not a strategyNotYetImplemented diagnostic).
    let source = mkSourceUser 1 ["U"; "S1"] (Some "alice@example.com")
    let strategies =
        [ ByEmail
          BySsKey
          ManualOverride Map.empty
          FallbackToSystemUser (TargetUserId.ofInt 999, ByEmail) ]
    for strategy in strategies do
        let result =
            UserFkReflowPass.discover (srcs [ source ]) UserPopulation.empty strategy
        // The pass produces a decision (Matched or Unmatched) for
        // every variant; no strategyNotYetImplemented diagnostic.
        let hasDeferredStrategy =
            result.Value.Entries
            |> List.exists (fun e -> e.Code = "userFkReflow.strategyNotYetImplemented")
        Assert.False (hasDeferredStrategy, sprintf "strategy %A produced deferred diagnostic" strategy)
