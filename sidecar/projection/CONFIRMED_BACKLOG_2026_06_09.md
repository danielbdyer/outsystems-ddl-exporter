# CONFIRMED BACKLOG — V2 Projection Sidecar (2026-06-09)

**Purpose.** A single, code-verified ledger of what is *actually still open* in
`/sidecar/projection`, distilled from a full sweep of the documentation corpus
(~125 markdown files) and then **confirmed against the live `src/` + `tests/`
code**. It exists because the planning/audit/ontology docs are substantially
**stale**: the large majority of items they describe as open/missing/partial
have since shipped, often under different names than the docs use. Future
sessions should treat *this* file as the starting point for "what's left," and
treat the older docs as framing/provenance, not status.

---

## ⓪ Execution status — 2026-06-09 session (read this first)

A two-wave parallel-agent execution slice landed on branch
`claude/sidecar-projection-backlog-r1622k` (atop the backlog doc commit). All
changes behavior-preserving / additive; full pure pool **2943 passed / 0
failed**; each commit SSH-signed.

**SHIPPED (16 items):**
- **B5** `ChannelDiff<'change>` unifies the 4 channel-diff records (alias-contained) — `1a4ad1b`.
- **B2** `LineageDiagnostics.touchedEpilogue` combinator, 5 analytics passes — `c1dc7ad`. *(3 passes intentionally excluded: different return shape / conditional emission.)*
- **B7** `Deploy.fs` decomposed into `DockerDaemon.fs` / `DatabaseNameGenerator.fs` / `DeployConnectionString.fs` + facade (1531→1315 LOC) — `90d9bee`.
- **B1** `ArtifactByKind.perKind`/`perKindBenched`, 9 emitter sites — `ac594ba`. *(Estimate was 7; `StaticPopulationEmitter`/`ManifestEmitter` were not actually `ArtifactByKind` sites.)*
- **A3** delete-scope-ready data emitters (`DeleteScope option` threaded, default `None`, byte-identical) — `ac594ba`. *(Emitter side only; CLI exposure not yet wired.)*
- **B3** `ReadSide.drainRows` combinator replaces 13 reader-drain loops — `0be4005`.
- **D1** Watch done-frame + run-title header — `88e4dd0`.
- **D2** Ladder "one lever" honest blocking-item — `88e4dd0`.
- **B6** canonical `mkName` promoted to shared `Fixtures`, 24 local copies removed — `2a6f157`. *(`mustOk` ×64 / `mkKey` ×31 correctly LEFT — divergent error types / SsKey prefixes.)*
- **A6** top-level `diff <a> <b>` verb (aliases `explain diff`) — `a317c07`.
- **A2** `--resumable` flag → `Transfer.runResumable` (default off) — `a317c07`.
- **A7** `ModuleFilter` gets its first production caller (`ModuleFilterBinding.fromConfig` applied in `Pipeline.readConfigModel`) — `a317c07`.
- **D3** setup probe surfaces full grant set (INSERT/CREATE/DELETE/ALTER), not just `alterGranted` — `a317c07`.
- **D4** `--from empty` genesis-force (`MigrationRun.previewFromStoreForcing`) — `a317c07`.
- **A1** transfer refusal exit single-sourced through `Preflight.refusalOf` — `24c956b`. *Decision: exit 3 was the latent bug (`classify` already maps `cdcTrackedSink → 9`, the integrity-refusal class); routing the refusal arm through the seam makes it 9, all other codes byte-identical. The successful-with-drops branch untouched.*
- **A5** rename-aware migrate-with-data — `38be8f5`. *Decision: data source is at schema A; `executeWithData`/`executeWithDataAndRecord` now route the data leg through `Transfer.runWithRenames`/`runReconcilingWithRenames` (`sourceContract = A`, `sinkContract = B`, renameMap from `CatalogDiff.between A B`). Empty rename map ⇒ identity repoint ⇒ byte-identical straight load. One pre-existing `executeWithData` canary was re-staged from B→A to match the settled premise; MigrationCanary 20/20 green on the integrated tree.*

**RESOLVED-as-decision (operator, this session):**
- **B4** retry → **DEFERRED**. The "exception leak" premise was stale (already caught by `ReadSide.read`'s outer try/with); only the retry gap remains. Adding retry needs a structural home for the primitive (`Retry.fs` is in `Adapters.OssysSql`; `Adapters.Sql` refs only `Core`). Re-open under a real transient-failure incident.
- **A4** cross-schema FK diagnostic → **CLOSED on substance**. The `Unreadable` reason is named, classified, unit-tested (not silent); the stderr-vs-structured-channel detail isn't worth rippling `read`'s return shape into 4 consumers.

**RESOLVED (operator, 2026-06-10):**
- **A7 polarity:** the flags STAY opt-in (effective only alongside a
  non-empty `model.modules`); the estate-wide form was declined. The inert
  combination now carries a named note + `moduleFilter.flagsInert`
  diagnostic (no silent no-op). See `DECISIONS 2026-06-10 — A7 polarity
  RESOLVED`.

*(A1 and A5 — previously the two open semantic forks — were decided and shipped this session; see the SHIPPED list above.)*

---

## ⓪′ Execution status — 2026-06-10 session (the remaining-shelf sweep)

Six wires landed on branch `claude/fsharp-projection-review-vrbtpr`, closing
the residuals the 2026-06-09 slice left open. Pure pool **3009 passed / 0
failed** at every commit; Docker pool run before push.

**SHIPPED (6 items):**
- **A1 (residual)** `Program.migratePreflights` routed through the one
  mandatory `Preflight.all` (G0), mirroring the transfer Execute path; the
  permission gate sequences on the connection gate's hot task (one `cnn`,
  no concurrent commands); codes/precedence/exit-7 preserved — `3915c28`.
  **A1 is now fully CLOSED.**
- **D8** `--seed <n>` / `--scale <f>` per-run synthesis knobs (the design's
  §7 vocabulary; the doc's "--rows N" phrasing was loose) →
  `FlowRunOpts → MovementSpec → LoadOpts → SyntheticLoadRun`; malformed
  values are named refusals; non-synthetic actions note, never drop — `e1f5a09`. **CLOSED.**
- **Dacpac wire** `emission.dacpac` honored: `runWithConfigCore` compiles
  the package over the SAME emitted catalog the SSDT step projects;
  `projection.dacpac` staged in the atomic write. Default flipped to
  `false` (the flag was inert at `true`, so the default bundle stays
  byte-identical; explicit opt-in grows the artifact set) — `6a5be78`.
  The E4 "conditional emitter executed at its own site" now exists for dacpac.
- **J2 (follow-on)** per-flow `"reconcile": ["<table>:<col>"]` config field
  (parse-validated via `TransferSpec.parseReconcileSpec`, rendered by the
  declarative dual, threaded through `resolveFlowSpec`) — the golden flow's
  User-by-email re-key is declaratively expressible — `8eff5e3`. **CLOSED.**
- **A3 (CLI/config exposure)** `emission.deleteScope { terms: [{column,
  value}] }` → Core `DeleteScopePolicy` on the Emission axis → composer
  threads the plain value (A18 holds) → per-kind
  `DeleteScopePolicy.resolveFor` (the arm renders exactly when every term
  column is an attribute of the kind; a kind outside the scope keeps the
  upsert-only MERGE — the faithful rendering, not a skip) — `b66bee4`.
  **A3 is now fully CLOSED** (AC-D7 D7.1–D7.3 witnessed at the emitter).
- **Manifest wire** `shape: manifest` (the S6.1 skeleton precedent) →
  `PlanAction.EmitManifest` → `Compose.runManifestOnlyFromCatalogWith`:
  the shaped full chain runs; ONLY `manifest.json` lands, via a
  single-file write that leaves a previously published bundle standing — `bedbb40`.

**Still open after this sweep:** B8/B9 refactor tails (B8 since closed
same-day); §C modeling decisions; D5–D7 instrument slices; J5 (managed-environment
execute, OPEN-2); §E/§F speculative items. *(A7 polarity RESOLVED and the
J3 residual CLOSED later the same day — see §⓪ and the J3 row.)*

Remaining backlog after this slice: the un-shipped Cluster-B/§E/§F items below (e.g. `Emitter.perKind` is done; **B8** binding-resolution dedup, **B9** IRBuilders α′ tail, the speculative §E/§F, the §C modeling decisions) plus A1/A5 above.

---

**Method.** Two passes. Pass 1 fanned out across the doc corpus and compiled a
~185-item candidate superset. Pass 2 dispatched per-cluster confirmation agents
that read the actual code under an explicit anti-false-positive discipline
(treat "couldn't find it" as inconclusive; a dedicated `FooTests.fs` is strong
evidence `Foo` ships; distinguish "exists but not wired" from "absent"; cite
`file:line`). Verdict vocabulary below: **OPEN** (absent), **PARTIAL** (built,
not fully wired/adopted), **CLOSED-stale** (doc said open; code says shipped),
**DEFERRED** (absent by design, with a codified trigger), **OUT-OF-CORPUS**
(belongs to the V1 C# trunk at `/home/user/outsystems-ddl-exporter/src/`, not
this V2 subtree).

**Headline.** Of ~185 candidates, the confirmed real backlog is ~30 items,
dominated by (B) refactor/duplication debt and (A/D) small wiring + terminal-UX
gaps. Every audit "CRITICAL"/"HIGH" data-plane and lifecycle finding (silent
row-drop, no migrate-orchestrator, no pre-flight, transactional/resumable,
SsKey-rename, CDC, eject/drift/wipe-and-load, episode-record, CatalogDiff
widening, ReadSide un-hollow) verified **CLOSED**.

Line numbers below were re-read at HEAD `105d8ec`; doc-cited line numbers from
older revisions are unreliable and were discarded.

---

## A. Wiring gaps — capability built & tested, not reachable from a production path
*Highest value-per-effort: the hard work is done; only the last connection is missing.*

| # | Item | Verdict | Evidence |
|---|------|---------|----------|
| A1 | **`Preflight.all` / `allReporting` unwired** | **PARTIAL** (transfer Execute path wired, M4 G0a 2026-06-09) | `runCore` now composes its pre-plan CDC→spanning Execute gates through `Preflight.all` (`Preflight.fs:364`) — the first production caller; codes/precedence preserved (CDC canary + AC-I5/PE-1/classify green). **Remaining:** route `Program.migratePreflights:1382` through it too, and (optional) let the CLI consume `classify` directly rather than re-derive exits by hand. The post-plan structural gates (executeGate→validateUserMap) stay precedence-ordered (need the built plan) but refuse through the same `refusalOf` seam. |
| A2 | **Resumable transfer not CLI-exposed** | PARTIAL | `Transfer.runResumable` (`TransferRun.fs:656`) + `writePlanResumable` (`:283`) with durable phase-marker table `__projection_transfer_progress` is built and Docker-canary-witnessed (`MigrationCanaryTests.fs:630` "AC-G10 … resumable transfer recovers a partial prior attempt with no duplicate rows"). Gap: production `transfer` routes through `runThroughConnectionsWithEmission` (`Program.fs:751`) with `WriteOptions.Resumable=false`; no `--resumable` flag selects `runResumable`. (By design it is phase-tracked, not single-transaction — `Preflight.fs:344-356`.) |
| A3 | **Scoped delete arm not emitted (AC-D7/G4)** | PARTIAL | `WHEN NOT MATCHED BY SOURCE … THEN DELETE` + `DeleteScope` gate fully built and tested (`ScriptDomBuild.fs:694-922`, `WithDiagnosticsBuildersTests.fs:116-149`), but every production emitter hardcodes `DeleteScope = None` (`StaticSeedsEmitter.fs:188`, `MigrationDependenciesEmitter.fs:225`). Capability exists; no live emission path passes a scope. |
| A4 | **Cross-schema FK diagnostic via stderr, not structured channel (E2/G4)** | minor / PARTIAL | No longer the doc's "CRITICAL silent gap": `ReadSide.ForeignKeyReadback.classify` returns `Unreadable of reason` for a NULL `SCHEMA_NAME()` (dropped schema / missing VIEW DEFINITION grant), named & unit-tested (`ForeignKeyReadbackTests.fs`). Both readback paths invoke it (`ReadSide.fs:~648,~1495`). Residual: surfaced via `eprintfn` to **stderr** (`:661,:1508`), not threaded into the structured `Diagnostics`/`Result` channel. CLOSED on the no-silent-drop axiom; PARTIAL only if "named diagnostic" must mean a structured entry. |
| A5 | **Rename-aware migrate-with-data not wired** | PARTIAL | `TransferRun.runWithRenames` (`:782`) builds a `renameMap` from the A→B `CatalogDiff` and repoints rows (`RenameProjection.repointRows`), but is **not called** from the migrate-with-data CLI path (`runMigrateWithData`, `Program.fs:1544`, which wires `--reconcile`/`--user-map` only). Rename flow exists in the pipeline; the CLI combination is the gap. |
| A6 | **`diff <runA> <runB>` only as `explain diff`, not a top-level verb** | PARTIAL | Run-vs-run diff fully built: `runDiff` (`Program.fs:1034`) resolves both operands via `Ref` incl. `@runId` (`Ref.fs:53-62`), renders via `Comparison.renderCatalogChange`. Wired as `explain diff <a> <b>` (`MovementSurface.fs:419`), not a top-level `diff` verb (absent from `parse`, `MovementSurface.fs:596-619`). |
| A7 | **Entity/module filter not wired (`ModuleFilter`)** | PARTIAL | `ModuleFilter.apply` — module include-list + system/inactive filtering + **per-module entity filtering** (`EntityFilters : Map<string, ModuleEntityFilter>`, `ModuleEntityFilter.matches`/`missingNames`, structured `moduleFilter.entities.missing` error) — is fully built (`ModuleFilter.fs`, 437 LOC) and unit-tested (`ModuleFilterTests.fs`, incl. entity-filter cases). **Zero production callers**: only refs in `src/` outside its own file are comment citations in `MetadataContractOverrides.fs`. `Config.fs` carries `IncludeSystemModules` (`:63`) but never constructs/applies `ModuleFilterOptions`; no config field for module-selection or entity filters. Capability exists, unreachable from CLI/config. |

---

## B. Refactor / duplication debt — largest genuinely-open cluster
*Survived the 2026-06-04 audit cleanup. Counts are the confirmation agent's own re-count at HEAD, which sometimes exceeds the audit's figures.*

| # | Item | Verdict | Evidence (own count) |
|---|------|---------|----------------------|
| B1 | **`Emitter.perKind` combinator absent** | OPEN | No `perKind`/`ofKinds` in `ArtifactByKind.fs` (only `create`/`toMap`/`tryFind`/`keys`). The `allKinds |> List.map (k -> k.SsKey, render k) |> Map.ofList |> ArtifactByKind.create` shape is hand-rolled in **7** emitters: `JsonEmitter.fs:184`, `DistributionsEmitter.fs:215`, `RefactorLogEmitter.fs:333`, `SsdtDdlEmitter.fs:713`, `DecisionLogEmitter.fs:127`, `StaticPopulationEmitter.fs:129`, `ManifestEmitter.fs:378`. |
| B2 | **Analytics-pass epilogue hand-rolled** | OPEN | The `Touched`-event map + `lineageDiagnostics { writeLineages; writeDiagnostic; return }` tail repeats identically in **8** passes (`grep "TransformKind = Touched"` in `Passes/`): Centrality `:156`, SchemaComplexity `:148`, BoundedContext `:184`, ProfileAnomaly `:135`, QueryHint `:93`, CanonicalizeIdentity, NormalizeStaticPopulations, TopologicalOrderPass `:411`. No shared epilogue combinator. |
| B3 | **ReadSide materialize/drain loop** | OPEN | **17** identical `let! more = reader.ReadAsync()` drain loops in `ReadSide.fs` (e.g. `:254,306,345,382,416,444,475,517,554…`), each a hand-rolled `while hasMore` block. No `readAll`/`foldReader`/`drainReader`. (ReadSide-only; `LiveProfiler.fs` has 0 — it uses the EvidenceCache discovery path.) |
| B4 | **ReadSide/LiveProfiler retry gap + exception leak** | OPEN | `Retry.fs` (Polly) is consumed **only** by `MetadataSnapshotRunner.fs`; `ReadSide.fs`/`LiveProfiler.fs` have zero `Retry`/`Polly`/`ResiliencePipeline` refs. ReadSide's `readXxx` return raw `Task<list/Map>` (e.g. `:239,292,365`), not `Task<Result<_>>` → exceptions propagate unwrapped. Both the retry gap and the leak are real. |
| B5 | **`CatalogDiff` C1 channel records** | OPEN | 4 structurally-identical records in `CatalogDiff.fs` — `AttributeDiff` (`:60`), `ReferenceDiff` (`:104`), `IndexDiff` (`:135`), `SequenceDiff` (`:166`): each `{ Added; Removed; Renamed; Changed: XChange list }`, identical but for the `Changed` element type. Candidate for a `ChannelDiff<'facet>` (see also AR2). |
| B6 | **Test-fixture dedup** | OPEN | Central `Fixtures.fs` defines `mustOk` as `let private` (`:25`) — unshareable. Across `tests/`: **64** local `mustOk`, **31** `mkKey`, ~44 `mkName` redefinitions. Helpers exist centrally but private and unadopted. |
| B7 | **`Deploy.fs` decomposition (ACTIONABLES R2)** | OPEN | `Deploy.fs` is **1530 LOC** today (grew from the doc's 1470); only 3 small nested submodules (`Docker:76`, `DatabaseNameGenerator:278`, `ConnectionString:343`). The 5-module split never shipped. |
| B8 | **`*Binding` catalog resolution** | PARTIAL | `CatalogResolution.fs` holds only `tryKindByLogical` (2 consumers: `EmissionFoldersBinding:150`, `SpecialCircumstancesBinding:63`), each still wrapping it per-file. The physical-table lookup (`SpecialCircumstancesBinding.resolveKindByPhysicalTable:81`) and attribute-ref lookup (`TighteningBinding.resolveAttributeRef:36`) are not centralized. ~2 lookup shapes remain. |
| B9 | **IRBuilders Kind/Module/Catalog sweep tail (ch 4.8/4.9 slice α′)** | PARTIAL | 13 files / ~70 sites migrated at slice α; **19 files deferred** (slice α′) on an F# offside-rule tooling blocker. `IRBuilders.fs` now holds only `mkModule`+`mkCatalog`; ~30 test files still hand-build raw `Kind` record literals. Trigger codified at `CHAPTER_4_8_CLOSE.md:125` (indentation-preserving Python pass). Quantified tooling deferral, not a functional gap. |

**Verified NOT debt (false-symmetry traps / already done — do not re-open):**
`*Diagnostics`→`DiagnosticRule` registry merge (N3: FK/Joint diverge on ratio &
threshold direction — merging miscompiles one); `MigrationRun` "2^N execute*
explosion" (actually one `execute` core + decorators — `:498,526,556,579,627`);
dead-algebra deletion (`LineageTree`/`Prism`/`PassContext`/`Pass.product` all
gone — remaining grep hits are TLS/bitwise/comments); FSHARP-LOW point-free /
`Static.fs` bind-ladder (Policy `>>` replaced by `matchById` `Policy.fs:691`;
`Static.fs`→`NormalizeStaticPopulations.fs` is a clean exhaustive `match`);
`CatalogReader` God-file (decomposed to 168-LOC facade); `optInt` +
`CatalogResolution.tryKindByLogical` (extracted). The 8 periphery-adversarial
traps N1–N8 remain DO-NOT-ATTEMPT.

---

## C. Identity / data-fidelity residuals — all minor, none silent bugs

| # | Item | Verdict | Evidence |
|---|------|---------|----------|
| C1 | **IDENTITY seed/increment hardcoded `(1,1)`** | OPEN (by design) | `Attribute.IsIdentity : bool` carries only the boolean (`Catalog.fs:468`, docstring: seed/increment deferred — always emit `IDENTITY(1,1)`); emitter hardcodes (1,1) (`ScriptDomBuild.fs:371-379`). Matches OutSystems convention; accepted low-severity limitation. ("Composite IDENTITY one leg" is moot — IDENTITY is a single per-column bool.) |
| C2 | **Empty-string ↔ NULL conflation** | OPEN (named tolerance) | Real & live: `ReadSide.formatRawValue` maps `DBNull`→`""` (`:790`) and `SqlLiteral.ofRaw` maps `""`→`NullLit` (`SqlLiteral.fs:75`). Captured as `ToleratedDivergence.EmptyTextNormalizedToNull` (`Tolerance.fs:85`) with retirement trigger ("needs a read-side sentinel; no fixture forces faithful preservation"). Documented + witnessed, not silent. |
| C3 | **`TighteningIntervention.id : string`** | OPEN | Every variant carries a raw `id: string` (`Policy.fs:244,249,254,262`); accessor returns `string` (`:628`). Not lifted to a typed VO (the `SchemaName`/`TableName`/`ColumnName` VOs *are* now cashed across 15–20 files each / 136 unwrap sites — that half of the FSHARP claim is stale). |
| C4 | **`Reference` boolean-tuple collapse** | OPEN (deliberate deferral) | `Index.Uniqueness` collapsed to a 3-state DU (`Catalog.fs:789`) and `ApprovalState` collapsed; `Reference` still carries raw bools `IsUserFk`/`HasDbConstraint`/`IsConstraintTrusted` (`Catalog.fs:587,600,620`). FSHARP audit explicitly deferred Reference to "its own modeling slice." |

---

## D. Terminal-UX / instrument polish
*Voice "6 waves unwired" was a false alarm — the 2026-06-09 close doc matches the code (gates routed via `renderGate`→`classify`→`gateSurface`; no `%A`/shout-leads on live paths; catalog-diff/lifecycle/move surfaces all called). Open items are exactly the residuals that close doc honestly names, plus a few CLI affordances.*

| # | Item | Verdict | Evidence |
|---|------|---------|----------|
| D1 | **Watch board footer + done-frame** | OPEN | `Watch.toRenderable` (`Watch.fs:231`) renders stage `Rows` only — no run-title header, no "→ verification follows" / "recorded as run N" footer (grep: zero hits). |
| D2 | **Ladder "one lever" honest blocking-item display** (instrument slice 7) | OPEN | `buildReadinessView` (`TtyRenderer.fs:88`) shows "X green run(s) to go" but no single blocking-item source; no in-terminal ladder Surface with the lever. (`MatrixLadderTests` covers the `matrix-status.sh` generator, a different artifact.) |
| D3 | **Setup live-probe breadth** | OPEN | `buildSetupView` (`TtyRenderer.fs:139`) probes reachability + ALTER only; INSERT/CREATE-TABLE grants not surfaced (they exist in `GrantEvidence` if a consumer wants them). |
| D4 | **`--from empty` genesis-force flag** | OPEN | No `--from empty`. `--fresh` sets `Baseline.Empty` (`MovementSurface.fs:502`) but `planMovement` emits a no-silent-drop note that baseline is auto-derived, not honored (`:287`). Genesis in `MigrationRun.previewFromStore` is automatic only when the store is absent (`:182`); no flag forces genesis when a store exists. |
| D5 | **`inspect <runId>` TUI / Explore navigator** (instrument slice 6; Dynamic-display Explore leg) | OPEN | No `inspect` verb / `Intent.Inspect` (`MovementSurface.fs:590` `secondaryVerbs` lacks it). `DYNAMIC_DISPLAY.md §2` itself names Explore/`inspect` as the unbuilt TUI leg. (Glance + Watch live legs *are* shipped — `renderWatch` wired at 6 sites.) |
| D6 | **Instrument Timeline (slice 5)** | PARTIAL | No `Timeline`/`Lattice` node in the `View` DU (`View.fs:26-60`). §8 timeline-in-words exists (`View.Dots`+Note, `TtyRenderer.fs:95`) but not the dedicated Timeline/lattice slice. |
| D7 | **Instrument User-map walkable Surface (slice 9)** | PARTIAL | Re-key flow + validate-user-map gate are wired into migrate/transfer (`Program.fs:677-784,1544+`); no walkable `Surface` specialization for the user map. |
| D8 | **`--data synthetic --rows N` knob** | minor | Synthetic-data generation is wired (`parseFlowSource "synthetic"`→`SynthesizeAndLoad`→`SyntheticLoadRun.run`→`Transfer.runSynthetic`); the config-driven form is CLOSED. Missing only the `--rows N` knob (seed is a fixed literal, `SyntheticLoadRun.fs:24`). |

---

## E. HORIZON — genuinely not implemented (mostly speculative-by-design)
*HORIZON.md's own `PROPOSED`/`SHIPPED` markers are unreliable; verified against code. Confirmed CLOSED despite "PROPOSED" markers: H-030 Faker, H-043 first-class diff, H-047 v1↔v2 registry audit (`RegisteredAllTransformsBidirectionalTests`), H-049 digest stability (`RegistryDigestRoundTripTests`), H-058 ReadSide reconstruction, H-059 `report` verb.*

**Confirmed OPEN (absent in code):**
- **Schema algebra / lifecycle:** H-007 SchemaDelta type, H-042 Catalog union/intersect/subtract, H-044 schema versioning/history, H-045 pass dependency graph (order is implicit in `chainSteps`), H-046 pass idempotence verification, H-023 coverage-gap tracking (`EmissionGap`), H-048 manifest external verifiability / `verify` verb (`RegistryDigest` exists in the manifest but no standalone re-derivation tool).
- **Deferred-stub (trigger unfired, by design):** H-012 active patterns for SsKey (accessor surface is the abstraction), H-013 units-of-measure on Profile, H-014 phantom pipeline-stage types, H-017 profile inference passes.
- **Speculative deep-categorical (all Skip stubs in `AxiomTests.fs:1184-1245`):** H-061 profunctor, H-063 free monad, H-064 colimits, H-065 Yoneda, H-066 parser CE, H-067 SRTP, H-068 measure-poly, H-069 SqlIdentifier VO, H-070 refinement-types, H-074 functional-dependency detection, H-077 time-series profiling.
- **External-target emitters (none exist under `Targets.*`):** H-078 EF, H-079 dbt, H-080 Liquibase, H-081 OpenAPI/JSON-Schema, H-082 GraphQL, H-083 Data Vault, H-084 Flyway.

---

## F. PERF — optimizations not yet applied (low-leverage, non-blocking by design)
*"CONFIRMED-OPEN" here just means "optimization not yet applied"; all are explicitly low-leverage. The 51-label perf canary is CLOSED — `ComprehensiveCanaryTests.fs` enumerates all 51 labels and asserts ≥45 fire (doc's "~6 labels" is stale; PERF doc confirms 65/65).*

| Item | Verdict | Evidence |
|------|---------|----------|
| `SsdtDdlEmitter` schema-side topological-level grouping | OPEN | `statementsWith` emits a flat topo stream (`order : SsKey list`, `:778`); no per-level grouping (data-side got `composeRenderedLeveled`, schema-side did not). |
| A4 `parseRowsetBundle` sequential `Map.ofList` | OPEN | Relocated to `OssysRowsetReader.fs:665`; ~13 sequential `List.groupBy >> Map.ofList`, not `Array.Parallel`. |
| C8 `schemaObjectFromTableId` allocation | OPEN (structural floor) | `ScriptDomBuild.fs:95-98` allocates a fresh `SchemaObjectName` per call; documented as irreducible (typed-AST per-fragment isolation). |
| D5 `Catalog.create` triple-walk | PARTIAL | D4's dup-Set + ref/index double-walk folded (Wave-0 slice 0.4, `Catalog.fs:1521-1536`); D5's top-level triple-walk over `allKindList` (`:1508,1510,1518`) remains 3 passes. |
| E3 `TopologicalOrderPass` O(\|scc\|²) | OPEN | `internalEdgesOf` still nested `for a … for b …` (`TopologicalOrderPass.fs:328`); conditional on real SCC sizes. |

---

## G. Deferred-by-design (codified triggers — not bugs, not scheduled)
- **DACPAC byte-determinism cash-out** (post-hoc `Origin.xml` canonicalization). `DacpacEmitter` returns raw DacFx bytes; header + tests assert content-equality only, byte-equality explicitly does NOT hold (`DacpacEmitterTests.fs:30,130`). Slice ζ deferred per `CHAPTER_3_X_CLOSE.md:5,74`; trigger = a snapshot consumer requiring byte-stable artifacts.
- **Modality marks → extended properties / comments** (slice ε). `ModalityMark` emits only as diagnostics + JSON-manifest flags; `TenantScoped`/`SoftDeletable`/`SystemOwned` not projected into SQL. Deferred per `CHAPTER_3_X_CLOSE.md:152`; trigger = downstream consumer demanding structured modality access. (`Temporal`→PERIOD FOR SYSTEM_TIME *is* emitted.)
- **Hex ports `IArtifactSink` / `IDeployHost`.** Neither interface exists; absent-until-consumer-demand, consistent with the codebase's "object expressions land when the abstraction lands" posture.
- **`osm_model.json` retirement.** As of 2026-06-08 live OSSYS is primary and `osm_model.json` is demoted to optional fallback — explicitly NOT retired (`V1_INPUT_DEPRECATION.md`); 10 V2 src files still read it; retirement gated on N consecutive green differential runs + operator sign-off (R6 ladder).
- **`UserRemapContext` → `SurrogateRemapContext` subsumption.** Both exist; bridged via one-directional `UserRemapContext.toSurrogate` (`UserRemap.fs:169`, consumed by `MigrationDependenciesEmitter.fs:403`) — a consumption-layer bridge, not a planned full type-merge. The typed User surface (email/SsKey diagnostics) intentionally stays distinct.

---

## H. OUT-OF-CORPUS (V1 C# trunk at `/home/user/outsystems-ddl-exporter/src/`)
*Not assessable from this V2 subtree; V2 is self-contained with zero runtime dependency on V1.*
- V1-soak debt: V1.1 EntityFilters wiring, V1.2 global topological sort for StaticSeeds, V1.3 DatabaseSnapshot dedup. **Note:** the V2 equivalents of V1.1 and V1.2 are *present* in V2 — entity filtering as `ModuleFilter` (built but unwired → see **A7**); topological sort as `TopologicalOrderPass`, applied **catalog-wide** across schema DDL + all data emission + transfer + parallel `levels` (CLOSED, not a backlog item). These V1.x rows pertain only to fixing the V1 C# trunk.
- `DmmCompare` pipeline + DMM lens machinery; `LoadHarness` DMV instrumentation; V1 JSON-aggregation rowsets / `osm_model.json` emission path — all V1-trunk C#, sunset post-cutover+30.

---

## J. Cloud insertion / data producers — design landed 2026-06-09, build outstanding
*New cluster. The producer trinity (`synthetic` / `legacy` / `peer`) feeding cloud insertion was
designed this session (`THE_DATA_PRODUCERS.md`) and is **design-only** — `DECISIONS 2026-06-09` confirms
no code changed. `synthetic` is BUILT (it predates the trinity framing); the Transfer engine the others
run on is BUILT (see §I). What is open is the per-producer ingest + canary + the gate wiring. Full
preflight, milestone graph, acceptance/test-case matrix, critical path, and risk register live in*
**`PREFLIGHT_CLOUD_INSERTION.md`** *— this is the canonical index of the items.*

| # | Item | Verdict | Evidence / target |
|---|------|---------|-------------------|
| J1 | **`rendition` env flag + routing** — add `rendition: physical \| logical` to each environment (operator decision 2026-06-09); distinguishes `peer` (physical source) from `legacy` (logical source). Plus **cross-rendition write-target resolution** for the legacy B→A leg (M1.5 — resolve the write target per-`SsKey` against the sink catalog; the two-contract `runWithRenames` path already does this — see J3). | **M1 LANDED** (env flag); M1.5 DONE via the two-contract path | env-metadata, *not* a `FlowSource` variant — `Rendition` DU + `Environment.Rendition` field parsed from `rendition: physical\|logical` (`MovementSurface.fs`); default absent⇒`None`; both producers move the same `SsKey` model. M1.5 scoped to legacy only — `peer` (A→A) needs none; the cross-rendition write resolves by SsKey against the sink contract in `runWithRenames`. PREFLIGHT M1/M1.5 / risk R-5. |
| J2 | **`peer` / `golden` cloud→cloud** — user exclusion + email re-key on the cloud→cloud (A→A) flow + the **re-key canary** (PE-3). | **DONE** (2026-06-09) | `TransferCanaryTests` `PE-3`/`PE-2` golden (Docker, green) + `TransferRefusalTests` `PE-1` ×3 (pure). Production: `Transfer.wipeTargets` (wipe skips ReconciledByRule + respects LoadSet) + the LoadSet-keeps-reconcile-source fix. **Follow-on:** wire the `golden` CLI flow to *default* a `User:Email` reconcile from config (`resolveFlowSpec`) — today it needs an explicit `--reconcile` tail. |
| J3 | **`legacy` / `preview` — the B→A reverse leg** (NOT foreign schema) — the logical on-prem model (B) piped up into the physical cloud (A); same `SsKey` model. The reverse-leg transfer (LE-1) + the **B→A round-trip canary** (LE-2). | **DONE (engine + canary + flow-recognition face)** (2026-06-09) | `TransferCanaryTests` `M3/LE-2` green via `runWithRenames` (the 6.B.2 two-contract path resolves table+column rendition by SsKey against the sink contract — no OSUSR generator needed). **M3.b flow-recognition face LANDED:** `Command.reverseLegOf` (pure, tested) recognizes a flow as the reverse leg from the M1 `rendition` flag (live logical source → live physical sink) + resolves its two conns; `TransferRun.runReverseLeg` delegates to `runWithRenames` given the two contracts. **Residual CLOSED (2026-06-10):** `CatalogRendition.logical`/`.physical` render the two contracts from the one authored model; `PlanAction.RunReverseLeg` carries the model; `Transfer.runReverseLegThroughConnections` + the `runReverseLegTransfer` CLI face replace the runtime refusal; LE-1 rendered-contracts canary green. The live two-DB attribute-SsKey recovery was NOT pursued (re-open trigger: a reverse leg over an estate with no authored model). See `DECISIONS 2026-06-10 — J3 residual CLOSED`. |
| J4 | **Capability survey as advisory G0** — wire the survey into the run verbs (S3, per R6) + wire `Preflight.all` (= A1) so the survey feeds one composed gate; add the P10 user-directory probe. | **DONE (G0a + G0b + G0c)** (2026-06-09) | **G0a (= A1 closed):** `Preflight.all`'s first production caller — `runCore`'s pre-plan CDC→spanning gates route through it (codes/precedence preserved; CDC canary + AC-I5/PE-1/classify green). **G0b:** the P10 user-directory probe — `ReadSide.userDirectoryReadability` + the `EnvironmentReport.UserDirectory` field (NOT a `Capability` variant — totality preserved); Docker witness `UserDirectoryProbeTests`. **G0c:** the survey is surfaced as a STDERR advisory in the run-flow path (`runTransfer`), warn-not-stop per R6 (the standalone verb keeps exit-7); `CapabilitySurvey.advisoryLines`/`blocked` (pure, tested). PREFLIGHT M4. |
| J5 | **Managed-environment execute (OPEN-2)** — the ops spike (writable-connection probe) + `--execute` under R6 + `--preview-row-cap`. | OPEN — **blocked on OPEN-2** | `PRESCOPE_TRANSFER.md` §13; `TRANSFER_ISOMORPHISM_SUBSTANTIATION.md` §2. PREFLIGHT M5. |
| J6 | **Follow-ons** — cyclic `AssignedBySink` (6.A.2) + composite-identity capture (6.A.3); `MERGE…OUTPUT` set-based capture (trigger-gated); synthetic `--rows N`/`--seed` (= D8); scoped-delete CLI exposure (= A3); user-map walkable Surface (= D7). | OPEN (pull under a consumer) | acyclic AssignedBySink shipped 2026-05-31; the rest are named follow-ons. PREFLIGHT M6. |

**Acceptance gate for the cluster:** all three producer canaries green (`π∘σ≈id` ✅ · PE-3 re-key
canary · LE-2 B→A round-trip canary); `golden` excludes users + re-keys by email under a passing
survey P10; the legacy B→A leg resolves write targets against the sink (M1.5); every cross-boundary
erasure named; all stay inside R6. See `PREFLIGHT_CLOUD_INSERTION.md` §3 for the full test-case matrix.

---

## I. Confirmed CLOSED-stale (do not re-chase)
*The bulk of the original candidate list. Recorded so future sessions don't re-investigate.*

**Pre-flight / transfer integrity (all CLOSED-stale unless noted):** connection
pre-flight (`Preflight.connectionPreflight:200`, wired `TransferRun:422`,
`Program:1392/1607`); permission/grant pre-flight (`permissionPreflight:307`,
`captureGrantEvidence:331`, `GrantEvidence:252`, wired `spanningPreflight:428`)
— object-scope refinement survey-gated only; `transfer` silent row-drop → now
exit-9 via `exitCodeForReport:962` (`TransferCanaryTests:428`); write-denied
sink (= connection+permission pre-flight); validate-user-map **pre-write** halt
(`validateUserMap:374`, wired `runCore:597`, `TransferRefusalTests:130`);
NOT-NULL data-compat gate on execute (`tighteningPreflight`, wired
`Program:1474`, `MigrationRun:435`); on-disk-bytes pre-flight (spec-equivalent —
real `COUNT_BIG(... IS NULL)` probe `LiveProfiler.fs:169`).

**CatalogDiff / ReadSide surface (all CLOSED):** C1 four-channel diff
(kinds/attributes/references/indexes/sequences, `applyDiff` patches all);
attribute-level changes + diff→ALTER (`SchemaMigrationEmitter.emitWith`, wired
`MigrationRun.fs:127`); `CatalogDiff.compose` (consumers `Lifecycle.netDiff:194`,
`Episode.netSchemaDiff:239`); refactorlog accumulate + real episode clock
(`RefactorLogEmitter.accumulate:375`, `toRefactorLogXmlAt:165`,
`RefactorLogAccumulateTests`); change-manifest full displacement
(`ChangeManifest` `ToleranceResidual`+`AppliedTransforms` `:36,43`, consumers
`ReportRun:28`, `Pipeline:1256`); `Kind.Indexes` via ReadSide
(`readIndexes:496`, `attachIndexes:1282`; `Tolerance.IndexesUnreflected` **no
longer exists** — narrowed to `IndexOptionsUnreflected`); FK-trust on readback
(`is_not_trusted` read+set via `withConstraintState`, `ForeignKeyReadbackTests`);
no-cheat round-trip across reference/index/sequence (`CatalogDiffTests` P1.4–P1.6).

**Migrate / CDC / lifecycle (all CLOSED-stale):** migrate orchestrator threads
rename→Transfer (`executeWithData`+`runWithRenames`+`repointRows`); wipe-and-load
`EmissionMode` DU + live FK-ordered delete leg (`EmissionMode.fs`,
`wipeFkOrdered` in `runCore:611`, CLI `--fresh`/`--replace`); CDC exact-count
(`CdcMeasureTests` asserts `+1`/`+2`/`0`, not `>`); CDC-silence on redeploy
(AC-X4/X8 assert zero ALTERs AND zero captures, meter proven live); episode
record (`executeAndRecord`→`record`, wired `Program:1493/1654`); **eject** verb
(`EjectRun.fs` complete + FTC self-verify, wired `projection seal --store`);
**drift** (`DriftRun.detect` deployed-vs-model, wired `check drift`); full-export
publish-with-changelog (`Pipeline.runWithConfigAndStore` accumulates refactorlog
+ ChangeManifest + episode).

**Identity / data fidelity (CLOSED-stale):** SsKey synthesized-rename no longer
silent (`synthesizedRenameWarnings` + `identity.synthesizedRenameUnstable` +
V2.SsKey serialize/deserialize persisted as extended property & recovered on
readback); extended-property round-trip (emit at SCHEMA/TABLE/COLUMN/INDEX +
`ReadSide.readExtendedProperties:576`; `CommentMetadataUnreflected` retired);
CHECK/NOCHECK trust (`untrustedFkAlters:349` two-step ALTER, readback of
`is_not_trusted`); null-safe predicate across 8 SqlLiteral types
(`CdcSilenceTests` Track D); computed-column exclusion from MERGE (`Computed =
None` filter in both data emitters); representation tolerances
(`CharAnsiPaddingTolerated`, `DecimalScaleTolerated`); Coordinate VOs now cashed
across the IR.

**Prescope / chapter / analyzer (CLOSED):** `AssignedBySink` realization wiring
(`insertCaptureRow:106` per-row INSERT + `OUTPUT inserted.*`, wired into
`writePlan` with topo-ordered remap; `TransferCanaryTests:736`); SnapshotRowsets
members (isSystemEntity→SystemOwned, ColumnChecks, ColumnReality all parsed —
only the OSSYS-*source* MetadataSnapshot reflection stays trigger-deferred);
chapter A.4.7 slice η (`emit --skeleton-only` + `RegistryDigest` SHA256 +
`RegistryDigestRoundTripTests`); Phase-8 custom analyzer
(`NoUnsafeTimeInCoreAnalyzer.fs` — real CLI+Editor analyzer, 7 forbidden
primitives, `Severity.Error`); Stage-2 typed VOs (defined + adopted across 28
src files). **Voice waves 0–6** (gates/diff/lifecycle/move all wired per the
2026-06-09 close doc). **Synthetic data** + **live Watch** + instrument slices
1–4/8.

**All `CHAPTER_*_OPEN.md` slices CLOSED by their `_CLOSE.md`:** 3.2, 3.5, 3.X
(slices ε/ζ deferred-by-design), 4.1.A, 4.1.B, 4.2, 4.3 (δ/ε deferred), 4.4,
4.5, 4.6, 4.7, 4.8 (α partial → B9), 4.9 (α′ partial → B9), 5.0, A.0′, A.4.7
(η→A.4.7′), A.4.7′, B.3, B.4. **STAGING** S0.A–S0.L all shipped. **SLICE_D_***
all shipped 2026-05-23 (only D.2.e ALTER-NOCHECK textual rework + formatter
lineage-events deferred). **No-CLOSE-file ambiguous (unverified):** CHAPTER_3_6
slices β–ε; CHAPTER_5 slices ν/θ (ν analyzer + θ Stage-2 VOs both verified
shipped above).

---

## Suggested execution order
1. **A (wiring gaps)** — highest value/effort. A1 (`Preflight.all`), A7
   (`ModuleFilter`), A2 (`--resumable`), A3 (scoped delete), A5 (rename
   migrate-with-data), A6 (`diff` verb), A4 (stderr→structured). Each is a
   small connect-the-wire, mostly with the capability + tests already present.
2. **B (refactor debt)** — B1/B2/B3 (combinator extractions, high duplication
   payoff), then B6 (fixture dedup), B7 (`Deploy.fs` split), B5/B8/B9.
3. **D (terminal UX)** — D1/D2/D3 are clean small slices the voice-close doc
   already scoped.
4. **C** — accept-or-model decisions (C1/C2 are documented tolerances; C3/C4
   are typed-VO modeling slices when a consumer pushes).
5. **E/F** — speculative/by-design; pull under a concrete consumer only.

*Verification provenance: 8 parallel code-confirmation agents over `src/` +
`tests/` at HEAD `105d8ec`, under an explicit anti-false-positive discipline,
following a doc-corpus compilation pass. Re-confirm `file:line` anchors before
acting — the tree moves.*
