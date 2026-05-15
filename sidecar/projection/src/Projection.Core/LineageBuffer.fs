namespace Projection.Core

// LINT-ALLOW-FILE-MUTATION: Reified pass-driver event accumulator.
//   The mutation lives EXCLUSIVELY in this module's implementation;
//   consumers see a typed opaque `Buffer`. Mutation lifetime is
//   scoped to one "build" call (`create` → `add*` → `toList`); the
//   `private` constructor prevents external aliasing. Per the FP
//   strict-mode discipline (`DECISIONS 2026-05-09`), this is the
//   canonical reified-mutation pattern: ResizeArray's mutation
//   becomes invisible from the type level outward.

open System.Collections.Generic

/// Reified pass-driver event accumulator. Replaces direct
/// `ResizeArray<LineageEvent>` usage in `NamingMorphism`,
/// `NormalizeStaticPopulations`, `SymmetricClosure`, and any
/// future pass driver that accumulates events. The opaque
/// `private` constructor enforces that consumers cannot inspect
/// or mutate the underlying ResizeArray; only the module's API
/// touches it.
///
/// Big-O: `add` is O(1) amortized (ResizeArray); `toList` is
/// O(N) one-pass copy. The reified accumulator preserves the
/// asymptotic shape of the prior direct-ResizeArray pattern.
[<RequireQualifiedAccess>]
module LineageBuffer =

    type Buffer = private Buffer of List<LineageEvent>

    /// Build a fresh empty buffer. Each `create` call yields an
    /// independent buffer — there is no module-level state.
    let create () : Buffer = Buffer (List<LineageEvent>())

    /// Append one event. Ordered: events surface in `toList` in
    /// insertion order. O(1) amortized.
    let add (event: LineageEvent) (Buffer b) : unit = b.Add(event)

    /// Append a sequence of events. Same insertion-order semantic.
    let addMany (events: LineageEvent seq) (Buffer b) : unit =
        b.AddRange(events)

    /// Project the buffer to an immutable `LineageEvent list`. The
    /// buffer remains usable; consumers typically project once at
    /// the end of a build call. O(N) one-pass copy.
    let toList (Buffer b) : LineageEvent list = List.ofSeq b

    /// True when no events have been added.
    let isEmpty (Buffer b) : bool = b.Count = 0

    /// Number of events added so far.
    let count (Buffer b) : int = b.Count


/// Catalog-traversal primitive — chapter-3.6 cross-cutting cleanup.
/// Visit every kind in the catalog, deciding keep-or-drop per kind
/// and accumulating lineage events into a typed buffer. Module
/// structure is preserved (kinds stay in their owning module). The
/// primitive captures the recurring pass-driver pattern:
///
///     "for each kind: maybe transform; maybe drop; emit events."
///
/// Two-consumer threshold (per `DECISIONS 2026-05-13`): satisfied by
/// `VisibilityMask.run` (drops kinds matching predicates) and
/// `NormalizeStaticPopulations.run` (transforms in-place for Static
/// kinds, never drops). Future filtering / mapping passes inherit
/// the primitive without each re-implementing the
/// LineageBuffer-create / map-modules / map-kinds / project-buffer
/// boilerplate.
///
/// Out of scope (intentional):
///   - `CanonicalizeIdentity` reorders kinds within modules and
///     modules within catalog — different shape; not abstracted.
///   - `NamingMorphism` recurses into attributes / references —
///     different shape; not abstracted.
[<RequireQualifiedAccess>]
module CatalogTraversal =

    /// Visit every kind in the catalog. The visitor returns
    /// `Some k'` to keep (transformed or unchanged), `None` to drop.
    /// Lineage events are accumulated via the supplied buffer.
    /// Big-O: O(N) where N is the total kind count; preserves the
    /// asymptotic shape of the hand-rolled `c.Modules |> List.map
    /// (fun m -> { m with Kinds = ... })` pattern.
    let mapKinds
        (visit: LineageBuffer.Buffer -> Kind -> Kind option)
        (c: Catalog)
        : Lineage<Catalog> =
        let events = LineageBuffer.create ()
        let modules' =
            c.Modules
            |> List.map (fun m ->
                { m with Kinds = m.Kinds |> List.choose (visit events) })
        Lineage.ofValueAndEvents (LineageBuffer.toList events) { c with Modules = modules' }
