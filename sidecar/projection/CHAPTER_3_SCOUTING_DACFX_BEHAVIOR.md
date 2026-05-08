# Chapter 3 scouting — DacFx `BuildPackage` empirical behavior

**Scouting subagent** dispatched at session 27 (chapter-3 working
surface). Companion to `CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md`
(subagent #4). The pre-scope named the API surface and eight risks;
this scouting refines those risks under empirical pressure **before**
slice 5 (DacpacEmitter) opens, by running concrete probes against
`Microsoft.SqlServer.DacFx` v162.5.57 (pinned in
`src/Projection.Adapters.Dacpac/Projection.Adapters.Dacpac.fsproj`).

Cite this document from slice 5+ planning when committing to a
DacpacEmitter shape; re-probe if DacFx is bumped.

## Bounds of investigation

  - **Version:** DacFx 162.5.57 on .NET 9 (SDK 9.0.305), Linux. The
    `lib/net8.0` assemblies load on net9 without trouble.
  - **Probe locus:** `/tmp/dacfx-probe/` (csproj + Program.cs +
    captured outputs `probe-output.txt`, `probe2-output.txt`,
    `probe3-output.txt`). Investigative; not committed.
  - **Out of scope:** `DacServices.Deploy` / `Extract` (testcontainers
    deferred per chapter-3 axis 6). Only the offline `BuildPackage` +
    `TSqlModel` parse path probed. All probes used
    `SqlServerVersion.Sql160` (matches the slice-1 fixture).

## Q1 — Minimum viable DACPAC

`BuildPackage(stream, emptyTSqlModel, metadata)` **succeeds**. No
exception; produces a 1742-byte zip with four entries:

```
[Content_Types].xml   175 bytes
DacMetadata.xml       218 bytes
model.xml             938 bytes
Origin.xml           1092 bytes
```

The empty `model.xml` carries `<DataSchemaModel
FileFormatVersion="1.2" SchemaVersion="2.4" DspName=
"Microsoft.Data.Tools.Schema.Sql.Sql160DatabaseSchemaProvider" …>`
with a single `<Element Type="SqlDatabaseOptions">` child of ten
default properties — no user objects. Reloading via
`new TSqlModel(path, Memory)` succeeds; `model.GetObjects(
DacQueryScopes.UserDefined, ModelSchema.Table)` returns empty.

**Implication.** The empty-Catalog edge case is well-defined:
empty DACPAC → zero `Table`s on enumeration; `parseModel`
(CatalogReader.fs:266–290) maps this to a single-module Catalog
with empty `Kinds`. No adapter change for the empty edge.

Source: probe-output.txt §[1]; probe3-output.txt §Q1b.

## Q2 — DACPAC zip structure

Populated DACPAC (one User table) at 2073 bytes carries the same
**four entries** as the empty case — never any more in vanilla
`BuildPackage` output:

| Entry | Role | Deterministic? |
|---|---|---|
| `model.xml` | Schema model: every Table/Column/PrimaryKeyConstraint as `<Element>` nodes with `<Property>`/`<Relationship>` children. Schema namespace `…/sqlserver/dac/Serialization/2012/02`. | **Yes** — byte-identical across runs. |
| `Origin.xml` | Provenance. Embeds GUID, Start/End timestamps, SHA-256 checksum of `model.xml`. | **No** (Q8). |
| `DacMetadata.xml` | `PackageMetadata` (Name, Version, Description) wrapped in `<DacType xmlns=…>`. | **Yes** when metadata stable. |
| `[Content_Types].xml` | OPC packaging boilerplate. | **Yes** — fixed string. |

**No `refactor.xml`, no `customdata.xml`, no `predeploy.sql`,
no `postdeploy.sql`** in vanilla `BuildPackage` output. Those entries
exist only in SSDT full-pipeline builds or via post-hoc
`System.IO.Packaging` surgery. Confirmed empirically — explicit
lookups for those names all reported missing.

**model.xml shape.** Each user object becomes
`<Element Type="Sql<Kind>" Name="[schema].[name]">…</Element>`. For
the User fixture: one `SqlPrimaryKeyConstraint`, one `SqlTable`, one
`SqlSimpleColumn` per column, plus the `SqlDatabaseOptions`. Names
are bracket-quoted multi-part identifiers (`[dbo].[User]`,
`[dbo].[User].[Id]`); resolution is via `<References Name="…" />`
inside `<Relationship>` nodes.

**Implication for byte-canonicalization (axis 4).** Three of four
entries are deterministic; only Origin.xml is not. The non-
determinism is well-localized: a future canonicalizer needs only to
rewrite Origin.xml's `<Identity>` GUID and `<Start>`/`<End>`
timestamps with pinned values (the model.xml `<Checksum>` is
already deterministic since model.xml is). The canonicalizer's
surface is materially smaller than the pre-scope feared.

Source: probe-output.txt §[2]; probe3-output.txt §"hashing model.xml".

## Q3 — `AddObjects` validation behavior

Three failure modes empirically observed:

  1. **Syntactically invalid script** (e.g., `CREATE TABLE bad ((`)
     → `AddObjects` throws `Microsoft.SqlServer.Dac.Model.DacModelException`
     synchronously: `"Add or update objects failed due to the
     following errors: Error SQL46010: Incorrect syntax near '('."`
     Error codes are `SQL` + 5-digit; `SQL46010` is parser-level.
  2. **Referentially-broken DDL** (e.g., FK to a not-yet-added table)
     → `AddObjects` **succeeds silently**. The error surfaces in
     either of:
       - `model.Validate()` returns a non-empty
         `IList<DacModelMessage>` with `MessageType = Error`,
         `Number = 71501`. Two messages per missing reference (one
         column-level, one object-level).
       - `BuildPackage(...)` throws
         `Microsoft.SqlServer.Dac.DacServicesException`: `"Cannot
         save package to file. The model has build blocking errors:
         Error SQL71501: …"`. **`BuildPackage` short-circuits on
         build-blocking errors before writing the zip.**
  3. **`AddObjects(null)`** → throws plain
     `System.ArgumentNullException` (parameter `inputScript`).

Additional observations:

  - **One statement per `AddObjects` call.** Multi-statement strings
    raise `SQL71006: Only one statement is allowed per batch`. Fix:
    one call per statement.
  - **`ALTER TABLE ... ADD <column>` is rejected** at AddObjects time
    (`SQL70645`). `ALTER TABLE ... ADD CONSTRAINT` is accepted.
    DacFx's model is a static snapshot, not a migration log; column-
    additions must happen inline within `CREATE TABLE`.
  - **Order-independence within a model.** Adding a child table with
    an FK before its parent makes `Validate()` report two unresolved
    references; adding the parent afterward and re-calling
    `Validate()` returns zero errors. `BuildPackage` then succeeds.

**Exception taxonomy for failure-arm shape.** Both
`DacModelException` and `DacServicesException` are concrete
`System.Exception` subclasses; the existing CatalogReader's broad
`try/with` (CatalogReader.fs:90–92) catches both. Slice-5+'s parser
tests on broken-input fixtures will hit `DacServicesException`
("Could not load schema model from package.") for any non-DACPAC
input — see Q-extra below.

Source: probe-output.txt §[3]; probe2-output.txt §Q4 footnote.

## Q4 — Cyclic FK dependencies (A → B → A)

**Cycle accepted at every stage.** Four scripts (CREATE A; CREATE B;
ALTER A ADD FK→B; ALTER B ADD FK→A), one per `AddObjects` call:

  - `AddObjects` accepts each.
  - `model.Validate()` returns **0 messages** — cycle not flagged.
  - `BuildPackage` succeeds (2128 bytes).
  - Reload + enumerate yields both FKs with correct `foreignTable`
    references (`FK_A_B → dbo.B`, `FK_B_A → dbo.A`).

Self-referential FK (`Node` → `Node`) accepted likewise.

**Implication for chapter-1 cycle-resolution work.** The canary loop
accommodates cycles cleanly at the DacFx level. V2's
`CycleResolution` strategy operates **above** DacFx — deciding
which FKs are nullable cycle-breakers as a Catalog-level decision.
DacFx itself is cycle-tolerant; the round-trip closes.

**Caveat (not probed).** `DacServices.Deploy` to a real SQL Server
will require a deployment order; cycles may complicate that.
Outside chapter-3's first six slices.

Source: probe2-output.txt §Q4, §Q4b.

## Q5 — Cross-schema references

Multi-schema models with cross-schema FKs **work cleanly**. Probed:
`[Sales].[Order]` with FK to `[Inventory].[Item]`.

  - `CREATE SCHEMA` accepted via `AddObjects` (one per call).
  - `Validate()` reports zero messages.
  - `BuildPackage` succeeds.
  - On reload, `Table.Name.Parts` is `[schema; table]` (count = 2).
  - The FK's `ForeignKeyConstraint.ForeignTable` reference resolves
    to a TSqlObject whose `Name.Parts` is `[Inventory; Item]` —
    full cross-schema name preserved.

**Implication for axis 7 (Module → Schema).** DacFx has **no module
concept**; Schema is the only grouping axis. The read-side adapter's
`SliceOnePlaceholderModule = "Pipeline"` (CatalogReader.fs:54)
cannot be derived from DacFx state. For multi-Module fixtures the
adapter must either (a) treat each schema as a separate Module,
(b) read Module identity from `PackageMetadata.Name`/`Description`,
or (c) embed Module in extended properties. The pre-scope's option
— `Schema` from `Kind.Physical.Schema`, Module from package
metadata — is consistent with Q5's evidence.

**Curiosity:** FK `Name.Parts` carries the **host** schema, not the
referenced schema (e.g., `Sales.FK_Order_Item`, host=`Sales.Order`).
This matters for OSSYS synthesis: `OS_REF_<srcModule>_<srcEntity>_
<viaAttr>` is rooted on the source/host entity — the DacFx
representation makes that derivation straightforward.

Source: probe-output.txt §[5].

## Q6 — `PackageMetadata` constraints

Probed 13 cases. **`BuildPackage` is extraordinarily permissive on
`PackageMetadata` fields:**

| Field | Probe input | Outcome | Reload value |
|---|---|---|---|
| `Name = null` / `""` / `"   "` | OK | empty string |
| `Name = "weird/name with:colons & <xml>"` | OK | preserved verbatim |
| `Name` = 5000-char string | OK | preserved verbatim |
| `Version = null` / `""` / `"not-a-version"` | OK | **silently dropped to empty** |
| `Version = "1.2.3.4"` / `"1.0"` | OK | preserved |
| `Description = null` / 10000-char | OK | preserved |
| default-only (no setters) | OK | empty/empty/null |

**Two material findings:**

  1. **`Version` is parsed as `System.Version`.** Garbage strings
     silently round-trip as empty. To carry content-addressed
     snapshot hashes (axis Q8 default), the emitter must encode them
     as a valid `System.Version` (e.g., truncated/numeric segments).
     Otherwise the manifest stores empty.
  2. **No special-character escaping needed.** XML-significant
     characters round-trip; DacFx encodes internally inside
     `DacMetadata.xml`. No truncation up to 10000 chars.

**Implication for axis Q8.** The pre-scope's recommendation
(`Name = catalog-derived; Version = derived from snapshot hash;
Description = generated stamp`) needs a Version-shape adapter:
SHA-256 hex hashes are not `System.Version`. Recommended encoding:
the four DWORDs of the truncated hash, capped to int32, joined as
`a.b.c.d` — round-trips as a valid Version.

Source: probe-output.txt §[6].

## Q7 — Stream vs file `BuildPackage`

**Both succeed; structurally-identical content; bytes are NOT
identical.**

```
stream bytes=1999  file bytes=1998  byte-equal=False
  entry model.xml: equal=True (sLen=2308, fLen=2308)
  entry DacMetadata.xml: equal=True
  entry Origin.xml: equal=False (different <Identity> per call)
  entry [Content_Types].xml: equal=True
```

The single source of difference is **Origin.xml's `<Identity>` GUID
and `<Start>`/`<End>` timestamps**, regenerated per `BuildPackage`
call. Stream and file overloads invoke the same internal write path;
they differ only in destination. The 1-byte size difference is zip-
overhead variance from compressing slightly different Origin.xml.

**Recommendation.** Stream overload is the natural fit for
DacpacEmitter (`Catalog -> byte[]`). File overload offers no
behavioral advantage — and writing then re-reading a temp file just
to materialize bytes adds I/O the algebra doesn't need. (The read-
side adapter currently uses the file *path* overload for *load*
because of stream-loader version-boundary concerns; that doesn't
apply to *build*.)

Source: probe2-output.txt §Q7.

## Q8 — Origin.xml structure + non-deterministic fields

Full Origin.xml from a populated build:

```xml
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
    <Identity>1517abc5-…-2187f2375424</Identity>     <!-- per-call GUID -->
    <Start>2026-05-08T01:44:44.3680995+00:00</Start> <!-- wall-clock -->
    <End>2026-05-08T01:44:44.3681405+00:00</End>     <!-- wall-clock -->
    <ProductName>Microsoft.SqlServer.Dac.Extensions, Version=162.0.0.0, …</ProductName>
    <ProductVersion>162.5.57.1</ProductVersion>      <!-- DacFx-version-pinned -->
    <ProductSchema>http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02</ProductSchema>
  </Operation>
  <Checksums>
    <Checksum Uri="/model.xml">7092D8…1926852</Checksum>  <!-- SHA-256 of model.xml -->
  </Checksums>
  <ModelSchemaVersion>2.4</ModelSchemaVersion>
</DacOrigin>
```

**Three non-deterministic fields, no others:**

  1. `<Operation>/<Identity>` — fresh `Guid.NewGuid()` per call.
  2. `<Operation>/<Start>` — `DateTimeOffset.UtcNow` at start.
  3. `<Operation>/<End>` — `DateTimeOffset.UtcNow` at end.

Other fields are deterministic: PackageProperties versions,
ProductName/Version/Schema, ModelSchemaVersion. The
`<Checksum Uri="/model.xml">` is SHA-256 hex of `model.xml`'s bytes
— since model.xml is itself byte-deterministic across identical
models, the checksum is too. **Subagent #4's pre-scope phrasing
("the model.xml checksum derives from [timestamps]") was imprecise:**
the checksum derives from `model.xml`'s bytes, not from timestamps.
Origin.xml embeds timestamps **and** a separate model.xml checksum;
neither depends on the other.

**Zip-entry timestamps.** Across runs ~1.5 s apart, all four entries
carry the **same LastWriteTime within a run**, but timestamps
advance between runs (zip MS-DOS time format has 2-second
resolution). DacFx sets all entries to a rounded run-start time.
Across well-spaced runs the values differ; rewriting all four to a
pinned epoch (e.g., 2000-01-01) along with rewriting Origin.xml's
three fields yields full byte-determinism. **No build-machine name
or hostname embedding observed anywhere.**

**Implication for axis 4.** The byte-canonicalizer's surface is
**six values total**: three Origin.xml fields plus four zip-entry
LastWriteTime values (which all converge to one pinned write time).
No GUIDs in `[Content_Types].xml`, no hostnames in any entry, no
embedded build-machine info anywhere. Materially smaller than the
pre-scope's worst case. Supports the chapter-3 axis-4 default
disposition (T1 holds at the loaded form; canonicalization is a
separable post-hoc concern).

Source: probe-output.txt §[8]; probe3-output.txt §"hashing model.xml".

## Q-extra — Adjacent findings worth carrying forward

  - **`TSqlModel(path, Memory)` failure-mode is uniform.** Empty
    file, garbage bytes, valid zip without `model.xml`, and
    nonexistent path **all** throw `DacServicesException` with
    message `"Could not load schema model from package."` Same type,
    same message — inner cause not surfaced through the message.
    Adapters needing fine-grained "missing file" vs "corrupt zip"
    must inspect `ex.InnerException` (not probed). For the read-side
    adapter, a single `modelLoad` failure code suffices, which is
    what CatalogReader.fs:104–107 already does.
  - **`SqlServerVersion` enum (DacFx 162.5.57):** `Sql90`, `Sql100`,
    `SqlAzure`, `Sql110`, `Sql120`, `Sql130`, `Sql140`, `Sql150`,
    `SqlDw`, `Sql160`, `SqlServerless`, `SqlDwUnified`,
    `SqlDbFabric`. Slice-1 pin (`Sql160`) = SQL Server 2022.
  - **`ForeignKeyAction` enum:** `NoAction = 0`, `Cascade = 1`,
    `SetNull = 2`, `SetDefault = 3`. The
    `GetProperty(...DeleteAction)` return value is the int. Direct
    mapping to V2's `ReferenceAction` DU; SetDefault adds a fourth
    case beyond the pre-scope's three.
  - **`model.Validate()` is the right gate.** Returns
    `DacModelMessage` items (`MessageType`, `Number`, `Message`).
    DacpacEmitter slice 5+ should call `Validate()` and surface
    non-empty errors as adapter Failure arms before invoking
    `BuildPackage`; otherwise `BuildPackage` throws
    `DacServicesException` with less granular provenance.

Source: probe3-output.txt §"corrupt-load", §"FK action enum".

## Implications for chapter-3 sequencing

Mapping findings to subagent #4's eight risks
(`CHAPTER_3_OPEN.md` §"Open questions"):

| # | Risk | Default disposition | Refined? |
|---|---|---|---|
| 1 | Byte-determinism strategy | T1 at loaded form; canonicalization operational | **Refined.** Canonicalizer's surface is exactly six values (Origin.xml's three fields + four zip-entry LastWriteTime, which converge to one pinned epoch). model.xml + DacMetadata.xml + [Content_Types].xml already byte-deterministic. No hostnames, no extra GUIDs. Slice 7+ post-hoc canonicalizer is small targeted work, not zip surgery as the pre-scope feared. |
| 2 | F#/C# wrapper layering | C# wrapper inside `Projection.Pipeline` | **Unrefined by scouting.** Code-organization decision; DacFx exceptions are clean .NET (not COM/HRESULT) so F# can call DacFx directly (CatalogReader already does). Re-evaluate at slice 5 against actual layering pressure, not as presumption. |
| 3 | Module → Schema mapping | (b) Schema from `Kind.Physical.Schema`, Module logical-only | **Refined.** Q5 confirms DacFx has no module concept; cross-schema FKs round-trip cleanly with `Name.Parts` carrying schema. The slice-4 multi-module fixture's Module-name source is open: `PackageMetadata.Name` is the natural channel (Q6 confirms it's permissive). |
| 4 | Modality marks | comments / extended properties | **Open.** Not probed; per-table extended properties via `EXEC sp_addextendedproperty` script-level. Defer until a downstream consumer demands structured access. |
| 5 | Pre/post-deploy scripts | out-of-scope | **Confirmed.** No predeploy/postdeploy entries in vanilla `BuildPackage` output. |
| 6 | Origin handling | DacpacEmitter blind to Origin | **Confirmed.** No DacFx peer; the emitter cannot accidentally reach for it. |
| 7 | DacFx version pinning | 162.x — 162.5.57 in fsproj | **Confirmed.** Pin matches; `<ProductVersion>162.5.57.1</ProductVersion>` lands in Origin.xml. The fourth-segment increment ("162.5.57.1" not ".0") suggests internal patch numbering invisible to NuGet; DacFx-version-aware canonicalizers should match on first three segments. |
| 8 | PackageMetadata choices | Name=catalog-derived; Version=hash-derived; Description=stamp | **Refined.** `Version` is parsed as `System.Version` and silently truncates non-conforming strings to empty; snapshot-hash encoding needs a Version-shape adapter (proposed: four DWORDs of the SHA-256, joined `a.b.c.d`). `Name` and `Description` are unrestricted strings up to thousands of characters — no escaping concerns. |

**New findings beyond the pre-scope's eight:**

  - **#9 (one-statement-per-`AddObjects`).** Multi-statement strings
    raise `SQL71006`. **DacpacEmitter must call `AddObjects` once per
    statement** (one CREATE TABLE, then one ALTER TABLE per FK,
    etc.), not concatenate. Easy to miss.
  - **#10 (`ALTER TABLE ADD <column>` rejected).** Each table's full
    column set must be declared inline in `CREATE TABLE`.
    RawTextEmitter already does this; DacpacEmitter inherits.
  - **#11 (cycle tolerance).** Cyclic FKs at the DacFx level "just
    work"; no special handling needed in DacpacEmitter.
    `CycleResolution` operates above this layer (Catalog-level
    decisions about which FKs are nullable cycle-breakers).
    **Slice 6's round-trip closure should include a cycle fixture**
    to confirm closure under cycle pressure.
  - **#12 (uniform load failure-mode).** Every malformed input to
    `new TSqlModel(path, Memory)` surfaces as `DacServicesException
    ("Could not load schema model from package.")`. The read-side
    adapter's outer match cascade needs a single `modelLoad` arm —
    which CatalogReader.fs:104–107 already provides. No additional
    Failure variants needed for the parse path.

**Open at chapter-3 close (not refined here):**

  - DacFx behavior under `DacServices.Deploy` (slice 7+).
  - `DacExtractOptions` / `DacDeployOptions` defaults for round-trip
    closure (slice 7+).
  - Whether `EXEC sp_addextendedproperty` round-trips through
    `BuildPackage → DacPackage.Load → enumerate`. Probably yes; not
    probed.
  - Multi-DACPAC composition (one-DACPAC-per-Module). Pre-scope
    recommends one-DACPAC-per-Catalog as default; not contradicted.

## Sources

  - **Probe code:** `/tmp/dacfx-probe/probe.csproj`,
    `/tmp/dacfx-probe/Program.cs` (current = probe3 — corrupt-load,
    order-dependence, content hashing, FK action enum),
    `/tmp/dacfx-probe/Program.cs.bak` (probe1 — Q1–Q8 main matrix).
  - **Probe outputs:** `/tmp/dacfx-probe/probe-output.txt` (Q1–Q8),
    `probe2-output.txt` (cyclic, self-FK, stream-vs-file deeper,
    version enum, FK delete actions),
    `probe3-output.txt` (corrupt loads, order-dependence,
    content-hash determinism, FK action enum).
  - **DacFx assembly:**
    `/root/.nuget/packages/microsoft.sqlserver.dacfx/162.5.57/lib/net8.0/`.
  - **Schema namespace:**
    `http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02`
    (used in both Origin.xml and model.xml).
  - **Chapter-3 context:** `CHAPTER_3_OPEN.md`,
    `CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md`,
    `AXIOMS.md` §"T1 amended again (2026-05-23)" (lines 660–735),
    `src/Projection.Adapters.Dacpac/CatalogReader.fs`,
    `tests/Projection.Tests/DacpacCatalogReaderDifferentialTests.fs`.

The probe project at `/tmp/dacfx-probe/` is investigative scratch.
The captured outputs at `probe*-output.txt` are the load-bearing
evidence for this document's findings.
