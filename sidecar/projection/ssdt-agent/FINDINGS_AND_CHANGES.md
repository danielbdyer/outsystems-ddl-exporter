# Findings & Changes — the active record of what we have proven and what we will change

**What this is.** The active working document for the deployment-rigor pass. Two halves:

- **Proven findings** — truths established by publishing to a real SQL Server on this branch
  (`sqlpackage 170.4.83.3`, SQL Server 2022, 2026-08-16). These are settled; do not re-argue one
  without a new publish that overturns it.
- **The change plan** — what we will change in the tree, in a **generalized** form (the doctrine
  every op inherits) and a **specific-target** form (per op). Each item is marked **PROVEN**
  (ready to apply) or **TO PROVE** (staged, needs its own publish first).

`THE_DECISION_TREE.md` and `THE_RECORD_FORMS.md` are the standard; this document is where new
truths land and where changes to that standard are staged before they are folded in.

---

## Part 1 — The axiom: the deployment gate cannot be toggled

The target pipeline is **Azure DevOps → Octopus**, which builds the SQL project to a dacpac and
publishes it. In that pipeline the publish always runs with **`BlockOnPossibleDataLoss = true`**,
and a change cannot toggle it off for its own deploy.

**The consequence, stated as an axiom:**

> Any change whose DacFx declarative difference contains a data-loss step — narrow a column, drop
> a column, drop a table, make a populated column `NOT NULL`, retype where a value will not
> convert — **cannot deploy** through this pipeline as a single declarative change. It must be
> shaped so DacFx generates **no** data-loss step.

Every op below is measured against this axiom. Relaxing the gate is not an option we have.

---

## Part 2 — Proven findings

Each was published to a throwaway database on the live server; the database name is the receipt.

- **F1 — A narrowing on a populated table blocks. (PROVEN)** Model `Code NVARCHAR(50) → NVARCHAR(10)`,
  no pre-deploy, `BlockOnPossibleDataLoss = true` → refused, `Msg 50000` (the row-presence guard
  `IF EXISTS(SELECT TOP 1 1 …) RAISERROR`). The column stayed 50. (DB `prove_nvA`.) The guard is
  table-has-rows, not does-the-data-fit.

- **F2 — Putting the ALTER in a pre-deploy while the model also narrows does NOT help, and half-applies. (PROVEN — the trap)**
  Model `→ NVARCHAR(10)` **plus** a pre-deploy that shortens the data and runs the `ALTER COLUMN`
  itself → still refused, `Msg 50000`. **But the column was already 10** afterward. DacFx computes
  its plan against the *original* database before the pre-deploy runs, so the guarded ALTER is
  still in the main script and still fires; meanwhile the pre-deploy's own ALTER had already
  committed. The result is a **failed deploy with a half-applied schema** — worse than no change.
  (DB `prove_nvB`.)

- **F3 — Doing the ALTER in a pre-deploy while the model is unchanged lands, but drifts. (PROVEN)**
  Model left at `NVARCHAR(50)` + the same pre-deploy → **landed**, column 10 (DacFx generated no
  Code change, so no guard). But re-publishing (model still says 50) **reverted the column to 50** —
  the model and the database disagree, and the next deploy widens it back. (DB `prove_nvC`.)

- **F4 — The working pattern is two releases. (PROVEN)** On one database:
  `50 →` **Release 1** (model unchanged `NVARCHAR(50)` + pre-deploy shortens and narrows) `→ 10 →`
  **Release 2** (model `→ NVARCHAR(10)`, no pre-deploy, database already 10, so DacFx generates
  nothing) `→ 10 →` re-publish `→ 10` (stable). (DB `prove_2rel`.) Release 1 changes the database
  physically while the model lags so no data-loss step is generated; Release 2 lets the model catch
  up as a no-op. **The model change and the pre-deploy ALTER must never share a release (F2).**

- **F5 — [OVERTURNED 2026-08-21] A declarative foreign-key add ends TRUSTED automatically; there is no manual trust step. (PROVEN)**
  The earlier reading — a pre-deploy leaves the key `is_not_trusted = 1`, requiring a hand-written
  post-deploy `WITH CHECK CHECK` — was a measurement artifact. Script capture on this branch shows
  DacFx generates the **same two statements for every declarative FK add**, whether or not a
  pre-deploy script is present: `ALTER TABLE [dbo].[Order] WITH NOCHECK ADD CONSTRAINT …`, then,
  later in the same publish, `ALTER TABLE [dbo].[Order] WITH CHECK CHECK CONSTRAINT …`. The key ends
  `is_not_trusted = 0`. Proven: clean add, no pre-deploy (DB `db_fkc2`); orphan reconciled in a
  pre-deploy, no manual re-trust (DB `db_orphB`) → trusted; a redundant manual `WITH CHECK CHECK` on
  top (DB `db_orphC`) → identical. An untrusted key (`is_not_trusted = 1`) comes only from a
  hand-written `WITH NOCHECK` add that skips the re-validation — the anti-pattern, not the
  declarative path. See F9.

- **F6 — A pre-deploy side effect is not rolled back when the main script fails. (PROVEN, from F2)**
  In F2 the pre-deploy's committed ALTER survived the deploy's failure. **Every pre-deploy step
  must be idempotent and safe to re-run over a partial state** — a failed deploy can leave its
  pre-deploy work behind.

- **F7 — make-mandatory (populated `NULL → NOT NULL`) behaves exactly as narrow. (PROVEN)** Model
  `Email → NOT NULL`, no pre-deploy → refused, `Msg 50000`, the column stayed nullable. The
  two-release landed and held: R1 (model still `NULL` + a pre-deploy that backfills the NULLs and
  runs `ALTER … NOT NULL`) → column `NOT NULL`; R2 (model `→ NOT NULL`, no pre-deploy) → no-op;
  re-publish → stable. (DBs `mm_ax`, `mm_2r`.) The tightening class — narrow and make-mandatory —
  is one pattern.

- **F8 — [mechanism corrected 2026-08-21] a clean foreign key lands trusted in one release. (PROVEN)**
  Adding `FK_Order_Status` (`Order.StatusId → Status.Id`) with every child row already valid and **no
  pre-deploy** → published clean, `is_not_trusted = 0` (DBs `pb_fkc`, `db_fkc2`). The mechanism is
  **not** `WITH CHECK ADD` (the earlier wording); the generated script is `WITH NOCHECK ADD` +
  `WITH CHECK CHECK`, the same two statements DacFx emits for every declarative FK add (F9). The
  outcome — one release, trusted, no manual step — stands. `create-fk-orphan` differs only by the
  fork the orphan forces (reconcile it, or the publish blocks `Msg 547`), not by a trust step. A
  CHECK on clean data with no pre-deploy is trusted the same way. (DB `pb_chk`.)

- **F9 — The four key-operation script shapes, script-captured. (PROVEN 2026-08-21)** Each op run as
  the ONLY change on an isolated database, no unrelated pre-deploy:
  - **create-fk-clean** (DB `db_fkc2`): `ALTER TABLE [dbo].[Order] WITH NOCHECK ADD CONSTRAINT
    [FK_Order_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status] ([Id]);` then
    `ALTER TABLE [dbo].[Order] WITH CHECK CHECK CONSTRAINT [FK_Order_Status];` → LAND,
    `is_not_trusted = 0`.
  - **create-fk-orphan** (DBs `db_orphB`/`db_orphC`/`db_orphD`): orphan present, no reconcile →
    **BLOCK `Msg 547`** ("the ALTER TABLE statement conflicted with the FOREIGN KEY constraint …");
    the failed publish leaves the key present-but-untrusted (half-applied, like F2/F6). Orphan
    reconciled in a pre-deploy and the seed fixed → LAND, `is_not_trusted = 0`, no manual trust step;
    a manual post-deploy `WITH CHECK CHECK` is redundant.
  - **change-delete-rule** (DB `db_cdr2`): `DROP CONSTRAINT [FK_Order_Status]` then
    `WITH NOCHECK ADD CONSTRAINT … ON DELETE CASCADE` then `WITH CHECK CHECK CONSTRAINT …` → LAND,
    `delete_referential_action_desc = CASCADE`, `is_not_trusted = 0`. No row is written; the
    `WITH CHECK CHECK` re-scans the child rows. **Cascade reach (DB `db_cascbehav`):** deleting one
    Status removed its 2 child Orders but left their 4 OrderLines orphaned — CASCADE goes one level,
    to the child whose key declares it, and does not chain to grandchildren without their own
    cascading key.
  - **drop-fk** (DB `db_dropfk`): a single `ALTER TABLE [dbo].[Order] DROP CONSTRAINT
    [FK_Order_Status];` → LAND, key gone. No data touched.

  **The law:** a declarative FK add or re-add is always `WITH NOCHECK ADD` + `WITH CHECK CHECK`. It
  ends trusted when the child data is valid and **blocks `Msg 547`** when it is not. There is no
  silent-untrusted outcome on the declarative path — that comes only from a hand-written
  `WITH NOCHECK` add with no re-validation.

- **F10 — the constraints family: add-check is create-fk's twin; add-unique is build-or-block; DEFAULTs never backfill. (PROVEN 2026-08-21)**
  - **add-check** (DBs `db_chk`, `db_chkv`): a declarative CHECK add generates the **same two
    statements as an FK** (F9) — `ALTER TABLE [dbo].[Order] WITH NOCHECK ADD CONSTRAINT
    [CK_Order_Total] CHECK (Total > 0);` then `ALTER TABLE [dbo].[Order] WITH CHECK CHECK CONSTRAINT
    [CK_Order_Total];`. Clean data → LAND, `is_not_trusted = 0` (trusts itself). One violating row
    (`Total = -5`) → **BLOCK `Msg 547`** ("the ALTER TABLE statement conflicted with the CHECK
    constraint … column 'Total'"). So add-check = create-fk: reconcile the violating rows in a
    pre-deploy or the add blocks; no manual trust step. The FK-trust law (F9) is the constraint law.
  - **add-unique** (a unique **INDEX** — the v2 emitter shape): **build-or-block, no trust state**
    (an index is always enforced once built). A duplicate → `Msg 1505` ("duplicate key value is
    (A)"); a **second NULL** → `Msg 1505` ("… (<NULL>)") — a unique index permits exactly **one**
    NULL. Clean values (including one NULL) build. "Unique among the filled values" is a **filtered
    unique index** (`WHERE col IS NOT NULL`).
  - **add-default / modify-default** (raw T-SQL): a DEFAULT fills only **new** rows and **never
    backfills** existing ones — proven: an existing `NULL` stayed `NULL`, an existing value
    unchanged, only a fresh insert got the default. No validation, no block, no data touched;
    modify-default is a DROP+ADD of the same class.

- **F11 — a foreign key does not auto-index the child column; an index is a separate act. (PROVEN 2026-08-21)**
  On a scratch DB (`ix_probe`), after `child.ParentId` gets a foreign key to `parent(Id)`, `sys.indexes`
  for `dbo.child` shows only `PK_c` (CLUSTERED on `Id`) — **nothing on `ParentId`**. A `CREATE INDEX
  IX_child_ParentId ON child(ParentId)` then adds a NONCLUSTERED index on the column. SQL Server indexes
  the **parent** side of a foreign key (its PK/unique target) but never the **child** column, so every
  FK we add leaves the join — and the parent-side delete/cascade check — scanning until the child column
  is indexed by hand. This is the strongest trigger for the `when-to-index` advisory.

- **F12 — the structural / reshape family re-proven live; earlier records were narrative, not proof. (PROVEN 2026-08-21)**
  Eight complex ops re-proven on disposable copies (warm SQL Server 2022, sqlpackage 170.4.83.3,
  `BlockOnPossibleDataLoss = True`), replacing fabricated or Twin-cited receipts with live ones. The
  DB name after each is the receipt.
  - **identity-swap** (`pg_idsw_before`): turning on IDENTITY for `Category.Id` is a **shadow-table
    rebuild** — `CREATE tmp_ms_xx_Category (Id IDENTITY(1,1))`, `SET IDENTITY_INSERT` copy preserving
    ids 1,2,3, `DROP TABLE`, `sp_rename` (logged `Starting rebuilding table [dbo].[Category]`). The
    data-loss gate **allows** it (rows moved, not dropped). `Category` has **no incoming FKs** → one
    release; the earlier "drop/recreate FKs from Order and OrderLine" was fabricated (nothing
    references Category). The post-deploy seed's explicit-id insert fails **`Msg 544`** until bracketed
    with `SET IDENTITY_INSERT` — the seed fix ships in the same change set. End: `is_identity = 1`, ids
    preserved, `IDENT_CURRENT = 3`, `Product.CategoryId` resolves.
  - **The content-hash alias law** (`pg_split` / `pg_merge` / `pg_move`): a `FOR XML RAW` content hash
    **encodes the column names** into the XML, so hashing `SELECT Id, X` against `SELECT CustomerId, X`
    reads **unequal over identical data**. Both projections must alias to the **same** names. Proven:
    aliasing to `(k, v …)` makes an identical split copy match (`51703987…`), a merge match
    (`70353E7E…`), a move match (`0DDC0E13…`). The split/merge/move verification queries were corrected
    accordingly.
  - **split-table** (`pg_split`): `ContactPhone` → a new 1:1 `CustomerContact`; additive publish clean,
    5 rows copied, hash-equal (aliased). (The old record split `Line1`/`City`/`PostalCode` off
    `Customer`, which never had those columns.)
  - **merge-tables** (`pg_merge`): `CustomerAddress` → `Customer`; cardinality `absorbed = 5 ==
    distinct_parents = 5` (1:1); a 2nd address for one Customer → `6 != 5`, the 1:many refusal.
  - **move-attribute** (`pg_move`): `Customer.Region` → `Account` across the `AccountId` join; 1:1 holds
    (**excluding NULLs** — else the 2 unmapped rows false-positive as 1:many), hash-equal (aliased);
    **2 of 5 customers have a NULL `AccountId`** so their Region strands — a fork the old record missed.
  - **extract-to-lookup** (`pg_base` positive, `pg_move` negative): `StatusText → Status.Code` total
    mapping returns 0 unmapped; the existing `StatusId` is backfill-consistent (0 mismatches); an
    injected `StatusText = 'Backordered'` fires the non-total negative.
  - **retype-explicit** (`pg_retype2`, `pg_retype`): `Order.Total DECIMAL(18,2) → INT` refused
    (`Warning SQL72015` + `Msg 50000`), all convert with 2 losing cents (the settle fork);
    `Product.Code → INT` is `TRY_CONVERT`-NULL for all 5 (alphanumeric) — the proof refutes the premise,
    a STOP. (The old record claimed numeric Codes `100`/`200`/`400`/`500`/`30X` that do not exist.)
  - **create-static-seed / edit-seed** (`pg_seed`, `pg_base`): the **guarded** `Category` MERGE over
    unchanged rows touches **0 rows** (silent, `content_hash = -1487866545`); written **unguarded** it
    touches **3** (the churn anti-pattern). edit-seed: an inserted `Refunded` row touches 1, a re-run 0,
    a label change (`Hardware → Hardware Pro`) exactly 1 — never the table.

  **The law across the reshape family:** a `BlockOnPossibleDataLoss` block on a Phase-3 drop is
  **data-blind** (row-presence); the conservation/mapping proof licenses the **reviewer's** decision
  that the values arrived, never the gate (consistent with `skills/_index/multi-phase`, Batch 1).

---

## Part 3 — The change plan, generalized (the doctrine every op inherits)

This replaces the provisional gate-relaxation guidance in `THE_DECISION_TREE.md` Node 5 and
`THE_RECORD_FORMS.md`. It is the procedural rigor that stops the "no pre-deploy" mistake: the op
cannot be classified as shippable until this graph is walked.

**Node 5 — how it ships (the deployment graph):**

```
Does DacFx's declarative difference contain a data-loss step?
(narrow · drop column · drop table · NOT NULL on a populated table · lossy retype)
│
├─ NO  → ships in one release, declaratively. Nothing to say beyond the change itself.
│
└─ YES → Can this pipeline toggle BlockOnPossibleDataLoss for this one deploy?
         │
         ├─ YES (not this estate) → one release, gate relaxed for that publish, logged.
         │
         └─ NO  (this estate: Azure DevOps → Octopus)
                → it cannot ship as one declarative release. Use the TWO-RELEASE pattern:
                  • Release 1: a pre-deploy (or one-time) script makes the change physically,
                    with the MODEL UNCHANGED, so DacFx generates no data-loss step. Idempotent
                    and safe over a partial state (F6).
                  • Release 2: the model catches up to the new shape; DacFx sees model = database
                    and generates nothing.
                  Never combine them (F2: blocks AND half-applies). Name both releases in the PR.
```

**The record says it plainly** (per `THE_RECORD_FORMS.md`): the PR's *How it ships* names the two
releases and why they are split; *Before promoting* tells the reviewer to confirm Release 1 landed
in each environment before Release 2 goes up. No mention of relaxing a gate we cannot relax.

**Folded in (2026-08-16):** `THE_DECISION_TREE.md` now carries this as the **S5 SHIP sub-machine** —
a state machine an agent cannot deviate from (operator direction: assert the choices as a state
machine so the outcome is protected, not left to judgement). `THE_RECORD_FORMS.md`'s note points to
it. That sub-machine is the authoritative source; this section is its proof.

---

## Part 4 — The change plan, specific-target (per op)

| Op | Data-loss step? | The pattern under the locked gate | Status |
|---|---|---|---|
| **narrow** (`skills/op/narrow`) | yes — shrink | Two-release (F4). Pre-deploy shortens + `ALTER` narrower with model lagging; model catches up next release. Also offer the **CHECK-constraint alternative** below. | **PROVEN** |
| **make-mandatory**, populated (`skills/op/add-mandatory` / `make-mandatory`) | yes — `NOT NULL` on rows | Same class as narrow — the **identical** row-presence guard (`Modules/Customer.sql`, Twin-documented). Two-release: R1 backfill the NULLs + pre-deploy `ALTER … NOT NULL` with the model lagging; R2 the model catches up. | **PROVEN** (F7, live 2026-08-16) |
| **delete-attribute** (`skills/op/delete-attribute`) | yes — drop column | **Two-release.** R1 = pre-deploy `DROP CONSTRAINT` + `DROP COLUMN` with the model still declaring the column (DacFx emits no drop; the row-presence guard never fires) + the corrected seed; R2 = the model drops the column, no-op. Re-publishing R1 **re-adds** the column with its `DEFAULT`, so R1 is a single publish and R2 follows at once. Receipt: `sample-prs/delete-attribute.md` (`Msg 50000` block; `Msg 207` seed trap). | **PROVEN** (2026-08-21) |
| **delete-entity** (`skills/op/delete-entity`) | yes — drop table | **One release, scripted** — *different from the column drop.* `DropObjectsNotInSource = false` makes removing the `.sql` a phantom (does nothing), so the `.sql` is removed **and** an idempotent pre-deploy `DROP TABLE` runs in the **same** release (raw T-SQL the data-loss gate does not govern). The two-release trick does **not** transfer — a model still holding the table re-creates it empty on the next publish. Receipt: `sample-prs/delete-entity.md` (`SQL72015`/`Msg 50000`; phantom `object_id` unchanged; re-create trap). | **PROVEN** (2026-08-21) |
| **retype-explicit**, lossy (`skills/op/retype-explicit`) | yes — lossy cast | Multi-phase, composing legs proven above: the additive new column (add-optional), then `TRY_CONVERT` and settle the non-converting rows, then the drop-old-column two-release (delete-attribute). | **PROVEN** (F12, 2026-08-21): the naive single-step lossy cast is refused — `Warning SQL72015` + `Msg 50000` on `pg_retype` / `pg_retype2`; each composed leg is proven in its own row. |
| **create-fk-orphan** (`skills/op/create-fk-orphan`) | no (the reconcile is manual pre-deploy DML) | Ships in one release. Reconcile the orphan in a pre-deploy, or the publish blocks `Msg 547` (F9). The declarative add ends trusted automatically — no manual trust step (F5 overturned, F9). | **PROVEN** |
| widen · add-optional · add-index · create-entity · add-default · edit-seed · … | no | Unaffected by the axiom — one declarative release. | n/a |

**The narrow op, concretely (ready to apply):** its *How it ships* becomes —
> Ships as **two releases**, because this pipeline cannot relax the data-loss guard:
> - **Release 1** — a one-time pre-deploy script shortens the over-length values and runs
>   `ALTER TABLE dbo.Product ALTER COLUMN Code NVARCHAR(10) NOT NULL`. The model still declares
>   `NVARCHAR(50)`, so DacFx generates no narrowing step and the guard never fires. The script is
>   idempotent (safe to re-run, and safe if a later step in the deploy fails).
> - **Release 2** — the model declares `NVARCHAR(10)`. The database is already 10, so DacFx
>   generates nothing. This resolves the temporary gap between the model and the database.
>
> *Before promoting:* confirm Release 1 has landed in an environment (the column is already 10)
> before sending Release 2 up to it.

---

## Part 5 — Decisions and what is still open

- **The rigor mechanism — RESOLVED (operator, 2026-08-16): a state machine.** The deployment graph
  is expressed as the **S5 SHIP sub-machine** in `THE_DECISION_TREE.md`, which an agent follows
  firmly — it asserts our choices as states and guards so the outcome is protected, not left to
  judgement. A per-op template carries the shape the machine decides; the machine, not the author,
  chooses it.
- **The CHECK-constraint alternative — DEFERRED (operator, 2026-08-16).** A `CHECK (LEN(Code) <= 10)`
  that keeps the column wide would sidestep the two-release, but it clutters the schema, and the
  schema is kept clean. Parked, not built. Revisit only if a concrete case needs a max-length rule
  without the physical narrowing and the clutter is judged worth it then.
- **Drops (delete-attribute, delete-entity) — PROVEN (2026-08-21); two distinct patterns.** The
  earlier "route these to REFUSED; do not write drop guidance until then" note is **retired** — the
  patterns are proven and recorded. A **column** drop ships **two-release** (R1 pre-deploy
  `DROP CONSTRAINT` + `DROP COLUMN` with the model lagging so DacFx emits no guarded drop; R2 the
  model catches up — `sample-prs/delete-attribute.md`). A **table** drop ships **one-release,
  scripted** (remove the `.sql` **and** run an idempotent pre-deploy `DROP TABLE` in the same
  release, because `DropObjectsNotInSource = false` makes the file-removal a phantom —
  `sample-prs/delete-entity.md`). The two-release trick does **not** transfer to a table drop (a
  model still holding it re-creates it empty). SHIP's REFUSED terminal is for a genuinely
  un-shippable request, not for drops; Part 4 carries the shapes and receipts.
- **RESOLVED (2026-08-22) — the locked-gate law is now propagated to the self-test and review layers.**
  Batch 1 propagated it across the authoring composed layer; a follow-up pass then re-scoped the two
  downstream layers the earlier note flagged:
  - **`self-test/*` (acceptance machinery).** `rubric.md`, `prompts.md`, `review-prompts.md`,
    `review-rubric.md`, `PROTOCOL.md`, and `golden/make-mandatory-*` now encode the **two-release** as
    the expected answer for a populated make-mandatory and narrow. The golden make-mandatory PR +
    conversation were **re-proved live** on the two-release (DBs `pg_mm` / `pg_mm_naive`) and rewritten:
    naive single-release blocks `Msg 50000`; R1 (model lagging + pre-deploy backfill+ALTER) lands
    (`is_nullable=0`, digest `1818783869`); the seed must ride with the change (`Msg 515`); R2 is a
    clean no-op. A populated **narrow-that-fits** was proven to still block on row-presence
    (`NVARCHAR(50)→NVARCHAR(16)`, max len = 16, all fit → `Msg 50000`), so the rubric's "fits → in
    place" was corrected to two-release (only an EMPTY table is in-place).
  - **`skills/review/*` (reviewer persona) + `agents/`.** A populated make-mandatory no longer
    escalates: a mis-authored clean-single-release claim is **Returned to the author** (the shape is a
    determined two-release), and a correctly-shaped one is **Approved with a named risk**. The
    escalation examples in `verdict`, `adversary`, the `reviewer` agent, and the `change-author` agent
    were replaced with a genuine design fork (a 1:many merge — which rows survive).
- **RESOLVED (2026-08-22) — the `SamplePr*` F# facts are a valid parallel corpus, not stale.** The
  earlier note assumed the Twin F# facts encoded the records' pre-re-scope scenarios and so lagged them.
  On inspection that was wrong: `tests/Twin.Tests.Integration/SamplePr*Tests.fs` is a **self-contained
  parallel proof corpus on the Twin's own richer synthetic estate** (~25 rows, DacFx 162.5.57), and its
  scenarios were **never** the record fabrications — the Twin split-table splits `Email` (not phantom
  address columns), identity-swap **removes** IDENTITY from `Order` with a real `OrderLine` FK (not the
  record's fabricated Order→Category FK), and retype-explicit is `INT→TINYINT` (`Msg 220` overflow). It
  **builds clean** and **runs green** — the full suite was re-run this session, **11 classes, 41/41
  facts, 0 skipped, 0 failed** (matching the corpus's stated size). Its "relaxed" publishes are the
  harness's dev-materialization posture
  and an adversarial *SQL-Server-itself-refuses* probe, not the retired shipping fork. So **no rewrite is
  warranted**: the two corpora prove the same laws on different substrates — each record's authoritative
  proof is its `sqlpackage` receipt, and the Twin suite is parallel. `sample-prs/README` now states that
  relationship; where the engines diverge on constraint trust, the live `sqlpackage` engine is
  authoritative (the README's engine-pair note).
- **Infra — a stable SQL target is needed for proving.** F1–F5 were proven on a SQL Server 2022
  container this session, but the Docker daemon degraded twice and cut the make-mandatory re-proof
  short. `sqlpackage` is now on the box (installed this session); the missing half is a **stable
  server** to publish against. Provision both in the environment setup so PROVE (the state
  machine's un-skippable guard) can always run — otherwise agents are pushed back toward guessing,
  which the whole machine exists to prevent.
- **Simple by default — configuration is one-time and central, not per-op (operator, 2026-08-21).**
  The developer experience on every operation must be simple: reconcile the data if it is dirty,
  publish, done. Deploy-engine behaviour that **can** be configured — constraint validation and trust
  is the clearest case — is pinned **once** in the pipeline's DacFx publish profile so a declarative
  add validates and ends trusted (`is_not_trusted = 0`) on its own. No per-op record tells a developer
  to add a manual trust step; the after-deploy `is_not_trusted = 0` check is the safety net, and the
  profile is owned centrally. This is distinct from the data-loss gate (Part 1), which is **locked on**
  and cannot be configured away — that one genuinely forces the two-release pattern, so it *is* surfaced
  to developers. The rule: **configure away what you can; surface only what is inherent.**
  - **Open — pin the profile to the estate's DacFx version.** Auto-trust was proven the default on
    `sqlpackage 170.4.83.3` (F9); the original Twin proof ran `DacFx 162.5.57` and read as untrusted;
    the estate pipeline sets DacFx options. So the exact publish-profile settings that guarantee
    auto-trust must be pinned against the estate's **actual** DacFx version and verified once
    (`is_not_trusted = 0` on a real publish). Until pinned, the records assume the configured
    (auto-trust) state and rely on the after-deploy check to catch a profile that does not.

---

## Part 6 — The keys-family convergence (2026-08-21): what we have arrived at

This part is the baseline: the settled truths, the register rule, the per-op terminals, and the shape
a converged skill has — captured whole so a future agent inherits the standard, not a pile of edits.
The keys family (five ops) was hand-treated to this standard; the remaining families follow it.

### 6.1 — The register rule (from the diction file)

"the data decides" is retired tree-wide. It personifies an abstraction and points at nothing a
reviewer can check — it fails `THE_RECORD_FORMS.md`'s test ("point at a referent, or cut it"). The
denotative forms replace it, and both already exist in the tree:

- **"prove before you classify"** — the imperative; the provisional-default gloss in every op skill.
- **"the existing rows determine how it ships"** — names the referent (the rows), for in-body use.

Recorded as a worked example under the one-test section of `THE_RECORD_FORMS.md`. This governs the
skills too, not only the PR record: a skill body carries trap-names and a teaching stance (licensed),
but no flourish and no invented counts.

### 6.2 — The FK-trust law (settled, script-captured; F9, overturning F5, correcting F8)

> A declarative foreign-key add or re-add is **always** `WITH NOCHECK ADD` + `WITH CHECK CHECK`. It
> ends **trusted** (`is_not_trusted = 0`) when the child data is valid, and **blocks `Msg 547`** when
> it is not. There is no silent-untrusted middle state on the declarative path. An untrusted key
> (`is_not_trusted = 1`) comes **only** from a hand-written `WITH NOCHECK` add that skips the
> re-validation — the anti-pattern.

Consequences that corrected earlier surfaces:
- **No manual trust step.** A reconciled orphan FK ends trusted on its own — DacFx emits the
  `WITH CHECK CHECK`. F5's "pre-deploy ⇒ untrusted, add a post-deploy `WITH CHECK CHECK`" was a
  measurement artifact; the manual step is redundant (proven `db_orphB` vs `db_orphC`).
- **The block is the constraint working.** An unreconciled orphan blocks `Msg 547` (`db_orphD`); a
  *blocked* publish is non-atomic and can leave the key half-applied and untrusted — re-probe after a
  block (`prove-on-dacpac`).

### 6.3 — The five keys terminals (each proven this branch, 2026-08-21)

| Op | SHIP terminal | Proven receipt |
|---|---|---|
| **create-fk-clean** | ONE RELEASE, trusted | `db_fkc2`: `WITH NOCHECK ADD` + `WITH CHECK CHECK` → LAND, `is_not_trusted = 0` |
| **create-fk-orphan** | ONE RELEASE (pre-deploy reconcile), trusted | `db_orphD` blocks `Msg 547` unreconciled; `db_orphB` reconciled → LAND, trusted, no manual step |
| **change-delete-rule** | ONE RELEASE, in place, trusted | `db_cdr2`: `DROP` + `WITH NOCHECK ADD … ON DELETE CASCADE` + `WITH CHECK CHECK` → LAND, `CASCADE`, trusted |
| **drop-fk** | ONE RELEASE, in place, clean | `db_dropfk`: single `DROP CONSTRAINT` → LAND, key gone, no data touched |
| **define-pk** | ONE RELEASE | dup → `Msg 1505` + `1750`; nullable → `Msg 8111` + `1750`; clean composite builds |

**Cascade reach (F9, `db_cascbehav`):** deleting one parent removed its direct children (2 Orders) and
left their grandchildren (4 OrderLines) orphaned — `ON DELETE CASCADE` goes exactly one level, to the
child whose key declares it, and does not chain without each level's own cascading key.

### 6.4 — Surfaces corrected to the new law

The FK-trust correction was propagated across the declarative-path surfaces: `skills/author-pr` (the
worked example), `skills/_index/constraint-is-a-claim` (the reconcile-first pattern),
`skills/prove-on-dacpac` (the blocked-publish fix), `skills/operations/keys-and-refs` (the TOC), the
five keys `skills/op/*`, the five `sample-prs/*`, `THE_DECISION_TREE.md` (the FK-orphan flex row,
2026-08-21), and `proving-ground/Modules/Order.sql` (the header, 2026-08-21). The declarative path
needs **no** manual trust step.

One post-deploy `WITH CHECK CHECK` legitimately remains — as **recovery, not the happy path**: a
blocked non-atomic publish can leave a key present-but-untrusted (`is_not_trusted = 1`), and
re-trusting it (or `toggle-trust`'s re-trust of a legacy `WITH NOCHECK` constraint) is the fix for
that specific partial state (`skills/prove-on-dacpac`, `skills/op/toggle-trust`). That is distinct
from the overturned F5 claim that the *declarative add itself* required a manual trust step — it
does not.

**Constraints family — DONE (2026-08-21, F10).** `add-check` proved create-fk's twin by script
capture (`db_chk` clean → trusts itself; `db_chkv` violating → `Msg 547`); `add-unique` proved
build-or-block (`db_uq` plain blocks on the second NULL `Msg 1505`; `db_uqf` filtered → builds,
`has_filter = 1`); DEFAULTs proved no-backfill. Surfaces brought to the v2 bar and the settled law:
the two `sample-prs/{add-check,add-unique}.md` (gold form, fresh proof), the five constraint
`skills/op/{add-check,add-unique,add-default,modify-default,toggle-trust}`, `constraint-is-a-claim`
(the CHECK-is-the-same note), and `skills/operations/constraints.md` (the TOC). `toggle-trust` reframed:
under F9/F10 a fresh declarative add auto-trusts, so it is for a legacy/bulk-load untrusted constraint,
not the create-fk-orphan remedy.

### 6.5 — The converged skill form (the high-water mark to replicate)

`skills/op/widen` and the keys five are the exemplars. A converged op skill has, in order:

1. **Frontmatter** — `name` + a `description` that is the routing trigger (OutSystems phrasing → the op).
2. **`> Default (provisional — prove before you classify).`** — the provisional call, risk paired with
   its reason (never a bare "a dev lead must review").
3. **`> SHIP terminal: <X>.`** — the terminal from `THE_DECISION_TREE.md` S5, with the **live receipt**
   (server, DB name, generated statements, end-state) and the `FINDINGS_AND_CHANGES.md` finding.
4. **`> Proven precedent:`** — the `sample-prs/<op>.md` worked instance of the ten-section template.
5. **OutSystems phrasing · SSDT meaning · The named trap · How it flips · Prove it** — the curriculum.
6. **The verdict (to the developer)** — addresses "you", states what the copy showed, no first-person
   agent voice, no invented counts (placeholders where a real run supplies the number).
7. **The reasoning (in conversation)** — why the rows set the shape; the failure the op avoids.
8. **On the record** — the `author-pr` pointer + the fragment (Review & release / Verification /
   Rollback / Not verified), each review line naming who **and** the risk.

Every classification in the skill traces to a real publish named in §6.3 — proving is classifying.

---

## Part 7 — The release-grain findings (2026-08-28): molecules, inverses, and the current engine

Proven on this branch through the packaged loop (`scripts/prove.mjs`, sqlpackage **170.5.76**,
SQL Server 2022); the disposable databases are the receipts (`PG_mol_x1`, `PG_inv_x1`). The
worked records are `sample-prs/compound/` and the four `sample-prs/drop-*.md`. The doctrine
these findings ground: **the unit of proof is the release delta** (`skills/prove-on-dacpac`,
`skills/decompose`).

- **F13 — A release is vetoed by its strictest atom, and the veto is atomic. (PROVEN)** One
  publish carrying an identity-preserving rename (`sp_rename`, refactorlog entry present) AND a
  populated tightening → refused (`Msg 50000`), and the copy afterward shows the rename did NOT
  land: the whole delta rolled back under `IncludeTransactionalScripts = true`, the innocent
  atom with it. (DB `PG_mol_x1`.) Reshape-coupled atoms serialize; the combined release is not a
  shortcut.

- **F14 — An all-additive batch inherits no guard and ships as one publish. (PROVEN)** Two new
  tables, their seed, three foreign-key adds (one over a populated child), and a defaulted NOT
  NULL column on a populated table published clean as ONE delta, first attempt; DacFx ordered
  the objects; the default stamped all existing rows; every key ended `is_not_trusted = 0`.
  (DB `PG_mol_x1`; `sample-prs/compound/additive-batch.md`.)

- **F15 — The post-deployment seed's claims bind every atom in the release, and undo what they
  contradict. (PROVEN, two faces)** (a) A pre-deploy repoint the seed still contradicted was
  reverted BY the seed in the same green publish — the seed is the truth surface; the pre-deploy
  only makes live rows match it. (b) After a column drop committed in pre-deploy, the seed still
  naming that column failed the publish with `Msg 207` on a half-applied release — the corrected
  seed is part of the change set (the F6 non-atomicity, now on the drop and rename faces; the
  rename face failed identically until the seed's six column references were renamed with it).
  (DB `PG_mol_x1`.)

- **F16 — A phase-bound pre-deploy block breaks the phase after it, and retiring it is the next
  phase's change-set work. (PROVEN)** The migrate-release reconcile block, left in place, read
  the column the contract release drops — `Msg 207` before anything else ran. (DB `PG_mol_x1`;
  the deploy-scripts lifecycle rule, now with its receipt.)

- **F17 — The lag-window revert, captured on a green publish. (PROVEN)** With contract-R1 landed
  (column physically dropped, model lagging), one more publish of the same release reported
  `Successfully published database` and re-created the column, every row backfilled from its
  default — the original values destroyed. Harmless in the program only because the migrate
  release had already moved the information. (DB `PG_mol_x1`.) The hold-other-publishes
  instruction (`skills/_index/tightening-class`, `skills/_index/multi-phase`) and the in-flight
  `tables` column + `scripts/inflight-check.mjs` are this finding's remedies.

- **F18 — A referenced primary-key drop refuses at the MODEL build; the engine is never
  reached. (PROVEN)** With a foreign key targeting the table, removing its PK fails
  `dotnet build` with `SQL71516` naming the referencing file — no dacpac, no delta, no publish.
  Unreferenced, the drop publishes clean as one `DROP CONSTRAINT` and the table is a heap after,
  rows intact. (DB `PG_inv_x1`; `sample-prs/drop-pk.md`.)

- **F19 — Constraint-trust re-confirmed on 170.5.76. (PROVEN)** Five declarative constraint adds
  on this branch — three FKs on empty children, one FK over a populated child, one FK over a
  populated child reconciled in the same release's pre-deploy, and one CHECK over clean data —
  all ended `is_not_trusted = 0` (F9's law, current engine; `estate/toolchain.md` carries the
  dated row). The estate pipeline's own DacFx version remains the open pin.

- **F20 — The scale lane's first light: three engine defects, and the tier where the scan
  surfaces. (PROVEN, 2026-08-28)** Standing up `proving-ground/twin.scale.json` (181k- and
  1.18M-row scenarios, floor-minted) surfaced three defects before any timing was taken, each
  fixed on this branch. (1) The unique-value synthesizer was width-blind: a 21-char token into
  `Status.Code NVARCHAR(20)` failed the bulk copy; tokens are now fitted to the declared width
  (verbatim where they fit — byte-identity kept — compact base-36 where they would truncate;
  a width too small even for the ordinal is left to the unique index to refuse by name).
  (2) The mint trust gate's Release build hit `FS3511` in two new shapes, both now in
  `CLAUDE.md` survival rule 5. (3) σ's per-row draws indexed F# *lists* — `List.tryItem i`
  per row and `pool.[j]` per FK draw, O(rows × pool) — invisible at the sample tier,
  12.7× the whole mint at 181k (75.1 s → 5.9 s once array-backed), and a wall at 1.18M
  (killed unfinished at 13 min; 28.0 s after the fix, ~42k rows/s, values byte-identical).
  The measured tiers (`estate/scale-datapoints.md`): every green Strict publish and the
  `Msg 50000` refusal stay inside single-digit tool overhead at BOTH tiers except the
  nonclustered index build, the first operation whose engine cost surfaces (~11 s over floor
  at 1.05M rows); FK and CHECK re-validation scans are still invisible there; a re-mint
  through live CK+FK+IX costs +8.6 s at 1.18M and ends trusted. The added-scrutiny window's
  teeth begin at the index-shaped operations, roughly linearly above 1M on this substrate.
