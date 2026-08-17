namespace Projection.Core

/// The round-trip canary's verdict for one run — the R6 cutover gate's unit
/// of evidence (`DECISIONS 2026-05-22 — R6`: N=10 consecutive green canaries
/// + operator sign-off). align-III.3 retires the `"green"` / `"red"` string
/// pair the run aggregate and the cross-run ledger carried: the gate reads a
/// VALUE now, not a literal a typo in one of two independent sites could
/// silently break (the audit's a5 finding).
///
/// Three variants — exactly the states the canary produces today. A fourth,
/// `Aborted` (a canary that BEGAN but reached no verdict), is a NAMED
/// DEFERRAL: no run emits a canary-abort signal into the `LogSink` envelope
/// stream yet (`canary.started` is Voice copy only, never accumulated), so an
/// `Aborted` variant would have no producer — "IR grows under evidence". Its
/// re-open trigger: the first run to journal a started-but-unconcluded canary.
type CanaryVerdict =
    /// The round-trip matched — `canary.diffEmpty` fired (a green canary).
    | Green
    /// The round-trip diverged — `canary.divergence` fired (a red canary).
    | Red
    /// No canary leg in this run — pending BY ABSENCE, structurally distinct
    /// from a recorded `Red`: the readiness gauge skips it, never resets the
    /// green streak on it.
    | NotRun

[<RequireQualifiedAccess>]
module CanaryVerdict =

    /// The wire token when the verdict rides the ledger / run JSON. `NotRun`
    /// writes NO token (the field absent or null — byte-identical to the
    /// prior `None`), so a pre-III.3 store round-trips unchanged; `Green` /
    /// `Red` keep their exact bytes.
    let tokenOpt (v: CanaryVerdict) : string option =
        match v with
        | Green -> Some "green"
        | Red -> Some "red"
        | NotRun -> None

    /// Read a wire token back. Absent / null ⇒ `NotRun`; an UNKNOWN token ⇒
    /// `NotRun` too — the ledger is opt-in and forward-compatible, so a
    /// future verdict an older reader cannot name reads as "no verdict I
    /// recognize", never a false green or red (the safe direction).
    let ofTokenOpt (token: string option) : CanaryVerdict =
        match token with
        | Some "green" -> Green
        | Some "red" -> Red
        | Some _ | None -> NotRun

    /// Does this verdict count toward the R6 green streak?
    let isGreen (v: CanaryVerdict) : bool = (v = Green)

    /// Did a canary leg run at all (Green / Red), or was there none?
    let ran (v: CanaryVerdict) : bool = (v <> NotRun)

    /// The board's display token — the recorded token, or `—` for `NotRun`.
    let display (v: CanaryVerdict) : string =
        match tokenOpt v with
        | Some t -> t
        | None -> "—"
