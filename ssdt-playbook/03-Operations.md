# Operations

How to classify, execute, and review database changes.

## Classification Framework

Start here to understand what tier your change is:

- [Dimension Framework](03-Operations/13-Dimension-Framework.md) — How to classify any change
- [Ownership Tiers](03-Operations/14-Ownership-Tiers.md) — Who owns what level of risk
- [SSDT Mechanism Axis](03-Operations/15-SSDT-Mechanism-Axis.md) — Declarative, scripted, or multi-phase

## Operation Reference

Detailed "how to" for every operation type:

📁 [Operation Reference](03-Operations/16-Operation-Reference.md)
- Entities (Tables)
- Attributes (Columns)
- Keys and References
- Indexes
- Lookup Tables
- Constraints
- Structural Changes
- Views and Abstraction
- Audit and Temporal
- Quick Lookup Tables

## Multi-Phase Patterns

When changes require multiple releases:

📁 [Multi-Phase Patterns](03-Operations/17-Multi-Phase-Patterns.md)
- Explicit Type Conversion
- NULL → NOT NULL
- Identity Property Changes
- FK with Orphan Data
- Safe Column Removal
- Table Split/Merge
- CDC Table Changes

## Decision Aids & Anti-Patterns

- [Decision Aids](03-Operations/18-Decision-Aids.md) — Flowcharts, checklists, quick references
- [Anti-Patterns Gallery](03-Operations/19-Anti-Patterns-Gallery.md) — Common mistakes to avoid

## Reading Path

1. Classify using **Dimension Framework** and **Ownership Tiers**
2. Find your operation in **Operation Reference**
3. Check if **Multi-Phase Patterns** apply
4. Use **Decision Aids** for quick lookups

---

Previous: [Foundations](02-Foundations.md) | Next: [Process](04-Process.md)
