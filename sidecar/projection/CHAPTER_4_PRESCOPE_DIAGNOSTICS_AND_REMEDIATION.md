# Chapter 4.3 + 4.4 pre-scope — Operational Diagnostics V2 + RemediationEmitter

Pre-scope for chapters 4.3 (Operational Diagnostics V2) and 4.4 (RemediationEmitter). Both deliverables are thin compositions over substrate that earlier chapters land — chapter 4.3 over the existing `Diagnostics<'a>` writer; chapter 4.4 over the chapter-3 read-side adapter, DacpacEmitter, and CatalogDiff. They are combined in one document because together they fall well under the size of the other chapter-4 chapters and share a common integration point: `Projection.Pipeline` is the consumer that wires both into the operator-facing CLI.

## Strategic frame

Two load-bearing axes cut across both chapters and should be named at chapter-open before any slice begins:

1. **Substrate-already-shipped.** Chapter 4.3 emits artifacts whose payload (`Diagnostics<'a>` entries) the passes already produce; the work is *projection*, not new algebra. Chapter 4.4 composes the `Catalog -> Catalog -> RemediationDacpac` dispatch over a `CatalogDiff` primitive that chapter 3.5 ships and a DacpacEmitter that chapter 3.3 ships. Neither chapter adds new IR or new pass shapes. The chapter-open document should restate this so an over-eager scope expansion (a fourth diagnostic channel; a new diff axis) is rejected by reference rather than by argument.
2. **The operator surface is where V2 starts being load-bearing for humans.** Up through chapter 4.2 the only humans reading V2's output have been agents and test fixtures. Operational diagnostics and remediation are the first surfaces real cutover operators consult during a deploy. The discipline shifts: every JSON shape needs to be greppable with `jq`, every error needs to name a remedy, and the manifest of a remediation DACPAC must answer "what would running this do?" before the operator deploys it. This frame motivates the `decision-log` / `opportunities` / `validations` framing as the three operator channels (§1.4).

---

## Part 1 — Chapter 4.3 — Operational Diagnostics V2

### §1.1 Scope

Three new sibling Π's under `Projection.Targets.OperationalDiagnostics`, each emitting one V2 equivalent of a V1 operator-facing JSON artifact:

- `DecisionLogEmitter` → `decision-log.json` (V2's name; V1 ships it as `policy-decisions.json` + `policy-decision-report.json` — see §1.2 below).
- `OpportunitiesEmitter` → `opportunities.json`.
- `ValidationsEmitter` → `validations.json`.

Each emitter is `Emitter<JsonElement>` (where `Emitter<'a>` is the `Catalog -> Result<ArtifactByKind<'a>, EmitError>` shape that chapter 3 lands per `VISION_REVIEW.md` Appendix H §H.4). Each consumes the existing `Diagnostics<'a>` payload that running passes already produce (`Lineage<Diagnostics<UniqueIndexDecisionSet>>` from `UniqueIndexPass.run`; the parallel shapes from `NullabilityPass.run` and `ForeignKeyPass.run`); each projects a different facet of the same underlying entries into the JSON shape that surface expects.

The work is **not** to add new diagnostic content. The passes already emit the entries (`Source`, `Severity`, `Code`, `Message`, `SsKey`, `Metadata`); chapter 4.3 routes those entries to three named files.

What chapter 4.3 does **not** do:
- Does not add new diagnostic codes (the passes own those).
- Does not introduce a new writer monad or a fourth channel (see §1.4).
- Does not own SQL remediation script rendering — that's V1's `OpportunityLogWriter`'s embedded responsibility and it is **not** carried into V2 (the V2 surface is JSON only; a future emitter can produce remediation SQL if a real consumer demands it).

### §1.2 V1 schemas — the differential oracle

Citations resolve against the trunk at `/home/user/outsystems-ddl-exporter/src/`.

**`policy-decisions.json` (V1's decision-log).** Shape from `PolicyDecisionLogWriter.WriteAsync` at `src/Osm.Pipeline/Orchestration/PolicyDecisionLogWriter.cs:36–88` (record types at lines 110–168). Top-level fields:

```
ColumnCount, TightenedColumnCount, RemediationColumnCount      # int
UniqueIndexCount, UniqueIndexesEnforcedCount,                  # int
  UniqueIndexesRequireRemediationCount
ForeignKeyCount, ForeignKeysCreatedCount                        # int
ColumnRationales         : Dict<string, int>     # rationale → count
UniqueIndexRationales    : Dict<string, int>
ForeignKeyRationales     : Dict<string, int>
ModuleRollups            : Dict<moduleName, ModuleDecisionRollup>
TogglePrecedence         : Dict<string, ToggleExportValue>
Columns                  : List<{Schema, Table, Column, MakeNotNull, RequiresRemediation, Rationales[], Module}>
UniqueIndexes            : List<{Schema, Table, Index, EnforceUnique, RequiresRemediation, Rationales[], Module}>
ForeignKeys              : List<{Schema, Table, Column, CreateConstraint, ScriptWithNoCheck, Rationales[], Module}>
Diagnostics              : List<{LogicalName, CanonicalModule, CanonicalSchema, CanonicalPhysicalName,
                                  Code, Message, Severity, ResolvedByOverride, Candidates[]}>
PredicateCoverage        : SsdtPredicateCoverage      # opaque-from-V2's-perspective; document as a "carried-through" field
```

V1 actually emits **two** files in tandem: `policy-decisions.json` (the log; record types `PolicyDecisionLogColumn`, `PolicyDecisionLogUniqueIndex`, `PolicyDecisionLogForeignKey`, `PolicyDecisionLogDiagnostic`, `PolicyDecisionLogDuplicateCandidate` at PolicyDecisionLogWriter.cs:110–168) and `policy-decision-report.json` (the structured `PolicyDecisionReport` from `Osm.Validation.Tightening.PolicyDecisionReporter.cs:64–76`, serialized verbatim). The two share the same underlying `PolicyDecisionSet` payload but differ in the post-processing the writer applies (the `*Log*` shape is flattened for human reading; the `*Report*` shape preserves the in-memory record structure for downstream tooling like `policy-decision-link-builder`).

**The V2 prompt names this artifact `decision-log.json`.** Recommendation: V2 ships **one** file, `decision-log.json`, whose shape is a shallow superset of V1's `policy-decisions.json` flat form, indexed by SsKey. The two-file split V1 ships is a V1 implementation detail (separate writers, separate rendering passes); V2's `Diagnostics<'a>` writer carries what both V1 files express. Document the V1↔V2 file-count divergence in the chapter-4.3 `DECISIONS.md` entry per the **DECISIONS is for resolved questions** discipline.

**`opportunities.json`.** Shape from `OpportunityLogWriter.WriteAsync` at `src/Osm.Pipeline/Orchestration/OpportunityLogWriter.cs:76–82` (serializes `OpportunitiesReport` directly via `JsonSerializer.Serialize`). The record at `src/Osm.Validation/Tightening/Opportunities/OpportunitiesReport.cs:6–13`:

```
Opportunities          : ImmutableArray<Opportunity>
DispositionCounts      : Dict<OpportunityDisposition, int>
CategoryCounts         : Dict<OpportunityCategory, int>
TypeCounts             : Dict<OpportunityType, int>
RiskCounts             : Dict<RiskLevel, int>
GeneratedAtUtc         : DateTimeOffset
```

`Opportunity` is the rich record at `src/Osm.Validation/Tightening/Opportunity.cs:86–102` carrying `Type`, `Title`, `Summary`, `Risk`, `Disposition`, `Category`, `Evidence[]`, `Column`, `Index`, `Schema`, `Table`, `ConstraintName`, `Statements[]`, `Rationales[]`, `EvidenceSummary`, `Columns[]`. `OpportunityType` (`Nullability | UniqueIndex | ForeignKey`), `OpportunityDisposition` (`Unknown | ReadyToApply | NeedsRemediation`), `OpportunityCategory` (`Contradiction | Recommendation | Validation`) — all at lines 10–51.

V2 binary outcomes (`UniqueIndexOutcome.EnforceUnique` / `DoNotEnforce`) collapse V1's "EnforceUnique + RequiresRemediation" combination. This is documented at `UniqueIndexPass.fs:96–113`. The chapter 4.3 differential against V1's opportunities surface must account for this — V2 emits one Warning per `DoNotEnforce`; V1 emits an Opportunity per "model wants but data doesn't support." The shapes converge on the per-decision disposition; the per-record content differs.

**`validations.json`.** Shape at `src/Osm.Validation/Tightening/Validations/ValidationReport.cs:7–15`:

```
Validations    : ImmutableArray<ValidationFinding>
TypeCounts     : Dict<OpportunityType, int>
GeneratedAtUtc : DateTimeOffset
```

`ValidationFinding` at `Validations/ValidationFinding.cs:8–19` is a strict structural subset of `Opportunity` — same fields minus `Risk`, `Disposition`, `Category`, `Statements`, `EvidenceSummary` — built via `ValidationFinding.FromOpportunity`. It is the "validations" channel: opportunities that profiling observed and confirmed. The V1 writer also sorts findings by Schema/Table/ConstraintName/Type/Title via `ValidationFindingComparer.Instance` (`OpportunityLogWriter.cs:458–505`); V2's deterministic ordering (sort by SsKey root) is a deliberate divergence — `Skip` test stub naming the rationale per the V2 test discipline.

### §1.3 V2 emitter shapes

Three modules under `src/Projection.Targets.OperationalDiagnostics/`:

```fsharp
namespace Projection.Targets.OperationalDiagnostics

[<RequireQualifiedAccess>]
module DecisionLogEmitter =
    /// V2's decision-log: one per-SsKey record per pass-produced decision,
    /// joined to its Lineage trail (so the audit reads "which pass version
    /// emitted this decision, with what rationale, against what evidence")
    /// and its Diagnostics entries (so the log carries the Warning the
    /// pass emitted alongside the decision).
    val emit :
        Catalog
        -> Lineage<Diagnostics<UniqueIndexDecisionSet>>
        -> Lineage<Diagnostics<NullabilityDecisionSet>>
        -> Lineage<Diagnostics<ForeignKeyDecisionSet>>
        -> Result<ArtifactByKind<JsonElement>, EmitError>

[<RequireQualifiedAccess>]
module OpportunitiesEmitter =
    /// V2's opportunities log: every Diagnostics entry whose Severity is
    /// Warning *and* whose Code carries the `tightening.*.opportunity`
    /// prefix (or Code contains "duplicates"/"orphans"/"nulls" — TBD at
    /// slice 3 trace-fixture). Routes by Code prefix, not by severity
    /// alone (Severity is a structural property; opportunity-vs-validation
    /// is a routing property over Code).
    val emit :
        Catalog
        -> Diagnostics<unit> list           // accumulated across passes
        -> Result<ArtifactByKind<JsonElement>, EmitError>

[<RequireQualifiedAccess>]
module ValidationsEmitter =
    /// V2's validations log: every Diagnostics entry whose Code carries
    /// the `tightening.*.validation` prefix — "the pass observed this
    /// invariant held with evidence."
    val emit :
        Catalog
        -> Diagnostics<unit> list
        -> Result<ArtifactByKind<JsonElement>, EmitError>
```

Each emitter's `Result<ArtifactByKind<JsonElement>, EmitError>` shape is exactly the shape `VISION_REVIEW.md` Appendix H §H.4 codifies; T11 commutativity follows by type construction (every Catalog kind appears as a key in the `ArtifactByKind`).

The three emitters share a **routing table** that decides which entry goes to which artifact, keyed by Code prefix. Recommended convention (lands as a small private module):

```
tightening.*.opportunity.*       → opportunities.json
tightening.*.validation.*        → validations.json
tightening.*  (everything else)  → decision-log.json
adapter.*    (boundary errors)   → decision-log.json
emitter.*    (Π-time errors)     → decision-log.json
```

The Code-prefix routing is a single point of decision for which artifact each entry lands in; this dissolves the "which channel does this entry belong to?" question that the chapter-2 deferred three-channel split would otherwise pose (see §1.4).

### §1.4 The three-channel split, deferred from chapter 2

`HANDOFF.md:65` lists "Three-channel Diagnostics split (operator/auditor/developer) — single channel sufficient at all chapter-2 consumers" as a lower-priority deferral. Chapter 4.3 is the natural place to re-examine the trigger: do operator-facing artifacts demand a structural split?

**Recommendation: no split.** The three V1 artifacts (`decision-log` / `opportunities` / `validations`) **are themselves the three channels**, named by V1 from years of operator pressure. They are descriptive of *what is being emitted*, not of *who consumes it*:
- `decision-log` carries every decision the system made (full audit; analogous to "auditor channel").
- `opportunities` carries actionable suggestions ("operator channel").
- `validations` carries pass-witnessed invariant confirmations ("developer channel" — confirms the system did what was expected).

Adopting this framing means **the existing `Diagnostics<'a>` writer remains single-channel**; routing happens at emit time via the Code-prefix table (§1.3). No `DiagnosticChannel` DU; no parallel writer. Three artifacts route from one stream.

This must be cashed out as a `DECISIONS.md` entry at chapter open, retiring the "Three-channel split" deferral with **Recommendation: refuse the split; the artifacts are the channels, route by Code prefix at emit time**. Per the discipline at `CLAUDE.md` ("Active deferrals re-checked at chapter close — silent-trigger fires get caught by table-scan, not by chronological re-read"), this active deferral fires under chapter 4.3 and should be retired (not silently resolved).

If a future operator surface (say, a streaming dashboard emitting only `Error` entries) demands a fourth axis, that's the trigger to re-examine. Today's three artifacts saturate the operator/auditor/developer cut.

### §1.5 Slice-by-slice breakdown for 4.3

Six slices, each closing one operator-visible artifact or its differential:

**Slice 1 — V1 schema documentation.** Goal: produce the differential-oracle reference. File: `sidecar/projection/CHAPTER_4_3_V1_DIFFERENTIAL.md` (or appended into the chapter-open doc). LOC: ~0 code, ~200 lines documentation. Acceptance: the three V1 record shapes documented field-by-field with file:line citations; the V1↔V2 schema deltas (e.g., V2 collapses V1's "EnforceUnique + RequiresRemediation" cases) named.

**Slice 2 — `DecisionLogEmitter` minimal.** Goal: emit `decision-log.json` for a single-pass single-kind Catalog. File: `src/Projection.Targets.OperationalDiagnostics/DecisionLogEmitter.fs`. LOC: ~150. Acceptance: `Lineage<Diagnostics<UniqueIndexDecisionSet>>` plus a one-Module/one-Kind/one-Index Catalog produces a JSON document containing one entry per decision; T1 byte-determinism property test holds; T11 every-SsKey-mentioned property test holds.

**Slice 3 — `OpportunitiesEmitter`.** Goal: route `tightening.*.opportunity.*`-prefixed entries to `opportunities.json`. File: `src/Projection.Targets.OperationalDiagnostics/OpportunitiesEmitter.fs`. LOC: ~120. Acceptance: trace-fixture-first (per `DECISIONS 2026-05-19`'s slice-level discipline) — first walk V1's `OpportunitiesReport` shape against an end-to-end fixture, then write the failing differential test, then implement. The Code-prefix routing-table primitive lands in shared `RoutingTable.fs` (~30 LOC) so OpportunitiesEmitter and ValidationsEmitter both consume it.

**Slice 4 — `ValidationsEmitter`.** Goal: route `tightening.*.validation.*`-prefixed entries to `validations.json`. File: `src/Projection.Targets.OperationalDiagnostics/ValidationsEmitter.fs`. LOC: ~80 (most plumbing already in slice 3's RoutingTable). Acceptance: differential test against V1's `ValidationReport` on a Catalog where the running passes produce both opportunity and validation entries; route correctly per Code prefix; ordering deterministic by SsKey root.

**Slice 5 — CLI wire-up in `Projection.Pipeline`.** Goal: the canary's CLI verb invokes the three emitters and writes the three files alongside the SSDT/DACPAC artifacts. File: `Projection.Pipeline/OperationalDiagnostics.cs` (C# in the canary project per `DECISIONS 2026-05-15`). LOC: ~120. Acceptance: integration test running the full canary loop against a fixture Catalog produces exactly three diagnostic files at the expected paths, byte-identical across two runs.

**Slice 6 — Differential test against V1 outputs.** Goal: the V1 envelope walk per chapter-close ritual item 8. Run V1 (existing trunk) and V2 (new emitters) against the same fixture Catalog; diff the three artifacts; account for every divergence per the chapter-2 three-class typology (lossiness / boundary-discipline / alternative-IR-surface). File: new test in `tests/Projection.Tests/OperationalDiagnosticsDifferentialTests.fs`. LOC: ~250 (mostly fixture). Acceptance: every divergence between V1's three artifacts and V2's three artifacts is either (a) named-and-documented (e.g., "V2 collapses EnforceUnique+Remediation"; deliberate divergence; `Skip` stub) or (b) bug-and-fixed.

Chapter total: ~750 LOC plus ~250 LOC test fixture plus documentation.

### §1.6 Test strategy for 4.3

- **Tier-1 pure properties (per the canary's property-test surface, chapter 3.4).**
  - `T1: same Diagnostics list -> byte-identical JSON` (FsCheck generated entries; expect deterministic output).
  - `T11: every Catalog kind appears as a top-level key in each ArtifactByKind` (the Appendix H §H.4 type-encoded variant; reduces to `Set.equal (Map.keys result) (Set.ofSeq (Catalog.allKinds c |> Seq.map _.SsKey))`).
  - **Routing partition property.** For every Diagnostics entry, exactly one of the three emitters claims it (no entry orphaned; no entry double-counted). Property test over generated entries with random Codes plus the Code-prefix table.
- **Differential against V1.** Slice 6 closes this as a permanent regression test. Named tolerances per chapter 3.1's comparator: V2 sorts by SsKey not by Schema/Table/ConstraintName; V2 collapses certain V1 enum permutations; document each tolerance in the test.
- **Trace-before-fixture per slice.** Per the chapter-2 codification, every slice traces V1's actual handling first, classifies into the three-class typology, then writes the failing test. `OpportunityLogWriter`'s embedded SQL rendering (`OpportunityLogWriter.cs:107–235`) is **boundary-discipline** class: V2 emits JSON only and routes SQL rendering to a future sibling Π if a consumer demands it; the V1 SQL rendering is won't-carry-forward.

---

## Part 2 — Chapter 4.4 — RemediationEmitter

### §2.1 Scope

Partial-state recovery primitive. The cutover-window failure mode the vision names but earlier chapters do not close (per `VISION_REVIEW.md` R5; Appendix B §B.6 — "Gap (prevention only)"): canary passes, prod deploy fails partway, leaving a hybrid state where some kinds are deployed and others are not. RemediationEmitter consumes the partially-deployed schema (via chapter 3.1's read-side adapter) and the V2-intended target Catalog, computes the diff (chapter 3.5's `CatalogDiff.between`), and emits the corrective DACPAC (chapter 3.3's `DacpacEmitter` over the diff slice).

The chapter delivers:
- The `RemediationEmitter.emit` function as a thin composition over read-side, CatalogDiff, and DacpacEmitter.
- A confirmation gate for subtractive remediation (DROP statements) — `--allow-subtractive` CLI flag plus an in-DACPAC manifest comment listing kinds being dropped.
- A promoted-lane integration test that reproduces a partial-deploy failure (drop column mid-deploy) and verifies remediation converges deployed → target.

Out of scope:
- New IR or new pass shapes.
- The data-side remediation (StaticSeeds / MigrationDependencies / Bootstrap) — chapter 4.1's data triumvirate owns its own redeploy-idempotence; a future RemediationEmitter widening could compose data-side remediation, but chapter 4.4 is schema-only.
- Cross-environment remediation (dev's deployed schema versus prod's target). Same shape; chapter 4.4's primitive composes; the policy of which-environment-vs-which is operator's, not the emitter's.

### §2.2 The composition

```fsharp
namespace Projection.Targets.SSDT

type RemediationDacpac = {
    Bytes   : byte[]                  // the corrective DACPAC bytes
    Diff    : CatalogDiff             // what the diff says was missing
    Lineage : Lineage<unit>           // audit trail of decisions made
                                       // (which kinds added/removed/renamed)
    Manifest : RemediationManifest    // operator-readable summary
}
and RemediationManifest = {
    AddedKinds       : SsKey list      // CREATE will fire for these
    RemovedKinds     : SsKey list      // DROP will fire (gated)
    RenamedKinds     : Map<SsKey, RenameRecord>   // refactor.log + ALTER
    UnchangedKinds   : SsKey list      // not in the remediation
    SubtractiveAllowed : bool          // operator gate
}

type RemediationError =
    | CatalogDiffFailed of EmitError
    | DacpacEmitFailed of EmitError
    | DiffIsEmpty                                  // already in sync
    | SubtractiveRequiresConfirmation of dropCount: int
                                                   // DROP requested but
                                                   // --allow-subtractive not set

[<RequireQualifiedAccess>]
module RemediationEmitter =
    /// Emit the corrective DACPAC for a partial-state recovery.
    ///
    /// Composition: chapter 3.1's read-side produces `deployed`; chapter
    /// 3.3's DacpacEmitter consumes the diff slice; chapter 3.5's
    /// CatalogDiff partitions source ∪ target into Renamed / Added /
    /// Removed / Unchanged.
    ///
    /// Subtractive (Removed) remediation is gated; the default is
    /// additive-only. Operator passes `subtractiveAllowed = true` after
    /// reviewing the manifest's RemovedKinds list.
    let emit
        (deployed: Catalog)
        (target: Catalog)
        (subtractiveAllowed: bool)
        : Result<RemediationDacpac, RemediationError> =
        match CatalogDiff.between deployed target with
        | Error e -> Error (CatalogDiffFailed e)
        | Ok diff when CatalogDiff.isEmpty diff ->
            Error DiffIsEmpty
        | Ok diff when not subtractiveAllowed
                       && not (Set.isEmpty diff.Removed) ->
            Error (SubtractiveRequiresConfirmation (Set.count diff.Removed))
        | Ok diff ->
            // Additive slice + (gated) subtractive slice composed into
            // a single Catalog the DacpacEmitter consumes.
            let remediationCatalog = CatalogDiff.toRemediationCatalog diff
            DacpacEmitter.emit remediationCatalog
            |> Result.map (fun bytes ->
                { Bytes    = bytes
                  Diff     = diff
                  Lineage  = lineageFor diff
                  Manifest = manifestFrom diff subtractiveAllowed })
            |> Result.mapError DacpacEmitFailed
```

`CatalogDiff.toRemediationCatalog` is a small helper inside `Projection.Core/Verification/CatalogDiff.fs` (chapter 3.5 lands the type; chapter 4.4 adds the projection helper). It converts the diff partition into a Catalog whose Modules contain only the kinds that need corrective DDL:
- `Added` SsKeys → kinds copied from `target`, emitted by DacpacEmitter as CREATE.
- `Renamed` SsKeys → kinds copied from `target` plus refactor.log entries (the existing chapter 3.5 `RefactorLogEmitter` runs in parallel; DacpacEmitter sees an ALTER-shape because DacFx's deploy with a refactor log issues ALTER not DROP+CREATE).
- `Removed` SsKeys → kinds copied from `deployed` (since they're not in target), emitted as DROP via DacpacEmitter's pass-through. The `[<RequireQualifiedAccess>]` of that "drop-only" emit shape is the additional surface the chapter adds (DacpacEmitter today emits CREATE / ALTER; the DROP path is not yet exercised).
- `Unchanged` SsKeys → not included; the remediation DACPAC is intentionally smaller than a full target DACPAC.

### §2.3 What "remediation" means concretely

Per the partition above:
- **Added in target but missing in deployed** → emit CREATE (or ALTER ADD COLUMN when the missing item is sub-kind, e.g., a column on an existing table). This is the always-safe additive class. `Diagnostics` entry on the lineage trail per added kind: `Source = "emitter:Remediation"`, `Severity = Info`, `Code = "remediation.added"`, `SsKey = Some k.SsKey`.
- **Removed in target but present in deployed** → emit DROP. Data-loss surface. Always with `IF EXISTS` guard (idempotence). Always behind the `subtractiveAllowed` gate. `Severity = Warning`. The diagnostic entry's Metadata carries `dropPriorRowCount` if profile evidence is available (so the operator can review "this drop will lose N rows").
- **Renamed** → refactor.log entry plus ALTER (DacFx's deploy interprets the refactor log; without it a renamed kind is a DROP-old + CREATE-new pair, which is data-loss). The chapter 3.5 `RefactorLogEmitter` produces the refactor.log XML; chapter 4.4 ensures the RemediationDacpac carries it. `Severity = Info`.
- **Unchanged** → not in remediation. The remediation DACPAC explicitly lists Unchanged in its Manifest.UnchangedKinds for operator audit, but the bytes don't reference those kinds at all.

The remediation DACPAC's `PackageMetadata.Description` is operator-readable (e.g., `"Remediation: 3 kinds added, 1 renamed, 0 removed; based on diff between deployed schema and V2 target c4a9b1..."`). This is the "manifest names the diff explicitly so the operator can audit before deploying" property. The operator inspects the manifest first, confirms intent, then runs `DacServices.Deploy`.

### §2.4 The "DROP requires confirmation" mechanic

Two-layer gate:

1. **In-DACPAC.** Every DROP statement in the remediation DACPAC's bytes is preceded by a top-of-file comment block listing the kinds being dropped, the operator (set at emit time from the CLI invocation), and the diff hash. Pure documentation; not enforced; survives `DacServices.GenerateDeployScript` so operator-readable in either preview or apply.

2. **At-emit.** `RemediationEmitter.emit` returns `Error (SubtractiveRequiresConfirmation count)` if the diff contains any `Removed` SsKeys and `subtractiveAllowed = false`. The CLI surface defaults to `subtractiveAllowed = false`; the operator passes `--allow-subtractive` to override. The error shape carries the count so the CLI can render `"Remediation requires --allow-subtractive: 4 kinds will be dropped"`.

Recommendation: ship layer (2) at chapter 4.4; defer layer (1)'s top-of-file comment to a follow-on slice if a real consumer demands it. Layer (2) is structural (the type system carries the gate); layer (1) is operational documentation.

The CLI surface defaults to **additive-only** because additive remediation is always-safe (no data loss), the default failure mode for a partial deploy is "additive failed partway" (DROP isn't the failure mode the cutover scenario worries about — DROP is `target` Catalog's intent, and if the operator hasn't reviewed the target's drop list before this point, the cutover process has bigger problems).

### §2.5 Slice-by-slice breakdown for 4.4

Four slices:

**Slice 1 — `RemediationEmitter.emit` minimal: additive-only.** Goal: consume `(deployed: Catalog) (target: Catalog) (subtractiveAllowed: false)`, produce a remediation DACPAC for the additive case (`Added` + `Renamed`); reject if `Removed` is non-empty. File: `src/Projection.Targets.SSDT/RemediationEmitter.fs`. LOC: ~180 (primarily `CatalogDiff.toRemediationCatalog` + manifest construction). Acceptance: tier-1 pure property test holds (`(deployed, target, allow) -> RemediationDacpac` is deterministic); empty diff produces `Error DiffIsEmpty`; non-empty Removed with `allow = false` produces `Error (SubtractiveRequiresConfirmation _)`.

**Slice 2 — Refactor.log composition.** Goal: when diff contains Renamed, the remediation DACPAC carries the refactor.log XML alongside (DacFx's deploy applies it, producing ALTER not DROP+CREATE). File: extend `RemediationEmitter.fs` to compose `RefactorLogEmitter` from chapter 3.5. LOC: ~60. Acceptance: integration test where deployed has a kind named `Foo`, target has the same `SsKey` named `Bar`; emit remediation; deploy via `DacServices.Deploy` against an ephemeral SQL Server; assert deployed's table is renamed (not dropped-and-created). This is the chapter-3.5-and-3.3 integration point.

**Slice 3 — Subtractive support.** Goal: `subtractiveAllowed = true` allows the gate to open and DROP statements to flow through. File: extend `RemediationEmitter.fs` and `DacpacEmitter.fs` (DacpacEmitter today doesn't exercise the DROP path; chapter 4.4 adds it). LOC: ~120 across both files. Acceptance: tier-2 container property test — deploy `target` to an ephemeral SQL Server; mutate target by removing one kind; emit remediation with `subtractiveAllowed = true`; deploy remediation; assert deployed schema matches new (smaller) target. The drop must use `DROP ... IF EXISTS` (idempotence; second remediation run is a no-op).

**Slice 4 — Promoted-lane integration test.** Goal: the cutover-failure-mode end-to-end test. File: `tests/Projection.Tests/RemediationPromotedLaneTests.fs` (or in `Projection.Pipeline.Tests` per the canary's project layout). LOC: ~200 fixture-heavy. Acceptance: the property `deploy target -> corrupt schema in N ways -> run RemediationEmitter -> deploy the remediation DACPAC -> assert deployed schema matches target` holds for N ∈ {drop-column-halfway, alter-type-halfway, drop-table-halfway, FK-violation-halfway}. Each corruption mode is a generator producing a "partially-failed deploy" state; the property is convergence under remediation. This is the chapter's tier-3 closure and the strongest evidence that the partial-state-recovery gap from `VISION_REVIEW.md` Appendix B §B.6 is closed.

Chapter total: ~360 LOC plus ~200 LOC test fixture. Smaller than 4.3 because the substrate is heavier-already-shipped.

### §2.6 Test strategy for 4.4

- **Tier-1 pure property: emit determinism.** Same `(deployed, target, allow)` triple → same `RemediationDacpac.Bytes` (modulo the byte-determinism caveat for DACPAC binaries from chapter 3.3 — the property reduces to model-API equality via `DacPackage.Load` round-trip per `CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md` §1).
- **Tier-2 container property: convergence.** `deploy target -> apply random subset of forward DDL (the partial-deploy model) -> remediate -> deploy remediation -> compare to target via read-side adapter, assert equal-by-SsKey`. ~30 cases per property; runs in CI. The property generator is small: the partial-deploy mutator drops a random subset of the target's CREATE statements; remediation must add them back.
- **Tier-3 integration: real partial-deploy failure modes.** Hand-curated `[<Theory>]` capturing the failure shapes operators have seen (or could plausibly see in the cutover scenario): DROP halfway, ALTER halfway, FK violation halfway, mixed-rename-and-drop halfway. Every shrunk failure from the tier-2 property gets pinned here as a regression. Runs nightly.
- **Property: subtractive gate is structural.** `Set.isEmpty diff.Removed = false && subtractiveAllowed = false ⇒ result is Error (SubtractiveRequiresConfirmation _)`. This is the load-bearing safety property; it prevents the "operator forgot to review drops" failure mode from silently producing a data-destroying DACPAC. Tier-1 pure.

The convergence property is the chapter's signature claim: a remediation DACPAC, applied to a partially-deployed schema, deterministically converges to the target. If the property doesn't hold at chapter close, R5 in `VISION_REVIEW.md` is unresolved.

---

## §3 (Cross-cutting) — Files inventory

### Created

- `src/Projection.Targets.OperationalDiagnostics/Projection.Targets.OperationalDiagnostics.fsproj` (new project; sibling to `Projection.Targets.Json`, `Projection.Targets.SSDT`, `Projection.Targets.Distributions`).
- `src/Projection.Targets.OperationalDiagnostics/RoutingTable.fs` (Code-prefix routing primitive shared across the three diagnostic emitters; ~30 LOC).
- `src/Projection.Targets.OperationalDiagnostics/DecisionLogEmitter.fs` (~150 LOC).
- `src/Projection.Targets.OperationalDiagnostics/OpportunitiesEmitter.fs` (~120 LOC).
- `src/Projection.Targets.OperationalDiagnostics/ValidationsEmitter.fs` (~80 LOC).
- `src/Projection.Targets.SSDT/RemediationEmitter.fs` (~360 LOC across slices 1–3).
- `tests/Projection.Tests/OperationalDiagnosticsDifferentialTests.fs` (~250 LOC; chapter 4.3 slice 6).
- `tests/Projection.Tests/OperationalDiagnosticsPropertyTests.fs` (T1, T11, routing-partition; ~150 LOC).
- `tests/Projection.Tests/RemediationEmitterTests.fs` (tier-1 pure properties; ~120 LOC).
- `tests/Projection.Tests/RemediationPromotedLaneTests.fs` (tier-2/3 in the canary's project per its layout; ~200 LOC).
- `Projection.Pipeline/OperationalDiagnostics.cs` (CLI wire-up; ~120 LOC).
- `Projection.Pipeline/Remediation.cs` (CLI wire-up for `remediation` verb; ~80 LOC).

### Modified

- `src/Projection.Core/Verification/CatalogDiff.fs` (chapter 3.5 lands; chapter 4.4 adds `toRemediationCatalog : CatalogDiff -> Catalog` helper; ~30 LOC).
- `src/Projection.Targets.SSDT/DacpacEmitter.fs` (chapter 3.3 lands; chapter 4.4 slice 3 adds the DROP-emit path the chapter-3 minimal slice did not exercise; ~50 LOC).
- `Projection.sln` — register the new `Projection.Targets.OperationalDiagnostics` project.
- `DECISIONS.md` — three new entries: chapter-4.3 three-channel-split retirement; chapter-4.3 V1↔V2 file-count divergence (V1's two-file split versus V2's one `decision-log.json`); chapter-4.4 subtractive gate.
- `ADMIRE.md` — V1 `OpportunityLogWriter` and `PolicyDecisionLogWriter` move from `admiring` to `extracted (chapter-4.3 close)` with the SQL-rendering won't-carry-forward note.
- `AXIOMS.md` — no amendments anticipated; both chapters operate within the existing axioms.
- `HANDOFF.md` — chapter-4 close letter (when the rest of chapter 4 closes; not chapter-4.3/4.4-specific).

---

## §4 (Cross-cutting) — Risks and dependencies

### Dependencies

**Chapter 4.3 depends on:**
- V1's `policy-decisions.json` / `opportunities.json` / `validations.json` shapes being well-understood. Resolved by §1.2's V1 schema documentation.
- The `ArtifactByKind<'element>` and `Emitter<'a>` types from chapter 3 (Appendix H §H.4 refactor). If chapter 3 has not landed those types when 4.3 opens, 4.3's `Emitter<JsonElement>` shape uses the legacy `Catalog -> string` form per `JsonEmitter` precedent and migrates when the refactor lands. Sequencing: prefer to open chapter 4 after chapter 3 closes the type-system refactor; if not, accept the legacy-emitter-shape interim.
- The Diagnostics Code-prefix convention being adopted by all V2 passes (`UniqueIndexPass.fs:122–132` already follows it for `tightening.uniqueIndex.*`; NullabilityPass and ForeignKeyPass need the same convention if they don't already; verify at chapter open via grep).

**Chapter 4.4 depends on:**
- Chapter 3.1 (read-side adapter producing the deployed Catalog).
- Chapter 3.3 (DacpacEmitter producing the corrective bytes).
- Chapter 3.5 (CatalogDiff type plus `CatalogDiff.between`; RefactorLogEmitter for the rename composition).
- The `ArtifactByKind` refactor (same caveat as chapter 4.3; the RemediationEmitter's signature is most natural over `ArtifactByKind` — emit the corrective DACPAC for exactly the diff's keys).

The strongest sequencing constraint: **chapter 4.4 cannot ship before chapter 3.5 ships CatalogDiff**. Chapter 4.3 is independent of chapter 3.5 and could ship in parallel with it.

### Risks

1. **4.3's V1 differential surfaces a deliberate divergence operators reject.** V2's binary outcome (e.g., `UniqueIndexOutcome.EnforceUnique` / `DoNotEnforce`) drops V1's "EnforceUnique + RequiresRemediation" intermediate state. If real operator workflows depend on the intermediate state showing in `policy-decisions.json`, V2's collapse-to-binary breaks that workflow. Mitigation: slice 6's V1-envelope-walk surfaces this before chapter close; if the operator demand is real, V2 widens the outcome DU (per IR-grows-under-evidence) rather than reshaping the emitter.

2. **4.3's three-channel-no-split decision turns out wrong.** A future streaming-dashboard consumer (parking-lot today) might want `Severity = Error` entries on a separate channel. Mitigation: §1.4's framing is reversible — the existing `Diagnostics<'a>` writer is single-channel, and a fourth artifact (e.g., `errors.json`) routes by `Severity = Error` independent of Code prefix. The chapter's three-artifacts-as-three-channels decision doesn't foreclose a fourth artifact later.

3. **4.4's subtractive-remediation is the data-loss surface.** Emitting wrong DROPs is catastrophic — losing tables in production is the worst-case the cutover scenario worries about. **Mitigations, layered**:
   - Operator confirmation gate (§2.4 layer 2; structural; default-deny).
   - In-DACPAC manifest comment (§2.4 layer 1; documentation; surfaces in `GenerateDeployScript`).
   - Tier-2 property test covering Removed cases (the convergence property; if remediation DROPs the wrong tables, deployed-equals-target fails).
   - Tier-3 hand-curated regression test for every shrunk failure.
   - Per-DROP `IF EXISTS` (idempotence; second deploy is a no-op even if the operator runs the remediation twice).

4. **4.4's tier-2 container property test is testcontainers-heavy.** ~30 cases × 3 deploys per case (target / corrupt / remediate) at ~150ms per deploy ≈ 13.5 seconds per property. Tolerable if the test runs in CI not pre-commit; ensure the chapter scopes the test to CI, not pre-commit, per the canary's tiered policy from chapter 3.4.

5. **4.4's RefactorLog composition (slice 2) is the chapter-3.5 integration risk.** If chapter 3.5's RefactorLogEmitter shape diverges from what RemediationEmitter expects, slice 2 forces a chapter 3.5 amendment. Mitigation: the chapter-4.4 chapter-open document re-reads chapter 3.5's shipped shape and confirms the integration point before slice 1 lands.

6. **4.3's V1 envelope walk (slice 6) takes longer than expected.** V1's two-file split (`policy-decisions.json` + `policy-decision-report.json`) plus the `OpportunityLogWriter`'s embedded SQL rendering create ~four V1 surfaces to walk; the chapter-2 close rituals' V1-envelope-walk discipline applies. Budget slice 6 at ~2 sessions, not one.

### Critical Files for Implementation

- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Core/Diagnostics.fs` (the Diagnostics + LineageDiagnostics writer substrate the three diagnostic emitters consume).
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Core/Passes/UniqueIndexPass.fs` (canonical example of the `Lineage<Diagnostics<_>>` pass shape; the emitters consume the same shape from NullabilityPass and ForeignKeyPass).
- `/home/user/outsystems-ddl-exporter/src/Osm.Pipeline/Orchestration/PolicyDecisionLogWriter.cs` (V1's `policy-decisions.json` differential oracle; lines 36–168 for the shape).
- `/home/user/outsystems-ddl-exporter/src/Osm.Pipeline/Orchestration/OpportunityLogWriter.cs` (V1's `opportunities.json` + `validations.json` differential oracle; lines 49–97 for emit; 442–505 for the validations-sort comparator).
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Targets.SSDT/RawTextEmitter.fs` (existing sibling Π precedent; chapter 3.3's DacpacEmitter and chapter 4.4's RemediationEmitter both extend the SSDT project, both follow this module's `[<RequireQualifiedAccess>]` shape).
