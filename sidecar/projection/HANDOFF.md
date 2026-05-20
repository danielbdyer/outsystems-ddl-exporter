# Handoff letter — 2026-05-20 (Chapter C CLOSED; phase A1 operator-config wiring sweep complete)

To the next agent.

You're picking up V2 with **Chapter C closed**. All six operator-overlay axes named at chapter B.4's mid-chapter strategic exploration are wired through `Compose.runWithConfig`: tightening (C.1), special-circumstances allowlists (C.2), emission-folders (C.3), tag-groups (C.4), insertion semantics (C.5), and verbosity + per-category mute (C.6). The chapter-close ritual ran in full: `CHAPTER_C_CLOSE.md` synthesis published, prior chapter letters archived at `HANDOFF_CHAPTER_C.md`, Active deferrals scanned clean, no CLAUDE.md / README.md drift. **Test baseline: 1871/1871 non-Docker passing; 0 warnings under `TreatWarningsAsErrors=true`.**

The natural next question is **what chapter opens next.** Five candidates surfaced at chapter close (see `CHAPTER_C_CLOSE.md` §"Open questions for the next chapter's opening"). The principal-PO has the call. This letter sets you up to bring the chapter-open conversation to the principal-PO with the substantive context already in hand.

## What's load-bearing across chapter C's contribution

**The Pipeline-as-overlay-realization-layer architectural pattern.** Across the six slices a single architectural seam recurred:
- Operator-supplied textual config (`Config.fs` section)
- → Dedicated binder in Pipeline (`<Axis>Binding.fromConfig`) resolving textual refs against the loaded catalog + validating shape-level invariants
- → Typed runtime overlay (`<Axis>` record / DU in Pipeline)
- → Applied at the Pipeline-layer realization boundary (chain filter / bundle rewrite / policy aggregation)

The Core (`Projection.Core`) carries the DataIntent kernel; the Pipeline carries the OperatorIntent realization. Future slices touching operator-overlay axes — insertion-consumer wiring, per-emitter group filtering, additional `TransformGroup` variants — should mirror this seam.

**The "verify the architect's named consumer layer against the substrate" discipline.** Codified at N=2 (C.3 + C.4 both found the architect's HANDOFF recommendation wrong-by-one-layer). The recipe is **read the substrate end-to-end before committing.** When a future architect's letter names a consumer-shape, validate it by walking the substrate from the relevant boundary IN both directions before committing to the recipe. The recommended layer might be one layer up (C.4: Pipeline tag map vs Core registry field) or one layer down (C.3: typed `ArtifactByKind<SsdtFile>` vs post-compose `Map<string, string>`).

**The annotate-don't-suppress discipline operationalized.** Every operator-overlay axis preserves the underlying structural value AND annotates the operator intent via metadata. C.2 stamps `Metadata.acceptedVia` on allowlisted diagnostics; C.3 preserves basename + smart-constructor invariant on rewrite; C.4's `passTags` map is a side annotation, not a structural mutation. Carries forward to any operator-overlay slice where source data could be occluded.

**The "wiring-without-downstream-consumer is a valid slice shape" discipline.** C.5 ships the `Policy.Insertion` binder + threading through the `Policy` record but no pass/emitter consumes it yet — the operator-facing surface lands so hand-edited configs produce no surprises; consumer wiring follows under concrete operator-pull pressure. Pairs with IR-grows-under-evidence; counters the "wait for the consumer" alternative that would delay operator-facing surfaces.

## Five candidate chapter-open conversations (read these before bringing options to the PO)

(1) **C.5 downstream consumer wiring as chapter D scope.** `Policy.Insertion` is wired but no pass/emitter reads it. The natural consumer is `DataEmissionComposer.dispatch` (chapter 4.1.B slice η). The composer today reads `EmissionPolicy.DataComposition` (`AllRemaining | AllExceptStatic | AllData`) which is structurally distinct from `InsertionPolicy` (`SchemaOnly | InsertNew | Merge | TruncateAndInsert`). The wiring shape: a per-emitter execution-mode resolver consults `Policy.Insertion` to choose between INSERT vs MERGE vs TRUNCATE+INSERT at MigrationDependencies + Bootstrap + StaticSeeds emission time. Estimate: ~1 week focused slice. **Most concrete; lowest risk; ships the chapter-C surface end-to-end.**

(2) **Faker emitter promotion (trigger structurally met since chapter B.3).** Three new evidence types shipped at slice B.3.8 + slice B.3.5's StatisticalMoments lift; the deferred-trigger fires structurally. Decision pending operator-pull at chapter B.4 / chapter C close — **deferred through both.** Principal-PO call on whether to open Chapter D as Faker promotion or hold longer. Estimate: 2-3 weeks (synthetic-data Π carbon-copy from V1 + tests + canary integration).

(3) **L3 axiom promotion cycle (verifiability-triangle audit refresh).** Two candidates from chapter C's pattern repetition: **L3-CC-AcceptanceAnnotation** (every operator-visible structural finding with an addressable acceptance path carries the annotation; the finding remains visible) and **L3-CC-ApplyLayerLocality** (operator overlays apply at the layer carrying the typed identity the override is keyed by). Promoting requires a verifiability-triangle audit dispatch (5 agents per `DECISIONS 2026-05-12 — Verifiability-triangle audit methodology`). Estimate: ~3-5 days for the audit + ~1 week for codification + per-axiom property tests.

(4) **`LiveOssysConnection` cluster.** Blocked on operator corp-network access. When access opens, the cluster (live OSSYS path + multi-env + UAT-users + user-reflow strategy + extraction-time knobs) lands as a follow-up chapter and closes Phase B's functional-equivalence arm (per `CHAPTER_B_4_CLOSE.md` §"Phase B exit gate status"). Estimate: 4-6 weeks; not chapter-D candidate unless operator surfaces access pre-cutover.

(5) **TransformGroup DU expansion + per-emitter filtering.** Today's C.4 chain-filter only excludes PASSES whose tags intersect disabled groups. If operator-pull surfaces for "toggle MigrationDependencies emission off without using `EmissionPolicy.EmitData = false`," that's a new `TransformGroup` variant + new emitter-side filtering mechanism. Estimate: ~3-5 days focused slice (the structural seam already exists; the work is the closed-DU expansion + the emitter-side filter at the chain boundary).

## Reading order (~45 min)

1. **This letter** — 5-min context for the chapter-D opening conversation.
2. **`CHAPTER_C_CLOSE.md` (~150 lines)** — full close synthesis: substantive contributions, disciplines codified, the chapter-close ritual's 8 items, test baseline, what's deferred, the five candidate open conversations.
3. **`HANDOFF_CHAPTER_C.md` (~410 lines)** — the chapter's per-slice architect letters preserved in chronological order (C.4-C.5-C.6 at top; C.3; C.2; C.1; chapter-B.4 close letter at bottom). Read for the per-slice architect narratives — these are where the substrate-verification discipline got codified slice-by-slice + where each architectural decision was named.
4. **The four key files chapter C added at the Pipeline layer:**
   - `src/Projection.Pipeline/SpecialCircumstancesBinding.fs` (~165 LOC) — the binder template
   - `src/Projection.Pipeline/SpecialCircumstancesDiagnostics.fs` (~140 LOC) — the post-chain-scan pattern
   - `src/Projection.Pipeline/EmissionFoldersBinding.fs` (~190 LOC) — the structured folder-validation pattern + binder
   - `src/Projection.Pipeline/TransformGroupsBinding.fs` (~125 LOC) — the Pipeline-layer tag-map pattern + closed-DU binder
5. **`DECISIONS 2026-05-20 (slices C.4 + C.5 + C.6)` consolidated entry + the per-slice entries above it** — the chapter's substantive resolved questions + discipline codifications.

## Disciplines internalized by the time you finish chapter C

Carried-forward from prior chapters (still load-bearing):
- HANDOFF.md is append-only within a chapter; rotates at close (you're the beneficiary of this discipline — `HANDOFF_CHAPTER_C.md` carries the chapter's letter history).
- "Handoff message" = forward-looking letter, not backward-looking status report (this letter addresses you in the second person; the structure is "what you need to know to do your work," not "what we did").
- Test-failure capture protocol — TRX-first when `dotnet test` reports `Failed: N` (used in this chapter when C.4's initial passTags map had stale names; the TRX surfaced the actual mismatched names in seconds).
- Closed-DU expansion empirical-test discipline — applied to C.4's `TransformGroup` DU (2 variants reflect today's evidence; no speculative pre-population).
- IR-grows-under-evidence — applied to C.5's wiring-without-consumer scope; applied to C.4's minimal DU seed.

New from chapter C (read the relevant DECISIONS entries):
- **Pillar 9 separation at the Pipeline boundary**: operator-overlay axes' typed runtime values live in Pipeline; Core stays DataIntent-pure.
- **Verify the architect's named layer against the substrate (N=2)**: codified at slice-C.3 + slice-C.4-C.6 DECISIONS entries.
- **Structured-error sub-codes for taxonomy**: dot-suffix codes when one validation step has multiple distinct rule violations.
- **Wiring-without-downstream-consumer as a valid slice shape**: codified at slice-C.4-C.6 DECISIONS entry.
- **Mute-before-accumulator-update ordering invariant**: codified at slice-C.4-C.6 DECISIONS entry.

## Pitfalls chapter C hit that you can avoid

- **Don't assume `setVerbose true` semantics survive a refactor.** The existing `LogSinkTests` test "trace and debug surface when setVerbose true" expects all-on behavior. C.6's `Verbosity` DU split could have broken this (Verbose was conceptually below Debug); keeping the back-compat shim `setVerbose true → Verbosity.Debug` preserved the test. Any future LogSink refactor that touches the `setVerbose`-equivalent API needs the same back-compat check.
- **`Map.singleton` doesn't exist in F#.** Use `Map.ofList [ k, v ]`. C.4 tests hit this once; the build error pointed it out, but a fresh agent might assume parity with `Set.singleton`. Defensive pattern: when reaching for a `Map`-construction primitive, double-check `Map.<primitive>` exists before committing.
- **Closed-DU case names can collide across DUs in the same module.** `Tightening` collides between `OverlayAxis` and `TransformGroup`; `Debug` collides between `Level` and `Verbosity`. Solution: `[<RequireQualifiedAccess>]` on the secondary DU. Worth checking when adding new DUs alongside existing ones.
- **Don't write tests that depend on Bench/LogSink mutable state without `[<Xunit.Collection("Global-MutableState")>]`.** The collection serializes the tests; without it, parallel xUnit execution can intermix two tests' LogSink writes. C.6's `LogSinkVerbosityTests` ships with the collection attribute; mirror for any future LogSink test.

Hold the spine. Phase A1 (operator-config wiring) closes with chapter C; phase A2's shape is the next-chapter conversation. Bring the five candidate open conversations to the principal-PO with the substantive context already in hand.

— The chapter C architect (chapter close).
