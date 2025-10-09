Below is a prioritized, tiered blueprint of the **essential inclusions** for making the SSDT + Azure DevOps + Octopus operating model succeed across **6 pods (2 devs + a dev lead each), ~300 entities, 12 modules**, and a path to **CDC enablement**, while **explicitly acknowledging a short-term velocity dip** and **committing to a return-to-velocity** once the system stabilizes. Each tier includes: what must be understood, the behaviors to institutionalize, the artifacts to ship, owners, metrics, and recommended diagrams. After the tiers, you’ll find a **Canonical Essentials List** to “lock” the scope for validation.

---

# Tier 1 — Existential: What must land immediately (or the program fails)

## 1) One Source of Truth & Minimal Survival Loop

**What:** SSDT project is the authoritative schema; everyone can run the loop: *change → build → PR → deploy → Integration Studio refresh*.
**Behaviors:**

* **Always build locally**; fix compile errors before PR.
* **Always open a PR**; no direct pushes to protected branches.
* **Always refresh External Entities** after DB deploy; republish the affected extension/module.
  **Artifacts:**
* SSDT project in repo with clear folder structure.
* **PR template** (risk callouts, migration type, test notes).
* **“Survival loop” runbook** (10-step laminated recipe).
  **Owners:** Dev Leads (enforce), Senior ICs (model), Octopus/Ops (pipeline health).
  **Metrics:** Build pass %, PR lead time, deploy success %, refresh completion %.
  **Diagram:** ***Swimlane*** for the survival loop across roles.

## 2) Cadence That Prevents Collisions

**What:** A simple, enforced **release train** for Dev and a ritualized **daily refresh window**.
**Behaviors:**

* **14:00 PR cutoff → 15:00 Dev deploy → 15:30 team-wide refresh.**
* Test/Prod on a predictable weekly/biweekly schedule; **no Friday Prod** during ramp-up.
  **Artifacts:**
* Calendar holds, Slack reminders, “Day-of” checklist.
  **Owners:** Dev Leads (pods), Release Manager/Ops.
  **Metrics:** % of pods hitting refresh on time, change-fail rate.
  **Diagram:** ***Timeline/Train track*** showing cutoff → deploy → refresh.

## 3) Non-Negotiable Guardrails (Safety Nets)

**What:** Small rules that massively reduce cross-pod breakage.
**Behaviors:**

* **No prod edits** outside SSDT.
* **No `SELECT *`** in any view.
* **BlockOnDataLoss** enabled for Test/Prod.
* **Two-phase rule** for any potentially breaking change.
  **Artifacts:**
* CI gate: forbidden-drop/`SELECT *` scanner.
* Policy file with 1-page “Golden Rules”.
  **Owners:** Dev Leads (reviews), Pipeline Owner (CI gates).
  **Metrics:** Incidents from breaking changes, CI gate failure rate.
  **Diagram:** ***Icon list*** (shield/lock/check).

## 4) Role Clarity Across 6 Pods

**What:** Everyone knows their lane and handoffs.
**Behaviors:**

* **Jr IC:** additive changes via playbooks; raise flag after 15 min stuck.
* **Sr IC:** refactor safety, reviews, two-phase design, mentor.
* **Dev Lead:** cadence, standards, exception handling.
* **QA/Ops:** post-deploy checks, drift monitoring, rollback readiness.
  **Artifacts:**
* One-page **Role Charter** per role.
  **Owners:** EM/Dev Leads.
  **Metrics:** Review SLA, rework rate, time-to-unblock.
  **Diagram:** ***Swimlane*** across pods/roles.

---

# Tier 2 — Stabilization: Reduce friction so the new model is livable

## 5) Two-Phase Schema Evolution (Default Pattern)

**What:** **Add → backfill/alias → enforce/remove**; never in-place break.
**Behaviors:**

* Treat **rename** as add+alias+migrate+deprecate.
* New non-nullable columns: add as nullable + default, backfill, then enforce later.
  **Artifacts:**
* Two-phase **checklist**; refactorlog how-to; sample PRs.
  **Owners:** Senior ICs, Dev Leads.
  **Metrics:** Production hotfixes due to schema breaks, # of phased PRs.
  **Diagram:** ***Two-phase timeline*** (A → B).

## 6) Contract-Minimalism (Views Where They Pay Off)

**What:** Introduce **contract views** only when risk warrants (fan-out, churn, PII, cross-team, key/shape volatility).
**Behaviors:**

* Start **direct** for low-risk tables; **promote to view** if risk increases.
* Never `SELECT *` in views; explicit column list.
  **Artifacts:**
* **Decision tree**; generator script for pass-through views; `contract` schema convention.
  **Owners:** Dev Leads (design approval), Senior ICs (implementation).
  **Metrics:** # of consumers vs. changes per entity; view coverage where justified.
  **Diagram:** ***Decision tree*** (Yes/No branches).

## 7) Static Data & Post-Deploy Discipline

**What:** 100 static entities require repeatable, idempotent updates.
**Behaviors:**

* Use idempotent **MERGE** scripts in post-deploy; seed data versioned in repo.
* No inline ad-hoc data tweaks in production.
  **Artifacts:**
* Static data folder with canonical CSV/SQL; sample MERGE templates.
  **Owners:** Senior ICs (templates), QA (validation), Ops (runbooks).
  **Metrics:** Post-deploy idempotency pass rate; drift between envs.
  **Diagram:** ***Flow***: repo data → post-deploy MERGE → verification.

## 8) Review Quality & CI Gates

**What:** Reviews catch dangers; CI automates boring checks.
**Behaviors:**

* Reviewers check for phased patterns, refactorlog, indexes, data loss.
* CI enforces **build pass**, gate scans, environment-specific guards.
  **Artifacts:**
* **Code Review Checklist**; CODEOWNERS for DB areas.
  **Owners:** Dev Leads, designated DB champions.
  **Metrics:** Rework rate per PR; % CI gate pass first time.
  **Diagram:** ***Gate icons*** along the PR pipeline.

## 9) Troubleshooting Playbook (Top 8)

**What:** First-move responses to common failures.
**Behaviors:**

* Look-up first move instead of guessing; escalate patterns quickly.
  **Artifacts:**
* One-pager: error → first move (e.g., SQL71501 → add DB reference).
  **Owners:** Senior ICs maintain; Dev Leads socialize.
  **Metrics:** MTTR for build/deploy issues.
  **Diagram:** ***Compact table*** (short phrases only).

---

# Tier 3 — Adoption: Make habits stick and morale hold

## 10) Champions & Reinforcement Loops

**What:** Human + automated nudges that normalize the new rhythm.
**Behaviors:**

* Seniors **narrate PRs** and post screenshots of “good diffs.”
* Slack **refresh reminders**; “green streak” call-outs.
  **Artifacts:**
* Named **DB champions** per pod; recognition rubric.
  **Owners:** Dev Leads, EM.
  **Metrics:** Participation in rituals; % pods with active champion.
  **Diagram:** ***Org map*** of pods with champion badges.

## 11) Metrics That Actually Matter

**What:** A few simple measures that communicate system health.
**Behaviors:**

* Review a tiny dashboard weekly; share wins and learnings.
  **Artifacts:**
* Dashboard: build pass %, PR lead time, deploy success, refresh SLA, change-fail rate.
  **Owners:** EM/Dev Leads.
  **Metrics:** Trend toward green; variance across pods.
  **Diagram:** ***Mini KPI board*** with trend arrows.

## 12) Knowledge Base & Playbooks (Living)

**What:** Centralized, searchable guides kept current.
**Behaviors:**

* Update docs after every notable incident or pattern.
  **Artifacts:**
* Playbooks (survival loop, two-phase, static data, troubleshooting); annotated PR template; glossary.
  **Owners:** Dev Leads curate; Senior ICs contribute.
  **Metrics:** Doc usage; time-to-onboard new devs.
  **Diagram:** ***Site map*** of the docs.

---

# Tier 4 — Acceleration: The road back to velocity (with safety)

## 13) Return-to-Velocity Roadmap (with Triggers)

**What:** A staged plan to **re-introduce trunk-branch-like agility** once stable.
**Behaviors:**

* Respect **triggers** before loosening controls (e.g., 4–6 weeks of green KPIs, drift=0, change-fail < X%).
  **Artifacts:**
* Roadmap with **gates** for: faster trains, reduced manual reviews for low-risk patterns, more parallel work.
  **Owners:** EM/Leads approve; Ops validates risk.
  **Metrics:** Lead time down; incidents remain low.
  **Diagram:** ***Maturity staircase*** with gate checks.

## 14) Dev Experience Automation

**What:** Remove ergonomic pain without removing safety.
**Behaviors:**

* Template generation for pass-through views; scripts to scaffold two-phase migrations; pre-commit linters.
  **Artifacts:**
* Tooling repo; VS/IDE snippets; PR bots (e.g., auto-label “needs two-phase”).
  **Owners:** Senior ICs/Platform team.
  **Metrics:** Manual review time; PR churn.
  **Diagram:** ***Before/after flow*** of a change with automation.

## 15) CDC Enablement & Data Lineage

**What:** The strategic payoff: reliable change streams and analytics.
**Behaviors:**

* Standardize keys/audit columns; avoid schema flapping on CDC-critical tables.
  **Artifacts:**
* CDC target list; retention/lineage notes; view contracts for change stability.
  **Owners:** Data Eng + DB champions.
  **Metrics:** CDC job health; downstream freshness/latency.
  **Diagram:** ***Data flow*** from OLTP → CDC → downstream.

## 16) Periodic Debt Retirement (“Deprecation Train”)

**What:** Routine removal of temporary artifacts to keep schema lean.
**Behaviors:**

* Monthly review/drop of deprecated columns/views once consumers migrate.
  **Artifacts:**
* Deprecation backlog; checklist for safe removal.
  **Owners:** Dev Leads, Senior ICs.
  **Metrics:** Backlog burn-down; schema simplicity trend.
  **Diagram:** ***Calendar cadence*** icon.

---

# Canonical Essentials List (for audit & tests)

**Tier 1 (Existential):**

1. SSDT as single source of truth.
2. Survival loop runbook: build → PR → deploy → refresh.
3. Release train & daily refresh cadence.
4. Non-negotiable guardrails (no prod edits, no `SELECT *`, data-loss block).
5. Role clarity across pods; dev leads as cadence owners.

**Tier 2 (Stabilization):**
6. Two-phase migration as default.
7. Contract-minimalism with promotion path to views.
8. Static data as code + idempotent post-deploy MERGEs.
9. Review quality + CI gates (forbidden-drop/`SELECT *`, refactorlog).
10. Troubleshooting one-pager for top issues.

**Tier 3 (Adoption):**
11. Champions network + reinforcement rituals.
12. Small, visible KPI board reviewed weekly.
13. Living knowledge base/playbooks.

**Tier 4 (Acceleration):**
14. Return-to-velocity roadmap with explicit stability triggers.
15. Developer experience automation (scaffolds, linters, bots).
16. CDC enablement patterns & lineage.
17. Monthly deprecation train.

---

## How to Use This Tiering in Your Deck & Operations

* **Opening:** Lead with Tier 1 only. Land the survival loop, cadence, and guardrails with absolute clarity.
* **Middle:** Layer Tier 2 practices as “how we keep it livable” (two-phase, contract-minimalism, static data discipline, reviews/gates).
* **Adoption:** Show Tier 3 as the *culture kit* (champions, metrics, knowledge base).
* **Closing:** Promise Tier 4 (return-to-velocity) with **objective triggers** and a visible roadmap.

---

## Diagram Plan (what to storyboard for slides)

* **Tier 1:** Swimlane of survival loop; daily train timeline; icon guardrails; role swimlanes.
* **Tier 2:** Two-phase timeline; contract-minimalism decision tree; static-data flow; PR gate strip with icons; compact troubleshooting table.
* **Tier 3:** Pod map with champion badges; mini KPI dashboard; docs site map.
* **Tier 4:** Maturity staircase with triggers; before/after of automation; CDC flow; deprecation calendar.

---

## First-Week Execution (to lock Tier 1 in place)

* Publish the **survival loop runbook** and **PR template**.
* Schedule and announce the **daily 15:30 refresh ritual**.
* Turn on CI gates for **forbidden-drop/`SELECT *`**.
* Appoint **one champion per pod**; hold a 20-minute champions sync.
* Create a **1-slide KPI board**; share in standup every other day.

---

If you want, I can turn this blueprint into a **presentation outline** with slide titles, bolded lead-ins, and diagram wireframes for each tier, or produce a **one-page wall poster** version of Tier 1 for every pod’s workspace.


Perfect — let’s build out the **micro-artifacts** of Tier 1 so you have tangible, hand-off-ready materials to put in front of pods on Day T+1. These are the lightweight but high-impact scaffolds that make the new process concrete.

---

# Tier 1 Micro-Artifacts

## 1. **PR Template**

Purpose: enforce risk awareness, checklist discipline, and predictable review quality.
Format: Markdown text that can be dropped into Azure DevOps PR settings.

```markdown
## Schema Change Summary
- [ ] **Additive** (new table/column/index)
- [ ] **Refactor** (rename, split, merge, datatype change)
- [ ] **Static Data** (new seed data or update)
- [ ] **Other**: ________________________

## Risk Assessment
- [ ] **Two-phase** required and implemented (yes/no, details)
- [ ] Impacted **views/contracts** updated
- [ ] **Downstream entities** reviewed (list modules affected)
- [ ] **Data migration** plan included (if applicable)

## Validation
- [ ] Build compiles locally
- [ ] Tested against **local/QA DB**
- [ ] Verified **no BlockOnDataLoss violations**
- [ ] Post-deploy script included (if static data or backfill)

## Reviewer Notes
_(Any context, known caveats, rollback considerations)_
```

---

## 2. **Survival Loop Checklist (Laminated Card)**

Purpose: one-page, posted at each desk/Slack pin.
Format: numbered list with verbs bolded.

**The Daily Survival Loop**

1. **Pull latest** SSDT main branch.
2. **Branch off** `feature/[pod]-[ticket]`.
3. **Implement change** in SSDT project (tables, views, static data).
4. **Build locally**; fix all compile errors.
5. **Run tests** (basic deploy to dev sandbox).
6. **Open PR** with template completed.
7. **Get review** by senior/lead.
8. **Wait for 14:00 cutoff**; pipeline deploys at 15:00.
9. **Refresh External Entities** in Integration Studio at 15:30.
10. **Republish modules** dependent on changed entities.

**Golden Rule:** *If you break the loop, you block your pod.*

---

## 3. **Guardrails “Blocker List”**

Purpose: clarity on what will stop a PR/deploy cold.
Format: big, bold rules with rationale.

* 🚫 **No `SELECT *` in views.** (Breaks contracts unpredictably.)
* 🚫 **No direct prod edits.** (Causes SSDT drift.)
* 🚫 **No in-place renames/drops.** (Use two-phase refactor.)
* ✅ **Always BlockOnDataLoss ON.** (Prevent silent truncations.)
* ✅ **Always Refresh External Entities after deploy.** (Prevent module mismatches.)

---

## 4. **Role Charter One-Pager**

Purpose: every IC knows what’s in their lane, and when to escalate.
Format: table with columns: Role | Core Duties | “Don’t Do” | Escalate When.

| Role      | Core Duties                                     | Don’t Do                         | Escalate When                     |
| --------- | ----------------------------------------------- | -------------------------------- | --------------------------------- |
| Junior IC | Additive changes, follow checklist, raise flags | Attempt refactors alone          | Stuck >15 min, schema conflicts   |
| Senior IC | Refactors, reviews, two-phase migrations        | Skip checklists, solo deploys    | Major model change, CI gate fails |
| Dev Lead  | Enforce cadence, guard standards, arbitrate     | Micromanage PRs, bypass pipeline | Broken cadence, major outage      |
| QA/Ops    | Post-deploy checks, drift monitoring, rollback  | Approve schema PRs               | Drift detected, prod anomaly      |

---

## 5. **Release Train Visual**

Purpose: embed the daily rhythm as ritual.
Format: horizontal train track timeline graphic.

* **14:00 → PR Cutoff** (final commits)
* **15:00 → Automated Deploy** (pipeline run)
* **15:30 → Team Refresh** (Integration Studio + republish)

Visual cue: a small train icon moving left to right, with “stations” labeled.

---

# Tier 1 Micro-Metrics (Quick Dashboard)

* **Drift Incidents**: # per week (target: ↓ to zero).
* **PR Compliance**: % PRs with full template (target: 100%).
* **Gate Failures**: # blocked by linter/CI (target: ↓ steadily).
* **Refresh SLA**: % modules refreshed by 16:00 daily (target: 95%+).
* **Blockage MTTR**: mean time to resolve developer stuck (target: <1h).

---

📌 With these five artifacts + five metrics, Tier 1 is **operationalized**: nothing is left at the level of abstraction, and every pod can point to a concrete “what to do” in the moment.

---

Would you like me to now **draft the exact laminated survival checklist and guardrails poster** as if they were to be printed and pinned at each pod’s workspace (short, bold, highly visual)?
