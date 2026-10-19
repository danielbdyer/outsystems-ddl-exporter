# Slice D.1.a — Logical-name emission as default

**Status**: shipped 2026-05-23. Two default-on Core passes substitute the catalog's logical names (`Kind.Name` / `Attribute.Name`) into the physical-realization slots the SSDT emitter reads (`Kind.Physical.Table` / `Attribute.Column.ColumnName`). V2 emits `[dbo].[Customer]([Email])` by default instead of `[dbo].[OSUSR_ABC_CUSTOMER]([EMAIL])`.

**Scope (this sub-slice)**: the substitution mechanism only. ReadSide-side recovery + canary triangle assertion ship as D.1.b + D.1.c.

## What this slice answers

The principal-PO question that opened chapter D was: "why is V2 emitting OSUSR_*-shaped names? operators see SSDT artifacts; the artifacts should carry operator-meaningful identifiers." The catalog already carries both the logical name (`Kind.Name = "Customer"`, derived from V1 metadata) and the physical name (`Kind.Physical.Table = "OSUSR_ABC_CUSTOMER"`, the deployed identifier). The emission layer reads only the physical side. The slice closes the gap by **substituting** the logical value into the physical-realization slot pre-emit, without changing what the emitter reads.

## Not a rename

The two new passes are deliberately NOT named `*Rename`. A rename authors a new name — `old → new` is a creative act, the operator's choice of what to call something. `TableRename` (the existing pass) IS a rename: operators supply `RenameSpec { Key; Target }` pairs that introduce target names not in the catalog. The new passes author nothing — both axes already exist; the substitution aligns the physical-realization slot with the logical name. The naming distinction is structural: passes that author names share the `*Rename` suffix; passes that substitute pre-existing names share the `*Emission` suffix per the operator-visible effect.

## Architectural shape

Two Core passes, mirror-shaped, both default-on, both classified `OperatorIntent of Emission`:

| Pass | Substitutes | Lineage variant |
|---|---|---|
| `LogicalTableEmission` | `Kind.Physical.Table = Name.value k.Name` | `PhysicallyRenamed` (existing; reused) |
| `LogicalColumnEmission` | `Attribute.Column.ColumnName = Name.value a.Name` | `ColumnPhysicallyRenamed` (new closed-DU widening) |

**Order in `RegisteredTransforms.allChainSteps`**: both passes run AFTER `TableRename` so operator-supplied physical pinnings (when present) dominate the logical substitution. The substitution short-circuits whenever `Name.value k.Name = k.Physical.Table` (which is exactly the case after an operator pin lands), so the two semantics compose without conflict.

**Mode parameter**: `Enabled | Disabled`. Production chain wires `Enabled` for both; `Disabled` is a no-op pass-through preserved for diagnostic / V1-parity fallback. The classification (`OperatorIntent Emission`) is invariant of mode — default-on IS the operator's intent in 2026; operators that want physical emission opt out.

**Length guard**: SQL Server identifier limit is 128 chars. If `Name.value k.Name` exceeds this, the kind's `Physical` is left unchanged (defensive boundary; source catalogs from OSSYS never produce such names in practice).

**Identity preservation (A1)**: only `Kind.Physical.Table` / `Attribute.Column.ColumnName` are touched. `SsKey`, `Name`, `Physical.Catalog`, `Physical.Schema`, `IsNullable`, every other field — byte-identical. The compiler enforces this — neither pass writes to any other field.

## Closed-DU widening

`TransformKind.ColumnPhysicallyRenamed of detail: ColumnRename` is the first new variant in `TransformKind` since `PhysicallyRenamed` landed for `TableRename`. The widening absorbed cleanly through every match site in the codebase (all existing matches had `_` wildcard fallthrough). New typed payload:

```fsharp
type ColumnRename = {
    Kind   : TableId   // owning kind coordinate at substitution time
    Before : string    // prior ColumnRealization.ColumnName
    After  : string    // new ColumnRealization.ColumnName
}
```

Plus `ColumnRename.toDiagnosticString` projection (`schema.table[before -> after]`) for the diagnostic surface.

## What downstream consumers see

Every emission site that reads `k.Physical.Table` / `k.Physical.Schema` / `a.Column.ColumnName` (17 sites across `Projection.Targets.SSDT/`; mapped at slice open) now produces logical-name output without any emitter-layer changes. The three derived names that compose from physical tokens (PK name `PK_<schema>_<table>`; FK name `FK_<table>_<targetTable>_<sourceColumn>`; manifest path `Modules/<module>/<schema>.<table>.sql`) also produce logical-shaped results — the substitution propagates through the existing composition formulas mechanically.

Worked example: a catalog with logical `Customer` / physical `OSUSR_ABC_CUSTOMER`, logical `Email` / physical `EMAIL`:

```sql
-- before slice D.1.a
CREATE TABLE [dbo].[OSUSR_ABC_CUSTOMER] (
    [ID] INT NOT NULL,
    [EMAIL] NVARCHAR(MAX) NOT NULL,
    CONSTRAINT [PK_dbo_OSUSR_ABC_CUSTOMER] PRIMARY KEY ([ID])
);
-- File: Modules/Sales/dbo.OSUSR_ABC_CUSTOMER.sql

-- after slice D.1.a (production default)
CREATE TABLE [dbo].[Customer] (
    [Id] INT NOT NULL,
    [Email] NVARCHAR(MAX) NOT NULL,
    CONSTRAINT [PK_dbo_Customer] PRIMARY KEY ([Id])
);
-- File: Modules/Sales/dbo.Customer.sql
```

## Test surface

- `tests/Projection.Tests/LogicalNameEmissionTests.fs` — 16 facts covering both passes individually + composed end-to-end through `Pass.compose`. Disciplines under test: identity preservation (A1), event count, payload shape, classification (pillar 9), pass-through under `Disabled`, no-op under aligned logical=physical.
- `tests/Projection.Tests/AxiomTests.fs` — `L3-Emission-Logical (slice D.1.a)` citation entry citing the integration test above.
- Updated tests: `EmissionFoldersOverlayTests` (6 facts), `EndToEndPipelineTests.M1` (1 fact), `RegisteredTransformsTests` (3 facts; counts shifted 22→24 metadata / 17→19 chain).

**Full test suite**: 2359 pass, 0 fail, 207 skipped (+17 from prior baseline of 2342).

## What this slice does NOT do

- **No ReadSide recovery.** ReadSide's `Name.create table` still derives `Kind.Name` from whatever physical name SQL Server's INFORMATION_SCHEMA returns. When V2 deploys logical-name SSDT and then reads it back, ReadSide produces `Kind.Name = "Customer"` and `Kind.Physical.Table = "Customer"` — no record of the original logical-vs-physical divergence survives the roundtrip. Slice D.1.b lifts this: V2 emits a `V2.LogicalName` extended property carrying the pre-substitution logical name; ReadSide queries it and hydrates `Kind.Name` from it (with backward-compat fallback when absent).
- **No canary triangle assertion.** Today's operator-reality canary uses a pure-physical fixture (`canary-gate.sql` / `SourceSchema.realistic`) where logical = physical. The substitution is a no-op on that fixture and the canary continues to pass trivially. Slice D.1.c augments the fixture with logical-name extended properties, amends `PhysicalSchema` to carry a `LogicalNameBinding` set, and adds the triangle assertion (`source.Kind.Name = target.Kind.Name = target.Kind.Physical.Table`) to the comparator.
- **No CLI override.** Operators today get the production default (`Enabled`); flipping to `Disabled` requires source-level wiring. A CLI flag (`--no-logical-emission` or equivalent) is operator-overlay-axis territory and ships when an operator-pull surfaces; the slice's mechanism supports it (the `Mode` parameter is the seam).
- **No operator-supplied column-rename machinery.** No `ColumnRename` generic equivalent of `TableRename` ships (per "IR grows under evidence" — today's only consumer is logical-name emission, which `LogicalColumnEmission` owns directly). When a second consumer surfaces (operator pinning individual column names; chapter 4.9 territory), the generic infrastructure lands as a sibling pass.

## Decisions resolved

- **Substitution is operator intent, not data intent.** Even though both name axes already exist in the catalog (the substitution doesn't introduce new evidence), the CHOICE to align physical with logical is the operator's emission-axis intent. Default-on production wiring IS the operator's intent — the operator (in 2026) wants logical names; physical-name emission is the diagnostic / V1-parity fallback.
- **`Enabled` / `Disabled` mode parameter over runtime config injection.** The mode is captured at registration time (`LogicalTableEmission.registered Enabled`) rather than via a runtime axis on `Policy` or a separate config section. Per "IR grows under evidence" — one consumer today (the production chain); no operator-pull yet for runtime toggle. The mode parameter is the seam if/when that pull surfaces.
- **Module names `LogicalTableEmission` / `LogicalColumnEmission` over `*RenameToLogical`.** Per principal-PO feedback during slice D.1.a: "rename infers a replacement, when we're not doing anything but swapping in another variable." Naming follows the operator-visible effect ("logical emission") rather than the misleading mechanism word ("rename"). Concept-shaped, ubiquitous-language-friendly, sibling-friendly with the existing `*Rename` family (which IS a rename).
- **`ColumnPhysicallyRenamed` lineage variant kept symmetric with `PhysicallyRenamed`.** The lineage TransformKind describes the structural effect (a physical name slot was rewritten), not the operator-visible product framing. At the audit-trail layer "rename" reads naturally for "the old value was replaced"; the naming concern applies at the module / product layer.

## Discipline reinforced

- **Domain-first naming (pillar 8).** Caught a misnomer carryover at slice mid-implementation — the original module names mirrored the existing `TableRename`'s shape without applying the four-question domain-naming analysis. The PO's flag triggered a rename before the slice landed; the slice doc records the substitution-vs-rename distinction so future slices that add similar passes don't recur the misnomer.
- **Closed-DU expansion empirical-test discipline.** Adding `ColumnPhysicallyRenamed` to `TransformKind` light up zero match sites — every existing matcher had `_` wildcard fallthrough. The pre-PR check (compile + tests) is the discipline's enforcement; passed without intervention.
- **IR grows under evidence, not speculation.** No generic `ColumnRename` machinery shipped despite the symmetry argument with `TableRename`. Today's one consumer (logical-name emission) is owned by `LogicalColumnEmission` directly; the generic infrastructure lands at the second consumer per the two-consumer threshold.

## Cross-references

- `src/Projection.Core/Lineage.fs:160-209` — `PhysicalRename` payload (existing); `ColumnRename` payload + `ColumnRename.toDiagnosticString` (new).
- `src/Projection.Core/Lineage.fs:235-251` — `TransformKind.ColumnPhysicallyRenamed` (new variant).
- `src/Projection.Core/Passes/LogicalTableEmission.fs` — NEW; default-on emission-axis pass for kind-level substitution.
- `src/Projection.Core/Passes/LogicalColumnEmission.fs` — NEW; default-on emission-axis pass for attribute-level substitution.
- `src/Projection.Core/RegisteredTransforms.fs:52-56,90-94,156-160` — wiring into `all` / `allChainSteps` / `allChainStepsFor` (all three surfaces).
- `tests/Projection.Tests/LogicalNameEmissionTests.fs` — NEW; 16 facts.
- `tests/Projection.Tests/AxiomTests.fs` — `L3-Emission-Logical (slice D.1.a)` citation entry.
- `tests/Projection.Tests/EmissionFoldersOverlayTests.fs` — 6 facts updated to assert logical-name paths.
- `tests/Projection.Tests/EndToEndPipelineTests.fs` — `M1` updated to assert `CREATE TABLE [dbo].[User]`.
- `tests/Projection.Tests/RegisteredTransformsTests.fs` — 3 facts updated to reflect new pass counts.
