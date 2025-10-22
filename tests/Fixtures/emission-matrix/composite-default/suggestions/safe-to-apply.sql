-- NotNull dbo.OSUSR_A_ORDERALLOCATION (COUNTRYID) Risk=SafeToApply
-- Rationale: COMPOSITE_UNIQUE_NO_NULLS
-- Rationale: DATA_NO_NULLS
-- Rationale: MANDATORY
-- Evidence: Rows=1200
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=1200, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: COMPOSITE_UNIQUE_NO_NULLS
-- Evidence: MANDATORY
-- Evidence: DATA_NO_NULLS
ALTER TABLE [dbo].[OSUSR_A_ORDERALLOCATION]
    ALTER COLUMN [COUNTRYID] Identifier NOT NULL;
GO

-- NotNull dbo.OSUSR_A_ORDERALLOCATION (ID) Risk=SafeToApply
-- Rationale: DATA_NO_NULLS
-- Rationale: MANDATORY
-- Rationale: PHYSICAL_NOT_NULL
-- Rationale: PK
-- Evidence: Rows=1200
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=1200, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: PK
-- Evidence: PHYSICAL_NOT_NULL
-- Evidence: MANDATORY
-- Evidence: DATA_NO_NULLS
ALTER TABLE [dbo].[OSUSR_A_ORDERALLOCATION]
    ALTER COLUMN [ID] Identifier NOT NULL;
GO

-- NotNull dbo.OSUSR_A_ORDERALLOCATION (DOCUMENTID) Risk=SafeToApply
-- Rationale: COMPOSITE_UNIQUE_NO_NULLS
-- Rationale: DATA_NO_NULLS
-- Rationale: MANDATORY
-- Evidence: Rows=1200
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=1200, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: COMPOSITE_UNIQUE_NO_NULLS
-- Evidence: MANDATORY
-- Evidence: DATA_NO_NULLS
ALTER TABLE [dbo].[OSUSR_A_ORDERALLOCATION]
    ALTER COLUMN [DOCUMENTID] Identifier NOT NULL;
GO

-- Unique dbo.OSUSR_A_ORDERALLOCATION (UX_ORDERALLOC_COUNTRY_DOCUMENT) Risk=SafeToApply
-- Rationale: COMPOSITE_UNIQUE_NO_NULLS
-- Evidence: Composite duplicates=False
-- Evidence: Composite duplicates=False
CREATE UNIQUE NONCLUSTERED INDEX [UX_ORDERALLOC_COUNTRY_DOCUMENT] ON [dbo].[OSUSR_A_ORDERALLOCATION] ([COUNTRYID] ASC, [DOCUMENTID] ASC);
GO

