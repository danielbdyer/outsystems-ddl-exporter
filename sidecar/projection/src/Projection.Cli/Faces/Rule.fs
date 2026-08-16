module Projection.Cli.Faces.Rule
// LINT-ALLOW-FILE: CLI run-face operator-facing prose + Voice payload boxing at the terminal CLI boundary; the structural surface is the typed MovementSpec / RulingStore / Voice catalog, BCL primitives only at this terminal text edge.

// The ruling face — `projection rule <finding-key> (--confirm | --reject)
// --by <name> [--rationale <text>] [--format json]` (align-II.6; A53). The
// verb records an `OperatorRuling` in the keyed ruling store under the SAME
// estate root the board reads (`RulingStore` — replace-by-key, atomic write)
// and renders the recorded judgment back through the one-mint copy the board
// uses (`Estate.rulingText`), so the verb's echo and the board's line can
// never drift. Record + render ONLY (the align-II.0 standing ruling):
// applying a ruling to policy or model stays the operator's move — the
// board still counts the finding until the world actually changes.

open System
open System.Text.Json.Nodes
open Projection.Core
open Projection.Pipeline
open Projection.Cli
open Projection.Cli.OperatorConsole

/// The machine lens (`--format json`): the recorded ruling as one envelope —
/// the same fields the ruling document persists (date at full precision;
/// the human echo renders date-only).
let private rulingJson (store: string) (ruling: OperatorRuling<FindingKey>) : string =
    let o = JsonObject()
    o["key"] <- JsonValue.Create(FindingKey.text ruling.Subject)
    o["verdict"] <- JsonValue.Create(RulingVerdict.token ruling.Verdict)
    o["by"] <- JsonValue.Create ruling.By
    o["at"] <- JsonValue.Create(ruling.At.ToString("O", Globalization.CultureInfo.InvariantCulture))
    (match ruling.Rationale with
     | Some why -> o["rationale"] <- JsonValue.Create why
     | None -> ())
    o["store"] <- JsonValue.Create store
    o.ToJsonString()

/// Record one operator ruling and render it back. Refusal channels:
/// no configured estate store (the rulings need a durable keyed home —
/// exit 2, the config-prerequisite convention) and a store write failure
/// (exit 1, the write axis). The plan already validated the key, the
/// verdict, the author, and the rationale — the constructor's own refusals
/// are impossible-state guards here, routed loud if ever reached.
let runRule (args: RuleArgs) : int =
    // The boundary supplies the clock (the Episode.fs canonical pattern) —
    // one reading, stamped on the ruling.
    let now = DateTimeOffset.UtcNow
    match EstateEvidenceStore.storeDir () with
    | None ->
        printErrors Console.Error
            [ ValidationError.create "rule.noStore"
                "projection rule: no estate store is configured — PROJECTION_ESTATE_DIR (or the ledger directory's estate child) holds the recorded rulings." ]
        CliExit.classifyCode "rule.noStore"
    | Some root ->
        match OperatorRuling.create args.Key args.Verdict (Some (BasisAnchor.FindingKey args.Key)) args.By now args.Rationale None with
        | Error errs ->
            printErrors Console.Error errs
            CliExit.classify errs
        | Ok ruling ->
            match RulingStore.save root ruling with
            | Error e ->
                printErrors Console.Error
                    [ ValidationError.create "rule.writeFailed" (RulingStore.describe e) ]
                CliExit.classifyCode "rule.writeFailed"
            | Ok () ->
                if args.AsJson then
                    printfn "%s" (rulingJson root ruling)
                else
                    TtyRenderer.renderVoicedTo Console.Out "rule.recorded"
                        (Map.ofList
                            [ "subject", box (FindingKey.readableLabel args.Key)
                              "verdict", box (RulingVerdict.token args.Verdict)
                              "by", box args.By ] : Voice.Payload)
                    // The one-mint board copy, echoed at record time — the
                    // verb's answer and the board's line are one sentence.
                    printfn "  %s" (Estate.rulingText ruling)
                dumpBench "rule"
                0
