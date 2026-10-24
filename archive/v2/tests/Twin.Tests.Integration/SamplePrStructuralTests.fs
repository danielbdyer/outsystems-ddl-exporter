module Twin.Tests.Integration.SamplePrStructuralTests

open System.Threading.Tasks
open Xunit
open Projection.Core
open Twin.Core
open Twin.Runtime
open Twin.Tests.Integration

// ---------------------------------------------------------------------------
// SAMPLE PRs — the STRUCTURAL "copy-then-drop" archetype: three HEAVY,
// multi-release operations that RELOCATE DATA BETWEEN SHAPES (split one entity
// into two, merge two into one, move a field across entities). None of them can
// ship in a single publish, so — exactly like extract-to-lookup — each is proven
// as PHASE 1: the additive half lands under the production-faithful publish, the
// copy is proven to preserve cardinality (1:1) and values (a digest match), and
// the subtractive drop is shown to be the guarded LATER phase.
//
// THE GOVERNING POSTURE (SamplePrPublish.strict / EstateModel.publishStrict):
// BlockOnPossibleDataLoss = true, GenerateSmartDefaults = false,
// DropObjectsNotInSource = FALSE. Two consequences, both load-bearing here:
//   * a populated COLUMN drop is not an "object not in source" — the DROP COLUMN
//     proceeds and BlockOnPossibleDataLoss BLOCKS it (the row-presence guard).
//     This is the guarded later phase for split-table and move-attribute.
//   * a whole-TABLE removal IS an "object not in source" — DropObjectsNotInSource
//     = false PROTECTS it: a PHANTOM removal (publish Ok, the table survives).
//     This is why merge-tables' Phase-3 absorbed-table drop is a deliberate
//     scripted DROP TABLE, not an automatic declarative one.
//
// The three ops and their PHASE-1 proofs (proven, not assumed):
//   1. split-table    — Customer -> Customer + a new CustomerContact holding
//      Email, linked 1:1 by CustomerId. Additive CREATE lands (strict Ok);
//      INSERT..SELECT copies all 25 Emails 1:1 (0 lost, 0 duplicated); the
//      Email digest matches source byte-for-byte; the FK lands trusted. Then the
//      drop of the source column Customer.Email is BLOCKED single-phase (the
//      guarded later phase).
//   2. merge-tables   — a 1:1 CustomerAddress satellite folded back into Customer
//      (Customer gains PostalCode). THE CRITICAL LESSON: prove cardinality is 1:1
//      (absorbed rows == distinct parents) BEFORE copying. The copy preserves
//      every row/value (0 NULL, digest match). A 1:many counterexample fires the
//      cardinality probe (26 != 25) and shows the naive copy silently drops a row
//      a value-hash would never flag. The absorbed-table drop is a PHANTOM under
//      the production posture (the deliberate scripted DROP TABLE is Phase 3).
//   3. move-attribute — Customer.CreatedOn relocated to an existing 1:1
//      CustomerProfile satellite. A move is COPY-THEN-DROP, never a rename. Prove
//      the join is 1:1, add the destination column (strict Ok), backfill 1:1
//      (values digest-identical), then the drop of the source column
//      Customer.CreatedOn is BLOCKED single-phase (the guarded later phase).
//
// Every fact is self-contained and order-independent: it restores every table to
// the canonical baseline, removes any stray added file, DROPS the twin database,
// and reconverges before applying its own edit — the database drop is what
// reverts a prior fact's applied strict edit (a plain re-mint's schema
// fingerprint still matches the baseline on disk). Any file a fact adds is
// retracted at the fact's end so it never bleeds. Evidence is flushed to scratch
// BEFORE the assertions so a surprising outcome is preserved as a finding.
// ---------------------------------------------------------------------------

/// Its own container + port, isolated from every other Twin fixture.
type SamplePrStructuralFixture () =
    inherit TwinEstateFixture ("twin-e2e-struct", 21843)

[<Collection("Twin-Docker")>]
type SamplePrStructuralTests (fixture: SamplePrStructuralFixture) =

    let scratch =
        "/tmp/claude-0/-home-user-outsystems-ddl-exporter/afabc5b4-cbd5-575f-a8f0-384d4ca8dfa2/scratchpad"

    // ---- the per-op schema shapes (baseline +/- one thing) ----------------

    // split-table: a new CustomerContact holding Email, 1:1 by CustomerId (the
    // UNIQUE makes the split genuinely 1:1). Its own one-statement estate file.
    let customerContactRel = "Tables/dbo.CustomerContact.sql"
    let customerContactCreate =
        """CREATE TABLE [dbo].[CustomerContact] (
    [Id]         INT           IDENTITY(1,1) NOT NULL,
    [CustomerId] INT           NOT NULL,
    [Email]      NVARCHAR(250) NOT NULL,
    CONSTRAINT [PK_CustomerContact] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_CustomerContact_Customer] UNIQUE ([CustomerId]),
    CONSTRAINT [FK_CustomerContact_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id])
);
"""

    // split-table Phase 3: Customer with the source column [Email] removed.
    let customerNoEmail =
        """CREATE TABLE [dbo].[Customer] (
    [Id]        INT            IDENTITY(1,1) NOT NULL,
    [Name]      NVARCHAR(100)  NOT NULL,
    [StatusId]  INT            NOT NULL,
    [CreatedOn] DATETIME2      NOT NULL,
    CONSTRAINT [PK_Customer] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Customer_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status] ([Id])
);
"""

    // merge-tables: a 1:1 CustomerAddress satellite (NO unique on CustomerId —
    // cardinality is a DATA property this op must PROVE, not a schema guarantee).
    let customerAddressRel = "Tables/dbo.CustomerAddress.sql"
    let customerAddressCreate =
        """CREATE TABLE [dbo].[CustomerAddress] (
    [Id]         INT           IDENTITY(1,1) NOT NULL,
    [CustomerId] INT           NOT NULL,
    [PostalCode] NVARCHAR(20)  NOT NULL,
    CONSTRAINT [PK_CustomerAddress] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_CustomerAddress_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id])
);
"""

    // merge-tables: the survivor Customer gains the absorbing column PostalCode
    // (nullable — added to a populated table, so it publishes clean).
    let customerWithPostalCode =
        """CREATE TABLE [dbo].[Customer] (
    [Id]         INT            IDENTITY(1,1) NOT NULL,
    [Name]       NVARCHAR(100)  NOT NULL,
    [Email]      NVARCHAR(250)  NOT NULL,
    [StatusId]   INT            NOT NULL,
    [CreatedOn]  DATETIME2      NOT NULL,
    [PostalCode] NVARCHAR(20)   NULL,
    CONSTRAINT [PK_Customer] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Customer_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status] ([Id])
);
"""

    // move-attribute: an EXISTING 1:1 CustomerProfile satellite (its own
    // DisplayName data), with a UNIQUE making the relationship genuinely 1:1.
    let customerProfileRel = "Tables/dbo.CustomerProfile.sql"
    let customerProfileCreate =
        """CREATE TABLE [dbo].[CustomerProfile] (
    [Id]          INT           IDENTITY(1,1) NOT NULL,
    [CustomerId]  INT           NOT NULL,
    [DisplayName] NVARCHAR(100) NOT NULL,
    CONSTRAINT [PK_CustomerProfile] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_CustomerProfile_Customer] UNIQUE ([CustomerId]),
    CONSTRAINT [FK_CustomerProfile_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id])
);
"""

    // move-attribute: the same profile after the destination column [CreatedOn]
    // is added (nullable — the additive half of the move).
    let customerProfileWithCreatedOn =
        """CREATE TABLE [dbo].[CustomerProfile] (
    [Id]          INT           IDENTITY(1,1) NOT NULL,
    [CustomerId]  INT           NOT NULL,
    [DisplayName] NVARCHAR(100) NOT NULL,
    [CreatedOn]   DATETIME2     NULL,
    CONSTRAINT [PK_CustomerProfile] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_CustomerProfile_Customer] UNIQUE ([CustomerId]),
    CONSTRAINT [FK_CustomerProfile_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id])
);
"""

    // move-attribute Phase 3: Customer with the source column [CreatedOn] removed.
    let customerNoCreatedOn =
        """CREATE TABLE [dbo].[Customer] (
    [Id]        INT            IDENTITY(1,1) NOT NULL,
    [Name]      NVARCHAR(100)  NOT NULL,
    [Email]     NVARCHAR(250)  NOT NULL,
    [StatusId]  INT            NOT NULL,
    CONSTRAINT [PK_Customer] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Customer_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status] ([Id])
);
"""

    /// An order-sensitive content-hash of a projection: SHA2_256 over the ORDERED
    /// FOR XML RAW rows. A byte-identical hex string proves the content is
    /// byte-identical; any changed/lost/added row shifts it. (Copied verbatim
    /// from the idempotent-seed exemplar's hash idiom.)
    let hashSql (table: string) (cols: string) (orderBy: string) : string =
        sprintf
            "SELECT ISNULL(CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', CAST((SELECT %s FROM %s ORDER BY %s FOR XML RAW) AS NVARCHAR(MAX))), 2), 'EMPTY');"
            cols table orderBy

    interface IClassFixture<SamplePrStructuralFixture>

    // ---- helpers (mirror the reference-integrity / clean-apply exemplars) --

    member private _.Up () : Task<Result<Runs.UpOutcome>> =
        Runs.up fixture.Root fixture.Config TwinConfig.BaselineScenario false

    member private _.Scalar (sql: string) : Task<int64> =
        SamplePrSql.scalar fixture.TwinConnectionString sql

    member private _.ScalarStr (sql: string) : Task<string> =
        SamplePrSql.scalarString fixture.TwinConnectionString sql

    member private _.Exec (sql: string) : Task<int> =
        SamplePrSql.exec fixture.TwinConnectionString sql

    member private _.Detail (es: ValidationError list) : string =
        SamplePrSql.detail es

    /// Converge (relaxed Runs.up) and demand a materialization.
    member private this.Converge (label: string) : Task<Runs.MaterializeReport> =
        task {
            let! outcome = this.Up()
            match outcome with
            | Ok (Runs.Materialized r) -> return r
            | Ok (Runs.NothingToApply _) -> return failwithf "%s: expected Materialized, got NothingToApply" label
            | Error es -> return failwithf "%s: up refused: %A" label (es |> List.map (fun e -> e.Code, e.Message))
        }

    /// Delete an estate file if present (retract an added table file).
    member private _.DeleteTableFile (rel: string) : unit =
        try System.IO.File.Delete(System.IO.Path.Combine(fixture.Root, rel.Replace('/', System.IO.Path.DirectorySeparatorChar)))
        with _ -> ()

    /// Restore every table to baseline, remove any stray added file, drop the
    /// twin database, reconverge — the per-fact isolation primitive that makes
    /// the class order-independent. The database drop is what reverts a prior
    /// fact's applied strict edit (a plain re-mint's schema fingerprint still
    /// matches the baseline on disk).
    member private this.Fresh (label: string) : Task<Runs.MaterializeReport> =
        task {
            let keep = set [ "dbo.Status.sql"; "dbo.Customer.sql"; "dbo.Order.sql"; "dbo.OrderLine.sql" ]
            let tablesDir = System.IO.Path.Combine(fixture.Root, "Tables")
            if System.IO.Directory.Exists tablesDir then
                for file in System.IO.Directory.GetFiles(tablesDir, "*.sql") do
                    let name = System.IO.Path.GetFileName file |> Option.ofObj |> Option.defaultValue ""
                    if not (keep.Contains name) then
                        try System.IO.File.Delete file with _ -> ()
            for f in SamplePrBaseline.files do fixture.Rewrite (fst f) (snd f)
            do! SamplePrBaseline.dropTwinDatabase fixture.Config
            return! this.Converge label
        }

    // =====================================================================
    // 1) split-table (phase 1) — Customer -> Customer + a new CustomerContact
    //    holding Email, linked 1:1 by CustomerId. The additive CREATE lands
    //    under strict; the copy carries all 25 Emails 1:1 (0 lost, 0 duplicated)
    //    with a byte-identical digest and a trusted FK. Then the drop of the
    //    source column Customer.Email is BLOCKED single-phase — the guarded
    //    later phase (Phase 3).
    // =====================================================================
    [<Fact>]
    member this.``split-table (phase 1): the new CustomerContact is created, every Email copies 1:1 with a matching digest and trusted FK, and the source-column drop is the guarded later phase`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-split-table.txt"), evidence.ToString()) with _ -> ()

            let ccExistsSql = "SELECT COUNT(*) FROM sys.tables WHERE name = N'CustomerContact';"
            let ccRowsSql = "SELECT ISNULL((SELECT SUM(row_count) FROM sys.dm_db_partition_stats WHERE object_id = OBJECT_ID(N'dbo.CustomerContact') AND index_id IN (0,1)), -1);"
            let customerRowsSql = "SELECT COUNT(*) FROM [dbo].[Customer];"
            let ccDistinctParentsSql = "SELECT COUNT(*) FROM (SELECT DISTINCT [CustomerId] FROM [dbo].[CustomerContact]) d;"
            // cardinality: customers whose Email did NOT arrive (a lost row), and
            // contacts sharing a CustomerId (a duplicated row).
            let lostSql = "SELECT COUNT(*) FROM [dbo].[Customer] c LEFT JOIN [dbo].[CustomerContact] cc ON cc.[CustomerId] = c.[Id] WHERE cc.[CustomerId] IS NULL;"
            let dupSql = "SELECT ISNULL((SELECT COUNT(*) FROM (SELECT [CustomerId] FROM [dbo].[CustomerContact] GROUP BY [CustomerId] HAVING COUNT(*) > 1) d), 0);"
            let fkTrustedSql = "SELECT ISNULL((SELECT CAST(is_not_trusted AS BIGINT) FROM sys.foreign_keys WHERE name = N'FK_CustomerContact_Customer'), -1);"
            let emailColSql = "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Customer') AND name = N'Email';"
            // the moving column, hashed source vs. new table under a shared alias.
            let sourceHash = hashSql "[dbo].[Customer]" "[Id] AS [Cid],[Email]" "[Id]"
            let destHash = hashSql "[dbo].[CustomerContact]" "[CustomerId] AS [Cid],[Email]" "[CustomerId]"

            // BEFORE — baseline; CustomerContact does not exist; Email populated.
            let! _ = this.Fresh "split-table/fresh"
            let! customerRows = this.Scalar customerRowsSql
            let! ccBefore = this.Scalar ccExistsSql
            let! sourceHashBefore = this.ScalarStr sourceHash
            Assert.True(customerRows > 0L, "Customer must hold rows for the proof")
            Assert.Equal(0L, ccBefore)          // the new entity does not exist yet
            record (sprintf "baseline: Customer rows=%d (each with Email populated), CustomerContact exists=%d (absent), source Email digest=%s" customerRows ccBefore sourceHashBefore)

            // PHASE 1a (additive) — create CustomerContact under the production
            // publish. A CREATE has no data condition to violate -> strict Ok.
            fixture.Rewrite customerContactRel customerContactCreate
            let! strictCreate = SamplePrPublish.strict fixture.Root fixture.Config
            let createOutcome =
                match strictCreate with
                | Ok () -> "APPLIED (Ok)"
                | Error es -> sprintf "REFUSED: %s" (this.Detail es)
            let! ccAfterCreate = this.Scalar ccExistsSql
            let! ccRowsEmpty = this.Scalar ccRowsSql
            record (sprintf "phase 1a additive: production publish (BlockOnPossibleDataLoss=true) CREATE TABLE [dbo].[CustomerContact] (Id/CustomerId FK->Customer + Email, 1:1 UNIQUE): %s" createOutcome)
            record (sprintf "  after create: CustomerContact exists=%d, rows=%d (created empty; strict publish does not mint)" ccAfterCreate ccRowsEmpty)

            // PHASE 1b (copy) — the post-deploy INSERT..SELECT SSDT will never do.
            let! copied = this.Exec "INSERT INTO [dbo].[CustomerContact] ([CustomerId],[Email]) SELECT [Id],[Email] FROM [dbo].[Customer];"
            let! ccRows = this.Scalar ccRowsSql
            let! distinctParents = this.Scalar ccDistinctParentsSql
            let! lost = this.Scalar lostSql
            let! dup = this.Scalar dupSql
            let! fkTrusted = this.Scalar fkTrustedSql
            let! destHashAfter = this.ScalarStr destHash
            let! sourceHashAfter = this.ScalarStr sourceHash
            record (sprintf "phase 1b copy: INSERT..SELECT every Customer.Email into CustomerContact -> rows copied=%d" copied)
            record (sprintf "  cardinality 1:1: CustomerContact rows=%d, distinct CustomerId=%d, Customers with no contact (lost)=%d, contacts sharing a CustomerId (duplicated)=%d" ccRows distinctParents lost dup)
            record (sprintf "  fidelity: FK_CustomerContact_Customer is_not_trusted=%d (0 = trusted); source Email digest=%s, new-table Email digest=%s (match=%b)" fkTrusted sourceHashAfter destHashAfter (sourceHashAfter = destHashAfter))
            flush ()

            match strictCreate with
            | Ok () -> ()
            | Error es -> failwithf "split-table: the additive CREATE was unexpectedly REFUSED: %s" (this.Detail es)
            Assert.Equal(1L, ccAfterCreate)             // the new entity exists
            Assert.Equal(0L, ccRowsEmpty)               // created empty (no mint)
            Assert.Equal(customerRows, int64 copied)    // every Customer row copied
            Assert.Equal(customerRows, ccRows)          // 25 -> 25 rows in the new table
            Assert.Equal(customerRows, distinctParents) // one contact per customer
            Assert.Equal(0L, lost)                      // no Email left behind
            Assert.Equal(0L, dup)                       // no customer copied twice
            Assert.Equal(0L, fkTrusted)                 // the FK landed trusted
            Assert.Equal<string>(sourceHashAfter, destHashAfter)  // Email digest byte-identical
            Assert.NotEqual<string>("EMPTY", destHashAfter)       // and not vacuously empty

            // PHASE 3 (subtractive, the GUARDED later phase) — drop the source
            // column Customer.Email. It is populated on 25 rows, so the
            // production publish BLOCKS on the data-loss / row-presence guard.
            fixture.Rewrite "Tables/dbo.Customer.sql" customerNoEmail
            let! strictDrop = SamplePrPublish.strict fixture.Root fixture.Config
            let dropRefused = match strictDrop with | Ok () -> false | Error _ -> true
            let dropDetail = match strictDrop with | Ok () -> "" | Error es -> this.Detail es
            let! emailColAfter = this.Scalar emailColSql
            let! customerRowsAfter = this.Scalar customerRowsSql
            record (sprintf "phase 3 subtractive (the guarded later phase): production publish DROP the source column [dbo].[Customer].[Email]: %s" (if dropRefused then "REFUSED (blocked)" else "APPLIED (unexpected)"))
            record (sprintf "  after the block: Customer.[Email] column exists=%d (1 = survived the block), Customer rows=%d (intact)" emailColAfter customerRowsAfter)
            record "  strict detail:"
            record dropDetail
            flush ()

            this.DeleteTableFile customerContactRel                 // retract the added file
            fixture.Rewrite "Tables/dbo.Customer.sql" SamplePrBaseline.customer

            Assert.True(dropRefused, "dropping the populated source column must be blocked single-phase")
            Assert.Contains("twin.publish.failed", (match strictDrop with Error es -> es |> List.map (fun e -> e.Code) | _ -> []))
            Assert.Contains("data loss", dropDetail.ToLowerInvariant())   // the DacFx data-loss guard
            Assert.Contains("Email", dropDetail)                          // it names the source column
            Assert.Contains("Rows were detected", dropDetail)             // the row-presence guard fired
            Assert.Equal(1L, emailColAfter)             // the source column survived the block
            Assert.Equal(customerRows, customerRowsAfter)  // no row touched
        }

    // =====================================================================
    // 2) merge-tables (phase 1) — a 1:1 CustomerAddress satellite folded back
    //    into Customer (which gains PostalCode). THE CRITICAL LESSON: prove
    //    cardinality is 1:1 (absorbed rows == distinct parents) BEFORE the copy.
    //    The copy preserves every row/value (0 NULL, digest match). A 1:many
    //    counterexample fires the cardinality probe and shows the naive copy
    //    silently drops a row a value-hash would never flag. The absorbed-table
    //    drop is a PHANTOM under the production posture (the deliberate scripted
    //    DROP TABLE is Phase 3).
    // =====================================================================
    [<Fact>]
    member this.``merge-tables (phase 1): cardinality is proven 1:1 before the copy, the copy preserves every row and value, a 1:many merge is caught before it silently drops rows, and the absorbed-table drop is the later phase`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-merge-tables.txt"), evidence.ToString()) with _ -> ()

            let caExistsSql = "SELECT COUNT(*) FROM sys.tables WHERE name = N'CustomerAddress';"
            let caRowsSql = "SELECT ISNULL((SELECT SUM(row_count) FROM sys.dm_db_partition_stats WHERE object_id = OBJECT_ID(N'dbo.CustomerAddress') AND index_id IN (0,1)), -1);"
            let customerRowsSql = "SELECT COUNT(*) FROM [dbo].[Customer];"
            let absorbedRowsSql = "SELECT COUNT(*) FROM [dbo].[CustomerAddress];"
            let distinctParentsSql = "SELECT COUNT(DISTINCT [CustomerId]) FROM [dbo].[CustomerAddress];"
            // the cardinality probe: parents carrying MORE THAN ONE child row.
            let manyParentsSql = "SELECT ISNULL((SELECT COUNT(*) FROM (SELECT [CustomerId] FROM [dbo].[CustomerAddress] GROUP BY [CustomerId] HAVING COUNT(*) > 1) d), 0);"
            let nullPostalSql = "SELECT COUNT(*) FROM [dbo].[Customer] WHERE [PostalCode] IS NULL;"
            let postalColSql = "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Customer') AND name = N'PostalCode';"
            let absorbedHash = hashSql "[dbo].[CustomerAddress]" "[CustomerId] AS [Cid],[PostalCode]" "[CustomerId]"
            let survivorHash = hashSql "[dbo].[Customer]" "[Id] AS [Cid],[PostalCode]" "[Id]"

            // BEFORE — baseline; CustomerAddress does not exist.
            let! _ = this.Fresh "merge-tables/fresh"
            let! customerRows = this.Scalar customerRowsSql
            let! caBefore = this.Scalar caExistsSql
            Assert.True(customerRows > 0L, "Customer must hold rows for the proof")
            Assert.Equal(0L, caBefore)
            record (sprintf "baseline: Customer rows=%d, CustomerAddress exists=%d (absent)" customerRows caBefore)

            // Setup — create the absorbed 1:1 satellite (strict Ok, additive) and
            // seed it 1:1: one address per Customer.
            fixture.Rewrite customerAddressRel customerAddressCreate
            let! strictSat = SamplePrPublish.strict fixture.Root fixture.Config
            let satOutcome = match strictSat with | Ok () -> "APPLIED (Ok)" | Error es -> sprintf "REFUSED: %s" (this.Detail es)
            let! caAfter = this.Scalar caExistsSql
            let! seeded = this.Exec "INSERT INTO [dbo].[CustomerAddress] ([CustomerId],[PostalCode]) SELECT [Id], RIGHT(N'00000' + CAST([Id] AS NVARCHAR(5)), 5) FROM [dbo].[Customer];"
            record (sprintf "setup: production publish CREATE TABLE [dbo].[CustomerAddress] (1:1 satellite): %s; seeded %d addresses (one per Customer)" satOutcome seeded)

            // PHASE 1 — CARDINALITY FIRST, before anything is copied. absorbed
            // rows == distinct parents == 1:1; unequal would mean silent loss.
            let! absorbedRows = this.Scalar absorbedRowsSql
            let! distinctParents = this.Scalar distinctParentsSql
            let! manyParents = this.Scalar manyParentsSql
            record (sprintf "phase 1 CARDINALITY (before any copy): absorbed rows=%d, distinct parents=%d, parents with >1 child=%d (equal + zero = 1:1, the copy is safe)" absorbedRows distinctParents manyParents)

            // PHASE 1 — add the absorbing column to the survivor (strict Ok,
            // nullable over a populated table) and copy the values across.
            fixture.Rewrite "Tables/dbo.Customer.sql" customerWithPostalCode
            let! strictCol = SamplePrPublish.strict fixture.Root fixture.Config
            let colOutcome = match strictCol with | Ok () -> "APPLIED (Ok)" | Error es -> sprintf "REFUSED: %s" (this.Detail es)
            let! postalCol = this.Scalar postalColSql
            let! copied = this.Exec "UPDATE c SET c.[PostalCode] = a.[PostalCode] FROM [dbo].[Customer] c JOIN [dbo].[CustomerAddress] a ON a.[CustomerId] = c.[Id];"
            let! nullPostal = this.Scalar nullPostalSql
            let! absorbedHashV = this.ScalarStr absorbedHash
            let! survivorHashV = this.ScalarStr survivorHash
            record (sprintf "phase 1 additive+copy: production publish ADD [dbo].[Customer].[PostalCode] (nullable): %s; PostalCode column exists=%d; copied %d rows from the absorbed table" colOutcome postalCol copied)
            record (sprintf "  fidelity: Customers with NULL PostalCode after the copy=%d (0 = every survivor row filled); absorbed digest=%s, survivor digest=%s (match=%b)" nullPostal absorbedHashV survivorHashV (absorbedHashV = survivorHashV))
            flush ()

            match strictSat with | Ok () -> () | Error es -> failwithf "merge-tables: satellite CREATE unexpectedly REFUSED: %s" (this.Detail es)
            match strictCol with | Ok () -> () | Error es -> failwithf "merge-tables: absorbing-column ADD unexpectedly REFUSED: %s" (this.Detail es)
            Assert.Equal(1L, caAfter)
            Assert.Equal(customerRows, int64 seeded)
            Assert.Equal(absorbedRows, distinctParents)     // the load-bearing 1:1 proof
            Assert.Equal(customerRows, absorbedRows)        // exactly one address per customer
            Assert.Equal(0L, manyParents)                   // no parent carries two children
            Assert.Equal(1L, postalCol)                     // the absorbing column landed
            Assert.Equal(customerRows, int64 copied)        // every row copied
            Assert.Equal(0L, nullPostal)                    // no survivor row left unfilled
            Assert.Equal<string>(absorbedHashV, survivorHashV)  // every value byte-identical
            Assert.NotEqual<string>("EMPTY", survivorHashV)

            // THE 1:MANY COUNTEREXAMPLE — insert a SECOND address for the smallest
            // Customer with a distinct PostalCode. The cardinality probe catches
            // it (absorbed != distinct); a naive copy then silently keeps one row
            // per parent and drops the extra — a value-hash would never flag it.
            let! _ = this.Exec "INSERT INTO [dbo].[CustomerAddress] ([CustomerId],[PostalCode]) SELECT MIN([Id]), N'ZZZZZ' FROM [dbo].[Customer];"
            let! absorbedRows2 = this.Scalar absorbedRowsSql
            let! distinctParents2 = this.Scalar distinctParentsSql
            let! manyParents2 = this.Scalar manyParentsSql
            let! _ = this.Exec "UPDATE [dbo].[Customer] SET [PostalCode] = NULL;"
            let! naiveCopied = this.Exec "UPDATE c SET c.[PostalCode] = a.[PostalCode] FROM [dbo].[Customer] c JOIN [dbo].[CustomerAddress] a ON a.[CustomerId] = c.[Id];"
            record (sprintf "the 1:many counterexample: a 2nd address is added for one Customer -> absorbed rows=%d, distinct parents=%d, parents with >1 child=%d (UNEQUAL -> the cardinality probe CATCHES it; STOP)" absorbedRows2 distinctParents2 manyParents2)
            record (sprintf "  the silent loss it prevents: the naive copy touched %d rows (one per parent), NOT the %d absorbed rows -> %d absorbed value(s) never reached the survivor, and a value-hash over the %d survivors would still look complete" naiveCopied absorbedRows2 (absorbedRows2 - int64 naiveCopied) naiveCopied)
            flush ()

            Assert.NotEqual(absorbedRows2, distinctParents2)  // the probe catches the 1:many
            Assert.Equal(customerRows + 1L, absorbedRows2)    // 26 absorbed rows
            Assert.Equal(customerRows, distinctParents2)      // but only 25 distinct parents
            Assert.Equal(1L, manyParents2)                    // exactly one over-loaded parent
            Assert.Equal(customerRows, int64 naiveCopied)     // the naive copy kept 25, silently dropping 1

            // PHASE 3 (subtractive) — the absorbed-table drop is a PHANTOM under
            // the production posture (DropObjectsNotInSource = false): removing it
            // from the project leaves it in place. The real Phase-3 removal is a
            // deliberate, reviewed scripted DROP TABLE.
            this.DeleteTableFile customerAddressRel
            let! strictPhantom = SamplePrPublish.strict fixture.Root fixture.Config
            let phantomOutcome = match strictPhantom with | Ok () -> "APPLIED (Ok)" | Error es -> sprintf "REFUSED: %s" (this.Detail es)
            let! caAfterPhantom = this.Scalar caExistsSql
            let! caRowsAfterPhantom = this.Scalar caRowsSql
            let! _ = this.Exec "DROP TABLE [dbo].[CustomerAddress];"
            let! caAfterScript = this.Scalar caExistsSql
            record (sprintf "phase 3 subtractive (the later phase): removing CustomerAddress from the project under the production publish: %s -> CustomerAddress exists=%d rows=%d (a PHANTOM — the declarative drop leaves it in place)" phantomOutcome caAfterPhantom caRowsAfterPhantom)
            record (sprintf "  the deliberate Phase-3 act: scripted DROP TABLE [dbo].[CustomerAddress] -> exists=%d (gone; the absorbed rows go with it — irreversible without a backup)" caAfterScript)
            flush ()

            fixture.Rewrite "Tables/dbo.Customer.sql" SamplePrBaseline.customer   // restore so it cannot bleed

            match strictPhantom with | Ok () -> () | Error es -> failwithf "merge-tables: the phantom-removal publish unexpectedly REFUSED: %s" (this.Detail es)
            Assert.Equal(1L, caAfterPhantom)     // the declarative removal is a phantom — it survives
            Assert.Equal(0L, caAfterScript)      // only the scripted DROP TABLE actually removes it
        }

    // =====================================================================
    // 3) move-attribute (phase 1) — Customer.CreatedOn relocated to an existing
    //    1:1 CustomerProfile satellite. A move is COPY-THEN-DROP, never a rename.
    //    Prove the join is 1:1, add the destination column (strict Ok), backfill
    //    1:1 (values digest-identical), then the drop of the source column
    //    Customer.CreatedOn is BLOCKED single-phase (the guarded later phase).
    // =====================================================================
    [<Fact>]
    member this.``move-attribute (phase 1): the join is proven 1:1, the destination column is backfilled with a matching digest, and the source-column drop is the guarded later phase`` () : Task =
        task {
            let evidence = System.Text.StringBuilder()
            let record (s: string) = evidence.AppendLine s |> ignore
            let flush () =
                try System.IO.File.WriteAllText(System.IO.Path.Combine(scratch, "evidence-move-attribute.txt"), evidence.ToString()) with _ -> ()

            let cpExistsSql = "SELECT COUNT(*) FROM sys.tables WHERE name = N'CustomerProfile';"
            let customerRowsSql = "SELECT COUNT(*) FROM [dbo].[Customer];"
            let cpRowsSql = "SELECT COUNT(*) FROM [dbo].[CustomerProfile];"
            // 1:1 join proof: parents carrying more than one profile row.
            let manyChildrenSql = "SELECT ISNULL((SELECT COUNT(*) FROM (SELECT [CustomerId] FROM [dbo].[CustomerProfile] GROUP BY [CustomerId] HAVING COUNT(*) > 1) d), 0);"
            let unmatchedSql = "SELECT COUNT(*) FROM [dbo].[Customer] c LEFT JOIN [dbo].[CustomerProfile] p ON p.[CustomerId] = c.[Id] WHERE p.[CustomerId] IS NULL;"
            let createdOnColSql = "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Customer') AND name = N'CreatedOn';"
            let profileCreatedOnColSql = "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.CustomerProfile') AND name = N'CreatedOn';"
            let nullDestSql = "SELECT COUNT(*) FROM [dbo].[CustomerProfile] WHERE [CreatedOn] IS NULL;"
            // the moving value, hashed source (Customer) vs. destination (profile).
            let sourceHash = hashSql "[dbo].[Customer]" "[Id] AS [Cid],[CreatedOn]" "[Id]"
            let destHash = hashSql "[dbo].[CustomerProfile]" "[CustomerId] AS [Cid],[CreatedOn]" "[CustomerId]"

            // BEFORE — baseline; the destination satellite does not exist yet.
            let! _ = this.Fresh "move-attribute/fresh"
            let! customerRows = this.Scalar customerRowsSql
            let! cpBefore = this.Scalar cpExistsSql
            let! createdOnBefore = this.Scalar createdOnColSql
            Assert.True(customerRows > 0L, "Customer must hold rows for the proof")
            Assert.Equal(0L, cpBefore)
            Assert.Equal(1L, createdOnBefore)   // CreatedOn starts on Customer
            record (sprintf "baseline: Customer rows=%d, source column Customer.[CreatedOn] exists=%d, CustomerProfile exists=%d (absent)" customerRows createdOnBefore cpBefore)

            // Setup — the EXISTING 1:1 destination satellite (its own DisplayName
            // data), created + seeded 1:1 (one profile per Customer).
            fixture.Rewrite customerProfileRel customerProfileCreate
            let! strictSat = SamplePrPublish.strict fixture.Root fixture.Config
            let satOutcome = match strictSat with | Ok () -> "APPLIED (Ok)" | Error es -> sprintf "REFUSED: %s" (this.Detail es)
            let! cpAfter = this.Scalar cpExistsSql
            let! seeded = this.Exec "INSERT INTO [dbo].[CustomerProfile] ([CustomerId],[DisplayName]) SELECT [Id],[Name] FROM [dbo].[Customer];"
            let! cpRows = this.Scalar cpRowsSql
            record (sprintf "setup: production publish CREATE TABLE [dbo].[CustomerProfile] (existing 1:1 satellite): %s; seeded %d profiles, rows=%d" satOutcome seeded cpRows)

            // PHASE 1 — prove the join is 1:1 BEFORE copying (no moved value is
            // ambiguous), then ADD the destination column (strict Ok) and backfill.
            let! manyChildren = this.Scalar manyChildrenSql
            let! unmatched = this.Scalar unmatchedSql
            record (sprintf "phase 1 relationship: Customer->CustomerProfile parents with >1 child=%d, Customers with no profile=%d (both zero + rows equal = 1:1; no moved value is ambiguous)" manyChildren unmatched)

            fixture.Rewrite customerProfileRel customerProfileWithCreatedOn
            let! strictCol = SamplePrPublish.strict fixture.Root fixture.Config
            let colOutcome = match strictCol with | Ok () -> "APPLIED (Ok)" | Error es -> sprintf "REFUSED: %s" (this.Detail es)
            let! destCol = this.Scalar profileCreatedOnColSql
            let! backfilled = this.Exec "UPDATE p SET p.[CreatedOn] = c.[CreatedOn] FROM [dbo].[CustomerProfile] p JOIN [dbo].[Customer] c ON c.[Id] = p.[CustomerId];"
            let! nullDest = this.Scalar nullDestSql
            let! sourceHashV = this.ScalarStr sourceHash
            let! destHashV = this.ScalarStr destHash
            record (sprintf "phase 1 additive+copy: production publish ADD [dbo].[CustomerProfile].[CreatedOn] (nullable): %s; destination column exists=%d; backfilled %d rows keyed by the 1:1 relationship" colOutcome destCol backfilled)
            record (sprintf "  fidelity: destination rows with NULL CreatedOn=%d (0 = every value moved); source digest=%s, destination digest=%s (match=%b)" nullDest sourceHashV destHashV (sourceHashV = destHashV))
            flush ()

            match strictSat with | Ok () -> () | Error es -> failwithf "move-attribute: satellite CREATE unexpectedly REFUSED: %s" (this.Detail es)
            match strictCol with | Ok () -> () | Error es -> failwithf "move-attribute: destination-column ADD unexpectedly REFUSED: %s" (this.Detail es)
            Assert.Equal(1L, cpAfter)
            Assert.Equal(customerRows, int64 seeded)
            Assert.Equal(customerRows, cpRows)
            Assert.Equal(0L, manyChildren)              // the 1:1 relationship proof
            Assert.Equal(0L, unmatched)                 // every customer maps to a profile
            Assert.Equal(1L, destCol)                   // the destination column landed
            Assert.Equal(customerRows, int64 backfilled)// every value copied across
            Assert.Equal(0L, nullDest)                  // none left behind
            Assert.Equal<string>(sourceHashV, destHashV)   // every moved value byte-identical
            Assert.NotEqual<string>("EMPTY", destHashV)

            // PHASE 3 (subtractive, the GUARDED later phase) — drop the source
            // column Customer.CreatedOn. It is populated on 25 rows, so the
            // production publish BLOCKS on the data-loss / row-presence guard.
            // (A move is copy-then-drop; the source column never merely renames.)
            fixture.Rewrite "Tables/dbo.Customer.sql" customerNoCreatedOn
            let! strictDrop = SamplePrPublish.strict fixture.Root fixture.Config
            let dropRefused = match strictDrop with | Ok () -> false | Error _ -> true
            let dropDetail = match strictDrop with | Ok () -> "" | Error es -> this.Detail es
            let! createdOnAfter = this.Scalar createdOnColSql
            let! customerRowsAfter = this.Scalar customerRowsSql
            record (sprintf "phase 3 subtractive (the guarded later phase): production publish DROP the source column [dbo].[Customer].[CreatedOn]: %s" (if dropRefused then "REFUSED (blocked)" else "APPLIED (unexpected)"))
            record (sprintf "  after the block: Customer.[CreatedOn] column exists=%d (1 = survived the block), Customer rows=%d (intact)" createdOnAfter customerRowsAfter)
            record "  strict detail:"
            record dropDetail
            flush ()

            this.DeleteTableFile customerProfileRel                 // retract the added file
            fixture.Rewrite "Tables/dbo.Customer.sql" SamplePrBaseline.customer

            Assert.True(dropRefused, "dropping the populated source column must be blocked single-phase")
            Assert.Contains("twin.publish.failed", (match strictDrop with Error es -> es |> List.map (fun e -> e.Code) | _ -> []))
            Assert.Contains("data loss", dropDetail.ToLowerInvariant())   // the DacFx data-loss guard
            Assert.Contains("CreatedOn", dropDetail)                      // it names the source column
            Assert.Contains("Rows were detected", dropDetail)             // the row-presence guard fired
            Assert.Equal(1L, createdOnAfter)            // the source column survived the block
            Assert.Equal(customerRows, customerRowsAfter)  // no row touched
        }
