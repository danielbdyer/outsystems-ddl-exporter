# Prescope — Reverse Import (the bidirectional pipeline)

> **Status:** research / feasibility prescope. No code change proposed
> in this document; it scopes the seams, the reuse surface, the gaps,
> and the governance frame for a future chapter. Authored from a
> six-agent codebase survey (data emission, schema/catalog,
> SQL adapters, identity/remap/topo, architecture seams, doc prior-art).
>
> **Scope guard:** this prescope concerns the **v2 application at
> `sidecar/projection` only**. V1 is out of scope.

---

## 0. The operator scenario (restated)

Today the pipeline runs **forward**:

```
OutSystems model ──Π──▶ Catalog ──passes──▶ SSDT DDL + bootstrap + static seed ──▶ staging SQL Server
```

The staging SQL Server is consumed by a partner team that maps to the
schema and runs SSIS ETL from a legacy application database. The staging
DB is periodically refreshed with the current shape of the replacement
OutSystems application. Three uses are in play:

1. **External-entity cutover prep** — the team's cloud schema + data is
   migrated so OutSystems External Entities can drive on-prem
   integrations.
2. **Blank migration target** — a blank DB carrying the schema is handed
   to another team as the destination for production data coming from a
   legacy application the OutSystems project replaces.
3. **Iterating staging DB** — the partner team practises SSIS ETL against
   an intermediate staging DB, refreshed as the OutSystems app evolves.
   At "eject" time they run the migration tool for real against on-prem.

**The ask.** Using the schema understanding the pipeline *already*
derives during emission, freeze the schema and hand the copy to the team
**as usual**, then — using that **same produced schema** — invert the
arrow: pull from the staging DB (today the *target* of emission) and
treat it as a **source** to reverse-migrate row data **into an
OutSystems Cloud UAT database**. This is explicitly a **temporary
preview data-load** before the wholesale "eject," and it targets **one
UAT cloud DB only**; dev/qa eject normally later.

```
staging SQL Server ──read──▶ (frozen schema as contract) ──▶ two-phase load ──▶ OutSystems Cloud UAT
```

The agreed transfer strategy is the **two-phase nulls-then-FKs Tarjan
import**: phase 1 inserts rows with cycle-breaking FK columns nulled;
phase 2 wires those FKs in topological dependency order once the target
rows exist.

---

## 1. Bottom line up front

**The pipeline is unusually well-positioned for this.** The hard parts of
a reverse import are already built, load-bearing, and tested — not as a
reverse feature, but as machinery the forward path and the canary already
depend on:

- The **two-phase nulls-then-FKs Tarjan plan is already implemented** in
  the data-emission "MERGE lane" (`DataInsertScript` / `StaticSeedsEmitter`
  / `MigrationDependenciesEmitter` / `DataEmissionComposer`). The strategy
  the operator names is the strategy the code already runs.
- The **reverse row-reader already exists**: `ReadSide.readRowsStream`
  streams any table's rows out of a live SQL Server into the canonical
  in-memory row IR (`StaticRow`).
- The **SQL write realization already exists**: `Bulk.copyRows`
  (`SqlBulkCopy`) and `Deploy.executeStream`.
- The **round-trip is already exercised**: the canary deploys to a DB,
  reads it back via `ReadSide`, re-emits, deploys to a second DB, and
  asserts structural equality. Cross-database read+write orchestration is
  not new territory.
- The **key-remap pattern already exists** (`UserRemapContext` /
  `UserFkReflowPass`) — the exact "map source-environment ids to
  target-environment ids, re-point FKs, skip-and-diagnose unmatched"
  shape the harder reverse mode needs.

**The reverse import is, architecturally, the existing two-phase data
plan re-sourced and re-sinked.** Forward, the plan's rows come from the
OSM model and the sink is the staging DB. Reverse, the rows come from the
staging DB and the sink is the UAT DB. The plan computation in between —
topological order, cycle detection, deferred-FK selection, raw-value
codec — is **direction-neutral** and reused wholesale.

**What is genuinely new** is bounded and identifiable:

1. A **two-endpoint connection seam** (source = staging, sink = UAT),
   which must live **outside** `Config` to respect the secret-free
   guardrail (D9).
2. A **reverse orchestrator** in `Projection.Pipeline` that opens both
   endpoints, reads in dependency order, and applies the two-phase plan
   against a **pre-existing** target (bypassing the canary's
   ephemeral-container / `CREATE DATABASE` scaffolding).
3. An **identity policy decision** — *preserve* staging keys vs. *remap*
   to UAT-assigned keys — and, if remap is chosen, **assigned-key
   capture** during phase-1 insert (no `OUTPUT`/`SCOPE_IDENTITY` capture
   exists today).
4. **Persisting `SsKey` into the frozen schema artifact** so a *later*
   reverse run can recover OutSystems identity from disk (today the
   on-disk contract recovers logical *names*, not identity).
5. A **CLI verb** + a **governance frame** for a non-production write
   path that respects R6.

**Recommended first cut for the stated scenario** (blank UAT, preview
load): **preserve-identity mode**. Because the UAT target is a *blank*
database carrying the frozen schema, we can preserve the staging surrogate
keys via `SqlBulkCopy`'s `KeepIdentity` (already the forward default) —
which **eliminates the entire remap problem**. Two-phase is still needed
to break FK cycles, but phase 2 simply restores the original FK values
(exactly what the forward path already does). Remap mode is the harder,
later capability for non-empty targets.

---

## 2. The central architectural insight

The pipeline already separates a **direction-neutral plan** from a
**direction-specific realization**. This is not incidental — it is
codified as A35 / A36:

- **A35** — Π's canonical output is a typed deterministic *statement
  stream* (`seq<Statement>`); realization layers consume the stream and
  choose their emission form.
- **A36** — bulk-vs-incremental is *realization-layer policy*. How a
  realization deploys (`SqlBulkCopy`, per-row INSERT, file write) is
  invisible to Π.

The two-phase data plan inherits this split exactly. The plan IR
(`DataInsertScript`) names *what* must happen and *in what order*; the
`Rendered*` strings and `Bulk.copyRows` are *one* realization
(write-SQL-to-staging). A reverse load is simply **another realization of
the same plan**, pointed at a different sink — and fed by a different
source.

```
            ┌─────────────────── direction-neutral ───────────────────┐
 SOURCE ──▶ │  rows → Catalog.Static modality → DataInsertScript plan  │ ──▶ SINK
            │   (TopologicalOrder · deferredColumns · RawValueCodec)    │
            └──────────────────────────────────────────────────────────┘
 forward:   OSM model                                                       staging DB  (render SQL / Bulk)
 reverse:   staging DB (ReadSide.readRowsStream)                            UAT DB      (Bulk / executeStream)
```

This is the load-bearing claim of the whole prescope: **we are not
building a reverse pipeline; we are adding a source adapter and a sink
realization to a plan that is already bidirectional in principle.**

---

## 3. What already exists — the reuse surface

### 3.1 The two-phase nulls-then-FKs plan (the named strategy, already built)

The forward emission has two lanes. The **MERGE lane** (`StaticSeedsEmitter`
+ `MigrationDependenciesEmitter` + `BootstrapEmitter`, composed by
`DataEmissionComposer`) **already implements the two-phase strategy the
operator describes**, end to end:

- **Plan IR** — `DataInsertRow` carries `KindKey`, `Identifier`,
  `Values : Map<Name, SqlLiteral>`, and crucially
  `DeferredFkSet : Set<Name>` — the column names that cycle-break across
  the two phases (`DataInsertScript.fs:26-56`). `DataInsertScript` splits
  into `Phase1Merges` / `Phase2Updates` and renders
  `RenderedPhase1` / `RenderedPhase2` / `Rendered`
  (`DataInsertScript.fs:82-117`).
- **Deferred-FK selection** — a pure predicate: a column defers iff its
  kind is in a cycle, its FK target is in the same cycle, and the source
  column is nullable (`StaticSeedsEmitter.fs:99-112`;
  `MigrationDependenciesEmitter.fs:136-149`). Not-null FKs in a cycle
  cannot defer (they surface as a diagnostic).
- **Phase 1** — deferred columns emit as `SqlLiteral.NullLit` in the
  MERGE VALUES and are excluded from the `WHEN MATCHED UPDATE` set
  (`StaticSeedsEmitter.fs:142-153`, `:192-196`).
- **Phase 2** — an `UPDATE … SET <deferred> = <orig> WHERE <pk>` wires
  the FKs once phase-1 rows exist (`StaticSeedsEmitter.fs:226-257`).
- **Global ordering** — `DataEmissionComposer.composeRenderedFull` walks
  kinds in topological order and concatenates **all** Phase-1 across all
  kinds **before** any Phase-2 (`DataEmissionComposer.fs:287-326`). A
  level-parallel sibling `composeRenderedLeveled` uses
  `TopologicalOrder.levels` (`DataEmissionComposer.fs:347-393`).

This is precisely "phase 1: insert with FK nulled; phase 2: update FKs in
dependency order," Tarjan-derived.

### 3.2 Topological order + cycle detection (direction-neutral)

`TopologicalOrderPass` runs Kahn's algorithm with Tarjan SCC detection,
producing a `TopologicalOrder` with an ordered `Order : SsKey list`,
Kahn `levels` (parallel-safe batches), and `Cycles : CycleDiagnostic list`
(`TopologicalOrder.fs:130-137`, `:211-235`). Self-references are
first-class via `SelfLoopPolicy = TreatAsCycle | SkipSelfEdges`
(`TopologicalOrder.fs:40-52`). This is pure graph algebra over the
catalog — it drives phase-2 sequencing for a reverse load directly.

One caveat for reverse use: the forward cycle *resolver*
(`CycleResolution`) only breaks 2-member SCCs with exactly one weak
(nullable, `NoAction|SetNull`) edge; larger cycles fall back to
alphabetical order. The reverse strategy is **more aggressive** — null
*every* deferrable FK in phase 1 regardless of cycle membership — so a
reverse import can bypass the resolver and use `Order` / `levels` purely
for phase-2 sequencing. (See §6.3.)

### 3.3 The reverse row-reader (already exists)

`ReadSide.readRowsStream (cnn) (kind) : AsyncStream<StaticRow>` issues
`SELECT <cols> FROM <schema.table> ORDER BY <pk>` and materializes each
SQL row into `StaticRow { Identifier; Values : Map<Name,string> }` via
`formatRawValue` (`ReadSide.fs:494-599`). The values are canonical
invariant-culture raw strings — **the same raw-string IR the seed
emitters consume**. This is the inverse of the emitter's input and the
natural feed for a reverse plan.

> **Identity nuance (verified).** `readRowsStream` synthesizes a
> per-row `SsKey` from a `READSIDE_ROW` basis (`schema.table.rowIdx`,
> `ReadSide.fs:578-587`) — **not** the OutSystems entity GUID. For
> preserve-identity mode this is irrelevant (the PK value rides in
> `Values` and `KeepIdentity` copies it). For remap mode it matters: row
> correlation needs a business/natural key, not the synthesized SsKey.

### 3.4 The SQL write realization (already exists)

`Bulk.copyRows` performs a real `SqlBulkCopy.WriteToServerAsync` with
`SqlBulkCopyOptions.KeepIdentity ||| KeepNulls` — the docstring states
"`KeepIdentity` is honored so source PKs survive across the round-trip"
(`Bulk.fs:89-106`). `Deploy.executeStream` consumes a `seq<Statement>`,
routing DDL to batch execution and `InsertRow` runs to `Bulk.copyRows`
(`Deploy.fs:710-833`). `RawValueCodec` is bidirectional and round-trip
stable by design; the reader (`ReadSide.formatRawValue`) and the writer
(`Bulk.parseRaw`, `""` → `DBNull`) converge on it (`Bulk.fs:39-67`).

**`KeepIdentity` is the linchpin of preserve-identity mode.** It is
already the forward default, and it is exactly what a blank-target preview
load wants.

### 3.5 The round-trip is already a load-bearing pattern

The **canary** is V2's primary wide integration surface (per CLAUDE.md).
It deploys an OutSystems-shaped source DDL to an ephemeral DB, reads it
back via `ReadSide` into a `Catalog`, runs the emitter on the
reconstruction, deploys to a second ephemeral DB, reads *that* back, and
asserts source ≈ target on `PhysicalSchema`. The reverse import is a
close cousin of half this loop: read a live DB → feed the plan → write a
live DB. The orchestration shape is precedented;
`Deploy.runWideCanary` is the structural template.

### 3.6 The key-remap pattern (analog for the harder mode)

`UserRemapContext` is `{ Mapping : Map<SourceUserId, TargetUserId>;
Unmatched; Diagnostics }` (`UserRemap.fs:81-98`), produced by
`UserFkReflowPass.discover` matching source users against the target
population by email / SsKey (`UserFkReflowPass.fs:334-348`), and consumed
at emit time by `MigrationDependenciesEmitter.rewriteUserFkColumns`, which
substitutes the target id per User-FK column and **skips the row with a
diagnostic if unmatched** (`MigrationDependenciesEmitter.fs:303-344`). The
orientation newtypes `SourceUserId` / `TargetUserId`
(`UserIdentity.fs:24-33`) prevent passing a source id where a target id
belongs.

This is the precise pattern remap mode generalizes: from user-only /
`int→int` to **per-kind catalog-wide** `Map<(KindKey, SourceKey),
AssignedKey>`, consumed in phase 2 to re-point FKs.

### 3.7 The schema is a bidirectional contract — in memory

The in-memory `Catalog` / `Kind` / `Attribute` model co-locates logical
identity (`SsKey`, including `OssysOriginal of Guid` — the stable
OutSystems entity GUID) with full physical realization (`TableId`,
`ColumnName`, types, nullability, PK/identity flags) and the FK graph
(`Reference` with `TargetKind : SsKey`, on-delete/update, `IsUserFk`,
`HasDbConstraint`). See `Catalog.fs:804-890`, `:410-585`. The SsKey-keyed
indices `kindIndex` / `attributeIndex` already exist
(`Catalog.fs:1287`, `:1129`). This model is already an excellent
bidirectional contract — see §5 for the persistence gap.

### 3.8 Architecture seams (where reverse plugs in)

- Emitters are `Emitter<'element> = Catalog -> Result<ArtifactByKind<'element>>`,
  open-coded in `Compose.project` (`Pipeline.fs:262-320`). Registration
  (`TransformRegistry` / `StrategyRegistrations`) is metadata-only, not a
  dispatch mechanism.
- The CLI is a hand-rolled `match` over `argv` with verbs
  `full-export | emit | skeleton | approve | deploy | canary`
  (`Program.fs:620-659`). Adding an `import` verb is low-disruption.
- **`Projection.Pipeline` is the only project that references both the
  SQL adapter and every target** — `Deploy.fs` and `Bulk.fs` already
  live there with `Microsoft.Data.SqlClient`. It is the natural home for
  reverse orchestration. **No change to `Pipeline.fs`'s export flow is
  required.**
- `Projection.Core` is **pure** — zero I/O, no `Task`, no time/random —
  enforced structurally by the `NoUnsafeTimeInCoreAnalyzer` (PRJ001).
  Reverse SQL I/O therefore lives in an adapter or in Pipeline; pure
  ordering/remap logic can live in Core.

---

## 4. Identity: the two modes (the crux)

Loading row data **into** OutSystems Cloud raises the only genuinely
deep question in this prescope: **whose surrogate keys win?**

### 4.1 Preserve-identity mode (recommended first cut)

**When:** the UAT target is a **blank** database carrying the frozen
schema (the operator's stated case — "deploy a blank database").

**How:** insert rows preserving the staging surrogate PKs via
`SqlBulkCopy`'s `KeepIdentity` (already the forward default,
`Bulk.fs:105`). Because keys are preserved, **no remap is needed at all**:

- Phase 1 inserts every row with cycle-breaking FK columns nulled,
  identity preserved, in any order (`KeepIdentity` + `KeepNulls`).
- Phase 2 restores the deferred FK columns to their **original** values
  in topological order — which is *exactly* what the forward phase-2
  UPDATE already does (`StaticSeedsEmitter.fs:226-257`). No value
  rewriting; the original FK value is still correct because the
  referenced PK was preserved.

**Why this is the right first cut:** it collapses the reverse import to
"read rows → run the existing plan → realize against the UAT sink with
`KeepIdentity`." The entire remap apparatus (assigned-key capture, FK
re-pointing, correlation keys) evaporates. The only new code is the
source/sink plumbing and the orchestrator.

**Risk:** if the UAT DB is *not* blank — if OutSystems has already seeded
rows in overlapping identity ranges, or the platform forbids
`IDENTITY_INSERT` on its managed tables — preserve mode collides. The
operator's scenario says blank, so this is acceptable for the preview;
remap is the escape hatch.

### 4.2 Remap mode (harder, later)

**When:** the UAT target is non-empty, or OutSystems must assign its own
`Id` values (platform-managed identity).

**How:** the generalization of `UserRemapContext`:

1. Phase 1 inserts rows **without** preserving identity; the target
   assigns new keys.
2. **Capture** the `(source key → assigned key)` pair per row. *This is
   the principal missing primitive* — no `OUTPUT inserted.*` /
   `SCOPE_IDENTITY` capture exists anywhere today. Capture requires either
   per-row INSERT with `OUTPUT`, or a post-insert correlation by a stable
   business/natural key (since `SqlBulkCopy` does not return assigned
   ids). This interacts with the reader's synthesized SsKey limitation
   (§3.3) — a natural key must be identified per kind.
3. Build a catalog-wide `Map<(KindKey, SourceKey), AssignedKey>`.
4. Phase 2 re-points **every** FK column (not just cycle-deferred ones)
   to the remapped assigned key, skipping-and-diagnosing unmatched
   references — the `UserFkReflowPass` discipline, generalized.

Remap mode is a strictly larger effort and should be its own chapter. The
preserve-mode first cut delivers the operator's preview without it.

### 4.3 Decision to surface

> **OPEN-1.** For the UAT preview, is the target guaranteed blank, and
> does the OutSystems Cloud UAT DB permit direct writes / `IDENTITY_INSERT`
> on the entity-backing tables? If yes → preserve mode is sufficient. If
> no → remap mode is required and assigned-key capture must be designed.

---

## 5. The frozen-schema-as-contract problem (SsKey persistence)

The operator's framing is "**freeze** the schema, copy it for the team,
then use **that same produced schema** to drive the reverse read." There
are two sub-cases:

- **Same-run reverse** — the reverse read happens in the same process
  that still holds the live `Catalog`. The in-memory contract is complete
  (§3.7); nothing is missing.
- **Later reverse from a frozen artifact** — the reverse read happens in
  a *separate* run that must reload the frozen schema from disk. **Here
  the on-disk contract is currently insufficient for identity.** The
  manifest (`ManifestEmitter.fs:473-893`) persists physical coordinates
  and counts, plus `DeploymentBatches` (the FK-safe topological order as
  `SsKey.rootOriginal` strings), but **not** the per-column attribute
  mapping, the FK edge graph, or `SsKey`. The emitted DDL recovers the
  logical *name* via the `V2.LogicalName` extended property
  (`PhysicalSchemaReader.fs:112-132`) but **`SsKey` / `OssysOriginal` is
  never written to any artifact** — it lives only in memory.

For preserve-identity mode this gap is **tolerable**: the reverse read
keys off physical names and PK values, and identity reconciliation is not
required. For remap mode it is **load-bearing**: relinking a staging row
to its OutSystems entity by name is fragile under rename, whereas
`OssysOriginal` is rename-stable (A1).

**Recommended enabling change (cheap, high-leverage):** persist `SsKey`
into the frozen artifact — either as a `V2.SsKey` extended property
sibling to `V2.LogicalName`, or as a structured field in an enriched,
re-loadable **schema contract** (a serialized projection of `Catalog`
carrying per-table `{ TableId, SsKey, Name }`, per-column
`{ ColumnName, SsKey, Name, Type, Nullable, IsPrimaryKey, IsIdentity }`,
and FK edges `{ source, target, TargetKind SsKey }`). This also gives a
clean home for a **physical-coordinate-keyed index** (`Map<TableId,Kind>`,
the inverse of the existing SsKey-keyed index) that a reverse reader needs
to map physical names the staging DB hands back to logical entities.

> This dovetails with HORIZON H-010 (the Catalog ↔ DDL `Prism`, whose
> `ReverseGet = ReadSide.readCatalog`) — the reverse read is the `Prism`'s
> reverse direction made to carry identity, not just structure.

---

## 6. Governance & axiom alignment

A reverse import that **writes to a UAT database** crosses the project's
single most sensitive guardrail. This section is the honest accounting.

### 6.1 R6 — "V2 owns no production write path during dual-track"

Per `DECISIONS 2026-05-22 — R6`, during the dual-track window V2
emits-but-doesn't-ship; V1 owns the production write path; the canary
asserts V1 ≈ V2 modulo named tolerances. A reverse loader is a **new
write path** and must be framed so it does **not** violate R6:

- **It targets UAT preview, not production.** The operator's scenario is
  explicit and bounded: one UAT cloud DB, temporary, pre-eject. This is a
  development/preview write, categorically outside the production write
  path V1 owns.
- **It is opt-in and separately gated.** A distinct CLI verb, a distinct
  connection seam, and (recommended) a **dry-run-by-default** posture:
  the reverse run *emits the two-phase script* by default and only
  *executes* against the UAT sink under an explicit `--execute` +
  operator-supplied connection — mirroring how the canary only ever
  executes against ephemeral DBs.
- **It must be named in DECISIONS before it ships.** R6 is non-negotiable
  without a superseding entry. The amendment should state the
  preview-only scope, the UAT-only target, and the dry-run default.

### 6.2 D9 — secret-free config

`Config` carries no connection/credential field by construction and
actively rejects credential-shaped JSON keys via a
`credentialSignatures` scan (`Config.fs:12-16`, `:345-390`). The
reverse import's **two connection endpoints must not live in `Config`.**
They arrive via environment variables (the forward `Deploy` precedent is
`PROJECTION_MSSQL_CONN_STR`) or a CLI-flag-referenced file path. Today
there is exactly one connection-string source pointing at a single
server's `master`; the reverse path needs **two distinct endpoints**
(source staging server, sink UAT server) each with its own credentials —
this is net-new plumbing, deliberately outside `Config`.

### 6.3 Pure-core / A18 / pillar 9

- **Pure core.** The SQL read (source) and SQL write (sink) live in
  adapters / Pipeline, never Core. Pure ordering and (eventual) remap
  logic may live in Core. Enforced by PRJ001.
- **A18.** Emitters consume `Catalog × Profile`, never `Policy`. The
  reverse "which rows, which mode, which phase batching" is **operator
  intent** and must enter at the Pipeline boundary (like
  `EmissionFolders` / `TransformGroups` overlays), not inside the plan
  emitter.
- **Pillar 9 (DataIntent vs OperatorIntent).** A reverse pass/realization
  is a registered transformation and must be classified. Reading rows
  from staging is `DataIntent` (no operator opinion); the
  identity-mode choice, the target selection, and the dry-run/execute
  toggle are `OperatorIntent`.

### 6.4 CDC safety

`idempotentRedeploy` requires zero spurious CDC change records, because
downstream ETL interprets them as real changes and corrupts replicas
(`PRODUCT_AXIOMS.md:95`). The reverse import writes into the **UAT**
target, but if that target (or anything downstream of it) is CDC-tracked,
the two-phase phase-2 UPDATEs would generate change events. The reverse
realization should reuse the forward CDC-aware discipline (phase-2
UPDATE touches only the deferred FK axis) and the preview target should
be confirmed CDC-free or CDC-tolerant.

### 6.5 Determinism (T1)

The plan is deterministic by construction (sorted by `SsKey`, `decimal`
evidence, no wall-clock in Core). The reverse realization inherits this:
the *order* of inserts/updates is the deterministic topological order;
only the I/O timing is nondeterministic, which is invisible to the plan.

---

## 7. Proposed seams — the minimal change set

Ordered by leverage; none requires forking `Pipeline.fs` or touching
Core purity.

1. **`ReverseSource` (adapter, `Projection.Adapters.Sql`).** A thin
   composition over the existing `ReadSide.readRowsStream`: given a frozen
   `Catalog` (the contract) and a source connection, stream `StaticRow`s
   per kind in dependency order. Mostly reuse; the new surface is "stream
   all kinds in `TopologicalOrder.Order`."

2. **`ReverseSink` / reverse realization (`Projection.Pipeline`).** Reuse
   `Bulk.copyRows` (preserve mode, `KeepIdentity`) and the two-phase
   ordering from `composeRenderedFull` / `composeRenderedLeveled`. Phase 1:
   bulk-insert with deferred FKs nulled. Phase 2: UPDATE deferred FKs in
   topo order. Target a **caller-supplied open connection**, bypassing the
   canary's `useContainer` / `createDatabase` scaffolding.

3. **Reverse orchestrator (`Projection.Pipeline`, sibling to `Compose`).**
   `loadContract → openSource → openSink → forEach kind in topo order:
   read rows → plan (existing emitters) → realize two-phase → diagnostics`.
   This is where source+sink endpoints meet; `Pipeline.fs` is untouched.

4. **Two-endpoint connection seam (outside `Config`).** Env vars
   (`PROJECTION_REVERSE_SOURCE_CONN_STR`,
   `PROJECTION_REVERSE_SINK_CONN_STR`) and/or `--source-connection-file` /
   `--sink-connection-file` CLI flags. Respects D9.

5. **CLI verb `import` (`Projection.Cli`).** One `match` arm at
   `Program.fs:628`-style + an `ImportArgs.fs` (mirror `FullExportArgs`),
   with `--contract`, `--mode preserve|remap`, `--dry-run` (default) /
   `--execute`, `--preview-row-cap`.

6. **SsKey persistence in the frozen artifact (`Targets.SSDT`).** `V2.SsKey`
   extended property and/or an enriched re-loadable schema contract (§5).
   Enables *later-run* reverse and is the prerequisite for remap mode.

7. **(Remap mode, later chapter.)** General surrogate-remap context
   (`Map<(KindKey,SourceKey),AssignedKey>`), assigned-key capture
   (`OUTPUT` or natural-key correlation), catalog-wide FK re-pointing in
   phase 2. Generalizes `UserRemapContext`.

---

## 8. Phased delivery plan

A possible chapter shape (slices sized to the repo's cadence):

- **Slice A — contract persistence.** Persist `SsKey` into the frozen
  schema (extended property / schema-contract artifact) + a
  physical-coordinate-keyed index. Property test: round-trip
  `Catalog → artifact → reload` preserves `SsKey` and the FK graph.
  *Unblocks later-run reverse and remap.*

- **Slice B — reverse source.** `ReverseSource` streaming `StaticRow`s in
  topological order from a live DB given the contract. Reuses
  `readRowsStream`. Test against an ephemeral DB seeded by the forward
  emitter (the canary substrate).

- **Slice C — preserve-mode reverse sink + orchestrator (dry-run).**
  Two-phase plan realized as a script for a caller-supplied sink;
  dry-run emits the script. Test: forward-emit to DB-A → reverse-read →
  reverse-plan → assert the generated two-phase script round-trips
  (deploy to DB-B, read back, `PhysicalSchema` + row-digest equality).
  This is a **data-level canary** — a natural extension of the existing
  schema-level canary.

- **Slice D — execute against UAT (gated).** `--execute` path against a
  real UAT connection, behind the R6 governance amendment, dry-run
  default, preview row cap, CDC-safety check. Operator sign-off gate.

- **Slice E (later chapter) — remap mode.** Assigned-key capture, general
  remap context, catalog-wide FK re-pointing. Its own prescope.

The data-level canary in Slice C is the highest-confidence deliverable:
it proves the reverse plan reconstructs the source data faithfully
*before* any UAT write, the same way the schema canary proves emitter
fidelity before any production emit.

---

## 9. Mapping to existing HORIZON / prior art

The reverse import is greenfield as a **data-flow goal** (the docs survey
found no "eject," no reverse-into-OutSystems, no bidirectional data
movement anywhere), but it lands cleanly onto existing structural prior
art:

- **H-010 — Catalog ↔ DDL `Prism`** (`HORIZON.md:415`): `Get =
  SsdtDdlEmitter.emit`, `ReverseGet = ReadSide.readCatalog`. The reverse
  import is the `Prism`'s reverse leg extended from *schema* to *data +
  identity*.
- **H-058 — ReadSide reconstruction** and **H-089 — migration preview
  (dry-run)**: H-089 today renders the DDL stream as a human-readable
  change list; the reverse import's dry-run is the **data** analog.
- **`LiveOssysConnection` re-open trigger** (`DECISIONS.md:~4796`): the
  documented seam for a live OutSystems connection (today framed as
  read-only metadata ingest). The reverse import is a write-direction
  cousin and should be cross-referenced when that trigger fires.
- **`UserFkReflowPass` / `MigrationDependencyContext`**
  (`CHAPTER_4_PRESCOPE_USERFK_REFLOW.md`, `CHAPTER_4_2_OPEN.md`): the
  remap discipline remap mode generalizes.

---

## 10. Open questions / risks

- **OPEN-1 (identity).** Is the UAT target blank, and does it permit
  direct writes / `IDENTITY_INSERT`? (Gates preserve vs. remap — §4.3.)
- **OPEN-2 (platform write surface).** Does the OutSystems Cloud UAT DB
  expose a writable connection to the entity-backing tables at all, or
  must the load go through a platform API? The whole approach assumes
  direct SQL write access to the UAT database. *This is the single
  biggest external dependency and should be confirmed first.*
- **OPEN-3 (CDC).** Is the UAT target or its downstream CDC-tracked?
  (§6.4.)
- **OPEN-4 (governance).** R6 amendment scope — confirm "UAT preview, not
  production" framing and dry-run-by-default before any execute path.
- **OPEN-5 (bulk lane vs. two-phase).** The high-throughput `Bulk` lane
  does **not** today implement two-phase cycle-breaking (it relies on
  topological order + `SkipSelfEdges` and assumes acyclic-or-self-FK-only,
  `StaticPopulationEmitter.fs:124-138`). A reverse bulk load over a cyclic
  graph must graft the two-phase plan (currently MERGE-lane-only) onto the
  bulk lane, or accept per-row INSERT for cyclic kinds.
- **OPEN-6 (constraints/triggers).** Reverse-loading into a schema with
  CHECK constraints, computed columns, or triggers may need
  `NOCHECK` / disable-trigger handling not present in the forward
  blank-deploy path.

---

## 11. One-paragraph synthesis

The pipeline already contains the hard machinery of a reverse import — a
working two-phase nulls-then-FKs Tarjan plan (`DataInsertScript` +
`DataEmissionComposer`), a live row-reader (`ReadSide.readRowsStream`), a
bulk SQL writer with identity preservation (`Bulk.copyRows`,
`KeepIdentity`), a bidirectional value codec (`RawValueCodec`), an
exercised read-back round-trip (the canary), and a key-remap template
(`UserRemapContext`). The reverse import is the *same direction-neutral
plan* (A35/A36) re-sourced from staging and re-sinked to UAT. For the
operator's stated blank-UAT preview, **preserve-identity mode** sidesteps
the only deep problem (surrogate remap) entirely, leaving a bounded build:
a two-endpoint connection seam outside `Config` (D9), a reverse
orchestrator in `Projection.Pipeline`, a CLI verb, and — for *later-run*
or *remap* use — persisting `SsKey` into the frozen schema contract. The
chief external unknown is whether the OutSystems Cloud UAT database
permits direct SQL writes (OPEN-2), and the chief internal commitment is
an R6 governance amendment scoping this as a UAT-only, dry-run-by-default,
preview write path.
