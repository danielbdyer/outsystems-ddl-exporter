namespace Projection.Core.Passes

open Projection.Core

/// The visibility-mask pass is the first filtering pass. Given a `Mask`
/// of typed predicates, it removes from the catalog every kind that
/// matches any predicate, and emits one `Removed` lineage event per
/// removed kind carrying the typed `RemovalReason` that fired (A14,
/// A23, A25).
///
/// The convention this pass establishes: **filtering passes always name
/// the predicate that caused the removal — structurally, via the typed
/// `RemovalReason` payload of `TransformKind.Removed`.** Any future
/// filtering pass (selection, modality, naming-based withholding)
/// follows the same shape: it adds variants to `RemovalReason` if its
/// rule shape isn't already covered.
///
/// Chapter-3.6 slice-α (`CHAPTER_3_6_OPEN.md`) widened `Removed` from
/// `string` to `RemovalReason`; the predicate's previous `Name : string`
/// field collapses into the typed payload it always represented.
/// Strings emerge ONLY at boundary-rendering consumers via
/// `RemovalReason.toDiagnosticString`.
[<RequireQualifiedAccess>]
module VisibilityMask =

    /// Pass version. Bump when the predicate-evaluation rules change.
    [<Literal>]
    let version : int = 1

    [<Literal>]
    let private passName : string = "visibilityMask"

    /// A predicate over kinds. The `Reason` is the typed payload
    /// recorded structurally in the lineage event when the predicate
    /// fires; the `Test` is the predicate's evaluation. Chapter-3.6
    /// slice-α renamed `Name : string` → `Reason : RemovalReason`;
    /// see the module docstring for rationale.
    type Predicate = {
        Reason : RemovalReason
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
    // Predicate constructors. Each builds a typed `RemovalReason` so the
    // lineage payload survives without ever passing through a
    // pre-built name string.
    // -----------------------------------------------------------------------

    /// Hide every kind whose origin equals `origin`. The lineage
    /// payload is `RemovalReason.OriginPredicate origin` — the typed
    /// `Origin` flows through structurally; consumers pattern-match.
    let hideOrigin (origin: Origin) : Predicate =
        { Reason = OriginPredicate origin
          Test = (fun k -> k.Origin = origin) }

    /// Hide every kind whose SsKey is in `keys`. The lineage payload
    /// is the marker variant `RemovalReason.ExplicitKeyList`; the full
    /// key set is intentionally NOT carried in the trail (per-event
    /// payload would otherwise be O(N), making the trail O(N²)).
    let hideKeys (keys: SsKey seq) : Predicate =
        let keySet = Set.ofSeq keys
        { Reason = ExplicitKeyList
          Test = (fun k -> Set.contains k.SsKey keySet) }

    /// Hide every kind whose modality includes the given mark. The
    /// lineage payload is `RemovalReason.ModalityPredicate mark` —
    /// the typed `ModalityMark` flows through structurally.
    let hideModality (mark: ModalityMark) : Predicate =
        { Reason = ModalityPredicate mark
          Test = (fun k -> List.contains mark k.Modality) }

    // -----------------------------------------------------------------------
    // Internals.
    // -----------------------------------------------------------------------

    /// Find the first predicate in the mask that matches the kind, if any.
    let private firstMatch (mask: Mask) (kind: Kind) : Predicate option =
        mask.Hide |> List.tryFind (fun p -> p.Test kind)

    /// Pillar 9 (chapter A.4.7 slice α): visibility-mask hides kinds
    /// per operator-supplied predicate (origin / explicit keys /
    /// modality). The predicate is operator intent on the Selection
    /// axis — operator chose which kinds appear in the catalog. Lands
    /// as registered overlay; skeleton-purity property test (slice θ)
    /// catches any leak.
    let private classification : Classification = OperatorIntent Selection

    let private removedEvent (predicate: Predicate) (key: SsKey) : LineageEvent =
        { PassName       = passName
          PassVersion    = version
          SsKey          = key
          TransformKind  = Removed predicate.Reason
          Classification = classification }

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
    ///
    /// Chapter-3.6 cross-cutting cleanup: delegates the
    /// catalog-traversal-with-event-collection pattern to the
    /// reified `CatalogTraversal.mapKinds` primitive (`LineageBuffer
    /// .fs`). This driver expresses what the pass DECIDES per kind;
    /// the primitive owns HOW the catalog is walked.
    let run (mask: Mask) (c: Catalog) : Lineage<Catalog> =
        use _ = Bench.scope "passes.visibilityMask"
        c |> CatalogTraversal.mapKinds (fun events k ->
            match firstMatch mask k with
            | None -> Some k
            | Some pred ->
                LineageBuffer.add (removedEvent pred k.SsKey) events
                None)
