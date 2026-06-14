MERGE INTO [dbo].[GOLD_ASSIGNMENT]
 AS [Target]
USING (VALUES (1, 1, N'Role1'), (2, 2, N'Role2')) AS [Source]([PROJECT_ID], [RESOURCE_ID], [ROLE]) ON [Target].[PROJECT_ID] = [Source].[PROJECT_ID]
                                                                                                      AND [Target].[RESOURCE_ID] = [Source].[RESOURCE_ID]
WHEN MATCHED THEN UPDATE 
    SET [Target].[ROLE] = [Source].[ROLE]
WHEN NOT MATCHED THEN INSERT ([PROJECT_ID], [RESOURCE_ID], [ROLE]) VALUES ([Source].[PROJECT_ID], [Source].[RESOURCE_ID], [Source].[ROLE]);
GO
