# Handoff — Typed-AST / Built-in Refactor (ranked, fleet-sourced)

**For:** a fresh agent in a new context window.
**Branch context:** `claude/staged-data-merge` (the staged-`#temp` MERGE work). This handoff folds that branch's pending "typed-wrapper" task into a broader, fleet-discovered refactor.
**Date:** 2026-06-25.

---

## 0. Why this exists (read first)

This codebase (`sidecar/projection`, F#) is **typed-AST-first**: T-SQL is emitted via Microsoft.SqlServer.TransactSql.ScriptDom (`ScriptDomBuild.fs` builds typed statements; `ScriptDomGenerate.generateOne` renders them); JSON via `System.Text.Json` (`Utf8JsonWriter`/`JsonNode`); XML via `System.Xml.XmlWriter`. Raw string-building of any of these is a **named anti-pattern**, allowed only at terminal boundaries with a `// LINT-ALLOW` comment.

**The trigger:** the staged-MERGE feature added hand-written SQL — `StaticSeedsEmitter.wrapAtomicBatch` (concatenates `SET XACT_ABORT ON; BEGIN TRY; …` as string literals) and `ScriptDomBuild.buildUpdateFromTemp` (sprintf → parse-back). A **real bug shipped and passed unit tests**: `"#seed_" + k.Physical.Table` stringified a `TableName` value object into SQL as `#seed_TableName "STG_BIG"` — a hard syntax error — caught **only by deploying it**. Fix was the VO accessor `TableId.tableText`.

**Two non-negotiable lessons baked into this plan:**
1. **Verify by DEPLOY, not by unit-test substrings.** Every SQL-emitting change must be re-run through the gated scale probe (`MergeScaleMeasurement`, `PROJECTION_MEASURE_SCALE=1`) or a Docker E2E. Substring asserts (`Assert.Contains "CREATE TABLE [#seed_"`) are green even when the full statement is malformed.
2. **Prefer typed construction over string-then-parse over raw concat**, and **drive built-ins/library/AST up the value scale**, hardest on the critical (emit→deploy) path.

A 4-agent fleet (2 blue = opportunities, 2 red = hazards) swept the tree. Verdict: **JSON/XML emission is already exemplary** (don't refactor it). The real gaps are below, ranked.

---

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
