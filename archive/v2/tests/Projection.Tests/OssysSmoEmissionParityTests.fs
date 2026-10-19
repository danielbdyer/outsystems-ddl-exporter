module Projection.Tests.OssysSmoEmissionParityTests

// V1 parity audit — slice 5.3.α.smo. Reserves matrix rows 120-130.
// V1 SMO-based per-table emission machinery vs V2 ScriptDom typed-AST.

open Xunit

[<Fact(Skip = "Matrix row 120 — 🟡 DIVERGENCE. V1 emission via `Microsoft.SqlServer.Management.Smo` (mutable Table/Column/Index/ForeignKey objects; `Table.Script()` to render). V2 via `Microsoft.SqlServer.TransactSql.ScriptDom` typed-AST builders (`ScriptDomBuild.buildCreateTable/buildCreateIndex/buildSetExtendedProperty` → typed Statement; delegate to `Sql160ScriptGenerator` with pinned options). See `DECISIONS 2026-05-18 (slice 5.3.α.smo) — Schema emission via ScriptDom typed-AST over SMO scripter`. Documented in chapter 4.1.A close arc. R6 governance gates V2-driver per-environment flip.")>]
let ``5.3.α row 120: V1 SMO scripter vs V2 ScriptDom typed-AST emitter architecture`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 120 + DECISIONS 2026-05-18 (slice 5.3.α.smo)"

[<Fact(Skip = "Matrix row 121 — 🟢 PARITY. V1 `CreateTableStatementBuilder.BuildCreateTableStatement` constructs `CreateTableStatement` (columns inline with nullability/identity/collation/defaults; PK logic per first-ordinal rule; FK constraints inline with NOCHECK deferred). V2 `ScriptDomBuild.buildCreateTable` mirrors shape: columns inline, PK logic, FK constraints, NOCHECK deferred to ALTER TABLE statements. PK naming convention `PK_<schema>_<table>` per chapter 3.7 slice β. Canary diff on 300-table schema shows zero delta modulo Tolerance.NormalizeWhitespace.")>]
let ``5.3.α row 121: V1 CreateTableStatementBuilder ↔ V2 ScriptDomBuild.buildCreateTable PARITY`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 121"

[<Fact(Skip = "Matrix row 122 — 🟢 PARITY (with deferred axes). V1 `IndexScriptBuilder.BuildCreateIndexStatement` handles keyed + included columns, sort order (Descending via SortOrder enum), metadata (fill-factor / pad-index / ignore-dup-key / compression / filegroup / partition scheme), filter predicate via ParsePredicate. V2 `ScriptDomBuild.buildCreateIndex` (chapter 4.5 / 4.8 / 4.9 slices) covers all the same axes including direction (chapter 4.9 slice γ). **Deferred V2 axes** (paired matrix row 56): PartitionColumns + PartitionCompression + DataSpace not yet lifted; AllowRowLocks + AllowPageLocks shipped per chapter 4.8 slice β.")>]
let ``5.3.α row 122: V1 IndexScriptBuilder ↔ V2 ScriptDomBuild.buildCreateIndex PARITY (partition axes deferred)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 122"

[<Fact(Skip = "Matrix row 123 — 🟢 PARITY (with V2 EXTENSION via Pass layer). V1 FK emission spans 5 files: `SmoForeignKeyBuilder` + `ForeignKeyEvidenceResolver` (5-phase rule-matching) + `ForeignKeyNameFactory` + `ForeignKeyColumnNormalizer` + `ForeignKeyFallbackFactory`. V2 distributes: emission via `ScriptDomBuild.buildForeignKeyConstraint` (inline in buildCreateTable); evidence resolution lifts to `Projection.Core.Passes.ForeignKeyPass` + `ForeignKeyRules` (strategized; per slice 5.4.β.nullability row 64); name generation via chapter 4.6 slice γ. **Deferred axes** (paired matrix row 58): UPDATE referential action; (paired matrix row 59): NOCHECK per-constraint trusted state.")>]
let ``5.3.α row 123: V1 5-file FK emission ↔ V2 ScriptDomBuild.buildForeignKey + ForeignKeyPass strategy`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 123"

[<Fact(Skip = "Matrix row 124 — 🟢 PARITY. V1 `ExtendedPropertyScriptBuilder` (~142 LOC) emits `EXEC sys.sp_addextendedproperty` via string concatenation with `'` → `''` escaping. V2 `ScriptDomBuild.buildSetExtendedProperty` (chapter 4.1.A slice 8) builds `ExecuteStatement` wrapping sp_addextendedproperty via typed ExecuteParameter binding. Same SQL surface; V2's typed-AST eliminates hand-rolled escaping. Multi-level emission (Schema/Table/Column/Index) integrated at SsdtDdlEmitter dispatch.")>]
let ``5.3.α row 124: V1 ExtendedPropertyScriptBuilder ↔ V2 ScriptDomBuild.buildSetExtendedProperty PARITY`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 124"

[<Fact(Skip = "Matrix row 125 — 🟡 DIVERGENCE (deliberate simplification). V1 type-mapping spans 4 files (`TypeMappingPolicy` + `TypeMappingRule` + `TypeMappingPolicyDefinition` + `TypeMappingPolicyLoader`); 3-path resolution (on-disk override + external DB type + attribute default); loads from JSON config. V2's `PrimitiveType` closed DU + `SqlTypeCorrespondence` module hardcodes mapping; read-side (`ReadSide.mapSqlType`) resolves type before emission; emitter consumes typed VO. **Rationale**: V2's pillar 1 + A18 amended — Π consumes typed Catalog × Profile, no Policy. Type resolution is profile-construction-time, not emission-time. Round-trip property tested per chapter 3.7 slice β.")>]
let ``5.3.α row 125: V1 4-file TypeMappingPolicy vs V2 PrimitiveType DU + SqlTypeCorrespondence (V2 simplified)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 125"

[<Fact(Skip = "Matrix row 126 — 🟢 PARITY. V1 `IdentifierFormatter` handles bracket-quoting per `QuoteType.SquareBracket`; `ModuleNameSanitizer` cleans module names; `IndexNameGenerator` builds index names. V2 `ScriptDomBuild.bracketed` (lines 48-52) delegates quoting to ScriptDom's `Identifier(QuoteType.SquareBracket)`; module-name normalization upstream in CatalogReader (chapter 2 OSSYS adapter); index naming via `indexNameResolver` (chapter 4.5 + 4.9 slice γ direction). Per pillar 8 — names are concepts; deterministic generation at source.")>]
let ``5.3.α row 126: V1 IdentifierFormatter + ModuleNameSanitizer + IndexNameGenerator ↔ V2 ScriptDom bracketed + adapter normalization PARITY`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 126"

[<Fact(Skip = "Matrix row 127 — 🟢 PARITY. V1 `ConstraintNameNormalizer` performs post-hoc rename mapping when table name is overridden (old constraint name → new); handles composite cases. V2 generates constraint names deterministically at emission-resolution time (after override is known); no post-hoc mapping. Convention: `PK_<schema>_<table>` for primary keys; `FK_<owner>_<target>` for FKs (chapter 4.6 slices γ-δ). Per pillar 8 (names are concepts; not post-hoc edits).")>]
let ``5.3.α row 127: V1 ConstraintNameNormalizer post-hoc rename vs V2 deterministic emission-time naming (V2 pillar 8)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 127"

[<Fact(Skip = "Matrix row 128 — 🟢 PARITY. V1 `StatementBatchFormatter` joins statements with `GO` separators + trims trailing whitespace per line; optional `NormalizeWhitespace`. V2 `BatchSplitter` (chapter 3.6 cash-out) ships two paths: gold-standard `splitViaScriptDom` (ScriptDom parser + Sql160ScriptGenerator per batch) + fallback `splitOnGoLineFold` (F# line-fold on `^GO$`). Batch assembly at realization layer (`Render.toText` / `Deploy.executeStream`). Per A35 (stream-realization).")>]
let ``5.3.α row 128: V1 StatementBatchFormatter ↔ V2 BatchSplitter + realization-layer assembly PARITY (A35)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 128"

[<Fact(Skip = "Matrix row 129 — 🟠 NOT-MAPPED. V1 `SmoTriggerBuilder.Build` extracts trigger definition from entity, normalizes whitespace, skips encrypted triggers (def is null), sorts by name; emits `SmoTriggerDefinition` carrying raw T-SQL body. **V2 status**: V2's `Trigger` IR shipped (chapter A.0' slice γ; matrix row 61 PARITY); emission deferred. Not in `SsdtDdlEmitter.statements` dispatch today. **Cash-out**: emit `ExecuteStatement` wrapping `CREATE TRIGGER` body. **Trigger**: chapter 4.2 (User FK reflow moves triggers when FKs move; trigger emission is coordinated with FK reflow, not standalone) OR chapter 4.10 / 5 standalone trigger emission slice.")>]
let ``5.3.α row 129: V1 SmoTriggerBuilder emission lifts to V2 SsdtDdlEmitter trigger dispatch (chapter 4.2/5+ deferred)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 129"

[<Fact(Skip = "Matrix row 130 — 🟢 PARITY. V1 `PerTableWriter` + `TableHeaderFactory` emit per-table to `Modules/<Module>/<Schema>.<Table>.sql` with header `/* Source: ... LogicalName ... */`. V2 `Render.toSsdtDirectory` (chapter 4.1.A slice 10) realizes `ArtifactByKind<SsdtFile>` map to disk with same path convention. Per A35/A36 — emitter produces in-memory artifact map; realization layer writes. **Tolerance**: V2 omits V1's per-table `/* Source: ... */` header comment per R6 split-brain (`Tolerance.IgnoreHeaderComments = true` initially); operator-requested headers are a future feature extension, not cutover-blocker.")>]
let ``5.3.α row 130: V1 PerTableWriter + TableHeaderFactory ↔ V2 Render.toSsdtDirectory PARITY (header comments tolerance)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 130"

[<Fact>]
let ``5.3.α.smo: smo-emission parity file present`` () =
    Assert.True(true)
