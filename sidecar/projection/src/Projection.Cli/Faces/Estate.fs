module Projection.Cli.Faces.Estate
// LINT-ALLOW-FILE: CLI run-face operator-facing prose + Voice payload boxing at the terminal CLI boundary; the structural surface is the typed MovementSpec / Estate report / Voice catalog, BCL primitives only at this terminal text edge.

// The estate-convergence face (`check estate` — CHAPTER_ESTATE_OPEN.md;
// DECISIONS 2026-07-15 "The estate chapter opens"). Read-only: resolves the
// unification target (the agreed environment's OSSYS shape, or the authored
// model under `--against model`) and every confirm environment (OSSYS
// identity — espace-safe; a profile failure degrades to advisory-silent,
// never aborts), rolls the `Estate.EstateReport`, renders the verdict through
// the Voice catalog with the board beneath it, writes `estate.json`, and
// exits 0 (unified) / 5 (diverged) / 6 (an environment could not be read —
// the estate verdict needs every named environment; no partial estate).
//
// The evidence store (wave A2.5): each environment's profile rides the
// pay-once store when its fingerprints hold — `--refresh` forces the
// re-capture, `--offline` reuses unprobed and downgrades to advisory, and
// every acquisition path lands on the report as `EvidenceProvenance` (the
// masthead line, the estate.json record, and the pre-verdict notice are one
// fact). A probe or store failure is a cache miss, never a downgrade: the
// run profiles live and the cause prints as an advisory.

open System
open Projection.Core
open Projection.Pipeline
open Projection.Targets.OperationalDiagnostics
open Projection.Cli
open Projection.Cli.OperatorConsole

/// The §14 unreadable-operand surface: the Voice line names the environment
/// and the cause; the raw errors follow for the substantiation.
let private unreadable (label: string) (errs: ValidationError list) : int =
    let reason =
        match errs with
        | e :: _ -> e.Message
        | [] -> "the read returned no catalog"
    TtyRenderer.renderVoicedTo Console.Error "estate.envUnreadable"
        (Map.ofList [ "env", box label; "reason", box reason ])
    printErrors Console.Error errs
    6

/// The provenance's pre-verdict notice — derived from the same value the
/// masthead renders, so the notice and the board cannot disagree.
let private noticeOf (basis: Estate.EnvBasis) : (string * Voice.Payload) option =
    match basis.Provenance with
    | Estate.EvidenceProvenance.Cached (_, age, kinds) ->
        Some ("estate.evidence.cached",
              Map.ofList [ "env", box basis.Env; "age", box (string age); "kinds", box kinds ])
    | Estate.EvidenceProvenance.Refreshed moved ->
        Some ("estate.evidence.stale",
              Map.ofList [ "env", box basis.Env; "moved", box (List.length moved) ])
    | Estate.EvidenceProvenance.Offline (_, age) ->
        Some ("estate.evidence.offline",
              Map.ofList [ "env", box basis.Env; "age", box (string age) ])
    | Estate.EvidenceProvenance.Live
    | Estate.EvidenceProvenance.Absent -> None

let runCheckEstate (args: CheckEstateArgs) : int =
    let nowUtc = DateTimeOffset.UtcNow
    let store = EstateEvidenceStore.storeDir ()
    let ageDaysOf (captured: DateTimeOffset) : int =
        max 0 (int ((nowUtc - captured).TotalDays))
    // The unification target — the run states which basis it used (the
    // masthead's first line; DECISIONS 2026-07-15).
    let targetOperand =
        match args.Target with
        | EstateTargetSource.AgreedEnv _ -> Estate.TargetOperand.AgreedEnv args.TargetLabel
        | EstateTargetSource.AuthoredModel _ -> Estate.TargetOperand.AuthoredModel args.TargetLabel
    let targetCatalog =
        match args.Target with
        | EstateTargetSource.AgreedEnv connRef ->
            (Source.read (Source.ofOssys connRef)).GetAwaiter().GetResult()
        | EstateTargetSource.AuthoredModel (modelOssys, modelFile) ->
            (ModelResolution.resolveCatalog modelOssys modelFile).GetAwaiter().GetResult()
    match targetCatalog with
    | Error errs -> unreadable args.TargetLabel errs
    | Ok target ->
        // The posture (wave A6): the loaded config's tightening section
        // binds against the resolved target catalog — logical names are
        // espace-stable, so the bound keys match every environment's
        // evidence. A posture the operator wrote that cannot bind is a
        // NAMED config-shape refusal (exit 2), never silently ignored.
        let postureBinding =
            TighteningBinding.fromConfig target args.Tightening
            |> Result.map (fun bound ->
                let relaxedRefs, relaxedAttrs = EstatePosture.activeOf bound
                ({ RepairBand = args.RepairBand |> Option.defaultValue Estate.repairBandDefault
                   RelaxedReferences = relaxedRefs
                   RelaxedAttributes = relaxedAttrs } : Estate.Posture))
        let postureErrors = match postureBinding with Error errs -> errs | Ok _ -> []
        if not (List.isEmpty postureErrors) then
            printErrors Console.Error postureErrors
            2
        else

        let posture =
            match postureBinding with
            | Ok p -> p
            | Error _ -> Estate.Posture.defaults   // unreachable behind the guard above
        // One environment's data-plane evidence under the store discipline.
        // A store/probe failure prints as an advisory and the run proceeds
        // live — the evidence stays fresh; only the pay-once saving is lost.
        let acquireEvidence
            (label: string)
            (refStr: string)
            (source: Source.Source)
            (catalog: Catalog)
            : Profile option * Estate.EvidenceProvenance =
            let liveProfile () : Profile option =
                match Source.profile source with
                | None -> None
                | Some acquire ->
                    match (acquire catalog).GetAwaiter().GetResult() with
                    | Ok p -> Some p
                    | Error _ -> None
            let liveOrAbsent () : Profile option * Estate.EvidenceProvenance =
                match liveProfile () with
                | Some p -> Some p, Estate.EvidenceProvenance.Live
                | None -> None, Estate.EvidenceProvenance.Absent
            match store with
            | None -> liveOrAbsent ()
            | Some root ->
                match args.Evidence with
                | EstateEvidenceMode.Offline ->
                    match EstateEvidenceStore.load root label with
                    | Some e ->
                        Some e.Profile,
                        Estate.EvidenceProvenance.Offline (e.CapturedAtUtc, ageDaysOf e.CapturedAtUtc)
                    | None -> None, Estate.EvidenceProvenance.Absent
                | mode ->
                    let forced =
                        match mode with
                        | EstateEvidenceMode.Refresh None -> true
                        | EstateEvidenceMode.Refresh (Some envs) -> List.contains label envs
                        | _ -> false
                    let saveAdvisory (profile: Profile) (fps: KindFingerprint list) : unit =
                        match EstateEvidenceStore.save nowUtc root label profile fps with
                        | Ok () -> ()
                        | Error errs -> printErrors Console.Error errs
                    match (EstateEvidenceStore.probeLive refStr catalog).GetAwaiter().GetResult() with
                    | Error errs ->
                        printErrors Console.Error errs
                        liveOrAbsent ()
                    | Ok live ->
                        let cached = if forced then None else EstateEvidenceStore.load root label
                        match cached with
                        | Some c ->
                            let stale = EstateEvidenceStore.staleKinds c.Fingerprints live
                            if List.isEmpty stale then
                                Some c.Profile,
                                Estate.EvidenceProvenance.Cached
                                    (c.CapturedAtUtc, ageDaysOf c.CapturedAtUtc, List.length live)
                            else
                                match liveProfile () with
                                | Some p ->
                                    saveAdvisory p live
                                    let moved =
                                        stale
                                        |> List.map (fun key ->
                                            match Catalog.tryFindKind key catalog with
                                            | Some k -> Name.value k.Name
                                            | None -> SsKey.rootOriginal key)
                                    Some p, Estate.EvidenceProvenance.Refreshed moved
                                | None -> None, Estate.EvidenceProvenance.Absent
                        | None ->
                            // A first capture, or a forced refresh — either
                            // way the evidence is this run's, then stored.
                            match liveProfile () with
                            | Some p ->
                                saveAdvisory p live
                                Some p, Estate.EvidenceProvenance.Live
                            | None -> None, Estate.EvidenceProvenance.Absent
        // Each confirm environment: its OSSYS catalog (schema) + the
        // data-plane evidence pair. A profile failure degrades to
        // advisory-silent — the schema verdict still leads; the masthead
        // says the data plane observed nothing.
        let resolveEnv
            (label: string, refStr: string)
            : string * Result<(string * Compare.Operand) * Estate.EvidenceProvenance> =
            let source = Source.ofOssys refStr
            match (Source.read source).GetAwaiter().GetResult() with
            | Error errs -> label, Result.failure errs
            | Ok catalog ->
                let profile, provenance = acquireEvidence label refStr source catalog
                label,
                Result.success
                    ((label, ({ Label = label; Catalog = catalog; Profile = profile } : Compare.Operand)),
                     provenance)
        let resolved = args.Confirm |> List.map resolveEnv
        match resolved |> List.tryPick (fun (label, r) -> match r with Error es -> Some (label, es) | Ok _ -> None) with
        | Some (label, errs) -> unreadable label errs
        | None ->
            let outcomes = resolved |> List.choose (fun (_, r) -> match r with Ok v -> Some v | Error _ -> None)
            let envs = outcomes |> List.map fst
            let provenanceByEnv =
                outcomes |> List.map (fun ((label, _), provenance) -> label, provenance) |> Map.ofList
            let storeBasis =
                match store with
                | Some dir -> Estate.EvidenceStoreBasis.Enabled dir
                | None -> Estate.EvidenceStoreBasis.Disabled
            let computed =
                Estate.computeWith posture targetOperand target envs
                |> Estate.withEvidence storeBasis provenanceByEnv
            // The remediation artifacts (wave A5): one file per environment
            // carrying REPAIR-lane blocks — written BEFORE the report is
            // stamped, so the board's levers, the ARTIFACTS index, and the
            // files on disk are one run's facts. The provenance header
            // makes the wrong-environment mistake structurally detectable.
            let connByEnv = args.Confirm |> Map.ofList
            let remediationArtifacts =
                envs
                |> List.choose (fun (label, operandValue) ->
                    let blocks =
                        EstateRemediation.blocksFor label
                            (Readiness.toLogicalShape operandValue.Catalog)
                            operandValue.Profile computed
                    if List.isEmpty blocks then None
                    else
                        let headerLines =
                            EstateRemediation.header label
                                (connByEnv |> Map.tryFind label |> Option.map Source.resolveConn |> Option.defaultValue "")
                                nowUtc
                        let file = EstateRemediation.fileNameFor label
                        IO.File.WriteAllText(file, RemediationEmitter.emitEstate headerLines blocks)
                        Some (file, List.length blocks))
            // The interim posture's artifacts (wave A6): every RELAX-lane
            // PROPOSED finding resolves to one overlay entry + one reopen
            // probe — written before stamping, so the board's levers, the
            // ARTIFACTS index, and the files are one run's facts
            // (π-coherence: report ⇔ overlay ⇔ probes, keyed alike).
            let relaxations =
                EstatePosture.relaxationsFor (Readiness.toLogicalShape target) computed
            if not (List.isEmpty relaxations) then
                let note =
                    sprintf "projection:estate-overlay generated=%s target=%s — suggested config edits; the merge is an operator edit, and the engine never applies it."
                        (nowUtc.ToString "o") args.TargetLabel
                IO.File.WriteAllText("estate.overlay.json", EstateOverlayEmitter.emitOverlay note relaxations)
                IO.File.WriteAllText(
                    "estate.probes.sql",
                    EstateOverlayEmitter.emitProbes
                        [ sprintf "-- projection:estate-probes generated=%s target=%s" (nowUtc.ToString "o") args.TargetLabel ]
                        relaxations)
            let report =
                computed
                |> Estate.withRemediation remediationArtifacts
                |> (fun r ->
                    if List.isEmpty relaxations then r
                    else Estate.withOverlay (List.length relaxations) r)
            let artifact = Estate.toJsonString report
            if args.AsJson then
                printfn "%s" artifact
            else
                // The provenance notices lead — the run's evidence basis is
                // said before any verdict stands on it (RT-7); the posture
                // notice follows them (the overlay exists before the
                // verdict cites the relaxation lane).
                for basis in report.Bases do
                    match noticeOf basis with
                    | Some (code, payload) -> TtyRenderer.renderVoicedTo Console.Out code payload
                    | None -> ()
                (match report.OverlayEntries with
                 | Some entries when entries > 0 ->
                     TtyRenderer.renderVoicedTo Console.Out "estate.overlay"
                         (Map.ofList [ "relaxations", box entries ])
                 | _ -> ())
                let laneCount lane =
                    Estate.laneCounts report
                    |> List.tryFind (fun (l, _) -> l = lane)
                    |> Option.map snd
                    |> Option.defaultValue 0
                let payload : Voice.Payload =
                    Map.ofList
                        [ "envs",         box (List.length report.Bases)
                          "total",        box (List.length report.Findings)
                          "decide",       box (laneCount EstateLane.Decide)
                          "repair",       box (laneCount EstateLane.Repair)
                          "relax",        box (laneCount EstateLane.Relax)
                          "watch",        box (laneCount EstateLane.Watch)
                          "forks",        box (report.Findings |> List.filter (fun f -> f.Fork) |> List.length)
                          "artifactPath", box "estate.json" ]
                let verdictCode =
                    match report.Verdict with
                    | Estate.Verdict.Unified -> "estate.unified"
                    | Estate.Verdict.Converging -> "estate.diverged"
                    | Estate.Verdict.Forked -> "estate.forked"
                TtyRenderer.renderVoicedTo Console.Out verdictCode payload
                printfn ""
                Estate.render report |> List.iter (fun line -> printfn "%s" line)
            IO.File.WriteAllText("estate.json", artifact)
            if Estate.isUnified report then 0 else 5
