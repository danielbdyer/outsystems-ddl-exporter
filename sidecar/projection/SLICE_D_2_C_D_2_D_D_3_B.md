# Slice D.2.c + D.2.d + D.3.b (combined XXXXXL slice)

**Status**: shipped 2026-05-23 as one architectural arc. Three closely-related slices that land together because they share the realization-layer-boundary discipline that D.3.c codifies.

- **D.3.b — `ConstraintFormatter` as registered emission-overlay**: Mode parameter + `registeredMetadata` carrying `OperatorIntent Emission` across eight sites; added to `RegisteredAllTransforms.all`.
- **D.2.c — GO batch separators**: new `Statement.BatchSeparator` variant + ScriptDom dispatch + `SsdtDdlEmitter.statements` wraps every top-level statement with trailing separator.
- **D.2.d — Trigger disable + comments**: new `Statement.AlterTableDisableTrigger` variant + per-trigger metadata `Comment` + post-CREATE disable when `Trigger.IsDisabled`.

Plus an audit-only **D.2.f** confirming temporal emission (H-022) was already complete; no code change needed.

## Why one slice

The three sub-slices share a common substrate: every new emission-aesthetic transformation now lands ALREADY-REGISTERED through the existing `RegisteredAllTransforms.all` surface. D.3.b establishes the precedent (registering the constraint formatter); D.2.c + D.2.d benefit from it (the new patterns extend the same registration shape). The realization-layer-boundary discipline that D.3.c codifies in the DECISIONS log applies uniformly across all three.

## What landed (Architecture)

### D.3.b — Registered emission-overlay

`ConstraintFormatter` is now a fully-classified pillar-9 transformation:

```fsharp
type Mode = Enabled | Disabled
let format (mode: Mode) (script: string) : string = ...
let registeredMetadata : RegisteredTransformMetadata = ...
```

Eight sites enumerated in `registeredMetadata`, each carrying `OperatorIntent Emission` + substantive rationale per pillar 9: `columnInlinePrimaryKey`, `columnInlineNamedDefault`, `columnInlineAnonymousDefault`, `columnInlineCheck`, `tableLevelForeignKey`, `tableLevelPrimaryKey`, `tableLevelCheck`, `extendedPropertyExecWrap`.

Mode parameter threads through `Render.toText`; production wiring sets `Enabled` (mirrors the slice-D.1.a `LogicalTableEmission.Enabled` + `LogicalColumnEmission.Enabled` default-on precedent). Operators that want raw ScriptDom emission for diagnostic / V1-parity-bisect reasons explicitly pass `Disabled`.

`ConstraintFormatter.registeredMetadata` added to `RegisteredAllTransforms.all` so the canary manifest's `applied-transforms` field surfaces it. Closes the architectural-totality gap surfaced in the prior session's perspective discussion.

### D.2.c — GO batch separators

New `Statement.BatchSeparator` variant (closed-DU widening; absorbed by `_` wildcards in pattern-match sites except one — `Deploy.executeStream`, which got an explicit no-op branch matching `Blank`'s shape because the per-statement DDL flush happens before BatchSeparator emits).

`Render.toSql` emits `[blank line]\nGO\n` (V1's per-statement-group convention). `ScriptDomBuild.buildStatement` returns `None` (sqlcmd directives have no ScriptDom AST equivalent). `DacpacEmitter` excludes from schema-statement classification.

`SsdtDdlEmitter.statements` introduces a `yieldWithSeparator` helper that wraps every top-level statement (CREATE TABLE / CREATE INDEX / ALTER / SetExtendedProperty / CreateTrigger / CreateSequence) with a trailing `BatchSeparator`. The canary deploy path absorbs the GO statements via `BatchSplitter.splitWithLoudFallback` — pre-existing GO recognition rule splits the rendered text into per-segment `ExecuteNonQueryAsync` round-trips.

### D.2.d — Trigger disable + comments

New `Statement.AlterTableDisableTrigger of table: TableId * triggerName: string` variant. `ScriptDomBuild.buildAlterTableDisableTrigger` produces `AlterTableTriggerModificationStatement` with `TriggerEnforcement.Disable` (sibling to the existing `buildAlterTableNoCheckConstraint` shape).

`SsdtDdlEmitter.triggerStatements` extended from "emit one CreateTrigger per trigger" to "emit `Comment(metadata) + CreateTrigger + (optional) AlterTableDisableTrigger`." Per-trigger output now:

```sql
-- Trigger: TR_JobRun_AUDIT (disabled: true)
CREATE TRIGGER [dbo].[TR_JobRun_AUDIT] ON [dbo].[JobRun] AFTER INSERT AS BEGIN SET NOCOUNT ON; END

GO

ALTER TABLE [dbo].[JobRun] DISABLE TRIGGER [TR_JobRun_AUDIT]

GO
```

Mirrors V1's `tests/Fixtures/emission/edge-case-untrusted/Modules/Ops/dbo.JobRun.sql:17-19` shape exactly.

### D.2.f — Temporal completeness audit (no change)

Confirmed `ScriptDomBuild.buildCreateTable` already emits PERIOD FOR SYSTEM_TIME (line 487-490) + SystemVersioningTableOption (line 492-505) with HistoryTable + RetentionPeriod. H-022 (Cluster A) closed this axis at chapter A.0' slice η. No work needed; subagent's "partial emission" flag was a false alarm.

## What's NOT in this slice

- **D.2.e — ALTER WITH NOCHECK ADD CONSTRAINT semantic rework**: deferred. V2's current shape (FK inside CREATE TABLE + post-ALTER CHECK CONSTRAINT) is semantically equivalent to V1's standalone ALTER WITH NOCHECK ADD CONSTRAINT — the deployed state matches. Operator-visible textual divergence remains; rework would touch emission-order rework + new Statement variant. Deferred unless an operator surfaces the preference; the V2 shape is structurally correct, just textually different from V1.
- **D.3.c — DECISIONS codification** of the realization-layer-boundary discipline: shipped inline as the DECISIONS entry below (no separate slice doc).
- **Lineage events on formatter sites**: the formatter still doesn't emit `LineageEvent`s when it fires per CONSTRAINT line; the registered metadata describes its sites + classification but per-invocation lineage emission would require either a writer-monad refactor of `Render.toText` (currently `string → string`) or a side-channel. Deferred — the pillar 9 site classification gap is closed; the per-invocation event-emission gap is a separate concern.

## Test surface

- **2370 pass, 0 fail, 207 skipped** — unchanged from D.2.b baseline. All snapshot tests absorbed the additional emission cleanly (GO separators + trigger comments are structurally additive; ScriptDom re-parses identically).
- **All Docker-bound canary tests pass green** — M3 V2-internal closure (`Deploy.runWithReadback` path with new GO separators) + M3 wide canary + D.1.c triangle canary. `BatchSplitter` handles GO recognition; ReadSide deploy + readback round-trips identically.

## Decisions resolved

**Realization-layer transformations DO carry pillar-9 classification, but as METADATA-ONLY registrations**. The full `RegisteredTransform<'In, 'Out>` shape requires `Run : 'In -> Lineage<Diagnostics<'Out>>`; realization-layer transformations operate on `string -> string` (text post-processors) which doesn't fit. Per the SSDT emitter precedent (`SsdtDdlEmitter.registeredMetadata`), realization-layer overlays register as metadata only — the totality-coverage scan + manifest's `applied-transforms` field see them; per-invocation execution happens at the realization-layer call site (e.g., `Render.toText`). This preserves pillar 9's classification contract WITHOUT forcing every text-level transformation through the writer-monad shell.

**Mode parameter on realization-layer overlays mirrors slice-D.1.a's pattern**. `LogicalTableEmission.Mode = Enabled | Disabled`; `LogicalColumnEmission.Mode = Enabled | Disabled`; `ConstraintFormatter.Mode = Enabled | Disabled`. Production wiring captures `Enabled` (default-on; matches operator's 2026 intent). `Disabled` is the diagnostic / V1-parity-bisect surface. Same shape across catalog-level + realization-level overlays.

**GO as a typed Statement variant, not a text post-processor**. `Statement.BatchSeparator` adds structural information to the stream — the canary deploy path's `BatchSplitter` can rely on it; the Render layer's emission contract is typed. Alternative (text-level post-processor inserting `\nGO\n` between rendered statements) was considered + rejected per the closed-DU expansion discipline + A35 (typed statement stream is canonical).

**Trigger disable + comment as two statements, not one composite**. V2's `Statement.Comment` already exists; reusing it for the metadata line keeps the variant count stable. The `AlterTableDisableTrigger` Statement is new (no equivalent in Blank / Comment / existing variants); its presence opens the type-system surface for future ENABLE TRIGGER if an operator-pull surfaces it (today only Disable; the `TriggerEnforcement` enum supports both).

## Discipline reinforced

- **Closed-DU expansion empirical-test discipline**: adding `BatchSeparator` + `AlterTableDisableTrigger` to the Statement DU produced exactly TWO exhaustiveness errors (both at `Deploy.executeStream`'s match site). All other match sites had `_` wildcards or covered the new variants via shape-based dispatch. The discipline holds.
- **Carbon-copy V1 with citation**: trigger emission shape carbon-copied from V1's `tests/Fixtures/emission/edge-case-untrusted/Modules/Ops/dbo.JobRun.sql:17-19`. ADMIRE.md entry updated.
- **Pillar 9 totality without forcing the typed shell**: the metadata-only registration pattern (precedent: SSDT emitter) IS the canonical fit for realization-layer transformations. Registry sees them; manifest sees them; the executable typed-Run surface stays where the writer-monad makes sense.

## Cross-references

- `src/Projection.Targets.SSDT/ConstraintFormatter.fs` — Mode + registeredMetadata added.
- `src/Projection.Targets.SSDT/Render.fs:122-135` — Mode threaded through `toText`.
- `src/Projection.Targets.SSDT/Statement.fs:262-321` — `BatchSeparator` + `AlterTableDisableTrigger` variants.
- `src/Projection.Targets.SSDT/ScriptDomBuild.fs:1291-1310,1437-1473` — dispatch for new variants + `buildAlterTableDisableTrigger`.
- `src/Projection.Targets.SSDT/SsdtDdlEmitter.fs:397-417,672-712` — trigger metadata-comment + post-CREATE disable; `yieldWithSeparator` helper.
- `src/Projection.Targets.SSDT/DacpacEmitter.fs:72-75` — `isSchemaStatement` covers `AlterTableDisableTrigger`; excludes `BatchSeparator`.
- `src/Projection.Pipeline/Deploy.fs:745-805` — `executeStream` no-op branch for `BatchSeparator`; `AlterTableDisableTrigger` DDL dispatch.
- `src/Projection.Pipeline/RegisteredAllTransforms.fs:53-59` — `ConstraintFormatter.registeredMetadata` appended.
- V1 references: `src/Osm.Smo/PerTableEmission/ConstraintFormatter.cs`; `tests/Fixtures/emission/edge-case/Modules/AppCore/dbo.Customer.sql`; `tests/Fixtures/emission/edge-case-untrusted/Modules/Ops/dbo.JobRun.sql`.
