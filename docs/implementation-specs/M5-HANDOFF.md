# 🚀 Milestone 5 Implementation Handoff — Data Portability

> A self-contained briefing for the agent(s) implementing the Milestone 5
> data-portability specs. You're inheriting a feature that's **mostly already
> built** — your job is assembly plus two precise swaps, not a greenfield system.
> This document straps you to the parts of the codebase that matter so you can
> start cutting code on the first read.
>
> **Specs (source of truth):**
> [design overview](../design-data-portability-subsetting.md) ·
> [glossary](../data-portability-glossary.md) ·
> [M5.0](./M5.0-data-portability-closure-selector.md) ·
> [M5.1](./M5.1-capture-and-remap-loader.md) ·
> [M5.2](./M5.2-natural-key-resolution.md) ·
> [M5.3](./M5.3-golden-transfer-verification.md) ·
> [data-slice verb](../verbs/data-slice.md)

---

## §0 — Orientation
- **Repo:** `/home/user/outsystems-ddl-exporter` (C# 13 / .NET 9, pinned via `global.json`). An F# sidecar exists but is **not** your target — work in `src/Osm.*`.
- **Branch:** `claude/data-portability-config-v8fziu` — commit and push here, never elsewhere.
- **PR:** #627 (the specs live on this branch).
- **Build/verify:** `dotnet build` and `dotnet test` from repo root. Tests are **fixture-first** — unit tests must NOT need a live DB. Run them after every change and keep the branch green.

## §1 — The one mental model to hold
Almost every hard primitive ships and is tested. The feature reduces to:
1. **A scoped closure selector** in front of existing extraction (today extraction is whole-table, no `WHERE`).
2. **An identity-strategy swap** on the load side: the existing dynamic loader preserves PKs via `SET IDENTITY_INSERT`, which the **DML-only on-prem target forbids**. Replace it with capture-the-auto-numbered-key-and-remap.
3. **Natural keys** as the connective tissue (the surrogate is gone at load time, so reuse-vs-insert and the FK remap join both key on a business key).

**The load-bearing conflict — burn this in:** `src/Osm.Emission/PhasedDynamicEntityInsertGenerator.cs:216-223` (and `:245-250`) emits `SET IDENTITY_INSERT ON/OFF`. The non-phased generator does the same at `DynamicEntityInsertGenerator.cs:115-121`. The portability path must emit **none**. Identity columns auto-number; you capture the assigned key via `OUTPUT`.

## §2 — Locked decisions (do NOT re-litigate)
- Closure is **configurable per relationship** (`up`/`down`/`stop`); `stop` is the default frontier ("one-node-extra-blast-radius").
- Artifact is **both**: versioned slice definition → either a transient transfer or a committed golden dataset; transfer can be promoted to golden.
- Identity: **remap surrogate keys, collision-safe**, under DML-only / no-`IDENTITY_INSERT`; capture server-assigned key, hoist into topologically-ordered referents.
- Natural keys: **declared** (config) + **profile-inferred, operator-confirmed**.
- Cycles: **two-phase insert-then-update** (already implemented — reuse it).
- Load vehicle: **both** — self-contained T-SQL artifact is primary; LoadHarness drives/verifies.

## §3 — Build order (dependency-correct)
**M5.0 → M5.2 → M5.1 → M5.3.** The loader (M5.1) consumes both the selected dataset (M5.0) and the natural-key map (M5.2), so build those first.

## §4 — The reuse map (don't rediscover these)

**Closure / extraction (M5.0):**
- `src/Osm.Pipeline/DynamicData/SqlDynamicEntityDataProvider.cs` — the BFS already exists: queue/visited at `:99+`, `SeedInitialEntities` `:245`, `TrackParentRequirements` `:279`, parent resolution `EntityLookup.ResolveRelationship` `:426`, batched live read `ExtractTableAsync` `:575`. **Two changes:** the SELECT at `:593` has no `WHERE` (add predicates); the closure only follows **static** parents (`:302` `IsStatic` gate) pulling **whole tables** (make it key-scoped, follow dynamic parents, add bounded `down`).

**Ordering / cycles (M5.1):**
- `src/Osm.Emission/Seeds/EntityDependencySorter.cs` — `TopologicalSort` (Kahn) `:482`, `TarjanScc` `:1374`, 2-cycle auto-resolution `:904`, `FindMinimumFeedbackArcSet` `:1173`. Use `SortByForeignKeys` as-is.
- `src/Osm.Emission/PhasedDynamicEntityInsertGenerator.cs` — copy the Phase-1/Phase-2 shape (`:88-148`), `GenerateUpdateForNullableFKs` (`:255`), `BuildCycleTableSet` over SCCs (`:325`), VALUES/CTE formatting `AppendValuesClause` (`:368`). Your generator differs only in: no `IDENTITY_INSERT`, identity col excluded from insert list, FK resolved via map-join, Phase-2 updates resolve through partner maps.

**The capture/remap idiom (M5.1):**
- `src/Osm.Pipeline/UatUsers/SqlScriptEmitter.cs:104-128` is the exact `OUTPUT … INTO #temp` template; `:215-241` shows `#UserRemap`/`#Changes` temp-table creation. You're generalizing this from User-only/precomputed to **all entities / at-insert-time**.
- Reuse step (match existing target rows): `StaticSeedSqlBuilder.cs:229` (MERGE `ON` key predicate); match-key determination `:147`.

**Natural keys (M5.2):**
- `src/Osm.Domain/Profiling/UniqueCandidateProfile.cs` (`HasDuplicate`, `ProbeStatus`) and `CompositeUniqueCandidateProfile.cs` — propose a key iff `HasDuplicate == false` and the probe is conclusive. Container: `ProfileSnapshot.UniqueCandidates` / `.CompositeUniqueCandidates`.

**Verification (M5.3):**
- `src/Osm.Pipeline/Profiling/MultiTargetSqlDataProfiler.cs` + `ForeignKeyReality` (`HasOrphan`/`OrphanCount`) for the before/after new-orphan delta. `src/Osm.LoadHarness/` for the apply+verify driver.

**Types you'll pass around (already exist):**
- `StaticEntitySeedColumn` (`Osm.Emission/Seeds/StaticEntitySeedScriptGenerator.cs:218`) — has `IsPrimaryKey`, `IsIdentity`, `IsNullable`, `EffectiveColumnName`. Note `IsIdentity = attribute.OnDisk.IsIdentity ?? attribute.IsAutoNumber`.
- `StaticEntityTableData` (`:266`) and `StaticEntityRow` (`:247`) — **despite the "Static" name these are the universal dynamic-data carriers.** `DynamicEntityDataset` wraps them.

## §5 — House idioms (match exactly — this is the quality bar)
- **Immutable `record` + static `Result<T> Create(...)` factory** for every new domain/config type. Result/error machinery: `src/Osm.Domain/Abstractions/Result.cs` — `Bind`/`Map`/`Ensure`/`Collect` + implicit conversions. Every failure is a `ValidationError.Create("config.dataPortability.<...>", "<actionable message>")` with a **unique, namespaced code**.
- **Config deserializer pattern:** mirror `src/Osm.Json/Deserialization/CircularDependencyConfigDeserializer.cs` (`JsonDocument.Parse` → `DeserializeFromDocument(JsonElement)` → per-item `Result` accumulation, early-return on failure) and the `JsonSerializerOptions` style in `TighteningOptionsDeserializer.cs` (case-insensitive, skip comments, allow trailing commas).
- **No regex / string-concat for SQL identifiers.** Use the existing bracket-quoting helpers (`FormatTwoPartName`, `FormatColumnName`, doubling `]`). Data values go through `SqlLiteralFormatter`; key-membership filters use **parameters** (`@k0…`), batched under the 2100-parameter limit — never interpolate data values.
- **CLI wiring:** add a `DataSliceCommandFactory` (model on `UatUsersCommandFactory.cs` for a standalone verb, or `PipelineCommandFactory<TOpts,TResult>` for pipeline plumbing), register in `src/Osm.Cli/Program.cs` via `AddSingleton<ICommandFactory, …>`. Verb name `data-slice`; flags per the verb doc.

## §6 — First concrete task (M5.0) to build momentum
1. `src/Osm.Domain/Configuration/DataPortabilityOptions.cs` — the records in M5.0 §Data Models (`DataPortabilityOptions`, `DataSliceOptions`, `SliceRoot`, `EdgeDirective`, `EdgeDirection`), Result-factory validated.
2. `src/Osm.Json/Configuration/DataPortabilityOptionsDeserializer.cs` — mirror the circular-deps deserializer. Unit test: fixture JSON → options, plus one test per `ValidationError` code.
3. Plumb an optional predicate into `SqlDynamicEntityDataProvider.ExtractTableAsync` (the `WHERE` slot); expose a seeding hook so a new `DataSliceSelector` owns the queue.
4. `src/Osm.Pipeline/DataPortability/DataSliceSelector.cs` — key-scoped up-closure (drop the `:302` static gate), bounded down-closure, `stop` frontier, dedupe by (entity, key).
5. `ClosureReport` + completeness invariant (no dangling mandatory FK; actionable failure naming entity/edge/sample keys).

Mirror existing tests: `tests/Osm.Pipeline.Integration.Tests/SqlDynamicEntityDataProviderIntegrationTests.cs` (opt-in live), `tests/Osm.Emission.Tests/PhasedDynamicEntityInsertGeneratorTests.cs` and `EntityDependencySorterTests.cs` (fixture-first unit).

## §7 — Sharp edges (know them before you hit them)
- **`OUTPUT … INTO` can't read source columns.** Default (M5.1 §Data Models): `OUTPUT` the new identity + the **natural key** into `#MapStage_<Entity>`, then join back to `#Stage_<Entity>` on the natural key to recover `SourceId`. This is *why* M5.2 is a hard dependency. Fallback: `MERGE … OUTPUT` (which can expose source columns) for entities whose natural key isn't unique within the staged set. **Get this right first.**
- **Populated target + no natural key = error, not silent insert.** Don't risk duplicates.
- **Determinism for golden:** topo entity order, rows by natural key then source PK, values normalized exactly as `StaticEntityRow`/`SqlLiteralFormatter` already do — so re-materializing the same source diffs byte-identically.
- **Predicates are trusted config** (same trust as `default-tightening.json`), read-only, slice-local. Don't build dynamic-SQL guards beyond the templated `WHERE`.
- **Composite / non-integer identities** are deferred in MVP — design the map tables not to *preclude* them.

## §8 — Definition of done
Each spec's **Success Criteria** checklist is the contract. Global bars: portability output contains **zero** `SET IDENTITY_INSERT`; closure reports zero dangling mandatory FKs; re-applying a load artifact is idempotent on row counts; `dotnet test` green; new types follow the Result/record/ValidationError idiom; commits scoped and pushed to the branch.

---

# 🧭 Strategy: Sequencing, Parallelization & De-risking

## §9 — Dependency graph & critical path

```
                 ┌──────────────────────────────┐
                 │ A. Config records (Domain)    │  ← unblocks everything, do first
                 │   DataPortabilityOptions etc. │
                 └───────┬───────────────┬───────┘
                         │               │
              ┌──────────▼───┐     ┌─────▼──────────────┐
              │ B. Deserializer│   │ C. Predicate plumb │   (B and C parallel)
              │   (Osm.Json)   │   │  ExtractTableAsync │
              └──────────┬─────┘   └─────┬──────────────┘
                         │               │
                         │         ┌─────▼───────────────┐    ┌──────────────────────┐
                         │         │ D. DataSliceSelector │    │ N. NaturalKeyResolver│ (D and N parallel)
                         │         │  + ClosureReport     │    │  (needs A + profiling)│
                         │         └─────┬────────────────┘    └─────┬────────────────┘
                         │               │                            │
                         │               └────────────┬───────────────┘
                         │                             │  (merge point — both feed the loader)
                         │                   ┌─────────▼──────────────┐
                         │                   │ E. CaptureRemapLoadGen │  ← novel SQL; longest pole
                         │                   │  (needs D dataset + N map)│
                         │                   └─────────┬──────────────┘
            ┌────────────▼────────────┐                │
            │ V. data-slice verb/CLI  │ (parallel,     │
            │  wiring + stubs         │  stub early)   │
            └─────────────────────────┘     ┌──────────▼───────────┐
                                            │ F. Materializer +     │
                                            │  verification (M5.3)  │
                                            └───────────────────────┘
```

**Critical path:** `A → C → D → E`. Everything else (B, N, V, the golden-writer half of F) runs concurrently. **E is the longest pole** — the only genuinely-novel SQL — so the highest-leverage move is to *shorten E's variance before you reach it* (§12 spike).

## §10 — Parallelization plan (if you fan out to multiple agents)

Partition by **project/namespace boundary** to minimize merge conflict, **freeze the integration contracts first**, and use **isolated git worktrees** per agent (integrate via small test-green commits).

| Agent | Owns (writes) | Depends on (reads) | Conflict risk |
|---|---|---|---|
| **A — Config** | `Osm.Domain/Configuration/DataPortability*`, `Osm.Json/Configuration/DataPortabilityOptionsDeserializer` | profiling types | none (all new files) |
| **B — Selector** | `Osm.Pipeline/DataPortability/DataSliceSelector`, `ClosureReport`; **sole editor of** `SqlDynamicEntityDataProvider.cs` | A's records | ⚠ no one else touches the provider |
| **C — Loader** | `Osm.Emission/CaptureRemapLoadGenerator`, `CaptureRemapLoadScript` | D dataset shape, N map | none (new files); reads phased generator, doesn't edit it |
| **D — Natural keys** | `Osm.Pipeline/DataPortability/NaturalKey*` | A's records, profiling | none (new files) |
| **E — CLI + M5.3** | `Osm.Cli/Commands/DataSliceCommandFactory`, `Program.cs` registration, `DataSliceMaterializer`, verification | all | ⚠ sole editor of `Program.cs` |

**Contract freeze — publish these signatures before anyone starts, then don't change them:**

```csharp
// Selector (Agent B)
Result<DataSliceSelection> DataSliceSelector.Select(
    DataSliceOptions slice, OsmModel model, /* row source */, CancellationToken ct);
record DataSliceSelection(DynamicEntityDataset Dataset, ClosureReport Report);

// Natural keys (Agent D)
Result<NaturalKeyMap> NaturalKeyResolver.Resolve(
    DataSliceOptions slice, OsmModel model, ProfileSnapshot? profile);

// Loader (Agent C) — consumes both
CaptureRemapLoadScript CaptureRemapLoadGenerator.Generate(
    DynamicEntityDataset dataset, OsmModel model,
    NaturalKeyMap naturalKeys, CircularDependencyOptions cycles);
```

Those three boundaries (`DataPortabilityOptions`, `NaturalKeyMap`, `DynamicEntityDataset`+`ClosureReport`) are the API seams. As long as they hold, A/B/C/D/E proceed in parallel and converge cleanly.

## §11 — Walking skeleton first (depth before breadth)

Before *any* breadth, drive **one** thread through all four milestones:

> One slice · one root with a predicate · **one** `up` edge to a parent · acyclic · single `INT` identity · one **declared** natural key · load into a populated target · verify.

~10% of the work, exercises **100% of the integration seams**. Surfaces the OUTPUT-correlation reality, the map-join wiring, and the verification diff on day two, not week three. Add breadth (down-closure, cycles, composite keys, golden determinism, proposals) only after the skeleton loads-and-verifies.

## §12 — De-risking spikes (day zero, before designing E)

Kill the top-variance unknowns immediately, in throwaway `.sql` against a real SQL Server (LocalDB/container), **not** in C#:

1. **🔴 `OUTPUT … INTO` source-key correlation.** Hand-write the `#Stage_/#Map_/#MapStage_` dance for a 2-table parent→child; confirm parent inserts capture keys, child FK resolves via join, natural-key correlation recovers `SourceId`, zero `IDENTITY_INSERT`. Decide here: natural-key correlation vs `MERGE … OUTPUT` fallback. **This decision shapes E's entire design — make it with a real query plan in front of you.**
2. **🟠 IN-list batching under the 2100-parameter limit.** Prove the up-closure key-membership filter batches for a parent referenced by thousands of children.

Defer: cycle+capture Phase-2 interaction (mechanically derivable from existing two-phase code), composite keys (out of MVP).

## §13 — Fixtures & test sequencing (build the test bed first)

Author fixtures before features:
- A minimal `OsmModel` fixture: root → parent(`up`) → lookup(`stop`) → child(`down`), plus a 2-node cycle. Extend `tests/Fixtures/model.edge-case.json`.
- An **in-memory `IDynamicEntityDataProvider` fake** so the selector is unit-testable with no DB (the live integration test stays opt-in).
- A `FixtureDataProfiler` snapshot feeding natural-key proposals.

Mirror existing test files: `PhasedDynamicEntityInsertGeneratorTests.cs`, `EntityDependencySorterTests.cs`, `SqlDynamicEntityDataProviderIntegrationTests.cs`.

## §14 — Tracer-bullet checkpoints (demoable, in order)

1. `data-portability.json` parses → `DataPortabilityOptions` (Result-validated).
2. Selector returns a closed `DynamicEntityDataset` + clean `ClosureReport` on fixtures (no dangling FK).
3. Generated SQL for the 2-table fixture: **no** `IDENTITY_INSERT`, correct `#Map_*` capture + FK join (assert on string shape).
4. Live load into a *populated* target: zero PK collisions, all FKs resolve, reused parents not duplicated.
5. Golden re-materializes **byte-identically** from the same source; post-load verification passes.

Each checkpoint = a commit + a status update on PR #627.

## §15 — Effort sizing (relative)

- **A (config+deserializer):** S — pattern-copy.
- **B (selector+closure):** L — the brain; closure fixed-point, dedupe, dynamic parents, completeness invariant.
- **C (loader):** XL — novel SQL; longest pole; gated by the §12 spike.
- **D (natural keys):** M — declared is S; profile-inferred proposals add M.
- **E (CLI+M5.3):** M — verb wiring S, materializer/verification M.

Front-load A and the §12 spike; they unblock the two biggest items (B, C).

## §16 — Anti-goals (scope discipline)

- ❌ Don't touch the **bootstrap / static-seed / UAT-users** paths. Their `IDENTITY_INSERT` is correct *for them* (greenfield first-deploy). The swap is portability-path-only.
- ❌ Don't refactor `EntityDependencySorter` / Tarjan / feedback-arc-set. Consume, don't improve.
- ❌ No composite or non-integer identities in v1 (don't *preclude* them, don't build them).
- ❌ Don't grow the predicate config into a query language. It's a trusted `WHERE` fragment.
- ❌ Don't add a parallel F# sidecar implementation.

## §17 — Observability (match the house tiers)

Thread `PipelineExecutionLogBuilder` through the selector: log each edge-traversal decision (entity, via-attribute, directive, keys-pulled count) so an over-/under-broad closure is diagnosable. Mirror the M1.0 Tier-1 idiom (`GO`/`PRINT`/comments) in the emitted artifact: per-entity banners, inserted-vs-reused counts. `ClosureReport` and `SliceVerificationReport` are the Tier-2/3 artifacts.

## §18 — Integration cadence & git hygiene

- Keep the **branch building at all times** — `dotnet test` green before every push; never land a red commit that blocks parallel agents.
- Stack small, milestone-scoped commits on `claude/data-portability-config-v8fziu`; push at each §14 checkpoint.
- Parallel agents work in **isolated worktrees**, integrate via the frozen §10 contracts; the owner of each hot file (`SqlDynamicEntityDataProvider.cs`, `Program.cs`) is the *only* writer.
- Update PR #627's status checklist at each checkpoint so the thread shows live state.

---

**The strategic shape in one line:** freeze three contracts → spike the `OUTPUT` correlation → drive one vertical skeleton end-to-end → then fan out by namespace, keeping the critical path `A→C→D→E` hot and everything else parallel beside it.
