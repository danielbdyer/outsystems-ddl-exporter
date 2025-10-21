# OutSystems Metadata Research Notes

This note distills the reference information gathered while expanding the advanced SQL extractor. It captures the logical intent encoded in the OutSystems metadata tables and how the exporter reconciles that intent with SQL Server catalogs.

## Modules (`ossys_Espace`)
- `Id`, `Name`, `SS_Key`, `Is_System`, and `Is_Active` are the authoritative module identity fields. Filtering on these columns lets the extractor honour the `@IncludeSystem` and `@ModuleNamesCsv` switches without losing referential context. 【F:src/AdvancedSql/outsystems_model_export.sql†L26-L62】
- Modules remain the unit of scoping throughout the extractor, so downstream queries (entities, attributes, indexes) always join back through `Espace_Id` and the temp table populated in step 2. 【F:src/AdvancedSql/outsystems_model_export.sql†L26-L112】

- Each entity row carries both logical identifiers (`Name`, `SS_Key`) and the desired physical table (`Physical_Table_Name`). These values are cached in `#Ent` together with state flags (`Is_Active`, `Is_System`, `Is_External`) and the human-authored description so downstream consumers can emit documentation surfaces. 【F:src/AdvancedSql/outsystems_model_export.sql†L32-L105】
- The extractor cross-references `sys.tables`/`sys.schemas` by table name to discover schema ownership and object ids, which later power column, index, and foreign-key joins. 【F:src/AdvancedSql/outsystems_model_export.sql†L173-L215】

## Attributes (`ossys_Entity_Attr`)
- Attribute metadata exposes rich logical signals: `SS_Key`, `Data_Type`, `Length`, `Precision`, `Scale/Decimals`, `Default_Value`, `Is_Mandatory`, `Is_Identifier`, `Is_AutoNumber`, `Referenced_Entity_Id`, `Delete_Rule`, `Original_Name`, `External_Column_Type`, and two possible physical name columns (`Physical_Column_Name`, `Database_Name`). 【F:src/AdvancedSql/outsystems_model_export.sql†L74-L165】
- Because older platform versions surface different column sets (`Data_Type` vs. `Type`, `Scale` vs. `Decimals`), the extractor now composes a dynamic `INSERT` statement that only references columns proven to exist via `COL_LENGTH`. This keeps the query portable while still emitting the richest possible metadata. 【F:src/AdvancedSql/outsystems_model_export.sql†L74-L165】
- Attribute physical names are normalised by preferring `Physical_Column_Name`, falling back to `Database_Name`, and ultimately the logical attribute name. This normalisation feeds column presence checks, index column reconciliation, and FK detection. 【F:src/AdvancedSql/outsystems_model_export.sql†L167-L218】
- Reference resolution first trusts the direct `Referenced_Entity_Id` column and then falls back to parsing the legacy `bt*<EspaceSSKey>*<EntitySSKey>` encoding stored in `Type`. This preserves cross-module relationships even when clones share names. 【F:src/AdvancedSql/outsystems_model_export.sql†L220-L242】
- Column truth now flows through `#ColumnReality`, which joins `sys.columns`, `sys.types`, `sys.default_constraints`, and `sys.computed_columns` to report nullability, SQL type, collation, identity/computed flags, and default expressions. The JSON projection emits this object under `attributes[].onDisk` so tightening, SSDT emission, and diagnostics can reason about physical reality. 【F:src/AdvancedSql/outsystems_model_export.sql†L217-L288】【F:src/AdvancedSql/outsystems_model_export.sql†L349-L378】
- Designer descriptions from `ossys_Entity_Attr` land in `attributes[].meta`, enabling documentation pipelines or MS_Description round-tripping. 【F:src/AdvancedSql/outsystems_model_export.sql†L86-L165】【F:readme.md†L209-L235】

## Indexes & Physical Evidence
- Index metadata is drawn from `sys.indexes`, `sys.key_constraints`, and `sys.index_columns`, then mapped back to attributes by comparing normalised physical names. Included columns are ordered after key columns by assigning ordinal offsets starting at 100000. 【F:src/AdvancedSql/outsystems_model_export.sql†L245-L318】
- Column presence and inactivity flags combine metadata intent (`Is_Active`) with `sys.columns` to highlight retired attributes that still exist physically (`physical_isPresentButInactive`). 【F:src/AdvancedSql/outsystems_model_export.sql†L217-L288】【F:src/AdvancedSql/outsystems_model_export.sql†L349-L381】
- Index column JSON now includes `direction` (`ASC`/`DESC`) and `isIncluded` so query planners and diff tooling understand the exact sort semantics. 【F:src/AdvancedSql/outsystems_model_export.sql†L282-L341】【F:readme.md†L271-L287】
- Foreign-key reality is determined through `sys.foreign_keys` + `sys.foreign_key_columns`, guaranteeing that `hasDbConstraint` only lights up when the intended column participates in a real FK pointing at the resolved table. The exporter also emits a rich `relationships[].actualConstraints` payload with per-column owner/ref mappings and delete/update actions. 【F:src/AdvancedSql/outsystems_model_export.sql†L292-L457】【F:readme.md†L241-L269】

## JSON Projection Highlights
- The attribute payload emits the logical shape (name, datatype, lengths), state flags, reference wiring, external hints, and the new `onDisk`/`meta` surfaces derived from catalog evidence and OutSystems designer metadata. 【F:src/AdvancedSql/outsystems_model_export.sql†L349-L386】【F:readme.md†L209-L235】
- Relationships reuse the FK detection results and include an `actualConstraints` array so reviewers can distinguish between designed references and those backed by real database constraints. 【F:src/AdvancedSql/outsystems_model_export.sql†L386-L410】【F:readme.md†L241-L269】
- Index projection keeps logical attribute ordering alongside the physical column names so downstream diff tooling can reason about both facets simultaneously, now with direction and included-column flags. 【F:src/AdvancedSql/outsystems_model_export.sql†L300-L341】【F:readme.md†L271-L287】

## Extended Descriptions → `MS_Description`
- README now documents how to apply the surfaced entity/attribute descriptions to SQL Server extended properties using `sp_addextendedproperty` / `sp_updateextendedproperty`, enabling SSDT parity without re-authoring metadata. 【F:readme.md†L289-L315】

These notes should make it easier to extend the extractor, audit its assumptions, or onboard new collaborators who need to understand where each JSON field originates.
