namespace Projection.Tests

// THE OPERATOR WALK (the §12 terminus — `CUTOVER_RECONCILIATION_IDEATION.md`
// §12; commissioned 2026-08-31): the two-loop operator experience driven
// END-TO-END over a LIVE two-cell estate through the REAL CLI faces — the
// first test anywhere to drive `runCheckEstate` (every prior estate witness
// sat at the engine seam, `Estate.computeWith`). The §12 target loop:
//
//   projection check environments    → the board: who diverges, the one lever each
//     <run the per-env remediation>  → block-keyed, safe-by-default SQL (the E7
//                                      executor verb was never built; the operator
//                                      runs the artifact — the walk mechanizes
//                                      exactly that step)
//   projection check environments    → the burndown: N closed · 0 opened; the streak
//   projection publish               → the ordinary pipeline (R6: emits, doesn't ship)
//   projection check fidelity <flow> → rows byte-identical modulo the ledger
//   projection check environments    → the board reads the proof (RT-10)
//
// Serial via the Docker-SqlServer collection. Every artifact is cwd-relative
// BY DESIGN (RT-10 reads `fidelity-proof/<flow>/fidelity.rows.json` from the
// same cwd the estate face runs in), so artifacts are deleted before each walk
// and in `finally` — a leaked artifact would poison a sibling's assertions.

open System
open System.IO
open Xunit
open Projection.Pipeline

module OperatorWalkFixtures =

    /// cell-b twins of the harness's deterministic source rows — for these two
    /// tables the espace shift IS the physical-prefix rewrite (`OSUSR_` →
    /// `OSUSR_X`), the same rendition `OssysSeedBuilder.withEspaceKey "X"`
    /// applied to the DDL.
    let sinkRows : string =
        PeerEstateHarness.sourceRows.Replace("OSUSR_", "OSUSR_X")

    /// The static REFERENCE seed (Country) — planted identically in both cells
    /// at phase 0 so the static plane is EXERCISED and agrees. The surrogates
    /// mint in the same order on both sides (fresh IDENTITY(1,1)), so the D11
    /// static-identity watch stays quiet by construction.
    let countryRows (table: string) : string =
        sprintf "INSERT INTO [dbo].[%s] ([CODE],[NAME]) VALUES (N'PT', N'Portugal'), (N'ES', N'Spain');" table  // LINT-ALLOW: terminal test-SQL boundary; table is a test literal

    /// The planted divergence, cell-b ONLY: a static-content label drift (D10 —
    /// the alignment MERGE is the prepared repair), an untrusted relationship
    /// (NOCHECK — how real estates come to hold orphans at all), and the FK
    /// orphan the untrusted constraint then admits (Customer 12 → City 999).
    let divergeSink : string =
        "UPDATE [dbo].[OSUSR_XREF_COUNTRY] SET [NAME] = N'Portugalia' WHERE [CODE] = N'PT'; \
         ALTER TABLE [dbo].[OSUSR_XABC_CUSTOMER] NOCHECK CONSTRAINT ALL; \
         SET IDENTITY_INSERT [dbo].[OSUSR_XABC_CUSTOMER] ON; \
         INSERT INTO [dbo].[OSUSR_XABC_CUSTOMER] ([ID],[EMAIL],[FIRSTNAME],[LASTNAME],[CITYID]) VALUES (12, N'orphan@x', N'Oscar', N'Silva', 999); \
         SET IDENTITY_INSERT [dbo].[OSUSR_XABC_CUSTOMER] OFF;"

    /// The identity model scope: empty `model.modules` is the show-everything
    /// default (system/inactive espaces excluded symmetrically on both cells).
    let identityScope : Config.ModelSection =
        { Path = None
          Ossys = None
          Modules = []
          IncludeSystemModules = false
          IncludeInactiveModules = false
          OnlyActiveAttributes = true }

    /// The face's coordinates over raw connection strings (`Source.resolveConn`
    /// passes a raw string through; only `env:` / `file:` are special — the D9
    /// precedent the go-board witnesses ride).
    let estateArgs
        (targetConn: string)
        (confirm: (string * string) list)
        (fidelityFlow: string option)
        : CheckEstateArgs =
        { TargetLabel = "cell-a"
          Target = EstateTargetSource.AgreedEnv targetConn
          Confirm = confirm
          Scope = identityScope
          AsJson = false
          Evidence = EstateEvidenceMode.FingerprintGated
          RepairBand = None
          RepairBandByEntity = Map.empty
          DecisionFloor = None
          AsymmetryFactor = None
          PromotionOrder = []
          Since = None
          FidelityFlow = fidelityFlow
          Tightening = None
          TableRenames = [] }

    /// The walk's cwd artifacts — deleted before each walk and in `finally`
    /// (the Docker collection is serial; a leaked artifact poisons a sibling).
    let walkArtifacts : string list =
        [ "environments.json"
          "environments.remediation.cell-a.sql"
          "environments.remediation.cell-b.sql"
          "environments.overlay.json"
          "environments.probes.sql" ]

    let cleanArtifacts () : unit =
        walkArtifacts |> List.iter (fun f -> if File.Exists f then File.Delete f)

    /// One walk step: drive the REAL face, capture the board the operator
    /// would read (stdout), return the exit code with it.
    let checkEnvironments (args: CheckEstateArgs) : int * string =
        GoBoardFixtures.captureBoard (fun () -> Projection.Cli.Faces.Estate.runCheckEstate args)

    /// The operator's remediation step, mechanized exactly as §12 describes it
    /// (the E7 executor verb was never built — the operator reads the block,
    /// uncomments the repair they choose, and runs it): take one block's
    /// commented repair lines (between its `-- key:` line and the next block
    /// header), strip the comment prefix, and keep the single-line executable
    /// statements — the prose guidance lines carry no leading SQL verb. The
    /// D10 alignment MERGE is multi-line and gets its own extraction when the
    /// walk applies it.
    let singleLineRepairsOfBlock (artifact: string) (key: string) : string list =
        let lines = artifact.Replace("\r\n", "\n").Split('\n') |> Array.toList
        let rec dropUntilKey (remaining: string list) =
            match remaining with
            | [] -> []
            | (l: string) :: rest when l.StartsWith("-- key: " + key) -> rest
            | _ :: rest -> dropUntilKey rest
        dropUntilKey lines
        |> List.takeWhile (fun l -> not (l.StartsWith "-- Block:"))
        |> List.filter (fun l -> l.StartsWith "-- ")
        |> List.map (fun l -> l.Substring 3)
        |> List.filter (fun s ->
            let t = s.TrimStart()
            t.StartsWith "UPDATE " || t.StartsWith "DELETE " || t.StartsWith "ALTER ")

/// The walk itself. Each phase asserts what the OPERATOR would see — exit
/// codes, the rendered board, the artifacts on disk — never engine internals;
/// the engine seams have their own witnesses (`PeerWitnessDockerTests`,
/// `EstateTests`).
[<Xunit.Collection("Docker-SqlServer")>]
type OperatorWalkDockerTests(fixture: EphemeralContainerFixture) =
    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``operator walk (§12 terminus): the estate's own finding is found, repaired through the artifact, and converged; a planted divergence reds the board and stages the keyed remediation`` () =
        if not (PeerEstateHarness.skipIfNoDocker "OperatorWalk") then () else
        let storeRoot = Path.Combine(Path.GetTempPath(), "opwalk-store-" + Guid.NewGuid().ToString "N")
        let priorStore = Environment.GetEnvironmentVariable "PROJECTION_ESTATE_DIR"
        Environment.SetEnvironmentVariable("PROJECTION_ESTATE_DIR", storeRoot)
        OperatorWalkFixtures.cleanArtifacts ()
        try
            PeerEstateHarness.run2Cell fixture "OpWalk" (fun src sink srcConnStr sinkConnStr _srcContract _sinkContract ->
                task {
                    // Phase 0 — twin data in both cells (rows AND the static
                    // reference seed agree), then the first reading. The
                    // edge-case estate is NOT pristine BY DESIGN: its seed
                    // ships `StockMovement.SupplierId` enforced WITH NOCHECK
                    // in every cell — the walk's first contact finds the
                    // estate's own untrusted relationship, exactly as a real
                    // first `check environments` would.
                    do! Deploy.executeBatch src PeerEstateHarness.sourceRows
                    do! Deploy.executeBatch sink OperatorWalkFixtures.sinkRows
                    do! Deploy.executeBatch src (OperatorWalkFixtures.countryRows "OSUSR_REF_COUNTRY")
                    do! Deploy.executeBatch sink (OperatorWalkFixtures.countryRows "OSUSR_XREF_COUNTRY")
                    let args =
                        OperatorWalkFixtures.estateArgs
                            srcConnStr [ "cell-a", srcConnStr; "cell-b", sinkConnStr ] None
                    let exit0, board0 = OperatorWalkFixtures.checkEnvironments args
                    Assert.True((exit0 = 5), sprintf "first contact expected exit 5 (the seed's own untrusted relationship), got %d; board:\n%s" exit0 board0)
                    // The face renders the RICH board (the engine's plain
                    // `Estate.render` says ENVIRONMENTS; the board panel's
                    // title is the operator-register lowercase).
                    Assert.Contains("─environments─", board0)
                    Assert.Contains("first recorded reading", board0)
                    Assert.Contains("StockMovement.SupplierId", board0)
                    Assert.True(File.Exists "environments.json", "environments.json was not written on the first reading")

                    // Phase 1 — the operator's step, mechanized: each cell's
                    // artifact stages the re-trust block against ITS physical
                    // names; uncomment and run each against its own cell.
                    let trustKey = "schema.trust:StockMovement.SupplierId"
                    let repairsA =
                        OperatorWalkFixtures.singleLineRepairsOfBlock
                            (File.ReadAllText "environments.remediation.cell-a.sql") trustKey
                    let repairsB =
                        OperatorWalkFixtures.singleLineRepairsOfBlock
                            (File.ReadAllText "environments.remediation.cell-b.sql") trustKey
                    Assert.True((not (List.isEmpty repairsA)) && not (List.isEmpty repairsB),
                                "the re-trust blocks were not staged in both cells' artifacts")
                    Assert.True(repairsA |> List.exists (fun s -> s.Contains "[OSUSR_INV_MOVEMENT]"),
                                "cell-a's re-trust block does not name its own physical table")
                    Assert.True(repairsB |> List.exists (fun s -> s.Contains "[OSUSR_XINV_MOVEMENT]"),
                                "cell-b's re-trust block does not name its espace-shifted physical table")
                    do! Deploy.executeBatch src (String.concat "\n" repairsA)
                    do! Deploy.executeBatch sink (String.concat "\n" repairsB)

                    // Phase 2 — the estate converges: the burndown closes the
                    // finding by key and the unified streak starts.
                    let exit1, board1 = OperatorWalkFixtures.checkEnvironments args
                    Assert.True((exit1 = 0), sprintf "post-repair walk expected exit 0 (unified), got %d; board:\n%s" exit1 board1)
                    Assert.Contains("BURNDOWN — movement since the recorded baseline", board1)
                    Assert.Contains("1 closed · 0 opened · 0 remain", board1)
                    Assert.Contains("1 consecutive unified run", board1)

                    // Phase 3 — the divergence lands in cell-b ONLY: the label
                    // drift, the untrusted relationship, the orphan it admits.
                    do! Deploy.executeBatch sink OperatorWalkFixtures.divergeSink

                    // Phase 4 — the board reds; the remediation artifact stages
                    // the repairs, keyed and safe-by-default.
                    let exit2, board2 = OperatorWalkFixtures.checkEnvironments args
                    Assert.True((exit2 = 5), sprintf "diverged walk expected exit 5, got %d; board:\n%s" exit2 board2)
                    Assert.True(File.Exists "environments.remediation.cell-b.sql",
                                sprintf "the per-env remediation artifact was not written; board:\n%s" board2)
                    let remediation = File.ReadAllText "environments.remediation.cell-b.sql"
                    // RT-12: the provenance header makes the wrong-environment
                    // mistake structurally detectable — and carries no secret.
                    Assert.Contains("-- projection:environments-remediation env=cell-b server=", remediation)
                    let sinkPassword = Microsoft.Data.SqlClient.SqlConnectionStringBuilder(sinkConnStr).Password
                    if sinkPassword <> "" then Assert.DoesNotContain(sinkPassword, remediation)
                    // The blocks are FindingKey-keyed — the board, the artifact,
                    // and the burndown speak one key vocabulary (E5).
                    Assert.Contains("-- key: data.staticContent:Country", remediation)
                    Assert.Contains("MERGE", remediation)
                    Assert.Contains("-- key: data.orphans:Customer.CityId", remediation)
                    // The operator-safety contract, pinned LIVE: the locating
                    // SELECT is active; every mutating repair line is commented.
                    for line in remediation.Split('\n') do
                        let t = line.TrimStart()
                        Assert.False(
                            t.StartsWith "UPDATE " || t.StartsWith "DELETE " || t.StartsWith "MERGE " || t.StartsWith "ALTER ",
                            sprintf "uncommented mutating line in the remediation artifact: %s" line)
                    // The burndown's opened side names the movement from the
                    // unified baseline (opened > 0 — never the vacuous zero).
                    Assert.Contains("BURNDOWN — movement since the recorded baseline", board2)
                    Assert.Matches(@"[1-9]\d* opened", board2)
                    return ()
                })
        finally
            Environment.SetEnvironmentVariable("PROJECTION_ESTATE_DIR", priorStore)
            OperatorWalkFixtures.cleanArtifacts ()
            try Directory.Delete(storeRoot, true) with _ -> ()
