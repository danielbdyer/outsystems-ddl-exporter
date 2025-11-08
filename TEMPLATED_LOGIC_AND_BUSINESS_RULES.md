# OutSystems DDL Exporter: Complete Templated Logic & Business Rules Documentation

> **Document Purpose**
>
> This document provides a comprehensive, externally reviewable inventory of **all templated/business logic choices** implemented throughout the OutSystems DDL Exporter. It documents every transformation, naming convention, policy decision, and emission rule applied when converting OutSystems logical models into SQL Server DDL artifacts.
>
> **Target Audience**: External stakeholders, reviewers, auditors, and technical teams who need to understand and verify the complete alignment between OutSystems metadata and the emitted SQL Server schema.
>
> **Last Updated**: 2025-11-03

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Directory and Folder Structure](#2-directory-and-folder-structure)
3. [File Naming Conventions](#3-file-naming-conventions)
4. [Database Object Naming Patterns](#4-database-object-naming-patterns)
5. [NULL/NOT NULL Computation Logic](#5-nullnot-null-computation-logic)
6. [Foreign Key Creation Logic](#6-foreign-key-creation-logic)
7. [Unique Index and Constraint Logic](#7-unique-index-and-constraint-logic)
8. [Data Type Mapping Rules](#8-data-type-mapping-rules)
9. [Seed File Emission Semantics](#9-seed-file-emission-semantics)
10. [Index Handling and Metadata Preservation](#10-index-handling-and-metadata-preservation)
11. [Extended Properties and Descriptions](#11-extended-properties-and-descriptions)
12. [Module and Entity Filtering Rules](#12-module-and-entity-filtering-rules)
13. [Naming Override System](#13-naming-override-system)
14. [Remediation and Sentinel Values](#14-remediation-and-sentinel-values)
15. [Output File Content Structure](#15-output-file-content-structure)
16. [Determinism and Reproducibility Guarantees](#16-determinism-and-reproducibility-guarantees)
17. [Configuration Hierarchy and Precedence](#17-configuration-hierarchy-and-precedence)
18. [Mode-Specific Behavior Matrix](#18-mode-specific-behavior-matrix)
19. [Transformation Pipeline Summary](#19-transformation-pipeline-summary)
20. [Edge Cases and Special Handling](#20-edge-cases-and-special-handling)

---

## 1. Executive Summary

The OutSystems DDL Exporter implements a **multi-stage, evidence-driven pipeline** that transforms OutSystems logical data models into production-ready SQL Server DDL scripts. The system applies consistent, configurable business rules across:

### Core Transformation Areas

- **Extraction**: Advanced SQL query extracts complete metamodel (modules → entities → attributes/indexes/relationships) in a single JSON payload
- **Profiling**: SQL Server catalog and data reality scanning captures NULL counts, duplicates, orphans, and physical metadata
- **Tightening**: Policy-driven decisions for NOT NULL, UNIQUE, and FK constraints based on evidence and configuration
- **Emission**: SMO-based DDL generation with deterministic, per-table output organized by module
- **Validation**: Optional DMM parity checking ensures 1:1 alignment with OutSystems platform DDL

### Key Design Principles

1. **Evidence-Gated Decisions**: All schema tightening (NOT NULL, UNIQUE, FK) requires profiling evidence unless explicitly overridden
2. **Deterministic Output**: Same inputs always produce identical DDL files with consistent naming and ordering
3. **No String Concatenation**: All DDL generated via SMO; no regex or manual SQL construction
4. **Logical Name Preference**: Emitted DDL uses human-readable logical names; physical names preserved in comments/metadata
5. **Module-Centric Organization**: Output structured by OutSystems module for traceability and SSDT compatibility
6. **Full Traceability**: Every decision captured in `policy-decisions.json` with rationales and evidence links

---

## 2. Directory and Folder Structure

### 2.1 Output Directory Structure

```
<output-dir>/
├── Modules/                          # Per-module DDL organization
│   ├── <ModuleName>/
│   │   ├── <schema>.<table>.sql     # One file per entity/table
│   │   └── ...
├── Seeds/                            # Static entity reference data
│   ├── <ModuleName>/
│   │   └── StaticEntities.seed.sql  # Idempotent MERGE scripts
├── suggestions/                      # Tightening opportunity scripts
│   ├── safe-to-apply.sql            # No data remediation required
│   └── needs-remediation.sql        # Requires data cleanup first
├── manifest.json                     # Complete emission metadata
├── policy-decisions.json             # All tightening decisions + rationales
├── opportunities.json                # Contradictions and recommendations
├── validations.json                  # Evidence for already-enforced constraints
└── README.txt                        # Import guidance for SSDT

```

### 2.2 Module Folder Naming Rules

- **Default**: Module name as-is from OutSystems metadata
- **Sanitization** (when `emission.sanitizeModuleNames = true`):
  - Replace spaces with underscores
  - Remove special characters that could cause file system issues
  - Prevent path traversal attacks via module names
  - Example: `"My App 1.0"` → `"My_App_1_0"`

### 2.3 Repository Source Structure

```
src/
├── Osm.Domain/              # Domain models, value objects, configuration
│   ├── Model/               # EntityModel, AttributeModel, RelationshipModel
│   ├── Configuration/       # TighteningOptions, NamingOverrideOptions
│   ├── ValueObjects/        # TableName, SchemaName, ColumnName, etc.
│   └── Profiling/           # ColumnProfile, ForeignKeyReality, ProfileSnapshot
├── Osm.Json/                # JSON serialization/deserialization
├── Osm.Validation/          # Tightening policy evaluation
│   └── Tightening/          # NullabilityEvaluator, ForeignKeyEvaluator, etc.
├── Osm.Smo/                 # SMO object graph building and DDL emission
│   └── PerTableEmission/    # Per-table DDL builders
├── Osm.Emission/            # High-level emission orchestration
│   └── Seeds/               # Static entity seed generation
├── Osm.Dmm/                 # DMM comparison using ScriptDom
├── Osm.Pipeline/            # CLI orchestration and workflows
│   └── Orchestration/       # BuildSsdtPipeline, TighteningAnalysisPipeline
└── Osm.Cli/                 # CLI entry points and command factories
    └── Commands/            # Command implementations

config/
├── default-tightening.json      # Default policy configuration
├── type-mapping.default.json    # Data type mapping rules
└── supplemental/                # Supplemental entity definitions
    └── ossys-user.json          # System tables (Users, etc.)

tests/Fixtures/
├── emission/                    # Expected DDL output baselines
├── profiling/                   # Profile snapshot fixtures
└── model.*.json                 # Test model files
```

---

## 3. File Naming Conventions

### 3.1 Per-Table DDL Files

**Pattern**: `<schema>.<table>.sql`

**Naming Resolution Order**:
1. Check `namingOverrides.rules` for explicit override
2. Use logical entity name if available (trimmed, whitespace normalized)
3. Fall back to physical table name

**Examples**:
- Physical: `OSUSR_ABC_CUSTOMER` → Emitted: `dbo.Customer.sql`
- With override: `OSUSR_ABC_CUSTOMER` → `dbo.CUSTOMER_PORTAL.sql` (if configured)
- Cross-schema: `OSUSR_DEF_CITY` in `billing` schema → `billing.City.sql`

**Key Rules**:
- Schema always included in filename (no schema = `dbo` assumed)
- Case preserved from logical names or overrides
- Special characters in table names escaped if necessary for file system
- Logical names preferred over physical names for readability

### 3.2 Seed Data Files

**Pattern**: `Seeds/<ModuleName>/StaticEntities.seed.sql`

**Grouping Rules**:
- `emission.staticSeeds.groupByModule = true`: One file per module (default)
- `emission.staticSeeds.emitMasterFile = true`: Additional combined `StaticEntities.master.sql`

**File Content Organization**:
- Ordered by module name (deterministic sort)
- Within module: ordered by schema, then logical table name
- Header comments identify source entity and target table
- Each entity gets separate MERGE block

### 3.3 Manifest and Decision Files

| File | Purpose | Format |
|------|---------|--------|
| `manifest.json` | Complete emission summary (tables, indexes, FKs, options) | JSON |
| `policy-decisions.json` | All nullability/unique/FK decisions with rationales | JSON |
| `opportunities.json` | Tightening contradictions/recommendations | JSON |
| `validations.json` | Profiling-confirmed constraints | JSON |
| `suggestions/safe-to-apply.sql` | Ready-to-apply constraint additions | SQL |
| `suggestions/needs-remediation.sql` | Requires data cleanup before applying | SQL |

---

## 4. Database Object Naming Patterns

### 4.1 Primary Key (PK) Constraints

**Pattern**: `PK_<TableName>_<ColumnName>`

**Examples**:
- `PK_Customer_Id`
- `PK_BillingAccount_Id`
- `PK_JobRun_Id`

**Rules**:
- Always uses logical table name (after naming overrides applied)
- Always uses logical column name
- PK columns always on identity columns (identifier attributes)
- Token normalization applied: `PK_CUSTOMER_ID` → `PK_Customer_Id`

**Implementation**: Inline in CREATE TABLE statement
```sql
[Id] BIGINT IDENTITY (1, 1) NOT NULL
    CONSTRAINT [PK_Customer_Id]
        PRIMARY KEY CLUSTERED
```

### 4.2 Foreign Key (FK) Constraints

**Pattern**: `FK_<OwningTable>_<ReferencedTable>_<OwningColumns>`

**Examples**:
- `FK_Customer_City_CityId`
- `FK_BillingAccount_Customer_CustomerId`
- `FK_OrderLine_Order_Product_OrderId_ProductId` (composite)

**Creation Logic** (ForeignKeyNameFactory):

1. **Evidence-Based Name** (if FK constraint exists in database):
   ```
   Base: OutSystems-provided constraint name
   If missing columns: Append column segment
   Example: "FK_Customer_City" + "_CityId" → "FK_Customer_City_CityId"
   ```

2. **Fallback Name** (if no DB constraint):
   ```
   Pattern: FK_<OwnerPhysical>_<ReferencedPhysical>_<ColumnPhysical>
   Then normalized: physical → logical names
   Example: FK_OSUSR_ABC_CUSTOMER_OSUSR_DEF_CITY_CITYID
        → FK_Customer_City_CityId
   ```

**Normalization Process** (ConstraintNameNormalizer):
1. Replace physical table names with logical names (case-insensitive)
2. Handle OutSystems prefix extraction (`OSUSR_wzu_ProjectStatus` → `ProjectStatus`)
3. Replace physical column names with logical names
4. Tokenize by underscore
5. Apply token normalization:
   - ALL_UPPER → Title_Case
   - all_lower → Title_Case
   - CamelCase → Preserve
6. Rejoin with underscores

**Implementation**: Inline in CREATE TABLE statement
```sql
[CityId] BIGINT NOT NULL
    CONSTRAINT [FK_Customer_City_CityId]
        FOREIGN KEY ([CityId]) REFERENCES [dbo].[City] ([Id])
```

### 4.3 Unique Index (UIX) Constraints

**Single-Column Pattern**: `UIX_<TableName>_<ColumnName>`

**Examples**:
- `UIX_Customer_Email`
- `UIX_BillingAccount_AccountNumber`

**Composite Pattern**: `UIX_<TableName>_<Col1>_<Col2>_..._<ColN>`

**Examples**:
- `UIX_OrderLine_OrderId_ProductId`
- `UIX_Customer_Email_Status`

**Rules**:
- Logical table and column names used
- Columns ordered by their position in the unique constraint
- Token normalization applied
- Filter predicates added for nullable columns: `WHERE ([Email] IS NOT NULL)`

**Implementation**: Separate CREATE INDEX statement
```sql
CREATE UNIQUE INDEX [UIX_Customer_Email]
    ON [dbo].[Customer]([Email])
    WHERE ([Email] IS NOT NULL)
    WITH (FILLFACTOR = 85, IGNORE_DUP_KEY = ON)
    ON [FG_Customers]
```

### 4.4 Non-Unique Index (IX)

**Pattern**: `IX_<TableName>_<Col1>_<Col2>_..._<ColN>`

**Examples**:
- `IX_Customer_LastName`
- `IX_Customer_LastName_FirstName`
- `IX_Order_CustomerId_OrderDate`

**Rules**:
- Logical names for table and columns
- Columns ordered by index column ordinal
- Token normalization applied
- Physical metadata preserved (FILLFACTOR, STATISTICS_NORECOMPUTE, etc.)

**Implementation**: Separate CREATE INDEX statement
```sql
CREATE INDEX [IX_Customer_LastName_FirstName]
    ON [dbo].[Customer]([LastName], [FirstName])
    WITH (STATISTICS_NORECOMPUTE = ON)
    ON [FG_Customers]
```

### 4.5 Default Constraints (DF)

**Pattern**: System-generated by SMO (not explicitly named in base config)

**Example**: `DF__Customer__FirstName__12345678`

**Rules**:
- SMO generates default system names
- Applied inline in CREATE TABLE for column defaults
- Default expression preserved from OutSystems metadata or on-disk definition

**Implementation**: Inline default definition
```sql
[FirstName] NVARCHAR (100) NULL
    DEFAULT ('')
```

### 4.6 Constraint Name Token Normalization

**Algorithm** (ConstraintNameNormalizer.NormalizeToken):

```
Input: Token string
1. Check if ALL_UPPER: Yes → Title_Case
2. Check if all_lower: Yes → Title_Case
3. Check if MixedCase: Yes → Preserve as-is
4. Apply: First character uppercase + rest lowercase

Examples:
  "CUSTOMER" → "Customer"
  "customer" → "Customer"
  "CityId"   → "CityId" (preserved)
  "ID"       → "Id"
  "A"        → "A"
```

---

## 5. NULL/NOT NULL Computation Logic

### 5.1 Tightening Modes

The system supports three modes for NOT NULL decision-making:

#### **Cautious Mode**
- **Philosophy**: Trust only physical evidence; ignore metadata
- **Logic**: NOT NULL only if physical column is NOT NULL OR primary key
- **Signals Used**: S1 (PK), S2 (Physical NOT NULL)
- **Remediation**: Never generates remediation scripts
- **Use Case**: Conservative migrations where metadata may be unreliable

#### **EvidenceGated Mode** (DEFAULT)
- **Philosophy**: Combine metadata + evidence; require both for NOT NULL
- **Logic**: NOT NULL if (PK OR Physical NOT NULL) OR ((Mandatory OR FK OR Unique) AND Data has no NULLs AND within null budget)
- **Signals Used**: S1, S2, S3 (FK), S4 (Unique), S5 (Mandatory), S7 (Default), plus data evidence
- **Remediation**: Only if data contradicts decision (rare)
- **Use Case**: Balanced approach for most production scenarios

#### **Aggressive Mode**
- **Philosophy**: Trust metadata; remediate data to match
- **Logic**: NOT NULL if PK OR Physical NOT NULL OR FK OR Unique OR Mandatory
- **Signals Used**: All signals (S1-S7)
- **Remediation**: Generates scripts when data has NULLs but metadata says mandatory
- **Use Case**: Greenfield projects or when data quality enforcement is priority

### 5.2 Nullability Signals

**Signal Hierarchy** (NullabilityEvaluator):

| Signal | Code | Description | Priority | Modes Using |
|--------|------|-------------|----------|-------------|
| S1 | PRIMARY_KEY | Column is primary key (IsIdentifier) | Strong | All |
| S2 | PHYSICAL_NOT_NULL | sys.columns.is_nullable = 0 | Strong | All |
| S3 | FK_CONSTRAINT | FK with DB constraint exists | Strong | EvidenceGated, Aggressive |
| S4 | UNIQUE_NO_NULLS | Single/composite unique + no nulls | Strong | EvidenceGated, Aggressive |
| S5 | MANDATORY | attribute.IsMandatory = true | Weak | EvidenceGated (with data), Aggressive |
| S7 | DEFAULT_PRESENT | Column has default value | Weak | EvidenceGated (with S5) |

**Data Evidence Checks**:
- **DATA_NO_NULLS**: Profile shows null_count = 0
- **NULL_BUDGET_OK**: null_count / total_rows ≤ configured null budget
- **PROFILE_MISSING**: No profiling evidence available (blocks tightening in EvidenceGated)

### 5.3 Null Budget Concept

**Configuration**: `policy.nullBudget` (0.0 to 1.0)
- **Default**: 0.0 (zero tolerance for nulls)
- **Interpretation**: Maximum acceptable percentage of NULL rows

**Logic**:
```
null_rate = null_count / total_rows
if null_rate > nullBudget:
    rationale: NULL_BUDGET_EXCEEDED
    decision: NULLABLE
else:
    rationale: NULL_BUDGET_OK
    proceed with tightening evaluation
```

**Examples**:
- `nullBudget = 0.0`: Column must have zero nulls to be NOT NULL
- `nullBudget = 0.05`: Column can have up to 5% nulls and still be NOT NULL
- `nullBudget = 0.10`: Column can have up to 10% nulls and still be NOT NULL

### 5.4 Decision Rationales

Every nullability decision includes one or more rationales explaining why the column is or is not NOT NULL:

| Rationale Code | Meaning | Typical Decision |
|----------------|---------|------------------|
| PRIMARY_KEY | Column is entity identifier | NOT NULL |
| PHYSICAL_NOT_NULL | Database metadata shows NOT NULL | NOT NULL |
| DATA_NO_NULLS | Profiling found zero NULL rows | NOT NULL (with other signals) |
| MANDATORY | Logical attribute marked mandatory | NOT NULL (Aggressive) / Evidence-gated |
| UNIQUE_NO_NULLS | Unique constraint + no nulls found | NOT NULL |
| DEFAULT_PRESENT | Default value defined | Supporting evidence |
| FK_CONSTRAINT | Foreign key with DB constraint | NOT NULL (typically) |
| PROFILE_MISSING | No profiling data available | NULLABLE (safe default) |
| NULL_BUDGET_EXCEEDED | Too many nulls found | NULLABLE |
| DATA_HAS_NULLS | Contradicts metadata | NULLABLE or Remediate |
| REMEDIATE_BEFORE_TIGHTEN | Aggressive mode + data has nulls | NOT NULL + Remediation required |

### 5.5 Nullability Decision Output

**Structure** (NullabilityDecision):
```csharp
{
    "Schema": "dbo",
    "Table": "Customer",
    "Column": "Email",
    "MakeNotNull": true,
    "RequiresRemediation": false,
    "Rationales": ["MANDATORY", "UNIQUE_NO_NULLS", "DATA_NO_NULLS"]
}
```

**Emitted as**:
```sql
[Email] NVARCHAR (255) NOT NULL
```

---

## 6. Foreign Key Creation Logic

### 6.1 FK Creation Decision Process

**Evaluator**: ForeignKeyEvaluator

**Decision Criteria** (all must pass):

1. **Attribute Must Be Reference**: `attribute.Reference.IsReference = true`
2. **FK Creation Enabled**: `foreignKeys.enableCreation = true`
3. **Schema Check** (if `allowCrossSchema = false`):
   - Owner schema = Referenced schema
4. **Catalog Check** (if `allowCrossCatalog = false`):
   - Owner catalog = Referenced catalog
5. **Delete Rule Check**:
   - If `deleteRule = "Ignore"` → No FK created (application-managed)
   - If `treatMissingDeleteRuleAsIgnore = true` AND deleteRule missing → No FK
6. **Evidence Check**:
   - Check for orphan records (FK references non-existent PK)
   - If orphans found → No FK OR mark for remediation

### 6.2 Delete Rule Mapping

OutSystems delete rules map to SQL Server FK actions:

| OutSystems Delete Rule | SQL Server Action | FK Created? |
|------------------------|-------------------|-------------|
| `Protect` | `ON DELETE NO ACTION` | Yes |
| `Delete` | `ON DELETE CASCADE` | Yes |
| `Ignore` | *(No constraint)* | No |
| Missing/NULL (default) | Depends on `treatMissingDeleteRuleAsIgnore` | Configurable |

**Configuration**:
```json
"foreignKeys": {
    "treatMissingDeleteRuleAsIgnore": false
}
```
- `false`: Missing delete rule causes validation error or defaults to Protect
- `true`: Missing delete rule treated as Ignore (no FK constraint)

### 6.3 FK Decision Rationales

| Rationale Code | Meaning | FK Created? |
|----------------|---------|-------------|
| DB_CONSTRAINT_PRESENT | FK already exists in database | Yes |
| DATA_NO_ORPHANS | No orphan records found | Yes (if other checks pass) |
| DELETE_RULE_PROTECT | Delete rule = Protect/Restrict | Yes |
| DELETE_RULE_CASCADE | Delete rule = Cascade | Yes |
| DELETE_RULE_IGNORE | Delete rule = Ignore | No |
| DATA_HAS_ORPHANS | Orphan records detected | No (or remediation) |
| CROSS_SCHEMA_BLOCKED | Owner/referenced in different schemas | No |
| CROSS_CATALOG_BLOCKED | Owner/referenced in different catalogs | No |
| FK_DISABLED | Foreign key creation disabled in config | No |

### 6.4 Orphan Detection

**Definition**: Orphan record exists when FK column value does not match any PK value in referenced table

**Detection Query** (conceptual):
```sql
SELECT COUNT(*)
FROM [owner].[table] o
WHERE o.[FKColumn] IS NOT NULL
  AND NOT EXISTS (
    SELECT 1
    FROM [referenced].[table] r
    WHERE r.[Id] = o.[FKColumn]
  )
```

**Handling**:
- If orphans > 0 → Rationale: `DATA_HAS_ORPHANS`
- EvidenceGated: No FK created
- Aggressive: Mark for remediation (generate DELETE script for orphans)

### 6.5 FK Emission Format

**Inline Foreign Key** (default):
```sql
[CityId] BIGINT NOT NULL
    CONSTRAINT [FK_Customer_City_CityId]
        FOREIGN KEY ([CityId]) REFERENCES [dbo].[City] ([Id])
```

**With ON DELETE Clause**:
```sql
CONSTRAINT [FK_Order_Customer_CustomerId]
    FOREIGN KEY ([CustomerId])
    REFERENCES [dbo].[Customer] ([Id])
    ON DELETE CASCADE
```

**Composite Foreign Key**:
```sql
CONSTRAINT [FK_OrderLine_Order_Product_OrderId_ProductId]
    FOREIGN KEY ([OrderId], [ProductId])
    REFERENCES [dbo].[Order_Product] ([OrderId], [ProductId])
```

---

## 7. Unique Index and Constraint Logic

### 7.1 Unique Constraint Decision Process

**Configuration**:
```json
"uniqueness": {
    "enforceSingleColumnUnique": true,
    "enforceMultiColumnUnique": true
}
```

**Evaluation** (UniqueEvaluator):

#### Single-Column Unique
1. Check if column is candidate unique key (OutSystems metadata or index definition)
2. Profile data for duplicates
3. If no duplicates found → Create UNIQUE index
4. If nullable column → Add filter predicate `WHERE [col] IS NOT NULL`

#### Composite Unique
1. Check if combination of columns is defined as unique in OutSystems
2. Profile data for duplicate combinations
3. If no duplicates found → Create composite UNIQUE index
4. If any column nullable → Add filter predicate for non-null combinations

### 7.2 Uniqueness Evidence

**Profiling Checks**:
- **Single-column**: `SELECT COUNT(*) FROM (SELECT col, COUNT(*) as cnt FROM table GROUP BY col HAVING COUNT(*) > 1)`
- **Composite**: Similar query but grouping by all columns in unique combination

**Decision Matrix**:

| Evidence | enforceSingleColumnUnique | enforcMultiColumnUnique | Result |
|----------|---------------------------|-------------------------|--------|
| No duplicates found | true | N/A | Create UIX (single) |
| No duplicates found | N/A | true | Create UIX (composite) |
| Duplicates found | true | N/A | No UIX (mark as opportunity) |
| No profile data | true | N/A | No UIX (safe default) |

### 7.3 Unique Index Metadata

**Standard Options**:
```sql
CREATE UNIQUE INDEX [UIX_Customer_Email]
    ON [dbo].[Customer]([Email])
    WHERE ([Email] IS NOT NULL)
    WITH (FILLFACTOR = 85, IGNORE_DUP_KEY = ON)
    ON [PRIMARY]
```

**Preserved From On-Disk**:
- `FILLFACTOR`: Preserved from existing index (default 85 if not found)
- `IGNORE_DUP_KEY`: Always ON for unique indexes (duplicate insert ignored vs error)
- `PAD_INDEX`: Preserved from on-disk metadata
- `STATISTICS_NORECOMPUTE`: Preserved from on-disk metadata
- Filter definition: Preserved from on-disk or generated for nullable columns

**Filegroup Placement**: Preserved from on-disk or defaults to PRIMARY

### 7.4 Filter Predicates for Nullable Unique Columns

**Generated Filter**:
```sql
WHERE ([ColumnName] IS NOT NULL)
```

**Rationale**:
- SQL Server allows multiple NULLs in unique indexes
- Filter predicate excludes NULLs from uniqueness check
- Allows index to enforce uniqueness only on non-NULL values
- Aligns with OutSystems semantic: "unique among non-null values"

**Example**:
```sql
-- Email can be NULL but must be unique when present
CREATE UNIQUE INDEX [UIX_Customer_Email]
    ON [dbo].[Customer]([Email])
    WHERE ([Email] IS NOT NULL)
```

---

## 8. Data Type Mapping Rules

### 8.1 Type Mapping Resolution Priority

**Algorithm** (TypeMappingPolicy.Resolve):

1. **On-Disk First** (if available and `onDisk` metadata present):
   - Use actual SQL type from `sys.columns`
   - Preserve length, precision, scale from physical column
   - Exception: `date` type forced to `date` (not fallback to datetime)

2. **External Second** (if external entity with stored proc definition):
   - Parse external type + parameters
   - Apply external mapping rules

3. **Logical Mapping** (from OutSystems attribute data type):
   - Look up in `mappings` section of type-mapping.default.json
   - Apply strategy with default parameters

4. **Default Fallback** (if no match):
   - Strategy: `UnicodeText` → `nvarchar(max)`

### 8.2 OutSystems Logical Type Mappings

**Complete Mapping Table** (from type-mapping.default.json):

| OutSystems Type | SQL Server Type | Strategy | Parameters |
|-----------------|-----------------|----------|------------|
| identifier | bigint | Direct | - |
| autonumber | bigint | Direct | - |
| integer | int | Direct | - |
| longinteger | bigint | Direct | - |
| boolean | bit | Direct | - |
| datetime | datetime | Direct | - |
| datetime2 | datetime2(7) | DateTime2 | scale: 7 |
| datetimeoffset | datetimeoffset(7) | DateTimeOffset | scale: 7 |
| date | date | Direct | - |
| time | time(7) | Time | scale: 7 |
| decimal | decimal(18, 0) | Decimal | precision: 18, scale: 0 |
| double | float | Direct | - |
| float | float | Direct | - |
| real | real | Direct | - |
| currency | decimal(37, 8) | Decimal | precision: 37, scale: 8 |
| binarydata | varbinary(max) | VarBinary | - |
| binary | varbinary(max) | VarBinary | - |
| varbinary | varbinary(max) | VarBinary | - |
| longbinarydata | varbinary(max) | VarBinary | - |
| image | image | Direct | - |
| longtext | nvarchar(max) | Direct | - |
| text | nvarchar(n) or nvarchar(max) | UnicodeText | maxLengthThreshold: 2000 |
| email | varchar(250) | VarCharText | fallbackLength: 250 |
| phonenumber | varchar(20) | VarCharText | fallbackLength: 20 |
| phone | varchar(20) | VarCharText | fallbackLength: 20 |
| url | nvarchar(n) or nvarchar(max) | UnicodeText | maxLengthThreshold: 2000 |
| password | nvarchar(n) or nvarchar(max) | UnicodeText | maxLengthThreshold: 2000 |
| username | nvarchar(n) or nvarchar(max) | UnicodeText | maxLengthThreshold: 2000 |
| identifiertext | nvarchar(n) or nvarchar(max) | UnicodeText | maxLengthThreshold: 2000 |
| guid | uniqueidentifier | Direct | - |
| uniqueidentifier | uniqueidentifier | Direct | - |
| xml | xml | Direct | - |

### 8.3 Type Mapping Strategies

#### UnicodeText Strategy
**Configuration**:
```json
{
  "strategy": "UnicodeText",
  "maxLengthThreshold": 2000
}
```

**Logic**:
```
if length IS NULL OR length > maxLengthThreshold:
    return nvarchar(max)
else:
    return nvarchar(length)
```

**Examples**:
- `text` with length 100 → `nvarchar(100)`
- `text` with length 3000 → `nvarchar(max)`
- `text` with no length → `nvarchar(max)`

#### Decimal Strategy
**Configuration**:
```json
{
  "strategy": "Decimal",
  "defaultPrecision": 18,
  "defaultScale": 0
}
```

**Logic**:
```
precision = attribute.precision ?? defaultPrecision
scale = attribute.scale ?? defaultScale
return decimal(precision, scale)
```

**Special Cases**:
- `currency`: Always `decimal(37, 8)` (high precision for financial calculations)
- `decimal` with no metadata: `decimal(18, 0)`

#### DateTime2 Strategy
**Configuration**:
```json
{
  "strategy": "DateTime2",
  "scale": 7
}
```

**Logic**:
```
scale = attribute.scale ?? 7
return datetime2(scale)
```

**Scales**: 0-7 (fractional seconds precision)
- 0 = seconds precision
- 3 = milliseconds precision
- 7 = 100-nanosecond precision (SQL Server maximum)

#### VarBinary Strategy
**Logic**:
```
if length IS NULL OR length > maxLengthThreshold:
    return varbinary(max)
else:
    return varbinary(length)
```

#### VarCharText Strategy (non-Unicode)
**Configuration**:
```json
{
  "strategy": "VarCharText",
  "fallbackLength": 250
}
```

**Logic**:
```
length = attribute.length ?? onDisk.length ?? fallbackLength
if length > 8000:
    return varchar(max)
else:
    return varchar(length)
```

**Use Cases**: email, phone (ASCII-only data types for performance)

### 8.4 On-Disk Type Preservation

**Priority**: On-disk metadata takes precedence over logical mappings when available

**Preserved Attributes**:
- SQL type name (e.g., `nvarchar`, `decimal`, `datetime2`)
- Length (for character/binary types)
- Precision (for numeric types)
- Scale (for decimal/time types)
- Collation (for character types)
- Identity properties (seed, increment)
- Computed column definition

**Example**:
```json
"onDisk": {
  "sqlType": "nvarchar",
  "maxLength": 255,
  "collation": "Latin1_General_CI_AI",
  "isIdentity": false,
  "isComputed": false
}
```

**Emitted as**:
```sql
[Email] NVARCHAR (255) COLLATE Latin1_General_CI_AI NOT NULL
```

### 8.5 Special Type Handling

#### Identity Columns
**OutSystems**: `IsAutoNumber = true` OR `onDisk.isIdentity = true`

**Emitted as**:
```sql
[Id] BIGINT IDENTITY (1, 1) NOT NULL
```

**Rules**:
- Seed: 1 (default)
- Increment: 1 (default)
- Always NOT NULL
- Always PRIMARY KEY

#### Computed Columns
**OutSystems**: `onDisk.isComputed = true`

**Emitted as**:
```sql
[FullName] AS ([FirstName] + ' ' + [LastName]) PERSISTED
```

**Rules**:
- Never included in INSERT/UPDATE seed scripts
- Definition preserved from on-disk metadata
- PERSISTED keyword included if stored computed column

#### Default Values
**Sources**:
1. OutSystems attribute `defaultValue`
2. On-disk `defaultDefinition` from sys.columns

**Emitted as**:
```sql
[FirstName] NVARCHAR (100) NULL
    DEFAULT ('')

[CreatedAt] DATETIME2 (7) NOT NULL
    DEFAULT (GETUTCDATE())
```

**Formatting**: Default expressions on separate indented line for readability

---

## 9. Seed File Emission Semantics

### 9.1 Static Entity Selection

**Criteria for Inclusion**:
1. `entity.IsStatic = true` (marked as static entity in OutSystems)
2. `entity.IsActive = true` (not soft-deleted)
3. Entity has at least one active attribute
4. Module is included in emission scope

**Exclusions**:
- Computed columns (never in seed data)
- Inactive attributes (if `onlyActiveAttributes = true`)
- System platform columns (unless explicitly modeled)

### 9.2 Seed Synchronization Modes

**Configuration**: `emission.staticSeeds.mode`

#### NonDestructive Mode (DEFAULT)
**Behavior**:
```sql
MERGE INTO [dbo].[City] AS Target
USING (VALUES ...) AS Source (...)
    ON Target.[Id] = Source.[Id]
WHEN MATCHED THEN UPDATE SET ...
WHEN NOT MATCHED THEN INSERT ...
```

**Characteristics**:
- Only INSERTs new rows and UPDATEs existing rows
- Never DELETEs rows not in seed data
- Idempotent: can run multiple times safely
- Safe for environments with user-added static data

#### Authoritative Mode
**Behavior**:
```sql
DELETE FROM [dbo].[City];

INSERT INTO [dbo].[City] ([Id], [Name], [IsActive])
VALUES
    (1, N'Lisbon', 1),
    (2, N'Porto', 1);
```

**Characteristics**:
- DELETEs all existing rows first
- Then INSERTs seed data
- Enforces seed as single source of truth
- **Destructive**: removes any non-seed data

#### ValidateThenApply Mode
**Behavior**:
```sql
-- Validate constraints first
IF EXISTS (SELECT 1 FROM [dbo].[City] WHERE [Name] IS NULL)
    RAISERROR('Constraint violation detected', 16, 1);

-- Then MERGE
MERGE INTO [dbo].[City] ...
```

**Characteristics**:
- Validates data against constraints before applying
- Detects and reports violations
- Fails fast if data quality issues detected
- Uses MERGE semantics (like NonDestructive)

### 9.3 Seed Data Ordering

**Deterministic Sort Order**:
1. **Module Name** (alphabetical, case-insensitive)
2. **Schema Name** (alphabetical, case-insensitive)
3. **Logical Table Name** (alphabetical, case-insensitive)
4. **Physical Table Name** (tie-breaker if logical names identical)

**Within Table** (row ordering):
- Ordered by PRIMARY KEY columns
- Deterministic: same input → same output order
- Supports composite primary keys (ordered by column ordinal)

**Column Ordering**:
1. Primary key columns first (in ordinal order)
2. Regular columns (in attribute definition order from OutSystems)
3. Computed columns excluded

**Example**:
```sql
-- Module: AppCore (sorted first)
-- Entity: City (sorted by name)
MERGE INTO [dbo].[City] AS Target
USING
(
    VALUES
        (1, N'Lisbon', 1),    -- Ordered by Id (PK)
        (2, N'Porto', 1),
        (3, N'Madrid', 0)
) AS Source ([Id], [Name], [IsActive])  -- Columns: PK first, then natural order
```

### 9.4 Seed File Structure

**Header**:
```sql
/* ==========================================================================
   Static Entity Seed Script
   Generated by OutSystems DDL Exporter
   This script merges reference data captured from static entities so that
   SSDT deployments remain idempotent across environments.
   --------------------------------------------------------------------------
   The blocks below are populated at generation time. Each block issues a
   MERGE statement scoped to a specific static entity.
   ==========================================================================
*/

SET NOCOUNT ON;
```

**Per-Entity Block**:
```sql
--------------------------------------------------------------------------------
-- Module: <ModuleName>
-- Entity: <LogicalEntityName> (<PhysicalTableName>)
-- Target: <schema>.<emitted_table_name>
--------------------------------------------------------------------------------
SET IDENTITY_INSERT [schema].[table] ON;
GO

MERGE INTO [schema].[table] AS Target
USING
(
    VALUES
        (val1, val2, val3),
        (val4, val5, val6)
) AS Source ([Col1], [Col2], [Col3])
    ON Target.[PKCol] = Source.[PKCol]
WHEN MATCHED THEN UPDATE SET
    Target.[Col2] = Source.[Col2],
    Target.[Col3] = Source.[Col3]
WHEN NOT MATCHED THEN INSERT ([Col1], [Col2], [Col3])
    VALUES (Source.[Col1], Source.[Col2], Source.[Col3]);

GO

SET IDENTITY_INSERT [schema].[table] OFF;
GO
```

### 9.5 Seed Data Value Formatting

**String Values**:
```sql
N'String value'  -- Unicode strings (nvarchar)
'ASCII string'   -- Non-unicode (varchar)
```

**Escaping**:
- Single quotes doubled: `O'Brien` → `'O''Brien'`
- Unicode prefix for nvarchar columns

**NULL Values**:
```sql
NULL  -- Explicit NULL keyword
```

**Numeric Values**:
```sql
123         -- Integer
123.45      -- Decimal
37.12345678 -- Currency (high precision)
```

**Date/Time Values**:
```sql
'2024-01-15'                    -- Date
'2024-01-15 10:30:00'           -- DateTime
'2024-01-15T10:30:00.1234567'  -- DateTime2
```

**Boolean Values**:
```sql
1  -- True
0  -- False
```

**GUID Values**:
```sql
'F47AC10B-58CC-4372-A567-0E02B2C3D479'
```

### 9.6 Identity Column Handling

**Pattern**:
```sql
SET IDENTITY_INSERT [schema].[table] ON;
GO

-- INSERT/MERGE statements with explicit Id values

GO

SET IDENTITY_INSERT [schema].[table] OFF;
GO
```

**Rules**:
- Always wrap MERGE statements with IDENTITY_INSERT ON/OFF
- Required to insert explicit values into identity columns
- Ensures static entity IDs preserved across environments
- Only one table at a time can have IDENTITY_INSERT ON (enforced by separate GO batches)

---

## 10. Index Handling and Metadata Preservation

### 10.1 Index Emission Order

**Within Per-Table File**:
1. **CREATE TABLE** (with inline PK constraint)
2. **GO** batch separator
3. **CREATE INDEX** statements (alphabetically sorted by index name)
4. **GO** batch separator (between each index)
5. **ALTER INDEX DISABLE** statements (if index disabled on-disk)
6. **GO** batch separator
7. **Extended properties** (MS_Description)

**Sorting**: Case-insensitive alphabetical by index logical name (deterministic)

**Example Emission Order**:
```sql
CREATE TABLE [dbo].[Customer] (...)
GO

CREATE INDEX [IX_Customer_City]
    ON [dbo].[Customer]([CityId])
GO

CREATE UNIQUE INDEX [UIX_Customer_Email]
    ON [dbo].[Customer]([Email])
GO

CREATE INDEX [IX_Customer_LastName_FirstName]
    ON [dbo].[Customer]([LastName], [FirstName])
GO

ALTER INDEX [IX_Customer_LastName_FirstName]
    ON [dbo].[Customer] DISABLE
GO
```

### 10.2 Index Metadata Preservation

**Preserved Properties** (from sys.indexes and sys.index_columns):

| Property | Preserved | Source | Example |
|----------|-----------|--------|---------|
| Index Name | Yes | Logical name or on-disk | IX_Customer_Email |
| Index Type | Yes | is_unique, type_desc | UNIQUE, NONCLUSTERED |
| Columns | Yes | index_columns + ordinal | [Email], [LastName], [FirstName] |
| Included Columns | Yes | is_included_column | INCLUDE ([Status]) |
| Sort Direction | Yes (if available) | is_descending_key | ASC, DESC |
| Filter Definition | Yes | filter_definition | WHERE [Email] IS NOT NULL |
| Filegroup | Yes | filegroup_name | ON [FG_Customers] |
| Fill Factor | Yes | fill_factor | FILLFACTOR = 85 |
| PAD_INDEX | Yes | is_padded | PAD_INDEX = ON |
| STATISTICS_NORECOMPUTE | Yes | no_recompute | STATISTICS_NORECOMPUTE = ON |
| IGNORE_DUP_KEY | Yes | ignore_dup_key | IGNORE_DUP_KEY = ON |
| ALLOW_ROW_LOCKS | Yes | allow_row_locks | ALLOW_ROW_LOCKS = ON |
| ALLOW_PAGE_LOCKS | Yes | allow_page_locks | ALLOW_PAGE_LOCKS = ON |
| Data Compression | Yes | data_compression_desc | DATA_COMPRESSION = ROW |
| Disabled Status | Yes | is_disabled | ALTER INDEX ... DISABLE |

**Full Example**:
```sql
CREATE UNIQUE INDEX [UIX_Customer_Email]
    ON [dbo].[Customer]([Email])
    INCLUDE ([FirstName], [LastName])
    WHERE ([Email] IS NOT NULL)
    WITH (
        FILLFACTOR = 85,
        PAD_INDEX = ON,
        IGNORE_DUP_KEY = ON,
        STATISTICS_NORECOMPUTE = ON,
        ALLOW_ROW_LOCKS = ON,
        ALLOW_PAGE_LOCKS = ON
    )
    ON [FG_Customers]
```

### 10.3 Platform Auto-Indexes

**Configuration**: `emission.includePlatformAutoIndexes`

**Default**: `false` (exclude system-generated indexes)

**Logic**:
- OutSystems platform automatically creates indexes for foreign keys and other patterns
- If `includePlatformAutoIndexes = false`: Skip indexes where `isPlatformAuto = true`
- If `includePlatformAutoIndexes = true`: Include all indexes

**Rationale**: Platform auto-indexes are regenerated automatically; explicit DDL may conflict

**Detection**: `index.IsPlatformAuto` flag from OutSystems metadata

### 10.4 Disabled Index Handling

**Detection**: `sys.indexes.is_disabled = 1`

**Emission**:
```sql
-- First: Create the index
CREATE INDEX [IX_Customer_LastName_FirstName]
    ON [dbo].[Customer]([LastName], [FirstName])
    WITH (STATISTICS_NORECOMPUTE = ON)
    ON [FG_Customers]

GO

-- Then: Disable it (preserving on-disk state)
ALTER INDEX [IX_Customer_LastName_FirstName]
    ON [dbo].[Customer] DISABLE
```

**Rationale**:
- Preserves exact on-disk state
- Allows index structure to be defined (for future enabling)
- Documents intentionally disabled indexes

### 10.5 Index Column Ordering

**Key Columns**: Ordered by `index_column_id` (ordinal position)

**Included Columns**: Ordered by appearance; listed after key columns

**Example**:
```sql
CREATE INDEX [IX_Customer_Complex]
    ON [dbo].[Customer](
        [LastName] ASC,      -- Key column 1
        [FirstName] ASC,     -- Key column 2
        [CityId] ASC         -- Key column 3
    )
    INCLUDE (                -- Included columns (not in key)
        [Email],
        [Status]
    )
```

---

## 11. Extended Properties and Descriptions

### 11.1 MS_Description Extended Properties

**Source**: OutSystems `meta` field on entities and attributes

**Levels**:
1. **Table-Level**: Entity description
2. **Column-Level**: Attribute description

**Emission**:
```sql
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description',
    @value=N'Stores customer records for AppCore',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE',  @level1name=N'Customer';
```

**Column-Level**:
```sql
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description',
    @value=N'Customer email',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE',  @level1name=N'Customer',
    @level2type=N'COLUMN', @level2name=N'Email';
```

### 11.2 Extended Property Emission Rules

**When Emitted**:
- Always included by default
- Can be suppressed with `emitBareTableOnly = true`

**Ordering**: After all CREATE INDEX statements

**Batch Separation**: Each sp_addextendedproperty call followed by GO

**Null/Empty Handling**:
- If description is NULL or empty string: No sp_addextendedproperty call emitted
- Prevents cluttering DDL with empty metadata

**String Escaping**:
- Single quotes doubled in description text
- Unicode strings preserved with N prefix

**Example with Special Characters**:
```sql
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description',
    @value=N'Customer''s primary email address (max 255 chars)',
    @level0type=N'SCHEMA', @level0name=N'dbo',
    @level1type=N'TABLE',  @level1name=N'Customer',
    @level2type=N'COLUMN', @level2name=N'Email';
```

### 11.3 Description Sources

**Entity Descriptions**:
1. OutSystems entity metadata `description` field
2. Trimmed and normalized
3. May include information about entity purpose, relationships

**Attribute Descriptions**:
1. OutSystems attribute metadata `description` field
2. May include business rules, validation notes
3. Preserved exactly as authored in OutSystems

**Fallback**: No description emitted if metadata empty (clean DDL)

### 11.4 Manifest Tracking

**Manifest JSON Entry**:
```json
{
  "Schema": "dbo",
  "Table": "Customer",
  "TableFile": "Modules/AppCore/dbo.Customer.sql",
  "IncludesExtendedProperties": true
}
```

**Purpose**:
- Tracks which tables have extended properties
- Enables validation that descriptions are preserved
- Supports diff tools and auditing

---

## 12. Module and Entity Filtering Rules

### 12.1 Module Filtering

**Configuration Sources** (in precedence order):
1. CLI flag: `--modules "AppCore,ExtBilling"`
2. Environment variable: `OSM_CLI_MODULES`
3. Configuration file: `model.modules` array
4. Default: All non-system modules

**Filter Syntax**:
```json
"modules": [
  "AppCore",                              // Include entire module
  "ExtBilling",
  {
    "name": "ServiceCenter",
    "entities": ["User", "Session"]       // Include specific entities only
  },
  {
    "name": "Ops",
    "entities": "*"                       // Include all entities (explicit)
  }
]
```

**Wildcard Support**:
- `"*"` = include all entities in module
- `true` = include all entities
- Array of strings = include only named entities (logical or physical names)

**Case Sensitivity**: Module names compared case-insensitively

### 12.2 System Module Handling

**Configuration**:
- CLI: `--include-system-modules` OR `--exclude-system-modules`
- Config: `model.includeSystemModules = true/false`

**Default**: `false` (exclude system modules)

**System Module Detection**: `module.isSystem = true` in OutSystems metadata

**Examples**:
- System Modules: `OutSystems.System`, `ServiceCenter`, `Users`
- Application Modules: `AppCore`, `ExtBilling`, `CustomModule`

### 12.3 Active vs Inactive Filtering

**Module-Level**:
```json
"includeInactiveModules": false
```
- `false` (default): Exclude modules where `module.isActive = false`
- `true`: Include inactive modules

**Entity-Level**:
```json
"onlyActiveAttributes": true
```
- `true` (default): Only process entities where `entity.isActive = true`
- `false`: Include inactive entities

**Attribute-Level**:
```
CLI: --only-active-attributes
Config: onlyActiveAttributes = true/false
```
- `true` (default): Only emit columns where `attribute.isActive = true`
- `false`: Include inactive attributes

**Combined Effect**:
| Module Active | Entity Active | Attribute Active | Emitted? |
|---------------|---------------|------------------|----------|
| Yes | Yes | Yes | ✓ Yes |
| Yes | Yes | No | Only if includeInactive |
| Yes | No | Yes/No | Only if includeInactive |
| No | Yes/No | Yes/No | Only if includeInactive |

### 12.4 Entity-Level Filtering

**Explicit Entity Selection**:
```json
{
  "name": "ServiceCenter",
  "entities": ["User"]
}
```
- Only `User` entity from `ServiceCenter` module will be processed
- Other entities in module ignored

**Matching Logic**:
1. Try exact match on logical entity name (case-insensitive)
2. Try exact match on physical table name (case-insensitive)
3. If no match: entity excluded

**Use Cases**:
- Extract only specific tables for focused migration
- Avoid entities with known validation issues
- Support phased migration approach

### 12.5 Filter Validation

**Validations Applied**:
- Module name must exist in extracted metadata
- Entity name must exist in specified module
- Warning if module filter results in zero entities
- Error if requested entity not found in module

**Error Messages**:
```
Warning: Module filter resulted in 0 entities for module 'NonExistent'
Error: Entity 'UnknownTable' not found in module 'AppCore'
```

---

## 13. Naming Override System

### 13.1 Naming Override Configuration

**Structure** (unified `rules` array):
```json
"namingOverrides": {
  "rules": [
    {
      "schema": "dbo",
      "table": "OSUSR_RTJ_CATEGORY",
      "module": "Inventory",
      "entity": "Category",
      "override": "CATEGORY_STATIC"
    },
    {
      "module": "SupportPortal",
      "entity": "Case",
      "override": "CASE_BACKOFFICE"
    },
    {
      "schema": "dbo",
      "table": "OSUSR_ABC_CUSTOMER",
      "override": "CUSTOMER_PORTAL"
    }
  ]
}
```

### 13.2 Override Rule Types

#### Physical-Only Override
**Coordinates**: `schema` + `table`

**Example**:
```json
{
  "schema": "dbo",
  "table": "OSUSR_ABC_CUSTOMER",
  "override": "CUSTOMER_PORTAL"
}
```

**Effect**:
- Renames table in DDL: `dbo.CUSTOMER_PORTAL.sql`
- All constraints use override: `PK_CUSTOMER_PORTAL_Id`, `FK_Order_CUSTOMER_PORTAL_CustomerId`
- Manifest uses override name
- Logical name unchanged (for OutSystems context)

#### Logical-Only Override
**Coordinates**: `module` + `entity` (optional: module can be omitted if entity name unique)

**Example**:
```json
{
  "module": "SupportPortal",
  "entity": "Case",
  "override": "CASE_BACKOFFICE"
}
```

**Effect**:
- Resolves duplicate logical names across modules
- All derived identifiers use override
- Foreign key targets use override name

#### Hybrid Override
**Coordinates**: `schema` + `table` + `module` + `entity`

**Example** (from above):
```json
{
  "schema": "dbo",
  "table": "OSUSR_RTJ_CATEGORY",
  "module": "Inventory",
  "entity": "Category",
  "override": "CATEGORY_STATIC"
}
```

**Effect**:
- Safest approach: covers physical and logical lookups
- SMO factory rewrites by physical coordinates first
- Then rechecks logical coordinates
- All downstream lookups return same override

### 13.3 CLI Override Syntax

**Flag**: `--rename-table`

**Simple Physical Rename**:
```bash
--rename-table dbo.OSUSR_ABC_CUSTOMER=CUSTOMER_PORTAL
```

**Combined Rename**:
```bash
--rename-table "dbo.OSUSR_RTJ_CATEGORY|Inventory::Category=Category_StaticEntity"
```

**Separator Explanation**:
- `|` separates physical and logical coordinates
- `::` separates module and entity in logical coordinate
- `=` precedes the override name

**Multiple Overrides**:
```bash
--rename-table "dbo.CUSTOMER=CustomerPortal;dbo.ORDER=OrderManagement"
```

**Delimiter**: `,` or `;` between multiple overrides

**Whitespace**: Trimmed from all segments

### 13.4 Override Precedence and Merging

**Merge Order**:
1. Configuration file `namingOverrides.rules`
2. CLI `--rename-table` flags
3. Later overrides for same coordinates win

**Conflict Resolution**:
- Same physical coordinates → CLI override wins
- Same logical coordinates → CLI override wins
- No warning on override collision (last wins)

**Example**:
```json
Config: { "schema": "dbo", "table": "CUSTOMER", "override": "Customer" }
CLI: --rename-table dbo.CUSTOMER=CUSTOMER_PORTAL
Result: CUSTOMER_PORTAL (CLI wins)
```

### 13.5 Override Application Scope

**Affected Artifacts**:
- Table file name: `<schema>.<override>.sql`
- CREATE TABLE statement: `CREATE TABLE [schema].[override]`
- Primary key constraint: `PK_<override>_<column>`
- Foreign key constraints: `FK_<ownerOverride>_<refOverride>_<columns>`
- Unique indexes: `UIX_<override>_<columns>`
- Non-unique indexes: `IX_<override>_<columns>`
- Extended properties: Table name in sp_addextendedproperty
- Manifest entries: All table references use override
- Seed data: Target table uses override
- DMM comparison: Override names compared

**Not Affected**:
- Source JSON model (unchanged)
- Profiling data (uses physical coordinates)
- Decision log physical coordinates (preserved for traceability)

### 13.6 Legacy Configuration Support

**Old Format** (still supported):
```json
"namingOverrides": {
  "tables": [
    { "schema": "dbo", "physical": "OSUSR_ABC_CUSTOMER", "override": "Customer" }
  ],
  "entities": [
    { "module": "AppCore", "entity": "Category", "override": "Category_Static" }
  ]
}
```

**Migration**: Unified `rules` array preferred going forward; deserializer merges legacy formats automatically

---

## 14. Remediation and Sentinel Values

### 14.1 Remediation Trigger Conditions

**When Remediation Required**:
1. **Aggressive Mode** + Attribute is mandatory + Data has NULLs
2. **Aggressive Mode** + Unique constraint required + Data has duplicates
3. **Aggressive Mode** + Foreign key required + Data has orphans

**Configuration**:
```json
"remediation": {
  "generatePreScripts": true,
  "sentinels": {
    "numeric": "0",
    "text": "",
    "date": "1900-01-01"
  },
  "maxRowsDefaultBackfill": 100000
}
```

### 14.2 Sentinel Value Mapping

**Sentinel by Data Type**:

| SQL Server Type Family | Default Sentinel | Purpose |
|------------------------|------------------|---------|
| int, bigint, smallint, tinyint | `0` | Zero for numeric IDs/counts |
| decimal, numeric, float, real | `0` | Zero for financial/scientific |
| bit | `0` | False for boolean |
| nvarchar, varchar, nchar, char | `''` (empty string) | Empty for text |
| datetime, datetime2, smalldatetime | `'1900-01-01'` | Minimum valid SQL date |
| date | `'1900-01-01'` | Minimum valid SQL date |
| time | `'00:00:00'` | Midnight |
| uniqueidentifier | `'00000000-0000-0000-0000-000000000000'` | Null GUID |
| varbinary, binary | `0x00` | Single zero byte |

**Customization**: Sentinels configurable per type family in configuration

### 14.3 NULL Remediation Script Generation

**Pattern**:
```sql
--------------------------------------------------------------------------------
-- NULL Remediation: dbo.Customer.Email
-- Reason: Column marked mandatory but contains NULL values
-- Rows Affected: ~47 (estimated)
--------------------------------------------------------------------------------
UPDATE [dbo].[Customer]
SET [Email] = ''  -- Sentinel for text type
WHERE [Email] IS NULL;
```

**Safety Limit**:
```json
"maxRowsDefaultBackfill": 100000
```
- If affected rows > limit: Warning generated, script not emitted
- Rationale: Large backfills should be reviewed/customized
- Administrator must manually remediate or increase limit

### 14.4 Orphan Remediation

**Detection**:
```sql
-- Orphan check for FK: Customer.CityId → City.Id
SELECT COUNT(*)
FROM [dbo].[Customer] c
WHERE c.[CityId] IS NOT NULL
  AND NOT EXISTS (
    SELECT 1 FROM [dbo].[City] city WHERE city.[Id] = c.[CityId]
  )
```

**Remediation Pattern**:
```sql
--------------------------------------------------------------------------------
-- Orphan Remediation: dbo.Customer.CityId → dbo.City.Id
-- Reason: Foreign key cannot be created due to orphan records
-- Rows Affected: ~12 (estimated)
--------------------------------------------------------------------------------
-- Option 1: Delete orphan rows (DESTRUCTIVE)
DELETE FROM [dbo].[Customer]
WHERE [CityId] IS NOT NULL
  AND NOT EXISTS (
    SELECT 1 FROM [dbo].[City] WHERE [Id] = [Customer].[CityId]
  );

-- Option 2: Set to sentinel value (SAFER)
-- UPDATE [dbo].[Customer]
-- SET [CityId] = 0  -- or another valid City.Id
-- WHERE [CityId] IS NOT NULL
--   AND NOT EXISTS (
--     SELECT 1 FROM [dbo].[City] WHERE [Id] = [Customer].[CityId]
--   );
```

**Choice**: Both options provided commented; administrator chooses approach

### 14.5 Duplicate Remediation for UNIQUE

**Detection**:
```sql
-- Duplicate check for UIX: Customer.Email
SELECT [Email], COUNT(*) as DuplicateCount
FROM [dbo].[Customer]
WHERE [Email] IS NOT NULL
GROUP BY [Email]
HAVING COUNT(*) > 1
```

**Remediation Pattern**:
```sql
--------------------------------------------------------------------------------
-- Duplicate Remediation: dbo.Customer.Email (UNIQUE constraint)
-- Reason: Unique index cannot be created due to duplicate values
-- Duplicate Groups: ~3
--------------------------------------------------------------------------------
-- Strategy: Keep first occurrence, delete or modify duplicates
-- Manual review required to determine correct business logic

WITH DuplicateRows AS (
  SELECT
    [Id],
    [Email],
    ROW_NUMBER() OVER (PARTITION BY [Email] ORDER BY [Id]) AS RowNum
  FROM [dbo].[Customer]
  WHERE [Email] IS NOT NULL
)
-- Option 1: Delete duplicates (keeps first by Id)
DELETE FROM DuplicateRows WHERE RowNum > 1;

-- Option 2: Modify duplicates to make unique
-- UPDATE DuplicateRows
-- SET [Email] = [Email] + '_' + CAST([Id] AS VARCHAR)
-- WHERE RowNum > 1;
```

### 14.6 Remediation Script Organization

**File Structure**:
```
suggestions/
├── safe-to-apply.sql          # No data changes required
└── needs-remediation.sql      # Requires data cleanup
```

**safe-to-apply.sql Contents**:
- ALTER TABLE ADD CONSTRAINT (when data is already compliant)
- CREATE INDEX (when no duplicates found)
- Comments explaining each operation

**needs-remediation.sql Contents**:
- NULL backfill scripts
- Orphan deletion/correction scripts
- Duplicate resolution scripts
- Safety warnings and manual review notes

**Header**:
```sql
/* ==========================================================================
   REQUIRES REMEDIATION - MANUAL REVIEW REQUIRED

   This script contains data modifications that must be reviewed before
   applying. Each section includes:
   - Reason for remediation
   - Estimated rows affected
   - Multiple remediation options (choose one)

   DO NOT RUN AUTOMATICALLY - REVIEW AND CUSTOMIZE FIRST
   ==========================================================================
*/
```

### 14.7 Remediation Decision Recording

**In policy-decisions.json**:
```json
{
  "Schema": "dbo",
  "Table": "Customer",
  "Column": "Email",
  "MakeNotNull": true,
  "RequiresRemediation": true,
  "Rationales": [
    "MANDATORY",
    "DATA_HAS_NULLS",
    "REMEDIATE_BEFORE_TIGHTEN"
  ],
  "NullCount": 47,
  "TotalRows": 10523,
  "NullPercentage": 0.0045
}
```

**In opportunities.json**:
```json
{
  "Type": "Nullability",
  "ConstraintType": "NOT NULL",
  "Summary": "Remediate data before enforcing NOT NULL.",
  "RiskLevel": "Medium",
  "Disposition": "NeedsRemediation",
  "Column": {
    "Schema": "dbo",
    "Table": "Customer",
    "Column": "Email"
  },
  "Evidence": {
    "NullCount": 47,
    "TotalRows": 10523
  },
  "Rationales": ["MANDATORY", "DATA_HAS_NULLS"]
}
```

**In validations.json**:
```json
{
  "Type": "Nullability",
  "Summary": "Validated: Column is already NOT NULL and profiling confirms data integrity.",
  "Schema": "dbo",
  "Table": "Customer",
  "ConstraintName": "Email",
  "Evidence": [
    "Rows=10523",
    "Nulls=0 (Outcome=Succeeded, Sample=10523, Captured=2024-01-01T00:00:00Z)"
  ]
}
```

---

## 15. Output File Content Structure

### 15.1 Per-Table DDL File Format

**Complete Structure**:
```sql
[Optional: Header Comment Block]

CREATE TABLE [schema].[table] (
    [Column1] TYPE CONSTRAINT,
    [Column2] TYPE
        DEFAULT (value),
    [Column3] TYPE CONSTRAINT
        FOREIGN KEY (...) REFERENCES [...]
)

GO

[Index Definitions - Alphabetically Sorted]
CREATE [UNIQUE] INDEX [name]
    ON [schema].[table]([columns])
    [WHERE predicate]
    [WITH options]
    [ON filegroup]

GO

[Disabled Index Alterations]
ALTER INDEX [name]
    ON [schema].[table] DISABLE

GO

[Extended Properties]
EXEC sys.sp_addextendedproperty ...

GO
```

### 15.2 CREATE TABLE Section

**Column Definition Order**:
1. Primary key column (identity) - always first
2. Regular columns - in attribute definition order from OutSystems
3. Foreign key columns - in natural position (not grouped)

**Constraint Ordering** (per column):
1. Data type
2. NULL/NOT NULL
3. IDENTITY (if applicable)
4. DEFAULT constraint (inline, indented)
5. PRIMARY KEY constraint (inline for PK column)
6. FOREIGN KEY constraint (inline for FK column)

**Example**:
```sql
CREATE TABLE [dbo].[Customer] (
    [Id]        BIGINT         IDENTITY (1, 1) NOT NULL
        CONSTRAINT [PK_Customer_Id]
            PRIMARY KEY CLUSTERED,
    [Email]     NVARCHAR (255) NOT NULL,
    [FirstName] NVARCHAR (100) NULL
        DEFAULT (''),
    [LastName]  NVARCHAR (100) NULL
        DEFAULT (''),
    [CityId]    BIGINT         NOT NULL
        CONSTRAINT [FK_Customer_City_CityId]
            FOREIGN KEY ([CityId]) REFERENCES [dbo].[City] ([Id])
)
```

**Indentation**:
- Column name: Left-aligned at position 4 (spaces)
- Data type: Aligned at position 20
- Constraints: Indented 8 spaces from constraint keyword
- Nested constraint definitions: Indented 12 spaces

### 15.3 Index Section

**Per-Index Format**:
```sql
CREATE [UNIQUE] [CLUSTERED|NONCLUSTERED] INDEX [IndexName]
    ON [schema].[table]([column1], [column2], ...)
    [INCLUDE ([included_columns])]
    [WHERE filter_predicate]
    [WITH (
        option1 = value1,
        option2 = value2
    )]
    [ON filegroup]
```

**WITH Options Order** (when multiple present):
1. FILLFACTOR
2. PAD_INDEX
3. IGNORE_DUP_KEY
4. STATISTICS_NORECOMPUTE
5. ALLOW_ROW_LOCKS
6. ALLOW_PAGE_LOCKS
7. DATA_COMPRESSION

**Example**:
```sql
CREATE UNIQUE INDEX [UIX_Customer_Email]
    ON [dbo].[Customer]([Email])
    INCLUDE ([FirstName], [LastName])
    WHERE ([Email] IS NOT NULL)
    WITH (
        FILLFACTOR = 85,
        PAD_INDEX = ON,
        IGNORE_DUP_KEY = ON,
        STATISTICS_NORECOMPUTE = ON
    )
    ON [FG_Customers]
```

### 15.4 Extended Properties Section

**Format**:
```sql
EXEC sys.sp_addextendedproperty
    @name=N'MS_Description',
    @value=N'[description text]',
    @level0type=N'SCHEMA', @level0name=N'[schema]',
    @level1type=N'TABLE',  @level1name=N'[table]'
    [, @level2type=N'COLUMN', @level2name=N'[column]'];
```

**Order**:
1. Table-level description (if present)
2. Column-level descriptions (in column definition order)

**Spacing**: One blank line before first extended property block; GO separator after each

### 15.5 Batch Separators (GO)

**Placement Rules**:
- After CREATE TABLE statement
- After each CREATE INDEX statement
- After each ALTER INDEX statement
- After each extended property statement

**Formatting**: `GO` on its own line, no indentation

**Rationale**: Ensures each DDL statement executes in its own batch (SQL Server requirement)

### 15.6 Header Comment Block

**Configuration**: `emission.emitTableHeaders = true`

**Format**:
```sql
/*
    Table: dbo.Customer
    Module: AppCore
    Source Entity: Customer (OSUSR_ABC_CUSTOMER)

    Generated by: OutSystems DDL Exporter
    Profile: /path/to/profile.json
    Model: /path/to/model.json
    Decisions: /path/to/policy-decisions.json

    SHA256: abc123def456... (optional fingerprint)
*/
```

**Purpose**: Traceability and audit support

---

## 16. Determinism and Reproducibility Guarantees

### 16.1 Deterministic Ordering

**Module Order**: Alphabetical by module name (case-insensitive)

**Table Order** (within module):
1. Schema name (alphabetical)
2. Logical table name (alphabetical)
3. Physical table name (tie-breaker)

**Column Order** (within table):
- Preserved from OutSystems attribute definition order
- Primary key always first (even if defined later in model)

**Index Order**: Alphabetical by index logical name (case-insensitive)

**Constraint Order** (within column):
- Type → Nullability → Identity → Default → PK → FK

**Rationale**: Same input always produces identical output (byte-for-byte)

### 16.2 Cache Keys and Evidence Reusability

**Cache Key Components**:
- Model file hash (SHA256)
- Profile snapshot hash (SHA256)
- Module filter (sorted, normalized)
- Configuration hash (tightening options, naming overrides)

**Cache Behavior**:
```
if (cache_key matches existing):
    reuse cached evidence and decisions
else:
    recompute from scratch
    store with new cache key
```

**Cache Location**: `.artifacts/cache/` (configurable via `cache.root`)

**Refresh**: `--refresh-cache` flag forces recomputation

### 16.3 Stable Naming Convention Application

**Token Normalization**: Always applied in same order
1. Physical → Logical name replacement
2. OutSystems prefix extraction
3. Tokenize by underscore
4. Normalize each token (case rules)
5. Rejoin with underscores
6. Apply naming override (if configured)

**Result**: Identical constraint names across runs

**Example Stability**:
```
Input (run 1): OSUSR_ABC_CUSTOMER
Input (run 2): OSUSR_ABC_CUSTOMER
Both produce: Customer (after normalization)

Constraint (run 1): PK_Customer_Id
Constraint (run 2): PK_Customer_Id (identical)
```

### 16.4 Timestamp and GUID Avoidance

**No Random Values**:
- No GUIDs generated (except from source data)
- No timestamps in DDL
- No random ordering

**Manifest Metadata**: Contains generation timestamp but doesn't affect DDL content

**Deterministic IDs**: All identifiers derived from logical names + rules (no entropy)

### 16.5 Sorting Algorithms

**String Comparison**:
- `StringComparer.OrdinalIgnoreCase` (consistent across cultures)
- No locale-dependent sorting

**Numeric Comparison**: Standard integer/decimal comparison

**Null Handling**: Nulls sort first (consistent)

**Example**:
```
Modules sorted:
  AppCore
  ExtBilling
  Ops
  ServiceCenter

Indexes sorted:
  IX_Customer_CityId
  IX_Customer_LastName_FirstName
  UIX_Customer_Email
```

### 16.6 Reproducibility Validation

**Test Strategy**:
- Run pipeline twice with same inputs
- Binary compare all output files
- Assert byte-for-byte equality

**Regression Tests**: Fixtures under `tests/Fixtures/emission/` serve as golden baselines

**CI/CD Integration**: Determinism validated on every PR

---

## 17. Configuration Hierarchy and Precedence

### 17.1 Configuration Source Precedence

**Order** (highest to lowest priority):
1. **CLI Flags**: `--modules`, `--rename-table`, `--out`, etc.
2. **Environment Variables**: `OSM_CLI_*` variables
3. **CLI Configuration File**: `--config appsettings.json`
4. **Default Configuration**: `config/default-tightening.json`, `config/type-mapping.default.json`
5. **Hardcoded Defaults**: Built into application

### 17.2 Configuration Merging Rules

**Additive Properties** (merged):
- `namingOverrides.rules`: CLI rules added to config rules
- `modules`: CLI modules merged with config modules (union)

**Override Properties** (CLI wins):
- `policy.mode`: CLI overrides config
- `foreignKeys.enableCreation`: CLI overrides config
- `emission.perTableFiles`: CLI overrides config

**Example**:
```
Config: emission.perTableFiles = true
CLI: --emit-bare-table-only
Result: emitBareTableOnly = true, perTableFiles = true (merged intent)
```

### 17.3 Environment Variable Mapping

**Variable Naming Pattern**: `OSM_CLI_<SECTION>_<KEY>`

**Examples**:
```bash
export OSM_CLI_MODEL_PATH=/path/to/model.json
export OSM_CLI_PROFILE_PATH=/path/to/profile.json
export OSM_CLI_CONNECTION_STRING="Server=...;Database=..."
export OSM_CLI_CACHE_ROOT=/tmp/cache
export OSM_CLI_REFRESH_CACHE=true
export OSM_CLI_PROFILER_PROVIDER=sql
export OSM_CLI_SQL_COMMAND_TIMEOUT=300
```

**Type Coercion**:
- Strings: Used as-is
- Booleans: `true`, `false` (case-insensitive)
- Numbers: Parsed as integer/decimal
- Paths: Expanded if relative

### 17.4 Configuration File Structure

**appsettings.json** (example):
```json
{
  "tighteningPath": "config/default-tightening.json",
  "model": {
    "path": "tests/Fixtures/model.edge-case.json",
    "modules": ["AppCore", "ExtBilling"],
    "includeSystemModules": false,
    "includeInactiveModules": false
  },
  "profile": {
    "path": "tests/Fixtures/profiling/profile.edge-case.json"
  },
  "cache": {
    "root": ".artifacts/cache",
    "refresh": false
  },
  "profiler": {
    "provider": "Fixture",
    "profilePath": "tests/Fixtures/profiling/profile.edge-case.json"
  },
  "sql": {
    "connectionString": "Server=localhost;Database=OutSystems;Trusted_Connection=True;",
    "commandTimeoutSeconds": 120,
    "sampling": {
      "rowThreshold": 250000,
      "sampleSize": 75000
    },
    "authentication": {
      "method": "Default"
    }
  },
  "supplementalModels": {
    "includeUsers": true,
    "paths": ["config/supplemental/ossys-user.json"]
  }
}
```

### 17.5 Tightening Configuration Deep Dive

**Complete default-tightening.json Structure**:
```json
{
  "policy": {
    "mode": "EvidenceGated",
    "nullBudget": 0.0
  },
  "foreignKeys": {
    "enableCreation": true,
    "allowCrossSchema": false,
    "allowCrossCatalog": false,
    "treatMissingDeleteRuleAsIgnore": false
  },
  "uniqueness": {
    "enforceSingleColumnUnique": true,
    "enforceMultiColumnUnique": true
  },
  "remediation": {
    "generatePreScripts": true,
    "sentinels": {
      "numeric": "0",
      "text": "",
      "date": "1900-01-01"
    },
    "maxRowsDefaultBackfill": 100000
  },
  "emission": {
    "perTableFiles": true,
    "includePlatformAutoIndexes": false,
    "sanitizeModuleNames": true,
    "emitBareTableOnly": false,
    "emitTableHeaders": false,
    "moduleParallelism": 1,
    "namingOverrides": {
      "rules": []
    },
    "staticSeeds": {
      "groupByModule": true,
      "emitMasterFile": false,
      "mode": "NonDestructive"
    }
  },
  "mocking": {
    "useProfileMockFolder": false,
    "profileMockFolder": null
  }
}
```

**Customization Points**:
- Change `mode` to `Aggressive` for strict enforcement
- Adjust `nullBudget` to allow small percentage of nulls
- Disable `enableCreation` to skip all FK generation
- Customize `sentinels` for domain-specific defaults
- Enable `emitTableHeaders` for audit trail

---

## 18. Mode-Specific Behavior Matrix

### 18.1 Complete Behavior Comparison

| Feature | Cautious | EvidenceGated (Default) | Aggressive |
|---------|----------|-------------------------|------------|
| **NOT NULL Decision** | PK OR Physical NOT NULL | (PK OR Physical NOT NULL) OR ((Mandatory OR FK OR Unique) AND Data has no NULLs) | PK OR Physical NOT NULL OR Mandatory OR FK OR Unique |
| **Trusts Metadata** | No | Partially (with evidence) | Yes |
| **Requires Profiling** | Recommended | Yes (blocks without) | Yes (for remediation) |
| **Generates Remediation** | Never | Rarely (only on contradiction) | Often (when metadata ≠ data) |
| **FK Creation** | Evidence-only (constraint exists) | Evidence-gated (orphans block) | Metadata-driven (remediate orphans) |
| **UNIQUE Enforcement** | Evidence-only | Evidence-gated (duplicates block) | Metadata-driven (remediate duplicates) |
| **Default Behavior** | Most conservative | Balanced | Most aggressive |
| **Use Case** | Legacy migrations, unreliable metadata | Standard production | Greenfield, strict governance |
| **Risk Level** | Lowest (may miss constraints) | Medium (balanced) | Highest (requires careful remediation) |

### 18.2 Signal Usage by Mode

| Signal | Cautious | EvidenceGated | Aggressive |
|--------|----------|---------------|------------|
| S1: Primary Key | ✓ Strong | ✓ Strong | ✓ Strong |
| S2: Physical NOT NULL | ✓ Strong | ✓ Strong | ✓ Strong |
| S3: FK Constraint Exists | Ignored | ✓ Strong (with data check) | ✓ Strong |
| S4: Unique No Nulls | Ignored | ✓ Strong (with data check) | ✓ Strong |
| S5: Mandatory Metadata | Ignored | Weak (needs evidence) | ✓ Strong |
| S7: Default Present | Ignored | Supporting evidence | Supporting evidence |
| Data Evidence (no nulls) | Required | Required (for S3-S5) | Optional (remediate if mismatch) |

### 18.3 Remediation Script Generation

| Scenario | Cautious | EvidenceGated | Aggressive |
|----------|----------|---------------|------------|
| Mandatory + Has NULLs | No script (column stays nullable) | No script (column stays nullable) | UPDATE script generated |
| FK Reference + Orphans | No FK created, no script | No FK created, warning | DELETE/UPDATE script for orphans |
| Unique + Duplicates | No UIX created, no script | No UIX created, warning | Resolution script with options |
| Physical NOT NULL + Has NULLs | Error (data inconsistency) | Error (data inconsistency) | Error (physical mismatch) |

### 18.4 Decision Rationale Differences

**Example Column**: `Customer.Email` (mandatory in OutSystems, has 3 NULLs in data)

#### Cautious Mode
```json
{
  "MakeNotNull": false,
  "Rationales": ["PROFILE_MISSING_OR_INSUFFICIENT"],
  "Explanation": "Data has nulls and metadata ignored in Cautious mode"
}
```

#### EvidenceGated Mode
```json
{
  "MakeNotNull": false,
  "Rationales": ["MANDATORY", "DATA_HAS_NULLS"],
  "Explanation": "Metadata says mandatory but evidence contradicts; safe default is nullable"
}
```

#### Aggressive Mode
```json
{
  "MakeNotNull": true,
  "RequiresRemediation": true,
  "Rationales": ["MANDATORY", "DATA_HAS_NULLS", "REMEDIATE_BEFORE_TIGHTEN"],
  "Explanation": "Metadata wins; data must be fixed to match",
  "RemediationScript": "UPDATE [dbo].[Customer] SET [Email] = '' WHERE [Email] IS NULL;"
}
```

### 18.5 Mode Selection Guidelines

**Choose Cautious When**:
- Migrating legacy systems with unreliable metadata
- OutSystems model not maintained (physical DB is source of truth)
- Conservative approach required (prefer nullable over broken constraints)
- Profiling data incomplete or suspect

**Choose EvidenceGated When** (RECOMMENDED):
- Modern OutSystems application with maintained model
- Profiling data available and trustworthy
- Balance between safety and correctness desired
- Standard production migration scenario

**Choose Aggressive When**:
- Greenfield project (OutSystems metadata is authoritative)
- Strong data governance in place
- Willing to execute remediation scripts before deployment
- Want to enforce maximum constraints for data quality

### 18.6 Mode Impact on Manifest

**Manifest Metadata**:
```json
{
  "TighteningOptions": {
    "Mode": "EvidenceGated",
    "NullBudget": 0.0
  },
  "DecisionSummary": {
    "TotalColumns": 156,
    "NotNullDecisions": 98,
    "RemediationRequired": 0,
    "ForeignKeysCreated": 23,
    "UniqueIndexesCreated": 12
  }
}
```

**Comparison**:
- Cautious: Fewer NOT NULL, fewer FKs, fewer UIX (most conservative counts)
- EvidenceGated: Moderate counts (balanced)
- Aggressive: Highest counts + remediation scripts (maximum enforcement)

---

## 19. Transformation Pipeline Summary

### 19.1 End-to-End Flow

```
┌─────────────────────────────────────────────────────────────┐
│ 1. EXTRACTION (Advanced SQL)                                │
│    Input: OutSystems database                               │
│    Output: model.json (modules → entities → attributes)     │
│    Logic: Single Advanced SQL query extracts complete model │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│ 2. INGESTION (JSON Deserialization)                         │
│    Input: model.json                                        │
│    Output: ModelRoot (domain objects)                       │
│    Logic: Parse, validate schema, create immutable model    │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│ 3. PROFILING (SQL Server Catalog + Data Sweep)              │
│    Input: ModelRoot, connection string                      │
│    Output: ProfileSnapshot (NULLs, duplicates, orphans)     │
│    Logic: sys.* metadata + dynamic COUNT queries per table  │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│ 4. TIGHTENING ANALYSIS (Policy Evaluation)                  │
│    Input: ModelRoot, ProfileSnapshot, TighteningOptions     │
│    Output: TighteningDecisions (NOT NULL, FK, UNIQUE)       │
│    Logic: Per-column evaluation with signal tree + evidence │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│ 5. SMO MATERIALIZATION (Object Graph Building)              │
│    Input: ModelRoot, TighteningDecisions                    │
│    Output: SMO Database object graph                        │
│    Logic: Create SMO Table/Column/Index/FK objects          │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│ 6. PER-TABLE EMISSION (DDL Script Generation)               │
│    Input: SMO Database                                      │
│    Output: <schema>.<table>.sql files                       │
│    Logic: CREATE TABLE + indexes + extended properties      │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│ 7. SEED DATA GENERATION (Static Entities)                   │
│    Input: ModelRoot (static entities), data provider        │
│    Output: StaticEntities.seed.sql files                    │
│    Logic: MERGE statements ordered deterministically        │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│ 8. MANIFEST GENERATION (Metadata + Decisions)               │
│    Input: All pipeline outputs                              │
│    Output: manifest.json, policy-decisions.json             │
│    Logic: Aggregate metadata, decision rationales           │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│ 9. (Optional) DMM COMPARISON (Validation)                   │
│    Input: SMO model, DMM DDL scripts                        │
│    Output: dmm-diff.json (differences)                      │
│    Logic: ScriptDom parse + structural comparison           │
└─────────────────────────────────────────────────────────────┘
```

### 19.2 Pipeline Stage Details

#### Stage 1: Extraction
- **Tool**: Advanced SQL Server Action in OutSystems
- **Query Location**: `src/AdvancedSql/outsystems_model_export.sql`
- **Performance**: Single query execution (~10-30 seconds for medium apps)
- **Output Size**: Typically 1-50 MB JSON
- **Key Metadata**: Physical names, logical names, on-disk types, delete rules, index definitions

#### Stage 2: Ingestion
- **Deserializer**: `ModelJsonDeserializer` in `Osm.Json`
- **Validation**: JSON schema validation, required field checks
- **Transformations**: Type normalization, value object creation
- **Errors**: Fail-fast on malformed JSON or missing required fields

#### Stage 3: Profiling
- **Profiler Types**:
  - SQL Server live profiler (queries running database)
  - Fixture profiler (replays saved JSON snapshot)
- **Queries Generated**: One dynamic query per table (NULL counts)
- **Sampling**: Automatic when table exceeds threshold (default 250K rows)
- **Performance**: ~1-5 minutes for 100 tables (non-sampled)

#### Stage 4: Tightening Analysis
- **Evaluators**: NullabilityEvaluator, ForeignKeyEvaluator, UniqueEvaluator
- **Parallelization**: Per-column analysis (thread-safe)
- **Decision Storage**: In-memory decision tree with rationales
- **Output**: Structured decisions with evidence links

#### Stage 5: SMO Materialization
- **Factory**: `SmoModelFactory` in `Osm.Smo`
- **SMO Version**: Compatible with SQL Server 2017+
- **Object Creation**: Table → Columns → PK → Indexes → FKs → ExtendedProperties
- **Validation**: SMO validates object graph before emission

#### Stage 6: Per-Table Emission
- **Writer**: `PerTableWriter` in `Osm.Smo/PerTableEmission`
- **Builders**:
  - CreateTableStatementBuilder
  - IndexScriptBuilder
  - ExtendedPropertyScriptBuilder
- **Formatting**: Indentation, line breaks, GO separators
- **Output**: One .sql file per entity

#### Stage 7: Seed Data Generation
- **Generator**: `StaticEntitySeedScriptGenerator` in `Osm.Emission/Seeds`
- **Data Source**: Live database or fixture data
- **Ordering**: Module → Schema → Table → PK values
- **Format**: MERGE statements with explicit column lists

#### Stage 8: Manifest Generation
- **Content**:
  - Tables emitted (with paths)
  - Indexes created (names)
  - Foreign keys created (names)
  - Extended properties included (boolean)
  - Decision summary (counts by rationale)
- **Format**: JSON (structured, diffable)

#### Stage 9: DMM Comparison
- **Parser**: ScriptDom TSql150Parser
- **Comparison**: Table structure, column types, PK columns
- **Tolerances**: Case-insensitive, whitespace-agnostic
- **Output**: Structured diff with line-level details

### 19.3 Error Handling and Recovery

**Extraction Failures**:
- Invalid SQL syntax → Error with line number
- Missing OutSystems tables → Error with table name
- Timeout → Retry with increased timeout or sampling

**Ingestion Failures**:
- JSON parse error → Line/column of syntax error
- Schema validation failure → Missing field name
- Invalid value → Field name + expected type

**Profiling Failures**:
- Connection failure → Retry with exponential backoff
- Query timeout → Skip table or reduce sample size
- Permission denied → Error with required permissions list

**Tightening Failures**:
- Missing profile data → Warning, fall back to safe defaults
- Contradictory signals → Warning, document in rationales
- Invalid configuration → Error with config path + issue

**Emission Failures**:
- File system error → Retry or error with path
- SMO exception → Error with table/object name
- SQL validation failure → Error with DDL + line number

**Seed Generation Failures**:
- Data type mismatch → Error with column name + types
- NULL in NOT NULL → Error with row identifier
- Duplicate PK → Error with conflicting values

### 19.4 Performance Characteristics

**Typical Execution Times** (100 entities, 1500 columns):

| Stage | Time | Bottleneck |
|-------|------|------------|
| Extraction | 10-30s | SQL Server query execution |
| Ingestion | 1-3s | JSON parsing |
| Profiling (live) | 2-10min | NULL count queries per table |
| Profiling (fixture) | <1s | JSON load |
| Tightening | 5-15s | Per-column evaluation |
| SMO Building | 10-30s | Object graph creation |
| Emission | 30-60s | File I/O per table |
| Seed Generation | 10-60s | Data retrieval + formatting |
| Manifest | <1s | JSON serialization |
| **Total (live)** | **5-15min** | Profiling dominates |
| **Total (fixture)** | **1-3min** | Emission dominates |

**Optimization Strategies**:
- Use sampling for large tables (profiling)
- Cache profile snapshots for reuse
- Run in fixture mode for development iterations
- Parallelize emission (configurable `moduleParallelism`)

---

## 20. Edge Cases and Special Handling

### 20.1 Cross-Schema Foreign Keys

**Scenario**: FK from `dbo.Order` to `billing.Account`

**Configuration Impact**:
```json
"foreignKeys": {
  "allowCrossSchema": false
}
```

**Behavior**:
- `false`: FK not created, rationale: `CROSS_SCHEMA_BLOCKED`
- `true`: FK created if all other checks pass

**Use Case**:
- `false` for strict domain separation
- `true` for integrated schemas

### 20.2 Cross-Catalog Foreign Keys

**Scenario**: FK from `AppDB.dbo.Order` to `AccountingDB.dbo.Account`

**Configuration Impact**:
```json
"foreignKeys": {
  "allowCrossCatalog": false
}
```

**Behavior**:
- `false`: FK not created, rationale: `CROSS_CATALOG_BLOCKED`
- `true`: FK created (SQL Server supports if linked)

**Note**: Cross-catalog FKs require linked servers and may have performance implications

### 20.3 Self-Referencing Foreign Keys

**Scenario**: `Employee.ManagerId` → `Employee.Id`

**Handling**:
- Detected as self-reference
- FK created normally if evidence supports
- No special orphan check (would always find self)

**Example**:
```sql
CREATE TABLE [dbo].[Employee] (
    [Id] BIGINT NOT NULL
        CONSTRAINT [PK_Employee_Id]
            PRIMARY KEY,
    [ManagerId] BIGINT NULL
        CONSTRAINT [FK_Employee_Employee_ManagerId]
            FOREIGN KEY ([ManagerId]) REFERENCES [dbo].[Employee] ([Id])
)
```

### 20.4 Composite Primary Keys

**OutSystems Support**: Limited (typically single-column identifier)

**External Entities**: May have composite PKs

**Handling**:
```sql
CREATE TABLE [dbo].[OrderLine] (
    [OrderId]   BIGINT NOT NULL,
    [ProductId] BIGINT NOT NULL,
    [Quantity]  INT NOT NULL,
    CONSTRAINT [PK_OrderLine_OrderId_ProductId]
        PRIMARY KEY CLUSTERED ([OrderId], [ProductId])
)
```

**Key Pattern**: `PK_<Table>_<Col1>_<Col2>_..._<ColN>`

### 20.5 Computed Columns

**Detection**: `attribute.onDisk.isComputed = true`

**Emission**:
```sql
[FullName] AS ([FirstName] + ' ' + [LastName]) PERSISTED
```

**Seed Data**: Excluded from INSERT/MERGE statements

**Indexing**: Indexes on computed columns preserved

### 20.6 Sparse Columns

**Detection**: `sys.columns.is_sparse = 1`

**Emission**: SPARSE keyword preserved
```sql
[OptionalData] NVARCHAR(MAX) SPARSE NULL
```

**Use Case**: Columns with high percentage of NULLs

### 20.7 Temporal Tables (System-Versioned)

**Detection**: `entity` has temporal metadata or `SYSTEM_TIME` index

**Emission**:
```sql
CREATE TABLE [dbo].[TemporalOrder] (
    [Id] BIGINT NOT NULL,
    [OrderDate] DATETIME2 NOT NULL,
    [ValidFrom] DATETIME2 GENERATED ALWAYS AS ROW START NOT NULL,
    [ValidTo] DATETIME2 GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME ([ValidFrom], [ValidTo]),
    CONSTRAINT [PK_TemporalOrder_Id]
        PRIMARY KEY CLUSTERED ([Id])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[TemporalOrder_History]));
```

**Note**: Full temporal table support requires explicit configuration

### 20.8 Filegroups and Partitioning

**Filegroup Preservation**:
- Table filegroup: `ON [FileGroupName]`
- Index filegroup: Preserved from on-disk
- Default: `ON [PRIMARY]` if not specified

**Partitioning**: Partition schemes and functions not yet supported (future)

### 20.9 Collation Overrides

**Column-Level Collation**:
```sql
[Email] NVARCHAR(255) COLLATE Latin1_General_CI_AI NOT NULL
```

**Preservation**: From on-disk metadata

**Use Case**: Mixed-collation databases

### 20.10 Identity Seed/Increment Customization

**Standard**: `IDENTITY(1, 1)`

**Custom** (preserved from on-disk):
```sql
[Id] BIGINT IDENTITY(1000, 10) NOT NULL
```

**Seed Data**: Requires `SET IDENTITY_INSERT ON` when seed < current identity

### 20.11 Filtered Indexes on Multiple Columns

**Scenario**: Unique index on composite key with filter

**Example**:
```sql
CREATE UNIQUE INDEX [UIX_OrderLine_OrderId_ProductId]
    ON [dbo].[OrderLine]([OrderId], [ProductId])
    WHERE ([OrderId] IS NOT NULL AND [ProductId] IS NOT NULL)
```

**Generation**: Automatic when any column in composite is nullable

### 20.12 XML Columns and Schemas

**Basic XML**:
```sql
[Config] XML NULL
```

**With XML Schema Collection**:
```sql
[Config] XML (CONTENT [dbo].[ConfigSchemaCollection]) NULL
```

**Preservation**: Schema collection reference preserved from on-disk

### 20.13 GUID vs Sequential GUID

**Standard GUID**:
```sql
[Id] UNIQUEIDENTIFIER NOT NULL
    DEFAULT (NEWID())
```

**Sequential GUID** (OutSystems common pattern):
```sql
[Id] UNIQUEIDENTIFIER NOT NULL
    DEFAULT (NEWSEQUENTIALID())
```

**Preservation**: Default expression preserved from on-disk or OutSystems metadata

### 20.14 Large Object Types (LOB)

**varchar(max), nvarchar(max), varbinary(max)**:
- Stored out-of-row if > 8000 bytes
- Index considerations (key size limits)
- TEXT/NTEXT/IMAGE (deprecated) mapped to MAX types

**Handling**: Automatic via type mapping rules

### 20.15 Inactive/Soft-Deleted Entities

**Detection**: `entity.IsActive = false` OR `attribute.IsActive = false`

**Behavior**:
- Entities: Excluded by default (unless `includeInactiveModules = true`)
- Attributes: Excluded by default (unless `onlyActiveAttributes = false`)

**Physical Presence**: Flag `physical_isPresentButInactive` indicates column still in DB

**Use Case**: Gradual schema evolution tracking

---

## Appendix: Quick Reference Tables

### A. All Constraint Naming Patterns

| Constraint Type | Pattern | Example |
|----------------|---------|---------|
| Primary Key | `PK_<Table>_<Column>` | `PK_Customer_Id` |
| Foreign Key (single) | `FK_<OwnerTable>_<RefTable>_<Column>` | `FK_Customer_City_CityId` |
| Foreign Key (composite) | `FK_<OwnerTable>_<RefTable>_<Col1>_<Col2>` | `FK_OrderLine_Order_Product_OrderId_ProductId` |
| Unique Index (single) | `UIX_<Table>_<Column>` | `UIX_Customer_Email` |
| Unique Index (composite) | `UIX_<Table>_<Col1>_<Col2>_...` | `UIX_OrderLine_OrderId_ProductId` |
| Non-Unique Index | `IX_<Table>_<Col1>_<Col2>_...` | `IX_Customer_LastName_FirstName` |
| Default Constraint | `DF__<Table>__<Column>__<hash>` | System-generated by SMO |

### B. All Tightening Rationale Codes

| Code | Meaning | Typical Impact |
|------|---------|----------------|
| PRIMARY_KEY | Column is PK (IsIdentifier) | NOT NULL |
| PHYSICAL_NOT_NULL | sys.columns.is_nullable = 0 | NOT NULL |
| DATA_NO_NULLS | Profile shows 0 NULLs | NOT NULL (with other signals) |
| MANDATORY | Logical attribute marked mandatory | NOT NULL (mode-dependent) |
| UNIQUE_NO_NULLS | Unique constraint + no nulls | NOT NULL |
| DEFAULT_PRESENT | Column has default value | Supporting evidence |
| FK_CONSTRAINT | FK with DB constraint exists | NOT NULL (typically) |
| PROFILE_MISSING | No profiling data | NULLABLE (safe default) |
| NULL_BUDGET_EXCEEDED | NULL % > configured budget | NULLABLE |
| NULL_BUDGET_OK | NULL % ≤ budget | Supports NOT NULL |
| DATA_HAS_NULLS | Profile contradicts metadata | NULLABLE or Remediate |
| REMEDIATE_BEFORE_TIGHTEN | Aggressive + data mismatch | NOT NULL + Remediation |
| DB_CONSTRAINT_PRESENT | FK already exists | Create FK |
| DATA_NO_ORPHANS | No orphan records found | Create FK |
| DATA_HAS_ORPHANS | Orphan records detected | No FK or Remediate |
| DELETE_RULE_PROTECT | Delete rule = Protect | Create FK |
| DELETE_RULE_CASCADE | Delete rule = Cascade | Create FK |
| DELETE_RULE_IGNORE | Delete rule = Ignore | No FK |
| CROSS_SCHEMA_BLOCKED | Schema mismatch blocked | No FK |
| CROSS_CATALOG_BLOCKED | Catalog mismatch blocked | No FK |

### C. Complete Data Type Mapping

| OutSystems Type | SQL Server Type | Notes |
|-----------------|-----------------|-------|
| identifier | bigint | Always NOT NULL, IDENTITY |
| autonumber | bigint | Always NOT NULL, IDENTITY |
| integer | int | |
| longinteger | bigint | |
| boolean | bit | |
| datetime | datetime | Legacy compatibility |
| datetime2 | datetime2(7) | Precision: 100ns |
| datetimeoffset | datetimeoffset(7) | Timezone-aware |
| date | date | Date only (no time) |
| time | time(7) | Time only (no date) |
| decimal | decimal(18, 0) | Configurable precision/scale |
| currency | decimal(37, 8) | High precision for finance |
| double | float | |
| float | float | |
| real | real | |
| text | nvarchar(n) or nvarchar(max) | Threshold: 2000 chars |
| longtext | nvarchar(max) | |
| email | varchar(250) | Non-Unicode (ASCII) |
| phonenumber | varchar(20) | Non-Unicode |
| phone | varchar(20) | Non-Unicode |
| binarydata | varbinary(max) | |
| guid | uniqueidentifier | |
| xml | xml | XML data type |

### D. Configuration Quick Reference

| Setting | Default | Impact |
|---------|---------|--------|
| `policy.mode` | `EvidenceGated` | NOT NULL decision philosophy |
| `policy.nullBudget` | `0.0` | Max % nulls allowed for NOT NULL |
| `foreignKeys.enableCreation` | `true` | Whether to create FKs |
| `foreignKeys.allowCrossSchema` | `false` | Allow FKs across schemas |
| `foreignKeys.allowCrossCatalog` | `false` | Allow FKs across databases |
| `foreignKeys.treatMissingDeleteRuleAsIgnore` | `false` | Missing delete rule handling |
| `uniqueness.enforceSingleColumnUnique` | `true` | Create single-column UIX |
| `uniqueness.enforceMultiColumnUnique` | `true` | Create composite UIX |
| `remediation.generatePreScripts` | `true` | Generate remediation SQL |
| `remediation.maxRowsDefaultBackfill` | `100000` | Safety limit for UPDATE scripts |
| `emission.perTableFiles` | `true` | One file per table vs monolithic |
| `emission.includePlatformAutoIndexes` | `false` | Include system-generated indexes |
| `emission.sanitizeModuleNames` | `true` | Clean module names for file paths |
| `emission.emitBareTableOnly` | `false` | Suppress indexes/FKs/properties |
| `emission.emitTableHeaders` | `false` | Add metadata comment blocks |
| `emission.staticSeeds.groupByModule` | `true` | Separate seed file per module |
| `emission.staticSeeds.mode` | `NonDestructive` | Seed synchronization strategy |

---

## Document Revision History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | 2025-11-03 | Initial comprehensive documentation of all templated logic and business rules |

---

**End of Document**
