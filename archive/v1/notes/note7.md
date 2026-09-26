Perfect addition. You’re right: before we ask about SSDT-specific workflows, it helps to baseline **general SQL fluency** and **adjacent tool experience** (e.g., EF Migrations, Liquibase, Flyway, OutSystems External Entities, etc.). That way, you can tailor training not just to SSDT skill gaps, but to what people already know by analogy.

Here’s a revised **Microsoft Forms survey structure** with those pieces dialed in up front, while still keeping it essentialized and behavior-focused:

---

# 📋 SSDT & Database Development Readiness Survey

**Purpose**: To understand the team’s baseline SQL and related tooling experience, and their readiness with SSDT database development practices. Please answer honestly; this will guide onboarding and support.

---

## Section 0 — SQL & Tooling Background

**Q1.** How would you describe your general **SQL experience**?

* [ ] Very limited (basic SELECT/INSERT only)
* [ ] Comfortable writing queries, joins, and simple DDL
* [ ] Confident with schema design, indexes, constraints
* [ ] Expert; I’ve managed database lifecycle and performance tuning

**Q2.** Have you worked with any of the following **schema management / migration tools**? (select all that apply)

* [ ] Entity Framework Migrations
* [ ] Flyway
* [ ] Liquibase
* [ ] OutSystems External Entities
* [ ] Other (please specify)
* [ ] None

**Q3.** If you’ve used **OutSystems External Entities**, how comfortable are you with concepts like schema synchronization and managing mappings between model and database?

* [ ] Never used
* [ ] Tried it briefly
* [ ] Comfortable with basics
* [ ] Confident, can explain tradeoffs and workflows

**Comment box:** *(Any other database tools or approaches you’ve used that might be similar to SSDT?)*

---

## Section 1 — Core Workflow

**Q1.** How familiar are you with creating and working inside a SQL Server Database Project in Visual Studio (SSDT)?

* [ ] Never used it
* [ ] Tried it briefly
* [ ] Comfortable creating/editing tables, views, procs
* [ ] Confident and can explain project setup to others

**Q2.** Have you used publish profiles (`.publish.xml`) and sqlcmd variables to target different environments (Dev/QA/Prod)?

* [ ] No experience
* [ ] Limited familiarity
* [ ] Confident in setting these up
* [ ] Expert; I’ve templated or automated them before

**Comment box:** *(Anything else about your experience with the SSDT core workflow?)*

---

## Section 2 — Safe Refactoring

**Q3.** When renaming tables or columns, how familiar are you with using SSDT’s **Refactor → Rename** feature (and committing the RefactorLog)?

* [ ] Not aware of it
* [ ] Aware, but never used
* [ ] Used a few times
* [ ] Confident; I always use the refactor tool correctly

**Q4.** Have you ever applied a **two-phase pattern** (e.g., adding a column as NULL → backfilling → enforcing NOT NULL)?

* [ ] Never
* [ ] Once or twice
* [ ] Comfortable when needed
* [ ] Confident; I can teach the pattern

**Comment box:** *(What challenges have you faced with schema refactoring?)*

---

## Section 3 — CI/CD & Deployment

**Q5.** Have you worked with **dacpacs** (`.dacpac`) and `sqlpackage` to generate scripts for review before deployment?

* [ ] No
* [ ] Once or twice
* [ ] Comfortable running basic commands
* [ ] Confident automating this in CI/CD pipelines

**Q6.** How familiar are you with using **Pre-Deployment** and **Post-Deployment** scripts for backfills, seed data, or phased rollouts?

* [ ] Not at all
* [ ] Limited familiarity
* [ ] Comfortable authoring basic scripts
* [ ] Confident with advanced use cases (idempotent MERGE, batching, etc.)

**Comment box:** *(Any experiences with database deployment pipelines you’d like to share?)*

---

## Section 4 — Quality Gates

**Q7.** Have you used **tSQLt** or any other framework for unit testing database code (views, procs, functions)?

* [ ] No
* [ ] Once or twice
* [ ] Comfortable writing simple tests
* [ ] Confident building a full test suite

**Q8.** How familiar are you with writing **idempotent MERGE scripts** for static/lookup data in Post-Deployment?

* [ ] Not familiar
* [ ] Basic familiarity
* [ ] Comfortable
* [ ] Confident (and can enforce conventions across a team)

**Comment box:** *(How do you currently test and validate database changes?)*

---

## Section 5 — Operator Literacy

**Q9.** How comfortable are you with reviewing generated deployment scripts for potentially destructive changes (drops, NOT NULL, data type changes)?

* [ ] Not at all
* [ ] Somewhat comfortable
* [ ] Comfortable with guidance
* [ ] Confident; I can spot and mitigate risks consistently

**Q10.** Have you worked with **drift detection** (detecting schema changes that diverged from the source project)?

* [ ] No
* [ ] Aware but never used
* [ ] Used occasionally
* [ ] Confident in using drift reports in practice

**Comment box:** *(Any other thoughts on reviewing and operating SSDT deployments?)*

---

## Final

**Q11.** What would help you feel most confident in adopting SSDT practices (select all that apply)?

* [ ] Step-by-step documentation
* [ ] Recorded walkthroughs
* [ ] Hands-on lab sessions
* [ ] Pairing/mentoring
* [ ] Reference examples/templates

**Q12.** Open comments: *(Any feedback, requests, or prior experiences to share?)*

---

Would you like me to also produce a **ready-to-import Microsoft Forms JSON** (so you don’t have to rebuild this manually), or do you prefer the clean outline to copy-paste?
