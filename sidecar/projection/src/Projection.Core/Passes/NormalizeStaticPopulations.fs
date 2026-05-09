namespace Projection.Core.Passes

open Projection.Core

/// The pure F# extraction of `EntitySeedDeterminizer`'s sort half (see
/// ADMIRE.md 2026-05-06). Walks the catalog, finds every kind carrying
/// the `Static` modality, and reorders its `populations` list
/// deterministically — primary-key first (the row's `Identifier`
/// `SsKey`, derived at the boundary from the source's PK columns) with
/// the row's `Values` map as tiebreaker (walked in alphabetical
/// attribute-name order so the sort is total).
///
/// The type-aware cell comparison from V1 (numeric coercion, `DateTime`
/// dispatch, `byte[]` length-then-content, etc.) lives at the boundary —
/// the Catalog Reader coerces V1's `object[]` cells to canonical
/// invariant-culture strings before the row reaches this pass. By the
/// time this pass runs every cell is already a comparable string;
/// ordinal string comparison is the only sort discipline this pass needs.
[<RequireQualifiedAccess>]
module NormalizeStaticPopulations =

    /// Pass version. Bump when the row-comparator semantics change.
    [<Literal>]
    let version : int = 1

    [<Literal>]
    let private passName : string = "normalizeStaticPopulations"

    /// The row's sort key. Identifier is primary; the values map walked
    /// in alphabetical attribute-name order (which is what `Map.toList`
    /// produces) is the total tiebreaker. Two rows that share the same
    /// `Identifier` and the same `Values` are indistinguishable; in
    /// practice rows are uniquely identified, so the tiebreaker is
    /// defensive.
    let private rowSortKey (row: StaticRow) : SsKey * (Name * string) list =
        (row.Identifier, Map.toList row.Values)

    let private normalizeModality (m: ModalityMark) : ModalityMark =
        match m with
        | Static rows   -> Static (rows |> List.sortBy rowSortKey)
        | TenantScoped  -> TenantScoped
        | SoftDeletable -> SoftDeletable

    let private hasStaticModality (k: Kind) : bool =
        k.Modality |> List.exists (function Static _ -> true | _ -> false)

    let private touchedEvent (key: SsKey) : LineageEvent =
        { PassName      = passName
          PassVersion   = version
          SsKey         = key
          TransformKind = Touched }

    /// Run the pass over the catalog. Kinds without a `Static` modality
    /// pass through structurally unchanged and emit no lineage events;
    /// kinds with a `Static` modality have their populations reordered
    /// in-place (within the kind, within the catalog) and emit one
    /// `Touched` event.
    ///
    /// Identity-preserving: the pass never invents, drops, or re-keys an
    /// `SsKey`. The cardinality of every population is preserved; only
    /// list order changes.
    ///
    /// Chapter-3.6 cross-cutting cleanup: delegates the
    /// catalog-traversal-with-event-collection pattern to the
    /// reified `CatalogTraversal.mapKinds` primitive.
    let run (c: Catalog) : Lineage<Catalog> =
        use _ = Bench.scope "passes.normalizeStaticPopulations"
        c |> CatalogTraversal.mapKinds (fun events k ->
            if hasStaticModality k then
                LineageBuffer.add (touchedEvent k.SsKey) events
                Some { k with Modality = k.Modality |> List.map normalizeModality }
            else
                Some k)
