# Chapter 3 pre-scope — `SnapshotRowsets` variant of `SnapshotSource`

**Subagent #5 dispatched at session 25** (chapter-2 close runway).
The OSSYS catalog adapter shipped at session 18 with two
variants of `SnapshotSource` (`SnapshotFile` and `SnapshotJson`)
and two reserved variants (`SnapshotRowsets`, `LiveOssysConnection`)
documented in the type's docstring as deferred. `SnapshotRowsets`
is the operator-decided canonical resolution to the
JSON-projection-lossiness class (see `DECISIONS 2026-05-19 —
naming the two classes of resolution patterns explicitly`).

This document is the chapter-3 chapter-open input for the
SnapshotRowsets implementation chapter. The full pre-scope
report follows; the chapter-3 agent should treat this as the
chapter-open document's first draft and refine under empirical
pressure once the chapter opens.

---

## 1. V1 rowset shape

V1's SQL extraction (`/home/user/outsystems-ddl-exporter/src/AdvancedSql/outsystems_metadata_rowsets.sql`,
1184 lines) executes in two phases. **Phase 1** materializes 21
temp tables into `tempdb..#*` workspace. **Phase 2** pre-
aggregates JSON blobs. The script then emits **23 trailing
`SELECT` rowsets** for managed orchestration (lines 956–1184).
The V1 C# DTO surface in
`/home/user/outsystems-ddl-exporter/src/Osm.Pipeline/SqlExtraction/IOutsystemsMetadataReader.cs:71-207`
mirrors them as 23 sealed records on `OutsystemsMetadataSnapshot`.

The rowsets, in extraction order:

| # | Rowset | DTO record | Lossiness-class members it carries |
|---|---|---|---|
| 1 | `#E` (modules) | `OutsystemsModuleRow` | **`EspaceKind`** (string), **`EspaceSSKey`** (Guid) |
| 2 | `#Ent` (entities) | `OutsystemsEntityRow` | **`IsSystemEntity`** (bool), **`PrimaryKeySSKey`** (Guid), **`EntitySSKey`** (Guid) |
| 3 | `#Attr` (attributes) | `OutsystemsAttributeRow` | **`AttrSSKey`** (Guid), `OriginalName`, `LegacyType`, `ExternalColumnType`, `OriginalType` |
| 4 | `#RefResolved` | `OutsystemsReferenceRow` | (resolved cross-module FK names — bears on the deferred cross-module-FK slice) |
| 5 | `#PhysTbls` | `OutsystemsPhysicalTableRow` | (per-table column structure scaffolding) |
| 6 | `#ColumnReality` | `OutsystemsColumnRealityRow` | **per-attribute column structure**: `IsNullable`, `SqlType`, `MaxLength`, `Precision`, `Scale`, `CollationName`, `IsIdentity`, `IsComputed`, `ComputedDefinition`, `DefaultConstraintName`, `DefaultDefinition`, `PhysicalColumn` |
| 7 | `#ColumnCheckReality` | `OutsystemsColumnCheckRow` | **check-constraint definitions** (per attribute) |
| 8 | `#AttrCheckJson` | `OutsystemsColumnCheckJsonRow` | (already-aggregated check-constraint JSON) |
| 9 | `#PhysColsPresent` | `OutsystemsPhysicalColumnPresenceRow` | (which logical attrs exist physically) |
| 10 | `#AllIdx` | `OutsystemsIndexRow` | (full index storage + structural attributes) |
| 11 | `#IdxColsMapped` | `OutsystemsIndexColumnRow` | (per-index column ordinal + direction + included-flag) |
| 12 | `#FkReality` | `OutsystemsForeignKeyRow` | (physical FK reality) |
| 13 | `#FkColumns` | `OutsystemsForeignKeyColumnRow` | (per-FK column mapping) |
| 14 | `#FkAttrMap` | `OutsystemsForeignKeyAttrMapRow` | |
| 15 | `#AttrHasFK` | `OutsystemsAttributeHasFkRow` | |
| 16 | `#FkColumnsJson` | `OutsystemsForeignKeyColumnsJsonRow` | (already-aggregated) |
| 17 | `#FkAttrJson` | `OutsystemsForeignKeyAttributeJsonRow` | (already-aggregated) |
| 18 | `#Triggers` | `OutsystemsTriggerRow` | (V2 explicitly NOT carrying forward) |
| 19 | `#AttrJson` | `OutsystemsAttributeJsonRow` | (already-aggregated; same shape `SnapshotJson` produces) |
| 20 | `#RelJson` | `OutsystemsRelationshipJsonRow` | (already-aggregated) |
| 21 | `#IdxJson` | `OutsystemsIndexJsonRow` | (already-aggregated) |
| 22 | `#TriggerJson` | `OutsystemsTriggerJsonRow` | (already-aggregated) |
| 23 | `#ModuleJson` | `OutsystemsModuleJsonRow` | (final assembly — the JSON `SnapshotJsonBuilder` re-stitches) |

**The lossiness happens at exactly one layer.** `SnapshotJsonBuilder.cs`
at `/home/user/outsystems-ddl-exporter/src/Osm.Pipeline/SqlExtraction/SnapshotJsonBuilder.cs:114-126`
writes **only** `name / isSystem / isActive / entities` for
modules — `EspaceKind`, `EspaceSSKey`, `EspaceId` are dropped.
At `:194-209` for entities, it writes `name / physicalName /
isStatic / isExternal / isActive / db_catalog / db_schema / meta`
plus the four sub-arrays — **`IsSystemEntity`, `EntitySSKey`,
`PrimaryKeySSKey` are dropped**. The `#AttrJson` aggregation at
SQL line 745–809 likewise drops `AttrSSKey`. Every lossy field
travels intact through phase-1 rowsets 1–3; the loss is purely
the `FOR JSON PATH` projection (rowsets 19–23) and
`SnapshotJsonBuilder`'s field selection.

**Implication for SnapshotRowsets:** phase-1 rowsets 1–3 are
sufficient to resolve all three known lossiness members.
Rowsets 6, 7, 10, 11 supply richer per-column / per-index
structure that may surface as future class members. Rowsets
19–23 are the JSON-aggregation subtree V1 already collapses;
SnapshotRowsets does not need them (rowsets 1–18 form the
structural "raw" layer).

---

## 2. Multi-rowset deserialization architecture

**Recommendation: per-rowset hand-written F# DTO records, bundle
delivered via a single carrier-DU variant, with the
materialization step located inside the OSSYS adapter.**

The architectural choices in tension:

- **C# DataReader streaming materialization at the boundary.**
  V1 already does this (`MetadataSnapshotRunner.ExecuteAsync` +
  23 `IResultSetProcessor` implementations). The natural shape
  for re-use would be sharing the V1 reader and DTO surface —
  but the cherry-pick discipline (`HANDOFF.md` — boundary is
  data, not typed cross-references) and the V2-OSSYS-adapter
  docstring at `CatalogReader.fs:14-17` ("does not depend on any
  V1 C# types") forbid that. SnapshotRowsets must define its
  own DTO surface.
- **Adapter-language rule (`DECISIONS 2026-05-09`).** F# core /
  C# shell was superseded; the rule today is *the natural
  language of the boundary*. `DataTable` / `DataReader`
  interactions are C#'s natural language. **However**, the OSSYS
  adapter is already F# (`Projection.Adapters.Osm/CatalogReader.fs`);
  the JSON path is in F# via `System.Text.Json.JsonElement`.
  Keeping the rowset path in the same adapter project (and same
  language) honors the **closed-DU expansion empirical-test
  discipline** — adding `SnapshotRowsets` as a third variant
  should not reshape callers outside the variant's module.
- **Testability.** Unit tests need to provide rowset data
  without a real SQL Server. Hand-written F# records are the
  simplest fixture surface — tests construct `RowsetBundle`
  literals inline; no `DataTable` mocking. This mirrors the
  existing fixture pattern (`v1MinimalFixture` is an inline
  string constant in `OsmCatalogReaderDifferentialTests.fs`).
- **Memory.** OSSYS metadata for a real OutSystems environment
  is small (megabytes, not gigabytes). Streaming pays no real
  dividend; in-memory joins are practical.

**Recommended shape:**

```fsharp
// New types in Projection.Adapters.Osm (F# records mirroring rowsets 1-3 first;
// extend under empirical pressure as future lossiness members surface).
type ModuleRow      = { EspaceId: int; EspaceName: string; IsSystemModule: bool;
                        ModuleIsActive: bool; EspaceKind: string option;
                        EspaceSsKey: System.Guid option }
type EntityRow      = { EntityId: int; EntityName: string; PhysicalTableName: string;
                        EspaceId: int; EntityIsActive: bool; IsSystemEntity: bool;
                        IsExternalEntity: bool; DataKind: string option;
                        PrimaryKeySsKey: System.Guid option;
                        EntitySsKey: System.Guid option;
                        EntityDescription: string option }
type AttributeRow   = { AttrId: int; EntityId: int; AttrName: string;
                        AttrSsKey: System.Guid option; (* ... *) }
type RowsetBundle   = { Modules: ModuleRow list; Entities: EntityRow list;
                        Attributes: AttributeRow list (* extend under demand *) }

type SnapshotSource =
    | SnapshotFile     of path: string
    | SnapshotJson     of json: string
    | SnapshotRowsets  of bundle: RowsetBundle      // new variant
```

**Two materialization paths into `RowsetBundle`** ride above the
adapter, not inside it (preserving F# core's no-I/O discipline
and matching the JSON-path's "string in, Catalog out" shape):

1. **In-memory fixture path** (the test surface, parallel to
   `SnapshotJson`'s string-fixture pattern). Tests construct the
   bundle directly.
2. **Future C#-shell loader** (parallel to nothing yet — the
   `LiveOssysConnection` shape). Lives in a new C# project (e.g.,
   `Projection.Adapters.Osm.SqlClient`) that runs the SQL, pulls
   23 rowsets via `DbDataReader`, materializes into the F# DTO,
   and hands the bundle to the F# adapter. **Out of first-slice
   scope** — its absence is what scopes the slice.

This shape has the property that **the F# adapter sees only a
value type**, just like the JSON-string variant. The dispatch in
`parse` extends naturally and the existing variants are
unchanged.

---

## 3. DTO shape questions

**Hand-written F# records first; type providers stay deferred.**

The `DECISIONS 2026-05-13 — Type providers (consciously deferred)`
re-open trigger named "JSON-shape evolution" specifically because
`JsonProvider` derives a schema from a sample JSON document.
**Rowset-shape evolution has different characteristics**:

- The schema source is not JSON — it would be `SqlClientProvider`
  (FSharp.Data.SqlClient) materializing types from a live SQL
  Server connection at compile time. That's a *strictly worse*
  CI fragility surface than `JsonProvider`: building requires DB
  connectivity at compile time.
- The trigger ("JSON-shape evolution becomes a maintenance
  burden") names the cost of hand-maintaining DTOs against a
  moving JSON shape. The rowset shape moves under V1 SQL
  evolution; that evolution is gated by the V1 chain shipping (a
  slow cadence). Hand-maintenance burden is bounded.
- The hand-written DTOs already exist in C# at
  `IOutsystemsMetadataReader.cs:71-207` — F# transcription is
  mechanical. The compile-time derivation pays only at the point
  that a 24th rowset's shape lands faster than someone can
  transcribe a record.

**The DTO definitions could live in C#**, but the adapter-
language-rule's "natural language" framing suggests F# wins here:
- `DataTable` / `DataReader` interactions live in the C#-shell
  loader (out of first-slice scope).
- The DTOs themselves are just records the F# adapter consumes
  — F# records are more idiomatic at the consumption site.
- Once the C# loader exists, it materializes *into F# records*
  (interop is straightforward — F# records compile to C#-visible
  properties).

**Recommendation for the chapter-open document:** start with
hand-written F# records mirroring rowsets 1–3 (modules, entities,
attributes — the three rowsets that carry the three known
lossiness-class members). Defer rowsets 4–18 until empirical
pressure forces them. The OSSYS-adapter chapter's "extends as
the OSSYS arc continues" pattern (`DECISIONS 2026-05-15 — OSSYS
adapter translation rules`) applies recursively — the DTO surface
grows under fixture pressure, just like the translation rules.

---

## 4. Integration with existing `CatalogReader.parse`

The `parse` function at `CatalogReader.fs:694-718` is a single
match. The `SnapshotSource` DU at `:69-75` is closed. The session-
25 code commentary at `:36-68` already names `SnapshotRowsets` as
the planned-third-variant.

**The structural extension is minimal.** Add the variant to the
DU (line 75), add a third match arm to `parse` (after line 705):

```fsharp
| SnapshotRowsets bundle ->
    Task.FromResult(parseRowsetBundle bundle)
```

Where `parseRowsetBundle : RowsetBundle -> Result<Catalog>` is a
new private function paralleling `parseDocument`. The closed-DU
empirical-test discipline (`DECISIONS 2026-05-13`) predicts: F#
exhaustiveness errors should light up only at this match site.
The two existing `parseJsonString` / `parseDocument` paths are
untouched.

**The "something" type for `SnapshotRowsets of <something>` is
`RowsetBundle`** (in-memory record). Reasoning:

- A `SqlConnection` would force live-DB I/O into the F# Core
  boundary — violates the no-I/O discipline; couples to ADO.NET;
  defers the C# loader question into the adapter itself.
- A path to a SQL Server (connection string) is the future
  `LiveOssysConnection` variant — operator-decided as deferred.
- A pre-materialized bundle is the cleanest analog to the
  existing `SnapshotJson of string` variant (both are "data the
  caller supplies; adapter computes Catalog from data"). Tests
  construct bundles directly. The future C# loader produces
  bundles. The seam splits cleanly: I/O above the adapter, pure
  translation below.

**Re-evaluation of existing slices' rules under the rowset path.**
Rules 1–3 (the SsKey synthesis convention `OS_MOD_<modName>` /
`OS_KIND_<modName>_<entName>` /
`OS_ATTR_<modName>_<entName>_<attrName>`) collapse: with
`EntitySsKey` and `AttrSsKey` available, the synthesis is
replaced by `SsKey.original (entityRow.EntitySsKey.ToString())`
(or similar canonicalization of the Guid). **However**, the
synthesis convention is V2-fixture-internal (the V2 IR's existing
fixtures use `OS_KIND_*` form); changing the SsKey shape under
the rowset path would break parity with the JSON path. **Two
design options:**

1. **Coexisting SsKey shapes per source variant.** JSON path
   emits `OS_KIND_*` synthesized SsKeys; rowset path emits Guid-
   string SsKeys. Parity tests cross-source can't compare
   SsKeys directly; they compare structural shape. This is the
   simpler implementation but loses the parity-test surface.
2. **Canonicalize at translation time.** Both paths emit
   `OS_KIND_*` synthesized SsKeys; the rowset path's actual
   `EntitySsKey` Guid is carried via a side-channel (perhaps a
   future `Identity` axis on `Kind`, or an adapter-level
   diagnostics emission noting the V1 SsKey for cross-reference).
   **More compatible with parity testing**; pays for it with a
   deferred IR refinement.

The chapter-open document should pick option 1 for the first
slice (smaller scope) and name option 2 as a refinement deferred
until cross-source parity tests demand it.

---

## 5. Class-of-lossiness coverage plan

**Three known lossiness-class members. First slice: SsKey at
every level.**

Rationale for the leverage ranking:

- **SsKey** is foundational (every entity, every attribute, every
  module has one). Its resolution exercises all three rowsets
  simultaneously (modules, entities, attributes). Once the
  bundle-to-Catalog path resolves SsKey end-to-end, the
  structural template for resolving any rowset-borne field is
  established. This is also the **A1-bound** member explicitly
  named in `AXIOMS A1` per the docstring at
  `CatalogReader.fs:81-90`; resolving SsKey resolves the bound.
- **`EspaceKind`** has a single fixture surface (the external-
  entity fixture in `OsmCatalogReaderDifferentialTests.fs:398-526`).
  Activates rule 17's three-way distinction (currently a
  placeholder collapsing to `ExternalViaIntegrationStudio`).
  Specifically: with `EspaceKind` visible, the placeholder
  refines to `EspaceKind = "Extension"` (or whatever the IS-
  marker turns out to be) → `ExternalViaIntegrationStudio`;
  otherwise → `ExternalDirect`. The exact value semantics need
  empirical evidence from V1 — that's part of the SnapshotRowsets
  implementation work.
- **`isSystemEntity`** has *no current fixture* (per
  `DECISIONS 2026-05-19 — naming the two classes` at line
  5459-5461 — "observed during session-20 trace; not yet
  exercised by a fixture"). Surfacing it requires either a
  system-entity fixture authored under empirical pressure or a
  deferred trigger.

**Recommended slice plan (4–6 substantive slices):**

| Slice | Scope | Lossiness members resolved |
|-------|-------|----------------------------|
| **1** (chapter-open) | `SnapshotRowsets` variant + DTO surface (modules / entities / attributes) + `parseRowsetBundle` minimum + first fixture (mirrors session-18 minimal fixture) | SsKey at all three levels |
| **2** | Re-do session-19 reference-bearing fixture under the rowset path | (Refines reference SsKey synthesis; rule 16's same-module assumption gets tested against actual SsKey carriage) |
| **3** | Re-do session-20 external-entity fixture under the rowset path | `EspaceKind` activation; rule 17 refines from placeholder to three-way real |
| **4** | New system-entity fixture | `isSystemEntity` activation (and likely a V2 IR refinement: `Modality.System`? Or a `Kind.IsSystem: bool`? — boundary-discipline question, decided under empirical pressure) |
| **5** | Cross-source parity tests (JSON ↔ Rowset for the same fixture) | (No new lossiness; validates that both paths produce equivalent Catalogs modulo the documented SsKey-shape divergence) |
| **Deferred slices** | Per-table column structure (rowset 6); check constraints (rowset 7); future members | Each surfaces under fixture pressure as the OSSYS arc continues |

This roughly mirrors chapter-2's six-slice OSSYS arc (sessions
17–24), with the **inversion** that the chapter opens with the
heaviest mechanical work (DTO surface + bundle path) and
accumulates fixture-driven refinements. Chapter-2 opened with
empirical-pressure fixtures and accumulated rules; chapter-3's
SnapshotRowsets opens with the canonical resolution shape and
applies it to (re-)solve already-known questions.

---

## 6. Coexistence with `SnapshotJson` after `SnapshotRowsets` ships

**Both paths coexist permanently in the closed DU.** No
deprecation path is named in DECISIONS; the `ADMIRE.md:2364-2369`
framing reads "the two paths will coexist when the variant lands
— `SnapshotJson` remains valid; `SnapshotRowsets` is the path
that resolves A1's bound and provides the richer extensibility
surface."

**Operationally:**

- **Cross-source parity tests** (slice 5 above) become the
  structural validation surface: same V1 fixture data, exercised
  through both `SnapshotJson` and `SnapshotRowsets`, produces
  structurally-equivalent Catalogs. Discrepancies fall into
  named categories — SsKey-shape divergence (documented),
  `EspaceKind`-driven Origin refinement (documented),
  `isSystemEntity` carriage (documented). Anything outside those
  categories is a translation bug.
- **`SnapshotJson` remains the simpler test surface** for
  fixture-driven slice work that doesn't exercise lossiness-
  class members. Many V2 emitter / pass tests need a Catalog as
  input but don't care about Origin's three-way distinction or
  the SsKey shape; the JSON path is faster to author.
- **`SnapshotJson` is the fallback when rowsets are unavailable**
  — operator deployments where V1 produces only the JSON
  projection (the current production state) flow through the
  JSON path. SnapshotRowsets adds capability; it does not
  replace the JSON path's role as the V1-bridge surface.
- **No deprecation trigger named.** The chapter-open should
  explicitly defer naming a deprecation trigger; the operator
  decision (`DECISIONS 2026-05-15`, session-20 amendment) treats
  SnapshotRowsets as adding capability, not replacing capability.

---

## 7. Recommended chapter-open scoping

**Sequencing.** Two competing chapter-3 candidates exist
(`CHAPTER_2_CLOSE.md:186-208`):

- `Projection.Pipeline` canary chapter (DacFx + testcontainers +
  ephemeral SQL Server; substantial multi-session arc; the
  natural locus for `DacpacEmitter`).
- `SnapshotRowsets` implementation chapter.

**Recommendation: parallel-to / before canary, not after.**
Reasoning:

- The canary chapter's DacFx work needs a Catalog input. If the
  canary opens with the JSON path's Catalog, future canary work
  re-asks the SsKey-bound question against DacFx's behavior;
  SnapshotRowsets resolved first means the canary inherits a
  Catalog with full SsKey carriage.
- SnapshotRowsets is structurally smaller scope (no DacFx, no
  testcontainers, no ephemeral DB — just an additive translation
  path through known V1 SQL output). The mechanical groundwork
  (DTO transcription, parseRowsetBundle) is well-bounded.
- Subagent #4's DacpacEmitter scoping (per the chapter-close
  ritual) operates on Π-emitter shape, not on adapter-input
  shape. The two chapters don't share a critical surface.

**Estimated arc length: 5–6 sessions** (mirroring chapter-2's
six-slice OSSYS arc). The first slice is heavier than chapter-2's
first slice (DTO surface + variant scaffolding + first fixture
vs. just first fixture); slices 2–4 are lighter (re-doing
already-traced fixtures under the new path); slice 5 is the
cross-source parity discipline; deferred slices wait for
empirical pressure.

**Minimal first slice scope:** new F# records for rowsets 1–3;
new `RowsetBundle` carrier; new `SnapshotRowsets` DU variant;
new `parseRowsetBundle` private function paralleling
`parseDocument`; first failing test that mirrors the session-18
minimal fixture as a `RowsetBundle` literal; pass it. Defer
everything else.

---

## 8. Risks / open questions

The pre-scope cannot decide:

1. **The SsKey-shape divergence resolution.** Option 1 (per-
   source SsKey shape) vs. option 2 (canonicalize via deferred
   IR refinement). Cross-source parity tests are the trigger;
   the chapter-open document should pick option 1 explicitly and
   name option 2 as deferred.
2. **The `EspaceKind` value semantics.** What strings does V1
   emit, and which mark IS-extension vs. Direct external?
   Empirical evidence from V1 is required; the chapter-open
   document should defer this to the slice-3 fixture work and
   not pre-specify rule 17's refined form.
3. **The C#-shell loader's project location.** A new
   `Projection.Adapters.Osm.SqlClient` C# project? An extension
   to the existing F# adapter project? The cherry-pick discipline
   plus `DECISIONS 2026-05-09` (adapter language rule) suggest C#
   project, but it's out of first-slice scope; the chapter-open
   document defers this to slice 5+ or to a separate trigger
   ("when `LiveOssysConnection` is reopened").
4. **Whether `SnapshotRowsets` triggers an `ICatalogReader`
   interface materialization.** `DECISIONS` lists `ICatalogReader`
   Position B → A trigger as "second catalog source materializes."
   `SnapshotRowsets` is *not* a second source — it's a second
   *variant* of the existing OSSYS source. The interface trigger
   does *not* fire here; the chapter-open document should
   explicitly name this to prevent silent trigger drift.
5. **Whether new fixture-driven IR refinements are in scope for
   the first arc.** `isSystemEntity` activation almost certainly
   demands an IR-level decision (e.g., `Modality.System` variant?
   `Kind.IsSystem: bool` field?). That's a boundary-discipline
   question (not lossiness); the chapter-open document should
   explicitly scope IR refinement *out* of the first arc and
   route it through a follow-on slice once `isSystemEntity` is
   visible.
6. **Whether parity tests are unit or integration.** The
   `SnapshotFile` parity test at
   `OsmCatalogReaderDifferentialTests.fs:184` already deferred
   this question with a `Skip` reason naming "this unit-test
   project or in a separate integration-test surface." The same
   question recurs for SnapshotJson↔SnapshotRowsets parity. The
   chapter-open document inherits the deferred question.
7. **Whether the V1 C# DTO surface in `IOutsystemsMetadataReader.cs`
   should be re-mirrored or re-derived.** Mechanical transcription
   is fastest; structural re-derivation (matching V2's algebraic-
   naming convention, e.g., `KindRow` rather than `EntityRow`) is
   the V2-style-coherent move. The chapter-open document should
   pick V2-naming and name the divergence explicitly.

---

## Files load-bearing for the chapter-open document

- `/home/user/outsystems-ddl-exporter/src/AdvancedSql/outsystems_metadata_rowsets.sql`
- `/home/user/outsystems-ddl-exporter/src/Osm.Pipeline/SqlExtraction/SnapshotJsonBuilder.cs`
- `/home/user/outsystems-ddl-exporter/src/Osm.Pipeline/SqlExtraction/IOutsystemsMetadataReader.cs`
- `/home/user/outsystems-ddl-exporter/src/Osm.Pipeline/SqlExtraction/MetadataSnapshotRunner.cs`
- `/home/user/outsystems-ddl-exporter/src/Osm.Pipeline/SqlExtraction/MetadataAccumulator.cs`
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Adapters.Osm/CatalogReader.fs`
- `/home/user/outsystems-ddl-exporter/sidecar/projection/tests/Projection.Tests/OsmCatalogReaderDifferentialTests.fs`
- `/home/user/outsystems-ddl-exporter/sidecar/projection/DECISIONS.md`
  (entries 2026-05-15 OSSYS adapter parse signature; 2026-05-15
  OSSYS translation rules; 2026-05-19 naming the two classes;
  2026-05-21 chapter 2 close)
- `/home/user/outsystems-ddl-exporter/sidecar/projection/ADMIRE.md`
  (2026-05-13 OSSYS catalog producer entry)
- `/home/user/outsystems-ddl-exporter/sidecar/projection/CHAPTER_2_CLOSE.md`
  (forward signals at line 186)
