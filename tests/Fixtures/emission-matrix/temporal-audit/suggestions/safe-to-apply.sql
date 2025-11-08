-- ============================================================================
-- OutSystems DDL Exporter - Safe to Apply Opportunities
-- ============================================================================
-- Generated: 2024-01-01 00:00:00 UTC
--
-- SUMMARY:
--   Total Opportunities: 6
--   Validations: 6 (Existing constraints confirmed by profiling)
--
-- This script contains 6 safe to apply opportunities.
--
-- IMPORTANT: Never modify OutSystems model JSON files directly.
--            These scripts are suggestions only and will not auto-execute.
-- ============================================================================

-- ========== VALIDATION ==========

-- VALIDATIONS - INFORMATIONAL
--
-- These opportunities represent EXISTING constraints that profiling has validated.
-- The database already has these constraints in place, and the data conforms to them.
-- Examples include:
--   • Columns already marked as NOT NULL that have no null values
--   • Existing unique indexes with no duplicate values
--   • Foreign key constraints with no orphaned references
--
-- ACTION: No action needed. This is confirmation that your database and model are aligned.
-- ---------- Nullability ----------

-- NULLABILITY VALIDATIONS
-- Why this matters: Confirms that existing NOT NULL constraints are working correctly.
-- What to do: No action needed - this is validation that your constraints are effective.
-- Nullability dbo.OSUSR_M_AUDIT (ID) Category=Validation Risk=Low
-- Summary: Validated: Column is already NOT NULL and profiling confirms data integrity.
-- Rationale: DATA_NO_NULLS
-- Rationale: MANDATORY
-- Rationale: PHYSICAL_NOT_NULL
-- Rationale: PK
-- Evidence: MANDATORY
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=250, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: PHYSICAL_NOT_NULL
-- Evidence: PK
-- Evidence: Rows=250
ALTER TABLE [dbo].[OSUSR_M_AUDIT]
    ALTER COLUMN [ID] int NOT NULL;
GO

-- Nullability dbo.OSUSR_M_TEMPORALORDER (ID) Category=Validation Risk=Low
-- Summary: Validated: Column is already NOT NULL and profiling confirms data integrity.
-- Rationale: DATA_NO_NULLS
-- Rationale: MANDATORY
-- Rationale: PHYSICAL_NOT_NULL
-- Rationale: PK
-- Evidence: MANDATORY
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=1024, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: PHYSICAL_NOT_NULL
-- Evidence: PK
-- Evidence: Rows=1024
ALTER TABLE [dbo].[OSUSR_M_TEMPORALORDER]
    ALTER COLUMN [ID] int NOT NULL;
GO

-- Nullability dbo.OSUSR_M_TEMPORALORDER (STATUS) Category=Validation Risk=Low
-- Summary: Validated: Column is already NOT NULL and profiling confirms data integrity.
-- Rationale: DATA_NO_NULLS
-- Rationale: DEFAULT_PRESENT
-- Rationale: MANDATORY
-- Rationale: PHYSICAL_NOT_NULL
-- Evidence: DEFAULT_PRESENT
-- Evidence: MANDATORY
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=1024, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: PHYSICAL_NOT_NULL
-- Evidence: Rows=1024
ALTER TABLE [dbo].[OSUSR_M_TEMPORALORDER]
    ALTER COLUMN [STATUS] nvarchar NOT NULL;
GO

-- Nullability dbo.OSUSR_M_TEMPORALORDER (VALIDFROM) Category=Validation Risk=Low
-- Summary: Validated: Column is already NOT NULL and profiling confirms data integrity.
-- Rationale: DATA_NO_NULLS
-- Rationale: MANDATORY
-- Rationale: PHYSICAL_NOT_NULL
-- Evidence: MANDATORY
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=1024, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: PHYSICAL_NOT_NULL
-- Evidence: Rows=1024
ALTER TABLE [dbo].[OSUSR_M_TEMPORALORDER]
    ALTER COLUMN [VALIDFROM] datetime2 NOT NULL;
GO

-- Nullability dbo.OSUSR_M_TEMPORALORDER (VALIDTO) Category=Validation Risk=Low
-- Summary: Validated: Column is already NOT NULL and profiling confirms data integrity.
-- Rationale: DATA_NO_NULLS
-- Rationale: MANDATORY
-- Rationale: PHYSICAL_NOT_NULL
-- Evidence: MANDATORY
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=1024, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: PHYSICAL_NOT_NULL
-- Evidence: Rows=1024
ALTER TABLE [dbo].[OSUSR_M_TEMPORALORDER]
    ALTER COLUMN [VALIDTO] datetime2 NOT NULL;
GO

-- ---------- ForeignKey ----------

-- FOREIGN KEY VALIDATIONS
-- Why this matters: Confirms that existing foreign key constraints are working correctly.
-- What to do: No action needed - this validates your referential integrity is maintained.
-- ForeignKey dbo.OSUSR_M_TEMPORALORDER (FK_OSUSR_M_TEMPORALORDER_AUDITID_OSUSR_M_AUDIT) Category=Validation Risk=Low
-- Summary: Validated: Foreign key constraint already exists and profiling confirms referential integrity.
-- Rationale: DB_CONSTRAINT_PRESENT
-- Evidence: HasConstraint=True
-- Evidence: HasOrphans=False (Outcome=Succeeded, Sample=0, Captured=2024-01-01T00:00:00.0000000+00:00)
ALTER TABLE [dbo].[OSUSR_M_TEMPORALORDER] WITH CHECK ADD CONSTRAINT [FK_OSUSR_M_TEMPORALORDER_AUDITID_OSUSR_M_AUDIT] FOREIGN KEY ([AUDITID]) REFERENCES [dbo].[OSUSR_M_AUDIT] ([ID]);
ALTER TABLE [dbo].[OSUSR_M_TEMPORALORDER] CHECK CONSTRAINT [FK_OSUSR_M_TEMPORALORDER_AUDITID_OSUSR_M_AUDIT];
GO

