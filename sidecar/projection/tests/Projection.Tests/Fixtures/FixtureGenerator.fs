namespace Projection.Tests.SourceFixtures

open System
open System.Text

/// Procedural OutSystems-shaped fixture generator. Per session-31
/// operator framing — "what about with 200 entities and 100 static
/// entities" — this lets the canary run against arbitrarily-sized
/// generated fixtures so we can measure how V2 actually scales.
///
/// The generator is deterministic per `Seed`: same spec → same DDL
/// byte-for-byte. Run-to-run reproducibility is essential for
/// bench measurement; a different RNG sample on each run would
/// muddy the timing data.

/// Configurable fixture shape. Distinct from the hand-written
/// fixtures in `SourceSchema.fs` — those are small, stable
/// landmarks; this is for parameterized scaling work.
type GenerateSpec =
    {
        /// Number of OutSystems modules. Entities are distributed
        /// across modules round-robin.
        Modules : int
        /// Total regular (transactional) entities.
        Entities : int
        /// Total static (lookup-table) entities.
        StaticEntities : int
        /// Average attributes per regular entity. The actual count
        /// varies per entity via RNG to exercise the
        /// per-iteration distribution view in the bench surface.
        AvgAttrsPerEntity : int
        /// Probability that a regular entity has an outgoing FK to
        /// another entity emitted earlier in topological order.
        /// 0.0 = no FKs anywhere; 1.0 = every entity has at least
        /// one FK.
        FkDensity : float
        /// Rows per static entity (small lookup data).
        StaticRowsPerEntity : int
        /// Deterministic seed. Same seed → same DDL.
        Seed : int
    }

/// Generated artifact: schema DDL + static-seed data SQL.
type GeneratedFixture =
    {
        /// CREATE TABLE statements with FK constraints.
        Ddl : string
        /// INSERT statements for static entities.
        SeedData : string
        /// Total tables in the generated fixture.
        TableCount : int
    }

[<RequireQualifiedAccess>]
module GenerateSpec =
    /// Small fixture (~12 tables) — fast smoke test scale; tuned
    /// for sub-2-second canary runs against a warm container.
    let small : GenerateSpec =
        {
            Modules = 2
            Entities = 8
            StaticEntities = 4
            AvgAttrsPerEntity = 6
            FkDensity = 0.3
            StaticRowsPerEntity = 5
            Seed = 42
        }

    /// Medium fixture (~75 tables) — exercises modest scale; the
    /// bench surface starts to show emit / readside scaling.
    let medium : GenerateSpec =
        {
            Modules = 4
            Entities = 50
            StaticEntities = 25
            AvgAttrsPerEntity = 8
            FkDensity = 0.25
            StaticRowsPerEntity = 10
            Seed = 42
        }

    /// Realistic fixture (~300 tables: 200 regular + 100 static).
    /// The forcing function from VISION.md — a 300-table OutSystems
    /// 11 system facing an External Entities cutover.
    let realistic : GenerateSpec =
        {
            Modules = 8
            Entities = 200
            StaticEntities = 100
            AvgAttrsPerEntity = 10
            FkDensity = 0.2
            StaticRowsPerEntity = 20
            Seed = 42
        }

[<RequireQualifiedAccess>]
module FixtureGenerator =

    let private moduleCode (i: int) : string =
        sprintf "M%02d" (i + 1)

    let private entityName (rng: Random) (idx: int) : string =
        let nouns =
            [|
                "User"; "Customer"; "Order"; "Product"; "Invoice"; "Payment"
                "Account"; "Address"; "Contact"; "Lead"; "Opportunity"; "Quote"
                "Subscription"; "Plan"; "Tier"; "Discount"; "Coupon"; "Voucher"
                "Notification"; "Message"; "Comment"; "Reaction"; "Tag"; "Category"
                "Inventory"; "Warehouse"; "Shipment"; "Delivery"; "Return"; "Refund"
                "Project"; "Task"; "Sprint"; "Backlog"; "Ticket"; "Board"
                "Document"; "File"; "Folder"; "Link"; "Bookmark"; "Share"
                "Event"; "Booking"; "Reservation"; "Schedule"; "Calendar"; "Reminder"
                "Department"; "Team"; "Role"; "Permission"; "Group"; "Membership"
                "Audit"; "Log"; "Trace"; "Metric"; "Alert"; "Threshold"
            |]
        // Combine a base noun + numeric suffix to disambiguate at
        // higher entity counts. Deterministic per index.
        let baseNoun = nouns[idx % nouns.Length]
        let suffix = idx / nouns.Length
        if suffix = 0 then baseNoun
        else sprintf "%s%d" baseNoun (suffix + 1)

    let private staticEntityName (idx: int) : string =
        let staticNouns =
            [|
                "Status"; "Country"; "Currency"; "Language"; "Locale"; "TimeZone"
                "OrderStatus"; "PaymentMethod"; "ShippingMethod"; "TaxRate"
                "Priority"; "Severity"; "Category"; "Subcategory"; "Type"
                "ColorCode"; "SizeCode"; "UnitOfMeasure"; "ChannelType"
                "DocumentType"; "FileType"; "MimeType"; "Encoding"; "CharSet"
                "RegionCode"; "Province"; "PostalRegion"; "DialingCode"
                "SkillLevel"; "EducationLevel"; "EmploymentStatus"; "MaritalStatus"
                "IndustryCode"; "JobTitle"; "Department"; "CostCenter"
                "Holiday"; "Season"; "Quarter"; "FiscalPeriod"
            |]
        let baseNoun = staticNouns[idx % staticNouns.Length]
        let suffix = idx / staticNouns.Length
        if suffix = 0 then baseNoun
        else sprintf "%s%d" baseNoun (suffix + 1)

    /// Pick a SQL Server type for a non-PK column. Distribution
    /// approximates real OutSystems schemas: text-heavy with
    /// numeric / temporal sprinkles.
    let private pickColumnType (rng: Random) : string =
        let roll = rng.Next 100
        if roll < 35 then sprintf "NVARCHAR(%d)" ([| 50; 100; 250; 500; 1000 |][rng.Next 5])
        elif roll < 50 then "INT"
        elif roll < 60 then sprintf "DECIMAL(%d, %d)" ([| 18; 20; 38 |][rng.Next 3]) (rng.Next 5)
        elif roll < 70 then "BIT"
        elif roll < 85 then "DATETIME2"
        elif roll < 90 then "DATE"
        elif roll < 95 then "UNIQUEIDENTIFIER"
        else "VARBINARY(MAX)"

    let private columnName (rng: Random) (idx: int) : string =
        let nouns =
            [|
                "name"; "title"; "description"; "code"; "label"; "value"
                "amount"; "quantity"; "rate"; "score"; "rank"; "level"
                "is_active"; "is_visible"; "is_archived"; "is_deleted"
                "started_on"; "completed_on"; "expires_on"; "valid_until"
                "external_ref"; "tracking_id"; "version"; "etag"
                "country"; "region"; "city"; "postcode"
                "phone"; "fax"; "url"; "domain"; "subdomain"
            |]
        let baseNoun = nouns[idx % nouns.Length]
        let suffix = idx / nouns.Length
        if suffix = 0 then baseNoun.ToUpperInvariant()
        else sprintf "%s%d" (baseNoun.ToUpperInvariant()) (suffix + 1)

    /// Generate one table's CREATE TABLE statement plus its FK
    /// ADD CONSTRAINT (if any). The `priorEntities` list contains
    /// the `(physicalName)` tuples of tables emitted earlier in
    /// topological order; FKs target one of these to guarantee
    /// acyclicity.
    let private generateTable
        (rng: Random)
        (spec: GenerateSpec)
        (modCode: string)
        (entityName: string)
        (priorEntities: string list)
        (sb: StringBuilder)
        : unit =
        let tableName = sprintf "OSUSR_%s_%s" modCode (entityName.ToUpperInvariant())
        let attrCount =
            let varied = rng.Next(spec.AvgAttrsPerEntity / 2, spec.AvgAttrsPerEntity * 3 / 2 + 1)
            max 3 varied
        // Audit columns + the entity-specific columns.
        let auditCols =
            [
                "[ID] INT NOT NULL IDENTITY(1,1)"
                "[TENANT_ID] INT NOT NULL"
                "[SS_KEY] UNIQUEIDENTIFIER NOT NULL"
                "[CREATEDON] DATETIME2 NOT NULL"
                "[UPDATEDON] DATETIME2 NOT NULL"
            ]
        // FK column (when this entity has an outgoing FK)
        let fkColumn, fkConstraint =
            if not (List.isEmpty priorEntities) && rng.NextDouble() < spec.FkDensity then
                let target = priorEntities[rng.Next priorEntities.Length]
                let fkColumnName = sprintf "PARENT_%d_ID" (rng.Next 1000)
                let fkColumnDecl = sprintf "[%s] INT NULL" fkColumnName
                let fkName = sprintf "FK_%s_%s" tableName fkColumnName
                let fkClause =
                    sprintf
                        "    CONSTRAINT [%s] FOREIGN KEY ([%s]) REFERENCES [dbo].[%s]([ID])"
                        fkName
                        fkColumnName
                        target
                Some fkColumnDecl, Some fkClause
            else
                None, None
        // Generate non-audit user columns.
        let userCols =
            [
                for i in 0 .. attrCount - 1 ->
                    let col = columnName rng i
                    let typ = pickColumnType rng
                    let nullness = if rng.NextDouble() < 0.4 then "NULL" else "NOT NULL"
                    sprintf "[%s] %s %s" col typ nullness
            ]
        // Compose
        sb.AppendLine(sprintf "CREATE TABLE [dbo].[%s] (" tableName) |> ignore
        let allCols =
            auditCols @ userCols @ (Option.toList fkColumn)
        let lastIdx = allCols.Length - 1
        allCols
        |> List.iteri (fun i col ->
            let needsComma = i < lastIdx || true   // PK constraint always follows
            let sep = if needsComma then "," else ""
            sb.AppendLine(sprintf "    %s%s" col sep) |> ignore)
        // PK
        sb.Append(
            sprintf "    CONSTRAINT [PK_dbo_%s] PRIMARY KEY ([ID])" tableName)
        |> ignore
        match fkConstraint with
        | Some fk ->
            sb.AppendLine(",") |> ignore
            sb.AppendLine(fk) |> ignore
        | None ->
            sb.AppendLine() |> ignore
        sb.AppendLine(");") |> ignore
        sb.AppendLine() |> ignore

    /// Generate one static entity (lookup table) with simpler shape:
    /// no audit columns, no FKs, just (ID, LABEL, SS_KEY).
    let private generateStaticTable
        (modCode: string)
        (entityName: string)
        (sb: StringBuilder)
        : string =
        let tableName = sprintf "OSUSR_%s_%s" modCode (entityName.ToUpperInvariant())
        sb.AppendLine(sprintf "CREATE TABLE [dbo].[%s] (" tableName) |> ignore
        sb.AppendLine("    [ID] INT NOT NULL,") |> ignore
        sb.AppendLine("    [LABEL] NVARCHAR(100) NOT NULL,") |> ignore
        sb.AppendLine("    [CODE] NVARCHAR(50) NOT NULL,") |> ignore
        sb.AppendLine("    [IS_ACTIVE] BIT NOT NULL,") |> ignore
        sb.AppendLine("    [SS_KEY] UNIQUEIDENTIFIER NOT NULL,") |> ignore
        sb.AppendLine(sprintf "    CONSTRAINT [PK_dbo_%s] PRIMARY KEY ([ID])" tableName) |> ignore
        sb.AppendLine(");") |> ignore
        sb.AppendLine() |> ignore
        tableName

    /// Generate INSERT statements for a static entity's seed data.
    let private generateStaticSeed
        (rng: Random)
        (rowsPerEntity: int)
        (tableName: string)
        (sb: StringBuilder)
        : unit =
        for i in 1 .. rowsPerEntity do
            let label = sprintf "%s_LABEL_%d" tableName i
            let code = sprintf "C%05d" i
            let active = if i % 7 = 0 then 0 else 1
            let guid = Guid.NewGuid().ToString "D"
            sb.AppendLine(
                sprintf
                    "INSERT INTO [dbo].[%s] ([ID], [LABEL], [CODE], [IS_ACTIVE], [SS_KEY]) VALUES (%d, '%s', '%s', %d, '%s');"
                    tableName
                    i
                    label
                    code
                    active
                    guid)
            |> ignore

    /// Generate a fixture matching the spec. Deterministic per
    /// `spec.Seed`: same spec → same DDL byte-for-byte.
    let generate (spec: GenerateSpec) : GeneratedFixture =
        // Note: Guid.NewGuid is non-deterministic. For static seed
        // data we tolerate this — the canary's PhysicalSchema
        // comparison doesn't include row data. If a future
        // round-trip data canary requires byte-determinism, swap
        // to a deterministic UUIDv5 per (spec.Seed, table, row).
        let rng = Random(spec.Seed)
        let ddlSb = StringBuilder(64 * 1024)
        let dataSb = StringBuilder(16 * 1024)

        ddlSb.AppendLine(
            sprintf
                "-- Generated by FixtureGenerator: %d modules, %d entities, %d static, seed=%d"
                spec.Modules
                spec.Entities
                spec.StaticEntities
                spec.Seed)
        |> ignore
        ddlSb.AppendLine() |> ignore

        // Generate regular entities in topological order — entity i can
        // FK-reference any entity 0..i-1.
        let mutable priorEntities : string list = []
        let mutable totalTables = 0

        for i in 0 .. spec.Entities - 1 do
            let modIdx = i % spec.Modules
            let modCode = moduleCode modIdx
            let ename = entityName rng i
            let tableName = sprintf "OSUSR_%s_%s" modCode (ename.ToUpperInvariant())
            generateTable rng spec modCode ename priorEntities ddlSb
            priorEntities <- tableName :: priorEntities
            totalTables <- totalTables + 1

        // Static entities — independent of regular entities; emitted
        // after for clarity.
        for i in 0 .. spec.StaticEntities - 1 do
            let modIdx = i % spec.Modules
            let modCode = moduleCode modIdx
            let sname = staticEntityName i
            let tableName = generateStaticTable modCode sname ddlSb
            generateStaticSeed rng spec.StaticRowsPerEntity tableName dataSb
            totalTables <- totalTables + 1

        {
            Ddl = ddlSb.ToString()
            SeedData = dataSb.ToString()
            TableCount = totalTables
        }
