module Projection.Tests.EstateBoardViewTests

// The estate board's RICH lens (wave A8, the live-board program): the pure
// `Estate.EstateReport` projected through the Spectre-backed `View` engine — a
// bordered masthead, disclosable lane findings each opening to its one lever, the
// environment × plane matrix as a table. Pure: renders to an in-memory writer (the
// redirected = NoColors, wide lens), asserts the operator-facing content survives.
// The one-substrate law is pinned too: `View.toJson` (the machine lens a `--query`
// walks) carries the SAME finding statements the human reads.

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Cli

let private render (report: Estate.EstateReport) : string =
    use sw = new System.IO.StringWriter()
    EstateBoardView.write sw report
    sw.ToString()

/// A diverged estate: one DECIDE fork (an AutoNumber static-identity fork) and one
/// REPAIR finding (NULLs under NOT NULL) with its lever, across two environments
/// on different evidence bases, a green fidelity proof, and one remediation file.
let private divergedReport : Estate.EstateReport =
    let decide : Estate.Finding =
        { Key = FindingKey.create EstateFindingKind.DataStaticIdentity "Status"
          Kind = EstateFindingKind.DataStaticIdentity
          Lane = EstateLane.Decide
          Plane = EstatePlane.Identity
          Envs = [ "cloud-dev", 3L; "cloud-uat", 7L ]
          Statement = "Status numbers 'Approved' as 3 in cloud-dev and 7 in cloud-uat."
          Lever = Some "Rule the seed: pin explicit key values in the model."
          Fork = true }
    let repair : Estate.Finding =
        { Key = FindingKey.create EstateFindingKind.DataNotNull "Customer.Email"
          Kind = EstateFindingKind.DataNotNull
          Lane = EstateLane.Repair
          Plane = EstatePlane.Data
          Envs = [ "cloud-uat", 4120L ]
          Statement = "Customer.Email declares NOT NULL; cloud-uat holds 4,120 NULL rows."
          Lever = Some "Review block 2 of environments.remediation.cloud-uat.sql."
          Fork = false }
    { Target = Estate.TargetOperand.AgreedEnv "cloud-dev"
      Bases =
        [ { Env = "cloud-dev"; DataEvidenceAvailable = true; Provenance = Estate.EvidenceProvenance.Live }
          { Env = "cloud-uat"; DataEvidenceAvailable = true
            Provenance = Estate.EvidenceProvenance.Cached (System.DateTimeOffset(2026, 7, 15, 0, 0, 0, System.TimeSpan.Zero), 2, 214) } ]
      Findings = [ decide; repair ]
      Verdict = Estate.Verdict.Forked
      Evidence = Estate.EvidenceStoreBasis.Enabled "/var/projection/estate"
      Remediation = [ "environments.remediation.cloud-uat.sql", 1 ]
      OverlayEntries = None
      EmissionFindings = []
      Burndown = None
      Streak = 0
      Fidelity = Estate.FidelityClause.Green ("cloud-uat-load", 2)
      StaticInspected = true }

/// A unified estate: no findings, a store, a streak — the empty state is a full
/// surface (RT-13).
let private unifiedReport : Estate.EstateReport =
    { divergedReport with
        Findings = []
        Verdict = Estate.Verdict.Unified
        Fidelity = Estate.FidelityClause.NotConfigured
        Streak = 4 }

[<Fact>]
let ``ofReport: the masthead names each environment and its evidence provenance`` () =
    let out = render divergedReport
    Assert.Contains("cloud-dev", out)
    Assert.Contains("live data evidence, profiled this run", out)
    Assert.Contains("cloud-uat", out)
    Assert.Contains("fingerprints (row count, max key, and content hash) clean across 214 kind(s)", out)

[<Fact>]
let ``ofReport: the DECIDE fork's statement and the REPAIR finding's lever both render`` () =
    let out = render divergedReport
    Assert.Contains("DECIDE — the ruling queue", out)
    Assert.Contains("Status numbers 'Approved' as 3 in cloud-dev and 7 in cloud-uat.", out)
    Assert.Contains("REPAIR — prepared repairs", out)
    // The lever is the one child of its finding's disclosure — revealed at the
    // calm default depth.
    Assert.Contains("Review block 2 of environments.remediation.cloud-uat.sql.", out)

[<Fact>]
let ``ofReport: the matrix renders each environment's per-plane counts`` () =
    let out = render divergedReport
    Assert.Contains("MATRIX", out)
    Assert.Contains("identity", out)   // the plane token header
    Assert.Contains("data", out)

[<Fact>]
let ``ofReport: the fidelity clause and a remediation artifact ride the board`` () =
    let out = render divergedReport
    Assert.Contains("green", out)                                     // the fidelity proof state
    Assert.Contains("environments.remediation.cloud-uat.sql", out)    // the artifact index line

[<Fact>]
let ``ofReport: a unified estate renders a full surface — empty lanes named, the streak stated`` () =
    let out = render unifiedReport
    Assert.Contains("Nothing awaits a ruling.", out)   // the empty DECIDE lane, named
    Assert.Contains("4 consecutive unified run(s)", out)
    // The one next move, for a holding estate.
    Assert.Contains("The estate holds", out)

[<Fact>]
let ``one substrate: toJson carries the same finding statement the human reads`` () =
    // The machine lens (`View.toJson`, which `--query` walks) and the terminal
    // are projections of ONE `View` — a statement on the board is a statement in
    // the JSON, never a parallel print path (RT-14).
    // System.Text.Json escapes the statement's apostrophes to `'` (correct
    // JSON; a `--query` unescapes them), so assert on the apostrophe-free spans —
    // proof the statement and the lever rode into the machine lens intact.
    let json = (View.toJson (EstateBoardView.ofReport divergedReport)).ToString()
    Assert.Contains("as 3 in cloud-dev and 7 in cloud-uat.", json)
    Assert.Contains("Review block 2 of environments.remediation.cloud-uat.sql.", json)
