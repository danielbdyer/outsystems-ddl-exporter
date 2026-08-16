# Emission/realization plane — alignment audit workpaper

Auditor A6. Scope: `src/Projection.Targets.*` (shared emitter contract), the emitter seams
in `src/Projection.Core` (`ArtifactByKind`, `EmissionMode`, the typed statement-stream),
the realization selector in `src/Projection.Pipeline` (`ReverseLegRealization`), and the
movement surfaces' emission vocabulary (`MovementSpec.fs`). All paths below are relative to
`/home/user/outsystems-ddl-exporter/sidecar/projection/` unless absolute.

## 1 Vocabulary inventory (with file anchors)

**The shared Π contract (Core).**
- `Emitter<'e> = Catalog -> Result<ArtifactByKind<'e>, EmitError>`; `EmitterWithProfile<'e>`;
  `EmitterOverDiff<'e>` — src/Projection.Core/Types.fs:50-64. Codomain uniform across every
  sibling; input arity grows per evidence axis (A18 amended).
- `ArtifactByKind<'e>` — private ctor + strict keyset equality vs `Catalog.kindKeySet`;
  T11 as a type theorem — src/Projection.Core/ArtifactByKind.fs:117-140; `perKind` /
  `perKindBenched` / key-preserving `mapValues` :142-180.
- `EmitError` — the shared refusal envelope: keyset errors (:20-25), render failure (:28),
  composer partition overlap (:38), and five named emission-capability refusals
  (`CompositeKeyReferenceRefused`… `ComputedExpressionRefused`) :62-96.
- `EmissionMode = Incremental | WipeAndLoad`, `isDestructive`, `cdcCostFactorPerRow` —
  src/Projection.Core/EmissionMode.fs:25-51.
- `ToleratedDivergence` / `Tolerance` — the erasure/quotient vocabulary, `@ladder`-tagged,
  fail-closed parse, per-run `matchedResidual` — src/Projection.Core/Tolerance.fs:26-458.
- Provenance grain: `EpisodeCoordinate`/`Episode` (Schema, Tolerances, AppliedTransforms,
  DataCorrectionReceipts, `RefactorLogRef : string option`) — src/Projection.Core/Episode.fs:11-93;
  `ChangeManifest` (per-edge displacement + tolerance residual) — src/Projection.Core/ChangeManifest.fs:13-46.

**The statement stream (housed in one target).**
- `Statement` DU (CreateTable/CreateIndex/InsertRow/InsertRowIfAbsent/DeleteRowIfMatches/
  Merge/Update/Alter*/Drop*/CreateSchema…) — src/Projection.Targets.SSDT/Statement.fs:284-443;
  `MergeBuildArgs`/`UpdateBuildArgs` in DataStatementArgs.fs; realizations `Render.toText`,
  `ScriptDomBuild`/`ScriptDomGenerate`, `BatchSplitter` — same assembly.
- Canonical form: `SsdtDdlEmitter.statements : Catalog -> seq<Statement>` —
  src/Projection.Targets.SSDT/SsdtDdlEmitter.fs:1117.

**Per-target emitters (the sibling family).**
- SSDT: `Emitter<SsdtFile>` (SsdtDdlEmitter.fs:1135), `EmitterOverDiff<RefactorLogEntry list>`
  (RefactorLogEmitter.fs:323), `DacpacEmitter.emit : Catalog -> Result<byte[]>`,
  `ManifestEmitter.Manifest` (ManifestEmitter.fs:533-634), `SsdtBundle.compose →
  Map<RelativePath, string>` (SsdtBundle.fs:52-83).
- Json: `Emitter<JsonNode>` (JsonEmitter.fs:172); lossless codecs (CatalogCodec, GoldenCodec).
- Distributions: `EmitterWithProfile<JsonNode>` (DistributionsEmitter.fs:219).
- Data: Static/MigrationDependencies/Bootstrap all return
  `Result<ArtifactByKind<DataInsertScript>, EmitError>` (StaticSeedsEmitter.fs:363,382,415;
  BootstrapEmitter.fs:88-94); `DataEmissionComposer` partitions lanes and reifies the
  realization contract (`LeveledDeploymentText`, DataEmissionComposer.fs:592).
- OperationalDiagnostics: `DiagnosticArtifact` channel partition (Routing.fs:32-42), whole-run
  grain (not per-kind).

**Seams and realization (Pipeline).**
- `EmissionSeam` (post-chain Catalog→Catalog, registered ⇔ executed) — EmissionSeam.fs:27-70.
- `SsdtArtifactSeam` (post-emit `ArtifactByKind<SsdtFile>` rewrites, same discipline) —
  SsdtArtifactSeam.fs:31-100.
- `ArtifactPath` (the pinned bundle layout: projection.json, projection.dacpac,
  catalog.snapshot.json, fidelity.json…) — Pipeline.fs:313-361; atomic staging-dir publish
  (overwrite-by-rename) — Pipeline.fs:987-1005; `aggregateSsdt` — Pipeline.fs:363-377.
- Realizations: `Deploy.executeStreamWith` (bulk-fold over the typed stream) — Deploy.fs:402-557;
  `DeployParallelism` (DMV-probed, `ParallelSafe`-gated) — DeployParallelism.fs:27-50;
  `DeployFeasibility` (fixed-point apply of the rendered bundle, named findings) —
  DeployFeasibility.fs:33-50.
- Selector: `ReverseLegRealization = Streaming of journalDirectory: string option | Materialized`;
  `choose` (six positional params, named refusals); `executeJournalGate` — TransferRun.fs:39-124.
- Capability descent: `Capability` + `CapabilityRefusal.ofErrorNumber` (closed SqlErrorNumber
  registry; data errors propagate) — CapabilityRefusal.fs:20-45; `LaneDescent` reporting —
  TransferRun.fs:210-214, 280-312.
- Movement vocabulary: `Destination` (Folder/Docker/Live/Csv), `Scope`, `Strategy`
  (Merge/Replace/Fresh), `Baseline`, `Shape` (Bundle/Ssdt/Skeleton/Manifest),
  `MovementDirection`, `MovementSpec`, `PlanAction` (~35 closed actions) —
  MovementSpec.fs:57-143, 149-237, 565-739; `RevertPolicy` :12-46; Strategy→EmissionMode fold —
  MovementSurface.fs:1613-1614.

## 2 The domain space (independent of current code)

What the emission/realization plane must be able to say, whether or not it is built yet:

1. **Emit** any projection of the catalog (+profile, +diff, +plan) to any registered target
   shape — file bundle, binary package, JSON, data scripts, live DB, CSV — one element per
   unit at the target's grain, with the keyset totality proven, not asserted.
2. **Realize** one canonical stream many ways (text, bulk, per-row, parallel, streaming,
   leveled) with the observable post-state invariant under the choice (A35/A36), and
   **descend** capability rungs only on a named capability error, every descent reported.
3. **Refuse by name** anything the emission cannot yet carry faithfully (temporal kinds,
   unrewritable triggers…) — total decisions, no silent downgrade.
4. **Name every erasure**: each target erases/normalizes axes (DacFx auto-names, text-path
   trigger drops, canonical-form widenings); the domain requires the erasure set of each
   target to be a declared, closed, per-run-witnessable VALUE (A37's whole content).
5. **Identify the artifact**: what bytes were emitted, from which catalog edition, under
   which policy, when, under which tolerance — the provenance triangle — durable past eject,
   when no upstream remains to re-derive from.
6. **Supersede/retract**: editions succeed one another (R6 dual-track, per-pair cutover,
   blessed-artifact-at-fingerprint); the plane must express "this artifact replaces that
   one" and "this artifact is no longer blessed", as it already can for data (revert) and
   for sink tables (`PhysicalTableSuperseded`).
7. **Select realizations totally**: every request lands on a realization or a named refusal;
   the request itself is a domain value, not a flag tuple.
8. **Stay honest about cost**: the CDC-as-norm ruler must price every mode correctly,
   including genesis loads.

The space is deliberately SQL-Server-shaped: the sole consumers are on-prem SQL Server and
an SSIS consumer. Dialect plurality is NOT in the domain; deploy-technology plurality
(bundle vs dacpac vs live vs docker vs csv) IS, and is expressed.

## 3 Findings

| ID | Class | Dimension | Reification axis | One-line claim | Anchor |
|----|-------|-----------|------------------|----------------|--------|
| F1 | M6+M4 | SEMANTIC | EPISTEMIC | Per-target erasure sets are not values: the DACPAC A37 erasure set lives as comments + implicit projections in a test file, unlike the typed `ToleratedDivergence` quotient | tests/Projection.Tests/DacpacRoundTripTests.fs:29-35,165 |
| F2 | M6+M7 | STATE/RELATIONAL | ONTIC | The emitted artifact has no reified identity/edition, so artifact supersession is inexpressible; episode↔artifact linkage is a `string option` + sibling-file convention, and publish destroys the predecessor | Episode.fs:66; ManifestEmitter.fs:533-634; Pipeline.fs:987-1005 |
| F3 | M3+M4 | HIERARCHICAL | ONTIC | The shared T-SQL statement algebra is housed in and named after one deploy technology (`Projection.Targets.SSDT`), which every non-SSDT consumer must import | Statement.fs:1; Targets.Data fsproj:54; TransferRun.fs:13 |
| F4 | M6+M5 | STATE | TELEOLOGICAL | The realization selector's request is six positional params (adjacent bools — the codebase's own named trap), and `Streaming None` is a representable state a second gate must forbid | TransferRun.fs:57-66,118-124; Cli/Faces/Transfer.fs:464 |
| F5 | M4 | SEMANTIC | ONTIC | Load-mode axis split across two vocabularies with a lossy fold: `Strategy.Fresh → EmissionMode.WipeAndLoad` misprices genesis on the CDC norm and mis-gates it as destructive | MovementSurface.fs:1613-1614; EmissionMode.fs:39-51 |
| F6 | M6 | SEMANTIC | EPISTEMIC | A35's normative worked example cites the retired `RawTextEmitter.statements`; live doc-comments still point at it — axiom text anchored to dead vocabulary | AXIOMS.md:1298; Catalog.fs:1964 |

### F1 — Erasure-as-value holds for the canary quotient but not per target (M6 unreified + M4 split)

**Evidence.** The canary/round-trip erasures are exemplary values: `ToleratedDivergence` is
a closed DU in Core with machine-readable `@ladder <Variant> <Axis> <Disposition>` tags
(Tolerance.fs:26-39), fail-closed config parse (:427-438), and a per-run witnessed residual
(`matchedResidual`, :440-458) that flows onto `Episode.Tolerances` and
`ChangeManifest.ToleranceResidual`. The DACPAC target's erasure set — the exact content A37
candidate says "the function IS the axiom" (AXIOMS.md:1345-1365) — is instead declared in a
test file's comment block ("A37 erasure set (declared, closed): E1 Origin.xml wall-clock,
E2 constraint auto-names, E3 identifier quoting/case-fold",
tests/Projection.Tests/DacpacRoundTripTests.fs:29-35) and applied *implicitly* inside
test-local projection functions (`norm`, `tableParts`, FK-shape-not-name), with
`equalModuloDacpacErasure` reduced to `equalStrict` of pre-erased summaries (:156-166). The
text path's trigger-drop erasure is a third carrier: an in-band marker comment at the
render site naming a `ToleratedDivergence` (Render.fs:109; ScriptDomGenerate.fs:411).
Meanwhile AXIOMS.md still lists A37 as "candidate — chapter 3.4 close (chapter not yet
open)" (AXIOMS.md:2040) although its anticipated witness shipped — doc drift.

**Misalignment.** One domain concept — "what THIS target erases" — is reified at three
different grains in three unlike media (Core DU / test comments / render-site comment).
Production code cannot ask a target for its erasure set; the manifest's `Unsupported` field
projects the *global* `ToleratedDivergence.allKnown` (ManifestEmitter.fs:424-429), not the
per-target declaration. A new binary or textual Π has no contract slot to declare its
erasures into, so A37's promotion criterion ("every binary Π requires the same declaration")
is structurally unevaluable — there is only one declared instance, and it is test-side.

**Candidate primitive.** `TargetErasure` (Core): a closed per-axis erasure value each Π
publishes alongside its emit (`DacpacEmitter.erasure : TargetErasure`), consumed by the
round-trip witnesses (the test then *reads* the production declaration instead of owning
it) and projected per-target into the manifest.

**Outcome-fluency bought.** "Emit to target X and show me exactly what X will not carry" as
a machine answer; A37 becomes promotable; erasure drift on a new target becomes a compile
event, not an archaeology project. **Effort:** M. **Risk-of-inaction:** the next binary
target (SSIS package? bacpac?) ships with an undeclared erasure set — the one thing the
named-erasure law forbids absolutely, per the system's own words (Tolerance.fs:199-209).

### F2 — The artifact is not a value: provenance triangle half-reified, supersession inexpressible (M6 + M7)

**Evidence.** The provenance planes are superbly reified at the *episode* grain: `Episode`
carries schema, coordinate, tolerance residual, applied transforms, correction receipts
(Episode.fs:61-93); `ChangeManifest` carries the per-edge displacement (ChangeManifest.fs:13-46).
But the *emitted artifact* — the thing operators ship — has no identity type: (a) the
episode's only anchor to what was emitted is `RefactorLogRef : string option`
(Episode.fs:66) — a stringly digest/path for ONE of the ~12 bundle files; (b) the
`Manifest` type stamps `EmitterVersion`, `RegistryDigest`, `PolicyVersion option` but no
catalog edition/coordinate, no bundle content digest, and deliberately no instant
(ManifestEmitter.fs:922-930 excludes `At` for T1); (c) the bundle's provenance is a
sibling-file convention (`manifest.json` + `catalog.snapshot.json` + `fidelity.json` under
one `ArtifactPath` layout, Pipeline.fs:313-361); (d) publication is atomic
staging-dir-rename over the SAME `outputDir` (Pipeline.fs:987-1005) — the predecessor
artifact is destroyed in place. Supersession vocabulary exists one plane over
(`SinkDisplacement.PhysicalTableSuperseded`, SinkJournal.fs:111,133) and retraction exists
for data (`RevertPolicy`, MovementSpec.fs:12-16; TransferRevert.fs), and act-at-fingerprint
blessing exists (`WriteSignoff.ActBlessing`, MovementSpec.fs:185-187) — so the domain
demonstrably HAS supersession/blessing concepts; only the artifact plane cannot say them.

**Misalignment.** M6: what-was-emitted/from-which-edition/when is knowledge distributed
across file conventions and a string ref, not a type. M7: emit exists; un-emit/supersede
does not — under R6 dual-track and per-pair cutover, "which emitted bundle is the blessed
current one for environment E, and which did it replace" is a real domain question with no
expressible answer; after eject (the charter's terminal event) the linkage becomes
permanently unrecoverable.

**Candidate primitive.** `ArtifactEdition` (Core): `{ BundleDigest; Coordinate :
EpisodeCoordinate; Policy : VersionedPolicy option; Residual : Tolerance }` — stamped into
the manifest at publish, stored on the Episode (replacing the bare string), and giving
supersession as edition ordering (`succeeds : ArtifactEdition -> ArtifactEdition -> bool`)
plus an explicit `Retracted` mark on the timeline.

**Outcome-fluency bought.** `report bundle` can say "this folder IS edition 14, superseding
13, blessed at fingerprint F"; offline verification (`CheckFidelityAgainst`) gains a typed
target instead of a manifest path; the eject carries a closed artifact genealogy.
**Effort:** M. **Risk-of-inaction:** provenance-by-folder-convention silently breaks the
first time an operator copies a bundle out of its directory — and the engine's whole thesis
is that nothing is lost in silence.

### F3 — The statement algebra lives at the wrong address (M3 misplaced / M4 alias seed)

**Evidence.** `Statement`, `ColumnDef`, `MergeBuildArgs`, and both realizations
(`Render`, `ScriptDomBuild`) live in `Projection.Targets.SSDT` (Statement.fs:1-443). Every
non-SSDT consumer of the stream imports that assembly: `Projection.Targets.Data` has a
ProjectReference on it (Projection.Targets.Data.fsproj:54) because its MERGE lane "joins
the DDL lane on the typed `Statement` stream" (Statement.fs:327-331); Pipeline's
`Deploy`, `TransferRun`, `DeployFeasibility` all `open Projection.Targets.SSDT`
(TransferRun.fs:13; Deploy.fs; DeployFeasibility.fs:6). The reverse leg — a live-database
data movement in which no SSDT bundle, sqlproj, or dacpac exists — speaks "SSDT" to name a
MERGE statement.

**Misalignment.** The concept is "the typed T-SQL statement algebra and its realizations"
— dialect-grain, shared across every SQL-realizing plane (A35's actual load-bearing seam).
Its address and name are bundle-emitter-grain (SSDT = one deploy technology among the
`Destination`/`Shape` axes). Core is clean (no leak upward — see anti-finding A1), but the
vocabulary is aliased: "SSDT" now denotes both the bundle target and the statement IR, and
any future script-shaped target (e.g., an SSIS-native package emitter, a migration-script
target) must either import the SSDT assembly or fork the stream type — the M4 seed. This is
the mirror image of the audit question ("does the stream leak into Core?") — the stream
did not rise too high; it sank into one sibling.

**Candidate primitive.** No new type — a home: `Projection.Statements.Sql` (or
`Projection.Targets.Sql.Statements`) assembly holding Statement/Args/Render/ScriptDomBuild/
BatchSplitter verbatim; Targets.SSDT, Targets.Data, and Pipeline all point there.

**Outcome-fluency bought.** The A35 claim ("Π's canonical output… realization invisible to
Π") becomes architecturally visible: producers and realizations of the stream stop being
transitively coupled to one bundle emitter. **Effort:** M (mechanical, wide). 
**Risk-of-inaction:** low today (single dialect), but each new consumer deepens the false
dependency; the rename cost grows linearly with consumers.

### F4 — The selector's request surface is open-coded (M6; M5 at the edges)

**Evidence.** `ReverseLegRealization.choose (emission: EmissionMode) (resumable: bool)
(tables: string list) (streamingRequested: bool) (journalDirectory: string option)
(sinkResidentResumeAvailable: bool)` (TransferRun.fs:57-64); admissibility is the inline
conjunction `List.isEmpty tables && not resumable && emission = Incremental` (:66); the CLI
face passes all six positionally (src/Projection.Cli/Faces/Transfer.fs:464). The codebase
itself already codified this exact hazard elsewhere: "the tuple had reached three adjacent
bools — the positional-misordering trap" motivated reifying `CheckGoArgs`
(MovementSpec.fs:698-701) and `CheckEstateArgs` ("payload reified to a record from birth",
:663). Separately, the DU embeds a realization *argument* in the realization *identity* —
`Streaming of journalDirectory: string option` — so the journal-less streaming-execute
shape is constructible and must be forbidden downstream by a second gate
(`executeJournalGate` matching `Streaming None, true`, :118-124).

**Misalignment.** The selector is total and refuses by name (excellent — see anti-finding
A4), but its DOMAIN — the request — is not a value. Two `bool`s and adjacent
list/option params are silently transposable; a new admissibility axis (the named
follow-ons: streaming table-subsets, wipe-journal-invalidation, TransferRun.fs:70,87)
widens a positional list rather than a record. And the representable-but-forbidden
`Streaming None` + execute state is exactly the make-illegal-states-unrepresentable miss:
the gate exists because the type allows what the doctrine forbids.

**Candidate primitive.** `ReverseLegRequest` record (Emission, Resumable, Tables,
StreamingRequested, Journal, SinkCapability) with `choose : ReverseLegRequest ->
Result<ReverseLegRealization>`; fold `executeJournalGate` into construction for the
execute path (a gated streaming realization carries its journal non-optionally).

**Outcome-fluency bought.** The selector's decision table becomes property-testable over a
generated record space; flag transposition becomes a compile error; the duplicate-hazard
gate collapses into the type. **Effort:** S. **Risk-of-inaction:** a transposed
`resumable`/`streaming` at a new call site silently picks the wrong realization — the
precise bug class the selector exists to name.

### F5 — One load-mode axis, two vocabularies, one lossy fold (M4)

**Evidence.** Operator grain: `Strategy = Merge | Replace | Fresh` (MovementSpec.fs:83-86,
"Fresh is genesis-load"). Engine grain: `EmissionMode = Incremental | WipeAndLoad`
(EmissionMode.fs:25-27). The fold: `Strategy.Merge -> Incremental; Strategy.Replace |
Strategy.Fresh -> WipeAndLoad` (MovementSurface.fs:1613-1614). `EmissionMode` then prices
and gates: `isDestructive WipeAndLoad = true`, `cdcCostFactorPerRow WipeAndLoad = 2`
(EmissionMode.fs:39-51) — but a genesis load against an empty baseline deletes nothing:
its true CDC factor is 1 (insert-images only) and it is not destructive. `Baseline.Empty`
partially re-encodes the lost bit on a parallel channel (MovementSurface.fs:2126).

**Misalignment.** M4 split-with-lossy-fold: the third point of the operator space is
folded onto the second point of the engine space, so the engine's own measurement doctrine
("CDC capture count = the data norm" — the system's ruler) misprices genesis, and the
destructive-op gate fires for a non-destructive act. Not silent (the gate over-asks rather
than under-asks), but the accounting system's ruler reads wrong on a named mode.

**Candidate primitive.** Either a third engine mode (`Genesis`) or thread `Baseline` into
`isDestructive`/`cdcCostFactorPerRow` so cost/gating is a function of (mode × baseline).

**Outcome-fluency bought.** Correct CDC-norm forecasting for genesis loads (the forecast
surfaces feed the go board); no destructive-gate friction on first-load flows.
**Effort:** S. **Risk-of-inaction:** CDC-budget forecasts overstate genesis loads 2×;
operators learn to discount the gate — the expensive habit.

### F6 — A35's worked example names a retired emitter (M6, doc-drift)

**Evidence.** AXIOMS.md:1298 grounds A35 in `RawTextEmitter.statements : Catalog ->
seq<Statement>`; the module was retired (SqlLiteral.fs:10 "RawTextEmitter retirement
Tier-1 #4") and survives only in stale doc-comments (Catalog.fs:1964; Lineage.fs:50;
TopologicalOrder.fs:53). The live canonical form is `SsdtDdlEmitter.statements`
(SsdtDdlEmitter.fs:1117). **Misalignment:** the axiom's normative example is dead
vocabulary; a fresh agent greps it and finds comments only. **Candidate fix:** amend the
worked-example paragraph (and the four stale comments) to the live name. **Effort:** S.
**Risk-of-inaction:** the axiom register slowly decouples from the code it governs — the
exact failure CLAUDE.md §0 declares a first-class defect.

## 4 Anti-findings (correct specializations)

- **A1 — The statement stream's saturated SQL-Server-ness is right.** `Statement` carries
  IDENTITY brackets, extended properties, NOCHECK ladders, TSql160 parses
  (Statement.fs:284-443): the domain is on-prem SQL Server + an SSIS consumer
  (CLAUDE.md §1); a dialect-neutral statement IR would be false generality. A35's
  neutrality claim is about REALIZATION, and that is real: the same stream feeds
  `Render.toText` (bytes) and `Deploy.executeStreamWith` (bulk-folded live deploy,
  Deploy.fs:402-470) with the algebra invariant — the claim holds where it is made. (F3
  contests the stream's *address*, not its dialect.)
- **A2 — Realization-as-function on the forward leg (no cross-plane `Realization` DU) is
  right.** A36 defines realizations as `fold : seq<'e> -> 'output` equivalent up to
  post-state; forcing text/bulk/parallel/dacpac/leveled into one closed DU would be the
  false symmetry CRYSTALLINE_FORM names as a defect. The reverse leg reified a DU exactly
  where refusals demanded a value (TransferRun.fs:39-42) — reification at the point of
  need, not before.
- **A3 — Emission-capability refusals living in Core's `EmitError` are right-grained.**
  `TemporalKindRefused`, `TriggerUnrewrittenRefused` etc. (ArtifactByKind.fs:62-96) look
  target-flavored, but they are catalog-shaped facts with SHARED Core predicates
  (`SqlLiteral.unparsableValueReason`, `Kind.unresolvedComputedIdentifiers`), and the
  shared `Emitter<'e>` signature (Types.fs:50) requires the refusal vocabulary at the
  contract site so text and dacpac paths refuse identically — downgrades-never-silent
  needs them shared, and the raise sites confirm shared use (SsdtDdlEmitter + Estate).
- **A4 — The capability-descent doctrine is typed and closed.** `Capability` +
  `CapabilityRefusal.ofErrorNumber` (CapabilityRefusal.fs:20-45): a closed
  SqlErrorNumber→Capability registry, total (unlisted numbers are data errors and
  PROPAGATE — degrading would mask corruption), with the two descent *shapes* (multi-rung
  ladder vs attempt-or-skip) deliberately kept distinct and their reports distinct
  (`LaneDescent` vs `ToleratedDivergence`). A named, justified asymmetry — not M7.
- **A5 — OperationalDiagnostics emitters bypassing `ArtifactByKind` is right.** Their
  grain is the diagnostic-code partition, not the kind; the routing partition property
  ("every entry routes to exactly one artifact", Routing.fs:26-30) is the T11-analog at
  their grain. Uniformity for its own sake would misplace the grain.
- **A6 — `Destination.Docker` naming a technology is acceptable.** It names the one
  ephemeral-verify mechanism the engine ships; the DU is closed, so a second ephemeral
  substrate is a compiler event, and the CLI vocabulary matches the operator's word for it.

## 5 Already-aligned (exemplary reifications)

- **`ArtifactByKind` — T11 as a type theorem.** Private ctor + strict keyset equality
  (ArtifactByKind.fs:117-140); `perKind` capturing the sibling fold (:154-159);
  `mapValues` carrying the PROVEN keyset through rewrites with no revalidation (:179-180).
  The house private-constructor discipline at its best; every sibling target (SSDT, Json,
  Distributions, Data triumvirate) actually rides it.
- **`ToleratedDivergence`/`Tolerance` — erasure as governed value.** Closed DU with
  compile-forced coverage (Tolerance.fs:278-319), fail-closed config parse (:427-438),
  `@ladder` machine tags that auto-flip the faithfulness matrix on retirement (:26-39),
  and the per-run `matchedResidual` honesty cut (:440-458) flowing onto Episode and
  ChangeManifest. This is the standard F1 asks the per-target erasures to meet.
- **The seam pattern — registered ⇔ executed by construction.** `EmissionSeam`
  (EmissionSeam.fs:27-70) and its artifact-plane analog `SsdtArtifactSeam`
  (SsdtArtifactSeam.fs:31-100): metadata and transform travel in one record, `apply` and
  `metadata` project from the SAME list — the blind spot class (bare post-chain mutators)
  is structurally extinct.
- **Episode/ChangeManifest — displacement provenance.** Five planes co-recorded at one
  coordinate (Episode.fs:61-93); per-edge manifests answering the SSIS consumer's "what
  changed, under what equivalence" (ChangeManifest.fs:13-46).
- **`MovementSpec` — the verb space as one typed value.** Closed axes
  (Destination/Scope/Strategy/Baseline/Shape/MovementDirection, MovementSpec.fs:57-143),
  defaults-that-vanish, `isLiveWrite` as a pure predicate (:286-289), and `PlanAction`'s
  total routing with named refusals (:565-739).
- **`DeployFeasibility` — proving the shipped bytes.** The gate applies the RENDERED
  bundle, not the internal stream ("what is proven is what ships"), with fixed-point
  retry separating ordering failures from genuine rejections (DeployFeasibility.fs:8-50).
- **`Deploy.executeStreamWith` — A36 made operational.** One typed stream, bulk-folded
  InsertRow runs, DDL flushed through the same `Render` the text path uses
  (Deploy.fs:402-470) — realization choice invisible to Π, exactly as the axiom states.
