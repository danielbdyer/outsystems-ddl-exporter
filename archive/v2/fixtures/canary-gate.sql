-- canary-gate.sql — OutSystems-shaped source fixture for the
-- SessionEnd hook's final-verification canary.
--
-- Per DECISIONS 2026-05-23 — "Source SQL Server with OutSystems
-- semantics is the canary's primary wide integration surface" —
-- this DDL represents a realistic operator-side schema slice. The
-- session-end hook deploys it via `projection canary`, reads it
-- back via the readside adapter, runs V2's emitter on the
-- reconstruction, deploys to a target database, reads back, and
-- compares on the PhysicalSchema axis.
--
-- A green canary at session end gives operator confidence that
-- the work shipped during the session preserved the round-trip
-- invariants. A red canary surfaces regressions while context is
-- still fresh.
--
-- This file mirrors `Projection.Tests.SourceFixtures.SourceSchema.realistic`
-- but lives at the repo root so the SessionEnd hook can reference
-- it without traversing the F# test sources. Keep them in sync.
--
-- Conventions (per DECISIONS 2026-05-23):
--   - Table naming: OSUSR_<MODULE>_<ENTITY> upper-case
--   - Identity: [ID] INT NOT NULL IDENTITY(1,1) PRIMARY KEY
--   - Multi-tenant marker: [TENANT_ID] INT NOT NULL
--   - Audit columns: CREATEDBY/UPDATEDBY (INT NULL FK shape) +
--     CREATEDON/UPDATEDON (DATETIME2 NOT NULL)
--   - Stable-key column: [SS_KEY] UNIQUEIDENTIFIER NOT NULL
--   - NVARCHAR length: explicit
--   - Decimal precision: explicit
--   - Boolean as BIT
--
-- Known V2 IR limitations the canary surfaces (M4 Tolerance flags):
--   - NVARCHAR(N) round-trips as NVARCHAR(MAX) — Text type doesn't
--     carry length
--   - DECIMAL(P, S) round-trips as DECIMAL(18, 4) — Decimal doesn't
--     carry precision
--   - IDENTITY property dropped — not in V2 IR
--   - FK constraints not yet round-tripped — readside doesn't
--     reconstruct References

CREATE TABLE [dbo].[OSUSR_M3_USER] (
    [ID] INT NOT NULL IDENTITY(1,1),
    [USERNAME] NVARCHAR(250) NOT NULL,
    [EMAIL] NVARCHAR(250) NULL,
    [TENANT_ID] INT NOT NULL,
    [SS_KEY] UNIQUEIDENTIFIER NOT NULL,
    [CREATEDON] DATETIME2 NOT NULL,
    [UPDATEDON] DATETIME2 NOT NULL,
    CONSTRAINT [PK_dbo_OSUSR_M3_USER] PRIMARY KEY ([ID])
);

CREATE TABLE [dbo].[OSUSR_M3_CUSTOMER] (
    [ID] INT NOT NULL IDENTITY(1,1),
    [NAME] NVARCHAR(250) NOT NULL,
    [TENANT_ID] INT NOT NULL,
    [BALANCE] DECIMAL(18, 4) NULL,
    [IS_ACTIVE] BIT NOT NULL,
    [CREATEDBY] INT NULL,
    [UPDATEDBY] INT NULL,
    [CREATEDON] DATETIME2 NOT NULL,
    [UPDATEDON] DATETIME2 NOT NULL,
    [SIGNED_ON] DATE NULL,
    [SS_KEY] UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_dbo_OSUSR_M3_CUSTOMER] PRIMARY KEY ([ID])
);

-- Slice D.1.c — V2.LogicalName extended properties on every table +
-- every column. ReadSide's slice-D.1.b hydration query reads these
-- and populates `Kind.Name` / `Attribute.Name` from the operator-
-- meaningful logical name, so the source-side catalog has divergent
-- Kind.Name (logical) vs Kind.Physical.Table (deployed OSSYS shape).
-- The canary then exercises the recovery path end-to-end.

EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'User',       @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_USER';
EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'Id',         @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_USER', @level2type = N'COLUMN', @level2name = N'ID';
EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'Username',   @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_USER', @level2type = N'COLUMN', @level2name = N'USERNAME';
EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'Email',      @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_USER', @level2type = N'COLUMN', @level2name = N'EMAIL';
EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'TenantId',   @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_USER', @level2type = N'COLUMN', @level2name = N'TENANT_ID';
EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'SsKey',      @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_USER', @level2type = N'COLUMN', @level2name = N'SS_KEY';
EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'CreatedOn',  @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_USER', @level2type = N'COLUMN', @level2name = N'CREATEDON';
EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'UpdatedOn',  @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_USER', @level2type = N'COLUMN', @level2name = N'UPDATEDON';

EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'Customer',   @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_CUSTOMER';
EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'Id',         @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_CUSTOMER', @level2type = N'COLUMN', @level2name = N'ID';
EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'Name',       @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_CUSTOMER', @level2type = N'COLUMN', @level2name = N'NAME';
EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'TenantId',   @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_CUSTOMER', @level2type = N'COLUMN', @level2name = N'TENANT_ID';
EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'Balance',    @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_CUSTOMER', @level2type = N'COLUMN', @level2name = N'BALANCE';
EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'IsActive',   @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_CUSTOMER', @level2type = N'COLUMN', @level2name = N'IS_ACTIVE';
EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'CreatedBy',  @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_CUSTOMER', @level2type = N'COLUMN', @level2name = N'CREATEDBY';
EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'UpdatedBy',  @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_CUSTOMER', @level2type = N'COLUMN', @level2name = N'UPDATEDBY';
EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'CreatedOn',  @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_CUSTOMER', @level2type = N'COLUMN', @level2name = N'CREATEDON';
EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'UpdatedOn',  @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_CUSTOMER', @level2type = N'COLUMN', @level2name = N'UPDATEDON';
EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'SignedOn',   @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_CUSTOMER', @level2type = N'COLUMN', @level2name = N'SIGNED_ON';
EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'SsKey',      @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'OSUSR_M3_CUSTOMER', @level2type = N'COLUMN', @level2name = N'SS_KEY';
