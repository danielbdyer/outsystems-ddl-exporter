namespace Projection.Pipeline

open System.Threading.Tasks
open Projection.Core
open Projection.Adapters.Sql
open Projection.Targets.Json

/// THE_SYNTHETIC_DATA_DESIGN §5 / §8, slice S3 (the flow front-end). The
/// orchestration behind `from: synthetic, profile: file:<path>`: read the
/// durable Profile (the hinge between capture and synthesis), read the target
/// schema B from the model, open the sink, and drive the pure generate-then-
/// load runner (`Transfer.runSynthetic`). The Profile is `σ`'s evidence; the
/// model is the schema it generates *for*; the sink is where it lands.
///
/// File I/O + connection opening live here (the boundary); the algorithmic
/// heart (`SyntheticData.generate`) and the write seam stay pure / reused.
[<RequireQualifiedAccess>]
module SyntheticLoadRun =

    /// The fixed default seed (design §7: "an optional `--seed`, default a
    /// fixed seed") — the floor when neither the `synthetic` config block nor
    /// `--seed` sets one.
    [<Literal>]
    let defaultSeed : uint64 = 0x5117_8E5D_0000_0001UL

    /// Defaults for centrality volume weighting (opt-in via
    /// `synthetic.weightVolumeByCentrality`). `strength` 1 means a kind at 2× the
    /// mean FK-graph centrality gets ~2× its baseline volume; `maxFactor` 4 caps
    /// the heaviest hub so one dominant table cannot explode the row budget.
    /// (`let`, not `[<Literal>]` — a `[<Literal>] decimal` is a module-load bomb.)
    let private centralityWeightStrength : decimal = 1M
    let private centralityWeightMaxFactor : decimal = 4M

    /// §11 — resolve the base `SyntheticConfig` from the declarative `synthetic`
    /// config block, with the per-run `--scale` override winning over the block's
    /// `scale`, over the built-in default (config is the primary surface; the CLI
    /// is the per-run knob). The richer per-column blessed intent is layered ON
    /// TOP by `Correction.applyToConfig` (FUZZING §2), so this is only the coarse
    /// baseline (τ / preserve / synthesize / scale).
    let resolveConfig (section: Config.SyntheticSection) (scaleOverride: decimal option) : SyntheticConfig =
        { SyntheticConfig.defaultConfig with
            PreserveCardinalityMax =
                section.PreserveCardinalityMax |> Option.defaultValue SyntheticConfig.defaultConfig.PreserveCardinalityMax
            PreserveColumns   = Set.ofList section.Preserve
            SynthesizeColumns = Set.ofList section.Synthesize
            Scale =
                scaleOverride
                |> Option.orElse section.Scale
                |> Option.defaultValue SyntheticConfig.defaultConfig.Scale }

    /// §11 — resolve the PRNG seed: `--seed` (per-run) wins over the block's
    /// `seed`, over the fixed `defaultSeed`.
    let resolveSeed (section: Config.SyntheticSection) (seedOverride: uint64 option) : uint64 =
        seedOverride |> Option.orElse section.Seed |> Option.defaultValue defaultSeed

    /// Resolve a profile reference to a `Profile`. Accepts the durable
    /// `file:<path>` form (design §2.2) and a bare path; reads the file and
    /// decodes through `ProfileCodec` (re-proving every leaf invariant).
    let resolveProfile (profileRef: string) : Result<Profile> =
        let path =
            if profileRef.StartsWith("file:", System.StringComparison.OrdinalIgnoreCase)
            then profileRef.Substring 5
            else profileRef
        if System.String.IsNullOrWhiteSpace path then
            Result.failureOf (ValidationError.create "synthetic.profileRef.blank" "synthetic profile reference is blank.")
        elif not (System.IO.File.Exists path) then
            Result.failureOf
                (ValidationError.create "synthetic.profileRef.missing" (sprintf "synthetic profile file '%s' not found." path))
        else
            try ProfileCodec.deserialize (System.IO.File.ReadAllText path)
            with ex ->
                Result.failureOf (ValidationError.create "synthetic.profileRef.readFailed" ex.Message)

    /// Resolve a correction reference to a `Correction` (F0c-I/O; FUZZING §2).
    /// `None` (no `correction` field, no `--correction`) is the empty correction
    /// — the identity of the `Profile ⊕ Correction` fold AND of the Faker
    /// realization, so an uncorrected synthetic load is byte-identical to the
    /// pre-F0c flow. The durable artifact rides the `file:<path>` form (or a bare
    /// path) and decodes through `CorrectionCodec` (re-proving the no-conflicting
    /// -double-correction invariant on load — a hand-edited artifact is REFUSED,
    /// not silently last-write-wins).
    let resolveCorrection (correctionRef: string option) : Result<Correction> =
        match correctionRef with
        | None -> Result.success Correction.empty
        | Some raw ->
            let path =
                if raw.StartsWith("file:", System.StringComparison.OrdinalIgnoreCase)
                then raw.Substring 5
                else raw
            if System.String.IsNullOrWhiteSpace path then
                Result.failureOf (ValidationError.create "synthetic.correctionRef.blank" "synthetic correction reference is blank.")
            elif not (System.IO.File.Exists path) then
                Result.failureOf
                    (ValidationError.create "synthetic.correctionRef.missing" (sprintf "synthetic correction file '%s' not found." path))
            else
                try CorrectionCodec.deserialize (System.IO.File.ReadAllText path)
                with ex ->
                    Result.failureOf (ValidationError.create "synthetic.correctionRef.readFailed" ex.Message)

    /// Drive a synthetic load. `execute = false` previews (DryRun, no write);
    /// `execute = true` generates and loads to the sink. The target schema B is
    /// resolved by `ModelResolution` — live OSSYS when `modelOssys` is set
    /// (primary; V1-free), else the `osm_model.json` file (fallback);
    /// `profileRef` the evidence; `connSpec` the sink. Connection / grant / CDC
    /// gates ride `Transfer.runSynthetic`.
    ///
    /// NM-08/09 — the resolved catalog passes through the SAME module-filter seam
    /// (`ModuleFilterBinding.fromConfig modelSection` → `ModuleFilter.apply`)
    /// every other action routes through at `Program.needCatalog` /
    /// `Pipeline.applyModuleFilter`. Before this, the synthetic path called
    /// `resolveCatalog` WITHOUT the filter, so a `from: synthetic` flow emitted
    /// the FULL estate, silently ignoring `model.modules`. An empty
    /// `model.modules` is the all-permissive identity, so the default synthetic
    /// load stays byte-identical.
    let run
        (modelOssys: string option)
        (modelFile: string option)
        (profileRef: string)
        (correctionRef: string option)
        (connSpec: string)
        (emission: EmissionMode)
        (allowCdc: bool)
        (config: SyntheticConfig)
        (seed: uint64)
        (execute: bool)
        (modelSection: Config.ModelSection)
        (weightVolumeByCentrality: bool)
        (clusterFksByContext: bool)
        : Task<Result<Transfer.TransferReport>> =
        task {
            match resolveProfile profileRef, resolveCorrection correctionRef with
            | Error es, _ -> return Result.failure es
            | _, Error es -> return Result.failure es
            | Ok profile, Ok correction ->
                match! ModelResolution.resolveCatalog modelOssys modelFile with
                | Error es -> return Result.failure es
                | Ok rawCatalog ->
                    // NM-08/09 — narrow the resolved estate by `model.modules`
                    // through the shared seam BEFORE synthesis (identity when
                    // `model.modules` is empty), so a `from: synthetic` flow honors
                    // the scope every sibling action applies.
                    let filtered =
                        ModuleFilterBinding.fromConfig modelSection
                        |> Result.bind (fun opts -> ModuleFilter.apply opts rawCatalog)
                    match filtered with
                    | Error es -> return Result.failure es
                    | Ok catalog ->
                    // F-Faker — refuse BY NAME any blessed Faker coordinate that
                    // does NOT resolve against the model (a rename / typo), before
                    // generating: a hand-authored binding that points nowhere is an
                    // operator error to surface, never a silent no-op (the
                    // hand-authored-coordinate analogue of "refuse rather than corrupt").
                    match Correction.unresolvedFakerCoordinates catalog correction with
                    | (bad :: _) as unresolved ->
                        return Result.failureOf
                            (ValidationError.create "synthetic.correction.unresolvedCoordinate"
                                (sprintf "%d blessed Faker coordinate(s) name a location not in the model (e.g. %s/%s/%s); re-point them or update the artifact." (List.length unresolved) bad.Module bad.Entity bad.Attribute))
                    | [] ->
                    // F0c-I/O — fold the blessed corrections onto the config (the
                    // PURE `Profile ⊕ Correction` hinge: Pii ⇒ Synthesize, fidelity
                    // overrides, per-kind volume) AND build the boundary realization
                    // (Faker over the σ tokens / preserved values, seeded per row).
                    // Both are identity when the correction is empty, so an
                    // uncorrected load is byte-identical to the pre-F0c flow.
                    let baseConfig = Correction.applyToConfig catalog correction config
                    // H-071 / H-072 consumers (both opt-in) — the two graph analytics
                    // reach the synthetic path here. Both read the SAME FK-graph
                    // topology the load already computes for its write order, so it is
                    // derived at most once. Each is OFF by default and byte-identical
                    // when off — the whole branch is skipped, so no analytics run.
                    //   H-071 — weight per-kind volume by centrality (central kinds get
                    //           more rows); operator `volume` corrections win the merge.
                    //   H-072 — cluster FK locality by discovered bounded context (an
                    //           intra-context reference set reads as a self-consistent
                    //           slice). Threaded as a generic cluster map (Core stays
                    //           decoupled from BoundedContextDiscovery).
                    let effectiveConfig =
                        if not (weightVolumeByCentrality || clusterFksByContext) then baseConfig
                        else
                            let topo = (Projection.Core.Passes.TopologicalOrderPass.runWith Projection.Core.TreatAsCycle catalog).Value
                            let withVolume =
                                if weightVolumeByCentrality then
                                    let ranking = (Projection.Core.Passes.CentralityPass.registered.Run topo).Value.Value
                                    let derived = SyntheticVolume.byCentrality centralityWeightStrength centralityWeightMaxFactor baseConfig.Scale ranking
                                    { baseConfig with VolumeByKind = SyntheticVolume.mergeUnderOperator baseConfig.VolumeByKind derived }
                                else baseConfig
                            if clusterFksByContext then
                                let discovery = (Projection.Core.Passes.BoundedContextPass.registered.Run topo).Value.Value
                                let clusters =
                                    discovery.Candidates
                                    |> List.collect (fun c -> c.Members |> List.map (fun m -> m, c.AnchorKey))
                                    |> Map.ofList
                                { withVolume with FkLocalityClusters = clusters }
                            else withVolume
                    let realize = FakerRealization.realize catalog correction
                    // The sink opens through the one `ConnectionSpec.openSpec`
                    // opener (recon #13 — `env:` / `file:` / `live:` / bare,
                    // uniform with `transfer` / `slice`).
                    match! ConnectionSpec.openSpec SubstrateRole.Sink "synthetic-sink" connSpec with
                    | Error es -> return Result.failure es
                    | Ok sink ->
                        use sink = sink
                        let mode = if execute then Transfer.Execute else Transfer.DryRun
                        return! Transfer.runSynthetic mode emission allowCdc sink catalog profile effectiveConfig seed realize
        }
