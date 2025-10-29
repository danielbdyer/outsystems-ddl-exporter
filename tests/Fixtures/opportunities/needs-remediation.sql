-- UniqueIndex dbo.OSUSR_ABC_ORDER (IX_OSUSR_ABC_ORDER_OrderNumber) Risk=Moderate
-- Rationale: Duplicate values detected.
-- Evidence: Unique duplicates=True (Outcome=Succeeded, Sample=100, Captured=2024-01-01T00:00:00.0000000+00:00)
CREATE UNIQUE INDEX [IX_OSUSR_ABC_ORDER_OrderNumber] ON [dbo].[OSUSR_ABC_ORDER] ([OrderNumber]);
GO

