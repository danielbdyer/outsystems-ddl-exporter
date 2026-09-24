namespace Projection.Tests

// The data-level canary (Slice C): the data analog of the schema canary
// and the runtime proof of H-050 extended to data. Seed a Source ephemeral
// DB, run a Transfer (ingest → plan → project) onto an empty Sink DB with
// the same schema, read both back, and assert the Sink reproduces the
// Source on `PhysicalSchema` (columns + FKs + per-row hashes). Serial via
// the Docker-SqlServer collection; blocking wait via `TaskSync.run`.

open System.IO
open Xunit
open Projection.Core
open Projection.Adapters.Sql
open Projection.Pipeline
open Projection.Targets.SSDT

module private TransferCanaryFixtures =

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else printfn "SKIP %s: Docker daemon not reachable." label; false

    /// Normalize the FK *trust* bit to `true` on both sides of a comparison.
    /// M1 gave `PhysicalForeignKey` an `IsTrusted` field, so the high-throughput
    /// bulk load (SqlBulkCopy WITHOUT `CHECK_CONSTRAINTS`, leaving the sink's FKs
    /// `is_not_trusted = 1`) is a trust-bit-ONLY divergence — FK structure
    /// (source/target schema·table·column), columns, rows, and indexes all
    /// round-trip; this helper normalizes ONLY `IsTrusted`, so a genuine
    /// *structural* FK divergence still fails.
    ///
    /// RESOLVED (operator, 2026-06-15 — option C; see DECISIONS "Wave 1 follow-on:
    /// the FK-trust transfer-leg OPEN QUESTION RESOLVED"). The default
    /// `Transfer.Execute` now RE-TRUSTS the sink's FKs post-load
    /// (`WriteOptions.RetrustForeignKeys = true`), so the on-path `RoundTrips`
    /// canary asserts FULL `isEqual` including trust — it no longer calls this
    /// helper. The helper survives to serve the OPT-OUT witness: the off-path
    /// canary runs with `RetrustForeignKeys = false` and uses this to prove the
    /// ONLY residual divergence is trust (everything else round-trips), the named
    /// `ToleratedDivergence.FkTrustNotRestoredOnBulkLoad`.
    let trustNormalizedFks (ps: PhysicalSchema) : PhysicalSchema =
        { ps with
            ForeignKeys = ps.ForeignKeys |> Set.map (fun fk -> { fk with IsTrusted = true }) }

    /// Acyclic two-table schema: Order has a NOT-NULL FK to Customer, so
    /// FK-safe ordering (Customer before Order) is required.
    let twoTableDdl =
        "CREATE TABLE [dbo].[OSUSR_XF_CUSTOMER] (" +
        "[ID] INT NOT NULL PRIMARY KEY, [NAME] NVARCHAR(50) NULL); " +
        "CREATE TABLE [dbo].[OSUSR_XF_ORDER] (" +
        "[ID] INT NOT NULL PRIMARY KEY, [CUSTOMER_ID] INT NOT NULL, [AMOUNT] INT NULL, " +
        "CONSTRAINT [FK_XfOrder_Customer] FOREIGN KEY ([CUSTOMER_ID]) " +
        "REFERENCES [dbo].[OSUSR_XF_CUSTOMER] ([ID]));"

    let twoTableSeed =
        "INSERT INTO [dbo].[OSUSR_XF_CUSTOMER] ([ID],[NAME]) VALUES (1,N'Alice'),(2,N'Bob'); " +
        "INSERT INTO [dbo].[OSUSR_XF_ORDER] ([ID],[CUSTOMER_ID],[AMOUNT]) VALUES " +
        "(10,1,100),(11,2,200),(12,1,300);"

    /// The twoTable schema whose FK is deployed UNTRUSTED — a `NoCheckFk`
    /// decision: created normally, then disabled and re-enabled `WITH NOCHECK`
    /// so it stays ENABLED (enforces new DML) but reads `is_not_trusted = 1`.
    /// Used to prove option C's re-trust PRESERVES an as-deployed untrusted FK
    /// (it restores only the trust the bulk load strips, never a NoCheckFk).
    let untrustedFkDdl =
        twoTableDdl +
        " ALTER TABLE [dbo].[OSUSR_XF_ORDER] NOCHECK CONSTRAINT [FK_XfOrder_Customer]; " +
        "ALTER TABLE [dbo].[OSUSR_XF_ORDER] WITH NOCHECK CHECK CONSTRAINT [FK_XfOrder_Customer];"

    /// Self-referential FK (a 1-cycle): MANAGER_ID is nullable, so it
    /// defers — phase 1 inserts with NULL, phase 2 re-points it.
    let selfRefDdl =
        "CREATE TABLE [dbo].[OSUSR_XF_EMP] (" +
        "[ID] INT NOT NULL PRIMARY KEY, [NAME] NVARCHAR(50) NULL, [MANAGER_ID] INT NULL, " +
        "CONSTRAINT [FK_XfEmp_Mgr] FOREIGN KEY ([MANAGER_ID]) " +
        "REFERENCES [dbo].[OSUSR_XF_EMP] ([ID]));"

    // Ordered so each manager exists before the row that references it
    // (the seed itself does not defer; the *Transfer* does).
    let selfRefSeed =
        "INSERT INTO [dbo].[OSUSR_XF_EMP] ([ID],[NAME],[MANAGER_ID]) VALUES " +
        "(1,N'CEO',NULL),(2,N'VP',1),(3,N'Mgr',2);"

    /// User (business-keyed for the test) + Order with a NOT-NULL FK to
    /// User. The reconciling Transfer matches Source users to the
    /// pre-existing Sink users by EMAIL and re-keys every Order's USER_ID.
    let reKeyDdl =
        "CREATE TABLE [dbo].[OSUSR_RC_USER] (" +
        "[ID] INT NOT NULL PRIMARY KEY, [EMAIL] NVARCHAR(100) NULL); " +
        "CREATE TABLE [dbo].[OSUSR_RC_ORDER] (" +
        "[ID] INT NOT NULL PRIMARY KEY, [USER_ID] INT NOT NULL, [AMOUNT] INT NULL, " +
        "CONSTRAINT [FK_RcOrder_User] FOREIGN KEY ([USER_ID]) " +
        "REFERENCES [dbo].[OSUSR_RC_USER] ([ID]));"

    // Dev users 280/281/999; 999 (ghost@x) has no UAT counterpart. Order 12
    // references 999 → it (and only it) is skipped-and-diagnosed.
    let reKeySourceSeed =
        "INSERT INTO [dbo].[OSUSR_RC_USER] ([ID],[EMAIL]) VALUES " +
        "(280,N'alice@x'),(281,N'bob@x'),(999,N'ghost@x'); " +
        "INSERT INTO [dbo].[OSUSR_RC_ORDER] ([ID],[USER_ID],[AMOUNT]) VALUES " +
        "(10,280,100),(11,281,200),(12,999,300);"

    // Pre-existing Sink (UAT) users — same emails, DIFFERENT surrogates.
    let reKeySinkSeed =
        "INSERT INTO [dbo].[OSUSR_RC_USER] ([ID],[EMAIL]) VALUES (18,N'alice@x'),(19,N'bob@x');"

    // Golden (peer cloud→cloud): every Source user matches a Sink user by email,
    // so the re-key drops nothing — PE-3 isolates the Update-not-re-import proof.
    let goldenSourceSeed =
        "INSERT INTO [dbo].[OSUSR_RC_USER] ([ID],[EMAIL]) VALUES (280,N'alice@x'),(281,N'bob@x'); " +
        "INSERT INTO [dbo].[OSUSR_RC_ORDER] ([ID],[USER_ID],[AMOUNT]) VALUES (10,280,100),(11,281,200);"

    /// AssignedBySink (§5.2): User's PK is IDENTITY, so the Sink mints new
    /// surrogates at insert time; Order (business PK) has a NOT-NULL FK to
    /// User. The Source seeds Users at 280/281 (via IDENTITY_INSERT) so the
    /// Sink-minted IDs (1,2 in the empty Sink) necessarily DIFFER — the
    /// round-trip can only hold if every Order's USER_ID was re-pointed
    /// through the captured SurrogateRemapContext.
    let assignedBySinkDdl =
        "CREATE TABLE [dbo].[OSUSR_AS_USER] (" +
        "[ID] INT IDENTITY(1,1) NOT NULL PRIMARY KEY, [EMAIL] NVARCHAR(100) NULL); " +
        "CREATE TABLE [dbo].[OSUSR_AS_ORDER] (" +
        "[ID] INT NOT NULL PRIMARY KEY, [USER_ID] INT NOT NULL, [AMOUNT] INT NULL, " +
        "CONSTRAINT [FK_AsOrder_User] FOREIGN KEY ([USER_ID]) " +
        "REFERENCES [dbo].[OSUSR_AS_USER] ([ID]));"

    let assignedBySinkSourceSeed =
        "SET IDENTITY_INSERT [dbo].[OSUSR_AS_USER] ON; " +
        "INSERT INTO [dbo].[OSUSR_AS_USER] ([ID],[EMAIL]) VALUES (280,N'alice@x'),(281,N'bob@x'); " +
        "SET IDENTITY_INSERT [dbo].[OSUSR_AS_USER] OFF; " +
        "INSERT INTO [dbo].[OSUSR_AS_ORDER] ([ID],[USER_ID],[AMOUNT]) VALUES (10,280,100),(11,281,200);"

    /// 6.A.2 LIFTED (operator-authorized 2026-06-10) — a self-referential
    /// IDENTITY kind: PK is IDENTITY (`AssignedBySink`) and MANAGER_ID is a
    /// NULLABLE self-FK, so the two-phase load defers it to Phase 2, which
    /// re-points it through the COMPLETED remap and keys its WHERE on the
    /// ASSIGNED PK. Source keys start at 1000 so the sink-minted keys (1,2,3)
    /// provably differ — a mis-keyed Phase 2 cannot pass by collision; and
    /// VP's manager (CEO, 1002) lands AFTER VP in PK order, so a
    /// phase-1-only re-point cannot pass either.
    let cyclicAssignedDdl =
        "CREATE TABLE [dbo].[OSUSR_XF_EMPID] (" +
        "[ID] INT IDENTITY(1,1) NOT NULL PRIMARY KEY, [NAME] NVARCHAR(50) NULL, [MANAGER_ID] INT NULL, " +
        "CONSTRAINT [FK_XfEmpId_Mgr] FOREIGN KEY ([MANAGER_ID]) " +
        "REFERENCES [dbo].[OSUSR_XF_EMPID] ([ID]));"

    let cyclicAssignedSeed =
        "ALTER TABLE [dbo].[OSUSR_XF_EMPID] NOCHECK CONSTRAINT ALL; " +
        "SET IDENTITY_INSERT [dbo].[OSUSR_XF_EMPID] ON; " +
        "INSERT INTO [dbo].[OSUSR_XF_EMPID] ([ID],[NAME],[MANAGER_ID]) VALUES " +
        "(1000,N'VP',1002),(1001,N'Mgr',1000),(1002,N'CEO',NULL); " +
        "SET IDENTITY_INSERT [dbo].[OSUSR_XF_EMPID] OFF; " +
        "ALTER TABLE [dbo].[OSUSR_XF_EMPID] WITH NOCHECK CHECK CONSTRAINT ALL;"

    /// (employee NAME, manager NAME or NULL) pairs — the identity-independent
    /// manager chain the lifted self-FK load must preserve.
    let managerChain (cnn: Microsoft.Data.SqlClient.SqlConnection) : System.Threading.Tasks.Task<(string * string option) list> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-
                "SELECT e.[NAME], m.[NAME] FROM [dbo].[OSUSR_XF_EMPID] e " +
                "LEFT JOIN [dbo].[OSUSR_XF_EMPID] m ON e.[MANAGER_ID] = m.[ID] ORDER BY e.[NAME];"
            use! reader = cmd.ExecuteReaderAsync()
            let acc = System.Collections.Generic.List<string * string option>()
            let mutable go = true
            while go do
                let! has = reader.ReadAsync()
                if has then
                    let mgr = if reader.IsDBNull 1 then None else Some (reader.GetString 1)
                    acc.Add(reader.GetString 0, mgr)
                else go <- false
            return List.ofSeq acc
        }

    /// 6.A.3 — a composite IDENTITY PK ([ID] IDENTITY + [TENANT]). `ofKind`
    /// is `AssignedBySink` (the PK carries IDENTITY), but the per-row capture
    /// is single-column and would truncate the composite surrogate. Refuse.
    let compositeAssignedDdl =
        "CREATE TABLE [dbo].[OSUSR_XF_CMP] (" +
        "[ID] INT IDENTITY(1,1) NOT NULL, [TENANT] INT NOT NULL, [NAME] NVARCHAR(50) NULL, " +
        "CONSTRAINT [PK_XfCmp] PRIMARY KEY ([ID],[TENANT]));"

    let compositeAssignedSeed =
        "SET IDENTITY_INSERT [dbo].[OSUSR_XF_CMP] ON; " +
        "INSERT INTO [dbo].[OSUSR_XF_CMP] ([ID],[TENANT],[NAME]) VALUES " +
        "(1,100,N'a'),(2,100,N'b'); " +
        "SET IDENTITY_INSERT [dbo].[OSUSR_XF_CMP] OFF;"

    /// WP-3 (F11) — a Text column seeded with a genuine empty string, a
    /// NULL, and a non-empty value. The read side carries NULL out-of-band
    /// (`ValueNone`) and the empty string as a value (`ValueSome ""`), and
    /// `Bulk.parseRaw` writes them back distinctly — the empty string
    /// SURVIVES transfer (the 6.A.4 tolerance `EmptyTextNormalizedToNull`
    /// is retired).
    let emptyTextDdl =
        "CREATE TABLE [dbo].[OSUSR_ET_NOTE] (" +
        "[ID] INT NOT NULL PRIMARY KEY, [BODY] NVARCHAR(50) NULL);"

    let emptyTextSeed =
        "INSERT INTO [dbo].[OSUSR_ET_NOTE] ([ID],[BODY]) VALUES " +
        "(1, N''), (2, NULL), (3, N'hello');"

    /// Per-ID `(BODY value-or-marker, BODY IS NULL)` from a connection.
    let noteBodies (cnn: Microsoft.Data.SqlClient.SqlConnection) : System.Threading.Tasks.Task<(int * bool) list> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-
                "SELECT [ID], CASE WHEN [BODY] IS NULL THEN 1 ELSE 0 END " +
                "FROM [dbo].[OSUSR_ET_NOTE] ORDER BY [ID];"
            use! reader = cmd.ExecuteReaderAsync()
            let acc = System.Collections.Generic.List<int * bool>()
            let mutable go = true
            while go do
                let! has = reader.ReadAsync()
                if has then acc.Add(reader.GetInt32 0, reader.GetInt32 1 = 1)
                else go <- false
            return List.ofSeq acc
        }

    /// 6.B.1 — a kind with a NULLABLE NOTE column carrying NULL rows. An
    /// `EnforceNotNull` tightening on NOTE would make the two-phase load fail
    /// mid-write; the pre-flight refuses first.
    let tighteningDdl =
        "CREATE TABLE [dbo].[OSUSR_TG_TICKET] (" +
        "[ID] INT NOT NULL PRIMARY KEY, [NOTE] NVARCHAR(50) NULL);"

    let tighteningSeed =
        "INSERT INTO [dbo].[OSUSR_TG_TICKET] ([ID],[NOTE]) VALUES " +
        "(1, N'hi'), (2, NULL), (3, NULL);"

    // 6.B.2 — authored source/sink contracts with STABLE, matching SsKeys so
    // the A→B diff detects the column rename (Email → Contact). The physical
    // column also moves (EMAIL → CONTACT), so a positional/source-name write
    // would mis-map; the rename map re-points by identity. (Authored, not
    // ReadSide-reconstructed: synthesized keys are name-derived and would not
    // thread the rename — see 6.A.7.)
    let private rpName (s: string) : Name = Name.create s |> Result.value
    let private rpKKey (s: string) : SsKey = SsKey.synthesizedComposite "RP_XFER_KIND" [ s ] |> Result.value
    let private rpAKey (s: string) : SsKey = SsKey.synthesizedComposite "RP_XFER_ATTR" [ s ] |> Result.value
    let private rpAttr (key: SsKey) (logical: string) (col: string) (isPk: bool) : Attribute =
        { Attribute.create key (rpName logical) (if isPk then Integer else Text) with
            Column = ColumnRealization.create (col) (not isPk) |> Result.value
            IsPrimaryKey = isPk
            IsMandatory = isPk }
    let private rpContract (emailName: string) (emailCol: string) : Catalog =
        let cust =
            Kind.create (rpKKey "Customer") (rpName "Customer")
                (TableId.create "dbo" "RP_XFER_CUSTOMER" |> Result.value)
                [ rpAttr (rpAKey "Id") "Id" "ID" true
                  rpAttr (rpAKey "Email") emailName emailCol false ]
        Catalog.create
            [ { SsKey = rpKKey "Mod"; Name = rpName "M"; Kinds = [ cust ]; IsActive = true; ExtendedProperties = [] } ] []
        |> Result.value

    /// Source at schema A (Email/EMAIL); sink at schema B (Contact/CONTACT) —
    /// the same kind + attribute SsKeys, so the diff sees a rename.
    let renameSourceContract : Catalog = rpContract "Email" "EMAIL"
    let renameSinkContract : Catalog = rpContract "Contact" "CONTACT"

    // M3 / LE-2 — the legacy B→A reverse leg: ONE SsKey-stable model in two
    // renditions. B (logical) carries clean names ([dbo].[Customer].[Email]);
    // A (physical) carries the OSUSR_* rendition ([dbo].[OSUSR_XF_CUSTOMER].[Contact]).
    // SAME kind + attribute SsKeys (so the A→B diff aligns by identity), differing
    // in BOTH table AND column name — the table-name rendition is resolved per-SsKey
    // against the sink contract; the column-name rendition rides the rename map.
    let private legacyContract (table: string) (emailName: string) (emailCol: string) : Catalog =
        let cust =
            Kind.create (rpKKey "Customer") (rpName "Customer")
                (TableId.create "dbo" table |> Result.value)
                [ rpAttr (rpAKey "Id") "Id" "ID" true
                  rpAttr (rpAKey "Email") emailName emailCol false ]
        Catalog.create
            [ { SsKey = rpKKey "Mod"; Name = rpName "M"; Kinds = [ cust ]; IsActive = true; ExtendedProperties = [] } ] []
        |> Result.value

    /// B (logical on-prem, migration-team-populated) → A (physical cloud OSUSR).
    let legacySourceContract : Catalog = legacyContract "Customer"          "Email"   "EMAIL"
    let legacySinkContract   : Catalog = legacyContract "OSUSR_XF_CUSTOMER" "Contact" "CONTACT"

    /// J3 closed — the ONE authored model in its as-authored (physical) form:
    /// the OSUSR table + physical columns; the logical names ride `Name`.
    /// `CatalogRendition.physical` IS this catalog; `CatalogRendition.logical`
    /// derives the B contract from it (the production contract source — the
    /// hand-built pair above is the engine-only LE-2 precedent).
    let legacyAuthoredModel : Catalog = legacyContract "OSUSR_XF_CUSTOMER" "Email" "CONTACT"

    /// Scalar string from a connection (for asserting a single cell).
    let scalarStr (cnn: Microsoft.Data.SqlClient.SqlConnection) (sql: string) : System.Threading.Tasks.Task<string> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sql
            let! v = cmd.ExecuteScalarAsync()
            return string v
        }

    let value (r: Result<'a>) : 'a = Result.value r

    /// Count rows in a table on the given connection.
    let countRows (cnn: Microsoft.Data.SqlClient.SqlConnection) (table: string) : System.Threading.Tasks.Task<int> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sprintf "SELECT COUNT_BIG(*) FROM %s;" table
            let! scalar = cmd.ExecuteScalarAsync()
            return int (unbox<int64> scalar)
        }

    /// The (Order ID → User EMAIL) join projection — the identity-
    /// independent relationship the AssignedBySink round-trip must preserve.
    let orderUserEmail (cnn: Microsoft.Data.SqlClient.SqlConnection) : System.Threading.Tasks.Task<(int * string) list> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-
                "SELECT o.[ID], u.[EMAIL] FROM [dbo].[OSUSR_AS_ORDER] o " +
                "JOIN [dbo].[OSUSR_AS_USER] u ON o.[USER_ID] = u.[ID] ORDER BY o.[ID];"
            use! reader = cmd.ExecuteReaderAsync()
            let acc = System.Collections.Generic.List<int * string>()
            let mutable go = true
            while go do
                let! has = reader.ReadAsync()
                if has then acc.Add(reader.GetInt32 0, reader.GetString 1)
                else go <- false
            return List.ofSeq acc
        }

    /// The (Order ID → User EMAIL) join over the RC tables — the identity-
    /// independent relationship the golden cloud→cloud re-key must preserve.
    let orderUserEmailRc (cnn: Microsoft.Data.SqlClient.SqlConnection) : System.Threading.Tasks.Task<(int * string) list> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <-
                "SELECT o.[ID], u.[EMAIL] FROM [dbo].[OSUSR_RC_ORDER] o " +
                "JOIN [dbo].[OSUSR_RC_USER] u ON o.[USER_ID] = u.[ID] ORDER BY o.[ID];"
            use! reader = cmd.ExecuteReaderAsync()
            let acc = System.Collections.Generic.List<int * string>()
            let mutable go = true
            while go do
                let! has = reader.ReadAsync()
                if has then acc.Add(reader.GetInt32 0, reader.GetString 1)
                else go <- false
            return List.ofSeq acc
        }

    /// Resolve `OSUSR_RC_USER` → MatchByColumn EMAIL against a reconstructed
    /// contract — the golden user re-key reconciliation (P-3 ByEmail).
    let resolveUserByEmail (contract: Catalog) : Result<Map<SsKey, ReconciliationStrategy>> =
        let userKind =
            Catalog.allModulesKinds contract |> List.map snd
            |> List.find (fun k -> TableId.tableText k.Physical = "OSUSR_RC_USER")
        let emailName =
            userKind.Attributes
            |> List.find (fun a -> ColumnRealization.columnNameText a.Column = "EMAIL")
            |> fun a -> a.Name
        Ok (Map.ofList [ userKind.SsKey, ReconciliationStrategy.MatchByColumn emailName ])


[<Xunit.Collection("Docker-SqlServer")>]
type TransferCanaryTests(fixture: EphemeralContainerFixture) =
    interface IClassFixture<EphemeralContainerFixture>

    /// Source ≈ Sink on PhysicalSchema after a Transfer of the given
    /// schema + seed — the data-level canary assertion.
    member private _.RoundTrips (label: string) (ddl: string) (seed: string) =
        if not (TransferCanaryFixtures.skipIfNoDocker label) then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase (label + "Src") (fun src _ ->
                task {
                    do! Deploy.executeBatch src ddl
                    do! Deploy.executeBatch src seed
                    return!
                        fixture.WithEphemeralDatabase (label + "Sink") (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink ddl   // same schema, no rows

                                // Contract = the Source's reconstructed schema.
                                let! contractR = ReadSide.read src
                                let contract = TransferCanaryFixtures.value contractR

                                let! reportR = Transfer.run Transfer.Execute true src sink contract
                                let report = TransferCanaryFixtures.value reportR
                                Assert.True(report.Kinds |> List.forall (fun k -> k.RowsWritten = k.RowsIngested))

                                // Sink reproduces Source on PhysicalSchema in FULL — per-row hashes
                                // AND FK trust. Option C (operator-authorized 2026-06-15): the default
                                // `Transfer.Execute` re-trusts the sink's FKs after the bulk load
                                // (`WriteOptions.RetrustForeignKeys = true`), restoring the trust bit
                                // the high-throughput `SqlBulkCopy` left `is_not_trusted = 1`. So the
                                // canary asserts full `isEqual` INCLUDING trust — no `trustNormalizedFks`
                                // normalization (retired here per its own docstring's promise). The
                                // opt-out (re-trust off) is witnessed by the `FkTrustNotRestoredOnBulkLoad`
                                // off-path canary below.
                                let! aR = ReadSide.read src
                                let! bR = ReadSide.read sink
                                let diff =
                                    PhysicalSchema.diff
                                        (PhysicalSchema.ofCatalog (TransferCanaryFixtures.value aR))
                                        (PhysicalSchema.ofCatalog (TransferCanaryFixtures.value bR))
                                Assert.True(PhysicalSchema.isEqual diff, PhysicalSchema.renderDiff diff)
                            })
                }))

    [<Fact>]
    member this.``data canary: multi-table FK chain round-trips with empty PhysicalSchema diff`` () =
        this.RoundTrips "XferTwo" TransferCanaryFixtures.twoTableDdl TransferCanaryFixtures.twoTableSeed

    [<Fact>]
    member this.``data canary: deferred self-referential FK is re-pointed in phase 2`` () =
        this.RoundTrips "XferSelf" TransferCanaryFixtures.selfRefDdl TransferCanaryFixtures.selfRefSeed

    // Option C off-path — the opt-out witness. With `RetrustForeignKeys = false`
    // the bulk load's untrusted sink FKs are LEFT untrusted: the named, accepted
    // `FkTrustNotRestoredOnBulkLoad` Decision-axis tolerance, not a silent drop.
    // Discriminating against the default (re-trust on, asserted by `RoundTrips`):
    // the RAW diff is non-empty (trust differs), the source reads trusted / the
    // sink untrusted, and normalizing ONLY trust empties the diff (nothing
    // structural diverges — the bulk data + FK structure still round-trip).
    [<Fact>]
    member _.``data canary (option C off-path): RetrustForeignKeys=false leaves sink FKs untrusted — named FkTrustNotRestoredOnBulkLoad`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "XferNoRetrust") then () else
        // The opt-out is a named, closed tolerance — not a silent divergence.
        Assert.Contains(ToleratedDivergence.FkTrustNotRestoredOnBulkLoad, ToleratedDivergence.allKnown)
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "XferNoRetrustSrc" (fun src _ ->
                task {
                    do! Deploy.executeBatch src TransferCanaryFixtures.twoTableDdl
                    do! Deploy.executeBatch src TransferCanaryFixtures.twoTableSeed
                    return!
                        fixture.WithEphemeralDatabase "XferNoRetrustSink" (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink TransferCanaryFixtures.twoTableDdl

                                let! contractR = ReadSide.read src
                                let contract = TransferCanaryFixtures.value contractR

                                // Opt out of re-trust — keep the bulk load's untrusted FKs.
                                let opts = { Transfer.WriteOptions.def with RetrustForeignKeys = false }
                                let! reportR = Transfer.runWithOptions Transfer.Execute opts true src sink contract
                                let _ = TransferCanaryFixtures.value reportR

                                let! aR = ReadSide.read src
                                let! bR = ReadSide.read sink
                                let srcPs = PhysicalSchema.ofCatalog (TransferCanaryFixtures.value aR)
                                let sinkPs = PhysicalSchema.ofCatalog (TransferCanaryFixtures.value bR)

                                // Source FK trusted; sink FK untrusted (the opt-out divergence).
                                Assert.True(srcPs.ForeignKeys |> Set.forall (fun fk -> fk.IsTrusted),
                                            "source FKs should read trusted")
                                Assert.True(sinkPs.ForeignKeys |> Set.exists (fun fk -> not fk.IsTrusted),
                                            "with re-trust off the sink FK should read untrusted")

                                // RAW diff is non-empty (trust differs)...
                                let rawDiff = PhysicalSchema.diff srcPs sinkPs
                                Assert.False(PhysicalSchema.isEqual rawDiff,
                                             "with re-trust off the FK-trust divergence must surface")
                                // ...and normalizing ONLY trust empties it (nothing structural diverges).
                                let normDiff =
                                    PhysicalSchema.diff
                                        (TransferCanaryFixtures.trustNormalizedFks srcPs)
                                        (TransferCanaryFixtures.trustNormalizedFks sinkPs)
                                Assert.True(PhysicalSchema.isEqual normDiff, PhysicalSchema.renderDiff normDiff)
                            })
                }))

    // -- Build A — the data-leg compensating-undo / revert-script -----------
    //
    // A failed AssignedBySink load: USER (IDENTITY PK, FK-targeted by ORDER) is
    // captured first (the sink mints 1,2 into the empty sink), THEN ORDER's load
    // fails because the sink's AMOUNT column was dropped (the bulk copy has no
    // destination column). At failure the remap holds USER's minted keys, so the
    // revert is a precise child-first DELETE-by-captured-key on USER — pre-existing
    // rows would be untouched. Two modes prove the operator's spec:
    //   • default (--auto-revert OFF): write the precise revert SCRIPT to an
    //     artifact and leave the sink for the operator to review/run;
    //   • --auto-revert ON: execute the DELETE-by-captured-key automatically.
    // Either way the original failure still propagates (the load DID fail).

    [<Fact>]
    member _.``data canary (Build A): a failed load with auto-revert OFF writes the precise revert script and leaves the sink`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "XferRevertScript") then () else
        let dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "v2-revert-canary-script")
        let scriptPath = System.IO.Path.Combine(dir, "transfer-revert.sql")
        (try System.IO.File.Delete scriptPath with _ -> ())
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "XferRevScriptSrc" (fun src _ ->
                task {
                    do! Deploy.executeBatch src TransferCanaryFixtures.assignedBySinkDdl
                    do! Deploy.executeBatch src TransferCanaryFixtures.assignedBySinkSourceSeed
                    return!
                        fixture.WithEphemeralDatabase "XferRevScriptSink" (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink TransferCanaryFixtures.assignedBySinkDdl
                                // Force ORDER's load to fail AFTER USER is captured.
                                do! Deploy.executeBatch sink "ALTER TABLE [dbo].[OSUSR_AS_ORDER] DROP COLUMN [AMOUNT];"
                                let! contractR = ReadSide.read src
                                let contract = TransferCanaryFixtures.value contractR
                                let opts = { Transfer.WriteOptions.def with AutoRevert = false; RevertArtifactDir = Some dir }
                                let! threw =
                                    task {
                                        try
                                            let! _ = Transfer.runWithOptions Transfer.Execute opts true src sink contract
                                            return false
                                        with _ -> return true
                                    }
                                Assert.True(threw, "the transfer should have failed on the dropped AMOUNT column")
                                // The precise revert script was written (default = no auto-delete).
                                Assert.True(System.IO.File.Exists scriptPath, sprintf "expected a revert script at %s" scriptPath)
                                let script = System.IO.File.ReadAllText scriptPath
                                Assert.Contains("DELETE FROM", script)
                                Assert.Contains("OSUSR_AS_USER", script)
                                // The minted USER rows REMAIN — the operator runs the script.
                                use cmd = sink.CreateCommand()
                                cmd.CommandText <- "SELECT COUNT(*) FROM [dbo].[OSUSR_AS_USER];"
                                let! users = cmd.ExecuteScalarAsync()
                                Assert.Equal(2, System.Convert.ToInt32 users)
                            })
                }))

    [<Fact>]
    member _.``data canary (Build A): a failed load with auto-revert ON deletes the sink-minted rows by captured key`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "XferRevertAuto") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "XferRevAutoSrc" (fun src _ ->
                task {
                    do! Deploy.executeBatch src TransferCanaryFixtures.assignedBySinkDdl
                    do! Deploy.executeBatch src TransferCanaryFixtures.assignedBySinkSourceSeed
                    return!
                        fixture.WithEphemeralDatabase "XferRevAutoSink" (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink TransferCanaryFixtures.assignedBySinkDdl
                                do! Deploy.executeBatch sink "ALTER TABLE [dbo].[OSUSR_AS_ORDER] DROP COLUMN [AMOUNT];"
                                let! contractR = ReadSide.read src
                                let contract = TransferCanaryFixtures.value contractR
                                let opts = { Transfer.WriteOptions.def with AutoRevert = true }
                                let! threw =
                                    task {
                                        try
                                            let! _ = Transfer.runWithOptions Transfer.Execute opts true src sink contract
                                            return false
                                        with _ -> return true
                                    }
                                Assert.True(threw, "the transfer should have failed on the dropped AMOUNT column")
                                // --auto-revert executed the DELETE-by-captured-key: the
                                // sink-minted USER rows are gone (no residue from the failed load).
                                use cmd = sink.CreateCommand()
                                cmd.CommandText <- "SELECT COUNT(*) FROM [dbo].[OSUSR_AS_USER];"
                                let! users = cmd.ExecuteScalarAsync()
                                Assert.Equal(0, System.Convert.ToInt32 users)
                            })
                }))

    // -- D — the STREAMING arm of the data-leg compensating-undo ------------
    //
    // The estate-scale sibling of the materialized "Build A" canaries above, and
    // the GATE for follow-on D (a wrong DELETE-by-captured-key at 10⁸ rows is
    // unrecoverable, so it must not land untested). The streaming reverse leg
    // (`runStreamingReconcilingWithRenames`, the hundreds-of-millions-row path)
    // mints USER's surrogates as it streams and journals each completed chunk's
    // captured (source → assigned) pairs to the off-box CaptureJournal. ORDER's
    // load then CRASHES (its sink AMOUNT column was dropped). On a streaming crash
    // `writePlanStreaming` RE-RAISES (it returns `Result.failure` ONLY on a NAMED
    // resume-drift refusal) — so D's compensation hangs off the EXCEPTION path,
    // not an `Error` match arm: it replays the journal into a remap and runs the
    // SAME M23 `buildRevertScript`/`runRevert` the materialized arm runs. A
    // PRE-EXISTING sink USER row (minted before the transfer) proves the revert
    // targets ONLY the captured minted keys — never pre-existing data. Two modes
    // mirror the materialized pair.

    [<Fact>]
    member _.``streaming data canary (D): a failed streaming load with auto-revert OFF writes the journal-replayed revert script and leaves the sink (pre-existing rows untouched)`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "XferStreamRevertScript") then () else
        let dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "v2-stream-revert-script-" + System.Guid.NewGuid().ToString("N").Substring(0, 8))
        let journalDir = System.IO.Path.Combine(dir, "journal")
        let scriptPath = System.IO.Path.Combine(dir, "transfer-revert.sql")
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "XferStreamRevScriptSrc" (fun src _ ->
                task {
                    do! Deploy.executeBatch src TransferCanaryFixtures.assignedBySinkDdl
                    do! Deploy.executeBatch src TransferCanaryFixtures.assignedBySinkSourceSeed
                    return!
                        fixture.WithEphemeralDatabase "XferStreamRevScriptSink" (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink TransferCanaryFixtures.assignedBySinkDdl
                                // A PRE-EXISTING minted USER row (the sink mints ID 1);
                                // the streaming load then mints 2,3 for the source users.
                                do! Deploy.executeBatch sink "INSERT INTO [dbo].[OSUSR_AS_USER] ([EMAIL]) VALUES (N'preexisting@x');"
                                // Force ORDER's chunk write to crash AFTER USER streams + journals.
                                do! Deploy.executeBatch sink "ALTER TABLE [dbo].[OSUSR_AS_ORDER] DROP COLUMN [AMOUNT];"
                                let! contractR = ReadSide.read src
                                let contract = TransferCanaryFixtures.value contractR
                                let! threw =
                                    task {
                                        try
                                            let! _ =
                                                Transfer.runStreamingReconcilingWithRenames
                                                    Transfer.Execute true false src sink contract contract Map.empty Set.empty Set.empty []
                                                    (Some journalDir) false (Some dir)
                                            return false
                                        with _ -> return true
                                    }
                                Assert.True(threw, "the streaming transfer should have crashed on the dropped AMOUNT column")
                                // The precise journal-replayed revert script was written (default = no auto-delete).
                                Assert.True(System.IO.File.Exists scriptPath, sprintf "expected a revert script at %s" scriptPath)
                                let script = System.IO.File.ReadAllText scriptPath
                                Assert.Contains("DELETE FROM", script)
                                Assert.Contains("OSUSR_AS_USER", script)
                                // The minted USER rows REMAIN (the operator runs the script):
                                // 1 pre-existing + 2 minted = 3, and the pre-existing row is intact.
                                let! total = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_AS_USER]"
                                Assert.Equal(3, total)
                                let! preexisting = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_AS_USER] WHERE [EMAIL] = N'preexisting@x'"
                                Assert.Equal(1, preexisting)
                            })
                }))

    [<Fact>]
    member _.``streaming data canary (D): a failed streaming load with auto-revert ON deletes the journal-captured minted rows and leaves pre-existing rows`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "XferStreamRevertAuto") then () else
        let journalDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "v2-stream-revert-auto-" + System.Guid.NewGuid().ToString("N").Substring(0, 8))
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "XferStreamRevAutoSrc" (fun src _ ->
                task {
                    do! Deploy.executeBatch src TransferCanaryFixtures.assignedBySinkDdl
                    do! Deploy.executeBatch src TransferCanaryFixtures.assignedBySinkSourceSeed
                    return!
                        fixture.WithEphemeralDatabase "XferStreamRevAutoSink" (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink TransferCanaryFixtures.assignedBySinkDdl
                                do! Deploy.executeBatch sink "INSERT INTO [dbo].[OSUSR_AS_USER] ([EMAIL]) VALUES (N'preexisting@x');"
                                do! Deploy.executeBatch sink "ALTER TABLE [dbo].[OSUSR_AS_ORDER] DROP COLUMN [AMOUNT];"
                                let! contractR = ReadSide.read src
                                let contract = TransferCanaryFixtures.value contractR
                                let! threw =
                                    task {
                                        try
                                            let! _ =
                                                Transfer.runStreamingReconcilingWithRenames
                                                    Transfer.Execute true false src sink contract contract Map.empty Set.empty Set.empty []
                                                    (Some journalDir) true None
                                            return false
                                        with _ -> return true
                                    }
                                Assert.True(threw, "the streaming transfer should have crashed on the dropped AMOUNT column")
                                // --auto-revert executed the journal-replayed DELETE-by-captured-key:
                                // the sink-minted USER rows (2,3) are gone; ONLY the pre-existing row remains.
                                let! total = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_AS_USER]"
                                Assert.Equal(1, total)
                                let! preexisting = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_AS_USER] WHERE [EMAIL] = N'preexisting@x'"
                                Assert.Equal(1, preexisting)
                            })
                }))

    // Option C — the NoCheckFk-PRESERVATION witness (the fidelity guard on the
    // re-trust step). A source FK deployed UNTRUSTED (a NoCheckFk decision — the
    // very thing M1 made faithful) must survive a DEFAULT (re-trust ON) transfer
    // as UNTRUSTED on the sink. The restore re-validates only the trust the bulk
    // load STRIPS (the pre-load snapshot), never an as-deployed untrusted FK.
    // Discriminating: a blanket "re-trust every untrusted FK" would trust the
    // sink FK — diverging from the source AND overriding the operator's NoCheckFk
    // decision (re-validating data they chose not to validate).
    [<Fact>]
    member _.``data canary (option C): an as-deployed UNTRUSTED FK (NoCheckFk) is PRESERVED through a default re-trust transfer`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "XferUntrustedFk") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "XferUntrustedFkSrc" (fun src _ ->
                task {
                    do! Deploy.executeBatch src TransferCanaryFixtures.untrustedFkDdl
                    do! Deploy.executeBatch src TransferCanaryFixtures.twoTableSeed
                    return!
                        fixture.WithEphemeralDatabase "XferUntrustedFkSink" (fun sink _ ->
                            task {
                                // Sink FK also deployed UNTRUSTED (as-deployed parity).
                                do! Deploy.executeBatch sink TransferCanaryFixtures.untrustedFkDdl

                                let! contractR = ReadSide.read src
                                let contract = TransferCanaryFixtures.value contractR

                                // Default transfer — re-trust ON.
                                let! reportR = Transfer.run Transfer.Execute true src sink contract
                                let _ = TransferCanaryFixtures.value reportR

                                let! aR = ReadSide.read src
                                let! bR = ReadSide.read sink
                                let srcPs = PhysicalSchema.ofCatalog (TransferCanaryFixtures.value aR)
                                let sinkPs = PhysicalSchema.ofCatalog (TransferCanaryFixtures.value bR)

                                // The as-deployed untrusted FK is preserved on BOTH sides — the
                                // restore touched only the load-stripped trust (none here), NOT
                                // the NoCheckFk decision.
                                Assert.True(srcPs.ForeignKeys |> Set.forall (fun fk -> not fk.IsTrusted),
                                            "source FK should read as-deployed untrusted")
                                Assert.True(sinkPs.ForeignKeys |> Set.forall (fun fk -> not fk.IsTrusted),
                                            "re-trust must PRESERVE the as-deployed untrusted FK, not blanket-trust it")
                                // Full isEqual incl. trust — both untrusted, everything else round-trips.
                                let diff = PhysicalSchema.diff srcPs sinkPs
                                Assert.True(PhysicalSchema.isEqual diff, PhysicalSchema.renderDiff diff)
                            })
                }))

    // Wave-3 slice 3.1 — the CDC pre-flight. An Execute transfer against a
    // Sink with CDC-tracked tables is refused unless --allow-cdc. Gracefully
    // skips if the container cannot enable CDC (the metadata flag is what the
    // pre-flight reads; capture jobs need Agent but the flag is set regardless).
    [<Fact>]
    member _.``3.1: CDC pre-flight refuses --execute against a CDC-tracked sink, allow-cdc overrides`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "XferCdc") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "XferCdcSrc" (fun src _ ->
                task {
                    let ddl = "CREATE TABLE dbo.Widget (Id INT NOT NULL PRIMARY KEY, Name NVARCHAR(50) NULL);"
                    do! Deploy.executeBatch src ddl
                    do! Deploy.executeBatch src "INSERT INTO dbo.Widget (Id, Name) VALUES (1, N'a');"
                    return!
                        fixture.WithEphemeralDatabase "XferCdcSink" (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink ddl
                                let! enabled =
                                    task {
                                        try
                                            do! Deploy.executeBatch sink "EXEC sys.sp_cdc_enable_db;"
                                            do! Deploy.executeBatch sink "EXEC sys.sp_cdc_enable_table @source_schema = N'dbo', @source_name = N'Widget', @role_name = NULL, @supports_net_changes = 0;"
                                            use cmd = sink.CreateCommand()
                                            cmd.CommandText <- "SELECT COUNT(*) FROM sys.tables WHERE is_tracked_by_cdc = 1 AND name = N'Widget'"
                                            let! c = cmd.ExecuteScalarAsync()
                                            return System.Convert.ToInt32 c > 0
                                        with _ -> return false
                                    }
                                if not enabled then
                                    printfn "SKIP 3.1 CDC pre-flight: container did not enable CDC (flag not set)"
                                else
                                    let! contractR = ReadSide.read src
                                    let contract = TransferCanaryFixtures.value contractR
                                    // allowCdc = false → the pre-flight refuses.
                                    let! refusedR = Transfer.run Transfer.Execute false src sink contract
                                    match refusedR with
                                    | Error es ->
                                        Assert.True(
                                            es |> List.exists (fun e -> e.Code = "transfer.cdcTrackedSink"),
                                            sprintf "expected transfer.cdcTrackedSink, got %A" (es |> List.map (fun e -> e.Code)))
                                    | Ok _ -> Assert.Fail("expected CDC pre-flight refusal against a CDC-tracked sink")
                                    // allowCdc = true → the override proceeds and writes.
                                    let! okR = Transfer.run Transfer.Execute true src sink contract
                                    match okR with
                                    | Ok _ -> ()
                                    | Error es -> Assert.Fail(sprintf "--allow-cdc override should proceed; got %A" (es |> List.map (fun e -> e.Code)))
                            })
                }))

    [<Fact>]
    member _.``data canary: reconciling Transfer re-keys FKs to pre-existing Sink identities (Dev->UAT User re-key)`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "XferReKey") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "XferReKeySrc" (fun src _ ->
                task {
                    do! Deploy.executeBatch src TransferCanaryFixtures.reKeyDdl
                    do! Deploy.executeBatch src TransferCanaryFixtures.reKeySourceSeed
                    return!
                        fixture.WithEphemeralDatabase "XferReKeySink" (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink TransferCanaryFixtures.reKeyDdl
                                do! Deploy.executeBatch sink TransferCanaryFixtures.reKeySinkSeed

                                // Contract = the Source's reconstructed schema.
                                let! contractR = ReadSide.read src
                                let contract = TransferCanaryFixtures.value contractR
                                let kindByTable (t: string) =
                                    Catalog.allModulesKinds contract |> List.map snd |> List.find (fun k -> TableId.tableText k.Physical = t)
                                let userKind = kindByTable "OSUSR_RC_USER"
                                let orderKind = kindByTable "OSUSR_RC_ORDER"
                                let emailName =
                                    userKind.Attributes |> List.find (fun a -> ColumnRealization.columnNameText a.Column = "EMAIL") |> (fun a -> a.Name)

                                let reconciliation =
                                    Map.ofList [ userKind.SsKey, ReconciliationStrategy.MatchByColumn emailName ]

                                // allowDrops = true: this canary observes the POST-write drop
                                // set (the orphan re-key behavior), so it runs the --allow-drops
                                // path past the AC-I5 pre-write gate. The pre-write halt itself is
                                // witnessed by the pure `validateUserMap` test (fast pool).
                                let! reportR = Transfer.runReconciling Transfer.Execute true true src sink contract reconciliation
                                let report = TransferCanaryFixtures.value reportR

                                // The reconciled kind skips its insert (its rows are already in the Sink).
                                let userOutcome = report.Kinds |> List.find (fun k -> k.Kind = userKind.SsKey)
                                Assert.Equal(IdentityDisposition.ReconciledByRule, userOutcome.Disposition)
                                Assert.Equal(0, userOutcome.RowsWritten)

                                // ghost@x (Source 999) had no Sink identity; Order 12 referencing it is dropped.
                                Assert.Contains(report.UnmatchedIdentities, fun (k, s) -> k = userKind.SsKey && s = SourceKey.ofString "999")
                                Assert.Contains(report.SkippedReferences, fun (owner, r: UnresolvedReference) ->
                                    owner = orderKind.SsKey && r.Target = userKind.SsKey && r.UnresolvedSource = SourceKey.ofString "999")

                                // Sink holds the 2 pre-existing users (none added) and 2 re-keyed orders.
                                let! users = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_RC_USER]"
                                let! orders = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_RC_ORDER]"
                                Assert.Equal(2, users)
                                Assert.Equal(2, orders)

                                // No inserted Order carries a Source surrogate — every FK was re-pointed.
                                let! sourceValued =
                                    TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_RC_ORDER] WHERE [USER_ID] IN (280,281,999)"
                                Assert.Equal(0, sourceValued)
                            })
                }))

    // 6.A.1 — the drop-set is fail-loud, not exit-0. The same Dev->UAT re-key
    // fixture that drops ghost@x's referencing Order (SkippedReferences +
    // UnmatchedIdentities) must map to a non-zero CLI exit, and --allow-drops
    // must downgrade it. The exit-code policy is the pure `exitCodeForReport`
    // that the CLI's runTransfer consumes, so this canary witnesses the same
    // decision the operator's refresh script branches on.
    [<Fact>]
    member _.``data canary: transfer with an unmatched FK exits non-zero (drop is fail-loud, not exit-0)`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "XferDrops") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "XferDropsSrc" (fun src _ ->
                task {
                    do! Deploy.executeBatch src TransferCanaryFixtures.reKeyDdl
                    do! Deploy.executeBatch src TransferCanaryFixtures.reKeySourceSeed
                    return!
                        fixture.WithEphemeralDatabase "XferDropsSink" (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink TransferCanaryFixtures.reKeyDdl
                                do! Deploy.executeBatch sink TransferCanaryFixtures.reKeySinkSeed

                                let! contractR = ReadSide.read src
                                let contract = TransferCanaryFixtures.value contractR
                                let userKind =
                                    Catalog.allModulesKinds contract |> List.map snd
                                    |> List.find (fun k -> TableId.tableText k.Physical = "OSUSR_RC_USER")
                                let emailName =
                                    userKind.Attributes |> List.find (fun a -> ColumnRealization.columnNameText a.Column = "EMAIL") |> (fun a -> a.Name)
                                let reconciliation =
                                    Map.ofList [ userKind.SsKey, ReconciliationStrategy.MatchByColumn emailName ]

                                // allowDrops = true: this witnesses the POST-write exit-code policy
                                // (AC-D8 drop fail-loud + the --allow-drops downgrade). The AC-I5
                                // pre-write halt (allowDrops = false) is witnessed separately by the
                                // pure `validateUserMap` test.
                                let! reportR = Transfer.runReconciling Transfer.Execute true true src sink contract reconciliation
                                let report = TransferCanaryFixtures.value reportR

                                // The drop-set is non-empty (ghost@x's Order vanished).
                                Assert.True(Transfer.hasDrops report)
                                Assert.True(Transfer.droppedRowCount report > 0)

                                // Fail-loud: a dropped run is a distinct non-zero exit, NOT 0.
                                Assert.Equal(Transfer.DroppedReferencesExit, Transfer.exitCodeForReport false report)
                                Assert.NotEqual(0, Transfer.exitCodeForReport false report)

                                // --allow-drops downgrades the declared-acceptable loss to exit 0.
                                Assert.Equal(0, Transfer.exitCodeForReport true report)
                            })
                }))

    // 6.A.2 LIFTED (operator-authorized 2026-06-10) — a cyclic AssignedBySink
    // load (self-ref IDENTITY kind with a nullable self-FK) now LOADS: Phase 1
    // inserts with the deferred self-FK NULLed and captures the minted keys;
    // Phase 2 re-points the self-FK through the completed remap and keys its
    // WHERE on the ASSIGNED PK. Source keys start at 1000 (sink mints 1..3,
    // no collision alibi) and VP's manager lands after VP in PK order (no
    // phase-1-only alibi). The manager chain by NAME is the proof.
    [<Fact>]
    member _.``data canary (6.A.2 lifted): a cyclic AssignedBySink kind loads — phase 2 re-points the self-FK keyed on the ASSIGNED PK`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "XferCyclicAssigned") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "XferCyclicAssignedSrc" (fun src _ ->
                task {
                    do! Deploy.executeBatch src TransferCanaryFixtures.cyclicAssignedDdl
                    do! Deploy.executeBatch src TransferCanaryFixtures.cyclicAssignedSeed
                    return!
                        fixture.WithEphemeralDatabase "XferCyclicAssignedSink" (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink TransferCanaryFixtures.cyclicAssignedDdl

                                let! contractR = ReadSide.read src
                                let contract = TransferCanaryFixtures.value contractR
                                let empKind =
                                    Catalog.allModulesKinds contract |> List.map snd
                                    |> List.find (fun k -> TableId.tableText k.Physical = "OSUSR_XF_EMPID")
                                // Precondition: the round-tripped contract classifies the kind
                                // AssignedBySink (its IDENTITY PK survives ReadSide).
                                Assert.Equal(IdentityDisposition.AssignedBySink, IdentityDisposition.ofKind empKind)

                                let! reportR = Transfer.run Transfer.Execute true src sink contract
                                let report = TransferCanaryFixtures.value reportR
                                Assert.Empty(report.SkippedReferences)

                                // All three rows landed with sink-minted keys.
                                let! rows = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_XF_EMPID]"
                                Assert.Equal(3, rows)
                                let! preserved = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_XF_EMPID] WHERE [ID] >= 1000"
                                Assert.Equal(0, preserved)

                                // The manager chain is identical by NAME across
                                // both estates — the phase-2 re-point hit the
                                // minted rows and pointed them at minted keys.
                                let! srcChain = TransferCanaryFixtures.managerChain src
                                let! sinkChain = TransferCanaryFixtures.managerChain sink
                                Assert.Equal<(string * string option) list>(srcChain, sinkChain)
                                Assert.Equal<(string * string option) list>(
                                    [ ("CEO", None); ("Mgr", Some "VP"); ("VP", Some "CEO") ], sinkChain)
                            })
                }))

    // 6.A.3 — a composite-IDENTITY AssignedBySink kind is refused, not
    // half-captured: the per-row OUTPUT capture is single-column and would
    // truncate the composite surrogate. The Sink stays empty.
    [<Fact>]
    member _.``data canary: composite-IDENTITY AssignedBySink is refused, not half-captured`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "XferCompositeAssigned") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "XferCompositeAssignedSrc" (fun src _ ->
                task {
                    do! Deploy.executeBatch src TransferCanaryFixtures.compositeAssignedDdl
                    do! Deploy.executeBatch src TransferCanaryFixtures.compositeAssignedSeed
                    return!
                        fixture.WithEphemeralDatabase "XferCompositeAssignedSink" (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink TransferCanaryFixtures.compositeAssignedDdl

                                let! contractR = ReadSide.read src
                                let contract = TransferCanaryFixtures.value contractR
                                let cmpKind =
                                    Catalog.allModulesKinds contract |> List.map snd
                                    |> List.find (fun k -> TableId.tableText k.Physical = "OSUSR_XF_CMP")
                                // Precondition: composite PK reconstructed (>1 IsPrimaryKey column)
                                // AND classified AssignedBySink (the IDENTITY leg survives ReadSide).
                                Assert.True((cmpKind.Attributes |> List.filter (fun a -> a.IsPrimaryKey) |> List.length) > 1)
                                Assert.Equal(IdentityDisposition.AssignedBySink, IdentityDisposition.ofKind cmpKind)

                                let! refusedR = Transfer.run Transfer.Execute true src sink contract
                                match refusedR with
                                | Error es ->
                                    Assert.True(
                                        es |> List.exists (fun e -> e.Code = "transfer.compositeSurrogateUnsupported"),
                                        sprintf "expected transfer.compositeSurrogateUnsupported, got %A" (es |> List.map (fun e -> e.Code)))
                                | Ok _ -> Assert.Fail("expected the composite-surrogate refusal at Execute")

                                let! rows = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_XF_CMP]"
                                Assert.Equal(0, rows)
                            })
                }))

    // WP-3 (F11) — empty-string Text ↔ NULL fidelity. The transfer IR now
    // carries NULL out-of-band, so a genuine empty-string Text value and a
    // NULL are DISTINCT end-to-end and both survive transfer faithfully —
    // the witness asserts preservation at the sink-DB level.
    [<Fact>]
    member _.``data canary: empty-string Text survives transfer distinct from NULL (F11 preservation)`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "XferEmptyText") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "XferEmptyTextSrc" (fun src _ ->
                task {
                    do! Deploy.executeBatch src TransferCanaryFixtures.emptyTextDdl
                    do! Deploy.executeBatch src TransferCanaryFixtures.emptyTextSeed
                    return!
                        fixture.WithEphemeralDatabase "XferEmptyTextSink" (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink TransferCanaryFixtures.emptyTextDdl

                                let! contractR = ReadSide.read src
                                let contract = TransferCanaryFixtures.value contractR
                                let! reportR = Transfer.run Transfer.Execute true src sink contract
                                let _ = TransferCanaryFixtures.value reportR

                                // Source: row 1 is '' (NOT NULL), row 2 NULL, row 3 'hello'.
                                let! srcBodies = TransferCanaryFixtures.noteBodies src
                                Assert.Equal<(int * bool) list>([ (1, false); (2, true); (3, false) ], srcBodies)

                                // Sink: row 1's empty string SURVIVES non-null (F11);
                                // NULL stays NULL; 'hello' survives non-null.
                                let! sinkBodies = TransferCanaryFixtures.noteBodies sink
                                Assert.Equal<(int * bool) list>([ (1, false); (2, true); (3, false) ], sinkBodies)
                            })
                }))

    // 6.B.1 — the Decision↔Data pre-flight. A tightening Decision
    // (EnforceNotNull) on a column whose source data carries NULLs is refused
    // (migrate.dataViolatesTightening) BEFORE any write — the coupling becomes
    // a named gate, not a crash mid-load. A clean overlay passes.
    [<Fact>]
    member _.``migrate pre-flight: EnforceNotNull on a NULL-bearing column refuses before writing`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "TighteningPreflight") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "TighteningPreflight" (fun src _ ->
                task {
                    do! Deploy.executeBatch src TransferCanaryFixtures.tighteningDdl
                    do! Deploy.executeBatch src TransferCanaryFixtures.tighteningSeed

                    let! contractR = ReadSide.read src
                    let contract = TransferCanaryFixtures.value contractR
                    let ticketKind =
                        Catalog.allModulesKinds contract |> List.map snd
                        |> List.find (fun k -> TableId.tableText k.Physical = "OSUSR_TG_TICKET")
                    let noteKey =
                        ticketKind.Attributes
                        |> List.find (fun a -> ColumnRealization.columnNameText a.Column = "NOTE")
                        |> (fun a -> a.SsKey)

                    // Clean (empty) overlay → the pre-flight passes.
                    let! clean = Preflight.tighteningPreflight src contract DecisionOverlay.empty
                    match clean with
                    | Ok () -> ()
                    | Error es -> Assert.Fail(sprintf "empty overlay should pass the pre-flight; got %A" (es |> List.map (fun e -> e.Code)))

                    // EnforceNotNull on NOTE (which has NULL rows) → refuse before writing.
                    let overlay = { DecisionOverlay.empty with EnforceNotNull = Set.singleton noteKey }
                    let! refused = Preflight.tighteningPreflight src contract overlay
                    match refused with
                    | Error es ->
                        Assert.True(
                            es |> List.exists (fun e -> e.Code = "migrate.dataViolatesTightening"),
                            sprintf "expected migrate.dataViolatesTightening, got %A" (es |> List.map (fun e -> e.Code)))
                    | Ok () -> Assert.Fail("expected the tightening pre-flight to refuse a NULL-bearing EnforceNotNull")
                }))

    // G7 — the WIRED migrate seam: the overlay is DERIVED from an A→B catalog
    // displacement (`tighteningOverlay` = the verb's exact composition), not
    // hand-built. A target B that narrows NOTE (nullable, NULL-bearing) to NOT
    // NULL refuses BEFORE any DDL; the table is read back UNCHANGED (NOTE still
    // nullable, all three rows intact). Discrimination: against the un-wired
    // code the verb would run the ALTER and crash mid-write on the NULL rows;
    // here the derived overlay is non-empty and the probe refuses first.
    [<Fact>]
    member _.``G7 witness: a migrate narrowing NOTE to NOT NULL on NULL data refuses before any DDL; table unchanged`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "G7Tightening") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "G7Tightening" (fun cnn _ ->
                task {
                    do! Deploy.executeBatch cnn TransferCanaryFixtures.tighteningDdl
                    do! Deploy.executeBatch cnn TransferCanaryFixtures.tighteningSeed

                    // State A — the deployed (NOTE nullable) contract, read live.
                    let! sourceAR = ReadSide.read cnn
                    let sourceA = TransferCanaryFixtures.value sourceAR

                    // State B — the SAME catalog with NOTE narrowed to NOT NULL.
                    let narrowNote (a: Attribute) : Attribute =
                        if ColumnRealization.columnNameText a.Column = "NOTE"
                        then { a with Column = ColumnRealization.create "NOTE" false |> Result.value }
                        else a
                    let target =
                        let mods =
                            sourceA.Modules
                            |> List.map (fun m ->
                                { m with Kinds = m.Kinds |> List.map (fun k -> { k with Attributes = k.Attributes |> List.map narrowNote }) })
                        Catalog.create mods [] |> Result.value

                    // The verb's exact derivation: overlay from the displacement.
                    let overlay = Preflight.tighteningOverlay sourceA target
                    Assert.False(Set.isEmpty overlay.EnforceNotNull, "the A→B displacement must narrow NOTE to NOT NULL")

                    // Wired refusal — probe the live data, refuse before any write.
                    let! refused = Preflight.tighteningPreflight cnn sourceA overlay
                    match refused with
                    | Error es ->
                        Assert.True(
                            es |> List.exists (fun e -> e.Code = "migrate.dataViolatesTightening"),
                            sprintf "expected migrate.dataViolatesTightening, got %A" (es |> List.map (fun e -> e.Code)))
                    | Ok () -> Assert.Fail("the derived overlay must refuse the NULL-bearing tightening before any DDL")

                    // Read back — NO DDL ran: NOTE is still nullable, rows intact.
                    let! isNullable =
                        TransferCanaryFixtures.scalarStr cnn
                            "SELECT IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='OSUSR_TG_TICKET' AND COLUMN_NAME='NOTE';"
                    Assert.Equal("YES", isNullable)
                    let! rowCount = TransferCanaryFixtures.countRows cnn "[dbo].[OSUSR_TG_TICKET]"
                    Assert.Equal(3, rowCount)
                }))

    // G7 relax — the CLI gate honored END-TO-END against a live CDC-tracked
    // sink. The G7 witness above proves the gate REFUSES; this proves the
    // steward's two reachable answers through the REAL CLI seam
    // (`Faces.Migrate.tighteningPreflight`, which composes `Intervene` + the
    // `RelaxationStore` blessing) against a CDC-tracked OSUSR_TG_TICKET:
    //   1. NO blessing, headless → the gate degrades to the named Halt fallback
    //      (Error 9); no DDL runs (NOTE still nullable, 3 rows intact). The
    //      default a steward gets when nothing is blessed.
    //   2. A persisted blessing covering the violating column (the A44-reachable
    //      equivalent of relax-ALWAYS) → the SAME headless run PROCEEDS, relaxing
    //      NOTE back to nullable so the emitted schema FITS the NULL-bearing
    //      data. The relaxed target then matches the deployed reality, so the
    //      live migrate is a faithful no-op: zero ALTERs and a MEASURED CDC delta
    //      of 0 (CDC-silent — the relaxation introduces no spurious capture; the
    //      meter is proven live by AC-X4, the same `executeAndMeasureCdc`
    //      primitive).
    // Discrimination: a gate that ignored the blessing would Halt leg 2 too; one
    // that relaxed WITHOUT the blessing would proceed in leg 1. The
    // PROJECTION_CONFIG blessing is the ONLY thing that flips the outcome.
    [<Fact>]
    member _.``G7 relax witness: a persisted blessing lets the CLI gate proceed on a CDC-tracked sink — NOTE stays nullable, data preserved, CDC-silent`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "G7RelaxCdc") then () else
        let prevConfig = System.Environment.GetEnvironmentVariable "PROJECTION_CONFIG"
        let configFile =
            Path.Combine(Path.GetTempPath(), "proj-relax-cdc-" + System.Guid.NewGuid().ToString("N") + ".json")
        try
            System.Environment.SetEnvironmentVariable("PROJECTION_CONFIG", configFile)
            TaskSync.run (fun () ->
                fixture.WithEphemeralDatabase "G7RelaxCdc" (fun cnn _ ->
                    task {
                        do! Deploy.executeBatch cnn TransferCanaryFixtures.tighteningDdl
                        do! Deploy.executeBatch cnn TransferCanaryFixtures.tighteningSeed

                        // State A — the deployed USER schema (NOTE nullable,
                        // NULL-bearing), read live BEFORE CDC is enabled. (Reading
                        // after enable would pull the whole `cdc.*` system schema
                        // into the catalog, and a name-based narrow would catch the
                        // CDC change-table's mirror NOTE column too.)
                        let! sourceAR = ReadSide.read cnn
                        let sourceA = TransferCanaryFixtures.value sourceAR

                        // State B — the SAME catalog with NOTE narrowed to NOT NULL.
                        let narrowNote (a: Attribute) : Attribute =
                            if ColumnRealization.columnNameText a.Column = "NOTE"
                            then { a with Column = ColumnRealization.create "NOTE" false |> Result.value }
                            else a
                        let target =
                            let mods =
                                sourceA.Modules
                                |> List.map (fun m ->
                                    { m with Kinds = m.Kinds |> List.map (fun k -> { k with Attributes = k.Attributes |> List.map narrowNote }) })
                            Catalog.create mods [] |> Result.value

                        // CDC-track the sink (skip if the container can't enable it).
                        let! enabled =
                            task {
                                try
                                    do! Deploy.executeBatch cnn "EXEC sys.sp_cdc_enable_db;"
                                    do! Deploy.executeBatch cnn "EXEC sys.sp_cdc_enable_table @source_schema = N'dbo', @source_name = N'OSUSR_TG_TICKET', @role_name = NULL, @supports_net_changes = 0;"
                                    use cmd = cnn.CreateCommand()
                                    cmd.CommandText <- "SELECT COUNT(*) FROM sys.tables WHERE is_tracked_by_cdc = 1 AND name = N'OSUSR_TG_TICKET';"
                                    let! c = cmd.ExecuteScalarAsync()
                                    return (System.Convert.ToInt32 c) > 0
                                with _ -> return false
                            }
                        if not enabled then
                            printfn "SKIP G7RelaxCdc: container did not enable CDC (flag not set)"
                        else
                            // LEG 1 — NO blessing, headless: the CLI gate degrades to
                            // the named Halt fallback (Error 9). No DDL runs.
                            Assert.True(
                                Set.isEmpty (Projection.Cli.RelaxationStore.read configFile),
                                "fixture: the config must carry no blessing yet")
                            let! halted = Projection.Cli.Faces.Migrate.tighteningPreflight sourceA target cnn
                            match halted with
                            | Error code -> Assert.Equal(9, code)
                            | Ok _ -> Assert.Fail("with no blessing the headless gate must Halt (Error 9), not proceed")
                            // No DDL ran — NOTE still nullable, all three rows intact.
                            let! nullableAfterHalt =
                                TransferCanaryFixtures.scalarStr cnn
                                    "SELECT IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='OSUSR_TG_TICKET' AND COLUMN_NAME='NOTE';"
                            Assert.Equal("YES", nullableAfterHalt)
                            let! rowsAfterHalt = TransferCanaryFixtures.countRows cnn "[dbo].[OSUSR_TG_TICKET]"
                            Assert.Equal(3, rowsAfterHalt)

                            // Persist the blessing — the EXACT violation keys the gate
                            // matches against (the A44-reachable relax-ALWAYS outcome).
                            let overlay = Preflight.tighteningOverlay sourceA target
                            let! violR = Preflight.tighteningViolations cnn sourceA overlay
                            let violations = TransferCanaryFixtures.value violR
                            Assert.NotEmpty violations
                            let ids = violations |> List.map Preflight.violationKey
                            Assert.True(
                                Projection.Cli.RelaxationStore.persist configFile ids = Ok (),
                                "fixture: the blessing must persist")

                            // LEG 2 — WITH the blessing: the same headless gate now
                            // PROCEEDS, relaxing NOTE back to nullable.
                            let! relaxed = Projection.Cli.Faces.Migrate.tighteningPreflight sourceA target cnn
                            let effectiveTarget =
                                match relaxed with
                                | Ok cat -> cat
                                | Error code -> failwithf "with the blessing the gate must proceed; got Error %d" code
                            // The relaxed target no longer narrows — the gate is satisfied.
                            Assert.True(
                                Set.isEmpty (Preflight.tightenedToNotNull sourceA effectiveTarget),
                                "the relaxed target must no longer narrow NOTE to NOT NULL")

                            // Apply A→effectiveTarget live on the CDC-tracked sink and
                            // measure (allowCdc=true — the sink is CDC-tracked). The
                            // relaxed target matches the deployed reality, so this is a
                            // faithful no-op: zero ALTERs, CDC-silent.
                            let! measured =
                                MigrationRun.executeAndMeasureCdc false true DeclareNone sourceA effectiveTarget cnn
                            match measured with
                            | Error e -> Assert.Fail(sprintf "the relaxed migrate must proceed, got %A" e)
                            | Ok (o, cdcDelta) ->
                                Assert.Empty(o.Artifacts.SchemaStatements)   // zero ALTERs — relaxed == deployed
                                Assert.Equal(0, cdcDelta)                    // CDC-silent, MEASURED (meter live: AC-X4)

                            // Data preserved, NOTE still nullable — the relaxation held.
                            let! nullableAfter =
                                TransferCanaryFixtures.scalarStr cnn
                                    "SELECT IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='OSUSR_TG_TICKET' AND COLUMN_NAME='NOTE';"
                            Assert.Equal("YES", nullableAfter)
                            let! rowCount = TransferCanaryFixtures.countRows cnn "[dbo].[OSUSR_TG_TICKET]"
                            Assert.Equal(3, rowCount)
                    }))
        finally
            System.Environment.SetEnvironmentVariable("PROJECTION_CONFIG", prevConfig)
            (try File.Delete configFile with _ -> ())

    // F4 (audit 2026-06-17) — INGEST FORWARD-COMPLETENESS. The adjunction proof
    // (AdjunctionLawTests) is pure, and the Docker canary reads via ReadSide on
    // one fixture; neither ENUMERATES the physical facets of a deployed column to
    // assert each is either carried into the Catalog OR a named, closed ingest
    // erasure. So a facet silently dropped at ingest (the F1 collation / F10
    // identity-seed class) was invisible to the round-trip proof. This closes the
    // loop for the ReadSide (deployed-target) ingest leg: deploy a column-rich
    // source, read it back, and hold every facet to the ledger.
    //
    // THE FACET LEDGER (ReadSide ingest):
    //   CARRIED — column name, nullability (asserted to round-trip below).
    //   ERASED  — collation (F1 ReadSide follow-on: INFORMATION_SCHEMA.COLUMNS
    //             .COLLATION_NAME not yet read) and identity seed/increment (F10
    //             ReadSide follow-on: sys.identity_columns not yet read). Each is
    //             `None` in the Catalog; the asserts below are the TRIPWIRES —
    //             when a follow-on wires the read, the `None` assertion fails,
    //             forcing the facet to move CARRIED and this ledger to update.
    //             (Through the OSSYS rowset path collation IS already carried —
    //             F1, witnessed in OsmRowsetReaderTests; this is the ReadSide leg.)
    [<Fact>]
    member _.``F4 forward-completeness: each deployed column facet is carried into the Catalog or a named ReadSide ingest erasure`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "F4Completeness") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "F4Completeness" (fun cnn _ ->
                task {
                    do! Deploy.executeBatch cnn
                            ("CREATE TABLE [dbo].[OSUSR_F4_WIDGET] (" +
                             "[ID] INT NOT NULL IDENTITY(1,1) PRIMARY KEY, " +
                             "[LABEL] NVARCHAR(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL, " +
                             "[NOTE] NVARCHAR(50) NULL);")
                    let! catR = ReadSide.read cnn
                    let catalog = TransferCanaryFixtures.value catR
                    let attrs =
                        Catalog.allKinds catalog |> List.collect (fun k -> k.Attributes)
                    let byCol (name: string) : Attribute =
                        attrs |> List.find (fun a -> ColumnRealization.columnNameEquals name a.Column)
                    let id    = byCol "ID"
                    let label = byCol "LABEL"
                    let note  = byCol "NOTE"

                    // CARRIED — nullability round-trips (the discriminator: a read
                    // that lost it would collapse every column to one polarity).
                    Assert.False(id.Column.IsNullable, "ID deployed NOT NULL must read back NOT NULL")
                    Assert.False(label.Column.IsNullable, "LABEL deployed NOT NULL must read back NOT NULL")
                    Assert.True(note.Column.IsNullable, "NOTE deployed NULL must read back nullable")

                    // ERASED (named ReadSide ingest erasures) — the TRIPWIRES.
                    // LABEL carries a non-default COLLATE on the wire, but ReadSide
                    // does not read collation_name yet (F1 follow-on).
                    Assert.Equal<string option>(None, label.Column.Collation)
                    // ID is IDENTITY(1,1) on the wire, but ReadSide does not read
                    // sys.identity_columns seed/increment yet (F10 follow-on).
                    Assert.Equal<(int64 * int64) option>(None, id.Column.Identity)
                }))

    // G1 — the transfer spanning pre-flight (connection), WIRED into the
    // Execute path. Driving `Transfer.run Execute` against a dead/unreachable
    // sink refuses with `transfer.connectionUnavailable` BEFORE any write.
    // Discrimination: against the un-wired `runCore` (which opened both
    // endpoints and ran straight into ingest/plan/write), the dead sink would
    // surface as a *mid-load* failure carrying some other code — not the named
    // connection refusal. Asserting the SPECIFIC code is what fails the
    // un-wired behavior. The source is read back untouched.
    [<Fact>]
    member _.``G1 witness: Transfer.run Execute refuses a dead sink endpoint with the connection code before any write`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "G1Connection") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "G1Connection" (fun src _ ->
                task {
                    do! Deploy.executeBatch src TransferCanaryFixtures.twoTableDdl
                    do! Deploy.executeBatch src TransferCanaryFixtures.twoTableSeed
                    let! contractR = ReadSide.read src
                    let contract = TransferCanaryFixtures.value contractR

                    // A sink pointed at an unreachable endpoint (a port nothing
                    // listens on), short login timeout so the probe fails fast.
                    use deadSink =
                        new Microsoft.Data.SqlClient.SqlConnection(
                            "Server=127.0.0.1,1;Database=nope;User Id=sa;Password=nope;TrustServerCertificate=true;Connect Timeout=3")
                    // allowCdc=true so the CDC gate is not what fires; the
                    // spanning gate is the one under test.
                    let! result = Transfer.run Transfer.Execute true src deadSink contract
                    match result with
                    | Error es ->
                        Assert.True(
                            es |> List.exists (fun e -> e.Code = "transfer.connectionUnavailable"),
                            sprintf "expected transfer.connectionUnavailable (the wired G1 refusal), got %A" (es |> List.map (fun e -> e.Code)))
                    | Ok _ -> Assert.Fail("Transfer.run Execute must refuse a dead sink endpoint before any write")

                    // The source is unchanged — the gate ran before any mutation.
                    let! custRows = TransferCanaryFixtures.countRows src "[dbo].[OSUSR_XF_CUSTOMER]"
                    Assert.Equal(2, custRows)
                }))

    // 6.B.2 — RefactorLog-aware Transfer end-to-end. The source is at schema A
    // (EMAIL); the sink is at schema B (the column renamed to CONTACT). The
    // rename map (from the A→B diff) re-points each source row's value onto the
    // sink's name BY IDENTITY — so the renamed column carries the source data,
    // not NULL (which a source-name or ordinal write would produce).
    [<Fact>]
    member _.``transfer: a renamed column is re-pointed by the rename map, not matched by ordinal`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "XferRename") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "XferRenameSrc" (fun src _ ->
                task {
                    do! Deploy.executeBatch src (SsdtDdlEmitter.statements TransferCanaryFixtures.renameSourceContract |> Render.toText)
                    do! Deploy.executeBatch src
                            "INSERT INTO [dbo].[RP_XFER_CUSTOMER] ([ID],[EMAIL]) VALUES (1, N'alice@x'), (2, N'bob@x');"
                    return!
                        fixture.WithEphemeralDatabase "XferRenameSink" (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink (SsdtDdlEmitter.statements TransferCanaryFixtures.renameSinkContract |> Render.toText)

                                let! reportR =
                                    Transfer.runWithRenames Transfer.Execute true src sink
                                        TransferCanaryFixtures.renameSourceContract
                                        TransferCanaryFixtures.renameSinkContract
                                let _ = TransferCanaryFixtures.value reportR

                                // The renamed CONTACT column carries the source's EMAIL values —
                                // re-pointed by the rename map (identity), not lost to NULL.
                                let! c1 = TransferCanaryFixtures.scalarStr sink "SELECT [CONTACT] FROM [dbo].[RP_XFER_CUSTOMER] WHERE [ID]=1;"
                                Assert.Equal("alice@x", c1)
                                let! c2 = TransferCanaryFixtures.scalarStr sink "SELECT [CONTACT] FROM [dbo].[RP_XFER_CUSTOMER] WHERE [ID]=2;"
                                Assert.Equal("bob@x", c2)
                                let! n = TransferCanaryFixtures.countRows sink "[dbo].[RP_XFER_CUSTOMER]"
                                Assert.Equal(2, n)
                            })
                }))

    // M3 / LE-2 — the legacy B→A reverse-leg canary. The migration team's data
    // sits in the LOGICAL on-prem rendition ([dbo].[Customer].[Email]); the engine
    // pipes it UP into the PHYSICAL cloud rendition ([dbo].[OSUSR_XF_CUSTOMER].[Contact]).
    // Same SsKey-stable model — NOT foreign schema, no tolerances. The two-contract
    // path resolves the write target table against the SINK contract by SsKey
    // (table-name rendition) and re-points the column by the rename map (column-name
    // rendition), so CONTACT in the OSUSR sink carries the source's EMAIL data.
    [<Fact>]
    member _.``M3/LE-2 legacy B->A: logical source pipes up into the physical OSUSR sink, data round-trips by SsKey`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "LegacyBA") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "LegacyBASrc" (fun src _ ->
                task {
                    // B (logical): the hosted on-prem model the migration team filled.
                    do! Deploy.executeBatch src (SsdtDdlEmitter.statements TransferCanaryFixtures.legacySourceContract |> Render.toText)
                    do! Deploy.executeBatch src
                            "INSERT INTO [dbo].[Customer] ([ID],[EMAIL]) VALUES (1, N'alice@x'), (2, N'bob@x');"
                    return!
                        fixture.WithEphemeralDatabase "LegacyBASink" (fun sink _ ->
                            task {
                                // A (physical): the frozen OSUSR_* cloud rendition, empty.
                                do! Deploy.executeBatch sink (SsdtDdlEmitter.statements TransferCanaryFixtures.legacySinkContract |> Render.toText)

                                // The up leg (B→A) over the same model.
                                let! reportR =
                                    Transfer.runWithRenames Transfer.Execute true src sink
                                        TransferCanaryFixtures.legacySourceContract
                                        TransferCanaryFixtures.legacySinkContract
                                let _ = TransferCanaryFixtures.value reportR

                                // The physical OSUSR sink's CONTACT column carries the logical
                                // source's EMAIL data — table + column rendition resolved by SsKey,
                                // not lost to NULL (a source-name/ordinal write) or a missing table.
                                let! c1 = TransferCanaryFixtures.scalarStr sink "SELECT [CONTACT] FROM [dbo].[OSUSR_XF_CUSTOMER] WHERE [ID]=1;"
                                Assert.Equal("alice@x", c1)
                                let! c2 = TransferCanaryFixtures.scalarStr sink "SELECT [CONTACT] FROM [dbo].[OSUSR_XF_CUSTOMER] WHERE [ID]=2;"
                                Assert.Equal("bob@x", c2)
                                let! n = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_XF_CUSTOMER]"
                                Assert.Equal(2, n)
                            })
                }))

    // J3 closed / LE-1 — the SAME reverse leg with the contracts RENDERED from
    // the ONE authored model by `CatalogRendition` (the production contract
    // source the CLI arm uses), not hand-built: the logical source contract is
    // DERIVED (table OSUSR_XF_CUSTOMER → Customer; columns ID/CONTACT → Id/Email),
    // deployed, seeded, and piped up into the as-authored physical sink.
    [<Fact>]
    member _.``M3/LE-1 legacy B->A: contracts RENDERED from the one authored model (CatalogRendition) round-trip`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "LegacyBARendered") then () else
        let model = TransferCanaryFixtures.legacyAuthoredModel
        let logicalContract = CatalogRendition.logical model
        let physicalContract = CatalogRendition.physical model
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "LegacyRenderSrc" (fun src _ ->
                task {
                    // B (logical): deployed from the RENDERED logical contract.
                    do! Deploy.executeBatch src (SsdtDdlEmitter.statements logicalContract |> Render.toText)
                    do! Deploy.executeBatch src
                            "INSERT INTO [dbo].[Customer] ([Id],[Email]) VALUES (1, N'alice@x'), (2, N'bob@x');"
                    return!
                        fixture.WithEphemeralDatabase "LegacyRenderSink" (fun sink _ ->
                            task {
                                // A (physical): the as-authored OSUSR rendition, empty.
                                do! Deploy.executeBatch sink (SsdtDdlEmitter.statements physicalContract |> Render.toText)

                                let! reportR =
                                    Transfer.runWithRenames Transfer.Execute true src sink
                                        logicalContract physicalContract
                                let _ = TransferCanaryFixtures.value reportR

                                let! c1 = TransferCanaryFixtures.scalarStr sink "SELECT [CONTACT] FROM [dbo].[OSUSR_XF_CUSTOMER] WHERE [ID]=1;"
                                Assert.Equal("alice@x", c1)
                                let! c2 = TransferCanaryFixtures.scalarStr sink "SELECT [CONTACT] FROM [dbo].[OSUSR_XF_CUSTOMER] WHERE [ID]=2;"
                                Assert.Equal("bob@x", c2)
                                let! n = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_XF_CUSTOMER]"
                                Assert.Equal(2, n)
                            })
                }))

    // §5.2 Slice E — the data adjunction for AssignedBySink (sink-minted keys).
    // User has an IDENTITY PK, so the Sink mints fresh surrogates per row; the
    // capture (INSERT…OUTPUT) feeds a SurrogateRemapContext that re-points every
    // Order's USER_ID. The round-trip holds MODULO that remap: the surrogate
    // values differ, but the (Order → User-by-email) relationship is identical.
    [<Fact>]
    member _.``data adjunction: AssignedBySink round-trips modulo SurrogateRemapContext`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "XferAssigned") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "XferAssignedSrc" (fun src _ ->
                task {
                    do! Deploy.executeBatch src TransferCanaryFixtures.assignedBySinkDdl
                    do! Deploy.executeBatch src TransferCanaryFixtures.assignedBySinkSourceSeed
                    return!
                        fixture.WithEphemeralDatabase "XferAssignedSink" (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink TransferCanaryFixtures.assignedBySinkDdl   // same schema, empty

                                let! contractR = ReadSide.read src
                                let contract = TransferCanaryFixtures.value contractR
                                let userKind =
                                    Catalog.allModulesKinds contract |> List.map snd
                                    |> List.find (fun k -> TableId.tableText k.Physical = "OSUSR_AS_USER")
                                // Precondition: the reconstructed contract classifies User as
                                // AssignedBySink (its IDENTITY PK survives the ReadSide round-trip).
                                Assert.Equal(IdentityDisposition.AssignedBySink, IdentityDisposition.ofKind userKind)

                                let! reportR = Transfer.run Transfer.Execute true src sink contract
                                let report = TransferCanaryFixtures.value reportR
                                Assert.Empty(report.SkippedReferences)
                                Assert.True(report.Kinds |> List.forall (fun k -> k.RowsWritten = k.RowsIngested))

                                // Row counts preserved on both kinds.
                                let! users  = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_AS_USER]"
                                let! orders = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_AS_ORDER]"
                                Assert.Equal(2, users)
                                Assert.Equal(2, orders)

                                // The Sink minted NEW surrogates — the Source IDs (280/281) are gone,
                                // so the FK re-point was load-bearing (not an accidental ID collision).
                                let! sourceIds =
                                    TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_AS_USER] WHERE [ID] IN (280,281)"
                                Assert.Equal(0, sourceIds)

                                // …yet the (Order → User-by-email) relationship is identical:
                                // round-trip holds MODULO the SurrogateRemapContext.
                                let! srcPairs  = TransferCanaryFixtures.orderUserEmail src
                                let! sinkPairs = TransferCanaryFixtures.orderUserEmail sink
                                Assert.Equal<(int * string) list>(srcPairs, sinkPairs)
                                Assert.Equal<(int * string) list>([ (10, "alice@x"); (11, "bob@x") ], sinkPairs)
                            })
                }))

    // Slice 4.2 — the reconcile canary driven THROUGH the TransferConnections
    // apparatus (not caller-opened connections), with a ManualOverride CSV
    // round-tripping into the reconcile path. The ephemeral per-DB connection
    // strings are written to temp files and referenced via ConnectionRef.File
    // (D9: the secret lives out of band); openSubstrate resolves + opens them;
    // runThroughConnections reads the contract from the Source, resolves the
    // CSV-derived ManualOverride against it, and runs. The reconcile outcome
    // matches the MatchByColumn canary above: User reconciled (0 rows written),
    // ghost 999 unmatched, Order 12 skipped, every FK re-pointed.
    [<Fact>]
    member _.``4.2: reconcile driven through TransferConnections + ManualOverride CSV round-trips`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "XferAppar") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "XferApparSrc" (fun src srcConnStr ->
                task {
                    do! Deploy.executeBatch src TransferCanaryFixtures.reKeyDdl
                    do! Deploy.executeBatch src TransferCanaryFixtures.reKeySourceSeed
                    return!
                        fixture.WithEphemeralDatabase "XferApparSink" (fun sink sinkConnStr ->
                            task {
                                do! Deploy.executeBatch sink TransferCanaryFixtures.reKeyDdl
                                do! Deploy.executeBatch sink TransferCanaryFixtures.reKeySinkSeed

                                // D9: write the per-DB connection strings to temp files and
                                // reference them out of band — the apparatus resolves them.
                                let srcFile = Path.GetTempFileName()
                                let sinkFile = Path.GetTempFileName()
                                File.WriteAllText(srcFile, srcConnStr)
                                File.WriteAllText(sinkFile, sinkConnStr)
                                try
                                    let srcSub : Substrate =
                                        { Environment = Projection.Core.Environment.Dev
                                          Role = SubstrateRole.Source
                                          ConnectionRef = ConnectionRef.File srcFile }
                                    let sinkSub : Substrate =
                                        { Environment = Projection.Core.Environment.Uat
                                          Role = SubstrateRole.Sink
                                          ConnectionRef = ConnectionRef.File sinkFile }
                                    let connections =
                                        TransferConnections.create srcSub sinkSub true
                                        |> TransferCanaryFixtures.value
                                    // Reconcile ⇒ the Sink is profiled for identity too.
                                    Assert.Equal(2, connections.ProfiledForIdentity.Length)

                                    // The ManualOverride map arrives as a CSV that is parsed
                                    // and resolved against the reconstructed contract — the
                                    // full round-trip into the reconcile path.
                                    let userMapCsv =
                                        "table,source,assigned\n" +
                                        "OSUSR_RC_USER,280,18\n" +
                                        "OSUSR_RC_USER,281,19"
                                    let resolveReconciliation (contract: Catalog) =
                                        match TransferSpec.parseUserMapCsv userMapCsv with
                                        | Error es -> Error es
                                        | Ok entries -> TransferSpec.resolveAllReconciliation contract [] entries

                                    // allowDrops = true: observe the post-write drop set (999 is
                                    // outside the override map) past the AC-I5 pre-write gate.
                                    let! reportR =
                                        Transfer.runThroughConnections Transfer.Execute true true connections resolveReconciliation
                                    let report = TransferCanaryFixtures.value reportR

                                    // Map tables → reconstructed SsKeys for the assertions.
                                    let! contractR = ReadSide.read src
                                    let contract = TransferCanaryFixtures.value contractR
                                    let kindByTable (t: string) =
                                        Catalog.allModulesKinds contract |> List.map snd |> List.find (fun k -> TableId.tableText k.Physical = t)
                                    let userKind = kindByTable "OSUSR_RC_USER"
                                    let orderKind = kindByTable "OSUSR_RC_ORDER"

                                    let userOutcome = report.Kinds |> List.find (fun k -> k.Kind = userKind.SsKey)
                                    Assert.Equal(IdentityDisposition.ReconciledByRule, userOutcome.Disposition)
                                    Assert.Equal(0, userOutcome.RowsWritten)
                                    Assert.Contains(report.UnmatchedIdentities, fun (k, s) -> k = userKind.SsKey && s = SourceKey.ofString "999")
                                    Assert.Contains(report.SkippedReferences, fun (owner, r: UnresolvedReference) ->
                                        owner = orderKind.SsKey && r.Target = userKind.SsKey && r.UnresolvedSource = SourceKey.ofString "999")

                                    let! users = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_RC_USER]"
                                    let! orders = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_RC_ORDER]"
                                    Assert.Equal(2, users)
                                    Assert.Equal(2, orders)
                                    let! sourceValued =
                                        TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_RC_ORDER] WHERE [USER_ID] IN (280,281,999)"
                                    Assert.Equal(0, sourceValued)
                                finally
                                    try File.Delete srcFile with _ -> ()
                                    try File.Delete sinkFile with _ -> ()
                            })
                }))

    [<Fact>]
    member _.``data canary: dry-run reports the plan and writes nothing to the Sink`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "XferDry") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "XferDrySrc" (fun src _ ->
                task {
                    do! Deploy.executeBatch src TransferCanaryFixtures.twoTableDdl
                    do! Deploy.executeBatch src TransferCanaryFixtures.twoTableSeed
                    return!
                        fixture.WithEphemeralDatabase "XferDrySink" (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink TransferCanaryFixtures.twoTableDdl

                                let! contractR = ReadSide.read src
                                let contract = TransferCanaryFixtures.value contractR

                                let! reportR = Transfer.run Transfer.DryRun true src sink contract
                                let report = TransferCanaryFixtures.value reportR
                                Assert.Equal(Transfer.DryRun, report.Mode)
                                // Rows were ingested + planned, but none written.
                                Assert.True(report.Kinds |> List.exists (fun k -> k.RowsIngested > 0))
                                Assert.True(report.Kinds |> List.forall (fun k -> k.RowsWritten = 0))

                                // The Sink remains empty.
                                let! custCount = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_XF_CUSTOMER]"
                                let! orderCount = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_XF_ORDER]"
                                Assert.Equal(0, custCount)
                                Assert.Equal(0, orderCount)
                            })
                }))

    // PE-3 — the golden cloud→cloud re-key canary (P-REKEY / AC-I2). A `golden`
    // flow copies a subset (`tables = [Order]`) and re-keys the user FK to the
    // Sink's OWN users by EMAIL. User is EXCLUDED from the copied set yet KEPT
    // for reconcile (it builds the email remap; its rows are never written).
    // Proof the re-key is an Update, not a re-import: the (Order→User-by-email)
    // join is identical Source↔Sink, the Source user surrogates are provably
    // ABSENT from the Sink, and the Sink's own user inventory is unchanged.
    [<Fact>]
    member _.``PE-3 golden: cloud->cloud re-key — User excluded, FKs re-keyed by email (P-REKEY/AC-I2)`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "GoldenPE3") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "GoldenPE3Src" (fun src srcConnStr ->
                task {
                    do! Deploy.executeBatch src TransferCanaryFixtures.reKeyDdl
                    do! Deploy.executeBatch src TransferCanaryFixtures.goldenSourceSeed
                    return!
                        fixture.WithEphemeralDatabase "GoldenPE3Sink" (fun sink sinkConnStr ->
                            task {
                                do! Deploy.executeBatch sink TransferCanaryFixtures.reKeyDdl
                                do! Deploy.executeBatch sink TransferCanaryFixtures.reKeySinkSeed

                                // The Order kind's LOGICAL name — the golden `tables` subset
                                // (`resolveLoadSet` matches the declared subset by logical Name).
                                let! contractR = ReadSide.read src
                                let contract = TransferCanaryFixtures.value contractR
                                let kindByTable (t: string) =
                                    Catalog.allModulesKinds contract |> List.map snd
                                    |> List.find (fun k -> TableId.tableText k.Physical = t)
                                let orderKind = kindByTable "OSUSR_RC_ORDER"
                                let userKind  = kindByTable "OSUSR_RC_USER"
                                let orderName = Name.value orderKind.Name

                                let srcFile = Path.GetTempFileName()
                                let sinkFile = Path.GetTempFileName()
                                File.WriteAllText(srcFile, srcConnStr)
                                File.WriteAllText(sinkFile, sinkConnStr)
                                try
                                    let srcSub : Substrate =
                                        { Environment = Projection.Core.Environment.Dev
                                          Role = SubstrateRole.Source
                                          ConnectionRef = ConnectionRef.File srcFile }
                                    let sinkSub : Substrate =
                                        { Environment = Projection.Core.Environment.Uat
                                          Role = SubstrateRole.Sink
                                          ConnectionRef = ConnectionRef.File sinkFile }
                                    let connections =
                                        TransferConnections.create srcSub sinkSub true
                                        |> TransferCanaryFixtures.value

                                    // tables = [Order]; reconcile User by email. allowDrops = false:
                                    // every Source user matches, so the AC-I5 gate passes cleanly.
                                    let! reportR =
                                        Transfer.runThroughConnectionsWithEmission
                                            Transfer.Execute EmissionMode.Incremental true false
                                            [ orderName ] connections TransferCanaryFixtures.resolveUserByEmail
                                    let report = TransferCanaryFixtures.value reportR

                                    // (c) User is reconciled, not copied — 0 rows written, no drops.
                                    let userOutcome = report.Kinds |> List.find (fun k -> k.Kind = userKind.SsKey)
                                    Assert.Equal(IdentityDisposition.ReconciledByRule, userOutcome.Disposition)
                                    Assert.Equal(0, userOutcome.RowsWritten)
                                    Assert.Empty(report.UnmatchedIdentities)
                                    Assert.Empty(report.SkippedReferences)

                                    // (a) The (Order→User-by-email) join is identical Source↔Sink.
                                    let! srcPairs = TransferCanaryFixtures.orderUserEmailRc src
                                    let! sinkPairs = TransferCanaryFixtures.orderUserEmailRc sink
                                    Assert.Equal<(int * string) list>(srcPairs, sinkPairs)
                                    Assert.Equal<(int * string) list>([ (10, "alice@x"); (11, "bob@x") ], sinkPairs)

                                    // (b) Source user surrogates ABSENT from the Sink; Sink's own
                                    // 2 users unchanged; every Order FK re-pointed to a Sink surrogate.
                                    let! users  = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_RC_USER]"
                                    let! orders = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_RC_ORDER]"
                                    Assert.Equal(2, users)
                                    Assert.Equal(2, orders)
                                    let! srcUsersInSink =
                                        TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_RC_USER] WHERE [ID] IN (280,281)"
                                    let! srcFksInSink =
                                        TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_RC_ORDER] WHERE [USER_ID] IN (280,281)"
                                    Assert.Equal(0, srcUsersInSink)
                                    Assert.Equal(0, srcFksInSink)
                                finally
                                    try File.Delete srcFile with _ -> ()
                                    try File.Delete sinkFile with _ -> ()
                            })
                }))

    // PE-2 — the validate-user-map pre-write halt on the golden flow (AC-I5). A
    // Source user with no Sink email match (ghost 999) makes Order 12 an unmapped
    // FK; with --allow-drops OFF the load refuses BEFORE any DML — no silent NULL,
    // no partial write. The Sink Order table stays empty (untouched).
    [<Fact>]
    member _.``PE-2 golden: an unmapped user FK halts the cloud load before any DML (AC-I5)`` () =
        if not (TransferCanaryFixtures.skipIfNoDocker "GoldenPE2") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "GoldenPE2Src" (fun src srcConnStr ->
                task {
                    do! Deploy.executeBatch src TransferCanaryFixtures.reKeyDdl
                    do! Deploy.executeBatch src TransferCanaryFixtures.reKeySourceSeed   // includes ghost 999
                    return!
                        fixture.WithEphemeralDatabase "GoldenPE2Sink" (fun sink sinkConnStr ->
                            task {
                                do! Deploy.executeBatch sink TransferCanaryFixtures.reKeyDdl
                                do! Deploy.executeBatch sink TransferCanaryFixtures.reKeySinkSeed

                                let! contractR = ReadSide.read src
                                let contract = TransferCanaryFixtures.value contractR
                                let orderName =
                                    Catalog.allModulesKinds contract |> List.map snd
                                    |> List.find (fun k -> TableId.tableText k.Physical = "OSUSR_RC_ORDER")
                                    |> fun k -> Name.value k.Name

                                let srcFile = Path.GetTempFileName()
                                let sinkFile = Path.GetTempFileName()
                                File.WriteAllText(srcFile, srcConnStr)
                                File.WriteAllText(sinkFile, sinkConnStr)
                                try
                                    let srcSub : Substrate =
                                        { Environment = Projection.Core.Environment.Dev
                                          Role = SubstrateRole.Source
                                          ConnectionRef = ConnectionRef.File srcFile }
                                    let sinkSub : Substrate =
                                        { Environment = Projection.Core.Environment.Uat
                                          Role = SubstrateRole.Sink
                                          ConnectionRef = ConnectionRef.File sinkFile }
                                    let connections =
                                        TransferConnections.create srcSub sinkSub true
                                        |> TransferCanaryFixtures.value

                                    let! reportR =
                                        Transfer.runThroughConnectionsWithEmission
                                            Transfer.Execute EmissionMode.Incremental true false
                                            [ orderName ] connections TransferCanaryFixtures.resolveUserByEmail
                                    // The unmapped 999 halts pre-write: Error transfer.unmappedIdentities.
                                    match reportR with
                                    | Error es ->
                                        Assert.True(
                                            es |> List.exists (fun e -> e.Code = "transfer.unmappedIdentities"),
                                            sprintf "expected transfer.unmappedIdentities, got %A" (es |> List.map (fun e -> e.Code)))
                                    | Ok _ -> Assert.Fail("expected the validate-user-map pre-write halt on the unmapped user FK")

                                    // No DML reached the Sink — the Order table is still empty.
                                    let! orders = TransferCanaryFixtures.countRows sink "[dbo].[OSUSR_RC_ORDER]"
                                    Assert.Equal(0, orders)
                                finally
                                    try File.Delete srcFile with _ -> ()
                                    try File.Delete sinkFile with _ -> ()
                            })
                }))
