namespace Projection.Core

/// The kind of transformation a lineage event records. The set is small
/// and additive — extend rather than reshape when new pass categories
/// appear, so historical lineage trails stay readable.
type TransformKind =
    /// The pass observed the node but introduced no change. Useful as a
    /// witness that a pass ran ("we looked at this and decided nothing").
    | Touched
    /// The pass changed the node's presentation name. Identity is
    /// untouched (A3, A4, A15).
    | Renamed
    /// The pass introduced a new node with a Derived SsKey (A5). The
    /// derivation reason lives in the SsKey itself; this tag merely
    /// flags the transform's category.
    | Created
    /// The pass masked (withheld) a node from the surface. The `reason`
    /// names the predicate (or rule) that fired. This is the convention
    /// for filtering passes: when a node is removed, the lineage event
    /// records *which* rule fired, so a downstream reader can answer
    /// "why is this kind missing?" by reading the trail.
    | Removed of reason: string
    /// The pass attached or rewrote metadata (modality marks, type
    /// correspondences). The detail string carries human-readable context;
    /// it is not consumed structurally.
    | Annotated of detail: string


/// One step in the provenance chain. Per A23, every event carries a
/// `PassVersion` so functionally different versions of the same pass
/// produce distinguishable lineage and replay determinism is preserved
/// across pipeline evolution.
type LineageEvent = {
    PassName      : string
    PassVersion   : int
    SsKey         : SsKey
    TransformKind : TransformKind
}


/// Writer-monadic carrier for any pass output. Per A25, every IR
/// transformation in the pipeline runs inside `Lineage<_>`; lineage is
/// constitutive, not opt-in. Per A26, lineage is metadata travelling
/// alongside structure — it does not participate in structural equality.
type Lineage<'a> = {
    Value : 'a
    Trail : LineageEvent list
}


/// Construction and composition for `Lineage<_>`. The `bind` operator
/// concatenates trails chronologically per A24: under `f >>= g` the
/// resulting trail is `f.Trail ++ g.Trail` — earliest-first. All passes
/// and all readers rely on this order; reversed-trail bugs are subtle and
/// expensive, so the convention is encoded in code, in `AXIOMS.md`, and
/// in the test suite.
[<RequireQualifiedAccess>]
module Lineage =

    /// Wrap a value with an empty trail. The unit of the writer monad.
    let ofValue (value: 'a) : Lineage<'a> = { Value = value; Trail = [] }

    /// Wrap a value with a single event. Convenience for passes whose
    /// transformation is described by exactly one event.
    let ofValueWith (event: LineageEvent) (value: 'a) : Lineage<'a> =
        { Value = value; Trail = [event] }

    /// Append a single event without changing the value. Useful when a
    /// pass needs to record an observation about a node it returns
    /// unchanged (e.g., `Touched`).
    let tell (event: LineageEvent) (m: Lineage<'a>) : Lineage<'a> =
        { m with Trail = m.Trail @ [event] }

    /// Append several events without changing the value.
    let tellMany (events: LineageEvent list) (m: Lineage<'a>) : Lineage<'a> =
        { m with Trail = m.Trail @ events }

    /// Functor map — preserves the trail untouched, transforms the value.
    let map (f: 'a -> 'b) (m: Lineage<'a>) : Lineage<'b> =
        { Value = f m.Value; Trail = m.Trail }

    /// Monadic bind. A24: `bind f m` produces a trail `m.Trail ++ (f
    /// m.Value).Trail` — earliest-first, chronological. The `@` operator
    /// (list concat) is associative, so the writer monad's laws hold.
    let bind (f: 'a -> Lineage<'b>) (m: Lineage<'a>) : Lineage<'b> =
        // A24: chronological — m.Trail first, then the new pass's trail.
        let next = f m.Value
        { Value = next.Value; Trail = m.Trail @ next.Trail }


/// Infix operators for `Lineage<_>`. Open this module at call sites that
/// benefit from `>>=` (the algebra reads more like the formal system).
module LineageOperators =

    /// Bind: `m >>= f` is `Lineage.bind f m`.
    let inline (>>=) (m: Lineage<'a>) (f: 'a -> Lineage<'b>) : Lineage<'b> =
        Lineage.bind f m

    /// Map: `f <!> m` is `Lineage.map f m`.
    let inline (<!>) (f: 'a -> 'b) (m: Lineage<'a>) : Lineage<'b> =
        Lineage.map f m
