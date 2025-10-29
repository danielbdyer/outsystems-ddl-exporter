-- Nullability dbo.OSUSR_ABC_CUSTOMER (Email) Risk=Low
-- Rationale: Null probe succeeded.
-- Evidence: Rows=100
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=100, Captured=2024-01-01T00:00:00.0000000+00:00)
ALTER TABLE [dbo].[OSUSR_ABC_CUSTOMER]
    ALTER COLUMN [Email] NVARCHAR(255) NOT NULL;
GO

