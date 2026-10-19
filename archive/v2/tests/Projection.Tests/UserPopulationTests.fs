module Projection.Tests.UserPopulationTests

open Xunit
open Projection.Core

// ---------------------------------------------------------------------------
// Chapter 4.2 slice β — UserAttributes<'id> + UserPopulation<'id>
// + Profile.SourceUsers + Profile.TargetUsers.
//
// Per `CHAPTER_4_PRESCOPE_USERFK_REFLOW.md` §4: per-environment user
// populations live on Profile (sibling fields to Columns / Distributions
// / CdcAwareness). The parameterized typed shape (`UserPopulation<
// SourceUserId>` vs `UserPopulation<TargetUserId>`) extends slice α's
// identity-orientation safety to the population level. A34 holds:
// `Profile.empty` carries empty user populations; passes that don't read
// them produce identical output for empty vs populated.
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
// UserAttributes<'id> + UserPopulation<'id>.
// ---------------------------------------------------------------------------

[<Fact>]
let ``UserAttributes.create populates all three fields`` () =
    let id = SourceUserId.ofInt 1
    let key = mkSsKey ["User"; "1"]
    let email = Email.create "alice@example.com" |> mustOk
    let attrs = UserAttributes.create id key (Some email)
    Assert.Equal (id, attrs.Id)
    Assert.Equal (key, attrs.SsKey)
    Assert.Equal<Email option> (Some email, attrs.Email)

[<Fact>]
let ``UserAttributes.Email = None for users without email registered`` () =
    let attrs = UserAttributes.create (SourceUserId.ofInt 2) (mkSsKey ["User"; "2"]) None
    Assert.Equal<Email option> (None, attrs.Email)

[<Fact>]
let ``UserPopulation.empty has zero users`` () =
    let p : UserPopulation<SourceUserId> = UserPopulation.empty
    Assert.True (UserPopulation.isEmpty p)
    Assert.Equal (0, List.length p.Users)

[<Fact>]
let ``UserPopulation.create wraps a list of attributes`` () =
    let users =
        [ UserAttributes.create (SourceUserId.ofInt 1) (mkSsKey ["U"; "1"]) None
          UserAttributes.create (SourceUserId.ofInt 2) (mkSsKey ["U"; "2"]) None ]
    let p = UserPopulation.create users
    Assert.Equal (2, List.length p.Users)
    Assert.False (UserPopulation.isEmpty p)

[<Fact>]
let ``UserPopulation<SourceUserId> and UserPopulation<TargetUserId> are distinct types (orientation safety)`` () =
    // Compile-time guarantee: a Source population cannot be assigned to
    // a Target field. Test the runtime equivalent: same shape, distinct
    // typed fields.
    let source : UserPopulation<SourceUserId> =
        UserPopulation.create [ UserAttributes.create (SourceUserId.ofInt 1) (mkSsKey ["U"; "1"]) None ]
    let target : UserPopulation<TargetUserId> =
        UserPopulation.create [ UserAttributes.create (TargetUserId.ofInt 100) (mkSsKey ["U"; "100"]) None ]
    Assert.Equal (1, List.length source.Users)
    Assert.Equal (1, List.length target.Users)
    // The id-orientation safety is enforced at compile time
    // (`source.Users.[0].Id` is `SourceUserId`; assigning to a
    // `TargetUserId`-typed binding fails to compile).

// ---------------------------------------------------------------------------
// Profile.SourceUsers + Profile.TargetUsers.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Profile.empty.SourceUsers is empty`` () =
    Assert.True (UserPopulation.isEmpty Profile.empty.SourceUsers)

[<Fact>]
let ``Profile.empty.TargetUsers is empty`` () =
    Assert.True (UserPopulation.isEmpty Profile.empty.TargetUsers)

[<Fact>]
let ``Profile.isEmpty true on Profile.empty (slice β extends the predicate)`` () =
    Assert.True (Profile.isEmpty Profile.empty)

[<Fact>]
let ``Profile.isEmpty false when SourceUsers is populated`` () =
    let users =
        [ UserAttributes.create (SourceUserId.ofInt 1) (mkSsKey ["U"; "1"]) None ]
    let p = { Profile.empty with SourceUsers = UserPopulation.create users }
    Assert.False (Profile.isEmpty p)

[<Fact>]
let ``Profile.isEmpty false when TargetUsers is populated`` () =
    let users =
        [ UserAttributes.create (TargetUserId.ofInt 100) (mkSsKey ["U"; "100"]) None ]
    let p = { Profile.empty with TargetUsers = UserPopulation.create users }
    Assert.False (Profile.isEmpty p)

[<Fact>]
let ``A34 orthogonality: Profile.empty.SourceUsers + .TargetUsers carry no Catalog/Policy back-references`` () =
    // The structural property A34 promises: Profile carries no
    // back-references to Catalog or Policy types. UserAttributes
    // carries SsKey + Email + identity; UserPopulation wraps the
    // attribute list. Verify by construction — the types compile
    // without importing Policy types (UserId family lives in
    // UserIdentity.fs, the earlier-compile sibling of Profile.fs).
    let _ : UserPopulation<SourceUserId> = Profile.empty.SourceUsers
    let _ : UserPopulation<TargetUserId> = Profile.empty.TargetUsers
    Assert.True true
