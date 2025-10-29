-- Nullability dbo.OSUSR_U_USER (EMAIL) Risk=Low
-- Rationale: DATA_NO_NULLS
-- Rationale: MANDATORY
-- Rationale: UNIQUE_NO_NULLS
-- Evidence: DATA_NO_NULLS
-- Evidence: MANDATORY
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=100, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: Rows=100
-- Evidence: UNIQUE_NO_NULLS
ALTER TABLE [dbo].[OSUSR_U_USER]
    ALTER COLUMN [EMAIL] Text NOT NULL;
GO

-- Nullability dbo.OSUSR_U_USER (ID) Risk=Low
-- Rationale: DATA_NO_NULLS
-- Rationale: MANDATORY
-- Rationale: PHYSICAL_NOT_NULL
-- Rationale: PK
-- Evidence: MANDATORY
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=100, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: PHYSICAL_NOT_NULL
-- Evidence: PK
-- Evidence: Rows=100
ALTER TABLE [dbo].[OSUSR_U_USER]
    ALTER COLUMN [ID] Identifier NOT NULL;
GO

