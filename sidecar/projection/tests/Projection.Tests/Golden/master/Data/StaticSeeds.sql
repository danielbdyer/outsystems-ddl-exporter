MERGE INTO [dbo].[Country]
 AS [Target]
USING (VALUES (2, N'CA', N'Canada'), (3, N'MX', N'Mexico'), (1, N'US', N'United States')) AS [Source]([Id], [Code], [Label]) ON [Target].[Id] = [Source].[Id]
WHEN MATCHED THEN UPDATE 
    SET [Target].[Code]  = [Source].[Code],
        [Target].[Label] = [Source].[Label]
WHEN NOT MATCHED THEN INSERT ([Id], [Code], [Label]) VALUES ([Source].[Id], [Source].[Code], [Source].[Label]);
GO
MERGE INTO [dbo].[RegionA]
 AS [Target]
USING (VALUES (1, N'North', NULL)) AS [Source]([Id], [Name], [PartnerId]) ON [Target].[Id] = [Source].[Id]
WHEN MATCHED THEN UPDATE 
    SET [Target].[Name] = [Source].[Name]
WHEN NOT MATCHED THEN INSERT ([Id], [Name], [PartnerId]) VALUES ([Source].[Id], [Source].[Name], [Source].[PartnerId]);
GO
MERGE INTO [dbo].[RegionB]
 AS [Target]
USING (VALUES (1, N'South', NULL)) AS [Source]([Id], [Name], [PartnerId]) ON [Target].[Id] = [Source].[Id]
WHEN MATCHED THEN UPDATE 
    SET [Target].[Name] = [Source].[Name]
WHEN NOT MATCHED THEN INSERT ([Id], [Name], [PartnerId]) VALUES ([Source].[Id], [Source].[Name], [Source].[PartnerId]);
GO
MERGE INTO [dbo].[ScopedLookup]
 AS [Target]
USING (VALUES (1, 42, N'Alpha'), (2, 42, N'Beta')) AS [Source]([Id], [TenantId], [Value]) ON [Target].[Id] = [Source].[Id]
WHEN MATCHED THEN UPDATE 
    SET [Target].[TenantId] = [Source].[TenantId],
        [Target].[Value]    = [Source].[Value]
WHEN NOT MATCHED THEN INSERT ([Id], [TenantId], [Value]) VALUES ([Source].[Id], [Source].[TenantId], [Source].[Value])
WHEN NOT MATCHED BY SOURCE AND [Target].[TenantId] = 42 THEN DELETE;
GO
SET IDENTITY_INSERT [dbo].[Tier] ON;
MERGE INTO [dbo].[Tier]
 AS [Target]
USING (VALUES (1, N'Bronze'), (3, N'Gold'), (2, N'Silver')) AS [Source]([Id], [Name]) ON [Target].[Id] = [Source].[Id]
WHEN MATCHED THEN UPDATE 
    SET [Target].[Name] = [Source].[Name]
WHEN NOT MATCHED THEN INSERT ([Id], [Name]) VALUES ([Source].[Id], [Source].[Name]);
SET IDENTITY_INSERT [dbo].[Tier] OFF;
GO
UPDATE  [dbo].[RegionA]
    SET [PartnerId] = 1
WHERE   [Id] = 1;
GO
UPDATE  [dbo].[RegionB]
    SET [PartnerId] = 1
WHERE   [Id] = 1;
GO
