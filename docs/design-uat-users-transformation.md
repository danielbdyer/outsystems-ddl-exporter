# UAT-Users Transformation Architecture

## Design Principle: Single Transformation Logic, Multiple Application Modes

The `uat-users` transformation uses a **unified mapping function** applied at different stages depending on operational context:

```
Core Transformation: Map(QA_UserId) -> UAT_UserId
```

This mapping is derived from:
1. **Orphan Discovery**: QA user IDs not present in UAT inventory
2. **User Map**: Operator-supplied or auto-generated `SourceUserId → TargetUserId` pairs
3. **Validation**: Proof that every source exists in QA inventory and every target exists in UAT inventory

**The transformation logic is mode-agnostic.** The difference between modes is **when and where** the mapping is applied, not **what** mapping is applied.

---

## Recommended Approach: Pre-Transformed INSERT Generation

### **Primary Mode: Full-Export Integration (Pre-Transformed INSERTs)**

**Use this mode for all new UAT deployments and refreshes.**

When integrated with `full-export --enable-uat-users`, the pipeline applies the transformation **during INSERT script generation**, emitting UAT-ready data:

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

**Workflow**:
1. Full-export discovers schema and data from QA
2. UAT-users pipeline discovers orphans and validates mapping
3. **Dynamic INSERT generator applies transformation in-memory during emission**
4. Emitted `DynamicData/**/*.dynamic.sql` files contain UAT-ready data
5. Load scripts directly to UAT (no post-processing)

**Output**: Pre-transformed INSERT scripts
```sql
-- Order.dynamic.sql: User IDs already mapped during generation
INSERT INTO dbo.Order (Id, ProductId, CreatedBy, Amount)
VALUES
  (1, 100, 200, 50.00),  -- CreatedBy: 999 (QA orphan) → 200 (UAT target)
  (2, 101, 201, 75.00);  -- CreatedBy: 111 (QA orphan) → 201 (UAT target)
```

**Benefits**:
- **Simpler deployment**: Load scripts directly; no separate transformation step
- **Faster UAT refresh**: Eliminates UPDATE overhead on large tables
- **Reduced lock contention**: Bulk INSERT with `TABLOCK` hints
- **Idempotent by design**: Reload from source INSERT scripts anytime
- **Audit-friendly**: INSERT scripts are the immutable source of truth
- **Minimally-logged operations**: Bulk INSERT faster than row-by-row UPDATE

---

## Verification/Fallback Mode: Standalone UPDATE Script Generation

### **Secondary Mode: Standalone (Post-Load Transformation)**

**Use this mode for verification or migrating existing UAT databases already loaded with QA data.**

When run independently, `uat-users` emits **UPDATE scripts** that can transform data in-place OR serve as verification artifacts:

```bash
dotnet run --project src/Osm.Cli -- uat-users \
  --model ./_artifacts/model.json \
  --connection-string "Server=uat;Database=UAT;..." \
  --uat-user-inventory ./uat_users.csv \
  --qa-user-inventory ./qa_users.csv \
  --out ./uat-users-artifacts
```

**Output**: UPDATE script (can be applied or analyzed)
```sql
-- 02_apply_user_remap.sql: Transforms data already in database
UPDATE dbo.Order SET CreatedBy =
  CASE CreatedBy
    WHEN 999 THEN 200  -- QA orphan → UAT target
    WHEN 111 THEN 201
    ELSE CreatedBy
  END
WHERE CreatedBy IN (999, 111) AND CreatedBy IS NOT NULL;
```

**Use Cases**:
1. **Verification**: Generate UPDATE script to analyze transformations before applying pre-transformed INSERTs
2. **Legacy migration**: Transform existing UAT database already loaded with QA data
3. **Proof artifact**: Document what transformations will occur for audit/compliance
4. **Dry-run validation**: Confirm mapping correctness without deploying full export

**Workflow for Legacy Migration**:
1. QA data already exists in UAT database (loaded via previous process)
2. Run standalone `uat-users` to generate UPDATE script
3. Review `01_preview.csv` and `02_apply_user_remap.sql`
4. Apply UPDATE script to transform data in-place
5. Script is idempotent; can rerun safely

**Workflow for Verification**:
1. Run `full-export --enable-uat-users` to generate pre-transformed INSERTs
2. **Also run standalone `uat-users` to generate UPDATE script for comparison**
3. Verify UPDATE script transformations match INSERT script transformations
4. Provides independent proof that transformation logic is consistent
5. UPDATE script serves as verification artifact (not applied)

---

## Unified Transformation Implementation

Both modes use the **same transformation logic**:

### Discovery & Validation (Shared by Both Modes)
```csharp
// 1. Discover FK catalog
var fkCatalog = ModelUserSchemaGraphFactory.Create(model);

// 2. Query QA database for distinct user IDs per column
var orphans = DiscoverOrphans(qaInventory, uatInventory, fkCatalog);

// 3. Validate user map
ValidateUserMap(orphans, userMap, qaInventory, uatInventory);

// 4. Build transformation map
var transformationMap = userMap.ToDictionary(
    m => m.SourceUserId,
    m => m.TargetUserId
);
```

### Application (Mode-Specific)

#### **Primary: In-Memory Transformation During INSERT Generation**
```csharp
// DynamicEntityInsertGenerator with transformation context
public class DynamicEntityInsertGenerator {
    private readonly IReadOnlyDictionary<UserIdentifier, UserIdentifier> _transformationMap;
    private readonly HashSet<string> _userFkColumns;

    public void EmitInsert(EntityRow row) {
        foreach (var column in row.Columns) {
            // Apply transformation if column is a user FK
            if (_userFkColumns.Contains(column.Name) &&
                column.Value != null &&
                _transformationMap.TryGetValue(column.Value, out var target)) {
                column.Value = target;  // Transform in-memory
            }
        }
        EmitInsertStatement(row);  // Emit with transformed values
    }
}
```

**Key**: Transformation happens during row emission, not as a separate SQL step.

#### **Secondary: SQL-Based Transformation (Verification/Fallback)**
```csharp
// SqlScriptEmitter generates UPDATE statements from same transformation map
public class SqlScriptEmitter {
    private readonly IReadOnlyDictionary<UserIdentifier, UserIdentifier> _transformationMap;

    public void EmitUpdateScript(FkColumn column) {
        var cases = _transformationMap
            .Select(kvp => $"WHEN {kvp.Key} THEN {kvp.Value}")
            .ToList();

        var inClause = string.Join(", ", _transformationMap.Keys);

        emit($@"
UPDATE {column.Table}
SET {column.Name} = CASE {column.Name}
    {string.Join("\n    ", cases)}
    ELSE {column.Name}
END
WHERE {column.Name} IN ({inClause})
  AND {column.Name} IS NOT NULL;
");
    }
}
```

**Key**: Same `_transformationMap` used; different application mechanism.

---

## Decision Tree: Which Mode Should I Use?

```
┌─────────────────────────────────────────┐
│ Do you need to deploy to UAT?          │
└────────────┬────────────────────────────┘
             │
             ├─ YES ──► Is UAT database empty or being refreshed?
             │          │
             │          ├─ YES ──► ✅ Use Full-Export Integration (Recommended)
             │          │          Generate pre-transformed INSERT scripts
             │          │          Load directly to UAT
             │          │
             │          └─ NO ───► UAT already contains QA data?
             │                     │
             │                     ├─ YES ──► Use Standalone Mode (Legacy Migration)
             │                     │          Generate & apply UPDATE scripts
             │                     │
             │                     └─ NO ───► Drop existing data; use Full-Export Integration
             │
             └─ NO ──► Just want to verify transformations?
                       │
                       └─ YES ──► Use Standalone Mode (Verification Only)
                                  Generate UPDATE scripts as proof artifacts
                                  Do NOT apply; use for analysis/audit
```

**Recommendation**: Always prefer **Full-Export Integration** unless you have an existing UAT database you cannot drop.

---

## Verification Strategy: Dual Proof Mechanism

For high-assurance scenarios, generate artifacts in **both modes** for cross-validation:

### Verification Workflow
```bash
# 1. Generate pre-transformed INSERTs (primary deployment artifact)
full-export --enable-uat-users \
  --build-out ./uat-export \
  --uat-user-inventory ./uat_users.csv \
  --qa-user-inventory ./qa_users.csv \
  --user-map ./uat_user_map.csv

# 2. Generate UPDATE script (verification artifact)
uat-users \
  --model ./uat-export/model.json \
  --connection-string "Server=qa;..." \
  --uat-user-inventory ./uat_users.csv \
  --qa-user-inventory ./qa_users.csv \
  --user-map ./uat_user_map.csv \
  --out ./uat-users-verification
```

### Cross-Validation Checks
1. **Transformation count match**: UPDATE script `WHERE ... IN (...)` clause should contain same count as orphans discovered
2. **User ID coverage**: All user IDs in UPDATE script `CASE` blocks should appear in INSERT scripts
3. **NULL preservation**: Both modes should preserve NULLs (UPDATE has `WHERE ... IS NOT NULL`, INSERTs skip NULLs)
4. **Target compliance**: Both modes map to same UAT inventory targets

The UPDATE script serves as an **independent proof** that transformation logic is correct, even if you never apply it.

---

## Full-Export Artifact Contract Extension

When `full-export` runs with `--enable-uat-users`, the manifest tracks transformation metadata:

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
    "uatUsers.validationReportPath": "/out/uat-users-verification.json"
  },
  "Stages": [
    {
      "Name": "dynamic-insert",
      "Status": "Success",
      "Artifacts": {
        "transformationApplied": true,
        "mode": "PerEntity",
        "scripts": [...]
      }
    }
  ]
}
```

**Key fields**:
- `transformationMode`: Always `pre-transformed-inserts` for full-export integration
- `transformationApplied: true`: Signals INSERT scripts contain UAT-ready data

Standalone mode would use `transformationMode: post-load-updates` (but this is rare in practice).

---

## Implementation Checklist

### Core Transformation Logic (Mode-Agnostic)
- [ ] User inventory loaders (QA + UAT CSV parsing)
- [ ] FK catalog discovery from model
- [ ] Orphan detection (QA users not in UAT inventory)
- [ ] User map validation (sources in QA, targets in UAT, no duplicates)
- [ ] Transformation map builder (`SourceUserId → TargetUserId`)
- [ ] Verification report generation (orphan discovery, validation results)

### Primary Mode: Full-Export Integration
- [ ] `DynamicEntityInsertGenerator` transformation hook
- [ ] Accept user FK column catalog during initialization
- [ ] Accept transformation map during initialization
- [ ] Apply transformation in-memory during row emission
- [ ] Preserve NULLs (skip transformation for NULL values)
- [ ] Emit metadata in manifest (`transformationApplied: true`)
- [ ] Verification: Parse INSERT scripts, prove all user IDs in UAT inventory

### Secondary Mode: Standalone UPDATE Script Generation
- [ ] `SqlScriptEmitter` UPDATE script generator
- [ ] Build CASE blocks from transformation map
- [ ] Generate WHERE clauses with orphan IN lists
- [ ] Add `WHERE ... IS NOT NULL` guards
- [ ] Emit idempotent, rerunnable UPDATE scripts
- [ ] Verification: Parse UPDATE scripts, prove transformations match map

### Dual-Mode Verification
- [ ] Generate artifacts in both modes for cross-validation
- [ ] Compare transformation counts (UPDATE vs INSERT)
- [ ] Verify user ID coverage matches between modes
- [ ] Prove NULL preservation in both modes
- [ ] Emit cross-validation report

---

## Migration Path

### **New UAT Deployments** (Recommended)
Use full-export integration exclusively:

```bash
# Single command generates UAT-ready artifacts
full-export --enable-uat-users \
  --build-out ./uat-export \
  --uat-user-inventory ./uat_users.csv \
  --qa-user-inventory ./qa_users.csv \
  --user-map ./uat_user_map.csv

# Deploy to UAT
sqlcmd -S uat -d UAT -i ./uat-export/SafeScript.sql
# Load static seeds via SSDT post-deployment
# Load pre-transformed dynamic data
for file in ./uat-export/DynamicData/**/*.dynamic.sql; do
  sqlcmd -S uat -d UAT -i "$file"
done
```

**No UPDATE scripts needed.** Data is UAT-ready at generation time.

### **Existing UAT Databases** (Legacy Migration)
Use standalone mode to transform in-place:

```bash
# Generate UPDATE script
uat-users \
  --model ./model.json \
  --connection-string "Server=uat;Database=UAT;..." \
  --uat-user-inventory ./uat_users.csv \
  --qa-user-inventory ./qa_users.csv \
  --out ./uat-users-migration

# Review preview
cat ./uat-users-migration/01_preview.csv

# Apply transformation
sqlcmd -S uat -d UAT -i ./uat-users-migration/02_apply_user_remap.sql
```

**After first migration**: Switch to full-export integration for future refreshes.

---

## Why Pre-Transformed INSERTs Are Superior

| Aspect | Pre-Transformed INSERTs (Mode 2) | Post-Load UPDATEs (Mode 1) |
|--------|----------------------------------|----------------------------|
| **Deployment complexity** | Load scripts directly | Load data, then run UPDATE scripts |
| **Performance** | Bulk INSERT with TABLOCK | Row-by-row UPDATE (slower) |
| **Lock contention** | Minimal (table locks) | Row/page locks (higher contention) |
| **Transaction log** | Minimally logged (bulk) | Fully logged (row updates) |
| **Idempotence** | Drop/reload anytime | Requires guards (`CASE ELSE`) |
| **Audit trail** | INSERT scripts are source of truth | Requires UPDATE script + before state |
| **Rollback** | Drop and reload original | Complex (inverse UPDATE script) |
| **Verification** | Parse INSERT scripts | Parse UPDATE scripts + replay |
| **Deployment risk** | Low (fresh load) | Medium (in-place transformation) |

**Recommendation**: Use Mode 2 (pre-transformed INSERTs) for all new deployments. Use Mode 1 only for verification or legacy migration.

---

## End-to-End Data Integrity Verification (DMM Replacement)

A **critical goal** of the full-export pipeline is to provide **unfailing confidence** that the ETL process is correct, enabling replacement of expensive third-party tools (like DMM) with a verifiable, auditable pipeline.

### Verification Strategy: Source-to-Target Parity

The pipeline implements comprehensive data integrity verification proving:
1. **No data loss**: All rows exported from source appear in target
2. **1:1 data fidelity**: Non-transformed columns match source exactly (byte-for-byte)
3. **Correct transformations**: User FK values map correctly per transformation map
4. **NULL preservation**: NULL values remain NULL (never transformed or lost)

### Implementation: Data Fingerprinting

#### **Source Fingerprint Capture**
Before generating INSERT scripts, capture source database metrics:
```json
{
  "tables": [
    {
      "schema": "dbo",
      "table": "Order",
      "rowCount": 1500,
      "columns": [
        {
          "name": "Id",
          "nullCount": 0,
          "checksum": "ABC123...",
          "distinctCount": 1500
        },
        {
          "name": "ProductId",
          "nullCount": 0,
          "checksum": "DEF456...",
          "distinctCount": 50
        },
        {
          "name": "CreatedBy",
          "isUserFK": true,
          "nullCount": 10,
          "distinctCount": 120,
          "orphanCount": 15,
          "orphanIds": [999, 111, ...]
        },
        {
          "name": "Amount",
          "nullCount": 5,
          "checksum": "GHI789..."
        }
      ]
    }
  ]
}
```

#### **Target Validation**
After loading INSERT scripts to target (UAT staging):
```sql
-- Verify row counts
SELECT COUNT(*) FROM dbo.Order;  -- Must equal source: 1500

-- Verify non-transformed columns (checksum comparison)
SELECT CHECKSUM_AGG(CHECKSUM(Id, ProductId, Amount))
FROM dbo.Order;  -- Must equal source checksum

-- Verify NULL preservation
SELECT
  SUM(CASE WHEN CreatedBy IS NULL THEN 1 ELSE 0 END) AS NullCount
FROM dbo.Order;  -- Must equal source: 10

-- Verify transformed column (all values in UAT inventory)
SELECT DISTINCT CreatedBy
FROM dbo.Order
WHERE CreatedBy IS NOT NULL
  AND CreatedBy NOT IN (SELECT Id FROM dbo.[User]);  -- Must return 0 rows
```

#### **Verification Report**
```json
{
  "overallStatus": "PASS",
  "verificationTimestamp": "2025-11-17T10:30:00Z",
  "sourceFingerprint": "source-data-fingerprint.json",
  "tables": [
    {
      "table": "dbo.Order",
      "rowCountMatch": true,
      "sourceRowCount": 1500,
      "targetRowCount": 1500,
      "columns": [
        {
          "name": "Id",
          "isTransformed": false,
          "checksumMatch": true,
          "nullCountMatch": true,
          "status": "PASS"
        },
        {
          "name": "ProductId",
          "isTransformed": false,
          "checksumMatch": true,
          "nullCountMatch": true,
          "status": "PASS"
        },
        {
          "name": "CreatedBy",
          "isTransformed": true,
          "transformationType": "user-fk-remap",
          "nullCountMatch": true,
          "allValuesInUATInventory": true,
          "orphansTransformed": 15,
          "orphansRemaining": 0,
          "status": "PASS"
        },
        {
          "name": "Amount",
          "isTransformed": false,
          "checksumMatch": true,
          "nullCountMatch": true,
          "status": "PASS"
        }
      ],
      "status": "PASS"
    }
  ],
  "discrepancies": [],
  "summary": {
    "tablesVerified": 50,
    "tablesPassed": 50,
    "tablesFailed": 0,
    "columnsVerified": 500,
    "columnsPassed": 500,
    "columnsFailed": 0,
    "transformedColumns": 23,
    "dataLossDetected": false
  }
}
```

### Benefits: Replacing DMM with Confidence

| Aspect | DMM (Current) | Full-Export with Verification |
|--------|---------------|-------------------------------|
| **Data integrity proof** | Manual validation required | Automated verification report |
| **Transformation validation** | Hope and pray | Provable correctness (checksum + map validation) |
| **Audit trail** | Limited visibility | Complete fingerprint + verification report |
| **Cost** | Expensive subscription | Open-source tooling |
| **Developer experience** | "Nightmare" (per user feedback) | Deterministic, repeatable, verifiable |
| **NULL preservation** | Not guaranteed | Proven with NULL count comparison |
| **Data loss detection** | Manual row count checks | Automated row count + checksum verification |
| **Rollback capability** | Complex | Drop and reload from source INSERT scripts |

### Integration with Load Harness

The `FullExportLoadHarness` tool implements this verification:

```bash
dotnet run --project tools/FullExportLoadHarness \
  --source-connection "Server=qa;Database=QA;..." \
  --target-connection "Server=uat-staging;Database=UAT;..." \
  --manifest ./out/full-export/full-export.manifest.json \
  --uat-user-inventory ./uat_users.csv \
  --verification-report-out ./verification-report.json
```

**Workflow**:
1. Connect to source (QA) and capture fingerprint
2. Load DDL scripts to target (UAT staging)
3. Load static seeds via SSDT post-deployment
4. Load pre-transformed dynamic INSERTs
5. Extract target metrics
6. Compare source vs. target
7. Emit verification report with pass/fail

**If verification passes**: Deploy to production UAT with confidence

**If verification fails**: Report shows exact discrepancies (table, column, row count, checksum mismatch) for remediation

### Verification as a Quality Gate

Recommended CI/CD integration:

```yaml
# GitHub Actions example
- name: Run Full Export
  run: |
    dotnet run --project src/Osm.Cli -- full-export \
      --enable-uat-users \
      --build-out ./out/uat-export \
      ...

- name: Verify Data Integrity
  run: |
    dotnet run --project tools/FullExportLoadHarness \
      --source-connection "$QA_CONNECTION" \
      --target-connection "$UAT_STAGING_CONNECTION" \
      --manifest ./out/uat-export/full-export.manifest.json \
      --verification-report-out ./verification-report.json

- name: Check Verification Status
  run: |
    STATUS=$(jq -r '.overallStatus' ./verification-report.json)
    if [ "$STATUS" != "PASS" ]; then
      echo "❌ Data integrity verification FAILED"
      jq '.discrepancies' ./verification-report.json
      exit 1
    fi
    echo "✅ Data integrity verification PASSED"

- name: Deploy to Production UAT
  if: success()
  run: |
    # Deploy verified artifacts to production UAT
```

### Future Enhancement: Incremental Verification

For large datasets, implement **incremental verification**:
- Verify random sample (e.g., 10% of rows)
- Checksum by partition (e.g., per day/month)
- Verify critical tables exhaustively
- Verify non-critical tables by sampling

This enables verification at scale without exhaustive comparison overhead.

---

*Last Updated: 2025-11-17 (added end-to-end data integrity verification for DMM replacement)*
