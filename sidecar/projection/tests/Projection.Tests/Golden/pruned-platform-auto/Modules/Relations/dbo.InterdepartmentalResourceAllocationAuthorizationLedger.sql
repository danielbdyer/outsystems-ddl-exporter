CREATE TABLE [dbo].[InterdepartmentalResourceAllocationAuthorizationLedger] (
    [Id]                                                        INT IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_dbo_InterdepartmentalResourceAllocationAuthorizationLedger]
            PRIMARY KEY CLUSTERED,
    [PrimaryResponsibleEnterpriseCustomerRelationshipManagerId] INT NOT NULL
        CONSTRAINT [FK_InterdepartmentalResourceAllocationAuthorizationLedger_EnterpriseCustomerRelationshipManagementProfileSnapshot_P_b94286649f49]
            FOREIGN KEY ([PrimaryResponsibleEnterpriseCustomerRelationshipManagerId]) REFERENCES [dbo].[EnterpriseCustomerRelationshipManagementProfileSnapshot] ([Id])
)

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'InterdepartmentalResourceAllocationAuthorizationLedger',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'InterdepartmentalResourceAllocationAuthorizationLedger'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.SsKey', @value = N'S9:GOLD_KIND1:16:Ledger',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'InterdepartmentalResourceAllocationAuthorizationLedger'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'InterdepartmentalResourceAllocationAuthorizationLedger',
    @level2type = N'COLUMN', @level2name = N'Id'

GO

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'PrimaryResponsibleEnterpriseCustomerRelationshipManagerId',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE', @level1name = N'InterdepartmentalResourceAllocationAuthorizationLedger',
    @level2type = N'COLUMN', @level2name = N'PrimaryResponsibleEnterpriseCustomerRelationshipManagerId'

