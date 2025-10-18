/* ============================================================================
   OutSystems → JSON (Two-phase, CTE-free)
   GOAL #1: emit 100% of module/entity/attribute/index fields in README schema,
   GOAL #2: reconcile ossys intent with sys.* physical reality.
   Inputs:
     @ModuleNamesCsv (Text), @IncludeSystem (Boolean), @OnlyActiveAttributes (Boolean)
   Output: JSON
   ============================================================================ */

SET NOCOUNT ON;
SET TEXTSIZE -1; -- unlimited for (n)varchar(max) in this session

IF OBJECT_ID(N'dbo.ossys_Espace') IS NULL
BEGIN
    THROW 50010, 'Required table dbo.ossys_Espace not found in current catalog. Aborting metadata export.', 1;
END;

IF OBJECT_ID(N'dbo.ossys_Entity') IS NULL
BEGIN
    THROW 50011, 'Required table dbo.ossys_Entity not found in current catalog. Aborting metadata export.', 1;
END;

/* Optional local defaults for standalone execution
DECLARE @ModuleNamesCsv NVARCHAR(MAX) = N'OutSystemsModule1_CS,OutSystemsModel2_CS';
DECLARE @IncludeSystem BIT = 1;
DECLARE @OnlyActiveAttributes BIT = 1;
*/

/* --------------------------------------------------------------------------
   Phase 1: Collect & materialize metadata
----------------------------------------------------------------------------*/

-- 1) #ModuleNames
IF OBJECT_ID('tempdb..#ModuleNames') IS NOT NULL DROP TABLE #ModuleNames;
CREATE TABLE #ModuleNames ( ModuleName NVARCHAR(256) COLLATE DATABASE_DEFAULT NOT NULL );

DECLARE @ModuleTokens TABLE ( ModuleName NVARCHAR(MAX) NOT NULL );

IF NULLIF(LTRIM(RTRIM(@ModuleNamesCsv)), '') IS NOT NULL
BEGIN
    INSERT INTO @ModuleTokens(ModuleName)
    SELECT LTRIM(RTRIM(value))
    FROM STRING_SPLIT(@ModuleNamesCsv, ',')
    WHERE NULLIF(LTRIM(RTRIM(value)), '') IS NOT NULL;
END;

DECLARE @InvalidModuleName NVARCHAR(MAX);
SELECT TOP (1) @InvalidModuleName = ModuleName
FROM @ModuleTokens
WHERE LEN(ModuleName) > 256;

IF @InvalidModuleName IS NOT NULL
BEGIN
    DECLARE @ModuleNameError NVARCHAR(4000) =
        N'Module filter module name exceeds 256 characters: '''
        + LEFT(@InvalidModuleName, 256)
        + N'''.';
    THROW 50000, @ModuleNameError, 1;
END;

IF EXISTS (SELECT 1 FROM @ModuleTokens)
BEGIN
    INSERT INTO #ModuleNames(ModuleName)
    SELECT ModuleName
    FROM @ModuleTokens;
END;

-- 2) #E (espace) with module-level IncludeSystem filtering
IF OBJECT_ID('tempdb..#E') IS NOT NULL DROP TABLE #E;
SELECT
    e.[Id]                                AS EspaceId,
    e.[Name]                              AS EspaceName,
    CAST(ISNULL(e.[Is_System],0) AS bit)  AS IsSystemModule,
    CAST(ISNULL(e.[Is_Active],1) AS bit)  AS ModuleIsActive,
    e.[EspaceKind],
    TRY_CONVERT(uniqueidentifier, e.[SS_Key]) AS EspaceSSKey
INTO #E
FROM dbo.ossys_Espace AS e
WHERE (@IncludeSystem = 1 OR ISNULL(e.[Is_System],0) = 0)
  AND (
        NOT EXISTS (SELECT 1 FROM #ModuleNames)
        OR EXISTS (SELECT 1 FROM #ModuleNames mn
                   WHERE e.[Name] COLLATE DATABASE_DEFAULT
                         = mn.ModuleName COLLATE DATABASE_DEFAULT)
      );
CREATE CLUSTERED INDEX IX_E ON #E(EspaceId);

-- 3) #Ent (Entity)
IF OBJECT_ID('tempdb..#Ent') IS NOT NULL DROP TABLE #Ent;
SELECT
    en.[Id]                                AS EntityId,
    en.[Name]                              AS EntityName,
    en.[Physical_Table_Name]               AS PhysicalTableName,
    en.[Espace_Id]                         AS EspaceId,
    CAST(ISNULL(en.[Is_Active],1)  AS bit) AS EntityIsActive,
    CAST(ISNULL(en.[Is_System],0)  AS bit) AS IsSystemEntity,
    CAST(ISNULL(en.[Is_External],0)AS bit) AS IsExternalEntity,
    en.[Data_Kind]                         AS DataKind,
    TRY_CONVERT(uniqueidentifier, en.[PrimaryKey_SS_Key]) AS PrimaryKeySSKey,
    TRY_CONVERT(uniqueidentifier, en.[SS_Key]) AS EntitySSKey,
    CAST(NULL AS NVARCHAR(MAX))            AS EntityDescription
INTO #Ent
FROM dbo.ossys_Entity en
JOIN #E ON #E.EspaceId = en.[Espace_Id]
WHERE (@IncludeSystem = 1 OR ISNULL(en.[Is_System],0) = 0);
CREATE CLUSTERED INDEX IX_Ent ON #Ent(EntityId);
CREATE NONCLUSTERED INDEX IX_Ent_Espace ON #Ent(EspaceId) INCLUDE (PhysicalTableName);

DECLARE @EntityDescriptionColumn SYSNAME =
    CASE
        WHEN COL_LENGTH(N'dbo.ossys_Entity', 'Description') IS NOT NULL THEN N'Description'
        WHEN COL_LENGTH(N'dbo.ossys_Entity', 'Description_Translation') IS NOT NULL THEN N'Description_Translation'
        ELSE NULL
    END;

IF @EntityDescriptionColumn IS NOT NULL
BEGIN
    DECLARE @EntityDescriptionSql NVARCHAR(MAX) =
        N'UPDATE en
          SET en.EntityDescription = NULLIF(LTRIM(RTRIM(src.' + QUOTENAME(@EntityDescriptionColumn) + N')), '''')
          FROM #Ent en
          JOIN dbo.ossys_Entity src ON src.[Id] = en.EntityId;';

    EXEC sys.sp_executesql @EntityDescriptionSql;
END;

-- 4) #Attr (Entity_Attr)
IF OBJECT_ID('tempdb..#Attr') IS NOT NULL DROP TABLE #Attr;
CREATE TABLE #Attr
(
    AttrId               INT            NOT NULL,
    EntityId             INT            NOT NULL,
    AttrName             NVARCHAR(200)  NOT NULL,
    AttrSSKey            UNIQUEIDENTIFIER NULL,
    DataType             NVARCHAR(200)  NULL,
    [Length]             INT            NULL,
    [Precision]          INT            NULL,
    [Scale]              INT            NULL,
    DefaultValue         NVARCHAR(MAX)  NULL,
    IsMandatory          BIT            NOT NULL,
    AttrIsActive         BIT            NOT NULL,
    IsAutoNumber         BIT            NULL,
    IsIdentifier         BIT            NULL,
    RefEntityId          INT            NULL,
    OriginalName         NVARCHAR(200)  NULL,
    ExternalColumnType   NVARCHAR(200)  NULL,
    DeleteRule           NVARCHAR(50)   NULL,
    PhysicalColumnName   NVARCHAR(200)  NULL,
    DatabaseColumnName   NVARCHAR(200)  NULL,
    LegacyType           NVARCHAR(400)  NULL,
    Decimals             INT            NULL,
    OriginalType         NVARCHAR(200)  NULL,
    AttrDescription      NVARCHAR(MAX)  NULL
);

DECLARE
    @AttrObjectId INT = OBJECT_ID(N'dbo.ossys_Entity_Attr'),
    @AttrSchema SYSNAME = OBJECT_SCHEMA_NAME(OBJECT_ID(N'dbo.ossys_Entity_Attr')),
    @AttrName SYSNAME = OBJECT_NAME(OBJECT_ID(N'dbo.ossys_Entity_Attr')),
    @HasDataType BIT = CASE WHEN COL_LENGTH(N'dbo.ossys_Entity_Attr', 'Data_Type') IS NOT NULL THEN 1 ELSE 0 END,
    @HasType BIT = CASE WHEN COL_LENGTH(N'dbo.ossys_Entity_Attr', 'Type') IS NOT NULL THEN 1 ELSE 0 END,
    @HasPrecision BIT = CASE WHEN COL_LENGTH(N'dbo.ossys_Entity_Attr', 'Precision') IS NOT NULL THEN 1 ELSE 0 END,
    @HasScale BIT = CASE WHEN COL_LENGTH(N'dbo.ossys_Entity_Attr', 'Scale') IS NOT NULL THEN 1 ELSE 0 END,
    @HasDecimals BIT = CASE WHEN COL_LENGTH(N'dbo.ossys_Entity_Attr', 'Decimals') IS NOT NULL THEN 1 ELSE 0 END,
    @HasOriginalName BIT = CASE WHEN COL_LENGTH(N'dbo.ossys_Entity_Attr', 'Original_Name') IS NOT NULL THEN 1 ELSE 0 END,
    @HasExternalColumnType BIT = CASE WHEN COL_LENGTH(N'dbo.ossys_Entity_Attr', 'External_Column_Type') IS NOT NULL THEN 1 ELSE 0 END,
    @HasPhysicalColumnName BIT = CASE WHEN COL_LENGTH(N'dbo.ossys_Entity_Attr', 'Physical_Column_Name') IS NOT NULL THEN 1 ELSE 0 END,
    @HasDatabaseName BIT = CASE WHEN COL_LENGTH(N'dbo.ossys_Entity_Attr', 'Database_Name') IS NOT NULL THEN 1 ELSE 0 END,
    @HasIsIdentifier BIT = CASE WHEN COL_LENGTH(N'dbo.ossys_Entity_Attr', 'Is_Identifier') IS NOT NULL THEN 1 ELSE 0 END,
    @HasRefEntityId BIT = CASE WHEN COL_LENGTH(N'dbo.ossys_Entity_Attr', 'Referenced_Entity_Id') IS NOT NULL THEN 1 ELSE 0 END,
    @HasIsAutoNumber BIT = CASE WHEN COL_LENGTH(N'dbo.ossys_Entity_Attr', 'Is_AutoNumber') IS NOT NULL THEN 1 ELSE 0 END,
    @HasDefaultValue BIT = CASE WHEN COL_LENGTH(N'dbo.ossys_Entity_Attr', 'Default_Value') IS NOT NULL THEN 1 ELSE 0 END,
    @HasDeleteRule BIT = CASE WHEN COL_LENGTH(N'dbo.ossys_Entity_Attr', 'Delete_Rule') IS NOT NULL THEN 1 ELSE 0 END,
    @HasOriginalType BIT = CASE WHEN COL_LENGTH(N'dbo.ossys_Entity_Attr', 'Original_Type') IS NOT NULL THEN 1 ELSE 0 END,
    @HasSSKey BIT = CASE WHEN COL_LENGTH(N'dbo.ossys_Entity_Attr', 'SS_Key') IS NOT NULL THEN 1 ELSE 0 END,
    @HasLength BIT = CASE WHEN COL_LENGTH(N'dbo.ossys_Entity_Attr', 'Length') IS NOT NULL THEN 1 ELSE 0 END,
    @AttrDescriptionExpr NVARCHAR(MAX),
    @InsertAttr NVARCHAR(MAX);

IF @AttrObjectId IS NULL
BEGIN
    THROW 50000, 'ossys_Entity_Attr table not found in current catalog.', 1;
END;

SET @AttrDescriptionExpr =
    CASE
        WHEN COL_LENGTH(N'dbo.ossys_Entity_Attr', 'Description') IS NOT NULL
            THEN N'NULLIF(LTRIM(RTRIM(a.[Description])),'''')'
        WHEN COL_LENGTH(N'dbo.ossys_Entity_Attr', 'Description_Translation') IS NOT NULL
            THEN N'NULLIF(LTRIM(RTRIM(a.[Description_Translation])),'''')'
        ELSE N'NULL'
    END;

SET @InsertAttr = N'INSERT INTO #Attr (
      AttrId, EntityId, AttrName, AttrSSKey, DataType, [Length], [Precision], [Scale],
      DefaultValue, IsMandatory, AttrIsActive, IsAutoNumber, IsIdentifier, RefEntityId,
      OriginalName, ExternalColumnType, DeleteRule, PhysicalColumnName, DatabaseColumnName,
      LegacyType, Decimals, OriginalType, AttrDescription)
    SELECT
      a.[Id],
      a.[Entity_Id],
      a.[Name],
      ' + CASE WHEN @HasSSKey = 1 THEN N'a.[SS_Key]' ELSE N'NULL' END + N',
      ' + CASE WHEN @HasDataType = 1 THEN N'a.[Data_Type]' ELSE CASE WHEN @HasType = 1 THEN N'a.[Type]' ELSE N'NULL' END END + N',
      ' + CASE WHEN @HasLength = 1 THEN N'a.[Length]' ELSE N'NULL' END + N',
      ' + CASE WHEN @HasPrecision = 1 THEN N'a.[Precision]' ELSE N'NULL' END + N',
      ' + CASE WHEN @HasScale = 1 THEN N'a.[Scale]' ELSE CASE WHEN @HasDecimals = 1 THEN N'a.[Decimals]' ELSE N'NULL' END END + N',
      ' + CASE WHEN @HasDefaultValue = 1 THEN N'a.[Default_Value]' ELSE N'NULL' END + N',
      CAST(ISNULL(a.[Is_Mandatory],0) AS bit),
      CAST(ISNULL(a.[Is_Active],1) AS bit),
      ' + CASE WHEN @HasIsAutoNumber = 1 THEN N'CAST(ISNULL(a.[Is_AutoNumber],0) AS bit)' ELSE N'NULL' END + N',
      ' + CASE WHEN @HasIsIdentifier = 1 THEN N'CAST(ISNULL(a.[Is_Identifier],0) AS bit)' ELSE N'NULL' END + N',
      ' + CASE WHEN @HasRefEntityId = 1 THEN N'a.[Referenced_Entity_Id]' ELSE N'NULL' END + N',
      ' + CASE WHEN @HasOriginalName = 1 THEN N'a.[Original_Name]' ELSE N'NULL' END + N',
      ' + CASE WHEN @HasExternalColumnType = 1 THEN N'a.[External_Column_Type]' ELSE N'NULL' END + N',
      ' + CASE WHEN @HasDeleteRule = 1 THEN N'a.[Delete_Rule]' ELSE N'NULL' END + N',
      ' + CASE WHEN @HasPhysicalColumnName = 1 THEN N'NULLIF(a.[Physical_Column_Name],'''')' ELSE N'NULL' END + N',
      ' + CASE WHEN @HasDatabaseName = 1 THEN N'NULLIF(a.[Database_Name],'''')' ELSE N'NULL' END + N',
      ' + CASE WHEN @HasType = 1 THEN N'a.[Type]' ELSE N'NULL' END + N',
      ' + CASE WHEN @HasDecimals = 1 THEN N'a.[Decimals]' ELSE N'NULL' END + N',
      ' + CASE WHEN @HasOriginalType = 1 THEN N'a.[Original_Type]' ELSE N'NULL' END + N',
      ' + @AttrDescriptionExpr + N'
    FROM ' + QUOTENAME(@AttrSchema) + N'.' + QUOTENAME(@AttrName) + N' AS a
    JOIN #Ent ON #Ent.EntityId = a.[Entity_Id]
    WHERE (@OnlyActiveAttributes = 0 OR ISNULL(a.[Is_Active],1) = 1);';

EXEC sys.sp_executesql @InsertAttr, N'@OnlyActiveAttributes bit', @OnlyActiveAttributes = @OnlyActiveAttributes;

CREATE CLUSTERED INDEX IX_Attr ON #Attr(EntityId, AttrId);
CREATE NONCLUSTERED INDEX IX_Attr_Name ON #Attr(AttrName);

-- 4b) Normalize stored physical names with any explicit database overrides
UPDATE a
SET a.PhysicalColumnName = COALESCE(NULLIF(a.PhysicalColumnName, ''), a.DatabaseColumnName)
FROM #Attr a
WHERE (a.PhysicalColumnName IS NULL OR a.PhysicalColumnName = '')
  AND a.DatabaseColumnName IS NOT NULL;

IF OBJECT_ID('tempdb..#RefResolved') IS NOT NULL DROP TABLE #RefResolved;
WITH ParsedRef AS
(
  SELECT
    a.AttrId,
    TRY_CONVERT(uniqueidentifier,
                CASE WHEN a.LegacyType LIKE 'bt*%'
                     THEN SUBSTRING(a.LegacyType, 3, 36)
                END) AS RefEspaceSSKey,
    TRY_CONVERT(uniqueidentifier,
                CASE WHEN a.LegacyType LIKE 'bt*%'
                     THEN SUBSTRING(a.LegacyType, CHARINDEX('*', a.LegacyType) + 1, 36)
                END) AS RefEntitySSKey
  FROM #Attr a
)
SELECT
    a.AttrId,
    COALESCE(
        eById.EntityId,
        eByKey.EntityId,
        fallbackById.EntityId,
        fallbackByKey.EntityId)                           AS RefEntityId,
    COALESCE(
        eById.EntityName,
        eByKey.EntityName,
        fallbackById.EntityName,
        fallbackByKey.EntityName)                         AS RefEntityName,
    COALESCE(
        eById.PhysicalTableName,
        eByKey.PhysicalTableName,
        fallbackById.PhysicalTableName,
        fallbackByKey.PhysicalTableName)                  AS RefPhysicalName
INTO #RefResolved
FROM #Attr a
LEFT JOIN #Ent eById ON eById.EntityId = a.RefEntityId
LEFT JOIN ParsedRef pr ON pr.AttrId = a.AttrId
LEFT JOIN #Ent eByKey
  ON eByKey.EntitySSKey = pr.RefEntitySSKey
LEFT JOIN #E eByKeyModule
  ON eByKeyModule.EspaceId = eByKey.EspaceId
 AND eByKeyModule.EspaceSSKey = pr.RefEspaceSSKey
OUTER APPLY (
    SELECT TOP (1)
        en.[Id] AS EntityId,
        en.[Name] AS EntityName,
        en.[Physical_Table_Name] AS PhysicalTableName
    FROM dbo.ossys_Entity en
    WHERE a.RefEntityId IS NOT NULL
      AND en.[Id] = a.RefEntityId
) fallbackById
OUTER APPLY (
    SELECT TOP (1)
        en.[Id] AS EntityId,
        en.[Name] AS EntityName,
        en.[Physical_Table_Name] AS PhysicalTableName
    FROM dbo.ossys_Entity en
    JOIN dbo.ossys_Espace ee ON ee.[Id] = en.[Espace_Id]
    WHERE pr.RefEntitySSKey IS NOT NULL
      AND TRY_CONVERT(uniqueidentifier, en.[SS_Key]) = pr.RefEntitySSKey
      AND (
            pr.RefEspaceSSKey IS NULL
            OR TRY_CONVERT(uniqueidentifier, ee.[SS_Key]) = pr.RefEspaceSSKey
          )
) fallbackByKey
WHERE COALESCE(
        eById.EntityId,
        eByKey.EntityId,
        fallbackById.EntityId,
        fallbackByKey.EntityId) IS NOT NULL;
CREATE CLUSTERED INDEX IX_RefResolved ON #RefResolved(AttrId);

-- 6) Physical tables (resolve schema)
IF OBJECT_ID('tempdb..#PhysTbls') IS NOT NULL DROP TABLE #PhysTbls;
SELECT
    en.EntityId,
    s.[name] AS SchemaName,
    t.[name] AS TableName,
    t.object_id
INTO #PhysTbls
FROM #Ent en
JOIN sys.tables t
  ON t.[name] COLLATE DATABASE_DEFAULT = en.PhysicalTableName COLLATE DATABASE_DEFAULT
JOIN sys.schemas s
  ON s.schema_id = t.schema_id;
CREATE CLUSTERED INDEX IX_PhysTbls ON #PhysTbls(EntityId);

-- Table triggers
IF OBJECT_ID('tempdb..#Triggers') IS NOT NULL DROP TABLE #Triggers;
SELECT
    pt.EntityId,
    tr.[name] AS TriggerName,
    CAST(tr.is_disabled AS bit) AS IsDisabled,
    COALESCE(OBJECT_DEFINITION(tr.object_id), N'') AS TriggerDefinition
INTO #Triggers
FROM #PhysTbls pt
JOIN sys.triggers tr
  ON tr.parent_id = pt.object_id
WHERE tr.parent_class_desc = 'OBJECT_OR_COLUMN';
CREATE CLUSTERED INDEX IX_Triggers ON #Triggers(EntityId, TriggerName);

-- 7) Column reality (nullability, SQL type, identity, defaults)
IF OBJECT_ID('tempdb..#ColumnReality') IS NOT NULL DROP TABLE #ColumnReality;
SELECT
    a.AttrId,
    CAST(c.is_nullable AS bit) AS IsNullable,
    t.[name] AS SqlType,
    CASE
        WHEN c.max_length = -1 THEN -1
        WHEN t.[name] IN (N'nchar', N'nvarchar', N'ntext') THEN c.max_length / 2
        ELSE c.max_length
    END AS MaxLength,
    c.precision AS [Precision],
    c.scale AS [Scale],
    c.collation_name AS CollationName,
    CAST(c.is_identity AS bit) AS IsIdentity,
    CAST(COLUMNPROPERTY(c.object_id, c.[name], 'IsComputed') AS bit) AS IsComputed,
    cc.definition AS ComputedDefinition,
    dc.name AS DefaultConstraintName,
    dc.definition AS DefaultDefinition,
    c.[name] AS PhysicalColumn
INTO #ColumnReality
FROM #Attr a
JOIN #PhysTbls pt ON pt.EntityId = a.EntityId
JOIN sys.columns c
  ON c.object_id = pt.object_id
 AND c.[name] COLLATE Latin1_General_CI_AI = COALESCE(NULLIF(a.PhysicalColumnName, ''), NULLIF(a.DatabaseColumnName, ''), a.AttrName) COLLATE Latin1_General_CI_AI
JOIN sys.types t
  ON t.user_type_id = c.user_type_id AND t.system_type_id = c.system_type_id
LEFT JOIN sys.computed_columns cc
  ON cc.object_id = c.object_id AND cc.column_id = c.column_id
LEFT JOIN sys.default_constraints dc
  ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id;
CREATE CLUSTERED INDEX IX_ColumnReality ON #ColumnReality(AttrId);

-- Column-level check constraints (attached to physical columns)
IF OBJECT_ID('tempdb..#ColumnCheckReality') IS NOT NULL DROP TABLE #ColumnCheckReality;
SELECT
    a.AttrId,
    ck.[name] AS ConstraintName,
    ck.definition AS Definition,
    CAST(ck.is_not_trusted AS bit) AS IsNotTrusted
INTO #ColumnCheckReality
FROM #Attr a
JOIN #PhysTbls pt ON pt.EntityId = a.EntityId
JOIN sys.columns c
  ON c.object_id = pt.object_id
 AND c.[name] COLLATE Latin1_General_CI_AI = COALESCE(NULLIF(a.PhysicalColumnName, ''), NULLIF(a.DatabaseColumnName, ''), a.AttrName) COLLATE Latin1_General_CI_AI
JOIN sys.check_constraints ck
  ON ck.parent_object_id = c.object_id
 AND ck.parent_column_id = c.column_id;
CREATE CLUSTERED INDEX IX_ColumnCheckReality ON #ColumnCheckReality(AttrId);

-- Aggregate check constraint JSON for quick lookup
IF OBJECT_ID('tempdb..#AttrCheckJson') IS NOT NULL DROP TABLE #AttrCheckJson;
SELECT
  cc.AttrId,
  ISNULL((
    SELECT
      cc2.ConstraintName AS [name],
      cc2.Definition AS [definition],
      CAST(cc2.IsNotTrusted AS bit) AS [isNotTrusted]
    FROM #ColumnCheckReality cc2
    WHERE cc2.AttrId = cc.AttrId
    FOR JSON PATH
  ), '[]') AS CheckJson
INTO #AttrCheckJson
FROM #ColumnCheckReality cc
GROUP BY cc.AttrId;
CREATE CLUSTERED INDEX IX_AttrCheckJson ON #AttrCheckJson(AttrId);

-- 8) Record which logical attributes still exist physically
IF OBJECT_ID('tempdb..#PhysColsPresent') IS NOT NULL DROP TABLE #PhysColsPresent;
SELECT DISTINCT AttrId
INTO #PhysColsPresent
FROM #ColumnReality;
CREATE CLUSTERED INDEX IX_PhysColsPresent ON #PhysColsPresent(AttrId);

-- 9) Backfill #Attr.PhysicalColumnName when catalog evidence disagrees
UPDATE a
SET a.PhysicalColumnName = cr.PhysicalColumn
FROM #Attr a
JOIN #ColumnReality cr ON cr.AttrId = a.AttrId
WHERE (a.PhysicalColumnName IS NULL OR a.PhysicalColumnName = '');

-- 9) Index catalog (IX + UQ + PK)
IF OBJECT_ID('tempdb..#AllIdx') IS NOT NULL DROP TABLE #AllIdx;
WITH IndexSource AS
(
  SELECT
      en.EntityId,
      i.object_id,
      i.index_id,
      COALESCE(kc.[name], i.[name])                 AS IndexName,
      CAST(i.is_unique AS bit)                      AS IsUnique,
      CAST(i.is_primary_key AS bit)                 AS IsPrimary,
      CASE
          WHEN i.is_primary_key = 1 THEN N'PK'
          WHEN i.is_unique_constraint = 1 THEN N'UQ'
          WHEN i.is_unique = 1 THEN N'UIX'
          ELSE N'IX'
      END                                           AS Kind,
      i.filter_definition                           AS FilterDefinition,
      CAST(i.is_disabled AS bit)                    AS IsDisabled,
      CAST(i.is_padded AS bit)                      AS IsPadded,
      i.fill_factor                                 AS Fill_Factor,
      CAST(i.ignore_dup_key AS bit)                 AS IgnoreDupKey,
      CAST(i.allow_row_locks AS bit)                AS AllowRowLocks,
      CAST(i.allow_page_locks AS bit)               AS AllowPageLocks,
      CAST(st.no_recompute AS bit)                  AS NoRecompute,
      ds.[name]                                     AS DataSpaceName,
      ds.type_desc                                  AS DataSpaceType,
      pc.PartitionColumnsJson,
      dc.DataCompressionJson
  FROM #PhysTbls pt
  JOIN #Ent en ON en.EntityId = pt.EntityId
  JOIN sys.indexes i ON i.object_id = pt.object_id
  LEFT JOIN sys.key_constraints kc
    ON kc.parent_object_id = i.object_id
   AND kc.unique_index_id = i.index_id
   AND kc.[type] IN ('PK', 'UQ')
  LEFT JOIN sys.stats st
    ON st.object_id = i.object_id AND st.stats_id = i.index_id
  LEFT JOIN sys.data_spaces ds
    ON ds.data_space_id = i.data_space_id
  OUTER APPLY (
    SELECT NULLIF(JSON_QUERY(
      (
        SELECT
            ic.partition_ordinal AS [ordinal],
            c.[name]              AS [name]
        FROM sys.index_columns ic
        JOIN sys.columns c
          ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE ic.object_id = i.object_id
          AND ic.index_id   = i.index_id
          AND ic.partition_ordinal > 0
        ORDER BY ic.partition_ordinal
        FOR JSON PATH
      )
    ), N'[]') AS PartitionColumnsJson
  ) pc
  OUTER APPLY (
    SELECT JSON_QUERY(
      (
        SELECT
            p.partition_number      AS [partition],
            p.data_compression_desc AS [compression]
        FROM sys.partitions p
        WHERE p.object_id = i.object_id
          AND p.index_id  = i.index_id
        ORDER BY p.partition_number
        FOR JSON PATH
      )
    ) AS DataCompressionJson
  ) dc
  WHERE i.[type_desc] <> 'HEAP'
    AND i.is_hypothetical = 0
)
SELECT
    EntityId,
    object_id,
    index_id,
    IndexName,
    IsUnique,
    IsPrimary,
    Kind,
    FilterDefinition,
    IsDisabled,
    IsPadded,
    Fill_Factor,
    IgnoreDupKey,
    AllowRowLocks,
    AllowPageLocks,
    NoRecompute,
    DataSpaceName,
    DataSpaceType,
    PartitionColumnsJson,
    DataCompressionJson
INTO #AllIdx
FROM IndexSource;

CREATE CLUSTERED INDEX IX_AllIdx ON #AllIdx(EntityId, IndexName);

-- 10) Index columns (keys + included) mapped back to attributes by physical or human name
IF OBJECT_ID('tempdb..#IdxColsMapped') IS NOT NULL DROP TABLE #IdxColsMapped;
WITH IdxColsKeys AS
(
  SELECT ai.EntityId, ai.IndexName, ic.key_ordinal AS Ordinal, c.[name] AS PhysicalColumn,
         CAST(0 AS bit) AS IsIncluded,
         CASE WHEN ic.is_descending_key = 1 THEN N'DESC' ELSE N'ASC' END AS Direction
  FROM #AllIdx ai
  JOIN sys.index_columns ic ON ic.object_id = ai.object_id AND ic.index_id = ai.index_id
  JOIN sys.columns c        ON c.object_id = ai.object_id AND c.column_id = ic.column_id
  WHERE ic.is_included_column = 0
),
IdxColsIncl AS
(
  SELECT ai.EntityId, ai.IndexName,
         100000 + ROW_NUMBER() OVER (PARTITION BY ai.object_id, ai.index_id ORDER BY ic.column_id) AS Ordinal,
         c.[name] AS PhysicalColumn,
         CAST(1 AS bit) AS IsIncluded,
         NULL AS Direction
  FROM #AllIdx ai
  JOIN sys.index_columns ic ON ic.object_id = ai.object_id AND ic.index_id = ai.index_id
  JOIN sys.columns c        ON c.object_id = ai.object_id AND c.column_id = ic.column_id
  WHERE ic.is_included_column = 1
),
IdxColsAll AS
(
  SELECT * FROM IdxColsKeys
  UNION ALL
  SELECT * FROM IdxColsIncl
)
SELECT
    i.EntityId,
    i.IndexName,
    i.Ordinal,
    i.PhysicalColumn,
    i.IsIncluded,
    i.Direction,
    COALESCE(NULLIF(a.PhysicalColumnName, ''), NULLIF(a.DatabaseColumnName, ''), a.AttrName) AS HumanAttr
INTO #IdxColsMapped
FROM IdxColsAll i
LEFT JOIN #Attr a
  ON a.EntityId = i.EntityId
 AND COALESCE(NULLIF(a.PhysicalColumnName, ''), NULLIF(a.DatabaseColumnName, ''), a.AttrName) COLLATE Latin1_General_CI_AI
     = i.PhysicalColumn COLLATE Latin1_General_CI_AI;
CREATE CLUSTERED INDEX IX_IdxColsMapped ON #IdxColsMapped(EntityId, IndexName, Ordinal);

-- 10b) Backfill from index mapping when present
UPDATE a
SET a.PhysicalColumnName = m.PhysicalColumn
FROM #Attr a
JOIN #IdxColsMapped m  ON m.EntityId = a.EntityId
WHERE (a.PhysicalColumnName IS NULL OR a.PhysicalColumnName = '')
  AND (
        m.HumanAttr COLLATE Latin1_General_CI_AI = a.AttrName COLLATE Latin1_General_CI_AI
        OR m.PhysicalColumn COLLATE Latin1_General_CI_AI = a.AttrName COLLATE Latin1_General_CI_AI
        OR m.PhysicalColumn COLLATE Latin1_General_CI_AI = COALESCE(NULLIF(a.DatabaseColumnName, ''), a.AttrName) COLLATE Latin1_General_CI_AI
      );

-- 11) Foreign key reality (id-based evidence)
IF OBJECT_ID('tempdb..#FkReality') IS NOT NULL DROP TABLE #FkReality;
SELECT
    pt.EntityId,
    fk.object_id AS FkObjectId,
    fk.[name] AS FkName,
    fk.delete_referential_action_desc AS DeleteAction,
    fk.update_referential_action_desc AS UpdateAction,
    fk.referenced_object_id AS ReferencedObjectId,
    refPt.EntityId AS ReferencedEntityId,
    sRef.[name] AS ReferencedSchema,
    tRef.[name] AS ReferencedTable,
    CAST(fk.is_not_trusted AS bit) AS IsNoCheck
INTO #FkReality
FROM #PhysTbls pt
JOIN sys.foreign_keys fk ON fk.parent_object_id = pt.object_id
JOIN sys.tables tRef ON tRef.object_id = fk.referenced_object_id
JOIN sys.schemas sRef ON sRef.schema_id = tRef.schema_id
LEFT JOIN #PhysTbls refPt ON refPt.object_id = fk.referenced_object_id;
CREATE CLUSTERED INDEX IX_FkReality ON #FkReality(EntityId, FkObjectId);

-- 11b) Foreign key column mapping
IF OBJECT_ID('tempdb..#FkColumns') IS NOT NULL DROP TABLE #FkColumns;
SELECT
    fk.EntityId,
    fk.FkObjectId,
    fkc.constraint_column_id AS Ordinal,
    cParent.[name] AS ParentColumn,
    cRef.[name] AS ReferencedColumn,
    ap.AttrId AS ParentAttrId,
    ap.AttrName AS ParentAttrName,
    ar.AttrId AS ReferencedAttrId,
    ar.AttrName AS ReferencedAttrName
INTO #FkColumns
FROM #FkReality fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.FkObjectId
JOIN sys.columns cParent ON cParent.object_id = fkc.parent_object_id AND cParent.column_id = fkc.parent_column_id
JOIN sys.columns cRef ON cRef.object_id = fkc.referenced_object_id AND cRef.column_id = fkc.referenced_column_id
OUTER APPLY (
    SELECT TOP (1) aParent.AttrId, aParent.AttrName
    FROM #Attr aParent
    WHERE aParent.EntityId = fk.EntityId
      AND cParent.[name] COLLATE Latin1_General_CI_AI =
          COALESCE(NULLIF(aParent.PhysicalColumnName, ''), NULLIF(aParent.DatabaseColumnName, ''), aParent.AttrName) COLLATE Latin1_General_CI_AI
    ORDER BY aParent.AttrId
) ap
OUTER APPLY (
    SELECT TOP (1) aRef.AttrId, aRef.AttrName
    FROM #Attr aRef
    WHERE fk.ReferencedEntityId IS NOT NULL
      AND aRef.EntityId = fk.ReferencedEntityId
      AND cRef.[name] COLLATE Latin1_General_CI_AI =
          COALESCE(NULLIF(aRef.PhysicalColumnName, ''), NULLIF(aRef.DatabaseColumnName, ''), aRef.AttrName) COLLATE Latin1_General_CI_AI
    ORDER BY aRef.AttrId
) ar;
CREATE CLUSTERED INDEX IX_FkColumns ON #FkColumns(FkObjectId, Ordinal);

-- 11c) Attribute-to-FK map
IF OBJECT_ID('tempdb..#FkAttrMap') IS NOT NULL DROP TABLE #FkAttrMap;
SELECT DISTINCT fc.ParentAttrId AS AttrId, fk.FkObjectId
INTO #FkAttrMap
FROM #FkColumns fc
JOIN #FkReality fk ON fk.FkObjectId = fc.FkObjectId
WHERE fc.ParentAttrId IS NOT NULL;
CREATE CLUSTERED INDEX IX_FkAttrMap ON #FkAttrMap(AttrId, FkObjectId);

-- 11d) Attribute-level actual FK existence
IF OBJECT_ID('tempdb..#AttrHasFK') IS NOT NULL DROP TABLE #AttrHasFK;
SELECT DISTINCT fam.AttrId, CAST(1 AS bit) AS HasFK
INTO #AttrHasFK
FROM #FkAttrMap fam
JOIN #FkReality fk ON fk.FkObjectId = fam.FkObjectId
LEFT JOIN #RefResolved r ON r.AttrId = fam.AttrId
LEFT JOIN #PhysTbls refPt ON refPt.EntityId = r.RefEntityId
WHERE r.AttrId IS NULL
   OR fk.ReferencedEntityId = r.RefEntityId
   OR (refPt.object_id IS NOT NULL AND refPt.object_id = fk.ReferencedObjectId)
   OR (r.RefPhysicalName IS NOT NULL AND r.RefPhysicalName COLLATE DATABASE_DEFAULT = fk.ReferencedTable COLLATE DATABASE_DEFAULT);
CREATE CLUSTERED INDEX IX_AttrHasFK ON #AttrHasFK(AttrId);

-- 11e) FK column JSON per constraint
IF OBJECT_ID('tempdb..#FkColumnsJson') IS NOT NULL DROP TABLE #FkColumnsJson;
SELECT
  fc.FkObjectId,
  ISNULL((
    SELECT
      fc2.Ordinal AS [ordinal],
      fc2.ParentColumn AS [owner.physical],
      fc2.ParentAttrName AS [owner.attribute],
      fc2.ReferencedColumn AS [referenced.physical],
      fc2.ReferencedAttrName AS [referenced.attribute]
    FROM #FkColumns fc2
    WHERE fc2.FkObjectId = fc.FkObjectId
    ORDER BY fc2.Ordinal
    FOR JSON PATH
  ), '[]') AS ColumnsJson
INTO #FkColumnsJson
FROM #FkColumns fc
GROUP BY fc.FkObjectId;
CREATE CLUSTERED INDEX IX_FkColumnsJson ON #FkColumnsJson(FkObjectId);

-- 11f) Attribute-to-FK JSON
IF OBJECT_ID('tempdb..#FkAttrJson') IS NOT NULL DROP TABLE #FkAttrJson;
SELECT
  fam.AttrId,
  ISNULL((
    SELECT
      fk.FkName AS [name],
      fk.DeleteAction AS [onDelete],
      fk.UpdateAction AS [onUpdate],
      fk.ReferencedSchema AS [referencedSchema],
      fk.ReferencedTable AS [referencedTable],
      CAST(fk.IsNoCheck AS bit) AS [isNoCheck],
      JSON_QUERY(fkc.ColumnsJson) AS [columns]
    FROM #FkReality fk
    LEFT JOIN #FkColumnsJson fkc ON fkc.FkObjectId = fk.FkObjectId
    WHERE EXISTS (SELECT 1 FROM #FkAttrMap fam2 WHERE fam2.AttrId = fam.AttrId AND fam2.FkObjectId = fk.FkObjectId)
    FOR JSON PATH
  ), '[]') AS ConstraintJson
INTO #FkAttrJson
FROM #FkAttrMap fam
GROUP BY fam.AttrId;
CREATE CLUSTERED INDEX IX_FkAttrJson ON #FkAttrJson(AttrId);

/* --------------------------------------------------------------------------
   Phase 2: Pre-aggregate JSON blobs
----------------------------------------------------------------------------*/

-- Attributes JSON per entity
IF OBJECT_ID('tempdb..#AttrJson') IS NOT NULL DROP TABLE #AttrJson;
SELECT
  en.EntityId,
  ISNULL((
    SELECT
      a.AttrName AS [name],
      COALESCE(NULLIF(a.PhysicalColumnName, ''), NULLIF(a.DatabaseColumnName, ''), a.AttrName) AS [physicalName],
      NULLIF(LTRIM(RTRIM(a.OriginalName)), '') AS [originalName],
      COALESCE(a.DataType, a.OriginalType, a.LegacyType) AS [dataType],
      a.[Length] AS [length],
      a.[Precision] AS [precision],
      COALESCE(a.[Scale], a.Decimals) AS [scale],
      a.DefaultValue AS [default],
      a.IsMandatory AS [isMandatory],
      a.AttrIsActive AS [isActive],
      CAST(CASE WHEN COALESCE(a.IsIdentifier, CASE WHEN a.AttrSSKey = en.PrimaryKeySSKey THEN 1 ELSE 0 END) = 1 THEN 1 ELSE 0 END AS bit) AS [isIdentifier],
      CAST(CASE WHEN COALESCE(a.RefEntityId, r.RefEntityId) IS NOT NULL THEN 1 ELSE 0 END AS int) AS [isReference],
      COALESCE(a.RefEntityId, r.RefEntityId) AS [refEntityId],
      r.RefEntityName AS [refEntity_name],
      r.RefPhysicalName AS [refEntity_physicalName],
      a.DeleteRule AS [reference_deleteRuleCode],
      CAST(ISNULL(h.HasFK, 0) AS int) AS [hasDbConstraint],
      a.ExternalColumnType AS [external_dbType],
      CAST(CASE WHEN a.AttrIsActive = 0 AND pc.AttrId IS NOT NULL THEN 1 ELSE 0 END AS bit) AS [physical_isPresentButInactive],
      CASE WHEN cr.AttrId IS NOT NULL OR chk.AttrId IS NOT NULL THEN JSON_QUERY(
        (SELECT
            CAST(cr.IsNullable AS bit) AS [isNullable],
            cr.SqlType AS [sqlType],
            cr.MaxLength AS [maxLength],
            cr.[Precision] AS [precision],
            cr.[Scale] AS [scale],
            cr.CollationName AS [collation],
            CAST(cr.IsIdentity AS bit) AS [isIdentity],
            CAST(cr.IsComputed AS bit) AS [isComputed],
            cr.ComputedDefinition AS [computedDefinition],
            cr.DefaultDefinition AS [defaultDefinition],
            CASE WHEN cr.DefaultConstraintName IS NOT NULL OR cr.DefaultDefinition IS NOT NULL THEN JSON_QUERY(
                (SELECT
                    cr.DefaultConstraintName AS [name],
                    cr.DefaultDefinition AS [definition],
                    CAST(0 AS bit) AS [isNotTrusted]
                 FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)
            ) END AS [defaultConstraint],
            CASE WHEN chk.CheckJson IS NOT NULL THEN JSON_QUERY(chk.CheckJson) END AS [checkConstraints]
         FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)
      ) END AS [onDisk],
      CASE WHEN NULLIF(LTRIM(RTRIM(a.AttrDescription)), '') IS NOT NULL THEN JSON_QUERY(
        (SELECT NULLIF(LTRIM(RTRIM(a.AttrDescription)), '') AS [description]
         FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)
      ) END AS [meta]
    FROM #Attr a
    LEFT JOIN #RefResolved r ON r.AttrId = a.AttrId
    LEFT JOIN #AttrHasFK h ON h.AttrId = a.AttrId
    LEFT JOIN #PhysColsPresent pc ON pc.AttrId = a.AttrId
    LEFT JOIN #ColumnReality cr ON cr.AttrId = a.AttrId
    LEFT JOIN #AttrCheckJson chk ON chk.AttrId = a.AttrId
    WHERE a.EntityId = en.EntityId
    ORDER BY CASE WHEN COALESCE(a.IsIdentifier, CASE WHEN a.AttrSSKey = en.PrimaryKeySSKey THEN 1 ELSE 0 END) = 1 THEN 0 ELSE 1 END,
             a.AttrName
    FOR JSON PATH
  ), '[]') AS AttributesJson
INTO #AttrJson
FROM #Ent en;
CREATE CLUSTERED INDEX IX_AttrJson ON #AttrJson(EntityId);

-- Relationships JSON per entity
IF OBJECT_ID('tempdb..#RelJson') IS NOT NULL DROP TABLE #RelJson;
SELECT
  en.EntityId,
  ISNULL((
    SELECT DISTINCT
      a.AttrId                           AS [viaAttributeId],
      a.AttrName                         AS [viaAttributeName],
      COALESCE(r.RefEntityName, fk.ReferencedTable)       AS [toEntity_name],
      COALESCE(r.RefPhysicalName, fk.ReferencedTable)     AS [toEntity_physicalName],
      a.DeleteRule                       AS [deleteRuleCode],
      CAST(ISNULL(h.HasFK, 0) AS int)    AS [hasDbConstraint],
      JSON_QUERY(faj.ConstraintJson)     AS [actualConstraints]
    FROM #Attr a
    LEFT JOIN #RefResolved r ON r.AttrId = a.AttrId
    LEFT JOIN #AttrHasFK h ON h.AttrId = a.AttrId
    LEFT JOIN #FkAttrJson faj ON faj.AttrId = a.AttrId
    LEFT JOIN #FkReality fk
      ON fk.EntityId = a.EntityId
     AND EXISTS (
           SELECT 1
           FROM #FkColumns fc
           WHERE fc.FkObjectId = fk.FkObjectId
             AND fc.ParentAttrId = a.AttrId)
    WHERE a.EntityId = en.EntityId
      AND (r.AttrId IS NOT NULL OR fk.FkObjectId IS NOT NULL)
    ORDER BY a.AttrName
    FOR JSON PATH
  ), '[]') AS RelationshipsJson
INTO #RelJson
FROM #Ent en;
CREATE CLUSTERED INDEX IX_RelJson ON #RelJson(EntityId);

-- Index columns JSON
IF OBJECT_ID('tempdb..#IdxColsJson') IS NOT NULL DROP TABLE #IdxColsJson;
SELECT
  m.EntityId,
  m.IndexName,
  ISNULL((
    SELECT m2.HumanAttr AS [attribute], m2.PhysicalColumn AS [physicalColumn], m2.Ordinal AS [ordinal],
           CAST(m2.IsIncluded AS bit) AS [isIncluded],
           m2.Direction AS [direction]
    FROM #IdxColsMapped m2
    WHERE m2.EntityId = m.EntityId AND m2.IndexName = m.IndexName
    ORDER BY m2.Ordinal
    FOR JSON PATH
  ), '[]') AS ColumnsJson
INTO #IdxColsJson
FROM #IdxColsMapped m
GROUP BY m.EntityId, m.IndexName;
CREATE CLUSTERED INDEX IX_IdxColsJson ON #IdxColsJson(EntityId, IndexName);

-- Indexes JSON per entity
IF OBJECT_ID('tempdb..#IdxJson') IS NOT NULL DROP TABLE #IdxJson;
SELECT
  en.EntityId,
  ISNULL((
    SELECT
      ai2.IndexName                         AS [name],
      CAST(ai2.IsPrimary AS bit)            AS [isPrimary],
      ai2.Kind                              AS [kind],
      CAST(ai2.IsUnique AS bit)             AS [isUnique],
      CAST(CASE WHEN ai2.IndexName LIKE 'OSIDX\_%' ESCAPE '\' THEN 1 ELSE 0 END AS int) AS [isPlatformAuto],
      CAST(ai2.IsDisabled AS bit)           AS [isDisabled],
      CAST(ai2.IsPadded AS bit)             AS [isPadded],
      ai2.Fill_Factor                       AS [fill_factor],
      CAST(ai2.IgnoreDupKey AS bit)         AS [ignoreDupKey],
      CAST(ai2.AllowRowLocks AS bit)        AS [allowRowLocks],
      CAST(ai2.AllowPageLocks AS bit)       AS [allowPageLocks],
      CAST(ai2.NoRecompute AS bit)          AS [noRecompute],
      ai2.FilterDefinition                  AS [filterDefinition],
      CASE WHEN ai2.DataSpaceName IS NOT NULL OR ai2.DataSpaceType IS NOT NULL THEN JSON_QUERY(
        (
          SELECT ai2.DataSpaceName AS [name], ai2.DataSpaceType AS [type]
          FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
        )
      ) END                                 AS [dataSpace],
      JSON_QUERY(COALESCE(ai2.PartitionColumnsJson, N'[]')) AS [partitionColumns],
      JSON_QUERY(COALESCE(ai2.DataCompressionJson, N'[]'))  AS [dataCompression],
      JSON_QUERY(icj.ColumnsJson)           AS [columns]
    FROM #AllIdx ai2
    LEFT JOIN #IdxColsJson icj ON icj.EntityId = ai2.EntityId AND icj.IndexName = ai2.IndexName
    WHERE ai2.EntityId = en.EntityId
    ORDER BY ai2.IndexName
    FOR JSON PATH
  ), '[]') AS IndexesJson
INTO #IdxJson
FROM #Ent en;
CREATE CLUSTERED INDEX IX_IdxJson ON #IdxJson(EntityId);

-- Trigger JSON per entity
IF OBJECT_ID('tempdb..#TriggerJson') IS NOT NULL DROP TABLE #TriggerJson;
SELECT
  en.EntityId,
  ISNULL((
    SELECT
      tr2.TriggerName       AS [name],
      CAST(tr2.IsDisabled AS bit) AS [isDisabled],
      tr2.TriggerDefinition AS [definition]
    FROM #Triggers tr2
    WHERE tr2.EntityId = en.EntityId
    ORDER BY tr2.TriggerName
    FOR JSON PATH
  ), '[]') AS TriggersJson
INTO #TriggerJson
FROM #Ent en;
CREATE CLUSTERED INDEX IX_TriggerJson ON #TriggerJson(EntityId);

-- Module JSON (final assembly)
IF OBJECT_ID('tempdb..#ModuleJson') IS NOT NULL DROP TABLE #ModuleJson;
SELECT
  e.EspaceName           AS [module.name],
  e.IsSystemModule       AS [module.isSystem],
  e.ModuleIsActive       AS [module.isActive],
  ISNULL((
    SELECT
      en.EntityName                   AS [name],
      en.PhysicalTableName            AS [physicalName],
      CAST(CASE WHEN en.DataKind = 'staticEntity' THEN 1 ELSE 0 END AS bit) AS [isStatic],
      en.IsExternalEntity             AS [isExternal],
      en.EntityIsActive               AS [isActive],
      DB_NAME()                       AS [db_catalog],
      pt.SchemaName                   AS [db_schema],
      CASE WHEN NULLIF(LTRIM(RTRIM(en.EntityDescription)), '') IS NOT NULL THEN JSON_QUERY(
        (SELECT NULLIF(LTRIM(RTRIM(en.EntityDescription)), '') AS [description]
         FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)
      ) END                           AS [meta],
      JSON_QUERY(aj.AttributesJson)   AS [attributes],
      JSON_QUERY(rj.RelationshipsJson)AS [relationships],
      JSON_QUERY(ij.IndexesJson)      AS [indexes],
      JSON_QUERY(tj.TriggersJson)     AS [triggers]
    FROM #Ent en
    LEFT JOIN #PhysTbls pt  ON pt.EntityId = en.EntityId
    LEFT JOIN sys.schemas s ON s.schema_id = OBJECT_SCHEMA_NAME(pt.object_id, DB_ID())
    LEFT JOIN #AttrJson aj  ON aj.EntityId = en.EntityId
    LEFT JOIN #RelJson  rj  ON rj.EntityId = en.EntityId
    LEFT JOIN #IdxJson  ij  ON ij.EntityId = en.EntityId
    LEFT JOIN #TriggerJson tj ON tj.EntityId = en.EntityId
    WHERE en.EspaceId = e.EspaceId
    ORDER BY en.EntityName
    FOR JSON PATH
  ), '[]') AS [module.entities]
INTO #ModuleJson
FROM #E e;

-- Output root object
SELECT
  JSON_QUERY(ISNULL((
    SELECT
      mj.[module.name]  AS [name],
      mj.[module.isSystem] AS [isSystem],
      mj.[module.isActive] AS [isActive],
      JSON_QUERY(mj.[module.entities]) AS [entities]
    FROM #ModuleJson AS mj
    ORDER BY mj.[module.name]
    FOR JSON PATH
  ), '[]')) AS [modules]
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
