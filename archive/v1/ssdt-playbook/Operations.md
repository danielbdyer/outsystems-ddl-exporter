# Operations

How to classify, execute, and review database changes.

## Classification Framework

Start here to understand what tier your change is:

- [Dimension Framework](Operations/Dimension-Framework.md) — How to classify any change
- [Ownership Tiers](Operations/Ownership-Tiers.md) — Who owns what level of risk
- [SSDT Mechanism Axis](Operations/SSDT-Mechanism-Axis.md) — Declarative, scripted, or multi-phase

## Operation Reference

Detailed "how to" for every operation type:

📁 [Operation Reference](Operations/Operation-Reference.md)
- Entities (Tables)
- Attributes (Columns)
- Keys and References
- Indexes
- Lookup Tables
- Constraints
- Structural Changes
- Audit and Temporal
- Quick Lookup Tables

## Multi-Phase Patterns

When changes require multiple releases:

📁 [Multi-Phase Patterns](Operations/Multi-Phase-Patterns.md)
- Explicit Type Conversion
- NULL → NOT NULL
- Identity Property Changes
- FK with Orphan Data
- Safe Column Removal
- Table Split/Merge

## Decision Aids & Anti-Patterns

- [Decision Aids](Operations/Decision-Aids.md) — Flowcharts, checklists, quick references
- [Anti-Patterns Gallery](Operations/Anti-Patterns-Gallery.md) — Common mistakes to avoid

## Reading Path

1. Classify using **Dimension Framework** and **Ownership Tiers**
2. Find your operation in **Operation Reference**
3. Check if **Multi-Phase Patterns** apply
4. Use **Decision Aids** for quick lookups

---

Previous: [Foundations](Foundations.md) | Next: [Process](Process.md)
