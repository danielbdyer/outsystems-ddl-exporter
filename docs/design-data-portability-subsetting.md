# Data Portability: Use-Case-Scoped Referential Subsetting & Transfer

> Status: Design / elicitation. Branch `claude/data-portability-config-v8fziu`.
> This document captures the intent, the locked decisions, and — crucially — a
> grounded gap analysis showing how much of the required machinery **already
> ships** in this repository.

## 1. The idea, de-abstracted

Certain sections of the application should be **baselineable and resettable**:
there is a *golden* set of rows for a given use case that we want to lift out of
one environment and land in another with high fidelity — preserving not just the
rows themselves but everything they referentially imply, so the slice is a
self-consistent, standalone-valid database fragment.

Translated into the vocabulary of this codebase:

| Gesture | Concrete meaning |
| --- | --- |
| "golden config data in its suchness" | A curated set of **actual rows** (values), not just schema. |
| "...and its implicature of consequence" | Plus the **referential closure** those rows require (parents, enums, lookups). |
| "know all the touchpoints" | Enumerate every table/column/FK a use case touches — already derivable from the `RelationshipModel` / `ForeignKeyTargetIndex` graph. |
| "statistical modeling to profile environments" | `MultiTargetSqlDataProfiler` / `ProfileSnapshot` — row counts, FK reality, orphans, uniqueness. |
| "sensible distillation / transfer a *subset*" | **Scoped subsetting**: reduce whole tables to a use-case-relevant row set. (New.) |
| "one-node-extra-blast-radius" | Bounded closure: pull what is needed for validity (and one controlled hop), not the whole transitive graph. |
| "user remapping and other congruencies" | Generalized **identity remapping** across all identity entities (today: User only). |
| "axiom-satisfying holonic subset" | The slice is itself a valid DB: every FK resolves, every NOT NULL / UNIQUE holds *within the slice*. |
| "well-quasi-ordered" | A load order with no infinite descending chains and a defined story for FK cycles — **already implemented**. |

## 2. Locked decisions (from elicitation)

1. **Closure depth** — *Configurable per relationship*. Each FK edge declares its
   traversal: follow-up (parent), follow-down (children), or stop; with optional
   predicates and depth caps. This config is the namesake of this branch.
2. **Artifact** — *Both*. The slice **definition** (config) is versioned/committed;
   running it yields either a committed golden dataset **or** an on-demand transfer.
   A transfer can be promoted to a golden baseline.
3. **Identity on import** — *Remap surrogate keys, collision-safe*, **under an
   on-prem DML-only constraint**: no `IDENTITY_INSERT` rights; identity columns
   auto-number on insert. Therefore the loader must insert without the PK,
   **capture the server-assigned key**, and **hoist it into topologically-ordered
   referents** before their FKs are inserted.
4. **Natural key (reuse-vs-insert)** — *Declared natural key per entity*, with
   *profile-inferred + operator-confirmed* enablement (reuse `UniqueCandidateProfile`
   to propose match columns).
5. **FK cycles at load** — *Two-phase insert-then-update* — **already implemented**
   (`PhasedDynamicEntityInsertGenerator`).
6. **Load vehicle** — *Both*: a self-contained T-SQL artifact (temp mapping tables,
   DML-only) **and** a harness driver/verifier — but build on the existing
   implementation rather than greenfield.

## 3. Gap analysis — what already ships

The headline finding: **the hard ordering primitives and an embryonic closure
walk already exist and are tested.** The feature is an assembly + two swaps, not a
new system.

### 3.1 Already implemented (reuse as-is)

- **Topological load order** — Kahn's algorithm.
  `src/Osm.Emission/Seeds/EntityDependencySorter.cs:482` (`TopologicalSort`).
- **Cycle handling (your chosen strategy)** — Tarjan SCC
  (`EntityDependencySorter.cs:1374`), Weak/Cascade/Other edge-strength
  classification, minimum **feedback-arc-set** search, and **two-phase
  insert-then-update** for cyclic nullable FKs
  (`src/Osm.Emission/PhasedDynamicEntityInsertGenerator.cs:88`).
- **Manual cycle ordering** — z-index `CircularDependencyOptions` / `AllowedCycle`
  / `TableOrdering.Position`.
- **Live, batched row extraction** — `SqlDynamicEntityDataProvider.ExtractTableAsync`
  (`src/Osm.Pipeline/DynamicData/SqlDynamicEntityDataProvider.cs:575`):
  `SELECT … FROM … ORDER BY pk OFFSET/FETCH`, checksums, telemetry.
- **Embryonic referential-closure walk** — same provider runs a BFS over
  relationships (`extractionQueue` / `enqueued` / `processed`, line 99+);
  `TrackParentRequirements` (line 279) resolves each relationship's **parent** and
  auto-enqueues it, with a parent-requirement tracker that distinguishes
  AutoLoad vs RequiresVerification (`StaticSeedParentHandlingMode`).
- **Generalized-remap template** — `#UserRemap` temp-table idiom + in-memory
  transformation map for User FKs (`src/Osm.Pipeline/UatUsers/SqlScriptEmitter.cs`,
  `ModelUserSchemaGraphFactory.cs`).
- **Natural-key matching (partial)** — static seeds MERGE on key columns
  (`StaticSeedSqlBuilder`); global bootstrap emits all entities in global topo order.
- **Cross-environment profiling** — `MultiTargetSqlDataProfiler`,
  `UniqueCandidateProfile` (the source of profile-inferred natural keys).

### 3.2 The load-bearing conflict

The existing **dynamic** loader preserves source PKs via `SET IDENTITY_INSERT`
— `PhasedDynamicEntityInsertGenerator.cs:216`:

```csharp
var hasIdentity = columns.Any(column => column.IsIdentity);
if (hasIdentity)
{
    sb.AppendLine($"SET IDENTITY_INSERT [{schema}].[{tableName}] ON;");
    ...
```

This is exactly the permission the on-prem DML target lacks. The static-seed and
`#UserRemap` paths already avoid `IDENTITY_INSERT` (M1.0 even asserts its
absence), but the dynamic path — the one that would carry a use-case slice —
hard-depends on it. **The dynamic identity strategy must change from
*preserve-via-IDENTITY_INSERT* to *capture-and-remap*.**

### 3.3 What is genuinely new

1. **Scoped selection / closure config (the keystone).** Today extraction is
   whole-table — the SELECT at `SqlDynamicEntityDataProvider.cs:593` has **no
   WHERE clause**, and the closure walk only follows **up** edges to **static**
   parents (line 302 skips non-static parents) and pulls the **entire** parent
   table. The feature needs:
   - **Root predicates** — `WHERE` on root entities (the "use case").
   - **Closure by referenced keys** — pull only parent rows actually referenced
     (`WHERE ParentPK IN (selected child FK values)`), recursively — not whole tables.
   - **Dynamic parents** — follow up edges to dynamic parents too, not just static.
   - **Down edges** — optionally follow children (`follow-down`), bounded.
   - **Per-edge directives** — follow-up / follow-down / stop, predicates, depth caps.
2. **Capture-and-remap identity strategy** — generalize `#UserRemap` from
   "User only, precomputed inventory map" to "every identity entity, map populated
   at insert time via `OUTPUT inserted.<pk>` / `SCOPE_IDENTITY()` into per-entity
   `#Map_*` temp tables; children resolve FKs by join."
3. **Natural-key declaration per entity** — the connective tissue: since the
   surrogate is gone at load time, both the `#Map_*` join handle and the
   reuse-vs-insert decision key on a declared (+ profile-inferred) natural key.

## 4. Proposed closure config schema (sketch)

A versioned `data-portability.json` (sibling to `default-tightening.json`):

```jsonc
{
  "slices": [
    {
      "name": "order-fulfillment-golden",
      "roots": [
        { "entity": "Order", "predicate": "Status = 'Template' AND IsArchived = 0" }
      ],
      "edges": {
        // default for any edge not named below
        "$default": "stop",
        "Order.CustomerId":      { "direction": "up" },                 // pull referenced parent rows
        "Order.StatusId":        { "direction": "up" },                 // enum/lookup closure
        "OrderLine.OrderId":     { "direction": "down", "maxDepth": 1 } // one controlled hop of children
      },
      "naturalKeys": {
        "Customer": ["ExternalRef"],
        "OrderStatus": ["Code"]
        // entities without a declared key fall back to profile-inferred + confirm
      }
    }
  ]
}
```

Semantics: BFS from root rows; for each visited row, for each FK edge, apply the
edge directive. `up` adds the referenced parent rows; `down` adds child rows
matching the parent keys (bounded by `maxDepth`); `stop` is the frontier (the
"one-node-extra-blast-radius" default). Closure terminates when no new rows are
discovered. Completeness invariant: no FK in the resulting set may dangle — every
referenced parent is present, or the column is nullable and nulled.

## 5. Capture-and-remap load idiom (replacing IDENTITY_INSERT)

Per identity entity, emit a mapping temp table; insert without the PK; capture the
server-assigned key via `OUTPUT`; resolve child FKs by joining their staged source
keys against the parent maps. Pure DML, no `IDENTITY_INSERT`. Shape:

```sql
-- Parent: Customer (natural key: ExternalRef)
CREATE TABLE #Map_Customer (SourceId INT, TargetId INT, ExternalRef NVARCHAR(50));

-- Reuse existing target rows by natural key (collision-safe merge into populated target)
INSERT INTO #Map_Customer (SourceId, TargetId, ExternalRef)
SELECT s.SourceId, t.[Id], s.ExternalRef
FROM #Staging_Customer s
JOIN [dbo].[Customer] t ON t.[ExternalRef] = s.ExternalRef;

-- Insert the genuinely-new rows, capturing the auto-numbered key
INSERT INTO [dbo].[Customer] ([ExternalRef], [Name] /* non-identity cols */)
OUTPUT inserted.[Id], s.SourceId INTO #Map_Customer (TargetId, SourceId)   -- see note
SELECT s.[ExternalRef], s.[Name]
FROM #Staging_Customer s
WHERE NOT EXISTS (SELECT 1 FROM #Map_Customer m WHERE m.SourceId = s.SourceId);

-- Child: Order — resolve CustomerId FK via the parent map, in topological order
INSERT INTO [dbo].[Order] ([CustomerId], [Status] /* ... */)
OUTPUT inserted.[Id], s.SourceId INTO #Map_Order (TargetId, SourceId)
SELECT m.TargetId, s.[Status]
FROM #Staging_Order s
JOIN #Map_Customer m ON m.SourceId = s.[CustomerId];
```

> Note: `OUTPUT … INTO` cannot reference non-inserted source columns directly; the
> production emitter will stage source keys alongside (e.g. via a `MERGE … OUTPUT`
> that exposes both `inserted` and source, or a deterministic per-batch correlation).
> This is the one mechanical detail to nail down in implementation; the existing
> `SqlScriptEmitter` (`#UserRemap`) and `PhasedDynamicEntityInsertGenerator`
> (two-phase cycles) are the reuse surfaces.

FK cycles reuse the **existing** two-phase path: insert the cyclic (nullable) FK as
NULL, capture keys, then `UPDATE` the FK once the partner's key exists.

## 6. Suggested milestones

- **D1 — Closure config + selector.** Add `data-portability.json` parsing; extend
  `SqlDynamicEntityDataProvider` with root predicates and per-edge directives
  (key-scoped parent pull, dynamic parents, bounded children). Reuse the existing
  BFS/queue. Output: a scoped `DynamicEntityDataset` + closure-completeness check.
- **D2 — Capture-and-remap emitter.** New emission path that drops
  `IDENTITY_INSERT` and emits `#Map_*` capture + FK-resolve-by-join, generalizing
  `#UserRemap`. Reuse `EntityDependencySorter` ordering and the two-phase cycle path.
- **D3 — Natural keys.** Declared keys in config + profile-inferred proposals from
  `UniqueCandidateProfile`; wire into reuse-vs-insert.
- **D4 — Golden/transfer duality + verification.** Promote a transfer to a
  committed golden dataset; post-load verification via the profiler (expected
  closure counts, zero new orphans).

## 7. Open questions

- **Row source confirmed**: live `SELECT … WHERE` against the source DB (the
  provider already does live batched reads; predicates are the only addition).
- **`OUTPUT … INTO` source-key correlation** — exact mechanism (staged correlation
  vs `MERGE … OUTPUT`) — see §5 note.
- **Composite / non-integer identities** — natural keys may be composite; map
  tables must generalize beyond a single `INT` surrogate.
- **Determinism for golden baselines** — stable ordering + stable selection given
  the same source, so committed datasets diff cleanly.
