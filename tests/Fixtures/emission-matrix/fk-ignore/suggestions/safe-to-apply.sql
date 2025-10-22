-- NotNull dbo.OSUSR_A (ID) Risk=SafeToApply
-- Rationale: DATA_NO_NULLS
-- Rationale: MANDATORY
-- Rationale: PHYSICAL_NOT_NULL
-- Rationale: PK
-- Evidence: Rows=200
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=200, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: PK
-- Evidence: PHYSICAL_NOT_NULL
-- Evidence: MANDATORY
-- Evidence: DATA_NO_NULLS
ALTER TABLE [dbo].[OSUSR_A]
    ALTER COLUMN [ID] Identifier NOT NULL;
GO

-- NotNull dbo.OSUSR_B (ID) Risk=SafeToApply
-- Rationale: DATA_NO_NULLS
-- Rationale: MANDATORY
-- Rationale: PHYSICAL_NOT_NULL
-- Rationale: PK
-- Evidence: Rows=4000
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=4000, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: PK
-- Evidence: PHYSICAL_NOT_NULL
-- Evidence: MANDATORY
-- Evidence: DATA_NO_NULLS
ALTER TABLE [dbo].[OSUSR_B]
    ALTER COLUMN [ID] Identifier NOT NULL;
GO

