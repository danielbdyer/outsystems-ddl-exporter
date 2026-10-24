# V2 Patterns Compendium

The architectural patterns V2 carries — distilled from the chapter 5 parity-audit wave (23 slices; 185 matrix rows; 22 dated DECISIONS entries; 6 deep-audit agent transcripts) plus the prior chapters' canonical work.

V2 is the F# sidecar at `/home/user/outsystems-ddl-exporter/sidecar/projection/`. It is self-contained — zero runtime dependency on V1's trunk (per `DECISIONS 2026-05-16 (later)`). V1 is editorial donor only.

This document is the canonical "**how V2 thinks**" reference. Each pattern carries:
- **What** — concise statement
- **When** — what triggers reaching for it
- **How V2 uses it** — concrete shape
- **Worked example** — file path + line ranges where it lives
- **Counter-pattern** — what V2 explicitly does NOT do, and why

Audience: any contributor designing a new pass / emitter / adapter / pipeline component.

---

## Pattern Index

**Section A. Foundational disciplines (the meta-patterns)**
1. F#-pure-core / no-I/O-in-Core
2. Type system as contract
3. Determinism is constructed, not validated
4. IR grows under evidence, not speculation
5. Two-consumer threshold for emergent primitives
6. V2 self-containment + V1 as editorial donor

**Section B. Algebraic patterns (the IR + decision shape)**
7. Closed DUs + smart constructors with `Result<'a>`
8. Identity-vs-presentation split (SsKey + Name)
9. Aggregate-root smart-constructor invariants (A39)
10. Typed-strategy with `StrategyEvaluator<'context, 'config, 'decision>`
11. Ternary outcome space + named keep-reason variants
12. Per-axis decision sets over per-column aggregation (pillar 9)

**Section C. Composition patterns (the pipeline)**
13. Registry-driven composition over imperative step-chaining
14. Sibling-Π chorus + ArtifactByKind output
15. Pass return-type codification (`Lineage<Diagnostics<'output>>`)
16. Writer-fidelity (the dual-writer)
17. `Composition.fanOut` for registered-intervention pass drivers
18. Stream-realization pattern (A35/A36)

**Section D. Emission patterns (the boundary to text)**
19. Text-builder-as-first-instinct (typed AST always)
20. ScriptDom typed-AST emission with pinned `Sql160ScriptGenerator`
21. Per-pass DiagnosticEntry contract
22. Manifest catalog-only scope + V2-extension fields

**Section E. Cutover-discipline patterns**
23. Canary as load-bearing forcing function
24. Tolerance taxonomy for governed divergences
25. R6 split-brain governance + V2-driver flip gates
26. Stage 0 ships before chapter 3.1 opens

---

# Section A — Foundational disciplines

These are the meta-patterns that constrain every other pattern. Violations require an amendment to the corresponding load-bearing commitment in `CLAUDE.md` before code lands.

## 1. F#-pure-core / no-I/O-in-Core

**What.** `Projection.Core` has zero I/O. No `Async<'a>` / `Task<'a>` / file system / network / clock access. Strategies + passes + IR + diagnostics live here.

**When.** Always. The "I'm tempted to read a file from Core" instinct → that work belongs in an adapter at the boundary.

**How V2 uses it.** Core consumes typed values; adapters at the boundary do I/O. `Projection.Adapters.{Sql,Osm,OssysSql}` carry the I/O surface. `Projection.Pipeline/Deploy.fs` orchestrates I/O against the typed `seq<Statement>` Π output.

**Worked example.** `Projection.Core/Catalog.fs` (the IR root) — no async, no I/O. `Projection.Adapters.Osm/CatalogReader.fs:CatalogReader.parse` returns `Task<Result<Catalog>>` (the boundary).

**Counter-pattern.** Don't sneak I/O into Core via "just one file read for cached metadata." Audited clean at `CHAPTER_1_CLOSE.md §1.1`.

## 2. Type system as contract

**What.** The first place to encode a constraint is the type system, not a runtime check. Closed DUs make exhaustiveness compiler-checked. Identity types (`SsKey`, `Name`) refuse to confuse with strings.

**When.** Every value type whose invariants the type system can express.

**How V2 uses it.** Smart constructors returning `Result<'a>`; `[<RequireQualifiedAccess>]` on DUs whose case names risk collision (`NullabilityOutcome`, `UniqueIndexOutcome`); `option` for absence never null (`Nullable=enable` + `TreatWarningsAsErrors=true`); generic algebraic names (`Kind`, `Module`, `Catalog`) in Core; domain-prescriptive names at adapter translation.

**Worked example.** `Projection.Core/Identity.fs` — `SsKey` is a 4-variant DU (`OssysOriginal | Synthesized | Derived | V1Mapped`); compiler refuses to substitute a string. `Projection.Core/Types.fs` — `Name.create : string -> Result<Name>`.

**Counter-pattern.** Don't write runtime validation when the type system can enforce. V1's `StringValidators.RequiredIdentifier` (11 separate VOs with parallel runtime checks) collapses to V2's single `Name.create`.

## 3. Determinism is constructed, not validated

**What.** Sort by `SsKey` before scanning. Use `decimal` for continuous statistical evidence (never `float`/`double`). No `DateTime.Now` / `Random` / I/O in Core. T1 byte-determinism holds because every choice supports it.

**When.** Any output that operators consume (manifests / artifacts / decision logs).

**How V2 uses it.** Every emitter-consumable row source lands pre-sorted by SsKey or explicit row order; the `Bench` surface's accumulators sort by `TotalMs` desc deterministically; `SqlLiteral.create` validates literal text at construction so canonical rendering is byte-stable; `RegistryDigest` is a SHA256 of registered transform metadata producing reproducible signatures.

**Worked example.** `Projection.Core/Profile.fs:ColumnProfile.create` rejects `nullCount > rowCount`; degenerate cases accepted by construction; `StaticSeedsEmitter.emitWithTopo` consumes pre-sorted rows.

**Counter-pattern.** Don't add a post-hoc `Normalize` step after construction. V1's `ProfilingSnapshotNormalizer` (~runtime guard) → V2's by-construction smart constructors. Determinism is the construction property, not a verification step.

## 4. IR grows under evidence, not speculation

**What.** Types, fields, DU variants, and helpers land when a consumer demands them. Two-consumer threshold for helper extraction.

**When.** Designing any IR refinement. Adding a field "because V1 has it" is the failure mode.

**How V2 uses it.** Chapter A.0' slices added IR fields incrementally as consumer evidence surfaced (Description / OriginalName / ExternalColumnType / IndexFilter / IncludedColumns / etc.). The matrix's 🟠 NOT-MAPPED rows each name a concrete trigger; "we'll get to it" is the forbidden anti-pattern.

**Worked example.** `Projection.Core/Catalog.fs:Attribute` grew from ~6 fields at chapter 1 to ~21 fields by chapter 4.9 — each addition tied to a consumer (an emitter or pass) needing the evidence. `DECISIONS 2026-05-07 — IR grows under evidence, not speculation`.

**Counter-pattern.** Don't add `Attribute.OnDiskCollation : string option` because V1 has Collation in `AttributeOnDiskMetadata`. Wait until a V2 emitter or pass demands it.

## 5. Two-consumer threshold for emergent primitives

**What.** Extract a helper / primitive at the second consumer, not the first.

**When.** Considering whether to refactor common shape into a reusable primitive.

**How V2 uses it.** `Composition.fanOut` was codified after the third registered-intervention strategy (NullabilityPass / UniqueIndexPass / ForeignKeyPass) showed the same shape. `propagateOrFallback` in `CatalogReader.fs` codified at the two-consumer threshold for V1↔V2 build-failure collection. Helpers `fallback` / `accumulate` / `wrap` / `lift` are deferred until evidence forces them.

**Worked example.** `Projection.Core/Strategies/Composition.fs:Composition.fanOut` — three current consumers (the three sibling strategy passes); codified per `DECISIONS 2026-05-13 — Emergent primitives earn their place through multi-consumer demand`.

**Counter-pattern.** Don't extract a `genericPassDriver` helper at the first pass. V2 inlines the pass driver until evidence shows the pattern recurs.

## 6. V2 self-containment + V1 as editorial donor

**What.** V2 has zero runtime dependency on V1's trunk. No `ProjectReference`. No V1 assembly on V2's classpath. When V2 wants a V1 capability, V2 carbon-copies the V1 source files into V2's domain-structured locations, cites the V1 source in a file-header comment + an `ADMIRE.md` row, and refactors freely.

**When.** Considering whether to reference V1 code or port it.

**How V2 uses it.** The F# pure core wraps external libraries via F# adapters (`Microsoft.Data.SqlClient` via `Projection.Adapters.Sql`; ScriptDom via `Projection.Targets.SSDT`); a small museum-polish C# layer exists only where the gold-standard library is irreducibly C#-idiomatic (`Projection.Adapters.OssysSql` for SQL extraction).

**Worked example.** `Projection.Adapters.OssysSql/Resources/outsystems_metadata_rowsets.sql` — carbon-copy of V1's SQL; byte-identical to V1 source; gated parity test enforces equivalence when V1 trunk is present.

**Counter-pattern.** Don't add `<ProjectReference Include="..\..\..\src\Osm.Domain\Osm.Domain.csproj" />` to a V2 fsproj. Per `DECISIONS 2026-05-16 (later) — V2 self-containment + carbon-copy editorial inheritance`.

---

# Section B — Algebraic patterns

## 7. Closed DUs + smart constructors with `Result<'a>`

**What.** Closed discriminated unions for sums. Records for products. Every value type with non-trivial invariants carries a `create` returning `Result<'a>`.

**When.** Always for IR types. Open enums + try-parsers are anti-patterns.

**How V2 uses it.** `SsKey.original / synthesized / synthesizedComposite` return `Result<SsKey>`; `Name.create`, `CategoricalDistribution.create`, `NumericDistribution.create`, `NullabilityTighteningConfig.create` similarly. Downstream consumers pattern-match without re-validating; the invariant rides on every value.

**Worked example.** `Projection.Core/Profile.fs:ColumnProfile.create` — rejects `nullCount > rowCount`; `Result.value` unwraps in tests; production code trusts the value.

**Counter-pattern.** Don't validate values inside consumers. If a consumer needs to validate, the invariant belongs on the smart constructor.

## 8. Identity-vs-presentation split (SsKey + Name)

**What.** Identity is a typed surface (`SsKey`, 4-variant DU). Names (`Name`) are presentation-only. Core code never holds a string in a place where identity belongs.

**When.** Any time you'd reach for a string-keyed map.

**How V2 uses it.** `SsKey = OssysOriginal of guid | Synthesized of source × basis | Derived of original × reason | V1Mapped of v1SsKey × v2Namespace`. `Map<SsKey, Reference list>` for FK lookups; `Map<int, SsKey>` for cross-key-shape resolution. Names are display projections via `SsKey.rootOriginal`.

**Worked example.** `Projection.Core/Identity.fs:SsKey` + `Coordinates.fs:TableId` — TableId bundles schema + table; SsKey threads identity per A1. Per matrix row 45 (V2-EXTENSION over V1 int+Guid dual identity) + row 180 (consolidation of V1's 11 naming VOs).

**Counter-pattern.** Don't use `Dictionary<string, T>` where `Map<SsKey, T>` would do. V1's 11 separate naming VOs collapse to V2's single load-bearing SsKey.

## 9. Aggregate-root smart-constructor invariants (A39)

**What.** Aggregate roots (`Catalog`, `Module`, `Profile`) enforce referential-integrity + cardinality invariants in one pass at construction. Consumers that flow through `create` trust the value.

**When.** Any IR aggregate combining multiple sub-values.

**How V2 uses it.** `Catalog.create` enforces global Kind-SsKey disjointness + per-module owner consistency; `Module.create` enforces Kind-SsKey disjointness within module. `Profile.empty` is structurally valid by construction.

**Worked example.** `Projection.Core/Catalog.fs:Catalog.create` — A39 (per `DECISIONS 2026-05-28 — Session 34 / A39 cash-out`).

**Counter-pattern.** Don't validate aggregate invariants in consumers. V1's per-module non-empty Entity invariant lives in `ModuleModel.Create` (V1) — V2's equivalent should land in `Module.create` (matrix row 42 cash-out).

## 10. Typed-strategy with `StrategyEvaluator<'context, 'config, 'decision>`

**What.** Pure functions of IR fields. Typed function-type seam. Structured rationale DUs covering the decision space exhaustively. Lineage events on actual decisions. Module name advertises domain (`<Domain>Rules` suffix). Total decisions with named skips.

**When.** Any registered-intervention strategy producing per-element decisions.

**How V2 uses it.** `NullabilityRules.evaluate : (Attribute × NullabilityTighteningConfig × ColumnProfile option) -> NullabilityOutcome`; `UniqueIndexRules.evaluate`, `ForeignKeyRules.evaluate`, `CategoricalUniquenessRules.evaluate` follow the same shape. Strategies are synchronous; outcomes carry typed evidence (no string rationales).

**Worked example.** `Projection.Core/Strategies/NullabilityRules.fs:evaluate` — linear if-elif on attribute fields + profile evidence; returns one of three `NullabilityOutcome` variants. Per `DECISIONS 2026-05-11 — Strategy-layer codification: empirical verdict after the fourth instance`.

**Counter-pattern.** Don't build signal-combinator trees (V1's `AllOfSignal` + `AnyOfSignal` + `RequiresEvidenceSignal`). V2's typed-strategy with linear evaluation + structured outcomes is the canonical form per matrix row 64.

## 11. Ternary outcome space + named keep-reason variants

**What.** Every tightening strategy carries a third outcome variant `RequireOperatorApproval` carrying typed conflict evidence. Every non-tightening outcome carries a named keep-reason variant. F# exhaustiveness checking refuses to compile consumers that ignore variants.

**When.** Designing any strategy outcome DU.

**How V2 uses it.** `NullabilityOutcome = EnforceNotNull of NullabilityRationale | KeepNullable of KeepNullableReason | RequireOperatorApproval of NullabilityConflict`. Same shape applies symmetrically to `UniqueIndexOutcome` + `ForeignKeyOutcome`. Each keep-reason is named (`RelaxedUnderEvidence`, `OperatorOverride`, `LogicalReferenceWithoutDbConstraint`, `MissingTarget`, ...) — V1's silent-skip becomes V2's named structural commitment.

**Worked example.** `Projection.Core/Strategies/NullabilityRules.fs:NullabilityOutcome` — 3 variants with typed evidence per variant. `ForeignKeyRules.fs:ForeignKeyKeepReason.MissingTarget` is the corrected V1-bug case per matrix row 73 + `DECISIONS 2026-05-18 (slice 5.4.γ.evaluators)`.

**Counter-pattern.** Don't use a boolean (V1's `MakeNotNull : bool` + `RequiresRemediation : bool` + side-channel `Opportunity` record). Don't have a `DoNotEnforce of string` (lossy). Every contested decision goes to `RequireOperatorApproval` with typed conflict evidence.

## 12. Per-axis decision sets over per-column aggregation (pillar 9)

**What.** V2 emits three separate per-axis decision sets (`NullabilityDecisionSet` / `ForeignKeyDecisionSet` / `UniqueIndexDecisionSet`); consumers JOIN at the boundary by SsKey when per-column aggregation is needed. No per-column aggregator in Core.

**When.** Designing the output of any registered-intervention pass.

**How V2 uses it.** Each pass produces `Lineage<Diagnostics<DecisionSet>>`; the three pass outputs are independent; consumers (manifest emitter, future SummaryFormatter, future operator dashboard) JOIN at their boundary. The skeleton is axis-neutral; decisions layer atop as orthogonal overlays.

**Worked example.** `Projection.Core/Passes/NullabilityPass.fs:run` — produces `Lineage<Diagnostics<NullabilityDecisionSet>>`; the parallel `UniqueIndexPass.run` and `ForeignKeyPass.run` produce sibling outputs. Per `DECISIONS 2026-05-18 (slice 5.4.γ.evaluators)`.

**Counter-pattern.** Don't add a `ColumnAnalysis` aggregator with `(nullability × FK × uniqueIndex × risk × opportunities)` per-column tuple. V1's `ColumnAnalysisBuilder` conflates axes; V2 keeps them separate so pass independence becomes structurally testable.

---

# Section C — Composition patterns

## 13. Registry-driven composition over imperative step-chaining

**What.** The pipeline IS a registry. `RegisteredTransforms.allChainSteps` is a list of `PassChainAdapter` entries; `Compose.project` consumes via fold-and-bind. Identity is metadata; ordering is list position; implementation is the closure.

**When.** Any multi-pass orchestration.

**How V2 uses it.** `RegisteredTransforms.all` (17 entries; chapter A.4.7' axes 1-3 shipped) + `allChainSteps` (12 of them flow through the chain composer) + `skeletonChainSteps` (4 of them for skeleton-only projection per `osm emit --skeleton-only`). The registry is the cross-cutting structural-evidence concern sibling to Lineage / Diagnostics / Bench.

**Worked example.** `Projection.Core/RegisteredTransforms.fs:allChainSteps` + `PassChainAdapter.compose` + `Compose.project`. Per `DECISIONS 2026-05-18 (slice 5.6.α.orchestration) — Registry-driven composition over imperative step-chaining`.

**Counter-pattern.** Don't add a `BuildSsdtPipeline.HandleAsync` with 12 imperative `.BindAsync()` calls and DI-injected step classes. V2's registry decouples identity / ordering / implementation; V1's imperative chain couples them at source.

## 14. Sibling-Π chorus + ArtifactByKind output

**What.** Π's canonical output is a typed deterministic stream (`seq<Statement>` for SSDT) — but for emitters that produce multiple artifacts, the output is `ArtifactByKind<'element>` (a typed `Map<SsKey, Artifact>`). Sibling emitters (`Projection.Targets.{SSDT,Json,Distributions,OperationalDiagnostics}`) consume the same final `Catalog × Profile × Policy` triple independently.

**When.** Designing emitter outputs that have multiple per-Kind artifacts.

**How V2 uses it.** `SsdtDdlEmitter.emit` returns `ArtifactByKind<SsdtFile>`; `JsonEmitter.emit` returns per-Kind JSON; `DistributionsEmitter.emit` returns per-Kind statistical artifacts. The realization layer (`Render.toSsdtDirectory`, `Deploy.executeStream`) consumes the map and chooses emission form (file write, network stream, etc.).

**Worked example.** `Projection.Core/ArtifactByKind.fs` — the typed map shape. `Projection.Targets.SSDT/SsdtDdlEmitter.fs:emit` consumes Catalog, produces `ArtifactByKind<SsdtFile>`.

**Counter-pattern.** Don't have a monolithic `SsdtEmitter.EmitAsync` that writes files synchronously. The Π produces the typed map; realization consumes it. Per A35 / A36.

## 15. Pass return-type codification (`Lineage<Diagnostics<'output>>`)

**What.** Passes return `Lineage<'output>` when they produce only decisions; `Lineage<Diagnostics<'output>>` when they produce decisions plus observer-relevant findings. The shape names the production.

**When.** Designing any pass return type.

**How V2 uses it.** `NullabilityPass.run : Catalog -> Policy -> Profile -> Lineage<Diagnostics<NullabilityDecisionSet>>` — decisions + diagnostics. `TopologicalOrderPass.run : Catalog -> Lineage<TopologicalOrder>` — decisions only. The two-shape codification means readers infer the pass's purpose from the signature.

**Worked example.** `Projection.Core/Passes/NullabilityPass.fs:run` — the canonical dual-writer shape. Per `DECISIONS 2026-05-13 — Pass return-type codification`.

**Counter-pattern.** Don't return a tuple `(Decision list, Diagnostic list)`. Don't return `Result<DecisionSet, Diagnostics>` (errors are not diagnostics). The writer-monad composition flows; tuples lose the monadic discipline.

## 16. Writer-fidelity (the dual-writer)

**What.** Pass drivers MUST use `LineageDiagnostics.tellDiagnostics` / `Lineage.ofValueAndEvents` (the canonical primitives); manual record-building is forbidden.

**When.** Inside any pass driver emitting both decisions + diagnostics.

**How V2 uses it.** Every pass goes through the canonical writer primitives; the dual-writer's algebraic surface is the load-bearing composition discipline; future pass drivers inherit by following the canonical pattern.

**Worked example.** `Projection.Core/Passes/NullabilityPass.fs` uses `LineageDiagnostics.tellDiagnostics` per opportunity emission; `Lineage.ofValueAndEvents` for the per-decision lineage. Per `DECISIONS 2026-05-30 — Session 36 / Writer-fidelity codification`.

**Counter-pattern.** Don't manually build `{ Payload = decisions; Events = lineageEvents; Diagnostics = diagnostics }`. Use the writer primitives — they enforce A24 (chronological-trail-under-bind) + A25 (every transform runs inside Lineage<_>).

## 17. `Composition.fanOut` for registered-intervention pass drivers

**What.** All registered-intervention pass drivers delegate to `Composition.fanOut` over `(context × intervention)` pairs.

**When.** Designing a registered-intervention pass.

**How V2 uses it.** `Composition.fanOut : FanOutConfig<'context, 'config, 'decision> -> Lineage<Diagnostics<'decision list>>` — the canonical pass-driver primitive. NullabilityPass, UniqueIndexPass, ForeignKeyPass, CategoricalUniquenessPass all delegate.

**Worked example.** `Projection.Core/Strategies/Composition.fs:Composition.fanOut`. The two-consumer threshold was met at the fourth consumer (`DECISIONS 2026-05-11 — Strategy-layer codification: empirical verdict after the fourth instance`).

**Counter-pattern.** Don't write a custom pass driver. If you need a registered-intervention pass, `fanOut` is the primitive.

## 18. Stream-realization pattern (A35/A36)

**What.** Π's canonical output is a typed deterministic statement stream. Realization layers consume the stream and choose their emission form (file, network, in-memory). The algebra (A18 / T1 / T11) holds at the stream level; bulk-vs-incremental deploy is realization-layer policy invisible to Π.

**When.** Designing any output emitter.

**How V2 uses it.** `SsdtDdlEmitter.statements : Catalog -> seq<Statement>` (chapter-3.1 contribution; canonical form). Realization layers consume:
- `Render.toText : seq<Statement> -> string` for file output
- `Deploy.executeStream : seq<Statement> -> SqlConnection -> Task<Result<unit>>` for live deployment

The `Bulk.copyRows + Deploy.executeStream` realization folds consecutive `InsertRow` runs into `SqlBulkCopy` batches — same algebra, different emission.

**Worked example.** `Projection.Pipeline/Deploy.fs:executeStream` — consumes typed Statement stream + SqlConnection. Per `DECISIONS 2026-05-28 — Session 34 / A35 cash-out` + `Session 34 / A36 cash-out`.

**Counter-pattern.** Don't pre-render to a string and write the string to disk. Don't have an emitter that knows it's writing to a file vs network vs database. The emitter produces the stream; realization consumes.

---

# Section D — Emission patterns

## 19. Text-builder-as-first-instinct (typed AST always)

**What.** Every new SQL- or text-emitting consumer starts on the typed-AST library, not `StringBuilder`. Before the first `StringBuilder()` / `String.Concat` / `sprintf`, articulate the typed-AST library that produces the structure being emitted. First draft uses the typed AST. LINT-ALLOWs at terminal text boundaries only.

**When.** Designing any text-emitting consumer.

**How V2 uses it.** ScriptDom for T-SQL (`ScriptDomBuild.buildCreateTable` / `buildCreateIndex` / `buildSetExtendedProperty` / `buildMergeStatement`). `Utf8JsonWriter` / `JsonNode` for JSON. `XmlWriter` / `XDocument` for XML (when needed). `Microsoft.SqlServer.Dac` for .dacpac.

**Worked example.** `Projection.Targets.SSDT/ScriptDomBuild.fs:buildCreateTable` — constructs `CreateTableStatement` typed AST; delegates to `Sql160ScriptGenerator` for rendering. Per `DECISIONS 2026-05-10 — Text-builder-as-first-instinct discipline` (worked counterfactuals: chapter 3.7 slice β shortcut + StaticSeedsEmitter slices α/β shortcut; both retired via ScriptDom typed AST).

**Counter-pattern.** Don't reach for `StringBuilder` then attach LINT-ALLOWs once the lint surfaces. Each LINT-ALLOW is individually defensible; the aggregate is the bug (the typed-AST migration was never attempted in the first place). The named failure mode: **text-builder-as-first-instinct**.

## 20. ScriptDom typed-AST emission with pinned `Sql160ScriptGenerator`

**What.** All T-SQL emission goes through ScriptDom typed-AST builders + pinned `Sql160ScriptGenerator` options (square-bracket quoting, semicolon terminators, capitalized keywords, deterministic spacing).

**When.** Designing any T-SQL output.

**How V2 uses it.** Builders in `ScriptDomBuild.fs` construct typed statements; `ScriptDomGenerate.fs:pinnedOptions` carries the canonical generator config; `Sql160ScriptGenerator` renders deterministic text. Filter-parse uses `TSql160Parser` (SQL Server 2022 compat).

**Worked example.** `Projection.Targets.SSDT/ScriptDomBuild.fs:buildCreateIndex` (lines 752-821) — handles columns + sort + INCLUDE + WHERE clause via `tryParseFilterWithDiagnostics` + index options. `ScriptDomGenerate.pinnedOptions` ensures byte-deterministic output. Per `DECISIONS 2026-05-18 (slice 5.3.α.smo) — Schema emission via ScriptDom typed-AST over SMO scripter`.

**Counter-pattern.** Don't use SMO. Don't use `Sql100ScriptGenerator` (loses SQL Server 2022+ features). Don't read script-generator options from config (the options are pinned for determinism).

## 21. Per-pass DiagnosticEntry contract

**What.** `DiagnosticEntry = { Source : string; Severity : Severity; Code : string; Message : string; SsKey : SsKey; Metadata : Map<string, string> }`. Source = `pass:<passName>` / `adapter:<name>` / `emitter:<name>`. Code = dot-separated routing prefix (`tightening.nullability.relaxedUnderEvidence`). Severity gated per Outcome variant. Metadata carries only non-structural values (typed structure stays on the Outcome).

**When.** Emitting any diagnostic from a pass / adapter / emitter.

**How V2 uses it.** Pass drivers emit via `LineageDiagnostics.tellDiagnostics`; the DiagnosticEntry's Source identifies the producer; Code routes by domain; Severity classifies (Info / Warning / Error); SsKey points to the IR node; Metadata supplements with non-typed context (interventionId + numeric thresholds for prose rendering).

**Worked example.** `Projection.Core/Passes/NullabilityPass.fs:opportunityEntry` (lines 117-170) — emits DiagnosticEntry with typed Outcome variant (RelaxedUnderEvidence / RequireOperatorApproval) + typed SsKey + Metadata for prose context. Per `DECISIONS 2026-05-18 (slice 5.4.γ.opportunities) — Per-pass DiagnosticEntry contract`.

**Counter-pattern.** Don't duplicate typed structure in Metadata. Don't use a tightening-specific record type (V1's `TighteningDiagnostic` is purpose-built for tightening; V2's generic DiagnosticEntry covers passes + adapters + emitters). Don't use a centralized log object (V1's `PipelineExecutionLog`); per-pass attribution via Source is more discoverable.

## 22. Manifest catalog-only scope + V2-extension fields

**What.** Manifest = catalog-only (per A18 amended — emitters consume Catalog × Profile, never Policy). The manifest carries 6 fields: Tables + EmitterVersion + RegistryDigest + Coverage + PredicateCoverage + Unsupported. PreRemediation emitted as `[]` per V2_DRIVER §154; Options + PolicySummary deferred (operator-config + policy-projection are NOT manifest concerns in V2).

**When.** Designing or extending the manifest emission surface.

**How V2 uses it.** `Projection.Targets.OperationalDiagnostics/ManifestEmitter.fs:buildWith` consumes registry + catalog; produces typed `Manifest` record; renders to JSON via explicit `JsonObject` construction (camelCase keys). `TableManifestEntry` carries cardinality (IndexCount + ForeignKeyCount) not name lists — names live in per-table DDL files; manifest is summary, DDL is detail.

**Worked example.** `Projection.Targets.OperationalDiagnostics/ManifestEmitter.fs` — full shape per chapter 4.4 close + `DECISIONS 2026-05-18 (slice 5.5.α.manifest) — V1-differential walk: manifest scope-reduction with V2-extension fields; TableManifestEntry: counts over name-lists`.

**Counter-pattern.** Don't carry Policy or operator-config in the manifest (V1's Options + PolicySummary fields). Don't carry index/FK name lists in the manifest (V1's TableManifestEntry name fields). The manifest summarizes the catalog; the DDL files carry detail.

---

# Section E — Cutover-discipline patterns

## 23. Canary as load-bearing forcing function

**What.** The canary's PhysicalSchema round-trip diff against an OutSystems-shaped source DDL is V2's primary wide integration surface. Empty diff = structural fidelity holds.

**When.** Every commit. Every Stop hook. Every chapter close.

**How V2 uses it.** Tiers:
- Schema-only canary (`fixtures/canary-gate.sql`, ~1.5s warm) on SessionEnd hook (operator-confidence smoke)
- Generator-scale canaries (`Generator bulk: 1k/10k/100k rows/table` in `GeneratorScaleTests`)
- Operator-reality canary (`50k rows × 300 tables, variegated`; ~10-12s warm) — production-shape baseline; per-commit + per-Stop-hook gate (`scripts/perf-gate.sh`)
- Realistic 300-table canary gated behind `PROJECTION_RUN_REALISTIC_CANARY` env var (nightly)

**Worked example.** `Projection.Pipeline/Deploy.fs:runWithReadback` — deploys source to ephemeral DB, reads back via ReadSide, runs V2's emitter on the reconstruction, deploys to a second DB, reads back, asserts source ≈ target on PhysicalSchema. Per `DECISIONS 2026-05-23 — Source SQL Server with OutSystems semantics`.

**Counter-pattern.** Don't add a new emission feature without a canary assertion. Don't ship a "we'll verify later" promise — the canary IS the verification.

## 24. Tolerance taxonomy for governed divergences

**What.** Every V2↔V1 divergence either matches a named `Tolerance` variant or fails the canary. The Tolerance enumeration is the governance surface; widening the closed DU is a deliberate choice that requires a DECISIONS entry.

**When.** When the canary surfaces a V1↔V2 byte-diff that's structurally principled (not a regression).

**How V2 uses it.** `Projection.Core/Tolerance.fs:Tolerance` — closed DU of accepted divergences (NormalizeWhitespace, IgnoreHeaderComments, CommentMetadataUnreflected — retired chapter 4.1.A slice 8 — and others as evidence forces them).

**Worked example.** Chapter 4.1.A slice 8 retired the `CommentMetadataUnreflected` Tolerance variant when extended-property emission shipped (the divergence ceased to exist; the variant retires per closed-DU empirical-test discipline).

**Counter-pattern.** Don't add `if v1Style then ...` branches to V2 emitters. Don't add fuzzy-comparison logic to the canary. Every divergence is either a Tolerance (named, governed) or a bug (fix it).

## 25. R6 split-brain governance + V2-driver flip gates

**What.** V2 emits-but-doesn't-ship while V1 owns the production write path. The canary asserts V1 ≈ V2 modulo named tolerances; disagreement blocks the PR. Per-environment-per-artifact-type V2-driver transition is gated on **N=10 consecutive green canary runs + operator sign-off**. The four-environment cutover stays per-pair; the gate flips when its evidence supports the flip.

**When.** Always. R6 is the governance discipline for the dual-track cutover window.

**How V2 uses it.** The canary's PhysicalSchema diff against V1's emission is the structural test. The R6 flip happens per (environment × artifact-type) pair, not globally. V1 stays warm through cutover+30 regardless.

**Worked example.** Per `DECISIONS 2026-05-22 — R6: Split-brain governance rule for the dual-track cutover window`.

**Counter-pattern.** Don't flip V2-driver mode without N=10 green canary runs. Don't flip globally — per-environment-per-artifact-type granularity. Don't shut down V1 on flip — V1 warm through cutover+30.

## 26. Stage 0 ships before chapter 3.1 opens

**What.** The twelve foundation items (S0.A–S0.L per `STAGING.md`) are codified before any chapter-3 slice opens. Tier 1 is documentation hygiene + governance burst; Tier 2 is the type-primitives keystone (SsKey + identity); Tier 3 is the structural-commitment refactor; Tier 4 is primitive support modules in parallel.

**When.** Designing any foundation work (axioms, identity, primitives, configuration).

**How V2 uses it.** Per `DECISIONS 2026-05-22 — Stage 0 foundation phase ships as one coherent unit`. The chapter-1 baseline (631 passing tests) holds at every Stage 0 step.

**Worked example.** S0.A (Identity.fs / SsKey 4-variant DU) + S0.B (structural-commitment refactor: smart constructors over all VOs) + S0.E (Tolerance taxonomy) all shipped as one Stage 0 burst.

**Counter-pattern.** Don't open chapter 3.1 with Stage 0 incomplete. Don't refactor foundations mid-chapter. The contract precedes its instances (per SPINE inference I6).

---

# Quick-reference: pattern selection

When designing a new V2 component, run through this quick-reference:

| Designing... | Apply patterns |
|---|---|
| A new IR field | 4 (IR grows under evidence) + 7 (closed DU + smart constructor) |
| A new identity type | 7 + 8 (SsKey-style; never a string) |
| A new aggregate root | 7 + 9 (A39 invariants in `create`) |
| A new pass | 10 (typed-strategy) + 11 (ternary outcome) + 12 (per-axis decision set) + 15 (return-type codification) + 16 (writer-fidelity) + 17 (fanOut) |
| A new emitter | 14 (sibling-Π) + 18 (stream-realization) + 19 (text-builder) + 20 (ScriptDom) + 21 (DiagnosticEntry) |
| A new pipeline orchestrator | 13 (registry-driven) + 15 (return-type) |
| A new manifest field | 22 (manifest scope) + 4 (evidence-driven) |
| A new V1-translation slice | 6 (carbon-copy + ADMIRE) + 4 (evidence-driven; don't speculate) |
| A new CLI verb | per slice 5.7.α.cli — config-driven defaults; CLI flags override; cutover-critical surface only at launch |
| A divergence from V1 | 24 (Tolerance) — name it; if structurally principled, DECISIONS entry |

---

# Worked patterns NOT in this compendium (deferred / partially-shipped)

The chapter 5 audit wave identified several patterns V2 will adopt when their triggers fire but hasn't shipped yet:

- **Polly retry policy on transient SqlExceptions** (matrix row 34; cutover-critical for cloud OSSYS)
- **`Profile.AttributeReality` reflection statistics carrier** (matrix row 49; OperatorIntent evidence axis)
- **`Catalog.Triggers` IR (already shipped) + trigger DDL emission** (matrix row 129; chapter 4.2 / 5+)
- **`projection compare <left> <right>` CLI verb** (matrix row 41; closed-DU `DiffSource` over the V1 `IDmmLens<TSource>` interface)
- **`PostDeployTemplateEmitter` sibling Π** (matrix row 140)
- **`Render.toSqlProject` realizer** (matrix row 141; `.sqlproj` MSBuild emission)
- **`RemediationEmitter` sibling to ManifestEmitter** (matrix row 83; V2_DRIVER §154 chapter 5+)
- **`SummaryFormatter` consumer** (matrix row 81; bucket-wise prose from Diagnostics + DecisionSet)
- **`SpectreProgressAdapter` on Bench iterator-samples** (matrix row 118; CLI TUI)

Each is named with cash-out shape + dependencies + trigger in the matrix. When the trigger fires, this compendium gains a new pattern entry.

---

# Closing

V2's patterns are not constraints — they are the load-bearing structure that lets each chapter ahead support more weight than the one behind.

The 26 patterns in this compendium emerged from the work, not from up-front design. Each pattern earned its place through repeated worked-example pressure; each carries a counter-pattern documenting what V2 chose NOT to do, and why.

When a new chapter opens, the agent's first task is to recognize which patterns apply — and which patterns the new work might add. Pattern additions land here when the work codifies them.

The cross-reference structure:
- `CLAUDE.md` — points at this compendium for pattern selection
- `DECISIONS.md` — codifies the discipline each pattern instantiates
- `V1_ARCHITECTURE_COMPENDIUM.md` — names what V1 carries that these patterns either preserve, refine, or sunset
- `V1_PARITY_MATRIX.md` — the row-level evidence trail
- `CUTOVER_READINESS_BRIEF.md` — synthesizes the matrix into per-axis confidence

— Compendium opened 2026-05-18 at chapter 5 audit-wave close.
