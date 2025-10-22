-- NotNull dbo.OSUSR_U_USER (EMAIL) Risk=SafeToApply
-- Rationale: DATA_NO_NULLS
-- Rationale: MANDATORY
-- Rationale: UNIQUE_NO_NULLS
-- Evidence: Rows=100
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=100, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: UNIQUE_NO_NULLS
-- Evidence: MANDATORY
-- Evidence: DATA_NO_NULLS
ALTER TABLE [dbo].[OSUSR_U_USER]
    ALTER COLUMN [EMAIL] Text NOT NULL;
GO

-- NotNull dbo.OSUSR_U_USER (ID) Risk=SafeToApply
-- Rationale: DATA_NO_NULLS
-- Rationale: MANDATORY
-- Rationale: PHYSICAL_NOT_NULL
-- Rationale: PK
-- Evidence: Rows=100
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=100, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: PK
-- Evidence: PHYSICAL_NOT_NULL
-- Evidence: MANDATORY
-- Evidence: DATA_NO_NULLS
ALTER TABLE [dbo].[OSUSR_U_USER]
    ALTER COLUMN [ID] Identifier NOT NULL;
GO

-- Unique dbo.OSUSR_U_USER (UX_USER_EMAIL) Risk=SafeToApply
-- Rationale: PHYSICAL_UNIQUE_KEY
-- Rationale: UNIQUE_NO_NULLS
-- Evidence: Unique duplicates=False (Outcome=Succeeded, Sample=0, Captured=2024-01-01T00:00:00.0000000+00:00)
CREATE UNIQUE NONCLUSTERED INDEX [UX_USER_EMAIL] ON [dbo].[OSUSR_U_USER] ([EMAIL] ASC);
GO

