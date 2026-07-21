CREATE SCHEMA [audit]

GO

CREATE TABLE [dbo].[Assignment] (
    [ProjectId]  INT           NOT NULL,
    [ResourceId] INT           NOT NULL,
    [Role]       NVARCHAR (40) NULL,
    CONSTRAINT [PK_Assignment_ProjectId_ResourceId]
        PRIMARY KEY ([ProjectId], [ResourceId])
)

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Assignment',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Assignment';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_KIND1:110:Assignment',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Assignment';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'ProjectId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Assignment',
    @level2type = N'COLUMN', @level2name = N'ProjectId';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:120:Assignment.ProjectId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Assignment',
    @level2type = N'COLUMN', @level2name = N'ProjectId';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'ResourceId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Assignment',
    @level2type = N'COLUMN', @level2name = N'ResourceId';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:121:Assignment.ResourceId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Assignment',
    @level2type = N'COLUMN', @level2name = N'ResourceId';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Role',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Assignment',
    @level2type = N'COLUMN', @level2name = N'Role';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:115:Assignment.Role',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Assignment',
    @level2type = N'COLUMN', @level2name = N'Role';

GO

CREATE TABLE [audit].[ChangeLog] (
    [At]     DATETIME NOT NULL,
    [Id]     INT      IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_ChangeLog_Id]
            PRIMARY KEY CLUSTERED,
    [UserId] INT      NOT NULL
        CONSTRAINT [FK_ChangeLog_User_UserId]
            FOREIGN KEY ([UserId]) REFERENCES [dbo].[User] ([Id])
)

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'ChangeLog',
    @level0type = N'SCHEMA', @level0name = N'audit',
    @level1type = N'TABLE', @level1name = N'ChangeLog';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_KIND1:19:ChangeLog',
    @level0type = N'SCHEMA', @level0name = N'audit',
    @level1type = N'TABLE', @level1name = N'ChangeLog';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'At',
    @level0type = N'SCHEMA', @level0name = N'audit',
    @level1type = N'TABLE', @level1name = N'ChangeLog',
    @level2type = N'COLUMN', @level2name = N'At';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:112:ChangeLog.At',
    @level0type = N'SCHEMA', @level0name = N'audit',
    @level1type = N'TABLE', @level1name = N'ChangeLog',
    @level2type = N'COLUMN', @level2name = N'At';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'audit',
    @level1type = N'TABLE', @level1name = N'ChangeLog',
    @level2type = N'COLUMN', @level2name = N'Id';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:112:ChangeLog.Id',
    @level0type = N'SCHEMA', @level0name = N'audit',
    @level1type = N'TABLE', @level1name = N'ChangeLog',
    @level2type = N'COLUMN', @level2name = N'Id';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'UserId',
    @level0type = N'SCHEMA', @level0name = N'audit',
    @level1type = N'TABLE', @level1name = N'ChangeLog',
    @level2type = N'COLUMN', @level2name = N'UserId';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:116:ChangeLog.UserId',
    @level0type = N'SCHEMA', @level0name = N'audit',
    @level1type = N'TABLE', @level1name = N'ChangeLog',
    @level2type = N'COLUMN', @level2name = N'UserId';

GO

CREATE TABLE [dbo].[Country] (
    [Code]  NVARCHAR (2)   NOT NULL,
    [Id]    INT            NOT NULL
        CONSTRAINT [PK_Country_Id]
            PRIMARY KEY CLUSTERED,
    [Label] NVARCHAR (100) NOT NULL
)

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Country',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Country';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_KIND1:17:Country',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Country';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Code',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Country',
    @level2type = N'COLUMN', @level2name = N'Code';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:112:Country.Code',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Country',
    @level2type = N'COLUMN', @level2name = N'Code';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Country',
    @level2type = N'COLUMN', @level2name = N'Id';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:110:Country.Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Country',
    @level2type = N'COLUMN', @level2name = N'Id';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Label',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Country',
    @level2type = N'COLUMN', @level2name = N'Label';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:113:Country.Label',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Country',
    @level2type = N'COLUMN', @level2name = N'Label';

GO

CREATE TABLE [dbo].[Customer] (
    [Id]   INT            IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_Customer_Id]
            PRIMARY KEY CLUSTERED,
    [Name] NVARCHAR (120) NOT NULL
)

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Customer',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Customer';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_KIND1:18:Customer',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Customer';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Customer',
    @level2type = N'COLUMN', @level2name = N'Id';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:111:Customer.Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Customer',
    @level2type = N'COLUMN', @level2name = N'Id';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Name',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Customer',
    @level2type = N'COLUMN', @level2name = N'Name';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:113:Customer.Name',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Customer',
    @level2type = N'COLUMN', @level2name = N'Name';

GO

CREATE TABLE [dbo].[EnterpriseCustomerRelationshipManagementProfileSnapshot] (
    [Id] INT IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_EnterpriseCustomerRelationshipManagementProfileSnapshot_Id]
            PRIMARY KEY CLUSTERED
)

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'EnterpriseCustomerRelationshipManagementProfileSnapshot',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'EnterpriseCustomerRelationshipManagementProfileSnapshot';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_KIND1:112:EcrmSnapshot',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'EnterpriseCustomerRelationshipManagementProfileSnapshot';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'EnterpriseCustomerRelationshipManagementProfileSnapshot',
    @level2type = N'COLUMN', @level2name = N'Id';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:115:EcrmSnapshot.Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'EnterpriseCustomerRelationshipManagementProfileSnapshot',
    @level2type = N'COLUMN', @level2name = N'Id';

GO

CREATE TABLE [dbo].[Engagement] (
    [AltCustomerId] INT            NULL
        CONSTRAINT [DF_Engagement_AltCustomerId] DEFAULT 0,
    [CreatedBy]     INT            NOT NULL
        CONSTRAINT [FK_Engagement_User_CreatedBy]
            FOREIGN KEY ([CreatedBy]) REFERENCES [dbo].[User] ([Id])
                ON DELETE NO ACTION
                ON UPDATE CASCADE,
    [CustomerId]    INT            NOT NULL,
    [Id]            INT            IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_Engagement_Id]
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

CREATE INDEX [IX_Engagement_CreatedBy_UpdatedBy]
    ON [dbo].[Engagement]([CreatedBy], [UpdatedBy] DESC)

GO

CREATE UNIQUE INDEX [UIX_Engagement_CustomerId_Subject]
    ON [dbo].[Engagement]([CustomerId], [Subject])

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Engagement',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Engagement';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_KIND1:110:Engagement',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Engagement';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'AltCustomerId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Engagement',
    @level2type = N'COLUMN', @level2name = N'AltCustomerId';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:124:Engagement.AltCustomerId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Engagement',
    @level2type = N'COLUMN', @level2name = N'AltCustomerId';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'CreatedBy',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Engagement',
    @level2type = N'COLUMN', @level2name = N'CreatedBy';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:120:Engagement.CreatedBy',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Engagement',
    @level2type = N'COLUMN', @level2name = N'CreatedBy';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'CustomerId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Engagement',
    @level2type = N'COLUMN', @level2name = N'CustomerId';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:121:Engagement.CustomerId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Engagement',
    @level2type = N'COLUMN', @level2name = N'CustomerId';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Engagement',
    @level2type = N'COLUMN', @level2name = N'Id';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:113:Engagement.Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Engagement',
    @level2type = N'COLUMN', @level2name = N'Id';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'ParentId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Engagement',
    @level2type = N'COLUMN', @level2name = N'ParentId';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:119:Engagement.ParentId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Engagement',
    @level2type = N'COLUMN', @level2name = N'ParentId';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Subject',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Engagement',
    @level2type = N'COLUMN', @level2name = N'Subject';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:118:Engagement.Subject',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Engagement',
    @level2type = N'COLUMN', @level2name = N'Subject';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'UpdatedBy',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Engagement',
    @level2type = N'COLUMN', @level2name = N'UpdatedBy';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:120:Engagement.UpdatedBy',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Engagement',
    @level2type = N'COLUMN', @level2name = N'UpdatedBy';

GO

CREATE TABLE [dbo].[Heap] (
    [LoggedAt] DATETIME       NOT NULL,
    [Message]  NVARCHAR (500) NULL
)

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Heap',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Heap';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_KIND1:14:Heap',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Heap';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'LoggedAt',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Heap',
    @level2type = N'COLUMN', @level2name = N'LoggedAt';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:113:Heap.LoggedAt',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Heap',
    @level2type = N'COLUMN', @level2name = N'LoggedAt';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Message',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Heap',
    @level2type = N'COLUMN', @level2name = N'Message';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:112:Heap.Message',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Heap',
    @level2type = N'COLUMN', @level2name = N'Message';

GO

CREATE TABLE [dbo].[InterdepartmentalResourceAllocationAuthorizationLedger] (
    [Id]                                                        INT IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_InterdepartmentalResourceAllocationAuthorizationLedger_Id]
            PRIMARY KEY CLUSTERED,
    [PrimaryResponsibleEnterpriseCustomerRelationshipManagerId] INT NOT NULL
        CONSTRAINT [FK_InterdepartmentalResourceAllocationAuthorizationLedger_EnterpriseCustomerRelationshipManagementProfileSnapshot_P_b94286649f49]
            FOREIGN KEY ([PrimaryResponsibleEnterpriseCustomerRelationshipManagerId]) REFERENCES [dbo].[EnterpriseCustomerRelationshipManagementProfileSnapshot] ([Id])
)

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'InterdepartmentalResourceAllocationAuthorizationLedger',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'InterdepartmentalResourceAllocationAuthorizationLedger';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_KIND1:16:Ledger',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'InterdepartmentalResourceAllocationAuthorizationLedger';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'InterdepartmentalResourceAllocationAuthorizationLedger',
    @level2type = N'COLUMN', @level2name = N'Id';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:19:Ledger.Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'InterdepartmentalResourceAllocationAuthorizationLedger',
    @level2type = N'COLUMN', @level2name = N'Id';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'PrimaryResponsibleEnterpriseCustomerRelationshipManagerId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'InterdepartmentalResourceAllocationAuthorizationLedger',
    @level2type = N'COLUMN', @level2name = N'PrimaryResponsibleEnterpriseCustomerRelationshipManagerId';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:116:Ledger.ManagerId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'InterdepartmentalResourceAllocationAuthorizationLedger',
    @level2type = N'COLUMN', @level2name = N'PrimaryResponsibleEnterpriseCustomerRelationshipManagerId';

GO

CREATE TABLE [dbo].[RegionA] (
    [Id]        INT           NOT NULL
        CONSTRAINT [PK_RegionA_Id]
            PRIMARY KEY CLUSTERED,
    [Name]      NVARCHAR (60) NOT NULL,
    [PartnerId] INT           NULL
)

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'RegionA',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionA';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_KIND1:17:RegionA',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionA';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionA',
    @level2type = N'COLUMN', @level2name = N'Id';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:110:RegionA.Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionA',
    @level2type = N'COLUMN', @level2name = N'Id';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Name',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionA',
    @level2type = N'COLUMN', @level2name = N'Name';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:112:RegionA.Name',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionA',
    @level2type = N'COLUMN', @level2name = N'Name';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'PartnerId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionA',
    @level2type = N'COLUMN', @level2name = N'PartnerId';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:117:RegionA.PartnerId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionA',
    @level2type = N'COLUMN', @level2name = N'PartnerId';

GO

CREATE TABLE [dbo].[RegionB] (
    [Id]        INT           NOT NULL
        CONSTRAINT [PK_RegionB_Id]
            PRIMARY KEY CLUSTERED,
    [Name]      NVARCHAR (60) NOT NULL,
    [PartnerId] INT           NULL
)

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'RegionB',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionB';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_KIND1:17:RegionB',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionB';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionB',
    @level2type = N'COLUMN', @level2name = N'Id';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:110:RegionB.Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionB',
    @level2type = N'COLUMN', @level2name = N'Id';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Name',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionB',
    @level2type = N'COLUMN', @level2name = N'Name';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:112:RegionB.Name',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionB',
    @level2type = N'COLUMN', @level2name = N'Name';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'PartnerId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionB',
    @level2type = N'COLUMN', @level2name = N'PartnerId';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:117:RegionB.PartnerId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'RegionB',
    @level2type = N'COLUMN', @level2name = N'PartnerId';

GO

CREATE TABLE [dbo].[ScalarGallery] (
    [AlarmAt]     TIME             NULL
        CONSTRAINT [DF_ScalarGallery_AlarmAt] DEFAULT CAST ('08:30:00' AS TIME (7)),
    [Amount]      DECIMAL (18, 4)  NULL
        CONSTRAINT [DF_ScalarGallery_Amount] DEFAULT 3.1400
        CHECK (([Amount] <= (1000000.0000))),
    [Code]        NVARCHAR (20)    NOT NULL
        CONSTRAINT [DF_ScalarGallery_Code] DEFAULT N'Pending',
    [DueDate]     DATE             NULL
        CONSTRAINT [DF_ScalarGallery_DueDate] DEFAULT CAST ('2020-01-01' AS DATE),
    [ExternalKey] UNIQUEIDENTIFIER NULL
        CONSTRAINT [DF_ScalarGallery_ExternalKey] DEFAULT '00000000-0000-0000-0000-000000000000',
    [FreeText]    NVARCHAR (50)    NULL,
    [Id]          INT              IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_ScalarGallery_Id]
            PRIMARY KEY CLUSTERED,
    [IsActive]    BIT              NOT NULL
        CONSTRAINT [DF_ScalarGallery_IsActive] DEFAULT 1,
    [Notes]       NVARCHAR (2000)  NULL
        CONSTRAINT [DF_ScalarGallery_Notes] DEFAULT N'',
    [OccurredOn]  DATETIME         NULL
        CONSTRAINT [DF_ScalarGallery_OccurredOn] DEFAULT CAST ('2020-01-01 00:00:00' AS DATETIME2 (7)),
    [Payload]     VARBINARY (512)  NULL
        CONSTRAINT [DF_ScalarGallery_Payload] DEFAULT 0x00,
    [Tally]       INT              NULL
        CONSTRAINT [DF_ScalarGallery_Tally] DEFAULT 42
        CONSTRAINT [CK_ScalarGallery_Tally] CHECK (([Tally] >= (0))),
    CONSTRAINT [CK_ScalarGallery_TallyWithinAmount]
        CHECK (([Tally] <= [Amount]))
)

GO

CREATE INDEX [IX_ScalarGallery_Code_1]
    ON [dbo].[ScalarGallery]([Code])
    INCLUDE([Amount])

GO

CREATE INDEX [IX_ScalarGallery_Code_2]
    ON [dbo].[ScalarGallery]([Code])

GO

CREATE INDEX [IX_ScalarGallery_Code_3]
    ON [dbo].[ScalarGallery]([Code])

GO

CREATE INDEX [IX_ScalarGallery_Tally_1]
    ON [dbo].[ScalarGallery]([Tally] DESC)

GO

CREATE INDEX [IX_ScalarGallery_Tally_2]
    ON [dbo].[ScalarGallery]([Tally]) WHERE ([Tally] IS NOT NULL)

GO

CREATE INDEX [IX_ScalarGallery_Tally_3]
    ON [dbo].[ScalarGallery]([Tally])

GO

CREATE UNIQUE INDEX [UIX_ScalarGallery_Amount]
    ON [dbo].[ScalarGallery]([Amount]) WITH (FILLFACTOR = 80, PAD_INDEX = ON, IGNORE_DUP_KEY = ON, DATA_COMPRESSION = PAGE)

GO

CREATE UNIQUE INDEX [UIX_ScalarGallery_Code]
    ON [dbo].[ScalarGallery]([Code])

GO

ALTER INDEX [IX_ScalarGallery_Code_2]
    ON [dbo].[ScalarGallery] DISABLE

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'MS_Description',
    @value = N'The scalar gallery: every primitive realization and every DEFAULT-able literal.',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'ScalarGallery',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_KIND1:113:ScalarGallery',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'AlarmAt',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'AlarmAt';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:121:ScalarGallery.AlarmAt',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'AlarmAt';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Amount',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'Amount';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:120:ScalarGallery.Amount',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'Amount';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'MS_Description',
    @value = N'Workflow code; defaults to Pending.',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'Code';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Code',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'Code';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:118:ScalarGallery.Code',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'Code';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'DueDate',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'DueDate';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:121:ScalarGallery.DueDate',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'DueDate';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'ExternalKey',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'ExternalKey';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:125:ScalarGallery.ExternalKey',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'ExternalKey';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'MS_Description',
    @value = N'No default; the contrast column.',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'FreeText';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'FreeText',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'FreeText';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:122:ScalarGallery.FreeText',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'FreeText';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'Id';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:116:ScalarGallery.Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'Id';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'IsActive',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'IsActive';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:122:ScalarGallery.IsActive',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'IsActive';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Notes',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'Notes';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:119:ScalarGallery.Notes',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'Notes';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'OccurredOn',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'OccurredOn';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:124:ScalarGallery.OccurredOn',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'OccurredOn';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Payload',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'Payload';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:121:ScalarGallery.Payload',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'Payload';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Tally',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'Tally';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:119:ScalarGallery.Tally',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'COLUMN', @level2name = N'Tally';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'MS_Description',
    @value = N'Descending scan support.',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScalarGallery',
    @level2type = N'INDEX', @level2name = N'IX_ScalarGallery_Tally_1';

GO

-- Trigger: TRG_ScalarGallery_Audit (disabled: false)

GO

CREATE TRIGGER [dbo].[TRG_ScalarGallery_Audit]
    ON [dbo].[ScalarGallery]
    AFTER INSERT
    AS BEGIN
           SET NOCOUNT ON;
       END

GO

CREATE TABLE [dbo].[ScopedLookup] (
    [Id]       INT           NOT NULL
        CONSTRAINT [PK_ScopedLookup_Id]
            PRIMARY KEY CLUSTERED,
    [TenantId] INT           NOT NULL,
    [Value]    NVARCHAR (80) NOT NULL
)

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'ScopedLookup',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScopedLookup';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_KIND1:112:ScopedLookup',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScopedLookup';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScopedLookup',
    @level2type = N'COLUMN', @level2name = N'Id';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:115:ScopedLookup.Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScopedLookup',
    @level2type = N'COLUMN', @level2name = N'Id';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'TenantId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScopedLookup',
    @level2type = N'COLUMN', @level2name = N'TenantId';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:121:ScopedLookup.TenantId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScopedLookup',
    @level2type = N'COLUMN', @level2name = N'TenantId';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Value',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScopedLookup',
    @level2type = N'COLUMN', @level2name = N'Value';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:118:ScopedLookup.Value',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'ScopedLookup',
    @level2type = N'COLUMN', @level2name = N'Value';

GO

CREATE TABLE [dbo].[TextFidelity] (
    [Body] NVARCHAR (50) NULL,
    [Id]   INT           NOT NULL
        CONSTRAINT [PK_TextFidelity_Id]
            PRIMARY KEY CLUSTERED
)

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'TextFidelity',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'TextFidelity';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_KIND1:112:TextFidelity',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'TextFidelity';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Body',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'TextFidelity',
    @level2type = N'COLUMN', @level2name = N'Body';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:117:TextFidelity.Body',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'TextFidelity',
    @level2type = N'COLUMN', @level2name = N'Body';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'TextFidelity',
    @level2type = N'COLUMN', @level2name = N'Id';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:115:TextFidelity.Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'TextFidelity',
    @level2type = N'COLUMN', @level2name = N'Id';

GO

CREATE TABLE [dbo].[Tier] (
    [Id]   INT           IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_Tier_Id]
            PRIMARY KEY CLUSTERED,
    [Name] NVARCHAR (40) NOT NULL
)

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'MS_Description',
    @value = N'Static lookup with an IDENTITY PK — the IDENTITY_INSERT bracket case.',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Tier';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Tier',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Tier';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_KIND1:14:Tier',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Tier';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Tier',
    @level2type = N'COLUMN', @level2name = N'Id';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:17:Tier.Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Tier',
    @level2type = N'COLUMN', @level2name = N'Id';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Name',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Tier',
    @level2type = N'COLUMN', @level2name = N'Name';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:19:Tier.Name',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'Tier',
    @level2type = N'COLUMN', @level2name = N'Name';

GO

CREATE TABLE [dbo].[User] (
    [Email] NVARCHAR (250) NOT NULL,
    [Id]    INT            IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_User_Id]
            PRIMARY KEY CLUSTERED
)

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'MS_Description',
    @value = N'The platform user kind (pure reference target).',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'User';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'User',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'User';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_KIND1:14:User',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'User';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Email',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'User',
    @level2type = N'COLUMN', @level2name = N'Email';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:110:User.Email',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'User',
    @level2type = N'COLUMN', @level2name = N'Email';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.LogicalName',
    @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'User',
    @level2type = N'COLUMN', @level2name = N'Id';

GO

EXECUTE [sys].[sp_addextendedproperty]
    @name = N'Projection.SsKey',
    @value = N'S9:GOLD_ATTR1:17:User.Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'User',
    @level2type = N'COLUMN', @level2name = N'Id';

GO


