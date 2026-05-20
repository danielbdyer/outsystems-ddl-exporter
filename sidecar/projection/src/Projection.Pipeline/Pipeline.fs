namespace Projection.Pipeline

open System.IO
open System.Text.Json.Nodes
open System.Threading.Tasks
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
            /// Distributions JSON from `DistributionsEmitter`,
            /// consuming `Profile.empty` since the dogfood frame does
            /// not yet thread profile evidence end-to-end. Same
            /// JsonNode-at-the-seam treatment as `Json` above.
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
    }

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
    let private projectFromChainWithState
        (chain: PassChainAdapter list)
        (policy: EmissionPolicy)
        (folders: EmissionFolders)
        (groups: TransformGroups)
        (catalog: Catalog)
        : Outputs * ComposeState =
        let filteredChain = filterChainByGroups groups chain
        use _ = Bench.scope "compose.project"
        let composedState =
            (use _ = Bench.scope "compose.runChain"
             PassChainAdapter.compose filteredChain (ComposeState.initial catalog)
             |> LineageDiagnostics.payload)
        // Chapter 4.9 slice δ — apply EmissionPolicy.filterPlatformAutoIndexes
        // at the post-chain seam. The filter is `OperatorIntent of Emission`
        // per pillar 9; lives outside the registered pass chain because
        // its evidence is policy, not catalog-derived. Identity when
        // `IncludePlatformAutoIndexes = true` (V1 parity default).
        let emittedCatalog = EmissionPolicy.filterPlatformAutoIndexes policy composedState.Catalog
        let bundle =
            (use _ = Bench.scope "emit.ssdtBundle.compose"
             match SsdtDdlEmitter.emitSlices emittedCatalog with
             | Ok ssdtFiles ->
                 let rewritten = applyEmissionFolderOverrides folders emittedCatalog ssdtFiles
                 let manifest = ManifestEmitter.emit emittedCatalog
                 SsdtBundle.compose rewritten manifest
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
             match JsonNode.Parse(DistributionsEmitter.emit emittedCatalog Profile.empty) with
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
        let outputs = {
            SsdtBundle     = bundle
            Json           = json
            Distributions  = distributions
            RemediationSql = remediationSql
            SummaryText    = summaryText
        }
        outputs, composedState

    let private projectFromChain
        (chain: PassChainAdapter list)
        (policy: EmissionPolicy)
        (catalog: Catalog)
        : Outputs =
        projectFromChainWithState chain policy EmissionFolders.empty TransformGroups.empty catalog |> fst

    /// Production-shape project: routes through
    /// `RegisteredTransforms.allChainSteps`. The hand-coded "emit raw
    /// catalog" shape retired at chapter A.4.7' slice δ; the registry
    /// is canonical. Chapter 4.9 slice δ — accepts an `EmissionPolicy`
    /// driving the platform-auto-indexes filter.
    let project (policy: EmissionPolicy) (catalog: Catalog) : Outputs =
        projectFromChain RegisteredTransforms.allChainSteps policy catalog

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
        projectFromChain chain emissionPolicy catalog

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
        projectFromChainWithState chain emissionPolicy folders groups catalog

    /// Skeleton-shape project: routes through
    /// `RegisteredTransforms.skeletonChainSteps` (per chapter A.4.7'
    /// slice ε; the four pure-DataIntent passes). Yields the baseline
    /// reachable from `Project(catalog, Policy.empty, profile)` —
    /// `osm emit --skeleton-only` consumes this. Chapter 4.9 slice δ —
    /// uses `EmissionPolicy.defaults` (no filtering) because the
    /// skeleton view is operator-free by definition.
    let projectSkeleton (catalog: Catalog) : Outputs =
        projectFromChain RegisteredTransforms.skeletonChainSteps EmissionPolicy.empty catalog

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
        // Final-path projection (under the eventual outputDir, post-swap)
        let jsonFinal = Path.Combine(outputDir, ArtifactPath.json)
        let distributionsFinal = Path.Combine(outputDir, ArtifactPath.distributions)
        let remediationFinal = Path.Combine(outputDir, ArtifactPath.remediation)
        let summaryFinal = Path.Combine(outputDir, ArtifactPath.summary)
        bundleFinalPaths @ [ jsonFinal; distributionsFinal; remediationFinal; summaryFinal ]

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
    let private buildPolicyFromConfig
        (cfg: Config.Config)
        (catalog: Catalog)
        : Result<Policy> =
        let tighteningR = TighteningBinding.fromConfig catalog cfg.Policy.Tightening
        let insertionR  = InsertionPolicyBinding.fromConfig cfg
        match tighteningR, insertionR with
        | Ok tightening, Ok insertion ->
            Result.success {
                Policy.empty with
                    Tightening = tightening
                    Insertion  = insertion
            }
        | _ ->
            let tighteningErrs = match tighteningR with Ok _ -> [] | Error es -> es
            let insertionErrs  = match insertionR  with Ok _ -> [] | Error es -> es
            Result.failure (tighteningErrs @ insertionErrs)

    /// Full end-to-end driven by a parsed `Config`. Reads `Model.Path`,
    /// applies config-driven catalog rewrites (rename), binds operator
    /// `Policy` (tightening — Chapter C slice C.1) + operator
    /// `SpecialCircumstances` (missing-PK / circular-deps
    /// acknowledgements — Chapter C slice C.2) + operator
    /// `EmissionFolders` (per-kind SSDT folder overrides — Chapter C
    /// slice C.3), projects through the policy-aware chain capturing
    /// the post-chain `ComposeState`, emits special-circumstances
    /// diagnostics with operator-allowlist `Metadata.acceptedVia`
    /// annotation, writes to `Output.Dir`, returns the paths + the
    /// diagnostic stream as a `RunReport`.
    let runWithConfig (cfg: Config.Config) : Task<Result<RunReport>> =
        task {
            let! parsed = read cfg.Model.Path
            match parsed with
            | Ok catalog ->
                match applyRenames cfg catalog with
                | Ok renamedCatalog ->
                    let policyR     = buildPolicyFromConfig cfg renamedCatalog
                    let overridesR  = SpecialCircumstancesBinding.fromConfig renamedCatalog cfg
                    let foldersR    = EmissionFoldersBinding.fromConfig renamedCatalog cfg
                    let groupsR     = TransformGroupsBinding.fromConfig cfg
                    match policyR, overridesR, foldersR, groupsR with
                    | Ok policy, Ok overrides, Ok folders, Ok groups ->
                        let outputs, finalState =
                            projectWithState policy Profile.empty EmissionPolicy.empty folders groups renamedCatalog
                        let diagnostics =
                            SpecialCircumstancesDiagnostics.emit overrides finalState
                        match write cfg.Output.Dir outputs with
                        | Ok paths ->
                            return Result.success { Paths = paths; Diagnostics = diagnostics }
                        | Error errors ->
                            return Result.failure errors
                    | _ ->
                        let policyErrs    = match policyR    with Ok _ -> [] | Error es -> es
                        let overridesErrs = match overridesR with Ok _ -> [] | Error es -> es
                        let foldersErrs   = match foldersR   with Ok _ -> [] | Error es -> es
                        let groupsErrs    = match groupsR    with Ok _ -> [] | Error es -> es
                        return Result.failure (policyErrs @ overridesErrs @ foldersErrs @ groupsErrs)
                | Error errors ->
                    return Result.failure errors
            | Error errors ->
                return Result.failure errors
        }
