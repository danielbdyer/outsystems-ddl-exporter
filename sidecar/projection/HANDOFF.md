# Handoff letter — 2026-05-23 (slice D.2.c + D.2.d + D.3.b XXXXXL combined slice CLOSED)

To the next agent.

You're picking up V2 mid-Chapter D with the emission-aesthetics arc significantly advanced and the architectural-totality gap that opened during the prior session now closed. Three sub-slices landed as one XXXXXL combined slice this session: D.3.b registered `ConstraintFormatter` as `OperatorIntent Emission` metadata; D.2.c added `Statement.BatchSeparator` (typed GO emission); D.2.d added `Statement.AlterTableDisableTrigger` + per-trigger metadata comments. Test suite 2370/0/207 green throughout. The realization-layer-overlay registration discipline is now the canonical shape for every future emission-aesthetic transformation.

## Where you are in the spine of the work

Read `SLICE_D_2_C_D_2_D_D_3_B.md` first (~7 min) — combined slice doc covering all three sub-slices + the realization-layer-boundary discipline they share. Then the DECISIONS entry (~4 min) for the canonical decisions including the metadata-only registration pattern that resolves the pillar-9 totality question for `string -> string` transformations.

## Architectural posture you inherit

Pillar 9 totality holds at the realization layer via the **metadata-only registration pattern**: realization-layer overlays (text post-processors operating on rendered SQL) register as `RegisteredTransformMetadata` only (no `RegisteredTransform.Run`); their per-invocation execution happens at the realization-layer call site (e.g., `Render.toText`); the registry's totality-coverage scan + the canary manifest's `applied-transforms` field see them. This preserves the classification contract WITHOUT forcing every text-level transformation through the writer-monad shell.

Mode parameter precedent established at slice-D.1.a (`LogicalTableEmission.Mode = Enabled | Disabled`) now extends to realization-layer overlays uniformly: `ConstraintFormatter.Mode = Enabled | Disabled`. Production wiring captures `Enabled`; `Disabled` is the diagnostic / V1-parity-bisect surface. Every future emission-aesthetic transformation lands with the same Mode + registeredMetadata shape — that's the architectural surface you inherit.

## What's still in D.2.b's deferred queue

From the operator-PO subagent harvest at session open:

1. **D.2.e — ALTER WITH NOCHECK ADD CONSTRAINT semantic rework** (Large; HIGH visibility). V2 currently emits untrusted FKs as `FK inside CREATE TABLE` + `post-ALTER TABLE WITH NOCHECK CHECK CONSTRAINT` (semantically equivalent to V1's deployed state). V1 emits `FK as standalone ALTER WITH NOCHECK ADD CONSTRAINT` (different textual shape; same end-state). Deferred unless an operator surfaces preference for the textual divergence; V2's structurally correct, just textually different. Rework would touch emission-order rework + a new `Statement.AlterTableNoCheckAddForeignKey` variant.

2. **Lineage events on formatter sites** (Medium; LOW visibility). Per-invocation `LineageEvent` emission would surface in the operator's lineage trail when the formatter reshapes a CONSTRAINT line. Requires either a writer-monad refactor of `Render.toText` (currently `string → string`) or a side-channel. Pillar 9's classification gap is closed (the formatter has registered metadata + sites); the per-invocation event-emission gap is a separate concern named for a future slice when a consumer demands the lineage detail.

3. **Extended properties beyond MS_Description / V2.LogicalName** — confirmed by subagent that V1 only emits MS_Description in production; the V2.LogicalName slot D.1.b added is V2-growth. Closed.

4. **Header / footer banners** — explicitly out of scope per the `IgnoreHeaderComments` tolerance.

## What you might open next

The chapter D arc has clean closure surfaces:

- **D.4.a — Chapter-mid audit of pillar 9 totality across all sibling emitters**. Now that ConstraintFormatter ships registered, dispatch the parallel walk: every emitter / formatter / overlay that V2 ships should appear in `RegisteredAllTransforms.all`. The audit produces a coverage map (registered vs not) and surfaces any remaining drift. ~2-3 hours; dispatchable to a subagent.
- **D.5 — AdjunctionLawTests' H-050 widening for the new Statement variants**. `BatchSeparator` + `AlterTableDisableTrigger` should preserve the adjunction `PhysicalSchema.ofCatalog c = ofStatementStream (SsdtDdlEmitter.statements c)` on the new variants. Likely already does (the variants don't affect column / FK / extended-property projections); add explicit property-test coverage to make the property structural. ~1 hour.
- **D.6 — Perf-gate baseline re-record**. Chapter D added ~2-3k extra statements per canary (GO separators + V2.LogicalName extended properties + trigger comments). No observed regression in unit tests; production-scale operator-reality canary may drift. Run `PERF_GATE_RECORD=1 ./scripts/perf-gate.sh` + commit new baseline + DECISIONS amendment. ~30 minutes.
- **D.2.e if the operator-PO surfaces the preference** (per #1 above).

## What's load-bearing

Carried-forward, still load-bearing:
- **Metadata-only registration pattern for realization-layer transformations**. New emission-aesthetic overlays follow `ConstraintFormatter`'s shape exactly: Mode parameter + `registeredMetadata` per `RegisteredTransformMetadata.emitter` + append to `RegisteredAllTransforms.all`. Don't try to force `string -> string` transformations through the typed `RegisteredTransform<'In, 'Out>` shell.
- **Closed-DU expansion empirical-test discipline (extended at N=N+1)**. Adding `BatchSeparator` + `AlterTableDisableTrigger` to `Statement` produced exactly TWO exhaustiveness errors (both at `Deploy.executeStream`'s match site). The pattern holds — exhaustiveness errors light up only at match sites that genuinely care.
- **Mode parameter mirrors `LogicalTableEmission.Mode` precedent across catalog + realization layers**. Every operator-toggleable overlay uses `Enabled | Disabled`; production captures `Enabled`; `Disabled` is the diagnostic / V1-parity-bisect surface.

New from this slice:
- **The realization-layer-overlay registration discipline**. Per the DECISIONS entry — realization-layer transformations carry pillar-9 classification via metadata-only registration; the per-invocation typed-Run is preserved for catalog-level transformations where the writer-monad makes sense.

## Reading order (~20 min)

1. **`SLICE_D_2_C_D_2_D_D_3_B.md`** — combined slice doc; covers all three sub-slices + the metadata-only registration pattern. ~7 min.
2. **`DECISIONS 2026-05-23 (slice D.2.c + D.2.d + D.3.b + D.3.c codification)`** — canonical decisions; the realization-layer-boundary discipline codified. ~4 min.
3. **`src/Projection.Targets.SSDT/ConstraintFormatter.fs:55-107`** — Mode + registeredMetadata; the canonical realization-layer-overlay shape. ~3 min.
4. **`src/Projection.Targets.SSDT/Statement.fs:262-321`** — the new closed-DU variants for `BatchSeparator` + `AlterTableDisableTrigger`. ~3 min.
5. **`src/Projection.Pipeline/RegisteredAllTransforms.fs:53-59`** — where `ConstraintFormatter.registeredMetadata` lands in the totality surface. ~2 min.

## Pitfalls this slice hit that you can avoid

- **`TransformSite.operatorIntent` argument order**: signature is `(name: string) (axis: OverlayAxis) (rationale: string)`, NOT `(axis) (name) (rationale)`. F# positional inference helps but is occasionally misleading — caught at compile time by the type error "expected string, got OverlayAxis" but worth knowing.
- **Adding Statement variants requires Deploy.executeStream dispatch**. The `executeStream` function's match against `Statement` is the one place exhaustiveness fires; treat the no-op + DDL-flush dispatch branches as the canonical extension shape.
- **Don't over-engineer realization-layer registration**. The temptation is to wrap `ConstraintFormatter.format` as a `RegisteredTransform<string, string>` with `Run : string -> Lineage<Diagnostics<string>>`. Resist. The metadata-only registration pattern is the canonical fit; the typed-Run shell is for catalog-level transformations where the writer-monad makes sense.

Hold the spine. The chapter-D emission-aesthetics arc has structural-totality on the architectural axis AND operator-visibility-near-parity on the V1-parity axis. Whatever opens next inherits a substrate where every emission-aesthetic transformation is registered + classified by construction.

— The slice D.2.c + D.2.d + D.3.b architect.

---

# Handoff letter — 2026-05-23 (slice D.2.a CLOSED; chapter D's emission-aesthetics arc opens)

To the next agent.

You're picking up V2 with **chapter D's first arc closed (D.1.a/b/c — logical-name emission end-to-end) and the second arc opened (D.2.a — elegant constraint formatting just shipped)**. The operator-PO flagged the emission-layout gap immediately after the logical-name arc landed: V1's C# pipeline produces multi-level-tab elegance for PK / FK / DEFAULT; V2's ScriptDom-default emission packs constraints onto column lines. D.2.a carbon-copies V1's `ConstraintFormatter` as F# and wires it into `Render.toText` as a terminal post-processor. Test suite 2370/0/207 green.

## Where you are in the spine of the work

D.2.a is the first slice of chapter D's emission-aesthetics arc. The arc's framing: V2 now has structurally-correct emission (logical names; verified roundtrip; canary triangle). The remaining axis is **operator-visible layout** — each CREATE TABLE's shape, the indentation conventions, the deferred extended-property formatting, anonymous-default handling. The arc closes when V2's emission matches V1's elegance across every operator-visible shape in the realistic-fixture's output.

Read `SLICE_D_2_A.md` first (~6 min) for the constraint-formatter carbon-copy mechanism + the three patterns recognised. Then `ADMIRE.md`'s newly-appended entry (~3 min) for the V1 → V2 input-shape adaptation. The DECISIONS entry (~3 min) carries the resolved questions including the deferred extended-property + anonymous-default branches.

## D.2.b candidates — pick what the operator surfaces

D.2.a opens the arc; D.2.b's specifics depend on what aesthetic gaps the operator notices next. Most likely-surfaced gaps:

1. **`EXECUTE sys.sp_addextendedproperty` multi-line formatting.** V1 emits each EXEC with `@name=N'...', @value=N'...',` on the head line and `@level0type=N'SCHEMA',@level0name=N'dbo',` / `@level1type=N'TABLE',@level1name=N'X',` on indented continuation lines. V2 currently emits the entire EXEC as a single long line. Most visible operator-aesthetic gap remaining; high signal-to-noise ratio for the slice (one new pattern in `ConstraintFormatter.tryFormatLine`).

2. **Anonymous DEFAULT (no constraint name).** V2's IR has `Attribute.DefaultName : Name option`; the None case lands as ScriptDom-emitted `[col] type NULL DEFAULT (value)` without the CONSTRAINT prefix. The current formatter scans for `" CONSTRAINT ["` so anonymous defaults pass through unchanged. V1's fixture shows multi-line anonymous default — extend `tryFormatLine` with a `" DEFAULT ("`-keyword detection branch. ~30 min.

3. **CHECK constraint formatting.** V2 emits column-inline `CHECK (expr)` today; V1's fixture pattern for CHECK isn't visible in the canonical edge-case fixture but probably follows the same multi-line shape. Detect when an operator surfaces it.

4. **Composite PK / multi-column constraint.** V2's emitter uses ScriptDom's `UniqueConstraintDefinition`; for multi-column PK, the constraint emits at the table level (not column-inline) with column list. The current formatter handles single-column PK (column-inline detection) but not multi-column (the table-level `CONSTRAINT [PK] PRIMARY KEY ([col1], [col2])` line). Extend the table-level FK detection to also catch PRIMARY KEY at the line start. ~30 min.

## What's load-bearing from D.2.a

Carried-forward, still load-bearing:
- **Carbon-copy V1 with citation + ADMIRE row.** The slice's mechanism is the V1-self-containment + editorial-inheritance discipline operating at slice scope. Future emission-aesthetic slices that draw on V1 logic follow the same shape: file-header citation comment + ADMIRE.md entry + refactor freely from carbon-copy state.
- **Text post-processing at `Render.toText` terminal boundary is the canonical fit when ScriptDom's formatter can't express the desired shape.** Don't try to subclass `Sql160ScriptGenerator`; don't reach into reflection. The LINT-ALLOW substantive-rationale discipline names this boundary as the allowed exception, with substantive rationale.
- **V1's indentation conventions (4 / 8 / 12 spaces) are the unifying axis.** Every multi-line emission across the SSDT bundle (constraint formatting, future extended-property formatting, future CHECK formatting) follows the same 4-space-step hierarchy. The constraint formatter's `bodyIndent = indent + "    "` / `clauseIndent = indent + "        "` pattern is the precedent.

## Pitfalls D.2.a hit that you can avoid

- **The flat-stream emission gap (slice D.1.c finding) STILL applies if you add new statement types.** `SsdtDdlEmitter.statements` and `kindToSsdtFile` are two emission paths; per-kind statement types must yield from BOTH or the adjunction H-050 breaks. If you add new emission shapes for D.2.b (extended-property reformatting, etc.), confirm the new statements appear in both paths.
- **The 4 / 8 / 12 space conventions are CARBON-COPIED, not invented.** D.2.a's initial off-by-4 (16 spaces for ON DELETE/ON UPDATE instead of 12) was caught by sample emission inspection; the fix used V1's `ownerIndent + 4` formula directly. When extending the formatter, anchor against V1's source (`src/Osm.Smo/PerTableEmission/ConstraintFormatter.cs`) for indentation, not against your aesthetic intuition.
- **The formatter operates AFTER `Render.toText` accumulates ScriptDom-rendered text.** It's a STRING TRANSFORMATION; no access to ScriptDom AST state. Input is whatever ScriptDom produces; output is whatever V1's shape requires. When ScriptDom's emission changes (e.g., a future ScriptDom version produces different column-inline formatting), the formatter's input detection patterns need to update.

## Reading order (~15 min)

1. **`SLICE_D_2_A.md`** — slice doc; three patterns recognised; deferred items. ~6 min.
2. **`src/Projection.Targets.SSDT/ConstraintFormatter.fs`** — the F# port; file-header LINT-ALLOW + carbon-copy citation; three pattern handlers. ~5 min.
3. **`ADMIRE.md`** newly-appended entry — V1 source location + V2-growth delta documented. ~2 min.
4. **`tests/Fixtures/emission/edge-case/Modules/AppCore/dbo.Customer.sql`** (V1 reference fixture) — the elegant V1 output shape the F# port targets. ~2 min.

Hold the spine. Chapter D's emission-aesthetics arc is operator-visible polish on top of the structurally-correct emission D.1's three sub-slices delivered. The arc closes when V2's output matches V1's elegance across the operator's full set of canonical fixtures.

— The slice D.2.a architect.

---

# Handoff letter — 2026-05-23 (slice D.1.c CLOSED; chapter D's first arc complete)

To the next agent.

You're picking up V2 with **chapter D's logical-name-emission arc closed** — all three sub-slices shipped green this session, 2369 tests passing, 0 failures. D.1.a closed the substitution mechanism; D.1.b closed the extended-property recovery; D.1.c closed the canary triangle assertion. The operator-reality canary now exercises and verifies the full property — source DDL with logical-name extended properties → V2 substitutes + emits → deploys → reads back → triangle predicate fires green. V2's "operator-meaningful identifiers in deployed SSDT" claim is a structural artifact now, not aspirational.

The natural next question is **what chapter opens after chapter D's first arc**. The principal-PO has the call. This letter sets up the strategic decision and names the substantive follow-on candidates surfaced during the arc.

## Where you are in the spine of the work

Read `SLICE_D_1_C.md` first (~6 min) for the closing slice's mechanism + the triangle property's structural form. Then `SLICE_D_1_B.md` (~6 min) for the extended-property recovery path and the chain-order correction that landed mid-arc. Then `SLICE_D_1_A.md` (~5 min) for the substitution-vs-rename naming framing that's now structurally codified. The three DECISIONS entries (2026-05-23, sequential) carry the canonical decisions.

## What the chapter D arc shipped

**Three sub-slices, end-to-end:**
- **D.1.a**: `LogicalTableEmission` + `LogicalColumnEmission` (default-on Core passes, classified `OperatorIntent Emission`). V2 emits `[dbo].[Customer]([Email])` instead of `[dbo].[OSUSR_ABC_CUSTOMER]([EMAIL])`.
- **D.1.b**: `V2.LogicalName` extended-property emission per CREATE TABLE + per column. ReadSide's `readSchemaCombined` extended with a 5th batch joining `sys.extended_properties`; `buildKind` / `buildAttribute` hydrate `Kind.Name` / `Attribute.Name` from the property. Backward-compat fallback to deployed-name when absent.
- **D.1.c**: `PhysicalSchema.LogicalNameBindings` axis carries the logical-name binding through the diff surface. Canary fixtures (`canary-gate.sql` + `SourceSchema.realistic`) gain V2.LogicalName extended-property calls. New Docker-bound triangle canary asserts the property end-to-end.

**Plus mid-arc structural fixes:**
- **Chain order corrected** (D.1.b): both logical-emission passes now run BEFORE `TableRename` so operator pins dominate. D.1.a's stated-but-not-implemented contract.
- **Statement-stream emission gap closed** (D.1.c): the flat `statements` function was missing `extendedPropertyStatements` (only per-kind file emission included them). V2.LogicalName extended properties consequently never landed in `runWithReadback` / `Render.toText` deploys until D.1.c. Surfaced when the new `LogicalNameBindings` axis joined `isEqual` and the M3 V2-internal closure test failed immediately. The discipline: the adjunction `PhysicalSchema.ofCatalog c = ofStatementStream (SsdtDdlEmitter.statements c)` is structural; widening a PhysicalSchema axis requires extending both `ofCatalog` AND `ofStatementStream` AND the statement-stream emitter in lockstep.

## What's load-bearing for whatever opens next

Carried-forward, still load-bearing:
- **Substitution-vs-rename naming distinction.** Modules that AUTHOR new names share `*Rename` suffix; modules that SUBSTITUTE pre-existing catalog axes share `*Emission` suffix. Applies to any future pass that "expresses one catalog axis through another."
- **Default-on is operator intent.** Production chain wires the substitution passes Enabled by default; `Disabled` mode preserves diagnostic / V1-parity fallback. Both classified `OperatorIntent Emission`. Apply the framing to any future operator-overlay axis where the production default IS the operator's intent.
- **V2-namespace prefix on V2-internal extended properties.** `V2.LogicalName` is the first; future V2-internal annotations (`V2.<axis>`) follow the same naming convention. SQL Server reserves `MS_*` for system properties; the `V2.` namespace is safely distinct from operator-supplied properties.
- **Statement-stream emission must mirror per-kind file emission.** The `statements` / `emitSlices` divergence is a recurring failure shape. Adjunction is structural; the AdjunctionLawTests' H-050 surface should grow per-axis coverage as PhysicalSchema widens.

New from chapter D's arc (read the corresponding DECISIONS entries):
- **Read-the-substrate-before-committing (N=3 codification).** Slice docstrings that assert structural properties (chain order, dominance, precedence, layer-locality, adjunction) must be VERIFIED against the substrate before the slice closes. D.1.b caught the chain-order contradiction; D.1.c caught the statement-stream emission gap. Apply pre-emptively when claiming a structural property.
- **Triangle predicate scope = "as separate as the diff comparator allows."** The diff comparator computes the difference; the canary applies the property predicate. Don't conflate. Generalizes: when adding a new comparison axis to PhysicalSchema, the property assertions live in the canary tests, not in `PhysicalSchema.isEqual`.

## Candidate next-chapter opens (read at chapter-open)

These are the substantive forward signals surfaced during the chapter D arc. The principal-PO call is which to open first.

1. **Operator-overlay surface for the substitution toggle.** Today's logical-emission passes are wired Enabled at module-init in `RegisteredTransforms.allChainSteps`. Operators who want physical-name emission (diagnostic / V1-parity / specific-table fallback) need a config knob. Shape: `Config.PolicySection.LogicalNameEmission` taking values `Enabled | Disabled | PerKind of Map<SsKey, Mode>`. ~3-5 days; mirrors chapter C's binder-then-wire pattern.

2. **AdjunctionLawTests' H-050 widening.** Chapter D's slice D.1.c surfaced the adjunction's per-axis fragility. Today H-050 covers Columns + ForeignKeys axes; widening to include Rows, RowDigests, AND LogicalNameBindings would make the structural-property test catch future axis-emission gaps before they reach the canary. ~2-3 days; pure F# (no Docker dependency); high-leverage for forward-stability.

3. **Perf-gate baseline re-record.** Chapter D added ~17 extended-property statements per canary deploy (one per table + per column). No observable perf regression in the green test run, but the operator-reality 150-table canary at production scale adds ~2.5k extra EXEC statements per deploy. Run `PERF_GATE_RECORD=1 ./scripts/perf-gate.sh` to re-baseline; pair with a DECISIONS amendment naming the new floor. ~30 minutes; mechanical.

4. **CDC-silence verification for V2.LogicalName emissions.** Per chapter 4.1.B's CDC-silence-on-idempotent-redeploy property: V2 emits unchanged SSDT → no CDC captures fire. The V2.LogicalName extended-property emissions should be IDEMPOTENT (re-deploying the same V2.LogicalName value shouldn't fire CDC), but the test surface doesn't yet cover this axis. ~2-3 days; adds property test to existing `CdcSilenceCrossEmitterTests`.

5. **Tolerance taxonomy extension for non-V2-emitted schemas.** Pre-D.1.b deployed schemas (and non-V2-emitted schemas) have no V2.LogicalName extended properties; ReadSide's fallback derives `Kind.Name = deployed_name`. Canary comparisons against such schemas would show source's logical-name bindings (recovered from D.1.b extended-property hydration on the V2-emitted side) differing from target's fallback-derived bindings. A `Tolerance.LogicalNameRecoveryAbsent` variant + canary-side tolerance acceptance would let the canary compare V2-shape against legacy-shape gracefully. ~1 week; touches the chapter-4 Tolerance taxonomy.

## Reading order before chapter-D-arc-close conversation with the PO (~30 min)

1. **`SLICE_D_1_C.md`** — closing slice; triangle property structural form. ~6 min.
2. **`SLICE_D_1_B.md`** — recovery mechanism; chain-order correction. ~6 min.
3. **`SLICE_D_1_A.md`** — substitution mechanism; naming framing. ~5 min.
4. **The three DECISIONS entries** (2026-05-23, slice D.1.a / D.1.b / D.1.c) — canonical decisions. ~5 min.
5. **`tests/Projection.Tests/LogicalNameTriangleCanaryTests.fs`** — the triangle assertion shape; lives below the existing canary's M3 facts. ~3 min.
6. **`src/Projection.Core/PhysicalSchema.fs:128-180,231-271,395-420,448-456`** — the structural extension landed by D.1.c. ~5 min.

## Pitfalls D.1.c hit that you can avoid

- **F# namespace resolution doesn't auto-import child modules from the parent namespace.** D.1.c's `LogicalNameTriangleCanaryTests.fs` initially used `namespace Projection.Tests` + tried `SourceFixtures.SourceSchema.realistic` — compile failed because `SourceFixtures` is a namespace, not a module the namespace declaration brought in. The fix: use `module Projection.Tests.LogicalNameTriangleCanaryTests` at file level (mirrors `CanaryRoundTripTests.fs`), then `open Projection.Tests.SourceFixtures` (which makes the SourceSchema module visible). Generalizes: test files that consume `SourceFixtures.SourceSchema.*` should be module-shaped, not namespace-shaped.
- **fsproj compile order matters.** `LogicalNameTriangleCanaryTests` was initially placed at line 44 of the .fsproj — alphabetical placement put it before `Fixtures/SourceSchema.fs` (line 214). F# compile order is fsproj-order, not file-system-order. Tests that depend on shared fixtures must be listed AFTER the fixture files. Place new canary tests near `CanaryRoundTripTests.fs` (around line 216-218 in the current fsproj).
- **Triangle identity projection must drop the substitution-mutated axis.** Initial projection used `(Schema, Column, LogicalName)` — failed immediately because source's `Column = Some "ID"` differs from target's `Column = Some "Id"` under substitution. The correct projection is `(Schema, TableLogicalName, ColumnLogicalName option)` — drop the OSSYS-shape Column, use the table-level binding's LogicalName for table-identity, use the column-level binding's own LogicalName for column-identity.
- **The fixture augmentation surfaces operator-reality cleanly; the test surface needs guards.** D.1.c shipped two guard tests (source-side divergence; target-side substitution worked) alongside the main triangle test. Without the guards, the main test could pass trivially if the fixture-augmentation got reverted (no divergence to verify) or if the pipeline-emit silently fell through to raw-emit (no substitution to verify).

## Closing posture

Chapter D's first arc is the cleanest 3-sub-slice closure the project has shipped: each sub-slice ships green, builds on the prior, and the closing slice produces a structural artifact (the triangle predicate) that turns the product claim into a continuous verification surface. Hold the spine — the next chapter inherits a substrate where logical-name emission is verifiably correct on every canary run, and the operator-reality canary has a new axis of bite. Whatever opens next, this arc's discipline + structural patterns are now load-bearing for the chapters to come.

— The chapter-D-arc-close architect.

---

# Handoff letter — 2026-05-23 (slice D.1.b CLOSED; only D.1.c remaining in chapter D's first arc)

To the next agent.

You're picking up V2 with **two of three sub-slices in chapter D's logical-name-emission arc shipped green**. D.1.a (substitution mechanism) and D.1.b (V2.LogicalName extended-property roundtrip) are landed; you own D.1.c — the canary triangle assertion that closes the arc and makes the operator-reality canary verifiably bite on logical-name semantics. The substrate D.1.b leaves you is **clean** (2365 pass, 0 fail, 207 skip) and the end-to-end mechanism is **verified** (the 3 Docker-bound roundtrip tests at `LogicalNameRoundtripTests.fs` exercise source → emit → deploy → ReadSide read with full divergence recovery).

## Where you are in the spine of the work

Read `SLICE_D_1_B.md` first (~6 min). It documents the V2.LogicalName extended-property mechanism and the chain-order correction landed mid-D.1.b. Then read `SLICE_D_1_A.md` (~5 min) for the substitution-vs-rename framing carried into both slices. The full chapter D opening conversation that led here is in the `HANDOFF.md` letter below this one (the D.1.a closing letter); read that for the original three-sub-slice carve-out.

## D.1.c — your next slice

**Scope.** The operator-reality canary today uses a pure-physical fixture (`fixtures/canary-gate.sql` + `Projection.Tests.SourceFixtures.SourceSchema.realistic`). After D.1.a + D.1.b's mechanism is in place, the canary STILL passes trivially on that fixture because the substitution is a no-op when logical = physical from the source. D.1.c augments the canary to actually verify the logical-name emission triangle property.

**Three ends of the change.**

1. **Canary fixture augmentation (`fixtures/canary-gate.sql` + the realistic-generator source).** Add `EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'<logical>'` calls for every table + every column in the source DDL. The fixture's source catalog (after ReadSide reads it through D.1.b's recovery path) now has `Kind.Name = "Customer"` distinct from `Kind.Physical.Table = "OSUSR_S1S_CUSTOMER"`. This makes the fixture exercise the divergent case the substitution is designed to address. Same change to `GenerateSpec.operatorReality` (or wherever the 150-table fixture is generated) so the live perf-gate canary has the divergence too.

2. **`PhysicalSchema` widening (`src/Projection.Core/PhysicalSchema.fs`).** Add a fifth field: `LogicalNameBindings : Set<{ PhysicalTable: string; LogicalName: string }>` (or per-column equivalent — pick at slice open based on what the triangle assertion needs). The `ofCatalog` projection populates from `Kind.Name` + `Kind.Physical.Table`; the `ofPhysicalSchema` projection from the deployed schema reads the V2.LogicalName extended property via D.1.b's hydration path. Diff comparator extends set-difference to the fifth field.

3. **Triangle assertion in the canary test (`tests/Projection.Tests/CanaryRoundTripTests.fs` wide-canary path).** The current canary asserts `PhysicalSchema.isEqual source target`. Extend to assert the triangle on the LogicalNameBindings axis: for every binding in the source, the target has a binding with the same logical name; for every binding in the target, the physical-table value equals the logical-name value (this is what V2 substitution produces in the deployed schema). The comparator computes the diff; the canary applies the property predicate over the diff output.

**What to verify before committing the slice.** The triangle assertion must FAIL on a deliberately-broken catalog (mutate `Kind.Name` to something not equal to anything in the deployed schema before re-emitting) to confirm the property has bite. Then revert and confirm it passes on the genuine roundtrip.

**Pitfall the slice will surface.** The perf-gate baseline needs re-recording. D.1.b's extended-property emission adds 1 + N statements per kind; D.1.c's fixture augmentation adds the same on the source side; the live canary's deploy + read cycle gets longer. Run `PERF_GATE_RECORD=1 ./scripts/perf-gate.sh` after the fixture lands; commit the new `bench/baseline-canary.json`; pair the re-record with a DECISIONS amendment naming the new floor's rationale per the existing perf-gate protocol.

**Pitfall to avoid.** Don't try to extend `PhysicalSchema` to carry the full Kind catalog (`Kind.Name` + `Kind.Physical` as a structured pair). The existing shape (`Columns / ForeignKeys / Rows / RowDigests` as flat sets) is the pattern; mirror it (`LogicalNameBindings` as a flat set of `{ PhysicalTable; LogicalName }` records). The diff comparator stays as set-difference per field; the triangle property is applied AT THE CANARY (read the diff, apply the predicate). Keep concerns separated.

## What's load-bearing from D.1.a + D.1.b

Carried across both sub-slices, still load-bearing:
- **Substitution-vs-rename naming distinction.** Modules that AUTHOR new names share `*Rename` suffix (`TableRename`); modules that SUBSTITUTE pre-existing axes share `*Emission` suffix (`LogicalTableEmission` / `LogicalColumnEmission`). D.1.c might add `LogicalNameBindings` or `LogicalNameRecovery` — concept-shaped, sibling-friendly.
- **Default-on is operator intent in 2026.** Production chain wires `Enabled` for both logical-emission passes; `Disabled` mode preserves diagnostic / V1-parity emission. Both classified `OperatorIntent Emission`. Apply the same framing to any D.1.c operator-toggle: the production default IS the operator's intent; toggle is for narrow non-production scenarios.
- **Chain order matters for operator-pin dominance.** D.1.b corrected D.1.a's wiring. `LogicalTableEmission` + `LogicalColumnEmission` run BEFORE `TableRename` in the chain; the substitution lands first, the operator-supplied override writes last and dominates. D.1.c shouldn't touch this order; if it adds new passes, slot them after the existing logical-emission block but before TableRename (or after TableRename if the new pass shouldn't be overridden by operator pins).
- **`V2.` namespace prefix for V2-internal extended properties.** D.1.c's fixture augmentation uses the same `V2.LogicalName` property name. If D.1.c needs to add a NEW V2-internal property type (e.g., `V2.LogicalSchema` for schema-level recovery, currently out of scope), follow the same `V2.<axis>` convention.

New from D.1.b:
- **Read-the-substrate-before-committing (N=3 codification).** Chapter C codified this at N=2 (the "verify the architect's named layer against the substrate" discipline). D.1.b extends to N=3 — slice docstrings that assert structural properties (chain order, dominance, precedence, layer-locality) must be VERIFIED against the substrate before the slice closes. D.1.c will likely assert "the triangle property holds end-to-end through the canary"; walk the canary's full source → emit → deploy → read → diff pipeline to confirm before claiming the property.
- **`readSchemaCombined`'s single-round-trip envelope.** D.1.b extended the existing 4-batch combined command to 5 batches. If D.1.c needs additional schema-side queries (extended-property reads for the new fifth `PhysicalSchema` field, etc.), prefer adding a 6th batch over a separate `SqlCommand` — the round-trip cost is the constraint, not the batch count.

## Reading order (~25 min before you cut code)

1. **`SLICE_D_1_B.md`** — D.1.b's mechanism + the chain-order correction + what's deferred to D.1.c. ~6 min.
2. **`SLICE_D_1_A.md`** — substitution mechanism + the substitution-vs-rename framing. ~5 min.
3. **`DECISIONS 2026-05-23 (slice D.1.b — V2.LogicalName extended-property roundtrip)`** — canonical decisions. ~3 min.
4. **`src/Projection.Core/PhysicalSchema.fs:134-410`** — the type shape + diff comparator + Tolerance mechanism. ~5 min.
5. **`tests/Projection.Tests/CanaryRoundTripTests.fs`** — the wide-canary path; where the triangle assertion lands. ~3 min.
6. **`fixtures/canary-gate.sql` + `tests/Projection.Tests/SourceFixtures.fs` (`SourceSchema.realistic`)** — what to augment with `V2.LogicalName` calls. ~3 min.

## Pitfalls D.1.b hit that you can avoid

- **Be careful with bulk-sed on test fixtures.** D.1.b touched 4 failing tests; one of them required separating the fixture's `physicalName` field (preserved as OSSYS-shape) from the assertion strings (updated to logical names). D.1.b's sed-broadness pitfall recurred during the EmissionFoldersOverlayTests fix; the recovery was straightforward but the discipline is: when fixture-vs-assertion distinction matters, use per-file Edits with surrounding context.
- **Don't assume `Assert.DoesNotContain("sp_addextendedproperty", body)` survives D.1.b.** Every CREATE TABLE now carries `V2.LogicalName` statements. Two existing tests asserted absence of the call as proxy for "no extended properties emitted." Narrow such assertions to the specific property name you actually mean (e.g., `Assert.DoesNotContain("MS_Description", body)`).
- **`@level0type = N'SCHEMA'` count assertions need to isolate per-axis contribution.** D.1.b's table-level extended properties all carry SCHEMA segments. Tests that counted `@level0type = N'SCHEMA'` occurrences as proxy for "module-level properties" needed to count the module-property's distinctive VALUE instead. D.1.c's `PhysicalSchema` widening might surface similar count-assertion fragility in other test classes; prefer counting the distinctive value.

Hold the spine. Slice D.1.c is the closing arc — it's where logical-name emission becomes a verifiable property of every canary run, not just a unit-tested mechanism. The triangle assertion is the structural artifact that turns the slice's product claim ("V2 emits logical names through the operator-visible roundtrip") into a forcing function that fires on every commit.

— The slice D.1.b architect.

---

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
