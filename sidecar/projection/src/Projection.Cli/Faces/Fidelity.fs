module Projection.Cli.Faces.Fidelity
// LINT-ALLOW-FILE: CLI run-face operator-facing prose + Voice payload boxing at the terminal CLI boundary; the structural surface is the typed MovementSpec / RowFidelityReport / Voice catalog, BCL primitives only at this terminal text edge.

// The row-fidelity face (`check data --rows` — T17, the fidelity chapter;
// DECISIONS 2026-07-15 "The fidelity chapter opens"; wave B2). Read-only:
// resolves the model reference (the alignment basis whose rename map closes
// the physical-to-logical gap), streams both environments' rows in
// primary-key order through the lockstep comparator, renders the proof
// verdict through the Voice catalog with the per-kind lines beneath it,
// writes `fidelity.rows.json`, and exits 0 (byte-identical) / 5 (differing
// rows, named) / 6 (the model or an environment could not be read).

open System
open System.Threading.Tasks
open Projection.Core
open Projection.Pipeline
open Projection.Targets.SSDT
open Projection.Cli
open Projection.Cli.OperatorConsole

/// Write the artifact, translating an I/O failure into a plain advisory
/// rather than an unhandled stack trace (THE_VOICE §14). The verdict
/// stands on the rendered proof; the file is its machine sibling.
let private tryWriteArtifact (path: string) (content: string) : unit =
    try IO.File.WriteAllText(path, content)
    with ex -> eprintfn "  Could not write %s: %s" path ex.Message

let runCheckDataRows (args: CheckDataRowsArgs) : int =
    match (Ref.resolveCatalog (Ref.parse args.ModelRef)).GetAwaiter().GetResult() with
    | Error errs ->
        printErrors Console.Error errs
        6
    | Ok model ->
        let outcome =
            (FidelityCompareRun.run
                args.BeforeLabel args.BeforeConn
                args.AfterLabel args.AfterConn
                args.ModelRef model
                args.Kind args.Module args.SampleCap args.Interventions).GetAwaiter().GetResult()
        match outcome with
        | Error errs ->
            printErrors Console.Error errs
            6
        | Ok report ->
            let artifact = FidelityCompareRun.toJsonString report
            if args.AsJson then
                printfn "%s" artifact
            else
                let payload : Voice.Payload =
                    Map.ofList
                        [ "rows",  box (RowFidelityReport.rowsCompared report)
                          "kinds", box (List.length report.Kinds)
                          "diffs", box (RowFidelityReport.differenceTotal report)
                          "ledger", box (report.Interventions |> Option.defaultValue "")
                          "tolerances", box (String.concat ", " report.TolerancesInForce)
                          "artifactPath", box "fidelity.rows.json" ]
                let verdictCode =
                    if RowFidelityReport.agrees report then "fidelity.rows.matched" else "fidelity.rows.diverged"
                TtyRenderer.renderVoicedTo Console.Out verdictCode payload
                printfn ""
                FidelityCompareRun.render report |> List.iter (fun line -> printfn "%s" line)
            tryWriteArtifact "fidelity.rows.json" artifact
            if RowFidelityReport.agrees report then 0 else 5

/// `check fidelity <flow>` — THE CONTAINER PROOF (T17, wave B5; DECISIONS
/// 2026-07-17, the loop-closing program's phase 2). One command: scaffold a
/// per-run database on the local container, stand the TARGET's shape up on
/// it (the model's logical rendition — what the cutover would deploy), load
/// it through the transfer machinery exactly as a cutover load runs
/// (journaled wipe-and-load across the rendition gap, FKs re-trusted —
/// "after applying the FKs"), then prove the load row-faithful against the
/// flow's live source modulo the journal's recorded interventions — and
/// reap the stand-in.
/// The write target is the verb's OWN scratch (created and dropped here), so
/// the run rides the ReadOnly register and no operator gate applies; the
/// Replace greenlight below is the verb's, never the flow's. Exits:
/// 0 proof green · 5 differing rows, named · 6 the model / source / scaffold
/// could not be read or refused · 4 Docker absent.
let private humaneRows (n: int64) : string =
    n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)

let private ageText (days: int) : string =
    if days <= 0 then "today" else sprintf "%d day(s) ago" days

/// P1-S1 — stand the target's SCHEMA up on the per-run scratch via the chosen
/// staging mode. `Ddl` applies the emitted statement batch (the executor's
/// path — the pre-P1-S1 behaviour, byte-identical); `Dacfx` publishes the
/// emitted `.dacpac` through `DacServices`. Both land the SAME logical schema —
/// a model-built dacpac is schema-only by construction — so the load and proof
/// that follow are identical; only the schema-staging leg differs. Fail-loud: an
/// emit/publish failure is the run's named error, never a silent empty stand-in.
let private stageStandIn
    (mode: StagingMode)
    (scratchConn: string)
    (standIn: Microsoft.Data.SqlClient.SqlConnection)
    (logical: Catalog)
    : Task<Result<unit>> =
    task {
        match mode with
        | StagingMode.Ddl ->
            do! Deploy.executeBatch standIn (SsdtDdlEmitter.statements logical |> Render.toText)
            return Result.success ()
        | StagingMode.Dacfx ->
            match DacpacEmitter.emit logical with
            | Error es  -> return Result.failure es
            | Ok dacpac -> return! Deploy.deployDacpac scratchConn dacpac
    }

let runCheckFidelityFlow (model: Catalog) (args: CheckFidelityFlowArgs) : int =
    // ONE model, two renditions — exactly the comparator's alignment package:
    // the flow's live source carries the PHYSICAL rendition (the estate's
    // OSUSR shape), the stand-in carries the LOGICAL rendition (the target's
    // shape — the model as the cutover would deploy it), and the compare
    // re-bases the physical stream onto the logical names (T17's triangle).
    // Model-rendered contracts on BOTH sides are the proof's precondition:
    // the SsKeys the journal records are the model's own (rendition-
    // invariant), so the ledger-modulated compare can translate them — a
    // live-read contract would synthesize keys the replay could never match.
    let physical = CatalogRendition.physical model
    let logical = CatalogRendition.logical model
    // The proof's journal is an operator artifact — the intervention ledger
    // the verdict cites, and the run's recorded `JournalRef` (so
    // `--interventions @runId` can replay this exact proof later). It lives
    // beside fidelity.rows.json, never inside the reaped scratch.
    let journalDir = IO.Path.Combine("fidelity-proof", args.Flow)
    // The clock, read ONCE at the boundary and threaded (the estate face's idiom).
    let nowUtc = DateTimeOffset.UtcNow
    // -- the incremental proof cache (wave B6) --------------------------------
    // A cached GREEN proof, still valid — the model's shape digest AND the
    // source's per-kind fingerprints unchanged since it ran — skips the
    // EXPENSIVE container proof entirely (no scaffold, no transfer, no Docker).
    // `--refresh` clears this flow's entry and forces a full re-prove. The cache
    // lives under the estate store root (`proofs/<flow>.proof.json`); it is
    // DISABLED — nothing cached, nothing to clear — when no store is configured.
    // Fingerprint honesty: the (row-count, max-key, schema-shape) fingerprint is
    // BLIND to an in-place UPDATE (the estate evidence store's named caveat), so
    // `--refresh` is the certain re-prove.
    // The estate store root, resolved ONCE (`None` = disabled → no cache).
    let storeRoot = EstateEvidenceStore.storeDir ()
    if args.Refresh then storeRoot |> Option.iter (fun root -> FidelityProofCache.clear root args.Flow |> ignore)
    let currentModelHash = FidelityProofCache.modelHash model
    // Probe the source's fingerprints as the proof reads the source (the
    // physical rendition's kinds, the resolved source connection). A probe
    // failure is a cache miss — the proof runs and names its own read.
    let sourceFps : KindFingerprint list option =
        // P1-S1 — the incremental cache is DDL-keyed; a `--stage dacfx` run
        // probes nothing and reuses nothing (it always runs the container proof).
        if Option.isNone storeRoot || args.Stage <> StagingMode.Ddl then None
        else
            match (EstateEvidenceStore.probeLive args.SourceConn physical).GetAwaiter().GetResult() with
            | Ok fps -> Some fps
            | Error _ -> None
    let cacheHit : CachedProof option =
        match storeRoot, sourceFps with
        // P2-S2 — `--capture` forces a full proof: the manifest needs the report's
        // per-kind source digests, which a cache hit does not carry.
        | Some root, Some fps when not args.Refresh && args.Stage = StagingMode.Ddl && Option.isNone args.Capture ->
            match FidelityProofCache.tryRead root args.Flow with
            | Some cached when FidelityProofCache.isFresh cached currentModelHash fps -> Some cached
            | _ -> None
        | _ -> None
    // The container proof — run only on a cache miss (or `--refresh`).
    let runProof () : int =
      if not (Deploy.Docker.isAvailable ()) then
        // §14 required-and-missing, voiced by code (the deploy face's shape).
        TtyRenderer.renderVoicedTo Console.Error "docker.unavailable"
            (Map.ofList [ "purpose", box "fidelity proof" ])
        4
      else
      TtyRenderer.renderVoicedTo Console.Out "container.starting"
        (Map.ofList [ "purpose", box "fidelity proof" ])
      let work () : Task<Result<Transfer.TransferReport * RowFidelityReport>> =
        Deploy.withScratchDatabase "ProjectionFidelity" (fun scratchConn ->
            task {
                // 1 — THE SCAFFOLD: the target-shape schema (the logical
                //     rendition), staged whole onto the per-run scratch — via
                //     the emitted DDL batch (`--stage ddl`, the default) or a
                //     DacFx publish (`--stage dacfx`). A model-built dacpac is
                //     schema-only, so the load below is identical across both.
                use standIn = new Microsoft.Data.SqlClient.SqlConnection(scratchConn)
                do! standIn.OpenAsync()
                match! stageStandIn args.Stage scratchConn standIn logical with
                | Error es -> return Result.failure es
                | Ok () ->
                // 2 — THE LOAD: the transfer machinery, journaled wipe-and-load
                //     across the rendition gap (reads with the source's physical
                //     names, writes with the stand-in's logical names — the
                //     rename-aware leg the LE-3 canaries prove, mirrored).
                let srcSub : Substrate =
                    { Environment = Projection.Core.Environment.Named args.FromLabel
                      Role = SubstrateRole.Source
                      ConnectionRef = ConnectionRef.Raw args.SourceConn }
                let sinkSub : Substrate =
                    { Environment = Projection.Core.Environment.Named "container stand-in"
                      Role = SubstrateRole.Sink
                      ConnectionRef = ConnectionRef.Raw scratchConn }
                match TransferConnections.create srcSub sinkSub false with
                | Error es -> return Result.failure es
                | Ok connections ->
                    let! transferR =
                        Transfer.runReverseLegThroughConnectionsWith
                            IdentityPolicy.Structural Transfer.Execute EmissionMode.WipeAndLoad false false false []
                            connections physical logical Map.empty Set.empty Set.empty Set.empty
                            [ WriteSignoff.greenlit WriteSignoff.WriteMode.Replace ] [] false Set.empty Set.empty false None (Some journalDir)
                    match transferR with
                    | Error es -> return Result.failure es
                    | Ok transferReport ->
                        // 3 — THE PROOF: the flow's live source against the
                        //     loaded stand-in, aligned by the model, modulo
                        //     the journal's recorded interventions.
                        let! proof =
                            FidelityCompareRun.run
                                args.FromLabel args.SourceConn
                                (sprintf "%s stand-in" args.Flow) scratchConn
                                "the authored model" model
                                None None args.SampleCap transferReport.JournalPath
                        return proof |> Result.map (fun report -> transferReport, report)
            })
      match (work ()).GetAwaiter().GetResult() with
      | Error errs ->
          printErrors Console.Error errs
          6
      | Ok (transferReport, report) ->
          // The run aggregate records WHERE the proof's ledger lives (wave B4a).
          transferReport.JournalPath
          |> Option.iter (fun p ->
              match CaptureJournal.digestOfFile p with
              | Some digest -> Shell.recordLedgerRef (Run.JournalRef (digest, p))
              | None -> ())
          let artifact = FidelityCompareRun.toJsonString report
          if args.AsJson then
              printfn "%s" artifact
          else
              let payload : Voice.Payload =
                  Map.ofList
                      [ "rows",  box (RowFidelityReport.rowsCompared report)
                        "kinds", box (List.length report.Kinds)
                        "diffs", box (RowFidelityReport.differenceTotal report)
                        "ledger", box (report.Interventions |> Option.defaultValue "")
                        "tolerances", box (String.concat ", " report.TolerancesInForce)
                        "artifactPath", box "fidelity.rows.json" ]
              let verdictCode =
                  if RowFidelityReport.agrees report then "fidelity.rows.matched" else "fidelity.rows.diverged"
              TtyRenderer.renderVoicedTo Console.Out verdictCode payload
              printfn ""
              printfn "  The stand-in: a per-run container database carrying the target's shape (the model's logical rendition), loaded by the transfer machinery (wipe-and-load, journaled, FKs re-trusted), reaped after the proof."
              // The load's own named erasures — each dropped row surfaces in
              // the compare as a missing row; the WHY is said here.
              if not (List.isEmpty transferReport.SkippedReferences) then
                  printfn "  The load dropped %d row(s) (a relationship pointed at an unmatched record) — each surfaces below as a missing row." (List.length transferReport.SkippedReferences)
              FidelityCompareRun.render report |> List.iter (fun line -> printfn "%s" line)
          tryWriteArtifact "fidelity.rows.json" artifact
          // RT-10 — a FLOW-SCOPED copy beside the journal, so the estate board
          // (readiness.estate.fidelityFlow) reads THIS flow's proof
          // unambiguously (the cwd copy is overwritten by whichever flow last
          // ran; the scoped copy is this flow's, and its mtime is its age).
          (try IO.Directory.CreateDirectory journalDir |> ignore with _ -> ())
          tryWriteArtifact (IO.Path.Combine(journalDir, "fidelity.rows.json")) artifact
          // P2-S2 — the PORTABLE proof manifest (`--capture <path>`): the SOURCE
          // side's per-kind RowDigestFold digests + capture provenance, written
          // for a later OFFLINE reconcile (`check fidelity --against`, P2-S3). The
          // manifest is the source's fingerprint at proof time — meaningful green
          // OR red (the source is the source). An explicitly-requested artifact,
          // so a write failure is a NAMED error, never a silent drop.
          (match args.Capture with
           | Some capturePath ->
               let manifest = ProofManifest.ofReport nowUtc currentModelHash report
               match ProofManifest.write capturePath manifest with
               | Ok () ->
                   if not args.AsJson then
                       printfn "  Proof manifest captured: %s — %d kind(s). Reconcile a target against it (no live source needed) with `check fidelity --against %s --target <ref>`." capturePath (List.length manifest.Kinds) capturePath
               | Error errs -> printErrors Console.Error errs
           | None -> ())
          // Cache the GREEN proof (wave B6) — the source fingerprints from the
          // pre-probe + the model hash; a non-green result CLEARS any prior entry
          // so a residual never short-circuits a future run.
          // P1-S1 — only the DDL staging owns the incremental cache; a Dacfx
          // run never writes (nor clears) it, since DacFx≡DDL is under proof.
          (match storeRoot with
           | Some root when args.Stage = StagingMode.Ddl ->
               if RowFidelityReport.agrees report then
                   match sourceFps with
                   | Some fps ->
                       FidelityProofCache.write root args.Flow
                           { ModelHash = currentModelHash
                             Fingerprints = fps
                             RowsCompared = RowFidelityReport.rowsCompared report
                             DifferenceTotal = RowFidelityReport.differenceTotal report
                             WrittenAtUtc = nowUtc }
                   | None -> ()
               else FidelityProofCache.clear root args.Flow |> ignore
           | _ -> ())
          if RowFidelityReport.agrees report then 0 else 5
    // The verb: a fresh cached proof short-circuits the container; else it runs.
    match cacheHit with
    | Some cached ->
        // CACHE HIT — the last proof stands; the container proof (and Docker) is
        // skipped. Touch the flow-scoped proof so the estate board reads this
        // still-valid proof as current (RT-10 staleness = proof mtime vs age).
        let ageDays = max 0 (int (nowUtc - cached.WrittenAtUtc).TotalDays)
        let cachePathText = storeRoot |> Option.map (fun root -> FidelityProofCache.cachePath root args.Flow) |> Option.defaultValue "(disabled)"
        let scoped = IO.Path.Combine(journalDir, "fidelity.rows.json")
        (try (if IO.File.Exists scoped then IO.File.SetLastWriteTimeUtc(scoped, nowUtc.UtcDateTime)) with _ -> ())
        if args.AsJson then
            let o = System.Text.Json.Nodes.JsonObject()
            o.["cached"] <- System.Text.Json.Nodes.JsonValue.Create true
            o.["flow"] <- System.Text.Json.Nodes.JsonValue.Create args.Flow
            o.["agrees"] <- System.Text.Json.Nodes.JsonValue.Create true
            o.["rowsCompared"] <- System.Text.Json.Nodes.JsonValue.Create cached.RowsCompared
            o.["differenceTotal"] <- System.Text.Json.Nodes.JsonValue.Create cached.DifferenceTotal
            o.["provenAtUtc"] <- System.Text.Json.Nodes.JsonValue.Create(cached.WrittenAtUtc.ToString "O")
            printfn "%s" (o.ToJsonString(System.Text.Json.JsonSerializerOptions(WriteIndented = true)))
        else
            printfn "  Fidelity proof for flow '%s' — CACHED GREEN: %s row(s) proven byte-identical, unchanged since %s." args.Flow (humaneRows cached.RowsCompared) (ageText ageDays)
            printfn "  The model shape and the source's per-kind fingerprints have not moved since the proof ran; the container proof is skipped (an in-place edit is invisible to that check — re-prove with --refresh to be certain)."
            printfn "  Proof cache: %s — clear with --refresh, or delete the file." cachePathText
        0
    | None -> runProof ()

/// P2-S3 — `check fidelity --against <manifest> --target <ref>`. THE OFFLINE
/// RECONCILE: read a portable proof manifest (a source estate's captured per-kind
/// digests + provenance), read back the target the operator applied THEMSELVES,
/// and prove each kind byte-identical WITHOUT the live source. Per-kind pass/fail
/// (the manifest carries digests, not rows — the drill-down escalates to
/// `check data --rows`). Read-only. Exits: 0 all kinds match · 5 a kind diverged
/// (named) · 6 the manifest is unreadable, the model's shape has moved from the
/// manifest's basis, or the target could not be read.
let runFidelityAgainst (model: Catalog) (args: CheckFidelityAgainstArgs) : int =
    match ProofManifest.tryRead args.ManifestPath with
    | None ->
        // §14 — the manifest is the reconcile's whole input; a torn/absent/foreign
        // one reconciles to NOTHING (fail-closed), surfaced as a named refusal.
        printErrors Console.Error
            [ ValidationError.create "cli.fidelity.against.manifestUnreadable"
                (sprintf "projection check fidelity --against: the manifest '%s' is absent, unreadable, or not a valid v%d rowDigestFold manifest." args.ManifestPath ProofManifest.Version) ]
        6
    | Some manifest ->
        // The alignment gate: the manifest's digests were captured under a
        // specific model shape; reconciling a target read under a DIFFERENT shape
        // would compare incomparable bytes. A mismatch is a NAMED refusal.
        if FidelityProofCache.modelHash model <> manifest.ModelHash then
            printErrors Console.Error
                [ ValidationError.create "cli.fidelity.against.modelMismatch"
                    (sprintf "projection check fidelity --against: the manifest was captured from '%s' under a DIFFERENT model shape than the one now configured — the alignment basis has moved, so the digests are not comparable. Re-capture the manifest against the current model, or point `model` at the shape it was captured under." manifest.SourceLabel) ]
            6
        else
        let logical = CatalogRendition.logical model
        let entries : SourceDigestEntry list =
            manifest.Kinds
            |> List.map (fun k -> { KindKey = k.Kind; KindName = k.KindName; Digest = k.Digest })
        let work () : Task<Result<RowFidelityReport>> =
            task {
                try
                    use target = new Microsoft.Data.SqlClient.SqlConnection(args.TargetConn)
                    do! target.OpenAsync()
                    let! report =
                        FidelityCompareRun.reconcileAgainstDigests
                            target manifest.SourceLabel args.TargetLabel manifest.TolerancesInForce logical entries
                    return Ok report
                with ex ->
                    return
                        Result.failureOf
                            (ValidationError.createWithMetadata
                                "cli.fidelity.against.targetUnreadable"
                                "The target could not be read for the reconcile (unreachable, or a kind's table is absent)."
                                (Map.ofList [ "target", Some args.TargetLabel; "detail", Some ex.Message ]))
            }
        match (work ()).GetAwaiter().GetResult() with
        | Error errs ->
            printErrors Console.Error errs
            6
        | Ok report ->
            let artifact = FidelityCompareRun.toJsonString report
            if args.AsJson then
                printfn "%s" artifact
            else
                let payload : Voice.Payload =
                    Map.ofList
                        [ "rows",  box (RowFidelityReport.rowsCompared report)
                          "kinds", box (List.length report.Kinds)
                          "diffs", box (RowFidelityReport.differenceTotal report)
                          "ledger", box ""
                          "tolerances", box (String.concat ", " report.TolerancesInForce)
                          "artifactPath", box "fidelity.rows.json" ]
                let verdictCode =
                    if RowFidelityReport.agrees report then "fidelity.rows.matched" else "fidelity.rows.diverged"
                TtyRenderer.renderVoicedTo Console.Out verdictCode payload
                printfn ""
                printfn "  Reconciled the target '%s' against the manifest captured from '%s' — per-kind pass/fail, NO live source. Escalate to `check data --rows` (both live) to name differing rows." args.TargetLabel manifest.SourceLabel
                FidelityCompareRun.render report |> List.iter (fun line -> printfn "%s" line)
            tryWriteArtifact "fidelity.rows.json" artifact
            if RowFidelityReport.agrees report then 0 else 5
