module Projection.Tests.CanaryResidualTests

open Xunit
open Projection.Core
open Projection.Pipeline

// ---------------------------------------------------------------------------
// The tolerance-residual canary coupling (closes the NM-32 / NM-33 provenance
// FLAG). A round-trip that invokes a known tolerance records that tolerance in
// the residual and surfaces it in the Model Fidelity Report's ACCEPTED
// DIVERGENCES section + the recorded Episode's `Tolerances`; a clean
// round-trip records none (resolves to `Tolerance.strict`, the
// strict-comparison report line).
//
// The collector (`CanaryResidual`) is the seam the canary threads through its
// per-comparison observation; `Tolerance.matchedResidual` intersects the
// observed firings with the run's configured tolerance (the accepted-AND-fired
// residual); `ModelFidelity.withAcceptedDivergences` + `Episode.withProvenance`
// are the hooks the run boundary feeds. These tests pin the wiring end-to-end
// at the canary / episode / report layer.
//
// WP-3 (F11): the empty-text → NULL erasure these tests originally used as
// their example divergence is RETIRED (the option-grain cell carriers keep
// `''` and NULL distinct end-to-end), along with its per-cell detector
// (`observeCell`). The wiring is exercised through the surviving
// representational tolerances instead (`CharAnsiPaddingTolerated` /
// `DecimalScaleTolerated` — AC-D6).
// ---------------------------------------------------------------------------

let private mustOk r = match r with Ok v -> v | Error es -> failwithf "fixture: %A" es

// A configured tolerance that accepts the char-padding erasure (a DEV-tier
// quotient that admits a data-plane representational erasure).
let private configuredTolerance : Tolerance =
    Tolerance.ofSet (Set.ofList [ ToleratedDivergence.CharAnsiPaddingTolerated ])

// ---------------------------------------------------------------------------
// CanaryResidual collector — the observed-divergence accumulator.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Canary residual: the empty collector is clean and resolves to strict`` () =
    Assert.True(CanaryResidual.isClean CanaryResidual.empty)
    let resolved = CanaryResidual.resolve configuredTolerance CanaryResidual.empty
    Assert.True(Tolerance.isStrict resolved)

[<Fact>]
let ``Canary residual: recording an observed divergence is idempotent`` () =
    let collector =
        CanaryResidual.empty
        |> CanaryResidual.record ToleratedDivergence.CharAnsiPaddingTolerated
        |> CanaryResidual.record ToleratedDivergence.CharAnsiPaddingTolerated
    Assert.False(CanaryResidual.isClean collector)
    Assert.Equal<Set<ToleratedDivergence>>(
        Set.ofList [ ToleratedDivergence.CharAnsiPaddingTolerated ],
        CanaryResidual.observed collector)

[<Fact>]
let ``Canary residual: a divergence that fired but is NOT configured-tolerated is excluded (it would block)`` () =
    // The canary observed a decimal-scale firing, but the configured tolerance
    // accepts only the char-padding erasure — so the scale divergence is NOT
    // part of the residual (a real run would FAIL the canary on it).
    let collector =
        CanaryResidual.empty
        |> CanaryResidual.record ToleratedDivergence.CharAnsiPaddingTolerated
        |> CanaryResidual.record ToleratedDivergence.DecimalScaleTolerated
    let residual = CanaryResidual.resolve configuredTolerance collector |> Tolerance.divergences
    Assert.Equal<Set<ToleratedDivergence>>(
        Set.ofList [ ToleratedDivergence.CharAnsiPaddingTolerated ],
        residual)

// ---------------------------------------------------------------------------
// Coupling into the Model Fidelity Report (ACCEPTED DIVERGENCES section).
// ---------------------------------------------------------------------------

[<Fact>]
let ``Canary residual: a fired tolerance surfaces in the report's accepted-divergences section`` () =
    let collector = CanaryResidual.empty |> CanaryResidual.record ToleratedDivergence.CharAnsiPaddingTolerated
    let residual = CanaryResidual.resolvedDivergences configuredTolerance collector
    let report =
        ModelFidelity.empty "ACME"
        |> ModelFidelity.withAcceptedDivergences residual
    match report.AcceptedDivergences with
    | [ d ] -> Assert.Equal(ToleratedDivergence.CharAnsiPaddingTolerated, d.Divergence)
    | other -> Assert.Fail(sprintf "expected one accepted divergence, got %A" other)
    // The rendered text names the fired tolerance, not the strict line.
    let lines = ModelFidelity.render report
    Assert.Contains(lines, fun (l: string) -> l.Contains "ACCEPTED DIVERGENCES (tolerances fired this run)")
    Assert.Contains(lines, fun (l: string) -> l.Contains "CharAnsiPaddingTolerated")

[<Fact>]
let ``Canary residual: a clean round-trip leaves the report's accepted-divergences section empty (strict)`` () =
    let residual = CanaryResidual.resolvedDivergences configuredTolerance CanaryResidual.empty
    let report =
        ModelFidelity.empty "ACME"
        |> ModelFidelity.withAcceptedDivergences residual
    Assert.Empty(report.AcceptedDivergences)
    let lines = ModelFidelity.render report
    Assert.Contains(lines, fun (l: string) -> l.Contains "no tolerance fired this run; the comparison is strict")

// ---------------------------------------------------------------------------
// Coupling into the recorded Episode (the tolerance-residual half of
// Episode.withProvenance — replacing the Tolerance.strict placeholder for a
// canary-coupled run).
// ---------------------------------------------------------------------------

let private genesisEpisode () : Episode =
    let version = Version.create 0 "1.0.0" |> mustOk
    let at = System.DateTimeOffset(2026, 6, 14, 0, 0, 0, System.TimeSpan.Zero)
    let coordinate = EpisodeCoordinate.create version Environment.Dev at
    Episode.ofSchema coordinate (Catalog.create [] [] |> mustOk)

[<Fact>]
let ``Canary residual: a fired tolerance is carried onto the episode's Tolerances via withProvenance`` () =
    let collector = CanaryResidual.empty |> CanaryResidual.record ToleratedDivergence.CharAnsiPaddingTolerated
    let residual = CanaryResidual.resolve configuredTolerance collector
    let episode = genesisEpisode () |> Episode.withProvenance residual []
    Assert.False(Tolerance.isStrict episode.Tolerances)
    Assert.True(Tolerance.tolerates ToleratedDivergence.CharAnsiPaddingTolerated episode.Tolerances)

[<Fact>]
let ``Canary residual: a clean round-trip records Tolerance.strict on the episode (the genesis placeholder is honest)`` () =
    let residual = CanaryResidual.resolve configuredTolerance CanaryResidual.empty
    let episode = genesisEpisode () |> Episode.withProvenance residual []
    Assert.True(Tolerance.isStrict episode.Tolerances)
