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

- [17.01: Explicit Type Conversion](Multi-Phase-Patterns/Explicit-Type-Conversion.md) — Change data types that require explicit conversion
- [17.02: NULL → NOT NULL](Multi-Phase-Patterns/Null-to-Not-Null.md) — Make a column required with backfill
- [17.03: Identity Property](Multi-Phase-Patterns/Identity-Property.md) — Add or remove IDENTITY
- [17.04: FK with Orphan Data](Multi-Phase-Patterns/FK-with-Orphan-Data.md) — Add foreign key when orphan rows exist
- [17.05: Safe Column Removal](Multi-Phase-Patterns/Safe-Column-Removal.md) — Four-phase deprecation workflow
- [17.06: Table Split](Multi-Phase-Patterns/Table-Split.md) — Vertical partitioning with data migration
- [17.07: Table Merge](Multi-Phase-Patterns/Table-Merge.md) — Denormalization with data migration
- [17.08: Schema Migration with Backward Compatibility](Multi-Phase-Patterns/Schema-Migration-Backward-Compatibility.md) — Maintaining compatibility during breaking changes
- [17.09: CDC Table Change](Multi-Phase-Patterns/CDC-Table-Change.md) — Schema changes on CDC-enabled tables

---

[← Back to Operations](../Operations.md)
