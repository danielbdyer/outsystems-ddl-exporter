module Projection.Tests.TransferSpecTests

open Xunit
open Projection.Core
open Projection.Pipeline

// Pure tests for the `transfer`-verb spec parsing in Pipeline. The CLI's
// Argu DU is a thin operator-facing wrapper; the substance — env/file
// connection specs and `<table>:<column>` reconcile entries → typed
// values, then resolved against the reconstructed catalog — lives in
// `TransferSpec` so the test pool covers it without a CLI dependency.

let private mustOk r = match r with Ok v -> v | Error es -> failwithf "fixture: %A" es
let private mkKey (parts: string list) : SsKey = SsKey.synthesizedComposite "OS_TEST_TSPEC" parts |> mustOk
let private mkName (s: string) : Name = Name.create s |> mustOk

// -- parseConnectionSpec ---------------------------------------------------

[<Fact>]
let ``parseConnectionSpec env:NAME parses to ConnectionRef.EnvVar`` () =
    let r = TransferSpec.parseConnectionSpec "env:DEV_CONN" |> mustOk
    Assert.Equal(ConnectionRef.EnvVar "DEV_CONN", r)

[<Fact>]
let ``parseConnectionSpec file:PATH parses to ConnectionRef.File`` () =
    let r = TransferSpec.parseConnectionSpec "file:/tmp/x" |> mustOk
    Assert.Equal(ConnectionRef.File "/tmp/x", r)

[<Fact>]
let ``parseConnectionSpec rejects a spec missing the prefix`` () =
    match TransferSpec.parseConnectionSpec "DEV_CONN" with
    | Ok _    -> Assert.Fail "expected Error"
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "transfer.connection.specShape")

[<Fact>]
let ``parseConnectionSpec rejects an unknown prefix`` () =
    match TransferSpec.parseConnectionSpec "vault:secret-name" with
    | Ok _    -> Assert.Fail "expected Error"
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "transfer.connection.specPrefix")

[<Fact>]
let ``parseConnectionSpec rejects an empty value after the prefix`` () =
    match TransferSpec.parseConnectionSpec "env:" with
    | Ok _    -> Assert.Fail "expected Error"
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "transfer.connection.specEmptyValue")

[<Fact>]
let ``parseConnectionSpec rejects a blank spec`` () =
    match TransferSpec.parseConnectionSpec "   " with
    | Ok _    -> Assert.Fail "expected Error"
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "transfer.connection.specEmpty")

// -- parseReconcileSpec ----------------------------------------------------

[<Fact>]
let ``parseReconcileSpec table:column parses to ReconcileEntry`` () =
    let r = TransferSpec.parseReconcileSpec "OSUSR_RC_USER:EMAIL" |> mustOk
    Assert.Equal("OSUSR_RC_USER", r.Table)
    Assert.Equal("EMAIL", r.MatchColumn)

[<Fact>]
let ``parseReconcileSpec rejects a spec missing the colon`` () =
    match TransferSpec.parseReconcileSpec "OSUSR_RC_USER" with
    | Ok _    -> Assert.Fail "expected Error"
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "transfer.reconcile.specShape")

[<Fact>]
let ``parseReconcileSpec rejects an empty table`` () =
    match TransferSpec.parseReconcileSpec ":EMAIL" with
    | Ok _    -> Assert.Fail "expected Error"
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "transfer.reconcile.tableEmpty")

[<Fact>]
let ``parseReconcileSpec rejects an empty match-column`` () =
    match TransferSpec.parseReconcileSpec "OSUSR_RC_USER:" with
    | Ok _    -> Assert.Fail "expected Error"
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "transfer.reconcile.columnEmpty")

// -- resolveReconciliation -------------------------------------------------

let private pk (ownerParts: string list) (col: string) : Attribute =
    { Attribute.create (mkKey (ownerParts @ [col])) (mkName col) Integer with
        Column = { ColumnName = col; IsNullable = false }; IsPrimaryKey = true; IsMandatory = true }

let private attr (ownerParts: string list) (col: string) (typ: PrimitiveType) : Attribute =
    { Attribute.create (mkKey (ownerParts @ [col])) (mkName col) typ with
        Column = { ColumnName = col; IsNullable = false }; IsMandatory = true }

let private kindOf (parts: string list) (table: string) (attrs: Attribute list) : Kind =
    { Kind.create (mkKey parts) (mkName (List.last parts))
        { Schema = "dbo"; Table = table; Catalog = None }
        attrs
      with References = []; Indexes = []; ColumnChecks = [] }

let private userKey = mkKey ["User"]
let private userKind = kindOf ["User"] "OSUSR_RC_USER" [ pk ["User"] "ID"; attr ["User"] "EMAIL" Text ]
let private orderKind = kindOf ["Order"] "OSUSR_RC_ORDER" [ pk ["Order"] "ID" ]

let private catalog : Catalog =
    IRBuilders.mkCatalog [ IRBuilders.mkModule (mkKey ["Module"]) (mkName "M") [ userKind; orderKind ] ]

[<Fact>]
let ``resolveReconciliation maps a table+column to (Kind.SsKey, MatchByColumn attribute.Name)`` () =
    let map =
        TransferSpec.resolveReconciliation catalog
            [ { TransferSpec.ReconcileEntry.Table = "OSUSR_RC_USER"; MatchColumn = "EMAIL" } ]
        |> mustOk
    Assert.Equal(Some (ReconciliationStrategy.MatchByColumn (mkName "EMAIL")), Map.tryFind userKey map)

[<Fact>]
let ``resolveReconciliation matches table and column names case-insensitively`` () =
    let map =
        TransferSpec.resolveReconciliation catalog
            [ { TransferSpec.ReconcileEntry.Table = "osusr_rc_user"; MatchColumn = "email" } ]
        |> mustOk
    Assert.True(Map.containsKey userKey map)

[<Fact>]
let ``resolveReconciliation surfaces tableNotFound when no kind matches`` () =
    match
        TransferSpec.resolveReconciliation catalog
            [ { TransferSpec.ReconcileEntry.Table = "NOT_THERE"; MatchColumn = "X" } ]
    with
    | Ok _    -> Assert.Fail "expected Error"
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "transfer.reconcile.tableNotFound")

[<Fact>]
let ``resolveReconciliation surfaces columnNotFound when the kind has no such column`` () =
    match
        TransferSpec.resolveReconciliation catalog
            [ { TransferSpec.ReconcileEntry.Table = "OSUSR_RC_USER"; MatchColumn = "NOT_A_COLUMN" } ]
    with
    | Ok _    -> Assert.Fail "expected Error"
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "transfer.reconcile.columnNotFound")

[<Fact>]
let ``resolveReconciliation aggregates every spec error in one pass`` () =
    match
        TransferSpec.resolveReconciliation catalog
            [ { TransferSpec.ReconcileEntry.Table = "NOT_THERE"; MatchColumn = "X" }
              { TransferSpec.ReconcileEntry.Table = "OSUSR_RC_USER"; MatchColumn = "NOT_A_COLUMN" } ]
    with
    | Ok _    -> Assert.Fail "expected Error"
    | Error es ->
        Assert.Contains(es, fun (e: ValidationError) -> e.Code = "transfer.reconcile.tableNotFound")
        Assert.Contains(es, fun (e: ValidationError) -> e.Code = "transfer.reconcile.columnNotFound")

[<Fact>]
let ``resolveReconciliation rejects a kind specified twice`` () =
    match
        TransferSpec.resolveReconciliation catalog
            [ { TransferSpec.ReconcileEntry.Table = "OSUSR_RC_USER"; MatchColumn = "EMAIL" }
              { TransferSpec.ReconcileEntry.Table = "OSUSR_RC_USER"; MatchColumn = "ID" } ]
    with
    | Ok _    -> Assert.Fail "expected Error"
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "transfer.reconcile.duplicateKind")
