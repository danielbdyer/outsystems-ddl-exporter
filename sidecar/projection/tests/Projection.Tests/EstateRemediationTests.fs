module Projection.Tests.EstateRemediationTests

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Targets.OperationalDiagnostics
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// The estate remediation artifacts (wave A5 — `EstateRemediation` +
// `RemediationEmitter.emitEstate`). The laws under test:
//   - BLOCK ⇔ LEVER: every REPAIR-lane finding that resolves to physical
//     coordinates earns one block whose id IS the finding's key, and the
//     finding's lever names that block in the primary environment's file.
//   - OPERATOR SAFETY: the locating SELECT is active; every repair is
//     commented out (the RemediationEmitter contract, carried over).
//   - THE SENTINEL CLASS: an orphan block whose environment witnesses the
//     zero sentinel leads with the unset-reference UPDATE.
//   - PROVENANCE (RT-12): the header names env, server, and database from
//     the connection's TYPED fields — never the raw string, never secrets.
//   - ONE SUBSTRATE: the stamped report renders the artifact index line and
//     carries the remediation array in estate.json.
// ---------------------------------------------------------------------------

let private agreed : Estate.TargetOperand = Estate.TargetOperand.AgreedEnv "cloud-dev"

let private operand (label: string) (c: Catalog) (p: Profile option) : Compare.Operand =
    { Label = label; Catalog = c; Profile = p }

let private nullEvidence (attrKey: SsKey) (rowCount: int64) (nullCount: int64) : ColumnProfile =
    { AttributeKey = attrKey
      RowCount = rowCount
      NullCount = nullCount
      MaxObservedLength = None
      NullCountProbeStatus = ProbeStatus.observed rowCount }

let private orphanEvidence (refKey: SsKey) (orphans: int64) : ForeignKeyReality =
    { ReferenceKey = refKey
      HasOrphan = orphans > 0L
      OrphanCount = orphans
      IsNoCheck = false
      ProbeStatus = ProbeStatus.observed 1000L }

let private categoricalOn (attrKey: SsKey) (freqs: (string * int64) list) : AttributeDistribution =
    AttributeDistribution.Categorical
        (CategoricalDistribution.create attrKey freqs (int64 (List.length freqs)) false (ProbeStatus.observed 1000L)
         |> Result.value)

let private reportFor (profile: Profile) : Estate.EstateReport =
    Estate.compute agreed sampleCatalog
        [ "cloud-uat", operand "cloud-uat" sampleCatalog (Some profile) ]

let private blocksFor (profile: Profile) (report: Estate.EstateReport) : RemediationEmitter.EstateBlock list =
    EstateRemediation.blocksFor "cloud-uat" (Readiness.toLogicalShape sampleCatalog) (Some profile) report

[<Fact>]
let ``block ⇔ lever: a NOT-NULL repair earns its block (id = the finding's key) and the finding's lever names it`` () =
    let dirty = { Profile.empty with Columns = [ nullEvidence customerNameKey 5000L 42L ] }
    let report = reportFor dirty
    let finding = report.Findings |> List.find (fun f -> f.Kind = EstateFindingKind.DataNotNull)
    Assert.Equal(Some "Review block data.notNull:Customer.Name of estate.remediation.cloud-uat.sql.", finding.Lever)
    let block = blocksFor dirty report |> List.exactlyOne
    Assert.Equal("data.notNull:Customer.Name", block.BlockId)
    Assert.Equal("SELECT * FROM [dbo].[OSUSR_S1S_CUSTOMER] WHERE [NAME] IS NULL;", block.Locate)
    Assert.Contains(block.Repairs, fun (r: string) -> r.StartsWith "UPDATE [dbo].[OSUSR_S1S_CUSTOMER] SET [NAME] = <DEFAULT>")
    Assert.Contains(block.Repairs, fun (r: string) -> r.StartsWith "DELETE FROM [dbo].[OSUSR_S1S_CUSTOMER] WHERE [NAME] IS NULL;")

[<Fact>]
let ``the sentinel class: an orphan block with zero-sentinel evidence leads with the unset-reference UPDATE`` () =
    let dirty =
        { Profile.empty with
            ForeignKeys = [ orphanEvidence orderRefToCustomer 20L ]
            Distributions = [ categoricalOn orderCustomerFkKey [ "0", 15L; "7", 5L ] ] }
    let report = reportFor dirty
    let block =
        blocksFor dirty report
        |> List.find (fun b -> b.BlockId = "data.orphans:Order.CustomerId")
    Assert.Contains("NOT IN (SELECT [ID] FROM [dbo].[OSUSR_S1S_CUSTOMER])", block.Locate)
    match block.Repairs with
    | sentinel :: remove :: _ ->
        Assert.StartsWith("UPDATE [dbo].[OSUSR_S1S_ORDER] SET [CUSTOMER_ID] = NULL WHERE [CUSTOMER_ID] = 0;", sentinel)
        Assert.StartsWith("DELETE FROM [dbo].[OSUSR_S1S_ORDER]", remove)
    | other -> Assert.Fail(sprintf "expected the sentinel UPDATE then the DELETE; got %A" other)

[<Fact>]
let ``the trust block: a WITH NOCHECK finding earns the re-trust candidate against sys.foreign_keys`` () =
    let untrusted =
        { Profile.empty with
            ForeignKeys =
                [ { ReferenceKey = orderRefToCustomer
                    HasOrphan = false
                    OrphanCount = 0L
                    IsNoCheck = true
                    ProbeStatus = ProbeStatus.observed 100L } ] }
    let report = reportFor untrusted
    let block =
        blocksFor untrusted report
        |> List.find (fun b -> b.BlockId = "schema.trust:Order.CustomerId")
    Assert.Contains("sys.foreign_keys", block.Locate)
    Assert.Contains("OBJECT_ID(N'[dbo].[OSUSR_S1S_ORDER]')", block.Locate)
    Assert.Contains(block.Repairs, fun (r: string) ->
        r.StartsWith "ALTER TABLE [dbo].[OSUSR_S1S_ORDER] WITH CHECK CHECK CONSTRAINT ALL;")

[<Fact>]
let ``emitEstate: the header leads, the block id and statement ride as comments, the SELECT stays active, every repair is commented`` () =
    let dirty = { Profile.empty with Columns = [ nullEvidence customerNameKey 5000L 42L ] }
    let report = reportFor dirty
    let sql =
        RemediationEmitter.emitEstate
            (EstateRemediation.header "cloud-uat" "Server=myhost,1433;Initial Catalog=OSDB;User Id=svc;Password=secret" (System.DateTimeOffset(2026, 7, 15, 12, 0, 0, System.TimeSpan.Zero)))
            (blocksFor dirty report)
    Assert.Contains("-- projection:estate-remediation env=cloud-uat server=myhost,1433 database=OSDB generated=2026-07-15T12:00:00", sql)
    Assert.DoesNotContain("secret", sql)
    Assert.Contains("-- block data.notNull:Customer.Name", sql)
    let lines = sql.Split '\n'
    let selectLines = lines |> Array.filter (fun l -> l.Contains "SELECT * FROM [dbo]")
    Assert.NotEmpty selectLines
    Assert.All(selectLines, fun l -> Assert.False(l.TrimStart().StartsWith "--", "the locating SELECT must stay active"))
    let repairLines = lines |> Array.filter (fun l -> l.Contains "DELETE FROM [dbo]" || l.Contains "UPDATE [dbo]")
    Assert.NotEmpty repairLines
    Assert.All(repairLines, fun l -> Assert.StartsWith("-- ", l.TrimStart()))

[<Fact>]
let ``one substrate: the stamped report renders the artifact index line and carries the remediation array in estate.json`` () =
    let dirty = { Profile.empty with Columns = [ nullEvidence customerNameKey 5000L 42L ] }
    let report = reportFor dirty |> Estate.withRemediation [ "estate.remediation.cloud-uat.sql", 1 ]
    let lines = Estate.render report
    Assert.Contains(lines, fun (l: string) ->
        l.Contains "estate.remediation.cloud-uat.sql — 1 prepared repair block(s)")
    let json = Estate.toJsonString report
    Assert.Contains("\"file\": \"estate.remediation.cloud-uat.sql\"", json)
    Assert.Contains("\"blocks\": 1", json)

[<Fact>]
let ``watch findings carry no lever — a lever is never promised before its artifact`` () =
    // An asymmetry advisory (WATCH) must stay lever-free even though the
    // repair machinery now mints levers for its lane siblings.
    let big  = { Profile.empty with Columns = [ nullEvidence customerNameKey 10400L 0L ] }
    let tiny = { Profile.empty with Columns = [ nullEvidence customerNameKey 12L 0L ] }
    let report =
        Estate.compute agreed sampleCatalog
            [ "cloud-uat", operand "cloud-uat" sampleCatalog (Some big)
              "cloud-dev", operand "cloud-dev" sampleCatalog (Some tiny) ]
    let watch = report.Findings |> List.find (fun f -> f.Kind = EstateFindingKind.DataAsymmetry)
    Assert.Equal(None, watch.Lever)
