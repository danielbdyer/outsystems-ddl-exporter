# Handoff letter — 2026-05-23 (slice D.1.a CLOSED; sub-slices D.1.b + D.1.c open)

To the next agent.

You're picking up V2 mid-Chapter D. Chapter D's framing is **operator-visible emission shape**: the SSDT artifacts V2 produces should carry operator-meaningful identifiers (ubiquitous-language names like `Customer.Email`) instead of the OSSYS storage shape (`OSUSR_ABC_CUSTOMER.EMAIL`) that V2 had been emitting through chapter C. Slice D.1 is the structural slice that closes that gap; the principal-PO carved it into three sub-slices at slice open and **only the first (D.1.a) has shipped**. Your job is D.1.b — and D.1.c after that — and you're inheriting a green tree (2359 pass, 0 fail, 207 skip) plus an architecturally-clean substitution mechanism that just needs end-to-end roundtrip recovery + canary teeth.

## Where you are in the spine of the work

Read `SLICE_D_1_A.md` first (~5 min). It carves the full slice into the three sub-slices and explains why D.1.a alone doesn't deliver the end-to-end product: the substitution works (V2 emits `[dbo].[Customer]` now) but **ReadSide can't recover the original logical-vs-physical divergence** because `ReadSide.fs:640` derives `Kind.Name = Name.create table` directly from the deployed physical name. Roundtrip: deploy `[dbo].[Customer]` → ReadSide reads → `Kind.Name = "Customer"`, `Kind.Physical.Table = "Customer"`. No record survives that the original source's `Kind.Physical.Table` was `OSUSR_ABC_CUSTOMER` while its `Kind.Name` was `Customer`. This means **the operator-reality canary cannot today verify logical-name emission** — its source fixture is pure-physical, the substitution is a no-op on that fixture, and the canary passes trivially. The bite arrives at D.1.c.

## D.1.b — your next slice

**Scope.** V2 emits a `V2.LogicalName` extended property on every deployed CREATE TABLE / column carrying the pre-substitution logical name. ReadSide queries the property and hydrates `Kind.Name` / `Attribute.Name` from it (backward-compat fallback to `Name.create table` when the property is absent). End-to-end roundtrip recovery: deploy-and-read recovers the original logical-vs-physical divergence.

**Two ends of the change.**

1. **Emitter side** (`Projection.Targets.SSDT`). The SSDT emitter already invokes `sp_addextendedproperty` for kind-level / column-level / index-level metadata at `ScriptDomBuild.fs:1241-1255`. Add a new extended-property entry whose name is `V2.LogicalName` (or whatever short canonical name you prefer — choose at slice open) and whose value is the PRE-substitution logical name. This means the `LogicalTableEmission` / `LogicalColumnEmission` passes need to either (a) record what they substituted in a side channel that emission reads, OR (b) the emitter reads `Kind.Name` / `Attribute.Name` directly at emission time (the logical name is still in the catalog after substitution — only `Physical.Table` / `Column.ColumnName` got rewritten). Option (b) is cleaner and likely the right answer: emission carries `Kind.Name` into the extended property without any side-channel needed.

2. **Reader side** (`Projection.Adapters.Sql/ReadSide.fs`). Today `ReadSide.fs:640` calls `Name.create table` unconditionally. Lift to: query `sys.extended_properties` for the `V2.LogicalName` property on every read table; when present, hydrate `Kind.Name` from the property value; when absent, fall back to the existing `Name.create table` behavior (backward-compat for pre-D.1.b deployed schemas). Same lift for column-level: query for `V2.LogicalName` on every column, hydrate `Attribute.Name` when present.

**What to verify before committing the slice.** Property roundtrip: a catalog with divergent logical/physical names → V2 emit → deploy → ReadSide read → catalog whose `Kind.Name` / `Attribute.Name` match the original (NOT derived from physical). Add a new test file `LogicalNameRoundtripTests.fs` covering the property; lives in `tests/Projection.Tests/`.

**Pitfall to avoid.** The existing `Kind.Description` / `Attribute.Description` extended-property emission landed at chapter A.0' slice α as carriage-only — `IR fidelity lift (L3-S9 descriptions sub-axiom)`. Don't accidentally entangle the new `V2.LogicalName` property with the existing description channel; they're different concerns. Use a distinct extended-property name; ReadSide reads them separately; emission writes them separately.

**Pitfall the slice will surface.** `Compose.aggregateSsdt` and the manifest emission both compose paths from `Kind.Physical.*` — after D.1.a these are logical; after D.1.b they're STILL logical (D.1.b doesn't change the substitution; it adds metadata for recovery). Manifests stay logical-named; this is correct. ReadSide-recovered catalogs after D.1.b will produce identical structural emission to the pre-deploy catalog (the roundtrip becomes symmetric on `Name` and `Physical` both).

## D.1.c — the slice after D.1.b

**Scope.** Canary fixture augmented with logical-name extended properties; `PhysicalSchema` gains a `LogicalNameBinding` set; diff comparator amended to assert the triangle (`source.Kind.Name = target.Kind.Name = target.Kind.Physical.Table`) on top of existing set-differences. Perf-gate baseline re-recorded.

**Architectural sketch.** Today `PhysicalSchema` carries `Columns / ForeignKeys / Rows / RowDigests` (`PhysicalSchema.fs:134-155`). Add a fifth field: `LogicalNameBindings : Set<{ PhysicalTable: string; LogicalName: string }>`. Diff comparator extends set-difference to the fifth field. Triangle assertion lives in the canary test (`CanaryRoundTripTests.fs`'s wide-canary path), not in the comparator itself — the comparator computes the diff; the canary asserts the triangle property holds against the diff output.

**Canary fixture augmentation.** `canary-gate.sql` / `SourceSchema.realistic` add `sp_addextendedproperty` invocations carrying `V2.LogicalName` for every table/column. D.1.b's ReadSide extension queries these on readback; the canary's source catalog now has `Kind.Name = "Customer"` (from the property) and `Kind.Physical.Table = "OSUSR_ABC_CUSTOMER"` (from the deployed name) — distinct values. After V2's pipeline runs and re-emits, the target catalog has `Kind.Name = "Customer"` and `Kind.Physical.Table = "Customer"`. The triangle holds.

**Perf-gate baseline re-record.** Per `scripts/perf-gate.sh` — `PERF_GATE_RECORD=1 ./perf-gate.sh` captures N warm runs after the fixture change; commit the new `bench/baseline-canary.json`. Expected delta: small bump from the extra extended-property SQL emission (~5-10ms warm). Per `DECISIONS 2026-05-10 — Perf-gate μ+σ statistical baseline` pair the re-record with a DECISIONS amendment naming the new floor's rationale.

## Reading order (~20 min before you cut code)

1. **`SLICE_D_1_A.md`** — what shipped, what didn't, the sub-slice carve-out and why. ~5 min.
2. **`DECISIONS 2026-05-23 (slice D.1.a — logical-name emission as default)`** — the canonical decisions: substitution is operator intent; module names follow operator-visible effect not mechanism; closed-DU expansion absorbed cleanly. ~3 min.
3. **`src/Projection.Core/Passes/LogicalTableEmission.fs` + `LogicalColumnEmission.fs`** — the substitution mechanism. Both modules' docstrings explicitly call out the substitution-vs-rename distinction. Sister-passes; learn one, you know both. ~5 min.
4. **`src/Projection.Adapters.Sql/ReadSide.fs:640` and surrounding ~50 lines** — the load-bearing site for D.1.b. The current `Name.create table` IS the gap D.1.b closes. ~3 min.
5. **`src/Projection.Targets.SSDT/ScriptDomBuild.fs:1241-1255` and the call sites** — the existing extended-property emission seam. D.1.b extends here with a new property entry. ~5 min.

## Disciplines you'll need

Carried-forward from the broader codebase (still load-bearing):
- HANDOFF.md is append-only within a chapter; prepend new letters; never overwrite with Write. You're benefiting from this discipline right now (this letter prepends; the C.4-C.6 close letter survives below).
- "Handoff message" = forward-looking letter, second-person, problem-oriented. This letter addresses YOU directly with "what you need to know to do D.1.b"; the structure is forward-looking, not a backward-looking status report on D.1.a.
- Test-failure capture protocol — TRX-first when `dotnet test` reports `Failed: N`. Slice D.1.a used it once and the 10 failing tests came back classified in seconds.
- Closed-DU expansion empirical-test discipline — F# exhaustiveness errors light up only at match sites that genuinely care. Slice D.1.a widened `TransformKind` with `ColumnPhysicallyRenamed` and zero match sites needed updating (all had `_` wildcards). Apply the same discipline if D.1.b widens any closed DU.
- AxiomTests entry alignment — every new behavioral property gets an AxiomTests citation entry alongside the test file. D.1.a added `L3-Emission-Logical (slice D.1.a)`; D.1.b adds something like `L3-Emission-LogicalRoundtrip (slice D.1.b)`.

New from D.1.a (read the corresponding DECISIONS entry for full prose):
- **Substitution vs rename naming distinction.** Passes that AUTHOR new names share the `*Rename` suffix (operator supplies new target via `RenameSpec`); passes that SUBSTITUTE pre-existing catalog axes share the `*Emission` suffix. D.1.b might add `LogicalNameRecovery` (ReadSide-side recovery of the logical name from extended properties) — concept-shaped, operator-visible-effect-named, sibling-friendly with `LogicalTableEmission` / `LogicalColumnEmission`.
- **Mode parameter as toggle seam over runtime config injection.** `Enabled | Disabled` captured at registration time. D.1.b's ReadSide extension might want a similar mode for the property-lookup-vs-fallback behavior; consider the same `Mode` shape if a toggle surfaces.
- **Default-on is operator intent in 2026.** Production chain wires `Enabled`; `Disabled` is the diagnostic / V1-parity fallback. Both classified `OperatorIntent Emission`. Apply the same framing to D.1.b: ReadSide property-lookup IS the production default; falling back to `Name.create table` is backward-compat for pre-D.1.b deployed schemas, not a configuration knob.

## Pitfalls D.1.a hit that you can avoid

- **Don't carry over naming patterns from existing modules without re-applying the four-question domain-naming analysis.** D.1.a originally named the new passes `TableRenameToLogical` / `ColumnRenameToLogical` because that was the closest sibling pattern (`TableRename`'s shape). Principal-PO flagged the misnomer mid-implementation. The fix: when adding a sibling pass, articulate what the sibling REPRESENTS in the domain before adopting the existing module name's pattern. The failure mode is **misnomer-by-inheritance**.
- **Don't broaden a fixture sed when you only want to change assertions.** D.1.a's `sed -i 's/OSUSR_APPCORE_USER/User/g'` on test files initially rewrote both the fixture JSON's `physicalName` field AND the assertion strings. The fixture's purpose was to test logical/physical divergence; rewriting the fixture eliminated the divergence. Caught quickly via re-read; the fix was to restore `OSUSR_*` in fixture JSON while keeping logical names in assertions. **Use narrow sed boundaries or per-file edits when fixture-vs-assertion distinction matters.**
- **`PrimitiveType` doesn't have `String` — it has `Text`.** Tripped up a test fixture mid-D.1.a. The compiler caught it instantly via `TreatWarningsAsErrors=true`; just naming so the next agent doesn't waste cycles on the same wrong-guess.

Hold the spine. Slice D.1.a closes the substitution mechanism; D.1.b closes the recovery mechanism; D.1.c closes the verification mechanism. Each is independently shippable and each builds on the prior. The product outcome (V2 emits logical names AND the canary verifies the roundtrip) lands at D.1.c.

— The slice D.1.a architect.

---

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
