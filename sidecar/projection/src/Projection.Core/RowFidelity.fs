namespace Projection.Core

open System

// ---------------------------------------------------------------------------
// Row fidelity — the pure comparator core (T17, the fidelity chapter;
// DECISIONS 2026-07-15 "The fidelity chapter opens"; wave B2).
//
// Two renditions of one estate stream their rows in primary-key order; the
// bases align the physical column names to the model's logical shape
// (`RowBasis.rename` — a header-only operation, the quanta untouched); and
// the comparator names every difference by the row's key:
//   - a key present on the source side alone is MISSING IN TARGET;
//   - a key present on the target side alone is EXTRA IN TARGET;
//   - one key with differing canonical bytes names its differing COLUMNS.
// The aggregate rung (L1) is `RowDigestFold` — order-independent, already
// law-tested; this module is the drill-down rung (L2) that names rows.
//
// The pure list-form comparator (`compareOrdered`) is the LAW SURFACE: the
// pipeline's streaming lockstep must equal it on materialized streams (the
// docker witness pins that equality live).
// ---------------------------------------------------------------------------

/// One named row difference — the drill-down grain. The key renders as the
/// row's primary-key value (the identity an operator can SELECT by).
[<RequireQualifiedAccess>]
type RowDifference =
    | MissingInTarget of key: string
    | ExtraInTarget of key: string
    | CellsDiffer of key: string * columns: Name list

/// One kind's row-fidelity verdict: both sides' aggregate digests (the L1
/// rung — count + order-independent digest), the named differences (capped;
/// the total exact), and the naming basis. `NamingSkipped` carries the named
/// reason when the drill-down could not run (a non-integer or composite
/// key) — the digest verdict still stands; the downgrade is never silent.
type KindRowVerdict =
    {
        Kind            : SsKey
        KindName        : string
        Source          : RowDigestFold.TableDigest
        Target          : RowDigestFold.TableDigest
        /// The primary-key column the differences are named by (the
        /// LOGICAL name — both sides align to it).
        KeyColumn       : string
        Differences     : RowDifference list
        DifferenceTotal : int64
        NamingSkipped   : string option
    }

[<RequireQualifiedAccess>]
module KindRowVerdict =

    /// The kind is byte-identical across the gap: equal counts, equal
    /// aggregate digests, and no named differences.
    let agrees (v: KindRowVerdict) : bool =
        v.Source = v.Target && v.DifferenceTotal = 0L

[<RequireQualifiedAccess>]
module RowFidelity =

    /// How a kind's rows can be NAMED during lockstep. The server orders
    /// both streams by the primary key; the client-side merge needs a key
    /// comparison that agrees with that order. An integer key's order is
    /// reproducible (`ORDER BY` on int/bigint = numeric order); text, GUID,
    /// date, and composite keys order by server semantics the client cannot
    /// faithfully reproduce — those kinds keep their aggregate verdict and
    /// carry the named reason instead of a wrong merge.
    [<RequireQualifiedAccess>]
    type KeyPlan =
        | Int64Key of column: Name
        | Unnameable of reason: string

    /// Decide a kind's key plan from its declared shape — total.
    let keyPlanOf (kind: Kind) : KeyPlan =
        match Kind.primaryKey kind with
        | [ pk ] ->
            match pk.Type with
            | Integer -> KeyPlan.Int64Key pk.Name
            | other ->
                KeyPlan.Unnameable
                    (String.Concat  // LINT-ALLOW: terminal reason composition over a typed type token; the reason is operator-facing free text
                        ("the key '", Name.value pk.Name, "' is ", string other,
                         " — row naming needs an integer key whose order the client can reproduce"))
        | [] -> KeyPlan.Unnameable "the kind declares no primary key — rows have no name to compare by"
        | _ -> KeyPlan.Unnameable "the primary key is composite — row naming needs a single integer key"

    /// The columns whose cells differ between two aligned rows, in the
    /// shared name-sorted order, capped (the operator reads the first
    /// culprits; the row is already named). Bases MUST carry equal name
    /// sets — the alignment package guarantees it (one model, two
    /// renditions, one rename map).
    let differingColumns
        (cap: int)
        (leftBasis: RowBasis)
        (rightBasis: RowBasis)
        (left: RowQuantum)
        (right: RowQuantum)
        : Name list =
        let names = RowBasis.names leftBasis
        let order = RowBasis.nameSortedOrder leftBasis
        let differing =
            order
            |> Array.choose (fun ordinal ->
                let name = names.[ordinal]
                let rightOrdinal = RowBasis.tryOrdinal name rightBasis
                match rightOrdinal with
                | Some ro when right.Cells.[ro] = left.Cells.[ordinal] -> None
                | Some _ -> Some name
                | None -> Some name)
        differing |> Array.truncate cap |> Array.toList

    /// Millisecond-canonicalize `DateTime`-typed cells before hashing (the
    /// `DateTimeTickPrecisionTolerated` erasure, in force on the row-fidelity
    /// path): the canonical cell form is `yyyy-MM-dd HH:mm:ss.fffffff` (23 +
    /// 4 sub-millisecond digits); truncating to 23 characters absorbs the
    /// `datetime` (1/300 s) vs `datetime2` (100 ns) tick residue while a
    /// genuine at-or-above-millisecond difference still fails. Identity when
    /// `ordinals` is empty or no cell exceeds the millisecond form — the
    /// quantum is shared, never mutated.
    let canonicalizeDateTimeCells (ordinals: int[]) (q: RowQuantum) : RowQuantum =
        let millisecondFormLength = 23
        let needsWork =
            ordinals
            |> Array.exists (fun i -> i < q.Cells.Length && q.Cells.[i].Length > millisecondFormLength)
        if not needsWork then q
        else
            let cells = Array.copy q.Cells
            for i in ordinals do
                if i < cells.Length && cells.[i].Length > millisecondFormLength then
                    cells.[i] <- cells.[i].Substring(0, millisecondFormLength)  // LINT-ALLOW: function-local mutation of the freshly copied cell array (copy-on-write); the input quantum is never touched
            { Cells = cells }

    /// Replay a transfer run's key interventions onto one SOURCE row — the
    /// predicted-target transform (T17: `κ (key r)` over the row's own key,
    /// `remapFks r` over its referencing cells). `keyRewrite` is the kind's
    /// own (source → assigned) pair map at the key ordinal; `fkRewrites`
    /// pairs each referencing cell's ordinal with the TARGET kind's pair
    /// map. A value absent from a map rides unchanged (preserved keys are
    /// identity). Identity when nothing rewrites — the quantum is shared,
    /// never mutated.
    let replayQuantum
        (keyRewrite: (int * Map<string, string>) option)
        (fkRewrites: (int * Map<string, string>) list)
        (q: RowQuantum)
        : RowQuantum =
        let rewriteAt (cells: string[] option) (ordinal: int) (map: Map<string, string>) : string[] option =
            let current = match cells with Some c -> c | None -> q.Cells
            if ordinal >= current.Length then cells
            else
                match Map.tryFind current.[ordinal] map with
                | Some assigned when assigned <> current.[ordinal] ->
                    let c = match cells with Some c -> c | None -> Array.copy q.Cells
                    c.[ordinal] <- assigned  // LINT-ALLOW: function-local mutation of the freshly copied cell array (copy-on-write); the input quantum is never touched
                    Some c
                | _ -> cells
        let afterKey =
            match keyRewrite with
            | Some (ordinal, map) -> rewriteAt None ordinal map
            | None -> None
        let afterFks =
            fkRewrites |> List.fold (fun cells (ordinal, map) -> rewriteAt cells ordinal map) afterKey
        match afterFks with
        | Some cells -> { Cells = cells }
        | None -> q

    /// The pure ordered-merge comparator — the law surface. Both lists are
    /// (key, row) ascending by key (the server's `ORDER BY pk` order); the
    /// bases align the two renditions' column names. Returns the named
    /// differences (capped at `sampleCap`, keys in encounter order) and the
    /// EXACT difference total. The L1 folds ride outside — this names rows.
    let compareOrdered
        (sampleCap: int)
        (leftBasis: RowBasis)
        (rightBasis: RowBasis)
        (left: (int64 * RowQuantum) list)
        (right: (int64 * RowQuantum) list)
        : RowDifference list * int64 =
        let keep (diffs: RowDifference list) (total: int64) (d: RowDifference) =
            (if total < int64 sampleCap then d :: diffs else diffs), total + 1L
        let rec go
            (l: (int64 * RowQuantum) list)
            (r: (int64 * RowQuantum) list)
            (diffs: RowDifference list)
            (total: int64)
            : RowDifference list * int64 =
            match l, r with
            | [], [] -> List.rev diffs, total
            | (lk, _) :: lt, [] ->
                let diffs', total' = keep diffs total (RowDifference.MissingInTarget (string lk))
                go lt [] diffs' total'
            | [], (rk, _) :: rt ->
                let diffs', total' = keep diffs total (RowDifference.ExtraInTarget (string rk))
                go [] rt diffs' total'
            | (lk, lq) :: lt, (rk, rq) :: rt ->
                if lk < rk then
                    let diffs', total' = keep diffs total (RowDifference.MissingInTarget (string lk))
                    go lt r diffs' total'
                elif rk < lk then
                    let diffs', total' = keep diffs total (RowDifference.ExtraInTarget (string rk))
                    go l rt diffs' total'
                else
                    let leftHash = RowDigester.hashQuantumBytes leftBasis lq
                    let rightHash = RowDigester.hashQuantumBytes rightBasis rq
                    if leftHash = rightHash then go lt rt diffs total
                    else
                        let columns = differingColumns 4 leftBasis rightBasis lq rq
                        let diffs', total' = keep diffs total (RowDifference.CellsDiffer (string lk, columns))
                        go lt rt diffs' total'
        go left right [] 0L
