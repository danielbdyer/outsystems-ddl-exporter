module Projection.Tests.StrategyRegistrationsTests

open Xunit
open Projection.Core

// ---------------------------------------------------------------------------
// Chapter A.4.7 slice ε — strategy registration witnesses.
//
// Slice ε ships `StrategyRegistrations` module with five strategy
// `RegisteredTransformMetadata` values (NullabilityRules,
// UniqueIndexRules, ForeignKeyRules, CategoricalUniquenessRules,
// CycleResolution). The first four classify as `OperatorIntent
// Tightening` (sub-strategies of the registered-intervention
// passes); CycleResolution classifies as `DataIntent` (algorithmic
// cycle-handling; the Ordering operator-intent surface is captured
// separately at TopologicalOrderPass.registered's selfLoopHandling
// site).
//
// Per the chapter A.4.7 open's anti-scope clause: registry
// classification is at strategy-level (one Site per strategy), not
// sub-strategy fanOut level (per-KeepReason within a strategy).
// ---------------------------------------------------------------------------

[<Fact>]
let ``A.4.7 slice ε: StrategyRegistrations.nullabilityRules classifies as OperatorIntent Tightening`` () =
    let rt = StrategyRegistrations.nullabilityRules
    Assert.Equal("nullabilityRules", rt.Name)
    Assert.Equal(Data, rt.Domain)
    Assert.Equal(OperatorIntent Tightening, rt.Sites.[0].Classification)

[<Fact>]
let ``A.4.7 slice ε: StrategyRegistrations.uniqueIndexRules classifies as OperatorIntent Tightening`` () =
    let rt = StrategyRegistrations.uniqueIndexRules
    Assert.Equal(Schema, rt.Domain)
    Assert.Equal(OperatorIntent Tightening, rt.Sites.[0].Classification)

[<Fact>]
let ``A.4.7 slice ε: StrategyRegistrations.foreignKeyRules classifies as OperatorIntent Tightening`` () =
    let rt = StrategyRegistrations.foreignKeyRules
    Assert.Equal(Schema, rt.Domain)
    Assert.Equal(OperatorIntent Tightening, rt.Sites.[0].Classification)

[<Fact>]
let ``A.4.7 slice ε: StrategyRegistrations.categoricalUniquenessRules classifies as OperatorIntent Tightening`` () =
    let rt = StrategyRegistrations.categoricalUniquenessRules
    Assert.Equal(Data, rt.Domain)
    Assert.Equal(OperatorIntent Tightening, rt.Sites.[0].Classification)

[<Fact>]
let ``A.4.7 slice ε: StrategyRegistrations.cycleResolution classifies as DataIntent (algorithm-internal)`` () =
    let rt = StrategyRegistrations.cycleResolution
    Assert.Equal(CrossCutting, rt.Domain)
    Assert.Equal(DataIntent, rt.Sites.[0].Classification)

[<Fact>]
let ``A.4.7 slice ε: StrategyRegistrations.all lists 5 strategy registrations`` () =
    Assert.Equal(5, List.length StrategyRegistrations.all)

[<Fact>]
let ``A.4.7 slice ε: StrategyRegistrations.all validates through TransformRegistry.create`` () =
    match TransformRegistry.create StrategyRegistrations.all with
    | Ok entries -> Assert.Equal(5, List.length entries)
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        Assert.Fail(sprintf "Expected strategy registrations to validate; got errors: %s" codes)

[<Fact>]
let ``A.4.7 slice ε: every StrategyRegistrations entry carries non-empty Rationale`` () =
    for rt in StrategyRegistrations.all do
        Assert.NotEmpty rt.Sites
        for site in rt.Sites do
            Assert.False (System.String.IsNullOrWhiteSpace site.Rationale)
