module Projection.Tests.MatrixLadderTests

open System.IO
open Xunit
open Projection.Core

// ---------------------------------------------------------------------------
// D1 — the self-verification meta-cell (EXECUTION_PLAN 6.E.1; debrief cluster D).
//
// `scripts/matrix-status.sh` derives the round-trip *ladder* (L1 witness / L2
// faithful / L3 composed) per axis from the proof — the test tree's witness
// names and `Tolerance.fs`'s `@ladder` tags — and writes
// `NORTH_STAR.matrix.generated.md`. These tests pin the generator's keystone
// behaviour: an axis carrying an `OpenGap` tolerance is reported L2-partial and
// NAMES the tolerance, while an axis with only accepted tolerances reaches L3.
//
// The honesty mechanism (verified at the script level + here): a cell cannot be
// hand-marked. L2 flips to faithful only when the `OpenGap` variant is retired
// from `Tolerance.fs` — so the matrix tracks the codebase's true distance to
// the bullseye. These tests read the committed generated file; CI also checks it
// is current (`scripts/matrix-status.sh` produces no git diff), so the two
// together enforce both "the generator computes the right ladder" and "the
// committed matrix reflects it."
// ---------------------------------------------------------------------------

let private generatedMatrix : string =
    // Walk up from the running test assembly to the projection root and read the
    // generated matrix. findUp tolerates any build depth (Debug/Release, net9.0).
    let rec findUp (dir: DirectoryInfo option) : string option =
        match dir with
        | None -> None
        | Some d ->
            let candidate = Path.Combine(d.FullName, "NORTH_STAR.matrix.generated.md")
            if File.Exists candidate then Some candidate
            else findUp (Option.ofObj d.Parent)
    let start =
        System.Reflection.Assembly.GetExecutingAssembly().Location
        |> Path.GetDirectoryName
        |> Option.ofObj
        |> Option.map (fun d -> DirectoryInfo d)
    match findUp start with
    | Some path -> File.ReadAllText path
    | None ->
        // Fail loud rather than skip: the generated file is committed at the
        // projection root, so its absence is a real regression, not an
        // environment gap.
        failwith "NORTH_STAR.matrix.generated.md not found above the test assembly — regenerate via scripts/matrix-status.sh"

let private rowFor (axis: string) : string =
    generatedMatrix.Split('\n')
    |> Array.tryFind (fun line -> line.Contains(sprintf "**%s**" axis))
    |> function
        | Some row -> row
        | None -> failwithf "no ladder row for axis %s in the generated matrix" axis

[<Fact>]
let ``D1: the generated matrix reports Schema=L2-partial because IndexOptionsUnreflected is an open tolerance`` () =
    // IndexOptionsUnreflected is a live, open fidelity gap: after E1 the index
    // *structure* round-trips (compared in PhysicalSchema.Indexes), but index
    // *options* (filter / included columns / storage flags) are recovered by
    // neither side, so the Schema round-trip is not yet fully faithful. The
    // generator must surface this residual, by name, at the ladder.
    Assert.Contains(ToleratedDivergence.IndexOptionsUnreflected, ToleratedDivergence.allKnown)
    let schema = rowFor "Schema"
    Assert.Contains("L2-partial", schema)
    Assert.Contains("IndexOptionsUnreflected", schema)

[<Fact>]
let ``D1: an axis with only accepted tolerances reaches L3 (the generator discriminates)`` () =
    // Discriminating control: a generator that marked every axis partial would
    // pass the Schema test above. Data carries only AcceptedFaithful tolerances,
    // so it must reach the composed rung — proving the ladder is computed, not
    // blanket-pessimistic.
    let data = rowFor "Data"
    Assert.Contains("faithful", data)
    Assert.Contains("L3", data)
    Assert.DoesNotContain("L2-partial", data)

[<Fact>]
let ``D1: exactly two tolerances are open fidelity gaps today`` () =
    // The matrix's open-gap count is the codebase's named schema-fidelity debt.
    // Pinning it makes a silently-added OpenGap — or a silently-retired one
    // without regenerating — fail here. NM-16 (2026-06-13) added four kind-facet
    // diff-erasure tolerances (KindTriggers / KindChecks / KindModality /
    // KindActivation UnreflectedInDiff), all Schema OpenGap, joining
    // IndexOptionsUnreflected — so the count moved 1 → 5. NM-28 (2026-06-14)
    // added CompositePkFkUnreflected (Schema OpenGap) → 6. NM-17 (2026-06-14)
    // RETIRED the four kind-facet OpenGaps by building the real `KindFacet`
    // diff channel → back to 2 (IndexOptionsUnreflected + CompositePkFkUnreflected).
    Assert.Contains("2 open", generatedMatrix)
