module Projection.Tests.SourceFixtures.SourceSchema

/// OutSystems-shaped source schema fixtures for the canary's wide
/// integration surface. Per `DECISIONS 2026-05-23 — Source SQL Server
/// with OutSystems semantics is the canary's primary wide integration
/// surface`, these DDL strings are deployed to ephemeral SQL Server
/// containers and represent the operator's actual schema reality.
///
/// **Iterative growth.** This file starts with the minimum viable
/// OutSystems shape (one user table + one entity table). It grows
/// over time as new shapes need coverage; per the trace-before-
/// fixture pattern, each new shape addition follows:
///
///   1. Identify the OutSystems shape needing coverage.
///   2. Trace what the OutSystems platform actually emits to disk
///      (V1's `SnapshotJsonBuilder` is the reference).
///   3. Add the shape here as a new table or column.
///   4. Run the canary's round-trip test; observe what surfaces.
///   5. Either improve V2's IR/adapter/emitter, OR add a tolerance
///      flag (S0.E) with a DECISIONS entry citing this fixture
///      shape and the deferred resolution.
///
/// **Conventions mirrored** (per DECISIONS 2026-05-23):
///   - Table naming: `OSUSR_<MODULE-CODE>_<ENTITY-NAME>` upper-case.
///   - Identity: `[ID] INT NOT NULL IDENTITY(1,1) PRIMARY KEY`.
///   - Multi-tenant marker: `[TENANT_ID] INT NOT NULL`.
///   - Audit columns: `[CREATEDBY] INT NULL`, `[CREATEDON] DATETIME2 NOT NULL`,
///     `[UPDATEDBY] INT NULL`, `[UPDATEDON] DATETIME2 NOT NULL`.
///   - Stable-key column: `[SS_KEY] UNIQUEIDENTIFIER NOT NULL`.
///   - NVARCHAR length: explicit (250 / 500 / etc.).
///   - Decimal precision: explicit (e.g., `DECIMAL(18, 4)`).
///   - Boolean as `BIT`.
///
/// **Known V2 IR limitations the canary will surface** (each is a
/// candidate for either IR refinement or a Tolerance flag in M4):
///
///   - **Column length.** V2's `PrimitiveType.Text` does not carry
///     an N value; the emitter outputs `NVARCHAR(MAX)`. Source's
///     `NVARCHAR(250)` round-trips as `NVARCHAR(MAX)` — caught only
///     when length is part of `PhysicalColumn` (deferred to M4).
///   - **Decimal precision.** V2's `PrimitiveType.Decimal` does not
///     carry (P, S); the emitter outputs `DECIMAL(18, 4)`.
///     Source's `DECIMAL(38, 8)` round-trips as `DECIMAL(18, 4)` —
///     caught only when precision is part of `PhysicalColumn`.
///   - **IDENTITY property.** Not in V2 IR; emitter doesn't emit
///     `IDENTITY(1,1)`. Round-trip drops identity-ness. M4 tolerance.
///   - **FK constraints.** Emitter emits `ALTER TABLE … ADD
///     CONSTRAINT FK_…` only when V2 IR carries a `Reference` —
///     ReadSide doesn't yet reconstruct References (M3 deferred).

/// **Minimal source.** One table with the OutSystems user-shape
/// pattern: identity PK + multi-tenant + audit + SS_KEY. Exercises
/// the IR's PrimitiveType vocabulary partially (Integer, Text,
/// DateTime, Guid). This is the entry-level fixture the canary's
/// `M3` round-trip test runs against.
let minimal : string =
    String.concat
        "\n"
        [
            "CREATE TABLE [dbo].[OSUSR_M3_USER] ("
            "    [ID] INT NOT NULL IDENTITY(1,1),"
            "    [USERNAME] NVARCHAR(250) NOT NULL,"
            "    [EMAIL] NVARCHAR(250) NULL,"
            "    [TENANT_ID] INT NOT NULL,"
            "    [SS_KEY] UNIQUEIDENTIFIER NOT NULL,"
            "    [CREATEDON] DATETIME2 NOT NULL,"
            "    [UPDATEDON] DATETIME2 NOT NULL,"
            "    CONSTRAINT [PK_dbo_OSUSR_M3_USER] PRIMARY KEY ([ID])"
            ");"
        ]

/// **Realistic source.** Two-table OutSystems shape with full
/// PrimitiveType coverage: a USER table (FK target for audit
/// reflows) and a CUSTOMER table that exercises Integer / Text /
/// Decimal / Boolean / DateTime / Date / Guid types. The CREATEDBY
/// / UPDATEDBY columns are typed `INT` (audit-FK shape) but the FK
/// constraint itself is omitted for M3 — the canary's emitter side
/// doesn't yet round-trip FK references (deferred to a follow-up
/// slice; see `Deploy.runWideCanary` and `ReadSide` notes).
let realistic : string =
    String.concat
        "\n"
        [
            "CREATE TABLE [dbo].[OSUSR_M3_USER] ("
            "    [ID] INT NOT NULL IDENTITY(1,1),"
            "    [USERNAME] NVARCHAR(250) NOT NULL,"
            "    [EMAIL] NVARCHAR(250) NULL,"
            "    [TENANT_ID] INT NOT NULL,"
            "    [SS_KEY] UNIQUEIDENTIFIER NOT NULL,"
            "    [CREATEDON] DATETIME2 NOT NULL,"
            "    [UPDATEDON] DATETIME2 NOT NULL,"
            "    CONSTRAINT [PK_dbo_OSUSR_M3_USER] PRIMARY KEY ([ID])"
            ");"
            ""
            "CREATE TABLE [dbo].[OSUSR_M3_CUSTOMER] ("
            "    [ID] INT NOT NULL IDENTITY(1,1),"
            "    [NAME] NVARCHAR(250) NOT NULL,"
            "    [TENANT_ID] INT NOT NULL,"
            "    [BALANCE] DECIMAL(18, 4) NULL,"
            "    [IS_ACTIVE] BIT NOT NULL,"
            "    [CREATEDBY] INT NULL,"
            "    [UPDATEDBY] INT NULL,"
            "    [CREATEDON] DATETIME2 NOT NULL,"
            "    [UPDATEDON] DATETIME2 NOT NULL,"
            "    [SIGNED_ON] DATE NULL,"
            "    [SS_KEY] UNIQUEIDENTIFIER NOT NULL,"
            "    CONSTRAINT [PK_dbo_OSUSR_M3_CUSTOMER] PRIMARY KEY ([ID])"
            ");"
            ""
            "-- Slice D.1.c — V2.LogicalName extended properties; ReadSide"
            "-- slice-D.1.b hydration recovers operator-meaningful Kind.Name /"
            "-- Attribute.Name values so the canary exercises the recovery path."
            "EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'User',       @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_USER';"
            "EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'Id',         @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_USER', @level2type = N'COLUMN', @level2name = N'ID';"
            "EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'Username',   @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_USER', @level2type = N'COLUMN', @level2name = N'USERNAME';"
            "EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'Email',      @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_USER', @level2type = N'COLUMN', @level2name = N'EMAIL';"
            "EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'TenantId',   @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_USER', @level2type = N'COLUMN', @level2name = N'TENANT_ID';"
            "EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'SsKey',      @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_USER', @level2type = N'COLUMN', @level2name = N'SS_KEY';"
            "EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'CreatedOn',  @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_USER', @level2type = N'COLUMN', @level2name = N'CREATEDON';"
            "EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'UpdatedOn',  @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_USER', @level2type = N'COLUMN', @level2name = N'UPDATEDON';"
            "EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'Customer',   @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_CUSTOMER';"
            "EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'Id',         @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_CUSTOMER', @level2type = N'COLUMN', @level2name = N'ID';"
            "EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'Name',       @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_CUSTOMER', @level2type = N'COLUMN', @level2name = N'NAME';"
            "EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'TenantId',   @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_CUSTOMER', @level2type = N'COLUMN', @level2name = N'TENANT_ID';"
            "EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'Balance',    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_CUSTOMER', @level2type = N'COLUMN', @level2name = N'BALANCE';"
            "EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'IsActive',   @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_CUSTOMER', @level2type = N'COLUMN', @level2name = N'IS_ACTIVE';"
            "EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'CreatedBy',  @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_CUSTOMER', @level2type = N'COLUMN', @level2name = N'CREATEDBY';"
            "EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'UpdatedBy',  @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_CUSTOMER', @level2type = N'COLUMN', @level2name = N'UPDATEDBY';"
            "EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'CreatedOn',  @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_CUSTOMER', @level2type = N'COLUMN', @level2name = N'CREATEDON';"
            "EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'UpdatedOn',  @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_CUSTOMER', @level2type = N'COLUMN', @level2name = N'UPDATEDON';"
            "EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'SignedOn',   @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_CUSTOMER', @level2type = N'COLUMN', @level2name = N'SIGNED_ON';"
            "EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'SsKey',      @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_CUSTOMER', @level2type = N'COLUMN', @level2name = N'SS_KEY';"
        ]

/// **Enterprise source.** Multi-module OutSystems shape mirroring
/// the conventions of an actual OutSystems platform deployment.
/// Three modules (`IDM` Identity Management, `CAT` Catalog, `SLS`
/// Sales) hosting ten interrelated tables with realistic shapes:
///
///   IDM:
///     OSUSR_IDM_USER         — auth users; audit-FK target across modules
///     OSUSR_IDM_ROLE         — Static-entity-shaped role lookup
///     OSUSR_IDM_USERROLE     — junction (USER × ROLE)
///
///   CAT:
///     OSUSR_CAT_CATEGORY     — Static-entity-shaped product categories
///     OSUSR_CAT_PRODUCT      — products with category FK + audit FKs
///
///   SLS:
///     OSUSR_SLS_CUSTOMER     — customers with audit FKs
///     OSUSR_SLS_ORDERSTATUS  — Static-entity-shaped order statuses
///     OSUSR_SLS_ORDER        — orders with multi-FK to CUSTOMER /
///                              ORDERSTATUS / USER (audit)
///     OSUSR_SLS_ORDERLINE    — order-line junction (ORDER × PRODUCT)
///     OSUSR_SLS_PROMOTION    — promotions with date ranges
///
/// **Shapes exercised** (beyond what `realistic` covers):
///   - **Three modules** with cross-module FK references
///     (PRODUCT in CAT references CATEGORY in CAT; ORDER in SLS
///     references CUSTOMER in SLS; CUSTOMER and PRODUCT both have
///     audit FKs to USER in IDM — cross-module).
///   - **Junction tables** (USERROLE, ORDERLINE) — composite
///     associations with surrogate IDs per OutSystems convention.
///   - **Static entities** (ROLE, CATEGORY, ORDERSTATUS) — lookup
///     tables with `[LABEL]` and `[ORDER]` columns. Static-entity
///     populations are NOT included as INSERT statements here
///     (data round-trip is chapter 4.1.B territory; the canary's
///     PhysicalSchema axis is schema-only).
///   - **Domain FK chains** (ORDERLINE → ORDER → CUSTOMER;
///     ORDERLINE → PRODUCT → CATEGORY).
///   - **Multi-tenant marker** on regular entities; absent on
///     static / junction entities (mirrors OutSystems platform
///     defaults).
///   - **NVARCHAR length variety**: 50 (codes), 100 (names), 250
///     (descriptions), 500 (long names), 1000 (free text).
///   - **DECIMAL precision variety**: (18, 4) currency, (8, 2)
///     percentages, (38, 8) high-precision.
///   - **Date / Time / DateTime variety** (CREATEDON / UPDATEDON
///     / SIGNED_ON / VALID_FROM / VALID_UNTIL).
///   - **Soft-delete** via `[ISACTIVE] BIT NOT NULL` on most
///     entities.
///
/// **Round-trip findings** (each catalogued under M4 Tolerance
/// taxonomy work; see DECISIONS 2026-05-23 fixture-growth model):
///
///   - Schema-only round-trip (PhysicalSchema axis) holds: source
///     and target both deploy 10 tables; column-set / type-set /
///     nullability / PK columns match.
///   - FK constraints surface in source but ReadSide does not
///     reconstruct References (M3 deferred); V2 emit produces
///     no FKs; PhysicalSchema invariant under FK absence.
///   - NVARCHAR(N) → NVARCHAR(MAX), DECIMAL(P,S) → DECIMAL(18,4),
///     IDENTITY property — all dropped by V2's IR shape; surfaced
///     when M4 introduces `Tolerance.IgnoreColumnLength` /
///     `IgnoreDecimalPrecision` / `IgnoreIdentityProperty`.
let enterprise : string =
    String.concat
        "\n"
        [
            // -- IDM module --------------------------------------------
            "CREATE TABLE [dbo].[OSUSR_IDM_USER] ("
            "    [ID] INT NOT NULL IDENTITY(1,1),"
            "    [SS_KEY] UNIQUEIDENTIFIER NOT NULL,"
            "    [USERNAME] NVARCHAR(100) NOT NULL,"
            "    [EMAIL] NVARCHAR(250) NOT NULL,"
            "    [DISPLAY_NAME] NVARCHAR(250) NOT NULL,"
            "    [TENANT_ID] INT NULL,"
            "    [ISACTIVE] BIT NOT NULL,"
            "    [CREATEDBY] INT NULL,"
            "    [CREATEDON] DATETIME2 NOT NULL,"
            "    [UPDATEDBY] INT NULL,"
            "    [UPDATEDON] DATETIME2 NOT NULL,"
            "    CONSTRAINT [PK_dbo_OSUSR_IDM_USER] PRIMARY KEY ([ID])"
            ");"
            ""
            "CREATE TABLE [dbo].[OSUSR_IDM_ROLE] ("
            "    [ID] INT NOT NULL,"
            "    [SS_KEY] UNIQUEIDENTIFIER NOT NULL,"
            "    [LABEL] NVARCHAR(100) NOT NULL,"
            "    [CODE] NVARCHAR(50) NOT NULL,"
            "    [ORDER] INT NOT NULL,"
            "    [ISACTIVE] BIT NOT NULL,"
            "    [CREATEDON] DATETIME2 NOT NULL,"
            "    [UPDATEDON] DATETIME2 NOT NULL,"
            "    CONSTRAINT [PK_dbo_OSUSR_IDM_ROLE] PRIMARY KEY ([ID])"
            ");"
            ""
            "CREATE TABLE [dbo].[OSUSR_IDM_USERROLE] ("
            "    [ID] INT NOT NULL IDENTITY(1,1),"
            "    [SS_KEY] UNIQUEIDENTIFIER NOT NULL,"
            "    [USERID] INT NOT NULL,"
            "    [ROLEID] INT NOT NULL,"
            "    [ISACTIVE] BIT NOT NULL,"
            "    [CREATEDBY] INT NULL,"
            "    [CREATEDON] DATETIME2 NOT NULL,"
            "    [UPDATEDBY] INT NULL,"
            "    [UPDATEDON] DATETIME2 NOT NULL,"
            "    CONSTRAINT [PK_dbo_OSUSR_IDM_USERROLE] PRIMARY KEY ([ID])"
            ");"
            ""
            // -- CAT module --------------------------------------------
            "CREATE TABLE [dbo].[OSUSR_CAT_CATEGORY] ("
            "    [ID] INT NOT NULL,"
            "    [SS_KEY] UNIQUEIDENTIFIER NOT NULL,"
            "    [LABEL] NVARCHAR(100) NOT NULL,"
            "    [CODE] NVARCHAR(50) NOT NULL,"
            "    [DESCRIPTION] NVARCHAR(500) NULL,"
            "    [ORDER] INT NOT NULL,"
            "    [ISACTIVE] BIT NOT NULL,"
            "    [CREATEDON] DATETIME2 NOT NULL,"
            "    [UPDATEDON] DATETIME2 NOT NULL,"
            "    CONSTRAINT [PK_dbo_OSUSR_CAT_CATEGORY] PRIMARY KEY ([ID])"
            ");"
            ""
            "CREATE TABLE [dbo].[OSUSR_CAT_PRODUCT] ("
            "    [ID] INT NOT NULL IDENTITY(1,1),"
            "    [SS_KEY] UNIQUEIDENTIFIER NOT NULL,"
            "    [NAME] NVARCHAR(250) NOT NULL,"
            "    [SKU] NVARCHAR(50) NOT NULL,"
            "    [DESCRIPTION] NVARCHAR(1000) NULL,"
            "    [CATEGORYID] INT NOT NULL,"
            "    [PRICE] DECIMAL(18, 4) NOT NULL,"
            "    [WEIGHT_GRAMS] DECIMAL(38, 8) NULL,"
            "    [IS_DIGITAL] BIT NOT NULL,"
            "    [TENANT_ID] INT NULL,"
            "    [ISACTIVE] BIT NOT NULL,"
            "    [CREATEDBY] INT NULL,"
            "    [CREATEDON] DATETIME2 NOT NULL,"
            "    [UPDATEDBY] INT NULL,"
            "    [UPDATEDON] DATETIME2 NOT NULL,"
            "    CONSTRAINT [PK_dbo_OSUSR_CAT_PRODUCT] PRIMARY KEY ([ID])"
            ");"
            ""
            // -- SLS module --------------------------------------------
            "CREATE TABLE [dbo].[OSUSR_SLS_CUSTOMER] ("
            "    [ID] INT NOT NULL IDENTITY(1,1),"
            "    [SS_KEY] UNIQUEIDENTIFIER NOT NULL,"
            "    [DISPLAY_NAME] NVARCHAR(250) NOT NULL,"
            "    [EMAIL] NVARCHAR(250) NULL,"
            "    [PHONE] NVARCHAR(50) NULL,"
            "    [LIFETIME_VALUE] DECIMAL(18, 4) NULL,"
            "    [LOYALTY_RATE] DECIMAL(8, 2) NULL,"
            "    [TENANT_ID] INT NULL,"
            "    [ISACTIVE] BIT NOT NULL,"
            "    [CREATEDBY] INT NULL,"
            "    [CREATEDON] DATETIME2 NOT NULL,"
            "    [UPDATEDBY] INT NULL,"
            "    [UPDATEDON] DATETIME2 NOT NULL,"
            "    CONSTRAINT [PK_dbo_OSUSR_SLS_CUSTOMER] PRIMARY KEY ([ID])"
            ");"
            ""
            "CREATE TABLE [dbo].[OSUSR_SLS_ORDERSTATUS] ("
            "    [ID] INT NOT NULL,"
            "    [SS_KEY] UNIQUEIDENTIFIER NOT NULL,"
            "    [LABEL] NVARCHAR(100) NOT NULL,"
            "    [CODE] NVARCHAR(50) NOT NULL,"
            "    [ORDER] INT NOT NULL,"
            "    [IS_TERMINAL] BIT NOT NULL,"
            "    [ISACTIVE] BIT NOT NULL,"
            "    [CREATEDON] DATETIME2 NOT NULL,"
            "    [UPDATEDON] DATETIME2 NOT NULL,"
            "    CONSTRAINT [PK_dbo_OSUSR_SLS_ORDERSTATUS] PRIMARY KEY ([ID])"
            ");"
            ""
            "CREATE TABLE [dbo].[OSUSR_SLS_ORDER] ("
            "    [ID] INT NOT NULL IDENTITY(1,1),"
            "    [SS_KEY] UNIQUEIDENTIFIER NOT NULL,"
            "    [ORDER_NUMBER] NVARCHAR(50) NOT NULL,"
            "    [CUSTOMERID] INT NOT NULL,"
            "    [STATUSID] INT NOT NULL,"
            "    [ORDER_DATE] DATETIME2 NOT NULL,"
            "    [SHIPPED_ON] DATE NULL,"
            "    [DELIVERED_ON] DATETIME2 NULL,"
            "    [TOTAL] DECIMAL(18, 4) NOT NULL,"
            "    [DISCOUNT_RATE] DECIMAL(8, 2) NOT NULL,"
            "    [TENANT_ID] INT NULL,"
            "    [ISACTIVE] BIT NOT NULL,"
            "    [CREATEDBY] INT NULL,"
            "    [CREATEDON] DATETIME2 NOT NULL,"
            "    [UPDATEDBY] INT NULL,"
            "    [UPDATEDON] DATETIME2 NOT NULL,"
            "    CONSTRAINT [PK_dbo_OSUSR_SLS_ORDER] PRIMARY KEY ([ID])"
            ");"
            ""
            "CREATE TABLE [dbo].[OSUSR_SLS_ORDERLINE] ("
            "    [ID] INT NOT NULL IDENTITY(1,1),"
            "    [SS_KEY] UNIQUEIDENTIFIER NOT NULL,"
            "    [ORDERID] INT NOT NULL,"
            "    [PRODUCTID] INT NOT NULL,"
            "    [QUANTITY] INT NOT NULL,"
            "    [UNIT_PRICE] DECIMAL(18, 4) NOT NULL,"
            "    [LINE_TOTAL] DECIMAL(18, 4) NOT NULL,"
            "    [ISACTIVE] BIT NOT NULL,"
            "    [CREATEDBY] INT NULL,"
            "    [CREATEDON] DATETIME2 NOT NULL,"
            "    [UPDATEDBY] INT NULL,"
            "    [UPDATEDON] DATETIME2 NOT NULL,"
            "    CONSTRAINT [PK_dbo_OSUSR_SLS_ORDERLINE] PRIMARY KEY ([ID])"
            ");"
            ""
            "CREATE TABLE [dbo].[OSUSR_SLS_PROMOTION] ("
            "    [ID] INT NOT NULL IDENTITY(1,1),"
            "    [SS_KEY] UNIQUEIDENTIFIER NOT NULL,"
            "    [LABEL] NVARCHAR(250) NOT NULL,"
            "    [CODE] NVARCHAR(50) NOT NULL,"
            "    [DESCRIPTION] NVARCHAR(1000) NULL,"
            "    [DISCOUNT_RATE] DECIMAL(8, 2) NOT NULL,"
            "    [VALID_FROM] DATETIME2 NOT NULL,"
            "    [VALID_UNTIL] DATETIME2 NULL,"
            "    [TENANT_ID] INT NULL,"
            "    [ISACTIVE] BIT NOT NULL,"
            "    [CREATEDBY] INT NULL,"
            "    [CREATEDON] DATETIME2 NOT NULL,"
            "    [UPDATEDBY] INT NULL,"
            "    [UPDATEDON] DATETIME2 NOT NULL,"
            "    CONSTRAINT [PK_dbo_OSUSR_SLS_PROMOTION] PRIMARY KEY ([ID])"
            ");"
            ""
            // -- Foreign-key constraints (separate ALTER TABLE batch) --
            // The source enforces referential integrity at deploy time;
            // V2's emitter does not yet round-trip these (ReadSide is
            // schema-only), so the target deploys without FKs. The
            // PhysicalSchema axis is invariant under FK absence.
            "ALTER TABLE [dbo].[OSUSR_IDM_USERROLE] ADD CONSTRAINT [FK_USERROLE_USER] "
            + "FOREIGN KEY ([USERID]) REFERENCES [dbo].[OSUSR_IDM_USER]([ID]);"
            "ALTER TABLE [dbo].[OSUSR_IDM_USERROLE] ADD CONSTRAINT [FK_USERROLE_ROLE] "
            + "FOREIGN KEY ([ROLEID]) REFERENCES [dbo].[OSUSR_IDM_ROLE]([ID]);"
            "ALTER TABLE [dbo].[OSUSR_CAT_PRODUCT] ADD CONSTRAINT [FK_PRODUCT_CATEGORY] "
            + "FOREIGN KEY ([CATEGORYID]) REFERENCES [dbo].[OSUSR_CAT_CATEGORY]([ID]);"
            "ALTER TABLE [dbo].[OSUSR_SLS_ORDER] ADD CONSTRAINT [FK_ORDER_CUSTOMER] "
            + "FOREIGN KEY ([CUSTOMERID]) REFERENCES [dbo].[OSUSR_SLS_CUSTOMER]([ID]);"
            "ALTER TABLE [dbo].[OSUSR_SLS_ORDER] ADD CONSTRAINT [FK_ORDER_STATUS] "
            + "FOREIGN KEY ([STATUSID]) REFERENCES [dbo].[OSUSR_SLS_ORDERSTATUS]([ID]);"
            "ALTER TABLE [dbo].[OSUSR_SLS_ORDERLINE] ADD CONSTRAINT [FK_ORDERLINE_ORDER] "
            + "FOREIGN KEY ([ORDERID]) REFERENCES [dbo].[OSUSR_SLS_ORDER]([ID]);"
            "ALTER TABLE [dbo].[OSUSR_SLS_ORDERLINE] ADD CONSTRAINT [FK_ORDERLINE_PRODUCT] "
            + "FOREIGN KEY ([PRODUCTID]) REFERENCES [dbo].[OSUSR_CAT_PRODUCT]([ID]);"
            ""
            // -- Audit FKs to IDM.USER (cross-module references) -------
            "ALTER TABLE [dbo].[OSUSR_IDM_USER] ADD CONSTRAINT [FK_USER_CREATEDBY] "
            + "FOREIGN KEY ([CREATEDBY]) REFERENCES [dbo].[OSUSR_IDM_USER]([ID]);"
            "ALTER TABLE [dbo].[OSUSR_IDM_USERROLE] ADD CONSTRAINT [FK_USERROLE_CREATEDBY] "
            + "FOREIGN KEY ([CREATEDBY]) REFERENCES [dbo].[OSUSR_IDM_USER]([ID]);"
            "ALTER TABLE [dbo].[OSUSR_CAT_PRODUCT] ADD CONSTRAINT [FK_PRODUCT_CREATEDBY] "
            + "FOREIGN KEY ([CREATEDBY]) REFERENCES [dbo].[OSUSR_IDM_USER]([ID]);"
            "ALTER TABLE [dbo].[OSUSR_SLS_CUSTOMER] ADD CONSTRAINT [FK_CUSTOMER_CREATEDBY] "
            + "FOREIGN KEY ([CREATEDBY]) REFERENCES [dbo].[OSUSR_IDM_USER]([ID]);"
            "ALTER TABLE [dbo].[OSUSR_SLS_ORDER] ADD CONSTRAINT [FK_ORDER_CREATEDBY] "
            + "FOREIGN KEY ([CREATEDBY]) REFERENCES [dbo].[OSUSR_IDM_USER]([ID]);"
            "ALTER TABLE [dbo].[OSUSR_SLS_ORDERLINE] ADD CONSTRAINT [FK_ORDERLINE_CREATEDBY] "
            + "FOREIGN KEY ([CREATEDBY]) REFERENCES [dbo].[OSUSR_IDM_USER]([ID]);"
            "ALTER TABLE [dbo].[OSUSR_SLS_PROMOTION] ADD CONSTRAINT [FK_PROMOTION_CREATEDBY] "
            + "FOREIGN KEY ([CREATEDBY]) REFERENCES [dbo].[OSUSR_IDM_USER]([ID]);"
        ]
