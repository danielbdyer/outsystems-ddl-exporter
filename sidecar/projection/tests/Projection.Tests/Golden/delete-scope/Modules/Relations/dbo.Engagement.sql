CREATE TABLE [dbo].[Engagement] (
    [AltCustomerId] INT            NULL
        DEFAULT 0
        CONSTRAINT [FK_Engagement_Customer_AltCustomerId]
            FOREIGN KEY ([AltCustomerId]) REFERENCES [dbo].[Customer] ([Id])
                ON DELETE SET NULL
                ON UPDATE NO ACTION,
    [CreatedBy]     INT            NOT NULL
        CONSTRAINT [FK_Engagement_User_CreatedBy]
            FOREIGN KEY ([CreatedBy]) REFERENCES [dbo].[User] ([Id])
                ON DELETE NO ACTION
                ON UPDATE CASCADE,
    [CustomerId]    INT            NOT NULL
        CONSTRAINT [FK_Engagement_Customer_CustomerId]
            FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id])
                ON DELETE CASCADE
                ON UPDATE NO ACTION,
    [Id]            INT            IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_dbo_Engagement]
            PRIMARY KEY CLUSTERED,
    [ParentId]      INT            NULL
        CONSTRAINT [FK_Engagement_Engagement_ParentId]
            FOREIGN KEY ([ParentId]) REFERENCES [dbo].[Engagement] ([Id]),
    [Subject]       NVARCHAR (200) NOT NULL,
    [UpdatedBy]     INT            NULL
        CONSTRAINT [FK_Engagement_User_UpdatedBy]
            FOREIGN KEY ([UpdatedBy]) REFERENCES [dbo].[User] ([Id])
)

GO

ALTER TABLE [dbo].[Engagement] NOCHECK CONSTRAINT [FK_Engagement_User_UpdatedBy]

GO

ALTER TABLE [dbo].[Engagement] WITH NOCHECK CHECK CONSTRAINT [FK_Engagement_User_UpdatedBy]

GO

CREATE INDEX [IX_Engagement_CreatedBy_UpdatedByDesc]
    ON [dbo].[Engagement]([CreatedBy], [UpdatedBy] DESC)

GO

CREATE UNIQUE INDEX [UIX_Engagement_CustomerId_Subject]
    ON [dbo].[Engagement]([CustomerId], [Subject])

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Engagement',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Engagement'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.SsKey', @value = N'S9:GOLD_KIND1:110:Engagement',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Engagement'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'AltCustomerId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Engagement',
    @level2type = N'COLUMN', @level2name = N'AltCustomerId'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'CreatedBy',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Engagement',
    @level2type = N'COLUMN', @level2name = N'CreatedBy'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'CustomerId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Engagement',
    @level2type = N'COLUMN', @level2name = N'CustomerId'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Engagement',
    @level2type = N'COLUMN', @level2name = N'Id'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'ParentId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Engagement',
    @level2type = N'COLUMN', @level2name = N'ParentId'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Subject',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Engagement',
    @level2type = N'COLUMN', @level2name = N'Subject'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'UpdatedBy',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Engagement',
    @level2type = N'COLUMN', @level2name = N'UpdatedBy'

