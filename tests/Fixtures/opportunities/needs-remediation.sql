-- ============================================================================
-- OutSystems DDL Exporter - Needs Remediation Opportunities
-- ============================================================================
-- Generated: 2024-01-01 00:00:00 UTC
--
-- SUMMARY:
--   Total Opportunities: 1
--   ⚠️  Contradictions: 1 (Data violates model expectations - REQUIRES MANUAL REMEDIATION)
--
-- This script contains 1 needs remediation opportunities.
--
-- ⚠️  WARNING: This script contains DATA CONTRADICTIONS that require manual remediation.
--              Do NOT execute these statements until the underlying data issues are resolved.
--
-- IMPORTANT: Never modify OutSystems model JSON files directly.
--            These scripts are suggestions only and will not auto-execute.
-- ============================================================================

-- ========== CONTRADICTION ==========

-- ⚠️  CONTRADICTIONS - MANUAL DATA REMEDIATION REQUIRED
--
-- These opportunities represent the MOST SEVERE issues where actual data in the database
-- contradicts what the OutSystems model expects. Examples include:
--   • NULL values in columns marked as Mandatory in the model
--   • Duplicate values in columns that should be unique
--   • Orphaned foreign key references (child records pointing to non-existent parents)
--
-- ACTION REQUIRED: You must manually clean the data BEFORE applying these constraints.
-- Attempting to add these constraints without fixing the data will result in SQL errors.
-- Review the evidence and remediation suggestions for each opportunity below.
-- ---------- UniqueIndex ----------

-- UNIQUE INDEX CONTRADICTIONS
-- Why this matters: Your OutSystems model expects unique values, but duplicates exist.
-- What to do: Identify and resolve duplicate records before adding unique constraints.
-- This may require merging records or updating values to ensure uniqueness.
-- UniqueIndex dbo.OSUSR_ABC_ORDER (IX_OSUSR_ABC_ORDER_OrderNumber) Category=Contradiction Risk=Moderate
-- Summary: Remediate data before enforcing the unique index.
-- Rationale: Duplicate values detected.
-- Evidence: Unique duplicates=True (Outcome=Succeeded, Sample=100, Captured=2024-01-01T00:00:00.0000000+00:00)
CREATE UNIQUE INDEX [IX_OSUSR_ABC_ORDER_OrderNumber] ON [dbo].[OSUSR_ABC_ORDER] ([OrderNumber]);
GO

