# UAT-Users Transformation Architecture

## Design Principle

The `uat-users` verb has **two operational modes** depending on whether it runs standalone or integrated with `full-export`:

### **Mode 1: Standalone UAT-Users (Post-Load Transformation)**
**Use case**: Migrate existing database already loaded with QA data

When run standalone, `uat-users` emits **UPDATE scripts** that transform user FK values after data is loaded:
```bash
dotnet run --project src/Osm.Cli -- uat-users \
  --model ./_artifacts/model.json \
  --connection-string "Server=uat;Database=UAT;..." \
  --uat-user-inventory ./uat_users.csv \
  --qa-user-inventory ./qa_users.csv
```

**Output**: `02_apply_user_remap.sql` with UPDATE statements
```sql
-- Transforms data already in the database
UPDATE dbo.Order SET CreatedBy =
  CASE CreatedBy
    WHEN 999 THEN 200  -- QA orphan → UAT target
    WHEN 111 THEN 201
    ELSE CreatedBy
  END
WHERE CreatedBy IN (999, 111) AND CreatedBy IS NOT NULL;
```

**Workflow**:
1. QA data already exists in UAT database
2. Run `02_apply_user_remap.sql` to fix user IDs in-place
3. Idempotent; can rerun safely

---

### **Mode 2: Full-Export Integration (Pre-Transformed INSERT Scripts)**
**Use case**: Generate UAT-ready data set from QA source

When integrated with `full-export --enable-uat-users`, the pipeline emits **pre-transformed INSERT scripts** where user FK values are already mapped to UAT targets:

```bash
dotnet run --project src/Osm.Cli -- full-export \
  --mock-advanced-sql tests/Fixtures/extraction/advanced-sql.manifest.json \
  --profile-out ./out/profiles \
  --build-out ./out/uat-export \
  --enable-uat-users \
  --uat-user-inventory ./uat_users.csv \
  --qa-user-inventory ./qa_users.csv \
  --user-map ./uat_user_map.csv
```

**Output**: Dynamic INSERT scripts in `DynamicData/` with **pre-transformed user IDs**

```sql
-- Order.dynamic.sql: User IDs already mapped during generation
INSERT INTO dbo.Order (Id, ProductId, CreatedBy, Amount)
VALUES
  (1, 100, 200, 50.00),  -- CreatedBy: 999 → 200 (transformed)
  (2, 101, 201, 75.00);  -- CreatedBy: 111 → 201 (transformed)
```

**Workflow**:
1. Full-export generates DDL + static seeds (unchanged)
2. UAT-users pipeline discovers orphans and validates mapping
3. Dynamic INSERT generator **applies transformation in-memory** during script emission
4. Emitted INSERT scripts contain UAT-ready data
5. Load scripts directly to UAT (no post-processing needed)

---

## Transformation Implementation

### Discovery & Validation (Shared by Both Modes)
1. **FK Catalog Discovery**: Identify all columns referencing `dbo.[User](Id)`
2. **Orphan Detection**: Query QA database for user IDs not in UAT inventory
3. **Mapping Validation**: Ensure every orphan has UAT target; verify targets exist
4. **Proof Artifact Emission**: Generate `uat-users-orphan-discovery.json`, `uat-users-validation-report.json`

### Script Generation (Mode-Specific)

#### **Standalone Mode: UPDATE Script Emission**
```csharp
// SqlScriptEmitter generates UPDATE statements
foreach (var column in fkCatalog) {
    var updates = orphanMap
        .Where(m => m.TargetUserId != null)
        .Select(m => $"WHEN {m.SourceUserId} THEN {m.TargetUserId}");

    emit($"UPDATE {column.Table} SET {column.Name} = CASE {column.Name} {updates} END");
}
```

#### **Full-Export Integration: INSERT Script Transformation**
```csharp
// DynamicEntityInsertGenerator applies transformation during row emission
foreach (var row in entityData) {
    foreach (var column in row.Columns.Where(c => IsUserFK(c))) {
        if (orphanMap.TryGetValue(column.Value, out var target)) {
            column.Value = target.TargetUserId;  // Transform in-memory
        }
    }
    emitInsertStatement(row);  // Emit with transformed values
}
```

**Key difference**: Transformation happens **during script generation**, not as a separate post-deployment step.

---

## Verification Requirements (Both Modes)

Regardless of mode, the verification framework must prove:

1. **Mapping Completeness**: Every orphan has a UAT target
2. **In-Scope Guarantee**: All targets exist in UAT inventory
3. **NULL Preservation**: NULL user IDs remain NULL (never transformed)
4. **Lossless Transformation**: No orphans created; all transformations reversible

### **Mode 1 Verification (UPDATE Scripts)**
- Parse `02_apply_user_remap.sql`
- Extract `WHERE ... IN (...)` clauses → verify against orphan set
- Extract `CASE ... WHEN ... THEN ...` blocks → verify against UAT inventory
- Assert `WHERE ... IS NOT NULL` guards present

### **Mode 2 Verification (INSERT Scripts)**
- Parse emitted `DynamicData/**/*.dynamic.sql` files
- Extract all INSERT VALUES containing user FK columns
- Verify no orphan IDs appear in emitted data (all transformed or filtered)
- Verify all user FK values exist in UAT inventory
- Compare row counts: QA source vs. UAT-ready output (should match; no data loss)

---

## Full-Export Artifact Contract Extension

When `--enable-uat-users` is supplied to `full-export`, the manifest tracks transformation metadata:

```json
{
  "Metadata": {
    "uatUsers.enabled": true,
    "uatUsers.transformationMode": "pre-transformed-inserts",
    "uatUsers.orphanCount": 15,
    "uatUsers.mappedCount": 15,
    "uatUsers.fkCatalogSize": 23,
    "uatUsers.qaInventoryPath": "/path/to/qa_users.csv",
    "uatUsers.uatInventoryPath": "/path/to/uat_users.csv",
    "uatUsers.userMapPath": "/path/to/uat_user_map.csv",
    "uatUsers.validationReportPath": "/out/uat-users/uat-users-validation-report.json"
  },
  "Stages": [
    {
      "Name": "dynamic-insert",
      "Status": "Success",
      "Artifacts": {
        "transformationApplied": true,
        "scripts": [...],
        "mode": "PerEntity"
      }
    }
  ]
}
```

**Key addition**: `transformationApplied: true` signals that INSERT scripts contain pre-transformed data.

---

## Migration Path

### **Phase 1: Existing Databases** (Use Standalone Mode)
If UAT database already contains QA data:
```bash
# Generate transformation script only
uat-users --model ... --connection-string ... --out ./migration
# Apply to existing database
sqlcmd -S uat -d UAT -i ./migration/02_apply_user_remap.sql
```

### **Phase 2: Fresh Deployments** (Use Full-Export Integration)
For new UAT environment or full refresh:
```bash
# Generate UAT-ready data set
full-export --enable-uat-users ... --build-out ./uat-export
# Deploy DDL + static seeds via SSDT
# Load pre-transformed dynamic data
sqlcmd -S uat -d UAT -i ./uat-export/DynamicData/**/*.dynamic.sql
```

**No post-processing needed** - data is UAT-ready at generation time.

---

## Implementation Checklist

- [ ] **M2.1 Enhancement**: Extend verification framework to support both modes
  - Detect transformation mode from context (standalone vs. full-export integration)
  - Generate appropriate proof artifacts for each mode

- [ ] **M2.2 Mode Detection**: Update SQL verification to handle both UPDATE and INSERT scripts
  - Parse UPDATE scripts for standalone mode
  - Parse INSERT scripts for full-export mode
  - Common validation: orphan set coverage, UAT inventory compliance, NULL preservation

- [ ] **DynamicEntityInsertGenerator Integration**: Add transformation hook
  - Accept user mapping context during initialization
  - Apply transformations in-memory during row emission
  - Emit metadata in manifest indicating transformation applied

- [ ] **M3.1 Manifest Extension**: Add `transformationMode` and `transformationApplied` fields

- [ ] **M3.4 Documentation**: Update all references to clarify two-mode design
  - `docs/verbs/uat-users.md`: Explain standalone vs. integrated behavior
  - `docs/full-export-artifact-contract.md`: Document pre-transformed INSERT contract
  - `docs/incident-response-uat-users.md`: Cover troubleshooting for both modes

---

## Benefits of Pre-Transformed INSERT Approach

1. **Simpler deployment**: Load scripts directly; no post-processing step
2. **Faster UAT refresh**: Eliminates UPDATE overhead on large tables
3. **Reduced lock contention**: Bulk INSERT faster than UPDATE
4. **Idempotent by design**: Reload from INSERT scripts anytime
5. **Audit-friendly**: INSERT scripts are the source of truth
6. **Minimally-logged bulk operations**: Can use `TABLOCK` hints without constraint violations

---

*Last Updated: 2025-11-17*
