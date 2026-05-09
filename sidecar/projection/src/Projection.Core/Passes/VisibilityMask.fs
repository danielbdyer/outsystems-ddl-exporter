namespace Projection.Core.Passes

open Projection.Core

/// The visibility-mask pass is the first filtering pass. Given a `Mask`
/// of named predicates, it removes from the catalog every kind that
/// matches any predicate, and emits one `Removed` lineage event per
/// removed kind naming the predicate that fired (A14, A23, A25).
///
/// The convention this pass establishes: **filtering passes always name
/// the predicate that caused the removal in the lineage event.** The
/// `TransformKind.Removed` payload carries that name. Any future
/// filtering pass (selection, modality, naming-based withholding) follows
/// the same shape.
[<RequireQualifiedAccess>]
module VisibilityMask =

    /// Pass version. Bump when the predicate-evaluation rules change.
    [<Literal>]
    let version : int = 1

    [<Literal>]
    let private passName : string = "visibilityMask"

    /// A named predicate over kinds. The `Name` is recorded in the
    /// lineage event when the predicate fires.
    type Predicate = {
        Name : string
        Test : Kind -> bool
    }

    /// The mask: an ordered list of `Hide` predicates. A kind matching
    /// any predicate is removed; the **first** matching predicate
    /// (in list order) wins for lineage attribution. This makes
    /// attribution deterministic when a kind would match multiple
    /// predicates.
    type Mask = {
        Hide : Predicate list
    }

    /// The identity mask. Hides nothing.
    let empty : Mask = { Hide = [] }

    // -----------------------------------------------------------------------
    // Predicate constructors. Named predicates with stable, descriptive
    // names so lineage is human-readable.
    // -----------------------------------------------------------------------

    /// Hide every kind whose origin equals `origin`.
    let hideOrigin (origin: Origin) : Predicate =
        { Name = sprintf "origin=%A" origin
          Test = (fun k -> k.Origin = origin) }

    /// Hide every kind whose SsKey is in `keys`.
    let hideKeys (keys: SsKey seq) : Predicate =
        let keySet = Set.ofSeq keys
        { Name = "explicit-key-list"
          Test = (fun k -> Set.contains k.SsKey keySet) }

    /// Hide every kind whose modality includes the given mark.
    let hideModality (mark: ModalityMark) : Predicate =
        { Name = sprintf "modality=%A" mark
          Test = (fun k -> List.contains mark k.Modality) }

    // -----------------------------------------------------------------------
    // Internals.
    // -----------------------------------------------------------------------

    /// Find the first predicate in the mask that matches the kind, if any.
    let private firstMatch (mask: Mask) (kind: Kind) : Predicate option =
        mask.Hide |> List.tryFind (fun p -> p.Test kind)

    let private removedEvent (predicate: Predicate) (key: SsKey) : LineageEvent =
        { PassName      = passName
          PassVersion   = version
          SsKey         = key
          TransformKind = Removed predicate.Name }

    // -----------------------------------------------------------------------
    // The pass.
    // -----------------------------------------------------------------------

    /// Run the pass over a catalog. Removed kinds emit one `Removed`
    /// lineage event each; surviving kinds pass through unchanged.
    /// Identity is preserved: no SsKey is invented or rekeyed (A3, A4).
    /// References from a surviving kind to a removed kind are NOT
    /// rewritten by this pass (preserving the catalog's structural
    /// truth); a downstream pass or emitter that cares about dangling
    /// references handles them.
    let run (mask: Mask) (c: Catalog) : Lineage<Catalog> =
        let mutable events : LineageEvent list = []
        let canonModules =
            c.Modules
            |> List.map (fun m ->
                let kept =
                    m.Kinds
                    |> List.choose (fun k ->
                        match firstMatch mask k with
                        | None -> Some k
                        | Some pred ->
                            events <- removedEvent pred k.SsKey :: events
                            None)
                { m with Kinds = kept })
        let masked = { Modules = canonModules }
        // Reverse so events appear in catalog-traversal order rather
        // than reverse-traversal order. A24's chronological-trail
        // discipline applies within bind composition; within a single
        // pass the convention is "events in the order the pass observed
        // its targets."
        Lineage.ofValueAndEvents (List.rev events) masked
