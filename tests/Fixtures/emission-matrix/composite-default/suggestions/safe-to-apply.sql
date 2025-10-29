-- Nullability dbo.OSUSR_A_ORDERALLOCATION (ID) Risk=Low
-- Rationale: DATA_NO_NULLS
-- Rationale: MANDATORY
-- Rationale: PHYSICAL_NOT_NULL
-- Rationale: PK
-- Evidence: MANDATORY
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=1200, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: PHYSICAL_NOT_NULL
-- Evidence: PK
-- Evidence: Rows=1200
ALTER TABLE [dbo].[OSUSR_A_ORDERALLOCATION]
    ALTER COLUMN [ID] Identifier NOT NULL;
GO

-- Nullability dbo.OSUSR_A_ORDERALLOCATION (COUNTRYID) Risk=Low
-- Rationale: COMPOSITE_UNIQUE_NO_NULLS
-- Rationale: DATA_NO_NULLS
-- Rationale: MANDATORY
-- Evidence: COMPOSITE_UNIQUE_NO_NULLS
-- Evidence: DATA_NO_NULLS
-- Evidence: MANDATORY
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=1200, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: Rows=1200
ALTER TABLE [dbo].[OSUSR_A_ORDERALLOCATION]
    ALTER COLUMN [COUNTRYID] Identifier NOT NULL;
GO

-- Nullability dbo.OSUSR_A_ORDERALLOCATION (DOCUMENTID) Risk=Low
-- Rationale: COMPOSITE_UNIQUE_NO_NULLS
-- Rationale: DATA_NO_NULLS
-- Rationale: MANDATORY
-- Evidence: COMPOSITE_UNIQUE_NO_NULLS
-- Evidence: DATA_NO_NULLS
-- Evidence: MANDATORY
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=1200, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: Rows=1200
ALTER TABLE [dbo].[OSUSR_A_ORDERALLOCATION]
    ALTER COLUMN [DOCUMENTID] Identifier NOT NULL;
GO

