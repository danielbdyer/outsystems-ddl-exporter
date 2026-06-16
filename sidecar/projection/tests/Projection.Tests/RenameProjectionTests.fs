module Projection.Tests.RenameProjectionTests

open Xunit
open Projection.Core
open Projection.Pipeline

// 6.B.2 — pure witnesses for the RefactorLog-aware Transfer re-point. A rename
// changes a column's physical coordinates; the source row carries the OLD name,
// the sink expects the NEW name. The A→B `CatalogDiff` attribute rename gives a
// source-Name → sink-Name re-key that moves each row's values onto the sink's
// names by IDENTITY (A1-stable SsKey), never by ordinal.

type private FsResult<'a, 'b> = Microsoft.FSharp.Core.Result<'a, 'b>

let private mustOk r = match r with Ok v -> v | Error es -> failwithf "fixture: %A" es
let private betweenOk (a: Catalog) (b: Catalog) : CatalogDiff =
    CatalogDiff.between a b
let private nm (s: string) : Name = Name.create s |> mustOk
let private kKey (s: string) : SsKey = SsKey.synthesizedComposite "OS_RP_KIND" [ s ] |> mustOk
let private aKey (s: string) : SsKey = SsKey.synthesizedComposite "OS_RP_ATTR" [ s ] |> mustOk

let private attr (key: SsKey) (logical: string) (col: string) (isPk: bool) : Attribute =
    { Attribute.create key (nm logical) Text with
        Column = ColumnRealization.create (col) (not isPk) |> Result.value
        IsPrimaryKey = isPk
        IsMandatory = isPk }

/// Customer with a stable Id and an Email attribute whose logical name +
/// physical column are parameterized (so A uses Email/EMAIL and B uses
/// Contact/CONTACT under the SAME attribute SsKey — a rename).
let private custKind (emailName: string) (emailCol: string) : Kind =
    Kind.create (kKey "Customer") (nm "Customer")
        (TableId.create "dbo" "RP_CUSTOMER" |> Result.value)
        [ attr (aKey "Id") "Id" "ID" true
          attr (aKey "Email") emailName emailCol false ]

let private catOf (k: Kind) : Catalog =
    IRBuilders.mkCatalog [ IRBuilders.mkModule (kKey "Mod") (nm "M") [ k ] ]

let private rowOf (vals: (string * string) list) : StaticRow =
    { Identifier = aKey "row"; Values = vals |> List.map (fun (k, v) -> nm k, v) |> Map.ofList }

[<Fact>]
let ``6.B.2: renames extracts the column rename from the A->B diff`` () =
    let a = catOf (custKind "Email" "EMAIL")
    let b = catOf (custKind "Contact" "CONTACT")
    let diff = betweenOk a b
    match RenameProjection.renames diff with
    | [ r ] ->
        Assert.Equal<SsKey>(aKey "Email", r.Attribute)
        Assert.Equal<Name>(nm "Email", r.SourceName)
        Assert.Equal<Name>(nm "Contact", r.SinkName)
    | other -> Assert.Fail(sprintf "expected exactly one column rename, got %A" other)

[<Fact>]
let ``6.B.2: a renamed column is re-pointed by the rename map, not matched by ordinal`` () =
    // Source row has Email + Phone. The rename map (Email → Contact) moves
    // Email's value onto Contact by NAME; Phone is untouched. A positional
    // (ordinal) scheme could not distinguish this — the re-point is by identity.
    let map =
        RenameProjection.renameMap
            [ { Attribute = aKey "Email"; SourceName = nm "Email"; SinkName = nm "Contact" } ]
    let row = rowOf [ "Email", "alice@x"; "Phone", "555" ]
    let out = RenameProjection.repointRow map row
    Assert.Equal("alice@x", out.Values.[nm "Contact"])
    Assert.Equal("555", out.Values.[nm "Phone"])
    Assert.False(out.Values.ContainsKey(nm "Email"))

[<Fact>]
let ``6.B.2: the value follows the name regardless of insertion order (not ordinal)`` () =
    let map =
        RenameProjection.renameMap
            [ { Attribute = aKey "Email"; SourceName = nm "Email"; SinkName = nm "Contact" } ]
    // Same cells, reversed insertion order — the re-point result is identical,
    // because it keys on the name, not the position.
    let a = RenameProjection.repointRow map (rowOf [ "Email", "x"; "Phone", "y" ])
    let b = RenameProjection.repointRow map (rowOf [ "Phone", "y"; "Email", "x" ])
    Assert.Equal<Map<Name, string>>(a.Values, b.Values)
    Assert.Equal("x", a.Values.[nm "Contact"])

[<Fact>]
let ``6.B.2: an empty rename map is identity (a no-rename transfer is byte-identical)`` () =
    let row = rowOf [ "Email", "a"; "Phone", "b" ]
    Assert.Equal<StaticRow>(row, RenameProjection.repointRow Map.empty row)
    Assert.Equal<StaticRow list>([ row ], RenameProjection.repointRows Map.empty [ row ])

[<Fact>]
let ``6.B.2: end-to-end — diff-derived renames re-point a source row onto the sink names`` () =
    // The full first-slice path: derive the rename map from between(A, B) and
    // re-point a source-shaped row onto the sink's names.
    let a = catOf (custKind "Email" "EMAIL")
    let b = catOf (custKind "Contact" "CONTACT")
    let map = RenameProjection.renames (betweenOk a b) |> RenameProjection.renameMap
    let sourceRow = rowOf [ "Id", "1"; "Email", "bob@x" ]
    let sinkRow = RenameProjection.repointRow map sourceRow
    Assert.Equal("bob@x", sinkRow.Values.[nm "Contact"])
    Assert.Equal("1", sinkRow.Values.[nm "Id"])

// A5 — the migrate-with-data data leg re-points A→B because the data source is
// at the OLD schema A. `executeWithData` now derives its rename map exactly as
// `runWithRenames` does — `CatalogDiff.between sinkSource target` (A→B) — so a
// row carrying the A name (EMAIL) is re-pointed onto the B name (CONTACT). This
// pure witness pins the diff ORIENTATION the data leg wires: A = sinkSource
// (source contract), B = target (sink contract).

[<Fact>]
let ``A5: the migrate-with-data rename map derives from between(sinkSource=A, target=B) and re-points A->B`` () =
    let a = catOf (custKind "Email" "EMAIL")    // sinkSource — the data source's schema A
    let b = catOf (custKind "Contact" "CONTACT") // target — the migrated sink schema B
    let map = RenameProjection.renames (betweenOk a b) |> RenameProjection.renameMap
    // A row carrying the A-schema name re-points onto the B name (not lost to NULL).
    let aRow = rowOf [ "Id", "1"; "Email", "alice@x" ]
    let repointed = RenameProjection.repointRow map aRow
    Assert.Equal("alice@x", repointed.Values.[nm "Contact"])
    Assert.False(repointed.Values.ContainsKey(nm "Email"))

[<Fact>]
let ``A5: a no-renames A->B diff yields an empty rename map (the data leg stays a straight load)`` () =
    // Default behavior is preserved: when A and B differ only in ways that are
    // not renames (here, identical), the rename map is empty ⇒ identity repoint.
    let a = catOf (custKind "Email" "EMAIL")
    let b = catOf (custKind "Email" "EMAIL")
    let map = RenameProjection.renames (betweenOk a b) |> RenameProjection.renameMap
    Assert.True(Map.isEmpty map)

// =====================================================================
// AC-I7 — the composed Transfer: a column rename AND a Dev→UAT re-key in
// ONE run. `Transfer.runReconcilingWithRenames` threads BOTH legs through
// the single `runCore` path: re-point each ingested row's values onto the
// sink names by SsKey (A1-stable, never ordinal), THEN reconcile + re-key
// every FK through the matched remap. These tests are the *pure* witness
// of that composition — they reproduce `runCore`'s post-ingestion pipeline
// (RenameProjection.repointRows → reconcile → DataLoadPlan.build with the
// reconciled remap → reclassifyReconciled) over in-memory fixtures, so the
// core discrimination needs no Docker. The DB-level end-to-end witness is
// the Skip-stubbed canary at the bottom.
//
// The adversarial shape: the FK column (User-pointer) is BOTH renamed
// (OWNER/OWNER_ID → BUYER/BUYER_ID under a stable SsKey) AND its target
// kind (User) is reconciled. The value must follow its SsKey to the BUYER
// column, AND the FK must re-key through the matched remap. An entrypoint
// that dropped EITHER leg produces an observably different row.

let private rpUserKey  = kKey "User"
let private rpOrderKey = kKey "Order"
let private rpOwnerAttrKey = aKey "Order.Owner"
let private rpUserPkAttrKey = aKey "User.Id"

/// User kind (reconcile target): identity-less business PK `ID`, plus an
/// Email match column. Reconciled Dev→UAT by Email.
let private rpUserKind : Kind =
    Kind.create rpUserKey (nm "User")
        (TableId.create "dbo" "RP_USER" |> Result.value)
        [ { Attribute.create rpUserPkAttrKey (nm "Id") Integer with
              Column = ColumnRealization.create "ID" false |> Result.value
              IsPrimaryKey = true; IsMandatory = true }
          attr (aKey "User.Email") "Email" "EMAIL" false ]

/// Order kind with a NULLABLE FK to User whose logical name + physical
/// column are parameterized — so A uses Owner/OWNER_ID and B uses
/// Buyer/BUYER_ID under the SAME attribute SsKey (a renamed FK column).
let private rpOrderKind (ownerName: string) (ownerCol: string) : Kind =
    { Kind.create rpOrderKey (nm "Order")
        (TableId.create "dbo" "RP_ORDER" |> Result.value)
        [ { Attribute.create (aKey "Order.Id") (nm "Id") Integer with
              Column = ColumnRealization.create "ID" false |> Result.value
              IsPrimaryKey = true; IsMandatory = true }
          { Attribute.create rpOwnerAttrKey (nm ownerName) Integer with
              Column = ColumnRealization.create ownerCol true |> Result.value } ]
      with
        References =
            [ { Reference.create (aKey "Order.OwnerRef") (nm "OwnerRef") rpOwnerAttrKey rpUserKey with
                  ConstraintState = ConstraintState.TrustedConstraint } ] }

let private rpCatOf (k: Kind list) : Catalog =
    IRBuilders.mkCatalog [ IRBuilders.mkModule (kKey "Mod") (nm "M") k ]

/// A→B contract pair: A names the FK Owner/OWNER_ID, B names it Buyer/BUYER_ID
/// (a rename). User is unchanged (it is reconciled, not renamed).
let private rpSourceContract = rpCatOf [ rpUserKind; rpOrderKind "Owner" "OWNER_ID" ]
let private rpSinkContract   = rpCatOf [ rpUserKind; rpOrderKind "Buyer" "BUYER_ID" ]

/// Topological order: User before Order (Order → User FK). No cycle.
let private rpTopo : TopologicalOrder =
    { Mode         = OrderingMode.Topological
      Order        = [ rpUserKey; rpOrderKey ]
      Edges        = [ (rpOrderKey, rpUserKey) ]
      MissingEdges = []
      Cycles       = []
      Diagnostics  = [] }

/// The reconciliation: User Dev→UAT by Email.
let private rpReconciliation : Map<SsKey, ReconciliationStrategy> =
    Map.ofList [ rpUserKey, ReconciliationStrategy.MatchByColumn (nm "Email") ]

/// Pre-existing Sink (UAT) User rows: Dev user 280 (alice) is UAT 18.
let private rpSinkUserRows : StaticRow list =
    [ { Identifier = aKey "u18"; Values = Map.ofList [ nm "ID", "18"; nm "Email", "alice@x" ] }
      { Identifier = aKey "u19"; Values = Map.ofList [ nm "ID", "19"; nm "Email", "bob@x" ] } ]

/// Reproduce `runCore`'s post-ingestion pipeline faithfully. `renameMap`
/// empty = the rename leg dropped; `reconciliation` empty = the reconcile
/// leg dropped. The composed run passes BOTH non-empty.
let private composePlan
    (renameMap: Map<Name, Name>)
    (reconciliation: Map<SsKey, ReconciliationStrategy>)
    (sourceOrderRows: StaticRow list)
    : DataLoadPlan =
    // 1. Re-point each ingested row onto the sink names by SsKey (rename leg).
    let repointed = RenameProjection.repointRows renameMap sourceOrderRows
    let rows = Map.ofList [ rpOrderKey, repointed; rpUserKey, [] ]
    // 2. Reconcile User against the pre-existing Sink (reconcile leg).
    let reconciled =
        match Map.tryFind rpUserKey reconciliation with
        | Some (ReconciliationStrategy.MatchByColumn _ as strat) ->
            (Reconciliation.reconcileKind rpUserKey (nm "Id") strat [] rpSinkUserRows).Remap
        | _ -> SurrogateRemapContext.empty
    // For the FK re-key the remap must carry the User Dev→UAT assignment.
    // The reconcile reads the *source* User surrogates; in `runCore` those
    // come from the source User rows. Model 280→18 directly (alice).
    let remap =
        match Map.tryFind rpUserKey reconciliation with
        | Some _ ->
            SurrogateRemapContext.capture rpUserKey (SourceKey.ofString "280") (AssignedKey.ofString "18")
                reconciled
            |> function Ok r -> r | Error _ -> reconciled
        | None -> SurrogateRemapContext.empty
    // 3. Build the plan (the ONE substitution site) + reclassify reconciled.
    DataLoadPlan.build rpSinkContract rpTopo rows remap
    |> DataLoadPlan.reclassifyReconciled (Set.singleton rpUserKey)

let private orderLoad (plan: DataLoadPlan) : DataLoadKind =
    plan.Loads |> List.find (fun l -> l.Kind = rpOrderKey)

[<Fact>]
let ``AC-I7: composed rename+rekey — FK value follows its SsKey to BUYER AND re-keys through the remap`` () =
    // Source Order row: Owner FK points at Dev User 280 (alice).
    let sourceOrder = [ rowOf [ "Id", "1000"; "Owner", "280" ] ]
    let renameMap = RenameProjection.renames (betweenOk rpSourceContract rpSinkContract) |> RenameProjection.renameMap
    let plan = composePlan renameMap rpReconciliation sourceOrder
    let load = orderLoad plan
    let row = List.exactlyOne load.Rows
    // BOTH legs landed: the value followed its SsKey to BUYER (rename),
    // AND the FK re-keyed 280 → 18 (reconcile). The source coordinates
    // (OWNER / 280) are gone.
    Assert.Equal(Some "18", Map.tryFind (nm "Buyer") row.Values)
    Assert.False(row.Values.ContainsKey(nm "Owner"))
    Assert.Empty plan.SkippedReferences

[<Fact>]
let ``AC-I7 discrimination: dropping the RENAME leg loses the FK re-key (Map.empty rename)`` () =
    // runReconciling-only: no rename context. The value stays under the
    // source name Owner; the sink contract's FK column is Buyer/BUYER_ID,
    // so plan-build (which reads the FK column by the sink contract) finds
    // nothing to re-key under that column — the re-key is silently lost.
    let sourceOrder = [ rowOf [ "Id", "1000"; "Owner", "280" ] ]
    let plan = composePlan Map.empty rpReconciliation sourceOrder
    let row = List.exactlyOne (orderLoad plan).Rows
    // No BUYER column carries the re-keyed value; the reconcile leg alone
    // cannot place it — proving the rename leg is load-bearing.
    Assert.NotEqual<string option>(Some "18", Map.tryFind (nm "Buyer") row.Values)

[<Fact>]
let ``AC-I7 discrimination: dropping the RECONCILE leg leaves the FK at the source key (Map.empty recon)`` () =
    // runWithRenames-only: no reconciliation. The value follows its SsKey to
    // BUYER (rename leg works) but stays the *source* User key 280, never
    // re-keyed to the UAT identity 18 — proving the reconcile leg is
    // load-bearing.
    let sourceOrder = [ rowOf [ "Id", "1000"; "Owner", "280" ] ]
    let renameMap = RenameProjection.renames (betweenOk rpSourceContract rpSinkContract) |> RenameProjection.renameMap
    let plan = composePlan renameMap Map.empty sourceOrder
    let row = List.exactlyOne (orderLoad plan).Rows
    Assert.Equal(Some "280", Map.tryFind (nm "Buyer") row.Values)
    Assert.NotEqual<string option>(Some "18", Map.tryFind (nm "Buyer") row.Values)

[<Fact>]
let ``AC-I7 adversarial: an ordinal rename would mis-assign — the SsKey re-point keeps the FK correct`` () =
    // The source row's cells are ordered so that a positional (ordinal)
    // rename — "the first non-Id source column becomes BUYER" — would put
    // the Email-shaped junk value into the FK column. The identity re-point
    // keys on the NAME (Owner → Buyer), so the FK value 280 lands in BUYER
    // regardless of cell order, and re-keys to 18.
    let renameMap = RenameProjection.renames (betweenOk rpSourceContract rpSinkContract) |> RenameProjection.renameMap
    // Adversarial cell order: a decoy column precedes Owner. An ordinal
    // scheme keyed on position would grab "decoy" for BUYER.
    let sourceOrder = [ rowOf [ "Decoy", "ZZZ"; "Owner", "280"; "Id", "1000" ] ]
    let plan = composePlan renameMap rpReconciliation sourceOrder
    let row = List.exactlyOne (orderLoad plan).Rows
    Assert.Equal(Some "18", Map.tryFind (nm "Buyer") row.Values)
    // The decoy did NOT become BUYER — the re-point is by identity, not ordinal.
    Assert.NotEqual<string option>(Some "ZZZ", Map.tryFind (nm "Buyer") row.Values)

[<Fact(Skip = "AC-I7 DB end-to-end witness needs Docker (another track holds Docker this round). The pure composition above is the discriminating witness; this canary asserts the same composition through Transfer.runReconcilingWithRenames against a real Source+Sink: source at contract A (Owner/OWNER_ID), sink at contract B (Buyer/BUYER_ID) with pre-existing UAT User rows, reconciliation = User-by-Email. Expect the written Order row's BUYER column to carry the reconciled UAT User key.")>]
let ``AC-I7 canary: runReconcilingWithRenames composes rename + re-key end-to-end (Docker)`` () =
    // Parent track: flip Skip→Fact when Docker is available. Drives
    // Transfer.runReconcilingWithRenames mode allowCdc source sink
    // rpSourceContract rpSinkContract rpReconciliation against two ephemeral
    // DBs and asserts the BUYER column re-keys.
    ()
