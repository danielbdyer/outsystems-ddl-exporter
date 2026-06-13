MERGE INTO [dbo].[Country]
 AS [Target]
USING (VALUES (N'CA', 2, N'Canada'), (N'MX', 3, N'Mexico'), (N'US', 1, N'United States')) AS [Source]([Code], [Id], [Label]) ON [Target].[Id] = [Source].[Id]
WHEN MATCHED THEN UPDATE 
    SET [Target].[Code]  = [Source].[Code],
        [Target].[Label] = [Source].[Label]
WHEN NOT MATCHED THEN INSERT ([Code], [Id], [Label]) VALUES ([Source].[Code], [Source].[Id], [Source].[Label]);
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
UPDATE  [dbo].[RegionA]
    SET [PartnerId] = 1
WHERE   [Id] = 1;
GO
UPDATE  [dbo].[RegionB]
    SET [PartnerId] = 1
WHERE   [Id] = 1;
GO
