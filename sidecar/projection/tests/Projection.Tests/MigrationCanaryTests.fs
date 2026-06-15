module Projection.Tests.MigrationCanaryTests

open Xunit
open Projection.Core
open Projection.Targets.SSDT
open Projection.Pipeline
open Projection.Tests.Fixtures

// 6.D.1 — the LIVE A→B canary: the master equation T16 realized on real SQL
// Server (the operator's target). The pure tests prove the composition + the
// structural round-trip; this proves `MigrationRun.execute` evolves a deployed
// state-A database to state B with **minimum-viable touches** across THREE
// channels at once — a physical table rename (sp_rename), a widening reshape
// (ALTER COLUMN), and a new column (ADD COLUMN) — that B' reproduces B at the
// PhysicalSchema level, that existing data SURVIVES (the whole point of a
// differential vs a drop+recreate), and that a re-run is idempotent (resumable
// by construction). Docker-SqlServer collection; blocking wait via TaskSync.

[<Xunit.Collection("Docker-SqlServer")>]
type MigrationCanaryTests(fixture: EphemeralContainerFixture) =

    static let mkKey (label: string) : SsKey = SsKey.synthesized "MIGAB" label |> Result.value
    static let nm (s: string) : Name = Name.create s |> Result.value

    static let mkAttr key col typ len isPk nullable : Attribute =
        { Attribute.create (mkKey key) (nm col) typ with
            Column = ColumnRealization.create (col) (nullable) |> Result.value
            Length = len
            IsPrimaryKey = isPk
            IsMandatory = isPk }

    /// State A: table MIGAB_CUSTOMER, Email NVARCHAR(50), no Loyalty.
    static let catalogA : Catalog =
        let customer =
            Kind.create (mkKey "Customer") (nm "Customer")
                (mkTableId "dbo" "MIGAB_CUSTOMER")
                [ mkAttr "Customer.Id" "ID" Integer None true false
                  mkAttr "Customer.Email" "EMAIL" Text (Some 50) false true ]
        Catalog.create
            [ { SsKey = mkKey "Mod"; Name = nm "MigMod"; Kinds = [ customer ]; IsActive = true; ExtendedProperties = [] } ]
            []
        |> Result.value

    /// State B: same kind SsKey, RENAMED to Patron / table MIGAB_PATRON; Email
    /// WIDENED to NVARCHAR(256); a NEW Loyalty column (Integer, nullable) appended.
    static let catalogB : Catalog =
        let patron =
            Kind.create (mkKey "Customer") (nm "Patron")
                (mkTableId "dbo" "MIGAB_PATRON")
                [ mkAttr "Customer.Id" "ID" Integer None true false
                  mkAttr "Customer.Email" "EMAIL" Text (Some 256) false true
                  mkAttr "Customer.Loyalty" "LOYALTY" Integer None false true ]
        Catalog.create
            [ { SsKey = mkKey "Mod"; Name = nm "MigMod"; Kinds = [ patron ]; IsActive = true; ExtendedProperties = [] } ]
            []
        |> Result.value

    /// State A': Customer with EMAIL. State B': EMAIL renamed to CONTACT (logical
    /// Name + physical ColumnName), table unchanged — a pure column rename.
    static let catalogAcol : Catalog =
        let customer =
            Kind.create (mkKey "C2") (nm "Customer")
                (mkTableId "dbo" "MIGAB_COL")
                [ mkAttr "C2.Id" "ID" Integer None true false
                  mkAttr "C2.Email" "EMAIL" Text (Some 100) false true ]
        Catalog.create [ { SsKey = mkKey "Mod2"; Name = nm "MigMod2"; Kinds = [ customer ]; IsActive = true; ExtendedProperties = [] } ] []
        |> Result.value

    static let catalogBcol : Catalog =
        let customer =
            Kind.create (mkKey "C2") (nm "Customer")
                (mkTableId "dbo" "MIGAB_COL")
                [ mkAttr "C2.Id" "ID" Integer None true false
                  { mkAttr "C2.Email" "CONTACT" Text (Some 100) false true with Name = nm "Contact" } ]
        Catalog.create [ { SsKey = mkKey "Mod2"; Name = nm "MigMod2"; Kinds = [ customer ]; IsActive = true; ExtendedProperties = [] } ] []
        |> Result.value

    /// AC-S8 state A: table MIGAB_RENSRC with ID + EMAIL. State B: SAME
    /// columns, table renamed to MIGAB_RENDST — a PURE table rename (no
    /// reshape, no add), so the only differential is one `sp_rename` of the
    /// table object. Kept column-stable on purpose: an ALTER on a CDC-tracked
    /// (replicated) column is blocked by SQL Server, so the table-rename case
    /// is the one that executes cleanly under CDC.
    static let catalogRenA : Catalog =
        let k =
            Kind.create (mkKey "Ren") (nm "Source")
                (mkTableId "dbo" "MIGAB_RENSRC")
                [ mkAttr "Ren.Id" "ID" Integer None true false
                  mkAttr "Ren.Email" "EMAIL" Text (Some 100) false true ]
        Catalog.create [ { SsKey = mkKey "RenMod"; Name = nm "RenMod"; Kinds = [ k ]; IsActive = true; ExtendedProperties = [] } ] []
        |> Result.value

    static let catalogRenB : Catalog =
        let k =
            Kind.create (mkKey "Ren") (nm "Sink")
                (mkTableId "dbo" "MIGAB_RENDST")
                [ mkAttr "Ren.Id" "ID" Integer None true false
                  mkAttr "Ren.Email" "EMAIL" Text (Some 100) false true ]
        Catalog.create [ { SsKey = mkKey "RenMod"; Name = nm "RenMod"; Kinds = [ k ]; IsActive = true; ExtendedProperties = [] } ] []
        |> Result.value

    /// G9 state A: table MIGAB_TIGHTEN, NOTES NVARCHAR(100) **nullable** —
    /// carries NULL rows. State B tightens NOTES to **NOT NULL** (a pure
    /// nullability tightening; same SsKey, same type/length). The G9 case: an
    /// in-place `ALTER COLUMN … NOT NULL` against existing NULL data.
    static let catalogTightenA : Catalog =
        let entity =
            Kind.create (mkKey "T3") (nm "Tightenable")
                (mkTableId "dbo" "MIGAB_TIGHTEN")
                [ mkAttr "T3.Id" "ID" Integer None true false
                  mkAttr "T3.Notes" "NOTES" Text (Some 100) false true ]
        Catalog.create [ { SsKey = mkKey "Mod3"; Name = nm "MigMod3"; Kinds = [ entity ]; IsActive = true; ExtendedProperties = [] } ] []
        |> Result.value

    static let catalogTightenB : Catalog =
        let entity =
            Kind.create (mkKey "T3") (nm "Tightenable")
                (mkTableId "dbo" "MIGAB_TIGHTEN")
                [ mkAttr "T3.Id" "ID" Integer None true false
                  mkAttr "T3.Notes" "NOTES" Text (Some 100) false false ]
        Catalog.create [ { SsKey = mkKey "Mod3"; Name = nm "MigMod3"; Kinds = [ entity ]; IsActive = true; ExtendedProperties = [] } ] []
        |> Result.value

    static let scalarString (cnn: Microsoft.Data.SqlClient.SqlConnection) (sql: string) =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sql
            let! v = cmd.ExecuteScalarAsync()
            return string v
        }

    static let scalarInt (cnn: Microsoft.Data.SqlClient.SqlConnection) (sql: string) =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sql
            cmd.CommandTimeout <- 0
            let! v = cmd.ExecuteScalarAsync()
            return System.Convert.ToInt32 v
        }

    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``migrate A B streams the live stage arc (build -> apply -> verify) for the Watch board`` () =
        if not (Deploy.Docker.ensureRunning ()) then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "MigrateStages" (fun conn _ ->
                task {
                    do! Deploy.executeBatch conn (SsdtDdlEmitter.statements catalogA |> Render.toText)
                    let seen = System.Collections.Generic.List<string>()
                    LogSink.addSubscriber (fun env -> lock seen (fun () -> seen.Add env.Code))
                    try
                        let! outcome = MigrationRun.execute true DeclareNone catalogA catalogB conn
                        match outcome with
                        | Error e -> Assert.Fail(sprintf "migrate execute failed: %A" e)
                        | Ok _ ->
                            let codes = lock seen (fun () -> List.ofSeq seen)
                            // each phase opens its line (the Watch board reacts to `.started`)
                            Assert.Contains("emit.started", codes)
                            Assert.Contains("deploy.started", codes)
                            Assert.Contains("canary.started", codes)
                            // and closes it (the board flips to Done on stageCompleted) — ≥3 phases
                            let completed = codes |> List.filter (fun c -> c = "summary.stageCompleted")
                            Assert.True(completed.Length >= 3, sprintf "expected ≥3 stageCompleted, got %d: %A" completed.Length codes)
                            // the apply phase reports intra-stage progress (the ETA basis)
                            Assert.Contains("summary.stageProgress", codes)
                    finally
                        LogSink.clearSubscribers ()
                }))

    [<Fact>]
    member _.``migrate A B canary: one execute evolves A→B across three channels; B reproduces B, data survives, re-run is idempotent`` () =
        if not (Deploy.Docker.ensureRunning ()) then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "MigrateAB" (fun conn _ ->
                task {
                    // Deploy state A and seed a row.
                    do! Deploy.executeBatch conn (SsdtDdlEmitter.statements catalogA |> Render.toText)
                    do! Deploy.executeBatch conn
                            "INSERT INTO [dbo].[MIGAB_CUSTOMER] ([ID],[EMAIL]) VALUES (1, N'alice@example.com');"

                    // One command: migrate A → B against the live deployed DB.
                    let! outcome = MigrationRun.execute true DeclareNone catalogA catalogB conn
                    match outcome with
                    | Error e -> Assert.Fail(sprintf "migrate execute failed: %A" e)
                    | Ok result ->
                        // (1) B' reproduces B at the physical level — the master equation, live.
                        Assert.True(
                            result.Verified,
                            sprintf "B' did not reproduce B:\n%s" (PhysicalSchema.renderDiff result.SchemaDiff))

                        // (2) Minimum-viable touches: the table was RENAMED (sp_rename),
                        // not dropped+recreated — so the old name is gone, the new exists.
                        let! oldExists =
                            scalarString conn
                                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'MIGAB_CUSTOMER';"
                        Assert.Equal("0", oldExists)
                        let! newExists =
                            scalarString conn
                                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'MIGAB_PATRON';"
                        Assert.Equal("1", newExists)

                        // (3) The reshape landed — EMAIL widened to 256.
                        let! maxLen =
                            scalarString conn
                                "SELECT CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS \
                                 WHERE TABLE_NAME = 'MIGAB_PATRON' AND COLUMN_NAME = 'EMAIL';"
                        Assert.Equal("256", maxLen)

                        // (4) The add landed — LOYALTY column exists.
                        let! loyalty =
                            scalarString conn
                                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS \
                                 WHERE TABLE_NAME = 'MIGAB_PATRON' AND COLUMN_NAME = 'LOYALTY';"
                        Assert.Equal("1", loyalty)

                        // (5) DATA SURVIVED the differential — the seeded row is intact
                        // in the renamed, reshaped table (a drop+recreate would lose it).
                        let! email = scalarString conn "SELECT [EMAIL] FROM [dbo].[MIGAB_PATRON] WHERE [ID] = 1;"
                        Assert.Equal("alice@example.com", email)

                        // (6) Idempotent + resumable by construction: re-running migrate
                        // A→B against the now-migrated DB is a no-op (empty differential).
                        // We re-plan from B (the new current state) → B: zero touches.
                        let! rerun = MigrationRun.execute true DeclareNone catalogB catalogB conn
                        match rerun with
                        | Error e -> Assert.Fail(sprintf "idempotent re-run failed: %A" e)
                        | Ok r2 ->
                            Assert.True(Migration.isIdempotent r2.Artifacts.Plan)
                            Assert.Empty(r2.Artifacts.SchemaStatements)
                            Assert.True(r2.Verified)
                }))

    [<Fact>]
    member _.``migrate A B canary: a destructive drop refuses before touching the live DB`` () =
        if not (Deploy.Docker.ensureRunning ()) then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "MigrateDrop" (fun conn _ ->
                task {
                    // Deploy B (the richer schema) and seed a row.
                    do! Deploy.executeBatch conn (SsdtDdlEmitter.statements catalogB |> Render.toText)
                    do! Deploy.executeBatch conn
                            "INSERT INTO [dbo].[MIGAB_PATRON] ([ID],[EMAIL],[LOYALTY]) VALUES (1, N'bob@example.com', 7);"

                    // migrate B → A would DROP the Loyalty column (and rename back).
                    // Without allowDrops it must refuse BEFORE any write.
                    let! outcome = MigrationRun.execute true DeclareNone catalogB catalogA conn
                    match outcome with
                    | Error (RefusedByViolations _) -> ()
                    | other -> Assert.Fail(sprintf "expected RefusedByViolations, got %A" other)

                    // The live DB is untouched — the row and the Loyalty column survive.
                    let! loyalty =
                        scalarString conn
                            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS \
                             WHERE TABLE_NAME = 'MIGAB_PATRON' AND COLUMN_NAME = 'LOYALTY';"
                    Assert.Equal("1", loyalty)
                    let! email = scalarString conn "SELECT [EMAIL] FROM [dbo].[MIGAB_PATRON] WHERE [ID] = 1;"
                    Assert.Equal("bob@example.com", email)
                }))

    // -- column rename (the second rename channel) ---------------------------

    [<Fact>]
    member _.``migrate canary: a column rename executes via sp_rename COLUMN and preserves data`` () =
        if not (Deploy.Docker.ensureRunning ()) then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "MigrateCol" (fun conn _ ->
                task {
                    do! Deploy.executeBatch conn (SsdtDdlEmitter.statements catalogAcol |> Render.toText)
                    do! Deploy.executeBatch conn
                            "INSERT INTO [dbo].[MIGAB_COL] ([ID],[EMAIL]) VALUES (1, N'carol@example.com');"

                    let! outcome = MigrationRun.execute true DeclareNone catalogAcol catalogBcol conn
                    match outcome with
                    | Error e -> Assert.Fail(sprintf "column-rename migrate failed: %A" e)
                    | Ok result ->
                        Assert.True(result.Verified, sprintf "B' did not reproduce B:\n%s" (PhysicalSchema.renderDiff result.SchemaDiff))
                        // The column was renamed (EMAIL gone, CONTACT present) — not dropped+added.
                        let! oldCol =
                            scalarString conn
                                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='MIGAB_COL' AND COLUMN_NAME='EMAIL';"
                        Assert.Equal("0", oldCol)
                        let! newCol =
                            scalarString conn
                                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='MIGAB_COL' AND COLUMN_NAME='CONTACT';"
                        Assert.Equal("1", newCol)
                        // The data survived the rename (a drop+add would lose it).
                        let! contact = scalarString conn "SELECT [CONTACT] FROM [dbo].[MIGAB_COL] WHERE [ID]=1;"
                        Assert.Equal("carol@example.com", contact)
                }))

    // -- the data-transfer composition (cross-substrate: schema + data) ------

    [<Fact>]
    member _.``migrate canary: executeWithData migrates the sink schema then loads rows from the source`` () =
        if not (Deploy.Docker.ensureRunning ()) then () else
        TaskSync.run (fun () ->
            // A5 — the data source is at the OLD schema A (MIGAB_CUSTOMER), NOT
            // already at B. The data leg re-points the A-named rows A→B (the table
            // is renamed MIGAB_CUSTOMER→MIGAB_PATRON; EMAIL is widened, not renamed)
            // and writes against the sink's B contract. Sink DB starts at A (empty).
            fixture.WithEphemeralDatabase "MigrateDataSrc" (fun source _ ->
                task {
                    do! Deploy.executeBatch source (SsdtDdlEmitter.statements catalogA |> Render.toText)
                    do! Deploy.executeBatch source
                            "INSERT INTO [dbo].[MIGAB_CUSTOMER] ([ID],[EMAIL]) VALUES (1, N'dave@example.com'), (2, N'erin@example.com');"
                    do! fixture.WithEphemeralDatabase "MigrateDataSink" (fun sink _ ->
                        task {
                            do! Deploy.executeBatch sink (SsdtDdlEmitter.statements catalogA |> Render.toText)

                            // One call: migrate the sink A→B, THEN transfer the rows source→sink.
                            let seen = System.Collections.Generic.List<string>()
                            LogSink.addSubscriber (fun env -> lock seen (fun () -> seen.Add env.Code))
                            let! outcome =
                                MigrationRun.executeWithData DeclareNone Transfer.Execute true catalogA catalogB Map.empty source sink
                            LogSink.clearSubscribers ()
                            match outcome with
                            | Error e -> Assert.Fail(sprintf "executeWithData failed: %A" e)
                            | Ok result ->
                                // Schema leg: the sink is now at B.
                                Assert.True(result.Schema.Verified, sprintf "sink schema not at B:\n%s" (PhysicalSchema.renderDiff result.Schema.SchemaDiff))
                                // Data leg: both rows landed in the sink's migrated (renamed) table.
                                let! count = scalarString sink "SELECT COUNT(*) FROM [dbo].[MIGAB_PATRON];"
                                Assert.Equal("2", count)
                                let! email = scalarString sink "SELECT [EMAIL] FROM [dbo].[MIGAB_PATRON] WHERE [ID]=2;"
                                Assert.Equal("erin@example.com", email)
                                // The data leg streamed the live "load" stage with per-table
                                // progress — the Watch board's "Loading the data · N of M".
                                let codes = lock seen (fun () -> List.ofSeq seen)
                                Assert.Contains("load.started", codes)
                                Assert.Contains("summary.stageProgress", codes)
                                Assert.Contains("summary.stageCompleted", codes)
                        })
                }))

    // A5 — rename-aware migrate-with-data: the data source is at the OLD schema
    // A. The sink (UAT) starts at A (EMAIL); the Dev data source ALSO holds rows
    // at A (EMAIL); the target is B (the column renamed EMAIL→CONTACT). The data
    // leg must re-point the A-named rows onto the sink's B name (CONTACT). Pre-A5
    // the data leg loaded against contract B directly, so the source's EMAIL rows
    // had no CONTACT value — CONTACT would land NULL. The re-pointed write is the
    // discriminator: CONTACT carries the source data BY IDENTITY (the A→B rename
    // map), never NULL.
    [<Fact>]
    member _.``A5: migrate-with-data re-points a renamed column when the data source is at schema A`` () =
        if not (Deploy.Docker.ensureRunning ()) then () else
        TaskSync.run (fun () ->
            // Data source (Dev) at schema A (EMAIL) with seeded rows; sink (UAT) at A.
            fixture.WithEphemeralDatabase "MigrateRenameSrc" (fun source _ ->
                task {
                    do! Deploy.executeBatch source (SsdtDdlEmitter.statements catalogAcol |> Render.toText)
                    do! Deploy.executeBatch source
                            "INSERT INTO [dbo].[MIGAB_COL] ([ID],[EMAIL]) VALUES (1, N'alice@x'), (2, N'bob@x');"
                    do! fixture.WithEphemeralDatabase "MigrateRenameSink" (fun sink _ ->
                        task {
                            do! Deploy.executeBatch sink (SsdtDdlEmitter.statements catalogAcol |> Render.toText)

                            // One call: migrate the sink A→B (EMAIL→CONTACT), THEN
                            // load the source's A-named rows re-pointed onto CONTACT.
                            let! outcome =
                                MigrationRun.executeWithData DeclareNone Transfer.Execute true catalogAcol catalogBcol Map.empty source sink
                            match outcome with
                            | Error e -> Assert.Fail(sprintf "A5 rename-aware migrate-with-data failed: %A" e)
                            | Ok result ->
                                Assert.True(result.Schema.Verified, sprintf "sink schema not at B:\n%s" (PhysicalSchema.renderDiff result.Schema.SchemaDiff))
                                // THE DISCRIMINATOR: the renamed CONTACT column carries the
                                // source's EMAIL values — re-pointed A→B by identity, not NULL.
                                let! c1 = scalarString sink "SELECT [CONTACT] FROM [dbo].[MIGAB_COL] WHERE [ID]=1;"
                                Assert.Equal("alice@x", c1)
                                let! c2 = scalarString sink "SELECT [CONTACT] FROM [dbo].[MIGAB_COL] WHERE [ID]=2;"
                                Assert.Equal("bob@x", c2)
                                let! count = scalarString sink "SELECT COUNT(*) FROM [dbo].[MIGAB_COL];"
                                Assert.Equal("2", count)
                        })
                }))

    // 6.A.13 — schema-side CDC pre-flight. A migrate that would emit DDL
    // against a CDC-tracked DB is refused unless --allow-cdc (mirrors the
    // transfer gate). An UNCHANGED schema emits zero DDL, so it is a no-op
    // regardless of CDC — engine-level CDC-silence. Skips gracefully if the
    // container cannot enable CDC.
    [<Fact>]
    member _.``6.A.13: migrate refuses schema DDL against a CDC-tracked DB unless allow-cdc; unchanged is CDC-silent`` () =
        if not (Deploy.Docker.ensureRunning ()) then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "MigrateCdc" (fun conn _ ->
                task {
                    do! Deploy.executeBatch conn (SsdtDdlEmitter.statements catalogA |> Render.toText)
                    let! enabled =
                        task {
                            try
                                do! Deploy.executeBatch conn "EXEC sys.sp_cdc_enable_db;"
                                do! Deploy.executeBatch conn "EXEC sys.sp_cdc_enable_table @source_schema = N'dbo', @source_name = N'MIGAB_CUSTOMER', @role_name = NULL, @supports_net_changes = 0;"
                                use cmd = conn.CreateCommand()
                                cmd.CommandText <- "SELECT COUNT(*) FROM sys.tables WHERE is_tracked_by_cdc = 1 AND name = N'MIGAB_CUSTOMER'"
                                let! c = cmd.ExecuteScalarAsync()
                                return System.Convert.ToInt32 c > 0
                            with _ -> return false
                        }
                    if not enabled then
                        printfn "SKIP 6.A.13: container did not enable CDC (flag not set)"
                    else
                        // Unchanged schema (A→A): zero DDL → CDC-silent, proceeds even with allowCdc=false.
                        let! silent = MigrationRun.execute false DeclareNone catalogA catalogA conn
                        match silent with
                        | Ok r -> Assert.Empty(r.Artifacts.SchemaStatements)
                        | Error e -> Assert.Fail(sprintf "unchanged schema should be CDC-silent (no DDL), got %A" e)

                        // A real change (A→B) against the CDC-tracked DB refuses unless allow-cdc.
                        let! refused = MigrationRun.execute false DeclareNone catalogA catalogB conn
                        match refused with
                        | Error (MigrationError.RefusedByCdc tracked) ->
                            Assert.Contains("dbo.MIGAB_CUSTOMER", tracked)
                        | other -> Assert.Fail(sprintf "expected RefusedByCdc, got %A" other)

                        // --allow-cdc overrides → no longer BLOCKED BY THE GATE.
                        // (DDL against a CDC-tracked table may still fail at the
                        // SQL level — that is the unsafe operation the gate guards
                        // — but that is not a RefusedByCdc refusal.)
                        let! allowed = MigrationRun.execute true DeclareNone catalogA catalogB conn
                        match allowed with
                        | Error (MigrationError.RefusedByCdc _) ->
                            Assert.Fail("allow-cdc must bypass the CDC gate, not refuse")
                        | _ -> ()
                }))

    // -- AC-X4 — the redeploy protein: redeploy an unchanged model; pass iff
    //    zero ALTERs AND zero CDC captures, BOTH MEASURED ----------------------
    //
    // THE STANDARD (obligations §0): the prior 6.A.13 cell proved the migrate
    // emits zero DDL on an unchanged schema — but it never MEASURED CDC; it
    // *assumed* "no DDL ⇒ CDC-silent." X4's criterion demands the engine
    // surface the CDC capture count, not infer it. `executeAndMeasureCdc`
    // brackets the (no-op) execute with the change-measure ‖·‖
    // (`Deploy.cdcCaptureTotal`, the PRODUCTION reader) and returns the delta.
    //
    // TWO LEGS, criterion-first:
    //   1. Idempotent redeploy (A→A): zero statements (ALTERs measured) AND
    //      delta == 0 (captures MEASURED, not assumed).
    //   2. Meter-liveness discriminator: the SAME production primitive the
    //      migrate uses, bracketing one real INSERT, returns exactly +1. A
    //      reader hard-wired to 0 (the impostor that makes leg 1 look green for
    //      free) turns this RED; an over-counting reader overshoots.
    [<Fact>]
    member _.``AC-X4: redeploy of an unchanged model measures zero ALTERs and zero CDC captures (meter proven live)`` () =
        if not (Deploy.Docker.ensureRunning ()) then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "RedeployCdcMeasure" (fun conn _ ->
                task {
                    do! Deploy.executeBatch conn (SsdtDdlEmitter.statements catalogA |> Render.toText)
                    let! enabled =
                        task {
                            try
                                do! Deploy.executeBatch conn "EXEC sys.sp_cdc_enable_db;"
                                do! Deploy.executeBatch conn "EXEC sys.sp_cdc_enable_table @source_schema = N'dbo', @source_name = N'MIGAB_CUSTOMER', @role_name = NULL, @supports_net_changes = 0;"
                                let! c =
                                    scalarInt conn
                                        "SELECT COUNT(*) FROM sys.tables WHERE is_tracked_by_cdc = 1 AND name = N'MIGAB_CUSTOMER';"
                                return c > 0
                            with _ -> return false
                        }
                    if not enabled then
                        printfn "SKIP AC-X4: container did not enable CDC (flag not set)"
                    else
                        // Seed rows so the tracked substrate carries real, prior
                        // CDC traffic (proves the meter isn't counting an empty CT).
                        do! Deploy.executeBatch conn
                                "INSERT INTO [dbo].[MIGAB_CUSTOMER] ([ID],[EMAIL]) VALUES (1, N'a@x.com'), (2, N'b@x.com');"

                        // LEG 1 — idempotent redeploy (A→A) measures BOTH legs zero.
                        let! measured = MigrationRun.executeAndMeasureCdc false DeclareNone catalogA catalogA conn
                        match measured with
                        | Error e -> Assert.Fail(sprintf "idempotent redeploy should succeed, got %A" e)
                        | Ok (o, cdcDelta) ->
                            // Zero ALTERs: the differential plan is empty — the
                            // engine emits no DDL for an unchanged model. (NB: B'
                            // verification is deliberately NOT asserted here — CDC
                            // enable adds `cdc`-schema objects the readback sees, so
                            // Verified is confounded under CDC; the *plan* emptiness
                            // is the faithful "zero ALTERs" measure.)
                            Assert.Empty(o.Artifacts.SchemaStatements)   // zero ALTERs, measured
                            Assert.Equal(0, cdcDelta)                    // zero CDC captures, measured

                        // LEG 2 — meter-liveness: the SAME production primitive,
                        // bracketing one INSERT, reads exactly +1.
                        let! baselineR = Deploy.cdcCaptureTotal conn
                        let baseline = match baselineR with Ok n -> n | Error es -> failwithf "cdcCaptureTotal baseline: %A" es
                        do! Deploy.executeBatch conn
                                "INSERT INTO [dbo].[MIGAB_CUSTOMER] ([ID],[EMAIL]) VALUES (3, N'c@x.com');"
                        let! postR = Deploy.cdcCaptureTotal conn
                        let post = match postR with Ok n -> n | Error es -> failwithf "cdcCaptureTotal post: %A" es
                        Assert.Equal(baseline + 1, post)
                }))

    // -- AC-X8 — the canary protein: assert CDC-silence on idempotent redeploy --
    //
    // THE STANDARD (obligations §0): the wide canary already proves the
    // PhysicalSchema round-trip; X8 demands it ALSO surface protein P-9 — an
    // idempotent redeploy of the deployed target fires ZERO CDC captures — via
    // the PRODUCTION reader, not a harness. The SAME canary path
    // (`Deploy.runWideCanaryWithCdcSilence`, parameterized by the redeploy)
    // discriminates two legs:
    //   1. idempotent redeploy (`MigrationRun.execute tgt tgt`, empty
    //      differential) ⇒ diff empty AND measured CDC delta == 0.
    //   2. a data-churning redeploy (one INSERT) ⇒ measured CDC delta == +1,
    //      proving the canary's CDC meter is LIVE. A hard-wired-0 meter — the
    //      impostor that makes leg 1 green for free — turns this RED.
    [<Fact>]
    member _.``AC-X8: the canary measures CDC-silence on an idempotent redeploy (meter proven live)`` () =
        if not (Deploy.Docker.ensureRunning ()) then () else
        let sourceDdl =
            "CREATE TABLE [dbo].[X8_WIDGET] ( [ID] INT NOT NULL PRIMARY KEY, [LABEL] NVARCHAR(50) NOT NULL );"
        let enableCdc cnn (cat: Catalog) =
            task {
                do! Deploy.executeBatch cnn "EXEC sys.sp_cdc_enable_db;"
                for k in Catalog.allKinds cat do
                    do! Deploy.executeBatch cnn
                            (System.String.Concat(
                                "EXEC sys.sp_cdc_enable_table @source_schema=N'", TableId.schemaText k.Physical,
                                "', @source_name=N'", TableId.tableText k.Physical,
                                "', @role_name=NULL, @supports_net_changes=0;"))
            }
        let idempotentRedeploy cnn (cat: Catalog) =
            task { let! _ = MigrationRun.execute true DeclareNone cat cat cnn in return () }
        let churningRedeploy cnn (_cat: Catalog) =
            task {
                do! Deploy.executeBatch cnn
                        "INSERT INTO [dbo].[X8_WIDGET] ([ID],[LABEL]) VALUES (1, N'a');"
            }
        // LEG 1 — idempotent redeploy is CDC-silent.
        let silent =
            TaskSync.run (fun () ->
                Deploy.runWideCanaryWithCdcSilence
                    (fun cnn -> Deploy.executeBatch cnn sourceDdl)
                    SsdtDdlEmitter.statements enableCdc idempotentRedeploy)
        match silent with
        | Error es -> Assert.Fail(sprintf "X8 idempotent canary failed: %A" es)
        | Ok (report, delta) ->
            Assert.True(PhysicalSchema.isEqual report.Diff, "wide canary PhysicalSchema diff must be empty")
            Assert.Equal(0, delta)
        // LEG 2 — a data-churning redeploy fires CDC; the meter is live.
        let churned =
            TaskSync.run (fun () ->
                Deploy.runWideCanaryWithCdcSilence
                    (fun cnn -> Deploy.executeBatch cnn sourceDdl)
                    SsdtDdlEmitter.statements enableCdc churningRedeploy)
        match churned with
        | Error es -> Assert.Fail(sprintf "X8 churning canary failed: %A" es)
        | Ok (_, delta) -> Assert.Equal(1, delta)

    // -- AC-X5 — the in-place migrate-with-data protein: Move-data + Measure-CDC
    //    + Record. The recorded episode carries the MEASURED capture count ------
    //
    // THE STANDARD (obligations §0): the prior executeWithData cell proved
    // schema-migrate + data-move; X5 demands the chain's last two links —
    // MEASURE the data movement (the change-measure ‖·‖) and RECORD it durably.
    // `executeWithDataAndRecord` brackets the transfer with the production
    // reader and persists an episode whose `DataObservation` carries the
    // measured capture count. The witness:
    //   - moves 3 rows into a CDC-tracked sink ⇒ the recorded episode's
    //     CdcCaptureCount == 3 (non-empty observation; the measure is real, not
    //     the hard-wired-empty DataObservation the schema-only record uses);
    //   - that count round-trips through the durable store.
    [<Fact>]
    member _.``AC-X5: migrate-with-data measures the data movement and records the CDC count on the episode`` () =
        if not (Deploy.Docker.ensureRunning ()) then () else
        let storePath =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                System.String.Concat("x5-", System.Guid.NewGuid().ToString("N"), ".lifecycle.json"))
        try
            TaskSync.run (fun () ->
                // Source at B with 3 rows; sink at B and CDC-tracked — the data
                // load moves rows into the tracked sink, firing 3 captures.
                fixture.WithEphemeralDatabase "X5Src" (fun source _ ->
                    task {
                        do! Deploy.executeBatch source (SsdtDdlEmitter.statements catalogB |> Render.toText)
                        do! Deploy.executeBatch source
                                "INSERT INTO [dbo].[MIGAB_PATRON] ([ID],[EMAIL],[LOYALTY]) VALUES (1, N'a@x.com', 1), (2, N'b@x.com', 2), (3, N'c@x.com', 3);"
                        do! fixture.WithEphemeralDatabase "X5Sink" (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink (SsdtDdlEmitter.statements catalogB |> Render.toText)
                                let! enabled =
                                    task {
                                        try
                                            do! Deploy.executeBatch sink "EXEC sys.sp_cdc_enable_db;"
                                            do! Deploy.executeBatch sink "EXEC sys.sp_cdc_enable_table @source_schema=N'dbo', @source_name=N'MIGAB_PATRON', @role_name=NULL, @supports_net_changes=0;"
                                            let! c = scalarInt sink "SELECT COUNT(*) FROM sys.tables WHERE is_tracked_by_cdc = 1 AND name = N'MIGAB_PATRON';"
                                            return c > 0
                                        with _ -> return false
                                    }
                                if not enabled then
                                    printfn "SKIP AC-X5: container did not enable CDC (flag not set)"
                                else
                                    let tl = Timeline.create "x5" |> Result.value
                                    let at = System.DateTimeOffset.UtcNow
                                    let! outcome =
                                        MigrationRun.executeWithDataAndRecord
                                            DeclareNone Transfer.Execute true catalogB catalogB Map.empty
                                            storePath tl Environment.Dev at None source sink
                                    match outcome with
                                    | Error e -> Assert.Fail(sprintf "executeWithDataAndRecord failed: %A" e)
                                    | Ok (_, chain) ->
                                        // The 3 source rows landed in the tracked sink.
                                        let! count = scalarString sink "SELECT COUNT(*) FROM [dbo].[MIGAB_PATRON];"
                                        Assert.Equal("3", count)
                                        // The recorded episode carries the MEASURED capture count.
                                        let recorded = EpisodicLifecycle.latest chain
                                        Assert.True(recorded.Data.CdcCaptureCount > 0, "the recorded observation must be non-empty (data moved)")
                                        Assert.Equal(3, recorded.Data.CdcCaptureCount)
                                        // …and it round-trips through the durable store.
                                        match LifecycleStore.load storePath with
                                        | Error e -> Assert.Fail(sprintf "store reload failed: %A" e)
                                        | Ok reloaded ->
                                            Assert.Equal(3, (EpisodicLifecycle.latest reloaded).Data.CdcCaptureCount)
                            })
                    }))
        finally
            if System.IO.File.Exists storePath then System.IO.File.Delete storePath

    // -- AC-X7 — the drift protein: diff the DEPLOYED substrate vs THE MODEL ----
    //
    // THE STANDARD (obligations §0): the criterion is "diff deployed vs **the
    // model**". `verify-data` compares two DEPLOYED substrates and so cannot
    // detect that the single deployment has drifted from the authored intent.
    // `DriftRun.detect` reads the live schema and diffs it against the in-memory
    // model `Catalog`. Two legs discriminate:
    //   - deployed == model ⇒ no drift (empty PhysicalSchema diff);
    //   - deployed (A) ≠ model (B) ⇒ drift detected (non-empty diff).
    // A drift-blind impl that always reports clean turns leg 2 RED; the
    // deployed-vs-deployed impostor cannot even be expressed (one connection,
    // compared against intent).
    [<Fact>]
    member _.``AC-X7: drift detection diffs the deployed schema against the model (not a second deployment)`` () =
        if not (Deploy.Docker.ensureRunning ()) then () else
        // LEG A — deployed == model ⇒ no drift.
        let noDrift =
            TaskSync.run (fun () ->
                fixture.WithEphemeralDatabase "X7Match" (fun conn _ ->
                    task {
                        do! Deploy.executeBatch conn (SsdtDdlEmitter.statements catalogB |> Render.toText)
                        return! DriftRun.detect catalogB conn
                    }))
        match noDrift with
        | Error es -> Assert.Fail(sprintf "X7 no-drift read failed: %A" es)
        | Ok diff ->
            Assert.True(PhysicalSchema.isEqual diff, sprintf "deployed==model must show no drift:\n%s" (PhysicalSchema.renderDiff diff))
        // LEG B — deployed (A) differs from the model (B) ⇒ drift detected.
        let drift =
            TaskSync.run (fun () ->
                fixture.WithEphemeralDatabase "X7Drift" (fun conn _ ->
                    task {
                        do! Deploy.executeBatch conn (SsdtDdlEmitter.statements catalogA |> Render.toText)
                        return! DriftRun.detect catalogB conn
                    }))
        match drift with
        | Error es -> Assert.Fail(sprintf "X7 drift read failed: %A" es)
        | Ok diff ->
            Assert.False(PhysicalSchema.isEqual diff, "deployed A vs model B must surface drift")

    // -- AC-G10 — the resumable/idempotent transfer (crash-safety fork) --------
    //
    // THE STANDARD (obligations §0): a mid-load failure is recoverable by
    // re-running the SAME command — phase-tracked + idempotent, NOT a single
    // all-or-nothing transaction envelope. We simulate a CRASHED partial prior
    // attempt by seeding stale rows into the sink with NO completion marker (the
    // state a crash leaves). `Transfer.runResumable` clears that partial state
    // FK-first and reloads, then marks complete. Two legs:
    //   1. recovery: the stale rows are gone and exactly the source rows remain
    //      (no duplicates, complete state) — a non-resumable `run` would leave
    //      the stale rows behind (count > source);
    //   2. idempotence: a second identical run is a NO-OP (the marker is
    //      present) — counts unchanged, and the phase-marker row exists (a
    //      transaction-only impl with no phase tracking would have none).
    [<Fact>]
    member _.``AC-G10: a resumable transfer recovers a partial prior attempt with no duplicate rows`` () =
        if not (Deploy.Docker.ensureRunning ()) then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "G10Src" (fun source _ ->
                task {
                    do! Deploy.executeBatch source (SsdtDdlEmitter.statements catalogA |> Render.toText)
                    do! Deploy.executeBatch source
                            "INSERT INTO [dbo].[MIGAB_CUSTOMER] ([ID],[EMAIL]) VALUES (1,N'a@x'),(2,N'b@x'),(3,N'c@x');"
                    do! fixture.WithEphemeralDatabase "G10Sink" (fun sink _ ->
                        task {
                            do! Deploy.executeBatch sink (SsdtDdlEmitter.statements catalogA |> Render.toText)
                            // A crashed partial prior attempt: rows present, NO marker.
                            do! Deploy.executeBatch sink
                                    "INSERT INTO [dbo].[MIGAB_CUSTOMER] ([ID],[EMAIL]) VALUES (98,N'stale@x'),(99,N'stale@x');"
                            // LEG 1 — resume: clear the partial state, load, mark.
                            let! r1 = Transfer.runResumable Transfer.Execute true source sink catalogA
                            match r1 with
                            | Error es -> Assert.Fail(sprintf "resumable run failed: %A" es)
                            | Ok _ ->
                                let! c1 = scalarInt sink "SELECT COUNT(*) FROM [dbo].[MIGAB_CUSTOMER];"
                                Assert.Equal(3, c1)   // stale 98/99 wiped; exactly the 3 source rows. No dupes.
                                let! marks = scalarInt sink "SELECT COUNT(*) FROM dbo.__projection_transfer_progress;"
                                Assert.Equal(1, marks)   // phase-marker written (resumability mechanism, not just a txn).
                                // LEG 2 — idempotent re-run: marker present ⇒ no-op.
                                let! r2 = Transfer.runResumable Transfer.Execute true source sink catalogA
                                match r2 with
                                | Error es -> Assert.Fail(sprintf "resumable re-run failed: %A" es)
                                | Ok _ ->
                                    let! c2 = scalarInt sink "SELECT COUNT(*) FROM [dbo].[MIGAB_CUSTOMER];"
                                    Assert.Equal(3, c2)   // unchanged — re-running the same command duplicates nothing.
                        })
                }))

    // -- AC-D10 — wipe-and-load (the explicit destructive EmissionMode) --------
    //
    // THE STANDARD (obligations §0): WipeAndLoad is the operator-selected full
    // refresh — it CLEARS the sink's existing rows (FK-ordered) and reloads. The
    // discriminator: a sink pre-populated with a row whose value DIFFERS from
    // the source. Incremental (`run`) would PK-collide on that row (the
    // PROD-empty premise); WipeAndLoad wipes it first, so the reload succeeds and
    // the row carries the SOURCE value — proving the wipe actually replaced the
    // pre-existing state rather than appending to it.
    [<Fact>]
    member _.``AC-D10: WipeAndLoad clears pre-existing sink rows and reloads from source`` () =
        if not (Deploy.Docker.ensureRunning ()) then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "D10Src" (fun source _ ->
                task {
                    do! Deploy.executeBatch source (SsdtDdlEmitter.statements catalogA |> Render.toText)
                    do! Deploy.executeBatch source
                            "INSERT INTO [dbo].[MIGAB_CUSTOMER] ([ID],[EMAIL]) VALUES (1,N'fresh@x'),(2,N'b@x'),(3,N'c@x');"
                    do! fixture.WithEphemeralDatabase "D10Sink" (fun sink _ ->
                        task {
                            do! Deploy.executeBatch sink (SsdtDdlEmitter.statements catalogA |> Render.toText)
                            // Pre-existing sink state: ID 1 with a STALE value (would
                            // PK-collide an incremental load).
                            do! Deploy.executeBatch sink
                                    "INSERT INTO [dbo].[MIGAB_CUSTOMER] ([ID],[EMAIL]) VALUES (1,N'stale@x');"
                            let! r = Transfer.runWithEmissionMode Transfer.Execute WipeAndLoad true source sink catalogA
                            match r with
                            | Error es -> Assert.Fail(sprintf "wipe-and-load failed: %A" es)
                            | Ok _ ->
                                let! c = scalarInt sink "SELECT COUNT(*) FROM [dbo].[MIGAB_CUSTOMER];"
                                Assert.Equal(3, c)   // exactly the source rows (the stale row was wiped, not duplicated).
                                let! v = scalarString sink "SELECT [EMAIL] FROM [dbo].[MIGAB_CUSTOMER] WHERE [ID]=1;"
                                Assert.Equal("fresh@x", v)   // ID 1 carries the SOURCE value ⇒ wipe replaced the stale state.
                        })
                }))

    // -- AC-X1 (part A, live) — the full-export seed bundle deployed to a
    //    FRESH-BLANK database is non-overwriting / CDC-silent on re-run --------
    //
    // THE STANDARD (obligations §0): the operator-resolved premise — full-export
    // emits the static-entity INSERT scripts for a from-fresh-blank-database
    // deploy, and they are idempotent (a MERGE), so a re-run lands the same state
    // and is CDC-silent. This drives the PRODUCTION bundle end-to-end: project
    // (EmitData) → deploy the DDL + `Data/seed.sql` to a fresh DB → enable CDC →
    // re-run the SAME seed → assert rows present AND the re-run fired ZERO CDC
    // captures. A bare-INSERT impostor would PK-collide/duplicate on the re-run;
    // an over-writing MERGE would fire captures.
    [<Fact>]
    member _.``AC-X1: the full-export seed bundle is non-overwriting and CDC-silent on a fresh-blank-DB re-run`` () =
        if not (Deploy.Docker.ensureRunning ()) then () else
        let catalog =
            StaticCatalogFixtures.staticCatalog "OS_X1L" "Mod" [ "Mod"; "Country" ] "Country" "Country"
                [ StaticCatalogFixtures.pk "Id" "ID" Integer
                  StaticCatalogFixtures.attr "Code" "CODE" Text
                  StaticCatalogFixtures.attr "Label" "LABEL" Text ]
                [ "1", [ "1"; "1"; "United States" ]
                  "2", [ "2"; "2"; "Canada" ] ]
        let policy =
            { Policy.empty with Emission = { Policy.empty.Emission with EmitData = true; DataComposition = AllRemaining } }
        let outputs =
            Compose.projectWithState policy Profile.empty EmissionFolders.empty TransformGroups.empty catalog
            |> fst
        let ddl = Compose.aggregateSsdt outputs.SsdtBundle
        // The fused Data/seed.sql FILE is retired (per-lane artifacts, DECISIONS
        // 2026-06-14); this single-static-lane catalog's idempotent MERGE is now
        // the Data/StaticSeeds.sql lane. (The prior retirement missed this
        // Docker-only AC-X1 pair.)
        let seed = Map.find "Data/StaticSeeds.sql" outputs.DataBundle
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "X1FreshSeed" (fun conn _ ->
                task {
                    // Fresh-blank DB: deploy schema, run the seed (first load).
                    do! Deploy.executeBatch conn ddl
                    do! Deploy.executeBatch conn seed
                    let! loaded = scalarInt conn "SELECT COUNT(*) FROM [dbo].[Country];"
                    Assert.Equal(2, loaded)   // the static rows landed on the fresh DB.
                    let! enabled =
                        task {
                            try
                                do! Deploy.executeBatch conn "EXEC sys.sp_cdc_enable_db;"
                                do! Deploy.executeBatch conn "EXEC sys.sp_cdc_enable_table @source_schema=N'dbo', @source_name=N'Country', @role_name=NULL, @supports_net_changes=0;"
                                let! c = scalarInt conn "SELECT COUNT(*) FROM sys.tables WHERE is_tracked_by_cdc = 1 AND name = N'Country';"
                                return c > 0
                            with _ -> return false
                        }
                    if not enabled then
                        printfn "SKIP AC-X1 (live): container did not enable CDC"
                    else
                        let! baselineR = Deploy.cdcCaptureTotal conn
                        let baseline = match baselineR with Ok n -> n | Error es -> failwithf "cdcCaptureTotal baseline: %A" es
                        do! Deploy.executeBatch conn seed   // re-run the SAME seed script
                        let! postR = Deploy.cdcCaptureTotal conn
                        let post = match postR with Ok n -> n | Error es -> failwithf "cdcCaptureTotal post: %A" es
                        let! after = scalarInt conn "SELECT COUNT(*) FROM [dbo].[Country];"
                        Assert.Equal(2, after)            // still 2 — non-overwriting, no dupes.
                        Assert.Equal(baseline, post)     // zero CDC captures on the idempotent re-run.
                }))

    // -- AC-X1 (part B, live) — the full-export DATA-LOAD leg: load + MEASURE +
    //    RECORD, CDC-silent on the idempotent re-run --------------------------
    //
    // THE STANDARD (obligations §0): the load protein's last links — Move-data
    // (the seed MERGE) → Measure-CDC (the ‖·‖) → Record (the episode carries the
    // MEASURED DataObservation, not the X3 store leg's empty one). `loadSeedAndRecord`
    // loads the published seed into a deployed + CDC-tracked sink, measures the
    // capture count, and records the episode. Two legs:
    //   1. first load ⇒ the measured delta == the rows inserted (2), and the
    //      recorded episode's CdcCaptureCount == 2 (an empty-observation impostor
    //      — the X3 behavior — records 0 and turns this RED);
    //   2. idempotent re-load ⇒ delta == 0 (CDC-silent) and the rows are
    //      unchanged (non-overwriting; a bare-INSERT impostor would PK-collide /
    //      duplicate).
    [<Fact>]
    member _.``AC-X1: the full-export load leg measures the data movement and records it; the re-load is CDC-silent`` () =
        if not (Deploy.Docker.ensureRunning ()) then () else
        let catalog =
            StaticCatalogFixtures.staticCatalog "OS_X1B" "Mod" [ "Mod"; "Country" ] "Country" "Country"
                [ StaticCatalogFixtures.pk "Id" "ID" Integer
                  StaticCatalogFixtures.attr "Code" "CODE" Text
                  StaticCatalogFixtures.attr "Label" "LABEL" Text ]
                [ "1", [ "1"; "1"; "United States" ]
                  "2", [ "2"; "2"; "Canada" ] ]
        let policy =
            { Policy.empty with Emission = { Policy.empty.Emission with EmitData = true; DataComposition = AllRemaining } }
        let outputs =
            Compose.projectWithState policy Profile.empty EmissionFolders.empty TransformGroups.empty catalog
            |> fst
        let ddl = Compose.aggregateSsdt outputs.SsdtBundle
        // The fused Data/seed.sql FILE is retired (per-lane artifacts, DECISIONS
        // 2026-06-14); this single-static-lane catalog's idempotent MERGE is now
        // the Data/StaticSeeds.sql lane. (The prior retirement missed this
        // Docker-only AC-X1 pair.)
        let seed = Map.find "Data/StaticSeeds.sql" outputs.DataBundle
        let storePath =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                System.String.Concat("x1b-", System.Guid.NewGuid().ToString("N"), ".lifecycle.json"))
        try
            TaskSync.run (fun () ->
                fixture.WithEphemeralDatabase "X1LoadLeg" (fun conn _ ->
                    task {
                        // The schema is deployed + CDC-tracked (the publish→deploy→load
                        // premise) BEFORE the data lands, so the load's captures are real.
                        do! Deploy.executeBatch conn ddl
                        let! enabled =
                            task {
                                try
                                    do! Deploy.executeBatch conn "EXEC sys.sp_cdc_enable_db;"
                                    do! Deploy.executeBatch conn "EXEC sys.sp_cdc_enable_table @source_schema=N'dbo', @source_name=N'Country', @role_name=NULL, @supports_net_changes=0;"
                                    let! c = scalarInt conn "SELECT COUNT(*) FROM sys.tables WHERE is_tracked_by_cdc = 1 AND name = N'Country';"
                                    return c > 0
                                with _ -> return false
                            }
                        if not enabled then
                            printfn "SKIP AC-X1 (load leg): container did not enable CDC"
                        else
                            let tl = Timeline.create "x1load" |> Result.value
                            let at = System.DateTimeOffset.UtcNow
                            // LEG 1 — first load: measure + record.
                            let! first = Compose.loadSeedAndRecord Deploy.cdcCaptureTotal Deploy.executeBatch catalog seed conn (Some storePath) tl Environment.Dev at
                            match first with
                            | Error es -> Assert.Fail(sprintf "load leg failed: %A" es)
                            | Ok (legOpt, delta1) ->
                                Assert.Equal(2, delta1)   // the 2 static rows were captured (measured).
                                match legOpt with
                                | None -> Assert.Fail("a store was supplied; an episode must be recorded")
                                | Some leg ->
                                    let recorded = EpisodicLifecycle.latest leg.Chain
                                    Assert.Equal(2, recorded.Data.CdcCaptureCount)   // the episode carries the MEASURED count.
                            // LEG 2 — idempotent re-load: CDC-silent, non-overwriting.
                            let! second = Compose.loadSeedAndRecord Deploy.cdcCaptureTotal Deploy.executeBatch catalog seed conn None tl Environment.Dev at
                            match second with
                            | Error es -> Assert.Fail(sprintf "re-load failed: %A" es)
                            | Ok (_, delta2) ->
                                Assert.Equal(0, delta2)   // CDC-silent on the idempotent re-load.
                                let! rows = scalarInt conn "SELECT COUNT(*) FROM [dbo].[Country];"
                                Assert.Equal(2, rows)     // non-overwriting — still 2, no dupes.
                    }))
        finally
            if System.IO.File.Exists storePath then System.IO.File.Delete storePath

    // -- Card P2 (live) — the PRODUCTION load shape: the seed deploys as the
    //    LEVELED plan through `Deploy.executeLeveledSeed` ----------------------
    //
    // THE STANDARD: `runFullExportLoad` now rides `Compose.loadLeveledSeedAndRecord`
    // + `Deploy.executeLeveledSeed <connStr>` (card P2 — the gate-met promotion).
    // This witnesses that face's exact dispatch end-to-end: the plan is minted
    // through the REAL prover (`composeRenderedLeveled` → `TopologicalOrder.levels`
    // → `ParallelSafe`), two independent static kinds share ONE level (genuinely
    // concurrent members), the rows land, the movement is MEASURED and RECORDED,
    // and the idempotent re-load is CDC-silent — the same contract the fused
    // sequential leg (`loadSeedAndRecord`, above) witnesses for the published
    // `Data/seed.sql` artifact.
    [<Fact>]
    member _.``P2: the leveled load leg lands the seed through ParallelSafe levels, measures it, and is CDC-silent on the re-load`` () =
        if not (Deploy.Docker.ensureRunning ()) then () else
        let catalog =
            StaticCatalogFixtures.staticCatalogOfKinds "OS_P2L" "Mod"
                [ { StaticCatalogFixtures.KindKeyParts = [ "Mod"; "P2LCountry" ]
                    KindName = "P2LCountry"
                    PhysicalTable = "P2LCountry"
                    Attrs =
                      [ StaticCatalogFixtures.pk "Id" "ID" Integer
                        StaticCatalogFixtures.attr "Label" "LABEL" Text ]
                    Rows =
                      [ "1", [ "1"; "United States" ]
                        "2", [ "2"; "Canada" ] ]
                  }
                  { StaticCatalogFixtures.KindKeyParts = [ "Mod"; "P2LRegion" ]
                    KindName = "P2LRegion"
                    PhysicalTable = "P2LRegion"
                    Attrs =
                      [ StaticCatalogFixtures.pk "Id" "ID" Integer
                        StaticCatalogFixtures.attr "Label" "LABEL" Text ]
                    Rows =
                      [ "1", [ "1"; "North" ]
                        "2", [ "2"; "South" ] ]
                  } ]
        let policy =
            { Policy.empty with Emission = { Policy.empty.Emission with EmitData = true; DataComposition = AllRemaining } }
        let outputs, finalState =
            Compose.projectWithState policy Profile.empty EmissionFolders.empty TransformGroups.empty catalog
        let ddl = Compose.aggregateSsdt outputs.SsdtBundle
        // The plan composes over the POST-CHAIN catalog — exactly what the
        // production seam (`Compose.projectSeedPlan`) does, so the MERGEs
        // target the same (logical-named) tables the deployed DDL created.
        let plan =
            Projection.Targets.Data.DataEmissionComposer.composeRenderedLeveled
                policy finalState.Catalog Profile.empty
                Projection.Targets.Data.MigrationDependencyContext.empty UserRemapContext.empty
            |> function
               | Ok p -> p
               | Error e -> failwithf "composeRenderedLeveled: %A" e
        // The plan shape the prover minted: two FK-independent kinds form ONE
        // Phase-1 level with TWO members (genuine within-level parallelism);
        // no cycles ⇒ no Phase-2 levels.
        Assert.Equal(1, List.length plan.Phase1Levels)
        Assert.Equal(2, ParallelSafe.members (List.head plan.Phase1Levels) |> List.length)
        Assert.Empty plan.Phase2Levels
        let storePath =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                System.String.Concat("p2lvl-", System.Guid.NewGuid().ToString("N"), ".lifecycle.json"))
        try
            TaskSync.run (fun () ->
                fixture.WithEphemeralDatabase "P2LeveledLoad" (fun conn perDbConn ->
                    task {
                        do! Deploy.executeBatch conn ddl
                        let! enabled =
                            task {
                                try
                                    do! Deploy.executeBatch conn "EXEC sys.sp_cdc_enable_db;"
                                    do! Deploy.executeBatch conn "EXEC sys.sp_cdc_enable_table @source_schema=N'dbo', @source_name=N'P2LCountry', @role_name=NULL, @supports_net_changes=0;"
                                    do! Deploy.executeBatch conn "EXEC sys.sp_cdc_enable_table @source_schema=N'dbo', @source_name=N'P2LRegion', @role_name=NULL, @supports_net_changes=0;"
                                    let! c = scalarInt conn "SELECT COUNT(*) FROM sys.tables WHERE is_tracked_by_cdc = 1 AND name IN (N'P2LCountry', N'P2LRegion');"
                                    return c = 2
                                with _ -> return false
                            }
                        if not enabled then
                            printfn "SKIP P2 (leveled load leg): container did not enable CDC"
                        else
                            let tl = Timeline.create "p2load" |> Result.value
                            let at = System.DateTimeOffset.UtcNow
                            // LEG 1 — first leveled load: the face's exact dispatch
                            // (executeLeveledSeed over the per-DB connection STRING).
                            let! first = Compose.loadLeveledSeedAndRecord Deploy.cdcCaptureTotal (Deploy.executeLeveledSeed perDbConn) catalog plan conn (Some storePath) tl Environment.Dev at
                            match first with
                            | Error es -> Assert.Fail(sprintf "leveled load leg failed: %A" es)
                            | Ok (legOpt, delta1) ->
                                Assert.Equal(4, delta1)   // 2 + 2 static rows captured (measured).
                                match legOpt with
                                | None -> Assert.Fail("a store was supplied; an episode must be recorded")
                                | Some leg ->
                                    let recorded = EpisodicLifecycle.latest leg.Chain
                                    Assert.Equal(4, recorded.Data.CdcCaptureCount)
                            // LEG 2 — idempotent leveled re-load: CDC-silent, non-overwriting.
                            let! second = Compose.loadLeveledSeedAndRecord Deploy.cdcCaptureTotal (Deploy.executeLeveledSeed perDbConn) catalog plan conn None tl Environment.Dev at
                            match second with
                            | Error es -> Assert.Fail(sprintf "leveled re-load failed: %A" es)
                            | Ok (_, delta2) ->
                                Assert.Equal(0, delta2)
                                let! countries = scalarInt conn "SELECT COUNT(*) FROM [dbo].[P2LCountry];"
                                let! regions   = scalarInt conn "SELECT COUNT(*) FROM [dbo].[P2LRegion];"
                                Assert.Equal(2, countries)
                                Assert.Equal(2, regions)
                    }))
        finally
            if System.IO.File.Exists storePath then System.IO.File.Delete storePath

    // -- AC-S8 — RENAME is CDC-transparent (‖rename‖_data = 0) ----------------
    //
    // THE STANDARD (obligations §0): a discriminating LIVE witness. A RENAME via
    // `sp_rename` (the V2 rename path — `MigrationRun.execute`'s
    // `renameStatements`) is a metadata-only operation: it relabels the object
    // in the catalog and touches NO data pages, so SQL Server's CDC log-scan
    // (`sys.sp_cdc_scan`) finds zero data-change records to capture. A data
    // UPDATE on the same CDC-tracked table, by contrast, writes a row version
    // the log-scan captures. The canary distinguishes the two: rename ⇒ 0 net
    // captures; update ⇒ > 0.
    //
    // WRONG IMPL THIS CATCHES: a "rename" implemented as DROP + re-CREATE (or,
    // for the column case, DROP COLUMN + ADD COLUMN) — the lossy reshape an
    // emitter reaches for when it can't carry the same SsKey across A→B. That
    // rewrites every row (and, for a table re-CREATE, re-keys the CDC capture
    // instance), firing CDC captures and losing data. The
    // `renameStatements`/`sp_rename` path is CDC-transparent; the
    // drop+recreate impostor is not.
    //
    // CDC-AFTER-RENAME CAVEAT (empirically established against the live
    // container, NOT assumed): under CDC, SQL Server BLOCKS a *column* rename —
    // `sp_rename '…','…','COLUMN'` raises "Cannot alter column … because it is
    // 'REPLICATED'" (CDC marks every captured column as replicated). So the
    // column-rename case cannot be a "0 captures" witness — it errors before
    // capturing anything. The *table* rename, however, executes cleanly and IS
    // CDC-transparent: the capture instance (`cdc.dbo_<table>_CT`) is bound to
    // the object_id + the enable-time name, so the table object renames while
    // the capture instance keeps its original name and zero rows are captured.
    // We therefore witness AC-S8 with the TABLE rename (the executable rename
    // under CDC) and seed/UPDATE through a column that is unchanged by the
    // rename, so the sensitivity half exercises real capture on the live
    // instance. (The capture instance survives the table rename — verified by
    // the UPDATE leg firing > 0.)
    [<Fact>]
    member _.``AC-S8: a table rename via sp_rename is CDC-transparent (0 net captures) while a data UPDATE fires`` () =
        if not (Deploy.Docker.ensureRunning ()) then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "RenameCdc" (fun conn _ ->
                task {
                    // Deploy state A (MIGAB_RENSRC with ID + EMAIL) and seed a row.
                    do! Deploy.executeBatch conn (SsdtDdlEmitter.statements catalogRenA |> Render.toText)
                    do! Deploy.executeBatch conn
                            "INSERT INTO [dbo].[MIGAB_RENSRC] ([ID],[EMAIL]) VALUES (1, N'carol@example.com');"

                    // Enable CDC on the database + the seeded table. Skip if the
                    // container image can't (mirrors 6.A.13's graceful skip).
                    let! enabled =
                        task {
                            try
                                do! Deploy.executeBatch conn "EXEC sys.sp_cdc_enable_db;"
                                do! Deploy.executeBatch conn
                                        "EXEC sys.sp_cdc_enable_table @source_schema=N'dbo', @source_name=N'MIGAB_RENSRC', @role_name=NULL, @supports_net_changes=0;"
                                let! c =
                                    scalarInt conn
                                        "SELECT COUNT(*) FROM sys.tables WHERE is_tracked_by_cdc = 1 AND name = N'MIGAB_RENSRC';"
                                return c > 0
                            with _ -> return false
                        }
                    if not enabled then
                        printfn "SKIP AC-S8: container did not enable CDC (flag not set)"
                    else
                        // The capture-instance name is bound at enable-time and
                        // survives the table rename — so we count the SAME capture
                        // table (`cdc.dbo_MIGAB_RENSRC_CT`) across all three phases.
                        let captureCountSql =
                            "SELECT COUNT(*) FROM cdc.[dbo_MIGAB_RENSRC_CT];"

                        // Force the synchronous log-scan and capture the baseline.
                        do! Deploy.executeBatch conn "EXEC sys.sp_cdc_scan;"
                        let! baseline = scalarInt conn captureCountSql

                        // === RENAME LEG: the V2 rename path (sp_rename of the
                        // table object) on the CDC-tracked table. allowCdc=true
                        // bypasses the schema-side CDC GATE (6.A.13) — the gate
                        // guards unsafe DDL; the point here is that the rename,
                        // once executed, is CDC-TRANSPARENT at the data-capture
                        // level.
                        let! outcome = MigrationRun.execute true DeclareNone catalogRenA catalogRenB conn
                        match outcome with
                        | Error e -> Assert.Fail(sprintf "table-rename migrate failed: %A" e)
                        | Ok _ ->
                            // The rename actually happened: old object gone, new present.
                            let! oldExists =
                                scalarInt conn
                                    "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='MIGAB_RENSRC';"
                            Assert.Equal(0, oldExists)
                            let! newExists =
                                scalarInt conn
                                    "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='MIGAB_RENDST';"
                            Assert.Equal(1, newExists)

                            // Scan again: the rename fired ZERO data captures.
                            do! Deploy.executeBatch conn "EXEC sys.sp_cdc_scan;"
                            let! postRename = scalarInt conn captureCountSql

                            // THE LOAD-BEARING ASSERTION: ‖rename‖_data = 0.
                            Assert.Equal(baseline, postRename)

                            // === SENSITIVITY (UPDATE) LEG: a data UPDATE on the
                            // (now-renamed) CDC-tracked table DOES fire — proving
                            // the canary isn't trivially silent (CDC plumbing IS
                            // live and the capture instance survived the rename).
                            do! Deploy.executeBatch conn
                                    "UPDATE [dbo].[MIGAB_RENDST] SET [EMAIL] = N'carol+changed@example.com' WHERE [ID] = 1;"
                            do! Deploy.executeBatch conn "EXEC sys.sp_cdc_scan;"
                            let! postUpdate = scalarInt conn captureCountSql

                            // THE SENSITIVITY ASSERTION: the UPDATE fired capture
                            // rows the rename did not. Discriminates: rename == 0
                            // net; update > baseline.
                            Assert.True(
                                postUpdate > postRename,
                                sprintf "expected the data UPDATE to fire CDC captures the rename did not; baseline=%d postRename=%d postUpdate=%d"
                                    baseline postRename postUpdate)
                }))

    // G9 (NEITHER→HELD) — the NOT-NULL tightening pre-flight. An in-place
    // `ALTER COLUMN … NOT NULL` against a column that still carries NULL rows
    // must be REFUSED by a PRE-FLIGHT (a `COUNT(*) WHERE col IS NULL` probe)
    // BEFORE the ALTER is submitted — surfacing the NAMED `RefusedByTightening`
    // refusal, NOT the post-facto `ExecutionFailed` the bare ALTER would raise.
    // The test DISCRIMINATES the two: it asserts the named pre-flight error AND
    // that NO DDL ran (the column stays nullable, the rows — NULL included —
    // are intact). Uses `DeclareAll` so the schema-blind narrowing declared-loss
    // gate (G8) is already SATISFIED — isolating G9's DATA-aware refusal as the
    // sole remaining line: declaring you accept the narrowing does NOT let you
    // apply NOT NULL while NULL rows physically remain.
    [<Fact>]
    member _.``G9: migrate refuses a NOT-NULL tightening on NULL-bearing data via a pre-flight, before any ALTER`` () =
        if not (Deploy.Docker.ensureRunning ()) then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "MigrateTighten" (fun conn _ ->
                task {
                    // Deploy state A (NOTES nullable) and seed BOTH a non-NULL
                    // row and a NULL-bearing row — the data that violates a
                    // NOT-NULL tightening.
                    do! Deploy.executeBatch conn (SsdtDdlEmitter.statements catalogTightenA |> Render.toText)
                    do! Deploy.executeBatch conn
                            "INSERT INTO [dbo].[MIGAB_TIGHTEN] ([ID],[NOTES]) VALUES (1, N'present');"
                    do! Deploy.executeBatch conn
                            "INSERT INTO [dbo].[MIGAB_TIGHTEN] ([ID],[NOTES]) VALUES (2, NULL);"

                    // migrate A → B tightens NOTES to NOT NULL. The live data
                    // carries a NULL → the pre-flight must REFUSE before the ALTER.
                    let! outcome = MigrationRun.execute true DeclareAll catalogTightenA catalogTightenB conn
                    match outcome with
                    | Error (MigrationError.RefusedByTightening msg) ->
                        // DISCRIMINATOR: the NAMED pre-flight refusal, carrying the
                        // probe's `migrate.dataViolatesTightening` evidence — NOT a
                        // generic `ExecutionFailed` from a submitted-then-rejected ALTER.
                        Assert.Contains("NULL", msg)
                    | Error (MigrationError.ExecutionFailed m) ->
                        Assert.Fail(
                            sprintf "G9 regression: the tightening was caught POST-FACTO as ExecutionFailed (%s), not refused by a PRE-FLIGHT before the ALTER." m)
                    | other ->
                        Assert.Fail(sprintf "expected RefusedByTightening (pre-flight), got %A" other)

                    // NO DDL RAN — the column is STILL NULLABLE (the ALTER never
                    // reached the live DB). A post-facto failure would have left
                    // the schema in whatever state the rejected ALTER left it; a
                    // true pre-flight leaves A untouched.
                    let! isNullable =
                        scalarString conn
                            "SELECT IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS \
                             WHERE TABLE_NAME = 'MIGAB_TIGHTEN' AND COLUMN_NAME = 'NOTES';"
                    Assert.Equal("YES", isNullable)

                    // The rows are intact, NULL included.
                    let! count = scalarString conn "SELECT COUNT(*) FROM [dbo].[MIGAB_TIGHTEN];"
                    Assert.Equal("2", count)
                    let! nullCount =
                        scalarString conn "SELECT COUNT(*) FROM [dbo].[MIGAB_TIGHTEN] WHERE [NOTES] IS NULL;"
                    Assert.Equal("1", nullCount)

                    // And the gate is DATA-driven, not blanket: with the NULL row
                    // REMEDIATED, the same tightening proceeds and verifies. This
                    // proves the refusal was the probe (NULL-count), not a refusal
                    // of all tightenings.
                    do! Deploy.executeBatch conn
                            "UPDATE [dbo].[MIGAB_TIGHTEN] SET [NOTES] = N'backfilled' WHERE [NOTES] IS NULL;"
                    let! afterFix = MigrationRun.execute true DeclareAll catalogTightenA catalogTightenB conn
                    match afterFix with
                    | Error e -> Assert.Fail(sprintf "remediated tightening should proceed, got %A" e)
                    | Ok r ->
                        Assert.True(r.Verified, sprintf "B' did not reproduce B after remediation:\n%s" (PhysicalSchema.renderDiff r.SchemaDiff))
                        let! nowNullable =
                            scalarString conn
                                "SELECT IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS \
                                 WHERE TABLE_NAME = 'MIGAB_TIGHTEN' AND COLUMN_NAME = 'NOTES';"
                        Assert.Equal("NO", nowNullable)
                }))

    // -- AC-X2 — the one-command UAT re-key composition. THE STANDARD
    //    (obligations §0): a witness that FAILS for the co-wrong `Map.empty`
    //    implementation. The criterion (acceptance ~136): "One command:
    //    schema-migrate UAT AND re-key user FKs by email. Pass iff
    //    schema+data+re-key compose with `validate-user-map` gating first."
    //
    //    These two tests pass a NON-EMPTY reconciliation map through the SAME
    //    `MigrationRun.executeWithData` the CLI verb now drives, with an
    //    ADVERSARIAL identity layout: the Source surrogate IDs DIFFER from the
    //    pre-existing Sink (UAT) identities, and a Source user's PK collides with
    //    a DIFFERENT Sink entity by ID while matching a THIRD Sink entity by
    //    EMAIL. A `Map.empty` composition re-keys nothing: it straight-loads the
    //    USER kind and collides on the engineered id-clash (the run cannot
    //    complete) and, even where it could, the Order FK would land on the
    //    id-collision identity, never the email match. The real map reconciles the
    //    colliding IDs away (USER ⇒ ReconciledByRule, no insert) and re-points
    //    every Order FK to the Sink's email-matched identity. The load-bearing
    //    assertion is the re-pointed FK; it is unreachable for `Map.empty`. The
    //    second test witnesses the `validate-user-map` halt firing PRE-WRITE on an
    //    orphan (the gate composes first — the Sink data write never happens).
    //
    //    Schema A (USER.EMAIL NVARCHAR(50)) → B (widened to NVARCHAR(256)); USER
    //    reconciled by EMAIL, ORDER carries a NOT-NULL FK to USER. The EMAIL
    //    widening is the real schema touch the data leg composes WITH. Contracts
    //    are reconstructed (ReadSide.read) so the FK + kind SsKeys come for free
    //    and the A→B diff is a pure widening (same kind SsKeys, same names).

    [<Fact>]
    member _.``AC-X2: one-command migrate-with-data re-keys Order FKs to the Sink's email-matched identity (fails for Map.empty)`` () =
        if not (Deploy.Docker.ensureRunning ()) then () else
        let reKeyDdlA =
            "CREATE TABLE [dbo].[MIGAB_RK_USER] ([ID] INT NOT NULL PRIMARY KEY, [EMAIL] NVARCHAR(50) NULL); \
             CREATE TABLE [dbo].[MIGAB_RK_ORDER] ([ID] INT NOT NULL PRIMARY KEY, [USER_ID] INT NOT NULL, [AMOUNT] INT NULL, \
             CONSTRAINT [FK_RkOrder_User] FOREIGN KEY ([USER_ID]) REFERENCES [dbo].[MIGAB_RK_USER] ([ID]));"
        let reKeyDdlB =
            "CREATE TABLE [dbo].[MIGAB_RK_USER] ([ID] INT NOT NULL PRIMARY KEY, [EMAIL] NVARCHAR(256) NULL); \
             CREATE TABLE [dbo].[MIGAB_RK_ORDER] ([ID] INT NOT NULL PRIMARY KEY, [USER_ID] INT NOT NULL, [AMOUNT] INT NULL, \
             CONSTRAINT [FK_RkOrder_User] FOREIGN KEY ([USER_ID]) REFERENCES [dbo].[MIGAB_RK_USER] ([ID]));"
        TaskSync.run (fun () ->
            // SOURCE (Dev) at schema B; SINK (UAT) at schema A — executeWithData
            // migrates the sink A→B (widening EMAIL), then re-keys.
            fixture.WithEphemeralDatabase "MigrateReKeySrc" (fun source _ ->
                task {
                    do! Deploy.executeBatch source reKeyDdlB
                    // Dev users 7/8 — surrogates that DIFFER from UAT's. Source user
                    // 7 (alice) collides BY ID with the UAT entity at 7 (carol@x),
                    // but matches the UAT alice (at id 1) BY EMAIL — the adversary.
                    do! Deploy.executeBatch source
                            "INSERT INTO [dbo].[MIGAB_RK_USER] ([ID],[EMAIL]) VALUES (7,N'alice@x'),(8,N'bob@x'); \
                             INSERT INTO [dbo].[MIGAB_RK_ORDER] ([ID],[USER_ID],[AMOUNT]) VALUES (100,7,500),(101,8,600);"
                    return!
                        fixture.WithEphemeralDatabase "MigrateReKeySink" (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink reKeyDdlA
                                // Pre-existing UAT users — SAME emails, DIFFERENT surrogates,
                                // PLUS a decoy (carol@x) occupying id 7 so the Source PK 7
                                // collides with a DIFFERENT Sink entity by id.
                                do! Deploy.executeBatch sink
                                        "INSERT INTO [dbo].[MIGAB_RK_USER] ([ID],[EMAIL]) VALUES (1,N'alice@x'),(2,N'bob@x'),(7,N'carol@x');"

                                let! sinkAR = Projection.Adapters.Sql.ReadSide.read sink
                                let sinkA = Result.value sinkAR
                                let! targetBR = Projection.Adapters.Sql.ReadSide.read source
                                let targetB = Result.value targetBR

                                let userKind =
                                    Catalog.allModulesKinds targetB |> List.map snd
                                    |> List.find (fun k -> TableId.tableText k.Physical = "MIGAB_RK_USER")
                                let emailName =
                                    userKind.Attributes
                                    |> List.find (fun a -> ColumnRealization.columnNameText a.Column = "EMAIL")
                                    |> fun a -> a.Name
                                // NON-EMPTY map: reconcile USER by EMAIL — the re-key.
                                let reconciliation =
                                    Map.ofList [ userKind.SsKey, ReconciliationStrategy.MatchByColumn emailName ]

                                // One command: schema-migrate the sink A→B AND re-key.
                                let! outcome =
                                    MigrationRun.executeWithData DeclareNone Transfer.Execute true sinkA targetB reconciliation source sink
                                match outcome with
                                | Error e -> Assert.Fail(sprintf "AC-X2 composed run failed: %A" e)
                                | Ok result ->
                                    // (compose) The schema leg landed — EMAIL widened to 256.
                                    Assert.True(result.Schema.Verified, sprintf "sink schema not at B:\n%s" (PhysicalSchema.renderDiff result.Schema.SchemaDiff))
                                    let! widened =
                                        scalarString sink
                                            "SELECT CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS \
                                             WHERE TABLE_NAME='MIGAB_RK_USER' AND COLUMN_NAME='EMAIL';"
                                    Assert.Equal("256", widened)

                                    // (re-key) USER is ReconciledByRule — its rows are the
                                    // pre-existing UAT identities, NOT re-inserted.
                                    let userOutcome = result.Transfer.Kinds |> List.find (fun k -> k.Kind = userKind.SsKey)
                                    Assert.Equal(IdentityDisposition.ReconciledByRule, userOutcome.Disposition)
                                    Assert.Equal(0, userOutcome.RowsWritten)

                                    // No UAT user was added by the load — the 3 pre-existing stay.
                                    let! users = scalarString sink "SELECT COUNT(*) FROM [dbo].[MIGAB_RK_USER];"
                                    Assert.Equal("3", users)

                                    // THE DISCRIMINATOR: Order 100 (Source USER_ID=7) re-points to
                                    // the Sink's EMAIL-matched alice@x (id 1) — NOT the id-collision
                                    // entity carol@x (id 7). A Map.empty load re-keys nothing: it
                                    // collides on the USER insert at id 7 (never completing) or, absent
                                    // the collision, lands the FK on carol@x. Either way it CANNOT make
                                    // this assertion true.
                                    let! email100 =
                                        scalarString sink
                                            "SELECT u.[EMAIL] FROM [dbo].[MIGAB_RK_ORDER] o \
                                             JOIN [dbo].[MIGAB_RK_USER] u ON o.[USER_ID] = u.[ID] WHERE o.[ID] = 100;"
                                    Assert.Equal("alice@x", email100)
                                    let! email101 =
                                        scalarString sink
                                            "SELECT u.[EMAIL] FROM [dbo].[MIGAB_RK_ORDER] o \
                                             JOIN [dbo].[MIGAB_RK_USER] u ON o.[USER_ID] = u.[ID] WHERE o.[ID] = 101;"
                                    Assert.Equal("bob@x", email101)

                                    // No Order carries a Source surrogate — every FK was re-pointed.
                                    let! sourceValued =
                                        scalarString sink "SELECT COUNT(*) FROM [dbo].[MIGAB_RK_ORDER] WHERE [USER_ID] IN (7,8);"
                                    Assert.Equal("0", sourceValued)
                            })
                }))

    [<Fact>]
    member _.``AC-X2: validate-user-map halts the composed run PRE-WRITE on an orphan (the gate composes first)`` () =
        if not (Deploy.Docker.ensureRunning ()) then () else
        let reKeyDdlA =
            "CREATE TABLE [dbo].[MIGAB_RK_USER] ([ID] INT NOT NULL PRIMARY KEY, [EMAIL] NVARCHAR(50) NULL); \
             CREATE TABLE [dbo].[MIGAB_RK_ORDER] ([ID] INT NOT NULL PRIMARY KEY, [USER_ID] INT NOT NULL, [AMOUNT] INT NULL, \
             CONSTRAINT [FK_RkOrder_User] FOREIGN KEY ([USER_ID]) REFERENCES [dbo].[MIGAB_RK_USER] ([ID]));"
        let reKeyDdlB =
            "CREATE TABLE [dbo].[MIGAB_RK_USER] ([ID] INT NOT NULL PRIMARY KEY, [EMAIL] NVARCHAR(256) NULL); \
             CREATE TABLE [dbo].[MIGAB_RK_ORDER] ([ID] INT NOT NULL PRIMARY KEY, [USER_ID] INT NOT NULL, [AMOUNT] INT NULL, \
             CONSTRAINT [FK_RkOrder_User] FOREIGN KEY ([USER_ID]) REFERENCES [dbo].[MIGAB_RK_USER] ([ID]));"
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "MigrateOrphanSrc" (fun source _ ->
                task {
                    do! Deploy.executeBatch source reKeyDdlB
                    // Source carries an ORPHAN (ghost@x, id 9) with no UAT email match.
                    do! Deploy.executeBatch source
                            "INSERT INTO [dbo].[MIGAB_RK_USER] ([ID],[EMAIL]) VALUES (8,N'bob@x'),(9,N'ghost@x'); \
                             INSERT INTO [dbo].[MIGAB_RK_ORDER] ([ID],[USER_ID],[AMOUNT]) VALUES (101,8,600),(102,9,700);"
                    return!
                        fixture.WithEphemeralDatabase "MigrateOrphanSink" (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink reKeyDdlA
                                do! Deploy.executeBatch sink
                                        "INSERT INTO [dbo].[MIGAB_RK_USER] ([ID],[EMAIL]) VALUES (2,N'bob@x');"

                                let! sinkAR = Projection.Adapters.Sql.ReadSide.read sink
                                let sinkA = Result.value sinkAR
                                let! targetBR = Projection.Adapters.Sql.ReadSide.read source
                                let targetB = Result.value targetBR
                                let userKind =
                                    Catalog.allModulesKinds targetB |> List.map snd
                                    |> List.find (fun k -> TableId.tableText k.Physical = "MIGAB_RK_USER")
                                let emailName =
                                    userKind.Attributes
                                    |> List.find (fun a -> ColumnRealization.columnNameText a.Column = "EMAIL")
                                    |> fun a -> a.Name
                                let reconciliation =
                                    Map.ofList [ userKind.SsKey, ReconciliationStrategy.MatchByColumn emailName ]

                                // executeWithData runs the reconciling load with allowDrops=false,
                                // so the AC-I5 validate-user-map gate fires BEFORE any data write.
                                let! outcome =
                                    MigrationRun.executeWithData DeclareNone Transfer.Execute true sinkA targetB reconciliation source sink
                                match outcome with
                                | Error (MigrationError.DataTransferFailed es) ->
                                    // The PRE-WRITE halt: the unmapped-identities refusal.
                                    Assert.Contains(es, fun (e: ValidationError) -> e.Code = "transfer.unmappedIdentities")
                                | other ->
                                    Assert.Fail(sprintf "expected a pre-write validate-user-map halt (DataTransferFailed transfer.unmappedIdentities), got %A" other)

                                // GATE COMPOSES FIRST: no Order rows were loaded (the data leg
                                // never ran) and no UAT user was added — the Sink data write
                                // never happened. (The schema leg precedes the data leg, so the
                                // EMAIL widening did run; the load-bearing fact is the untouched data.)
                                let! orders = scalarString sink "SELECT COUNT(*) FROM [dbo].[MIGAB_RK_ORDER];"
                                Assert.Equal("0", orders)
                                let! users = scalarString sink "SELECT COUNT(*) FROM [dbo].[MIGAB_RK_USER];"
                                Assert.Equal("1", users)
                            })
                }))
