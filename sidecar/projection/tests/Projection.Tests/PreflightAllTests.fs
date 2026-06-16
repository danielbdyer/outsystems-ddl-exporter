module Projection.Tests.PreflightAllTests

open System.Threading.Tasks
open Xunit
open Projection.Core
open Projection.Pipeline

// G0 — `Preflight.all` mandatory (THE_USE_CASE_ONTOLOGY.obligations.md §6,
// AC-G0 / OB-G0.1-3; acceptance ~114). The criterion: ANY `--execute` path
// passes iff a SINGLE mandatory `Preflight.all` composes the full gate suite,
// so no caller can skip a gate. §8 names the hazard the composition risks — a
// `code→(exit,label)` reporting refactor that REGRESSES the distinct per-gate
// exit codes the verbs produce today.
//
// These are the PURE (DB-free) witnesses for the reality leg. Each test cites
// the G0 sub-criterion it enforces.

[<RequireQualifiedAccess>]
module private Gate =

    // The verbs hand `Preflight.all`/`allReporting` a list of `Task<Result<unit>>`
    // — already-started (hot) tasks. The composition's guarantee is therefore
    // about which gates it AWAITS (short-circuit = does not await the rest) and
    // WHICH refusal it returns (the first). A hot `task { trace.Add … }` records
    // its side effect at CONSTRUCTION, before the composition awaits anything, so
    // a trace over hot tasks cannot witness short-circuit. To witness "did not
    // await gate N" faithfully, the side effect that proves an await must live in
    // a CONTINUATION gated on a trigger the harness releases in order — a
    // `TaskCompletionSource` whose completion records into the trace. A gate the
    // composition never awaits is never released, so its name never lands in the
    // trace even though the task object exists.

    /// A cold gate paired with a release trigger. `Task` is what the composition
    /// awaits; `Release` completes it (recording into the trace) and is only
    /// invoked by the harness when the composition reaches that gate.
    type Pending =
        {
            Task    : Task<Result<unit>>
            Release : unit -> unit
        }

    let private pending
        (trace: System.Collections.Generic.List<string>)
        (name: string)
        (outcome: Result<unit>)
        : Pending =
        let tcs = System.Threading.Tasks.TaskCompletionSource<Result<unit>>()
        { Task = tcs.Task
          Release = fun () ->
              trace.Add name
              tcs.SetResult outcome }

    /// A gate that passes — completes `Ok ()` when released.
    let pass (trace: System.Collections.Generic.List<string>) (name: string) : Pending =
        pending trace name (Ok ())

    /// A gate that refuses with the given `ValidationError.Code` when released.
    let refuse
        (trace: System.Collections.Generic.List<string>)
        (name: string)
        (code: string)
        : Pending =
        pending trace name (Result.failureOf (ValidationError.create code (sprintf "%s refused" name)))

    /// Run the composition over the pending gates, releasing each gate just
    /// before the composition would await it. Because a never-awaited gate is
    /// never released, the trace records EXACTLY the gates the composition
    /// awaited, in order. Returns the composition's result.
    let private driveWith
        (compose: Task<Result<unit>> list -> Task<'r>)
        (gates: Pending list)
        : 'r =
        let composed = compose (gates |> List.map (fun g -> g.Task))
        let rec pump (remaining: Pending list) =
            if composed.IsCompleted then ()    // short-circuited: stop releasing.
            else
                match remaining with
                | [] -> ()
                | g :: rest ->
                    g.Release()
                    // Let the awaiting continuation advance before deciding
                    // whether the composition has short-circuited.
                    composed.Wait(1000) |> ignore
                    pump rest
        pump gates
        composed.GetAwaiter().GetResult()

    /// Drive `Preflight.allReporting` over pending gates.
    let driveReporting (gates: Pending list) : Result<unit, Preflight.GateRefusal> =
        driveWith Preflight.allReporting gates

    /// Drive `Preflight.all` over pending gates.
    let driveAll (gates: Pending list) : Result<unit> =
        driveWith Preflight.all gates

// -- Composition: ordering + short-circuit (G0 — `all` runs in order) ---------

[<Fact>]
let ``G0.1: allReporting runs gates in order and returns Ok when all pass`` () =
    // AC-G0: the suite composes into one entry point; an all-passing suite is Ok.
    let trace = System.Collections.Generic.List<string>()
    let gates =
        [ Gate.pass trace "a"
          Gate.pass trace "b"
          Gate.pass trace "c" ]
    match Gate.driveReporting gates with
    | Ok () -> ()
    | Error r -> Assert.Fail(sprintf "expected Ok, got refusal %A" r.Label)
    // Ordering: every gate fired, left to right.
    Assert.Equal<string list>([ "a"; "b"; "c" ], List.ofSeq trace)

[<Fact>]
let ``G0.1: allReporting short-circuits on the FIRST refusal`` () =
    // AC-G0: short-circuit on first refusal — gates AFTER the refusal do NOT run.
    let trace = System.Collections.Generic.List<string>()
    let gates =
        [ Gate.pass trace "a"
          Gate.refuse trace "b" "migrate.dataViolatesTightening"
          Gate.pass trace "c" ]   // must NOT be awaited
    match Gate.driveReporting gates with
    | Error r -> Assert.Equal("migrate.dataViolatesTightening", r.Error.Code)
    | Ok () -> Assert.Fail "expected the second gate to refuse"
    // Short-circuit: "c" was never awaited — only a and b are in the trace.
    Assert.Equal<string list>([ "a"; "b" ], List.ofSeq trace)

[<Fact>]
let ``G0.1: allReporting reports the FIRST refusal when several would refuse`` () =
    // The first refusal wins (precedence is composition order, not severity); the
    // second refusing gate is never awaited.
    let trace = System.Collections.Generic.List<string>()
    let gates =
        [ Gate.refuse trace "conn" "transfer.connectionUnavailable"
          Gate.refuse trace "grant" "transfer.insufficientGrant" ]  // never reached
    match Gate.driveReporting gates with
    | Error r ->
        Assert.Equal("transfer.connectionUnavailable", r.Error.Code)
        Assert.Equal(Preflight.ConnectionUnavailable, r.Label)
    | Ok () -> Assert.Fail "expected a refusal"
    Assert.Equal<string list>([ "conn" ], List.ofSeq trace)

[<Fact>]
let ``G0.1: all (the Result<unit> sibling) is unchanged — same first-failure`` () =
    // The additive surface preserves `all`'s existing semantics (zero-caller
    // refactor risk): `all` still returns the bare `Result<unit>` first failure
    // and short-circuits identically.
    let trace = System.Collections.Generic.List<string>()
    let gates =
        [ Gate.pass trace "a"
          Gate.refuse trace "b" "transfer.insufficientGrant"
          Gate.pass trace "c" ]
    match Gate.driveAll gates with
    | Error [ e ] -> Assert.Equal("transfer.insufficientGrant", e.Code)
    | other -> Assert.Fail(sprintf "expected single-error refusal, got %A" other)
    Assert.Equal<string list>([ "a"; "b" ], List.ofSeq trace)

// -- No-bypass: the G0 criterion (G0.4 — a gate in `all` cannot be skipped) ----

[<Fact>]
let ``G0.4: a gate ADDED to the suite fires on input a shorter suite passed`` () =
    // AC-G0 (G0.4): composing through the suite cannot skip a gate. The SAME
    // input passes a suite that OMITS the tightening gate, but refuses once that
    // gate is composed in — so adding a gate to `all` cannot be bypassed.
    let trace = System.Collections.Generic.List<string>()
    let connGate () = Gate.pass trace "connection"
    let grantGate () = Gate.pass trace "permission"
    // The data on which the tightening gate refuses.
    let tighteningGate () = Gate.refuse trace "tightening" "migrate.dataViolatesTightening"

    // A "verb" that OMITS the tightening gate: it passes (the bypass).
    match Gate.driveReporting [ connGate (); grantGate () ] with
    | Ok () -> ()
    | Error r -> Assert.Fail(sprintf "the omitting suite should pass, got %A" r.Label)

    // The SAME inputs routed through the FULL suite (tightening composed in)
    // refuse — the gate cannot be skipped once it is in `all`.
    match Gate.driveReporting [ connGate (); grantGate (); tighteningGate () ] with
    | Error r -> Assert.Equal(Preflight.DataViolatesTightening, r.Label)
    | Ok () -> Assert.Fail "the full suite must refuse — the added gate was skipped"

[<Fact>]
let ``G0.4: a verb that omits a gate produces a WORSE outcome than routing through all`` () =
    // The adversarial structural witness: an omitting "verb" exits 0 (clean) on
    // input the full suite refuses with a distinct non-zero exit. Composing
    // through `all` is STRICTLY safer — the omission is observable as a worse
    // (silently-clean) outcome.
    let trace = System.Collections.Generic.List<string>()
    let passing () = Gate.pass trace "a"
    let refusing () = Gate.refuse trace "b" "transfer.unmappedIdentities"

    // Omitting verb: exit 0 (the silent-corruption hazard G0 closes).
    let omittedExit =
        match Gate.driveReporting [ passing () ] with
        | Ok () -> 0
        | Error r -> r.ExitCode

    // Routed-through-all verb: the distinct non-zero exit.
    let routedExit =
        match Gate.driveReporting [ passing (); refusing () ] with
        | Ok () -> 0
        | Error r -> r.ExitCode

    Assert.Equal(0, omittedExit)
    Assert.NotEqual(0, routedExit)
    Assert.True(routedExit > omittedExit, "routing through all must produce a worse (non-zero) exit than the omitting verb")

// -- Exit-code preservation: `code → (exit, label)` (the §8 discriminator) -----

[<Theory>]
[<InlineData("transfer.connectionUnavailable", 6)>]
[<InlineData("migrate.connectionUnavailable", 6)>]
// NM-61 (extended) — the `ConnectionResolver.openSubstrate` error codes (the
// ones the migrate execute / migrate-with-data / project-preview faces now route
// through `refusalOf` instead of hardcoding 3). All `transfer.connection.*`, so
// the connection-reach axis is exit 6 on EVERY verb, never the prior face-local 3.
[<InlineData("transfer.connection.openFailed", 6)>]
[<InlineData("transfer.connection.refMissing", 6)>]
[<InlineData("transfer.connection.refEmpty", 6)>]
[<InlineData("transfer.insufficientGrant", 7)>]
[<InlineData("migrate.insufficientGrant", 7)>]
[<InlineData("transfer.grantProbeFailed", 7)>]
[<InlineData("transfer.reconcile.userMatch", 2)>]
[<InlineData("transfer.userMap.unmatched", 2)>]
[<InlineData("transfer.unmappedIdentities", 9)>]
[<InlineData("migrate.dataViolatesTightening", 9)>]
[<InlineData("transfer.cdcTrackedSink", 9)>]
[<InlineData("migrate.cdcTrackedSink", 9)>]
[<InlineData("migrate.schemaReadFailed", 6)>]
let ``G0: classify maps each KNOWN gate code to its distinct per-verb exit`` (code: string) (expected: int) =
    // §8 discriminator: the composition must NOT flatten distinct refusals to one
    // exit. Each known gate code maps to the exit the verbs produce today.
    let exit, _label = Preflight.classify code
    Assert.Equal(expected, exit)

[<Fact>]
let ``G0: classify yields the four discriminating exits 2/3/7/9 across the gates`` () =
    // The discriminator against a refactor that collapses all refusals to one
    // exit: the four canonical exits the criterion names (2/3/7/9) are each
    // produced by a distinct gate code, plus 6 for the connection axis.
    let exitOf c = fst (Preflight.classify c)
    Assert.Equal(2, exitOf "transfer.reconcile.userMatch")
    Assert.Equal(3, exitOf "something.totally.unknown")           // the named default
    Assert.Equal(7, exitOf "transfer.insufficientGrant")
    Assert.Equal(9, exitOf "migrate.dataViolatesTightening")
    Assert.Equal(6, exitOf "transfer.connectionUnavailable")
    // Distinct: the suite does not collapse to a single exit.
    let distinct =
        [ "transfer.reconcile.x"; "x.unknown"; "transfer.insufficientGrant"
          "migrate.dataViolatesTightening"; "transfer.connectionUnavailable" ]
        |> List.map exitOf
        |> List.distinct
    Assert.Equal(5, List.length distinct)

[<Fact>]
let ``G0: an UNKNOWN code maps to the named default (3, UnclassifiedRefusal) — fail loud`` () =
    // Totality: a code outside the gate vocabulary is NOT a silent exit 0 — it
    // falls to the named default carrying the generic non-zero refusal exit.
    let exit, label = Preflight.classify "gate.never.seen.before"
    Assert.Equal(3, exit)
    Assert.Equal(Preflight.UnclassifiedRefusal, label)
    Assert.NotEqual(0, exit)

[<Fact>]
let ``G0: labelText renders every GateLabel variant (closed-DU totality)`` () =
    // Closed-DU totality: every label has an operator-facing rendering (the
    // reporting surface names WHICH axis refused).
    let labels =
        [ Preflight.ConnectionUnavailable
          Preflight.InsufficientGrant
          Preflight.ReconciliationMismatch
          Preflight.UnmappedIdentities
          Preflight.DataViolatesTightening
          Preflight.CdcTrackedSink
          Preflight.SchemaReadFailed
          Preflight.UndeclaredDestructiveChange
          Preflight.MidWriteNotProtected
          Preflight.UnclassifiedRefusal ]
    for l in labels do
        Assert.False(System.String.IsNullOrWhiteSpace(Preflight.labelText l))

[<Fact>]
let ``NM-61: a migrate connection refusal classifies to exit 6 (its own axis), not the flattened 7`` () =
    // NM-61 — the migrate face (`migratePreflights` / `runMigrateWithData`) once
    // hardcoded EVERY refusal to exit 7, so a dead endpoint exited 6 on `transfer`
    // but 7 on `migrate` — the same axis, the same probe, two codes — and the
    // `migrate.connectionUnavailable → 6` arm in `classify` was dead. The face now
    // routes through `refusalOf` (the A1 single-source seam) and returns its exit,
    // so the connection axis is 6 on migrate too and the classify arm is live.
    let refusal =
        Preflight.refusalOf
            [ ValidationError.create "migrate.connectionUnavailable" "the sink endpoint refused" ]
    Assert.Equal(6, refusal.ExitCode)
    Assert.Equal(Preflight.ConnectionUnavailable, refusal.Label)

[<Fact>]
let ``NM-61 (extended): an openSubstrate connection failure classifies to 6 on the migrate faces, matching transfer`` () =
    // The migrate execute / migrate-with-data / project-preview faces opened the
    // sink via `ConnectionResolver.openSubstrate` and hardcoded exit 3 on its
    // failure — so a dead endpoint exited 6 on `transfer` (which routes the same
    // codes through `refusalOf`) but 3 on `migrate`. The faces now route the
    // openSubstrate errors through `refusalOf` too; this pins that every code
    // `openSubstrate` can emit lands on the connection axis (6), the verb-agnostic
    // exit, so the same dead endpoint exits the same way everywhere.
    let exitOf code = (Preflight.refusalOf [ ValidationError.create code "openSubstrate refused" ]).ExitCode
    Assert.Equal(6, exitOf "transfer.connection.openFailed")
    Assert.Equal(6, exitOf "transfer.connection.refMissing")
    Assert.Equal(6, exitOf "transfer.connection.refEmpty")
    // And the label is the shared connection-axis label, not a verb-local one.
    let refusal = Preflight.refusalOf [ ValidationError.create "transfer.connection.openFailed" "down" ]
    Assert.Equal(Preflight.ConnectionUnavailable, refusal.Label)

[<Fact>]
let ``NM-61: the OTHER migrate refusal axes keep their distinct exits through refusalOf`` () =
    // The fix must not flatten the rest: the migrate permission/grant axis stays 7,
    // the CDC-tracked sink 9, the tightening axis 9 — the same exits the verb
    // produced before, now sourced from the one classification.
    let exitOf code = (Preflight.refusalOf [ ValidationError.create code "refused" ]).ExitCode
    Assert.Equal(7, exitOf "migrate.insufficientGrant")
    Assert.Equal(7, exitOf "migrate.grantProbeFailed")
    Assert.Equal(9, exitOf "migrate.cdcTrackedSink")
    Assert.Equal(9, exitOf "migrate.dataViolatesTightening")

[<Fact>]
let ``G0: refusalOf classifies the FIRST error code (the gate's primary refusal)`` () =
    // The composition reports the structured first-failure with its (exit,label):
    // `refusalOf` keys off the primary (first) error's code.
    let errors =
        [ ValidationError.create "transfer.insufficientGrant" "grant missing"
          ValidationError.create "transfer.connectionUnavailable" "secondary" ]
    let refusal = Preflight.refusalOf errors
    Assert.Equal("transfer.insufficientGrant", refusal.Error.Code)
    Assert.Equal(7, refusal.ExitCode)
    Assert.Equal(Preflight.InsufficientGrant, refusal.Label)

// -- M20 · Transactionality (T-VI spanning) — the honest-naming half ----------
//
// THE VECTOR §5.1 / M20. A half-populated target after a mid-Phase-2 crash on an
// unprotected (non-atomic, non-resumable) write path was the GENERIC
// `UnclassifiedRefusal` (exit 3). M20 NAMES it: a closed-DU variant
// (`MidWriteNotProtected`), its exit-9 classify arm (the destructive-failure
// class), and a PURE gate. The live atomic `BEGIN TRAN` wrapper stays deferred
// (the A3 scaffold in Preflight.fs is the survey's to resolve) — these witness
// the classification half only.

[<Fact>]
let ``M20 (T-VI spanning): classify maps transfer.midWriteNotProtected to (9, MidWriteNotProtected)`` () =
    // The transactionality axis classifies as the destructive-failure class
    // (exit 9), no longer the generic (3, UnclassifiedRefusal).
    let exit, label = Preflight.classify "transfer.midWriteNotProtected"
    Assert.Equal(9, exit)
    Assert.Equal(Preflight.MidWriteNotProtected, label)
    Assert.NotEqual(3, exit)

[<Fact>]
let ``M20 (T-VI spanning): classify maps the migrate.midWriteNotProtected form identically`` () =
    // The SAME axis under the migrate namespace classifies identically — the same
    // exit, the same label.
    let exit, label = Preflight.classify "migrate.midWriteNotProtected"
    Assert.Equal(9, exit)
    Assert.Equal(Preflight.MidWriteNotProtected, label)

[<Fact>]
let ``M20 (T-VI spanning): labelText covers MidWriteNotProtected`` () =
    // The named axis has an operator-facing rendering — the reporting surface can
    // name WHICH axis refused.
    Assert.Equal("mid-write not protected", Preflight.labelText Preflight.MidWriteNotProtected)
    Assert.False(System.String.IsNullOrWhiteSpace(Preflight.labelText Preflight.MidWriteNotProtected))

// A small set of planned destructive writes the transactionality gate reasons
// over (an Insert and a Delete on two objects).
let private plannedDestructiveWrites : Preflight.PlannedWrite list =
    [ { Schema = "dbo"; Table = "Customer"; Action = Preflight.Insert }
      { Schema = "dbo"; Table = "Order"; Action = Preflight.Delete } ]

[<Fact>]
let ``M20 (T-VI spanning): transactionalityViolations is non-empty and named under Unprotected`` () =
    // An unprotected write path yields one violation per planned destructive write
    // — a mid-write crash would leave each half-applied.
    let violations =
        Preflight.transactionalityViolations Preflight.Unprotected plannedDestructiveWrites
    Assert.Equal(2, List.length violations)
    // Named: each violation locates the object + action it would leave half-applied.
    let objects = violations |> List.map (fun v -> v.Object)
    Assert.Contains("dbo.Customer", objects)
    Assert.Contains("dbo.Order", objects)
    // Deterministic — sorted by object then action.
    Assert.Equal<string list>([ "dbo.Customer"; "dbo.Order" ], objects)

[<Fact>]
let ``M20 (T-VI spanning): transactionalityViolations is empty under Atomic and Resumable`` () =
    // A protected path (whole-write rollback OR idempotent re-run) yields no
    // violation — the mid-write crash is survived.
    Assert.Empty(Preflight.transactionalityViolations Preflight.Atomic plannedDestructiveWrites)
    Assert.Empty(Preflight.transactionalityViolations Preflight.Resumable plannedDestructiveWrites)

[<Fact>]
let ``M20 (T-VI spanning): transactionalityPreflight is Ok when the path is protected`` () =
    // Atomic and Resumable both pass the gate — no refusal.
    match Preflight.transactionalityPreflight Preflight.Atomic plannedDestructiveWrites with
    | Ok () -> ()
    | Error e -> Assert.Fail(sprintf "expected Ok under Atomic, got %A" e)
    match Preflight.transactionalityPreflight Preflight.Resumable plannedDestructiveWrites with
    | Ok () -> ()
    | Error e -> Assert.Fail(sprintf "expected Ok under Resumable, got %A" e)

[<Fact>]
let ``M20 (T-VI spanning): transactionalityPreflight refuses migrate.midWriteNotProtected under Unprotected`` () =
    // An unprotected path refuses with the named code — which classifies to exit 9
    // (the destructive-failure axis), never the generic exit 3.
    match Preflight.transactionalityPreflight Preflight.Unprotected plannedDestructiveWrites with
    | Error [ e ] ->
        Assert.Equal("migrate.midWriteNotProtected", e.Code)
        let exit, label = Preflight.classify e.Code
        Assert.Equal(9, exit)
        Assert.Equal(Preflight.MidWriteNotProtected, label)
    | other -> Assert.Fail(sprintf "expected a single-error refusal, got %A" other)

[<Fact>]
let ``M20 (T-VI spanning): an empty plan never refuses, even Unprotected`` () =
    // No planned destructive write means nothing to leave half-applied — the gate
    // is vacuously Ok regardless of protection.
    match Preflight.transactionalityPreflight Preflight.Unprotected [] with
    | Ok () -> ()
    | Error e -> Assert.Fail(sprintf "an empty plan must pass, got %A" e)
