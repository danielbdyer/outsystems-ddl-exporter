namespace Projection.Tests

// TEST SUPPORT — THE BLESS-ALL RUNNER (2026-07-10, the transfer-manifest
// program, slice 4b): the per-act consent gate is ALWAYS ON for the peer
// transfer path, so every witness that EXECUTES a peer transfer must bless
// the acts its run performs. The gate's refusal metadata carries the FULL
// sorted unblessed set (token → "fingerprint — statement"), so the honest
// test-support flow is two-pass: run once, bless every act at exactly the
// fingerprints the refusal names, run again. A witness that is NOT about
// consent stays one line (`blessAllAndRun`); the consent witness blesses
// selectively by hand.

open System.Threading.Tasks
open Projection.Core
open Projection.Pipeline

[<RequireQualifiedAccess>]
module TransferActs =

    /// The blessings an `actUnblessed` refusal names: one per metadata entry,
    /// each at the exact fingerprint the gate derived. An entry whose
    /// substrate would not read carries no fingerprint ("unread — …") and is
    /// skipped — it cannot be blessed, which is the gate's point.
    let blessingsOf (refusal: ValidationError) : WriteSignoff.ActBlessing list =
        refusal.Metadata
        |> Map.toList
        |> List.choose (fun (token, value) ->
            value
            |> Option.bind (fun v ->
                match v.Split([| " — " |], System.StringSplitOptions.None) with
                | parts when parts.Length >= 1 ->
                    ActConsent.parseFingerprint parts.[0]
                    |> Option.map (fun fp -> WriteSignoff.blessed token fp)
                | _ -> None))

    /// Run a peer-execute leg under the always-on consent gate: the first
    /// pass refuses `transfer.writeSignoff.actUnblessed`, the blessings are
    /// taken verbatim from the refusal's metadata, and the second pass runs
    /// blessed. Any OTHER refusal (or a first-pass success, e.g. a DryRun)
    /// passes straight through — the helper never masks a real failure.
    let blessAllAndRun
        (run: WriteSignoff.ActBlessing list -> Task<Result<Transfer.TransferReport>>)
        : Task<Result<Transfer.TransferReport>> =
        task {
            let! first = run []
            match first with
            | Error es when es |> List.exists (fun e -> e.Code = "transfer.writeSignoff.actUnblessed") ->
                let refusal = es |> List.find (fun e -> e.Code = "transfer.writeSignoff.actUnblessed")
                return! run (blessingsOf refusal)
            | other -> return other
        }
