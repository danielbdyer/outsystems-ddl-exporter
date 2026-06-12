CREATE TABLE [dbo].[Guarded] (
    [Id]  INT IDENTITY (1, 1) NOT NULL CONSTRAINT [PK_dbo_Guarded] PRIMARY KEY CLUSTERED,
    [Qty] INT NOT NULL,
    CONSTRAINT [CK_Guarded_Qty] CHECK (([QTY] >= (0))),
    CHECK (([QTY] <= (1000000)))
)
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Guarded', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Guarded'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.SsKey', @value = N'S9:GOLD_KIND1:17:Guarded', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Guarded'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Id', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Guarded', @level2type = N'COLUMN', @level2name = N'Id'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Qty', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Guarded', @level2type = N'COLUMN', @level2name = N'Qty'
-- Trigger: TRG_Guarded_Audit (disabled: false)
CREATE TRIGGER [dbo].[TRG_Guarded_Audit]
    ON [dbo].[GOLD_GUARDED]
    AFTER INSERT
    AS BEGIN
           SET NOCOUNT ON;
       END