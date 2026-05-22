namespace Projection.Core.Passes

open Projection.Core

/// H-075 — Schema complexity scoring. Derives a composite complexity
/// score from multiple IR-level metrics: cyclomatic complexity (FK
/// count), coupling index (average FK refs per kind), cohesion index
/// (fraction of intra-module FK edges), depth of inheritance (Kahn
/// levels), and nullability ratio. The weighted OverallScore is
/// normalized to [0, 1].
///
/// Inputs: `Catalog` (for attribute/module structure) +
/// `TopologicalOrder` (for FK graph + depth layers).
/// Output: `SchemaComplexity`.
[<RequireQualifiedAccess>]
module SchemaComplexityPass =

    [<Literal>]
    let version : int = 1

    [<Literal>]
    let private passName : string = "schemaComplexity"

    // Composite score weights — NOT [<Literal>] on decimal per
    // cctor-bomb discipline.
    let private weightCyclomatic  : decimal = 0.20m
    let private weightCoupling    : decimal = 0.20m
    let private weightCohesion    : decimal = 0.15m
    let private weightDepth       : decimal = 0.25m
    let private weightNullability : decimal = 0.20m

    /// Normalize a raw value to [0, 1] given an expected maximum.
    /// Values above `cap` clamp to 1.0.
    let private normalize (cap: decimal) (value: decimal) : decimal =
        if cap <= 0.0m then 0.0m
        else System.Math.Min(1.0m, value / cap)

    let run
        (catalog: Catalog)
        (t: TopologicalOrder)
        : Lineage<Diagnostics<SchemaComplexity>> =
        use _ = Bench.scope "pass.schemaComplexity"

        let allKinds   = Catalog.allKinds catalog
        let kindCount  = List.length allKinds
        let edgeCount  = List.length t.Edges

        // Cyclomatic complexity = number of FK edges.
        let cyclomatic = edgeCount

        // Coupling index = average FK references per kind.
        let coupling =
            if kindCount = 0 then 0.0m
            else decimal edgeCount / decimal kindCount

        // Cohesion index: per module, fraction of edges where both
        // endpoints are in the same module. Average across modules
        // that have ≥1 kind.
        let cohesion =
            let moduleKindSets =
                catalog.Modules
                |> List.map (fun m ->
                    m.Kinds |> List.map (fun k -> k.SsKey) |> Set.ofList)
                |> List.filter (fun s -> not (Set.isEmpty s))
            if List.isEmpty moduleKindSets then 1.0m
            else
                let fractions =
                    moduleKindSets
                    |> List.choose (fun kindSet ->
                        let intra =
                            t.Edges
                            |> List.filter (fun (src, tgt) ->
                                Set.contains src kindSet && Set.contains tgt kindSet)
                            |> List.length
                        let total =
                            t.Edges
                            |> List.filter (fun (src, tgt) ->
                                Set.contains src kindSet || Set.contains tgt kindSet)
                            |> List.length
                        if total = 0 then None
                        else Some (decimal intra / decimal total))
                if List.isEmpty fractions then 1.0m
                else List.sum fractions / decimal (List.length fractions)

        // Depth of inheritance = number of Kahn levels - 1.
        let depth =
            let levelCount = TopologicalOrder.levels t |> List.length
            max 0 (levelCount - 1)

        // Nullability ratio = nullable attributes / total attributes.
        let nullabilityRatio =
            let allAttrs =
                allKinds |> List.collect (fun k -> k.Attributes)
            let total = List.length allAttrs
            if total = 0 then 0.0m
            else
                let nullableCount =
                    allAttrs |> List.filter (fun a -> a.Column.IsNullable) |> List.length
                decimal nullableCount / decimal total

        // Normalize each component to [0, 1] with heuristic caps.
        // These caps are "typical large schema" reference points, not
        // strict thresholds — scores above cap clamp to 1.0.
        let cyclomaticN  = normalize 500.0m (decimal cyclomatic)
        let couplingN    = normalize   5.0m coupling
        // Cohesion: high cohesion = low complexity; invert.
        let cohesionN    = 1.0m - System.Math.Min(1.0m, System.Math.Max(0.0m, cohesion))
        let depthN       = normalize  20.0m (decimal depth)
        let nullabilityN = normalize   1.0m nullabilityRatio

        let overallScore =
            weightCyclomatic  * cyclomaticN +
            weightCoupling    * couplingN   +
            weightCohesion    * cohesionN   +
            weightDepth       * depthN      +
            weightNullability * nullabilityN

        let result =
            { CyclomaticComplexity = cyclomatic
              CouplingIndex        = coupling
              CohesionIndex        = cohesion
              DepthOfInheritance   = depth
              NullabilityRatio     = nullabilityRatio
              OverallScore         = overallScore }

        let entry =
            DiagnosticEntry.create passName DiagnosticSeverity.Info
                "schemaComplexity.computed"
                (sprintf "schemaComplexity v%d: cyclomatic=%d coupling=%.3f cohesion=%.3f depth=%d nullability=%.3f overall=%.3f"
                    version cyclomatic coupling cohesion depth nullabilityRatio overallScore)

        let events =
            allKinds
            |> List.map (fun k ->
                { PassName       = passName
                  PassVersion    = version
                  SsKey          = k.SsKey
                  TransformKind  = Touched
                  Classification = DataIntent })

        Lineage.ofValueAndEvents events { Value = result; Entries = [entry] }

    let registered (topology: TopologicalOrder option) : RegisteredTransform<Catalog, SchemaComplexity> =
        let topoDefault = topology |> Option.defaultValue TopologicalOrder.empty
        { Name         = passName
          Domain       = CrossCutting
          StageBinding = Pass
          Sites =
            [ { SiteName       = "complexityMetrics"
                Classification = DataIntent
                Rationale      = "Composite schema complexity scoring over FK graph + IR attribute statistics. All inputs are catalog/topology-derived; no operator opinion." } ]
          Run    = fun c -> run c topoDefault
          Status = Active }
