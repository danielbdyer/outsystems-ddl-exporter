SET IDENTITY_INSERT [dbo].[GOLD_CUSTOMER] ON;
MERGE INTO [dbo].[GOLD_CUSTOMER]
 AS [Target]
USING (VALUES (1, N'Customer1'), (2, N'Customer2')) AS [Source]([ID], [NAME]) ON [Target].[ID] = [Source].[ID]
WHEN MATCHED THEN UPDATE 
    SET [Target].[NAME] = [Source].[NAME]
WHEN NOT MATCHED THEN INSERT ([ID], [NAME]) VALUES ([Source].[ID], [Source].[NAME]);
SET IDENTITY_INSERT [dbo].[GOLD_CUSTOMER] OFF;
GO
