module Projection.Tests.UserFkReflowPropertyTests

// Chapter 5.13 slice identity-axis-closure — property surface for
// `UserFkReflowPass.discover`. Closes matrix row 174's
// "🟢 PARITY (partial)" claim by exercising the four strategy
// variants under FsCheck-generated populations and pinning the
// load-bearing axioms structurally.
//
// Per Phase 8 acceptance criterion (CUTOVER_READINESS_BRIEF Section 4
// blocker #2): "Property test asserting symmetry of matched +
// unmatched diagnostics on shared fixtures." This file is the
// canonical cash-out — five properties + per-strategy coverage
// across FsCheck-generated UserPopulations.
//
// **What "symmetry" means here.** Per pillar 9 + A41 (totality):
//
//   (S1) Strategy totality — for every variant, every source user
//        in the input population appears in exactly one of
//        `Mapping.Keys` or `Unmatched`. The disjoint partition is
//        already enforced by `UserRemapContext.create`'s smart
//        constructor; this property asserts the algorithm produces
//        an EXHAUSTIVE partition (`Mapping.Keys ∪ Unmatched =
//        source population's Ids`).
//
//   (S2) Per-source diagnostic count — every unmatched source emits
//        exactly one Warning entry; every matched source emits zero
//        Warning entries. The count invariants are the canary's
//        primary triage signal under R6 (operator counts the
//        unmatched diagnostics directly from the Warning stream).
//
//   (S3) Diagnostics count = Unmatched.Count — the diagnostic-trail
//        cardinality matches the unmatched cardinality. Per pre-scope
//        §6: "every unmatched user produces exactly one Warning."
//
//   (S4) Permutation invariance — source-list ordering does not
//        affect the produced `UserRemapContext`. (Existing example
//        test reinforced as FsCheck property.)
//
//   (S5) Idempotence / T1 byte-determinism — repeated `discover`
//        on identical inputs produces equal output.
//
// **FallbackToSystemUser safety net** carries an additional property:
// `Set.isEmpty Unmatched` structurally (per pre-scope §3 slice 3).
// The fallback catches every miss; the property pins this as a
// structural commitment regardless of primary-strategy outcome.

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Core.Passes

// ----------------------------------------------------------------
// Generator: deterministic synthetic populations.
// FsCheck's Arb defaults produce too-wild inputs (negative ids,
// surrogate-pair emails). We hand-shape a generator producing
// realistic shapes deterministically derived from a positive-int
// seed — small populations (0..20 users) so the algorithm runs in
// bounded time even under FsCheck's default 100 sample sweep.
// ----------------------------------------------------------------

let private mustOk r =
    match r with
    | Ok v -> v
    | Error _ -> invalidOp "fixture"

let private mkSsKey (parts: string list) : SsKey =
    SsKey.synthesizedComposite "OS_PROP" parts |> mustOk

let private mkEmail (raw: string) : Email =
    Email.create raw |> mustOk

let private mkSourceUser (id: int) (email: string option) : UserAttributes<SourceUserId> =
    UserAttributes.create
        (SourceUserId.ofInt id)
        (mkSsKey [ "S"; sprintf "%d" id ])
        (email |> Option.map mkEmail)

let private mkTargetUser (id: int) (email: string option) : UserAttributes<TargetUserId> =
    UserAttributes.create
        (TargetUserId.ofInt id)
        (mkSsKey [ "T"; sprintf "%d" id ])
        (email |> Option.map mkEmail)

/// Synthetic fixture: a `PopulationPair` carries co-generated
/// source + target populations where target ids overlap source ids
/// at known positions so the strategies have realistic hit-rate
/// distributions.
type PopulationPair =
    {
        Source : UserPopulation<SourceUserId>
        Target : UserPopulation<TargetUserId>
    }

/// Generator: 0..20 source users + 0..20 target users; each user
/// has an email with 50% probability. Target ids overlap source
/// ids by intersection-of-positive-int-id-ranges so non-empty
/// matches are realistic.
let private populationPairGen : Gen<PopulationPair> =
    gen {
        let! srcCount = Gen.choose (0, 12)
        let! tgtCount = Gen.choose (0, 12)
        let! srcIds = Gen.choose (1, 50) |> Gen.listOfLength srcCount
        let! tgtIds = Gen.choose (1, 50) |> Gen.listOfLength tgtCount
        let! srcEmailIdx = Gen.choose (0, 1) |> Gen.listOfLength srcCount
        let! tgtEmailIdx = Gen.choose (0, 1) |> Gen.listOfLength tgtCount
        let emailFor i = if i = 0 then None else Some (sprintf "u%d@example.com" i)
        let srcUsers =
            srcIds
            |> List.distinct
            |> List.mapi (fun ix id ->
                let email =
                    if ix < List.length srcEmailIdx && srcEmailIdx.[ix] = 1 then
                        emailFor id
                    else None
                mkSourceUser id email)
        let tgtUsers =
            tgtIds
            |> List.distinct
            |> List.mapi (fun ix id ->
                let email =
                    if ix < List.length tgtEmailIdx && tgtEmailIdx.[ix] = 1 then
                        emailFor id
                    else None
                mkTargetUser id email)
        return
            { Source = UserPopulation.create srcUsers
              Target = UserPopulation.create tgtUsers }
    }

type Generators =
    static member PopulationPair () = Arb.fromGen populationPairGen

let private propertyCfg = Config.QuickThrowOnFailure
let private withGenerators () =
    Arb.register<Generators>() |> ignore

// ----------------------------------------------------------------
// Property: strategy totality (S1) — every source user appears in
// exactly one of (Mapping.Keys, Unmatched). The strategies under
// test:
//   - ByEmail
//   - BySsKey
//   - ManualOverride (empty map; every source unmatched)
//   - FallbackToSystemUser (ByEmail + system fallback; every
//     source matched)
// ----------------------------------------------------------------

let private totalityHolds (pop: PopulationPair) (strategy: UserMatchingStrategy) : bool =
    let result = UserFkReflowPass.discover pop.Source pop.Target strategy
    let ctx = result.Value.Value
    let sourceIds =
        pop.Source.Users
        |> List.map (fun u -> u.Id)
        |> Set.ofList
    let mappingKeys =
        ctx.Mapping |> Map.toSeq |> Seq.map fst |> Set.ofSeq
    let observedPartition = Set.union mappingKeys ctx.Unmatched
    let disjoint = Set.isEmpty (Set.intersect mappingKeys ctx.Unmatched)
    let exhaustive = observedPartition = sourceIds
    disjoint && exhaustive

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``S1 totality (ByEmail): source = Mapping.Keys ⊎ Unmatched`` (pop: PopulationPair) : bool =
    totalityHolds pop ByEmail

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``S1 totality (BySsKey): source = Mapping.Keys ⊎ Unmatched`` (pop: PopulationPair) : bool =
    totalityHolds pop BySsKey

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``S1 totality (ManualOverride Map.empty): every source is unmatched`` (pop: PopulationPair) : bool =
    let result =
        UserFkReflowPass.discover pop.Source pop.Target (ManualOverride Map.empty)
    let ctx = result.Value.Value
    let sourceIds =
        pop.Source.Users |> List.map (fun u -> u.Id) |> Set.ofList
    Map.isEmpty ctx.Mapping && ctx.Unmatched = sourceIds

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``S1 totality (FallbackToSystemUser ByEmail): every source matched (safety net)`` (pop: PopulationPair) : bool =
    // Synthesize a fallback target id; pick any target id from
    // population, or 999 if the target population is empty.
    let fallbackId =
        match pop.Target.Users with
        | [] -> TargetUserId.ofInt 999
        | u :: _ -> u.Id
    let strategy = FallbackToSystemUser (fallbackId, ByEmail)
    let result =
        UserFkReflowPass.discover pop.Source pop.Target strategy
    let ctx = result.Value.Value
    let sourceIds =
        pop.Source.Users |> List.map (fun u -> u.Id) |> Set.ofList
    let mappingKeys =
        ctx.Mapping |> Map.toSeq |> Seq.map fst |> Set.ofSeq
    Set.isEmpty ctx.Unmatched && mappingKeys = sourceIds

// ----------------------------------------------------------------
// Property: S3 diagnostic count = Unmatched.Count. Per pre-scope §6.
// ----------------------------------------------------------------

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``S3 diagnostics: count of Warning diagnostics = Unmatched.Count (ByEmail)`` (pop: PopulationPair) : bool =
    let result = UserFkReflowPass.discover pop.Source pop.Target ByEmail
    let ctx = result.Value.Value
    let warningCount =
        result.Value.Entries
        |> List.filter (fun e -> e.Severity = DiagnosticSeverity.Warning)
        |> List.length
    let unmatchedCount = Set.count ctx.Unmatched
    warningCount = unmatchedCount && ctx.Diagnostics.Length = unmatchedCount

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``S3 diagnostics: count holds under BySsKey`` (pop: PopulationPair) : bool =
    let result = UserFkReflowPass.discover pop.Source pop.Target BySsKey
    let ctx = result.Value.Value
    let warningCount =
        result.Value.Entries
        |> List.filter (fun e -> e.Severity = DiagnosticSeverity.Warning)
        |> List.length
    warningCount = Set.count ctx.Unmatched

// ----------------------------------------------------------------
// Property: S4 permutation invariance — source-list ordering does
// not affect the output context.
// ----------------------------------------------------------------

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``S4 permutation invariance: discover output is order-independent on source list (ByEmail)`` (pop: PopulationPair) : bool =
    let shuffled =
        UserPopulation.create (pop.Source.Users |> List.rev)
    let r1 = (UserFkReflowPass.discover pop.Source pop.Target ByEmail).Value.Value
    let r2 = (UserFkReflowPass.discover shuffled pop.Target ByEmail).Value.Value
    r1.Mapping = r2.Mapping &&
    r1.Unmatched = r2.Unmatched &&
    (List.sort r1.Diagnostics) = (List.sort r2.Diagnostics)

// ----------------------------------------------------------------
// Property: S5 idempotence / T1 byte-determinism — running
// `discover` twice produces equal output.
// ----------------------------------------------------------------

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``S5 idempotence (ByEmail): repeated discover produces equal output`` (pop: PopulationPair) : bool =
    let r1 = UserFkReflowPass.discover pop.Source pop.Target ByEmail
    let r2 = UserFkReflowPass.discover pop.Source pop.Target ByEmail
    r1.Value.Value.Mapping = r2.Value.Value.Mapping &&
    r1.Value.Value.Unmatched = r2.Value.Value.Unmatched &&
    r1.Trail = r2.Trail

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``S5 idempotence (BySsKey): repeated discover produces equal output`` (pop: PopulationPair) : bool =
    let r1 = UserFkReflowPass.discover pop.Source pop.Target BySsKey
    let r2 = UserFkReflowPass.discover pop.Source pop.Target BySsKey
    r1.Value.Value.Mapping = r2.Value.Value.Mapping &&
    r1.Value.Value.Unmatched = r2.Value.Value.Unmatched

// ----------------------------------------------------------------
// Property: S2 per-source diagnostic count — every matched source
// emits zero Warnings; every unmatched source emits exactly one.
// Asserted via the Annotated event count + Warning count split.
// ----------------------------------------------------------------

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``S2 per-source diagnostic count: matched → 0 Warnings; unmatched → 1 Warning`` (pop: PopulationPair) : bool =
    let result = UserFkReflowPass.discover pop.Source pop.Target ByEmail
    let ctx = result.Value.Value
    let annotatedCount = List.length result.Trail
    let warningCount =
        result.Value.Entries
        |> List.filter (fun e -> e.Severity = DiagnosticSeverity.Warning)
        |> List.length
    annotatedCount = Map.count ctx.Mapping &&
    warningCount = Set.count ctx.Unmatched

// ----------------------------------------------------------------
// FallbackToSystemUser-specific safety-net property (S1 corollary)
// at example scale across multiple primary strategies.
// ----------------------------------------------------------------

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``FallbackToSystemUser safety net: Unmatched is empty under primary=ByEmail`` (pop: PopulationPair) : bool =
    let fallbackId =
        match pop.Target.Users with
        | [] -> TargetUserId.ofInt 999
        | u :: _ -> u.Id
    let r =
        UserFkReflowPass.discover
            pop.Source pop.Target
            (FallbackToSystemUser (fallbackId, ByEmail))
    Set.isEmpty r.Value.Value.Unmatched

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``FallbackToSystemUser safety net: Unmatched is empty under primary=BySsKey`` (pop: PopulationPair) : bool =
    let fallbackId =
        match pop.Target.Users with
        | [] -> TargetUserId.ofInt 999
        | u :: _ -> u.Id
    let r =
        UserFkReflowPass.discover
            pop.Source pop.Target
            (FallbackToSystemUser (fallbackId, BySsKey))
    Set.isEmpty r.Value.Value.Unmatched

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``FallbackToSystemUser safety net: Unmatched is empty under primary=ManualOverride empty`` (pop: PopulationPair) : bool =
    let fallbackId =
        match pop.Target.Users with
        | [] -> TargetUserId.ofInt 999
        | u :: _ -> u.Id
    let r =
        UserFkReflowPass.discover
            pop.Source pop.Target
            (FallbackToSystemUser (fallbackId, ManualOverride Map.empty))
    Set.isEmpty r.Value.Value.Unmatched

// ----------------------------------------------------------------
// Bootstrapping (xunit collection) — register FsCheck arb on the
// first test invocation by referencing the generator type.
// ----------------------------------------------------------------

[<Fact>]
let ``5.13.identity-axis-closure: FsCheck arb registers PopulationPair generator`` () =
    withGenerators ()
    Check.One(propertyCfg, fun (pop: PopulationPair) -> totalityHolds pop ByEmail)
    Assert.True(true)
