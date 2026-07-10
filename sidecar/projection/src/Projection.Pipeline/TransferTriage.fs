namespace Projection.Pipeline

open Projection.Core

/// THE TRIAGE LAYER (2026-07-10, the manifest program, slice 1 —
/// THE_TRANSFER_MANIFEST.md §3): classify each relational component of a
/// transfer (`TransferImpact.Segment`, the weakly-connected FK unit) so the
/// operator's attention is spent only where uncertainty lives. The certain
/// folds to one line; the open/coupled foregrounds first.
///
/// Pure and total — presentation-only. Nothing here touches the write path;
/// the classifier consumes signal sets the face derives from values the board
/// already holds (escapes, red verdicts, static-lookup divergences, the
/// destructive/creative act kinds, the static-lookup set).
///
/// The single safety invariant: classification FAILS TOWARD FOREGROUNDING.
/// The only dangerous mistake is showing less than the truth — an `Open`
/// unit mis-classed `Settled` — so any force-OPEN signal makes the unit
/// `Open*`, never `Settled*`. Precedence: OpenEscaping > OpenDestructive >
/// Settled*.
[<RequireQualifiedAccess>]
module TransferTriage =

    [<RequireQualifiedAccess>]
    type TriageClass =
        /// Every member is a static-lookup kind and no divergence fired —
        /// the datasets are verified identical; no rows move. The clearest
        /// case the manifest has: one blessable line.
        | SettledStatic
        /// A self-contained unit whose act set is entirely safe — matched
        /// to the target's own rows or carrying no delta; no wipe, no
        /// insert, no delete, no unresolved escape (the §10-K restriction:
        /// any destructive or creative act forces OPEN).
        | SettledClosed
        /// Every table context carries zero adds, deletes, and changes —
        /// nothing happens to this unit.
        | SettledNoop
        /// A member sources an unresolved escaping reference, carries a red
        /// relational-role verdict, or a static-lookup divergence — the hard
        /// case; ranks first and opens first.
        | OpenEscaping
        /// The unit carries a destructive or creative act (a wipe, rows
        /// inserted or deleted) — reviewable, never folded behind a
        /// one-gesture roll-up.
        | OpenDestructive

    /// One reviewable unit of the transfer: the relational component with its
    /// triage class and coupling weight (the ranking key).
    type TransferUnit =
        { Segment        : TransferImpact.Segment
          Triage         : TriageClass
          CouplingWeight : int }

    /// The drift-keyword test over a relational-role verdict string — the
    /// SAME predicate the impact artifact's confirmation panel applies, so
    /// the triage and the panel cannot disagree on what reads as drift.
    let isDriftVerdict (v: string) : bool =
        [ "drift"; "diverge"; "extra"; "missing"; "⚠" ] |> List.exists v.Contains

    let private membersOf (s: TransferImpact.Segment) : Set<SsKey> = Set.ofList s.Members

    let private intersects (members: Set<SsKey>) (signal: Set<SsKey>) : bool =
        not (Set.isEmpty (Set.intersect members signal))

    /// Classify one segment. Total; fails toward foregrounding: any force-OPEN
    /// signal intersecting the segment's members yields an `Open*` class.
    ///   - `escapes`     — kinds sourcing an unresolved escaping FK
    ///   - `redVerdicts` — kinds whose relational-role verdict reads as drift
    ///   - `divergences` — kinds with a non-empty static-lookup divergence
    ///   - `destructive` — kinds carrying any destructive/creative act
    ///                     (wiped, or rows added/deleted outside a reconcile)
    ///   - `staticKinds` — the declared static-lookup kinds
    let classify
        (escapes: Set<SsKey>)
        (redVerdicts: Set<SsKey>)
        (divergences: Set<SsKey>)
        (destructive: Set<SsKey>)
        (staticKinds: Set<SsKey>)
        (s: TransferImpact.Segment)
        : TriageClass =
        let members = membersOf s
        if intersects members escapes
           || intersects members redVerdicts
           || intersects members divergences then TriageClass.OpenEscaping
        elif intersects members destructive then TriageClass.OpenDestructive
        elif not (Set.isEmpty members) && Set.isSubset members staticKinds then TriageClass.SettledStatic
        elif s.Context |> List.forall (fun c -> c.Added = 0 && c.Deleted = 0 && c.Changed = 0) then TriageClass.SettledNoop
        else TriageClass.SettledClosed

    /// The fixed foregrounding penalty an escape or red verdict adds — the
    /// scariest coupled component ranks first even when its row churn is
    /// small (a 3-row unit with an unresolved escape outranks a 500-row
    /// clean reload).
    [<Literal>]
    let ForegroundPenalty = 1000

    /// Σ over the segment's contexts of (Added + Deleted + Changed), plus the
    /// foregrounding penalty when the unit carries an escape or red verdict.
    let couplingWeight (escapes: Set<SsKey>) (redVerdicts: Set<SsKey>) (s: TransferImpact.Segment) : int =
        let churn = s.Context |> List.sumBy (fun c -> c.Added + c.Deleted + c.Changed)
        let members = membersOf s
        churn + (if intersects members escapes || intersects members redVerdicts then ForegroundPenalty else 0)

    /// Open units before settled; within a band, CouplingWeight descending;
    /// tiebreak by the segment's FULL member-key list (two distinct segments
    /// always differ on it — the segmentation partitions kinds), so the order
    /// is total and pretty and JSON lenses agree under any input permutation.
    let rank (units: TransferUnit list) : TransferUnit list =
        let bandOf (u: TransferUnit) =
            match u.Triage with
            | TriageClass.OpenEscaping    -> 0
            | TriageClass.OpenDestructive -> 1
            | TriageClass.SettledClosed   -> 2
            | TriageClass.SettledStatic   -> 3
            | TriageClass.SettledNoop     -> 4
        units
        |> List.sortBy (fun u ->
            (bandOf u,
             -u.CouplingWeight,
             u.Segment.Members |> List.map SsKey.rootOriginal |> String.concat "|"))

    /// Whether a class is settled (folds to one line) — the render's fold test.
    let isSettled (t: TriageClass) : bool =
        match t with
        | TriageClass.SettledStatic | TriageClass.SettledClosed | TriageClass.SettledNoop -> true
        | TriageClass.OpenEscaping | TriageClass.OpenDestructive -> false

    /// The stable token each class renders under in the JSON twin.
    let token (t: TriageClass) : string =
        match t with
        | TriageClass.SettledStatic   -> "settled-static"
        | TriageClass.SettledClosed   -> "settled-closed"
        | TriageClass.SettledNoop     -> "settled-noop"
        | TriageClass.OpenEscaping    -> "open-escaping"
        | TriageClass.OpenDestructive -> "open-destructive"

    /// Classify + weight + rank a whole segmentation in one call — the shape
    /// the face consumes.
    let unitsOf
        (escapes: Set<SsKey>)
        (redVerdicts: Set<SsKey>)
        (divergences: Set<SsKey>)
        (destructive: Set<SsKey>)
        (staticKinds: Set<SsKey>)
        (segments: TransferImpact.Segment list)
        : TransferUnit list =
        segments
        |> List.map (fun s ->
            { Segment        = s
              Triage         = classify escapes redVerdicts divergences destructive staticKinds s
              CouplingWeight = couplingWeight escapes redVerdicts s })
        |> rank
