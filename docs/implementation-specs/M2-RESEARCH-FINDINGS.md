# M2.* Red Team Research Findings

**Date**: 2025-11-19
**Status**: RESEARCH COMPLETE - Ready for V2 Iteration

---

## Executive Summary

Completed systematic investigation of critical gaps identified in red team analysis. Findings reveal **major architectural discrepancies** between design documents and current implementation, but also identify **existing infrastructure** that simplifies M2.* implementation.

**Key Outcomes**:
1. 🔴 **CRITICAL**: INSERT transformation injection **does not exist** - design doc describes aspirational architecture
2. ✅ **GOOD NEWS**: SQL parser infrastructure already exists (`TSql150Parser`)
3. ✅ **GOOD NEWS**: Result<T> pattern established for non-throwing validation
4. ⚠️ **MODERATE**: No existing verification infrastructure (build from scratch)

**Recommendation**: Revise M2.2 to reflect current architecture (UPDATE-only verification), defer INSERT verification to future milestone when pre-transformed INSERTs are implemented.

---

## Finding 1: INSERT Transformation Injection - DOES NOT EXIST

### Investigation Process

1. **Searched for `--enable-uat-users` flag handling** → Found in `FullExportCommandFactory.cs` (validation only)
2. **Traced full-export pipeline flow** → `FullExportApplicationService.cs`, `FullExportCoordinator.cs`
3. **Discovered execution order**:
   ```
   Extract → Profile → Build (INSERTs) → Apply → UAT-users (UPDATEs)
   ```
4. **Searched `DynamicEntityInsertGenerator.cs` for transformation logic** → **ZERO MATCHES**
5. **Conclusion**: Transformations are NOT applied during INSERT generation

### Evidence

**File**: `src/Osm.Pipeline/Orchestration/FullExportCoordinator.cs` (lines 87-126)
```csharp
// Build runs FIRST (line 87):
var buildResult = await request.BuildAsync(extraction, profile, cancellationToken)
    .ConfigureAwait(false);

// UAT-users runs AFTER build completes (line 118):
if (request.RunUatUsersAsync is { } uatUsersAsync)
{
    var uatUsersResult = await uatUsersAsync(extraction, build, schemaGraph, cancellationToken)
        .ConfigureAwait(false);
}
```

**File**: `src/Osm.Emission/DynamicEntityInsertGenerator.cs`
- **810 lines total**
- **ZERO references to**: transformation, transform, uat, user map, remap
- **Conclusion**: INSERT generator is completely unaware of UAT-users

### Design vs Reality Discrepancy

**Design Document** (`docs/design-uat-users-transformation.md` lines 20-53):
> "Recommended Approach: Pre-Transformed INSERT Generation"
> "Dynamic INSERT generator applies transformation in-memory during emission"
> "Emitted `DynamicData/**/*.dynamic.sql` files contain UAT-ready data"

**Current Reality**:
- INSERTs generated with QA user IDs (no transformation)
- UAT-users generates UPDATE scripts only
- Mode 1 (pre-transformed INSERTs) is **aspirational**, not implemented
- Mode 2 (post-load UPDATEs) is **current implementation**

### Impact on M2.* Specs

**M2.2 - Transformation Verification**:
- **Current Spec Assumption**: "INSERT scripts contain transformed user FK values" (WRONG)
- **Reality**: No INSERT transformations exist to verify
- **Required Change**: Remove INSERT verification OR defer to future milestone
- **Keep**: UPDATE verification (this does exist and work)

**M2.3 - Integration Tests**:
- **Current Spec Assumption**: "Full-export integration tests verify INSERT transformations" (WRONG)
- **Reality**: Full-export integration only tests UPDATE script generation
- **Required Change**: Adjust test expectations to match current architecture

### Recommendation for V2

**Option A - Defer INSERT Verification** (RECOMMENDED):
1. Remove Phase 0 research from M2.2 (question answered: doesn't exist)
2. Rename M2.2 to "UPDATE Script Verification"
3. Scope M2.2 to only verify UPDATE scripts (SqlScriptEmitter output)
4. Add future milestone "M5.1: Pre-Transformed INSERT Implementation"
5. Add future milestone "M5.2: INSERT Transformation Verification"

**Option B - Include INSERT Infrastructure Build**:
1. Expand M2.2 to implement INSERT transformation injection (major scope increase)
2. Add M2.2 Phase 0: "Implement Transformation Injection" (3-5 days additional)
3. Then verify transformations
4. **Risk**: Significantly increases M2.2 effort from 3-4 days to 7-10 days

**Recommendation**: **Option A** - V2 specs should reflect current architecture, not aspirational

---

## Finding 2: SQL Parser Infrastructure - ALREADY EXISTS

### Investigation Process

1. **Searched for SQL parsing utilities** → Found `ScriptDomDmmLens.cs`
2. **Discovered existing infrastructure**:
   - `Microsoft.SqlServer.TransactSql.ScriptDom` NuGet package already referenced
   - `TSql150Parser` already in use for DMM comparison

### Evidence

**File**: `src/Osm.Dmm/ScriptDomDmmLens.cs` (line 25)
```csharp
var parser = new TSql150Parser(initialQuotedIdentifiers: true);
using var textReader = new StringReader(script);
var fragment = parser.Parse(textReader, out var errors);
```

**Pattern to Follow**:
```csharp
// Instead of regex (fragile, error-prone):
var regex = new Regex(@"INSERT INTO.*VALUES\s*\((.*?)\)", ...);

// Use ScriptDom (robust, production-ready):
var parser = new TSql150Parser(initialQuotedIdentifiers: true);
var fragment = parser.Parse(reader, out var errors);
var visitor = new InsertStatementVisitor();
fragment.Accept(visitor);
var insertStatements = visitor.InsertStatements;
```

### Impact on M2.* Specs

**M2.2 - SQL Safety Analyzer**:
- **Current Spec**: "Use regex or simple string matching for MVP"
- **Reality**: Full SQL parser already available
- **Required Change**: Update to use `TSql150Parser` instead of regex
- **Benefit**: More robust, handles all edge cases, same patterns as existing codebase

**M2.1 - SQL Safety Analyzer** (if UPDATE verification only):
- Can parse UPDATE scripts with ScriptDom
- Verify CASE blocks, WHERE clauses programmatically
- More reliable than regex

### Recommendation for V2

1. **Replace all regex SQL parsing references** with `TSql150Parser`
2. **Add code examples** using ScriptDom visitor pattern
3. **Reference existing pattern** in `ScriptDomDmmLens.cs`
4. **Remove "regex vs parser" decision point** - decision made: use ScriptDom

---

## Finding 3: Result<T> Pattern - ESTABLISHED

### Investigation Process

1. **Searched for Result<T> usage** in UAT-users code
2. **Found consistent pattern** in `UatUsersPipelineRunner.cs`

### Evidence

**Pattern**: Validation returns `Result<T>` instead of throwing

**File**: `src/Osm.Pipeline/UatUsers/UatUsersPipelineRunner.cs`
```csharp
// Success pattern (line 150):
return Result<UatUsersApplicationResult>.Success(new UatUsersApplicationResult(...));

// Failure pattern (line 57):
return Result<UatUsersApplicationResult>.Failure(ValidationError.Create(
    "pipeline.fullExport.uatUsers.outputDirectory.missing",
    "Build output directory is required to emit uat-users artifacts."));
```

### Impact on M2.* Specs

**M2.1 - TransformationMapVerifier Refactoring**:
- **Current Spec Gap**: No clear refactoring pattern for exception → result conversion
- **Reality**: Established Result<T> pattern exists
- **Required Change**: Add explicit refactoring example

**Before (ValidateUserMapStep - throws)**:
```csharp
if (errors.Count > 0) {
    throw new InvalidOperationException("User map validation failed.");
}
```

**After (TransformationMapVerifier - returns Result)**:
```csharp
if (errors.Count > 0) {
    return Result<UserMapVerificationResult>.Failure(
        ValidationError.Create(
            "verification.userMap.invalid",
            "User map validation failed.",
            errors.ToArray()));
}
return Result<UserMapVerificationResult>.Success(new UserMapVerificationResult(
    isValid: true,
    duplicateSources: ImmutableArray<UserIdentifier>.Empty,
    invalidTargets: ImmutableArray<UserIdentifier>.Empty));
```

### Recommendation for V2

1. **Add "Refactoring Pattern" section** to M2.1
2. **Show explicit before/after** code examples
3. **Reference Result<T> pattern** from UatUsersPipelineRunner
4. **Document ValidationError.Create** usage

---

## Finding 4: No Existing Verification Infrastructure

### Investigation Process

1. **Searched for existing verifiers** → No matches
2. **Searched for existing verification reports** → No matches
3. **Conclusion**: Verification infrastructure is greenfield

### Evidence

```bash
$ grep -r "class.*Verif" src/Osm.Pipeline/
# No matches

$ grep -r "VerificationReport" src/Osm.Pipeline/
# No matches
```

### Impact on M2.* Specs

**M2.1 - Verification Framework**:
- **Current Spec Assumption**: No existing infrastructure (CORRECT)
- **Reality**: Build from scratch (as spec describes)
- **No Changes Needed**: Spec is accurate

**M2.3 - Integration Tests**:
- **Current Spec Assumption**: Use M2.1/M2.2 verifiers as oracles (CORRECT)
- **Reality**: Verifiers will be new code (high test coverage needed)
- **No Changes Needed**: Spec is accurate

### Recommendation for V2

- **No changes needed** - specs correctly assume greenfield

---

## Additional Investigation: FK Catalog Discovery

### Question

How does FK catalog discovery work? Is there existing comparison logic?

### Evidence

**File**: `src/Osm.Pipeline/UatUsers/Steps/DiscoverUserFkCatalogStep.cs`
- Uses `ModelUserSchemaGraphFactory.Create()` to build catalog from model
- Returns `IReadOnlyList<UserFkColumn>` with schema, table, column, FK name

**Pattern**:
```csharp
var catalog = context.SchemaGraph.GetAllUserForeignKeys();
context.SetUserFkCatalog(catalog.ToList());
```

### Impact on M2.* Specs

**M2.1 - FK Catalog Completeness Verifier**:
- **Current Spec Gap**: No algorithm specified for comparison
- **Reality**: Discovery step exists, just need comparison logic
- **Required Change**: Add pseudocode for comparison

**Comparison Algorithm** (add to M2.1 V2):
```csharp
public FkCatalogVerificationResult Verify(
    IReadOnlyList<UserFkColumn> discoveredCatalog,
    IUserSchemaGraph schemaGraph)
{
    // Get expected catalog from model
    var expectedCatalog = schemaGraph.GetAllUserForeignKeys().ToList();

    // Build lookup sets (key: schema.table.column)
    var discoveredSet = BuildLookupSet(discoveredCatalog);
    var expectedSet = BuildLookupSet(expectedCatalog);

    // Find missing columns (in expected but not discovered)
    var missingColumns = expectedSet.Except(discoveredSet).ToImmutableArray();

    // Find unexpected columns (in discovered but not expected - WARNING only)
    var unexpectedColumns = discoveredSet.Except(expectedSet).ToImmutableArray();

    // Fail only if missing columns (unexpected is warning)
    var isValid = missingColumns.Length == 0;

    return new FkCatalogVerificationResult(
        isValid,
        discoveredCatalog.Count,
        expectedCatalog.Count,
        missingColumns);
}
```

### Recommendation for V2

1. **Add FK Catalog Comparison Algorithm** section to M2.1
2. **Show explicit comparison logic** with pseudocode
3. **Clarify**: Missing columns = FAIL, unexpected columns = WARNING
4. **Explain**: Unexpected columns are acceptable (model may be stale, manual FKs added)

---

## Additional Investigation: Error Message Format

### Question

What error message format should verifiers use? JSON only? Or also human-readable CLI output?

### Evidence

**No existing verification report format found** - greenfield decision

### Recommendation for V2

**Add Error Message Format Specification** to M2.1:

**JSON Report Format** (machine-readable for CI/CD):
```json
{
  "overallStatus": "FAIL",
  "timestamp": "2025-11-19T10:00:00Z",
  "verifications": {
    "transformationMapCompleteness": "FAIL"
  },
  "discrepancies": [
    {
      "severity": "ERROR",
      "code": "UNMAPPED_ORPHAN",
      "message": "Orphan user 999 has no target mapping",
      "location": "00_user_map.csv:15",
      "suggestion": "Add mapping: 999,<uat_target_id>,<rationale>"
    },
    {
      "severity": "ERROR",
      "code": "INVALID_TARGET",
      "message": "Target user 888 not found in UAT inventory",
      "location": "00_user_map.csv:20",
      "suggestion": "Verify user 888 exists in uat_users.csv or choose different target"
    }
  ]
}
```

**CLI Output Format** (human-readable):
```
UAT-Users Verification: FAIL

Transformation Map Validation:
  ✗ FAIL - 2 errors found

Errors:
  [00_user_map.csv:15] UNMAPPED_ORPHAN
    Orphan user 999 has no target mapping
    → Fix: Add mapping: 999,<uat_target_id>,<rationale>

  [00_user_map.csv:20] INVALID_TARGET
    Target user 888 not found in UAT inventory
    → Fix: Verify user 888 exists in uat_users.csv or choose different target

FK Catalog Completeness:
  ✓ PASS - All 23 columns discovered

SQL Safety:
  ✓ PASS - All required guards present
```

---

## Summary of V2 Changes Required

### M2.1: UAT-Users Verification Framework

**Changes**:
1. ✅ **Add**: Refactoring pattern section (exception → Result<T>)
2. ✅ **Add**: FK catalog comparison algorithm (pseudocode)
3. ✅ **Add**: Error message format specification (JSON + CLI)
4. ✅ **Add**: ScriptDom SQL parsing pattern (replace regex references)
5. ✅ **Clarify**: Artifact completeness pre-check (Phase 0)
6. ✅ **Clarify**: Performance acceptance criteria (verify 100 orphans in <1 sec)

**Effort Adjustment**: 2-3 days → **3-4 days** (add refactoring complexity buffer)

### M2.2: Transformation Verification

**MAJOR REVISION REQUIRED**:

**Option A - UPDATE-Only Verification** (RECOMMENDED):
1. 🔴 **Remove**: Entire "INSERT Transformation Verification" section (doesn't exist)
2. 🔴 **Remove**: Phase 0 research (question answered: not implemented)
3. 🔴 **Remove**: InsertScriptParser, InsertTransformationVerifier
4. 🔴 **Rename**: "M2.2: UPDATE Script Verification"
5. ✅ **Keep**: UpdateScriptSimulator, NullPreservationVerifier
6. ✅ **Add**: ScriptDom parsing examples (replace regex)
7. ✅ **Remove**: Cross-mode validation (no INSERT mode to compare)
8. ✅ **Clarify**: Scope is UPDATE-only verification
9. ✅ **Add**: Note about future INSERT verification (M5.*)

**Effort Adjustment**: 3-4 days → **2-3 days** (scope reduction)

**Option B - Include INSERT Implementation**:
1. ✅ **Add**: Phase 0: "Implement INSERT Transformation Injection" (NEW - 3-5 days)
2. ✅ **Keep**: All INSERT verification sections
3. ✅ **Add**: Integration with DynamicEntityInsertGenerator
4. ✅ **Add**: Transformation map passing through build pipeline

**Effort Adjustment**: 3-4 days → **7-10 days** (major scope increase)

**Recommendation**: **Option A** - Match current architecture

### M2.3: UAT-Users Integration Tests

**Changes**:
1. ✅ **Adjust**: Full-export integration test expectations (UPDATE scripts only, no INSERT transformations)
2. ✅ **Remove**: INSERT transformation verification tests (or defer to M5.3)
3. ✅ **Add**: Test fixture management section (CSVs, models, location)
4. ✅ **Add**: Full-export test harness setup (Phase 0 - 1 day)
5. ✅ **Add**: Scale tests (1k, 10k orphans)
6. ✅ **Add**: Concurrency tests
7. ✅ **Remove**: Cross-mode equivalence tests (no INSERT mode)

**Effort Adjustment**: 2-3 days → **3-4 days** (add test infrastructure setup)

### Cross-Cutting Changes

**All Specs**:
1. ✅ **Add**: Observability section (logging, metrics, telemetry strategy)
2. ✅ **Add**: Versioning strategy (artifact format compatibility)
3. ✅ **Add**: CI/CD integration examples (pipeline YAML, error parsing)
4. ✅ **Clarify**: Dependency types (implementation vs conceptual)

---

## Revised Effort Estimates

**Option A - UPDATE-Only Verification** (RECOMMENDED):
```
M2.1: 2-3 days → 3-4 days (refactoring complexity)
M2.2: 3-4 days → 2-3 days (scope reduction to UPDATE-only)
M2.3: 2-3 days → 3-4 days (test infrastructure setup)
--------------------------------------------
Total: 7-10 days → 8-11 days
```

**Option B - Include INSERT Implementation**:
```
M2.1: 2-3 days → 3-4 days (refactoring complexity)
M2.2: 3-4 days → 7-10 days (INSERT infrastructure + verification)
M2.3: 2-3 days → 3-4 days (test infrastructure setup)
--------------------------------------------
Total: 7-10 days → 13-18 days
```

**Recommendation**: **Option A** - Reflects current architecture accurately

---

## Next Steps

1. **Decision Point**: Choose Option A (UPDATE-only) or Option B (include INSERT implementation)
2. **If Option A**:
   - Revise M2.2 to "UPDATE Script Verification"
   - Remove INSERT-related sections
   - Add future milestone notes (M5.1, M5.2)
3. **If Option B**:
   - Expand M2.2 Phase 0 to include INSERT transformation implementation
   - Add DynamicEntityInsertGenerator integration details
   - Increase effort estimates
4. **Apply V2 changes** to all three specs
5. **Review revised specs** before implementation begins

---

*Research Complete: 2025-11-19*
*Recommendation: Proceed with Option A (UPDATE-only verification)*
