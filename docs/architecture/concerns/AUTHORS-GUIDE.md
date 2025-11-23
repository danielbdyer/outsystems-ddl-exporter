# Author's Guide: Entity Pipeline Concerns Documentation

**Purpose**: Capture the principles, approach, and course corrections for documenting entity pipeline primitives
**Audience**: Future agents/developers continuing this work
**Date**: 2025-01-23

---

## 🎯 What We're Doing (Core Principles)

### 1. **"Ingredients, Not Recipes"**

**User feedback**: _"we're aggregating ingredients for our FUTURE recipe, not our current one"_

- ✅ **DO**: Catalog what primitives exist (classes, methods, data structures)
- ✅ **DO**: Show which pipelines use them (universal, shared, unique)
- ✅ **DO**: Provide file locations and line numbers
- ❌ **DON'T**: Prescribe how to extract, generalize, or implement unified pipeline
- ❌ **DON'T**: Write "Implementation Strategy" sections with step-by-step extraction plans
- ❌ **DON'T**: Create "Future Generalized Models" predictions

**Why**: The unification doc (`entity-pipeline-unification.md`) is the recipe. Concern docs are the parts inventory.

---

### 2. **"Organize by Future Namespace/Class Shape"**

**User feedback**: _"I'd probably bias toward it being an encapsulation of the primary future namespace or class shape - that maybe is the future governing principle"_

Each concern document represents what will likely become a **namespace or major class** in the unified architecture.

**Examples**:
- `01-topological-ordering.md` → Future: `TopologicalSorter` class
- `02-insertion-strategies.md` → Future: `InsertionStrategy` interface (MERGE + INSERT implementations)
- `03-data-structures.md` → Future: `EntityData` namespace

**Why**: This naturally creates "bundles of value" aligned to discrete deliverables/roadmap items.

---

### 3. **"Bundle of Value = Discrete Deliverable"**

**User feedback**: _"the goal is to be able to provide subtargets that can be accomplished as roadmap items"_

- ✅ Each document = one thing you could tackle as a roadmap item
- ✅ One document should represent a "discrete crossover of concern and stage"
- ✅ Usually one stage + one concern, but not strictly
- ❌ Don't split arbitrarily if there's not sufficient information

**Example**: MERGE and INSERT are **both insertion strategies** (different implementations of the same concern), so they belong in ONE document, not two separate docs.

---

### 4. **"Don't Split Arbitrarily"**

**User feedback**: _"Sometimes if there's not sufficient information it doesn't make sense to break down it any further arbitrarily!"_

- ✅ **DO**: Keep related concerns together if they're part of the same deliverable
- ✅ **DO**: MERGE + INSERT = one concern ("insertion strategies")
- ✅ **DO**: Only split when content becomes extraneous to the main deliverable
- ❌ **DON'T**: Create separate files mechanically by stage if they form one logical unit

**Anti-pattern**: Creating `02-merge-insertion-primitive.md` and `03-insert-insertion-primitive.md` separately.
**Correct pattern**: Creating `02-insertion-strategies.md` covering both MERGE and INSERT.

---

### 5. **"Unification Doc is the North Star"**

**User feedback**: _"we need to bridge the gap between our current architecture and the new one"_

- The `entity-pipeline-unification.md` is the **PRIMARY** document (the blueprint)
- Concern docs are **SUPPORTING** reference material (the materials inventory)
- Concern docs **don't replace** the unification narrative - they **support** it
- The README acts as the "unification overlay" showing how concerns map to the 7-stage pipeline

**Reading flow**:
1. Read `entity-pipeline-unification.md` to understand the vision
2. Use concern docs as reference for specific primitives
3. Use README's pipeline diagram to navigate between stages

---

### 6. **"Verify Assertions Against Codebase"**

**User feedback**: _"Can you go through and check your assertions/assumptions one by one? For example - the part about topological sort being broken - your disclaimer about it being used only by Static Entity Seeds may be incorrect"_

- ❌ **DON'T**: Make assumptions about component usage
- ✅ **DO**: Grep for actual consumers of each primitive
- ✅ **DO**: Read the usage sites to understand real patterns
- ✅ **DO**: Mark findings as (✅ = Verified) when checked against codebase

**Example correction**:
- **Was**: "Topological sort is BROKEN - currently static-only"
- **Actually**: EntityDependencySorter used by all 3 pipelines (StaticSeeds, DynamicInsert, Bootstrap)
- **Found by**: Grepping for `EntityDependencySorter.SortByForeignKeys` callsites

---

### 7. **"Some Duplication is Fine"**

**User feedback**: _"I love having the massive document approach and would be totally fine having some duplicate information in multiple files since - after all - some concerns can't be fully guarded into a standalone piece"_

- ✅ Concerns overlap naturally (e.g., topological ordering uses data structures)
- ✅ Documents should include cross-references where concerns intersect
- ✅ It's okay to mention the same primitive in multiple docs if it's relevant to both concerns
- ❌ Don't try to force strict separation with no overlap

---

### 8. **"Focus on Future Recipe Ingredients"**

**User feedback**: _"while it's fine that we are profiling current implementation norms we should also keep in the back of our mind that we're aggregating ingredients for our future recipe, not our current one"_

- ✅ Focus on **what to extract** for the unified pipeline
- ✅ Highlight **shared primitives** (can be reused)
- ✅ Identify **unique features** (may need to be generalized or made optional)
- ❌ Don't just document current state without thinking about future unification

**Example**: Note that `StaticEntityTableData` is **universal** (used by all 3 pipelines) despite the misleading "Static" name - this is important for future renaming.

---

## 🚫 What We're NOT Doing (Anti-Patterns)

### 1. **NOT: Extraction Guides**

❌ Don't create step-by-step "how to extract" instructions
❌ Don't write "Phase 1: Understand, Phase 2: Extract, Phase 3: Build"
❌ Don't prescribe future implementation steps

**Why**: That's what the unification doc is for. Concern docs are just the parts inventory.

---

### 2. **NOT: Mechanically Splitting by Stage**

❌ Don't create one doc per stage just because there are 7 stages
❌ Don't split MERGE and INSERT into separate docs just because they're different primitives

**Why**: Organize by **deliverable/future class**, not by mechanical stage boundaries.

---

### 3. **NOT: Making Unverified Assumptions**

❌ Don't assume a primitive is "static-only" without checking consumers
❌ Don't claim something is "broken" without verifying actual usage
❌ Don't state something is "unique" without grepping for other usages

**Why**: We were wrong about topological sort being "broken" - it's actually universal. Always verify.

---

### 4. **NOT: Creating Arbitrary File Structure**

❌ Don't create folders like `insertion-strategies/`, `ordering/`, etc. without justification
❌ Keep it flat unless there's a clear need for hierarchy
❌ Don't over-organize prematurely

**Why**: Simpler is better. Flat structure is easier to navigate until complexity demands hierarchy.

---

### 5. **NOT: Fragmenting the Unification Narrative**

❌ Don't let concern docs become the primary source of truth
❌ Don't lose sight of how primitives compose into the unified pipeline
❌ Don't make the README just a file list

**Why**: The README needs to be a "unification overlay" showing the 7-stage pipeline flow.

---

## 📋 Document Organization Checklist

When creating or updating a concern document, ensure:

- [ ] **Title reflects future class/namespace** (not just current implementation)
- [ ] **Header specifies pipeline stage(s)** this concern covers
- [ ] **"What This Document Covers" section** clearly states the concern
- [ ] **Quick Reference table** with primitives and file locations
- [ ] **Critical Findings** are marked (✅ = Verified) when checked against codebase
- [ ] **Cross-references** to related concerns where they intersect
- [ ] **Primitives are cataloged**, not prescriptive extraction steps
- [ ] **File inventory** lists all implementing files
- [ ] **NO "Implementation Strategy" or "Phase 1/2/3" sections**

---

## 🗺️ The Big Picture

```
entity-pipeline-unification.md (North Star - conceptual vision)
         ↓
concerns/README.md (Overlay - maps concerns to 7-stage pipeline)
         ↓
concerns/ROADMAP.md (Plan - what docs to create, priority order)
         ↓
concerns/*.md (Parts inventory - primitives catalog)
```

**Flow**:
1. Unification doc tells you **why** and **what** (the vision)
2. README tells you **how concerns map to stages** (the overlay)
3. ROADMAP tells you **what to build** (the plan)
4. Concern docs tell you **what exists today** (the inventory)

---

## 🎬 Next Steps (When Resuming)

1. **Read ROADMAP.md** - Understand the complete plan
2. **Use current mega-document** (`01-entity-pipeline-shared-primitives.md`) as source material
3. **Extract systematically**:
   - Priority 1: `02-insertion-strategies.md` (add INSERT content to existing MERGE content)
   - Priority 2: `03-data-structures.md` (foundation - extract from mega-doc)
   - Priority 3: `05-business-logic-transforms.md` (supporting primitives)
   - Optional: `04-data-providers.md`, `06-entity-selection.md` (may not need separate docs)
4. **Trim mega-document** - Once content is extracted, remove from original and add stubs/cross-references
5. **Rename mega-document** - `01-entity-pipeline-shared-primitives.md` → `01-topological-ordering.md` (focused on Stage 4 only)

---

## 🔍 Key Insights Learned

### About EntityDependencySorter (Stage 4)
- ✅ **Universal** - Used by all 3 pipelines (StaticSeeds, DynamicInsert, Bootstrap)
- ❌ **NOT broken** - Works perfectly on any entity set
- ⚠️ **Problem**: Executed separately per pipeline (misses cross-category FKs)
- ✅ **Solution**: Bootstrap demonstrates correct pattern (global sort on all entities)

### About MERGE vs INSERT (Stages 5+6)
- ✅ **MERGE is shared** - Used by StaticSeeds AND Bootstrap (not static-entity-specific!)
- ✅ **INSERT is pipeline-specific** - Only DynamicInsert uses it
- ✅ **Both strategies needed** - Different use cases (idempotent vs. append-only)
- ✅ **One concern** - Both solve "how to insert data", belong in one doc

### About Data Structures (Stage 2)
- ✅ **StaticEntityTableData is universal** - Used by all 3 pipelines despite "Static" name
- ✅ **Naming is misleading** - Many "Static" prefixed classes are actually universal
- ✅ **Foundation for everything** - Must understand these before other stages

### About Bootstrap
- ✅ **Proof of concept** - Already demonstrates unified pipeline pattern
- ✅ **Combines all entities** - Static + regular in one global sort
- ✅ **Uses MERGE** - Via StaticSeedSqlBuilder (same as StaticSeeds)
- 💡 **Key insight**: Bootstrap IS the unified pipeline for all entities!

---

## 💬 User Feedback (Verbatim Key Quotes)

> "we're aggregating ingredients for our FUTURE recipe, not our current one"

> "I'd probably bias toward it being an encapsulation of the primary future namespace or class shape"

> "the goal is to be able to provide subtargets that can be accomplished as roadmap items"

> "Sometimes if there's not sufficient information it doesn't make sense to break down it any further arbitrarily!"

> "we need to bridge the gap between our current architecture and the new one"

> "Can you go through and check your assertions/assumptions one by one?"

> "I love having the massive document approach and would be totally fine having some duplicate information in multiple files"

---

## ✅ Success Criteria

You'll know you're on track when:

- [ ] Each document represents a **future class/namespace** (not arbitrary split)
- [ ] Documents are **400-700 lines** each (focused bundles of value)
- [ ] The **README maintains the 7-stage pipeline narrative** (unification overlay)
- [ ] **No prescriptive extraction steps** in concern docs (just inventory)
- [ ] **Assertions are verified** against codebase (marked ✅)
- [ ] **Cross-references** link related concerns
- [ ] **Unification doc remains the North Star** (concern docs support it)

---

**Last updated**: 2025-01-23
**Status**: Ready for systematic extraction based on ROADMAP.md
