module Projection.Tests.ApplyRunbookEmitterTests

open Xunit
open Projection.Core
open Projection.Targets.SSDT
open Projection.Tests.IRBuilders
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// The cutover apply-runbook emitter (`ApplyRunbookEmitter` — wave B6 / ideation
// §12 F7). The laws under test:
//   - the runbook is a PROJECTION of the manifest — the deploy ORDER is the
//     manifest's FK-safe deployment batches, each rendered as a numbered step
//     whose precondition names the batch before it;
//   - the four phases (schema → tables → data/post-deploy → verify) render in
//     order, with the verify step pointing at `check fidelity`;
//   - the step numbers are contiguous across a variable batch count;
//   - the render is DETERMINISTIC (the bundle-equality law the pipeline pins).
// ---------------------------------------------------------------------------

let private manifestWithBatches (batches: SsKey list list) : ManifestEmitter.Manifest =
    { ManifestEmitter.build sampleCatalog with DeploymentBatches = batches }

[<Fact>]
let ``apply runbook: the four phases render in order and the verify step names check fidelity`` () =
    let runbook = ApplyRunbookEmitter.render 2 (manifestWithBatches [ [ kindKey ["Customer"] ] ])
    Assert.Contains("# Apply runbook", runbook)
    let phase1 = runbook.IndexOf "## Phase 1 — Schema"
    let phase2 = runbook.IndexOf "## Phase 2 — Tables"
    let phase3 = runbook.IndexOf "## Phase 3 — Data and post-deploy"
    let phase4 = runbook.IndexOf "## Phase 4 — Verify"
    Assert.True(phase1 >= 0 && phase1 < phase2 && phase2 < phase3 && phase3 < phase4, "the four phases render in order")
    Assert.Contains("check fidelity", runbook)
    Assert.Contains("2 schema(s)", runbook)

[<Fact>]
let ``apply runbook: the deployment batches render in dependency order, each precondition naming the batch before it`` () =
    // Two FK-safe batches: Customer (no deps) then Order (depends on batch 1).
    let runbook = ApplyRunbookEmitter.render 1 (manifestWithBatches [ [ kindKey ["Customer"] ]; [ kindKey ["Order"] ] ])
    let b1 = runbook.IndexOf "Batch 1"
    let b2 = runbook.IndexOf "Batch 2"
    Assert.True(b1 >= 0 && b1 < b2, "batch 1 renders before batch 2 (dependency order)")
    Assert.Contains("Customer", runbook)
    Assert.Contains("Order", runbook)
    // Batch 1 stands on Phase 1; batch 2 stands on batch 1 (the FK-safe closure).
    Assert.Contains("Precondition: Phase 1 applied.", runbook)
    Assert.Contains("Precondition: batch 1 applied.", runbook)

[<Fact>]
let ``apply runbook: step numbers are contiguous across the batch count`` () =
    // 2 batches ⇒ Phase 2 is steps 2,3; Phase 3 is steps 4,5,6; Phase 4 is step 7.
    let runbook = ApplyRunbookEmitter.render 1 (manifestWithBatches [ [ kindKey ["Customer"] ]; [ kindKey ["Order"] ] ])
    for n in [ "1."; "2."; "3."; "4."; "5."; "6."; "7." ] do
        Assert.Contains(n, runbook)
    // The verify step is the last (step 7 with two batches).
    Assert.Contains("7. `projection check fidelity", runbook)

[<Fact>]
let ``apply runbook: no topological batches falls back to a single tables step`` () =
    let runbook = ApplyRunbookEmitter.render 1 (manifestWithBatches [])
    Assert.Contains("2. Apply the", runbook)
    // Phase 3 then starts at step 3 (one fallback tables step).
    Assert.Contains("3. Static seeds", runbook)

[<Fact>]
let ``apply runbook: the render is deterministic (the bundle-equality law)`` () =
    let m = manifestWithBatches [ [ kindKey ["Customer"] ]; [ kindKey ["Order"] ] ]
    Assert.Equal(ApplyRunbookEmitter.render 3 m, ApplyRunbookEmitter.render 3 m)
