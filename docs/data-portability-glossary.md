# Data Portability — Glossary

This glossary anchors the (deliberately abstract) vocabulary used to describe the
data-portability feature to concrete engineering terms and code locations in this
repository. It exists so reviewers can translate the "what" into the "where."

Legend: ✅ already implemented · 🟡 partial / embryonic · ❌ new work.

---

**Axiom-satisfying subset** — A slice that is itself a *valid* database fragment:
every foreign key resolves within the slice, every `NOT NULL` holds, every declared
`UNIQUE` / natural key holds. The enforceable form of this is the **closure
completeness invariant** (no dangling FK). ❌ New (the invariant check); the FK
graph it checks against is ✅ (`RelationshipModel`, `ForeignKeyTargetIndex`).

**Baseline / resettable** — A committed, versioned golden dataset an environment
can be reset to. Analogous to today's static seeds, but for use-case-scoped
*dynamic* rows. 🟡 Static seeds exist (`StaticSeedSqlBuilder`); scoped dynamic
golden datasets are ❌.

**Blast radius (one-node-extra)** — The bounded frontier of the closure: pull what
referential integrity requires (and at most one controlled extra hop), not the
whole transitive graph. Encoded as the per-edge `stop` directive being the default.
❌ New (the directive system).

**Capture-and-remap** — The load strategy required by the DML-only target: insert a
row *without* its primary key, let the server auto-number it, **capture** the
assigned key (`OUTPUT inserted.<pk>` / `SCOPE_IDENTITY()`), and **remap** dependent
child FKs to the captured key before inserting them. Replaces
`SET IDENTITY_INSERT`. ❌ New; templated on the ✅ `#UserRemap` /
`OUTPUT … INTO #Changes` idiom in `SqlScriptEmitter.cs`.

**Closure (referential closure)** — The transitive set of rows reachable from the
roots by following configured FK edges, terminating when no new rows are
discovered. *Upward* closure (parents/enums/lookups) is what makes a slice valid;
*downward* closure (children) is optional context. 🟡 An embryonic upward walk to
**static** parents exists in `SqlDynamicEntityDataProvider.TrackParentRequirements`;
key-scoped, dynamic-parent, and downward closure are ❌.

**Closure engine / selector** — The component that takes roots + per-edge config and
produces the closed row set. ❌ New; built atop the ✅ BFS queue
(`extractionQueue` / `enqueued` / `processed`) already in
`SqlDynamicEntityDataProvider`.

**Condensation / SCC** — The strongly-connected-component contraction of the FK
graph; cycles become single nodes for ordering. ✅ Implemented via Tarjan
(`EntityDependencySorter.cs:1374`).

**Data-portability config** — The versioned `data-portability.json` defining slices:
roots (entity + predicate), per-edge directives, and natural keys. The namesake of
branch `claude/data-portability-config-v8fziu`. ❌ New; mirrors the ✅
`CircularDependencyOptions` / `TighteningOptions` config-deserialization pattern.

**Edge directive** — Per-relationship traversal rule: `up` (follow to parent),
`down` (follow to children, bounded by `maxDepth`), or `stop` (frontier). May carry
a predicate. ❌ New.

**Feedback arc set** — The minimal set of (weak) FK edges whose removal makes a
cyclic component acyclic, so a load order exists. ✅ Implemented
(`EntityDependencySorter.FindMinimumFeedbackArcSet`).

**Golden dataset** — A materialized, committed slice used as a baseline. See
*Baseline*. The "both" decision: a transfer can be *promoted* to a golden dataset.
❌ New.

**Haecceity / suchness** — The actual row *values* (as opposed to schema). Captured
today by live extraction (`SqlDynamicEntityDataProvider.ExtractTableAsync`,
`StaticEntityRow`). ✅ for capture; scoping it is ❌.

**Holon / holonic** — A part that is simultaneously a self-contained whole. Here: a
slice that is both a fragment *of* the catalog and a valid database *in itself*.
Conceptual framing for the closure-completeness invariant.

**Identity remapping** — Translating source surrogate keys to target keys so the
slice merges into a populated target without PK collisions. 🟡 Exists for **User**
only, via a *precomputed* inventory map (`UatUsers`); generalizing to all entities
with *at-insert-time* capture is ❌.

**Implicature of consequence** — Everything a chosen row *requires* to be valid: its
referential closure. See *Closure*.

**Map table (`#Map_*`)** — A per-entity temp table holding `SourceId → TargetId`
(+ natural-key columns) populated during load; children join it to resolve FKs.
❌ New; generalizes the ✅ `#UserRemap` / `#Changes` temp tables.

**Natural / business key** — The columns that identify "the same entity" across
environments independent of the surrogate PK (e.g. `Email`, `Code`, `ExternalRef`).
Drives reuse-vs-insert and is the `#Map_*` join handle. 🟡 Static seeds match on PK;
declared per-entity natural keys for dynamic entities are ❌. Profile-inferred
candidates come from ✅ `UniqueCandidateProfile`.

**Phased (two-phase) load** — Insert cyclic/nullable FK rows with the FK `NULL`,
then `UPDATE` once the partner key exists. ✅ Implemented
(`PhasedDynamicEntityInsertGenerator`); the chosen cycle strategy.

**Profiling / statistical modeling** — Measuring environments (row counts, null
density, uniqueness, FK reality/orphans), used for sizing, natural-key inference,
and post-load verification. ✅ `MultiTargetSqlDataProfiler`, `ProfileSnapshot`.

**Roots** — The seed rows that define a use case: an entity plus a `WHERE`
predicate. ❌ New (predicates); entity seeding is 🟡 (`SeedInitialEntities`,
currently whole-table by module/entity filter).

**Slice** — A single named, use-case-scoped, referentially-closed set of rows,
defined by one `slices[]` entry in the config. ❌ New.

**Topological order / well-quasi-ordered** — A child-after-parent load order with no
infinite descending chains and a defined story for cycles. ✅ Kahn's algorithm
(`EntityDependencySorter.TopologicalSort`) + SCC handling.

**Touchpoints** — Every table/column/FK a use case references; derivable from the
relationship graph. ✅ (`RelationshipModel`, `ForeignKeyTargetIndex`).

**Transfer (operational)** — An on-demand, transient lift-and-shift of a slice
between live environments (vs a committed golden dataset). ❌ New.
