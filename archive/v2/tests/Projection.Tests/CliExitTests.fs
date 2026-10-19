module Projection.Tests.CliExitTests

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Cli

// Recon #3 (Concern 1) — the six hand-rolled `anyCode` exit ladders across the
// CLI artifact/read verbs (synth-load, profile, synth-correct, slice-extract,
// slice-apply/reset, slice-run) are collapsed into one total `CliExit`
// classifier. These pin the RECONCILED contract: every CLI refusal lands on the
// same exit as the equivalent migrate/transfer gate refusal, and the three
// pre-existing divergences are fixed onto the gate convention.

[<Theory>]
// -- exit 1: artifact write/emit failed (output-IO) -------------------------
[<InlineData("profile.writeFailed", 1)>]
[<InlineData("correction.writeFailed", 1)>]
[<InlineData("slice.writeFailed", 1)>]
[<InlineData("slice.emitFailed", 1)>]
// -- exit 2: bad input — model/spec/golden/schema parse ----------------------
[<InlineData("model.parseFailed", 2)>]
[<InlineData("synthetic.profileRef", 2)>]
[<InlineData("slice.root", 2)>]
[<InlineData("slice.spec", 2)>]
[<InlineData("slice.golden", 2)>]
[<InlineData("slice.schemaParity", 2)>]
// -- exit 6: connection axis -------------------------------------------------
[<InlineData("connection", 6)>]
[<InlineData("connectionSpec", 6)>]
// -- exit 7: permission axis -------------------------------------------------
[<InlineData("synthetic.insufficientGrant", 7)>]
[<InlineData("slice.apply.insufficientGrant", 7)>]
// -- exit 9: refused, fail-loud — CDC-tracked sink ---------------------------
[<InlineData("slice.apply.cdcTrackedSink", 9)>]
let ``CliExit.classifyCode maps each CLI refusal code to its axis exit`` (code: string) (expected: int) =
    Assert.Equal(expected, CliExit.classifyCode code)

[<Fact>]
let ``CliExit reconciles synthetic.cdcTrackedSink onto the gate convention (9, not the old 7)`` () =
    // D1: synth-load's ladder grouped cdcTrackedSink with the grant axis (7);
    // every other `*.cdcTrackedSink` (the migrate/transfer gate convention)
    // exits 9 — the fail-loud destructive class. Reconciled to 9.
    Assert.Equal(9, CliExit.classifyCode "synthetic.cdcTrackedSink")
    Assert.Equal(9, fst (Preflight.classify "migrate.cdcTrackedSink"))

[<Fact>]
let ``CliExit reconciles slice.apply.grantProbeFailed onto the grant axis (7, not the old 6)`` () =
    // D2: slice-apply's ladder grouped grantProbeFailed with the connection
    // axis (6); the grant axis exits 7 everywhere else. Reconciled to 7.
    Assert.Equal(7, CliExit.classifyCode "slice.apply.grantProbeFailed")
    Assert.Equal(7, fst (Preflight.classify "migrate.grantProbeFailed"))

[<Fact>]
let ``CliExit makes the two slice verbs agree on the slice.apply.* sub-codes`` () =
    // D3: slice-apply mapped these to 7/7 while slice-run flattened the whole
    // `slice.apply` prefix to 6. One classifier now serves both, so the same
    // code yields the same exit regardless of which slice verb refused.
    Assert.Equal(9, CliExit.classifyCode "slice.apply.cdcTrackedSink")
    Assert.Equal(7, CliExit.classifyCode "slice.apply.insufficientGrant")
    Assert.Equal(7, CliExit.classifyCode "slice.apply.grantProbeFailed")

[<Fact>]
let ``CliExit defers a gate-vocabulary code to the single Preflight.classify seam`` () =
    // A `transfer.*`/`migrate.*` code, should one reach a CLI face, is not
    // re-stated here — it classifies through the one gate seam.
    Assert.Equal(fst (Preflight.classify "transfer.connectionUnavailable"),
                 CliExit.classifyCode "transfer.connectionUnavailable")
    Assert.Equal(fst (Preflight.classify "migrate.insufficientGrant"),
                 CliExit.classifyCode "migrate.insufficientGrant")
    Assert.Equal(6, CliExit.classifyCode "transfer.connection.openFailed")

[<Fact>]
let ``CliExit is total — an unknown code is the named generic refusal (3), never 0`` () =
    let exit = CliExit.classifyCode "something.totally.unknown"
    Assert.Equal(3, exit)
    Assert.NotEqual(0, exit)

[<Fact>]
let ``CliExit specific axes win over the generic slice-parse bucket (order)`` () =
    // `slice.apply.grantProbeFailed` matches both the grant axis and the generic
    // `slice.` parse bucket; the specific axis (7) must win, not the bucket (2).
    Assert.Equal(7, CliExit.classifyCode "slice.apply.grantProbeFailed")
    Assert.Equal(9, CliExit.classifyCode "slice.apply.cdcTrackedSink")
    // A plain slice-parse code still lands in the bucket.
    Assert.Equal(2, CliExit.classifyCode "slice.root")

[<Fact>]
let ``CliExit.classify keys off the primary (first) error, matching Preflight.refusalOf`` () =
    let errors =
        [ ValidationError.create "slice.apply.insufficientGrant" "grant missing"
          ValidationError.create "connection" "secondary" ]
    Assert.Equal(7, CliExit.classify errors)
    // Reversed primary → the connection axis.
    let reversed =
        [ ValidationError.create "connection" "down"
          ValidationError.create "slice.apply.insufficientGrant" "grant missing" ]
    Assert.Equal(6, CliExit.classify reversed)

[<Fact>]
let ``CliExit.classify on an empty list is the generic refusal (3)`` () =
    Assert.Equal(3, CliExit.classify [])
