namespace Projection.Tests

// LE-3 — the full-shape B→A reverse leg, proven at the untested intersection
// (THE_DATA_PRODUCERS §0/§6; DECISIONS 2026-06-10 — J3 residual CLOSED):
// rendered cross-rendition contracts (`CatalogRendition`) × sink-minted
// identity (every PK IDENTITY ⇒ `AssignedBySink`) × a multi-kind FK graph
// (chain depth 4 + a diamond) × the DML-only principal. The LE-1/LE-2
// canaries are single-kind `PreservedFromSource`; this file closes the
// composition. Tier 2 runs the SAME leg as a principal granted exactly
// SELECT/INSERT/UPDATE/DELETE — the cloud sink's `grant: data` envelope —
// so success proves the write path's whole statement vocabulary fits the
// grant (TRANSFER_ISOMORPHISM_SUBSTANTIATION §2 probes P1/P2/P3/P6 on mock
// infrastructure). Serial via Docker-SqlServer; blocking wait via TaskSync.

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Targets.SSDT

module internal ReverseLegFixtures =

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else printfn "SKIP %s: Docker daemon not reachable." label; false

    let value (r: Result<'a>) : 'a = Result.value r

    let nm (s: string) : Name = Name.create s |> Result.value
    let kKey (s: string) : SsKey = SsKey.synthesizedComposite "L3_KIND" [ s ] |> Result.value
    let aKey (kind: string) (attr: string) : SsKey = SsKey.synthesizedComposite "L3_ATTR" [ kind; attr ] |> Result.value
    let rKey (kind: string) (refName: string) : SsKey = SsKey.synthesizedComposite "L3_REF" [ kind; refName ] |> Result.value

    let private idPk (kind: string) : Attribute =
        { Attribute.create (aKey kind "Id") (nm "Id") Integer with
            Column       = ColumnRealization.create "ID" false |> Result.value
            IsPrimaryKey = true
            IsIdentity   = true
            IsMandatory  = true }

    let private intCol (kind: string) (logical: string) (physical: string) (nullable: bool) : Attribute =
        { Attribute.create (aKey kind logical) (nm logical) Integer with
            Column      = ColumnRealization.create physical nullable |> Result.value
            IsMandatory = not nullable }

    let private textCol (kind: string) (logical: string) (physical: string) : Attribute =
        { Attribute.create (aKey kind logical) (nm logical) Text with
            Column      = ColumnRealization.create physical false |> Result.value
            IsMandatory = true }

    /// The ONE authored model (as-authored = physical rendition A): four
    /// kinds, every PK an IDENTITY (so every kind is `AssignedBySink`), an
    /// FK chain Customer ← Account ← Invoice ← Payment (depth 4) plus a
    /// diamond (Invoice and Payment both reference Account; Invoice also
    /// references Customer directly — nullable, the NULL-FK case).
    /// `CatalogRendition.logical` derives the B contract (tables/columns by
    /// logical `Name`); `.physical` is this catalog.
    let authoredModel : Catalog =
        let customer =
            Kind.create (kKey "Customer") (nm "Customer")
                (TableId.create "dbo" "OSUSR_L3_CUSTOMER" |> Result.value)
                [ idPk "Customer"; textCol "Customer" "Email" "EMAIL" ]
        let account =
            { Kind.create (kKey "Account") (nm "Account")
                (TableId.create "dbo" "OSUSR_L3_ACCOUNT" |> Result.value)
                [ idPk "Account"
                  intCol "Account" "CustomerId" "CUSTOMER_ID" false
                  textCol "Account" "AccName" "ACCNAME" ] with
                References =
                    [ Reference.create (rKey "Account" "Customer") (nm "AccountCustomer")
                        (aKey "Account" "CustomerId") (kKey "Customer") ] }
        let invoice =
            { Kind.create (kKey "Invoice") (nm "Invoice")
                (TableId.create "dbo" "OSUSR_L3_INVOICE" |> Result.value)
                [ idPk "Invoice"
                  intCol "Invoice" "AccountId" "ACCOUNT_ID" false
                  intCol "Invoice" "CustomerId" "CUSTOMER_ID" true
                  textCol "Invoice" "Ref" "REF" ] with
                References =
                    [ Reference.create (rKey "Invoice" "Account") (nm "InvoiceAccount")
                        (aKey "Invoice" "AccountId") (kKey "Account")
                      Reference.create (rKey "Invoice" "Customer") (nm "InvoiceCustomer")
                        (aKey "Invoice" "CustomerId") (kKey "Customer") ] }
        let payment =
            { Kind.create (kKey "Payment") (nm "Payment")
                (TableId.create "dbo" "OSUSR_L3_PAYMENT" |> Result.value)
                [ idPk "Payment"
                  intCol "Payment" "InvoiceId" "INVOICE_ID" false
                  intCol "Payment" "AccountId" "ACCOUNT_ID" false
                  textCol "Payment" "PayRef" "PAYREF" ] with
                References =
                    [ Reference.create (rKey "Payment" "Invoice") (nm "PaymentInvoice")
                        (aKey "Payment" "InvoiceId") (kKey "Invoice")
                      Reference.create (rKey "Payment" "Account") (nm "PaymentAccount")
                        (aKey "Payment" "AccountId") (kKey "Account") ] }
        Catalog.create
            [ { SsKey = kKey "Module"; Name = nm "L3"; Kinds = [ customer; account; invoice; payment ]
                IsActive = true; ExtendedProperties = [] } ] []
        |> Result.value

    /// B's seed (logical names) with DELIBERATELY COLLIDING source key
    /// spaces: every table's IDs start at 1000, so a preserved key would be
    /// visibly wrong in the sink (the sink mints from 1). Invoice inv-2
    /// carries the NULL nullable FK.
    let seedClean =
        "SET IDENTITY_INSERT [dbo].[Customer] ON; " +
        "INSERT INTO [dbo].[Customer] ([Id],[Email]) VALUES (1000,N'alice@x'),(1001,N'bob@x'); " +
        "SET IDENTITY_INSERT [dbo].[Customer] OFF; " +
        "SET IDENTITY_INSERT [dbo].[Account] ON; " +
        "INSERT INTO [dbo].[Account] ([Id],[CustomerId],[AccName]) VALUES " +
        "(1000,1000,N'acc-a1'),(1001,1000,N'acc-a2'),(1002,1001,N'acc-b1'); " +
        "SET IDENTITY_INSERT [dbo].[Account] OFF; " +
        "SET IDENTITY_INSERT [dbo].[Invoice] ON; " +
        "INSERT INTO [dbo].[Invoice] ([Id],[AccountId],[CustomerId],[Ref]) VALUES " +
        "(1000,1000,1000,N'inv-1'),(1001,1001,NULL,N'inv-2'),(1002,1002,1001,N'inv-3'); " +
        "SET IDENTITY_INSERT [dbo].[Invoice] OFF; " +
        "SET IDENTITY_INSERT [dbo].[Payment] ON; " +
        "INSERT INTO [dbo].[Payment] ([Id],[InvoiceId],[AccountId],[PayRef]) VALUES " +
        "(1000,1000,1000,N'pay-1'),(1001,1001,1001,N'pay-2'),(1002,1002,1002,N'pay-3'),(1003,1000,1000,N'pay-4'); " +
        "SET IDENTITY_INSERT [dbo].[Payment] OFF;"

    /// The FK-orphan row: Payment pay-orphan references Invoice 9999 (no
    /// such row). B's FK constraint is NOCHECK'd to admit it — migration-
    /// team data quality, not engine data. The transfer must drop it BY
    /// NAME (SkippedReferences + exit 9), never silently.
    let seedOrphan =
        "ALTER TABLE [dbo].[Payment] NOCHECK CONSTRAINT ALL; " +
        "SET IDENTITY_INSERT [dbo].[Payment] ON; " +
        "INSERT INTO [dbo].[Payment] ([Id],[InvoiceId],[AccountId],[PayRef]) VALUES (1004,9999,1000,N'pay-orphan'); " +
        "SET IDENTITY_INSERT [dbo].[Payment] OFF;"

    /// (left, right) string pairs from a two-column query.
    let pairs (cnn: Microsoft.Data.SqlClient.SqlConnection) (sql: string)
        : System.Threading.Tasks.Task<(string * string option) list> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sql
            use! reader = cmd.ExecuteReaderAsync()
            let acc = System.Collections.Generic.List<string * string option>()
            let mutable go = true
            while go do
                let! has = reader.ReadAsync()
                if has then
                    let right = if reader.IsDBNull 1 then None else Some (reader.GetString 1)
                    acc.Add(reader.GetString 0, right)
                else go <- false
            return List.ofSeq acc
        }

    let countRows (cnn: Microsoft.Data.SqlClient.SqlConnection) (table: string) : System.Threading.Tasks.Task<int> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sprintf "SELECT COUNT_BIG(*) FROM %s;" table
            let! scalar = cmd.ExecuteScalarAsync()
            return int (unbox<int64> scalar)
        }

    /// Every FK-edge join in the PHYSICAL sink (A), by business key,
    /// ordered. The five edges of the graph; the surrogates never appear.
    let sinkEdgeJoins (sink: Microsoft.Data.SqlClient.SqlConnection) =
        task {
            let! accountCustomer =
                pairs sink ("SELECT a.[ACCNAME], c.[EMAIL] FROM [dbo].[OSUSR_L3_ACCOUNT] a " +
                            "JOIN [dbo].[OSUSR_L3_CUSTOMER] c ON a.[CUSTOMER_ID] = c.[ID] ORDER BY a.[ACCNAME];")
            let! invoiceAccount =
                pairs sink ("SELECT i.[REF], a.[ACCNAME] FROM [dbo].[OSUSR_L3_INVOICE] i " +
                            "JOIN [dbo].[OSUSR_L3_ACCOUNT] a ON i.[ACCOUNT_ID] = a.[ID] ORDER BY i.[REF];")
            let! invoiceCustomer =
                pairs sink ("SELECT i.[REF], c.[EMAIL] FROM [dbo].[OSUSR_L3_INVOICE] i " +
                            "LEFT JOIN [dbo].[OSUSR_L3_CUSTOMER] c ON i.[CUSTOMER_ID] = c.[ID] ORDER BY i.[REF];")
            let! paymentInvoice =
                pairs sink ("SELECT p.[PAYREF], i.[REF] FROM [dbo].[OSUSR_L3_PAYMENT] p " +
                            "JOIN [dbo].[OSUSR_L3_INVOICE] i ON p.[INVOICE_ID] = i.[ID] ORDER BY p.[PAYREF];")
            let! paymentAccount =
                pairs sink ("SELECT p.[PAYREF], a.[ACCNAME] FROM [dbo].[OSUSR_L3_PAYMENT] p " +
                            "JOIN [dbo].[OSUSR_L3_ACCOUNT] a ON p.[ACCOUNT_ID] = a.[ID] ORDER BY p.[PAYREF];")
            return accountCustomer, invoiceAccount, invoiceCustomer, paymentInvoice, paymentAccount
        }

    /// The same five joins in the LOGICAL source (B), by business key.
    let sourceEdgeJoins (src: Microsoft.Data.SqlClient.SqlConnection) =
        task {
            let! accountCustomer =
                pairs src ("SELECT a.[AccName], c.[Email] FROM [dbo].[Account] a " +
                           "JOIN [dbo].[Customer] c ON a.[CustomerId] = c.[Id] ORDER BY a.[AccName];")
            let! invoiceAccount =
                pairs src ("SELECT i.[Ref], a.[AccName] FROM [dbo].[Invoice] i " +
                           "JOIN [dbo].[Account] a ON i.[AccountId] = a.[Id] ORDER BY i.[Ref];")
            let! invoiceCustomer =
                pairs src ("SELECT i.[Ref], c.[Email] FROM [dbo].[Invoice] i " +
                           "LEFT JOIN [dbo].[Customer] c ON i.[CustomerId] = c.[Id] ORDER BY i.[Ref];")
            let! paymentInvoice =
                pairs src ("SELECT p.[PayRef], i.[Ref] FROM [dbo].[Payment] p " +
                           "JOIN [dbo].[Invoice] i ON p.[InvoiceId] = i.[Id] ORDER BY p.[PayRef];")
            let! paymentAccount =
                pairs src ("SELECT p.[PayRef], a.[AccName] FROM [dbo].[Payment] p " +
                           "JOIN [dbo].[Account] a ON p.[AccountId] = a.[Id] ORDER BY p.[PayRef];")
            return accountCustomer, invoiceAccount, invoiceCustomer, paymentInvoice, paymentAccount
        }

    /// Source surrogates all live at >= 1000; a sink that carries ANY such
    /// ID preserved a source key instead of letting the sink mint.
    let preservedKeyCount (sink: Microsoft.Data.SqlClient.SqlConnection) =
        task {
            let! c1 = countRows sink "[dbo].[OSUSR_L3_CUSTOMER] WHERE [ID] >= 1000"
            let! c2 = countRows sink "[dbo].[OSUSR_L3_ACCOUNT] WHERE [ID] >= 1000"
            let! c3 = countRows sink "[dbo].[OSUSR_L3_INVOICE] WHERE [ID] >= 1000"
            let! c4 = countRows sink "[dbo].[OSUSR_L3_PAYMENT] WHERE [ID] >= 1000"
            return c1 + c2 + c3 + c4
        }

    /// The 6.A.2-lift reverse-leg fixture: one self-referencing IDENTITY
    /// kind (the operator's User.ManagerId shape), rendered at both
    /// renditions. The nullable self-FK defers to Phase 2, which keys on
    /// the ASSIGNED PK through the captured remap (operator-authorized
    /// 2026-06-10).
    let selfFkModel : Catalog =
        let employee =
            { Kind.create (kKey "Employee") (nm "Employee")
                (TableId.create "dbo" "OSUSR_L3_EMPLOYEE" |> Result.value)
                [ { Attribute.create (aKey "Employee" "Id") (nm "Id") Integer with
                      Column       = ColumnRealization.create "ID" false |> Result.value
                      IsPrimaryKey = true
                      IsIdentity   = true
                      IsMandatory  = true }
                  { Attribute.create (aKey "Employee" "FullName") (nm "FullName") Text with
                      Column      = ColumnRealization.create "FULLNAME" false |> Result.value
                      IsMandatory = true }
                  { Attribute.create (aKey "Employee" "ManagerId") (nm "ManagerId") Integer with
                      Column = ColumnRealization.create "MANAGER_ID" true |> Result.value } ] with
                References =
                    [ Reference.create (rKey "Employee" "Manager") (nm "EmployeeManager")
                        (aKey "Employee" "ManagerId") (kKey "Employee") ] }
        Catalog.create
            [ { SsKey = kKey "SelfFkModule"; Name = nm "L3SelfFk"; Kinds = [ employee ]
                IsActive = true; ExtendedProperties = [] } ] []
        |> Result.value

    /// VP's manager (CEO, 1002) lands AFTER VP in PK order — a forward
    /// reference only Phase 2 can satisfy; Ghost's manager (9999) does not
    /// exist — the named phase-2 erasure (the row stands, the reference is
    /// lost, the skip is named).
    let selfFkSeed =
        "ALTER TABLE [dbo].[Employee] NOCHECK CONSTRAINT ALL; " +
        "SET IDENTITY_INSERT [dbo].[Employee] ON; " +
        "INSERT INTO [dbo].[Employee] ([Id],[FullName],[ManagerId]) VALUES " +
        "(1000,N'VP',1002),(1001,N'Mgr',1000),(1002,N'CEO',NULL),(1003,N'Ghost',9999); " +
        "SET IDENTITY_INSERT [dbo].[Employee] OFF;"

[<Xunit.Collection("Docker-SqlServer")>]
type ReverseLegCanaryTests(fixture: EphemeralContainerFixture) =
    interface IClassFixture<EphemeralContainerFixture>

    /// Deploy B from the rendered logical contract + seed it, deploy A from
    /// the physical contract (empty), and hand both to the body along with
    /// the two contracts. The shared spine of every test in this file.
    member private _.WithReverseLegEstates
        (label: string)
        (seed: string)
        (body: Microsoft.Data.SqlClient.SqlConnection -> string ->
               Microsoft.Data.SqlClient.SqlConnection -> string ->
               Catalog -> Catalog -> System.Threading.Tasks.Task<unit>) =
        let model = ReverseLegFixtures.authoredModel
        let logicalContract = CatalogRendition.logical model
        let physicalContract = CatalogRendition.physical model
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase (label + "Src") (fun src srcConnStr ->
                task {
                    do! Deploy.executeBatch src (SsdtDdlEmitter.statements logicalContract |> Render.toText)
                    do! Deploy.executeBatch src seed
                    return!
                        fixture.WithEphemeralDatabase (label + "Sink") (fun sink sinkConnStr ->
                            task {
                                do! Deploy.executeBatch sink (SsdtDdlEmitter.statements physicalContract |> Render.toText)
                                do! body src srcConnStr sink sinkConnStr logicalContract physicalContract
                            })
                }))

    // ------------------------------------------------------------------
    // Tier 1 — the keystone: rendered contracts × AssignedBySink × the
    // multi-kind graph, relational fidelity by business-key join.
    // ------------------------------------------------------------------

    [<Fact>]
    member this.``LE-3 keystone: full-shape reverse leg — every FK edge's business-key join survives B->A while every surrogate is sink-minted`` () =
        if not (ReverseLegFixtures.skipIfNoDocker "L3Keystone") then () else
        this.WithReverseLegEstates "L3Keystone" (ReverseLegFixtures.seedClean + " " + ReverseLegFixtures.seedOrphan)
            (fun src _ sink _ logicalContract physicalContract ->
                task {
                    let! reportR =
                        Transfer.runWithRenames Transfer.Execute true src sink logicalContract physicalContract
                    let report = ReverseLegFixtures.value reportR

                    // Every kind in the graph is AssignedBySink — the sink mints.
                    Assert.Equal(4, report.Kinds.Length)
                    for k in report.Kinds do
                        Assert.Equal(IdentityDisposition.AssignedBySink, k.Disposition)

                    // The orphan (Payment -> Invoice 9999) is dropped BY NAME:
                    // the skip carries the owning kind, the FK column, the
                    // target kind, and the unresolved source surrogate.
                    Assert.Contains(report.SkippedReferences, fun (owner, r: UnresolvedReference) ->
                        owner = ReverseLegFixtures.kKey "Payment"
                        && r.Target = ReverseLegFixtures.kKey "Invoice"
                        && r.UnresolvedSource = SourceKey.ofString "9999")
                    Assert.Equal(1, report.SkippedReferences.Length)

                    // 6.A.1 — the drop maps to the fail-loud exit (9), and only
                    // an explicit --allow-drops downgrades it to 0.
                    Assert.True(Transfer.hasDrops report)
                    Assert.Equal(Transfer.DroppedReferencesExit, Transfer.exitCodeForReport false report)
                    Assert.Equal(0, Transfer.exitCodeForReport true report)

                    // The sink minted EVERY surrogate: no source key (>= 1000)
                    // survives anywhere — colliding source key spaces would
                    // make a preserved key visible here.
                    let! preserved = ReverseLegFixtures.preservedKeyCount sink
                    Assert.Equal(0, preserved)

                    // Row counts: everything lands except the named orphan.
                    let! customers = ReverseLegFixtures.countRows sink "[dbo].[OSUSR_L3_CUSTOMER]"
                    let! accounts  = ReverseLegFixtures.countRows sink "[dbo].[OSUSR_L3_ACCOUNT]"
                    let! invoices  = ReverseLegFixtures.countRows sink "[dbo].[OSUSR_L3_INVOICE]"
                    let! payments  = ReverseLegFixtures.countRows sink "[dbo].[OSUSR_L3_PAYMENT]"
                    Assert.Equal(2, customers)
                    Assert.Equal(3, accounts)
                    Assert.Equal(3, invoices)
                    Assert.Equal(4, payments)

                    // Relational fidelity: for EVERY FK edge, the business-key
                    // join in A equals the same join in B (minus the one named
                    // dropped row), including the NULL nullable FK riding
                    // through as NULL.
                    let! (bAccCust, bInvAcc, bInvCust, bPayInv, bPayAcc) = ReverseLegFixtures.sourceEdgeJoins src
                    let! (aAccCust, aInvAcc, aInvCust, aPayInv, aPayAcc) = ReverseLegFixtures.sinkEdgeJoins sink
                    Assert.Equal<(string * string option) list>(bAccCust, aAccCust)
                    Assert.Equal<(string * string option) list>(bInvAcc, aInvAcc)
                    Assert.Equal<(string * string option) list>(bInvCust, aInvCust)
                    let minusOrphan (edge: (string * string option) list) =
                        edge |> List.filter (fun (l, _) -> l <> "pay-orphan")
                    Assert.Equal<(string * string option) list>(minusOrphan bPayInv, aPayInv)
                    Assert.Equal<(string * string option) list>(minusOrphan bPayAcc, aPayAcc)

                    // Pin the literals too, so a B-side seeding mistake cannot
                    // vacuously satisfy the A=B comparison.
                    Assert.Equal<(string * string option) list>(
                        [ ("acc-a1", Some "alice@x"); ("acc-a2", Some "alice@x"); ("acc-b1", Some "bob@x") ], aAccCust)
                    Assert.Equal<(string * string option) list>(
                        [ ("inv-1", Some "alice@x"); ("inv-2", None); ("inv-3", Some "bob@x") ], aInvCust)
                })

    // ------------------------------------------------------------------
    // Slice C1 — the FullRights populate fork: PreferPreservedKeys writes
    // the SOURCE key directly (Bulk.copyRows + KeepIdentity) for IDENTITY
    // PKs too, so the whole capture/remap/FK-repoint machinery is skipped.
    // The exact INVERSE of the keystone above: every key preserved (not
    // minted), zero AssignedBySink, joins still hold. Witnesses the work
    // plan's Slice C exit test on the same multi-kind graph.
    // ------------------------------------------------------------------

    [<Fact>]
    member this.``Slice C1: a FullRights populate (PreferPreservedKeys) preserves every source key — zero remap, joins hold — the inverse of the AssignedBySink keystone`` () =
        if not (ReverseLegFixtures.skipIfNoDocker "L3SliceC1") then () else
        this.WithReverseLegEstates "L3SliceC1" ReverseLegFixtures.seedClean
            (fun src _ sink _ logicalContract physicalContract ->
                task {
                    let! reportR =
                        Transfer.runWithRenamesWith IdentityPolicy.PreferPreservedKeys Transfer.Execute true src sink logicalContract physicalContract
                    let report = ReverseLegFixtures.value reportR

                    // The fork: EVERY kind is PreservedFromSource — the source key
                    // is written directly, NOT minted. No capture, no remap, no
                    // FK re-point (the dramatically simpler load); nothing skipped.
                    Assert.Equal(4, report.Kinds.Length)
                    for k in report.Kinds do
                        Assert.Equal(IdentityDisposition.PreservedFromSource, k.Disposition)
                    Assert.Empty(report.SkippedReferences)

                    // Every source key (>= 1000) is PRESERVED in the sink — the
                    // INVERSE of the keystone (AssignedBySink ⇒ preservedKeyCount = 0).
                    // 2 + 3 + 3 + 4 = 12 rows, each carrying its source identity.
                    let! preserved = ReverseLegFixtures.preservedKeyCount sink
                    Assert.Equal(12, preserved)

                    let! customers = ReverseLegFixtures.countRows sink "[dbo].[OSUSR_L3_CUSTOMER]"
                    let! accounts  = ReverseLegFixtures.countRows sink "[dbo].[OSUSR_L3_ACCOUNT]"
                    let! invoices  = ReverseLegFixtures.countRows sink "[dbo].[OSUSR_L3_INVOICE]"
                    let! payments  = ReverseLegFixtures.countRows sink "[dbo].[OSUSR_L3_PAYMENT]"
                    Assert.Equal(2, customers)
                    Assert.Equal(3, accounts)
                    Assert.Equal(3, invoices)
                    Assert.Equal(4, payments)

                    // Joins hold: every FK edge's business-key join in A equals B's
                    // — the FK values stayed valid because the keys were preserved
                    // (no re-point needed). Nothing dropped, so no `minusOrphan`.
                    let! (bAccCust, bInvAcc, bInvCust, bPayInv, bPayAcc) = ReverseLegFixtures.sourceEdgeJoins src
                    let! (aAccCust, aInvAcc, aInvCust, aPayInv, aPayAcc) = ReverseLegFixtures.sinkEdgeJoins sink
                    Assert.Equal<(string * string option) list>(bAccCust, aAccCust)
                    Assert.Equal<(string * string option) list>(bInvAcc, aInvAcc)
                    Assert.Equal<(string * string option) list>(bInvCust, aInvCust)
                    Assert.Equal<(string * string option) list>(bPayInv, aPayInv)
                    Assert.Equal<(string * string option) list>(bPayAcc, aPayAcc)
                })

    [<Fact>]
    member this.``LE-3 apparatus: the same full-shape leg through runReverseLegThroughConnections, and a WipeAndLoad re-run leaves no duplicates`` () =
        if not (ReverseLegFixtures.skipIfNoDocker "L3Apparatus") then () else
        this.WithReverseLegEstates "L3Apparatus" ReverseLegFixtures.seedClean
            (fun _ srcConnStr sink sinkConnStr logicalContract physicalContract ->
                task {
                    let srcFile = System.IO.Path.GetTempFileName()
                    let sinkFile = System.IO.Path.GetTempFileName()
                    System.IO.File.WriteAllText(srcFile, srcConnStr)
                    System.IO.File.WriteAllText(sinkFile, sinkConnStr)
                    try
                        let srcSub : Substrate =
                            { Environment = Projection.Core.Environment.Dev
                              Role = SubstrateRole.Source
                              ConnectionRef = ConnectionRef.File srcFile }
                        let sinkSub : Substrate =
                            { Environment = Projection.Core.Environment.Uat
                              Role = SubstrateRole.Sink
                              ConnectionRef = ConnectionRef.File sinkFile }
                        let connections =
                            TransferConnections.create srcSub sinkSub false
                            |> ReverseLegFixtures.value

                        let runLeg () =
                            Transfer.runReverseLegThroughConnections
                                Transfer.Execute EmissionMode.WipeAndLoad false true false []
                                connections logicalContract physicalContract Map.empty

                        let! firstR = runLeg ()
                        let first = ReverseLegFixtures.value firstR
                        Assert.Empty(first.SkippedReferences)
                        Assert.True(first.Kinds |> List.forall (fun k -> k.Disposition = IdentityDisposition.AssignedBySink))

                        let! paymentsAfterFirst = ReverseLegFixtures.countRows sink "[dbo].[OSUSR_L3_PAYMENT]"
                        Assert.Equal(4, paymentsAfterFirst)

                        // D10 — the WipeAndLoad re-run is the DML-legal refresh:
                        // child-first DELETE then reload; counts hold, no dupes.
                        let! secondR = runLeg ()
                        let _ = ReverseLegFixtures.value secondR
                        let! customers = ReverseLegFixtures.countRows sink "[dbo].[OSUSR_L3_CUSTOMER]"
                        let! payments  = ReverseLegFixtures.countRows sink "[dbo].[OSUSR_L3_PAYMENT]"
                        Assert.Equal(2, customers)
                        Assert.Equal(4, payments)

                        // The wiped-and-reloaded sink still minted fresh keys.
                        let! preserved = ReverseLegFixtures.preservedKeyCount sink
                        Assert.Equal(0, preserved)

                        let! (aAccCust, _, _, _, _) = ReverseLegFixtures.sinkEdgeJoins sink
                        Assert.Equal<(string * string option) list>(
                            [ ("acc-a1", Some "alice@x"); ("acc-a2", Some "alice@x"); ("acc-b1", Some "bob@x") ], aAccCust)
                    finally
                        try System.IO.File.Delete srcFile with _ -> ()
                        try System.IO.File.Delete sinkFile with _ -> ()
                })

    [<Fact>]
    member _.``6.A.2 lifted on the reverse leg: a self-FK IDENTITY kind loads B->A — the forward manager reference resolves in phase 2 and the orphan manager is a NAMED erasure`` () =
        if not (ReverseLegFixtures.skipIfNoDocker "L3SelfFk") then () else
        let model = ReverseLegFixtures.selfFkModel
        let logicalContract = CatalogRendition.logical model
        let physicalContract = CatalogRendition.physical model
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "L3SelfFkSrc" (fun src _ ->
                task {
                    do! Deploy.executeBatch src (SsdtDdlEmitter.statements logicalContract |> Render.toText)
                    do! Deploy.executeBatch src ReverseLegFixtures.selfFkSeed
                    return!
                        fixture.WithEphemeralDatabase "L3SelfFkSink" (fun sink _ ->
                            task {
                                do! Deploy.executeBatch sink (SsdtDdlEmitter.statements physicalContract |> Render.toText)

                                let! reportR =
                                    Transfer.runWithRenames Transfer.Execute true src sink logicalContract physicalContract
                                let report = ReverseLegFixtures.value reportR

                                // Ghost's manager (9999) is the NAMED phase-2
                                // erasure: the row stands, the reference is lost,
                                // and the run maps to exit 9 without --allow-drops.
                                Assert.Contains(report.SkippedReferences, fun (owner, r: UnresolvedReference) ->
                                    owner = ReverseLegFixtures.kKey "Employee"
                                    && r.Target = ReverseLegFixtures.kKey "Employee"
                                    && r.UnresolvedSource = SourceKey.ofString "9999")
                                Assert.Equal(Transfer.DroppedReferencesExit, Transfer.exitCodeForReport false report)

                                // All four rows landed with sink-minted keys.
                                let! rows = ReverseLegFixtures.countRows sink "[dbo].[OSUSR_L3_EMPLOYEE]"
                                Assert.Equal(4, rows)
                                let! preserved = ReverseLegFixtures.countRows sink "[dbo].[OSUSR_L3_EMPLOYEE] WHERE [ID] >= 1000"
                                Assert.Equal(0, preserved)

                                // The manager chain by name: the forward VP->CEO
                                // reference resolved in phase 2; Ghost's manager
                                // is NULL (the named erasure, never a wrong key).
                                let! chain =
                                    ReverseLegFixtures.pairs sink
                                        ("SELECT e.[FULLNAME], m.[FULLNAME] FROM [dbo].[OSUSR_L3_EMPLOYEE] e " +
                                         "LEFT JOIN [dbo].[OSUSR_L3_EMPLOYEE] m ON e.[MANAGER_ID] = m.[ID] ORDER BY e.[FULLNAME];")
                                Assert.Equal<(string * string option) list>(
                                    [ ("CEO", None); ("Ghost", None); ("Mgr", Some "VP"); ("VP", Some "CEO") ], chain)
                            })
                }))

    // ------------------------------------------------------------------
    // The capture-lane ladder — capability descent, named, never silent.
    // ------------------------------------------------------------------

    [<Fact>]
    member this.``capture ladder: a trigger on the sink table refuses OUTPUT-without-INTO (error 334) — the lane descends ONE rung, the load succeeds, the descent is NAMED on the report`` () =
        if not (ReverseLegFixtures.skipIfNoDocker "L3Ladder") then () else
        this.WithReverseLegEstates "L3Ladder" ReverseLegFixtures.seedClean
            (fun src _ sink _ logicalContract physicalContract ->
                task {
                    // The real-OSUSR risk this ladder exists for: an enabled
                    // trigger on the sink table makes OUTPUT-without-INTO
                    // illegal (SQL error 334) — the fastest rung is refused
                    // BY CAPABILITY, not by data.
                    do! Deploy.executeBatch sink
                            ("CREATE TRIGGER [trg_L3Ladder_Customer] ON [dbo].[OSUSR_L3_CUSTOMER] AFTER INSERT AS BEGIN SET NOCOUNT ON; END;")

                    let! reportR =
                        Transfer.runWithRenames Transfer.Execute true src sink logicalContract physicalContract
                    let report = ReverseLegFixtures.value reportR
                    Assert.Empty(report.SkippedReferences)

                    // The descent is a NAMED outcome: Customer degraded one
                    // rung (StagedMergeOutput -> StagedMergeOutputInto, 334);
                    // the untriggered kinds ran the preferred rung.
                    Assert.Contains(report.CaptureLaneDescents, fun (d: LaneDescent) ->
                        d.Kind = ReverseLegFixtures.kKey "Customer"
                        && d.From = CaptureLane.StagedMergeOutput
                        && d.To = CaptureLane.StagedMergeOutputInto
                        && d.SqlErrorNumber = 334)
                    Assert.True(
                        report.CaptureLaneDescents |> List.forall (fun d -> d.Kind = ReverseLegFixtures.kKey "Customer"),
                        sprintf "only the triggered kind should descend; got %A" report.CaptureLaneDescents)

                    // The degraded lane carries the SAME semantics: every
                    // surrogate minted, every business-key join intact.
                    let! preserved = ReverseLegFixtures.preservedKeyCount sink
                    Assert.Equal(0, preserved)
                    let! (aAccCust, _, aInvCust, _, _) = ReverseLegFixtures.sinkEdgeJoins sink
                    Assert.Equal<(string * string option) list>(
                        [ ("acc-a1", Some "alice@x"); ("acc-a2", Some "alice@x"); ("acc-b1", Some "bob@x") ], aAccCust)
                    Assert.Equal<(string * string option) list>(
                        [ ("inv-1", Some "alice@x"); ("inv-2", None); ("inv-3", Some "bob@x") ], aInvCust)
                })

    [<Fact>]
    member this.``capture floor: the rowwise SCOPE_IDENTITY rung captures per row on a TRIGGERED table — the ladder's last rung is behaviorally identical`` () =
        if not (ReverseLegFixtures.skipIfNoDocker "L3Floor") then () else
        this.WithReverseLegEstates "L3Floor" ReverseLegFixtures.seedClean
            (fun _ _ sink _ _ physicalContract ->
                task {
                    do! Deploy.executeBatch sink
                            ("CREATE TRIGGER [trg_L3Floor_Customer] ON [dbo].[OSUSR_L3_CUSTOMER] AFTER INSERT AS BEGIN SET NOCOUNT ON; END;")
                    let customer =
                        Catalog.allKinds physicalContract
                        |> List.find (fun k -> TableId.tableText k.Physical = "OSUSR_L3_CUSTOMER")
                    let idAttr = customer.Attributes |> List.find (fun a -> a.IsPrimaryKey && a.IsIdentity)
                    let rows =
                        [ { Identifier = ReverseLegFixtures.aKey "Floor" "r1"
                            Values = Map.ofList [ ReverseLegFixtures.nm "Id", "1000"; ReverseLegFixtures.nm "Email", "floor-a@x" ] }
                          { Identifier = ReverseLegFixtures.aKey "Floor" "r2"
                            Values = Map.ofList [ ReverseLegFixtures.nm "Id", "1001"; ReverseLegFixtures.nm "Email", "floor-b@x" ] } ]
                    let! pairs =
                        SurrogateCapture.captureChunk sink customer
                            (fun (a: Attribute) -> StaticRow.valueOrEmpty a.Name)
                            idAttr Set.empty
                            CaptureLane.RowwiseScopeIdentity rows
                    Assert.Equal<(string * string) list>([ ("1000", "1"); ("1001", "2") ], pairs)
                    let! n = ReverseLegFixtures.countRows sink "[dbo].[OSUSR_L3_CUSTOMER]"
                    Assert.Equal(2, n)
                })

    [<Fact>]
    member this.``re-run honesty: a second Incremental Execute into a populated sink DUPLICATES AssignedBySink rows (the named open question, pinned)`` () =
        // The data path is INSERT-based and the sink mints fresh surrogates,
        // so an Incremental re-run cannot collide on the PK — it duplicates.
        // Today's honest modes are WipeAndLoad (D10) or the G10 resumable
        // marker; this pin keeps the gap named until idempotent re-run lands.
        if not (ReverseLegFixtures.skipIfNoDocker "L3Rerun") then () else
        this.WithReverseLegEstates "L3Rerun" ReverseLegFixtures.seedClean
            (fun src _ sink _ logicalContract physicalContract ->
                task {
                    let runLeg () =
                        Transfer.runWithRenames Transfer.Execute true src sink logicalContract physicalContract
                    let! firstR = runLeg ()
                    let _ = ReverseLegFixtures.value firstR
                    let! secondR = runLeg ()
                    let _ = ReverseLegFixtures.value secondR
                    let! customers = ReverseLegFixtures.countRows sink "[dbo].[OSUSR_L3_CUSTOMER]"
                    let! payments  = ReverseLegFixtures.countRows sink "[dbo].[OSUSR_L3_PAYMENT]"
                    Assert.Equal(4, customers)
                    Assert.Equal(8, payments)
                })

    // ------------------------------------------------------------------
    // Tier 2 — the DML-only principal: the whole leg under the cloud
    // sink's `grant: data` envelope (P1/P2/P3 on mock infrastructure).
    // ------------------------------------------------------------------

    [<Fact>]
    member this.``DML-only principal: the full reverse leg succeeds as a login granted exactly SELECT, INSERT, UPDATE, DELETE — no ALTER anywhere in the write path`` () =
        if not (ReverseLegFixtures.skipIfNoDocker "L3Dml") then () else
        this.WithReverseLegEstates "L3Dml" ReverseLegFixtures.seedClean
            (fun src _ adminSink sinkConnStr logicalContract physicalContract ->
                task {
                    let! (login, restrictedConnStr) =
                        DmlPrincipal.createManaged adminSink sinkConnStr
                    try
                        use sink = new Microsoft.Data.SqlClient.SqlConnection(restrictedConnStr)
                        do! sink.OpenAsync()

                        let! reportR =
                            Transfer.runWithRenames Transfer.Execute true src sink logicalContract physicalContract
                        let report = ReverseLegFixtures.value reportR
                        Assert.Empty(report.SkippedReferences)

                        // P2 (insert omitting the identity column, read the
                        // assigned key back) is what the engine does natively:
                        // the load landed and the sink minted every key.
                        let! preserved = ReverseLegFixtures.preservedKeyCount adminSink
                        Assert.Equal(0, preserved)
                        let! (aAccCust, _, _, aPayInv, _) = ReverseLegFixtures.sinkEdgeJoins adminSink
                        Assert.Equal<(string * string option) list>(
                            [ ("acc-a1", Some "alice@x"); ("acc-a2", Some "alice@x"); ("acc-b1", Some "bob@x") ], aAccCust)
                        Assert.Equal(4, aPayInv.Length)
                    finally
                        DmlPrincipal.dropLogin adminSink login
                })

    [<Fact>]
    member this.``DML-only principal: SET IDENTITY_INSERT is DENIED to the data grant (probe P3, expected-denied) — the engine never needs it`` () =
        if not (ReverseLegFixtures.skipIfNoDocker "L3P3") then () else
        this.WithReverseLegEstates "L3P3" ReverseLegFixtures.seedClean
            (fun _ _ adminSink sinkConnStr _ _ ->
                task {
                    let! (login, restrictedConnStr) =
                        DmlPrincipal.createManaged adminSink sinkConnStr
                    try
                        use restricted = new Microsoft.Data.SqlClient.SqlConnection(restrictedConnStr)
                        do! restricted.OpenAsync()
                        // SET IDENTITY_INSERT requires ALTER on the table; the
                        // data grant must be refused by the server.
                        let! ex =
                            Assert.ThrowsAnyAsync<Microsoft.Data.SqlClient.SqlException>(fun () ->
                                Deploy.executeBatch restricted
                                    "SET IDENTITY_INSERT [dbo].[OSUSR_L3_CUSTOMER] ON;" :> System.Threading.Tasks.Task)
                        Assert.Contains("permission", ex.Message.ToLowerInvariant())
                    finally
                        DmlPrincipal.dropLogin adminSink login
                })

    [<Fact>]
    member this.``DML-only principal: a sink lacking INSERT refuses by name (transfer.insufficientGrant) before any row moves`` () =
        if not (ReverseLegFixtures.skipIfNoDocker "L3NoInsert") then () else
        this.WithReverseLegEstates "L3NoInsert" ReverseLegFixtures.seedClean
            (fun src _ adminSink sinkConnStr logicalContract physicalContract ->
                task {
                    let! (login, restrictedConnStr) =
                        DmlPrincipal.create adminSink sinkConnStr "SELECT"
                    try
                        use sink = new Microsoft.Data.SqlClient.SqlConnection(restrictedConnStr)
                        do! sink.OpenAsync()
                        let! reportR =
                            Transfer.runWithRenames Transfer.Execute true src sink logicalContract physicalContract
                        match reportR with
                        | Error es ->
                            Assert.True(
                                es |> List.exists (fun e -> e.Code = "transfer.insufficientGrant"),
                                sprintf "expected transfer.insufficientGrant, got %A" (es |> List.map (fun e -> e.Code)))
                        | Ok _ -> Assert.Fail("expected the permission preflight to refuse a SELECT-only sink")
                        // The refusal preceded any write.
                        let! customers = ReverseLegFixtures.countRows adminSink "[dbo].[OSUSR_L3_CUSTOMER]"
                        Assert.Equal(0, customers)
                    finally
                        DmlPrincipal.dropLogin adminSink login
                })

    [<Fact>]
    member this.``PROMOTED (was the pinned G1 gap): an object-scope DENY INSERT refuses by name pre-write — zero partial write`` () =
        // 2026-07-07: `spanningPreflight` captures grant evidence PER
        // PLANNED TABLE (`fn_my_permissions('<obj>','OBJECT')` — EFFECTIVE
        // permissions, table-level DENYs subtracted), so the DENY that used
        // to crash mid-load with upstream kinds already landed (the
        // partial-write hazard the LE-3 report named) now refuses
        // `transfer.insufficientGrant` before any write.
        if not (ReverseLegFixtures.skipIfNoDocker "L3Deny") then () else
        this.WithReverseLegEstates "L3Deny" ReverseLegFixtures.seedClean
            (fun src _ adminSink sinkConnStr logicalContract physicalContract ->
                task {
                    let! (login, restrictedConnStr) =
                        DmlPrincipal.createManaged adminSink sinkConnStr
                    try
                        do! Deploy.executeBatch adminSink
                                (sprintf "DENY INSERT ON [dbo].[OSUSR_L3_INVOICE] TO [%s];" login)
                        use sink = new Microsoft.Data.SqlClient.SqlConnection(restrictedConnStr)
                        do! sink.OpenAsync()
                        let! reportR =
                            Transfer.runWithRenames Transfer.Execute true src sink logicalContract physicalContract
                        match reportR with
                        | Error es ->
                            Assert.True(
                                es |> List.exists (fun e -> e.Code = "transfer.insufficientGrant"),
                                sprintf "expected transfer.insufficientGrant, got %A" (es |> List.map (fun e -> e.Code)))
                        | Ok _ -> Assert.Fail "expected the object-scope DENY to refuse pre-write"
                        // ZERO partial write — no upstream kind landed.
                        let! customers = ReverseLegFixtures.countRows adminSink "[dbo].[OSUSR_L3_CUSTOMER]"
                        let! invoices  = ReverseLegFixtures.countRows adminSink "[dbo].[OSUSR_L3_INVOICE]"
                        Assert.Equal(0, customers)
                        Assert.Equal(0, invoices)
                    finally
                        DmlPrincipal.dropLogin adminSink login
                })

    // ------------------------------------------------------------------
    // Tier 5 — the boundary map: B's live shape drifting from the
    // rendered contract.
    // ------------------------------------------------------------------

    [<Fact>]
    member this.``B-drift (pinned): a column the contract names but live B lacks crashes ingest with a raw SqlException — an UNNAMED refusal (gap)`` () =
        // The rendered logical contract drives the ingest SELECT by column
        // name; a live B missing one of those columns dies inside
        // ReadSide.readRowsStream with 'Invalid column name', not a named
        // transfer.* refusal. Pinned as today's behavior; the named
        // contract-vs-live-shape preflight is the follow-on (see report).
        if not (ReverseLegFixtures.skipIfNoDocker "L3DriftMissing") then () else
        this.WithReverseLegEstates "L3DriftMissing" ReverseLegFixtures.seedClean
            (fun src _ sink _ logicalContract physicalContract ->
                task {
                    do! Deploy.executeBatch src "ALTER TABLE [dbo].[Invoice] DROP COLUMN [Ref];"
                    let! _ =
                        Assert.ThrowsAnyAsync<Microsoft.Data.SqlClient.SqlException>(fun () ->
                            Transfer.runWithRenames Transfer.Execute true src sink logicalContract physicalContract
                            :> System.Threading.Tasks.Task)
                    return ()
                })

    [<Fact>]
    member this.``B-drift (pinned): a live-B column OUTSIDE the model is silently not moved — the model is the boundary of what crosses`` () =
        // Extra data the migration team landed outside the model never
        // crosses: the ingest SELECT names only the contract's columns. The
        // leg succeeds and the report is clean — the erasure is named only
        // by the model's own boundary, not per-run. Pinned; flagged in the
        // LE-3 report as a candidate for a live-shape advisory.
        if not (ReverseLegFixtures.skipIfNoDocker "L3DriftExtra") then () else
        this.WithReverseLegEstates "L3DriftExtra" ReverseLegFixtures.seedClean
            (fun src _ sink _ logicalContract physicalContract ->
                task {
                    do! Deploy.executeBatch src "ALTER TABLE [dbo].[Customer] ADD [Phone] NVARCHAR(50) NULL;"
                    do! Deploy.executeBatch src "UPDATE [dbo].[Customer] SET [Phone] = N'555-0100';"
                    let! reportR =
                        Transfer.runWithRenames Transfer.Execute true src sink logicalContract physicalContract
                    let report = ReverseLegFixtures.value reportR
                    Assert.Empty(report.SkippedReferences)
                    let! customers = ReverseLegFixtures.countRows sink "[dbo].[OSUSR_L3_CUSTOMER]"
                    Assert.Equal(2, customers)
                })
