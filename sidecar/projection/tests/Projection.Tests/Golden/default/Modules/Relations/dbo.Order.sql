CREATE TABLE [dbo].[Order] (
    [AltCustomerId] INT NULL,
    [CustomerId]    INT NOT NULL,
    [Id]            INT IDENTITY (1, 1) NOT NULL CONSTRAINT [PK_dbo_Order] PRIMARY KEY CLUSTERED,
    CONSTRAINT [FK_Order_Customer_AltCustomerId] FOREIGN KEY ([AltCustomerId]) REFERENCES [dbo].[Customer] ([Id]) ON DELETE SET NULL,
    CONSTRAINT [FK_Order_Customer_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id]) ON DELETE CASCADE
)
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Order', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Order'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.SsKey', @value = N'S9:GOLD_KIND1:15:Order', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Order'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'AltCustomerId', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Order', @level2type = N'COLUMN', @level2name = N'AltCustomerId'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'CustomerId', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Order', @level2type = N'COLUMN', @level2name = N'CustomerId'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Id', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Order', @level2type = N'COLUMN', @level2name = N'Id'