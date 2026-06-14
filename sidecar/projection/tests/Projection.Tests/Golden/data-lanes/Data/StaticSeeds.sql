MERGE INTO [dbo].[GOLD_COUNTRY]
 AS [Target]
USING (VALUES (1, N'US', N'United States'), (2, N'CA', N'Canada'), (3, N'MX', N'Mexico')) AS [Source]([ID], [CODE], [LABEL]) ON [Target].[ID] = [Source].[ID]
WHEN MATCHED THEN UPDATE 
    SET [Target].[CODE]  = [Source].[CODE],
        [Target].[LABEL] = [Source].[LABEL]
WHEN NOT MATCHED THEN INSERT ([ID], [CODE], [LABEL]) VALUES ([Source].[ID], [Source].[CODE], [Source].[LABEL]);
GO
MERGE INTO [dbo].[GOLD_REGION_A]
 AS [Target]
USING (VALUES (1, N'North', NULL)) AS [Source]([ID], [NAME], [PARTNER_ID]) ON [Target].[ID] = [Source].[ID]
WHEN MATCHED THEN UPDATE 
    SET [Target].[NAME] = [Source].[NAME]
WHEN NOT MATCHED THEN INSERT ([ID], [NAME], [PARTNER_ID]) VALUES ([Source].[ID], [Source].[NAME], [Source].[PARTNER_ID]);
GO
MERGE INTO [dbo].[GOLD_REGION_B]
 AS [Target]
USING (VALUES (1, N'South', NULL)) AS [Source]([ID], [NAME], [PARTNER_ID]) ON [Target].[ID] = [Source].[ID]
WHEN MATCHED THEN UPDATE 
    SET [Target].[NAME] = [Source].[NAME]
WHEN NOT MATCHED THEN INSERT ([ID], [NAME], [PARTNER_ID]) VALUES ([Source].[ID], [Source].[NAME], [Source].[PARTNER_ID]);
GO
MERGE INTO [dbo].[GOLD_SCOPED_LOOKUP]
 AS [Target]
USING (VALUES (1, 42, N'Alpha'), (2, 42, N'Beta')) AS [Source]([ID], [TENANT_ID], [VALUE]) ON [Target].[ID] = [Source].[ID]
WHEN MATCHED THEN UPDATE 
    SET [Target].[TENANT_ID] = [Source].[TENANT_ID],
        [Target].[VALUE]     = [Source].[VALUE]
WHEN NOT MATCHED THEN INSERT ([ID], [TENANT_ID], [VALUE]) VALUES ([Source].[ID], [Source].[TENANT_ID], [Source].[VALUE]);
GO
SET IDENTITY_INSERT [dbo].[GOLD_TIER] ON;
MERGE INTO [dbo].[GOLD_TIER]
 AS [Target]
USING (VALUES (1, N'Bronze'), (2, N'Silver'), (3, N'Gold')) AS [Source]([ID], [NAME]) ON [Target].[ID] = [Source].[ID]
WHEN MATCHED THEN UPDATE 
    SET [Target].[NAME] = [Source].[NAME]
WHEN NOT MATCHED THEN INSERT ([ID], [NAME]) VALUES ([Source].[ID], [Source].[NAME]);
SET IDENTITY_INSERT [dbo].[GOLD_TIER] OFF;
GO
UPDATE  [dbo].[GOLD_REGION_A]
    SET [PARTNER_ID] = 1
WHERE   [ID] = 1;
GO
UPDATE  [dbo].[GOLD_REGION_B]
    SET [PARTNER_ID] = 1
WHERE   [ID] = 1;
GO
