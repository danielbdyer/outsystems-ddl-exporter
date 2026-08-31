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
open Projection.Core
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
          "environments.probes.sql"
          "fidelity.rows.json" ]

    let cleanArtifacts () : unit =
        walkArtifacts |> List.iter (fun f -> if File.Exists f then File.Delete f)
        let proofDir = Path.Combine("fidelity-proof", "opwalk")
        if Directory.Exists proofDir then Directory.Delete(proofDir, true)

    /// One walk step: drive the REAL face, capture the board the operator
    /// would read (stdout), return the exit code with it.
    let checkEnvironments (args: CheckEstateArgs) : int * string =
        GoBoardFixtures.captureBoard (fun () -> Projection.Cli.Faces.Estate.runCheckEstate args)

    /// The operator's remediation step, mechanized exactly as §12 describes it
    /// (the E7 executor verb was never built — the operator reads the block,
    /// uncomments the repair they choose, and runs it). One block's layout is
    /// fixed by the emitter: `-- Block:` title, `-- key:`, ONE commented
    /// statement line, the ACTIVE locating SELECT, then the commented repair
    /// lines. This takes the repair lines of the named block, uncommented.
    let repairLinesOfBlock (artifact: string) (key: string) : string list =
        let lines = artifact.Replace("\r\n", "\n").Split('\n') |> Array.toList
        let rec dropUntilKey (remaining: string list) =
            match remaining with
            | [] -> []
            | (l: string) :: rest when l.StartsWith("-- key: " + key) -> rest
            | _ :: rest -> dropUntilKey rest
        match dropUntilKey lines with
        | [] -> []
        | _statementLine :: rest ->
            rest
            |> List.skipWhile (fun l -> not (l.StartsWith "-- "))   // the active Locate line(s)
            |> List.takeWhile (fun l -> not (l.StartsWith "-- Block:"))
            |> List.filter (fun l -> l.StartsWith "-- ")
            |> List.map (fun l -> l.Substring 3)

    /// Single-statement repairs (re-trust, orphan DELETE): the executable
    /// lines carry a leading SQL verb; prose guidance lines do not.
    let singleLineRepairsOfBlock (artifact: string) (key: string) : string list =
        repairLinesOfBlock artifact key
        |> List.filter (fun s ->
            let t = s.TrimStart()
            t.StartsWith "UPDATE " || t.StartsWith "DELETE " || t.StartsWith "ALTER ")

    /// The D10 alignment MERGE (multi-line): drop the block's two prose
    /// guidance lines — what remains is the MERGE batch the operator runs.
    let mergeRepairOfBlock (artifact: string) (key: string) : string =
        repairLinesOfBlock artifact key
        |> List.filter (fun s ->
            let t = s.TrimStart()
            not (t.StartsWith "align ") && not (t.StartsWith "rows present"))
        |> String.concat "\n"

    /// Capture BOTH consoles (a refusal voices to stderr; the board to
    /// stdout) — the WP-13 probe asserts across the two.
    let captureAll (f: unit -> int) : int * string =
        let priorOut = Console.Out
        let priorErr = Console.Error
        use sw = new StringWriter()
        Console.SetOut sw
        Console.SetError sw
        try
            let exit = f ()
            exit, sw.ToString()
        finally
            Console.SetOut priorOut
            Console.SetError priorErr

    /// The WP-13 probe cell: two AppCore entities in a WEAK-LESS 2-cycle —
    /// both FK columns NOT NULL (no nullable edge for the resolver to break),
    /// physical DDL + the OSSYS metadata rows in the seed's own shapes (the
    /// `bt<EspaceSsKey>*<EntitySsKey>` reference binding, PK/attr key
    /// conventions copied from the edge-case seed).
    let cycleCell : string =
        "CREATE TABLE [dbo].[OSUSR_DEF_GAMMA] ( \
             [ID] INT IDENTITY(1,1) NOT NULL, [DELTAID] INT NOT NULL, \
             CONSTRAINT [PK_Gamma_Id] PRIMARY KEY CLUSTERED ([ID])); \
         CREATE TABLE [dbo].[OSUSR_DEF_DELTA] ( \
             [ID] INT IDENTITY(1,1) NOT NULL, [GAMMAID] INT NOT NULL, \
             CONSTRAINT [PK_Delta_Id] PRIMARY KEY CLUSTERED ([ID])); \
         ALTER TABLE [dbo].[OSUSR_DEF_GAMMA] ADD CONSTRAINT [FK_OSUSR_DEF_GAMMA_OSUSR_DEF_DELTA] FOREIGN KEY ([DELTAID]) REFERENCES [dbo].[OSUSR_DEF_DELTA]([ID]); \
         ALTER TABLE [dbo].[OSUSR_DEF_DELTA] ADD CONSTRAINT [FK_OSUSR_DEF_DELTA_OSUSR_DEF_GAMMA] FOREIGN KEY ([GAMMAID]) REFERENCES [dbo].[OSUSR_DEF_GAMMA]([ID]); \
         INSERT INTO [dbo].[ossys_Entity] \
             ([Id], [Name], [Physical_Table_Name], [Espace_Id], [Is_Active], [Is_System], [Is_External], [Data_Kind], [PrimaryKey_SS_Key], [SS_Key], [Description]) \
         VALUES \
             (9000, N'Gamma', N'OSUSR_DEF_GAMMA', 100, 1, 0, 0, N'entity', 'aaaaaaaa-0000-0000-0000-000000000090', 'bbbbbbbb-0000-0000-0000-000000000090', N'WP-13 probe: cycle member'), \
             (9001, N'Delta', N'OSUSR_DEF_DELTA', 100, 1, 0, 0, N'entity', 'aaaaaaaa-0000-0000-0000-000000000091', 'bbbbbbbb-0000-0000-0000-000000000091', N'WP-13 probe: cycle member'); \
         INSERT INTO [dbo].[ossys_Entity_Attr] \
             ([Id], [Entity_Id], [Name], [SS_Key], [Data_Type], [Length], [Precision], [Scale], [Default_Value], [Is_Mandatory], [Is_Active], [Is_AutoNumber], [Is_Identifier], [Referenced_Entity_Id], [Original_Name], [External_Column_Type], [Delete_Rule], [Physical_Column_Name], [Database_Name], [Type], [Legacy_Type], [Decimals], [Original_Type], [Description], [Order_Num]) \
         VALUES \
             (90001, 9000, N'Id', 'cccccccc-0000-0000-0000-000000000090', N'Identifier', NULL, NULL, NULL, NULL, 1, 1, 1, 1, NULL, NULL, NULL, NULL, N'ID', NULL, NULL, NULL, NULL, NULL, NULL, 1), \
             (90002, 9000, N'DeltaId', 'cccccccc-0000-0000-0000-000000000092', N'Identifier', NULL, NULL, NULL, NULL, 1, 1, 0, 0, NULL, NULL, NULL, N'Protect', N'DELTAID', NULL, N'bt11111111-1111-1111-1111-111111111111*bbbbbbbb-0000-0000-0000-000000000091', NULL, NULL, NULL, NULL, 10), \
             (90003, 9001, N'Id', 'cccccccc-0000-0000-0000-000000000091', N'Identifier', NULL, NULL, NULL, NULL, 1, 1, 1, 1, NULL, NULL, NULL, NULL, N'ID', NULL, NULL, NULL, NULL, NULL, NULL, 1), \
             (90004, 9001, N'GammaId', 'cccccccc-0000-0000-0000-000000000093', N'Identifier', NULL, NULL, NULL, NULL, 1, 1, 0, 0, NULL, NULL, NULL, N'Protect', N'GAMMAID', NULL, N'bt11111111-1111-1111-1111-111111111111*bbbbbbbb-0000-0000-0000-000000000090', NULL, NULL, NULL, NULL, 10);"

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

                    // Phase 5 — the mid-walk remediation (§12's middle step):
                    // cell-b's three staged blocks, applied in the order the
                    // estate itself demands — the orphan must leave BEFORE
                    // WITH CHECK can re-validate the relationship (Msg 547
                    // otherwise). The operator reads that order off the
                    // board; the walk mechanizes the same judgment.
                    let orphanRepairs =
                        OperatorWalkFixtures.singleLineRepairsOfBlock remediation "data.orphans:Customer.CityId"
                        |> List.filter (fun s -> s.TrimStart().StartsWith "DELETE ")
                    let trustRepairs =
                        OperatorWalkFixtures.singleLineRepairsOfBlock remediation "schema.trust:Customer.CityId"
                    let mergeRepair =
                        OperatorWalkFixtures.mergeRepairOfBlock remediation "data.staticContent:Country"
                    Assert.True(not (List.isEmpty orphanRepairs), "the orphan block staged no executable DELETE")
                    Assert.True(not (List.isEmpty trustRepairs), "the re-trust block was not staged for cell-b")
                    Assert.Contains("MERGE", mergeRepair)
                    do! Deploy.executeBatch sink (String.concat "\n" orphanRepairs)
                    do! Deploy.executeBatch sink mergeRepair
                    do! Deploy.executeBatch sink (String.concat "\n" trustRepairs)

                    // Phase 6 — the burndown closes what the walk repaired:
                    // unified again, every opened finding closed BY KEY, the
                    // streak restarts from the diverged reading.
                    let exit3, board3 = OperatorWalkFixtures.checkEnvironments args
                    Assert.True((exit3 = 0), sprintf "post-remediation walk expected exit 0 (unified), got %d; board:\n%s" exit3 board3)
                    Assert.Matches(@"[1-9]\d* closed · 0 opened · 0 remain", board3)
                    Assert.Contains("1 consecutive unified run", board3)

                    // Phase 7 — the ordinary pipeline (R6: emits, doesn't
                    // ship): the config-driven bundle publish over cell-a's
                    // live OSSYS model, through the REAL loader and the REAL
                    // face. The bundle carries its own apply story
                    // (`apply-runbook.md` — ideation §12 F7).
                    let cfgPath =
                        Path.Combine(Path.GetTempPath(), sprintf "opwalk-config-%s.json" (Guid.NewGuid().ToString "N"))
                    let outDir =
                        Path.Combine(Path.GetTempPath(), sprintf "opwalk-bundle-%s" (Guid.NewGuid().ToString "N"))
                    File.WriteAllText(cfgPath,
                        sprintf """{ "model": { "ossys": "%s" }, "output": { "dir": "%s" } }"""
                            (srcConnStr.Replace("\\", "\\\\")) (outDir.Replace("\\", "\\\\")))
                    try
                        let exitPub =
                            Projection.Cli.Faces.Export.runFullExport
                                cfgPath (Some outDir) LogSink.Verbosity.Quiet Set.empty None None
                        Assert.True((exitPub = 0), sprintf "bundle publish expected exit 0, got %d" exitPub)
                        let bundleFiles =
                            Directory.EnumerateFiles(outDir, "*", SearchOption.AllDirectories) |> Seq.toList
                        Assert.True(bundleFiles.Length > 0, "the publish emitted an empty bundle")
                        Assert.True(bundleFiles |> List.exists (fun p -> Path.GetFileName p = "apply-runbook.md"),
                                    "the bundle carries no apply-runbook.md (ideation §12 F7)")
                        Assert.True(bundleFiles |> List.exists (fun p -> Path.GetExtension p = ".sql"),
                                    "the bundle carries no SQL artifact")

                        // Phase 8 — the fidelity proof (B5): the flow's
                        // container proof stages the model, loads from cell-a,
                        // and compares — every row byte-identical.
                        let fidelityArgs : CheckFidelityFlowArgs =
                            { Flow = "opwalk"
                              FromLabel = "cell-a"
                              SourceConn = srcConnStr
                              SampleCap = 20
                              AsJson = false
                              Refresh = false
                              Stage = StagingMode.Ddl
                              Capture = None
                              IdentityPolicy = IdentityPolicy.Structural
                              Load = LoadMode.Transfer
                              Corrections = []
                              CorrectionReceipts = None }
                        let exitFid =
                            Projection.Cli.Faces.Fidelity.runCheckFidelityFlow _srcContract fidelityArgs
                        Assert.True((exitFid = 0), "the fidelity proof did not read green")
                        Assert.True(File.Exists "fidelity.rows.json", "the proof record was not written")
                        Assert.True(File.Exists (Path.Combine("fidelity-proof", "opwalk", "fidelity.rows.json")),
                                    "the flow-scoped proof copy (the RT-10 read path) was not written")

                        // Phase 9 — the loop closes: the board reads the proof
                        // (RT-10) and the streak carries. Every §12 line has
                        // now been walked over one live estate in one cwd.
                        let argsWithFlow =
                            { args with FidelityFlow = Some "opwalk" }
                        let exit4, board4 = OperatorWalkFixtures.checkEnvironments argsWithFlow
                        Assert.True((exit4 = 0), sprintf "the closing walk expected exit 0, got %d; board:\n%s" exit4 board4)
                        Assert.Contains("green — flow 'opwalk', every row byte-identical", board4)
                        Assert.Contains("2 consecutive unified runs", board4)
                        return ()
                    finally
                        try File.Delete cfgPath with _ -> ()
                        try if Directory.Exists outDir then Directory.Delete(outDir, true) with _ -> ()
                })
        finally
            Environment.SetEnvironmentVariable("PROJECTION_ESTATE_DIR", priorStore)
            OperatorWalkFixtures.cleanArtifacts ()
            try Directory.Delete(storeRoot, true) with _ -> ()
