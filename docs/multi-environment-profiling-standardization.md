# Multi-Environment Profiling Standardization Guide

## Overview

The multi-environment profiling system enables you to profile multiple database environments (Production, QA, Staging, Development) simultaneously and determine which constraints can be safely applied across **all** environments. This document explains the standardization approach and how it helps achieve constraint readiness.

## Core Problem

When deploying database schema changes across multiple environments, you face:

1. **Schema Drift**: Tables may be missing or renamed in some environments
2. **Data Quality Variance**: Production might have 1000 NULL values while QA has 1500
3. **Constraint Violations**: Some environments have duplicate values, others don't
4. **Orphaned References**: Foreign keys may point to missing records in different environments

Applying constraints blindly can break deployments. The multi-profiler solves this by **standardizing disparate data sets** to find a safe baseline.

## Architecture Goals

### Primary Goal: Constraint Readiness Across All Environments

The system analyzes profiling data from all surveyed environments and answers:
> "Which constraints can I safely apply to **every** environment without breaking any of them?"

### Secondary Goals

1. **Worst-Case Aggregation**: Use the most conservative data quality metrics
2. **Drift Detection**: Identify where environments diverge
3. **Consensus Analysis**: Calculate percentage of environments where constraints are safe
4. **Actionable Recommendations**: Provide specific remediation steps

## How It Works

### 1. Environment Classification

#### Primary Environment (Strict Mode)
- **Purpose**: Production or authoritative source
- **Behavior**: Fails fast if tables are missing
- **Configuration**: `AllowMissingTables = false`
- **Use Case**: Ensures complete profiling of production schema

#### Secondary Environments (Lenient Mode)
- **Purpose**: QA, Staging, Development environments
- **Behavior**: Gracefully skips missing tables
- **Configuration**: `AllowMissingTables = true`
- **Use Case**: Handles schema drift without failing

The secondary connections act as the "reality check" for constraint readiness. Their job is to highlight how far non-production
data sets have drifted from the primary environment and to document the remediation steps required to realign them. Every
variance discovered in secondary environments is projected into the consolidated multi-environment report so that remediation
teams can standardize naming, null-handling, and relational integrity before enabling hardening policies. Think of the
secondary database list as the sandbox that tells you **why** a constraint cannot be promoted today and exactly which columns or
tables require cleanup.

### 2. Data Aggregation Strategy

The system uses **worst-case aggregation** to ensure constraint safety:

| Metric | Aggregation Strategy | Rationale |
|--------|---------------------|-----------|
| **NULL Count** | MAX across environments | If ANY environment has NULLs, NOT NULL would fail |
| **Duplicate Detection** | OR logic (HasDuplicate) | If ANY environment has duplicates, UNIQUE would fail |
| **Foreign Key Orphans** | OR logic (HasOrphan) | If ANY environment has orphans, FK would fail |
| **Probe Status** | Worst outcome | Reflects least reliable profiling evidence |

#### Example: NULL Count Aggregation

```
Environment     | CustomerId NULL Count
----------------|----------------------
Production      | 0 (safe)
QA              | 1500 (unsafe)
Development     | 250 (unsafe)
----------------|----------------------
MERGED RESULT   | 1500 (use max - conservative)
```

**Outcome**: System correctly identifies that NOT NULL constraint would fail in QA, preventing broken deployment.

### 3. Constraint Consensus Analysis

The `MultiEnvironmentConstraintConsensus` analyzer determines which constraints have cross-environment agreement:

```csharp
var consensus = MultiEnvironmentConstraintConsensus.Analyze(
    snapshots,
    minimumConsensusThreshold: 1.0  // Require 100% agreement
);
```

#### Consensus Results

For each constraint, the system calculates:

- **Safe Environment Count**: Environments where constraint would succeed
- **Total Environment Count**: Total environments surveyed
- **Consensus Ratio**: `SafeCount / TotalCount`
- **Is Safe To Apply**: `ConsensusRatio >= Threshold`
- **Recommendation**: Actionable next steps

#### Example: Unique Constraint Consensus

```
Constraint: dbo.Customer.Email UNIQUE
- Total Environments: 3
- Safe Environments: 2 (Production, QA)
- Unsafe Environments: 1 (Development has duplicates)
- Consensus Ratio: 66.7%
- Is Safe To Apply: NO (threshold = 100%)
- Recommendation: "UNIQUE constraint would fail. Environments with duplicates: Development"
```

### 4. Schema Drift Handling

#### Table Name Mappings

Handle renamed tables across environments:

```json
{
  "tableNameMappings": [
    {
      "sourceSchema": "dbo",
      "sourceTable": "User",
      "targetSchema": "dbo",
      "targetTable": "Users"  // Pluralized in QA
    }
  ]
}
```

The profiler:
1. Checks if `dbo.User` exists in metadata
2. If missing, tries mapping to `dbo.Users`
3. Logs the mapping for diagnostics
4. Proceeds with profiling

#### Missing Table Handling

**Primary Environment (Strict)**:
```sql
-- Missing table causes failure
ERROR: Table 'dbo.Customer' not found in database
```

**Secondary Environment (Lenient)**:
```sql
-- Missing table is gracefully skipped
INFO: Skipping table 'dbo.Customer' - not found in metadata
```

Every skipped table is now surfaced in the multi-environment validation findings (code:
`profiling.validation.schema.tableMissing`). The accompanying remediation guidance inside the CLI report points the operator to
either synchronize the missing table or supply a `tableNameMappings` entry so profiling data can be normalized across
environments.

## Standardization Features

### 1. Case-Insensitive Comparison

All identifiers (schemas, tables, columns) use **OrdinalIgnoreCase** comparison:

- `dbo.Customer.EMAIL` == `dbo.customer.email`
- `DBO.CUSTOMER.EMAIL` == `dbo.Customer.Email`

This ensures consistent matching across environments with different casing conventions.

### 2. Normalized Key Generation

Composite unique keys and foreign keys use pipe-delimited normalization:

```csharp
// Unique key on (Email, Phone)
BuildUniqueKey(["Email", "Phone"]) => "Email|Phone"

// Case-insensitive comparison handles variations
"Email|Phone" == "email|phone" == "EMAIL|PHONE"
```

### 3. Environment Label Standardization

Environment labels are normalized to prevent duplicates:

```csharp
// User provides: "QA", "qa", "Qa"
// System allocates: "QA", "QA #2", "QA #3"

// Canonical casing (first-seen) is preserved:
// "Production", "production" => "Production", "Production #2"
```

### 4. Probe Status Aggregation

Profiling probes may timeout or be cancelled. The system aggregates statuses conservatively:

```
Priority: Cancelled > Timeout > Succeeded

Environment A: Succeeded
Environment B: Timeout
Merged Result: Timeout (worst case)
```

## Configuration

### Basic Multi-Environment Setup

```json
{
  "sql": {
    "connectionString": "Production::Server=prod;Database=MainDB;",
    "profilingConnectionStrings": [
      "QA::Server=qa;Database=MainDB;",
      "Development::Server=dev;Database=MainDB;"
    ]
  }
}
```

### Advanced Configuration

```json
{
  "sql": {
    "connectionString": "Server=prod;Database=MainDB;",
    "profilingConnectionStrings": [
      "Server=qa;Database=MainDB;",
      "Server=dev;Database=MainDB;"
    ],
    "tableNameMappings": [
      {
        "sourceSchema": "dbo",
        "sourceTable": "User",
        "targetSchema": "dbo",
        "targetTable": "Users"
      }
    ],
    "allowMissingTables": true,
    "commandTimeoutSeconds": 300,
    "sampling": {
      "rowSamplingThreshold": 100000,
      "sampleSize": 10000
    }
  }
}
```

## Output: Multi-Environment Profile Report

### Environment Summaries

```
Environment: Production (Primary)
- Duration: 0:02:15
- Columns: 450
- Columns with NULLs: 12
- Unique Violations: 0
- FK Orphans: 0

Environment: QA
- Duration: 0:02:20
- Columns: 445 (5 missing due to schema drift)
- Columns with NULLs: 45 (33 more than Production)
- Unique Violations: 3
- FK Orphans: 2
```

### Drift Findings

```
[WARNING] QA: elevated null counts
- 45 columns reported null values compared to 12 in Production
- Affected: dbo.Customer.Email (+250 NULLs; primary 0)
            dbo.Customer.Phone (+180 NULLs; primary 0)

[CRITICAL] QA: orphaned foreign keys
- 2 orphaned foreign key references detected while Production reports 0
- Affected: dbo.Order.CustomerId -> dbo.Customer.Id (FK)
```

### Constraint Consensus

```
Consensus Analysis: 3 environments surveyed
- NOT NULL Constraints: 438/450 safe (97.3%)
- UNIQUE Constraints: 42/45 safe (93.3%)
- FOREIGN KEY Constraints: 28/30 safe (93.3%)
- Overall Safety: 508/525 constraints ready (96.8%)

Unsafe Constraints Requiring Remediation:
1. dbo.Customer.Email NOT NULL - QA has 250 NULLs
2. dbo.Customer.Email UNIQUE - Development has duplicates
3. dbo.Order.CustomerId FK -> dbo.Customer.Id - QA has 2 orphans
```

## Best Practices

### 1. Always Profile Production First

Designate production as the primary environment to establish a strict baseline:

```json
{
  "connectionString": "Production::Server=prod;Database=MainDB;"
}
```

### 2. Use 100% Consensus Threshold for Critical Systems

For high-availability systems, require unanimous consensus:

```csharp
var consensus = MultiEnvironmentConstraintConsensus.Analyze(
    snapshots,
    minimumConsensusThreshold: 1.0  // 100% agreement required
);
```

### 3. Remediate Before Deployment

Use the multi-environment report to fix data quality issues **before** applying constraints:

1. Review findings for elevated NULL counts
2. Fix duplicates in environments that violate uniqueness
3. Repair orphaned foreign key references
4. Re-run profiling to verify fixes

### 4. Map Renamed Tables

Document table renames and add mappings for smooth profiling:

```json
{
  "tableNameMappings": [
    {
      "sourceSchema": "dbo",
      "sourceTable": "OldTableName",
      "targetSchema": "dbo",
      "targetTable": "NewTableName"
    }
  ]
}
```

### 5. Monitor Profiling Duration

If secondary environments take significantly longer:

```
[ADVISORY] QA: slower profiling
- Profiling took 0:05:30 versus 0:02:15 in Production
- Suggested Action: Review connection locality or apply sampling overrides
```

Consider:
- Adjusting sampling thresholds
- Increasing command timeouts
- Checking network latency

## Troubleshooting

### Issue: "Table not found" in Secondary Environment

**Cause**: Schema drift - table doesn't exist in that environment

**Solution**: This is expected behavior with `AllowMissingTables = true`. The table will be skipped. Check logs:

```
[INFO] profiling.table.skipped: Skipping table 'dbo.TemporaryTable' - not found in metadata
```

### Issue: Different NULL Counts Across Environments

**Cause**: Data quality variance - normal in multi-environment setups

**Solution**: The merged snapshot uses the **maximum** NULL count. Review advisory findings that document variance without
blocking remediation:

```
[ADVISORY] profiling.validation.dataQuality.nullVariance: dbo.Customer.Email
- Suggested Action: Monitor null-handling differences across environments when planning NOT NULL enforcement.
```

### Issue: Consensus Shows 0% Agreement

**Cause**: Significant data quality or schema divergence

**Solution**:
1. Review individual environment snapshots
2. Identify root cause (schema drift vs. data quality)
3. Standardize schemas across environments
4. Fix data quality issues in problematic environments
5. Re-run profiling

## Advanced: Custom Consensus Thresholds

For non-critical systems, you may accept lower consensus:

```csharp
// Allow constraints if 80% of environments agree
var consensus = MultiEnvironmentConstraintConsensus.Analyze(
    snapshots,
    minimumConsensusThreshold: 0.8
);

// Result: Constraint safe in 4/5 environments (80%) => APPROVED
```

**Use Cases**:
- Development/staging-only constraints
- Experimental features
- Gradual rollout strategies

**Warning**: Lower thresholds increase deployment risk. Always validate in production-like environments first.

## Summary

The multi-environment profiling standardization system:

1. ✅ **Aggregates** disparate data quality metrics using worst-case logic
2. ✅ **Detects** schema drift and data quality divergence
3. ✅ **Analyzes** constraint consensus across all environments
4. ✅ **Recommends** specific remediation actions
5. ✅ **Ensures** constraints are safe to apply everywhere

By standardizing profiling results across multiple environments, you achieve **true constraint readiness** - the confidence that your database schema changes will deploy successfully to all target environments.
