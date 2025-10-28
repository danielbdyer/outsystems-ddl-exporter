-- NotNull dbo.OSUSR_M_TEMPORALORDER (VALIDFROM) Risk=SafeToApply
-- Rationale: DATA_NO_NULLS
-- Rationale: MANDATORY
-- Rationale: PHYSICAL_NOT_NULL
-- Evidence: Rows=1024
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=1024, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: PHYSICAL_NOT_NULL
-- Evidence: MANDATORY
-- Evidence: DATA_NO_NULLS
ALTER TABLE [dbo].[OSUSR_M_TEMPORALORDER]
    ALTER COLUMN [VALIDFROM] datetime2 NOT NULL;
GO

-- NotNull dbo.OSUSR_M_AUDIT (ID) Risk=SafeToApply
-- Rationale: DATA_NO_NULLS
-- Rationale: MANDATORY
-- Rationale: PHYSICAL_NOT_NULL
-- Rationale: PK
-- Evidence: Rows=250
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=250, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: PK
-- Evidence: PHYSICAL_NOT_NULL
-- Evidence: MANDATORY
-- Evidence: DATA_NO_NULLS
ALTER TABLE [dbo].[OSUSR_M_AUDIT]
    ALTER COLUMN [ID] int NOT NULL;
GO

-- NotNull dbo.OSUSR_M_TEMPORALORDER (VALIDTO) Risk=SafeToApply
-- Rationale: DATA_NO_NULLS
-- Rationale: MANDATORY
-- Rationale: PHYSICAL_NOT_NULL
-- Evidence: Rows=1024
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=1024, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: PHYSICAL_NOT_NULL
-- Evidence: MANDATORY
-- Evidence: DATA_NO_NULLS
ALTER TABLE [dbo].[OSUSR_M_TEMPORALORDER]
    ALTER COLUMN [VALIDTO] datetime2 NOT NULL;
GO

-- NotNull dbo.OSUSR_M_TEMPORALORDER (STATUS) Risk=SafeToApply
-- Rationale: DATA_NO_NULLS
-- Rationale: DEFAULT_PRESENT
-- Rationale: MANDATORY
-- Rationale: PHYSICAL_NOT_NULL
-- Evidence: Rows=1024
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=1024, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: PHYSICAL_NOT_NULL
-- Evidence: MANDATORY
-- Evidence: DATA_NO_NULLS
-- Evidence: DEFAULT_PRESENT
ALTER TABLE [dbo].[OSUSR_M_TEMPORALORDER]
    ALTER COLUMN [STATUS] nvarchar NOT NULL;
GO

-- NotNull dbo.OSUSR_M_TEMPORALORDER (ID) Risk=SafeToApply
-- Rationale: DATA_NO_NULLS
-- Rationale: MANDATORY
-- Rationale: PHYSICAL_NOT_NULL
-- Rationale: PK
-- Evidence: Rows=1024
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=1024, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: PK
-- Evidence: PHYSICAL_NOT_NULL
-- Evidence: MANDATORY
-- Evidence: DATA_NO_NULLS
ALTER TABLE [dbo].[OSUSR_M_TEMPORALORDER]
    ALTER COLUMN [ID] int NOT NULL;
GO

-- ForeignKey dbo.OSUSR_M_TEMPORALORDER (AUDITID) Risk=SafeToApply
-- Rationale: DB_CONSTRAINT_PRESENT
-- Evidence: HasConstraint=True
-- Evidence: HasOrphans=False (Outcome=Succeeded, Sample=0, Captured=2024-01-01T00:00:00.0000000+00:00)
ALTER TABLE [dbo].[OSUSR_M_TEMPORALORDER] WITH CHECK ADD CONSTRAINT [FK_OSUSR_M_TEMPORALORDER_OSUSR_M_AUDIT_AUDITID] FOREIGN KEY ([AUDITID]) REFERENCES [dbo].[OSUSR_M_AUDIT] ([ID]);
ALTER TABLE [dbo].[OSUSR_M_TEMPORALORDER] CHECK CONSTRAINT [FK_OSUSR_M_TEMPORALORDER_OSUSR_M_AUDIT_AUDITID];
GO

