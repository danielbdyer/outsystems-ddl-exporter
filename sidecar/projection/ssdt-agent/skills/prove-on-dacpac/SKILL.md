---
name: prove-on-dacpac
description: Use when a data-model change has a provisional classification from classify-mechanism and must be confirmed (or flipped) against real-shaped data. Builds the .sqlproj to a dacpac, previews the real SSDT-generated delta, then publishes to a disposable copy of the Dev database under a Strict profile (which surfaces the block) and a Permissive profile (which proceeds past it to reveal the consequence) to discover whether the data blocks the change. Invoke whenever classification depends on table state — populated? rule-violating rows? — which is ALWAYS for anything past the purely-additive corner. Commands are scaffolded here; the developer's agent runs them.
---

# Prove on dacpac

> **Why this.** The block is the classification. SSDT's publish engine computes the real delta
> against the real rows, so its refusal is more reliable than any rule table read from the `.sql`
> text alone. Proving replaces advising because the `.sql` text cannot reveal how a change must
> ship — only the data can, and the moment SSDT refuses, the shipping shape is settled. A
> green-looking schema diff and a clean data probe are *necessary but not sufficient*; the deciding
> evidence is what the engine actually does on publish. The sharpest result below: a
> `NULL -> NOT NULL` block is **table-has-rows, not column-has-NULLs**, so backfilling every NULL
> does not clear it. This is worth surfacing to the developer, who then relies on the proof rather
> than on a reading of the SQL.

This skill helps an **OutSystems-native developer** land a safe schema change. The classification
handed over is **provisional** — a reading of intent. **Proving is classifying:** the data decides
how the change must ship, and the only way to know the data is to publish the change to a disposable
copy of it and read what SSDT's publish engine actually does.

This is the capability the team's decks never taught: a disposable copy of the Dev database —
real-shaped data — with `sqlpackage` driving the real dacpac delta against it. Introduce this
vocabulary **gently**, always tied back to a phrase the team already owns:
**"You describe the destination, SSDT computes the journey."** The dacpac is the described
destination; `sqlpackage` computes the journey; the disposable copy shows the journey before it
ever runs on production.

## When to use

- `classify-mechanism` handed over a provisional shipping shape and review need, marked
  **must-prove**.
- The verdict depends on any of the four state-variables (populated / violates / CDC-no-gap /
  coexist). Prove before telling the developer anything.
- A named trap is suspected (especially a **rename with no refactorlog entry** — always read the
  delta).

If `classify-mechanism` said **on-sight** (the purely-additive corner — additive, application
unaffected), still run one clean Strict publish to confirm no surprise delta — but no flip is
possible.

## Running in parallel — see self-test/PROTOCOL.md

When MANY executors prove cases at once (the self-test fleet), do **not** publish to the shared
default `ProvingGround` catalog and do **not** edit the authored `proving-ground/` tree. Each
executor copies the tree to its own scratch dir and publishes to a **unique database**
(`/TargetDatabaseName:PG_<testId>_<rand>`, which overrides the profile's `Initial Catalog`), then
drops the DB and deletes the scratch on exit. The full parallel-safe, idempotent isolation
protocol is **`self-test/PROTOCOL.md`** — read it before running any prove loop alongside other
executors. (Single-developer, one-at-a-time use can target `ProvingGround` directly per
`talk-to-local-sql`.)

## The vocabulary to introduce (gently)

| Term | What it is | Tie it back to |
|---|---|---|
| **dacpac** | the compiled "destination" (the edited CREATEs, built) | "describe the destination" |
| **sqlpackage** | the tool that computes + applies the delta | "SSDT computes the journey" |
| **publish profile** | the rules SSDT publishes under (a `.publish.xml`) | the team's prod safety settings |
| **BlockOnPossibleDataLoss** | the **block** — refuses any step that could lose data | "it stops the change before it can hurt the data" |
| **GenerateSmartDefaults** | the **silent backfill** — stamps a default into NOT-NULL columns | "what it would have done quietly" |
| **disposable copy** | a disposable copy of real-shaped data to publish against | "a copy of Dev, safe to throw away" |

## The proving loop (scaffolded here; the developer's agent runs each command)

> All commands assume the repo root as the working directory and a warm disposable copy — see
> `talk-to-local-sql` for the container, the connection string, and the **runtime shim** (the
> `DOTNET_ROOT` + `DOTNET_ROLL_FORWARD=Major` exports are **required on this machine**, because
> `sqlpackage` targets .NET 8 and the box has .NET 9 at a non-standard path). On Git Bash also
> export `MSYS_NO_PATHCONV=1` so the `/Action:` / `/SourceFile:` switches and any `/opt/...`
> docker-exec paths are not mangled.

```bash
# 0. Runtime shim (REQUIRED here) + warm DB — details in talk-to-local-sql
export DOTNET_ROOT="C:/Users/danny/AppData/Local/Microsoft/dotnet"
export DOTNET_ROLL_FORWARD=Major
export MSYS_NO_PATHCONV=1                  # Git Bash: keep /Action: + /opt/... paths intact
scripts/warm-sql.sh start                 # container projection-mssql-warm, localhost,11433

# 1. Establish the BEFORE state once: deploy current CREATEs + seed (the 'real-shaped data')
#    (see proving-ground/README.md — first Strict publish of the unedited project + post-deploy seed)

# 2. Edit the CREATE to the destination in proving-ground/Modules/*.sql. NEVER write ALTER.
#    "Edit the CREATE, never write ALTER."

# 3. BUILD the destination to a dacpac
dotnet build ssdt-agent/proving-ground/SampleCatalog.sqlproj -c Release
#    -> ssdt-agent/proving-ground/bin/Release/SampleCatalog.dacpac

# 4. PREVIEW the REAL delta (changes NOTHING — this is the script SSDT would run)
sqlpackage /Action:Script \
  /SourceFile:ssdt-agent/proving-ground/bin/Release/SampleCatalog.dacpac \
  /Profile:ssdt-agent/proving-ground/profiles/ProvingGround.Strict.publish.xml \
  /OutputPath:ssdt-agent/proving-ground/bin/Release/delta.sql

# 5. BLOCK CHECK — Strict surfaces the block. Does the data refuse the change?
sqlpackage /Action:Publish \
  /SourceFile:ssdt-agent/proving-ground/bin/Release/SampleCatalog.dacpac \
  /Profile:ssdt-agent/proving-ground/profiles/ProvingGround.Strict.publish.xml

# 6. ON A BLOCK ONLY — Permissive proceeds past the block, so what it was protecting against can be
#    snapshotted (see the content-hash check in talk-to-local-sql).
sqlpackage /Action:Publish \
  /SourceFile:ssdt-agent/proving-ground/bin/Release/SampleCatalog.dacpac \
  /Profile:ssdt-agent/proving-ground/profiles/ProvingGround.Permissive.publish.xml
```

> For a **parallel** run, every command above also carries `/TargetDatabaseName:PG_<testId>_<rand>`
> and points at a scratch copy of the tree — see `self-test/PROTOCOL.md`.

## Reading the result -> how it ships

**Read the generated `delta.sql` AND the Strict publish outcome together.** Map what they show:

- **Strict publishes clean, the delta is an in-place `ALTER` (or nothing), no block**
  -> **Ships as a single schema change, applied in place. No data is read or written.** Confirmed.
- **Strict blocks** with `BlockOnPossibleDataLoss` / "rows that would be affected" / NOT NULL on
  populated data / truncation -> the data **raised the review need and reshaped how it ships**. The
  remedy that clears the block is the proof:
  - backfill before the schema change -> **Ships as one release: a pre-deployment script prepares
    the data, then the schema change lands validated.** (But see the make-mandatory finding below —
    for `NULL -> NOT NULL` on a populated table, the backfill alone does **not** clear the block;
    that is the corrected finding.)
  - the fix is a post-deploy seed/reconcile -> **Ships as one release: the schema change, then a
    post-deployment script that runs after it lands.**
  - the change isn't declarative at all (FK `NOCHECK`->`WITH CHECK CHECK`, CDC, IDENTITY swap)
    -> **Ships as a scripted change — reconciling the foreign key / enabling CDC / the identity
    change cannot be expressed as a table definition.**
  - old+new app code must coexist, or a no-gap CDC rollout -> **Ships across multiple releases so
    the running application keeps working while the change is in flight.**
- **The delta shows `DROP <col/table>` + `CREATE` on an object requested as a RENAME**
  -> **a rename with no refactorlog entry. STOP.** The refactorlog entry is missing; publishing
  this drops the column and its data. Demand the refactorlog entry (it turns the DROP+CREATE into
  `sp_rename`), rebuild, re-preview, and confirm the delta is now a rename. This is the single most
  important read in the set.
- **The delta shows a shadow-table rebuild** (CREATE new table -> copy -> drop old -> rename) or
  **drop-by-absence** (an object vanishes because it's no longer in source) -> name it to the
  developer explicitly; these are expensive or destructive and were not requested.

## The make-mandatory finding: the guard is TABLE-HAS-ROWS, not column-has-NULLs (VERIFIED, sqlpackage 170.4.83)

This is the central result the disposable copy exists to surface, and it **corrects an earlier,
wrong recipe.** The old advice said "a pre-deploy backfill clears the NULLs, then the declarative
NOT NULL lands clean under Strict, shipping as one pre-deploy-then-schema release." **That was
disproven empirically here.**

For a `NULL -> NOT NULL` change, `sqlpackage` generates the `BlockOnPossibleDataLoss` guard as:

```sql
IF EXISTS (SELECT TOP 1 1 FROM [dbo].[Customer])
    RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127);
-- ... and BELOW it, the actual:
ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR(256) NOT NULL;
```

*(As emitted by sqlpackage 170.4.83.3 the guard reads lowercase and carries `WITH NOWAIT`; a
blocked publish surfaces it as `Error SQL72014` / `Msg 50000, Level 16, State 127`.)*

That guard fires on `IF EXISTS (... FROM Table)` — **the table merely having rows** — and is placed
**before** the `ALTER COLUMN`. It **does not inspect the `Email` column at all.** Why: **SSDT
computes the entire deploy script once, up front, from the pre-publish model state, and is
conservative by design** — it cannot know that a pre-deploy backfill (which runs at deploy time,
after the script is already generated) will have emptied the NULLs, so it refuses the moment the
table has any row. The gate cannot know the change's intent, so it assumes the worst.

**Proven on a disposable copy (sqlpackage 170.4.83):** a pre-deploy backfill cleared **all** NULLs
(a fresh `SELECT COUNT(*) WHERE Email IS NULL` returned **0**), and Strict **STILL blocked the
publish**, leaving the column nullable. **Zero NULLs is necessary but NOT sufficient** to pass the
prod-strict gate on a populated table.

The **honest, corrected recipe:**

- **EMPTY table** -> **ships as a single schema change, applied in place; any team member can review
  it.** No rows, the `IF EXISTS` is false, the RAISERROR never fires, the `ALTER COLUMN NOT NULL`
  lands. (Confirm the table is genuinely empty first.)
- **POPULATED table (NULLs present OR zero NULLs — it makes no difference)** -> the make-mandatory
  change **cannot pass the prod-strict gate by backfill alone.** It needs a **conscious, documented
  decision taken AFTER a verified backfill** (prove `COUNT(*) WHERE col IS NULL = 0` first —
  necessary, not sufficient), then ONE of:
  - **(a) Targeted gate-relaxation** — **ships as a scripted change with a named relaxation,**
    because relaxing the guard for one column cannot be expressed as a table definition. Having
    proven zero NULLs remain, deliberately disable `BlockOnPossibleDataLoss` for **this one targeted
    change** (a scoped profile override or a script-only path), so the `ALTER COLUMN NOT NULL`
    proceeds against the now-clean column. The proof packet must carry **both** the zero-NULL probe
    AND the explicit record of the relaxation decision.
  - **(b) Restructure to stage it across releases** — **ships across multiple releases so the
    running application keeps working** and the engine never has to relax its guard. A dev lead or
    an experienced developer must review this: the running application must change to keep working.
    Added scrutiny where it applies: the table feeds a change-data-capture stream, or holds more
    than a million rows, or the operation is a first on this estate.

**What the proof must EMPIRICALLY show (do not assert it — discover it):**

1. Edit `Email ... NULL` -> `NOT NULL`, build, Strict publish -> prove the block fires, and read
   the delta to **SEE** the `IF EXISTS(...) RAISERROR(...,16,127)` guard placed **above** the
   `ALTER COLUMN` (confirming table-has-rows).
2. Author the pre-deploy backfill, re-run the NULL probe -> prove `0` NULL emails remain.
3. Re-run Strict -> prove it **STILL blocks** and the column **stays nullable** — the backfill did
   not satisfy the gate. **This is the finding the section exists for.**
4. Deliver the corrected verdict and prove the chosen path — (a) named gate-relaxation after
   proven-zero-NULL, or (b) staged across releases — actually lands the NOT NULL.

This is the central proof that the tree's *prove-don't-advise* thesis holds: the finding surfaced
here **contradicts an earlier, wrong recipe** rather than parroting it.

## The FK findings: a blocked publish is non-atomic; a post-deploy seed re-plants the fix (VERIFIED, sqlpackage 170.4.83)

Two live-run findings on the foreign-key path, both proven on a disposable copy of Dev, both easy
to miss:

- **A blocked FK publish is non-atomic; the constraint can end untrusted.** When an orphan row makes
  SSDT block the publish (Msg 547 — conflicted with FOREIGN KEY constraint), the publish does not
  cleanly unwind: the `ADD CONSTRAINT` can land `WITH NOCHECK` while the follow-on
  `WITH CHECK CHECK` fails on the orphan, leaving the foreign key present but `is_not_trusted = 1`.
  A blocked publish is therefore not proof the copy is unchanged. **Always re-probe after any
  blocked publish:**

  ```sql
  SELECT name, is_not_trusted FROM sys.foreign_keys WHERE name = 'FK_Order_Customer';
  -- is_not_trusted = 1 means the constraint exists but was never validated.
  ```

  The trust ladder is the fix, in order: add `WITH NOCHECK`, reconcile the orphan rows, run
  `ALTER TABLE ... WITH CHECK CHECK CONSTRAINT`, then confirm `is_not_trusted = 0`.
- **A post-deployment seed re-plants a manually reconciled row.** Reassigning an orphan row on the
  copy by hand (Order 4, CustomerId 999 -> 1) clears the block once, but the post-deployment seed is
  an idempotent MERGE that re-inserts the original seeded rows on the next publish — re-planting
  CustomerId 999 and re-breaking the foreign key. **The reconcile must be durable at source:** fix
  the seed data (or the pre-deployment remediation script), not just the row on the copy. A reconcile
  that lives only on the disposable copy is undone by the next deploy.

## Strict vs Permissive — what the diff reveals

Strict and Permissive aim at the **same disposable copy** and differ on two settings
(`BlockOnPossibleDataLoss` and `GenerateSmartDefaults`). The difference between their outcomes is
the proof:

- **Strict refused, Permissive proceeded** -> the change *does* touch data. Snapshot the data
  **before vs after** the Permissive publish using the content-hash check (see
  `talk-to-local-sql`). The rows whose hash changed are exactly what `GenerateSmartDefaults`
  stamped, or what truncated. **That set is the exact row count the change would affect.**
- **Both proceeded clean** -> no data at risk; the Strict-clean result already proved the change
  ships as a single schema change, applied in place.
- A **rename** changes a row's hash **by design** (the column name is part of the row shape) — so a
  hash diff on a rename is *correct*, and confirms the rename touched the right rows. The diff IS
  the proof, not a false alarm.

## The content-hash check (the "did values change" question)

The content-hash check is a per-row `SHA2_256` over `(SELECT x.* FOR XML RAW)`, summed
**order-independently**
across the table (so row order doesn't matter), with **NULL kept distinct from `''`**. Snapshot it
before and after a Permissive publish; an unchanged total means no value moved. The exact SQL lives
in `talk-to-local-sql` (one query, reusable). Use it to **quantify** a flip — "1,240 rows would be
stamped" — not merely assert "data might change."

## Two named proof moves (scope them by op class)

Two moves the fitness runs invented and proved — use them where they fit, and do **not** fabricate
them where they cannot fire:

- **consequence check** — for a data-removing op that a principal must review because data is
  removed irreversibly (`delete-attribute`, `delete-entity`, `narrow` past the data, drop-table):
  after Strict blocks, run Permissive to let the irreversible act happen on the disposable copy and
  snapshot the exact rows and values that would be lost — so the claim that data is removed is
  **observed, not asserted.** (This is the Strict->Permissive pattern above, named.)
- **violating-row probe** — for a *constraint / tightening* op that has a data block (`define-pk`,
  `add-unique`, `add-check`, `create-fk-orphan`, `make-mandatory`): if the seeded data happens to be
  clean, inject one violating row (a dup, an orphan, an over-length value, a NULL) into the scratch
  DB, then publish — to capture the **exact `Msg` number and the offending value** the developer will
  hit. It turns "this could fail on bad data" into "here is the failure, verbatim."

**Scope discipline.** These apply only to op classes that HAVE a data consequence or block. Ops with
**no** data block — `edit-seed` (a MERGE), `enable-cdc` (a scripted change), `create-view` (no rows)
— must **not** manufacture a block that structurally cannot fire. Naming the absence is itself the
honest result.

## What the proof hands back (and what it feeds into the PR)

The proof hands back a set of findings, each with its evidence, ready for `../author-pr/SKILL.md` to
assemble into the pull request the reviewer approves by reading:

- **How it ships** and **who must review, and why** — the two plain findings
  (`../../THE_RECORD.md` §5), now proven rather than provisional. These become the PR's
  **Review & release** section.
- **The real generated delta** and the Strict outcome — the block (with the verbatim `Msg` and the
  row counts from the content-hash check) and, after the remedy, the clean re-run. These become
  **Deployment evidence**; stamp the sqlpackage version, because the guard behaviour is version-bound.
- **The remedy that makes Strict pass clean**, and its durability at source (for a reconcile, per the
  FK findings above). This feeds **Data remediation**.
- **The verification query** that returns an unambiguous expected result in any environment —
  **Verification**.
- **What the disposable copy could not prove** — reversibility, application impact, production scale
  — as standing **Not verified** items.

Re-run the Strict publish after applying the remedy: that clean re-run is the evidence the change
now lands.

The developer is owed the same finding in conversation — plain, in their terms, with the one
decision that is genuinely theirs. For the make-mandatory case:

> You asked to make Email required. On a disposable copy of Dev, SSDT refused it: it checks whether
> the table has any rows, not whether Email has blanks, so it blocks the change while the table
> holds data — even after the blanks are filled. On an empty table it would just apply. With data in
> the table, this needs a deliberate call: relax the data-loss guard for this one change after
> proving no blanks remain, or stage it over two releases. Which would you prefer?

And the same finding on the record, for the PR body:

> Making Email NOT NULL is blocked while dbo.Customer holds rows: SSDT guards the change with
> `IF EXISTS (SELECT TOP 1 1 FROM dbo.Customer) RAISERROR(...)`, which fires on row presence, not on
> blank values — verified on a disposable copy, where a backfill to zero blank Emails was still
> blocked. A dev lead must review this: existing data is affected. Ships as a scripted change — the
> data-loss guard is relaxed for this one column after the zero-blank count is proven, or the column
> is filled and tightened across two releases.

## The named traps to catch in the delta (handbook file 16 = §19)

A rename with no refactorlog entry, Optimistic NOT NULL, Ambitious Narrowing, Forgotten FK Check,
CDC Surprise, Refactorlog Cleanup, SELECT * View. **Catch them in the generated delta or the Strict
block — not after a hypothetical deploy.** That timing is the whole value of the disposable copy.

> **SELECT \* View nuance (proven).** SSDT **auto-emits `EXECUTE sp_refreshsqlmodule`** for
> dependent views on publish, so a `SELECT *` view stays correct through a *normal SSDT* base-column
> add — the drift is **invisible through SSDT** and only bites when a base column is added **out of
> band** (a raw `ALTER` outside the dacpac). Prove the trap with a non-SSDT column-add, not a
> through-model one.

## What the disposable copy honestly cannot prove

Be truthful with the developer about the edges:

- **Reversibility — the disposable copy proves the FORWARD publish only.** Every command in the
  loop runs `/Action:Publish` forward; nothing here exercises a rollback, a down-migration, or
  backing the change out. A clean forward Strict publish says **nothing** about whether the change
  can be safely reversed. Reversibility is one of the team's **Four Dimensions**, but here it stays
  an *asserted claim, not a proven one* — when an operation entry calls a change "reversible" (e.g.
  drop-index "re-add the definition", or a staged add->backfill->cut-over->drop), that is a
  **claim**, not something this loop demonstrated. State it plainly: the forward change is proven
  safe for the data; backing it out is not exercised here.
- **Application impact — the disposable copy cannot prove the running app keeps working.** This is
  state-variable 4 (must old + new app code coexist), and it is the very thing that separates a
  change any team member can review from one a dev lead must review because the running application
  must change to keep working. A single-connection publish against a disposable copy cannot show
  whether the live OutSystems app — or old and new app code mid-rollout — still compiles and reads
  correctly against the new shape. That answer comes from the developer and the architecture,
  **not** from a publish. A clean Strict publish proves the *schema* transition is safe for the
  *data*; it is silent on the *app*.
- **Production scale and timing.** The disposable copy is real-*shaped*, not real-*sized*. It proves
  *whether* a block fires and *what* changes; it does **not** prove how long an index build or a
  table rebuild takes at 50M rows, or whether a lock will block. That stays added scrutiny — at
  production row counts the change may block writes or run long, so schedule a window — not a proof.
- **CDC capture-instance behavior over time.** CDC is a **scripted change** and not in the dacpac
  model; the disposable copy cannot demonstrate the downstream capture-instance management burden.
  Flag **CDC Surprise** as a standing consequence — the capture instance is frozen to the table's
  current columns and needs handling — don't try to publish-prove it away.
- **Concurrency, blocking, and online-vs-offline.** `ONLINE` index builds (Enterprise) and lock
  behavior under live traffic are not modeled by a single-connection publish.
- **External Entities and downstream consumers** (ETL, reports, procs in other databases). The
  disposable copy holds one catalog; cross-database and external effects are out of frame and must
  be named as **dependency scope — what else this change touches**, not proven here.
- **A profile pointed anywhere but the disposable copy is dangerous.** Both profiles target
  `localhost,11433 / ProvingGround` only (or a per-executor `PG_<testId>_<rand>` via
  `/TargetDatabaseName`). Never repoint one at anything else — the whole point is that this DB is
  disposable.

When one of these applies, say so plainly: the block and the row count are proven; the rebuild
duration at production scale is not — that is why the change carries added scrutiny at production
row counts.

## The two publish profiles (fenced examples; the live files are under `proving-ground/profiles/`)

Both aim at the **same disposable copy**. They differ only in the block + smart-default settings.

**Strict — the profile that surfaces the block**
(`proving-ground/profiles/ProvingGround.Strict.publish.xml`):

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="Current" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <TargetConnectionString>Server=localhost,11433;Initial Catalog=ProvingGround;User ID=sa;Password=Projection@Strong1;TrustServerCertificate=True;Encrypt=False</TargetConnectionString>
    <BlockOnPossibleDataLoss>True</BlockOnPossibleDataLoss>
    <GenerateSmartDefaults>False</GenerateSmartDefaults>
    <IgnoreColumnOrder>True</IgnoreColumnOrder>
    <DropObjectsNotInSource>True</DropObjectsNotInSource>
    <IncludeTransactionalScripts>True</IncludeTransactionalScripts>
    <AllowIncompatiblePlatform>False</AllowIncompatiblePlatform>
    <IgnorePermissions>True</IgnorePermissions>
  </PropertyGroup>
</Project>
```

Why each setting: `BlockOnPossibleDataLoss=True` mirrors prod's refusal — it is the block to trip
(and, per the finding above, it fires on table-has-rows for a `NULL -> NOT NULL` change, not on the
column's NULL count). `GenerateSmartDefaults=False` means SSDT will **not** quietly paper over a
NOT-NULL gap, so the block surfaces instead of a silent stamp. `DropObjectsNotInSource=True` is safe
**because this copy is disposable** — drop-by-absence is visible here, where production never would
allow it. `IgnoreColumnOrder=True` keeps cosmetic ordering from masquerading as a real change.

**Permissive — the profile that proceeds past the block to reveal the consequence**
(`proving-ground/profiles/ProvingGround.Permissive.publish.xml`): identical to Strict except the
two flipped settings, so the change **proceeds past the block** and what it did can be snapshotted:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="Current" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <TargetConnectionString>Server=localhost,11433;Initial Catalog=ProvingGround;User ID=sa;Password=Projection@Strong1;TrustServerCertificate=True;Encrypt=False</TargetConnectionString>
    <BlockOnPossibleDataLoss>False</BlockOnPossibleDataLoss>
    <GenerateSmartDefaults>True</GenerateSmartDefaults>
    <IgnoreColumnOrder>True</IgnoreColumnOrder>
    <DropObjectsNotInSource>True</DropObjectsNotInSource>
    <IncludeTransactionalScripts>True</IncludeTransactionalScripts>
    <AllowIncompatiblePlatform>False</AllowIncompatiblePlatform>
    <IgnorePermissions>True</IgnorePermissions>
  </PropertyGroup>
</Project>
```

Never run Permissive **first** — it would erase the evidence the block was protecting. Strict
proves the refusal; Permissive, only after, shows the consequence.

## Hard rules

- Commands are **scaffolded** here; no wrapper script ships. The developer's agent runs
  `docker` / `dotnet` / `sqlpackage` itself.
- Everything this skill touches lives under `ssdt-agent/`.
- For parallel/self-test runs, obey `self-test/PROTOCOL.md`: scratch copy + unique DB + teardown.
  Never edit the authored tree and never publish to a shared catalog while others run.
- The F# Projection engine is an **optional accelerant**, not wired here.

## Connector points

The hand-authored `SampleCatalog` can be replaced by the F# engine's `SqlprojEmitter` /
`DacpacEmitter` / `PostDeployEmitter` output (in `src/Projection.Targets.SSDT`) from a **real**
OutSystems catalog — same proving loop, real schema. The engine emits the artifacts but never
*drives* `sqlpackage`; **this skill is that missing driver**, kept as agent-run commands by
design. See `CONNECTORS.md` for the `Render` / `SsdtBundle` seam.
