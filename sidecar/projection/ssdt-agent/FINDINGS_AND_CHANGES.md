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
| **delete-attribute** (`skills/op/delete-attribute`) | yes — drop column | A drop cannot pre-run with the model still holding the column (DacFx re-adds it). Needs its own proof; likely deprecate-then-drop across releases. | TO PROVE |
| **delete-entity** (`skills/op/delete-entity`) | yes — drop table | As above, for a whole table. Needs its own proof. | TO PROVE |
| **retype-explicit**, lossy (`skills/op/retype-explicit`) | yes — lossy cast | Already multi-phase; confirm each phase is gate-clean under the axiom. | TO PROVE |
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
- **Drops (delete-attribute, delete-entity) — still TO PROVE.** The two-release trick does not
  transfer directly — a drop with the model still holding the object gets re-added. SHIP routes
  these to REFUSED until their own pattern is proven. Do not write drop guidance until then.
- **Infra — a stable SQL target is needed for proving.** F1–F5 were proven on a SQL Server 2022
  container this session, but the Docker daemon degraded twice and cut the make-mandatory re-proof
  short. `sqlpackage` is now on the box (installed this session); the missing half is a **stable
  server** to publish against. Provision both in the environment setup so PROVE (the state
  machine's un-skippable guard) can always run — otherwise agents are pushed back toward guessing,
  which the whole machine exists to prevent.

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

The FK-trust correction was propagated so no surface still teaches the manual trust step:
`skills/author-pr` (the worked example), `skills/_index/constraint-is-a-claim` (the reconcile-first
pattern), `skills/prove-on-dacpac` (the blocked-publish fix), `skills/operations/keys-and-refs` (the
TOC), the five keys `skills/op/*`, and the five `sample-prs/*`. (Deferred to the constraints-family
pass, same law: `add-check`, `add-unique`, `toggle-trust`, `skills/operations/constraints.md` —
CHECK/UNIQUE validate the same `WITH NOCHECK ADD` + `WITH CHECK CHECK` way; confirm by capture there.)

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
