-- Nullability dbo.OSUSR_OP_OPERATORPROFILE (ID) Risk=Low
-- Rationale: MANDATORY
-- Rationale: PK
-- Rationale: PROFILE_MISSING
-- Evidence: MANDATORY
-- Evidence: Null profile unavailable.
-- Evidence: PK
ALTER TABLE [dbo].[OSUSR_OP_OPERATORPROFILE]
    ALTER COLUMN [ID] Identifier NOT NULL;
GO

-- Nullability dbo.OSUSR_OP_OPERATORPROFILE (OPERATORCODE) Risk=Low
-- Rationale: MANDATORY
-- Rationale: PROFILE_MISSING
-- Evidence: MANDATORY
-- Evidence: Null profile unavailable.
ALTER TABLE [dbo].[OSUSR_OP_OPERATORPROFILE]
    ALTER COLUMN [OPERATORCODE] Text NOT NULL;
GO

