# 28. Templates

Reusable code templates for common SSDT operations.

## How to Use These Templates

Copy the template, fill in your specifics, and adjust as needed. All templates follow idempotency principles — they can run multiple times safely.

---

## Available Templates

### Schema Templates

- [28.01: New Table](28.01-New-Table.md) — Boilerplate for creating tables with standard columns
- [28.04: Migration Tracking](28.04-Migration-Tracking.md) — Table for tracking one-time migrations

### Script Templates

- [28.02: Migration Block](28.02-Migration-Block.md) — Idempotent post-deployment migration pattern
- [28.03: Seed Data](28.03-Seed-Data.md) — MERGE-based seed data for lookup tables
- [28.05: Validation](28.05-Validation.md) — Pre-deployment validation checks
- [28.06: CDC Scripts](28.06-CDC-Scripts.md) — Enable/disable CDC capture instances

### Process Templates

- [28.07: Incident Report](28.07-Incident-Report.md) — Post-mortem template for production issues

---

## General Principles

All deployment scripts should be:
- **Idempotent** — Safe to run multiple times
- **Defensive** — Check conditions before acting
- **Logged** — Print what they're doing
- **Tested** — Deployed to dev/test before prod

---

[← Back to Reference](../index.md)
