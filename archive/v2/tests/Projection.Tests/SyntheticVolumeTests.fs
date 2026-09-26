module Projection.Tests.SyntheticVolumeTests

open Xunit
open Projection.Core
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// H-071 consumption — `SyntheticVolume.byCentrality` derives a per-kind
// `VolumeTarget` from a `CentralityRanking` so central kinds get proportionally
// more synthetic rows. The floor these tests hold:
//   - identity-in-effect when weighting is off (flat schema / zero strength /
//     empty ranking all reproduce the `scale` baseline, so σ stays byte-
//     identical to the global-Scale path);
//   - amplify-only (a peripheral kind is never shrunk below `scale`);
//   - the cap bounds the heaviest hub;
//   - determinism (T1) and monotonicity;
//   - operator `VolumeByKind` always wins the merge.
// ---------------------------------------------------------------------------

let private score (key: SsKey) (s: decimal) : CentralityScore = { SsKey = key; Score = s }

let private ranking (scores: CentralityScore list) : CentralityRanking =
    { Scores = scores; Iterations = 1 }

let private mult (t: VolumeTarget) : decimal =
    match t with
    | VolumeTarget.Multiplier f -> f
    | VolumeTarget.Absolute r -> failwithf "expected a Multiplier VolumeTarget, got Absolute %d" r

let private k (name: string) : SsKey = kindKey [ name ]

[<Fact>]
let ``byCentrality: an empty ranking yields an empty map`` () =
    let derived = SyntheticVolume.byCentrality 1M 4M 1M (ranking [])
    Assert.Empty derived

[<Fact>]
let ``byCentrality: a flat ranking reproduces the scale baseline for every kind (identity to Scale)`` () =
    // All scores equal ⇒ every relative = 1 ⇒ factor = 1 ⇒ Multiplier (scale × 1).
    // With scale = 2, that is exactly `observed × Scale` — byte-identical to the
    // global-Scale path in SyntheticData.rowCountFor.
    let scale = 2M
    let derived =
        ranking [ score (k "A") 0.25M; score (k "B") 0.25M; score (k "C") 0.25M; score (k "D") 0.25M ]
        |> SyntheticVolume.byCentrality 1M 4M scale
    Assert.All(derived |> Map.toList, fun (_, v) -> Assert.Equal(scale, mult v))

[<Fact>]
let ``byCentrality: zero strength holds the scale baseline even for a central kind`` () =
    let scale = 1M
    let derived =
        ranking [ score (k "Hub") 0.7M; score (k "Leaf") 0.1M ]
        |> SyntheticVolume.byCentrality 0M 4M scale
    Assert.Equal(scale, mult derived.[k "Hub"])
    Assert.Equal(scale, mult derived.[k "Leaf"])

[<Fact>]
let ``byCentrality: a central kind is boosted while a peripheral kind holds the baseline`` () =
    // scores mean = 0.25. Hub at 0.4 (relative 1.6) boosts; Leaf at 0.1
    // (relative 0.4, below mean) is clamped to the scale baseline — amplify-only.
    let scale = 1M
    let derived =
        ranking [ score (k "Hub") 0.4M; score (k "Mid") 0.3M; score (k "Sat") 0.2M; score (k "Leaf") 0.1M ]
        |> SyntheticVolume.byCentrality 1M 4M scale
    Assert.True(mult derived.[k "Hub"] > scale, "central kind should be boosted above the baseline")
    Assert.Equal(scale, mult derived.[k "Leaf"])       // relative < 1 ⇒ never below baseline
    Assert.True(mult derived.[k "Hub"] > mult derived.[k "Mid"])

[<Fact>]
let ``byCentrality: the boost is capped at maxFactor × scale`` () =
    // Hub relative = 1.6 ⇒ uncapped factor 1.6; maxFactor 1.5 clamps it.
    let scale = 1M
    let maxFactor = 1.5M
    let derived =
        ranking [ score (k "Hub") 0.4M; score (k "B") 0.3M; score (k "C") 0.2M; score (k "D") 0.1M ]
        |> SyntheticVolume.byCentrality 1M maxFactor scale
    Assert.Equal(scale * maxFactor, mult derived.[k "Hub"])

[<Fact>]
let ``byCentrality: determinism — the same ranking yields the same map`` () =
    let r = ranking [ score (k "A") 0.5M; score (k "B") 0.3M; score (k "C") 0.2M ]
    let a = SyntheticVolume.byCentrality 1.5M 4M 1M r
    let b = SyntheticVolume.byCentrality 1.5M 4M 1M r
    Assert.Equal<Map<SsKey, VolumeTarget>>(a, b)

[<Fact>]
let ``byCentrality: monotonic — a higher score never yields a smaller multiplier`` () =
    let scores = [ score (k "A") 0.5M; score (k "B") 0.3M; score (k "C") 0.15M; score (k "D") 0.05M ]
    let derived = SyntheticVolume.byCentrality 2M 8M 1M (ranking scores)
    // scores already Score-desc; multipliers must be non-increasing in the same order.
    let mults = scores |> List.map (fun s -> mult derived.[s.SsKey])
    mults
    |> List.pairwise
    |> List.iter (fun (hi, lo) -> Assert.True(hi >= lo, "a higher centrality score must not map to a smaller multiplier"))

[<Fact>]
let ``mergeUnderOperator: an operator entry wins; derived fills only the gaps`` () =
    let operator = Map.ofList [ k "Pinned", VolumeTarget.Absolute 500 ]
    let derived =
        Map.ofList
            [ k "Pinned", VolumeTarget.Multiplier 3M      // must be ignored — operator pinned it
              k "Derived", VolumeTarget.Multiplier 2M ]   // must survive — operator left it unset
    let merged = SyntheticVolume.mergeUnderOperator operator derived
    Assert.Equal(VolumeTarget.Absolute 500, merged.[k "Pinned"])
    Assert.Equal(VolumeTarget.Multiplier 2M, merged.[k "Derived"])
