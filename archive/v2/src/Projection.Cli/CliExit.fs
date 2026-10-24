[<RequireQualifiedAccess>]
module Projection.Cli.CliExit

open Projection.Core
open Projection.Pipeline

/// The single, total exit-code classifier for the CLI's artifact / read-only
/// verbs — `synth-load`, `profile`, `synth-correct`, and the `slice-extract` /
/// `slice-apply` / `slice-reset` / `slice-run` family. These verbs refuse with
/// their OWN code families (`slice.*` / `synthetic.*` / `profile.*` /
/// `correction.*` / `model.*`) — codes OUTSIDE the `Preflight.classify` gate
/// vocabulary (`transfer.*` / `migrate.*`).
///
/// Before this module each face hand-rolled a prefix `if/elif`-over-`anyCode`
/// ladder mapping those codes to an exit, copied six times — and the copies had
/// DRIFTED (the recurring "law honored by vigilance" smell, Recon #3 Concern 1):
///   - `synthetic.cdcTrackedSink` exited 7, but every other `*.cdcTrackedSink`
///     (the migrate/transfer gate convention) exits 9;
///   - `slice.apply.grantProbeFailed` exited 6, but the grant axis exits 7
///     everywhere else;
///   - `slice-apply` mapped `slice.apply.cdcTrackedSink`/`insufficientGrant` to
///     7, while `slice-run` flattened the whole `slice.apply` prefix to 6 — the
///     two slice verbs disagreed on the SAME codes.
/// This classifier collapses the six copies into one greppable, total table and
/// RECONCILES the three divergences onto the gate convention (so a CLI refusal
/// lands on the SAME exit as the equivalent migrate/transfer gate refusal). A
/// new refusal can no longer fall through inconsistently.
///
/// The exit axes match the published contract (`Program.usageLines`, "Exit
/// codes") and the `Preflight` gate convention:
///   1  artifact write/emit failed (an output-IO failure)
///   2  bad input — model/spec/golden/schema parse; a missing/malformed ref
///   6  connection axis — resolve / open a source or target
///   7  permission axis — insufficient grant / grant-probe failed
///   9  refused, fail-loud — a CDC-tracked sink
///   3  any other refusal — the named generic default (fail loud, never 0)

/// Classify ONE refusal code to its CLI exit. A gate-vocabulary code
/// (`transfer.*` / `migrate.*`), should one reach a CLI face, defers to the
/// single `Preflight.classify` seam so the gate convention is never re-stated;
/// the CLI-specific families are classified here. Total — an unrecognized code
/// is the named generic refusal (3), never a silent 0.
let classifyCode (code: string) : int =
    match Preflight.classify code with
    | exit, label when label <> Preflight.UnclassifiedRefusal -> exit
    | _ ->
        // The CLI verbs' own vocabulary. Order matters: the specific axes
        // (write / CDC / grant / connection) are tested BEFORE the generic
        // `slice.` / `model.` parse bucket, so e.g.
        // `slice.apply.grantProbeFailed` lands on the grant axis (7), not the
        // generic slice-parse 2.
        let has (sub: string) = code.Contains sub
        if has ".writeFailed" || has ".emitFailed" then 1
        elif has "cdcTrackedSink" then 9                              // reconciled: synth-load was 7
        elif has "insufficientGrant" || has "grantProbeFailed" then 7 // reconciled: slice.apply.grantProbeFailed was 6
        elif has "connection" then 6                                  // `connection`, `connectionSpec`
        elif code.StartsWith "model."
             || code.StartsWith "synthetic.profileRef"
             || code.StartsWith "slice." then 2
        else 3

/// Classify a refusal (a non-empty `ValidationError list`) to its CLI exit by
/// its PRIMARY (first) error — the same "the first error is the gate's primary
/// refusal" convention `Preflight.refusalOf` pins. The six replaced ladders each
/// scanned ALL errors under a per-verb tier order, but those orders were
/// mutually inconsistent (no single scan reproduces all six), so the first-error
/// rule is the principled, seam-aligned unification — and on the common
/// single-error refusal it is identical to the old `anyCode`. An empty list
/// (which `Result.failure` forbids) is the generic refusal (3), never 0.
let classify (errors: ValidationError list) : int =
    match errors with
    | e :: _ -> classifyCode e.Code
    | []     -> 3
