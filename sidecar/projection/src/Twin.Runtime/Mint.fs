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
/// generator; the Twin only *prepares evidence, volumes, corrections,
/// and pools*. Value precedence per column is the engine's own (design
/// §3); volume precedence here is: scenario `rows` → per-kind default
/// (flat or centrality-weighted) — every kind the estate defines gets an
/// explicit `VolumeTarget`, so an unprofiled kind is never silently
/// zero-row.
[<RequireQualifiedAccess>]
module Mint =

    /// Centrality weighting constants — same posture as the kernel's
    /// synthetic flow (bounded amplification of structurally central
    /// kinds).
    let private centralityStrength : decimal = 1.0m
    let private centralityMaxFactor : decimal = 10.0m

    let private correctionUnreadable (path: string) (detail: string) : ValidationError =
        ValidationError.createWithMetadata
            "twin.corrections.unreadable"
            "The corrections artifact could not be read."
            (Map.ofList [ "path", Some path; "detail", Some detail ])

    let private scenarioNotYet (scenario: string) (field: string) : ValidationError =
        ValidationError.createWithMetadata
            "twin.scenario.notYetSupported"
            "This scenario field is not built yet: column overrides, per-parent ratios, and pins land with the scenario compiler. Volume (rows), scale, and seed apply today."
            (Map.ofList [ "scenario", Some scenario; "field", Some field ])

    /// Load the blessed corrections artifact, when configured.
    let loadCorrection (root: string) (path: string option) : Result<Correction option> =
        match path with
        | None -> Result.success None
        | Some rel ->
            let full = System.IO.Path.Combine(root, rel.Replace('/', System.IO.Path.DirectorySeparatorChar))
            try
                let json = System.IO.File.ReadAllText full
                CorrectionCodec.deserialize json |> Result.map Some
            with ex ->
                Result.failureOf (correctionUnreadable rel ex.Message)

    /// The effective (scale, seed) after the scenario's overrides.
    let effectiveScaleSeed (config: TwinConfig) (scenario: ScenarioIr option) : decimal * uint64 =
        let chainScale = scenario |> Option.bind (fun s -> s.Scale) |> Option.defaultValue config.Scale
        let chainSeed = scenario |> Option.bind (fun s -> s.Seed) |> Option.defaultValue config.Seed
        chainScale, chainSeed

    /// Default per-kind volumes for every synthetic-target kind (kinds
    /// with provided pools excluded — they are not generated). Flat mode
    /// assigns `defaultRows` everywhere; centrality mode amplifies
    /// structurally central kinds by the kernel's PageRank derivation,
    /// bounded by `centralityMaxFactor`.
    let private defaultVolumes
        (config: TwinConfig)
        (catalog: Catalog)
        (pools: Map<SsKey, string list>)
        : Result<Map<SsKey, VolumeTarget>> =
        let targets =
            Catalog.allKinds catalog
            |> List.filter (fun k -> not (Map.containsKey k.SsKey pools))
            |> List.map (fun k -> k.SsKey)
        match config.Volumes with
        | FlatVolumes ->
            targets
            |> List.map (fun k -> k, VolumeTarget.Absolute config.DefaultRows)
            |> Map.ofList
            |> Result.success
        | CentralityVolumes ->
            match Projection.Core.Passes.TopologicalOrderPass.runWith Projection.Core.TreatAsCycle catalog with
            | topoLineage ->
                let topo = topoLineage.Value
                let ranking = (Projection.Core.Passes.CentralityPass.registered.Run topo).Value.Value
                // Factors from the kernel derivation at scale 1 — then
                // realized as ABSOLUTE rows off `defaultRows`, because with
                // no evidence a multiplier would amplify an observed count
                // of zero.
                let factors = SyntheticVolume.byCentrality centralityStrength centralityMaxFactor 1.0m ranking
                targets
                |> List.map (fun k ->
                    let factor =
                        match Map.tryFind k factors with
                        | Some (VolumeTarget.Multiplier f) -> f
                        | Some (VolumeTarget.Absolute n) -> decimal n / decimal (max 1 config.DefaultRows)
                        | None -> 1.0m
                    let rows = int (System.Decimal.Truncate (decimal config.DefaultRows * factor))
                    k, VolumeTarget.Absolute (max 1 rows))
                |> Map.ofList
                |> Result.success

    /// Scenario `rows` overrides, bound by coordinate against the twin
    /// catalog (law 2 — an unbound coordinate refuses by name). Column
    /// overrides, per-parent ratios, and pins are named as not-yet-built
    /// until the scenario compiler lands (M4) — never silently dropped.
    let private scenarioVolumes
        (index: CatalogIndex)
        (scenario: ScenarioIr option)
        : Result<Map<SsKey, VolumeTarget>> =
        match scenario with
        | None -> Result.success Map.empty
        | Some s ->
            let notYet =
                [ for (coord, o) in s.Tables do
                    if not (List.isEmpty o.Columns) then
                        yield scenarioNotYet s.Name (System.String.Concat(TableCoordinate.text coord, ".columns"))  // LINT-ALLOW: terminal refusal metadata naming the field path
                    if not (List.isEmpty o.PerParent) then
                        yield scenarioNotYet s.Name (System.String.Concat(TableCoordinate.text coord, ".perParent"))  // LINT-ALLOW: terminal refusal metadata naming the field path
                  if not (List.isEmpty s.Pins) then
                    yield scenarioNotYet s.Name "pins" ]
            match notYet with
            | _ :: _ -> Result.failure notYet
            | [] ->
                s.Tables
                |> List.choose (fun (coord, o) -> o.Rows |> Option.map (fun rows -> coord, rows))
                |> List.map (fun (coord, rows) ->
                    CatalogIndex.bindKind index coord
                    |> Result.map (fun kind -> kind.SsKey, VolumeTarget.Absolute rows))
                |> Result.aggregate
                |> Result.map Map.ofList

    /// Assemble the engine `SyntheticConfig` + the boundary realization
    /// for one mint.
    let prepare
        (config: TwinConfig)
        (scenario: ScenarioIr option)
        (catalog: Catalog)
        (pools: Map<SsKey, string list>)
        (correction: Correction option)
        : Result<SyntheticConfig * (Map<SsKey, StaticRow list> -> Map<SsKey, StaticRow list>)> =
        let index = CatalogIndex.ofCatalog catalog
        let scale, _ = effectiveScaleSeed config scenario
        let baseConfig =
            { SyntheticConfig.defaultConfig with
                Scale = scale
                ProvidedPools = pools }
        let withCorrection =
            match correction with
            | None -> Result.success (baseConfig, id)
            | Some c ->
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
        | Ok (cfg, realize) ->
            match defaultVolumes config catalog pools, scenarioVolumes index scenario with
            | Ok defaults, Ok overrides ->
                let volumes = Map.fold (fun acc k v -> Map.add k v acc) defaults overrides
                Result.success ({ cfg with VolumeByKind = volumes }, realize)
            | dR, oR -> Result.failure (Result.errors dR @ Result.errors oR)

    /// Drive the kernel's synthetic load against the twin. WipeAndLoad —
    /// a mint always lands a fresh deterministic dataset (the estate's
    /// own seeded kinds excluded from generation and wipe via K1).
    let run
        (sink: SqlConnection)
        (catalog: Catalog)
        (profile: Profile)
        (config: SyntheticConfig)
        (seed: uint64)
        (realize: Map<SsKey, StaticRow list> -> Map<SsKey, StaticRow list>)
        : Task<Result<Transfer.TransferReport>> =
        Transfer.runSynthetic Transfer.Execute EmissionMode.WipeAndLoad false sink catalog profile config seed realize
