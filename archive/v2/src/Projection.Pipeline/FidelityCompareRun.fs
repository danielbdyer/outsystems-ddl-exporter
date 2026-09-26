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
        /// The approved data-correction receipts replayed onto the source before
        /// comparing (the intervention ledger's count-bearing half). Empty ⇒ the
        /// proof claims byte-identity with no approved corrections; non-empty ⇒
        /// every non-identical raw-source difference is accounted for by a named,
        /// counted receipt. `TolerancesInForce` are representation erasures;
        /// `DataCorrectionReceipts` are approved semantic row changes — distinct
        /// planes (a correction is never a tolerance).
        DataCorrectionReceipts : DataCorrectionReceipt list
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

    /// Attach the approved data-correction receipts the publish recorded — the
    /// intervention ledger the proof's noted-exceptions cite (sorted for
    /// determinism). The fidelity face threads the episode's receipts here so a
    /// green proof NAMES its approved corrections rather than claiming raw
    /// byte-identity.
    let withDataCorrectionReceipts (receipts: DataCorrectionReceipt list) (report: RowFidelityReport) : RowFidelityReport =
        { report with DataCorrectionReceipts = DataCorrectionReceipt.sorted receipts }

    /// The kinds that disagree, largest difference first.
    let disagreeing (report: RowFidelityReport) : KindRowVerdict list =
        report.Kinds
        |> List.filter (fun k -> not (KindRowVerdict.agrees k))
        |> List.sortByDescending (fun k -> k.DifferenceTotal)

/// P2-S3 — one captured SOURCE digest to reconcile a live target kind against
/// OFFLINE (no live source). Carries the manifest's per-kind data decoupled from
/// the `ProofManifest` type (which compiles AFTER this module — `ofReport` reads
/// a `RowFidelityReport`), so the reconcile core stays free of that dependency.
type SourceDigestEntry =
    {
        KindKey  : SsKey
        KindName : string
        Digest   : RowDigestFold.TableDigest
    }

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

    /// Resolve an `@runId` operand through the run store (wave B4a — the
    /// refusal that stood at the CLI seam is lifted; a stored run now carries
    /// its `JournalRef`s, digest + path). Every miss is a NAMED refusal: no
    /// store, no such run, a run that kept no ledger, a recorded file that
    /// moved — never a guess.
    let private resolveRunJournal (runRef: string) : Result<string> =
        let runId = runRef.Substring 1
        match Run.storeDir () with
        | None ->
            Result.failureOf
                (ValidationError.create "fidelity.rows.runStoreMissing"
                    (sprintf "'%s' names a stored run, but no run store is configured — set PROJECTION_RUNS_DIR (or PROJECTION_LEDGER_DIR) to where the runs live, or name the journal file itself." runRef))
        | Some dir ->
            match Run.load dir runId with
            | None ->
                Result.failureOf
                    (ValidationError.create "fidelity.rows.runNotFound"
                        (sprintf "run '%s' is not in the run store at '%s' — `projection inspect` walks what is." runId dir))
            | Some r ->
                match r.Ledgers |> List.choose (function Run.JournalRef (digest, path) -> Some (digest, path) | _ -> None) with
                | [] ->
                    Result.failureOf
                        (ValidationError.create "fidelity.rows.runNoJournal"
                            (sprintf "run '%s' recorded no transfer journal — it ran without --journal, or predates the journal promotion. Re-run the transfer with --journal <dir>, or name the journal file itself." runId))
                | [ (_, path) ] when path <> "" ->
                    if IO.File.Exists path then Result.success path
                    else
                        Result.failureOf
                            (ValidationError.create "fidelity.rows.journalMissing"
                                (sprintf "run '%s' recorded its journal at '%s', but no file is there now." runId path))
                | [ (digest, _) ] ->
                    Result.failureOf
                        (ValidationError.create "fidelity.rows.journalMissing"
                            (sprintf "run '%s' recorded journal digest %s without a path (a pre-B4a record) — name the journal file itself (transfer-%s.ndjson under the transfer's --journal directory)." runId digest digest))
                | several ->
                    Result.failureOf
                        (ValidationError.create "fidelity.rows.journalAmbiguous"
                            (sprintf "run '%s' recorded %d journals — name the file itself: %s."
                                runId several.Length (several |> List.map snd |> List.filter (fun p -> p <> "") |> String.concat ", ")))

    /// Resolve the operand to a journal FILE: an `@runId` reads the run
    /// store's recorded `JournalRef`; a file path is itself; a directory must
    /// hold exactly one `transfer-*.ndjson` (a second one is an ambiguity,
    /// refused by name — the operator picks).
    let private resolveJournalFile (path: string) : Result<string> =
        if path.StartsWith "@" && path.Length > 1 then resolveRunJournal path
        elif IO.File.Exists path then Result.success path
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

    // ------------------------------------------------------------------
    // Approved-data-correction SOURCE replay (T17 / the intervention ledger's
    // count-bearing half). A correction rewrote/excluded row VALUES at publish;
    // the fidelity proof must replay those onto the SOURCE before comparing, or a
    // corrected target reads as a diverged source. The replay REUSES the publish
    // engine (`ApprovedDataCorrections.apply`) over buffered source rows, so the
    // replayed change counts match the recorded receipts exactly — no per-quantum
    // reimplementation that could diverge on whole-set guards.
    // ------------------------------------------------------------------

    let private ccEq (a: string) (b: string) = System.String.Equals(a, b, System.StringComparison.OrdinalIgnoreCase)

    /// A pull stream over a materialized quantum list — the substitute source for
    /// a kind whose rows the correction replay rewrote.
    let private streamOfList (items: RowQuantum list) : AsyncStream<RowQuantum> =
        let arr = List.toArray items
        let mutable i = 0
        fun () ->
            task {
                if i < arr.Length then
                    let q = arr.[i]
                    i <- i + 1
                    return Some q
                else return None
            }

    /// Drain a kind's SOURCE rows into logical-name-keyed `StaticRow`s (the grain
    /// the correction engine consumes). Identity is synthesized per row position —
    /// the engine keys exclusions on it and the proof re-derives it deterministically.
    let private bufferKindRows (source: SqlConnection) (physicalKind: Kind) (basis: RowBasis) : Task<StaticRow list> =
        task {
            let pull = Ingestion.streamKind source physicalKind
            let acc = System.Collections.Generic.List<StaticRow>()
            let mutable go = true
            let mutable idx = 0
            while go do
                match! pull () with
                | Some q ->
                    acc.Add { Identifier = StaticRow.readsideIdentity "fidelity" (Name.value physicalKind.Name) idx
                              Values = RowQuantum.toValues basis q }
                    idx <- idx + 1
                | None -> go <- false
            return List.ofSeq acc
        }

    /// The logical kinds one correction reads: its subject kind, its referenced
    /// entity (reference/sentinel guards), and its parent kind (parentAttribute) —
    /// buffered so the engine's guards see real key sets in the fidelity context.
    let private neededKindKeys (model: Catalog) (c: ApprovedDataCorrection) : Set<SsKey> =
        let subjectKey =
            match AttributeCoordinate.resolveFull model c.Subject with
            | Ok (k, _, _) -> Set.singleton k
            | Error _ -> Set.empty
        let referencedKey =
            match c.ReferencedEntity with
            | Some ent ->
                Catalog.allModulesKinds model
                |> List.tryPick (fun (m, k) ->
                    if (ent.Module = "" || ccEq (Name.value m.Name) ent.Module) && ccEq (Name.value k.Name) ent.Entity
                    then Some k.SsKey else None)
                |> Option.map Set.singleton |> Option.defaultValue Set.empty
            | None -> Set.empty
        let parentKey =
            match c.Derivation with
            | DataCorrectionDerivationSpec.ParentAttribute (rel, _) ->
                match AttributeCoordinate.resolveFull model c.Subject with
                | Ok (k, _, _) ->
                    match Catalog.tryFindKind k model with
                    | Some kind ->
                        kind.References
                        |> List.tryFind (fun r -> ccEq (Name.value r.Name) rel)
                        |> Option.map (fun r -> Set.singleton r.TargetKind)
                        |> Option.defaultValue Set.empty
                    | None -> Set.empty
                | Error _ -> Set.empty
            | _ -> Set.empty
        Set.unionMany [ subjectKey; referencedKey; parentKey ]

    let private correctedKindKeys (model: Catalog) (enabled: ApprovedDataCorrection list) : Set<SsKey> =
        enabled
        |> List.choose (fun c -> match AttributeCoordinate.resolveFull model c.Subject with Ok (k, _, _) -> Some k | Error _ -> None)
        |> Set.ofList

    /// Replay approved corrections onto the source: buffer each needed kind's
    /// source rows, run the publish engine, and return the corrected rows FOR THE
    /// SUBJECT KINDS (the ones whose source stream is substituted in the compare)
    /// plus the count-bearing receipts. Empty corrections ⇒ identity. A named
    /// engine refusal (e.g. a parent-derived correction whose parent evidence is
    /// unavailable) fails the proof BY NAME rather than silently comparing raw.
    let private replayCorrections
        (source: SqlConnection)
        (physical: Catalog)
        (model: Catalog)
        (renamesByKind: Map<SsKey, Map<Name, Name>>)
        (corrections: ApprovedDataCorrection list)
        : Task<Result<Map<SsKey, StaticRow list> * DataCorrectionReceipt list>> =
        task {
            let enabled = corrections |> List.filter (fun c -> c.Enabled)
            if List.isEmpty enabled then return Result.success (Map.empty, [])
            else
                let needed = enabled |> List.map (neededKindKeys model) |> Set.unionMany
                let acc = System.Collections.Generic.Dictionary<SsKey, StaticRow list>()
                for logicalKey in Set.toList needed do
                    match Catalog.tryFindKind logicalKey physical, Catalog.tryFindKind logicalKey model with
                    | Some physKind, Some _ ->
                        let renameMap = RenameProjection.forKind logicalKey renamesByKind
                        let basis = RowBasis.rename renameMap (Kind.rowBasis physKind)
                        let! rows = bufferKindRows source physKind basis
                        acc.[logicalKey] <- rows
                    | _ -> ()
                let rowMap = acc |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
                match ApprovedDataCorrections.apply model enabled rowMap with
                | Error e -> return Error e
                | Ok outcome ->
                    let corrected = correctedKindKeys model enabled
                    let correctedByKind = outcome.CorrectedRows |> Map.filter (fun k _ -> Set.contains k corrected)
                    return Result.success (correctedByKind, outcome.Receipts)
        }

    let private compareKind
        (source: SqlConnection)
        (target: SqlConnection)
        (renamesByKind: Map<SsKey, Map<Name, Name>>)
        (replayMaps: Map<string, Map<string, string>>)
        (correctedByKind: Map<SsKey, StaticRow list>)
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
            // Approved-correction replay: when this kind's rows were corrected,
            // the source stream is the ENGINE-corrected rows (rebased onto the
            // source basis), so `target == replay(corrections, source)`. Otherwise
            // the live source streams as before.
            let sourcePull =
                match Map.tryFind logicalKind.SsKey correctedByKind with
                | Some correctedRows -> streamOfList (correctedRows |> List.map (RowQuantum.ofStaticRow sourceBasis))
                | None -> Ingestion.streamKind source physicalKind
            let sourceStream = mapStream transformSource sourcePull
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

    // -- P2-S3: the OFFLINE reconcile (a live target vs a STORED source digest) --

    /// Fold ONE kind's digest from the LIVE TARGET alone and compare it to the
    /// STORED source digest carried by a portable manifest (no live source). The
    /// target's rows read under the LOGICAL basis, millisecond-canonicalized
    /// exactly as `compareKind` does, so a byte-identical estate re-derives the
    /// captured digest. Per-kind pass/fail ONLY — naming WHICH rows differ needs
    /// the live source, so the verdict carries the named `NamingSkipped` downgrade.
    let private reconcileKindAgainstDigest
        (target: SqlConnection)
        (sourceDigest: RowDigestFold.TableDigest)
        (logicalKind: Kind)
        : Task<KindRowVerdict> =
        task {
            use _ = Bench.scope "fidelity.reconcile.kind"
            let targetBasis = Kind.rowBasis logicalKind
            let dateTimeOrdinals =
                logicalKind.Attributes
                |> List.mapi (fun i a -> i, a.Type)
                |> List.choose (fun (i, t) -> if t = DateTime then Some i else None)
                |> List.toArray
            let canonicalize (q: RowQuantum) : RowQuantum =
                RowFidelity.canonicalizeDateTimeCells dateTimeOrdinals q
            let targetStream = mapStream canonicalize (Ingestion.streamKind target logicalKind)
            let! foldRight = foldStream targetBasis targetStream RowDigestFold.empty
            let targetDigest = RowDigestFold.finalize foldRight
            return
                { Kind = logicalKind.SsKey
                  KindName = Name.value logicalKind.Name
                  Source = sourceDigest
                  Target = targetDigest
                  KeyColumn = ""
                  Differences = []
                  DifferenceTotal = (if sourceDigest = targetDigest then 0L else 1L)
                  NamingSkipped = Some "reconciled against a stored source digest — no live source, so the per-kind verdict stands but rows are not named (escalate to `check data --rows` for the drill-down)" }
        }

    /// Reconcile ONE captured entry: fold the target kind when the model carries
    /// it, else surface a NAMED divergence (a manifest kind the model lacks — the
    /// alignment basis moved; never a silent drop). The entry is a RECORD, so it
    /// destructures by field, not a tuple `let!`/`for` (FS3511-safe inside `task`).
    let private reconcileEntry
        (target: SqlConnection)
        (logicalByKey: Map<SsKey, Kind>)
        (entry: SourceDigestEntry)
        : Task<KindRowVerdict> =
        task {
            match Map.tryFind entry.KindKey logicalByKey with
            | Some logicalKind -> return! reconcileKindAgainstDigest target entry.Digest logicalKind
            | None ->
                return
                    { Kind = entry.KindKey
                      KindName = entry.KindName
                      Source = entry.Digest
                      Target = RowDigestFold.finalize RowDigestFold.empty
                      KeyColumn = ""
                      Differences = []
                      DifferenceTotal = 1L
                      NamingSkipped = Some "the manifest carries a kind the model does not — the alignment basis has moved; cannot reconcile" }
        }

    /// P2-S3 — the OFFLINE reconcile: for each captured source digest, fold the
    /// live target kind and compare, with NO live source present. Reuses the
    /// `RowFidelityReport` shape so the `--against` verb rides the same render /
    /// JSON / exit-code surface as the flow proof.
    let reconcileAgainstDigests
        (target: SqlConnection)
        (sourceLabel: string)
        (targetLabel: string)
        (tolerancesInForce: string list)
        (logical: Catalog)
        (sourceDigests: SourceDigestEntry list)
        : Task<RowFidelityReport> =
        task {
            let logicalByKey =
                logical |> Catalog.allKinds |> List.map (fun k -> k.SsKey, k) |> Map.ofList
            let verdicts = System.Collections.Generic.List<KindRowVerdict>()
            for entry in sourceDigests do
                let! v = reconcileEntry target logicalByKey entry
                verdicts.Add v
            return
                { BeforeLabel = sourceLabel
                  AfterLabel = targetLabel
                  ModelBasis = "the portable proof manifest"
                  Interventions = None
                  TolerancesInForce = tolerancesInForce
                  DataCorrectionReceipts = []
                  Kinds = List.ofSeq verdicts }
        }

    let rec private compareKinds
        (source: SqlConnection)
        (target: SqlConnection)
        (renamesByKind: Map<SsKey, Map<Name, Name>>)
        (replayMaps: Map<string, Map<string, string>>)
        (correctedByKind: Map<SsKey, StaticRow list>)
        (sampleCap: int)
        (pairs: (Kind * Kind) list)
        (acc: KindRowVerdict list)
        : Task<KindRowVerdict list> =
        task {
            match pairs with
            | [] -> return List.rev acc
            | (physicalKind, logicalKind) :: rest ->
                let! verdict = compareKind source target renamesByKind replayMaps correctedByKind sampleCap physicalKind logicalKind
                return! compareKinds source target renamesByKind replayMaps correctedByKind sampleCap rest (verdict :: acc)
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
        (corrections: ApprovedDataCorrection list)
        (recordedReceipts: DataCorrectionReceipt list)
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
                    // Replay approved corrections onto the source (engine-reuse) —
                    // the subject kinds' streams become the corrected rows, so a
                    // corrected target proves byte-identical against the replayed
                    // source. A named engine refusal fails the proof by name.
                    match! replayCorrections source physical model renamesByKind corrections with
                    | Error es -> return Result.failure es
                    | Ok (correctedByKind, replayedReceipts) ->
                        // The count-bounded reconciliation: when recorded receipts
                        // are supplied, the replay must reproduce them count-for-count
                        // (a tampered / drifted count reds the proof by name).
                        let reconciliation =
                            if List.isEmpty recordedReceipts then Result.success ()
                            else ApprovedDataCorrections.reconcile recordedReceipts replayedReceipts
                        match reconciliation with
                        | Error es -> return Result.failure es
                        | Ok () ->
                            let! verdicts = compareKinds source target renamesByKind replayMaps correctedByKind sampleCap paired []
                            return
                                Result.success
                                    { BeforeLabel = beforeLabel
                                      AfterLabel = afterLabel
                                      ModelBasis = modelBasis
                                      Interventions = interventions |> Option.map fst
                                      TolerancesInForce = tolerancesInForce |> List.map ToleratedDivergence.name
                                      DataCorrectionReceipts = DataCorrectionReceipt.sorted replayedReceipts
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
        (corrections: ApprovedDataCorrection list)
        (recordedReceipts: DataCorrectionReceipt list)
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
                    return! runWith source target beforeLabel afterLabel modelBasis model kindFilter moduleFilter sampleCap interventions corrections recordedReceipts
                with ex ->
                    return Result.failureOf (ValidationError.create "fidelity.rows.readFailed" ex.Message)
        }

    /// Load the RECORDED correction receipts the publish/load episode produced,
    /// so the proof reconciles against the EXACT counts recorded (not merely the
    /// configured corrections). Accepts either a bare JSON array of receipt
    /// objects or an object carrying a `dataCorrectionReceipts` array (so an
    /// operator can point `--correction-receipts` at a run's `fidelity.rows.json`
    /// or a `manifest.data-corrections.json`). Only the reconcile-relevant fields
    /// (`correctionId`, `rowsChanged`, `rowsExcluded`) are read; the rest default.
    let loadCorrectionReceipts (path: string) : Result<DataCorrectionReceipt list> =
        try
            let text = System.IO.File.ReadAllText path
            use doc = System.Text.Json.JsonDocument.Parse text
            let root = doc.RootElement
            let arr : System.Text.Json.JsonElement option =
                match root.ValueKind with
                | System.Text.Json.JsonValueKind.Array -> Some root
                | System.Text.Json.JsonValueKind.Object ->
                    match root.TryGetProperty "dataCorrectionReceipts" with
                    | true, v when v.ValueKind = System.Text.Json.JsonValueKind.Array -> Some v
                    | _ -> None
                | _ -> None
            match arr with
            | None ->
                Result.failureOf (ValidationError.create "fidelity.correctionReceipts.shape"
                    (sprintf "the correction-receipts file '%s' must be a JSON array of receipts, or an object with a 'dataCorrectionReceipts' array." path))
            | Some a ->
                let int64Of (el: System.Text.Json.JsonElement) (name: string) : int64 =
                    match el.TryGetProperty name with
                    | true, v when v.ValueKind = System.Text.Json.JsonValueKind.Number -> (match v.TryGetInt64() with true, n -> n | _ -> 0L)
                    | _ -> 0L
                let receipts =
                    [ for el in a.EnumerateArray() do
                        if el.ValueKind = System.Text.Json.JsonValueKind.Object then
                            match el.TryGetProperty "correctionId" with
                            | true, cid when cid.ValueKind = System.Text.Json.JsonValueKind.String ->
                                match Option.ofObj (cid.GetString()) with
                                | Some id ->
                                    yield
                                        { CorrectionId = id
                                          SourceRemediationId = None
                                          Subject = AttributeCoordinate.create "" "" ""
                                          Derivation = DataCorrectionDerivation.ExcludeRows
                                          GuardResults = []
                                          RowsMatched = int64Of el "rowsMatched"
                                          RowsChanged = int64Of el "rowsChanged"
                                          RowsExcluded = int64Of el "rowsExcluded"
                                          ChangedRows = []
                                          ExcludedRows = []
                                          BeforeDigest = None
                                          AfterDigest = None
                                          EvidenceColumns = []
                                          EvidenceDigest = None
                                          ApprovedBy = None
                                          ApprovedAt = None }
                                | None -> ()
                            | _ -> () ]
                Result.success receipts
        with ex ->
            Result.failureOf (ValidationError.create "fidelity.correctionReceipts.unreadable"
                (sprintf "the correction-receipts file '%s' did not load: %s" path ex.Message))

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
          match report.DataCorrectionReceipts with
          | [] -> ()
          | receipts ->
              let changed = receipts |> List.sumBy (fun r -> r.RowsChanged)
              let excluded = receipts |> List.sumBy (fun r -> r.RowsExcluded)
              yield sprintf "  Interventions: approved data corrections replayed onto the source before comparing — %s correction(s), %s row(s) changed, %s excluded; every difference cites its receipt."
                        (humane64 (int64 (List.length receipts))) (humane64 changed) (humane64 excluded)
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
        // The approved data-correction receipts — the count-bearing ledger a green
        // proof's noted-exceptions cite (distinct from the representation erasures
        // in `tolerancesInForce`). Deterministic (sorted at attach time).
        let receipts = JsonArray()
        for r in report.DataCorrectionReceipts do
            let o = JsonObject()
            o.["correctionId"] <- JsonValue.Create r.CorrectionId
            (match r.SourceRemediationId with Some s -> o.["sourceRemediationId"] <- JsonValue.Create s | None -> ())
            o.["derivation"] <- JsonValue.Create(DataCorrectionDerivation.name r.Derivation)
            o.["rowsMatched"] <- JsonValue.Create r.RowsMatched
            o.["rowsChanged"] <- JsonValue.Create r.RowsChanged
            o.["rowsExcluded"] <- JsonValue.Create r.RowsExcluded
            // The EXACT rows touched — the precise audit log ("no more, no less"):
            // each entry names the row identity + subject before → after.
            let rowArray (rows: DataCorrectionRowChange list) : JsonArray =
                let arr = JsonArray()
                for rc in rows do
                    let ro = JsonObject()
                    ro.["rowIdentity"] <- JsonValue.Create rc.RowIdentity
                    (match rc.Before with Some b -> ro.["before"] <- JsonValue.Create b | None -> ())
                    (match rc.After with Some a -> ro.["after"] <- JsonValue.Create a | None -> ())
                    arr.Add ro
                arr
            (if not (List.isEmpty r.ChangedRows) then o.["changedRows"] <- rowArray r.ChangedRows)
            (if not (List.isEmpty r.ExcludedRows) then o.["excludedRows"] <- rowArray r.ExcludedRows)
            (match r.EvidenceDigest with Some d -> o.["evidenceDigest"] <- JsonValue.Create d | None -> ())
            (if not (List.isEmpty r.EvidenceColumns) then
                let ev = JsonArray()
                for ec in r.EvidenceColumns do
                    ev.Add(JsonValue.Create(System.String.Concat(ec.Module, "/", ec.Entity, "/", ec.Attribute)))  // LINT-ALLOW: terminal display projection of an evidence-column coordinate in the fidelity JSON artifact
                o.["evidenceColumns"] <- ev)
            receipts.Add o
        root.["dataCorrectionReceipts"] <- receipts
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

    // ------------------------------------------------------------------
    // The proof reader (RT-10, wave A4β) — the estate board reads back a
    // `fidelity.rows.json` artifact to fold its verdict into the estate's.
    // The proof carries no timestamp of its own, so its AGE comes from the
    // file's last-write time (the boundary supplies the clock). Fail-closed:
    // an absent or torn artifact reads as `None`, never a half-truth — the
    // board then treats the proof as missing. The read shape is the codec's
    // top-level `agrees` / `differenceTotal` / `rowsCompared` (the digests
    // and per-kind detail are not needed for the clause).
    // ------------------------------------------------------------------

    /// The read-back summary of one proof artifact — what the estate's
    /// fidelity clause reads.
    type ProofSummary =
        { Agrees          : bool
          RowsCompared    : int64
          DifferenceTotal : int64
          WrittenAtUtc    : DateTimeOffset }

    /// Read a `fidelity.rows.json` artifact at `path`, or `None` when it is
    /// absent or unreadable (fail-closed — a torn proof is no proof). The
    /// write time is the artifact file's `LastWriteTimeUtc` (the proof's age
    /// basis; the codec writes no timestamp).
    let tryReadProof (path: string) : ProofSummary option =
        if not (IO.File.Exists path) then None
        else
            try
                use doc = System.Text.Json.JsonDocument.Parse(IO.File.ReadAllText path)
                let root = doc.RootElement
                let boolOf (name: string) =
                    let mutable v = Unchecked.defaultof<System.Text.Json.JsonElement>
                    if root.TryGetProperty(name, &v) && (v.ValueKind = System.Text.Json.JsonValueKind.True || v.ValueKind = System.Text.Json.JsonValueKind.False)
                    then Some (v.GetBoolean()) else None
                let i64Of (name: string) =
                    let mutable v = Unchecked.defaultof<System.Text.Json.JsonElement>
                    if root.TryGetProperty(name, &v) && v.ValueKind = System.Text.Json.JsonValueKind.Number
                    then let mutable n = 0L in (if v.TryGetInt64(&n) then Some n else None)
                    else None
                // `agrees` and `differenceTotal` are the load-bearing fields; a
                // proof missing either is torn (fail-closed).
                match boolOf "agrees", i64Of "differenceTotal" with
                | Some agrees, Some diffs ->
                    Some
                        { Agrees          = agrees
                          RowsCompared    = i64Of "rowsCompared" |> Option.defaultValue 0L
                          DifferenceTotal = diffs
                          WrittenAtUtc    = DateTimeOffset(IO.File.GetLastWriteTimeUtc path, TimeSpan.Zero) }
                | _ -> None
            with _ -> None
