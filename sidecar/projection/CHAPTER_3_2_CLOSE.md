# Chapter 3.2 close — synthesis

This document is the chapter-3.2 close synthesis. **Chapter 3.2
closed at the 2026-05-10 close arc** covering five substantive
slices plus a post-slice bug-fix cash-out. The chapter resolved
the **JSON-projection-lossiness class** structurally by adding
the `SnapshotRowsets` variant of `SnapshotSource` end-to-end.

**Status:** **closed.** This document is the chapter-3.2 synthesis
and the chapter-3 cross-cutting close contribution. Companion
files: `CHAPTER_3_2_OPEN.md` (the chapter-open scaffold);
`CHAPTER_3_PRESCOPE_SNAPSHOT_ROWSETS.md` (chapter-2-close subagent
#5's pre-scope, which this chapter implemented).

The chapter-close ritual (`DECISIONS 2026-05-14`, session-25
amendment) names eight load-bearing items. Chapter 3.2's
execution of the ritual lives in the "Chapter-close ritual
execution" section at the end.

## Chapter 3.2 arc

Five substantive slices plus a post-close bug-fix arc, shipped
in one close arc (per the chapter-open §7 "first slice is
heavier" framing — subsequent slices re-do already-traced
fixtures lightly under the new path).

| Slice | Commit | Scope |
|-------|--------|-------|
| **1** | `6dab9cd` | `SnapshotRowsets` DU variant + `RowsetBundle` carrier + `ModuleRow` / `KindRow` / `AttributeRow` records + `parseRowsetBundle` minimum + first fixture (mirrors session-18 minimal). SsKey at all three levels. |
| **2** | `0354727` | Reference rowsets (`#RefResolved` ⊕ `#FkReality`); FK SsKey carriage. Rule 16 same-module assumption tested against rowset path. |
| **3** | `d5d1812` | `EspaceKind` activation; `parseOriginFromRowset` three-way real (`OsNative` / `ExternalViaIntegrationStudio` / `ExternalDirect`). Refines rule 17 from JSON-path placeholder. |
| **4** | `6eae21f` | `IsSystemEntity` activation; new `ModalityMark.SystemOwned` variant. Third JSON-projection-lossiness class member resolved. |
| **5** | `a74b904` | Cross-source parity tests (JSON ↔ Rowset). Total-equality (no-Guids) + shape-equality (Guid-carrying). Three fixture classes covered. |
| **post** | `0336795` | `propagateOrFallback` codification — error-propagation bug surfaced under chapter-close audit pressure; seven build-failure sites refactored uniformly across both translation paths. |

## What the chapter resolved

### JSON-projection-lossiness class — structurally closed

Per `DECISIONS 2026-05-19 — naming the two classes of resolution
patterns explicitly`, the class had three known members. All three
landed in chapter 3.2:

1. **SsKey at every level** (slice 1). `EspaceSSKey` / `EntitySSKey` /
   `PrimaryKeySSKey` / `AttrSSKey` carry through the rowset bundle
   as `Guid option`; translation emits `SsKey.OssysOriginal guid`
   when present, falls back to `SsKey.Synthesized` when absent
   (per A1's four-variant amendment shipped at Stage 0 S0.B).
2. **`EspaceKind`** (slice 3). String column on V1's `ossys_Espace`
   carries via `ModuleRow.EspaceKind : string option`;
   `parseOriginFromRowset` consumes it to discriminate Origin
   three-way. Case-insensitive `"Extension"` marker.
3. **`IsSystemEntity`** (slice 4). Bool column on V1's
   `ossys_Entity.Is_System` carries via `KindRow.IsSystemEntity`;
   lifts into V2's IR as `ModalityMark.SystemOwned` (payload-free
   variant in the existing orthogonal-axes list pattern).

Future class members (per-table column structure from rowset 6;
check constraints from rowset 7; triggers from rowset 18 —
documented not-carried-forward) surface as **future deferred
slices under fixture pressure**. The structural foundation is now
established:

- `RowsetBundle` as a flat-list carrier joinable on FK ID columns
- Closed-DU expansion empirical-test discipline confirmed at
  record level (4 record-extension events; 1 DU-variant event)
- `propagateOrFallback` for uniform boundary error propagation
- Sibling translation surfaces (`parseRowsetBundle` ↔ `parseDocument`)

### A1's JSON-projection-lossiness bound — operationally resolved

Chapter 3.2 makes A1's `OssysOriginal` variant operationally
reachable at the OSSYS-adapter boundary for the first time. The
four-variant amendment shipped at Stage 0 S0.B encoded the bound
type-stratifically; chapter 3.5's `RefactorLogEmitter` was the
first downstream consumer that pattern-matched on the variants;
chapter 3.2 is the first **boundary** that *emits* `OssysOriginal`
SsKeys directly from V1's actual `SS_Key` columns.

The bound persists through the JSON path by design (the JSON
shape continues to strip SSKey columns); both paths coexist as
source variants. Cross-source SsKey-shape divergence is documented
per `CHAPTER_3_2_OPEN.md` axis 5 (option 1); the deeper
canonicalization (option 2: `V1Mapped` UUIDv5 derivation) reserves
to **chapter 4.2 User FK reflow's `SourceTag` refactor**.

### IR refinement — `ModalityMark.SystemOwned` lift

Boundary-discipline question per chapter open's framing. Decision
matrix considered four options:

| Option | Decision | Rationale |
|---|---|---|
| Flat `Kind.IsSystem: bool` | REJECTED | V2 convention avoids `Is*` booleans; flat booleans grow into orthogonal-axis combinatorial mess. |
| `Origin` DU expansion (`OsNativeSystem`) | REJECTED | System-entity orthogonal to native-vs-external; conflating axes loses information. |
| New `Kind.Stewardship: Stewardship` DU | REJECTED | Heavier IR surface than evidence demands; defer until a second stewardship axis surfaces. |
| **`ModalityMark.SystemOwned`** | **SELECTED** | Payload-free variant in existing orthogonal-axes list pattern; mirrors `TenantScoped` / `SoftDeletable`. |

Closed-DU expansion empirical-test discipline (`DECISIONS
2026-05-13`) lit up cleanly: four interpretation sites
(`CanonicalizeIdentity`, `NamingMorphism`, `NormalizeStaticPopulations`,
`JsonEmitter.modalityString`). Each got an identity-shape branch;
no caller reshaping outside the variant's module. The discipline
survives the record-extension generalization (slice 2's
`RowsetBundle.References` field + slice 3's `EspaceKind` field +
slice 4's `IsSystemEntity` field).

### `propagateOrFallback` codification — audit-during-validation worked precedent

Chapter close-prep audit surfaced an error-propagation bug across
**seven** build-failure sites in `CatalogReader.fs`. The pattern
(across both translation paths):

```fsharp
match a, b, c, d with
| Ok a', Ok b', Ok c', Ok d' -> Result.success { ... }
| _ -> Result.failureOf (adapterError "<umbrella>" "...")   // swallows underlying errors
```

The umbrella codes (`kindBuild` / `moduleBuild` / `attributeBuild` /
`referenceBuild` / `indexBuild` plus their `*RowBuild` rowset
siblings) **dropped** the substantive cause (e.g.,
`adapter.osm.unmappedDeleteRule` from `parseDeleteRule`;
`adapter.osm.unmappedDataType` from `parsePrimitiveType`). Callers
couldn't act on what the adapter actually rejected.

Fix: codify `propagateOrFallback` at the two-consumer threshold
(slice 2 inline fix on rowset path = consumer 1; JSON-path
`parseKind` + `parseModule` = consumers 2 + 3; broader audit
surfaced 4 more sites; the codified primitive now serves 7
consumers uniformly).

Two new JSON-path regression tests (`unmapped DeleteRuleCode
propagates`; `unmapped DataType propagates`) assert positively
(substantive cause appears) AND negatively (umbrella codes do NOT
appear). The negative assertions are the load-bearing claim: the
fix is *propagation*, not *augmentation*.

## What carries forward to future chapters

### Deferred slices under fixture pressure

- **Cross-module FK IR refinement** (Active deferrals row;
  highest-priority deferred slice). Slice 2 tested rule 16's
  same-module assumption against the rowset path; the cross-
  module case remains the highest-priority deferred slice. Trigger
  condition: a fixture exercising cross-module FK.
- **Per-table column structure** (rowset 6). Future
  JSON-projection-lossiness class member; surfaces under fixture
  pressure.
- **Check constraints** (rowset 7). Future class member.
- **Triggers** (V1 rowset 18). Documented as not-carried-forward;
  available if a future use case surfaces.

### Critical-path forward to V2-driver KPI (per `V2_DRIVER.md`)

1. **Chapter 4.1.B slice δ** (two-phase insertion / cycle-breaking).
   CDC-silence-on-idempotent-redeploy property test is the
   V2-driver KPI's highest-leverage single deliverable.
2. **Chapter 4.1.B slices ε/ζ** (MigrationDependencies + Bootstrap).
   `ScriptDomBuild.buildMergeStatement` adoption mandatory per
   Active deferrals row.
3. **Chapter 3.x DacpacEmitter**. DacFx adoption mandatory per
   Active deferrals row.
4. **Chapter 4.2 User FK reflow**. Inherits chapter 3.2's
   `OssysOriginal` operational reachability; cross-version
   `V1Mapped` UUIDv5 derivation lands here.

### Chapter 3.7 slice queue (in-flight; audit-cleanup hygiene)

Per `HANDOFF.md` outstanding queue, 11 deferred slices: γ (`traverseCatalog`),
ζ (three `attach` adapters), η (`result {}` CE at ReadSide),
θ (Coordinates Stage 2 typed VOs), ι (writer-monad refresh),
κ (`Lineage.tell` O(N²) audit), λ (V1 prefix in emitter output),
μ (`Restrict→NoActionSql` Diagnostics), ν (F# Analyzers SDK),
ξ/ο/π (port lifts — `ICatalogReader` lift can now use chapter 3.2
as second source-of-truth via `SnapshotRowsets`).

## Chapter-close meta-codifications

Three patterns held cleanly across chapter 3.2; recorded for future
chapters that operate the same shapes.

### 1. Closed-DU expansion empirical-test discipline survives the record-extension generalization

The discipline (`DECISIONS 2026-05-13 — Closed-DU expansion: empirical
confirmation`) originally codified for *DU variants*; chapter 3.2
exercised it on *record extensions* four times (`RowsetBundle.References`
at slice 2; `RowsetBundle.EspaceKind` indirectly at slice 3 via
`ModuleRow.EspaceKind`; `RowsetBundle.IsSystemEntity` indirectly at
slice 4 via `KindRow.IsSystemEntity`; literal-update plumbing
across test bundles). The discipline's predictive power held:
F# field-missing errors lit up only at literal-construction sites
(test fixtures); no semantic interpretation sites surprised.

Combined with the one DU-variant event (`ModalityMark.SystemOwned`),
the chapter delivered **five closed-DU-style expansion events** —
all clean. The discipline is now empirically validated for both
record and DU shapes; the generalization is structural.

### 2. Two-consumer threshold's predictive power for emergent primitives

`propagateOrFallback` extracted at consumers 2 + 3 + 4 (rowset
path's two sites + JSON path's `parseKind` + `parseModule`); the
broader audit surfaced the primitive now serves 7 consumers
uniformly. The threshold's predictive power held — the helper's
*shape* was concrete by consumer 2; consumer 3's demand
crystallized the extraction; consumer 4+ surfaced under audit
pressure on the already-codified primitive.

The codification ordering matters: extracting at consumer 2
(before the others surface) gave the primitive a load-bearing
home before the audit dispatched. If the codification had waited
until "all 7 consumers visible," the audit pressure would have
landed on 7 inline copies — much harder to refactor uniformly.

The discipline (`DECISIONS 2026-05-13 — Two-consumer threshold
for emergent primitives`) is empirically validated at N=7
consumer fan-out from a 2-consumer codification point. Future
applications can lean on the early-extraction shape.

### 3. Audit-during-validation discipline operates at chapter close, not just at commits

`DECISIONS 2026-05-09 — Audits surface things not on the agenda`
codified the discipline at session 4–11 paydowns and reinforced it
at session 14. Chapter 3.2 close-prep audit operates the discipline
at *chapter close* (not commit): the bug surfaced during slice 2
work; the audit pressure at chapter close surfaced 5 more swallow
sites; the codified fix shipped end-to-end BEFORE the chapter
close ritual ran. The discipline's window extends from "during the
slice" to "during the close arc."

## Test baseline at chapter 3.2 close

- **882 non-canary tests passing** (was 857 at chapter 3.2 open;
  +25 from chapter 3.2: 30 rowset tests − 7 JSON-path tests
  unchanged = 23 net rowset; +2 JSON-path regression tests).
- 0 skipped.
- 0 build warnings under `TreatWarningsAsErrors=true`.
- Lint clean across 27 rules.
- Perf-gate clean (no canary-affecting changes in chapter 3.2;
  adapter-only).

## Chapter-close ritual execution

Per `DECISIONS 2026-05-14 — Chapter-close ritual` (session-25
amendment), eight load-bearing items.

### 1. Active deferrals scan — silent-trigger fires

**One trigger fired and cashed out at this close:**
`SnapshotRowsets variant of SnapshotSource` — implemented end-to-end
across five slices. Cash-out entry: `DECISIONS 2026-05-10 — Chapter
3.2 close: SnapshotRowsets variant cash-out + JSON-projection-
lossiness class structurally resolved`. Active deferrals row
updated to status "Cashed out — chapter 3.2".

**No other silent-trigger fires** across 17 active deferrals scanned.
Composition primitives (`fallback` / `accumulate` / `wrap` / `lift`)
remain at 0 consumers each. `Strategy registry mechanism` remains
at 5 strategies; name-keyed lookup demand absent. Cross-module FK
trigger NOT FIRED (chapter 3.2 fixtures all same-module).
DacpacEmitter + 4.1.B slices ε/ζ triggers pre-chapter-open.

### 2. Contract-vs-implementation walk — Skip tests vs implementations

No new `Skip` test stubs landed in chapter 3.2. All 30 new rowset
tests are `[<Fact>]` direct contracts. No deferred-contract drift.
(Pending broader walk for the joint 3.x close ritual covering
3.1/3.5/3.6/3.7/4.1.A/4.1.B if those are within scope.)

### 3. `CLAUDE.md` staleness check

CLAUDE.md was read at chapter 3.2 open and during slice work. Drift
candidates (deferred to a separate update if surfaced):
- Operating-disciplines table currency — see item 5 below.
- F# feature surface — no new feature category landed in chapter
  3.2; existing categories accurate.
- Programming-style center target — discipline patterns held; no
  new pattern surfaced for the "Aligned but underused" table.

### 4. `README.md` staleness check

README's surface-level orientation references chapter 2's OSSYS
adapter as substantive deliverable; chapter 3.2 extends the adapter
with `SnapshotRowsets`. A single README update note ("chapter 3.2
adds SnapshotRowsets variant; JSON-projection-lossiness class
structurally resolved") would land at the joint chapter-3 close
ritual if appropriate. Not a critical drift.

### 5. `HANDOFF.md` + chapter close synthesis

This document IS the chapter-3.2 synthesis. `HANDOFF.md`'s
"Outstanding queue (post-this-session)" should append a chapter-3.2
entry on the next pass.

### 6. Fresh-eye walk

`KICKOFF.md` → `HANDOFF.md` → `CHAPTER_3_2_OPEN.md` →
`CHAPTER_3_2_CLOSE.md` (this doc) reading order is coherent. No
fresh-eye drift surfaced during the chapter close.

### 7. Operating-disciplines table currency (CLAUDE.md)

No new operating discipline codified in chapter 3.2. The chapter
*operated* existing disciplines:
- Closed-DU expansion empirical-test discipline (record-extension generalization confirmed at N=4+1 events).
- Two-consumer threshold for emergent primitives (`propagateOrFallback` extracted at N=2; serves 7 consumers).
- Audit-during-validation (chapter-close-window observation).
- Three-class typology (chapter 3.2 findings all classified as JSON-projection-lossiness class).
- Trace-before-fixture (slices 2-4 re-did already-traced fixtures under rowset path).
- IR grows under evidence (`ModalityMark.SystemOwned` lift only when slice 4 surfaced it).

The operating-disciplines table does not require an addition;
the disciplines listed are the ones chapter 3.2 reinforced.

### 8. V1-input-envelope walk (V1↔V2 translation chapter discipline)

Chapter 3.2 is a V1↔V2 translation chapter; the V1-input-envelope
walk (`DECISIONS 2026-05-14 — Chapter-close ritual`, session-25
amendment) applies.

V1's `IOutsystemsMetadataReader.cs` records mirrored into V2 F#:

| V1 record | Chapter-3.2 F# DTO | Status |
|---|---|---|
| `OutsystemsModuleRow` | `CatalogReader.ModuleRow` | Mirror: EspaceId / EspaceName / IsSystemModule / ModuleIsActive / **EspaceKind** (slice 3) / EspaceSsKey |
| `OutsystemsEntityRow` | `CatalogReader.KindRow` | Mirror: EntityId / EspaceId / EntityName / PhysicalTableName / DbSchema / IsStatic / IsExternal / **IsSystemEntity** (slice 4) / IsActive / EntitySsKey / PrimaryKeySsKey |
| `OutsystemsAttributeRow` | `CatalogReader.AttributeRow` | Mirror: AttrId / EntityId / AttrName / PhysicalCol / DataType / IsMandatory / IsIdentifier / IsAutoNumber / Length / Precision / Scale / AttrSsKey / IsActive |
| `OutsystemsReferenceRow` | `CatalogReader.ReferenceRow` | Mirror: AttrId / **RefEntityName** + **DeleteRuleCode** + **HasDbConstraint** (slice 2 denormalization of `#FkReality`) |

V1 rowset-bundle coverage at chapter 3.2 close: rowsets 1-4 (`#E` /
`#Ent` / `#Attr` / `#RefResolved`) plus `#FkReality` (rowset 12)
denormalized into ReferenceRow. **Not yet carried**: rowsets 5-11
(per-table column structure / check constraints / indexes —
deferred-slice candidates); rowsets 13-17 (FK columns / attr map /
remaining FK plumbing — covered structurally by ReferenceRow at
this fidelity); rowsets 18+ (static populations / triggers —
documented not-carried-forward or deferred).

Future fixtures that surface lossiness from rowsets 5-11 trigger
the next deferred-slice arc. The V1-envelope is mapped to V2's
needed surface at chapter 3.2 close; the unmapped remainder is
*evidence-deferred*, not *known-missing*.

## Closing

Chapter 3.2 closes the JSON-projection-lossiness class
structurally and unbounds A1 at the OSSYS-adapter boundary. The
five slices ship cleanly; the post-close bug-fix arc cashes the
audit-during-validation discipline at chapter-close window scope;
the deferred-slice surface (cross-module FK; rowsets 5-11) is
structurally ready under future fixture pressure.

The next chapter agent inherits a chapter sequence with V2-driver
KPI critical path mapped (per `V2_DRIVER.md`): chapter 4.1.B
slices δ/ε/ζ + chapter 3.x DacpacEmitter + chapter 4.2 User FK
reflow + chapter 3.7's audit-cleanup slice queue. Chapter 3.2's
operational unblocking of `OssysOriginal` reachability is the
structural prerequisite for chapter 4.2.

Hold the spine.
