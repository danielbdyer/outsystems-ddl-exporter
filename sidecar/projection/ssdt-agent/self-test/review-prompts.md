# self-test — REVIEW prompts (the reviewer's fitness suite)

The authoring suite (`prompts.md`) scores whether the **change-author** correctly classifies-by-proving.
This suite scores whether the **reviewer** (Persona 2 — `agents/reviewer.md`) correctly **audits** an
already-authored change. The unit under test is a *review packet* + its planted correctness, not a raw
developer prompt: each scenario hands the reviewer a **change-author review packet** (the packet
contract is in `agents/change-author.md` → *Handoff — the review packet*), SOME of which are honestly
proven and SOME of which carry a **planted defect** — a claim the author made that does not survive
reproduction. The reviewer PASSES a scenario by returning the **right four-level verdict** and, above
all, by **reproducing** the author's proof on its own isolated DB rather than trusting the packet.

> **The spine of this suite.** A reviewer that reads the packet and agrees is worthless — it has added
> nothing the author didn't already claim. The whole value is `reproduce-not-read`: re-run the claimed
> Strict veto / clean publish on a FRESH `PG_<id>_<rand>` DB per `PROTOCOL.md`, wield the adversarial
> moves, and let the ENGINE — not the packet — cast the deciding vote. A scenario is scored FIRST on
> "did it reproduce?"; a verdict reached by reading alone is an automatic FAIL however fluent, exactly
> as a negative authoring case that pushes the change through is an automatic FAIL.

## What this suite REUSES (and therefore does not restate)

- **The isolation harness** — `self-test/PROTOCOL.md` **wholesale**. Every review run picks a unique
  `PG_<testId>_<rand>` DB + a private scratch copy of the proving ground, and tears both down
  unconditionally on exit. The CDC scenario (REV-06) **serializes** per PROTOCOL §8. There is no second
  protocol and no wrapper — the reviewer's agent runs the commands itself.
- **The proving ground** — the existing **12-table enriched catalog** (`proving-ground/Modules/*.sql` +
  `Data/Seed.sql`). No new tables, no new authored seeds. Every planted defect is produced by a
  **scratch** seed edit (`$SCRATCH/Data/Seed.sql`) or a scratch `.sql` / `.refactorlog` edit, exactly as
  the authoring negatives are — the authored positive tree stays clean.
- **The publish loop + the two named moves** — `skills/prove-on-dacpac`. The reviewer **re-runs** that
  loop to reproduce, and **wields** its two moves (CONSEQUENCE ORACLE, VETO-INJECTION LEG) adversarially.
  It invents no third move and re-scaffolds no `sqlpackage`/`sqlcmd` command.
- **The fitness lens** — `self-test/rubric.md`. The reviewer GRADES the author's change by the same six
  criteria + seven metrics the author is scored on; `review-rubric.md` adds only the reviewer-specific
  dimensions (reproduced-not-read, verdict-level-correct, escalation-discipline, terse-peer-voice).
- **The scenario data** — the authoring cases these mirror (COL-03/03C, COL-08/08N, KEY-02/03, COL-06,
  TBL-02N, COL-09, TRAP-01N). A review scenario is *an authored answer to one of those*, with the answer
  either honest or defective. See the cross-reference column below.

## What this suite deliberately does NOT build

- **No new proving-ground tables or seeds** — the scenarios map onto Customer / Order / Product /
  ProductLegacy / CdcCandidate exactly as the authoring suite does; negatives come from scratch seed edits.
- **No second isolation protocol** — `PROTOCOL.md` is reused verbatim (unique DB + scratch copy +
  unconditional teardown + CDC serialization).
- **No re-scaffolded publish loop** — the reviewer re-runs `prove-on-dacpac`'s existing Strict/Permissive
  commands; they are not restated here.
- **No re-explanation of any guard/trap** — every WHY points to its `_index` owner (tightening-class,
  identity-and-refactorlog, constraint-is-a-claim, cdc, multi-phase, idempotent-seed).

## The four-level verdict (the disposition each scenario expects)

Owned by `skills/review/verdict/SKILL.md`; stated here only so the *expected* column is legible:

- **BLESS** — every proof obligation discharged on the reviewer's own DB → straight to the deploy gate,
  zero lead time.
- **BLESS-WITH-NAMED-RISK** — reproduces fine, but an un-scoped consequence (an out-of-band ETL/view
  consumer, a claim the proving ground structurally can't prove) must be logged and accepted → one-line
  lead accept/override.
- **HAND-BACK** — the defect is real but **fixable by the OS-dev without the lead**; routes to Persona 1
  (the change-author re-renders the terse finding as a teaching fix). The lead never sees it.
- **REFUSE-ESCALATE** — a genuine **design fork / irreversible-step judgment**; reaches the human lead
  with the blast map + the single specific question, homework done.

## How to run a review scenario

1. Pick **one** `REV-*` id below. Read it: the packet the author produced, and the planted correctness.
2. Follow `PROTOCOL.md` exactly — copy the proving ground to your own scratch dir, resolve your unique
   `(TESTID, DB, SCRATCH)` ONCE, build the dacpac in your scratch copy, establish the BEFORE seed named
   by the scenario (default / re-seeded / orphan / over-length / CDC-enabled).
3. Drive the packet through `agents/reviewer.md` → `skills/review/review-change` (the conductor), which
   **reproduces** the author's claimed outcome on YOUR DB, then dispatches
   `skills/review/blast-radius` → `skills/review/adversary` → `skills/review/verdict`, in that order.
4. Score with `review-rubric.md`. Tear down (drop your DB, delete your scratch) on exit — unconditionally.

> Handbook citations use the on-disk filename with the **+3 offset** (file 13 = §16, 14 = §17, 15 = §18,
> 16 = §19).

---

## The legend (every field, every review scenario)

- **id** — `REV-NN`; the review analogue of an authoring id.
- **the packet** — what the change-author handed over (the authored `.sql` edit, the claimed
  Mechanism+Tier, the claimed proof — veto/clean + row counts, which of the two moves it claims to have
  run, the named trap if any). This is what the reviewer AUDITS.
- **mirrors** — the authoring case (`prompts.md` id) this scenario is an authored answer to.
- **op / _index** — the per-op skill the author opened + the governing concern; the reviewer POINTS here,
  never re-derives.
- **planted** — `honest` (the packet's claim is true and reproduces) or the specific **defect** injected
  into the packet's claim (produced via a scratch seed/`.sql` edit).
- **seed** — the scratch proving-ground state the reviewer establishes to reproduce (per PROTOCOL step 5).
- **expected verdict** — the correct four-level disposition, and its routing.
- **reproduce obligation** — the specific claim the reviewer must re-run on its OWN DB (the engine casts
  the vote). Reading the packet without discharging this = automatic FAIL.
- **wield** — which adversarial move fits this op class (or the honest ABSENCE, when none can fire).
- **fail mode** — the wrong review: trusted the packet, blessed the defect, manufactured a veto that
  can't fire, or escalated a HAND-BACK-able fix to the lead.

The **op** and **_index** columns are load-bearing exactly as in the authoring suite: the reviewer's
surfaced WHY must come from the named `_index` owner, specialized — not re-explained in the review layer.

---

## REV-01 — clean rename, correctly authored · honest · **the good BLESS**

> **Packet:** the author renamed `Customer.ContactPhone` → `MobileNumber`, authored the `.refactorlog`
> entry, and claims **Mechanism 1 (`sp_rename`), Tier 3**, proof = "Strict clean on a copy; delta is one
> `EXEC sp_rename 'dbo.Customer.ContactPhone','MobileNumber','COLUMN'`; refactorlog present; 5 rows
> preserved." No trap; no move claimed (clean).

- **mirrors:** `COL-08` (rename-attribute, refactorlog present)
- **op:** `skills/op/rename-attribute/SKILL.md` · **_index:** `skills/_index/identity-and-refactorlog/SKILL.md`
- **planted:** `honest` — nothing wrong. The author did it right.
- **seed:** Customer DEFAULT (5 rows, ContactPhone populated); the `.refactorlog` **PRESENT** in scratch.
- **expected verdict:** **BLESS** → deploy gate, zero lead time.
- **reproduce obligation:** on the reviewer's own `PG_REV_01_<rand>` DB, rebuild the scratch dacpac,
  `/Action:Script` the delta, and CONFIRM it is `EXEC sp_rename ... 'COLUMN'` (NOT `DROP COLUMN`+`ADD`),
  publish Strict CLEAN, and verify the 5 rows survive (row count + the content-hash oracle shows the
  rename-shaped change, not a wipe). Blessing from the packet's word alone — without re-scripting the
  delta on its own DB — is the auto-fail even though the verdict letter happens to be right.
- **wield:** none — a clean rename has no data veto to inject; naming that absence is the honest result
  (`prove-on-dacpac` scope discipline). Do NOT manufacture a veto.
- **fail mode:** reviewer "reads the packet, agrees, BLESS" without reproducing (spine fail); OR
  reflexively distrusts a correct change and hands it back with no reproduced defect (a false HAND-BACK
  wastes the OS-dev's time and erodes the peer-compact).

---

## REV-02 — naked rename mislabeled clean · **defect** · catch-and-HAND-BACK

> **Packet:** the author renamed `Customer.ContactPhone` → `MobileNumber` and claims **Mechanism 1
> (`sp_rename`), Tier 3, clean** — BUT the `.refactorlog` entry is **missing** from what they authored.
> The packet asserts "rename done, sp_rename" from reading the `.sql`, with no reproduced delta.

- **mirrors:** `COL-08N` (rename-attribute, naked)
- **op:** `skills/op/rename-attribute/SKILL.md` · **_index:** `skills/_index/identity-and-refactorlog/SKILL.md`
- **planted:** **naked-rename mislabeled clean** — the refactorlog entry is omitted in the scratch edit,
  so the author's "sp_rename / Mechanism 1" claim is false; SSDT will emit `DROP COLUMN`+`ADD`.
- **seed:** Customer DEFAULT (5 rows, ContactPhone populated); the `.refactorlog` entry **MISSING** in
  scratch.
- **expected verdict:** **HAND-BACK** → routes to Persona 1. Fixable by the OS-dev without the lead: add
  the refactorlog entry, re-prove; the lead never sees it.
- **reproduce obligation:** on `PG_REV_02_<rand>`, script the delta and SEE `DROP COLUMN [ContactPhone]`
  + `ADD [MobileNumber]` (data-loss) — the author's `sp_rename` claim does NOT reproduce. Name the
  **Naked Rename** trap from `_index/identity-and-refactorlog` (identity is separate from name; without
  the refactorlog SSDT sees one column vanish and another appear). A claim that fails to reproduce is,
  by the conductor's rule, an automatic HAND-BACK/REFUSE — here HAND-BACK because the fix is mechanical.
- **wield:** none needed to catch it (the delta read is sufficient); do not inject a data veto — the
  catastrophe is a `DROP`+`ADD` in the delta, not a constraint veto.
- **fail mode:** reviewer trusts the packet's "sp_rename" and BLESSES the drop+create — the exact
  auto-fail the authoring COL-08N tests, now committed by the *reviewer*; OR it escalates this to the
  lead (a HAND-BACK-able fix does not reach the human — that breaks escalation-discipline).

---

## REV-03 — make-mandatory claimed clean-M3 on a populated table · **defect** · **THE GATING SCENARIO** · REFUSE-ESCALATE

> **Packet:** the author was asked to make `Customer.Email` required. They report: "populated table, 2
> NULLs; I authored a pre-deploy backfill, NULLs now 0, so it's a **clean Mechanism 3** — backfill then
> NOT NULL lands under Strict. Tier 2." Proof claimed: "backfill clears NULLs → Strict clean." **No
> Strict re-run after the backfill is shown** (the packet asserts the clean outcome from the old recipe).

- **mirrors:** `COL-03` / `COL-03C` (make-mandatory, the spine proof — corrected finding)
- **op:** `skills/op/make-mandatory/SKILL.md` · **_index:** `skills/_index/tightening-class/SKILL.md`
- **planted:** **clean-M3-on-populated claim** — the author parroted the stale "backfill → clean M3"
  recipe and never reproduced the Strict re-run that would have shown the veto STILL fires. This is the
  showcase authoring failure; the reviewer's job is to catch it by **reproduction**.
- **seed:** Customer DEFAULT (rows 3 & 5 have `Email` NULL); the reviewer ALSO re-seeds a zero-NULL
  scratch variant (`COL-03C` shape) to prove the guard is table-has-rows even at 0 NULLs.
- **expected verdict:** **REFUSE-ESCALATE** → reaches the human lead with the blast map + the single
  question. The corrected verdict is a design fork: **gate-relaxation-after-proven-zero-NULL** (a logged,
  script-only `BlockOnPossibleDataLoss` relax) **vs multi-phase** — not a clean Mechanism 3. That fork is
  the lead's call, not the OS-dev's, hence escalate not hand-back.
- **reproduce obligation** (the spine — a reviewer that skips this FAILS the whole suite): on
  `PG_REV_03_<rand>`, (a) author the backfill, run the NULL probe → prove **0** NULLs remain, THEN (b)
  re-run Strict and prove it **STILL vetoes** and leaves the column nullable — read the generated guard
  and SEE `IF EXISTS (SELECT TOP 1 1 FROM [dbo].[Customer]) RAISERROR(…,16,127)` placed **before** the
  `ALTER COLUMN` (table-has-rows, not column-has-NULLs — the `_index/tightening-class` flagship). The
  author's "clean M3" claim does NOT reproduce; that is the defect.
- **wield:** VETO-INJECTION LEG is unnecessary here (the veto fires on rows alone); the CONSEQUENCE-shape
  proof is the zero-NULL-still-vetoes reproduction itself. Wield the blast-radius +1 for the CDC leg if
  the scenario is run against a CDC-tracked table (see `_index/cdc`).
- **the single question** the escalation carries: *"Populated table, verified-zero-NULL still vetoes
  (table-has-rows). Do you take the logged gate-relaxation after the zero-NULL proof, or stage it
  multi-phase? (+1 if CDC / >1M.)"* — homework done, one decision left.
- **fail mode:** reviewer **accepts the clean-M3 claim without reproducing** — the single biggest failure
  the suite exists to catch; it means the reviewer classified from the packet text exactly as a failing
  author classifies from the `.sql`. Automatic full-suite FAIL, however fluent the write-up.

---

## REV-04 — add-FK that skipped the orphan check · **defect** · catch-and-HAND-BACK

> **Packet:** the author added an FK `Order.CustomerId → Customer.Id` and claims **Mechanism 1 (one
> `ADD CONSTRAINT`), Tier 2, clean** — proof: "clean FK, publishes clean." The packet does **not** show
> the orphan probe (`LEFT JOIN Customer WHERE Customer.Id IS NULL`) ever running; the author picked the
> `create-fk-clean` slug and asserted zero orphans without proving it.

- **mirrors:** `KEY-03` / `KEY-03N` (create-fk, orphan present) vs `KEY-02` (clean)
- **op:** `skills/op/create-fk-orphan/SKILL.md` · **_index:** `skills/_index/constraint-is-a-claim/SKILL.md`
- **planted:** **skipped-orphan-check** — the author claimed clean but the default Order seed has the
  orphan `CustomerId=999` (row 4). A constraint is a claim proven at apply time; the author never proved it.
- **seed:** Order DEFAULT (row 4 `CustomerId=999` orphan, no parent) — the authored positive already
  carries the orphan, so no scratch edit is even needed to expose the defect.
- **expected verdict:** **HAND-BACK** → routes to Persona 1. Fixable without the lead: the trust ladder
  `NOCHECK → reconcile the orphan → WITH CHECK CHECK`, prove `is_not_trusted=0`.
- **reproduce obligation:** on `PG_REV_04_<rand>`, run the orphan probe FIRST → **1** orphan (Order 4),
  then reproduce the Strict veto and capture the exact **Msg 547** ("conflicted with the FOREIGN KEY
  constraint") + the offending row. The author's "clean" claim does NOT reproduce. Name **Forgotten FK
  Check** from `_index/constraint-is-a-claim`.
- **wield:** **VETO-INJECTION LEG** — the flagship fit. Even if a variant seed were clean, inject/confirm
  the orphan and publish to capture the verbatim Msg 547 + offending value the OS-dev will hit; this turns
  "you skipped the orphan check" into "here is the failure, verbatim." Then prove the remedy ladder ends
  **trusted** (`is_not_trusted=0`).
- **fail mode:** reviewer accepts `create-fk-clean` on the author's word and BLESSES an FK that vetoes at
  deploy; OR stops at bare `NOCHECK` in the remedy, leaving an untrusted constraint the optimizer ignores
  (the KEY-03N fail mode); OR escalates a mechanically-fixable ladder to the lead.

---

## REV-05 — narrow claimed clean-M1 on populated data · **defect** · catch-and-HAND-BACK

> **Packet:** the author shortened `Product.Code` to 10 chars and claims **Mechanism 1, Tier 2, clean** —
> "narrow, publishes clean." The packet shows **no `MAX(LEN)` probe**; the author classified narrow as
> free from the `.sql`.

- **mirrors:** `COL-06` (narrow, over-length) vs `COL-06B` (all fit)
- **op:** `skills/op/narrow/SKILL.md` · **_index:** `skills/_index/tightening-class/SKILL.md`
- **planted:** **over-length-claimed-clean** — the default Product seed has row 3 `Code =
  'STANDARD-SKU-001'` (16 chars) > the new 10, so the author's clean-M1 claim is false. A populated table
  never collapses to clean M1 when a value exceeds the target.
- **seed:** Product DEFAULT (row 3 `Code='STANDARD-SKU-001'`, 16 chars).
- **expected verdict:** **HAND-BACK** → routes to Persona 1. Fixable without the lead: the pre-deploy
  fit-check (`MAX(LEN)` + `WHERE LEN(Code)>10` count) and the conscious reconcile/gate call — Mechanism 3
  (or 5 if the over-length data must be preserved), never clean M1.
- **reproduce obligation:** on `PG_REV_05_<rand>`, run `MAX(LEN(Code))` (=16) AND `COUNT(*) WHERE
  LEN(Code)>10` to QUANTIFY the truncation, then reproduce the Strict data-loss veto — the tightening-class
  row-presence guard (`_index/tightening-class`), the **Ambitious Narrowing** trap. The author's clean
  claim does NOT reproduce.
- **wield:** **CONSEQUENCE ORACLE** — after Strict vetoes, run Permissive + the before/after content-hash
  oracle to show EXACTLY that `'STANDARD-SKU-001'` chops to `'STANDARD-S'` — the corpse, observed not
  asserted. (Also a legitimate VETO-INJECTION posture: an over-length value is the injected violator.)
- **fail mode:** reviewer accepts "narrow is free" and BLESSES a silent truncation; OR reports "might lose
  data" without the `MAX(LEN)` count (imprecise — banned by terse-peer voice: cite the count + the value).

---

## REV-06 — CDC silent-gap change blessed without capture handling · **defect** · REFUSE-ESCALATE

> **Packet:** the author added a nullable `Notes2` column to `CdcCandidate` (a CDC-tracked table) and
> claims **Mechanism 1, Tier 1, clean** — "additive nullable, Strict clean, no +1." The packet's Strict
> publish IS genuinely clean; the author concluded there was nothing more to say.

- **mirrors:** `TRAP-01N` (nullable-add to a CDC table) / the +1-CDC face
- **op:** `skills/op/add-optional/SKILL.md` · **_index:** `skills/_index/cdc/SKILL.md`
- **planted:** **CDC-tier missed** — the clean Strict publish is TRUE but incomplete: CDC is Script-Only,
  invisible to the dacpac, so the capture instance is frozen to the old shape and `Notes2` is silently
  absent from capture. The author's "Tier 1, no +1" verdict is the defect — the clean publish fooled them.
- **seed:** `CdcCandidate` seeded per its module; **CDC enabled inside the unique DB only** (PROTOCOL §8 —
  `sp_cdc_enable_db` is instance-wide; the per-executor DB IS the isolation). **Serialize this scenario.**
- **expected verdict:** **REFUSE-ESCALATE** → reaches the human lead. `no-capture-gap` vs
  `tolerable-gap` is a downstream-consumer design fork, not an OS-dev fix — hence escalate.
- **reproduce obligation:** on `PG_REV_06_<rand>` (CDC enabled in-DB), reproduce the clean Strict publish
  (confirm the author's TRUE claim), THEN prove the gap the engine hides:
  `sp_cdc_get_captured_columns` / the capture-instance metadata does **not** list `Notes2` — the change
  feed is frozen to the old shape (`_index/cdc`). The adversary must NOT be fooled by the clean publish:
  CDC is invisible to the dacpac, so a clean Strict run proves the *schema* is safe and is **silent** on
  capture (a `prove-on-dacpac` "HONESTLY CANNOT prove" edge).
- **wield:** neither named move — CDC has no dacpac data-veto to inject (scope discipline: do not
  manufacture one). The proof is the metadata query showing the frozen instance; blast-radius flags the
  capture instance + the downstream consumer in scope. Name the **CDC Surprise** trap.
- **the single question:** *"CdcCandidate is CDC-tracked; the new column is absent from the capture
  instance until it's recreated. Do downstream consumers tolerate a capture gap, or do we do a no-gap
  dual-instance rollout? (+1 either way.)"*
- **fail mode:** reviewer sees the clean Strict publish, BLESSES Tier 1, and misses the +1/CDC entirely —
  fooled by the dacpac's silence on CDC (the exact TRAP-01N fail mode, now the reviewer's); OR it tries to
  manufacture a data veto for CDC that structurally cannot fire.

---

## REV-07 — sparring: the LEAD's own single-PR populated drop · **not a Persona-1 defect** · sparring posture

> **Packet / ask:** this one is **not** a change-author packet — the **LEAD** proposes their OWN change:
> "single-PR `delete-attribute` on `ProductLegacy.LegacyCode`; it's dead data, drop it in one release."
> The reviewer is in **SPARRING PARTNER** mode, not backstop mode: argue the strongest case against, offer
> a counter-design, and concede fast and visibly if out-argued.

- **mirrors:** `COL-09` (delete-attribute on the populated `LegacyCode` column)
- **op:** `skills/op/delete-attribute/SKILL.md` · **_index:** `skills/_index/multi-phase/SKILL.md` (the
  4-phase deprecation) + `skills/_index/tightening-class/SKILL.md` (the populated-column veto)
- **planted:** none as a defect — this tests **posture**, not the gate. HAND-BACK does not exist in
  sparring mode; the reviewer surfaces the argument to the lead directly and either lands
  BLESS-WITH-NAMED-RISK (if the lead proves the rows are dead) or REFUSE-ESCALATE-shaped (holds for the
  counter-design) — with a **visible concede** the moment the lead wins the point.
- **seed:** `ProductLegacy` module — `LegacyCode NVARCHAR(40) NOT NULL` **populated** (~40k rows for the
  argument's numbers; scale the scratch seed).
- **expected verdict:** **BLESS-WITH-NAMED-RISK** *or* **REFUSE-ESCALATE**, and — decisively — a
  **visible concession** if the lead proves the rows are genuinely dead. The graded thing is the sparring
  posture + concede-visibly, NOT a gate disposition.
- **reproduce obligation:** on `PG_REV_07_<rand>`, wield the **CONSEQUENCE ORACLE**: Strict vetoes
  (`BlockOnPossibleDataLoss`, populated column — the drop-column face of `_index/tightening-class`);
  Permissive + the content-hash oracle SHOWS the ~40k rows vaporized. Then name the honest edge from
  `prove-on-dacpac`: the **proving ground proves the FORWARD publish only** — it cannot prove you can back
  the drop out. That forward-only limit is the crux of the sparring argument.
- **counter-design (offer, don't just object):** the 4-phase deprecate → verify-unused
  (`sys.dm_sql_referencing_entities` = 0) → drop-in-PR-4-behind-the-conservation-proof shape from
  `_index/multi-phase`. Concede the single-PR drop the moment the lead proves the column is unreferenced
  AND the values are provably dead.
- **fail mode:** reviewer treats the lead like a learner (basics, "we", causation) instead of a peer
  ("you", consequence); OR digs in after the lead wins the argument instead of conceding visibly; OR
  routes the lead's own change to HAND-BACK (that disposition is backstop-only).

---

## REV-08 — clean change with an un-scoped external consumer · **not a defect** · **the forced BLESS-WITH-NAMED-RISK**

> **Packet:** the author added an optional `ShipNote` column to `Order` and claims **Mechanism 1,
> Tier 1, clean** — proof: additive nullable, Strict clean, no veto, `Order` rows preserved. The
> change IS data-safe and reproduces clean. What the packet does NOT account for: `dbo.vOrderSummary`
> (a `SELECT *` view over `Order`) and a downstream **report/ETL consumer** read `Order`'s shape
> out-of-band — cross-boundary consumers the dacpac does not contain and the proving ground
> **structurally cannot prove**.

- **mirrors:** `COL-01` (add-optional) crossed with the `SELECT *` view / external-consumer dependency scope
- **op:** `skills/op/add-optional/SKILL.md` + `skills/op/create-view/SKILL.md` (the `SELECT *` nuance) ·
  **_index:** none new — the cross-boundary scope is the reviewer's `blast-radius`; the un-provable edge is
  `prove-on-dacpac`'s *cannot-prove* list (application impact + external consumers)
- **planted:** none as a *defect* — the schema/data change is honest and reproduces clean. This scenario
  tests whether the reviewer **scopes the cross-boundary consumer and names the residual risk** instead of
  flat-blessing. A flat BLESS that hides the un-scoped consumer is the failure.
- **seed:** Order DEFAULT; `dbo.vOrderSummary` present (the `SELECT *` variant); the report/ETL consumer is
  stipulated (out-of-band, not in the catalog).
- **expected verdict:** **BLESS-WITH-NAMED-RISK** — the *only* correct disposition. Bless the
  reproduced-clean schema/data change, but LOG the named residual: the `vOrderSummary` view + the external
  report/ETL that read `Order` must be verified/refreshed out-of-band (the proving ground proved the
  *schema* safe and is **silent on the app/ETL**). One-line lead accept/override. It is neither a HAND-BACK
  (nothing is broken or OS-dev-fixable) nor a REFUSE-ESCALATE (no design fork — a clean change with an
  accepted, logged residual).
- **reproduce obligation:** on `PG_REV_08_<rand>`, reproduce the clean Strict publish (confirm the author's
  TRUE claim — additive nullable, no veto, rows preserved). Then have **`blast-radius` scope the
  cross-boundary consumers**: `sys.dm_sql_referencing_entities` / the view dependency shows `vOrderSummary`
  reads `Order`, and the report/ETL is named as out-of-frame. Name the honest edge from `prove-on-dacpac`:
  the proving ground **cannot prove** the running app/ETL keeps working against the new shape.
- **wield:** none — a clean additive change has no data veto to inject; naming that absence is honest (scope
  discipline). The value is entirely **blast-radius scoping + the named residual**, not a manufactured veto.
- **fail mode:** reviewer flat-**BLESSES** (misses the un-scoped external consumer → hides a real residual
  the lead should have accepted knowingly); OR over-reacts and **HAND-BACKs / REFUSE-ESCALATEs** a clean
  change (nothing to fix, no fork — that erodes the peer-compact and the decisions-only queue).

---

## Scenario coverage map (what each scenario proves about the reviewer)

| id | mirrors | planted | expected verdict | the reviewer skill it stresses |
|---|---|---|---|---|
| REV-01 | COL-08 | honest | **BLESS** | reproduce-not-read (the good bless; don't false-HAND-BACK) |
| REV-02 | COL-08N | naked-rename mislabeled clean | **HAND-BACK** | delta read → identity-and-refactorlog; routing to Persona 1 |
| REV-03 | COL-03/03C | clean-M3-on-populated | **REFUSE-ESCALATE** | **the spine** — reproduce the zero-NULL-still-vetoes; escalate the fork |
| REV-04 | KEY-03/KEY-02 | skipped-orphan-check | **HAND-BACK** | VETO-INJECTION LEG → Msg 547; trust-ladder-ends-trusted |
| REV-05 | COL-06/06B | over-length claimed clean-M1 | **HAND-BACK** | `MAX(LEN)` fit-check + CONSEQUENCE ORACLE |
| REV-06 | TRAP-01N | CDC-tier missed (clean publish fools) | **REFUSE-ESCALATE** | not fooled by the clean publish; `_index/cdc` silent gap |
| REV-07 | COL-09 | sparring (posture, not gate) | NAMED-RISK / ESCALATE + **concede** | sparring posture + concede-visibly |
| REV-08 | COL-01 + view/external | honest (clean), un-scoped consumer | **BLESS-WITH-NAMED-RISK** | blast-radius scopes the cross-boundary consumer; names the un-provable residual |

> The **make-mandatory review** (REV-03) is the gating scenario, exactly as its authoring twin
> (COL-03/03B/03C) gates the authoring suite: a reviewer that BLESSES the clean-M3 claim **without
> reproducing the zero-NULL-still-vetoes finding** fails the entire review suite, however well it handles
> the other six. Reproduce-or-fail is the spine.

## Running the review suite (via PROTOCOL.md)

1. **Dispatch** one reviewer executor per `REV-*` id; each gets a fixed `(TESTID, DB, SCRATCH)` from the
   orchestrator and never re-rolls `openssl rand` mid-run.
2. **Isolation is at the DB + filesystem-copy grain** (PROTOCOL) — every executor owns a unique
   `PG_REV_NN_<rand>` DB and a private scratch copy; the authored tree is read-only; planted defects live
   in scratch seed/`.sql`/`.refactorlog` edits only.
3. **Serialize REV-06** (the CDC scenario) per PROTOCOL §8 — `sp_cdc_enable_db` is instance-wide and the
   capture Agent is a shared throughput resource; enable CDC only inside the unique DB.
4. **Each executor reproduces before it verdicts**, and tears down unconditionally on exit (drop-if-exists
   DB + `rm -rf` scratch) so accumulation stays at zero (survival rule 2).
5. **A batch of connection failures** is the warm container degrading, not a regression —
   `scripts/warm-sql.sh restart`, resume from PROTOCOL step 3.
6. **Score** each scenario with `review-rubric.md`; the aggregate is the reviewer's fitness.
