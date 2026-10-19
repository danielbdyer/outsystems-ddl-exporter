# Entity Pipeline Concerns: Complete Roadmap

This document enumerates ALL future concern documents based on the future namespace/class architecture.

**Date**: 2025-01-23
**Status**: Planning snapshot

---

## 🎯 Organization Principle

Each document represents what will likely become a **namespace or major class** in the unified entity pipeline architecture.

**One document = One deliverable/roadmap item**

---

## 📋 Complete Concern Documents List

### ✅ COMPLETED

#### 01-topological-ordering.md
- **Future Class**: `TopologicalSorter` or `DependencyOrdering`
- **Pipeline Stage**: Stage 4
- **Primitives**:
  - `EntityDependencySorter` (universal - 2 real implementations: StaticSeeds, Bootstrap)
  - `EntityDependencySortOptions`
  - `CircularDependencyOptions`
  - `TopologicalOrderingValidator` (Bootstrap only)
- **Status**: ✅ Complete (~400 lines)
- **Deliverable**: Understand topological ordering primitive
- **Note**: DynamicData also uses this but is DEPRECATED (to be deleted)

#### 02-insertion-strategies.md
- **Future Interface**: `InsertionStrategy` with MERGE and INSERT implementations
- **Pipeline Stages**: Stage 5 (Emission) + Stage 6 (Insertion)
- **Primitives**:
  - **MERGE**: `StaticSeedSqlBuilder` (StaticSeeds only - idempotent upsert)
  - **INSERT**: `DynamicEntityInsertGenerator` (Bootstrap primary - one-time load)
  - Configuration: `StaticSeedSynchronizationMode`, `DynamicEntityInsertGenerationOptions`
- **Status**: 🚧 In progress (MERGE complete ~330 lines, INSERT to be added)
- **Deliverable**: Understand insertion strategy primitives (both MERGE and INSERT)
- **Note**: DynamicData also uses INSERT but is DEPRECATED (redundant with Bootstrap, to be deleted)

---

### 🚧 TO BE CREATED

#### 03-data-structures.md
- **Future Namespace**: `EntityData` or `DataStructures`
- **Pipeline Stage**: Stage 2 (Database Snapshot Fetch)
- **Primitives**:
  - `StaticEntityTableData` (universal - used by both real implementations, despite "Static" name!)
  - `StaticEntityRow` (universal)
  - `StaticEntitySeedTableDefinition` (shared by StaticSeeds + Bootstrap)
  - `StaticEntitySeedColumn` (shared by StaticSeeds + Bootstrap)
  - `DynamicEntityDataset` (Bootstrap + deprecated DynamicData)
  - `EntitySeedDeterminizer` (deterministic ordering/normalization)
- **Why needed**: These are universal data structures used by both real implementations - critical foundation
- **Estimated size**: ~400-500 lines
- **Deliverable**: Understand core entity data structures
- **Note**: Only 2 real implementations to consider (StaticSeeds MERGE, Bootstrap INSERT); DynamicData and Supplemental are deprecated

#### 04-data-providers.md (OPTIONAL - may not be needed)
- **Future Class**: `DatabaseSnapshot` or merged into Stage 2
- **Pipeline Stage**: Stage 2 (Database Snapshot Fetch)
- **Primitives**:
  - `SqlStaticEntityDataProvider` (StaticSeeds only)
  - `IStaticEntityDataProvider` interface
  - Fixture-based providers
  - SELECT + ORDER BY patterns
- **Decision needed**: Merge with 03-data-structures.md or keep separate?
- **Estimated size**: ~300-400 lines (if separate)
- **Deliverable**: Understand data fetching patterns

#### 05-business-logic-transforms.md
- **Future Namespace**: `BusinessLogicTransforms` or `Transforms`
- **Pipeline Stage**: Stage 3 (Business Logic Transforms)
- **Primitives**:
  - `StaticSeedForeignKeyPreflight` (FK orphan detection - StaticSeeds only)
  - `TopologicalOrderingValidator` (enhanced cycle diagnostics - Bootstrap only)
  - Nullability tightening (validation overrides)
  - Deferred FK constraints (WITH NOCHECK)
  - Module name collision handling
  - UAT-users transformation (referenced, already documented separately)
- **Why needed**: Optional validation/transformation primitives that may be generalized
- **Estimated size**: ~400-600 lines
- **Deliverable**: Understand business logic transformation primitives

#### 06-entity-selection.md (OPTIONAL - may be too small)
- **Future Class**: `EntitySelector`
- **Pipeline Stage**: Stage 1 (Entity Selection)
- **Primitives**:
  - `StaticEntitySeedDefinitionBuilder` (StaticSeeds only)
  - Hardcoded filter patterns (`e.IsStatic && e.IsActive`)
  - Module filtering logic
- **Decision needed**: May not have enough content to justify separate doc
- **Estimated size**: ~200-300 lines (too small?)
- **Deliverable**: Understand entity selection patterns
- **Alternative**: Fold into 03-data-structures.md or 05-business-logic-transforms.md

---

## 📊 Priority Order for Creation

Based on dependencies and importance:

### Priority 1: Foundation
1. ✅ **01-topological-ordering.md** - Complete
2. ✅ **02-insertion-strategies.md** - In progress (add INSERT content)
3. 🚧 **03-data-structures.md** - Create next (foundation for everything)

### Priority 2: Supporting Primitives
4. 🚧 **05-business-logic-transforms.md** - Create after data structures
5. 🚧 **04-data-providers.md** - Optional (may merge with #3)

### Priority 3: Nice-to-Have
6. 🚧 **06-entity-selection.md** - Optional (may be too small, fold into #3 or #5)

---

## 🔄 Stage 0 (EXTRACT-MODEL)

**No concern document planned** - Uses shared OsmModel extraction, not entity-pipeline-specific.

---

## 📐 Document Template Structure

Each concern document should follow this structure:

```markdown
# [Concern Name] ([Pipeline Stages])

**Pipeline Stage(s)**: [Stage numbers]
**Concern**: [Brief description]
**Future Architecture**: [Namespace/class name]
**Date**: 2025-01-23

## 🎯 What This Document Covers
- Overview of concern
- Primitives cataloged
- Which pipelines use them

## ⚡ Quick Reference
- Table of primitives with file locations

## 🔬 Critical Findings (✅ = Verified)
- Key insights from codebase analysis

## 🔗 Related Concerns
- Cross-references to other concern docs

## 📐 [Main content organized by primitive]
- Detailed catalog of each primitive

## 💡 Key Insights
- Summary of what was learned

## 🗂️ Complete File Inventory
- All files implementing this concern
```

---

## 🎬 Next Steps

1. **Complete 02-insertion-strategies.md** - Extract INSERT content from mega-document
2. **Trim 01-topological-ordering.md** - Remove content that belongs in other docs (add stubs)
3. **Create 03-data-structures.md** - Extract from mega-document
4. **Decide on 04-data-providers.md** - Merge with #3 or keep separate?
5. **Create 05-business-logic-transforms.md** - Extract from mega-document
6. **Decide on 06-entity-selection.md** - Enough content to justify? Or fold into another doc?

---

## 🔍 Content Currently in Mega-Document

The current `01-topological-ordering.md` (569 lines) contains content for ALL stages:
- Stage 1 (Entity Selection) - Lines 149-172
- Stage 2 (Database Snapshot) - Lines 175-235
- Stage 3 (Transforms) - Lines 238-264
- Stage 4 (Topological Sort) - Lines 155-209 ✅ **Keep here**
- Stage 5 (Emission) - Lines 211-320 → Extract to 02-insertion-strategies.md
- Stage 6 (Insertion) - Lines 322-365 → Extract to 02-insertion-strategies.md
- Data Models - Lines 368-387 → Extract to 03-data-structures.md
- Test Coverage - Lines 389-401 → Distribute to relevant docs
- File Inventory - Lines 403-428 → Distribute to relevant docs

**Strategy**: Either trim systematically OR check out earlier commit and extract fresh.

---

**Last updated**: 2025-01-23
