namespace Twin.Core
// LINT-ALLOW-FILE-MUTATION: the deterministic sector realization mutates sealed function-local row arrays and shuffle buffers in place; assembled once then returned immutably (the SyntheticData posture)

open Projection.Core

/// THE TWIN — the sector realization (F3).
///
/// The crossover's merged pack carries its inputs whole (`Sectors`), so
/// the mint can realize per-environment SUBPOPULATIONS with no
/// configuration: after σ generates the global rows from the merged
/// evidence (volumes, null budgets, envelopes — the extremes), the
/// sector paint partitions each evidenced kind's rows into contiguous
/// slices proportional to each sector's recorded table volume, and
/// redraws every slice's frequency-carrying text columns from THAT
/// sector's own vocabulary — by quota (largest remainder), so every
/// recorded value lands its proportional presence deterministically,
/// never by independent chance draws. A dev-slice row then reads
/// dev-like where the environments diverge, and the union across slices
/// still covers every environment's vocabulary.
///
/// The paint never touches: NULL cells (σ's null landscape and the
/// witness floors own nullness), primary-key / identity / enforced-FK /
/// single-column-unique columns (key spaces and reference validity are
/// σ's and the loader's), non-text columns, columns the sector recorded
/// no vocabulary for, and the empty-string sentinel (the empty floor
/// witness owns empties — a sector vocabulary is stripped of `''`
/// before quota). Faker corrections and scenario pins run AFTER the
/// paint and win, in that order — the same precedence the mint already
/// gives them over σ.
[<RequireQualifiedAccess>]
module SectorPaint =

    let private fnv1a (s: string) : uint64 =
        let mutable h = 14695981039346656037UL
        for ch in s do
            h <- h ^^^ uint64 (uint32 ch)
            h <- h * 1099511628211UL
        h

    /// splitmix64 — the same host-independent generator family σ uses.
    let private step (state: uint64) : uint64 = state + 0x9E3779B97F4A7C15UL

    let private draw (state: uint64) : uint64 =
        let mutable z = state
        z <- (z ^^^ (z >>> 30)) * 0xBF58476D1CE4E5B9UL
        z <- (z ^^^ (z >>> 27)) * 0x94D049BB133111EBUL
        z ^^^ (z >>> 31)

    let private mix (a: uint64) (b: uint64) : uint64 =
        (a ^^^ b) * 0x9E3779B97F4A7C15UL + 0x2545F4914F6CDD1DUL

    /// Largest-remainder allocation of `total` units across int64
    /// weights: quotas sum exactly to `total`; ties break toward the
    /// earlier entry. Deterministic, order-preserving.
    let allocate (total: int) (weights: int64 list) : int list =
        let clamped = weights |> List.map (max 0L)
        let sum = List.sum clamped
        if sum <= 0L || total <= 0 then clamped |> List.map (fun _ -> 0)
        else
            let exact = clamped |> List.map (fun w -> decimal w * decimal total / decimal sum)
            let floors = exact |> List.map (fun e -> int (System.Decimal.Truncate e))
            let extra = total - List.sum floors
            let winners =
                exact
                |> List.mapi (fun i e -> i, e - System.Decimal.Truncate e)
                |> List.sortBy (fun (i, r) -> -r, i)
                |> List.truncate (max 0 extra)
                |> List.map fst
                |> Set.ofList
            floors |> List.mapi (fun i f -> if Set.contains i winners then f + 1 else f)

    let private shuffleInPlace (seed: uint64) (items: string[]) : unit =
        let mutable state = seed
        for i = items.Length - 1 downto 1 do
            state <- step state
            let j = int (draw state % uint64 (i + 1))
            let tmp = items.[i]
            items.[i] <- items.[j]
            items.[j] <- tmp

    let private hasSingleColumnUnique (kind: Kind) (attrKey: SsKey) : bool =
        kind.Indexes
        |> List.exists (fun i ->
            (match i.Uniqueness with
             | IndexUniqueness.Unique | IndexUniqueness.PrimaryKey -> true
             | IndexUniqueness.NotUnique -> false)
            && (match i.Columns with
                | [ only ] -> only.Attribute = attrKey
                | _ -> false))

    let private isEnforcedFkSource (kind: Kind) (attrKey: SsKey) : bool =
        kind.References |> List.exists (fun r -> r.SourceAttribute = attrKey)

    /// The realization. `bound` pairs each kind with its coordinate text
    /// (the caller's binding — the same shape the reality probe takes);
    /// `sectors` is the merged pack's labeled inputs. Identity when the
    /// sector list is empty or a kind carries no sector evidence.
    let realize
        (seed: uint64)
        (bound: (string * Kind) list)
        (sectors: (string * EvidencePack) list)
        (dataset: Map<SsKey, StaticRow list>)
        : Map<SsKey, StaticRow list> =
        if List.isEmpty sectors then dataset
        else
            bound
            |> List.fold
                (fun (acc: Map<SsKey, StaticRow list>) (coordText, kind) ->
                    match Map.tryFind kind.SsKey acc with
                    | None -> acc
                    | Some rows when List.isEmpty rows -> acc
                    | Some rows ->
                        // Slice order per table: the sector that recorded
                        // the EMPTY STRINGS goes LAST. The empty-floor
                        // witness claims the global tail of the non-null
                        // space, so the tail slice is the one whose rows
                        // it overwrites — ordering empties-last lands
                        // those plants in the sector whose reality they
                        // are, and keeps every other sector's vocabulary
                        // intact by construction. Ties break by label.
                        let emptiesOf (p: EvidencePack) : int64 =
                            p.Tables
                            |> List.filter (fun t ->
                                System.String.Equals(t.Table, coordText, System.StringComparison.OrdinalIgnoreCase))
                            |> List.sumBy (fun t ->
                                t.Columns
                                |> List.sumBy (fun c ->
                                    match c.Text with
                                    | Some ts -> ts.EmptyCount
                                    | None -> 0L))
                        let sectorTables =
                            sectors
                            |> List.sortBy (fun (label, p) -> emptiesOf p, label.ToLowerInvariant())
                            |> List.map (fun (label, p) ->
                                label,
                                p.Tables
                                |> List.tryFind (fun t ->
                                    System.String.Equals(t.Table, coordText, System.StringComparison.OrdinalIgnoreCase)))
                        let weights =
                            sectorTables
                            |> List.map (fun (_, t) -> match t with Some te -> te.RowCount | None -> 0L)
                        if weights |> List.forall (fun w -> w <= 0L) then acc
                        else
                            let rowsArr = List.toArray rows
                            let quotas = allocate rowsArr.Length weights
                            let kindSeed = mix seed (fnv1a (coordText.ToLowerInvariant()))
                            let paintable =
                                kind.Attributes
                                |> List.filter (fun a ->
                                    a.Type = PrimitiveType.Text
                                    && not a.IsPrimaryKey
                                    && not a.IsIdentity
                                    && not (isEnforcedFkSource kind a.SsKey)
                                    && not (hasSingleColumnUnique kind a.SsKey))
                            let mutable start = 0
                            for (sliceIndex, ((label, tableEvidence), quota)) in List.indexed (List.zip sectorTables quotas) do
                                (match tableEvidence with
                                 | None -> ()
                                 | Some te when quota <= 0 -> ignore te
                                 | Some te ->
                                     let sliceSeed = mix kindSeed (mix (uint64 sliceIndex) (fnv1a (label.ToLowerInvariant())))
                                     for attr in paintable do
                                         let colName = ColumnRealization.columnNameText attr.Column
                                         let vocabulary =
                                             te.Columns
                                             |> List.tryFind (fun c ->
                                                 System.String.Equals(c.Column, colName, System.StringComparison.OrdinalIgnoreCase))
                                             |> Option.map (fun c ->
                                                 // The empty floor owns `''` — never plant
                                                 // the sentinel through the paint.
                                                 c.Frequencies |> List.filter (fun (v, _) -> v <> ""))
                                             |> Option.defaultValue []
                                         if not (List.isEmpty vocabulary) then
                                             let cellIndexes =
                                                 [ for i in start .. start + quota - 1 do
                                                     match Map.tryFind attr.Name rowsArr.[i].Values with
                                                     | Some (Some _) -> yield i
                                                     | _ -> () ]
                                             let nonNull = List.length cellIndexes
                                             if nonNull > 0 then
                                                 let valueQuotas = allocate nonNull (vocabulary |> List.map snd)
                                                 let values =
                                                     List.zip vocabulary valueQuotas
                                                     |> List.collect (fun ((v, _), q) -> List.replicate q v)
                                                     |> List.toArray
                                                 shuffleInPlace (mix sliceSeed (fnv1a (colName.ToLowerInvariant()))) values
                                                 cellIndexes
                                                 |> List.iteri (fun k i ->
                                                     let row = rowsArr.[i]
                                                     rowsArr.[i] <- { row with Values = Map.add attr.Name (Some values.[k]) row.Values }))
                                start <- start + quota
                            Map.add kind.SsKey (List.ofArray rowsArr) acc)
                dataset
