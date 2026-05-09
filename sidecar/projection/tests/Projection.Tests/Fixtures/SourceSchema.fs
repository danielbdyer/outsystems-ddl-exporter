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
        ]
