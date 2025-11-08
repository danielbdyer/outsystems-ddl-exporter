-- ============================================================================
-- OutSystems DDL Exporter - Safe to Apply Opportunities
-- ============================================================================
-- Generated: 2024-01-01 00:00:00 UTC
--
-- SUMMARY:
--   Total Opportunities: 1
--   Validations: 1 (Existing constraints confirmed by profiling)
--
-- This script contains 1 safe to apply opportunities.
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
-- ---------- UniqueIndex ----------

-- UNIQUE INDEX VALIDATIONS
-- Why this matters: Confirms that existing unique constraints are working correctly.
-- What to do: No action needed - this validates your unique constraints are effective.
-- UniqueIndex dbo.OSUSR_U_USER (UX_USER_EMAIL) Category=Validation Risk=Low
-- Summary: Validated: Index is already UNIQUE and profiling confirms data integrity.
-- Rationale: PHYSICAL_UNIQUE_KEY
-- Rationale: UNIQUE_NO_NULLS
-- Evidence: Unique duplicates=False (Outcome=Succeeded, Sample=0, Captured=2024-01-01T00:00:00.0000000+00:00)
CREATE UNIQUE NONCLUSTERED INDEX [UX_USER_EMAIL] ON [dbo].[OSUSR_U_USER] ([EMAIL] ASC);
GO

