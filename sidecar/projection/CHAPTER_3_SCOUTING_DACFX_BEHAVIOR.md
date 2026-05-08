# Chapter 3 scouting — DacFx `BuildPackage` empirical behavior

**Scouting subagent** dispatched at session 27 (chapter-3 working
surface). Companion to `CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md`
(subagent #4). The pre-scope named the API surface and the eight
risks; this scouting refines those risks under empirical pressure
**before** slice 5 (DacpacEmitter) opens, by running concrete probes
against `Microsoft.SqlServer.DacFx` v162.5.57 (the version pinned in
`src/Projection.Adapters.Dacpac/Projection.Adapters.Dacpac.fsproj`).

Read this document for the empirical bounds on each behavior. Cite
it from slice 5+ planning when committing to a DacpacEmitter shape;
re-probe if DacFx is bumped.

## Bounds of investigation

  - **Version probed:** `Microsoft.SqlServer.DacFx` 162.5.57 on
    .NET 9 (SDK 9.0.305), Linux. The package's `lib/net8.0`
    assemblies load on net9 without trouble.
  - **Probe locus:** `/tmp/dacfx-probe/` (csproj + Program.cs +
    captured outputs at `probe-output.txt`, `probe2-output.txt`,
    `probe3-output.txt`). Investigative; not committed.
  - **Out of scope:** `DacServices.Deploy` / `Extract`
    (testcontainers deferred per chapter-3 axis 6). Only offline
    `BuildPackage` + `TSqlModel` parse path probed. Pre/post-deployment
    scripts not probed (subagent #4 confirmed `BuildPackage` does not
    expose them; chapter-3 axis Q5 already resolved out-of-scope).
  - **Schema-version pinning:** all probes used
    `SqlServerVersion.Sql160` (matches the slice-1 fixture). DacFx
    exposes 13 versions; the enum is named at §Q-extra.

## Q1 — Minimum viable DACPAC

`BuildPackage(stream, emptyTSqlModel, metadata)` **succeeds**. No
exception; produces a 1742-byte zip containing four entries:

```
[Content_Types].xml   175 bytes
DacMetadata.xml       218 bytes
model.xml             938 bytes
Origin.xml           1092 bytes
```

The empty `model.xml` carries `<DataSchemaModel
FileFormatVersion="1.2" SchemaVersion="2.4"
DspName="Microsoft.Data.Tools.Schema.Sql.Sql160DatabaseSchemaProvider"
…>` with a single `<Element Type="SqlDatabaseOptions">` child holding
ten default property settings (Collation, IsAnsiNullsOn, etc.) — no
user objects. Reloading the empty bytes via
`new TSqlModel(path, DacSchemaModelStorageType.Memory)` succeeds
and `model.GetObjects(DacQueryScopes.UserDefined,
ModelSchema.Table)` returns an empty enumeration.

**Implication for the read-side adapter.** The empty-Catalog edge
case is well-defined: an empty DACPAC produces zero `Table`s on
enumeration; `parseModel` (CatalogReader.fs:266–290) currently maps
this to `Catalog { Modules = [{ Pipeline placeholder; Kinds = [] }] }`
— a single-module catalog with no kinds. That shape is consistent
with chapter-3 axis 7's slice-1 default; no adapter change needed
for the empty edge.

**Source.** /tmp/dacfx-probe/probe-output.txt §[1] and §Q1b in
probe3-output.txt.

## Q2 — DACPAC zip structure

Populated DACPAC (one User table) at 2073 bytes carries the same
**four entries** as the empty case — never any more in vanilla
`BuildPackage` output:

| Entry | Role | Deterministic? |
|---|---|---|
| `model.xml` | The schema model: every Table/Column/PrimaryKeyConstraint as `<Element>` nodes with `<Property>`/`<Relationship>` children. Schema namespace `http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02`. | **Yes** — byte-identical across runs (probe3-output.txt §"hashing model.xml"). |
| `Origin.xml` | DacFx package origin/provenance. Embeds GUID, wall-clock Start/End, and a SHA-256 checksum of `model.xml`. | **No** — varies per run (Q8). |
| `DacMetadata.xml` | The `PackageMetadata` (Name, Version, Description). Wrapped in `<DacType xmlns="…/2012/02">`. | **Yes** when metadata fields are stable across runs. |
| `[Content_Types].xml` | OPC packaging metadata (`<Default Extension="xml" ContentType="text/xml" />`). | **Yes** — fixed string. |

**No `refactor.xml`, no `customdata.xml`, no `predeploy.sql`,
no `postdeploy.sql`** in vanilla `BuildPackage` output. These
entries are produced only by SSDT's full pipeline (project-build
mode) or by post-hoc `System.IO.Packaging` surgery. Confirmed
empirically (probe-output.txt §[2]: probed with explicit lookups
for these names; all reported missing).

**model.xml shape.** Each user object becomes an `<Element
Type="Sql<Kind>" Name="[schema].[name]">…</Element>`. For the User
fixture: one `SqlPrimaryKeyConstraint`, one `SqlTable`, one
`SqlSimpleColumn` per column, plus a `SqlDatabaseOptions`. Names
inside the XML are the **bracket-quoted multi-part identifiers**
(`[dbo].[User]`, `[dbo].[User].[Id]`); resolution is via
`<References Name="…" />` cross-references inside `<Relationship>`
nodes.

**Implication for byte-canonicalization (axis 4).** Of the four
entries, **three are deterministic and one is not** (Origin.xml).
The non-determinism is well-localized: a future canonicalizer
needs only to rewrite Origin.xml (replace the `<Identity>` GUID,
the `<Start>` and `<End>` timestamps with pinned values; the
`<Checksum>` of model.xml is already deterministic since model.xml
is). Zip-entry timestamps appear to already be pinned (Q8 below
shows them identical across runs); the canonicalizer's surface is
narrower than subagent #4 anticipated.

## Q3 — `AddObjects` validation behavior

Three failure modes empirically observed:

  1. **Syntactically invalid script** (e.g., `CREATE TABLE bad ((`)
     → `AddObjects` throws **`Microsoft.SqlServer.Dac.Model.DacModelException`**
     synchronously. Message format: `"Add or update objects failed
     due to the following errors: \nError SQL46010: Incorrect syntax
     near '('.\n"`. The error-code prefix is `SQL` + 5-digit;
     `SQL46010` is the parser-level code.
  2. **Referentially-broken DDL** (e.g., FK to a not-yet-added
     table) → `AddObjects` **succeeds silently**. The model is
     internally inconsistent but loaded. The error surfaces in
     either of:
       - `model.Validate()` returns a non-empty
         `IList<DacModelMessage>` with `MessageType = Error`,
         `Number = 71501`. Two messages per missing reference (one
         for the column-level reference, one for the object-level).
       - `BuildPackage(...)` throws
         **`Microsoft.SqlServer.Dac.DacServicesException`**: `"Cannot
         save package to file. The model has build blocking errors:
         Error SQL71501: …"`. **`BuildPackage` short-circuits on
         build-blocking errors before writing the zip.**
  3. **`AddObjects(null)`** → throws plain
     **`System.ArgumentNullException`** with parameter name
     `inputScript`.

Additional observations:

  - **One statement per `AddObjects` call.** Multiple statements in
    one string (separated by `;`) raise
    `Error SQL71006: Only one statement is allowed per batch. A
    batch separator, such as 'GO', might be required between
    statements.` Fix: call `AddObjects` once per statement.
    Surfaced unintentionally during Q4's first attempt
    (probe-output.txt §[4]).
  - **`ALTER TABLE ... ADD <column>` is rejected** at AddObjects
    time: `Error SQL70645: In this context, you must specify columns
    by using a CREATE TABLE statement instead of by using an ALTER
    TABLE statement.` `ALTER TABLE ... ADD CONSTRAINT` is accepted
    (Q4). DacFx's model is a static snapshot, not a migration log;
    column-add via ALTER is not supported in script-build mode.
  - **Order-independence within a model.** Adding a child table
    with an FK before its parent table makes `model.Validate()`
    report two unresolved-reference errors; adding the parent
    afterward and re-calling `Validate()` returns zero errors.
    `BuildPackage` then succeeds (probe3-output.txt §"order-
    dependence"). Useful for the emitter: emit in any order; FK
    references resolve once both tables are present.

**Exception taxonomy for `parseDacpac` failure-arm shape.** Both
`DacModelException` and `DacServicesException` are concrete subclasses
of `System.Exception` from
`Microsoft.SqlServer.Dac.dll`/`Microsoft.SqlServer.Dac.Extensions.dll`.
The existing CatalogReader uses a broad `try/with ex -> ...`
(CatalogReader.fs:90–92); the existing reader does NOT differentiate
by exception type. Slice 5+'s parser tests on broken-input fixtures
will hit `DacServicesException` ("Could not load schema model from
package.") for any non-DACPAC input — see Q-extra below.

## Q4 — Cyclic FK dependencies (A → B → A)

**Cycle is accepted at every stage.** When the four scripts (CREATE
A; CREATE B; ALTER A ADD FK→B; ALTER B ADD FK→A) are added one per
`AddObjects` call:

  - `AddObjects` accepts each call.
  - `model.Validate()` returns **0 messages** — the cycle is not
    flagged as an error or warning.
  - `BuildPackage` succeeds (2128 bytes).
  - Reloading and enumerating yields both FKs with correct
    `foreignTable` references (`FK_A_B → dbo.B`, `FK_B_A → dbo.A`).

Self-referential FK (`Node` → `Node`) also accepted at every stage
(probe2-output.txt §Q4b).

**Implication for chapter-1 cycle-resolution work.** The canary loop
accommodates cycles cleanly at the DacFx level. V2's
`CycleResolution` strategy operates **above** DacFx — it determines
which FKs are nullable / which are cycle-breakers as a
catalog-level decision. DacFx itself is cycle-tolerant; the round-
trip closes.

**Caveat (not probed).** `DacServices.Deploy` to a real SQL Server
will require a deployment order; cycles may complicate that. This
is outside chapter-3's first six slices (testcontainers deferred to
slice 7+).

## Q5 — Cross-schema references

Multi-schema models with cross-schema FKs **work cleanly**.
Probed: `[Sales].[Order]` with FK to `[Inventory].[Item]`.

  - `CREATE SCHEMA` statements are accepted via `AddObjects` (one
    per call).
  - `model.Validate()` reports zero messages.
  - `BuildPackage` succeeds.
  - On reload, `Table.Name.Parts` is `[schema; table]` (count = 2).
  - The FK's `ForeignKeyConstraint.ForeignTable` reference resolves
    to a TSqlObject whose `Name.Parts` is `[Inventory; Item]` —
    full cross-schema name preserved.

**Implication for axis 7 (Module → Schema).** The DacFx
representation does **not** carry any "module" namespace. Schema is
the only grouping axis at the DacFx surface. The read-side
adapter's `SliceOnePlaceholderModule = "Pipeline"`
(CatalogReader.fs:54) cannot be derived from DacFx state; for
multi-Module fixtures the adapter must either (a) treat each schema
as a separate Module, (b) read Module identity from
`PackageMetadata.Name` or `Description`, or (c) embed Module in
extended properties. The pre-scope's option (b) — `Schema` from
`Kind.Physical.Schema`, Module from package metadata — is consistent
with Q5's evidence and remains the recommended slice-4 default.

**Curiosity finding: FK `Name.Parts` carries the host schema, not
the FK's own schema.** Probed FK name is `Sales.FK_Order_Item`
(host table is `Sales.Order`). The FK is logically owned by the
host table's schema. This matters for the OSSYS synthesis-convention:
the read-side adapter's `OS_REF_<srcModule>_<srcEntity>_<viaAttr>`
naming is rooted on the **source** (host) entity, not on the
referenced entity — the DacFx representation makes that derivation
straightforward.

## Q6 — `PackageMetadata` constraints

Probed 13 cases (Q6 in probe-output.txt §[6]). **`BuildPackage` is
extraordinarily permissive on `PackageMetadata` fields:**

| Field | Probe input | BuildPackage outcome | Reload `pkg.<field>` value |
|---|---|---|---|
| `Name = null` | OK | empty string |
| `Name = ""` | OK | empty string |
| `Name = "   "` (whitespace) | OK | empty string (trimmed?) |
| `Name = "weird/name with:colons & <xml>"` | OK | preserved verbatim |
| `Name` = 5000-char string | OK | preserved verbatim (5000 chars round-tripped) |
| `Version = null` | OK | empty string |
| `Version = ""` | OK | empty string |
| `Version = "not-a-version"` | OK | empty string (silently dropped — invalid `System.Version`) |
| `Version = "1.2.3.4"` | OK | preserved |
| `Version = "1.0"` | OK | preserved |
| `Description = null` | OK | null |
| `Description` = 10000-char string | OK | preserved verbatim |
| default-only (no setters) | OK | empty/empty/null |

**Two material findings:**

  1. **`Version` is parsed as a `System.Version`.** Garbage strings
     ("not-a-version") silently round-trip as empty. To carry
     content-addressed snapshot hashes (per axis Q8 default
     disposition), the emitter must encode them as a valid
     `System.Version` shape (e.g., truncated/numeric segments).
     Otherwise the manifest stores empty.
  2. **No special-character escaping needed for `Name` /
     `Description`.** XML-significant characters (`<`, `&`, `:`,
     `/`) round-trip; DacFx handles encoding internally inside the
     `DacMetadata.xml` entry. Long values (5000+ chars) are not
     truncated.

**Implication for axis Q8 (PackageMetadata defaults).** The pre-
scope's recommendation (`Name = catalog-derived;
Version = derived from snapshot hash; Description = generated stamp`)
needs a Version-shape adapter: snapshot hashes are SHA-256
hex-encoded strings, not `System.Version`. Recommended shape:
encode the hash as four short numeric segments (e.g., the four
DWORDs of the truncated hash, capped to int32) — round-trips as
a valid Version, distinguishes snapshots, and avoids
DacFx's silent-drop on parse failure.

## Q7 — Stream vs file `BuildPackage` variants

**Both succeed; both produce structurally-identical content; bytes
are NOT identical.** Probed at probe2-output.txt §Q7:

```
stream bytes=1999  file bytes=1998  byte-equal=False
  entry model.xml: equal=True, sLen=2308, fLen=2308
  entry DacMetadata.xml: equal=True
  entry Origin.xml: equal=False (different <Identity> GUID per call)
  entry [Content_Types].xml: equal=True
```

The single source of difference is **Origin.xml's `<Identity>` GUID
and `<Start>`/`<End>` timestamps**, regenerated per `BuildPackage`
call. Stream and file overloads each invoke the same internal
write path; they differ only in destination, not in the
provenance values they generate. The 1-byte size difference is
zip-overhead variance from compressing slightly different
Origin.xml content.

**Recommendation.** Stream overload is the natural fit for
DacpacEmitter (`Catalog -> byte[]`). File overload offers no
behavioral advantage — and writing then re-reading a temp file
just to materialize bytes adds I/O the algebra doesn't need.
**The read-side adapter currently uses the file path overload**
(CatalogReader.fs:104–107) because of a documented "DacFx's
stream-based loaders are awkward across version boundaries"
concern; that's about *load*, not *build*. For *build* in the
emitter, stream is the cleaner shape.

## Q8 — Origin.xml structure + non-deterministic fields

Full Origin.xml from a populated build (probe-output.txt §[2]):

```xml
<?xml version="1.0" encoding="utf-8"?>
<DacOrigin xmlns="http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02">
  <PackageProperties>
    <Version>3.1.0.0</Version>
    <ContainsExportedData>false</ContainsExportedData>
    <StreamVersions>
      <Version StreamName="Data">2.0.0.0</Version>
      <Version StreamName="DeploymentContributors">1.0.0.0</Version>
    </StreamVersions>
  </PackageProperties>
  <Operation>
    <Identity>1517abc5-abb5-4035-bebe-2187f2375424</Identity>     <!-- NON-DETERMINISTIC: per-call GUID -->
    <Start>2026-05-08T01:44:44.3680995+00:00</Start>              <!-- NON-DETERMINISTIC: wall-clock -->
    <End>2026-05-08T01:44:44.3681405+00:00</End>                  <!-- NON-DETERMINISTIC: wall-clock -->
    <ProductName>Microsoft.SqlServer.Dac.Extensions, Version=162.0.0.0, …</ProductName>
    <ProductVersion>162.5.57.1</ProductVersion>                   <!-- DacFx version-pinned -->
    <ProductSchema>http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02</ProductSchema>
  </Operation>
  <Checksums>
    <Checksum Uri="/model.xml">7092D8EDB6C0382356EFBEFBA3D41F294DB77A29E234283212626261F1926852</Checksum>
  </Checksums>
  <ModelSchemaVersion>2.4</ModelSchemaVersion>
</DacOrigin>
```

**Three non-deterministic fields, no others:**

  1. `<Operation>/<Identity>` — fresh `Guid.NewGuid()` per
     BuildPackage call.
  2. `<Operation>/<Start>` — `DateTimeOffset.UtcNow` at start of
     BuildPackage.
  3. `<Operation>/<End>` — `DateTimeOffset.UtcNow` at end of
     BuildPackage.

**Other Origin.xml fields are all deterministic** (PackageProperties
versions, ProductName/Version/Schema strings, ModelSchemaVersion).
The `<Checksum Uri="/model.xml">` is a SHA-256 hex digest of
`model.xml` — since `model.xml` itself is byte-deterministic across
identical models (probe3-output.txt §"hashing model.xml"), the
checksum is too. Subagent #4's pre-scope phrasing ("the model.xml
checksum derives from [timestamps]") was imprecise: the checksum
derives from `model.xml`'s bytes, not from timestamps. Origin.xml
embeds **timestamps** and a **separate model.xml checksum**;
neither depends on the other.

**Zip-entry timestamps (subagent #4 risk #1).** Probed across two
runs 1.5 seconds apart (probe-output.txt §[8]):

```
model.xml:        r1=2026-05-08T01:44:46.0+00:00  r2=2026-05-08T01:44:46.0+00:00
DacMetadata.xml:  r1=2026-05-08T01:44:46.0+00:00  r2=2026-05-08T01:44:46.0+00:00
Origin.xml:       r1=2026-05-08T01:44:46.0+00:00  r2=2026-05-08T01:44:46.0+00:00
[Content_Types]:  r1=2026-05-08T01:44:46.0+00:00  r2=2026-05-08T01:44:46.0+00:00
```

All four entries carry the **same zip-entry LastWriteTime within a
single run**, but **the timestamp does advance between runs** —
just not at a resolution that surfaces in probes 1.5 seconds apart
(zip MS-DOS time format has 2-second resolution and DacFx
appears to set them to a rounded run-start time). On runs spaced
> 2 seconds apart, the zip-entry timestamps will differ.
**For canonicalization purposes, all four zip-entry timestamps
are write-time wall-clock; rewriting them to a pinned value (e.g.,
2000-01-01) along with rewriting Origin.xml's three fields
yields full byte-determinism.** No build-machine-name or
hostname embedding observed.

**Implication for axis 4.** The byte-canonicalizer's surface is
**six values** total: three Origin.xml fields plus four zip-entry
LastWriteTime values (which all converge to one pinned write
time). No GUIDs in `[Content_Types].xml`, no hostnames in
`DacMetadata.xml`, no embedded build-machine info anywhere. This
is materially smaller than the worst-case anticipated by the pre-
scope and supports the chapter-3 axis-4 default disposition (T1
holds at the loaded form; canonicalization is a separable post-hoc
concern when a byte-stable consumer surfaces).

**Operational confirmation.** `model.xml`, `DacMetadata.xml`, and
`[Content_Types].xml` are byte-identical across runs of the same
input model (probe3-output.txt §"hashing model.xml"). A
canonicalizer rewrites only Origin.xml + zip-entry-timestamps; the
rest is already deterministic.

## Q-extra — Adjacent findings worth carrying forward

  - **`TSqlModel(path, Memory)` failure-mode is uniform.** Empty
    file, garbage bytes, valid zip without `model.xml`, and
    nonexistent path all throw
    `Microsoft.SqlServer.Dac.DacServicesException` with message
    `"Could not load schema model from package."` Same exception
    type, same message — the inner cause is not surfaced through
    the message. Adapters needing fine-grained "is this a missing
    file" vs "is this a corrupt DACPAC" distinction must inspect
    `ex.InnerException` (not probed). For the read-side adapter's
    parse-failure shape, a single "modelLoad" failure code suffices
    (which is what CatalogReader.fs:104–107 already does).
  - **`SqlServerVersion` enum (DacFx 162.5.57):** `Sql90`, `Sql100`,
    `SqlAzure`, `Sql110`, `Sql120`, `Sql130`, `Sql140`, `Sql150`,
    `SqlDw`, `Sql160`, `SqlServerless`, `SqlDwUnified`,
    `SqlDbFabric`. Slice-1 fixture pin (`Sql160`) is current
    SQL Server 2022 / box-product. SQL Server 2025 should appear as
    `Sql170` (or similar) in a future DacFx release; the chapter-3
    pin matches what production deploys to.
  - **`ForeignKeyAction` enum:** `NoAction = 0`, `Cascade = 1`,
    `SetNull = 2`, `SetDefault = 3`. The `GetProperty(...DeleteAction)`
    return value is the int. The read-side adapter's slice-2 FK
    work needs to translate these four values to V2's
    `ReferenceAction` DU; mapping is direct (the pre-scope already
    named `NoAction/Cascade/SetNull` in §2; SetDefault adds a
    fourth).
  - **`model.Validate()` is the right gate** for "is this model
    build-safe?" before calling `BuildPackage`. It reports
    `DacModelMessage` items with `MessageType` (Error/Warning),
    `Number` (e.g., 71501 for unresolved reference), and `Message`
    (human-readable). DacpacEmitter slice 5+ should call
    `Validate()` and surface non-empty errors as adapter Failure
    arms before invoking BuildPackage; otherwise `BuildPackage`
    throws `DacServicesException` and the failure provenance is
    less granular.

## Implications for chapter-3 sequencing

Mapping findings to subagent #4's eight risks (CHAPTER_3_OPEN.md
§"Open questions"):

| # | Risk | Default disposition | Refined by scouting? |
|---|---|---|---|
| 1 | Byte-determinism strategy | T1 holds at loaded form; canonicalization operational | **Refined.** Canonicalizer's surface is exactly Origin.xml's three fields (`<Identity>`, `<Start>`, `<End>`) plus zip-entry LastWriteTime; model.xml + DacMetadata.xml + [Content_Types].xml already byte-deterministic. No hostnames, no extra GUIDs. Slice 7+ post-hoc canonicalizer is six-line work, not zip surgery as the pre-scope feared. |
| 2 | F#/C# wrapper layering | C# wrapper inside `Projection.Pipeline` | **Unrefined by scouting.** Layering is a code-organization decision; DacFx's exception model (`DacModelException` and `DacServicesException` are clean .NET exceptions, not COM/HRESULT) means F# can call into DacFx directly without a C# adapter (the existing CatalogReader does this). The C#-wrapper recommendation should be re-evaluated at slice 5 against the actual layering pressure, not held as a presumption. |
| 3 | Module → Schema mapping | (b) — Schema from `Kind.Physical.Schema`, Module logical-only | **Refined.** Q5 confirms DacFx has no module concept; cross-schema FKs round-trip cleanly with `Name.Parts` carrying schema. The slice-4 multi-module fixture's Module-name source is open: PackageMetadata.Name is the natural channel (Q6 confirms it's permissive on content). |
| 4 | Modality marks | comments / extended properties | **Open.** Not probed; extended-property emission via DacFx is via `AddObjects("EXEC sp_addextendedproperty …")` at the script level, but per-table extended properties surface only via scripts, and the pre-scope's "comments-first" disposition is consistent with chapter-3's slice-1 minimalism. Defer until a downstream consumer demands structured access. |
| 5 | Pre/post-deploy scripts | out-of-scope | **Confirmed.** No predeploy/postdeploy entries in vanilla `BuildPackage` output. StaticSeeds / MigrationDependencies / Bootstrap remain separate emitter responsibilities. |
| 6 | Origin handling | DacpacEmitter blind to Origin | **Confirmed.** No DacFx peer for Origin; the emitter cannot accidentally reach for it. Selection-by-Origin is a pass-time concern. |
| 7 | DacFx version pinning | 162.x — 162.5.57 in fsproj | **Confirmed.** Pin matches; `<ProductVersion>162.5.57.1</ProductVersion>` lands in Origin.xml. The fourth-segment increment ("162.5.57.1" not "162.5.57.0") suggests internal patch numbering invisible to NuGet; canonicalization that wants to be DacFx-version-aware should match on the first three segments. |
| 8 | PackageMetadata choices | Name=catalog-derived; Version=snapshot-hash-derived; Description=stamp | **Refined.** `Version` is parsed as `System.Version` and silently truncates non-conforming strings to empty; the snapshot hash needs a Version-shape encoder (proposed: four DWORDs of the SHA-256 hash, each capped to int32, joined as `a.b.c.d`). `Name` and `Description` are unrestricted strings up to thousands of characters — no escaping concerns. |

**Risks promoted from the pre-scope's eight + new findings from
scouting:**

  - **New finding #9 (one-statement-per-AddObjects).** DacFx's
    `AddObjects(string)` parses one statement per call; multi-
    statement strings raise `SQL71006`. The slice-1 differential
    test passes a multi-line `CREATE TABLE` (one logical
    statement, multiple lines), which works fine; **the
    DacpacEmitter generating multi-table catalogs must call
    `AddObjects` once per statement** (one CREATE TABLE, then one
    ALTER TABLE per FK, etc.), not concatenate. This is a small
    but easy-to-miss shape constraint.
  - **New finding #10 (`ALTER TABLE ADD <column>` rejected).**
    `AddObjects` rejects column-additions via ALTER. The emitter
    must build each table's full column set inline within the
    `CREATE TABLE` statement. RawTextEmitter already does this;
    DacpacEmitter inherits the same discipline. No
    incremental-column emission shape is admissible.
  - **New finding #11 (cycle tolerance).** Cyclic FKs at the DacFx
    level "just work"; no special handling needed in DacpacEmitter
    for cycles. The chapter-1 `CycleResolution` strategy operates
    above this layer (deciding which FKs are nullable cycle-
    breakers as a Catalog-level decision) and is independent of
    DacFx's cycle behavior. **Slice 6's round-trip closure should
    include a cycle fixture** to confirm closure under cycle
    pressure.
  - **New finding #12 (uniform load failure-mode for parser).**
    Every malformed input to `new TSqlModel(path, Memory)`
    surfaces as `DacServicesException("Could not load schema model
    from package.")`. The read-side adapter's outer match cascade
    (per session-26 forward signal: explicit Failure arms for
    previously-untested failure paths) needs a single `modelLoad`
    arm — which CatalogReader.fs:104–107 already provides. No
    additional Failure variants needed for the parse path; the
    granularity of cause (empty / garbage / wrong-zip /
    missing-file) is not surfaced by DacFx itself.

**Open at chapter-3 close (not refined here):**

  - DacFx behavior under `DacServices.Deploy` (slice 7+; testcontainers).
  - `DacExtractOptions` / `DacDeployOptions` defaults for round-trip
    closure (slice 7+).
  - Whether emitting `EXEC sp_addextendedproperty` via `AddObjects`
    for modality marks round-trips through `BuildPackage` →
    `DacPackage.Load` → enumerate. Probably yes; not probed.
  - Multi-DACPAC composition (one-DACPAC-per-Module). Pre-scope
    recommends one-DACPAC-per-Catalog as chapter-open default;
    not contradicted by scouting.

## Sources

  - **Probe code:** `/tmp/dacfx-probe/probe.csproj`,
    `/tmp/dacfx-probe/Program.cs`, `/tmp/dacfx-probe/Program.cs.bak`.
  - **Probe outputs:** `/tmp/dacfx-probe/probe-output.txt` (Q1–Q8
    + multi-call AddObjects); `/tmp/dacfx-probe/probe2-output.txt`
    (cyclic, self-FK, stream-vs-file deeper, version enum, FK
    actions); `/tmp/dacfx-probe/probe3-output.txt` (corrupt-load
    failure modes, order-dependence, content-hash determinism, FK
    action enum).
  - **DacFx assembly:**
    `/root/.nuget/packages/microsoft.sqlserver.dacfx/162.5.57/lib/net8.0/`.
  - **DacFx schema namespace:**
    `http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02`
    (used in both `Origin.xml` and `model.xml`).
  - **Chapter-3 context (do not recap):** `CHAPTER_3_OPEN.md`,
    `CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md`,
    `AXIOMS.md` §"T1 amended again (2026-05-23)" (lines 660–735),
    `src/Projection.Adapters.Dacpac/CatalogReader.fs`,
    `tests/Projection.Tests/DacpacCatalogReaderDifferentialTests.fs`.

## Cleanup note

The probe project at `/tmp/dacfx-probe/` is investigative scratch.
Subsequent agents may discard it; the captured outputs at
`probe*-output.txt` are the load-bearing evidence for this
document's findings.
