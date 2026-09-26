Awesome—let’s turn this into a **full DX blueprint** you can ship to the team. I’ll keep it concrete, opinionated, and implementable with your constraints:

* one **shared Dev** SQL Server (no per-dev DBs, no local SQL required),
* **dev leads approve every schema change**,
* **manual** Integration Studio refresh/publish after each merge train,
* views/contracts are **Phase-2** (optional later),
* devs contribute only **Additive/Safe** changes; **Breaking** changes are lead-only.

Below is the “playbook of playbooks.” Each section has: what/why, step-by-step, and drop-in deliverables.

---

# 1) Tools & setup (SSDT, VS Code, Azure Data Studio)

**Why:** everyone shares the same mental model—*project as desired state → build → dacpac → diff/publish*—regardless of editor.

## Tooling you’ll use

* **Visual Studio (SSDT)** — lead-only for **Refactor → Rename**, Schema Compare, designers; produces `.refactorlog`.
* **VS Code** + extensions

  * *SQL Database Projects* (opens/builds `.sqlproj`, produces `.dacpac`)
  * *SQL Server (mssql)* (connections; Schema Compare UI)
* **Azure Data Studio (ADS)** + extensions

  * *SQL Database Projects* (project editing/build)
  * *Schema Compare* (visual diffs; can apply changes to project)
* **SqlPackage** CLI — build/diff/script/publish in CI/CD.
* **Azure DevOps Pipelines** (or GitHub Actions) — to build, generate `Diff.sql`/`DeployReport.xml`, and publish to Dev.
* **Integration Studio** (OutSystems) — manual *Refresh Entity / Import* + *Publish*.
* **Service Studio** (OutSystems) — *Refresh Dependencies* + republish consumer modules.

## Deliverables (ship these this week)

* **Install guides (1-pagers)** for VS Code / ADS / SSDT (extensions, where to click “Build Project”).
* **Team cheat-sheet**: common commands (`sqlpackage` actions, build output path).

---

# 2) Repository structure & conventions

**Why:** predictable diffs, fewer merge conflicts, easier review.

```
/db
  Project.sqlproj
  /schema
    /Customer
      /Tables/*.sql
      /Views/*.sql
      /Procs/*.sql
    /Billing
      /Tables/*.sql
      ...
  /RefactorLogs/*.refactorlog          # VS writes these; commits are mandatory for renames
  /PreDeploy/PreDeploy.sql             # optional (env-safe)
  /PostDeploy/PostDeploy.sql           # seeds/lookups (idempotent MERGE)
  /profiles
     Dev.publish.xml
     Test.publish.xml
     UAT.publish.xml
     Prod.publish.xml
/.pipelines/azure-pipelines-db.yml
/.github/pull_request_template.md (or DevOps equivalent)
CODEOWNERS
```

**Naming**

* Schemas kebab-free; snake_case or PascalCase consistently (`dbo.CustomerOrder`).
* PK: `Id` (INT/BIGINT/GUID consistent by domain).
* Audit columns: `CreatedUtc DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME()`, `UpdatedUtc DATETIME2(3) NULL`.
* Strings default to `NVARCHAR` (OutSystems is Unicode).
* Index naming: `IX_<Table>_<ColList>`; unique as `UX_…`.

---

# 3) Authoring changes (devs vs leads)

## Devs — **Additive/Safe** only (no SQL typing required)

Typical tasks and how to do them:

### A) Widen a column

* Open project in **Azure Data Studio** (ADS) or VS Code.
* In ADS, *Database Projects* view → table file → (if a designer is available in your build, use it; otherwise edit the `CREATE TABLE` script directly): change `NVARCHAR(300)` → `NVARCHAR(500)`.
* Save → **Build** project → proceed to PR (see §5).
  *(If no designer is available in your ADS build, edit the column definition textually; Schema Compare will still validate the change visually.)*

### B) Add a **nullable** column

* Same flow: add `, NewCol NVARCHAR(100) NULL` to the table definition.
* Save → Build → PR.

### C) Add **NOT NULL** column *with DEFAULT*

* Add `NewCol INT NOT NULL CONSTRAINT DF_Table_NewCol DEFAULT (0)`.
* Save → Build → PR. *(CI will block NOT NULL without a DEFAULT.)*

### D) Create a new table/index

* Add “Table” or “Script” item in the project → write the `CREATE TABLE` with PK.
* Optional: add indexes in a separate `.sql` file under `schema/<domain>/Tables/Index_*.sql` or directly in the table script.
* Save → Build → PR.

> Devs **do not**: rename/drop/narrow/not-null-without-default/PK-FK redefs.

## Leads — **Breaking** & refactors

* Open project in **Visual Studio (SSDT)**.
* Use **Refactor → Rename** (table/column). VS writes a **`.refactorlog`** entry; this is required so deployment does a real rename instead of drop/recreate.
* For dropping columns, type narrowings, PK changes, constraint tightenings: create a design note, schedule into a merge train, and expect consumer fixes post-refresh.

---

# 4) Branching, PRs, and commit discipline

**Branch:** `feature/db/<ticket>-short-desc`
**Commit message:** `[DB][<Domain>] <short change> (#ticket)`
**PR title:** `[DB][<Domain>][Green] Widen Customer.Comment 300→500` or `[DB][<Domain>][Breaking] Rename Foo→Bar`

**PR template (drop-in)**

```
### Intent
What & why in 1–3 sentences. Link ticket.

### Change Class
- [x] Green (Additive/Safe)
- [ ] Breaking (Lead-owned refactor)

### Safety
- Drops?  [ ] No
- Type narrow?  [ ] No
- NotNull w/o DEFAULT?  [ ] No
- Rename with RefactorLog?  [ ] N/A  [ ] Yes (path: /db/RefactorLogs/…)

### Impact
- Tables/entities touched:
- Likely consumer modules (if known):
```

**CODEOWNERS**

```
/db/**  @dev-leads @dba
```

---

# 5) CI on PR (build, diff, safety gates) — **no DB writes**

**Goal:** a lead can approve straight from the PR page.

**Pipeline steps (PR builds):**

1. **Build** project → get `Project.dacpac`.
2. **Baseline** for diff:

   * Option A (preferred): read-only connection to Dev for *fresh* schema compare.
   * Option B: `dev-baseline.dacpac` exported after each merge train.
3. Generate **DeployReport.xml** and **Diff.sql** (the exact script that would run with safe options).
4. **Policy check**: parse the report; fail PR if forbidden ops present.
5. Attach artifacts to the PR.

**Forbidden ops (fail PR unless labeled `Breaking` and owned by a lead):**

* `DROP` (table/column)
* `ALTER COLUMN` to a **narrower** type/length
* `ALTER COLUMN` **NULL→NOT NULL** without DEFAULT
* PK/FK redefinitions
* Rename **without** `.refactorlog`

> *Tip:* also fail if the diff shows a drop+create on the same object name where a rename was expected.

**Script skeleton to generate artifacts (PowerShell)**

```powershell
# Build already done -> dacpac path is $(Build.SourcesDirectory)\db\bin\Release\Project.dacpac

# DeployReport (XML)
& sqlpackage /Action:DeployReport `
  /SourceFile:"db\bin\Release\Project.dacpac" `
  /TargetConnectionString:"$(DEV_READONLY_CS)" `
  /OutputPath:"$(Build.ArtifactStagingDirectory)\DeployReport.xml"

# Script (T-SQL)
& sqlpackage /Action:Script `
  /SourceFile:"db\bin\Release\Project.dacpac" `
  /TargetConnectionString:"$(DEV_READONLY_CS)" `
  /p:BlockOnPossibleDataLoss=true `
  /p:DropObjectsNotInSource=false `
  /OutputPath:"$(Build.ArtifactStagingDirectory)\Diff.sql"
```

**Policy check (pseudo)**

```powershell
[xml]$r = Get-Content "$(Build.ArtifactStagingDirectory)\DeployReport.xml"
$ops = $r.DeploymentContributorInputs.InputXml.Operations.Operation
$bad = $ops | Where-Object {
  $_.Type -in @('DropObject','AlterColumn','AlterTableConstraint','AlterKey') -or
  # Narrow checks (example heuristic)
  ($_.Type -eq 'AlterColumn' -and $_.AlterOptions -match 'decrease')
}
if ($bad.Count -gt 0) { Write-Error "Forbidden operations detected"; exit 1 }
```

---

# 6) Merge trains & Dev publish (serialized)

**Why:** predictability in a shared Dev.

* **Windows** (example): 10:00 / 13:00 / 16:00 PT.
* **Queue**: one deployment at a time; later merges wait for the next window.
* After each window’s deploy, the **lead performs Integration Studio refresh/publish** (see §7), then announces “entities touched.”

**Dev publish profile (`/db/profiles/Dev.publish.xml`)**

```xml
<Project>
  <PropertyGroup>
    <TargetDatabaseName>YourApp_Dev</TargetDatabaseName>
    <BlockOnPossibleDataLoss>True</BlockOnPossibleDataLoss>
    <DropObjectsNotInSource>False</DropObjectsNotInSource>
    <CommandTimeout>120</CommandTimeout>
    <IgnoreUserLoginMappings>True</IgnoreUserLoginMappings>
    <DoNotDropObjectTypes>Role;User;Permission</DoNotDropObjectTypes>
  </PropertyGroup>
</Project>
```

**Test/UAT/Prod profiles**

* Usually the same as Dev; consider `DropObjectsNotInSource=True` in non-Prod once stable; keep `BlockOnPossibleDataLoss=True` everywhere.

**Publish step (post-merge on `main`)**

```powershell
& sqlpackage /Action:Publish `
  /SourceFile:"db\bin\Release\Project.dacpac" `
  /Profile:"db\profiles\Dev.publish.xml"
```

---

# 7) Integration Studio refresh/publish (lead runbook)

**Why:** OutSystems only “sees” schema changes after the extension is refreshed & published.

**Per merge train window**

1. Open **Integration Studio** → open the external DB **extension**.
2. **If new tables**: *Right-click project → Import Entities from Database…* → select tables → Finish.
3. **If changed tables**: *Right-click entity (or Entities node) → Refresh Entity* → review changes.

   * For **renames**, use the “Entities Change Management” mapping (old→new) when prompted.
4. **1-Click Publish** the extension.
5. Post in team channel: **extension name**, **entities touched**, **notes** (e.g., “`Customer.Comment` widened to 500; `Order.NewCol` added (nullable)”).
6. **Consumer owners**: in **Service Studio**, open affected modules → *Refresh Dependencies* → fix any compile breaks → **1-Click Publish**.

**Cadence & separation**

* **Cadence:** Immediately after each merge train deploy; SLA ≤ 30 min.
* **How many extensions?** Start with **domain-level** extensions (Customer, Orders, Billing…). Don’t mirror every consumer module; that creates needless ripple. Revisit granularity quarterly.

---

# 8) Consumer modules (developers) — refresh & fix

**Why:** changes propagate only when consumers refresh dependencies.

**Steps**

1. Open module → *Manage Dependencies* → **Refresh** for the changed extension.
2. Fix compile errors (typical for Breaking changes):

   * Missing attribute (dropped/renamed) → update Aggregates/Assignments.
   * New NOT NULL column → update Create/Update actions to supply defaults.
3. **1-Click Publish** the module; functional smoke test key flows.

**Tip:** keep a simple owner map: *Extension/Table → Consumer Modules → Owners*. Post it with each train announcement.

---

# 9) Reducing schema churn (Phase-1 without views)

**Why:** fewer refresh cycles, fewer compile ripples.

* Bias to **widening** and **nullable additions**.
* Schedule **Breaking** changes in bundled batches per domain (once every N trains).
* For **NOT NULL** columns: add with DEFAULT first; let consumers adopt it in the same or next train.
* For **renames**: always do a true rename (RefactorLog) and allow two trains before removing any legacy column references in code.
* Keep **data types** consistent across domains (e.g., all monetary values `DECIMAL(19,4)`).

**Phase-2 (when you have headroom):** introduce **compatibility views** for the hottest domains to shield refactors.

---

# 10) Static Entities — go-forward policy

**Decision tree**

* If values are **compile-time constants** and **module-local** → keep **OS Static Entity**.
* If values are **shared**, **updated**, or need **admin UI** → model as **DB lookup table** and expose as External Entity.

**Seeding standard** (`PostDeploy.sql`)

* Use idempotent `MERGE` seeds with deterministic IDs.
* Use SQLCMD variables for env-specific toggles (e.g., seed only in Dev/Test).

**Example**

```sql
MERGE ref.Status AS T
USING (VALUES
  (1, N'Pending'),
  (2, N'Active'),
  (3, N'Suspended')
) AS S(Id, Name)
ON (T.Id = S.Id)
WHEN MATCHED AND T.Name <> S.Name THEN UPDATE SET Name = S.Name
WHEN NOT MATCHED BY TARGET THEN INSERT (Id, Name) VALUES (S.Id, S.Name);
```

---

# 11) “Model first in OutSystems, then PR” (without dual entities)

**Goal:** unblock UI/API work **without** maintaining a second entity model.

**Pattern**

* Create **Structures** (not Entities) in an *Integration/Domain* module representing the target shape.
* Build screens/logic against **Service Actions** that return/use those Structures (temporary stub).
* Once the DB change merges → Dev publish → IS refresh completes, **swap** Service Action internals to use the real External Entity; delete temporary scaffolding in the next train.

**Notes**

* If you need sample data to render screens, seed a **Dev-only** row in the real table, or mock inside the Service Action. Remove when real data arrives.

---

# 12) Security & permissions

* **Developers:** no DDL on Dev; read-only connection (for Schema Compare if needed).
* **Pipeline service principal:** minimal DDL rights on Dev.
* **Leads:** Integration Studio publish rights on Dev; permission to create/modify DB Connection config if needed.
* **Prod:** no ad-hoc DDL; deploy through release only.

---

# 13) Backup, rollback, and hotfixes

**Default posture:** **roll-forward** (fast follow-up PR) rather than rollback.

**Before risky (Breaking) trains**

* Take a lightweight **database snapshot** or full backup (if available).
* Prepare a minimal **backout plan** (e.g., re-add column as nullable) if consumers fail to converge.

**Emergency hotfix path**

* Leads push a forward-fix PR (e.g., temporarily make a new column nullable) → priority merge → Dev publish → IS refresh.
* As a last resort: revert the extension publish (restore previous version) and/or re-add dropped artifacts with minimal definitions.

---

# 14) Performance & correctness

* Index policy: create supporting indexes for typical OutSystems Aggregates/JOINs; monitor missing index DMVs in Dev/Test.
* Choose sensible data types (no `NVARCHAR(MAX)` unless truly needed).
* Consider **tSQLt** for critical stored procedures (optional).
* Watch **query store** / execution plans for regressions after Breaking trains.

---

# 15) Observability & SLOs

**You care about**

* Time from **merge → Dev publish → IS publish**
* Time from **IS publish → all consumer modules compile-green**
* Count of **Breaking** vs **Green** changes per week
* Number of **post-train compile errors** (aim to trend down)

**SLOs (starting targets)**

* Green PR median **≤ 1 business day** to merge.
* IS refresh/publish **≤ 30 min** after Dev deploy.
* Consumers compile-green **≤ 2 hours** post IS publish.
* Breaking change share **≤ 15%**.

---

# 16) RACI (roles you’ll actually use)

| Step                   | Dev | Lead | CI/CD | Consumer Owner |
| ---------------------- | --- | ---- | ----- | -------------- |
| Author safe change     | R   | C    |       |                |
| Open PR                | R   | C    |       |                |
| Generate diff/report   |     |      | A     |                |
| Approve                |     | A    |       |                |
| Merge (train)          |     | A    |       |                |
| Publish to Dev         |     |      | A     |                |
| IS Refresh & Publish   |     | A    |       |                |
| Refresh Dependencies   |     | C    |       | A              |
| Fix & republish module |     | C    |       | A              |
| Promote apps upward    | C   | A    | A     | C              |

---

# 17) Training & enablement (what to run next week)

**Session 1 (Dev & Lead, 60–75 min)**

* Project basics → build dacpac → read DeployReport/Diff.sql.
* Lab: widen `Customer.Comment` 300→500 → PR.

**Session 2 (Dev, 60 min)**

* Schema Compare GUI → sanity check diffs → generate script (no DB writes).
* Lab: add `Order.Notes NVARCHAR(200) NULL` → PR.

**Session 3 (Lead, 60–75 min)**

* **Refactor → Rename** (creates `.refactorlog`) → verify deploy plan shows rename (not drop).
* Lab: rename `OrderItems.Qty` → `Quantity`.

**Session 4 (Lead, 45 min)**

* Merge train dry-run → Dev publish → Integration Studio **Refresh/Publish** → announce entities touched.
* Devs: **Refresh Dependencies** + republish.

---

# 18) Templates (ready to paste)

**A) CODEOWNERS**

```
/db/**  @dev-leads @dba
```

**B) PR template** → see §4.

**C) Dev publish profile** → see §6.

**D) Azure DevOps YAML (minimum viable)**

```yaml
trigger: none
pr:
  branches: [ main, feature/* ]

pool: { vmImage: 'windows-latest' }

stages:
- stage: PR_Checks
  condition: eq(variables['Build.Reason'], 'PullRequest')
  jobs:
  - job: BuildAndDiff
    steps:
    - task: VSBuild@1
      inputs:
        solution: 'db/Project.sqlproj'
        msbuildArgs: '/p:Configuration=Release'
    - powershell: |
        sqlpackage /Action:DeployReport /SourceFile:db\bin\Release\Project.dacpac /TargetConnectionString:"$(DEV_READONLY_CS)" /OutputPath:$(Build.ArtifactStagingDirectory)\DeployReport.xml
        sqlpackage /Action:Script       /SourceFile:db\bin\Release\Project.dacpac /TargetConnectionString:"$(DEV_READONLY_CS)" /p:BlockOnPossibleDataLoss=true /p:DropObjectsNotInSource=false /OutputPath:$(Build.ArtifactStagingDirectory)\Diff.sql
        # TODO: parse DeployReport.xml and fail on forbidden ops
      displayName: 'Build & Generate Diff'
    - task: PublishBuildArtifacts@1
      inputs:
        PathtoPublish: '$(Build.ArtifactStagingDirectory)'
        ArtifactName: 'pr-artifacts'

- stage: Dev_Deploy
  condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'))
  jobs:
  - deployment: PublishToDev
    environment: Dev
    strategy:
      runOnce:
        deploy:
          steps:
          - task: VSBuild@1
            inputs:
              solution: 'db/Project.sqlproj'
              msbuildArgs: '/p:Configuration=Release'
          - powershell: |
              sqlpackage /Action:Publish /SourceFile:db\bin\Release\Project.dacpac /Profile:db\profiles\Dev.publish.xml
            displayName: 'Publish dacpac to Dev'
```

**E) Seed data pattern** → see §10.

**F) Lead review checklist**

* [ ] PR labeled **Green** or **Breaking**.
* [ ] **Diff.sql** shows only allowed ops for Green.
* [ ] **DeployReport.xml** has **no** `DropObject`/narrowing `AlterColumn`/NOT NULL w/o default.
* [ ] For renames, `.refactorlog` present and deploy plan shows rename (not drop).
* [ ] Impact noted; schedule when needed.

**G) Train announcement template**

```
Train 13:00 PT – Dev published

Extension: External.Customer
Entities changed:
- Customer.Comment NVARCHAR 300→500
- Order: added Notes NVARCHAR(200) NULL

Action for consumers:
- Refresh Dependencies (External.Customer)
- Expect zero compile changes; DM #db-stewards if anything breaks
```

---

# 19) FAQ / edge cases

**Q: How do we add a NOT NULL column without breaking writes?**
A: Add with DEFAULT; deploy; leads refresh extension; consumers update to provide a value; optionally drop the DEFAULT later.

**Q: How do we rename safely?**
A: Lead in Visual Studio → **Refactor → Rename** (writes `.refactorlog`) → merge train → IS refresh with rename mapping.

**Q: Can developers do renames if they promise to be careful?**
A: No. Without `.refactorlog` you risk drop/recreate and data loss. Keep renames lead-only.

**Q: Can we automate Integration Studio refresh?**
A: Treat it as **manual** today; include it in the train runbook.

**Q: Should we mirror Service Studio modules with separate adapters?**
A: Start **domain-level** (fewer extensions). Only split when a domain’s change rate causes too much ripple.

---

## TL;DR

* Devs propose **safe** changes in VS Code/ADS → PR produces **Diff.sql + DeployReport**.
* Leads approve every PR, merge in **trains**, publish to Dev, then **Refresh/Publish** the extension in Integration Studio and announce “entities touched.”
* Consumers refresh dependencies and republish.
* Breaking work (renames/drops/constraints) is **lead-only** with **RefactorLog** and scheduled windows.

If you want, I can package this into a **ready-to-share Markdown/Confluence doc** with all templates filled in and a small “starter” repo skeleton so you can run a dry-run merge train with the team.


Awesome — picking up right where we left off and **expanding each section into an actionable, “hand-to-team” playbook**. I’ll keep everything grounded in what we *know* works (no localhost dependency, one shared Dev DB, manual IS refresh by leads, devs limited to safe changes). Where there are options, I’ll call them out and give a default.

---

# 1) Resources & Ramp: SSDT + ADS/VS Code (deep, step-by-step)

## 1.1 Install & tool priming (devs and leads)

**VS Code (developers)**

* Extensions:

  * `ms-mssql.mssql` (connections + Schema Compare UI)
  * `ms-mssql.sql-database-projects-vscode` (open/build `.sqlproj`, produce `.dacpac`)
* Verify:

  * **Command Palette → “SQL Database Projects: Build”** appears.
  * **Command Palette → “Schema Compare: New Comparison”** appears.

**Azure Data Studio (developers)**

* Extensions: **SQL Database Projects** (and **Schema Compare** if not bundled).
* Verify:

  * Open Folder → select repo containing `.sqlproj` → **Database Projects** view appears.
  * Right-click a table within the project → **Open in designer** is present.

**Visual Studio (leads)**

* Workload: **Data storage and processing**.
* Verify:

  * Create/Open **SQL Server Database Project**.
  * **Refactor → Rename** available on schema objects.

**SqlPackage (CI/lead workstations)**

* Ensure `sqlpackage` is on PATH or referenced by full path in pipelines.

---

## 1.2 “What is a SQL Database Project?” (shared mental model)

* A **state-based** source of truth: repo contains *final desired shape* (`CREATE TABLE …` files, `CREATE VIEW …`, etc.).
* Build → **`.dacpac`** (compiled model).
* Deploy uses DacFx to **diff**: *source model* (dacpac) vs *target* (DB or baseline dacpac) → produces the *exact* T-SQL script to reconcile.

**Key file types**

* `.sqlproj` — project definition (includes folders, references, target platform).
* `/schema/**.sql` — object definitions.
* `/RefactorLogs/*.refactorlog` — rename mappings (critical for true renames).
* `/PreDeploy.sql`, `/PostDeploy.sql` — optional scripts (e.g., seeds).
* `/profiles/*.publish.xml` — per-env deployment options (guardrails).

---

## 1.3 Typical activities — micro-walkthroughs

### A) Build a dacpac (VS Code or ADS)

1. Open the folder with `.sqlproj`.
2. Right-click project → **Build** (or Command Palette action).
3. Verify: `db/bin/Release/Project.dacpac`.

### B) Widen a column (developer)

1. ADS → **Database Projects** → expand Tables → Right-click table → **Open in designer**.
2. Select column `Comment`, change `nvarchar(300)` → `nvarchar(500)`.
3. Save → Build → push branch → open PR.

### C) Add a nullable column (developer)

1. ADS designer → **+ Column**.
2. Set `Nullable = Yes`.
3. Save → Build → PR.

### D) Add NOT NULL with DEFAULT (developer)

1. ADS designer → **+ Column**, `Nullable = No`.
2. Set **DEFAULT** expression (e.g., `N''` or `GETUTCDATE()` depending on semantics).
3. Save → Build → PR (expect consumer write updates later).

### E) New table (developer)

1. In project → **Add → Table** (if template) or ADS designer **Add Table**.
2. Define PK, columns, defaults.
3. Save → Build → PR.

### F) Rename a column/table (lead)

1. Visual Studio → right-click object → **Refactor → Rename**.
2. Commit generated `.refactorlog`.
3. Build → PR. (CI should then see a rename, not drop/recreate.)

### G) Visual diff before PR (optional)

* VS Code → **Schema Compare: New Comparison**

  * Source: **Project** (current branch)
  * Target: baseline (read-only Dev connection *or* exported `dev-baseline.dacpac`)
  * Click **Compare** → inspect grid → **Generate Script** if you want the preview.

---

# 2) Developer workflow (authoring proposals)

## 2.1 Policy (who can do what)

* **Developers**: *Additive/Safe* only — new objects, nullable adds, widenings, NOT NULL with DEFAULT, new indexes.
* **Leads**: all *Breaking* — renames/moves/drops/type-narrow/constraint-tighten/data moves.

## 2.2 Branching & PR format

* Branch: `feature/db/<ticket>-<short-desc>`
* Commit message: `[DB][<Domain>] <concise change>` e.g., `[DB][Customer] Enlarge Comment to 500`
* PR template includes:

  * Change class: **Additive/Safe** or **Breaking**
  * Intent: *“Increase Comment length for partner feedback”*
  * Impact: *“No write shape changes”* / *“Writes now require Foo”*
  * Affected entities/tables: list
  * Requires IS refresh? **Yes** (always for entity shape changes)
  * Consumer modules notified: list

## 2.3 The “Golden Path” (for 90% cases)

1. Open ADS → designer → make the safe change → Save.
2. Build → open PR.
3. CI posts **Diff.sql** + **DeployReport.xml**.
4. Await merge train; after Dev publish, **lead** does IS refresh/publish.
5. In Service Studio: **Refresh Dependencies** + quick smoke test.

---

# 3) Lead gates & review process

## 3.1 Guardrails (CI enforced)

* **Allow** (fast path):

  * `CREATE TABLE/INDEX/VIEW`
  * `ALTER TABLE ADD` (Nullable=Yes)
  * `ALTER COLUMN` widen type/length/precision/scale
  * `ADD` NOT NULL with **DEFAULT**
* **Block** (unless PR labeled “Breaking” and owned by a lead):

  * Any `DROP` (table/column)
  * `ALTER COLUMN` to narrower or to NOT NULL without DEFAULT
  * PK/FK redefinitions, constraint tightens
  * Rename **without** `.refactorlog`

> **Tip:** Implement as a simple parser of `DeployReport.xml` (see §8.3).

## 3.2 Review heuristic (how a lead approves in 60 seconds)

1. Open **Diff.sql**:

   * Expect `ALTER TABLE … ADD …` or `ALTER TABLE … ALTER COLUMN` widen; no `DROP`.
2. Scan **DeployReport.xml** for forbidden nodes (quick pattern match).
3. If rename is claimed: confirm presence of `.refactorlog` in the PR and that the deploy plan shows **rename** semantics (not drop/create).

## 3.3 Merge trains (and why they help)

* **Fixed windows** (e.g., 10:00 / 13:00 / 16:00 PT) — concentrate attention & reduce thrash.
* **Serialized publish** — one deployment per window, Dev stays coherent.
* Immediately after publish → **IS refresh/publish** (lead).
* Post “entities touched” → consumer owners refresh dependencies and republish.

---

# 4) Merge trains & IS refresh cadence

## 4.1 Train anatomy (checklist)

* **T-15 min:** lead scans pending PRs, confirms safe set for this train.
* **T-0:** merge them; pipeline builds dacpac from `main` and **publishes to Dev** (Dev profile).
* **T+5:** **IS refresh/publish** (lead) for changed extensions:

  * **Refresh Entity** (or **Import Entities** for new table)
  * Review/match renames if prompted
  * **Publish** extension
* **T+15:** Announce “entities touched” and consumer list.
* **T+30:** Spot-check for consumer compile errors; nudge owners.

## 4.2 Communication template (Slack/Teams)

> **DB Train @13:00** — Published to Dev
> **Extension:** `Ext_Customer`
> **Entities:** `Customer` (Comment 300→500), `CustomerNote` (+ IsPinned nullable)
> **Action:** Refresh Dependencies & republish modules: `CS.CustomerPortal`, `CS.CustomerAdmin`
> **Breaking?** No (additive)
> **Lead on-call:** @danny (IS refresh done)

## 4.3 Should we split adapters (extensions)?

* **Default:** **domain-level** (Customer, Policy, Billing…) — minimizes ripple while keeping cohesion.
* **Avoid** 1:1 with consumer modules (too many touch points).
* **Revisit quarterly** if a domain is too “hot”; consider splitting that domain or moving to a view-based contract later.

---

# 5) Reducing schema churn (without views, for now)

## 5.1 Design hygiene

* Prefer **nullable adds** and **length widenings**.
* Introduce new NOT NULL with **DEFAULT**, then update consumers to supply values.
* Use **consistent naming** and types: `Id` `uniqueidentifier` or `int` with identity; `NVARCHAR` for text (matches OS Unicode).
* Include audit columns (`CreatedUtc`, `UpdatedUtc`) with defaults.

## 5.2 Breaking changes playbook

* **Rename** via VS **Refactor → Rename** (creates `.refactorlog`); schedule train.
* **Drop** after 1–2 trains of deprecation announcement.
* **Type narrow** only when strictly required; prefer new column + migrate + swap + drop later.

## 5.3 Batch by domain

* Collect minor tweaks into one PR per domain per day to reduce IS refresh frequency and consumer thrash.

---

# 6) Static Entities — go-forward approach

## 6.1 Decision table

| Use case                                | Keep OS Static | Move to DB table (External) |
| --------------------------------------- | -------------- | --------------------------- |
| Compile-time constants, single app      | ✅              |                             |
| Shared lookup across many apps          |                | ✅                           |
| Values change occasionally (ops-driven) |                | ✅                           |
| Needs admin UI & audit                  |                | ✅                           |

## 6.2 Standard for DB-backed lookups

* **Table** in project (`dbo.Country`, etc.).
* **Seeds** in `PostDeploy.sql` via idempotent `MERGE`.
* Consumers use External Entities; add simple admin pages if needed.

## 6.3 Migration from existing OS Static

1. Create DB table with same keys.
2. One-time backfill (script or integration).
3. Switch consumer modules to External Entity; retire OS Static after one or two trains.

---

# 7) Modeling first in OutSystems, then PR (without dual entities)

## 7.1 Pattern: structure-first, service action façade

* In an **Integration/Domain module**, define **Structures** mirroring intended shapes.
* Build screens/logic against **Service Actions** returning those structures (backed by stubs during design).
* After DB PR merges & IS refresh publishes the External Entity, wire the Service Actions to the real entity and remove stubs.

**Benefits:** Unblocks UI/business iteration while avoiding “two entity models” in OS.

## 7.2 Dev-only stubs (if needed)

* For list screens, return **one stub row** so UX can be built; remove once data lands.
* No per-dev DBs needed; this is purely application-layer scaffolding.

---

# 8) CI/CD: artifacts, policies, sample code

## 8.1 Repo layout

```
/db
  Project.sqlproj
  /schema/<Domain>/Tables/*.sql
  /schema/<Domain>/Views/*.sql
  /RefactorLogs/*.refactorlog
  /PreDeploy/PreDeploy.sql
  /PostDeploy/PostDeploy.sql
  /profiles
    Dev.publish.xml
    Test.publish.xml
    UAT.publish.xml
    Prod.publish.xml
/pipelines/azure-pipelines-db.yml
/.github/PULL_REQUEST_TEMPLATE.md (or DevOps PR template)
/CODEOWNERS
```

## 8.2 Dev publish profile (safe defaults)

```xml
<Project>
  <PropertyGroup>
    <TargetDatabaseName>YourApp_Dev</TargetDatabaseName>
    <BlockOnPossibleDataLoss>True</BlockOnPossibleDataLoss>
    <DropObjectsNotInSource>False</DropObjectsNotInSource>
    <CommandTimeout>120</CommandTimeout>
    <IgnoreUserLoginMappings>True</IgnoreUserLoginMappings>
    <DoNotDropObjectTypes>Role;User;Permission</DoNotDropObjectTypes>
  </PropertyGroup>
</Project>
```

## 8.3 PR pipeline (build + diff + safety gate)

**Pseudocode for safety check (PowerShell)**

```powershell
[xml]$report = Get-Content "$(Build.ArtifactStagingDirectory)\DeployReport.xml"

$errors = @()

# Block drops (tables/columns)
$errors += $report.DeploymentReport.Operations.Operation |
  Where-Object { $_.Name -eq 'Drop' -and ($_.ObjectType -match 'Table|Column') } |
  ForEach-Object { "Forbidden DROP: $($_.ObjectType) $($_.Value)" }

# Block narrowing / NOT NULL without default
$errors += $report.DeploymentReport.Operations.Operation |
  Where-Object { $_.Name -eq 'Alter' -and $_.Details -match 'NOT NULL|decrease|narrow' } |
  ForEach-Object { "Risky ALTER: $($_.Details)" }

# Block PK/FK redefine (simple heuristic)
$errors += $report.DeploymentReport.Operations.Operation |
  Where-Object { $_.ObjectType -match 'PrimaryKey|ForeignKey' -and $_.Name -eq 'Alter' } |
  ForEach-Object { "Key/Constraint change: $($_.Value)" }

if ($errors.Count -gt 0 -and -not $env:ALLOW_BREAKING) {
  Write-Host "##vso[task.logissue type=error]$(($errors -join "`n"))"
  exit 1
}
```

**Minimal Azure DevOps YAML (essence)**

```yaml
pr:
  branches: [ main, feature/* ]

pool: { vmImage: 'windows-latest' }

stages:
- stage: PR_Checks
  condition: eq(variables['Build.Reason'], 'PullRequest')
  jobs:
  - job: BuildAndDiff
    steps:
    - task: VSBuild@1
      inputs:
        solution: 'db/Project.sqlproj'
        msbuildArgs: '/p:Configuration=Release'
    - powershell: |
        sqlpackage /Action:DeployReport /SourceFile:db\bin\Release\Project.dacpac `
                  /TargetConnectionString:"$(DEV_READONLY_CS)" `
                  /OutputPath:$(Build.ArtifactStagingDirectory)\DeployReport.xml
        sqlpackage /Action:Script /SourceFile:db\bin\Release\Project.dacpac `
                  /TargetConnectionString:"$(DEV_READONLY_CS)" `
                  /p:BlockOnPossibleDataLoss=true /p:DropObjectsNotInSource=false `
                  /OutputPath:$(Build.ArtifactStagingDirectory)\Diff.sql
      displayName: 'Generate DeployReport & Diff'
    - powershell: |
        # call safety check from above
      displayName: 'Safety Gate'
    - task: PublishBuildArtifacts@1
      inputs:
        PathtoPublish: '$(Build.ArtifactStagingDirectory)'
        ArtifactName: 'pr-artifacts'

- stage: Dev_Deploy
  condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'))
  jobs:
  - deployment: PublishToDev
    environment: Dev
    strategy:
      runOnce:
        deploy:
          steps:
          - task: VSBuild@1
            inputs:
              solution: 'db/Project.sqlproj'
              msbuildArgs: '/p:Configuration=Release'
          - powershell: |
              sqlpackage /Action:Publish `
                        /SourceFile:db\bin\Release\Project.dacpac `
                        /Profile:db\profiles\Dev.publish.xml
            displayName: 'Publish dacpac to Dev'
```

> **Concurrency:** Use a *single* deployment job to naturally serialize Dev publishes.

## 8.4 CODEOWNERS (force lead review)

```
/db/** @dev-leads
```

## 8.5 PR template (review friction = low)

```markdown
### Change Class
- [x] Additive/Safe
- [ ] Breaking (lead-owned)

### Intent
Increase Customer.Comment length 300→500 to accommodate partner feedback.

### Entities/Tables
- dbo.Customer (Comment)

### Impact
- Reads: none
- Writes: none

### Safety
- No drops
- No NOT NULL without default
- No renames (N/A)

### IS Step
- Requires Integration Studio refresh/publish (Yes)
- Affected extensions: Ext_Customer
- Consumer modules to ping: CS.CustomerPortal, CS.CustomerAdmin
```

---

# 9) Naming, structure, and modeling conventions

## 9.1 Schemas & folders

* Schemas by **domain**: `Customer`, `Policy`, `Billing` (or `dbo` if you prefer simple).
* Foldering mirrors schemas: `/schema/Customer/Tables/*.sql`.

## 9.2 Keys & columns

* PK: `Id` (`uniqueidentifier` via `DEFAULT NEWSEQUENTIALID()` or `int IDENTITY(1,1)`).
* Audit: `CreatedUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()`, `UpdatedUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()`.
* Text: default to `NVARCHAR` (OutSystems strings are Unicode).

## 9.3 Index naming

* `IX_<Table>_<Col1>_<Col2>`.
* For foreign keys: `FK_<FromTable>_<ToTable>_<Column>`.

## 9.4 Defaults & nullability

* Default **Nullable** for optional fields; NOT NULL **only** when business rules truly require.

---

# 10) Runbooks (copy/paste ready)

## 10.1 Dev widening change (10-minute win)

1. Open **ADS** → project → designer → change length.
2. **Build** → **PR** (Additive/Safe).
3. After merge train: **Refresh Dependencies** in Service Studio; republish.

## 10.2 Lead rename (safe)

1. Visual Studio → **Refactor → Rename** column/table.
2. Commit `.refactorlog`.
3. Merge at scheduled train → pipeline publishes.
4. IS: **Refresh** (map Original→New if prompted) → **Publish**.
5. Announce consumers → teams refresh dependencies & republish.

## 10.3 IS refresh (lead)

1. Open **Integration Studio** → extension → right-click **Entities → Refresh** (or **Import**).
2. Review diffs (ensure renames are mapped if relevant).
3. Click **1-Click Publish**; confirm success.
4. Post “entities touched” to #dev-announcements with consumer owners.

---

# 11) Ops & SLOs (make it observable)

**Weekly metrics**

* Median **PR (Additive)** time-to-merge ≤ 1 day.
* **IS refresh latency** ≤ 30 minutes after Dev deploy.
* **Consumer compile-clean** within 2 hours of IS publish.
* **Breaking change ratio** ≤ 15% of total DB PRs.
* **Rollbacks**: 0 (aim for forward fixes).
* **Hotfix MTTR** ≤ next train (≤ 4 hours in business day).

**Simple dashboards**

* Count of entities changed per train.
* Number of consumer compile failures after each train.
* Time from merge → Dev publish → IS publish → first consumer republish.

---

# 12) Risks & countermeasures (expanded)

1. **Rename causes drop/recreate**

   * Only leads perform renames in Visual Studio; **require `.refactorlog`**.
   * CI blocks rename-without-refactorlog.

2. **Ad-hoc DDL on Dev**

   * Remove write perms for humans; publish only via pipeline.
   * Periodic drift reports; alert on drift.

3. **Constraint tightens break writes**

   * Stage with NOT NULL + DEFAULT; monitor errors post-train; coordinate consumer updates.

4. **IS refresh forgotten**

   * Pipeline posts a checklist ping to “lead on-call” after a Dev publish.
   * Add calendar holds for trains including IS time.

5. **Consumer thrash (too many refreshes)**

   * Batch per domain; 2–3 trains per day only; consistent times.

6. **Merge conflicts in project files**

   * Folder by domain; small PRs; don’t re-format unrelated files.

7. **Seeds diverge**

   * Centralize in `PostDeploy.sql` with idempotent `MERGE`.
   * Use SQLCMD var `ENABLE_SEEDS` to toggle in higher envs if needed.

8. **Performance regressions**

   * Require index plan note for new heavy queries.
   * Capture query plans before/after when touching hot tables.

---

# 13) Concrete delivery plan for *this week*

* **Day 1 (today)**

  * Ship: Install 1-pager (Dev & Lead), PR template, CODEOWNERS.
  * Commit: `/db/profiles/Dev.publish.xml` and sibling profiles.
  * Stand up: PR pipeline that attaches **Diff.sql** + **DeployReport.xml** (safety gate disabled initially if needed).

* **Day 2**

  * Enable safety gate; socialize **Green vs Red** doc.
  * Publish **IS refresh runbook** with screenshots.
  * Run first **merge train dry-run** (no consumers).

* **Day 3–4**

  * Devs do **D1–D3** micro-labs (widen/add), submit PRs.
  * Leads do **L1–L2** (Schema Compare + Refactor→Rename lab).
  * Execute two real trains with low-risk changes.

* **Day 5**

  * Review metrics (latency, compile breaks).
  * Tune train times; confirm steward rotation.
  * Decide whether to keep “optional pre-PR Schema Compare” in VS Code or skip.

---

# 14) Appendices (drop-in assets)

## 14.1 Sample `PostDeploy.sql` (lookup seeds)

```sql
:setvar ENABLE_SEEDS true

IF '$(ENABLE_SEEDS)' = 'true'
BEGIN
  MERGE dbo.Status AS T
  USING (VALUES
      (1, N'New'),
      (2, N'Active'),
      (3, N'Inactive')
  ) AS S(Id, Name)
  ON T.Id = S.Id
  WHEN MATCHED AND T.Name <> S.Name THEN
    UPDATE SET Name = S.Name
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, Name) VALUES (S.Id, S.Name);
END
```

## 14.2 Drift report job (optional scheduled check)

```powershell
sqlpackage /Action:DriftReport `
  /SourceConnectionString:"$(DEV_CS)" `
  /TargetFile:db\bin\Release\Project.dacpac `
  /OutputPath:artifacts\DriftReport.xml
# Parse for unexpected objects; alert if present
```

## 14.3 Consumer owner map (CSV → PR bot)

```
Extension,Entity,ConsumerModule,OwnerSlack
Ext_Customer,Customer,CS.CustomerPortal,@alice
Ext_Customer,Customer,CS.CustomerAdmin,@bob
```

* A tiny script posts the owner list in the train announcement.

## 14.4 Reviewer quick-cue card (fits on a sticky)

* **Allowed:** ADD, widen, new objects, NOT NULL+DEFAULT.
* **Red flags:** DROP, narrow, NOT NULL w/o default, PK/FK redefine, rename w/o refactorlog.
* **IS?** If entity shape changed, yes.
* **Post:** which extension & entities, which consumers.

---

## Final note

This gives you a **complete, governed, and teachable DX** without local SQL or per-dev instances, keeps **leads in control**, and lets **developers contribute safely** with point-and-click where it matters (ADS designer + VS Code Schema Compare). If you want, I can package this as a *ready-to-paste* Markdown/Confluence doc set with the code snippets and templates separated into copyable blocks.
