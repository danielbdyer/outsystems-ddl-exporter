namespace Projection.Core.Passes

open Projection.Core

/// The physical-claim annotation pass (the data-sink chapter, S13 — the
/// lineage half of the claims machinery, landing at its real execution
/// site: the sink-backed model read). Given the journal-assembled
/// adjudications for the witnessed edition a read is parsing, annotate each
/// kind whose physical table carries a NON-TRIVIAL decision — an adoption
/// over outranked rivals (the superseded editions and tombstones ride the
/// trail), a contest, a tombstone-only claim — with the typed
/// `AnnotationDetail.PhysicalClaimDecision`. The trivial sole adoption (no
/// rivals — the overwhelming majority) annotates nothing, so the healthy
/// estate's trail stays quiet. Identity-preserving by construction
/// (`CatalogTraversal.mapKindsTotal` — an annotation never drops a kind).
[<RequireQualifiedAccess>]
module PhysicalClaimPass =

    /// Pass version. Bump when the annotation rules change.
    [<Literal>]
    let version : int = 1

    [<Literal>]
    let private passName : string = "physicalClaims"

    /// The adjudication is algorithm-internal over journal evidence — no
    /// operator opinion at the per-table level (a genuine contest REFUSES
    /// to pick, which is exactly why it is not operator intent).
    let private classification : Classification = DataIntent

    /// Does this outcome carry information worth a trail entry?
    let private nonTrivial (outcome: PhysicalClaimRules.PhysicalClaimOutcome) : bool =
        match outcome with
        | PhysicalClaimRules.PhysicalClaimOutcome.Adopted (_, []) -> false
        | PhysicalClaimRules.PhysicalClaimOutcome.Adopted _
        | PhysicalClaimRules.PhysicalClaimOutcome.Contested _
        | PhysicalClaimRules.PhysicalClaimOutcome.TombstoneOnly _ -> true
        | PhysicalClaimRules.PhysicalClaimOutcome.Unclaimed -> false

    let private run
        (outcomes: (PhysicalClaimRules.ClaimSet * PhysicalClaimRules.PhysicalClaimOutcome) list)
        (c: Catalog)
        : Lineage<Catalog> =
        use _ = Bench.scope "passes.physicalClaims"
        let byTable =
            outcomes
            |> List.filter (fun (_, o) -> nonTrivial o)
            |> List.map (fun (set, o) ->
                (set.Schema.ToUpperInvariant(), set.Table.ToUpperInvariant()), (set, o))
            |> Map.ofList
        c |> CatalogTraversal.mapKindsTotal (fun events k ->
            let key = (TableId.schemaText k.Physical).ToUpperInvariant(), (TableId.tableText k.Physical).ToUpperInvariant()
            match Map.tryFind key byTable with
            | Some (set, outcome) ->
                let table = System.String.Concat(set.Schema, ".", set.Table) // LINT-ALLOW: terminal annotation-subject composition at the lineage boundary; the typed ClaimSet IS the structure
                LineageBuffer.add
                    (LineageEvent.forPass passName version classification k.SsKey
                        (Annotated (PhysicalClaimDecision (table, outcome))))
                    events
                k
            | None -> k)

    /// S13 factory — the outcomes are the sink-backed read's
    /// journal-assembled adjudications. Registered in `chainSteps` with `[]`
    /// (the config-invariant metadata projection; the chain default
    /// annotates nothing — byte-identical); the sink-backed model read
    /// executes it with the real outcomes.
    let registered
        (outcomes: (PhysicalClaimRules.ClaimSet * PhysicalClaimRules.PhysicalClaimOutcome) list)
        : RegisteredTransform<Catalog, Catalog> =
        { Name = passName
          Domain = Identity
          StageBinding = Pass
          Sites =
            [ { SiteName = "annotate"
                Classification = classification
                Rationale = "Annotate each kind whose physical table carries a non-trivial ownership decision (adoption over outranked rivals / contested / tombstone-only) with the typed PhysicalClaimDecision as a sink-backed read parses the witnessed edition — the lineage half of the claims machinery; the trivial sole adoption annotates nothing, so the healthy estate's trail stays quiet." } ]
          Run = fun c -> run outcomes c |> Lineage.map Diagnostics.ofValue
          Status = Active }
