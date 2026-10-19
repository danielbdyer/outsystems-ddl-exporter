# Handoff — Finish the staged-MERGE feature (steps 4–6) + Typed-AST refactor

**For:** a fresh agent in a new context window.
**Branch context:** `claude/staged-data-merge` (the staged-`#temp` MERGE work). This handoff folds that branch's pending "typed-wrapper" task into a broader, fleet-discovered refactor.
**Date:** 2026-06-25.

---

## ✅ COMPLETE — 2026-06-27 (branch `claude/finish-typed-ast-refactor`)

The fleet-ranked refactor below is **done**; Part A's witnesses landed earlier with
the staged-MERGE work. Final state (pure pool **3692/0**, Docker **273/273**):

- **Part A** — staged-MERGE steps 4–6: DONE (set-based phase-2 wired across all 3
  lanes; `emission.dataStaging` config knob with `mode`/`threshold`/`indexThreshold`;
  Step-6 witnesses all present — the >threshold staged-deploy regression E2Es
  (migration/static/self-ref, idempotent re-deploy), the measured-`indexThreshold`
  clustered-`#temp`-index E2E, and the atomicity "mid-batch failure rolls back —
  target untouched, no `#temp` survives" E2E).
- **Tier 0** — `%A`-to-output: DONE (no `%A` reaches output; only diagnostic messages).
- **Tier 1** — the shared `buildAtomicBatch` (1.1/1.2) landed earlier; **Tier 1.3**
  (the two parse-back guards `buildUpdateFromTemp` + `buildValidateBeforeApplyGuard`)
  now fully typed — no `sprintf`→`Parse` middle path; the set-based UPDATE reuses the
  shared `changeDetectionPredicate` (string-WHERE duplicate deleted). Deploy-verified.
- **Tier 2** — **2.1 SurrogateCapture** (the 8 raw `sprintf` capture sites → typed
  `buildCaptureStaging`/`buildKeymapStaging`/`buildCaptureMerge` (OUTPUT + OUTPUT…INTO
  + DEFAULT VALUES)/`buildSelectColumnsFromTemp`/`buildInsertDefaultValues`/
  `buildScopeIdentitySelect`) and **2.2 KeymapSpill** (`buildKeymapSpillTable` +
  `buildKeymapRepoint`, `@kind` still a bound parameter) — both typed + deploy-verified
  (the byte-identical-sink KeymapSpill equivalence + reverse-leg capture canaries).
  13 render-witness unit tests.
- **Tier 3** — 3.1 (`TransferRun.normalizedKey`) already done; **3.2** audited CLEAN
  (the one path-shaped concat is the *intentional* forward-slash SSDT relpath; Compose
  uses `Path.Combine`); **3.3** SQL-path magic strings are now `[<Literal>]`; **3.4**
  `CREATE DATABASE` → typed `CreateDatabaseStatement`, the scratch-container teardown's
  `SET SINGLE_USER WITH ROLLBACK IMMEDIATE` left as an annotated 4-question `LINT-ALLOW`
  (ScriptDom 161 has no clean typed node — `DatabaseOptionKind` omits it; non-production,
  best-effort, `]`-safe); **3.5** confirmed legit (prose concat, not structured).

**One open micro-decision (raised to the operator):** whether to push the Tier-3.4
teardown `SET SINGLE_USER` to the sanctioned parse-template idiom, or keep the annotated
terminal (the plan's own "do only if cheap" guidance → keep). Defaulting to keep.

The original plan is preserved below for provenance.

---

## 0. Why this exists (read first)

This codebase (`sidecar/projection`, F#) is **typed-AST-first**: T-SQL is emitted via Microsoft.SqlServer.TransactSql.ScriptDom (`ScriptDomBuild.fs` builds typed statements; `ScriptDomGenerate.generateOne` renders them); JSON via `System.Text.Json` (`Utf8JsonWriter`/`JsonNode`); XML via `System.Xml.XmlWriter`. Raw string-building of any of these is a **named anti-pattern**, allowed only at terminal boundaries with a `// LINT-ALLOW` comment.

**The trigger:** the staged-MERGE feature added hand-written SQL — `StaticSeedsEmitter.wrapAtomicBatch` (concatenates `SET XACT_ABORT ON; BEGIN TRY; …` as string literals) and `ScriptDomBuild.buildUpdateFromTemp` (sprintf → parse-back). A **real bug shipped and passed unit tests**: `"#seed_" + k.Physical.Table` stringified a `TableName` value object into SQL as `#seed_TableName "STG_BIG"` — a hard syntax error — caught **only by deploying it**. Fix was the VO accessor `TableId.tableText`.

**Two non-negotiable lessons baked into this plan:**
1. **Verify by DEPLOY, not by unit-test substrings.** Every SQL-emitting change must be re-run through the gated scale probe (`MergeScaleMeasurement`, `PROJECTION_MEASURE_SCALE=1`) or a Docker E2E. Substring asserts (`Assert.Contains "CREATE TABLE [#seed_"`) are green even when the full statement is malformed.
2. **Prefer typed construction over string-then-parse over raw concat**, and **drive built-ins/library/AST up the value scale**, hardest on the critical (emit→deploy) path.

A 4-agent fleet (2 blue = opportunities, 2 red = hazards) swept the tree. Verdict: **JSON/XML emission is already exemplary** (don't refactor it).

**Two bodies of work:** **Part A** — finish the staged-MERGE feature (steps 4–6 of the original plan; this is the in-flight feature). **Part B** — the fleet-ranked typed-AST / built-in refactor (Tiers 0–3). They overlap at one point (Step 4 ⇄ Tier 1); see the note at the end of Part A.

---

## Part A — Finish the staged-MERGE feature (steps 4–6)

The branch landed **phase-1 staging, proven to deploy at 25k/100k/250k** (the error-8623 wall is gone; flat ~6k rows/sec). Three planned steps remain. Do these to COMPLETE the feature.

### Step 4 — wire the set-based phase-2 (`buildUpdateFromTemp` is already built + unit-tested)
For a LARGE *cyclic* kind (deferred FKs **and** > threshold rows), phase-2 currently emits N per-row `UPDATE`s. Above the SAME `stagingRowThreshold`, escalate to one set-based UPDATE (the deterministic rule the operator locked: one threshold, both phases, so a kind is treated coherently).
- Add `renderStagedPhase2` in `Projection.Targets.Data/StaticSeedsEmitter.fs` (mirror `renderStagedPhase1`): stage a NARROW `#temp` named `#fk_<table>` of PK + deferred-FK columns carrying the **real** FK values (NOT the phase-1 NULL form — project `typedRows` over PK+deferred without `typedValuesToSqlLiterals`'s deferred-NULLing), then `ScriptDomBuild.buildUpdateFromTemp table tempName setCols pkCols cdcAware`, wrapped in `wrapAtomicBatch`. Reuse `stagingColumnDefsOf` for the narrow columns. Use `TableId.tableText k.Physical` for the temp name (NOT `k.Physical.Table` — that VO-stringification was the shipped bug).
- Branch `kindToScript`'s phase-2 (the `renderedPhase2` `let`, ~line 375): `elif List.length typedRows > stagingRowThreshold then renderStagedPhase2 …` else the current per-row loop.
- **Verify by DEPLOY** (lesson #1): a > threshold self-referential kind must deploy AND its children's FKs must land in phase-2 — extend the scale probe with a cyclic case, or add a Docker E2E (`SsdtDataBehaviorE2ETests` shape). Unit substrings are NOT sufficient.

### Step 5 — the config knob `emission.dataStaging`
Replace the hardcoded `stagingRowThreshold = 1000` with config.
- `Projection.Pipeline/Config.fs` `EmissionSection` + parser (mirror the `dacpac`/`sqlproj`/`deleteScope` rungs): `emission.dataStaging : { "mode": "auto" | "inline" | "tempTable"; "threshold": int }` (default `auto`, 1000). Thread → `EmissionPolicy` (a new field) → the emitter (this path takes no policy today; thread it the way `cdcAware`/`deleteScope` are).
- **Portability stance (the J5 / on-prem-permissions reality — see memory `j5-cloud-uat-ledger`):** the `#temp` + transaction need only baseline rights (temp-table creation + `BEGIN TRAN`; `IDENTITY_INSERT` is unchanged from today), so it's portable wherever the current identity-seed path runs. But make it **config-gated, never forced** — an operator on a locked-down/managed env can pin `inline` (accepting the ~30k 8623 ceiling). Surface the choice as a **named run-report line** (capability-descent doctrine: "staged" vs "stayed inline" must be auditable). Managed envs may route writes differently (`AssignedBySink`) and never execute this path — don't assume it runs everywhere.
- Document in `CONFIG_REFERENCE.md` + `examples/projection.sample.json`.

### Step 6 — witnesses (make the deploy-proof DURABLE) + the measured index
1. **Permanent staged-deploy regression E2E** — the temp-name lesson made durable: a small > threshold kind (~1500 rows) deploys to a real server (Docker pool) and asserts the rows land + idempotency. This is what would have caught the `TableName`-VO bug at CI time; substring unit tests didn't.
2. **The measured `indexThreshold`** (the reverse-leg-at-scale concern the operator raised): A/B the scale probe with a `CREATE CLUSTERED INDEX` on the `#temp` PK (built AFTER the load, dropped WITH the table) vs without, across 100k → 1M; find the crossover where the MERGE-join speedup beats the index-build cost; bake it as `emission.dataStaging.indexThreshold` (separate, higher than the staging threshold). **Until measured, ship NO index** (don't add a speculative one — that discipline already removed one).
3. **Atomicity E2E** — inject a mid-batch failure (e.g. a duplicate-PK row) into a staged load; assert the target is **untouched** (rollback) and **no `#temp` survives** (the `XACT_ABORT` + `TRY/CATCH` + pooled-connection-reset guarantees).

> **Part A ⇄ Part B overlap:** Step 4 adds a second `wrapAtomicBatch` caller, and Part B **Tier 1** replaces `wrapAtomicBatch` (string-concat) with a typed `buildAtomicBatch`. Do **Tier 1 together with Step 4** so phase-1 and phase-2 share the typed builder from the start (don't build phase-2 on the concat wrapper you're about to delete).

---

## Part B — Typed-AST / built-in refactor (fleet-ranked)

The real gaps are below, ranked.

## TIER 0 — Confirmed latent fragility, NON-SQL, cheap (do first)

| # | File:Line | Smell | Fix | Why it matters | Effort |
|---|---|---|---|---|---|
| 0.1 | `Projection.Targets.SSDT/ManifestEmitter.fs:919, 935` | `JsonValue.Create(sprintf "%A" axis)` where `axis : OverlayAxis` — `%A` to serialize a DU into **durable JSON** | `OverlayAxis.name axis` (the canonical accessor; already exists, exhaustive-matched) | `%A` shape is **F#-compiler-version-dependent** and is explicitly **forbidden for output** elsewhere (`TransformRegistry.fs:457`). Works today only because case names == tokens; a new `OverlayAxis` variant or compiler bump silently corrupts the manifest, which **round-trips through `LifecycleStore`**. | S |
| 0.2 | (sweep) | Any other `sprintf "%A"` / `%O` / `string <du>` whose result reaches **output / an identifier / a key** (vs a diagnostic/exception message) | The DU's canonical accessor (e.g. `*.name` / `*.value`) | Same class. The fleet found only 0.1 as a concrete output hazard, but a `grep "%A"` pass over `Targets.*` + `Pipeline` output paths is cheap insurance. | S |

**This is the "don't only bias to SQL" tier — start here.**

---

## TIER 1 — One shared typed transaction-envelope builder (SQL, critical path, highest value)

The **same** `SET XACT_ABORT ON; BEGIN TRY; BEGIN TRAN; … COMMIT; END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; THROW; END CATCH` pattern is hand-written as strings in **three** places. Build it ONCE, typed, and have all three call it.

| # | File:Line | Smell | Fix |
|---|---|---|---|
| 1.1 | `Projection.Pipeline/MigrationRun.fs:624, 635, 648` | The transaction envelope (M22 atomic deploy) as string literals | a new typed builder (below) |
| 1.2 | `Projection.Targets.Data/StaticSeedsEmitter.fs` `wrapAtomicBatch` *(this branch)* | the IDENTICAL envelope, string-built | the same typed builder |
| 1.3 | `Projection.Targets.SSDT/ScriptDomBuild.fs:1071-1089` `buildValidateBeforeApplyGuard` **and** the new `buildUpdateFromTemp` | sprintf a SQL template, then `threadLocalParser.Parse` it back (a "middle path") | fully typed nodes |

**The build:** add `buildAtomicBatch (inner: TSqlStatement list) : TSqlStatement` (or a small `TSqlScript`) to `ScriptDomBuild.fs` using the typed nodes ScriptDom already ships:
`PredicateSetStatement` (SET XACT_ABORT), `TryCatchStatement` (`TryStatements`/`CatchStatements`), `BeginTransactionStatement`, `CommitTransactionStatement`, `RollbackTransactionStatement`, `ThrowStatement`, `IfStatement` (for `@@TRANCOUNT > 0` via `GlobalVariableExpression`), `DropTableStatement` (for the `#temp` cleanup), and an `IfStatement` over `OBJECT_ID(...) IS NOT NULL` for the drop-if-exists. The guard (1.3) becomes a typed `IfStatement` + `BooleanBinaryExpression(AND/OR)` over `ExistsExpression`s wrapping a `ThrowStatement`; the EXCEPT sides are `SelectStatement`s.

**⚠ THE WRINKLE (verify, don't assume):** `generateOne` omits the trailing `;` after a *bare* `MERGE` (the inline path works around it by hand). Inside a multi-statement `StatementList` it *should* terminate it, but **confirm by rendering AND deploying** (Tier-0 lesson #1). If the generator still drops it, the typed batch is invalid SQL and must handle the terminator explicitly.

**Priority:** P0 (deploy-critical). **Effort:** M–L. **Payoff:** removes the brittlest, highest-blast-radius string SQL in the codebase AND deletes a duplicated pattern.

---

## TIER 2 — Ephemeral-table DDL templates (SQL, P1)

| # | File:Line | Smell | Fix | Effort |
|---|---|---|---|---|
| 2.1 | `Projection.Pipeline/SurrogateCapture.fs:124-128, 132, 139-141` | `SELECT TOP 0 … INTO #cap`, `IF OBJECT_ID(…) DROP`, `INSERT … VALUES` via sprintf + `String.concat` | `SelectStatement`(+`TopRowFilter`/`IntoClause`), `IfStatement`+`DropTableStatement`, `InsertStatement` | M |
| 2.2 | `Projection.Pipeline/KeymapSpill.fs:77-79, 93-99` | keymap session-table DDL hard-coded as a string template (schema baked in) | `IfStatement`+`CreateTableStatement` (the row-VALUES at :93 are already parameterized — leave those) | M |

These are real (transfer/remap lanes) but ephemeral session tables; same discipline as Tier 1, lower blast radius.

---

## TIER 3 — Built-in/library smells beyond SQL (P2 — audits + low-risk)

| # | File:Line | Smell | Fix |
|---|---|---|---|
| 3.1 | `Projection.Pipeline/TransferRun.fs:650` | `(schemaText …).ToLowerInvariant() + "." + (tableText …).ToLowerInvariant()` — string `+` (the smell that harbingered the temp-name bug) | a `TableId.normalizedKey` accessor or a `(schema, table)` record key; `String.concat` at minimum |
| 3.2 | (sweep) | hand-built **file paths** via `+` / `String.Concat` | `System.IO.Path.Combine` — audit `Pipeline` (`Compose`, `ArtifactPath`, writers) |
| 3.3 | (sweep) | repeated magic strings / format strings | `[<Literal>]` constants |
| 3.4 | `Projection.Pipeline/Deploy.fs:174-175, 956-958` | `CREATE DATABASE [x]` / `ALTER DATABASE … SET SINGLE_USER` via `String.Concat` | `CreateDatabaseStatement` / `AlterDatabaseStatement` — low value (one-off ephemeral container ops); do only if cheap |
| 3.5 | `Projection.Targets.SSDT/DecisionLogEmitter.fs:148` | `String.concat "\n"` on diagnostic codes — **cosmetic prose, not structured**; SKIP unless trivial | — |

---

## What NOT to touch
- **JSON codecs / `Utf8JsonWriter` / `CatalogCodec` / `DistributionsEmitter` / `JsonEmitter`** — already typed end-to-end (Blue-Struct: zero P0/P1).
- **`RefactorLogRender` / `SqlprojEmitter` / `PostDeployEmitter` XML** — already `XmlWriter`-typed.
- **`%A` inside exception/diagnostic messages** — legitimate; only `%A`-in-*output* is the hazard.
- **Report/`.md`/`.txt` prose lines** — prose, not structured; concat is fine.

## Execution order & discipline
1. Tier 0 (cheap, non-SQL, latent-bug) → commit, run pure pool.
2. Tier 1 (the shared typed envelope) → **render-then-DEPLOY-verify** (scale probe + a Docker E2E) before trusting; goldens must stay byte-identical for any below-threshold path.
3. Tier 2, then Tier 3 audits.
- Each tier: byte-identical goldens where applicable, pure pool `3632/0`, and for any SQL change a **real deploy**. Drive ASTs/built-ins up; never ship hand-assembled SQL/JSON/XML unverified by deploy.
