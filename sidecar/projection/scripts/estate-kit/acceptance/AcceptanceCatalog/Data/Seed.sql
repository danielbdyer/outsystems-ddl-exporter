-- The acceptance seed: dirty in exactly the ways the proving loop gates on.
--   * two NULL Emails on a populated table  -> the make-mandatory block's carrier
--   * every RegionId pointing at a real Region -> the FK-add validates TRUSTED
MERGE INTO [dbo].[Status] AS t
USING (VALUES (1, N'Open'), (2, N'Closed'), (3, N'Pending')) AS s ([Id], [Name])
ON t.[Id] = s.[Id]
WHEN MATCHED AND t.[Name] <> s.[Name] THEN UPDATE SET [Name] = s.[Name]
WHEN NOT MATCHED BY TARGET THEN INSERT ([Id], [Name]) VALUES (s.[Id], s.[Name]);

MERGE INTO [dbo].[Region] AS t
USING (VALUES (1, N'North'), (2, N'South'), (3, N'East')) AS s ([Id], [Name])
ON t.[Id] = s.[Id]
WHEN MATCHED AND t.[Name] <> s.[Name] THEN UPDATE SET [Name] = s.[Name]
WHEN NOT MATCHED BY TARGET THEN INSERT ([Id], [Name]) VALUES (s.[Id], s.[Name]);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Customer])
BEGIN
    INSERT INTO [dbo].[Customer] ([Name], [Email], [StatusId], [RegionId], [Score])
    VALUES (N'Acceptance One',   N'one@acceptance.test',  1, 1, 10),
           (N'Acceptance Two',   NULL,                    2, 2, 20),
           (N'Acceptance Three', N'three@acceptance.test',3, 3, 30),
           (N'Acceptance Four',  NULL,                    1, 1, 40),
           (N'Acceptance Five',  N'five@acceptance.test', 2, 2, 50),
           (N'Acceptance Six',   N'six@acceptance.test',  3, 3, 60);
END;
