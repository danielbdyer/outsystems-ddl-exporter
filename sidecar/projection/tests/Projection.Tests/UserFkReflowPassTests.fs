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
    let catalog = { Modules = [] }
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
// Deferred-strategy emissions (slice δ scope; slice ε retires these).
// ---------------------------------------------------------------------------

[<Fact>]
let ``Slice δ deferral: BySsKey strategy emits strategyNotYetImplemented Error and treats every source as unmatched`` () =
    let source = mkSourceUser 1 ["U"; "S1"] (Some "alice@example.com")
    let result =
        UserFkReflowPass.discover (srcs [ source ]) UserPopulation.empty BySsKey
    // Every source user unmatched.
    Assert.True (Set.contains (SourceUserId.ofInt 1) result.Value.Value.Unmatched)
    // First Diagnostic Entry is the deferred-strategy Error.
    let entries = result.Value.Entries
    Assert.NotEmpty entries
    Assert.Equal<string> ("userFkReflow.strategyNotYetImplemented", (List.head entries).Code)
    Assert.Equal (DiagnosticSeverity.Error, (List.head entries).Severity)

[<Fact>]
let ``Slice δ deferral: ManualOverride emits strategyNotYetImplemented Error`` () =
    let source = mkSourceUser 1 ["U"; "S1"] (Some "alice@example.com")
    let result =
        UserFkReflowPass.discover (srcs [ source ]) UserPopulation.empty (ManualOverride Map.empty)
    Assert.Equal<string>
        ("userFkReflow.strategyNotYetImplemented",
         (List.head result.Value.Entries).Code)

[<Fact>]
let ``Slice δ deferral: FallbackToSystemUser emits strategyNotYetImplemented Error`` () =
    let source = mkSourceUser 1 ["U"; "S1"] (Some "alice@example.com")
    let result =
        UserFkReflowPass.discover
            (srcs [ source ]) UserPopulation.empty
            (FallbackToSystemUser (TargetUserId.ofInt 999, ByEmail))
    Assert.Equal<string>
        ("userFkReflow.strategyNotYetImplemented",
         (List.head result.Value.Entries).Code)
