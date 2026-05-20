# Handoff letter — 2026-05-20 (Chapter C slice 2 of 6 done; mid-chapter)

To the next agent.

You're picking up V2 with **Chapter C two slices in** and the substrate doing more work for you than the slice queue length suggests. C.1 (tightening axis) shipped earlier today and set the binder + chain-factory template; C.2 (special-circumstances axis — `AllowMissingPrimaryKey` + `CircularDependencies` consumer) shipped this session as the **first non-binder-only Chapter C slice** — operator allowlists now bind from config + a post-chain scan emits the missing-PK / cycle findings as typed `DiagnosticEntry`s through the LogSink envelope stream, with `Metadata.acceptedVia` annotation on allowlist matches per the annotate-don't-suppress discipline. Four Chapter C slices remain. **Your next slice is C.3 (emission-folders)** unless the principal-PO redirects.

## What C.2 changed that C.3 inherits

The big load-bearing shape C.2 introduced is the **pure-additive post-chain scan pattern**, generalizable across the remaining slices. Rather than reshape pass return types (the architect's slice-C.1 HANDOFF letter implicitly assumed this; would cascade through ~90 consumer sites for `TopologicalOrderPass` + `SymmetricClosure` alone), C.2 added `src/Projection.Pipeline/SpecialCircumstancesDiagnostics.fs` — a 140-LOC scan step that observes the post-chain `Catalog` + `ComposeState` (read via the new `Compose.projectFromChainWithState` factored out of `projectFromChain`) and emits `DiagnosticEntry`s with operator-allowlist annotations applied in one pass. **Future slices needing to surface internal pass state without return-type cascade should mirror this shape.**

The other big surface change is **`runWithConfig` widened** — return type went from `Task<Result<string list>>` to `Task<Result<Compose.RunReport>>` where `RunReport = { Paths : string list; Diagnostics : DiagnosticEntry list }`. The three consumers (`Program.fs runFullExport` + `Program.fs runEmitFromConfig` + `FullExportCliTests.runFullExportInProcess`) all updated; the diagnostic stream flows to LogSink envelopes in full-export, to stderr text in legacy `emit --config`, to the test harness's captured envelope stream in tests.

## Two principal-PO scope decisions C.2 surfaced (read these — they generalize for C.3-C.6)

(a) **Operator config ref shape: typed tuples over full SsKey-ref form.** The architect's slice-C.1 letter recommended copying `TighteningBinding`'s logical-or-physical `SsKey` ref shape verbatim for C.2. The operator chose typed `(Module × Entity)` tuples (`Config.LogicalName list`) instead — lighter parser, smaller test surface, no physical-name fallback. The trade-off: divergent surface from C.1's tightening axis (which DOES carry the logical-or-physical shape). The principle: **slice-by-slice scope decisions belong to the principal-PO at slice open, not pre-decided by HANDOFF recommendations.** Bring C.3's `EmissionFolders` ref shape decision (likely `Map<SsKey, string>` with the SsKey resolved at bind time from a `LogicalName`) to the principal-PO; don't presume.

(b) **Annotate vs suppress, and what depth the annotation lives at.** Operator decision: annotate, not suppress, per the slice-6 reshape lesson ("actionability = enrichment + presentation, NOT occlusion"). What surprised me: the architect's HANDOFF letter assumed the annotation surface was `DiagnosticEntry.Metadata.acceptedVia` — but missing-PK and cycle signals **don't reach the DiagnosticEntry stream at all today** (missing-PK lives in `LineageEvent.Annotated`; cycles live in `TopologicalOrder.Cycles : CycleDiagnostic list`). So C.2's "annotate" actually required FIRST lifting these signals into operator-visible `DiagnosticEntry` emissions (new emission sites) BEFORE annotation could land. That's the "full lift" scope option I brought to the principal-PO; operator accepted the budget overrun (~5-7h instead of 3-5h). **C.3 may have a similar discovery — emission folders likely don't have an existing operator-visible diagnostic surface either; budget accordingly.**

## Where you are in the spine

C.3 wires the **emission-folders axis** per `DECISIONS 2026-05-19 (chapter B.4 hygiene strike + axis-survey supplement)`. The scope: a new `Overrides.EmissionFolders : Map<SsKey, string>` config section + an `SsdtFile.RelativePath` rewrite pass + emit-time validation. Touchpoints per the architect's slice-C.1 letter: `src/Projection.Targets.SSDT/SsdtFile.fs` for the `RelativePath` shape; `src/Projection.Pipeline/Pipeline.fs` around line 120 for the `SsdtBundle.compose` site; `src/Projection.Pipeline/Config.fs` for the new section. The rewrite fires **after** SSDT emit produces the bundle but before write. Architect's recommendation: add `Compose.applyEmissionFolderOverrides : Map<SsKey, string> -> SsdtBundle -> Result<SsdtBundle>` as a post-emit step in `runWithConfig`; validate target folder paths at the binder layer (no `..`; no absolute paths; no path-separator chars in segment names) before the rewrite fires.

## Read order (~30 min) — same shape as the C.1 letter below

1. **This letter + the slice-C.1 architect letter below** (the "What to know about the V2 shape" + "Per-slice guide" sections specifically — they're still the operator's-manual for the next 2-3 weeks).
2. **`DECISIONS 2026-05-20 (slice C.2 — special-circumstances axis full lift)`** — names the binder shape, the pure-additive scan pattern, the `acceptedVia` annotation convention, the scope-adjustment rationale, the disciplines reinforced.
3. **`src/Projection.Pipeline/SpecialCircumstancesBinding.fs` (~150 LOC)** — read alongside `TighteningBinding.fs`; the binder shape generalizes for C.3 (typed `EmissionFolders` resolution).
4. **`src/Projection.Pipeline/SpecialCircumstancesDiagnostics.fs` (~140 LOC)** — read for the post-chain scan pattern. C.3's `Compose.applyEmissionFolderOverrides` is a sibling but operates on `SsdtBundle` (not `DiagnosticEntry list`).
5. **The five operating disciplines below** before writing code (same four as C.1's letter + one new from C.2).

## Disciplines internalized this session (C.2 contribution + carry-forward from C.1)

**Pure-additive scan over IR-traversal cascade (C.2 contribution)** — when a new operator-visible signal needs lifting from internal pass state, prefer a post-chain scan step over pass-internal writer-extension unless the new signal requires per-pass attribution. Trade-off: small predicate duplication is acceptable (both consult the same primitive — e.g., `Kind.primaryKey k = []`); the alternative — threading the `Diagnostics` writer through every pass for one new emission — is not. Mirrors the C.1 "factory shape" pattern (`allChainStepsFor` policy-parameterized chain) one layer up: structural observability without invasive rewiring.

**The four C.1-era disciplines carry forward unchanged.** Operator-supplied-ref resolution (resolve at bind time, not at use time; structured ValidationError on miss); single-entry-shape pattern across DU variants (apply if C.5 goes per-target Insertion); Global-MutableState xUnit collection (any test touching Bench/LogSink state); TRX-first test-failure capture protocol (mandatory when dotnet test reports `Failed:` > 0).

**HANDOFF append-only-within-chapter + handoff-as-prose-letter disciplines** — read the CLAUDE.md operating-disciplines table rows. C.2 honored both (this letter prepends above the C.1 + B.4 close letters via Edit, not Write overwrite; addresses you in second person).

## Test baseline + branch state

**1871/1871 non-Docker passing; 0 warnings under TreatWarningsAsErrors=true.** Was 1854 pre-C.2 (post-housekeeping baseline above the architect's 1793 starting point); +17 from the two new C.2 test files (7 binder facts + 10 scan facts). Operator-reality canary unchanged from C.1 (no chain-factory touch this slice; the new scan runs once at `runWithConfig` boundary, costs ~1 catalog walk for missing-PK + one `Set.contains` per cycle). Branch stays `claude/v2-chapter-c-continue-XGxAL`; PR #552 has merged the prior C.1 + B.4 work into `main` — this session's commit lives on the continuation branch.

## Pitfalls C.2 hit that you can avoid

- **Don't presume the HANDOFF letter's recommended consumer shape exists.** The slice-C.1 architect letter recommended `DiagnosticEntry.Metadata.acceptedVia` as the annotation surface. The signals being annotated turned out to NOT flow through `DiagnosticEntry` at all. Verify the consumer substrate exists BEFORE committing to the architect's recipe; bring scope-adjustment options to the principal-PO if the substrate is missing.
- **Don't widen pass return types speculatively.** I almost reshaped `SymmetricClosure` + `TopologicalOrderPass` from `Lineage<_>` to `Lineage<Diagnostics<_>>` to absorb the new emissions. The cascade is ~90 consumer sites. The pure-additive scan-step alternative is structurally equivalent + zero cascade. Run the blast-radius `grep` before any return-type widening.
- **`Model.ValidationOverrides` vs `Overrides`.** The dormant `AllowMissingPrimaryKey` field lived in the wrong section pre-C.2 (`Model.ValidationOverrides` rather than `Overrides`). Moving was correct — `Overrides` is the operator-driven-catalog-rewrites/annotations neighborhood; `Model` owns the catalog source. C.3 may face a similar location question if `EmissionFolders` parses anywhere else today; verify.

## Open questions for chapter close (C.6+)

Same as the C.1 letter's chapter-close section. The C.2 contribution to the list: when C.6 ships, the chapter-close ritual's "Promote any new L3 axioms" step should consider an **L3-CC-AcceptanceAnnotation** axiom (every operator-visible structural finding with an addressable acceptance path carries the annotation in `Metadata.acceptedVia`; the diagnostic remains visible). The annotate-don't-suppress discipline is operative for all of C.3-C.5 as the special-circumstances pattern generalizes.

Hold the spine. The Chapter C arc is mechanical wiring against a substrate that already exists, plus the occasional discovery that a recommended consumer surface doesn't exist (C.2's case) — bring those scope-adjustments to the principal-PO at slice open. Each slice earned, one at a time.

— The slice-C.2 architect.

---

# Handoff letter — 2026-05-20 (Chapter C slice 1 of 6 done; mid-chapter)

To the next agent.

You're picking up V2 mid-Chapter-C with a substrate that's almost embarrassingly complete underneath you. Chapter B.4 closed with Phase B's *structural* exit gate green; `projection full-export --config <path>` emits a conforming NDJSON event stream end-to-end; the `LogSink` substrate + `§11` rollup + `Bench.Stats` integration are wired; the actionable-diagnostics enrichment routes `suggestedConfig` payloads to operators on every applicable finding. **Cluster A7 turned out to be already shipped** via slice 5.13.production-wiring-classification + sibling slices on 2026-05-18 — Polly transient retry, the closed-DU `MetadataExtractionError`, the 22-rowset contract check, and the progress callback all exist in `src/Projection.Adapters.OssysSql/{Retry.fs, MetadataExtractionError.fs, MetadataSnapshotRunner.fs}`. Don't re-implement; the parity-matrix amendments are also already in place. **Slice C.1 (tightening axis wiring)** shipped this session and set the template the remaining five slices follow. **Faker stays deferred** per the principal-PO this session, until the corp-network V1 HEAD access opens.

Your next slice is **C.2 (special-circumstances axis)** unless the principal-PO redirects. The scope per `DECISIONS 2026-05-19 (chapter B.4 hygiene strike + axis-survey supplement)`: an `Overrides.AllowMissingPrimaryKey : SsKey list` allowlist + a consumer pass that reads it and suppresses the matching diagnostics, plus a consumer pass for the already-parsed `Overrides.CircularDependencies.AllowedCycles`. Two scope decisions the strategic exploration didn't resolve, both worth bringing to the principal-PO at slice open: (a) does `AllowMissingPrimaryKey` get the same logical-or-physical SsKey ref shape `TighteningBinding` uses, or simpler? (b) does the `CircularDependencies` consumer suppress diagnostics outright, or annotate them as accepted? My recommendation: copy the TighteningBinding ref-resolution shape verbatim for (a) — operators get a consistent surface across Chapter C, and the ValidationError code namespace stays uniform. For (b), annotate (don't suppress) — per the slice-6 reshape lesson ("actionability = enrichment + presentation, NOT occlusion"), source defects must remain visible; operator config marks them as *accepted* rather than *invisible*.

## Read order (~30 min)

1. **This letter's "What to know about the V2 shape" + "Per-slice guide" sections below** — they're the load-bearing operator's manual for the next 2-3 weeks of work. Don't skip to the code without these.
2. **`DECISIONS 2026-05-20 (slice C.1 — tightening axis config wiring)`** — names the binder shape, override-resolution discipline, why `allChainStepsFor` was the right factory shape, the test-collection hygiene fix folded inline. Treat as your design template.
3. **`src/Projection.Pipeline/TighteningBinding.fs`** (~190 LOC, single read) — every remaining Chapter C binder mirrors this shape. Read `resolveAttributeRef` carefully; the try-logical-then-physical pattern is the operator-facing surface you'll repeat.
4. **`DECISIONS 2026-05-19 (chapter B.4 hygiene strike + axis-survey supplement)`** — the canonical Chapter C 6-slice plan with axis numbers. Names which `Pipeline.Config` sections already parse-but-ignore today so you don't re-add what's already there.
5. **`CLAUDE.md` operating-disciplines table** — read the four newest rows from this session before writing code: "HANDOFF.md is append-only within a chapter" (never `Write`-overwrite this file), "Handoff message = forward-looking letter" (the meta-discipline the operator surfaced), "Test-failure capture protocol — TRX-first" (mandatory when `dotnet test` reports `Failed:` > 0), and the **`Global-MutableState` xUnit collection** (any test touching `Bench` or `LogSink` state needs `[<Xunit.Collection("Global-MutableState")>]`).
6. **The chapter B.4 close letter below this one** — substantive backdrop. The L3-X11 + L3-X12 axiom promotion + the full-export CLI scope + the dormant-config-section sweep all anchor Chapter C's positioning.

## What to know about the V2 shape (the "101 class")

V2's pipeline is `OSSYS source → CatalogReader → Catalog → pass chain (RegisteredTransforms.allChainSteps) → sibling Π emitters (SSDT / JSON / Distributions) → on-disk artifacts`. The pass chain has 12 entries: 6 catalog-rewriting passes (CanonicalizeIdentity / VisibilityMask / NamingMorphism / NormalizeStaticPopulations / SymmetricClosure / TableRename) followed by 6 decision-set passes (TopologicalOrderPass / NullabilityPass / UniqueIndexPass / ForeignKeyPass / CategoricalUniquenessPass / UserFkReflowPass). Catalog-rewriting passes mutate the Catalog in flight; decision-set passes write results to `ComposeState` slots.

`Policy` is the five-axis operator-intent aggregate (Selection / Emission / Insertion / Tightening / UserMatching) at `src/Projection.Core/Policy.fs:372`. `Policy.empty` is the no-policy default. **The pre-C.1 codebase only had ONE site instantiating Policy: `Policy.empty` at module init in `RegisteredTransforms.fs` lines 52-56 + 75-87.** C.1 added `allChainStepsFor : Policy -> Profile -> chain` so `runWithConfig` could thread non-empty Policy through. The factory shape is the load-bearing innovation; when C.4 / C.5 add new Policy fields, they extend `Policy` (already-typed) + the factory signature stays the same.

`Pipeline.Config` (`src/Projection.Pipeline/Config.fs`) is the operator JSON parser. It's **shape-complete but mostly wiring-incomplete** — only 3 sections had live consumers before chapter B.4 (`Model.Path` → catalog source; `Overrides.TableRenames` → rename pass; `Output.Dir` → write target). C.1 added a fourth: `Policy.Tightening` → `TighteningPolicy` via `TighteningBinding.fromConfig`. The rest of Chapter C wires the dormant sections per the strategic exploration plan. **`Pipeline.Config` accepts unknown sections silently** (no schema validation by design — the parser only knows what it knows; operators can hand-write future-shaped configs without runtime surprises). When C.2 adds `Overrides.AllowMissingPrimaryKey`, the parser needs the new field; consumer pass needs the wiring.

`LogSink` (`src/Projection.Pipeline/LogSink.fs`, ~540 LOC) is V2's structured event substrate, shipped at slice 6.5. Hand-rolled per `docs/logging-format.md` §15.2 — no `Microsoft.Extensions.Logging`, no `Serilog`, no third-party logger. The runtime touches: `LogSink.reset()` at entry; `LogSink.emit envelope` for each event; `LogSink.recordStage` / `recordArtifact` for the runSummary roll-up; `LogSink.runComplete outcome command benchStats` as the terminal mandatory event. **Tests that touch LogSink global state MUST carry `[<Xunit.Collection("Global-MutableState")>]`** or they'll race other tests. C.2-C.6 likely won't need direct LogSink wiring (the existing CLI surface emits structurally); but if a slice adds new event codes (e.g., `transform.declined` rationales for the C.2 suppression pass), the additions land in `docs/logging-format.md` §7 (the codes table) under the additive-only discipline + tests verify the events fire.

`Bench` (`src/Projection.Core/Bench.fs`) is the process-scoped performance-observation substrate. `Bench.scope label` is RAII timing; nested scopes compose; the snapshot rolls up. The `Bench.Stats` records surface in the `summary.runComplete` envelope's `aggregates` array under `category=summary, code=bench.label`. Same `Global-MutableState` collection rule applies.

`Diagnostics` (`src/Projection.Core/Diagnostics.fs`) is V2's typed diagnostic-entry substrate. `DiagnosticEntry` carries Source + Severity + Code + Message + SsKey + Metadata + **`SuggestedConfig` option** (the actionable-payload field from slice 6). Pass-produced entries flow through `Lineage<Diagnostics<'output>>`. When C.2's suppression pass marks diagnostics as accepted, the natural shape is `DiagnosticEntry.Metadata` carrying an `"acceptedVia": "config:allowMissingPrimaryKey"` key — the entry stays in the stream + downstream consumers see the annotation.

`SsKey` (`src/Projection.Core/Identity.fs:45`) is the typed identity. Four variants (`OssysOriginal of Guid | Synthesized of source × basisParts | DerivedFrom of parent × reason | V1Mapped of v1Sskey × v2Namespace`). Operators reference SsKeys textually via `SsKey.rootOriginal` rendering. `TighteningBinding.resolveAttributeRef` is the bidirectional bridge: operator string → catalog walk → typed SsKey. Reuse it; don't reinvent.

## Per-slice guide (for the next 5 slices)

**C.2 — Special-circumstances axis (next).** Two consumer paths over already-parsed config (`Overrides.CircularDependencies`) + one new section (`Overrides.AllowMissingPrimaryKey : SsKey list`). Touchpoints: `src/Projection.Pipeline/Config.fs:121` (extend `OverridesSection` with `AllowMissingPrimaryKey : string list`); the parser pattern lives in `parseOverrides`. The consumer pass either lives at `Compose.runWithConfig` (post-rename, pre-project, like `applyRenames`) or as a new entry in `allChainStepsFor` between rename and the tightening passes. Recommendation: keep it inline in `runWithConfig` like `applyRenames` does, since both are "operator-driven catalog rewrites" rather than typed decision-set passes. For ref resolution: copy `TighteningBinding.resolveAttributeRef` — the user-facing surface should be consistent. Tests follow `TighteningBindingTests.fs` structure (~12-15 tests covering parse + bind + resolution + structured-error paths).

**C.3 — Emission-folder targeting axis.** New `Overrides.EmissionFolders : Map<SsKey, string>` config section + `SsdtFile.RelativePath` rewrite pass + emit-time validation. Touchpoints: `src/Projection.Targets.SSDT/SsdtFile.fs` for the `RelativePath` shape; `src/Projection.Pipeline/Pipeline.fs` for where the SSDT bundle is composed (~line 120, `SsdtBundle.compose`); `Config.fs` for the new `Overrides.EmissionFolders` section. The rewrite pass needs to fire **after** SSDT emit produces the bundle but before write. Recommendation: add a `Compose.applyEmissionFolderOverrides : Map<SsKey, string> -> SsdtBundle -> Result<SsdtBundle>` post-emit step in `runWithConfig`; validate that target folder paths respect SSDT naming conventions at the binder layer (no `..`, no absolute paths, no path-separator chars in segment names) before the rewrite fires. Tests cover binder + rewrite + the validation rejections.

**C.4 — Tag-groups axis.** This is the only Chapter C slice without a pre-existing substrate — needs new types. Per `DECISIONS 2026-05-19` decision 3, ship as a **closed `TransformGroup` DU** (operator-facing preset list — NOT operator-defined sets), not as a `string list`. The preliminary preset list per the decision: `CDC | UATUsers | MigrationDependencies | Bootstrap | RefactorLog | ...`. The closed-DU-expansion empirical-test discipline applies — preset membership grows under named-trigger evidence at chapter close, not under operator demand at config-author time. Touchpoints: add `Projection.Core/TransformGroup.fs` for the DU + a `TransformGroup.tryParse : string -> Result<TransformGroup>` to bridge operator config strings; extend `RegisteredTransformMetadata` with `Tags : Set<TransformGroup>` (probably; possibly `Tags : TransformGroup list` — check the existing metadata shape); extend `Compose.runWithConfig` to filter the chain by `Policy.TransformGroups : Map<TransformGroup, bool>`. Recommendation: the preset list at land-time should derive from the actual transform set that exists then (today's 12-entry chain + 5 strategy registrations + the 1 adapter entry = 18 registered transforms per `RegisteredTransforms.all` + A41); only emit presets for transforms that actually carry a Tags annotation. Tests verify (i) unknown preset strings surface ValidationError, (ii) the filter actually excludes tagged transforms from the chain when `Policy.TransformGroups` has the matching `false` value, (iii) untagged transforms always fire (Tags-as-filter is opt-in).

**C.5 — Insertion semantics.** `Policy.InsertionPolicy` DU exists at `src/Projection.Core/Policy.fs:77` — 4 variants (SchemaOnly / InsertNew / Merge / TruncateAndInsert). C.5's job is the config-binding layer + threading. Touchpoints: extend `Config.PolicySection.Insertion` from `string` to a typed binding (today it's a free-form `string` field per the dormant-section sweep); write `InsertionBinding.fromConfig : Catalog -> string -> Result<InsertionPolicy>` mirroring `TighteningBinding.fromConfig`; thread through `buildPolicyFromConfig` (extend the Policy record-update site). The single-entry-shape pattern from C.1 applies if you'd let operators choose per-target (e.g., `[{ "target": "AppCore.User", "policy": "merge" }, ...]`); the simpler shape is one global InsertionPolicy. Recommendation: ship the simpler shape first; promote to per-target if a real operator workflow demands it (IR-grows-under-evidence). Tests follow the C.1 template; ~10 tests should cover parse + bind + the four variant mappings + structured-error paths.

**C.6 — Verbosity flags.** `--verbose` already lands at slice 7 in the `full-export` Argu surface; flips `LogSink.setVerbose true`. C.6 adds `--debug` (mirror of `--verbose` but lower-level) + per-category filters (e.g., `--quiet-categories=transform`). Touchpoints: `src/Projection.Cli/FullExportArgs.fs` for the new Argu DU variants; `src/Projection.Pipeline/LogSink.fs` for the per-category suppression logic (extends the existing `Verbose: bool` ref to a richer shape — possibly `Verbosity: VerbosityFilter` record carrying per-level + per-category flags). Recommendation: keep `LogSink`'s mutable-state additions in the same `RunState` record + lock-protected access pattern. The Spectre TtyRenderer micro-chapter stays deferred — confirm with the principal-PO at C.6 open that the scope is just the flags, not the TtyRenderer. Tests: extend `LogSinkTests.fs` with verbosity-filter cases.

## Disciplines codified this session — internalize before code

The **operator-supplied-ref resolution discipline** (load-bearing). Resolve textual config refs against the loaded catalog at config-bind time, NOT at pass-execution time. Errors surface early with structured `pipeline.<axis>.<problem>` ValidationError codes; passes consume already-typed `SsKey` values. C.2's `AllowMissingPrimaryKey` and C.3's `EmissionFolders` both involve operator-supplied SsKey refs — follow the `TighteningBinding.resolveAttributeRef` template verbatim, including the try-logical-then-physical fallback.

The **`allChainStepsFor` factory shape** is how Policy threads through. The static `RegisteredTransforms.allChainSteps` bakes `Policy.empty` at module init; the new `allChainStepsFor : Policy -> Profile -> chain` accepts caller-supplied Policy. If C.4 or C.5 needs to add a new Policy field, extend the factory's signature; don't mutate the static chain. The static one stays for canary tests + the skeleton-only / no-policy paths (verified at chapter B.4 close — Compose.run / Compose.runSkeletonOnly both consume the static chain).

The **single-entry-shape pattern across DU variants** kept C.1's operator surface clean. `TighteningInterventionEntry` carries every field from all four intervention variants as `option`-typed, with a `Kind` discriminator string the binder dispatches on. Trade-off: the binder validates Kind→fields pairing at runtime. **Apply to C.5 if you go per-target**; skip for the simpler one-global shape.

The **Global-MutableState xUnit collection** (defensive). Any test that calls `Bench.reset` / `Bench.scope` / `LogSink.reset` / `LogSink.emit` / `LogSink.setWriter` / `LogSink.runComplete` MUST carry `[<Xunit.Collection("Global-MutableState")>]` at module top. The collection serializes within itself; doesn't affect the ~1700 other tests' parallelism. Skipping this attribute will produce intermittent failures that look like real bugs but are race conditions — wasted-cycles risk per the test-failure capture protocol.

The **TRX-first test-failure protocol** (mandatory). When `dotnet test` reports `Failed: N` with N > 0, immediately re-run with `--logger "trx;LogFileName=test-results.trx" -- RunConfiguration.ResultsDirectory=/tmp/test-results`, then `grep -oE 'testName="[^"]*"' /tmp/test-results/test-results.trx | sort -u` for names and `grep -A 30 'outcome="Failed"' /tmp/test-results/test-results.trx` for details. The console output interleaves across parallel test classes and is unreliable for failure identification. Skipping this protocol wastes cycles.

The **HANDOFF.md append-only-within-chapter discipline** (defensive against the failure mode that triggered this codification). The HANDOFF.md letters within a chapter accumulate, newest at top; never `Write`-overwrite the file. At chapter close (after C.6), rotate the entire file to `HANDOFF_CHAPTER_4.md` and write a fresh chapter-close letter to a new `HANDOFF.md`.

The **handoff-message-as-prose-letter discipline** (read the CLAUDE.md row; codified this session). When asked to write a handoff message, write a prose letter addressed to "you" — not a structured summary doc.

## Common pitfalls I'd avoid

- **Don't re-implement A7.** Cluster A7 rows (32 / 33 / 34 / 35 / 36) all shipped on 2026-05-18 via slice 5.13.production-wiring-classification + .progress-callback + .command-timeout. Polly retry, the closed-DU error, the 22-rowset contract check, the progress callback — all there. Verify in `src/Projection.Adapters.OssysSql/{Retry.fs, MetadataExtractionError.fs, MetadataSnapshotRunner.fs}` before opening a new slice on this surface.
- **Don't reach for `System.CommandLine`** — V2's CLI library is Argu per `docs/logging-format.md` §15.2. The `FullExportArgs.fs` template lives at `src/Projection.Cli/FullExportArgs.fs`; copy that shape for any new CLI surface in C.6.
- **Don't reach for `Microsoft.Extensions.Logging` or `Serilog`** — V2's LogSink is the logger per §15.2 banned alternatives. The hand-rolled `Utf8JsonWriter` pattern is the entire emission stack.
- **Don't add per-line `Console.WriteLine` / `printfn` / `eprintfn`** anywhere outside `LogSink.fs` (and the existing audited sites in `Program.fs` for stdout artifact-path narration). Per §15.5: every operator-visible byte flows through the LogSink envelope.
- **Don't widen `Policy` fields without extending `Policy.empty`** — the empty-policy invariant is load-bearing (skeleton-purity property + the entire no-policy-default discipline rests on it). When C.4 adds `TransformGroups`, the field's empty value must be a no-op (e.g., `Map.empty` meaning "no filters").
- **Don't write a status-report when asked for a handoff letter.** The operator names this as a recurring agent failure mode. Read the "Handoff message = forward-looking letter" CLAUDE.md row.

## Recommendations on session shape

Each Chapter C slice should ship in a session of ~3-5 hours focused work. Pattern: read the relevant existing surface (binder + chain factory + tests pattern; ~30 min); design + write the new types (~30 min); write the new code under the patterns above (~60-90 min); write tests with the TRX-first protocol ready (~60-90 min); run the full non-Docker sweep + verify against the operator-reality canary if you touched the chain factory or projection path (~10 min); write the DECISIONS entry while the slice is hot (~30 min); commit + push. The HANDOFF letter PREPENDS a new mid-chapter letter at the top.

If a slice exceeds session capacity, stop at a clean boundary (binder shipped + tests passing, wiring deferred to next session; OR binder + wiring shipped, tests deferred — but **never** code without tests). Write a brief mid-slice handoff letter (~10 lines) at HANDOFF.md top naming the partial state so the next agent can resume cleanly.

## Open questions for chapter close

When C.6 ships, the chapter-close ritual runs (8 items per CLAUDE.md). Specifically for Chapter C close, you'll want to:

1. Re-verify Active deferrals (Faker still trigger-met but stays deferred per operator direction; LiveOssysConnection still blocked on corp-network access; the new Chapter-C-deferred items if any).
2. Promote any new L3 axioms surfaced by Chapter C (likely L3-X13 for the `Policy` axes' config-binding totality property — every Policy axis the operator configures has a binder + a typed runtime value; the empty-Policy + populated-Policy skeleton-purity property test verifies).
3. Run the V1-input-envelope walk — Chapter C is config-axis work, not V1↔V2 translation, so the envelope walk applies trivially; the carbon-copy verification continues from chapter B.4.
4. Per-axis-stakes evaluation against V2_DRIVER — Chapter C primarily advances the **Operator-Intent / Pillar 9** axes; verify the tightening / insertion / selection / emission-folders axes' progress against the table.
5. Update CLAUDE.md to point at any new patterns Chapter C established.

## Test baseline + branch state

1793/1793 non-Docker passing; 0 warnings under `TreatWarningsAsErrors=true`; operator-reality canary green at the tuned 150-table × 6.25k-row floor (~5s warm); end-to-end CLI smoke green (`dotnet projection.dll full-export --config ...` produces 4 artifacts; outcome=succeeded; 74 aggregate entries; NDJSON envelopes conform to §3 verbatim). Stay on `claude/review-handoff-docs-M5RVa`; PR #552 tracks it. Each slice = one commit + one DECISIONS entry. Mid-chapter handoff letters PREPEND at HANDOFF.md top.

Hold the spine. Chapter C is mostly mechanical wiring against a substrate that already exists. The binder pattern + the chain factory + the operator-supplied-ref resolution discipline + the Global-MutableState xUnit collection are the substrate every remaining slice consumes. Each slice earned, one at a time.

— The slice-C.1 architect.

---

# Handoff letter — 2026-05-20 (chapter B.4 CLOSES) — Phase B *structural* exit gate green; full-export CLI + LogSink + actionable diagnostics ship; functional-equivalence arm awaits LiveOssysConnection

---

## 📍 Next-agent orientation — DO THIS FIRST

> **You're picking up V2 after chapter B.4 closed.** All 8 slices shipped; the chapter-close ritual ran; Phase B's *structural* exit gate is green. The functional-equivalence arm waits on `LiveOssysConnection` (operator's V1 corporate-network HEAD; named blocker).
>
> **Read these, in this order (~30 min):**
>
> 1. **This letter** (the latest, at the top) — 5 min. Names what shipped + what's load-bearing + what's deferred + the chapter's open questions.
> 2. **`CHAPTER_B_4_CLOSE.md`** — 10 min. Primary close artifact. Substantive contributions + disciplines codified + chapter-close ritual findings.
> 3. **`CLAUDE.md` operating disciplines table** — 5 min. Two new entries from chapter B.4 (LogSink hand-rolled per §15.2; canary volume-reduction protocol).
> 4. **`DECISIONS Active deferrals — index`** at top of `DECISIONS.md` — 5 min. Faker stays trigger-met-awaiting-promotion; new deferrals (Spectre TtyRenderer micro-chapter; data-twin micro-chapter; LiveOssysConnection cluster) carry named triggers.
> 5. **`V2_DRIVER.md` per-axis stakes table** — 5 min. Confirm where chapter B.4 work landed (Operational-Diagnostics axis L3-X11 + L3-X12 promoted to Bucket A; Schema + Data axes' structural arm green; Identity axis unchanged).
>
> **Then orient on the next chapter** by reading the "Open questions for the next chapter's opening" section at the bottom of `CHAPTER_B_4_CLOSE.md`. Pick ONE to bring forward to the principal-PO as the chapter-open conversation. The natural sequencing is **Chapter C** (operator-config cash-out for the dormant Pipeline.Config sections; 6 slices per the strategic-exploration framing) — Phase B's structural arm is closed but Chapter C lights the operator-facing config surface that lets operators wire tightening, emission, insertion, user-matching axes.
>
> **Branch protocol.** Chapter B.4 worked on `claude/review-handoff-docs-M5RVa` (descended from `claude/chapter-b4-opening-vGe7J` merge). If opening Chapter C, the V2 convention is a new branch (`claude/chapter-c-<slug>`). Confirm with the principal-PO before opening — chapter-open decisions are theirs.

---

## Chapter B.4 summary (8 slices, all DONE)

| Slice | Cash-out | Status |
|---|---|---|
| 1 — `logging-format-contract` | `docs/logging-format.md` ships the V2 logging contract: §3 envelope, §4 levels, §5 sink discipline, §6 categories, §7 codes, §8 classifications, §9 payloads, §10 runSummary, §11 rollup, §12 suggestedConfig, §13 antipatterns, §14 sink, §15 CLI library recommendations. Proposes L3-X11 + L3-X12. | DONE |
| 2 — `capture-retirement` | Retires 5 `capture*` SQL surfaces from LiveProfiler (dead post-slice-6b); 7 tests reshape from SQL-direct to cache-derivation assertions. | DONE |
| 3 — `composite-pk-fk` | Resolved out-of-scope at slice open — principal-PO confirmed composite-PK targets aren't an OS use case; slice-1 `AmbiguousMapping` outcome stands. Documentation-only. | DONE |
| 4 — `module-filter-port` | `ModuleFilter` carbon-copied from V1 to `Projection.Core/ModuleFilter.fs`; ~330 LOC F# consolidating V1's three donor files; 30 tests pass. | DONE |
| 5 — `metadata-contract-overrides` | V1's `MetadataContractOverrides` SQL-extraction column-relaxation surface carbon-copied to `Projection.Adapters.OssysSql/MetadataContractOverrides.fs`; ~290 LOC F#; 25 tests pass. Mechanism shipped; wiring deferred to a follow-up chapter. | DONE |
| 6 — `actionable-diagnostics` | `SuggestedConfig` typed record + `DiagnosticEntry.SuggestedConfig` field; `ActionableDiagnostics.organize` severity-sort + axis-cluster (no occlusion); 28 tests pass. Reshape mid-slice dropped the initial cluster-cap design per principal-operator pushback — "actionability = enrichment + presentation, NOT occlusion." | DONE |
| 6.5 — `logsink-rollup` | `Projection.Pipeline/LogSink.fs` hand-rolled per §15.2; ULID runId; NDJSON envelope to stderr via `Utf8JsonWriter`; §11 roll-up aggregator built ONCE during emission; terminal `summary.runComplete` carrying aggregates + Bench.Stats; 27 property + example tests pass. | DONE |
| 7 — `full-export-cli` | `projection full-export --config <path> [--output <dir>] [--verbose]` CLI subcommand; Argu per §15.2; wraps `Compose.runWithConfig` + slice-6 actionable enrichment + slice-6.5 LogSink emission; 10 integration tests pass; end-to-end CLI smoke green. | DONE |

Plus one hygiene commit during the chapter: **canary volume reduction** (`GenerateSpec.operatorReality` 300 tables × 50k rows → 150 tables × 6.25k rows; wall ~10-12s → ~5s; baseline-canary.json re-recorded; per `DECISIONS 2026-05-20 (canary volume reduction)`).

## What's load-bearing after chapter B.4

**Operator-facing surface (the structural commitment):**
- `projection full-export --config <path>` is the operator-touchable CLI per `V2_PRODUCTION_CUTOVER §7.5`.
- NDJSON event stream to stderr per `docs/logging-format.md` §3-§15. Operators pipe `2>events.log` or `2>&1 | jq` and get a machine-parseable stream.
- `summary.runComplete` is the terminal event on every exit path — operator's scrollback target carrying outcome / stages / artifacts / eventCounts / suggestedConfigEdits / aggregates.

**Emission substrate:**
- `Projection.Pipeline/LogSink.fs` — hand-rolled per §15.2 (no `Microsoft.Extensions.Logging`, no `Serilog`); `System.Text.Json.Utf8JsonWriter` serialization; ULID runIds; lock-protected per-process state; §11 rollup built ONCE during emission per the Big-O constraint.
- `Projection.Core/Diagnostics.fs:SuggestedConfig` — typed actionable-payload primitive routed through every `DiagnosticEntry`.
- `Projection.Targets.OperationalDiagnostics/ActionableDiagnostics.fs` — severity-sort + axis-cluster pure-DataIntent enrichment over emit-bound diagnostics.

**V1 inheritance (carbon-copies under V2 self-containment discipline):**
- `Projection.Core/ModuleFilter.fs` — V1's three donor files consolidated; pillar 9 classification: `OperatorIntent of Selection`.
- `Projection.Adapters.OssysSql/MetadataContractOverrides.fs` — V1's column-relaxation surface; mechanism shipped; wiring lands when consumer demand surfaces.

**Verifiability triangle:**
- **L3-X11** (structured event-stream conformance) and **L3-X12** (actionable events carry suggestedConfig) promoted to Bucket A at chapter close. Tier 1 + Tier 2 respectively. Verifiability: `LogSinkTests` (27) + `FullExportCliTests` (10) + `ActionableDiagnosticsTests` (28).

## What's deferred (Active deferrals — chapter B.4 status)

Verify each at the next chapter close per the ritual. See `DECISIONS.md` Active deferrals index for the canonical list with trigger conditions.

- **Faker emitter promotion** — trigger STRUCTURALLY MET since chapter B.3 close. Chapter B.4 did NOT promote (cutover-window priority routed structural work to the CLI surface). Re-evaluate at chapter B.5 / Chapter C open under concrete consumer demand.
- **`LiveOssysConnection` variant + cluster** (multi-env + UAT-users + axis 10 user reflow + axis 14 extraction knobs) — trigger: operator's V1 corporate-network HEAD becomes accessible. Lights the functional-equivalence arm of Phase B's exit gate.
- **Standalone `projection extract` + `projection profile` subcommands** — dropped at chapter-mid rescope; reserved code paths in §6 event categories still fire from `full-export`'s orchestration. Re-open if operator workflow demands them.
- **`data-twin` CLI verb micro-chapter** — wraps existing `DockerImageEmitter` (chapter 3.x); surfaces when dev-team dockerized-replica workflow demands.
- **Spectre.Console `TtyRenderer` + dual-channel `--json-out` routing micro-chapter** — per §15.3. Trigger: operator reports NDJSON-only stderr as unfriendly for interactive runs.
- **Static-seed parent-handling behavior** — dispersed from the struck `DynamicDataSection`; surfaces under concrete operator demand as an emitter `Options` parameter.
- **CSV adapter for `ManualOverride` (`UserMapLoader`)** — Chapter 4.2 deferral; surfaces under concrete operator workflow demand.
- **Cluster A7 row 34** (Polly transient retry for cloud OSSYS) — ★ CUTOVER-CRITICAL per V1_PARITY_MATRIX. Not blocked on corp-network access; can ship independently with `MockSqlConnection`-driven tests. Recommended candidate for an early Chapter C slice OR a separate hygiene sprint.

## Phase B exit gate — gate status after chapter B.4 close

Per `V2_PRODUCTION_CUTOVER §8.2`, Phase B's exit criterion has TWO arms; chapter B.4 deliberately closes only the **structural** arm:

| Exit criterion | Status post-chapter-B.4 |
|---|---|
| L3 catalog reaches target bucket | ✅ DONE — L3-X11 + L3-X12 promoted to Bucket A |
| V2 `full-export` CLI subcommand exists | ✅ DONE — slice 7 |
| V2 emits SSDT + JSON + Distributions + actionable diagnostics from config | ✅ DONE — slice 7 (via slice 6 actionable enrichment) |
| V2 logging-format contract documented | ✅ DONE — slice 1 |
| V2 emits conforming events end-to-end | ✅ DONE — slice 7 (consuming slice 6.5 LogSink) |
| **Structural arm CLOSED.** | **✅ at chapter B.4.** |
| | |
| V2 `full-export` runs against live OSSYS and produces functionally-equivalent `osm_model.json` to V1 | ⏳ WAITS on `LiveOssysConnection` |
| V2 profile probes produce functionally-equivalent Profile data | ⏳ WAITS on `LiveOssysConnection` |
| ≥1 full end-to-end production dry-run | ⏳ WAITS on operator scheduling |
| **Functional-equivalence arm OPEN.** | **⏳ waits on corp-network access path.** |

## Test baseline at chapter close

- **1779/1779 non-Docker** passing (was 1695 at chapter B.3 close; +84 from chapter B.4 work: ModuleFilter 30 + MetadataContractOverrides 25 + ActionableDiagnostics 28 + Catalog smart-constructor lift adjustments + LogSinkTests 27 + FullExportCliTests 10 — minus reshapes).
- **0 build warnings** under `TreatWarningsAsErrors=true`.
- **Operator-reality canary GREEN** at the new tuned floor (150 tables × 6.25k rows; ~5s warm; 230 labels in `bench/baseline-canary.json`).
- **End-to-end CLI smoke green** — `dotnet projection.dll full-export --config /tmp/fe-config.json` produces 4 artifacts; NDJSON envelopes parse cleanly; `summary.runComplete` carries outcome=succeeded + 74 aggregate entries.

## Best practices the chapter taught (carry forward)

- **Contract IS the spec; tests verify the contract.** Every property test in `LogSinkTests` cites a contract section (`§3 envelope`, `§11 rollup`, `§5 sink`). The test file is itself a contract-conformance checker — a future agent extending the contract adds tests by matching contract sections to test file sections.
- **"Actionability" means enrichment + presentation, NOT occlusion.** The slice-6 reshape lesson per `DECISIONS 2026-05-20 (slice B.4.6 reshape)`: source defects (NULLs in NOT NULL columns; orphaned FKs; duplicate unique candidates) are first-class signal; the diagnostic-emit layer surfaces them faithfully without curated suppression. Per-finding-type emission gates live at the strategy layer (e.g., `NullabilityTighteningConfig.NullBudget`), not at the emit boundary. Chapter C slice C.1 wires operator config to those existing knobs.
- **Big-O constraint cashed at design time.** Slice 6.5's §11 aggregator builds the dictionary ONCE during emission per the contract Big-O constraint — alternative (scan event list at runComplete) would be asymptotically equivalent but constant-factor worse and conceptually slipperier. Structural enforcement matches the contract verbatim.
- **Cost-driver identification is empirical, not assumed.** The canary volume-reduction tuning showed the operator's "reduce row volume by 3/4" framing assumed the wrong cost driver — empirical wall-time measurement showed deploy + reflection (proportional to table count) dominated. Surfacing the empirical finding before re-baselining was the right honest move; the user then redirected to the table-count lever which delivered the additional reduction.
- **Argu per §15.2 sets the F# CLI pattern for Chapter C.** `FullExportArg` closed-DU is the template Chapter C extends with additional subcommands. The existing `emit` / `deploy` / `canary` subcommands stay on raw argv during the transition (no breakage; no Chapter C scope creep into chapter B.4).

## Open questions for the next chapter's opening

Three open questions surfaced at chapter B.4 close; each is a candidate chapter-open conversation:

1. **Chapter C scope** — the 6-slice plan per `DECISIONS 2026-05-19 (chapter B.4 mid-chapter strategic exploration)` wires operator config to (a) tightening axis (C.1 priority slice; cashes the slice-6 reshape lesson via `Policy.TighteningPolicy` + `TighteningOverride`); (b) special-circumstances axis; (c) emission-folders axis; (d) tag-groups-as-closed-DU axis; (e) Argu CLI consolidation (migrate existing emit/deploy/canary to Argu); (f) verbosity flags (`--verbose` / `--debug` per §4). Which order? Which cuts at chapter open per chapter-mid rescope precedent?

2. **Faker emitter promotion** — trigger structurally met since chapter B.3. Cutover-window priority routed chapter B.4 to CLI; chapter B.5 could be the Faker promotion chapter. Or stay deferred until concrete consumer demand surfaces. Principal-PO call.

3. **Cluster A7 row 34 (Polly transient retry)** — the V1_PARITY_MATRIX's one explicit ★ CUTOVER-CRITICAL row that's NOT blocked on corp-network access. Can ship as a small focused slice with `MockSqlConnection`-driven tests. Sequence ahead of Chapter C, or interleave with C.1?

## Reading order for the next agent

1. **This letter** (~5 minutes).
2. **`CHAPTER_B_4_CLOSE.md`** — chapter synthesis + disciplines codified.
3. **`CHAPTER_B_4_OPEN.md`** — the 8-slice plan; all marked DONE; chapter-close-ritual paragraph at the bottom names the 8 items the close ran.
4. **`DECISIONS 2026-05-20 (slice B.4.7 — full-export CLI)`** + the slice-6.5 + canary-tuning entries — substantive contributions.
5. **`docs/logging-format.md`** — the contract; now operative end-to-end.
6. **`V1_PARITY_MATRIX.md` Cluster A7 row 34** — the cutover-critical row not blocked on corp-network.
7. **`AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md §3.4`** — L3-X11 + L3-X12 newly promoted; §10.4 coverage matrix updated.

Chapter B.4 is the substantive contribution: V2 has an operator-touchable CLI subcommand emitting machine-parseable structured events end-to-end; the actionable-diagnostics enrichment routes config-edit suggestions to operators on every applicable finding; the structural arm of Phase B's exit gate closed on schedule; the functional-equivalence arm waits on its named blocker.

Hold the spine. The chapter compounded: slice 1 set the contract; slices 4 + 5 ported V1 mechanisms; slice 6 enriched artifacts; slice 6.5 built the emission substrate; slice 7 wove them into the CLI. Each slice was earned.

— The chapter B.4 architect (chapter close).
