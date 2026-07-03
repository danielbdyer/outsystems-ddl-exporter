namespace Projection.Pipeline

// LINT-ALLOW-FILE: pipeline orchestration at the boundary — terminal SQL/path text composition
//   across typed segments (the per-line Guid staging-suffix carries its own
//   marker). `String.concat` is the BCL primitive at the terminal-text boundary.

open System.IO
open System.Text.Json.Nodes
open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Adapters.Osm
open Projection.Targets.SSDT
open Projection.Targets.Json
open Projection.Targets.Distributions
open Projection.Targets.OperationalDiagnostics
open FsToolkit.ErrorHandling

/// End-to-end pipeline composition: V1 `osm_model.json` →
/// `Projection.Adapters.Osm.CatalogReader.parse` → V2 Catalog →
/// three sibling Π's (RawText / JSON / Distributions) → on-disk
/// artifacts. The dogfood frame (per `VISION.md` §"Chapter 3 plan"
/// and `DECISIONS 2026-05-22 — Chapter 3 sequencing`): V2 verifies
/// V1 by parsing V1's JSON, projecting through V2's IR, and emitting
/// the chorus.
///
/// This module is the composition surface; `Program.fs` owns argv
/// parsing and exit codes. Tests bypass `Program.fs` and invoke
/// `Pipeline.run` directly with a `Composition` value, so the
/// golden-file regression surface exercises the same code path the
/// CLI does.
[<RequireQualifiedAccess>]
module Compose =

    // Card P2 — the seed-load seam names the data composer's leveled plan;
    // module abbreviations are file-local, so nothing is re-exported.
    module DataComposer = Projection.Targets.Data.DataEmissionComposer

    /// Per-emitter output captured into memory before being written to
    /// disk. Tests assert against these values without round-tripping
    /// through the file system.
    ///
    /// Per the chapter 4.1.A close arc + Tier-1 #2 RawTextEmitter
    /// retirement transition: `SsdtBundle` is the production-shape
    /// per-table file map (per `SsdtBundle.compose`) — `Map<RelativePath
    /// , string>` containing one `.sql` per kind plus `manifest.json`.
    /// This retires the chapter-3-era `Sql : string` single-blob
    /// dogfood output in favor of the per-table file shape the
    /// operator's Azure DevOps pipeline consumes.
    type Outputs =
        {
            /// Production-shape SSDT bundle: per-kind `.sql` files
            /// (under `Modules/<Module>/<Schema>.<Table>.sql` per V1
            /// convention) + `manifest.json` (per V1 SsdtManifest
            /// schema). Keyed by relative path; values are file
            /// contents. Iterate to write to disk; consumers needing
            /// a single concatenated SQL string call `aggregateSsdt`.
            SsdtBundle : Map<string, string>
            /// AC-X1 — the data/seed bundle. When the operator opts into data
            /// emission (`emission.staticSeeds` / `migrationDependencies` /
            /// `bootstrap` in config → `EmissionPolicy.EmitData = true`), this
            /// carries the idempotent, CDC-aware, cycle-ordered MERGE seed
            /// scripts (`DataEmissionComposer`) keyed by relative path
            /// (`Data/seed.sql`). Re-running a script against a fresh-blank /
            /// already-loaded DB is non-overwriting and CDC-silent on unchanged
            /// rows (the PROD-empty premise). Empty when data emission is off
            /// (byte-identical to the schema-only bundle).
            DataBundle : Map<string, string>
            /// V2 IR JSON from `JsonEmitter`, typed as `JsonNode` so
            /// consumers (drift detection, structural diff, post-write
            /// enrichment) query the doc tree without a `JsonNode
            /// .Parse` re-parse step. Per Tier-1 #3 (RawTextEmitter
            /// retirement arc): the chapter 3.7 slice ε already typed
            /// the per-kind value at the Π port; Tier-1 #3 lifts the
            /// composition output to JsonNode at the Outputs seam.
            /// Distinct from `manifest.json` (the V1-mirror SSDT
            /// manifest in `SsdtBundle`).
            Json : JsonNode
            /// The FAITHFUL, round-trippable catalog snapshot (`CatalogCodec`) —
            /// the persisted form of THIS export's model, written as
            /// `catalog.snapshot.json` alongside the bundle. Unlike `Json` (the
            /// lossy SSDT-consumer `projection.json`, which has no reader), this
            /// round-trips losslessly, so diffing two publish dirs
            /// (`diff before/ after/`) renders the precise model change. Seeded
            /// directly like `Fidelity` / `Manifest` (not a sibling-Π emit step),
            /// and written unconditionally on every bundle emission.
            CatalogSnapshot : string
            /// Distributions JSON from `DistributionsEmitter`, consuming
            /// the acquired `Profile` (live source-environment evidence
            /// when `profiler.provider = "live"`; `Profile.empty`
            /// otherwise). Same JsonNode-at-the-seam treatment as `Json`
            /// above.
            Distributions : JsonNode
            /// Chapter 5+ slice `5.13.remediation-emitter` —
            /// per-decision remediation SQL (UPDATE/DELETE/SELECT
            /// options for operator-attention findings: nullability
            /// conflicts + FK orphans + unique-index duplicates).
            /// Operator-safety contract: only SELECT active; UPDATE
            /// + DELETE commented-out by default. Written as
            /// `manifest.remediation.sql` alongside the SSDT bundle.
            RemediationSql : string
            /// Chapter 5+ slice `5.13.summary-formatter` — 6-bucket
            /// rollup prose (PrimaryKey / Physical / Mandatory /
            /// ForeignKey / Unique / Remediation). Written as
            /// `manifest.summary.txt` alongside the SSDT bundle.
            SummaryText : string
            /// H-032 — operator-facing config-edit suggestions derived
            /// from the pass-chain diagnostic stream. Every
            /// `DiagnosticEntry` whose `SuggestedConfig = Some _` is
            /// deduplicated by `Path` and emitted here. Written as
            /// `suggest-config.json` alongside the SSDT bundle.
            SuggestConfigJson : JsonNode
            /// The compiled `.dacpac` package (`DacpacEmitter.emit` over the
            /// emitted catalog), present exactly when the operator opted in via
            /// `emission.dacpac: true`. `None` (the default) writes no package —
            /// byte-identical to the pre-wire bundle. Raw DacFx bytes:
            /// content-equal across runs, NOT byte-stable (slice ζ deferral —
            /// see `DacpacEmitter`'s header).
            Dacpac : byte[] option
            /// The SDK-style `.sqlproj` + its `Script.PostDeployment.sql`
            /// (`emission.sqlproj: true`), so a normal publish drops a buildable
            /// SSDT project whose post-deploy `:r`-includes the data lanes. `None`
            /// (the default) writes neither — byte-identical to the pre-wire bundle.
            Sqlproj : string option
            PostDeploy : string option
            /// The typed SSDT manifest built during projection (the same
            /// value serialized into `manifest.json` within `SsdtBundle`).
            /// Surfaced here so consumers — `runWithConfig`'s `RunReport`,
            /// drift detection — read the structured manifest (coverage,
            /// `ColumnProfiles`, deployment batches) without re-parsing
            /// the serialized JSON. `ColumnProfiles` is non-empty exactly
            /// when the acquired `Profile` carries numeric moments.
            Manifest : ManifestEmitter.Manifest
            /// §16 conduit — the pass chain's full Lineage trail
            /// (`composed.Trail`). Carried here so the run boundary
            /// (`projectFrom` → `RunReport`) can project it onto
            /// `transform.lineage` / `.applied` / `.declined` envelopes at
            /// egress without Core touching `LogSink` (no I/O in Core; Core
            /// compiles before `LogSink`). The `Manifest` above already
            /// embeds the trail, so this bundle is provenance-bearing by
            /// precedent. Not serialized into any artifact file.
            Trail : LineageEvent list
            /// §16 conduit — the chain's full `Diagnostics` stream
            /// (`LineageDiagnostics.entries composed`), for the same
            /// `transform.diagnostic` egress projection. Disjoint from the
            /// curated operational `RunReport.Diagnostics` set.
            PassEntries : DiagnosticEntry list
            /// The Model Fidelity Report — the per-run, rolled-up account of the
            /// distance between the declared model and the source reality the
            /// live profiler observed (data violations + accepted divergences +
            /// uniqueness candidates). Computed at the `seedOutputs` seam from
            /// the emitted catalog × the run's `Profile` × the categorical-
            /// uniqueness decision set; written as `fidelity.json` (structured)
            /// + `fidelity.txt` (the rolled-up text). Empty-but-present on the
            /// `Profile.empty` base case (a pure emit observes no reality — the
            /// honest empty report).
            Fidelity : ModelFidelity.ModelFidelityReport
        }

    /// Chapter C slice C.2 — run-shape value returned by
    /// `runWithConfig`. Carries the written artifact paths AND the
    /// post-run diagnostic stream (today: the special-circumstances
    /// scan emissions for missing-PK targets + unresolved cycles,
    /// with operator-allowlist `Metadata.acceptedVia` annotations
    /// applied). CLI consumers (`runFullExport`) emit each
    /// diagnostic as a LogSink envelope; other consumers (older
    /// `emit --config` surface) may ignore.
    type RunReport = {
        Paths       : string list
        Diagnostics : DiagnosticEntry list
        /// The typed manifest emitted by this run. `Manifest.ColumnProfiles`
        /// is non-empty when the run acquired a live source-environment
        /// `Profile` (`profiler.provider = "live"` + an accessible database
        /// via the out-of-band connection); empty for the `Profile.empty`
        /// base case.
        Manifest    : ManifestEmitter.Manifest
        /// The pass chain's full Lineage trail (`composed.Trail`), surfaced
        /// at the run boundary so the structured-logging layer can project
        /// it onto `transform.lineage` / `transform.applied` / `.declined`
        /// envelopes per `docs/logging-format.md` §7.4 + §16 (project from
        /// the accumulated writer at egress; Core stays I/O-free). Distinct
        /// from `Diagnostics` (the curated operational findings).
        Trail          : LineageEvent list
        /// The pass chain's full `Diagnostics` stream
        /// (`LineageDiagnostics.entries composed`), surfaced for the same
        /// `transform.diagnostic` egress projection. Disjoint from the
        /// curated `Diagnostics` set (special-circumstances / inactive /
        /// FK-selectivity / joint-dependency / FK-drop witnesses).
        PassDiagnostics : DiagnosticEntry list
        /// NM-34b (live) — the source `Catalog` the run READ (the live model
        /// resolved via `LiveModelRead` / hydration, before renames). Surfaced
        /// so the run boundary can hash its CANONICAL form
        /// (`CatalogCodec.serialize`) into the `Run.inputDigest` model half on
        /// the live-OSSYS path, where there is no on-disk model file to hash.
        /// Path-sourced runs keep hashing the file text (byte-identical); this
        /// field is the deterministic model-content input for the live path.
        ReadCatalog : Catalog
    }

    /// Track W1-B (seam T2) — the **diff-vs-prior store leg** of `full-export`.
    /// When the operator threads a `--lifecycle-store`, a full-export is no
    /// longer treated as a genesis emission: the prior emission's schema (state
    /// A, reconstructed from the durable `EpisodicLifecycle`) is loaded and the
    /// run's emitted schema (state B) is diffed against it. This value carries
    /// the displacement `B ⊖ A` the run measured — the four protein-X1/X3
    /// pieces the morphology flagged as `no-diff-vs-prior`:
    ///   * `Displacement` — `CatalogDiff.between prior current` (empty ⟺ an
    ///     idempotent re-export of the same model);
    ///   * `Manifest` — the `ChangeManifest` of the displacement (channel
    ///     counts, norm, refactorlog ref, CDC count) the SSIS consumer reads to
    ///     answer "what did this sprint change?";
    ///   * `AccumulatedRefactorLog` — the cumulative `.refactorlog` (prior ⊕
    ///     current, deduped by `OperationKey` per AC-P6), so DacFx applies
    ///     `sp_rename` (not DROP+ADD) for any source older than the latest;
    ///   * `Chain` — the `EpisodicLifecycle` after recording exactly ONE new
    ///     episode (the run's emitted schema becomes the new schema plane).
    ///
    /// Absent / empty store ⇒ this leg does not run (byte-identical genesis
    /// emission); present store ⇒ `Some` after the emission lands and the
    /// episode is recorded.
    type FullExportStoreLeg = {
        Displacement           : CatalogDiff
        Manifest               : ChangeManifest
        AccumulatedRefactorLog : RefactorLogEntry list
        Chain                  : EpisodicLifecycle
    }

    /// Fail-loud refusals of the diff-vs-prior store leg (seam T2). Distinct
    /// from the genesis emission's errors: a malformed store, an unobservable
    /// displacement, or a non-monotone record are all named, never silent.
    type FullExportStoreError =
        | StoreReadFailed of message: string
        | DisplacementFailed of EmitError
        | RecordFailed of message: string

    /// Per-artifact relative path. Centralized so tests and the CLI
    /// agree on naming.
    [<RequireQualifiedAccess>]
    module ArtifactPath =
        [<Literal>]
        let json = "projection.json"
        [<Literal>]
        let distributions = "distributions.json"
        /// Chapter 5+ slice 5.13.remediation-emitter — per-finding
        /// UPDATE/DELETE/SELECT options for operator-attention findings.
        [<Literal>]
        let remediation = "manifest.remediation.sql"
        /// Chapter 5+ slice 5.13.summary-formatter — 6-bucket rollup
        /// prose for operator review of tightening decisions.
        [<Literal>]
        let summary = "manifest.summary.txt"
        /// H-032 — config-edit suggestions document. JSON array of
        /// deduplicated `SuggestedConfig` payloads from the diagnostic
        /// stream; operator reviews and applies to their config file.
        [<Literal>]
        let suggestConfig = "suggest-config.json"
        /// The compiled DacFx package (`emission.dacpac: true`); the
        /// sibling of `projection.json` on the deployable axis.
        [<Literal>]
        let dacpac = "projection.dacpac"
        /// The SDK-style SSDT project + its post-deploy (`emission.sqlproj: true`).
        /// Mirror `SqlprojEmitter.fileName` / `PostDeployEmitter.fileName` — the
        /// `.sqlproj`-build test pins both names, so a drift surfaces there.
        [<Literal>]
        let sqlproj = "ProjectionCatalog.sqlproj"
        [<Literal>]
        let postDeploy = "Script.PostDeployment.sql"
        /// The Model Fidelity Report — structured (`fidelity.json`) + the
        /// rolled-up operator text (`fidelity.txt`). Default-on; emitted
        /// alongside the other run artifacts on full-export and migrate.
        [<Literal>]
        let fidelityJson = "fidelity.json"
        [<Literal>]
        let fidelityText = "fidelity.txt"
        /// The FAITHFUL, round-trippable catalog snapshot (`CatalogCodec`) — the
        /// persisted model of THIS export, for drift-diffing two publish dirs
        /// (`diff A/ B/`). Read back losslessly by `Source.ofFile` via its
        /// `codecVersion` marker (kept in sync with the literal in `Source.ofFile`).
        [<Literal>]
        let catalogSnapshot = "catalog.snapshot.json"

    /// Aggregate the SSDT bundle's per-table SQL files into one
    /// concatenated SQL string (manifest.json excluded; iterates the
    /// bundle in deterministic SsKey-derived order via Map's natural
    /// ordering). Used by Deploy.runEphemeral and any consumer that
    /// needs the single-string deploy form (e.g., the V1↔V2
    /// differential dogfood tests). The per-file shape stays canonical
    /// for production deploys.
    let aggregateSsdt (bundle: Map<string, string>) : string =
        bundle
        |> Map.toSeq
        |> Seq.filter (fun (path, _) -> path.EndsWith(".sql"))
        |> Seq.map snd
        |> String.concat "\nGO\n\n"  // LINT-ALLOW: terminal SQL-batch joiner across per-table SsdtBundle entries; segments are typed (each `Body` is the rendered ScriptDom output from SsdtDdlEmitter); BCL `String.concat` IS the use-case-specific library at the SQL-batch concatenation boundary. Reconciliation slice 2 — blank line on BOTH sides of GO (V1 StatementBatchFormatter spacing)

    /// Run the three sibling Π's against a Catalog. Pure: same Catalog
    /// → same Outputs (T1 byte-determinism). Profile is `Profile.empty`
    /// for the dogfood frame; M2 onward will thread real profile
    /// evidence.
    ///
    /// **Chapter A.4.7' slice δ** wires `RegisteredTransforms.allChainSteps`
    /// in front of the emitter fan-out per A41 (registry as load-bearing
    /// execution surface). Catalog flows: raw → `compose allChainSteps`
    /// → emitters. With skeleton-friendly defaults (Mask = empty;
    /// Morphism = identity; RenameSpec = []; Policy = Policy.empty;
    /// Profile = Profile.empty), Catalog-rewriting passes contribute
    /// only the canonicalization / closure they would apply
    /// unconditionally; decision-set passes write back evidence the
    /// emitters do not yet consume (decision-set consumption is a
    /// future-chapter concern).
    /// Chapter C slice C.3 — apply operator-supplied emission-folder
    /// overrides to the typed per-kind SSDT bundle. The rewrite
    /// preserves each `SsdtFile`'s basename (the cross-platform-
    /// deterministic `<Schema>.<Table>.sql` suffix) and replaces the
    /// directory prefix with the operator-named folder.
    /// `EmissionFolders.empty` short-circuits to the input unchanged.
    ///
    /// The rewrite fires at the typed `ArtifactByKind<SsdtFile>` layer
    /// — operator overlay sits outside Π (pillar 9: emitters are
    /// `DataIntent`; operator opinion enters at the Pipeline-layer
    /// realization boundary). Reconstructs through
    /// `ArtifactByKind.create` against the same catalog so the
    /// strict-equality keyset invariant is preserved.
    let private applyEmissionFolderOverrides
        (folders: EmissionFolders)
        (catalog: Catalog)
        (files: ArtifactByKind<SsdtDdlEmitter.SsdtFile>)
        : ArtifactByKind<SsdtDdlEmitter.SsdtFile> =
        if EmissionFolders.isEmpty folders then files
        else
            use _ = Bench.scope "compose.applyEmissionFolderOverrides"
            // PL-4 (S56) — key-preserving rewrite: the proven keyset carries
            // over via `mapValues`; no re-validation, no unreachable arm.
            files
            |> ArtifactByKind.mapValues (fun key file ->
                match Map.tryFind key folders.ByKind with
                | None        -> file
                | Some folder ->
                    let segments = file.RelativePath.Split('/')
                    let basename = segments.[segments.Length - 1]
                    { file with
                        RelativePath = System.String.Concat(folder, "/", basename) })

    /// Chapter C slice C.4 — filter the pass chain by operator-supplied
    /// `TransformGroups`. Each chain entry's name is looked up against
    /// `RegisteredTransformTags.passTags`; if the entry's tag set
    /// intersects any group the operator has disabled, the entry is
    /// excluded. Empty `TransformGroups.disabledGroups` (no operator
    /// override, or all groups enabled) returns the chain unchanged.
    ///
    /// Per pillar 9: the filter expresses operator intent at the
    /// realization-layer boundary; passes self-identify their group
    /// membership via the static `passTags` map alongside the chain
    /// they participate in.
    let private filterChainByGroups
        (groups: TransformGroups)
        (chain: PassChainAdapter list)
        : PassChainAdapter list =
        let disabled = TransformGroups.disabledGroups groups
        if Set.isEmpty disabled then chain
        else
            chain
            |> List.filter (fun adapter ->
                let tags = RegisteredTransformTags.tagsFor adapter.Name
                Set.intersect tags disabled |> Set.isEmpty)

    /// Chapter C slice C.2 — state-capturing project. Returns both
    /// the `Outputs` and the post-chain `ComposeState` so downstream
    /// consumers (today: `SpecialCircumstancesDiagnostics.emit`) can
    /// observe per-pass evidence without re-running the chain.
    /// `projectFromChain` is now a thin wrapper that drops the state.
    ///
    /// Chapter C slice C.3 — accepts `EmissionFolders` and applies the
    /// folder overrides at the typed per-kind SSDT bundle layer
    /// (post-emit, pre-compose). `EmissionFolders.empty` is a no-op.
    ///
    /// Chapter C slice C.4 — accepts `TransformGroups` and filters the
    /// chain to exclude passes whose tag-set intersects any disabled
    /// group. `TransformGroups.empty` is a no-op (V1-parity: all
    /// transforms run).
    /// Build a `VersionedPolicy` snapshot of the policy that drove the
    /// chain. Always at the genesis version (1.0.0); evolution across
    /// runs is a downstream consumer concern (the manifest captures the
    /// version of *this* projection, not the history).
    let private versionPolicy (policy: Policy) : VersionedPolicy =
        // Slice 0 (2026-06-02): Core retired `VersionedPolicy.now`; Pipeline
        // is the boundary that supplies the wall clock per the Episode.fs
        // canonical "boundary-supplied at" pattern.
        VersionedPolicy.create System.DateTimeOffset.UtcNow policy None

    // ------------------------------------------------------------------
    // E1 — registry-driven emit phase (`DECISIONS 2026-06-04`).
    // The sibling-Π emit phase is ONE `emitSteps : EmitStep list` source,
    // from which both the execution (the fold in `projectFromChainWithState`)
    // and the registry's emit-metadata (`RegisteredAllTransforms`) project —
    // so `registered ⇔ executed` holds for the emit stage by construction.
    // Each `Emit` wraps its emitter verbatim and writes its one `Outputs`
    // field; the steps are independent (no cross-reads), so the fold over
    // `seedOutputs` reproduces the prior hand-assembled `Outputs` byte-for-byte.
    // ------------------------------------------------------------------

    /// The emit phase's read-only inputs — the final `ComposeState` plus the
    /// emission-policy-filtered catalog and the run's profile / folders /
    /// versioned-policy / writers. Everything the six emitters consume.
    type EmitContext = {
        EmittedCatalog  : Catalog
        ComposedState   : ComposeState
        Profile         : Profile
        Folders         : EmissionFolders
        VersionedPolicy : VersionedPolicy option
        Trail           : LineageEvent list
        PassEntries     : DiagnosticEntry list
        /// NM-38 — the SSDT constraint-rendering mode lifted from
        /// `EmissionPolicy.RenderConstraintsElegant` at the composition
        /// seam (Core's bool → SSDT's typed `Mode`; Core cannot name the
        /// SSDT type). Threaded into `SsdtDdlEmitter.emitSlicesWithRendering`
        /// so the operator's V1-parity / bisect opt-out reaches production.
        ConstraintRendering : ConstraintFormatter.Mode
        /// NM-70 (WP5) — `EmissionPolicy.EmitIdentityAnnotations` lifted to
        /// the SSDT emit seam. `true` (default) ⇒ the `Projection.*` identity
        /// extended properties emit (byte-identical). `false` ⇒ they are
        /// suppressed and the `emission.identityAnnotations.omitted` named
        /// downgrade diagnostic is emitted at the SSDT emit step.
        EmitIdentityAnnotations : bool
        /// Wave-3 slice 3.4 (now WIRED) — the per-run accepted-divergence set
        /// lifted from `EmissionPolicy.ConfiguredTolerance` at the composition
        /// seam, so `fidelityOf` resolves the tolerance residual against the
        /// OPERATOR's configured set (`emission.tolerance`) rather than the
        /// hardcoded `Tolerance.permissive`. Default-permissive ⇒ byte-identical.
        ConfiguredTolerance : Tolerance
    }

    /// One registered emit step: its metadata (the pillar-9 classification
    /// surface `RegisteredAllTransforms` reads) paired with how it runs
    /// (`Emit` — writes its one `Outputs` field from the `EmitContext`).
    type EmitStep = {
        Metadata : RegisteredTransformMetadata
        Emit     : EmitContext -> Outputs -> Outputs
    }

    /// The three tightening DecisionSets pulled from the post-chain
    /// `ComposeState`, with empty defaults when an axis registered no
    /// interventions.
    let private decisionSetsOf (state: ComposeState)
        : NullabilityDecisionSet * UniqueIndexDecisionSet * ForeignKeyDecisionSet =
        state.NullabilityDecisions |> Option.defaultValue NullabilityRules.emptyDecisionSet,
        state.UniqueIndexDecisions |> Option.defaultValue UniqueIndexRules.emptyDecisionSet,
        state.ForeignKeyDecisions  |> Option.defaultValue ForeignKeyRules.emptyDecisionSet

    let private emptyJsonNode () : JsonNode =
        match JsonNode.Parse "{}" with
        | null -> invalidOp "Compose.seedOutputs: '{}' did not parse (unreachable)"
        | n    -> n

    /// The estate name the fidelity-report masthead leads with — neutral,
    /// THE_VOICE §7 ("never *your*"). A single-module catalog names itself; a
    /// multi-module estate is "the model" (the count-bearing masthead carries
    /// the module / entity tally beside it).
    let private estateName (catalog: Catalog) : string =
        match catalog.Modules with
        | [ only ] -> Name.value only.Name
        | _        -> "the model"

    /// Compute the per-run Model Fidelity Report from the emit context — the
    /// emitted catalog × the run's profile × the categorical-uniqueness
    /// decision set (`ComposeState.CategoricalUniquenessDecisions`, which the
    /// `CategoricalUniquenessPass` write-back populates; this is the consumer
    /// that closes NM-35). The accepted-divergence residual is the set of named
    /// tolerances the emitted output structurally invokes (`emittedAccepted-
    /// Divergences`, NM-32/33 final hop); a clean pure emit yields the empty
    /// residual, the honest base case.
    /// NM-32/33 (final hop) — the per-run accepted-divergence residual,
    /// computed STRUCTURALLY from the emitted catalog's static data. The emit
    /// path has no deploy round-trip (the canary is the separate `check` verb),
    /// so the residual is the set of named tolerances the EMITTED output
    /// structurally invokes — today, the empty-text → NULL normalization
    /// (`CanaryResidual.observeCell`) — resolved against the accepted set.
    /// `Tolerance.permissive` is the accepted set until an operator tolerance
    /// config is wired (then it becomes that configured value). Closes the
    /// `runStoreLeg` / `fidelityOf` FLAG.
    let private emittedResidualCollector (catalog: Catalog) : CanaryResidual.Collector =
        Catalog.allKinds catalog
        |> List.fold
            (fun coll (k: Kind) ->
                let typeByName = k.Attributes |> List.map (fun a -> a.Name, a.Type) |> Map.ofList
                Kind.staticPopulations k
                |> List.fold
                    (fun c (row: StaticRow) ->
                        row.Values
                        |> Map.fold
                            (fun c2 name raw ->
                                match Map.tryFind name typeByName with
                                | Some typ -> CanaryResidual.observeCell typ raw c2
                                | None     -> c2)
                            c)
                    coll)
            CanaryResidual.empty

    /// Wave-3 3.4 (now WIRED) — the per-run tolerance residual, resolved against
    /// the operator's CONFIGURED accepted set (`emission.tolerance` →
    /// `EmissionPolicy.ConfiguredTolerance`) rather than a hardcoded constant.
    /// `Tolerance.permissive` (the default when `emission.tolerance` is absent)
    /// reports every fired divergence — byte-identical to the prior behavior.
    let private emittedToleranceResidual (configured: Tolerance) (catalog: Catalog) : Tolerance =
        CanaryResidual.resolve configured (emittedResidualCollector catalog)

    let private emittedAcceptedDivergences (configured: Tolerance) (catalog: Catalog) : ToleratedDivergence list =
        CanaryResidual.resolvedDivergences configured (emittedResidualCollector catalog)

    let private fidelityOf (ctx: EmitContext) : ModelFidelity.ModelFidelityReport =
        let categoricalDecisions =
            ctx.ComposedState.CategoricalUniquenessDecisions
            |> Option.defaultValue CategoricalUniquenessRules.emptyDecisionSet
        ModelFidelity.compose
            (estateName ctx.EmittedCatalog)
            ctx.EmittedCatalog
            ctx.Profile
            categoricalDecisions
            (emittedAcceptedDivergences ctx.ConfiguredTolerance ctx.EmittedCatalog)

    /// The fold seed. Every artifact field is a placeholder the matching
    /// `EmitStep` overwrites (the `registered ⇔ executed` invariant
    /// guarantees every step runs); `Trail` / `PassEntries` carry through
    /// as the §16 egress conduits, `DataBundle` is populated downstream
    /// (AC-X1) when data emission is on.
    let private seedOutputs (ctx: EmitContext) : Outputs =
        { SsdtBundle        = Map.empty
          DataBundle        = Map.empty
          Json              = emptyJsonNode ()
          Distributions     = emptyJsonNode ()
          RemediationSql    = ""
          SummaryText       = ""
          SuggestConfigJson = emptyJsonNode ()
          // Conditional (operator-gated) artifact — populated at the
          // `runWithConfigCore` seam when `emission.dacpac` opts in, not by an
          // unconditional `EmitStep`.
          Dacpac            = None
          // Operator-gated SSDT project (`emission.sqlproj`); populated at the
          // `runWithConfigCore` seam alongside `Dacpac`, not by an `EmitStep`.
          Sqlproj           = None
          PostDeploy        = None
          // The faithful, round-trippable snapshot of the emitted model — seeded
          // directly (like `Manifest` / `Fidelity`), written on every bundle
          // emission so two publish dirs can be diffed precisely.
          CatalogSnapshot   = CatalogCodec.serialize ctx.EmittedCatalog
          Manifest          = ManifestEmitter.build ctx.EmittedCatalog
          Trail             = ctx.Trail
          PassEntries       = ctx.PassEntries
          Fidelity          = fidelityOf ctx }

    /// The single source for the sibling-Π emit phase, in emission order.
    let emitSteps : EmitStep list =
        [ { Metadata = SsdtDdlEmitter.registeredMetadata
            Emit =
              fun ctx outputs ->
                use _ = Bench.scope "emit.ssdtBundle.compose"
                let decisionOverlay = DecisionOverlay.ofComposeState ctx.ComposedState
                match SsdtDdlEmitter.emitSlicesWithRendering ctx.ConstraintRendering ctx.EmitIdentityAnnotations decisionOverlay ctx.EmittedCatalog with
                | Ok ssdtFiles ->
                    let rewritten = applyEmissionFolderOverrides ctx.Folders ctx.EmittedCatalog ssdtFiles
                    let policyConflicts = ConflictDetector.detectConflicts ctx.Trail ctx.PassEntries
                    let registry = SsdtDdlEmitter.registeredMetadata :: RegisteredTransforms.all
                    let manifest =
                        ManifestEmitter.buildFull
                            ctx.Profile registry ctx.ComposedState.TopologicalOrder
                            ctx.VersionedPolicy policyConflicts ctx.Trail (Some ctx.ComposedState) ctx.EmittedCatalog
                    { outputs with SsdtBundle = SsdtBundle.compose rewritten manifest; Manifest = manifest }
                | Error err ->
                    invalidOp (sprintf "Compose.project: SsdtDdlEmitter.emitSlices: %A" err) }
          { Metadata = JsonEmitter.registeredMetadata
            Emit =
              fun ctx outputs ->
                use _ = Bench.scope "emit.json"
                match JsonNode.Parse(JsonEmitter.emit ctx.EmittedCatalog) with
                | null -> invalidOp "Compose.project: JsonEmitter.emit produced unparseable text (unreachable)"
                | n    -> { outputs with Json = n } }
          { Metadata = DistributionsEmitter.registeredMetadata
            Emit =
              fun ctx outputs ->
                use _ = Bench.scope "emit.distributions"
                match JsonNode.Parse(DistributionsEmitter.emit ctx.EmittedCatalog ctx.Profile) with
                | null -> invalidOp "Compose.project: DistributionsEmitter.emit produced unparseable text (unreachable)"
                | n    -> { outputs with Distributions = n } }
          { Metadata = RemediationEmitter.registeredMetadata
            Emit =
              fun ctx outputs ->
                let n, u, f = decisionSetsOf ctx.ComposedState
                { outputs with RemediationSql = RemediationEmitter.emit ctx.ComposedState.Catalog n u f } }
          { Metadata = SummaryFormatter.registeredMetadata
            Emit =
              fun ctx outputs ->
                let n, u, f = decisionSetsOf ctx.ComposedState
                { outputs with SummaryText = SummaryFormatter.formatText n u f } }
          { Metadata = SuggestConfigEmitter.registeredMetadata
            Emit =
              fun ctx outputs ->
                use _ = Bench.scope "emit.suggestConfig"
                { outputs with SuggestConfigJson = SuggestConfigEmitter.emit ctx.PassEntries } } ]

    let private projectFromChainWithState
        (chain: PassChainAdapter list)
        (profile: Profile)
        (policy: EmissionPolicy)
        (folders: EmissionFolders)
        (groups: TransformGroups)
        (versionedPolicy: VersionedPolicy option)
        (catalog: Catalog)
        : Outputs * ComposeState =
        let filteredChain = filterChainByGroups groups chain
        use _ = Bench.scope "compose.project"
        let composed =
            use _ = Bench.scope "compose.runChain"
            PassChainAdapter.compose filteredChain (ComposeState.initial catalog)
        let composedState   = LineageDiagnostics.payload composed
        let passEntries     = LineageDiagnostics.entries composed
        // Chapter 4.9 slice δ + F3 (audit 2026-06-17) — apply the post-chain
        // emission-seam rewrites via the ONE bound `EmissionSeam.apply` (today:
        // `filterPlatformAutoIndexes`, `OperatorIntent Emission`). Routing
        // through the seam binds execution↔registration so the totality proof
        // covers it; identity when `IncludePlatformAutoIndexes = true` (V1
        // parity default).
        let emittedCatalog = EmissionSeam.apply policy composedState.Catalog
        // E1 (`DECISIONS 2026-06-04`) — the sibling-Π emit phase is the
        // registry-driven `emitSteps` fold over a seed `Outputs`. Each step
        // writes its one field; the SSDT step computes `decisionOverlay`
        // (Wave-2 2.2 — decisions, never Policy; A18 holds) + `policyConflicts`
        // (H-034) internally from the context. `registered ⇔ executed` for the
        // emit stage holds because `RegisteredAllTransforms` reads the same
        // `emitSteps`. Byte-identical to the prior hand-assembled `Outputs`.
        let emitContext : EmitContext =
            { EmittedCatalog  = emittedCatalog
              ComposedState   = composedState
              Profile         = profile
              Folders         = folders
              VersionedPolicy = versionedPolicy
              Trail           = composed.Trail
              PassEntries     = passEntries
              // NM-38 — lift the Core bool to the SSDT typed Mode at the
              // composition seam; `true` (default) ⇒ `Enabled` (byte-identical).
              ConstraintRendering =
                if policy.RenderConstraintsElegant then ConstraintFormatter.Enabled
                else ConstraintFormatter.Disabled
              // NM-70 — thread the identity-annotation gate to the SSDT emit
              // step; `true` (default) ⇒ the `Projection.*` properties emit
              // (byte-identical). The named-downgrade diagnostic is emitted at
              // the `runWithConfigCore` diagnostics merge, not here.
              EmitIdentityAnnotations = policy.EmitIdentityAnnotations
              // Wave-3 3.4 — lift the operator's accepted-divergence set so the
              // residual resolves against it (default-permissive ⇒ byte-identical).
              ConfiguredTolerance = policy.ConfiguredTolerance }
        let outputs =
            emitSteps
            |> List.fold (fun acc step -> step.Emit emitContext acc) (seedOutputs emitContext)
        // NM-02 (2026-06-13) — the emission axes `EmitSchema` / `EmitDiagnostics`
        // now gate real emit steps, mirroring the `EmitData` gate on the data
        // bundle (line ~608). Every `EmitStep` still runs (so `registered ⇔
        // executed` holds and `Manifest`/`Trail`/`PassEntries` stay populated as
        // the §16 egress conduits); the gate clears the artifact fields AFTER the
        // fold rather than skipping the step. The defaults (`EmissionPolicy.empty`
        // = schema + diagnostics on) leave this identity — byte-identical default.
        let schemaGated =
            // `EmitSchema = false` ⇒ no CREATE/SSDT schema bundle. The `Manifest`
            // value stays (a conduit, embedded in no file when SsdtBundle is empty).
            if policy.EmitSchema then outputs
            else { outputs with SsdtBundle = Map.empty }
        let diagnosticsGated =
            // `EmitDiagnostics = false` ⇒ no operational diagnostic artifacts
            // (decision-log-derived remediation SQL / summary prose / suggest-config).
            if policy.EmitDiagnostics then schemaGated
            else
                { schemaGated with
                    RemediationSql    = ""
                    SummaryText       = ""
                    SuggestConfigJson = emptyJsonNode () }
        diagnosticsGated, composedState

    let private projectFromChain
        (chain: PassChainAdapter list)
        (profile: Profile)
        (policy: EmissionPolicy)
        (catalog: Catalog)
        : Outputs =
        projectFromChainWithState
            chain
            profile
            policy
            EmissionFolders.empty
            TransformGroups.empty
            None
            catalog
        |> fst

    /// Production-shape project: routes through
    /// `RegisteredTransforms.allChainSteps`. The hand-coded "emit raw
    /// catalog" shape retired at chapter A.4.7' slice δ; the registry
    /// is canonical. Chapter 4.9 slice δ — accepts an `EmissionPolicy`
    /// driving the platform-auto-indexes filter.
    let project (policy: EmissionPolicy) (catalog: Catalog) : Outputs =
        projectFromChain RegisteredTransforms.allChainSteps Profile.empty policy catalog

    /// Chapter C slice C.1 — production-shape project with caller-
    /// supplied full `Policy` + `Profile` threaded through the four
    /// tightening passes via `RegisteredTransforms.allChainStepsFor`.
    /// Sibling to `project`. The emission axis rides
    /// `fullPolicy.Emission` — reconciliation slice 2 (`DECISIONS
    /// 2026-06-12`) collapsed the former separate `EmissionPolicy`
    /// parameter (one type, two channels, one config-fed — the
    /// sibling-wrapper smell). Operator-tightening interventions
    /// registered in `fullPolicy.Tightening.Interventions` fire here.
    let projectWith
        (fullPolicy: Policy)
        (profile: Profile)
        (catalog: Catalog)
        : Outputs =
        let chain = RegisteredTransforms.allChainStepsFor fullPolicy profile
        projectFromChain chain profile fullPolicy.Emission catalog

    /// Chapter C slice C.2 — `projectWith` sibling that returns the
    /// post-chain `ComposeState` alongside the outputs. Used by
    /// `runWithConfig` to feed `SpecialCircumstancesDiagnostics`
    /// without re-running the registered chain.
    ///
    /// Chapter C slice C.3 — accepts `EmissionFolders` and applies
    /// the operator-supplied folder overrides at the typed per-kind
    /// SSDT bundle layer (post-emit, pre-compose). Pass
    /// `EmissionFolders.empty` for the no-overrides path (preserves
    /// V1's default `Modules/<Module>/` layout).
    ///
    /// Chapter C slice C.4 — accepts `TransformGroups` and filters
    /// the chain to exclude passes whose tag-set intersects any
    /// disabled group. Pass `TransformGroups.empty` for the
    /// no-overrides path (preserves V1-parity: all transforms run).
    /// S6.3 — `projectWithState` with operator physical-rename pins: the
    /// `LogicalTableEmission` chain step skips the pinned kinds so a physical-form
    /// `tableRenames` override survives into the emitted physical table.
    /// `Set.empty` is `projectWithState` (byte-identical default).
    let projectWithStateWithPinsAndBootstrapLane
        (logicalEmissionPins: Set<SsKey>)
        (fullPolicy: Policy)
        (profile: Profile)
        (folders: EmissionFolders)
        (groups: TransformGroups)
        (migration: Projection.Targets.Data.MigrationDependencyContext)
        (bootstrapLane: DataComposer.BootstrapLane)
        (catalog: Catalog)
        : Outputs * ComposeState =
        let chain = RegisteredTransforms.allChainStepsForWithPins logicalEmissionPins fullPolicy profile
        // H-085 — stamp the manifest with a VersionedPolicy snapshot of
        // the full policy that drove the chain when the operator
        // supplied a non-default policy. `Policy.empty` callers (no
        // operator opinion) produce no stamp — keeps `projectWithState
        // Policy.empty` byte-identical to `project` for T1 determinism.
        let versionedPolicy =
            if fullPolicy = Policy.empty then None
            else Some (versionPolicy fullPolicy)
        let outputs, finalState =
            projectFromChainWithState
                chain
                profile
                fullPolicy.Emission
                folders
                groups
                versionedPolicy
                catalog
        // AC-X1 — the data leg of the publication bundle. When the operator
        // opts into data emission, render the idempotent CDC-aware seed scripts
        // (static-entity populations + bootstrap) over the emitted catalog and
        // add them to the bundle as `Data/seed.sql`. The scripts are MERGE
        // (non-overwriting, CDC-silent on unchanged rows) so a fresh-blank DB or
        // a re-run lands the same state. Off ⇒ `DataBundle` stays empty and the
        // result is byte-identical to the schema-only bundle.
        let decorated =
            if not fullPolicy.Emission.EmitData then outputs
            else
                use _ = Bench.scope "emit.dataBundle.compose"
                // Bootstrap-always (2026-06-14) — thread the hydrated Bootstrap
                // row source into the per-lane render so `Data/Bootstrap.sql`
                // carries content. `bootstrapRows = Map.empty` (the non-hydrated
                // path, all callers but the config-driven publish path) is
                // byte-identical to the prior `composeRenderedBundle`.
                // Migration-context wiring (2026-06-15) — the operator-curated
                // Migration lane rides the SAME seam; `MigrationDependency
                // Context.empty` (no migration file) is byte-identical to the
                // prior threading. The Bootstrap complement already excludes the
                // migration kinds (`hydrateBootstrapRowsExcluding`), so the
                // composer's `OverlappingEmitterCoverage` partition law holds.
                // The chain's `TopologicalOrderPass` already ran Kahn/Tarjan
                // over this exact catalog and stored the order — thread it
                // instead of letting the composer re-derive it. A filtered
                // chain without the pass falls back to the composer's own
                // computation.
                let composed =
                    match finalState.TopologicalOrder with
                    | Some topo ->
                        DataComposer.composeRenderedBundleWithBootstrapLaneUsing topo fullPolicy finalState.Catalog profile migration bootstrapLane UserRemapContext.empty
                    | None ->
                        DataComposer.composeRenderedBundleWithBootstrapLane fullPolicy finalState.Catalog profile migration bootstrapLane UserRemapContext.empty
                match composed with
                | Ok bundle ->
                    // The PER-LANE files (`Data/StaticSeeds.sql` /
                    // `Data/MigrationData.sql` / `Data/Bootstrap.sql`) are the
                    // operator-facing data artifacts — each lane that carries
                    // content emits its own file (DECISIONS 2026-06-14, operator
                    // decision: the per-lane files are the reviewed/applied
                    // artifacts; the prior ≥2-lane gate existed only to avoid
                    // byte-duplicating the fused file, which is no longer emitted).
                    // The is-anything-there gate reads the LANES — the fused
                    // cross-lane text is no longer materialized on this path
                    // (it re-concatenated every per-kind string into a second
                    // whole-estate copy; `composeRenderedFull` remains the
                    // on-demand fused surface). An all-empty lane set ⇒ no
                    // rows in scope ⇒ nothing to emit.
                    let laneFiles = DataComposer.RenderedDataBundle.perLaneFiles bundle
                    if Map.isEmpty laneFiles then outputs
                    else { outputs with DataBundle = laneFiles }
                | Error err ->
                    // Mirrors the SSDT-bundle invariant: a valid catalog never
                    // fails the composer (the keyset is `Catalog.allKinds`).
                    invalidOp (sprintf "Compose.projectWithState: DataEmissionComposer.composeRenderedBundle: %A" err)
        decorated, finalState

    /// Rows-taking sibling of `projectWithStateWithPinsAndBootstrapLane` —
    /// the established call shape for callers whose Bootstrap rows drain
    /// pre-compose (the two-phase schedule); the pipelined publish arm
    /// routes drain-time-rendered scripts through the lane form directly.
    let projectWithStateWithPinsAndBootstrap
        (logicalEmissionPins: Set<SsKey>)
        (fullPolicy: Policy)
        (profile: Profile)
        (folders: EmissionFolders)
        (groups: TransformGroups)
        (migration: Projection.Targets.Data.MigrationDependencyContext)
        (bootstrapRows: Map<SsKey, StaticRow list>)
        (catalog: Catalog)
        : Outputs * ComposeState =
        projectWithStateWithPinsAndBootstrapLane
            logicalEmissionPins fullPolicy profile folders groups migration
            (DataComposer.BootstrapLane.Rows bootstrapRows) catalog

    /// `projectWithStateWithPins` — no Bootstrap row source
    /// (`projectWithStateWithPinsAndBootstrap` with `Map.empty`; byte-identical
    /// to the pre-Bootstrap-always behaviour). The established call shape for
    /// every caller but the config-driven publish path, which hydrates the
    /// Bootstrap lane and routes through the `…AndBootstrap` entry.
    let projectWithStateWithPins
        (logicalEmissionPins: Set<SsKey>)
        (fullPolicy: Policy)
        (profile: Profile)
        (folders: EmissionFolders)
        (groups: TransformGroups)
        (catalog: Catalog)
        : Outputs * ComposeState =
        projectWithStateWithPinsAndBootstrap logicalEmissionPins fullPolicy profile folders groups Projection.Targets.Data.MigrationDependencyContext.empty Map.empty catalog

    /// The canonical `projectWithState` — no physical-rename pins (byte-identical
    /// default; the existing callers are unchanged).
    let projectWithState
        (fullPolicy: Policy)
        (profile: Profile)
        (folders: EmissionFolders)
        (groups: TransformGroups)
        (catalog: Catalog)
        : Outputs * ComposeState =
        projectWithStateWithPins Set.empty fullPolicy profile folders groups catalog

    /// Skeleton-shape project: routes through
    /// `RegisteredTransforms.skeletonChainSteps` (per chapter A.4.7'
    /// slice ε; the four pure-DataIntent passes). Yields the baseline
    /// reachable from `Project(catalog, Policy.empty, profile)` —
    /// `osm emit --skeleton-only` consumes this. Chapter 4.9 slice δ —
    /// uses `EmissionPolicy.defaults` (no filtering) because the
    /// skeleton view is operator-free by definition.
    let projectSkeleton (catalog: Catalog) : Outputs =
        projectFromChain RegisteredTransforms.skeletonChainSteps Profile.empty EmissionPolicy.empty catalog

    /// Chapter A.4.7' slice ε — registry-driven traversal restricted
    /// to the skeleton view (every Site classifies as `DataIntent`).
    /// Returns the `Lineage<Diagnostics<ComposeState>>` from running
    /// `RegisteredTransforms.skeletonChainSteps`; consumers inspect
    /// the trail to assert skeleton-purity, or project to the final
    /// Catalog for skeleton-only emit (slice ζ CLI).
    ///
    /// The skeleton-purity property test (`runSkeleton` emits zero
    /// `OperatorIntent` LineageEvents) promotes from filter-shape
    /// only (chapter A.4.7 slice θ) to true-execution at this slice.
    let runSkeleton (catalog: Catalog) : Lineage<Diagnostics<ComposeState>> =
        use _ = Bench.scope "compose.runSkeleton"
        PassChainAdapter.compose
            RegisteredTransforms.skeletonChainSteps
            (ComposeState.initial catalog)

    /// Read a V1 `osm_model.json` from disk and parse it into a V2
    /// Catalog. Errors are surfaced via the codebase's single-arity
    /// `Result<'a>` (see `Result.fs` arity-coexistence note).
    /// E2 (`DECISIONS 2026-06-04`) — the read adapter as a registry entry:
    /// its metadata (what `RegisteredAllTransforms` reads) paired with its
    /// `Read` function (what `Compose.read` / `readJson` dispatch through),
    /// so `registered ⇔ executed` holds for the read stage from one source.
    type ReadStep = {
        Metadata : RegisteredTransformMetadata
        Read     : CatalogReader.SnapshotSource -> Task<Result<Catalog>>
    }

    /// The single read-adapter entry. The execution path (`read` / `readJson`)
    /// and the registry (`RegisteredAllTransforms`) both consume this.
    let readStep : ReadStep =
        { Metadata = CatalogReader.registeredMetadata
          Read     = CatalogReader.parse }

    let read (jsonPath: string) : Task<Result<Catalog>> =
        task {
            use _ = Bench.scope "compose.read"
            return! readStep.Read (CatalogReader.SnapshotFile jsonPath)
        }

    /// Read a V1 `osm_model.json` from an in-memory string and parse
    /// it into a V2 Catalog. Used by the golden-file test to keep the
    /// regression surface hermetic.
    let readJson (json: string) : Task<Result<Catalog>> =
        task {
            use _ = Bench.scope "compose.readJson"
            return! readStep.Read (CatalogReader.SnapshotJson json)
        }

    /// Write a single file at `absPath`, creating parent directories as needed.
    /// The default `IO` implementation calls BCL `File.WriteAllText`; tests
    /// inject alternatives to simulate failures mid-stream.
    type FileWriter = string -> string -> unit

    let private defaultFileWriter : FileWriter =
        fun (absPath: string) (body: string) -> File.WriteAllText(absPath, body)

    /// Build a unique staging-directory path in `outputDir`'s parent. The
    /// dot-prefix hides the directory from casual `ls`; the short GUID
    /// suffix prevents collision under concurrent writes against the same
    /// `outputDir`. Same-parent placement keeps `Directory.Move(staging,
    /// outputDir)` on the same filesystem volume — POSIX `rename(2)` is
    /// atomic in that case, and .NET delegates to it.
    let private buildStagingDir (outputDir: string) : string =
        let parent =
            match Path.GetDirectoryName outputDir with
            | null -> "."
            | "" -> "."
            | p -> p
        let baseName =
            match Path.GetFileName outputDir with
            | null -> "out"
            | "" -> "out"
            | n -> n
        let suffix = System.Guid.NewGuid().ToString("N").Substring(0, 12)  // LINT-ALLOW: transient staging-dir suffix for the atomic publish-rename; never appears in any emitted artifact (the staging dir is renamed to the deterministic outputDir, so T1 byte-determinism holds); not a DatabaseNameGenerator site (filesystem path, not a DB name)
        Path.Combine(parent, sprintf ".%s.staging-%s" baseName suffix)

    let private writeAllToStaging
        (writeFile: FileWriter)
        (stagingDir: string)
        (outputDir: string)
        (outputs: Outputs)
        : string list =
        // Bundle entries (per-kind .sql + manifest)
        let bundleFinalPaths =
            outputs.SsdtBundle
            |> Map.toList
            |> List.map (fun (relPath, body) ->
                let stagingPath = Path.Combine(stagingDir, relPath)
                match Path.GetDirectoryName stagingPath with
                | null -> ()
                | parent when System.String.IsNullOrEmpty parent -> ()
                | parent -> Directory.CreateDirectory parent |> ignore
                writeFile stagingPath body
                Path.Combine(outputDir, relPath))
        // AC-X1 — the data/seed bundle (idempotent CDC-aware MERGE scripts).
        // Empty unless the operator opted into data emission; same per-path
        // staging + directory-creation discipline as the SSDT bundle.
        let dataFinalPaths =
            outputs.DataBundle
            |> Map.toList
            |> List.map (fun (relPath, body) ->
                let stagingPath = Path.Combine(stagingDir, relPath)
                match Path.GetDirectoryName stagingPath with
                | null -> ()
                | parent when System.String.IsNullOrEmpty parent -> ()
                | parent -> Directory.CreateDirectory parent |> ignore
                writeFile stagingPath body
                Path.Combine(outputDir, relPath))
        // V2 IR JSON
        let jsonOpts = System.Text.Json.JsonSerializerOptions(WriteIndented = true)
        let jsonStaging = Path.Combine(stagingDir, ArtifactPath.json)
        writeFile jsonStaging (outputs.Json.ToJsonString(jsonOpts))
        // Distributions
        let distributionsStaging = Path.Combine(stagingDir, ArtifactPath.distributions)
        writeFile distributionsStaging (outputs.Distributions.ToJsonString(jsonOpts))
        // Chapter 5+ slice 5.13.remediation-emitter — per-finding
        // UPDATE/DELETE/SELECT (SQL text artifact, not JSON).
        let remediationStaging = Path.Combine(stagingDir, ArtifactPath.remediation)
        writeFile remediationStaging outputs.RemediationSql
        // Chapter 5+ slice 5.13.summary-formatter — 6-bucket rollup
        // prose (plain-text artifact).
        let summaryStaging = Path.Combine(stagingDir, ArtifactPath.summary)
        writeFile summaryStaging outputs.SummaryText
        // H-032 — config-edit suggestion document (JSON).
        let suggestConfigStaging = Path.Combine(stagingDir, ArtifactPath.suggestConfig)
        writeFile suggestConfigStaging (outputs.SuggestConfigJson.ToJsonString(jsonOpts))
        // The Model Fidelity Report — structured + the rolled-up text. Default-on;
        // written alongside the other run artifacts on every BUNDLE emission
        // (full-export / emit). The live in-place `migrate` differential has no
        // output directory and no profiled source, so it emits no fidelity artifact
        // — its provenance is the recorded episode + RefactorLog, not a bundle. (The
        // prior comment claimed "full-export / migrate"; migrate never reaches this
        // writer — it runs ALTERs against a live DB. THE_CLI distinction: bundle vs
        // live-differential.)
        let fidelityJsonStaging = Path.Combine(stagingDir, ArtifactPath.fidelityJson)
        writeFile fidelityJsonStaging (ModelFidelity.toJsonString outputs.Fidelity)
        let fidelityTextStaging = Path.Combine(stagingDir, ArtifactPath.fidelityText)
        writeFile fidelityTextStaging (String.concat "\n" (ModelFidelity.render outputs.Fidelity))
        // The FAITHFUL catalog snapshot (`CatalogCodec`) — the persisted, round-
        // trippable model for drift-diffing two publish dirs (`diff A/ B/`).
        // Default-on like fidelity; written on every bundle emission. T1-deterministic.
        let catalogSnapshotStaging = Path.Combine(stagingDir, ArtifactPath.catalogSnapshot)
        writeFile catalogSnapshotStaging outputs.CatalogSnapshot
        // The compiled .dacpac (operator-gated; absent by default). A binary
        // artifact: written directly rather than through the text `FileWriter`
        // seam, inside the same staging directory so the atomic-replace
        // contract covers it.
        let dacpacFinalPaths =
            match outputs.Dacpac with
            | None -> []
            | Some bytes ->
                File.WriteAllBytes(Path.Combine(stagingDir, ArtifactPath.dacpac), bytes)
                [ Path.Combine(outputDir, ArtifactPath.dacpac) ]
        // The SDK-style SSDT project + its post-deploy (operator-gated, absent by
        // default). Text artifacts written into the same staging dir so the
        // atomic-replace contract covers them; final paths projected post-swap.
        let sqlprojFinalPaths =
            [ ArtifactPath.sqlproj,    outputs.Sqlproj
              ArtifactPath.postDeploy, outputs.PostDeploy ]
            |> List.choose (fun (rel, body) ->
                body
                |> Option.map (fun b ->
                    writeFile (Path.Combine(stagingDir, rel)) b
                    Path.Combine(outputDir, rel)))
        // Final-path projection (under the eventual outputDir, post-swap)
        let jsonFinal          = Path.Combine(outputDir, ArtifactPath.json)
        let distributionsFinal = Path.Combine(outputDir, ArtifactPath.distributions)
        let remediationFinal   = Path.Combine(outputDir, ArtifactPath.remediation)
        let summaryFinal       = Path.Combine(outputDir, ArtifactPath.summary)
        let suggestConfigFinal = Path.Combine(outputDir, ArtifactPath.suggestConfig)
        let fidelityJsonFinal  = Path.Combine(outputDir, ArtifactPath.fidelityJson)
        let fidelityTextFinal  = Path.Combine(outputDir, ArtifactPath.fidelityText)
        let catalogSnapshotFinal = Path.Combine(outputDir, ArtifactPath.catalogSnapshot)
        bundleFinalPaths @ dataFinalPaths @ dacpacFinalPaths @ sqlprojFinalPaths @ [ jsonFinal; distributionsFinal; remediationFinal; summaryFinal; suggestConfigFinal; fidelityJsonFinal; fidelityTextFinal; catalogSnapshotFinal ]

    let private safeCleanupStaging (stagingDir: string) : unit =
        if Directory.Exists stagingDir then
            try Directory.Delete(stagingDir, recursive = true)
            with _ -> ()  // best-effort; surface via the original error, not via cleanup secondary

    /// Write `outputs` to `outputDir` atomically. **L3-Boundary-AtomicEmission**:
    /// a successful call produces an `outputDir` containing exactly the
    /// artifacts named in `outputs`. A failed call leaves `outputDir`
    /// byte-identical to its pre-call state (or absent if it didn't exist
    /// before). Failure modes: file-system errors during multi-file writes,
    /// serialization failures, or any exception within the staging phase.
    ///
    /// Mechanism: write all artifacts to a sibling staging directory; on
    /// success, atomically replace `outputDir` with the staging directory;
    /// on any failure, delete the staging directory. The staging directory
    /// is in the same parent as `outputDir` so the rename stays within one
    /// filesystem volume (POSIX `rename(2)` is atomic there; .NET
    /// `Directory.Move` delegates to it).
    ///
    /// **Semantics on success**: `outputDir` is *replaced*, not merged. Any
    /// pre-existing files in `outputDir` are gone after a successful write.
    /// Operators should treat `outputDir` as V2-owned; place external state
    /// elsewhere.
    let writeWith
        (writeFile: FileWriter)
        (outputDir: string)
        (outputs: Outputs)
        : Result<string list> =
        use _ = Bench.scope "compose.write"
        let stagingDir = buildStagingDir outputDir
        try
            Directory.CreateDirectory stagingDir |> ignore
            let finalPaths = writeAllToStaging writeFile stagingDir outputDir outputs
            // All staging writes succeeded. Commit:
            //   (a) delete existing outputDir if present;
            //   (b) Directory.Move(staging, outputDir).
            // The window between (a) and (b) is the only non-atomic gap;
            // a crash there leaves outputDir absent (operator can retry).
            if Directory.Exists outputDir then
                Directory.Delete(outputDir, recursive = true)
            Directory.Move(stagingDir, outputDir)
            Result.success finalPaths
        with ex ->
            safeCleanupStaging stagingDir
            Result.failureOf (
                ValidationError.create
                    "pipeline.compose.write.atomicFailure"
                    (sprintf
                        "Compose.write failed; output directory '%s' left unchanged. Cause: %s"
                        outputDir
                        ex.Message))

    /// Write the SSDT bundle (per-kind .sql + manifest.json) plus the
    /// V2 IR JSON + Distributions artifacts to a directory. Atomic per
    /// `writeWith`. Returns the absolute paths of every written file on
    /// success, or a structured error on failure.
    let write (outputDir: string) (outputs: Outputs) : Result<string list> =
        writeWith defaultFileWriter outputDir outputs

    /// Full end-to-end: read V1 JSON from disk, project, write
    /// artifacts to the output directory. Returns the artifact paths
    /// on success; surfaces parse failures via the result.
    let run (jsonPath: string) (outputDir: string) : Task<Result<string list>> =
        task {
            let! parsed = read jsonPath
            match parsed with
            | Ok catalog ->
                let outputs = project EmissionPolicy.empty catalog
                return write outputDir outputs
            | Error errors ->
                return Result.failure errors
        }

    /// Chapter A.4.7' slice ζ — skeleton-only emit. Reads JSON,
    /// projects via `projectSkeleton`, writes to `outputDir`. The
    /// resulting bundle reflects the V2 baseline before any operator
    /// intent landed; consumed by the CLI's `--skeleton-only` flag.
    let runSkeletonOnly (jsonPath: string) (outputDir: string) : Task<Result<string list>> =
        task {
            let! parsed = read jsonPath
            match parsed with
            | Ok catalog ->
                let outputs = projectSkeleton catalog
                return write outputDir outputs
            | Error errors ->
                return Result.failure errors
        }

    /// The bundle / skeleton emit from an already-resolved `Catalog` — the
    /// model-source-agnostic cores of `run` / `runSkeletonOnly`. The model may
    /// have been read live from OSSYS (primary) or from the `osm_model.json`
    /// file (fallback); both arrive here as a `Catalog`.
    let runFromCatalog (catalog: Catalog) (outputDir: string) : Result<string list> =
        write outputDir (project EmissionPolicy.empty catalog)

    let runSkeletonOnlyFromCatalog (catalog: Catalog) (outputDir: string) : Result<string list> =
        write outputDir (projectSkeleton catalog)


    /// Apply config-driven catalog rewrites (today: table renames).
    /// Empty overrides short-circuit to the input catalog unchanged.
    /// Errors aggregate from boundary-mapping (`RenameBinding.fromConfig`)
    /// and from pass-level validation, surfaced through the registered
    /// transform's Diagnostics layer per chapter A.4.7' slice η.
    let private applyRenames
        (cfg: Config.Config)
        (catalog: Projection.Core.Catalog)
        : Result<Projection.Core.Catalog> =
        match cfg.Overrides.TableRenames with
        | [] -> Result.success catalog
        | renames ->
            renames
            |> RenameBinding.fromConfig
            |> Result.bind (fun specs ->
                let lineage = (Projection.Core.Passes.TableRename.registered specs).Run catalog
                let diag = lineage.Value
                let errors =
                    diag.Entries
                    |> List.filter (fun e -> e.Severity = Projection.Core.DiagnosticSeverity.Error)
                if List.isEmpty errors then
                    Result.success diag.Value
                else
                    errors
                    |> List.map (fun e ->
                        let metadata = e.Metadata |> Map.map (fun _ v -> Some v)
                        ValidationError.createWithMetadata e.Code e.Message metadata)
                    |> Error)

    /// S6.3 — the SsKeys of kinds an operator pinned via a PHYSICAL-form
    /// `tableRenames` override (`{ "from": { schema, table }, "to": … }`). The
    /// physical `from` is resolved against the ORIGINAL (pre-`applyRenames`,
    /// pre-`LogicalTableEmission`) catalog — the OSSYS physical coordinate the
    /// operator named — so the `LogicalTableEmission` chain step can skip these
    /// kinds and the operator's physical target survives into the emitted table.
    /// (A physical `from` matched against the post-emission catalog would miss,
    /// because `LogicalTableEmission` already rewrote `Kind.Physical.Table` to the
    /// logical name — that mismatch IS the clobber this set repairs.) Logical-form
    /// renames are NOT pinned (the prompt scopes the fix to physical-form); the
    /// empty set is the byte-identical default for every config without one.
    let private physicalRenamePins
        (cfg: Config.Config)
        (catalog: Projection.Core.Catalog)
        : Set<SsKey> =
        match cfg.Overrides.TableRenames with
        | [] -> Set.empty
        | renames ->
            match RenameBinding.fromConfig renames with
            | Error _ -> Set.empty   // binding errors surface in `applyRenames`; no pins.
            | Ok specs ->
                let physicalSources =
                    specs
                    |> List.choose (fun s ->
                        match s.Key with
                        | Projection.Core.Passes.TableRename.Physical source -> Some source
                        | Projection.Core.Passes.TableRename.Logical _       -> None)
                    |> Set.ofList
                Projection.Core.Catalog.allKinds catalog
                |> List.filter (fun k -> Set.contains k.Physical physicalSources)
                |> List.map (fun k -> k.SsKey)
                |> Set.ofList

    /// Build the full `Policy` aggregate from a parsed `Config` and
    /// the loaded `Catalog`. Wires the tightening axis (Chapter C
    /// slice C.1) + insertion axis (Chapter C slice C.5); Selection /
    /// Emission / UserMatching axes remain dormant pending operator-
    /// pull triggers per the dormant-config-section sweep.
    /// **Second consumer (§5.6).** Made public for the `policy-diff` verb,
    /// which binds two operator `Policy` values from two configs against a
    /// shared catalog. Previously private to `runWithConfigCore`.
    let buildPolicyFromConfig
        (cfg: Config.Config)
        (catalog: Catalog)
        : Result<Policy> =
        validation {
            let! tightening = TighteningBinding.fromConfig catalog cfg.Policy.Tightening
            and! insertion  = InsertionPolicyBinding.fromConfig cfg
            // AC-X1 — translate the config's data-emission toggles into the
            // EmissionPolicy. `staticSeeds` / `migrationDependencies` /
            // `bootstrap` turning on enables `EmitData`; `DataComposition`
            // selects which data emitters fire (Static included ⇒ AllRemaining;
            // Static off but Migration/Bootstrap on ⇒ AllExceptStatic).
            let emitData =
                cfg.Emission.StaticSeeds
                || cfg.Emission.MigrationDependencies
                || cfg.Emission.Bootstrap
            // Bootstrap-always (2026-06-14) — the SINGLE composition derivation
            // (`Config.dataCompositionOf`), shared with the Bootstrap-lane
            // hydration so dispatch and row-scope can never drift. `AllData`
            // (Bootstrap covers everything) is now reachable via
            // `emission.bootstrapAllData`.
            let dataComposition = Config.dataCompositionOf cfg
            // NM-02 (2026-06-13) — translate the schema / diagnostics emission
            // toggles into the EmissionPolicy, mirroring the data-lane wiring
            // above. `emission.ssdt` gates the CREATE/SSDT schema bundle;
            // any of `emission.decisionLog` / `.opportunities` / `.validations`
            // keeps the diagnostic-artifact emission on. Both config families
            // default `true`, so the default policy stays schema + diagnostics
            // (byte-identical to `EmissionPolicy.empty`).
            let emitSchema = cfg.Emission.Ssdt
            let emitDiagnostics =
                cfg.Emission.DecisionLog
                || cfg.Emission.Opportunities
                || cfg.Emission.Validations
            return {
                Policy.empty with
                    Tightening = tightening
                    Insertion  = insertion
                    // AC-D7 — the operator's convergent-delete scope rides the
                    // Emission axis; absent (the default) the MERGE stays
                    // upsert-only, byte-identical. Reconciliation slice 2 —
                    // `emission.includePlatformAutoIndexes` threads to the
                    // collapsed seam (default true = current behavior).
                    Emission   = { Policy.empty.Emission with
                                     EmitSchema = emitSchema
                                     EmitData = emitData
                                     EmitDiagnostics = emitDiagnostics
                                     DataComposition = dataComposition
                                     DeleteScope = cfg.Emission.DeleteScope
                                     IncludePlatformAutoIndexes = cfg.Emission.IncludePlatformAutoIndexes
                                     // NM-38 — `emission.renderConstraintsElegant`
                                     // threads the operator's constraint-rendering
                                     // opt-out (default true = current behavior).
                                     RenderConstraintsElegant = cfg.Emission.RenderConstraintsElegant
                                     // NM-70 — `emission.identityAnnotations`
                                     // threads the operator's identity-annotation
                                     // gate (default true = current behavior;
                                     // false is the named downgrade).
                                     EmitIdentityAnnotations = cfg.Emission.EmitIdentityAnnotations
                                     // NM-73 — `emission.dataVerification` threads
                                     // the operator's drift-guard posture (default
                                     // Standard = byte-identical; ValidateBeforeApply
                                     // prepends the symmetric-EXCEPT THROW guard).
                                     DataVerification = cfg.Emission.DataVerification
                                     // Wave-3 3.4 — `emission.tolerance` threads the
                                     // operator's accepted-divergence set into the
                                     // per-run residual; absent ⇒ the permissive
                                     // dual-track default (byte-identical reporting).
                                     ConfiguredTolerance = defaultArg cfg.Emission.Tolerance Tolerance.permissive
                                     // 2026-06-25 — `emission.dataStaging` threads the
                                     // large-kind staging posture (default auto > 1000
                                     // rows = byte-identical; inline/tempTable pin it).
                                     DataStaging = cfg.Emission.DataStaging }
            }
        }

    /// THE_CONFIG_CONTROL_PLANE §6 — the SINGLE shaping-overlay bind-all
    /// combinator. Binds the policy / emission-folders / transform-groups
    /// triple against an already-rename-applied catalog, accumulating every
    /// binder's `ValidationError`s (FsToolkit `validation { }`; the
    /// applicative `and!` concatenates in `policy @ folders @ groups` order).
    ///
    /// **Convergence-by-construction (the §6 named risk).** The two non-bundle
    /// shaping sites that thread *exactly this triple* — `projectWithConfig`
    /// (the bundle emit) and `applyShapingToCatalog` (the migrate / preview
    /// catalog shape) — BOTH funnel through here, so their bind-set + error-
    /// accumulation mechanism is structurally identical and cannot drift apart.
    /// `runWithConfigCore` threads a SUPERSET (it additionally binds
    /// `SpecialCircumstancesBinding` — the overrides axis — interleaved in its
    /// native `policy @ overrides @ folders @ groups` order), so it keeps its
    /// own `validation { }` block rather than this triple; the binding SET it
    /// threads is unchanged from before this compression.
    let private bindShapingTriple
        (shaping: Config.Config)
        (renamedCatalog: Catalog)
        : Result<Policy * EmissionFolders * TransformGroups> =
        validation {
            let! policy  = buildPolicyFromConfig shaping renamedCatalog
            and! folders = EmissionFoldersBinding.fromConfig renamedCatalog shaping
            and! groups  = TransformGroupsBinding.fromConfig shaping
            return (policy, folders, groups)
        }

    /// Open the out-of-band source connection and enrich `Profile.empty`
    /// via the `LiveProfiler` adapter. An unreachable / malformed
    /// connection is a *named failure* (`pipeline.profiler.connectionFailed`),
    /// never a silent fallback to empty.
    let private openProfilerConnection
        (connectionString: string)
        ()
        : Task<Result<SqlConnection>> =
        task {
            try
                let cnn = new SqlConnection(connectionString)
                do! cnn.OpenAsync()
                return Result.success cnn
            with ex ->
                return
                    Result.failureOf
                        (ValidationError.create
                            "pipeline.profiler.connectionFailed"
                            (sprintf "live profiling could not reach the source database: %s" ex.Message))
        }

    let private profileFromLiveConnection
        (maxConcurrency: int)
        (hydratedRows: Map<SsKey, StaticRow list>)
        (connectionString: string)
        (catalog: Catalog)
        : Task<Result<Profile>> =
        task {
            try
                // Single-scan unification: when the data lanes already
                // hydrated rows, the evidence DERIVES from them
                // (`attachDerived` — one global reflection query; a counted
                // live fallback covers kinds outside the hydrated set).
                // With no hydrated rows (data lanes off / file-sourced), the
                // live scan paths stand: `profiler.maxConcurrency` bounded
                // per-kind discovery, `1` = strictly serial.
                let capped = max 1 maxConcurrency
                if not (Map.isEmpty hydratedRows) then
                    let options =
                        { Projection.Adapters.Sql.SqlProfilerOptions.defaults with
                            MaxConcurrency = capped }
                    return!
                        Projection.Adapters.Sql.LiveProfiler.attachDerived
                            options (openProfilerConnection connectionString) hydratedRows catalog Profile.empty
                elif capped = 1 then
                    use cnn = new SqlConnection(connectionString)
                    do! cnn.OpenAsync()
                    return! Projection.Adapters.Sql.LiveProfiler.attach cnn catalog Profile.empty
                else
                    let options =
                        { Projection.Adapters.Sql.SqlProfilerOptions.defaults with
                            MaxConcurrency = capped }
                    return!
                        Projection.Adapters.Sql.LiveProfiler.attachConcurrent
                            options (openProfilerConnection connectionString) catalog Profile.empty
            with ex ->
                return
                    Result.failureOf
                        (ValidationError.create
                            "pipeline.profiler.connectionFailed"
                            (sprintf "live profiling could not reach the source database: %s" ex.Message))
        }

    /// Acquire the source-environment `Profile` for a run. When
    /// `profiler.provider = "live"` (`Config.LiveProfilerProvider`), opens
    /// the out-of-band source connection (`Config.SourceConnectionStringEnvVar`;
    /// D9 — never from the config document) and profiles against the
    /// *source* catalog, whose physical coordinates match the live
    /// database. `SsKey` identity is rename-invariant (A1), so the
    /// resulting profile keys still resolve when the manifest is built
    /// from the renamed catalog. Any other provider carries `Profile.empty`
    /// forward as the no-evidence base case; a `"live"` provider with a
    /// missing connection is a named failure.
    let private acquireProfile (cfg: Config.Config) (hydratedRows: Map<SsKey, StaticRow list>) (catalog: Catalog) : Task<Result<Profile>> =
        task {
            match cfg.Profiler.Provider with
            | Config.ProfilerProvider.Fixture ->
                return Result.success Profile.empty
            | Config.ProfilerProvider.Live ->
                // Coalesce the nullable env read to a non-null string (F#9
                // nullness) so the non-empty branch passes a non-null value.
                let connectionString =
                    System.Environment.GetEnvironmentVariable Config.SourceConnectionStringEnvVar
                    |> Option.ofObj
                    |> Option.defaultValue ""
                if connectionString = "" then
                    return
                        Result.failureOf
                            (ValidationError.create
                                "pipeline.profiler.connectionMissing"
                                (sprintf
                                    "profiler.provider = \"%s\" requires the %s environment variable (D9: connection sources are out-of-band)."
                                    Config.LiveProfilerProvider
                                    Config.SourceConnectionStringEnvVar))
                else
                    return! profileFromLiveConnection cfg.Profiler.MaxConcurrency hydratedRows connectionString catalog
        }

    /// Synchronous core for `runWithConfig`. Extracted from the `task { }`
    /// block to keep the async surface minimal and allow static state-machine
    /// compilation (avoids FS3511 — deeply nested matches inside `task { }`
    /// prevent static compilation of the resumable state machine). The
    /// acquired `Profile` is threaded in by the async caller.
    /// PL-1 — returns the post-chain `ComposeState` beside the report so the
    /// combined verbs' load/store legs consume the publish's own composed
    /// state instead of re-deriving it (`runWithConfig` drops it for the
    /// report-only callers).
    let private runWithConfigCore
        (cfg: Config.Config)
        (parsed: Result<Catalog>)
        (bootstrapLane: DataComposer.BootstrapLane)
        (migration: Projection.Targets.Data.MigrationDependencyContext)
        (profile: Profile)
        : Result<RunReport * ComposeState> =
        match parsed with
        | Error errors -> Result.failure errors
        | Ok catalog ->
            // S6.3 — pin the operator's physical-form renames against the
            // ORIGINAL catalog (before applyRenames) so LogicalTableEmission
            // skips them and the operator's physical target survives.
            let pins = physicalRenamePins cfg catalog
            match applyRenames cfg catalog with
            | Error errors -> Result.failure errors
            | Ok renamedCatalog ->
                // THE_CONFIG_CONTROL_PLANE §6 — `runWithConfigCore` threads the
                // SUPERSET binding set (the `policy/folders/groups` triple PLUS
                // the `SpecialCircumstances` overrides axis the live/migrate
                // sites omit). The `validation { }` applicative accumulates in
                // the historical `policy @ overrides @ folders @ groups` order;
                // the binding SET is unchanged from before the M9 compression.
                let boundR =
                    validation {
                        let! policy    = buildPolicyFromConfig cfg renamedCatalog
                        and! overrides = SpecialCircumstancesBinding.fromConfig renamedCatalog cfg
                        and! folders   = EmissionFoldersBinding.fromConfig renamedCatalog cfg
                        and! groups    = TransformGroupsBinding.fromConfig cfg
                        return (policy, overrides, folders, groups)
                    }
                match boundR with
                | Ok (policy, overrides, folders, groups) ->
                    let outputs, finalState =
                        projectWithStateWithPinsAndBootstrapLane pins policy profile folders groups migration bootstrapLane renamedCatalog
                    // `emission.dacpac: true` — compile the .dacpac over the SAME
                    // emitted catalog the SSDT step projected (the post-chain
                    // catalog under the identical platform-auto-index filter —
                    // the identical POLICY too, since reconciliation slice 2
                    // collapsed the seam onto `policy.Emission`).
                    // Conditional by operator opt-in; a DacFx failure fails the
                    // run loud, never a silent bundle-without-package.
                    let dacpacR : Result<byte[] option> =
                        if not cfg.Emission.Dacpac then Result.success None
                        else
                            // F3 (audit 2026-06-17) — same bound emission seam as
                            // the main path, so the dacpac arm cannot drift to a
                            // different (unregistered) post-chain rewrite set.
                            EmissionSeam.apply policy.Emission finalState.Catalog
                            |> DacpacEmitter.emit
                            |> Result.map Some
                    match dacpacR with
                    | Error errors -> Result.failure errors
                    | Ok dacpac ->
                    let outputs = { outputs with Dacpac = dacpac }
                    // `emission.sqlproj: true` — drop a buildable SDK-style SSDT
                    // project (the `.sqlproj` + its post-deploy) over the data
                    // lanes the publish already emitted, so the operator's
                    // `dotnet build`/`sqlpackage` path is one config flag, not a
                    // manual assembly. Derived from the ACTUAL data-lane file set
                    // (`outputs.DataBundle`), so the post-deploy `:r` includes can
                    // never dangle. The Bootstrap lane is a SEPARATE post-publish
                    // step: `None`'d out of the schema build, but NOT `:r`-included
                    // by the post-deploy (operator runs it after publish).
                    let outputs =
                        if not cfg.Emission.Sqlproj then outputs
                        else
                            let dataLanes =
                                outputs.DataBundle |> Map.toList |> List.map fst |> List.sort
                            let postDeployLanes =
                                dataLanes |> List.filter (fun p -> p <> "Data/Bootstrap.sql")
                            let postDeploy =
                                if List.isEmpty postDeployLanes then None
                                else Some (PostDeployEmitter.renderIncludes postDeployLanes)
                            let sqlproj = SqlprojEmitter.emit dataLanes (Option.isSome postDeploy)
                            { outputs with Sqlproj = Some sqlproj; PostDeploy = postDeploy }
                    // PL-4 (S46/S47/S37/S49) — the FK lookup triple, the
                    // per-reference resolutions, and the decision overlay
                    // each derive ONCE here and thread to the three FK
                    // diagnostics siblings (previously: three lookup
                    // rebuilds, a fourth allKinds walk, per-reference
                    // re-resolution at two sites, and two back-to-back
                    // `ofComposeState` projections over the same state).
                    // These derive over finalState.Catalog — the DIAGNOSTIC
                    // plane's value; the emit step's interior lookups ride
                    // its own EmittedCatalog value (K26: receipts match on
                    // the VALUE, not the function).
                    let fkLookups = SsdtDdlEmitter.FkEmissionLookups.ofCatalog finalState.Catalog
                    let fkResolutions = SsdtDdlEmitter.fkResolutionsUsing fkLookups
                    let decisionOverlay = DecisionOverlay.ofComposeState finalState
                    let diagnostics =
                        // A7 (no-silent-drop) — surface the inert module-filter
                        // flags on the structured diagnostic stream.
                        (ModuleFilterBinding.inertFlagNote cfg.Model
                         |> Option.map (DiagnosticEntry.create "config:model" DiagnosticSeverity.Info "moduleFilter.flagsInert")
                         |> Option.toList)
                        // WP6 step 4 — data on + file-sourced model ⇒ a named
                        // hydration skip (never silent emptiness). Config-
                        // derived; the actual graft runs in the async caller
                        // (`readAndHydrateConfigModel`).
                        @ Hydration.diagnostics cfg
                        @ SpecialCircumstancesDiagnostics.emit overrides finalState
                        @ InactiveAttributeDiagnostics.emit profile
                        @ FkSelectivityDiagnostics.emit profile    // H-025
                        @ JointDependencyDiagnostics.emit profile   // H-026
                        // Wave-2 slice 2.5(b) — the FK silent-drop witness over
                        // the emitted catalog (slice-μ retired). Pure sibling of
                        // the emitter port; A18 holds.
                        @ SsdtDdlEmitter.foreignKeyDropDiagnosticsUsing fkLookups fkResolutions
                        // 6.A.9 — the DECISION-driven FK-drop audit trail. A
                        // tightening Decision that drops an FK the source
                        // enforced is a safety change; surface one Warning per
                        // dropped decision so it is never silent at emission.
                        // (Reconciliation slice 1 narrows the claim: Warning
                        // `decision.fkDropped` only when the source really
                        // enforced it; Info `decision.fkNotIntroduced` for
                        // logical-only references.)
                        @ SsdtDdlEmitter.foreignKeyDecisionDropDiagnosticsUsing
                            decisionOverlay fkLookups.AllKinds
                        // Reconciliation slice 1 — the FK-name collision
                        // tripwire (schema-scoped constraint-name uniqueness;
                        // one Error per participating reference, never a
                        // silent dedupe).
                        @ SsdtDdlEmitter.foreignKeyNameCollisionDiagnosticsUsing
                            decisionOverlay fkResolutions
                        // NM-70 (WP5) — the named downgrade. When the operator
                        // omits the identity annotations, the `Projection.*`
                        // extended properties are NOT written, so identity
                        // recovery degrades to name-derived SsKeys (no
                        // persisted SsKey to read back on roundtrip). One
                        // Warning per run; never a silent suppression.
                        @ (if policy.Emission.EmitIdentityAnnotations then []
                           else
                               [ DiagnosticEntry.create
                                   "emitter:ssdtDdlEmitter"
                                   DiagnosticSeverity.Warning
                                   "emission.identityAnnotations.omitted"
                                   "Identity annotations omitted: the Projection.SsKey / Projection.LogicalName extended properties were not emitted; identity recovery degrades to name-derived SsKeys (no persisted SsKey to read back on roundtrip)." ])
                    match write cfg.Output.Dir outputs with
                    // NM-34b (live) — `ReadCatalog = catalog`: the source model
                    // the run READ (pre-rename), surfaced so the run boundary can
                    // hash its canonical form into the live-path input digest.
                    | Ok paths    -> Result.success ({ Paths = paths; Diagnostics = diagnostics; Manifest = outputs.Manifest; Trail = outputs.Trail; PassDiagnostics = outputs.PassEntries; ReadCatalog = catalog }, finalState)
                    | Error errors -> Result.failure errors
                | Error errors -> Result.failure errors

    /// Full end-to-end driven by a parsed `Config`. Reads `Model.Path`,
    /// applies config-driven catalog rewrites (rename), binds operator
    /// `Policy` (tightening — Chapter C slice C.1) + operator
    /// `SpecialCircumstances` (missing-PK / circular-deps
    /// acknowledgements — Chapter C slice C.2) + operator
    /// `EmissionFolders` (per-kind SSDT folder overrides — Chapter C
    /// slice C.3), projects through the policy-aware chain capturing
    /// the post-chain `ComposeState`, emits special-circumstances and
    /// inactive-attribute diagnostics, writes to `Output.Dir`, returns
    /// the paths + diagnostic stream as a `RunReport`.
    /// Emit one orchestration stage marker (info-level start / end
    /// category event) per `docs/logging-format.md` §7.2-§7.5. The
    /// matching `summary.stageCompleted` + §10 stage-table entry is
    /// `LogSink.recordStageEvent`. Slice 2 (stage-boundary events).
    let private emitStageMarker
        (category: LogSink.Category)
        (code: string)
        (phase: LogSink.Phase)
        (payload: Map<string, objnull>)
        : unit =
        LogSink.emit { LogSink.envelope LogSink.Info category code payload with Phase = phase }

    /// Apply the config-driven module-selection filter (`model.modules` +
    /// the system / inactive include flags) to the read catalog — the
    /// `ModuleFilter.apply` Selection seam (pillar 9). An empty `model.modules`
    /// is the all-permissive identity (`ModuleFilterBinding.fromConfig` returns
    /// `ModuleFilter.empty`, and `ModuleFilter.apply` short-circuits to the
    /// input), so the default config is byte-identical. A non-empty selection
    /// narrows the catalog to the named modules + their per-module entity
    /// subsets; operator-supplied-name mismatches surface as structured
    /// `moduleFilter.*` errors (fail-loud, never a silent empty catalog).
    /// THE_CONFIG_CONTROL_PLANE §6 (S3) — the SINGLE shared module-filter
    /// seam. `runWithConfig`'s model-read path (`readConfigModel`) and the
    /// flow dispatch's resolved-catalog path (`Program.needCatalog`) both
    /// route the read catalog through here, so a `model.modules` scope
    /// narrows the bundle and the live/docker/migrate catalogs identically.
    /// An empty `model.modules` is the all-permissive identity
    /// (`ModuleFilterBinding`/`ModuleFilter.apply` short-circuit), so the
    /// default config is byte-identical.
    let applyModuleFilter
        (cfg: Config.Config)
        (catalog: Catalog)
        : Result<Catalog> =
        ModuleFilterBinding.fromConfig cfg.Model
        |> Result.bind (fun opts -> ModuleFilter.apply opts catalog)

    /// The full-export model read under the live-OSSYS-primary / file-fallback
    /// policy (V1_INPUT_DEPRECATION.md §3). `cfg.Model.Ossys` set ⇒ read live
    /// from OSSYS (`LiveModelRead`, V1-free); else read `cfg.Model.Path` (the
    /// `osm_model.json` fallback). The read catalog passes through the
    /// config-driven module-selection filter (identity on the default config).
    /// Byte-identical to the prior `read cfg.Model.Path` when `Ossys = None` and
    /// no `model.modules` selection is declared.
    let readConfigModel (cfg: Config.Config) : Task<Result<Catalog>> =
        task {
            let! read =
                match cfg.Model.Ossys, cfg.Model.Path with
                | Some connSpec, _ ->
                    // Reconciliation slice 4 (DECISIONS 2026-06-13; C4) —
                    // the declared module/entity scope pushes down to the
                    // rowsets SQL at query time (extraction-cost reduction;
                    // opt-in through non-empty model.modules, the A7
                    // polarity). `applyModuleFilter` below REMAINS the
                    // semantic seam — double enforcement, V1's own
                    // precedent.
                    LiveModelRead.fromConnSpecWith (SnapshotScopeBinding.fromModel cfg.Model) connSpec
                | None, Some path -> read path
                | None, None ->
                    Task.FromResult
                        (Result.failureOf
                            (ValidationError.create
                                "pipeline.config.modelNoSource"
                                "model needs `path` (osm_model.json) or `ossys` (live OSSYS connection)."))
            return read |> Result.bind (applyModuleFilter cfg)
        }

    /// WP6 step 4 (DECISIONS 2026-06-13) — the full-export model read followed
    /// by data hydration. Reads the catalog (`readConfigModel`) then grafts
    /// live static-entity rows when data emission is on AND the model is
    /// OSSYS-sourced (`Hydration.hydrateCatalog` — a SECOND connection from
    /// `cfg.Model.Ossys`, streaming owned static kinds via `Ingestion`, never
    /// `ReadSide.read`). A file-sourced or data-off run is the identity on the
    /// catalog (the file-sourced skip is NAMED in `Hydration.diagnostics`,
    /// surfaced by `runWithConfigCore`). This is the SINGLE hydration seam
    /// shared by the publish path (the extract stage) and the store leg
    /// (`emittedSeedPlan`), so the deployed seed never drifts from the
    /// published one (the parity duty).
    /// Bootstrap-always (2026-06-14) — returns BOTH the static-hydrated catalog
    /// (StaticSeeds lane, rows grafted onto `Modality.Static`) AND the Bootstrap
    /// lane's row source (`Hydration.hydrateBootstrapRows`, scoped per
    /// composition). Both callers — the publish path and the store-leg seed plan
    /// — thread the same pair, so the deployed Bootstrap never drifts from the
    /// published one (the parity duty). Empty bootstrap Map on the file-sourced /
    /// data-off path (the named skip is in `Hydration.diagnostics`).
    /// Migration-context wiring (2026-06-15) — completes the data triumvirate.
    /// The operator-curated Migration lane's rows are bound from
    /// `overrides.migrationDependencies.path` against the read catalog
    /// (rename-invariant `SsKey`, A1); the resolved context is threaded the
    /// SAME seam the Bootstrap rows ride (publish + store-leg, for parity) and
    /// its kinds are EXCLUDED from the Bootstrap complement so the three lanes
    /// stay disjoint (the partition law). No path ⇒ the empty context (no-op;
    /// byte-identical). A malformed / unresolvable file fails loud
    /// (`pipeline.migrationDependencies.*`).
    /// PL-2 (S04) — the AllData hydration arm: ONE drain. Under AllData the
    /// Bootstrap complement covers every data-bearing kind (the composer
    /// dispatches the Static lane empty), so drain Bootstrap FIRST over the
    /// ungrafted catalog (eligibility + column lists read marks/attributes,
    /// never populations) and graft the static populations from the rows
    /// already in hand. Module-level (FS3511).
    let private hydrateAllDataArm
        (cfg: Config.Config)
        (topo: TopologicalOrder)
        (migrationKinds: Set<SsKey>)
        (catalog: Catalog)
        : Task<Result<Catalog * Map<SsKey, StaticRow list>>> =
        task {
            let! bootRowsR = Hydration.hydrateBootstrapRowsExcludingUsing topo migrationKinds cfg catalog
            match bootRowsR with
            | Error es -> return Result.failure es
            | Ok bootRows ->
                let! hydratedR = Hydration.hydrateCatalogFromBootstrapRowsUsing topo cfg bootRows catalog
                match hydratedR with
                | Error es -> return Result.failure es
                | Ok hydrated -> return Result.success (hydrated, bootRows)
        }

    /// The default (non-AllData) hydration arm: static graft, then the
    /// Bootstrap drain. Module-level so `readAndHydrateConfigModel`'s task
    /// stays statically compilable in Release (FS3511 — a `let!` inside a
    /// guarded match arm is the named failure shape).
    let private hydrateDefaultArm
        (cfg: Config.Config)
        (hydrationTopo: TopologicalOrder option)
        (migrationKinds: Set<SsKey>)
        (catalog: Catalog)
        : Task<Result<Catalog * Map<SsKey, StaticRow list>>> =
        task {
            let! hydratedR =
                match hydrationTopo with
                | Some topo -> Hydration.hydrateCatalogUsing topo cfg catalog
                | None      -> Hydration.hydrateCatalog cfg catalog
            match hydratedR with
            | Error es -> return Result.failure es
            | Ok hydrated ->
                let! bootRowsR =
                    match hydrationTopo with
                    | Some topo -> Hydration.hydrateBootstrapRowsExcludingUsing topo migrationKinds cfg hydrated
                    | None      -> Hydration.hydrateBootstrapRowsExcluding migrationKinds cfg hydrated
                match bootRowsR with
                | Error es      -> return Result.failure es
                | Ok bootRows   -> return Result.success (hydrated, bootRows)
        }

    /// PL-1 — the first element is the READ catalog (pre-hydration, the value
    /// `readConfigModel` produced): the store leg's emitted-schema plane
    /// derives from it (`applyRenames` over the model as READ — static
    /// populations are a data-lane graft, never part of the recorded schema
    /// plane), so a combined publish+store verb pays for ONE model read.
    let readAndHydrateConfigModel
        (cfg: Config.Config)
        : Task<Result<Catalog * Catalog * Map<SsKey, StaticRow list> * Projection.Targets.Data.MigrationDependencyContext>> =
        task {
            let! parsed = readConfigModel cfg
            match parsed with
            | Error es -> return Result.failure es
            | Ok catalog ->
                match MigrationDependenciesBinding.fromConfig catalog cfg with
                | Error es -> return Result.failure es
                | Ok migration ->
                    // ONE topological order serves BOTH hydration arms: they
                    // run over the same pre-chain catalog (the static graft
                    // changes `Modality` only — never the FK edges the order
                    // derives from), so the second Kahn/Tarjan run was pure
                    // repetition. Data-off / file-sourced publishes compute
                    // nothing.
                    let hydrationTopo =
                        if Hydration.emitDataOf cfg && cfg.Model.Ossys.IsSome then
                            Some ((Projection.Core.Passes.TopologicalOrderPass.runWith Projection.Core.TreatAsCycle catalog).Value)
                        else None
                    let migrationKinds = MigrationDependenciesBinding.kindKeysOf migration
                    // The arm is SELECTED synchronously, then awaited once —
                    // the FS3511-safe shape (`loadMeasureAndRecord` precedent).
                    let arm =
                        match hydrationTopo with
                        | Some topo when Config.dataCompositionOf cfg = AllData ->
                            // PL-2 (S04) — one drain: static graft rides the
                            // Bootstrap rows (see `hydrateAllDataArm`).
                            hydrateAllDataArm cfg topo migrationKinds catalog
                        | _ ->
                            hydrateDefaultArm cfg hydrationTopo migrationKinds catalog
                    let! pairR = arm
                    return pairR |> Result.map (fun (hydrated, bootRows) -> (catalog, hydrated, bootRows, migration))
        }

    /// THE_CONFIG_CONTROL_PLANE §6 (S3) — project a **caller-supplied**
    /// catalog through the unified config's shaping overlays, yielding the
    /// SSDT/data `Outputs`. The sibling of `runWithConfigCore`'s emit body,
    /// but over a catalog the flow runner already resolved (live/docker/
    /// migrate) rather than re-read from `cfg.Model`. Applies `applyRenames`
    /// (table renames), `buildPolicyFromConfig` (tightening / insertion /
    /// emission toggles), and the `EmissionFolders`/`TransformGroups`
    /// bindings. The module filter is NOT applied here — it lives at the
    /// shared `applyModuleFilter` seam the caller already routed the catalog
    /// through (`Program.needCatalog`), so bundle and live agree.
    ///
    /// A `Config.defaultConfig` shaping (no renames, default policy, empty
    /// folders/groups) yields `projectWithState Policy.empty … catalog`,
    /// which is byte-identical to `project EmissionPolicy.empty catalog`
    /// (the prior `runFromCatalog` body) — the empty-default invariant.
    let projectWithConfig
        (shaping: Config.Config)
        (catalog: Catalog)
        : Result<Outputs> =
        // Empty-default invariant — a movement-only `projection.json` carries
        // `Config.defaultConfig` shaping. Route it through the exact prior
        // `runFromCatalog` body (`project EmissionPolicy.empty`) so the bundle
        // is byte-identical: `projectWithState` stamps a VersionedPolicy when
        // `buildPolicyFromConfig` ≠ `Policy.empty` (the default's
        // `DataComposition` differs), which would otherwise perturb the
        // manifest under the no-opinion config.
        if shaping = Config.defaultConfig then
            Result.success (project EmissionPolicy.empty catalog)
        else
        let pins = physicalRenamePins shaping catalog
        match applyRenames shaping catalog with
        | Error errors -> Result.failure errors
        | Ok renamedCatalog ->
            // §6 — the SHARED triple bind-all (drift-proof with
            // `applyShapingToCatalog`, which threads the identical set).
            match bindShapingTriple shaping renamedCatalog with
            | Ok (policy, folders, groups) ->
                let outputs, _ =
                    projectWithStateWithPins pins policy Profile.empty folders groups renamedCatalog
                Result.success outputs
            | Error errors -> Result.failure errors

    /// THE_CONFIG_CONTROL_PLANE §6 (S3) — the overlay-aware sibling of
    /// `runFromCatalog`: project a caller-supplied catalog through the
    /// shaping overlays and write the bundle to `outputDir`. The flow
    /// `EmitBundle` arm routes here so `projection <flow>` honors
    /// `policy`/`overrides`/`emission`. `Config.defaultConfig` shaping is
    /// byte-identical to `runFromCatalog catalog outputDir`.
    let runFromCatalogWith
        (shaping: Config.Config)
        (catalog: Catalog)
        (outputDir: string)
        : Result<string list> =
        projectWithConfig shaping catalog
        |> Result.bind (write outputDir)

    /// The manifest-only emit — `shape: manifest` (the A-cluster manifest
    /// exposure). Projects through the SAME shaped full pass chain the
    /// bundle rides (`projectWithConfig` — the manifest names every applied
    /// transform per kind, so the chain must run), then writes ONLY
    /// `manifest.json`. A direct single-file write, NOT the atomic
    /// directory-replace `write` performs — replacing `outputDir` here would
    /// destroy a previously published bundle beside the manifest.
    let runManifestOnlyFromCatalogWith
        (shaping: Config.Config)
        (catalog: Catalog)
        (outputDir: string)
        : Result<string list> =
        projectWithConfig shaping catalog
        |> Result.bind (fun outputs ->
            try
                Directory.CreateDirectory outputDir |> ignore
                let path = Path.Combine(outputDir, "manifest.json")
                File.WriteAllText(path, ManifestEmitter.toJson outputs.Manifest)
                Result.success [ path ]
            with ex ->
                Result.failureOf (
                    ValidationError.create
                        "pipeline.compose.manifestOnly.writeFailed"
                        (sprintf "manifest-only emit could not write '%s': %s" outputDir ex.Message)))

    /// The PROFILE-INVARIANT chain prefix composed over a renamed catalog —
    /// the post-chain catalog + topology WITHOUT the emit fold or the
    /// profile-consuming suffix (every post-topo step is a decision pass,
    /// catalog-preserving, so `(composePrefixState …).Catalog` equals the
    /// full chain's `finalState.Catalog` for ANY profile). This is the
    /// state-only chain runner (PL-1, dissolving S52): consumers that need
    /// only the composed state pay no per-kind artifact build — the NM-02
    /// registered⇔executed invariant is untouched because it governs
    /// artifact-PRODUCING runs, which still fold every emit step. Pure +
    /// synchronous (module-level; callers' `task { }` stay statically
    /// compilable — FS3511).
    let private composePrefixState
        (pins: Set<SsKey>)
        (policy: Policy)
        (groups: TransformGroups)
        (renamedCatalog: Catalog)
        : ComposeState =
        let prefixSteps, _suffix = RegisteredTransforms.chainStepsSplitWithPins pins
        let prefixAdapters = prefixSteps |> List.map (ChainStep.build policy Profile.empty)
        let filtered = filterChainByGroups groups prefixAdapters
        let composed = PassChainAdapter.compose filtered (ComposeState.initial renamedCatalog)
        LineageDiagnostics.payload composed

    /// THE_CONFIG_CONTROL_PLANE §6 (S3) — the catalog-shaping overlay for the
    /// non-bundle destinations (live preview / migrate / migrate-with-data),
    /// which evolve a SINK's schema toward a target Catalog rather than emit a
    /// file bundle. Applies `applyRenames` then runs the policy-aware chain
    /// PREFIX (`composePrefixState` — the suffix is catalog-preserving and the
    /// artifact fold was built-then-discarded here, S52) and returns the
    /// post-chain `ComposeState.Catalog` — the same shaped schema-B
    /// `runWithConfigCore` publishes. The module filter is applied upstream at
    /// the shared `Program.needCatalog` seam. `Config.defaultConfig` shaping
    /// yields the input catalog (the chain under `Policy.empty` is the
    /// skeleton — no operator tightening), so the default migrate/preview is
    /// byte-identical.
    let applyShapingToCatalog
        (shaping: Config.Config)
        (catalog: Catalog)
        : Result<Catalog> =
        // Empty-default invariant — the no-opinion shaping is the identity on
        // the catalog (the migrate/preview target is the resolved catalog
        // unchanged), so the default migrate/preview is byte-identical.
        if shaping = Config.defaultConfig then
            Result.success catalog
        else
        let pins = physicalRenamePins shaping catalog
        match applyRenames shaping catalog with
        | Error errors -> Result.failure errors
        | Ok renamedCatalog ->
            // §6 — the SHARED triple bind-all (drift-proof with
            // `projectWithConfig`, which threads the identical set).
            match bindShapingTriple shaping renamedCatalog with
            | Ok (policy, _folders, groups) ->
                Result.success (composePrefixState pins policy groups renamedCatalog).Catalog
            | Error errors -> Result.failure errors

    // -- P2 production wiring: the PIPELINED publish arm ---------------------
    //
    // The two-phase publish schedule drains the whole Bootstrap estate in the
    // extract stage, derives evidence from the retained rows in the profile
    // stage, and renders every kind's MERGE at compose time in the emit stage
    // — three sequential passes over the same landed rows. The pipelined arm
    // reorders the SAME work: the profile-invariant chain prefix (catalog
    // rewrites + `TopologicalOrderPass`) composes BEFORE the drain, so each
    // kind's MERGE renders and its evidence derives ON THE DRAIN WORKER the
    // moment its rows land (`Ingestion.collectInOrderForConcurrentWith`),
    // overlapping the remaining kinds' wire time; the rows drop per kind, so
    // live row memory is capped at `dataReadConcurrency` kinds. The emitted
    // bundle is IDENTICAL (equivalence pinned by the docker test): the same
    // `DataLoadPlan.loadFor` core, the same `StaticSeedsEmitter.renderLoad`,
    // the same evidence derivation — only the schedule differs.

    /// What the pipelined extract stage hands the profile + emit stages.
    type private PipelinedExtracted = {
        /// The model as READ (pre-hydration) — the store leg's schema plane
        /// derives from it (PL-1).
        ReadCatalog : Catalog
        Hydrated    : Catalog
        Migration   : Projection.Targets.Data.MigrationDependencyContext
        Prerendered : Map<SsKey, Projection.Targets.Data.DataInsertScript>
        Covered     : Set<SsKey>
        Derived     : CachedKind list
        Nullability : Map<string, Map<string, bool>>
    }

    /// The pipelined-arm gate. Every condition is a fact the arm's schedule
    /// depends on: the operator opted in (`emission.pipelinedBootstrap`,
    /// default true), a data lane is on (there is a Bootstrap drain to
    /// overlap), the model is OSSYS-sourced (there are live rows), the
    /// profiler is live (there is evidence to derive at drain time), and the
    /// profiler connection is present (the nullability reflection must run
    /// BEFORE the drain). Any miss falls back to the two-phase schedule,
    /// which then surfaces its own named failures at the established stages.
    let private pipelinedPublishGate (cfg: Config.Config) (connectionString: string) : bool =
        cfg.Emission.PipelinedBootstrap
        && Hydration.emitDataOf cfg
        && cfg.Model.Ossys.IsSome
        && (match cfg.Profiler.Provider with
            | Config.ProfilerProvider.Live -> true
            | _ -> false)
        && connectionString <> ""

    /// Phase A of the pipelined arm: bind the run's shaping (the SAME
    /// superset `runWithConfigCore` binds, so a binding failure surfaces the
    /// same accumulated errors) and compose the PROFILE-INVARIANT chain
    /// prefix over the hydrated catalog — the render-plane catalog +
    /// topology the drain-time Bootstrap render targets. The emit stage
    /// re-runs the full chain inside `runWithConfigCore` with the real
    /// profile; the prefix's outputs are identical there BY profile-
    /// invariance (property-pinned), so this pre-run buys the drain its
    /// render targets without forking the chain. Pure + synchronous
    /// (module-level; the caller's `task { }` stays statically compilable —
    /// FS3511).
    let private pipelinedPrefixState
        (cfg: Config.Config)
        (hydrated: Catalog)
        : Result<Policy * ComposeState> =
        let pins = physicalRenamePins cfg hydrated
        match applyRenames cfg hydrated with
        | Error errors -> Result.failure errors
        | Ok renamedCatalog ->
            let boundR =
                validation {
                    let! policy    = buildPolicyFromConfig cfg renamedCatalog
                    and! overrides = SpecialCircumstancesBinding.fromConfig renamedCatalog cfg
                    and! folders   = EmissionFoldersBinding.fromConfig renamedCatalog cfg
                    and! groups    = TransformGroupsBinding.fromConfig cfg
                    return (policy, overrides, folders, groups)
                }
            match boundR with
            | Error errors -> Result.failure errors
            | Ok (policy, _overrides, _folders, groups) ->
                Result.success (policy, composePrefixState pins policy groups renamedCatalog)

    /// The pure pre-drain computation of the pipelined arm, hoisted out of
    /// the task CE (FS3511): the eligibility + evidence partitions and the
    /// RENDER-plane inputs the drain-time projection needs. Returns
    /// `(eligible, evidenceKinds, drain)` where `drain nullability` starts
    /// the projected collect.
    let private pipelinedDrainOf
        (cfg: Config.Config)
        (connSpec: string)
        (migrationKinds: Set<SsKey>)
        (hydrated: Catalog)
        (sourceTopo: TopologicalOrder)
        (policy: Policy)
        (stateA: ComposeState)
        : Set<SsKey> * (Map<string, Map<string, bool>> -> Task<Result<Map<SsKey, Projection.Targets.Data.DataInsertScript * CachedKind option * StaticRow list option>>>) =
        let profilerSampling = Projection.Adapters.Sql.SqlProfilerOptions.defaults.Sampling
        let eligible = Hydration.bootstrapEligible migrationKinds cfg hydrated
        let staticKeys = Hydration.staticKindKeys hydrated
        // PL-2 (S04) — under AllData the static kinds ARE bootstrap-eligible
        // and the pre-drain static graft is skipped (see `pipelinedExtract`);
        // retain those kinds' rows at the drain worker so the graft rides
        // this one drain. Other compositions retain nothing.
        let retainRows =
            match Config.dataCompositionOf cfg with
            | AllData -> Set.intersect staticKeys eligible
            | AllRemaining | AllExceptStatic -> Set.empty
        // The evidence partition mirrors `captureEvidenceCacheDerived`:
        // static kinds never derive; sampled kinds keep the live capped
        // discovery (a full-row derivation would not be the sampled
        // shape the operator asked for).
        let evidenceKinds =
            eligible
            |> Set.filter (fun k ->
                not (Set.contains k staticKeys)
                && not (SamplingPolicy.isSampled k profilerSampling))
        // The RENDER-plane targets: the post-prefix catalog's kinds
        // (physical forms final — every post-topo step is a decision
        // pass, catalog-preserving) and ITS topology's cycle members
        // (the deferred-FK theory the batch plan reads).
        let targetKinds = Catalog.kindIndex stateA.Catalog
        let topoPrime =
            match stateA.TopologicalOrder with
            | Some t -> t
            | None -> (Projection.Core.Passes.TopologicalOrderPass.runWith Projection.Core.TreatAsCycle stateA.Catalog).Value
        let cycleMembers = TopologicalOrder.cycleMembers topoPrime
        // The bootstrap lane's posture: the composer suppresses the
        // delete arm on this lane regardless of `opts.DeleteScope`
        // (the additive upsert lane) — mirror it at drain time.
        let opts =
            DataEmitOptions.withDeleteScope None
                (DataEmitOptions.ofEmissionPolicy policy.Emission)
        // CdcAwareness is never populated on the publish path (no
        // ProfileDerivation / LiveProfiler axis writes it), so the
        // drain-time value equals what the compose-time render reads
        // off the attached profile.
        let cdc = Profile.empty.CdcAwareness
        let drain (nullability: Map<string, Map<string, bool>>) =
            Hydration.collectBootstrapRenderedUsing
                (max 1 cfg.Emission.DataReadConcurrency)
                connSpec eligible hydrated targetKinds cycleMembers
                opts cdc nullability evidenceKinds retainRows sourceTopo
        evidenceKinds, drain

    /// Project the drain's per-kind triples into the planes the profile +
    /// emit stages consume — scripts, derived evidence, and the PL-2
    /// retained rows (the AllData static graft's row source; empty on the
    /// other compositions). Pure; hoisted out of the task CE (FS3511).
    let private splitCollected
        (collected: Map<SsKey, Projection.Targets.Data.DataInsertScript * CachedKind option * StaticRow list option>)
        : Map<SsKey, Projection.Targets.Data.DataInsertScript> * CachedKind list * Map<SsKey, StaticRow list> =
        let prerendered = collected |> Map.map (fun _ (script, _, _) -> script)
        let derived =
            collected
            |> Map.toList
            |> List.choose (fun (_, (_, evidence, _)) -> evidence)
        let retained =
            collected
            |> Map.toList
            |> List.choose (fun (key, (_, _, rows)) -> rows |> Option.map (fun r -> key, r))
            |> Map.ofList
        prerendered, derived, retained

    /// The pipelined extract tail: reflect nullability ONCE (the pre-drain
    /// global query the drain-time evidence derivations slice per kind),
    /// then drain the Bootstrap-eligible kinds with drain-time render +
    /// evidence. Hoisted to module level (FS3511).
    let private pipelinedCollect
        (cfg: Config.Config)
        (connSpec: string)
        (connectionString: string)
        (migrationKinds: Set<SsKey>)
        (hydrated: Catalog)
        (sourceTopo: TopologicalOrder)
        (policy: Policy)
        (stateA: ComposeState)
        : Task<Result<Map<SsKey, Projection.Targets.Data.DataInsertScript> * Set<SsKey> * CachedKind list * Map<string, Map<string, bool>> * Map<SsKey, StaticRow list>>> =
        task {
            let! nullabilityR =
                Projection.Adapters.Sql.LiveProfiler.reflectNullability
                    (openProfilerConnection connectionString)
            match nullabilityR with
            | Error es -> return Result.failure es
            | Ok nullability ->
                let evidenceKinds, drain =
                    pipelinedDrainOf cfg connSpec migrationKinds hydrated sourceTopo policy stateA
                let! collectedR = drain nullability
                match collectedR with
                | Error es -> return Result.failure es
                | Ok collected ->
                    let prerendered, derived, retained = splitCollected collected
                    return Result.success (prerendered, evidenceKinds, derived, nullability, retained)
        }

    /// The pipelined extract stage body — the extract-lite read (model +
    /// migration binding + static graft; NO whole-estate Bootstrap collect),
    /// phase A, then the drain-time-rendering collect. Module-level (FS3511).
    let private pipelinedExtract
        (cfg: Config.Config)
        (connectionString: string)
        : Task<Result<PipelinedExtracted>> =
        task {
            let! parsed = readConfigModel cfg
            match parsed with
            | Error es -> return Result.failure es
            | Ok catalog ->
                match MigrationDependenciesBinding.fromConfig catalog cfg with
                | Error es -> return Result.failure es
                | Ok migration ->
                    match cfg.Model.Ossys with
                    | None ->
                        // Unreachable behind `pipelinedPublishGate`; named
                        // rather than silently degraded.
                        return
                            Result.failureOf
                                (ValidationError.create
                                    "pipeline.pipelinedBootstrap.noOssysSource"
                                    "the pipelined publish arm requires model.ossys (gate invariant).")
                    | Some connSpec ->
                        let sourceTopo =
                            (Projection.Core.Passes.TopologicalOrderPass.runWith Projection.Core.TreatAsCycle catalog).Value
                        let migrationKinds = MigrationDependenciesBinding.kindKeysOf migration
                        let! hydratedR =
                            // PL-2 (S04) — under AllData the static graft rides
                            // the Bootstrap drain (rows retained at the worker,
                            // grafted below); pre-graft only the residual
                            // static kinds Bootstrap will not drain (normally
                            // the empty set). The prefix state + drain consume
                            // marks, attributes and FK edges — never
                            // populations — so the deferred graft is invisible
                            // to them.
                            if Config.dataCompositionOf cfg = AllData then
                                let residual =
                                    Set.difference
                                        (Hydration.staticKindKeys catalog)
                                        (Hydration.bootstrapEligible migrationKinds cfg catalog)
                                Hydration.hydrateStaticSubsetUsing residual sourceTopo cfg catalog
                            else
                                Hydration.hydrateCatalogUsing sourceTopo cfg catalog
                        match hydratedR with
                        | Error es -> return Result.failure es
                        | Ok hydrated ->
                            match pipelinedPrefixState cfg hydrated with
                            | Error es -> return Result.failure es
                            | Ok (policy, stateA) ->
                                let! collectedR =
                                    pipelinedCollect cfg connSpec connectionString migrationKinds hydrated sourceTopo policy stateA
                                match collectedR with
                                | Error es -> return Result.failure es
                                | Ok (prerendered, covered, derived, nullability, retained) ->
                                    return
                                        Result.success
                                            { ReadCatalog = catalog
                                              // PL-2 — graft the drain-retained
                                              // static rows (identity when
                                              // nothing was retained).
                                              Hydrated = Hydration.graftStaticPopulations retained hydrated
                                              Migration = migration
                                              Prerendered = prerendered
                                              Covered = covered
                                              Derived = derived
                                              Nullability = nullability }
        }

    /// The pipelined profile stage body: assemble the evidence cache from the
    /// drain-time derivations (counted live fallback for uncovered kinds) and
    /// compose the Profile axes — `attachDerived`'s equal, on the overlapped
    /// schedule. Module-level (FS3511).
    let private profileFromDrainDerived
        (cfg: Config.Config)
        (connectionString: string)
        (ex: PipelinedExtracted)
        : Task<Result<Profile>> =
        let options =
            { Projection.Adapters.Sql.SqlProfilerOptions.defaults with
                MaxConcurrency = max 1 cfg.Profiler.MaxConcurrency }
        Projection.Adapters.Sql.LiveProfiler.attachFromKinds
            options (openProfilerConnection connectionString)
            ex.Nullability ex.Covered ex.Derived ex.Hydrated Profile.empty

    /// PL-1 — everything a combined verb's second leg consumes from the
    /// publish, named so the second payment is visible: the estate is
    /// ACQUIRED once (model read, static graft, Bootstrap drain, migration
    /// binding) and COMPOSED once (the post-chain state); the load/store
    /// legs thread these values instead of re-reading the live source and
    /// re-running the chain they just rode.
    type EstateAcquisition = {
        /// The model as READ (pre-hydration, pre-rename) — the store leg's
        /// emitted-schema plane derives from this value (static populations
        /// are a data-lane graft, never part of the recorded schema plane).
        ReadCatalog   : Catalog
        /// The static-grafted catalog (the publish's chain input) — the load
        /// leg's episode plane derives from this value (parity with the
        /// published bundle's hydrated rows).
        Hydrated      : Catalog
        /// The Bootstrap lane as the publish carried it — drained rows on the
        /// two-phase schedule, drain-time-prerendered scripts on the
        /// pipelined arm (equal by the pinned pipelined-equivalence law).
        BootstrapLane : DataComposer.BootstrapLane
        Migration     : Projection.Targets.Data.MigrationDependencyContext
        /// The post-chain `ComposeState` of the publish — the load leg
        /// renders the seed plan against ITS catalog + topology, so the
        /// deployed seed cannot drift from the published bundle.
        FinalState    : ComposeState
    }

    /// The publish, returning the `EstateAcquisition` beside the report —
    /// the combined verbs (`runWithConfigAndLoad` / `runWithConfigAndStore`)
    /// ride this so one verb pays for ONE estate acquisition (PL-1).
    /// `runWithConfig` is the report-only projection.
    let runWithConfigAcquiring (cfg: Config.Config) : Task<Result<RunReport * EstateAcquisition>> =
        task {
            // Card S4a — the publish arc rides the spine. The `staged { }`
            // CE owns the "pipeline" umbrella root + the extract / profile /
            // emit brackets (started/stageCompleted envelopes, the §10 stage
            // table, `stage.<name>` Bench scopes); the bodies keep their
            // category-bearing `<stage>.completed` domain markers (§7.2-§7.5
            // payloads). Two shapes changed, named: the `<stage>.started`
            // markers now carry the bracket's Summary category (uniform with
            // the migrate/transfer legs), and a failed stage SKIPS the
            // downstream arc instead of opening phantom failed stages (the
            // pre-spine stream closed `emit` as failed on a profile failure
            // that never reached it).
            let sourceConnectionString =
                System.Environment.GetEnvironmentVariable Config.SourceConnectionStringEnvVar
                |> Option.ofObj
                |> Option.defaultValue ""
            let! verdict =
                if pipelinedPublishGate cfg sourceConnectionString then
                    // P2 production wiring — the acquisition-overlapped
                    // schedule: extract = read + static graft + chain prefix +
                    // drain-time render/evidence; profile = cache assembly +
                    // counted live fallback; emit = the unchanged core with
                    // the prerendered Bootstrap lane.
                    staged Spines.pipeline {
                        let! extracted =
                            Staged.stage Stages.extract (fun () ->
                                task {
                                    let! result = pipelinedExtract cfg sourceConnectionString
                                    match result with
                                    | Ok ex ->
                                        emitStageMarker LogSink.Extract "extract.completed" LogSink.End
                                            (Map.ofList [ "moduleCount", box (List.length ex.Hydrated.Modules) ])
                                        return Ok ex
                                    | Error errors ->
                                        return Error errors
                                })
                        let! profile =
                            Staged.stage Stages.profile (fun () ->
                                task {
                                    let! profileResult = profileFromDrainDerived cfg sourceConnectionString extracted
                                    emitStageMarker LogSink.Profile "profile.completed" LogSink.End Map.empty
                                    return profileResult
                                })
                        let! report =
                            Staged.stage Stages.emit (fun () ->
                                task {
                                    let lane = DataComposer.BootstrapLane.Prerendered extracted.Prerendered
                                    let result =
                                        runWithConfigCore cfg (Ok extracted.Hydrated)
                                            lane extracted.Migration profile
                                        |> Result.map (fun (report, finalState) ->
                                            report,
                                            { ReadCatalog = extracted.ReadCatalog
                                              Hydrated = extracted.Hydrated
                                              BootstrapLane = lane
                                              Migration = extracted.Migration
                                              FinalState = finalState })
                                    emitStageMarker LogSink.Emit "emit.completed" LogSink.End Map.empty
                                    return result
                                })
                        return report
                    }
                else
                    staged Spines.pipeline {
                        let! extracted =
                            Staged.stage Stages.extract (fun () ->
                                task {
                                    // §7.2 extract — OSSYS catalog read + WP6 step-4
                                    // data hydration (graft live static rows when
                                    // OSSYS-sourced + data on; identity otherwise) +
                                    // the Bootstrap lane's row source (Bootstrap-always).
                                    let! parsed = readAndHydrateConfigModel cfg
                                    match parsed with
                                    | Ok (readCatalog, catalog, bootRows, migration) ->
                                        emitStageMarker LogSink.Extract "extract.completed" LogSink.End
                                            (Map.ofList [ "moduleCount", box (List.length catalog.Modules) ])
                                        return Ok (readCatalog, catalog, bootRows, migration)
                                    | Error errors ->
                                        return Error errors
                                })
                        let readCatalog, catalog, bootstrapRows, migration = extracted
                        let! profile =
                            Staged.stage Stages.profile (fun () ->
                                task {
                                    // §7.3 profile — live SQL probing (Profile.empty
                                    // for the SnapshotJson path; a real probe for the
                                    // live provider).
                                    // Single-scan: the bootstrap rows hydrated in
                                    // the extract stage feed evidence derivation.
                                    let! profileResult = acquireProfile cfg bootstrapRows catalog
                                    emitStageMarker LogSink.Profile "profile.completed" LogSink.End Map.empty
                                    return profileResult
                                })
                        let! report =
                            Staged.stage Stages.emit (fun () ->
                                task {
                                    // §7.5 emit — pass chain + sibling-Π emission +
                                    // artifact write.
                                    let lane = DataComposer.BootstrapLane.Rows bootstrapRows
                                    let result =
                                        runWithConfigCore cfg (Ok catalog)
                                            lane migration profile
                                        |> Result.map (fun (report, finalState) ->
                                            report,
                                            { ReadCatalog = readCatalog
                                              Hydrated = catalog
                                              BootstrapLane = lane
                                              Migration = migration
                                              FinalState = finalState })
                                    emitStageMarker LogSink.Emit "emit.completed" LogSink.End Map.empty
                                    return result
                                })
                        return report
                    }
            // The spine closed the books (the open stage + the root closed
            // `aborted` on the wire); `StagedVerdict.toResult` preserves the
            // composition's crash semantics for the caller's catch.
            return StagedVerdict.toResult verdict
        }

    /// The report-only publish (`runWithConfigAcquiring` with the
    /// acquisition dropped) — the established entry for callers without a
    /// second leg.
    let runWithConfig (cfg: Config.Config) : Task<Result<RunReport>> =
        task {
            let! acquired = runWithConfigAcquiring cfg
            return acquired |> Result.map fst
        }

    // -- Track W1-B (seam T2): the diff-vs-prior store leg -------------------

    /// The emitted schema plane of a full-export run — the config's model
    /// after the config-driven rewrites (`applyRenames`). This is the
    /// **same** catalog `runWithConfigCore` projects to the CREATE files, so the
    /// displacement the store leg measures is the displacement the bundle
    /// publishes (B in `B ⊖ A`). Pure; no Profile dependence (the schema plane
    /// is profile-invariant — profiling only annotates the manifest).
    /// PL-1: the combined verb derives this plane from the publish's OWN
    /// `EstateAcquisition.ReadCatalog` (`applyRenames` is pure) — this
    /// standalone reads fresh, and is the identity-gate witness the threaded
    /// form is pinned against.
    let emittedSchema (cfg: Config.Config) : Task<Result<Catalog>> =
        task {
            let! parsed = readConfigModel cfg
            return parsed |> Result.bind (applyRenames cfg)
        }

    /// AC-X1 (part B, leveled per card P2) — the emitted catalog + the LEVELED
    /// seed plan for the live-load leg, projected from the publish's OWN
    /// `EstateAcquisition` (PL-1): the hydrated catalog, Bootstrap lane,
    /// migration context and post-chain `ComposeState` are consumed as
    /// threaded values — no second model read, no second row hydration, no
    /// second chain run. The seed renders against `FinalState.Catalog` (the
    /// catalog the publish EMITTED — post-chain, pins honored) under ITS
    /// stored topology, so the deployed plan cannot drift from the published
    /// bundle (the parity duty, now BY CONSTRUCTION). The plan remains a
    /// faithful PARTITION of the published data lanes — same
    /// `dispatchSiblings + unionSiblings` artifact, same per-kind
    /// `RenderedPhase1`/`RenderedPhase2` strings (the partition law is
    /// property-witnessed in `DataEmissionComposerTests`). Empty plan when
    /// data emission is off or the catalog projects no seed statements.
    let projectSeedPlanUsing
        (cfg: Config.Config)
        (acquired: EstateAcquisition)
        : Result<Catalog * DataComposer.LeveledDeploymentText> =
        match applyRenames cfg acquired.Hydrated with
        | Error es -> Result.failure es
        | Ok emitted ->
            // §6 — the SHARED triple bind-all. The load leg threads the SAME
            // policy/folders/groups set the publish path binds, so the deployed
            // leveled seed never drifts from the bundle (the parity duty).
            match bindShapingTriple cfg emitted with
            | Error errs -> Result.failure errs
            | Ok (policy, _folders, _groups) ->
                if not policy.Emission.EmitData then
                    Result.success (emitted, DataComposer.LeveledDeploymentText.empty)
                else
                    let renderCatalog = acquired.FinalState.Catalog
                    // The chain's TopologicalOrderPass already ran Kahn/Tarjan
                    // over this exact catalog — thread its order; a filtered
                    // chain without the pass recomputes (mirrors the bundle
                    // decoration's fallback).
                    let topo =
                        match acquired.FinalState.TopologicalOrder with
                        | Some t -> t
                        | None -> (Projection.Core.Passes.TopologicalOrderPass.runWith Projection.Core.TreatAsCycle renderCatalog).Value
                    match
                        DataComposer.composeRenderedLeveledWithBootstrapLaneUsing
                            topo policy renderCatalog Profile.empty
                            acquired.Migration
                            acquired.BootstrapLane
                            UserRemapContext.empty
                    with
                    | Ok plan -> Result.success (emitted, plan)
                    | Error err ->
                        // Mirrors the bundle decoration's invariant: a valid
                        // catalog never fails the composer (the keyset is
                        // `Catalog.allKinds`).
                        invalidOp (sprintf "Compose.projectSeedPlanUsing: DataEmissionComposer.composeRenderedLeveled: %A" err)

    /// Standalone compute-then-delegate entry (the `hydrateCatalogUsing`
    /// pattern): ONE model read + hydration from cfg, the profile-invariant
    /// prefix state (no artifact fold — S52's carrier), then
    /// `projectSeedPlanUsing`. The combined verb threads the publish's
    /// acquisition instead (`runWithConfigAndLoad`); this entry serves
    /// standalone seed-plan consumers and is the identity-gate witness the
    /// threaded form is pinned against.
    let emittedSeedPlan (cfg: Config.Config) : Task<Result<Catalog * DataComposer.LeveledDeploymentText>> =
        task {
            // WP6 step 4 parity — hydrate the standalone seed plan the SAME way
            // the publish path does (`readAndHydrateConfigModel`), so the
            // deployed seed plan reflects the same hydrated rows the bundle
            // published.
            let! parsed = readAndHydrateConfigModel cfg
            return
                match parsed with
                | Error es -> Result.failure es
                | Ok (readCatalog, hydrated, bootRows, migration) ->
                    match applyRenames cfg hydrated with
                    | Error es -> Result.failure es
                    | Ok renamed ->
                        match bindShapingTriple cfg renamed with
                        | Error errs -> Result.failure errs
                        | Ok (policy, _folders, groups) ->
                            // Pins resolve against the ORIGINAL (pre-rename)
                            // catalog — same as `runWithConfigCore`.
                            let pins = physicalRenamePins cfg hydrated
                            projectSeedPlanUsing cfg
                                { ReadCatalog = readCatalog
                                  Hydrated = hydrated
                                  BootstrapLane = DataComposer.BootstrapLane.Rows bootRows
                                  Migration = migration
                                  FinalState = composePrefixState pins policy groups renamed }
        }

    /// PL-1 (S51) — the ONE durable read per store leg. `None` is genesis (no
    /// file yet); a malformed store is fail-closed (`StoreReadFailed`), never
    /// silently treated as genesis. Every store-leg consumer below takes the
    /// loaded chain, so one `runStoreLeg` pays `File.ReadAllText` +
    /// `JsonDocument.Parse` + the per-episode `CatalogCodec.deserialize` +
    /// the per-edge monotone verification exactly once (previously four
    /// independent loads of the same file in one leg).
    let private loadStoreChain (path: string) : Result<EpisodicLifecycle option, FullExportStoreError> =
        if System.IO.File.Exists path then
            match LifecycleStore.load path with
            | Error e -> Error (StoreReadFailed (string e))
            | Ok chain -> Ok (Some chain)
        else Ok None

    /// State A — the prior emission's schema. A **missing store is genesis**:
    /// A = ∅ (every kind `Add`, no `Remove`) — byte-faithful to today's
    /// first-emission behavior. On a loaded chain the recovery reads the
    /// stored latest snapshot directly (each episode carries FULL state —
    /// `Episode.fs`'s own contract; the FTC fold over the edge diffs is the
    /// VERIFICATION property, pinned by the `6.H.2 reconstructLatestSchema`
    /// law test, not the recovery path — S53).
    let private priorSchemaOfChain (chain: EpisodicLifecycle option) : Result<Catalog, FullExportStoreError> =
        match chain with
        | Some c -> Ok (Episode.schema (EpisodicLifecycle.latest c))
        | None ->
            match Catalog.create [] [] with
            | Ok empty   -> Ok empty
            | Error errs -> Error (StoreReadFailed (errs |> List.map (fun e -> e.Message) |> String.concat "; "))

    /// The prior chain's **accumulated** `.refactorlog` — every rename the
    /// timeline has ever performed, folded over its per-edge schema diffs via
    /// `RefactorLogEmitter.emit` + `accumulate` (AC-P6, the cumulative document).
    /// Genesis (no chain / genesis-only chain) ⇒ empty. This is the prior the new
    /// displacement's refactorlog accumulates against, so a rename already
    /// committed is not re-emitted (deduped by `OperationKey`). The per-edge
    /// diff chain is computed here ONCE per store leg (S53 — the second
    /// computation lived in the retired FTC-fold recovery above).
    let private priorAccumulatedRefactorLogOfChain
        (chain: EpisodicLifecycle option)
        : Result<RefactorLogEntry list, FullExportStoreError> =
        match chain with
        | None -> Ok []
        | Some c ->
            match EpisodicLifecycle.schemaEvolutionChain c with
            | Error e -> Error (DisplacementFailed e)
            | Ok edgeDiffs ->
                // Fold each edge's refactorlog into the cumulative log, in
                // timeline order, deduping by OperationKey (prior wins).
                let rec loop acc remaining =
                    match remaining with
                    | [] -> Ok (acc : RefactorLogEntry list)
                    | edge :: rest ->
                        match RefactorLogEmitter.emit edge with
                        | Error e -> Error (DisplacementFailed e)
                        | Ok artifact ->
                            loop (RefactorLogEmitter.accumulateArtifact acc artifact) rest
                loop [] edgeDiffs

    /// The next monotone `EpisodeCoordinate` for the loaded timeline — ordinal
    /// 0 for a genesis (no chain), else the latest episode's ordinal + 1.
    /// Mirrors the migrate path's coordinate derivation; the boundary supplies
    /// `environment` + `at` (Core holds no clock).
    let private nextStoreCoordinateOfChain
        (chain: EpisodicLifecycle option)
        (environment: Environment)
        (at: System.DateTimeOffset)
        : Result<EpisodeCoordinate, FullExportStoreError> =
        let ordinal =
            match chain with
            | Some c -> Version.ordinal (Episode.version (EpisodicLifecycle.latest c)) + 1
            | None -> 0
        match Version.create ordinal (sprintf "v%d" ordinal) with
        | Ok version -> Ok (EpisodeCoordinate.create version environment at)
        | Error errs -> Error (RecordFailed (errs |> List.map (fun e -> e.Message) |> String.concat "; "))

    /// Record exactly ONE new episode (the run's emitted schema as the new
    /// schema plane) onto the loaded timeline, saving to `path`. Genesis-opens
    /// on the first run; appends thereafter. Fail-closed on a non-monotone
    /// append. Reuses `LifecycleStore.save` + the `EpisodicLifecycle`
    /// monotone-history invariant (already re-verified per edge by the leg's
    /// one load).
    let private recordEpisodeOnChain
        (path: string)
        (timeline: Timeline)
        (episode: Episode)
        (chain: EpisodicLifecycle option)
        : Result<EpisodicLifecycle, FullExportStoreError> =
        let chainR : Result<EpisodicLifecycle, FullExportStoreError> =
            match chain with
            | Some existing ->
                match EpisodicLifecycle.append episode existing with
                | Ok appended -> Ok appended
                | Error errs -> Error (RecordFailed (errs |> List.map (fun e -> e.Message) |> String.concat "; "))
            | None -> Ok (EpisodicLifecycle.genesis timeline episode)
        match chainR with
        | Error e -> Error e
        | Ok appended ->
            match LifecycleStore.save path appended with
            | Ok ()   -> Ok appended
            | Error e -> Error (StoreReadFailed (string e))

    /// The diff-vs-prior store leg for one full-export run (seam T2). Given the
    /// run's emitted schema (state B) and the timeline at `path`: load the prior
    /// emission's schema (state A), measure `B ⊖ A`, accumulate the
    /// displacement's refactorlog against the prior committed log, build the
    /// `ChangeManifest` for the edge, and record exactly one new episode. Pure
    /// w.r.t. the genesis emission (it runs *after* the bundle lands and never
    /// alters it); the only side effect is the durable store write.
    ///
    /// `appliedTransforms` is the composed run's §5.5 per-artifact overlay
    /// enumeration (`ManifestEmitter.appliedTransforms` over the run's lineage
    /// trail, surfaced on `RunReport.Manifest.AppliedTransforms`); `tolerances`
    /// is the run's resolved tolerance residual. Both are stamped onto the new
    /// episode via `Episode.withProvenance` so the recorded episode — and the
    /// `ChangeManifest` of its edge — carries "under what overlay / equivalence
    /// was this displacement accepted?", not the dead `[]` / `strict` defaults.
    /// (FLAG: no production caller resolves a non-`strict` tolerance set today —
    /// the config carries no per-environment divergence axis and `Tolerance.parse`
    /// is unwired; see `storeLegFromConfig`. The wiring is live so a future
    /// tolerance-resolving caller populates it without re-touching this seam.)
    let private runStoreLeg
        (path: string)
        (timeline: Timeline)
        (environment: Environment)
        (at: System.DateTimeOffset)
        (refactorLogRef: string option)
        (data: DataObservation)
        (tolerances: Tolerance)
        (appliedTransforms: (SsKey * OverlayAxis option) list)
        (emitted: Catalog)
        : Result<FullExportStoreLeg, FullExportStoreError> =
        // PL-1 (S51) — ONE durable load; the four consumers below thread it.
        match loadStoreChain path with
        | Error e -> Error e
        | Ok loaded ->
        match priorSchemaOfChain loaded with
        | Error e -> Error e
        | Ok prior ->
            let displacement = CatalogDiff.between prior emitted
            match priorAccumulatedRefactorLogOfChain loaded with
            | Error e -> Error e
            | Ok priorLog ->
                    match RefactorLogEmitter.emit displacement with
                    | Error e -> Error (DisplacementFailed e)
                    | Ok currentArtifact ->
                        let accumulated =
                            RefactorLogEmitter.accumulateArtifact priorLog currentArtifact
                        match nextStoreCoordinateOfChain loaded environment at with
                        | Error e -> Error e
                        | Ok coordinate ->
                            // The emitted schema is the new episode's schema plane
                            // (the same `Catalog` the bundle published). Thread the
                            // run's provenance onto it (NM-33): the resolved
                            // tolerance residual + the composed run's per-artifact
                            // overlay enumeration. `withProvenance` is the single
                            // production caller of the provenance plane.
                            let episode =
                                Episode.create coordinate emitted Profile.empty refactorLogRef data
                                |> Episode.withProvenance tolerances appliedTransforms
                            match recordEpisodeOnChain path timeline episode loaded with
                            | Error e -> Error e
                            | Ok chain ->
                                // The ChangeManifest of the edge prior → emitted.
                                // We build the `From` episode from the prior schema
                                // at a genesis coordinate; `ChangeManifest.between`
                                // reads only the schemas + the To-episode's planes.
                                let priorCoordinate =
                                    match Version.create 0 "v0" with
                                    | Ok v -> EpisodeCoordinate.create v environment at
                                    | Error _ -> coordinate
                                let fromEpisode = Episode.create priorCoordinate prior Profile.empty None DataObservation.empty
                                match ChangeManifest.between fromEpisode episode with
                                | Error e -> Error (DisplacementFailed e)
                                | Ok manifest ->
                                    Ok { Displacement = displacement
                                         Manifest = manifest
                                         AccumulatedRefactorLog = accumulated
                                         Chain = chain }

    let private mapStoreErr (storeErr: FullExportStoreError) : ValidationError =
        match storeErr with
        | StoreReadFailed m -> ValidationError.create "pipeline.fullExport.store.readFailed" m
        | DisplacementFailed e -> ValidationError.create "pipeline.fullExport.store.displacementFailed" (string e)
        | RecordFailed m -> ValidationError.create "pipeline.fullExport.store.recordFailed" m

    /// The diff-vs-prior store leg over the publish's own acquisition, or
    /// `Ok None` when no store is supplied. PL-1: the emitted-schema plane
    /// derives from `acquired.ReadCatalog` by the same pure `applyRenames`
    /// the retired second read fed — no second `MetadataSnapshotRunner` run
    /// against the live source. Pure/synchronous (no await — the acquisition
    /// is already in hand).
    /// `appliedTransforms` is the run's §5.5 overlay enumeration, taken from
    /// `RunReport.Manifest.AppliedTransforms` (the composed run the caller
    /// already produced), threaded onto the recorded episode (NM-33). The
    /// tolerance residual is `Tolerance.strict` here — the only resolved value
    /// available: the unified config carries no per-environment divergence axis
    /// and `Tolerance.parse` has no production caller, so a non-strict residual
    /// is not yet reachable at this site (FLAGGED on `runStoreLeg`).
    let private storeLegFromAcquisition
        (cfg: Config.Config)
        (acquired: EstateAcquisition)
        (storePath: string option)
        (timeline: Timeline)
        (environment: Environment)
        (at: System.DateTimeOffset)
        (appliedTransforms: (SsKey * OverlayAxis option) list)
        : Result<FullExportStoreLeg option> =
        match storePath with
        | None -> Result.success None
        | Some p when System.String.IsNullOrWhiteSpace p -> Result.success None
        | Some path ->
            match applyRenames cfg acquired.ReadCatalog with
            | Error errors -> Result.failure errors
            | Ok emitted ->
                match runStoreLeg path timeline environment at None DataObservation.empty (emittedToleranceResidual (defaultArg cfg.Emission.Tolerance Tolerance.permissive) emitted) appliedTransforms emitted with
                | Ok leg -> Result.success (Some leg)
                | Error storeErr -> Result.failureOf (mapStoreErr storeErr)

    /// Track W1-B (seam T2) — `runWithConfig` with the **optional diff-vs-prior
    /// store leg**. Additive over the genesis path: when `storePath` is `None`
    /// or empty, this is byte-identical to `runWithConfig` (the genesis emission
    /// alone, `snd = None`); when a store is supplied, the CREATE files are
    /// emitted first (unchanged), then the store leg loads the prior emission,
    /// measures the displacement, accumulates the refactorlog, builds the
    /// `ChangeManifest`, and records exactly one new episode.
    ///
    /// **Scope (W1-B leg 1):** diff-vs-prior + `ChangeManifest` +
    /// refactorlog-accumulate + `record`. NO data-merge / data-load leg (a
    /// larger feature, out of scope for 6b); `DataObservation.empty` is recorded
    /// (the CDC-measure leg is a sibling track the parent joins).
    ///
    /// The store write is fail-loud: a malformed store, an unobservable
    /// displacement, or a non-monotone record surface as `Error` on the run,
    /// *after* the bundle has landed (the operator knows the emission succeeded
    /// but provenance did not).
    /// Bracket a post-root publish leg (`store` / `seed-load`) in the stage
    /// wire events (2026-07-02 — the legs join the declared publish spine so
    /// the live board covers the whole run). Same wire shape as
    /// `Staged.stage`: `<key>.started` → `summary.stageCompleted` with
    /// `succeeded`/`failed` off the Result and `aborted` on a throw — the
    /// board's line always closes, never a hang. The legs run AFTER the
    /// pipeline CE's root scope closed, so they bracket via the primitives
    /// rather than inside the CE (the umbrella brackets the emission arc).
    let private stagedLeg (key: string) (body: unit -> Task<Result<'a>>) : Task<Result<'a>> =
        task {
            LogSink.recordStageStart key
            let sw = System.Diagnostics.Stopwatch.StartNew()
            try
                let! result = body ()
                sw.Stop()
                match result with
                | Ok _    -> LogSink.recordStageEvent key sw.ElapsedMilliseconds LogSink.Succeeded
                | Error _ -> LogSink.recordStageEvent key sw.ElapsedMilliseconds LogSink.Failed
                return result
            with ex ->
                sw.Stop()
                LogSink.recordStageEvent key sw.ElapsedMilliseconds LogSink.Aborted
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw()
                return Unchecked.defaultof<_>
        }

    let runWithConfigAndStore
        (cfg: Config.Config)
        (storePath: string option)
        (timeline: Timeline)
        (environment: Environment)
        (at: System.DateTimeOffset)
        : Task<Result<RunReport * FullExportStoreLeg option>> =
        task {
            // PL-1 — one estate acquisition: the store leg consumes the
            // publish's own read catalog instead of re-extracting the model.
            let! acquiredR = runWithConfigAcquiring cfg
            match acquiredR with
            | Error errors -> return Result.failure errors
            | Ok (report, acquired) ->
                match storePath with
                | Some p when not (System.String.IsNullOrWhiteSpace p) ->
                    // The store leg is a declared stage on the publish spine
                    // (`Spines.publishWith true …`) — bracketed so the board's
                    // store line opens, works, and closes honestly.
                    let! legR =
                        stagedLeg (StageName.value Stages.store) (fun () ->
                            Task.FromResult
                                (storeLegFromAcquisition cfg acquired storePath timeline environment at report.Manifest.AppliedTransforms))
                    return legR |> Result.map (fun legOpt -> report, legOpt)
                | _ ->
                    // No store — the genesis emission; no store stage was
                    // seeded (dispatch chose the bare pipeline spine), so no
                    // bracket fires.
                    return
                        storeLegFromAcquisition cfg acquired storePath timeline environment at report.Manifest.AppliedTransforms
                        |> Result.map (fun legOpt -> report, legOpt)
        }

    /// AC-X1 (part B) — the **live data-load leg core**: load the idempotent
    /// CDC-aware `seed` into the already-deployed `sink`, MEASURE the data
    /// movement (the change-measure ‖·‖ via `Deploy.cdcCaptureTotal`, the
    /// production reader), and — when a store is supplied — record an episode
    /// (schema = `emitted`) with the **measured** `DataObservation` (not
    /// `DataObservation.empty`). The seed is a MERGE, so the measure on a first
    /// load = rows inserted; on an idempotent re-run = 0 (CDC-silent). Catalog +
    /// seed are caller-supplied so this is unit-testable without a config file;
    /// `runWithConfigAndLoad` is the config-driven wrapper.
    /// Synchronous record tail of `loadSeedAndRecord` (keeps the task surface a
    /// flat `let!`/`do!` sequence + `return` — FS3511-safe).
    let private recordLoad
        (emitted: Catalog)
        (cdcDelta: int)
        (storePath: string option)
        (timeline: Timeline)
        (environment: Environment)
        (at: System.DateTimeOffset)
        : Result<FullExportStoreLeg option * int> =
        match storePath with
        | Some path when not (System.String.IsNullOrWhiteSpace path) ->
            let data = DataObservation.create cdcDelta None
            // The data-load leg records the measured CDC `DataObservation` but
            // carries no composed-run lineage trail at this site (the trail is a
            // schema-emission artifact, not a load artifact), so the §5.5 overlay
            // enumeration is empty here. The tolerance residual IS now resolved
            // (NM-32/33 final hop): the accepted-divergences the emitted catalog
            // structurally invokes, computed from its static data. The diff-vs-prior
            // leg (`storeLegFromConfig`) is where the real overlay enumeration threads.
            // This data-load leg is config-less (unit-testable), so it resolves against
            // `Tolerance.permissive` (report every fired divergence) — the schema-publish
            // store leg is where the operator's `emission.tolerance` threads (Wave-3 3.4).
            match runStoreLeg path timeline environment at None data (emittedToleranceResidual Tolerance.permissive emitted) [] emitted with
            | Ok leg -> Result.success (Some leg, cdcDelta)
            | Error storeErr -> Result.failureOf (mapStoreErr storeErr)
        | _ -> Result.success (None, cdcDelta)

    /// The CDC-bracketed load-measure-record core shared by the two load
    /// shapes (fused-string and leveled). `load` is a thunk so the work
    /// starts AFTER the baseline capture; the caller selects it
    /// synchronously (an unconditional `do!` of a pre-selected Task is
    /// the FS3511-safe shape).
    let private loadMeasureAndRecord
        (cdcCaptureTotal: SqlConnection -> Task<Result<int>>)
        (load: unit -> Task<unit>)
        (emitted: Catalog)
        (sink: SqlConnection)
        (storePath: string option)
        (timeline: Timeline)
        (environment: Environment)
        (at: System.DateTimeOffset)
        : Task<Result<FullExportStoreLeg option * int>> =
        task {
            // NM-54 — the CDC measure surfaces its probe Error rather than
            // fabricating a count; an unreadable axis fails the load-measure leg.
            match! cdcCaptureTotal sink with
            | Error es -> return Result.failure es
            | Ok baseline ->
                let loading = load ()
                do! loading
                match! cdcCaptureTotal sink with
                | Error es -> return Result.failure es
                | Ok post ->
                    return recordLoad emitted (post - baseline) storePath timeline environment at
        }

    /// The fused-string load shape — what an operator executing the
    /// published `Data/seed.sql` by hand gets; the witness surface for
    /// the bundle's idempotent-MERGE contract (AC-X1 part B tests).
    /// The CLI load leg rides `loadLeveledSeedAndRecord` since card P2.
    let loadSeedAndRecord
        (cdcCaptureTotal: SqlConnection -> Task<Result<int>>)
        (executeBatch: SqlConnection -> string -> Task<unit>)
        (emitted: Catalog)
        (seed: string)
        (sink: SqlConnection)
        (storePath: string option)
        (timeline: Timeline)
        (environment: Environment)
        (at: System.DateTimeOffset)
        : Task<Result<FullExportStoreLeg option * int>> =
        // `cdcCaptureTotal` / `executeBatch` are injected (the `Deploy` module
        // compiles AFTER this one, so Compose cannot name it; callers pass
        // `Deploy.cdcCaptureTotal` / `Deploy.executeBatch`).
        loadMeasureAndRecord
            cdcCaptureTotal
            (fun () ->
                // An empty seed loads nothing.
                if System.String.IsNullOrWhiteSpace seed then Task.FromResult ()
                else executeBatch sink seed)
            emitted sink storePath timeline environment at

    /// Card P2 — the LEVELED production load: same CDC bracket, same
    /// episode recording, but the seed deploys as the leveled plan
    /// through the injected executor (callers pass
    /// `Deploy.executeLeveledSeed <connection string>` — the executor
    /// closes over the connection STRING because every segment opens its
    /// own pooled connection; `sink` stays the CDC measure's connection).
    let loadLeveledSeedAndRecord
        (cdcCaptureTotal: SqlConnection -> Task<Result<int>>)
        (executeLeveled: DataComposer.LeveledDeploymentText -> Task<unit>)
        (emitted: Catalog)
        (plan: DataComposer.LeveledDeploymentText)
        (sink: SqlConnection)
        (storePath: string option)
        (timeline: Timeline)
        (environment: Environment)
        (at: System.DateTimeOffset)
        : Task<Result<FullExportStoreLeg option * int>> =
        loadMeasureAndRecord
            cdcCaptureTotal
            (fun () ->
                // An empty plan loads nothing (data emission off, or no
                // seed statements in scope) — parity with the fused form's
                // whitespace gate.
                if DataComposer.LeveledDeploymentText.isEmpty plan then Task.FromResult ()
                else executeLeveled plan)
            emitted sink storePath timeline environment at

    /// AC-X1 (part B) — `runWithConfig` PLUS the live data-load leg the W1-B
    /// store leg deferred. After the bundle is published (unchanged), the
    /// idempotent CDC-aware seed is loaded into the already-deployed `sink` as
    /// the LEVELED plan (card P2 — a faithful partition of the published
    /// `Data/seed.sql`, dispatched per level through the injected executor),
    /// the movement is measured, and (with a store) the episode is recorded
    /// with the measured `DataObservation`. The schema is assumed already
    /// deployed on the sink (the publication→deploy→load premise: the operator
    /// deploys the published DDL + enables CDC for the SSIS consumer, then the
    /// data lands).
    /// Emit-then-load over the publish's own acquisition (PL-1): the seed
    /// plan projects from threaded values (`projectSeedPlanUsing`) — no
    /// second extract, hydration, or chain run — then loads. Sync dispatch
    /// into the Task-returning load (FS3511-safe call shape).
    let private loadFromAcquisition
        (cdcCaptureTotal: SqlConnection -> Task<Result<int>>)
        (executeLeveled: DataComposer.LeveledDeploymentText -> Task<unit>)
        (cfg: Config.Config)
        (acquired: EstateAcquisition)
        (sink: SqlConnection)
        (storePath: string option)
        (timeline: Timeline)
        (environment: Environment)
        (at: System.DateTimeOffset)
        : Task<Result<FullExportStoreLeg option * int>> =
        match projectSeedPlanUsing cfg acquired with
        | Error errors -> Task.FromResult (Result.failure errors)
        | Ok (emitted, plan) ->
            loadLeveledSeedAndRecord cdcCaptureTotal executeLeveled emitted plan sink storePath timeline environment at

    /// Card P2 re-threaded seam: the second argument is the LEVELED seed
    /// executor (`Deploy.executeLeveledSeed <connection string>` at the CLI
    /// face — partial application carries the connection string the
    /// per-segment opens need; `sink` remains the CDC measure's connection).
    let runWithConfigAndLoad
        (cdcCaptureTotal: SqlConnection -> Task<Result<int>>)
        (executeLeveled: DataComposer.LeveledDeploymentText -> Task<unit>)
        (cfg: Config.Config)
        (sink: SqlConnection)
        (storePath: string option)
        (timeline: Timeline)
        (environment: Environment)
        (at: System.DateTimeOffset)
        : Task<Result<RunReport * FullExportStoreLeg option * int>> =
        task {
            // PL-1 — one estate acquisition: the load leg consumes the
            // publish's extract triple + composed state instead of paying a
            // second full publish (model read + hydration + chain + render).
            let! acquiredR = runWithConfigAcquiring cfg
            match acquiredR with
            | Error errors -> return Result.failure errors
            | Ok (report, acquired) ->
                // The seed-load leg is a declared stage on the publish-and-load
                // spine (`Spines.publishWith … true`, 2026-07-02) — bracketed so
                // the board covers the potentially-longest leg of the run (the
                // episode record, when a store rides, happens INSIDE this leg).
                let! loadResult =
                    stagedLeg (StageName.value Stages.seedLoad) (fun () ->
                        loadFromAcquisition cdcCaptureTotal executeLeveled cfg acquired sink storePath timeline environment at)
                return loadResult |> Result.map (fun (leg, cdcDelta) -> report, leg, cdcDelta)
        }
