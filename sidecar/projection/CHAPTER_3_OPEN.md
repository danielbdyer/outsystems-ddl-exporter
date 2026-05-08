# Chapter 3 open — `Projection.Pipeline` canary chapter

This document is the chapter-3 working surface. Chapter 3 is the
**canary chapter** — strategic-frame axis-4 from the session-17 OSSYS
strategic frame (`DECISIONS 2026-05-15 — Strategic frame for the OSSYS
implementation chapter`). It opens at session 27.

The canary's algebraic claim: **emit Π → deploy to ephemeral SQL Server →
read-side Π (read back) → compare-by-SsKey** closes as identity (modulo
documented bounds). The chapter consumes everything chapter 2 produced
and stresses it under real metadata.

**Status:** open (session 27; first substantive slice — read-side
adapter parses a hand-built DACPAC).

## How this document is used

  - **At chapter open (session 27):** strategic frame named, slice plan
    sketched, pre-scope reports indexed, T1 amendment scheduled as the
    first substantive AXIOMS work. Updated continuously through the
    chapter as findings land, slices close, and the slice plan refines
    under empirical pressure.
  - **At chapter-mid-audits (every 3–5 substantive sessions per the
    codified discipline, `DECISIONS 2026-05-19 — Chapter-mid-audit as a
    routine practice`):** the chapter-mid-audit dispatch points scaffold
    here; CRITICAL findings go to next hygiene work, MINOR/OPEN
    accumulate.
  - **At chapter close:** this document folds into `CHAPTER_3_CLOSE.md`
    via the chapter-close ritual (`DECISIONS 2026-05-14 — Chapter-close
    ritual`).

## Inputs

  - **Sequencing rationale.** `DECISIONS 2026-05-22 — Chapter 3
    sequencing: canary opens on JSON path; SnapshotRowsets deferred
    until its forcing function fires`. The canary opens with the JSON
    path's Catalog as input; SsKey carriage from V1's Guid is
    orthogonal to canary's structural validation loop (synthesis-
    convention stability between input-Catalog and read-back-Catalog
    is what closes the loop).
  - **Pre-scope inputs.**
    - `CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md` (subagent #4). DacFx API
      surface; F# vs C# layering recommendation; byte-determinism risk;
      eight risks/open questions.
    - `CHAPTER_3_PRESCOPE_SNAPSHOT_ROWSETS.md` (subagent #5). Held in
      reserve; reads as chapter-open input when SnapshotRowsets's
      forcing function fires.
  - **Companion deferral.** Active deferrals index row "DacFx
    integration in `Projection.Targets.SSDT.DacpacEmitter`" was
    re-deferred at session 24 with tighter trigger condition (real
    Catalog flowing end-to-end through a pipeline exercising T11 on
    real metadata; canary chapter is the natural locus). Trigger fires
    when DacpacEmitter implementation begins (planned slice 5+).

## Strategic-frame axes (this chapter's load-bearing structure)

Per the strategic-frame axis-naming discipline (`DECISIONS 2026-05-15`,
session 17 framework-extension amendment for multi-session chapters
generally). Eight axes name the work the chapter is committed to
shaping; future slices refine under empirical pressure rather than
adding axes ad-hoc.

### Axis 1 — Round-trip closure as the algebraic claim

The canary's correctness condition: **`Catalog → DacpacEmitter →
DacPackage → DacServices.Deploy → DacServices.Extract (or read-side
DACPAC reader) → Catalog'` is the identity** modulo documented bounds.
Composition of A4 (identity-equality is structural, by SsKey),
T1 (determinism), and T11 (sibling-Π commutativity).

Bound under JSON-path input: synthesized SsKeys (`OS_KIND_<mod>_<ent>`)
mean rename produces a different identity; the canary loop preserves
the rename consistently across both sides because both synthesize from
physical names. The canary loop **does not** restore A1 in its strong
form (Guid-stable identity); it does close under name-based synthesis,
which is what its consumers need.

### Axis 2 — Read-side adapter as a new V1↔V2 translation surface

`DACPAC bytes → V2 Catalog` is structurally analogous to
`OSSYS JSON → V2 Catalog` (chapter 2) but the source is different:
SQL Server's structural model exposed through DacFx, not V1's
extraction JSON. The three-class typology (`DECISIONS 2026-05-21 —
Chapter 2 close: alternative-IR-surface class`) applies; new
translation findings classify under empirical pressure.

The typology may extend with a fourth class here, or the existing
three may suffice. Open question; classify under evidence per the
chapter-2 architect's framing of the cross-module FK question.

### Axis 3 — Synthesis-convention stability

Read-side adapter mirrors OSSYS's SsKey synthesis convention exactly:
`OS_MOD_<modName>`, `OS_KIND_<modName>_<entName>`,
`OS_ATTR_<modName>_<entName>_<attrName>`,
`OS_REF_<srcModule>_<srcEntity>_<viaAttr>`,
`OS_IDX_<modName>_<entName>_<idxName>`. Deviations require an explicit
amendment because the canary loop closes only when both sides use the
same synthesis (`DECISIONS 2026-05-22`).

Open question: SQL Server's `[schema].[table]` carries schema as the
namespace, not "module." The read-side adapter must derive a synthetic
"module" from somewhere — either from `Kind.Physical.Schema` (if
canary-emitted DACPAC uses module name as schema) or from a separate
package-metadata channel (Module → DACPAC `PackageMetadata`). Resolves
under fixture pressure when the first multi-module canary slice lands.

### Axis 4 — T1 normal-form discipline (binary emitters)

Subagent #4 flagged that vanilla `BuildPackage` emits non-byte-
deterministic DACPAC bytes (Origin.xml timestamps; zip-entry
timestamps). T1's "same triple, same surface, bit-identical" cannot
hold for DACPAC bytes out of the box.

**T1 amendment lands at chapter-open as the first substantive AXIOMS
work** (session 27 commit 2; before any DacpacEmitter code). The
amendment generalizes T1 to **the projection language's normal form**:
bytes for text/JSON; loaded-and-enumerated `TSqlModel` structure for
DACPAC. Byte-canonicalization is a separable operational concern,
handled post-hoc when a snapshot consumer requires byte-stability.

### Axis 5 — DacFx as foreign-API I/O; F# value-typed seam

Per `DECISIONS 2026-05-09 — Adapter language choice` and subagent #4's
recommendation, DacFx's idiom (disposable scopes, mutable model state,
exception-driven validation) is the C#-natural shape. The chapter
holds two dispositions:

  - **Read-side adapter (slices 1–4):** F#-first. `DacPackage.Load(stream)`
    + `new TSqlModel(stream, ...)` are pure-data operations with no DB
    connection; the DacFx surface fits the value-typed
    `Catalog -> Result<Catalog>` shape of the F# adapter pattern. C#
    wrapper deferred until a real coupling reason surfaces.
  - **DacpacEmitter (slice 5+):** Recommendation per subagent #4 —
    DacFx wrapper as a C# class inside `Projection.Pipeline` (the
    canary's C# project, named in `DECISIONS 2026-05-15`); F#
    DacpacEmitter delegates over `Catalog -> Result<byte[]>`. Final
    placement decided when DacpacEmitter implementation begins.

### Axis 6 — No-test-DB-for-now; testcontainers deferred until canary loop closes

DacFx's `DacPackage.Load(stream)` + `new TSqlModel(...)` parse a `.dacpac`
as a structured zip without any DB connection. The first 4 slices
exercise read-side adapter purely offline:

  - **Test fixtures** are produced by calling `BuildPackage(stream,
    model, metadata)` against a hand-crafted in-memory `TSqlModel`,
    yielding `byte[]`. Hermetic; no SQL Server.
  - **Differential tests** assert `parseDacpac bytes = expectedCatalog`
    using F#'s `Assert.Equal<Catalog>` shape (chapter-2 OSSYS adapter
    differential pattern).

Testcontainers + ephemeral SQL Server arrive when the canary loop
closes end-to-end (slice 6+: `DacServices.Deploy` an emitted DACPAC,
`DacServices.Extract` it back, compare).

### Axis 7 — Module → Schema mapping (open under fixture pressure)

Per subagent #4 risk #3. Three options:
(a) Module name → SQL Schema (`[ModuleName].[TableName]`);
(b) Module is emitter-side metadata only; Schema comes from
`Kind.Physical.Schema` (existing OSSYS-adapter behavior, where every
entity carries its `db_schema`);
(c) hybrid.

Default disposition (slice 1): **(b)** — `Kind.Physical.Schema` is the
existing hard contract; Module is a logical grouping that surfaces in
package metadata. Confirm under fixture pressure when the first
multi-module canary slice lands.

The read-side adapter parses `[schema].[table]` from
`Table.Name.Parts`; produces `Kind.Physical = { Schema; Table }`. The
"module" question is "which V2 module owns this Kind?" — under the
synthesis convention (axis 3) the module name is a synthesized value,
typically derived from package metadata or from a chapter-3-decided
heuristic.

### Axis 8 — Modality, Origin, and other V2 axes with no DacFx peer

Static populations route to a separate `StaticSeedsEmitter` (per
`DECISIONS 2026-05-15` data-emission classes). TenantScoped /
SoftDeletable shape tables via pass-time additions. Origin
(`OsNative` / `ExternalViaIntegrationStudio` / `ExternalDirect`) has
no DacFx representation; once a kind reaches the read-side adapter,
Origin must be reconstructed from package metadata, set to a default,
or carried as a Diagnostics emission. Defaults to `OsNative` for
slice 1; refines under empirical pressure.

## Slice plan (sketch; refines under empirical pressure)

| Slice | Status | Scope | What it surfaced / will surface |
|---|---|---|---|
| **1** | ✅ closed (session 27) | T1 amendment + new `Projection.Adapters.Dacpac` project + `parseDacpac` for one-Module / one-Kind / two-Attribute fixture (PK + Name) | DacFx parse path (pure-F#, no DB); minimum viable Catalog from DACPAC; T1 normal-form codified before any binary-emitter code |
| **2** | ✅ closed (session 28) | References (FKs) — cross-table FK in single-module fixture; `parseReference` + `parseReferenceAction` (DacFx `ForeignKeyAction` → V2 `ReferenceAction`); `multiColumnFk` Failure for >1 source columns until empirical pressure forces an IR refinement | Per-attribute Reference shape mirrors OSSYS rule 16; SetDefault → `unmappedFkAction` Failure (V2 has no SetDefault); reference SsKey synthesis under read-side path |
| **3** | ✅ closed (session 28) | Indexes — single-column unique, composite non-unique; `parseIndex`; PK structurally distinct (DacFx `PrimaryKeyConstraint` is not `Index`) | `DacIndex.TypeClass` + `GetParent` filter (relationship-based enumeration not exposed in DacFx 162); index column ordering preserved via DacFx referenced-relationship traversal |
| **4** | ✅ closed (session 28) | Composite primary key — test-only slice confirming slice-1's `primaryKeyColumnNames` Set<string> shape handles multi-column PKs structurally | No parser change; existing implementation handles composite PKs via `pkColumns.Contains` |
| **5** | open | DacpacEmitter (`Projection.Targets.SSDT.DacpacEmitter`) — emit Π consuming Catalog, producing DACPAC bytes via DacFx | C# wrapper layering decision (axis 5); first sibling-Π exercising T11 with read-side adapter; subagent #6 (DacFx BuildPackage scout, dispatched session 28) refines axis-4 byte-determinism risk under empirical pressure |
| **6** | open | First end-to-end loop offline — emit DACPAC bytes, immediately read them back, assert round-trip closure | T1 amendment exercised on real metadata; T11 commutativity; SsKey synthesis convention's stability under round-trip (axis 3) |
| **7+** | open | Testcontainers + ephemeral SQL Server; `DacServices.Deploy` + `DacServices.Extract`; full canary loop closure | Deployment-time invariants; byte-determinism question if a snapshot consumer demands byte-stability (axis 4 follow-on); multi-Module fixture forces axis-7 Module → Schema decision |

The slice plan is sketch-grade; chapter-2's six-slice OSSYS arc landed
in the order it landed, not the order session 17 sketched. Session 28
bundled slices 2/3/4 in one session because DacFx's API is well-defined
and the empirical novelty per slice was low (mechanical extension; no
unexpected impedance). The discipline is "atomic commits; classify
findings before resolution lands; chapter-mid-audit at 3-5 sessions
in" — bundling is fine when the work allows.

## Session log (chapter-3 progress)

  - **Session 27** — Chapter-open document; T1 amendment to AXIOMS;
    slice 1 (read-side adapter; one-Kind / two-Attribute fixture).
    Test baseline 632→637 (+2: slice-1 differential + T1-amended
    round-trip).
  - **Session 28** — Slices 2/3/4 bundled (FKs, indexes, composite
    PKs). Subagent #6 (DacFx BuildPackage behavior scout) dispatched
    in parallel for forthcoming DacpacEmitter sequencing (slice 5).
    Test baseline 637→640 (+3: slice-2 cross-table FK, slice-3
    indexes, slice-4 composite PK). One latent DacFx surprise found
    and resolved: `Microsoft.SqlServer.Dac.Model.Index` exposes
    `Unique` not `IsUnique`, and indexes are top-level model objects
    enumerated via `GetObjects` + `GetParent` filter (no `Host`
    relationship exists for Index). Aliased `DacIndex` to
    disambiguate from V2's `Catalog.Index` IR type.

## Open questions (chapter-3 working surface)

Subagent #4's eight risks/open questions land here as chapter-3
working material; resolution disposition follows.

  1. **Byte-determinism strategy.** Resolved at chapter-open by the T1
     amendment (axis 4): T1 holds at the projection language's normal
     form; binary canonicalization is operational. Re-opens only when
     a snapshot consumer demands byte-stable artifacts.
     **Refined by subagent #6 (`CHAPTER_3_SCOUTING_DACFX_BEHAVIOR.md`):**
     non-determinism surface is exactly 6 values — Origin.xml's
     `<Identity>` GUID + `<Start>` + `<End>` plus 4 zip-entry
     `LastWriteTime` fields. `model.xml`, `DacMetadata.xml`,
     `[Content_Types].xml` are byte-identical across runs. The
     pre-scope's "model.xml checksum derives from timestamps" claim was
     imprecise: checksum is SHA-256 of bytes, timestamp-independent.
     Post-hoc canonicalizer is small-surface, not zip-format-fragile.
  2. **F#-vs-C# wrapper layering for DacpacEmitter.** Open until slice
     5 (DacpacEmitter implementation). Default per subagent #4: C#
     wrapper inside `Projection.Pipeline` (or a new
     `Projection.Targets.SSDT.Dacpac` C# project); F# emitter
     delegates over a value-typed seam.
  3. **Module → Schema mapping.** Open; default disposition per axis 7
     is `Kind.Physical.Schema` only. Confirms under multi-module
     fixture pressure.
  4. **Modality marks at the dacpac surface.** Open; default for slice
     1 is "comments / extended properties." Refines if a downstream
     consumer needs structured access.
  5. **Pre/post-deployment scripts.** Confirmed out-of-scope for
     DacpacEmitter (per subagent #4); StaticSeeds /
     MigrationDependencies / Bootstrap are separate artifacts. No
     temptation to bolt scripts onto the dacpac via Packaging surgery
     in this chapter.
  6. **Origin handling at emit time.** Per A18 amended, Π doesn't
     filter by Origin (Selection is policy, applied in a pass).
     DacpacEmitter consumes Catalog blindly. Read-side adapter
     defaults Origin to `OsNative`; refines under empirical pressure.
  7. **DacFx version pinning.** `Microsoft.SqlServer.DacFx` v162.x
     (most recent stable; targets net6+/net9). Pin matches by NuGet
     package version per `DECISIONS 2026-05-15` hardcoded-version
     commitment.
  8. **`PackageMetadata` choices.** `Name = catalog-derived`;
     `Version = derived from snapshot hash` (per A22 — content-
     addressed snapshots); `Description = generated stamp`. Avoid
     embedding wall-clock time anywhere.
     **Refined by subagent #6:** `PackageMetadata.Version` is parsed
     as `System.Version` and silently truncates non-conforming strings
     to empty. The "derived from snapshot hash" disposition needs a
     Version-shape encoder; proposed shape (subagent #6's
     recommendation) — four int32 DWORDs of the SHA-256, encoded as
     `major.minor.build.revision`. Slice 5 implements the encoder.

## Forward signals for slice 5 (DacpacEmitter — from subagent #6)

Beyond refinements to the eight risks above, subagent #6 surfaced four
findings outside the original pre-scope's scope. They constrain
DacpacEmitter's implementation shape:

  - **Finding #9 — `AddObjects` is one-statement-per-call** (DacFx
    SQL71006). DacpacEmitter must emit one `CREATE TABLE` script per
    `AddObjects` call; concatenated multi-statement scripts fail.
    Constraint shape carries through to: separate calls per kind;
    separate calls per FK constraint when constraints are
    `ALTER TABLE ... ADD CONSTRAINT`.
  - **Finding #10 — `ALTER TABLE ... ADD <column>` is rejected**
    (SQL70645). Columns must be inline in `CREATE TABLE`. DacpacEmitter
    must declare all columns at table-creation time; no column-add
    after the fact.
  - **Finding #11 — Cycles are DacFx-tolerant.** `BuildPackage`
    accepts cyclic FK definitions; `Validate()` reports zero messages
    on cycles. V2's `CycleResolution` strategy (chapter 1) operates
    above the DacFx layer — DacFx does not reject cycles; the V2
    pre-emit pass does. Slice 6 round-trip closure should include
    a cycle fixture exercising both layers.
  - **Finding #12 — Malformed-input `TSqlModel(path, Memory)` failures
    surface as one diagnostic shape:** `DacServicesException` "Could
    not load schema model from package." Read-side adapter's `loadModel`
    helper already wraps with `tryWith`; the error code surface is
    therefore well-bounded.

Slice 5 inherits these as construction discipline. The scout's full
report at `CHAPTER_3_SCOUTING_DACFX_BEHAVIOR.md` is the canonical
surface; this section is a slice-planning summary.

## Forward signals from session 26

Cross-module FK slice (session 26) closed with a latent `parseKind` /
`parseModule` Failure-propagation gap surfaced by strict-disposition
tests. The fix landed atomically with the rule 16 amendment. The
**lesson generalizes**: when a slice exercises a previously-untested
failure path, latent gaps surface; explicit Failure arms are cheap and
diagnostic codes need to survive cascades. Carry forward into chapter
3's read-side adapter — its parse function will likewise need explicit
Failure arms in its outer match cascades.

## Closing note at chapter open

Chapter 3 is the chapter where everything chapter 2 produced gets
stressed under real metadata. The disciplines hold; the algebra holds;
the slice plan adapts. The chapter is open.
