---
name: prove-on-dacpac
description: Use when a data-model change has a PROVISIONAL mechanism from classify-mechanism and must be confirmed (or flipped) against real-shaped data. Builds the .sqlproj to a dacpac, previews the REAL SSDT-generated delta, then publishes to a throwaway DB under a Strict (veto-detector) and Permissive (consequence-oracle) profile to discover whether the data vetoes the change. Invoke whenever classification depends on table state — populated? rule-violating rows? — which is ALWAYS for anything past the purely-additive corner. Scaffolds commands; the developer's agent runs them.
---

# Prove on dacpac

> **Why this (and what it teaches).** The veto **is** the classification: SSDT's own publish engine
> is a more honest oracle than any rule table, because it computes the real delta against the real
> rows. We prove instead of advise because the `.sql` text cannot tell you the mechanism — only the
> data on the proving ground can, and the moment SSDT refuses, you have learned the bucket. What
> this teaches: a green-looking schema diff and a clean data probe are *necessary but not
> sufficient* — the deciding evidence is what the engine actually does when you publish. The
> sharpest lesson the proving ground teaches (below) is that a `NULL -> NOT NULL` veto is
> **table-has-rows, not column-has-NULLs** — so even backfilling every NULL does not clear it.
> Surface this to the developer: a developer who watches the engine veto a change they were sure
> was clean graduates from trusting their reading of the SQL to demanding the proof.

You are helping an **OutSystems-native developer** land a safe schema change. The classification
you were handed is **provisional** — a reading of intent. **Proving is classifying.** The data
decides the mechanism, and the only way to know the data is to publish the change to a copy of it
and watch what SSDT's publish engine actually does.

This is the capability the team's decks never taught: a **proving ground** — a throwaway copy of
real-shaped data — and `sqlpackage` driving the real dacpac delta against it. You introduce this
vocabulary **gently**, always tied back to a phrase the team already owns:
**"You describe the destination, SSDT computes the journey."** The dacpac is the described
destination; `sqlpackage` computes the journey; the proving ground lets you watch the journey
before it ever runs on production.

## When to use

- `classify-mechanism` handed you a provisional Mechanism + Tier and said **must-prove**.
- The verdict depends on any of the four state-variables (populated / violates / CDC-no-gap /
  coexist). Prove before you tell the developer anything.
- A named trap is suspected (especially **Naked Rename** — always read the delta).

If `classify-mechanism` said **on-sight** (the purely-additive Tier 1 corner), you still run one
clean Strict publish to confirm no surprise delta — but no flip is possible.

## Running in parallel — see self-test/PROTOCOL.md

When MANY executors prove cases at once (the self-test fleet), do **not** publish to the shared
default `ProvingGround` catalog and do **not** edit the authored `proving-ground/` tree. Each
executor copies the tree to its own scratch dir and publishes to a **unique database**
(`/TargetDatabaseName:PG_<testId>_<rand>`, which overrides the profile's `Initial Catalog`), then
drops the DB and deletes the scratch on exit. The full parallel-safe, idempotent isolation
protocol is **`self-test/PROTOCOL.md`** — read it before running any prove loop alongside other
executors. (Single-developer, one-at-a-time use can target `ProvingGround` directly per
`talk-to-local-sql`.)

## The vocabulary you are teaching (gently)

| Term | What it is | Tie it back to |
|---|---|---|
| **dacpac** | the compiled "destination" (your edited CREATEs, built) | "describe the destination" |
| **sqlpackage** | the tool that computes + applies the delta | "SSDT computes the journey" |
| **publish profile** | the rules SSDT publishes under (a `.publish.xml`) | the team's prod safety settings |
| **BlockOnPossibleDataLoss** | the **veto** — refuses any step that could lose data | "it stops you before you hurt the data" |
| **GenerateSmartDefaults** | the **silent backfill** — stamps a default into NOT-NULL columns | "what it would have done quietly" |
| **proving ground** | a throwaway copy of real-shaped data to publish against | "a sandbox that looks like prod" |

## The proving loop (you scaffold; the developer's agent runs each command)

> All commands assume you are at the repo root and the proving ground is warm — see
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

# 5. VETO CHECK — Strict = the veto detector. Does the data refuse the change?
sqlpackage /Action:Publish \
  /SourceFile:ssdt-agent/proving-ground/bin/Release/SampleCatalog.dacpac \
  /Profile:ssdt-agent/proving-ground/profiles/ProvingGround.Strict.publish.xml

# 6. ON VETO ONLY — Permissive = the consequence oracle. Let it proceed past the veto so you can
#    snapshot WHAT it was protecting against (see the content-hash oracle in talk-to-local-sql).
sqlpackage /Action:Publish \
  /SourceFile:ssdt-agent/proving-ground/bin/Release/SampleCatalog.dacpac \
  /Profile:ssdt-agent/proving-ground/profiles/ProvingGround.Permissive.publish.xml
```

> For a **parallel** run, every command above also carries `/TargetDatabaseName:PG_<testId>_<rand>`
> and points at a scratch copy of the tree — see `self-test/PROTOCOL.md`.

## Reading the result -> the classification

**Read the generated `delta.sql` AND the Strict publish outcome together.** Map what you see:

- **Strict publishes clean, the delta is an in-place `ALTER` (or nothing), no veto**
  -> **Mechanism 1 Pure Declarative** (single-phase). Confirmed.
- **Strict vetoes** with `BlockOnPossibleDataLoss` / "rows that would be affected" / NOT NULL on
  populated data / truncation -> the data **flipped the bucket up**. The remedy that clears the
  veto is your proof:
  - backfill before the schema change -> **Mechanism 3 Pre-Deploy+Declarative**, single-PR.
    (But see the make-mandatory caveat below — for `NULL -> NOT NULL` on a populated table, the
    backfill alone does **not** clear the veto; that is the corrected finding.)
  - the fix is a post-deploy seed/reconcile -> **Mechanism 2 Declarative+Post-Deploy**.
  - the change isn't declarative at all (FK `NOCHECK`->`WITH CHECK CHECK`, CDC, IDENTITY swap)
    -> **Mechanism 4 Script-Only**.
  - old+new app code must coexist, or a no-gap CDC rollout -> **Mechanism 5 Multi-Phase**, multi-PR.
- **The delta shows `DROP <col/table>` + `CREATE` on something you asked to RENAME**
  -> **Naked Rename. STOP.** The refactorlog entry is missing; publishing this loses the data
  silently. Demand the refactorlog entry (it turns the DROP+CREATE into `sp_rename`), rebuild,
  re-preview, and confirm the delta is now a rename. This is the single most important read in the
  set.
- **The delta shows a shadow-table rebuild** (CREATE new table -> copy -> drop old -> rename) or
  **drop-by-absence** (an object vanishes because it's no longer in source) -> name it to the
  developer explicitly; these are expensive and/or destructive and they did not ask for them.

## The make-mandatory finding: the guard is TABLE-HAS-ROWS, not column-has-NULLs (VERIFIED)

This is the showcase result the proving ground exists to teach, and it **corrects an earlier,
wrong recipe.** The old advice said "a pre-deploy backfill clears the NULLs, then the declarative
NOT NULL lands clean under Strict = Mechanism 3." **That was disproven empirically here.**

For a `NULL -> NOT NULL` change, `sqlpackage` generates the `BlockOnPossibleDataLoss` guard as:

```sql
IF EXISTS (SELECT TOP 1 1 FROM [dbo].[Customer])
    RAISERROR (N'Rows were detected. The schema update is terminating because data loss might occur.', 16, 127);
-- ... and BELOW it, the actual:
ALTER TABLE [dbo].[Customer] ALTER COLUMN [Email] NVARCHAR(256) NOT NULL;
```

That guard fires on `IF EXISTS (... FROM Table)` — **the table merely having rows** — and is placed
**before** the `ALTER COLUMN`. It **does not inspect the `Email` column at all.** Why: **SSDT
computes the entire deploy script once, up front, from the pre-publish model state, and is
conservative by design** — it cannot know that a pre-deploy backfill (which runs at deploy time,
after the script is already generated) will have emptied the NULLs, so it refuses the moment the
table has any row. The gate cannot know your intent, so it assumes the worst.

**Proven on the proving ground:** a pre-deploy backfill cleared **all** NULLs (a fresh
`SELECT COUNT(*) WHERE Email IS NULL` returned **0**), and Strict **STILL vetoed**, leaving the
column nullable. **Zero NULLs is necessary but NOT sufficient** to pass the prod-strict gate on a
populated table.

The **honest, corrected recipe:**

- **EMPTY table** -> clean **Mechanism 1, single-phase, Tier 1.** No rows, the `IF EXISTS` is false,
  the RAISERROR never fires, the `ALTER COLUMN NOT NULL` lands. (Confirm the table is genuinely
  empty first.)
- **POPULATED table (NULLs present OR zero NULLs — it makes no difference)** -> the make-mandatory
  change **cannot pass the prod-strict gate by backfill alone.** It needs a **conscious, documented
  decision taken AFTER a verified backfill** (prove `COUNT(*) WHERE col IS NULL = 0` first —
  necessary, not sufficient), then ONE of:
  - **(a) Targeted gate-relaxation** — operationally **Mechanism 4 / Script-Only with a named
    relax.** Having proven zero NULLs remain, deliberately disable `BlockOnPossibleDataLoss` for
    **this one targeted change** (a scoped profile override or a script-only path), so the
    `ALTER COLUMN NOT NULL` proceeds against the now-clean column. The proof packet must carry
    **both** the zero-NULL probe AND the explicit record of the relaxation decision.
  - **(b) Restructure as Mechanism 5 — Multi-Phase, multi-PR** — stage it so the engine never has
    to relax its guard. Tier 2 baseline; **+1** for CDC / >1M rows / first-time.

**What the proof must EMPIRICALLY show (do not assert it — discover it):**

1. Edit `Email ... NULL` -> `NOT NULL`, build, Strict publish -> prove the veto fires, and read
   the delta to **SEE** the `IF EXISTS(...) RAISERROR(...,16,127)` guard placed **above** the
   `ALTER COLUMN` (confirming table-has-rows).
2. Author the pre-deploy backfill, re-run the NULL probe -> prove `0` NULL emails remain.
3. Re-run Strict -> prove it **STILL vetoes** and the column **stays nullable** — the backfill did
   not satisfy the gate. **This step is the showcase finding.**
4. Deliver the corrected verdict and prove the chosen path ((a) named gate-relaxation after
   proven-zero-NULL, or (b) multi-phase) actually lands the NOT NULL.

This is the spine proof that the tree's *prove-don't-advise* thesis holds: the agent discovers a
finding that **contradicts its own earlier skill text** rather than parroting it.

## Strict vs Permissive — what the diff tells you

Strict and Permissive aim at the **same throwaway DB** and differ on two settings
(`BlockOnPossibleDataLoss` and `GenerateSmartDefaults`). The difference between their outcomes is
the proof:

- **Strict refused, Permissive proceeded** -> the change *does* touch data. Now snapshot the data
  **before vs after** the Permissive publish using the content-hash oracle (see
  `talk-to-local-sql`). The rows whose hash changed are exactly what `GenerateSmartDefaults`
  stamped, or what truncated. **That set is the magic line's row count.**
- **Both proceeded clean** -> no data at risk; the Strict-clean result already proved Mechanism 1.
- A **rename** changes a row's hash **by design** (the column name is part of the row shape) — so a
  hash diff on a rename is *correct*, and confirms the rename touched the right rows. The diff IS
  the proof, not a false alarm.

## The content-hash data oracle (the "did values change" question)

The oracle is a per-row `SHA2_256` over `(SELECT x.* FOR XML RAW)`, summed **order-independently**
across the table (so row order doesn't matter), with **NULL kept distinct from `''`**. Snapshot it
before and after a Permissive publish; an unchanged total means no value moved. The exact SQL lives
in `talk-to-local-sql` (one query, reusable). Use it to **quantify** a flip — "1,240 rows would be
stamped" — not merely assert "data might change."

## Two named proof moves (scope them by op class)

Two moves the fitness runs invented and proved — use them where they fit, and do **not** fabricate
them where they cannot fire:

- **CONSEQUENCE ORACLE** — for a Tier-4 *destroying* op (`delete-attribute`, `delete-entity`,
  `narrow` past the data, drop-table): after Strict vetoes, run Permissive to let the irreversible
  act happen on the throwaway copy and snapshot the corpse — the exact rows/values lost — so the
  Tier-4 claim is **observed, not asserted.** (This is the Strict→Permissive pattern above, named.)
- **VETO-INJECTION LEG** — for a *constraint / tightening* op that has a data veto (`define-pk`,
  `add-unique`, `add-check`, `create-fk-orphan`, `make-mandatory`): if the seeded data happens to be
  clean, inject one violating row (a dup, an orphan, an over-length value, a NULL) into your scratch
  DB, then publish — to capture the **exact `Msg` number and the offending value** the developer will
  hit. It turns "this could fail on bad data" into "here is the failure, verbatim."

**Scope discipline.** These apply only to op classes that HAVE a data consequence/veto. Ops with
**no** data veto — `edit-seed` (a MERGE), `enable-cdc` (Script-Only), `create-view` (no rows) —
must **not** manufacture a veto that structurally cannot fire. Naming the absence is itself the
honest result.

## What you hand back (the verdict + the magic line)

The **proven** Mechanism + Tier, the real generated delta, the named veto **with row counts from
the oracle**, and the remedy that makes Strict pass clean — all phrased for the developer. Then
re-run the Strict publish after applying the remedy: **that clean re-run is the proof.** Deliver
the magic line:

> "You said *make it mandatory*. I published that to a copy of your data — SSDT **vetoed** it, and
> when I read the generated script the guard is `IF EXISTS (SELECT TOP 1 1 FROM Customer)
> RAISERROR(...)` placed *before* the ALTER. That's **table-has-rows, not NULL-has-rows** — SSDT
> builds the deploy script up front and can't know I'll backfill, so it refuses the moment the
> table has any rows. I proved it: I backfilled every NULL (0 remain) and Strict **STILL** vetoed
> and left the column nullable. So on your populated table this isn't a clean backfill-then-NOT-NULL
> — it needs a conscious call: either I deliberately relax BlockOnPossibleDataLoss for this one
> change *after* proving zero NULLs (a logged, script-only gate decision), or we stage it
> multi-phase. Here is the proof for the path you choose — Tier 2 (+1 if CDC). On an EMPTY table it
> would have been a clean one-liner, Tier 1; the difference is entirely the rows."

## The named traps to catch in the delta (handbook file 16 = §19)

Naked Rename, Optimistic NOT NULL, Ambitious Narrowing, Forgotten FK Check, CDC Surprise,
Refactorlog Cleanup, SELECT * View. **Catch them in the generated delta / Strict veto — not after
a hypothetical deploy.** That timing is the whole value of the proving ground.

> **SELECT \* View nuance (proven).** SSDT **auto-emits `EXECUTE sp_refreshsqlmodule`** for
> dependent views on publish, so a `SELECT *` view stays correct through a *normal SSDT* base-column
> add — the drift is **invisible through SSDT** and only bites when a base column is added **out of
> band** (a raw `ALTER` outside the dacpac). Prove the trap with a non-SSDT column-add, not a
> through-model one.

## What the proving ground HONESTLY CANNOT prove

Be truthful with the developer about the edges:

- **Reversibility — the proving ground proves the FORWARD publish only.** Every command in the
  loop runs `/Action:Publish` forward; nothing here exercises a rollback, a down-migration, or
  backing the change out. A clean forward Strict publish says **nothing** about whether the change
  can be safely reversed. Reversibility is one of the team's **Four Dimensions**, but it stays an
  *asserted Tier dimension, not a proven one* — when an operation entry calls a change "reversible"
  (e.g. drop-index "re-add the definition", or a multi-phase add->backfill->cut-over->drop), that is
  a **claim**, not something this loop demonstrated. Say so: "I proved the forward change is safe for
  the data; I did not prove you can back it out."
- **Application impact — the proving ground cannot prove the running app keeps working.** This is
  state-variable 4 (must old + new app code coexist), and it is the very thing that splits Tier 2
  SINGLE-PR from Tier 1 SINGLE-PHASE in the cascade. A single-connection publish against a
  throwaway DB cannot show whether the live OutSystems app — or old and new app code mid-rollout —
  still compiles and reads correctly against the new shape. That answer comes from the developer
  and the architecture, **not** from a publish. A clean Strict publish proves the *schema*
  transition is safe for the *data*; it is silent on the *app*.
- **Production scale and timing.** The throwaway DB is real-*shaped*, not real-*sized*. It proves
  *whether* a veto fires and *what* changes; it does **not** prove how long an index build or a
  table rebuild takes at 50M rows, or whether a lock will block. That stays a Tier escalation
  (>1M rows, +1), not a proof.
- **CDC capture-instance behavior over time.** CDC is **Script-Only** and not in the dacpac model;
  the proving ground cannot demonstrate the downstream capture-instance management burden. Flag
  **CDC Surprise** as a standing consequence, don't try to publish-prove it away.
- **Concurrency, blocking, and online-vs-offline.** `ONLINE` index builds (Enterprise) and lock
  behavior under live traffic are not modeled by a single-connection publish.
- **External Entities and downstream consumers** (ETL, reports, procs in other databases). The
  proving ground holds one catalog; cross-database / external effects are out of frame and must be
  named as **Tier 3 dependency scope**, not proven here.
- **A profile pointed anywhere but the throwaway DB is a foot-gun.** Both profiles target
  `localhost,11433 / ProvingGround` only (or a per-executor `PG_<testId>_<rand>` via
  `/TargetDatabaseName`). Never repoint one at anything else — the whole point is that this DB is
  disposable.

When you hit one of these, say so plainly: *"I can prove the veto and the row count; I cannot prove
the rebuild duration at production scale — that's why this stays Tier 3."*

## The two publish profiles (fenced examples; the live files are under `proving-ground/profiles/`)

Both aim at the **same throwaway DB**. They differ only in the veto + smart-default settings.

**Strict = the VETO DETECTOR** (`proving-ground/profiles/ProvingGround.Strict.publish.xml`):

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

Why each setting: `BlockOnPossibleDataLoss=True` mirrors prod's refusal — it is the veto you want
to trip (and, per the finding above, it fires on table-has-rows for a `NULL -> NOT NULL` change,
not on the column's NULL count). `GenerateSmartDefaults=False` means SSDT will **not** quietly
paper over a NOT-NULL gap, so the veto surfaces instead of a silent stamp. `DropObjectsNotInSource=
True` is safe **because the proving ground is disposable** — you *want* to see drop-by-absence here,
where production never would. `IgnoreColumnOrder=True` keeps cosmetic ordering from masquerading as
a real change.

**Permissive = the CONSEQUENCE ORACLE**
(`proving-ground/profiles/ProvingGround.Permissive.publish.xml`): identical to Strict except the
two flipped settings, so the change **proceeds past the veto** and you can snapshot what it did:

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

Never run Permissive **first** — you'd destroy the evidence the veto was protecting. Strict
proves the refusal; Permissive, only after, shows the consequence.

## Hard rules

- You **scaffold** commands; you do **not** ship a wrapper script. The developer's agent runs
  `docker` / `dotnet` / `sqlpackage` itself.
- Everything you touch lives under `ssdt-agent/`.
- For parallel/self-test runs, obey `self-test/PROTOCOL.md`: scratch copy + unique DB + teardown.
  Never edit the authored tree and never publish to a shared catalog while others run.
- The F# Projection engine is an **optional accelerant**, not wired here.

## Connector points

The hand-authored `SampleCatalog` can be replaced by the F# engine's `SqlprojEmitter` /
`DacpacEmitter` / `PostDeployEmitter` output (in `src/Projection.Targets.SSDT`) from a **real**
OutSystems catalog — same proving loop, real schema. The engine emits the artifacts but never
*drives* `sqlpackage`; **this skill is that missing driver**, kept as agent-run commands by
design. See `CONNECTORS.md` for the `Render` / `SsdtBundle` seam.
