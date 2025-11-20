--------------------------------------------------------------------------------
-- PostDeployment Bootstrap Script
-- Generated: 2025-11-19 17:49:07 UTC
-- Usage: Copy this file to your SSDT project's PostDeployment folder
--------------------------------------------------------------------------------

-- Guard: Only apply bootstrap snapshot on first deployment
IF NOT EXISTS (SELECT 1 FROM [dbo].[OSUSR_M_TEMPORALORDER])
BEGIN
    PRINT 'First deployment detected - applying bootstrap snapshot';
    PRINT 'Loading: Bootstrap/AllEntitiesIncludingStatic.bootstrap.sql';

    :r Bootstrap\AllEntitiesIncludingStatic.bootstrap.sql

    PRINT 'Bootstrap snapshot applied successfully (1 entities)';
END
ELSE
BEGIN
    PRINT 'Existing deployment detected - skipping bootstrap snapshot';
END
GO

--------------------------------------------------------------------------------
-- Baseline Seeds (Static Entities) - Applied on every deployment
--------------------------------------------------------------------------------
PRINT 'Applying baseline seeds (static entities)';

:r BaselineSeeds\Matrix\StaticEntities.seed.sql
PRINT 'Baseline seeds applied successfully';
GO
