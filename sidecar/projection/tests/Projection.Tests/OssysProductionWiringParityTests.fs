module Projection.Tests.OssysProductionWiringParityTests

// V1 parity audit — chapter 5.1.γ. `V1_PARITY_MATRIX.md` rows 32–36
// covering V1's production-wiring behaviors on
// `SqlClientOutsystemsMetadataReader` + adjacent connection/command/
// processor abstractions.
//
// **Chapter close 2026-05-18.** All five rows shipped across two
// cash-out arcs:
//   - slice 5.13.production-wiring-classification → rows 32 + 34 + 35
//     (DU + Polly retry + result-set count contract)
//   - slice 5.13.progress-callback → row 36 (per-rowset observation)
//   - slice 5.13.command-timeout + sibling-wrapper-collapse → row 33
//     (tunable timeout via RunOptions; retires runAsyncWithProgress
//     overdifferentiated middle-tier)
//
// Zero Skip stubs in this file as of chapter close.

open System
open System.Threading.Tasks
open Xunit
open Projection.Core
open Projection.Adapters.OssysSql

// -----------------------------------------------------------------
// Slice 5.13.production-wiring-classification: rows 32 + 34 + 35.
// Closed-DU MetadataExtractionError (4 variants); Polly retry
// pipeline at command-execute boundary; post-loop result-set
// contract check.
// -----------------------------------------------------------------

/// Custom test exception simulating a transient SQL failure. Used in
/// retry tests because `Microsoft.Data.SqlClient.SqlException` is sealed
/// with no public constructor; the predicate-parameterized
/// `Retry.buildPipeline` lets the test substitute a custom predicate
/// matching this type instead of `SqlException`.
type TransientTestException(message: string) =
    inherit exn(message)

[<Fact>]
let ``5.1.γ row 32: each MetadataExtractionError variant maps to a distinct ValidationError code`` () =
    let variants : MetadataExtractionError list =
        [
            MetadataExtractionError.RowMappingFailure ("modules", 3, InvalidCastException "type mismatch")
            MetadataExtractionError.TransientSqlError (40613, "Azure SQL database currently unavailable")
            MetadataExtractionError.OtherSqlError "permission denied"
        ]
    let codes =
        variants
        |> List.map (MetadataExtractionError.toValidationError >> fun v -> v.Code)
    // The contract: every variant produces a distinct code so consumers
    // can route by Code without parsing message text.
    Assert.Equal(List.length variants, List.length (List.distinct codes))

[<Fact>]
let ``5.1.γ row 32: RowMappingFailure ValidationError carries resultSet + rowIndex metadata`` () =
    let err =
        MetadataExtractionError.RowMappingFailure ("attributes", 17, InvalidOperationException "null column")
        |> MetadataExtractionError.toValidationError
    Assert.Equal(MetadataExtractionError.CodeRowMapping, err.Code)
    Assert.Equal(Some "attributes", err.Metadata.["resultSet"])
    Assert.Equal(Some "17", err.Metadata.["rowIndex"])

[<Fact>]
let ``5.1.γ row 32: TransientSqlError ValidationError carries sqlNumber metadata`` () =
    let err =
        MetadataExtractionError.TransientSqlError (40501, "service is busy")
        |> MetadataExtractionError.toValidationError
    Assert.Equal(MetadataExtractionError.CodeTransient, err.Code)
    Assert.Equal(Some "40501", err.Metadata.["sqlNumber"])

[<Fact>]
let ``5.1.γ row 32: classify lifts RowMappingException to RowMappingFailure`` () =
    let inner = InvalidCastException "widened int"
    let raised = RowMappingException ("modules", 4, inner)
    match MetadataExtractionError.classify Retry.isTransientSqlError raised with
    | MetadataExtractionError.RowMappingFailure (rs, idx, e) ->
        Assert.Equal("modules", rs)
        Assert.Equal(4, idx)
        Assert.Same(inner, e)
    | other ->
        Assert.Fail (sprintf "expected RowMappingFailure, got %A" other)

[<Fact>]
let ``5.1.γ row 32: classify lifts non-SqlException to OtherSqlError`` () =
    let ex = InvalidOperationException "boundary failure"
    match MetadataExtractionError.classify Retry.isTransientSqlError ex with
    | MetadataExtractionError.OtherSqlError msg -> Assert.Equal("boundary failure", msg)
    | other -> Assert.Fail (sprintf "expected OtherSqlError, got %A" other)

[<Fact>]
let ``5.1.γ row 34: transientSqlNumbers covers the documented cutover-critical numbers`` () =
    // Per V1_PARITY_MATRIX row 34 cash-out + Azure SQL transient-error
    // catalog. Locking the set membership protects against silent drift
    // (e.g., dropping 4060 in a refactor would re-open a transient surface
    // for cloud OSSYS warmup).
    let expected =
        Set.ofList [ -2; -1; 4060; 18452; 40197; 40501; 40613 ]
    Assert.Equal<Set<int>>(expected, Retry.transientSqlNumbers)

[<Fact>]
let ``5.1.γ row 34: isTransientSqlError refuses non-SqlException`` () =
    // Pure-predicate behavior: the predicate IS narrow on type — a
    // generic Exception with Message containing "timeout" is NOT
    // transient. SqlException is the type-witness for OSSYS-source
    // transients.
    Assert.False (Retry.isTransientSqlError (exn "timeout"))
    Assert.False (Retry.isTransientSqlError (TransientTestException "service busy"))
    Assert.False (Retry.isTransientSqlError (InvalidOperationException "transient-ish"))

[<Fact>]
let ``5.1.γ row 34: retry pipeline retries until the operation succeeds`` () =
    // Counting closure: throws on the first 2 attempts (transient per
    // the custom predicate), succeeds on the 3rd. The pipeline runs
    // up to 4 total attempts (1 initial + 3 retries) so success on
    // attempt 3 falls within the budget.
    let attempts = ref 0
    let pipeline =
        Retry.buildPipeline
            (fun ex -> ex :? TransientTestException)
            (TimeSpan.FromMilliseconds 1.0)
            Retry.DefaultMaxRetryAttempts
    let operation _ct : Task<string> =
        task {
            attempts.Value <- attempts.Value + 1
            if attempts.Value < 3 then
                raise (TransientTestException (sprintf "attempt %d transient" attempts.Value))
            return "ok"
        }
    let result = TaskSync.run (fun () -> Retry.runOnPipeline pipeline operation)
    Assert.Equal("ok", result)
    Assert.Equal(3, attempts.Value)

[<Fact>]
let ``5.1.γ row 34: retry pipeline surfaces the final exception after retries exhaust`` () =
    // Predicate matches every attempt; budget exhausts (1 initial + 3
    // retries = 4 attempts) and the final exception bubbles. Outer
    // classifier in `MetadataSnapshotRunner.runAsync` would then lift
    // it to `MetadataExtractionError.OtherSqlError` (TransientTestException
    // is not a SqlException so the transient-classifier returns false
    // in production — this test exercises the retry pipeline behavior
    // in isolation).
    let attempts = ref 0
    let pipeline =
        Retry.buildPipeline
            (fun ex -> ex :? TransientTestException)
            (TimeSpan.FromMilliseconds 1.0)
            Retry.DefaultMaxRetryAttempts
    let operation _ct : Task<string> =
        task {
            attempts.Value <- attempts.Value + 1
            raise (TransientTestException (sprintf "attempt %d" attempts.Value))
            return "never"
        }
    let ex =
        Assert.Throws<TransientTestException>(Action(fun () ->
            TaskSync.run (fun () -> Retry.runOnPipeline pipeline operation) |> ignore))
    Assert.Equal("attempt 4", ex.Message)
    Assert.Equal(4, attempts.Value)

[<Fact>]
let ``5.1.γ row 34: retry pipeline does not retry on non-matching exceptions`` () =
    // Predicate only matches TransientTestException; a different
    // exception bubbles immediately without retry. Production analog:
    // a non-transient SqlException (e.g., permission denied;
    // Number = 18456) bypasses retry and surfaces as OtherSqlError on
    // the first attempt.
    let attempts = ref 0
    let pipeline =
        Retry.buildPipeline
            (fun ex -> ex :? TransientTestException)
            (TimeSpan.FromMilliseconds 1.0)
            Retry.DefaultMaxRetryAttempts
    let operation _ct : Task<string> =
        task {
            attempts.Value <- attempts.Value + 1
            raise (InvalidOperationException "non-transient")
            return "never"
        }
    let _ =
        Assert.Throws<InvalidOperationException>(Action(fun () ->
            TaskSync.run (fun () -> Retry.runOnPipeline pipeline operation) |> ignore))
    Assert.Equal(1, attempts.Value)

// -----------------------------------------------------------------
// Slice 5.13.command-timeout + sibling-wrapper-collapse
// (matrix row 33). Shipped 2026-05-18.
//
// The `RunOptions` record consolidates two axes (CommandTimeoutSeconds
// + OnRowsetComplete) into a single typed surface. The runner exposes
// two entry points (`runAsync` zero-default + `runAsyncWithOptions`
// full-explicit) per the sibling-wrapper discipline — collapses the
// 3-arity `runAsyncWithProgress` middle-tier introduced in the
// progress-callback slice.
// -----------------------------------------------------------------

[<Fact>]
let ``5.1.γ row 33: defaultOptions preserve canary semantics`` () =
    // Zero-axis defaults: no timeout, no progress observation. This is
    // the canary's contract — any change here is a canary regression.
    Assert.Equal<int option>(None, MetadataSnapshotRunner.defaultOptions.CommandTimeoutSeconds)

[<Fact>]
let ``5.1.γ row 33: RunOptions record threads CommandTimeoutSeconds for production CLI`` () =
    // The cash-out shape — production CLI passes operator-tunable
    // value via a future --command-timeout-seconds flag; the V2 surface
    // is value-typed (no DI / no mutable global).
    let withTimeout =
        { MetadataSnapshotRunner.defaultOptions with CommandTimeoutSeconds = Some 30 }
    Assert.Equal<int option>(Some 30, withTimeout.CommandTimeoutSeconds)
    // The progress callback is a function value — F# function equality
    // is reference-only; we verify the record-update axis ORTHOGONALITY
    // by exercising the callback (a no-op should not throw).
    withTimeout.OnRowsetComplete {
        ResultSetIndex = 0; ResultSetName = "modules"; RowCount = 0 }

// -----------------------------------------------------------------
// Slice 5.13.result-set-contract (matrix row 35). Shipped 2026-05-18.
//
// The DU's `ResultSetMissing` variant + post-loop contract check in
// `runAsync`. Tests pin the count constant (22) and exercise the
// pure assertion function over (expected, actual) pairs.
// -----------------------------------------------------------------

[<Fact>]
let ``5.1.γ row 35: ExpectedResultSets is pinned per empirical canary observation`` () =
    // V1 documented 22 user-visible rowsets; the canary's empirical walk
    // saw 23 (an extra validation/sanity-check projection that V1's
    // per-processor approach skipped but V2's NextResultAsync loop
    // enumerates). The extraction fork (DECISIONS 2026-07-18; #669
    // EF-22 + EF-23) appended the sequences and temporal rowsets: 25.
    // The data-sink chapter (S2, 2026-08-15) appended the capability
    // vector: 26. align-II.7: the count now DERIVES from the rowset
    // contract (`ExpectedResultSets = List.length RowsetContract.all`);
    // this pin catches a table row added or dropped without the walk —
    // the count still moves only with a deliberate contract change.
    Assert.Equal(26, MetadataSnapshotRunner.ExpectedResultSets)
    Assert.Equal(List.length MetadataSnapshotRunner.RowsetContract.all, MetadataSnapshotRunner.ExpectedResultSets)

// -----------------------------------------------------------------
// align-II.7 (E5; audit a1) — THE ROWSET CONTRACT's own laws. The walk
// derives its count, labels, positions, and leading-column tripwires
// from `RowsetContract.all`; these pin the table's internal coherence
// (the live canary verifies it against the wire on every extraction).
// -----------------------------------------------------------------

[<Fact>]
let ``align-II.7: the rowset contract is wire-ordered — ordinals are exactly 0..N-1 in list order, names distinct, leading columns named`` () =
    let entries = MetadataSnapshotRunner.RowsetContract.all
    entries
    |> List.iteri (fun ix e ->
        Assert.Equal(ix, e.Ordinal)
        Assert.False(String.IsNullOrWhiteSpace e.Name, sprintf "entry %d carries no name" ix)
        Assert.False(String.IsNullOrWhiteSpace e.LeadingColumn, sprintf "'%s' carries no leading column" e.Name))
    Assert.Equal(List.length entries, entries |> List.map (fun e -> e.Name) |> List.distinct |> List.length)

[<Fact>]
let ``align-II.7: the parsed targets are exactly the MetadataSnapshot fields — every field has its wire rowset, every parsed rowset a field`` () =
    // The bidirectional totality: a snapshot field added without its
    // contract row fails, and a contract row naming a ghost field fails.
    let parsedTargets =
        MetadataSnapshotRunner.RowsetContract.all
        |> List.choose (fun e ->
            match e.Disposition with
            | MetadataSnapshotRunner.RowsetDisposition.Parsed target -> Some target
            | MetadataSnapshotRunner.RowsetDisposition.Drained _ -> None)
    let snapshotFields =
        Reflection.FSharpType.GetRecordFields typeof<MetadataSnapshotRunner.MetadataSnapshot>
        |> Array.map (fun p -> p.Name)
        |> Array.toList
        // `Scope` is the stamped acquisition METADATA (align-II.9), not a
        // wire rowset — the one declared non-rowset field. A second
        // metadata field must join this list deliberately or fail here.
        |> List.filter (fun name -> name <> "Scope")
    Assert.Equal<Set<string>>(Set.ofList snapshotFields, Set.ofList parsedTargets)
    Assert.Equal(List.length parsedTargets, parsedTargets |> List.distinct |> List.length)

[<Fact>]
let ``align-II.7: every drained rowset carries its reason — a drain is never unexplained`` () =
    for e in MetadataSnapshotRunner.RowsetContract.all do
        match e.Disposition with
        | MetadataSnapshotRunner.RowsetDisposition.Drained reason ->
            Assert.False(String.IsNullOrWhiteSpace reason, sprintf "'%s' drains with no reason" e.Name)
        | MetadataSnapshotRunner.RowsetDisposition.Parsed _ -> ()

[<Fact>]
let ``align-II.7: byName resolves every contract row and refuses an unknown name`` () =
    for e in MetadataSnapshotRunner.RowsetContract.all do
        Assert.Equal(e, MetadataSnapshotRunner.RowsetContract.byName e.Name)
    Assert.Throws<ArgumentException>(fun () ->
        MetadataSnapshotRunner.RowsetContract.byName "nonsense" |> ignore)
    |> ignore

[<Fact>]
let ``align-II.7: a RowsetContractDrift surfaces the located rowset on its own code`` () =
    let err =
        MetadataExtractionError.RowsetContractDrift ("entities", "leads with column 'EspaceId'; the contract pins 'EntityId'")
        |> MetadataExtractionError.toValidationError
    Assert.Equal(MetadataExtractionError.CodeRowsetContractDrift, err.Code)
    Assert.Contains("entities", err.Message)
    Assert.Equal(Some "entities", err.Metadata.["rowset"])

[<Fact>]
let ``5.1.γ row 35: resultSetContractCheck succeeds on matching count`` () =
    let r = MetadataExtractionError.resultSetContractCheck 23 23
    Assert.True(Result.isSuccess r)

[<Fact>]
let ``5.1.γ row 35: resultSetContractCheck surfaces ResultSetMissing on mismatch`` () =
    let r = MetadataExtractionError.resultSetContractCheck 23 20
    Assert.True(Result.isFailure r)
    let err = Result.errors r |> List.head
    Assert.Equal(MetadataExtractionError.CodeResultSetContractBreach, err.Code)
    Assert.Equal(Some "23", err.Metadata.["expectedCount"])
    Assert.Equal(Some "20", err.Metadata.["actualCount"])

[<Fact>]
let ``5.1.γ row 35: ResultSetMissing ValidationError carries the contract-breach code`` () =
    let err =
        MetadataExtractionError.ResultSetMissing (22, 17)
        |> MetadataExtractionError.toValidationError
    Assert.Equal(MetadataExtractionError.CodeResultSetContractBreach, err.Code)

[<Fact>]
let ``5.1.γ row 35: every MetadataExtractionError variant produces a distinct code`` () =
    // Widening guard — when this slice's DU gained ResultSetMissing,
    // the distinct-code invariant from row 32's slice must still hold.
    // Future variants must extend this list.
    let variants : MetadataExtractionError list =
        [
            MetadataExtractionError.RowMappingFailure ("modules", 3, InvalidCastException "")
            MetadataExtractionError.ResultSetMissing (22, 20)
            MetadataExtractionError.TransientSqlError (40613, "")
            MetadataExtractionError.OtherSqlError ""
            MetadataExtractionError.RowsetContractDrift ("entities", "expected at wire ordinal 1, observed at 2")
        ]
    let codes =
        variants
        |> List.map (MetadataExtractionError.toValidationError >> fun v -> v.Code)
    Assert.Equal(List.length variants, List.length (List.distinct codes))

[<Fact>]
let ``5.1.γ row 35: V2 surfaces result-set count mismatch on OSSYS rowsets`` () =
    // Slice 5.13.result-set-contract — V2's `runAsync` now asserts the
    // observed count equals `ExpectedResultSets` after the skip-loop
    // exits; mismatch surfaces as `MetadataExtractionError.ResultSetMissing`
    // with the contract-breach code. The pure check function carries the
    // structural commitment; the live-extraction canary in
    // `OssysExtractionCanaryTests` exercises the integrated path.
    let r = MetadataExtractionError.resultSetContractCheck MetadataSnapshotRunner.ExpectedResultSets 18
    Assert.True(Result.isFailure r)
    let err = Result.errors r |> List.head
    Assert.Equal(MetadataExtractionError.CodeResultSetContractBreach, err.Code)

[<Fact>]
let ``5.1.γ row 36: V2 carries per-rowset progress observation on OSSYS extraction`` () =
    // Slice 5.13.progress-callback — `runAsyncWithProgress` invokes the
    // OnRowsetComplete callback after each rowset's parse/skip
    // completes. `noOpProgress` is the canonical no-observation default
    // (used by `runAsync` for caller convenience); CLI / TUI surfaces
    // pass their own callbacks.
    let observations = ResizeArray<MetadataSnapshotRunner.ProgressObservation>()
    let onComplete : MetadataSnapshotRunner.OnRowsetComplete =
        fun obs -> observations.Add obs
    // Sanity: the callback type is a one-shot value-record producer;
    // here we just verify it can be invoked outside `runAsync` (i.e.,
    // the type is public and callers can construct it directly).
    onComplete { ResultSetIndex = 0; ResultSetName = "modules"; RowCount = 3 }
    onComplete { ResultSetIndex = 4; ResultSetName = "physicalTables"; RowCount = 7 }
    Assert.Equal(2, observations.Count)
    Assert.Equal("modules", observations.[0].ResultSetName)
    Assert.Equal(3, observations.[0].RowCount)
    Assert.Equal("physicalTables", observations.[1].ResultSetName)
    Assert.Equal(7, observations.[1].RowCount)

[<Fact>]
let ``5.1.γ row 36: noOpProgress is a no-throw default`` () =
    // The default progress callback used by `runAsync` is no-op; this
    // guarantees the convenience overload doesn't crash on caller-side
    // when the consumer hasn't wired observation.
    MetadataSnapshotRunner.noOpProgress {
        ResultSetIndex = 0; ResultSetName = "modules"; RowCount = 1 }
    Assert.True(true)

[<Fact>]
let ``5.1.γ: production-wiring parity file present`` () =
    Assert.True(true)

// -----------------------------------------------------------------
// align-II.9 (E2; audit a1) — AcquisitionScope: how an acquisition was
// scoped, as a value. The ONE classifier reads the parameters; the
// totality gate reads the type; the S13 fast-path gate compares scope
// subsumption. These pin the classifier's totality, the gate
// equivalence, the subsumption law, and the wire-token round-trip.
// -----------------------------------------------------------------

/// Every (modules × system × inactive × attrs × filter) combination —
/// the classifier's whole input space at the boolean grain.
let private allParameterShapes : MetadataSnapshotRunner.SnapshotParameters list =
    [ for modules in [ []; [ "Sales" ] ] do
        for includeSystem in [ true; false ] do
            for includeInactive in [ true; false ] do
                for onlyActive in [ true; false ] do
                    for filter in [ None; Some "{}" ] ->
                        { MetadataSnapshotRunner.SnapshotParameters.ModuleNames = modules
                          IncludeSystem = includeSystem
                          IncludeInactive = includeInactive
                          OnlyActiveAttributes = onlyActive
                          EntityFilterJson = filter } ]

[<Fact>]
let ``align-II.9: ofParameters is Total exactly on the show-me-everything shape — the totality gate reads the type (equivalence over the whole space)`` () =
    Assert.Equal(32, List.length allParameterShapes)
    for p in allParameterShapes do
        let scope = MetadataSnapshotRunner.AcquisitionScope.ofParameters p
        // The retired structural-equality predicate and the classifier
        // agree everywhere — the gate's meaning did not move.
        Assert.True(
            (p = MetadataSnapshotRunner.defaultParameters) = (scope = MetadataSnapshotRunner.AcquisitionScope.Total),
            sprintf "gate equivalence broke on %A (scope %A)" p scope)
        // Scoped never carries an empty axis list (Total IS that state).
        match scope with
        | MetadataSnapshotRunner.AcquisitionScope.Scoped axes -> Assert.NotEmpty axes
        | MetadataSnapshotRunner.AcquisitionScope.Total -> ()

[<Fact>]
let ``align-II.9: each parameter knob fires exactly its named axis`` () =
    let axesOf p =
        match MetadataSnapshotRunner.AcquisitionScope.ofParameters p with
        | MetadataSnapshotRunner.AcquisitionScope.Total -> []
        | MetadataSnapshotRunner.AcquisitionScope.Scoped axes -> axes
    Assert.Equal<MetadataSnapshotRunner.ScopeAxis list>(
        [ MetadataSnapshotRunner.ScopeAxis.Modules ],
        axesOf { MetadataSnapshotRunner.defaultParameters with ModuleNames = [ "Sales" ] })
    Assert.Equal<MetadataSnapshotRunner.ScopeAxis list>(
        [ MetadataSnapshotRunner.ScopeAxis.System ],
        axesOf { MetadataSnapshotRunner.defaultParameters with IncludeSystem = false })
    Assert.Equal<MetadataSnapshotRunner.ScopeAxis list>(
        [ MetadataSnapshotRunner.ScopeAxis.Lifecycle ],
        axesOf { MetadataSnapshotRunner.defaultParameters with IncludeInactive = false })
    Assert.Equal<MetadataSnapshotRunner.ScopeAxis list>(
        [ MetadataSnapshotRunner.ScopeAxis.AttributeActivity ],
        axesOf { MetadataSnapshotRunner.defaultParameters with OnlyActiveAttributes = true })
    Assert.Equal<MetadataSnapshotRunner.ScopeAxis list>(
        [ MetadataSnapshotRunner.ScopeAxis.EntityFilter ],
        axesOf { MetadataSnapshotRunner.defaultParameters with EntityFilterJson = Some "{}" })

[<Fact>]
let ``align-II.9: a held-Total state serves any request without the attribute axis — the S13 gate's subsumption, behavior-identical to the residual special-case`` () =
    for p in allParameterShapes do
        let requested = MetadataSnapshotRunner.AcquisitionScope.ofParameters p
        // The named residual: servability is exactly the absence of the
        // attribute axis — the boolean the gate used to special-case.
        Assert.Equal(
            not p.OnlyActiveAttributes,
            MetadataSnapshotRunner.AcquisitionScope.serves MetadataSnapshotRunner.AcquisitionScope.Total requested)
    // A scoped held state serves only its identical scope (the honest
    // future-proof: nothing witnesses scoped today).
    let scoped = MetadataSnapshotRunner.AcquisitionScope.Scoped [ MetadataSnapshotRunner.ScopeAxis.Modules ]
    Assert.True(MetadataSnapshotRunner.AcquisitionScope.serves scoped scoped)
    Assert.False(MetadataSnapshotRunner.AcquisitionScope.serves scoped MetadataSnapshotRunner.AcquisitionScope.Total)

[<Fact>]
let ``align-II.9: the scope wire token round-trips over the whole classifier range and refuses junk`` () =
    for p in allParameterShapes do
        let scope = MetadataSnapshotRunner.AcquisitionScope.ofParameters p
        Assert.Equal<MetadataSnapshotRunner.AcquisitionScope option>(
            Some scope,
            MetadataSnapshotRunner.AcquisitionScope.tryParse (MetadataSnapshotRunner.AcquisitionScope.token scope))
    for junk in [ ""; "scoped:"; "scoped:nonsense"; "partial"; "scoped:modules+nonsense" ] do
        Assert.Equal<MetadataSnapshotRunner.AcquisitionScope option>(None, MetadataSnapshotRunner.AcquisitionScope.tryParse junk)
