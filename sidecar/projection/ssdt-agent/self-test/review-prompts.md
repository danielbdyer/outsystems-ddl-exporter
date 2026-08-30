# self-test — REVIEW prompts (the reviewer's fitness suite)

The authoring suite (`prompts.md`) scores whether the **change-author** correctly classifies-by-proving.
This suite scores whether the **reviewer** (Persona 2 — `agents/reviewer.md`) correctly **audits** an
already-authored change. The unit under test is a *review packet* + its planted correctness, not a raw
developer prompt: each scenario hands the reviewer a **change-author review packet** — the pull request
the author produced per `skills/author-pr`, carried with its proof (the packet contract is in
`agents/change-author.md` → *Handoff — the review packet*). SOME packets are honestly proven and SOME
carry a **planted defect** — a claim the author made that does not survive reproduction. The reviewer
PASSES a scenario by returning the **correct disposition** and, above all, by **reproducing** the
author's proof on its own isolated DB rather than trusting the packet.

> **The discipline this suite enforces.** A reviewer that reads the packet and agrees is worthless — it
> has added nothing the author didn't already claim. The whole value is `reproduce-not-read`: re-run the
> claimed Strict outcome — a blocked publish or a clean one — on a FRESH `PG_<id>_<rand>` DB per
> `PROTOCOL.md`, wield the adversarial moves, and let the ENGINE — not the packet — cast the deciding
> vote. A scenario is scored FIRST on "did it reproduce?"; a verdict reached by reading alone is an
> automatic FAIL however fluent, exactly as a negative authoring case that pushes the change through is
> an automatic FAIL.

## What this suite REUSES (and therefore does not restate)

- **The isolation harness** — `self-test/PROTOCOL.md` **wholesale**. Every review run picks a unique
  `PG_<testId>_<rand>` DB + a private scratch copy of the proving ground, and tears both down
  unconditionally on exit. There is no second protocol and no wrapper — the reviewer's agent runs
  the commands itself.
- **The proving ground** — the existing **enriched catalog** (`proving-ground/Modules/*.sql` +
  `Data/Seed.sql`). No new tables, no new authored seeds. Every planted defect is produced by a
  **scratch** seed edit (`$SCRATCH/Data/Seed.sql`) or a scratch `.sql` / `.refactorlog` edit, exactly as
  the authoring negatives are — the authored positive tree stays clean.
- **The publish loop + the two named moves** — `skills/prove-on-dacpac`. The reviewer **re-runs** that
  loop to reproduce, and **wields** its two moves (consequence check, violating-row probe) adversarially.
  It invents no third move and re-scaffolds no `sqlpackage`/`sqlcmd` command.
- **The fitness lens** — `self-test/rubric.md`. The reviewer GRADES the author's change by the same six
  criteria + seven metrics the author is scored on; `review-rubric.md` adds only the reviewer-specific
  dimensions (reproduced-not-read, verdict-level-correct, escalation-discipline, terse-peer-voice).
- **The scenario data** — the authoring cases these mirror (COL-03/03C, COL-08/08N, KEY-02/03, COL-06,
  TBL-02N, COL-09, COL-01). A review scenario is *an authored answer to one of those*, with the answer
  either honest or defective. See the cross-reference column below.

## What this suite deliberately does NOT build

- **No new proving-ground tables or seeds** — the scenarios map onto Customer / Order / Product /
  ProductLegacy exactly as the authoring suite does; negatives come from scratch seed edits.
- **No second isolation protocol** — `PROTOCOL.md` is reused verbatim (unique DB + scratch copy +
  unconditional teardown).
- **No re-scaffolded publish loop** — the reviewer re-runs `prove-on-dacpac`'s existing Strict/Permissive
  commands; they are not restated here.
- **No re-explanation of any guard/trap** — every WHY points to its `_index` owner (tightening-class,
  identity-and-refactorlog, constraint-is-a-claim, multi-phase, idempotent-seed).

## The four dispositions (what each scenario expects)

Owned by `skills/review/verdict/SKILL.md`; stated here only so the *expected* column is legible:

- **Approved** — every proof obligation discharged on the reviewer's own DB → straight to the deploy
  gate, zero lead time.
- **Approved with a named risk** — reproduces fine, but an un-scoped consequence (an out-of-band ETL
  consumer, a claim the proving ground structurally cannot prove) must be logged and accepted → one-line
  lead accept/override.
- **Returned to the author** — the defect is real but **fixable by the OS-dev without the lead**; routes
  to Persona 1 (the change-author re-renders the terse finding as a teaching fix). The lead never sees it.
- **Escalated — one question for the lead** — a genuine **design fork / irreversible-step judgment**;
  reaches the human lead with the dependency map + the single specific question, homework done.

## How to run a review scenario

1. Pick **one** `REV-*` id below. Read it: the packet the author produced, and the planted correctness.
2. Follow `PROTOCOL.md` exactly — copy the proving ground to a private scratch dir, resolve a unique
   `(TESTID, DB, SCRATCH)` ONCE, build the dacpac in the scratch copy, establish the BEFORE seed named
   by the scenario (default / re-seeded / orphan / over-length).
3. Drive the packet through `agents/reviewer.md` → `skills/review/review-change` (the conductor), which
   **reproduces** the author's claimed outcome on its own DB, then dispatches
   `skills/review/dependency-scope` → `skills/review/adversary` → `skills/review/verdict`, in that order.
4. Score with `review-rubric.md`. Tear down (drop the DB, delete the scratch) on exit — unconditionally.

> Handbook citations use the on-disk filename with the **+3 offset** (file 13 = §16, 14 = §17, 15 = §18,
> 16 = §19).

---

## The legend (every field, every review scenario)

- **id** — `REV-NN`; the review analogue of an authoring id.
- **the packet** — what the change-author handed over (the authored `.sql` edit, the claimed shipping
  shape and review need, the claimed proof — a blocked or clean publish + row counts, which of the two
  moves it claims to have run, the named trap if any). This is what the reviewer AUDITS.
- **mirrors** — the authoring case (`prompts.md` id) this scenario is an authored answer to.
- **op / _index** — the per-op skill the author opened + the governing concern; the reviewer POINTS here,
  never re-derives.
- **planted** — `honest` (the packet's claim is true and reproduces) or the specific **defect** injected
  into the packet's claim (produced via a scratch seed/`.sql` edit).
- **seed** — the scratch proving-ground state the reviewer establishes to reproduce (per PROTOCOL step 5).
- **expected verdict** — the correct disposition, and its routing.
- **reproduce obligation** — the specific claim the reviewer must re-run on its OWN DB (the engine casts
  the vote). Reading the packet without discharging this = automatic FAIL.
- **wield** — which adversarial move fits this op class (or the honest ABSENCE, when none can fire).
- **fail mode** — the wrong review: trusted the packet, approved the defect, manufactured a block that
  cannot fire, or escalated a return-to-author fix to the lead.

The **op** and **_index** columns are load-bearing exactly as in the authoring suite: the reviewer's
surfaced WHY must come from the named `_index` owner, specialized — not re-explained in the review layer.

---

## REV-01 — clean rename, correctly authored · honest · **the clean approval**

> **Packet:** the author renamed `Customer.ContactPhone` → `MobileNumber`, authored the `.refactorlog`
> entry, and claims it ships in place as one `sp_rename`, reviewable by a dev lead or an experienced
> developer because the running application must change to use the new name. Proof = "Strict clean on a
> copy; delta is one `EXEC sp_rename 'dbo.Customer.ContactPhone','MobileNumber','COLUMN'`; refactorlog
> present; 5 rows preserved." No trap; no move claimed (clean).

- **mirrors:** `COL-08` (rename-attribute, refactorlog present)
- **op:** `skills/op/rename-attribute/SKILL.md` · **_index:** `skills/_index/identity-and-refactorlog/SKILL.md`
- **planted:** `honest` — nothing wrong. The author did it right.
- **seed:** Customer DEFAULT (5 rows, ContactPhone populated); the `.refactorlog` **PRESENT** in scratch.
- **expected verdict:** **Approved** → deploy gate, zero lead time.
- **reproduce obligation:** on the reviewer's own `PG_REV_01_<rand>` DB, rebuild the scratch dacpac,
  `/Action:Script` the delta, and CONFIRM it is `EXEC sp_rename ... 'COLUMN'` (NOT `DROP COLUMN`+`ADD`),
  publish Strict CLEAN, and verify the 5 rows survive (row count + the content-hash check shows the
  rename-shaped change, not a wipe). Approving from the packet's word alone — without re-scripting the
  delta on its own DB — is the auto-fail even though the disposition happens to be right.
- **wield:** none — a clean rename has nothing for SSDT to block on, so there is no data-loss block to
  inject; naming that absence is the honest result (`prove-on-dacpac` scope discipline). Do NOT
  manufacture a block.
- **fail mode:** reviewer "reads the packet, agrees, Approved" without reproducing (the read-not-reproduce
  failure); OR reflexively distrusts a correct change and returns it with no reproduced defect (a false
  return-to-author wastes the OS-dev's time and erodes the peer-compact).

---

## REV-02 — a rename with no refactorlog entry, mislabeled clean · **defect** · catch-and-return

> **Packet:** the author renamed `Customer.ContactPhone` → `MobileNumber` and claims it ships in place as
> one `sp_rename`, clean, the approval weighing only the app-side caller change — BUT the `.refactorlog`
> entry is **missing** from what they authored. The packet asserts "rename done, sp_rename" from reading
> the `.sql`, with no reproduced delta.

- **mirrors:** `COL-08N` (rename-attribute, no refactorlog entry)
- **op:** `skills/op/rename-attribute/SKILL.md` · **_index:** `skills/_index/identity-and-refactorlog/SKILL.md`
- **planted:** **a rename with no refactorlog entry, mislabeled clean** — the refactorlog entry is omitted
  in the scratch edit, so the author's "sp_rename, in place" claim is false; SSDT will emit `DROP
  COLUMN`+`ADD`.
- **seed:** Customer DEFAULT (5 rows, ContactPhone populated); the `.refactorlog` entry **MISSING** in
  scratch.
- **expected verdict:** **Returned to the author** → routes to Persona 1. Fixable by the OS-dev without
  the lead: add the refactorlog entry, re-prove; the lead never sees it.
- **reproduce obligation:** on `PG_REV_02_<rand>`, script the delta and SEE `DROP COLUMN [ContactPhone]`
  + `ADD [MobileNumber]` (data loss) — the author's `sp_rename` claim does NOT reproduce. Name the trap
  from `_index/identity-and-refactorlog`: a rename with no refactorlog entry loses the column's data,
  because identity is separate from name — without the refactorlog SSDT sees one column vanish and
  another appear. A claim that fails to reproduce is, by the conductor's rule, an automatic
  return-to-author or escalation — here a return to the author, because the fix is mechanical.
- **wield:** none needed to catch it (the delta read is sufficient); do not inject a data-loss block —
  the data loss is a `DROP`+`ADD` in the delta, not a blocked constraint.
- **fail mode:** reviewer trusts the packet's "sp_rename" and approves the drop+create — the exact
  auto-fail the authoring COL-08N tests, now committed by the *reviewer*; OR it escalates this to the
  lead (a return-to-author fix does not reach the human — that breaks escalation-discipline).

---

## REV-03 — make-mandatory claimed clean on a populated table · **defect** · **THE GATING SCENARIO** · catch-and-return

> **Packet:** the author was asked to make `Customer.Email` required. They report: "populated table, 2
> NULLs; a pre-deploy backfill clears them to 0, so it ships clean as one release — backfill, then NOT
> NULL lands under Strict; a dev lead reviews since existing data is modified." Proof claimed: "backfill
> clears NULLs → Strict clean." **No Strict re-run after the backfill is shown** (the packet asserts the
> clean outcome from the old recipe).

- **mirrors:** `COL-03` / `COL-03C` (make-mandatory, the core proof — corrected finding)
- **op:** `skills/op/make-mandatory/SKILL.md` · **_index:** `skills/_index/tightening-class/SKILL.md`
- **planted:** **clean-on-populated claim** — the author parroted the stale "backfill → clean" recipe and
  never reproduced the Strict re-run that would have shown SSDT STILL blocks the publish. This is the
  showcase authoring failure; the reviewer's job is to catch it by **reproduction**.
- **seed:** Customer DEFAULT (rows 3 & 5 have `Email` NULL); the reviewer ALSO re-seeds a zero-NULL
  scratch variant (`COL-03C` shape) to prove the guard is table-has-rows even at 0 NULLs.
- **expected verdict:** **Returned to the author** → the author's clean-single-release claim is the
  disproven recipe. The corrected shape is the **two-release** (R1 backfills and tightens with the model
  lagging, R2 the model catches up); this pipeline cannot relax the gate, so the shape is determined, not
  a design fork — it returns to the OS-dev, not the lead. A reviewer that Escalates this (spending the
  escalation on a settled shape) misses.
- **reproduce obligation** (the core obligation — a reviewer that skips this FAILS the whole suite): on
  `PG_REV_03_<rand>`, (a) author the backfill, run the NULL probe → prove **0** NULLs remain, THEN (b)
  re-run Strict and prove it **STILL blocks** the publish and leaves the column nullable — read the
  generated guard and SEE `IF EXISTS (SELECT TOP 1 1 FROM [dbo].[Customer]) RAISERROR(…,16,127)` placed
  **before** the `ALTER COLUMN` (table-has-rows, not column-has-NULLs — the `_index/tightening-class`
  flagship). The author's "clean" claim does NOT reproduce; that is the defect.
- **wield:** violating-row probe is unnecessary here (SSDT blocks on row presence alone); the
  CONSEQUENCE-shape proof is the zero-NULL-still-blocks reproduction itself.
- **the finding** the return carries: *"Populated table, verified zero-NULL, SSDT still blocks the
  publish (table-has-rows). The 'ships clean' claim is the disproven recipe — ship it as two releases:
  R1 backfills and tightens with the model lagging, R2 the model catches up. (Added scrutiny if over
  ~1M rows.)"* — reproduced, one fix named.
- **fail mode:** reviewer **accepts the clean claim without reproducing** — the single biggest failure
  the suite exists to catch; it means the reviewer classified from the packet text exactly as a failing
  author classifies from the `.sql`. Automatic full-suite FAIL, however fluent the write-up.

---

## REV-04 — add-FK that skipped the orphan check · **defect** · catch-and-return

> **Packet:** the author added an FK `Order.CustomerId → Customer.Id` and claims it ships in place as one
> `ADD CONSTRAINT`, clean, reviewable by a dev lead because a cross-table relationship is added — proof:
> "clean FK, publishes clean." The packet does **not** show the orphan probe (`LEFT JOIN Customer WHERE
> Customer.Id IS NULL`) ever running; the author picked the `create-fk-clean` slug and asserted zero
> orphans without proving it.

- **mirrors:** `KEY-03` / `KEY-03N` (create-fk, orphan present) vs `KEY-02` (clean)
- **op:** `skills/op/create-fk-orphan/SKILL.md` · **_index:** `skills/_index/constraint-is-a-claim/SKILL.md`
- **planted:** **skipped-orphan-check** — the author claimed clean but the default Order seed has the
  orphan `CustomerId=999` (row 4). A constraint is a claim proven at apply time; the author never proved it.
- **seed:** Order DEFAULT (row 4 `CustomerId=999` orphan, no parent) — the authored positive already
  carries the orphan, so no scratch edit is even needed to expose the defect.
- **expected verdict:** **Returned to the author** → routes to Persona 1. Fixable without the lead: the
  trust ladder `NOCHECK → reconcile the orphan → WITH CHECK CHECK`, prove `is_not_trusted=0`.
- **reproduce obligation:** on `PG_REV_04_<rand>`, run the orphan probe FIRST → **1** orphan (Order 4),
  then reproduce the blocked Strict publish and capture the exact **Msg 547** ("conflicted with the
  FOREIGN KEY constraint") + the offending row. The author's "clean" claim does NOT reproduce. Name the
  **Forgotten FK Check** trap from `_index/constraint-is-a-claim`.
- **wield:** **violating-row probe** — the flagship fit. Even if a variant seed were clean, inject/confirm
  the orphan and publish to capture the verbatim Msg 547 + offending value the OS-dev will hit; this turns
  "the orphan check was skipped" into "here is the failure, verbatim." Then prove the remedy ladder ends
  **trusted** (`is_not_trusted=0`).
- **fail mode:** reviewer accepts `create-fk-clean` on the author's word and approves an FK that blocks
  the publish at deploy; OR stops at bare `NOCHECK` in the remedy, leaving an untrusted constraint the
  optimizer ignores (the KEY-03N fail mode); OR escalates a mechanically-fixable ladder to the lead.

---

## REV-05 — narrow claimed clean on populated data · **defect** · catch-and-return

> **Packet:** the author shortened `Product.Code` to 10 chars and claims it ships in place as one `ALTER
> COLUMN`, clean, reviewable by a dev lead because existing data is modified — "narrow, publishes clean."
> The packet shows **no `MAX(LEN)` probe**; the author classified narrow as free from the `.sql`.

- **mirrors:** `COL-06` (narrow, over-length) vs `COL-06B` (all fit)
- **op:** `skills/op/narrow/SKILL.md` · **_index:** `skills/_index/tightening-class/SKILL.md`
- **planted:** **over-length-claimed-clean** — the default Product seed has row 3 `Code =
  'STANDARD-SKU-001'` (16 chars) > the new 10, so the author's clean claim is false. A populated table
  never ships as a clean in-place change when a value exceeds the target.
- **seed:** Product DEFAULT (row 3 `Code='STANDARD-SKU-001'`, 16 chars).
- **expected verdict:** **Returned to the author** → routes to Persona 1. Fixable without the lead: the
  pre-deploy fit-check (`MAX(LEN)` + `WHERE LEN(Code)>10` count), then the **two-release** — R1 reconciles
  the over-length values and narrows in a pre-deploy with the model lagging, R2 the model catches up (this
  pipeline cannot relax the gate), never a clean in-place change.
- **reproduce obligation:** on `PG_REV_05_<rand>`, run `MAX(LEN(Code))` (=16) AND `COUNT(*) WHERE
  LEN(Code)>10` to QUANTIFY the truncation, then reproduce the blocked Strict publish (data loss) — the
  tightening-class row-presence guard (`_index/tightening-class`), the **Ambitious Narrowing** trap. The
  author's clean claim does NOT reproduce.
- **wield:** **consequence check** — after Strict blocks the publish, run Permissive + the before/after
  content-hash check to show EXACTLY that `'STANDARD-SKU-001'` chops to `'STANDARD-S'` — the truncated
  value, observed not asserted. (Also a legitimate violating-row posture: an over-length value is the
  injected violator.)
- **fail mode:** reviewer accepts "narrow is free" and approves a silent truncation; OR reports "might
  lose data" without the `MAX(LEN)` count (imprecise — banned by terse-peer voice: cite the count + the
  value).

---

## REV-07 — sparring: the LEAD's own single-PR populated drop · **not a Persona-1 defect** · sparring posture

> **Packet / ask:** this one is **not** a change-author packet — the **LEAD** proposes their OWN change:
> "single-PR `delete-attribute` on `ProductLegacy.LegacyCode`; it's dead data, drop it in one release."
> The reviewer is in **SPARRING PARTNER** mode, not backstop mode: argue the strongest case against, offer
> a counter-design, and concede fast and visibly if out-argued.

- **mirrors:** `COL-09` (delete-attribute on the populated `LegacyCode` column)
- **op:** `skills/op/delete-attribute/SKILL.md` · **_index:** `skills/_index/multi-phase/SKILL.md` (the
  4-phase deprecation) + `skills/_index/tightening-class/SKILL.md` (the populated-column block)
- **planted:** none as a defect — this tests **posture**, not the gate. Returning to the author does not
  exist in sparring mode; the reviewer surfaces the argument to the lead directly and either lands
  **Approved with a named risk** (if the lead proves the rows are dead) or an escalation-shaped hold (for
  the counter-design) — with a **visible concession** the moment the lead wins the point.
- **seed:** `ProductLegacy` module — `LegacyCode NVARCHAR(40) NOT NULL` **populated** (~40k rows for the
  argument's numbers; scale the scratch seed).
- **expected verdict:** **Approved with a named risk** *or* **Escalated**, and — decisively — a
  **visible concession** if the lead proves the rows are genuinely dead. The graded thing is the sparring
  posture + concede-visibly, NOT a gate disposition.
- **reproduce obligation:** on `PG_REV_07_<rand>`, wield the **consequence check**: Strict blocks the
  publish (`BlockOnPossibleDataLoss`, populated column — the drop-column face of `_index/tightening-class`);
  Permissive + the content-hash check SHOWS the ~40k `LegacyCode` values lost. Then name the honest edge
  from `prove-on-dacpac`: the **proving ground proves the FORWARD publish only** — it cannot prove the drop
  can be backed out. That forward-only limit is the crux of the sparring argument.
- **counter-design (offer, don't just object):** the 4-phase deprecate → verify-unused
  (`sys.dm_sql_referencing_entities` = 0) → drop-in-PR-4-behind-the-conservation-proof shape from
  `_index/multi-phase`. Concede the single-PR drop the moment the lead proves the column is unreferenced
  AND the values are provably dead.
- **fail mode:** reviewer treats the lead like a learner (teaching basics, softened causation) instead of a
  peer (direct, consequence-first); OR digs in after the lead wins the argument instead of conceding
  visibly; OR routes the lead's own change to a return-to-author (that disposition is backstop-only).

---

## REV-08 — clean change with an un-scoped external consumer · **not a defect** · **the forced Approved-with-a-named-risk**

> **Packet:** the author added an optional `ShipNote` column to `Order` and claims it ships in place,
> clean, the lightest look because the change is additive and the running application is
> unaffected — proof: additive nullable, Strict clean, nothing blocked, `Order` rows preserved. The
> change IS data-safe and reproduces clean. What the packet does NOT account for: a downstream
> **report/ETL consumer** reads `Order`'s shape out-of-band — a cross-boundary consumer the dacpac
> does not contain and the proving ground **structurally cannot prove**.

- **mirrors:** `COL-01` (add-optional) crossed with the external-consumer dependency scope
- **op:** `skills/op/add-optional/SKILL.md` ·
  **_index:** none new — the cross-boundary scope is the reviewer's `dependency-scope`; the un-provable
  edge is `prove-on-dacpac`'s *cannot-prove* list (application impact + external consumers)
- **planted:** none as a *defect* — the schema/data change is honest and reproduces clean. This scenario
  tests whether the reviewer **scopes the cross-boundary consumer and names the residual risk** instead of
  a flat approval. A flat **Approved** that hides the un-scoped consumer is the failure.
- **seed:** Order DEFAULT; the report/ETL consumer is stipulated (out-of-band, not in the catalog).
- **expected verdict:** **Approved with a named risk** — the *only* correct disposition. Approve the
  reproduced-clean schema/data change, but LOG the named residual: the external report/ETL that reads
  `Order` must be verified/refreshed out-of-band (the proving ground proved the *schema* safe and is
  **silent on the app/ETL**). One-line lead accept/override. It is neither a return to the author
  (nothing is broken or OS-dev-fixable) nor an escalation (no design fork — a clean change with an
  accepted, logged residual).
- **reproduce obligation:** on `PG_REV_08_<rand>`, reproduce the clean Strict publish (confirm the author's
  TRUE claim — additive nullable, nothing blocked, rows preserved). Then have **`dependency-scope` map the
  cross-boundary consumers**: `sys.dm_sql_referencing_entities` finds nothing in-catalog, and the
  report/ETL that reads `Order` is named as out-of-frame. Name the honest edge from `prove-on-dacpac`:
  the proving ground **cannot prove** the running app/ETL keeps working against the new shape.
- **wield:** none — a clean additive change has no data-loss block to inject; naming that absence is honest
  (scope discipline). The value is entirely **dependency-scope mapping + the named residual**, not a
  manufactured block.
- **fail mode:** reviewer flat-**approves** (misses the un-scoped external consumer → hides a real residual
  the lead should have accepted knowingly); OR over-reacts and **returns or escalates** a clean change
  (nothing to fix, no fork — that erodes the peer-compact and the decisions-only queue).

---

## REV-09 — a compound release packing reshape-coupled atoms · **defect** · catch-and-return

> **Packet:** one pull request carries two atoms on `dbo.Customer` — the `ContactPhone → MobileNumber`
> rename (refactorlog entry present) and the `Email NULL → NOT NULL` tightening — and claims one
> release: "the rename is safe, and the blanks are backfilled in a pre-deploy, so the whole thing
> ships in place." Proof = a delta listing showing the `sp_rename` and the `ALTER`, and a NULL probe
> reading 0.

- **mirrors:** `CMP-02` (the reshape-coupling molecule; `sample-prs/compound/rename-then-tighten.md`)
- **op:** `skills/decompose/SKILL.md` step 4 · **_index:** `skills/_index/tightening-class/SKILL.md`
- **planted:** `defect` — the release count is wrong at the RELEASE grain: the combined delta is
  vetoed by its strictest atom, and the veto is atomic (`FINDINGS_AND_CHANGES.md` F13).
- **seed:** Customer DEFAULT (populated; some Email blanks); the `.refactorlog` PRESENT in scratch;
  both edits applied to the scratch CREATE.
- **expected verdict:** **Returned to the author** — serialize: Release 1 the rename (with the
  seed's column references renamed in the same change set), then the tightening as its own
  two-release. Not an escalation: the shape is determined by the locked gate, not a design fork.
- **reproduce obligation:** publish the COMBINED delta once on the reviewer's own DB and capture
  both halves of F13 — the refusal (`Msg 50000`, row-presence), and the rollback (the old column
  name still present, Email still nullable). A reviewer who only re-checks the NULL probe has
  reproduced the atom and missed the molecule.
- **wield:** none to inject — the packet's own combined delta is the violating artifact; publishing
  it IS the attack.
- **fail mode:** approving because "each atom is individually fine" (atom-grain review of a
  release-grain claim); or returning only the tightening and letting the rename "ship first" without
  naming the seed's column references as part of its change set.

---

## REV-10 — an honestly-authored additive batch · honest · **the compound clean approval**

> **Packet:** one pull request stands up a Returns feature as ONE release — a `ReturnReason` lookup
> plus seed, a `Return` table with two foreign keys, and `Order.ReturnsAllowed BIT NOT NULL DEFAULT (1)`
> on the populated `Order` — and claims a single clean publish: every atom is additive, the engine
> orders the objects, the default stamps existing rows, the keys land trusted. Proof = the one
> combined Strict publish, clean, with the stamped-rows count and `is_not_trusted = 0` for every key.

- **mirrors:** `CMP-01` (the additive batch; `sample-prs/compound/additive-batch.md`)
- **op:** `skills/decompose/SKILL.md` steps 2 and 5 · **_index:** `skills/_index/constraint-is-a-claim/SKILL.md`
- **planted:** `honest` — the packing is correct and the proof is the right shape (one release-grain
  publish, `FINDINGS_AND_CHANGES.md` F14).
- **seed:** the DEFAULT estate; the scratch carries the two new table files and the Order edit.
- **expected verdict:** **Approved** → deploy gate.
- **reproduce obligation:** ONE combined publish on the reviewer's own DB — not six per-atom
  publishes — verifying the release-grain facts: clean, all existing Order rows stamped, both keys
  present and trusted. Splitting the batch to "review each atom separately" re-proves the atoms and
  misses the release; it also fails the obligation even when the disposition lands right.
- **wield:** none — an all-additive release has no block to inject; naming that absence is the
  honest result. Do NOT manufacture one.
- **fail mode:** the false return — demanding the feature be split into six pull requests "to be
  safe", which trades one coherent review for six review cycles with no added safety (the packing
  law is decompose's, and the engine confirmed it).

---

## Scenario coverage map (what each scenario proves about the reviewer)

| id | mirrors | planted | expected verdict | the reviewer skill it stresses |
|---|---|---|---|---|
| REV-01 | COL-08 | honest | **Approved** | reproduce-not-read (the clean approval; don't false-return) |
| REV-02 | COL-08N | rename with no refactorlog entry, mislabeled clean | **Returned to the author** | delta read → identity-and-refactorlog; routing to Persona 1 |
| REV-03 | COL-03/03C | clean-on-populated | **Returned to the author** | **the core obligation** — reproduce the zero-NULL-still-blocks; return the disproven recipe (the two-release shape is determined, not a fork — do not spend the escalation) |
| REV-04 | KEY-03/KEY-02 | skipped-orphan-check | **Returned to the author** | violating-row probe → Msg 547; trust-ladder-ends-trusted |
| REV-05 | COL-06/06B | over-length claimed clean | **Returned to the author** | `MAX(LEN)` fit-check + consequence check |
| REV-07 | COL-09 | sparring (posture, not gate) | Named risk / Escalated + **concede** | sparring posture + concede-visibly |
| REV-08 | COL-01 + external | honest (clean), un-scoped consumer | **Approved with a named risk** | dependency-scope maps the cross-boundary consumer; names the un-provable residual |
| REV-09 | CMP-02 | reshape-coupled atoms packed into one release | **Returned to the author** | release-grain reproduction — publish the COMBINED delta, capture the atomic veto (F13) |
| REV-10 | CMP-01 | honest additive batch, one release | **Approved** | one release-grain publish, not six atom publishes; don't false-return a correct molecule |

> The **make-mandatory review** (REV-03) is the gating scenario, exactly as its authoring twin
> (COL-03/03B/03C) gates the authoring suite: a reviewer that approves the clean claim **without
> reproducing the zero-NULL-still-blocks finding** fails the entire review suite, however well it handles
> the other six. Reproduce-or-fail is the core rule.

## Running the review suite (via PROTOCOL.md)

1. **Dispatch** one reviewer executor per `REV-*` id; each gets a fixed `(TESTID, DB, SCRATCH)` from the
   orchestrator and never re-rolls `openssl rand` mid-run.
2. **Isolation is at the DB + filesystem-copy grain** (PROTOCOL) — every executor owns a unique
   `PG_REV_NN_<rand>` DB and a private scratch copy; the authored tree is read-only; planted defects live
   in scratch seed/`.sql`/`.refactorlog` edits only.
3. **Each executor reproduces before it verdicts**, and tears down unconditionally on exit (drop-if-exists
   DB + `rm -rf` scratch) so accumulation stays at zero (survival rule 2).
4. **A batch of connection failures** is the warm container degrading, not a regression —
   `scripts/warm-sql.sh restart`, resume from PROTOCOL step 3.
5. **Score** each scenario with `review-rubric.md`; the aggregate is the reviewer's fitness.
