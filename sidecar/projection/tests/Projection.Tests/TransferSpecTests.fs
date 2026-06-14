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

// -- NM-53: a resumable G10 no-op re-run replays the prior drop verdict ------

/// A clean transfer report (no live drops) carrying an optional replayed
/// prior-drop count — the shape a G10 no-op re-run produces.
let private reportWithReplay (replayed: int option) : Transfer.TransferReport =
    { Mode                = Transfer.Execute
      Kinds               = []
      UnbreakableCycleFks = []
      UnmatchedIdentities = []
      AmbiguousIdentities = []
      SkippedReferences   = []
      CaptureLaneDescents = []
      ReplayedPriorDrops  = replayed
      SyntheticUnsatisfiableFks = [] }

[<Fact>]
let ``NM-53: a no-op re-run with a replayed prior-drop count replays exit-9, not a clean exit-0`` () =
    // The first run legitimately dropped 3 FK-orphans (exit 9); the marker
    // persists the count. The re-run is a no-op (empty live drop-set) but
    // re-surfaces the prior verdict — `hasDrops` true, exit 9 — so a refresh
    // wrapper re-running to confirm is NOT misled into a clean exit-0.
    let replayed = reportWithReplay (Some 3)
    Assert.True(Transfer.hasDrops replayed, "a replayed prior drop count must count as drops")
    Assert.Equal(3, Transfer.droppedRowCount replayed)
    Assert.Equal(Transfer.DroppedReferencesExit, Transfer.exitCodeForReport false replayed)
    // --allow-drops still downgrades the replayed verdict to 0 (the operator
    // accepted the loss), exactly as it does for live drops.
    Assert.Equal(0, Transfer.exitCodeForReport true replayed)

[<Fact>]
let ``NM-53: a no-op re-run of a clean prior run (no drops) stays exit-0`` () =
    // A first run that dropped nothing records 0; the replay is benign.
    for replayed in [ None; Some 0 ] do
        let clean = reportWithReplay replayed
        Assert.False(Transfer.hasDrops clean, "a zero/absent replay is not drops")
        Assert.Equal(0, Transfer.droppedRowCount clean)
        Assert.Equal(0, Transfer.exitCodeForReport false clean)

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
        Column = ColumnRealization.create (col) (false) |> Result.value; IsPrimaryKey = true; IsMandatory = true }

let private attr (ownerParts: string list) (col: string) (typ: PrimitiveType) : Attribute =
    { Attribute.create (mkKey (ownerParts @ [col])) (mkName col) typ with
        Column = ColumnRealization.create (col) (false) |> Result.value; IsMandatory = true }

let private kindOf (parts: string list) (table: string) (attrs: Attribute list) : Kind =
    { Kind.create (mkKey parts) (mkName (List.last parts))
        (TableId.create "dbo" table |> Result.value)
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

// -- F3 / plane N3: the one physical-name resolution policy ---------------
// `CatalogResolution`'s physical lookups had drifted to case-sensitive `=`
// while `TransferSpec`'s matched case-insensitively. SQL Server treats
// identifiers case-insensitively under default collation, so the
// case-sensitive side silently failed to resolve a differently-cased
// operator ref. Both entry points now share `TableId.tableTextEquals` /
// `ColumnRealization.columnNameEquals`.

[<Fact>]
let ``F3: physical lookup resolves case-divergent names identically from every entry point`` () =
    // BEFORE this slice these three asserts returned None / no match.
    Assert.Equal(Some userKey, CatalogResolution.tryKindByPhysicalTable catalog "osusr_rc_user")
    Assert.Equal(Some userKey, CatalogResolution.tryKindByPhysicalTable catalog "OSUSR_RC_USER")
    let emailKey = mkKey [ "User"; "EMAIL" ]
    Assert.Equal(Some emailKey, CatalogResolution.tryAttributeByPhysical catalog "DBO" "osusr_rc_user" "email")
    // … and TransferSpec resolves the same divergent-case ref to the same kind.
    let viaTransfer =
        TransferSpec.resolveReconciliation catalog
            [ { TransferSpec.ReconcileEntry.Table = "OsUsr_Rc_User"; MatchColumn = "Email" } ]
        |> mustOk
    Assert.True(Map.containsKey userKey viaTransfer)

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

// -- parseUserMapCsv / resolveUserMap (slice 4.2) --------------------------

[<Fact>]
let ``parseUserMapCsv parses table,source,assigned rows`` () =
    let rows =
        TransferSpec.parseUserMapCsv "OSUSR_RC_USER,280,18\nOSUSR_RC_USER,281,19"
        |> mustOk
    Assert.Equal(2, rows.Length)
    Assert.Equal("OSUSR_RC_USER", rows.[0].Table)
    Assert.Equal("280", rows.[0].Source)
    Assert.Equal("18", rows.[0].Assigned)

[<Fact>]
let ``parseUserMapCsv skips a leading header line and blank lines`` () =
    let rows =
        TransferSpec.parseUserMapCsv "table,source,assigned\n\nOSUSR_RC_USER,280,18\n\n"
        |> mustOk
    Assert.Equal(1, rows.Length)
    Assert.Equal("280", (List.head rows).Source)

[<Fact>]
let ``parseUserMapCsv rejects a line without exactly three fields`` () =
    match TransferSpec.parseUserMapCsv "OSUSR_RC_USER,280" with
    | Ok _    -> Assert.Fail "expected Error"
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "transfer.userMap.shape")

[<Fact>]
let ``resolveUserMap resolves rows to a per-table ManualOverride strategy`` () =
    let map =
        TransferSpec.resolveUserMap catalog
            [ { TransferSpec.UserMapEntry.Table = "OSUSR_RC_USER"; Source = "280"; Assigned = "18" }
              { TransferSpec.UserMapEntry.Table = "OSUSR_RC_USER"; Source = "281"; Assigned = "19" } ]
        |> mustOk
    match Map.tryFind userKey map with
    | Some (ReconciliationStrategy.ManualOverride overrides) ->
        Assert.Equal(Some (AssignedKey.ofString "18"), Map.tryFind (SourceKey.ofString "280") overrides)
        Assert.Equal(Some (AssignedKey.ofString "19"), Map.tryFind (SourceKey.ofString "281") overrides)
    | other -> Assert.Fail(sprintf "expected ManualOverride; got %A" other)

[<Fact>]
let ``resolveUserMap surfaces tableNotFound for an unknown table`` () =
    match
        TransferSpec.resolveUserMap catalog
            [ { TransferSpec.UserMapEntry.Table = "NOT_THERE"; Source = "1"; Assigned = "2" } ]
    with
    | Ok _    -> Assert.Fail "expected Error"
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "transfer.userMap.tableNotFound")

[<Fact>]
let ``resolveUserMap rejects a duplicate source key within one table`` () =
    match
        TransferSpec.resolveUserMap catalog
            [ { TransferSpec.UserMapEntry.Table = "OSUSR_RC_USER"; Source = "280"; Assigned = "18" }
              { TransferSpec.UserMapEntry.Table = "OSUSR_RC_USER"; Source = "280"; Assigned = "19" } ]
    with
    | Ok _    -> Assert.Fail "expected Error"
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "transfer.userMap.duplicateSource")

[<Fact>]
let ``resolveAllReconciliation merges MatchByColumn and ManualOverride across distinct kinds`` () =
    let map =
        TransferSpec.resolveAllReconciliation catalog
            [ { TransferSpec.ReconcileEntry.Table = "OSUSR_RC_USER"; MatchColumn = "EMAIL" } ]
            [ { TransferSpec.UserMapEntry.Table = "OSUSR_RC_ORDER"; Source = "1"; Assigned = "9" } ]
        |> mustOk
    Assert.Equal(2, Map.count map)
    match Map.tryFind userKey map with
    | Some (ReconciliationStrategy.MatchByColumn _) -> ()
    | other -> Assert.Fail(sprintf "expected MatchByColumn for User; got %A" other)

[<Fact>]
let ``resolveAllReconciliation rejects a kind reconciled by both strategies`` () =
    match
        TransferSpec.resolveAllReconciliation catalog
            [ { TransferSpec.ReconcileEntry.Table = "OSUSR_RC_USER"; MatchColumn = "EMAIL" } ]
            [ { TransferSpec.UserMapEntry.Table = "OSUSR_RC_USER"; Source = "280"; Assigned = "18" } ]
    with
    | Ok _    -> Assert.Fail "expected Error"
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "transfer.reconcile.strategyConflict")

// ---------------------------------------------------------------------------
// NM-49: the capture-lane descent positioning is TOTAL — `CaptureLane.ladderFrom`
// returns the descent suffix at `preferred`, and an unknown preferred lane
// begins at the ladder HEAD rather than the empty tail that the descent loop
// mislabels "capture ladder exhausted".
// ---------------------------------------------------------------------------

[<Fact>]
let ``NM-49: ladderFrom positions the descent at a known preferred lane (inclusive)`` () =
    Assert.Equal<CaptureLane list>(
        CaptureLane.ladder,
        CaptureLane.ladderFrom CaptureLane.StagedMergeOutput)
    Assert.Equal<CaptureLane list>(
        [ CaptureLane.StagedMergeOutputInto; CaptureLane.RowwiseScopeIdentity ],
        CaptureLane.ladderFrom CaptureLane.StagedMergeOutputInto)
    Assert.Equal<CaptureLane list>(
        [ CaptureLane.RowwiseScopeIdentity ],
        CaptureLane.ladderFrom CaptureLane.RowwiseScopeIdentity)

[<Fact>]
let ``NM-49: ladderFrom is total — every closed CaptureLane variant yields a non-empty descent`` () =
    // The descent must NEVER be empty (an empty descent is the "exhausted"
    // mislabel). Exhaustively over the closed DU.
    for lane in [ CaptureLane.StagedMergeOutput; CaptureLane.StagedMergeOutputInto; CaptureLane.RowwiseScopeIdentity ] do
        let descent = CaptureLane.ladderFrom lane
        Assert.NotEmpty descent
        // The descent is always a suffix of the full ladder (the conservative
        // order is preserved — no rung skipped, no rung reordered).
        Assert.True(
            CaptureLane.ladder |> List.skipWhile (fun l -> l <> List.head descent) = descent
            || descent = CaptureLane.ladder,
            sprintf "ladderFrom %A is not a ladder suffix: %A" lane descent)
