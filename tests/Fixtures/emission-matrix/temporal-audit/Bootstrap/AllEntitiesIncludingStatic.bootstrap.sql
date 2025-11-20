--------------------------------------------------------------------------------
-- Bootstrap Snapshot: All Entities (Static + Regular)
-- Generated: 2025-11-19 17:49:07 UTC
-- Total Entities: 1
-- Ordering: Alphabetical fallback
-- Mode: alphabetical
--
-- USAGE: This file is applied ONCE on first SSDT deployment via PostDeployment guard.
--        Do NOT commit to source control (add Bootstrap/ to .gitignore).
--------------------------------------------------------------------------------

-- Entity: TemporalOrder (dbo.OSUSR_M_TEMPORALORDER)
-- Module: Matrix
-- Topological Order: 1 of 1

--------------------------------------------------------------------------------
-- Module: Matrix
-- Entity: TemporalOrder (dbo.OSUSR_M_TEMPORALORDER)
-- Target: dbo.TemporalOrder
--------------------------------------------------------------------------------
IF EXISTS (
    SELECT Source.[Id], Source.[Status], Source.[AuditId], Source.[ValidFrom], Source.[ValidTo]
    FROM
    (
        VALUES
            (1, N'PENDING', 1, CAST('2025-01-01 00:00:00.0000000' AS datetime2(7)), CAST('9999-12-31 23:59:59.0000000' AS datetime2(7)))
    ) AS Source ([Id], [Status], [AuditId], [ValidFrom], [ValidTo])
    EXCEPT
    SELECT Existing.[Id], Existing.[Status], Existing.[AuditId], Existing.[ValidFrom], Existing.[ValidTo]
    FROM [dbo].[TemporalOrder] AS Existing
)
    OR EXISTS (
    SELECT Existing.[Id], Existing.[Status], Existing.[AuditId], Existing.[ValidFrom], Existing.[ValidTo]
    FROM [dbo].[TemporalOrder] AS Existing
    EXCEPT
    SELECT Source.[Id], Source.[Status], Source.[AuditId], Source.[ValidFrom], Source.[ValidTo]
    FROM
    (
        VALUES
            (1, N'PENDING', 1, CAST('2025-01-01 00:00:00.0000000' AS datetime2(7)), CAST('9999-12-31 23:59:59.0000000' AS datetime2(7)))
    ) AS Source ([Id], [Status], [AuditId], [ValidFrom], [ValidTo])
)
BEGIN
    THROW 50000, 'Static entity seed data drift detected for Matrix::TemporalOrder (dbo.TemporalOrder).', 1;
END;

MERGE INTO [dbo].[TemporalOrder] AS Target
USING
(
    VALUES
        (1, N'PENDING', 1, CAST('2025-01-01 00:00:00.0000000' AS datetime2(7)), CAST('9999-12-31 23:59:59.0000000' AS datetime2(7)))
) AS Source ([Id], [Status], [AuditId], [ValidFrom], [ValidTo])
    ON Target.[Id] = Source.[Id]
WHEN MATCHED THEN UPDATE SET
    Target.[Status] = Source.[Status],
    Target.[AuditId] = Source.[AuditId],
    Target.[ValidFrom] = Source.[ValidFrom],
    Target.[ValidTo] = Source.[ValidTo]
WHEN NOT MATCHED THEN INSERT ([Id], [Status], [AuditId], [ValidFrom], [ValidTo])
    VALUES (Source.[Id], Source.[Status], Source.[AuditId], Source.[ValidFrom], Source.[ValidTo]);

GO

PRINT 'Bootstrap: Completed entity 1/1: dbo.OSUSR_M_TEMPORALORDER (1 rows)';
GO

--------------------------------------------------------------------------------
-- Bootstrap Snapshot Complete: 1 entities loaded
--------------------------------------------------------------------------------
