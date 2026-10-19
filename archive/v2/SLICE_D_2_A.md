# Slice D.2.a — Elegant constraint formatting (V1-shape multi-line)

**Status**: shipped 2026-05-23. Opens chapter D's second arc (emission aesthetics). `Projection.Targets.SSDT.ConstraintFormatter` reformats ScriptDom's column-inline single-line constraints into V1's multi-line elegant shape — column / constraint name / constraint body, three indentation levels. Carbon-copied from V1's `Osm.Smo.PerTableEmission.ConstraintFormatter` with adaptation for V2's ScriptDom-shaped input.

## Why this slice

The chapter-D logical-name emission arc (D.1.a/b/c) closed last commit; V2 now emits operator-meaningful identifiers (`[Customer]` instead of `[OSUSR_ABC_CUSTOMER]`). With the names landed, the next operator-visible axis is the **layout** of each CREATE TABLE. V2's ScriptDom-default emission packs PK / DEFAULT / FK onto the column line:

```sql
CREATE TABLE [dbo].[Customer] (
    [Id]       INT            NOT NULL CONSTRAINT [PK_dbo_Customer] PRIMARY KEY CLUSTERED,
    [Email]    NVARCHAR (MAX) NULL,
    [IsActive] BIT            NOT NULL CONSTRAINT [DF_Customer_IsActive] DEFAULT 1
)
```

V1's elegance — three-level indentation, constraint name on its own line, constraint body indented further — makes the table's structural relationships immediately scannable:

```sql
CREATE TABLE [dbo].[Customer] (
    [Id]       INT            NOT NULL
        CONSTRAINT [PK_dbo_Customer]
            PRIMARY KEY CLUSTERED,
    [Email]    NVARCHAR (MAX) NULL,
    [IsActive] BIT            NOT NULL
        CONSTRAINT [DF_Customer_IsActive] DEFAULT 1
)
```

Same SQL semantically; vastly more elegant to read. The operator-PO flagged the gap explicitly: V1's pipeline has multi-level tabs for primary keys, foreign keys, and defaults; V2 doesn't.

## Architectural shape

**Carbon-copy with input adaptation.** V1's `ConstraintFormatter.cs` is the source; V2's `ConstraintFormatter.fs` is the F# port. The two formatters share the multi-line output shape and indentation conventions but differ on input expectations:

| Input shape | V1 (SMO) | V2 (ScriptDom) |
|---|---|---|
| Column-inline PK | `CONSTRAINT [pk] PRIMARY KEY` on its own line | `[col] TYPE NOT NULL CONSTRAINT [pk] PRIMARY KEY CLUSTERED,` (column line) |
| Column-inline DEFAULT | `DEFAULT (value)` on its own line | `[col] TYPE NULL CONSTRAINT [df] DEFAULT value,` (column line) |
| Table-level FK | `CONSTRAINT [fk] FOREIGN KEY ... REFERENCES ...` on its own line | Same |

The F# port adds column-inline detection for PK + DEFAULT (where V1's formatter wouldn't fire because CONSTRAINT isn't at line start) and reuses V1's logic for table-level FK (where input shapes already match).

**Terminal text-emission boundary.** The formatter operates AFTER `Render.toText` accumulates ScriptDom-rendered statements into a StringBuilder. Per pillar 7 amendment four-question analysis: ScriptDom's `Sql160ScriptGenerator` has no per-constraint formatting option; subclassing was considered + rejected (visibility lift cost too high for the single consumer); text post-processing IS the canonical fit at this boundary.

## Three patterns recognised

**Column-inline PRIMARY KEY** (`[col] TYPE NOT NULL CONSTRAINT [pk] PRIMARY KEY [CLUSTERED]`):
- Output: 3 lines. Column (4 chars indent) / `CONSTRAINT [name]` (8 chars) / `PRIMARY KEY CLUSTERED,` (12 chars).

**Column-inline named DEFAULT** (`[col] TYPE NULL CONSTRAINT [df] DEFAULT value`):
- Output: 2 lines. Column (4 chars indent) / `CONSTRAINT [name] DEFAULT value,` (8 chars).

**Table-level FOREIGN KEY** (`CONSTRAINT [fk] FOREIGN KEY (cols) REFERENCES table (cols) [ON DELETE x] [ON UPDATE y]`):
- Output: 3 + (0-2) lines. `CONSTRAINT [name]` (4 chars) / `FOREIGN KEY (cols) REFERENCES table (cols)` (8 chars) / `ON DELETE x` (12 chars) / `ON UPDATE y` (12 chars).
- V1's NO ACTION normalization preserved: if exactly one of ON DELETE / ON UPDATE is present, the other defaults to `ON DELETE NO ACTION` / `ON UPDATE NO ACTION`. If both are NO ACTION, both drop (server default).

Other lines pass through unchanged: CREATE TABLE / column-only definitions / CHECK constraints / ALTER statements / EXEC sys.sp_addextendedproperty / blank lines.

## Test surface

- **Existing snapshot tests** absorb the reformatting cleanly. The multi-line shape is structurally identical SQL (semantic equivalence preserved); ScriptDom can re-parse the formatted output without issue.
- **H-050 adjunction-property** tests continue green — the SetExtendedProperty + CreateIndex statements pass through unchanged; only column-inline constraint lines and table-level FK lines reformat.
- **Docker-bound canary tests** (M3 V2-internal closure, M3 wide canary, D.1.c triangle canary) pass green — the reformatted SQL deploys and round-trips identically through the ephemeral SQL Server.
- **Full test suite**: 2370 pass, 0 fail, 207 skipped (+1 from prior 2369; the additional pass is the AdjunctionLawTests' axis discovering the multi-line shape preserves structural equality).

## Decisions resolved

**Text post-processing over ScriptDom subclassing.** Subclassing `Sql160ScriptGenerator` to control column-inline formatting would require either reflection-based access to private formatter state (V2 doesn't reach into runtime metaprogramming per the F# feature surface) OR a full re-emission via a custom IL-level generator (visibility-lift cost orders of magnitude higher than the operator-visible benefit). Text post-processing at the `Render.toText` terminal boundary is the canonical fit; it's exactly the boundary the LINT-ALLOW substantive-rationale discipline names.

**Carbon-copy V1 with citation, not re-derive.** V1's `ConstraintFormatter.cs` already solves the bulk of the problem (table-level FK with ON DELETE / ON UPDATE NO ACTION normalization). Per `DECISIONS 2026-05-16 (later) — V2 self-containment + carbon-copy editorial inheritance`, V2 carbon-copies the V1 source into V2's domain-structured location, cites the V1 source in a file-header comment + an `ADMIRE.md` row, and refactors freely from there. The F# port preserves V1's indentation conventions (4 / 8 / 12 spaces) and the NO ACTION normalization logic; the input-shape adaptation (column-inline detection for PK + DEFAULT) is the V2-growth delta.

**No `EXECUTE sys.sp_addextendedproperty` reformatting.** V1's fixture shows extended-property EXEC statements with multi-line `@level0type` / `@level1type` / `@level2type` clauses on separate lines; V2 currently emits them as single long lines. Deferred — the slice's scope is constraint formatting (PK / FK / DEFAULT, per operator request). If the operator surfaces preference for extended-property reformatting, extend `ConstraintFormatter` with a parallel branch.

**No anonymous-DEFAULT formatting.** V2's IR has `Attribute.DefaultName : Name option`; when None, ScriptDom may emit `DEFAULT (value)` without the `CONSTRAINT [name]` prefix. The formatter's column-inline detection scans for `" CONSTRAINT ["` which wouldn't fire on the anonymous shape. Deferred — no current V2 fixture exercises anonymous defaults; the realistic operator-reality source uses named defaults exclusively. If anonymous defaults surface in a future fixture, extend `tryFormatLine` with a `" DEFAULT ("`-keyword detection branch.

## Discipline reinforced

- **Carbon-copy with citation discipline.** V2 inherits V1's `ConstraintFormatter` source as the editorial donor; the F# port carries a file-header citation comment + an `ADMIRE.md` entry. Mirrors the precedent set by prior carbon-copy events (`EntitySeedDeterminizer` chapter 4.1.B; `OssysSqlExtractor` chapter B.3.1).
- **LINT-ALLOW substantive-rationale on text-emission boundary.** Each indentation literal in the F# port carries an inline LINT-ALLOW with substantive rationale (the 4 / 8 / 12 space conventions are carbon-copied from V1's `ConstraintFormatter.cs`; the four-question analysis lands in the file-header comment).
- **Pillar 8 domain-first naming on the carbon-copy.** The V2 module name (`ConstraintFormatter`) matches V1's; per the carbon-copy convention, V1 vocabulary applies until V2's domain articulation produces a name that better captures the concept. The action-shaped `*Formatter` suffix is borderline but mirrors V1; refactor in follow-up if pillar-8 pressure surfaces.

## Cross-references

- `src/Projection.Targets.SSDT/ConstraintFormatter.fs` — NEW; F# port of V1's `ConstraintFormatter.cs`.
- `src/Projection.Targets.SSDT/Render.fs:122-130` — wire-in at `Render.toText` terminal boundary.
- `src/Projection.Targets.SSDT/Projection.Targets.SSDT.fsproj:16` — compile order (between `ScriptDomGenerate.fs` and `Render.fs`).
- `ADMIRE.md` — NEW entry documenting the V1 source + V2-growth delta (input-shape adaptation).
- V1 reference: `src/Osm.Smo/PerTableEmission/ConstraintFormatter.cs` — the source of the carbon-copy.
- V1 reference fixture: `tests/Fixtures/emission/edge-case/Modules/AppCore/dbo.Customer.sql` — the elegant V1 output shape the F# port targets.
