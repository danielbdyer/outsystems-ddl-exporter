namespace Projection.Pipeline

open Projection.Core

/// The ONE escaping-relationship traversal (2026-07-06, the final-pass
/// consolidation): every FK edge from an in-subset kind to an out-of-subset,
/// un-reconciled target. Two consumers project it — the engine backstop
/// (`Transfer.subsetEscapeGate`, the compact leg-neutral refusal) and the
/// peer face's rich detector (`PeerTransfer.escapingFks`, which enriches
/// each edge with module names, nullability softening, and reconcile
/// candidates). One traversal means the board can never show green while
/// the engine refuses (or vice versa) — the predicates cannot drift.
[<RequireQualifiedAccess>]
module TransferSubset =

    /// `(owning kind, the reference, the target kind)` for every escaping
    /// edge. A dangling model edge (target kind absent from the contract) is
    /// the model's own diagnostic, not this traversal's — skipped, as both
    /// prior copies did. Deterministic: sorted by (kind name, reference name).
    let escapingEdges
        (contract: Catalog)
        (loadSet: Set<SsKey>)
        (reconciled: Set<SsKey>)
        : (Kind * Reference * Kind) list =
        Catalog.allKinds contract
        |> List.filter (fun k -> Set.contains k.SsKey loadSet)
        |> List.collect (fun kind ->
            kind.References
            |> List.choose (fun r ->
                if Set.contains r.TargetKind loadSet || Set.contains r.TargetKind reconciled then None
                else
                    Catalog.tryFindKind r.TargetKind contract
                    |> Option.map (fun target -> (kind, r, target))))
        |> List.sortBy (fun (k, r, _) -> Name.value k.Name, Name.value r.Name)

    /// The INBOUND mirror of `escapingEdges` (2026-07-08, the business-intent
    /// program): every FK edge from an OUT-of-payload kind INTO the payload —
    /// the discovered dependents that `owned-child` / `blocked-dependent`
    /// classify, and the rows a replace-wipe of the payload would orphan.
    /// `(dependent source kind, the reference, the in-payload target)`.
    /// Deterministic: sorted by (source name, reference name).
    let dependentEdges
        (contract: Catalog)
        (payload: Set<SsKey>)
        : (Kind * Reference * Kind) list =
        Catalog.allKinds contract
        |> List.filter (fun k -> not (Set.contains k.SsKey payload))
        |> List.collect (fun source ->
            source.References
            |> List.choose (fun r ->
                if Set.contains r.TargetKind payload then
                    Catalog.tryFindKind r.TargetKind contract
                    |> Option.map (fun target -> (source, r, target))
                else None))
        |> List.sortBy (fun (k, r, _) -> Name.value k.Name, Name.value r.Name)
