namespace Projection.Core.Passes

open Projection.Core

/// H-076 — Query plan hint annotation emission. For FK references
/// with high cardinality selectivity (high DistinctCount), suggests
/// lowering the fill factor on the associated index to improve insert
/// performance. Emits `SuggestedConfig` entries referencing the index
/// SsKey as actionable config edits.
///
/// Inputs: `Catalog` (for index lookup) + `Profile` (for FK
/// selectivity data). Output: `QueryHintReport`.
[<RequireQualifiedAccess>]
module QueryHintPass =

    [<Literal>]
    let version : int = 1

    [<Literal>]
    let private passName : string = "queryHint"

    // Thresholds — NOT [<Literal>] on int/decimal per discipline
    // (int is OK as Literal, but decimal is not; keep both private
    // let bindings for consistency).
    let private highSelectivityThreshold : int64   = 100L   // DistinctCount ≥ this
    let private suggestedFillFactor      : int     = 70
    let private suggestedFillFactorStr   : string  = "70"

    let run (catalog: Catalog) (profile: Profile) : Lineage<Diagnostics<QueryHintReport>> =
        use _ = Bench.scope "pass.queryHint"

        let allKinds = Catalog.allKinds catalog

        // Build a map from (kindSsKey, attributeSsKey) → Kind for
        // quick index lookup.
        let kindByKey : Map<SsKey, Kind> =
            allKinds |> List.map (fun k -> k.SsKey, k) |> Map.ofList

        let suggestions =
            profile.ForeignKeySelectivities
            |> List.filter (fun sel -> sel.DistinctCount >= highSelectivityThreshold)
            |> List.choose (fun sel ->
                // Find the Reference in the catalog.
                let refOpt =
                    allKinds
                    |> List.tryPick (fun k ->
                        k.References
                        |> List.tryFind (fun r -> r.SsKey = sel.ReferenceKey)
                        |> Option.map (fun r -> k, r))
                match refOpt with
                | None -> None
                | Some (sourceKind, ref_) ->
                    // Find an index on sourceKind covering the FK attribute
                    // that has no explicit fill factor (i.e., using server
                    // default, which we interpret as 80).
                    let idxOpt =
                        sourceKind.Indexes
                        |> List.tryFind (fun idx ->
                            not (IndexUniqueness.isPrimaryKey idx.Uniqueness) &&
                            Option.isNone idx.FillFactor &&
                            idx.Columns
                            |> List.exists (fun ic -> ic.Attribute = ref_.SourceAttribute))
                    match idxOpt with
                    | None -> None
                    | Some idx -> Some (idx.SsKey, suggestedFillFactor, sel))
            |> List.sortBy (fun (idxKey, _, _) -> idxKey)

        let fillFactorSuggestions =
            suggestions |> List.map (fun (idxKey, ff, _) -> idxKey, ff)

        let report = { FillFactorSuggestions = fillFactorSuggestions }

        let diagnostics =
            suggestions
            |> List.map (fun (idxKey, _, sel) ->
                let configPath =
                    sprintf "indexes[\"%s\"].fillFactor" (SsKey.rootOriginal idxKey)
                let suggestedConfig : SuggestedConfig =
                    { Path  = configPath
                      Value = suggestedFillFactorStr
                      Note  = Some (sprintf "High FK selectivity: DistinctCount=%d" sel.DistinctCount) }
                { DiagnosticEntry.create passName DiagnosticSeverity.Info
                    "topology.queryHint.fillFactor"
                    (sprintf "Index %s has no fill factor; high FK selectivity (DistinctCount=%d) suggests fill factor %d"
                        (SsKey.rootOriginal idxKey) sel.DistinctCount suggestedFillFactor)
                  with SsKey = Some idxKey; SuggestedConfig = Some suggestedConfig })

        let events =
            allKinds
            |> List.map (fun k ->
                { PassName       = passName
                  PassVersion    = version
                  SsKey          = k.SsKey
                  TransformKind  = Touched
                  Classification = DataIntent })

        lineageDiagnostics {
            do! LineageDiagnostics.writeLineages events
            do! LineageDiagnostics.writeDiagnostics diagnostics
            return report
        }

    let registered (profile: Profile) : RegisteredTransform<Catalog, QueryHintReport> =
        { Name         = passName
          Domain       = CrossCutting
          StageBinding = Pass
          Sites =
            [ { SiteName       = "fillFactorHint"
                Classification = DataIntent
                Rationale      = "FK selectivity from Profile drives fill-factor SuggestedConfig hints for high-cardinality indexes. Profile evidence only; no operator opinion." } ]
          Run    = fun c -> run c profile
          Status = Active }
