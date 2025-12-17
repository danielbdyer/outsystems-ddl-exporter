# Foundations

Core concepts you need to understand before making changes.

## Essential Concepts

- [State-Based Modeling](04-State-Based-Modeling-vs-Imperative-Migrations.md) — Declarative vs. imperative
- [Anatomy of an SSDT Project](05-Anatomy-of-an-SSDT-Project.md) — What lives where
- [Deployment Scripts](06-Pre-Deployment-and-Post-Deployment-Scripts.md) — Pre/post patterns
- [Idempotency](07-Idempotency-101.md) — Scripts that run safely multiple times

## Safety & Constraints

- [Referential Integrity](08-Referential-Integrity-Basics.md) — Foreign keys and dependencies
- [Refactorlog and Renames](09-The-Refactorlog-and-Rename-Discipline.md) — Critical for data safety
- [Deployment Safety Settings](10-SSDT-Deployment-Safety.md) — Publish profiles and protections

## Advanced Patterns

- [Multi-Phase Evolution](11-Multi-Phase-Evolution.md) — Changes that span releases
- [CDC and Schema Evolution](12-CDC-and-Schema-Evolution.md) — Working with CDC-enabled tables

## Reading Path

**Minimum:** Read State-Based Modeling, Refactorlog, and Deployment Safety before your first change.

**Recommended:** All sections, in order.

---

Previous: [Orientation](../01-orientation/index.md) | Next: [Operations](../03-operations/index.md)
