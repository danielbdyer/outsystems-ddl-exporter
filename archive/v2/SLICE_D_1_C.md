# Slice D.1.c — canary triangle assertion

**Status**: shipped 2026-05-23. Closes chapter D's logical-name-emission arc. `PhysicalSchema` widens with `LogicalNameBindings : Set<LogicalNameBinding>`; the canary fixtures gain `V2.LogicalName` extended-property calls; a new triangle canary test (Docker-bound, 3 facts) asserts the property end-to-end on the realistic operator-shape source.

## The triangle property

After slices D.1.a (substitution) + D.1.b (extended-property recovery), the logical-name emission pipeline has two architectural legs that need verification:

```
source.Kind.Name = target.Kind.Name              (logical identity preserved across deploy→read)
source.Kind.Name = target.Kind.Physical.Table    (V2 substituted the logical name as deployed physical)
```

Both legs are verified on every canary run by a single predicate over `PhysicalSchema.LogicalNameBindings`:

- **Identity preservation**: for every source binding's `(Schema, TableLogicalName, ColumnLogicalName)` triple, exists a target binding with the same triple. Set-difference over the projection (drop `Table` because it differs by design — source's is OSSYS-shape, target's is logical).
- **Substitution worked**: every target binding satisfies `binding.Table = binding.LogicalName` at the table level and `binding.Column = Some binding.LogicalName` at the column level.

Together: source's logical names round-trip through the canary AS deployed physical identifiers AND remain recoverable on the target side. The chapter D product claim ("V2 emits operator-meaningful identifiers verifiable through the operator-reality canary") is a structural artifact now.

## What landed

**Core widening** — `src/Projection.Core/PhysicalSchema.fs`:
- New type `LogicalNameBinding { Schema; Table; Column: string option; LogicalName }` — concept-shaped record carrying the deployed coordinate paired with the logical name. `Column = None` for table-level bindings (the kind); `Column = Some col` for column-level.
- New field `PhysicalSchema.LogicalNameBindings : Set<LogicalNameBinding>` — populated by `ofCatalog` from `Kind.Name` + `Attribute.Name` paired with the physical coordinate.
- `PhysicalSchemaDiff.MissingLogicalNameBindings` / `ExtraLogicalNameBindings` — set-difference comparison on the new axis. `isEqual` extends to ten axes (was eight); `renderDiff` extends with operator-readable binding output.

**Statement-stream emission fix** — `src/Projection.Targets.SSDT/SsdtDdlEmitter.fs`:
- The `statements` function (flat-stream surface consumed by `Render.toText` / `Deploy.runWithReadback`) was emitting CREATE TABLE + FK + indexes + triggers but **not** the per-kind `extendedPropertyStatements` (including the slice-D.1.b `V2.LogicalName` entries). The `emitSlices` per-kind file path included them; the `statements` path didn't.
- Fix: thread `extendedPropertyStatements k` into the per-kind statement yield, matching `kindToSsdtFile`'s emission order. This is what made the M3 V2-internal closure canary actually exercise the logical-name roundtrip (it had been silently emitting via the flat stream without the extended properties).

**Statement-stream adjunction** — `src/Projection.Targets.SSDT/PhysicalSchemaReader.fs`:
- `ofStatementStream` extended to recover `LogicalNameBindings` from `SetExtendedProperty` statements where `propertyName = "V2.LogicalName"`. The adjunction `PhysicalSchema.ofCatalog catalog = ofStatementStream (SsdtDdlEmitter.statements catalog)` now holds on the new axis too — the statement-stream projection produces the same bindings as the catalog-side projection.

**Fixture augmentation** — `fixtures/canary-gate.sql` + `tests/Projection.Tests/Fixtures/SourceSchema.fs`:
- Both fixtures gain `EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'<logical>'` calls for every table + every column. ReadSide's slice-D.1.b hydration query picks these up; the source catalog (post-readback) has divergent `Kind.Name = "Customer"` vs `Kind.Physical.Table = "OSUSR_M3_CUSTOMER"`. Without this, the source side has no logical-vs-physical divergence and the substitution path is unobservable.

**Triangle canary** — `tests/Projection.Tests/LogicalNameTriangleCanaryTests.fs`:
- 3 Docker-bound facts using `SourceSchema.realistic` (augmented) + a pipeline-emit function that composes `LogicalTableEmission.Enabled` + `LogicalColumnEmission.Enabled` + `SsdtDdlEmitter.statements`. Each fact asserts a different leg of the triangle:
  1. The full predicate (identity preservation + substitution worked).
  2. Source-side divergence is real (guard against silent fixture drift; if V2.LogicalName extended-property statements stop landing, this fires before the main triangle test passes trivially).
  3. Target-side substitution worked (every target kind has `Physical.Table = Name.value Name`).

**AxiomTests citation** — `L3-Emission-LogicalTriangle (slice D.1.c)` citing the triangle canary test.

**Full test suite**: 2369 pass, 0 fail, 207 skipped (+3 from prior 2366).

## Why the existing canary already works

Slice D.1.c's widening surfaced a structural collision with the existing `M3: V2-internal closure` test: with the new `LogicalNameBindings` axis, `isEqual` compares bindings as full records (including the `Table` field). If source's `Kind.Physical.Table = "OSUSR_M3_USER"` and target's also = `"OSUSR_M3_USER"` (no substitution), bindings should match exactly.

What broke initially: the flat-stream emit path (used by `runWithReadback`) didn't include the per-kind `extendedPropertyStatements`. So target had no V2.LogicalName entries; ReadSide fell back to `Name.create deployed_name`; target's bindings had `LogicalName = "OSUSR_M3_USER"` while source had `LogicalName = "User"` (the programmatic catalog's `Kind.Name`). Diff: mismatch.

The statement-stream emission fix lands the V2.LogicalName statements in the flat stream too. Now the raw-emit path (no substitution, no pipeline) produces target whose bindings round-trip identically to source's — both have `Table = "OSUSR_M3_USER"`, both have `LogicalName = "User"`, etc. `isEqual` returns true.

The triangle canary is the SEPARATE assertion path — it deliberately runs the pipeline-emit (substitution + recovery) so target's Table = logical-name, and applies the predicate that compares identity-axis (not full record) plus the substitution-worked leg.

## What this slice does NOT do

- **No perf-gate baseline re-record.** The fixture augmentation adds ~17 extended-property statements per canary deploy (one per table + per column). Per-canary perf delta should be a few ms (well within σ_effective floor); no observed regression in the green test run. If the perf-gate fires on next CI run, re-record per the existing protocol (`PERF_GATE_RECORD=1 ./scripts/perf-gate.sh` + DECISIONS amendment naming the new floor).
- **No `LogicalNameBinding` smart constructor.** The type is a plain record. Smart constructor (returning `Result<_>`) would land when consumers carry invariants beyond what the type expresses today (e.g., `Schema` non-blank, `LogicalName` matches SQL identifier shape). One consumer today (canary projection); below the two-consumer threshold.
- **No CLI surface for the triangle assertion.** The triangle is a test-side predicate, not a production-side function. Operators run the canary; the canary asserts the property; failure surfaces via the standard test-failure channel. A `projection canary --verify-triangle` CLI flag would land if operator-pull surfaces for invoking the property outside the test harness.

## Decisions resolved

- **Triangle property as a SEPARATE predicate, not part of `isEqual`.** `PhysicalSchema.isEqual` does set-difference on the FULL `LogicalNameBinding` record (Table included). Under substitution, source's Table = "OSUSR_*" and target's Table = "logical" — set-difference flags every binding as missing/extra. That's CORRECT diagnostic information (the binding records ARE different) but not what the triangle property cares about. The triangle predicate projects to `(Schema, TableLogicalName, ColumnLogicalName)` and compares on identity alone. Both predicates ship; the canary uses each at its appropriate test (existing canary uses `isEqual` for the raw-emit path; triangle canary uses the predicate for the pipeline-emit path).
- **`LogicalNameBinding` over `PhysicalLogicalNameBinding`.** The concept being named is the BINDING between physical coords and logical name. Naming it `Physical*` would suggest it carries physical-only information (parallel with `PhysicalColumn` / `PhysicalForeignKey`); but the binding's defining attribute IS the logical name. `LogicalNameBinding` names what it IS; the physical-coord fields are attributes. Lives in `PhysicalSchema.fs` because that's where the diff comparator threads it through.
- **Statement-stream emission must mirror per-kind file emission.** The `statements` / `emitSlices` divergence (slice D.1.b shipped V2.LogicalName only via `extendedPropertyStatements` in `emitSlices`; the flat `statements` path missed it) is a recurring failure shape — flat-stream consumers (Deploy.runWithReadback, Render.toText) get a different statement set than per-kind-file consumers (Compose.project). Discipline reinforced: any per-kind statement type that ships via `kindToSsdtFile` must also yield from `statements` to preserve the adjunction.

## Discipline reinforced

- **Statement-stream adjunction is structural.** `PhysicalSchema.ofCatalog catalog = ofStatementStream (SsdtDdlEmitter.statements catalog)` is the AdjunctionLawTests' property; widening `PhysicalSchema` requires extending `ofStatementStream` in lockstep with `ofCatalog`. Otherwise the adjunction quietly fails on the new axis. Caught and fixed in this slice; codified as a reminder for future PhysicalSchema axes.
- **Triangle predicate scope = "as separate as the diff comparator allows."** The diff comparator computes the difference; the canary applies the property predicate. Don't conflate. Keeps `PhysicalSchema` focused on structural comparison; lets test-side assertions express domain-specific predicates without coupling Core to canary-specific semantics.
- **Identity projection drops the substitution-mutated axis.** The triangle's identity triple is `(Schema, TableLogicalName, ColumnLogicalName option)` — NOT `(Schema, Table, Column, LogicalName)`. Source's `Table` and target's `Table` differ by design under substitution; including them in the identity makes the predicate fail on a non-meaningful axis. The projection encodes "what's structurally INVARIANT under substitution" — that's the property being verified.

## Cross-references

- `SLICE_D_1_A.md` + `SLICE_D_1_B.md` — predecessor slices (substitution mechanism + extended-property recovery).
- `src/Projection.Core/PhysicalSchema.fs` — `LogicalNameBinding` type, `PhysicalSchema.LogicalNameBindings` field, diff/isEqual/renderDiff extensions.
- `src/Projection.Targets.SSDT/SsdtDdlEmitter.fs:687` — `extendedPropertyStatements k` yielded in the flat-stream emission path.
- `src/Projection.Targets.SSDT/PhysicalSchemaReader.fs` — `ofStatementStream` extension recovering bindings from `SetExtendedProperty` statements.
- `fixtures/canary-gate.sql` + `tests/Projection.Tests/Fixtures/SourceSchema.fs` — augmented with V2.LogicalName extended-property calls.
- `tests/Projection.Tests/LogicalNameTriangleCanaryTests.fs` — NEW; 3 Docker-bound triangle assertion facts.
- `tests/Projection.Tests/AxiomTests.fs` — `L3-Emission-LogicalTriangle (slice D.1.c)` citation entry.
