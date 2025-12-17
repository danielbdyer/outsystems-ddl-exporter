# 2. The Big Picture

---

## Why We're Here

We're migrating OutSystems applications to use **External Entities** — database tables managed outside of OutSystems, in SQL Server, using SSDT.

**Before:** OutSystems owned the schema. Changes happened in Service Studio. The database was a black box.

**After:** We own the schema. Changes happen in SSDT. The database is explicitly managed, version-controlled, and deployed through our pipeline.

This gives us:
- Full control over schema design, indexing, constraints
- Ability to support complex data patterns OutSystems can't express
- Change Data Capture for audit history across ~200 tables
- Standard SQL Server tooling and practices

It costs us:
- A new mental model (declarative, not imperative)
- Additional process (PR, review, staged deployment)
- CDC management overhead on schema changes
- Learning curve for the team

This playbook exists to minimize the cost and maximize the benefit.

---

## How Database Changes Flow

```
┌──────────────────────────────────────────────────────────────────────────┐
│                                                                          │
│   Developer identifies need for schema change                            │
│                           │                                              │
│                           ▼                                              │
│   ┌─────────────────────────────────────────┐                            │
│   │  CLASSIFY                               │                            │
│   │  - What tier? (1-4)                     │                            │
│   │  - What SSDT mechanism?                 │                            │
│   │  - Multi-phase needed?                  │                            │
│   │  - CDC impact?                          │                            │
│   └─────────────────────────────────────────┘                            │
│                           │                                              │
│                           ▼                                              │
│   ┌─────────────────────────────────────────┐                            │
│   │  IMPLEMENT                              │                            │
│   │  - Edit .sql files (declarative)        │                            │
│   │  - Add pre/post scripts (if needed)     │                            │
│   │  - Update refactorlog (if rename)       │                            │
│   └─────────────────────────────────────────┘                            │
│                           │                                              │
│                           ▼                                              │
│   ┌─────────────────────────────────────────┐                            │
│   │  TEST LOCALLY                           │                            │
│   │  - Build project                        │                            │
│   │  - Deploy to local SQL Server           │                            │
│   │  - Verify change works as expected      │                            │
│   │  - Review generated script              │                            │
│   └─────────────────────────────────────────┘                            │
│                           │                                              │
│                           ▼                                              │
│   ┌─────────────────────────────────────────┐                            │
│   │  OPEN PR                                │                            │
│   │  - Use PR template                      │                            │
│   │  - Tag appropriate reviewers (by tier)  │                            │
│   │  - Include classification rationale     │                            │
│   └─────────────────────────────────────────┘                            │
│                           │                                              │
│                           ▼                                              │
│   ┌─────────────────────────────────────────┐                            │
│   │  REVIEW & MERGE                         │                            │
│   │  - Reviewer validates classification    │                            │
│   │  - Reviewer checks for gotchas          │                            │
│   │  - Merge triggers pipeline              │                            │
│   └─────────────────────────────────────────┘                            │
│                           │                                              │
│                           ▼                                              │
│   ┌─────────────────────────────────────────┐                            │
│   │  DEPLOY                                 │                            │
│   │  - Pipeline deploys to dev              │                            │
│   │  - Promote to test → UAT → prod         │                            │
│   │  - Verify at each stage                 │                            │
│   └─────────────────────────────────────────┘                            │
│                                                                          │
│   If multi-phase: repeat for subsequent phases                           │
│                                                                          │
└──────────────────────────────────────────────────────────────────────────┘
```

---

## Where SSDT Fits with OutSystems

```
┌─────────────────────────────────────────────────────────────────────────┐
│  OutSystems (Service Studio)                                            │
│  ┌────────────────────────────────────────────────────────────────────┐ │
│  │  Application logic, screens, integrations                          │ │
│  │                                                                    │ │
│  │  External Entities ──────────────────┐                             │ │
│  │  (reference to external tables)      │                             │ │
│  └──────────────────────────────────────│─────────────────────────────┘ │
└─────────────────────────────────────────│───────────────────────────────┘
                                          │
                                          │ References (via Integration Studio)
                                          │
                                          ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  SQL Server Database                                                    │
│  ┌────────────────────────────────────────────────────────────────────┐ │
│  │  Tables, Views, Indexes, Constraints                               │ │
│  │  Managed by: SSDT                                                  │ │
│  │                                                                    │ │
│  │  CDC Capture Tables                                                │ │
│  │  (auto-generated, track changes)                                   │ │
│  └────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────┘
                                          │
                                          │ Version controlled in
                                          │
                                          ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  Git Repository                                                         │
│  ┌────────────────────────────────────────────────────────────────────┐ │
│  │  /Tables/*.sql     (declarative schema)                            │ │
│  │  /Views/*.sql                                                      │ │
│  │  /Scripts/         (pre/post deployment)                           │ │
│  │  *.refactorlog     (rename tracking)                               │ │
│  │  *.sqlproj         (project definition)                            │ │
│  └────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────┘
```

**Key insight:** OutSystems *consumes* the schema but doesn't *own* it. When you need a schema change:

1. Make it in SSDT (this playbook)
2. Deploy to the database
3. Refresh the External Entity in Integration Studio
4. Use the updated entity in Service Studio

---

## The Change Data Capture Constraint

Almost 200 tables have CDC enabled. This powers our Change History feature — showing end users who changed what, when.

**Why this matters for schema changes:**

CDC capture instances are schema-bound. When you change a table's structure, you often need to:
- Recreate the capture instance (accepting a history gap), or
- Create a new instance alongside the old one (no gap, more complexity)

In **development**: We accept gaps. Velocity matters more than audit completeness.

In **production**: We cannot accept gaps. Schema changes on CDC-enabled tables require multi-phase treatment.

Every schema change on a CDC-enabled table is at least Tier 2. See [11. CDC and Schema Evolution](#) for full guidance.

---

## What Success Looks Like

**For you (individual contributor):**
- You can classify a change and know whether to proceed or escalate
- You can execute Tier 1-2 changes confidently
- You know where to look when you're uncertain
- You've never caused data loss from a schema change

**For the team:**
- PRs are properly classified and reviewed
- Escalations happen at the right time, not too early or too late
- No one is a bottleneck — knowledge is distributed
- The playbook is improving because people contribute to it

**For the system:**
- Zero data loss incidents from SSDT deployments
- Change History feature has complete audit trails in production
- Deployments are predictable and recoverable
- Schema matches source control — no drift

**For Danny:**
- The team owns this process; Danny is not a bottleneck
- Onboarding new team members is structured, not ad-hoc
- Incidents are rare, and when they happen, they become playbook improvements
- The team's SSDT capability is growing over time

---

## What This Playbook Doesn't Cover

- **OutSystems development** — Service Studio, application logic, etc.
- **General SQL Server administration** — backups, security, performance tuning (beyond indexes)
- **Pipeline/DevOps configuration** — Azure DevOps setup, deployment agents
- **Application-level data access** — ORMs, query patterns in application code

Those are important, but they're not this playbook's scope.

---

