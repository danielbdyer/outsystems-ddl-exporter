--------------------------------------------------------------------------------
-- Bootstrap Snapshot: Phased Loading Strategy (NULL→UPDATE)
-- Generated: 2025-12-17 03:19:32 UTC
-- Total Entities: 1
-- Strategy: Phased loading to resolve circular dependencies
--
-- Phase 1: INSERT with nullable FKs = NULL (mandatory-edge topological order)
-- Phase 2: UPDATE to populate nullable FK values after all tables exist
--
-- This eliminates the need for constraint disabling while respecting FK integrity.
--------------------------------------------------------------------------------

--------------------------------------------------------------------------------
-- PHASE 1: MERGE with nullable FKs = NULL
--------------------------------------------------------------------------------

-- MERGE: dbo.OSUSR_M_TEMPORALORDER
WITH SourceRows ([Id], [Status], [AuditId], [ValidFrom], [ValidTo]) AS
(
    VALUES
        (1, N'PENDING', 1, CAST('2025-01-01 00:00:00.0000000' AS datetime2(7)), CAST('9999-12-31 23:59:59.0000000' AS datetime2(7)))
)

MERGE INTO [dbo].[OSUSR_M_TEMPORALORDER] AS Target
USING SourceRows AS Source

    ON Target.[Id] = Source.[Id]
WHEN NOT MATCHED THEN INSERT (
    [Id], [Status], [AuditId], [ValidFrom], [ValidTo]
)
    VALUES (
    Source.[Id], Source.[Status], Source.[AuditId], Source.[ValidFrom], Source.[ValidTo]
);

GO


--------------------------------------------------------------------------------
-- Bootstrap Snapshot Complete: 1 entities loaded
-- Phased Loading: NOT REQUIRED
--------------------------------------------------------------------------------
