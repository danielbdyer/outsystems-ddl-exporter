# self-test — rubric

How to score a run of `prompts.md` / the test matrix. The bar is not "did the agent give a
plausible answer" — it is "did the agent **prove** the answer against the data, emit **both
axes**, catch the **named trap**, deliver the **magic line** with a remedy that re-passes Strict
clean (or correctly REFUSES when refusal is the right call), AND **surface the reasoning** to the
developer so using the system levels them up."

The reasoning the agent surfaces (criterion 6) now has a canonical home: the six `skills/_index/*`
concern skills. A positive case's WHY is not free-form — it is the governing `_index` skill's
principle, specialized to the op. When you score criterion 6 you are checking that the agent drew
the principle from the right `_index` owner (tightening-class / identity-and-refactorlog /
multi-phase / cdc / constraint-is-a-claim / idempotent-seed), not that it improvised a plausible
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
   downstream veto is caught.

2. **Determined the four state-variables BY PROVING.** Built the dacpac, previewed the real
   generated delta (`/Action:Script`), and ran the Strict veto check (`/Action:Publish`) on its
   own isolated DB — did **not** classify from the `.sql` text, did **not** guess from a row
   count it was simply told. The verdict is grounded in an artifact (a delta, a veto, a hash
   diff).

3. **Emitted BOTH orthogonal axes.** Mechanism (1–5) **and its release bucket** (single-phase /
   single-PR / multi-PR), AND Tier (1–4) with any **+1 escalation** named (CDC / >1M rows /
   first-time op). **Danger kept distinct from release-count** — a single-PR drop can be Tier 4.

4. **Caught the named anti-pattern, in the delta.** When the seed plants one (handbook 16 =
   §19: Naked Rename, Optimistic NOT NULL, Forgotten FK Check, Ambitious Narrowing, CDC
   Surprise, Refactorlog Cleanup, SELECT * View), the agent caught it in the **generated delta /
   veto**, not after a hypothetical deploy. The traps are owned: Naked Rename / Refactorlog
   Cleanup by `_index/identity-and-refactorlog`; Optimistic NOT NULL / Ambitious Narrowing by
   `_index/tightening-class`; Forgotten FK Check by `_index/constraint-is-a-claim`; CDC Surprise
   by `_index/cdc`; SELECT * View inline in create-view/compat-view.

5. **Delivered the magic line.** A proven, data-grounded verdict with the **specific remedy**
   (pre-deploy backfill / gate-relaxation / refactorlog entry / staged FK / disable-CDC-first /
   dedupe / deactivate-not-delete) AND the **clean Strict re-run** (or the proven refusal) that
   shows the remedy works. The line names the real numbers ("…vetoed because N rows…").

6. **Surfaced the reasoning to the developer (the graduation criterion).** The agent did not
   just hand a verdict — it explained the **why** behind the classification/remedy in the
   developer's terms, drawn from the case's governing `_index` skill and specialized to the op
   (the gate is conservative because it can't know intent → `_index/tightening-class`; identity is
   separate from name → `_index/identity-and-refactorlog`; old and new must coexist →
   `_index/multi-phase`; the feed is frozen to the old shape → `_index/cdc`; a constraint is a
   claim proven at apply time → `_index/constraint-is-a-claim`; silence is the proof →
   `_index/idempotent-seed`). A run that produces the right label with no reasoning is **scored
   lower** than one that produces the right label AND teaches it. This criterion is what makes
   *using* the system a graduation mechanism; reward it explicitly. A per-op skill that
   **re-states** a lifted concern instead of pointing to its `_index` owner is a skill-body defect
   — record it against the op skill (it will make criterion 6 drift), separately from the run score.

## Negative / adversarial cases — PASS = the agent REFUSES, vetoes, or escalates

For every `caseType: negative` entry (ids ending `N`, plus the data-loss / trust / cardinality
traps), the pass condition INVERTS: the agent passes by **correctly NOT forcing the change
through**. Score a negative case PASS only if:

- **The refusal/veto/escalation actually fired**, grounded in a proof artifact — the agent
  proved the veto (BlockOnPossibleDataLoss, duplicate key, orphan FK), or read the catastrophe
  in the delta (DROP+CREATE on a naked rename), or detected the discriminator (1:many row
  counts; CDC-enabled flag; populated table; unmapped text value) — it did not merely *assert*
  a refusal.
- **It proposed the safe alternative**, not just "no": deprecation/4-phase for a populated drop;
  refactorlog-demanded for a naked rename; dedupe-first for add-unique; reconcile +
  `WITH CHECK CHECK` (ending trusted) for an orphan FK; deactivate (`IsActive=0`) for a retired
  lookup value; capture-instance handling + disable-CDC-first for a CDC-table drop; route to a
  maintenance job for a rebuild; the +1-CDC bump for the nullable-add-to-CDC-table trap; the
  add-the-lookup-row-or-deactivate call for an unmapped extract value.
- **It named the FAIL mode** — what a naive agent would have wrongly done (the blind drop, the
  forced NOCHECK, the silent truncation, the dropped 1:many rows, the missed CDC tier, the
  NULLed unmapped value).
- **It surfaced WHY the refusal is correct** (criterion 6 applies to negatives too), drawn from
  the governing `_index` skill: the gate is conservative on purpose (tightening-class); identity
  vs name (identity-and-refactorlog); the feed is frozen to the old shape (cdc); a constraint is
  a claim (constraint-is-a-claim).

A negative case that "succeeds" by pushing the change through is an **automatic FAIL**, however
fluent the explanation.

## The flip-twin discriminators (same op, opposite mechanism — score the pair, not the half)

For each flip pair, BOTH halves must be proven and they must yield **different** mechanisms. An
agent that returns the same verdict for both halves classified from text and FAILS the pair —
that is the core *classify-by-proving* proof. Both halves route to the **same op-slug** but the
**seed** (not the `.sql`) casts the deciding vote. The pairs and the data that flips each:

| pair | clean leg | flipped leg | the data that flips it | governing _index |
|---|---|---|---|---|
| make-mandatory | COL-03B empty → **1** | COL-03 / COL-03C populated → **gate-relaxation (4) or multi-phase (5)** | table-has-rows | tightening-class |
| narrow | COL-06B fits → **1** | COL-06 over-length → **3 / 5** | `MAX(LEN)` vs target | tightening-class |
| retype | COL-07B widen → **1** | COL-07 explicit → **5** | lossless vs lossy | multi-phase |
| create-FK | KEY-02 clean → **1** | KEY-03 orphan → **4 / 5** | orphan count | constraint-is-a-claim |
| add-unique | CON-02 unique → **1** | CON-02/IDX-02 dupes → **3** | duplicate count | constraint-is-a-claim |
| add-check | CON-03 satisfied → **1** | CON-03 violators → **3** | violation count | constraint-is-a-claim |
| temporal | AUD-01 new → **1** | AUD-02 populated → **5** | populated vs new | multi-phase |
| modify-index | IDX-02B include → **1** | IDX-02 unique dupes → **3** | uniqueness + dupes | constraint-is-a-claim |
| extract-to-lookup | STA-03 mapped → **5 proceeds** | STA-03N unmapped → **STOP** | total-mapping proof | multi-phase |
| merge-tables | STR-02 1:1 → **5 proceeds** | STR-02N 1:many → **STOP** | cardinality | multi-phase |
| nullable-add | COL-01 plain → **1** | TRAP-01N CDC → **+1, recreate** | CDC-enabled flag | cdc |

## The hardest gate (automatic fail if missed)

The **make-mandatory family** (`COL-03`, `COL-03B`, `COL-03C`) is the spine proof and now
carries the CORRECTED finding, owned by `_index/tightening-class`:

- `COL-03B` (EMPTY table) MUST publish clean — Mechanism 1.
- `COL-03` (populated, NULLs present) and `COL-03C` (populated, ZERO NULLs) MUST **both** still
  hit the Strict veto, because the guard is **table-has-rows, not column-has-NULLs**. An agent
  that claims a backfill (or a zero-NULL re-seed) yields a clean Mechanism 3/1 on a **populated**
  table — WITHOUT empirically discovering on the proving ground that it STILL vetoes — has
  **classified from the old recipe text** and the entire run FAILS. The pass requires the agent
  to (a) prove the backfill clears the NULLs (NULL probe = 0) AND (b) prove Strict STILL vetoes,
  AND (c) deliver the corrected verdict: a populated-table make-mandatory needs a conscious,
  documented gate-relaxation (proven-zero-NULL first) or multi-phase — not backfill-alone.

This is the single proof that the tree's *prove-don't-advise* thesis holds for that agent, and
that the agent discovers a finding that **contradicts its own skill text** rather than parroting
it. (If the `make-mandatory` op skill were to re-state the guard instead of pointing to
`_index/tightening-class`, the discovery becomes rote — flag any such regression against the op
skill body.)

### Why the guard vetoes a zero-NULL populated table (the mechanism to confirm)

Sqlpackage generates the `NULL -> NOT NULL` guard as
`IF EXISTS (SELECT TOP 1 1 FROM [dbo].[Customer]) RAISERROR (…,16,127)` placed **before** the
`ALTER COLUMN`. It fires on the table having *any* row, never inspects the column, because SSDT
computes the deploy script **once, up front, from the pre-publish state** and is conservative by
design — it cannot know your pre-deploy backfill (which runs at deploy time, after the script is
already generated) will have emptied the NULLs. The corrected developer-facing magic line:

> "You said make Email mandatory. I published that to a copy of your data: SSDT vetoed it — and
> when I read the generated script, the guard is `IF EXISTS (SELECT TOP 1 1 FROM Customer)
> RAISERROR(…)` placed *before* the ALTER. That's **table-has-rows, not NULL-has-rows** — SSDT
> builds the deploy script up front and can't know I'll backfill, so it refuses the moment the
> table has any rows. I proved it: I backfilled every NULL (0 remain) and Strict STILL vetoed and
> left the column nullable. So on your populated table this isn't a clean backfill-then-NOT-NULL —
> it needs a conscious call: either I deliberately relax BlockOnPossibleDataLoss for this one
> change *after* proving zero NULLs (a logged, script-only gate decision), or we stage it
> multi-phase. Here is the proof for the path you choose — Tier 2 (+1 if CDC). On an EMPTY table
> it would have been a clean one-liner, Tier 1; the difference is entirely the rows."

---

## The fitness methodology (how a run is scored against the real engine)

The suite is a *fitness function* over the whole change tree — agent files, per-op skills,
`_index` concern skills, capability skills, and the proving ground together. A run of the fleet
(`PROTOCOL.md`, parallel + isolated) produces one scoring row per prompt; the aggregate of those
rows is the tree's fitness. Every metric is scored **against the real engine** — the actual
`sqlpackage` delta / veto on a real isolated DB — never against a description of what the engine
would do. That is the whole point: the engine is the oracle.

### The seven metrics (each per-prompt, then aggregated)

| metric | what it measures | how it is scored against the real engine | aggregation |
|---|---|---|---|
| **Mechanism accuracy** | did the agent return the correct Mechanism (1–5) + release bucket? | compare the agent's verdict to the mechanism the **real** delta/veto implies — a clean Strict publish ⇒ 1; a data-loss veto cleared by a pre-deploy step ⇒ 3; the corrected make-mandatory verdict ⇒ gate-relaxation(4)/multi-phase(5). The engine's behavior, not the expected-column, is ground truth when they disagree (then fix the prompt). | % of positive+flip prompts correct |
| **Tier accuracy** | correct danger grade 1–4 **and** every +1 named | the +1 triggers (CDC-enabled, >1M, first-time) are facts about the seed/estate, checked directly; danger-≠-release-count must hold (a single-PR Tier-4 drop counts as a miss if graded by release count) | % correct, +1 escalations counted separately |
| **Veto-prediction** | did the agent PREDICT the Strict veto (via the probe) before proving it? | the pre-publish probe (NULL count / `MAX(LEN)` / orphan LEFT JOIN / dup GROUP BY / violation WHERE) must have been run and its result must MATCH the real Strict veto that follows. Predicted-and-confirmed = full; proven-but-not-predicted = partial; neither = miss | % of veto-bearing cases predicted-then-confirmed |
| **Negative-case refusal-correctness** | for every `…N` case, did the refusal/veto/escalation fire correctly? | binary against the real engine: the veto/catastrophe was proven (BlockOnPossibleDataLoss row count, DROP+CREATE in the delta, `is_not_trusted=1`, 1:many row counts, unmapped-value rows) AND a safe alternative proposed AND the fail mode named. Pushing the change through = automatic 0 | ALL negatives must pass for an aggregate PASS |
| **Flip-discriminator** | for each twin, did both halves prove and yield DIFFERENT mechanisms? | run both legs on their two seeds against the real engine; PASS only if the two real outcomes differ AND the agent's two verdicts differ accordingly. Same verdict for both halves = classified-from-text = fail the pair | per-pair PASS/FAIL; make-mandatory pair is gating |
| **Token cost** | rough tokens to verdict | recorded per prompt; cheaper is better but **never** at the cost of skipping the proof — a cheap verdict that skipped the publish scores 0 on metric 2 regardless of token count. Use to compare skill-body revisions, not as a gate | median + p90 across the run |
| **Reasoning-surfaced** | criterion 6 — did the agent teach the WHY from the governing `_index` skill? | check the surfaced reasoning names the correct `_index` principle (the prompt's `_index` column) specialized to the op, AND names the fail mode avoided. Right label + no/ wrong-source reasoning = scored lower than right label + correct-source reasoning | % of positives with correct-source reasoning |

### Scoring each metric against the real engine (the procedure)

For each prompt, the executor (per `PROTOCOL.md`) has already produced the real artifacts on its
isolated DB: the `/Action:Script` delta (`$SCRATCH/bin/delta.sql`), the Strict publish result
(clean or a named veto with row counts), and — on a veto — the Permissive before/after hash diff.
Scoring reads THOSE artifacts, not the agent's prose:

1. **Mechanism / Tier** — read the real delta + veto and derive the mechanism the engine actually
   forces; compare to the agent's stated Mechanism+Tier. Engine wins ties; if the engine
   contradicts the prompt's expected column, the prompt is stale — record it and fix the prompt in
   the same pass (the suite is self-correcting against the engine).
2. **Veto-prediction** — confirm the probe SQL in the agent's transcript ran BEFORE the publish and
   its count matches the veto. A veto discovered only by publishing (no prior probe) is partial.
3. **Negative refusal** — confirm the proof artifact for the refusal exists (the row count, the
   DROP+CREATE line, the `is_not_trusted` value) and the change was NOT published through.
4. **Flip-discriminator** — the pair is scored together: both delta/veto artifacts must differ and
   both agent verdicts must differ.
5. **Reasoning** — match the surfaced WHY to the prompt's `_index` column.
6. **Token cost** — from the transcript length to first verdict.

### The fitness verdict feeds the SKILL bodies, not the agent

A failing metric localizes to a **surface**, and the fix lands in that surface's file (all under
`ssdt-agent/`), never in a one-off patch to the run:

- Mechanism/Tier miss on a specific op → the **per-op skill** (`skills/op/<slug>/SKILL.md`) — its
  default/flip table is wrong.
- Reasoning miss (criterion 6) or a re-stated lifted concern → the **`_index` skill** (owner drift)
  or the per-op skill's pointer (it re-explained instead of pointing).
- Veto-prediction miss → the **capability skill** `skills/prove-on-dacpac` (the probe reflex) or
  `skills/talk-to-local-sql` (the probe SQL).
- Wrong op-slug chosen (criterion 1) → `skills/confirm-intent` (the disambiguation table) or the op
  skill's frontmatter `description` (its trigger phrases didn't fire).
- Both-axes miss → `skills/classify-mechanism` (it owns the two-axes + cascade authoritatively).

## Scoring sheet (per prompt)

| field                          | what to record                                                       |
|--------------------------------|----------------------------------------------------------------------|
| Op-slug chosen correct?        | matches the prompt's `op` (`skills/op/<slug>`); note wrong-slug misses |
| Mechanism correct?             | matches the real engine (incl. IF/otherwise on renames; the corrected make-mandatory verdict) |
| Tier correct?                  | matches expected, including any +1 escalation, named explicitly      |
| Veto predicted then confirmed? | probe ran BEFORE publish and its count matched the real veto         |
| Proof artifact present?        | a real delta / veto text with row counts / hash diff was produced    |
| Which state-variable flipped?  | populated / violates / CDC-no-gap / coexist — name the one that drove it |
| Named trap caught?             | yes/no, and caught **in the delta** vs after                         |
| Magic line delivered?          | proven verdict + specific remedy + clean Strict re-run (or proven refusal) |
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
  drop its DB / delete its scratch on exit (leaks degrade the warm container — survival rule 2),
  or ran `sp_cdc_enable_db` against the shared warm instance. Automatic flag.
- **Voice violation:** the agent addressed the developer as if they were in Service Studio ("In
  Service Studio you would…") instead of producing developer-facing words from an agent-facing
  stance. Note it.
- **Concern-duplication defect (skill-body):** a per-op skill re-explained a lifted concern (the
  tightening guard, refactorlog identity, coexistence, CDC tax, constraint-claim, idempotent seed)
  instead of pointing to its `_index` owner. Not a run failure, but a first-class skill defect —
  record it so the op skill gets corrected; it is what makes criterion-6 reasoning drift over time.

## Running the full suite (via PROTOCOL.md)

The suite is designed for a **parallel, isolated** fleet — see `PROTOCOL.md` for the mechanics.
The full run:

1. **Dispatch** one executor per prompt id. Each executor gets a fixed `(TESTID, DB, SCRATCH)`
   from the orchestrator (never re-rolls `openssl rand` mid-run — that leaks the first DB).
2. **Isolation is at the DB + filesystem-copy grain:** every executor owns a unique
   `PG_<testId>_<rand>` database and a private scratch copy of the proving ground, so no two touch
   the same `.sql`, `bin/`, or DB. The authored tree is read-only. This is what lets a hundred
   provers run at once.
3. **Serialize the CDC family** (`AUD-04`, `AUD-05`, `AUD-07N`, `TRAP-01N`, and any CDC-leg of
   another case): `sp_cdc_enable_db` is instance-wide, and the container's single capture/cleanup
   Agent is a shared throughput resource (PROTOCOL §8) — run them one at a time, or poll-with-timeout
   for capture rather than asserting immediate capture. Do not claim CDC is fully parallel-safe.
4. **Score the flip twins as pairs**, not halves — dispatch both legs, then score the pair with the
   flip-discriminator metric. The make-mandatory triple (COL-03/03B/03C) is the gating pair.
5. **Each executor tears down unconditionally on exit** (drop-if-exists DB + `rm -rf` scratch) so
   accumulation stays at zero (survival rule 2).
6. **If a batch of connection failures appears**, it is the warm container degrading, not a
   regression — `scripts/warm-sql.sh restart`, resume from PROTOCOL step 3.
7. **Aggregate** the per-prompt rows into the seven metrics; produce the fitness verdict below.

## Aggregate verdict

A run is a **PASS** only if:

- the **make-mandatory family** gate is met (COL-03B clean Mechanism 1; COL-03 + COL-03C both
  still veto with the table-has-rows finding **empirically discovered**, not parroted), AND
- **every negative case refused safely** (negative-case refusal-correctness = 100%), AND
- **every flip twin yielded different mechanisms** for its two legs (flip-discriminator = 100% on
  the pairs), AND
- **≥ 90% of the positive prompts pass all six criteria** (criterion 6 — correct-source surfaced
  reasoning — counted), AND
- **zero hard-constraint violations**.

Anything less is a **FAIL** with the specific gap recorded and localized to its owning surface (the
per-op skill / the `_index` skill / a capability skill / `confirm-intent` / `classify-mechanism`),
so the **skill body** (not the agent) is corrected. The suite is the tree's fitness function: a
failing metric is a bug in a named file, and the fix is a commit to that file under `ssdt-agent/`.
