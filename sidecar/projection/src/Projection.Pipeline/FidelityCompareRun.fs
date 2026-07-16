namespace Projection.Pipeline

// LINT-ALLOW-FILE: the row-fidelity compare run (T17, wave B2) — a run module
//   at the SQL boundary: two live connections stream PK-ordered quanta through
//   the pure comparator core; the report renderer and the fidelity.rows.json
//   codec compose operator-facing statements (THE_VOICE register) and
//   structured JSON at a terminal reporting boundary.

open System
open System.Text.Json.Nodes
open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Adapters.Sql

/// The row-fidelity proof record — one value the terminal lines, the JSON
/// artifact, and the exit code project (the one-substrate law).
type RowFidelityReport =
    {
        BeforeLabel : string
        AfterLabel  : string
        ModelBasis  : string
        /// The intervention ledger's display path when the proof replayed
        /// one (`--interventions`); `None` claims strict byte-identity.
        Interventions : string option
        /// The canonical row form's named erasures, in force on every
        /// `--rows` proof (T17/B4b) — declared, never silent.
        TolerancesInForce : string list
        Kinds       : KindRowVerdict list
    }

[<RequireQualifiedAccess>]
module RowFidelityReport =

    /// Every kind byte-identical — the exit-0 predicate.
    let agrees (report: RowFidelityReport) : bool =
        report.Kinds |> List.forall KindRowVerdict.agrees

    /// Source-side rows read across the estate (the proof's breadth).
    let rowsCompared (report: RowFidelityReport) : int64 =
        report.Kinds |> List.sumBy (fun k -> k.Source.Count)

    /// The exact difference total across every kind.
    let differenceTotal (report: RowFidelityReport) : int64 =
        report.Kinds |> List.sumBy (fun k -> k.DifferenceTotal)

    /// The kinds that disagree, largest difference first.
    let disagreeing (report: RowFidelityReport) : KindRowVerdict list =
        report.Kinds
        |> List.filter (fun k -> not (KindRowVerdict.agrees k))
        |> List.sortByDescending (fun k -> k.DifferenceTotal)

[<RequireQualifiedAccess>]
module FidelityCompareRun =

    /// The canonical row form's named erasures, in force on every `--rows`
    /// proof by construction (T17/B4b): the reader renders every boolean as
    /// `true`/`false` and every integer family through int64, and the
    /// comparator millisecond-canonicalizes `DateTime` cells — three closed
    /// erasures the report declares rather than silently assumes.
    let tolerancesInForce : ToleratedDivergence list =
        [ ToleratedDivergence.BooleanCanonicalizationTolerated
          ToleratedDivergence.DateTimeTickPrecisionTolerated
          ToleratedDivergence.IntegerWidthNormalized ]

    // ------------------------------------------------------------------
    // The intervention ledger (T17/B4b): load a transfer journal's
    // (source → assigned) pairs as per-kind replay maps. Keep-first per
    // source key — the journal is at-least-once (a resumed run re-appends
    // its completed chunks), so the fold dedupes; `CaptureJournal.load`
    // already last-write-wins per (kind, chunk).
    // ------------------------------------------------------------------

    /// Resolve the operand to a journal FILE: a file path is itself; a
    /// directory must hold exactly one `transfer-*.ndjson` (a second one is
    /// an ambiguity, refused by name — the operator picks).
    let private resolveJournalFile (path: string) : Result<string> =
        if IO.File.Exists path then Result.success path
        elif IO.Directory.Exists path then
            match IO.Directory.GetFiles(path, "transfer-*.ndjson") |> Array.sort with
            | [| one |] -> Result.success one
            | [||] ->
                Result.failureOf
                    (ValidationError.create "fidelity.rows.journalMissing"
                        (sprintf "no transfer-*.ndjson journal exists under '%s'." path))
            | several ->
                Result.failureOf
                    (ValidationError.create "fidelity.rows.journalAmbiguous"
                        (sprintf "'%s' holds %d transfer journals — name the file itself: %s."
                            path several.Length (String.concat ", " (Array.toList several))))
        else
            Result.failureOf
                (ValidationError.create "fidelity.rows.journalMissing"
                    (sprintf "the interventions path '%s' does not exist." path))

    /// Load one journal's replay maps: kind root (`SsKey.rootOriginal`) →
    /// (source raw → assigned raw), keep-first per source key.
    let loadReplayMaps (path: string) : Result<Map<string, Map<string, string>>> =
        match resolveJournalFile path with
        | Error es -> Result.failure es
        | Ok file ->
            try
                let journal = CaptureJournal.ofFile file
                // Chunk order is part of the fold's meaning (keep-first per
                // source key), so the load's dictionary sorts by (kind,
                // chunk index) before folding — deterministic at-least-once
                // dedupe, earliest chunk wins.
                let records =
                    CaptureJournal.load journal
                    |> Seq.map (fun (KeyValue (_, record)) -> record)
                    |> Seq.sortBy (fun r -> r.Kind, r.ChunkIx)
                    |> Seq.toList
                let foldRecord (maps: Map<string, Map<string, string>>) (record: ChunkRecord) =
                    let existing = maps |> Map.tryFind record.Kind |> Option.defaultValue Map.empty
                    let folded =
                        record.Pairs
                        |> Array.fold
                            (fun (acc: Map<string, string>) (pair: string[]) ->
                                if pair.Length = 2 && not (acc.ContainsKey pair.[0]) then acc.Add(pair.[0], pair.[1])
                                else acc)
                            existing
                    maps.Add(record.Kind, folded)
                Result.success (records |> List.fold foldRecord Map.empty)
            with ex ->
                Result.failureOf
                    (ValidationError.create "fidelity.rows.journalUnreadable"
                        (sprintf "the journal at '%s' did not load: %s" file ex.Message))

    /// Wrap a stream with a pure per-quantum transform (the replay and the
    /// millisecond canonicalization ride the pull, so the lockstep, the
    /// folds, and the keyed compare all see one transformed stream).
    let private mapStream (transform: RowQuantum -> RowQuantum) (pull: AsyncStream<RowQuantum>) : AsyncStream<RowQuantum> =
        fun () ->
            task {
                let! head = pull ()
                return head |> Option.map transform
            }

    // ------------------------------------------------------------------
    // The streaming lockstep — module-level `rec` task functions with the
    // cursor state as parameters (FS3511: no `let rec` inside `task { }`),
    // returning a record (never a tuple `let!`).
    // ------------------------------------------------------------------

    type private LockstepOutcome =
        {
            FoldLeft    : RowDigestFold.State
            FoldRight   : RowDigestFold.State
            Differences : RowDifference list
            Total       : int64
        }

    let private keepDiff
        (cap: int)
        (diffs: RowDifference list)
        (total: int64)
        (d: RowDifference)
        : RowDifference list * int64 =
        (if total < int64 cap then d :: diffs else diffs), total + 1L

    /// The merge over two PK-ordered streams. Every row on each side folds
    /// into that side's aggregate digest exactly once; key-aligned rows
    /// compare by canonical bytes; misaligned keys name their direction.
    /// The pure list-form law (`RowFidelity.compareOrdered`) is the
    /// reference this must equal on materialized streams.
    let rec private lockstep
        (cap: int)
        (leftBasis: RowBasis)
        (rightBasis: RowBasis)
        (keyOfLeft: RowQuantum -> int64)
        (keyOfRight: RowQuantum -> int64)
        (pullLeft: AsyncStream<RowQuantum>)
        (pullRight: AsyncStream<RowQuantum>)
        (leftHead: RowQuantum option)
        (rightHead: RowQuantum option)
        (foldLeft: RowDigestFold.State)
        (foldRight: RowDigestFold.State)
        (diffs: RowDifference list)
        (total: int64)
        : Task<LockstepOutcome> =
        task {
            match leftHead, rightHead with
            | None, None ->
                return { FoldLeft = foldLeft; FoldRight = foldRight; Differences = List.rev diffs; Total = total }
            | Some lq, None ->
                let foldLeft' = RowDigestFold.addQuantum leftBasis foldLeft lq
                let diffs', total' = keepDiff cap diffs total (RowDifference.MissingInTarget (string (keyOfLeft lq)))
                let! nextLeft = pullLeft ()
                return! lockstep cap leftBasis rightBasis keyOfLeft keyOfRight pullLeft pullRight nextLeft None foldLeft' foldRight diffs' total'
            | None, Some rq ->
                let foldRight' = RowDigestFold.addQuantum rightBasis foldRight rq
                let diffs', total' = keepDiff cap diffs total (RowDifference.ExtraInTarget (string (keyOfRight rq)))
                let! nextRight = pullRight ()
                return! lockstep cap leftBasis rightBasis keyOfLeft keyOfRight pullLeft pullRight None nextRight foldLeft foldRight' diffs' total'
            | Some lq, Some rq ->
                let leftKey = keyOfLeft lq
                let rightKey = keyOfRight rq
                if leftKey < rightKey then
                    let foldLeft' = RowDigestFold.addQuantum leftBasis foldLeft lq
                    let diffs', total' = keepDiff cap diffs total (RowDifference.MissingInTarget (string leftKey))
                    let! nextLeft = pullLeft ()
                    return! lockstep cap leftBasis rightBasis keyOfLeft keyOfRight pullLeft pullRight nextLeft rightHead foldLeft' foldRight diffs' total'
                elif rightKey < leftKey then
                    let foldRight' = RowDigestFold.addQuantum rightBasis foldRight rq
                    let diffs', total' = keepDiff cap diffs total (RowDifference.ExtraInTarget (string rightKey))
                    let! nextRight = pullRight ()
                    return! lockstep cap leftBasis rightBasis keyOfLeft keyOfRight pullLeft pullRight leftHead nextRight foldLeft foldRight' diffs' total'
                else
                    let foldLeft' = RowDigestFold.addQuantum leftBasis foldLeft lq
                    let foldRight' = RowDigestFold.addQuantum rightBasis foldRight rq
                    let leftHash = RowDigester.hashQuantumBytes leftBasis lq
                    let rightHash = RowDigester.hashQuantumBytes rightBasis rq
                    let diffs', total' =
                        if leftHash = rightHash then diffs, total
                        else
                            keepDiff cap diffs total
                                (RowDifference.CellsDiffer
                                    (string leftKey, RowFidelity.differingColumns 4 leftBasis rightBasis lq rq))
                    let! nextLeft = pullLeft ()
                    let! nextRight = pullRight ()
                    return! lockstep cap leftBasis rightBasis keyOfLeft keyOfRight pullLeft pullRight nextLeft nextRight foldLeft' foldRight' diffs' total'
        }

    /// Fold one stream into the aggregate digest alone — the unnameable-key
    /// path keeps its L1 verdict; the rows stay unnamed and the reason is
    /// carried on the verdict (named, never silent).
    let rec private foldStream
        (basis: RowBasis)
        (pull: AsyncStream<RowQuantum>)
        (state: RowDigestFold.State)
        : Task<RowDigestFold.State> =
        task {
            let! head = pull ()
            match head with
            | None -> return state
            | Some q -> return! foldStream basis pull (RowDigestFold.addQuantum basis state q)
        }

    // ------------------------------------------------------------------
    // The keyed compare — the own-key-remapped path (T17/B4b). A replayed
    // key rewrite reorders the source stream in assigned-key space, so the
    // ordered lockstep cannot merge; the target side indexes (key → row
    // hash) once, the transformed source probes it, and the leftovers are
    // the extras. Bounded by the remapped kind's row count — the
    // AssignedBySink set is the reconcile/static family by construction.
    // ------------------------------------------------------------------

    let rec private indexTarget
        (basis: RowBasis)
        (keyOf: RowQuantum -> int64)
        (pull: AsyncStream<RowQuantum>)
        (fold: RowDigestFold.State)
        (index: System.Collections.Generic.Dictionary<int64, byte[]>)
        : Task<RowDigestFold.State> =
        task {
            let! head = pull ()
            match head with
            | None -> return fold
            | Some q ->
                let fold' = RowDigestFold.addQuantum basis fold q
                index.[keyOf q] <- RowDigester.hashQuantumBytes basis q
                return! indexTarget basis keyOf pull fold' index
        }

    let rec private probeSource
        (cap: int)
        (basis: RowBasis)
        (keyOf: RowQuantum -> int64)
        (pull: AsyncStream<RowQuantum>)
        (index: System.Collections.Generic.Dictionary<int64, byte[]>)
        (fold: RowDigestFold.State)
        (diffs: RowDifference list)
        (total: int64)
        : Task<LockstepOutcome> =
        task {
            let! head = pull ()
            match head with
            | None ->
                // The leftovers never matched a source row — extras, named
                // ascending for determinism.
                let leftovers = index.Keys |> Seq.sort |> Seq.toList
                let mutable diffsAcc = diffs
                let mutable totalAcc = total
                for key in leftovers do
                    let diffs', total' = keepDiff cap diffsAcc totalAcc (RowDifference.ExtraInTarget (string key))
                    diffsAcc <- diffs'
                    totalAcc <- total'
                return { FoldLeft = fold; FoldRight = RowDigestFold.empty; Differences = List.rev diffsAcc; Total = totalAcc }
            | Some q ->
                let fold' = RowDigestFold.addQuantum basis fold q
                let key = keyOf q
                let diffs', total' =
                    match index.TryGetValue key with
                    | true, targetHash ->
                        index.Remove key |> ignore
                        if targetHash = RowDigester.hashQuantumBytes basis q then diffs, total
                        else keepDiff cap diffs total (RowDifference.CellsDiffer (string key, []))
                    | false, _ ->
                        keepDiff cap diffs total (RowDifference.MissingInTarget (string key))
                return! probeSource cap basis keyOf pull index fold' diffs' total'
        }

    // ------------------------------------------------------------------
    // One kind's comparison — the alignment package applied.
    // ------------------------------------------------------------------

    let private compareKind
        (source: SqlConnection)
        (target: SqlConnection)
        (renamesByKind: Map<SsKey, Map<Name, Name>>)
        (replayMaps: Map<string, Map<string, string>>)
        (sampleCap: int)
        (physicalKind: Kind)
        (logicalKind: Kind)
        : Task<KindRowVerdict> =
        task {
            use _ = Bench.scope "fidelity.rows.kind"
            // The physical stream re-bases onto the model's logical names —
            // a header-only operation (the quanta are untouched); the
            // logical side hashes under its own basis. Equal rows then
            // carry equal canonical bytes across the gap (T17's triangle).
            let renameMap = RenameProjection.forKind logicalKind.SsKey renamesByKind
            let sourceBasis = RowBasis.rename renameMap (Kind.rowBasis physicalKind)
            let targetBasis = Kind.rowBasis logicalKind
            // The millisecond canonicalization (DateTimeTickPrecisionTolerated,
            // in force): ordinals from the logical declaration — the two
            // renditions share attribute positions, so one array serves both
            // sides of the gap.
            let dateTimeOrdinals =
                logicalKind.Attributes
                |> List.mapi (fun i a -> i, a.Type)
                |> List.choose (fun (i, t) -> if t = DateTime then Some i else None)
                |> List.toArray
            let canonicalize (q: RowQuantum) : RowQuantum =
                RowFidelity.canonicalizeDateTimeCells dateTimeOrdinals q
            // The intervention replay (SOURCE side only): the kind's own
            // (source → assigned) key map at the pk ordinal, plus each
            // referencing cell's target-kind map — T17's `κ` and `remapFks`.
            let ownMap =
                replayMaps
                |> Map.tryFind (SsKey.rootOriginal logicalKind.SsKey)
                |> Option.filter (fun m -> not (Map.isEmpty m))
            let pkOrdinal =
                logicalKind.Attributes |> List.tryFindIndex (fun a -> a.IsPrimaryKey)
            let keyRewrite =
                match ownMap, pkOrdinal with
                | Some m, Some ordinal -> Some (ordinal, m)
                | _ -> None
            let fkRewrites =
                logicalKind.References
                |> List.choose (fun r ->
                    match Map.tryFind (SsKey.rootOriginal r.TargetKind) replayMaps with
                    | Some m when not (Map.isEmpty m) ->
                        logicalKind.Attributes
                        |> List.tryFindIndex (fun a -> a.SsKey = r.SourceAttribute)
                        |> Option.map (fun ordinal -> ordinal, m)
                    | _ -> None)
            let transformSource (q: RowQuantum) : RowQuantum =
                canonicalize (RowFidelity.replayQuantum keyRewrite fkRewrites q)
            let sourceStream = mapStream transformSource (Ingestion.streamKind source physicalKind)
            let targetStream = mapStream canonicalize (Ingestion.streamKind target logicalKind)
            match RowFidelity.keyPlanOf logicalKind with
            | RowFidelity.KeyPlan.Int64Key pkName ->
                let sourceCell = RowQuantum.cellGetter sourceBasis pkName
                let targetCell = RowQuantum.cellGetter targetBasis pkName
                // A NULL primary-key cell violates the keyed-compare
                // precondition — refuse loudly, never coerce (WP-3).
                let keyValue (side: string) (cell: string voption) : int64 =
                    match cell with
                    | ValueSome v -> Int64.Parse(v, Globalization.CultureInfo.InvariantCulture)
                    | ValueNone -> invalidOp (sprintf "fidelity.keyOf: NULL primary-key cell on the %s stream" side)
                let keyOfLeft (q: RowQuantum) : int64 = keyValue "source" (sourceCell q)
                let keyOfRight (q: RowQuantum) : int64 = keyValue "target" (targetCell q)
                match keyRewrite with
                | Some _ ->
                    // The replayed key rewrite reorders the source stream in
                    // assigned-key space — the keyed compare replaces the
                    // ordered lockstep (columns stay unnamed under remap; the
                    // row is named by its assigned key).
                    let index = System.Collections.Generic.Dictionary<int64, byte[]>()
                    let! foldRight = indexTarget targetBasis keyOfRight targetStream RowDigestFold.empty index
                    let! outcome = probeSource sampleCap sourceBasis keyOfLeft sourceStream index RowDigestFold.empty [] 0L
                    return
                        { Kind = logicalKind.SsKey
                          KindName = Name.value logicalKind.Name
                          Source = RowDigestFold.finalize outcome.FoldLeft
                          Target = RowDigestFold.finalize foldRight
                          KeyColumn = Name.value pkName
                          Differences = outcome.Differences
                          DifferenceTotal = outcome.Total
                          NamingSkipped = None }
                | None ->
                    let! firstLeft = sourceStream ()
                    let! firstRight = targetStream ()
                    let! outcome =
                        lockstep sampleCap sourceBasis targetBasis keyOfLeft keyOfRight
                            sourceStream targetStream firstLeft firstRight
                            RowDigestFold.empty RowDigestFold.empty [] 0L
                    return
                        { Kind = logicalKind.SsKey
                          KindName = Name.value logicalKind.Name
                          Source = RowDigestFold.finalize outcome.FoldLeft
                          Target = RowDigestFold.finalize outcome.FoldRight
                          KeyColumn = Name.value pkName
                          Differences = outcome.Differences
                          DifferenceTotal = outcome.Total
                          NamingSkipped = None }
            | RowFidelity.KeyPlan.Unnameable reason ->
                let! foldLeft = foldStream sourceBasis sourceStream RowDigestFold.empty
                let! foldRight = foldStream targetBasis targetStream RowDigestFold.empty
                let sourceDigest = RowDigestFold.finalize foldLeft
                let targetDigest = RowDigestFold.finalize foldRight
                return
                    { Kind = logicalKind.SsKey
                      KindName = Name.value logicalKind.Name
                      Source = sourceDigest
                      Target = targetDigest
                      KeyColumn = ""
                      Differences = []
                      DifferenceTotal = (if sourceDigest = targetDigest then 0L else 1L)
                      NamingSkipped = Some reason }
        }

    let rec private compareKinds
        (source: SqlConnection)
        (target: SqlConnection)
        (renamesByKind: Map<SsKey, Map<Name, Name>>)
        (replayMaps: Map<string, Map<string, string>>)
        (sampleCap: int)
        (pairs: (Kind * Kind) list)
        (acc: KindRowVerdict list)
        : Task<KindRowVerdict list> =
        task {
            match pairs with
            | [] -> return List.rev acc
            | (physicalKind, logicalKind) :: rest ->
                let! verdict = compareKind source target renamesByKind replayMaps sampleCap physicalKind logicalKind
                return! compareKinds source target renamesByKind replayMaps sampleCap rest (verdict :: acc)
        }

    // ------------------------------------------------------------------
    // The run — resolve the alignment package, scope, stream, roll up.
    // `runWith` is the connection-agnostic core (the docker witness drives
    // it over fixture connections); `run` is the face's boundary that
    // opens the two connections from their specs.
    // ------------------------------------------------------------------

    let runWith
        (source: SqlConnection)
        (target: SqlConnection)
        (beforeLabel: string)
        (afterLabel: string)
        (modelBasis: string)
        (model: Catalog)
        (kindFilter: string option)
        (moduleFilter: string option)
        (sampleCap: int)
        (interventions: (string * Map<string, Map<string, string>>) option)
        : Task<Result<RowFidelityReport>> =
        task {
            try
                use _ = Bench.scope "fidelity.rows.run"
                let replayMaps =
                    interventions |> Option.map snd |> Option.defaultValue Map.empty
                // The alignment package: one model, two renditions, one
                // rename map per kind (physical Name → logical Name).
                let physical = CatalogRendition.physical model
                let logical = CatalogRendition.logical model
                let renamesByKind =
                    RenameProjection.renameMapByKind
                        (RenameProjection.renames (CatalogDiff.between physical logical))
                let matchesFilter (filter: string option) (n: Name) : bool =
                    match filter with
                    | None -> true
                    | Some f -> String.Equals(Name.value n, f, StringComparison.OrdinalIgnoreCase)
                let scoped =
                    logical.Modules
                    |> List.filter (fun m -> matchesFilter moduleFilter m.Name)
                    |> List.collect (fun m -> m.Kinds)
                    |> List.filter (fun k -> matchesFilter kindFilter k.Name)
                match scoped, kindFilter, moduleFilter with
                | [], Some k, _ ->
                    return Result.failureOf (ValidationError.create "fidelity.rows.scopeEmpty" (sprintf "kind '%s' is not in the model's scope." k))
                | [], _, Some m ->
                    return Result.failureOf (ValidationError.create "fidelity.rows.scopeEmpty" (sprintf "module '%s' is not in the model's scope." m))
                | [], None, None ->
                    return Result.failureOf (ValidationError.create "fidelity.rows.scopeEmpty" "the model carries no kinds to compare.")
                | scopedKinds, _, _ ->
                    let paired =
                        scopedKinds
                        |> List.choose (fun logicalKind ->
                            Catalog.tryFindKind logicalKind.SsKey physical
                            |> Option.map (fun physicalKind -> physicalKind, logicalKind))
                    let! verdicts = compareKinds source target renamesByKind replayMaps sampleCap paired []
                    return
                        Result.success
                            { BeforeLabel = beforeLabel
                              AfterLabel = afterLabel
                              ModelBasis = modelBasis
                              Interventions = interventions |> Option.map fst
                              TolerancesInForce = tolerancesInForce |> List.map ToleratedDivergence.name
                              Kinds = verdicts }
            with ex ->
                return Result.failureOf (ValidationError.create "fidelity.rows.readFailed" ex.Message)
        }

    let run
        (beforeLabel: string)
        (beforeConn: string)
        (afterLabel: string)
        (afterConn: string)
        (modelBasis: string)
        (model: Catalog)
        (kindFilter: string option)
        (moduleFilter: string option)
        (sampleCap: int)
        (interventionsPath: string option)
        : Task<Result<RowFidelityReport>> =
        task {
            // The ledger loads BEFORE the connections open — a bad path is a
            // named refusal, never a half-opened run.
            match interventionsPath |> Option.map loadReplayMaps with
            | Some (Error es) -> return Result.failure es
            | loaded ->
                try
                    let interventions =
                        match interventionsPath, loaded with
                        | Some path, Some (Ok maps) -> Some (path, maps)
                        | _ -> None
                    use source = new SqlConnection(beforeConn)
                    use target = new SqlConnection(afterConn)
                    do! source.OpenAsync()
                    do! target.OpenAsync()
                    return! runWith source target beforeLabel afterLabel modelBasis model kindFilter moduleFilter sampleCap interventions
                with ex ->
                    return Result.failureOf (ValidationError.create "fidelity.rows.readFailed" ex.Message)
        }

    // ------------------------------------------------------------------
    // The renderer — the lines beneath the voiced verdict (one report
    // value; the JSON codec is its sibling).
    // ------------------------------------------------------------------

    let private humane64 (n: int64) : string =
        n.ToString("N0", Globalization.CultureInfo.InvariantCulture)

    /// One difference, named by the kind's key column. Under a replayed
    /// remap the differing columns stay unnamed (the keyed compare holds
    /// hashes, not rows) — the row is still named by its assigned key.
    let differenceText (keyColumn: string) (d: RowDifference) : string =
        match d with
        | RowDifference.MissingInTarget key -> sprintf "%s %s is missing in the target" keyColumn key
        | RowDifference.ExtraInTarget key -> sprintf "%s %s is extra in the target" keyColumn key
        | RowDifference.CellsDiffer (key, []) -> sprintf "%s %s differs" keyColumn key
        | RowDifference.CellsDiffer (key, columns) ->
            sprintf "%s %s differs at %s" keyColumn key
                (columns |> List.map Name.value |> String.concat ", ")

    /// Render the per-kind lines beneath the verdict: agreeing kinds one
    /// line each; disagreeing kinds lead with their first named rows.
    let render (report: RowFidelityReport) : string list =
        [ yield sprintf "ROWS — %s against %s, aligned by %s"
                    report.BeforeLabel report.AfterLabel report.ModelBasis
          yield sprintf "  Tolerances in force: %s — the canonical row form's named erasures."
                    (String.concat ", " report.TolerancesInForce)
          match report.Interventions with
          | Some ledger ->
              yield sprintf "  Interventions: %s — the journal's key remaps replay onto the source before comparing." ledger
          | None -> ()
          for verdict in report.Kinds do
              if KindRowVerdict.agrees verdict then
                  yield sprintf "  %s — %s row(s), byte-identical." verdict.KindName (humane64 verdict.Source.Count)
              else
                  match verdict.NamingSkipped with
                  | Some reason ->
                      yield sprintf "  %s — the digests differ (source %s row(s), target %s); the rows stay unnamed: %s."
                                verdict.KindName (humane64 verdict.Source.Count) (humane64 verdict.Target.Count) reason
                  | None ->
                      let named =
                          verdict.Differences
                          |> List.truncate 3
                          |> List.map (differenceText verdict.KeyColumn)
                      let remainder = verdict.DifferenceTotal - int64 (List.length named)
                      let tail =
                          if remainder > 0L then sprintf "; and %s more — fidelity.rows.json carries every digest" (humane64 remainder)
                          else ""
                      yield sprintf "  %s — %s difference(s) across %s source row(s): %s%s."
                                verdict.KindName (humane64 verdict.DifferenceTotal) (humane64 verdict.Source.Count)
                                (String.concat "; " named) tail ]

    // ------------------------------------------------------------------
    // The fidelity.rows.json codec.
    // ------------------------------------------------------------------

    let private differenceJson (d: RowDifference) : JsonObject =
        let o = JsonObject()
        match d with
        | RowDifference.MissingInTarget key ->
            o.["kind"] <- JsonValue.Create "missingInTarget"
            o.["key"] <- JsonValue.Create key
        | RowDifference.ExtraInTarget key ->
            o.["kind"] <- JsonValue.Create "extraInTarget"
            o.["key"] <- JsonValue.Create key
        | RowDifference.CellsDiffer (key, columns) ->
            o.["kind"] <- JsonValue.Create "cellsDiffer"
            o.["key"] <- JsonValue.Create key
            let arr = JsonArray()
            for c in columns do arr.Add(JsonValue.Create(Name.value c))
            o.["columns"] <- arr
        o

    let toJson (report: RowFidelityReport) : JsonObject =
        let root = JsonObject()
        root.["before"] <- JsonValue.Create report.BeforeLabel
        root.["after"] <- JsonValue.Create report.AfterLabel
        root.["model"] <- JsonValue.Create report.ModelBasis
        (match report.Interventions with
         | Some ledger -> root.["interventions"] <- JsonValue.Create ledger
         | None -> ())
        let tolerances = JsonArray()
        for t in report.TolerancesInForce do tolerances.Add(JsonValue.Create t)
        root.["tolerancesInForce"] <- tolerances
        root.["agrees"] <- JsonValue.Create(RowFidelityReport.agrees report)
        root.["rowsCompared"] <- JsonValue.Create(RowFidelityReport.rowsCompared report)
        root.["differenceTotal"] <- JsonValue.Create(RowFidelityReport.differenceTotal report)
        let kinds = JsonArray()
        for v in report.Kinds do
            let o = JsonObject()
            o.["kind"] <- JsonValue.Create v.KindName
            o.["ssKey"] <- JsonValue.Create(SsKey.serialize v.Kind)
            o.["agrees"] <- JsonValue.Create(KindRowVerdict.agrees v)
            o.["sourceCount"] <- JsonValue.Create v.Source.Count
            o.["targetCount"] <- JsonValue.Create v.Target.Count
            o.["sourceDigest"] <- JsonValue.Create v.Source.Aggregate
            o.["targetDigest"] <- JsonValue.Create v.Target.Aggregate
            (if v.KeyColumn <> "" then o.["keyColumn"] <- JsonValue.Create v.KeyColumn)
            o.["differenceTotal"] <- JsonValue.Create v.DifferenceTotal
            (match v.NamingSkipped with
             | Some reason -> o.["namingSkipped"] <- JsonValue.Create reason
             | None -> ())
            let ds = JsonArray()
            for d in v.Differences do ds.Add(differenceJson d)
            o.["differences"] <- ds
            kinds.Add o
        root.["kinds"] <- kinds
        root

    /// Serialize to a pretty-printed JSON string (the artifact body).
    let toJsonString (report: RowFidelityReport) : string =
        let opts = System.Text.Json.JsonSerializerOptions(WriteIndented = true)
        (toJson report).ToJsonString(opts)
