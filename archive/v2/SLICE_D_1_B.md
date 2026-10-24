# Slice D.1.b — V2.LogicalName extended-property roundtrip

**Status**: shipped 2026-05-23. V2 emits a `V2.LogicalName` extended property on every CREATE TABLE + every column carrying the catalog's logical name. ReadSide queries `sys.extended_properties` for the property and hydrates `Kind.Name` / `Attribute.Name` from its value. Backward-compat fallback to `Name.create deployed_name` when the property is absent.

**Scope (this sub-slice)**: end-to-end logical-name recovery through deploy → ReadSide read. Canary triangle assertion (D.1.c) follows.

## What this slice answers

After slice D.1.a, V2 emits logical-shaped SSDT (`[dbo].[Customer]`). But the catalog's logical-vs-physical divergence (`Kind.Name = "Customer"` distinct from `Kind.Physical.Table = "OSUSR_ABC_CUSTOMER"`) doesn't survive a deploy → read roundtrip — ReadSide derives `Kind.Name` directly from the deployed table name via `Name.create table`. This slice carries the logical name through the deployed schema via a SQL Server extended property and recovers it on readback.

## Two ends of the change

**Emitter (`SsdtDdlEmitter.fs:472-500`).** `extendedPropertyStatements` extended to emit two new unconditional statements per kind:
- Table-level: `EXEC sys.sp_addextendedproperty @name = N'V2.LogicalName', @value = N'Customer', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'<deployed-table-name>'`
- Column-level (per attribute): same property, `@level2type = N'COLUMN', @level2name = N'<deployed-column-name>'`

The value carried is `Name.value k.Name` / `Name.value a.Name` — the LOGICAL name. After slice D.1.a's substitution, the logical name is also the deployed table name (so the property is technically redundant in the default chain). The property earns its keep when:
- The catalog passes through `LogicalTableEmission.Disabled` (diagnostic / V1-parity emission); deployed name is physical, property carries the logical.
- An operator pins `TableRename` (corrected D.1.b chain order — pins now dominate the logical substitution); deployed name is operator-chosen, property still carries the original logical from the catalog.
- Future ReadSide-driven comparison flows (the canary's D.1.c triangle assertion) need to know what V2 considered "logical" independently of what landed in the deployed identifier.

**Reader (`ReadSide.fs:300-345, 622-664, 727-895`).** Three changes:
1. `readSchemaCombined` gains a 5th SQL batch joining `sys.extended_properties` (`class = 1` for OBJECT_OR_COLUMN; `name = N'V2.LogicalName'`) with `sys.tables` + `LEFT JOIN sys.columns` so table-level (`minor_id = 0`) and column-level rows arrive in one round-trip envelope. Result-set walker partitions into `tableLogicalNames : Map<(schema, table), string>` and `columnLogicalNames : Map<(schema, table, column), string>`.
2. `buildKind` accepts both maps; hydrates `Kind.Name` from `tableLogicalNames` when present, falls back to `Name.create table` (the prior behavior) when absent.
3. `buildAttribute` accepts `columnLogicalNames`; hydrates `Attribute.Name` from the per-column entry when present, falls back to `Name.create row.Column` otherwise.

The single-round-trip optimization established at chapter 3.6 is preserved — the property query lands as a fifth batch on the existing combined command, not as a separate `SqlCommand`.

## Chain-order correction (preparatory D.1.a fix)

D.1.a wired `LogicalTableEmission` + `LogicalColumnEmission` AFTER `TableRename` in `allChainSteps` / `allChainStepsFor`. The pass docstring claimed "operator pins dominate" — but with the substitution running last, operator-supplied `TableRename` specs got OVERWRITTEN by the catalog-driven logical substitution. Caught during D.1.b planning when the contradiction between the docstring and the chain order became visible.

**Fix**: both logical-emission passes now run BEFORE `TableRename`. The substitution lands first; `TableRename` (the operator's explicit-override surface) writes to `Kind.Physical` last and dominates. The substitution still applies for kinds the operator hasn't pinned, so the production default behavior is preserved.

No tests exercised the conflict (TableRename ships with empty default specs; no in-flight slice exercises a non-empty rename list through the chain), so the correction landed without test failures. Future tests that exercise the conflict will find it correctly ordered.

## What downstream consumers see

**Same as D.1.a for SSDT bundle bodies, plus**: each per-kind file's `EXEC sys.sp_addextendedproperty` block grows by `1 + N` statements (one table-level V2.LogicalName + N column-level V2.LogicalName entries where N is the attribute count). Worked example:

```sql
CREATE TABLE [dbo].[Customer] (
    [Id] INT NOT NULL,
    [Email] NVARCHAR(MAX) NOT NULL,
    CONSTRAINT [PK_dbo_Customer] PRIMARY KEY ([Id])
);

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Customer',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE',  @level1name = N'Customer';

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Id',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE',  @level1name = N'Customer',
    @level2type = N'COLUMN', @level2name = N'Id';

EXECUTE [sys].[sp_addextendedproperty] @name = N'V2.LogicalName', @value = N'Email',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE',  @level1name = N'Customer',
    @level2type = N'COLUMN', @level2name = N'Email';
```

**ReadSide-recovered catalogs** for V2-emitted schemas carry the original logical name in `Kind.Name` / `Attribute.Name` — distinct from the deployed physical when the substitution was disabled or an operator pin was applied. ReadSide-recovered catalogs for non-V2-emitted schemas (no `V2.LogicalName` property) fall back to the prior behavior (`Kind.Name = deployed name`).

## Test surface

- `tests/Projection.Tests/LogicalNameRoundtripTests.fs` — NEW; 6 facts total.
  - **Unit** (3 facts): SSDT body contains `V2.LogicalName` for table + column; emits unconditionally even when logical = physical.
  - **Integration** (3 facts; Docker-bound via `EphemeralContainerFixture`): full source → emit (with substitution Disabled to preserve divergence) → deploy → ReadSide read → assert recovered `Kind.Name` / `Attribute.Name` match originals while `Kind.Physical.Table` / `Attribute.Column.ColumnName` preserve the deployed names; backward-compat fallback test for non-V2-emitted plain CREATE TABLE.
- `tests/Projection.Tests/AxiomTests.fs` — `L3-Emission-LogicalRoundtrip (slice D.1.b)` citation entry.
- Updated tests: `SsdtExtendedPropertyEmissionTests` (2 facts narrowed — the unconditional V2.LogicalName emission means assertions on "no sp_addextendedproperty present" had to narrow to "no MS_Description present"); `ModuleExtendedPropertyEmissionTests` (2 facts updated — schema-segment counts now isolate the module-property's distinctive value instead of raw `@level0type = N'SCHEMA'` occurrences).

**Full test suite**: 2365 pass, 0 fail, 207 skipped (+6 from prior 2359).

## What this slice does NOT do

- **No canary triangle assertion.** The operator-reality canary (`canary-gate.sql` / `SourceSchema.realistic`) is still pure-physical; the substitution is a no-op on its fixture. D.1.c augments the fixture with logical-name extended properties so the source catalog carries `Kind.Name` distinct from `Kind.Physical.Table` (using D.1.b's recovery on the source side), amends `PhysicalSchema` to carry a `LogicalNameBinding` set, and adds the triangle assertion to the diff comparator.
- **No CLI override for the extended-property emission.** Operators get the unconditional emission; flipping to off requires source-level wiring (no `ExtendedProperty.V2LogicalName.Disabled` config axis). Per "IR grows under evidence" — one consumer today (ReadSide recovery); no operator-pull for runtime toggle.
- **No bench impact analysis.** The extra batch added to `readSchemaCombined` is well within the single-round-trip envelope; per-canary-readback perf delta should be negligible. D.1.c's perf-gate re-record will capture any actual impact when the canary fixture changes shape.
- **No reflection / FK / index recovery extension.** ReadSide still doesn't reconstruct triggers, table-level CHECK constraints, or entity-level extended properties (chapters A.0' slices γ + ε + ζ territory). The slice ONLY extends the name-recovery axis.

## Decisions resolved

- **Property name: `V2.LogicalName`.** The `V2.` namespace prefix prevents collision with operator-supplied extended properties (a hypothetical future operator who carries a "LogicalName" annotation on their tables for unrelated reasons doesn't collide). Short canonical name; reads naturally in `sys.extended_properties` queries; the SQL Server reserved name space for system properties starts with `MS_` so `V2.` is safely distinct.
- **Emit unconditionally, not conditionally.** Even when `Name.value k.Name = k.Physical.Table` (post-substitution), the property still emits. Trade: every CREATE TABLE adds 1 + N extended-property statements. Benefit: ReadSide doesn't need to distinguish "property absent → fallback" from "property absent → genuinely no divergence" — absence always means "pre-D.1.b deployment or non-V2 schema." Robustness > emission size.
- **5th batch on `readSchemaCombined`, not a separate query.** Preserves the chapter-3.6 single-round-trip optimization. The query joins `sys.extended_properties` with `sys.tables` / `sys.columns` so table-level + column-level entries arrive in one result set, partitioned client-side by `IsDBNull 2` (the `column_name` column is NULL for table-level entries via LEFT JOIN).
- **Backward-compat fallback to `Name.create deployed_name`.** Pre-D.1.b deployed schemas and non-V2-emitted schemas continue to round-trip via the prior behavior. ReadSide's contract widens (lookup property first, fall back second); no V1-shape catalog stops working.
- **Chain reorder for `LogicalTableEmission` / `LogicalColumnEmission` to run BEFORE `TableRename`.** Corrects D.1.a's stated-but-not-implemented "operator pins dominate" contract. Substitution lands first (catalog-driven default); operator `TableRename` writes last and dominates where present. No tests exercise the conflict today; future tests find it correctly ordered.

## Discipline reinforced

- **Read-the-substrate-before-committing.** D.1.a documented "operator pins dominate" without verifying the chain order. D.1.b's pre-work walk through `RegisteredTransforms.allChainSteps` surfaced the contradiction immediately. Apply the discipline pre-emptively: when a slice docstring asserts a structural property (ordering, dominance, precedence), walk the substrate to confirm.
- **Wiring-without-downstream-consumer is a valid slice shape (Chapter C contribution).** D.1.b's emission lands without an immediate consumer for the `V2.LogicalName` property in the canary path — the canary triangle assertion arrives at D.1.c. The unit-level emission tests + the integration-level ReadSide roundtrip tests validate the mechanism; the production-shape consumer (canary) follows. Same shape as chapter C's `Policy.Insertion` binder + the chapter C sibling.
- **Test-failure capture protocol (TRX-first).** Four failures surfaced after the emission change; TRX log identified them in seconds, classified them into two clusters (descriptive assertions and count assertions), both fixable inline.

## Cross-references

- `SLICE_D_1_A.md` — the predecessor slice; substitution mechanism; carve-out for D.1.b / D.1.c.
- `src/Projection.Core/RegisteredTransforms.fs:80-99,138-160` — chain reorder (logical emission BEFORE `TableRename` in both `allChainSteps` + `allChainStepsFor`).
- `src/Projection.Core/Passes/LogicalTableEmission.fs:14-22` — docstring updated to reflect corrected ordering.
- `src/Projection.Targets.SSDT/SsdtDdlEmitter.fs:472-500` — `extendedPropertyStatements` extended with table-level + column-level `V2.LogicalName` entries.
- `src/Projection.Adapters.Sql/ReadSide.fs:300-345` — `buildAttribute` accepts `columnLogicalNames`; hydrates `Attribute.Name` with property-first fallback.
- `src/Projection.Adapters.Sql/ReadSide.fs:622-664` — `buildKind` accepts both maps; hydrates `Kind.Name` with property-first fallback.
- `src/Projection.Adapters.Sql/ReadSide.fs:727-895` — `readSchemaCombined` 5th batch; result-set walker partitions table-level vs column-level entries via LEFT JOIN nullness.
- `tests/Projection.Tests/LogicalNameRoundtripTests.fs` — NEW; 6 facts (3 unit + 3 Docker-bound roundtrip).
- `tests/Projection.Tests/AxiomTests.fs` — `L3-Emission-LogicalRoundtrip (slice D.1.b)` citation entry.
- `tests/Projection.Tests/SsdtExtendedPropertyEmissionTests.fs` — 2 facts narrowed.
- `tests/Projection.Tests/ModuleExtendedPropertyEmissionTests.fs` — 2 facts updated.
