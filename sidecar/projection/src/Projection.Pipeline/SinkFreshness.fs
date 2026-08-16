namespace Projection.Pipeline
// LINT-ALLOW-FILE: fingerprint rendering at the persistence boundary (the
//   data-sink chapter, S8) — terminal fingerprint-text composition
//   (sprintf/String.Concat over already-rendered probe segments) for the
//   manifest's recorded pairs; the structural surface is the typed
//   TableFingerprint carrier + the closed Decision/Miss algebra.

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Adapters.Sql

/// The sink's freshness axis (the data-sink chapter, S8): WHAT is probed
/// (three literal ossys tables — the metadata plane's movement signal),
/// HOW a reading renders into the manifest's recorded pairs, and the pure
/// decision table `off | auto | pinned` × `--refresh` × recorded × probed
/// resolve through. R2 (ruled at chapter open): this axis governs REUSE
/// only — witnessing is gated by store presence + acquisition totality,
/// never by policy.
[<RequireQualifiedAccess>]
module SinkFreshness =

    /// The three movement bellwethers: eSpaces, entities, attributes — the
    /// tables whose rows carry the identity/lifecycle facts every sink
    /// displacement classifies over. Star-form checksum (no column
    /// inventory is assumed for a platform table; `BINARY_CHECKSUM(*)`
    /// skips noncomparable columns server-side); `ID` is each table's
    /// single-column primary key on every supported platform version.
    let targets : TableFingerprint.TableTarget list =
        [ { Schema = "dbo"; Table = "ossys_Espace"; MaxPkColumn = Some "ID"; ChecksumColumns = None }
          { Schema = "dbo"; Table = "ossys_Entity"; MaxPkColumn = Some "ID"; ChecksumColumns = None }
          { Schema = "dbo"; Table = "ossys_Entity_Attr"; MaxPkColumn = Some "ID"; ChecksumColumns = None } ]

    /// A target's manifest key (`dbo.ossys_Espace`).
    let nameOf (t: TableFingerprint.TableTarget) : string =
        System.String.Concat(t.Schema, ".", t.Table)

    /// The typed reading a probe measurement carries (align-II.11: the
    /// manifest persists the three explicit fields; the packed
    /// `count|maxPk|content` display form lives on `FingerprintReading.packed`).
    let readingOf (r: TableFingerprint.TableReading) : SinkStore.FingerprintReading =
        { RowCount = r.RowCount; MaxPk = r.MaxPk; Content = r.Content }

    /// A reading's display text — the S8 packed form (kept as the ONE
    /// rendering for logs and old-manifest parsing; comparison no longer
    /// rides it — `decide` compares fields).
    let render (r: TableFingerprint.TableReading) : string =
        SinkStore.FingerprintReading.packed (readingOf r)

    /// Probe the three targets on an open source connection → the typed
    /// (target, reading) pairs. ADVISORY: a probe failure returns `[]`
    /// (the witness records no fingerprints; `auto` then always reads live
    /// — freshness only ever degrades toward the wire, never toward stale
    /// reuse).
    let probe (cnn: SqlConnection) : Task<(string * SinkStore.FingerprintReading) list> =
        task {
            match! TableFingerprint.probeWith "sink.fingerprint.probe" "sink.probeFailed" cnn targets with
            | Ok readings -> return readings |> List.map (fun r -> nameOf r.Target, readingOf r)
            | Error _ -> return []
        }

    /// Why a freshness decision paid the wire — the CLOSED miss taxonomy
    /// (every miss is named; the pure table below is total over it).
    [<RequireQualifiedAccess>]
    type Miss =
        /// The standing default: the policy says every read pays the wire.
        | PolicyOff
        /// `--refresh` — the operator's one-run override (beats even a pin).
        | RefreshForced
        /// No witnessed state exists for this source yet.
        | NoWitnessedState
        /// The manifest carries no recorded fingerprints (witnessed before
        /// S8, or the capture-time probe failed) — nothing to match against.
        | FingerprintAbsent
        /// The live probe failed NOW — freshness cannot be confirmed.
        | ProbeFailed
        /// The estate moved: each moved target with the AXES that moved
        /// (align-II.11 — field-wise comparison names the movement; an
        /// absent current reading moves every axis, since nothing
        /// confirms any of them).
        | FingerprintMoved of moved: (string * SinkStore.FingerprintAxis list) list

    [<RequireQualifiedAccess>]
    type Decision =
        /// Serve the witnessed state (the pay-once fast path).
        | ReuseSink of syncId: int
        /// Pay the wire (which witnesses the fresh state), for the named miss.
        | ReadLive of miss: Miss

    /// The pure decision table. `probed = None` means the live probe was
    /// unavailable or failed; `Some pairs` is the current reading. Total
    /// over every input shape; the precedence is operator-act-first
    /// (`--refresh` names the decision even under `off`, because the
    /// explicit act outranks the standing default in the telling).
    let decide
        (policy: Config.SinkPolicy)
        (refresh: bool)
        (manifest: SinkStore.Manifest option)
        (probed: (string * SinkStore.FingerprintReading) list option)
        : Decision =
        if refresh then Decision.ReadLive Miss.RefreshForced
        else
            match policy with
            | Config.SinkPolicy.Off -> Decision.ReadLive Miss.PolicyOff
            | Config.SinkPolicy.Pinned ->
                match manifest with
                | Some m -> Decision.ReuseSink m.LatestSyncId
                | None -> Decision.ReadLive Miss.NoWitnessedState
            | Config.SinkPolicy.Auto ->
                match manifest with
                | None -> Decision.ReadLive Miss.NoWitnessedState
                | Some m ->
                    match m.SourceFingerprints with
                    | [] -> Decision.ReadLive Miss.FingerprintAbsent
                    | recorded ->
                        match probed with
                        | None -> Decision.ReadLive Miss.ProbeFailed
                        | Some current ->
                            let currentMap = Map.ofList current
                            let moved =
                                recorded
                                |> List.choose (fun (target, reading) ->
                                    match Map.tryFind target currentMap with
                                    | Some now ->
                                        match SinkStore.FingerprintReading.movedAxes reading now with
                                        | [] -> None
                                        | axes -> Some (target, axes)
                                    | None ->
                                        // An absent current reading moves
                                        // every axis — nothing confirms
                                        // any of them.
                                        Some (target, [ SinkStore.FingerprintAxis.Rows; SinkStore.FingerprintAxis.MaxPk; SinkStore.FingerprintAxis.Content ]))
                            match moved with
                            | [] -> Decision.ReuseSink m.LatestSyncId
                            | moved -> Decision.ReadLive (Miss.FingerprintMoved moved)
