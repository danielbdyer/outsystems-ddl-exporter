namespace Twin.Runtime

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Pipeline
open Projection.Targets.Json
open Twin.Core

/// THE TWIN — the mint (Twin.Runtime).
///
/// Assembles the engine inputs for one deterministic generation run and
/// drives the kernel's `Transfer.runSynthetic` — σ stays the single
/// generator; the Twin only prepares evidence, volumes, corrections,
/// pools, and the scenario compilation.
///
/// Precedence, per column: scenario override → rich evidence → shape
/// evidence → type-default + corrections realism. Volumes, per kind:
/// scenario (explicit rows / per-parent derivation) → evidence
/// (observed × scale) → default (`defaultRows`, flat or
/// centrality-weighted). Every kind carries an explicit resolution —
/// nothing is silently zero-row.
[<RequireQualifiedAccess>]
module Mint =

    /// Centrality weighting constants — same posture as the kernel's
    /// synthetic flow (bounded amplification of structurally central
    /// kinds).
    let private centralityStrength : decimal = 1.0m
    let private centralityMaxFactor : decimal = 10.0m

    /// Everything one mint needs, resolved.
    type MintPlan = {
        Config  : SyntheticConfig
        /// Layered evidence with the scenario overlay applied.
        Profile : Profile
        /// Boundary realization: corrections/Faker, then pins (pins
        /// stay verbatim — Faker never rewrites them).
        Realize : Map<SsKey, StaticRow list> -> Map<SsKey, StaticRow list>
        Seed    : uint64
    }

    let private correctionUnreadable (path: string) (detail: string) : ValidationError =
        ValidationError.createWithMetadata
            "twin.corrections.unreadable"
            "The corrections artifact could not be read."
            (Map.ofList [ "path", Some path; "detail", Some detail ])

    let private packUnreadable (path: string) (detail: string) : ValidationError =
        ValidationError.createWithMetadata
            "twin.evidence.unreadable"
            "An evidence pack could not be read."
            (Map.ofList [ "path", Some path; "detail", Some detail ])

    let private resolvePath (root: string) (ref: string) : string =
        let cleaned = if ref.StartsWith "file:" then ref.Substring 5 else ref
        System.IO.Path.Combine(root, cleaned.Replace('/', System.IO.Path.DirectorySeparatorChar))

    /// Load the blessed corrections artifact, when configured.
    let loadCorrection (root: string) (path: string option) : Result<Correction option> =
        match path with
        | None -> Result.success None
        | Some rel ->
            let full = resolvePath root rel
            try
                let json = System.IO.File.ReadAllText full
                CorrectionCodec.deserialize json |> Result.map Some
            with ex ->
                Result.failureOf (correctionUnreadable rel ex.Message)

    /// Load an evidence pack, when configured AND present (an absent
    /// file is a choice not yet made, never a refusal — THE_VOICE §14).
    let loadPack (root: string) (ref: string option) : Result<EvidencePack option> =
        match ref with
        | None -> Result.success None
        | Some r ->
            let full = resolvePath root r
            if not (System.IO.File.Exists full) then Result.success None
            else
                try Evidence.deserialize (System.IO.File.ReadAllText full) |> Result.map Some
                with ex -> Result.failureOf (packUnreadable r ex.Message)

    /// The effective (scale, seed) after the scenario chain's overrides
    /// (nearest scenario wins).
    let effectiveScaleSeed (config: TwinConfig) (chain: ScenarioIr list) : decimal * uint64 =
        let pick (f: ScenarioIr -> 'a option) (fallback: 'a) =
            chain |> List.rev |> List.tryPick f |> Option.defaultValue fallback
        pick (fun s -> s.Scale) config.Scale, pick (fun s -> s.Seed) config.Seed

    /// Observed row count for a kind, from layered evidence.
    let private observedRows (evidence: Profile) (kind: Kind) : int64 =
        kind.Attributes
        |> List.choose (fun a -> Profile.tryFindColumn a.SsKey evidence |> Option.map (fun c -> c.RowCount))
        |> function [] -> 0L | xs -> List.max xs

    /// Default per-kind volumes for kinds with neither provided pools
    /// nor evidence (evidence-backed kinds ride the engine's own
    /// observed × scale path).
    let private defaultVolumes
        (config: TwinConfig)
        (catalog: Catalog)
        (pools: Map<SsKey, string list>)
        (evidenced: Set<SsKey>)
        : Map<SsKey, VolumeTarget> =
        let targets =
            Catalog.allKinds catalog
            |> List.filter (fun k -> not (Map.containsKey k.SsKey pools) && not (Set.contains k.SsKey evidenced))
            |> List.map (fun k -> k.SsKey)
        match config.Volumes with
        | FlatVolumes ->
            targets |> List.map (fun k -> k, VolumeTarget.Absolute config.DefaultRows) |> Map.ofList
        | CentralityVolumes ->
            let topo = (Projection.Core.Passes.TopologicalOrderPass.runWith Projection.Core.TreatAsCycle catalog).Value
            let ranking = (Projection.Core.Passes.CentralityPass.registered.Run topo).Value.Value
            // Factors from the kernel derivation at scale 1, realized as
            // ABSOLUTE rows off `defaultRows` (with no evidence a
            // multiplier would amplify an observed count of zero).
            let factors = SyntheticVolume.byCentrality centralityStrength centralityMaxFactor 1.0m ranking
            targets
            |> List.map (fun k ->
                let factor =
                    match Map.tryFind k factors with
                    | Some (VolumeTarget.Multiplier f) -> f
                    | Some (VolumeTarget.Absolute n) -> decimal n / decimal (max 1 config.DefaultRows)
                    | None -> 1.0m
                k, VolumeTarget.Absolute (max 1 (int (System.Decimal.Truncate (decimal config.DefaultRows * factor)))))
            |> Map.ofList

    /// Assemble everything one mint needs. Pure except the three
    /// artifact reads (corrections, shape pack, rich pack).
    let prepare
        (root: string)
        (config: TwinConfig)
        (scenarioName: string)
        (catalog: Catalog)
        (pools: Map<SsKey, string list>)
        : Result<MintPlan> =
        let index = CatalogIndex.ofCatalog catalog
        let chain = TwinConfig.scenarioChain config scenarioName
        let scale, seed = effectiveScaleSeed config chain

        // Evidence: shape under rich (rich wins where both speak).
        let evidenceProfile =
            match loadPack root config.Evidence.ShapePath, loadPack root config.Evidence.RichRef with
            | Ok shape, Ok rich ->
                let bind (pack: EvidencePack option) : Result<Profile option> =
                    match pack with
                    | None -> Result.success None
                    | Some p -> Evidence.toProfile index p |> Result.map Some
                match bind shape, bind rich with
                | Ok s, Ok r ->
                    let baseP = defaultArg s Profile.empty
                    Result.success (match r with Some rich -> Evidence.layer baseP rich | None -> baseP)
                | sR, rR -> Result.failure (Result.errors sR @ Result.errors rR)
            | sR, rR -> Result.failure (Result.errors (sR |> Result.map ignore) @ Result.errors (rR |> Result.map ignore))

        match evidenceProfile with
        | Error es -> Result.failure es
        | Ok evidence ->
            let evidenced = Evidence.evidencedKinds index evidence
            let defaults = defaultVolumes config catalog pools evidenced
            let defaultVolumeOf (k: SsKey) : int =
                match Map.tryFind k defaults with
                | Some (VolumeTarget.Absolute n) -> n
                | Some (VolumeTarget.Multiplier f) -> int (System.Decimal.Truncate (decimal config.DefaultRows * f))
                | None ->
                    if Set.contains k evidenced then
                        let kind = CatalogIndex.kinds index |> List.tryPick (fun (_, kd) -> if kd.SsKey = k then Some kd else None)
                        match kind with
                        | Some kd -> max 1 (int (System.Decimal.Truncate (decimal (observedRows evidence kd) * scale)))
                        | None -> config.DefaultRows
                    else config.DefaultRows

            let baseConfig =
                { SyntheticConfig.defaultConfig with
                    Scale = scale
                    ProvidedPools = pools }

            let withCorrection =
                match loadCorrection root config.CorrectionsPath with
                | Error es -> Result.failure es
                | Ok None -> Result.success (baseConfig, id)
                | Ok (Some c) ->
                    match Correction.unresolvedFakerCoordinates catalog c with
                    | (bad :: _) as unresolved ->
                        Result.failureOf
                            (ValidationError.createWithMetadata
                                "twin.corrections.unresolvedCoordinate"
                                "A blessed Faker binding names a location the estate does not carry. Re-point it or update the artifact."
                                (Map.ofList
                                    [ "count", Some (string (List.length unresolved))
                                      "example", Some (System.String.Concat(bad.Module, "/", bad.Entity, "/", bad.Attribute)) ]))  // LINT-ALLOW: terminal refusal metadata naming the coordinate
                    | [] ->
                        Result.success (Correction.applyToConfig catalog c baseConfig, FakerRealization.realize catalog c)

            match withCorrection with
            | Error es -> Result.failure es
            | Ok (correctedConfig, fakerRealize) ->
                match ScenarioCompiler.compile index defaultVolumeOf chain with
                | Error es -> Result.failure es
                | Ok compiled ->
                    let volumes =
                        Map.fold (fun acc k v -> Map.add k v acc) defaults compiled.Volumes
                    let augment =
                        compiled.Pins
                        |> List.groupBy (fun p -> p.Kind)
                        |> List.map (fun (k, ps) -> k, ps |> List.collect (fun p -> p.PoolKeys))
                        |> Map.ofList
                    let syntheticConfig =
                        { correctedConfig with
                            VolumeByKind = volumes
                            PreserveColumns = Set.union correctedConfig.PreserveColumns compiled.ForcePreserve
                            SynthesizeColumns = Set.difference correctedConfig.SynthesizeColumns compiled.UnSynthesize
                            AugmentPools = augment }
                    Result.success
                        { Config = syntheticConfig
                          Profile = compiled.Overlay evidence
                          Realize = fakerRealize >> ScenarioCompiler.applyPins compiled.Pins
                          Seed = seed }

    /// Drive the kernel's synthetic load against the twin. WipeAndLoad —
    /// a mint always lands a fresh deterministic dataset (the estate's
    /// own seeded kinds excluded from generation and wipe via K1) — and
    /// PreferPreservedKeys (K1c): the twin is a FullRights sink, so σ's
    /// minted keys land verbatim; pinned keys are honored and a re-mint
    /// is byte-identical by construction.
    let run (sink: SqlConnection) (catalog: Catalog) (plan: MintPlan) : Task<Result<Transfer.TransferReport>> =
        Transfer.runSynthetic Transfer.Execute EmissionMode.WipeAndLoad false IdentityPolicy.PreferPreservedKeys sink catalog plan.Profile plan.Config plan.Seed plan.Realize
