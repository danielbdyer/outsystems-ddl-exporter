# 20. The OutSystems → External Entities Workflow

---

## What This Section Covers

This section describes the boundary between OutSystems and SSDT-managed databases. It explains what happens when a project migrates to External Entities, what changes for developers, and how the two systems coordinate.

---

## Context: What External Entities Are

In OutSystems, most entities are "internal" — OutSystems owns the schema, generates the tables, and manages all changes through Service Studio.

**External Entities** are different. They point to tables that exist outside OutSystems — tables we create and manage ourselves in SQL Server using SSDT.

OutSystems can read from and write to External Entities, but it doesn't control their structure. We do.

**Why we use External Entities:**
- Full control over schema design, data types, constraints
- Ability to implement patterns OutSystems doesn't support (temporal tables, complex indexing, partitioning)
- Change Data Capture for audit history
- Standard SQL Server tooling and practices
- Schema versioned in git, deployed through pipelines

---

## The Handoff: When a Project Moves to SSDT

A project migrates from OutSystems-managed entities to External Entities when:
- We need capabilities OutSystems can't provide (CDC, temporal, complex constraints)
- We need tighter control over schema evolution
- The project is designated for the External Entities architecture

**What happens during migration:**

1. **Schema extraction:** Current OutSystems-generated tables are reverse-engineered into SSDT `.sql` files
2. **SSDT project creation:** Tables, indexes, constraints defined declaratively
3. **CDC enablement:** Capture instances created for audit tracking
4. **External Entity creation:** Integration Studio extension created pointing to the tables
5. **OutSystems reconnection:** Applications switch from internal entities to External Entities
6. **Validation:** Data integrity verified, application tested

**After migration:**
- SSDT owns the schema
- OutSystems consumes the schema through External Entities
- All schema changes go through SSDT, then refresh in Integration Studio

---

## What Changes for the OutSystems Developer

### Before (Internal Entities)

| Task | How you did it |
|------|----------------|
| Add an attribute | Service Studio → Edit Entity → Add Attribute → Publish |
| Change data type | Service Studio → Edit Attribute → Change Type → Publish |
| Add an index | Service Studio → Entity Properties → Indexes → Publish |
| See schema changes | Immediate in Service Studio |
| Rollback | Republish previous version |

### After (External Entities)

| Task | How you do it now |
|------|-------------------|
| Add a column | SSDT → Edit table .sql file → PR → Deploy → Integration Studio Refresh → Service Studio Refresh |
| Change data type | SSDT → Classify change → Possibly multi-phase → PR → Deploy → Integration Studio Refresh → Service Studio Refresh |
| Add an index | SSDT → Create index → PR → Deploy (no Integration Studio refresh needed for indexes) |
| See schema changes | Check the SSDT project in git, or query the database directly |
| Rollback | Depends on change type — may require scripted rollback or backup restore |

### The Key Mental Shifts

**Schema changes require process.**
You can't make a quick schema tweak and publish. Changes go through PR review, classification, and staged deployment. This is intentional — it prevents the mistakes that OutSystems's guardrails used to catch for you.

**Two systems must stay synchronized.**
After SSDT deploys, Integration Studio must refresh to see the new schema, then Service Studio must refresh to use it. Forgetting a step creates confusion.

**You own data safety.**
OutSystems validated your changes. Now you must validate them — checking for orphan data, ensuring columns have values, reviewing generated scripts.

---

## The Synchronization Flow

Every schema change follows this flow:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                                                                         │
│   ┌─────────────────┐                                                   │
│   │  1. SSDT        │  Developer makes schema change                    │
│   │     Change      │  - Edit .sql files                                │
│   │                 │  - Create PR                                       │
│   └────────┬────────┘  - Merge after review                             │
│            │                                                            │
│            ▼                                                            │
│   ┌─────────────────┐                                                   │
│   │  2. Pipeline    │  Automated deployment                             │
│   │     Deploy      │  - Build dacpac                                   │
│   │                 │  - Deploy to target environment                   │
│   └────────┬────────┘  - Run pre/post scripts                           │
│            │                                                            │
│            ▼                                                            │
│   ┌─────────────────┐                                                   │
│   │  3. Database    │  Schema is now updated                            │
│   │     Updated     │  - New columns exist                              │
│   │                 │  - Constraints applied                            │
│   └────────┬────────┘  - Indexes created                                │
│            │                                                            │
│            │         ╔═══════════════════════════════════════════════╗  │
│            │         ║  OutSystems doesn't know yet.                 ║  │
│            │         ║  The External Entity definition is stale.     ║  │
│            │         ╚═══════════════════════════════════════════════╝  │
│            ▼                                                            │
│   ┌─────────────────┐                                                   │
│   │  4. Integration │  Developer refreshes External Entity              │
│   │     Studio      │  - Connect to database                            │
│   │     Refresh     │  - Refresh entity definition                      │
│   └────────┬────────┘  - Publish extension                              │
│            │                                                            │
│            ▼                                                            │
│   ┌─────────────────┐                                                   │
│   │  5. Service     │  Developer updates application                    │
│   │     Studio      │  - Refresh extension reference                    │
│   │     Update      │  - Update logic for new attributes                │
│   └────────┬────────┘  - Publish application                            │
│            │                                                            │
│            ▼                                                            │
│   ┌─────────────────┐                                                   │
│   │  6. Complete    │  Schema change is fully propagated                │
│   │                 │  - Database updated                               │
│   │                 │  - External Entity updated                        │
│   └─────────────────┘  - Application using new schema                   │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Integration Studio: Step by Step

### Opening the Extension

1. Launch Integration Studio (from Start menu or Service Studio → Edit → Open Integration Studio)
2. Connect to your environment
3. Open the extension containing the External Entities (File → Open, or download from environment)

### Refreshing an External Entity

1. In the extension, navigate to the **Entities** folder
2. Find the External Entity that corresponds to the changed table
3. Right-click → **Refresh**
4. Integration Studio connects to the database and compares schemas

### What the Refresh Shows

| Integration Studio shows... | What happened | What to do |
|-----------------------------|---------------|------------|
| New attribute detected | SSDT added a column | Accept. Configure Is Mandatory, data type. |
| Attribute removed | SSDT dropped a column | Accept. Update application to remove usage. |
| Attribute type changed | SSDT changed column type | Accept. Verify mapping is correct. |
| Multiple changes | Several columns affected | Review each carefully. Accept when understood. |
| No changes detected | Schema matches extension | Nothing to do — already in sync. |

### Accepting Changes

1. Review each detected change
2. For new attributes: Set **Is Mandatory** appropriately (match your NOT NULL constraint)
3. For new attributes: Verify **Data Type** mapping is correct
4. Accept the changes
5. **Save** the extension

### Publishing the Extension

1. Click **1-Click Publish** (or Publish button)
2. Wait for publication to complete
3. Note any warnings or errors

### Returning to Service Studio

1. Open Service Studio
2. Open the module(s) using this External Entity
3. In the module tree, find the extension reference
4. Right-click → **Refresh**
5. Service Studio pulls the updated entity definition
6. Update application logic to use new attributes (if any)
7. Publish the application

---

## Coordinating Releases

### Additive Changes (Safe Order)

When adding columns, indexes, or tables:

```
SSDT deploys first → Database has new structure
                  → Integration Studio can see it
                  → Service Studio can use it
OutSystems deploys second → Application uses new structure
```

OutSystems can't use what doesn't exist yet. SSDT must lead.

### Breaking Changes (Requires Coordination)

When removing or renaming columns:

```
Release N:
  - SSDT: Add new column (if rename) or prepare for removal
  - OutSystems: Migrate to new column / stop using old column
  
Release N+1:
  - SSDT: Remove old column
  - OutSystems: Already not using it
```

You cannot remove a column that OutSystems is still using. The application will break.

### Timing Considerations

| Environment | Coordination needs |
|-------------|-------------------|
| Dev | Minimal — developer does both SSDT and OutSystems |
| Test | Moderate — ensure SSDT deployed before testing |
| UAT | Higher — coordinate with test schedule |
| Prod | Highest — scheduled releases, explicit handoff |

---

## Common Synchronization Issues

| Issue | Symptom | Cause | Resolution |
|-------|---------|-------|------------|
| External Entity missing column | Service Studio doesn't show new attribute | Integration Studio not refreshed | Refresh in Integration Studio, then Service Studio |
| Type mismatch errors | Runtime errors on data access | Integration Studio mapped type differently | Check mapping in Integration Studio, adjust if needed |
| "Column does not exist" at runtime | Application crashes on data access | SSDT deploy didn't complete, or wrong environment | Verify SSDT deployment, check environment connection |
| Phantom columns | Integration Studio shows columns you didn't add | Connected to wrong environment | Check database connection string |
| Refresh shows no changes | Expected to see new column | SSDT deploy didn't run, or you're in wrong branch | Verify pipeline completed, verify correct database |

---

## Checklist: After Every SSDT Deployment

For deployments that change table structure:

- [ ] Verify SSDT pipeline completed successfully
- [ ] Verify changes visible in database (query in SSMS if unsure)
- [ ] Open Integration Studio
- [ ] Connect to correct environment
- [ ] Refresh affected External Entity(ies)
- [ ] Review and accept changes
- [ ] Publish extension
- [ ] Open Service Studio
- [ ] Refresh extension reference
- [ ] Update application logic if needed
- [ ] Publish application (if changes made)
- [ ] Verify application works with new schema

---

