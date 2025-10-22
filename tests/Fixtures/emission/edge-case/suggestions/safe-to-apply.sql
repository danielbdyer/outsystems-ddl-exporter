-- NotNull dbo.OSUSR_DEF_CITY (ID) Risk=SafeToApply
-- Rationale: DATA_NO_NULLS
-- Rationale: MANDATORY
-- Rationale: PHYSICAL_NOT_NULL
-- Rationale: PK
-- Evidence: Rows=320
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=320, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: PK
-- Evidence: PHYSICAL_NOT_NULL
-- Evidence: MANDATORY
-- Evidence: DATA_NO_NULLS
ALTER TABLE [dbo.[OSUSR_DEF_CITY
    ALTER COLUMN [ID Identifier NOT NULL;
GO

-- NotNull dbo.OSUSR_DEF_CITY (ISACTIVE) Risk=SafeToApply
-- Rationale: DATA_NO_NULLS
-- Rationale: DEFAULT_PRESENT
-- Rationale: MANDATORY
-- Evidence: Rows=320
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=320, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: MANDATORY
-- Evidence: DATA_NO_NULLS
-- Evidence: DEFAULT_PRESENT
ALTER TABLE [dbo.[OSUSR_DEF_CITY
    ALTER COLUMN [ISACTIVE Boolean NOT NULL;
GO

-- NotNull dbo.OSUSR_DEF_CITY (NAME) Risk=SafeToApply
-- Rationale: DATA_NO_NULLS
-- Rationale: MANDATORY
-- Evidence: Rows=320
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=320, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: MANDATORY
-- Evidence: DATA_NO_NULLS
ALTER TABLE [dbo.[OSUSR_DEF_CITY
    ALTER COLUMN [NAME Text NOT NULL;
GO

-- NotNull billing.BILLING_ACCOUNT (ID) Risk=SafeToApply
-- Rationale: DATA_NO_NULLS
-- Rationale: MANDATORY
-- Rationale: PHYSICAL_NOT_NULL
-- Rationale: PK
-- Evidence: Rows=58000
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=58000, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: PK
-- Evidence: PHYSICAL_NOT_NULL
-- Evidence: MANDATORY
-- Evidence: DATA_NO_NULLS
ALTER TABLE [billing.[BILLING_ACCOUNT
    ALTER COLUMN [ID int NOT NULL;
GO

-- NotNull dbo.OSUSR_XYZ_JOBRUN (ID) Risk=SafeToApply
-- Rationale: DATA_NO_NULLS
-- Rationale: MANDATORY
-- Rationale: PHYSICAL_NOT_NULL
-- Rationale: PK
-- Evidence: Rows=2500000
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=2500000, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: PK
-- Evidence: PHYSICAL_NOT_NULL
-- Evidence: MANDATORY
-- Evidence: DATA_NO_NULLS
ALTER TABLE [dbo.[OSUSR_XYZ_JOBRUN
    ALTER COLUMN [ID Identifier NOT NULL;
GO

-- NotNull billing.BILLING_ACCOUNT (ACCOUNTNUMBER) Risk=SafeToApply
-- Rationale: DATA_NO_NULLS
-- Rationale: MANDATORY
-- Rationale: UNIQUE_NO_NULLS
-- Evidence: Rows=58000
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=58000, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: UNIQUE_NO_NULLS
-- Evidence: MANDATORY
-- Evidence: DATA_NO_NULLS
ALTER TABLE [billing.[BILLING_ACCOUNT
    ALTER COLUMN [ACCOUNTNUMBER varchar(50) NOT NULL;
GO

-- NotNull dbo.OSUSR_XYZ_JOBRUN (CREATEDON) Risk=SafeToApply
-- Rationale: DATA_NO_NULLS
-- Rationale: DEFAULT_PRESENT
-- Rationale: MANDATORY
-- Evidence: Rows=2500000
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=2500000, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: MANDATORY
-- Evidence: DATA_NO_NULLS
-- Evidence: DEFAULT_PRESENT
ALTER TABLE [dbo.[OSUSR_XYZ_JOBRUN
    ALTER COLUMN [CREATEDON DateTime NOT NULL;
GO

-- NotNull dbo.OSUSR_ABC_CUSTOMER (EMAIL) Risk=SafeToApply
-- Rationale: DATA_NO_NULLS
-- Rationale: MANDATORY
-- Rationale: UNIQUE_NO_NULLS
-- Evidence: Rows=125000
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=125000, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: UNIQUE_NO_NULLS
-- Evidence: MANDATORY
-- Evidence: DATA_NO_NULLS
ALTER TABLE [dbo.[OSUSR_ABC_CUSTOMER
    ALTER COLUMN [EMAIL nvarchar NOT NULL;
GO

-- NotNull dbo.OSUSR_ABC_CUSTOMER (CITYID) Risk=SafeToApply
-- Rationale: DATA_NO_NULLS
-- Rationale: FK_ENFORCED
-- Rationale: MANDATORY
-- Evidence: Rows=125000
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=125000, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: FK_ENFORCED
-- Evidence: MANDATORY
-- Evidence: DATA_NO_NULLS
ALTER TABLE [dbo.[OSUSR_ABC_CUSTOMER
    ALTER COLUMN [CITYID int NOT NULL;
GO

-- NotNull dbo.OSUSR_ABC_CUSTOMER (ID) Risk=SafeToApply
-- Rationale: DATA_NO_NULLS
-- Rationale: MANDATORY
-- Rationale: PHYSICAL_NOT_NULL
-- Rationale: PK
-- Evidence: Rows=125000
-- Evidence: Nulls=0 (Outcome=Succeeded, Sample=125000, Captured=2024-01-01T00:00:00.0000000+00:00)
-- Evidence: PK
-- Evidence: PHYSICAL_NOT_NULL
-- Evidence: MANDATORY
-- Evidence: DATA_NO_NULLS
ALTER TABLE [dbo.[OSUSR_ABC_CUSTOMER
    ALTER COLUMN [ID int NOT NULL;
GO

-- Unique billing.BILLING_ACCOUNT (IDX_BILLINGACCOUNT_ACCTNUM) Risk=SafeToApply
-- Rationale: PHYSICAL_UNIQUE_KEY
-- Rationale: UNIQUE_NO_NULLS
-- Evidence: Unique duplicates=False (Outcome=Succeeded, Sample=0, Captured=2024-01-01T00:00:00.0000000+00:00)
CREATE UNIQUE NONCLUSTERED INDEX [IDX_BILLINGACCOUNT_ACCTNUM ON [billing.[BILLING_ACCOUNT ([ACCOUNTNUMBER ASC);
GO

-- Unique dbo.OSUSR_ABC_CUSTOMER (IDX_CUSTOMER_EMAIL) Risk=SafeToApply
-- Rationale: PHYSICAL_UNIQUE_KEY
-- Rationale: UNIQUE_NO_NULLS
-- Evidence: Unique duplicates=False (Outcome=Succeeded, Sample=0, Captured=2024-01-01T00:00:00.0000000+00:00)
CREATE UNIQUE NONCLUSTERED INDEX [IDX_CUSTOMER_EMAIL ON [dbo.[OSUSR_ABC_CUSTOMER ([EMAIL ASC);
GO

-- ForeignKey dbo.OSUSR_ABC_CUSTOMER (CITYID) Risk=SafeToApply
-- Rationale: DB_CONSTRAINT_PRESENT
-- Evidence: HasConstraint=True
-- Evidence: HasOrphans=False (Outcome=Succeeded, Sample=0, Captured=2024-01-01T00:00:00.0000000+00:00)
ALTER TABLE [dbo.[OSUSR_ABC_CUSTOMER WITH CHECK ADD CONSTRAINT [FK_OSUSR_ABC_CUSTOMER_CITYID_OSUSR_DEF_CITY FOREIGN KEY ([CITYID) REFERENCES [dbo.[OSUSR_DEF_CITY ([ID);
ALTER TABLE [dbo.[OSUSR_ABC_CUSTOMER CHECK CONSTRAINT [FK_OSUSR_ABC_CUSTOMER_CITYID_OSUSR_DEF_CITY;
GO

