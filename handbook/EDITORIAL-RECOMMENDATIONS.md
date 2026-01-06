# SSDT Playbook: Technical Editor's Report

**Date:** 2026-01-06
**Reviewer:** Technical Editor (Final Draft Review)
**Status:** 6 of 33 sections complete

---

## Executive Summary

The completed sections (1, 2, 13-15, 23) are **excellent**. They're clear, actionable, and properly structured. The dimension/tier/mechanism trinity is particularly strong.

**Key finding:** The playbook needs its "translation layer" sections urgently. OutSystems developers won't naturally map their mental models to SSDT concepts without explicit bridges.

---

## Current State: What's Complete

### ✅ Section 1: Start Here
**Quality:** Strong
**Issues:** Minor
**Recommendation:** Add OutSystems equivalents to Quick Glossary

The section is well-structured with clear paths for different audiences. The "Foundational Truths" are excellent.

**Suggested enhancement:**
```markdown
| Term | Meaning | OutSystems Equivalent |
|------|---------|----------------------|
| **SSDT** | SQL Server Data Tools | (none - new tooling) |
| **Declarative** | You describe end state | Like Entity definitions in Service Studio |
| **Publish** | Deploy SSDT project | Like 1-Click Publish |
```

---

### ✅ Section 2: The Big Picture
**Quality:** Excellent
**Issues:** None significant
**Recommendation:** Complete as-is

The flowcharts are clear, the "Why We're Here" section provides great context, and the CDC constraint is properly elevated.

---

### ✅ Section 13: Dimension Framework
**Quality:** Excellent
**Issues:** None
**Recommendation:** Complete as-is

The four dimensions are clearly explained with good examples. Recognition heuristics are practical. The four classification examples demonstrate correct application.

---

### ✅ Section 14: Ownership Tiers
**Quality:** Excellent
**Issues:** Minor - missing visual flowchart
**Recommendation:** Add tier decision flowchart

Content is comprehensive. Escalation triggers are well-documented. Classification examples are helpful.

**Suggested addition:** One-page "What Tier Is This?" flowchart as referenced in the TOC.

---

### ✅ Section 15: SSDT Mechanism Axis
**Quality:** Excellent
**Issues:** None
**Recommendation:** Complete as-is

Clear explanation of all five mechanisms. Decision guide is practical. Operation-to-mechanism mapping table is valuable.

---

### ✅ Section 23: PR Template
**Quality:** Strong
**Issues:** None
**Recommendation:** Complete as-is

Template is comprehensive and mirrors the dimension/tier/mechanism framework well. Reviewer guidance is helpful.

---

## Gap Analysis: What's Missing

### 🔴 CRITICAL PRIORITY

#### Section 3: The Translation Layer
**Why critical:** OutSystems developers need explicit mental model bridges.
**Components needed:**
- OutSystems → SSDT Rosetta Stone (term mapping)
- "I Used To... Now I..." (action mapping)
- Risk Recalibration: "Feels Like / Actually Is" (danger rerating)
- Integration Studio Bridge (the literal handoff)

**User impact:** Without this, onboarding friction will be high.

#### Section 16: Operation Reference
**Why critical:** This is the day-to-day lookup reference.
**Components needed:**
- 9 major operation categories (per TOC)
- Each operation: Tier, Mechanism, Gotchas, Example
- Organized by OutSystems intent (not technical taxonomy)

**User impact:** Without this, every change requires re-deriving from first principles.

#### Section 18: Decision Aids
**Why critical:** Quick-reference tools for classification.
**Components needed:**
- "What Tier Is This?" flowchart
- "Do I Need Multi-Phase?" checklist
- "Can SSDT Handle This Declaratively?" quick ref
- Before-You-Start checklist
- CDC Impact Checker

**User impact:** Without this, classification takes too long and errors increase.

---

### 🟠 HIGH PRIORITY

#### Section 17: Multi-Phase Pattern Templates
**Why high priority:** Complex changes need concrete patterns.
**Components needed (9 patterns per TOC):**
1. Explicit Conversion Data Type Change
2. NULL → NOT NULL on Populated Table
3. Add/Remove IDENTITY
4. Add FK with Orphan Data
5. Safe Column Removal (4-Phase)
6. Table Split
7. Table Merge
8. Schema Migration with Backward Compatibility
9. CDC-Enabled Table Schema Change

**User impact:** Without this, teams will improvise and create inconsistent approaches.

#### Section 4: State-Based vs Imperative
**Why high priority:** Core mental model shift.
**Components needed:**
- The Mental Model Shift (declarative vs imperative)
- How SSDT Computes the Delta
- When the Abstraction Leaks

**User impact:** Without understanding this, developers will fight SSDT instead of working with it.

#### Section 10: SSDT Deployment Safety
**Why high priority:** Last line of defense against data loss.
**Components needed:**
- Publish Profile explained
- `BlockOnPossibleDataLoss` (the non-negotiable)
- `DropObjectsNotInSource` (environment-specific)
- Other critical settings matrix

**User impact:** Settings misconfiguration can bypass all other safeguards.

#### Section 12: CDC and Schema Evolution
**Why high priority:** Affects ~200 tables; frequent gotcha.
**Components needed:**
- Why CDC Complicates Everything
- Operations Requiring Instance Recreation (matrix)
- Dev/UAT/Prod strategies
- CDC Table Registry (link)

**User impact:** CDC surprises cause production incidents.

---

### 🟡 MEDIUM PRIORITY

#### Section 19: Anti-Patterns Gallery
**Why medium priority:** Preventive education through "don't do this."
**Components needed (7 anti-patterns per TOC):**
1. The Naked Rename
2. The Optimistic NOT NULL
3. The Forgotten FK Check
4. The Ambitious Narrowing
5. The CDC Surprise
6. The Refactorlog Cleanup
7. The SELECT * View

**User impact:** Concrete "what not to do" examples prevent common mistakes.

#### Sections 5-9, 11, 20-22, 24-28, 30-32
**Why medium priority:** Important but can be added iteratively.

These sections are foundational (5-9), process (20-22), or reference (24-28, 30-32). They're needed for completeness but aren't blocking day-to-day work.

---

## Structural Recommendations

### 1. Create Handbook Navigation Index

The `handbook/` directory needs a README that:
- Lists all sections with status (✅ Complete / 🚧 In Progress / ⬜ Planned)
- Links to each section
- Shows the overall structure (Orientation / Foundations / Operations / Process / Reference)

### 2. Cross-Reference Validation

As more sections are added, validate all `[Section N](#)` links actually resolve. Consider:
- A script to validate internal links
- Or a GitHub Wiki structure where links auto-validate

### 3. Progressive Disclosure Markers

Per the TOC notes, Operation Reference should use **Layer 1/2/3** progressive disclosure:
- **Layer 1:** One-liner (tier, mechanism)
- **Layer 2:** Full card (dimensions, steps, SSDT output)
- **Layer 3:** Gotchas, edge cases, worked example

---

## Recommended Execution Order

If you want me to execute these, here's my suggested priority order:

### Phase 1: Critical Path (Weeks 1-2)
1. ✅ **Section 3: Translation Layer** — Bridges OutSystems to SSDT mental models
2. ✅ **Section 18: Decision Aids** — Quick-reference flowcharts
3. ✅ **Section 16: Operation Reference** — Day-to-day lookup (can start with top 10 operations)
4. ✅ **Handbook README** — Navigation index

### Phase 2: Safety & Patterns (Week 3)
5. ✅ **Section 10: SSDT Deployment Safety** — Settings as last line of defense
6. ✅ **Section 12: CDC and Schema Evolution** — Frequent gotcha
7. ✅ **Section 17: Multi-Phase Pattern Templates** — Start with top 3-5 patterns
8. ✅ **Section 4: State-Based vs Imperative** — Core mental model

### Phase 3: Preventive & Polish (Week 4)
9. ✅ **Section 19: Anti-Patterns Gallery** — What not to do
10. ✅ **Section 14 Enhancement:** Add tier decision flowchart
11. ✅ **Section 1 Enhancement:** OutSystems equivalents in glossary

### Phase 4: Foundations & Process (Iterative)
12-27. Remaining sections (5-9, 11, 20-22, 24-28, 30-33)

---

## Specific Tasks for Approval

Here are the concrete tasks I'm proposing to execute:

### Tier 1: Immediate (Do These First)

**Task 1: Create Section 3 - The Translation Layer**
- OutSystems ↔ SSDT term mapping table (20 key terms)
- "I Used To... Now I..." action mapping (15 operations)
- Risk Recalibration table ("Feels Like Tier 1 / Actually Tier 3" examples)
- Integration Studio workflow with decision points

**Task 2: Create Section 18 - Decision Aids**
- "What Tier Is This?" flowchart (visual, one-page)
- "Do I Need Multi-Phase?" checklist (yes/no questions)
- "Can SSDT Handle This Declaratively?" quick ref table
- Before-You-Start universal checklist
- CDC Impact Checker (query + interpretation)

**Task 3: Create Handbook README**
- Section status matrix
- Direct links to all sections
- Quick-start paths by role
- Contributing guide link

### Tier 2: High-Value Next

**Task 4: Create Section 16 - Operation Reference (Initial)**
- Start with top 10 most common operations:
  - Create Table
  - Add Nullable Column
  - Add NOT NULL Column
  - Add FK (Clean Data)
  - Add FK (Orphan Data)
  - Rename Column
  - Rename Table
  - NULL → NOT NULL
  - Widen Column
  - Add Index
- Use progressive disclosure format (Layer 1/2/3)
- Each operation: Tier, Mechanism, Steps, Gotchas, Example

**Task 5: Create Section 10 - SSDT Deployment Safety**
- Publish Profile explanation
- `BlockOnPossibleDataLoss` deep dive
- `DropObjectsNotInSource` by environment
- Settings matrix table
- "What to do when deployment is blocked" flowchart

**Task 6: Create Section 12 - CDC and Schema Evolution**
- "Why CDC Complicates Everything" explanation
- Operations matrix (what requires instance recreation)
- Dev strategy (accept gaps, batch cycles)
- UAT strategy (gap communication template)
- Prod strategy (dual-instance pattern)
- Link to CDC Table Registry (placeholder if registry doesn't exist yet)

### Tier 3: Patterns & Preventive

**Task 7: Create Section 17 - Multi-Phase Patterns (Initial)**
- Start with top 5 patterns:
  - Explicit Type Conversion (VARCHAR → DATE)
  - NULL → NOT NULL (Populated Table)
  - Add FK with Orphan Data
  - Safe Column Removal (4-Phase)
  - CDC-Enabled Table Change
- Each pattern: Phase sequence, code templates, rollback points

**Task 8: Create Section 4 - State-Based vs Imperative**
- The mental model shift (declarative vs imperative)
- How SSDT computes the delta (comparison engine)
- When the abstraction leaks (cases where you must review generated SQL)
- Worked example: "What happens when you edit a .sql file"

**Task 9: Create Section 19 - Anti-Patterns Gallery**
- All 7 anti-patterns from TOC
- Each: Setup (~1 para), Code example, Consequence, Correct approach

### Tier 4: Polish & Enhancement

**Task 10: Add Tier Decision Flowchart to Section 14**
- Visual flowchart (ASCII art or mermaid diagram)
- One-page printable format
- Decision points based on dimension assessment

**Task 11: Enhance Section 1 Glossary**
- Add "OutSystems Equivalent" column to Quick Glossary table
- Map 10 key terms to OutSystems concepts

---

## Quality Standards for New Sections

All new sections should follow these standards (which the existing 6 sections already meet):

1. **Clear "What This Section Covers" intro** — Set expectations
2. **Scannable structure** — Headers, tables, code blocks
3. **Concrete examples** — Every principle gets a worked example
4. **Cross-references** — Link to related sections
5. **Consistent voice** — Direct, practical, non-academic
6. **No orphaned references** — All `[Section N](#)` links should either resolve or be marked as TODO

---

## Questions for You

Before I execute, I need your input on:

1. **Do you want me to proceed with all Tier 1 tasks (Tasks 1-3)?**
   - These are the highest impact and create the "translation layer" for OutSystems developers.

2. **Should I create sections incrementally (e.g., Operation Reference with 10 operations first), or wait until I can do them comprehensively?**
   - Incremental: Faster time-to-value, but some "Coming Soon" placeholders
   - Comprehensive: Complete sections, but takes longer

3. **For Section 16 (Operation Reference), do you want me to draft all ~40 operations at once, or start with the top 10-15?**
   - The TOC shows 9 major categories with multiple operations each.

4. **Do you have an existing CDC Table Registry?**
   - If not, should I create a placeholder template for Section 12 to reference?

5. **Visual aids: ASCII art, mermaid diagrams, or just descriptive text?**
   - Flowcharts and diagrams are referenced in the TOC. What's your preferred format?

6. **Should I create the remaining foundational sections (5-9, 11) before or after the operational sections (16-19)?**
   - Foundations-first: More logical order
   - Operations-first: Faster practical value

---

## Recommendation

**My recommendation:** Execute in this exact order:

1. **Task 1** (Section 3) — Translation Layer is THE critical gap
2. **Task 3** (Handbook README) — Navigation infrastructure
3. **Task 2** (Section 18) — Decision aids make everything else usable
4. **Task 4** (Section 16 - Top 10 ops) — Day-to-day reference
5. **Task 5** (Section 10) — Safety critical
6. **Task 6** (Section 12) — CDC is a frequent blocker

That gives you:
- **Mental model bridges** (Section 3)
- **Quick-reference tools** (Section 18)
- **Day-to-day lookup** (Section 16 initial)
- **Safety guardrails** (Section 10)
- **CDC handling** (Section 12)

Then you have a **minimally complete playbook** that covers ~80% of day-to-day work.

---

## Awaiting Your Direction

I'm ready to execute on your approval. Which tasks would you like me to tackle first?
