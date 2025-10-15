# `outsystems_model_export.sql` Reverse-Engineering Notes

The `src/AdvancedSql/outsystems_model_export.sql` script drives the legacy one-shot metadata export. It runs in two phases: phase 1 materializes normalized temp tables and phase 2 projects JSON aggregates. The notes below break down every table, normalization rule, and JSON projection so the managed pipeline can recreate the exact payload without depending on SQL Server JSON assembly.

## Parameters and global session state

* Inputs: `@ModuleNamesCsv` (comma delimited names), `@IncludeSystem` (BIT), `@OnlyActiveAttributes` (BIT).【F:src/AdvancedSql/outsystems_model_export.sql†L6-L17】
* Session options: `SET NOCOUNT ON;` and `SET TEXTSIZE -1;` to avoid row-count noise and enforce unlimited `(n)varchar(max)` streaming.【F:src/AdvancedSql/outsystems_model_export.sql†L11-L12】

## Module selection (`#ModuleNames`, `#E`)

1. `#ModuleNames` stores trimmed module names from the CSV input. Empty tokens are discarded.【F:src/AdvancedSql/outsystems_model_export.sql†L22-L28】
2. `#E` pulls modules from `dbo.ossys_Espace`, applying two filters:
   * System modules are included only when `@IncludeSystem = 1`.
   * When a name list is supplied, the module name (database collation) must match an entry in `#ModuleNames`.
   * Columns captured: id, name, system flag, active flag, kind, SS key (converted to `uniqueidentifier`).【F:src/AdvancedSql/outsystems_model_export.sql†L30-L46】

## Entity normalization (`#Ent`)

`#Ent` restricts `dbo.ossys_Entity` to modules present in `#E` and applies the same system filter. Captured fields include physical table name, data kind, external flag, and converted SS keys. Descriptions are resolved after the fact:

* The script probes for `Description` then `Description_Translation`. If either column exists it updates `EntityDescription` with the trimmed value, otherwise it remains `NULL`.【F:src/AdvancedSql/outsystems_model_export.sql†L48-L85】

## Attribute normalization (`#Attr`)

Attributes are loaded via dynamic SQL because OutSystems payloads differ across platform versions. The builder inspects optional columns (`Data_Type`, `Type`, `Length`, `Precision`, `Decimals`, `Original_Name`, etc.) and composes an `INSERT` statement that only references columns that exist. Important behaviors:

* `DataType` prefers `Data_Type` over `Type` when both exist; `Scale` falls back to `Decimals` the same way.【F:src/AdvancedSql/outsystems_model_export.sql†L93-L143】
* Physical column names favor `Physical_Column_Name`; `Database_Name` is the fallback, but both are normalized later if empty.【F:src/AdvancedSql/outsystems_model_export.sql†L112-L140】【F:src/AdvancedSql/outsystems_model_export.sql†L152-L159】
* `AttrDescription` is populated from `Description` or `Description_Translation` when available.【F:src/AdvancedSql/outsystems_model_export.sql†L119-L137】
* After load, any blank `PhysicalColumnName` is replaced with `DatabaseColumnName`.【F:src/AdvancedSql/outsystems_model_export.sql†L148-L159】

## Reference resolution (`#RefResolved`)

To map logical references back to entities, the script joins attributes to `#Ent` in four passes:

1. Direct `RefEntityId` match.
2. SS key match derived from the `LegacyType` pattern `bt*<module guid>*<entity guid>`.
3. Fallback lookups against `dbo.ossys_Entity` / `dbo.ossys_Espace` when the entity was filtered out of `#Ent` (e.g., system modules suppressed).【F:src/AdvancedSql/outsystems_model_export.sql†L161-L210】

This produces `RefEntityId`, `RefEntityName`, and `RefPhysicalName` per attribute where any evidence is available.【F:src/AdvancedSql/outsystems_model_export.sql†L180-L212】

## Physical table lookup (`#PhysTbls`) and triggers (`#Triggers`)

* `#PhysTbls` maps entity ids to `sys.tables`/`sys.schemas` metadata so downstream catalog queries can be scoped per entity.【F:src/AdvancedSql/outsystems_model_export.sql†L214-L225】
* `#Triggers` captures table triggers, storing name, disabled flag, and full definition text.【F:src/AdvancedSql/outsystems_model_export.sql†L227-L235】

## Column reality and checks (`#ColumnReality`, `#ColumnCheckReality`, `#AttrCheckJson`)

* `#ColumnReality` projects actual catalog state for every logical attribute: nullability, SQL type, computed/default expressions, collation, identity flag, and the concrete column name. Unicode types halve `max_length` to reflect character counts.【F:src/AdvancedSql/outsystems_model_export.sql†L237-L275】
* `#ColumnCheckReality` captures column-scoped check constraints, later grouped into JSON via `#AttrCheckJson`. Each constraint carries name, definition, and trust flag.【F:src/AdvancedSql/outsystems_model_export.sql†L277-L306】
* `#AttrCheckJson` aggregates the checks per attribute into `[name, definition, isNotTrusted]` JSON fragments for quick embedding in the final payload.【F:src/AdvancedSql/outsystems_model_export.sql†L308-L319】

## Physical presence tracking (`#PhysColsPresent`)

Any attribute with a matching entry in `#ColumnReality` is marked as still present on disk; the flag is later used to detect inactive-but-present columns.【F:src/AdvancedSql/outsystems_model_export.sql†L321-L329】

## Index catalog (`#AllIdx`, `#IdxColsMapped`)

* `#AllIdx` enumerates non-heap indexes (PK, UQ, regular) with key options, filter definitions, data space metadata, partitioning JSON, and per-partition compression JSON. Platform-generated names (`OSIDX_%`) are flagged for downstream policy decisions.【F:src/AdvancedSql/outsystems_model_export.sql†L331-L392】【F:src/AdvancedSql/outsystems_model_export.sql†L414-L433】
* `#IdxColsMapped` resolves index key/included columns back to logical attributes by comparing normalized physical names. Included columns receive high ordinal values to preserve ordering semantics. The mapping also backfills missing `PhysicalColumnName` values on attributes.【F:src/AdvancedSql/outsystems_model_export.sql†L394-L452】

## Foreign key evidence (`#FkReality`, `#FkColumns`, `#FkAttrMap`, `#AttrHasFK`, `#FkColumnsJson`, `#FkAttrJson`)

1. `#FkReality` pulls catalog foreign keys with delete/update actions, trust flag, and referenced tables.【F:src/AdvancedSql/outsystems_model_export.sql†L454-L471】
2. `#FkColumns` matches FK columns to logical attributes on both parent and referenced entities, using the normalized physical names to locate attribute ids.【F:src/AdvancedSql/outsystems_model_export.sql†L473-L505】
3. `#FkAttrMap` associates each parent attribute with the FK constraint(s) that reference it. `#AttrHasFK` filters out constraints whose referenced entity/table does not corroborate the logical reference, yielding a definitive “has catalog FK” flag per attribute.【F:src/AdvancedSql/outsystems_model_export.sql†L507-L535】
4. `#FkColumnsJson` and `#FkAttrJson` aggregate FK column mappings and constraint metadata into JSON arrays for embedding alongside attribute relationships.【F:src/AdvancedSql/outsystems_model_export.sql†L537-L568】

## Attribute JSON (`#AttrJson`)

Attributes per entity are emitted with the following normalization rules before JSON aggregation:

* `physicalName` prefers `PhysicalColumnName`, then `DatabaseColumnName`, then `AttrName`.
* `dataType` chooses `DataType`, `OriginalType`, then `LegacyType`.
* Identifier detection: `IsIdentifier` falls back to `AttrSSKey == en.PrimaryKeySSKey` when the column is missing.
* `isReference` and `ref*` fields use the resolved entity evidence (`#RefResolved`).
* `hasDbConstraint` leverages `#AttrHasFK`.
* `physical_isPresentButInactive` is true when an inactive attribute still has a physical column.
* `onDisk` JSON is only included when column or check evidence exists, embedding nullability, SQL type, identity/computed flags, default/check constraint metadata, and trust indicators.
* Descriptions populate the `meta.description` node when non-empty.【F:src/AdvancedSql/outsystems_model_export.sql†L570-L643】

## Relationship JSON (`#RelJson`)

Relationships walk each attribute that either has logical reference evidence or a catalog FK:

* `toEntity` name/physical name prefer logical resolution (`#RefResolved`) but fall back to catalog FK targets when missing.
* `hasDbConstraint` is derived from `#AttrHasFK`.
* `actualConstraints` embeds the FK constraint JSON from `#FkAttrJson` when present.【F:src/AdvancedSql/outsystems_model_export.sql†L645-L673】

## Index and trigger JSON (`#IdxColsJson`, `#IdxJson`, `#TriggerJson`)

* `#IdxColsJson` flattens mapped index columns into `[attribute, physicalColumn, ordinal, isIncluded, direction]` JSON arrays.【F:src/AdvancedSql/outsystems_model_export.sql†L675-L694】
* `#IdxJson` projects each index with primary/unique flags, platform auto-detection, filter, data space info, partition/compression JSON, and the column JSON from `#IdxColsJson`.【F:src/AdvancedSql/outsystems_model_export.sql†L696-L733】
* `#TriggerJson` lists triggers with disabled flag and body text, ordered by trigger name.【F:src/AdvancedSql/outsystems_model_export.sql†L735-L750】

## Module assembly (`#ModuleJson`) and final payload

`#ModuleJson` collects per-module JSON using the aggregated attribute, relationship, index, and trigger blobs:

* Entity-level metadata includes `isStatic` from `DataKind`, external/active flags, `DB_NAME()` for catalog, schema from `#PhysTbls`, and optional `meta.description` when the entity description is non-empty.
* JSON fragments from `#AttrJson`, `#RelJson`, `#IdxJson`, and `#TriggerJson` are embedded using `JSON_QUERY` to preserve object/array semantics.【F:src/AdvancedSql/outsystems_model_export.sql†L752-L792】

Finally, the script wraps the module rows into `{ "modules": [...] }` via nested `FOR JSON PATH` calls.【F:src/AdvancedSql/outsystems_model_export.sql†L794-L807】

## Key takeaways for the managed reader

* Every logical attribute is traced back to physical catalog evidence (column presence, computed/default definitions, check constraints, foreign keys, index participation).
* Reference resolution combines logical metadata (`RefEntityId`, `LegacyType` SS keys) with live catalog verification to avoid stale cross-module links.
* JSON fragments are deliberately composed with `JSON_QUERY` to prevent double-encoding; consumers must preserve these semantics when rebuilding JSON outside SQL Server.
* Module filtering logic must honor both `@IncludeSystem` and the CSV list exactly as implemented above to avoid silently dropping modules.
