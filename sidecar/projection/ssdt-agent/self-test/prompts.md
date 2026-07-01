# self-test — prompts (the complete suite)

Human-shaped developer prompts, one per operation across the nine families, phrased the way an
OutSystems-native developer actually asks — in entities, attributes, references, and the
**Mandatory** checkbox, never in SSDT mechanics. Each case carries the expected **Mechanism**
(1–5 with its release bucket), **Tier** (1–4, with any +1 escalation named), the **caseType**,
the **seed** it needs on the **enriched proving ground**, the **expected proving-ground outcome**,
and the **fail mode** — what a naive agent wrongly does.

This is a *classify-by-proving* suite. For almost every case the answer comes from the **data
on the proving ground**, not from the `.sql` text. The same edit on a different seed flips the
mechanism — those are the **flip twins**, and an agent that returns the same verdict for both
halves classified from text and FAILS that pair.

## The enriched proving ground (the seed surface every case maps to)

The four original tables are **intact** — the six verified cases below (ADD `TBL-01`,
`COL-03`/`COL-03B`/`COL-03C`, `COL-06`, `COL-08N`, `CON-02`) still pass against them. Every new
op maps to one of the enriched Modules, each of which the enriched-sample authoring documents as
"which self-test ids it unlocks" in its module header:

| Module (`proving-ground/Modules/*.sql`) | Shape | Unlocks |
|---|---|---|
| `Customer` (original) | IDENTITY PK; Email NULL rows 3,5; ContactPhone populated; new nullable `AccountId` | make-mandatory family, rename-attribute, retype, audit-cols, temporal-convert, split |
| `Order` (original) | IDENTITY PK; orphan `CustomerId=999` row 4; new `StatusText` free-text col | create-FK, change-delete-rule, extract-to-lookup, retype-implicit |
| `Product` (original) | IDENTITY PK; over-length `STANDARD-SKU-001`; `DUPE`/`DUPE`; new nullable `CategoryId`; new populated `LegacyCode` | narrow, add-unique, widen, delete-attribute, index ops |
| `Status` (original) | explicit-id lookup; `UX_Status_Code` | edit-seed, define-PK, static-data |
| `Account` (new) | IDENTITY PK; `Region NULL`; 1:1 with Customer via `Customer.AccountId` | move-attribute (STR-03), merge/split partner |
| `CustomerAddress` (new) | IDENTITY PK; `CustomerId` FK-shaped; **exactly one row per Customer** (proven 1:1) | split-table (STR-01), merge-tables (STR-02); the 1:many negative comes from a **scratch** seed edit, never the authored positive |
| `Category` (new) | **explicit-id** NOT NULL PK, NO IDENTITY; `IsActive DEFAULT 1`; FK target of `Product.CategoryId` | create-static-seed, edit-seed, delete-seed-value (STA-04N), identity-swap SOURCE (STR-04) |
| `ProductLegacy` (new) | adds a **populated** `LegacyCode NVARCHAR(40) NOT NULL` to Product | delete-attribute (COL-09) — the populated-column veto |
| `OrderLine` (new) | IDENTITY PK; `OrderId` FK to Order; `LineNumber`; `Amount`; 2–3 lines/Order | define-PK composite (KEY-01), change-delete-rule cascade blast-radius (KEY-04), FK-graph depth |
| `OrderStatusText` (new) | adds free-text `StatusText NVARCHAR(20) NOT NULL` to Order, values `Pending`/`Shipped`/`Cancelled` mapping to Status | extract-to-lookup (STA-03) + its total-mapping negative |
| `CdcCandidate` (new) | plain `Id/Name/Notes` table, seeded, header carries the survival-rule-1 / PROTOCOL §8 CDC-isolation warning | enable-cdc (AUD-04), recreate-capture-instance (AUD-05), change-tracking (AUD-06), drop-CDC-table (AUD-07N), nullable-add-to-CDC (TRAP-01N) |
| `OrderSummary` (new) | authored VIEW `dbo.vOrderSummary` with **enumerated** columns joining Order+Customer+Status, plus a documented `SELECT *` variant for the trap | create-view (VIE-01), compat-view target (VIE-02), indexed-view (VIE-04) |

**Seed discipline (mirrors PROTOCOL step 5):** the **authored** positive seed is never edited to
produce a negative/flip — you edit **your scratch copy** (`$SCRATCH/Data/Seed.sql`) for the
flipped leg. Empty-table, zero-NULL re-seed, extra orphan, 1:many address, unmapped status text,
CDC-enable are all **scratch** seed states, so the authored 1:1 / clean / mapped positives keep
passing.

## How to run a case

1. Pick **one** case id below. Read it.
2. Follow `PROTOCOL.md` exactly: copy the proving ground to your own scratch dir, create your
   own unique database (`/TargetDatabaseName:PG_<id>_<rand>`), build the dacpac in your scratch
   copy, establish the BEFORE seed, then run the prove loop. **You never touch shared state.**
3. Drive the prompt through `agents/intake.md` (which fires `skills/confirm-intent`) →
   `agents/change-author.md` (which opens the **per-op skill** `skills/op/<op-slug>/SKILL.md`
   plus the `skills/_index/*` concern skills it points to) → `agents/reviewer.md`, against your
   isolated DB.
4. Score the result with `rubric.md`. Tear down (drop your DB, delete your scratch) on exit.

> Handbook citations use the on-disk filename with the **+3 offset**: file 13 = §16, file 14 =
> §17, file 15 = §18, file 16 = §19 (the anti-pattern catalog).

---

## The legend (every field, every case)

- **id** — globally unique; ids ending `N` are negative/adversarial (PASS = correct refusal).
- **prompt** — what the developer types, in their words (matches the op skill's frontmatter
  `description` trigger phrases — the phrasing is the dispatch surface).
- **op** — the per-op skill the change-author opens: `skills/op/<op-slug>/SKILL.md`.
- **_index** — the governing cross-cutting concern skill(s): `skills/_index/<concern>/SKILL.md`.
  The op skill points here for the shared WHY; scoring criterion 6 draws the reasoning from it.
- **Mechanism** — 1 Pure Declarative (single-phase) · 2 Declarative+Post-Deploy (single-PR) ·
  3 Pre-Deploy+Declarative (single-PR) · 4 Script-Only · 5 Multi-Phase (multi-PR). The release
  bucket (single-phase / single-PR / multi-PR) is part of the verdict.
- **Tier** — 1–4 danger grade; **+1** for CDC / >1M rows / first-time op. Danger is **distinct**
  from release-count: a single-PR drop can be Tier 4.
- **caseType** — positive · flip (a twin whose seed flips the mechanism) · negative (PASS = the
  agent refuses / vetoes / escalates).
- **seed** — the proving-ground state this case needs (edit `$SCRATCH/Data/Seed.sql`).
- **outcome** — the expected proving-ground result: clean publish, specific veto text, or refusal.
- **fail mode** — the wrong move a naive agent makes.

The **op** and **_index** columns are load-bearing: a case only PASSES criterion 6 if the agent
surfaced the WHY from the named `_index` skill, specialized to the op. A per-op skill that
re-explains a lifted concern (the tightening guard, refactorlog, coexistence, CDC tax,
constraint-claim, idempotent seed) instead of pointing to its `_index` owner is itself a defect —
flag it against the op skill, not the run.

---

## Family: tables — `skills/operations/tables.md` (family index)

### TBL-01 — create-entity · positive
> **"I need a new CustomerPreference entity to store per-customer settings."**
- **op:** `skills/op/create-entity/SKILL.md`
- **Mechanism:** 1 — Pure Declarative (single-phase) · **Tier:** 1
- **Seed:** baseline; the new table references nothing existing (or add its parent first).
- **Outcome:** Strict publishes clean; the delta is a single `CREATE TABLE [dbo].[CustomerPreference]`
  with no DROP/ALTER of any sibling. Magic line names "additive, nothing depends on it yet,
  proven clean on a copy."
- **Fail mode:** treats it as risky because it's "new schema" and over-tiers; or fails to confirm
  the project glob picks the new file up (silent never-deploys).

### TBL-02 — rename-entity · flip (refactorlog present)
> **"I renamed the Customer entity to Client in Service Studio — wire it through."**
- **op:** `skills/op/rename-entity/SKILL.md` · **_index:** `skills/_index/identity-and-refactorlog/SKILL.md`
- **Mechanism:** 1 — Pure Declarative (`sp_rename`) **IF** the refactorlog entry is present;
  otherwise a CATASTROPHE graded Script-Only data loss. · **Tier:** 3
- **Seed:** Customer populated (5 rows); the `.refactorlog` rename entry **PRESENT**.
- **Outcome:** the Script delta = `EXEC sp_rename 'dbo.Customer','Client','OBJECT'`; rows preserved.
  Agent confirms the `.refactorlog` changed. Tier 3 because every caller (FKs from Order, views,
  ETL) breaks.
- **Fail mode:** ships without reading the delta; misses that without the refactorlog SSDT emits
  `DROP TABLE` + `CREATE TABLE` and loses all 5 rows.

### TBL-02N — rename-entity, naked · negative
> **"I renamed the Customer entity to Client in Service Studio — wire it through."**
- **op:** `skills/op/rename-entity/SKILL.md` · **_index:** `skills/_index/identity-and-refactorlog/SKILL.md`
- **Mechanism:** REFUSE / STOP — naked rename detected. · **Tier:** 3 (catastrophe-grade)
- **Seed:** Customer populated (5 rows); the refactorlog entry **MISSING** (omit it).
- **Outcome (PASS):** the agent reads the `/Action:Script` delta, SEES `DROP TABLE [dbo].[Customer]`
  + `CREATE TABLE [dbo].[Client]`, STOPS, and demands the refactorlog entry before proceeding.
  Never publishes. The catch is **in the delta**, not after a hypothetical deploy.
- **Fail mode:** publishes the drop+create (Permissive / DropObjectsNotInSource) and destroys all
  5 Customer rows, or reports "rename done" from the `.sql` text without proving the delta.

### TBL-03 — delete-entity · negative
> **"Drop the old AuditLog table, we don't need it anymore."**
- **op:** `skills/op/delete-entity/SKILL.md` · **_index:** `skills/_index/tightening-class/SKILL.md`
  + `skills/_index/cdc/SKILL.md` (the +1 face)
- **Mechanism:** 4 — Script-Only (mechanically single-PR). · **Tier:** 4 (data loss; **+1 if CDC**)
- **Seed:** a populated table to drop (seed AuditLog, or exercise the veto on any populated table
  e.g. Order); optionally CDC-enabled on an isolated DB (use `CdcCandidate` for the CDC leg).
- **Outcome (PASS):** the agent proves the Strict `BlockOnPossibleDataLoss` veto with the row count
  as the SAFETY proof, flags Tier 4 (danger ≠ release-count), drops inbound FKs / disables CDC
  FIRST in the proven order, and does NOT blind-drop. If CDC-enabled, +1 escalation and
  capture-instance handling named (see `_index/cdc`).
- **Fail mode:** runs DropObjectsNotInSource and force-drops a populated, possibly CDC-tracked
  table, orphaning the capture instance and losing data irreversibly.

### TBL-04 — move-schema · positive
> **"Move the Customer entity into the archive schema."**
- **op:** `skills/op/move-schema/SKILL.md` · **_index:** `skills/_index/identity-and-refactorlog/SKILL.md`
- **Mechanism:** 1 — Pure Declarative (refactorlog) OR 4 — Script-Only (`ALTER SCHEMA TRANSFER`).
  · **Tier:** 3
- **Seed:** Customer populated; refactorlog move entry present, OR author
  `ALTER SCHEMA archive TRANSFER dbo.Customer`.
- **Outcome:** delta is `sp_rename` (schema-qualified) or the agent authors `ALTER SCHEMA TRANSFER`
  and proves row counts + `object_id` unchanged. A DROP+CREATE in the delta is the catastrophe
  signal to STOP. Tier 3: every `dbo.` reference must follow.
- **Fail mode:** edits the schema in the CREATE with no refactorlog; SSDT reads it as drop-old +
  create-new and the data is lost — same shape as a naked rename.

### TBL-05 — archive-entity · positive
> **"Archive orders older than two years into an OrderArchive table."**
- **op:** `skills/op/archive-entity/SKILL.md` · **_index:** `skills/_index/multi-phase/SKILL.md`
- **Mechanism:** 5 — Multi-Phase (multi-PR). · **Tier:** 3 (**+1 at >1M rows**)
- **Seed:** Order populated; new OrderArchive destination created declaratively.
- **Outcome:** create archive table (declarative) + batched post-deploy
  `DELETE … OUTPUT DELETED.* INTO archive`. Agent proves source-count + archive-count == original
  total (no rows lost/duplicated — the conservation proof from `_index/multi-phase`) and that each
  batch commits (log bounded).
- **Fail mode:** an unbatched single-statement move that bloats the transaction log; or moves child
  rows before parents and trips FKs; or reports counts it never reconciled.

### TBL-06 — junction (many-to-many) · positive
> **"A Student can have many Courses and a Course many Students — make it many-to-many."**
- **op:** `skills/op/junction/SKILL.md` · **_index:** `skills/_index/constraint-is-a-claim/SKILL.md`
- **Mechanism:** 1 — Pure Declarative (single-phase). · **Tier:** 2
- **Seed:** both parent tables present and seeded (use `Order` + `Product` as parents, or author a
  fresh pair); bridge empty.
- **Outcome:** Strict creates the bridge (composite PK over the two FK columns) clean; NO FK veto
  fires because every seeded pair has both parents (the constraint-is-a-claim orphan probe = 0).
  Tier 2 (two inter-table dependencies). Agent proves zero orphan pairs.
- **Fail mode:** seeds bridge pairs whose parents don't exist (Forgotten FK Check in disguise) and
  the FK validation vetoes; or omits the composite PK and lets duplicate pairs in.

---

## Family: columns — `skills/operations/columns.md` (family index)

### COL-01 — add-optional · positive
> **"Add an optional MiddleName field to Customer, it can be blank."**
- **op:** `skills/op/add-optional/SKILL.md`
- **Mechanism:** 1 — Pure Declarative (single-phase). · **Tier:** 1
- **Seed:** Customer populated (default seed).
- **Outcome:** Strict publishes clean on the populated copy; delta is a single
  `ALTER TABLE … ADD [MiddleName] NVARCHAR NULL`; no veto at any row count. Existing rows get NULL.
- **Fail mode:** invents a default or backfill that isn't needed, or over-tiers an additive
  nullable column.

### COL-02 — add-mandatory · positive
> **"Add a required Status field to Customer — everyone must have one."**
- **op:** `skills/op/add-mandatory/SKILL.md` · **_index:** `skills/_index/tightening-class/SKILL.md`
  (the no-default veto face)
- **Mechanism:** 1 — Pure Declarative WITH explicit default (single-phase); vetoes without a default.
  · **Tier:** 2
- **Seed:** Customer populated.
- **Outcome:** with an explicit `DEFAULT`, Strict publishes clean and the delta shows
  `ADD … NOT NULL CONSTRAINT … DEFAULT`; SQL Server stamps existing rows. The agent ALSO proves the
  no-default Strict veto ("Cannot insert NULL") and runs Permissive with `GenerateSmartDefaults` to
  SHOW what would have been silently stamped. Tier 2 (contractual).
- **Fail mode:** adds NOT NULL with no default (Optimistic NOT NULL) and the deploy fails on the
  populated table; or lets `GenerateSmartDefaults` silently fill empty strings without telling the
  developer.

### COL-03 — make-mandatory, NULLs present · positive (THE SPINE PROOF — corrected finding)
> **"Make the Email field on Customer required."**
- **op:** `skills/op/make-mandatory/SKILL.md` · **_index:** `skills/_index/tightening-class/SKILL.md`
- **Mechanism:** **NOT Mechanism 1 and NOT a clean Mechanism 3.** Backfill alone does NOT clear the
  Strict veto. The honest verdict is a CONSCIOUS, documented decision **after** a verified-zero-NULL
  backfill: **(a)** targeted relaxation of `BlockOnPossibleDataLoss` for this one change —
  operationally Mechanism 4 / Script-Only with a named gate-relaxation — or **(b)** Mechanism 5
  Multi-Phase. · **Tier:** 2 (**+1 if CDC / >1M**)
- **Seed:** Customer DEFAULT seed: rows 3 and 5 have `Email` NULL.
- **Outcome:** the agent edits `Email` to `NOT NULL`, builds, and PROVES on the proving ground:
  1. Strict vetoes — and crucially, the veto is generated as
     `IF EXISTS (SELECT TOP 1 1 FROM dbo.Customer) RAISERROR(…,16,127)` placed **before** the
     `ALTER COLUMN`, i.e. **table-has-rows, not column-has-NULLs** (this is the `_index/tightening-class`
     flagship — the op skill points there, it does not re-derive the guard).
  2. authors a pre-deploy backfill, re-runs the NULL probe to confirm **0** NULL emails, AND proves
     Strict **STILL** vetoes and the column stays nullable.
  3. delivers the corrected verdict: on a populated table, backfill alone cannot pass prod-strict —
     it needs a named gate-relaxation **after** verified zero NULLs, or multi-phase.
  Magic line names the real NULL count (**2**) AND the empirical fact that **0-NULL still vetoed**.
- **Fail mode:** reports the OLD, WRONG recipe ("pre-deploy backfill → clean NOT NULL under Strict =
  Mechanism 3") from any stale skill text WITHOUT proving it — and never discovers the
  backfill-then-still-vetoed reality. **This is the showcase failure the run must catch.**

### COL-03B — make-mandatory, EMPTY table · flip
> **"Make the Email field on Customer required." (empty table)**
- **op:** `skills/op/make-mandatory/SKILL.md` · **_index:** `skills/_index/tightening-class/SKILL.md`
- **Mechanism:** 1 — Pure Declarative (single-phase). · **Tier:** 1
- **Seed:** Customer table EMPTY (skip the seed / truncate before publish, in scratch).
- **Outcome:** Strict publishes clean — no rows means the table-has-rows guard's `IF EXISTS` is
  false, the RAISERROR never fires, and the `ALTER COLUMN NOT NULL` lands. Proves the EMPTY-table
  clean Mechanism 1 leg and that the guard is genuinely table-has-rows (see `_index/tightening-class`).
- **Fail mode:** assumes "make-mandatory always needs a backfill" and over-classifies the empty case.

### COL-03C — make-mandatory, ZERO NULLs but populated · flip (the empirical clincher)
> **"Make Email on Customer required." (re-seeded with zero NULL Email)**
- **op:** `skills/op/make-mandatory/SKILL.md` · **_index:** `skills/_index/tightening-class/SKILL.md`
- **Mechanism:** FLIP TWIN of COL-03: same edit, populated with ZERO NULLs. **STILL** vetoes under
  Strict because the guard is table-has-rows, not column-has-NULLs — the honest verdict is a named
  gate-relaxation (proven 0 NULLs first) or multi-phase, NOT a clean Mechanism 1. · **Tier:** 2
  (+1 if CDC/>1M). Same op and table shape as COL-03 — the NULL count does not change the tier,
  since the veto is table-has-rows.
- **Seed:** Customer re-seeded (scratch) with all 5 Emails populated (zero NULLs).
- **Outcome:** the agent proves: NULL probe returns **0**, yet Strict **STILL** vetoes (table has
  rows). This is the empirical confirmation of the corrected recipe — zero NULLs is **necessary but
  not sufficient** to pass the prod-strict gate on a populated table. The pass is the agent
  surfacing exactly this and choosing a conscious gate-relaxation or multi-phase, with the proof.
- **Fail mode:** claims "zero NULLs → clean Mechanism 1" from any old framing and never runs the
  publish to discover the table-has-rows veto. Classified-from-text failure.

### COL-04 — make-optional · positive
> **"Make MiddleName on Customer optional — uncheck Mandatory."**
- **op:** `skills/op/make-optional/SKILL.md`
- **Mechanism:** 1 — Pure Declarative (single-phase). · **Tier:** 1–2 (2 if consumers assume non-NULL)
- **Seed:** Customer with a NOT NULL column to loosen.
- **Outcome:** Strict publishes clean (loosening never vetoes); delta is a single `ALTER COLUMN … NULL`.
  The agent's real work is flagging the DOWNSTREAM NULL risk (reports/ETL/code that never expected
  NULL), since the publish itself cannot fail.
- **Fail mode:** treats it as risky at the deploy layer; or fails to flag the downstream consumer
  risk that is the actual Tier-2 driver.

### COL-05 — widen · positive
> **"Increase Email on Customer to 256 characters… actually make it longer, 512."**
- **op:** `skills/op/widen/SKILL.md`
- **Mechanism:** 1 — Pure Declarative (single-phase). · **Tier:** 2
- **Seed:** Customer populated; Email not in an index key near the byte limit.
- **Outcome:** Strict publishes clean; delta is `ALTER COLUMN` to the wider type, metadata-only, no
  rebuild of unrelated objects. Agent checks the column isn't in an index key that would blow the
  1700-byte limit (and NVARCHAR doubling) — the index-key byte coupling is stated inline in the
  widen op skill (a single-op concern, not lifted).
- **Fail mode:** misses an index-key byte-limit veto on an indexed column, or assumes widen is
  always free without checking the index coupling.

### COL-06 — narrow, over-length data · flip
> **"Shorten Product.Code to 10 characters."**
- **op:** `skills/op/narrow/SKILL.md` · **_index:** `skills/_index/tightening-class/SKILL.md`
- **Mechanism:** 3 — Pre-Deploy+Declarative (single-PR), or 5 — Multi-Phase if over-length data must
  be preserved. · **Tier:** 3
- **Seed:** Product DEFAULT seed: row 3 `Code = 'STANDARD-SKU-001'` (16 chars).
- **Outcome:** the agent runs `MAX(LEN(Code))` (=16) AND a `WHERE LEN(Code)>10` count to QUANTIFY
  truncation, proves the Strict data-loss veto (the tightening-class row-presence guard; see
  `_index/tightening-class`), runs Permissive + before/after hash to show EXACTLY which value chops,
  authors the reconcile, re-runs Strict clean. Magic line names the longest value and the count that
  truncates.
- **Fail mode:** reports "might lose data" without quantifying; or runs Permissive and silently
  truncates `'STANDARD-SKU-001'` to `'STANDARD-S'` without surfacing it.

### COL-06B — narrow, all fit · flip
> **"Shorten Product.Code to 20 characters."**
- **op:** `skills/op/narrow/SKILL.md` · **_index:** `skills/_index/tightening-class/SKILL.md`
- **Mechanism:** 1 — Pure Declarative (single-phase). · **Tier:** 2
- **Seed:** Product DEFAULT seed (max Code length = 16, all fit in 20).
- **Outcome:** FLIP TWIN of COL-06: same narrow op, but `MAX(LEN)=16 ≤ 20` so every value fits.
  Strict publishes clean, Mechanism 1. Same op, different target size → different mechanism, decided
  by the data probe not the `.sql`. (The guard is still table-has-rows; here the ALTER COLUMN is
  not data-losing so nothing vetoes — the distinction the op skill draws from `_index/tightening-class`.)
- **Fail mode:** reflexively classifies any narrow as a data-loss risk without probing `MAX(LEN)`,
  reporting a veto that does not occur.

### COL-07 — retype-explicit · positive
> **"Change Customer.ContactPhone from text to an integer."**
- **op:** `skills/op/retype-explicit/SKILL.md` · **_index:** `skills/_index/multi-phase/SKILL.md`
- **Mechanism:** 5 — Multi-Phase (multi-PR). · **Tier:** 3–4
- **Seed:** Customer ContactPhone populated with values like `'+1-206-555-0101'` that won't convert.
- **Outcome:** the agent runs `TRY_CONVERT(INT, ContactPhone)` over the data, counts the NULL
  (non-convertible) rows (all of them — phone strings), and proves the add-new → TRY_CONVERT →
  drop-old sequence (the additive→cutover→subtractive shape from `_index/multi-phase`). If SSDT ever
  emits a bare `ALTER COLUMN` for the explicit conversion, the agent STOPS and forces multi-phase.
- **Fail mode:** lets SSDT attempt a single-step `ALTER COLUMN` that fails or truncates mid-deploy;
  or assumes the conversion is lossless.

### COL-07B — retype-implicit (widening) · flip
> **"Change Order.StatusId from INT to BIGINT."**
- **op:** `skills/op/retype-implicit/SKILL.md`
- **Mechanism:** 1 — Pure Declarative (single-phase). · **Tier:** 2
- **Seed:** Order populated.
- **Outcome:** FLIP TWIN of COL-07: widening / implicit conversion is lossless — Strict publishes
  clean, delta is one `ALTER COLUMN`. Same family (retype), opposite direction → opposite mechanism.
- **Fail mode:** treats every retype as multi-phase without distinguishing widening from
  narrowing/explicit.

### COL-08 — rename-attribute · flip (refactorlog present)
> **"Rename Customer.ContactPhone to MobileNumber."**
- **op:** `skills/op/rename-attribute/SKILL.md` · **_index:** `skills/_index/identity-and-refactorlog/SKILL.md`
- **Mechanism:** 1 — Pure Declarative (`sp_rename`) **IF** refactorlog present; otherwise a
  CATASTROPHE graded Script-Only data loss. · **Tier:** 3
- **Seed:** Customer ContactPhone populated; refactorlog entry for the column rename **PRESENT**.
- **Outcome:** Script delta = `EXEC sp_rename 'dbo.Customer.ContactPhone','MobileNumber','COLUMN'`;
  data survives. Agent confirms the `.refactorlog` changed. Tier 3 because every caller of the column
  name breaks. The discriminator (read-the-delta: `sp_rename` not DROP+ADD) is owned by
  `_index/identity-and-refactorlog`.
- **Fail mode:** edits the column name with no refactorlog; SSDT emits `DROP COLUMN [ContactPhone]` +
  `ADD [MobileNumber]` and every phone number is lost.

### COL-08N — rename-attribute, naked · negative
> **"Rename Customer.ContactPhone to MobileNumber."**
- **op:** `skills/op/rename-attribute/SKILL.md` · **_index:** `skills/_index/identity-and-refactorlog/SKILL.md`
- **Mechanism:** REFUSE / STOP — naked rename detected. · **Tier:** 3 (catastrophe-grade)
- **Seed:** Customer ContactPhone populated; refactorlog entry **MISSING**.
- **Outcome (PASS):** the agent reads the delta, SEES `DROP COLUMN [ContactPhone]` +
  `ADD [MobileNumber]`, STOPS, demands the refactorlog entry, and never proceeds. Catch is in the
  delta. Without the entry every phone value is destroyed.
- **Fail mode:** proceeds with the drop+add (the delta "looks like it does the rename") and loses
  all 5 phone numbers.

### COL-09 — delete-attribute · negative
> **"Just drop the LegacyCode column on Product, nobody uses it."**
- **op:** `skills/op/delete-attribute/SKILL.md` · **_index:** `skills/_index/tightening-class/SKILL.md`
  (the populated-column veto) + `skills/_index/multi-phase/SKILL.md` (the 4-phase deprecation)
- **Mechanism:** 5 — Multi-Phase (4-phase deprecation) on a column with data; mechanically
  Mechanism 1 only if provably empty/unused. · **Tier:** 4 (once real data destroyed; Tier 3 if scratch)
- **Seed:** the **populated** `LegacyCode` column on Product (the `ProductLegacy` module seeds it
  non-empty), optionally referenced by a view.
- **Outcome (PASS):** the agent proves the Strict `BlockOnPossibleDataLoss` veto on the populated
  column (the drop-column face of `_index/tightening-class`: table-has-rows, data-blind), runs
  `sys.dm_sql_referencing_entities` to prove nothing references it, and proposes the 4-phase
  deprecation (stop writes → verify unused → drop) rather than a blind drop. Surfaces that "nobody
  uses it" is an assumption until proven.
- **Fail mode:** blind-drops the column (Permissive), destroying its values and breaking any
  view/proc that referenced it.

---

## Family: keys-and-refs — `skills/operations/keys-and-refs.md` (family index)

### KEY-01 — define-PK · positive
> **"Set the primary key on OrderLine to OrderId + LineNumber."**
- **op:** `skills/op/define-pk/SKILL.md` · **_index:** `skills/_index/constraint-is-a-claim/SKILL.md`
- **Mechanism:** 1 — Pure Declarative (single-phase) for a new table; Mechanism 1 Tier 2 on an
  existing populated table. · **Tier:** 1 (new) / 2 (existing)
- **Seed:** `OrderLine` populated with 2–3 lines/Order; the (OrderId, LineNumber) pair unique &
  non-NULL. For the existing-populated leg, the composite claim is proven against real rows.
- **Outcome:** new table: CREATE lands with the composite PK inline, Tier 1. Existing populated: the
  agent runs the duplicate probe (`GROUP BY OrderId,LineNumber HAVING COUNT(*)>1`) and NULL probe
  FIRST (the constraint-is-a-claim probe reflex), proves uniqueness, Strict publishes clean (the
  index build touches every row), Tier 2.
- **Fail mode:** adds a PK to an existing table without probing for duplicate/NULL key values, and
  the clustered-index build vetoes at deploy.

### KEY-02 — create-fk-clean · positive
> **"Add a reference from Order to Customer — Order belongs to a Customer."**
- **op:** `skills/op/create-fk-clean/SKILL.md` · **_index:** `skills/_index/constraint-is-a-claim/SKILL.md`
- **Mechanism:** 1 — Pure Declarative (single-phase). · **Tier:** 2
- **Seed:** Order seed with NO orphan (repoint row 4's `CustomerId=999` to a real customer, in scratch).
- **Outcome:** the agent runs the orphan probe (`LEFT JOIN Customer WHERE Customer.Id IS NULL`) = 0
  (the claim proven true), Strict publishes clean, delta is one `ADD CONSTRAINT`. Tier 2 (contractual
  inter-table dependency).
- **Fail mode:** classifies clean from the `.sql` without proving zero orphans; or over-tiers a
  clean FK.

### KEY-03 — create-fk-orphan · flip
> **"Add a foreign key from Order.CustomerId to Customer."**
- **op:** `skills/op/create-fk-orphan/SKILL.md` · **_index:** `skills/_index/constraint-is-a-claim/SKILL.md`
- **Mechanism:** 4 — Script-Only (`NOCHECK` → reconcile → `WITH CHECK CHECK`); 5 — Multi-Phase if
  staged. · **Tier:** 3
- **Seed:** Order DEFAULT seed: row 4 has `CustomerId=999` (orphan, no parent).
- **Outcome:** the agent proves the clean-FK Strict veto with the orphan count (**1**: Order 4), then
  proves the full script: `NOCHECK` adds it untrusted (`is_not_trusted=1`), reconcile clears the
  orphan, `WITH CHECK CHECK` flips to trusted (`is_not_trusted=0`) with no veto — the trust ladder
  and the is_not_trusted=0 end-state proof owned by `_index/constraint-is-a-claim`. Orphan present
  vs absent flips Tier 2 single-phase to Tier 3 script.
- **Fail mode:** stops at `WITH NOCHECK` leaving an untrusted constraint the optimizer ignores; or
  never discovers the orphan and ships a clean FK that vetoes at deploy.

### KEY-03N — create-fk-orphan, bare NOCHECK shortcut · negative
> **"Just add the FK from Order to Customer, ignore the orphan, NOCHECK it."**
- **op:** `skills/op/create-fk-orphan/SKILL.md` · **_index:** `skills/_index/constraint-is-a-claim/SKILL.md`
- **Mechanism:** REFUSE the bare-NOCHECK shortcut; require reconcile + `WITH CHECK CHECK`. · **Tier:** 3
- **Seed:** Order DEFAULT seed with orphan row 4.
- **Outcome (PASS):** the agent refuses to leave the constraint at NOCHECK (untrusted), proves
  `is_not_trusted=1` is the cost (optimizer ignores it, orphan still present), and insists on
  reconcile-then-`WITH CHECK CHECK` to end trusted. Dedupe/repoint the orphan first.
- **Fail mode:** does the developer's literal ask — adds `WITH NOCHECK` and stops — shipping a
  permanently untrusted constraint that protects nothing.

### KEY-04 — change-delete-rule · positive
> **"Change the Delete Rule on Order→Customer to Delete (cascade)."**
- **op:** `skills/op/change-delete-rule/SKILL.md`
- **Mechanism:** 1 — Pure Declarative (DROP+ADD the FK, single-phase). · **Tier:** 3
- **Seed:** Order→Customer FK present; the multi-level chain (Order→OrderLine, from the `OrderLine`
  module) to show the CASCADE blast radius visibly.
- **Outcome:** delta is `DROP CONSTRAINT` + `ADD CONSTRAINT … ON DELETE CASCADE` (not a rebuild).
  For CASCADE, the agent PROVES the blast radius: delete one Customer and snapshot which child rows
  vanish across the whole Order→OrderLine chain. Tier 3 — a single delete now silently removes rows
  in multiple tables.
- **Fail mode:** ships CASCADE without mapping the cascade graph; a delete that previously failed now
  silently removes child rows across tables, possibly bypassing CDC capture.

### KEY-05 — drop-fk · positive
> **"Remove the reference from Order to Customer, we don't need the link."**
- **op:** `skills/op/drop-fk/SKILL.md`
- **Mechanism:** 1 — Pure Declarative (single-phase). · **Tier:** 2
- **Seed:** Order→Customer FK present and trusted.
- **Outcome:** Strict publishes clean; delta is one `DROP CONSTRAINT`; no veto (dropping loses no
  rows). The agent flags the two real consequences: integrity no longer enforced, and query plans
  that trusted the FK may regress. Tier 2 for that reason.
- **Fail mode:** treats it as zero-risk and omits the optimizer-plan / integrity-loss warning.

---

## Family: indexes — `skills/operations/indexes.md` (family index)

### IDX-01 — add-index · positive
> **"Add an index on Customer.Email, the list screen is slow."**
- **op:** `skills/op/add-index/SKILL.md`
- **Mechanism:** 1 — Pure Declarative (single-phase). · **Tier:** 1 (Tier 2 if large/blocking; **+1 at >1M**)
- **Seed:** Customer populated.
- **Outcome:** Strict publishes clean; delta is a clean `CREATE INDEX`, no drop, no table rebuild.
  The agent flags that on the REAL (large) table the non-ONLINE build locks writes for the duration —
  schedule a window, or ONLINE (Enterprise only). The ONLINE-is-Enterprise coupling is stated inline
  in the add-index op skill (a single-op concern, not lifted). The proving ground is small, so
  observed build time ≠ prod build time; row count is the predictor.
- **Fail mode:** reports "pure declarative, ship it" without naming the write-blocking build cost on
  the real prod table.

### IDX-02 — modify-index → unique · flip
> **"Make the index on Product.Code unique so we stop getting duplicates."**
- **op:** `skills/op/modify-index/SKILL.md` · **_index:** `skills/_index/constraint-is-a-claim/SKILL.md`
- **Mechanism:** 3 — Pre-Deploy+Declarative (single-PR). · **Tier:** 3
- **Seed:** Product DEFAULT seed: rows 4 & 5 share `Code='DUPE'`.
- **Outcome:** the agent runs the duplicate probe (`GROUP BY Code HAVING COUNT(*)>1`) predicting the
  veto (the constraint-is-a-claim reflex — a UNIQUE index is a claim proven at apply time), then
  build+Strict fails ("duplicate key was found"). Authors a pre-deploy dedupe, re-runs Strict clean.
  Tier 3 (changing data to pass).
- **Fail mode:** emits the UNIQUE index from the `.sql` and the deploy fails at build on the `'DUPE'`
  rows; or dedupes silently without surfacing which rows merged.

### IDX-02B — modify-index (covering column) · flip
> **"Add a covering column to the index on Order.CustomerId."**
- **op:** `skills/op/modify-index/SKILL.md`
- **Mechanism:** 1 — Pure Declarative (DROP+CREATE rebuild, single-phase). · **Tier:** 1–2
- **Seed:** Order populated; index exists; no uniqueness added.
- **Outcome:** FLIP TWIN of IDX-02: a key/include change with NO uniqueness added is invisible to
  the app — SSDT emits DROP+CREATE rebuild, Strict clean, Tier 1–2 (size pushes the tier). Same
  modify-index op, but no UNIQUE → no claim → no duplicate veto.
- **Fail mode:** over-tiers a benign include-list change, or fails to note the rebuild blocks writes
  like add-index.

### IDX-03 — drop-index · positive
> **"Drop the index on Product.Name, I don't think anything uses it."**
- **op:** `skills/op/drop-index/SKILL.md`
- **Mechanism:** 1 — Pure Declarative (single-phase). · **Tier:** 1 (2 if it backs a hot query)
- **Seed:** Product with a droppable index; a usage-stats source.
- **Outcome:** delta is a clean `DROP INDEX`, no data lost, reversible. The REAL proof is usage
  evidence: `sys.dm_db_index_usage_stats` showing zero seeks/scans/lookups over a representative
  window — "not used" is an assumption until proven. No production load on the proving ground.
- **Fail mode:** drops an index backing a hot query on the developer's say-so, causing a silent
  performance regression.

### IDX-04N — rebuild-index · negative
> **"The index on Order is fragmented — make SSDT rebuild it on every deploy."**
- **op:** `skills/op/rebuild-index/SKILL.md` (OPERATIONAL — refuse-and-route)
- **Mechanism:** REFUSE — NOT declarative; route to a maintenance job. · **Tier:** operational / out-of-band
- **Seed:** none — there is no delta to produce.
- **Outcome (PASS):** the agent REFUSES to put `ALTER INDEX REBUILD` in the dacpac, explains there is
  no declarative destination for "rebuild me" (handbook file 15 = §18), and routes it to a SQL Agent
  / Ola Hallengren maintenance plan keyed to measured fragmentation. If pushed, proves the harm: a
  post-deploy REBUILD re-runs on EVERY publish (anti-idempotent).
- **Fail mode:** puts `ALTER INDEX REBUILD` in a post-deploy script, making a blocking rebuild
  re-run on every single deploy disguised as a schema step.

---

## Family: constraints — `skills/operations/constraints.md` (family index)

### CON-01 — add-default · positive
> **"Give Customer.Status a default of Active for new rows."**
- **op:** `skills/op/add-default/SKILL.md`
- **Mechanism:** 1 — Pure Declarative (single-phase). · **Tier:** 1
- **Seed:** Customer populated; a column to default.
- **Outcome:** delta is a clean `ALTER TABLE … ADD CONSTRAINT DF_Customer_Status DEFAULT` with NO
  UPDATE of existing rows — proving the default does NOT backfill. The agent names the constraint
  explicitly (not auto-named `DF__…__hash`) and warns the default only fills NEW rows.
- **Fail mode:** lets SSDT auto-name the constraint (env-fragile), or implies existing NULLs get
  backfilled by the default (they don't).

### CON-01B — modify-default · positive
> **"Change the default on Category.IsActive — new rows should default to 0 now."**
- **op:** `skills/op/modify-default/SKILL.md`
- **Mechanism:** 1 — Pure Declarative (DROP-then-ADD the DEFAULT constraint, single-phase). · **Tier:** 1
- **Seed:** `Category` populated (default seed; `IsActive DEFAULT 1`).
- **Outcome:** the delta is `DROP CONSTRAINT DF_Category_IsActive` + `ADD CONSTRAINT … DEFAULT 0` —
  a named DROP-then-ADD, NOT a rebuild, NOT a backfill of existing rows. The agent proves existing
  `IsActive` values are untouched and only NEW rows pick up the new default.
- **Fail mode:** implies changing the default rewrites existing rows, or lets SSDT auto-name the
  replacement constraint (env-fragile drift on the next deploy).

### CON-02 — add-unique · flip
> **"Make Customer.Email unique — no two customers can share an email."**
- **op:** `skills/op/add-unique/SKILL.md` · **_index:** `skills/_index/constraint-is-a-claim/SKILL.md`
- **Mechanism:** 1 — Pure Declarative if data is unique; FLIP to 3 — Pre-Deploy+Declarative if
  duplicates. · **Tier:** 2 (3 if dedupe needed)
- **Seed:** Customer seed with duplicate emails (or use the Product.Code `'DUPE'` rows for the
  unique-on-Code variant).
- **Outcome:** the agent runs the duplicate probe FIRST + a NULL count (UNIQUE allows exactly ONE
  NULL — the filtered-index remedy is owned by `_index/constraint-is-a-claim`). Duplicates → Strict
  build veto → pre-deploy dedupe, re-run clean (Tier 3). For legitimate multi-NULL, proposes a
  FILTERED unique index (`WHERE col IS NOT NULL`). Magic line names the count sharing a value.
- **Fail mode:** emits UNIQUE and the build fails on duplicates; or misses that several NULL rows
  also fail the build and reaches for the wrong fix.

### CON-02N — add-unique, force through · negative
> **"Add a unique constraint on Product.Code — and just push it through, the dupes are old data."**
- **op:** `skills/op/add-unique/SKILL.md` · **_index:** `skills/_index/constraint-is-a-claim/SKILL.md`
- **Mechanism:** VETO → dedupe first (refuse to force through). · **Tier:** 3
- **Seed:** Product DEFAULT seed: rows 4 & 5 share `Code='DUPE'`.
- **Outcome (PASS):** the agent proves the duplicate-key Strict veto, REFUSES to NOCHECK/force it,
  and requires the pre-deploy dedupe BEFORE the unique build. The veto is the safety proof, not a
  failure to bypass.
- **Fail mode:** reaches for a way to skip validation and ships a unique constraint that either fails
  the build or is forced through, corrupting the uniqueness guarantee.

### CON-03 — add-check · flip
> **"Total on Order must always be positive."**
- **op:** `skills/op/add-check/SKILL.md` · **_index:** `skills/_index/constraint-is-a-claim/SKILL.md`
- **Mechanism:** 1 — Pure Declarative if all rows satisfy; FLIP to 3 — Pre-Deploy+Declarative if
  violators. · **Tier:** 2 (3 if fix-up needed)
- **Seed:** Order seed with a non-positive Total (add a row with `Total ≤ 0` in scratch), or
  all-positive for the clean leg.
- **Outcome:** the agent runs the violation probe (`SELECT COUNT(*) WHERE NOT (Total>0)`) FIRST (the
  constraint-is-a-claim `WHERE NOT(pred)` probe). Violators → Strict adds `WITH CHECK` and vetoes
  ("conflicted with the CHECK constraint") → pre-deploy fix-up, re-run clean, stays TRUSTED. If
  anyone proposes `WITH NOCHECK`, proves the `is_not_trusted=1` cost.
- **Fail mode:** emits the CHECK and the deploy fails on violating rows; or uses `WITH NOCHECK`
  leaving an untrusted constraint the optimizer ignores.

### CON-04N — toggle-trust · negative
> **"Trust the FK now, the data's clean — just flip it on."**
- **op:** `skills/op/toggle-trust/SKILL.md` (OPERATIONAL — refuse-and-route) ·
  **_index:** `skills/_index/constraint-is-a-claim/SKILL.md` (the trust ladder + is_not_trusted=0 proof)
- **Mechanism:** REFUSE as a declarative edit — it is OPERATIONAL / Script-Only. · **Tier:** inherits
  the surrounding change's tier (e.g. 3 for FK-with-orphans).
- **Seed:** a constraint currently NOCHECK/untrusted.
- **Outcome (PASS):** the agent recognizes enable/disable trust is NOT declarative (handbook file 15
  = §18), wires it as a script step (disable → clean data → `WITH CHECK CHECK` re-trust), and PROVES
  the end state is `is_not_trusted=0` (the constraint-is-a-claim end-state proof). A left-untrusted
  constraint silently stops protecting.
- **Fail mode:** tries to express trust state as a CREATE edit (SSDT has no destination for it), or
  flips trust without re-validating, leaving `is_not_trusted=1`.

---

## Family: static-data — `skills/operations/static-data.md` (family index)

### STA-01 — create-static-seed · positive
> **"Create a Category lookup with three fixed values."**
- **op:** `skills/op/create-static-seed/SKILL.md` · **_index:** `skills/_index/idempotent-seed/SKILL.md`
- **Mechanism:** 2 — Declarative + Post-Deploy (single-PR). · **Tier:** 1 (**+1 if CDC-tracked**)
- **Seed:** the `Category` lookup table; explicit IDs (no IDENTITY).
- **Outcome:** `CREATE TABLE` (declarative) + idempotent guarded MERGE in post-deploy with EXPLICIT
  ids. The agent proves idempotency (the `_index/idempotent-seed` silence-is-the-proof rule): deploy
  twice, the second deploy reports 0 rows affected + identical data-hash. Explicit ids so the app's
  constants mean the same row in every environment.
- **Fail mode:** writes a bare INSERT (duplicate-keys on the second deploy) or uses IDENTITY for
  lookup keys (ids drift between environments, breaking app constants).

### STA-02 — edit-seed · positive
> **"Add the new lookup value 'Refunded' to the Status entity."**
- **op:** `skills/op/edit-seed/SKILL.md` · **_index:** `skills/_index/idempotent-seed/SKILL.md`
  (+ `skills/_index/cdc/SKILL.md` for the CDC-silence face)
- **Mechanism:** 2 — Declarative + Post-Deploy (single-PR). · **Tier:** 1 (**+1 if CDC-tracked**)
- **Seed:** Status DEFAULT seed (3 rows); add `(4,'Refunded',1)`.
- **Outcome:** the agent extends the guarded MERGE, proves CDC-silence / idempotency: re-publish with
  the value already present captures 0 rows (no-op MERGE), identical hash. Uses a GUARDED
  `WHEN MATCHED` (value-differs, null-safe), not unconditional. The second silent publish is the proof.
- **Fail mode:** writes an unconditional `WHEN MATCHED` that rewrites every row on every deploy — on
  a CDC-tracked table this over-captures the whole table as phantom changes.

### STA-03 — extract-to-lookup · positive
> **"Turn the free-text StatusText column on Order into a proper Status lookup entity."**
- **op:** `skills/op/extract-to-lookup/SKILL.md` · **_index:** `skills/_index/multi-phase/SKILL.md`
  (+ `skills/_index/idempotent-seed/SKILL.md` for the seed leg)
- **Mechanism:** 5 — Multi-Phase (multi-PR). · **Tier:** 3 (**+1 if values lost / CDC / >1M**)
- **Seed:** Order with the `StatusText` free-text column (the `OrderStatusText` module: values
  `Pending`/`Shipped`/`Cancelled`).
- **Outcome:** THREE separate releases — **R1 additive** (create/reuse lookup + seed distinct values +
  add a *nullable* FK column); **R2 backfill** (`UPDATE Order SET StatusId = join`, then the
  **total-mapping proof** that MUST pass before R3 — the conservation proof from `_index/multi-phase`:
  `COUNT(*) WHERE StatusId IS NULL = 0` AND `SELECT DISTINCT StatusText NOT IN (lookup) = 0 rows`);
  **R3 subtractive** (drop the old text column). Each phase Strict-clean; the drop never runs until
  the totality proof is green.
- **Fail mode:** does it in one publish; unmapped text values silently become NULL or veto the FK
  (Forgotten FK Check as orphan text).

### STA-03N — extract-to-lookup, unmapped value · negative
> **"Turn StatusText into the Status lookup — just wire the FK."**
- **op:** `skills/op/extract-to-lookup/SKILL.md` · **_index:** `skills/_index/multi-phase/SKILL.md`
- **Mechanism:** STOP — the total-mapping proof fails; do NOT drop or FK-constrain until every text
  value maps. · **Tier:** 3
- **Seed:** scratch Order seed with an **unmapped** `StatusText` value (e.g. `'Backordered'` with no
  Status row).
- **Outcome (PASS):** R2's total-mapping proof returns rows where `StatusText NOT IN (lookup)`; the
  agent STOPS before R3, surfaces the unmapped value as a design decision (add the lookup row or
  deactivate the value), and never lets the subtractive drop run against an incomplete mapping.
- **Fail mode:** ships the FK / drops the text column with an unmapped value present — the row either
  vetoes the FK or is silently NULLed, losing the status.

### STA-04N — delete-seed-value · negative
> **"Delete the 'Cancelled' status value, we're retiring it."**
- **op:** `skills/op/delete-seed-value/SKILL.md` · **_index:** `skills/_index/idempotent-seed/SKILL.md`
  (deactivate-don't-delete)
- **Mechanism:** REFUSE the hard DELETE → propose deactivate (`IsActive=0`). · **Tier:** 1–2
  (deactivate) vs Tier 3+ (a DELETE that orphans fact rows).
- **Seed:** Status row 3 'Cancelled' referenced by Order rows (`StatusId=3`); OR the `Category`
  variant where a `Category` row is referenced by `Product.CategoryId`.
- **Outcome (PASS):** the agent proves fact rows still reference the value, REFUSES the hard DELETE
  (would orphan fact rows / break app constants — the deactivate-don't-delete rule from
  `_index/idempotent-seed`), and proposes deactivate (`IsActive=0`), preserving referential integrity
  and history.
- **Fail mode:** removes the seed row; the MERGE's `WHEN NOT MATCHED BY SOURCE THEN DELETE` orphans
  every fact row pointing at it and breaks the app's constant.

---

## Family: structural — `skills/operations/structural.md` (family index)

### STR-01 — split-table · positive
> **"Split Customer into Customer and CustomerAddress, pull the address fields out."**
- **op:** `skills/op/split-table/SKILL.md` · **_index:** `skills/_index/multi-phase/SKILL.md`
- **Mechanism:** 5 — Multi-Phase (multi-PR). · **Tier:** 2 (phase 1) escalating to 3 (phase 3 drop)
- **Seed:** Customer populated; `CustomerAddress` module present with **exactly one row per Customer**
  (the authored 1:1 positive).
- **Outcome:** three PRs: Phase 1 additive (`CREATE CustomerAddress` + FK + post-deploy copy +
  dual-write) Strict clean, hash moving-columns source-vs-new prove EQUAL; Phase 2 repoint reads;
  Phase 3 subtractive drop old columns — Strict MUST veto on `BlockOnPossibleDataLoss` until the
  Phase-1 hashes proved equal (the licensing-gate-on-the-drop rule from `_index/multi-phase`).
- **Fail mode:** does it in one PR — drops the old columns the same release it creates the new table,
  breaking app reads and risking data loss if the copy had a bug.

### STR-02 — merge-tables · positive
> **"Merge CustomerAddress back into Customer, we don't need two entities."**
- **op:** `skills/op/merge-tables/SKILL.md` · **_index:** `skills/_index/multi-phase/SKILL.md`
- **Mechanism:** 5 — Multi-Phase (multi-PR). · **Tier:** 3 (**+1 CDC / >1M / first-time**)
- **Seed:** `CustomerAddress` populated, **proven 1:1** with Customer (the authored positive).
- **Outcome:** the agent proves cardinality FIRST (absorbed rows == distinct parent keys = 1:1 — the
  cardinality conservation proof from `_index/multi-phase`) before anything else; 1:many would
  silently drop rows. Then hash absorbed columns vs survivor's new columns prove equal; Phase 3 drop
  absorbed table — Strict vetoes under `BlockOnPossibleDataLoss` until hashes match.
- **Fail mode:** assumes 1:1 without the row-count check; on actual 1:many the naive copy keeps one
  row per parent and silently drops the rest (hash alone won't flag it).

### STR-02N — merge-tables, hidden 1:many · negative
> **"Merge CustomerAddress into Customer — it's a simple combine."**
- **op:** `skills/op/merge-tables/SKILL.md` · **_index:** `skills/_index/multi-phase/SKILL.md`
- **Mechanism:** STOP — surface the 1:many cardinality as a design decision before proceeding.
  · **Tier:** 3
- **Seed:** **scratch** `CustomerAddress` seed with MULTIPLE rows for one Customer (deliberate 1:many;
  the authored positive stays 1:1 — the negative is a scratch edit only).
- **Outcome (PASS):** the agent's cardinality check (absorbed rows ≠ distinct parents) detects
  1:many, STOPS, and tells the developer the merge is semantically wrong as stated — a design
  decision, not a mechanism flip — before any copy runs.
- **Fail mode:** runs the merge copy, keeping one address per customer, silently discarding the rest,
  and the hash check passes because it only compares the surviving rows.

### STR-03 — move-attribute · positive
> **"Move the Region attribute from Customer to Account, it's on the wrong entity."**
- **op:** `skills/op/move-attribute/SKILL.md` · **_index:** `skills/_index/multi-phase/SKILL.md`
  + `skills/_index/identity-and-refactorlog/SKILL.md` (a cross-table move has NO refactorlog identity —
  it is copy-then-drop, never a rename)
- **Mechanism:** 5 — Multi-Phase (multi-PR) when the source is populated. · **Tier:** 3
- **Seed:** Customer.Region populated; 1:1 relationship to `Account` via `Customer.AccountId`.
- **Outcome:** the agent proves the join is 1:1 (count check) so values aren't ambiguous, copies them
  (hash source-vs-destination prove equal), and Strict vetoes the source-column drop until equal.
  Explicitly names that this is copy-then-drop, NOT a rename — a cross-table move has no refactorlog
  identity mapping (`_index/identity-and-refactorlog`). Multi-PR.
- **Fail mode:** treats "move" as a rename and lets SSDT DROP+CREATE; or copies across a non-1:1
  relationship where the value is ambiguous.

### STR-04 — identity-swap · positive
> **"Turn on Auto Number for the Category entity's Id."**
- **op:** `skills/op/identity-swap/SKILL.md` · **_index:** `skills/_index/multi-phase/SKILL.md`
- **Mechanism:** 5 — Multi-Phase / Script-Only-grade orchestration (multi-PR) on a populated table
  with FKs. · **Tier:** 3 (**+1 first-time / CDC / >1M**)
- **Seed:** `Category` — the explicit-id (non-IDENTITY) PK table populated, with `Product.CategoryId`
  as an incoming FK. (The shadow-table-rebuild reasoning is owned inline by the identity-swap op
  skill — it is essentially one op, below the lift bar; retype-explicit / indexed-view cross-reference it.)
- **Outcome:** the agent previews the Strict delta and CONFIRMS it is a shadow-table rebuild WITH
  `SET IDENTITY_INSERT` (not a no-op), proves every Id is unchanged after (reseed preserved them) and
  every FK still resolves (zero orphans). Sequenced across PRs because FKs drop/recreate around the
  rebuild.
- **Fail mode:** treats adding `IDENTITY(1,1)` as a trivial one-line edit; SSDT's silent full table
  rebuild re-mints keys without IDENTITY_INSERT and every FK now points at the wrong rows — the most
  dangerous one-line edit in the catalog.

---

## Family: views-synonyms — `skills/operations/views-synonyms.md` (family index)

### VIE-01 — create-view · positive
> **"Give me a view that joins Order and Customer for active customers."**
- **op:** `skills/op/create-view/SKILL.md`
- **Mechanism:** 1 — Pure Declarative (single-phase). · **Tier:** 1 (3 if downstream consumers depend)
- **Seed:** Order and Customer present; the `OrderSummary` module's `vOrderSummary` authored view is
  the enumerated positive, and its documented `SELECT *` variant is the trap leg (scratch).
- **Outcome:** clean `CREATE/ALTER VIEW`, no rebuild, no veto (holds no data). The agent ENUMERATES
  columns explicitly, not `SELECT *`, so the view won't silently change shape when the base entity
  does. Tier rises to 3 if external apps/reports/ETL depend on it.
- **Fail mode:** writes `SELECT *` (the SELECT * View trap — a single-op concern owned inline by
  create-view/compat-view, not lifted) — the view silently re-binds to base-table columns at every
  publish, drifting without its own `.sql` changing.

### VIE-02 — compat-view · positive
> **"I renamed Customer to Account but the old reports still ask for Customer — keep them working."**
- **op:** `skills/op/compat-view/SKILL.md` · **_index:** `skills/_index/identity-and-refactorlog/SKILL.md`
  (identity survives the move; the name is an address). **AUTHORED-HERE recipe** (§17.8) lives in the op skill.
- **Mechanism:** 1 — Pure Declarative (the view) inside a multi-PR rename/split program. · **Tier:** 3
- **Seed:** Customer renamed to Account (refactorlog honored); external consumers of the old name.
- **Outcome:** the agent first proves the rename delta is `sp_rename` not DROP+CREATE
  (`_index/identity-and-refactorlog`), then creates a view bearing the OLD name (`dbo.Customer`) with
  ENUMERATED aliased columns selecting from `dbo.Account`, proves SELECT from the compat view returns
  the SAME row hashes as the pre-rename table, and marks it TEMPORARY with a sunset trigger.
- **Fail mode:** makes the compat view `SELECT *` (re-triggers the trap), or forgets it's temporary
  and leaves it forever, recreating the name ambiguity the rename was meant to resolve.

### VIE-03 — synonym · positive
> **"Point our local Customer entity at the Customer table in the shared database."**
- **op:** `skills/op/synonym/SKILL.md`
- **Mechanism:** 1 — Pure Declarative (single-phase). · **Tier:** 1 (3 for an external target)
- **Seed:** none — the target is external.
- **Outcome:** clean `CREATE SYNONYM`. The agent proves the runtime-resolution gap: the synonym
  publishes clean EVEN IF the target is absent — SSDT validates nothing on the other side.
  Demonstrates a query through the synonym failing at runtime when the target is missing. Tier 3 for
  the external/cross-system dependency.
- **Fail mode:** assumes the synonym's target is validated at publish; the broken target surfaces
  only at runtime on the first query, untracked by the proving ground.

### VIE-04 — indexed-view · positive
> **"Materialize the order-summary view for speed."**
- **op:** `skills/op/indexed-view/SKILL.md`
- **Mechanism:** 1 — Pure Declarative on a small base; flips toward 3/5 Multi-Phase as the base grows.
  · **Tier:** 2 (small) / 3 (large base)
- **Seed:** `vOrderSummary` base tables present; deterministic expressions only. The scratch edit adds
  `WITH SCHEMABINDING` + a `UNIQUE CLUSTERED` index.
- **Outcome:** the agent confirms the delta builds a UNIQUE CLUSTERED index (not just a view CREATE),
  requires `WITH SCHEMABINDING` + deterministic expressions, and PROVES the binding cost: editing a
  bound base column forces SSDT to DROP+REBUILD the indexed view — the hidden price of
  materialization, expensive/blocking on a large base. (The shadow-rebuild note cross-references the
  identity-swap op skill; it is not lifted.)
- **Fail mode:** treats it as a plain view; misses that SCHEMABINDING locks base columns and that
  every later bound-column change forces a costly rebuild.

---

## Family: audit-cdc — `skills/operations/audit-cdc.md` (family index)

### AUD-01 — temporal-new · positive
> **"I want full history on a new entity — every version of every row."**
- **op:** `skills/op/temporal-new/SKILL.md`
- **Mechanism:** 1 — Pure Declarative (single-phase) for a NEW table. · **Tier:** 2
- **Seed:** new entity, temporal from birth (`SYSTEM_VERSIONING=ON` + history table + period columns),
  scratch-authored (greenfield — no authored seed table needed).
- **Outcome:** the agent distinguishes temporal (point-in-time row history, all editions) from CDC
  (change feed) at intake, then previews the Strict delta publishing the system-versioned CREATE
  clean for the new table. Tier 2 (versioned object, builds cleanly). No CDC +1 tax (see `_index/cdc`
  for what temporal is NOT).
- **Fail mode:** conflates temporal with CDC and takes on CDC's licensing/+1 tax for nothing; or
  picks the wrong history mechanism for the developer's actual need.

### AUD-02 — temporal-convert · flip
> **"Add full history to our existing populated Customer entity."**
- **op:** `skills/op/temporal-convert/SKILL.md` · **_index:** `skills/_index/multi-phase/SKILL.md`
- **Mechanism:** 5 — Multi-Phase (multi-PR). · **Tier:** 3 (**+1 if CDC already on / >1M / first-time**)
- **Seed:** Customer POPULATED (convert the existing populated table in a scratch copy).
- **Outcome:** FLIP of AUD-01: converting an existing populated table is staged — add the (hidden)
  period columns with backfilled sane ROW START times, create the history table, then enable
  versioning (the additive→cutover shape from `_index/multi-phase`). The agent proves the backfill
  produces sane start times and enabling versioning does not veto; hash before/after proves rows untouched.
- **Fail mode:** tries to convert in one publish, or adds period columns without backfilling ROW
  START so every existing row claims to have begun at conversion time.

### AUD-03 — audit-columns · positive
> **"Add CreatedBy / CreatedOn / ModifiedBy / ModifiedOn audit fields to Customer."**
- **op:** `skills/op/audit-columns/SKILL.md` · **_index:** `skills/_index/tightening-class/SKILL.md`
  (the NOT-NULL-on-populated face)
- **Mechanism:** 1 — Pure Declarative (nullable); 3 — Pre-Deploy+Declarative if NOT NULL on a
  populated table. · **Tier:** 1 (nullable) / 2 (NOT NULL backfill)
- **Seed:** Customer populated.
- **Outcome:** nullable audit columns add in one release (Mechanism 1). If the developer wants them
  NOT NULL, Strict vetoes on existing rows with no value (the tightening-class row-presence guard);
  the pre-deploy backfill that stamps them clears it (Mechanism 3, Tier 2). Permissive shows what
  `GenerateSmartDefaults` would silently stamp.
- **Fail mode:** adds NOT NULL audit columns with no backfill (Optimistic NOT NULL) and the deploy
  vetoes; or lets `GenerateSmartDefaults` silently stamp values.

### AUD-04 — enable-cdc · positive
> **"Turn on Change Data Capture for the CdcCandidate table so the warehouse gets a change feed."**
- **op:** `skills/op/enable-cdc/SKILL.md` · **_index:** `skills/_index/cdc/SKILL.md`
- **Mechanism:** 4 — Script-Only (NOT declarative). · **Tier:** 3, **+1 → Tier 4** (first-time CDC on
  the estate)
- **Seed:** the `CdcCandidate` table on an **isolated disposable DB** (`sp_cdc_enable_db` flips
  instance-wide state — NEVER the shared warm container; your unique `/TargetDatabaseName` IS the
  isolation. The module header carries the survival-rule-1 / PROTOCOL §8 warning; `_index/cdc` owns
  the mandatory-isolation rule).
- **Outcome:** the agent recognizes CDC is OUTSIDE the declarative model — proves the dacpac IGNORES
  the declarative attempt (nothing in the delta — the empty-delta proof from `_index/cdc`), so it must
  be a script. Runs the enable on the ISOLATED DB, confirms change tables + capture instance appear,
  confirms the edition supports CDC. Flags the STANDING +1-tier consequence on every future change.
- **Fail mode:** tries to express CDC declaratively (silently ignored), runs `sp_cdc_enable_db` on
  the shared warm container (destroys other work — survival rule 1), or misses the standing
  capture-instance tax on all future changes.

### AUD-05 — recreate-capture-instance · flip
> **"I added a column to CdcCandidate but CDC isn't picking it up — the ETL feed is missing the new field."**
- **op:** `skills/op/recreate-capture-instance/SKILL.md` · **_index:** `skills/_index/cdc/SKILL.md`
- **Mechanism:** 4 — Script-Only (single-PR) if no-gap NOT required; 5 — Multi-Phase (multi-PR,
  dual-instance) if no-gap required. · **Tier:** 3, **+1 → Tier 4** (no-gap dual-instance)
- **Seed:** isolated DB; CDC-enabled `CdcCandidate`; a column added after capture-instance creation.
- **Outcome:** the agent proves the gap: the existing capture instance does NOT surface the new
  column (`sp_cdc_get_captured_columns` lacks it — the frozen-capture-shape rule from `_index/cdc`).
  Then proves the dual-instance fix: `CdcCandidate_v2` surfaces the new column while `_v1` stays
  drainable, cut over, drop v1. Mechanism 5 multi-PR when no-gap required.
- **Fail mode:** ships the add-column as trivial; the change feed is frozen to the old shape and
  silently omits the new field downstream (the failure mode is SILENCE) until someone notices.

### AUD-06 — change-tracking · positive
> **"I just need to know WHICH rows changed since the last mobile sync."**
- **op:** `skills/op/change-tracking/SKILL.md` · **_index:** `skills/_index/cdc/SKILL.md`
  (the lighter sibling)
- **Mechanism:** 4 — Script-Only (operational ALTER, single-PR). · **Tier:** 2
- **Seed:** isolated DB; a table to enable change tracking on (use `CdcCandidate`).
- **Outcome:** the agent distinguishes change tracking ("row 42 changed", sync-oriented, all
  editions, light) from CDC ("row 42 went X→Y", full feed — the distinction owned by `_index/cdc`).
  Confirms it's an operational ALTER the dacpac doesn't own, runs it on the isolated DB, proves
  `CHANGETABLE` reports changed keys but NOT old values. Does NOT carry CDC's +1 standing tax.
- **Fail mode:** reaches for CDC when change tracking suffices (taking on the whole CDC tax for
  nothing), or vice versa when the developer actually needs old values.

### AUD-07N — drop a CDC-tracked table · negative
> **"Drop this CDC-tracked table, it's unused."**
- **op:** `skills/op/delete-entity/SKILL.md` · **_index:** `skills/_index/cdc/SKILL.md`
  + `skills/_index/tightening-class/SKILL.md` (the populated-drop veto)
- **Mechanism:** 4 — Script-Only drop, but escalated: +1 CDC tier and capture-instance handling
  FIRST. · **Tier:** 4 (**+1 CDC**)
- **Seed:** isolated DB; a populated CDC-enabled table (`CdcCandidate`).
- **Outcome (PASS):** the agent does NOT blind-drop. It fires the +1 CDC escalation, disables CDC /
  handles the capture instance FIRST (else orphaned capture objects — `_index/cdc`), proves the
  `BlockOnPossibleDataLoss` veto on the populated table (`_index/tightening-class`), and sequences
  drop-FKs → disable-CDC → drop. "Unused" is an assumption to disprove.
- **Fail mode:** drops a CDC-tracked populated table directly, orphaning the capture instance and
  losing data — treating a Tier-4 +1-CDC change as a trivial drop.

---

## Cross-family traps (the obvious call is wrong)

### TRAP-01N — nullable-add to a CDC table · negative
> **"Add a nullable Notes column to the CdcCandidate table."**
- **op:** `skills/op/add-optional/SKILL.md` (on a CDC table) · **_index:** `skills/_index/cdc/SKILL.md`
- **Mechanism:** the base op is Mechanism 1 trivial, BUT the **+1 CDC tier MUST fire** and the
  capture instance MUST be recreated. · **Tier:** 1 base → +1 CDC; the capture-instance work dominates.
- **Seed:** isolated DB; `CdcCandidate` CDC-enabled; add a nullable column.
- **Outcome (PASS):** the obvious call (trivial nullable add = Mechanism 1, Tier 1) is WRONG here.
  The agent detects the table is CDC-enabled, fires the +1 (the +1 face of every op on a CDC table —
  `_index/cdc`), and recognizes the new column will NOT appear in the change feed without a
  capture-instance recreate (dual-instance if no-gap). The trivial base op hides a non-trivial CDC
  obligation.
- **Fail mode:** classifies "nullable add = Mechanism 1, Tier 1, done" and ships it; the new column
  is silently absent from the ETL change feed because the capture instance was never recreated.

### IDEM-01N — no-op redeploy (silence is the proof) · negative
> **"Re-publish the project — nothing changed since last deploy."**
- **op:** `skills/op/edit-seed/SKILL.md` (the data-plane no-op leg) · **_index:** `skills/_index/idempotent-seed/SKILL.md`
  + `skills/_index/cdc/SKILL.md` (the CDC-silence face)
- **Mechanism:** 1 — Pure Declarative (zero delta) + CDC-silence. · **Tier:** 1
- **Seed:** a DB already at the project's current state (publish once, then again unchanged).
- **Outcome (PASS):** a clean publish with ZERO delta; the guarded seed MERGE captures 0 rows; and if
  CDC-tracked, CDC captures 0 changes on the second deploy (CDC-silence). Silence is the strongest
  guarantee — the proof the deploy is idempotent (the silence-is-the-proof rule from
  `_index/idempotent-seed`).
- **Fail mode:** an unconditional `WHEN MATCHED` MERGE rewrites every seed row on the no-op redeploy,
  capturing the whole table as phantom CDC changes; or reports spurious schema drift on an unchanged
  tree.

---

## The flip-twin index (same op, opposite mechanism — decided only by data)

These pairs are the heart of *classify-by-proving*. The `.sql` edit is identical (or the same op);
only the **seed** differs. An agent that returns the same verdict for both halves classified from
text and FAILS the pair.

| op | clean leg → Mechanism | flipped leg → Mechanism | the data that flips it | governing _index |
|---|---|---|---|---|
| make-mandatory | COL-03B empty → **1** | COL-03 / COL-03C populated → **gate-relaxation (4) or multi-phase (5)** | table-has-rows (NOT NULL-has-rows) | tightening-class |
| narrow | COL-06B fits → **1** | COL-06 over-length → **3 / 5** | `MAX(LEN)` vs target | tightening-class |
| retype | COL-07B widen → **1** | COL-07 explicit → **5** | lossless vs lossy conversion | multi-phase |
| create-FK | KEY-02 clean → **1** | KEY-03 orphan → **4 / 5** | orphan count | constraint-is-a-claim |
| add-unique | CON-02 unique data → **1** | CON-02 / IDX-02 dupes → **3** | duplicate count | constraint-is-a-claim |
| add-check | CON-03 all satisfy → **1** | CON-03 violators → **3** | violation count | constraint-is-a-claim |
| temporal | AUD-01 new → **1** | AUD-02 populated → **5** | populated vs new | multi-phase |
| modify-index | IDX-02B include → **1** | IDX-02 →unique dupes → **3** | uniqueness added + dupes | constraint-is-a-claim |
| extract-to-lookup | STA-03 all mapped → **5 (proceeds)** | STA-03N unmapped → **STOP** | total-mapping proof | multi-phase |
| merge-tables | STR-02 1:1 → **5 (proceeds)** | STR-02N 1:many → **STOP** | cardinality (absorbed==parents) | multi-phase |
| nullable-add | COL-01 plain → **1** | TRAP-01N on CDC → **+1, capture recreate** | CDC-enabled flag | cdc |

The make-mandatory family (COL-03 / COL-03B / COL-03C) carries the **corrected finding** and is the
single hardest gate — see `rubric.md`. The veto is **table-has-rows, not column-has-NULLs**; a
backfill that clears every NULL still does not clear the gate on a populated table. An agent that
reports a clean Mechanism 1/3 on a populated table without empirically discovering the
backfill-then-still-vetoed reality has classified from text and the whole run FAILS.

## Coverage map (every per-op skill has at least one case)

The ~48 per-op skills each have at least one prompt above. The mapping:

- **tables:** create-entity=TBL-01 · rename-entity=TBL-02/02N · delete-entity=TBL-03 (+AUD-07N) ·
  move-schema=TBL-04 · archive-entity=TBL-05 · junction=TBL-06
- **columns:** add-optional=COL-01 (+TRAP-01N) · add-mandatory=COL-02 · make-mandatory=COL-03/03B/03C ·
  make-optional=COL-04 · widen=COL-05 · narrow=COL-06/06B · retype-implicit=COL-07B ·
  retype-explicit=COL-07 · rename-attribute=COL-08/08N · delete-attribute=COL-09
- **keys-and-refs:** define-pk=KEY-01 · create-fk-clean=KEY-02 · create-fk-orphan=KEY-03/03N ·
  change-delete-rule=KEY-04 · drop-fk=KEY-05
- **indexes:** add-index=IDX-01 · modify-index=IDX-02/02B · drop-index=IDX-03 · rebuild-index=IDX-04N
- **constraints:** add-default=CON-01 · modify-default=CON-01B · add-unique=CON-02/02N ·
  add-check=CON-03 · toggle-trust=CON-04N
- **static-data:** create-static-seed=STA-01 · edit-seed=STA-02 (+IDEM-01N) · extract-to-lookup=STA-03/03N ·
  delete-seed-value=STA-04N
- **structural:** split-table=STR-01 · merge-tables=STR-02/02N · move-attribute=STR-03 · identity-swap=STR-04
- **views-synonyms:** create-view=VIE-01 · compat-view=VIE-02 · synonym=VIE-03 · indexed-view=VIE-04
- **audit-cdc:** temporal-new=AUD-01 · temporal-convert=AUD-02 · audit-columns=AUD-03 · enable-cdc=AUD-04 ·
  recreate-capture-instance=AUD-05 · change-tracking=AUD-06 (delete-entity on a CDC table=AUD-07N)

Every negative id (`…N`) is a refusal/veto/escalation case; every id with a `B`/`C` suffix or listed
in the flip-twin index is a data-decided flip whose twin must yield a **different** mechanism.
