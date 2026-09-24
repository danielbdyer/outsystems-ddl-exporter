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

