# Chapter 3 pre-scope — `Projection.Targets.SSDT.DacpacEmitter`

**Subagent #4 dispatched at session 25** (chapter-2 close runway,
per `DECISIONS 2026-05-20 — DacFx/DacpacEmitter cash-out` session-
24 amendment which tightened the trigger condition to "real
Catalog flowing end-to-end through a pipeline exercising T11
sibling-Π commutativity on real metadata; canary chapter
(`Projection.Pipeline`) is the natural locus").

This document is the chapter-3 chapter-open input for the
DacpacEmitter implementation chapter. The full pre-scope report
follows; the chapter-3 agent should treat this as the
chapter-open document's first draft and refine under empirical
pressure once the chapter opens.

---

## Investigation summary

The V2 sidecar (`/home/user/outsystems-ddl-exporter/sidecar/projection/`)
ships two sibling Π emitters today — `RawTextEmitter` (raw
`.sql`-ish text for diff-oracle use) and `JsonEmitter` (UTF-8
JSON via `System.Text.Json.Utf8JsonWriter`) — plus
`DistributionsEmitter` as the third sibling consuming Profile.
The C# trunk under `/home/user/outsystems-ddl-exporter/src/Osm.Smo/`
uses **SMO** (`Microsoft.SqlServer.Management.Smo` — live-DB
administration) rather than DacFx; **no DacFx code exists
anywhere in the repository today**. DacpacEmitter would be the
first DacFx integration in either codebase.

---

## 1. DacFx API surface relevant to V2

DacFx ships in NuGet packages `Microsoft.SqlServer.DacFx`
(currently v162.x targeting net6+) and exposes the public Model
API across two assemblies (`Microsoft.SqlServer.Dac.dll`,
`Microsoft.SqlServer.Dac.Extensions.dll`). The four classes V2
needs are:

**`TSqlModel`** (`Microsoft.SqlServer.Dac.Model`, in
`Microsoft.SqlServer.Dac.Extensions.dll`). Represents a SQL
Server schema model. The constructor V2 cares about is
`new TSqlModel(SqlServerVersion, TSqlModelOptions)` — creates an
empty model targeting a specific server version. Schema content
is added via `AddObjects(string script)` (or
`AddObjects(TSqlScript)`, or named-source
`AddOrUpdateObjects(script, sourceName, options)`). Objects can
be enumerated back out via `GetObjects(DacQueryScopes, params
ModelTypeClass[])` returning `IEnumerable<TSqlObject>`.
Implements `IDisposable`. Critical observation: **DacFx's public
API is script-text-driven, not object-graph-driven**. You feed
it `CREATE TABLE …` statements; you get loosely-typed
`TSqlObject` instances back. There is no public "construct a
Table object directly" API — the trunk's SMO emitter owns
table-as-object semantics; DacFx Model is closer to "parse +
index a script bundle." This shapes the impedance map (§2).

**`TSqlObject`** (`Microsoft.SqlServer.Dac.Model`). Loosely-typed
handle on a model element. Strongly-typed metadata classes
(`Table`, `Column`, `Index`, `ForeignKeyConstraint`, `View`, …)
provide the property keys (`Table.Columns`, `Column.Length`,
`Column.DataType`) used with `obj.GetProperty<T>(prop)` and
`obj.GetReferenced(rel)` / `obj.GetChildren()` / `obj.GetParent()`
for traversal. `TryGetScript(out string)` returns the round-
trippable T-SQL source; this is the operation that powers
script-back-out flows (model → filtered model → new model).
Names use `ObjectIdentifier.Parts` — first part is the schema.

**`DacPackage`** (`Microsoft.SqlServer.Dac`, in
`Microsoft.SqlServer.Dac.dll`). The `.dacpac` artifact (a zip-of-
XML — Origin.xml, model.xml, etc.). Key static methods:
`DacPackage.Load(stream | path, DacSchemaModelStorageType,
FileAccess)`. Properties: `PreDeploymentScript`,
`PostDeploymentScript`, `Version`, `Name`, `Description`.
Implements `IDisposable`.

**`DacPackageExtensions.BuildPackage`** (extension on the
namespace; lives in `Microsoft.SqlServer.Dac.Extensions.dll`).
**This is V2's serialization entry point.** Four overloads:
`BuildPackage(Stream, TSqlModel, PackageMetadata)`,
`BuildPackage(Stream, TSqlModel, PackageMetadata, PackageOptions)`,
plus path-based equivalents. The `Stream` form is the byte-stream
surface DacpacEmitter needs (writes a `.dacpac` zip to a
`MemoryStream`; emitter returns `byte[]`). `PackageMetadata`
carries `Name`, `Description`, `Version` — these become identity
in the package's manifest. `PackageOptions` adds refactor-log and
deployment-contributors. The legacy archive tutorial shows that
pre/post-deployment scripts are **not** supported via
`BuildPackage`; they require post-hoc `Package`-API surgery on
the zip. Mark this as a known limitation for chapter-3
sequencing.

**`DacServices`** (`Microsoft.SqlServer.Dac`). The deployment
driver — constructed with a connection string. Methods relevant
to the canary:
- `Deploy(DacPackage, dbName, upgradeExisting, DacDeployOptions)`
  — deploys the dacpac to an actual SQL Server.
- `Extract(stream | path, dbName, appName, version, …,
  DacExtractOptions)` — reads a database into a `.dacpac`.
- `GenerateDeployScript(DacPackage, …)` — produces the T-SQL the
  deployment would run, without touching the DB.
- `GenerateDeployReport` — XML report of deployment steps.
- `Publish` — Deploy + script + report in one call.

**Round-trip path.** V2 Catalog → emit T-SQL DDL strings →
`TSqlModel.AddObjects(scripts)` → `BuildPackage(stream, model,
metadata)` → `byte[]`. Validation: `DacPackage.Load(stream)` →
`new TSqlModel(stream, …)` → `model.GetObjects(Table.TypeClass,
…)` → re-derive Catalog and compare.

**F# vs C#.** The DacFx API is heavily object-instantiation- and
side-effect-oriented (`new TSqlModel(...)`, `IDisposable`
lifetimes, mutable model state via successive `AddObjects` calls,
exception-driven validation). It has F# signatures published
(the docs show `static member BuildPackage : Stream * TSqlModel *
PackageMetadata -> unit`) so it is *callable* from F#. But the
idiom — disposable scopes, dictionary-of-properties access via
`GetProperty<T>`, mutation of a model via successive script-add
— is the exact "object-instantiation-heavy, foreign-API-I/O"
shape `DECISIONS 2026-05-09` (Adapter language choice) sends to
C#. **Recommendation: the DacFx wrapper itself lives in C#
inside `Projection.Pipeline` (the canary's C# project, already
named in `DECISIONS 2026-05-15`); the F# DacpacEmitter calls
into that wrapper across a value-typed seam.** The seam is
`Catalog -> byte[]` (or `Catalog -> Result<byte[]>`); F# Π stays
pure, the C# wrapper owns the impure-feeling DacFx surface.

---

## 2. The IR-to-DacFx impedance map

Mapping V2's `Catalog` shape to DacFx primitives, axis by axis:

- **`Module`** → no native DacFx peer. `TSqlModel` is flat at the
  database level; DacFx expresses "module-ish grouping" only via
  **schema** (the SQL `CREATE SCHEMA`). The Catalog's Module is
  a coproduct cell (A11) — V2 must decide whether modules land
  as SQL schemas, as a no-op (one schema, table names
  disambiguate), or as an emitter-side annotation only. The
  trunk's `SmoTableBuilder` uses `Schema` from
  `PhysicalRealization`, not module name; the V2 Module is
  currently invisible to physical layout. RawTextEmitter prints
  module headers as comments; that's tolerable for debug oracle
  but DacpacEmitter will need an explicit decision (probably:
  Module → comment + module-name on the manifest; Schema → from
  `Kind.Physical.Schema`). Naming this in chapter-open.

- **`Kind`** → top-level `Table` (`ModelTypeClass =
  Table.TypeClass`). One `CREATE TABLE` script per kind. The
  schema-qualified name is `[k.Physical.Schema].[k.Physical.Table]`.
  View-backed kinds, table-valued external kinds, etc., are not
  in the IR today and would be widening events; DacpacEmitter
  starts table-only.

- **`Attribute`** → column on the table. Per-attribute mapping
  needs a `PrimitiveType → SQL type string` policy; today
  RawTextEmitter inlines a synthetic-default map (`Integer →
  INT`, `Decimal → DECIMAL(18,4)`, `Text → NVARCHAR(MAX)`, …),
  with the comment "this belongs in Policy when Policy lands."
  Per A18 amended, **Π consumes evidence subsets but not Policy**
  — so DacpacEmitter must either inherit RawTextEmitter's
  synthetic default or rely on a pass that has produced an
  emitter-consumable type-map artifact (per A32). Recommendation
  for first slice: inherit RawTextEmitter's synthetic map verbatim
  so the impedance is identical across siblings; T11 commutativity
  tests then check that `RawTextEmitter` and `DacpacEmitter`
  agree on every attribute's type rendering.

- **`Reference`** → `FOREIGN KEY` constraint inside or alongside
  `CREATE TABLE`. DacFx doesn't care which shape; `ALTER TABLE …
  ADD CONSTRAINT` works as a separately-added script.
  RawTextEmitter takes the `ALTER` form (separate from `CREATE
  TABLE`); DacpacEmitter can do the same. `OnDelete` translation
  already exists in RawTextEmitter (`renderAction`) — `NoAction →
  "NO ACTION"`, `Cascade → "CASCADE"`, `SetNull → "SET NULL"`,
  `Restrict → "NO ACTION"` (SQL Server convention). Reuse that
  mapping.

- **`Index`** → `CREATE INDEX [name] ON …` script (or `UNIQUE
  INDEX` if `IsUnique`). Composite indexes are list-of-attribute-
  SsKeys; the emitter resolves SsKey to column name via the
  kind's attribute lookup. DacFx ingests this cleanly.

- **`PrimaryKey` (composite)** → `PRIMARY KEY (col1, col2, …)`
  clause inside `CREATE TABLE`, populated from
  `Kind.Attributes |> List.filter IsPrimaryKey`. RawTextEmitter
  currently emits a `PK` inline comment but does not emit a real
  `PRIMARY KEY` clause; **DacpacEmitter must emit the real PK
  declaration** because DacFx validation will reject FK
  references that target a table without a declared PK. This is
  a known gap RawTextEmitter has elided as debug-oracle license;
  DacpacEmitter cannot.

- **`Origin`** → no DacFx peer.
  `OsNative`/`ExternalViaIntegrationStudio`/`ExternalDirect` are
  V2-IR-only tags. They affect *whether* a kind is emitted (per
  Selection policy in the pass layer) but not *how* — once a
  kind reaches the emitter, the Origin is informational only (a
  comment, at most).

- **`ModalityMark`** → no native DacFx representation:
  - `Static populations` → not schema; data emission territory.
    Per `DECISIONS 2026-05-15` the canary distinguishes three
    data-emission classes (`StaticSeeds`, `MigrationDependencies`,
    `Bootstrap`) as **separate artifacts**, not part of the
    schema dacpac. DacpacEmitter is schema-only; populations are
    out of scope, emitted by sibling artifact emitters (probably
    `StaticSeedsEmitter`).
  - `TenantScoped` → policy-discriminator-column; the
    discriminator column is policy-driven (per A14) and lands on
    the table as a regular attribute through a pass, not as
    anything Π-time.
  - `SoftDeletable` → policy-discriminator-column; same shape.

  All three modality marks are informational at Π time. Surface
  them as table extended properties? Or as comments? Lowest-
  coupling first: comments on the `CREATE TABLE` script, then
  promote to `EXTENDED PROPERTY` if a downstream consumer needs
  structured access.

**The general impedance shape.** V2's IR is generic-algebraic
(kinds, attributes, references, modality, origin); DacFx's model
is SQL-Server-prescriptive (tables, columns, constraints, indexes
— no concept of origin or modality). The mismatch is well-bounded:
**everything DacFx needs, V2 has; some V2 axes (Origin, Modality)
have no DacFx counterpart and either route through pass-time
decisions or appear as emitter-side annotations.** This is
exactly what A32 anticipated — passes attach values that emitters
consume; Π doesn't reach for Policy.

---

## 3. Π architectural fit

DacpacEmitter slots in as `Projection.Targets.SSDT.DacpacEmitter`
— sibling module to the existing `RawTextEmitter` in the same
project, same `[<RequireQualifiedAccess>]` shape:

```fsharp
module Projection.Targets.SSDT.DacpacEmitter
val emit : Catalog -> byte[]   // or: Result<byte[]>, depending on validation surface
```

A18 amended is honored by signature: no `Policy` parameter. The
emitter consumes `Catalog` only; if Profile-driven shaping
arrives later (e.g., type-correspondence based on observed value
distributions), the signature widens to
`Catalog -> Profile -> byte[]` per the Distributions precedent.

**T11 sibling-Π commutativity.** "Every Π's output should mention
every catalog kind by SsKey root." For text/JSON emitters this
is grep-able. For DACPAC bytes it requires:
1. Load the bytes back via `DacPackage.Load(stream)` →
   `new TSqlModel(...)`.
2. Enumerate `model.GetObjects(DacQueryScopes.UserDefined,
   Table.TypeClass)`.
3. Assert one Table per Kind; assert Table name corresponds to
   `Kind.SsKey` root (via either schema-qualified name or a
   derived round-trip mapping).

The T11 test for DacpacEmitter is structurally heavier than
RawTextEmitter's `String.Contains` form — it requires a load-
and-enumerate. Cost is acceptable; the existence of the round-
trip is itself the strongest commutativity guarantee.

**Byte-determinism — the critical risk.** `.dacpac` is a zip-of-
XML; the `Origin.xml` member is documented to embed `Operation`
timestamps (Start/End wall-clock times) and a checksum of
`model.xml`. The zip container itself embeds entry timestamps.
**Vanilla `BuildPackage` produces non-byte-deterministic output**
even for byte-identical models; two runs differ in Origin.xml
timestamps and, transitively, in the model.xml checksum and in
zip-entry timestamps. T1's "same input ⇒ byte-identical output"
therefore does not hold for DACPAC bytes out of the box.

Three strategies to consider at chapter-open (deferring choice
to chapter-3):

1. **Post-hoc canonicalization.** Build the dacpac, then open as
   zip, rewrite Origin.xml with pinned timestamps, recompute the
   model.xml checksum, re-pack with pinned zip-entry timestamps.
   Owns determinism explicitly; couples emitter to dacpac
   internal layout (fragile under DacFx version bumps).
2. **Determinism-by-content-hash, not by bytes.** Define T1 for
   DacpacEmitter as "identity-preserving content under model-API
   equality" rather than byte-equality. Round-trip via
   `DacPackage.Load` + `model.GetObjects` and compare structural
   content; assert that load-then-extract yields identical
   results across runs. Aligns with A22 (snapshots are content-
   addressed) — V2 hashes the *normalized model representation*,
   not the dacpac bytes.
3. **Hybrid.** Apply (1) for the bytes consumers see (file
   artifacts, snapshot store) and (2) for the algebraic claim.
   (1) is operational determinism; (2) is the algebra's claim.

The chapter-open should pick a strategy, ideally (2) for the
algebra and (1) for operations, and update T1's V2 amendment to
name the byte-vs-content distinction for binary emitters. This
is the highest-leverage open question DacpacEmitter raises.

---

## 4. Canary chapter dependencies

Per `DECISIONS 2026-05-15` (canary architectural commitments),
the canary's pipeline is: **emit → apply to ephemeral SQL Server
→ read back → compare to source Catalog**. DacpacEmitter is the
"emit" stage; the read-side adapter is the "read back" stage;
the comparison is structural (by SsKey, per A4).

What the canary needs from DacpacEmitter:

- **Deployable dacpac bytes.** `DacServices.Deploy` against an
  ephemeral SQL Server (testcontainers, version pinned to
  production per `DECISIONS 2026-05-15`) must succeed. This is
  stricter than "bytes that round-trip through DacFx" — the
  model must validate as deploy-ready: every FK has a target
  table with a declared PK; every type is a valid SQL Server
  type; every constraint is well-formed.
- **`DacServices.Extract` round-trip.** Optional but high-value
  for the read-side adapter's design: extract the deployed
  schema to a fresh dacpac, load it, and compare to the
  originally-emitted dacpac. Confirms deploy-extract round-trip
  identity at the dacpac level (separate from the read-side-
  adapter's Catalog round-trip).
- **One dacpac per catalog (recommended).** The canary's deploy-
  and-read-back is simplest with a single dacpac per Catalog.
  Multi-dacpac (per-schema or per-module) splits would require
  coordinated multi-deploy, which complicates atomicity. If V1
  ever needs multi-dacpac (cross-application boundaries, CLR
  isolation), defer to that evidence. **Chapter-open default:
  one dacpac for the whole catalog.**
- **No pre/post-deployment scripts in scope.** `BuildPackage`'s
  public API does not support them. The static-seed and
  bootstrap data-emission classes (`DECISIONS 2026-05-15`) are
  separate artifacts the canary applies after the schema
  dacpac; DacpacEmitter does not own them.
- **Deterministic deploy.** Two emit-then-deploy runs against an
  empty database should produce identical resulting schema. The
  byte-determinism question above is separate; deploy-
  determinism is satisfied by content-determinism (T1 on the
  model, not on the bytes).

The canary's testcontainers / ephemeral SQL Server requirements
live in `Projection.Pipeline` (C# project), not in DacpacEmitter.
DacpacEmitter's job ends at producing a `byte[]` (or `Stream`);
the canary's job is to apply it. Clean separation.

---

## 5. Recommended chapter-open scoping

**Sequencing.** Confirm session-24 cash-out's framing: the canary
chapter (`Projection.Pipeline`) opens with **read-side adapter
first** (DACPAC reader → V2 Catalog), then **DacpacEmitter
second**. The read-side adapter has two consumers from day one
(the canary's read-back; future operator drift detection —
`DECISIONS 2026-05-15`); DacpacEmitter has only the canary as a
near-term consumer until V1's deployment pipeline migrates to
consume V2 dacpacs. Read-side first because its IR-direction
(DACPAC → Catalog) lets us define the round-trip target before
we commit to the emit shape; DacpacEmitter's first slice can
then be tested by symmetric round-trip rather than by byte-
comparison.

**Minimal first slice.** Single-table Catalog (one Module, one
Kind, two attributes including a PK, no references, no indexes,
no modality marks). Emit → load → enumerate → assert one Table
with two Columns and a PK constraint. Skip byte-determinism for
this slice; cover content-determinism via DacFx round-trip. Add
T11 commutativity test: same Catalog through `RawTextEmitter`
and `DacpacEmitter` agree on the SsKey-root-mention property.

**Deferred slices in chapter-3 ordering.**
1. Multi-table Catalog (no FKs).
2. FK constraints (with `OnDelete` translation; tests every
   `ReferenceAction` variant).
3. Indexes (single-column unique; composite; non-unique).
4. Composite primary keys.
5. Cross-schema references (Module → Schema decision is forced
   here).
6. Modality marks: emit as comments / extended properties;
   surface that Static populations route to `StaticSeedsEmitter`,
   not DacpacEmitter.
7. Origin axis: confirm Π is origin-blind once a kind reaches
   the emitter (Selection in Policy decides whether to include
   external kinds; DacpacEmitter never inspects Origin).
8. Byte-determinism cash-out (post-hoc canonicalization) once a
   snapshot consumer requires it.
9. Cross-module FK (deferred from chapter 2 per the chapter-3
   handoff; lands here because the FK emission code already
   exists by this slice).

**Relationship to existing emitters.** DacpacEmitter is
**additive, not a replacement**. RawTextEmitter remains the
debug oracle (legible diffs, no DacFx dependency, fast tests);
JsonEmitter remains the structural snapshot; DacpacEmitter is
the deployment artifact. T11 holds across all three: same
Catalog ⇒ same kind-mention set, modulo the projection language.
The chapter-open scoping should restate this — DacpacEmitter
does not displace RawTextEmitter even when DacFx is available.

---

## 6. Risks / open questions for chapter-open

1. **Byte-determinism strategy.** DacFx-emitted dacpacs embed
   timestamps in Origin.xml and zip-entry headers, breaking T1
   byte-equality. Chapter-open must pick: (a) post-hoc
   canonicalize the zip; (b) redefine T1 for binary emitters as
   content-equality via DacFx round-trip; (c) hybrid. Recommend
   (b) for the algebra and (a) when a snapshot consumer requires
   byte-stable artifacts. Explicit T1 amendment likely required.
2. **F#-vs-C# wrapper layering.** Per `DECISIONS 2026-05-09`,
   foreign-API I/O lives in C#. Recommend: DacFx wrapper as a C#
   class inside `Projection.Pipeline` (already the canary's C#
   project per `DECISIONS 2026-05-15`); F# DacpacEmitter
   delegates over a `Catalog -> Result<byte[]>` seam. Open: does
   the DacFx wrapper live in `Projection.Pipeline` (canary's
   project, where `DacServices.Deploy` already lives) or a new
   `Projection.Targets.SSDT.Dacpac` C# project? Slight bias
   toward the latter for module cohesion (the SSDT target's
   binary half).
3. **Module → Schema mapping.** Modules have no DacFx peer.
   Chapter-open decides: (a) Module name becomes SQL Schema; (b)
   Module is emitter-side metadata only and Schema comes from
   `Kind.Physical.Schema`; (c) hybrid. Recommend (b) —
   `Physical.Schema` is the existing hard contract; Module is a
   logical grouping that surfaces in comments / package metadata.
   Confirm with V1 fixtures.
4. **Modality marks at the dacpac surface.** Static populations
   route to a separate emitter (clear). TenantScoped and
   SoftDeletable shape the table via pass-time additions (also
   clear, but not yet implemented). Open: do the marks
   themselves appear in the dacpac as `EXTENDED PROPERTY` for
   downstream tooling, or as comments only? Defer until a V1
   consumer of those properties surfaces.
5. **Pre/post-deployment scripts.** `BuildPackage`'s public API
   doesn't support them; the canary's data-emission classes
   (StaticSeeds / MigrationDependencies / Bootstrap) are
   separate artifacts. Confirm that DacpacEmitter's scope is
   schema-only and the data classes have their own emitters; no
   temptation to bolt scripts onto the dacpac via
   System.IO.Packaging surgery in this chapter.
6. **Origin handling at emit time.** Per A18 amended, Π doesn't
   filter by Origin (Selection is policy, applied in a pass).
   Chapter-open should restate this so DacpacEmitter doesn't
   accidentally reach for `if k.Origin = OsNative then …`.
   Anti-pattern explicitly named.
7. **DacFx version pinning.** `Microsoft.SqlServer.DacFx` v162.x
   targets net8 idiomatically; the pin should match production's
   SQL Server version (per `DECISIONS 2026-05-15`'s hardcoded-
   version commitment). The DacFx version is one more chunk of
   the canary's "exact-match-production" surface area.
8. **`PackageMetadata` choices.** `Name`, `Description`,
   `Version` go into the package manifest. Recommend `Name =
   catalog-derived`, `Version = derived from snapshot hash`
   (per A22 — content-addressed snapshots), `Description =
   generated stamp`. Avoid embedding wall-clock time anywhere in
   metadata fields the emitter controls.

---

## Files load-bearing for the chapter-open document

- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Targets.SSDT/RawTextEmitter.fs`
  — sibling Π pattern; type-mapping defaults to inherit verbatim
  for first slice.
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Targets.Json/JsonEmitter.fs`
  — sibling Π pattern; deterministic-bytes via `Utf8JsonWriter`
  precedent.
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Core/Catalog.fs`
  — the IR shape DacpacEmitter consumes.
- `/home/user/outsystems-ddl-exporter/sidecar/projection/AXIOMS.md`
  (lines 540–590, A18 amended; T11) — emitter contract.
- `/home/user/outsystems-ddl-exporter/sidecar/projection/DECISIONS.md`
  lines 470–556 (DacFx integration deferred + session-24
  amendment), lines 4175–4380 (OSSYS / canary strategic frame),
  lines 4326–4347 (`Projection.Pipeline` as a new C# project).
- `/home/user/outsystems-ddl-exporter/src/Osm.Smo/SmoTableBuilder.cs`
  and `SmoEntityEmitter.cs` — V1's nearest analog (SMO, not
  DacFx, but the IR-to-DDL translation patterns — column-build,
  FK-build, index-build, name-quote, type-mapping — are reusable
  across both libraries).

## Sources

- [TSqlModel Class (Microsoft.SqlServer.Dac.Model)](https://learn.microsoft.com/en-us/dotnet/api/microsoft.sqlserver.dac.model.tsqlmodel)
- [DacPackage Class (Microsoft.SqlServer.Dac)](https://learn.microsoft.com/en-us/dotnet/api/microsoft.sqlserver.dac.dacpackage)
- [DacServices Class (Microsoft.SqlServer.Dac)](https://learn.microsoft.com/en-us/dotnet/api/microsoft.sqlserver.dac.dacservices)
- [DacPackageExtensions.BuildPackage Method](https://learn.microsoft.com/en-us/dotnet/api/microsoft.sqlserver.dac.dacpackageextensions.buildpackage)
- [DacFx Public Model Tutorial (archived)](https://learn.microsoft.com/en-us/archive/blogs/ssdt/dacfx-public-model-tutorial)
- [DACExtensions samples (microsoft/DACExtensions)](https://github.com/microsoft/DACExtensions)
- [DacFx GitHub repository](https://github.com/microsoft/DacFx)
- [DacFx .NET Core generate .dacpac without Origin.xml — issue #32](https://github.com/microsoft/DACExtensions/issues/32)
- ['Origin.xml' is missing from the dacpac package — Microsoft Learn archive](https://learn.microsoft.com/en-us/archive/msdn-technet-forums/c0d64441-ed06-4bbc-9802-64cb51e947cc)
