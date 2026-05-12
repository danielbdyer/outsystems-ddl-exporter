# DECISIONS

Append-only log of resolved questions during V2 development. Each entry
captures the decision, the reasoning, and (where applicable) the axiom or
theorem it serves. Future readers — including future agents and Danny —
should be able to reconstruct context from this log without spelunking
commit history.

---

## ⭐ Supreme operating discipline (read first; supersedes all other intents per session)

**Per the user's chapter-3.5 sidebar (2026-05-09)** — the following
disciplines are the *ultimate goal* and **supersede most other intents
for each and every session that follows**. Every agent confirms intent
against these before adopting any pattern.

1. **Data-structure-oriented over string-parsing.** Carry typed values
   through the pipeline. Strings emerge ONLY at the absolute terminal
   boundary (BCL writers — `Utf8JsonWriter`, `XmlWriter`,
   `Sql160ScriptGenerator` — or final text output). No intermediate
   string-construction; no string-parsing-back-into-data.
2. **Avoid string concatenation aggressively.** `sprintf`, `+`,
   `String.Format`, AND `System.String.Concat` are all flagged by the
   lint guardrail. Even the V2-internal `StructuredString` builder is
   itself a stopgap; agents must confirm that their intent is NOT
   better served by a built-in BCL method or typed data structure
   before reaching for it.
3. **Built-in obligation.** When a BCL or vendor SDK emits the
   structure being emitted, agents are *obliged* to use it
   (`Sql160ScriptGenerator` for T-SQL, `XmlWriter` for XML,
   `Utf8JsonWriter` / `JsonNode` for JSON, `UuidV5` for RFC 4122
   GUIDs, `DataContractSerializer` / `JsonSerializer` for typed
   round-trips, etc.).
4. **Promised land of FP.** ≥95% of functions are pure; the remaining
   ≤5% (mutation-justified algorithms, BCL-mandated mutable surfaces,
   reified non-determinism boundaries) is *isolated and tested
   exhaustively*. Mutation reified at the file level via
   `LINT-ALLOW-FILE-MUTATION`; "really test the heck out of" applies
   structurally — property tests + parse-roundtrip + byte-determinism.
5. **Coding-style commitments (preserved across all sessions):**
   - **Deep DDD** — bounded contexts, value objects with smart
     constructors, aggregates with referential-integrity invariants,
     ubiquitous language reified in types.
   - **Point-free composition** — pipeline-style (`|>`, `>>`) over
     parameter-named where the shape reads as well or better.
   - **Hexagonal architecture** — Core ⟵ Adapters/Targets ⟵
     Pipeline ⟵ CLI; one-way dependency direction; lint-enforced
     per Rules 20/21/22.
   - **Hardcore FP** — closed DUs, smart constructors with
     `Result<'a>`, pattern-match exhaustiveness, no `null`,
     immutability by default, monadic composition over imperative
     accumulation.
   - **OOP where appropriate** — at adapters/boundary code where BCL
     surfaces force it (e.g., `SqlBulkCopy`, `Utf8JsonWriter`'s mutable
     options); reified as `LINT-ALLOW-FILE-MUTATION`.
   - **Deep separation of concerns** — Core has no I/O / no time /
     no Policy in Π / no concurrency primitives.
   - **Verifiable + observable to the nth degree** — property tests
     (parse-roundtrip, determinism, exhaustiveness, idempotence,
     permutation-invariance), structured lineage trails, structured
     diagnostic emission, bench-driven optimization with three-
     candidate / 2-refuted / 1-confirmed shape.
6. **No V2-internal back-compat paths — refactor fully at time of
   insight, no exceptions** (codified 2026-05-09 chapter 3.6 sidebar).
   V2 is pre-production; nothing in V2 needs to preserve a prior V2
   surface. Back-compat shims, "legacy" markers, "pre-stratification"
   parsers, and aliasing forwarders within V2 are tech debt and must
   be eliminated at the moment they are discovered. **The exception
   is V1↔V2 bridging:** when a path is genuinely about reading V1's
   output / preserving V1's identity / integrating with V1's
   admire-spectrum surface (per `ADMIRE.md`), it is not back-compat
   — it is the V1-bridge contract, and lives explicitly under the
   `V1*` / `Ossys*` / `LiveOssysConnection` named surfaces. Anything
   else: refactor it out now, including all test-fixture callers.
   Per the supreme operating discipline pillars 1–5: typed builders
   beat parser shims; closed-DU exhaustiveness beats string-prefix
   detection; the type system is the contract. The cost of the
   refactor at insight is paid once; the cost of carrying the shim
   compounds across every reader.
7. **Gold-standard library precedence** (codified 2026-05-09 chapter
   3.6 sidebar; refines pillar 3 `Built-in obligation`). When a site
   constructs / parses structured output, the precedence is:
   1. **Use-case-specific library** (`Sql160ScriptGenerator`,
      `TSql160Parser`, `XmlWriter`, `Utf8JsonWriter`, `JsonNode`,
      `Path.Combine`, `SqlConnectionStringBuilder`,
      `Identifier.EncodeIdentifier`, `UuidV5`, BCL parsers/formatters
      with `CultureInfo.InvariantCulture`, `DacFx.TSqlModel` when it
      lands). **Gold standard.**
   2. **Typed data structures** (closed DUs, records, typed lists)
      that carry the structure through; strings emerge only at the
      absolute terminal boundary.
   3. **`StructuredString`** (V2's typed AST builder for diagnostic
      prose). The third-tier preferred form.
   4. **Anything else** requires a per-site `LINT-ALLOW: <rationale>`
      marker that critically considers the alternatives (alternatives
      considered + why chosen). The marker IS the provenance log.
   Each adoption of `String.concat` / `String.Concat` / `String.Join`
   / `String.Format` / `sprintf` / `+` / `$"…"` outside the gold
   standard requires a documented justification. The lint guardrail
   enforces; the rationale text in the marker is the audit trail.
   **Bias toward gold-standard library adoption when one exists** —
   even at the cost of an extra package dep. Per the user's
   "be bold" directive (2026-05-09): expensive-now-for-cheaper-later
   beats compounding tech debt.

   **Pillar-7 substantive-rationale amendment** (codified 2026-05-10
   chapter 3.7 sidebar; named after the slice-β failure mode). Every
   `LINT-ALLOW` marker on a string-composition / built-in-substitute
   site MUST embody the four-question analysis BEFORE the marker is
   committed:
   1. **Use-case-specific library** for THIS output structure?
      Name it explicitly (module + type + function).
   2. **Already in codebase** (or available as non-V2-back-compat
      dep)? If yes, name the existing consumer site; if no, name
      the package + version.
   3. **Cost** of using it here? Visibility lift (LOC) + perf class
      (zero / O(1) / O(N) / ...) + dep weight.
   4. **Structural reason it doesn't apply?**
      - **NO** → there is no shortcut; do the work (lift visibility,
        add helper, refactor call site).
      - **YES** → the marker text MUST name the specific reason —
        NOT generic vocabulary alone ("typed segments", "boundary"
        without naming WHICH boundary, etc.).

   The named failure mode is **performance-of-compliance**: a marker
   with the SHAPE of an audit trail without the substance. The lint
   passes, the vocabulary fits, the tests are green — and the
   structural commitment is unmet. See `DECISIONS 2026-05-10 —
   LINT-ALLOW substantive-rationale discipline` for the worked
   counterfactual (slice-β `Render.columnSqlType` shortcut → slice-β'
   ScriptDom delegation; cost of doing it right was 87 LOC).
   See `PLAYBOOK.md` decision tree "When you reach for a
   string-composition primitive" for the executable form.

   Lint Rule 27 maintains a per-line concat-aversion `LINT-ALLOW`
   inventory printed at the end of every clean run AND enforces a
   soft floor (≥30 chars after the colon, at least one substantive-
   vocabulary token). Heuristics can't catch performance-of-
   compliance reliably; the discipline document does. The inventory
   is the audit surface for chapter-close ritual + PR review.

   **Pillar-7 perf-clause** (added 2026-05-09 chapter 3.6 sidebar
   reminder; iterator-logging-as-first-class-outcome): every
   refactor SHALL cite its perf implications in the commit message.
   Every new hot-path function SHALL have a `Bench.scope` at entry.
   Every loop / iteration / lazy-stream pull over scaling data
   SHALL flow through `Bench.iterDo` / `iterMap` / `iteriDo` /
   `streamProbe` / `streamTransit`. Every external counter (bytes
   emitted, rows bulk-copied, events accumulated, cache hit/miss,
   etc.) SHALL surface via `Bench.recordSample`. The bench rollup
   table is V2's structural perf-evidence surface; it is the
   canary-scale early-warning system for the cutover. Agents
   adopting a new pattern SHALL identify the perf class before
   committing (zero / O(1) / O(N) / O(N log N) / O(N²) — including
   the scaling axis). The perf-gate (`scripts/perf-gate.sh`) runs
   on every pre-commit; per-label regressions vs
   `bench/baseline-canary.json` block the commit. Audit `Bench`
   coverage at every chapter close.

The lint guardrail (`scripts/lint-discipline.sh`) is the structural
enforcement of these disciplines: **default to explicit
acknowledgement of deviance**. Every legitimate exception carries a
`LINT-ALLOW` marker (per-line) or `LINT-ALLOW-FILE` /
`LINT-ALLOW-FILE-MUTATION` marker (top-of-file) with rationale.
Bypassing via `git commit --no-verify` is the explicit-deviance
escape hatch; CI catches bypasses on PR.

Agents starting a new session: read this section first. Confirm intent
against each pillar before adopting any new pattern. Where in doubt,
defer to the discipline; where the discipline forbids what's
ergonomically tempting, write the DECISIONS amendment first.

---

Format:

    ## YYYY-MM-DD — short title
    **Status:** decided
    **Context:** ...
    **Decision:** ...
    **Reasoning / consequences:** ...

Entries are append-only. Earlier entries are preserved as historical
artifacts even when later entries refine or supersede them. Where an
earlier decision is amended, the amendment names the prior entry by date
and title rather than rewriting it.

---

## Active deferrals — index

Deferred decisions with explicit trigger conditions. The chapter-close
audit (session 12) found one trigger had fired silently (transform
registry, N=10 with N≥4 deferral); session 13 cashed it out and
introduced this index so the failure mode does not recur. Future agents
scan this section before committing to substantive work; if a trigger
has fired since the last review, log the cash-out entry below the
table before continuing.

| Deferral | First logged | Trigger condition | Status (current; session-N tag indicates last update) |
|---|---|---|---|
| **Composition primitive `fallback`** | 2026-05-13 (Composition vocabulary cash-out) | A second strategy returns "no decision" / Defer outcome and another picks up | 0 consumers (session 25) |
| **Composition primitive `accumulate`** | 2026-05-13 (Composition vocabulary cash-out) | A second pass needs to consume multiple-strategy decisions at once | 0 consumers (session 25) |
| **Composition primitive `wrap`** | 2026-05-13 (Composition vocabulary cash-out) | Per-strategy diagnostics emerge (likely tied to Diagnostics writer) | 0 consumers (session 25) |
| **Composition primitive `lift`** | 2026-05-13 (Composition vocabulary cash-out) | A strategy reused across different IR granularities (e.g., Nullability rule on view columns) | 0 consumers (session 25) |
| **Strategy registry mechanism** | 2026-05-11 (Strategy layer: a named architectural vector) | N≥4–6 strategies make name-keyed lookup useful | 5 strategy modules (UniqueIndexRules / NullabilityRules / ForeignKeyRules / CategoricalUniquenessRules / CycleResolution); Composition is the primitive module, not a strategy. No caller demands lookup by name (session 25) |
| **Diagnostics writer** | 2026-05-06 (Diagnostics live in a writer parallel to Lineage) | First downstream artifact gates on operator-channel telemetry | **Cashed out — session 14 commit 3 landed `Projection.Core/Diagnostics.fs`. UniqueIndex opportunity stream activated as first consumer (session 14 commit 5). Three-channel split (operator/auditor/developer) remains deferred until a real consumer demands differentiation.** |
| **`RequireQualifiedAccess` retrofit** on `UniqueIndexKeepReason` / `ForeignKeyKeepReason` / similar | 2026-05-11 refinement 1 (Strategy-layer codification empirical verdict) | A DU's variants change shape (added/removed/renamed) — substantive structural modification, not interpretive resolution | `ForeignKeyKeepReason` got `MissingTarget` (session 19) and the unreachable-`DeleteRuleIgnored` interpretive resolution (session 19; `DECISIONS.md:5048` rule 13). Neither rose to "structural modification" warranting retrofit; trigger sharpened at session 25 to clarify the threshold. Today neither `UniqueIndexKeepReason` nor `ForeignKeyKeepReason` carry the attribute (session 25) |
| **`CycleResolution.ResolutionStep.Reason` migration to structured DU** | 2026-05-11 (Strategy layer: a named architectural vector — caveat) | A second resolver strategy lands per the 2026-05-08 pluggability deferral | No second resolver; reason field still free-form string (session 25) |
| **Cross-catalog FK detection IR refinement** (`Catalog : string option` on `Reference` and `ForeignKeyKeepReason.CrossCatalogBlocked` made reachable) | 2026-05-13 (Closed-DU expansion: empirical confirmation) | A fixture exercising cross-catalog FKs surfaces the gap | Reserved DU variant exists but is unreachable; do not delete (session 25) |
| **Cross-module FK IR refinement** | 2026-05-19 (rule 16's same-module assumption — session 19 reference-bearing slice) | A fixture exercising cross-module FK surfaces a gap **at the emit layer that topological ordering cannot satisfy** (refined trigger condition; see status). | **Trigger fired and partially satisfied (chapter 4.1.A close arc, 2026-05-10).** Chapter 4.1.A's enterprise canary fixture exercises cross-module FKs (PRODUCT.CATEGORYID, ORDER.CUSTOMERID, ORDERLINE.PRODUCTID, audit FKs to IDM.USER). The fixture deploys clean: `SsdtDdlEmitter.statements` uses `TopologicalOrderPass.runWith SkipSelfEdges` so FK targets emit before referencers regardless of module membership. **The IR refinement (adding cross-module distinction at the `Reference` type) remains deferred** pending a use case where topological ordering at the emit layer is insufficient — e.g., a DacpacEmitter `SqlSchemaModel` cross-database reference that needs schema-disambiguation metadata, or an OssysOriginal catalog spanning multiple V1 SS instances. Cross-module FK same-module assumption (rule 16) at the OSSYS adapter rowset path also remains the operational shape (chapter 3.2 same-module fixtures all round-trip); the gap is at the IR, not the adapter (session 41 reframe; first noted at `CHAPTER_4_1_A_CLOSE.md:80-85`). |
| **Faker emitter (synthetic-data Π)** | 2026-05-13 (Session 11 reflection) | Either a third evidence type lands, or a use case forces proceeding with two evidence types and accepting the limitations | Two evidence types operational (Categorical, Numeric); no third in scope (session 25) |
| **DacFx integration in `Projection.Targets.SSDT.DacpacEmitter`** | 2026-05-06 (DacFx integration deferred to first real-fixture milestone) | A real Catalog (from any adapter) flows end-to-end through a pipeline exercising sibling-Π commutativity (T11) on real metadata; canary chapter (`Projection.Pipeline`) is the natural locus | **Cashed out — chapter 3.x open (2026-05-11) reframes the trigger condition.** Production deploy path stays SSDT-style file deploy (`SsdtDdlEmitter.emitSlices`); DacpacEmitter ships as the **dev-tooling sibling-Π emitter** for local one-click stand-up per operator directive. See `2026-05-11 — Chapter 3.x DacpacEmitter open` entry below. |
| **Multi-spine state pattern** | 2026-05-06 (Multi-spine state pattern is endorsed but not yet built) | A real use case surfaces in the algebra | None yet (session 25) |
| **Three-channel Diagnostics split** (operator / auditor / developer) | 2026-05-06 (Diagnostics live in a writer parallel to Lineage) | A real downstream consumer demands per-channel routing | **Retired at chapter 4.3 open (2026-05-11).** Decision: refuse the split. The three V1 artifacts (`decision-log.json` / `opportunities.json` / `validations.json`) ARE the three channels — descriptive of *what is being emitted*, not of *who consumes it*. The existing `Diagnostics<'a>` writer remains single-channel; routing happens at emit time via the `Code`-prefix table consumed by `DecisionLogEmitter` (chapter 4.3 slice α) + `OpportunitiesEmitter` (slice β) + `ValidationsEmitter` (slice γ). No `DiagnosticChannel` DU; no parallel writer. Three artifacts route from one stream. See `CHAPTER_4_3_OPEN.md` §"Retiring the three-channel Diagnostics split deferral" + `2026-05-11 — Chapter 4.3 open` entry below. |
| **Reflection** (`typeof<>`, attribute scanning for plugin discovery) | Session 14 (CLAUDE.md, F# feature surface — consciously deferred) | A real consumer demands name-keyed strategy dispatch (paired with the strategy registry mechanism deferral above) | Closed-DU + typed-seam dispatches at compile time today; no reflective discovery needed (session 25) |
| **Object expressions** (`{ new IInterface with ... }`) for adapter-side abstractions | Session 14 (CLAUDE.md, F# feature surface — consciously deferred) | V2 grows interface-based polymorphism (e.g., `IDiagnosticSink` for streaming consumers; `ICatalogReader` after a second catalog source materializes) | Codebase has zero interface boundaries today; all polymorphism via DU pattern matching (session 25) |
| **Type providers** (`JsonProvider` for `osm_model.json`) | Session 14 (CLAUDE.md, F# feature surface — consciously deferred) | OSSYS adapter ships and JSON-shape evolution becomes a maintenance burden | OSSYS adapter ships at session 18 with hand-written DTOs; JSON-shape evolution has not yet surfaced as a burden (session 25) |
| **`ICatalogReader` interface** (Position B → A) | 2026-05-13 (Anticipation vs. speculation in abstraction extraction) | A second *catalog* source materializes (DACPAC, OData, in-memory test reader; distinct from a second *variant* of an existing OSSYS source) | **Reaffirmed at chapter 3.2 open (axis 6) / close (2026-05-10).** `SnapshotRowsets` is a second *variant* of the OSSYS source, not a second source — per `CHAPTER_3_2_OPEN.md` axis 6, the Position B → A trigger does NOT fire. `ReadSide.read` is a profile/data reader, not a catalog reader (out of scope). The trigger condition was sharpened at chapter 3.2 close to make the variant-vs-source distinction explicit. OSSYS adapter still uses `parse : SnapshotSource -> Task<Result<Catalog>>` (Position B); interface lift remains deferred until DACPAC / OData / in-memory test reader materializes (chapter 3.7 slice ξ candidate; chapter-3.x DacpacEmitter may surface the second source). |
| **`SnapshotRowsets` variant of `SnapshotSource`** | 2026-05-17 (OSSYS adapter parse signature, session-20 amendment) | The JSON-projection-lossiness class needs unblocking — A1 SsKey bound resolution; `EspaceKind` distinction; `isSystemEntity` evidence; future class members (per `DECISIONS 2026-05-19 — naming the two classes of resolution patterns explicitly`) | **Cashed out — chapter 3.2 (commits 6dab9cd / 0354727 / d5d1812 / 6eae21f / a74b904). Variant implemented end-to-end across five slices: (1) SnapshotRowsets variant + RowsetBundle DTO + SsKey at all three levels; (2) reference rowsets (`#RefResolved + #FkReality`); (3) `EspaceKind` activation (Origin three-way real); (4) `IsSystemEntity` → `ModalityMark.SystemOwned`; (5) cross-source parity tests. A1's JSON-projection-lossiness bound resolved structurally (`OssysOriginal` Guid carriage). Three class members landed: SsKey at every level; EspaceKind; IsSystemEntity. Future class members (per-table column structure rowset 6; check constraints rowset 7; triggers rowset 18 — documented not-carried-forward) surface under fixture pressure as further deferred slices. See cash-out entry `2026-05-10 — SnapshotRowsets variant chapter 3.2 close` below.** |
| **`LiveOssysConnection` variant of `SnapshotSource`** | 2026-05-17 (OSSYS adapter parse signature) | V2 needs to operate without V1's chain in the loop entirely (real DB-touching variant) | Reserved in `SnapshotSource` DU (`CatalogReader.fs:58-62`); chapter-3+ when canary's deployment-validation arc materializes (session 25) |
| **`Microsoft.SqlServer.Dac` (DacFx) adoption in `Projection.Targets.SSDT.DacpacEmitter`** | 2026-05-10 (Tier-3 codification: text-builder-as-first-instinct discipline) | Chapter 3.x DacpacEmitter opens. **Hard requirement, not preference**: the .dacpac file format is a Microsoft-proprietary ZIP-with-manifest-XML structure — hand-rolling it via `System.IO.Compression.ZipArchive` + manual XML composition is the prototypical "text-builder-as-first-instinct" failure mode. DacFx (`Microsoft.SqlServer.Dac` NuGet) IS the canonical use-case-specific library; per pillar 7, no LINT-ALLOW will excuse a hand-rolled .dacpac. The chapter-3.x agent reads this entry at chapter open and writes the cash-out below the Active deferrals table on adoption. | **Cashed out — chapter 3.x slice α (2026-05-11) adopts `Microsoft.SqlServer.DacFx` v162.x in `Projection.Targets.SSDT`.** Pure F# wrapper (empirical condition: `use TSqlModel`, `model.AddObjects`, `DacPackageExtensions.BuildPackage` — four `IDisposable`-aware calls F# handles natively). No C# subproject; pre-scope §6.2 bias yielded under empirical pressure. See `2026-05-11 — Chapter 3.x DacpacEmitter open` entry below. |
| **MigrationDependenciesEmitter + BootstrapEmitter typed-AST adoption from slice α** | 2026-05-10 (Tier-3 codification: text-builder-as-first-instinct discipline) | Chapter 4.1.B slices ε (MigrationDependenciesEmitter) / ζ (BootstrapEmitter) open. **Hard requirement**: both emitters are MERGE / INSERT producers; per the Tier-1 #1 cash-out (`bface9a` — chapter 4.1.B StaticSeedsEmitter MERGE → ScriptDom MergeStatement), every new SQL-emitting consumer starts on the typed-AST library, not StringBuilder. `ScriptDomBuild.buildMergeStatement` + `ScriptDomBuild.buildInsertRow` + `SqlLiteral.ofRaw` are the precedent surface; cross-target dep on Projection.Targets.SSDT acceptable per the StaticSeedsEmitter precedent (single-line LINT-ALLOW with rationale). The chapter 4.1.B slice agent reads this entry at slice open. | **Cashed out — chapter 4.1.B slice ε (commit `0aa3761`) + slice ζ (commit `9544006`).** MigrationDependenciesEmitter ships the typed-AST MERGE + UPDATE shape mirroring StaticSeedsEmitter; BootstrapEmitter ships as structural stub (no SQL emission today; UserRemapContext slot pending chapter 4.2). See `CHAPTER_4_1_B_CLOSE.md` and `2026-05-11 — Chapter 4.1.B close` entry below. |
| **Statement DU MERGE/UPDATE promotion** | 2026-05-11 (Chapter 4.1.B close) | A third MERGE/UPDATE consumer lands (e.g., chapter 3.x DacpacEmitter Phase-2 path; future Faker-style data emitter; future Profile-attached row source in chapter 4.3). Today `Projection.Core.Statement` carries SSDT DDL variants only (CreateTable / CreateIndex / InsertRow / SetIdentityInsert / Comment / Blank); MERGE + UPDATE are emitted directly via `ScriptDomBuild.buildMergeStatement` / `buildUpdateStatement` + `ScriptDomGenerate.generateOne` + LINT-ALLOW'd `;\nGO\n` text suffix. 6 LINT-ALLOWs total across StaticSeedsEmitter + MigrationDependenciesEmitter at terminal text concatenation boundaries. Promoting `Statement` to include `Merge of MergeBuildArgs \| Update of UpdateBuildArgs` lets `ScriptDomGenerate.toText` handle per-kind concat structurally + retire all 6. Cross-target lift required (MergeBuildArgs / UpdateBuildArgs from Projection.Targets.SSDT to Projection.Core). | Two consumers today (StaticSeeds + MigrationDeps); deferred per two-consumer threshold. See `2026-05-11 — Chapter 4.1.B close` entry. |
| **Sort-vs-data deferral predicate distinction** | 2026-05-11 (Chapter 4.1.B close) | A third cycle-metadata consumer surfaces with a sibling-but-distinct semantic question (beyond sort-edge breakability + data-emission deferral). The two predicates diverge on Cascade-nullable FKs: `CycleResolution.classify` returns `Cascade` (NOT `Weak`) so sort-edge-breaker refuses to break; `<Emitter>.deferredColumns` DOES defer (Cascade is about DELETE; column is nullable). The two-predicate split is codified explicitly so future emitter agents choose the predicate that fits their semantic question. | Two predicates today (CycleResolution.classify + StaticSeedsEmitter.deferredColumns / MigrationDependenciesEmitter.deferredColumns); discipline codified. See `2026-05-11 — Chapter 4.1.B close` entry. |
| **OSSYS adapter User-kind identification surface** | 2026-05-11 (Chapter 4.2 close) | A real OSSYS-source-V2-target reflow workflow surfaces with User-FK columns operators need rewritten. At cash-out time the OSSYS adapter gains a `userKindIdentity : Catalog -> SsKey option` resolution surface (per V1 reference `ModelUserSchemaGraphFactory.GetSyntheticUserForeignKeys`); references whose `TargetKind` matches the identified user kind get `IsUserFk = true`. Slice η emitter integration (MigrationDependenciesEmitter rewrite path) is structurally complete today; the gap is at the adapter boundary only. | OSSYS adapter currently sets `IsUserFk = false` for every Reference; slice η rewrite is operationally a no-op until the adapter resolves real User-FKs. See `2026-05-11 — Chapter 4.2 close` entry. |
| **CSV adapter for `ManualOverride` (UserMapLoader)** | 2026-05-11 (Chapter 4.2 close) | A real operator workflow demands the file-format pickup path. Pre-scope §3 names `Projection.Adapters.UserMap.UserMapLoader` (CSV: `SourceUserId,TargetUserId,Rationale`). Slice ε ships `ManualOverride` consuming a programmatic `Map<SourceUserId, TargetUserId>`; I/O adapter at the boundary is deferred. Mirrors the chapter 4.1.B slice ε NDJSON-adapter deferral — sibling chapter, same shape. | No I/O adapter today; ManualOverride works via programmatic construction. See `2026-05-11 — Chapter 4.2 close` entry. |
| **`Attribute.Default` field + DEFAULT constraint emission (chapter 4.1.A slice 7-default)** | 2026-05-11 (Chapter 4.1.A slices 6/7/8 disposition) | The SnapshotRowsets adapter (chapter 3.2) surfaces default-constraint columns from `sys.default_constraints` (the rowset variant materializes default expressions per column). Pre-scope §8 slice 7 names the IR widening (`Attribute.Default : string option`) + emission of `CONSTRAINT [DF_<Table>_<Col>] DEFAULT (<expr>)`. **107+ Attribute literal-construction sites** would need updating with `Default = None` under the record-extension empirical-test discipline; deferred per IR-grows-under-evidence until the rowset adapter surfaces real defaults. | `Tolerance.IgnoreDefaultNames = false` per pre-scope §4 line 214 documents the comparator's current acceptance posture; no consumer demands the field today. Slice 7's identity portion (`Attribute.IsIdentity`) shipped at chapter 3.1/3.2; only the default-constraint portion is deferred. |
| **`Kind.Description` + `Attribute.Description` fields + extended-properties emission (chapter 4.1.A slice 8)** | 2026-05-11 (Chapter 4.1.A slices 6/7/8 disposition) | The SnapshotRowsets adapter surfaces description columns (`MS_Description` extended properties) from `sys.extended_properties`. Pre-scope §8 slice 8 names the IR widening (`Kind.Description : string option` + `Attribute.Description : string option`) + emission of `EXEC sys.sp_addextendedproperty @name=N'MS_Description', ...` statements per V1's `ExtendedPropertyScriptBuilder.cs:91-95`. **107+ Attribute literal-construction sites** + Kind literal-construction sites would need updating with `Description = None`; deferred per IR-grows-under-evidence until the rowset adapter surfaces real descriptions. | `Tolerance.IgnoreExtendedProperties = true` per pre-scope §4 line 213 documents the comparator's current acceptance posture; no consumer demands the field today. The V1↔V2 differential test treats extended-property absence as a deliberate divergence. |

**Discipline.** Each deferral here was logged as the right call **at the
time it was made** under "IR grows under evidence." A deferral is not a
TODO — the cash-out point is a structural condition, not a date. The
table tells future agents which conditions to monitor; the discipline is
to review the table when surveying CHAPTER_CLOSE-ranked priorities, when
adding a strategy or pass, and at chapter close. If a trigger has fired
silently between reviews, the audit-during-validation discipline expects
a cash-out entry before substantive work continues — that is the lesson
the transform-registry miss surfaced (`DECISIONS 2026-05-13 — Transform
registry cash-out`).

**Scope of the index.** This index lists **deferrals with explicit
re-open triggers** — both architectural (composition primitives, IR
refinements, registry mechanisms) and feature-surface (reflection,
object expressions, type providers consciously deferred per the
CLAUDE.md F# feature surface section). Both share the same shape: a
deferred decision with a structural condition that, when met, requires
a cash-out entry. The index does **not** list:

  - **Adoption-trigger candidates** from CLAUDE.md's "Aligned but
    underused" section (computation expressions, active patterns,
    units of measure). Those are aspirational adoption signals, not
    re-open obligations — adopting them is encouraged when the
    trigger fires but the trigger firing does not by itself demand
    a cash-out entry. They live in CLAUDE.md as guidance, not in
    DECISIONS as load-bearing.
  - **Out-of-scope-for-Core** features (Async/Task,
    MailboxProcessor, FRP). Those are scope rules, not deferrals —
    they are forbidden in Core regardless of demand and only land
    in adapters when the adapter's role demands them. They live
    in CLAUDE.md as scope guidance.

This distinction matters: the Active deferrals index is the list
the chapter-close audit must scan; aspirational guidance and scope
rules don't need that level of attention.

---

## 2026-05-06 — Sidecar lives at `sidecar/projection/` with its own solution

**Status:** decided
**Context:** The trunk's main solution is `OutSystemsModelToSql.sln`; the
sidecar must be cherry-pick friendly and leave trunk behavior unchanged
whether the sidecar is present or absent.
**Decision:** Place all sidecar code under `sidecar/projection/`. Create a
separate `Projection.sln` inside that folder. Do **not** add sidecar
projects to the trunk solution.
**Reasoning / consequences:** A separate solution is the cleanest way to
honor the cherry-pick discipline and keep the trunk's build/CI surface
untouched. Cost: developers must `cd sidecar/projection/` (or pass the
sidecar's `.sln` path) to build and test the sidecar.

## 2026-05-06 — F# is introduced for the algebraic core

**Status:** decided
**Context:** The trunk is 100% C#. The architecture spec mandates F# for
the pure functor pipeline so that "the source code reads as the formal
system" and discriminated unions are first-class.
**Decision:** F# for `Projection.Core` and all enrichment passes. C# for
adapter projects later (`Projection.Adapters`, `Projection.Host`). The
algebra/I-O boundary is also the language boundary.
**Reasoning / consequences:** The DU and pipeline-composition idioms in F#
make the algebra visible at call sites. C# adapter code stays minimal and
speaks .NET libraries (DacFx, Hot Chocolate, ASP.NET) natively. Cross-
language values are F# records and discriminated unions, consumable from
C#.

## 2026-05-06 — Property testing via FsCheck.Xunit

**Status:** decided
**Context:** The trunk uses xUnit but no property-based testing library.
The axioms list literally tells us what properties to write; we need a
property-based test framework.
**Decision:** Add `FsCheck.Xunit` to the test project. Hedgehog is nicer
ergonomically but FsCheck has deeper .NET tooling integration and a longer
track record.
**Reasoning / consequences:** One new test-time dependency. Production
dependencies remain unchanged.

## 2026-05-06 — Lineage shape: hand-rolled writer with versioned events

**Status:** decided
**Context:** Every pass runs inside a lineage monad. Library writers
(FSharpPlus etc.) introduce dependency surface and obscure the algebra.
Custom writer keeps it visible. Per A23, lineage events must include
`PassVersion` to make provenance hashes stable across pipeline evolution.
**Decision:**

```fsharp
type Lineage<'a> = { Value: 'a; Trail: LineageEvent list }
and LineageEvent = {
    PassName    : string
    PassVersion : int
    SsKey       : SsKey
    TransformKind : TransformKind
}
```

`bind f m` produces `{ Value = f(m.Value).Value; Trail = m.Trail @
f(m.Value).Trail }` — earliest-first, chronological (A24). The `>>=`
operator and the computation expression both follow this order.
**Reasoning / consequences:** Hand-rolled, no external deps. Versioning
guarantees replay determinism. Chronological order is documented in code,
in `AXIOMS.md` (A24), and tested.

## 2026-05-06 — SsKey as a sum type: Original vs Derived

**Status:** decided
**Context:** Some passes (e.g., symmetric closure) introduce nodes whose
identities are derived from existing identities. These derivations must be
deterministic and machine-checkable, not stringly conventional.
**Decision:**

```fsharp
type SsKey =
    | Original of string
    | Derived of original: SsKey * reason: string
```

The original/derived distinction is in the type system; later passes can
pattern-match on whether a key was synthesized. The string-concat
alternative (e.g., `"::inv::"` separator) is rejected as too easy to bypass
and too hard to reason about under composition.
**Reasoning / consequences:** Slight type-system overhead; large
correctness payoff. Reserved `reason` strings will be enumerated as a
`DerivationReason` DU when more than one is in use; for now we use string
literals registered in this document. First reserved reason: `"inverse"`
(symmetric closure pass).

## 2026-05-06 — Π_SSDT first emission target is raw .sql-style text

**Status:** decided
**Context:** Building real SSDT artifacts requires DacFx interop and a real
SQL Server target; that is heavy machinery for the first milestone.
**Decision:** For the synthetic-fixture milestone, Π_SSDT emits external-
table declarations as text (diffable, dependency-free).
**Forward-looking note:** When real SSDT artifacts are required, use
**DacFx** (`Microsoft.SqlServer.DacFx`), not SMO. SMO is for live SQL
Server administration; DacFx is the SSDT-native tooling for DACPAC
construction and schema comparison. SMO will get close enough to be
misleading. **Do not go down the SMO path** for SSDT.

## 2026-05-06 — Static populations live in the catalog

**Status:** decided (per spec; logged for visibility)
**Context:** The unfold pass for static kinds needs row data. Two options:
keep rows in the catalog, or maintain a parallel data layer.
**Decision:** Per A7, row data lives in the catalog. The Catalog Reader
populates `staticPopulation` when reading from real meta; the unfold pass
lifts populations into type-level metadata for Π.
**Caveat parked:** Real populations may eventually be large or carry non-
trivial values. The algebra is indifferent; the implementation may want
bounded loading later. Do not pre-solve.

## 2026-05-06 — PhysicalRealization starts as `{ Schema; Table }`

**Status:** decided
**Context:** External kinds may eventually point at different
databases/servers, view-backed entities may need view metadata, and
identity-column overrides may be needed.
**Decision:** For the first milestone, `PhysicalRealization = { Schema:
string; Table: string }`. Widen when evidence forces it.

## 2026-05-06 — Result and ValidationError are F# ports of trunk shapes

**Status:** decided
**Context:** The trunk has a well-shaped `Result<T>` + `ValidationError` in
`src/Osm.Domain/Abstractions/`. Algebraic purity benefits from F# DUs.
**Decision:** F# port: `type Result<'a> = Success of 'a | Failure of
ValidationError list`. `ValidationError` is a record with `Code` (e.g.,
`"sskey.empty"`), `Message`, `Metadata` map. `Bind`, `Map`, `Ensure`
operators preserved. Error codes follow the trunk's
`"category.subject.problem"` lower-dot convention.

## 2026-05-06 — V2 mandate (supersedes the sidecar framing)

**Status:** decided
**Context:** What was originally framed as a self-contained sidecar
experiment is elevated to V2 of the codebase. V2 is the foundation V1
will eventually orbit around via an admire-and-extract migration. The
folder path stays at `sidecar/projection/` to preserve cherry-pick
history; the conceptual frame upgrades.
**Decision:** The pure F# core under `sidecar/projection/` is V2's spine.
V1 (the existing C# implementation in the rest of the trunk) continues to
operate untouched. V2 is additive. Each V1 component will eventually be
admired (read carefully, categorized algebraically, placed) and extracted
(brought into V2 as a pure pass, an adapter at a port, or a split
between the two).
**Reasoning / consequences:** README is reframed. AXIOMS.md gains a V2
Amendments section (preserving the original A1–A31 / T1–T10 numbering).
ADMIRE.md is created as the append-only log of V1 admirations and their
V2 placements.

## 2026-05-06 — UAT-Users dual-mode collapses to "passes feed emitters"

**Status:** decided
**Resolves:** Q1 from the V2 handoff reply.
**Context:** V1's UAT-Users transform is dual-mode: discovery in Stage 3,
application in either Stage 5 (INSERT pre-transform) or Stage 6 (UPDATE
post-load). The masterwork frames this as a special case; the algebraic
spec was silent on it. The decomposition flagged it as a tension point.
**Decision:** Discovery is one E-pass producing a `UserRemapContext`
value attached to the enriched IR. Application is two sibling Π's: an
INSERT-mode Π that consumes catalog + context to emit pre-transformed
INSERTs, and an UPDATE-mode Π that consumes the context alone to emit
standalone CASE-WHEN UPDATEs. Both Π's read the same enriched IR; each
chooses which subset of attached values it consumes.
**Reasoning / consequences:** The dual-mode framing collapses to a
canonical algebraic principle: **passes may produce values consumed by
emitters, not just by other passes.** This becomes new axiom A32. It is
the canonical answer for any future "discover something at one stage,
use it at another" pattern.

## 2026-05-06 — Policy is three orthogonal axes

**Status:** decided
**Resolves:** Q2.
**Context:** The decomposition's three-dimensional decomposition
refinement (Selection / Emission / Insertion) and the masterwork's
bounded-context partitioning pointed at the same structure. The
algebraic spec had Policy as a single opaque aggregate.
**Decision:** Policy is a structured F# record with three named axes:

```fsharp
type Policy = {
    Selection : SelectionPolicy
    Emission  : EmissionPolicy
    Insertion : InsertionPolicy
}
```

Each axis is its own value type with its own validation. The three are
orthogonal: changing one does not constrain the others. AXIOMS.md
amendment to A12 records the structural commitment.
**Reasoning / consequences:** The three axes become the canonical place
to ask "where does this configuration belong?" Type system makes the
axes discoverable and resists drift.

## 2026-05-06 — Diagnostics live in a writer parallel to Lineage

**Status:** decided
**Resolves:** Q3.
**Context:** The masterwork prescribes three diagnostic channels
(operator / auditor / developer) orthogonal to domain logic.
**Decision:** `Lineage<'a>` remains the foundational, content-addressable,
constitutive provenance writer. A separate `Diagnostics<'a>` writer
(name TBD) carries human-consumable telemetry; near-term it is
single-channel, structured emission. The three-channel split arrives
when a real consumer asks for differentiated output.
**Reasoning / consequences:** Lineage and diagnostics have different
lifetimes, consumers, verbosity, and audiences. Conflating them would
either bloat lineage with operator-text or force diagnostics into
lineage's structural constraints.

## 2026-05-06 — Profile is a third substantive input

**Status:** decided — real algebraic amendment
**Resolves:** Q3.5.
**Context:** Both the decomposition and the masterwork demand profile
evidence (null counts, orphan FK rates, uniqueness violations) feed
policy decisions. The original "three aggregates, only three" framing
(A6) cannot absorb this — Profile is empirical evidence, distinct from
structural truth (Catalog) and operator intent (Policy).
**Decision:** Amend the algebra. The system has **three substantive
inputs** — Catalog, Policy, Profile — and **one temporal dimension** —
Lifecycle. Together they fully determine the projection.

```fsharp
type ProjectionInput = {
    Catalog : Catalog
    Policy  : Policy
    Profile : Profile  // may be Profile.empty for use cases needing no evidence
}
```

`E : (Catalog, Policy, Profile) → EnrichedCatalog`. Passes that do not
need profile evidence ignore it and behave identically as if the input
were `Profile.empty`. Passes that need it (the eventual nullability and
FK evaluators) accept it as a parameter.
**Reasoning / consequences:** A6 is amended. A12 (policy as data)
remains intact but composed alongside Profile. A17 (Project = Π ∘ E) is
amended to specify E's signature. T1 (determinism) extends to the
triple. New axiom A34 names Profile's independence from Catalog and
Policy. AXIOMS.md records all four amendments.

## 2026-05-06 — General names in the pure core; V1↔V2 mapping at the boundary

**Status:** decided
**Resolves:** Q4.
**Context:** V1 uses domain-prescriptive names (`EntityModel`,
`ModuleModel`, `OsmModel`) the masterwork calls "ontological law." The
algebra is source-agnostic and uses general names (`Kind`, `Module`,
`Catalog`).
**Decision:** Pure core uses general algebraic names. Boundary
adapters translate. The mapping is documented in the README and
preserved here for cherry-pick context:

| V1 (`Osm.Domain`)   | V2 (`Projection.Core`) | Notes                          |
|---------------------|------------------------|--------------------------------|
| `OsmModel`          | `Catalog`              | top-level aggregate            |
| `ModuleModel`       | `Module`               | coproduct cell                 |
| `EntityModel`       | `Kind`                 | the schema-level entity type   |
| `AttributeModel`    | `Attribute`            | scalar property                |
| `RelationshipModel` | `Reference`            | directional FK edge            |
| `EntityName`        | wrapped in `SsKey`     | identity, type-distinguished   |
| `TableName`         | `PhysicalRealization`  | physical projection            |
| `ProfileSnapshot`   | `Profile`              | empirical evidence             |

**On identity (`SsKey` vs `EntityName`):** V2 `SsKey` wraps whatever V1
supplies as canonical identity. For OutSystems, that is `EntityName` —
the logical name, stable across rename-to-database. It is **not**
`PhysicalTableName`, which can change when the database is migrated.
The principle is portable: identity is whatever survives the source's
most aggressive refactoring. When DACPAC support arrives, identity is
DACPAC's most stable identifier, wrapped in `SsKey`.
**Reasoning / consequences:** Algebra in the core stays clean. The
mapping is a forcing function for future translations: when DACPAC
support arrives, the question "how does DACPAC's `TableDefinition` map
to V2's `Kind`?" has a place to live. If the mapping grows large, it
graduates to its own `MAPPING.md`.

## 2026-05-06 — Transform registry deferred until N≥4 passes

**Status:** decided
**Resolves:** Q5.
**Context:** The masterwork prescribes a TransformRegistry with explicit
ordering constraints, discoverability, and startup validation. Today V2
has one pass.
**Decision:** Continue with `>>` composition for the next two or three
passes. Graduate to the registry when:
- composition with `>>` starts to feel fragile, or
- ordering rationale begins accumulating in code comments rather than
  in types, or
- `N` (number of passes) reaches four.

Migration when it arrives is mechanical: each pass becomes a
`RegisteredTransform`, ordering constraints get declared, the
composition site changes from `pass1 >> pass2 >> pass3` to a registry
configuration.
**Reasoning / consequences:** The registry's complexity earns its keep
when the load is there. Until then, `>>` composition is more legible.

## 2026-05-06 — Schema vs Data ordering law promoted to A33

**Status:** decided
**Context:** Masterwork §14 (lines 946–956) prescribes that schema
emission uses deterministic (alphabetical) ordering, while data emission
uses topological (FK-dependency-safe) ordering. The algebraic spec was
silent on this distinction.
**Decision:** Promote the rule to a V2 axiom (A33). The F# type system
encodes it: a schema-emission configuration cannot accept a topological-
order input, and vice versa. AXIOMS.md records the law. Implementation
arrives when the first emission pass that takes ordering as input does.
**Reasoning / consequences:** Schema artifacts must produce reproducible
diffs (alphabetical ordering survives every refactor). Data artifacts
must respect FK constraints (topological ordering prevents reference
violations). Mismatching the two is a class of subtle bugs the type
system can prevent at compile time.

## 2026-05-06 — Multi-spine state pattern is endorsed but not yet built

**Status:** decided (deferred implementation)
**Context:** Masterwork §15 (lines 795–949) prescribes multiple typed
"spines" (`ExtractionSpine`, `SchemaSpine`, `FullPipelineSpine`) so that
different use cases consume only the stages they need, without bloating
one mega-state.
**Decision:** Bless the pattern as the V2 framing for use-case-specific
state types. Build spines as evidence demands — not in the next handful
of commits, but when the algebra needs to express a use case where
"there is no Profile" or "there is no Apply" is a structural fact.
**Reasoning / consequences:** Avoids type bloat. Allows the type
system to prevent illegal compositions (an extract-model spine cannot
reach Apply). Held against premature spine-explosion: build one spine
first; introduce a second when the first one starts collecting optional
fields that are mandatory only in one mode.

## 2026-05-06 — Built-ins first; no hand-rolled serialization

**Status:** decided (operating discipline)
**Context:** Session 2 introduced `Projection.Targets.Json.JsonEmitter`
as a hand-rolled string-concat serializer. This was a shortcut: F# has
multiple real JSON options (built-in `System.Text.Json.Utf8JsonWriter`,
`FSharp.SystemTextJson`, `Thoth.Json.Net`), and reinventing serialization
adds risk (escaping, encoding, ordering corners) without algebraic
benefit. Caught and called out in review.
**Decision:** Default to built-in libraries for I/O / serialization /
parsing concerns. Hand-rolling is justified only when the algebra
demands a representation no library exposes (e.g., a deliberate canonical
text form for visual diffing, like the SSDT raw-text emitter — see the
adjacent decision). When in doubt, log the choice in DECISIONS.md
*before* writing the code.
**Reasoning / consequences:** The pure core stays small. Library code
absorbs the corners we don't need to own. Future agents read this entry
and avoid the same shortcut. The discipline is named explicitly so it
can be invoked on review without re-litigating each instance.

## 2026-05-06 — Π_Json now uses System.Text.Json.Utf8JsonWriter

**Status:** decided
**Resolves:** the hand-rolled JSON regression flagged in review.
**Context:** `JsonEmitter.fs` shipped in commit 5 as hand-rolled string
concatenation with bespoke UTF-8 escaping.
**Decision:** Rewrite using `System.Text.Json.Utf8JsonWriter` (built-in
to .NET, no third-party dep). Property order is the order of writes
(stable). Pretty-print uses `Indented = true` plus an explicit
`NewLine = "\n"` so output is byte-deterministic across platforms (T1).
**Reasoning / consequences:** Less code, fewer bugs, no escaping
corners we don't want to own. `JsonEmitter.version` bumped to 2 to
distinguish hand-rolled (v1) from library-backed (v2) output in any
already-cached snapshots. Existing tests survive the format change
because they assert structural properties (parseable, contains roots,
modality is a JSON array) rather than byte-exact form.

## 2026-05-06 — DacFx integration deferred to first real-fixture milestone

**Status:** decided (refines the 2026-05-06 — Π_SSDT decision)
**Context:** The same review that flagged hand-rolled JSON asked why
Π_SSDT does not use DacFx now. Honest answer: Π_SSDT raw text is the
algebraic claim made human-legible (T1 byte-determinism is eyeballed; T8
diffability of snapshots is read directly). DacFx-built `.dacpac`
artifacts are zip archives of XML schemas: legible only through DacFx
itself. The raw-text emitter is doing real work as a debug oracle.
**Decision:** Keep `Projection.Targets.SSDT.RawTextEmitter` as a
debug / diff-oracle sibling Π. When the first real-fixture milestone
arrives (session 3+, when the C# Catalog Reader admits real OutSystems
metadata), add `Projection.Targets.SSDT.DacpacEmitter` as a third sibling
Π built on `Microsoft.SqlServer.DacFx`. Tests at that point assert
sibling-functor commutativity (T4 / T11) across all three: same
enriched IR ⇒ identity-consistent surfaces in raw text, JSON, and
DACPAC bytes.
**Reasoning / consequences:** No premature dependency on DacFx; no
giving up the human-readable diff oracle. The migration is additive —
real-fixture work introduces DacFx alongside, not replacing, the raw-
text emitter.

#### 2026-05-20 (session 24 amendment) — trigger fired silently across sessions 18–22; cash-out is a re-defer with a tighter trigger condition

The session-23 chapter-mid-audit (subagent #2; see
`DECISIONS 2026-05-19 — Chapter-mid-audit as a routine practice`)
surfaced that this deferral's trigger condition has been
empirically satisfied without a cash-out entry being written. The
original trigger condition reads "first real-fixture milestone
arrives via the OSSYS catalog adapter." The OSSYS catalog adapter
shipped at session 18 (`Projection.Adapters.Osm/CatalogReader.fs`)
and operated across five real fixtures (minimal, reference-bearing,
external-entity, mixed-active, index-bearing) producing 23+
translation rules across sessions 18–22. The structural condition
is met; seven sessions have accumulated against a satisfied
trigger.

This is the same shape as the transform-registry miss the Active
deferrals index was created to prevent — a structural condition has
been satisfied, no cash-out entry has been written, and substantive
work has accumulated. The audit-during-validation discipline expects
a cash-out before the next substantive work. This amendment is that
cash-out.

**The trigger as originally written was scoped imprecisely.** "First
real-fixture milestone arrives via the OSSYS catalog adapter" reads
naturally as "OSSYS adapter exists with real fixtures," which is now
true. But the original 2026-05-06 entry's substantive intent — read
in context of the surrounding decisions about RawTextEmitter as
debug oracle and DacpacEmitter as the third sibling Π built when
real metadata flows through — is tighter: the milestone that opens
DacpacEmitter work is a real Catalog (from OSSYS) feeding through
the **emitter chain**, not just the **adapter** ingesting OSSYS
metadata. The OSSYS adapter parses to a Catalog; nothing yet
consumes that Catalog through Π emitters in a real-fixture
end-to-end shape. The chapter that opens DacpacEmitter work is
the canary chapter (`Projection.Pipeline` — see
`DECISIONS 2026-05-15 — OSSYS adapter strategic frame`, axis 4),
which is sequenced for chapter 3.

**Re-defer with tighter trigger condition.** DacpacEmitter remains
deferred. The new trigger condition: **a real Catalog (from any
adapter — OSSYS, DACPAC, in-memory) flows end-to-end through a
pipeline that exercises sibling-Π commutativity (T11) on real
metadata.** The canary chapter (`Projection.Pipeline`) is the
natural locus. When that chapter opens substantive deployment-arc
work, this trigger fires and DacpacEmitter implementation lands.

**Why this amendment matters beyond the bookkeeping.** The audit
surfaced not just this individual trigger fire but a structural
gap in how the index discipline operates: an agent ships work that
satisfies a deferral's structural condition without scanning the
index for fired triggers. The chapter-mid-audit codification
(`DECISIONS 2026-05-19`) added at session 23 catches pointer drift
but does not yet require active-deferrals-scanning as an explicit
audit dimension; the session-24 refinement amends that. The two
amendments — this cash-out and the chapter-mid-audit refinement —
are paired: they together close the structural gap the audit
surfaced.

**Update to the Active deferrals index.** The DacpacEmitter row's
status updates from "OSSYS catalog adapter itself not yet built"
(stale since session 18) to "**Re-deferred at session 24 with
tighter trigger condition** — sequenced for canary chapter
(`Projection.Pipeline`); see this amendment for rationale." The
trigger condition column updates to reflect the tightened
scoping.

## 2026-05-07 — Contract testing is the V1↔V2 bridge

**Status:** decided (operating discipline)
**Context:** V1's existing tests encode behavioral contracts empirically
— "given X, the implementation produces Y." When V2 implements
equivalent functionality through F# passes and Π emitters, those tests
become the validation that the migration is faithful. The algebra
explains *why* the tests should pass; the tests confirm V1 and V2 are
equivalent compositions on the migrated subset.
**Decision:** Every V1→V2 migration uses one or more of three contract-
testing forms:

1. **Differential / golden-file.** Run V1 and V2 against shared
   fixtures; compare outputs. Strongest possible evidence; appropriate
   when the output shapes match (e.g., both emit textual SQL).
2. **Property-based.** Lift V1's example-based assertions into
   universally-quantified F# properties. Both implementations are
   obligated to satisfy them; FsCheck runs against V2 and accumulates
   confidence with every fuzz case. The right form for invariants like
   "deterministic", "idempotent", "FK target precedes source."
3. **Behavioral re-expression.** Some V1 tests test C# specifics
   (mocking, class-level concerns) that don't translate cleanly. These
   get rewritten in F# against V2's API; behavior preserved, encoding
   shifts.

When V1 and V2 disagree, the divergence is diagnostic, not a failure
mode. Three possibilities:

- V2 is wrong → fix V2.
- V1 was buggy → V1's test was encoding a bug; V2 corrects it; the
  test is updated and the divergence is logged here as an improvement.
- V2 is intentionally different → V2 made an explicit algebraic
  refinement V1 didn't have; the test is updated and the divergence is
  logged here.

**Reasoning / consequences:** Migration becomes constructive: "did we
migrate this correctly?" has a yes/no answer. ADMIRE.md gains an
"Existing test coverage" section per entry, listing each V1 test, what
it asserts, and which form translates it into V2. The discipline is
named here so reviewers can invoke it without re-litigating each
migration.

## 2026-05-07 — IR grows under evidence, not speculation

**Status:** decided (operating discipline)
**Context:** Several V2 commits will refine the IR: `IsPrimaryKey` on
`Attribute` (justified by the `EntitySeedDeterminizer` admire); future
`IsForeignKeyTarget` or richer `Reference` shape (likely justified by
`EntityDependencySorter` admire); column-level metadata (computed,
default expression) when admire passes surface real fixtures that need
them.
**Decision:** The IR grows when an admire pass surfaces a structural
need from a V1 component being migrated, OR when a property test
discovers an invariant the IR cannot currently express. It does **not**
grow speculatively — "we might want this someday" is not a justification.
Every IR refinement carries a comment naming the admire entry or test
that motivated it.
**Reasoning / consequences:** The IR stays small. Future readers can
read every field's justification by following the comment back to the
ADMIRE entry or test. Speculative complexity has nowhere to land.

## 2026-05-07 — IR refinement: `IsPrimaryKey` on `Attribute`

**Status:** decided
**Refines:** the IR shape introduced in commit 4 of session 1.
**Context:** The `EntitySeedDeterminizer` admire entry (ADMIRE.md, 2026-05-06)
identified PK-column knowledge as a structural prerequisite for the
extracted `NormalizeStaticPopulations` pass. The synthetic-milestone
`RawTextEmitter` was using a name-based hack (assume the PK attribute
is named "Id") to resolve FK target columns; that hack is now retired.
**Decision:** Add `IsPrimaryKey : bool` to `Attribute`. Composite primary
keys are expressed by flagging multiple attributes on the same kind.
`Kind.primaryKey` returns the PK attributes in declaration order.
**Reasoning / consequences:** First IR refinement under the
"IR grows under evidence" discipline — motivated by a concrete admire
pass and a concrete emitter need, not by speculation. The synthetic
fixture marks each Id attribute as the PK; the JsonEmitter surfaces
`primaryKey` alongside `nullable` for every attribute; the SSDT
RawTextEmitter tags PK columns with " PK" in the inline comment.

## 2026-05-08 — Contract testing surfaces V1 latent bugs as well as V1 intent

**Status:** decided (operating discipline; worked example)
**Context:** The contract-testing discipline (2026-05-07) framed V1
tests as oracles for V2 migration. A natural read of that framing is
"V2 must reproduce V1." The truth is more useful: contract testing
surfaces V1's *implicit* contracts as well as its explicit ones, and
some of the implicit ones are latent bugs.
**Worked example.** While preparing the second admire entry
(`EntityDependencySorter`, 2026-05-07), the scout surfaced that V1's
correctness depends on `Dictionary<K,V>` insertion-order iteration in
the CLR. That dependency is nowhere in V1's tests; it is a load-bearing
implementation detail no contract documents. A V2 property test
sweeping shuffled inputs catches the dependency and pins it as a real
V2 invariant: `TopologicalOrder.run is invariant under input
permutation`. The diagnostic moves the constraint from "implicit
behavior of the CLR happens to give V1 the result it wants" to
"V2 actively guarantees this." The result is V2 is more robust than V1
on this axis, by virtue of the discipline catching the gap.
**Decision:** Treat divergences from V1's *implicit* behaviors with
the same algebraic-conversation rigor as divergences from V1's
*explicit* tests. The three categories from the contract-testing
entry (V2 wrong / V1 buggy / V2 intentionally different) extend to
implicit contracts: when V2's property test surfaces a behavior V1
relied on but didn't assert, the question is the same — is V2's
codification a fix, a refinement, or a regression?
**Reasoning / consequences:** Contract testing is a dividend, not just
a cost. Future agents reading this entry see the discipline pay out,
not just impose a discipline tax. Worked examples accrue here as
sessions surface them.

## 2026-05-08 — Lineage events fire only on actual change

**Status:** decided (silent operating convention)
**Context:** The `namingMorphism` pass (session 2, commit 3) emits
`Renamed` lineage events only when the morphism produced a different
name; no-op morphisms produce empty trails. The convention reads in
code naturally and keeps lineage chains forensically meaningful —
every event is a real transformation, not noise from passes that
happened to run.
**Decision:** Adopt the convention silently for any pass that has a
no-op case. The pattern: a pass runs over every node, but emits an
event only when its work actually changed something. `namingMorphism`
is the template; future renaming-flavored passes (policy-driven
sanitization, schema-prefix injection, identifier collision
resolution) follow.
**Reasoning / consequences:** Lineage is provenance, not progress
reporting. A `Renamed` event in the trail means a name actually
changed; reading the trail is a forensic exercise, not a tally of
which passes ran. Passes that observe but don't transform (e.g.,
`canonicalizeIdentity`'s sweep) emit `Touched` events explicitly —
that's a different convention because the *act of observing* is
itself the contract.

## 2026-05-08 — Algebra/domain split: edge classification lives in CycleResolution

**Status:** decided
**Context:** Pre-commit audit on session-4 commits 4–6 (Kahn's, Tarjan's,
edge classification + asymmetric-2-cycle resolver). Commits 4 and 5 are
pure graph-theory algorithms with no V1 business logic. Commit 6's
`classifyEdge` and the resolver heuristic, however, are V1-flavored
domain rules — the `Weak | Cascade | Other` taxonomy and the
"break exactly one Weak edge in a 2-cycle" strategy are V1's
EntityDependencySorter conventions, lifted but not algebraically forced.
Embedding them inside `TopologicalOrderPass` mixed graph algebra with
domain interpretation.
**Decision:** Extract `EdgeStrength`, `classify`, and the resolver
strategies (`asymmetric2CycleStrategy`, `neverResolve`) into a new
`CycleResolution` module. The pass calls into `CycleResolution`; the
algebra (graph build, Kahn's, Tarjan's, "remove edges and re-sort") is
free of domain rules. The `CycleResolution.Resolver` type
(`SsKey list -> ((SsKey * SsKey) * EdgeStrength) list -> ResolutionStep`)
is the seam — call sites can pass any conforming function; the
algebra doesn't know which strategy is in use.
**Reasoning / consequences:** When V2 admits a non-RDBMS catalog or a
new resolver strategy (manual cycle overrides, MFAS, deferred
junctions), the new logic lands in `CycleResolution` (or a sibling
module) without touching the algebra. `TopologicalOrderPass` currently
passes `CycleResolution.asymmetric2CycleStrategy` as the resolver;
making it a pass-level parameter is deferred until the second resolver
strategy actually lands (per "IR grows under evidence"). The audit
itself is the kind of thing the discipline is designed to surface;
preserving the resulting algebra/domain split in code keeps the
algebra small.

## 2026-05-09 — Algebra/domain split pattern (generalizable)

**Status:** decided (operating discipline)
**Context:** Session 4 commit 6 split V1's edge classification and
asymmetric-2-cycle resolver out of `TopologicalOrderPass` into a
sibling `CycleResolution` module. The *shape* of that refactor is
generalizable; `EntityDependencySorter` is unlikely to be the only
place V1 entangled structural algorithm with domain rules.
**Decision:** Adopt the following canonical shape for any V1
component whose logic mixes graph algebra / structural transformation
with domain interpretation rules:

1. **Algebra in the pass.** The pass file in `Projection.Core.Passes`
   contains only the structural algorithm — graph traversal,
   composition, identity preservation, lineage emission. No domain
   rules; no V1-flavored interpretation of IR fields.
2. **Domain in a named module.** A sibling module (e.g.,
   `CycleResolution`, `NullabilityRules`, ...) named for the domain
   concern carries the V1 rules — taxonomies, classification
   functions, named strategies. The module name advertises that
   domain logic lives here, not algebra.
3. **Typed seam between them.** A function-type alias (e.g.,
   `Resolver`, `Classifier`) is the seam. Call sites pass any
   conforming function; the algebra knows nothing about which
   strategy is in use. Pluggable-as-pass-parameter is deferred
   until the second strategy actually arrives — the seam exists,
   the dispatch does not, "IR grows under evidence" applied to
   extensibility rather than data shape.

Apply this shape to future admire-and-extract migrations whenever
the V1 component mixes structural algorithm with domain
interpretation. The named-module sibling makes the algebra/domain
boundary visible at the file level; the typed seam keeps the
algebra honest while permitting future strategies without
rewriting the pass.

**Reasoning / consequences:** The pattern is canonical, not
ad-hoc. Future agents read this entry and recognize the shape;
reviewers can ask "what's the algebra here, what's the domain,
where's the seam" of any V1-derived pass and expect a clean answer.

## 2026-05-09 — Audits surface things not on the agenda

**Status:** decided (operating disposition)
**Context:** Session 4 produced two findings neither were planned:
the Dictionary-iteration-order invariant (commit 4) and the
algebra/domain split that prompted commit 6's pre-commit refactor.
Both required acting on what surfaced rather than shipping what was
planned.
**Decision:** Treat audits as an exploratory practice, not a
checkbox. When pre-commit reflection (or a property test, or a
reviewer's question) surfaces something second-order — a hidden
contract, a domain rule embedded in algebra, a latent V1 dependency
— the right response is to act on the finding before shipping,
even when it expands the commit's scope. Logging the finding in
DECISIONS *and* shipping the original work unchanged is the
checkbox-audit failure mode. Avoid it.

**Reasoning / consequences:** Audit dividends compound when
findings land in code; they evaporate when findings land only in
notes. The discipline pays off because the practice acts on what it
finds. Future agents reading this entry should expect their own
audits to produce findings that reshape work in flight, and should
budget time to honor those findings rather than defer them.

## 2026-05-09 — Annotated events with documented skip reasons (silent convention)

**Status:** decided (silent operating convention)
**Context:** The symmetric-closure pass (session 4 commit 3) uses
`Annotated` lineage events to record skip cases — when an inverse
isn't added because the target kind is absent or has no primary key.
The `detail` string names *why* the skip happened. Reading the trail
recovers the reason without re-running the pass.
**Decision:** Adopt the convention silently for any future pass with
skip cases. The pattern: a pass scans every node it could
transform; for nodes it processes, emit the appropriate transform
event; for nodes it skips, emit `Annotated` with a documented
detail string (e.g., `"skipped: target has no primary key"`,
`"skipped: precondition X not met"`). The lineage chain becomes
forensically useful for absences as well as presences.

Idempotence-twice-over plus documented skip reasons is the
recognizable shape for closure-flavored and conditional-application
passes. `symmetricClosure` is the template; future passes follow.

**Reasoning / consequences:** The trail answers "why isn't node X
in the surface?" without dropping into source code. Convention is
silent (no enforcement mechanism beyond review) but cheap to follow
once seen.

## 2026-05-09 — Adapter language choice: F# for IR-conversion, C# reserved for foreign-API I/O

**Status:** decided
**Context:** Session 5 commit 3 was originally intended as the first
C# adapter (`Projection.Adapters.Sql/Static.cs`), per the V2 handoff's
"C# at the boundary" framing. Implementation hit F# interop friction —
F# `Result<'a>` and discriminated unions consumed from C# require
verbose `NewSuccess` / `NewFailure` factories and pattern-matching
through nested case classes, costing readability without earning
anything for an adapter whose only foreign API is `System.Text.Json`
(which both languages handle equally well).
**Decision:** Adapter language is decided per-adapter, by which side of
the seam the foreign API sits on:

- **F# adapters** for **IR-conversion adapters** — adapters whose job
  is to coerce one shape into another, with no native-API dependencies
  beyond `System.*`. Examples: V1 JSON ↔ V2 IR (this commit), V1
  Profile JSON ↔ V2 Profile (future), DACPAC schema ↔ V2 IR (future,
  if the DACPAC parsing API is comfortable from F#).
- **C# adapters** for **foreign-API I/O adapters** — adapters whose
  job is to talk to an external system whose .NET API is OOP-flavored
  and lives on the C# side: SQL Server connections (ADO.NET, Dapper,
  Entity Framework), HTTP servers (ASP.NET / Hot Chocolate), DACPAC
  building (DacFx — *if* its API turns out to be unfriendly from F#),
  external authorization frameworks. C# is the right side of the
  language seam when the native API is the cost; F# `Result<'a>`
  interop is awkward from C# but the `Result.bind` / `Result.map`
  composition stays inside the F# core where it belongs.

The seam stays at the language boundary, not at the project boundary.
A `Projection.Adapters.<Foreign>` project may be C# or F# depending on
its native API; the namespace pattern is preserved either way.

**Reasoning / consequences:** F# for IR conversion keeps
`Result.bind` / `Result.map` composition natural at the boundary;
short-circuit semantics on adapter failures look identical to F# core
code. C# for foreign-API I/O keeps the boundary readable when the
native API is OOP-shaped. Future agents reading this entry decide
adapter language by asking "what's the native API?" — IR shapes are
F#-native; SQL/HTTP/DACPAC are C#-native.

The session 5 commit 3 adapter (`Projection.Adapters.Sql/Static.fs`)
is the canonical pattern for IR-conversion adapters; the future
SQL-I/O adapter when it arrives will be the canonical pattern for
foreign-API I/O adapters.

## 2026-05-09 — Policy.Tightening as fourth top-level axis (worked example: structural commitments are defaults)

**Status:** decided
**Context:** The session-2 commitment to a three-axis Policy
(Selection / Emission / Insertion) was right at the time given the
evidence. The session-5 `NullabilityEvaluator` admire pass surfaced
configuration that does not fit any of the three axes: tightening
mode, null budget, cautious-relaxation toggle, override list. These
inputs control *what shape of decision gets produced*, not which kinds
participate, what artifacts are emitted, or how data is applied.
Trying to fit them into one of the existing axes would be artificial
and lossy.
**Decision:** Add `Tightening` as a fourth orthogonal Policy axis.
`TighteningPolicy` carries `Mode` (`Cautious | EvidenceGated |
Aggressive`), `NullBudget` (decimal in [0, 1]), `AllowCautiousRelaxation`
(bool), and `Overrides` (list of `TighteningOverride` keyed by SsKey
per A4). AXIOMS A12 receives a second amendment (2026-05-09 — four
orthogonal axes) preserving the three-axis history; the original
amendment from 2026-05-06 is the lineage, not the rule.
**Reasoning / consequences:** This is a worked example of a principle
worth naming explicitly: **structural commitments are defaults, not
promises.** The three-axis claim was a default given the evidence
available at session 2; it grew when a real pass forced it to. The
discipline is "IR grows under evidence" applied at the policy-shape
level, exactly as it has applied at the data-shape level. Future
agents reading this entry should expect their own structural
commitments to refine when consumers demand it, and should not
defend earlier commitments against pressure from real evidence.

The amendment cadence — three axes (session 2) → four axes
(session 6) — is itself a worked example: the architecture refined
in flight three times across five sessions (Kahn's permutation
invariance, CycleResolution extraction, language rule supersession),
each driven by evidence rather than by speculation. The four-axis
amendment is the fourth and the first at the policy-shape level
rather than the implementation level.

## 2026-05-09 — Adapter language rule supersedes the original "F# core / C# shell" framing

**Status:** decided (supersedes the 2026-05-06 — F# is introduced for the algebraic core entry's framing)
**Context:** The original V2 handoff partitioned languages by
algebra-vs-I/O — F# for the pure core, C# at the imperative shell.
Session 5 commit 3 (the static-data adapter) showed this partition
is too coarse: the JSON-parsing adapter is "shell" by the original
framing but its native API is `System.Text.Json`, which both
languages handle equally well. Forcing C# created interop friction
without earning anything.
**Decision:** **Adapter language is decided per-adapter, by which
side of the seam the foreign API sits on.** F# adapters for
IR-conversion concerns whose only foreign dependencies are
`System.*` (JSON parsing, byte-array hashing, etc.). C# adapters for
foreign-API concerns whose .NET API is OOP-flavored (SQL Server
connections, ASP.NET / Hot Chocolate, DACPAC building, external
authorization). The seam is at the language boundary; the project
boundary follows from API alignment, not from a pre-imposed
algebra/shell partition.

This rule supersedes the original framing as the canonical statement.
The earlier entry remains for historical context; future agents
applying the rule should consult this entry first.

**Reasoning / consequences:** The refined rule was Danny's
formulation in the session-5 review. It is sharper than the original:
language alignment with native API maximizes readability on each
side of the seam. F# `Result.bind` / `Result.map` composition stays
natural at IR-conversion boundaries; OOP-flavored .NET APIs stay
natural at SQL-I/O boundaries. The seam is honest about what
actually crosses; the project naming follows from where the seam
falls, not from a pre-imposed partition.

## 2026-05-09 — Pattern setters explicitly named in ADMIRE.md

**Status:** decided (operational discipline; observation from session 5)
**Context:** Session 5 shipped two canonical patterns:
`EntitySeedDeterminizer` (the "split" pattern, with status `extracted
(differential confirmed)` as the marker for completed migrations);
`Projection.Adapters.Sql.Static` (the IR-conversion adapter pattern).
Both have ADMIRE entries; both have explicit canonical-string statuses;
future agents can scan ADMIRE.md and see at a glance what shape to copy
and what state each migration is in.
**Decision:** Continue this naming explicitly as ADMIRE entries land.
Each new V1 component admire identifies whether its V2 placement is a
**copy of an existing canonical pattern** (cite the earlier ADMIRE
entry by date/title) or a **new pattern setter** (mark the status
explicitly as canonical). After the second confirming instance of a
pattern, the pattern is a shape, not a one-off.

**Reasoning / consequences:** "Make the laws visible" applied at the
operational level. Future readers can scan for status strings —
`admired (placement decided)`, `extracted (differential confirmed)`,
`extracted (full coverage)` — and understand the migration arc at a
glance. The corpus accumulates value over time precisely because
patterns get named when they emerge.

## 2026-05-09 — Tightening as a registry of named interventions; modes collapsed

**Status:** decided (refines the 2026-05-09 — Policy.Tightening as fourth top-level axis entry)
**Context:** The first commit of this session shipped `TighteningPolicy`
as a flat record with a `Mode` field defaulting to `Cautious`, a
`NullBudget` defaulting to `0.0`, and an `AllowCautiousRelaxation`
toggle. Reviewed pre-push: even those defaults are themselves
*interventions* — `Cautious` mode produces decisions when
`NullabilityPass` runs; the empty policy was not actually empty.
The end goal: "no unknown alterations to the system; all
interventions stubbed as plugins, clearly identified and trackable."
Concurrent observation: V1 has only ever used `Cautious` mode in
production; the `EvidenceGated` and `Aggressive` variants are
unused.
**Decision:** Two refinements, applied together:

1. **Plugin/intervention model.** `TighteningPolicy` is a registry of
   zero or more named `TighteningIntervention` values. Empty registry
   = no interventions = no decisions produced. Each intervention
   carries a stable `Id` chosen by the caller (e.g.,
   `"v1-style-nullability"`, `"per-tenant-overrides-2026-05"`); the
   id appears in lineage events when the intervention fires, so
   audit consumers answer "which intervention changed this column?"
   structurally. The `TighteningIntervention` DU is closed; new
   intervention kinds (FK enforcement, unique enforcement, type
   tightening) land as new variants when admire passes surface them.

2. **Modes collapsed.** Per Danny's observation that V1 only ever
   uses `Cautious` mode, `TighteningMode` is removed from V2
   entirely. `NullabilityTighteningConfig` carries
   `NullBudget` + `AllowMandatoryRelaxation` + `Overrides` only — no
   mode field. If a real second mode lands later, it returns as a
   field or as a new intervention variant, motivated by evidence.
   Rename: V1's `AllowCautiousNullabilityRelaxation` becomes
   V2's `AllowMandatoryRelaxation`, naming the semantic ("permit
   mandatory→nullable relaxation under evidence") rather than the
   collapsed mode.

**Reasoning / consequences:** Two principles in evidence:

- **Defaults that intervene are themselves an intervention.** The
  prior shape's `Cautious` default would have caused
  `NullabilityPass` to produce decisions silently when the caller
  set `Policy.empty`. V2's strict default is to do nothing.
- **Unused variants are speculative complexity.** V1's three modes
  cost no maintenance in V1 because V1 isn't being refactored. They
  cost real complexity in V2 — three code paths, three test cases,
  three rationale-set composition rules — for no current consumer.
  Collapsing to one mode ("IR grows under evidence") leaves the
  algebra room to grow back into multiple modes when demand is real.

This entry refines the prior session-6 entry on Tightening; the two
should be read together. The DU-per-intervention shape is the
canonical pattern for any future "pluggable behavior" axis on Policy
— next time something feels like a registry of named operations,
this is the template.

**Worked-example dimension.** This refinement is itself a worked
example of "audits surface things not on the agenda" — the prior
commit was reviewed, the default-as-intervention smell was caught,
and the refactor landed before push. The discipline pays off when
findings land in code; deferring this to a later session would
have shipped the wrong shape and forced a more expensive amendment
later.

## 2026-05-09 — NullabilityOutcome shape: ternary with structured rationale (the V1↔masterwork choice precedent)

**Status:** decided
**Context:** The first deliberate V1↔masterwork architectural choice
V2 has made. V1 represents nullability decisions as
`(MakeNotNull: bool, RequiresRemediation: bool, Rationales: string[])`
— a binary primary outcome plus a remediation flag plus free-form
string rationales. The masterwork prescribes a ternary
`NullabilityOutcome = EnforceNotNull | KeepNullable |
RequireOperatorApproval` with a single string `Rationale` and a
`Risk` enum. Both have costs: V1's binary scrubs context the
operator-approval case actually needs; the masterwork's strings
require text parsing for downstream consumers.
**Decision:** Adopt the **masterwork's ternary outcome** with V2's
**structured rationale at the type level**. Each variant carries a
typed rationale value:

```fsharp
type NullabilityOutcome =
    | EnforceNotNull of evidence: NullabilityEvidence
    | KeepNullable of reason: KeepNullableReason
    | RequireOperatorApproval of conflict: NullabilityConflict
```

The rationale DUs (`NullabilityEvidence`, `KeepNullableReason`,
`NullabilityConflict`) are closed and structured. Lineage chains and
emitter consumers pattern-match on rationale rather than parsing
strings.

**Reasoning / consequences:** The structural rationale is more honest
than V1's binary-plus-remediation (which scrubs context the
operator-approval case actually needs) and more rigorous than the
masterwork's ternary (which uses free-form strings). The cost is
three small DUs; the benefit is type-checked rationale at every
consumer site — an emitter that handles
`MandatoryButHasNullsBeyondBudget` knows the exact data it has, no
string parsing.

**Precedent.** This is the first time V2 has made an architectural
choice between V1 and the masterwork shapes. The principle the
choice sets: **V2 doesn't inherit from one source by default; it
picks based on what serves the algebra and the codebase.** Future
similar choices (FK decision shape, unique decision shape, type
decision shape — all coming when their admire passes land) follow
the same principle. Where V1's shape serves better, take V1's.
Where the masterwork's shape serves better, take the masterwork's.
Where neither is right, refine V2's own.

## 2026-05-09 — Observable-identity-on-empty-policy as structural commitment

**Status:** decided (structural commitment, not just a default)
**Context:** Per the plugin/intervention refactor (DECISIONS
2026-05-09 — Tightening as a registry of named interventions),
`TighteningPolicy.empty` carries zero interventions and produces zero
decisions when a pass runs against it. This is V2's strict default —
no system alterations unless the caller explicitly registers an
intervention. The structural form of this rule is observable
identity: a pass running against `Policy.empty` returns an empty
output and emits no events.
**Decision:** Promote the rule from "default behavior" to
**structural commitment**. Every V2 pass that consumes Policy must
satisfy:

  *Observable identity on empty policy.* For a pass `p` taking
  `(Catalog, Policy, Profile)` and returning `Lineage<Output>`:
    - `p (catalog, Policy.empty, profile)` returns
      `{ Value = empty-output; Trail = [] }`.
    - The Catalog is unchanged (passes that produce values rather
      than transforming the catalog return their `empty-output`).
    - No lineage events are emitted (no `Touched`, no `Annotated`,
      no `Created`, no `Removed`).

This is the V2 algebraic property the masterwork's "warn, don't
auto-fix" principle compiles down to. Future passes adopt the
commitment by construction; tests verify it explicitly.

**Reasoning / consequences:** "V2 takes no action on empty policy"
is a structural property the type system + tests can guarantee, not
a convention reviewers must remember to check. Future agents
reading this entry inherit the rule for any new pass; the rule
becomes a compiler-checkable obligation as more passes adopt the
ProjectionInput-shaped signature.

## 2026-05-09 — V1→V2 name mapping: `AllowCautiousNullabilityRelaxation` → `AllowMandatoryRelaxation`

**Status:** decided (documentation; not a code change)
**Context:** During the mode-collapse refactor, V1's
`AllowCautiousNullabilityRelaxation` was renamed to
`AllowMandatoryRelaxation` in V2. The rename names the semantic
("permit mandatory→nullable relaxation under evidence pressure")
rather than the now-collapsed Cautious mode that was the V1 flag's
referent.
**Decision:** Record the mapping here so the V1↔V2 grep-bridge
exists at the documentation layer. Migration scripts, debugging
sessions, and future agents tracing V1 behavior to V2 can follow
the rename through this entry.

| V1 name                                  | V2 name                    |
|------------------------------------------|----------------------------|
| `AllowCautiousNullabilityRelaxation`     | `AllowMandatoryRelaxation` |

This entry pairs with the broader V1↔V2 vocabulary mapping in the
2026-05-06 — General names in the pure core entry; this is the
nullability-specific rename. Future renames at the rules-module
level land here as additional rows.

## 2026-05-09 — Three-input projection validated end-to-end (the milestone)

**Status:** decided (worked-example milestone)
**Context:** Session 6's planned milestone — combine the static-data
adapter, the profile-snapshot adapter, and `NullabilityPass` to
validate the three-input projection
`Project = Π ∘ E : (Catalog, Policy, Profile) → Output` against
V1-fixture-equivalent inputs. The test exercises the full V1↔V2
boundary stack:

```
V1 JSON (static-data + profile-snapshot)
     │
     ▼
F# adapters (Static.attachStaticPopulations + ProfileSnapshot.attach)
     │
     ▼
V2 IR (Catalog with populations + Profile)
     │
     ▼
NullabilityPass (under registered Nullability intervention)
     │
     ▼
NullabilityDecisionSet (emitter-consumable per A32)
```

**Result:** the milestone test
(`MILESTONE: three-input projection passes end-to-end through both
adapters and NullabilityPass`) passes. 348/348 tests green.

**Two structural commitments validated empirically:**

1. **The three-input projection works.** `Project = Π ∘ E` consumes
   all three inputs (Catalog, Policy, Profile) and produces decisions
   end-to-end. The plumbing through both adapters preserves identity
   (every decision keys back to a real catalog Attribute SsKey); the
   pass produces decisions for every (attribute × intervention) pair
   the policy registers; outcomes match expectations on the
   V2-expressible cases (PrimaryKey, PhysicallyNotNull, override,
   no-signal).

2. **The plugin-shape Tightening supports the projection without
   compromise.** Sessions 6's mid-session refactor (DECISIONS
   2026-05-09 — Tightening as a registry of named interventions)
   could have introduced friction at the integration site; it didn't.
   Registering one intervention with a stable id, running the pass,
   and getting decisions tagged with that id all flow naturally.
   Future audit consumers reading the lineage will see "intervention
   `v1-cautious-equivalent` produced EnforceNotNull(PrimaryKey) on
   `OS_ATTR_E2E_Parent_Id`" — structural, queryable, type-checked.

**Caveats parked:**

- V2's IR does not yet carry `IsMandatory` on `Attribute`. The V1
  mandatory-driven branches (`LogicalMandatoryNoNulls`,
  `RelaxedUnderEvidence`,
  `MandatoryButHasNullsBeyondBudget`) are commented pseudocode in
  `NullabilityRules.evaluate`; they wire in when `IsMandatory` lands
  under "IR grows under evidence." The milestone test annotates
  this in code so future agents see the limitation surfaced.
- Differential parity with V1's full `NullabilityEvaluatorTests`
  fixture suite (8 tests) requires the mandatory branch. The
  current end-to-end differential validates the V2-expressible
  subset; the remaining V1 parity arrives with the IR refinement.

**Reasoning / consequences:** The milestone is achieved at the
algebra-and-plumbing level. The remaining V1 parity is a known IR
gap, not a structural one. V2's three-input projection is now
empirically validated; future passes (FK enforcement, unique
enforcement) follow the same shape and inherit the validation by
construction. Session-6 commits 1 (Tightening axis) → 2 (plugin
refactor) → 3 (NullabilityRules) → 4 (NullabilityPass) → 5
(profile adapter) → 6 (this milestone) form a coherent vertical
slice; each commit is independently meaningful and the whole stack
passes its empirical contract.

## 2026-05-10 — Milestone (re-marked): the algebra is now operational

**Status:** decided (worked-example milestone, marked deliberately)
**Context:** Session 6 commit 6 ran the three-input projection
end-to-end through both adapters and `NullabilityPass` against real
V1 fixture data; the test passes. This entry re-marks the moment
clearly, separately from the commit-level milestone entry.

**What it is.** The first execution of `Project = Π ∘ E` on the
triple `(Catalog, Policy, Profile)` end-to-end against V1-derived
inputs. Two adapters convert V1 JSON to V2 IR; the policy registers a
Nullability intervention; the pass produces a structured
`NullabilityDecisionSet`. The plumbing is empirical, not
hypothetical.

**What it validates.** The three-input projection works in practice
— identity preserved across the boundary; profile evidence flows
into per-attribute decisions; intervention id threaded through to
lineage; outcomes match expectations on the V2-expressible cases.
The plugin-shape Tightening (DECISIONS 2026-05-09) supports the
projection without compromise — the mid-session refactor is
empirically vindicated.

**What it does not yet validate.** V2's IR does not yet carry
`IsMandatory` on `Attribute`. The V1 mandatory-driven branches
(`LogicalMandatoryNoNulls`, `RelaxedUnderEvidence`,
`MandatoryButHasNullsBeyondBudget`) are pseudocode in
`NullabilityRules.evaluate`, awaiting the IR refinement under "IR
grows under evidence." Full V1 parity with V1's eight
`NullabilityEvaluatorTests` requires this. The honest frame for the
parity gap: a known IR refinement, not a structural one.

**The phase change.** This is the moment the algebra stops being a
structural claim and becomes an operational fact. The properties
the axioms promised — A6's three substantive inputs, A12's policy
axes, A17's `Project = Π ∘ E`, A32's emitter-consumable values, the
2026-05-09 observable-identity-on-empty-policy commitment — are
demonstrated, not just claimed. Future agents reading this log
should identify session 6 commit 6 as the inflection point.

## 2026-05-10 — IR-conversion adapter pattern: the adapter is where V1's vestigial fields die

**Status:** decided (operational discipline; observation from session 6)
**Context:** Two F# IR-conversion adapters now share a shape:
`Projection.Adapters.Sql.Static.attachStaticPopulations` (session 5)
and `Projection.Adapters.Sql.ProfileSnapshot.attach` (session 6).
The pattern is canonical not because it was prescribed but because
it was repeated and confirmed.

The shared shape:
- Signature: `Catalog -> string -> Result<Catalog>` (or returning a
  built value type like `Profile`).
- JSON parsing via `System.Text.Json`.
- Embedded V1 fixture content as the V2 contract; the test fails
  loudly if V1's JSON shape changes without a matched V2 expectation
  update.
- Silent skip for unresolvable rows (the catalog's selection is the
  contract, not the JSON's).
- Result-typed return; never throws across the seam.
- F# language for IR conversion (per the 2026-05-09 adapter language
  rule).

**Decision (the additional convention this entry names):** **The
adapter is the place V1's vestigial fields die.** V2's IR carries
only what V2 uses. V1's serialized data formats may include fields
V2 does not model (catalog metadata embedded in profile JSON;
operational sample arrays; redundant copies of catalog facts).
The adapter:

  - **Drops** V1 fields V2 does not model. Examples from session 6's
    `ProfileSnapshot` adapter: V1's
    `IsNullablePhysical`/`IsComputed`/`IsPrimaryKey`/`IsUniqueKey`/
    `DefaultDefinition` are catalog metadata in V2 (lives on
    `Attribute`/`Column`); the adapter ignores V1's redundant copies
    and trusts the V2 catalog. V1's `NullSample`/`OrphanSample` are
    operational diagnostics; V2 elides them.
  - **Synthesizes** V1 fields V2 demands but V1 lacks. Example:
    V1's `CompositeUniqueCandidateProfile` has no `ProbeStatus`; V2
    requires it for evidence-vs-no-evidence distinguishability, so
    the adapter synthesizes a default `Succeeded` probe at
    `UnixEpoch`. If a real V1 fixture surfaces a meaningful
    distinction, the adapter learns the field; until then, the
    synthesized default flows through.
  - **Names** the divergences in code comments. Each drop /
    synthesis carries a comment so future readers can audit the
    boundary's choices without surprise.

**Reasoning / consequences:** V2's IR stays small. V1's
serialization quirks don't propagate into the algebra. Adapters
that deviate without justification (carry V1 fields that V2 does
not use; or fail to synthesize fields V2 demands) are flagged in
review. Future IR-conversion adapters (UniqueIndex, FK enforcement,
type tightening — coming in subsequent sessions) inherit this
convention by construction.

## 2026-05-10 — Audit discipline operates at design-time, not just commit-time

**Status:** observed (operating discipline reflection)
**Context:** Sessions 4–6 produced four audit-driven course
corrections:

  - Session 4: Kahn's permutation invariance (commit 4).
  - Session 4: CycleResolution algebra/domain split (commit 6).
  - Session 5: C# → F# language pivot (commit 3).
  - Session 6: Plugin/intervention refactor of Tightening (commit 2).

The first three caught issues mid-session as the work progressed.
The fourth — session 6's plugin refactor — caught something at the
level of *initial design intent* and reshaped the work before the
flat-record commit was pushed. The default-as-intervention smell
was a problem in the work's premise, not its execution.
**Decision:** Mark the observation. The audit discipline is
beginning to operate at design-time, not just commit-time. The
practice deepens with use; future agents reading this log should
expect their own audits to surface premise-level findings, not
only execution-level ones, and should plan to act on them in
flight rather than ship past them.

This is not a flagship principle but a worked-example observation:
the practice gets better the more it gets used.

## 2026-05-10 — Second decision-producing V1 transform fully migrated; status string in use

**Status:** decided (worked-example marker)
**Context:** V2's third "extracted (differential confirmed)" status
lights up: `NullabilityEvaluator` joins `EntitySeedDeterminizer` as a
fully-migrated V1 component. Five of V1's eight test scenarios pass
as Behavioral parity assertions in V2; three are explicit Skip cases
documenting intentional V2 divergences (Aggressive mode collapsed;
opportunity-stream pending Diagnostics writer).

**Decision:** Mark the moment. `NullabilityEvaluator`'s ADMIRE entry
reaches `extracted (differential confirmed)`. The status string is
canonical; future ADMIRE entries that achieve this state use the same
phrase. The convention from DECISIONS 2026-05-09 (Pattern setters
explicitly named in ADMIRE.md) is paying out: readers can scan
ADMIRE.md and see at a glance which V1 components have been
empirically validated against V2.

**The differential-with-skips pattern.** When V2 diverges from V1
deliberately (collapsed Aggressive mode; structured Diagnostics
writer instead of inline opportunities), the parity test names the
divergence as a Skip case with explicit rationale. This preserves
two invariants:

  1. The migration is **honest** — V2 doesn't pretend to match V1
     where it deliberately doesn't. The skip case is the divergence
     made visible.
  2. The discipline is **constructive** — adding the V2 equivalent
     of a skipped V1 case (e.g., when an Aggressive-equivalent
     intervention arrives, or the Diagnostics writer lands) is a
     mechanical activation: remove the `Skip = "..."` argument and
     write the V2 assertion. The skip is a forward-pointing TODO,
     not a permanent gap.

**Reasoning / consequences:** The third use of the status string
makes the convention canonical by repetition (per the
2026-05-09 — Pattern setters discipline). Future ADMIRE entries that
reach `extracted (differential confirmed)` follow the same
differential-with-skips pattern: 100% V1 contract under V2's
expressible cases; explicit Skip-with-rationale for V2 divergences.

## 2026-05-11 — Strategy layer: a named architectural vector

**Status:** decided (operating discipline)
**Context:** Three V1 components have been migrated under the
algebra/domain split (DECISIONS 2026-05-09 — Algebra/domain split
pattern): `EntityDependencySorter` produced `CycleResolution`
(session 4); `NullabilityEvaluator` produced `NullabilityRules`
(session 6); `UniqueIndexDecisionOrchestrator` produced
`UniqueIndexRules` (session 7). Each migration lifted V1's domain
reasoning out of the algebra and named it as a sibling module. The
*shape* of these modules has now stabilized through repetition, and
the moment to codify it as a first-class architectural concern has
arrived — before a fourth instance lands and the implicit convention
drifts.

The lesson from the previous "Algebra/domain split pattern (generalizable)"
entry is that the canonical shape is observable in code, not just in
prose. With three instances the shape is empirically real; the cost
of codifying now is low; the cost of codifying after six instances
is rewriting six modules to fit. This entry promotes the strategy
layer from implicit convention to named architectural vector.

**Decision:** **Strategy** is a named architectural concern within
`Projection.Core`, distinct from but adjacent to the algebraic core.
Strategy modules carry domain-specific decision logic that the
algebra invokes through a typed seam. The canonical shape of a
strategy module:

1. **Pure functions of IR fields.** No I/O, no mutable state, no
   external context. The strategy reads `Catalog`, `Policy`, and
   `Profile` fields and returns decisions. Determinism follows from
   purity.
2. **A typed function-type alias is the seam.** The pass that
   consumes the strategy calls into it through a named function type
   (e.g., `Resolver`, `evaluate`); the algebra knows nothing about
   how the decision is made. New strategies plug in by conforming
   to the seam without rewriting the algebra.
3. **Structured rationale DUs cover the decision space.** Each
   variant of the outcome DU carries the evidence or reason for the
   decision at the type level. Lineage events emit a textual summary
   for grep-ability; the structured outcome lives in the decision
   set for downstream pattern-matching. Free-form rationale strings
   are an anti-pattern (see CycleResolution caveat below).
4. **Lineage events fire only on actual decisions.** When a
   strategy makes no decision (registry empty, intervention not
   registered, structural commitment to inaction), no events are
   emitted. The `Annotated`-with-skip-reason convention (DECISIONS
   2026-05-09) covers conditional cases that still warrant a trail
   entry.
5. **The module name advertises the domain.** `<Domain>Rules` for
   per-record deciders (`NullabilityRules`, `UniqueIndexRules`,
   future `ForeignKeyRules`); domain-named modules for non-record
   strategies (`CycleResolution`). The `Rules` suffix is the
   recognizable shape for registered-intervention strategies; other
   suffixes are admissible when the call pattern differs.

**Two strategy flavors observed.** The three current modules split
into two flavors that share the deep shape but differ in call
pattern:

- **Registered-intervention strategies** (`NullabilityRules`,
  `UniqueIndexRules`): invoked through registry iteration over a
  `TighteningIntervention` variant, one decision per (record ×
  intervention) pair, intervention-id flowing through every
  decision. The pass driver fans out over the registry; the
  strategy's `evaluate` decides each pair.
- **Structural strategies** (`CycleResolution`): invoked from
  inside a pass at structurally-determined moments (per-FK-edge
  classification during graph construction; per-SCC resolver
  application during cycle handling). No registry; no
  intervention-id. The seam is a function type the pass passes
  through.

Both flavors honor the deep shape; the call pattern is what differs.
Future strategy modules pick the flavor that matches their domain.

**Worked examples.**

| Module | Flavor | Seam | Decision DU | Status |
|---|---|---|---|---|
| `CycleResolution` | structural | `Resolver` (`SsKey list -> ((SsKey * SsKey) * EdgeStrength) list -> ResolutionStep`); `classify` | `EdgeStrength`; `ResolutionStep` (free-form `Reason`) | extracted |
| `NullabilityRules` | registered-intervention | `evaluate : interventionId -> config -> Attribute -> Profile -> NullabilityDecision` | `NullabilityOutcome` ternary with `NullabilityEvidence` / `KeepNullableReason` / `NullabilityConflict` | extracted (differential confirmed) |
| `UniqueIndexRules` | registered-intervention | `evaluate : interventionId -> config -> Kind -> Index -> Profile -> UniqueIndexDecision` | `UniqueIndexOutcome` binary with `UniqueIndexEvidence` / `UniqueIndexKeepReason` | extracted |

**CycleResolution caveat.** `CycleResolution.ResolutionStep.Reason`
is a free-form string ("auto-resolved by removing weak edge", "SCC
has no Weak edge to break", etc.). This predates the
structured-rationale-DU convention and is grandfathered; when
`CycleResolution` is next substantively touched (e.g., when a second
resolver strategy lands per the 2026-05-08 pluggability deferral),
migrate `Reason` to a structured DU mirroring `NullabilityRules`'s
approach. Logging the migration as a TODO here rather than
performing it now keeps session 8 focused on codification.

**Registry deferred.** A registry mechanism for strategy
discoverability (a top-level `Strategies : Strategy list` axis;
plug-in loading; cross-strategy composition combinators) is the
next promotion candidate when N grows past 4–6. At N=3 the registry
is overkill — each strategy's call site is named explicitly and the
seam is its type. Recording the deferral here so the next agent
reading this entry doesn't build the registry under "IR grows under
evidence" and finds it unjustified at the time of writing.

**Reasoning / consequences:** Strategy is now nameable in code and
in conversation as a first-class concern. New V1 admire migrations
that surface domain-decision logic land into a named layer with a
recognized shape; reviewers can ask "what's the seam, what's the
DU, where's the algebra" and expect a structurally-honest answer.
The pattern's empirical basis (three instances) supports the
codification; future instances either fit the codification or
surface a revision. The codification is descriptive, not
prescriptive; if a strategy doesn't fit, the question is whether
the codification or the strategy is wrong, and either answer is
interesting.

## 2026-05-11 — Strategy composition vocabulary (sketch, deferred)

**Status:** sketched (not implemented)
**Context:** Each pass driver that consumes a strategy implements
the iteration/accumulation/lineage discipline ad hoc. With three
strategy modules and two pass drivers (NullabilityPass,
UniqueIndexPass) using nearly the same fan-out shape, the
composition logic has now been duplicated twice. Before the third
duplication ships (`ForeignKeyPass`), the question is whether a
small composition vocabulary belongs at the strategy layer.

**Sketch (proposal, not implementation).** The composition primitives
that would land into a `Projection.Core.Strategies.Composition`
module if and when the cost-benefit clears:

1. **`fanOut`** — registry iteration. Given a list of `(id, config)`
   pairs and a `decide : id -> config -> 'context -> 'decision`
   function, produce `'decision list` over a list of contexts.
   Currently inlined in NullabilityPass and UniqueIndexPass via list
   comprehension. The vocabulary exists; it's been duplicated; it's
   short enough that duplication has not yet been painful. The
   primitive earns its place when N=3+ pass drivers iterate the same
   way, or when a fourth axis of variation lands (e.g., decision
   weights, conditional invocation, ordering preferences) that would
   force the inlined version to grow into a function anyway.
2. **`fallback`** — chained strategy. Given strategies A and B, run
   A; if it returns a "no decision" / default outcome, run B.
   Currently no use case in the codebase — every strategy returns a
   total decision (every variant of every outcome DU is meaningful).
   Speculative until a partial strategy lands (e.g., manual override
   sets that fall back to evidence-driven decisions when the
   override is absent).
3. **`accumulate`** — multi-strategy aggregation. Given strategies
   A and B that both return decisions, produce a combined decision
   set with both flowed through. Currently the registry already does
   this implicitly: multiple registered interventions of the same
   variant fan out into separate decisions per intervention. The
   primitive earns its place when cross-variant aggregation arrives
   (e.g., a pass that consumes both Nullability and UniqueIndex
   decisions to produce a unified column-level annotation).
4. **`wrap`** — instrumented strategy. Decorate a strategy with
   logging / lineage / telemetry. Currently the lineage-event
   discipline is inlined into each pass driver; lineage is therefore
   not strategy-scoped but pass-scoped (correct for passes that
   coordinate multiple strategies). The primitive earns its place
   if strategies become independently observable — e.g., per-strategy
   lineage subtrails, per-strategy diagnostics — which is not yet
   the case.
5. **`lift`** — context translation. Given a strategy that decides
   on context type `'a`, produce one that decides on `'b` via a
   `'b -> 'a` projection. Currently every strategy already operates
   on its natural context (`Attribute`, `Index`, `Reference`); no
   need for a generalization. The primitive earns its place when a
   strategy is reused across different IR shapes (e.g., applying the
   same nullability rules to view columns as well as table columns,
   if views land as a Kind variant).

**Decision:** **Sketch, defer implementation.** None of the
primitives have N≥2 forced uses today — `fanOut` is the closest
(N=2 inlined instances, soon N=3 with ForeignKey), but the inlined
form is 4 lines and the function form would be 6 lines including
type annotation. The argument for landing `fanOut` now is mostly
aesthetic; the argument for deferring is "IR grows under evidence"
applied to the strategy layer itself.

The right cue to revisit: when a fourth registered-intervention
strategy lands (the fifth strategy module overall, after
ForeignKeyRules), the `fanOut` duplication crosses the threshold
where a function helps more than it costs. Then `fanOut` lands as
the first composition primitive; the others follow as their use
cases arrive.

**Reasoning / consequences:** Codifying the composition vocabulary
in advance of need would be the same speculative-architecture
failure mode the registry deferral sidesteps. Recording the sketch
preserves the thinking — the next agent encountering a fourth
registered-intervention pass driver doesn't reinvent the analysis;
they read this entry, see the threshold, and decide based on the
same empirical criterion.

## 2026-05-11 — Strategy-layer codification: empirical verdict after the fourth instance

**Status:** decided (codification confirmed, with refinements)
**Context:** Session 8's codification of the strategy layer
(2026-05-11 entries above) was promoted from implicit to explicit
based on three instances (CycleResolution, NullabilityRules,
UniqueIndexRules). The fourth instance (ForeignKeyRules +
ForeignKeyPass) was implemented under the freshly-codified pattern
to test whether the codification holds without strain. Per the
user's session-8 brief: "If the codification held for ForeignKey
without strain, the registered-intervention sub-pattern is
empirically validated. If it didn't, the codification needs
revision." This entry records the verdict.

**Verdict.** **The deep-shape codification held.** All five core
predictions carried over to ForeignKey without revision:

| Codification prediction | ForeignKey outcome |
|---|---|
| Pure functions of IR fields | ✓ `ForeignKeyRules.evaluate` is pure; reads Catalog/Profile/config; no I/O |
| Typed function-type seam | ✓ `evaluate` is the seam; `ForeignKeyPass` calls into it via the same shape |
| Structured rationale DUs cover decision space | ✓ Three DUs (Evidence, KeepReason, Outcome); 13 variants total exhaustively covering V1's signal hierarchy |
| Lineage fires only on actual decisions | ✓ Observable identity on empty policy preserved; Annotated events on decisions |
| Module name advertises domain | ✓ `<Domain>Rules` suffix; lives in `Strategies/` folder |

The pattern is empirically validated at the third
registered-intervention instance.

**Refinements surfaced.** Three findings the codification did not
anticipate; each is worth recording as a refinement.

### Refinement 1: KeepReason DUs at namespace level need `RequireQualifiedAccess` when case names overlap

**The friction.** `UniqueIndexKeepReason.PolicyDisabled` and
`ForeignKeyKeepReason.PolicyDisabled` share a case name (similarly
`EvidenceMissing`). With both DUs at the `Projection.Core` namespace
level and neither carrying `[<RequireQualifiedAccess>]`, F#
ambiguity-resolution picks one — and in `ForeignKeyRulesTests.fs`,
it picked the wrong one, generating compile errors. The fix was
qualifying every collision site with the type prefix
(`ForeignKeyKeepReason.PolicyDisabled`).

**The codification refinement.** When a strategy's KeepReason DU
shares case names with another strategy's KeepReason DU (a real
risk because `PolicyDisabled`, `EvidenceMissing`, and similar
generic names will recur across strategies), the codification
should add `[<RequireQualifiedAccess>]` to the KeepReason DU. The
rationale is the same as for `NullabilityOutcome` (DECISIONS
2026-05-09 — case-name conflict with `OverrideAction.KeepNullable`):
`RequireQualifiedAccess` keeps semantically-clean names while
preventing ambiguity.

**Action item (deferred).** Retroactively applying
`RequireQualifiedAccess` to `UniqueIndexKeepReason` and
`ForeignKeyKeepReason` (and `NullabilityEvidence` /
`KeepNullableReason` if their case names ever clash) would touch
tests and rules modules alike. Defer the refactor; capture the
discipline here as a forward rule for future strategies. When a new
strategy's KeepReason DU is written, it lands with
`RequireQualifiedAccess`. When any current KeepReason DU is next
substantively modified, retrofit `RequireQualifiedAccess` as part
of that change.

### Refinement 2: `'context` is variable-arity across strategies

**The observation.** The cross-strategy generalization the user
flagged in commit 4's admire entry is empirically real, but the
`'context` slice that flows into each strategy's `evaluate` varies
in arity:

| Strategy | `'context` slice | Why |
|---|---|---|
| Nullability | `Attribute` | Per-attribute decision, no cross-record reasoning |
| UniqueIndex | `Kind × Index` | Composite-unique candidates need the kind to disambiguate |
| ForeignKey | `Kind × Reference × Catalog` | FK decisions reach across kinds (target lookup, cross-schema check) |

ForeignKey takes the **catalog itself** as an argument, which the
other two do not. This is a structural difference: FK decisions are
the first instance of a strategy that **reaches across the catalog**
rather than deciding locally per-record. The codification's
predicted uniform `(interventionId, config, context, profile) →
decision` shape is technically uniform if `context` is allowed to be
any tuple — but the practical signatures differ because what
`context` *means* differs.

**The codification refinement.** Strategy modules within the
registered-intervention sub-pattern share the *signature shape*
`(interventionId, config, ...record-or-record-bundle..., profile) →
decision`, where the record-or-record-bundle is *whatever IR
context the rule needs*. The codification's prediction of a
**uniform single-context** signature was too narrow; the prediction
of a **uniform shape** (named arguments, fixed positions for
`interventionId` first and `profile` last) holds.

**Generic alias deferred.** The cross-strategy alias
`type StrategyEvaluator<'context, 'config, 'decision> = string * 'config * 'context * Profile -> 'decision`
would absorb all three signatures with `'context` as `Attribute`,
`Kind * Index`, or `Kind * Reference * Catalog` respectively. At
N=3 the alias is aesthetic; at N=4 (when a fourth
registered-intervention strategy lands), the alias earns its place
as a way to name the shape and make composition primitives
(`fanOut`, `fallback`, etc.) typeable. Defer; the threshold is
explicit.

### Refinement 3: Audit dividend on `MissingTarget`

**The observation.** V2's `ForeignKeyKeepReason.MissingTarget` has
no V1 counterpart — V1's `ForeignKeyEvaluator` silently skips
references to missing targets. Surfacing the missing target as an
explicit keep-reason produces an audit-trail entry V1 lacked: every
FK decision now has a structured reason, even the "no decision"
cases. This is the same audit-dividend pattern that surfaced in the
2026-05-09 entry "Annotated events with documented skip reasons" —
applied to the strategy layer's outcome DUs rather than to lineage
events.

**The codification refinement.** Where a V1 component silently
skips work, V2's strategy module **should surface the skip as a
named keep-reason variant** in the outcome DU. The audit chain
gains a structured reason; the algebra gains a total decision
function (every input produces a decision); the V1↔V2 differential
gains a skip-with-rationale Behavioral assertion rather than a
ghost in V1's code. Three instances now: SymmetricClosure (Annotated
skip events on the lineage trail), NullabilityEvaluator (Skip cases
on V1 parity tests where V2 diverges), ForeignKeyEvaluator
(MissingTarget keep-reason variant). The pattern's general; the
codification absorbs it as a fourth core prediction:
**total decisions, named skips.**

**Reasoning / consequences.** The codification was descriptive
(three instances at session start) and is now empirically validated
(four instances at session end). Three refinements landed — none
of them invalidated the deep shape; each one strengthened the
codification by surfacing a non-obvious detail. The codification
now reads as: pure functions, typed seam, structured rationale DUs
(KeepReasons under `RequireQualifiedAccess`), lineage events on
actual decisions, module name advertising the domain, total
decisions with named skips. Future strategy migrations have a
sharper rubric.

The user's session-8 framing ("the test of whether session 8
succeeded is whether the fourth strategy migration fits cleanly")
is empirically met. Session 9+ rich-profiling and Faker-style
emission inherit a strategy layer that is named, observable in the
file system, codified with documented refinements, and validated
on its central case.

**Shared trigger across the two deferrals.** The composition
vocabulary deferral (the 2026-05-11 sketch entry) and the generic
`StrategyEvaluator` alias deferral (refinement 2 above) now have a
single shared cash-out point: the **next registered-intervention
strategy migration** (the fifth strategy module overall, the fourth
registered-intervention instance). At that moment both questions
are decided empirically — `fanOut` either earns its place from a
fourth duplicated inlining, and the generic alias either surfaces
as a useful naming for the four observed signatures or remains
aesthetic. Recording the shared trigger so the next migration
agent doesn't decide the two questions in isolation.

## 2026-05-12 — Rich-profiling session 9: surfacings beyond the original plan

**Status:** decided (operating discipline; future-session direction)
**Context:** Session 9 opened the rich-profiling vector — Profile
gains its first distribution evidence type, validated end-to-end
through a sibling Π. The session-9 brief laid out a six-commit
shape; what arrived along with the planned work was a small set of
findings that reshape sessions 10+ and the architecture's claims.
The reflection commit's job is to write them down.

**Finding 1: V1 is the empty-set source for distribution evidence.**
The biggest realization. Before session 9 we treated "V1 has the
shape; V2 expresses it cleanly" as the migration archetype. V1's
profiling is *entirely* binary-question outcomes — nulls /
duplicates / orphans, yes/no plus a count. There is **no V1
distribution evidence to migrate.** This is the first admire entry
that surfaces V1 absence as the gap, not V1 logic to lift.

The architectural consequence: rich profiling is **not a migration,
it's growth.** V2 is now extending its capability beyond V1's
substrate, with V2-defined JSON shapes, V2-only adapters, and
V2-only consumers. Future evidence types (numeric distributions,
temporal density, joint statistics) follow the same template — no
V1 source to mirror; the V2 boundary is data the V2 shape itself
prescribes. This reframes the multi-session arc: sessions 10+ are
expanding the algebra into territory V1 never reached, not
migrating V1 work.

**Finding 2: Π signature variation is a real architectural axis.**
SSDT and JSON take `Catalog -> string`. Distributions takes
`Catalog -> Profile -> string`. The variation isn't an
inconsistency; it's a refinement of A18 (no policy parameter on Π).
The three substantive inputs are Catalog, Policy, Profile (A6); a
Π consumes whichever subset of `Catalog × Profile` it needs,
**but never Policy** — Policy lives in passes, not emitters. The
three-emitter empirical evidence:

| Π | Signature | Inputs consumed |
|---|---|---|
| RawTextEmitter (SSDT) | `Catalog -> string` | Catalog only |
| JsonEmitter | `Catalog -> string` | Catalog only |
| DistributionsEmitter | `Catalog -> Profile -> string` | Catalog × Profile |

This parallels the strategy-layer finding from session 8 (refinement
2: the `'context` slice into `evaluate` is variable-arity across
strategies). Same pattern, same empirical resolution: the deep
shape (no policy; pure function of substantive inputs) holds; the
practical signatures differ because what each Π *consumes* differs.
Future Π — Faker, anomaly reports, etc. — pick the signature that
matches their consumption pattern.

**Finding 3: Composition via `Result.bind` is the right granularity
for sibling adapters.** Two adapters, both pure functions of
`Catalog * JSON-string * Profile -> Result<Profile>`. The caller
composes them in any order:

```fsharp
ProfileSnapshot.attach catalog snapshotJson
|> Result.bind (ProfileStatistics.attach catalog distributionsJson)
```

Or reverse, or interleaved, or with intermediate transformations.
At N=2 adapters the explicit `Result.bind` is cheap and visible.
A top-level orchestrator earns its place when N≥3 adapters all
need to compose with the same predictable order; for now the
explicit composition documents itself. The session-8 composition-
vocabulary deferral discipline applies symmetrically to adapters.

**Finding 4: Truncation contracts are structural commitments, not
ad-hoc validation.** `CategoricalDistribution.create` enforces
"`IsTruncated = false ⇒ DistinctCount = Frequencies.Length`" —
the structural meaning of "I observed every distinct value." The
`IsTruncated` flag distinguishes "captured all 3" from "captured
3 of N." Without this discipline, downstream consumers (the
eventual anomaly strategy, the Faker emitter) would have no way
to distinguish complete from partial vocabularies. The validation
is small; the consumer-side benefit is structural — pattern-match
on the flag, get the contract.

Future evidence types inherit the discipline: every distribution
DU variant carries a structural answer to "is this evidence
complete or sampled?" Numeric histograms will have an analogous
flag; temporal evidence may have its own ("date-range observed
in full" vs "sampled within range"). Codify in the second
distribution variant's design (session 10).

**Finding 5: First-consumer smallness validates the architecture
better than first-consumer ambition would have.** DistributionsEmitter
is small — ~200 lines. Building it as a sibling Π rather than a
one-off formatter cost almost nothing extra (the file structure,
the project setup, the test discipline) and bought the
empirically-validated claim that emission is parameterized over the
enriched IR for the third time. The user's framing was prescient:
"the discipline of building it as one matters more than its size."
Future sessions follow this rule — when a new evidence type's first
consumer is a diagnostic, build it as a sibling Π regardless of size.

**Finding 6: The closed-DU `AttributeDistribution` shape absorbs
new variants cleanly, with one expected friction.** Adding `Numeric`
and `Temporal` variants in sessions 10+ extends the DU and the
adapter's `Kind` dispatch and the emitter's `match` arms. The F#
incomplete-match warning fires when there's only one variant (as it
did when implementing `tryFindCategorical`); the workaround is a
defensive second branch (`AttributeDistribution.Categorical _ ->
None`). When the second variant lands, the second branch becomes a
real `Numeric _ -> ...` case and the friction disappears. Document
the workaround so session 10 understands why the redundant-looking
branch exists.

**Direction signals for sessions 10+.**

  - **Numeric distribution shape question.** Histograms (binned
    counts), percentiles (5/25/50/75/95), or range (min/max)? Or
    all three? The choice is the design question for session 10's
    admire. Recommendation: percentile + range as the foundational
    shape (smaller; more useful for synthetic generation),
    histograms as a follow-up if a real consumer demands.
  - **First substantive consumer of a distribution.** Session 11's
    proposed work is the first distribution-aware strategy
    (e.g., a uniqueness strategy that consults distinct-count to
    distinguish "candidate uniqueness" from "spurious uniqueness").
    Per "each evidence type lands when its first consumer arrives,"
    session 11's strategy choice validates whether the evidence
    shape is fit for purpose. If the strategy's logic doesn't fit
    cleanly into the codified strategy layer with the new evidence
    type, the codification gets refinement #4.
  - **The shared trigger from session 8 still holds.** Session 11
    is the projected cash-out point for both the composition
    vocabulary deferral and the generic `StrategyEvaluator` alias.
    Three deferred decisions converge there; the session-11 agent
    inherits a sharp empirical setup.
  - **Faker emitter waits for the third evidence type.** A
    synthetic generator needs at least categorical + numeric
    + cardinality to produce plausible data. Faker is session 12+
    work; sessions 10 and 11 lay the foundation.

**Reasoning / consequences.** Session 9's job was "extend and
consume." It did that, but it also surfaced the V1-empty-source
framing, the Π-signature-variation refinement, the truncation-
contract discipline, and the small-first-consumer validation —
findings the session-9 brief didn't anticipate but that will shape
sessions 10+. Recording them here means the next agent starts with
the empirical context, not the original plan; the same reflective
discipline that made session 8's codification empirically validated
applies here to the rich-profiling agenda.

## 2026-05-13 — Admire entries fall on a spectrum (V1-migration / V2-growth / hybrid)

**Status:** decided (operating discipline)
**Context:** Up through session 8, every admire entry surfaced V1
logic to migrate, V1 fields to absorb, V1 contracts to honor — the
"V1 has the shape; V2 expresses it cleanly" archetype. Session 9's
rich-profiling admire (ADMIRE.md 2026-05-12) was the first that
surfaced V1 *absence* as the gap and V2 architectural growth as
the work. The session-9 reflection (DECISIONS 2026-05-12 Finding
1) named this as a reframe of the migration discipline.

**Decision:** Admire entries fall on a three-mode spectrum, named
by what V1 contributes:

  1. **V1-migration mode.** V1 has the logic; V2 expresses it
     cleanly. The admire entry's "Existing test coverage" section
     is rich (V1 tests categorized as Behavioral / Property /
     Differential / Skip). The migration completes when V2
     satisfies V1's contracts. Examples:
     `EntitySeedDeterminizer`, `NullabilityEvaluator`,
     `UniqueIndexDecisionOrchestrator`, `EntityDependencySorter`,
     `ForeignKeyEvaluator`. Every admire through session 8 was
     this mode.
  2. **V2-growth mode.** V1 has nothing; V2 extends. The "Existing
     test coverage" section is structurally absent — V1 has no
     contracts to honor — and replaced by a "what V2 needs and
     why" section plus V2-only contract tests. The migration is
     not migration but architectural growth, validated by
     end-to-end consumption rather than V1 differential.
     Example: rich profiling (session 9, ADMIRE.md 2026-05-12).
  3. **Hybrid mode.** V1 has partial coverage; V2 extends beyond
     it. The admire splits into "what V1 gives us" (with the
     V1-migration test discipline) and "what V2 adds" (with the
     V2-growth shape). When numeric distributions arrive in
     session 10 they would technically be hybrid — V1 has *zero*
     numeric evidence (a strict subset of the rich-profiling
     admire's gap analysis), so they cleanly inherit the V2-growth
     mode of the parent admire.

**Future admire entries name their mode at the top.** The mode
tells readers which template to use:

  - V1-migration: original ADMIRE format (What it does → V2
    placement → Inputs/outputs → Existing test coverage →
    Migration path → Edges).
  - V2-growth: extended format (What V1 collects → What V1 doesn't
    → What V2 needs → V2 extension shape → V2-only tests →
    Multi-session agenda → Edges).
  - Hybrid: both sections, side-by-side; the boundary between V1
    coverage and V2 extension explicit.

**Reasoning / consequences.** Naming the modes explicitly does
two things. First, it lets future admire authors pick the right
template upfront rather than discovering the mismatch midway.
Second, it makes the V1-vs-V2 boundary structural at the
documentation level — readers can scan ADMIRE.md and see at a
glance which entries are migrations and which are growth, and the
test discipline expectations follow accordingly. The session-9
admire stands as the V2-growth template; future V2-growth admires
follow its structure.

#### 2026-05-19 (session 23 amendment) — status framework extension for multi-session chapters in flight

The session-22 cross-document audit surfaced a framework gap: the
admire status framework was designed for chapters that complete in
a single bounded arc. Chapters that run for many sessions (the
OSSYS adapter chapter, for instance — five substantive slices
across sessions 18–22) accumulate work that is **clearly past
"chapter-open scoping"** but **not yet "extracted (...) confirmed"**.
The framework had no status that fit the in-flight state.

Without a fitting status, multi-session chapter entries either
understate (`chapter-open scoping` after five slices misleads) or
overstate (`extracted (differential confirmed)` premature when the
chapter has known remaining substantive work). The OSSYS ADMIRE
entry sat at `chapter-open scoping (session 17)` through session
22 because no better status existed in the framework.

**Decision: extend the framework with a partial-extracted status
for multi-session chapters in flight.** The status string:

  **`extracting (in flight, N slices)`**

where `N` names the count of substantive slices the chapter has
landed at the time of writing. The status is **explicit about
in-flight-ness**: future readers know the entry is current as of
N slices, not stable.

Naming choices considered:

  - `extracting (in flight, N slices)` — chosen. Active verb form
    ("extracting") symmetric with the past form ("extracted").
    `(in flight, N slices)` parameter gives concrete state. Reads
    naturally: `extracting (in flight, 5 slices)` for the OSSYS
    chapter at session 22 close.
  - `partially-extracted (chapter in flight)` — rejected. Compound
    past form is awkward; "partially-extracted" reads as a static
    fraction rather than active progression.
  - `in-progress-extraction` — rejected. Too long; reads as a
    noun phrase rather than a status.

The chosen form pairs cleanly with the existing four status
strings:

  | Status | When |
  |---|---|
  | `admired (placement decided)` | V2 placement chosen; no implementation yet |
  | `chapter-open scoping (session N)` | Chapter just opened; strategic frame + ADMIRE chapter scope landed; no substantive slices yet |
  | **`extracting (in flight, N slices)`** | **Chapter past chapter-open; substantive slices landing; not yet at chapter close** |
  | `extracted (differential confirmed)` | Chapter complete; differential tests confirm the contract |

**Update protocol.** When a chapter close lands, the status moves
from `extracting (in flight, N slices)` to
`extracted (differential confirmed)` (or the V2-growth /
hybrid-mode equivalent). When a substantive slice ships within
the chapter, the entry's `N` updates to reflect the new count.
Updates happen as part of each session's work, not just at chapter
close — keeping the status accurate is the responsibility of the
session that lands the slice.

**Worked example.** The OSSYS catalog producer entry transitions
from `chapter-open scoping (session 17)` → `extracting (in flight,
5 slices)` (session 23 application). The chapter close in
session 25 will transition it to whatever extracted-status applies
at completion. Future multi-session chapters follow the same
pattern.

**Reasoning / consequences.** The extension closes the
framework gap surfaced by the session-22 audit. Multi-session
chapters can keep their ADMIRE status accurate without forcing
premature `extracted` claims or misleading `chapter-open scoping`
holdovers. The framework is small (one new status string); the
update discipline is small (per-slice update of `N`); the audit-
trail compounds because future agents reading ADMIRE see the
chapter's progression at a glance.

The entry-template implications: the in-flight status's
"Existing test coverage" subsection should accumulate as fixtures
land, rather than being purely forward-looking. Forward-looking
shape ("V2's test surface for the adapter (when implemented):")
applies to `chapter-open scoping`; landed-shape ("V2's test surface
includes:") applies to `extracting (in flight, N slices)`.

## 2026-05-13 — Session 10 reflection: closed-DU expansion validated; forward signals

**Status:** decided (operating discipline; session 11 hand-off)
**Context:** Session 10's job was to land the second
`AttributeDistribution` variant (Numeric) end-to-end through every
layer of the rich-profiling pipeline. The session-9 reflection
asked whether the closed DU's seam was positioned correctly; the
session-10 brief framed adding the second variant as the
codification's "first real test." This entry records the answer
and the forward signals for session 11.

**Did the closed DU accommodate the second variant cleanly?** Yes.
The exhaustiveness checks lit up exactly where the codification
predicted, and the variant addition required updates at exactly
the sites a closed DU promises. The friction was zero on the
structural axis; the rough edges that surfaced are minor
operational concerns, not architectural ones.

**Empirical record of the closed-DU expansion.** Adding
`Numeric of NumericDistribution` to `AttributeDistribution`
required updates at:

  1. `Profile.tryFindCategorical` — F# exhaustiveness error;
     added `Numeric _ -> None` branch.
  2. `Profile.tryFindNumeric` — new helper, structurally symmetric
     to its categorical sibling.
  3. `Profile.tryFindDistribution` — *new* variant-agnostic helper
     that emerged as the natural lookup primitive for the
     emitter. Returns the first registered distribution by key,
     regardless of variant. Useful primitive; consumers (Faker,
     anomaly strategies) will reuse it.
  4. `DistributionsEmitter.writeDistribution` — F# exhaustiveness
     error; added `Numeric -> writeNumeric` branch.
  5. `ProfileStatistics.parseDistribution` — string-dispatch on
     "Kind" field; added "Numeric" branch alongside "Categorical".
     Coordinate resolution shared (single-function dispatch held).

Five sites; five updates; F# enforcement at the compile level on
sites 1, 2, and 4; deliberate update at sites 3 and 5. No
surprises. The codification's prediction (closed-DU expansion is
clean when the seams are positioned at the variant level, not at
the consumer level) held.

**Findings beyond the plan.**

1. **`Profile.tryFindDistribution` is a useful primitive that
   wasn't in the brief.** When adding the variant to the emitter,
   I reached for a variant-agnostic lookup rather than a chain of
   per-variant lookups. The helper emerged because the emitter
   doesn't care which variant the IR carries — it just needs to
   render whatever's there. Future Π (Faker, anomaly reports) and
   future strategies (distribution-aware tightening) will likely
   want the same primitive. The pattern: per-variant helpers for
   consumers that care about the shape; variant-agnostic helper
   for consumers that just need to dispatch.
2. **Variants currently share an `AttributeKey` field convention.**
   Both `CategoricalDistribution` and `NumericDistribution` carry
   `AttributeKey : SsKey` as their first field. This convention
   lets `tryFindDistribution` extract the key uniformly via a
   small private helper (`distributionKey`). If a future variant
   diverges (e.g., `JointDistribution` keyed by *two* attributes),
   the variant-agnostic lookup needs revision — the key isn't a
   single SsKey anymore. Document the convention so the next
   variant author knows the implicit rule and can surface a
   refactor if their variant breaks it.
3. **Intermediate-state commits worked but require discipline.**
   Between commit 2 (variant added with placeholder rendering)
   and commit 4 (real rendering), the emitter silently dropped
   numeric data. The placeholder was documented and tests
   confirmed the eventual fix — but the intermediate state was
   technically incorrect. Future variant additions should weigh
   atomic-but-incomplete (this session's approach) against
   bundle-everything-in-one-commit. The split approach is more
   reviewable and surfaces F# exhaustiveness issues at the right
   moment; the bundled approach lands the working feature
   sooner. No forced choice; surface the trade-off.
4. **Decimal as the percentile value type was right.** No
   floating-point drift surfaced; T1 byte-determinism held across
   repeats. The choice was made on session 10's first commit
   based on V2's existing decimal use; the milestone validates it
   end-to-end.

**The structural-commitment pattern's reach validated.**
`NumericDistribution.create` rejects monotonicity violations at
construction. The adapter's `Result.bind` chain surfaces the
rejection as an adapter error. The end-to-end test confirms a bad
fixture halts the pipeline with the constructor's error code, not
a silent degenerate Profile. The full pipeline trusts that every
`NumericDistribution` value satisfies the contract because every
path to its existence checked it. The pattern (AXIOMS.md
2026-05-12) compounds: every layer downstream gets cheaper to
write because invariants ride on every value.

**Forward signals for session 11.**

  - **The deferred-decisions cash-out trigger fires.** Session 11's
    first distribution-aware strategy is the fourth
    registered-intervention strategy (after Nullability,
    UniqueIndex, ForeignKey). Both deferred decisions from
    session 8 cash out together: the composition vocabulary
    (`fanOut`, `fallback`, etc.) and the generic
    `StrategyEvaluator` alias. The shared trigger discipline
    documented in DECISIONS 2026-05-11 should be honored —
    decide both questions empirically when the migration ships,
    not in isolation.
  - **The codified strategy layer's third real test.** Session 11's
    strategy migration uses the codification (DECISIONS 2026-05-11)
    as its rubric. Three previous registered-intervention strategies
    (Nullability, UniqueIndex, ForeignKey) validated the codification
    on session-8 evidence types (null counts, duplicate booleans,
    orphan flags). Session 11's strategy validates it on
    distribution evidence — a structurally richer Profile slice.
    If the codification's `evaluate` shape accommodates the new
    evidence type without revision, the codification's reach
    extends to rich profiling.
  - **Distribution-aware strategy candidates.** Several substantive
    options for session 11's strategy migration:
      - Categorical-aware uniqueness: distinct-count vs row-count
        heuristic for "candidate uniqueness" vs "spurious
        uniqueness." Per-attribute decision; consumes
        Categorical evidence.
      - Numeric-bounded mandatory check: an attribute with a
        narrow numeric range (P95 / P99 close to Max) might
        warrant a tighter constraint than a wide-tailed one.
      - Cardinality-aware FK: when the FK target's distinct
        values are below a threshold, the FK should perhaps be
        held to a stricter standard.
    Session 11's admire selects one (probably the categorical
    one — simplest seam, most testable).
  - **The closed-DU shape continues to hold.** Adding numeric
    didn't reveal a need for refactoring. Adding the third variant
    in a future session is the same shape: extend
    `AttributeDistribution`, update `tryFindDistribution` and
    `parseDistribution` and `writeDistribution`, write a
    smart constructor with structural-commitment validation,
    extend the milestone test. The pattern's repeatable.

**Reasoning / consequences.** Session 10's job was extension; the
discipline it tested was whether session 9's foundations would
support a second variant. They did. The closed-DU codification
(implicit before, explicit after session 8) absorbed the variant
without revision. The structural-commitment pattern's reach
extended through the full pipeline. The composition discipline
(`Result.bind` for adapter chaining; sibling-Π for emitter
extension) carried over. Session 11 inherits a strategy layer and
a rich-profiling foundation that have both been empirically
validated; the deferred decisions from session 8 cash out there.
Hold the cadence.

## 2026-05-13 — Closed-DU expansion: empirical confirmation, not a foregone conclusion

**Status:** decided (operating discipline; future-author trust signal)
**Context:** Session 10 added the second variant
(`Numeric of NumericDistribution`) to `AttributeDistribution`. The
expansion was clean — five sites required updates, F# enforced
exhaustiveness on three of them, two were deliberate non-matches,
no surprises. The session-10 reflection logged this as an empirical
record. This entry promotes it from a single-session finding to a
trust signal future authors can rely on.

**Decision:** **A clean closed-DU expansion is evidence, not
inevitability.** When the second variant lands without forcing a
DU reshape, splitting, or new-context threading through old call
sites, the seam was positioned correctly — the codification works
the way it claims. Future authors absorb the conventions
(`AttributeKey`-as-first-field for `AttributeDistribution`; the
analogous patterns elsewhere in V2) and trust that adding a third
variant follows the same shape.

**The empirical test for "well-positioned seam":**

  1. Adding a new variant requires updates **only** at sites that
     pattern-match on the DU. F# exhaustiveness errors are the
     compiler's enforcement.
  2. The new variant uses the **same shape of construction
     validation** as existing variants (smart constructor returning
     `Result<'a>`, structural-commitment invariants).
  3. The new variant uses the **same shape of consumer dispatch**
     in adapters and emitters (string-Kind branch in adapters;
     match-arm in emitters).
  4. **No callers outside the variant's own module need to change**
     to support the new variant beyond the exhaustiveness updates.
     If a caller's logic needs reshaping, the seam is wrong.

If a future variant addition violates any of these — e.g., the new
variant doesn't share the `AttributeKey` convention; the
construction validation has fundamentally different shape; consumers
need to thread new context through old sites — surface the
divergence and consider whether to refactor the DU before
proceeding.

**Reasoning / consequences.** The codification (DECISIONS
2026-05-11) made the strategy layer's shape explicit; this entry
makes the closed-DU expansion's empirical-test discipline explicit.
Together they let future agents work confidently inside the
patterns rather than re-deriving them from first principles. If the
patterns ever stop working, the discipline above is how the next
agent notices.

## 2026-05-13 — Emergent primitives earn their place through multi-consumer demand

**Status:** decided (operating discipline)
**Context:** Session 10 surfaced `Profile.tryFindDistribution` as a
useful variant-agnostic lookup helper that wasn't in the original
plan. It was added because the emitter needed it; future Π
(Faker, anomaly reports) and future strategies will likely reuse
it. The session-10 reflection (Finding 1) noted it as a small
example of a real principle worth naming.

**Decision:** **A primitive earns its place when a second consumer
needs it, not when the first one does.** This is the same threshold
the strategy-layer codification (DECISIONS 2026-05-11) and the
composition vocabulary deferral (DECISIONS 2026-05-11) both apply.
Generalize the discipline:

  - **First consumer** of a hypothetical helper: write the inline
    code. The cost of duplication-of-one is lower than the cost of
    speculative abstraction.
  - **Second consumer**: extract the helper. The duplication is now
    real; the abstraction's shape is empirically grounded; the
    third consumer (when it arrives) lands cleanly into the
    extraction.
  - **N-th consumer** (where N >= 3): the helper is canonical; new
    consumers reuse without question.

The principle is implicit in "IR grows under evidence" applied to
helper extraction; making it explicit gives reviewers a clean test
for "should I extract this?" and authors a clean test for "is this
ready to be extracted?" The answer in both cases: count consumers,
not anticipated callers.

**Worked examples in V2:**

  - `Profile.tryFindCategorical` (session 9): extracted because the
    emitter and the (eventual) strategy layer both needed it. Two
    consumers established at session 9.
  - `Profile.tryFindDistribution` (session 10): extracted because
    the emitter's variant-agnostic lookup pattern was the natural
    shape for the second variant. Two consumers anticipated; today
    only the emitter uses it. Borderline at extraction time, but
    the pattern's reuse path is clear (Faker emitter, anomaly
    strategies).
  - `fanOut` composition primitive (deferred): inlined in two pass
    drivers (NullabilityPass, UniqueIndexPass) at session 7; a
    third (ForeignKeyPass) at session 8. Threshold met; cash-out
    in session 11.
  - `StrategyEvaluator` alias (deferred): three strategies share
    the signature shape; cash-out in session 11.

**Counter-examples:**

  - The strategy registry mechanism (deferred at session 8): zero
    consumers have demanded it. Defer until N=4-6 strategies make
    the registry's lookup-by-name pattern useful.
  - Faker emitter (deferred to session 12+): the synthetic
    generator consumes evidence types that don't yet all exist.
    The "consumers" for distribution evidence today are the
    diagnostic emitter and (future) strategies; Faker waits.

**Reasoning / consequences.** Naming the principle prevents two
common failure modes: speculative abstraction (extracting on the
first consumer because "we might need it") and speculative
deferral (refusing to extract on the second consumer because "two
isn't enough yet"). Two consumers is the threshold; it's
empirically grounded; future authors can apply it without
re-litigating.

## 2026-05-13 — Decimal is the default for continuous statistical evidence

**Status:** decided (precedent)
**Context:** Session 10 chose `decimal` over `float` (or `double`)
for the percentile values in `NumericDistribution`. The choice was
made on session-10 commit 1 with brief rationale; the milestone
test (session-10 commit 5) validated it end-to-end with byte-
identical determinism across repeats. The session-10 reflection
flagged this as a small precision call worth marking as a
precedent.

**Decision:** **`decimal` is V2's default representation for
continuous statistical evidence.** New numeric evidence types
(temporal density bins, joint distribution coordinates, future
statistical primitives) use `decimal` unless the consumer has a
real reason to deviate.

**Rationale:**

  1. **Determinism across platforms.** `decimal` is a
     fixed-precision type with deterministic arithmetic across
     hosts; T1 byte-identity (a load-bearing V2 commitment, A17
     amended) requires this. `float` arithmetic varies subtly
     with CPU / runtime / compiler; bit-identical output is not
     guaranteed.
  2. **Exact representation of source values.** V2 attributes are
     `Integer` or `Decimal` at the IR level; both convert to
     `decimal` exactly. `float`/`double` introduce silent
     precision drift on integer values exceeding 2^53 and on
     decimal values that are not powers-of-two fractions.
  3. **Consistency with existing V2 numeric use.** V2's
     `NullBudget : decimal`, the existing numeric configuration,
     uses `decimal`. Distribution evidence consumed by the same
     algebra should use the same representation.

**When to deviate:** if a future consumer has structural reasons
that demand floating-point (e.g., interfacing with a downstream
numerical library that accepts only `double`), the deviation is
a documented exception in DECISIONS, not a silent re-litigation.
The default holds; the exception is explicit.

**Worked precedent:**

  - `NumericDistribution.{Min, P25, P50, P75, P95, P99, Max}`:
    `decimal`. Session 10 commit 1.
  - Future temporal evidence with continuous date/time values:
    consider `DateTimeOffset` for the date component (deterministic
    string roundtrip is the existing V2 convention from
    `ProbeStatus.CapturedAtUtc`); use `decimal` for any derived
    statistical scalar.
  - Future joint-distribution coordinates: `decimal × decimal` for
    paired numeric attributes.

**Reasoning / consequences.** Marking the precedent prevents the
question from reopening when the next numeric evidence type lands.
The choice is small but load-bearing for T1; making it canonical
saves the next agent a deliberation cycle and ensures consistency
across the rich-profiling agenda's growth.

## 2026-05-13 — Composition vocabulary cash-out: `fanOut` codified, four others deferred

**Status:** decided (deferred-decisions cash-out, session-8 trigger)
**Context:** Session 8 sketched five strategy-composition primitives
(`fanOut`, `fallback`, `accumulate`, `wrap`, `lift`) and deferred
implementation pending the two-consumer threshold (DECISIONS
2026-05-11 — composition vocabulary sketch). Session 11's fourth
registered-intervention strategy (`CategoricalUniqueness`) fired
the trigger condition. This entry records the cash-out per the
empirical pattern across all four pass drivers.

**Empirical state at the trigger:**

| Primitive | Consumers | Verdict | Disposition |
|---|---|---|---|
| `fanOut` | **4** (Nullability, UniqueIndex, ForeignKey, CategoricalUniqueness) | Threshold met by wide margin | **Codified** in `Projection.Core/Strategies/Composition.fs` |
| `fallback` | 0 | No strategy falls back to another | Deferred |
| `accumulate` | 0 | No strategy aggregates across other strategies | Deferred |
| `wrap` | 0 | No instrumented strategies | Deferred |
| `lift` | 0 | No context translation needed | Deferred |

**Decision: codify `fanOut`; defer the other four.** The
two-consumer threshold (DECISIONS 2026-05-13 — emergent primitives)
is the test; `fanOut` passes by a wide margin (4 consumers); the
others have no consumers and stay deferred until their first one
arrives.

**`fanOut` shape.** A `FanOutConfig<'context, 'config, 'decision,
'decisionSet>` record carries the strategy-specific functions
(intervention filter, sorted-context enumerator, evaluate seam,
empty decision set, decision-set wrapper, lineage event builder).
The `fanOut` function is the canonical iteration discipline:
observable identity on empty policy; per-(context × intervention)
fan-out; one event per decision; deterministic ordering; lineage
emission via `Lineage.tellMany`.

**Refactoring impact.** All four pass drivers refactored to
delegate to `Composition.fanOut`. The `run` functions become thin
wrappers that construct the FanOutConfig (capturing strategy-specific
context like `ForeignKeyRules.evaluate`'s catalog parameter via
closure) and invoke the primitive. Pass-driver behavior unchanged
(all 570 tests still pass after the refactor); 8 new tests in
`CompositionTests.fs` exercise the primitive directly via a
synthetic minimal strategy.

**Why the four others stay deferred:**

  - **`fallback`**: every strategy returns a *total* decision (one
    of the closed-DU outcome variants for every input combination
    — the "total decisions, named skips" core prediction from
    session-8 codification refinement 3). No strategy needs a
    fallback because no strategy ever returns "no decision."
    `fallback` becomes useful when a strategy can refuse to decide
    and another picks up — currently no consumer.
  - **`accumulate`**: the closed `TighteningIntervention` DU keeps
    each strategy's decision set independent. A pass that produces
    a unified per-attribute annotation by merging Nullability +
    CategoricalUniqueness decisions (e.g., a "column metadata"
    annotation pass) would need `accumulate`. No such pass exists.
  - **`wrap`**: per-strategy lineage subtrails / instrumentation /
    timing is not yet a need. Lineage discipline lives in the pass
    driver via `BuildEvent`; per-strategy diagnostics
    (`ProfilingInsight`-style) are not yet modeled in V2.
  - **`lift`**: every strategy operates on its natural context
    (`Attribute`, `Index`, `Reference`). No strategy's logic is
    reused across different context shapes. The eventual scenario
    — a Nullability-style strategy applied to view columns as a
    Kind variant — doesn't exist because views aren't yet in V2.

**Forward triggers:**

  - `fallback` ships when a strategy's `evaluate` returns
    `Outcome.Defer` (or equivalent "no opinion" variant) and a
    second strategy picks up.
  - `accumulate` ships when the second pass needs to consume
    multiple-strategy decisions at once.
  - `wrap` ships when per-strategy diagnostics emerge as a real
    concern (likely tied to the eventual `Diagnostics` writer
    monad).
  - `lift` ships when a strategy is reused across different IR
    granularities (e.g., a Nullability rule on view columns).

**Reasoning / consequences.** The codification of `fanOut`
empirically validates the strategy-layer codification's
ergonomics. The four pass drivers are now mechanically uniform:
they construct configuration records and delegate to one canonical
primitive. The deferral discipline (DECISIONS 2026-05-13 — emergent
primitives) gets its first real test: four candidates were
considered together; one was extracted; four were deferred with
explicit forward triggers. Future composition primitives follow
the same protocol — count consumers; codify when the threshold
hits; defer with forward triggers when it doesn't.

## 2026-05-13 — Generic StrategyEvaluator alias cash-out: codified

**Status:** decided (deferred-decisions cash-out, session-8 trigger)
**Context:** The second of two deferred decisions converging at
session 11 (DECISIONS 2026-05-11 — shared trigger). The first
(composition vocabulary) cashed out as `Composition.fanOut`. This
entry decides the second: the generic
`StrategyEvaluator<'context, 'config, 'decision>` alias.

**Empirical state at the trigger.** Four registered-intervention
strategies' `evaluate` signatures, mapped against the candidate
shape `string × 'config × 'context × Profile → 'decision`:

| Strategy | Natural rules-module signature | Fits the shape? |
|---|---|---|
| `NullabilityRules.evaluate` | `string -> NullabilityTighteningConfig -> Attribute -> Profile -> NullabilityDecision` | **Exactly** (with `'context = Attribute`) |
| `UniqueIndexRules.evaluate` | `string -> UniqueIndexTighteningConfig -> Kind -> Index -> Profile -> UniqueIndexDecision` | **With minor argument-tupling** (`'context = Kind × Index`) |
| `ForeignKeyRules.evaluate` | `string -> ForeignKeyTighteningConfig -> Kind -> Reference -> Catalog -> Profile -> ForeignKeyDecision` | **With closure-adaptation** (catalog captured by `FanOutConfig.Evaluate` lambda; `'context = Kind × Reference`) |
| `CategoricalUniquenessRules.evaluate` | `string -> CategoricalUniquenessConfig -> Attribute -> Profile -> CategoricalUniquenessDecision` | **Exactly** (with `'context = Attribute`) |

3 of 4 strategies fit the shape exactly or with minor tupling. 1
of 4 (ForeignKey) has an extra argument that adapts cleanly via
closure when constructing the FanOutConfig. The shape is real;
the divergence is handled mechanically; the codification's
"uniform signature shape but variable arity context" finding from
session-8 refinement 2 holds.

**Decision: codify the alias.** Per the discipline (DECISIONS
2026-05-13 — emergent primitives), the fourth empirical
confirmation earns the alias. Lands as
`type StrategyEvaluator<'context, 'config, 'decision> =
 string -> 'config -> 'context -> Profile -> 'decision` in
`Projection.Core/Strategies/Composition.fs`.

**What the alias does:**

  - **Names the canonical shape.** The four-input
    `(interventionId, config, context, profile)` shape is now
    nameable in code and conversation. Future strategy authors
    have a target signature.
  - **Types the `Composition.FanOutConfig.Evaluate` field.** The
    field becomes `StrategyEvaluator<'context, 'config, 'decision>`
    rather than the inline arrow type. Documentation and
    discoverability improve; behavior is unchanged.
  - **Lets future strategies declare conformance.** A new strategy's
    rules module can write
    `let evaluate : StrategyEvaluator<MyContext, MyConfig, MyDecision> = fun id cfg ctx prof -> ...`
    and get a compile-time check that the shape is preserved.

**What the alias doesn't do:**

  - **It doesn't force every rules module to refactor.**
    `ForeignKeyRules.evaluate` continues to take `Catalog` as a
    separate argument (its natural shape, given that FK decisions
    need cross-attribute reach for target-kind lookup). The
    FanOutConfig.Evaluate lambda closes over the catalog and
    adapts to the alias's shape. The "uniform signature shape but
    variable arity context" principle (session-8 refinement 2) is
    honored explicitly.
  - **It doesn't introduce structural enforcement beyond
    FanOutConfig.** The alias is documentary unless a strategy
    author chooses to type its `evaluate` against it.

**What might force a future revision.** If a fifth strategy's
evaluate genuinely cannot adapt to this shape (e.g., needs an
asynchronous context, returns multiple decisions per invocation,
or consumes a Diagnostics writer in addition to Profile), the
alias gets revisited. Per the codification discipline (DECISIONS
2026-05-11 — empirical verdict), divergence is a tell, not a
defeat.

**Reasoning / consequences.** Both deferred decisions from
session 8 have now cashed out. Composition vocabulary: `fanOut`
codified, four others deferred with forward triggers. Generic
alias: `StrategyEvaluator` codified as a type-level name for the
shape that's already enforced at the `FanOutConfig` boundary.
The strategy layer's codification is more thoroughly named after
session 11 than after session 8 — `fanOut` and `StrategyEvaluator`
are the new vocabulary. Future strategy migrations have less to
re-invent and more to inherit.

## 2026-05-13 — Session 11 reflection: codification's third real test passed; forward signals for session 12

**Status:** decided (operating discipline; session 12 hand-off)
**Context:** Session 11's job was the codification's third real
test — the first distribution-aware strategy
(`CategoricalUniqueness`) under the codified strategy layer +
the cash-out of two deferred decisions from session 8 (composition
vocabulary; generic alias). The session-11 brief asked whether
distribution-aware decision logic would stress the structured-
rationale DU pattern in a way binary-evidence patterns didn't.
This entry records the answer and forward signals.

**Did the codification's third real test pass?** Yes. The
codification absorbed the new evidence type, the new strategy, the
new pass driver, the composition primitive, and the generic alias
— without revision. Empirical record:

| Axis | Outcome |
|---|---|
| Closed-DU expansion (4th `TighteningIntervention` variant) | Clean — only `TighteningIntervention.id` needed exhaustiveness update; per-variant filter helpers used wildcard fall-through; closed-DU expansion empirical-test discipline (DECISIONS 2026-05-13) holds for the third time (after session 9's IsMandatory variant + session 10's Numeric variant) |
| Strategy-layer codification (4th instance) | All five core predictions held (pure functions, typed seam, structured rationale DUs, lineage discipline, `<Domain>Rules` naming + total decisions with named skips). No fourth refinement needed |
| Composition vocabulary cash-out (`fanOut`) | Earned its place at four consumers; codified; pass drivers now ~10 lines each instead of ~20 |
| Generic alias cash-out (`StrategyEvaluator`) | Earned its place; named the canonical shape; honored "uniform signature shape but variable arity context" (session-8 refinement 2) by adapting `ForeignKey`'s extra catalog argument via closure rather than forcing surgery |
| End-to-end milestone | All 585 tests pass; Categorical evidence flows through ProfileSnapshot.attach + ProfileStatistics.attach into the enriched Profile, the strategy decides per-attribute, the pass produces the decision set with full lineage discipline, sibling-Π commutativity preserved, T1 byte-determinism holds |

**Did distribution-aware decision logic stress the rationale DU
pattern?** Less than the user's brief anticipated. Three
observations:

1. **Confidence didn't surface as a separate dimension.** The
   user's forward note (session-10 brief) speculated that
   distribution-aware strategies might want a confidence concept
   alongside structured rationale ("this column's distribution
   suggests X with confidence Y"). For `CategoricalUniqueness`,
   confidence was implicitly modeled by the keep-reason variants
   themselves — `EvidenceMissing`, `VocabularyTruncated`,
   `DistinctCountBelowThreshold`, `DuplicatesObserved` are
   discrete bands of confidence (none / unsafe / insufficient /
   contradicted). The single positive variant (`EveryValueDistinct`)
   is itself a high-confidence signal. The DU absorbed the
   confidence spectrum without needing a separate scalar.
2. **Continuous evidence still discretized in the rationale DU.**
   `CategoricalDistribution.DistinctCount` is `int64` (continuous
   in the unbounded sense) but the strategy's decision flattens it
   to "above threshold" / "below threshold" / "matches total." The
   continuous evidence informs which discrete variant fires; the
   rationale DU stays discrete. This held for binary-evidence
   strategies and continues to hold here. If a future strategy
   wants to expose a numeric confidence score (e.g., "this is 80%
   likely to be unique based on coverage"), the variant gains a
   `confidence: decimal` field rather than the DU shape changing.
3. **Truncation as a first-class concern.** The strategy
   distinguishes `VocabularyTruncated` from `EvidenceMissing` —
   truncation is a known unknown (we have evidence but it's a
   prefix); evidence-missing is an unknown unknown (probe didn't
   succeed). This is finer than V1's binary "did the probe
   succeed" framing. Distribution-aware strategies have richer
   evidence; the rationale DU absorbs the richness without needing
   a confidence scalar.

**Verdict on the user's hypothesis:** the rationale DU pattern is
expressive enough for distribution-aware decisions at this
granularity. If a future strategy returns a numeric confidence
score (e.g., a Bayesian prior on "this column is unique"), it
likely lives as a field on the variant rather than as a separate
DU axis. Don't pre-decide; surface when the use case arrives.

**Forward signals for session 12 (Faker direction).**

  - **Two evidence types + four strategies** is the architectural
    state at session 11's close. Per session-9's "session 12+ for
    Faker holds" framing, the synthetic-data emitter is now
    plausible — categorical for low-cardinality, numeric for
    measurements, plus the strategy layer to drive the synthesis
    decisions.
  - **Faker as the third sibling Π that consumes Profile.** The
    Distributions emitter (session 9) consumed Profile for
    diagnostic output; Faker would consume Profile for synthetic
    *data* output. A18 amended (DECISIONS 2026-05-12) holds: Faker
    takes `(Catalog, Profile)`, not Policy. The synthesis
    parameters that *might* feel like policy (e.g., row-count
    target, deterministic seed) are emission configuration, which
    by A18 amended must live in a pass's output that Faker
    consumes — so a `SynthesisPlan` value produced by a future
    pass (or a Plan emitter parameter that doesn't qualify as
    Policy under the amended A18). Defer the architectural
    question; surface when the Faker work begins.
  - **Cardinality strategies likely for session 12 or beyond.**
    The session-10 brief listed cardinality-aware FK as a
    candidate; `CategoricalUniqueness` covered the per-attribute
    cardinality reasoning. Cross-attribute cardinality reasoning
    (the FK case) is a natural follow-up but not pressing.
  - **Joint distributions and temporal density** remain in the
    rich-profiling agenda (ADMIRE.md 2026-05-12). Faker's quality
    benefits from each; neither is required for a first cut.
    Session 12 picks one or proceeds without and accepts the
    limitations.

**Findings beyond the brief:**

  - **The `fanOut` extraction was a clean win.** Four pass drivers
    became thin wrappers; behavior preserved exactly (570
    pre-existing tests still pass after the refactor); the
    canonical iteration logic now lives in one place. The
    two-consumer threshold discipline (DECISIONS 2026-05-13)
    proved itself: extracting at four consumers gave both DRY and
    the empirical evidence that the abstraction was right.
  - **The `StrategyEvaluator` alias is documentary, not
    enforcement.** A type alias in F# doesn't constrain consumers
    that don't ascribe to it. The alias names the canonical shape
    for documentation and discoverability; structural enforcement
    happens at the `FanOutConfig.Evaluate` boundary. This
    distinction matters for future authors — write your evaluate
    against `StrategyEvaluator<...>` to get a compile-time check;
    the alias is opt-in.
  - **The "hybrid mode" admire works.** First admire under the
    three-mode framework (DECISIONS 2026-05-13). The boundary
    between V1-migration share (uniqueness domain inheritance) and
    V2-growth share (per-attribute distribution-driven inference)
    was clear; the admire's structure made the boundary visible;
    the test discipline (V2-only contract tests) followed
    naturally.

**Reasoning / consequences.** Session 11's job was validation
under pressure, and the codification + the rich-profiling vector
both passed. The strategy layer is now more thoroughly named
(`fanOut`, `StrategyEvaluator`) and more thoroughly tested (third
real test of the codification + first distribution-aware
consumer). Session 12 inherits a layered architecture where the
strategy infrastructure is solid; the rich-profiling foundation
has two evidence types operational; and the next big move — Faker
or third evidence type or cross-attribute strategies — has clean
empirical context to choose from. Hold the cadence.

## 2026-05-13 — Strategy-layer codification reaches stability mark

**Status:** decided (operating discipline; trust signal for future authors)
**Context:** Session 8's codification of the strategy layer carried
three refinements during its initial validation
(`RequireQualifiedAccess` on KeepReason DUs; variable-arity context
for evaluate; total-decisions/named-skips). Sessions 9 and 10
extended the rich-profiling vector under the codification; session
11 ran the codification's third real test through a genuinely new
domain (distribution-driven decisions on continuous evidence). No
fourth refinement was required.

**Decision:** **The strategy layer's codification is at its
stability mark.** The four core predictions (pure functions, typed
seam, structured rationale DUs covering the decision space, total
decisions with named skips) plus the recognized conventions
(Strategies/ folder placement, `<Domain>Rules` naming,
`RequireQualifiedAccess` on KeepReason DUs, FanOutConfig delegation)
have absorbed the variation a new domain brings without amendment.

**The empirical test for "stability":**

  1. The codification was named in session 8 (a descriptive pass
     over three existing instances).
  2. It was tested under closely-related variation through session
     10 (per-attribute / per-index / per-reference granularities;
     binary-question evidence).
  3. It was tested under genuinely new pressure in session 11
     (distribution-driven decisions, finer-grained evidence, hybrid
     V1-migration / V2-growth admire mode).
  4. None of those tests forced a fourth refinement. The absence of
     finding is itself the finding.

**What stability means in practice:** future strategy migrations
after this point inherit a codification that has been validated on
its central case (Nullability), on its variation case (UniqueIndex,
ForeignKey), and on its first new-domain case (CategoricalUniqueness)
without amendment. The pattern is now load-bearing in a way it
wasn't at session 10 close. Future authors absorb the conventions
(`AttributeKey`-as-first-field, `<Domain>Rules` suffix,
`Composition.fanOut` delegation, `StrategyEvaluator` alias for
typed seams) and trust they hold.

**What stability does not mean:** it does not mean the codification
is finished. New domains may surface new pressure. The next
amendment, if it comes, will be evidence; the current absence of
amendment is also evidence. Future agents should record either
outcome in this entry's lineage.

**Reasoning / consequences.** Empirical confirmation that an
abstraction holds is stronger than predictive confidence that it
will hold. Session 11's finding earns the codification its place
across the rest of V2 — it is no longer a discipline being tested,
it is a discipline being inherited.

### 2026-05-13 (session 13 amendment) — softening the claim to its evidence

**Status:** appended (forward-pointing refinement; original entry preserved)

The claim above ("the codification's stability mark") is true for what
the codification has been tested on. It is **softer than the entry's
phrasing makes it sound** when read against the shape of the testing.
Honest restatement:

  **The codification has absorbed three real instances within a
  coherent shape — per-record decisions keyed by a single SsKey,
  evaluated synchronously, returning one decision per (record ×
  intervention) pair.** The four core predictions held without forcing
  a fourth refinement *under that shape*. The empirical claim is
  bounded by the shape that was tested.

The three instances tested:

  - **NullabilityRules** — context `Attribute`; decision keyed by
    `Attribute.SsKey`.
  - **UniqueIndexRules / ForeignKeyRules** — context `Kind × Index`
    or `Kind × Reference (× Catalog)`; decision keyed by `Index.SsKey`
    or `Reference.SsKey`.
  - **CategoricalUniquenessRules** — context `Attribute`; decision
    keyed by `Attribute.SsKey`.

All three are per-record (the unit of decision is a single IR record),
single-key (one `SsKey` identifies the decision), synchronous (the
strategy returns immediately; no async / IO / writer-effect wrapping),
and single-decision-per-invocation (`evaluate` returns one
`'decision`, not a list).

**The genuine untested seams.** A heterogeneous fourth strategy that
breaks any of those three shape constraints might surface a fourth
refinement the codification cannot absorb without amendment:

  - **Multi-key strategies.** A `JointDistribution` strategy keyed by
    *two* `SsKey`s (e.g., FK pair statistics) — `'context` is two
    records, the decision is a relation, the lineage event needs to
    carry both keys. The current `Composition.fanOut` wraps a single
    iteration over a single `'context` enumerator; multi-key would
    likely need a different combinator.
  - **Async or effectful strategies.** A strategy whose `evaluate`
    needs to await an external probe, write a Diagnostics event, or
    emit a `Result<'decision>` — the current
    `StrategyEvaluator<'context, 'config, 'decision>` alias forecloses
    on this by typing the return as `'decision` directly. A strategy
    that returns `Async<'decision>` or `Result<'decision>` would force
    either a second alias or a generalization.
  - **Multi-decision-per-invocation strategies.** A strategy that
    decides multiple things from one input (e.g., a strategy producing
    both a Nullability decision *and* a UniqueIndex decision from the
    same evidence) — the current shape returns one decision; producing
    multiple would force the FanOutConfig to grow a `decisionList`
    accumulator or the strategy to be split.

**Disposition.** None of these heterogeneous-shape cases have a
consumer today. Per the two-consumer threshold (`DECISIONS 2026-05-13
— Emergent primitives`), we don't pre-absorb the refinement. The
amendment exists to mark the boundary of the claim:

  - "The codification has absorbed three instances within a coherent
    shape" — confirmed empirically.
  - "The codification will absorb the next instance" — confirmed
    *if and only if* the next instance shares the shape; otherwise the
    next instance is the codification's fourth real test, and the
    test's outcome is empirical, not predicted.

Future agents who read the original entry should also read this
amendment. The original's framing was earned at session 12 against the
evidence available; this amendment names the evidence's shape so the
next instance — when it arrives — is recognized as a fourth test, not
treated as a fourth confirmation by inertia.

## 2026-05-13 — Discrete-rationale DUs absorb continuous evidence by adding variants at meaningful inflection points

**Status:** decided (recognized property of the rationale-DU pattern)
**Context:** Session 11's brief speculated that distribution-aware
decision logic might stress the structured-rationale DU pattern by
forcing a confidence dimension alongside the discrete variants —
"this column's distribution suggests X with confidence Y." The
session-11 reflection found the opposite: confidence didn't surface
as a separate dimension because the keep-reason variants themselves
modeled discrete confidence bands. The pattern absorbed continuous
evidence without parametric confidence values.

**Decision:** **Structured-rationale DUs absorb continuous evidence
by adding variants at meaningful inflection points, not by carrying
parametric confidence values.** This is a recognized property of
the rationale-DU pattern; future strategy authors design new
variants around evidential thresholds rather than reaching for
numeric confidence scores.

**The worked example.** `CategoricalUniquenessKeepReason` distinguishes:

  - `NoCategoricalEvidence` — no observation at all (zero confidence)
  - `EvidenceMissing` — probe attempted, didn't succeed reliably
    (unknown confidence)
  - `VocabularyTruncated` — evidence is a known prefix; full
    vocabulary unknown (bounded-by-truncation confidence)
  - `DistinctCountBelowThreshold` — vocabulary too small to merit
    inference (insufficient confidence)
  - `DuplicatesObserved` — direct contradiction (negative confidence)

Five discrete bands, each named at a meaningful inflection point.
A parametric `confidence: decimal` field on a coarser variant would
have collapsed the structural distinctions into a number that
downstream consumers would need to re-discriminate.

**`VocabularyTruncated` distinct from `EvidenceMissing` is the
sharpest case.** Both fall in the "we don't know enough" region.
Conflating them under a single low-confidence variant would lose a
meaningful distinction: truncated evidence is a *known unknown*
(probe ran, capped the vocabulary at a configured limit; the
unobserved vocabulary may extend the distinct count); missing
evidence is an *unknown unknown* (probe didn't succeed; nothing
observed). Different variants because the consumer's response
differs.

**The principle generalizes:**

  - Continuous evidence (distinct counts, percentiles, sample
    sizes) flows in.
  - The strategy decides which discrete band the evidence falls
    into based on configured thresholds and structural commitments.
  - The variant that fires names the band.
  - Downstream consumers pattern-match on the variant; no parsing
    of confidence numbers required.

**When to deviate.** If a future strategy genuinely returns a
continuous-valued confidence (e.g., a Bayesian prior that callers
need as a numeric input to further computation), the variant gains
a `confidence: decimal` field rather than the DU shape changing.
The principle: continuous values live as fields on variants; the
variant identifies the regime, the field carries the magnitude.

**Reasoning / consequences.** Future strategy authors faced with
continuous evidence have a structural answer to "we need
confidence" — add the right variant, not a confidence number on a
coarser variant. This keeps rationale DUs pattern-match-friendly
and downstream consumers free of confidence-threshold parsing.
Joins the strategy-layer codification (DECISIONS 2026-05-11) and
the structural-commitment-via-construction-validation principle
(AXIOMS.md 2026-05-12) as a recognized operational primitive of
V2's reliability texture.

## 2026-05-13 — Chapter close: audit-by-subagent verification, drift findings, next-chapter priorities

**Status:** decided (chapter closure marker; routes findings to
next chapter)
**Context:** Session 12 ran a five-agent parallel audit (V1 input
contracts, V1 output contracts, V1 test coverage, architectural-doc
drift, build-graph and dependency hygiene) as the first formal
verification pass on the V2 sidecar after eleven build-and-validate
sessions. The synthesis lives in `CHAPTER_1_CLOSE.md` at the
projection root; the handoff letter for the next-chapter agent
lives in `HANDOFF.md`. This DECISIONS entry records the chapter
closure and routes the findings.

**Decision:** **The chapter ends. The next chapter opens with
CHAPTER_1_CLOSE.md and HANDOFF.md as orientation documents.**
Findings documented; resolutions deferred to the next chapter per
the leave-clean-ground discipline (don't fix in the audit session).

**Audit summary** (full detail in CHAPTER_1_CLOSE.md):

| Audit axis | Verdict |
|---|---|
| F#-pure-core / no-I/O-in-Core | Confirmed clean |
| Strategy-layer placement matches codification | Confirmed clean |
| Sibling Π independence (A18 amended) | Confirmed clean |
| Composition-pattern adherence (`fanOut` delegation) | Confirmed clean |
| Project reference graph (inward flow) | Confirmed clean |
| Closed-DU exhaustiveness | Confirmed clean (one acknowledged trade-off in `TighteningPolicy` filter helpers) |
| ADMIRE entry status strings | Drift — five of nine entries stale |
| README.md | Drift — materially behind eleven sessions of work |
| AXIOMS.md opening summary | Drift — still says "thirty-one axioms" |
| Three-mode admire framework adoption | Drift — only one entry strictly follows |
| V1 outputs without V2 equivalents | Documented backlog (Diagnostics writer is the gating dependency) |
| Transform registry deferral | Drift — trigger fired at N=4, codebase at N=10, no cash-out logged |
| Skip-stub asymmetry across V2 test files | Drift — three test files lack stubs the canonical pattern would prescribe |
| V1↔V2 adapter / emitter divergences | ~10 cosmetic-to-medium drifts without DECISIONS audit trail |

**Top 10 next-chapter priorities** (full detail in
CHAPTER_1_CLOSE.md §4):

  1. README.md absorbs the eleven-session state
  2. ADMIRE status sweep (5 entries) + mode annotations
  3. Skip-stub completion across V2 test files
  4. Two missing V2 TopologicalOrderPass tests (manual cycle, junction deferral)
  5. Transform registry cash-out (build or re-defer with rationale)
  6. Diagnostics writer scoping
  7. OSSYS catalog adapter ADMIRE stub
  8. Faker emitter (deferred until third evidence type)
  9. Build-graph and dependency hygiene cleanups
  10. Adapter / emitter divergence DECISIONS batch entry

**Discipline preserved through the chapter:**

  - **Audit before commit** (DECISIONS 2026-05-09 — Audits surface
    things not on the agenda). Session 12 is the first
    chapter-scale audit applying this discipline at the chapter
    boundary, not just the commit boundary.
  - **Leave clean ground, not perfect ground.** The audit
    documents drift; resolution belongs to the next chapter.
    Documenting findings is the audit's product; fixing them is
    different work with different tradeoffs.
  - **Documentation is the bridge.** A fresh agent inherits the
    codebase plus three documents (CHAPTER_1_CLOSE.md, HANDOFF.md,
    and the existing canonical trio AXIOMS / DECISIONS / ADMIRE).
    Honest documentation is the chapter's deliverable to its
    successor.

**Reasoning / consequences.** Chapter closure is a real
architectural event — it marks where the prior chapter's
accumulated judgment becomes documentation a fresh agent can
inherit. Without this marker, the next chapter starts with the
codebase but not the context; with this marker, it starts with
both. The chapter ends here.

## 2026-05-13 — Transform registry cash-out: deferral resolved as overtaken-by-evidence

**Status:** decided (deferred-decisions cash-out, session-13)
**Context:** `DECISIONS 2026-05-06 — Transform registry deferred until N≥4 passes`
committed to revisit the transform registry when N reached 4. The
codebase reached N=4 around session 6 and now stands at N=10
(`Passes/CanonicalizeIdentity`, `NamingMorphism`, `NormalizeStaticPopulations`,
`SymmetricClosure`, `TopologicalOrderPass`, `VisibilityMask`,
`NullabilityPass`, `UniqueIndexPass`, `ForeignKeyPass`,
`CategoricalUniquenessPass`). No subsequent DECISIONS entry either
built the registry or re-deferred with rationale. The chapter-close
audit (`CHAPTER_1_CLOSE.md §2.6`) flagged this as the most consequential
silent-trigger miss: an explicit deferral with a numerical trigger that
fired without a cash-out logged.

**Decision:** **The transform registry is not built. The deferral
resolves as overtaken-by-evidence.**

**Rationale.** The 2026-05-06 deferral was sized against a *single
linear pipeline composed via `>>`* — the masterwork's
`pass1 >> pass2 >> pass3` framing where the registry's value was
explicit ordering constraints, discoverability through reflection, and
startup validation across a unified pipeline. V2 did not evolve into
that shape. What V2 evolved into, instead, is **per-use-case driver
functions** that compose passes ad hoc:

  - The end-to-end milestone (`EndToEndDifferentialTests.fs`)
    composes `CanonicalizeIdentity → NormalizeStaticPopulations →
    SymmetricClosure → NullabilityPass → ...` for the Nullability
    use case.
  - The rich-profiling milestone (`RichProfilingEndToEndTests.fs`)
    composes a different subset for distribution-aware emission.
  - The strategy-driven passes (`NullabilityPass`, `UniqueIndexPass`,
    `ForeignKeyPass`, `CategoricalUniquenessPass`) all delegate to
    `Composition.fanOut` — but the inter-pass composition lives in
    each test's setup or in each emitter's `emitFromInput` helper, not
    in a global registry.
  - There is no single "the V2 pipeline" to register passes against;
    there are use cases (differential parity, rich profiling, future
    Faker, future DacFx milestone) that compose subsets of the
    available passes with different ordering and different evidence
    inputs.

The registry's value-proposition (one place to declare ordering;
reflection-driven discovery; centralized startup validation) was
written for a single-pipeline architecture. V2's per-use-case driver
pattern doesn't have a single pipeline; each use case names its own
composition explicitly, and the explicitness is itself the
documentation. A registry would either become a per-use-case-driver
*alternative* implementation (no value over what exists) or a
*global* layer above the drivers (an abstraction over abstractions
without empirical demand).

**The numeric trigger was right; the framing was overtaken.** N≥4
passes was the threshold predicted in the pipeline-style framing; in
the driver-pattern framing the question becomes "does any driver
benefit from registering passes by name?" and the answer is "no driver
demands it." The numeric trigger fired honestly; the framing it
referred to had been overtaken before the trigger was reached.

**The lesson.** Deferrals with numerical triggers need explicit
re-evaluation when the triggering condition is met, even if the
work has continued in directions that make the deferral feel
irrelevant. The 2026-05-06 deferral fired around session 6 (when the
fourth pass landed); no agent caught it until the chapter-close audit
in session 12. The structural answer is the new
**Active deferrals — index** at the top of this file (introduced in
session 13 alongside this entry). Future agents scan that table when
surveying priorities; if a trigger has fired silently, the cash-out
happens before substantive work continues.

**Reasoning / consequences.** This entry closes the deferral
explicitly. Future agents who read `DECISIONS 2026-05-06 — Transform
registry deferred` should follow the back-reference to this entry and
understand that the registry is not in V2's future under the current
architecture. If a future use case demands a global pass-registration
layer (e.g., the OSSYS catalog adapter exposes a multi-pipeline shape
the driver pattern can't absorb), this cash-out is itself reversible
under "IR grows under evidence" — but the demand has to surface, not
the trigger.

The active-deferrals index codifies the discipline so the same silent
miss does not recur for `RequireQualifiedAccess` retrofit, the
`CycleResolution.ResolutionStep.Reason` migration, the cross-catalog
FK refinement, or any composition primitive whose second consumer
arrives quietly.

## 2026-05-13 — Session 13 closing: doc-hygiene chapter-open landed; one substantive finding deferred

**Status:** decided (session 13 closing marker; routes findings to session 14)
**Context:** Session 12 (chapter close) routed three classes of work
to session 13: documentation hygiene (priorities 1–3); two
deferred-decisions cash-outs (priority 5 transform registry; the
stability-mark claim was self-flagged as possibly too strong); two
missing TopologicalOrderPass tests (priority 4). Session 13 inherited
the doc-hygiene work as the chapter-open opening — the prior agent's
explicit recommendation.

**What landed in session 13** (eight commits):

| # | Scope | Verdict |
|---|---|---|
| 1 | README.md absorbs eleven sessions of state | landed; F# adapters, four-axis Policy, actual project layout, strategy layer, rich profiling, composition primitives, A18 amended, Diagnostics writer flagged, OSSYS catalog adapter named |
| 2 | AXIOMS.md opening + A18 forwarding pointer | landed; opening now acknowledges A1–A34 / T1–T11 with five amended originals; A18 original points forward to its load-bearing amendment |
| 3 | ADMIRE status sweep | landed; five entries updated to `extracted (...)` with mode annotations under three-mode framework; EntitySeedDeterminizer entry now acknowledges `StaticAdapterDifferentialTests.fs` as the differential landing it promised |
| 4 | Skip-stub completion (V1NullabilityParity pattern) | landed in three test files; four new Skip stubs (UniqueIndex Aggressive mode, UniqueIndex included-columns boundary, ForeignKey DeleteRuleIgnore rationale, TopologicalOrder sanitized-effective-names) |
| 5 | Transform registry cash-out + Active deferrals index | landed; the deferral resolves as overtaken-by-evidence (V2 evolved into per-use-case driver functions, not a single linear pipeline); new index at top of DECISIONS lists ten active deferrals with trigger conditions so silent triggers get caught structurally |
| 6 | Stability-mark amendment | landed; appended forward-pointing refinement softening the claim to its evidence (three instances within a coherent shape — per-record decisions keyed by a single SsKey); names three genuine untested seams (multi-key, async, multi-decision) |
| 7 | TopologicalOrderPass V2 contracts reserved | landed as Skip stubs (not as full Behavioral tests); see substantive finding below |
| 8 | This entry — session 13 closing summary | landed |

**Test baseline:** 585 passed, 9 skipped, 594 total (was 585/3/588 at
chapter close). The 6 new skips (4 V1-divergence + 2 reserved) widen
test discovery's surface for V2's named divergences without changing
the passing-test count. Build green; no warnings beyond a pre-existing
nullness warning in `DistributionsEmitterTests.fs:126` that predates
session 13.

**Substantive finding from priority 4 (audit-during-validation).**
`CHAPTER_1_CLOSE.md §4 priority 4` listed "two missing
TopologicalOrderPass tests" with cost "moderate — depends on whether
OrderingPolicy already supports these knobs in V2 IR." It does not.
V2's Policy has Selection / Emission / Insertion / Tightening axes;
no Ordering axis. `TopologicalOrderPass.run` takes no Policy
parameter (it is `Catalog -> Lineage<TopologicalOrder>`). Manual-cycle
override has no representation. The junction-table heuristic is not
implemented; `OrderingMode.JunctionDeferred` is declared in the DU
but the pass never produces it. The two contracts ADMIRE flags as
Behavioral V2 translations are **features-not-yet-built**, not
just-missing-tests.

Disposition for session 13: reserve the contract names via Skip
stubs (commit 7). Implementation belongs to a substantive next-chapter
move under the audit-during-validation discipline — surface the
finding, name the disposition, defer the build to a session that
takes it as scope. The audit-induced expansion of priority 4 is
itself the discipline working.

**Recommended priorities for session 14, with rationale.**

  1. **Diagnostics writer.** Twelve sessions of consistent demand;
     gating dependency for at least seven concrete artifacts and
     pipelines (`decision-log.json`, `opportunities.json`,
     `validations.json`, `dmm-diff.json`, opportunity-stream half of
     UniqueIndex, operator-approval handoff for FK and Nullability,
     V1 nullability `Analyze()` pipeline). The deferral has been
     intellectually honest at every prior session ("don't build
     speculatively"), but the demand is consistent enough that
     building is now plausibly cheaper than continuing to defer. My
     read aligns with `CHAPTER_1_CLOSE.md §4 priority 6` and the prior
     agent's "I'd revisit if I had more time" item: the writer's
     value-prop is no longer speculative. Recommend session 14 scopes
     the writer (single-channel for now per the constitution; three
     channels later) and lands at least one downstream artifact
     consuming it (probably opportunity-stream because it's the
     cheapest first consumer).
  2. **OSSYS catalog adapter.** The undocumented production boundary
     (`CHAPTER_1_CLOSE.md §2.10` and `§4 priority 7`). V2 catalogs are
     today built by F# fixtures; production V2 needs to consume real
     OutSystems metadata via the V1 `outsystems_metadata_rowsets.sql`
     → `MetadataSnapshotRunner` → `SnapshotJsonBuilder` → V2 path. No
     ADMIRE entry covers this. Probably a session-14 ADMIRE stub; the
     implementation itself is a separate larger chapter.
  3. **OrderingPolicy axis + junction heuristic** (deferred priority
     from session 13). Cashes out the priority-4 work the audit found
     was bigger than ranked. The TopologicalOrderPass tests reserved
     in commit 7 then promote from Skip to `[<Fact>]`. Lower
     immediate-value than (1)/(2); could reasonably wait for session
     15 unless a real cycle-resolution use case forces it earlier.
  4. **Faker emitter.** Per `CHAPTER_1_CLOSE.md §4 priority 8` and the
     two-evidence-types-only constraint, defer until either a third
     evidence type lands or Faker proceeds with two and accepts the
     limitations. Not session 14 unless one of (1)/(2)/(3) opens it
     up.

The ranking (1) → (2) is gated on which produces more downstream
demand quickly; the prior agent's "Diagnostics writer first, OSSYS
catalog adapter second" framing matches my read after orientation.
Faker remains genuinely deferred.

**Disposition I inherited and am passing forward.**

  - **Audit during validation.** Discovered priority-4 was feature
    work not test work; logged the finding before pretending the
    full priority was met. Same discipline that produced five
    paydowns across sessions 4, 5, 7, 8, 11.
  - **Total decisions, named skips.** Six new Skip stubs across
    three test files now name V2 divergences and un-built features
    that previously lived only in ADMIRE prose.
  - **Documentation is the bridge.** Every commit in session 13
    leaves the docs honest enough that the next agent reading only
    the docs would understand what changed and why.
  - **Defer with structure, not just intention.** The
    "Active deferrals" index codifies the discipline that the
    transform-registry miss surfaced. Future trigger-fires get
    caught by table-scan, not by chronological re-read.

**Closing.** Session 13 was doc-hygiene plus two deferred-decision
cash-outs plus one audit-induced finding. The codebase is unchanged
(no code touched outside test-file Skip stubs). The documentation is
honest in places it was stale before. The deferred decisions index
exists so the silent-trigger failure mode does not recur. Session 14
inherits a chapter whose first substantive decision is whether to
build the Diagnostics writer, ADMIRE the OSSYS catalog producer, or
take a different opening based on demand the next agent reads from
the codebase. The doc-hygiene chapter-open is what it claimed to be:
not new architecture, just clean ground for the chapter ahead to
support more weight than the one behind.

Hold the spine.

— Session 13 (the doc-hygiene chapter-open)

## 2026-05-13 — Audit discipline refinement: contract-vs-implementation cross-reference

**Status:** decided (audit-discipline operating principle; refinement of `DECISIONS 2026-05-09 — Audits surface things not on the agenda` and the `CHAPTER_1_CLOSE.md` audit-by-subagent verification approach)
**Context:** Session 13's audit-during-validation produced a finding
the chapter-close audit (session 12) had missed: priority-4 work
("two missing TopologicalOrderPass tests") was actually feature-work
("two un-built V2 contracts — no `OrderingPolicy` axis, no
junction-table heuristic, `OrderingMode.JunctionDeferred` declared
but never produced"). The miss was not random. The session-12 audit
dispatched five parallel subagents against ADMIRE entries, V1 test
coverage, V1 input/output contracts, doc drift, and build-graph
hygiene — none of which cross-referenced ADMIRE-promised V2 contracts
against the implementation modules to verify feature-completeness.
The audit walked **contract → test** ("does the test exist?") but
did not walk **contract → implementation** ("does the feature the
test would assert exist?"). Both walks are needed; only one was done.

**Decision:** **Any audit that walks a contract-vs-test
cross-reference must also walk a contract-vs-implementation
cross-reference.** Without it, audits systematically undercount the
substantive backlog and present feature-work as test-work — exactly
what session 12's priority-4 entry did.

**The structural lesson generalizes.** ADMIRE entries promise V2
contracts in three modes (V1-migration / V2-growth / hybrid;
`DECISIONS 2026-05-13 — admire spectrum`). Each promised contract
has three states the audit must distinguish:

  | Contract → test? | Contract → implementation? | Diagnosis |
  |---|---|---|
  | Test exists | Implementation exists | Migrated; ADMIRE entry should be `extracted (differential confirmed)` |
  | Test exists | No implementation | Test will fail; the implementation is the gap |
  | No test | Implementation exists | Test gap; the audit's contract-vs-test walk catches this |
  | No test | No implementation | **Feature gap** — the audit's contract-vs-test walk **misclassifies this as a test gap** unless implementation is also walked |

The fourth row is the failure mode. Session 12 found the third row
on UniqueIndex / ForeignKey / Topological skip-stub asymmetry
(`CHAPTER_1_CLOSE.md §2.7`); session 13's skip-stub completion (commit
4) addressed the test gaps. Session 13's TopologicalOrderPass
finding was the fourth row — the implementation didn't exist; the
contract-vs-test walk reported "missing test" because there was
nothing to compare against.

**Discipline going forward:**

  1. **Chapter-close audits run two cross-references in parallel.**
     One subagent walks ADMIRE-contracts × V2-tests; another walks
     ADMIRE-contracts × V2-implementation. Findings are reported
     against both axes; the tabular form above lets readers see
     which row the finding falls into.
  2. **Contract-vs-implementation walks check three things:**
     module presence (does the named V2 module exist?), feature
     presence (do the IR types and policy fields the contract
     names exist?), and behavior presence (does the implementation
     produce the named outcomes? — `OrderingMode.JunctionDeferred`
     declared but never produced is the canonical anti-pattern).
  3. **The result of the two walks combines into a single
     priority-ranked findings list.** "Missing test, implementation
     exists" is mechanical to fix (add the test; lock the
     contract). "Missing implementation" is a substantive deferred
     decision that needs DECISIONS-entry routing, not test-priority
     ranking.

**The fresh-agent observation.** This miss came from someone who
had never read the code before. The session-12 chapter-close audit
was conducted by an agent with eleven sessions of accumulated
familiarity; the familiarity made the un-built `OrderingPolicy`
axis invisible-because-known. Session 13's fresh agent (no prior
context) cross-referenced ADMIRE → implementation as a normal part
of orientation — there was no familiarity to elide. The general
form: **fresh agents at chapter boundaries find things that the
prior chapter's accumulated familiarity hid.**

This argues for a structural disposition: chapter-close audits
should explicitly include a "fresh-eye walk" — either by a subagent
configured to ignore accumulated context, or by a fresh-agent
review at the chapter boundary itself. Session 13's read-in served
as a de facto fresh-eye walk; future chapter closes should make it
deliberate.

**Reasoning / consequences.** The audit-during-validation
discipline (`DECISIONS 2026-05-09`) caught the priority-4 miss
during session 13's work — exactly the failure mode it exists to
catch. This entry refines the *chapter-close audit* protocol so the
miss doesn't recur. The next chapter close (whenever it lands) will
benefit: contract-vs-implementation walk runs alongside
contract-vs-test walk; findings classify against the four-row
table; fresh-eye review is structural rather than incidental.

The Active deferrals index (`session 13 commit 5`) and this
audit-discipline refinement are paired: the index makes deferred
decisions visible across chapters; this refinement makes the
distinction between deferred-test-work and deferred-feature-work
visible within an audit. Both compound: the index catches silent
trigger-fires; this discipline catches misclassified findings.

## 2026-05-13 — Pass return-type codification: `Lineage<Diagnostics<'a>>` when the pass produces both

**Status:** decided (operating discipline; pass-codification refinement; preserves the false start so future agents recognize the temptation)
**Context:** Session 14 commit 4 needed to wire `UniqueIndexPass`
to the new Diagnostics writer (commit 3) so the V1 OpportunityBuilder
contract (V2 Skip stub reserved in commit 2) could activate. The
return-type question surfaced: should `UniqueIndexPass.run` keep its
existing `Catalog -> Policy -> Profile -> Lineage<UniqueIndexDecisionSet>`
shape and gain a sibling
`runWithDiagnostics : ... -> Lineage<Diagnostics<UniqueIndexDecisionSet>>`,
or should `run` itself migrate to the dual-writer shape?

**The false start I want recorded.** Initial choice was the sibling
function. The justification cited at the time: the closed-DU
expansion empirical-test discipline (`DECISIONS 2026-05-13 — Closed-DU
expansion: empirical confirmation`) — "the seam is positioned
correctly if F# exhaustiveness errors light up only at match sites
and no callers outside the variant's module need reshaping. If they
do, the seam is wrong and you're being told that." Twenty existing
`UniqueIndexPass.run` call sites in tests would have updated under
the return-type change, which felt like a violation of that rule. So
I picked the sibling — `run` unchanged, `runWithDiagnostics` as a
new entry point that internally wraps `run`'s output with
post-hoc-constructed diagnostic entries.

**Why the citation was wrong.** The closed-DU expansion discipline
is about *DU variant additions* — does the seam absorb a new variant
without forcing reshapes at consumer sites? That's a property of
variant-level changes against pattern-match sites. **Return-type
generalization is a different category.** The empirical test for
return-type changes is not "do callers reshape?" — it is "does the
type signature accurately name what the function produces?"

The two disciplines look superficially similar (both involve "do
callers change?") and the closed-DU one is load-bearing in V2's
recent codification, which made it the available rule when the
question came up. But the rules apply to different change shapes;
reaching for the closed-DU discipline on a return-type question is
a category error. Future agents who notice test ripple from a
return-type change and feel the pull toward the closed-DU rule
should pause and ask: **am I adding a DU variant, or am I changing
what the function produces?** The disciplines diverge there.

**Why the sibling shape is wrong long-run.** A sibling
`runWithDiagnostics` synthesizes diagnostic entries post-hoc from
the decision set — the diagnostics aren't truly "what the pass
produces," they're "what a wrapper produces from the pass's
output." That's a tell: the canonical entry point should return what
the pass actually does. More structurally, every pass that grows
diagnostic emission later (`NullabilityPass` activates V1 #6/#7;
`ForeignKeyPass` activates the DeleteRuleIgnore stub from session
13; `CategoricalUniquenessPass` whenever it surfaces an audit-trail
need) faces the same fork. The codebase ends up with
`Pass.run` (vestigial-by-construction; the historical lineage-only
shape) and `Pass.runWithDiagnostics` (the actual canonical entry
point) duplicated across four passes. The vestigial half stays in
test code forever because removing it would break callers — exactly
the test-stability bias that made the sibling tempting in the first
place, perpetuated.

**The right framing — the shape that names the production.** A
pass's return type should capture what the pass produces. The same
discipline names `A18 amended` (Π consumes whichever subset of
`Catalog × Profile` it needs — type signature names the inputs) and
`A32` (passes may produce values consumed by emitters — the
EnrichedCatalog or sibling value names the production). Type
signatures are honest: they name what flows in and out. Passes that
produce only decisions return `Lineage<'output>`. Passes that
produce decisions plus observer-relevant findings return
`Lineage<Diagnostics<'output>>`. The shape declares the production;
callers update mechanically when production changes.

**Decision:** **Passes return `Lineage<'output>` when they produce
only decisions, and `Lineage<Diagnostics<'output>>` when they
produce decisions plus observer-relevant diagnostics.** The variant
arrives at meaningful inflection points — mirrors `DECISIONS
2026-05-13` on rationale DUs absorbing continuous evidence (variants
at meaningful inflection points beats parametric values on coarser
variants); the same principle applied to function shapes. No
sibling-function half-measure.

**Worked example (commit 5).** `UniqueIndexPass.run` migrates from
`Catalog -> Policy -> Profile -> Lineage<UniqueIndexDecisionSet>` to
`Catalog -> Policy -> Profile -> Lineage<Diagnostics<UniqueIndexDecisionSet>>`.
The pass body now emits a `DiagnosticEntry` for every decision that
does not enforce uniqueness or that requires remediation (mirroring
V1 `OpportunityBuilder.TryCreate`). Test sites (~20 in test files)
update mechanically: `lineage.Value` becomes `dual.Value.Value`. A
small helper `UniqueIndexPass.decisionsOf` extracts the
`UniqueIndexDecisionSet` from the dual writer for tests that only
care about decisions; tests that care about diagnostics access
`dual.Value.Entries` directly.

**Forward signal.** When `NullabilityPass`, `ForeignKeyPass`, or
`CategoricalUniquenessPass` next grow diagnostic emission, they
follow the same migration. Don't add a sibling function. Change the
return type. Pay the test ripple. The cost is one-time; the
discipline is permanent. Each migration is independent — passes that
don't yet emit diagnostics keep their `Lineage<'output>` shape.

**The general rule, named.** When a function's category of output
grows (decisions → decisions + diagnostics; pure → effectful;
single-value → multi-value), change the signature to name the new
production. Test ripple is information about *where the function is
called from*; it is not evidence the seam is wrong. The closed-DU
discipline applies to DU variant additions; return-type
generalizations have their own discipline, and that discipline is
"name the production."

**Reasoning / consequences.** Recording the false start is itself
the discipline's value-add to future agents. The closed-DU rule will
be tempting again — it's load-bearing, it's recent, it's available.
The right reflex when a return-type change forces test ripple is
*not* to reach for closed-DU; it is to ask whether the new return
type names the production accurately. If yes, the ripple is the
cost of honesty in the type system. If no, the change is wrong and
the question is what the right shape is.

This entry pairs with the audit-discipline refinement (session 14
commit 1 — contract-vs-implementation cross-reference) as a session
that produced two operating-discipline entries before producing
substantive infrastructure. Both were named because both could
recur. Future agents inherit the disciplines and the false starts
together.

## 2026-05-13 — Named accessors for stacked types whose nested access loses self-description

**Status:** decided (operating discipline; smell-fix codification; preserves the false start)
**Context:** Session 14 commit 5 migrated `UniqueIndexPass.run` from
`Lineage<UniqueIndexDecisionSet>` to
`Lineage<Diagnostics<UniqueIndexDecisionSet>>` per the pass
return-type codification (`DECISIONS 2026-05-13` — pass return-type
codification). The migration's first cut updated test sites with
the literal access pattern `lineage.Value.Value.Decisions`. During
the work, the user surfaced the question: "is `lineage.Value.Value`
a code smell in that it's not self-descriptive?" The answer is yes,
and the disposition generalizes.

**The false start preserved.** The mechanical migration produced
~14 call sites of the form `lineage.Value.Value.Decisions`. Each
read forces the reader to count `.Value` projections to know which
writer they land in. The first `.Value` strips the outer `Lineage`
wrapper; the second strips the inner `Diagnostics` wrapper; the
third reaches `.Decisions` on the underlying `UniqueIndexDecisionSet`.
F# infers the types correctly, but the consumer expression encodes
no semantic intent — `Value` of a `Lineage<...>` and `Value` of a
`Diagnostics<...>` share a name, and the reader has to know the
field-naming convention to disambiguate.

The smell is real. The smell test that names it:

  **Would a reader of this expression need the type definition open
  in another window to know which level they're on?**

If yes, the access pattern is not self-descriptive and the
discipline is to provide named accessors that name the intent at
each level.

**Decision:** **Stacked types deserve named accessors at call sites
where nested access loses self-description.** Whenever a stacked
writer (or any nested type) creates a `.Field.Field` access pattern
at multiple consumer sites, and the structural shape requires the
reader to count nesting levels to know which level they're on, the
discipline is to provide module-level accessors that name what's
being reached for.

**The pattern shape:**

```
module <DualOrStackedType> =
    let <intentName1>  : <Stacked<'a>> -> <X>  = ...
    let <intentName2>  : <Stacked<'a>> -> <Y>  = ...
    let payload        : <Stacked<'a>> -> 'a   = ...   (the deep value, named)
```

For the dual writer `Lineage<Diagnostics<'a>>` (this commit's
example), the helpers are:

  - `LineageDiagnostics.payload      : Lineage<Diagnostics<'a>> -> 'a`
  - `LineageDiagnostics.entries      : Lineage<Diagnostics<'a>> -> DiagnosticEntry list`
  - `LineageDiagnostics.diagnostics  : Lineage<Diagnostics<'a>> -> Diagnostics<'a>`
  - `m.Trail` stays as-is (single Field at the outer level; already
    self-descriptive — the smell is specifically about nested
    repetition, not single access)

Domain-named shortcuts compose cleanly with the generic helpers.
`UniqueIndexPass.decisionsOf` delegates to
`LineageDiagnostics.payload`; the domain shortcut reads more
clearly than the generic accessor at consumer sites
(`UniqueIndexPass.decisionsOf lineage` over
`LineageDiagnostics.payload lineage`), but both are self-descriptive
and the underlying structure is asserted in one place.

**Where the discipline applies (and where it does not):**

  - **Applies:** consumer sites that read through a stacked type to
    a deep value. The named accessor declares intent.
  - **Does not apply:** structural assertion tests for the writer
    itself. `LineageDiagnostics.payload` is *defined* as
    `m.Value.Value`; the test that asserts the helper does what it
    claims must read `m.Value.Value` directly to verify the helper.
    Reaching past the helper at a structural-test site is the test's
    purpose.
  - **Does not apply:** single-level access where the field name is
    unambiguous in context (`m.Trail` for `Lineage<...>`,
    `m.Entries` for `Diagnostics<...>` — single Field access at the
    outer layer is self-descriptive).

**The general smell test, restated:**

  - One `.Field` access: usually self-descriptive; the field name
    carries the intent.
  - Two `.Field.Field` of the same name: smell; the reader counts
    levels to know which writer they're on.
  - Two `.Field.OtherField` of different names: usually fine; the
    second name disambiguates.
  - Three or more `.Field`: smell regardless of name uniqueness;
    nested access at depth loses structural intent even when each
    name is distinct.

The boundary is not an exact line; the test is whether a reader can
tell, from the expression alone, what's being reached for.

**Pairs with the pass return-type codification.** Honest signatures
+ readable consumers is the joint commitment. The pass return-type
codification (`DECISIONS 2026-05-13` — pass return-type) says:
*change the type signature when the production grows.* This entry
says: *provide named accessors when the new type's consumer pattern
loses self-description.* Together, the two disciplines keep both
the type system and the call sites honest.

**Why the false start is preserved.** The same temptation will
recur: future agents migrating to a stacked type will produce
`.Value.Value` access patterns by default, and the smell will read
as "F# being F#" rather than as a discipline gap. This entry exists
so the next agent recognizes the smell as soluble and not as a
language artifact. The named-accessor discipline applies; the smell
test is the trigger.

**A meta-pattern across session 14 entries.** This is the third
operating discipline session 14 has produced — alongside the
audit-discipline refinement (`DECISIONS 2026-05-13` —
contract-vs-implementation cross-reference) and the pass
return-type codification. All three followed the same pattern:

  1. The discipline surfaced during substantive work, not as a
     planned discipline-codification effort.
  2. The discipline was named and recorded *with the false start
     preserved*, so future agents recognize the temptation when it
     recurs.
  3. The substantive work continued under the new discipline before
     the commit shipped.

This meta-pattern itself is worth naming: **disciplines emerge from
the work, not from speculation about the work.** Audit-during-
validation (`DECISIONS 2026-05-09`) is the upstream discipline; the
three session-14 entries are downstream consequences of operating
that discipline at a chapter-open. Future chapters that operate
audit-during-validation should expect to produce disciplines of
this shape; recording them with their false starts is the
convention this session establishes.

**Reasoning / consequences.** The named-accessor discipline is now
named, codified, and discoverable from any call site that imports
`Projection.Core`. Future stacked-type designs (a third-channel
Diagnostics split when it lands; future writer compositions; deeply
nested IR records) inherit the convention: provide named accessors
at the consumer surface whenever the structural shape requires
counted projections at call sites.

## 2026-05-13 — Anticipation vs. speculation in abstraction extraction (refinement of the two-consumer threshold)

**Status:** decided (operating discipline; refinement of `DECISIONS 2026-05-13 — Emergent primitives earn their place through multi-consumer demand`)
**Context:** Session 14's discussion of object expressions for
hypothetical `ICatalogReader` and `IDiagnosticSink` interfaces
surfaced a question the two-consumer threshold doesn't directly
answer. The `ICatalogReader` case looks plausibly worth amortizing
up front (DACPAC support is named in V2's vocabulary docs;
`README.md` calls out "DACPAC, OData, or other sources later" as
the algebra's whole reason for using generic algebraic names; the
OSSYS adapter implementation chapter is the natural moment to make
the interface decision once before the function shape is calcified
by callers). The `IDiagnosticSink` case looks distinctly *not*
worth amortizing (writer-vs-sink semantics are genuinely
uncertain; the first real downstream consumer's shape will
constrain the design in a way speculation cannot). The two-consumer
threshold treated symmetrically would defer both; the cases are
not symmetric.

**The reframing:** **The two-consumer threshold is not "wait for
the second consumer to literally exist" — it is "wait for the
second consumer's *shape* to be visible enough to validate the
abstraction."** When the shape is visible, the threshold is met by
anticipation; when the shape isn't, speculation about the
abstraction is what the threshold guards against. The discipline
is against speculative abstraction, not against thoughtful
anticipation.

**Three positions for any abstraction-extraction question:**

| Position | What it means | When it applies |
|---|---|---|
| **A — Amortize fully now** | Define the abstraction (interface, helper, primitive, etc.) today; route all consumers through it. Pay full cost up front. | When the second consumer's shape *and* arrival are both highly probable within the next few sessions. Rare; usually a sign we're past the threshold and just hadn't noticed. |
| **B — Amortize structurally only** | Don't define the abstraction today, but design the function signatures / module shapes / value types so they map cleanly to the eventual abstraction. When the second consumer arrives, the abstraction lands as a one-line wrapper; no retrofit. Pay structural cost up front, defer concrete cost. | When the second consumer's *shape* is visible and validatable (we know what the abstraction would look like) but its *arrival* is not yet concrete. The discipline preserved: no speculative abstraction; the discipline relaxed: design with anticipated shape in mind. |
| **C — Defer fully** | Build whatever's natural for the first consumer; let the second consumer force the abstraction. Retrofit cost is real but small in F# (object expressions, type aliases, monad bindings all keep the cost low). | When the second consumer's shape is genuinely uncertain. The risk of premature naming is higher than the cost of retrofit. |

**Worked examples:**

| Abstraction | Position | Rationale |
|---|---|---|
| `ICatalogReader` (multiple catalog sources: OSSYS, DACPAC, OData, in-memory fixtures) | **B** | DACPAC's shape is concrete enough to design for. The OSSYS adapter's primary entry point should be `parse : string -> Task<Result<Catalog>>` — exactly the shape the future interface would have. Interface itself defers until a second source materializes; structural alignment lands in the OSSYS implementation chapter. |
| `IDiagnosticSink` (streaming consumers of Diagnostics entries) | **C** | Writer-vs-sink semantics are the deeper question; V2 chose writer (entries accumulate in a value; consumer reads). The first real downstream consumer (JSON manifest emitter, operator dashboard, telemetry consumer) will constrain whether sink semantics are needed. Three plausible futures, three different right answers. Wait for the first consumer to surface the question. |
| Composition primitives (`fallback`, `accumulate`, `wrap`, `lift`) | **C** | Sketched at session 8; deferred at session 11 commit cash-out (`DECISIONS 2026-05-13 — Composition vocabulary cash-out`). The first consumer's shape isn't visible — we don't know what the second pass that needs `accumulate` would look like, what the second strategy that needs `wrap` would instrument, etc. The shape isn't validatable; speculation would name the wrong abstraction. |
| `StrategyEvaluator` alias (now codified) | **B → A retroactively** | Sketched at session 8 (Position B; the shape was visible across three strategies); cashed out at session 11 commit 5 when the fourth strategy made the shape empirically real (`DECISIONS 2026-05-13 — Generic StrategyEvaluator alias cash-out`). The retrospective Position A landing was actually a Position B that ripened into A through real consumer demand. |

**The empirical test for "shape visible enough":**

  1. **Can you write the abstraction's signature without making
     contested choices?** If yes, the shape is visible. If you find
     yourself pausing on "should this be `Async` or sync?" or
     "should it return `Result` or throw?" — those are the contested
     choices that mean the shape isn't visible enough yet.
  2. **Can you predict the second consumer's call site without
     consulting an external source?** If yes, anticipation is
     grounded. If you're reaching for "well, it depends on what the
     downstream design is" — the second consumer's shape is
     speculative, not visible.
  3. **Would naming the abstraction now constrain the second
     consumer's design in ways you'd be confident about?** If yes,
     the abstraction earns its place by anticipation. If naming it
     now would force the second consumer into a shape that might be
     wrong — you're speculating, not anticipating.

**The discipline restated:**

  - Position B is acceptable when all three empirical tests pass.
    The structural cost (designing the function with the
    anticipated abstraction in mind) is small; the future cost
    saved (no retrofit) is real.
  - Position A requires both shape visibility AND a concrete second
    consumer. Without the concrete consumer, A is just speculation
    in disguise.
  - Position C is the default. When in doubt, defer; F# makes
    retrofit cheap.

**Why this refinement matters.** The two-consumer threshold has
served the codebase well — `fallback` / `accumulate` / `wrap` /
`lift` deferred at session 11 are still deferred at session 14
because no consumer has surfaced their shape; the discipline
caught what would have been speculative abstraction. But applied
*as a literal rule* it would also defer `ICatalogReader` even when
DACPAC is named in V2's vocabulary docs as a planned source. That's
treating anticipation as speculation, which loses information.

The refinement preserves the discipline's value (no abstractions
named on hope alone) while permitting Position B (structural
alignment when the shape is concrete enough). Future agents
applying the discipline have the three positions plus the empirical
test to choose among them; the choice is now nuanced, not
mechanical.

**Pairs with three other entries in this session:**

  - `DECISIONS 2026-05-13 — Emergent primitives earn their place
    through multi-consumer demand` — the original threshold this
    refinement extends.
  - `DECISIONS 2026-05-13 — Pass return-type codification` (session
    14) — also in the abstraction-design family; the discipline
    there is "the type signature names the production." This entry
    says the timing of when to introduce that signature follows
    the visibility-of-shape rule.
  - `DECISIONS 2026-05-13 — Named accessors for stacked types`
    (session 14) — the smell-fix discipline for nested access; the
    timing of when to extract a named accessor follows the same
    rule (when call sites recur enough that the accessor's shape
    is visible).

**Reasoning / consequences.** Future abstraction-extraction
decisions explicitly choose among A, B, or C and apply the empirical
test. The decision is captured in the relevant DECISIONS entry or
the commit message that introduces the abstraction. Documentation
of the position taken — and the test result — pays compound
interest when a future agent revisits the choice.

The general lesson: **disciplines refine through use, not through
restatement.** The two-consumer threshold was the right rule when
named; the refinement is the right rule now that one of its edge
cases (anticipation grounded in concrete planning) has surfaced.
The next agent who finds another edge case extends this entry or
writes a successor; the discipline is alive, not frozen.

## 2026-05-14 — Chapter-close ritual: the things to check at every chapter boundary

**Status:** decided (operating discipline; codifies the chapter-close ritual the prior chapter operated informally and the next chapter should operate explicitly)
**Context:** Session 14 (chapter-close audit, conducted in session 12)
caught the transform-registry deferral that had fired silently. The
session-13 audit-during-validation produced the
contract-vs-implementation refinement
(`DECISIONS 2026-05-13 — Audit discipline refinement`). Session 14's
operator-led reflection raised two more concerns:

  - The F#-feature-surface section in CLAUDE.md has re-open
    triggers; if those are not cross-referenced from the Active
    deferrals index, silent-trigger fires can recur in a different
    surface. (Now fixed; commit 1 of session 15 added the
    consciously-deferred features to the index.)
  - CLAUDE.md will drift the same way README.md did — a fresh agent
    rewrote the README at session 13 because eleven sessions of
    accumulated change had made it stale. CLAUDE.md is at higher
    risk because it indexes other docs that themselves change.

The fix-each-thing-once approach addressed both. But the underlying
problem is structural: chapter-close audits have run as ad-hoc
investigations, not as a codified ritual. The next chapter close
will benefit from a named, repeatable list of things to check.

**Decision:** **The chapter-close ritual is codified. Every chapter
close must execute the items below before declaring the chapter
done.** Items marked "load-bearing" must produce a written
finding (either "clean" or a remediation entry); items marked
"informal" are encouraged but not required.

### Load-bearing items

  1. **Active deferrals index scan.** Walk every entry in the
     Active deferrals index at the top of `DECISIONS.md`. For each:
     verify the trigger condition still describes the right
     condition; verify the current state still describes reality;
     if the trigger has fired since the last scan, log a cash-out
     entry. The transform-registry miss is the worked example of
     what happens without this scan.
  2. **Contract-vs-implementation cross-reference walk.** Per
     `DECISIONS 2026-05-13 — Audit discipline refinement`, every
     ADMIRE entry's promised V2 contracts must be checked against
     both the test surface AND the implementation surface. The
     four-row classification table (test×impl) routes findings:
     "no test, no implementation" is feature-gap; "no test,
     implementation exists" is test-gap; etc.
  3. **CLAUDE.md staleness check.** Walk every section of
     CLAUDE.md against the current state of the canonical surfaces
     it indexes. Reading order pointer still resolves to the right
     documents; operating-disciplines table still points at
     current DECISIONS entries; F#-feature-surface section still
     reflects what the codebase uses; programming-style center
     target still describes patterns visible in the code. If
     anything has drifted, fix it during the close — don't leave
     it for the next chapter.
  4. **README.md staleness check.** Same shape as CLAUDE.md but
     for the README — surface-level orientation. Session 13
     rewrote it after eleven sessions of drift; the discipline
     prevents that recurring.
  5. **HANDOFF.md / CHAPTER_1_CLOSE.md scope.** Each chapter
     produces its own HANDOFF letter and CHAPTER_CLOSE audit
     synthesis at the close. Chapter 1's CHAPTER_1_CLOSE.md (sessions
     1–12) lives at the projection root; chapter 2's belongs
     adjacent or under a chapter-numbered subfolder. The next
     chapter's handoff should not overwrite chapter 1's; the
     append-only documentation discipline is structural.
  6. **Fresh-eye walk.** Per `DECISIONS 2026-05-13 — Audit
     discipline refinement`, chapter-close audits explicitly
     include a fresh-eye walk — either by a subagent configured to
     ignore accumulated context, or by a fresh-agent review at
     the chapter boundary itself. Familiarity hides what fresh
     eyes find.
  7. **Operating disciplines table currency.** CLAUDE.md's
     operating-disciplines table must point at current DECISIONS
     entries by date. New disciplines added during the chapter
     are reflected; deprecated disciplines are removed or marked
     superseded.
  8. **V1-input-envelope walk** (added at session 25 chapter-2
     close per the audit's recommendation; applies to V1↔V2
     translation chapters and chapters where a structured input
     envelope has comprehensive content). Walk the input envelope
     field-by-field at chapter close to identify silent drops not
     yet on the won't-carry-forward list. The trace-before-fixture
     pattern catches per-slice questions; the envelope walk
     catches chapter-level coverage gaps. For the OSSYS chapter,
     the walk is `SnapshotJsonBuilder.cs` field-by-field against
     the won't-carry-forward list in ADMIRE plus the running
     translation-rules amendments. For future V1↔V2 chapters
     (DACPAC, OData, etc.), the walk is the analogous V1-side
     envelope-projection code. Findings categorize into the
     three-class typology (lossiness / boundary-discipline /
     alternative-IR-surface; see `DECISIONS 2026-05-21 — Chapter
     2 close: alternative-IR-surface class`). See the session-25
     amendment below for the discipline's origin and rationale.

### Informal items

  - **Test baseline diff.** Test count delta from chapter open to
    close, broken down by added vs migrated vs deleted. Useful for
    the chapter's quantitative narrative; not load-bearing.
  - **Forward-signal triage.** Each chapter close names forward
    signals for the next chapter; the ritual encourages but does
    not require ranking them with rationale.
  - **Discipline rent-paying check.** Per session-14 closing
    addendum's distinction between during-work disciplines and
    reflection-driven disciplines, the chapter close may include
    a brief check on whether each post-reflection discipline got
    used during the chapter — did it shape future code, or did it
    age without being consulted? Useful for catching descriptive
    orientation that didn't earn its keep.

### Where the ritual lives

  - **This DECISIONS entry** is the load-bearing surface; the
    ritual is structurally captured here.
  - **CLAUDE.md's "Chapter-close ritual" section** is the
    navigational pointer; future fresh agents read CLAUDE.md
    first and see the ritual indexed there. Substantive details
    stay in this entry.
  - **Each chapter's CHAPTER_1_CLOSE.md** records the result of
    running the ritual — clean items, remediation entries,
    findings.

### Why this entry exists

The chapter-close audit in session 12 caught real problems but
operated as an ad-hoc investigation. Session 13's reflection
flagged that the transform-registry miss happened because the
chronological-append discipline of DECISIONS doesn't surface "this
trigger has fired." The Active deferrals index addressed that
specific failure mode; this entry addresses the broader one — that
the entire chapter-close audit should be a codified ritual, not a
re-derivation each time.

The session-14 operator-led reflection raised two CLAUDE.md
maintenance concerns; both are real and both belong in the ritual.
Codifying the ritual now makes CLAUDE.md maintenance load-bearing
rather than aspirational; it converts the user's named worry into
a structural answer.

**Reasoning / consequences.** Future chapter closes execute the
seven load-bearing items and write the result. The ritual itself
will likely refine across chapters — items will be added, items
will be marked informal-now-load-bearing as the discipline matures.
Each refinement updates this entry or appends a successor; the
discipline is alive, not frozen.

#### 2026-05-21 (session 25 amendment) — V1-input-envelope walk added as load-bearing item 8

Subagent #3's chapter-2-close audit (OSSYS chapter completeness)
surfaced that the chapter grew its won't-carry-forward list
opportunistically under fixture pressure (V2-IR shape coverage)
rather than under V1-input pressure (walking
`SnapshotJsonBuilder.cs` field-by-field). Six fixtures surfaced
six rule-bearing surfaces, but the V1 input envelope contains
additional information envelopes — `attributes[].onDisk`,
`attributes[].default`, `module.isSystem`/`isActive`,
`refEntity_isActive`, and others — that no fixture has yet
forced the chapter to consider. The trace-before-fixture
discipline caught what mattered for each slice; it did not
walk V1's full envelope at chapter open or chapter close.

Subagent #3's diagnosis: this is a different discipline gap
from the implicit-coverage finding session 24 surfaced. Session
24 was about V2-implementation-paths-not-fixture-exercised; this
is about V1-input-envelope-not-walked. The won't-carry-forward
list has never been audited against the V1 envelope projection
code field-by-field until subagent #3 performed that walk at
chapter close.

**The pattern parallels session 24's chapter-mid-audit
refinement:** a missing audit dimension surfaces during the
audit's own operation. The codebase's audit disciplines are
growing through their own use — the audits are not just
artifacts but *generative mechanisms producing further audit
disciplines*. Session 24 added active-deferrals scan to the
chapter-mid-audit; session 25 adds V1-envelope walk to the
chapter-close ritual. The meta-pattern (audits generating
disciplines) is the chapter-2 closing arc's most distinctive
intellectual feature.

**Decision: V1-input-envelope walk is load-bearing item 8 on
the chapter-close ritual.** Applies to V1↔V2 translation
chapters and chapters where a structured input envelope has
comprehensive content. Walks the V1-side envelope-projection
code field-by-field. Findings categorize into the three-class
typology (lossiness / boundary-discipline / alternative-IR-
surface).

**The ritual is now eight items.** Item 8 is the chapter-2
contribution; it is structurally a chapter-close-only item
(differs from the other seven in being chapter-class-conditional
rather than chapter-class-universal).

**Forward-looking.** Subsequent V1↔V2 translation chapters
(DACPAC adapter chapter; OData adapter chapter; future input
sources) inherit item 8 as a chapter-close obligation. The
chapter-3 canary chapter is not a translation chapter (it's a
deployment-validation chapter); item 8 is conditional and does
not apply there. Item 8's applicability is judged at chapter
open: if the chapter's structural shape is "consume V1 input;
emit V2 output," item 8 applies; if the chapter is V2-internal
or V2-to-external, item 8 does not.

The general lesson generalizes the audit-during-validation pattern:
**recurring audits codify into rituals; ad-hoc investigations don't
compound.** Rituals do — once codified, future iterations follow
the named pattern, contribute their findings, and refine the
ritual itself based on what surfaces.

## 2026-05-14 — DECISIONS is for resolved questions, not session narrative

**Status:** decided (operating discipline; corrects a drift introduced during session 14)
**Context:** Session 14 introduced session-closing reflections as
DECISIONS entries (commits 9 and 12) and session 15 followed with
its own reflection. These entries were narrative recaps —
commit lists, test baselines, forward signals, rent-paying checks
— with cross-references to the individual substantive entries
made during the session. The substantive content already lives in
those individual entries; the narrative wrapper duplicated it
once and then aged immediately.

The user surfaced the drift directly and asked for the session 14
and 15 reflections to be removed. Right call. Codifying the rule
so the drift does not recur.

**Decision:** **DECISIONS.md is for substantive resolved questions
only.** Session-narrative content — closing summaries, commit
lists, forward signals, rent-paying checks, "what surfaced this
session" — does not belong here.

**The substance test:** would this entry still be useful in six
months? If yes, it belongs in DECISIONS. If it ages with the
session, it belongs elsewhere.

  - **Substantive (in DECISIONS):** disciplines, refinements,
    cash-outs of deferrals, amendments to existing entries,
    codifications of patterns, decisions about specific design
    questions. Worked examples: pass return-type codification;
    named accessors for stacked types; anticipation vs.
    speculation refinement; chapter-close ritual.
  - **Session narrative (NOT in DECISIONS):** commit lists,
    test-baseline diffs, forward signals for the next session,
    "what surfaced during the work" recaps, rent-paying checks on
    specific disciplines, session-by-session reflections.

**Where session narrative belongs instead:**

  - **Commit messages.** In-flight findings during the work; the
    "what surfaced" content lands as part of the commit that
    addressed it. Disciplines named separately as their own
    DECISIONS entries.
  - **PR descriptions.** Summary of what shipped; forward signals
    for the next chapter; rent-paying observations.
  - **`HANDOFF.md` updates.** When a chapter closes and a new
    agent inherits, the bridge document captures the relevant
    context.
  - **`CHAPTER_1_CLOSE.md`** (or its equivalent for future
    chapters). Chapter-end audit synthesis lives there; that's
    where commit lists and forward signals belong.
  - **The conversation itself.** Reflections shared with the
    operator during the session — rent-paying checks, "did the
    discipline hold?" observations, "here's what I'd watch
    next" — are conversational, not durable. They go in the
    chat, not in DECISIONS.

**The drift that produced this entry.** Session 14's closing
entry was useful framing for session 15's opening — it named the
five disciplines and the meta-pattern about disciplines emerging
from work and reflection. But that framing lived in DECISIONS for
about three days before it was redundant; the disciplines
themselves were already documented in their own entries; the
forward signals were already in HANDOFF-style conversation. The
narrative wrapper aged faster than DECISIONS' append-only
discipline assumed.

The session 15 reflection compounded the drift — it extended the
narrative pattern with a rent-paying check structure that, while
useful as a check, shouldn't have lived in DECISIONS.

**Why it was tempting.** DECISIONS feels like the "official"
record of what a session produced; reflections naturally want to
live where the disciplines they observe live. The discipline-vs-
narrative line wasn't drawn explicitly; the drift happened by
default.

**The corrective rule, restated for the future:**

  Each substantive DECISIONS entry stands on its own. It is
  discoverable from the chronological log, from the Active
  deferrals index (if it codifies a deferral), from the operating
  disciplines table in CLAUDE.md (if it codifies a cross-cutting
  practice), and from cross-references in other entries. Session
  narrative does not need a separate substantive entry to be
  discoverable; the individual disciplines are already
  discoverable.

  When closing a session, the agent's reflection lives in the
  conversation with the operator, in the PR description, or in
  HANDOFF.md if the chapter is closing. The agent does not write
  a DECISIONS entry summarizing the session.

**Reasoning / consequences.** DECISIONS stays load-bearing only
where it earns rent. The narrative gets pruned (session 14 and 15
reflections removed in a follow-up commit). The discipline holds
going forward; future agents inherit the rule explicitly so the
drift does not recur.

The general lesson: **append-only disciplines need a complementary
prune-when-wrong discipline.** Append-only protects against
revisionism; prune-when-wrong protects against narrative drift.
The two pair: substantive content stays; narrative gets pruned
when the rule is violated.

## 2026-05-14 — Writer codification reaches its stability mark (heterogeneous third test held)

**Status:** decided (codification stability earned through three real tests; mirrors `DECISIONS 2026-05-13 — Strategy-layer codification reaches stability mark`)
**Context:** The Diagnostics writer landed at session 14 commit 3
with three predictions about how the dual-writer pattern would
behave: (1) the pass return-type codification (`Lineage<'output>`
vs `Lineage<Diagnostics<'output>>`) would absorb pass migrations
mechanically; (2) the named-accessor discipline would keep call
sites readable through migrations; (3) the Skip-to-Behavioral
activation pattern would be mechanically repeatable for new
consumers. Session 14 (UniqueIndex) and session 15 (Nullability)
provided two real tests of these predictions; both passed. The
codification was held back from a stability claim per `DECISIONS
2026-05-13 — Stability mark amendment` — N=2 within a coherent
shape (per-record decisions keyed by single SsKey, both emitting
on failure-side variants of their outcome DUs) earns less than
N=3 with at least one heterogeneous instance.

Session 16 ran the third test on ForeignKey, deliberately chosen
to be heterogeneous in emission shape: ForeignKey emits diagnostics
on **both** failure-side keep-reasons (mirroring UniqueIndex /
Nullability) **and** on a success-with-caveat variant
(`EnforceConstraint(ScriptWithNoCheck(orphanCount))`) within a
single pass. The substantive question: does the writer absorb
both shapes side-by-side without structural refinement?

**Decision:** **The Diagnostics writer codification reaches its
stability mark.** The four core predictions all held under the
heterogeneous third test:

| Prediction | ForeignKey outcome |
|---|---|
| Pass return-type codification absorbs the migration | ✓ ForeignKeyPass.run migrated to `Lineage<Diagnostics<ForeignKeyDecisionSet>>` mechanically; ~14 test sites updated via sed; no refinement to the writer or the codification required |
| Named-accessor discipline keeps call sites readable | ✓ `LineageDiagnostics.payload`, `ForeignKeyPass.decisionsOf` — same shape as the prior two passes; no smell-fix discoveries |
| Skip-to-Behavioral activation is mechanically repeatable | ✓ The session-13 Skip stub redirected to V2's actual success-with-caveat case (the V1 anchor was unreachable from V2 fixtures); the activation flipped to `[<Fact>]` cleanly |
| Diagnostic emission shape absorbs heterogeneity | ✓ Success-with-caveat (`EnforceConstraint(ScriptWithNoCheck _)`) and keep-reason (`DoNotEnforce(...)`) emissions produce structurally identical `DiagnosticEntry` values (same Source / Severity / field shape); only the `Code` prefix routes them. No structural distinction needed at the entry level |

**The empirical test for stability — the same one the strategy-layer codification used:**

  1. The codification was named in session 14 (a descriptive pass
     after the first instance — UniqueIndex).
  2. It was tested under closely-related variation through session
     15 (Nullability — second instance with similar shape, both
     failure-side keep-reasons within "per-record decision keyed
     by single SsKey" shape).
  3. It was tested under genuinely new pressure in session 16
     (ForeignKey — third instance with heterogeneous emission:
     keep-reasons + success-with-caveat within one pass, plus
     two reserved-but-unreachable variants for IR-refinement
     completeness).
  4. None of those tests forced a refinement. The absence of
     finding is itself the finding.

**What stability means in practice.** Future writer-consumer
activations after this point inherit a codification that has been
validated on:

  - Its central case (UniqueIndex per-index granularity, failure-
    side emission on PolicyDisabled / DataHasDuplicates / etc.)
  - Its variation case (Nullability per-attribute granularity,
    same failure-side shape with one audit-worthy
    KeepNullable(RelaxedUnderEvidence))
  - Its heterogeneous case (ForeignKey per-reference granularity,
    failure-side AND success-with-caveat emission within one pass)

Future agents migrating the fourth pass to the dual writer (likely
CategoricalUniqueness if a use case demands diagnostic emission, or
a future pass migrating from a third-channel split if that lands)
absorb the codification's conventions and trust they hold:
`Lineage<Diagnostics<DecisionSet>>` shape; `opportunityEntry`-style
mapping function; `LineageDiagnostics.payload` named accessor;
`Pass.decisionsOf` domain shortcut; same closed-DU exhaustiveness
discipline.

**What stability does not mean** (preserving the session-13 amendment's framing):

  - It does not mean the codification is finished. New pressure
    may surface refinements — the three-channel split (operator /
    auditor / developer per the constitution) is the most plausible
    refinement vector when a real consumer demands per-channel
    routing. The `Severity` field's three-way DU (Info | Warning |
    Error) may grow if a fourth band emerges. The `Metadata` field's
    `Map<string, string>` may promote to a typed DU when a consumer
    demands typed payload.
  - The stability claim is bounded by what's been tested. The three
    real tests were all single-channel synchronous emission with
    `Map<string, string>` metadata. Multi-channel, async-emitting,
    or typed-metadata consumers would be fourth-test-shaped.

Within those bounds, the stability mark is earned.

**Reasoning / consequences.** The Diagnostics writer's codification
has now been validated on the same empirical pattern the strategy-
layer codification was: descriptive pass → variation case →
heterogeneous case → no fourth refinement required. Future writer
work inherits a codification that holds; future stability marks for
other codifications follow the same N=3-with-heterogeneity protocol.

**The general lesson:** **stability claims earn their place through
heterogeneous third tests, not structurally-similar third tests.**
A third consumer with the same shape as the first two adds confidence
to a coherent-shape claim but doesn't extend the claim. A third
consumer with a different shape (like ForeignKey's success-with-
caveat alongside keep-reasons) tests whether the codification's
seams are positioned to absorb variation, not just to repeat the
same pattern. The protocol should be honored in future codification
work — when designing the third real test of any codification, pick
the case that stresses the seams you suspect, not the case that
confirms what you already know.

## 2026-05-14 — opportunityEntry stays inlined: N=3 of two distinct shapes, not N=3 of one

**Status:** decided (extraction question evaluated empirically; defer)
**Context:** Three passes (UniqueIndex, Nullability, ForeignKey)
each have a private `opportunityEntry` function that maps decisions
to `DiagnosticEntry option`. At surface count this is N=3 — the
two-consumer threshold (`DECISIONS 2026-05-13 — Emergent primitives`)
plus the anticipation-vs-speculation refinement (`DECISIONS
2026-05-13 — Anticipation vs. speculation`) suggest extraction
becomes a question at N=3. The session 14 reflection (now pruned;
preserved in commit history) and the session 15 reflection both
flagged the question for explicit evaluation here.

The substantive question — does the opportunityEntry-style mapping
earn primitive extraction at N=3? — has a more nuanced answer than
naive consumer-counting.

**The empirical inventory of the three opportunityEntry functions:**

| Pass | Input type | Mapping shape | Code prefix |
|---|---|---|---|
| UniqueIndex | `UniqueIndexDecision` | `match decision.Outcome with` → `EnforceUnique _` → None; `DoNotEnforce reason` → match-on-reason → entry | `tightening.uniqueIndex.<reason>` |
| Nullability | `NullabilityDecision` | `match decision.Outcome with` → `EnforceNotNull _` → None; `KeepNullable reason` → match-on-reason → entry (3 of 3 reasons handled, 2 emit None); `RequireOperatorApproval conflict` → match-on-conflict → entry | `tightening.nullability.<reason>` |
| ForeignKey | `ForeignKeyDecision` | `match decision.Outcome with` → `EnforceConstraint evidence` → match-on-evidence → entry-or-None (3 of 3 evidence handled; 1 emits Some, 2 emit None); `DoNotEnforce reason` → match-on-reason → entry (7 of 7 reasons handled, all emit Some) | `tightening.foreignKey.<reason>` |

**The shape distinction.** UniqueIndex and Nullability share a
deeper shape: only the failure-side variant of the outcome
emits-with-payload-mapping. The positive-side
(`EnforceUnique _` / `EnforceNotNull _`) collapses to `None`
without further inspection. ForeignKey is structurally different:
the positive-side (`EnforceConstraint evidence`) requires
inspection because one of three evidence variants
(`ScriptWithNoCheck _`) emits-with-payload while the other two
collapse to `None`.

**Two shapes, not one:**

  - **Shape A (N=2 — UniqueIndex, Nullability)**: positive-side
    is uniformly None; only the failure-side discriminates. The
    extracted primitive would be roughly:
    ```fsharp
    type DiagnosticPolicy<'outcome, 'failure> = {
        IsFailure : 'outcome -> 'failure option
        FailureToEntry : 'failure -> DiagnosticEntry
    }
    ```
  - **Shape B (N=1 — ForeignKey)**: both positive-side and
    failure-side discriminate; positive-side has at least one
    success-with-caveat. The extracted primitive would be roughly:
    ```fsharp
    type DiagnosticPolicy<'outcome> = {
        OutcomeToEntry : 'outcome -> DiagnosticEntry option
    }
    ```

The Shape-B form actually generalizes Shape-A (every Shape-A
function trivially fits the Shape-B signature). But the extraction's
ergonomics suffer at Shape-A consumers — they'd have to write
boilerplate handling positive-side variants that always collapse to
None.

**Decision:** **Defer extraction. The apparent N=3 is N=2-of-shape-A
plus N=1-of-shape-B; neither shape has reached the two-consumer
threshold within itself.**

The honest interpretation of the codebase's emission patterns:

  - Shape-A has two consumers (UniqueIndex, Nullability). At N=2
    the two-consumer threshold suggests the abstraction earns its
    place — but only within Shape-A.
  - Shape-B has one consumer (ForeignKey). At N=1 the threshold is
    not yet met.
  - Extracting a primitive that subsumes both shapes (the Shape-B
    generalization) would force Shape-A consumers to write boilerplate
    they don't need today; that's the "speculative abstraction"
    failure mode the discipline guards against.
  - Extracting a Shape-A-only primitive would leave ForeignKey
    inlined; the inconsistency creates its own friction.
  - Inlining all three preserves the per-pass clarity (each
    `opportunityEntry` is locally readable) at the cost of
    duplication — but the duplication is small (~30 lines each)
    and stable.

**The forward trigger for re-evaluation:**

  - **A fourth pass** (e.g., CategoricalUniquenessPass migrating to
    diagnostic emission, or a future pass like a Faker emitter that
    co-emits diagnostics) gives the question a fourth data point. If
    the fourth pass fits Shape-A, that's N=3 of Shape-A — extraction
    earns its place within Shape-A; ForeignKey's Shape-B remains
    inlined as the heterogeneous case. If the fourth pass fits
    Shape-B, that's N=2 of Shape-B — extraction earns its place
    within Shape-B; the Shape-A passes can opt into the same
    primitive at the cost of trivial boilerplate, or stay
    Shape-A-extracted.

  - **A consumer outside the pass layer** (e.g., the Faker emitter
    consuming decisions plus diagnostics from upstream passes; or a
    CLI shell composing per-strategy diagnostics across passes)
    might surface a need for a primitive that operates on
    `DiagnosticEntry list` rather than on outcome-to-entry mapping.
    That would be a different abstraction question entirely.

**Position B re-applied (per `DECISIONS 2026-05-13 — Anticipation vs. speculation`).**

  - Position A (extract fully now): wrong — the apparent N=3 is
    actually N=2+N=1 of distinct shapes; extracting against either
    shape introduces speculative cost.
  - Position B (structural alignment without extraction): the
    three opportunityEntry functions already share a structural
    shape — same input/output types modulo decision-type
    parameter, same use of `match decision.Outcome`, same
    `mkEntry` helper pattern. A future agent extracting can do so
    mechanically. No code change needed today; the structural
    alignment is honored by inlined consistency.
  - Position C (defer fully): the chosen disposition. Inlining at
    N=3-of-two-shapes preserves clarity; extraction becomes a
    question again at N=4 with concrete shape evidence.

**Reasoning / consequences.** Naive consumer-counting would have
extracted at N=3 and produced a primitive that one of three
consumers fits awkwardly. Looking at the shape distinction reveals
that N=3 is the wrong count; the right counts are N=2 and N=1.
The two-consumer threshold (within a shape, not across shapes) is
honored by deferring; the anticipation-vs-speculation refinement
is honored by recognizing that ForeignKey's heterogeneity is real
shape variation, not a misclassification.

**The general lesson:** **count consumers within a shape, not
across shapes.** When evaluating extraction, the question is "do N
consumers share the same shape such that one abstraction serves
them all without forcing accommodation?" Two consumers with the
same shape and one with a different shape is N=2-and-N=1, not N=3.
The discipline against speculative abstraction extends to
classifying consumers by shape before counting.

## 2026-05-15 — Strategic frame for the OSSYS implementation chapter (architectural commitments)

**Status:** decided (strategic frame; load-bearing for the OSSYS arc and beyond)
**Context:** Session 17 opens the OSSYS catalog adapter implementation
chapter. Multiple architectural commitments emerged from conversation
between session 16's close and session 17's opening; none had landed
in DECISIONS yet. This entry codifies them so they exist as
load-bearing context for the OSSYS arc and for the chapters that
follow (data emission, deployment integration, validation).

The frame is **strategic, not implementation-spec.** Specific
implementation choices land as their own DECISIONS entries when
those chapters open. This entry names the architectural axes;
subsequent entries fill them in.

### Posture 1, extended — V2 emits artifacts; deployment is downstream; the canary is upstream

The original Posture 1 stance: **V2 emits artifacts; ADO/Octopus
deploys to dev/staging/prod; V2 is not in the deployment path.**
The boundary is structural — V2's job ends when the artifacts are
written; downstream tooling owns the deploy.

The session-17 extension adds an **upstream pipeline canary**:
before V2 publishes the artifacts, the export pipeline self-validates
the artifacts against an ephemeral Docker SQL Server instance. The
artifacts must apply cleanly against an empty database; if the
canary fails, the export halts and the artifacts are not published.

The canary is **upstream of publication, not downstream of
deployment.** The deployment path remains ADO/Octopus territory;
the canary is the export pipeline's own self-validation. This
addresses a real failure mode V1 doesn't catch — artifacts that
look correct in isolation but don't apply cleanly together.

The canary's mechanism:

  - Spin up an ephemeral SQL Server container (testcontainers).
  - Apply the emitted artifacts (schema first, then seeds, then
    bootstrap) using DacFx for schema and direct script execution
    for data.
  - Read the resulting database state back through a read-side
    adapter (see below) into a V2 Catalog.
  - Compare the read-back Catalog to the source-of-truth Catalog
    that produced the artifacts. Any discrepancy halts the
    export.

The canary is **opt-in** at first — declared on EmissionPolicy or
its successor — but the architectural axis is named here so future
chapters know where it fits.

### Read-side adapter as a new architectural axis

The OSSYS adapter is the **write-side ingestion path**: take
OutSystems metadata, produce a V2 Catalog. The **read-side
adapter** is its sibling: take a SQL Server database, produce a
V2 Catalog by reading schema metadata back. Two distinct adapters,
both producing `Result<Catalog>`, both at the boundary.

The read-side adapter has **two consumers from day one**:

  1. **The canary's read-back step** (described above). The export
     pipeline writes artifacts, applies them to an ephemeral SQL
     Server, and reads back the resulting state to compare against
     the source Catalog.
  2. **Optional production observation.** A future operator might
     point V2's read-side adapter at a production database to
     observe the deployed schema's actual shape — useful for
     drift detection, post-deployment audits, and the V1
     `dmm-diff.json` equivalent.

Two consumers from day one is exactly the threshold the
two-consumer rule predicts (`DECISIONS 2026-05-13 — Emergent
primitives` and the session-16 shape-classification refinement).
The read-side adapter earns its place architecturally, not
speculatively.

The read-side adapter is **not in scope for the OSSYS chapter
opening** — it's a sibling architectural commitment named here so
the OSSYS write-side adapter doesn't accidentally calcify in a
shape that the read-side can't mirror.

### Refactor.log emission with deterministic SsKey-to-GUID via UUIDv5

V1 emits a `refactorlog` artifact (per the SSDT pattern) tracking
schema-rename events. The artifact requires GUIDs to identify
renamed objects.

V2's refactor.log emission uses **UUIDv5** to derive GUIDs
deterministically from `SsKey` values plus a stable namespace.
The choice eliminates a class of state V2 would otherwise need to
maintain — V1 tracks (or risks losing) GUID-to-object mappings
across runs; V2 derives the GUID at emission time and the same
SsKey always produces the same GUID. **No separate state.**

The UUIDv5 approach is structural-commitment-via-construction-
validation (`AXIOMS.md` operational principle) applied to GUIDs:
every GUID is derived; the derivation is deterministic; the
mapping is the function, not a stored table.

Implementation lands when the refactorlog emitter does. Naming
the choice now prevents the emitter from accidentally introducing
state-tracking machinery before this commitment is honored.

### Three data-emission classes named explicitly

V2 distinguishes three classes of data emission, each with its own
artifact shape and its own deployment semantics:

  1. **StaticSeeds.** Static-entity populations carried by the
     catalog itself (per A7 — Static modality is part of catalog
     structure). Emitted as MERGE seed scripts; deployed
     idempotently; expected to apply cleanly against existing
     populations or to seed empty tables.

  2. **MigrationDependencies.** Operator-policy-declared regular
     entities whose populations need to be carried forward as part
     of the migration. These are entities the operator has
     specifically marked — *MigrationDependency is a policy
     choice, not a structural property of Kind.* The catalog
     doesn't carry "this is a migration dependency"; the policy
     does. Same kind of separation as A18 amended (Policy is
     intent; Catalog is evidence).

  3. **Bootstrap.** A variable-composition emission class governed
     by a closed DU on `EmissionPolicy`:
     ```fsharp
     type BootstrapComposition =
         | AllRemaining       // default — everything not in StaticSeeds or MigrationDependencies
         | AllExceptStatic    // everything except static populations
         | AllData            // everything (including static populations)
     ```
     The DU is **closed** so consumers can pattern-match
     exhaustively; new variants land at meaningful inflection
     points per `DECISIONS 2026-05-13 — Discrete-rationale DUs`.

The three classes are **distinct artifacts**, not three shapes of
one artifact. Each class has its own emitter; the canary applies
them in order; the deployment pipeline carries all three.

### Verisimilitude policy held until real demand

A "verisimilitude policy" — controlling how faithfully V2's data
emission reproduces V1's exact byte sequence vs. how aggressively
V2 reformats — was discussed but **deferred until a real
validation consumer demands it.** Premature design here would
codify a policy axis that today has no consumer.

Forward trigger: when a real operator complains that V2's emission
differs from V1's in a way that breaks downstream tooling, the
verisimilitude policy lands. Not before.

### Projection.Pipeline as a new C# project

The canary's mechanism (testcontainers, DacFx, ephemeral SQL Server,
script execution) involves I/O, async, third-party dependencies, and
runtime concerns that V2's F# Core forbids by codification (per
CLAUDE.md's F# feature surface — purity-first sort; effect, time,
concurrency forbidden in Core).

The right home for the canary's orchestration is a **new C# project,
`Projection.Pipeline`**. C# is appropriate for:

  - DacFx integration (the .NET ecosystem's natural language for
    DacFx is C#)
  - Testcontainers usage (testcontainers.NET works fine from F#
    but is more idiomatic in C#)
  - Async orchestration with explicit Task/await semantics
  - Coordination between F# pure-core (the Catalog comparison
    logic) and the I/O surfaces

The codification preserves: **F# Core's purity is unchanged;
adapters at the boundary may use what Core forbids; the canary's
orchestration is at the boundary by definition.**

The project name `Projection.Pipeline` distinguishes from
`Projection.Adapters.*` (which are F# value-returning boundaries)
because the canary is more orchestration than adapter — it
coordinates multiple adapters and emitters into a single workflow.

### Docker SQL Server version hardcoded to match production

The canary's ephemeral SQL Server container is pinned to **the
exact SQL Server version that production runs**. Hardcoded; no
configuration knob; no version range.

Rationale: the canary's value-prop is "the artifacts apply cleanly
in production." The signal is meaningful only if the canary's
target matches production's. A version range introduces a class of
canary-passes-but-production-fails failures the canary exists to
prevent.

The hardcoded value lives at the canary's configuration surface
(`Projection.Pipeline`'s configuration). When production upgrades,
the canary upgrades atomically with it. The few-months-horizon
framing applies: short-lived hardcoding, not permanent.

### What's not in this entry

Specific implementation choices for any of the above are
**deferred to their own DECISIONS entries** when the relevant
chapters open:

  - The OSSYS adapter's parse signature → Position B entry
    (session 17 commit 4)
  - The read-side adapter's specific shape → its own chapter
    when it opens
  - The three data-emission classes' specific artifact formats →
    each emitter's chapter
  - The canary's specific orchestration → `Projection.Pipeline`
    chapter when it opens
  - The refactor.log emitter's UUIDv5 namespace and exact derivation
    → that emitter's chapter

#### 2026-05-16 (session 19 amendment) — canary's rename-handling depends on the SsKey-source path

The canary's roll-forward minimally-invasive guarantee — that
deployments to a fresh database render minimum diff against the
prior snapshot — depends on **SsKey preservation across renames
in the input source**. T8's structural diff is keyed by SsKey;
when source changes between snapshot N-1 and snapshot N include
a rename, the diff produces a `RENAME` only when SsKey is
preserved across the change.

**With the current OSSYS path** (`SnapshotJson` consuming V1's
canonical `osm_model.json`, which is lossy on SSKey per
`DECISIONS 2026-05-15 — OSSYS adapter translation rules`,
amended in session 19): a renamed entity produces a different
synthesized SsKey, so the diff sees `DELETE old + INSERT new`.
The canary's deployment-success leg still passes (the new state
deploys cleanly against an empty database); the deployment
script that gets generated drops and recreates the renamed
object — the **noisy mode** the strategic frame names V2 as
avoiding.

**The minimally-invasive guarantee is bounded by the input
path:**

  - With name-synthesized SsKey through the current JSON path:
    renames-across-the-JSON-path render as drop-create.
  - With any of the three re-open triggers fired
    (`SnapshotJsonBuilder` line-level fix; `SnapshotRowsets`
    variant; `LiveOssysConnection`): the bound resolves and
    renames produce structural-rename diffs.

**Graceful-degradation-shaped.** Drop-create renames are
**correct** (state matches end-to-end); they are just **noisy**.
Production operators will notice. The bound is documented; the
resolution path is reachable; the choice is open until either
empirical pressure (rename-fixture friction during the OSSYS
chapter) or a chapter or operator decision selects.

**This amendment exists because future agents opening the canary,
read-side adapter, or `Projection.Pipeline` chapters will need
to know which trigger has fired by the time they reach the
roll-forward-rename logic.** Making the dependency explicit in
the strategic frame keeps it visible across the gating-dependency
graph rather than leaving it implicit at the OSSYS adapter
boundary.

**No immediate work.** This amendment is documentation of the
constraint, not a directive to act on it. The OSSYS adapter
chapter continues without immediate need to resolve the
SsKey-source choice; the canary's later integration work will
inherit whichever trigger has fired by then.

#### 2026-05-17 (session 20 amendment) — input-source choice closed; canary's bound resolves when SnapshotRowsets lands

The session-19 amendment above named three reachable triggers
for resolving the canary's roll-forward minimally-invasive
guarantee. **Operator decision (per `DECISIONS 2026-05-15 —
OSSYS adapter translation rules`, session-20 amendment): Option
2 (`SnapshotRowsets` variant) is the canonical resolution
path.** This sub-section sharpens the canary's dependency
accordingly.

**The canary's roll-forward minimally-invasive guarantee
resolves when the `SnapshotRowsets` variant implements.** Until
that implementation lands, V2 continues consuming `SnapshotJson`
with name-synthesized SsKey; the canary deploys cleanly but
renames-across-the-input-path render as drop-create. This is
**graceful-degradation-pending** behavior — correct, just
noisier than the post-resolution state.

The session-19 framing of "graceful degradation; choice is
open" updates to **"graceful degradation pending; resolution
chosen; implementation sequences in."** Future agents opening
canary, read-side adapter, or `Projection.Pipeline` chapters
inherit `SnapshotRowsets` as the assumed input source for the
roll-forward-rename logic.

**Implementation timing for the canary's dependency.** The
`SnapshotRowsets` variant lands when chapter 2's organic flow
brings it — likely after the current OSSYS adapter chapter
completes its translation work. The canary's roll-forward
logic, when its chapter opens, can be designed against the
post-resolution state; the bound documented here applies only
to interim deployments where `SnapshotRowsets` has not yet
shipped.

This entry's role is to **name the architectural axes** so future
chapters land into a coherent frame. The axes are load-bearing;
the implementations are deferred.

**Reasoning / consequences.** Without this strategic frame, each
of the eight commitments would land separately as the chapter
that needs it opens, and the cross-chapter coherence would be
incidental. With the frame, every chapter that opens against one
of these axes inherits the other seven as context.

The frame is **subject to refinement** as chapters open and surface
real evidence — the OSSYS adapter chapter may surface a parse-
signature question that affects the read-side adapter's shape; the
canary's first real run may surface a verisimilitude need; the
three-emission-class scheme may need a fourth class. Refinements
land as amendments to this entry or as their own entries that
reference it.

## 2026-05-15 — OSSYS adapter parse signature (Position B; input slot decided)

**Status:** decided (Position B per `DECISIONS 2026-05-13 — Anticipation vs. speculation in abstraction extraction`)
**Context:** Session 17's chapter-open work names the OSSYS adapter
as the V2 boundary for OutSystems metadata ingestion. The
anticipation-vs-speculation refinement (session 14 commit 11)
recommends Position B for cases where a future abstraction's
shape is visible but its arrival is not concrete. `ICatalogReader`
is the named future abstraction (a second catalog source —
DACPAC, OData, in-memory test reader — would surface it). Position
B says: design the function signature to map cleanly to the
eventual interface; defer the interface itself.

This entry records Position B for the OSSYS adapter and decides
the open `<input>` slot the session-17 instruction explicitly
flagged.

### Decision

**The OSSYS adapter's canonical entry-point signature is:**

```fsharp
module Projection.Adapters.Osm.CatalogReader

val parse : SnapshotSource -> Task<Result<Catalog>>
```

**The `<input>` slot is the V1-produced JSON snapshot, lifted
into a small typed value:**

```fsharp
type SnapshotSource =
    /// Path to a V1-produced osm_model.json file on disk. Read
    /// synchronously inside the Task; the adapter is async at the
    /// boundary for ecosystem consistency, not because the file
    /// I/O itself benefits from it.
    | SnapshotFile of path: string
    /// In-memory snapshot string. Useful for tests and for
    /// pipelines that produce the snapshot in-memory rather than
    /// via disk.
    | SnapshotJson of json: string
```

The `SnapshotSource` DU is **closed** (per the strategy-layer
codification's discipline of closed-DU expansion when consumers
are at meaningful inflection points). Adding a third variant
(e.g., `LiveOssysConnection of connectionString` once V2 grows a
SQL-running entry point) lands as an explicit DU expansion,
not a silent open variant.

### Position B rationale: shape alignment for `ICatalogReader`

The session-14 anticipation-vs-speculation refinement named
`ICatalogReader` as a Position B candidate. The OSSYS adapter's
chapter-open is the moment to honor that: design the signature
so a future interface lands as a one-line wrapper, not a
retrofit.

The Position B alignment:

```fsharp
// Future, when a second catalog source materializes:
type ICatalogReader =
    abstract Read : SnapshotSource -> Task<Result<Catalog>>

// OSSYS adapter wraps trivially via object expression:
let osmReader : ICatalogReader =
    { new ICatalogReader with
        member _.Read source = Projection.Adapters.Osm.CatalogReader.parse source }

// A DACPAC reader (when it lands) wraps the same way:
let dacpacReader : ICatalogReader =
    { new ICatalogReader with
        member _.Read source = Projection.Adapters.Dacpac.CatalogReader.parse source }
```

The `SnapshotSource` DU is the abstraction's input parameter even
in the single-adapter case. A future DACPAC reader's variants
(`DacpacFile`, `DacpacBytes`) would expand the same DU, OR a
distinct `DacpacSource` DU would parallel it. Position B doesn't
require the DUs to merge — it requires the *signature shape* to
align so the interface, when it lands, doesn't force retrofit.

### Why JSON snapshot, not live OSSYS connection

The session-17 instruction asked: connection string to a live
OutSystems database, path to a JSON snapshot file, or a DU
accepting either?

**Decision: JSON snapshot only at chapter-open.** The
`SnapshotSource` DU has two variants today (file-path and
in-memory string); a third (`LiveOssysConnection`) is a Position-C
deferral with explicit re-open trigger.

**Rationale for the JSON-only choice:**

  1. **Preserves V1's reconciliation chain.** V1's 1184-line SQL
     script does the hard work of intent-vs-reality reconciliation
     (per the OSSYS ADMIRE chapter scope, session 17 commit 2).
     Re-implementing that work in V2 would be a substantial
     additional chapter; V2's OSSYS adapter does shape translation
     from V1's already-reconciled JSON, not re-reconciliation
     from raw SQL.
  2. **Preserves F# Core's no-I/O / no-time discipline at the
     test surface.** Reading a JSON file is a single point of
     I/O at the boundary; running the OSSYS SQL script is a
     full DbConnection lifecycle, async DB I/O, and ~22 rowset
     processors. JSON-path keeps the boundary thin.
  3. **The V2 fixture pattern already mirrors the JSON shape.**
     `tests/Fixtures/model.*.json` files are V1-shaped today;
     consuming them directly via the JSON-path adapter is the
     differential test V2 needs (per the OSSYS ADMIRE chapter
     scope's "differential validation" section).
  4. **The canary path stays clean.** The strategic frame's
     canary (session 17 commit 1) needs a Catalog input; reading
     it from a JSON snapshot is what V2's existing test surface
     does. The canary doesn't need a live OutSystems instance to
     validate emission; the canary applies V2's emitted artifacts
     against an ephemeral SQL Server, which is unrelated to the
     OSSYS adapter's input.

**Re-open trigger for `LiveOssysConnection`:** when a real
operator workflow demands V2 ingest OutSystems metadata directly
without staging through V1's JSON chain (e.g., a CLI surface
where the operator points V2 at an OutSystems database and V2
runs the extraction itself, replacing V1's `MetadataSnapshotRunner`
in V2's stack). Until that workflow surfaces, V1's SQL chain
remains the metadata producer; V2 reads its JSON output.

The `SnapshotSource` DU is the carrier for this future expansion.
When the trigger fires, a third variant lands; the parse function
gains a third branch; the rest of the adapter is unchanged.

### Why `Task<Result<Catalog>>`, not `Result<Catalog>`

The signature uses `Task<Result<Catalog>>` even though file I/O
on the JSON-path could be synchronous. The `Task` wrapping serves
two purposes:

  1. **`ICatalogReader` interface alignment.** A future DACPAC
     adapter (DACPAC files unzip and parse asynchronously) and
     a future `LiveOssysConnection` variant (DB I/O is async by
     definition) both need `Task<...>` shape. Placing the OSSYS
     adapter under the same shape today means the interface, when
     it lands, doesn't have to upcast sync `Result` to async
     `Task<Result>`.
  2. **Ecosystem consistency.** The trunk's V1 adapter
     (`MetadataSnapshotRunner.ExecuteAsync`) returns
     `Task<Result<OutsystemsMetadataSnapshot>>`. V2's OSSYS
     adapter mirroring the shape simplifies the C#-from-F#
     interop story when `Projection.Pipeline` (the canary's C#
     orchestration project) wants to call into V2's adapter.

The trade-off is small ceremony at the JSON-path call site
(`async { ... } |> Async.StartAsTask` or equivalent) in exchange
for shape alignment with future async-by-nature variants.

### Where the entry point lives (project structure)

The OSSYS adapter lives in a new project:
`src/Projection.Adapters.Osm/`. Sibling to `Projection.Adapters.Sql/`
(which today carries `Static.fs`, `ProfileSnapshot.fs`,
`ProfileStatistics.fs`). The choice of a separate project rather
than a file under `Projection.Adapters.Sql/` reflects the
adapter's distinct role:

  - `Projection.Adapters.Sql` is for SQL-Server-side metadata
    (column reality, FK reality, profile probes). It does NOT
    read OutSystems platform metadata; it reads database
    structural reality.
  - `Projection.Adapters.Osm` is for OutSystems-platform metadata
    (the OSSYS_* / OSUSR_* schema). It does NOT read database
    structural reality directly; it consumes V1's reconciled
    output.

The two adapters are siblings in the same architectural axis
(both read external metadata into V2's IR) but separate projects
because their input domains differ. The split also makes
test-fixture organization clearer: `Projection.Tests/Fixtures/`
JSON files belong to the Osm adapter's test surface; profile
snapshot fixtures belong to the Sql adapter's.

### What this entry doesn't decide

  - **The DTO shape inside the adapter.** Whether to use
    `System.Text.Json.JsonDocument`, hand-written DTO records, a
    type provider, or something else is implementation-territory
    for the next chapter in the OSSYS arc.
  - **Translation rules for V1↔V2 vocabulary.** The mapping rules
    for V1 `IsExternalEntity` + `IsSystemModule` → V2 `Origin`,
    V1 nullable `DeleteRule` → V2 closed `OnDelete`, etc., are
    implementation decisions that land in the relevant chapter.
  - **Test fixture strategy.** Whether to embed fixtures inline
    (per `StaticAdapterDifferentialTests.fs`'s pattern) or to
    consume V1's `tests/Fixtures/` JSON files directly is a test-
    surface decision the chapter-open hasn't reached yet.
  - **Diagnostic emission.** The OSSYS adapter will likely emit
    `DiagnosticEntry` values for parser warnings (per the
    Diagnostics writer that landed at session 14 commit 3); the
    return type extension to `Lineage<Diagnostics<Catalog>>` is
    deferred until the implementation chapter decides whether
    the adapter's diagnostics warrant the dual-writer shape or
    a simpler `Result<Catalog * DiagnosticEntry list>` tuple.

### Reasoning / consequences

The Position B framing says: **shape now, interface later.** The
parse signature is the shape; the interface is the deferral.
Future agents implementing the OSSYS adapter inherit the
signature as a constraint; future agents wrapping it in
`ICatalogReader` (when a second source materializes) inherit the
trivial wrapping path.

The JSON-only-at-chapter-open choice is itself a Position-B move
on the input slot: design `SnapshotSource` as a closed DU so a
future `LiveOssysConnection` variant lands cleanly, but don't
build it today. Two-consumer threshold (within a shape) applies
recursively — the variant earns its place when a second consumer
demands SQL-direct ingestion, not before.

This entry pairs with `DECISIONS 2026-05-15 — Strategic frame for
the OSSYS implementation chapter` (session 17 commit 1) and the
OSSYS ADMIRE chapter scope (session 17 commit 2). Together they
form the chapter-open: the strategic axes; the V1↔V2 chapter
scope; the canonical entry signature. The implementation
chapters open from here.

## 2026-05-15 — OSSYS adapter translation rules (chapter session 18; rules surfaced under empirical pressure)

**Status:** decided (chapter rules — extends as the OSSYS arc continues)
**Context:** Session 18 opened the OSSYS adapter implementation
chapter via the differential-test path: a minimal V1 fixture (one
module, one entity, two attributes) embedded in
`OsmCatalogReaderDifferentialTests.fs`; an expected V2 Catalog
hand-built; the parser implemented just enough to make the
assertion pass. Working under empirical pressure surfaced six
translation rules and one substantive architectural finding. This
entry captures them as the chapter's running translation-rules
list per the session 17 instruction's discipline.

The list **extends** as the OSSYS arc continues — each session
adding rules under the same empirical discipline. New rules land
either as amendments to this entry or as their own entries that
reference it.

### The substantive architectural finding: V1's JSON is lossy on SSKey identity

V1's metadata extraction chain produces SSKey values at the SQL
rowset layer (`EspaceSSKey`, `EntitySSKey`, `AttrSSKey` columns
per `outsystems_metadata_rowsets.sql`). The in-memory
`OutsystemsMetadataSnapshot` carries them. **But
`SnapshotJsonBuilder` does NOT write them to the canonical
`osm_model.json` document.** The assembled JSON carries names and
physical names; the SSKeys are discarded at JSON serialization.

V2's identity-survives-rename promise (A1) is bounded by what's
in the input. For the JSON-snapshot path, V2's `CatalogReader`
**synthesizes** `SsKey` deterministically from name fields:

  - Module: `OS_MOD_<ModuleName>`
  - Kind:   `OS_KIND_<ModuleName>_<EntityName>`
  - Attribute: `OS_ATTR_<ModuleName>_<EntityName>_<AttrName>`

The synthesis is stable across runs of identical input; same
JSON in, same SsKey out. Renames in the source OutSystems
platform produce different SsKey values in V2's IR — A1's
identity-survives-rename guarantee is **not honored** for renames
that traverse the JSON-snapshot path.

**Re-open triggers** (when this synthesis convention should be
revisited):

  - **V1's `SnapshotJsonBuilder` is extended to emit SSKeys.**
    The cleanest fix; preserves V1's chain-shape and makes V2's
    identity stable across renames.
  - **An alternative input source carries SSKeys natively.**
    A future `LiveOssysConnection` variant (per `DECISIONS
    2026-05-15 — OSSYS adapter parse signature`) running the
    SQL extraction directly would have access to the rowset
    SSKey columns; the synthesis convention becomes a fallback
    rather than the primary path.

Until either trigger fires, the synthesis convention is the
canonical V2 identity for OSSYS-sourced catalogs. **Documented
divergence; not a bug.**

This is the kind of finding the test-driven path was supposed
to surface — the rule was not visible from the orientation
reading; it became visible only when the parser had to produce
SsKey values for the assertion.

#### 2026-05-16 (session 19 amendment) — sharpened by SQL evidence; third re-open path; operator confirmation

Reading V1's `outsystems_metadata_rowsets.sql` directly sharpens
the original characterization. The lossiness is **at exactly one
projection layer**, not end-to-end:

```
ossys_* tables  →  temp tables (#E, #Ent, #Attr — SSKey present)
                →  trailing rowsets (SELECTs at script bottom — SSKey present)
                                                ↘
                                                  JSON pre-aggregations (#AttrJson,
                                                  #ModuleJson via FOR JSON PATH —
                                                  SSKey stripped)
                                                ↘
                                                  osm_model.json (SSKey stripped)
```

`#E` carries `EspaceSSKey`; `#Ent` carries `EntitySSKey` and
`PrimaryKeySSKey`; `#Attr` carries `AttrSSKey`. The trailing
rowset SELECTs at the bottom of the script all emit those
columns. The data is available everywhere upstream of the JSON
projection layer; what's lost is what the JSON `FOR JSON PATH`
projections happen not to include.

**The first re-open trigger is much cheaper than the original
entry implied.** Calling it "extending `SnapshotJsonBuilder`"
is technically correct but undersells the work: the existing
JSON projections already SELECT from `#Attr`, `#Ent`, `#E`.
Adding `a.AttrSSKey AS [ssKey]` (or similar) to the existing
`FOR JSON PATH` projections is **line-level additive, low-risk,
no upstream change**. The SQL extraction is already producing
the data; the canonical osm_model.json's projection is the only
thing that elides it.

**A third re-open path the original entry didn't enumerate.** The
SQL emits *both* the JSON for the canonical osm_model.json *and*
the trailing rowsets as result sets. If V2's input could be the
rowsets directly (delivered as some persisted form — multi-rowset
JSON, CSV per table, whatever the operational layer provides),
V2 gets SSKey natively without V1 pipeline cooperation. This
would land as a third `SnapshotSource` variant alongside
`SnapshotFile` and `SnapshotJson` — perhaps `SnapshotRowsets`
of some input type — and exercises the closed-DU expansion
discipline cleanly.

**Three paths, all confirmed reachable by the operator:**

  1. **`SnapshotJsonBuilder` line-level fix** — V1 cooperation;
     preserves V2's existing single-input-source posture; smallest
     diff at the V1 boundary.
  2. **`SnapshotRowsets` variant** — V2-internal; adds a new
     parsing surface to V2 but no V1 change required; exercises
     closed-DU expansion at `SnapshotSource`.
  3. **`LiveOssysConnection` variant** — substantial; V2
     maintains its own database connection running the SQL or
     equivalent extraction; reserved for future demand.

The choice between the three is **open**. The operator has
confirmed any of them works; the trade-offs differ:

  - **Path 1** depends on V1 pipeline cooperation but is
    architecturally invisible to V2.
  - **Path 2** requires no V1 cooperation but expands V2's
    parsing surface.
  - **Path 3** is the most architecturally substantial and
    reserved for the case where V2 needs to operate without
    V1's chain in the loop.

**The bounded-A1-claim disposition is unchanged** — through the
current `SnapshotJson` path V2 uses today, A1 is bounded; the
bound resolves when any of the three triggers fires. What
changes is that **the resolution is more reachable than the
original entry implied** — Path 1 is line-level work; Path 2 is
a closed-DU expansion within V2.

**No code change today.** Adding `SnapshotRowsets` speculatively
would violate the closed-DU expansion discipline (one consumer
needed; zero exist). The variant is named here so it's
discoverable when a real consumer surfaces; the entry is
amendment-only documentation.

**Strategic-frame implication (cross-reference).** The pipeline
canary's roll-forward minimally-invasive guarantee is bounded
by which of the three triggers is operating. See the strategic-
frame entry's session-19 amendment for the specific
canary-rename-handling implication.

#### 2026-05-17 (session 20 amendment) — operator decision: SnapshotRowsets is canonical

**The choice is closed.** Operator decision: **Option 2
(`SnapshotRowsets` as a third closed-DU variant on
`SnapshotSource`) is the canonical resolution path.** This
decision is not subject to relitigation; future sessions inherit
`SnapshotRowsets` as the assumed input source for OSSYS
metadata when the bound on A1 needs to resolve.

**Rationale.** Rowsets carry richer information than the
aggregated JSON does. Three concrete advantages over the
JSON-only path:

  1. **SSKey natively at every level.** `EspaceSSKey`,
     `EntitySSKey`, `PrimaryKeySSKey`, `AttrSSKey` are present
     in the rowsets; the V2 catalog reader reads them directly
     rather than synthesizing from names. A1's
     identity-survives-rename guarantee resolves to its full
     promise through this input path.
  2. **Per-table column structure preserved.** V1's `FOR JSON
     PATH` aggregations collapse some structural information
     that the rowsets retain. Specific examples will surface as
     fixtures grow under the OSSYS arc; the rowsets-as-input
     path future-proofs the boundary against the
     eleven-deferred-fields backlog the session-18 entry named
     and the session-19 entry extended.
  3. **Independent of V1 pipeline cooperation.** Unlike Option
     1 (extending `SnapshotJsonBuilder`), V2 doesn't depend on
     V1-side changes to land. The rowsets already exist as
     trailing SELECTs in `outsystems_metadata_rowsets.sql`;
     V2's adapter takes them in whatever persisted form the
     operational layer provides (multi-rowset JSON, per-table
     CSV, etc.).

**Why not Option 1 (extend `SnapshotJsonBuilder`).** Simpler
than Option 2 — line-level additive work to the JSON
projections — but solves only the immediate SSKey question.
Doesn't address the broader collapse of structural information
the JSON aggregation introduces; doesn't future-proof the V2
boundary against the deferred-fields backlog. The operator
considered Option 1 and chose against it.

**Why not Option 3 (`LiveOssysConnection`).** More
architecturally substantial than Option 2 — V2 maintains its
own database connection running the SQL or equivalent
extraction. Reserved as a future variant for the case where V2
needs to operate without V1's chain in the loop entirely. The
operator considered Option 3 and chose against it for now;
Option 3 remains as a future variant when its specific demand
surfaces.

**Implementation timing.** The actual `SnapshotRowsets` variant
lands when chapter 2's organic flow brings it — likely after
the current OSSYS adapter chapter completes its translation
work through the existing `SnapshotJson` path. The variant is
its own coherent slice when it opens. **Until implementation
lands, V2 continues consuming `SnapshotJson` with
name-synthesized SsKey; the bound on A1 through that path
remains as documented in this entry's original session-18
content.**

**The canonical resolution exists in documentation now; the
code follows when sequencing brings it.** Future sessions
opening canary chapters, read-side adapter chapters, or
roll-forward chapters inherit `SnapshotRowsets` as the assumed
input source. If implementation surfaces refinements during the
work (DTO shape questions, multi-rowset deserialization
choices, integration with existing parser code), those land as
their own DECISIONS entries — but the architectural commitment
to the variant itself is fixed.

**Entry-shape note for future readers.** This sub-section
supersedes the "three paths, choice open" framing in the
session-19 amendment above. The session-19 framing is preserved
verbatim as the historical lineage of the decision; this
sub-section is the load-bearing rule for future agents. The
amendment-discipline pattern: original text preserved; new
text supersedes; future readers see the lineage.

##### 2026-05-17 (session 20 strengthening — composability finding)

The session-20 external-entity slice surfaced a finding that
**strengthens the canonical-resolution choice beyond what was
visible at decision time**: V1's JSON projection layer is
structurally lossy in a class-shaped way, not coincidentally on
two unrelated fields.

The class has at least three currently-known members:

  - **SsKey at every level** — `EspaceSSKey`, `EntitySSKey`,
    `PrimaryKeySSKey`, `AttrSSKey` all stripped at JSON
    aggregation (session 18 finding).
  - **`EspaceKind`** — string column on `dbo.ossys_Espace`
    encoding the IS-vs-Direct distinction; stripped at JSON
    aggregation (session 20 finding via the external-entity
    fixture).
  - **`isSystemEntity`** — present in the `#Ent` rowset; not
    written by `SnapshotJsonBuilder` (observed during the
    session-20 trace; not yet exercised by a fixture).

Future fixtures may surface additional class members (per-table
column structure that `FOR JSON PATH` collapses; check-constraint
definitions; etc.). Each is a member of the same class.

**The reframing.** Option 1 (extend `SnapshotJsonBuilder`)
solves only one class member at a time. Option 2 (`SnapshotRowsets`)
absorbs the class structurally — once the variant implements,
**all class members resolve together**. The `EspaceKind` finding
from session 20 is empirical confirmation of what was an
architectural intuition at canonical-decision time: the rowsets
are the right level of abstraction to consume from, because the
JSON-projection lossiness is a structural property of that
projection layer, not a per-field oversight.

**The architectural commitment was more right than was visible
when it was made.** The operator's decision rests on a stronger
foundation now: the choice covers a class of lossiness, not just
the originally-named SsKey question.

**For the agent who opens the `SnapshotRowsets` implementation
chapter:** the implementation is not a one-bug fix. It's the
resolution to a class. Future fixtures are likely to surface
additional class members; the implementation needs to absorb
those too. The class is named in this entry's session-20
amendment to the Origin entry below (rule 17's amendment
section); reference it from the implementation chapter when it
opens.

### Translation rules the minimal fixture forced

| # | V1 input shape | V2 output | Rationale |
|---|---|---|---|
| 1 | Module `name` (string) | `Module.SsKey = OS_MOD_<name>`, `Module.Name = Name.create name` | SsKey synthesis (see finding above). The Name DU validates non-blank; module-level translation fails early on blank input. |
| 2 | Entity `name` + parent module `name` | `Kind.SsKey = OS_KIND_<modName>_<entName>` | Synthesis includes module name to disambiguate same-named entities across modules. |
| 3 | Attribute `name` + parent entity + module | `Attribute.SsKey = OS_ATTR_<modName>_<entName>_<attrName>` | Three-level naming preserves attribute identity across module / entity rename scenarios. |
| 4 | `dataType: "Identifier"` | `Attribute.Type = Integer` | OutSystems' Identifier data type is the standard PK type; V2 maps it to the Integer primitive. The `isAutoNumber` flag is **silently dropped** at the V2 boundary today (V2 IR has no auto-number axis; deferred). [session-25 wording fix: original phrasing said "read but discarded today" — the adapter does not in fact call `getProperty`/`getBool` on `isAutoNumber`; subagent #3 audit M3.] |
| 5 | `dataType: "Text"` | `Attribute.Type = Text` | Direct mapping. The `length` field is read but discarded today (V2 IR has no per-attribute length axis; SQL-type translation handles length at emit time per Policy A13). |
| 6 | `physicalName` (string) | `Attribute.Column.ColumnName = physicalName` | Direct. The `originalName` and `databaseColumnName` fields are not in this fixture; their translation rule lands when a fixture surfaces them. |
| 7 | `isMandatory: true \| false` | `Attribute.IsMandatory = isMandatory`, `Attribute.Column.IsNullable = not isMandatory` | The IsNullable proxy is **catalog-only**; it derives nullability from logical mandatory rather than from physical evidence. Profile evidence (when wired) refines it. The OSSYS adapter's job is structural; physical-reality reconciliation lives in V1's SQL chain (already done before V2 sees the JSON) and in `Projection.Adapters.Sql/ProfileSnapshot.fs` (separate input). |
| 8 | `isIdentifier: true \| false` | `Attribute.IsPrimaryKey = isIdentifier` | Direct. V1's `isIdentifier` flag corresponds to V2's structural PK marker. |
| 9 | Entity `db_schema` + `physicalName` | `Kind.Physical = { Schema; Table }` | Direct; the V1 JSON's reconciled `db_schema` already accounts for any `db_catalog` context (which V2 ignores per the OSSYS ADMIRE chapter scope's "what V2 will explicitly NOT carry forward" section). |
| 10 | `isStatic: true` → `Modality = [Static []]`; `isStatic: false` → `Modality = []` | Per A7. Static populations themselves come from a separate input (V1's static-data JSON via `Projection.Adapters.Sql/Static.fs`). The OSSYS adapter sets the modality marker; the populations join later. |
| 11 | `isExternal: false` → `Origin = OsNative`; `isExternal: true` → `Origin = ExternalDirect` | **SUPERSEDED by rule 17 (session 20).** Original placeholder for the minimal fixture; the IS-vs-Direct distinction was deferred at the time. Session 20's external-entity slice (`DECISIONS.md:5231` rule 17 — JSON path bound) replaced the `ExternalDirect` mapping with `ExternalViaIntegrationStudio` under empirical pressure. The adapter at `CatalogReader.fs:337-338` implements rule 17. Original retained for chapter-close-audit lineage; live rule is 17. |

### What this commit explicitly does NOT carry forward (yet)

Fields the minimal fixture contains but the parser ignores:

  - `attributes[].originalName` — V1's pre-rename name; V2 has no
    rename-history axis on Attribute. Defer until a use case
    demands it (likely the refactor.log emission per the
    strategic frame).
  - `attributes[].length` / `precision` / `scale` — V1 type
    metadata. V2 IR's `PrimitiveType` is abstract; concrete
    SQL-type details land in emitter-time policy (A13). Defer
    until either the IR grows a length-bearing variant or a
    consumer demands the discriminated translation.
  - `attributes[].isAutoNumber` — V1 auto-number flag; V2 IR
    has no auto-number axis on Attribute. Defer.
  - `attributes[].isActive` — V1 activity flag. Per the OSSYS
    ADMIRE chapter scope, V2's `Selection` policy handles
    activity at the policy level. The minimal fixture sets
    everything to `isActive=true`; the reader currently does
    not check it. Re-open trigger: a fixture with mixed-active
    entities or attributes surfaces the boundary-vs-policy
    decision (filter at adapter, or carry through with a
    distinct V2 representation).
  - `attributes[].isReference` / `refEntityId` / `refEntity_name`
    / `reference_deleteRuleCode` etc. — Reference translation.
    The minimal fixture has `isReference: 0` for both
    attributes; references aren't exercised. The next session
    in the OSSYS arc likely adds a reference-bearing fixture
    and surfaces the V1 nullable `DeleteRule` → V2 closed
    `OnDelete` translation rule.
  - `attributes[].external_dbType` — External-DB type for
    integration-studio attributes. Defer with the Origin
    rule.
  - `attributes[].physical_isPresentButInactive` — V1's
    inactive-but-physically-present marker. Defer with the
    activity rule.
  - `entities[].relationships` — Reference list. Empty in this
    fixture; translation defers to the reference-bearing fixture.
  - `entities[].indexes` — Index list. Empty in this fixture;
    translation defers to the index-bearing fixture.
  - `entities[].triggers` — Trigger list. Empty in this fixture;
    per the OSSYS ADMIRE chapter scope, V2 has no Trigger IR
    type today. Defer until consumer demand surfaces the IR
    refinement.
  - `entities[].db_catalog` — Cross-catalog FK marker. Per the
    Active deferrals index, the cross-catalog IR refinement is
    reserved-but-unreachable; the fixture has `null`; the
    parser ignores the field.
  - `entities[].meta` — Entity description string. V2 IR has no
    description axis. Defer.
  - Top-level `exportedAtUtc` — V1 export timestamp. V2 has no
    catalog-level timestamp; the `Lineage` writer captures
    when each pass runs. Defer with explicit not-carried.

### Discipline going forward

The chapter accumulates translation rules under empirical
pressure. Each subsequent session in the OSSYS arc extends the
running list with new rules surfaced by new fixtures. The
discipline:

  1. New fixture lands in `OsmCatalogReaderDifferentialTests.fs`
     (or a sibling test file) embedding a V1 shape that surfaces
     a translation question.
  2. Test fails until the parser handles the new shape.
  3. Parser implementation lands; new translation rules surface.
  4. The rules are appended to this entry (or a sibling entry
     references them) with the empirical example attached.

This is the same shape as the strategy-layer codification's
empirical-verdict process (`DECISIONS 2026-05-11 — Strategy-layer
codification: empirical verdict after the fourth instance`):
rules emerge from real consumers, not from speculation about
hypothetical shapes. The chapter's running list is the
audit-trail.

### Reasoning / consequences

The differential-test path produced exactly the value session 17's
instruction predicted: rules surfaced under code pressure rather
than under speculative reasoning. The SsKey-lossy-JSON finding
specifically would have been hard to anticipate from the
orientation reading alone — it became visible only when the
parser had to produce SsKey values for the assertion. Future
chapter sessions following the same path are likely to surface
similar findings; the running translation-rules list is how the
chapter accumulates them auditably.

The won't-carry-forward list (above) extends the OSSYS ADMIRE
entry's chapter-scope section with concrete examples from the
minimal fixture. As subsequent fixtures land, more V1 fields
will surface that need either-way decisions; keeping them
explicit (rather than letting them emerge silently as gaps) is
the discipline session 17's instruction named.

#### 2026-05-16 (session 19 amendment) — reference-bearing fixture extends the running list with five FK translation rules

Session 19's reference-bearing fixture (User → Account FK with
`reference_deleteRuleCode: "Protect"`) surfaced five translation
rules under empirical pressure. Appended to the running list as
rules 12–16; the table-shape from the original entry continues.

**The deferred V1 nullable `deleteRuleCode` → V2 closed `OnDelete`
question is now resolved.** Session 17's OSSYS ADMIRE chapter
scope named this as one of three deferred translation questions;
it lands here as rule 13 with the full mapping table per V1's
existing convention in `Osm.Smo/SmoEntityEmitter.cs`.

| #  | V1 input shape | V2 output | Rationale |
|----|---|---|---|
| 12 | Source attribute name + parent entity + module (when `isReference: 1`) | `Reference.SsKey = OS_REF_<modName>_<entName>_<attrName>` | Reference SsKey synthesis. The reference identifies by its source coordinate; an attribute carries at most one outgoing reference in V1's metadata, so the source coordinate is unique. |
| 13 | `reference_deleteRuleCode: "Protect"` | `Reference.OnDelete = NoAction` | V1 → V2 mapping per `Osm.Smo/SmoEntityEmitter.cs`. The full table: `"Delete" → Cascade`; `"Protect" → NoAction`; `"Ignore" → NoAction`; `"SetNull" → SetNull`; `null → NoAction` (the V1 `TreatMissingDeleteRuleAsIgnore` default). The minimal fixture exercises only "Protect"; the full table lands so subsequent fixtures don't re-litigate. **Note:** `"Ignore"` collapses to V2 `NoAction` because V2's `ReferenceAction` DU has no Ignore variant and V1's "Ignore" is semantically `NoAction` at the SQL level (the V1 audit-worthy "we tolerated a missing delete-rule" concern belongs to the Diagnostics writer, per session 16 commit 1's FK activation). The session 18 finding that V2's `DeleteRuleIgnored` keep-reason is unreachable from V2 fixtures resolves here too: if V1's `deleteRuleCode` is `"Ignore"`, V2's `OnDelete` becomes `NoAction` and the reference is *enforced* (V2 doesn't decline to enforce); the V1 audit-trail concern emits a Diagnostics entry rather than a structural keep-reason. |
| 14 | V1 attributes with `isReference: 1` carry full reference fields (`refEntity_name`, `reference_deleteRuleCode`, etc.) | Walk attributes for `isReference: 1`; ignore the `relationships[]` array | V1 carries reference info in two places — on the source attribute and in the parent entity's `relationships[]` array (with `viaAttributeName + toEntity_name + hasDbConstraint`). The V2 adapter walks the attribute fields because they carry every field the V2 `Reference` shape needs. The `relationships[]` array is V1's aggregated cross-check; it could become a verification surface later but is not the primary source. **Documented divergence:** V1's two-source representation collapses to V2's one-source extraction. |
| 15 | Source attribute name | `Reference.Name = Name.create attrName` | V1 has no separate "relationship name" field; the via-attribute carries the relationship's display identity. The V2 `Reference.Name` derives from the attribute name (e.g., User's `AccountId` attribute produces a Reference named "AccountId"). Same-shape with V2's existing convention for un-named structural elements. |
| 16 | V1 `refEntity_name` (within the same module's catalog) | `Reference.TargetKind = OS_KIND_<sourceModule>_<refEntity_name>` | Same-module assumption. Cross-module FK references would require either: (a) carrying `refEntity_module` in V1's JSON (V1 does not today), or (b) V2 adapter scanning all modules to disambiguate (problematic when names collide). The same-module rule covers every fixture seen so far; cross-module references defer until a fixture surfaces the case (re-open trigger). |

#### What this commit explicitly does NOT carry forward (FK extensions)

Adding to the won't-carry-forward list:

  - `attributes[].refEntityId` — V1's numeric foreign-key-target
    pointer. V2 uses synthesized SsKey via name; the numeric ID
    is V1's internal database ID, not stable across deployments.
    The parser reads but ignores the field.
  - `attributes[].refEntity_physicalName` — V1's pre-resolved
    target physical table name. V2 derives the target's physical
    realization from the target Kind, not from the source
    attribute. Redundant under the same-module assumption.
  - `attributes[].reference_hasDbConstraint` — V1's flag for
    whether the physical FK constraint exists at the database
    level. V2's `Reference` carries no "is enforced" axis at the
    structural level — that distinction lives in `Profile`
    (empirical evidence, per A34's separation of structure from
    evidence) and in `ForeignKeyOutcome.EnforceConstraint(...)`
    decisions. The catalog reader surfaces structural FKs only;
    the Profile-side reader (separate input) carries
    `hasDbConstraint`-equivalent evidence.
  - `entities[].relationships[]` — entity-level aggregated
    relationship array. V2 walks attributes for primary
    extraction; relationships[] is unconsumed. Re-open trigger:
    if a future fixture surfaces a relationship that exists in
    relationships[] but NOT in attributes[isReference=1] (or
    vice versa), the divergence forces a cross-check.

#### Updated chapter status

Two slices through the OSSYS adapter chapter:

  - Session 18: minimal slice (one entity, two non-reference
    attributes). Eleven translation rules surfaced.
  - Session 19: reference-bearing slice (two entities, one
    reference). Five additional rules surfaced; the deferred
    deleteRuleCode question resolved.

Sixteen rules total in the running list. The two remaining
deferred questions from the session 17 ADMIRE chapter scope:

  - V1 `IsExternalEntity` + `IsSystemEntity` → V2 `Origin`
    three-way DU. Still pending; minimal and reference fixtures
    both have `isExternal: false`. A fixture with an external
    entity (Integration Studio or Direct) surfaces it.
  - Inactive-records boundary choice (filter at adapter or
    carry through and let Selection filter). Still pending; all
    fixtures so far have `isActive: true` everywhere. A
    mixed-active fixture surfaces it.

These continue to defer; the chapter's discipline holds — rules
land under empirical pressure, not under speculative reasoning.

#### 2026-05-17 (session 20 amendment) — external-entity fixture surfaces the Origin translation rule; placeholder updated under empirical pressure

Session 20's external-entity fixture surfaced the Origin
three-way collapse rule that the session 17 OSSYS ADMIRE chapter
scope flagged as one of three deferred translation questions.
Three substantive findings landed under the same fixture-driven
empirical-pressure discipline.

**Finding 1: V1's IS-vs-Direct distinction is encoded in
`EspaceKind`, which is NOT carried through V1's JSON projection.**
Trace performed before writing the fixture:

  - `EspaceKind` is a string column on `dbo.ossys_Espace`
    (V1 OutSystems platform metadata) read by V1's
    `outsystems_metadata_rowsets.sql` at line 96 (`#E.EspaceKind`).
  - The trailing rowset SELECT for the `#E` table emits
    `EspaceKind` (line 961 of the same file).
  - **`SnapshotJsonBuilder` does NOT write `EspaceKind` to
    `osm_model.json`.** The JSON output for modules carries only
    `name`, `isSystem`, `isActive`, `entities`. The IS-vs-Direct
    distinction is invisible to V2 through the `SnapshotJson`
    path.

This composes with the SsKey-lossy-JSON finding from session 18:
both deferred translation questions resolve through the same
input-path expansion (the `SnapshotRowsets` variant per
`DECISIONS 2026-05-15 — OSSYS adapter translation rules`,
session-20 amendment of the lossy-SSKey rule). The
`SnapshotRowsets` canonical resolution covers a **class** of
JSON-projection-lossiness questions, not just the SsKey question
that surfaced first.

**Finding 2: The session-18 placeholder for `isExternal: true`
was speculative; the session-20 fixture provides empirical
pressure to revise it.** Session 18's parser mapped
`isExternal: true` to `ExternalDirect` as a placeholder. That
choice was made when no fixture exercised the `isExternal: true`
branch; the rule was speculative. The session-20 fixture
mirrors V1's existing `model.edge-case.json` shape (the
`ExtBilling` module — the "Ext" prefix is conventional for
IS-extension modules in V1's domain). The placeholder updates
under that pressure to `ExternalViaIntegrationStudio`.

**The new placeholder rule (rule 17, extending the running
list):**

| #  | V1 input shape | V2 output | Rationale |
|----|---|---|---|
| 17 | Entity `isExternal` boolean (through the JSON path; `EspaceKind` not visible to V2) | `isExternal: false` → `OsNative`; `isExternal: true` → `ExternalViaIntegrationStudio` | Through the JSON-snapshot path, V2 cannot distinguish IS-vs-Direct because `EspaceKind` is stripped at the JSON projection layer. Placeholder picks `ExternalViaIntegrationStudio` because IS extensions are the standard V1 mechanism for external entities; most `isExternal=true` cases are IS-imported. The full three-way distinction (with `ExternalDirect` for non-IS external entities) resolves when `SnapshotRowsets` implements and `EspaceKind` becomes visible. **This rule supersedes the session-18 placeholder (`ExternalDirect`)** which was speculative without empirical pressure. |

**Finding 3: The bounded-A1-equivalent disposition extends to
Origin.** Through the `SnapshotJson` path, V2's three-way
`Origin` discrimination is bounded — `OsNative` and
`ExternalViaIntegrationStudio` are reachable; `ExternalDirect`
is unreachable from V2 fixtures because the JSON shape can't
distinguish it from IS-extension external entities. This is the
same shape as the bounded-A1 disposition from the session-18
SsKey finding. **Documented divergence; not a bug.**

The bound resolves identically to the SsKey bound — through
the same `SnapshotRowsets` canonical-resolution path. When
`SnapshotRowsets` implements, the V2 catalog reader gains
access to `EspaceKind` and the Origin translation rule
refines:

  - `isExternal: false` → `OsNative` (unchanged)
  - `isExternal: true` AND `EspaceKind: "Extension"` (or whatever
    the IS-marker turns out to be) → `ExternalViaIntegrationStudio`
  - `isExternal: true` AND not the IS-marker → `ExternalDirect`

The exact rule needs the empirical evidence of what
`EspaceKind` values appear and what they mean — that's the
work for the session that lands `SnapshotRowsets`.

**Updated chapter status (translation rules in the running list):**

  - Sessions 18: rules 1–11 (minimal slice — module / kind /
    attribute structure, type primitives, modality)
  - Session 19: rules 12–16 (reference-bearing slice — FK
    SsKey synthesis, deleteRuleCode mapping, attributes-as-
    primary-source, same-module assumption)
  - Session 20: rule 17 (Origin three-way placeholder under
    JSON-path bound)

Seventeen rules total in the running list.

**One deferred translation question remains** (from the session
17 ADMIRE chapter scope):

  - Inactive-records boundary choice (filter at adapter or
    carry through and let Selection filter). All fixtures so
    far have `isActive: true` everywhere. A mixed-active
    fixture surfaces it.

This continues to defer; the chapter's discipline holds — rules
land under empirical pressure, not under speculative reasoning.

**The composability finding is itself worth marking.** Two
deferred translation questions (lossy-SSKey from session 18,
IS-vs-Direct from session 20) both resolve through the same
input-path expansion (`SnapshotRowsets`). The OSSYS chapter is
discovering that the `JSON projection layer is structurally
lossy in a class-shaped way — multiple V1 fields are stripped at
the same projection layer; the resolution to any single one
generalizes to all. The class is named here so future agents
opening the `SnapshotRowsets` implementation chapter inherit
the framing: it's not three separate fixes; it's one resolution
to a class of lossiness.

Future fixtures may surface additional members of the same
class (e.g., `isSystemEntity` is in the rowsets but not the
JSON; per-table column structure that `FOR JSON PATH`
collapses; check-constraint definitions; etc.). Each is
deferred-by-input-path until `SnapshotRowsets` lands; the
single resolution covers them all.

#### 2026-05-18 (session 21 amendment) — mixed-active fixture surfaces inactive-records boundary; chapter-open backlog clears

Session 21's mixed-active fixture surfaced the deferred
inactive-records boundary choice that the session 17 OSSYS
ADMIRE chapter scope flagged as the third (and last) of its
deferred translation questions.

**Trace before fixture (admire-mode discipline at the slice
level — same as session 20):** V1 SQL carries IsActive flags
through to JSON at three levels — module-level (line 924),
entity-level (line 931), attribute-level (line 759). V1 SQL
also has SQL-layer pre-filtering parameters
(`@IncludeInactive` line 127; `@OnlyActiveAttributes` line
254). **The flags ARE visible to V2 through the JSON path.**
Unlike the SsKey question (session 18) and the IS-vs-Direct
question (session 20), inactive-records-handling is **NOT a
member of the JSON-projection-lossiness class** — V2 has the
information; the boundary choice is genuine.

**The boundary choice and its rationale:**

The architectural alternatives:

  - **Filter at adapter** — entity/attribute with `isActive: false`
    is dropped from the V2 Catalog at parse time.
  - **Carry through with IsActive axis** — V2 IR grows a
    per-record IsActive axis (on Kind / Attribute, or as a
    `Modality.Inactive` variant); the Selection policy filters
    at projection time per A18 amended (filtering is operator
    intent, which is Policy).

A18 amended (Π consumes Catalog × Profile, never Policy)
argues for carry-through in principle — filtering is operator
intent, not catalog evidence. But:

  - V2 IR has no per-record IsActive axis today; carry-through
    requires substantive IR refinement.
  - "IR grows under evidence" — no current V2 consumer demands
    the inactive records' presence in V2's IR. No emitter
    uses them; no pass consumes them; no Selection policy axis
    today reads "include inactive" or "exclude inactive."
  - The adapter's existing return shape `Task<Result<Catalog>>`
    cannot carry per-record auditability for dropped records;
    that requires extending the return shape to a
    Diagnostics-bearing variant (which is its own future
    slice).

**Decision: filter at adapter for now; document the bound.**
The smallest honest-now implementation. The bound resolves
when one of the following triggers fires:

  - **A real consumer demands inactive records' presence in
    V2's IR.** Likely candidates: a refactor.log emission
    that needs inactive entities to compute deletion sets; a
    multi-environment Selection policy that wants different
    inclusion rules for different deployments. When such a
    consumer surfaces, the IR grows (likely as a
    `Modality.Inactive` variant for entity-level, plus a
    per-attribute axis for attribute-level — the exact shape
    depends on the consumer); the adapter changes to
    carry-through; this rule supersedes.
  - **The adapter's return shape extends to support
    Diagnostics-attached audit.** When the adapter's return
    shape grows from `Task<Result<Catalog>>` to a
    Diagnostics-bearing variant (likely
    `Task<Result<Diagnostics<Catalog>>>` or similar), the
    silent drop becomes audited drop — each filtered record
    emits a `DiagnosticEntry` with `Source = "adapter:Osm"`,
    `Severity = Info`, `Code = "adapter.osm.inactiveRecordDropped"`,
    and the dropped record's identity. The structural rule
    stays "filter at adapter"; the audit improves.

**The new translation rule (rule 18, extending the running
list):**

| #  | V1 input shape | V2 output | Rationale |
|----|---|---|---|
| 18 | `entity.isActive: false` or `attribute.isActive: false` (default missing → true per V1's SQL `ISNULL(Is_Active, 1)` semantics) | Inactive entities are dropped from the V2 Catalog at parse time; inactive attributes are dropped from their Kind's `Attributes` list. | Filter at adapter under "IR grows under evidence" — no current consumer demands inactive records' presence in V2's IR. The drop is silent today; the future Diagnostics-attached audit is named in the bound. The carry-through alternative defers until a real consumer surfaces. |

**Module-level `isActive: false`** is **not** exercised by the
mixed-active fixture and not yet handled by the parser.
Defers until a fixture forces the question. The most likely
shape: same filter rule (drop the module entirely), but
modules are coproduct cells (A11) and dropping a module drops
all its entities, which is a bigger semantic claim than
dropping individual records. Surface when a fixture requires
it.

**`physical_isPresentButInactive` field** in V1's JSON (line
769 of SnapshotJsonBuilder; example at the
`DeprecatedField` attribute in this slice's fixture, value 1)
is **read but discarded today**. V1's SQL surfaces this as a
derived flag — the attribute's logical IsActive is false but
the physical column exists. V2's adapter has no use for the
flag because it filters the inactive attribute before
encountering it. Re-open trigger: a Diagnostics-bearing
adapter that wants to surface "the physical column is still
present even though the logical attribute is retired" as an
audit-trail concern.

#### Chapter-open backlog clears at session 21 — natural within-chapter milestone

The chapter has now cleared all three deferred translation
questions named in the session 17 OSSYS ADMIRE chapter scope:

  - **Origin three-way collapse** — resolved session 20 (rule
    17 + bounded-by-input-path disposition + composability
    finding pointing at the SnapshotRowsets canonical
    resolution).
  - **Reference DeleteRule** — resolved across sessions 18–19
    via the Ignore-mapping composition (rule 13's full table
    + the V2-NoAction-as-Ignore-target finding that resolved
    the unreachable-`DeleteRuleIgnored`-keep-reason loose end
    from session 16).
  - **Inactive-records boundary** — resolved this session
    (rule 18 + bound documented + carry-through trigger
    named).

Eighteen translation rules total in the running list across
four substantive slices.

This is a **natural within-chapter milestone**, not a
chapter-close. The chapter has more substantive slices ahead
— index-bearing, static-entity, cross-module FK, plus
whatever new V1 fields surface from real fixtures as the
adapter is exercised against larger inputs. But the
chapter-open's named uncertainties have all been answered
under empirical pressure. The chapter's discipline is
operating; the running list is auditable; the bounds are
documented.

#### 2026-05-19 (session 22 documentation hygiene) — naming the two classes of resolution patterns explicitly

Sessions 18, 20, and 21 together produced findings that fit
into two structurally distinct classes. The composability
finding from session 20 named the first class (lossiness);
session 21's inactive-records resolution implicitly distinguished
the second class (boundary discipline) by resolving differently.
This sub-section names both classes explicitly so future agents
reading the chapter's accumulated translation surface see the
distinction up front rather than re-deriving it.

**The two classes:**

  1. **JSON-projection-lossiness class.** The information is
     **upstream of V2's current input but stripped at V1's JSON
     projection layer**. V2 cannot make the translation through
     the current `SnapshotJson` path because the data isn't
     visible. Resolution: **input-path expansion** via the
     `SnapshotRowsets` variant (per `DECISIONS 2026-05-15 — OSSYS
     adapter translation rules`, session-20 amendment); the
     class resolves *all members together* when the variant
     implements.

     Currently-known members:

       - **SsKey at every level** (session 18) — stripped at JSON
         aggregation; rowsets carry it.
       - **`EspaceKind`** (session 20) — encodes IS-vs-Direct;
         stripped at JSON aggregation; rowsets carry it.
       - **`isSystemEntity`** (observed during session-20 trace;
         not yet exercised by a fixture) — entity-level system
         flag; stripped at JSON aggregation; rowsets carry it.

     Likely future members: per-table column structure that
     `FOR JSON PATH` collapses; check-constraint definitions;
     additional fields the JSON projections happen not to
     include.

  2. **V2-boundary-discipline class.** The information **is
     visible to V2 through the current input**; the translation
     question is V2's own architectural choice about what to do
     with it. Resolution: **V2's own boundary discipline** —
     filter at adapter, carry through the IR, refine the IR with
     a new axis, etc. The choice is bounded by what V2's
     architecture today supports vs what consumer demand would
     need; the smallest honest-now choice is documented; the
     bound resolves on a named consumer-demand trigger.

     Currently-known members:

       - **Inactive-records boundary** (session 21) — V1 carries
         IsActive flags through to JSON; V2 has the choice
         between filter-at-adapter (chosen) and carry-through
         (deferred to consumer demand).

     Likely future members: any V1 field that V2 receives but
     V2's IR has no axis for (e.g., trigger metadata when a
     fixture surfaces it; computed-column definitions; field-
     level descriptions / `meta` strings).

**Why naming the classes matters operationally.**

The two classes have **different resolution paths** and
**different coupling characteristics**:

  - Lossiness-class findings **compose** through one resolution
    (`SnapshotRowsets` implementation absorbs all members
    together). The agent who opens that chapter inherits a
    class to resolve, not a list of bugs to fix.
  - Boundary-discipline findings **don't compose** the same
    way. Each member's resolution depends on its specific
    consumer demand and its specific IR-refinement implications;
    they are individually negotiated. A future
    `Modality.Inactive` variant doesn't automatically extend to
    cover triggers, computed columns, etc.

**The trace-before-fixture pattern classifies findings into one
or the other before implementation begins.** Session 20's trace
of `EspaceKind` placed it in the lossiness class (not in the
JSON; in the rowsets); session 21's trace of `Is_Active`/
`isActive` placed it in the boundary-discipline class (carried
through to JSON; V2 has the choice). Future slices apply the
same trace-before-fixture admire-mode discipline; the
classification informs the resolution shape.

**This sub-section refines, not replaces, session 20's
composability finding.** The lossiness class is one half; the
boundary-discipline class is the other half. Together they
form the chapter's accumulated structural picture of V1↔V2
translation.

**No code change.** Documentation hygiene only. Future
findings classify into one of the two classes (or surface a
third if neither fits, which would itself be a substantive
finding worth marking explicitly).

#### 2026-05-19 (session 22 amendment) — index-bearing fixture surfaces five index translation rules

Session 22's index-bearing fixture surfaced five translation
rules under empirical pressure (rules 19–23). The fixture
exercised three V2-IR-relevant index shapes (PK; unique non-PK;
composite non-unique with included columns) within a single
entity.

**Trace before fixture (admire-mode at slice level):** V1 carries
the indexes[] array through to JSON via the
`outsystems_metadata_rowsets.sql` aggregations (`#AllIdx`,
`#IdxColsMapped`, `#IdxColsJson`, `#IdxJson`). The JSON shape
includes name, isPrimary, kind, isUnique, isPlatformAuto,
storage/perf attributes (isDisabled / isPadded / fill_factor /
ignoreDupKey / etc.), structural fields (filterDefinition,
dataSpace, partitionColumns, dataCompression), and a columns
array with attribute / physicalColumn / ordinal / isIncluded /
direction per column. **All visible to V2 through the JSON
path.**

**Classification:** V2-boundary-discipline class. V1 has the
information; V2's IR scope is what's being chosen. The
translation rules are V2's own architectural choices about
scope, not input-path-bound questions. Same shape as the
inactive-records resolution (session 21).

**The five new translation rules:**

| #  | V1 input shape | V2 output | Rationale |
|----|---|---|---|
| 19 | Index `name` + parent entity + module | `Index.SsKey = OS_IDX_<modName>_<entName>_<indexName>` | Index SsKey synthesis. V1's IndexName is unique per entity (per the SQL extraction's `#AllIdx` clustered key). The synthesis convention extends the existing module/kind/attribute/reference pattern. |
| 20 | `index.isUnique` (boolean) | `Index.IsUnique = isUnique` | Direct mapping. |
| 21 | `index.isPrimary` (boolean) | `Index.IsPrimaryKey = isPrimary` | Direct mapping. V2 distinguishes IsPrimaryKey from IsUnique at the structural level (V1 treats PK as a unique index, but V2 separates the concerns per the Index DU's design notes in `Catalog.fs:144-146`). |
| 22 | `index.columns[].attribute` (string, attribute name within parent entity) | `Index.Columns = [SsKey list]` (resolved via `attributeSsKey moduleName entityName attribute`); sorted by `columns[].ordinal`; `columns[].isIncluded=true` entries dropped at the boundary | Same-entity attribute resolution. V1's `attribute` field names the attribute by string within the parent entity; V2 resolves to the synthesized SsKey. The included-columns drop is the canonical V2 boundary choice (per the OSSYS ADMIRE entry's "what V2 will explicitly NOT carry forward" section); V2's Columns carries only key columns. The ordinal sort preserves key-column order. |
| 23 | Index records have no `isActive` field on the index itself | All indexes are carried through; no filter | V1's index metadata is at storage-object level (sys.indexes); there's no logical activity flag on indexes. The session-21 inactive-records filter does NOT extend to indexes. If a future fixture surfaces inactive-index handling (e.g., V2 grows a per-index activity flag for some emitter), the rule extends under empirical pressure. |

**What this commit explicitly does NOT carry forward (FK
extensions for indexes):**

  - `index.kind` — V1 string field ("Index" / "PrimaryKey" /
    "UniqueIndex" etc.). Redundant with V2's IsUnique +
    IsPrimaryKey flags; V1's `kind` field encodes the same
    distinctions structurally.
  - `index.isPlatformAuto` — V1 marker for OSIDX_-prefixed
    platform-generated indexes. V2 has no auto-generated marker
    today. If a future emitter needs to skip platform-auto
    indexes (e.g., to avoid scripting OutSystems-internal
    indexes the platform regenerates), the rule extends.
  - **Storage/performance attributes** — `isDisabled`,
    `isPadded`, `fill_factor`, `ignoreDupKey`, `allowRowLocks`,
    `allowPageLocks`, `noRecompute`. V2's Index has no axis for
    these. They're DDL-emission concerns, not catalog structure;
    if a future emitter wants WITH-clause scripting, the rule
    extends.
  - `index.filterDefinition` — V1 carries SQL Server filtered-
    index definitions. V2 has no filtered-index axis. Defer
    until a fixture surfaces a filtered index that matters to
    the V2 IR.
  - `index.dataSpace`, `index.partitionColumns`,
    `index.dataCompression` — Storage placement metadata; V2 has
    no axis. Same disposition as filter.
  - `columns[].direction` — Per-column ASC/DESC ordering. V2's
    Index.Columns is a positional SsKey list; no per-column
    direction axis. If a future emitter wants direction-aware
    DDL (e.g., descending PK for time-series tables), the rule
    extends.
  - `columns[].physicalColumn` — V1 redundancy; V2 derives
    physical name from the attribute's ColumnRealization rather
    than from the index column entry. The redundancy in V1 was
    likely for cross-validation; V2 doesn't need it because
    V2's IR resolves through SsKey identity.

**Updated chapter status:**

  - Sessions 18: rules 1–11 (minimal slice — module / kind /
    attribute structure)
  - Session 19: rules 12–16 (reference-bearing slice — FK
    SsKey / deleteRule / cross-attribute)
  - Session 20: rule 17 (Origin three-way placeholder under
    JSON-path bound)
  - Session 21: rule 18 (inactive-records boundary)
  - Session 22: rules 19–23 (index translation — five rules)

**Twenty-three translation rules total** in the running list
across five substantive slices. The chapter has now exercised
all four V2 Kind sub-shapes (Attributes; References; Indexes;
Modality) plus the boundary disciplines (Origin; inactive-
records). Two substantive slices likely remain plausible:
static-entity (exercises Modality.Static populations end-to-end,
couples with Projection.Adapters.Sql/Static.fs); cross-module
FK (refines rule 16's same-module assumption).

**Class summary** (per the session-22 two-classes amendment):

  - Lossiness class: SsKey (rule 1-3 synthesis vs the bound);
    EspaceKind (rule 17's bound). Two members exercised; both
    resolve through SnapshotRowsets.
  - Boundary-discipline class: inactive-records (rule 18);
    index translation choices (rules 19–23). Multiple members
    exercised; each member's resolution is independent.

The class distinction is now empirically confirmed across two
members per class. Future findings classify into one or the
other before implementation begins; the resolution shape
follows from the classification.

#### 2026-05-20 (session 24 amendment) — static-entity fixture surfaces two translation rules and one implicit-coverage finding

Session 24's static-entity fixture is the chapter's sixth
substantive slice and the last substantive slice in chapter 2.
The fixture exercises the V2 `Modality = [Static []]` translation
that has shipped without explicit fixture coverage since session
18.

**Trace before fixture (admire-mode at slice level):** V1's SQL
extraction at `src/AdvancedSql/outsystems_metadata_rowsets.sql:929`
emits `CAST(CASE WHEN en.DataKind = 'staticEntity' THEN 1 ELSE 0
END AS bit) AS [isStatic]`. V1's JSON projection at
`src/Osm.Pipeline/SqlExtraction/SnapshotJsonBuilder.cs:207` writes
`writer.WriteBoolean("isStatic", string.Equals(entity.DataKind,
"staticEntity", ...))`. The `isStatic` boolean is faithfully
carried through the JSON path. **The static-entity *populations*
flow through a separate V1 extraction pipeline** — population data
arrives at V2 via `Projection.Adapters.Sql/Static.fs`'s
`attachStaticPopulations`, consuming a separately-emitted
`static-entities.*.json` rather than the `osm_model.json` snapshot
the OSSYS adapter consumes.

**Classification:** V2-boundary-discipline class. V1 has the
information; V2's IR scope is split between the OSSYS adapter
(modality flag only) and the Static SQL adapter (populations).
This split is itself a V2 design choice that mirrors V1's own
extraction split. Same shape as session 21's inactive-records
resolution.

**The two new translation rules:**

| #  | V1 input shape | V2 output | Rationale |
|----|---|---|---|
| 24 | `entity.isStatic: true` | `Kind.Modality = [Static []]` (empty population) | Static-entity modality flag. Empty population is intentional — the OSSYS adapter's responsibility ends at the modality marker; populations flow through `Projection.Adapters.Sql/Static.attachStaticPopulations`, which consumes a separately-emitted `static-entities.*.json` and composes onto a Catalog already carrying the `[Static []]` markers. The split mirrors V1's own extraction split. |
| 25 | `entity.isStatic: false` | `Kind.Modality = []` (no Static mark) | Direct mapping. The `Modality` list is empty for non-static entities; a kind without `Static` in its modality list is treated as a dynamic entity by all downstream consumers. |

**What this commit explicitly does NOT carry forward:**

  - **Static-entity population data inside the OSSYS adapter.** V1's
    `osm_model.json` does not carry population data; population data
    lives in the separate `static-entities.*.json` extraction. The
    OSSYS adapter must produce `[Static []]` (empty population)
    rather than attempt to derive populations from the model JSON.
    The downstream `Projection.Adapters.Sql/Static.fs` is
    responsible for filling populations against the marker. No
    re-open trigger — the split is V2's design intent.
  - **`Modality.Inactive`, `Modality.SoftDeletable`, `Modality.TenantScoped`** —
    V2's `Catalog.fs:52-55` ModalityMark DU has variants beyond
    `Static`. None are in V1's JSON snapshot today (the V1 model
    has no analogous markers). If future fixtures surface these
    via different V1 fields (e.g., a soft-delete column convention),
    rules extend under empirical pressure.

**The implicit-coverage finding.** The static-entity translation
implementation has shipped at `CatalogReader.fs:578` since session
18 (`if isStatic then [ Static [] ] else []`) — written
defensively while building the minimal-slice infrastructure. Five
prior fixtures (sessions 18–22) all carried `isStatic: false`; no
fixture exercised the `true` branch until this slice. The
implementation worked; the contract was uncovered.

This is a small instance of a real discipline question: **when
implementation ships ahead of fixture coverage, the contract is
asserted by the type system and by inspection rather than by
test.** The session-22 chapter-mid-audit dispatch could in
principle have caught this — "scan the OSSYS adapter for
implementations whose code paths no fixture exercises" — but the
audit's framing (cross-document consistency, Active deferrals
scan after the session 24 amendment) does not include
contract-vs-implementation walking at the adapter level. The
session 14 audit-discipline refinement (`DECISIONS 2026-05-13`)
codified contract-vs-implementation cross-reference for
pass-and-strategy work; the same shape applies at adapter level
but with different criteria — implementation paths whose input
condition has not been fixture-exercised.

The slice's resolution recovers the contract gap by adding the
fixture; codifying the discipline lesson here lets future
chapter-close audits include "are there input-conditional
adapter paths uncovered by fixture?" as a dimension. **The
discipline does not yet need its own DECISIONS row; it surfaces
here to be tested at chapter close — if subagent #3 (the OSSYS
chapter completeness audit) flags additional uncovered paths,
the discipline earns its row.**

**Updated chapter status:**

  - Session 18: rules 1–11 (minimal slice — module / kind /
    attribute structure)
  - Session 19: rules 12–16 (reference-bearing slice — FK
    SsKey / deleteRule / cross-attribute)
  - Session 20: rule 17 (Origin three-way placeholder under
    JSON-path bound)
  - Session 21: rule 18 (inactive-records boundary)
  - Session 22: rules 19–23 (index translation — five rules)
  - Session 24: rules 24–25 (static-entity modality flag — two rules)

**Twenty-five translation rules total** in the running list
across six substantive slices. The chapter has now exercised
all four V2 Kind sub-shapes (Attributes; References; Indexes;
Modality) plus the boundary disciplines (Origin; inactive-
records; static-entity split). The cross-module FK slice
remains plausibly substantive but defers to fresh context per
the chapter-close handoff (see CHAPTER_2_CLOSE.md and the
session-23 runway plan).

**Class summary** (per the session-22 two-classes amendment):

  - Lossiness class: SsKey (rules 1-3 synthesis vs the bound);
    EspaceKind (rule 17's bound). Two members exercised; both
    resolve through SnapshotRowsets.
  - Boundary-discipline class: inactive-records (rule 18); index
    translation choices (rules 19–23); static-entity split
    (rules 24–25). Three members exercised; each member's
    resolution is independent.

Three boundary-discipline members empirically confirms the class
as the more populated of the two — the lossiness class has a
single-source resolution (SnapshotRowsets); the boundary-
discipline class accumulates multiple members, each with
independent resolution shapes.

## 2026-05-19 — Chapter-mid-audit as a routine practice

**Status:** decided (operating discipline; pairs with the chapter-close ritual)
**Context:** Session 22 dispatched a cross-document consistency audit
subagent during a substantive-work session — not at chapter close.
The audit surfaced 6 CRITICAL, 21 MINOR, and 7 OPEN findings, of which
the cross-cutting observation (most CRITICAL findings tracked to a
single phenomenon — OSSYS chapter sessions 18–22 faithfully recorded
as DECISIONS amendments but not propagated back to index documents)
identified a real gap in the chapter-close ritual's coverage.

The chapter-close ritual (`DECISIONS 2026-05-14`) handles propagation
at chapter boundaries but not at chapter-mid-flight. Multi-session
chapters that run for many sessions accumulate propagation debt
between substantive work and downstream documentation surfaces;
the ritual doesn't catch it until chapter close, at which point the
debt has compounded.

**The audit-as-routine practice fills this gap.** Session 22's
dispatch demonstrated its value: drift surfaced now is cheaper to
address than drift surfaced at close. The 6 CRITICAL findings
became session 23's documentation-hygiene work; the 21 MINOR
findings landed in `CHAPTER_2_CLOSE.md`'s scaffold for chapter close
to address.

**Decision:** **Chapter-mid-audit is a routine practice alongside
the chapter-close ritual.** Multi-session chapters that run for
more than ~5 sessions should dispatch a cross-document consistency
audit subagent at intervals during the chapter — typically when
substantive work has accumulated enough to make propagation drift
plausible.

**The dispatch pattern:**

  1. **Brief the subagent.** Self-contained prompt; specify the
     document pairs to walk; specify the categorization scheme
     (CRITICAL / MINOR / OPEN); specify "surface, not act"; specify
     the working directory and word budget. The session-22 dispatch
     is the worked template.

  2. **Run in parallel** with substantive work. The audit doesn't
     need real-time review; the session that dispatches it can
     continue substantive work while the subagent runs in the
     background. The agent's findings arrive via completion
     notification; integration into the session's summary is a
     synthesis step at session close.

  3. **Categorize findings.** CRITICAL gets immediate-fix-or-explain
     in the next hygiene work (typically the next session, unless
     blocking). MINOR rolls into the chapter-close synthesis via
     `CHAPTER_2_CLOSE.md` (or its successor's scaffold). OPEN
     warrants discussion; can land in hygiene work if architectural
     answers emerge naturally, or defer to chapter close.

  4. **Don't act in the dispatch session.** The audit's job is
     surface, not act. Findings get reviewed in the next hygiene
     session. This separation keeps the substantive-work session
     focused.

**Cadence relative to chapter-close ritual:**

  | Practice | When | Job |
  |---|---|---|
  | **Chapter-mid-audit** (this entry) | At intervals during multi-session chapters | Surface mid-flight propagation drift; categorize findings; land them in the in-flight close scaffold |
  | **Chapter-close ritual** (`DECISIONS 2026-05-14`) | At chapter close | Execute seven load-bearing items; produce the chapter's CHAPTER_N_CLOSE synthesis |

The two are **complementary**, not redundant. Mid-audit catches drift
before it compounds; close ritual integrates the accumulated material
into the formal close synthesis.

**The session-22 worked example** sets the dispatch pattern. The
subagent walked seven document pairs (DECISIONS↔AXIOMS,
DECISIONS↔ADMIRE, DECISIONS↔CHAPTER_CLOSE, ADMIRE↔source code,
CLAUDE.md↔DECISIONS, CLAUDE.md↔source code, README↔all of the above);
findings categorized by criticality; output budgeted at 1500–2200
words; "surface, not act" enforced. Future chapter-mid-audits follow
the same shape with adjusted document pairs as new surfaces accumulate.

**On the subagent dispatch's cost.** The session-22 dispatch hit a
budget limit on its first attempt and required re-dispatch. The
practice carries this cost: subagent runs are stateful with respect
to the dispatching session's budget. When a re-dispatch is needed,
the second attempt can use the same prompt unchanged. Future agents
running this practice should expect occasional dispatch failures
and treat them as routine rather than as evidence the practice is
broken.

**Reasoning / consequences.** The chapter-close ritual addressed
chapter-boundary drift; chapter-mid-audit addresses chapter-mid-
flight drift. Together they form a complete propagation-correction
shape for multi-session chapters. The session-22 audit's value-prop
was demonstrably real — without it, the OSSYS ADMIRE entry would
have remained at "chapter-open scoping (session 17)" through chapter
close, the README would have remained materially stale, and the
chapter-2 close synthesis would have inherited substantial drift.
The practice pays for itself; codifying it makes future chapters
inherit the value.

**Forward-looking:** subsequent multi-session chapters dispatch
chapter-mid-audits at appropriate intervals (typically every 3–5
substantive sessions, or when a session's substantive work feels
like it's accumulated enough to warrant the check). Sessions 24
and 25 of chapter 2 will dispatch their own audits per the
session-23 runway plan.

#### 2026-05-20 (session 24 amendment) — Active deferrals scan is a required dispatch dimension

The session-23 chapter-mid-audit (subagent #2; the second audit
under this practice) surfaced 1 CRITICAL finding — the DacFx /
DacpacEmitter trigger had fired silently across sessions 18–22 —
plus a cross-cutting observation: **propagation gaps cluster into
two phenomena, only one of which the chapter-mid-audit dispatch
shape explicitly covered.**

Phenomenon A: pointer drift between substantive DECISIONS entries
and the index documents that should reflect them (CLAUDE.md
operating-disciplines table; CHAPTER_N_CLOSE.md scaffolds; README;
ADMIRE entry status). Session-22 audit subagent #1 surfaced this
class; session-23 hygiene addressed it.

Phenomenon B: trigger-fire drift in the Active deferrals index.
Substantive work satisfies a deferral's structural condition
without the agent shipping the work scanning the index for fired
triggers. Subagent #2 surfaced one CRITICAL instance (DacFx) and
named it as the more expensive class — pointer drift means
documents say slightly stale things; trigger drift means *actual
work that should have begun didn't begin*. The cost difference
is structural.

**The session-22 dispatch did not include "scan the Active
deferrals index for silent firings" as an explicit document pair.**
The audit walked seven document pairs (DECISIONS↔AXIOMS,
DECISIONS↔ADMIRE, etc.) but the Active deferrals index sits as a
section *within* DECISIONS, and "deferral status conditions vs.
substantive work shipped this chapter" was not a named dimension.
Subagent #2 found the DacFx fire only because its prompt was
explicit about the Active deferrals index as a dimension; without
that explicit framing, mid-audits would catch pointer drift but
miss the more expensive class.

**Decision: Active deferrals scan is a required dimension on every
chapter-mid-audit dispatch.** The dispatch pattern's four steps
(brief, run-in-parallel, categorize, don't-act) extend to a fifth
explicit dimension on the briefing:

  5. **Walk the Active deferrals index against the chapter's
     substantive work.** For each row, evaluate whether the
     trigger condition has been satisfied by work landed in the
     chapter (or earlier work the previous audits did not catch).
     Categorize fires as CRITICAL (the audit-during-validation
     discipline expects a cash-out before further substantive
     work). The dispatch prompt explicitly names this as a
     required dimension, not an emergent one.

This is the discipline operating on itself: the audit just ran,
surfaced the structural gap in its own dispatch shape, and the
discipline absorbs the refinement before its next operation.

**Worked example for the next dispatch.** The session-25 chapter
close (planned dispatch of subagents #4 and #5) and any session-24
subagent #3 dispatch include the Active deferrals scan as an
explicit dimension. The session-22 dispatch's seven document pairs
become eight, with the eighth being "Active deferrals index ↔
substantive work shipped this chapter."

**Why the refinement lands now and not at chapter close.** The
chapter-mid-audit codification is fresh (session 23). Refining it
while the next dispatch is still ahead keeps the discipline
coherent before it operates again — the session-23 codification
establishes the practice; the session-24 amendment closes the
structural gap the practice's own first operation surfaced. Same
shape as the strategy-layer codification's refinements landing
during validation rather than after (see
`DECISIONS 2026-05-09 — Audits surface things not on the agenda`).

## 2026-05-19 — Trace-before-fixture pattern at slice level (codified at N=3)

**Status:** decided (operating discipline; codifies the pattern at N=3 with consistent shape)
**Context:** Sessions 20, 21, and 22 each dispatched a V1 metadata
trace before writing the slice's failing differential test. Three
instances; same shape; same value-add. The pattern is sufficiently
demonstrated to earn codification per the chapter's two-consumer
threshold for emergent disciplines.

The pattern's shape:

  1. **Trace V1's actual handling first** — read V1's SQL extraction
     script and JSON projection logic for the field/feature about to
     be tested. Identify what V1 carries through to JSON vs what V1
     strips at the projection layer.
  2. **Classify the finding** — into either the JSON-projection-
     lossiness class (information stripped at the JSON layer; V2
     bound by input path) or the V2-boundary-discipline class
     (information visible to V2; V2's IR scope is what's chosen).
     The two-classes distinction (`DECISIONS 2026-05-15 — OSSYS
     adapter translation rules`, session-22 documentation hygiene)
     names the two paths.
  3. **Then write the failing test** — fixture and expected V2
     Catalog hand-built in light of the classification. The
     classification informs the resolution shape: lossiness-class
     findings get bounded-by-input-path placeholders; boundary-
     discipline-class findings get IR-scope-choice rules.
  4. **Implement under empirical pressure** — minimum to make the
     test pass; document the won't-carry-forward extensions.
  5. **Record translation rules** — DECISIONS amendment captures
     the rules surfaced under empirical pressure plus the
     classification.

**Empirical record (the three instances):**

  | Session | Slice | Trace finding | Class |
  |---|---|---|---|
  | 20 | External-entity | `EspaceKind` encodes IS-vs-Direct at espace/rowset level; stripped at JSON projection | Lossiness |
  | 21 | Mixed-active | `IsActive` flags carried through to JSON at three levels (module / entity / attribute); V1 also has SQL pre-filter parameters | Boundary-discipline |
  | 22 | Index-bearing | `indexes[]` JSON has rich shape (storage attrs, structural fields, columns array); all visible to V2 | Boundary-discipline |

In each case, the trace identified the resolution shape **before**
implementation began. The session-20 trace was particularly
load-bearing — without it, the implementation would have plausibly
made the wrong assumption (synthesize Origin from `isExternal`
boolean alone, treating it as if V1's full information were
present). The trace surfaced that V1 had richer information at the
rowset level that the JSON projection had stripped — placing the
question in the lossiness class with a known canonical resolution
(`SnapshotRowsets`). The implementation could then land a
placeholder rule with bound documented, rather than overclaim
distinction.

**Decision:** **The trace-before-fixture pattern is codified.** When
the OSSYS adapter chapter (or any future chapter doing V1↔V2
translation work) writes a new slice, the trace happens first; the
classification informs the test shape; the implementation lands
under empirical pressure with the won't-carry-forward list growing
to absorb V1 fields the V2 IR doesn't model.

**Relation to admire-mode (`HANDOFF.md` "What's load-bearing").**
The pattern is **slice-level admire-mode**, distinct from
chapter-level admire-mode. Chapter-level admire happens once at
chapter open (per the OSSYS ADMIRE chapter scope from session 17);
slice-level admire happens per fixture as new V1 fields surface.
Both honor the broader admire-mode discipline (read V1 first,
identify what V2 will carry forward, name what V2 won't carry
forward); the slice-level form applies the same shape at a smaller
scope.

The two operate at different cadences:

  | Admire-mode level | When | Output |
  |---|---|---|
  | **Chapter level** | At chapter open | ADMIRE chapter scope document; carry-forward set; won't-carry-forward set; structural-difference list |
  | **Slice level** (this entry) | Per fixture during the chapter | Class classification (lossiness vs boundary-discipline); rules informed by classification; won't-carry-forward extensions |

The hierarchy is real: slice-level admire surfaces specific findings
that the chapter-level admire's structural-difference list named at
the right level of abstraction. Session 21's `Is_Active` trace
confirmed the chapter-level note about activity flags; session 20's
`EspaceKind` trace surfaced a specific case the chapter-level
"V1↔V2 vocabulary mapping" hadn't fully traced.

**Why codify now (N=3 with consistent shape).** The pattern's
two-consumer threshold cleared at session 21 (instances 20, 21);
session 22 was the third instance with the same shape. Per the
emergent-primitives discipline (`DECISIONS 2026-05-13`), the
threshold is N=2 for codification; this codification at N=3 is
slightly conservative but pairs naturally with the broader
chapter-mid-audit codification landing in this session.

**Forward signals.** Subsequent slices in the OSSYS chapter (and
any future V1↔V2 chapters) operate the pattern explicitly. Static-
entity slice (session 24) dispatches a trace of V1's static
populations handling first; cross-module FK slice (deferred to
fresh context) dispatches a trace of V1's cross-module FK encoding
first. Each trace classifies before the fixture lands.

**Reasoning / consequences.** The trace-before-fixture pattern is
a small but real discipline that has paid rent across three
substantive slices. Codifying it makes the pattern explicit to
future agents who would otherwise either re-derive it (and likely
get it right; the pattern is structurally natural) or skip it (and
risk wrong-class assumptions like the speculative-Origin case from
session 18). The codification is a checked-implicit-pattern;
operating it is a small per-slice discipline that compounds into
correct class classification across the chapter's running rules.

## 2026-05-21 — Chapter 2 close: alternative-IR-surface class (third translation-finding class)

**Status:** decided (operating discipline; chapter-2 close
codification — completes the V1↔V2 translation-finding typology)
**Context:** Session 25's chapter-close audit (subagent #3 — OSSYS
chapter completeness) surfaced a third class of V1↔V2 translation
finding that the chapter has been operating implicitly without
naming. The two-class typology (`DECISIONS 2026-05-19 — naming the
two classes of resolution patterns explicitly`, session 22
documentation hygiene) covered:

  1. **JSON-projection-lossiness class** — V2 cannot see X
     because V1's JSON projection strips it; resolved via input-
     path expansion (`SnapshotRowsets`).
  2. **V2-boundary-discipline class** — V2 sees X but has no
     axis; resolved via V2's own architectural choice (filter,
     carry through, refine IR with new variant).

Subagent #3's `onDisk` finding (the eleven-field per-attribute
envelope V1 emits and V2 silently drops) does not fit either
class cleanly: V2 *does* see `onDisk` (it's in the JSON), so it's
not lossiness; V2's IR has no axis for `onDisk` *as such*, so it
looks boundary-discipline; but V2 *does* have a parallel
structure (Profile) that is the natural home for the same
information. The "no axis" framing of the boundary-discipline
class doesn't capture the third option — *route to alternative
surface*.

**Decision: the typology is three classes, not two.** The third
class is named explicitly here.

  3. **Alternative-IR-surface class.** V2 sees X through the
     current input; V2's primary IR (Catalog) has no axis for it;
     **but V2 has a parallel structure (Profile, Diagnostics, or
     a future surface) that is the natural home for the same
     information class.** Resolution: **route to the alternative
     surface**; possibly identify the alternative as canonical,
     making V1 input redundant (when a parallel V2 chain produces
     the same evidence at a different temporal point).

**Currently-known members:**

  - **V1 `deleteRuleCode: "Ignore"` → V2 `OnDelete: NoAction` +
    Diagnostics emission** (session 19; rule 13). V1's "Ignore"
    encodes the audit-trail concern "we tolerated a missing delete-
    rule." V2's `ReferenceAction` DU has no `Ignore` variant; the
    `NoAction` collapse is structural. The audit-trail concern
    routes to V2's Diagnostics writer (the alternative surface),
    where structured-rationale emission preserves V1's audit
    intent without polluting Catalog's structural typing.
  - **V1 `attributes[].onDisk` envelope → silently dropped by
    OSSYS adapter; routes to V2's Profile (or read-side-adapter
    output) when read-side adapter chapter materializes** (session
    25 commit 1; ADMIRE entry's won't-carry-forward addition).
    The eleven physical-reality fields (`sqlType`, `maxLength`,
    `collation`, `isIdentity`, `isComputed`, `computedDefinition`,
    `defaultDefinition`, `defaultConstraint`, `checkConstraints`,
    plus more) are V1's snapshot of physical reality at extraction
    time; V2's read-side adapter is V2's read at deployment-
    validation time. Parallel sources of the same information
    class. The read-side adapter is canonical for the canary use
    case (it queries deployed reality directly); OSSYS `onDisk`
    is *redundant* until the read-side chapter discovers a drift-
    detection use case requiring both.

**Two members empirically confirms the class** at the same N=2
threshold the boundary-discipline class earned its naming at
(session 22). The chapter has now produced the complete typology
of V1↔V2 translation findings.

**The three classes operationally:**

| Class | Symptom | Resolution shape | Composability |
|---|---|---|---|
| **JSON-projection-lossiness** | V2 can't see X | Input-path expansion (e.g., `SnapshotRowsets`) | All members compose through one resolution |
| **V2-boundary-discipline** | V2 sees X; V2 IR has no axis; choose | Filter-at-adapter, carry-through, or IR-refinement-under-demand | Members don't compose; each negotiates independently |
| **Alternative-IR-surface** | V2 sees X; primary IR has no axis; parallel V2 surface is the natural home | Route to alternative surface (Profile, Diagnostics, future surface); possibly identify alternative as canonical | Each member routes independently; the routing target may make V1 input redundant |

**The trace-before-fixture pattern extends to three-class
classification.** Future slices apply the same trace-before-
fixture discipline; the classification informs the resolution
shape. A finding that initially looks boundary-discipline ("V2
has no axis") gets re-evaluated against the alternative-IR-
surface question ("does V2 have a parallel surface that's the
natural home?") before resolution lands. The session-25 onDisk
finding is the worked example: initial framing was "won't-carry
under boundary-discipline shape"; trace and re-evaluation
surfaced the read-side adapter as the alternative surface;
resolution shape became "won't-carry-with-route-to-alternative-
when-that-chapter-lands."

**Why naming this class matters operationally.**

The alternative-IR-surface class has different *coupling
characteristics* from the other two:

  - **Couples chapters across V2's surfaces.** `onDisk`'s
    resolution depends on the read-side adapter chapter; the
    DeleteRule-Ignore resolution depended on the Diagnostics
    writer chapter. The class is structurally cross-chapter,
    where the other two classes are structurally within-chapter
    (lossiness resolves through one input-path-expansion chapter;
    boundary-discipline resolves through one IR-refinement-or-
    boundary-choice chapter).
  - **Re-evaluates V1 input as potentially redundant.** When the
    alternative V2 surface is canonical (read-side adapter for
    `onDisk`), the V1 input is not just "not-carried" but
    "redundant-with-canonical-V2-source." This is a different
    disposition from "V2 has no axis"; it carries a different
    re-open trigger ("does the alternative surface need
    cross-validation against V1's source?" rather than "does
    consumer demand surface a need for the IR axis?").

**Forward-looking.** Subsequent V1↔V2 translation chapters (and
chapters bridging V2 to anything else, which the codebase will
accumulate as DACPAC, OData, etc. adapters land per A18 and A21)
inherit the three-class framework. The class typology is now
complete enough to operate as a checked surface during chapter-
open scoping, slice-level trace-before-fixture, and chapter-
close ritual. The chapter-2 work has produced the typology; the
chapter-3+ work operates it.

**Reasoning / consequences.** Naming the third class makes the
chapter's complete typology explicit. Future chapters dealing
with V1↔V2 translation (or V2 to anything else) inherit the
three-class framework: input lossiness, boundary discipline,
alternative-surface routing. The trace-before-fixture pattern
extends to three-class classification at N=3 (already at N=3
on classification practice from session 22; the class extension
landing here is consistent with the existing operating-
discipline shape). The chapter-2 close has produced one of its
most consequential intellectual artifacts: a complete typology
for V1↔V2 translation findings that future chapters operate.

## 2026-05-21 — Chapter 2 close: OPEN-question resolutions

**Status:** decided (chapter-2 close — resolves the OPEN questions
from the chapter-mid-audits at sessions 22, 23, and 25)
**Context:** The chapter-mid-audits (subagents #1, #2, #3)
surfaced OPEN questions throughout the chapter. Per the
chapter-mid-audit discipline, OPEN questions warrant explicit
decision-or-defer at chapter close. This entry resolves them
together so the chapter-3 handoff inherits a clean slate of
decisions rather than a backlog.

### From subagent #2 (session 23)

**Subagent #2 O1 — Adapter-boundary deferrals scope in the
Active deferrals index.** Sessions 18–22 produced 10+
adapter-translation deferrals with explicit re-open triggers
(auto-number axis; cross-module FK; `IsExternalEntity` Origin
three-way; `Modality.Inactive` carry-through; filter-definition
indexes; `physical_isPresentButInactive`; etc.). The index's
scope statement admits architectural IR refinements but is
silent on adapter-boundary deferrals.

**Resolution: yes, expand the index's scope to include adapter-
boundary deferrals that have explicit re-open triggers and
architectural significance.** Cross-catalog FK is already in the
index; cross-module FK is structurally adjacent. Subagent #2
flagged `SnapshotRowsets` and `LiveOssysConnection` as deferrals
with explicit re-open triggers; both are now in the index
(session 25 commit 4). The OSSYS-translation-rule deferrals
that don't rise to architectural IR refinement (e.g., specific
field translation rules that defer until a fixture surfaces
them — like `physical_isPresentButInactive`) stay in the
DECISIONS amendments to the OSSYS translation-rules entry where
they're already discoverable from rule context. The
distinction: architectural IR refinements (DU expansions; new
adapter variants; new IR axes) belong in the index;
field-specific rules belong in the rules amendments.

The index's scope statement is updated implicitly by the new
rows added at session 25 commit 4; no scope-statement rewrite
needed because the new rows demonstrate the broader scope
empirically.

**Subagent #2 O2 — Chapter-level scope deferrals discipline-
table entry.** The OSSYS strategic-frame entry
(`DECISIONS.md:4032`) codifies a chapter-open pattern that
other future chapters (Pipeline canary; SnapshotRowsets) are
explicitly named as inheriting from. Should it earn a
discipline-table entry?

**Resolution: yes.** The pattern is structurally a discipline —
"do strategic-frame axis naming at chapter open" — and it is
demonstrably reusable (the framework extension amendment
explicitly applies to multi-session chapters generally). A row
gets added at this commit (see CLAUDE.md update below).

**Subagent #2 O3 — Trace-before-fixture pointer suffix drift.**
Row at `CLAUDE.md` cites `DECISIONS 2026-05-19 — Trace-before-
fixture pattern at slice level (session 23)`. The DECISIONS
entry header reads `(codified at N=3)`. Suffix-convention
inconsistency.

**Resolution: small fix at session 25 commit 2 (row already
updated to `(session 23; codified at N=3)`).** No structural
issue; the inconsistency was readable, the cleanup is just
hygiene.

### From subagent #3 (session 25)

**Subagent #3 O1 — Rule 21 ambiguity for `isPrimary: true,
isUnique: false`.** V1's domain disallows the combination at
SQL level, but V2's `Index` DU at `Catalog.fs:159-164` makes
both bools independent. The adapter does not validate the
combination; a malformed input would produce a structurally-
incoherent V2 `Index`.

**Resolution: stay literal at the adapter; document the bound
explicitly here.** Rule 21 says `Index.IsPrimaryKey = isPrimary`
(direct); rule 20 says `Index.IsUnique = isUnique` (direct).
Both are coordinate maps. The combination `isPrimary: true,
isUnique: false` is not constructible from V1 SQL (V1's domain
enforces uniqueness on PK structurally); if it appeared in V1's
JSON output, it would be an upstream V1 bug rather than a V2
adapter responsibility. V2's adapter-boundary discipline is
"reflect what V1 says"; **upstream-coherence enforcement is
not the adapter's responsibility**. Future fixtures may force
re-evaluation if a V1 bug surfaces with this shape; until then,
the literal mapping is correct. The smallest honest-now choice
matches the existing rule wording.

**Subagent #3 O2 — `module.isActive: false` adapter behavior.**
The adapter's behavior on `module.isActive: false` is silent
(no filter, no error). Rule 18 deferred the question explicitly.

**Resolution: filter at adapter, consistent with rule 18's
inactive-records handling.** When V1 emits `module.isActive:
false`, the OSSYS adapter drops the entire module (and all its
entities). Same disposition as inactive entities and inactive
attributes. The won't-carry-forward list (session 25 commit 6)
names this; rule 18 extends to the module level. **Status:
codified here; implementation lands in chapter 3 if a fixture
surfaces a `module.isActive: false` case** — until then, the
rule is documented but no V1 fixture exercises the path. The
chapter-mid-audit will catch if a future fixture surfaces the
case before the implementation extends.

**Subagent #3 O3 — `attributes[].onDisk` Profile routing.**
Should the OSSYS adapter route `onDisk` to V2's Profile rather
than silently drop?

**Resolution: addressed at session 25 commit 1 + commit 2.** The
disposition is "silently dropped at OSSYS; routes to read-side
adapter when that chapter materializes." If the read-side
adapter chapter discovers a drift-detection use case requiring
both V1's recorded reality and deployed reality as separate
sources, the OSSYS adapter routes onDisk to Profile alongside
the read-side adapter's emission. **Re-open trigger sits in the
read-side adapter chapter, not in OSSYS chapter 2's close.**

**Subagent #3 O4 — Won't-carry-forward list as Active-deferrals-
shaped surface.** The won't-carry-forward list grew over six
slices and at chapter-2 close still had gaps. Should it have
the same audit discipline as the Active deferrals index?

**Resolution: defer with rationale.** The V1-input-envelope walk
discipline added as chapter-close ritual item 8 (session 25
commit 3) addresses the gap structurally — the won't-carry-
forward list is audited at chapter close against the V1
envelope projection code field-by-field. The Active deferrals
index pattern (re-open triggers; structural conditions) does
not map cleanly onto won't-carry-forward members (which are
choices about what V2's IR doesn't carry, not deferrals
awaiting structural conditions). The two surfaces are
complementary, not duplicative; the V1-envelope-walk discipline
is the correct integration point. If the won't-carry-forward
list grows beyond what the chapter-close walk catches, the
question re-opens.

**Subagent #3 O5 — Cross-module FK chapter-3 handoff.** The
chapter-2 close handoff names cross-module FK as "highest-
priority deferred slice" but the scaffold doesn't carry V1's
`relationships[]` detail.

**Resolution: defer to handoff document.** Session 25 commit 10
(handoff document) carries the V1 `relationships[]` shape and
the rule-14-may-not-hold-cross-module warning explicitly.

**Subagent #3 O6 — `SnapshotRowsets` handoff lossiness-class
framing.** The chapter-2 work has earned the canonical-
resolution-as-class framing; the handoff should preserve it.

**Resolution: defer to handoff document.** Session 25 commit 10
preserves the framing; subagent #5 pre-scope (session 25 commit
9) extends it.

**Subagent #3 O7 — DacpacEmitter as canary's second
deliverable.** Subagent #2's CRITICAL was cashed out at session
24 commit 1. The chapter-3 handoff should make the sequencing
explicit: "DacpacEmitter is the second deliverable in canary,
after the read-side adapter integration."

**Resolution: confirmed in handoff document.** Session 25
commit 10 carries the explicit sequencing.

### Why these resolutions land at chapter close together

The chapter-mid-audit discipline (`DECISIONS 2026-05-19`, session
23 + session 24 amendment) names "OPEN warrants discussion;
defer to chapter close" as one of three category dispositions.
Chapter close is the right venue: the audit dispatch session
focused on substantive work; the chapter close synthesizes.
Bundling them in one entry preserves the chapter-mid-audit's
discipline ("don't act in dispatch session; review at next
hygiene") while producing concrete decisions before chapter 3
opens.

**Reasoning / consequences.** Chapter 3 opens with a clean
backlog: every OPEN question from the three chapter-mid-audits
has either landed as a decision (codified, implementation
deferred to a chapter that will do the work) or as an explicit
defer with rationale (re-open trigger named, re-evaluation venue
named). The chapter-3 agent inherits decisions, not unfinished
arguments.

## 2026-05-22 — Stage 0 foundation phase ships as one coherent unit before chapter 3.1 opens

**Status:** decided (Stage 0 commitment; per `STAGING.md`)
**Context:** `SPINE.md` rendered the V2 system as a category in the
technical sense — seven tessellating patterns (Π Emitter / Adapter /
Pass / Render / Compare / Property / Diff) over seven recurring
primitives (SsKey-keyed Map / Writer-monad accumulation / Ordered
linearization / Smart-constructor invariants / Origin tagging /
Erasure declaration / Closed DUs with structured rationale), with
six structural inferences (sheaf gluing / adjunction / Hom-set /
quotient / continuation / tessellation instance) and ten leverage
points. The chapter pre-scopes (3.1, 3.2, 3.3, 3.4, 3.5, 4.1.A,
4.1.B, 4.2, 4.3, 4.4) are concrete morphism constructions —
instantiations of the seven patterns with chapter-specific type
variables.

The foundation insight: if the patterns and primitives are codified
**first** — as F# types in `Projection.Core` rather than as
conventions implicit across eight pre-scope documents — every
chapter inherits the contracts at compile time. The chapter writes
the *body* of the pattern; the *signature* is fixed.

Without the foundation phase, every chapter re-derives the pattern
shape under chapter-local pressure. With it, the chapter writes
itself.

**Decision:** Per `STAGING.md` (revision authored 2026-05-08), Stage 0
ships as **one coherent unit** before chapter 3.1 opens. Stage 0 is
**not** chapter 3.1; Stage 0 is **everything that ships before chapter
3.1 opens**. The unit comprises twelve items, sequenced in four tiers:

**Tier 1 — documentation hygiene + governance burst (no code; ship in
parallel; first-session goal):**

1. **S0.F** — `AXIOMS.md` amendment scaffolding (placeholder headers
   with TBD bodies for each pending amendment; chapter agents fill at
   close).
2. **S0.G** — `DECISIONS.md` governance burst (this entry plus four
   companions: R6 split-brain rule; chapter 3 sequencing decision;
   CLAUDE.md reading-order update; T-30 / T-15 fallback ladder gates).
3. **S0.J** — Active deferrals scan + ADMIRE / AXIOMS / CLAUDE /
   HANDOFF currency checks (documentation hygiene).
4. **S0.L** — `VISION.md` + `BACKLOG.md` cross-references to SPINE /
   PLAYBOOK / STAGING (documentation; verified current as of `630e32c`).

**Tier 2 — type primitives keystone (depends on Tier 1; foundation
for all subsequent code):**

5. **S0.A** — Type primitives in `src/Projection.Core/Types.fs` (the
   seven tessellating-pattern signatures as F# type aliases; ~50 LOC).

**Tier 3 — structural commitment refactor (depends on Tier 2; the
largest single Stage 0 item):**

6. **S0.B** — `ArtifactByKind<'a>` + `SsKey` four-variant DU split +
   `CatalogDiff` per `CHAPTER_3_PRESCOPE_ARTIFACTBYKIND_REFACTOR.md`.
   Six slices, ~700 LOC source + ~520 LOC test, runs across 4-5
   sessions.

**Tier 4 — primitive support modules (depends on Tier 3; can ship in
parallel):**

7. **S0.C** — `Projection.Core/Render.fs` skeletons (per-target
   composition layer; `failwith` stubs filled per chapter; ~80 LOC).
8. **S0.D** — `tests/Projection.Tests/PropertyCombinators.fs` (`.&&.`,
   `.||.`, `negate`, `conditional`; ~50 LOC).
9. **S0.E** — `Projection.Core/Verification/Tolerance.fs` taxonomy
   (named flag list; permissive / strict defaults; ~30 LOC; every
   flag carries a doc citation to its V1 file:line).
10. **S0.H** — Configuration port (`config/default-tightening.json`
    + `Projection.Adapters.Sql/PolicyDefaults.fs`; ~80 LOC + ~50
    LOC config).
11. **S0.I** — Test support consolidation (`Projection.Tests.Support`
    new shared library lifting V1's `Osm.TestSupport` patterns;
    ~600 LOC test).
12. **S0.K** — Multi-environment Profile/Policy generator skeleton
    (extends `tests/Projection.Tests/CatalogGen.fs`; ~30 LOC).

**Stage 0 totals ~3,110 LOC across source / test / docs; ~12-15
sessions of focused work.** Per `STAGING.md` U1–U10, Stage 0 pays
back at chapter 3.3; every chapter beyond that is pure compounding
(~30-40% LOC reduction per chapter; ~2,500-3,500 LOC saved across
chapters 3.1–4.4).

**Reasoning / consequences.**

The decision to ship Stage 0 as one unit (rather than threading the
twelve items through chapters 3.1+ on demand) is load-bearing for
two reasons:

1. **Type signatures are contracts; bodies are implementations.** If
   `Emitter<'element> = Catalog -> Result<ArtifactByKind<'element>,
   EmitError>` is an F# type alias before chapter 3.1 opens, every
   chapter's emitter signature matches by typing — the F# compiler
   enforces the pattern. If the type alias lands during chapter 3.3
   instead, chapters 3.1 and 3.2 have already shipped emitters whose
   signatures *don't* match the alias, and the alias becomes a
   lagging-indicator rather than a leading contract. SPINE inference
   I6 (chapter as tessellation instance) requires the contract to
   precede its instances.

2. **Foundation drift compounds.** The closed-DU expansion empirical
   test (`DECISIONS 2026-05-13`) holds because `SsKey` is a closed DU.
   The foundation refactor that splits `SsKey` into the four-variant
   DU (`OssysOriginal | Synthesized | DerivedFrom | V1Mapped`)
   touches every consumer of `SsKey` — including chapter-1's three
   sibling emitters and chapter-2's OSSYS adapter. Performing the
   split *before* chapter 3.1 opens means the new chapter's code
   pattern-matches against the four-variant DU from its first line;
   performing it *during* chapter 3.1 means chapter 3.1's slices
   fight refactoring pressure they didn't choose. The "big-bang
   within Core" framing in the pre-scope is a deliberate choice to
   absorb the disruption once.

The Stage 0 commitment is the structural answer to "should we just
start chapter 3.1 and refactor as we go?" The answer per SPINE +
STAGING: **no**, because the cost of the refactor is independent
of when it lands, but the *compounding* of consumer-side type-
discipline is monotonically front-loaded. Land the refactor before
the consumers exist; consumers inherit type discipline by typing.

**The chapter-1 baseline (631 passing tests) holds at every Stage 0
step.** Tier 1 items are documentation-only and trivially preserve
the baseline. Tier 2 (S0.A type primitives) adds a file but no
runtime behavior. Tier 3 (S0.B structural refactor) is staged across
six slices; each slice keeps the baseline green. Tier 4 modules add
new test coverage rather than perturbing existing tests.

**Stage 0 closes when all twelve items ship, the baseline holds,
the AXIOMS amendments are scheduled with TBD bodies (S0.F
scaffolding), and the chapter-close ritual confirms no drift on the
canonical surfaces (S0.J).** Chapter 3.1's slice 1 then ships *the
same day*: the JSON round-trip canary uses the existing JsonEmitter
+ CatalogReader pair through Stage 0's new types.

**This entry IS Stage 0.G.5 — the Stage-0-commitment governance
entry.** The four sibling entries (R6; chapter 3 sequencing; CLAUDE
reading-order; T-30/T-15 gates) land in the same governance burst.
Together, the five entries operationalize the foundation phase.

## 2026-05-22 — R6: Split-brain governance rule for the dual-track cutover window

**Status:** decided (R6 from `VISION_REVIEW.md`; cutover-window
governance rule for the dual-track operating mode)
**Context:** During the cutover window, V1 and V2 will both be
producing emissions for the same Catalog × Policy × Profile triple.
V1 owns the production-deployment write path; V2 emits the same
artifacts in parallel for verification. Without a governance rule
for *which* emission ships when V1 and V2 disagree, the dual-track
mode produces split-brain — two artifacts purporting to be the
production deployment, with no operationally-defined arbiter.

`VISION_REVIEW.md` reasoning resolution R6 (referenced from VISION.md
§"Cutover fallback ladder", V2-augmented path) frames the governance
posture: **V2 owns no production write path.** This eliminates
split-brain by construction — there is exactly one production write
path (V1's), and V2's role is verification, not displacement. The
question this entry resolves is: how does V2's verification couple
into the PR pipeline operationally?

Without an operational rule, three failure modes loom:
1. V1 and V2 disagree; the PR ships V1's artifact; the disagreement
   is not surfaced and accumulates as undetected drift.
2. V1 and V2 disagree; reviewers are uncertain which to trust;
   merge stalls; cutover schedule slips.
3. V1 and V2 agree consistently; V2's verification feels redundant;
   the verification surface atrophies.

**Decision:** During dual-track operation (chapters 3.1 onward,
through V2-driver mode at the earliest), **V2 emits-but-doesn't-ship.
V2's emission feeds the canary; the canary asserts V1 ≈ V2 modulo
named tolerances; disagreement blocks the PR. V2's transition to
production write path (V2-driver mode) is per-environment-per-
artifact-type, gated on N=10 consecutive green canary runs and
explicit operator sign-off.**

Operational shape:

1. **PR pipeline carries V1's artifact as the production write path.**
   This is the existing V1 surface; no changes.
2. **V2 emits in parallel.** The PR pipeline (Azure DevOps) invokes
   V2 against the same Catalog × Policy × Profile triple V1 received
   and writes V2's artifact bundle alongside V1's.
3. **Canary asserts V1 ≈ V2 modulo Tolerance.** The Tolerance taxonomy
   (S0.E; thirteen named flags per `CHAPTER_3_PRESCOPE_READSIDE_ADAPTER.md`
   §10) declares which divergences are absorbed by name (e.g.,
   `IgnoreIndexNames`, `NewlineNormalization`, `IgnoreFingerprintHash`);
   the comparator returns a structural Diff over the SsKey-keyed
   `ArtifactByKind` shape.
4. **Disagreement blocks the PR.** A Diff that is not empty after
   tolerance erasure is a structural disagreement; the PR pipeline
   fails. Reviewers triage: (a) is V1 wrong; (b) is V2 wrong; (c) is
   the divergence a known tolerance the taxonomy doesn't yet name?
   Cases (a) and (b) are bug-fix tasks; case (c) earns a new
   Tolerance flag *with* a DECISIONS entry citing the V1 file:line
   that produces the divergence.
5. **Per-environment-per-artifact-type V2-driver transition.** V2
   does not become the production write path globally. The
   transition is a switch flipped per environment (dev → qa → UAT
   → prod) per artifact type (SSDT DDL, DACPAC, RefactorLog, data
   inserts). Each switch is gated on **N=10 consecutive green
   canary runs** for that environment-artifact pair, plus
   **explicit operator sign-off**. The N=10 threshold is a
   discipline, not a magic number — it's a tractable signal that
   the canary's tolerance calibration matches operational reality
   across at least two weeks of schema-evolution cadence (assuming
   ~daily PR throughput).
6. **V1 stays warm through cutover+30.** Even after every
   environment-artifact pair has flipped to V2-driver mode, V1's
   emission path is preserved as a fallback for 30 days post-cutover.
   See the T-30 / T-15 fallback ladder entry (sibling DECISIONS
   entry) for the full ladder.

**Reasoning / consequences.**

R6 is the structural answer to "which emission ships?" The answer
is: **always V1's, until both the canary and the operator agree
otherwise, per environment-artifact pair.** V2's value is not in
shipping a different artifact; V2's value is in producing a
verification surface that V1 alone does not possess.

Three load-bearing properties follow:

- **Split-brain by construction is impossible during dual-track.**
  V1 owns the write path; V2 emits but cannot ship. The canary's
  blocking semantic ensures that disagreements surface
  operationally — they cannot accumulate silently.
- **The Tolerance taxonomy is the governance surface.** Every
  divergence between V1 and V2 either matches a named tolerance
  (acceptable) or fails the canary (blocking). New tolerances earn
  a DECISIONS entry; the taxonomy grows under empirical pressure,
  not speculation. The tolerance flags are not bug-acceptance
  switches — they are deliberate-divergence declarations, each
  citing the V1 file:line that produces the divergence and naming
  the V2 reason for accepting it.
- **The N=10 + sign-off gate is per-pair, not global.** The
  transition decision is a function of (environment, artifact type)
  — there are ~16 such pairs (4 environments × 4 artifact types).
  Each pair flips when its evidence supports the flip; no pair
  flips because another pair already did. The cutover does not
  require a coordinated global switch; it is an additive sequence
  of pair-flips, each independently auditable.

The dual-track window starts when V2 ships chapter 3.1's first
canary integration and ends when **all sixteen pairs** have flipped
to V2-driver mode (the V2-driver row of the fallback ladder). The
window may be long; that is fine — V1's warmth is the safety, not a
liability.

**This entry is R6's operational form.** The strategic frame lives
in `VISION.md` §"Cutover fallback ladder" and `VISION_REVIEW.md` R6;
this entry codifies the rule the canary's pipeline implements and
the gate the operator flips. Future agents extending the canary
(chapter 3.4) read this entry alongside the Tolerance taxonomy
(S0.E) to understand why the tolerance flags exist and what the
canary's blocking semantic guards against.

## 2026-05-22 — Chapter 3 sequencing: read-side adapter promoted to chapter 3.1 (was 3.2)

**Status:** decided (chapter 3 sequencing; promotes the read-side
adapter to the chapter-opening slice; per `VISION_REVIEW.md`
Appendix F dogfood reframing and subagent #5's pre-scope on
SnapshotRowsets)
**Context:** As of chapter-2 close, three plausible chapter-3 arcs
were on the table per `HANDOFF.md`:

- **`SnapshotRowsets` implementation chapter** — resolves the
  JSON-projection-lossiness class (SsKey at every level, EspaceKind,
  isSystemEntity); subagent #5's pre-scope.
- **`Projection.Pipeline` canary chapter** — strategic-frame axis-4;
  brings DacFx, testcontainers, ephemeral SQL Server, read-side
  adapter into V2; subagent #4's pre-scope.
- **Cross-module FK slice** — small tactical-completeness step;
  refines OSSYS rule 16's same-module assumption.

The handoff explicitly noted that subagent #4's and #5's
recommendations were compatible: SnapshotRowsets runs parallel-to or
before the canary; cross-module FK lands when convenient.

The dogfood reframing in `VISION_REVIEW.md` Appendix F (and
referenced from `VISION.md` §"Chapter 3 plan") sharpened the picture:
**the read-side adapter has two consumers from day one** —
(1) verification of V1's emissions against ephemeral deployments,
and (2) drift detection between source-of-truth catalogs (V1's
`osm_model.json` round-trip via `JsonEmitter`) and deployed reality
(read-side adapter against an ephemeral SQL Server). Both consumers
exist before any new emitter ships. Per the two-consumer threshold
(`DECISIONS 2026-05-13` — anticipation vs. speculation), the
read-side adapter earns its place from the first slice; speculative
deferral is not an option.

**Decision:** **Chapter 3 sequencing is reorganized around the
read-side adapter as chapter 3.1's centerpiece, with SnapshotRowsets
promoted to 3.2 and DacpacEmitter to 3.3.** The full sequence:

| Chapter | Title | Centerpiece deliverable | LOC est. |
|---|---|---|---|
| **3.1** | Read-side adapter + comparator + Projection.Pipeline shell | `Projection.Adapters.Sql.ReadSide.CatalogReader` matching `Adapter<connStr × schemas, Catalog, ReadSideError>`; `CatalogEquivalence` comparator matching `Compare<Tolerance>`; canary tier-1 property tests on the JsonEmitter round-trip | ~30 items per BACKLOG |
| **3.2** | SnapshotRowsets adapter variant | `SnapshotSource.SnapshotRowsets` variant; multi-rowset deserialization; closes the JSON-projection-lossiness class | ~25 items |
| **3.3** | DacpacEmitter + DacFx wrapper | `Projection.Targets.SSDT.DacpacEmitter` matching `Emitter<TSqlObjectScript>`; `Projection.Targets.SSDT.Dacpac` C# DacFx interop project; T1 amendment for binary normal-form composition | ~25 items |
| **3.4** | Canary as property-test surface | Tier-1 / tier-2 / tier-3 property tests; FsCheck generators; multi-environment generator (S0.K) extended; ~12 predicates | ~12 items |
| **3.5** | RefactorLogEmitter + CatalogDiff | `Emitter<RefactorLogEntry list>` matching the `EmitterOverDiff` pattern; `CatalogDiff` exhaustiveness amendment (A36 candidate); `OssysOriginal` SsKey access from `SnapshotRowsets` | ~10 items |
| **3-cross-cutting** | ArtifactByKind + SsKey four-variant + CatalogDiff foundation | Stage 0's S0.B; ships **before** 3.1 opens, not as a chapter | ~8 items |

The cross-module FK slice (handoff's third option) lands as a
chapter-3.x sub-slice when convenient — most likely during chapter
3.2 SnapshotRowsets work, since the cross-module case may force
walking V1's `relationships[]` array and SnapshotRowsets has
adapter-side touch points.

**Reasoning / consequences.**

The promotion is structurally motivated, not aesthetically. Three
points:

1. **Two-consumer earned membership.** Per `DECISIONS 2026-05-13`
   anticipation-vs-speculation Position A (full extraction requires
   shape visibility *plus* a concrete second consumer), the
   read-side adapter passes both gates from day one. Verification
   is consumer one; drift detection is consumer two. The earlier
   plan's Position B (defer the adapter; ship SnapshotRowsets first)
   underweights the dogfood frame — V2 can verify V1 *now*, before
   any new emitter ships, simply by reading both V1's `osm_model.json`
   (via `JsonEmitter` round-trip) and the deployed reality (via
   read-side adapter) and comparing. The "now" here is operationally
   real: the trunk has thirteen UAT environments where the V1
   pipeline already runs; V2 can ride that pipeline as a
   verification-only sidecar without touching any production write
   path.

2. **The canary's tolerance taxonomy needs a fixed point.** Without
   the read-side adapter, the canary's tolerance flags
   (`IgnoreIndexNames`, etc.) are abstract — names without referents.
   With the read-side adapter as chapter 3.1's centerpiece, every
   tolerance flag earns its DECISIONS entry from the first slice
   that observes a divergence in deployed reality. The taxonomy
   grows under empirical pressure (the OSSYS chapter-2 discipline);
   chapter 3.1's slice 5 (Tolerance profile calibration; per the
   pre-scope) is exactly the slice that codifies what "modulo
   tolerance" means concretely.

3. **SnapshotRowsets is sequenced *after* the read-side adapter
   precisely because the read-side adapter unblocks the dogfood
   frame.** Subagent #5's pre-scope notes that SnapshotRowsets
   "should run parallel-to or before the canary"; the parallel-to
   reading is correct here. Chapter 3.1 opens with the dogfood
   frame using existing `SnapshotJson`; chapter 3.2 SnapshotRowsets
   work proceeds in parallel and lands when ready, at which point
   the canary upgrades to consume the higher-fidelity catalog.
   The A1-bound resolution (per `AXIOMS.md` A1's bottom note;
   `DECISIONS 2026-05-15` session-20 amendment) lands at chapter
   3.2's close, not 3.1's. **Chapter 3.1 ships under the bound;
   the bound's lift is chapter 3.2's contribution.**

The sequencing also aligns with subagent #4's DacpacEmitter
pre-scope: chapter 3.3 (DacpacEmitter) consumes a Catalog flowing
through chapter 3.1's Projection.Pipeline shell against ephemeral
SQL Server (via the read-side adapter and the C# DacFx wrapper).
The DacFx trigger's tighter re-deferral condition (`DECISIONS
2026-05-06` session-24 amendment) names "a real Catalog flowing
end-to-end through a pipeline exercising T11 sibling-Π
commutativity on real metadata; canary chapter is the natural
locus" — chapter 3.3 is exactly that locus.

**Implication for the chapter-3 agent.** Chapter 3.1 opens with the
read-side adapter as its centerpiece. Slice 1 is the JSON
round-trip canary using existing `JsonEmitter` + `CatalogReader`;
slice 2 ships the read-side adapter skeleton + queries 1-2; slices
3-4 extend; slice 5 codifies tolerances; slice 6 lands the
Projection.Pipeline orchestrator. The closing chapter-3.1
deliverable: **V2 verifies V1 against ephemeral SQL Server, with
named tolerances, with a triangulation comparator, with the seven
type primitives in production use.** The cutover fallback ladder's
V2-augmented mode is operational at chapter 3.1's close.

**This entry supersedes the implicit chapter-3 sequencing carried
by `HANDOFF.md` §"Where to start".** The handoff letter named three
plausible arcs without ordering them; this entry orders them. The
handoff's framing — that subagent #4's and #5's recommendations are
compatible — survives; the addition is the explicit ordering and
the dogfood-frame reasoning that motivates it.

## 2026-05-22 — CLAUDE.md reading-order update: VISION.md added to canonical surface list

**Status:** decided (CLAUDE.md fresh-agent reading order; per Stage
0 governance burst)
**Context:** `CLAUDE.md` lists the reading order for fresh agents
in §"Reading order for a fresh agent". As of session 25 close, the
list named (1) HANDOFF.md, (2) CHAPTER_2_CLOSE.md, (3)
CHAPTER_1_CLOSE.md, (4) AXIOMS.md, (5) DECISIONS.md, (6) ADMIRE.md,
(7) README.md, (8) the code. The list predates the 2026-05-08
strategic-surface burst that added `VISION.md`, `SPINE.md`,
`PLAYBOOK.md`, `STAGING.md`, and `BACKLOG.md` as companion
canonical documents.

`VISION.md` is the strategic frame — the cutover as forcing
function; the sibling chorus + verification posture; acceptance
criteria; the cutover fallback ladder. It is **load-bearing for
fresh agents** in a way that the sub-companion documents
(`SPINE.md`, `PLAYBOOK.md`, `STAGING.md`) elaborate but do not
substitute. A fresh agent who reads HANDOFF + AXIOMS + DECISIONS
without VISION inherits the tactical posture but not the strategic
*why*; VISION's "what V2 uniquely contributes" section is the
answer to "should I prioritize this chapter or that one?" that the
tactical surfaces presuppose.

The chapter-close ritual (`DECISIONS 2026-05-14`) makes CLAUDE.md
currency a load-bearing item. Adding VISION.md to the reading
order is not a cosmetic update; it is a structural correction of
a list that has drifted out of date relative to the canonical
surface set.

**Decision:** **`VISION.md` is added to CLAUDE.md's "Reading order
for a fresh agent" section as item 1.5 — read after HANDOFF.md and
before CHAPTER_2_CLOSE.md.** The companion strategic surfaces
(`SPINE.md`, `PLAYBOOK.md`, `STAGING.md`, `BACKLOG.md`) are
documented in the reading order's preamble as **on-demand**
references — read when the relevant work surfaces them, not as
part of the canonical first-read pass.

The full updated reading order:

1. **HANDOFF.md** — bridge letter from the most-recent-closed
   chapter.
2. **VISION.md** *(new)* — strategic frame; cutover as forcing
   function; acceptance criteria; cutover fallback ladder. Read
   for the *why*. Companion strategic surfaces (`SPINE.md`,
   `PLAYBOOK.md`, `STAGING.md`, `BACKLOG.md`) referenced on
   demand.
3. **CHAPTER_2_CLOSE.md** — chapter-2 close synthesis.
4. **CHAPTER_1_CLOSE.md** — chapter-1 close synthesis.
5. **AXIOMS.md** — the formal system.
6. **DECISIONS.md** — append-only resolved-questions log.
7. **ADMIRE.md** — V1↔V2 bridge.
8. **README.md** — surface-level orientation.
9. **The code.**

KICKOFF.md (added at `1cdfe1e`) is the fresh-agent first-message
brief — the 5-minute orientation that points at the canonical
surfaces in the order this entry codifies. KICKOFF is the
on-ramp; the canonical reading order is the ramp itself.

**Reasoning / consequences.**

The update has three downstream consequences:

1. **Fresh agents reach VISION before tactical surfaces.** A
   fresh agent reading HANDOFF + VISION before AXIOMS / DECISIONS
   has the strategic frame in context when interpreting the
   tactical entries. Without VISION, the entry "DacFx trigger
   re-deferred to canary chapter" reads as schedule juggling;
   *with* VISION, it reads as "the canary is the verification
   surface; DacFx without the canary has no consumer." The
   strategic surface is the structural why for tactical entries
   that otherwise read as detached judgment calls.

2. **The chapter-close ritual scans for currency on this section.**
   The chapter-close ritual (`DECISIONS 2026-05-14`) item 3
   ("CLAUDE.md / README.md staleness checks") explicitly walks the
   reading-order section. As new strategic surfaces land
   (post-Stage 0, plausibly: a `RUNBOOK.md` if cutover ops
   warrants; a `RISKS.md` if the active deferrals index
   outgrows DECISIONS' top), the ritual catches the drift. This
   entry sets the precedent: **strategic surfaces earn explicit
   placement in the reading order or explicit on-demand
   designation**, not silent omission.

3. **The companion-strategic-surface designation prevents
   reading-order bloat.** SPINE / PLAYBOOK / STAGING / BACKLOG
   each carry ~500-900 lines. Including them all in the canonical
   first-read pass extends the orientation hour to a half-day; the
   on-demand designation respects the differential weight
   (strategic frame for everyone vs. structural-mechanics for
   chapter-open scoping). The designation is itself a discipline:
   **first-read pass is short and load-bearing; companion
   surfaces are deeply useful but consulted by the chapter agent
   for the chapter's needs**.

The update is small in lines (one new bullet plus the on-demand
preamble) but compounds: every fresh-agent session from now on
reaches the strategic frame before the tactical entries.

**This entry pairs with the CLAUDE.md edit landing in the same
Stage 0 governance burst.** The DECISIONS entry codifies the
decision; the CLAUDE.md edit operationalizes it.

## 2026-05-22 — T-30 / T-15 cutover fallback ladder gates

**Status:** decided (cutover-time fallback gates; per
`VISION.md` §"Cutover fallback ladder" and the R6 split-brain
governance entry)
**Context:** `VISION.md` §"Cutover fallback ladder" names three
operating modes for the cutover window: V2-driver (V2 owns the
write path; V1 is fallback), V2-augmented (V1 drives; V2 verifies),
and V1-only (V1 ships alone; V2 does not run). The ladder names
the modes but did not codify the *gates* — the structural
conditions under which the operating mode shifts as the cutover
date approaches.

Without operationalized gates, three failure modes loom:
1. **V2-driver pursued past readiness.** Chapter 3 closes with a
   green canary on synthetic fixtures; chapter 4 ships partially;
   the team flips to V2-driver because the schedule says "now."
   Production data integrity risk: V2's chapter 4 isn't yet
   shipping the cutover-blocking artifacts, so the V2-driver flip
   is reckless.
2. **V2-augmented unwound at the last minute.** The canary
   surfaces a tolerance-flag-needing divergence at T-7 days; the
   team panics and disables V2; the canary surface goes dark; the
   cutover loses its verification posture in the highest-risk
   week.
3. **V1-only declared too late.** V2's chapters 3-4 are
   structurally on track but behaving unstably under operational
   load (testcontainers contention; CI flake; tolerance-taxonomy
   churn); the team waits until T-3 days to fall back to V1-only,
   leaving no time to validate V1's standalone path.

The ladder needs T-30 and T-15 gates: structural conditions at
fixed dates that determine which operating mode the cutover
window enters and whether late-stage retreat is permitted.

**Decision:** **V2-driver mode requires four conditions met by
T-30 (thirty days before cutover); V2-augmented and V1-only fall
out of the gate's negative branches.**

### V2-driver gate (T-30)

V2-driver mode is the aspirational target — V2 owns the production
write path; V1 stays warm as a fallback. **All four conditions
below must be met by T-30 days for V2-driver to be the cutover
mode:**

(a) **Chapter 3 closed with green canary on full 300-table
    Catalog.** Chapter 3.5's RefactorLogEmitter has shipped;
    chapter 3.4's canary has run successfully across the full
    Catalog (not synthetic fixtures); the read-side adapter
    against ephemeral SQL Server holds for all 300 tables; T1
    binary-normal-form composition (chapter 3.3 amendment) holds
    on real DACPAC artifacts.

(b) **Chapter 4.1 (data triumvirate) shipping.** Chapter 4.1.A
    (SSDT DDL emitter) and chapter 4.1.B (CDC-aware data
    triumvirate — `StaticSeedsEmitter`, `MigrationDependenciesEmitter`,
    `BootstrapEmitter`) are operational; the change-detection
    predicate for CDC-tracked tables (chapter 4.1.B slice 6) holds
    on the `change_tracked` table set; redeploy-zero-ALTERs
    property holds.

(c) **Chapter 4.2 (User FK reflow) shipping.** The four V1
    pipeline-step distillation has shipped:
    `UserMatchingStrategy` DU; `UserRemapContext` value;
    `UserFkReflowPass.discover` produces the context; sibling
    Π's consume the context (the A32 cash-out — see the AXIOMS
    amendment scaffolding entry). User FKs (CreatedBy, UpdatedBy)
    remap correctly across all four environments under operator-
    supplied mapping inputs.

(d) **≥1 full UAT dry-run.** A complete environment-by-environment
    dry-run of the cutover sequence (dev → qa → UAT → prod
    against UAT-equivalent infrastructure) has shipped, with
    cross-environment Profile/Policy pairs producing structurally-
    consistent artifacts. Subagent #4's pre-scope notes the
    byte-determinism risk of vanilla DacFx `BuildPackage`; this
    dry-run's success implies that risk has been fully mitigated.

If all four conditions hold at T-30, the cutover proceeds in
V2-driver mode per environment per artifact type, gated on the
N=10 + sign-off discipline (R6).

### V2-augmented fallback (T-30 yellow)

If any subset of (a)-(d) fails at T-30 but the canary surface is
operational and stable, the cutover falls back to V2-augmented
mode: **V1 drives the cutover; V2 runs the canary as a
verification-only sidecar.** V2 emits, deploys to ephemeral, reads
back, compares to V1's emission and to V2's expected Catalog.
Disagreement blocks the PR (R6's blocking semantic). V2 owns no
production write path. This is the **safe-default mode** for the
cutover window — V1's existing capability ships the cutover; V2's
verification posture surfaces drift without taking on production
risk.

The T-30 gate **does not require all conditions** for V2-augmented;
it requires only that the canary surface itself is operational
(chapter 3 closed; tolerance taxonomy stable; comparator returning
structural Diffs; PR blocking semantic wired up). A partial chapter
4 is acceptable for V2-augmented because the verification surface
operates against whichever artifacts V1 ships.

### V1-only retreat (T-15 unstable)

If between T-30 and T-15 the canary itself becomes unstable
(testcontainers contention causing CI flake at >10% rate;
tolerance-taxonomy churn requiring multiple per-week DECISIONS
entries; the comparator producing inconsistent Diffs across
re-runs against the same input), the cutover falls back to
V1-only mode: **V1 ships the cutover alone; V2 does not run
during the cutover window.** V2 development continues post-cutover;
the cutover-quarter trajectory completes through V1's existing
capability without V2's verification surface.

The T-15 gate is operational (canary stability), not structural
(chapter completion). T-15 is fifteen days before cutover —
sufficient time to validate V1's standalone path against the
final environment-by-environment sequence without V2's
verification overhead.

### Hard rule: V1 stays warm through cutover+30

Regardless of which mode the cutover enters, **V1's emission path
is preserved as a fallback for thirty days post-cutover.** V1's
codebase remains buildable; its CI lane remains green; its
emission script remains runnable. If a post-cutover defect
surfaces in V2's emissions (unlikely but possible), the operator
can re-emit via V1 within the cutover+30 window. The V1 sunset
decision is deferred to **chapter 5+** when all four environments
have run V2 emissions for one full schema-evolution cycle and the
operator has explicit confidence in V2's standalone behavior.

**Reasoning / consequences.**

The gate dates (T-30, T-15) are structural choices, not arbitrary
calendar markers:

- **T-30 is the earliest operationally-meaningful gate.** Less
  than thirty days before cutover, late-stage chapter work cannot
  reach production-ready stability under any reasonable schedule.
  The gate names the four conditions because each is a structurally
  load-bearing chapter-cluster (read-side + canary + DACPAC for
  3.x; SSDT + data for 4.1; User FK reflow for 4.2; UAT dry-run
  for cross-environment integration). Missing any of the four
  produces a cutover risk that V2-augmented absorbs but V2-driver
  does not.

- **T-15 is the safety gate.** Fifteen days is enough for the
  team to validate V1's standalone path through the final
  environment sequence (dev → qa → UAT → prod) without V2 in the
  loop. Less than fifteen days, the V1-only path itself becomes
  high-risk because operators have lost familiarity with running
  the cutover without V2's verification feedback. T-15 is the
  point at which V1-only retreat is *still* operationally safe;
  after T-15, retreat becomes "ship and pray."

- **The hard cutover+30 V1-warm rule is non-negotiable.** It is
  the cutover's deepest fallback layer; it survives every gate
  decision because it costs nothing operationally (V1 already
  exists; preserving its build is keeping a green lane green).
  The cost is purely calendric — chapter 5+ work that depends on
  V1 retirement defers thirty days. That is acceptable.

**Three load-bearing properties follow from the gates:**

1. **The cutover decision criterion at T-30 is mechanically
   evaluable.** Each of (a)-(d) is a yes/no structural check; the
   operator does not exercise judgment on whether the chapter has
   "shipped enough" — the chapter's close synthesis is the
   evidence. This makes the T-30 review a *audit*, not a
   *negotiation*.

2. **The T-15 retreat is reversible-cost-bounded.** Falling back
   to V1-only at T-15 costs the team V2's verification posture
   for the cutover window itself. It does **not** cost the team
   V2's chapter 3-4 work — that work continues, and the canary
   resumes post-cutover as part of cutover+30 validation. The
   retreat is not a sunset; it is a pause.

3. **The four-environment cutover stays per-environment-per-
   artifact-type.** The gates determine the *mode*; R6 governs
   the per-pair flips inside the chosen mode. The two
   abstractions compose: the gate sets the ladder rung; R6
   determines per-pair progression along the rung.

**Operational implication.** The chapter-3 and chapter-4 agents
working between now and T-30 know the four conditions explicitly.
Chapter 3.5 cannot defer to a later chapter without forfeiting
condition (a); chapter 4.1's slice list must close all
cutover-blocking properties to honor (b); chapter 4.2 must close
the User FK reflow before T-30 to honor (c); the UAT dry-run must
be scheduled against (a)-(c)'s readiness to honor (d). The chapter
backlogs inherit the gate as a structural deadline.

**This entry codifies the cutover fallback ladder's gates.** The
ladder itself lives in `VISION.md` §"Cutover fallback ladder"; R6
(per-pair governance during dual-track) lives in the sibling
DECISIONS entry; the AXIOMS amendments scheduled at chapter
closes (T1, T11×2, A1, A35, A36, A32 cash-out) align with the
chapters whose closure conditions (a)-(d) require. **Together
the three governance entries (R6, sequencing, gates) plus the
Stage 0 commitment and the CLAUDE.md reading-order update
operationalize the cutover-quarter trajectory.**

## 2026-05-23 — Source SQL Server with OutSystems semantics is the canary's primary wide integration surface

**Status:** decided (canary integration-surface frame; per session 28
operator framing alongside the M2→M3 milestone push)
**Context:** M2 shipped the deploy half of the canary loop (V2's
emitted SSDT → ephemeral SQL Server → table count). M3 lands the
read-side adapter that closes the loop (deployed schema → V2 IR
reconstruction). The question of *what fixture* the canary's
round-trip property runs against is structurally load-bearing —
the canary only catches what the fixtures stretch.

The current minimal fixture (`v1MinimalFixture` in
`OsmCatalogReaderDifferentialTests.fs` and
`EndToEndPipelineTests.fs`) is one module, one entity, two
attributes, no FKs, no indexes, no static data. It is the smallest
fixture that exercises the OSSYS adapter's translation rules and
is appropriate for unit-test-style assertions on parser output.
But it does **not** exercise:

  - Multi-table relationships (FK chains; cycle resolution)
  - The full PrimitiveType matrix (Identifier, Decimal, Boolean,
    DateTime, Date, Time, Binary, Guid)
  - Multi-tenant patterns (TENANT_ID columns; TenantScoped modality)
  - Audit columns (CREATEDBY/CREATEDON/UPDATEDBY/UPDATEDON; user FK
    reflow per chapter 4.2)
  - Static-entity populations
  - External-entity origins (OssysOriginal SsKey wiring)
  - Index variety (PK-as-clustered vs heap; non-unique with INCLUDE)
  - SS_KEY column variety (the `OssysOriginal` four-variant from
    slice 5.5 only matters when fixtures carry the GUID column)
  - Cross-module references
  - The 300-table scale that VISION.md's forcing function names

The forcing function (per `VISION.md`) is a **300-table OutSystems
11 system facing an External Entities cutover**. If the canary's
fixture is a one-table toy, the canary will pass while V2 silently
ships catalog-corrupting bugs on real operator data. The fixture
shape determines what the canary catches.

**Decision:** The canary's primary wide integration surface is a
**SQL Server fixture with OutSystems semantics**, deployed to an
ephemeral container (per M2's testcontainers infrastructure), grown
iteratively over time to cover OutSystems' full schema-shape
vocabulary. Per session-28 operator framing:

  > "Set up a source SQL server environment that tries its best to
  > mirror the semantics of what the OutSystems SQL must therefore
  > look like."
  > — operator, session 28

The source SQL Server is **not** the OSSYS metadata DB; it is the
**operator's application database** — the OSUSR_*-shaped tables an
OutSystems platform creates for entities. The conventions to
mirror:

  - **Table naming.** `OSUSR_<MODULE-CODE>_<ENTITY-NAME>` with
    upper-case (e.g., `OSUSR_M3_CUSTOMER`). Module code is the
    short identifier; entity name is the entity's physical name.
  - **Identity columns.** `[ID] INT NOT NULL IDENTITY(1,1) PRIMARY
    KEY` for auto-number entities. Identifier-typed columns
    elsewhere are typed `INT`.
  - **Multi-tenant marker.** `[TENANT_ID] INT NOT NULL` on
    multi-tenant entities; OutSystems platform-side discriminator.
  - **Audit columns.** `[CREATEDBY] INT NULL` (FK to User entity),
    `[CREATEDON] DATETIME2 NOT NULL`, `[UPDATEDBY] INT NULL`,
    `[UPDATEDON] DATETIME2 NOT NULL`. Wired through every entity
    that participates in the cutover's user-FK-reflow story
    (chapter 4.2).
  - **Stable-key column.** `[SS_KEY] UNIQUEIDENTIFIER NOT NULL` —
    OutSystems' GUID-based identity primitive that the
    `OssysOriginal` SsKey variant (slice 5.5) carries verbatim.
    Source for the `SnapshotRowsets` adapter variant when chapter
    3.2 ships.
  - **NVARCHAR length.** OutSystems' Text type maps to
    `NVARCHAR(N)` with operator-supplied length (50 / 100 / 250 /
    500 / 1000 / 2000 / 4000 / MAX). Not always MAX.
  - **Decimal precision.** `DECIMAL(P, S)` with operator-supplied
    precision and scale. Common: `DECIMAL(18, 4)`, `DECIMAL(38, 8)`,
    `DECIMAL(8, 2)` for currency.
  - **Boolean as BIT.** No `TINYINT` substitution.
  - **Foreign-key style.** Named constraints
    (`FK_OSUSR_M3_CUSTOMER_USERID_OSUSR_M3_USER`); ON DELETE
    NO ACTION by default; some `ON DELETE CASCADE` for owned-aggregate
    relationships.
  - **Index style.** Clustered PK; non-unique secondary indexes
    with `INCLUDE` columns for covering common query patterns;
    occasional unique non-PK indexes for natural-key uniqueness.

**Reasoning / consequences.**

The integration-surface frame has three load-bearing properties:

1. **The canary's round-trip property is meaningful only at
   representative scale.** A property test that asserts "every
   table the source has appears in the target with the same
   columns" catches bugs only across the table/column shapes the
   source actually has. If the source is one table, the property
   passes for any single-table emitter. If the source has 30
   tables with realistic FK chains, identity columns, multi-tenant
   markers, audit columns, and varied data types, the same
   property is structurally rich and surfaces real
   defect-classes (missing audit columns, wrong nullability on
   FK-target IDs, type-mapping inconsistencies on Decimal
   precision, ...).

2. **Iterative growth is the model.** The source DDL fixture
   starts small (M3: 2-3 tables exercising the FK-target +
   audit-column shape). It grows iteratively as new defects
   surface or new shapes need coverage. The DECISIONS entry that
   would land here at chapter 3.4 close ("canary fixture corpus
   reaches stability mark") tracks the growth trajectory; the
   stability mark is when adding new shapes stops surfacing new
   classes of defect (typically N=3 of consecutive shape additions
   that produce zero new failures). Per the codification-stability-
   mark discipline (`DECISIONS 2026-05-14 — Writer codification
   reaches its stability mark`), the canary's fixture corpus
   follows the same N=3 protocol.

3. **The source fixture is itself the contract for what V2 must
   support.** Tracing the OutSystems platform's actual schema
   conventions means the fixture is *not* synthetic — it is a
   minimum-viable-OutSystems schema that V2 has to handle without
   compromise. New conventions surface as new fixture shapes; the
   adapter / emitter / pass surface evolves to support each new
   shape under the canary's blocking semantic. This is the
   trace-before-fixture discipline (`DECISIONS 2026-05-19`)
   extended to schema-shape coverage: trace OutSystems' actual
   conventions; classify the gap; resolve.

**Operational shape.**

  - Source DDL fixture lives at
    `tests/Projection.Tests/Fixtures/SourceSchema.fs` (initially
    embedded F# string; promoted to `.sql` resource files when
    the corpus exceeds ~500 LOC of DDL).
  - The canary's wide integration test (`CanaryRoundTripTests.fs`)
    deploys the source DDL to an ephemeral SQL Server, reads it
    back via the read-side adapter (M3), runs V2's emitter on the
    reconstructed Catalog, deploys to a *second* ephemeral
    container, reads back, and asserts source ≈ target modulo
    named tolerances.
  - The Tolerance taxonomy (Stage 0 S0.E; M4) names which
    divergences are absorbed as known emitter-IR limitations
    (e.g., `IgnoreColumnLength` while NVARCHAR length isn't in
    the IR; `IgnoreIdentityProperty` while IDENTITY isn't in the
    IR). Every tolerance flag earns a DECISIONS entry citing the
    source-fixture shape that motivated it and the IR refinement
    or emitter improvement that would resolve it.
  - As the fixture corpus grows, V2's IR grows with it (per
    `DECISIONS 2026-05-07 — IR grows under evidence`). Each new
    fixture shape that surfaces an IR gap earns either an IR
    refinement (with corresponding adapter/emitter work) or a
    tolerance flag (with the deferred-IR-work logged as an Active
    deferral).

**This entry establishes the canary's wide integration surface
as load-bearing for V2's verifiable-cutover guarantee.** It is
sibling to the R6 split-brain governance rule (`DECISIONS
2026-05-22 — R6`): R6 names the per-pair flip discipline at the
PR-pipeline scale; this entry names the property-test surface at
the canary scale. Together they are how V2 earns its
verifiable-cutover guarantee — R6 says "the canary's verdict
blocks the PR"; this entry says "the canary's verdict is
meaningful because the source covers operator reality."

**Reading order for fixture additions.** Future agents adding
fixture shapes follow the trace-before-fixture pattern:

  1. Identify the OutSystems shape needing coverage (e.g., a new
     PrimitiveType, a new modality, a new index variety).
  2. Trace what the OutSystems platform actually emits to disk
     (V1's `SnapshotJsonBuilder` at
     `src/Osm.Pipeline/SqlExtraction/SnapshotJsonBuilder.cs`
     names the conventions; SQL Server documentation for the
     specific column type / index style fills the rest).
  3. Add the shape to `SourceSchema.fs` as a new table or column
     in an existing table.
  4. Run the canary's round-trip test; observe the defect that
     surfaces.
  5. Either improve V2's IR/adapter/emitter to handle the shape,
     OR add a tolerance flag (S0.E) with a DECISIONS entry citing
     the source-fixture shape and the deferred resolution.
  6. Retire the tolerance when the IR/emitter improvement lands.

The forcing function: V2 ships when the source fixture covers the
operator's actual 300-table schema, all defects are either
resolved or named as deferred tolerances, and the canary's
round-trip is green on full-scale fixtures. That is when V2
earns the V2-driver row of the cutover fallback ladder per
`DECISIONS 2026-05-22 — T-30 / T-15 cutover fallback ladder gates`.

## 2026-05-24 — Bench surface caught two wrong-direction canary optimizations (sys.* readside queries; MARS-enabled parallel readside)

**Status:** decided (Phase 3 of session-30 canary optimization
push; documented as a worked example of the bench-surface-as-
optimization-signal discipline)
**Context:** Per session-30 operator framing — "increase
observability and agent monitoring/alerting in a recurrent way
that promotes attention and optimization over time" — the Bench
observability layer (`Projection.Core.Bench`, commit 7bb0ca0)
plus iterator-level coverage (commit fb12761) were specifically
intended to make the canary's perf surface visible enough that
optimization candidates could be evaluated empirically rather
than by intuition.

Phase 3 of the optimization sequence tried three changes against
the canary's bench surface:

  1. **Switch readside queries from INFORMATION_SCHEMA to sys.***
     for `readColumnRows` and `readPrimaryKeys`. Hypothesis: sys.*
     catalog tables are direct system tables; INFORMATION_SCHEMA
     is a view layer over them; sys.* should be faster.

  2. **Enable MARS + parallelize readside queries.** Hypothesis:
     the column-rows query and the primary-keys query are
     independent; with `MultipleActiveResultSets=true` on the
     connection string they can run concurrently on one
     connection; total readside.read time becomes
     `max(columns, pks)` instead of `columns + pks`.

  3. **Eliminate `countUserTables`** by deriving table count from
     the readside Catalog (`Catalog.allKinds c |> List.length`)
     in `runWithReadback` / `runWideCanary`. The query is
     redundant once readside reconstruction succeeds.

Bench data on the canary-gate fixture (warm container; 2
OUSR_*-shaped tables, 18 attributes) before / after each change:

  | Optimization                  | Per-canary total | Per-readside | PK query | Columns query | Verdict |
  |-------------------------------|-----------------:|-------------:|---------:|--------------:|---------|
  | (baseline; pre-Phase-3)       |          1270 ms |       418 ms |   140 ms |          56 ms | n/a     |
  | + sys.* readside (alone)      |          ~1320 ms|       ~480 ms|   174 ms |         130 ms | **REVERTED** |
  | + MARS + parallel readside    |          ~1340 ms|       ~470 ms|   295 ms |         130 ms | **REVERTED** |
  | + drop countUserTables        |          1198 ms |       418 ms |   140 ms |          56 ms | **KEPT** |

Each cell is median across 3-5 runs.

**Decision:** **Keep** the `countUserTables` elimination
(~70 ms savings, ~5.5% of canary total). **Revert** sys.* readside
queries — INFORMATION_SCHEMA is faster at canary scale. **Revert**
MARS + parallel readside — MARS adds ~150 ms per query when
enabled even when queries don't actually interleave; the
parallelism savings (~100 ms) don't compensate for the per-query
regression.

**Reasoning / consequences.**

This entry is the worked example of why the bench surface was
worth building. Without per-query timings, all three changes
would have looked plausible by intuition:

  - "sys.* is the underlying table; INFORMATION_SCHEMA is a view.
    The view layer must add overhead." → Wrong. SQL Server's
    optimizer specializes INFORMATION_SCHEMA's `CONSTRAINT_TYPE`
    filter more efficiently than `is_primary_key = 1` against
    sys.indexes; the view is faster in our access pattern.
  - "MARS lets parallel queries share a connection." → Right
    technically, but MARS adds per-command overhead (~150 ms in
    our measurements) that dominates the parallelism savings at
    small-DB scale. Larger DBs may flip this trade-off; not
    relitigating without re-measurement.
  - "countUserTables is just one query; how slow could it be?"
    → ~70 ms of pure overhead per readback-style call. Eliminating
    it was strictly free.

The bench data made each verdict visible within a single
canary run.

**Three forward implications** for future optimization passes:

1. **Measure first, intuit second.** Each candidate optimization
   gets a per-canary run before / after; the bench surface
   confirms or refutes the hypothesis empirically. Future agents
   touching the canary's hot path follow the same protocol.

2. **Document dead ends in code.** The reverted sys.* and MARS
   approaches are recorded as docstring "Bench note" sections on
   `readside.readColumnRows` and `readside.readPrimaryKeys`. The
   note names what was tried, the measured result, and the
   reverted state. Future agents who consider the same swap see
   the prior data first.

3. **Optimization candidates should be tagged with their bench
   leverage.** Items in the Active deferrals index for perf
   improvements should cite the per-label total they propose to
   reduce. E.g., "CREATE SCHEMA-instead-of-DATABASE optimization
   targets the `deploy.createDatabase` label currently averaging
   ~360 ms per call; bench delta to verify on the canary-gate
   fixture; revert if no improvement." This format gives the
   reviewer a structural check against the "but did it actually
   help?" question.

**Active perf candidates (deferred pending bench-driven
investigation):**

  - **CREATE SCHEMA-instead-of-DATABASE** for canary isolation.
    Targets `deploy.createDatabase` (~360 ms × 2 = 720 ms / canary
    = 60% of total). Requires DDL string substitution to retarget
    `[dbo]` to `[Source_<guid>]` / `[Target_<guid>]`; readside
    queries to filter by schema; PhysicalSchema comparison
    invariant under schema-name. Substantial change; defer to a
    bench-leverage-justified slice.
  - **Connection-pool warm-up.** First SqlConnection in a process
    pays TLS + auth setup (~150 ms observed). Pre-warm at
    SessionStart by opening + immediately closing a connection.
    Easy win; defer to a follow-up.
  - **Single-readside-call wide canary.** Currently the wide
    canary calls `ReadSide.read` twice (once per phase). For
    fixtures where the source and target schemas are identical,
    one call suffices via a comparison-aware readside. Tightly
    coupled to the schema-vs-database refactor above.

**This entry IS the discipline.** Optimization conversations
that don't surface a bench delta should be redirected to "run
the canary, capture the snapshot, then we'll talk." Per the
session-29 framing extended in session-30: the bench surface is
how V2 earns its perf claims, the same way the canary's
PhysicalSchema diff earns its fidelity claims. Both rest on
making the relevant evidence cheap to produce.



## 2026-05-26 — Session 32 / Type fidelity round-trip — IR carries Length / Precision / Scale / IsIdentity

The canary's PhysicalSchema-axis comparison previously caught
`(schema, table, column, type, nullable, isPrimaryKey)` drift but
silently absorbed declared-length and identity-property
divergences (NVARCHAR(50) → NVARCHAR(MAX) absorbed; INT IDENTITY →
INT absorbed). Session 32 closed the gap: `Attribute` grows
`Length : int option`, `Precision : int option`, `Scale : int
option`, `IsIdentity : bool`. ReadSide reads from
`INFORMATION_SCHEMA.COLUMNS` (CHARACTER_MAXIMUM_LENGTH /
NUMERIC_PRECISION / NUMERIC_SCALE) and `sys.columns.is_identity`.
The 300-table forcing-function fixture round-trips with full
type fidelity green.

`PhysicalSchema.PhysicalColumn` extended with `Length` /
`Precision` / `Scale` / `IsIdentity`; diff renderer prints them
when present. T1 byte-determinism strengthens to type-declaration
round-trip: identical input declares identical output across runs.


## 2026-05-27 — Session 33 / Data plane round-trip — PhysicalSchema.Rows axis + StaticRow.Values raw IR contract

`PhysicalSchema` grows a fourth axis: `Rows : Set<PhysicalRow>`
(SHA256-hashed). Each `PhysicalRow = { Schema; Table; Hash }`
where `Hash` is SHA256 over sorted `<column>=<value>` pairs.
ReadSide reads row data per kind (default threshold: 1000 rows)
and populates `Kind.Modality = [ Static rows ]`. RawTextEmitter
emits `INSERT INTO ... VALUES (...);` from the IR; the
round-trip closes the data axis at static-table scale.

**The IR contract for `StaticRow.Values : Map<Name, string>` is:
raw invariant-culture strings, no SQL quoting.** Both ReadSide
(`formatRawValue`) and the V1 JSON adapter (`Static.fs`)
produce raw strings; the emitter (`Render.formatSqlLiteral`)
quotes per `PrimitiveType`. The contract centralizes the
convention so all producers/consumers agree.

`""` denotes NULL by convention. `Truncation` to 5 entries in
`PhysicalSchema.renderDiff` for row diffs at scale (failure
diagnostics).


## 2026-05-28 — Session 34 / A35 cash-out: Π's output is a deterministic statement stream

Session 34 cashed AXIOMS' A35 candidate. Π's canonical output is
no longer `string`; it is `seq<Statement>` — a typed,
deterministic, lazy stream. New file `Statement.fs` carries the
DU (`Blank | Comment | CreateTable | InsertRow | SetIdentityInsert`).
`Render.toText` is one realization of the stream; `Deploy.executeStream`
is another. Both consume the same upstream.

T1 byte-determinism strengthens to *statement-stream
determinism*: identical Catalog produces identical Statement
sequence; `Render.toText` produces identical bytes from
identical streams. The stream is the canonical form; the bytes
are a realization.

`RawTextEmitter.emit : Catalog -> string` becomes a back-compat
wrapper for `statements >> Render.toText`. Existing callers
unchanged. Future emitters (Json, Distributions) inherit the
stream-output pattern as their typed structured form.


## 2026-05-28 — Session 34 / A36 cash-out: bulk-vs-incremental is realization-layer policy

Session 34 cashed A36 candidate. `Deploy.executeStream` consumes
a `seq<Statement>` and folds consecutive `InsertRow` runs
(matching `(TableId, columnShape)`) into `SqlBulkCopy` batches
(KeepIdentity, KeepNulls). Non-InsertRow statements (DDL,
SetIdentityInsert) flush via a text-batch path. The realization
layer chooses between bulk and per-row INSERT based on a single
policy parameter (`DefaultBulkBatchSize`); the algebra at the
stream level is invariant.

**A36 in operational form**: the same Π output produces both
the diffable .sql text (via `Render.toText`) and the bulk-
deployed target database (via `Deploy.executeStream`). Two
realizations; one canonical stream. T1 byte-determinism on the
stream level subsumes the realization-layer choice — bytes-out
from `toText` are deterministic, observable post-state from
`executeStream` is deterministic; both rest on the stream's
statement-level determinism.


## 2026-05-28 — Session 34 / AsyncStream as V2's streaming primitive (sync-Core / async-adapter split)

Streaming on the async side of the pipeline uses `AsyncStream<'a> =
unit -> Task<'a option>` (pull-based, single-shot). New module
in `Projection.Adapters.Sql` carries `map / mapAsync / iter /
fold / toList / bufferUpTo / probe / batchesOf` combinators.
`ReadSide.readRowsStream : SqlConnection -> Kind ->
AsyncStream<StaticRow>` is the canonical row source.

Core stays sync (no Task in Core per the F#-pure-core
commitment). Adapters can stream; Core-resident algorithms
(e.g., `RowDigester`) consume materialized values, with
`bufferUpTo` and `toList` providing the bridge.

The eight-of-eleven-combinators-unused state is acknowledged
(per session-36 audit Agent 4 #12); retained for chapter-4
data-triumvirate consumer pressure or retracted at chapter-4
close if pressure doesn't materialize.


## 2026-05-28 — Session 34 / Bench stream observability — `streamProbe` / `AsyncStream.probe` first-class

The bench surface gains streaming-aware probes:
- `Bench.streamProbe : string -> seq<'a> -> seq<'a>` (sync,
  Core).
- `AsyncStream.probe : string -> AsyncStream<'a> ->
  AsyncStream<'a>` (async, in Adapters.Sql).

Each records `<label>` (total ms across enumeration) and
`<label>.elements` (count) at upstream EOF. Pass-through with
RAII-shaped semantics. Used at four+ sites (RawTextEmitter,
Render.toText, Deploy.executeStream, ReadSide.readRowsStream).

**Stream observability is first-class.** The bench surface
isn't just per-function; it's per-stream-stage. Operators
reading the bench table see throughput per realization layer
(Π statement stream → executeStream input → bulk.copyRows
batches) on the same run.


## 2026-05-29 — Session 35 / Bulk fixture loader as test-side realization

`Deploy.runWideCanaryWithLoader : (SqlConnection -> Task<unit>) ->
(Catalog -> seq<Statement>) -> Task<Result<WideCanaryReport>>`
parameterizes the source-loader over a function instead of a SQL
string. `runWideCanary` becomes a thin wrapper. Test fixtures
expose `BulkSeeds : StaticTableSeed list` for typed bulk
seed-loading via `Bulk.copyRows` (SqlBulkCopy with KeepIdentity).

Source loading at 500k rows: 585s → 3.7s (157× speedup).
Bench-driven; the text-INSERT path was the wall, the bulk path
removed it entirely. Bulk + text variants coexist as parallel
test surfaces.


## 2026-05-29 — Session 35 / RowDigester streaming digest as chapter-4 row-axis seam

`RowDigester` lives in Core, sync, commutative-monoid carrier.
`State = { Count : int64; Acc : byte[] }` (32-byte sum mod
2^256 of per-row SHA256s); `add` and `finalize` are the
operations. Order-independent: streaming order doesn't matter.

`PhysicalSchema.RowDigests : Set<PhysicalRowDigest>` is the
fourth axis (`{ Schema; Table; Count; AggregateHash }`). Today
populated only by digest-aware producers; the per-row Set
remains for small inline rows.

**Scaffolding for chapter 4.1.** Transactional rows (>100k per
table) won't fit `Modality.Static`'s materialization budget;
chapter 4.1's data triumvirate uses `RowDigester` to fold rows
through a streaming pass without holding them in IR memory.


## 2026-05-29 — Session 35 / Big-O codification — HashSet diff, Result.aggregate, parallel hashing, lifted FK Maps

Session 35's Big-O agent identified ~27 algorithmic findings;
the high-leverage subset shipped:

- **`Result.aggregate` helper**: replaces the `xs @ [x]` O(N²)
  fold pattern at 4 ReadSide sites. Aggregates errors across
  the sequence rather than short-circuiting.
- **`ResizeArray` for `kindsWithRows` accumulator**: replaces
  `xs <- xs @ [y]` O(N²) prepend in the readside row-loading
  loop.
- **`HashSet.ExceptWith` for `PhysicalSchema.diff`**: replaces
  `Set.difference` at the 8 axes; matters when canaries fail
  with millions of mismatched rows.
- **`SHA256.HashData` static API**: drops per-row instance
  allocation (1M garbage objects/run at 500k-row scale).
- **`Array.Parallel.map` for row hashing**: SHA256 is CPU-bound,
  embarrassingly parallel; 1M hashes in 2.5s on multi-core.
- **`Map<SsKey, Kind>` and `Map<SsKey, Attribute>` lifted once**
  at `PhysicalSchema.toPhysicalForeignKeys` and
  `RawTextEmitter.fkDef`. FK projection: O(K · R) catalog scans
  → O(R) hash lookups (~450k linear ops → ~1500 hashed).

Result: 500k-row warm canary 610s → 27s (22.6× speedup).

**The discipline**: Big-O analysis at chapter close flags
algorithmic violations against the codebase's own scale targets.
Each finding ships independently, each ships with a measured
delta.


## 2026-05-30 — Session 36 / Five-agent DDD/Hexagonal/FP audit protocol

Chapter-close audit dispatched five agents in parallel covering
tightly orthogonal concerns:

| Agent | Lens |
|---|---|
| 1 | Ubiquitous language & bounded contexts |
| 2 | Hexagonal architecture (ports / adapters / dependency direction) |
| 3 | DDD aggregates / entities / value objects / invariants |
| 4 | FP composition primitives & algebraic structures |
| 5 | V1↔V2 anti-corruption layer fidelity |

Each agent classifies findings as **B&W** (objectively a leak;
no design judgment needed) vs **SUBJ** (judgment call), and
ranks **H/M/L** for refactor leverage.

**Convergence map** — multi-axis confirmation as confidence
signal — is the synthesis primary surface. Worked examples in
the audit (preserved at `AUDIT_2026_05_DDD_HEXAGONAL_FP.md`):
3-axis confirmation on TableId-lift, identity-vocabulary leak,
SSDT-vocabulary collapse; 2-axis on type-correspondence ownership,
declared-ports-unrealized.

**Tier 1/2/3/4 backlog discipline** organizes findings by
epistemic level + leverage. Tier 1 = B&W H (act without
ceremony); Tier 2 = B&W M; Tier 3 = SUBJ H (decisions for
operator); Tier 4 = SUBJ M.

**Audits are routed, not piled.** ~30 findings; 10 acted on at
session 36 (B&W ship-without-ceremony subset); ~20 routed to
named sub-chapters (3.2 / 3.5 / 4.1 / 4.2) with explicit
pre-scope alignment. The discipline: audit findings become named
items in named chapters with named pre-scopes, not a TODO list.


## 2026-05-30 — Session 36 / Coordinates bounded context — TableId lifted to Core

`TableId = { Schema : string; Table : string }` lives in
`Projection.Core/Coordinates.fs` with smart constructor
(`TableId.create` rejects blanks) and a canonical
`TableId.qualified` rendering (`"[schema].[table]"`).
`PhysicalRealization` aliased to `TableId`; SSDT-local `TableId`
in `Statement.fs` retired; `Bulk.copyRows` and
`Render.tableQualified` delegate to `TableId.qualified`.

**Stage 1 of Coordinates.** Stage 2 (typed `SchemaName` /
`TableName` / `ColumnName` value objects) deferred — would
require ripple updates at ~64 sites (`kind.Physical.Schema` /
`.Table` / `a.Column.ColumnName` reads). Defer until a real
consumer pays for the explicit `value` projections.

The lift is multi-axis confirmed by chapter-3.1 audit
(Agent 1 #1/#2, Agent 2 #19, Agent 3 #1/#2/#3) — strongest
B&W finding in the audit.


## 2026-05-30 — Session 36 / Aggregate smart constructors — Catalog.create / Module.create with referential-integrity invariants

`Module.create : SsKey -> Name -> Kind list -> Result<Module>`
enforces "Kind SsKeys disjoint within the module" (A11 cell
invariant). `Catalog.create : Module list -> Result<Catalog>`
enforces five invariants in one pass with errors aggregated:

1. Module SsKeys disjoint (A11).
2. Kind SsKeys disjoint across all modules.
3. Every `Reference.SourceAttribute` exists on its owning Kind.
4. Every `Reference.TargetKind` exists in the catalog.
5. Every `Index.Columns` SsKey exists on its owning Kind.

Existing record-literal construction continues to work
(back-compat); `create` is the gated entry consumers flow
through to make invariants structural.

`RawTextEmitter.fkDef` / `PhysicalSchema.toPhysicalForeignKeys`
previously each silently dropped on dangling references; the
invariants now live with the type, not in the consumer.

Companion: `ColumnProfile.create` enforces `0 ≤ NullCount ≤
RowCount` (chapter-3.1 audit Agent 3 #20). `NullabilityRules`
divides without precondition; `create` is the structural
substitute.


## 2026-05-30 — Session 36 / Topological-sort harmonization via SelfLoopPolicy

`RawTextEmitter.emissionOrder` previously re-implemented Kahn's
algorithm. `TopologicalOrderPass` already provided one. Divergent
on a single axis: the pass treated self-loops as 1-node SCCs
(cycle path); the emitter skipped them since SQL Server allows
inline self-FK constraints in CREATE TABLE.

Resolution: `SelfLoopPolicy = TreatAsCycle | SkipSelfEdges` DU
in `TopologicalOrder.fs`; `TopologicalOrderPass.runWith :
SelfLoopPolicy -> Catalog -> Lineage<TopologicalOrder>` produces
both projections from one algorithm. `run` defaults to
`TreatAsCycle` (existing pass semantics); `RawTextEmitter`
consumes `runWith SkipSelfEdges` and uses the resulting `Order`
field directly.

**Harmonization-via-parameterization** as a meta-pattern:
single-axis-divergent implementations earn one parameterized
algorithm. Same algorithm; multiple projections; consumers
choose. Worked example for chapters ahead.

A33 (deterministic-ordered schema emission) is satisfied
structurally — same algorithm, two projections.


## 2026-05-30 — Session 36 / Writer-fidelity codification — LineageDiagnostics.tellDiagnostics is canonical

Three pass drivers (`NullabilityPass:192`, `UniqueIndexPass:182`,
`ForeignKeyPass:255`) hand-built `{ Value = { Value =
lineage.Value; Entries = entries }; Trail = lineage.Trail }`
records, bypassing `LineageDiagnostics.tellDiagnostics` /
`ofLineage`. Session 36 adopted the writer's API at all three
sites.

Plus: `Lineage.ofValueAndEvents : LineageEvent list -> 'a ->
Lineage<'a>` extracted as the canonical "value + trail in one
shot" primitive. Replaces `Lineage.tellMany events
(Lineage.ofValue x)` at 6 terminal-event passes. Two-consumer
threshold for this shape was crossed at session 8 and never
closed.

**Manual writer-state construction is forbidden.** Pass
drivers MUST use `LineageDiagnostics.tellDiagnostics` /
`Lineage.ofValueAndEvents`. The dual-writer's algebraic surface
is now activated for the first time across the actual passes
that produce both decisions and diagnostics. Future pass
drivers inherit the discipline.


## 2026-05-30 — Session 36 / Adapter alias retired from Core

`type Adapter<'source, 'inner> = 'source -> Task<Result<'inner>>`
in `Projection.Core/Types.fs` opened `System.Threading.Tasks` in
Core, contradicting the load-bearing F#-pure-core / no-Task-in-
Core commitment. The alias had no consumers in Core code (only
a Stage-0 reservation test in `TypesTests.fs` referenced it).

Resolution: alias retired; `System.Threading.Tasks` import
removed from `Types.fs`; the test reservation rewires to a
bare task-shaped signature `string -> Task<Result<int>>`.
Adapters at the boundary declare their task-shaped signatures
inline.

**Closed-DU expansion empirical-test discipline applies to
ports too.** The fix isn't to add more port declarations — it's
either to *realize* with a real consumer or to *retire* the
declaration. Worked example: this alias retired with no
consumer; the chapter-3.5 Π port realization will *realize*
the `Emitter<'element>` declaration with three real consumers.


## 2026-05-30 — Session 36 / Lazy Docker JIT bring-up at the test boundary

`Deploy.Docker.ensureRunning : unit -> bool` probes
responsiveness via `docker version` (active probe; 2s ceiling)
and best-effort spawns `sudo dockerd` with poll-until-ready
(named constants: `BringupBudgetMs = 30000`, `BringupPollMs =
200`). Tests' `skipIfNoDocker` switched from `isAvailable`
(static socket-file probe) to `ensureRunning` so a mid-session
daemon drop no longer turns canary tests into spurious failures.

The poll loop is *poll-until-ready*, not a fixed wait. Budget
consumes only when the daemon genuinely failed to start.
Constants justified by empirical bring-up time (1-3s typical;
10× p99 = 30s ceiling).

Production code Deploy.Docker module owns the bring-up.
Test-side `skipIfNoDocker` consumes it. The session-start hook
remains the primary bring-up path; `ensureRunning` is the
mid-session safety net.


## 2026-05-09 — Chapter 3.5 open / strategic frame for the Π port realization runway

Chapter 3.5 opens. Per `DECISIONS 2026-05-15 — Strategic frame
for the OSSYS implementation chapter`, multi-session chapters
earn a chapter-open document at chapter open; chapter 3.5
inherits the discipline. The chapter-open document at
`CHAPTER_3_5_OPEN.md` names eight strategic-frame axes:
(1) hexagonal port realization vs declaration; (2) DDD T11 as
type theorem; (3) FP composition over the port; (4) streaming /
A35 at the per-kind boundary; (5) Big-O O(N log N) per-key
composition; (6) `Emitter<'element>` as canonical primitive;
(7) ACL unaffected (V2-internal architectural realization);
(8) two-consumer threshold for richer per-element types.

The chapter's substantive deliverable is `RefactorLogEmitter`
over `CatalogDiff` (per `CHAPTER_3_PRESCOPE_REFACTORLOG_AND_CATALOG_DIFF.md`).
The chapter-3.5 pre-scope §1 named the pre-condition: the typed
`Emitter<'element>` port realized for the existing three sibling
Π's *before* the diff-typed fourth sibling lands. Otherwise
`RefactorLogEmitter` ships into a port whose shape was only
declared, never inhabited; the asymmetry the chapter is meant
to eliminate (T11 prose vs T11 type) survives the chapter that
was supposed to retire it.

Chapter 3.5 therefore opens with the Π port realization as its
first slice arc — chapter-3.1 audit Tier-1 #7's deferred item
becomes chapter 3.5's runway.


## 2026-05-09 — Chapter 3.5 slices α / β / γ / δ — Π port realization shipped

Three sibling Π's converge on the typed `Emitter<'element>`
port shape. The realization arc:

- **Slice α — `RawTextEmitter.emitSlices : Emitter<Statement
  list>`**. Per-kind value is `Statement list` per A35 (Π's
  canonical output is a typed deterministic statement stream).
  The legacy `statements` and `emit` realizations route through
  the typed seam; the `composeFromArtifact` private weaver
  reconstitutes the whole-catalog stream by adding preamble +
  module-header transitions + per-kind slices in topological
  order via `TopologicalOrderPass.runWith SkipSelfEdges`.

- **Slice β — `JsonEmitter.emitSlices : Emitter<string>`**.
  Per-kind value is the kind's JSON object as compact UTF-8
  text (`writeKind` rendered through a depth-0 `Utf8JsonWriter`
  with `Indented = false`). Composer in `emit` re-parses each
  per-kind fragment via `JsonNode.Parse` and writes through the
  indented document writer so depth-tracking matches the
  surrounding catalog document. Property insertion order is
  preserved by `JsonObject` so the round trip is
  byte-deterministic.

- **Slice γ — `DistributionsEmitter.emitSlices :
  EmitterWithProfile<string>`**. First realization of
  `EmitterWithProfile<'element>` (`Types.fs:55`); same
  compact-then-indent compositional pattern as JsonEmitter.
  The diff-typed sibling `EmitterOverDiff` realizes when
  chapter 3.5's substantive deliverable (`RefactorLogEmitter`)
  lands on the runway after this slice arc.

- **Slice δ — T11 type-theorem worked examples; substring
  discipline retired**. New `T11TypeTheoremTests.fs` (renamed
  `SiblingEmitterContractTests.fs` at chapter 3.7 slice ε per
  `DECISIONS 2026-05-10 — Domain-first naming`) carries
  three per-emitter `emitSlices key-set equals Catalog.allKinds`
  tests + one cross-emitter sibling-commutativity test.
  Substring enforcement at `JsonEmitterTests.fs:96-105` and
  `RichProfilingEndToEndTests.fs:280-289` retired with
  pointer-comments naming the structural successor. Surviving
  T11 tests at `JsonEmitterTests.fs` (physical realization) and
  T4 tests stay — they test rendering invariants, not kind
  coverage.

**T11 amended (cashed, 2026-05-09).** AXIOMS.md amendment
filled — T11 stops being substring discipline and becomes a
type theorem. The smart constructor on `ArtifactByKind` is the
load-bearing surface; any two `ArtifactByKind` values built
from the same Catalog have equal keysets by construction.

**Two-consumer threshold honored on per-element types.**
Per-kind values stay simple (`Statement list`, `string`,
`string`) for first slice; richer types (`JsonObject` for Json,
typed `DistributionSlice` for Distributions, `seq<Statement>`
for the bulk-aware realization in chapter 4.1) earn their place
under second-consumer pressure (DacpacEmitter, drift detection,
data triumvirate).

**Big-O.** `ArtifactByKind` is `Map<SsKey, _>` — O(log N)
lookup; smart-constructor's set-difference is O(N log N) where
N = catalog kinds. Per-key composition through
`composeFromArtifact` walks the topological order O(N) times
with O(log N) Map lookups → O(N log N). Same shape as legacy
implementation; structurally right for chapter-4.4 drift
detection where `Map<SsKey, DriftKind>` replaces today's
byte-string-diff (Appendix H §H.8).

**Test count at slice δ close**: 707 fast-suite + 8 T11-specific
+ 4 canary round-trip = 719 tests green at byte-identity.

**Chapter-3.1 audit Tier-1 #7 closed**: declared `Emitter
<'element>` port realized at three concrete consumers; closed-
DU expansion empirical-test discipline applies to ports too —
the audit's prescription was either *realize* or *retire*; this
slice arc realized.

**Forward signal.** Chapter 3.5's substantive deliverable
(`CatalogDiff.between` + `RefactorLogEmitter` +
`Render.toRefactorLogXml`) opens after this slice arc lands.
Inherits the realized seam; T11 trivializes for the diff-typed
sibling via the `EmitterOverDiff<'element>` shape already
declared at `Types.fs:62-63`.


## 2026-05-09 — Chapter 3.5 slices ζ / η / θ / ι — substantive deliverable shipped

The chapter 3.5 substantive deliverable lands per
`CHAPTER_3_PRESCOPE_REFACTORLOG_AND_CATALOG_DIFF.md`. Four slices
on the runway opened by the Π port realization (slices α–δ):

- **Slice ζ — `CatalogDiff.between`**. Stage 0's `CatalogDiff = |
  Pending` placeholder retired; the real exhaustive type and
  smart constructor live in `Projection.Core/CatalogDiff.fs`.
  `Renamed | Added | Removed | Unchanged` partitions
  `Catalog.allKinds source ∪ Catalog.allKinds target` exhaustively
  by construction. Cashes A38 (CatalogDiff exhaustiveness, see
  `AXIOMS.md`).

- **Slice η — `UuidV5.create`**. RFC 4122 §4.3 name-based UUID
  derivation lands at `Projection.Core/UuidV5.fs`. Pure byte-
  twiddling: namespace + name → SHA-1 → 16-byte truncation →
  version/variant bit-set → endian-roundtrip back to .NET Guid.
  Verified against three canonical vectors. The deterministic-
  Guid primitive enables `RefactorLogEmitter`'s `OperationKey`
  derivation.

- **Slice θ — `RefactorLogEmitter`**. The fourth sibling Π —
  `EmitterOverDiff<RefactorLogEntry list>`. First substantive
  consumer of `EmitterOverDiff<'element>` (declared at
  `Types.fs:62-63`); first realization of `CatalogDiff`'s
  consumer side. T11 amended again (diff-typed inputs) cashes
  here — `ArtifactByKind` smart constructor binds the artifact
  keyset to the diff's *target* Catalog. First-slice scope:
  kind-level renames produce one `SqlTable` entry per renamed
  kind (column-level renames defer under closed-DU expansion
  empirical-test discipline). `OperationKey` derivation via
  `String.concat ":"` over a typed component list rather than
  `sprintf` per the no-string-concatenation discipline.

- **Slice ι — `RefactorLogRender.toRefactorLogXml`**. Pure XML
  rendering through the BCL's `XmlWriter` typed API. No string
  concatenation, no `sprintf` on XML fragments. Determinism axes
  pinned: operations sorted by `OperationKey`, `ChangeDateTime`
  pinned to `2000-01-01T00:00:00Z` (DacFx ignores it for refactor
  application; chapter 3.5 prescope §6 option 1), UTF-8 without
  BOM, `\n` newlines, two-space indent, namespace pinned per the
  SSDT 2012/02 schema URI.

**T1 byte-determinism on the rendered .refactorlog**: every byte-
affecting variable is pinned. Verified by 10× repeat invocation
in `RefactorLogRenderTests.fs:T1`. Tests use `XDocument` (built-in
BCL DOM) for structural assertions — the no-string-concatenation
discipline applies to test verification too.

**AXIOMS amendments cashed at chapter 3.5 close (slice κ)**:
T11 amended again (diff-typed inputs); A1 amended (four-variant
SsKey — Stage 0 + chapter 3.5); A38 promoted from candidate (the
exhaustiveness invariant of CatalogDiff).

**Test count**: 743 fast-suite passing. New tests: 9
(CatalogDiff) + 8 (UuidV5) + 8 (RefactorLogEmitter) + 9
(RefactorLogRender) = 34 added across slices ζ / η / θ / ι.

**Big-O at chapter close**:
- `CatalogDiff.between`: O(N log N) where N = |source ∪ target|.
- `RefactorLogEmitter.emit`: O(N log N) where N = |target kinds|;
  per-kind `Map.tryFind` over renames is O(log R).
- `RefactorLogRender.toRefactorLogXml`: O(R log R) where R =
  |renames|; sort dominates; XML writer is O(R) on the sorted
  stream.

**Forward signals.**
- **Chapter 3.4 — `renameSurvives` predicate library.** Now
  unblocked: the diff + emitter + renderer cash the property's
  evidence side. Pre-scope at
  `CHAPTER_3_PRESCOPE_CANARY_PROPERTY_SURFACE.md`.
- **Chapter 3.2 — `SnapshotRowsets` + `OssysOriginal` SsKeys at
  scale.** Lifts A1's bound from `Synthesized` to `OssysOriginal`
  for kinds with V1 SSKey Guids. Pairs naturally with
  `RefactorLogEmitter` — once rowsets arrive, renames over
  `OssysOriginal` keys flow through the diff cleanly; renames
  over `Synthesized` keys remain bounded (renames produce different
  `Synthesized` SsKeys; diff classifies as Removed + Added).
- **Chapter 4.2 — User FK reflow + `V1Mapped` reach.** The diff-
  side cross-version threading uses `SsKey.identityKey` (UUIDv5
  derivation through `UuidV5.create`); chapter 4.2 makes the
  variant reachable from production input.
- **Chapter 4.4 — drift detection via `ArtifactByKind.compareWith`**.
  The pointwise per-key diff over `ArtifactByKind` returning
  `Map<SsKey, DriftKind>` replaces today's byte-string-diff
  (Appendix H §H.8). The seam is open via the typed Π port +
  exhaustive diff.


## 2026-05-09 — No-string-concatenation / no-regex discipline (codifying)

V2 commits: avoid string concatenation (`+`, `sprintf` building
structured values, `String.Format`) and regex
(`System.Text.RegularExpressions`); prefer built-in writers and
parsers (`String.concat`, `String.Split`, `XmlWriter`,
`Utf8JsonWriter`, `JsonNode`, `StringBuilder` where the algorithm
genuinely needs incremental construction).

**Rationale.**

- Structured values built via format strings drift from their
  parsers — the codebase's own `Identity.fs:122` round-trip
  (`SsKey.synthesized "OS_KIND" basis` builds via `sprintf
  "%s_%s"`; `SsKey.original "OS_KIND_..."` parses via
  `StartsWith(src + "_", …)`) is the canonical example. Typed
  builders + typed parsers eliminate the round-trip drift.
- Regex makes intent opaque and is hard to reason about for
  determinism. The codebase's only regex (singleton at
  `Deploy.fs:216-220` for `^\s*GO\s*$` batch-splitting) is
  retiring at the next slice (`String.Split('\n')` + `Trim` +
  literal compare).
- Built-in BCL writers (`XmlWriter`, `Utf8JsonWriter`,
  `JsonNode`) handle escaping, encoding, and namespace concerns
  by construction; T1 byte-determinism is preserved by pinning
  the writer's settings rather than reasoning about every escape
  branch.

**Operational consequences.**

- Pre-existing `sprintf` sites stay (back-compat); new code
  defaults to `String.concat` over typed component lists. Worked
  example: `RefactorLogEmitter.renameOperationKey` composes via
  `["rename"; rootOriginal; oldName; newName] |> String.concat
  ":"` rather than `sprintf "rename:%s:%s:%s" ...`.
- New XML, JSON, SQL emission routes through typed BCL writers
  exclusively. `RefactorLogRender.toRefactorLogXml` is the
  worked example for XML.
- `System.Text.RegularExpressions` is banned in new code; the
  Deploy.fs:216 violation is the audit-Tier-1 carry-forward.
- Lint guardrail: `scripts/lint-discipline.sh` (chapter-3.5
  follow-on slice) runs in CI / pre-commit and greps for
  `sprintf` / `RegularExpressions` / string-`+` in production
  code paths under `Projection.Core/`. The audit (`Codebase
  determinism + non-built-in audit`, 2026-05-09) named the
  Tier-1 follow-on backlog; the script catches regressions
  going forward.

**Audit-deferred (Tier-1) sites carried into this discipline:**
- `Deploy.fs:216-220` regex — retire to `String.Split` +
  literal compare. ✅ slice λ.1
- `Deploy.fs:344-346` string `+` SQL fragment build — replace
  with `String.concat`. ✅ slice λ.1
- `Deploy.fs:203` `sprintf "CREATE DATABASE [%s];"` — typed
  helper. ✅ slice λ.2
- `Render.fs:16,30,34,38,39,62,64,67` — quote/type/literal
  `sprintf`s and `"0x" + raw` plain `+`. ✅ slice λ.1
- `Deploy.fs:176` `Guid.NewGuid()` — non-determinism leak;
  reified as `DatabaseNameGenerator` typed seam. ✅ slice λ.2
- `Deploy.fs:555-630` six `let mutable` accumulators — refactored
  to `runSourcePhase` / `runTargetPhase` pure-async phase
  functions with typed `PhaseOutcome` return. ✅ slice λ.2
- `CatalogReader.fs:104-127` synthesis-basis `sprintf "%s_%s_%s"`
  + `Identity.fs:122,155` round-trip — retired via
  `SsKey.synthesizedComposite` typed-component constructor. ✅
  slice λ.3

The audit findings route to follow-on slices in chapter 3.5 close
or chapter 3.6 hygiene; tracking via `DECISIONS` so the deferral
doesn't recur silently (per the "Active deferrals re-checked at
chapter close" discipline).


## 2026-05-09 — Built-in obligation: when a BCL or vendor SDK emits the structure, use it (TSQL150Parser / ScriptDom / DacFx)

V2 commits a stronger form of the no-string-concatenation
discipline: **if there is a method (BCL, vendor SDK, or
established library) that emits the string we are trying to
emit, we are obliged to use it.** Hand-rolled string composition
of structured emission targets is forbidden when a typed
domain-aware emitter exists for the same target.

**Worked obligations (already adopted).**

- **JSON emission** — `System.Text.Json.Utf8JsonWriter` and
  `System.Text.Json.Nodes.JsonNode`. `JsonEmitter` /
  `DistributionsEmitter` (chapter 3.5 slices β / γ) rebuild from
  these BCL primitives; no manual `{ ... }` interpolation.
- **XML emission** — `System.Xml.XmlWriter`.
  `RefactorLogRender.toRefactorLogXml` (chapter 3.5 slice ι)
  uses `XmlWriter` exclusively; no manual `<Operation Name="…">`
  interpolation.
- **UUIDv5 (RFC 4122 §4.3)** — derived deterministically from
  `(namespace, name)` via `SHA1.HashData` + RFC bit-set, not via
  ad-hoc Guid construction.
- **Bracket-quoted SSDT identifiers** — `Render.quote` /
  `TableId.qualified` (`Coordinates.fs`) is the canonical
  joiner; consumers delegate.

**Worked obligations (deferred, chapter 3.7+ candidate).**

- **T-SQL emission** — `Microsoft.SqlServer.TransactSql.ScriptDom`
  (`TSql150Parser` for parsing; `Sql150ScriptGenerator` /
  `Sql160ScriptGenerator` for emission) provides the typed AST
  + script generator that V2's `Render.toSql` currently
  hand-rolls. Chapter 3.5's `Statement` DU is V2's algebraic
  pre-text form; ScriptDom's `TSqlFragment` hierarchy is the
  vendor-canonical pre-text form. The two ought to converge:
  V2's `Render` step would build `TSqlFragment` instances and
  delegate to `Sql150ScriptGenerator.GenerateScript`, with
  pinned `SqlScriptGeneratorOptions` for T1 byte-determinism.
  Adopting ScriptDom retires every remaining `sprintf` /
  `String.Concat` / `StringBuilder` use in `Render.fs`,
  `Deploy.fs:countUserTablesSql` / `createDatabaseSql`, and
  `ReadSide.fs`'s SELECT-builder. Estimated chapter scope:
  ~6-8 sessions, comparable to chapter 3.5.

- **DACPAC emission** — `Microsoft.SqlServer.DacFx`'s
  `TSqlModel` + `DacPackage` would become the typed
  pre-bytes form when DacpacEmitter (chapter 3.x deferred)
  ships. Same discipline.

**Operational rule.**

Before introducing string-composition for any structured emission
target (SQL, XML, JSON, YAML, refactor.log, .dacpac, etc.), the
author MUST ascertain whether a typed builder exists. If it does,
adopt it; if it doesn't, document the absence and the search
rationale before falling back to `String.Concat` /
`StringBuilder`.

**Cross-reference.** The audit (`Codebase determinism +
non-built-in audit`, 2026-05-09) named the SQL-emission paths
that the ScriptDom adoption would cleanest. Tracked at
`HANDOFF.md` "deferred-but-might-fire" list.

**Forward chapter.** Chapter 3.7 candidate — "ScriptDom adoption
for the SQL emission layer". Pre-scope to write at runway.
Inherits chapter 3.5's typed-Π port; the realization step
becomes a `TSqlFragment` build + `GenerateScript` call.
Substantive deliverable: every `Render.fs` / `Deploy.fs` SQL
text path replaced by ScriptDom-typed flow; T1 byte-determinism
verified at the script generator's pinned-options surface.


## 2026-05-09 — Lint guardrail: scripts/lint-discipline.sh

Per the audit's Lens 3 #2 recommendation, codifies a grep-based
guardrail at `sidecar/projection/scripts/lint-discipline.sh`
that fails CI / pre-commit on disallowed patterns in production
code paths under `src/`:

  - `System\.Text\.RegularExpressions` — banned namespace.
  - `\bsprintf\b` / `\bprintfn\b` / `\bprintf\b` in `Projection.Core/`
    (Core's purity discipline; sprintf is allowed in adapters
    where boundary code emits diagnostic strings).
  - String-`+` heuristic — `\b"\s*\+\s*` and `\s*\+\s*"`
    (catches `"x" + y` and `x + "y"` patterns).
  - `String\.Format\(` — banned alternative path.
  - `Guid\.NewGuid\(\)` outside the reified `DatabaseNameGenerator`
    seam in `Deploy.fs`.
  - `DateTime\.Now` / `DateTime\.UtcNow` outside `Bench.fs`
    (Core's no-time discipline).
  - `Random\.` outside test fixtures.

The script runs in <100ms over the V2 source tree; wired via
`.git/hooks/pre-commit` (local) and `.github/workflows/lint.yml`
(CI). Allowlisted via comment marker `// LINT-ALLOW: <reason>`
on the offending line.

**Escalation tier**: any new violation requires either (a) a
`LINT-ALLOW` marker with rationale or (b) a paired DECISIONS
amendment naming why the discipline is being relaxed for the
specific site. The script is the load-bearing guard against
discipline drift.


## 2026-05-09 — FP strict mode discipline (mutation reified, overflow checked, immutable-by-default)

V2 commits a hardline FP discipline beyond the no-string-
concatenation rule: **immutable-by-default with first-class
escalation and reification of any mutative method use**.
Mutation is allowed where justified (algorithm-internal
accumulators, BCL-mandated mutable struct option-builders,
streaming-reader lifetime state machines), but every mutation
site is *reified at the file level* via a top-of-file
`LINT-ALLOW-FILE-MUTATION` marker that names the specific
audited rationale. New mutation sites without the marker fail
the lint guardrail.

**Three lint rules enforce mutation discipline (per
`scripts/lint-discipline.sh`):**

  - `let-mutable` — `let mutable` outside files marked
    `LINT-ALLOW-FILE-MUTATION` or `LINT-ALLOW-FILE`.
  - `mutable-collection` — `ResizeArray<` / `Dictionary<` /
    `HashSet<` / `Stack<` / `Queue<` /
    `ConcurrentDictionary<` / `ConcurrentQueue<` /
    `ConcurrentBag<` outside the same allowlist.
  - `set-assign` — `<-` assignment outside the same allowlist
    (catches both `let mutable` reassignment and BCL property
    setters).

**Audit-justified mutation files** (top-of-file marker; rationale
named per-file): `AsyncStream.fs` (pull-based streaming primitive),
`ReadSide.fs` (streaming reader lifetime), `Static.fs` /
`ProfileSnapshot.fs` / `ProfileStatistics.fs` (function-local
placeholders), `RawTextEmitter.fs` (currentModuleKey),
`JsonEmitter.fs` / `DistributionsEmitter.fs` (BCL JsonWriterOptions
struct), `RefactorLogRender.fs` (BCL XmlWriterSettings class),
`NamingMorphism.fs` / `NormalizeStaticPopulations.fs` /
`SymmetricClosure.fs` (pass-driver event accumulation), `UuidV5.fs`
(RFC 4122 byte-twiddling on local copy), `Deploy.fs` (Docker JIT
poll + bulk grouping), `Bulk.fs` (BCL SqlBulkCopy mutable surface),
`Bench.fs` / `PhysicalSchema.fs` / `Catalog.fs` / pass drivers /
`CycleResolution.fs` (already exempt via `LINT-ALLOW-FILE` for
the sprintf rule, which also covers mutation).

**Per-line allowlist** for surgical mutations in otherwise pure
files: `Result.fs:134-135` (the two ResizeArray accumulators
inside `Result.aggregate`'s pure aggregator) carry per-line
markers.

**Compiler strict mode (Projection.Core.fsproj):**

  - `<Nullable>enable</Nullable>` — null escapes fail compilation
    (already on).
  - `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` — every
    warning is a compile error (already on).
  - `<CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>`
    — arithmetic overflow / underflow elevates to runtime
    exception rather than silent wraparound. T1 byte-determinism
    requires that wraparound is observable (a programmer error);
    `<checked>` flag at MSBuild level pairs with
    `Projection.Core/UuidV5.fs`'s byte arithmetic and the
    `RowDigester` / `PhysicalSchema` aggregators. Adapters /
    Pipeline don't carry the flag — Pipeline-side counts fit
    `int64` and BCL surface invokes can be unchecked by design.

**Reification rule:** every mutation site must be answerable to:
"why is mutation correct here (not observable from outside the
function); why is it justified (algorithmic necessity, BCL
constraint, performance evidence); and is it reified (encapsulated
behind a pure interface so callers can't observe it)?" The
top-of-file marker is the structural answer.

**Empirical baseline:** the audit (`Codebase determinism +
non-built-in audit`, 2026-05-09) found ~130 mutation sites in V2;
all mapped to one of the justified categories above. Adding a new
mutation site requires either (a) extending an existing category's
file marker, (b) adding a new file marker with audited rationale,
or (c) opening a paired DECISIONS amendment. Lint guardrail
catches drift in CI / pre-commit.

**Forward chapter.** Chapter 3.7 candidate (ScriptDom adoption
for SQL emission) retires `Render.fs`'s `StringBuilder` mutation
by routing through ScriptDom's typed AST + `Sql150ScriptGenerator`.
Chapter 4 candidate (typed `Outcome.toDiagnosticString` per DU)
retires the pass-driver `%A` formatters. Each substantive
chapter compounds the discipline.

**Out of scope (re-open triggers explicit):**

  - F# Analyzers SDK custom analyzer — would catch syntax-tree
    patterns the grep heuristic misses (e.g., `member val X = …
    with get, set` mutable properties; reflection-based
    mutation). Re-open trigger: lint-script false-negative
    surfacing in CI (a real mutation site shipping without
    catching). Estimated cost: a separable chapter.
  - `[<Sealed>]` / `[<NoComparison>]` / `[<NoEquality>]` blanket
    application — over-tightens types where structural defaults
    are correct. Per-type adoption when an invariant breaks
    "structural equality = semantic equality" (audit Lens-2
    trigger).


## 2026-05-09 — Reified-primitive pattern: dry up LINT-ALLOWs by extracting typed-opaque mutation surfaces

The audit-justified mutation files were discovered to share two
recurring shapes: pass-driver event accumulation (3 files used
`ResizeArray<LineageEvent>` directly) and BCL writer-options
construction (3 files mutated `JsonWriterOptions` /
`XmlWriterSettings` builders). Each shape is reified into a single
typed primitive whose mutation lives EXCLUSIVELY in that primitive's
module — the consumers become marker-free.

**`Projection.Core/LineageBuffer.fs`** — `LineageBuffer.Buffer` is
a `private` DU wrapping `System.Collections.Generic.List<
LineageEvent>`. Module API: `create` / `add` / `addMany` / `toList`
/ `isEmpty` / `count`. The opaque `private` constructor enforces
that consumers cannot inspect or mutate the underlying List;
mutation is type-invisible from the consumer's perspective. Three
pass drivers (`NamingMorphism`, `NormalizeStaticPopulations`,
`SymmetricClosure`) consume this primitive and lose their
`LINT-ALLOW-FILE-MUTATION` markers (no direct mutation in their
files).

**`Projection.Core/PinnedWriting.fs`** — `JsonOptions.indented` /
`JsonOptions.compact` / `XmlSettings.indentedUtf8NoBom` are
factory functions returning fresh BCL option-builder instances
with V2's pinned-deterministic settings (UTF-8 no-BOM, LF newlines,
two-space indent). Each call yields an independent instance; the
mutable struct/class surface is fully encapsulated. Three emitters
(`JsonEmitter`, `DistributionsEmitter`, `RefactorLogRender`)
consume this primitive and lose their `LINT-ALLOW-FILE-MUTATION`
markers.

**SymmetricClosure pure F# fold refactor** — the `let mutable
inversesByTarget : Map<…>` retired. The symmetric-closure
construction is reified as a `Step` DU (`NoOp | Skip ev | Created
(ev, targetKey, inverse)`) classifier function plus a `Seq.fold`
over `(sourceKind, reference)` pairs accumulating
`(events, inversesByTarget)` immutably. No mutation in the file.

**Net effect on the LINT-ALLOW landscape:** five
`LINT-ALLOW-FILE-MUTATION` markers retired (3 pass drivers + 3
emitters minus the 2 new shared modules' markers); the consumer
surfaces stop carrying the discipline-relaxation. The FP discipline
strengthens at the *type level* — mutation is reified into named
typed-opaque primitives rather than excused per-consumer.

**Pattern.** When N≥3 consumer files share the same mutation
shape and the mutation has a clean typed-opaque interface, extract
to a single named module under `Projection.Core/`. The module
carries one `LINT-ALLOW-FILE-MUTATION` marker; the N consumers
become marker-free. Per the two-consumer threshold + N=3 trigger,
extraction earned at audit close. Future repetitions of the
recurring-pattern audit drive further drying.


## 2026-05-09 — FP strict mode regression preventatives: 5 new lint rules

The audit confirmed Core is already clean of these patterns; the
rules are regression-preventative (catch new violations before
they ship). Each rule names the discipline it enforces:

  - **Rule 11 — `core-failwith`**: bans `failwith` / `failwithf`
    in `Projection.Core/`. Core uses `Result<'a>` for typed error
    paths; only `invalidOp` / `invalidArg` / `nullArg` (BCL-
    convention preconditions) are allowed for unreachable /
    invariant-breach branches. Untyped `failwith` raises bare
    `Exception` (no semantic routing for callers); the Result
    discipline rejects it.

  - **Rule 12 — `core-async-block`**: bans `async {` / `task {`
    blocks in `Projection.Core/`. Core is synchronous by design
    (T1 byte-determinism requires deterministic execution; async
    introduces scheduler nondeterminism). Adapters at the
    boundary may use these freely.

  - **Rule 13 — `core-concurrency-primitive`**: bans `Task.Run`
    / `Task.Delay` / `Thread.Sleep` / `Task.WaitAll` /
    `Task.WaitAny` / `Task.Wait` in `Projection.Core/`.
    Concurrency primitives belong in adapters; Core's purity
    discipline forbids them.

  - **Rule 14 — `type-erasure`**: bans `box` / `unbox` in
    production code (Core + adapters). Explicit type erasure /
    recovery is incompatible with F#'s closed-DU + generic
    dispatch discipline. Today: zero occurrences; rule prevents
    future regression.

  - **Rule 15 — `mutable-record-field`**: bans
    `mutable Foo : T` field declarations in record types
    anywhere in production. F# records are immutable by default;
    mutable record fields break structural-equality + T1 byte-
    determinism (two records with the same field values can
    drift if one's field is later mutated). Function-local
    `let mutable` is the allowed mutation surface; mutable
    record fields aren't.

**Operational consequence.** The `scripts/lint-discipline.sh`
script now enforces 15 rules over `src/`. Today's clean state
sets the baseline; any new violation requires either
refactoring or a paired DECISIONS amendment with rationale.


## 2026-05-09 — Extended lint discipline: hexagonal coupling, Big-O anti-patterns, regression-preventatives, pre-commit hook

V2's lint guardrail extends from 15 rules to 22, broadens scope
beyond Core where the discipline applies broadly, fixes a build-
output false-positive in scan paths, and ships a pre-commit hook
+ CI workflow so the guardrail runs at *every* commit and PR.
Per the user's principle: **"default to explicit acknowledgement
of deviance"** — every legitimate exception carries an explicit
`LINT-ALLOW` marker (per-line) or `LINT-ALLOW-FILE` /
`LINT-ALLOW-FILE-MUTATION` marker (top-of-file) with rationale.

**Lint script fixes:**

  - **Build-output exclusion.** All scans now use
    `--exclude-dir=obj --exclude-dir=bin --exclude-dir=.git`,
    eliminating false positives on generated `*.AssemblyInfo.fs`
    files (had reported 8 spurious `System.Reflection`
    occurrences).

**Extended scope:**

  - The `string-format` / `random-banned` / `datetime-now` /
    `guid-newguid` / `type-erasure` / `mutable-record-field`
    rules continue to apply broadly across `src/`. The
    `core-sprintf` / `string-plus` / `core-failwith` /
    `core-async-block` / `core-concurrency-primitive` rules
    remain Core-scoped (boundary code legitimately uses
    `sprintf` / `task {`-blocks for SQL emission, etc.; the
    discipline holds at Core).

**Three new opinionated rules (regression-preventative + audit-
backed):**

  - **Rule 16 — `nowarn-banned`**: bans `#nowarn` directive
    anywhere in production. Silencing a compiler warning is
    explicit-deviance bypass; the discipline is to fix the
    underlying issue or open a paired DECISIONS amendment, not
    to selectively silence per-file. Today: zero occurrences.

  - **Rule 17 — `reflection-banned`**: bans `open
    System.Reflection` in production. Reflection is consciously
    deferred per `CLAUDE.md` F# feature surface; the closed-DU +
    typed-seam codification means dispatch is type-checked at
    compile time, not discovered at runtime.

  - **Rule 18 — `obj-typed`**: bans `obj`-typed parameters /
    returns (`: obj )`, `: obj ->`, `: obj` at line-end). F#
    closed-DU + generic dispatch is the structural alternative;
    `obj` is type erasure dressed as a parameter.

**One new opinionated rule (Big-O):**

  - **Rule 19 — `big-o-list-append-singleton`**: bans `xs @ [x]`
    list-append-singleton anywhere in production. `List.append`
    is O(N); inside a fold it grows O(N²). Idioms: `x :: xs`
    + `List.rev` at consumption time, OR `Result.aggregate`
    (V2's reified accumulator), OR `LineageBuffer` (typed
    opaque accumulator). The audit's chapter-3.1 close cashed
    `Result.aggregate` as the Big-O fix for Result-fold
    aggregation; this rule structurally enforces the migration.
    Writer-monad `tell` (Lineage / Diagnostics) carries a
    per-line `LINT-ALLOW` because `tell` is the algebraic
    primitive (terminal annotation), not a fold accumulator.
    `LineageBuffer` is the high-rate path.

**Three new opinionated rules (hexagonal architecture):**

  - **Rule 20 — `hex-core-coupling`**: Core files (under
    `Projection.Core/`) cannot `open Projection.<Adapters |
    Targets | Pipeline | Cli>`. Core has no V2 dependencies;
    the dependency direction Core <- Adapters / Targets <-
    Pipeline <- CLI is one-way. The compiler enforces this at
    the project-reference level; the lint catches accidental
    cross-namespace `open`s at the file level.

  - **Rule 21 — `hex-target-coupling`**: Target files (under
    `Projection.Targets.*/`) cannot `open Projection.<Adapters
    | Pipeline | Cli>` or cross-target `open
    Projection.Targets.<Other>`. Targets are horizontal
    siblings; cross-target coupling violates the hexagon's
    structural commitment.

  - **Rule 22 — `hex-adapter-coupling`**: Adapter files (under
    `Projection.Adapters.*/`) cannot `open Projection.<Targets
    | Pipeline | Cli>` or cross-adapter `open
    Projection.Adapters.<Other>`. Same horizontal-sibling
    discipline.

**Refactor: 6 `xs @ [x]` Big-O sites in `CatalogReader.fs`
retired.** Each fold-of-Result-list pattern (5 standard plus 1
`Option`-filtering variant) collapses to `Result.aggregate
<input>` (or `Result.aggregate <input> |> Result.map (List.choose
id)` for the Option case). Same Big-O profile (O(N)) without the
per-step append. The chapter-3.1 audit's Result.aggregate cash-
out is now structurally the only path through; the lint enforces.

**Pre-commit hook + CI workflow:**

  - `sidecar/projection/scripts/git-hooks/pre-commit` — bash
    hook that runs `lint-discipline.sh --ci` whenever staged
    files include any F# under `sidecar/projection/`. Skips
    cleanly when no V2 files are staged (V1-only commits pay
    zero cost). Exit non-zero blocks the commit.
  - `sidecar/projection/scripts/install-hooks.sh` — installer
    that symlinks (or copies) the source hook into
    `.git/hooks/pre-commit`. Idempotent; safe to re-run.
  - `.github/workflows/lint-projection.yml` — GitHub Actions
    workflow triggered on `pull_request` and `push to main`
    paths-filtered to `sidecar/projection/**`. Defense-in-depth
    against bypassed local hooks.

**Bypass discipline.** `git commit --no-verify` is the explicit-
acknowledgement-of-deviance escape hatch. Per the discipline,
bypass should be paired with a follow-on slice that retires the
violation. CI catches bypasses on PR.

**Operational consequence.** The lint script now enforces 22
rules over `src/`. Per the audit-driven 2026-05-09 work,
today's empirical baseline is clean; the rules prevent
regression. Pre-commit + CI run the same script for defense-in-
depth.


## 2026-05-09 — Operating discipline: ongoing compliance to Big-O + hexagonal architecture (lint-enforced)

The chapter-3.1 audit catalogued ~27 Big-O findings; the high-
leverage subset shipped as substantive refactors (countUserTables
elimination, ResizeArray accumulators, HashSet diff,
SHA256.HashData, parallel hashing, lifted FK Maps). The audit's
remaining Big-O concerns are now lint-enforced:

  - **`xs @ [x]` ban (Rule 19)** — the canonical fold-of-Result
    anti-pattern (O(N²) per call site) is structurally unreachable
    in new code; existing sites refactored to `Result.aggregate`.
  - **Bench-driven optimization protocol** (per `DECISIONS
    2026-05-24`) is the operational discipline for any new
    perf concern: three-candidate / 2-refuted / 1-confirmed.
    Refuted swaps documented with bench data so the same swap
    doesn't recur.

The hexagonal-coupling lint rules (20 / 21 / 22) structurally
enforce the dependency direction `Core <- Adapters / Targets <-
Pipeline <- CLI` at the file level (the project-reference level
is enforced by F# itself; the lint catches `open`-statement
drift). Per `VISION.md` / `CLAUDE.md` "Load-bearing commitments,"
F#-pure-core / no-I/O-in-Core is the structural answer; the lint
makes the commitment grep-checkable in CI / pre-commit.

**Forward signals:**

  - **F# Analyzers SDK custom analyzer** (deferred; chapter
    3.7+ candidate) — would catch syntax-tree patterns the
    grep heuristic misses (`member val X with get, set`
    properties, reflection-via-attribute scanning, `obj`
    parameters with implicit type inference). Re-open trigger:
    a real false-negative surfacing in CI — i.e., a new
    discipline-violation shipping unflagged.
  - **`Outcome.toDiagnosticString` per DU** (deferred per
    chapter-3.1 audit Tier-2 #14) — would retire the `%A`
    pretty-print sites in pass drivers, removing several
    `LINT-ALLOW-FILE` markers. Substantive ~1-day chapter
    touching every Outcome / KeepReason / Origin DU.
  - **ScriptDom adoption for SQL emission** (per `DECISIONS
    2026-05-09 — Built-in obligation`; chapter 3.7+ candidate)
    — retires `Render.fs` / `Deploy.fs` SQL string-build paths
    via typed `TSqlFragment` AST + `Sql150ScriptGenerator`.
    Removes several `LINT-ALLOW` per-line markers.

Each candidate compounds the discipline at the next chapter
close.


## 2026-05-09 — Chapter 3.6 brittleness audit + library-API pass: act-on / record-for-future log

**Status:** decided
**Context:** User's "battle-harden" directive (2026-05-09) plus the
"be bold" follow-up authorized expensive-now-for-cheaper-later
moves. Two subagents ran:
  1. **Brittleness audit** — surveyed every `String.Concat` /
     `String.concat` / `String.Join` / `sprintf` / interpolated
     string / magic constant / hand-rolled SQL / regex-adjacent
     pattern in src/. Output: 6-section report with Top-10
     refactors + Section-6 "irreducibles" defended.
  2. **Library-API audit** — surveyed FsToolkit /
     `FSharp.Core` / DacFx / `Microsoft.Data.SqlClient` /
     `System.Text.Json` / `System.Xml` / new-library candidates
     for adoption opportunities.
**Decision:** Acted on the high-leverage findings; documented
the rationale + perf analysis for the skipped ones; recorded
queued items for future agents.

**Acted on this session (chapter 3.6):**
  - **`RawValueCodec` module** — V2's canonical raw-value format
    contract consolidates 3 consumers (`Render.formatSqlLiteral` /
    `Bulk.parseRaw` / `ReadSide.formatRawValue`). Eliminates 6
    duplicated format strings (DateTime / Date / Time / Guid /
    Boolean / hex prefix). Audit Top-10 #5 + #9.
  - **Magic-constant extraction** — BenchSink timestamp format,
    Deploy GuidSuffixLength + GoBatchSeparator + SystemSocketPath
    via `Path.Combine`, ReadSide reconstructedModuleName promoted
    to `[<Literal>]`. Audit Top-10 #5.
  - **ScriptDom expansion** — `createDatabaseSql` collapsed manual
    `[…]` into `Render.quote`; `readRowsStream` 3 sites + `readRows`
    `SELECT COUNT` site routed through
    `Identifier.EncodeIdentifier`. Adapters.Sql gained ScriptDom
    package dep. Audit Top-10 #10 + library-audit Section 4
    consistency miss.
  - **`ConnectionString` module** — typed
    `SqlConnectionStringBuilder` validation at `parse`; warm-env-var
    surfaces malformed strings as stderr warning + structured
    fallback rather than opaque `SqlException` at connect.
    Audit Top-10 #1.
  - **`EmissionPolicy.create`** smart constructor — enforces
    "at least one artifact family" invariant. Audit Top-10 #8;
    A39 cash-out.

**Skipped with documented reasoning:**
  - **`Bulk.parseRaw` → `Result<obj>` wrap** (audit Top-10 #2):
    SKIPPED. Critical re-evaluation: the deploy boundary already
    catches per-batch exceptions via `try/with` →
    `Errors = collectErrors ex` → `Report.Errors`, so failures are
    NOT silent. Per the user's perf-analysis directive
    (2026-05-09): wrapping each cell parse in `Result<obj>` adds
    one Result allocation per cell × ~10 cells/row × 100k+ rows =
    ~16 MB extra GC pressure on the hot bulk-load path. The
    happy-path cost (zero overhead in current form vs per-call
    `Ok` wrap) compounds at scale. Verdict: existing
    exception-to-Report pattern is structurally adequate AND
    faster; Result-wrap would be over-engineering with negative
    perf at scale. The cell raw inputs come from V2-internal IR
    (built by the adapter via validated parsing) — failures here
    are programmer errors, not data errors; exception is the
    correct surface.
  - **`Result<'a>` migration to `FSharp.Core.Result<'a, ValidationError list>`**
    (Phase 8 attempted, REVERTED): the alias works in Core but
    the cascade collides with three existing names: F#'s built-in
    `Failure` exception pattern (`failwith`/`Failure msg`),
    `Diagnostics.DiagnosticSeverity.Error` case (in scope wherever
    `open Projection.Core` is), and `JsonEmitter`'s existing
    explicit `Microsoft.FSharp.Core.Result<_, EmitError>` usage.
    Mass sed of `Success → Ok` / `Failure → Error` produces
    ~50+ ambiguity errors that need per-site qualified-name
    resolution. Needs a dedicated session with planned per-file
    migration + careful collision audit, not a single-commit
    sweep. Filed as Phase 8 deferral with explicit
    "Begin by adding `[<RequireQualifiedAccess>]` to
    `DiagnosticSeverity` then migrating top-down" guidance.

**Recorded for future agents (audit Section 8 + library-audit
Section 8):**

  | Item | Trigger | First natural site |
  |---|---|---|
  | **DacFx adoption** (`Microsoft.SqlServer.DacFx`) | Chapter 3.x DacpacEmitter opens | New `src/Projection.Targets.DacPac/DacPacEmitter.fs` consuming `RawTextEmitter.statements` + emitting via `TSqlModel.AddObjects` + `DacPackageExtensions.BuildPackage`. Pairs with `ArtifactByKind`. Note: ~30MB transitive deps; budget accordingly. **Per `DECISIONS 2026-05-06`, this is the right tool for SSDT — NOT SMO.** Primitives worth knowing: `TSqlModel`, `DacPackage`, `DacServices.GenerateDeployScript` (consumes `.refactorlog` for `sp_rename` translation — load-bearing for chapter 3.5+), `SchemaComparison` (typed diff sibling to `PhysicalSchema`), `DacDeployOptions` (the V2-equivalent of `Policy` knobs for deploy). |
  | **`SqlBatch`** (Microsoft.Data.SqlClient ≥ 5.5) | Confirmed in target environment AND `Deploy.executeBatch` becomes the canary's bottleneck again | `Deploy.executeBatch` (~22 LoC of `splitOnGo` parser cleanly retired). Native batched-command shipping. |
  | **`SqlConnection.RetryLogicProvider`** | Canary CI flake from transient SQL Server warm-up | `Deploy.executeBatch`. Pairs with T-30 / T-15 cutover ladder gate (gate-(d) UAT dry-run flake-rate threshold). |
  | **`AsyncSeq`** (`FSharp.Control.AsyncSeq`) | Streaming readside or downstream consumer needs `bufferByCountAndTime` (time-windowed batching) or `mergeAll` (multiple parallel streams) | Chapter 4.x data triumvirate. Until trigger, the custom `AsyncStream` stays — it's doing its job. |
  | **`JsonObject` typed per-kind value** | A second consumer of `ArtifactByKind<string>` needs typed manipulation (post-write enrichment, drift detection) | Already named at `JsonEmitter.fs:160-162`. Chapter-4.4 drift detection or DacpacEmitter's catalog-metadata channel. |
  | **`Argu` for CLI** | CLI grows beyond 3 commands OR adds per-command flags (chapter-3.x canary will add `--source-driver`, `--target-image`, `--tolerance-yaml`) | `src/Projection.Cli/Program.fs` rewrite to typed `Arguments` DU + `ArgumentParser.Parse`. ~60 LoC net reduction. Audit Section 7 #1. |
  | **`Verify.XUnit` for golden tests** | Chapter-3.x DacpacEmitter golden-file rotation | `tests/Projection.Tests/RefactorLogRenderTests.fs` first; then `JsonEmitterTests.fs`, `RawTextEmitterTests.fs`. Sharply reduces friction at golden rotation. Audit Section 7 #2. |
  | **`FsToolkit.ErrorHandling` adoption + `Result<'a>` migration** | Dedicated session with planned per-file migration; expect ~387 site changes + 50+ name-collision per-site fixes | Begin with `[<RequireQualifiedAccess>]` on `DiagnosticSeverity`. Then migrate `Result<'a>` to `Microsoft.FSharp.Core.Result<'a, ValidationError list>`. Then sed `Success`/`Failure` per-file with build-after-each. Then adopt `taskResult { }` for `Deploy.runWideCanaryWithLoader` (~40 LoC reduction) and `validation { }` for `CatalogReader.parseAttribute` / `parseKind` (~80 LoC reduction). Audit Section 7 #3 + #5. |
  | **`Microsoft.Extensions.Logging`** | A CI consumer demands structured logs (machine-readable diagnostics) | A `Diagnostics.fs` sibling at the boundary in `Pipeline.fs` (NOT in Core). |
  | **`Utf8JsonReader`** for streaming JSON parse | Bench surfaces parse time at chapter-3.x scale | `CatalogReader.parseDocument` (already structured as a tree-walk; refactor when worth it). |
  | **Hand-rolled `splitOnGo` retained** with documented justification | ScriptDom's `TSql160Parser.Parse` would auto-batch via `TSqlScript.Batches` BUT requires well-formed input (operator-supplied DDL may have errors; current fold is permissive). Trade-off documented inline at `Deploy.fs`. | Re-evaluate when operator-supplied DDL becomes the dominant input shape. |

**Reasoning / consequences:** The audit's 6-section report +
library-audit's 8-section report were a structural opportunity
to harden the codebase against multiple risk classes. Acting on
the high-leverage findings (RawValueCodec, ConnectionString,
ScriptDom expansion, EmissionPolicy.create) eliminated ~6 brittle
patterns at zero behavioral risk. Skipping `Bulk.parseRaw` →
Result with documented perf analysis demonstrates the critical
thinking the user's directive demands. The Phase 8 (Result
migration) revert is honest about scope risk and queues the work
for a session with adequate budget. Future-agent log preserves
every recommendation NOT acted on, so no work is silently lost.

The lint baseline at chapter-3.6 close (per-line `LINT-ALLOW`):
17 markers (was 15 pre-3.6). Net +2 from `RawValueCodec`'s
terminal-text emission boundary (which itself eliminated 6
duplicate-format-string sites at producers). The boundary moved
from many producers to one canonical renderer — that's the
architectural improvement, not the marker count.

---

## 2026-05-10 — V2-driver as destination KPI (chapter 3.7 sidebar; principal-PO discussion)

**Status:** decided
**Codified at:** `V2_DRIVER.md` (standalone canonical surface; supersedes `BACKLOG.md` which is now a forwarding pointer).
**Resolves:** the implicit ambiguity in `DECISIONS 2026-05-22 — R6: Split-brain governance rule for the dual-track cutover window` about whether V2-driver was the destination or the stretch goal. R6's transition mechanism (per-environment-per-artifact-type V2-driver transition gated on N=10 consecutive green canary runs plus operator sign-off) is preserved unchanged; what shifts is the *direction* of the gate.

**Context.** The principal-PO discussion at chapter 3.7 sidebar (this date) clarified the project's primary KPI: **provable correctness across every axis V2 owns** is the primary motivation, not just verification of V1's outputs. The cutover (~late July 2026 per operator estimate; ~80 days from this codification) is V1-functional already; V2's job is to make every axis it owns provably correct so the cutover is verifiable end-to-end and so V1 sunset becomes a real plan post-cutover.

The R6 entry as written framed three rungs of a fallback ladder: V1-only / V2-augmented (V1 drives, V2 verifies) / V2-driver (V2 emits production artifacts; V1 stays warm through cutover+30 as fallback). It was operationally precise about *how* the transition happens but ambiguous about *whether* V2-driver was the destination. The ambiguity led to a legitimate "save LOC; V2-augmented as destination" framing that, under examination, conflicted with the operator's primary motivation. This entry resolves the ambiguity.

**Decision.** **V2-driver is the destination.** V2-augmented is the gate to V2-driver, not the floor; V1-only is the cutover-window safety net, not the sustained operating mode. The fallback ladder operates as before; the gate direction shifts to "we progress per-environment-per-artifact-type as fast as the per-axis property tests support." V1 stays warm through cutover+30 (per existing T-30 / T-15 ladder rule, unchanged); V1 sunset begins after the cutover-survival window AND all four environments have run V2 emissions for one full schema-evolution cycle without canary divergence.

**The KPI in one sentence.** V2 reaches V2-driver mode for the cutover by being provably correct on every axis V2 owns — schema, data, identity, diagnostics, and any future sibling — with provable correctness defined as structural-type-level enforcement plus per-axis property tests, not aspirational discipline plus selective coverage.

**Operationally.** Every chapter, every slice, every architectural decision in V2 from this date forward biases toward V2-driver. When two paths offer the same correctness with different LOC, prefer fewer LOC. When two paths offer different correctness depth, prefer the deeper correctness — even if the LOC investment is larger.

**The CDC-silence-on-idempotent-redeploy property test (chapter 4.1.B) is the highest-leverage single deliverable in the entire chapter sequence** (per the per-axis stakes table in `V2_DRIVER.md`). Build it first inside chapter 4.1.B; gate the chapter close on it; run a dry-run on at least one CDC-enabled table at production shape before chapter close.

**Per-axis correctness depth (excerpted; full table in `V2_DRIVER.md`):**

| Axis | Failure mode if wrong | Verification depth |
|---|---|---|
| CDC silence on idempotent redeploy | Spurious change records corrupt CDC-dependent features silently | **Highest.** Per-CDC-table coverage; multi-redeploy property; CI gate red on any non-zero. |
| Schema (SSDT DDL) | Production deploy fails or deploys wrong shape | High. Mostly shipped (chapter 3.1). |
| Data (static populations + seeds + bootstrap) | Seed data missing/duplicated/topologically out-of-order | High. Substrate ready; emitters chapter 4.1.B. |
| User FK reflow | Production reports break or data loss | High. Chapter 4.2. |
| RefactorLog round-trip | Cross-version identity tracking breaks | High. Chapter 3.5 θ/ι. |
| Multi-environment promotion | Per-env policy divergence not caught until second env deploys | Medium-high. Tolerance taxonomy (M4) is the decision surface. |
| DACPAC round-trip | DACPAC erases axes silently; SSDT and DACPAC diverge | Medium-high. Chapter 3.x; conditional on deploy path. |
| Operational diagnostics | Operator can't diagnose post-cutover issues | Lower. Chapter 4.3. |

**Chapter sequencing under V2-driver KPI** (full sequence in `V2_DRIVER.md`):

| Phase | Chapter | Status | Critical-path? |
|---|---|---|---|
| Phase 1 | Chapter 3.5 (Π port + RefactorLog + CatalogDiff) | In-flight | YES |
| Phase 2 | Chapter 4.1.A + Tolerance + multi-env | Not-started | YES |
| Phase 3 | Chapter 4.1.B (CDC-critical) | Not-started | YES |
| Phase 4 | Chapter 4.2 (User FK reflow) | Not-started | YES |
| Phase 5 | Chapter 4.3 (operational diagnostics) | Not-started | YES |
| Phase 6 | Chapter 3.x DacpacEmitter | Not-started | Conditional on deploy path |
| Phase 7 | Chapter 3.2 SnapshotRowsets | Not-started | YES (cross-version identity stability) |
| Phase 8 | Chapter 5+ pragmatic close | Deferred-with-trigger | Consumer-pressure-driven |

**What V1 retains under V2-driver mode.** V2 does not duplicate V1 surfaces that don't add provability. V1 retains: live SQL extraction (V2 consumes V1's evidence cache); the V1 manifest as evidence source (not as deploy artifact); the V1 documentation surfaces V2 doesn't duplicate (`handbook/` operator teaching material, `ssdt-playbook/` per-change-tier mechanics); operator-facing CLI surfaces during transition (per-env per-artifact-type R6 governance); V1 stays warm through cutover+30 as fallback. V1's role under V2-driver is **upstream evidence + safety net**, not co-driver.

**The disciplines that compound under this KPI.** The eight pillars in the supreme operating discipline (top of this file) and the two named failure modes (performance-of-compliance; domain-blind naming) are exactly the substrate provable correctness needs. Each pillar protects against a class of runtime-only invariant. Pillar 1 (data-structure-oriented), pillar 3 (built-in obligation), pillar 7 (gold-standard library precedence + LINT-ALLOW substantive-rationale amendment), pillar 8 (domain-first naming) all serve the V2-driver KPI directly. The two-consumer threshold + IR-grows-under-evidence prevent speculative LOC; provable correctness requires the right code with provable properties, not more code. Closed-DU expansion empirical-test discipline + bench-driven optimization protocol + iterator-logging-as-first-class-outcome compound across chapters.

**What this KPI is NOT.** Not "ship faster" (the constraint is rigor, not date); not "more code at any cost" (smart-product-choices framing applies); not "less rigor in some places" (per-axis stakes vary, but discipline floor is uniform); not "skip V2-augmented" (V2-augmented IS the gate); not "V1 must sunset on a deadline" (sunset is conditional on cutover+30 + one full schema-evolution cycle + operator confirmation); not a deadline-driven framing (the cutover is V1-functional already; V2's job is provable correctness, not delivery speed).

**Operating implications for chapter agents.** When opening a chapter: name the axes the chapter advances per the per-axis stakes table; state explicitly which property tests the chapter will make hold; state explicitly what V1 capability the chapter is making V2 own. When choosing the next slice: prefer slices that advance an axis V2 is committing to own under V2-driver; defer slices that are quality-of-life without advancing a V2-driver axis. When considering primitive extraction: the two-consumer threshold still applies; do NOT extract speculatively. When considering whether to defer a chapter: chapters 4.1.B / 4.2 / 4.3 / 3.x / 3.2 are NOT optional under V2-driver KPI; sequence them, don't skip them.

**Reasoning / consequences.**

This entry codifies what was previously implicit. R6 governance was framed in terms of "we'll progress along the ladder per-environment-per-artifact-type" — meaning V2-augmented is the floor, V2-driver is aspirational. Under examination at the principal-PO sidebar, that framing was incomplete: V2-driver is the operative target, not the stretch goal. V2-augmented is the *gate* by which the operator earns confidence in V2-driver per environment per artifact type; once the gate clears, V2-driver is the operating mode.

The codification has structural impact on chapter sequencing: chapters 4.1.B (data triumvirate; CDC-critical), 4.2 (User FK reflow), 4.3 (operational diagnostics), 3.x (DacpacEmitter conditionally), 3.2 (SnapshotRowsets) become critical-path under V2-driver KPI. Pre-V2-driver-KPI codification, these chapters were "on the backlog" with implicit "if cutover deadline allows" framing. The KPI clarifies they ARE the deliverable.

The codification has structural impact on every chapter agent's decision-making: bias toward V2-driver. The disciplines codified in the supreme operating discipline ARE the substrate for the KPI; the chapter-close ritual + per-axis property tests + AXIOMS amendments are the verification surface.

The standalone document `V2_DRIVER.md` is the canonical surface; this DECISIONS entry is the formal codification reference. The two surfaces share substance; `V2_DRIVER.md` is the operative read for chapter agents (extends to cover the operative backlog supersedes `BACKLOG.md`); this DECISIONS entry is the chronological codification record.

---

## 2026-05-10 — Domain-first naming and ubiquitous-language consistency (pillar 8; chapter 3.7 sidebar)

**Status:** decided
**Context:** The V2 sidecar already operates DDD bounded contexts
(Coordinates, Identity, RawValueCodec, SqlTypeCorrespondence) and
generic algebraic naming (Catalog / Module / Kind / Reference at
Core; domain-prescriptive vocabulary at adapter boundaries). The
"Programming style — Naming" section in `CLAUDE.md` documents the
intent. But intent is descriptive; structurally there is no
named-failure-mode forcing function preventing drift. As V2 grows
(chapters 4.1 / 4.2 / 4.4 introduce dozens of new types), the most
likely failure mode is *not* an explicit anti-pattern (V2 will not
suddenly grow `KindManager.fs`). It is **the slow accretion of
generic CS vocabulary** in places where a domain term should sit —
"Helper", "Util", "Service", "Handler", "Processor", "Builder"
(when not BCL-mandated), "Factory" (likewise), "Provider", "Wrapper".
Each generic suffix is a placeholder for "I haven't identified the
domain concept yet." Each gets ratified across reviews because the
code works; the cost compounds across every reader.

The user's directive (chapter-3.7 sidebar): make domain naming +
domain alignment + domain critical thinking a **first-class and
protected focus** for V2 agent operation. Codified at the same
structural level as pillars 1–7 (data-structure-oriented; no string
concat; built-in obligation; FP promised land; coding-style; no
back-compat; gold-standard library precedence + LINT-ALLOW
substantive-rationale).

**Decision: Pillar 8 — Domain-first naming and ubiquitous-language
consistency.**

The named failure mode is **domain-blind naming**: when a name
answers "what does this DO" (action-shaped: `process`, `handle`,
`manage`, `run`, `execute`) rather than "what does this REPRESENT
in the domain" (concept-shaped). Fails to put the domain concept in
the type system. The agent feels productive (a name exists; the code
compiles; tests pass) without doing the domain-modeling work that
makes the name structurally accountable.

The cutover stakes (300-table OutSystems → SQL Server external-
entity migration; four environments; active CDC dependencies; R6
split-brain governance; T-30 / T-15 fallback ladder) are the
forcing function. The cutover is a **business event** — operators
and DBAs talk about it in domain vocabulary (Entity, Espace,
Application, Module, Static Entity, External Entity, RefactorLog,
DACPAC, Catalog, Schema). V2's job is to make the cutover
verifiable, reversible, and repeatable. **Verifiability rests on
the V2 vocabulary mirroring the cutover vocabulary.** When V2
introduces CS-vocabulary terms ("KindManager", "EmissionService",
"Helpers") in places where the domain has a sharper term, the
mirror cracks — operators and DBAs reading V2 source can no
longer recognize their concepts; engineers reviewing V2 changes
no longer recognize when the concept being changed has business
implications.

The four-question domain-naming analysis (the structural prerequisite
before introducing any named type / function / file / module / test):

```
1. What domain concept does this represent?
   Articulate it in cutover-business terms. Examples:
     - "This is the V2 IR's identity for an external entity post-cutover."
     - "This is the round-trip pair PrimitiveType ↔ SQL Server DDL base name."
     - "This is the lineage event for a removal predicate firing."
     - "This is the schema-fidelity comparison between source and target."
   If you cannot articulate what the concept IS in the cutover business,
   you do not have a name yet. STOP.

2. Does V2 already name this concept somewhere?
   YES → use the same name. Ubiquitous-language consistency: the same
         concept appears under the same name across Core / Adapters /
         Targets / Pipeline / CLI. Cross-surface name drift is itself
         a structural failure (an operator reading two surfaces
         encounters two names for one concept).
   NO  → pick a name that aligns with how domain experts (operators,
         DBAs, OutSystems platform docs, CDC documentation, SQL Server
         admin guides) name the concept. The reference vocabularies
         are the cutover stakeholder's vocabularies — V2 lives within
         them, not above them.

3. Is the proposed name concept-shaped or action-shaped?
   CONCEPT-SHAPED → "what this IS in the domain" (Catalog, Module,
                    Kind, Reference, RemovalReason, AnnotationDetail,
                    SqlTypeCorrespondence, RefactorLog). Default for
                    types, modules, files.
   ACTION-SHAPED  → "what this DOES" (canonicalize, normalize, render,
                    emit, project). Acceptable for function names
                    when the verb names a *domain* operation. NOT
                    acceptable when the verb is a generic CS
                    operation (process, handle, manage, run, do).

4. Generic-suffix smell test.
   If the name ends in any of:
     - Helper / Util / Utils / Utility / Utilities
     - Manager / Service / Handler / Processor / Wrapper
     - Builder / Factory / Provider / Strategy (when not BCL-mandated)
   STOP. The generic suffix is a placeholder for "I haven't identified
   the domain concept yet." Either:
     a. Find the concept (rename to the domain term), OR
     b. Restructure (the concept is being squashed into something else
        — it doesn't deserve a wrapper around an unnamed thing).

   Note: the lint guardrail does NOT enforce this syntactically.
   Heuristics misfire on legitimate uses (e.g., `LineageBuffer` is
   concept-shaped despite the "Buffer" suffix — the buffer IS the
   reified mutation surface). The discipline document does the
   catching the heuristic can't.
```

**Ubiquitous-language consistency** is a separate axis from
domain-shaped naming. Two surfaces independently picking concept-
shaped names can still drift if they pick *different* names for the
same concept. V2 already has worked examples of consistent naming:
`SsKey` is the identity term across Core / Adapters / Targets /
Pipeline / CLI — never `EntityKey`, never `Identifier`, never
`Hash`. When the term is consistent, an operator reading any V2
surface encounters the same concept. When the term drifts (one
surface uses `SsKey`, another uses `Identifier`), readers must
mentally translate; the translation cost compounds.

**Domain critical thinking at every decision point.** The
disciplined posture: when reaching for a new type / function / file
name, ask `"What does this represent in the cutover business?"`
*before* drafting the name. The answer is the documentation of
the agent's domain understanding at this site. If the answer is
generic ("it's a helper for emit") — the domain understanding hasn't
landed; the name is wrong; the work is to identify the concept.

**Worked precedents in V2 (concept-shaped, ubiquitous):**
  - `Catalog` / `Module` / `Kind` / `Reference` — generic algebraic
    names at Core; mirror the domain-prescriptive `Application` /
    `Espace` / `Entity` / `ForeignKey` at adapter boundaries.
  - `SsKey` (`OssysOriginal` / `Synthesized` / `V1Mapped`) — identity
    DU; every variant names a provenance class.
  - `RemovalReason` / `AnnotationDetail` — typed lineage payload DUs;
    each variant names the predicate / decision class.
  - `Coordinates.TableId` — schema-coordinate value object; consistent
    across PhysicalSchema / Statement / ReadSide / ProfileSnapshot.
  - `RawValueCodec` — V2's canonical raw-value format contract;
    consolidates 3 producer/consumer sites.
  - `SqlTypeCorrespondence` — round-trip pair `PrimitiveType ↔ SQL
    DDL base name`; bounded context name reified.
  - `RefactorLog` / `CatalogDiff` — domain terms (RefactorLog from
    SSDT vocabulary; CatalogDiff from V2's diff algebra).
  - `BatchSplitter` — concept; what splits is the cutover's deploy
    batches.
  - `DatabaseNameGenerator` — concept; reified non-determinism
    boundary for ephemeral DB names.
  - `EmissionPolicy` — concept; per A39 the policy is structurally
    named (NOT `EmissionConfig` or `EmitterSettings`).
  - `LineageBuffer` — concept; the buffer IS the reified mutation
    surface. The "Buffer" suffix is concept-shaped (a buffer of
    lineage events) — the smell test is heuristic, not absolute.

**Worked anti-patterns (what V2 does NOT do):**
  - V2 has no `*Helper.fs` / `*Util*.fs` / `*Manager.fs` / `*Service.fs`
    files. Generic suffixes are absent by construction.
  - Strategies are named `<Domain>Rules` (`NullabilityRules`,
    `UniqueIndexRules`, `ForeignKeyRules`, `CategoricalUniquenessRules`)
    — the suffix names the algebraic role; the prefix names the
    domain.
  - Passes are named after the verb-noun shape but the verbs ARE
    domain operations (`canonicalize`, `normalize`, `mask` —
    each names a structural commitment in the cutover algebra).

**Reasoning / consequences.**

This entry refines the "Programming style — Naming" section of
`CLAUDE.md` (which describes the intended pattern) with structural
prerequisites that name the failure mode and the four-question
analysis. The discipline lives in the documents — `DECISIONS` (this
entry, the substance), `CLAUDE.md` operating-disciplines table (the
navigation pointer), `AGENTS.md` (root agent surface), `KICKOFF.md`
(supreme operating discipline), `PLAYBOOK.md` (decision tree
"When you reach for a name" with the four questions executable-form),
`HANDOFF.md` (chapter-3.7 prologue addition).

**No lint enforcement.** Heuristic syntactic checks misfire on
legitimate uses (`LineageBuffer`, `BatchSplitter`); the discipline is
inherently semantic — it requires understanding the domain to apply.
The discipline-document path catches what the heuristic can't.
Future agents (and future me) encounter the four questions at every
naming decision; the named failure mode (domain-blind naming) is
recognizable as a pattern.

**Why this matters now.** Chapters 4.1 / 4.2 / 4.4 will introduce
dozens of new domain types (StaticSeedsEmitter / MigrationDependencies
/ BootstrapEmitter / UserFkReflowPass / SourceTag VO / DacpacEmitter
+ subordinate types). Each is a domain-modeling moment. The codified
discipline is the structural answer to the question "how do we
preserve V2's domain-first naming under chapter-by-chapter pressure
without re-deriving the discipline at every decision?" Codifying it
now means chapter 4.x agents inherit the discipline as named-and-
recognized rather than rediscovered.

**Why this is named at the supreme-discipline level.** Pillar 8
joins pillars 1–7 (data-structure-oriented; no string concat; built-
in obligation; FP promised land; coding-style; no back-compat;
gold-standard library precedence) because **domain alignment is the
load-bearing structural commitment that makes V2's verification
claim trustworthy**. V2 is the trust anchor for the cutover; a trust
anchor whose vocabulary doesn't mirror the business is a trust
anchor whose claim must be re-translated to be checked. The
translation cost compounds across every operator interaction.

---

## 2026-05-10 — LINT-ALLOW substantive-rationale discipline (chapter 3.7 sidebar; pillar 7 amendment)

**Status:** decided
**Resolves:** the slice-β pickup at chapter 3.7 — `Render.columnSqlType`
landed with four `String.Concat` sites carrying `LINT-ALLOW` markers
shaped like an audit trail ("terminal SQL DDL emission boundary; both
segments are typed (closed-DU dispatch + literal)") without the
substance: the markers did not name the use-case-specific library
(ScriptDom's `SqlDataTypeReference` + `Sql160ScriptGenerator`, both
already loaded, both already used by the sibling `ScriptDomBuild`
module), did not compute the cost of the alternative (~30 LOC: lift
visibility + add a one-call generator helper), and did not conclude.
Operator caught the shortcut on review. Slice β' immediately followed:
lifted `dataTypeReference` from `private` to public, added
`generateDataType : DataTypeReference -> string`, made
`Render.columnSqlType` delegate. All four LINT-ALLOWs retired; output
byte-identical (790 tests still green); perf-gate clean. The "cost of
doing it right" was trivial compared to the structural drift the
shortcut would have introduced over time.

**Context.** Pillar 7 (`DECISIONS 2026-05-09 — Gold-standard library
precedence`) already names the precedence: use-case-specific library →
typed data structure → `StructuredString` → documented LINT-ALLOW.
Pillar 7's "deep per-site analysis" clause is the load-bearing demand:
every adoption of `String.concat` / `String.Concat` / `String.Join` /
`String.Format` / `sprintf` / `+` / interpolated string outside the
gold standard requires an analysis the marker text records as the
audit trail. The slice-β failure surfaced that "deep per-site analysis"
is too easily satisfied by a *justification-shaped marker* that uses
discipline vocabulary ("terminal", "boundary", "typed") without
performing the analysis the vocabulary is supposed to summarize.

The named failure mode is **performance-of-compliance**: a marker with
the SHAPE of an audit trail but without the substance. Distinct from
explicit non-compliance (which is recoverable because it's visible at
the lint surface); distinct from genuine compliance (which is the
work). Performance-of-compliance is the failure mode where the agent
*feels* compliant, the lint passes, the tests are green — and the
structural commitment is unmet. The marker's audit-trail shape masks
the absence of the audit. Future agents reading the marker (including
future me) treat the formula as decided fact.

The asymmetry is structural: V2 is the trust anchor for the high-stakes
cutover; every shortcut introduces a runtime-only invariant ("our
composition matches the vendor's emission today") that future drift
forces are waiting to surface. The cost of the shortcut compounds
across every reader. The cost of doing the work is paid once, at the
moment of insight. Pillar 6 (`DECISIONS 2026-05-09 — No V2-internal
back-compat`) already names the structural pressure: shortcuts are
back-compat-debt invitations.

**Decision.** Codify the four-question analysis as the structural
prerequisite for any `LINT-ALLOW` marker on a string-composition or
built-in-substitute site. The analysis MUST be performed (and the
marker text MUST embody it) before the marker is committed:

  1. **What is the use-case-specific library** for this output
     structure? Name it explicitly (module + type + function).
     Examples: `Microsoft.SqlServer.TransactSql.ScriptDom
     .SqlDataTypeReference` + `Sql160ScriptGenerator.GenerateScript`
     for SQL DDL type expressions; `System.Xml.XmlWriter` for XML;
     `System.Text.Json.Utf8JsonWriter` / `JsonNode` for JSON;
     `Microsoft.SqlServer.Server.SqlMetaData` for SqlClient parameter
     metadata; `RFC4122 UuidV5` for namespaced GUIDs; `BCL parsers/
     formatters with CultureInfo.InvariantCulture` for typed
     parse/format round-trips.
  2. **Is it already in the codebase** (or available as a non-V2-back-
     compat dependency)? If yes, name the existing consumer site so
     the precedent is structurally visible. If no, name the package
     name + version that would land it.
  3. **What is the cost of using it here?** Be concrete: visibility
     lift (`private` → public; ~N LOC); perf class (zero / O(1) /
     O(N) / O(N log N) / O(N²) per-call delta; bench label that
     would surface it); dep weight (transitive package size). The
     cost analysis IS the perf-clause cash-out at this site.
  4. **Is there a structural reason it doesn't apply?** Examples of
     legitimate "no": the BCL writer's grammar can't express the
     structure (e.g., escape-sequence formatting for which no
     formatter exists); the data is V2-internal-only and the BCL
     writer would lose the typed information; the BCL writer
     introduces non-determinism (e.g., `JsonSerializer` with default
     options yields culture-dependent output) that violates T1.

If the answer to #4 is **"no"**, there is no shortcut — there is the
work. The LINT-ALLOW marker is wrong; the right move is to lift the
visibility, add the helper, refactor the call site. Slice β' is the
worked example: the answer to #4 was "no, ScriptDom applies fully";
the cost was trivial; the marker came down.

If the answer to #4 is **"yes"**, the marker text MUST name the
specific structural reason (not generic vocabulary). Examples:

  - **GOOD** (substantive): `LINT-ALLOW: writer-monad tell algebraic
    primitive; pass drivers use LineageBuffer for high-rate
    accumulation, tell is terminal annotation only` — names the
    algebraic role + the alternative for the hot path.
  - **GOOD** (substantive): `LINT-ALLOW: terminal diagnostic
    projection; typed Synthesized (s, parts) available via
    pattern-match for structural consumers` — names the boundary +
    the structural alternative for non-terminal consumers.
  - **GOOD** (substantive): `LINT-ALLOW: terminal text-emission
    boundary; HexLiteralPrefix is the canonical typed segment, raw
    is already vetted hex` — names the boundary + the typed source
    of each segment.
  - **BAD** (performance-of-compliance): `LINT-ALLOW: terminal SQL
    DDL emission boundary; both segments are typed (closed-DU
    dispatch + literal)` — uses pillar vocabulary ("terminal",
    "boundary", "typed") without naming the considered alternative
    (`Sql160ScriptGenerator`) or the structural reason it doesn't
    apply. The marker IS the slice-β failure mode.

**Lint guardrail.** Rule 27 (added in this slice) maintains an
inventory of every per-line concat-aversion `LINT-ALLOW` and emits
the inventory at the end of every clean run. The inventory is the
audit surface — making the markers visible at the discipline-review
moment, not just at the rule-violation moment. The inventory does
not gate compliance (a heuristic can't reliably distinguish
performance-of-compliance from substance); the discipline document
does. Rule 27 also enforces a soft floor: per-line concat-aversion
markers must be at least 30 chars after the colon AND contain at
least one substantive-vocabulary token from the established
discipline lexicon (terminal / boundary / primitive / round-trip /
considered / alternative / gold-standard / escape / irreducible).

**The four-question analysis when reaching for `String.Concat`,
`String.concat`, `String.Format`, `sprintf`, `String.Join`, or
interpolated strings:**

```
1. Use-case-specific library for THIS output structure?
   ├─ ScriptDom (SQL DDL/DML)
   ├─ XmlWriter / XDocument (XML)
   ├─ Utf8JsonWriter / JsonNode (JSON)
   ├─ SqlConnectionStringBuilder (connection strings)
   ├─ Path.Combine (filesystem paths)
   ├─ Identifier.EncodeIdentifier (SQL identifiers)
   ├─ UuidV5 (RFC 4122 namespaced GUIDs)
   ├─ DacFx (DACPAC; pending chapter 3.x adoption)
   ├─ Verify.XUnit (golden-file diff)
   └─ ... (extend as new use cases land)
2. Already in codebase?  YES ─→ name the existing consumer site.
                         NO  ─→ name the package + version.
3. Cost?  visibility lift (LOC) + perf class (zero/O(1)/O(N)/...)
          + dep weight (MB transitive).
4. Structural reason it doesn't apply?
   ├─ NO  → there is no shortcut; do the work.
   └─ YES → marker text MUST name the specific reason
            (NOT generic vocabulary alone).
```

**Reasoning / consequences.** This entry refines pillar 7 with the
named failure mode and the four-question structural prerequisite.
Pillar 7's "deep per-site analysis" was descriptive of the desired
discipline; this entry makes the SHAPE of the analysis structural.
Future agents (and future me) reading the discipline encounter the
four questions before drafting the marker, not after. Performance-
of-compliance is named so the failure mode is reified as a
recognizable pattern rather than an unnamed slip. The lint inventory
keeps the per-line markers visible at the audit surface so the
discipline check is structural at PR review and chapter close.

**Worked precedent of doing it right (slice β'):** the cost of the
"do the work" path was 87 lines of diff across 3 files; the perf-
gate stayed clean; the tests stayed green; four LINT-ALLOWs
retired; two private helpers retired (`sqlTypeWithLength`,
`sqlDecimal`); one unused import retired (`open
System.Globalization`); the SQL DDL type expression flows through
ScriptDom's typed AST end-to-end. The slice's commit message
records the perf-class analysis (per-column generator instantiation
surfaced via bench label `scriptDom.generateDataType`) so the
next-touch agent inherits the perf footprint as structural fact.

**Why this matters in V2.** The cutover stakes (300-table OutSystems
external-entity migration, four environments, active CDC dependencies,
R6 split-brain governance, T-30 / T-15 fallback ladder) are the
forcing function. V2 is the verification surface. Every drift class V2
introduces is a probability-mass increase on "we caught a thing we'd
never expected; per R6 we revert to V1; the cutover delays N days."
The discipline isn't ascetic — it's protective. Each disciplined
choice forecloses a future debugging session, and the cost of those
sessions compounds during the highest-stakes window: the actual
cutover.

---

## 2026-05-09 — Operator-reality canary as the production-baseline perf gate

**Decision:** the chapter-3.6 perf-regression gate
(`scripts/perf-gate.sh`, fired by the pre-commit hook AND the Stop
hook on every agent message) runs the **operator-reality canary**
(`Operator-reality canary: 50k rows × 300 tables, variegated,
round-trips via bulk path` in `tests/Projection.Tests/GeneratorScaleTests.fs`,
spec at `FixtureGenerator.GenerateSpec.operatorReality`), NOT the
schema-only `fixtures/canary-gate.sql`. The schema canary stays in
service for the SessionEnd smoke (`.claude/hooks/session-end.sh`)
but is explicitly **inappropriate** as the perf baseline.

**Operator directive (verbatim):** "operator disagrees. canary-gate.sql
is inappropriate for stop hooks. I would like to compromise — 50k
records, variegated, 300 tables. Full stop. How else do we know the
production use case is baselined as we add on features?"

**Reasoning:** the schema canary exercises ~7 tables and ~0 rows;
its bench surface is dominated by Docker-warmup constants and
ScriptDom parse overhead. Per-pass / per-emit / per-readside /
per-deploy-batch distributions don't surface at meaningful tail
latency. A perf gate that doesn't exercise the production envelope
is a gate that approves regressions *because they don't appear in
the gated workload*. The operator-reality fixture (8 modules, 200
regular entities + 100 static entities × 500 rows each, ~10 attrs/
entity, FK density 0.2, deterministic seed 42) matches the
`VISION.md` 300-table forcing function on cardinality and exercises
the bulk realization path (`SqlBulkCopy` over many tables vs few
tables in `bulk1k`/`10k`/`100k`). At ~10-12s warm, it fits the
Stop-hook budget (timeout bumped from 30s to 60s for cold-start
margin) while exercising every production hot path.

**Mechanism:**

  - Test invocation: `dotnet test --filter "FullyQualifiedName~Operator-reality"`
    with `PROJECTION_BENCH_DIR=$ROOT` (so the test process writes
    bench JSON to `bench/canary/<utc>.json` adjacent to
    `Projection.sln`, not to its own bin dir). The test resolves
    `PROJECTION_BENCH_DIR` first; falls back to walking up from
    `Directory.GetCurrentDirectory()` to find `Projection.sln`.
  - Statistical gate: per-label `μ + Kσ` (default K=3.0; ~99.7%
    one-tailed bound) against rolling `bench/history-canary.jsonl`
    (max 20 runs). Warm-up phase (N < 5 history entries) falls
    back to flat `BENCH_TOLERANCE` 1.5×. Per-label noise filter:
    labels with mean < `BENCH_MIN_MS` (default 5ms) skipped.
  - Soft-skip preserved: Docker / dotnet unavailable → exit 0
    (docs-only commits + dev hosts without canary stack don't
    hard-block).
  - Re-record: `PERF_GATE_RECORD=1 scripts/perf-gate.sh` overwrites
    `bench/baseline-canary.json` AND clears `bench/history-canary.jsonl`;
    pair with this DECISIONS amendment naming the new floor's
    rationale.

**What this supersedes:** the chapter-3.6 cash-out at `scripts/perf-gate.sh`
originally invoked the CLI canary against `fixtures/canary-gate.sql`
(~1.5s warm). The CLAUDE.md operating-disciplines table's "Canary
as load-bearing forcing function" entry described "Per-commit gate
is bulk10k" — also superseded; bulk10k stays in `GeneratorScaleTests`
as a sub-second smoke but is no longer the perf gate. The
KICKOFF.md "Perf-regression gate" bullet, CLAUDE.md canary-discipline
entry, and `scripts/perf-gate.sh` header comment all updated to
reflect operator-reality as the production-baseline shape.

**What stays:**

  - `fixtures/canary-gate.sql` + `.claude/hooks/session-end.sh` —
    schema-only smoke at session end. Different surface (parser /
    schema discovery / DDL diff) at fast cadence.
  - `bulk1k`/`10k`/`100k` in `GeneratorScaleTests` — fidelity
    tests for the bulk realization path; not perf-gated, but
    pre-commit `dotnet test` runs them.
  - `realistic` (300 tables, env-var gated) — nightly forcing
    function for full operator-shape stress.

**Consequences:** pre-commit budget grows from ~1.5s to ~12s warm
(~22s cold including build). The Stop hook fires at every agent
message stop; on a clean checkout with Docker warm, expect
~12-15s tail to each session-message. The bench JSON history
accumulates per gate-fire so feature additions trigger statistical
outlier detection within ~5 commits (warm-up phase). The first 5
commits after this DECISIONS entry land are warm-up; commit #6
onward gets the `μ + Kσ` discipline.

**Pillar 7 alignment:** the bench surface at production cardinality
IS the perf evidence; this gate makes regression detection
structural under operator-reality conditions. Iterator-logging
discipline (CLAUDE.md operating-disciplines table) plus this gate
together close the "feature added; perf regressed; we noticed
months later" failure mode by construction.

## 2026-05-10 — Tolerance taxonomy (M4 slice α): typed `ToleratedDivergence` DU + `Set`-encoded `Tolerance` value object

**Context.** R6 split-brain governance (`DECISIONS 2026-05-22`) and
the cutover fallback ladder (`DECISIONS 2026-05-22 — T-30 / T-15`)
both depend on a typed equivalence-class definition for "V1≈V2
modulo named tolerances" and "source-deploy ≈ target-deploy modulo
named tolerances." Until this slice landed, "tolerance" was a string
vibe in canary-test comments and STAGING.md sketches; consumers
could not enforce the discipline at the type level. R4 (multi-
environment promotion property test) and the per-environment
quotient flip both fan out from this primitive.

**Resolution.** Slice α ships `Projection.Core.Tolerance` as a
private-constructor value object wrapping `Set<ToleratedDivergence>`,
where `ToleratedDivergence` is a closed DU enumerating
empirically-grounded variants. Five variants land at slice α; the
STAGING.md S0.E proposal sketched ~13 candidate flag names but the
empirical cut at chapter 4.1.A retains only those with concrete
canary or emitter evidence today:

  - `HeaderCommentsOmitted` — `SsdtDdlEmitter.fs:94` ("V2 omits");
  - `PostDeployForeignKeysSplit` — `CHAPTER_4_PRESCOPE_SSDT_DDL
    _EMITTER.md:104` (cross-module FKs as PostDeploy script);
  - `IndexesUnreflected` — `PhysicalSchema.fs:44` ("What's NOT
    compared. ... Indexes (non-PK) ...");
  - `StaticPopulationsUnreflected` — same docstring;
  - `CommentMetadataUnreflected` — same docstring.

The remaining STAGING.md candidates (`AttributeOrderInsensitive`,
`NewlineNormalization`, `IgnoreNoCheckClause`, `IgnoreTriggers`,
`IgnoreFingerprintHash`, `IgnoreV1OnlyKinds` etc.) are not yet
empirically active; they remain available for closed-DU expansion
when canary evidence demands them.

**Encoding rationale.** STAGING.md's proposal used a flat `bool`
record ("`IgnoreColumnLength : bool`, `IgnoreCheckConstraints :
bool`, ..."). V2 ships `Set<ToleratedDivergence>` instead because:

  1. **Pillar 1 (data-structure-oriented, no string parsing).**
     A flat-bool record is "many parallel scalars without a
     domain story." The `Set<ToleratedDivergence>` IS the
     equivalence-class definition; membership says "this
     divergence is accepted." The Set encoding makes the
     concept explicit: a `Tolerance` is a *set of accepted
     divergences*, not a row of flags.
  2. **Pillar 8 (concept-shaped naming).** Each variant names
     *what* the divergence IS, not *what to ignore*. Per the
     pillar-8 four-question domain-naming analysis: variant
     names answer "what does this represent in the cutover-
     business domain" (e.g., "the V2 emitter omits source-
     comment headers per `SsdtDdlEmitter.fs:94`"), not "what
     does the comparator do" (which is action-shaped).
  3. **Closed-DU expansion empirical-test discipline (`DECISIONS
     2026-05-13`).** Adding a flag in a `bool` record is silent;
     adding a `ToleratedDivergence` variant fires F#
     exhaustiveness errors at every match site under
     `TreatWarningsAsErrors=true`, including the in-module
     `coverage` function that `allKnown` round-trips through.
     The compile-time forcing function catches at-the-source
     omissions; the runtime cardinality test (`Closed-DU
     coverage: ToleratedDivergence.allKnown contains five
     variants`) is the second-line guard.
  4. **Smart-constructor encapsulation.** `Tolerance = private
     Tolerance of Set<ToleratedDivergence>` ensures consumers
     go through named operations (`withDivergence`,
     `tolerates`, `divergences`, `isStrict`); accidental
     construction with a wrong-shape set is impossible. Per
     the AXIOMS.md operational principle of
     structural-commitment-via-construction-validation.
  5. **Algebraic shape.** `Set` membership composes naturally
     with `Compare<Tolerance>` (S0.A): `t -> Catalog ->
     Catalog -> Diff` quotients its inputs by the accepted-
     divergence set. The bool-record shape would force every
     comparator to remember field names; the Set shape lets
     consumers iterate / fold / intersect generically.

**Smart-constructor surface.**

```fsharp
val strict          : Tolerance                      // empty set
val permissive      : Tolerance                      // all known variants
val ofSet           : Set<ToleratedDivergence> -> Tolerance
val withDivergence  : ToleratedDivergence -> Tolerance -> Tolerance
val tolerates       : ToleratedDivergence -> Tolerance -> bool
val divergences     : Tolerance -> Set<ToleratedDivergence>
val isStrict        : Tolerance -> bool
```

The `strict` ↔ `permissive` named bracket is the cutover-ladder
operational vocabulary: PROD targets `strict` per `DECISIONS
2026-05-22 — T-30 / T-15`; DEV may run `permissive` while V2's IR
matures; per-environment configuration carries its own
`Tolerance` via `ofSet`.

**What this slice does NOT include (gated on consumer demand).**

  - **Quotient operator on `PhysicalSchemaDiff`** (slice β).
    `applyTolerance : Tolerance -> PhysicalSchemaDiff ->
    PhysicalSchemaDiff` — filters the diff per accepted
    variants. Lands when the canary needs to absorb
    `HeaderCommentsOmitted` differences in the file-set
    comparator (R4 multi-env promotion).
  - **YAML / TOML deserialization for per-environment config**
    (slice γ). Lands when the cutover host shell needs to
    read environment-keyed Tolerance configurations from disk.
  - **R4 multi-environment promotion property test** — pairs
    with slice β + γ; deferred to its own slice when DEV /
    STAGING / PRE-PROD / PROD configurations have shipped.

**Test surface added.** `tests/Projection.Tests/ToleranceTests.fs`
(12 tests; `[<Fact>]` plus FsCheck `[<Property>]`) — covers
smart-constructor invariants, `allKnown` cardinality, monotonicity
of `withDivergence`, set-membership agreement of `tolerates`,
strict / permissive bracket invariants, `Compare<Tolerance>`
inhabitance.

**Pillar alignment.** Pillar 1 (typed value, no string) ✓ — every
divergence is a typed DU variant. Pillar 7 (gold-standard library
precedence) ✓ — F#'s `Set` is the canonical use-case-specific
library for the equivalence-class semantics. Pillar 8 (concept-
shaped naming) ✓ — `Tolerance` (the equivalence-class), `Tolerated
Divergence` (the named accepted divergence), variant names
(`HeaderCommentsOmitted` etc.) are concept-shaped.

**What this supersedes.** STAGING.md S0.E's bool-record sketch.
The `Tolerance` smart-constructor + `Set<ToleratedDivergence>`
encoding is the canonical shape; the STAGING.md proposal carried
a forwarding pointer (its substance was "name the flags upfront";
the substance survives, the encoding refines).

## 2026-05-10 — Chapter 4.1.B opens (CDC-aware data triumvirate; Phase 3 of V2-driver KPI critical path)

**Context.** Per `V2_DRIVER.md` per-axis correctness stakes table,
the highest-leverage single deliverable in the V2-driver KPI sequence
is the **CDC-silence-on-idempotent-redeploy property test**: V1's
MERGE applies UPDATE on every match (`StaticSeedSqlBuilder.cs:237`
unconditional), which fires CDC capture rows on identical-content
redeploys; consuming production features see "changes" that didn't
happen. This is the property the cutover team most needs proven.

**Resolution.** Chapter 4.1.B opens with `CHAPTER_4_1_B_OPEN.md`
(strategic-frame eight-axis discipline per `DECISIONS 2026-05-15`)
and ships its first two slices in this session:

  - **Slice α (commit `fd38908`).** New `Projection.Targets.Data`
    project (sibling to `Targets.SSDT` / `Targets.Json` /
    `Targets.Distributions`). `DataInsertScript` + `DataInsertRow`
    typed value foundation. `StaticSeedsEmitter v0` emits V1's
    MERGE shape parity for `Modality.Static` kinds. T11 sibling-Π
    keyset coverage; T1 byte-determinism; A18 amended (Catalog ×
    Profile, never Policy). Slice α scope: NO change-detection
    predicate yet (the CDC-noise closure lands at slice β).

  - **Slice β (commit `2d8210e`).** The load-bearing semantic
    addition. `Profile.CdcAwareness` field + `CdcAwareness` value
    type (`CdcEnabled : Set<SsKey>`, `CdcInstance : Map<SsKey,
    string>`). `StaticSeedsEmitter` now dispatches per-kind on
    `CdcAwareness.isEnabled`: CDC-enabled kinds emit the change-
    detection predicate per pre-scope §6:

    ```sql
    WHEN MATCHED AND (
        Target.[col1] <> Source.[col1] OR
        (Target.[col1] IS NULL AND Source.[col1] IS NOT NULL) OR
        (Target.[col1] IS NOT NULL AND Source.[col1] IS NULL) OR
        ...  -- repeat per non-key column
    ) THEN UPDATE SET ...
    ```

    The predicate is nullable-aware (NULL ≠ NULL in SQL) and covers
    every non-key column. Identical content → no condition fires →
    no UPDATE → CDC capture-process emits no row. CDC-disabled
    kinds keep V1's predicate-free WHEN MATCHED (V1 already proven
    correct; CDC-noise irrelevant for non-tracked tables).

**Why CdcAwareness lives on Profile, not Policy (A34 alignment).**
CDC-enabled status is *evidence the deployed schema carries*, not
intent the operator supplies. Two reasons it is Profile-shaped:

  1. **A34 (Profile is independent of Catalog and Policy).** CDC
     discovery does not reference Policy; CDC discovery is an
     empirical observation made against the deployed schema.
     Emitters that consume `CdcAwareness` are emitters that
     consume Profile evidence — `Catalog × Profile`, A18 amended.
  2. **The two-environment failure mode argues evidence over
     intent.** If `CdcEnabled` were on Policy, the operator would
     declare "this table is CDC-enabled" — but the operator does
     not own that fact in production; the cutover team enabled
     CDC and V2 must respect what *is*. Intent-shaped CDC
     declaration would let an out-of-date Policy generate a
     CDC-noise event by claiming a table is not tracked when it
     actually is.

**What this DOES NOT yet ship (gated on consumer demand).**

  - **Slice γ — CDC-silence canary** (Docker-dependent property
    test): `deploy → enable CDC → redeploy same artifact →
    cdc.fn_cdc_get_all_changes returns ∅`. The structural
    commitment IS in slice β; γ proves it operationally under
    real SQL Server CDC. Deferred until the Docker substrate is
    reliably available in the test environment.
  - **Slice δ — Two-phase insertion (DeferredFkSet).** Cycle-
    breaking for kinds in FK cycles. Lands when fixture surfaces
    a real cycle (the chapter-3.1 enterprise canary's domain FK
    chains do not cycle).
  - **Slice ε — MigrationDependenciesEmitter.** Needs a
    `MigrationDependencyContext` adapter (operator-published
    rows pickup); separate slice when the chapter-team supplies
    a fixture format.
  - **Slice ζ — BootstrapEmitter.** Pass-through `UserRemap
    Context = Map.empty` until chapter 4.2 ships.
  - **Slice η — DataEmissionComposer + EmissionPolicy.Data
    Composition DU.** Composer-level dispatch; lands at the
    second emitter (slice ε is the trigger).

**Provisional Data → SSDT dependency.** `Projection.Targets.Data`
references `Projection.Targets.SSDT` for `Render.formatSqlLiteral`
(the IR→SQL boundary primitive). Per `DECISIONS 2026-05-13 —
Emergent primitives earn their place through multi-consumer
demand`, two-consumer threshold (SSDT.RawTextEmitter +
StaticSeedsEmitter) is met but the cross-target edge is awkward;
promotion to a concept-shaped `Projection.Core.SqlLiteral` module
lands at slice ε when MigrationDependenciesEmitter joins as the
third consumer (N=3 distinct-shape pressure). Rationale documented
in `Projection.Targets.Data.fsproj` ProjectReference comment.

**Test surface.** 20 StaticSeedsEmitterTests (11 slice α + 9 slice
β): T1 byte-determinism, T11 keyset, V1 MERGE clause shape,
nullable-aware change-detection predicate (NULL-asymmetry both
ways), every-non-key-column coverage, PK exclusion, per-kind
dispatch, CdcAwareness invariants. 835 non-canary tests pass in 1s.

**Pillar alignment.** Pillar 1 (typed values; `DataInsertRow` /
`DataInsertScript` / `CdcAwareness` are concept-shaped records) ✓.
Pillar 7 (gold-standard library precedence; `Render.formatSqlLiteral`
+ ScriptDom `Render.tableQualified` / `Render.quote` reused; future
ScriptDom `MergeStatement` typed AST adoption deferred) — partial
✓ with explicit deferral. Pillar 8 (concept-shaped naming;
`StaticSeedsEmitter` / `CdcAwareness` / `change-detection predicate`
all answer "what does this represent in the cutover-business
domain") ✓.

**Phase progress.** V2-driver KPI Phase 3 (chapter 4.1.B) is now
actively in flight. Phases 1 (foundations), 2 (chapter 4.1.A
schema), and the in-flight Phase 3 substantive surface together
represent the bulk of the V2-driver structural commitment. Phase
3 close depends on slice γ canary green; slice γ defers until
Docker is reliably available.

## 2026-05-10 — Text-builder-as-first-instinct discipline (chapter 4.1.A close arc; pillar 1 + pillar 7 amendment; Tier-3 codification)

**Context.** During the chapter 4.1.A close arc + Tier-1 transitions
(this session: RawTextEmitter retirement; SqlLiteral typed module;
MERGE → ScriptDom MergeStatement; Outputs → SsdtBundle + JsonNode), a
recurring pattern surfaced: **the agent's first instinct on a new
SQL- or text-emitting consumer is to build via StringBuilder + concat,
not via the typed-AST library**. StaticSeedsEmitter slices α + β
shipped with 6 LINT-ALLOWs at exactly this site; the Tier-1 #1 cash-
out (`bface9a`) retired all 6 by routing through `ScriptDomBuild
.buildMergeStatement`. Same shape: the substance of the work was
typed AST construction, not text composition; the agent's first draft
went the other way.

**Failure mode named: "text-builder-as-first-instinct."** The agent
reaches for `StringBuilder` / `String.Concat` / `String.concat` /
`sprintf` as the default for new emitters, then attaches LINT-ALLOWs
once the lint surfaces. The LINT-ALLOWs are individually defensible
(per the substantive-rationale discipline; `DECISIONS 2026-05-10 —
LINT-ALLOW substantive-rationale`); the *aggregate* is the bug. Six
LINT-ALLOWs at one MERGE site means the typed-AST migration was
never attempted in the first place — the discipline failed at the
first-draft stage. Mirror of the chapter 3.7 slice β shortcut
(`Render.columnSqlType` String.Concat → ScriptDom typed AST cost 87
LOC); same pattern, different consumer.

**The discipline.** **Every new SQL- or text-emitting consumer
starts on the typed-AST library, not StringBuilder. LINT-ALLOWs are
exit-not-entry — the LINT-ALLOW marker is the audit trail for an
EXIT from the typed-AST path that the four-question analysis
*forced*, not the audit trail for skipping the typed-AST path
entirely.** The protocol at consumer-construction time:

  1. **First instinct check**: BEFORE writing the first
     `StringBuilder()` or `String.Concat`, the agent must articulate
     the typed-AST library that produces the structure being
     emitted (ScriptDom `MergeStatement` / `CreateTableStatement`
     / `InsertStatement`; `Utf8JsonWriter` / `JsonNode`; `XmlWriter`
     / `XDocument`; `Microsoft.SqlServer.Dac` for .dacpac; etc.).
     If no such library exists, document the absence.
  2. **Cross-check the precedent emitters**: how does
     `SsdtDdlEmitter.emitSlices` build CREATE TABLE? How does
     `StaticSeedsEmitter.renderMerge` build MERGE? How does
     `JsonEmitter.emit` build doc trees? The precedent IS the
     pattern; new consumers inherit it.
  3. **First draft uses the typed AST.** Period. The
     StringBuilder reflex is suppressed at construction time, not
     at lint time.
  4. **LINT-ALLOWs at terminal text boundaries only.** The
     remaining sites — `SqlLiteral.toString`'s `'<raw>'` quoting,
     the GO-batch suffix on a rendered MERGE, the cross-platform-
     deterministic relative-path concatenation — are the exit
     points where the typed AST has already discharged its work
     and the absolute terminal text emerges. Each LINT-ALLOW
     embodies the four-question analysis (per the substantive-
     rationale discipline); the aggregate count per file is the
     soft floor (per Lint Rule 27).

**Worked counterfactuals (from this session):**

  - **StaticSeedsEmitter slice α + β (`fd38908` + `2d8210e`)**:
    shipped with 6 LINT-ALLOW StringBuilder MERGE construction.
    Tier-1 #1 (`bface9a`) retired all 6 via ScriptDom
    MergeStatement typed AST. Cost: 150 LOC of typed-AST
    construction, replacing 80 LOC of StringBuilder. Net: more
    code, but pillar-1 + pillar-7 alignment, future-emitter
    precedent, change-detection predicate is a typed boolean-
    expression AST (instead of string concat).
  - **`Render.columnSqlType` chapter 3.7 slice β shortcut**: 4
    LINT-ALLOWs added; user caught it; slice β' delegated to
    ScriptDom typed AST. Same failure mode, prior chapter.

**Tier-1 discipline cash-outs (this session):**

  - **#4 — SqlLiteral typed expression module (`08ca554`)**: Core-
    resident typed `SqlLiteral` DU; `Render.formatSqlLiteral`
    delegates; consumers (SSDT.Render + Data.StaticSeedsEmitter)
    flow through the typed middle layer. Unblocked #1.
  - **#1 — MERGE → ScriptDom MergeStatement (`bface9a`)**: 150
    LOC of typed-AST construction in `ScriptDomBuild
    .buildMergeStatement` (record-shaped MergeBuildArgs + per-
    column predicate builders). 6 LINT-ALLOWs retired. The
    change-detection predicate is now `BooleanBinaryExpression
    (Or)` of `BooleanComparisonExpression(NotEqualToBrackets)` +
    `BooleanIsNullExpression` AST nodes wrapped in
    `BooleanParenthesisExpression`.
  - **#2 — Compose.Outputs.Sql → SsdtBundle (`705e31d`)**: chapter-
    3-era single-blob `Sql : string` retired in favor of `Map<
    RelativePath, string>` per `SsdtBundle.compose` (the
    production shape from chapter 4.1.A slice 10). The Pipeline
    `write` iterates the bundle; `Deploy.runEphemeral` consumes
    `Compose.aggregateSsdt` for the legacy single-string deploy
    contract.
  - **#3 — Compose.Outputs.Json + .Distributions → JsonNode
    (`22ecc59`)**: chapter 3.7 slice ε's per-kind typed JsonNode
    surface lifted to the Outputs seam. Consumers query the typed
    tree (no `JsonNode.Parse` re-parse). Pillar 1 holds end-to-end
    across the Pipeline composition surface.

**Tier-2 audit conclusions (this session):**

  - **Connection-string composition tightening**: zero raw-concat
    sites outside `Deploy.ConnectionString.parse` (the smart
    constructor over `SqlConnectionStringBuilder`). Already tight.
    No retirement.
  - **`BatchSplitter` line-fold fallback**: structurally defended
    via the loud-fallback pattern. Per pillar 7 four-question:
    ScriptDom is the gold standard; line-fold is the explicit
    operator-facing escape hatch with stderr announcement (per
    chapter-3.6 cash-out). The discipline is satisfied; the
    fallback is not retire-able without losing the operator-
    rationale-defended escape hatch.
  - **Path composition audit**: every `String.Concat` for paths
    has a substantive cross-platform-deterministic LINT-ALLOW per
    the four-question analysis. The `BenchSink.fs` site uses
    `Path.Combine` correctly (timestamp pre-vetted). No drift.

**Tier-3 codification (this entry).** The DECISIONS entry IS the
codification; the Active deferrals index entries (above this entry
in the table) name the chapter-specific incarnations:

  - "Microsoft.SqlServer.Dac (DacFx) adoption in Projection.Targets
    .SSDT.DacpacEmitter" — chapter 3.x. Hard requirement.
  - "MigrationDependenciesEmitter + BootstrapEmitter typed-AST
    adoption from slice α" — chapter 4.1.B slices ε/ζ. Hard
    requirement; precedent is StaticSeedsEmitter (`bface9a`).

The chapter-close ritual scans the Active deferrals table at every
chapter close (per `DECISIONS 2026-05-13 — Transform registry cash-
out + Active deferrals index`); these new rows surface to the
agent as triggers when the relevant chapter opens.

**Operating disciplines table update (CLAUDE.md + AGENTS.md).** A
follow-on commit adds this discipline to the operating-disciplines
table — sibling to "Domain-first naming and ubiquitous-language
consistency" (pillar 8) and "LINT-ALLOW substantive-rationale
discipline" (pillar 7 amendment). Together the three disciplines
form the codified failure-mode set: **performance-of-compliance**
(LINT-ALLOW shaped like an audit trail without substance) +
**domain-blind naming** (name shaped like a placeholder for an
absent domain concept) + **text-builder-as-first-instinct**
(typed-AST library is the first instinct, not the lint-time
fallback).

**Pillar alignment.** Pillar 1 (data-structure-oriented over
string-parsing) ✓. Pillar 7 (gold-standard library precedence;
substantive-rationale amendment) ✓. Pillar 8 (concept-shaped
naming) ✓ — `text-builder-as-first-instinct` IS the failure-mode
name (concept-shaped, not action-shaped).


## 2026-05-10 — Perf-gate μ+σ statistical baseline (drops rolling history)

**Status:** decided

**Context:** the chapter-3.6 `scripts/perf-gate.sh` design (codified
at `2026-05-09 — Operator-reality canary as the production-baseline
perf gate`) used a **rolling history** model: each gate-fire
appended a snapshot to `bench/history-canary.jsonl` (max N=20),
and the per-label threshold computed as `μ + Kσ` over the
history with a flat-tolerance warm-up phase (`mean × 1.5`) until
N≥5 history accumulated. The committed `bench/baseline-canary.json`
served the warm-up phase and as a reference floor.

The chapter 4.1.A static-population regression (`commit 651d6a4`)
surfaced two structural problems with this design:

1. **The committed baseline was synthetic.** `bench/baseline-canary.json`
   was recorded once with placeholder 500ms-each entries (every
   `Count`, `MinMs`, `MaxMs`, `MeanMs`, `P50Ms`, `P95Ms`, `P99Ms`
   identical at 500). No real measurement set was ever committed.
   The warm-up gate (`current < 1.5 × baseline`) was therefore
   gating against fiction — every label below 333ms passed
   irrespective of regression; every label above 500ms failed
   irrespective of fidelity.

2. **History is per-machine, gitignored, ephemeral.** `bench/history-canary.jsonl`
   is gitignored (`.gitignore:25-27` keeps `bench/baseline-*.json`
   tracked, ignores everything else under `sidecar/projection/bench/`).
   Every fresh checkout starts at N=0; CI doesn't accumulate
   history across runs without artifact storage; cross-machine
   regression detection isn't structural — it's per-machine.
   When the static-population regression shipped against this
   baseline-and-history pair, neither layer engaged: warm-up
   compared against synthetic 500s, and the previous machine's
   history didn't exist on the next agent's clone.

The compounding effect: the perf-gate was effectively a placebo
across the chapters that shipped against it (3.5 / 3.6 / 3.7 /
4.1.A / 4.1.B-α/β/γ / RawTextEmitter retirement / Tier 1/2/3
transitions). The static-population regression slipped past
every gate-fire because the gate had no real floor to compare
against.

**Decision:** replace the rolling-history model with a **tracked
μ+σ statistical baseline**. The committed `bench/baseline-canary.json`
IS the statistical model; there is no rolling history accumulator.

**New baseline format:**

```json
{
  "RecordedAtUtc": "2026-05-10T17:42:00Z",
  "Tag": "operatorReality",
  "Runs": 5,
  "Stats": [
    { "Label": "deploy.bulk.copyRows", "SampleCount": 5,
      "MeanMs": 3048, "StdevMs": 250 },
    ...
  ]
}
```

Each `Stats[i]` entry carries per-label `MeanMs` + `StdevMs`
computed from N≥5 warm captures (the `Runs` field). Per-label
threshold = `MeanMs + K × σ_effective` where:

  - `K = 5.0` (default; widened from the prior K=3.0 to absorb
    cross-machine timing variance — CI ↔ dev laptop can drift
    2-3σ even on the same workload).
  - `σ_effective = max(StdevMs, MeanMs × MIN_RELATIVE_STDEV)`
    (default `MIN_RELATIVE_STDEV=0.20`, a 20% relative σ floor).
    The floor is a Bayesian prior on σ: at N=5, `StdevMs` often
    underestimates the true population σ — particularly for I/O-
    bound labels (Docker container creation, SqlBulkCopy network
    round-trips, ScriptDom parser warmup) whose run-to-run
    variance is dominated by external jitter rather than algorithm
    timing. Without the floor, a baseline that happened to record
    five tightly-clustered samples produces a brittle gate
    (`σ_observed = 0.4ms` on a 7ms label gives a 9ms threshold;
    legitimate jitter to 14ms trips the gate). Tightening
    `MIN_RELATIVE_STDEV` is appropriate when the baseline came
    from a representative-spread run set; loosening (e.g., 0.30)
    is appropriate during initial calibration on a new machine
    class.

**Recorder mechanism:**

  - `PERF_GATE_RECORD=1 scripts/perf-gate.sh` runs the operator-
    reality canary `BENCH_RECORD_RUNS` times (default 5),
    aggregates per-label `TotalMs` across runs into μ + σ, writes
    the new baseline file. The per-label noise filter (drop labels
    with `MeanMs < BENCH_MIN_MS=5`) applies symmetrically at
    record + gate time so the baseline carries only signal-
    bearing labels.
  - When the perf floor legitimately changes (algorithmic
    improvement; new workload axis; intentional accommodation of
    a new label-emitting hot path), the recorder is run and the
    new baseline is committed. The commit pairs with a DECISIONS
    amendment naming the new floor's rationale.

**Gate mechanism:**

  - `scripts/perf-gate.sh` runs the canary once, compares each
    label's `TotalMs` against the baseline's threshold, fails on
    any regression. New labels (not in the baseline; e.g., a
    feature added new bench scopes) pass with a soft warning —
    they join the baseline at the next record cycle.
  - Soft-skip on Docker / dotnet unavailable preserved.

**What this supersedes:**

  - The rolling-history accumulator (`bench/history-canary.jsonl`)
    retires structurally. The .gitignore rule that ignored it
    stays (the file may exist locally as orphan; it's not
    consulted). Removing the rule isn't necessary because the
    file is no longer written to.
  - The warm-up phase retires — the baseline is always the
    statistical model, no fallback to flat-tolerance.
  - `BENCH_TOLERANCE` env var retires (no flat-tolerance
    fallback). `BENCH_MAX_HISTORY` and `BENCH_MIN_SAMPLES`
    retire (no rolling history).
  - The 2026-05-09 entry's "Mechanism" section's per-label
    `μ + Kσ` description over rolling history is superseded by
    this entry; the rest of the 2026-05-09 entry (operator-
    reality as the gate workload; soft-skip semantics; pillar
    7 alignment) stays in force.

**What stays:**

  - Operator-reality as the gate workload (50k rows × 300 tables
    × variegated; `GenerateSpec.operatorReality`).
  - The bench JSON snapshot path convention (`bench/canary/<utc>.json`
    written by `BenchSink.persistJson` from the test process,
    with `PROJECTION_BENCH_DIR` for path resolution).
  - The `.gitignore` carveout (`!sidecar/projection/bench/baseline-*.json`)
    — the new baseline file is the same path; tracked.
  - The Stop-hook + pre-commit hook integration. The gate
    runtime is unchanged (one canary run; ~12s warm).

**Reasoning:**

  - **Cross-machine reproducibility.** Every contributor + CI
    gates against the same baseline. A regression on a dev
    laptop surfaces as the same regression on CI (modulo K).
    The previous design had cross-machine convergence only as
    a side effect of warm-up + N runs of accumulated history;
    the new design gives it as a structural invariant.
  - **PR-visible floor bumps.** Re-recording the baseline
    produces a tracked-file diff in the PR. Operators reviewing
    the PR see exactly which labels changed and by how much —
    the perf-floor evolution is in the git history, alongside
    the DECISIONS amendment naming why the floor moved.
  - **No warm-up phase confusion.** The synthetic baseline
    + warm-up failure-mode is structurally impossible: the
    baseline is computed from real measurements at record
    time; if the recorder hasn't run, the gate emits a clear
    "no baseline; run `PERF_GATE_RECORD=1` to seed" warning
    rather than gating against fiction.
  - **Cross-machine variance absorbed by K.** The default
    K=5.0 is calibrated for ~3σ of legitimate machine-to-
    machine timing variance plus ~2σ of run-to-run variance.
    Dev laptops with thermal throttling or shared-CI runners
    with noisy neighbors will land within K=5σ of the recorded
    mean for any label that's intrinsically deterministic
    (no I/O contention). Tightening K (e.g., to K=3.0) is
    appropriate when the recorded baseline came from runs on
    the same machine class as the gate fires; loosening
    (K=7.0) is appropriate during initial calibration.
  - **Two operative env vars** (vs four under the prior design):
    `BENCH_K_SIGMA` (gate width), `BENCH_RECORD_RUNS`
    (sample count for record mode). Plus the legacy
    `BENCH_MIN_MS` (noise floor). Reduced surface area; less
    operator confusion at gate-fire.

**Tradeoff:** the prior design's per-machine adaptation is lost.
A dev laptop with consistently slower bulk-copy throughput than
CI will trip the gate unless K is widened to absorb the
machine-class shift. Mitigations: (a) run the recorder on the
machine class the gate will fire on (CI + every contributor's
laptop, when calibrating); (b) widen K when cross-machine
variance is the dominant signal; (c) accept the variance as
a structural feature — the gate fires when *any* machine sees a
regression, which is more conservative than fires-when-this-
machine sees a regression.

**Implementation.** `scripts/perf-gate.sh` rewritten:

  - Default mode: one canary run + Python-3 gate logic that
    loads `bench/baseline-canary.json` + per-label `latest_ms
    > MeanMs + K × StdevMs` check.
  - Record mode (`PERF_GATE_RECORD=1`): N canary runs + Python-3
    aggregator that computes per-label μ + σ and writes the
    new baseline file. Default `BENCH_RECORD_RUNS=5`.
  - Header documentation rewritten; usage examples updated.

**Pillar alignment:**

  - **Pillar 1** (data-structure-oriented over string-parsing) ✓
    — the baseline is typed JSON with explicit per-label
    `MeanMs` / `StdevMs` / `SampleCount`; the gate logic reads
    them directly without parsing aggregate strings.
  - **Pillar 5** (deep separation of concerns) ✓ — the
    statistical model is a separate file from the runtime
    snapshot; record-time + gate-time concerns are explicit
    code paths.
  - **Pillar 6** (no V2-internal back-compat paths) ✓ — the
    legacy rolling-history file path retires structurally;
    no shim, no migration, no "still read history if present"
    branch.
  - **Pillar 7** (gold-standard library precedence;
    substantive-rationale + perf-clause) ✓ — pure-Python
    aggregation (BCL standard library); the perf-clause
    discipline is what this gate enforces; the gate IS the
    perf evidence per the iterator-logging discipline.
  - **Pillar 8** (domain-first naming) ✓ — `MeanMs` /
    `StdevMs` / `SampleCount` are concept-shaped; "μ+σ
    statistical baseline" is the load-bearing concept the
    domain operates on.

**Surprise worth flagging.** The synthetic baseline existed
across `2026-05-09` (the perf-gate codification entry) →
`2026-05-10` (this entry) without a real measurement set ever
being committed. The `chapter-close ritual` (eight items per
CLAUDE.md operating-disciplines table) does NOT explicitly
walk the perf-gate baseline's currency. Future amendment to
the chapter-close ritual: add a 9th item — "perf-gate
baseline currency check (recorded at most one chapter ago;
threshold within K=3σ of last actual run)." Defer the
amendment to the next chapter close that operates the ritual
in full.





---

## 2026-05-10 — Chapter 3.2 close: `SnapshotRowsets` variant cash-out + JSON-projection-lossiness class structurally resolved

**Cashed out** the **SnapshotRowsets variant** trigger from the
Active deferrals index. The deferral was first logged at 2026-05-17
(OSSYS adapter parse signature; session-20 amendment) and pre-scoped
at chapter-2 close (subagent #5 → `CHAPTER_3_PRESCOPE_SNAPSHOT_ROWSETS.md`).
Chapter 3.2 implements end-to-end across five substantive slices.

**Slices shipped.**

| Slice | Commit | Scope | Lossiness members resolved |
|---|---|---|---|
| **1** | `6dab9cd` | `SnapshotRowsets` DU variant + `RowsetBundle` DTO carrier + `ModuleRow`/`KindRow`/`AttributeRow` records + `parseRowsetBundle` minimum + first fixture mirroring session-18 minimal | SsKey at all three levels (module / kind / attribute) |
| **2** | `0354727` | Reference rowsets (`#RefResolved` ⊕ `#FkReality`); FK SsKey carriage + rule 16 same-module assumption tested under rowset path | Reference SsKey synthesis empirically validated |
| **3** | `d5d1812` | `EspaceKind` activation; `parseOriginFromRowset` three-way real | Rule 17 refined from JSON-path placeholder to OsNative / ExternalViaIntegrationStudio / ExternalDirect |
| **4** | `6eae21f` | `IsSystemEntity` activation; new `ModalityMark.SystemOwned` variant | Third known JSON-projection-lossiness class member resolved |
| **5** | `a74b904` | Cross-source parity tests (JSON ↔ Rowset) — total-equality (no-Guids) + shape-equality (Guid-carrying) | No new lossiness; validates structural equivalence modulo documented SsKey divergence |
| **post** | `0336795` | `propagateOrFallback` codification — error-propagation bug surfaced during slice 2 audit, backported to JSON path; seven build-failure sites refactored uniformly | (Bug fix; not new lossiness — surfaced under chapter-3.2 audit pressure) |

**JSON-projection-lossiness class — structural disposition.**

Per `DECISIONS 2026-05-19 — naming the two classes of resolution
patterns explicitly`, the class has three known members. All three
landed in chapter 3.2:

  - **SsKey at every level** (slice 1). `EspaceSSKey` / `EntitySSKey` /
    `PrimaryKeySSKey` / `AttrSSKey` carry through the rowset bundle
    as `Guid option`; translation emits `SsKey.OssysOriginal guid`
    when present, falls back to `SsKey.Synthesized` when absent
    (per A1's four-variant amendment shipped at Stage 0 S0.B).
  - **`EspaceKind`** (slice 3). String column on V1's `ossys_Espace`
    carries via `ModuleRow.EspaceKind : string option`;
    `parseOriginFromRowset` consumes it to discriminate Origin
    three-way (case-insensitive `"Extension"` marker per
    chapter-3.2 empirical evidence; documented in the
    `parseOriginFromRowset` docstring).
  - **`IsSystemEntity`** (slice 4). Bool column on V1's
    `ossys_Entity.Is_System` carries via `KindRow.IsSystemEntity`;
    lifts into V2's IR as `ModalityMark.SystemOwned` (payload-free
    variant in the existing orthogonal-axes list pattern; rejected
    flat `Kind.IsSystem: bool` per V2 IR boolean-avoidance
    convention; rejected `Origin` axis split per orthogonality;
    rejected new `Kind.Stewardship` DU per two-consumer threshold).

Future class members (per-table column structure that
`FOR JSON PATH` collapses; check-constraint definitions; triggers)
surface under fixture pressure as further deferred slices — the
structural foundation (RowsetBundle as a flat-list carrier joinable
on FK ID columns; `propagateOrFallback` for boundary error
propagation; closed-DU expansion empirical-test discipline) is
established.

**A1's JSON-projection-lossiness bound — operational disposition.**

Chapter 3.2 makes A1's `OssysOriginal` variant **operationally
reachable** for the first time at the OSSYS-adapter boundary. The
four-variant amendment shipped at Stage 0 S0.B encoded the bound
type-stratifically; chapter 3.5's `RefactorLogEmitter` was the
first downstream consumer that pattern-matched on the variants;
chapter 3.2 is the first **boundary** that *emits* `OssysOriginal`
SsKeys directly from V1's actual `SS_Key` columns. A1's bound is
no longer "operational placeholder" — it is one fixture away from
production identity stability.

**Cross-source SsKey-shape divergence — operational disposition.**

Per `CHAPTER_3_2_OPEN.md` axis 5 (option 1 from pre-scope §4): the
JSON path emits `Synthesized` SsKeys; the rowset path emits
`OssysOriginal` SsKeys when Guids are present, `Synthesized`
otherwise. Both paths produce structurally-equivalent Catalogs
modulo this identity-shape divergence (slice 5 cross-source parity
tests establish this empirically across all three fixture classes:
minimal, reference-bearing, external-aligned-at-Extension).

The deeper canonicalization (option 2: a `V1Mapped` SsKey carrying
both forms via UUIDv5 derivation per `UuidV5.create`) is reserved
for **chapter 4.2 User FK reflow's `SourceTag` refactor**, where
cross-version identity stability becomes the load-bearing
discipline. Chapter 3.2 establishes the source variants;
chapter 4.2 will harmonize them under V1Mapped.

**Rule 16 (same-module FK assumption) — disposition unchanged.**

Slice 2 tests rule 16's same-module assumption against the rowset
path's flat-list reference shape. Same-module FK round-trips
cleanly; cross-module FK case remains the **Cross-module FK IR
refinement** Active deferral (highest-priority deferred slice per
the index; trigger condition: a fixture exercising cross-module
FK). The rowset path is structurally ready for cross-module FK
extension when the fixture surfaces; the deferral does not impede
chapter 3.2 close.

**`propagateOrFallback` codification — audit-during-validation worked precedent.**

The chapter 3.2 close-prep audit surfaced an error-propagation bug
across SEVEN build-failure sites in CatalogReader.fs. The pattern:
build functions assembled a target from N intermediate `Result<_>`
values; the failure branch swallowed underlying error codes under
generic umbrella codes (`kindBuild` / `moduleBuild` /
`attributeBuild` / `referenceBuild` / `indexBuild` plus rowset
siblings). The fix: codify `propagateOrFallback` at the two-
consumer threshold; refactor seven sites uniformly. Test surface:
two new JSON-path regression tests (`unmapped DeleteRuleCode
propagates`; `unmapped DataType propagates`) assert positively
(substantive cause appears) AND negatively (umbrella codes do NOT
appear). The audit-during-validation discipline (`DECISIONS
2026-05-09 — Audits surface things not on the agenda`) operated as
designed — the bug surfaced during slice 2 work, expanded under
chapter close-prep audit pressure, and shipped end-to-end in
commit `0336795` BEFORE chapter close ritual ran.

**Chapter close arc lessons.**

Three patterns held cleanly across chapter 3.2:

  - **Closed-DU expansion empirical-test discipline** (`DECISIONS
    2026-05-13`). Two record-style extensions
    (`RowsetBundle.References` at slice 2; `RowsetBundle` field
    additions at slices 3/4) and one DU variant extension
    (`ModalityMark.SystemOwned` at slice 4). F# exhaustiveness
    errors lit up only at predicted interpretation sites; no
    caller reshaping outside the variant's module. Four
    interpretation sites for `ModalityMark.SystemOwned`
    (CanonicalizeIdentity / NamingMorphism /
    NormalizeStaticPopulations / JsonEmitter.modalityString); each
    got an identity-shape branch. The discipline survives the
    record-extension generalization.

  - **Two-consumer threshold for emergent primitives** (`DECISIONS
    2026-05-13`). `propagateOrFallback` extracted at consumers 2 + 3
    + 4 (rowset path's two sites + JSON path's parseKind + parseModule);
    the broader audit surfaced the helper now serves 7 consumers
    uniformly. The threshold's predictive power held —
    the helper's shape was concrete by consumer 2; consumer 3's
    demand crystallized the extraction.

  - **Trace-before-fixture pattern** (`DECISIONS 2026-05-19`). Each
    of slices 2-4 re-did an already-traced V1↔V2 fixture under the
    rowset path. The three-class typology (`DECISIONS 2026-05-21`)
    operated: all chapter 3.2 findings classified as **JSON-
    projection-lossiness** (V2 can't see X through JSON; resolved
    by input-path expansion). The structural foundation for V1↔V2
    translation work is now the chapter-3.2 surface; future
    fixtures that surface alternative-IR-surface or
    V2-boundary-discipline class members route to other chapters.

**Test baseline at chapter 3.2 close.** 882 non-canary tests
passing; 0 skipped; lint clean across 27 rules. Chapter 3.2 added
30 new rowset tests + 2 JSON-path regression tests + 7 IR-
refinement coverage tests across `ModalityMark.SystemOwned`'s four
interpretation sites. Build clean under `TreatWarningsAsErrors=true`.

**Forward signal.**

Chapter 3.2 closes the SnapshotRowsets variant deferral structurally.
The remaining V2-driver KPI critical path (per `V2_DRIVER.md`):

  - **Chapter 4.1.B slice δ** (two-phase insertion / cycle-breaking;
    CDC-silence-on-idempotent-redeploy property test is the V2-
    driver KPI's highest-leverage single deliverable).
  - **Chapter 4.1.B slices ε/ζ** (MigrationDependencies + Bootstrap;
    `ScriptDomBuild.buildMergeStatement` adoption mandatory per
    Active deferrals row).
  - **Chapter 3.x DacpacEmitter** (DacFx adoption mandatory per
    Active deferrals row).
  - **Cross-module FK IR refinement** (highest-priority deferred
    slice in the index; trigger: fixture exercising cross-module
    FK).

Chapter 3.2's JSON-projection-lossiness class resolution unblocks
A1 for renames at the boundary, which downstream-unblocks chapter
4.2 User FK reflow's `SourceTag` / `V1Mapped` UUIDv5 work.

---

## 2026-05-11 — Chapter 4.1.B close + two deferred-item codifications

**Status:** decided

**Context:** Chapter 4.1.B (CDC-aware data triumvirate; V2-driver KPI
Phase 3 — the highest-leverage chapter in the entire critical path)
closes end-to-end. Per `CHAPTER_4_1_B_CLOSE.md`: slice α (StaticSeeds)
+ β (CDC-aware MERGE) + γ (CDC-silence canary GREEN) + δ (two-phase
insertion) + ε (MigrationDeps; Tier-3 cash-out) + ζ (Bootstrap stub)
+ η (DataEmissionComposer + DataComposition DU) + θ (partition
assertion + OverlappingEmitterCoverage) + ι (composeRendered global
ordering) + κ (typed DataInsertRow.Values pillar 1 lift) all shipped.
Test baseline 893 non-canary green; lint clean; canary suite hang
fix shipped (Docker-SqlServer xUnit collection + dedicated CdcSilence
container).

**Decision:** Two new deferrals codified for the Active deferrals
index. Both surfaced organically during the slice arc but were
deferred per IR-grows-under-evidence + two-consumer-threshold; the
codification records the structural trigger so future agents catch
the cash-out moment.

**Deferral A — Statement DU MERGE/UPDATE promotion.**

`Projection.Core.Statement` DU today carries SSDT DDL variants
(`CreateTable / CreateIndex / InsertRow / SetIdentityInsert /
Comment / Blank`). MERGE and UPDATE statements (chapter 4.1.B's
data emission shapes) are NOT modeled by `Statement`; instead, the
data emitters call `ScriptDomBuild.buildMergeStatement` /
`buildUpdateStatement` directly + render the typed AST via
`ScriptDomGenerate.generateOne` + terminate with `;\nGO\n` at a
LINT-ALLOW'd text boundary. This produces 3 LINT-ALLOWs per data
emitter (MERGE statement-terminator; UPDATE statement-terminator;
per-kind concat) — 6 total across `StaticSeedsEmitter` +
`MigrationDependenciesEmitter`.

Promoting `Statement` to include `Merge of MergeBuildArgs | Update
of UpdateBuildArgs` would let `ScriptDomGenerate.toText` handle
per-kind concat structurally and retire all 6 LINT-ALLOWs. Cost:
- Extend `Statement` DU + `ScriptDomBuild.buildStatement`
  exhaustive match (lights up at one site).
- Cross-target dep: `Statement` lives in `Projection.Core`, so the
  MergeBuildArgs / UpdateBuildArgs records must lift to Core too
  (today they're in `Projection.Targets.SSDT.ScriptDomBuild`).
- Risk: every existing `Statement` pattern-match site outside the
  emit pipeline (today: zero — `Statement` is only consumed by
  `ScriptDomBuild.buildStatement` + `ScriptDomGenerate.toText`).

**Trigger to cash out:** a third MERGE/UPDATE consumer lands (e.g.,
chapter 3.x DacpacEmitter Phase-2 path, future Faker-style data
emitter, future Profile-attached row source in chapter 4.3). At
that point the per-site LINT-ALLOW count justifies the typed-
Statement promotion; today the two-consumer threshold (StaticSeeds
+ MigrationDeps) doesn't.

**Reasoning / consequences:** The deferral is the right call today
per the two-consumer threshold; the cost of the cross-target lift
is substantial relative to the LINT-ALLOW count. The codification
ensures future agents at the third-consumer surface know exactly
which structural commitment to inspect first.

**Deferral B — Sort-vs-data deferral predicate distinction.**

V2 has TWO distinct predicates that consume cycle metadata:

1. **Sort-edge breakability** (chapter 2; `Strategies/CycleResolution.fs:
   classify`): an FK edge is `Weak` IFF `OnDelete ∈ (NoAction |
   SetNull) ∧ source.IsNullable = true`. Used by
   `TopologicalOrderPass.applyResolver` to decide which precedence-
   graph edges the resolver may break to produce a topological
   ordering.

2. **Data-emission deferral** (chapter 4.1.B slice δ;
   `StaticSeedsEmitter.deferredColumns`): an FK column is deferred
   IFF `target ∈ cycleMembers ∧ source.Column.IsNullable = true`.
   Used by the two-phase Phase-1 NULL substitution / Phase-2
   UPDATE pattern. V1 reference: `IdentifyNullableFKColumns:184`.

**The predicates diverge** on Cascade-nullable FKs: V2's
`CycleResolution.classify` returns `Cascade` (NOT `Weak`), so the
sort-edge-breaker refuses to break it for sort purposes. But V2's
data-emission `deferredColumns` DOES defer it (Cascade behavior is
about DELETE; the column is nullable so we can NULL it in Phase-1
and restore in Phase-2). This matches V1's empirical behavior:
`IdentifyNullableFKColumns:184` checks only nullability, ignoring
`OnDelete`.

**The two questions are sibling-but-distinct:**

| Question | Predicate | Locus |
|---|---|---|
| Can the resolver break this precedence edge for SORT purposes? | `(NoAction \| SetNull) ∧ nullable` | `CycleResolution.classify` |
| Can the emitter DEFER this FK column across two-phase INSERT/UPDATE? | `in-cycle ∧ nullable` | `<Emitter>.deferredColumns` |

**Codified discipline:** future emitters that consume cycle
metadata SHALL choose the predicate that fits their semantic
question explicitly. Don't import `CycleResolution.classify` if the
intent is data deferral; don't reimplement nullability checks if
the intent is sort breakability.

**Trigger to escalate** (i.e., promote one predicate to be derived
from the other, or otherwise codify their relationship at the type
level): a third predicate consumer surfaces (e.g., a third axis
that asks a sibling-but-distinct cycle question), at which point
the closed-DU + multi-predicate abstraction earns its place.
Today's two-predicate split is the minimum that names the
distinction; expansion follows consumer evidence.

**Reasoning / consequences:** Naming the distinction prevents the
performance-of-compliance failure mode (a future emitter agent
might import `CycleResolution.classify` thinking it's "the cycle
predicate," producing subtle Cascade-nullable data-emission bugs).
The pillar-7 substantive-rationale discipline applies: when a
generic name (`classify`, `deferred`) hides a load-bearing
distinction, NAME the distinction.

---

## 2026-05-11 — Chapter 4.2 close + A32 cash-out + two new deferrals

**Status:** decided

**Context:** Chapter 4.2 (User FK reflow; V2-driver KPI Phase 4)
closes end-to-end. Slice arc α → η shipped on branch
`claude/chapter-4-ddd-improvements-XVCAM` (commits `17930c2` →
`08a75cf`). The chapter signature deliverable — the multi-
environment commutativity property test — is green. Test baseline
963 non-canary passing; lint clean.

**Decision A — A32 cashed out.** Per `CHAPTER_4_2_CLOSE.md` §8 +
the AXIOMS.md A32 cash-out body. The scheduled "passes may produce
values consumed by emitters" axiom (originally codified 2026-05-06)
becomes a wired template with chapter 4.2's `UserFkReflowPass.
discover` → `UserRemapContext` → `MigrationDependenciesEmitter.
emitWithUserRemap` chain. The property test specializes T4
(sibling functor commutativity) to A32's worked example via the
multi-environment commutativity property (slice η).

**Decision B — OSSYS adapter User-kind identification surface
deferral.** Chapter 4.2 ships the IR refinement (`Reference.
IsUserFk`) + the emitter integration (slice η rewrite at
MigrationDependenciesEmitter). The OSSYS adapter currently sets
`IsUserFk = false` for every Reference because resolving the
platform-user-kind identity requires V1's `extension_id` lookup
pattern (per `ModelUserSchemaGraphFactory.GetSyntheticUserForeignKeys`).
**Trigger to cash out**: a real OSSYS-source-V2-target reflow
workflow surfaces with User-FK columns operators need rewritten.
At that point the OSSYS adapter gains a
`userKindIdentity : Catalog -> SsKey option` resolution surface;
references whose `TargetKind` matches the identified user kind
get `IsUserFk = true`. Slice η emitter integration is
structurally complete; the deferral is at the adapter boundary
only.

**Decision C — CSV adapter for `ManualOverride` deferral.**
Pre-scope §3 names `Projection.Adapters.UserMap.UserMapLoader`
(CSV: `SourceUserId,TargetUserId,Rationale`). Slice ε ships
`ManualOverride` consuming a programmatic `Map<SourceUserId,
TargetUserId>`; the I/O adapter at the boundary is deferred.
**Trigger to cash out**: a real operator workflow demands the
file-format pickup path. Mirrors the chapter 4.1.B slice ε
NDJSON-adapter deferral — same shape, sibling chapter.

**Reasoning / consequences.** Both deferrals are at I/O / boundary
layers that don't gate the pure-F#-core algebraic claim. Chapter
4.2's structural commitments hold at the type level today;
real-cutover-workflow consumer pressure will trigger the
boundary-adapter cash-outs when operationally needed.

---

## 2026-05-11 — Chapter 4.1.A slices 6/7/8 disposition

**Status:** decided (slice 6 shipped; slices 7-default + 8 deferred)

**Context:** Per `CHAPTER_4_PRESCOPE_SSDT_DDL_EMITTER.md` §8 + chapter-
4.1.A close-arc (2026-05-10): slices 6, 7, 8 were gated on chapter 3.2
SnapshotRowsets landing the IR widening triggers. SnapshotRowsets
shipped at chapter 3.2 close. Auditing what's actually shippable:

**Slice 6 — Cross-module FKs (SHIPPED).** Verification slice; the
SsdtDdlEmitter already orders kinds across module boundaries via
`TopologicalOrderPass.runWith SkipSelfEdges`. Three new tests in
`SsdtDdlEmitterTests.fs` assert: (1) cross-module FK target's CREATE
TABLE precedes its source in the statement stream; (2) `REFERENCES
[dbo].[<target_table>]` clause resolves the target's physical name
correctly via `Catalog.tryFindKind`; (3) T11 keyset spans modules
(every kind keyed; cross-module FKs don't perturb the keyset).

**Slice 7-identity (ALREADY SHIPPED).** `Attribute.IsIdentity : bool`
was added in chapter 3.1 / 3.2 (V2 IR carries it; SnapshotRowsets
populates from `sys.columns.is_identity`); `ScriptDomBuild.build
CreateTable` emits `IDENTITY(1, 1)` when the flag is true (lines
158-162). No additional work needed at this disposition.

**Slice 7-default (DEFERRED).** Adding `Attribute.Default : string
option` + DEFAULT-constraint emission requires updating 107+ Attribute
literal-construction sites with `Default = None` under the record-
extension empirical-test discipline. No consumer demands the field
today: the SnapshotRowsets adapter does not currently surface
`sys.default_constraints` (the rowset variant would need a sibling
rowset query), and `Tolerance.IgnoreDefaultNames = false` documents
the comparator's current acceptance posture. **Trigger to cash out**:
the SnapshotRowsets adapter surfaces default-constraint columns
(adds a rowset variant joining `sys.columns.default_object_id` to
`sys.default_constraints.definition`).

**Slice 8 (DEFERRED).** Adding `Kind.Description + Attribute.Description
: string option` + extended-properties emission requires 107+ Attribute
+ N Kind literal-construction sites updated with `Description = None`.
No consumer demands the field today: SnapshotRowsets does not surface
`sys.extended_properties` (the rowset variant would need a sibling
query), and `Tolerance.IgnoreExtendedProperties = true` documents the
comparator's current acceptance posture. **Trigger to cash out**: the
SnapshotRowsets adapter surfaces description columns.

**Decision.** Slice 6 ships; slices 7-default and 8 stay deferred at
the chapter 4.1.A close-arc disposition. Both new Active deferrals
entries codify the rowset-adapter-surfaces-the-evidence trigger. Per
IR-grows-under-evidence: the IR widens when the adapter surfaces real
evidence, not speculatively.

**Reasoning / consequences.** The 107+ Attribute construction-site
mechanical edits (per `Reference.IsUserFk` precedent at chapter 4.2
slice ζ where the cost-benefit favored shipping) don't pay off here
because no consumer demands the fields. The Tolerance taxonomy
(`IgnoreDefaultNames`, `IgnoreExtendedProperties`) absorbs the
divergence today; the canary doesn't flag missing defaults or
descriptions. When SnapshotRowsets gains the surfaces, both slices
cash out cleanly: the IR widening lands first, the emitter wires in
behind, the Tolerance flags flip from `true` to `false`.

---

## 2026-05-11 — Chapter 4.3 open: three-channel Diagnostics deferral retired (refuse the split)

**Status:** decided

**Context:** Per `CHAPTER_4_3_OPEN.md` §"Retiring the three-channel
Diagnostics split deferral" + pre-scope §1.4. The chapter-2 "three-
channel Diagnostics split" Active deferral has sat in the index
since 2026-05-06 ("operator / auditor / developer" channels;
trigger: "real downstream consumer demands per-channel routing").
Chapter 4.3 is the natural site to re-examine — the operator-
facing artifacts (`decision-log.json` / `opportunities.json` /
`validations.json`) are the first V2 surfaces humans consult.

**Decision:** **Refuse the split.** The three V1 artifacts ARE the
three channels — descriptive of *what is being emitted*, not of
*who consumes it*:

- `decision-log.json` → audit channel (every decision the system made)
- `opportunities.json` → operator channel (actionable suggestions)
- `validations.json` → developer channel (pass-witnessed invariants)

Adopting this framing means **the existing `Diagnostics<'a>` writer
remains single-channel**; routing happens at emit time via the
`Code`-prefix table:

```
tightening.*.opportunity.*       → opportunities.json
tightening.*.validation.*        → validations.json
tightening.*  (everything else)  → decision-log.json
adapter.*    (boundary errors)   → decision-log.json
emitter.*    (Π-time errors)     → decision-log.json
```

No `DiagnosticChannel` DU. No parallel writer. Three artifacts
route from one stream via a pure function of `DiagnosticEntry`.

**Reasoning / consequences.** Pillar 8 (domain-first naming): the
three-artifact split is operator vocabulary; ubiquitous-language
consistency means V2 inherits V1's names. The Active deferral
index moves the entry to **retired** status; the chapter-2 framing
("the split is a structural extension of the writer") is the
abandoned-alternative, codified as a documented false-start per
the discipline ("Document the false starts" — `DECISIONS 2026-05-13
— Pass return-type codification (session 14)`).

**Future trigger** (if a fourth axis surfaces): a streaming
operator surface (e.g., real-time dashboard) demanding only
`Error`-severity entries would prompt re-examination. Today's
three artifacts saturate the operator/auditor/developer cut.

---

## 2026-05-11 — Chapter 4.3 close + slices δ + ε deferred-with-trigger

**Status:** decided

**Context:** Chapter 4.3 (Operational Diagnostics V2; V2-driver KPI
Phase 5) closes structurally. Slice arc α + β + γ shipped on branch
`claude/chapter-4-ddd-improvements-XVCAM` (commits `bf3770b` →
`abe0040`). Three operator-facing JSON artifacts route from one
stream via the Code-prefix table; partition property green.
Test baseline 1012 non-canary passing; lint clean.

Per pre-scope §1.5: slices δ (CLI wire-up in `Projection.Pipeline`)
and ε (V1 differential test) are sequenced after the structural
commitment ships. Per the V2-driver KPI per-axis correctness
stakes table, operational-diagnostics is "Lower" stakes; the
structural three-emitter projection IS the V2-driver commitment.

**Decision A — Slice δ (CLI wire-up) deferred-with-trigger.**
Per pre-scope §1.5 slice 5: the canary's CLI verb invokes the
three emitters and writes the three files alongside the SSDT/
DACPAC artifacts (C# in `Projection.Pipeline/OperationalDiagnostics
.cs` per `DECISIONS 2026-05-15`). **Trigger to cash out**: a real
cutover-day operator workflow that consumes the three artifacts
(e.g., a CI pipeline step that publishes them as build artifacts;
a jq-based dashboard). The structural commitment ships at chapter
4.3 close; the wire-up is operator-UX integration not algebraic
content.

**Decision B — Slice ε (V1 differential test) deferred-with-trigger.**
Per pre-scope §1.5 slice 6: the V1 envelope walk runs both V1
(existing trunk) and V2 (new emitters) against the same fixture
Catalog and diffs the three artifacts. **Trigger to cash out**:
V1's `OpportunityLogWriter` + `PolicyDecisionLogWriter` +
`ValidationReport` writers stabilize as the canonical V1 reference
shape. Today the divergences are documented in
`CHAPTER_4_3_CLOSE.md` §V1-input-envelope walk:
- V2 ships **one** `decision-log.json` (V1 ships two: `policy-
  decisions.json` + `policy-decision-report.json`).
- V2 collapses V1's "EnforceUnique + RequiresRemediation" cases
  per `UniqueIndexPass.fs:96-113`.
- V2 sorts findings by SsKey root (V1 sorts by Schema/Table/
  ConstraintName/Type/Title via `ValidationFindingComparer.
  Instance`).

**Reasoning / consequences.** Both deferrals are at the operator-
UX integration / V1-fixture-stabilization layer; neither gates
the V2-driver KPI structural commitment that chapter 4.3 ships.
The chapter close discharges the structural arc; the trigger
conditions for both slices are non-algebraic (operator-workflow
demand + V1 reference-shape stabilization).

**Chapter 4.4 RemediationEmitter sequencing.** Per V2_DRIVER.md
Phase 6 + chapter 4.4 pre-scope: RemediationEmitter is sequenced
AFTER chapter 3.x DacpacEmitter (composes over DacpacEmitter's
typed-DACPAC output). DacpacEmitter is conditional on the deploy
path; chapter 4.4 inherits the conditionality. **Codified at
this close**: chapter 4.4 stays not-started until chapter 3.x
DacpacEmitter ships.

---

## 2026-05-11 — Chapter 3.x DacpacEmitter open: dev-tooling reframe + F# wrapper + content-equality T1 for binary emitters

**Status:** decided
**Context:** Chapter 3.x DacpacEmitter was pre-scoped at session 25
(`CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md`) as a **deploy-path-conditional**
V2-driver KPI critical-path emitter — the second production-write Π
covering the DACPAC + SqlPackage deploy lane parallel to
`SsdtDdlEmitter`'s SSDT-style file deploy. At chapter 4.3 close
(2026-05-11) the operator reframed the chapter's scope:

> "I think the DacpacEmitter is okay to decline in favor of SSDT in
> terms of deploy path, however I would like to go for it. Let's
> bring it on in terms of having an emission target so that I can
> stand up a local copy of the database in no time flat — almost a
> one-click deploy strategy for my development team to be able to
> query the database locally or develop on it locally. This would
> not be used for the production path until we identify a reason
> why it should."

Production deploy path stays SSDT-style (the V1-compatible file
bundle the operator's Azure DevOps pipeline already consumes).
DacpacEmitter ships as a **dev-tooling sibling-Π emitter** — the
artifact format that `sqlpackage.exe`, Visual Studio's "Publish DAC
Package," and `DacServices.Deploy` already speak, so the dev team
can one-click stand up a local copy of the projected schema for
local query / development.

**Decision (three coupled commitments):**

1. **DacpacEmitter is scoped as dev-tooling, NOT on the production
   deploy path.** Production deploy stays via `SsdtDdlEmitter
   .emitSlices` directory bundle; DacpacEmitter is the local-stand-
   up artifact format. R6 split-brain governance is unaffected —
   V2 still emits-but-doesn't-ship-to-production during dual-track;
   the DACPAC artifact is dev-environment-only consumption. The
   production-deploy scope can re-open if a future operator decision
   names a reason (the operator's framing: "until we identify a
   reason why it should").
2. **Pure F# wrapper inside `Projection.Targets.SSDT`; no C#
   subproject.** Per `DECISIONS 2026-05-09 — Adapter language
   choice`, C# was reserved for foreign APIs whose surface was
   "unfriendly from F#." The pre-scope's session-25 bias toward a
   new C# project (`Projection.Targets.SSDT.Dacpac`) yields under
   empirical pressure: (a) `Projection.Pipeline` ended up F# (not
   C# as the pre-scope assumed); (b) DacFx's V2-relevant surface
   is small — `new TSqlModel(SqlServerVersion.Sql160) |> use`,
   `model.AddObjects(scriptText)`, `use stream = ...`,
   `DacPackageExtensions.BuildPackage(stream, model, metadata)` —
   four `IDisposable`-aware calls F# handles natively via `use`;
   (c) ScriptDom is already used directly from F# at
   `ScriptDomBuild.fs` — the precedent for OO-shaped Microsoft SQL
   libraries inside F# is established. The 2026-05-09 decision's
   conditional clause ("DacFx — *if* its API turns out to be
   unfriendly from F#") fell on the F#-friendly side empirically.
   `Microsoft.SqlServer.DacFx` v162.x PackageReference lives on
   `Projection.Targets.SSDT.fsproj`.
3. **T1 for binary emitters: content-equality via DacFx round-trip
   (not byte-equality).** DacFx's `BuildPackage` embeds wall-clock
   timestamps in `Origin.xml` and zip-entry headers; two emit calls
   on the same Catalog produce non-byte-identical streams. Per the
   pre-scope §6.1 option (b): T1 for `DacpacEmitter.emit` is
   **`Catalog → emit → DacPackage.Load → TSqlModel.GetObjects →
   re-derive table count` equals source kind count** — content
   identity at the DacFx model level rather than byte identity at
   the stream level. Post-hoc zip canonicalization (option a) stays
   deferred-with-trigger: re-open if a snapshot consumer demands
   byte-stable dacpac artifacts. The dev-tooling consumer
   (one-click stand-up) does not need byte stability; content
   identity is sufficient.

**Reasoning / consequences:**

The dev-tooling reframe preserves the Tier-3 hard-required
`text-builder-as-first-instinct` deferral (DacFx adoption is
non-negotiable; hand-rolling .dacpac via `System.IO.Packaging`
remains forbidden) while loosening the scope's coupling to the
production-deploy promotion ladder. The chapter ships independently
of the cutover team's production deploy-path choice and does not
gate on R6 promotion criteria.

Pure-F# wrapper consequences: one new NuGet PackageReference
(`Microsoft.SqlServer.DacFx` v162.x) on `Projection.Targets.SSDT`,
matched against the `Microsoft.SqlServer.TransactSql.ScriptDom`
v170.x already pinned. Cross-target dependency weight increases by
the DacFx assembly set (~10 MB; pre-scope §6.1's documented Origin
.xml machinery sits in `Microsoft.SqlServer.Dac.Extensions.dll`).
Re-open the C#-subproject decision at chapter close if dependency
weight + project-cohesion arguments justify the subproject lift.

Content-equality T1 consequences: T1's "same input ⇒ byte-identical
output" formal statement is unchanged for text emitters
(`SsdtDdlEmitter` / `RawTextEmitter` / `JsonEmitter`); the **binary
emitter amendment** says T1 for `DacpacEmitter` reads as "same
input ⇒ content-identical DacFx model under round-trip" — the
algebraic claim holds at the model level, not the stream level. The
AXIOMS amendment scaffolding (per `DECISIONS 2026-05-22 — AXIOMS
amendments scaffolded at chapter open`) records this at chapter
close when slice α's worked example confirms the discipline.

**Chapter 4.4 RemediationEmitter sequencing is preserved** —
chapter 4.4 still sequenced after chapter 3.x DacpacEmitter (the
RemediationEmitter composes over DacpacEmitter's typed model
output). The dev-tooling reframe does not change the sequencing;
chapter 4.4 inherits the dev-tooling framing rather than the
production framing (operator-side remediation for dev-environment
partial-state recovery).

**Slice arc** at chapter open: α (single-Kind round-trip), β
(multi-Kind + FK), γ (indexes), δ (CLI `dac deploy` verb), ε
(modality marks → comments / extended properties), ζ
(byte-determinism cash-out — deferred-with-trigger; no snapshot
consumer today).

---

## 2026-05-11 — Chapter 3.x slice δ_dock: DockerImageEmitter replaces CLI `dac deploy` per operator directive

**Status:** decided
**Context:** Pre-scope §5 sequenced slice δ as a Pipeline + CLI
wire-up: `Projection.Cli dac deploy <jsonPath> <connStr>` would
build the dacpac in-process and invoke `DacServices.Deploy` against
a caller-supplied SQL Server connection. After slice α shipped the
operator named the next axis explicitly:

> "Even better is if we don't have a CLI command but can somehow
> create a custom Docker package that stands itself up with the
> loaded SQL server inside of it? That way it's a single command
> up and my team doesn't have to have the repository to pull the
> data fresh each time."

The operator's two coupled requirements:
1. **Single-command stand-up.** The dev consumer's runtime path is
   `docker run <image>` — no CLI invocation, no source checkout,
   no `DacServices.Deploy` orchestration on the dev's machine. The
   image IS the deployment.
2. **No repository dependency for the dev consumer.** Whoever runs
   the image needs only Docker + a registry reference; pulling a
   fresh schema does not require cloning, building, or running the
   sidecar's CLI.

**Decision:** Slice δ_dock ships **`Projection.Targets.SSDT.DockerImageEmitter`**
as a sibling-Π emitter producing a typed `DockerImageContext`
record (Dockerfile + DacpacBytes + EntrypointScript + Readme).
The emitter's signature is `Catalog -> Result<DockerImageContext>`;
the four fields are pure constants for slice δ_dock (per-Catalog
parameterization deferred per IR-grows-under-evidence). The CLI
verb originally scoped for slice δ is **retired without replacement**
— the deployment surface is the Docker image, not a Pipeline
helper. CI/CD pipelines consume the build context (`docker build .`)
and publish the resulting image to a registry; dev teams pull
from the registry.

**The two pillar-7-canonical libraries this slice adopts:**

1. **`mcr.microsoft.com/mssql/server:2022-latest`** as the base
   image. Microsoft's canonical SQL Server-on-Linux image; same
   pin as `Projection.Pipeline.Deploy.DefaultImage` so the dev
   container shares the canary's exact-match-production surface
   area (per `DECISIONS 2026-05-15`).
2. **`sqlpackage`** (downloaded from `https://aka.ms/sqlpackage-linux`
   at image build time) as the DACPAC deploy tool. Microsoft's
   canonical SSDT-side DACPAC publisher; mirrors what Visual
   Studio's "Publish DAC Package" uses under the hood.

The entrypoint script's deploy path is `sqlpackage /Action:Publish
/SourceFile:catalog.dacpac /TargetServerName:localhost ...` —
identical surface to what a dev would invoke locally if running
sqlpackage by hand. Idempotency is sqlpackage's contract: on
container restart against a persisted volume, sqlpackage validates
the existing schema matches rather than re-creating.

**Reasoning / consequences:**

The Docker image is operationally a **distribution channel** the
CLI verb is not. A CLI verb requires the dev to have the sidecar
repository, .NET SDK, and a running SQL Server to point at; the
Docker image bundles all three (SQL Server inside the image, dacpac
embedded, deploy tool installed at build time). The bytes that
travel to the dev's machine are exactly one artifact — the image.

The image is a **build-time-vs-runtime split**: CI/CD owns image
build (downloads `sqlpackage`, bakes the dacpac, pushes to
registry — paid once per schema-evolution event); the dev's
container start paid the much-cheaper "publish dacpac to fresh
SQL Server instance" cost (~5–30 seconds). The split fits the
cutover team's existing release pipeline: the same schema
artifact that CI builds and publishes for production also
builds + publishes the dev image; one source-of-truth.

**Pillar 7 + pillar 8 hold structurally.** No string composition in
the build context (Dockerfile / entrypoint / README are pure
constants for slice δ_dock; lint clean with zero new LINT-ALLOWs).
The name `DockerImageEmitter` is concept-shaped (names what it
emits — the build context for a Docker image); `DockerImageContext`
names the bundled artifact (the context for `docker build`).

**Per-Catalog parameterization deferred-with-trigger** — the
slice ships pinned constants for `PROJECTION_DB_NAME` (default
`ProjectionCatalog`) and `BaseImage` (`mcr.microsoft.com/mssql/
server:2022-latest`). Per-Catalog overrides (multi-database
images; alternative SQL Server versions; custom env-var name
schemes) surface when an operator workflow demands them. The
empirical condition: a second consumer with conflicting defaults.

**Chapter 4.4 RemediationEmitter sequencing preserved** — chapter
4.4 still sequenced after chapter 3.x; the dev-tooling Docker
context becomes the natural delivery vehicle for chapter 4.4's
remediation scripts (operator deploys a fresh Docker image
incorporating remediation, vs. mutating an existing dev
database via remediation scripts).

---

## 2026-05-11 — Chapter 3.x close: T1 binary-emitter amendment cashed + three slices deferred-with-trigger

**Status:** decided
**Context:** Chapter 3.x (DacpacEmitter dev-tooling) closes per the
eight-item chapter-close ritual (`CHAPTER_3_X_CLOSE.md`). Slice arc
α + β + γ + δ_dock shipped end-to-end (`090f2d7` + `5985b40`); the
Tier-3 `text-builder-as-first-instinct` hard-required deferral
cashed out at slice α; the dev-tooling reframe is structurally
green (Catalog → DacFx → `.dacpac` → Docker image → registry →
`docker pull` + `docker run`).

**Decision (four coupled commitments at chapter close):**

1. **AXIOMS T1 binary-emitter amendment is cashed.** The 2026-05-22
   "Scheduled chapter 3.3 close" placeholder at `AXIOMS.md:689`
   now carries the worked body: text emitters preserve byte-
   equality; binary emitters (`DacpacEmitter` today; future
   `RemediationEmitter` per V2_DRIVER §147) preserve **content-
   equality via DacFx model round-trip** because DacFx embeds
   wall-clock timestamps in `Origin.xml` + zip-entry headers. The
   unifying predicate `t1ByteEqualOrModelEquivalent` chooses per
   emitter kind. Slice α's `T1 (binary): DacpacEmitter.emit is
   content-deterministic under DacFx round-trip` is the worked
   example test.
2. **Slice ε (modality marks → comments / extended properties)
   deferred-with-trigger.** Per pre-scope §2: modality marks
   (`TenantScoped`, `SoftDeletable`, `Static populations`) are
   informational at Π time; surfacing them as `EXTENDED PROPERTY`
   or as inline DDL comments has no current consumer (dev-tooling
   `docker run` + SSMS connect doesn't read modality metadata).
   **Trigger**: a downstream consumer (cutover audit, remediation
   flow, dev-tooling sub-feature) demands structured access to
   modality marks from the .dacpac model.
3. **Slice ζ (byte-determinism cash-out via post-hoc Origin.xml
   canonicalization) deferred-with-trigger.** Per chapter-open
   strategic frame: rewrite `Origin.xml` timestamps; recompute
   model.xml checksum; re-pack with pinned zip-entry timestamps.
   Content-equality T1 (commitment 1) is sufficient for dev-
   tooling. **Trigger**: a snapshot consumer demands byte-stable
   `.dacpac` artifacts (e.g., a content-addressable artifact
   store keyed on dacpac SHA256; a CI cache keyed on dacpac hash).
4. **Per-Catalog Dockerfile / entrypoint parameterization
   deferred-with-trigger.** Slice δ_dock ships pinned constants
   for `PROJECTION_DB_NAME` (`ProjectionCatalog`) and `BaseImage`
   (`mcr.microsoft.com/mssql/server:2022-latest`). Per-Catalog
   overrides (multi-database images; alternative SQL Server
   versions; custom env-var name schemes) stay deferred-with-
   trigger per IR-grows-under-evidence. **Trigger**: a second
   consumer with conflicting defaults (e.g., a dev team needing
   an alternative SQL Server version; a per-environment override
   pattern).

**Reasoning / consequences:**

The binary-emitter T1 amendment is the highest-leverage cash-out
at chapter 3.x close. It names the structural reason byte-equality
fails for `DacpacEmitter` (DacFx's `BuildPackage` is non-
deterministic by design) AND the algebraic form that holds in
exchange (model-content-equality under round-trip). Future binary
emitters inherit the predicate; the canary's tier-1 properties
choose the right form per emitter kind without re-deriving the
amendment at each new binary emitter's slice α.

The three deferred-with-trigger slices ε / ζ / parameterization
share a common shape: **no current consumer, no urgency**. Per
IR-grows-under-evidence, none earn their place until a consumer
demands them. The dev-tooling reframe means the dev team's
`docker pull` + `docker run` loop IS the primary feedback channel
— per-Catalog parameterization will surface naturally when a
second team adopts the image with conflicting defaults.

**V2_DRIVER.md Phase 6 status flips** from "not-started
(conditional)" to **substantively shipped (under dev-tooling
reframe)**. The original Phase 6 framing (DACPAC + SqlPackage as
production deploy path) defers indefinitely per the dev-tooling
reframe; the production-deploy condition would need to re-fire to
reopen the production-scope. Chapter 3.x's dev-tooling output is
operationally green — no R6 promotion ladder applies (R6 governs
production-write paths; dev-tooling lives outside the ladder).

**Chapter 4.4 RemediationEmitter framing inherits the reframe**:
when it ships (if it ships), it ships under dev-tooling framing
(operator-side partial-state recovery for dev environments) per
V2_DRIVER §147 free-corollary table. The Docker image's "regenerate
fresh dacpac + restart container" loop already provides one
remediation pattern; RemediationEmitter earns its place when an
operator workflow demands programmatic partial-state recovery
distinct from the regenerate-and-redeploy pattern.

**Chapter 5 (Phase 8 pragmatic close) opens next.** Consumer-
pressure-driven items per V2_DRIVER §252: F# Analyzers SDK custom
analyzer (slice ν from chapter 3.7); Coordinates Stage 2 typed
VOs (`SchemaName` / `TableName` / `ColumnName`; slice θ from
chapter 3.7); Hex port lifts (`IArtifactSink` / `IDeployHost`)
under genuine consumer demand; cutover-day operator runbook
(joint with solution architect); V1 sunset planning.

---

## 2026-05-11 — Chapter 5 open + slices ν + θ: FSharp.Analyzers.SDK infrastructure + Coordinates Stage 2 VOs (partial cash-out)

**Status:** decided
**Context:** Per chapter 3.x close (above) + `V2_DRIVER.md` §252:
the V2-driver KPI critical path closed at chapter 4.3 + chapter
3.x; remaining work is **Phase 8 pragmatic close** — consumer-
pressure-driven hygiene + governance. Chapter 5 opens as the
formal chapter name for that queue. Slices land as separate
commits; the chapter open accumulates a slice list; no single-
chapter close fires until the queue empties or stabilizes per
V1-sunset milestones.

**Two slices ship at chapter open:**

### Slice ν — F# Analyzers SDK custom analyzer

`Projection.Analyzers` F# class library ships under
`src/Projection.Analyzers/`. Targets net8.0 (the SDK's TFM);
consuming projects' net9.0 targeting is independent (analyzer
assemblies are loaded by the `fsharp-analyzers` runner, not
linked into consumers). Pinned to `FSharp.Analyzers.SDK` 0.30.0
— last release whose FSharp.Core dependency (9.0.201) is
compatible with the .NET 9 SDK 9.0.305 we run; 0.36.0+ requires
FSharp.Core 10.0.101 (.NET 10).

One analyzer ships: **`Projection001NoUnsafeTimeInCore`** —
detects `System.DateTime.Now` / `DateTime.UtcNow` /
`DateTime.Today` / `Guid.NewGuid` / `Random.Shared` calls inside
files under `src/Projection.Core/`. Walks the untyped AST
(`SynExpr.LongIdent` references with matching long-id suffix
pairs); cross-platform path discrimination on the file name's
`/Projection.Core/` segment. Surfaces violations at error
severity per the CLAUDE.md load-bearing commitment "F#-pure-
core / no-I/O-in-Core" + the operating-disciplines table entry
"Determinism is constructed, not validated."

Tool integration: `.config/dotnet-tools.json` registers
`fsharp-analyzers` 0.30.0; `scripts/run-analyzers.sh` is the
opt-in runner (build → restore tool → invoke against
Projection.Core with `--analyzers-path` pointing at the built
DLL). **CI integration deferred** — the runner is invoked
manually for now; CI wire-up earns its place when the analyzer
set grows beyond one rule.

End-to-end verified: runner picks up the analyzer, walks all
28 files in `Projection.Core`, reports zero violations (Core is
clean of the forbidden primitives by discipline). The
infrastructure is proven; the analyzer set is intentionally
narrow.

### Slice θ — Coordinates Stage 2 typed VOs (smart constructors only)

Per `Coordinates.fs:19-23` Stage 1 docstring's "Stage 2
(deferred)" placeholder + chapter 5 open strategic frame:
typed `SchemaName` / `TableName` / `ColumnName` value objects
land as single-case DUs wrapping validated strings.

Smart constructor invariants:
- Reject null / empty / whitespace (codes:
  `schemaName.empty` / `tableName.empty` / `columnName.empty`).
- Reject identifiers longer than 128 characters (SQL Server
  identifier limit per
  `https://learn.microsoft.com/en-us/sql/relational-databases/
  databases/database-identifiers`; codes:
  `schemaName.tooLong` / `tableName.tooLong` /
  `columnName.tooLong`).
- Accept any otherwise-valid identifier string. Bracket-quoted
  identifiers may carry SQL-reserved characters; that's a
  render-time concern (ScriptDom's `Identifier.EncodeIdentifier`
  handles it), not a construction concern.

The three VOs are **structurally distinct types** — the compiler
refuses to confuse a `SchemaName` with a `TableName` (or with
a raw `string`). Per the codebase's "Identity is a type, not a
string" principle.

**The `PhysicalRealization.Schema/Table` and `Column.ColumnName`
record-field migration stays deferred-with-trigger.** Stage 1's
trigger condition is preserved: "Triggers when a real bug would
have been caught (or when the cost of the explicit `value`
projections is exceeded by the safety win at the next adapter)."
The typed surface is **opt-in for new code**; existing `string`-
field readers keep compiling. **Trigger for the full migration**:
a real bug caught (schema-vs-table confusion at a boundary) OR
adapter-ripple cost dominated by safety win at the next adapter.

**Reasoning / consequences:**

Both slices share the **infrastructure-now / migration-later**
pattern. The analyzer infrastructure ships (one analyzer + the
runner + tool manifest); the broader analyzer suite earns its
place when false-negatives surface on the grep rules. The
Coordinates Stage 2 types ship (three smart constructors +
distinctness witnesses); the record-field migration earns its
place when a real bug forces the boundary.

This is honest to the consumer-pressure-driven Phase 8 framing.
Chapter 5 is **not** a "ship the entire pragmatic-close queue"
chapter — it's the open-ended queue itself, with slices landing
as the queue's items earn their place under consumer pressure.

**Test baseline:** 1060 → 1072 non-canary tests (+12 across
slices ν + θ; the analyzer infrastructure's runtime verification
is the end-to-end runner against `Projection.Core` reporting
zero false positives; the typed-VO smart constructors carry 12
direct tests covering accept / reject / boundary cases). Lint
clean across 27 grep rules (slices ν + θ introduce zero new
LINT-ALLOWs — the typed-VO smart constructors use BCL primitives
at the validation boundary; the analyzer assembly is outside the
27-rule scope by analyzer-not-application convention).

---

## 2026-05-12 — Verifiability-triangle audit methodology

**Status:** decided
**Context:** V2's structural-commitment posture had drifted to a place
where the algebra's interior (identity, lineage, sibling-Π
commutativity, aggregate-root invariants) was full-stack covered (L3
product behavior ↔ L2 axiom ↔ L1 structural commitment ↔ test) but the
boundary (V1→V2 parsing, V2→disk writing, V2→operator diagnostics,
config surface) had silent dependencies — L3 properties the operator
implicitly trusts that no L2 axiom names and no L1 commitment
structurally guarantees. The chapter-3.1 close-audit (`AUDIT_2026_05_
DDD_HEXAGONAL_FP.md`) had established the multi-agent epistemic-tier
audit protocol for a single dimension (DDD / Hexagonal / FP); the
question was how to extend that protocol to systematically surface
structural gaps across *all* axes of operator-facing behavior, not
just one.

**Decision:** the **L1↔L2↔L3 verifiability triangle** becomes V2's
audit lens, with a documented cadence:

1. **Three connected levels.**
   - **L1**: structural commitments — smart constructors, closed DUs,
     value objects, type-system contracts. Enforced by construction.
   - **L2**: formal axioms (`AXIOMS.md`) — A1–A40 + T1–T11 + amended
     originals. The algebra's claims about itself.
   - **L3**: product axioms (`PRODUCT_AXIOMS.md`, new in this
     decision) — operator-meaningful claims V2 must verifiably
     guarantee end-to-end. The promise the operator implicitly trusts.

2. **Bidirectional tracing.** Every L3 axiom must trace down to L2
   underwriting and L1 commitment; every L2 axiom must trace up to a
   product behavior; every L1 commitment must trace up to an axiom.
   Failures of the trace are the audit's primary findings.

3. **Bucket classification.** Each axiom (L2 or L3) is classified
   into one of four buckets:
   - **Bucket A**: full coverage (L3 ✓ L2 ✓ L1 ✓ test). The model.
   - **Bucket B**: L3 ✓ L2 ✓ test ✓, L1 by convention. One refactor
     away from regression.
   - **Bucket C**: weakness (untested, hidden, aspirational, deferred,
     subsumed, scope boundary, partial structural).
   - **Bucket D**: unnamed L3 axiom with no L2 backing — silent
     operator dependency.

4. **Audit dispatch protocol.** Three parallel agents in a single
   dispatch:
   - Agent A: top-down — articulate L3 product axioms from strategic
     docs, grouped by core concern (schema/data/identity/diagnostics/
     cutover-safety + cross-cutting).
   - Agent B: middle-out — bridge L2 ↔ L3 for every existing axiom in
     `AXIOMS.md`; assign bucket classification.
   - Agent C: adversarial gap-hunt — act as the operator pre-cutover;
     list questions whose answer is a candidate L3 axiom.
   Plus optionally a parallel Round 1 bottom-up scan (L1 inventory +
   illegal-states audit across multiple surfaces) when the audit
   includes a structural-commitment refresh.

5. **Cadence.** Three triggers operationalize the discipline:
   - **Annual re-audit refresh** — full multi-agent dispatch every
     ~12 months to refresh the coverage map.
   - **Chapter-close L3 step** — every chapter close adds a
     one-paragraph audit check naming the L3 axioms its work touched
     and any new Bucket-D gaps introduced.
   - **Per-PR L3 review** — PRs that touch boundary code or add
     config/CLI surface get a checklist: which L3 axioms does this
     touch? Are they Bucket A or below? Does this PR strengthen or
     weaken the structural commitment?

6. **Promotion path.** When a campaign operationalizes a Bucket-D
   axiom, the promotion lands in `AXIOMS.md` (new L2 axiom) or
   `PRODUCT_AXIOMS.md` (existing L3 axiom moves from candidate to
   formal). The audit doc retains the campaign close-note in its
   append-only Part XII.

**Consequence:** the methodology codifies what the chapter-3.1 audit
established (multi-agent epistemic-tier) and extends it from
one-dimensional ad-hoc dispatches to a recurring, multi-level coverage
map. The first audit under this methodology (2026-05-12) produced
`AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md` (the integrator's view),
`PRODUCT_AXIOMS.md` (the L3 sibling to `AXIOMS.md`), and three
proposed campaigns superseding the prior 3-slice airtight plan.

**Companion artifacts:** `AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md`
(the audit's integrator view + campaigns); `PRODUCT_AXIOMS.md` (L3
canonical surface); `CLAUDE.md` operating-disciplines table updated
with a row pointing at this entry.

---
