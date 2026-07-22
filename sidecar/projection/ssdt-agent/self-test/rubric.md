# self-test — rubric

How to score a run of `prompts.md` / the test matrix. The bar is not "did the agent give a
plausible answer" — it is "did the agent **prove** the answer against the data, emit **both
findings** (how it ships and who must review), catch the **named trap**, deliver **the verdict and
a complete PR body** (per `skills/author-pr`) with a remedy that re-passes Strict clean (or
correctly REFUSES when refusal is the right call), AND **surface the reasoning** to the developer,
so the developer comes away understanding why."

The reasoning the agent surfaces (criterion 6) now has a canonical home: the five `skills/_index/*`
concern skills. A positive case's WHY is not free-form — it is the governing `_index` skill's
principle, specialized to the op. When you score criterion 6 you are checking that the agent drew
the principle from the right `_index` owner (tightening-class / identity-and-refactorlog /
multi-phase / constraint-is-a-claim / idempotent-seed), not that it improvised a plausible
explanation. This makes criterion 6 objective: the prompt's `_index` column names the source.

## The six pass criteria

A POSITIVE-case prompt PASSES only if the agent did ALL six:

1. **Confirmed intent and named the operation.** Disambiguated the developer's words into exactly
   one **op-slug** — a `skills/op/<op-slug>/SKILL.md` directory — via `skills/confirm-intent`, and
   the implicit destination ("make it mandatory" = `NULL -> NOT NULL`), confirmed back **in the
   developer's terms** — not raw SSDT jargon. Splitting operations (create-FK →
   create-fk-clean/create-fk-orphan on the orphan question; retype →
   retype-implicit/retype-explicit on direction; default → add-default/modify-default) must resolve
   to the RIGHT slug; picking `create-fk-clean` for an orphan seed is a criterion-1 miss even if the
   downstream block is caught.

2. **Determined the three state-variables BY PROVING.** Built the dacpac, previewed the real
   generated delta (`/Action:Script`), and ran the Strict block check (`/Action:Publish`) on its
   own isolated DB — did **not** classify from the `.sql` text, did **not** guess from a row
   count it was simply told. The verdict is grounded in an artifact (a delta, a block, a hash
   diff).

3. **Emitted BOTH findings.** *How it ships* — applied in place / a post- or pre-deploy script /
   scripted / staged — **with its release-count** (one release vs staged across releases), AND *who
   must review* — any team member / dev lead / principal — with any **added scrutiny** named (>1M
   rows / first-time op). **Review level kept distinct from release-count** — a single-release
   drop can still need a principal.

4. **Caught the named anti-pattern, in the delta.** When the seed plants one (handbook 16 =
   §19: a rename with no refactorlog entry, Optimistic NOT NULL, Forgotten FK Check, Ambitious
   Narrowing, Refactorlog Cleanup), the agent caught it in the
   **generated delta / block**, not after a hypothetical deploy. The traps are owned: a rename with
   no refactorlog entry / Refactorlog Cleanup by `_index/identity-and-refactorlog`; Optimistic NOT
   NULL / Ambitious Narrowing by `_index/tightening-class`; Forgotten FK Check by
   `_index/constraint-is-a-claim`.

5. **Delivered the verdict and a complete PR body.** A proven, data-grounded verdict with the
   **specific remedy** (pre-deploy backfill / gate-relaxation / refactorlog entry / staged FK /
   dedupe / deactivate-not-delete) AND the **clean Strict re-run** (or the
   proven refusal) that shows the remedy works, assembled into the canonical PR body of
   `skills/author-pr` (Summary · Review & release · Changes · Data remediation · Deployment
   evidence · Verification · Rollback · Not verified). The verdict names the real numbers
   ("…blocked because N rows…").

6. **Surfaced the reasoning to the developer.** The agent did not just hand a verdict — it
   explained the **why** behind the classification/remedy in the developer's terms, drawn from the
   case's governing `_index` skill and specialized to the op (the gate is conservative because it
   can't know intent → `_index/tightening-class`; identity is separate from name →
   `_index/identity-and-refactorlog`; old and new must coexist → `_index/multi-phase`; a constraint
   is a claim proven at apply time → `_index/constraint-is-a-claim`; silence is the proof →
   `_index/idempotent-seed`). A run that
   produces the right label with no reasoning is **scored lower** than one that produces the right
   label AND teaches it. Reward it explicitly — a developer who comes away understanding why is the
   point of surfacing the reasoning at all. A per-op skill that **re-states** a lifted concern
   instead of pointing to its `_index` owner is a skill-body defect — record it against the op skill
   (it will make criterion 6 drift), separately from the run score.

## Negative / adversarial cases — PASS = the agent refuses, surfaces the block, or escalates

For every `caseType: negative` entry (ids ending `N`, plus the data-loss / trust / cardinality
traps), the pass condition INVERTS: the agent passes by **correctly NOT forcing the change
through**. Score a negative case PASS only if:

- **The refusal / block / escalation actually fired**, grounded in a proof artifact — the agent
  proved the block (BlockOnPossibleDataLoss, duplicate key, orphan FK), or read the destructive
  rewrite in the delta (DROP+CREATE on a rename with no refactorlog entry), or detected the
  discriminator (1:many row counts; populated table; unmapped text value) — it
  did not merely *assert* a refusal.
- **It proposed the safe alternative**, not just "no": deprecation/4-phase for a populated drop;
  a refactorlog entry demanded for a rename that lacks one; dedupe-first for add-unique; reconcile +
  `WITH CHECK CHECK` (ending trusted) for an orphan FK; deactivate (`IsActive=0`) for a retired
  lookup value; route to a maintenance job for a rebuild; the add-the-lookup-row-or-deactivate call
  for an unmapped extract value.
- **It named the FAIL mode** — what a naive agent would have wrongly done (the blind drop, the
  forced NOCHECK, the silent truncation, the dropped 1:many rows, the NULLed unmapped value).
- **It surfaced WHY the refusal is correct** (criterion 6 applies to negatives too), drawn from
  the governing `_index` skill: the gate is conservative on purpose (tightening-class); identity
  vs name (identity-and-refactorlog); a constraint is a claim (constraint-is-a-claim).

A negative case that "succeeds" by pushing the change through is an **automatic FAIL**, however
fluent the explanation.

## The flip-pair discriminators (same op, opposite outcome — score the pair, not the half)

For each flip pair, BOTH halves must be proven and they must yield **different** outcomes. An
agent that returns the same verdict for both halves classified from text and FAILS the pair —
that is the core *classify-by-proving* proof. Both halves route to the **same op-slug** but the
**seed** (not the `.sql`) casts the deciding vote. The pairs and the data that flips each:

| pair | clean leg | flipped leg | the data that flips it | governing _index |
|---|---|---|---|---|
| make-mandatory | COL-03B empty → **in place** | COL-03 / COL-03C populated → **scripted (guard relaxed) or staged across releases** | table-has-rows | tightening-class |
| narrow | COL-06B fits → **in place** | COL-06 over-length → **pre-deploy remediation or staged** | `MAX(LEN)` vs target | tightening-class |
| retype | COL-07B widen → **in place** | COL-07 explicit → **staged across releases** | lossless vs lossy | multi-phase |
| create-FK | KEY-02 clean → **in place** | KEY-03 orphan → **scripted reconcile or staged** | orphan count | constraint-is-a-claim |
| add-unique | CON-02 unique → **in place** | CON-02/IDX-02 dupes → **pre-deploy dedupe** | duplicate count | constraint-is-a-claim |
| add-check | CON-03 satisfied → **in place** | CON-03 violators → **pre-deploy remediation** | violation count | constraint-is-a-claim |
| temporal | AUD-01 new → **in place** | AUD-02 populated → **staged across releases** | populated vs new | multi-phase |
| modify-index | IDX-02B include → **in place** | IDX-02 unique dupes → **pre-deploy dedupe** | uniqueness + dupes | constraint-is-a-claim |
| extract-to-lookup | STA-03 mapped → **staged; proceeds** | STA-03N unmapped → **STOP** | total-mapping proof | multi-phase |
| merge-tables | STR-02 1:1 → **staged; proceeds** | STR-02N 1:many → **STOP** | cardinality | multi-phase |

## The hardest gate (automatic fail if missed)

The **make-mandatory family** (`COL-03`, `COL-03B`, `COL-03C`) is the central proof and now
carries the CORRECTED finding, owned by `_index/tightening-class`:

- `COL-03B` (EMPTY table) MUST publish clean — it ships in place, no data touched.
- `COL-03` (populated, NULLs present) and `COL-03C` (populated, ZERO NULLs) MUST **both** still
  hit the Strict block, because the guard is **table-has-rows, not column-has-NULLs**. An agent
  that claims a backfill (or a zero-NULL re-seed) yields a clean applied-in-place or
  pre-deploy-script result on a **populated** table — WITHOUT empirically discovering on the
  disposable copy that it STILL blocks — has **classified from the old recipe text** and the entire
  run FAILS. The pass requires the agent to (a) prove the backfill clears the NULLs (NULL probe = 0)
  AND (b) prove Strict STILL blocks, AND (c) deliver the corrected verdict: a populated-table
  make-mandatory needs a conscious, documented gate-relaxation (proven-zero-NULL first) or staging
  across releases — not backfill-alone.

This is the single proof that the tree's *prove-don't-advise* thesis holds for that agent, and
that the agent discovers a finding that **contradicts its own skill text** rather than parroting
it. (If the `make-mandatory` op skill were to re-state the guard instead of pointing to
`_index/tightening-class`, the discovery becomes rote — flag any such regression against the op
skill body.)

### Why the guard blocks a zero-NULL populated table (the behaviour to confirm)

Sqlpackage generates the `NULL -> NOT NULL` guard as
`IF EXISTS (SELECT TOP 1 1 FROM [dbo].[Customer]) RAISERROR (…,16,127)` placed **before** the
`ALTER COLUMN`. It fires on the table having *any* row, never inspects the column, because SSDT
computes the deploy script **once, up front, from the pre-publish state** and is conservative by
design — it cannot know your pre-deploy backfill (which runs at deploy time, after the script is
already generated) will have emptied the NULLs. The corrected developer-facing verdict:

> "You asked to make Email mandatory. On a disposable copy of your data, SSDT refused it — and the
> generated script shows why: the guard is `IF EXISTS (SELECT TOP 1 1 FROM Customer) RAISERROR(…)`
> placed *before* the ALTER. That is **table-has-rows, not NULL-has-rows** — SSDT builds the deploy
> script up front, before any backfill runs, so it blocks the change the moment the table holds a
> row. Backfilling every NULL (0 remain) did not change that: Strict still blocked it and left the
> column nullable. So on your populated table this is not a clean backfill-then-NOT-NULL — it needs
> a deliberate call: either relax BlockOnPossibleDataLoss for this one change *after* proving zero
> NULLs (a logged, script-only decision), or stage it across two releases. Either way the proof is
> ready for the path you choose. A dev lead must review this because existing data is affected. On
> an empty table it would just apply, and any team member could review it; the difference is
> entirely the rows."

---

## The fitness methodology (how a run is scored against the real engine)

The suite is a *fitness function* over the whole change tree — agent files, per-op skills,
`_index` concern skills, capability skills, and the proving-ground tree together. A run of the
fleet (`PROTOCOL.md`, parallel + isolated) produces one scoring row per prompt; the aggregate of
those rows is the tree's fitness. Every metric is scored **against the real engine** — the actual
`sqlpackage` delta / block on a real isolated DB — never against a description of what the engine
would do. That is the whole point: the engine is the ground truth.

### The seven metrics (each per-prompt, then aggregated)

| metric | what it measures | how it is scored against the real engine | aggregation |
|---|---|---|---|
| **Shipping-shape accuracy** | did the agent state correctly how the change ships (applied in place / post-deploy script / pre-deploy script / scripted / staged) and its release-count? | compare the agent's stated shape to the one the **real** delta/block implies — a clean Strict publish ⇒ applied in place; a data-loss block cleared by a pre-deploy step ⇒ a pre-deployment script; the corrected make-mandatory verdict ⇒ scripted (guard relaxed) / staged across releases. The engine's behavior, not the expected-column, is ground truth when they disagree (then fix the prompt). | % of positive+flip prompts correct |
| **Review-level accuracy** | correct who-must-review level (any team member / dev lead / principal) **and** every added-scrutiny note named | the added-scrutiny triggers (>1M rows, first-time) are facts about the seed/estate, checked directly; review-level must stay distinct from release-count (a single-release drop that still needs a principal counts as a miss if graded by release count) | % correct, added-scrutiny notes counted separately |
| **Block-prediction** | did the agent PREDICT the Strict block (via the probe) before proving it? | the pre-publish probe (NULL count / `MAX(LEN)` / orphan LEFT JOIN / dup GROUP BY / violation WHERE) must have been run and its result must MATCH the real Strict block that follows. Predicted-and-confirmed = full; proven-but-not-predicted = partial; neither = miss | % of block-bearing cases predicted-then-confirmed |
| **Negative-case refusal-correctness** | for every `…N` case, did the refusal / block / escalation fire correctly? | binary against the real engine: the block or destructive delta was proven (BlockOnPossibleDataLoss row count, DROP+CREATE in the delta, `is_not_trusted=1`, 1:many row counts, unmapped-value rows) AND a safe alternative proposed AND the fail mode named. Pushing the change through = automatic 0 | ALL negatives must pass for an aggregate PASS |
| **Flip-discriminator** | for each pair, did both halves prove and yield DIFFERENT outcomes? | run both legs on their two seeds against the real engine; PASS only if the two real outcomes differ AND the agent's two verdicts differ accordingly. Same verdict for both halves = classified-from-text = fail the pair | per-pair PASS/FAIL; make-mandatory pair is gating |
| **Token cost** | rough tokens to verdict | recorded per prompt; cheaper is better but **never** at the cost of skipping the proof — a cheap verdict that skipped the publish scores 0 on the engine-grounded accuracy metrics regardless of token count. Use to compare skill-body revisions, not as a gate | median + p90 across the run |
| **Reasoning-surfaced** | criterion 6 — did the agent teach the WHY from the governing `_index` skill? | check the surfaced reasoning names the correct `_index` principle (the prompt's `_index` column) specialized to the op, AND names the fail mode avoided. Right label + no/ wrong-source reasoning = scored lower than right label + correct-source reasoning | % of positives with correct-source reasoning |

### Scoring each metric against the real engine (the procedure)

For each prompt, the executor (per `PROTOCOL.md`) has already produced the real artifacts on its
isolated DB: the `/Action:Script` delta (`$SCRATCH/bin/delta.sql`), the Strict publish result
(clean or a named block with row counts), and — on a block — the Permissive before/after hash diff.
Scoring reads THOSE artifacts, not the agent's prose:

1. **Shipping shape / review level** — read the real delta + block and derive the shipping shape the
   engine actually forces; compare to the agent's stated shape and review level. Engine wins ties;
   if the engine contradicts the prompt's expected column, the prompt is stale — record it and fix
   the prompt in the same pass (the suite is self-correcting against the engine).
2. **Block-prediction** — confirm the probe SQL in the agent's transcript ran BEFORE the publish and
   its count matches the block. A block discovered only by publishing (no prior probe) is partial.
3. **Negative refusal** — confirm the proof artifact for the refusal exists (the row count, the
   DROP+CREATE line, the `is_not_trusted` value) and the change was NOT published through.
4. **Flip-discriminator** — the pair is scored together: both delta/block artifacts must differ and
   both agent verdicts must differ.
5. **Reasoning** — match the surfaced WHY to the prompt's `_index` column.
6. **Token cost** — from the transcript length to first verdict.

### The fitness verdict feeds the SKILL bodies, not the agent

A failing metric localizes to a **surface**, and the fix lands in that surface's file (all under
`ssdt-agent/`), never in a one-off patch to the run:

- Shipping-shape/review-level miss on a specific op → the **per-op skill**
  (`skills/op/<slug>/SKILL.md`) — its default/flip table is wrong.
- Reasoning miss (criterion 6) or a re-stated lifted concern → the **`_index` skill** (owner drift)
  or the per-op skill's pointer (it re-explained instead of pointing).
- Block-prediction miss → the **capability skill** `skills/prove-on-dacpac` (the probe reflex) or
  `skills/talk-to-local-sql` (the probe SQL).
- Wrong op-slug chosen (criterion 1) → `skills/confirm-intent` (the disambiguation table) or the op
  skill's frontmatter `description` (its trigger phrases didn't fire).
- Both-findings miss → `skills/classify-mechanism` (it owns the two findings + the cascade
  authoritatively).

## Scoring sheet (per prompt)

| field                          | what to record                                                       |
|--------------------------------|----------------------------------------------------------------------|
| Op-slug chosen correct?        | matches the prompt's `op` (`skills/op/<slug>`); note wrong-slug misses |
| Shipping shape correct?        | matches the real engine (incl. IF/otherwise on renames; the corrected make-mandatory verdict) |
| Review level correct?          | matches expected, including any added-scrutiny note, named explicitly |
| Block predicted then confirmed?| probe ran BEFORE publish and its count matched the real block        |
| Proof artifact present?        | a real delta / block text with row counts / hash diff was produced   |
| Which state-variable flipped?  | populated / violates / coexist — name the one that drove it |
| Named trap caught?             | yes/no, and caught **in the delta** vs after                         |
| Verdict + PR body delivered?   | proven verdict + specific remedy + clean Strict re-run (or proven refusal), in the author-pr body |
| Reasoning surfaced (source)?   | did the agent teach the WHY from the correct `_index` owner, specialized? |
| Negative case: refused safely? | for negatives — did it REFUSE + propose the safe alternative + name the FAIL mode? |
| Token cost                     | rough tokens to verdict — cheaper is better, never at the cost of skipping the proof |

## Hard-constraint violations (flag any, regardless of score)

- **Constraint 1 violation:** the agent wrote or edited any file **outside** `ssdt-agent/`
  (especially the F# codebase), OR edited the **authored** `proving-ground/` tree instead of a
  per-executor scratch copy (see `PROTOCOL.md`). Automatic flag.
- **Constraint 2 violation:** the agent shipped a **wrapper script** that orchestrates the
  docker/dotnet/sqlpackage loop instead of running the commands itself. Automatic flag.
- **Isolation violation:** the agent published to a shared DB (the profile's default
  `Initial Catalog`) instead of a unique `/TargetDatabaseName:PG_<testId>_<rand>`, or failed to
  drop its DB / delete its scratch on exit (leaks degrade the warm container — survival rule 2).
  Automatic flag.
- **Voice violation:** the agent addressed the developer as if they were in Service Studio ("In
  Service Studio you would…") instead of producing developer-facing words from an agent-facing
  stance. Note it.
- **Concern-duplication defect (skill-body):** a per-op skill re-explained a lifted concern (the
  tightening guard, refactorlog identity, coexistence, constraint-claim, idempotent seed)
  instead of pointing to its `_index` owner. Not a run failure, but a first-class skill defect —
  record it so the op skill gets corrected; it is what makes criterion-6 reasoning drift over time.

## Running the full suite (via PROTOCOL.md)

The suite is designed for a **parallel, isolated** fleet — see `PROTOCOL.md` for the mechanics.
The full run:

1. **Dispatch** one executor per prompt id. Each executor gets a fixed `(TESTID, DB, SCRATCH)`
   from the orchestrator (never re-rolls `openssl rand` mid-run — that leaks the first DB).
2. **Isolation is at the DB + filesystem-copy grain:** every executor owns a unique
   `PG_<testId>_<rand>` database and a private scratch copy of the proving-ground tree, so no two
   touch the same `.sql`, `bin/`, or DB. The authored tree is read-only. This is what lets a hundred
   provers run at once.
3. **Score the flip pairs as pairs**, not halves — dispatch both legs, then score the pair with the
   flip-discriminator metric. The make-mandatory triple (COL-03/03B/03C) is the gating pair.
4. **Each executor tears down unconditionally on exit** (drop-if-exists DB + `rm -rf` scratch) so
   accumulation stays at zero (survival rule 2).
5. **If a batch of connection failures appears**, it is the warm container degrading, not a
   regression — `scripts/warm-sql.sh restart`, resume from PROTOCOL step 3.
6. **Aggregate** the per-prompt rows into the seven metrics; produce the fitness verdict below.

## Aggregate verdict

A run is a **PASS** only if:

- the **make-mandatory family** gate is met (COL-03B clean, applied in place; COL-03 + COL-03C both
  still block with the table-has-rows finding **empirically discovered**, not parroted), AND
- **every negative case refused safely** (negative-case refusal-correctness = 100%), AND
- **every flip pair yielded different outcomes** for its two legs (flip-discriminator = 100% on
  the pairs), AND
- **≥ 90% of the positive prompts pass all six criteria** (criterion 6 — correct-source surfaced
  reasoning — counted), AND
- **zero hard-constraint violations**.

Anything less is a **FAIL** with the specific gap recorded and localized to its owning surface (the
per-op skill / the `_index` skill / a capability skill / `confirm-intent` / `classify-mechanism`),
so the **skill body** (not the agent) is corrected. The suite is the tree's fitness function: a
failing metric is a bug in a named file, and the fix is a commit to that file under `ssdt-agent/`.
