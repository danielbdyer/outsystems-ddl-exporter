namespace Projection.Pipeline

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
            /// The typed SSDT manifest built during projection (the same
            /// value serialized into `manifest.json` within `SsdtBundle`).
            /// Surfaced here so consumers — `runWithConfig`'s `RunReport`,
            /// drift detection — read the structured manifest (coverage,
            /// `ColumnProfiles`, deployment batches) without re-parsing
            /// the serialized JSON. `ColumnProfiles` is non-empty exactly
            /// when the acquired `Profile` carries numeric moments.
            Manifest : ManifestEmitter.Manifest
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
        |> String.concat "\nGO\n"  // LINT-ALLOW: terminal SQL-batch joiner across per-table SsdtBundle entries; segments are typed (each `Body` is the rendered ScriptDom output from SsdtDdlEmitter); BCL `String.concat` IS the use-case-specific library at the SQL-batch concatenation boundary

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
            let rewritten =
                files
                |> ArtifactByKind.toMap
                |> Map.map (fun key file ->
                    match Map.tryFind key folders.ByKind with
                    | None        -> file
                    | Some folder ->
                        let segments = file.RelativePath.Split('/')
                        let basename = segments.[segments.Length - 1]
                        { file with
                            RelativePath = System.String.Concat(folder, "/", basename) })
            match ArtifactByKind.create catalog rewritten with
            | Ok a -> a
            | Error err ->
                // Unreachable: we Map.map preserving keys; the input
                // keyset equals Catalog.allKinds by construction
                // (input came from SsdtDdlEmitter.emitSlices which
                // smart-constructed against the same catalog).
                invalidOp (sprintf "Compose.applyEmissionFolderOverrides: %A" err)

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
        // H-034 — detect policy conflicts from the chain's lineage trail
        // and diagnostics. The detector gates on Selection-removal
        // evidence; normal `tightening.*` outcomes on visible kinds are
        // NOT flagged (per the post-audit fix).
        let policyConflicts =
            use _ = Bench.scope "compose.conflictDetect"
            ConflictDetector.detectConflicts composed.Trail passEntries
        // Chapter 4.9 slice δ — apply EmissionPolicy.filterPlatformAutoIndexes
        // at the post-chain seam. The filter is `OperatorIntent of Emission`
        // per pillar 9; lives outside the registered pass chain because
        // its evidence is policy, not catalog-derived. Identity when
        // `IncludePlatformAutoIndexes = true` (V1 parity default).
        let emittedCatalog = EmissionPolicy.filterPlatformAutoIndexes policy composedState.Catalog
        // Wave-2 slice 2.2 — thread the tightening decisions through the
        // emitter seam. `DecisionOverlay.ofComposeState` projects the
        // chain's NullabilityDecisions / UniqueIndexDecisions /
        // ForeignKeyDecisions into emitter-consumable `Set<SsKey>` lookups.
        // Byte-identical to pre-overlay emission in 2.2 (the emitter threads
        // the prefix arg unused); slices 2.3 / 2.4 begin consuming it. A18
        // holds — the overlay carries decisions (evidence), never Policy.
        let decisionOverlay = DecisionOverlay.ofComposeState composedState
        let bundle, manifest =
            (use _ = Bench.scope "emit.ssdtBundle.compose"
             match SsdtDdlEmitter.emitSlicesWith decisionOverlay emittedCatalog with
             | Ok ssdtFiles ->
                 let rewritten = applyEmissionFolderOverrides folders emittedCatalog ssdtFiles
                 let manifest =
                     let registry = SsdtDdlEmitter.registeredMetadata :: RegisteredTransforms.all
                     ManifestEmitter.buildFull
                         profile
                         registry
                         composedState.TopologicalOrder
                         versionedPolicy
                         policyConflicts
                         composed.Trail
                         emittedCatalog
                 SsdtBundle.compose rewritten manifest, manifest
             | Error err ->
                 // Unreachable in production paths: Catalog.allKinds is
                 // the keyset by construction; SsdtDdlEmitter.emitSlices
                 // produces one slice per kind (smart-constructor
                 // strict-equality); error states surface only on
                 // pathological catalogs that bypass smart constructors.
                 invalidOp (sprintf "Compose.project: SsdtDdlEmitter.emitSlices: %A" err))
        let json =
            (use _ = Bench.scope "emit.json"
             // Per Tier-1 #3: parse once at project time so the
             // Outputs seam is JsonNode-typed. Downstream consumers
             // query the tree without re-parse.
             match JsonNode.Parse(JsonEmitter.emit emittedCatalog) with
             | null -> invalidOp "Compose.project: JsonEmitter.emit produced unparseable text (unreachable)"
             | n    -> n)
        let distributions =
            (use _ = Bench.scope "emit.distributions"
             match JsonNode.Parse(DistributionsEmitter.emit emittedCatalog profile) with
             | null -> invalidOp "Compose.project: DistributionsEmitter.emit produced unparseable text (unreachable)"
             | n    -> n)
        // Chapter 5+ slices 5.13.remediation-emitter +
        // 5.13.summary-formatter — per-decision remediation SQL +
        // 6-bucket summary prose. Pull DecisionSets from the
        // post-chain ComposeState; empty defaults when a decision
        // set wasn't produced (no interventions registered for the
        // axis). Bench-scoped at each emitter's boundary.
        let nullabilityDecisions =
            composedState.NullabilityDecisions
            |> Option.defaultValue NullabilityRules.emptyDecisionSet
        let uniqueIndexDecisions =
            composedState.UniqueIndexDecisions
            |> Option.defaultValue UniqueIndexRules.emptyDecisionSet
        let foreignKeyDecisions =
            composedState.ForeignKeyDecisions
            |> Option.defaultValue ForeignKeyRules.emptyDecisionSet
        let remediationSql =
            RemediationEmitter.emit
                composedState.Catalog nullabilityDecisions uniqueIndexDecisions foreignKeyDecisions
        let summaryText =
            SummaryFormatter.formatText
                nullabilityDecisions uniqueIndexDecisions foreignKeyDecisions
        let suggestConfigJson =
            (use _ = Bench.scope "emit.suggestConfig"
             SuggestConfigEmitter.emit passEntries)
        let outputs = {
            SsdtBundle        = bundle
            DataBundle        = Map.empty   // populated by projectWithState when EmitData (AC-X1)
            Json              = json
            Distributions     = distributions
            RemediationSql    = remediationSql
            SummaryText       = summaryText
            SuggestConfigJson = suggestConfigJson
            Manifest          = manifest
        }
        outputs, composedState

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
    /// Sibling to `project`; preserves the EmissionPolicy filter
    /// behavior. Operator-tightening interventions registered in
    /// `fullPolicy.Tightening.Interventions` fire here.
    let projectWith
        (fullPolicy: Policy)
        (profile: Profile)
        (emissionPolicy: EmissionPolicy)
        (catalog: Catalog)
        : Outputs =
        let chain = RegisteredTransforms.allChainStepsFor fullPolicy profile
        projectFromChain chain profile emissionPolicy catalog

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
    let projectWithState
        (fullPolicy: Policy)
        (profile: Profile)
        (emissionPolicy: EmissionPolicy)
        (folders: EmissionFolders)
        (groups: TransformGroups)
        (catalog: Catalog)
        : Outputs * ComposeState =
        let chain = RegisteredTransforms.allChainStepsFor fullPolicy profile
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
                emissionPolicy
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
                match Projection.Targets.Data.DataEmissionComposer.composeRendered fullPolicy finalState.Catalog profile with
                | Ok sql when not (System.String.IsNullOrWhiteSpace sql) ->
                    { outputs with DataBundle = Map.ofList [ "Data/seed.sql", sql ] }
                | Ok _ -> outputs   // no static/bootstrap rows in scope ⇒ nothing to emit
                | Error err ->
                    // Mirrors the SSDT-bundle invariant: a valid catalog never
                    // fails the composer (the keyset is `Catalog.allKinds`).
                    invalidOp (sprintf "Compose.projectWithState: DataEmissionComposer.composeRendered: %A" err)
        decorated, finalState

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
    let read (jsonPath: string) : Task<Result<Catalog>> =
        task {
            use _ = Bench.scope "compose.read"
            return! CatalogReader.parse (CatalogReader.SnapshotFile jsonPath)
        }

    /// Read a V1 `osm_model.json` from an in-memory string and parse
    /// it into a V2 Catalog. Used by the golden-file test to keep the
    /// regression surface hermetic.
    let readJson (json: string) : Task<Result<Catalog>> =
        task {
            use _ = Bench.scope "compose.readJson"
            return! CatalogReader.parse (CatalogReader.SnapshotJson json)
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
        let suffix = System.Guid.NewGuid().ToString("N").Substring(0, 12)
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
        // Final-path projection (under the eventual outputDir, post-swap)
        let jsonFinal          = Path.Combine(outputDir, ArtifactPath.json)
        let distributionsFinal = Path.Combine(outputDir, ArtifactPath.distributions)
        let remediationFinal   = Path.Combine(outputDir, ArtifactPath.remediation)
        let summaryFinal       = Path.Combine(outputDir, ArtifactPath.summary)
        let suggestConfigFinal = Path.Combine(outputDir, ArtifactPath.suggestConfig)
        bundleFinalPaths @ dataFinalPaths @ [ jsonFinal; distributionsFinal; remediationFinal; summaryFinal; suggestConfigFinal ]

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
        let tighteningR = TighteningBinding.fromConfig catalog cfg.Policy.Tightening
        let insertionR  = InsertionPolicyBinding.fromConfig cfg
        match tighteningR, insertionR with
        | Ok tightening, Ok insertion ->
            // AC-X1 — translate the config's data-emission toggles into the
            // EmissionPolicy. `staticSeeds` / `migrationDependencies` /
            // `bootstrap` turning on enables `EmitData`; `DataComposition`
            // selects which data emitters fire (Static included ⇒ AllRemaining;
            // Static off but Migration/Bootstrap on ⇒ AllExceptStatic).
            let emitData =
                cfg.Emission.StaticSeeds
                || cfg.Emission.MigrationDependencies
                || cfg.Emission.Bootstrap
            let dataComposition =
                if cfg.Emission.StaticSeeds then AllRemaining else AllExceptStatic
            Result.success {
                Policy.empty with
                    Tightening = tightening
                    Insertion  = insertion
                    Emission   = { Policy.empty.Emission with EmitData = emitData; DataComposition = dataComposition }
            }
        | _ ->
            let tighteningErrs = match tighteningR with Ok _ -> [] | Error es -> es
            let insertionErrs  = match insertionR  with Ok _ -> [] | Error es -> es
            Result.failure (tighteningErrs @ insertionErrs)

    /// Open the out-of-band source connection and enrich `Profile.empty`
    /// via the `LiveProfiler` adapter. An unreachable / malformed
    /// connection is a *named failure* (`pipeline.profiler.connectionFailed`),
    /// never a silent fallback to empty.
    let private profileFromLiveConnection
        (connectionString: string)
        (catalog: Catalog)
        : Task<Result<Profile>> =
        task {
            try
                use cnn = new SqlConnection(connectionString)
                do! cnn.OpenAsync()
                return! Projection.Adapters.Sql.LiveProfiler.attach cnn catalog Profile.empty
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
    let private acquireProfile (cfg: Config.Config) (catalog: Catalog) : Task<Result<Profile>> =
        task {
            if cfg.Profiler.Provider <> Config.LiveProfilerProvider then
                return Result.success Profile.empty
            else
                match System.Environment.GetEnvironmentVariable Config.SourceConnectionStringEnvVar with
                | null | "" ->
                    return
                        Result.failureOf
                            (ValidationError.create
                                "pipeline.profiler.connectionMissing"
                                (sprintf
                                    "profiler.provider = \"%s\" requires the %s environment variable (D9: connection sources are out-of-band)."
                                    Config.LiveProfilerProvider
                                    Config.SourceConnectionStringEnvVar))
                | connectionString ->
                    return! profileFromLiveConnection connectionString catalog
        }

    /// Synchronous core for `runWithConfig`. Extracted from the `task { }`
    /// block to keep the async surface minimal and allow static state-machine
    /// compilation (avoids FS3511 — deeply nested matches inside `task { }`
    /// prevent static compilation of the resumable state machine). The
    /// acquired `Profile` is threaded in by the async caller.
    let private runWithConfigCore
        (cfg: Config.Config)
        (parsed: Result<Catalog>)
        (profile: Profile)
        : Result<RunReport> =
        match parsed with
        | Error errors -> Result.failure errors
        | Ok catalog ->
            match applyRenames cfg catalog with
            | Error errors -> Result.failure errors
            | Ok renamedCatalog ->
                let policyR    = buildPolicyFromConfig cfg renamedCatalog
                let overridesR = SpecialCircumstancesBinding.fromConfig renamedCatalog cfg
                let foldersR   = EmissionFoldersBinding.fromConfig renamedCatalog cfg
                let groupsR    = TransformGroupsBinding.fromConfig cfg
                match policyR, overridesR, foldersR, groupsR with
                | Ok policy, Ok overrides, Ok folders, Ok groups ->
                    let outputs, finalState =
                        projectWithState policy profile EmissionPolicy.empty folders groups renamedCatalog
                    let diagnostics =
                        SpecialCircumstancesDiagnostics.emit overrides finalState
                        @ InactiveAttributeDiagnostics.emit profile
                        @ FkSelectivityDiagnostics.emit profile    // H-025
                        @ JointDependencyDiagnostics.emit profile   // H-026
                        // Wave-2 slice 2.5(b) — the FK silent-drop witness over
                        // the emitted catalog (slice-μ retired). Pure sibling of
                        // the emitter port; A18 holds.
                        @ SsdtDdlEmitter.foreignKeyDropDiagnostics finalState.Catalog
                        // 6.A.9 — the DECISION-driven FK-drop audit trail. A
                        // tightening Decision that drops an FK the source
                        // enforced is a safety change; surface one Warning per
                        // dropped decision so it is never silent at emission.
                        @ SsdtDdlEmitter.foreignKeyDecisionDropDiagnostics
                            (DecisionOverlay.ofComposeState finalState) finalState.Catalog
                    match write cfg.Output.Dir outputs with
                    | Ok paths    -> Result.success { Paths = paths; Diagnostics = diagnostics; Manifest = outputs.Manifest }
                    | Error errors -> Result.failure errors
                | _ ->
                    let policyErrs    = match policyR    with Ok _ -> [] | Error es -> es
                    let overridesErrs = match overridesR with Ok _ -> [] | Error es -> es
                    let foldersErrs   = match foldersR   with Ok _ -> [] | Error es -> es
                    let groupsErrs    = match groupsR    with Ok _ -> [] | Error es -> es
                    Result.failure (policyErrs @ overridesErrs @ foldersErrs @ groupsErrs)

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
    let runWithConfig (cfg: Config.Config) : Task<Result<RunReport>> =
        task {
            let! parsed = read cfg.Model.Path
            match parsed with
            | Error errors -> return Result.failure errors
            | Ok catalog ->
                let! profileResult = acquireProfile cfg catalog
                return profileResult |> Result.bind (runWithConfigCore cfg (Ok catalog))
        }

    // -- Track W1-B (seam T2): the diff-vs-prior store leg -------------------

    /// The emitted schema plane of a full-export run — `cfg.Model.Path`'s
    /// catalog after the config-driven rewrites (`applyRenames`). This is the
    /// **same** catalog `runWithConfigCore` projects to the CREATE files, so the
    /// displacement the store leg measures is the displacement the bundle
    /// publishes (B in `B ⊖ A`). Pure; no Profile dependence (the schema plane
    /// is profile-invariant — profiling only annotates the manifest).
    let private emittedSchema (cfg: Config.Config) : Task<Result<Catalog>> =
        task {
            let! parsed = read cfg.Model.Path
            return parsed |> Result.bind (applyRenames cfg)
        }

    /// AC-X1 (part B) — the emitted catalog + the rendered data-seed script for
    /// the live-load leg. Re-projects the config's model (genesis profile shape;
    /// the seed rows are catalog-borne `Modality.Static`, profile-independent) so
    /// the load consumes exactly the `Data/seed.sql` the bundle published. Empty
    /// seed when data emission is off.
    /// Synchronous core of `emittedSeed` (extracted so the task surface is a
    /// single `let!` + `return`, statically compilable in Release — FS3511).
    let private projectSeed (cfg: Config.Config) (parsed: Result<Catalog>) : Result<Catalog * string> =
        match parsed |> Result.bind (applyRenames cfg) with
        | Error es -> Result.failure es
        | Ok catalog ->
            match buildPolicyFromConfig cfg catalog,
                  EmissionFoldersBinding.fromConfig catalog cfg,
                  TransformGroupsBinding.fromConfig cfg with
            | Ok policy, Ok folders, Ok groups ->
                let outputs, _ =
                    projectWithState policy Profile.empty EmissionPolicy.empty folders groups catalog
                let seed = Map.tryFind "Data/seed.sql" outputs.DataBundle |> Option.defaultValue ""
                Result.success (catalog, seed)
            | p, f, g ->
                let errs =
                    (match p with Error e -> e | _ -> [])
                    @ (match f with Error e -> e | _ -> [])
                    @ (match g with Error e -> e | _ -> [])
                Result.failure errs

    let private emittedSeed (cfg: Config.Config) : Task<Result<Catalog * string>> =
        task {
            let! parsed = read cfg.Model.Path
            return projectSeed cfg parsed
        }

    /// State A — the prior emission's schema, reconstructed from the durable
    /// `EpisodicLifecycle` at `path` (the FTC fold over the chain). A **missing
    /// store is genesis**: A = ∅ (every kind `Add`, no `Remove`) — byte-faithful
    /// to today's first-emission behavior. A malformed store is fail-closed
    /// (`StoreReadFailed`), never silently treated as genesis.
    let private priorSchemaFromStore (path: string) : Result<Catalog, FullExportStoreError> =
        if System.IO.File.Exists path then
            match LifecycleStore.load path with
            | Error e -> Error (StoreReadFailed (string e))
            | Ok chain ->
                match EpisodicLifecycle.reconstructLatestSchema chain with
                | Ok a    -> Ok a
                | Error e -> Error (DisplacementFailed e)
        else
            match Catalog.create [] [] with
            | Ok empty   -> Ok empty
            | Error errs -> Error (StoreReadFailed (errs |> List.map (fun e -> e.Message) |> String.concat "; "))

    /// The prior chain's **accumulated** `.refactorlog` — every rename the
    /// timeline has ever performed, folded over its per-edge schema diffs via
    /// `RefactorLogEmitter.emit` + `accumulate` (AC-P6, the cumulative document).
    /// Genesis (no file / genesis-only chain) ⇒ empty. This is the prior the new
    /// displacement's refactorlog accumulates against, so a rename already
    /// committed is not re-emitted (deduped by `OperationKey`).
    let private priorAccumulatedRefactorLog (path: string) : Result<RefactorLogEntry list, FullExportStoreError> =
        if not (System.IO.File.Exists path) then Ok []
        else
            match LifecycleStore.load path with
            | Error e -> Error (StoreReadFailed (string e))
            | Ok chain ->
                match EpisodicLifecycle.schemaEvolutionChain chain with
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

    /// The next monotone `EpisodeCoordinate` for the timeline at `path` — ordinal
    /// 0 for a genesis (no file), else the latest episode's ordinal + 1. Mirrors
    /// the migrate path's coordinate derivation; the boundary supplies
    /// `environment` + `at` (Core holds no clock).
    let private nextStoreCoordinate
        (path: string)
        (environment: Environment)
        (at: System.DateTimeOffset)
        : Result<EpisodeCoordinate, FullExportStoreError> =
        let ordinalR : Result<int, FullExportStoreError> =
            if System.IO.File.Exists path then
                match LifecycleStore.load path with
                | Error e -> Error (StoreReadFailed (string e))
                | Ok chain -> Ok (Version.ordinal (Episode.version (EpisodicLifecycle.latest chain)) + 1)
            else Ok 0
        match ordinalR with
        | Error e -> Error e
        | Ok ordinal ->
            match Version.create ordinal (sprintf "v%d" ordinal) with
            | Ok version -> Ok (EpisodeCoordinate.create version environment at)
            | Error errs -> Error (RecordFailed (errs |> List.map (fun e -> e.Message) |> String.concat "; "))

    /// Record exactly ONE new episode (the run's emitted schema as the new
    /// schema plane) onto the timeline at `path`. Genesis-opens on the first
    /// run; load-and-appends thereafter. Fail-closed on a malformed store or a
    /// non-monotone append. Reuses `LifecycleStore.load/save` + the
    /// `EpisodicLifecycle` monotone-history invariant.
    let private recordEpisode
        (path: string)
        (timeline: Timeline)
        (episode: Episode)
        : Result<EpisodicLifecycle, FullExportStoreError> =
        let chainR : Result<EpisodicLifecycle, FullExportStoreError> =
            if System.IO.File.Exists path then
                match LifecycleStore.load path with
                | Error e -> Error (StoreReadFailed (string e))
                | Ok existing ->
                    match EpisodicLifecycle.append episode existing with
                    | Ok chain  -> Ok chain
                    | Error errs -> Error (RecordFailed (errs |> List.map (fun e -> e.Message) |> String.concat "; "))
            else Ok (EpisodicLifecycle.genesis timeline episode)
        match chainR with
        | Error e -> Error e
        | Ok chain ->
            match LifecycleStore.save path chain with
            | Ok ()   -> Ok chain
            | Error e -> Error (StoreReadFailed (string e))

    /// The diff-vs-prior store leg for one full-export run (seam T2). Given the
    /// run's emitted schema (state B) and the timeline at `path`: load the prior
    /// emission's schema (state A), measure `B ⊖ A`, accumulate the
    /// displacement's refactorlog against the prior committed log, build the
    /// `ChangeManifest` for the edge, and record exactly one new episode. Pure
    /// w.r.t. the genesis emission (it runs *after* the bundle lands and never
    /// alters it); the only side effect is the durable store write.
    let private runStoreLeg
        (path: string)
        (timeline: Timeline)
        (environment: Environment)
        (at: System.DateTimeOffset)
        (refactorLogRef: string option)
        (data: DataObservation)
        (emitted: Catalog)
        : Result<FullExportStoreLeg, FullExportStoreError> =
        match priorSchemaFromStore path with
        | Error e -> Error e
        | Ok prior ->
            match CatalogDiff.between prior emitted with
            | Error e -> Error (DisplacementFailed e)
            | Ok displacement ->
                match priorAccumulatedRefactorLog path with
                | Error e -> Error e
                | Ok priorLog ->
                    match RefactorLogEmitter.emit displacement with
                    | Error e -> Error (DisplacementFailed e)
                    | Ok currentArtifact ->
                        let accumulated =
                            RefactorLogEmitter.accumulateArtifact priorLog currentArtifact
                        match nextStoreCoordinate path environment at with
                        | Error e -> Error e
                        | Ok coordinate ->
                            // The emitted schema is the new episode's schema plane
                            // (the same `Catalog` the bundle published).
                            let episode = Episode.create coordinate emitted Profile.empty refactorLogRef data
                            match recordEpisode path timeline episode with
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

    /// The diff-vs-prior store leg for a config, or `Ok None` when no store is
    /// supplied. Extracted so `runWithConfigAndStore` keeps a two-level await
    /// depth (the three-level `let!`-in-match nesting is not statically
    /// compilable under Release — FS3511).
    let private storeLegFromConfig
        (cfg: Config.Config)
        (storePath: string option)
        (timeline: Timeline)
        (environment: Environment)
        (at: System.DateTimeOffset)
        : Task<Result<FullExportStoreLeg option>> =
        match storePath with
        | None -> Task.FromResult (Result.success None)
        | Some p when System.String.IsNullOrWhiteSpace p -> Task.FromResult (Result.success None)
        | Some path ->
            task {
                let! emittedR = emittedSchema cfg
                return
                    match emittedR with
                    | Error errors -> Result.failure errors
                    | Ok emitted ->
                        match runStoreLeg path timeline environment at None DataObservation.empty emitted with
                        | Ok leg -> Result.success (Some leg)
                        | Error storeErr -> Result.failureOf (mapStoreErr storeErr)
            }

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
    let runWithConfigAndStore
        (cfg: Config.Config)
        (storePath: string option)
        (timeline: Timeline)
        (environment: Environment)
        (at: System.DateTimeOffset)
        : Task<Result<RunReport * FullExportStoreLeg option>> =
        task {
            let! reportResult = runWithConfig cfg
            match reportResult with
            | Error errors -> return Result.failure errors
            | Ok report ->
                let! legResult = storeLegFromConfig cfg storePath timeline environment at
                return legResult |> Result.map (fun legOpt -> report, legOpt)
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
            match runStoreLeg path timeline environment at None data emitted with
            | Ok leg -> Result.success (Some leg, cdcDelta)
            | Error storeErr -> Result.failureOf (mapStoreErr storeErr)
        | _ -> Result.success (None, cdcDelta)

    let loadSeedAndRecord
        (cdcCaptureTotal: SqlConnection -> Task<int>)
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
        task {
            let! baseline = cdcCaptureTotal sink
            // Unconditional `do!` of a synchronously-selected Task (a *conditional*
            // `do!` is the FS3511 trigger): an empty seed loads nothing.
            let execSeed =
                if System.String.IsNullOrWhiteSpace seed then Task.FromResult ()
                else executeBatch sink seed
            do! execSeed
            let! post = cdcCaptureTotal sink
            return recordLoad emitted (post - baseline) storePath timeline environment at
        }

    /// AC-X1 (part B) — `runWithConfig` PLUS the live data-load leg the W1-B
    /// store leg deferred. After the bundle is published (unchanged), the
    /// idempotent CDC-aware seed (`Data/seed.sql`) is loaded into the
    /// already-deployed `sink`, the movement is measured, and (with a store) the
    /// episode is recorded with the measured `DataObservation`. The schema is
    /// assumed already deployed on the sink (the publication→deploy→load premise:
    /// the operator deploys the published DDL + enables CDC for the SSIS consumer,
    /// then the data lands).
    /// Emit-then-load for a config: re-project the seed and load it. Extracted
    /// so `runWithConfigAndLoad` keeps a two-level await depth (FS3511-safe;
    /// the three-level `let!`-in-match nesting does not statically compile).
    let private loadFromConfig
        (cdcCaptureTotal: SqlConnection -> Task<int>)
        (executeBatch: SqlConnection -> string -> Task<unit>)
        (cfg: Config.Config)
        (sink: SqlConnection)
        (storePath: string option)
        (timeline: Timeline)
        (environment: Environment)
        (at: System.DateTimeOffset)
        : Task<Result<FullExportStoreLeg option * int>> =
        task {
            let! seedR = emittedSeed cfg
            return!
                match seedR with
                | Error errors -> Task.FromResult (Result.failure errors)
                | Ok (emitted, seed) ->
                    loadSeedAndRecord cdcCaptureTotal executeBatch emitted seed sink storePath timeline environment at
        }

    let runWithConfigAndLoad
        (cdcCaptureTotal: SqlConnection -> Task<int>)
        (executeBatch: SqlConnection -> string -> Task<unit>)
        (cfg: Config.Config)
        (sink: SqlConnection)
        (storePath: string option)
        (timeline: Timeline)
        (environment: Environment)
        (at: System.DateTimeOffset)
        : Task<Result<RunReport * FullExportStoreLeg option * int>> =
        task {
            let! reportResult = runWithConfig cfg
            match reportResult with
            | Error errors -> return Result.failure errors
            | Ok report ->
                let! loadResult = loadFromConfig cdcCaptureTotal executeBatch cfg sink storePath timeline environment at
                return loadResult |> Result.map (fun (leg, cdcDelta) -> report, leg, cdcDelta)
        }
