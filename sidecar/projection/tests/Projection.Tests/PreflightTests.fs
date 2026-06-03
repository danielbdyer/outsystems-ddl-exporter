module Projection.Tests.PreflightTests

open Xunit
open Projection.Core
open Projection.Adapters.Sql
open Projection.Pipeline

// 6.B.1 — pure (DB-free) witnesses for the Decision↔Data pre-flight. The
// LiveProfiler null-count evidence is the cache; the tightening overlay is the
// Decision. `dataViolatesTightening` flags each EnforceNotNull column whose
// source data carries NULLs — the coupling that would otherwise crash the
// two-phase load mid-write. The Docker witness (TransferCanaryTests) drives the
// same decision against a live source via `tighteningPreflight`.

let private mustOk r = match r with Ok v -> v | Error es -> failwithf "fixture: %A" es
let private attrKey (s: string) : SsKey = SsKey.synthesizedComposite "OS_BTEST_ATTR" [ s ] |> mustOk
let private kindKey (s: string) : SsKey = SsKey.synthesizedComposite "OS_BTEST_KIND" [ s ] |> mustOk

let private cacheWith (kindK: SsKey) (nullCounts: (SsKey * int64) list) : EvidenceCache =
    let ck : CachedKind =
        { KindKey      = kindK
          RowCount     = 10L
          NullCounts   = Map.ofList nullCounts
          Columns      = []
          ColumnsByKey = Map.empty }
    { Kinds = Map.ofList [ kindK, ck ] }

[<Fact>]
let ``6.B.1: EnforceNotNull on a NULL-bearing column is a tightening violation`` () =
    let noteK = attrKey "Note"
    let tK = kindKey "Ticket"
    let cache = cacheWith tK [ noteK, 3L ]
    let overlay = { DecisionOverlay.empty with EnforceNotNull = Set.singleton noteK }
    match Preflight.dataViolatesTightening cache overlay with
    | [ v ] ->
        Assert.Equal<SsKey>(noteK, v.AttributeKey)
        Assert.Equal<SsKey>(tK, v.KindKey)
        Assert.Equal(3L, v.NullCount)
    | other -> Assert.Fail(sprintf "expected exactly one violation, got %A" other)

[<Fact>]
let ``6.B.1: EnforceNotNull on a column with zero NULLs is not a violation`` () =
    let noteK = attrKey "Note"
    let cache = cacheWith (kindKey "Ticket") [ noteK, 0L ]
    let overlay = { DecisionOverlay.empty with EnforceNotNull = Set.singleton noteK }
    Assert.Empty(Preflight.dataViolatesTightening cache overlay)

[<Fact>]
let ``6.B.1: a NULL-bearing column NOT tightened is not a violation`` () =
    let noteK = attrKey "Note"
    let cache = cacheWith (kindKey "Ticket") [ noteK, 5L ]
    // Empty overlay — the operator did not tighten this column.
    Assert.Empty(Preflight.dataViolatesTightening cache DecisionOverlay.empty)

[<Fact>]
let ``6.B.1: violations are deterministic across attributes (sorted by identity)`` () =
    let tK = kindKey "Ticket"
    let a = attrKey "Alpha"
    let b = attrKey "Beta"
    let cache = cacheWith tK [ a, 1L; b, 2L ]
    let overlay = { DecisionOverlay.empty with EnforceNotNull = Set.ofList [ a; b ] }
    let names =
        Preflight.dataViolatesTightening cache overlay
        |> List.map (fun v -> SsKey.rootOriginal v.AttributeKey)
    Assert.Equal<string list>(List.sort names, names)

// ---------------------------------------------------------------------------
// A1 — connection pre-flight (T-VI spanning). Pure (DB-free) witnesses over
// `connectionViolations`; the live probe (`connectionPreflight`) drives the
// same decision against real endpoints via a Docker witness.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A1: both endpoints reachable + credentialed → no connection violation`` () =
    let evidence =
        [ { Preflight.Role = "source"; Preflight.IsReachable = true; Preflight.Login = Some "svc_migrate" }
          { Preflight.Role = "sink";   Preflight.IsReachable = true; Preflight.Login = Some "svc_migrate" } ]
    Assert.Empty(Preflight.connectionViolations evidence)

[<Fact>]
let ``A1: an unreachable endpoint is a connection violation (not a mid-load failure)`` () =
    let evidence =
        [ { Preflight.Role = "source"; Preflight.IsReachable = true;  Preflight.Login = Some "svc" }
          { Preflight.Role = "sink";   Preflight.IsReachable = false; Preflight.Login = None } ]
    match Preflight.connectionViolations evidence with
    | [ v ] -> Assert.Equal("sink", v.Role)
    | other -> Assert.Fail(sprintf "expected exactly one violation, got %A" other)

[<Fact>]
let ``A1: a reachable endpoint with no authenticated login is a connection violation`` () =
    let evidence =
        [ { Preflight.Role = "sink"; Preflight.IsReachable = true; Preflight.Login = None } ]
    match Preflight.connectionViolations evidence with
    | [ v ] -> Assert.Contains("no authenticated login", v.Reason)
    | other -> Assert.Fail(sprintf "expected one violation, got %A" other)

// ---------------------------------------------------------------------------
// A2 — permission pre-flight (T-VI spanning). Pure witnesses over
// `permissionViolations` / `permissionPreflight`; the grant capture
// (`captureGrantEvidence`) is the survey-gated probe.
// ---------------------------------------------------------------------------

let private granted (pairs: (string * string) list) : Preflight.GrantEvidence =
    { Granted = Set.ofList pairs }

[<Fact>]
let ``A2: a planned INSERT the grant covers (object scope) is not a violation`` () =
    let planned = [ { Preflight.Schema = "dbo"; Preflight.Table = "Customer"; Preflight.Action = Preflight.Insert } ]
    let grant = granted [ ("dbo.Customer", "INSERT") ]
    Assert.Empty(Preflight.permissionViolations planned grant)

[<Fact>]
let ``A2: a database-scope grant covers a planned write at any object`` () =
    let planned = [ { Preflight.Schema = "dbo"; Preflight.Table = "Customer"; Preflight.Action = Preflight.Insert } ]
    let grant = granted [ ("", "INSERT") ]   // database-wide INSERT
    Assert.Empty(Preflight.permissionViolations planned grant)

[<Fact>]
let ``A2: a write-denied sink surfaces a permission violation (not silent zero rows)`` () =
    let planned = [ { Preflight.Schema = "dbo"; Preflight.Table = "Customer"; Preflight.Action = Preflight.Insert } ]
    let grant = granted [ ("dbo.Customer", "SELECT") ]   // can read, cannot write
    match Preflight.permissionViolations planned grant with
    | [ v ] ->
        Assert.Equal("dbo.Customer", v.Object)
        Assert.Equal(Preflight.Insert, v.Action)
    | other -> Assert.Fail(sprintf "expected one violation, got %A" other)

[<Fact>]
let ``A2: permissionPreflight refuses with migrate.insufficientGrant when a write is uncovered`` () =
    let planned =
        [ { Preflight.Schema = "dbo"; Preflight.Table = "Customer"; Preflight.Action = Preflight.Insert }
          { Preflight.Schema = "dbo"; Preflight.Table = "Order";    Preflight.Action = Preflight.Alter } ]
    let grant = granted [ ("dbo.Customer", "INSERT") ]   // Order ALTER uncovered
    match Preflight.permissionPreflight grant planned with
    | Ok () -> Assert.Fail "expected a refusal"
    | Error errs ->
        Assert.Contains(errs, fun (e: ValidationError) -> e.Code = "migrate.insufficientGrant")

[<Fact>]
let ``A2: permissionPreflight passes when every planned write is covered`` () =
    let planned = [ { Preflight.Schema = "dbo"; Preflight.Table = "Customer"; Preflight.Action = Preflight.Insert } ]
    let grant = granted [ ("dbo.Customer", "INSERT") ]
    match Preflight.permissionPreflight grant planned with
    | Ok () -> ()
    | Error errs -> Assert.Fail(sprintf "expected Ok, got %A" errs)
