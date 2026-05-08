# Chapter 3.1 pre-scope — Read-side adapter + comparator + minimal `Projection.Pipeline`

> Pre-scope document for the chapter-3.1 agent. Treat as the chapter-open input's first draft and refine under empirical pressure once the chapter opens. Companion to `CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md` (chapter 3.3) and `CHAPTER_3_PRESCOPE_SNAPSHOT_ROWSETS.md` (chapter 3.2). Per `VISION.md` §"Chapter 3 plan" and `VISION_REVIEW.md` Appendix F, this slice is the inflection point at which V2 starts verifying V1.

---

## 1. Scope and value timeline

This chapter delivers V2-augmented mode against V1 (`VISION.md` §"Cutover fallback ladder"): every `build-ssdt` PR runs through V2's canary; structural divergences from V1's expected Catalog block the merge. The slice closes when step (b) below is green. No new emitter ships in this chapter. V2 verifies V1 — the whole point.

Value steps from `VISION_REVIEW.md` §F.5:

- **(a) JSON-round-trip canary, today, with shipped code.** `Projection.Adapters.Osm.CatalogReader.parse` + `Projection.Targets.Json.JsonEmitter.emit` round-trips V1's `osm_model.json`. No DB, no read-side, no DacFx. Catches OSSYS-adapter regressions and JSON-projection drift. Wired as a single test or verb; lands in slice 1 as warm-up. In scope for this chapter.
- **(b) Read-side adapter + ephemeral DB canary — the inflection point.** V1 emits SSDT → `Projection.Pipeline` deploys to testcontainers → `ReadSide.readCatalog` extracts `Catalog_observed` → `CatalogEquivalence.equalModulo` compares against `Catalog_expected` (V2-projected from V1's `osm_model.json`). This is the chapter's terminal acceptance. **The slice closes when (b) is green.**
- **(c) DacpacEmitter swap-in (chapter 3.3).** Same pipeline, replace V1's `.sql` directory with V2-emitted DACPAC. Out of scope here; `Projection.Pipeline`'s seam (apply *whatever* SQL the emitter produced) is built so 3.3 swaps the producer without rewriting the verifier.

Each step ships independently. (a) earns its keep within slice 1; (b) is the chapter close.

---

## 2. Architecture

The diagram is `VISION_REVIEW.md` §F.1 verbatim. New components:

| Component | Project | Language | Role |
|---|---|---|---|
| Read-side adapter | `src/Projection.Adapters.Sql.ReadSide/` | F# | `Task<Result<Catalog,_>>` over `INFORMATION_SCHEMA` + `sys.*` |
| Comparator | `src/Projection.Core/Verification/CatalogEquivalence.fs` | F# | pure `Tolerance -> Catalog -> Catalog -> Diff` |
| Pipeline orchestrator | `src/Projection.Pipeline/` | C# | testcontainers boot, apply-each-`.sql`, exit-code semantics |

**Project layout decisions.**

- **`Projection.Adapters.Sql.ReadSide` (new F#).** Sibling to `Projection.Adapters.Sql` and `Projection.Adapters.Osm`. Same posture: returns `Task<Result<Catalog,_>>` (CLAUDE.md "Async/Task in adapters only"). Distinct project rather than another file under `Projection.Adapters.Sql/` because the existing `Projection.Adapters.Sql` consumes V1-shaped JSON (`ProfileSnapshot`, `ProfileStatistics`, `Static`) — it is V1-snapshot-driven, not live-DB-driven. Different package surface (`Microsoft.Data.SqlClient`, not `System.Text.Json`); different consumers (canary, drift). Project references: `Projection.Core` only. NuGet: `Microsoft.Data.SqlClient` (matches V1 trunk's choice, see `tests/Osm.TestSupport/SqlServerFixture.cs:11`).
- **Comparator placement.** Lives at `src/Projection.Core/Verification/CatalogEquivalence.fs` — *inside Core, not in a sibling `Projection.Verification` project.* Justification: pure function of two `Catalog` values + a `Tolerance` record. No I/O. No DU additions outside `Projection.Core`'s axiom surface. Per CLAUDE.md "F#-pure-core / no-I/O-in-Core" the comparator belongs in Core; it's the same posture as `Catalog.tryFindKind` (`Catalog.fs:232`). A separate `Projection.Verification` project would split a primitive across two compilation units for no benefit. Re-open trigger: if the comparator grows a streaming-diff variant with effectful sinks, lift the effectful surface into `Projection.Verification` and keep `equalModulo` pure in Core.
- **`Projection.Pipeline` (new C#).** First C# project in the sidecar; rationale per `DECISIONS 2026-05-09 — Adapter language choice` is the same posture as the (planned) DacFx wrapper: object-instantiation-heavy, foreign-API-I/O, mutable container lifetimes. C# owns testcontainers boot + `SqlConnection` lifetime + `dotnet-script`-style batch apply. Project references: the F# `Projection.Adapters.Sql.ReadSide` and `Projection.Core` for the comparator.
- **Solution wiring.** Three new entries in `Projection.sln` (current solution at `/home/user/outsystems-ddl-exporter/sidecar/projection/Projection.sln`): F# project type GUID `F2A71F9B-...` for `Projection.Adapters.Sql.ReadSide`; C# project type GUID `9A19103F-16F7-4668-BE54-9A1E7A4F7556` (or VS legacy `FAE04EC0-301F-11D3-BF4B-00C04F79EFBC`) for `Projection.Pipeline`. Add to the `src` solution folder (nested-projects section).

Comparator-in-Core honors A18 amended (`AXIOMS.md:546`): the comparator consumes Catalog evidence, never Policy. Tolerance is *configuration*, not Policy — it names what V1 emits-but-V2-doesn't-model, parallel to A14's discriminator-column policy axis but *over the comparator*, not the emitter.

---

## 3. The read-side adapter — concrete F# design

**Public surface.**

```fsharp
namespace Projection.Adapters.Sql.ReadSide

[<RequireQualifiedAccess>]
module CatalogReader =

    type ReadSideError =
        | ConnectionFailed of detail: string
        | QueryFailed      of queryName: string * detail: string
        | TypeMismatch     of column: string * expected: string * actual: string
        | UnmappedDataType of sqlType: string * column: string

    /// Read the deployed schema as a Catalog.
    /// `schemas`: SQL schema names to include (`["dbo"]` is the canary default).
    val readCatalog
        : connStr: string
        -> schemas: string list
        -> System.Threading.Tasks.Task<Result<Projection.Core.Catalog, ReadSideError>>
```

The `Result<_, ReadSideError>` shape is *not* `Projection.Core.Result<_>` (which uses `ValidationError`) — adapter errors carry a typed DU because callers (the C# pipeline) need to discriminate connection-vs-query-vs-mapping failures for exit-code semantics. The `Result` type can be the existing `Projection.Core.Result<_>` if `ReadSideError` is folded into `ValidationError` codes (`adapter.sql.readside.*`). Recommend the latter for symmetry with `CatalogReader.parse` (`Projection.Adapters.Osm/CatalogReader.fs:77`); flag for resolution at slice 1 chapter-open.

**Internal structure — nine queries, one connection.** Open one `SqlConnection`; one transaction (READ COMMITTED SNAPSHOT) for consistency across reads. Each query maps to a typed DTO list; DTOs are translated into Core records at the end. No streaming — full materialization (300 tables × ~20 columns is ~6000 rows; trivial).

| # | Query target | Purpose | Projects to |
|---|---|---|---|
| 1 | `INFORMATION_SCHEMA.TABLES` filter `TABLE_TYPE='BASE TABLE'` and schema | enumerate tables | `Module.Kinds` (one Kind each) |
| 2 | `INFORMATION_SCHEMA.COLUMNS` | column metadata | `Kind.Attributes` |
| 3 | `sys.indexes` join `sys.objects` | index list, `is_unique`, `is_primary_key`, `type_desc` | `Kind.Indexes`, `Attribute.IsPrimaryKey` |
| 4 | `sys.index_columns` join `sys.columns` | index column lists in `key_ordinal` order | `Index.Columns` |
| 5 | `sys.foreign_keys` | FK headers, `delete_referential_action` | `Reference` |
| 6 | `sys.foreign_key_columns` | FK column pairs | resolves source/target attribute SsKeys |
| 7 | `sys.check_constraints` | check-constraint definitions | dropped at boundary (tolerance), but *captured* for diagnostics |
| 8 | `sys.extended_properties` filter `class IN (1,7)` | MS_Description on tables/columns | dropped (tolerance) but captured |
| 9 | `sys.tables` join `sys.change_tracking_tables` (or `cdc.change_tables`) | CDC-enabled flag | reserved for chapter 4.1's `CdcAwareness` config |

T-SQL skeleton for #2 (representative):

```sql
SELECT
    c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME, c.ORDINAL_POSITION,
    c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH, c.NUMERIC_PRECISION,
    c.NUMERIC_SCALE, c.IS_NULLABLE, c.COLUMN_DEFAULT,
    COLUMNPROPERTY(OBJECT_ID(c.TABLE_SCHEMA + '.' + c.TABLE_NAME), c.COLUMN_NAME, 'IsComputed') AS IsComputed
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_SCHEMA IN (SELECT value FROM STRING_SPLIT(@schemas, ','))
ORDER BY c.TABLE_SCHEMA, c.TABLE_NAME, c.ORDINAL_POSITION;
```

DTO shape:

```fsharp
type private ColumnRow = {
    Schema     : string
    Table      : string
    Column     : string
    Ordinal    : int
    DataType   : string
    MaxLength  : int option
    Precision  : int option
    Scale      : int option
    IsNullable : bool
    Default    : string option
    IsComputed : bool
}
```

Reader: `use cmd = conn.CreateCommand()` ; `cmd.CommandText <- ...` ; one `parameter @schemas` (comma-separated); `use rdr = cmd.ExecuteReader()` ; loop. Type-tolerant column reading via the trunk's pattern at `src/Osm.Pipeline/SqlExtraction/ColumnDefinitions.cs:11` — `GetInt32FlexibleOrNull` style helpers, tolerating widening (`smallint -> int`, `tinyint -> int`).

**DTO → Core mapping.**

- Each `(Schema, Table)` row in #1 → `Kind` with `Physical = { Schema; Table }`. Module: derived from a `[Schema] -> ModuleName` policy (default: schema name *is* the module; refine in slice 5 if the operator wants Module-from-extended-property instead).
- Each `Attribute` row → `Attribute` with `Type = mapSqlTypeToPrimitive row.DataType`, `Column = { ColumnName = row.Column; IsNullable = row.IsNullable }`. The type-mapping table mirrors the V2 → SQL table that `RawTextEmitter` already inlines (Integer↔INT, Decimal↔DECIMAL(p,s), Text↔NVARCHAR(MAX)/NVARCHAR(n), …). Failure mode: unmapped SQL types raise `UnmappedDataType` and abort the read — that's a real V1-emission divergence we want surfaced.
- `IsPrimaryKey` is *the union of #3 (index where `is_primary_key`) and the index_columns from #4* — a column is PK iff it appears in an index with `is_primary_key = 1`.
- Indexes: one `Index` per row in #3 (excluding heap-marker rows where `index_id = 0`). `Columns: SsKey list` constructed from #4 in `key_ordinal` order, joined to the column-`SsKey` lookup. `IsUnique = is_unique`. `IsPrimaryKey = is_primary_key`.
- References: one `Reference` per `sys.foreign_keys.object_id`. `OnDelete` mapping: `0=NO_ACTION`, `1=CASCADE`, `2=SET_NULL`, `3=SET_DEFAULT` (map to `Restrict` per RawTextEmitter's existing convention). Source attribute: the column on the constraint's parent. Target kind: the referenced object's `(schema, name)` keyed back to a `Kind.SsKey` via the kind-by-physical lookup.

**Identity — the SsKey synthesis question (FLAG FOR RESOLUTION).** `Identity.fs:16-18` defines `SsKey = Original of string | Derived of original * reason`. Deployed-schema reads do **not** carry OutSystems SS_KEY values — the source of identity is `OBJECT_ID` (per-database integer) plus `COLUMN_ID` for attributes. Per `VISION_REVIEW.md` Appendix H §H.5, the planned future shape adds variants `OssysOriginal of Guid | Synthesized of source*basis | DerivedFrom of parent*reason | V1Mapped of Guid * Guid`. The read-side adapter is a third source of identity — *deployed-schema* — distinct from both OSSYS rowsets (Guid) and JSON-name-synthesis (string). Two options:

1. **Use the existing `Original of string`, prefix-tagged.** `SsKey.original (sprintf "DEPLOYED_KIND_%d_%s_%s" objectId schema table)` and `DEPLOYED_ATTR_%d_%d` for columns. Same shape as `CatalogReader.fs:92-119`'s `OS_KIND_…` synthesis. No type-system change; comparator equality holds *only when both sides synthesize from the same basis* — which they don't (V2-expected uses `OS_KIND_<module>_<entity>`; read-side uses `DEPLOYED_KIND_<oid>_<schema>_<table>`).
2. **Anticipate the H.5 split and add a `DeployedObjectId` variant now.** `SsKey = ... | DeployedObjectId of objectId:int * schema:string * name:string`. Comparator equality is keyed on the *physical coordinate*, not on SsKey itself, in the read-side comparison path — the adapter never produces strings keyable against V2-expected SsKey synthesis. This pushes the H.5 refactor partially forward without committing to the full four-variant DU.

**Recommendation: option (1) for slice 1 (no type change), and the comparator keys on `(Kind.Physical.Schema, Kind.Physical.Table)` not on `SsKey`** for the deployed↔expected matching. The expected-side carries V2-name-synthesized SsKeys; the observed-side carries deployed-coordinate SsKeys; matching by physical coordinate avoids forcing the H.5 split into chapter 3.1. `SsKey`-keyed equality returns at chapter 3.5 once the H.5 refactor lands. Flag for chapter-open: confirm with the operator that "match by physical coordinate" is the right semantics for *deployed-schema* verification (it is — physical is the deploy contract; SsKey is V2's source-side identity).

This decision is the single biggest open question in §3. Resolve at slice 1.

---

## 4. The comparator — concrete F# design

```fsharp
namespace Projection.Core.Verification

open Projection.Core

[<RequireQualifiedAccess>]
module CatalogEquivalence =

    type Tolerance = {
        IgnoreIndexNames         : bool          // index-shape compare: (columns, IsUnique, filter)
        IgnoreCheckConstraints   : bool          // V1 emits CHECKs V2 doesn't model
        IgnoreExtendedProperties : bool          // MS_Description et al.
        IgnoreDefaultNames       : bool          // V1's deterministic DF_<...> vs SQL Server-generated
        AttributeOrderInsensitive: bool          // ordinal in deploy ≠ source order
        IgnoreV1OnlyKinds        : Set<string>   // physical-coordinate exemptions (system tables emitted by V1 templates)
    }

    [<RequireQualifiedAccess>]
    type Side = Expected | Observed

    type AttributeDelta = {
        Attribute : string                       // physical column name (we matched on coord)
        Field     : string                       // "type" | "nullability" | "default" | …
        Expected  : string
        Observed  : string
    }

    type IndexDelta = {
        IndexShape : string                      // textual rendering for diagnostics
        Differs    : string list
    }

    type Divergence =
        | KindMissing       of physical: PhysicalRealization * Side
        | AttributeMismatch of physical: PhysicalRealization * AttributeDelta
        | AttributeMissing  of physical: PhysicalRealization * column: string * Side
        | ReferenceMissing  of fromPhysical: PhysicalRealization * toPhysical: PhysicalRealization * Side
        | ReferenceShape    of fromPhysical: PhysicalRealization * field: string * Expected: string * Observed: string
        | IndexShapeMismatch of physical: PhysicalRealization * IndexDelta
        | UnexpectedExtra   of physical: PhysicalRealization * Side * detail: string

    type Diff = { Divergences: Divergence list }   // empty = pass

    val defaultTolerance : Tolerance
    val equalModulo      : Tolerance -> Catalog -> Catalog -> Diff
```

**Matching scheme.** Per §3 the comparator keys kinds by `(Physical.Schema, Physical.Table)`, attributes by `Column.ColumnName` within a kind, references by `(source physical column → target physical column)`, indexes by `(columns-set, IsUnique)` modulo `IgnoreIndexNames`. Catalog-source SsKeys (which differ trivially across the two sides) are not the matching key.

**Default tolerance profile.** Every named tolerance cites a V1 emission convention:

- **`IgnoreIndexNames = true`.** V1 generates index names with prefixes/suffixes (`IX_<table>_<col>` and `OSIDX_<...>`); see `src/Osm.Emission/Formatting/` index emitters. V2-expected has SsKey-derived names.
- **`IgnoreCheckConstraints = true`.** V1 emits Static-entity discriminators and OutSystems-platform CHECKs that V2 doesn't model in `Catalog`. Trace: `src/Osm.Emission/SsdtEmitter.cs` calls a CHECK template for static entities; V2 has no `CheckConstraint` IR axis.
- **`IgnoreExtendedProperties = true`.** V1 emits `EXEC sp_addextendedproperty 'MS_Description', ...` for column descriptions when an OSSYS attribute carries `Description`. V2 doesn't model description metadata.
- **`IgnoreDefaultNames = true`.** V1 generates `DF_<table>_<col>` deterministic constraint names for column defaults; SQL Server-generated names differ. The *value* of the default is compared; the *name* is not.
- **`AttributeOrderInsensitive = true`.** V1's emission order ≠ deploy order ≠ source order. Order is not load-bearing for SSDT consumers.
- **`IgnoreV1OnlyKinds = Set.empty`.** Operator-tunable for known V1-only emissions (e.g., a `__RefactorLog` table).

The tolerance profile is calibrated empirically by running the canary against a real V1 output and pruning each false-positive class to a named tolerance with a citation (per Appendix F §F.4). This converts V1's quirks into machine-readable record. Slice 5 pays this debt.

**Triangulation comparator (Appendix F §F.6).** The comparator is invoked three times by `Projection.Pipeline`:

```
C_ossys = CatalogReader.parse(osm_model.json) |> passes               // V2-expected from OSSYS
C_v1    = ReadSide.readCatalog(deployedConn)                          // what V1 actually built
C_round = passes(C_v1)                                                // V2 round-trip on observed
```

Three diffs (`equalModulo defaultTolerance`):

- `C_ossys ≡ C_v1` → pass.
- `C_ossys ≢ C_v1` and `C_round ≡ C_v1` → V1 bug (intent diverges from OSSYS).
- `C_ossys ≡ C_round` and `C_ossys ≢ C_v1` → V1 emission bug (formatting / ordering).
- All three disagree → V2 bug (read-side or comparator tolerance), fix before publishing any verdict.

Attribution lives at the `Projection.Pipeline` layer, not in Core — the comparator returns plain `Diff`s; the orchestrator runs three calls and labels them. CLI prints triangulated output, not yes/no.

---

## 5. The `Projection.Pipeline` C# orchestrator

**Minimum scope.** Test-host-only first slice — i.e., a public class library with one entry-point method, consumed by one xUnit integration test that does the boot/apply/read/compare end-to-end. CLI verb (`Projection.Pipeline verify --emitted <outDir> --snapshot <osm_model.json>`) lands at slice 6, not slice 1. Argument: the canary's first consumer is the canary's own integration test; CLI surface is for the Azure DevOps pipeline and lands once the algebra is green.

**Public surface (slice 1).**

```csharp
public sealed class CanaryVerifier
{
    public CanaryVerifier(SqlServerFixture fixture);
    public async Task<VerifyResult> VerifyAsync(
        string emittedSqlDirectory,   // V1's <outDir>/Modules tree
        string osmModelJsonPath,      // V1's --snapshot output
        Tolerance? tolerance = null,
        CancellationToken ct = default);
}

public sealed record VerifyResult(
    Diff OssysVsObserved,
    Diff RoundTripVsObserved,
    Diff OssysVsRoundTrip,
    Attribution Attribution,
    string Report);
```

**Testcontainers wiring.** Lift `tests/Osm.TestSupport/SqlServerFixture.cs` into a sidecar-callable surface. Two options:

1. Add a `ProjectReference` from `Projection.Pipeline` to `tests/Osm.TestSupport/Osm.TestSupport.csproj`. Pro: single source of truth. Con: production-side project depending on a `tests/` project violates layering.
2. Extract a slim `MsSqlEphemeralContainer` class into `Projection.Pipeline.Ephemeral` namespace (in the same C# project). Mirror the `SqlServerFixture` pattern — `IAsyncLifetime`, fresh `dbName` per test instance per `VISION.md` "Tier 2 — container-pooled deploy" — but seed-script-free (the canary supplies its own seed via apply-each-`.sql`).

Recommendation: **option 2.** The fixture pattern is small (~150 lines including DockerAvailability checks); copying preserves layering. Wire to MS SQL 2022-CU15 to match V1's pin (`SqlServerFixture.cs:33`).

**Apply-each-`.sql`-in-topo-order step.** Two choices for topo order:

1. **Re-read from V1's `manifest.json`.** V1 already emits `<outDir>/manifest.json` (`SsdtManifest` shape — see `src/Osm.Emission/SsdtManifest.cs:6`). The `Tables` list is in emission order; combined with `Indexes` and `ForeignKeys` per-entry, the orchestrator can produce a topological apply sequence: tables first (alphabetical or as-listed), then FKs as `ALTER TABLE ... ADD CONSTRAINT` in a second pass.
2. **Re-derive topo from `Catalog_expected`.** V2 already has `TopologicalOrderPass` in `src/Projection.Core/Passes/`. Use it.

Recommendation: **(1) with (2) as fallback.** Reading `manifest.json` is one `JsonSerializer.Deserialize` call; it's the V1-source-of-truth ordering V1's deploy lane consumes today. If `manifest.json` is absent (older V1 output) fall back to (2). Either way, the apply sequence is **two passes**: pass A creates tables (`CREATE TABLE` files in any order; FK references resolve at constraint-application time), pass B applies all FKs/constraints. SQL Server tolerates this for `WITH NOCHECK` semantics V1 already uses. The split prevents FK-target-not-yet-created errors on per-`.sql` apply.

**Connection management.** Per-call. Each `SqlConnection` opens, applies one batch, disposes. SQL Server's connection pool reuses the underlying socket. Pattern matches `SqlServerFixture.ExecuteScriptAsync` (`SqlServerFixture.cs:127`). Read-side gets *its own* dedicated `SqlConnection` for the nine-query session (transaction-scoped). Don't pool across phases — the deploy phase wants short-lived connections; the read phase wants one snapshot-consistent read.

**Exit-code semantics for PR-gating.**

- `0` — all three diffs empty (`C_ossys ≡ C_v1 ≡ C_round`). PR may merge.
- `1` — `C_ossys ≢ C_v1`, attribution = V1-bug. PR blocks; report names the divergent kinds.
- `2` — `C_ossys ≢ C_v1`, attribution = V1-emission-bug (formatting/ordering). PR blocks.
- `3` — all three disagree, attribution = V2-bug (read-side or tolerance). PR blocks; routes to V2 maintainer, not V1.
- `4` — infrastructure failure (Docker unavailable, deploy timeout). Treat as test infra failure (per `SqlServerFactAttribute.cs` skip semantics); doesn't block PR but flags.

---

## 6. Slice-by-slice breakdown

Six slices, ordered for independent green-ability.

**Slice 1 — JSON-round-trip canary.** Goal: verify the existing `CatalogReader.parse` + `JsonEmitter.emit` round-trip against a V1-persisted `osm_model.json`. Test: a single integration-style xUnit `[<Fact>]` reading `tests/Fixtures/profiling/<some>.osm_model.json`, parsing through CatalogReader, emitting through JsonEmitter, comparing structurally to the persisted V1 snapshot if one exists, else to a golden-string baseline. Files: `sidecar/projection/tests/Projection.Tests/JsonRoundTripCanaryTests.fs` (~80 LOC). Acceptance: green; one regression-bait fixture per known projection-lossiness class (the `EspaceKind`-stripped class is known per `SnapshotRowsets` pre-scope §F.5). LOC: ~80.

**Slice 2 — `Projection.Adapters.Sql.ReadSide` skeleton + queries 1–2.** Goal: F# project compiles with `Microsoft.Data.SqlClient`; `readCatalog` runs queries #1 (TABLES) and #2 (COLUMNS); produces a `Catalog` with empty `References`/`Indexes`. Test: `[<DockerFact>]` (gated by `SqlServerFactAttribute` pattern) — boot fixture, apply a 2-table `CREATE TABLE` script, call `readCatalog`, assert two `Kind`s with the expected `Attribute` shapes. Files: `src/Projection.Adapters.Sql.ReadSide/{CatalogReader.fs, Projection.Adapters.Sql.ReadSide.fsproj}` (~250 LOC); `tests/Projection.Tests/ReadSideAdapterSliceTests.fs` (~120 LOC). Acceptance: round-trip on a synthetic 2-table fixture works. LOC: ~370.

**Slice 3 — Read-side queries 3–6 (indexes, FKs).** Goal: read indexes and references; resolve target-kind links via the kind-by-physical lookup; `readCatalog` returns full structural Catalog. Test: property test (FsCheck) generating small synthetic Catalogs (1–5 kinds, 0–10 attributes each, 0–5 FKs, 0–3 indexes), emitting via existing `RawTextEmitter`, deploying, reading back, asserting **physical-coordinate equality** modulo the tolerance defaults of slice 5. Where the emitter is buggy, the test fails — that's a real signal. Files: extend `CatalogReader.fs` (+250 LOC); add `tests/Projection.Tests/ReadSideRoundTripPropertyTests.fs` (~150 LOC). Acceptance: 30+ FsCheck cases pass. LOC: ~400.

**Slice 4 — `CatalogEquivalence.fs` comparator.** Goal: pure comparator with `Tolerance`, `Diff`, `Divergence`, `equalModulo`, `defaultTolerance`. Test: example tests for each `Divergence` shape (one missing kind, one type mismatch, one missing reference, one index-shape mismatch); property test "two structurally-identical Catalogs always diff to empty" (the structural-self-equality property — `equalModulo defaultTolerance c c = { Divergences = [] }`). Files: `src/Projection.Core/Verification/CatalogEquivalence.fs` (~350 LOC); `tests/Projection.Tests/CatalogEquivalenceTests.fs` (~250 LOC). Acceptance: all divergence shapes covered; property test green. LOC: ~600.

**Slice 5 — Tolerance profile calibration against real V1 output.** Goal: run the slice-3 round-trip against a *real* V1 emission (from `tests/Fixtures/sql/model.edge-case.seed.sql` deployed via `SqlServerFixture` — the existing fixture path). Each false positive is converted into either (a) a named tolerance flag with a citation, or (b) a real divergence to fix. Test: a single `[<DockerFact>]` "V1 SSDT seed deploys and round-trips clean under defaultTolerance." Files: extend `CatalogEquivalence.defaultTolerance`; `tests/Projection.Tests/V1SsdtCanaryTests.fs` (~150 LOC). Acceptance: divergence list is empty against the seed. LOC: ~150 plus tolerance entries.

**Slice 6 — `Projection.Pipeline` C# orchestrator + triangulation.** Goal: the C# `CanaryVerifier` class lifts SqlServerFixture, applies-each-`.sql`-in-topo-order from `manifest.json`, runs read-side, runs the comparator three times, returns `VerifyResult` with attribution. Test: an xUnit integration test that drives the full pipeline against the existing seed fixture; asserts `VerifyResult.Attribution = Pass`. CLI verb (`verify --emitted ... --snapshot ...`) deferred to a follow-up but interface and exit codes specified. Files: `src/Projection.Pipeline/{CanaryVerifier.cs, EphemeralSqlServer.cs, ApplyEmittedSql.cs, Attribution.cs, Projection.Pipeline.csproj}` (~600 LOC); `tests/Projection.Pipeline.Tests/CanaryVerifierIntegrationTests.cs` (~200 LOC). Acceptance: full-stack green on the seed fixture; attribution `Pass`. LOC: ~800.

Total chapter LOC estimate: ~2400 (read-side ~600, comparator ~600, pipeline ~800, tests ~400).

---

## 7. Test strategy

| Slice | Tier | Fixture | Trait | Notes |
|---|---|---|---|---|
| 1 | tier-1 (pure) | persisted `osm_model.json` | `[<Fact>]` | runs in pre-commit |
| 2 | tier-2 (container) | `SqlServerFixture`-pattern, fresh `dbName` per test | `[<SqlServerFact>]` | gated by Docker availability |
| 3 | tier-2 (container) + property | same; FsCheck generators bottom-up per `VISION_REVIEW` Appendix E.1 | `[<SqlServerFact>]` + `Property` | ~30 cases; ~150ms/case → ~5s total |
| 4 | tier-1 (pure) | inline `Catalog` builders (per `Fixtures.fs:1`) | `[<Fact>]` + `[<Property>]` | no Docker |
| 5 | tier-2 (container) | `SqlServerFixture` boot of `model.edge-case.seed.sql` | `[<SqlServerFact>]` | one-shot, real V1 output |
| 6 | tier-3 (full integration) | full pipeline | `[<SqlServerFact>]` | nightly + CI |

**JSON round-trip canary placement.** Lives in slice 1 as a `tier-1` test alongside existing `JsonEmitterTests.fs`. *Not* a separate verb; the verb is a future CLI surface. The test is the canary today.

**Property tests.** Slice 3 (read-side round-trip) and slice 4 (comparator self-equality) carry FsCheck. Generators are bottom-up per Appendix E.1: `genAttribute → genKind → genModule → genCatalog`, with structural well-formedness baked in (FK targets exist by construction; no shrinker filtering). Shrinking is outermost-first per Appendix E.2.

---

## 8. Risks and open questions

1. **SsKey synthesis on the read side (§3).** Recommended: physical-coordinate matching, *not* SsKey matching, for the read-side comparator path. Resolve at slice 1 chapter-open. If resolution forces the H.5 four-variant DU split forward, that's a chapter 3.5 dependency now — re-sequence.
2. **Connection ownership.** Recommended: per-call `using` for deploy (one connection per batch), one transaction-scoped connection for the nine-query read-side. Open: does the nine-query read need explicit `READ COMMITTED SNAPSHOT` or is the default `READ COMMITTED` sufficient against a quiescent ephemeral DB? Recommend the former for forward-compat with future drift-detection runs against live DBs.
3. **`InvalidCastException` from type widening.** Adopt the trunk's `GetInt32FlexibleOrNull` pattern (`src/Osm.Pipeline/SqlExtraction/ColumnDefinitions.cs:11`). Open: how do we surface a *real* unmappable type — fail fast (recommended; it's a real divergence) or carry as an `UnmappedDataType` divergence (operator chooses)? Recommend: fail fast in the adapter; the canary surfaces it as `ReadSideError.UnmappedDataType` and the orchestrator translates to exit code 3 (V2-bug).
4. **Ordered-vs-unordered comparison in the comparator.** `Index.Columns` is ordered (per `Catalog.fs:159`'s "in declaration order"); `Reference` is single-column today; `Kind.Attributes` is unordered for matching but ordered for emission. Comparator must be explicit. Recommendation: index columns ordered; attributes matched by `ColumnName`; references matched by source-target column pair. Document in the `Tolerance` doc comment.
5. **DacFx Extract as a parallel reader.** Per Appendix F §F.2, **skip for chapter 3.1.** Re-open trigger: chapter 3.3 (DacpacEmitter) ships; cross-validation between INFORMATION_SCHEMA-read and DacFx-Extract-read is wanted as a triangulation expansion. Then DacFx Extract becomes a fourth Catalog `C_dacfx` and the attribution table grows.
6. **Module → Schema mapping.** The read-side has no Module column — SQL Server schemas are flat. Recommended: derive Module from `Schema` 1:1 for the deployed-side Catalog (every `Kind` lands under a synthetic `Module` named after its schema). The comparator's physical-coordinate matching ignores Module identity — only `(Schema, Table)` matter. Module mismatch is therefore *expected* at the read-side ingress and silently absorbed.
7. **Tolerance bootstrap.** Slice 5 may surface enough false positives that `defaultTolerance` grows too fast. Mitigation: each tolerance flag carries a one-line citation comment to the V1 emission convention; reviewing the list at chapter close becomes a chapter-close ritual entry.

---

## 9. Dependencies

**What 3.1 requires from existing code:**

- `Projection.Core` IR (Catalog, Identity, Module, Kind, Attribute, Reference, Index, ColumnRealization, ReferenceAction, PrimitiveType). All shipped. `Catalog.fs:1-243`, `Identity.fs:1-61`.
- `Projection.Adapters.Osm.CatalogReader.parse` (`CatalogReader.fs:694-705`). Shipped. The expected-Catalog producer.
- `Projection.Targets.Json.JsonEmitter.emit`. Shipped. The slice-1 round-trip target.
- `tests/Osm.TestSupport/SqlServerFixture.cs` pattern. Shipped at trunk. Lifted into `Projection.Pipeline.Ephemeral`.
- `Microsoft.Data.SqlClient` NuGet (already in trunk solution). Add as `<PackageReference>` on the new F# project.
- `src/Osm.Emission/SsdtManifest.cs:6` — manifest.json shape consumed by `Projection.Pipeline` for topo order. Shipped.

**Seams unclear (cite line numbers):**

- The exact V1 manifest.json topo guarantees: `BuildSsdtPipeline.cs:108` writes `manifest.json` but it's unclear whether `Tables` order is FK-respecting. Verify at slice 6 chapter-open. If not FK-respecting, fall back to the two-pass apply (tables first, FKs second).
- `SqlServerFixture.cs:67-104` boots the *seed* — for `Projection.Pipeline` we want a *bare* container (no seed); the lifted `EphemeralSqlServer` removes the seed-application block.

**What blocks chapter 3.2/3.3 if 3.1 doesn't ship:**

- 3.2 (`SnapshotRowsets`): independent — does not depend on the read-side. Can ship in parallel.
- 3.3 (DacpacEmitter): the canary is the dacpac's first consumer; `Projection.Pipeline` provides `apply-each-.sql` (or equivalent for dacpac via `DacServices.Deploy`). Without 3.1's pipeline shell, 3.3 has nowhere to verify. **3.1 is a hard prerequisite for 3.3.**
- 3.4 (canary closure): is essentially the property-test surface on top of 3.1 + 3.3. Hard prerequisite.
- 3.5 (RefactorLogEmitter): independent of 3.1 *if* the H.5 SsKey refactor doesn't get pulled in by §3's open question. If it does, 3.5 becomes a hard prerequisite.

---

## 10. Files inventory

**New F# files.**
- `sidecar/projection/src/Projection.Adapters.Sql.ReadSide/Projection.Adapters.Sql.ReadSide.fsproj` (~25 LOC). Project file referencing Core + `Microsoft.Data.SqlClient`.
- `sidecar/projection/src/Projection.Adapters.Sql.ReadSide/CatalogReader.fs` (~500 LOC). Nine queries, DTOs, mapping, `readCatalog` public surface.
- `sidecar/projection/src/Projection.Core/Verification/CatalogEquivalence.fs` (~350 LOC). Pure comparator with `Tolerance`, `Divergence`, `Diff`, `equalModulo`, `defaultTolerance`.

**New C# files.**
- `sidecar/projection/src/Projection.Pipeline/Projection.Pipeline.csproj` (~25 LOC). NuGet refs (`Microsoft.Data.SqlClient`, `DotNet.Testcontainers`); project refs (`Projection.Core`, `Projection.Adapters.Sql.ReadSide`, `Projection.Adapters.Osm`).
- `sidecar/projection/src/Projection.Pipeline/EphemeralSqlServer.cs` (~150 LOC). Lifted from `SqlServerFixture` minus seed.
- `sidecar/projection/src/Projection.Pipeline/ApplyEmittedSql.cs` (~150 LOC). Two-pass apply (tables, then FKs) reading `manifest.json`.
- `sidecar/projection/src/Projection.Pipeline/CanaryVerifier.cs` (~250 LOC). Triangulation orchestrator, `VerifyResult`, exit-code semantics.
- `sidecar/projection/src/Projection.Pipeline/Attribution.cs` (~50 LOC). `Pass | V1Bug | V1EmissionBug | V2Bug | InfraFailure` DU + attribution rule.

**New test files.**
- `sidecar/projection/tests/Projection.Tests/JsonRoundTripCanaryTests.fs` (~80 LOC). Slice 1.
- `sidecar/projection/tests/Projection.Tests/ReadSideAdapterSliceTests.fs` (~120 LOC). Slice 2.
- `sidecar/projection/tests/Projection.Tests/ReadSideRoundTripPropertyTests.fs` (~150 LOC). Slice 3 FsCheck.
- `sidecar/projection/tests/Projection.Tests/CatalogEquivalenceTests.fs` (~250 LOC). Slice 4.
- `sidecar/projection/tests/Projection.Tests/V1SsdtCanaryTests.fs` (~150 LOC). Slice 5.
- `sidecar/projection/tests/Projection.Pipeline.Tests/Projection.Pipeline.Tests.csproj` (~25 LOC) and `CanaryVerifierIntegrationTests.cs` (~200 LOC). Slice 6.

**Modified files.**
- `sidecar/projection/Projection.sln` (+ ~25 LOC). Three project entries (`Projection.Adapters.Sql.ReadSide`, `Projection.Pipeline`, `Projection.Pipeline.Tests`); per-project configuration platform sections.
- `sidecar/projection/tests/Projection.Tests/Projection.Tests.fsproj`. New `<Compile Include="..."/>` entries for the five new F# test files; `<ProjectReference>` for `Projection.Adapters.Sql.ReadSide`.

**Cumulative LOC: ~2475 production + ~950 tests ≈ 3.4 KLOC.** Within a single chapter's working set per `CHAPTER_2_CLOSE.md` empirical baseline.

---

### Critical Files for Implementation
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Core/Catalog.fs`
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Core/Identity.fs`
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Adapters.Osm/CatalogReader.fs`
- `/home/user/outsystems-ddl-exporter/tests/Osm.TestSupport/SqlServerFixture.cs`
- `/home/user/outsystems-ddl-exporter/src/Osm.Emission/SsdtManifest.cs`
