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
