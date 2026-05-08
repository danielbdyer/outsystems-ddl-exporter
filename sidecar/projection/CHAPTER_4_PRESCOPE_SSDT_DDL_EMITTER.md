# Chapter 4.1.A pre-scope — `Projection.Targets.SSDT.Ddl.SsdtDdlEmitter`

This document is the implementation-grade pre-scope for chapter 4.1.A: the production-deployment SSDT DDL emitter (sibling Π) that complements the DACPAC fast-iteration emitter (chapter 3.3) and feeds the Azure DevOps integration-test promoted lane. It assumes the reader has read the chapter-3 DACPAC pre-scope at `/home/user/outsystems-ddl-exporter/sidecar/projection/CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md`; this document deliberately does not re-derive what that one already establishes. The CDC-aware data triumvirate (chapter 4.1.B) is a different pre-scope.

## §1 Scope and value

Chapter 4.1.A delivers **the V2 SSDT DDL emitter** — a sibling Π whose output is per-table `.sql` files arranged exactly as V1 currently emits them at `<outDir>/Modules/<Module>/<Schema>.<Table>.sql`, plus a sibling `<outDir>/manifest.json`. The artifact is the production-deployment surface: an SSDT-style directory bundle promoted into the team's existing Azure DevOps pipeline (which already imports V1's output today).

What this slice contributes that the DACPAC half does not:

- **Trustability under integration-test pressure.** The promoted lane runs `DacServices.Deploy` against a real testcontainers SQL Server, not against an in-memory model. Every emitter quirk SSDT/DacFx tolerates *or* rejects shows up here.
- **T11 cross-validation against DACPAC.** With the DACPAC and SSDT DDL emitters both shipping from a single `Catalog`, T11 (sibling-Π commutativity) becomes a real, exercised property: `keys(dacpacEmitter c) ≡ keys(ssdtDdlEmitter c)` and per-kind agreement on column shape. If they disagree, one of them has a bug.
- **The first concrete consumer of the `ArtifactByKind<_>` refactor.** The Appendix-H type refactor (`Emitter<'element> = Catalog -> Result<ArtifactByKind<'element>, EmitError>`) becomes load-bearing here: the SSDT shape *is* a per-kind map (one file per table). The refactor's payoff cashes out in this chapter.
- **The seam V2 verifies V1 across.** Per `VISION_REVIEW.md` Appendix F.3, the canary's input today is V1's emitted directory at `<outDir>/Modules/...`. Once V2 emits the same shape, the chapter-3.1 read-side comparator can be retargeted from "compare V1's deployed schema to V2's expected" to "compare V2's emitted SSDT directory to V1's emitted SSDT directory" — pure-text differential without needing testcontainers for every CI run. A second consumer for the same comparator, validating the refactor.

V1's existing emitter (`SsdtEmitter.EmitAsync` at `src/Osm.Emission/SsdtEmitter.cs:55-122`) is the empirical reference. V2 inherits its directory shape, its file naming, its manifest schema. V2 deliberately diverges only where the algebra forces it (determinism guarantees, refactor-log integration, T11 type-safety) and documents every divergence as a `Tolerance` flag in the `CatalogEquivalence` comparator from chapter 3.1.

## §2 Architecture

**Project placement.** Extend the existing `sidecar/projection/src/Projection.Targets.SSDT/` project rather than create a new `Projection.Targets.SSDT.Ddl/`. Argument: `RawTextEmitter.fs` (the debug oracle) and the future `DacpacEmitter.fs` (chapter 3.3) and `RefactorLogEmitter.fs` (chapter 3.5) all live under `Projection.Targets.SSDT/`. The "SSDT DDL" emitter is a fourth file in that project, sharing the Catalog→T-SQL impedance helpers that `RawTextEmitter` and `DacpacEmitter` will both need (`defaultSqlType`, `renderAction`, `quote`, `originLabel`, `modalityLabel` — all currently in `RawTextEmitter.fs:31-65`). Folding a sibling project would force a circular extraction or duplication. The DACPAC pre-scope §6 already calls for the C# DacFx wrapper to live in a *separate* `Projection.Targets.SSDT.Dacpac` C# project; that boundary is where C# vs. F# is split. F#-side SSDT emitters cohabit. **Decision: extend `Projection.Targets.SSDT/`; new file `SsdtDdlEmitter.fs`.**

**Emitter signature, post-Appendix-H refactor.** The Appendix-H `ArtifactByKind` type refactor is a chapter-3 cross-cutting prerequisite. After it lands, the per-kind shape is:

```fsharp
type SsdtFile = {
    RelativePath : string   // "Modules/Customers/dbo.OSUSR_S1S_CUSTOMER.sql"
    Body         : string
}

module SsdtDdlEmitter =
    let emit : Emitter<SsdtFile> = fun catalog ->
        // per-kind: one SsdtFile = body containing CREATE TABLE + indexes + extended properties + inline FKs
        // Cross-module FKs route to a separate post-deploy slice (see §4)
        ...

module ManifestEmitter =
    let emit : Catalog -> Manifest = ...

module Render =
    let toSsdtDirectory
        : (order: SsKey list)
       -> ArtifactByKind<SsdtFile>
       -> Manifest
       -> Map<RelativePath, string> = ...
```

`Render.toSsdtDirectory` is the composition layer (precedent: `Render.concatSql` in Appendix H §H.4). It produces a `Map<RelativePath, string>` — the in-memory representation of the directory bundle. **The F# core never touches the file system.** A C# host (eventually living next to the DACPAC C# wrapper, or under `Projection.Pipeline/`) consumes the map and writes the files; that side owns directory creation, parallelism, and the UTF-8 BOM-less encoding V1 uses (`UTF8Encoding(false)` at `SsdtEmitter.cs:18`).

A18 amended is honored by signature: `Catalog -> Result<ArtifactByKind<SsdtFile>, EmitError>` carries no Policy. EmissionPolicy (§7) is consumed by a **pass** that produces the catalog the emitter sees, not by the emitter itself.

## §3 Catalog → T-SQL impedance (SSDT shape)

Per V2 IR element, the SSDT DDL emission. The mapping is a refinement, not a rewrite, of `RawTextEmitter.fs`'s helpers — the latter targets a debug-oracle aesthetic (inline comments naming SsKeys), the SSDT DDL aesthetic targets DacFx's tolerance for opinionated layout.

**`Module` → folder grouping + optional schema header.** Per the DACPAC pre-scope §2, `Module` has no SQL peer; it surfaces as the directory name in `<outDir>/Modules/<Module>/`. V1 uses `snapshot.Identity.Module` for the folder name (`TableEmissionPlanner.cs:232`), with `SanitizeModuleNames` turning `My.Module` into `My_Module` (V1 option `IncludePlatformAutoIndexes` lives on the manifest options at `SsdtManifest.cs:25`). V2 inherits this convention. **Schema declaration:** V1 does *not* emit `CREATE SCHEMA` per-module; the schema is implicit from `Kind.Physical.Schema` (typically `dbo`). SSDT projects provide schema declarations through the `.sqlproj` itself, not the per-table files. V2 mirrors V1: no `CREATE SCHEMA` in per-table `.sql` files. If a Catalog ever introduces a non-`dbo` schema, V2 emits a `CREATE SCHEMA IF NOT EXISTS` header in a single file `<outDir>/Schemas/<Schema>.sql` (new convention; one tolerance entry).

**`Kind` → `CREATE TABLE` statement.** One per kind. Schema-qualified name `[Schema].[Table]`. Opinionated formatting per V1's `CreateTableFormatter.FormatCreateTableScript` (`PerTableEmission/CreateTableFormatter.cs:17-100`): one column per line, comma at end of line, primary-key constraint inlined when single-column, separated when composite. V2 reproduces this layout because the chapter-3.1 differential comparator's null-tolerance default is "byte-equal modulo whitespace runs"; matching layout cuts the tolerance set V2 must accept.

**`Attribute` → column line.** Format: `[ColumnName] <SqlType> <NULL|NOT NULL>[ IDENTITY(s, i)][ DEFAULT (...)][, ]`. The SQL type is the same `defaultSqlType` mapping used by `RawTextEmitter.fs:31-41` — the type-correspondence policy is not yet a real Policy (per A18 amended, it can't be — Π consumes evidence, not Policy), so V2's first-slice `defaultSqlType` is inherited from `RawTextEmitter` verbatim. T11 cross-validates: SSDT DDL and DACPAC must produce the same SQL type for the same `PrimitiveType` (a property test).

**Identity columns.** V2's IR currently does not flag identity columns explicitly. V1 carries this through `SmoColumnDefinition` (the SMO model). For chapter 4.1.A, the IR widens by **evidence** (per the IR-grows-under-evidence discipline): when the OSSYS adapter surfaces a column with `IsIdentity=true` flag from rowsets, the V2 `Attribute` record gains a fourth axis. Per the V2 convention this is a slice (§8 slice 7) gated on the SnapshotRowsets adapter (chapter 3.2) surfacing the flag. Pre-3.2, the SSDT DDL emitter emits no `IDENTITY(...)` clause and tolerates that V1 does — an explicit `Tolerance.IgnoreIdentityClause` flag in the comparator.

**Default constraints.** V1 generates default-constraint names deterministically (see `ConstraintNameNormalizer` referenced at `ForeignKeyNameFactory.cs:53` and `IndexNameGenerator.cs:37`). V2's IR does not yet model defaults. **Slice deferred:** when V1 fixtures with defaults surface (likely during the chapter-3.1 differential), the IR widens with `Attribute.Default : DefaultExpression option` and the emitter renders `CONSTRAINT [DF_<Table>_<Column>] DEFAULT (<expr>)` inline. Until then: V1's `Tolerance.IgnoreDefaultClauses` absorbs the gap.

**`Index` → `CREATE [UNIQUE] [NONCLUSTERED] INDEX [name] ON [Schema].[Table] ([cols]) [INCLUDE (...)] [WHERE ...]`.** V1's `IndexNameGenerator` (`src/Osm.Smo/IndexNameGenerator.cs:30-36`) computes the index name as `prefix_PhysicalEntity_columns` where prefix is `UIX` for unique, `IX` for non-unique. V2 inherits this naming scheme. Per `Catalog.fs:160-165`, V2's `Index` carries `SsKey`, `Name`, `Columns: SsKey list`, `IsUnique`, `IsPrimaryKey`. The PK index is *not* re-emitted as a CREATE INDEX (it's inlined in the CREATE TABLE per V1 convention at `CreateTableStatementBuilder.cs:62-99`); the emitter filters `IsPrimaryKey=true` indexes from the index-emission loop, exactly as V1 does at `PerTableWriter.cs:122-124`.

Filtered indexes (`WHERE` clause) and computed-column indexes are not yet in V2's IR. Slice-deferred under the same evidence discipline.

**`PrimaryKey` (composite-aware).** V2 derives the PK from `Kind.Attributes |> List.filter IsPrimaryKey` (helper at `Catalog.fs:213-215`). The constraint name follows V1's convention: `PK_<PhysicalTable>` (V1's `ConstraintNameNormalizer` enforces uniqueness, length-cap at 128). Single-column PK: inline as `[col] <type> NOT NULL CONSTRAINT [PK_<table>] PRIMARY KEY CLUSTERED`. Composite PK: a separate `CONSTRAINT [PK_<table>] PRIMARY KEY CLUSTERED ([col1], [col2])` table-constraint at the end of the CREATE TABLE body. V1 makes exactly this distinction at `CreateTableStatementBuilder.cs:67-98`.

**`Reference` (FK) → `ALTER TABLE` add-constraint.** V2 follows V1's pattern of emitting FKs *inline* in the CREATE TABLE for same-module references and *separately* for cross-module references. Constraint name follows V1's `ForeignKeyNameFactory.CreateEvidenceName` (`src/Osm.Smo/ForeignKeyNameFactory.cs:17-60`): `FK_<OwnerTable>_<RefTable>_<col>`, length-capped at 128 with `_<sha256-12-hex>` suffix when over. The four `ReferenceAction` variants map per V1: `NoAction → "NO ACTION"`, `Cascade → "CASCADE"`, `SetNull → "SET NULL"`, `Restrict → "NO ACTION"` (SQL Server convention; same mapping used by `RawTextEmitter.fs:43-48`).

**Cross-module FK resolution.** This is where V2 meaningfully diverges from V1, and chapter 4.1.A is where the divergence finally matters. V1 carries `ReferencedModule` on the FK and uses the receiving module's effective name (`CreateTableStatementBuilder.cs:158-163`). V2's `Reference.TargetKind : SsKey` is module-agnostic; the emitter resolves to the target kind via `Catalog.tryFindKind` (per `RawTextEmitter.fs:128`) and reads `target.Physical.Schema`/`target.Physical.Table` — which is correct *if* SnapshotRowsets has landed and rule 16 has been wired (chapter 3.2 carries this; see HANDOFF.md). Pre-3.2, V2 may resolve a cross-module FK to a different physical table than V1 (a `Tolerance.CrossModuleFkResolution` divergence the comparator absorbs, with a citation back to chapter-2's rule-16 deferral).

**`CheckConstraint` (not yet in V2 IR).** V1 emits `CHECK` constraints from static-entity discriminators and operator-supplied constraints. V2 IR has no `CheckConstraint` element. Per Appendix F.4 of `VISION_REVIEW.md`, this is `Tolerance.IgnoreCheckConstraints : Set<SsKey>` — the comparator scopes V1-only checks per kind. SSDT DDL emitter emits no `CHECK` constraints in chapter 4.1.A. Slice deferred until a real consumer surfaces (e.g., `Profile` evidence about column distribution that justifies a `CHECK Age >= 0`).

**Extended properties (`MS_Description`).** V1 emits these unconditionally when descriptions are present (`ExtendedPropertyScriptBuilder.cs:47-79`). V2's IR does *not* currently carry descriptions on `Kind`/`Attribute`/`Index`. Per `VISION_REVIEW.md` Appendix F.4 these are listed as a documented divergence (`Tolerance.IgnoreExtendedProperties = true` by default). Chapter 4.1.A defers extended-property emission until SnapshotRowsets surfaces description columns; **slice 8 in §8** lifts the IR and emits them. The V1 SQL form to match is at `ExtendedPropertyScriptBuilder.cs:91-95`:

```sql
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'<desc>',
    @level0type=N'SCHEMA',@level0name=N'<schema>',
    @level1type=N'TABLE',@level1name=N'<table>';
```

V2 emits byte-for-byte the same shape, with V1's escape rule (single-quote → double-single-quote at `ExtendedPropertyScriptBuilder.cs:140`).

**Static populations.** When EmissionPolicy admits inline INSERTs (see §7), the SSDT DDL emitter would route static rows to a **post-deploy script** (V1 convention) or to `StaticSeedsEmitter` (chapter 4.1.B). Chapter 4.1.A's emitter **never emits inline INSERTs**: the SSDT shape's idiomatic place for seed data is a post-deploy script under `<outDir>/Scripts/PostDeploy/`, not the per-table `.sql`. This matches V1's `DynamicEntityInsertGenerator`/`PhasedDynamicEntityInsertGenerator` convention where insert generation is a *separate* emitter family. Routing static populations to `StaticSeedsEmitter` is the chapter-4.1.B sibling Π's job.

## §4 Per-file structure and ordering

V1's per-file structure (`PerTableWriter.GenerateInternal` at `PerTableWriter.cs:87-185`):

1. Optional header comment block (`/* Source: ..., Profile: ..., Decisions: ..., Fingerprint: ... */`) — `BuildHeader` at `PerTableWriter.cs:187-267`.
2. `CREATE TABLE` statement, with inline single-column PK constraint and inline same-module FK constraints.
3. Deferred `NOCHECK` foreign-key `ALTER TABLE` statements (one per `IsNoCheck` FK; `PerTableWriter.cs:110-114`).
4. `CREATE [UNIQUE] [NONCLUSTERED] INDEX` statements, sorted alphabetically by index name (`PerTableWriter.cs:163`).
5. Optional `ALTER INDEX [...] DISABLE` for disabled indexes.
6. Optional extended-property `EXEC sys.sp_addextendedproperty` statements.
7. Optional `CREATE TRIGGER` statements (V1 supports trigger emission at `PerTableWriter.cs:153-157`; out-of-scope for chapter 4.1.A — IR doesn't model triggers).
8. Final newline.

Statements are joined by `StatementBatchFormatter.JoinStatements` (`PerTableEmission/StatementBatchFormatter.cs`) — this introduces a `GO` separator between batches in V1. V2 mirrors. **Critical V1 detail:** `TablePlanWriter.WriteSingleAsync` at `TablePlanWriter.cs:83-87` appends `Environment.NewLine` if not already terminated. This means V1's emission is **OS-dependent** — Windows produces CRLF, Linux/macOS produces LF. For V2's T1 byte-determinism (§6), this is a hard divergence: V2 normalizes to LF unconditionally and documents the divergence as `Tolerance.NewlineNormalization` (the comparator strips line endings before equality).

**Cross-module FKs as post-deploy.** SSDT idiom is to put cross-application/cross-module FKs in a `Scripts/PostDeploy/AddCrossModuleForeignKeys.sql` file rather than inline, because in incremental deploys the target table may not yet exist when its referencer's CREATE TABLE runs. V1's `PerTableWriter` actually keeps cross-module FKs *inline* (relying on SSDT's deploy-order resolution). V2 follows V1 here for parity in the first slice. If integration tests surface a deploy-order failure, V2 splits cross-module FKs into a post-deploy file (slice 8 in §8). The split is *not* a tolerance — it's a divergence the comparator notices structurally (different file count, different content layout); recorded as `Tolerance.PostDeployForeignKeys = false` initially.

**Refactor.log positioning.** SSDT projects expect a single `<projectName>.refactorlog` at the project root. V2's `RefactorLogEmitter` (chapter 3.5; produces an `Emitter<RefactorLogEntry>` per Appendix H §H.6) feeds an XML rename document. The chapter-4.1.A SSDT DDL bundle includes that file alongside the per-table `.sql` files. The `Render.toSsdtDirectory` composition consumes `(ArtifactByKind<SsdtFile>, RefactorLogXml, Manifest)` and emits the directory map — the refactor.log appears at `<outDir>/<projectName>.refactorlog`.

## §5 Refactor-log integration

The cutover demand named at `VISION.md:32` and revisited in `R1` of `VISION_REVIEW.md`: schema and data evolve continuously; renames must propagate so DacFx applies an `ALTER ... RENAME` not a `DROP+CREATE`. The SSDT DDL emitter's role in the rename story:

- **The CREATE TABLE statement uses the *current* names** (post-rename). This is straightforward: the emitter reads `Kind.Name` and `Attribute.Column.ColumnName` as they exist in the input Catalog; if a rename has been applied (via `NamingMorphism`, `src/Projection.Core/Passes/NamingMorphism.fs`), the names already reflect the new state.
- **The refactor.log carries the rename history**. Per Appendix H §H.6, `RefactorLogEmitter : CatalogDiff -> Result<ArtifactByKind<RefactorLogEntry>, EmitError>` produces the SSDT-native rename records. These ride alongside the SSDT DDL bundle: when DacFx incremental-deploys, it reads the refactor.log first, recognizes the rename, and ALTERs instead of DROP+CREATEing.
- **The SSDT DDL emitter does not consume the refactor.log directly.** Composition: `Render.toSsdtDirectory` takes both the per-kind `ArtifactByKind<SsdtFile>` from the DDL emitter *and* the `ArtifactByKind<RefactorLogEntry>` from `RefactorLogEmitter`; it emits the `.sql` files into `Modules/<Module>/` and the `.refactorlog` into the project root.

The two emitters are **independently testable** sibling Π's. T11 cross-validates: the set of SsKeys referenced in the refactor.log's `Renamed` map must be a subset of the set of SsKeys in the SSDT DDL emitter's `ArtifactByKind` (you cannot rename what you don't emit). Property test: `Set.isSubset (renameLog |> Map.keys) (ddlSlices |> Map.keys)`.

The refactor.log is emitted only if `CatalogDiff.between previous current` carries renames — empty diff → no refactor.log written. This matches V1's behavior (no refactor.log file when nothing renames).

## §6 Determinism

T1 byte-determinism is in V2's range for the SSDT DDL surface — unlike DACPAC, no DacFx wall-clock issue. Sources of nondeterminism the emitter must control:

1. **Newline normalization.** V1 uses `Environment.NewLine` (CRLF on Windows, LF on Linux/macOS) at `PerTableWriter.cs:176` and `TablePlanWriter.cs:83`. V2 normalizes to LF unconditionally in F# emitters. The C# host writing files writes LF bytes regardless of host OS. Documented divergence: `Tolerance.NewlineNormalization` strips `\r` from V1's output before comparison.
2. **Dictionary / Map iteration order.** V2 uses `ImmutableSortedDictionary` (or F# `Map` with `IComparable` keys) wherever it iterates per-key collections. The `ArtifactByKind` underlying `Map<SsKey, _>` already iterates by SsKey order (F# `Map` is sorted). Module iteration order in `Catalog.allKinds` follows the input list; the canonicalization pass at `CanonicalizeIdentityPass` enforces alphabetical-by-SsKey before emit (per A33: Schema emission uses **deterministic ordering**, never topological).
3. **Trailing whitespace.** Every line is `TrimEnd`'d before being appended. Tests cover via `output.Lines |> Array.forall (fun l -> l = l.TrimEnd())`.
4. **Index ordering within a table file.** V1 sorts index names case-insensitively (`PerTableWriter.cs:163`). V2 sorts by `Index.SsKey` (case-sensitive ordinal); rationale: V2's identity is the structural key, not the name, and the canonicalization pass guarantees SsKey ordering matches name ordering modulo case folding. `Tolerance.IgnoreIndexOrder = false` if comparator ever sees a mis-ordering.
5. **Extended-property ordering.** V1 emits in (table, columns, indexes) groups, with columns iterated in `table.Columns` order (`ExtendedPropertyScriptBuilder.cs:53`). V2 emits in (table, sorted-columns-by-SsKey, sorted-indexes-by-SsKey) order; if column order in the IR is canonical (post-canonicalization pass), the two agree.
6. **FK ordering across statements.** Inline FKs follow column order; ALTER-TABLE deferred-NOCHECK FKs are sorted alphabetically by name (V1 at `PerTableWriter.cs:169`). V2 mirrors.

T1 contract for the SSDT DDL emitter:

```fsharp
property ``T1: SsdtDdlEmitter is byte-deterministic`` (c: Catalog) =
    let a = SsdtDdlEmitter.emit c |> Result.value |> Render.toSsdtDirectory order manifest
    let b = SsdtDdlEmitter.emit c |> Result.value |> Render.toSsdtDirectory order manifest
    Map.equal a b   // pointwise byte-equal across every (RelativePath, body) pair
```

This is straightforward — no DacFx, no zip-canonicalization post-pass, no `Origin.xml` timestamp problem. The DACPAC pre-scope §3's option (a)/(b)/(c) elaborate dance does not apply to chapter 4.1.A; the SSDT DDL surface satisfies T1's original (byte-determinism) form.

## §7 EmissionPolicy axis

The new `EmissionPolicy` DU (per `VISION_REVIEW.md` Appendix D §D.2; the variant set is `AllRemaining` default / `AllExceptStatic` / `AllData`) lives in `Projection.Core/Policy.fs`. It is intent, not evidence; per A18 amended, the SSDT DDL emitter cannot consume Policy. Resolution: a chapter-4 pass — `EmissionPolicyPass : Policy -> Catalog -> Lineage<Catalog>` — interprets EmissionPolicy and produces an enriched Catalog that downstream emitters consume. The SSDT DDL emitter sees only the post-pass Catalog and reads no Policy.

What `AllExceptStatic` means for the SSDT DDL emitter: the pass strips `Static` modality marks from kinds, leaving the kind itself (the table) but signaling that no inline INSERTs and no post-deploy seed file should be emitted by the static-seeds emitter. Schema-only deployments are the typical use. The SSDT DDL emitter *always* emits the table (the Kind). It never inlines INSERTs (chapter 4.1.A explicit non-goal, per §3). Therefore **`AllExceptStatic`'s effect on SSDT DDL is structurally trivial — the emitter behaves the same way; the seed emitter (chapter 4.1.B) is the consumer that actually changes**.

What `AllData` means: the SSDT DDL emitter emits *no* tables (because schema is assumed deployed); only the data-emission triumvirate runs. Per A18 amended this is achieved by the EmissionPolicyPass producing an empty-modules Catalog when fed `AllData`. The SSDT DDL emitter, given an empty Catalog, produces an empty `ArtifactByKind` (zero entries). The smart-constructor at `ArtifactByKind.create` accepts this — empty input → empty output → trivially T11-compliant.

What `AllRemaining` (default) means: full schema + full data; the SSDT DDL emitter emits its tables, and the data triumvirate emits its INSERTs. This is the chapter-4.1 default and the production-deployment shape.

## §8 Slice-by-slice breakdown

Eight substantive slices, ordered by the IR-grows-under-evidence discipline (small evidence first, complex evidence later). Each slice gets a goal, test, file footprint, LOC estimate, acceptance criterion.

**Slice 1 — Single-table catalog, schema + table emission.** Goal: produce one `.sql` per kind containing only the `CREATE TABLE` body. Fixture: one Module, one Kind with two scalar Attributes (one PK, one non-PK), no FKs, no indexes, no modality. Test: golden-file test; T1 byte-determinism property; T11 keysets agree with `RawTextEmitter`. Files: `src/Projection.Targets.SSDT/SsdtDdlEmitter.fs` (~120 LOC), `tests/Projection.Tests/SsdtDdlEmitterTests.fs` (~80 LOC). Acceptance: 4 tests pass; the directory map carries one entry at `Modules/<Module>/<Schema>.<Table>.sql`.

**Slice 2 — Multi-attribute formatting + composite types.** Goal: every `PrimitiveType` variant maps correctly; column-line padding matches V1 layout. Fixture: kind with all 9 PrimitiveType columns. Test: byte-equality against an expected string per type; T11 cross-validation with `RawTextEmitter`'s `defaultSqlType` (extract a shared module). Files: extend SsdtDdlEmitter (~30 LOC); shared `SqlTypeMap.fs` extraction (~20 LOC). Acceptance: every PrimitiveType produces the expected SQL type token; the emitters agree.

**Slice 3 — Indexes (single-column, non-unique and unique).** Goal: `CREATE INDEX [name] ON [Schema].[Table] ([col])` per non-PK index, sorted by SsKey. Fixture: one kind with two indexes (one unique, one not). Test: golden file; index name follows V1's `IndexNameGenerator.Generate` shape (`IX_<Table>_<col>` / `UIX_<Table>_<col>`). Files: extend SsdtDdlEmitter (~50 LOC); index-name helper extracted (~20 LOC). Acceptance: indexes appear in alphabetical-by-SsKey order; PK-marked indexes are skipped.

**Slice 4 — Composite primary keys.** Goal: when `Kind.primaryKey` returns >1 attribute, emit a separate `CONSTRAINT [PK_<Table>] PRIMARY KEY CLUSTERED ([col1], [col2])` table-constraint at the end of the CREATE TABLE body. Fixture: one kind with a 2-attribute composite PK. Test: golden file; T11 with DACPAC for the same kind. Files: extend SsdtDdlEmitter (~30 LOC). Acceptance: composite PK emits as table-constraint, not as inline column-constraint.

**Slice 5 — Intra-module FKs.** Goal: same-module FK constraints inline in the CREATE TABLE body. Fixture: two kinds in one module, one with an FK to the other. Test: golden file; FK constraint name matches V1's `FK_<Owner>_<Ref>_<col>` shape; T11. Files: extend SsdtDdlEmitter (~60 LOC); FK-name helper extracted (~30 LOC). Acceptance: inline FK appears in the owning kind's file; target kind's file is unchanged.

**Slice 6 — Cross-module FKs (parity-divergence accepted).** Goal: FKs whose target lives in a different module emit inline (per V1) but the target's physical name is resolved via `Catalog.tryFindKind`. Fixture: two modules, one kind in each, FK across. Test: golden file (V1-parity); explicit `Tolerance.CrossModuleFkResolution` flag in the comparator covers the chapter-2 rule-16 deferral. Files: extend SsdtDdlEmitter (~20 LOC). Acceptance: cross-module FK resolves correctly when SnapshotRowsets is present (gated; `[<Skip>]` until 3.2 lands).

**Slice 7 — Identity columns + default constraints.** Goal: when `Attribute.IsIdentity = true` (new IR field landed by 3.2), emit `IDENTITY(1, 1)` after the type. Default constraints: when `Attribute.Default = Some (Literal "0")`, emit `CONSTRAINT [DF_<Table>_<Col>] DEFAULT (0)`. Fixture: one kind with an identity PK and one default. Test: golden file; T11. Files: extend SsdtDdlEmitter (~40 LOC); IR widening in `Catalog.fs` (~10 LOC). Acceptance: IDENTITY clause matches V1's `SmoColumnBuilder` output; default-constraint name matches V1's normalizer.

**Slice 8 — Extended properties.** Goal: when `Kind.Description : string option` or `Attribute.Description : string option` is `Some`, emit `EXEC sys.sp_addextendedproperty` statements at the end of the file. Fixture: one kind with a table description and one column description. Test: golden file matching V1's exact escape rule (`'` → `''`). Files: extend SsdtDdlEmitter (~50 LOC); IR widening for descriptions (~10 LOC); shared escape helper (~10 LOC). Acceptance: byte-equal to V1's `BuildTableExtendedPropertyScript` output for matching descriptions.

**Slice 9 — Manifest emitter.** Goal: produce a `manifest.json` at `<outDir>/manifest.json` matching V1's `SsdtManifest` schema (`SsdtManifest.cs:6-14`). Fields: `Tables` (array of `TableManifestEntry` with `Module`, `Schema`, `Table`, `TableFile`, `Indexes`, `ForeignKeys`, `IncludesExtendedProperties`); `Options`; `Emission` (algorithm + content-hash); `PreRemediation` (empty in chapter 4.1.A); `Coverage` and `PredicateCoverage` (chapter 4.4 fills in); `Unsupported`. Use `Utf8JsonWriter` per the precedent at `JsonEmitter.fs:140` (deterministic UTF-8). Files: `src/Projection.Targets.SSDT/ManifestEmitter.fs` (~150 LOC); `tests/Projection.Tests/ManifestEmitterTests.fs` (~80 LOC). Acceptance: manifest is byte-identical to V1's for an aligned input fixture (modulo documented coverage/policy divergences which are explicit `null` in V2).

**Slice 10 — Refactor-log composition + post-deploy split.** Goal: `Render.toSsdtDirectory` consumes `(ArtifactByKind<SsdtFile>, RefactorLogEntries option, Manifest)`; emits the refactor.log XML at `<outDir>/<projectName>.refactorlog`. Cross-module FKs split into a `Scripts/PostDeploy/CrossModuleForeignKeys.sql` if `Tolerance.PostDeployForeignKeys = true` (operator-decided via Policy). Fixture: catalog with a rename in the diff, one cross-module FK. Test: refactor.log file is present iff diff carries renames; cross-module FK file present iff post-deploy mode. Files: `src/Projection.Targets.SSDT/Render.fs` (~80 LOC); test (~60 LOC). Acceptance: composition produces the directory map with all expected entries; T11 holds across SSDT DDL + RefactorLogEmitter.

Total: ~720 LOC source, ~480 LOC tests. Ratio ~1:0.67; tighter than the chapter-3 1:1.7 target because a third of the code is shared with `RawTextEmitter` and `DacpacEmitter`.

## §9 Test strategy

**Tier-1 pure properties (no Docker, no DacFx).**

- **T1 byte-determinism.** `forall c in generated catalogs, emit c = emit c`. FsCheck generator over `Catalog`. Sub-second per case.
- **T11 sibling-Π commutativity (DACPAC × SSDT DDL).** `forall c, keys (dacpacEmitter c |> ArtifactByKind.toMap) = keys (ssdtDdlEmitter c |> ArtifactByKind.toMap)`. Plus per-kind agreement on the rendered SQL type for each attribute (extracted via the shared `SqlTypeMap`). Cross-validates the impedance-map decisions in §3.
- **T11 sibling-Π commutativity (SSDT DDL × RawTextEmitter).** `forall c, keys (ssdtDdlEmitter c) = keys (rawTextEmitter c |> slice)`. Easier than the DACPAC case because both produce strings per kind; this property is the *cheap* T11 test that runs in pre-commit.
- **A18 amended.** SsdtDdlEmitter's signature does not accept `Policy` — compiler-enforced. No runtime test needed.
- **A33 (deterministic ordering).** The emitter's per-file ordering is a deterministic function of SsKey, never of FK-topology. Property: shuffling the input Catalog's modules and re-canonicalizing produces the same emission.

**Tier-2 container-pooled deploy (testcontainers, ~150ms per case).**

- **DacFx ingestion.** `forall c, ssdtDdlEmitter c |> Render.toSsdtDirectory |> deployToTestcontainer = no errors`. SSDT projects ingest the per-table `.sql` files via `SqlPackage.exe Publish` or a programmatic `SchemaModel.LoadFromFiles + DacServices.Deploy`. The promoted-lane integration test runs this. Mainly catches "DacFx tolerates V2's layout" failures.
- **Idempotent redeploy (the CDC-safety surface).** Deploy → redeploy → assert second deploy issued zero ALTERs. This is `R2`'s `idempotentRedeploy` predicate, applied to the SSDT DDL bundle. Per `VISION.md` "Both lanes use the same `Catalog × Policy × Profile` algebra"; the redeploy-zero-ALTER assertion is the SSDT DDL surface's CDC-safety verification.

**Tier-3 differential V1 vs. V2.** This is the canary-as-V1-verifier from `VISION_REVIEW.md` Appendix F.5 step 2, retargeted to compare emitted bytes rather than deployed schemas:

```
V1 produces <outDir-v1>/Modules/...
V2 produces <outDir-v2>/Modules/...
Compare: every file at the same relative path is byte-equal modulo a configurable Tolerance set.
```

The Tolerance set is the documented divergence list from §10. Each false-positive surfaced by running the differential against a real V1 fixture becomes either (a) a fix in V2 or (b) a new tolerance entry with explicit citation. **This is the canary triangulation pattern from chapter 3.1 (Appendix F §F.6) applied to text artifacts.** `C_v1_text` (V1's emitted directory), `C_v2_text` (V2's emitted directory), `C_v1_deployed` (V1's deployed schema read back via the Sql.ReadSide adapter), `C_v2_deployed` (V2's deployed schema read back); pairwise diffs attribute every divergence to V1 / V2 / comparator.

**Golden-file tests per slice.** One curated Catalog → one expected SSDT directory tree, committed to the repo at `tests/Projection.Tests/Goldens/SsdtDdl/<slice-name>/`. The test reads expected, asserts equal. Updates require explicit operator approval (the `--update-goldens` test flag pattern).

## §10 V1 differential — what V2 deliberately does or doesn't match

The `Tolerance` record from `VISION_REVIEW.md` Appendix F.4 grows the following entries through chapter 4.1.A:

| Tolerance flag | Meaning | Citation |
|---|---|---|
| `IgnoreIndexNames = false` | V2 inherits V1's `IX_*`/`UIX_*` naming convention exactly. No tolerance needed. | `IndexNameGenerator.cs:30-36` |
| `IgnoreCheckConstraints : Set<SsKey>` | V1-only checks scoped per-kind; V2 IR doesn't model checks. | Appendix F.4 |
| `IgnoreExtendedProperties = true` (until slice 8) | V2 IR doesn't carry descriptions until SnapshotRowsets surfaces them. | `Catalog.fs:119` (no Description field) |
| `IgnoreDefaultNames = false` (until slice 7) | When IR carries defaults, V2 uses V1's `DF_<Table>_<Col>` convention. | `ConstraintNameNormalizer` referenced at `ForeignKeyNameFactory.cs:53` |
| `AttributeOrderInsensitive = false` | V2's canonicalization pass enforces SsKey-sorted attribute order; V1's order matches when input is canonical. | `Passes/CanonicalizeIdentityPass` |
| `NewlineNormalization = true` | V2 emits LF unconditionally; V1 emits OS-dependent. | `TablePlanWriter.cs:83` |
| `IgnoreHeaderComments = true` (initial) | V2 may omit the `/* Source: ... */` header block until the EmissionPolicyPass surfaces an equivalent. | `PerTableWriter.cs:187-267` |
| `CrossModuleFkResolution = "v2-direct"` | V2 resolves cross-module FK targets via `Catalog.tryFindKind`; V1 routes via the `ReferencedModule` field. Pre-3.2 these may diverge on cross-module references. | `CreateTableStatementBuilder.cs:158-163`; HANDOFF rule 16 deferral |
| `IgnoreNoCheckClause = false` | V2's IR currently doesn't model `IsNoCheck` FKs; slice-deferred. | `PerTableWriter.cs:110-114` |
| `IgnoreTriggers = true` | V2 IR has no triggers; V1 carries them at `PerTableWriter.cs:153-157`. Out of scope. | `SmoTriggerBuilder.cs` |
| `IgnoreFingerprintHash = true` | V1's manifest carries an emission `Fingerprint`; V2 emits an SsKey-rooted content hash that doesn't match V1's algorithm. | `SsdtManifest.cs:31` |
| `PostDeployForeignKeys = false` (initial) | V2 keeps cross-module FKs inline; if integration tests fail on deploy order, flip to `true`. | §4 |

Each tolerance entry has a citation, a re-open trigger, and an explicit re-evaluation point in chapter 5+. The comparator's default Tolerance profile starts maximally permissive and tightens as each tolerance's underlying gap is closed.

## §11 Risks

1. **DacFx tolerance for non-standard ordering.** V2's emitter orders attributes / indexes / FKs by SsKey. V1 orders by source declaration order (in some places) or by name (in others). `SqlPackage.exe Publish` may issue cosmetic ALTERs (column reordering) on the second deploy if the model-internal canonical order differs from the deployed order. Mitigation: tier-2 idempotent-redeploy property test catches this empirically. If it fires, V2's canonicalization pass aligns with whatever DacFx considers canonical (typically: PK columns first, then alphabetical).
2. **Refactor.log XML schema brittleness.** SSDT's refactor.log XML is undocumented; we infer the schema from sample `.refactorlog` files V1 has produced. If our schema diverges, DacFx silently ignores the refactor.log and DROP+CREATEs the renamed objects (the failure mode is *silent*, not loud — caught only by the idempotent-redeploy property test on a renamed Catalog). Mitigation: chapter 3.5's slice 1 is "round-trip a known-good V1 refactor.log through V2's emitter and assert byte-equality."
3. **Cross-module FK resolution divergence (rule 16).** Until SnapshotRowsets ships, V2's cross-module FK resolution may pick the wrong target. Mitigation: `Tolerance.CrossModuleFkResolution`; integration tests use single-module fixtures until 3.2 lands.
4. **Manifest schema drift.** V1's `SsdtManifest` may evolve. Mitigation: V2's `ManifestEmitter` produces a manifest with explicit version field; old V1 consumers ignore unknown fields, V2's reader rejects unknown versions. The chapter-3.1 differential surfaces drift loudly.
5. **DacFx wrapping the SSDT bundle.** Promoting an SSDT directory through a `.sqlproj` or directly through `SqlPackage.exe Publish` requires the C# host (chapter 4.1.A's deferred dependency). If DacFx rejects the bundle outright, V2's emitter is wrong and the chapter blocks. Mitigation: tier-2 deploy property test runs as part of the chapter's acceptance; failure here is caught early.
6. **Test fixture supply.** Golden-file tests require curated fixtures. The chapter-2 sample catalog (`Fixtures.fs:sampleCatalog`) is a good seed; the slice-by-slice fixture proliferation is real engineering work. Mitigation: a single end-to-end fixture covers slices 1–5; slices 6–10 each add one targeted fixture.

## §12 Dependencies

- **`ArtifactByKind<_>` type refactor (chapter 3 cross-cutting, Appendix H §H.4-H.7).** Hard prerequisite. The SSDT DDL emitter cannot be written cleanly without it; if the refactor lands first, slices 1–9 are unaffected by it and the type is consumed naturally. If chapter 3 ships emitters under the legacy `Catalog -> string` shape, chapter 4.1.A is the forcing function for the migration.
- **`RefactorLogEmitter` (chapter 3.5).** Soft prerequisite. Chapter 4.1.A's slice 10 (composition) depends on it. Slices 1–9 do not.
- **Read-side adapter + `CatalogEquivalence` comparator (chapter 3.1).** Hard prerequisite for tier-3 differential testing. The chapter-4.1.A integration tests use the comparator to assert V1-vs-V2 directory equivalence under the documented Tolerance set. Per the V2-verifies-V1 dogfood frame (Appendix F), the comparator is already shipping by chapter-3.1 close.
- **SnapshotRowsets adapter (chapter 3.2).** Soft prerequisite. Slices 7 (identity) and 8 (extended properties) gate on IR widening that SnapshotRowsets surfaces. Pre-3.2, those slices ship `[<Skip>]` stubs documenting the gating reason.
- **DacFx C# wrapper / `Projection.Targets.SSDT.Dacpac` C# project (chapter 3.3).** Soft prerequisite for tier-2 deploy property tests. The wrapper that owns `DacServices.Deploy` for the DACPAC chapter trivially extends to consume an SSDT directory.
- **`Projection.Pipeline` C# project (chapter 3.1).** Hard prerequisite for the file-system writer that consumes `Map<RelativePath, string>` and writes UTF-8 LF files to disk.
- **`EmissionPolicy` DU + `EmissionPolicyPass` (chapter 4.1.B sibling).** Soft co-dependency. The pass interpreting EmissionPolicy ships in 4.1.B (data triumvirate); 4.1.A's emitter consumes the post-pass Catalog and is unaware of which Policy was applied.

## §13 Files inventory

**Created:**

- `sidecar/projection/src/Projection.Targets.SSDT/SsdtDdlEmitter.fs` (~380 LOC across slices 1–8)
- `sidecar/projection/src/Projection.Targets.SSDT/ManifestEmitter.fs` (~150 LOC, slice 9)
- `sidecar/projection/src/Projection.Targets.SSDT/Render.fs` (~120 LOC, slice 10)
- `sidecar/projection/src/Projection.Targets.SSDT/SqlTypeMap.fs` (~30 LOC, extracted from `RawTextEmitter.fs`)
- `sidecar/projection/src/Projection.Targets.SSDT/IndexNameFactory.fs` (~40 LOC, V1-parity convention)
- `sidecar/projection/src/Projection.Targets.SSDT/ForeignKeyNameFactory.fs` (~50 LOC, V1-parity convention)
- `sidecar/projection/src/Projection.Targets.SSDT/SsdtFile.fs` (~30 LOC, types)
- `sidecar/projection/tests/Projection.Tests/SsdtDdlEmitterTests.fs` (~280 LOC)
- `sidecar/projection/tests/Projection.Tests/ManifestEmitterTests.fs` (~80 LOC)
- `sidecar/projection/tests/Projection.Tests/RenderToSsdtDirectoryTests.fs` (~80 LOC)
- `sidecar/projection/tests/Projection.Tests/SsdtDdlVsDacpacTests.fs` (~60 LOC, T11 cross-validation)
- `sidecar/projection/tests/Projection.Tests/Goldens/SsdtDdl/...` (~10 fixture directory trees)

**Touched:**

- `sidecar/projection/src/Projection.Targets.SSDT/RawTextEmitter.fs` (~30 LOC removed; `defaultSqlType`/`renderAction`/`quote`/`originLabel`/`modalityLabel` extracted to `SqlTypeMap.fs`)
- `sidecar/projection/src/Projection.Core/Catalog.fs` (~20 LOC, slice 7+8 IR widening for `IsIdentity`, `Default`, `Description`)
- `sidecar/projection/src/Projection.Core/Policy.fs` (~30 LOC, `EmissionPolicy` DU)
- `sidecar/projection/src/Projection.Core/Verification/CatalogEquivalence.fs` (~40 LOC, new tolerance entries from §10)
- `sidecar/projection/src/Projection.Targets.SSDT/Projection.Targets.SSDT.fsproj` (file order: `SsdtFile.fs` before `SqlTypeMap.fs` before `RawTextEmitter.fs` before `SsdtDdlEmitter.fs` before `ManifestEmitter.fs` before `Render.fs`)
- `sidecar/projection/AXIOMS.md` (T1 amendment notes the SSDT DDL surface as a byte-determinism instance, complementing DACPAC's content-determinism)
- `sidecar/projection/DECISIONS.md` (entries for: project-cohabitation decision, newline-normalization divergence from V1, post-deploy FK split toggle, EmissionPolicy DU placement)
- `sidecar/projection/HANDOFF.md` (chapter-4.1.A close notes; what 4.1.B inherits)

### Critical Files for Implementation

- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Targets.SSDT/RawTextEmitter.fs`
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Core/Catalog.fs`
- `/home/user/outsystems-ddl-exporter/src/Osm.Smo/PerTableWriter.cs`
- `/home/user/outsystems-ddl-exporter/src/Osm.Smo/PerTableEmission/CreateTableStatementBuilder.cs`
- `/home/user/outsystems-ddl-exporter/src/Osm.Emission/SsdtManifest.cs`
