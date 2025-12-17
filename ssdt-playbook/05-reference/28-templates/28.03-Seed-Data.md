# 28.3 Idempotent Seed Data Template

```sql
/*
Reference Data: TableName
Description: Seeds the lookup/reference values for TableName
*/

PRINT 'Seeding TableName reference data...'

MERGE INTO [dbo].[TableName] AS target
USING (VALUES
    (1, 'Value1', 'Description 1', 1),
    (2, 'Value2', 'Description 2', 2),
    (3, 'Value3', 'Description 3', 3)
) AS source ([Id], [Code], [Description], [SortOrder])
ON target.[Id] = source.[Id]
WHEN MATCHED THEN
    UPDATE SET 
        [Code] = source.[Code],
        [Description] = source.[Description],
        [SortOrder] = source.[SortOrder]
WHEN NOT MATCHED THEN
    INSERT ([Id], [Code], [Description], [SortOrder])
    VALUES (source.[Id], source.[Code], source.[Description], source.[SortOrder]);

PRINT '  TableName seeded/updated.'
GO
```

---
