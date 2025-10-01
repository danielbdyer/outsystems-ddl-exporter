/* ===========================
   OutSystems Advanced SQL: One-shot model → JSON
   Inputs:
     @ModuleNamesCsv (Text), @IncludeSystem (Boolean), @OnlyActiveAttributes (Boolean)
   Output: JSON
   =========================== */
WITH ModuleNames AS (
  SELECT LTRIM(RTRIM(value)) AS ModuleName
  FROM STRING_SPLIT(@ModuleNamesCsv, ',')
  WHERE NULLIF(LTRIM(RTRIM(value)), '') IS NOT NULL
),
E AS (
  SELECT e.[Id] EspaceId, e.[Name] EspaceName,
         ISNULL(e.[Is_System],0) IsSystemModule, ISNULL(e.[Is_Active],1) ModuleIsActive
  FROM {Espace} e
  WHERE (@IncludeSystem = 1 OR ISNULL(e.[Is_System],0) = 0)
    AND (NOT EXISTS (SELECT 1 FROM ModuleNames) OR e.[Name] IN (SELECT ModuleName FROM ModuleNames))
),
Ent AS (
  SELECT en.[Id] EntityId, en.[Name] EntityName, en.[Physical_Table_Name] PhysicalTableName,
         ISNULL(en.[Is_Static],0) IsStaticEntity, ISNULL(en.[Is_External],0) IsExternalEntity,
         ISNULL(en.[Is_Active],1) EntityIsActive,
         NULLIF(en.[Db_Catalog],'') DbCatalog, NULLIF(en.[Db_Schema],'') DbSchema,
         en.[Espace_Id] EspaceId
  FROM {Entity} en
  WHERE en.[Espace_Id] IN (SELECT EspaceId FROM E)
),
Attr AS (
  SELECT a.[Id] AttrId, a.[Entity_Id] EntityId, a.[Name] AttrName,
         a.[Physical_Column_Name] PhysicalColumnName, a.[Data_Type] DataType,
         a.[Length] [Length], a.[Precision] [Precision], a.[Scale] [Scale],
         a.[Default_Value] DefaultValue,
         ISNULL(a.[Is_Mandatory],0) IsMandatory,
         ISNULL(a.[Is_Active],1) AttrIsActive, ISNULL(a.[Is_Identifier],0) IsIdentifier,
         a.[Referenced_Entity_Id] RefEntityId,
         a.[Original_Name] OriginalName, a.[External_Column_Type] ExternalColumnType,
         a.[Delete_Rule] DeleteRuleCode
  FROM {Entity_Attr} a
  WHERE (@OnlyActiveAttributes = 0 OR ISNULL(a.[Is_Active],1) = 1)
),
Idx AS (
  SELECT i.[Id] IndexId, i.[Entity_Id] EntityId, i.[Name] IndexName,
         ISNULL(i.[Is_Unique],0) IsUnique, ISNULL(i.[Is_Active],1) IndexIsActive
  FROM {Index} i
),
IdxAttr AS (
  SELECT ia.[Id] IndexAttrId, ia.[Index_Id] IndexId, ia.[Entity_Attr_Id] AttrId, ia.[Sort_Order] OrdinalPosition
  FROM {Index_Attr} ia
),
RefMap AS (
  SELECT a.[Id] AttrId, a.[Entity_Id] FromEntityId, a.[Referenced_Entity_Id] ToEntityId
  FROM Attr a WHERE a.RefEntityId IS NOT NULL
),
AttrAll AS (
  SELECT a.*, en.EntityName, en.PhysicalTableName, en.IsStaticEntity, en.IsExternalEntity, en.DbCatalog, en.DbSchema
  FROM Attr a JOIN Ent en ON en.EntityId = a.EntityId
),
IdxAll AS (
  SELECT i.IndexId, i.EntityId, i.IndexName, i.IsUnique
  FROM Idx i WHERE ISNULL(i.IndexIsActive,1) = 1
),
IdxCols AS (
  SELECT ia.IndexId, ia.AttrId, ia.OrdinalPosition, a.AttrName, a.PhysicalColumnName
  FROM IdxAttr ia JOIN Attr a ON a.AttrId = ia.AttrId
),
EntObj AS (
  SELECT en.EntityId, en.PhysicalTableName,
         QUOTENAME(ISNULL(NULLIF(en.DbSchema,''),'dbo')) + N'.' + QUOTENAME(en.PhysicalTableName) AS TwoPartName
  FROM Ent en
),
PhysCols AS (
  SELECT a.AttrId, sc.column_id AS ColumnId
  FROM AttrAll a JOIN EntObj eo ON eo.EntityId = a.EntityId
  OUTER APPLY (
    SELECT sc.column_id FROM sys.columns sc
    WHERE sc.object_id = OBJECT_ID(eo.TwoPartName) AND sc.name = a.PhysicalColumnName
  ) pc WHERE pc.column_id IS NOT NULL
)
SELECT
  e.EspaceName AS [module.name],
  e.IsSystemModule AS [module.isSystem],
  e.ModuleIsActive AS [module.isActive],
  (
    SELECT
      en.EntityName AS [name],
      en.PhysicalTableName AS [physicalName],
      en.IsStaticEntity AS [isStatic],
      en.IsExternalEntity AS [isExternal],
      en.EntityIsActive AS [isActive],
      en.DbCatalog AS [db_catalog],
      en.DbSchema AS [db_schema],
      (
        SELECT
          a.AttrName AS [name],
          a.PhysicalColumnName AS [physicalName],
          a.OriginalName AS [originalName],
          a.DataType AS [dataType], a.[Length] AS [length],
          a.[Precision] AS [precision], a.[Scale] AS [scale],
          a.DefaultValue AS [default],
          a.IsMandatory AS [isMandatory],
          a.AttrIsActive AS [isActive],
          a.IsIdentifier AS [isIdentifier],
          CASE WHEN a.RefEntityId IS NOT NULL THEN 1 ELSE 0 END AS [isReference],
          a.RefEntityId AS [refEntityId],
          refEn.EntityName AS [refEntity_name],
          refEn.PhysicalTableName AS [refEntity_physicalName],
          a.DeleteRuleCode AS [reference_deleteRuleCode],
          CASE WHEN a.RefEntityId IS NOT NULL AND (a.DeleteRuleCode IN ('Protect','Delete',1,2)) THEN 1 ELSE 0 END AS [reference_hasDbConstraint],
          a.ExternalColumnType AS [external_dbType],
          CASE WHEN a.AttrIsActive = 0 AND EXISTS (SELECT 1 FROM PhysCols pc WHERE pc.AttrId = a.AttrId) THEN 1 ELSE 0 END AS [physical_isPresentButInactive]
        FROM AttrAll a
        LEFT JOIN Ent refEn ON refEn.EntityId = a.RefEntityId
        WHERE a.EntityId = en.EntityId
        ORDER BY a.IsIdentifier DESC, a.AttrName
        FOR JSON PATH
      ) AS [attributes],
      (
        SELECT DISTINCT
          f.AttrId AS [viaAttributeId],
          a.AttrName AS [viaAttributeName],
          toEn.EntityName AS [toEntity_name],
          toEn.PhysicalTableName AS [toEntity_physicalName],
          a.DeleteRuleCode AS [deleteRuleCode],
          CASE WHEN a.DeleteRuleCode IN ('Protect','Delete',1,2) THEN 1 ELSE 0 END AS [hasDbConstraint]
        FROM RefMap f
        JOIN Attr a ON a.AttrId = f.AttrId
        JOIN Ent toEn ON toEn.EntityId = f.ToEntityId
        WHERE f.FromEntityId = en.EntityId
        ORDER BY a.AttrName
        FOR JSON PATH
      ) AS [relationships],
      (
        SELECT
          i.IndexName AS [name],
          i.IsUnique AS [isUnique],
          CASE WHEN i.IndexName LIKE 'OSIDX\_%' ESCAPE '\\' THEN 1 ELSE 0 END AS [isPlatformAuto],
          (
            SELECT
              c.AttrName AS [attribute],
              c.PhysicalColumnName AS [physicalColumn],
              c.OrdinalPosition AS [ordinal]
            FROM IdxCols c
            WHERE c.IndexId = i.IndexId
            ORDER BY c.OrdinalPosition
            FOR JSON PATH
          ) AS [columns]
        FROM IdxAll i
        WHERE i.EntityId = en.EntityId
        ORDER BY i.IndexName
        FOR JSON PATH
      ) AS [indexes]
    FROM Ent en
    WHERE en.EspaceId = e.EspaceId
    ORDER BY en.EntityName
    FOR JSON PATH
  ) AS [module.entities]
FROM E e
ORDER BY e.EspaceName
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
