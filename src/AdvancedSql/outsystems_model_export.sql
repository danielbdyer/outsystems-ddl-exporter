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
CREATE TABLE #ModuleNames ( ModuleName NVARCHAR(200) COLLATE DATABASE_DEFAULT NOT NULL );
INSERT INTO #ModuleNames(ModuleName)
SELECT LTRIM(RTRIM(value))
FROM STRING_SPLIT(@ModuleNamesCsv, ',')
WHERE NULLIF(LTRIM(RTRIM(value)), '') IS NOT NULL;

-- 2) #E (espace) with module-level IncludeSystem filtering
IF OBJECT_ID('tempdb..#E') IS NOT NULL DROP TABLE #E;
SELECT
    e.[Id]                                AS EspaceId,
    e.[Name]                              AS EspaceName,
    CAST(ISNULL(e.[Is_System],0) AS bit)  AS IsSystemModule,
    CAST(ISNULL(e.[Is_Active],1) AS bit)  AS ModuleIsActive,
    e.[EspaceKind],
    e.[SS_Key]                            AS EspaceSSKey
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
    en.[Id]                                 AS EntityId,
    en.[Name]                               AS EntityName,
    en.[Physical_Table_Name]                AS PhysicalTableName,
    en.[Espace_Id]                          AS EspaceId,
    CAST(ISNULL(en.[Is_Active],1)   AS bit) AS EntityIsActive,
    CAST(ISNULL(en.[Is_System],0)   AS bit) AS IsSystemEntity,
    CAST(ISNULL(en.[Is_External],0) AS bit) AS IsExternalEntity,
    en.[Data_Kind]                          AS DataKind,
    NULLIF(en.[Db_Catalog],'')              AS DbCatalog,
    NULLIF(en.[Db_Schema],'')               AS DbSchema,
    NULLIF(en.[External_Db_Name],'')        AS ExternalDbCatalog,
    NULLIF(en.[External_Db_Owner],'')       AS ExternalDbSchema,
    en.[PrimaryKey_SS_Key]                  AS PrimaryKeySSKey,
    en.[SS_Key]                             AS EntitySSKey
INTO #Ent
FROM dbo.ossys_Entity en
JOIN #E ON #E.EspaceId = en.[Espace_Id]
WHERE (@IncludeSystem = 1 OR ISNULL(en.[Is_System],0) = 0);
CREATE CLUSTERED INDEX IX_Ent ON #Ent(EntityId);
CREATE NONCLUSTERED INDEX IX_Ent_Espace ON #Ent(EspaceId, PhysicalTableName);

-- 4) #Attr (Entity_Attr)
IF OBJECT_ID('tempdb..#Attr') IS NOT NULL DROP TABLE #Attr;
SELECT
    a.[Id]                                   AS AttrId,
    a.[Entity_Id]                            AS EntityId,
    a.[Name]                                 AS AttrName,
    a.[SS_Key]                               AS AttrSSKey,
    a.[Type]                                 AS [Type],
    a.[Data_Type]                            AS DataType,
    a.[Length]                               AS [Length],
    a.[Precision]                            AS [Precision],
    a.[Scale]                                AS [Scale],
    a.[Decimals]                             AS [Decimals],
    a.[Default_Value]                        AS DefaultValue,
    CAST(ISNULL(a.[Is_Mandatory],0)   AS bit) AS IsMandatory,
    CAST(ISNULL(a.[Is_Active],1)     AS bit) AS AttrIsActive,
    CAST(ISNULL(a.[Is_Identifier],0) AS bit) AS IsIdentifier,
    CAST(ISNULL(a.[Is_AutoNumber],0) AS bit) AS IsAutoNumber,
    CAST(a.[Delete_Rule] AS NVARCHAR(20))    AS DeleteRule,
    NULLIF(a.[Original_Name],'')             AS OriginalName,
    NULLIF(a.[Physical_Column_Name],'')      AS PhysicalColumnName,
    NULLIF(a.[External_Db_Type],'')          AS ExternalDbType,
    a.[Referenced_Entity_Id]                 AS ReferencedEntityId,
    a.[Original_Type]                        AS OriginalType
INTO #Attr
FROM dbo.ossys_Entity_Attr a
JOIN #Ent ON #Ent.EntityId = a.[Entity_Id]
WHERE (@OnlyActiveAttributes = 0 OR ISNULL(a.[Is_Active],1) = 1);
CREATE CLUSTERED INDEX IX_Attr ON #Attr(EntityId, AttrId);
CREATE NONCLUSTERED INDEX IX_Attr_Name ON #Attr(AttrName);

-- 5) Resolve references: parse TYPE = 'bt*' + <Espace_SS_Key> + '*' + <Entity_SS_Key>
IF OBJECT_ID('tempdb..#RefResolved') IS NOT NULL DROP TABLE #RefResolved;
WITH ParsedRef AS
(
  SELECT
    a.AttrId,
    CASE WHEN a.[Type] LIKE 'bt*%' THEN SUBSTRING(a.[Type], 3, 36) END                 AS RefEspaceSSKey,
    CASE WHEN a.[Type] LIKE 'bt*%' THEN SUBSTRING(a.[Type], CHARINDEX('*', a.[Type])+1, 36) END AS RefEntitySSKey
  FROM #Attr a
)
SELECT
    a.AttrId,
    eTarget.EntityId         AS RefEntityId,
    eTarget.EntityName       AS RefEntityName,
    eTarget.PhysicalTableName AS RefPhysicalName
INTO #RefResolved
FROM ParsedRef a
JOIN #Ent eTarget
  ON eTarget.EntitySSKey = a.RefEntitySSKey
JOIN #E eTargetModule
  ON eTargetModule.EspaceId   = eTarget.EspaceId
 AND eTargetModule.EspaceSSKey = a.RefEspaceSSKey;  -- ensure uniqueness across modules/clones
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

-- 7) Physical column presence (prefer physical name, fallback to logical)
IF OBJECT_ID('tempdb..#PhysColsPresent') IS NOT NULL DROP TABLE #PhysColsPresent;
SELECT DISTINCT a.AttrId
INTO #PhysColsPresent
FROM #Attr a
JOIN #PhysTbls pt       ON pt.EntityId = a.EntityId
JOIN sys.columns c      ON c.object_id = pt.object_id
AND c.[name] COLLATE DATABASE_DEFAULT = COALESCE(a.PhysicalColumnName, a.AttrName) COLLATE DATABASE_DEFAULT;
CREATE CLUSTERED INDEX IX_PhysColsPresent ON #PhysColsPresent(AttrId);

-- 8) Backfill #Attr.PhysicalColumnName if blank/NULL using sys.columns
UPDATE a
SET a.PhysicalColumnName = c.[name]
FROM #Attr a
JOIN #PhysTbls pt ON pt.EntityId = a.EntityId
JOIN sys.columns c ON c.object_id = pt.object_id
WHERE (a.PhysicalColumnName IS NULL OR a.PhysicalColumnName = '')
  AND (c.[name] COLLATE Latin1_General_CI_AI = a.AttrName COLLATE Latin1_General_CI_AI);

-- 9) Index catalog (IX + UQ + PK)
IF OBJECT_ID('tempdb..#AllIdx') IS NOT NULL DROP TABLE #AllIdx;
SELECT
    en.EntityId,
    i.object_id,
    i.index_id,
    i.[name] AS IndexName,
    CAST(i.is_unique AS bit) AS IsUnique
INTO #AllIdx
FROM #PhysTbls pt
JOIN #Ent en ON en.EntityId = pt.EntityId
JOIN sys.indexes i ON i.object_id = pt.object_id
WHERE i.[type_desc] <> 'HEAP' AND i.is_primary_key = 0 AND i.is_unique_constraint = 0;

INSERT INTO #AllIdx(EntityId, object_id, index_id, IndexName, IsUnique)
SELECT en.EntityId, i.object_id, i.index_id, kc.[name], CAST(1 AS bit)
FROM #PhysTbls pt
JOIN #Ent en ON en.EntityId = pt.EntityId
JOIN sys.key_constraints kc
  ON kc.parent_object_id = pt.object_id AND kc.[type] = 'UQ';

INSERT INTO #AllIdx(EntityId, object_id, index_id, IndexName, IsUnique)
SELECT en.EntityId, i.object_id, i.index_id, kc.[name], CAST(1 AS bit)
FROM #PhysTbls pt
JOIN #Ent en ON en.EntityId = pt.EntityId
JOIN sys.key_constraints kc
  ON kc.parent_object_id = pt.object_id AND kc.[type] = 'PK'
JOIN sys.indexes i
  ON i.object_id = kc.parent_object_id AND i.index_id = kc.unique_index_id;

CREATE CLUSTERED INDEX IX_AllIdx ON #AllIdx(EntityId, IndexName);

-- 10) Index columns (keys + included) mapped back to attributes by physical or human name
IF OBJECT_ID('tempdb..#IdxColsMapped') IS NOT NULL DROP TABLE #IdxColsMapped;
WITH IdxColsKeys AS
(
  SELECT ai.EntityId, ai.IndexName, ic.key_ordinal AS Ordinal, c.[name] AS PhysicalColumn
  FROM #AllIdx ai
  JOIN sys.index_columns ic ON ic.object_id = ai.object_id AND ic.index_id = ai.index_id
  JOIN sys.columns c        ON c.object_id = ai.object_id AND c.column_id = ic.column_id
  WHERE ic.is_included_column = 0
),
IdxColsIncl AS
(
  SELECT ai.EntityId, ai.IndexName,
         100000 + ROW_NUMBER() OVER (PARTITION BY ai.object_id, ai.index_id ORDER BY ic.column_id) AS Ordinal,
         c.[name] AS PhysicalColumn
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
    COALESCE(a.PhysicalColumnName, a.AttrName) AS HumanAttr
INTO #IdxColsMapped
FROM IdxColsAll i
LEFT JOIN #Attr a
  ON a.EntityId = i.EntityId
 AND COALESCE(a.PhysicalColumnName, a.AttrName) COLLATE Latin1_General_CI_AI
     = i.PhysicalColumn COLLATE Latin1_General_CI_AI;
CREATE CLUSTERED INDEX IX_IdxColsMapped ON #IdxColsMapped(EntityId, IndexName, Ordinal);

-- 10b) Backfill from index mapping when present
UPDATE a
SET a.PhysicalColumnName = m.PhysicalColumn
FROM #Attr a
JOIN #IdxColsMapped m  ON m.EntityId = a.EntityId
WHERE (a.PhysicalColumnName IS NULL OR a.PhysicalColumnName = '')
  AND (m.HumanAttr COLLATE Latin1_General_CI_AI = a.AttrName COLLATE Latin1_General_CI_AI
       OR m.PhysicalColumn COLLATE Latin1_General_CI_AI = a.AttrName COLLATE Latin1_General_CI_AI);

-- 11) FK map & attribute-level FK existence
IF OBJECT_ID('tempdb..#FkMap') IS NOT NULL DROP TABLE #FkMap;
SELECT
    en.EntityId,
    fk.object_id AS FkObjectId,
    STUFF(
      (SELECT ', ' + QUOTENAME(c1.[name])
       FROM sys.foreign_key_columns fkc2
       JOIN sys.columns c1 ON c1.object_id = fkc2.parent_object_id AND c1.column_id = fkc2.parent_column_id
       WHERE fkc2.constraint_object_id = fk.object_id
       ORDER BY fkc2.constraint_column_id
       FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)')
    ,1,2,'') AS OwnerCols,
    t2.[name] AS RefTable
INTO #FkMap
FROM #PhysTbls pt
JOIN #Ent en ON en.EntityId = pt.EntityId
JOIN sys.foreign_keys fk ON fk.parent_object_id = pt.object_id
JOIN sys.tables t2       ON t2.object_id = fk.referenced_object_id;
CREATE CLUSTERED INDEX IX_FkMap ON #FkMap(EntityId);

-- 11b) Attribute-level actual FK existence
IF OBJECT_ID('tempdb..#AttrHasFK') IS NOT NULL DROP TABLE #AttrHasFK;
SELECT DISTINCT a.AttrId, CAST(1 AS bit) AS HasFK
INTO #AttrHasFK
FROM #Attr a
JOIN #RefResolved r ON r.AttrId = a.AttrId
JOIN #FkMap fk
  ON fk.OwnerCols COLLATE DATABASE_DEFAULT LIKE ('%[' + COALESCE(a.PhysicalColumnName, a.AttrName) + ']%')
 COLLATE DATABASE_DEFAULT
 AND fk.RefTable  COLLATE DATABASE_DEFAULT = r.RefPhysicalName COLLATE DATABASE_DEFAULT;
CREATE CLUSTERED INDEX IX_AttrHasFK ON #AttrHasFK(AttrId);

/* --------------------------------------------------------------------------
   Phase 2: Pre-aggregate JSON blobs
----------------------------------------------------------------------------*/

-- Attributes JSON per entity
IF OBJECT_ID('tempdb..#AttrJson') IS NOT NULL DROP TABLE #AttrJson;
SELECT
  en.EntityId,
  (
    SELECT
      a.AttrName                               AS [name],
      COALESCE(a.PhysicalColumnName, a.AttrName) AS [physicalName],
      a.OriginalName                           AS [originalName],
      a.DataType                               AS [dataType],
      a.[Length]                               AS [length],
      a.[Precision]                            AS [precision],
      a.[Scale]                                AS [scale],
      a.[DefaultValue]                         AS [default],
      a.IsMandatory                            AS [isMandatory],
      a.AttrIsActive                           AS [isActive],
      a.IsIdentifier                           AS [isIdentifier],
      CAST(CASE WHEN r.AttrId IS NOT NULL THEN 1 ELSE 0 END AS int) AS [isReference],
      r.RefEntityId                            AS [refEntityId],
      r.RefEntityName                          AS [refEntity_name],
      r.RefPhysicalName                        AS [refEntity_physicalName],
      a.DeleteRule                             AS [reference_deleteRuleCode],
      CAST(CASE WHEN fk.HasFK = 1 THEN 1 ELSE 0 END AS int) AS [reference_hasDbConstraint],
      a.ExternalDbType                         AS [external_dbType],
      CAST(CASE WHEN a.AttrIsActive = 0 AND pc.AttrId IS NOT NULL THEN 1 ELSE 0 END AS int) AS [physical_isPresentButInactive]
    FROM #Attr a
    LEFT JOIN #RefResolved r ON r.AttrId = a.AttrId
    LEFT JOIN #AttrHasFK fk ON fk.AttrId = a.AttrId
    LEFT JOIN #PhysColsPresent pc ON pc.AttrId = a.AttrId
    WHERE a.EntityId = en.EntityId
    ORDER BY CASE WHEN a.IsIdentifier = 1 THEN 0 ELSE 1 END, a.AttrName
    FOR JSON PATH
  ) AS AttributesJson
INTO #AttrJson
FROM #Ent en;
CREATE CLUSTERED INDEX IX_AttrJson ON #AttrJson(EntityId);

-- Relationships JSON per entity
IF OBJECT_ID('tempdb..#RelJson') IS NOT NULL DROP TABLE #RelJson;
SELECT
  en.EntityId,
  (
    SELECT DISTINCT
      a.AttrId                           AS [viaAttributeId],
      a.AttrName                         AS [viaAttributeName],
      r.RefEntityName                    AS [toEntity_name],
      r.RefPhysicalName                  AS [toEntity_physicalName],
      a.DeleteRule                       AS [deleteRuleCode],
      CAST(CASE WHEN fk.HasFK = 1 THEN 1 ELSE 0 END AS int) AS [hasDbConstraint]
    FROM #Attr a
    JOIN #RefResolved r ON r.AttrId = a.AttrId
    LEFT JOIN #AttrHasFK fk ON fk.AttrId = a.AttrId
    WHERE a.EntityId = en.EntityId
    ORDER BY a.AttrName
    FOR JSON PATH
  ) AS RelationshipsJson
INTO #RelJson
FROM #Ent en;
CREATE CLUSTERED INDEX IX_RelJson ON #RelJson(EntityId);

-- Index columns JSON
IF OBJECT_ID('tempdb..#IdxColsJson') IS NOT NULL DROP TABLE #IdxColsJson;
SELECT
  m.EntityId,
  m.IndexName,
  (
    SELECT m2.HumanAttr AS [attribute], m2.PhysicalColumn AS [physicalColumn], m2.Ordinal AS [ordinal]
    FROM #IdxColsMapped m2
    WHERE m2.EntityId = m.EntityId AND m2.IndexName = m.IndexName
    ORDER BY m2.Ordinal
    FOR JSON PATH
  ) AS ColumnsJson
INTO #IdxColsJson
FROM #IdxColsMapped m
GROUP BY m.EntityId, m.IndexName;
CREATE CLUSTERED INDEX IX_IdxColsJson ON #IdxColsJson(EntityId, IndexName);

-- Indexes JSON per entity
IF OBJECT_ID('tempdb..#IdxJson') IS NOT NULL DROP TABLE #IdxJson;
SELECT
  ai.EntityId,
  (
    SELECT
      ai2.IndexName                         AS [name],
      CAST(ai2.IsUnique AS bit)             AS [isUnique],
      CAST(CASE WHEN ai2.IndexName LIKE 'OSIDX\_%' ESCAPE '\\' THEN 1 ELSE 0 END AS int) AS [isPlatformAuto],
      JSON_QUERY(icj.ColumnsJson)           AS [columns]
    FROM #AllIdx ai2
    LEFT JOIN #IdxColsJson icj ON icj.EntityId = ai2.EntityId AND icj.IndexName = ai2.IndexName
    WHERE ai2.EntityId = ai.EntityId
    ORDER BY ai2.IndexName
    FOR JSON PATH
  ) AS IndexesJson
INTO #IdxJson
FROM #AllIdx ai
GROUP BY ai.EntityId;
CREATE CLUSTERED INDEX IX_IdxJson ON #IdxJson(EntityId);

-- Module JSON (final assembly)
IF OBJECT_ID('tempdb..#ModuleJson') IS NOT NULL DROP TABLE #ModuleJson;
SELECT
  e.EspaceName           AS [module.name],
  e.IsSystemModule       AS [module.isSystem],
  e.ModuleIsActive       AS [module.isActive],
  (
    SELECT
      en.EntityName                   AS [name],
      en.PhysicalTableName            AS [physicalName],
      CAST(CASE WHEN en.Data_Kind = 'staticEntity' THEN 1 ELSE 0 END AS bit) AS [isStatic],
      en.IsExternalEntity             AS [isExternal],
      en.EntityIsActive               AS [isActive],
      COALESCE(en.DbCatalog, CASE WHEN en.IsExternalEntity = 1 THEN en.ExternalDbCatalog ELSE DB_NAME() END) AS [db_catalog],
      CASE WHEN en.IsExternalEntity = 1
           THEN COALESCE(en.DbSchema, en.ExternalDbSchema)
           ELSE COALESCE(pt.SchemaName, en.DbSchema, 'dbo')
      END                             AS [db_schema],
      JSON_QUERY(aj.AttributesJson)   AS [attributes],
      JSON_QUERY(rj.RelationshipsJson)AS [relationships],
      JSON_QUERY(ij.IndexesJson)      AS [indexes]
    FROM #Ent en
    LEFT JOIN #PhysTbls pt  ON pt.EntityId = en.EntityId
    LEFT JOIN #AttrJson aj  ON aj.EntityId = en.EntityId
    LEFT JOIN #RelJson  rj  ON rj.EntityId = en.EntityId
    LEFT JOIN #IdxJson  ij  ON ij.EntityId = en.EntityId
    WHERE en.EspaceId = e.EspaceId
    ORDER BY en.EntityName
    FOR JSON PATH
  ) AS [module.entities]
INTO #ModuleJson
FROM #E e;

-- Output root object
SELECT
  JSON_QUERY((
    SELECT
      mj.[module.name]  AS [name],
      mj.[module.isSystem] AS [isSystem],
      mj.[module.isActive] AS [isActive],
      JSON_QUERY(mj.[module.entities]) AS [entities]
    FROM #ModuleJson AS mj
    ORDER BY mj.[module.name]
    FOR JSON PATH
  )) AS [modules]
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
