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

