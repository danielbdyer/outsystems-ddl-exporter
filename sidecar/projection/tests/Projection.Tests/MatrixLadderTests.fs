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
let ``D1: the generated matrix reports Schema=L3 — every Schema OpenGap is retired (the schema-L3 program's flip)`` () =
    // THE FLIP (schema-L3.3b, 2026-08-30). This pin spent its life naming
    // Schema's CURRENT open tolerance (IndexOptionsUnreflected →
    // CompositePkFkUnreflected → TriggerBodyUnparsedDropped as each
    // closed); with the last retirement the generator auto-flips the axis
    // and this test now asserts the L3 state POSITIVELY. The three closure
    // witnesses: `IndexRoundtripTests` (options), `CanaryRoundTripTests` +
    // `PhysicalSchemaForeignKeyTests` (composite legs),
    // `ComposeEmitRefusalTests` (the named trigger refusal).
    let schema = rowFor "Schema"
    Assert.Contains("✅ L3", schema)
    Assert.DoesNotContain("L2-partial", schema)
    Assert.DoesNotContain("IndexOptionsUnreflected", schema)
    Assert.DoesNotContain("CompositePkFkUnreflected", schema)
    Assert.DoesNotContain("TriggerBodyUnparsedDropped", schema)

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
let ``D1: ZERO tolerances are open fidelity gaps today`` () =
    // The matrix's open-gap count is the codebase's named schema-fidelity debt.
    // Pinning it makes a silently-added OpenGap — or a silently-retired one
    // without regenerating — fail here. NM-16 (2026-06-13) added four kind-facet
    // diff-erasure tolerances (KindTriggers / KindChecks / KindModality /
    // KindActivation UnreflectedInDiff), all Schema OpenGap, joining
    // IndexOptionsUnreflected — so the count moved 1 → 5. NM-28 (2026-06-14)
    // added CompositePkFkUnreflected (Schema OpenGap) → 6. NM-17 (2026-06-14)
    // RETIRED the four kind-facet OpenGaps by building the real `KindFacet`
    // diff channel → back to 2 (IndexOptionsUnreflected + CompositePkFkUnreflected).
    // M1′ (THE VECTOR Wave 0, 2026-06-15) added TriggerBodyUnparsedDropped (Schema
    // OpenGap) + FkTrustUnreflected + UniquePromotionUnreflected (Decision OpenGap)
    // → 5. M1 (THE VECTOR Wave 1, 2026-06-15) RETIRED the two Decision OpenGaps by
    // routing FK-trust / unique-promotion through the general comparator → back to
    // 3 (IndexOptionsUnreflected + CompositePkFkUnreflected + TriggerBodyUnparsedDropped,
    // all Schema OpenGap; the Decision axis is now faithful). schema-L3.1
    // (2026-08-30) RETIRED IndexOptionsUnreflected by recovering the full
    // index option surface through ReadSide + the widened PhysicalIndex →
    // back to 2 (CompositePkFkUnreflected + TriggerBodyUnparsedDropped).
    // schema-L3.2 (2026-08-30) RETIRED CompositePkFkUnreflected via the
    // Reference.Legs lift → 1 (TriggerBodyUnparsedDropped alone).
    // schema-L3.3b (2026-08-30) RETIRED TriggerBodyUnparsedDropped via the
    // named compose-seam refusal → 0. Every remaining tolerance is
    // AcceptedFaithful; the ladder reads L1/L2/L3 = 5/5/5.
    Assert.Contains("0 open", generatedMatrix)
