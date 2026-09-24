# SSDT Playbook

Comprehensive guidance for managing database schema changes using SQL Server Data Tools (SSDT) with OutSystems External Entities.

---

## Quick Start

- **New to SSDT?** Start with [Orientation](Orientation/Start-Here.md)
- **Making a change?** Go to [Decision Aids](Operations/Decision-Aids.md)
- **Need a specific operation?** Check the [Operation Reference](Operations/Operation-Reference.md)
- **Something broke?** See [Troubleshooting](Process/Troubleshooting-Playbook.md)

---

## Sections

### [I. Orientation](Orientation.md)

Getting started with the playbook and understanding the SSDT approach.

- [Start Here](Orientation/Start-Here.md) — What this is, who it's for, how to use it
- [The Big Picture](Orientation/The-Big-Picture.md) — Why we're here, how changes flow, what success looks like
- [The Translation Layer](Orientation/The-Translation-Layer.md) — OutSystems to SSDT mental model shift

---

### [II. Foundations](Foundations.md)

Core concepts you need to understand before making changes.

- [State-Based Modeling vs. Imperative Migrations](Foundations/State-Based-Modeling-vs-Imperative-Migrations.md) — The mental model shift
- [Anatomy of an SSDT Project](Foundations/Anatomy-of-an-SSDT-Project.md) — File structure and organization
- [Pre-Deployment and Post-Deployment Scripts](Foundations/Pre-Deployment-and-Post-Deployment-Scripts.md) — When declarative isn't enough
- [Idempotency 101](Foundations/Idempotency-101.md) — Scripts that can run multiple times safely
- [Referential Integrity Basics](Foundations/Referential-Integrity-Basics.md) — Foreign keys, dependencies, and orphan data
- [The Refactorlog and Rename Discipline](Foundations/The-Refactorlog-and-Rename-Discipline.md) — How to rename without data loss
- [SSDT Deployment Safety](Foundations/SSDT-Deployment-Safety.md) — Publish profiles and protection mechanisms
- [Multi-Phase Evolution](Foundations/Multi-Phase-Evolution.md) — Changes requiring multiple sequential releases

---

### [III. Operations](Operations.md)

How to perform specific database operations safely and correctly.

- [Dimension Framework](Operations/Dimension-Framework.md) — Classifying changes by risk and impact
- [Ownership Tiers](Operations/Ownership-Tiers.md) — Self-service to principal escalation
- [SSDT Mechanism Axis](Operations/SSDT-Mechanism-Axis.md) — Pure declarative to multi-phase patterns

#### Operation Reference

- [Operation Reference Index](Operations/Operation-Reference.md)
  - [Working with Entities (Tables)](Operations/Operation-Reference/Entities-Tables.md)
  - [Working with Attributes (Columns)](Operations/Operation-Reference/Attributes-Columns.md)
  - [Working with Keys and References](Operations/Operation-Reference/Keys-and-References.md)
  - [Working with Indexes](Operations/Operation-Reference/Indexes.md)
  - [Working with Lookup Tables](Operations/Operation-Reference/Lookup-Tables.md)
  - [Working with Constraints](Operations/Operation-Reference/Constraints.md)
  - [Structural Changes](Operations/Operation-Reference/Structural-Changes.md)
  - [Audit and Temporal](Operations/Operation-Reference/Audit-and-Temporal.md)
  - [Quick Lookup Tables](Operations/Operation-Reference/Quick-Lookup.md)
  - [Cross-Reference](Operations/Operation-Reference/Cross-Reference.md)

#### Multi-Phase Patterns

- [Multi-Phase Patterns Index](Operations/Multi-Phase-Patterns.md)
  - [Explicit Type Conversion](Operations/Multi-Phase-Patterns/Explicit-Type-Conversion.md)
  - [NULL → NOT NULL on Populated Table](Operations/Multi-Phase-Patterns/Null-to-Not-Null.md)
  - [Add/Remove IDENTITY Property](Operations/Multi-Phase-Patterns/Identity-Property.md)
  - [Add FK with Orphan Data](Operations/Multi-Phase-Patterns/FK-with-Orphan-Data.md)
  - [Safe Column Removal](Operations/Multi-Phase-Patterns/Safe-Column-Removal.md)
  - [Table Split](Operations/Multi-Phase-Patterns/Table-Split.md)
  - [Table Merge](Operations/Multi-Phase-Patterns/Table-Merge.md)
  - [Schema Migration with Backward Compatibility](Operations/Multi-Phase-Patterns/Schema-Migration-Backward-Compatibility.md)

#### Quick References

- [Decision Aids](Operations/Decision-Aids.md) — Flowcharts, checklists, and quick references
- [Anti-Patterns Gallery](Operations/Anti-Patterns-Gallery.md) — Common mistakes and how to avoid them

---

### [IV. Process](Process.md)

How changes move from idea to production.

- [The OutSystems External Entities Workflow](Process/The-OutSystems-External-Entities-Workflow.md) — Synchronization between SSDT and OutSystems
- [Local Development Setup](Process/Local-Development-Setup.md) — Get your environment ready
- [The Change/Release Process](Process/The-ChangeRelease-Process.md) — Step-by-step from classification to deployment
- [The PR Template](Process/The-PR-Template.md) — What to include in pull requests
- [Troubleshooting Playbook](Process/Troubleshooting-Playbook.md) — Common issues and resolutions
- [Escalation Paths](Process/Escalation-Paths.md) — When and how to escalate
- [Capability Development](Process/Capability-Development.md) — Graduation path from observer to dev lead

---

### [V. Reference](Reference.md)

Standards, templates, and resources.

- [SSDT Standards](Reference/SSDT-Standards.md) — Naming conventions and coding standards
- [Templates](Reference/Templates.md) — Reusable code templates
  - [New Table](Reference/Templates/New-Table.md)
  - [Migration Block](Reference/Templates/Migration-Block.md)
  - [Seed Data](Reference/Templates/Seed-Data.md)
  - [Migration Tracking](Reference/Templates/Migration-Tracking.md)
  - [Validation](Reference/Templates/Validation.md)
  - [Incident Report](Reference/Templates/Incident-Report.md)
- [Glossary](Reference/Glossary.md) — Term definitions and OutSystems equivalents
- [Resources](Reference/Resources.md) — Documentation, links, and contacts
- [Contribution Guidelines](Reference/Contribution-Guidelines.md) — How to improve this playbook
- [Changelog](Reference/Changelog.md) — Document evolution history

---

## Getting Help

| Need | Where to go |
|------|-------------|
| Quick question | ssdt-questions channel in Teams |
| PR review | Tag reviewer per tier guidance |
| Escalation | See [Escalation Paths](Process/Escalation-Paths.md) |
| Something's broken | [Troubleshooting](Process/Troubleshooting-Playbook.md), then escalate |
| Playbook feedback | ssdt-playbook channel in Teams or direct to Danny |

---

*This playbook is living documentation. If something is wrong, unclear, or missing — that's a contribution opportunity.*
