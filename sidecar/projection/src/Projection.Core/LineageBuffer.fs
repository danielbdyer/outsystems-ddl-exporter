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
/// O(N) one-pass copy. Same as the legacy direct-ResizeArray usage.
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
