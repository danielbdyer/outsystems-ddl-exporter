---

## 0. Orientation & Map (STR.MAP)

* **STR.MAP.001 Purpose & Scope.** External Entities cutover + SSDT adoption mid‑stream; unify DB‑as‑code with OutSystems as consumer; reduce blast radius; create repeatable cadence. [S:1][S:5][S:6][S:8][S:9][S:10]
* **STR.MAP.002 Reading Order.** (1) Strategy → (2) Governance → (3) SSDT Core → (4) Playbooks (Week‑1, Month‑1, Advanced) → (5) Decisions/Patterns → (6) OutSystems Integration → (7) CI/CD → (8) Training & Survey → (9) Ops Checklists → (10) Docs/Artifacts → (11) Quality → (12) Glossary.
* **STR.MAP.003 Roles & Audiences.** EM/Staff (architecture, cadence), Dev Leads (review, coordination), ICs (execution), Ops (deploy), QA (verification), PM (timeline), Stakeholders (executive summary). [S:2][S:3][S:4][S:5]

---

## 1. Strategy & First Principles (STR)

* **STR.FP.101 North Star.** **One place of change** (SSDT/DACPAC); state‑based deploys; OutSystems refreshed post‑deploy; views/contracts only where risk compels. [S:1][S:5][S:9][S:10]
* **STR.FP.102 Risk Posture.** Prefer additive changes; phase tightening; orchestrate breaking changes via release trains; forbid ad‑hoc renames/drops in prod. [S:1][S:3][S:5]
* **STR.FP.103 Boundary Surfaces.** Consider contracts (views) when ≥2 of: high fan‑out, churn, security shape, perf shape, ownership boundary, evolving keys. Otherwise keep direct binds. [S:1][S:9]
* **STR.FP.104 Environment Flow.** Dev → Test/UAT → Prod with windowed deploys; publish profiles encode policy; Integration Studio refresh windows aligned to trains. [S:1][S:2][S:5]
* **STR.FP.105 Learning by Doing.** Week‑1 narrated deploys + office hours; Month‑1 optimization sprints; advanced playbooks on refactors and zero‑downtime patterns. [S:2][S:3][S:4]

---

## 2. Governance: Release Trains, Swim Lanes, Standards (GOV)

* **GOV.TRN.201 Daily Train Rhythm.** 2:00p PR cutoff → 3:00p DACPAC deploy → 3:30p Integration Studio refresh → announce, verify, and close loop. [S:2][S:5]
* **GOV.TRN.202 Week‑1 “Narrated Ops.”** Extended standup; narrated deploys; FAQ capture; Friday retro; publish a living “Week‑1 Survival” guide. [S:2][S:3]
* **GOV.SWL.203 Capability Ladder.** Junior (safe additive) → Mid (defaults/NOT NULL/FKs) → Senior (complex refactors) → Staff/Lead (architecture, cadence, metrics). [S:5][S:6]
* **GOV.STD.204 PR Template & Safety Gates.** Typed PR categories; breaking‑change declaration; generated diff report attached; forbidden‑drop guard; idempotent post‑deploy proof (double‑run). [S:2][S:3][S:5][S:6]
* **GOV.MET.205 Metrics.** Lead time to train; refresh SLA; defect escape; forbidden‑drop blocks; refactorlog usage; idempotency coverage; rollback rate. [S:1][S:5]

---

## 3. SSDT Core & DB‑as‑Code (DBX)

* **DBX.PROJ.301 Project Anatomy.** `.sqlproj`, schema folders, `RefactorLog`, `PreDeploy.sql`, `PostDeploy.sql`, publish profiles, `CODEOWNERS`. [S:6][S:8]
* **DBX.DEP.302 State‑Based Deploy.** Build → DACPAC → compare → script; use pre/post for delicate transitions; never rely on ad‑hoc prod DDL. [S:6][S:8]
* **DBX.CMP.303 Schema Compare/Publish.** Project↔DB diff; review generated script; parameterize; safe publish via pipeline. [S:6]
* **DBX.LOC.304 Dev DB Strategy.** Local for experimentation; shared Dev target for integration; seed data idempotently. [S:2][S:6]
* **DBX.SEED.305 Reference Data.** `MERGE` with deterministic IDs; environment gates via sqlcmd vars; double‑deploy verification. [S:2][S:3]

---

## 4. Playbooks: Week‑1 (WRK.W1)

* **WRK.W1.401 “Hello SSDT.”** Install VS + SSDT; clone repo; build DACPAC; publish to Dev; verify schema; run Integration Studio refresh; smoke test. [S:2][S:6]
* **WRK.W1.402 Safe Additive Change.** Add nullable column → publish → IS refresh → update consumers → add coverage index if needed. [S:2]
* **WRK.W1.403 Tightening (Phased NOT NULL).** Add column with DEFAULT + backfill → verify consumers → enforce NOT NULL next train. [S:2][S:3]
* **WRK.W1.404 Foreign Keys & Indexes.** Create FK with supporting index; watch cardinality/selectivity; monitor plan regressions. [S:2]
* **WRK.W1.405 Rename With RefactorLog.** Use SSDT rename; forbid in‑place rename SQL; track via `RefactorLog`; coordinate IS mapping. [S:2][S:3]
* **WRK.W1.406 Static→Lookup Migration.** Create DB lookup table; seed; wire External Entity; switch consumers; retire static after sunset. [S:2]
* **WRK.W1.407 Troubleshooting Fastpaths.** Non‑idempotent reruns, NOT NULL failures, `PostDeploy` didn’t fire, IS drift; provide playcard with signals → fix. [S:3]

---

## 5. Playbooks: Month‑1 Optimization & Advanced (WRK.M1)

* **WRK.M1.501 Multi‑Table Refactors.** Split/merge tables using views as compatibility layer; backfill; dual‑write window if needed; cutover. [S:4]
* **WRK.M1.502 Type Changes at Scale.** Introduce parallel column → copy/backfill in batches → migrate consumers → drop legacy later. [S:4]
* **WRK.M1.503 Pre/Post Deploy Patterns.** Use `PreDeploy` for guards/backups; `PostDeploy` for idempotent data fixes, grants, and cross‑schema sync. [S:4]
* **WRK.M1.504 Performance Engineering.** Index design (covering, filtered), SARGability, stats mgmt, plan capture regressions; perf playcards. [S:4]
* **WRK.M1.505 Merge Conflict Heuristics.** Additive vs overlapping vs divergent definitions; resolution protocol and examples. [S:4]
* **WRK.M1.506 Zero‑Downtime Tactics.** Expand/contract, blue‑green shadow, feature flags at consumer layer; safety windows. [S:4]

---

## 6. Decision Frameworks (REF.DEC)

* **REF.DEC.601 Direct Bind vs Contract (View).** Use decision matrix (fan‑out, churn, security, perf, ownership, key evolution). Default: direct; escalate to contract when ≥2 drivers present. [S:9][S:10]
* **REF.DEC.602 Phased Tightening.** Make invisible changes first (new nullable with DEFAULT → backfill) then enforce. [S:2][S:3]
* **REF.DEC.603 Rename vs New Column.** Prefer additive + alias in view; plan sunset windows; only *tool‑based* renames allowed. [S:2][S:3]
* **REF.DEC.604 Static vs DB Lookup.** Keep Static for low churn + tiny enums; move to DB when shared, audited, or needs joins/perf. [S:2]
* **REF.DEC.605 When to Use Pre/Post.** Pre: invariants/guards; Post: idempotent data ops, grants, seeds, sync; both: double‑run proof. [S:4]

---

## 7. OutSystems Integration Flow (OSX)

* **OSX.FLO.701 Change Sequence.** DB deploy first → Integration Studio refresh/publish → Service Studio refresh → smoke test. [S:1][S:2]
* **OSX.EXT.702 Extension Granularity.** Domain‑level extensions; owner map; announce touched entities; SLA ≤30m after train. [S:2]
* **OSX.MOD.703 Modeling‑First Pattern.** Use stub/facade to unblock UI while DB change is in flight; wire to real entity after refresh. [S:1][S:2]
* **OSX.STC.704 Static→External Checklist.** Table, seed, external entity, map types, swap references, deprecate static, sunset. [S:2][S:3]

---

## 8. CI/CD, Profiles, and Safety Nets (CICD)

* **CICD.PFL.801 Publish Profiles.** `BlockOnPossibleDataLoss=True`, `DropObjectsNotInSource=False`, tuned timeouts, excludes, sqlcmd vars. [S:6]
* **CICD.SEC.802 Security & Least Privilege.** Build agent permissions, constrained deployment identity, auditable change logs. [S:6]
* **CICD.PIP.803 PR Pipeline.** Build DACPAC; generate diff report; forbidden‑drop scanner; attach artifacts to PR; fail fast on blockers. [S:2][S:5][S:6]

---

## 9. Operational Checklists & Runbooks (OPS)

* **OPS.DEP.901 Deployer Checklist.** Pre: backups/snapshots, lockout calendar, announce; During: observe script, verify rows affected; Post: IS refresh, smoke tests, announce green. [S:2][S:3]
* **OPS.PR.902 Developer PR Checklist.** Naming, types, FK + supporting index, idempotent seeds, local build green, post‑deploy proof. [S:2][S:5]
* **OPS.W1.903 Week‑1 Ops Runbook.** Narrated deploys, office hours, FAQ capture, retro; publish updates next morning. [S:2][S:3]
* **OPS.TS.904 Troubleshooting Index.** Map symptom → likely cause → fix (NOT NULL failures, drift, non‑idempotent scripts, permission errors). [S:3]

---

## 10. Training Roadmap & Cohorts (TRN)

* **TRN.FND.1001 Foundations (Weeks 1–2).** Install toolchain; import schema; build/publish; basic vocabulary (DDL, normalization, transactions); guided labs. [S:6][S:8]
* **TRN.INT.1002 Intermediate (Month 1).** Multi‑table design; FKs + indexes; pre/post patterns; merge conflict resolution; code review rituals. [S:4][S:6][S:8]
* **TRN.ADV.1003 Advanced (Ongoing).** Large refactors; zero‑downtime patterns; performance engineering; automated reports; enterprise coordination. [S:4][S:8]
* **TRN.MAT.1004 Curated Materials.** Essentials‑first, behavior‑oriented resources (SSDT projects, publish profiles, schema compare, pre/post, data‑motion). [S:6][S:8]

---

## 11. Readiness Survey (SRV)

* **SRV.STR.1101 Survey Structure.** Sections: Prior SQL exposure; Tooling analogs; SSDT core skills; Safe refactors; CI/CD; Troubleshooting; External Entities/OutSystems. [S:7]
* **SRV.INS.1102 Scoring & Cohorts.** Map responses to Foundation/Intermediate/Advanced tracks; auto‑assign labs; surface learning deltas week‑over‑week. [S:7]
* **SRV.FRM.1103 MS Forms Blueprint.** Question bank with Likert + short‑form comments; per‑section confidence scores; link to lab recommendations. [S:7]

---

## 12. Documentation Library & Templates (DOC)

* **DOC.RDM.1201 Collated README Skeleton.** Start‑here, daily train, how to submit PR, Integration Studio refresh, anti‑patterns, troubleshooting, playbooks index. [S:2][S:3][S:5][S:6]
* **DOC.TPL.1202 PR Template.** Change type, risk class, affected domains, pre/post snippets, verification steps, diff report link. [S:2][S:5]
* **DOC.TPL.1203 Deployer Runbook.** Pre/During/Post checklist; rollback plan; comms template; success criteria. [S:2][S:3]
* **DOC.TPL.1204 View/Contract Policy.** Decision table + examples; alias strategy; sunset schedule template. [S:9][S:10]

---

## 13. Quality, Risk, and Anti‑Patterns (QLT)

* **QLT.ANP.1301 Non‑Idempotent Post‑Deploy.** Use `MERGE` / `IF NOT EXISTS`; prove double‑run; keep data motion repeatable. [S:3][S:4]
* **QLT.ANP.1302 Forbidden Drops/Renames.** Scanner blocks; tool‑based rename only; plan for additive aliasing; add sunset plan. [S:2][S:5]
* **QLT.ANP.1303 Hard‑coded Cross‑DB.** Replace with sqlcmd vars or synonyms/linked servers with policy. [S:3]
* **QLT.ANP.1304 No‑Txn Multi‑Insert.** Wrap in transactions; TRY/CATCH; visible logging; measurable invariants. [S:3]
* **QLT.SIG.1305 Quality Signals.** Safety blocks, RefactorLog entries, idempotent coverage, defect trend, rollback need frequency. [S:5]

---

## 14. Glossary (GLS)

* **GLS.1401 SSDT** (SQL Server Data Tools): declarative DB‑as‑code project that compiles to DACPAC.
* **GLS.1402 DACPAC**: compiled model for schema compare/publish.
* **GLS.1403 RefactorLog**: renaming map ensuring safe scripted renames.
* **GLS.1404 IS/SS**: Integration Studio / Service Studio (OutSystems tools).
* **GLS.1405 Contract (View)**: compatibility surface insulating consumers.
* **GLS.1406 Expand/Contract**: zero‑downtime refactor method: expand schema → migrate → contract.

---

## 15. Traceability: Which Sections Came From Which Source Notes

* **S:1 →** Strategy, sequence, modeling‑first, contract minimalism, metrics.
* **S:2 →** Week‑1 full playbooks, trains, checklists, static→lookup, safe adds.
* **S:3 →** Troubleshooting, enforcement patterns, anti‑patterns, runbooks.
* **S:4 →** Month‑1 advanced scenarios: multi‑table, type changes, zero‑downtime.
* **S:5 →** Release train policy, swim lanes, standards, metrics.
* **S:6 →** SSDT rationale, project anatomy, publish profiles, behavior‑oriented links.
* **S:7 →** Microsoft Forms survey structure and cohort mapping.
* **S:8 →** Upskilling notes and curriculum scoping.
* **S:9 →** Direct vs contract decision matrix (v1).
* **S:10 →** Direct vs contract decision matrix (v2 refinements).

---

### Appendix A — Collated README (export‑ready skeleton)

```
# External Entities Cutover & SSDT Adoption — Team Handbook

## 1. Quick Start
- Toolchain install → clone → build DACPAC → publish to Dev → IS refresh → smoke test.
- Daily train: 2:00p cutoff → 3:00p publish → 3:30p IS refresh.

## 2. How We Change the DB
- State‑based deploys (DACPAC). No ad‑hoc prod DDL.
- Additive first; tighten later. Use RefactorLog for renames.
- Seeds via MERGE, idempotent, double‑run proof.

## 3. Decision Tables
- Direct bind vs Contract (view): matrix + examples.
- Static vs DB lookup.
- When to pre vs post‑deploy scripts.

## 4. Playbooks
- Week‑1: safe add, phased NOT NULL, FK + index, static→lookup.
- Month‑1: split/merge, type changes, zero‑downtime, perf.
- Troubleshooting: symptom → fix.

## 5. PR & Deploy
- PR template, diff report, forbidden‑drop guard.
- Deployer runbook (Pre/During/Post) + rollback plan.

## 6. OutSystems Flow
- DB first → IS refresh → SS refresh → smoke.

## 7. Training & Survey
- Cohorts (FND/INT/ADV). Survey link.

## 8. Glossary
- SSDT, DACPAC, RefactorLog, Contract, Expand/Contract.
```

### Appendix B — Checklists (printable)

* **Deployer**: backups → publish → verify → IS refresh → smoke → announce.
* **Developer PR**: naming → types → FK+index → seeds → local build → post‑deploy proof → attach diff.

### Appendix C — Policy Snippets

* **Publish Profile**: `BlockOnPossibleDataLoss=True`, `DropObjectsNotInSource=False`.
* **Idempotent MERGE**: pattern + comment banner explaining double‑run invariants.

Awesome—here’s a **significantly expanded** (≈2–4×) rewrite of the opening sections, written directly to the *impacted development team* (ICs, Leads, Ops), with exhaustive coverage and concrete, do-this-not-that guidance. I’ve kept a **map-first structure**, but each node now carries **operational depth**, **edge cases**, and **ready-to-run** examples.

---

# 1) Executive Overview & Problem Statement (Developer-Facing)

## 1.1 Context (What’s changing and why it matters to you)

* We’re mid-development converting ~100+ **OutSystems internal entities** (plus many **static entities**) into **External Entities** backed by **on-prem SQL Server**.
* In parallel, we’re moving from ad-hoc DB edits to **SSDT (SQL Server Data Tools)** with **state-based deployments** (DACPAC).
* CI/CD is anchored in **Azure DevOps** with **publish profiles** enforcing **no data-lossing operations** in Prod by default.
* The environment flow is **Dev → Test/UAT → Prod**, with **tight weekend and daily windows** for DB publishes and **Integration Studio** refreshes.

**What this *means* for you:**

* Schema changes are authored in a **.sqlproj** (SSDT) and **published** via a controlled pipeline—**not** by running ad-hoc T-SQL in Prod.
* OutSystems modules obtain updated shapes **after** the daily publish via **Integration Studio refresh + republish**.
* We bias toward **additive** schema moves that don’t break consumers; “tightening” follows in later trains.

## 1.2 Core Problem (The thing we must solve without slowing product work)

We must migrate a large surface area **without breaking consumers** or creating a “frozen app” period. We also need to **up-skill** a team new to SSDT—*while shipping*.

**Risks to manage:**

* **Consumer breakage:** renames or NOT NULL constraints landing before consumers adapt.
* **Data motion hazards:** backfills that aren’t idempotent; partial writes; long-running locks.
* **Integration drift:** OutSystems entity models not refreshed promptly after DB change windows.
* **Team variance:** mixed SSDT fluency; “works on my machine” seeds; PRs lacking reviewable diffs.

## 1.3 Thesis (The operating compact)

* **One place of change:** SSDT is the source of truth; OutSystems consumes it.
* **Add → Backfill → Tighten:** prefer invisible moves first; enforce constraints later.
* **Views-as-contract sparingly:** introduce views to stabilize hot, high-fan-out surfaces; otherwise bind directly for simplicity.
* **Modeling-first delivery:** unblock UI/API by modeling the intended shape; switch to real External Entities after the DB publish window.

## 1.4 Day-in-the-life (The daily train)

* **2:00 pm** PR cutoff → **3:00 pm** DACPAC publish (Dev/Test/UAT as applicable) → **3:30 pm** Integration Studio refresh & publish → **announce** impacted domains → quick **smoke** (happy paths).
* Off-cycle publishes are **break-glass** only.

## 1.5 How we’ll know it’s working (Signals you can see)

* **Flow:** PR lead time trending down; fewer off-cycle requests; Integration Studio refresh completed within SLA.
* **Safety:** publish profile blocks “possible data loss”; RefactorLog entries present when you rename; MERGE seeds pass double-run tests.
* **Quality/Learning:** defect escape rate drops; fewer “drift” issues; office-hour questions taper; survey shows cohort progression.

## 1.6 What’s *explicitly* not the goal

* We are **not** building a universal view layer or a bespoke migration framework.
* We are **not** freezing all change until a “perfect” schema emerges.
* We are **not** tolerating ad-hoc Prod scripts. (Dev/ops velocity comes from standardization.)

---

# 2) Strategy & First Principles (Developer-Facing, with Concrete Mechanics)

> **Mantra:** *Add now, tighten later; measure twice, script once; refresh consumers fast; narrate the first weeks.*

## 2.1 “One Place of Change”: SSDT/DACPAC as the source of truth

**Do:**

* Structure `.sqlproj` with folders: `Tables/`, `Views/`, `Programmability/`, plus `PreDeploy.sql`, `PostDeploy.sql`, and **publish profiles** per env.
* Use **Schema Compare** (project ↔ target) to generate scripts; **review diffs** in PRs (attach reports).
* Keep **RefactorLog** current via tool-driven renames (don’t hand-write `sp_rename`).

**Don’t:**

* Don’t run manual DDL in Prod.
* Don’t drop objects not in source (`DropObjectsNotInSource=False` in Prod).
* Don’t hand-craft destructive scripts; let the model drive.

**Publish profile pins (Prod-like):**

```xml
<BlockOnPossibleDataLoss>True</BlockOnPossibleDataLoss>
<DropObjectsNotInSource>False</DropObjectsNotInSource>
<IncludeTransactionalScripts>True</IncludeTransactionalScripts>
<CommandTimeout>1200</CommandTimeout>
```

## 2.2 Additive-First, Tighten-Later (the expand/contract doctrine)

**Pattern:**

1. **Add**: introduce the future column/table in a non-breaking way (nullable/wider type).
2. **Backfill**: populate safely (idempotent MERGE or batched updates, with row-count logging).
3. **Tighten**: enforce NOT NULL / CHECK / FK once consumers are aligned.

**Example: add and later enforce NOT NULL with DEFAULT**

```sql
-- Add (in model)
ALTER TABLE dbo.Customer ADD Email NVARCHAR(320) NULL;

-- Backfill (PostDeploy, idempotent)
MERGE dbo.Customer AS T
USING (SELECT CustomerId FROM dbo.Customer) AS S
ON (T.CustomerId = S.CustomerId)
WHEN MATCHED AND T.Email IS NULL
THEN UPDATE SET Email = CONCAT('unknown+', CAST(T.CustomerId AS NVARCHAR(20)), '@example.com');

-- Tighten (next train)
ALTER TABLE dbo.Customer ALTER COLUMN Email NVARCHAR(320) NOT NULL;
```

**Why this works:**

* No break at introduction; consumers can adapt; 2nd train enforces quality.

## 2.3 Data motion: Idempotency & safety

**Rules of thumb:**

* **Idempotent by construction**: design PostDeploy so reruns don’t harm (MERGE with natural keys, or `IF NOT EXISTS` guards).
* **Transactions + TRY/CATCH**: wrap multi-step fixes to avoid partial application.
* **Measure**: `@@ROWCOUNT` or output clauses → print/log row counts.

**Template:**

```sql
BEGIN TRY
  BEGIN TRAN;
  -- Example MERGE seed
  MERGE dbo.State AS T
  USING (VALUES
    ('WA','Washington'),
    ('OR','Oregon')
  ) AS S(Abbr,Name)
  ON (T.Abbr = S.Abbr)
  WHEN NOT MATCHED BY TARGET THEN
    INSERT(Abbr, Name) VALUES (S.Abbr, S.Name)
  WHEN MATCHED AND ISNULL(T.Name,'') <> S.Name THEN
    UPDATE SET Name = S.Name;
  PRINT CONCAT('Seeded/updated rows: ', @@ROWCOUNT);
  COMMIT TRAN;
END TRY
BEGIN CATCH
  IF XACT_STATE() <> 0 ROLLBACK TRAN;
  THROW;
END CATCH
```

## 2.4 Renames & refactors: **Use RefactorLog or parallel columns**

**Allowed rename approach:**

* In SSDT/VS, **rename the object** via the designer so it writes a **RefactorLog** entry.
* Validate schema compare shows a **rename**, not drop/create.

**High-risk cases:** large tables, critical columns, heavily joined names. Prefer **parallel columns**:

* Add `NewColumn`; backfill; shift consumers; retire `OldColumn` later.

**Anti-pattern to avoid:**
`sp_rename` in PostDeploy for live Prod. It breaks drift detection and can silently cause drops/recreates.

## 2.5 Direct bind vs View-as-Contract (use a **decision matrix**)

Default to **direct External Entity** bindings. Use a **contract view** when **≥2** are true:

* Many consumers (fan-out);
* High churn expected in base tables;
* Security shaping (subset/rename/exclude);
* Perf shaping (computed/denormalized aliases);
* Cross-team boundary ownership;
* Evolving primary keys or name semantics.

**Contract pattern (example):**

```sql
CREATE VIEW vCustomer_Contract AS
SELECT
  c.CustomerId       AS CustomerId,
  c.Email            AS Email,          -- stable alias
  c.GivenName        AS FirstName,      -- legacy name preserved as alias
  c.FamilyName       AS LastName,
  COALESCE(c.Status,'Active') AS Status
FROM dbo.Customer c;
```

Bind External Entities to the **view**, not the base, for hot domains. Keep the count of contract views **focused** (e.g., 20–40).

## 2.6 Modeling-first delivery (unblock the app)

**Why:** prevent UI/API stalls while schema work is in flight.
**How:**

* In OutSystems, create service modules with **stub server actions** that match the intended contract (shape).
* After the **daily publish**, wire those actions to **External Entities** based on the real schema.
* Keep a **toggle** or clear branch strategy so the switch is safe and visible.

**Common traps and fixes:**

* *Trap:* devs build against static entities and forget to swap.
  *Fix:* add a **“contract readiness” checklist** to PRs and a **“swap audit”** item after each refresh window.
* *Trap:* type mismatches (e.g., NVARCHAR length).
  *Fix:* publish **field mapping tables** (DB type ↔ OutSystems type) and validate during PR review.

## 2.7 Environment discipline & windows

* **Daily window** for DB publishes and IS refresh concentrates risk, makes support predictable.
* **Week-1 narrated ops**: extended stand-ups; screen-shared deploys; FAQ captured into docs **same day**.
* **No off-cycle** unless Sev-high; if so, require **Ops + Lead sign-off** and **post-mortem**.

## 2.8 Metrics as coaching signals (not as punishments)

Track and socialize weekly:

* **Flow:** PR lead time to train, percentage hitting the train, off-cycle count.
* **Safety/Quality:** forbidden-drop blocks, RefactorLog coverage on renames, idempotent script coverage, rollback count.
* **Learning:** office-hour volume trend, doc/library views, readiness survey cohort movement.

**Interpretation guidance:**

* Rising forbidden-drop blocks ≠ “bad devs”—it may mean people are attempting legitimate refactors; respond with **how-to clinics** on safe patterns.

## 2.9 Security & least privilege

* Deploy identity should have **exact** DDL rights; grants done via **PostDeploy** where needed (idempotent).
* Keep **audit artifacts** (diff reports, publish logs) attached to PRs/releases.

## 2.10 Naming, indexing, and SARGability standards

* **Naming:** human-readable PascalCase for tables/columns (unless legacy demands all-caps machine names for compatibility).
* **Indexes:**

  * Create FK **supporting indexes** to avoid scans on child lookups.
  * Use **covering** indexes for hot read paths; apply **filtered** indexes for selective predicates.
  * Review query plans in Dev after change; add “before/after” notes to PR for hot paths.
* **SARGability:** avoid functions on indexed columns in predicates; precompute where appropriate.

## 2.11 High-risk scenarios & prescribed moves

* **Type contraction (NVARCHAR(4000) → (320))** on a hot column:

  * Add `NewEmail NVARCHAR(320) NULL`; backfill with **LEFT** and validate; switch consumers; enforce NOT NULL; drop old column later.
* **Splitting a table** (Customer → Customer + CustomerProfile):

  * Create new table; backfill; add **view** that projects legacy shape; shift consumers module by module; retire view after sunset.
* **Primary key evolution** (INT → BIGINT or GUID):

  * Introduce **surrogate** PK in parallel; maintain dual keys during cutover; migrate FKs; tighten in the last phase.

## 2.12 Troubleshooting fastpaths (symptom → probable cause → fix)

* **PostDeploy “did nothing”** → Not in publish scope or transaction failed silently → Check publish log; add PRINTs; ensure `IncludeTransactionalScripts=True`.
* **NOT NULL add failed** → Consumers not aligned or backfill incomplete → Revert to nullable; complete backfill; enforce next train.
* **Integration Studio sees no new columns** → Publish didn’t hit target DB or extension not republished → Confirm profile/target; run IS **Refresh → Publish**; check connection string.
* **Permissions missing** → Grants not in PostDeploy or idempotent guard blocked → Add grants to PostDeploy with EXISTS checks.

---

## 2.13 Developer checklists (quick-use, repeatable)

**PR checklist (attach diff + script artifacts):**

1. Change type (Additive/Tighten/Refactor/DataMotion) and risk class chosen.
2. RefactorLog present for renames (or parallel column plan described).
3. Seeds/data motion **idempotent** and transaction-wrapped; double-run proved.
4. FKs have supporting indexes; SARGable predicates reviewed.
5. OutSystems type mapping validated; “contract readiness” items checked.
6. Diff report attached; publish profile named; forbidden-drop scan passed.

**Deployer checklist (daily window):**

1. Announce upcoming publish; confirm PR list frozen.
2. Execute publish with Prod-like profile; archive logs/artifacts.
3. Integration Studio **Refresh → Publish** all changed extensions; push consumer dependency refresh.
4. Perform smoke tests on critical flows; announce green + any follow-ups.
5. Record metrics (duration, issues, blocks) and update FAQ.

---

## 2.14 Small library of “known-good” snippets

**Idempotent upsert seed (MERGE + name normalization)**

```sql
MERGE dbo.Country AS T
USING (
  VALUES
  ('US','United States'),
  ('CA','Canada')
) AS S(Code,Name)
ON (T.Code = S.Code)
WHEN NOT MATCHED BY TARGET THEN
  INSERT(Code, Name) VALUES (S.Code, S.Name)
WHEN MATCHED AND NULLIF(T.Name, S.Name) IS NOT NULL
THEN UPDATE SET Name = S.Name;
```

**Guarded CHECK constraint (phase-in)**

```sql
-- Phase 1: Add column + enforce via application logic
ALTER TABLE dbo.Invoice ADD Amount DECIMAL(18,2) NULL;

-- Phase 2: Backfill negatives to 0 and log
UPDATE dbo.Invoice SET Amount = 0 WHERE Amount < 0 OR Amount IS NULL;

-- Phase 3: Add CHECK after data clean
ALTER TABLE dbo.Invoice WITH CHECK ADD CONSTRAINT CK_Invoice_Amount_Positive CHECK (Amount >= 0);
ALTER TABLE dbo.Invoice CHECK CONSTRAINT CK_Invoice_Amount_Positive;
```

**Parallel column for type change**

```sql
ALTER TABLE dbo.Order ADD OrderNumber_v2 NVARCHAR(32) NULL;
UPDATE dbo.Order SET OrderNumber_v2 = CAST(OrderNumber AS NVARCHAR(32));
-- Swap consumers to v2; later:
ALTER TABLE dbo.Order ALTER COLUMN OrderNumber_v2 NVARCHAR(32) NOT NULL;
-- After sunset, drop old column in a dedicated train.
```

**View-as-contract with aliasing & defaulting**

```sql
CREATE VIEW sales.vOrder_Contract AS
SELECT
  o.OrderId,
  o.OrderNumber_v2 AS OrderNumber,            -- alias new to legacy name
  COALESCE(o.Status,'Pending') AS Status,
  TRY_CONVERT(DATE, o.OrderDateTime) AS OrderDate,
  c.CustomerId,
  c.Email
FROM dbo.[Order] o
JOIN dbo.Customer c ON c.CustomerId = o.CustomerId;
```

---

## 2.15 What to do when something *must* break (rare, planned)

* Escalate to **break-glass**:

  * Announce: what/why/when/how long.
  * Prepare a **rollback** and a **compatibility shim** where feasible (temporary view or stored proc).
  * Schedule a **consumer alignment sprint** with hard dates.
  * Post-mortem within 24–48h and convert learning into policy/checklists.

---

### Quick index of who does what (so you’re never stuck)

* **IC Dev (Junior/Mid):** additive columns; seed MERGEs; simple indexes; PRs with artifacts; local → Dev verification.
* **Senior Dev:** complex refactors; performance plan reviews; parallel columns; view contracts; troubleshooting knotty deploys.
* **Dev Lead/Staff:** decide direct vs contract; gate break-glass; run daily train; watch metrics; host narrated Week-1; steward docs.
* **Ops/DBA:** enforce profiles; maintain deployment identity; ensure backups/snapshots prior to risky windows; instrument logs.
* **OutSystems devs:** model contract first; refresh & republish during window; ensure Service Studio dependencies updated; smoke test flows.

---

If this direction hits the mark, I can immediately expand the next nodes with similar density (“**CI/CD & publish profiles**” + “**Decision Frameworks**” or “**Week-1/Month-1 Playbooks**” with stepwise pseudo-migrations and detailed OutSystems refresh choreography).

Perfect—continuing **inline** with the same developer-facing density. Here are **Section 3** (Governance: Release Trains, Standards & Swim Lanes) and **Section 4** (SSDT Core: Project, Build, Publish), expanded 2–4× with concrete mechanics, examples, and anti-patterns.

---

# 3) Governance: Release Trains, Standards & Swim Lanes

> **Why governance matters:** it’s the exoskeleton that lets us move fast without breaking things. Treat this as operational code: precise, repeatable, observable.

## 3.1 Daily Release Train (Cadence, Rituals, Variants)

**Baseline (Dev/Test/UAT trains)**

* **13:30** “last call” bot ping in #dev-db-train (auto pin “what’s landing” dashboard).
* **14:00** PR cutoff. Train captain (Lead/Staff) assembles the manifest: PRs, target envs, publish profiles.
* **14:10–14:40** Pre-flight review (captain + on-duty DBA): forbidden-drop scanner, diff reports, PostDeploy scan for non-idempotency, grants present.
* **15:00** Publish (DACPAC via profile). Capture artifacts (script, diff, logs) → attach to the release record.
* **15:30** **Integration Studio** Refresh → Publish all touched extensions; broadcast impacted domains; kick Service Studio dependency refresh.
* **15:35–15:50** Smoke tests (golden paths); if red, open an **Immediate Remediation Room** (Zoom/Meet) and staff it.
* **15:55** Close-out: announce “green/amber/red,” link artifacts, list any manual follow-ups.

**Prod train (windowed; change calendar required)**

* Same shape, plus:

  * **Rollback asset** confirmed (backup, point-in-time restore, revert PR).
  * Change ticket with: blast radius, runbook link, comms plan, explicit “BlockOnPossibleDataLoss=True” evidence.
  * Post-publish business smoke (not just technical), owner signed.

**Variants**

* **Micro-train (hotfix for non-breaking additive)**: allowed off-cycle with captain + DBA sign-off; still attach artifacts.
* **Break-glass (rare, planned breaking change)**: requires director-level approval, formal comms, dedicated bridge, rollback rehearsed.

**Artifacts to keep per train**

* `manifest.json` (list of PRs, SHAs, authors, risk class).
* `diff-report.html`, `publish.sql`, `publish.log`, `profile.used.json`.
* `ops.postcheck.md` (what was verified, by whom, timestamps).
* `is.refresh.report.md` (extensions republished, durations).

---

## 3.2 PR Standards & Safety Gates

**PR Template (excerpt)**

```markdown
# Change Summary
- Type: (Additive | Tighten | Refactor | Data Motion | Security/Grants)
- Risk: (Low | Medium | High)
- Domain(s): Customer, Orders

# Evidence
- [ ] Schema Compare diff attached (project↔target)
- [ ] Publish profile referenced: `profiles/UAT.publish.xml`
- [ ] Forbidden-drop scan: PASS/FAIL (details)
- [ ] Idempotency proof for PostDeploy: link to dev double-run log
- [ ] RefactorLog entries present for renames (if any)
- [ ] OutSystems mapping validated (type lengths, nullability)

# Verification Steps
- [ ] Local build ✓
- [ ] Dev publish dry-run ✓
- [ ] Seed rowcounts logged ✓
- [ ] FK supporting indexes in place ✓

# Rollback plan
- Notes:
```

**Automated checks (required on PR)**

* Build DACPAC; fail on project warnings as errors.
* Generate diff vs the target env snapshot; attach HTML diff + JSON.
* **Forbidden-drop scanner** (regex across generated script): DROP TABLE/VIEW/PROC/FK; ALTER … DROP COLUMN; ALTER to narrower type → FAIL in Prod; WARN in Dev/Test.
* PostDeploy analysis: flag `DELETE`/non-guarded updates; ensure `BEGIN TRY/TRAN/COMMIT` + `CATCH/ROLLBACK`.
* Ensure **RefactorLog** changed when rename detected.

**Human checks (Lead/Staff)**

* Validate decision: direct bind vs contract (view).
* Validate phased tightening plan (Add → Backfill → Tighten).
* Validate perf considerations: FK supporting indexes, covering idx, filtered idx rationale.
* Confirm OutSystems type compatibility (lengths, date/time, decimals).

---

## 3.3 Swim Lanes, RACI & Permissioning

**RACI per activity**

* **Author additive (Junior/Mid)** → R/A: Dev; C: Lead; I: DBA.
* **Refactor rename (Senior+)** → R: Senior; A: Lead/Staff; C: DBA; I: PM.
* **Publish to non-Prod** → R: On-duty DBA; A: Lead; C: Captain; I: team.
* **Prod publish** → R: DBA; A: Staff/Director; C: Lead, PM, QA; I: Business owner.

**Permissioning**

* Only **DBA/Deploy identity** with controlled pipeline can execute publishes.
* Developers do not possess direct Prod DDL rights.
* Secrets in ADO variable groups; approvals required on Prod stage.

**Capability ladder (granular)**

* **Junior**: columns (nullable), reference data seeds, simple idx; cannot approve PRs solo; no publishes.
* **Mid**: NOT NULL after default/backfill, FK creation, view creation; can conduct Dev publishes.
* **Senior**: multi-table refactors, parallel columns, contract views for hot domains, perf tuning.
* **Staff/Lead**: trains, metrics, policy evolution, incident commander for break-glass.

---

## 3.4 Comms & Incident Hygiene

**Proactive comms**

* Change calendar entries for Prod trains; Slack reminders at 13:30 & 15:25.
* “What lands today” bot enumerates domains + risk class.
* Consumers subscribe to domain tags (#domain-customer, #domain-orders).

**Incident protocol**

* **Sev-1**: data-corrupting or business-stopping → Freeze further publishes; bring up bridge; enact rollback; post-mortem in 24h; policy lesson captured.
* **Sev-2**: degraded but workable → Hotfix plan within train window or next micro-train.
* **Sev-3**: nuisance/edge → Track with owner; fold into next train.

---

## 3.5 Metrics, SLOs & Continuous Improvement

**Flow SLOs**

* ≥ 85% of schema PRs hit same-day train after merge.
* IS refresh completed ≤ 15 minutes post publish.

**Safety/Quality SLOs**

* Forbidden-drop blocks at 100% in Prod.
* RefactorLog coverage at 100% for renames.
* PostDeploy idempotency coverage ≥ 95%.

**Learning Signals**

* Office-hour volume ↓ week-over-week after Month-1.
* Survey cohort uplift: ≥ 30% move from Foundation→Intermediate in 6 weeks.

**Ops dashboard (weekly)**

* Lead time trend, publish durations, failure causes, top anti-patterns.
* Heatmap of domains with frequent churn (candidate for contract views).

---

## 3.6 Standards: Naming, Indexing, Constraints, Security

**Naming**

* Tables: `Domain.Entity` (e.g., `sales.Order`), Columns: `PascalCase`.
* Constraints: `PK_<Table>`, `FK_<Child>_<Parent>`, `CK_<Table>_<Rule>`, `IX_<Table>_<Cols>[_Include]`.

**Indexing policy**

* FK → supporting nonclustered idx on child FK cols.
* Covering idx for hot queries (document predicates and includes).
* Filtered idx when selectivity > 95% on boolean/status flags.

**Constraint staging**

* CHECK constraints added **WITH CHECK** only after data cleanup.
* Default constraints named and scripted in model (no ad-hoc unnamed defaults).

**Security**

* Grants in PostDeploy, idempotent:

```sql
IF NOT EXISTS (SELECT 1 FROM sys.database_permissions p
 WHERE p.class_desc='OBJECT_OR_COLUMN' AND p.permission_name='SELECT'
   AND OBJECT_SCHEMA_NAME(p.major_id)='sales'
   AND OBJECT_NAME(p.major_id)='vOrder_Contract'
   AND USER_NAME(p.grantee_principal_id)='app_user')
BEGIN
  GRANT SELECT ON sales.vOrder_Contract TO app_user;
END
```

---

# 4) SSDT Core: Project, Build, Publish

> **Goal:** make the model authoritative, the pipeline boring, and the outputs inspectable.

## 4.1 Project Anatomy & Repository Layout

**Folder tree (suggested)**

```
/db
  /schema
    /sales
      /Tables
      /Views
      /Programmability
    /customer
      /Tables
      /Views
      /Programmability
  PreDeploy.sql
  PostDeploy.sql
  MyDatabase.sqlproj
  /profiles
    Dev.publish.xml
    Test.publish.xml
    UAT.publish.xml
    Prod.publish.xml
  /refactorlog
    refactorlog.xml
  /scripts
    samples/ (MERGE templates, grants)
  /docs
    diff-examples/, decisions/, runbooks/
CODEOWNERS
PULL_REQUEST_TEMPLATE.md
```

**Project settings (non-negotiables)**

* **ANSI settings** consistent (`SET ANSI_NULLS ON`, `QUOTED_IDENTIFIER ON`).
* Treat warnings as errors where feasible.
* Model collation matches target server (document exceptions).
* Include **database scoped configurations** where needed (e.g., `LEGACY_CARDINALITY_ESTIMATION` off unless legacy).

---

## 4.2 Publish Profiles: Policy Encoded as Code

**Prod-like profile (excerpt)**

```xml
<Project>
  <PropertyGroup>
    <TargetDatabaseName>MyDb</TargetDatabaseName>
    <BlockOnPossibleDataLoss>True</BlockOnPossibleDataLoss>
    <DropObjectsNotInSource>False</DropObjectsNotInSource>
    <ScriptDatabaseOptions>True</ScriptDatabaseOptions>
    <ScriptNewConstraintValidation>True</ScriptNewConstraintValidation>
    <VerifyDeployment>True</VerifyDeployment>
    <IncludeTransactionalScripts>True</IncludeTransactionalScripts>
    <CommandTimeout>1200</CommandTimeout>
    <IgnorePermissions>False</IgnorePermissions>
    <IgnoreUserSettingsObjects>False</IgnoreUserSettingsObjects>
    <ExcludeObjectTypes>
      <ObjectType>RoleMembership</ObjectType>
    </ExcludeObjectTypes>
    <SqlCommandVariableValues>
      <SqlCommandVariableValue Value="app_user" Name="APP_USER" />
    </SqlCommandVariableValues>
  </PropertyGroup>
</Project>
```

**Profile policies to standardize across envs**

* **Prod/Test/UAT**: `BlockOnPossibleDataLoss=True`, `DropObjectsNotInSource=False`.
* **Dev**: may allow `DropObjectsNotInSource=True` for clean-room resets **only on sandbox DBs**, never shared Dev.

---

## 4.3 Build → Diff → Script → Publish (The Golden Path)

**Azure DevOps pipeline (YAML, sketch)**

```yaml
trigger:
  branches: { include: [ main ] }

stages:
- stage: Build
  jobs:
  - job: BuildDacpac
    pool: { vmImage: 'windows-latest' }
    steps:
    - task: VSBuild@1
      inputs:
        solution: 'db/MyDatabase.sqlproj'
        configuration: 'Release'
    - publish: 'db/bin/Release/MyDatabase.dacpac'
      artifact: 'dacpac'

- stage: Diff
  dependsOn: Build
  jobs:
  - job: SchemaCompare
    steps:
    - download: current
      artifact: dacpac
    - powershell: |
        # Compare dacpac to snapshot/target and export diff & report artifacts
        .\tools\SqlPackage.exe /a:Script /sf:$(Pipeline.Workspace)\dacpac\MyDatabase.dacpac `
          /pr:db/profiles/UAT.publish.xml /op:publish.sql /p:DropObjectsNotInSource=False `
          /Diagnostics:true
        # run forbidden-drop scanner
        python .\tools\scan_forbidden.py publish.sql
      displayName: 'Schema Compare + Forbidden Drop Scan'
    - publish: 'publish.sql'
      artifact: 'diff'

- stage: Deploy_UAT
  dependsOn: Diff
  condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'))
  jobs:
  - deployment: PublishUAT
    environment: 'uat-db'
    strategy:
      runOnce:
        deploy:
          steps:
          - download: current
            artifact: dacpac
          - download: current
            artifact: diff
          - task: PowerShell@2
            inputs:
              targetType: 'inline'
              script: |
                .\tools\SqlPackage.exe /a:Publish /sf:$(Pipeline.Workspace)\dacpac\MyDatabase.dacpac `
                  /pr:db/profiles/UAT.publish.xml /p:BlockOnPossibleDataLoss=True
```

**Key points**

* Always **publish** using the **profile** (no bespoke flags scattered in scripts).
* Attach the `publish.sql`, `publish.log`, and scanner report to the run.
* Verify **RefactorLog**-backed renames appear as `sp_rename` equivalents in generated script, not drop/create.

---

## 4.4 PreDeploy & PostDeploy: Guardrails and Idempotency

**PreDeploy common uses**

* **Guards**: assert expected server options, database compatibility level; fail early if incompatible.
* **Safety rails**: refuse to proceed if business window flag isn’t set (`SELECT 1 FROM dbo.DeployWindow WHERE IsOpen=1`).

**PostDeploy common uses**

* **Reference data**: MERGE seeds with deterministic keys.
* **Grants**: idempotent GRANT statements (see §3.6).
* **Cross-schema sync**: populate materialized lookup tables; refresh synonyms when policy allows.

**Anti-patterns**

* Non-idempotent `INSERT` seeds (duplicating rows on rerun).
* Long, unbatched updates on hot tables during daytime (lock storms) → schedule at low-traffic or batch with `TOP (N)` loops.

**Batched backfill pattern (safe for large sets)**

```sql
DECLARE @batch INT = 1000;
WHILE 1=1
BEGIN
  WITH cte AS (
    SELECT TOP (@batch) Id
    FROM dbo.BigTable WITH (READPAST, ROWLOCK)
    WHERE NewCol IS NULL
    ORDER BY Id
  )
  UPDATE t SET NewCol = <expr>
  FROM dbo.BigTable t
  JOIN cte ON cte.Id = t.Id;

  IF @@ROWCOUNT = 0 BREAK;
END
```

---

## 4.5 Multiple Databases & Cross-DB Access

**Recommended**

* Prefer **same-DB** contracts; if cross-DB unavoidable, use **synonyms** under policy and gate via sqlcmd variables for DB names.
* Keep cross-DB names in **one place** (PostDeploy or a constants script); no hard-coded strings in scattered objects.

**Cautions**

* Schema compare can’t manage external DBs; treat synonyms/externals as **declarative configuration**, not a moving target.
* For Prod: validate the **target DB names** via PreDeploy guards; fail fast if not matching expected catalog.

---

## 4.6 Local vs Shared Dev, Snapshots, and Repro

**Local**

* Allow experiments; default to **LocalDB** or a personal SQL instance.
* Provide `dev.sample.settings.json` with connection strings and sqlcmd defaults for easy spin-up.

**Shared Dev**

* The only environment where team integration is validated *before* Test/UAT.
* Nightly snapshot/refresh from Test (if policy allows); publish a simple **reset** runbook for clean state.

**Repro discipline**

* Log minimal repro steps for any failed publish in `ops.postcheck.md`.
* Attach exact `publish.sql` and DB state notes (version, compatibility level, collation).

---

## 4.7 RefactorLog: What It Is and How to Not Break It

**What**

* An XML ledger SSDT uses to map old→new names so schema compare generates **rename** ops instead of drop/create.

**How to use**

* Always perform renames via SSDT UI or proper refactor commands, never by hand-editing DDL strings.
* Commit the **refactorlog.xml** with the same PR as the rename.
* If a rename didn’t produce a RefactorLog entry, **back out** and redo via the tool.

**When to avoid rename entirely**

* Very big, hot tables or columns → prefer **parallel column/table** with phased cutover; use a **view** to maintain legacy shape until sunset.

---

## 4.8 Type Mapping & OutSystems Compatibility

**Common mismatches**

* `NVARCHAR(50)` vs OutSystems Text length validations → standardize canonical lengths (e.g., emails 320, names 100).
* `DECIMAL(18,2)` vs currency widgets → lock on 18,2 unless biz rules dictate.
* Date/time: prefer `datetime2` (with precision) for new fields; avoid `smalldatetime`.
* Boolean: use `BIT` (map to Boolean), not tinyint flags.

**Validation step in PR**

* Include a small **type mapping table** in PR description for any touched External Entity columns.

---

## 4.9 Testing the Model: Double-Run, Drift, and Non-Regression

**Double-run rule**

* PostDeploy must be safe to run twice. Demonstrate in Dev and paste the proof (row counts, harmless re-exec) into PR notes.

**Drift checks**

* Maintain a **UAT schema snapshot** artifact; diffs measured against snapshot pre-merge.
* If drift detected (external change), stop and reconcile (bring the change back into SSDT or revert the external drift).

**Non-regression**

* Keep **tiny “tripwire” queries** (e.g., `SELECT COUNT(*)` from critical lookups) in a smoke pack that runs after publish.
* Version the smoke pack; link the run in the train close-out.

---

## 4.10 Performance Considerations in SSDT World

**Before:** session by session “tune it in prod.”
**After:** change intent is **reviewable**; capture the plan before/after for hot queries.

**Perf checklist**

* FKs backed by indexes; verify that joins on FK columns seek.
* Long MERGE/UPDATE backfills batched; NOLOCK avoided on writes; `READPAST` + small batches for backfills.
* Statistics: if bulk data changes, trigger `UPDATE STATISTICS` or rely on auto-update (document thresholds).
* Capture regressions: add plan hash + duration notes in PR for hot paths.

---

## 4.11 Common Failure Modes & Their Fast Fixes

* **Generated script tries to drop** something important → your profile isn’t Prod-safe or a rename wasn’t captured → flip `DropObjectsNotInSource=False`, fix RefactorLog, re-generate diff.
* **Publish succeeds, IS shows no changes** → wrong target DB or publish skipped objects via excludes → check profile’s excludes and connection.
* **PostDeploy failed mid-flight** → ensure TRY/CATCH + TRAN; echo error with `THROW;` so pipeline fails loudly.
* **Grant missing** → GRANT in PostDeploy with `IF NOT EXISTS` guard; re-publish or run a scoped hotfix to add grant.

---

## 4.12 Mini “Known-Good” Library (paste-ready)

**CHECK with WITH CHECK (after cleanup)**

```sql
ALTER TABLE customer.Person WITH CHECK 
ADD CONSTRAINT CK_Person_Email_Valid CHECK (Email LIKE '%@%.%');
ALTER TABLE customer.Person CHECK CONSTRAINT CK_Person_Email_Valid;
```

**Filtered index for active rows**

```sql
CREATE INDEX IX_Order_Active ON sales.[Order](CustomerId)
WHERE Status = 'Active';
```

**Synonym (cross-DB, gated)**

```sql
-- sqlcmd var: $(EXT_DB)
IF OBJECT_ID('dbo.Orders_ext','SN') IS NOT NULL DROP SYNONYM dbo.Orders_ext;
EXEC('CREATE SYNONYM dbo.Orders_ext FOR [' + '$(EXT_DB)' + '].sales.[Order]');
```

---

**Close-out for Sections 3 & 4:**

* You now have the **governance spine** (trains, PR gates, roles, comms, SLOs) and the **SSDT core practice** (profiles, pipeline, pre/post patterns, refactors, type mapping, performance).
* If this meets the bar, I’ll proceed next with **Section 5 (OutSystems Integration Flow—deep choreography, static→lookup migrations)** and **Section 6 (Decision Frameworks—direct vs contract, phased tightening, static vs lookup)**, each at comparable density, with more stepwise examples and anti-pattern counters.

Absolutely—here’s a **20–30× expansion** on **Swim Lanes** that turns the concept into a full **operating system for who does what, when, with which powers, and at what quality**. It’s written directly to the impacted development team and is built to be *used*, not admired.

---

# Swim Lanes OS (v1.0)

> **Purpose:** Make change predictable and safe by clarifying **authority, accountability, and skill thresholds** for every role interacting with schema, data motion, and OutSystems integration.
> **Scope:** IC Devs (Junior/Mid/Senior), Staff/Lead, DBA/Ops, OutSystems Devs, QA, PM, and Business Owner.
> **Ground rule:** **Authority follows demonstrated capability** + **artifact quality**. When in doubt, downgrade the change to a safer lane (expand/contract, alias via view, parallel columns) and escalate the review.

---

## 0) Swim Lane Principles

1. **Least-Surprise:** Anyone reading the lane table should be able to predict the next safe move.
2. **Evidence-Based Authority:** Powers are earned by **clean diffs, idempotent data motion, refactor discipline**, and on-time train participation.
3. **No Silent Renames:** All renames are tool-driven with **RefactorLog** or replaced by **parallel constructs**.
4. **One Window:** DB publishes + Integration Studio refresh sit in a **daily window**; off-cycle is break-glass.
5. **Two-Train Tightening:** Add → Backfill → Tighten (never “tighten + pray”).
6. **Artifact or It Didn’t Happen:** Every change carries **diff report**, **publish profile**, **forbidden-drop scan**, and **postcheck** notes.

---

## 1) Roles & Lanes: Authority Matrix

### 1.1 Role Catalog

* **IC-J** = Individual Contributor, **Junior**
* **IC-M** = Individual Contributor, **Mid**
* **IC-S** = Individual Contributor, **Senior**
* **LEAD/STAFF** = Tech Lead or Staff Engineer
* **DBA/OPS** = Database Administrator / Release Engineer
* **OS-DEV** = OutSystems Developer (Integration/Service Studio)
* **QA** = Quality Analyst / SDET
* **PM** = Product/Project Manager
* **BO** = Business Owner / Domain SME

### 1.2 Authority by Change Type (Who May Propose/Approve/Execute)

| Change Type                          | Propose        | Approve (PR)          | Execute (Publish) | Notes                                               |
| ------------------------------------ | -------------- | --------------------- | ----------------- | --------------------------------------------------- |
| **Additive column (nullable/wider)** | IC-J/IC-M/IC-S | LEAD/STAFF            | DBA/OPS           | Include OutSystems type mapping; no consumer break. |
| **Additive table**                   | IC-M/IC-S      | LEAD/STAFF            | DBA/OPS           | Deterministic PK; seed plan idempotent.             |
| **NOT NULL enforcement**             | IC-M/IC-S      | LEAD/STAFF            | DBA/OPS           | Must follow Add→Backfill; attach backfill proof.    |
| **CHECK constraint**                 | IC-M/IC-S      | LEAD/STAFF            | DBA/OPS           | Use `WITH CHECK` *only after* cleanup.              |
| **FK creation**                      | IC-M/IC-S      | LEAD/STAFF            | DBA/OPS           | Requires supporting index; perf note in PR.         |
| **Rename (tool-driven)**             | IC-S           | LEAD/STAFF            | DBA/OPS           | **RefactorLog** required; no ad-hoc rename.         |
| **Parallel column for type change**  | IC-M/IC-S      | LEAD/STAFF            | DBA/OPS           | Migrate consumers; sunset plan documented.          |
| **Split/Merge tables**               | IC-S           | LEAD/STAFF            | DBA/OPS           | Contract view strongly recommended.                 |
| **PostDeploy data motion**           | IC-M/IC-S      | LEAD/STAFF            | DBA/OPS           | Idempotent + transaction + rowcount logs.           |
| **Security/Grants**                  | IC-M/IC-S      | LEAD/STAFF            | DBA/OPS           | In PostDeploy, idempotent `GRANT`.                  |
| **Cross-DB synonyms**                | IC-S           | LEAD/STAFF            | DBA/OPS           | sqlcmd-gated; PreDeploy guard for target DB.        |
| **Drop or data-lossing op (Prod)**   | LEAD/STAFF     | Director/Change Board | DBA/OPS           | Break-glass; rollback rehearsed.                    |

> **Rule of thumb:** **IC-J** does additive & seeds under review; **IC-M** adds constraints with defaults/backfill; **IC-S** performs complex refactors; **LEAD/STAFF** arbitrates decisions & owns trains; **DBA/OPS** is the only executor of publishes.

---

## 2) What’s In/Out Per Lane (Allow/Prohibit List)

### 2.1 IC-Junior (IC-J)

**Allowed**

* Add **nullable** columns; widen types (e.g., `NVARCHAR(100)` → `(320)`).
* Write **MERGE-based** seeds for reference tables with deterministic keys.
* Create **simple** nonclustered indexes (single/dual-column) with rationale.
* Update **documentation** (README, decision logs) and **PR artifacts**.

**Prohibited**

* NOT NULL enforcement; CHECK constraints; FKs.
* Any **rename** or table split/merge.
* Non-idempotent PostDeploy; long-running backfills.
* Running **any** publish; editing **publish profiles**.

**Exit criteria → IC-M**

* 10 consecutive clean PRs (no scanner violations; idempotent proof).
* Demonstrated OutSystems type mapping competence.
* 2 seed packs with double-run evidence captured.

### 2.2 IC-Mid (IC-M)

**Allowed**

* Add **NOT NULL** *after* default/backfill; introduce **CHECK** with cleanup.
* Add FKs with **supporting indexes**; show plan considerations.
* Use **parallel columns** for safe type changes; phase enforcement.
* Author idempotent **PostDeploy** data motion with transaction and logs.

**Prohibited**

* Tool-driven **renames** on large/hot objects (Senior+); split/merge.
* Cross-DB synonyms without sqlcmd gating and PreDeploy guards.
* Any **Prod** publish.

**Exit criteria → IC-S**

* 5 phased-tightening changes landing cleanly (Add→Backfill→Tighten).
* 3 FKs with measured perf before/after; plans attached.
* 1 parallel-column migration with consumer cutover checklist.

### 2.3 IC-Senior (IC-S)

**Allowed**

* Tool-driven **renames** with RefactorLog; validate rename diffs.
* Table **split/merge** with compatibility **view-as-contract**.
* Type migrations at scale; **batched** backfills; perf shaping.
* Author **cross-DB** patterns (synonyms) with policy & guards.

**Prohibited**

* Silent drops; in-place destructive changes in Prod.
* Bypassing contracts on high fan-out domains without written rationale.

**Exit criteria → Lead/Staff**

* Led 2 multi-table refactors to sunset without incident.
* Demonstrated incident handling (Sev-2) with post-mortem & policy update.
* Mentored IC-J/IC-M through cohort uplift (documented).

### 2.4 Lead/Staff (LEAD/STAFF)

**Responsibilities**

* Decide **Direct vs Contract**; own **release trains**.
* Enforce **policy** via PR gates; own **metrics** & retros.
* Approve break-glass; define rollback; chair incident bridges.
* Shepherd **docs** and **training**; design cohort labs.

**Prohibited**

* Ad-hoc exceptions that skip artifact standards.
* Allowing publishes without **manifest** & **postcheck**.

---

## 3) RACI by Workflow

| Workflow                       | R (Responsible) | A (Accountable) | C (Consulted) | I (Informed) |
| ------------------------------ | --------------- | --------------- | ------------- | ------------ |
| Daily Train (Dev/Test/UAT)     | DBA/OPS         | LEAD/STAFF      | IC-S, QA      | Team, PM     |
| Prod Train                     | DBA/OPS         | Director/LEAD   | QA, PM, BO    | Team         |
| PR Review (schema)             | IC-M/IC-S       | LEAD/STAFF      | DBA           | Team         |
| Rename (tool-driven)           | IC-S            | LEAD/STAFF      | DBA, OS-DEV   | Team         |
| Split/Merge with contract view | IC-S            | LEAD/STAFF      | DBA, QA       | Team         |
| Static→Lookup migration        | IC-M            | LEAD/STAFF      | OS-DEV, QA    | Team         |
| Incident (Sev-1)               | DBA/OPS         | LEAD/STAFF      | IC-S, QA, PM  | BO, Team     |

---

## 4) Gatekeeping Rubrics (How We Decide Readiness)

### 4.1 Additive Column

* **Completeness:** column spec includes type/length/nullable, default (if business needs).
* **Compatibility:** OutSystems type mapping checked; no existing consumer break.
* **Artifacts:** diff report, profile, scanner PASS, PR notes include mapping table.
* **Outcome:** approve if all green → schedule on next train.

### 4.2 Tightening (NOT NULL / CHECK)

* **Prereqs:** Backfill present + logs; consumers updated (evidence).
* **Risk:** Impacted rows ≤ published threshold; rollback plan (revert to nullable or drop constraint).
* **Outcome:** approve for next train, not same-day as backfill unless trivial and low risk.

### 4.3 Rename

* **Tool use:** RefactorLog entry exists; diff shows rename not drop/create.
* **Fan-out:** Count of dependent objects; **view-as-alias** considered?
* **Outcome:** approve only if low fan-out or contract present; otherwise prefer parallel + alias.

### 4.4 Split/Merge

* **Contract:** Transitional view present; sunset plan with dates.
* **Data motion:** Batched; idempotent; measured throughput; off-hours window if needed.
* **Outcome:** staged over ≥2 trains; approve with rehearsed rollback.

---

## 5) Ceremony Pack (Who shows up, when, for what)

* **13:30 Daily “Last Call” (15 min)**

  * Attendees: LEAD/STAFF, DBA, one representative per domain with pending PRs.
  * Agenda: manifest freeze, risk scan highlights, IS refresh prep.

* **15:30 IS Refresh & Publish (15–30 min)**

  * Attendees: DBA, OS-DEV per affected extension, QA.
  * Agenda: refresh extensions; publish; broadcast impacted consumers; trigger SS dependency refresh.

* **Friday Retro (30–45 min)**

  * Attendees: Team, QA, PM.
  * Artifacts: weekly metrics; top anti-patterns; doc deltas; action items with owners/dates.

* **Incident Post-Mortem (within 24–48h)**

  * Attendees: Incident actors + LEAD.
  * Outputs: timeline, root cause, “single change to prevent recurrence,” doc/policy patch.

---

## 6) Pairings & Handoffs (Minimize friction)

**Common pairings**

* **IC-J ↔ IC-M** for seeds + additive columns (J authors, M reviews).
* **IC-M ↔ IC-S** for phased tightening & FKs (M authors, S reviews).
* **IC-S ↔ DBA** for refactors & batched backfills.
* **IC-S ↔ OS-DEV** for contract views & consumer swap choreography.

**Handoffs**

* PR → **Manifest**: LEAD/STAFF confirms risk classes, adds to manifest.
* Publish → **IS Refresh**: DBA hands to OS-DEV with list of touched extensions.
* IS Refresh → **QA Smoke**: OS-DEV signals QA; QA runs tripwires and golden paths.
* Close-out → **Metrics**: DBA populates ops.postcheck; LEAD updates dashboard.

---

## 7) Performance & Scale Lanes

* **IC-M:** may add simple covering indexes if supported by workload evidence; must attach query plan before/after.
* **IC-S:** may add filtered indexes, partitioning schemes, computed columns (persisted) with perf notes and storage impact callout.
* **LEAD/DBA:** approve or defer to perf clinic; align with maintenance windows for heavy operations.

---

## 8) Anti-Pattern Catalog (Lane-Specific)

* **IC-J:** non-idempotent seeds (TRUNCATE/INSERT), direct Prod DDL, copying patterns from blogs without guards.
* **IC-M:** enforcing NOT NULL in the same train as first introduction; adding FK without index; unbatched UPDATE on big tables.
* **IC-S:** overusing contracts (everything behind a view); clever renames without RefactorLog; cross-DB without policy.
* **LEAD/STAFF:** accepting “tiny exceptions,” skipping artifacts “just this once,” unannounced off-cycle publish.

---

## 9) Lane-Linked Training (Cohort by Capability)

* **IC-J → IC-M Track:**

  * Labs: MERGE idempotency; OutSystems type mapping; simple indexes; PR artifact hygiene.
  * Assessment: 2 seeds, 1 additive, 1 FK proposal with index.
* **IC-M → IC-S Track:**

  * Labs: phased NOT NULL; batched backfill; RefactorLog rename in sandbox; contract view migration.
  * Assessment: 1 parallel-column migration; 1 view-contract cutover with sunset.
* **IC-S → LEAD Track:**

  * Labs: run a train; incident command simulation; metrics dashboard narrative.
  * Assessment: 1 refactor program from plan → sunset without Sev-2+.

---

## 10) Evidence & Promotion

**Portfolio Items Required**

* PR links with attached diffs and publish logs.
* PostDeploy double-run proof (screenshots/logs).
* Before/after perf snippets (plans, durations) for index work.
* “What I would change in the policy now” memo (for IC-S/LEAD).

**Panel Rubric**

* **Technical correctness:** 0–5
* **Safety discipline:** 0–5
* **Communication clarity:** 0–5
* **Operational maturity (trains, artifacts):** 0–5
* **Coaching impact:** 0–5

---

## 11) SLOs by Lane (Personal, Team-Visible)

* **IC-J:** 95% PRs artifact-complete; 0 scanner fails; ≤1 doc back-and-forth.
* **IC-M:** 90% trains met; no idempotency regressions; 100% FK+index pairs.
* **IC-S:** 100% RefactorLog on renames; 0 Sev-2+ from refactor; 2 doc deltas/month.
* **LEAD/STAFF:** 85% on-time trains; weekly metrics published; retro actions <7-day closure; 0 unauthorized off-cycles.

---

## 12) Domain Ownership & Change Requests

**Owner Map**

* Each schema (`sales`, `customer`, etc.) has **Owner (LEAD/STAFF)**, **IC-S deputy**, and **OS-DEV lead**.
* Changes outside your domain require **consult** with the owner; contract view strongly considered at boundaries.

**CR Template (for cross-domain)**

* Proposed change, driver (perf/security/churn), dependency list, fallbacks (view alias, parallel columns), timeline, rollback.

---

## 13) Static → DB Lookup Lane

* **IC-M** authors table + seed + External Entity binding.
* **OS-DEV** switches consumers; **QA** verifies reference parity.
* **LEAD** sets sunset date; **DBA** ensures grants.
* **Guard:** treat ID values as **deterministic**; never delete/re-insert IDs.

---

## 14) Example: End-to-End Refactor with Lanes

**Scenario:** Split `Customer` into `Customer` + `CustomerProfile`; preserve legacy shape.

1. **IC-S** drafts plan: new table + backfill; `vCustomer_Contract` view; consumer swap; sunset.
2. **LEAD** approves decision; **DBA** validates window.
3. **IC-S** PR includes: tables, view, PostDeploy batch, perf notes; **RefactorLog** avoided (split).
4. **DBA** publishes in Dev; **OS-DEV** aligns External Entities; **QA** runs smoke.
5. Next train: consumers switch to contract view; **IC-S** monitors perf.
6. Sunset: deprecate legacy column set; **LEAD** closes out with doc update.

---

## 15) Lane Checklists (Printables)

**IC-M Tightening Checklist**

* [ ] Prior backfill + rowcount logs attached
* [ ] All consumers ack’d (issue links)
* [ ] Constraint added with `WITH CHECK`
* [ ] Rollback note (drop/disable) present
* [ ] Publish profile named; scanner PASS

**IC-S Refactor Checklist**

* [ ] Contract view or alias plan in place
* [ ] No drop/create masquerading as rename
* [ ] Batched data motion with TRY/CATCH
* [ ] OutSystems mapping doc updated
* [ ] Sunset plan (date, comms)

**LEAD Train Captain Checklist**

* [ ] Manifest frozen at 14:00
* [ ] Diff reports attached per PR
* [ ] Forbidden-drop scan PASS
* [ ] IS refresh schedule + owners pinged
* [ ] Postcheck + metrics logged

---

## 16) Calendars by Lane (Sample Week)

* **Mon–Thu**

  * 13:30 Last Call → 14:00 Cutoff → 15:00 Publish → 15:30 IS Refresh → 15:45 Smoke → 15:55 Close
* **Fri**

  * 13:30 Last Call → 14:00 Cutoff → 15:00 Publish → 15:30 IS Refresh → 16:15 Weekly Retro
* **Wed (biweekly)**

  * 11:00 Perf Clinic (IC-S + DBA)
* **Tue/Thu (wk1–wk4)**

  * 10:00 Cohort Labs (J→M, M→S)

---

## 17) Lane-Scoped Tooling & Bots

* **Manifest Bot:** compiles merged PRs by 13:30, flags risk class.
* **Scanner Bot:** comments FAIL on forbidden drops; links to offending lines.
* **IS Orchestrator:** posts list of extensions needing refresh/publish.
* **Metrics Collector:** generates weekly dashboard with trends and outliers.

---

## 18) Lane-Based Risk Controls (What prevents badness)

* **Profile pins** (Prod safety switches baked in).
* **RefactorLog enforcement** (rename diff test).
* **PostDeploy idempotency tests** (double-run harness in Dev).
* **Train window discipline** (no ad-hoc publishes).
* **Comms & ownership tags** (domain channels).

---

## 19) Promotion & Rotation Paths

* **IC-J → IC-M:** Additive mastery → Tightening with defaults → FK/index hygiene.
* **IC-M → IC-S:** Complex phased refactors → Contract views → Backfill/perf.
* **IC-S → LEAD:** Trains + incidents + policy stewardship.

> **Rotations:** 4–6 weeks shadowing **DBA/OPS** and **OS-DEV** to reduce silo friction; 1–2 refactor programs per rotation.

---

## 20) FAQs (Lane-Specific)

* **Q:** Can IC-J add an FK?
  **A:** With IC-M co-author and IC-S reviewer, yes—*if* the supporting index and perf note are provided.

* **Q:** Who decides if we need a contract view?
  **A:** LEAD/STAFF, advised by IC-S; default is direct bind unless ≥2 drivers (fan-out, churn, security, perf, boundary, evolving keys).

* **Q:** Who can request an off-cycle publish?
  **A:** LEAD/STAFF only; DBA executes; PM/BO informed; post-mortem required.

---

## 21) “What Good Looks Like” (Exemplars)

* **Exemplar PR (IC-M Tightening):**

  * Add column (train N), backfill MERGE with logs, consumers updated.
  * Next train: ALTER to NOT NULL; diff + profile attached; smoke passes.

* **Exemplar Program (IC-S Refactor):**

  * Split table with contract view; batched data motion; consumer swap; sunset.
  * Zero Sev incidents; perf improved; doc and training updated.

* **Exemplar Train (LEAD):**

  * On-time; manifest tight; scanner PASS; IS refresh in <15 mins; metrics posted; 2 FAQs updated.

---

## 22) Red Flags & Escalation Triggers

* Missing RefactorLog on rename diff → **Halt & fix**.
* PostDeploy without TRY/CATCH or MERGE → **Request rewrite**.
* “We can just drop it; no one uses this” → **Require usage evidence** or contract alias.
* Off-cycle habit forming → **Retro & policy nudge**.

---

## 23) Lane-to-Decision Matrix (At a Glance)

| Context                  | Default Lane       | Decision                        |
| ------------------------ | ------------------ | ------------------------------- |
| Low fan-out, simple add  | IC-J → LEAD review | Direct bind                     |
| Add + enforce constraint | IC-M → LEAD        | Two-train tightening            |
| Rename on hot table      | IC-S → LEAD        | Parallel + alias (avoid rename) |
| Cross-domain churn       | IC-S/LEAD          | Contract view                   |
| Cross-DB dependency      | IC-S/DBA           | Synonym with guards             |

---

## 24) Minimal Artifacts (No Exceptions)

* **PR**: diff report, profile name, scanner result, idempotency proof, mapping table (if OS impacted).
* **Publish**: manifest, publish.sql, publish.log, profile.used.json.
* **Postcheck**: smoke pack results, IS refresh list, issues/owners.
* **Docs**: decision log line (what/why), FAQ additions if pattern repeats.

---

## 25) Closing Mantra

> **Clarity over cleverness.**
> **Add now; tighten later.**
> **Refactor with refuge (contracts & parallels).**
> **Artifacts or it didn’t happen.**
> **Trains make change safe.**

---

If you want, I can now **render these swim lanes into a printable “lane card” set** (one per role), plus a **manifest.json schema** for train automation and a **scanner ruleset** that aligns to the prohibitions above.

Awesome—continuing **inline** with the same developer-facing density. Here are **Section 5** (OutSystems Integration Flow—deep choreography, static→lookup) and **Section 6** (Decision Frameworks—direct vs contract, phased tightening, static vs lookup, pre/post, refactors). I’ve folded in every relevant pattern from our source notes and added hard-won practices to close gaps.

---

# 5) OutSystems Integration Flow (Deep Choreography)

> **Goal:** make DB changes “edible” by OutSystems with zero scavenger hunts. Treat the **DB publish + IS refresh** as a single ritual with clear inputs/outputs, owners, and smoke signals.

## 5.1 End-to-End Sequence (Per Train)

1. **DB publish (DACPAC)** lands in the daily window using the selected **publish profile**.
2. **Integration Studio (IS) Refresh → Publish** all touched **extensions** (External Entities).
3. **Service Studio (SS)**: consumer modules **Refresh Dependencies**, then Publish.
4. **Smoke pack** (golden flows) runs; announce green/amber/red; file targeted follow-ups.

**Why this order:** Avoid stale metadata and broken bindings—IS owns the “external schema view”; SS must **never** assume DB drift; it must refresh against published IS extensions.

---

## 5.2 Integration Studio: Extension Hygiene & Ownership

**Extension granularity**

* Prefer **one extension per domain** (e.g., `CustomerDB`, `SalesDB`) versus one monolith or too-many micro-extensions.
* Each extension has a **technical owner** (IC-S/OS-DEV) and a **backup**; a rotation schedule prevents knowledge silos.

**Extension contents**

* External Entities bound either to **base tables** (direct) or **contract views** (for hot domains).
* Use **explicit naming** mirroring DB logical names (pascal-case; avoid cryptic abbreviations).
* Expose **read-only** unless a write is intentionally supported; default writes off for views unless proven safe.

**IS refresh process**

* For each extension:

  * Open **IS → Connect → Refresh** (compare).
  * Review **differences** (added/removed/changed attributes/keys).
  * Regenerate and **Publish** the extension.
  * Capture a quick **refresh report** (module, changed entities/attributes, duration).
* If in doubt, **reload connection** settings to ensure you are pointing at the correct DB and schema.

**Common deltas and their causes**

* **New column not visible**: DB publish missed (wrong target/profile), or the extension points to a different catalog/schema.
* **Changed PK/FK not reflected**: The binding points to a **view** without PK metadata; fix by annotating PK in the view (if supported) or adjust OutSystems entity keys manually.
* **Datatype mismatch warnings**: DB changed length/precision; ensure mapping tables are up to date (see §5.5) and consumers are adapted before tightening.

---

## 5.3 Service Studio: Consumer Refresh & Safe Wiring

**Dependency refresh**

* After every IS publish, **all consuming modules** must run **Dependencies → Refresh**.
* Establish a “**dependency refresh roster**”: which modules depend on which extensions. Automate a Slack ping (bot) listing modules to refresh after the train.

**Wiring practices**

* **Service façade first**: Consumers call **Server Actions** in a domain service module (not raw aggregates everywhere).
* Put the **DB access** (External Entity CRUD/Aggregates) behind these actions; swap implementations without chasing 50 screens.

**Safe write patterns**

* If binding to **views** (contracts), keep them **read-only**; write via stored procs or base tables with business checks; wrap in domain service actions.
* If binding to **tables**, ensure **transactions** in service actions when performing multi-row operations (create + children).

**Caching & invalidation**

* For reference lookups migrated from Static Entities, add **entity cache** (short TTL) or preload caches at module start.
* Invalidate cache on **PostDeploy** seeds that change critical lookups; log a “cache bust” message in the train close-out.

---

## 5.4 Modeling-First Delivery (Unblocking UI/API Work)

**Problem:** UI teams need the shape **before** the DB is ready.
**Solution:** Build **stub server actions** and **data models** (record definitions) that match the intended contract; use in-memory **test data** or static JSON mocked into the actions.

**Switching from stubs to live**

* After DB publish and IS refresh, **replace** the data layer of actions with External Entities/Aggregates.
* Keep a **feature toggle** (`UseLiveDB`) for one or two trains to enable rollback without code churn.
* Add a **post-switch checklist**: remove mock data includes, remove “UseLiveDB” toggle after sunset.

**Guardrails**

* **Type compatibility table** (see §5.5) pre-shared so modeling matches DB reality (lengths, nullability, decimals).
* Run **schema lint**: automated check that SS record definitions match the extension entity metadata (name, type, length).

---

## 5.5 Type Mapping Canon (DB ↔ OutSystems)

**Canonical choices**

* `NVARCHAR` → Text (specify max length; e.g., Email **320**, Name **100**, Code **16**).
* `DECIMAL(18,2)` → Decimal; keep **18,2** for money unless domain requires different.
* `BIT` → Boolean.
* `DATETIME2(3|7)` → Date Time (prefer `datetime2` over `smalldatetime`).
* **Identity**/GUID keys → Integer/Identifier types accordingly (be explicit).

**Mapping table artifact**

* Maintain a **living page** that lists every External Entity attribute with DB type and OutSystems type, including **lengths and nullability**.
* Require inclusion of mapping diffs in PRs that touch entity shapes.

---

## 5.6 Static Entities → DB Lookup (Migration Cookbook)

**When to migrate:**

* Shared across modules, needs auditing, or suffers from performance issues as **Static** grows.
* Cross-domain reuse (e.g., `Country`, `OrderStatus`).

**Migration envelope**

1. **Create** table in DB with deterministic IDs (no re-seeding).
2. **Seed** with idempotent MERGE (PostDeploy).
3. **Bind** as External Entity in IS; expose read-only view if you need stable surface (`vOrderStatus`).
4. **Swap** consumers in SS (feature toggle optional).
5. **Retire** the Static Entity after a sunset period; add a detection to fail any new references to Static.

**Edge cases**

* Legacy consumers rely on **enumeration values** embedded in logic. Provide a **compatibility table** or constants map; search-and-replace guided by an index of use sites.
* **Language/localization**: if Static previously used locale strings, design a `Lookup` + `LookupTranslation` structure; hydrate per culture in the service layer.

---

## 5.7 Write-Path Strategies (Views vs Tables)

* **Views** are **read surfaces**. Writes should target **bases** via domain actions or stored procedures; do **not** mix writes to views unless updatable and well-constrained (discouraged).
* **Concurrency:** adopt optimistic concurrency (version/timestamp or rowversion columns) for critical entities; surface conflicts with actionable error messages.

---

## 5.8 Security & Connectivity

* **Connection users**: least privilege; separate read vs write identities when possible.
* **Grants**: applied via PostDeploy idempotent scripts; verify after each IS publish by running a **connection test** in IS.
* **Secrets**: managed in platform settings (not in code). Rotate per policy; never hard-code in IS.

---

## 5.9 Failure Modes & Fast Fixes (OS-centric)

* **“Entity not found” after refresh**: Consumer module didn’t refresh dependencies → SS **Refresh Dependencies** and publish.
* **“Attribute length exceeded” errors**: DB tightened before consumers trimmed inputs → revert to widened column (or lift view alias length), then schedule phased tightening after consumer fixes.
* **“Permission denied” at runtime**: Grant missing after publish → run PostDeploy grants or hotfix GRANT with idempotent block; then re-publish extension.
* **“Connection mismatch”**: IS connected to Dev while module runs in UAT → verify **connection strings** per environment and IS server connection.

---

## 5.10 OutSystems-Specific Playcards (Copy/Paste)

**Refresh+Publish checklist (per extension)**

* [ ] Validate connection target (env DB)
* [ ] Compare & review differences
* [ ] Regenerate + Publish
* [ ] Note changed entities/attributes (for broadcast)
* [ ] Confirm permissions (test connection)

**Consumer cutover checklist**

* [ ] Refresh Dependencies in SS
* [ ] Re-publish module(s)
* [ ] Run smoke tests (list provided)
* [ ] Remove mocks, if any; toggle `UseLiveDB=on`
* [ ] Log success and any residual tasks

---

## 5.11 Telemetry & Observability (Post-Train)

* **Smoke pack** results posted (pass/fail + duration).
* **Error rate** on critical screens for 1–2 hours after refresh; watch for spikes.
* **DB side**: slow query log/plan regressions on newly touched entities; capture plan hashes and durations for hot screens.

---

## 5.12 “Modeling Debt” Registry

* Keep a backlog of **temporary model compromises** (e.g., view aliasing, over-widened lengths, toggles).
* Each item has: **owner**, **sunset target train**, and **remediation** (tighten, rename for real, remove toggle, etc.).

---

# 6) Decision Frameworks (Direct vs Contract, Tightening, Static vs Lookup, Pre/Post, Refactors)

> **Goal:** remove bike-shed debates. These decisions should feel like **typed functions**: give the inputs, the output is deterministic.

## 6.1 Direct Bind vs View-as-Contract (Matrix + Thresholds)

**Default:** Direct External Entity binding to **base tables**.
**Escalate to contract view** when **≥ 2** drivers:

| Driver             | Threshold/Signal                                | Notes                                            |
| ------------------ | ----------------------------------------------- | ------------------------------------------------ |
| Fan-out            | ≥ 5 consumer modules or cross-team use          | Contract centralizes rename/shape churn.         |
| Churn risk         | ≥ 3 planned breaking refactors in 2 months      | Use view to alias old→new and shield.            |
| Security shaping   | Need to hide/rename/exclude sensitive fields    | Expose minimal surface via view.                 |
| Perf shaping       | Stable projection/denorm required for hot paths | Indexed view (if allowed) or tuned base + view.  |
| Ownership boundary | Different teams own consumer vs producer        | Contract clarifies the boundary.                 |
| Evolving keys      | Key type/name likely to change                  | View can alias; schedule parallel key migration. |

**Anti-pattern:** “View all the things.” Keep contracts **focused** (20–40 hot domains); direct bind elsewhere.

**Contract obligations**

* Only **rename/alias** to stabilize; **do not** put business logic in the view.
* Document **sunset plans** if the contract is purely transitional.
* Define **PK**/unique key in OutSystems explicitly if the view can’t carry key metadata.

---

## 6.2 Add → Backfill → Tighten (Staged Tightening)

**Decision rule:** if a change **can** be staged, it **must** be staged.

**Stage 1 (Add)**

* Add new column (nullable/wider); or add new table alongside existing.
* Ensure **seed** or default paths exist to populate for new records.

**Stage 2 (Backfill)**

* PostDeploy **idempotent MERGE** or batched UPDATEs; capture counts and durations.
* Validate with consumer smoke; plan hash checks for hot queries.

**Stage 3 (Tighten)**

* ALTER to **NOT NULL**, add **CHECK**, add **FK** with supporting index.
* Communicate **one train in advance**.

**When to collapse stages**

* **Only** for trivial, provably safe changes (tiny tables, no writes during window) and with Lead/DBA approval. Log explicit justification.

---

## 6.3 Static Entity vs DB Lookup (Decision Guide)

**Keep Static** (in-app) when:

* Tiny enums, near-zero churn, single module consumption, no audit/perf concerns.

**Move to DB Lookup** when any are true:

* Multiple modules/domains use the set; need **auditing** or **report joins**; performance issues with Static.

**DB Lookup obligations**

* Deterministic IDs (do not reseed IDs across environments).
* MERGE seeds; no TRUNCATE/INSERT.
* Consumer swap checklist in §5.10; deprecate Static after sunset.

---

## 6.4 PreDeploy vs PostDeploy (Which Script Does What?)

| Concern                                        | PreDeploy | PostDeploy      |
| ---------------------------------------------- | --------- | --------------- |
| Safety guards (window open, server options)    | ✅         | ❌               |
| Schema invariants checks (compat level)        | ✅         | ❌               |
| Idempotent reference data seeds                | ❌         | ✅               |
| Grants/permissions                             | ❌         | ✅               |
| Cross-schema sync / synonyms refresh           | ❌         | ✅               |
| Cleanup after expand (e.g., drop temp staging) | ❌         | ✅ (later train) |

**Rules:** PreDeploy **fails fast**; PostDeploy **acts** idempotently. Neither should contain destructive operations in Prod without break-glass.

---

## 6.5 Rename Strategies (Tool vs Parallel vs Alias)

**Tool-driven rename with RefactorLog**

* Use for **low/moderate** fan-out; ensures schema compare emits **RENAME** not DROP/CREATE.
* Validate with diff before merging.

**Parallel column/table**

* Use when fan-out is **high**, objects are **hot**, or type changes are involved.
* Keep both old and new until consumers switch; provide **view alias** if needed.

**Alias through contract view**

* Add view that **aliases** new names to old names; route consumers to the view; later, remove aliasing and switch to base.

**Refusal case**

* If rename would cause massive churn + risk, **do not** rename; prefer **semantic tolerance** (retain legacy name) or evolve naming in service layer.

---

## 6.6 Expand/Contract & Split/Merge (Zero-Downtime Refactors)

**Expand/Contract (column change)**

* **Expand:** add new column; backfill; dual-write if necessary.
* **Switch:** consumers write/read new column; keep old as alias.
* **Contract:** drop old column in a future train after sunset.

**Split/Merge (table topology)**

* **Split:** create `NewTable`; backfill; create **contract view** that reproduces legacy shape; shift consumers; retire view later.
* **Merge:** create wider table; backfill with JOIN; present contract view for old shape during transition.

**Operational guardrails**

* Batch backfills; avoid lock storms (`ROWLOCK`, `READPAST`, small batches).
* Capture throughput and estimated completion; schedule in low-traffic windows.

---

## 6.7 Performance Decisions (When to Index/Denorm/Materialize)

* **Index now** if: new FK created; hot predicate surfaces; or regression noticed in smoke.
* **Denormalize** if: read path is critical and normalized join is demonstrably hot (measure), **and** churn on source columns is modest.
* **Materialized/Indexed view**: consider only under clear benefit; document maintenance impact and constraints.

**Checklist in PR**

* Predicate(s), selectivity guess, expected plan change; **before/after** plan snippet or duration.

---

## 6.8 Cross-DB & External Dependencies (When to Allow)

**Allow** if:

* Strong boundary exists and cannot be collapsed; read-mostly; performance is adequate; change cadence aligned.
* Use **synonyms** gated by sqlcmd variables; PreDeploy guards confirm target DB exists and is the expected one.

**Prefer not** if:

* You can replicate small lookups locally; coupling harms deployments or increases incident domain.

---

## 6.9 Risk Classification & Required Evidence

| Risk        | Examples                                       | Evidence Required                                           |
| ----------- | ---------------------------------------------- | ----------------------------------------------------------- |
| Low         | Additive nullable/widened, new table, seed     | Diff report, profile, idempotency proof                     |
| Medium      | NOT NULL, CHECK, FK                            | Backfill logs, index plan, consumer ack                     |
| High        | Rename hot object, split/merge, type migration | Refactor plan, contract view/parallel, batch plan, rollback |
| Break-glass | Drop/contract in Prod                          | Director approval, rollback rehearsal, comms plan           |

---

## 6.10 Decision Log (Single Source of Truth)

* **Every non-trivial decision** (direct vs contract, phased plan, rename strategy) gets a one-liner in `/docs/decisions/YYYY-MM-DD-slug.md`

  * Context, Options, Decision, Why, Owner, Sunset date if transitional.
* Weekly retro surfaces **top 3 decisions** for team visibility.

---

## 6.11 Canonical Snippets (Drop-in)

**View aliasing legacy → new**

```sql
CREATE VIEW customer.vPerson_Contract AS
SELECT
  p.PersonId,
  p.GivenName  AS FirstName,
  p.FamilyName AS LastName,
  TRY_CONVERT(date, p.BirthDateTime) AS BirthDate
FROM customer.Person p;
```

**FK with supporting index**

```sql
ALTER TABLE sales.[Order]
  ADD CONSTRAINT FK_Order_Customer
    FOREIGN KEY (CustomerId) REFERENCES customer.Customer(CustomerId);
CREATE INDEX IX_Order_CustomerId ON sales.[Order](CustomerId);
```

**PreDeploy guard (window open)**

```sql
IF NOT EXISTS (SELECT 1 FROM dbo.DeployWindow WHERE IsOpen = 1)
  THROW 51000, 'Deployment window is closed', 1;
```

**PostDeploy grant idempotent**

```sql
IF NOT EXISTS (
  SELECT 1 FROM sys.database_permissions p
  JOIN sys.objects o ON o.object_id=p.major_id
 WHERE o.name='vPerson_Contract' AND p.permission_name='SELECT'
   AND USER_NAME(p.grantee_principal_id)='app_user')
GRANT SELECT ON customer.vPerson_Contract TO app_user;
```

---

## 6.12 Anti-Patterns & Counters (Decision-Level)

* **“Rename in place—no one will notice.”** → *Counter:* require RefactorLog; if hot, use parallel+alias.
* **“Drop objects not in source to clean up.”** → *Counter:* Prod profile forbids; track explicit deprecations.
* **“Tighten immediately after add.”** → *Counter:* enforce two-train policy unless trivial; evidence required.
* **“Views everywhere.”** → *Counter:* matrix threshold; log reasons; cap contract count; audit quarterly.

---

## 6.13 Quick Decision Trees

**Do I need a contract view?**

* Fan-out ≥ 5? **Yes** → +1
* Churn planned? **Yes** → +1
* Security/Perf shaping? **Yes** → +1
* Score ≥ 2 → **Use contract**. Else **Direct bind**.

**How to change a column type?**

* Can consumers adapt same train? **No** → **Parallel column** → backfill → switch → tighten.
* Is the column cold with tiny table? **Yes** → With approval, consider in-place change in non-Prod first; still prefer parallel in Prod.

**Static → DB lookup?**

* Shared? Audited? Join-heavy? **Any Yes** → **Migrate**; else keep **Static**.

---

**Close-out for Sections 5 & 6:**

* You now have the **full choreography** from DB publish through IS/SS refresh and consumer cutover, plus a **hard criteria** playbook for **direct vs contract**, **staged tightening**, **static→lookup**, **pre/post roles**, and **refactor strategies**.
* If this meets the bar, I can continue with **Section 7 (CI/CD & publish profiles—deep)** and **Section 8 (Operational checklists & troubleshooting catalog)** at the same level of scrutiny, with runnable examples and printable checklists.

Locked on the outline you provided. Here are **Sections 7, 8, and 9** inline—**7 and 9 are deeply expanded** per your instruction; **8 is crisp but complete**.

---

# 7) OutSystems Integration Flow (OSX)

> **Prime directive:** DB deploy lands first; OutSystems **converges** to reality via Integration Studio (IS) refresh/publish, then Service Studio (SS) dependency refresh and smoke. We reduce “metadata drift,” choreograph ownership, and make swaps boring.

## OSX.FLO.701 — Change Sequence (the choreography that never changes)

**Golden order per daily train**

1. **DB Publish** (DACPAC via profile) → artifact captured (`publish.sql`, `publish.log`, diff report).
2. **IS Refresh → Publish** for all **touched** extensions (domain-level; see §702).
3. **SS Refresh Dependencies → Publish** for all **consuming modules**.
4. **Smoke pack** (server actions + top 3 screens per domain) → announce **green/amber/red**.

**Why this order**

* IS *owns* the “externalized schema.” If SS refreshes **before** IS publishes, you propagate stale shapes.
* The IS publish is the **hard boundary** where DB shape becomes platform-visible.

**Automation aids**

* **Train manifest → IS list**: bot emits which extensions must refresh based on diff report (schema → entities touched).
* **IS publish watcher**: logs duration & changes: `ext: CustomerDB; +2 attrs, 0 drops, publish 00:00:14`.

**SLAs**

* IS refresh & publish across all touched extensions **≤ 30 minutes** after DB publish.
* SS dependency refresh on all consumers **≤ 60 minutes** after IS publish.

---

## OSX.EXT.702 — Extension Granularity, Ownership, and Hygiene

**Granularity rule**

* **One extension per domain** (e.g., `CustomerDB`, `SalesDB`, `CatalogDB`). Avoid both extremes: a monolith (**too coupled**) or dozens of tiny shards (**ops thrash**).

**Ownership**

* Each extension has: **Owner (IC-S or OS-DEV)**, **Backup**, and a Slack **channel tag** (`#domain-customer`).
* A simple **owner map** lives in `/docs/owners.md` with escalation contacts.

**Hygiene practices**

* External Entities point to **tables** (default) or **contract views** (only for hot boundaries).
* **Read-only by default** for view-backed entities. Write-paths go through **base tables** via domain actions/stored procedures.
* Version shape **on purpose** (append-only where feasible). Dedicate a **deprecation window** for alias removals.

**IS refresh steps (per extension)**

* Validate **connection target** (env & catalog).
* **Compare** and review attribute/key deltas (watch out for PK inference on views).
* **Regenerate → Publish**; capture a “mini report” (name, time, changes).
* If no changes detected but DB diff says there are: check catalog/schema mismatch, or missing DB publish.

---

## OSX.MOD.703 — Modeling-First Pattern (unblock app teams)

**Intent**

* UI/API teams should sprint **ahead** using stubs that mirror the **intended** contract, then switch to live data with minimal churn.

**Method**

* Build domain **service façades** (server actions) returning well-typed records that match the future External Entity shape.
* Back the façades with **in-memory fixtures** or **static JSON** during development.
* After DB publish + IS refresh, **swap** the data source to External Entities; keep a **`UseLiveDB` toggle** for one or two trains.

**Safeguards**

* Maintain a living **type-mapping canon** (DB ↔ OS) with lengths/precision/nullability; require PR tickbox “**Modeling parity verified**.”
* Run a **schema-lint** (scripted) that compares SS record definition against extension entity metadata; fail the check if divergent.

**Switch checklist**

* [ ] Replace stub calls with External Entity aggregates/CRUD.
* [ ] Toggle `UseLiveDB = True` in non-Prod and validate.
* [ ] Remove mocks & toggle after one sunset train.
* [ ] Re-run smoke pack and perf snapshots (plan hashes) for hot queries.

---

## OSX.STC.704 — Static → External (DB Lookup) Migration Checklist

**“Should we migrate?” quick test**

* Shared across modules? Needs audit/history? Joins in reporting? Performance complaints? If **any yes**, migrate.

**Cookbook**

1. **Create table** with **deterministic IDs** (never reseed across envs).
2. **Seed with MERGE** (idempotent; transaction-wrapped; log rowcounts).
3. **Expose** as External Entity via IS (or a **contract view** if shape needs alias stability).
4. **Swap consumers** in SS (dependency refresh + façade wire-up); optional feature toggle during validation.
5. **Sunset** the Static Entity: block new references, remove usage, delete only after a full train passes green.

**Edge-handling**

* Legacy logic referencing enum **integers/strings**: create a **compat map**; do guided search/replace.
* **Localization**: structure as `Lookup` + `LookupTranslation(culture code)`, hydrate in service actions.

**Post-migration invariant**

* Treat lookup rows as **append-only** except for value label corrections; never TRUNCATE/INSERT or change PKs after adoption.

---

# 8) CI/CD, Profiles, and Safety Nets (CICD)  *(focused & tight per instruction)*

## CICD.PFL.801 — Publish Profiles (policy encoded as code)

* **Prod/UAT/Test**:

  * `<BlockOnPossibleDataLoss>True</BlockOnPossibleDataLoss>`
  * `<DropObjectsNotInSource>False</DropObjectsNotInSource>`
  * `<IncludeTransactionalScripts>True</IncludeTransactionalScripts>`
  * `<VerifyDeployment>True</VerifyDeployment>`
  * Timeouts sized to data volume; explicit **sqlcmd vars** (e.g., users, synonyms DB).
* **Dev**: may allow `DropObjectsNotInSource=True` for **sandbox only**—never shared Dev.

## CICD.SEC.802 — Security & Least Privilege

* Separate **build agent** from **deploy identity**; the latter has only necessary DDL.
* Secrets via pipeline key vault; no connection strings in repo.
* Keep **auditable artifacts**: diff reports, logs, profile.used.json, scanner result.

## CICD.PIP.803 — PR Pipeline (fail fast)

* Build **DACPAC**; attach artifact.
* Generate **schema diff**; run **forbidden-drop scanner** on `publish.sql`.
* Validate **RefactorLog** on rename; flag **PostDeploy** non-idempotence; verify **FK → supporting index** pairs.
* Gate merge on **all checks green**; auto-populate train **manifest**.

---

# 9) Operational Checklists & Runbooks (OPS)

> **Outcome orientation:** reduce cognitive load under time pressure. These are the **battle cards** used every day. They encode pre/during/post, who does what, and how we know we’re done.

## OPS.DEP.901 — Deployer Checklist (DBA/OPS)

**Pre-publish (T-30 to T-10 min)**

* [ ] **Calendar lock**: window confirmed; no conflicting ops.
* [ ] **Manifest frozen**: PRs, target DB(s), profile(s) listed.
* [ ] **Backups/snapshots**: restore point or PITR capability ready (Prod).
* [ ] **Artifacts staged**: DACPAC, `publish.sql`, `profile.used.json`, scanner PASS.
* [ ] **Comms**: Slack pre-announce with impacted domains & expected duration.

**Publish (T-0)**

* [ ] Execute publish with **profile**; capture `publish.log`.
* [ ] Watch for **data loss warnings** (should be blocked in Prod); abort if triggered.
* [ ] Verify **rowcounts/messages** from PostDeploy (seeds/grants).
* [ ] If failure: **ROLLBACK** plan (restore/revert PR) + incident bridge.

**Immediately post-publish (T+0 to +30)**

* [ ] Trigger **IS Refresh → Publish** for all touched extensions (owners pinged).
* [ ] Record IS durations and deltas.
* [ ] Post “**IS publish complete**” with extension list & timestamps.

**Post-publish (T+30 to +60)**

* [ ] Confirm **SS dependency refresh** by consumer owners; republish modules.
* [ ] Run **smoke pack**: top 3 actions/screens per domain; record pass/fail + time.
* [ ] Announce **green/amber/red**; list any follow-ups & owners.

**Close-out (T+60 to +90)**

* [ ] Upload artifacts: `publish.sql`, `publish.log`, diff report, IS report, smoke results.
* [ ] Update **ops.postcheck.md** (anomalies, durations, notes).
* [ ] Log metrics (duration, incidents, blocks) to dashboard.

---

## OPS.PR.902 — Developer PR Checklist (IC-J/M/S)

**Schema & shape**

* [ ] Names human-readable; types/lengths explicit; nullability correct.
* [ ] **FKs** have supporting indexes; **CHECK** only after cleanup (`WITH CHECK`).
* [ ] **RefactorLog** exists for renames (or parallel+alias plan documented).

**Data motion & idempotency**

* [ ] PostDeploy uses **MERGE**/guards; double-run proved (include logs/rowcounts).
* [ ] Multi-step logic wrapped in **TRY/CATCH + TRAN**; error re-throws (`THROW`).
* [ ] Backfill batched for large sets (ROWLOCK/READPAST/top-N loop).

**OutSystems integration**

* [ ] Type mapping table included (DB ↔ OS).
* [ ] If contract view used: read-only; alias documented; sunset date noted.
* [ ] Consumer impact assessed; dependency refresh roster updated.

**Artifacts & gates**

* [ ] Diff report + `publish.sql` attached; **forbidden-drop scanner** PASS.
* [ ] Profile named; target DB noted; security grants included (idempotent).
* [ ] Rollback approach written (revert, disable constraint, etc.).

---

## OPS.W1.903 — Week-1 Ops Runbook (Narrated adoption)

**Daily**

* **Narrated deploy** (screen share): LEAD walks through diff & script; DBA publishes.
* **Office hours** (30 min post-train): live Q&A; capture FAQ.
* **Doc delta**: update handbook same day with new examples or pitfalls.

**End of week**

* **Retro**: metrics, top anti-patterns, “one change we ship to policy/docs.”
* **Cohort mapping**: readiness survey → who needs which labs next week.

---

## OPS.TS.904 — Troubleshooting Index (symptom → likely cause → fix)

**“IS sees no changes”**

* *Cause:* wrong DB/catalog, DB publish missed, extension bound to view without metadata.
* *Fix:* verify profile target; rerun publish; adjust view keys or bind to tables.

**“NOT NULL add failed”**

* *Cause:* backfill incomplete; consumers not aligned.
* *Fix:* revert to nullable; complete backfill; re-enforce next train.

**“Permission denied” at runtime**

* *Cause:* missing GRANT; wrong principal in env.
* *Fix:* run idempotent GRANT in PostDeploy; confirm via IS connection test.

**“Length/precision mismatches”**

* *Cause:* tightened DB column before consumer adaptation.
* *Fix:* (1) temporarily widen (or alias length in view), (2) fix consumers, (3) tighten in next train with comms.

**“Publish script wants to DROP objects” (Prod)**

* *Cause:* rename not captured (RefactorLog), or DropObjectsNotInSource accidentally true.
* *Fix:* fix rename via tool; ensure Prod profile forbids drops; regenerate diff.

**“Backfill locks everything”**

* *Cause:* unbatched updates; missing indexes; daytime window.
* *Fix:* batch with TOP-N + ROWLOCK/READPAST; add supporting index; schedule low-traffic.

**“SS crashes on dependency refresh”**

* *Cause:* extension publish mid-refresh; version skew.
* *Fix:* wait for IS completion; restart SS; refresh again; re-publish module.

---

## Appendices (Ops-ready snippets)

**Idempotent GRANT (template)**

```sql
IF NOT EXISTS (
  SELECT 1 FROM sys.database_permissions p
  JOIN sys.objects o ON o.object_id=p.major_id
 WHERE o.schema_id=SCHEMA_ID('sales')
   AND o.name='vOrder_Contract'
   AND p.permission_name='SELECT'
   AND USER_NAME(p.grantee_principal_id)='$(APP_USER)')
GRANT SELECT ON sales.vOrder_Contract TO [$(APP_USER)];
```

**Batched backfill skeleton**

```sql
DECLARE @batch INT = 2000;
WHILE 1=1
BEGIN
  WITH todo AS (
    SELECT TOP (@batch) Id
    FROM dbo.Big WITH (READPAST, ROWLOCK)
    WHERE NewCol IS NULL
    ORDER BY Id
  )
  UPDATE b SET NewCol = <expr>
  FROM dbo.Big b JOIN todo t ON t.Id = b.Id;

  IF @@ROWCOUNT = 0 BREAK;
END
```

**IS refresh report (what to capture)**

```
Extension: CustomerDB
Changes: +2 attributes (Customer.Email, Customer.Status), 0 drops
Duration: 00:00:11
Owner: @jane.d
```

---

### Section 7–9 Recap

* **7 (OSX)** gave you the **exact choreography** (DB → IS → SS → smoke), domain-level extension hygiene, modeling-first mechanics, static→lookup migration, and the precise checklists/toggles to make swaps safe and repeatable.
* **8 (CICD)** locked in **policy as code** in publish profiles, least-privilege deploy hygiene, and a **PR pipeline that fails fast** on the right hazards.
* **9 (OPS)** provided **battle cards**: deployer/PR/Week-1 runbooks and a **troubleshooting index** that maps symptom→cause→fix without guesswork.

If you want, I can now:

* generate **printable one-pagers** for 7 and 9 (role-specific),
* or proceed to the next sections (e.g., Metrics & Continuous Improvement; Quality & Anti-Patterns; Glossary) at this same level of density.

Absolutely—here’s a **30× expansion** of **TRN.MAT.1004 — Curated Materials**. It’s a complete, production-ready learning library blueprint you can stand up today. Everything is organized so leads can run it, ICs can learn from it, and ops can audit it.

---

# TRN.MAT.1004 — Curated Materials (30× Edition)

> **Purpose.** Centralize *exactly what to read, watch, try, and ship* for our stack: SSDT/DACPAC, state-based DB-as-code, OutSystems External Entities, and zero-downtime refactors.
> **Design.** Essentials-first, behavior-oriented, runnable. Every item ties to competencies, labs, and the daily release train.

---

## 0) Library Spine & Navigation

**Directory (repo)**

```
/training
  /00-foundation
  /10-ssdt-core
  /20-outsyst-integration
  /30-playbooks
  /40-refactors
  /50-performance
  /60-ci-cd
  /70-ops-and-runbooks
  /90-assessments
  /assets (datasets, mock dbs, screenshots)
  /tools (scanners, scripts, templates)
  /canon (type-mapping, policies, decisions)
```

**Index pages**

* `/training/README.md` — quick start + table of contents.
* Per folder `README.md` with *learning outcomes*, *time budgets*, and *links to labs/exams*.

**Tagging scheme**

* `#comp:ssdt`, `#comp:outsys`, `#comp:refactor`, `#comp:perf`, `#level:foundation|intermediate|advanced`, `#role:ic|lead|ops`.

---

## 1) Competency Matrix (what each material maps to)

| Competency                     | Behaviors you must demonstrate                                      | Materials blocks           |
| ------------------------------ | ------------------------------------------------------------------- | -------------------------- |
| **SSDT Core**                  | Build DACPAC, interpret diff, publish with profile, use RefactorLog | 10-SSDT core, 60-CI/CD     |
| **Safe Data Motion**           | Idempotent MERGE, TRY/CATCH + TRAN, double-run proof                | 30-playbooks, 40-refactors |
| **OutSystems Externalization** | IS refresh/publish, SS dependency refresh, modeling parity          | 20-outsyst-integration     |
| **Staged Tightening**          | Add→Backfill→Tighten across trains                                  | 30-playbooks               |
| **Refactors**                  | Parallel columns, contract views, split/merge with sunset           | 40-refactors               |
| **Performance**                | FK+index hygiene, SARGability, filtered/covering indices            | 50-performance             |
| **Ops Discipline**             | Train artifacts, IS timings, smoke packs                            | 70-ops-and-runbooks        |

---

## 2) Canon: “Don’t make me think” references (single-page truths)

**/canon/type-mapping.md (DB ↔ OutSystems)**

* Email `NVARCHAR(320)` ↔ Text(320)
* Name `NVARCHAR(100)` ↔ Text(100)
* Money `DECIMAL(18,2)` ↔ Decimal(18,2)
* Flags `BIT` ↔ Boolean
* Temporal `DATETIME2(3|7)` ↔ Date Time
* Keys: INT IDENTITY ↔ Integer Identifier; UNIQUEIDENTIFIER ↔ Text/GUID strategy (explicit)

**/canon/publish-profile-policy.md**

* Prod/UAT/Test: BlockOnPossibleDataLoss=True; DropObjectsNotInSource=False; IncludeTransactionalScripts=True; VerifyDeployment=True; Timeout=1200.
* Dev: allow DropObjectsNotInSource only on sandbox DBs.

**/canon/decision-tables.md**

* Direct vs Contract (threshold ≥2: fan-out, churn, security, perf, boundary, evolving keys).
* Staged Tightening: enforce on Train+1 after backfill proof.
* Rename Strategy: tool+RefactorLog if low fan-out; else parallel + alias via view.

**/canon/snippet-library.md (links to code below)**

* MERGE seeds; batched backfills; grants idempotent; FK + supporting index; view-as-contract; PreDeploy guard.

---

## 3) Snippet Library (copy-paste, production-ready)

**3.1 Idempotent MERGE seed**

```sql
MERGE dbo.Country AS T
USING (VALUES
  ('US','United States'),
  ('CA','Canada')
) AS S(Code,Name)
ON (T.Code = S.Code)
WHEN NOT MATCHED BY TARGET THEN
  INSERT(Code, Name) VALUES (S.Code, S.Name)
WHEN MATCHED AND NULLIF(T.Name, S.Name) IS NOT NULL
THEN UPDATE SET Name = S.Name;
```

**3.2 Batched backfill skeleton**

```sql
DECLARE @batch INT = 2000;
WHILE 1=1
BEGIN
  WITH todo AS (
    SELECT TOP (@batch) Id
    FROM dbo.Big WITH (READPAST, ROWLOCK)
    WHERE NewCol IS NULL
    ORDER BY Id
  )
  UPDATE b SET NewCol = <expr>
  FROM dbo.Big b JOIN todo t ON t.Id = b.Id;

  IF @@ROWCOUNT = 0 BREAK;
END
```

**3.3 FK + supporting index**

```sql
ALTER TABLE sales.[Order]
  ADD CONSTRAINT FK_Order_Customer
    FOREIGN KEY (CustomerId) REFERENCES customer.Customer(CustomerId);
CREATE INDEX IX_Order_CustomerId ON sales.[Order](CustomerId);
```

**3.4 View-as-contract (aliasing)**

```sql
CREATE VIEW customer.vPerson_Contract AS
SELECT
  p.PersonId,
  p.GivenName  AS FirstName,
  p.FamilyName AS LastName,
  TRY_CONVERT(date, p.BirthDateTime) AS BirthDate
FROM customer.Person p;
```

**3.5 PreDeploy guard (window open)**

```sql
IF NOT EXISTS (SELECT 1 FROM dbo.DeployWindow WHERE IsOpen = 1)
  THROW 51000, 'Deployment window is closed', 1;
```

**3.6 Grants (idempotent)**

```sql
IF NOT EXISTS (
  SELECT 1 FROM sys.database_permissions p
  JOIN sys.objects o ON o.object_id=p.major_id
 WHERE o.schema_id=SCHEMA_ID('sales')
   AND o.name='vOrder_Contract'
   AND p.permission_name='SELECT'
   AND USER_NAME(p.grantee_principal_id)='$(APP_USER)')
GRANT SELECT ON sales.vOrder_Contract TO [$(APP_USER)];
```

---

## 4) Playbooks (step-by-step, with acceptance tests)

**4.1 Additive Column → Tighten (two-train recipe)**

* Train N: Add `Email NVARCHAR(320) NULL)`; PostDeploy MERGE backfill; attach rowcount log.
* Train N+1: ALTER to NOT NULL; run CHECK constraints; smoke passes.
* **ATs:** Consumers not broken; insert path creates Email; no NULL rows after tighten.

**4.2 Static → DB Lookup**

* Create `dbo.OrderStatus`; seed MERGE; expose External Entity; swap SS consumers; sunset Static.
* **ATs:** parity count equals; screen filters behave identically; no code references to Static remain.

**4.3 Rename (parallel + alias)**

* Add `OrderNumber_v2 NVARCHAR(32)`; copy/backfill; view aliases `OrderNumber_v2 AS OrderNumber`; swap consumers; later drop legacy column.
* **ATs:** queries return unchanged shape; no drops in publish script.

**4.4 Split Table (Customer → Customer + Profile)**

* Create `CustomerProfile`; backfill; contract view reproduces legacy; switch consumers; sunset.
* **ATs:** joins predictable; perf unchanged or better; defect rate zero.

---

## 5) Labs (guided, graded, with solution keys)

Each lab includes: **Objectives**, **Estimated time**, **Prereqs**, **Steps**, **What to submit**, **Rubric**, **Solution key**.

**5.1 Lab F-1 (Foundations): Build & Additive**

* Steps: clone repo; build DACPAC; add `Phone NVARCHAR(32) NULL`; PostDeploy MERGE; PR with diff+profile+scanner PASS.
* Submit: PR link; Dev publish log; rowcount log.
* Rubric (0–5): artifacts complete; idempotent proof; OS mapping; correctness.

**5.2 Lab F-2: Seeds & Grants**

* Add `OrderStatus` lookup; MERGE seed; idempotent GRANT for `app_user` to contract view.
* Submit: PostDeploy snippet; double-run screenshots.
* Rubric: idempotency; guard clauses; correct grant target.

**5.3 Lab I-1 (Intermediate): Tightening**

* Add `PostalCode` nullable + default/backfill; enforce NOT NULL next train.
* Submit: two PRs (N and N+1), consumer ack evidence.
* Rubric: staged correctly; no drops; consumer readiness.

**5.4 Lab I-2: FK+Index & Plan Evidence**

* Add FK `Order → Customer`; create covering index; capture before/after plan or duration.
* Rubric: predicate clarity; index choice; measurable improvement.

**5.5 Lab A-1 (Advanced): Split with Contract**

* Split `Person` into `Person` + `PersonProfile`; create `vPerson_Contract`; cutover; sunset.
* Rubric: zero Sev; contract documented; backfill batched; perf stable.

**5.6 Lab A-2: Parallel Type Migration**

* Migrate `OrderNumber` to NVARCHAR(32) using parallel column; dual-write; enforce; remove old.
* Rubric: dual-write correct; cutover safe; clean publish.

---

## 6) Datasets & Mock Systems

**/assets/datasets**

* `customers_10k.csv`, `orders_100k.csv` with realistic skew (for indexing/plan labs).
* `lookups/*.csv` (countries, states, statuses) for MERGE exercises.

**/assets/mock-dbs**

* `WideWorldImportsX` fork with training schema.
* `RetailLite` (3 tables) for foundations.

**Seeding scripts** to load datasets into Dev quickly.

---

## 7) Tools & Automation (dev-friendly)

**/tools/sqlpackage.cmd** — convenience wrappers to Script/Publish with the right profiles.
**/tools/scan_forbidden.py** — scans publish.sql for DROP/DATA LOSS patterns; exits non-zero on Prod.
**/tools/diff_to_manifest.py** — turns schema diff into “touched extensions” list for IS refresh.
**/tools/lint_os_mapping.ps1** — compares OutSystems entity metadata vs type-mapping canon.

---

## 8) CI/CD Example Pipelines (minimal yet strict)

* Build → Diff → Deploy stages (with artifacts).
* Policies: **must** attach diff HTML + `publish.sql` + scan report to PR; break on forbidden ops.

---

## 9) Video Capsules (5–10 min, task-oriented)

* “Build a DACPAC & read the diff.”
* “MERGE seeds: make double-run safe.”
* “IS Refresh → SS Dependency Refresh: the choreography.”
* “Parallel column migration in 6 steps.”
* “Filtered vs covering index: when to pick which.”

Each video has a **one-pager** and a **quizlet** (3–5 MCQs).

---

## 10) Quick Reference Cards (printables)

* **Daily Train** (DB → IS → SS → Smoke, SLAs, owners).
* **PR Checklist** (evidence/gates).
* **Refactor Decision Tree** (rename vs parallel+alias).
* **Type Mapping Canon** (DB ↔ OS).
* **Troubleshooting Index** (symptom → cause → fix).

---

## 11) Assessments & Rubrics

* **Foundations exam (1h)**: additive + seed + IS refresh simulation.
* **Intermediate practicum (2h)**: staged tightening + FK/index + plan note.
* **Advanced program review (panel)**: refactor plan + rollback + metrics.

Rubrics weight: **Technical (40%)**, **Safety (30%)**, **Ops discipline (20%)**, **Communication (10%)**.

---

## 12) Mentoring & Office Hours

* **Buddy pairs** (J→M, M→S) with weekly 30-min code review.
* **Office hours** immediately after trains (Week-1 daily; Week-2/3 alternate days).
* **Shadowing**: rotate IC-S through IS refresh + smoke for OS choreography.

---

## 13) Cohort Automation

* Survey export → `/training/90-assessments/cohort_assign.json`
* Script assigns lab IDs, pushes calendar invites, and posts Slack welcome DM with links to the exact materials.

---

## 14) Quality Gates on Materials

* Every doc has **owner**, **last-reviewed**, **next-review**.
* Lint: broken links, stale snippets (profile flags), and type-canon drift flagged weekly.
* Retire/replace policy: anything older than 6 months must be re-verified or marked **legacy**.

---

## 15) “Day 0” Pack (new hire ready)

* One-page **Orientation** + **Toolchain install guide** + **Hello SSDT** lab.
* Direct links: PR template, publish profiles, type mapping canon, snippet library.
* Slack channels to join; who to ping for IS refresh.

---

## 16) Example PRs & Artifacts (gold standards)

* **Additive PR** with perfect artifacts.
* **Tightening PR** across two trains.
* **Refactor PR** with contract view and sunset plan.
* Each includes: diff HTML, `publish.sql`, scan report, PostDeploy log, IS report, smoke results.

---

## 17) FAQ (high-friction questions answered tersely)

* *Why not tighten immediately?* To avoid consumer break; we enforce on Train+1 with evidence.
* *Why not view everything?* Contracts are for hot domains; direct bind keeps velocity & clarity.
* *How do I rename safely?* Use RefactorLog or parallel+alias; never in-place destructive rename in Prod.

---

## 18) Style Guide (authoring materials)

* Write outcomes as **can-do** statements.
* Put runnable snippets first, theory second.
* Include **time budgets** and **what to submit**.
* Cross-link to decision tables and checklists.

---

## 19) Versioning & Changelog

* SemVer the library (`vX.Y`).
* `CHANGELOG.md` records: added labs, updated profiles, new decisions.
* Breaking changes flagged and broadcast in #engineering.

---

## 20) Accessibility & Inclusivity

* All videos captioned.
* Fonts ≥ 12pt in printables; dark-mode friendly code blocks.
* Alternate text for diagrams; keyboard-only navigation in docs.

---

## 21) Governance of the Library

* **Stewards:** 1 Lead (editor-in-chief), 1 IC-S (technical), 1 OS-DEV (platform), 1 DBA/Ops (pipeline).
* Monthly review: top 5 friction points → new lab or snippet.
* Quarterly audit: retire outdated materials, refresh canon.

---

## 22) Sample Weekly Schedules (by cohort)

**Foundation (Week-1)**

* Mon: Hello SSDT (F-1)
* Tue: Seeds & Grants (F-2)
* Wed: IS/SS choreography
* Thu: Static→Lookup mini-migration
* Fri: Mini-exam + retro

**Intermediate (Week-3)**

* Mon: Tightening lab
* Tue: FK+Index lab
* Thu: Merge-conflict dojo
* Fri: Practicum

**Advanced (Week-5)**

* Tue: Split with contract
* Thu: Parallel type migration
* Fri: Perf clinic + incident simulation

---

## 23) “Red Team” Cards (challenge assumptions)

* **Card A:** “This rename will appear as drop/create in the diff—what now?” (Answer: abort, fix RefactorLog or pivot to parallel + alias.)
* **Card B:** “PostDeploy UPDATE ran twice—should that matter?” (Idempotency must ensure it doesn’t.)
* **Card C:** “Static enums exploded—do we migrate?” (If shared/audited/joined: yes; follow Static→Lookup.)

---

## 24) Self-Check Micro-Quizzes (5–8 mins each)

* After each video/playbook: 3 MCQs + 1 scenario.
* Auto-score maps to cohort deltas (Foundation→Intermediate signals).

---

## 25) Ready-to-Paste Templates

* **PR Template** (change type, risk class, evidence, verification, rollback).
* **Train Close-out** (artifacts uploaded, IS report, smoke results, anomalies).
* **Decision Log** page stub (Context, Options, Decision, Why, Owner, Sunset).

---

## 26) “Tripwire” Smoke Pack (SQL & OS)

* SQL: `SELECT COUNT(*)` from critical lookups; quick join latency probes.
* OS: 3 golden screens per domain; success path only; duration and error rate.

---

## 27) Performance Clinics: Packaged Exercises

* Diagnose a plan regression after FK addition; propose filtered vs covering index; measure improvement.
* SARGability refactor: predicate rewrite to enable seeks.

---

## 28) Advanced Patterns Cookbook

* **Blue/green schema shadowing**
* **Materialized view (indexed) caveats**
* **Rowversion-based optimistic concurrency**
* **Cross-DB synonyms with PreDeploy guards**

Each with risks, when-to-use, and rollbacks.

---

## 29) Maintenance Scripts & Checkers

* **drift-check.ps1**: compares UAT snapshot to project; alerts on external drift.
* **postdeploy-double-run.ps1**: reruns PostDeploy against a Dev clone, parses logs for idempotency signals.
* **is-todo-from-diff.ps1**: emits a Markdown checklist of extensions to refresh.

---

## 30) Success Criteria for TRN.MAT.1004 (how we know it works)

* New hires complete **Day-0** pack in < 4h and land an additive PR in Week-1.
* ≥ 85% of PRs include correct artifacts on first pass.
* Tightening changes: zero Sev incidents; **two-train** adherence ≥ 95%.
* Survey deltas show **Foundation→Intermediate** uplift ≥ 30% within 6 weeks.
* Quarterly advanced refactors: zero Sev-2+ tied to training gaps.

---

### Final note

This library is **not static**. Treat TRN.MAT.1004 as code: version it, review it, and evolve it. Each incident, each retro, each performance win should produce a *diff* in this library—so the next developer moves faster and safer than the last.

Absolutely—here’s a **production-ready, collated `README.md`** you can drop into `/docs` (or the repo root). It’s expanded far beyond a skeleton—**role-aware, checklist-driven, copy-paste friendly**, and wired to our ontology (sections 7–12). Replace `[…]` link targets with your actual paths.

---

# External Entities Cutover & SSDT Adoption — **Handbook README**

> **Audience:** ICs (J/M/S), Leads/Staff, DBA/Ops, OutSystems devs, QA.
> **Purpose:** A single place that gets you from *idea → safe change → shipped* using **SSDT/DACPAC** and **OutSystems** External Entities.
> **Prime directive:** **DB publish first → IS refresh/publish → SS dependency refresh → smoke** (daily window).

---

## 0. Table of Contents

1. [Start Here (TL;DR)](#1-start-here-tldr)
2. [Daily Train (DB→IS→SS→Smoke)](#2-daily-train-dbissssmoke)
3. [How to Submit a Schema PR](#3-how-to-submit-a-schema-pr)
4. [Integration Studio Refresh (Extensions)](#4-integration-studio-refresh-extensions)
5. [Anti-Patterns (Top 10 w/ Counters)](#5-anti-patterns-top-10-w-counters)
6. [Troubleshooting Index (symptom → fix)](#6-troubleshooting-index-symptom--fix)
7. [Playbooks Index (expand/contract, rename, static→lookup…)](#7-playbooks-index)
8. [Decision Tables (direct vs contract, tightening, rename)](#8-decision-tables)
9. [Type Mapping Canon (DB ↔ OutSystems)](#9-type-mapping-canon-db--outsystems)
10. [Profiles, Pipelines & Safety Nets](#10-profiles-pipelines--safety-nets)
11. [Checklists (printables)](#11-checklists-printables)
12. [Glossary](#12-glossary)
13. [Change Log & Ownership](#13-change-log--ownership)

---

## 1) Start Here (TL;DR)

* **One place of change:** SSDT `.sqlproj` → **DACPAC** publish using **profiles** (Prod profiles block data loss & drops).
* **Change posture:** **Add → Backfill → Tighten** (NOT NULL/CHECK/FK in the next train).
* **Contracts:** Use **views** only when **≥2** drivers: fan-out, churn risk, security shaping, perf shaping, cross-team boundary, evolving keys.
* **OutSystems choreography:** After DB publish, **Integration Studio** (IS) refresh/publish extensions → **Service Studio** (SS) refresh dependencies → smoke.
* **Artifacts or it didn’t happen:** PR must carry **diff report**, **publish profile**, **forbidden-drop scan**, **idempotency proof**.

**Quick links**

* PR Template → `[…] /docs/templates/PR_TEMPLATE.md`
* Deployer Runbook → `[…] /docs/runbooks/deployer.md`
* Type Mapping Canon → `[…] /docs/canon/type-mapping.md`
* Playbooks → `[…] /docs/playbooks/`

---

## 2) Daily Train (DB→IS→SS→Smoke)

**Cadence (Dev/Test/UAT; Prod is windowed)**

* **13:30** last-call ping → **14:00** PR cutoff → **15:00** publish (DACPAC) → **15:30** IS refresh & publish → **15:40–15:55** smoke → **15:55** announce green/amber/red.

**Golden order (never change):**

1. **Publish DB** with profile (`publish.sql`, logs captured).
2. **IS Refresh → Publish** all touched extensions.
3. **SS Refresh Dependencies → Publish** consumer modules.
4. **Smoke pack** runs; anomalies filed with owners.

**SLOs:** IS complete ≤ **30m** after publish; SS deps refreshed ≤ **60m** after IS.

**Artifacts per train:**
`manifest.json`, `diff-report.html`, `publish.sql`, `publish.log`, `profile.used.json`, `is.refresh.report.md`, `ops.postcheck.md`.

---

## 3) How to Submit a Schema PR

**You must include these 6 things**

1. **Diff report** (project↔target snapshot).
2. **Publish profile** used (`profiles/UAT.publish.xml`).
3. **Forbidden-drop scanner** result = **PASS**.
4. **PostDeploy idempotency proof** (double-run; rowcounts).
5. **RefactorLog** entries for renames **or** parallel+alias plan.
6. **OutSystems mapping table** (DB↔OS types/lengths/nullability).

**PR template (paste)**

```markdown
# Change Summary
Type: (Additive | Tighten | Refactor | DataMotion | Security)
Risk: (Low | Medium | High)
Domains: …

# Evidence
- [ ] Diff report attached (project↔target)
- [ ] Profile: `profiles/UAT.publish.xml`
- [ ] Forbidden-drop scan: PASS (link)
- [ ] PostDeploy idempotent proof (rowcounts; double-run)
- [ ] RefactorLog present (if rename) or Parallel+Alias plan
- [ ] OS type mapping table included

# Verification
- [ ] Local build ✓  [ ] Dev publish dry-run ✓
- [ ] FK supporting index ✓  [ ] Pre/Post guards reviewed ✓

# Rollback
Plan (revert, disable constraint, restore point)
```

**When NOT to submit**

* You are enforcing **NOT NULL** in the same PR where you introduced the column (stage it to Train+1).
* A rename appears in the diff as **DROP/CREATE** (fix RefactorLog or use parallel+alias).
* PostDeploy contains non-idempotent `INSERT` or unbatched UPDATE on a large table.

---

## 4) Integration Studio Refresh (Extensions)

**Extension granularity**: **one per domain** (e.g., `CustomerDB`, `SalesDB`), not a monolith.
**Ownership**: each extension has **Owner** + **Backup** + channel tag (e.g., `#domain-customer`).

**Refresh steps (per extension)**

* Open **IS**, verify **connection target** (env/catalog).
* **Compare**: review attributes/keys; watch for PK inference on views.
* **Regenerate → Publish**; capture a mini report:

```
Extension: CustomerDB
Changes: +2 attributes (Customer.Email, Customer.Status), 0 drops
Duration: 00:00:11
Owner: @jane.d
```

**Consumer follow-through (Service Studio)**

* **Refresh Dependencies → Publish** for **all** consuming modules.
* Use roster/bot output to ensure **nothing is missed**.

**Read vs write**

* View-backed entities are **read-only**; write via domain actions to base tables or stored procs.

---

## 5) Anti-Patterns (Top 10 w/ Counters)

1. **Tighten immediately after add** → **Counter:** stage to Train+1; backfill first.
2. **Rename in place on hot tables** → **Counter:** RefactorLog *or* parallel column + alias via view.
3. **TRUNCATE/INSERT seeds** → **Counter:** idempotent **MERGE** with keys.
4. **FK without supporting index** → **Counter:** add index on child FK column(s).
5. **Drop objects not in source (Prod)** → **Counter:** Prod profile forbids; use explicit deprecations.
6. **Long unbatched updates in daytime** → **Counter:** batched `TOP(N)` + `ROWLOCK/READPAST`, off-peak.
7. **SS refresh before IS publish** → **Counter:** keep golden order (DB→IS→SS).
8. **View-everything** → **Counter:** contract only when ≥2 drivers.
9. **Cross-DB hard-coding** → **Counter:** synonyms + sqlcmd vars + PreDeploy guards.
10. **No artifacts** → **Counter:** PR gate blocks merges without diff/profile/scan/idempotency.

---

## 6) Troubleshooting Index (symptom → fix)

* **“IS sees no changes”** → Wrong catalog or publish missed → verify profile target; redo publish; check view PK metadata.
* **“NOT NULL add failed”** → Backfill incomplete; consumers not ready → revert to nullable; complete backfill; enforce next train.
* **“Permission denied”** → Missing GRANT → run idempotent PostDeploy grant; test connection in IS.
* **“Publish wants to DROP in Prod”** → Rename not captured or profile bad → fix RefactorLog; ensure `DropObjectsNotInSource=False`; regenerate.
* **“Length exceeded”** → Tightened before consumers → temporarily widen (or alias via view), fix consumers, tighten later.
* **“Backfill locks”** → Unbatched update → batch with `TOP`, add supporting index, schedule off-peak.
* **“SS refresh crash”** → Version skew / mid-publish → wait for IS completion; restart SS; refresh again.

---

## 7) Playbooks Index

* **Additive → Tighten (two-train)** → `[…] /docs/playbooks/additive-to-tighten.md`
* **Static → DB Lookup** → `[…] /docs/playbooks/static-to-lookup.md`
* **Rename (parallel + alias)** → `[…] /docs/playbooks/rename-parallel-alias.md`
* **Split/Merge with Contract View** → `[…] /docs/playbooks/split-merge-contract.md`
* **Batched Backfill (large sets)** → `[…] /docs/playbooks/batched-backfill.md`
* **FK + Supporting Index** → `[…] /docs/playbooks/fk-index.md`
* **IS/SS Choreography** → `[…] /docs/playbooks/os-integration-choreo.md`

Each playbook has: **step list**, **copy-paste snippets**, **acceptance tests**, **rollback**, **owner**.

---

## 8) Decision Tables

**Direct vs Contract (view)**
Use **contract** when **≥2** of: **fan-out ≥5**, **churn**, **security shaping**, **perf shaping**, **ownership boundary**, **evolving keys**. Else **direct bind**.

**Staged Tightening**

* **Add** (nullable/wider) → **Backfill** (idempotent) → **Tighten** (NOT NULL/CHECK/FK) next train.
* Collapse stages only for **trivial** cases with Lead/DBA sign-off.

**Rename**

* Low fan-out → **RefactorLog** rename.
* High/Hot → **Parallel column/table** + **alias via view** → sunset → drop later.

(Full matrices → `[…] /docs/canon/decision-tables.md`)

---

## 9) Type Mapping Canon (DB ↔ OutSystems)

* **Text**: Email `NVARCHAR(320)`, Name `NVARCHAR(100)`, Code `NVARCHAR(16)`
* **Money**: `DECIMAL(18,2)`
* **Boolean**: `BIT`
* **Datetime**: `DATETIME2(3|7)`
* **Keys**: INT IDENTITY ↔ Integer Identifier; UNIQUEIDENTIFIER ↔ explicit mapping strategy

> Keep the **canon** authoritative: `[…] /docs/canon/type-mapping.md` (PRs must update this when shapes change).

---

## 10) Profiles, Pipelines & Safety Nets

**Publish profiles (Prod/UAT/Test)**

```xml
<BlockOnPossibleDataLoss>True</BlockOnPossibleDataLoss>
<DropObjectsNotInSource>False</DropObjectsNotInSource>
<IncludeTransactionalScripts>True</IncludeTransactionalScripts>
<VerifyDeployment>True</VerifyDeployment>
<CommandTimeout>1200</CommandTimeout>
```

**PR pipeline gates**

* Build **DACPAC** → generate **diff** → run **forbidden-drop scanner** → validate **RefactorLog** on renames → lint **PostDeploy** for idempotency and transactions → attach artifacts → block merge if any fail.

**Dev note:** Only sandbox Dev DBs may allow `DropObjectsNotInSource=True` for clean resets—**never shared Dev**.

---

## 11) Checklists (printables)

**Deployer (per train)**

* Pre: window locked; manifest frozen; backups ready; artifacts staged; comms posted.
* During: run publish with profile; tail log; verify rowcounts; capture artifacts.
* Post: IS refresh list executed; SS deps refreshed; smoke passed; announce green; update `ops.postcheck`.

**Developer PR**

* Names/types/lengths/nullability set; FK+index; PostDeploy idempotent & transactional; mapping table included; diff/profile/scan attached; rollback noted.

**IS Refresh (per extension)**

* Target checked; deltas reviewed; regenerate/publish; mini report filed; consumers roster pinged.

Grab printable PDFs: `[…] /docs/printables/`

---

## 12) Glossary

* **SSDT** — SQL Server Data Tools; declarative DB project → DACPAC.
* **DACPAC** — compiled schema model used to diff/publish.
* **RefactorLog** — tool-maintained rename ledger (prevents drop/create).
* **Contract View** — stable read surface (alias/shape) shielding consumers.
* **Expand/Contract** — zero-downtime refactor tactic (add/parallel → switch → tighten).
* **IS/SS** — Integration Studio / Service Studio (OutSystems).

---

## 13) Change Log & Ownership

**Owners**

* Docs: `@lead-eng` (editor), `@senior-db`, `@os-dev-lead`, `@ops-release`.
* Review cycle: **monthly** for canon; **weekly** for checklists.

**Changelog (excerpt)**

* `v1.4.0` — Added “Rename (parallel + alias)” playbook; updated type canon with Email(320).
* `v1.3.0` — New IS mini-report; tightened Prod profile with `VerifyDeployment=True`.
* `v1.2.0` — Added Troubleshooting Index and printable checklists.

---

## Appendix A — Copy/Paste Snippets

**Idempotent MERGE (seed)**

```sql
MERGE dbo.Country AS T
USING (VALUES ('US','United States'), ('CA','Canada')) AS S(Code,Name)
ON (T.Code = S.Code)
WHEN NOT MATCHED BY TARGET THEN INSERT(Code,Name) VALUES (S.Code,S.Name)
WHEN MATCHED AND NULLIF(T.Name,S.Name) IS NOT NULL THEN UPDATE SET Name=S.Name;
```

**Batched backfill**

```sql
DECLARE @batch INT = 2000;
WHILE 1=1
BEGIN
  WITH todo AS (
    SELECT TOP (@batch) Id FROM dbo.Big WITH (READPAST, ROWLOCK)
    WHERE NewCol IS NULL ORDER BY Id)
  UPDATE b SET NewCol = <expr>
  FROM dbo.Big b JOIN todo t ON t.Id=b.Id;
  IF @@ROWCOUNT=0 BREAK;
END
```

**FK + index**

```sql
ALTER TABLE sales.[Order]
  ADD CONSTRAINT FK_Order_Customer FOREIGN KEY (CustomerId)
  REFERENCES customer.Customer(CustomerId);
CREATE INDEX IX_Order_CustomerId ON sales.[Order](CustomerId);
```

**View-as-contract**

```sql
CREATE VIEW customer.vPerson_Contract AS
SELECT p.PersonId, p.GivenName AS FirstName, p.FamilyName AS LastName,
       TRY_CONVERT(date, p.BirthDateTime) AS BirthDate
FROM customer.Person p;
```

**PreDeploy guard**

```sql
IF NOT EXISTS (SELECT 1 FROM dbo.DeployWindow WHERE IsOpen=1)
  THROW 51000,'Deployment window is closed',1;
```

**Idempotent GRANT**

```sql
IF NOT EXISTS (
  SELECT 1 FROM sys.database_permissions p
  JOIN sys.objects o ON o.object_id=p.major_id
 WHERE o.schema_id=SCHEMA_ID('sales') AND o.name='vOrder_Contract'
   AND p.permission_name='SELECT'
   AND USER_NAME(p.grantee_principal_id)='$(APP_USER)')
GRANT SELECT ON sales.vOrder_Contract TO [$(APP_USER)];
```

---

### Using this README

* **New devs:** read §1–4, run one additive PR using the template, and shadow a train.
* **IC-M/S:** jump to §7 Playbooks & §8 Decisions for refactors.
* **Ops/DBA:** pin §2 Daily Train, §10 Profiles/Pipelines, §11 Deployer checklist.
* **OS-devs:** pin §4 IS Refresh and §9 Type Mapping Canon.

> **Mantra:** **Add now, tighten later.** **Refactor with refuge (contracts/parallel).** **Artifacts or it didn’t happen.** **Trains make change safe.**

Locked to your outline. Here’s **Section 13 — Quality, Risk, and Anti-Patterns (QLT)**, expanded into a developer-facing, ops-ready guide with **what/why**, **how to detect**, **how to prevent**, **how to fix**, and **copy-paste snippets**. It aligns to your IDs and folds in all relevant details from prior sections.

---

# 13) Quality, Risk, and Anti-Patterns (QLT)

> **North Star:** Changes are **repeatable**, **observable**, and **reversible**. Every risky move has a **guard**, a **fallback**, and a **proof** that it’s safe to run **twice**.

---

## QLT.ANP.1301 — Non-Idempotent Post-Deploy

**What it is**
Post-deploy data motion that **mutates state differently on re-run** (duplicates rows, corrupts counts, re-applies destructive updates).

**Why it bites**
Pipelines retry; humans re-run; Dev/UAT rehearse multiple times. Non-idempotent scripts create drift that SSDT cannot reason about.

**Detect it (before merge)**

* **Static lint:** flag `INSERT` without key predicate, `TRUNCATE`, `DELETE` without guard, `UPDATE` without deterministic `WHERE`.
* **Harness:** “double-run” Dev job: execute PostDeploy **twice**, parse `@@ROWCOUNT` deltas, fail if second run changes > 0 rows (unless explicitly allowed).

**Prevent it (patterns)**

* Prefer **`MERGE`** keyed by **immutable business keys**.
* Guard with **`IF NOT EXISTS`**, **`EXISTS`**, or **`NOT MATCHED`** branches.
* Wrap in **`TRY/CATCH + TRAN`** and **PRINT**/log counts.

**Fix it (when found post-merge)**

* Replace non-deterministic `INSERT`/`UPDATE` with `MERGE` keyed on business keys.
* Add **compensating steps** to dedupe (temporary staging + `ROW_NUMBER()` cleanup), then re-establish idempotency.

**Idempotent MERGE seed (drop-in)**

```sql
BEGIN TRY
  BEGIN TRAN;
  MERGE dbo.OrderStatus AS T
  USING (VALUES
    (1,'Pending'),
    (2,'Shipped'),
    (3,'Cancelled')
  ) AS S(Id,Name)
  ON (T.Id = S.Id)
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, Name) VALUES (S.Id, S.Name)
  WHEN MATCHED AND NULLIF(T.Name, S.Name) IS NOT NULL THEN
    UPDATE SET Name = S.Name;
  PRINT CONCAT('OrderStatus upserted rows: ', @@ROWCOUNT);
  COMMIT TRAN;
END TRY
BEGIN CATCH
  IF XACT_STATE() <> 0 ROLLBACK TRAN;
  THROW;
END CATCH
```

**Double-run harness (Dev gate, concept)**

* Run PostDeploy twice; assert the second pass prints “upserted rows: 0” (or a known harmless constant).
* Gate PR with an attached **screenshot/log** of the second pass.

---

## QLT.ANP.1302 — Forbidden Drops/Renames

**What it is**

* **Drops:** `DROP TABLE/VIEW/PROC/…` or `ALTER TABLE … DROP COLUMN` in environments that must not lose objects/data.
* **Renames:** In-place renames that appear as **drop/create** to SSDT (no RefactorLog).

**Why it bites**
Loss of data/permissions; hard breaks for consumers; SSDT drift that cannot be auto-reconciled.

**Detect it**

* **Forbidden-drop scanner** runs on `publish.sql` and **fails** on Prod/UAT/Test.
* Diff shows rename as **DROP + CREATE** rather than **RENAME** (RefactorLog missing).

**Prevent it**

* **Profile policy**: `BlockOnPossibleDataLoss=True`, `DropObjectsNotInSource=False` (non-negotiable in Prod/UAT/Test).
* **Rename discipline**: tool-based rename → **RefactorLog**; for hot objects, prefer **parallel + alias** via contract view.
* **Sunset plans**: any alias/compat object has a **removal train** scheduled.

**Fix it**

* If rename manifested as drop/create: **revert**, perform tool-based rename (or parallel+alias), re-generate diff.
* If drop desirable: deprecate explicitly (feature flags, contract view), **archive**, and remove in a dedicated train with approval.

**Scanner (regex sketch, concept)**

* Block patterns: `\bDROP\s+(TABLE|VIEW|PROCEDURE|FUNCTION|INDEX)\b`, `ALTER\s+TABLE\s+.+\s+DROP\s+COLUMN`, **narrowings** of types.
* CI blocks PR if hits occur and target `profile.used.json` is Prod/UAT/Test.

**Parallel + alias (safe rename alternative)**

```sql
-- Expand
ALTER TABLE sales.[Order] ADD OrderNumber_v2 NVARCHAR(32) NULL;
UPDATE o SET OrderNumber_v2 = CAST(OrderNumber AS NVARCHAR(32)) FROM sales.[Order] o;
-- Contract view shields consumers
CREATE OR ALTER VIEW sales.vOrder_Contract AS
SELECT OrderId, OrderNumber_v2 AS OrderNumber, ... FROM sales.[Order];
-- Switch consumers → later enforce NOT NULL on _v2 → drop legacy column in a future train
```

---

## QLT.ANP.1303 — Hard-coded Cross-DB

**What it is**
Literal DB names/schema paths spread across objects (`FROM ProdServer.OtherDb.dbo.Table`) or app configs, coupling environments and breaking reproducibility.

**Why it bites**
Schema compare can’t govern external catalogs; refactors get stuck; non-Prod runs against Prod by mistake.

**Detect it**

* **Lint** for `\[[A-Za-z0-9_]+\]\.[A-Za-z0-9_]+\.` patterns in objects.
* Review PreDeploy/PostDeploy for **embedded** DB names.

**Prevent it (policy)**

* Use **sqlcmd variables** for external names.
* Prefer **synonyms** under a policy layer; refresh them in **PostDeploy**.
* Add **PreDeploy guards** to assert the expected external target exists.

**Fix it**

* Centralize names (sqlcmd) and create synonyms; refactor references to point to synonyms; remove literals.

**Synonym pattern with sqlcmd & guards**

```sql
-- PreDeploy guard
IF DB_ID('$(EXT_DB)') IS NULL
  THROW 51001, 'External DB not found: $(EXT_DB)', 1;

-- PostDeploy synonym refresh (idempotent)
IF OBJECT_ID('dbo.Orders_ext','SN') IS NOT NULL DROP SYNONYM dbo.Orders_ext;
EXEC('CREATE SYNONYM dbo.Orders_ext FOR [' + '$(EXT_DB)' + '].sales.[Order]');
```

---

## QLT.ANP.1304 — No-Txn Multi-Insert (and unsafe large updates)

**What it is**
Bulk inserts/updates that run without a **transaction** and **error handling**, or without **batching**, leading to partial state, long-held locks, and timeouts.

**Why it bites**
Partial failures are worst-case for audits and rollback; long scans lock out the app; retries multiply damage.

**Detect it**

* Lint for **absence** of `BEGIN TRAN / COMMIT` with DML; **absence** of `TRY/CATCH`.
* Heuristic: UPDATE without `TOP (N)` on a large table; missing `ROWLOCK/READPAST` hints during backfill.

**Prevent it**

* Wrap multi-step DML in `TRY/CATCH + TRAN`.
* Use **batched** backfills with predictable chunk size.
* **PRINT**/log invariants: `@@ROWCOUNT`, counts before/after, duration.

**Fix it**

* Convert to **batched** pattern; add indexes to support `WHERE` clause; schedule during **low-traffic** windows.

**Transactional, batched update (template)**

```sql
DECLARE @batch INT = 1000;
WHILE 1=1
BEGIN
  BEGIN TRY
    BEGIN TRAN;
    WITH todo AS (
      SELECT TOP (@batch) Id
      FROM dbo.Big WITH (READPAST, ROWLOCK)
      WHERE NewCol IS NULL
      ORDER BY Id
    )
    UPDATE b SET NewCol = <expr>
    FROM dbo.Big b JOIN todo t ON t.Id = b.Id;

    DECLARE @c INT = @@ROWCOUNT;
    PRINT CONCAT('Batch updated rows: ', @c);
    COMMIT TRAN;
    IF @c = 0 BREAK;
  END TRY
  BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRAN;
    THROW;
  END CATCH
END
```

---

## QLT.SIG.1305 — Quality Signals (what we watch, thresholds, and how to react)

> **Interpretation:** Signals are **coaching tools**, not punishment. If a metric moves, we ask “what behavior changed?” then update **training**, **playbooks**, or **policy**.

### Signals & Targets

| Signal                                    | Definition / How measured                                     | Target / SLO                                       | Action if breaching                                                                                      |
| ----------------------------------------- | ------------------------------------------------------------- | -------------------------------------------------- | -------------------------------------------------------------------------------------------------------- |
| **Safety Blocks**                         | Count of CI blocks from forbidden-drop scanner (by env)       | **Prod:** 100% blocked; **UAT/Test:** 100% flagged | If recurring on same dev/domain → run a **rename/parallel clinic**; expand decision tables with examples |
| **RefactorLog Entries (rename coverage)** | % of rename diffs represented as **RENAME** (not drop/create) | **≥ 95%**                                          | If lower: enforce tool-based rename PR checklist; when hot, **mandate** parallel+alias                   |
| **Idempotent Coverage**                   | % of PostDeploy scripts that pass **double-run harness**      | **≥ 95%**                                          | Add MERGE and TRY/CATCH drills to labs; require proof artifacts in PR                                    |
| **Defect Trend (post-train)**             | Incidents per week within 2h of train (green/amber/red)       | **Down-trend over month**                          | If up: inspect deltas; add guardrails (PreDeploy checks), add IS timing SLO enforcement                  |
| **Rollback Need Frequency**               | % of trains requiring revert/restore                          | **≤ 2%** (per quarter)                             | Investigate root cause; tighten pipeline gates; add rehearsal for risky steps                            |
| **IS Refresh SLA**                        | Time from DB publish → IS publish complete                    | **≤ 30m**                                          | Add owner map/automation; pre-stage connection tests; reduce ext granularity if too coarse               |
| **SS Dependency SLA**                     | Time from IS publish → all consuming modules refreshed        | **≤ 60m**                                          | Roster bot nudges; per-module owners; escalate chronic laggards                                          |

### Collection & Visibility

* **Pipeline emits** `profile.used.json`, scanner results, PostDeploy logs, and double-run proofs as artifacts.
* **Ops dashboard** (weekly): trend lines for each signal; heatmap by domain and by change type.
* **Retro cadence**: pick top **two** metrics to improve next week; ship one **policy/doc** change and one **training** change.

### Guardrail Upgrades (when signals degrade)

* **Too many scanner blocks** → teach **parallel + alias**; add “rename rehearsal” lab; enrich decision tables with real examples.
* **Idempotency dips** → make double-run harness **blocking**; commit snippet library updates; require rowcount printing.
* **IS/SS SLA misses** → reduce extension breadth; pre-assign refresh owners; build diff→extension mapping bot.

---

## Put it all together (QA posture)

**Change design**

* Prefer **additive**; plan for **two-train** tightening; if rename is tempting, try **parallel + alias** first.

**Pipeline**

* Profiles **forbid drops**; scanners run; **RefactorLog** enforced; PostDeploy **double-run** proven.

**Ops**

* Daily **DB→IS→SS→Smoke** ritual with SLOs; artifacts uploaded; deviations logged.

**Learning loop**

* Signals flow to **training** (labs), **docs** (playbooks), **policy** (profiles/gates). Incidents become **curriculum** within a week.

---

## One-page “QA Tripwires” (paste into your runbook)

* **Tripwire 1:** Publish script contains DROP/ALTER DROP → **Stop**. Check RefactorLog; switch to parallel+alias.
* **Tripwire 2:** PostDeploy has `INSERT` without keys → **Stop**. Replace with `MERGE`.
* **Tripwire 3:** Large UPDATE without batching → **Stop**. Add `TOP(N)` loop + `ROWLOCK/READPAST`.
* **Tripwire 4:** Cross-DB literal spotted → **Stop**. Move to sqlcmd var + synonym; add PreDeploy guard.
* **Tripwire 5:** Rename diff shows drop/create → **Stop**. Redo rename via tool or pivot strategy.

---

### Closing mantra

**Idempotent by default.**
**Rename with a map (or don’t).**
**No literals across DBs.**
**Every multi-row change is transactional & batched.**
**Quality is visible: we measure, coach, and improve.**
