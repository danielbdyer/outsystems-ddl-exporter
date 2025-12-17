# 17. Multi-Phase Pattern Templates

When changes require multiple sequential deployments to complete safely.

## What This Section Covers

Some database changes can't be done atomically in a single deployment. They require coordination across multiple releases because:

- **Data dependencies** — Data must exist before constraints can be added
- **Application coordination** — Code changes must deploy before/after schema changes
- **CDC constraints** — Capture instances must be managed carefully
- **Risk minimization** — Breaking changes into phases reduces blast radius

Each pattern in this section provides:
- **Phase sequence** — What happens in each release
- **Release mapping** — Which phases can share a release
- **Code templates** — Exact scripts for each phase
- **Rollback considerations** — What's reversible at each stage

---

## Available Patterns

- [17.01: Explicit Type Conversion](17.01-Explicit-Type-Conversion.md) — Change data types that require explicit conversion
- [17.02: NULL → NOT NULL](17.02-Null-to-Not-Null.md) — Make a column required with backfill
- [17.03: Identity Property](17.03-Identity-Property.md) — Add or remove IDENTITY
- [17.04: FK with Orphan Data](17.04-FK-with-Orphan-Data.md) — Add foreign key when orphan rows exist
- [17.05: Safe Column Removal](17.05-Safe-Column-Removal.md) — Four-phase deprecation workflow
- [17.06: Table Split](17.06-Table-Split.md) — Vertical partitioning with data migration
- [17.09: CDC Table Change](17.09-CDC-Table-Change.md) — Schema changes on CDC-enabled tables

---

[← Back to Operations](../index.md)
