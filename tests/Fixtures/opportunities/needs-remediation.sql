-- ============================================================================
-- OutSystems DDL Exporter - Needs Remediation Opportunities
-- ============================================================================
-- Generated: 2024-01-01 00:00:00 UTC
--
-- SUMMARY:
--   Total Opportunities: 3
--   ⚠️  Contradictions: 3 (Data violates model expectations - REQUIRES MANUAL REMEDIATION)
--
-- This script contains 3 needs remediation opportunities.
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
-- ---------- Nullability ----------

-- NOT NULL CONTRADICTIONS
-- Why this matters: Mandatory attributes contain NULL values that would violate a NOT NULL constraint.
-- What to do: Identify and update rows with NULL values before enforcing the NOT NULL constraint.
-- Nullability dbo.OSUSR_ABC_ORDER (DELIVERYDATE) Category=Contradiction Risk=Moderate
-- Summary: DATA CONTRADICTION: Profiling found NULL values that violate the model's mandatory constraint. Manual remediation required.
-- Rationale: DATA_HAS_NULLS
-- Rationale: MANDATORY
-- Evidence: Nulls=5 (Outcome=Succeeded, Sample=100, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: Rows=100
ALTER TABLE [dbo].[OSUSR_ABC_ORDER]
    ALTER COLUMN [DeliveryDate] DATETIME NOT NULL;
GO

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

-- ---------- ForeignKey ----------

-- FOREIGN KEY CONTRADICTIONS
-- Why this matters: Child rows reference parents that do not exist, breaking referential integrity.
-- What to do: Either remove or repair orphaned child rows before enabling the foreign key constraint.
-- ForeignKey dbo.OSUSR_ABC_ORDER (FK_OSUSR_ABC_ORDER_CUSTOMERID_OSUSR_DEF_CUSTOMER) Category=Contradiction Risk=High
-- Summary: DATA CONTRADICTION: Profiling found orphaned rows that violate referential integrity. Manual remediation required.
-- Foreign key state: No database constraint currently enforces this relationship.
-- Remediation steps:
--   1. Use the CLI orphan samples to query the child rows and confirm they lack parents.
--   2. Repair or backfill the offending child rows so every key maps to a valid parent.
--   3. Re-run build-ssdt; once orphan counts reach zero the FK moves into the safe scripts.
--
-- Rationale: DATA_HAS_ORPHANS
-- Evidence: HasConstraint=False
-- Evidence: ConstraintTrust=Missing
-- Evidence: HasOrphans=True (Outcome=Succeeded, Sample=100, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: OrphanCount=3
-- Evidence: OrphanSample=(101) -> 'MissingCustomer'
ALTER TABLE [dbo].[OSUSR_ABC_ORDER] WITH CHECK ADD CONSTRAINT [FK_OSUSR_ABC_ORDER_CUSTOMERID_OSUSR_DEF_CUSTOMER] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[OSUSR_DEF_CUSTOMER] ([Id]);
GO
ALTER TABLE [dbo].[OSUSR_ABC_ORDER] CHECK CONSTRAINT [FK_OSUSR_ABC_ORDER_CUSTOMERID_OSUSR_DEF_CUSTOMER];
GO
