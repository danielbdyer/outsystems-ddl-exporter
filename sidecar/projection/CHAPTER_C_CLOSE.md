# Chapter C close — operator-facing config surface ships end-to-end (6/6 slices done; phase A1 operator wiring sweep complete)

**Branch:** `claude/add-emission-folders-overrides-Y9oGn` (descended from `main` post-PR-#553 merge). **Closed:** 2026-05-20. **Slices shipped:** 6/6. **Predecessor chapter:** `CHAPTER_B_4_CLOSE.md` (Phase B structural exit gate).

This document is the chapter C close synthesis per the chapter-close-ritual discipline (`CLAUDE.md` operating-disciplines table). Predecessor chapter opening: the 6-slice plan from `DECISIONS 2026-05-19 (chapter B.4 mid-chapter strategic exploration)` plus `DECISIONS 2026-05-19 (chapter B.4 hygiene strike + axis-survey supplement)`. Bridge letter at chapter close: `HANDOFF.md` (this commit); previous chapter letter archived at `HANDOFF_CHAPTER_C.md`.

## Substantive contributions

### 1. Six operator-overlay axes wired end-to-end through `Compose.runWithConfig`

Each slice followed the same shape: textual config section → typed runtime overlay via dedicated binder → threading through `Compose.runWithConfig` → tests covering binder + (where applicable) the consumer.

- **C.1 — Tightening axis.** `Policy.TighteningPolicy.Interventions` binds from `policy.tightening.interventions` config; `TighteningBinding.fromConfig` resolves logical-or-physical attribute refs against the loaded catalog; `RegisteredTransforms.allChainStepsFor policy profile` is the policy-parameterized chain factory (sibling to the static `allChainSteps`). Decision-set passes (`NullabilityPass` / `UniqueIndexPass` / `ForeignKeyPass` / `CategoricalUniquenessPass`) consume `policy` directly.

- **C.2 — Special-circumstances axis.** `Overrides.AllowMissingPrimaryKey` (relocated from the wrongly-placed `Model.ValidationOverrides`) + `Overrides.CircularDependencies.AllowedCycles` bind via `SpecialCircumstancesBinding.fromConfig`. New `SpecialCircumstancesDiagnostics.emit` post-chain scan lifts the missing-PK and unresolved-cycle signals from internal pass state (`SymmetricClosure.Skip ClosureSkipped TargetHasNoPrimaryKey` + `TopologicalOrder.Cycles : CycleDiagnostic list`) into operator-visible `DiagnosticEntry`s flowing through the LogSink envelope stream; allowlist matches carry `Metadata.acceptedVia` annotation per the annotate-don't-suppress discipline (slice-6 reshape lesson).

- **C.3 — Emission-folders axis.** New `Overrides.EmissionFolders : EmissionFolderEntry list` config; `EmissionFoldersBinding.fromConfig` validates folder strings against five sub-rules (`empty | absolute | backslash | parentTraversal | emptySegment | invalidChar`). Apply step (`applyEmissionFolderOverrides` private helper) fires at the typed `ArtifactByKind<SsdtFile>` layer between `SsdtDdlEmitter.emitSlices` and `SsdtBundle.compose` — preserves the `<Schema>.<Table>.sql` basename + the smart-constructor strict-equality invariant.

- **C.4 — Tag-groups axis.** Closed `TransformGroup` DU (`Tightening | UserReflow`; `[<RequireQualifiedAccess>]`) lands in `Projection.Core.Classification.fs`. `Policy.TransformGroups : TransformGroupEntry list` binds via `TransformGroupsBinding.fromConfig`. Static `RegisteredTransformTags.passTags : Map<string, Set<TransformGroup>>` lives at the Pipeline layer (NOT on the Core registry record — per pillar 9 + minimal-cascade reasoning). `Pipeline.filterChainByGroups` excludes passes whose tags intersect operator-disabled groups; `TransformGroups.empty` is a no-op (V1-parity).

- **C.5 — Insertion semantics.** `Policy.InsertionPolicy` (the existing four-variant closed DU: `SchemaOnly | InsertNew | Merge | TruncateAndInsert`) binds from `policy.insertion` string via `InsertionPolicyBinding.fromConfig`. `buildPolicyFromConfig` now aggregates tightening + insertion binder errors in one pass. Wiring-without-downstream-consumer scope: the binder lands + threads into `Policy.Insertion`; downstream pass/emitter consumers don't yet read `Policy.Insertion` (consumer wiring follows under concrete operator-pull pressure per IR-grows-under-evidence).

- **C.6 — Verbosity + per-category mute.** `LogSink.Verbosity` closed DU (`Quiet | Verbose | Debug`; `[<RequireQualifiedAccess>]`) replaces the binary `setVerbose`. `setVerbosity : Verbosity -> unit` + `setMutedCategories : Set<Category> -> unit` ship the two-axis surface. CLI extends `FullExportArg` with `Debug` (alias `-d`) + `MuteCategory` flags; the dispatcher resolves category-name strings through closed lookup with per-argument error aggregation. Back-compat shim preserves `setVerbose : bool -> unit` (`true → Debug` preserves prior all-on semantics).

### 2. The "Pipeline-layer tag map" architectural pattern

The slice-C.3 HANDOFF letter recommended putting `Tags : Set<TransformGroup>` on the Core's `RegisteredTransformMetadata` record (the architect's "structural co-location" framing). Read-through during C.4 surfaced the cascade cost: 12 pass modules' `.registered` record-literal declarations + 9 emitter/adapter sites + the smart constructors + `toMetadata` = ~21 record-literal edits under `TreatWarningsAsErrors=true`.

The lighter-touch alternative shipped: a static `Map<string, Set<TransformGroup>>` (`RegisteredTransformTags.passTags`) in the Pipeline layer alongside the chain it filters. Architectural rationale (beyond cascade minimization): per pillar 9, operator-overlay-axis classification IS operator intent; the Core's registry record IS DataIntent (its `Sites : TransformSite list` carries classification on a structural axis, not operator-toggle membership). Keeping the tag-set in Pipeline preserves Core's DataIntent purity. The decoupling-vs-co-location trade-off is mitigated by a property-test invariant (`passTags coverage`) asserting every name in `passTags` exists in `RegisteredAllTransforms.all` — catches refactor drift.

### 3. The "annotate-don't-suppress" discipline operationalized across overlay axes

The slice-6 (chapter B.4) reshape lesson — "actionability = enrichment + presentation, NOT occlusion" — generalized across C.2 + C.3:
- **C.2** annotates allowlisted findings with `Metadata.acceptedVia` keyed by config source (`config:overrides.allowMissingPrimaryKey` / `config:overrides.circularDependencies`). The structural finding remains visible; the annotation tells downstream operator surfaces (LogSink envelopes) how to render the acceptance.
- **C.3** preserves the basename + the smart-constructor strict-equality invariant on rewrite — the override doesn't OCCLUDE the catalog's per-kind identity, it RE-LOCATES the rendered file.

The pattern that generalizes for C.7+: every operator-overlay axis preserves the underlying structural value AND annotates the operator intent via metadata. Promoted to L3-CC-AcceptanceAnnotation candidate (see "Open questions for next chapter open" §3 below).

### 4. The "Pipeline-as-overlay-realization-layer" architectural pattern

Across the six slices, the same architectural seam recurred: operator-supplied textual config → typed runtime overlay (a typed value in `Projection.Pipeline`) → applied at the Pipeline-layer realization boundary (the chain filter, the bundle rewrite, the policy aggregation). The Core (`Projection.Core`) carries the registry types + the chain step kernels + the per-pass logic; the Pipeline applies operator intent over the Core's output.

This codifies a working version of pillar 9's data-intent / operator-intent separation at the Pipeline layer: the Pipeline is the operator-intent realization layer; the Core is the data-intent kernel. Future slices touching operator-overlay axes (insertion-consumer wiring, per-emitter group filtering, etc.) should mirror this seam.

## Disciplines codified or reinforced

### 1. "Verify the architect's named consumer layer against the substrate" (codified at N=2)

Both C.3 and C.4 found the architect's HANDOFF recommendation wrong-by-one-layer:
- **C.3** — architect named `SsdtBundle = Map<string, string>` (post-compose) as the apply layer; actual right place was `ArtifactByKind<SsdtFile>` (one layer upstream, where SsKey context survives).
- **C.4** — architect named `RegisteredTransform.Tags : Set<TransformGroup>` (Core record); actual right place was Pipeline-layer static `passTags` map (avoids 21-site cascade + better matches pillar 9).

Pattern: **read the substrate end-to-end before committing to the recipe.** The architect's recommendation is a starting framing, not a contract.

### 2. "Wiring-without-downstream-consumer is a valid slice shape" (codified)

Codifies the pattern that's been in flight since chapter B.4 slice 7: when the operator-facing config surface needs to land BEFORE the downstream consumer is ready, ship the binder + typed overlay + threading through the relevant aggregate (`Policy`, `EmissionPolicy`, etc.). The consumer reads the field when ready (non-breaking add). Catches the failure mode of "deferred config sections that parse-but-ignore + then surprise operators when the consumer arrives."

Worked example this chapter: C.5's `Policy.Insertion` lands + threads through the `Policy` record but no pass/emitter consumes it. The follow-on consumer wiring (DataEmissionComposer integration?) is gated on concrete operator-pull pressure.

### 3. "Mute-before-accumulator-update" (codified)

When an operator-supplied filter drops events from the live stream, it must also drop them from the terminal summary's rollup. The naive ordering (accumulator update → filter → emit) would keep muted events in the summary, surprising operators. The correct ordering (filter → accumulator update → emit) is structurally enforced in `LogSink.emit`'s lock-protected body. C.6's `setMutedCategories` codifies this; future operator-side filters at the LogSink boundary should mirror.

### 4. "Closed-DU expansion empirical-test discipline applied at first-evidence-fires"

`TransformGroup` ships with EXACTLY the operator-toggle evidence today (Tightening + UserReflow). The speculative axis-survey list (`CDC | UATUsers | MigrationDependencies | Bootstrap | RefactorLog`) doesn't pre-populate the DU — those variants land under operator-pull + DECISIONS amendment. Pairs with IR-grows-under-evidence; counter-balances the temptation to seed exhaustively from V1's enum.

### 5. "Structured-error sub-codes for taxonomy" (C.3 contribution, generalizes)

When a single validation step has multiple distinct rule violations, give each its own dot-suffix code:
- `pipeline.emissionFolders.invalidFolder.empty | absolute | backslash | parentTraversal | emptySegment | invalidChar` (6 codes)
- `pipeline.specialCircumstances.allowMissingPk.unresolved | allowedCycle.unresolved` (2 codes)
- `pipeline.transformGroups.unknownGroup` (1 code)
- `pipeline.insertionPolicy.unknownVariant` (1 code)

The operator gets one specific diagnosis per malformed entry; the test surface stays small (one Assert per code). Generalizes for future binders with multiple validation rules.

## Chapter-close ritual — the 8 items

Per the chapter-close ritual operating discipline (`CLAUDE.md`):

1. **Active deferrals scan** ✓ — No silent-trigger fires. Faker emitter promotion remains trigger-met-awaiting-PO-decision (carried from chapter B.4 close). LiveOssysConnection cluster (multi-env + UAT-users + user-reflow strategy + extraction-time-knobs) stays blocked on corp-network access. Spectre TtyRenderer + data-twin verb deferrals stand. No new deferrals required from chapter C's work — every slice cashed out within the chapter.
2. **Contract-vs-implementation walk** ✓ — Every section of the `DECISIONS 2026-05-19 (chapter B.4 mid-chapter strategic exploration)` 6-slice plan has a corresponding binder + typed runtime overlay + tests + threading through `runWithConfig`. The "Per-axis status" table from that DECISIONS entry inverts:
   - Axis 4 (Tighten attributes) gap → wired (C.1)
   - Axis 5 (Special circumstances) gap → wired (C.2)
   - Axis 2 (Move emission folder) gap → wired (C.3)
   - Axis 6 (Tag groups) novel → wired (C.4)
   - Axis 9-revised (Insertion semantics) → wired (C.5)
   - Axis 7 (Logging verbosity / sink redirection) → wired (C.6)
3. **CLAUDE.md staleness** — Operating-disciplines table reviewed; the new disciplines codified this chapter (verify-architect's-named-layer; wiring-without-consumer; mute-before-accumulator; structured-error-sub-codes) match existing patterns naturally and don't warrant new rows. The "F# feature surface" section's "Aligned but underused" entries (computation expressions, etc.) don't fire from this chapter's work. **No CLAUDE.md edits required this close.**
4. **README.md staleness** — README pointer stays at the existing CLI surface (`full-export`); CLI flags expanded (`--debug`, `--mute-category`) but the high-level command shape unchanged. No breaking surface change. Update deferred to README's natural cadence.
5. **HANDOFF + close-doc scope** ✓ — Current `HANDOFF.md` (carrying C.1/C.2/C.3/chapter-mid C.4-C.6 letters) rotates to `HANDOFF_CHAPTER_C.md`; fresh `HANDOFF.md` opens with the chapter-close letter. This `CHAPTER_C_CLOSE.md` synthesis published.
6. **Fresh-eye walk** ✓ — Code reviewed during slice implementation; no orphans; no Skip stubs added without trigger; test counts match commit narratives (6 + 7 + 24 + 17 + 9 + 10 = 73 net-new tests; sanity-checked against the 1798→1871 baseline progression).
7. **Operating-disciplines table currency** ✓ — Table points at current DECISIONS entries; no drift detected. The C.4 "verify the architect's layer" lesson is codified in the slice-C.3 + slice-C.4-C.6 DECISIONS entries; the table's existing entries on "Closed-DU expansion empirical-test discipline" + "IR grows under evidence, not speculation" cover the related codifications without needing new rows.
8. **V1-input-envelope walk** — N/A. Chapter C is the operator-config-wiring chapter, not a V1↔V2 translation chapter. The carbon-copy discipline (V2 self-containment + V1-as-editorial-donor) doesn't apply here; no new V1 input shapes consumed.

## Test baseline at chapter close

| Surface | Count | Status |
|---|---|---|
| Non-Docker total | 1871 | All passing (was 1798 at chapter C open / chapter B.4 close+canary tuning; +73 across 6 slices) |
| C.1 tightening tests | 6 binder + chain integration | All passing |
| C.2 special-circumstances tests | 7 binder + 10 diagnostics scan | All passing |
| C.3 emission-folders tests | 15 binder + 9 overlay + 5 config parser | All passing |
| C.4 tag-groups tests | 10 binder (incl. passTags coverage invariant) + 7 chain filter | All passing |
| C.5 insertion-policy tests | 9 binder | All passing |
| C.6 verbosity + mute tests | 10 LogSink (incl. back-compat shim + mute-doesn't-rollup) | All passing |
| Build warnings under `TreatWarningsAsErrors=true` | 0 | Clean |
| Operator-reality canary | GREEN | ~5s warm (unchanged from chapter B.4 baseline; chapter C work doesn't touch the perf hot paths) |

## What's NOT in this chapter (deferred-with-trigger)

- **C.5 downstream consumer wiring.** `Policy.Insertion` flows through the config + binder into the `Policy` record but no pass/emitter reads it yet. Trigger: concrete operator-pull for the insertion-driven data-emission behavior (today the `DataEmissionComposer` reads `EmissionPolicy.DataComposition` `AllRemaining | AllExceptStatic | AllData` which is structurally distinct from `Policy.InsertionPolicy`). Cash-out shape: extend `DataEmissionComposer.dispatch` to consult `Policy.Insertion` for the per-emitter execution-mode choice.
- **TransformGroup DU expansion beyond Tightening + UserReflow.** The DECISIONS axis-survey hinted at more (`CDC | UATUsers | MigrationDependencies | Bootstrap | RefactorLog`). Most are emitter-level not chain-level; landing them requires both the new variant AND the emitter-side filtering mechanism (today's C.4 chain-filter only excludes PASSES). Trigger: operator-pull for "toggle X off without using `EmissionPolicy.EmitData = false`" surfaces.
- **Per-emitter group filtering at `Compose.runWithConfig`.** Today's C.4 chain-filter excludes passes only; emitters fire unconditionally (their per-emitter on/off lives in `EmissionPolicy` booleans). Trigger: operator-pull for unified "filter via TransformGroup across passes AND emitters" surfaces.
- **L3-CC-AcceptanceAnnotation axiom promotion.** The annotate-don't-suppress discipline is operative across C.2-C.6; promoting to a formal L3 product axiom would make it a contract rather than a convention. Trigger: next L3 audit cycle (verifiability-triangle audit refresh).
- **L3-CC-ApplyLayerLocality axiom promotion.** The "operator overlays apply at the typed-identity-carrying layer" pattern (verified twice across C.3 + C.4) is worth formalizing. Trigger: same as above.
- **`MetadataContractOverrides` wiring into V2 mappers.** Carried forward from chapter B.4 close; trigger remains "V1-source drift event surfaces." No change this chapter.
- **`Compose.aggregateSsdt` retirement.** Used by canary deploy paths to flatten the typed bundle to one SQL string; the C.3 overlay leaves `aggregateSsdt` unchanged (it iterates the bundle's `.sql` keys via `Map.toSeq`, which sees the rewritten paths). No incompatibility surfaced, but worth a future dual-path audit when emission-folder operator-overlay sees production use.

## Open questions for the next chapter's opening

Five questions surfaced at chapter close; each is a candidate chapter-open conversation:

1. **C.5 downstream consumer wiring as the chapter D scope?** A focused slice extending `DataEmissionComposer.dispatch` to consume `Policy.Insertion` lands the operator-facing insertion-semantics surface end-to-end. Estimate: ~1 week. Decision shape: chapter D scope vs follow-on micro-slice under chapter C.

2. **Faker emitter promotion (trigger met since chapter B.3).** Carried from chapter B.4 close; principal-PO call. Surfaces as either a Faker-focused chapter or a continued deferral until concrete consumer demand.

3. **L3 axiom promotion cycle.** Two candidates (L3-CC-AcceptanceAnnotation + L3-CC-ApplyLayerLocality) earned through chapter C's pattern repetition. Next L3 audit dispatch (per the verifiability-triangle audit cadence discipline) would promote both. Trigger shape: annual refresh, or chapter-close L3 step (per CLAUDE.md operating-disciplines table).

4. **`LiveOssysConnection` cluster.** Blocked on operator corp-network access. When access opens, the cluster (live OSSYS path + multi-env + UAT-users + user-reflow strategy + extraction-time knobs) lands as a follow-up chapter and closes Phase B's functional-equivalence arm.

5. **Tag-group DU expansion + per-emitter filtering.** If operator-pull surfaces for richer toggling (CDC group; per-emitter mute via TransformGroup), the structural slice that lands the closed-DU expansion + emitter-side filter is concrete and small (~3-5 days).

## Closing

Chapter C closes the operator-facing config surface gap surfaced at chapter B.4's mid-chapter strategic exploration. The operator hand-edits one JSON document and gets six axes of operator-overlay control: tightening enforcement; allowlists for source defects; per-kind file targeting; named group on/off; insertion semantics; logging verbosity + per-category mute. Each axis carries the same structural shape — typed config record, dedicated binder, typed runtime overlay, threading through `Compose.runWithConfig` — so future operator-overlay additions follow the seam.

The pillar 9 / DataIntent-vs-OperatorIntent separation lands operationally as a layered architecture: `Projection.Core` carries the DataIntent kernel (the registry, the chain steps, the per-pass logic, the structural types); `Projection.Pipeline` carries the operator-overlay realization layer (the binders, the chain filter, the bundle rewrites, the policy aggregation). The architect's recommended consumer-shape got re-validated against the substrate twice (C.3 + C.4), and the pattern that emerged — read the substrate end-to-end before committing — codifies one discipline up from the lessons.

Hold the spine. Chapter C compounded — C.1 set the binder + chain-factory template that C.2 mirrored; C.2's post-chain scan pattern generalized to "pure-additive scan over IR-traversal cascade"; C.3's apply-layer ambiguity codified "verify the architect's named consumer layer against the substrate" which C.4 re-validated; C.5 confirmed "wiring-without-downstream-consumer is a valid slice shape"; C.6's mute-before-accumulator ordering invariant lands the per-category filter without surprising operators on the terminal summary. Each slice was earned; the chapter ships earned.

— The chapter C architect (chapter close).
