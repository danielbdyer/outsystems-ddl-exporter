# Prescope — Transfer (the bidirectional pipeline)

> **Status:** active prescope for the **Transfer epic**. Slice 1
> (vocabulary reification) has **shipped** — `Transfer.fs` reified the
> bidirectional control-plane lexicon in pure Core (commit on
> `claude/bidirectional-exporter-pipeline-4XN4Q`). The remaining slices
> (Ingestion adapter, DML-only Projection realization, Transfer
> orchestrator, CLI verb, `SchemaContract` persistence) are scoped here
> and not yet built. This document is the epic's epistemic anchor: the
> North Star, the holistic feature backlog, the governance frame, and the
> reuse surface.
>
> **Scope guard:** this prescope concerns the **v2 application at
> `sidecar/projection` only**. V1 is out of scope.
>
> **Naming note (2026-05-24).** This file was `PRESCOPE_REVERSE_IMPORT.md`.
> "Reverse" framed the capability as a bolt-on to a one-directional tool.
> The reified understanding is that the pipeline is *bidirectional in
> principle* — what looked like "reverse" is the other leg of an
> adjunction the codebase already names (H-050). The vocabulary is now
> **Projection / Ingestion / Transfer / Source / Sink**, locked per
> `DECISIONS 2026-05-24`. "Reverse" appears nowhere in the code.

---

## 0. Epic framing — epistemic orientation and the North Star

**What the epic is.** A *Transfer* moves row data from one database
substrate into another, governed by a single logical schema. The motivating
instance: freeze the schema the pipeline already derives, hand the copy to
the partner team **as usual**, and then use that *same* schema to pull rows
out of the staging SQL Server (today the *target* of emission) and load them
**into an OutSystems Cloud UAT database** as a temporary, pre-eject preview.

**Why the pipeline already half-contains it.** The forward pipeline was
built around a structural law the codebase has already named and
partially proven:

> **H-050 (the adjunction).** `Ingestion ∘ Projection = id`, up to named
> lossy fields. The "emitter" (Projection / Π) lowers a `Catalog` to a
> statement stream; the "reader" (Ingestion) lifts a substrate back to a
> `Catalog`. `AdjunctionLawTests.fs` already proves the *schema-level*
> form: `PhysicalSchema.ofCatalog c = PhysicalSchemaReader.ofStatementStream
> (emit c)` on the Columns + ForeignKeys axes.

A Transfer is **the adjunction run across two substrates and extended from
schema to data**. The hard machinery — the two-phase nulls-then-FKs Tarjan
plan, the live row reader, the bulk writer with identity preservation, the
bidirectional value codec, the read-back round-trip canary, the key-remap
template — already exists, load-bearing and tested, because the forward path
and the canary depend on it.

**The North Star.** The epic's correctness criterion is the **data-level
extension of H-050**:

> A Transfer is correct when `Ingestion(Source) → Projection(Sink)`
> reproduces the source rows in the sink **up to named identity-remap
> tolerances**. The proof surface is a **data-level canary** — the exact
> sibling of the schema canary that already earns emitter fidelity. The
> schema canary proves structure round-trips; the data canary proves
> *data* round-trips before any UAT write.

This is squarely on the project's V2-driver North Star: "provably correct on
every axis V2 owns … structural-type-level enforcement plus per-axis property
tests." Transfer adds a new axis (bidirectional data movement) and earns it
the same way every other axis is earned — a forcing-function canary plus
typed structural commitments. It is **preview/migration tooling, not a
production write path**; the governance frame (§8) keeps it inside R6.

**How to think about it going forward (the critical-thinking grounding).**
Three habits keep this epic aligned:

1. **Reach for the adjunction, not a second pipeline.** Every time a slice
   feels like "build the reverse of X," ask: what is X's Ingestion peer, and
   does the existing direction-neutral plan already serve it? The answer is
   almost always "re-source and re-sink the plan you have."
2. **Direction is a binding, not an identity.** A substrate is a `Source` or
   a `Sink` *per Transfer*; staging is a sink on export and a source on the
   UAT load. Code that hard-codes "staging" or "target" into a type is a
   smell.
3. **Identity is the only deep problem.** Schema and value-codec round-trip
   for free (H-050 + `RawValueCodec`). Surrogate-key reconciliation under
   DML-only sink rights is where the real design lives — and it is now reified
   (`IdentityDisposition` + `SurrogateRemapContext`).

---

## 1. The operator scenario (restated)

Today the pipeline runs one leg — Projection onto a staging substrate:

```
OutSystems model ──Π (Projection)──▶ Catalog ──passes──▶ SSDT DDL + bootstrap + static seed ──▶ staging SQL Server
```

The staging SQL Server is consumed by a partner team that maps to the
schema and runs SSIS ETL from a legacy application database. The staging
DB is periodically refreshed with the current shape of the replacement
OutSystems application. Three uses are in play:

1. **External-entity cutover prep** — the team's cloud schema + data is
   migrated so OutSystems External Entities can drive on-prem integrations.
2. **Blank migration target** — a blank DB carrying the schema is handed to
   another team as the destination for production data coming from a legacy
   application the OutSystems project replaces.
3. **Iterating staging DB** — the partner team practises SSIS ETL against an
   intermediate staging DB, refreshed as the OutSystems app evolves. At
   "eject" time they run the migration tool for real against on-prem.

**The ask.** Using the schema understanding the pipeline *already* derives
during emission, freeze the schema and hand the copy to the team **as
usual**, then — using that **same produced schema** as the `SchemaContract`
— run a **Transfer**: ingest from the staging DB (today the *sink* of
Projection, here re-bound as the **Source**) and project the rows into an
**OutSystems Cloud UAT database** (the **Sink**). This is explicitly a
**temporary preview data-load** before the wholesale "eject," targeting
**one UAT cloud DB only**; dev/qa eject normally later.

```
staging SQL Server ──Ingestion──▶ (SchemaContract) ──two-phase Projection──▶ OutSystems Cloud UAT
       (Source)                                                                   (Sink)
```

The agreed transfer strategy is the **two-phase nulls-then-FKs Tarjan
plan**: phase 1 inserts rows with cycle-breaking FK columns nulled; phase 2
wires those FKs in topological dependency order once the target rows exist.

---

## 2. Bottom line up front

**The pipeline is unusually well-positioned for this**, because the hard
parts are already built, load-bearing, and tested — not as a "reverse"
feature, but as machinery the forward path and the canary already depend on:

- The **two-phase nulls-then-FKs Tarjan plan is already implemented** in the
  data-emission "MERGE lane" (`DataInsertScript` / `StaticSeedsEmitter` /
  `MigrationDependenciesEmitter` / `DataEmissionComposer`). The strategy the
  operator names is the strategy the code already runs.
- The **Ingestion reader already exists**: `ReadSide.readRowsStream` streams
  any table's rows out of a live SQL Server into the canonical in-memory row
  IR (`StaticRow`).
- The **Projection-onto-SQL realization already exists**: `Bulk.copyRows`
  (`SqlBulkCopy`) and `Deploy.executeStream`.
- The **adjunction round-trip is already exercised**: the canary deploys to
  a DB, ingests it back via `ReadSide`, re-projects, deploys to a second DB,
  and asserts structural equality (this *is* H-050 at runtime). Cross-database
  read+write orchestration is not new territory.
- The **key-remap pattern already exists** (`UserRemapContext` /
  `UserFkReflowPass`) — the exact "map source ids to target ids, re-point
  FKs, skip-and-diagnose unmatched" shape, now generalized to all kinds by
  `SurrogateRemapContext` (slice 1, shipped).

**A Transfer is, architecturally, the existing two-phase data plan
re-sourced and re-sinked.** On export, the plan's rows come from the OSM
model and the sink is staging. On the UAT load, the rows come from staging
and the sink is UAT. The plan computation in between — topological order,
cycle detection, deferred-FK selection, raw-value codec — is
**direction-neutral** and reused wholesale.

**What is genuinely new** is bounded and identifiable:

1. An **Ingestion adapter** that streams `StaticRow`s per kind in dependency
   order from a `Source`, given a `SchemaContract`.
2. A **two-endpoint connection seam** (Source = staging, Sink = UAT), which
   must live **outside** `Config` to respect the secret-free guardrail (D9).
3. A **Transfer orchestrator** in `Projection.Pipeline` that opens both
   endpoints, ingests in dependency order, and projects the two-phase plan
   against a **pre-existing** sink (bypassing the canary's ephemeral-container
   / `CREATE DATABASE` scaffolding).
4. An **identity decision per kind** — `PreservedFromSource` vs
   `AssignedBySink` (`IdentityDisposition`, shipped) — and, for the
   sink-assigned case, **assigned-key capture** during phase-1 insert into a
   `SurrogateRemapContext` (shipped); no `OUTPUT`/`SCOPE_IDENTITY` capture
   wiring exists yet at the realization layer.
5. **Persisting `SsKey` into the frozen `SchemaContract`** so a *later*
   Transfer can recover OutSystems identity from disk (today the on-disk
   artifact recovers logical *names*, not identity).
6. A **`transfer` CLI verb** + a **governance frame** for a non-production
   write path that respects R6.

**Recommended first cut for the stated scenario** (blank UAT, preview load):
**preserve-identity** — every kind treated as `PreservedFromSource`. Because
the UAT target is a *blank* database carrying the frozen schema, we preserve
the staging surrogate keys via `SqlBulkCopy`'s `KeepIdentity` (already the
forward default) — which **eliminates the entire remap problem**. Two-phase
is still needed to break FK cycles, but phase 2 simply restores the original
FK values (exactly what the forward path already does). Remap
(`AssignedBySink`) is the harder, later capability for non-empty / managed
targets — and `SurrogateRemapContext` is the primitive that will carry it.

---

## 3. The central architectural insight — the adjunction made bidirectional

The pipeline already separates a **direction-neutral plan** from a
**direction-specific realization**, codified as A35 / A36:

- **A35** — Π's canonical output is a typed deterministic *statement stream*
  (`seq<Statement>`); realization layers consume the stream and choose their
  emission form.
- **A36** — bulk-vs-incremental is *realization-layer policy*. How a
  realization deploys (`SqlBulkCopy`, per-row INSERT, file write) is invisible
  to Π.

The two-phase data plan inherits this split exactly. The plan IR
(`DataInsertScript`) names *what* must happen and *in what order*; the
`Rendered*` strings and `Bulk.copyRows` are *one* realization (project to
staging). A UAT load is simply **another Projection realization of the same
plan**, pointed at a different `Sink` — and fed by a different `Source` via
Ingestion.

```
            ┌─────────────────── direction-neutral plan ───────────────────┐
 SOURCE ──▶ │  rows → Catalog.Static modality → DataInsertScript plan       │ ──▶ SINK
(Ingestion) │   (TopologicalOrder · deferredColumns · RawValueCodec)        │  (Projection)
            └───────────────────────────────────────────────────────────────┘
 export:   OSM model                                                            staging DB  (render SQL / Bulk)
 UAT load: staging DB (ReadSide.readRowsStream)                                 UAT DB      (Bulk / executeStream)
```

This is the load-bearing claim of the whole prescope, and it is just H-050
read constructively: **we are not building a reverse pipeline; we are
binding the two legs of an adjunction the codebase already proves at the
schema level, and extending the proof to data.** The data-level canary
(§10, Slice C) is that extended proof.

---

## 4. The reified vocabulary (the lexicon, locked)

Per `DECISIONS 2026-05-24`, the Transfer epic's ubiquitous language is fixed.
These names are the same across Core / Adapters / Pipeline / CLI (pillar 8).

| Term | Meaning | Status |
|---|---|---|
| **Projection** (Π) | Lower a `Catalog` (+ rows) onto a substrate. The existing emitter direction. | Built (forward path). |
| **Ingestion** | Π's named peer: lift a substrate back into a `Catalog` (+ rows). The reader leg of the adjunction; `ReadSide` is today's schema-level Ingestion. | Reader exists; row-stream Ingestion adapter is a slice. |
| **Transfer** | `Ingestion(Source)` then `Projection(Sink)` over one shared `SchemaContract`. The flow. | Orchestrator is a slice. |
| **Source / Sink** | The flow-relative role a substrate plays in a Transfer (`SubstrateRole`). Not intrinsic — staging is a Sink on export, a Source on the UAT load. **A Sink is not write-only:** a reconcile Transfer *reads* (profiles) the Sink's identity population before writing it. | Built (`SubstrateRole`, slice 1). |
| **Substrate / Environment / TransferConnections** | The reified **connection apparatus** (§4.1): an `Environment` (DEV/TEST/UAT/PROD or named); a `Substrate` binding an environment to a `SubstrateRole` with credentials resolved out-of-band (D9); `TransferConnections` the set a Transfer binds, expressing which are profiled and which are concurrent. | Concept; a slice (unifies the multi-env + LiveOssysConnection deferrals). |
| **SchemaContract** | The frozen, reloadable schema artifact carrying `SsKey` + the FK edge graph + physical coordinates — the contract both legs share. Today the in-memory `Catalog` is a complete contract; the on-disk artifact is not (§7). | In-memory complete; on-disk artifact is a slice. |
| **IdentityDisposition** | Per-kind, three variants: `PreservedFromSource` (business/non-identity PK; source key written directly), `AssignedBySink` (identity PK; sink mints a *new* surrogate → capture-during-insert + remap), and `ReconciledByRule` (the referenced rows already exist in the Sink; match source↔sink identity by operator ruleset *before* insert → remap). `ofKind` derives `PreservedFromSource` vs `AssignedBySink` from `IsIdentity`; `ReconciledByRule` is an operator-chosen override. | `AssignedBySink` / `PreservedFromSource` built (slice 1); **`ReconciledByRule` is a planned third variant** (its engine — `UserFkReflowPass` — exists). |
| **SourceKey / AssignedKey** | Orientation-typed surrogate raw values — the surrogate-remap analog of `SourceUserId` / `TargetUserId`. | Built (slice 1). |
| **SurrogateRemapContext** | Per-kind `Source → Sink` surrogate map; the per-kind generalization of `UserRemapContext`. Carries both the `AssignedBySink` (captured-during-insert) and `ReconciledByRule` (matched-by-rule) mappings — the carrier is the same; only how the `AssignedKey` is *discovered* differs. | Built (slice 1). |

**Relationship to `UserRemapContext` / `UserFkReflowPass`.** These are not a
separate feature — they are the **`ReconciledByRule` engine for the User
kind**, already built and tested. `UserFkReflowPass.discover (sourceUsers)
(targetUsers)` matches a source population against the Sink's population by
operator ruleset (`UserMatchingStrategy = ByEmail | BySsKey | ManualOverride |
FallbackToSystemUser`) and produces a `UserRemapContext` — the single-kind
instance of `SurrogateRemapContext`. The Transfer epic generalizes it along two
axes: from User-only to any kind, and from static model populations to **live
dual-environment profiling** (§4.1). §11 records the subsumption of
`UserRemapContext` into `SurrogateRemapContext` as a trigger-gated candidate.

### 4.1 The connection apparatus

Today the forward path has exactly **one** connection
(`PROJECTION_MSSQL_CONN_STR` → a single server's `master`). A Transfer needs a
*set* of environment-bound connections, and the `ReconciledByRule` case proves
why a thin two-endpoint env-var seam is insufficient: to re-key Dev data into
UAT (user Id 280 in Dev, 18 in UAT), the apparatus must **profile both
environments' identity populations concurrently** *before* writing — the Sink
is read first, then written. The reified shape (credentials always out-of-band
per D9):

- **`Environment`** — a logical environment identity (DEV / TEST / UAT / PROD,
  or a named string). The multi-environment dimension the V1 corporate remote
  already carries (the deferred "Multi-environment config" cluster).
- **`Substrate`** — an `Environment` bound to a `SubstrateRole` (`Source` /
  `Sink`) with a `ConnectionRef` (a *reference* — env-var name or file path —
  not the secret). The thing you open.
- **`TransferConnections`** — the set a Transfer binds: which substrate is the
  data Source, which is the write Sink, which substrates are **profiled for
  identity reference** (for `ReconciledByRule`, both ends are profiled), and
  the concurrency they require (the operator's V1 "two concurrent connections"
  — Source + Sink open at once during reconcile-profiling). The V1 "up to four
  source connections" maps to the four environments; a single Transfer binds a
  pair.

This apparatus is the convergence point of three concerns the backlog
previously logged separately — the Transfer epic, the deferred
*Multi-environment config (DEV/TEST/UAT/PROD) + UAT-users*, and the deferred
*LiveOssysConnection* (the live read path). The `ReconciledByRule` UAT-user
re-key is the use case that unifies them: it needs live reads of two
environments, the multi-env connection set, and the `UserFkReflowPass` engine
in one operation.

---

## 5. What already exists — the reuse surface

### 5.1 The two-phase nulls-then-FKs plan (the named strategy, already built)

The forward emission has two lanes. The **MERGE lane** (`StaticSeedsEmitter` +
`MigrationDependenciesEmitter` + `BootstrapEmitter`, composed by
`DataEmissionComposer`) **already implements the two-phase strategy the
operator describes**, end to end:

- **Plan IR** — `DataInsertRow` carries `KindKey`, `Identifier`,
  `Values : Map<Name, SqlLiteral>`, and crucially `DeferredFkSet : Set<Name>`
  — the column names that cycle-break across the two phases
  (`DataInsertScript.fs:26-56`). `DataInsertScript` splits into
  `Phase1Merges` / `Phase2Updates` and renders `RenderedPhase1` /
  `RenderedPhase2` / `Rendered` (`DataInsertScript.fs:82-117`).
- **Deferred-FK selection** — a pure predicate: a column defers iff its kind
  is in a cycle, its FK target is in the same cycle, and the source column is
  nullable (`StaticSeedsEmitter.fs:99-112`;
  `MigrationDependenciesEmitter.fs:136-149`). Not-null FKs in a cycle cannot
  defer (they surface as a diagnostic).
- **Phase 1** — deferred columns emit as `SqlLiteral.NullLit` in the MERGE
  VALUES and are excluded from the `WHEN MATCHED UPDATE` set
  (`StaticSeedsEmitter.fs:142-153`, `:192-196`).
- **Phase 2** — an `UPDATE … SET <deferred> = <orig> WHERE <pk>` wires the
  FKs once phase-1 rows exist (`StaticSeedsEmitter.fs:226-257`).
- **Global ordering** — `DataEmissionComposer.composeRenderedFull` walks
  kinds in topological order and concatenates **all** Phase-1 across all kinds
  **before** any Phase-2 (`DataEmissionComposer.fs:287-326`). A level-parallel
  sibling `composeRenderedLeveled` uses `TopologicalOrder.levels`
  (`DataEmissionComposer.fs:347-393`).

This is precisely "phase 1: insert with FK nulled; phase 2: update FKs in
dependency order," Tarjan-derived.

### 5.2 Topological order + cycle detection (direction-neutral)

`TopologicalOrderPass` runs Kahn's algorithm with Tarjan SCC detection,
producing a `TopologicalOrder` with an ordered `Order : SsKey list`, Kahn
`levels` (parallel-safe batches), and `Cycles : CycleDiagnostic list`
(`TopologicalOrder.fs:130-137`, `:211-235`). Self-references are first-class
via `SelfLoopPolicy = TreatAsCycle | SkipSelfEdges` (`TopologicalOrder.fs:40-52`).
This is pure graph algebra over the catalog — it drives phase-2 sequencing
for a Transfer directly.

One caveat: the forward cycle *resolver* (`CycleResolution`) only breaks
2-member SCCs with exactly one weak (nullable, `NoAction|SetNull`) edge;
larger cycles fall back to alphabetical order. The Transfer strategy is **more
aggressive** — null *every* deferrable FK in phase 1 regardless of cycle
membership — so a Transfer can bypass the resolver and use `Order` / `levels`
purely for phase-2 sequencing.

### 5.3 The Ingestion row-reader (already exists)

`ReadSide.readRowsStream (cnn) (kind) : AsyncStream<StaticRow>` issues
`SELECT <cols> FROM <schema.table> ORDER BY <pk>` and materializes each SQL
row into `StaticRow { Identifier; Values : Map<Name,string> }` via
`formatRawValue` (`ReadSide.fs:494-599`). The values are canonical
invariant-culture raw strings — **the same raw-string IR the seed emitters
consume**. This is the inverse of the emitter's input and the natural feed
for the Transfer plan.

> **Identity nuance (verified).** `readRowsStream` synthesizes a per-row
> `SsKey` from a `READSIDE_ROW` basis (`schema.table.rowIdx`,
> `ReadSide.fs:578-587`) — **not** the OutSystems entity GUID. For
> `PreservedFromSource` this is irrelevant (the PK value rides in `Values` and
> `KeepIdentity` copies it). For `AssignedBySink` it matters: row correlation
> needs a business/natural key, not the synthesized SsKey. This is the same
> correlation gap the `SurrogateRemapContext` capture step must close (§6.2).

### 5.4 The Projection-onto-SQL realization (already exists)

`Bulk.copyRows` performs a real `SqlBulkCopy.WriteToServerAsync` with
`SqlBulkCopyOptions.KeepIdentity ||| KeepNulls` — the docstring states
"`KeepIdentity` is honored so source PKs survive across the round-trip"
(`Bulk.fs:89-106`). `Deploy.executeStream` consumes a `seq<Statement>`,
routing DDL to batch execution and `InsertRow` runs to `Bulk.copyRows`
(`Deploy.fs:710-833`). `RawValueCodec` is bidirectional and round-trip stable
by design; the reader (`ReadSide.formatRawValue`) and the writer
(`Bulk.parseRaw`, `""` → `DBNull`) converge on it (`Bulk.fs:39-67`).

**`KeepIdentity` is the linchpin of `PreservedFromSource`.** It is already the
forward default, and it is exactly what a blank-target preview load wants.

### 5.5 The adjunction round-trip is already a load-bearing pattern

The **canary** is V2's primary wide integration surface (per CLAUDE.md). It
deploys an OutSystems-shaped source DDL to an ephemeral DB, ingests it back
via `ReadSide` into a `Catalog`, runs the emitter on the reconstruction,
deploys to a second ephemeral DB, ingests *that* back, and asserts source ≈
target on `PhysicalSchema`. This *is* H-050 at runtime. A Transfer is a close
cousin of half this loop: ingest a live DB → feed the plan → project a live
DB. The orchestration shape is precedented; `Deploy.runWideCanary` is the
structural template, and the **data-level canary** (Slice C) is its data
analog.

### 5.6 The key-remap pattern (now generalized in slice 1)

`UserRemapContext` is `{ Mapping : Map<SourceUserId, TargetUserId>; Unmatched;
Diagnostics }` (`UserRemap.fs:81-98`), produced by `UserFkReflowPass.discover`
matching source users against the target population by email / SsKey
(`UserFkReflowPass.fs:334-348`), and consumed at emit time by
`MigrationDependenciesEmitter.rewriteUserFkColumns`, which substitutes the
target id per User-FK column and **skips the row with a diagnostic if
unmatched** (`MigrationDependenciesEmitter.fs:303-344`). The orientation
newtypes `SourceUserId` / `TargetUserId` (`UserIdentity.fs:24-33`) prevent
passing a source id where a target id belongs.

Slice 1 generalized exactly this shape from user-only / `int→int` to
**per-kind catalog-wide** `SurrogateRemapContext` — `Assignments :
Map<SsKey, Map<SourceKey, AssignedKey>>`, captured during phase-1 insert and
consulted in phase 2 to re-point FKs (`Transfer.fs`). The orientation newtypes
`SourceKey` / `AssignedKey` mirror the User pair.

### 5.7 The schema is a bidirectional contract — in memory

The in-memory `Catalog` / `Kind` / `Attribute` model co-locates logical
identity (`SsKey`, including `OssysOriginal of Guid` — the stable OutSystems
entity GUID) with full physical realization (`TableId`, `ColumnName`, types,
nullability, PK/identity flags) and the FK graph (`Reference` with `TargetKind
: SsKey`, on-delete/update, `IsUserFk`, `HasDbConstraint`). See
`Catalog.fs:804-890`, `:410-585`. The SsKey-keyed indices `kindIndex` /
`attributeIndex` already exist (`Catalog.fs:1287`, `:1129`). This is already
an excellent `SchemaContract` **in memory** — see §7 for the persistence gap.

### 5.8 Architecture seams (where Transfer plugs in)

- Emitters are `Emitter<'element> = Catalog -> Result<ArtifactByKind<'element>>`,
  open-coded in `Compose.project` (`Pipeline.fs:262-320`). Registration
  (`TransformRegistry` / `StrategyRegistrations`) is metadata-only, not a
  dispatch mechanism.
- The CLI is a hand-rolled `match` over `argv` with verbs
  `full-export | emit | skeleton | approve | deploy | canary`
  (`Program.fs:620-659`). Adding a `transfer` verb is low-disruption.
- **`Projection.Pipeline` is the only project that references both the SQL
  adapter and every target** — `Deploy.fs` and `Bulk.fs` already live there
  with `Microsoft.Data.SqlClient`. It is the natural home for the Transfer
  orchestrator. **No change to `Pipeline.fs`'s export flow is required.**
- `Projection.Core` is **pure** — zero I/O, no `Task`, no time/random —
  enforced structurally by the `NoUnsafeTimeInCoreAnalyzer` (PRJ001). Transfer
  SQL I/O lives in an adapter or in Pipeline; pure ordering/remap logic lives
  in Core (this is where `Transfer.fs` and the future Transfer-plan computation
  live).

---

## 6. Identity: the three dispositions (the crux)

Loading row data **into** OutSystems Cloud raises the only genuinely deep
question: **whose surrogate keys win?** The answer is a per-kind
`IdentityDisposition` with three variants — `PreservedFromSource` and
`AssignedBySink` (reified in slice 1) and `ReconciledByRule` (the
cross-environment match; planned variant whose engine already exists).

### 6.1 `PreservedFromSource` (recommended first cut)

**When:** the kind's PK is not an identity column, *or* the whole Transfer
runs against a **blank** sink carrying the frozen schema (the operator's
stated case — "deploy a blank database").

**How:** insert rows preserving the source surrogate PKs via `SqlBulkCopy`'s
`KeepIdentity` (already the forward default, `Bulk.fs:105`). Because keys are
preserved, **no remap is needed at all**:

- Phase 1 inserts every row with cycle-breaking FK columns nulled, identity
  preserved, in any order (`KeepIdentity` + `KeepNulls`).
- Phase 2 restores the deferred FK columns to their **original** values in
  topological order — *exactly* what the forward phase-2 UPDATE already does
  (`StaticSeedsEmitter.fs:226-257`). No value rewriting; the original FK value
  is still correct because the referenced PK was preserved.

**Why this is the right first cut:** it collapses the Transfer to "ingest rows
→ run the existing plan → project against the UAT Sink with `KeepIdentity`."
The entire remap apparatus (assigned-key capture, FK re-pointing, correlation
keys) evaporates. The only new code is the Source/Sink plumbing and the
orchestrator.

**Risk:** if the UAT DB is *not* blank — if OutSystems has already seeded rows
in overlapping identity ranges, or the platform forbids `IDENTITY_INSERT` on
its managed tables — `PreservedFromSource` collides. The operator's scenario
says blank, so this is acceptable for the preview; `AssignedBySink` is the
escape hatch.

### 6.2 `AssignedBySink` (harder, later — primitive now exists)

**When:** the sink is non-empty, or OutSystems must assign its own `Id` values
(platform-managed identity). `IdentityDisposition.ofKind` already classifies
this from `IsIdentity`.

**How:** the generalization of `UserRemapContext`, now reified as
`SurrogateRemapContext`:

1. Phase 1 inserts rows **without** preserving identity; the sink assigns new
   keys.
2. **Capture** the `(SourceKey → AssignedKey)` pair per row into the
   `SurrogateRemapContext` via `capture`. *This is the principal missing
   realization-layer wiring* — no `OUTPUT inserted.*` / `SCOPE_IDENTITY`
   capture exists yet. Capture requires either per-row INSERT with `OUTPUT`,
   or a post-insert correlation by a stable business/natural key (since
   `SqlBulkCopy` does not return assigned ids). This interacts with the
   reader's synthesized SsKey limitation (§5.3) — a natural key must be
   identified per kind.
3. The `SurrogateRemapContext` accumulates a catalog-wide `Map<SsKey,
   Map<SourceKey, AssignedKey>>` (built; smart-constructor invariant enforces
   one assignment per source surrogate per kind).
4. Phase 2 re-points **every** FK column (not just cycle-deferred ones) to the
   remapped `AssignedKey` via `tryFindAssigned`, skipping-and-diagnosing
   unmatched references (a `None` from `tryFindAssigned` is the orphan-FK
   signal) — the `UserFkReflowPass` discipline, generalized.

`AssignedBySink` is a strictly larger effort and should be its own chapter.
The `PreservedFromSource` first cut delivers the operator's preview without it,
and slice 1 already lays the type-level foundation it will stand on.

### 6.3 `ReconciledByRule` (cross-environment match by operator ruleset)

**When:** the referenced rows **already exist** in the Sink, independently of
the Transfer — the dominant case being **Users**. The same human is user Id
280 in Dev and Id 18 in UAT; loading Dev data into UAT requires every User-FK
to be re-keyed from the Dev surrogate to the *pre-existing* UAT surrogate. This
is neither `PreservedFromSource` (280 is wrong in UAT) nor `AssignedBySink`
(UAT does not mint a fresh key — Id 18 already exists). It is an operator-chosen
disposition, not derivable from `IsIdentity`.

**How — discover-by-profiling-both-ends, then match:**

1. **Profile both environments' identity populations** via the connection
   apparatus (§4.1): read the Source population (Dev users) and the Sink
   population (UAT users) — the Sink is *read first*, concurrently with the
   Source. This is why a reconcile Transfer needs ≥2 concurrent connections.
2. **Match by operator ruleset.** `UserMatchingStrategy = ByEmail | BySsKey |
   ManualOverride of Map<SourceUserId,TargetUserId> | FallbackToSystemUser`
   reconciles source identities to pre-existing sink identities. This is the
   "operator-configured rulesets" the V1 corporate remote already implements.
3. **Build the remap into `SurrogateRemapContext`** — same carrier as
   `AssignedBySink`; the difference is *when* and *how* the `AssignedKey` is
   discovered (matched-before-insert here vs captured-during-insert there).
4. **Phase 2 re-points** every reconciled FK to the matched sink key,
   **skipping-and-diagnosing unmatched** source identities (no match → orphan
   diagnostic, the row is skipped — V1's "diagnostic + skip" behavior).

**The engine already exists.** `UserFkReflowPass.discover (sourceUsers:
UserPopulation<SourceUserId>) (targetUsers: UserPopulation<TargetUserId>)`
produces exactly this remap (`UserRemapContext`), consumed today by
`MigrationDependenciesEmitter.rewriteUserFkColumns` in the *forward* path. The
Transfer's `ReconciledByRule` is that pass fed from **live dual-environment
profiling** instead of static model populations, and generalized from the User
kind to any kind the operator marks reconcilable. So `ReconciledByRule` is
mostly a *wiring* effort over built machinery, plus the live-profiling
connection apparatus — not a from-scratch capability.

### 6.4 Decision to surface

> **OPEN-1.** For the UAT preview, is the target guaranteed blank, and does the
> OutSystems Cloud UAT DB permit direct writes / `IDENTITY_INSERT` on the
> entity-backing tables? If yes → `PreservedFromSource` is sufficient. If the
> target is **non-empty with pre-existing Users** (the realistic UAT case) →
> `ReconciledByRule` for the User kind (live dual-profile + ruleset match) is
> required; if the platform mints its own keys for newly-loaded entity rows →
> `AssignedBySink` with assigned-key capture. A real UAT load may mix all three
> dispositions across kinds.

---

## 7. The `SchemaContract` problem (SsKey persistence)

The operator's framing is "**freeze** the schema, copy it for the team, then
use **that same produced schema** to drive the Transfer." Two sub-cases:

- **Same-run Transfer** — the Ingestion happens in the same process that still
  holds the live `Catalog`. The in-memory `SchemaContract` is complete (§5.7);
  nothing is missing.
- **Later Transfer from a frozen artifact** — the Ingestion happens in a
  *separate* run that must reload the `SchemaContract` from disk. **Here the
  on-disk artifact is currently insufficient for identity.** The manifest
  (`ManifestEmitter.fs:473-893`) persists physical coordinates and counts,
  plus `DeploymentBatches` (the FK-safe topological order as
  `SsKey.rootOriginal` strings), but **not** the per-column attribute mapping,
  the FK edge graph, or `SsKey`. The emitted DDL recovers the logical *name*
  via the `V2.LogicalName` extended property (`PhysicalSchemaReader.fs:112-132`)
  but **`SsKey` / `OssysOriginal` is never written to any artifact** — it lives
  only in memory.

For `PreservedFromSource` this gap is **tolerable**: Ingestion keys off
physical names and PK values, and identity reconciliation is not required. For
`AssignedBySink` it is **load-bearing**: relinking a staging row to its
OutSystems entity by name is fragile under rename, whereas `OssysOriginal` is
rename-stable (A1).

**Recommended enabling change (cheap, high-leverage):** reify a serialized,
re-loadable **`SchemaContract`** — a projection of `Catalog` carrying
per-table `{ TableId, SsKey, Name }`, per-column `{ ColumnName, SsKey, Name,
Type, Nullable, IsPrimaryKey, IsIdentity }`, and FK edges `{ source, target,
TargetKind SsKey }` — plus a `V2.SsKey` extended property sibling to
`V2.LogicalName` so even a bare-DDL reload recovers identity. This also gives a
clean home for a **physical-coordinate-keyed index** (`Map<TableId, Kind>`, the
inverse of the existing SsKey-keyed index) that Ingestion needs to map physical
names the staging DB hands back to logical entities.

> This dovetails with **H-010** (the Catalog ↔ DDL `Prism`, whose `ReverseGet
> = ReadSide.readCatalog`) — Ingestion is the `Prism`'s reverse leg made to
> carry **identity**, not just structure. The `SchemaContract` is what lets the
> `Prism` round-trip identity across a process boundary.

---

## 8. Governance & axiom alignment

A Transfer that **writes to a UAT database** crosses the project's single most
sensitive guardrail. This is the honest accounting.

### 8.1 R6 — "V2 owns no production write path during dual-track"

Per `DECISIONS 2026-05-22 — R6`, during the dual-track window V2
emits-but-doesn't-ship; V1 owns the production write path; the canary asserts
V1 ≈ V2 modulo named tolerances. A Transfer is a **new write path** and must be
framed so it does **not** violate R6:

- **It targets UAT preview, not production.** The operator's scenario is
  explicit and bounded: one UAT cloud DB, temporary, pre-eject. A
  development/preview write, categorically outside the production write path V1
  owns.
- **It is opt-in and separately gated.** A distinct CLI verb, a distinct
  connection seam, and (recommended) a **dry-run-by-default** posture: the
  Transfer *emits the two-phase script* by default and only *executes* against
  the UAT Sink under an explicit `--execute` + operator-supplied connection —
  mirroring how the canary only ever executes against ephemeral DBs.
- **It must be named in DECISIONS before it ships an execute path.** R6 is
  non-negotiable without a superseding entry. The amendment states the
  preview-only scope, the UAT-only target, and the dry-run default.

### 8.2 D9 — secret-free config

`Config` carries no connection/credential field by construction and actively
rejects credential-shaped JSON keys via a `credentialSignatures` scan
(`Config.fs:12-16`, `:345-390`). The Transfer's **two connection endpoints must
not live in `Config`.** They arrive via environment variables (the forward
`Deploy` precedent is `PROJECTION_MSSQL_CONN_STR`) or a CLI-flag-referenced file
path. Today there is exactly one connection-string source pointing at a single
server's `master`; a Transfer needs **two distinct endpoints** (Source staging
server, Sink UAT server) each with its own credentials — net-new plumbing,
deliberately outside `Config`.

### 8.3 Pure-core / A18 / pillar 9

- **Pure core.** The SQL read (Source / Ingestion) and SQL write (Sink /
  Projection) live in adapters / Pipeline, never Core. Pure ordering and remap
  logic may live in Core (`Transfer.fs`, and the future Transfer-plan). Enforced
  by PRJ001.
- **A18.** Emitters consume `Catalog × Profile`, never `Policy`. The Transfer's
  "which rows, which disposition, which phase batching" is **operator intent**
  and must enter at the Pipeline boundary (like `EmissionFolders` /
  `TransformGroups` overlays), not inside the plan emitter.
- **Pillar 9 (DataIntent vs OperatorIntent).** A Transfer pass/realization is a
  registered transformation and must be classified. Ingesting rows from the
  Source is `DataIntent` (no operator opinion); the `IdentityDisposition`
  override, the Sink selection, and the dry-run/execute toggle are
  `OperatorIntent`. (`IdentityDisposition.ofKind` is `DataIntent` — it reads
  structure; an operator *forcing* a disposition would be `OperatorIntent`.)

### 8.4 CDC safety

`idempotentRedeploy` requires zero spurious CDC change records, because
downstream ETL interprets them as real changes and corrupts replicas
(`PRODUCT_AXIOMS.md:95`). A Transfer writes into the **UAT** Sink, but if that
Sink (or anything downstream of it) is CDC-tracked, the two-phase phase-2
UPDATEs would generate change events. The Projection realization should reuse
the forward CDC-aware discipline (phase-2 UPDATE touches only the deferred FK
axis) and the preview Sink should be confirmed CDC-free or CDC-tolerant.

### 8.5 Determinism (T1)

The plan is deterministic by construction (sorted by `SsKey`, `decimal`
evidence, no wall-clock in Core). The Projection realization inherits this: the
*order* of inserts/updates is the deterministic topological order; only the I/O
timing is nondeterministic, which is invisible to the plan.

### 8.6 The adjunction as a scaffolded axiom candidate

The data-level extension of H-050 (§0 North Star) should be **scaffolded as an
axiom candidate at the Transfer chapter's open** and cashed at its close, per
the "AXIOMS amendments scaffolded at chapter open; bodies filled at chapter
close" discipline. Candidate statement: *for a Transfer in `PreservedFromSource`
mode against a blank Sink, `Ingestion(Projection(rows)) = rows` on the row-digest
axis (the data analog of `PhysicalSchema.diff = ∅`); in `AssignedBySink` mode the
equality holds modulo the `SurrogateRemapContext` substitution.* Do **not** cash
it now — name it, test it via the data-level canary, then promote.

---

## 9. Proposed seams — the minimal change set

Ordered by leverage; none requires forking `Pipeline.fs` or touching Core
purity. **Slice 1 (vocabulary reification in `Transfer.fs`) is shipped**; the
following are the remaining seams.

1. **Transfer plan (pure, Core or `Targets.Data`).** Given a `SchemaContract`
   + ingested rows per kind, produce the identity-aware two-phase plan:
   classify each kind's `IdentityDisposition`, order by `TopologicalOrder`,
   select deferred-FK columns. Reuses the existing deferred-FK predicate and
   topo pass. Pure and fully unit-testable without a DB.

2. **Ingestion adapter (`Projection.Adapters.Sql`).** A thin composition over
   `ReadSide.readRowsStream`: given a `SchemaContract` and a Source connection,
   stream `StaticRow`s per kind in `TopologicalOrder.Order`. Mostly reuse.

3. **Projection-onto-Sink realization (`Projection.Pipeline`).** Reuse
   `Bulk.copyRows` (`PreservedFromSource`, `KeepIdentity`) and the two-phase
   ordering from `composeRenderedFull` / `composeRenderedLeveled`. Phase 1:
   bulk-insert with deferred FKs nulled. Phase 2: UPDATE deferred FKs in topo
   order. Target a **caller-supplied open connection**, bypassing the canary's
   `useContainer` / `createDatabase` scaffolding. (`AssignedBySink` adds the
   `OUTPUT`-capture step feeding `SurrogateRemapContext.capture`.)

4. **Transfer orchestrator (`Projection.Pipeline`, sibling to `Compose`).**
   `loadContract → openSource → openSink → forEach kind in topo order: ingest
   rows → plan → project two-phase → diagnostics`. Where Source + Sink endpoints
   meet; `Pipeline.fs` is untouched.

5. **Connection apparatus (outside `Config`).** The reified `Environment` /
   `Substrate` / `TransferConnections` set (§4.1): a multi-environment,
   role-bound, concurrency-aware connection set with credentials resolved
   out-of-band (env vars keyed by environment / `--*-connection-file` flags;
   D9). For `PreservedFromSource` it degenerates to two endpoints (Source +
   Sink, write-only Sink); for `ReconciledByRule` both ends are opened for
   *profiling* concurrently before the Sink is written. This unifies the
   deferred "Multi-environment config (DEV/TEST/UAT/PROD)" + "LiveOssysConnection"
   concerns.

6. **`ReconciledByRule` identity profiling + reflow.** Profile the Source and
   Sink identity populations via the apparatus, apply the operator
   `UserMatchingStrategy` ruleset (reusing `UserFkReflowPass.discover`),
   generalize from the User kind to any reconcilable kind, and feed the result
   into the phase-2 FK re-point through `SurrogateRemapContext`. Mostly wiring
   over built machinery + the live-profiling apparatus.

7. **CLI verb `transfer` (`Projection.Cli`).** One `match` arm + a
   `TransferArgs.fs` (mirror `FullExportArgs`), with `--contract`,
   `--disposition preserve|assign|reconcile` (per-kind override; default
   preserve), `--user-map <file>` (the `ManualOverride` ruleset),
   `--dry-run` (default) / `--execute`, `--preview-row-cap`.

8. **`SchemaContract` persistence (`Targets.SSDT`).** `V2.SsKey` extended
   property and/or the serialized re-loadable `SchemaContract` artifact (§7).
   Enables *later-run* Transfer and is the prerequisite for `AssignedBySink`.

9. **(`AssignedBySink`, later chapter.)** Assigned-key capture (`OUTPUT` or
   natural-key correlation) feeding `SurrogateRemapContext`, catalog-wide FK
   re-pointing in phase 2. The type foundation shipped in slice 1.

---

## 10. Phased delivery plan — the epic backlog

Slices sized to the repo's cadence. **Slice 1 is shipped.**

- **Slice 1 — vocabulary reification (SHIPPED).** `Transfer.fs` in Core:
  `SubstrateRole`, `SourceKey` / `AssignedKey`, `IdentityDisposition` +
  `ofKind`, `SurrogateRemapContext` + smart constructor. 11 unit tests. Pure,
  no I/O. *This is the type-level spine every later slice stands on.*

- **Slice A — `SchemaContract` persistence.** Persist `SsKey` + FK graph into
  the frozen schema (extended property / serialized contract) + a
  physical-coordinate-keyed index. Property test: round-trip `Catalog →
  artifact → reload` preserves `SsKey` and the FK graph. *Unblocks later-run
  Transfer and `AssignedBySink`.*

- **Slice B — Transfer plan + Ingestion source.** The pure two-phase
  identity-aware plan (seam 1) + `Ingestion` streaming `StaticRow`s in
  topological order (seam 2). Test the plan with pure fixtures; test Ingestion
  against an ephemeral DB seeded by the forward emitter (the canary substrate).

- **Slice C — `PreservedFromSource` Projection-onto-Sink + orchestrator
  (dry-run) + the data-level canary.** Two-phase plan realized as a script for
  a caller-supplied Sink; dry-run emits the script. **The data-level canary:**
  forward-emit to DB-A → ingest → plan → project to DB-B → ingest DB-B →
  assert `PhysicalSchema` + row-digest equality against DB-A. *This is the
  extended H-050 proof and the highest-confidence deliverable: it proves the
  Transfer reconstructs source data faithfully before any UAT write.*

- **Slice C′ — connection apparatus + `ReconciledByRule` (cross-environment
  User re-key).** The reified `Environment` / `Substrate` / `TransferConnections`
  apparatus (§4.1) + live dual-environment identity profiling feeding
  `UserFkReflowPass.discover` + the operator `UserMatchingStrategy` ruleset
  (and the `ManualOverride` CSV loader), generalized from the User kind into the
  Transfer phase-2 reflow via `SurrogateRemapContext`. *This is the realistic
  UAT case* (UAT has pre-existing Users); it **unifies the deferred
  "Multi-environment config (DEV/TEST/UAT/PROD) + UAT-users" and
  "LiveOssysConnection" concerns** into the epic. Prerequisite for a useful
  Slice D against a non-blank UAT.

- **Slice D — execute against UAT (gated).** `--execute` against a real UAT
  connection, behind the R6 governance amendment, dry-run default, preview row
  cap, CDC-safety check. Operator sign-off gate. A real UAT load mixes
  dispositions: `PreservedFromSource` for business-key kinds, `ReconciledByRule`
  for Users (Slice C′), and — if the platform mints keys — `AssignedBySink`
  (Slice E).

- **Slice E — `AssignedBySink`. SHIPPED 2026-05-31 (acyclic; §5.2).** Assigned-key
  capture via per-row `INSERT … OUTPUT inserted.<pk>` (the `OUTPUT` route — `SqlBulkCopy`
  returns no ids), FK re-pointing via `SurrogateRemapContext` threaded through the
  topological Phase-1 loop. Distinct from C′: here the sink mints a *new* key
  (discover-during-insert), where C′ matches a *pre-existing* sink key
  (discover-by-profiling). Canary: `data adjunction: AssignedBySink round-trips modulo
  SurrogateRemapContext`. Cyclic AssignedBySink (self-ref IDENTITY) is a named follow-on
  (Phase-2 keys on the source PK, gone once the Sink mints).

The data-level canary in Slice C is the load-bearing deliverable — it earns
the epic's North Star (the data-level adjunction) the same way the schema
canary earns emitter fidelity. Slice C′ is the operator's headline case (the
Dev→UAT User re-key) and the unification point for the multi-environment
connection concerns.

---

## 11. What else should shift — in the document and the codebase

The maturer understanding surfaces a set of candidate shifts. Each is
classified *do-now* vs *candidate (trigger named)* to respect "IR grows under
evidence."

**Shipped already (this refresh + slice 1):**
- The doc renamed `PRESCOPE_REVERSE_IMPORT.md → PRESCOPE_TRANSFER.md`; "reverse"
  retired from the codebase.
- The vocabulary reified in `Transfer.fs` (slice 1).
- `DECISIONS 2026-05-24` records the locked lexicon + governing decisions.
- CLAUDE.md pillar-8 ubiquitous-language precedents extended with the Transfer
  vocabulary; BACKLOG.md carries the epic.

**Do-now-adjacent (next slices, already scoped above):**
- Name the **`SchemaContract`** as a first-class artifact (Slice A) rather than
  leaving the on-disk contract implicit in the manifest.
- The **data-level canary** as the sibling of the schema canary (Slice C) — the
  single highest-leverage verification surface for the epic.
- **The third disposition `ReconciledByRule`** + the **connection apparatus**
  (`Environment` / `Substrate` / `TransferConnections`, §4.1) are now recorded
  (this refresh): the operator's Dev→UAT User re-key is the headline case
  (Slice C′), and the apparatus is its enabling reification. When Slice C′
  opens, `IdentityDisposition` gains the `ReconciledByRule` variant (closed-DU
  expansion; exhaustiveness lights up at match sites).
- **Unify the deferrals.** The previously-separate "Multi-environment config
  (DEV/TEST/UAT/PROD) + UAT-users" and "LiveOssysConnection" deferrals are
  **subsumed into the Transfer epic** as Slice C′'s connection apparatus +
  live dual-profiling. They are no longer free-floating; `DECISIONS 2026-05-24`
  cross-references them.

**Candidates (trigger named; do not pre-build):**
- **Subsume `UserRemapContext` into `SurrogateRemapContext`.** `UserFkReflowPass`
  is the `ReconciledByRule` engine for the User kind; `UserRemapContext` is the
  single-kind instance of the general context. *Trigger:* when Slice C′ wires
  `UserFkReflowPass` output into the Transfer reflow (a second consumer of the
  general context), evaluate collapsing them (the two-consumer threshold). Until
  then they coexist; the prose in `Transfer.fs` and `UserRemap.fs`
  cross-references the relationship.
- **Promote the data-level adjunction to a numbered axiom.** *Trigger:* the
  Transfer chapter open scaffolds it (§8.6); the chapter close cashes it once
  the data-level canary is green. Sibling to H-050 / A35 / A36.
- **A physical-coordinate-keyed `Catalog` index (`Map<TableId, Kind>`).**
  *Trigger:* the Ingestion adapter (Slice B) is the first consumer that maps
  physical names → logical kinds; build it there, not before.
- **Generalize `Bulk` two-phase to the bulk lane (OPEN-5).** The high-throughput
  bulk lane does not implement two-phase cycle-breaking today. *Trigger:* a
  Transfer over a cyclic graph at bulk volume; until then per-row INSERT for
  cyclic kinds is acceptable.
- **Constraint/trigger handling on load (OPEN-6).** `NOCHECK` / disable-trigger
  handling for loading into a constrained schema. *Trigger:* Slice C surfaces a
  CHECK/computed/trigger collision against a real OutSystems-shaped Sink.

**Explicitly NOT shifting:**
- No new pipeline; no forking of `Pipeline.fs`'s export flow.
- No connection/credential fields in `Config` (D9 holds).
- No production write path; Transfer stays UAT-preview + dry-run-default (R6).
- No axiom cashed mid-flight; the adjunction-data axiom is scaffolded only.

---

## 12. Mapping to existing HORIZON / prior art

The Transfer epic is greenfield as a **data-flow goal** (no "eject," no
load-into-OutSystems, no bidirectional data movement existed), but it lands
cleanly onto existing structural prior art:

- **H-050 — the emitter/reader adjunction** (`HORIZON.md:1465`;
  `AdjunctionLawTests.fs`): `Ingestion ∘ Projection = id` up to lossy fields,
  proven at the schema level. **The Transfer epic IS this adjunction extended
  from schema to data across two substrates.** This is the epic's formal
  anchor, not a loose analogy.
- **H-010 — Catalog ↔ DDL `Prism`** (`HORIZON.md:415`): `Get =
  SsdtDdlEmitter.emit`, `ReverseGet = ReadSide.readCatalog`. Ingestion is the
  `Prism`'s reverse leg extended to carry identity (via the `SchemaContract`),
  not just structure.
- **H-058 — ReadSide reconstruction** and **H-089 — migration preview
  (dry-run)**: H-089 today renders the DDL stream as a human-readable change
  list; the Transfer dry-run is the **data** analog.
- **`LiveOssysConnection` re-open trigger** (`DECISIONS.md` Active deferrals):
  the documented seam for a live OutSystems connection (today framed as
  read-only metadata ingest). The Transfer is a write-direction cousin and
  should be cross-referenced when that trigger fires.
- **`UserFkReflowPass` / `MigrationDependencyContext`**
  (`CHAPTER_4_PRESCOPE_USERFK_REFLOW.md`, `CHAPTER_4_2_OPEN.md`): the remap
  discipline `SurrogateRemapContext` generalizes (slice 1).

---

## 13. Open questions / risks

- **OPEN-1 (identity).** Is the UAT target blank, or non-empty with
  pre-existing Users? And does it permit direct writes / `IDENTITY_INSERT`?
  (Gates the per-kind disposition mix — `PreservedFromSource` /
  `AssignedBySink` / `ReconciledByRule` — §6.4.)
- **OPEN-7 (connection apparatus scope).** How many environments must the
  apparatus bind, and what concurrency does the platform/license permit (the
  V1 "four connections, two concurrent")? Gates how rich the `TransferConnections`
  reification needs to be (§4.1).
- **OPEN-2 (platform write surface).** Does the OutSystems Cloud UAT DB expose
  a writable connection to the entity-backing tables at all, or must the load
  go through a platform API? The whole approach assumes direct SQL write access
  to the UAT database. *This is the single biggest external dependency and
  should be confirmed first.*
- **OPEN-3 (CDC).** Is the UAT target or its downstream CDC-tracked? (§8.4.)
- **OPEN-4 (governance).** R6 amendment scope — confirm "UAT preview, not
  production" framing and dry-run-by-default before any execute path.
- **OPEN-5 (bulk lane vs. two-phase).** The high-throughput `Bulk` lane does
  **not** today implement two-phase cycle-breaking (it relies on topological
  order + `SkipSelfEdges` and assumes acyclic-or-self-FK-only,
  `StaticPopulationEmitter.fs:124-138`). A Transfer (bulk) over a cyclic graph
  must graft the two-phase plan onto the bulk lane, or accept per-row INSERT for
  cyclic kinds.
- **OPEN-6 (constraints/triggers).** Loading into a schema with CHECK
  constraints, computed columns, or triggers may need `NOCHECK` /
  disable-trigger handling not present in the forward blank-deploy path.

**Resolved since the original prescope:**
- *Vocabulary* (was implicit): locked to Projection / Ingestion / Transfer /
  Source / Sink / `SchemaContract` / `SurrogateRemapContext` /
  `IdentityDisposition` (`DECISIONS 2026-05-24`).
- *Identity primitive* (was "principal missing primitive"): the per-kind remap
  context and disposition are reified in `Transfer.fs` (slice 1); what remains
  is the realization-layer *capture wiring*, not the type.
- *CLI verb name*: `transfer` (was `import`), matching the flow concept.

---

## 14. One-paragraph synthesis

The pipeline already contains the hard machinery of a Transfer — a working
two-phase nulls-then-FKs Tarjan plan (`DataInsertScript` +
`DataEmissionComposer`), a live Ingestion reader (`ReadSide.readRowsStream`), a
bulk Projection writer with identity preservation (`Bulk.copyRows`,
`KeepIdentity`), a bidirectional value codec (`RawValueCodec`), an exercised
read-back round-trip (the canary, which *is* H-050 at runtime), and a key-remap
template now generalized to all kinds (`SurrogateRemapContext`, slice 1). A
Transfer is the *same direction-neutral plan* (A35/A36) re-sourced via
Ingestion and re-sinked via Projection — the H-050 adjunction extended from
schema to data across two substrates. For the operator's blank-UAT preview,
`PreservedFromSource` sidesteps the only deep problem (surrogate remap)
entirely, leaving a bounded build: a Transfer plan, an Ingestion adapter, a
two-endpoint connection seam outside `Config` (D9), a Transfer orchestrator in
`Projection.Pipeline`, a `transfer` CLI verb, and — for *later-run* or
`AssignedBySink` use — the persisted `SchemaContract`. The epic's North Star is
the **data-level canary** (Slice C): the data analog of the schema canary,
proving the adjunction round-trips data before any UAT write. The chief
external unknown is whether the OutSystems Cloud UAT database permits direct SQL
writes (OPEN-2), and the chief internal commitment is an R6 governance amendment
scoping this as a UAT-only, dry-run-by-default, preview write path.
