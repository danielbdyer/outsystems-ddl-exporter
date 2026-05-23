module Projection.Tests.OssysComprehensiveFixtureTests

open System.Threading.Tasks
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Adapters.Osm
open Projection.Adapters.OssysSql

// ---------------------------------------------------------------------
// Comprehensive edge-case fixture coverage. The expanded
// `ossys-edge-case.seed.sql` deliberately exercises contract surfaces the
// original five-entity fixture left untouched. These Docker-gated tests
// assert each edge case extracts into the V2 Catalog with the right
// shape — composite PKs, self-/cross-module/multi FKs, referential
// actions, untrusted FKs, computed columns, CHECK constraints, the full
// SqlStorageType vocabulary, the Extension-module origin, and static
// entities.
// ---------------------------------------------------------------------

let private skipIfNoDocker (label: string) : bool =
    if Deploy.Docker.ensureRunning () then true
    else
        printfn "SKIP %s: Docker daemon not reachable." label
        false

let private extractFromSeed () : Task<Result<Catalog>> =
    task {
        let seed = MetadataExtractionSql.readEdgeCaseSeed()
        let! result =
            Deploy.withBootstrappedDatabase "OssysComprehensive" seed (fun cnn ->
                task {
                    let! snapshotResult = MetadataSnapshotRunner.runAsync cnn MetadataSnapshotRunner.defaultParameters
                    match snapshotResult with
                    | Error errors -> return Result.failure errors
                    | Ok snapshot ->
                        let bundle = MetadataSnapshotRunner.toBundle snapshot
                        let! catalog = CatalogReader.parse (CatalogReader.SnapshotRowsets bundle)
                        return catalog
                })
        return result
    }

let private allKinds (c: Catalog) : Kind list =
    c.Modules |> List.collect (fun m -> m.Kinds)

let private kindNamed (name: string) (c: Catalog) : Kind =
    allKinds c |> List.find (fun k -> Name.value k.Name = name)

let private attrNamed (name: string) (k: Kind) : Attribute =
    k.Attributes |> List.find (fun a -> Name.value a.Name = name)

let private refNamed (name: string) (k: Kind) : Reference =
    k.References |> List.find (fun r -> Name.value r.Name = name)

let private withCatalog (label: string) (assertion: Catalog -> unit) : unit =
    if skipIfNoDocker label then
        match (extractFromSeed ()).GetAwaiter().GetResult() with
        | Error errors -> Assert.Fail (sprintf "comprehensive fixture extraction failed: %A" errors)
        | Ok catalog   -> assertion catalog

// --- Composite primary key ------------------------------------------

[<Fact>]
let ``OrderLine carries a composite primary key (two PK attributes)`` () =
    withCatalog "comp-composite-pk" (fun catalog ->
        let orderLine = kindNamed "OrderLine" catalog
        let pkAttrs = orderLine.Attributes |> List.filter (fun a -> a.IsPrimaryKey)
        let pkNames = pkAttrs |> List.map (fun a -> Name.value a.Name) |> List.sort
        Assert.Equal<string list>(["LineNo"; "OrderId"], pkNames))

// --- Self-referencing FK --------------------------------------------

[<Fact>]
let ``Category has a self-referencing foreign key`` () =
    withCatalog "comp-self-ref" (fun catalog ->
        let category = kindNamed "Category" catalog
        let parentRef = refNamed "ParentCategoryId" category
        Assert.Equal(category.SsKey, parentRef.TargetKind))

// --- Multiple FKs on one entity (cross-module + same-module) ---------

[<Fact>]
let ``SalesOrder carries two foreign keys to distinct targets`` () =
    withCatalog "comp-multi-fk" (fun catalog ->
        let order    = kindNamed "SalesOrder" catalog
        let customer = kindNamed "Customer" catalog
        let category = kindNamed "Category" catalog
        Assert.Equal(customer.SsKey, (refNamed "CustomerId" order).TargetKind)
        Assert.Equal(category.SsKey, (refNamed "CategoryId" order).TargetKind))

// --- Referential actions --------------------------------------------

[<Fact>]
let ``Referential actions: Cascade, SetNull, and NoAction all resolve`` () =
    withCatalog "comp-ref-actions" (fun catalog ->
        let order    = kindNamed "SalesOrder" catalog
        let movement = kindNamed "StockMovement" catalog
        // 'Delete' -> Cascade
        Assert.Equal(Cascade, (refNamed "CustomerId" order).OnDelete)
        // 'Protect' -> NoAction
        Assert.Equal(NoAction, (refNamed "CategoryId" order).OnDelete)
        // 'SetNull' -> SetNull
        Assert.Equal(SetNull, (refNamed "SupplierId" movement).OnDelete))

[<Fact>]
let ``Untrusted (WITH NOCHECK) FK lands with IsConstraintTrusted=false`` () =
    withCatalog "comp-untrusted-fk" (fun catalog ->
        let movement = kindNamed "StockMovement" catalog
        Assert.False((refNamed "SupplierId" movement).IsConstraintTrusted))

[<Fact>]
let ``ON UPDATE CASCADE flows into Reference.OnUpdate`` () =
    withCatalog "comp-on-update" (fun catalog ->
        let movement = kindNamed "StockMovement" catalog
        Assert.Equal<ReferenceAction option>(Some Cascade, (refNamed "StockItemId" movement).OnUpdate))

// --- Computed column + CHECK constraint -----------------------------

[<Fact>]
let ``Product.DisplayLabel is extracted as a computed column`` () =
    withCatalog "comp-computed" (fun catalog ->
        let product = kindNamed "Product" catalog
        let label = attrNamed "DisplayLabel" product
        Assert.True(label.Computed.IsSome, "expected DisplayLabel to be a computed column"))

[<Fact>]
let ``Product carries a CHECK constraint on Price`` () =
    withCatalog "comp-check" (fun catalog ->
        let product = kindNamed "Product" catalog
        Assert.NotEmpty(product.ColumnChecks))

// --- SqlStorageType vocabulary coverage -----------------------------

[<Fact>]
let ``StockItem covers Integer / LongInteger / GUID / DateTimeOffset / Time / Float storage`` () =
    withCatalog "comp-types-stockitem" (fun catalog ->
        let s = kindNamed "StockItem" catalog
        Assert.Equal(Some SqlStorageType.Int, (attrNamed "ReorderLevel" s).SqlStorage)
        Assert.Equal(Some SqlStorageType.BigInt, (attrNamed "WarehouseQty" s).SqlStorage)
        Assert.Equal(Some SqlStorageType.UniqueIdentifier, (attrNamed "PublicGuid" s).SqlStorage)
        Assert.Equal(Some SqlStorageType.Float, (attrNamed "AvgCost" s).SqlStorage)
        match (attrNamed "LastSyncedAt" s).SqlStorage with
        | Some (SqlStorageType.DateTimeOffset _) -> ()
        | other -> Assert.Fail (sprintf "expected DateTimeOffset storage, got %A" other)
        match (attrNamed "OpenTime" s).SqlStorage with
        | Some (SqlStorageType.Time _) -> ()
        | other -> Assert.Fail (sprintf "expected Time storage, got %A" other))

[<Fact>]
let ``Product covers Decimal / Currency / Date / VARBINARY(MAX) / NVARCHAR(MAX) storage`` () =
    withCatalog "comp-types-product" (fun catalog ->
        let p = kindNamed "Product" catalog
        Assert.Equal(Some (SqlStorageType.Decimal (18, 2)), (attrNamed "Price" p).SqlStorage)
        Assert.Equal(Some (SqlStorageType.Decimal (37, 8)), (attrNamed "Cost" p).SqlStorage)
        Assert.Equal(Some SqlStorageType.Date, (attrNamed "LaunchDate" p).SqlStorage)
        Assert.Equal(Some (SqlStorageType.VarBinary Max), (attrNamed "Photo" p).SqlStorage)
        Assert.Equal(Some (SqlStorageType.NVarChar Max), (attrNamed "Notes" p).SqlStorage))

// --- Extension module origin ----------------------------------------

[<Fact>]
let ``External entity in an Extension module resolves to ExternalViaIntegrationStudio`` () =
    withCatalog "comp-extension-origin" (fun catalog ->
        let syncLog = kindNamed "SyncLog" catalog
        Assert.Equal(ExternalViaIntegrationStudio, syncLog.Origin))

// --- Static entities -------------------------------------------------

[<Fact>]
let ``Static lookup entities are marked with the Static modality`` () =
    withCatalog "comp-static" (fun catalog ->
        let country = kindNamed "Country" catalog
        let isStatic =
            country.Modality
            |> List.exists (function Static _ -> true | _ -> false)
        Assert.True(isStatic, "expected Country to carry Modality.Static"))
