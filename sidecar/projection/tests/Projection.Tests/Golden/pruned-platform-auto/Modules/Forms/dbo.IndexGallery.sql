CREATE TABLE [dbo].[IndexGallery] (
    [Alpha] NVARCHAR (50) NULL,
    [Beta]  INT           NULL,
    [Gamma] INT           NULL,
    [Id]    INT           IDENTITY (1, 1) NOT NULL CONSTRAINT [PK_dbo_IndexGallery] PRIMARY KEY CLUSTERED
)
CREATE INDEX [IX_IndexGallery_Alpha_Covering]
    ON [dbo].[IndexGallery]([Alpha])
    INCLUDE([Gamma])
CREATE INDEX [IX_IndexGallery_Beta_Desc]
    ON [dbo].[IndexGallery]([Beta] DESC)
CREATE INDEX [IX_IndexGallery_Alpha_Disabled]
    ON [dbo].[IndexGallery]([Alpha])
CREATE INDEX [IX_IndexGallery_Beta_Filtered]
    ON [dbo].[IndexGallery]([Beta]) WHERE ([BETA] IS NOT NULL)
CREATE INDEX [IX_IndexGallery_Alpha]
    ON [dbo].[IndexGallery]([Alpha])
CREATE UNIQUE INDEX [UIX_IndexGallery_Gamma_Tuned]
    ON [dbo].[IndexGallery]([Gamma]) WITH (FILLFACTOR = 80, PAD_INDEX = ON, IGNORE_DUP_KEY = ON, DATA_COMPRESSION = PAGE)
CREATE UNIQUE INDEX [UIX_IndexGallery_Beta]
    ON [dbo].[IndexGallery]([Beta])
ALTER INDEX [IX_IndexGallery_Alpha_Disabled]
    ON [dbo].[IndexGallery] DISABLE
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'IndexGallery', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'IndexGallery'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.SsKey', @value = N'S9:GOLD_KIND1:112:IndexGallery', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'IndexGallery'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Alpha', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'IndexGallery', @level2type = N'COLUMN', @level2name = N'Alpha'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Beta', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'IndexGallery', @level2type = N'COLUMN', @level2name = N'Beta'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Gamma', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'IndexGallery', @level2type = N'COLUMN', @level2name = N'Gamma'
EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Id', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'IndexGallery', @level2type = N'COLUMN', @level2name = N'Id'
EXECUTE [sys].[sp_addextendedproperty] @name = N'MS_Description', @value = N'Descending scan support.', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'IndexGallery', @level2type = N'INDEX', @level2name = N'IX_IndexGallery_Beta_Desc'