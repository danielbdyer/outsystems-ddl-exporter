namespace Projection.Tests.SourceFixtures

open System
open System.Text
open Projection.Core
open Projection.Targets.SSDT

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

/// One static table's bulk-loadable seed rows. Per session-35 —
/// the bulk fixture loader path uses `Bulk.copyRows` directly
/// (SqlBulkCopy) instead of executing N INSERT statements as text;
/// at 500k-row scale this drops source-load time from ~10 minutes
/// to a handful of seconds. Each row is a `CellValue list`
/// matching the table's schema in column order.
type StaticTableSeed =
    {
        Table : TableId
        Rows : CellValue list list
    }

/// One attribute (column) on a generated entity. Captured here so the
/// comprehensive canary's OSSYS-metadata synthesizer can describe the
/// same schema the DDL declares — single source of truth, byte-for-byte
/// determinism is preserved because the DDL emitter and the structural
/// emitter walk the same RNG sequence in the same order.
/// Slice A.4.7'-prelude.comprehensive-canary (2026-05-19).
type GeneratedAttribute =
    {
        /// Physical column name (uppercase per OutSystems convention).
        PhysicalName : string
        /// SQL Server type (e.g., `NVARCHAR(100)`, `INT`, `BIT`).
        SqlType : string
        /// `true` when the column is NOT NULL.
        IsMandatory : bool
        /// `true` for the entity's `[ID]` primary-key column.
        IsIdentifier : bool
        /// `true` for IDENTITY(1,1) columns (typically the PK).
        IsAutoNumber : bool
        /// When this attribute is a FK, the physical table name it
        /// references (resolved at generation time). `None` for
        /// non-FK columns.
        ReferencedTable : string option
    }

/// One generated entity (regular or static). The OSSYS-metadata
/// synthesizer (`OssysFixtureSynthesizer`) consumes this list to
/// emit `ossys_Espace` / `ossys_Entity` / `ossys_Entity_Attr` rows
/// describing the same schema FixtureGenerator's DDL declares.
type GeneratedEntityModel =
    {
        /// Zero-based module index (round-robin distribution).
        ModuleIndex : int
        /// Module short code (e.g., `M01`).
        ModuleCode : string
        /// Human entity name (e.g., `Customer`, `Status`).
        EntityName : string
        /// Physical SQL table name (e.g., `OSUSR_M01_CUSTOMER`).
        PhysicalTable : string
        /// `true` for static (lookup) entities.
        IsStatic : bool
        /// Column-order list of attributes. Mirrors the DDL.
        Attributes : GeneratedAttribute list
    }

/// Generated artifact: schema DDL + static-seed data, both as text
/// (for the historical executeBatch path) and as typed bulk seeds
/// (for the SqlBulkCopy path).
type GeneratedFixture =
    {
        /// CREATE TABLE statements with FK constraints.
        Ddl : string
        /// INSERT statements for static entities (text form, GO-chunked).
        SeedData : string
        /// `Ddl` + `SeedData` concatenated.
        Combined : string
        /// Per session-35 — typed bulk seeds for SqlBulkCopy. One
        /// entry per static table; rows are `CellValue` lists in
        /// column order. Empty when `StaticEntities = 0`.
        BulkSeeds : StaticTableSeed list
        /// Per slice A.4.7'-prelude.comprehensive-canary — structural
        /// view of every generated entity (regular + static) in
        /// emission order. The OSSYS-metadata synthesizer consumes
        /// this to emit `ossys_*` INSERTs describing the same schema
        /// the DDL declares. Empty when the generator hasn't been
        /// extended for a given path (defensive — defaults to []
        /// so legacy callers don't break).
        Entities : GeneratedEntityModel list
        /// Total tables in the generated fixture.
        TableCount : int
        /// Total static-seed rows (sum across all static entities).
        SeedRowCount : int
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

    /// Bulk-path stress fixtures: few tables, many rows per table.
    /// Per session-34 — exercise the `Deploy.executeStream` /
    /// `Bulk.copyRows` realization at enterprise row volumes. Five
    /// static tables × N rows each isolates the bulk path's
    /// throughput and memory profile from the schema-side scaling
    /// covered by `realistic`.
    let private bulkSpec (rowsPerTable: int) : GenerateSpec =
        {
            Modules = 1
            Entities = 0
            StaticEntities = 5
            AvgAttrsPerEntity = 5
            FkDensity = 0.0
            StaticRowsPerEntity = rowsPerTable
            Seed = 42
        }

    let bulk1k : GenerateSpec = bulkSpec 1_000
    let bulk10k : GenerateSpec = bulkSpec 10_000
    let bulk100k : GenerateSpec = bulkSpec 100_000

    /// **Comprehensive canary fixture** — slice A.4.7'-prelude.
    /// comprehensive-canary (2026-05-19). The pre-perf-sweep baseline
    /// canary; smaller than `operatorReality` because it exercises a
    /// *wider* pipeline (OSSYS adapter extract + parse + Catalog
    /// construction + Policy + tightening passes + topological order
    /// + data emission + dual deploy + diff). Wall-time target <60s
    /// warm; coverage matters more than scale.
    ///
    /// **Shape:** 8 modules × (25 regular + ~12-13 static) = 300 tables;
    /// 5000 rows × 100 static = ~500k static rows ≈ 100MB of seed data.
    /// Matches `operatorReality` topology + 10× the row volume for the
    /// "production scenario" baseline. Variegated via the same RNG-driven
    /// attribute / FK density.
    ///
    /// Per slice A.4.7'-prelude.canary-production-scale (2026-05-19):
    /// the comprehensive canary now exercises bench labels at production
    /// cardinality so per-iteration distribution (P50/P95/P99) surfaces
    /// real timings (vs the prior 100-table scale where most labels
    /// reported sub-millisecond per-iteration means).
    let comprehensiveCanary : GenerateSpec =
        {
            Modules = 8
            Entities = 200
            StaticEntities = 100
            AvgAttrsPerEntity = 10
            FkDensity = 0.2
            StaticRowsPerEntity = 5000
            Seed = 42
        }

    /// **Operator-reality fixture** — 300 tables (200 regular + 100
    /// static) × 50k total rows (500 rows × 100 static tables), with
    /// variegated entity / attribute / FK shapes via the deterministic
    /// RNG. This is the chapter-3.6 perf-gate baseline per the
    /// 2026-05-09 operator directive: "canary-gate.sql is
    /// inappropriate for stop hooks ... 50k records, variegated, 300
    /// tables. Full stop."
    ///
    /// Why this shape:
    ///   - **300 tables** — the VISION.md forcing function (300-table
    ///     OutSystems 11 system facing External Entities cutover);
    ///     same scale as `realistic` so per-table emit / readside /
    ///     deploy paths are exercised at production cardinality.
    ///   - **50k rows** — enough to exercise the SqlBulkCopy path
    ///     across many tables (vs `bulk1k`/`10k`/`100k` which
    ///     concentrate rows in 5 tables); spreads bulk-load over
    ///     100 static tables (500 rows each) so the per-table-bulk
    ///     overhead amortizes realistically.
    ///   - **Variegated** — RNG-driven attribute counts + FK density
    ///     so per-iteration distribution surfaces in the bench
    ///     rollup (P50 vs P95 vs P99 per pass / per emit kind);
    ///     fixed shape would hide tail-latency regressions.
    ///   - **Deterministic seed** (42; same as other specs) — bench
    ///     measurements are comparable across runs; perf-gate
    ///     `μ + Kσ` outlier detection requires stable mean.
    let operatorReality : GenerateSpec =
        {
            Modules = 8
            // 2026-05-20 tuning (pass 2): table count 300 → 150
            // (Entities 200→100; StaticEntities 100→50; preserved
            // 2:1 non-static:static ratio). The row-count cut alone
            // (pass 1, StaticRowsPerEntity 500→125) produced only
            // ~25% wall-time reduction because the dominant cost is
            // DDL deploy + ReadSide reflection over the table count,
            // not seed-row processing. Cutting tables 300→150 halves
            // deploy + reflection cost while preserving the FK-density
            // envelope at the smaller scale. Operator framing: "we're
            // not getting the value of waiting for it all the time."
            // See DECISIONS 2026-05-20 (canary volume reduction).
            Entities = 100
            StaticEntities = 50
            AvgAttrsPerEntity = 10
            FkDensity = 0.2
            // 2026-05-20 tuning (pass 1): StaticRowsPerEntity 500 → 125
            // (per-static-entity row volume cut 75%; total seed rows
            // post-both-passes = 50 × 125 = 6,250).
            StaticRowsPerEntity = 125
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

    /// Audit columns common to every regular entity (slice
    /// A.4.7'-prelude.comprehensive-canary — extracted so the OSSYS-
    /// metadata synthesizer can describe them structurally).
    let private auditAttributes : GeneratedAttribute list =
        [
            { PhysicalName = "ID";        SqlType = "INT";                IsMandatory = true; IsIdentifier = true;  IsAutoNumber = true;  ReferencedTable = None }
            { PhysicalName = "TENANT_ID"; SqlType = "INT";                IsMandatory = true; IsIdentifier = false; IsAutoNumber = false; ReferencedTable = None }
            { PhysicalName = "SS_KEY";    SqlType = "UNIQUEIDENTIFIER";   IsMandatory = true; IsIdentifier = false; IsAutoNumber = false; ReferencedTable = None }
            { PhysicalName = "CREATEDON"; SqlType = "DATETIME2";          IsMandatory = true; IsIdentifier = false; IsAutoNumber = false; ReferencedTable = None }
            { PhysicalName = "UPDATEDON"; SqlType = "DATETIME2";          IsMandatory = true; IsIdentifier = false; IsAutoNumber = false; ReferencedTable = None }
        ]

    /// Generate one table's CREATE TABLE statement plus its FK
    /// ADD CONSTRAINT (if any). The `priorEntities` list contains
    /// the physical names of tables emitted earlier in topological
    /// order; FKs target one of these to guarantee acyclicity.
    ///
    /// Returns the structural model so the OSSYS-metadata synthesizer
    /// can describe the same columns + FK relationships.
    let private generateTable
        (rng: Random)
        (spec: GenerateSpec)
        (modIdx: int)
        (modCode: string)
        (entityName: string)
        (priorEntities: string list)
        (sb: StringBuilder)
        : GeneratedEntityModel =
        let tableName = sprintf "OSUSR_%s_%s" modCode (entityName.ToUpperInvariant())
        let attrCount =
            let varied = rng.Next(spec.AvgAttrsPerEntity / 2, spec.AvgAttrsPerEntity * 3 / 2 + 1)
            max 3 varied
        // FK column (when this entity has an outgoing FK)
        let fkColumnModel, fkColumnDecl, fkConstraint =
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
                let model =
                    {
                        PhysicalName    = fkColumnName
                        SqlType         = "INT"
                        IsMandatory     = false
                        IsIdentifier    = false
                        IsAutoNumber    = false
                        ReferencedTable = Some target
                    }
                Some model, Some fkColumnDecl, Some fkClause
            else
                None, None, None
        // Generate non-audit user columns. Capture the random draws
        // so the structural-model output mirrors what the DDL declares
        // byte-for-byte.
        let userCols : (string * GeneratedAttribute) list =
            [
                for i in 0 .. attrCount - 1 ->
                    let col = columnName rng i
                    let typ = pickColumnType rng
                    let mandatory = not (rng.NextDouble() < 0.4)
                    let nullness = if mandatory then "NOT NULL" else "NULL"
                    let decl = sprintf "[%s] %s %s" col typ nullness
                    let model =
                        {
                            PhysicalName    = col
                            SqlType         = typ
                            IsMandatory     = mandatory
                            IsIdentifier    = false
                            IsAutoNumber    = false
                            ReferencedTable = None
                        }
                    decl, model
            ]
        // Audit-col text + user-col text + FK-col text — matching the
        // DDL declarations order so attribute layout is consistent.
        let auditColTexts =
            [
                "[ID] INT NOT NULL IDENTITY(1,1)"
                "[TENANT_ID] INT NOT NULL"
                "[SS_KEY] UNIQUEIDENTIFIER NOT NULL"
                "[CREATEDON] DATETIME2 NOT NULL"
                "[UPDATEDON] DATETIME2 NOT NULL"
            ]
        // Compose
        sb.AppendLine(sprintf "CREATE TABLE [dbo].[%s] (" tableName) |> ignore
        let allCols =
            auditColTexts @ (userCols |> List.map fst) @ (Option.toList fkColumnDecl)
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
        // Compose structural model: audit + user + FK.
        let attrs =
            auditAttributes
            @ (userCols |> List.map snd)
            @ (Option.toList fkColumnModel)
        {
            ModuleIndex   = modIdx
            ModuleCode    = modCode
            EntityName    = entityName
            PhysicalTable = tableName
            IsStatic      = false
            Attributes    = attrs
        }

    /// Schema of a static (lookup) entity. Fixed per the
    /// `generateStaticTable` DDL emission.
    let private staticAttributes : GeneratedAttribute list =
        [
            { PhysicalName = "ID";        SqlType = "INT";              IsMandatory = true; IsIdentifier = true;  IsAutoNumber = false; ReferencedTable = None }
            { PhysicalName = "LABEL";     SqlType = "NVARCHAR(100)";    IsMandatory = true; IsIdentifier = false; IsAutoNumber = false; ReferencedTable = None }
            { PhysicalName = "CODE";      SqlType = "NVARCHAR(50)";     IsMandatory = true; IsIdentifier = false; IsAutoNumber = false; ReferencedTable = None }
            { PhysicalName = "IS_ACTIVE"; SqlType = "BIT";              IsMandatory = true; IsIdentifier = false; IsAutoNumber = false; ReferencedTable = None }
            { PhysicalName = "SS_KEY";    SqlType = "UNIQUEIDENTIFIER"; IsMandatory = true; IsIdentifier = false; IsAutoNumber = false; ReferencedTable = None }
        ]

    /// Generate one static entity (lookup table) with simpler shape:
    /// no audit columns, no FKs, just (ID, LABEL, SS_KEY).
    let private generateStaticTable
        (modIdx: int)
        (modCode: string)
        (entityName: string)
        (sb: StringBuilder)
        : GeneratedEntityModel =
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
        {
            ModuleIndex   = modIdx
            ModuleCode    = modCode
            EntityName    = entityName
            PhysicalTable = tableName
            IsStatic      = true
            Attributes    = staticAttributes
        }

    // -----------------------------------------------------------------
    // Variegated production-like value generators. Per session-33
    // operator framing — "variegated randomly to simulate production
    // data." Each generator is RNG-seeded for run-to-run determinism;
    // same seed → same data byte-for-byte.
    //
    // The value pools mirror shapes operators see in real OutSystems
    // datasets: human-name strings, locale codes, ISO-style timestamps,
    // currency-precision decimals, deterministic GUIDs.
    // -----------------------------------------------------------------

    let private statusLabels =
        [|
            "Active"; "Inactive"; "Pending Review"; "In Progress"
            "Submitted"; "Approved"; "Rejected"; "Archived"
            "Draft"; "Published"; "Suspended"; "Terminated"
            "Awaiting Verification"; "Verified"; "Onboarding"; "Onboarded"
            "Trial"; "Subscribed"; "Lapsed"; "Renewed"
            "Open"; "Closed"; "Reopened"; "Escalated"
            "Confirmed"; "Cancelled"; "Refunded"; "Disputed"
        |]

    let private statusCodes =
        [|
            "ACT"; "INA"; "PRV"; "PRG"; "SUB"; "APR"; "REJ"; "ARC"
            "DRF"; "PUB"; "SUS"; "TRM"; "AWV"; "VER"; "ONB"; "OND"
            "TRI"; "SBD"; "LPS"; "RNW"; "OPN"; "CLS"; "ROP"; "ESC"
            "CFM"; "CXL"; "RFD"; "DIS"
        |]

    /// Deterministic GUID derived from the RNG. Same seed →
    /// same GUID sequence, so the canary's row-set hash is stable
    /// across runs.
    let private nextGuid (rng: Random) : System.Guid =
        let bytes = Array.zeroCreate 16
        rng.NextBytes bytes
        System.Guid bytes

    /// Generate a variegated INSERT row for the static-entity shape
    /// (ID, LABEL, CODE, IS_ACTIVE, SS_KEY). The label / code pools
    /// give realistic-looking variety so the canary's row-data
    /// round-trip exercises a meaningful sample of string content,
    /// boolean distribution, GUID coverage.
    /// Sqlcmd-style batch chunk size. Per session-34 — `Deploy.executeBatch`
    /// splits SQL on `^\s*GO\s*$` markers and runs each segment in
    /// its own round-trip; chunking keeps any one batch's text size
    /// bounded so SqlClient round-trips and SQL Server parse time
    /// stay reasonable at 100k+ row scale.
    [<Literal>]
    let private SeedBatchSize : int = 1_000

    /// Per session-35 — emits the static-seed rows in two parallel
    /// forms: GO-chunked INSERT text (for executeBatch consumers)
    /// and typed `CellValue list list` (for `Bulk.copyRows`
    /// consumers). Same RNG sequence drives both, so seed-byte
    /// determinism holds: text and bulk forms describe the same
    /// rows.
    let private generateStaticSeed
        (rng: Random)
        (rowsPerEntity: int)
        (tableName: string)
        (sb: StringBuilder)
        : CellValue list list =
        let bulkRows = ResizeArray<CellValue list>(rowsPerEntity)
        for i in 1 .. rowsPerEntity do
            if i > 1 && (i - 1) % SeedBatchSize = 0 then
                sb.AppendLine "GO" |> ignore
            let labelIdx = rng.Next statusLabels.Length
            let label = sprintf "%s %d" statusLabels[labelIdx] i
            let codeIdx = rng.Next statusCodes.Length
            let code = sprintf "%s%03d" statusCodes[codeIdx] i
            // Active distribution: ~85% active, mirrors typical
            // lookup tables' deactivation patterns.
            let active = if rng.NextDouble() < 0.85 then 1 else 0
            let guid = (nextGuid rng).ToString "D"
            sb.AppendLine(
                sprintf
                    "INSERT INTO [dbo].[%s] ([ID], [LABEL], [CODE], [IS_ACTIVE], [SS_KEY]) VALUES (%d, N'%s', N'%s', %d, '%s');"
                    tableName
                    i
                    (label.Replace("'", "''"))
                    code
                    active
                    guid)
            |> ignore
            bulkRows.Add
                [
                    { Column = "ID";        Type = Integer; Raw = string i }
                    { Column = "LABEL";     Type = Text;    Raw = label }
                    { Column = "CODE";      Type = Text;    Raw = code }
                    { Column = "IS_ACTIVE"; Type = Boolean; Raw = if active = 1 then "true" else "false" }
                    { Column = "SS_KEY";    Type = Guid;    Raw = guid }
                ]
        if rowsPerEntity > 0 then
            sb.AppendLine "GO" |> ignore
        List.ofSeq bulkRows

    /// Generate a fixture matching the spec. Deterministic per
    /// `spec.Seed`: same spec → same DDL byte-for-byte.
    let generate (spec: GenerateSpec) : GeneratedFixture =
        // Fully deterministic: every random draw — structure, names,
        // types, FK targets, AND static-seed-row SS_Key GUIDs (via
        // `nextGuid`, which pulls from this same seeded `rng`) — flows
        // from `spec.Seed`. No `Guid.NewGuid`, clock, or ambient state.
        // Same spec → byte-identical `Ddl`, `SeedData`, and `Combined`
        // (asserted by the determinism test in `GeneratorScaleTests`).
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
        // FK-reference any entity 0..i-1. Capture structural models so
        // the OSSYS-metadata synthesizer (slice
        // A.4.7'-prelude.comprehensive-canary) can describe the same
        // schema declaratively.
        let mutable priorEntities : string list = []
        let mutable totalTables = 0
        let entityModels = ResizeArray<GeneratedEntityModel>(spec.Entities + spec.StaticEntities)

        for i in 0 .. spec.Entities - 1 do
            let modIdx = i % spec.Modules
            let modCode = moduleCode modIdx
            let ename = entityName rng i
            let model = generateTable rng spec modIdx modCode ename priorEntities ddlSb
            priorEntities <- model.PhysicalTable :: priorEntities
            entityModels.Add model
            totalTables <- totalTables + 1

        // Static entities — independent of regular entities; emitted
        // after for clarity. Each table's seed rows are captured in
        // both text form (text-INSERT batches in `dataSb`) and bulk
        // form (typed `CellValue` lists in `bulkSeeds`) so callers
        // can choose a loader path.
        let bulkSeeds = ResizeArray<StaticTableSeed>(spec.StaticEntities)
        for i in 0 .. spec.StaticEntities - 1 do
            let modIdx = i % spec.Modules
            let modCode = moduleCode modIdx
            let sname = staticEntityName i
            let model = generateStaticTable modIdx modCode sname ddlSb
            entityModels.Add model
            let rows = generateStaticSeed rng spec.StaticRowsPerEntity model.PhysicalTable dataSb
            if not (List.isEmpty rows) then
                bulkSeeds.Add
                    {
                        Table = (TableId.create "dbo" model.PhysicalTable |> Result.value)
                        Rows = rows
                    }
            totalTables <- totalTables + 1

        let ddl = ddlSb.ToString()
        let seedData = dataSb.ToString()
        let combined =
            if seedData.Length = 0 then ddl
            else
                let combinedSb = StringBuilder(ddl.Length + seedData.Length + 64)
                combinedSb.Append(ddl) |> ignore
                combinedSb.AppendLine() |> ignore
                combinedSb.AppendLine("-- Seed data --") |> ignore
                combinedSb.Append(seedData) |> ignore
                combinedSb.ToString()
        let seedRowCount = spec.StaticEntities * spec.StaticRowsPerEntity
        {
            Ddl = ddl
            SeedData = seedData
            Combined = combined
            BulkSeeds = List.ofSeq bulkSeeds
            Entities = List.ofSeq entityModels
            TableCount = totalTables
            SeedRowCount = seedRowCount
        }
