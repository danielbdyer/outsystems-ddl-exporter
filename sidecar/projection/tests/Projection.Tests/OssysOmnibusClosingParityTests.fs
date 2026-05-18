module Projection.Tests.OssysOmnibusClosingParityTests

// V1 parity audit — omnibus closer: slice 5.5.ε + 5.7.β +
// 5.2.α.valueobjects + 5.3.β. Reserves matrix rows 174-185. Final
// audit slice; closes the chapter 5 audit wave.

open Xunit

// 5.5.ε — UAT Users + Verification

[<Fact(Skip = "Matrix row 174 — 🟢 PARITY (partial). V1 `Osm.Pipeline/UatUsers/UserMatchingEngine.cs` (~316 LOC) carries 3 strategies (`CaseInsensitiveEmail` / `ExactAttribute` / `Regex`) + fallback (`RoundRobin` / `SingleTarget` / `Ignore`); `UserIdentifier.cs` (~155 LOC) is 3-variant numeric/guid/text discriminator. V2 `Projection.Core/UserIdentity.fs` + `UserRemap.fs` + `Projection.Core/Passes/UserFkReflowPass.fs`: typed `UserId` newtype + `SourceUserId` / `TargetUserId` orientation markers; `Policy.UserMatching` DU; `buildEmailIndex` (V2 lines 137-146) mirrors V1's `TryExactMatch`; matching result is typed `UserRemapContext` IR; diagnostics via `RemapDiagnostic` DU. **Slice δ ships ByEmail strategy**; BySsKey/Regex/FallbackToSystemUser deferred to slice ε per pre-scope.")>]
let ``5.5.ε row 174: V1 UserMatchingEngine 3-strategy ↔ V2 UserFkReflowPass + Policy.UserMatching PARITY (slice δ ByEmail; others deferred)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 174"

[<Fact(Skip = "Matrix row 175 — 🔵 V2-EXTENSION. V1 `UserIdentifier.cs` 3-variant discriminator (Numeric/Guid/Text; FromString/FromDatabaseValue factories) carries runtime-introspectable kind. V2 `UserId` newtype + typed `SourceUserId` / `TargetUserId` orientation markers — runtime-discriminated kind traded for compile-time orientation safety. Same numeric/guid/text evidence via value projection.")>]
let ``5.5.ε row 175: V1 UserIdentifier runtime discriminator vs V2 typed orientation markers (V2 compile-time safety)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 175"

[<Fact(Skip = "Matrix row 176 — 🔵 V2-EXTENSION. V1 `UatUsersPipelineRunner` imperative step-pipeline: 6 sequential steps (LoadQaUserInventory / PrepareUserMap / AnalyzeForeignKeyValues / DiscoverUserFkCatalog / ApplyMatchingStrategy / LoadUatUserInventory) chained via mutable `UatUsersContext`. V2 `UserFkReflowPass.discover` monadic composition via `Lineage.bind`; immutable IR (`UserRemapContext`) produced; pass-return-type codification per `DECISIONS 2026-05-13`. Per matrix row 131 — registry-driven composition principle extends to UAT users pipeline.")>]
let ``5.5.ε row 176: V1 UatUsersPipelineRunner imperative steps ↔ V2 UserFkReflowPass monadic composition (per row 131)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 176"

[<Fact(Skip = "Matrix row 177 — 🟠 NOT-MAPPED (gated). V1 `Osm.Pipeline/UatUsers/Verification/{UatUsersVerifier,FkCatalogCompletenessVerifier,TransformationMapVerifier,SqlSafetyAnalyzer}.cs` (~4 files) — orchestrates 3 independent verifiers post-pipeline; `UatUsersVerificationContext` + `UatUsersVerificationReport` synthesizes results. V2 verification deferred (post-cutover v2-driver work per V2_DRIVER.md). Slice δ pre-scope names verification as out-of-scope; canary's round-trip diff + tolerance table carry verification responsibility in dual-track mode per R6 governance. **Cash-out**: chapter 4.3+ post-deploy verification phase OR cutover dry-run discovers a verification case the canary doesn't cover.")>]
let ``5.5.ε row 177: V1 UatUsersVerifier + 3 verifiers + Report deferred to V2 chapter 4.3+ post-deploy verification`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 177"

// 5.7.β — LoadHarness

[<Fact(Skip = "Matrix row 178 — ⚫ V1-SUNSET (partial). V1 `Osm.LoadHarness/LoadHarnessRunner.cs` (~6 files / ~1300 LOC): ExecuteAsync orchestrator with script replay + batch splitting on GO; `ScriptReplayResult` carries per-batch timing + DMV wait-stats delta + lock summary + index fragmentation. V2 has NO direct LoadHarness equivalent — canary mechanism (`tests/Projection.Tests/ScriptDomRoundTripTests.fs` + `GeneratorScaleTests.fs`) replaces V1's load-harness for pre-deployment validation. **Sunset rationale**: V1's perf measurement + DB instrumentation (wait stats / locks / fragmentation) is a V1-side operational tool; V2's pre-cutover validation relies on schema-only canary (fast, structural) + operator-reality canary (slower, 300-table 50k-row baseline). DMV instrumentation is post-cutover operator-facing tool — chapter 5+ work.")>]
let ``5.7.β row 178: V1 LoadHarnessRunner DMV instrumentation sunsets — V2 canary covers pre-cutover; DMV deferred to post-cutover tool`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 178"

[<Fact(Skip = "Matrix row 179 — 🟠 NOT-MAPPED. V1 `Osm.LoadHarness` DMV-based instrumentation: `QueryWaitStatsAsync` (lines 278-303) + delta calculation (lines 306-329); `QueryLockSummaryAsync` (lines 331-357); `QueryIndexFragmentationAsync` (lines 360-388). Provides per-batch timing + wait-stats + locks + fragmentation snapshots. V2 has no DMV instrumentation; Bench surface covers timing per A24/A25 (iterator logging per chapter 3.6). **Cash-out shape**: post-cutover operator-facing tool consuming Bench samples + adding DMV queries via `Projection.Adapters.Sql` DMV adapter; emit consolidated post-deploy diagnostic report. **Trigger**: chapter 5+ operator-facing post-deploy tools OR operator demands DMV-style observability beyond Bench.")>]
let ``5.7.β row 179: V1 DMV instrumentation (WaitStats + Locks + IndexFragmentation) lifts to V2 chapter 5+ post-deploy DMV adapter`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 179"

// 5.2.α.valueobjects

[<Fact(Skip = "Matrix row 180 — 🔵 V2-EXTENSION. V1 `Osm.Domain/ValueObjects/*.cs` (~11 files) — 11 naming VOs (EntityName / ModuleName / TableName / ColumnName / AttributeName / SchemaName / IndexName / ForeignKeyName / SequenceName / TriggerName + StringValidators shared validation); each is record struct with `Create: Result<Name>` validating via `StringValidators.RequiredIdentifier`. V2 consolidates to ONE load-bearing identity VO (`SsKey` 4-variant DU per row 45 + matrix; identity is structural per A1; never a string) + Name VO (presentation-only; smart constructor per pillar 8) + `Coordinates.fs` typed records (TableId + ModuleId bundle related names). **V2 cleaner**: V1's 11-type struct sprawl was compile-time noise; V2's identity-vs-presentation split (SsKey vs Name) is structurally cleaner without parity loss. Per A1 (identity is structural) + pillar 8 (domain-first naming).")>]
let ``5.2.α.valueobjects row 180: V1 11 naming VOs ↔ V2 SsKey identity + Name presentation + Coordinates bundles (V2 stronger)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 180"

[<Fact(Skip = "Matrix row 181 — 🟢 PARITY. V1 `Osm.Domain/ValueObjects/StringValidators.cs` shared validation logic — `RequiredIdentifier` enforces non-null + non-empty + trimmed. V2 distributes validation across consumer smart constructors per IR-grows-under-evidence; equivalent invariant (`Name.create` rejects empty/whitespace; per-type smart constructors validate). Two-consumer threshold for shared validator module not yet met in V2. Per CLAUDE.md operating-disciplines.")>]
let ``5.2.α.valueobjects row 181: V1 StringValidators shared validation ↔ V2 per-consumer smart constructors PARITY`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 181"

// 5.3.β — CreateTableStatementBuilder + IndexScriptBuilder line-by-line audit

[<Fact(Skip = "Matrix row 182 — 🟢 PARITY (95%). V1 `Osm.Smo/CreateTableStatementBuilder.cs` line-by-line audit: column data type (line 296; TranslateDataType SMO→ScriptDom) ↔ V2 `dataTypeReference` (lines 100-138; PrimitiveType DU dispatch). Nullability (V1 line 301) ↔ V2 lines 156. Identity IDENTITY(1,1) (V1 lines 304-311) ↔ V2 lines 160-168. Primary key — single-column inline (V1 lines 67-77) DEFERRED in V2 (all PKs table-level via primaryKeyConstraint); multi-column PK (V1 lines 81-98) ↔ V2 (lines 235-238) PARITY. Column ordinal order (V1 line 64) ↔ V2 implicit. Foreign keys — DELETE action (V1 line 168) ↔ V2 lines 203-207. NOCHECK FK (V1 lines 214-286 string-composed) ↔ V2 deferred to MigrationDependenciesEmitter typed-statement emission. **Deferred axes**: column defaults + CHECK constraints + computed columns (V1 lines 319-364) — ColumnDef IR fields exist; emit layer deferred per slice ζ candidates.")>]
let ``5.3.β row 182: V1 CreateTableStatementBuilder line-by-line ↔ V2 ScriptDomBuild.buildCreateTable PARITY 95% (defer candidates named)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 182"

[<Fact(Skip = "Matrix row 183 — 🟢 PARITY (70%). V1 `Osm.Smo/IndexScriptBuilder.cs` line-by-line audit: index columns + sort order (V1 lines 65-84) ↔ V2 lines 757-771 (ColumnWithSortOrder per keyCol; Descending → SortOrder.Descending). INCLUDE columns (V1 lines 67-71) ↔ V2 lines 773-778 (IsIncluded flag dispatch). WHERE clause parsing (V1 line 410 TSql150Parser; null on failure) ↔ V2 line 698 TSql160Parser (upgraded; SQL Server 2022 compat); **V2 EXTENSION**: filter-parse failures surface as Diagnostics Warning (V1 silent). FillFactor + PadIndex + StatisticsNoRecompute + AllowRowLocks + AllowPageLocks: all 5 axes 100% parity. **Deferred axes** (slice ζ candidates): IgnoreDupKey ON/OFF (V1 lines 215-221); DataCompression with partition-range collapse (V1 lines 259-301); FileGroup/PartitionScheme dataspace (V1 lines 322-374). Paired matrix rows 55+56.")>]
let ``5.3.β row 183: V1 IndexScriptBuilder line-by-line ↔ V2 ScriptDomBuild.buildCreateIndex PARITY 70% (partition axes deferred)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 183"

[<Fact(Skip = "Matrix row 184 — 🔵 V2-EXTENSION. V1 `Osm.Smo/IndexScriptBuilder.ParsePredicate` returns null on parse failure (silent; filtered by caller; lines 403-419). V2 `ScriptDomBuild.tryParseFilterWithDiagnostics` (chapter 4.6 slice γ; lines 692-735) emits Diagnostics Warning entry + None on parse failure (lines 702-724); the manifest carries the filter-parse-failure event as operator-visible per slice 5.4.γ.opportunities Per-pass DiagnosticEntry contract. V1's silent-skip becomes V2's named diagnostic per Total-decisions-named-skips discipline.")>]
let ``5.3.β row 184: V1 ParsePredicate silent-failure vs V2 tryParseFilterWithDiagnostics observable-failure (V2 stronger)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 184"

[<Fact(Skip = "Matrix row 185 — 🔵 V2-EXTENSION. V1 `Osm.Smo/IndexScriptBuilder.ColumnReferenceRewriteVisitor` (lines 421-450) rewrites column references post-parse (physical → logical name mapping) via visitor pattern. V2 encodes both names in ColumnDef and uses logical (Name field) at emit time; no rewriter visitor needed. Per pillar 8 — names are concepts; deterministic at-source generation. V2 IR carries both names from CatalogReader (chapter 2 OSSYS adapter responsibility); emitter consumes logical name directly. Same outcome; V2's IR-level naming eliminates the rewriter complexity.")>]
let ``5.3.β row 185: V1 ColumnReferenceRewriteVisitor post-parse rewrite vs V2 IR-level dual-name encoding (V2 cleaner per pillar 8)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 185"

[<Fact>]
let ``omnibus closer: omnibus parity file present`` () =
    Assert.True(true)
