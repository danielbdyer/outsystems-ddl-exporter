module Projection.Tests.DacpacCatalogReaderDifferentialTests

open System.IO
open Xunit
open Microsoft.SqlServer.Dac
open Microsoft.SqlServer.Dac.Model
open Projection.Core
open Projection.Adapters.Dacpac

// ---------------------------------------------------------------------------
// Differential test for the DACPAC catalog adapter (chapter 3, session 27 —
// first substantive slice of the canary chapter).
//
// The contract: V2's `Projection.Adapters.Dacpac.CatalogReader.parse`
// consumes DACPAC bytes (zip-of-XML produced by SSDT or by
// DacpacEmitter when it lands at slice 5+) and produces a V2 `Catalog`.
//
// **T1 amended (2026-05-23) — the algebraic equality.** For binary
// Π's, T1 holds at the projection language's normal form: the
// loaded representation under DacFx's parser. This test enforces
// that contract by:
//   1. building a TSqlModel via DacFx's `AddObjects(script)`;
//   2. emitting DACPAC bytes via `BuildPackage(stream, model, metadata)`;
//   3. parsing those bytes via the read-side adapter;
//   4. asserting the parsed Catalog equals the hand-built expected one.
// Byte-equality is NOT asserted (timestamps embedded in Origin.xml).
//
// **Slice-1 scope.** One Module ("Pipeline" placeholder per axis 7),
// one Kind, two Attributes (PK + nullable Name). No FKs, no indexes,
// no modality. Subsequent slices extend.
// ---------------------------------------------------------------------------

let private mkKey s = SsKey.original s |> Result.value
let private mkName s = Name.create s |> Result.value

let private pipelineModuleKey = mkKey "OS_MOD_Pipeline"
let private userKindKey       = mkKey "OS_KIND_Pipeline_User"
let private userIdAttrKey     = mkKey "OS_ATTR_Pipeline_User_Id"
let private userNameAttrKey   = mkKey "OS_ATTR_Pipeline_User_Name"

/// Build a DACPAC byte[] from one or more T-SQL scripts. DacFx's
/// `AddObjects` consumes script text; `BuildPackage` writes to a stream.
let private buildDacpacBytes (scripts: string list) : byte[] =
    use model = new TSqlModel(SqlServerVersion.Sql160, TSqlModelOptions())
    for script in scripts do
        model.AddObjects(script)
    let metadata = PackageMetadata()
    metadata.Name <- "PipelineFixture"
    metadata.Description <- "Slice-1 hermetic fixture for the DACPAC read-side adapter."
    metadata.Version <- "1.0.0"
    use stream = new MemoryStream()
    DacPackageExtensions.BuildPackage(stream, model, metadata)
    stream.ToArray()

// Slice-1 fixture: single User table with PK Id and nullable Name.
let private slice1Scripts : string list =
    [
        """
CREATE TABLE [dbo].[User]
(
    [Id]   INT          NOT NULL,
    [Name] NVARCHAR(200) NULL,
    CONSTRAINT [PK_User] PRIMARY KEY ([Id])
);
"""
    ]

let private expectedSlice1Catalog : Catalog =
    let userKind : Kind =
        { SsKey    = userKindKey
          Name     = mkName "User"
          Origin   = OsNative
          Modality = []
          Physical = { Schema = "dbo"; Table = "User" }
          Attributes = [
              { SsKey        = userIdAttrKey
                Name         = mkName "Id"
                Type         = Integer
                Column       = { ColumnName = "Id"; IsNullable = false }
                IsPrimaryKey = true
                IsMandatory  = true }
              { SsKey        = userNameAttrKey
                Name         = mkName "Name"
                Type         = Text
                Column       = { ColumnName = "Name"; IsNullable = true }
                IsPrimaryKey = false
                IsMandatory  = false }
          ]
          References = []
          Indexes    = [] }
    { Modules = [
        { SsKey = pipelineModuleKey
          Name  = mkName "Pipeline"
          Kinds = [ userKind ] } ] }

[<Fact>]
let ``differential: slice-1 DACPAC bytes parse into the expected V2 Catalog (T1 amended — loaded form)`` () =
    let bytes = buildDacpacBytes slice1Scripts
    let result = CatalogReader.parse (CatalogReader.DacpacBytes bytes)
    match result with
    | Failure errors ->
        Assert.Fail(
            sprintf
                "Expected Result.Success; got Result.Failure with %d error(s): %A"
                errors.Length
                errors)
    | Success actual ->
        Assert.Equal<Catalog>(expectedSlice1Catalog, actual)

[<Fact>]
let ``T1 amended (loaded form): same triple round-trips identically through DacFx parse`` () =
    // Build twice and parse both; the loaded representation is invariant
    // even though the bytes may differ (timestamps in Origin.xml).
    let firstBytes  = buildDacpacBytes slice1Scripts
    let secondBytes = buildDacpacBytes slice1Scripts
    let firstParse  = CatalogReader.parse (CatalogReader.DacpacBytes firstBytes)
    let secondParse = CatalogReader.parse (CatalogReader.DacpacBytes secondBytes)
    match firstParse, secondParse with
    | Success a, Success b -> Assert.Equal<Catalog>(a, b)
    | _ ->
        Assert.Fail(
            sprintf
                "Both parses should succeed; got %A and %A"
                firstParse
                secondParse)

// ---------------------------------------------------------------------------
// Slice 2 — FK references (chapter 3, session 28).
//
// Two tables (Account, [Order]) within the slice-1 placeholder module;
// Order has an AccountId column with a foreign-key to Account.Id.
// Single-column FK (V2's per-attribute Reference shape); same-module
// target (resolved to the placeholder module under slice 1's
// single-module assumption — multi-Module fixture lands in slice 4+).
//
// Exercises parseReference + parseReferenceAction. NoAction is the
// default ON DELETE behavior in SQL Server when not specified;
// DacFx surfaces this as ForeignKeyAction.NoAction → V2 NoAction.
// ---------------------------------------------------------------------------

let private slice2Scripts : string list =
    [
        """
CREATE TABLE [dbo].[Account]
(
    [Id]   INT          NOT NULL,
    [Name] NVARCHAR(200) NULL,
    CONSTRAINT [PK_Account] PRIMARY KEY ([Id])
);
"""
        """
CREATE TABLE [dbo].[Order]
(
    [Id]        INT NOT NULL,
    [AccountId] INT NOT NULL,
    CONSTRAINT [PK_Order]           PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Order_Account]   FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Account] ([Id])
);
"""
    ]

let private accountKindKey      = mkKey "OS_KIND_Pipeline_Account"
let private accountIdAttrKey    = mkKey "OS_ATTR_Pipeline_Account_Id"
let private accountNameAttrKey  = mkKey "OS_ATTR_Pipeline_Account_Name"
let private orderKindKey        = mkKey "OS_KIND_Pipeline_Order"
let private orderIdAttrKey      = mkKey "OS_ATTR_Pipeline_Order_Id"
let private orderAccountAttrKey = mkKey "OS_ATTR_Pipeline_Order_AccountId"
let private orderAccountRefKey  = mkKey "OS_REF_Pipeline_Order_AccountId"

let private expectedSlice2Catalog : Catalog =
    let accountKind : Kind =
        { SsKey    = accountKindKey
          Name     = mkName "Account"
          Origin   = OsNative
          Modality = []
          Physical = { Schema = "dbo"; Table = "Account" }
          Attributes = [
              { SsKey        = accountIdAttrKey
                Name         = mkName "Id"
                Type         = Integer
                Column       = { ColumnName = "Id"; IsNullable = false }
                IsPrimaryKey = true
                IsMandatory  = true }
              { SsKey        = accountNameAttrKey
                Name         = mkName "Name"
                Type         = Text
                Column       = { ColumnName = "Name"; IsNullable = true }
                IsPrimaryKey = false
                IsMandatory  = false }
          ]
          References = []
          Indexes    = [] }
    let orderKind : Kind =
        { SsKey    = orderKindKey
          Name     = mkName "Order"
          Origin   = OsNative
          Modality = []
          Physical = { Schema = "dbo"; Table = "Order" }
          Attributes = [
              { SsKey        = orderIdAttrKey
                Name         = mkName "Id"
                Type         = Integer
                Column       = { ColumnName = "Id"; IsNullable = false }
                IsPrimaryKey = true
                IsMandatory  = true }
              { SsKey        = orderAccountAttrKey
                Name         = mkName "AccountId"
                Type         = Integer
                Column       = { ColumnName = "AccountId"; IsNullable = false }
                IsPrimaryKey = false
                IsMandatory  = true }
          ]
          References = [
              { SsKey           = orderAccountRefKey
                Name            = mkName "AccountId"
                SourceAttribute = orderAccountAttrKey
                TargetKind      = accountKindKey
                OnDelete        = NoAction }
          ]
          Indexes    = [] }
    { Modules = [
        { SsKey = pipelineModuleKey
          Name  = mkName "Pipeline"
          // Tables enumerate in DacFx-internal order; both orderings
          // are admissible at the IR level (Module.Kinds is a list,
          // not a set). Compare structurally.
          Kinds = [ accountKind; orderKind ] } ] }

[<Fact>]
let ``differential: slice-2 cross-table FK fixture parses with the expected V2 Reference`` () =
    let bytes = buildDacpacBytes slice2Scripts
    let result = CatalogReader.parse (CatalogReader.DacpacBytes bytes)
    match result with
    | Failure errors ->
        Assert.Fail(
            sprintf
                "Expected Result.Success; got %d error(s): %A"
                errors.Length errors)
    | Success actual ->
        // Compare by SsKey-sorted module/kind structure to absorb
        // DacFx's enumeration order. Account before Order is the
        // expected SsKey-sorted order.
        let normalize (c: Catalog) : Catalog =
            { c with
                Modules =
                    c.Modules
                    |> List.map (fun m ->
                        { m with
                            Kinds = m.Kinds |> List.sortBy (fun k -> SsKey.rootOriginal k.SsKey) })
                    |> List.sortBy (fun m -> SsKey.rootOriginal m.SsKey) }
        Assert.Equal<Catalog>(normalize expectedSlice2Catalog, normalize actual)

// ---------------------------------------------------------------------------
// Slice 3 — Indexes (chapter 3, session 28).
//
// Customer table with three non-PK indexes: single-column unique on
// Email, composite non-unique on (LastName, FirstName), and the PK
// itself (which DacFx represents as PrimaryKeyConstraint, NOT
// Index — the read-side adapter filters PK out of the index list,
// confirming the structural distinction.
// ---------------------------------------------------------------------------

let private slice3Scripts : string list =
    [
        """
CREATE TABLE [dbo].[Customer]
(
    [Id]        INT NOT NULL,
    [Email]     NVARCHAR(200) NOT NULL,
    [LastName]  NVARCHAR(100) NULL,
    [FirstName] NVARCHAR(100) NULL,
    CONSTRAINT [PK_Customer] PRIMARY KEY ([Id])
);
"""
        """
CREATE UNIQUE INDEX [IX_Customer_Email] ON [dbo].[Customer]([Email]);
"""
        """
CREATE INDEX [IX_Customer_Name] ON [dbo].[Customer]([LastName], [FirstName]);
"""
    ]

let private customerKindKey       = mkKey "OS_KIND_Pipeline_Customer"
let private customerIdKey         = mkKey "OS_ATTR_Pipeline_Customer_Id"
let private customerEmailKey      = mkKey "OS_ATTR_Pipeline_Customer_Email"
let private customerLastNameKey   = mkKey "OS_ATTR_Pipeline_Customer_LastName"
let private customerFirstNameKey  = mkKey "OS_ATTR_Pipeline_Customer_FirstName"
let private customerEmailIdxKey   = mkKey "OS_IDX_Pipeline_Customer_IX_Customer_Email"
let private customerNameIdxKey    = mkKey "OS_IDX_Pipeline_Customer_IX_Customer_Name"

[<Fact>]
let ``differential: slice-3 indexes fixture parses with two non-PK indexes (PK is not an Index in DacFx)`` () =
    let bytes = buildDacpacBytes slice3Scripts
    let result = CatalogReader.parse (CatalogReader.DacpacBytes bytes)
    match result with
    | Failure errors ->
        Assert.Fail(
            sprintf
                "Expected Result.Success; got %d error(s): %A"
                errors.Length errors)
    | Success actual ->
        let customer =
            actual.Modules
            |> List.collect (fun m -> m.Kinds)
            |> List.find (fun k -> k.SsKey = customerKindKey)
        // The two indexes; sorted by SsKey for stable comparison.
        let actualIndexes =
            customer.Indexes |> List.sortBy (fun i -> SsKey.rootOriginal i.SsKey)
        let expectedIndexes : Index list = [
            // OS_IDX_Pipeline_Customer_IX_Customer_Email sorts before
            // OS_IDX_Pipeline_Customer_IX_Customer_Name lexically.
            { SsKey        = customerEmailIdxKey
              Name         = mkName "IX_Customer_Email"
              Columns      = [ customerEmailKey ]
              IsUnique     = true
              IsPrimaryKey = false }
            { SsKey        = customerNameIdxKey
              Name         = mkName "IX_Customer_Name"
              Columns      = [ customerLastNameKey; customerFirstNameKey ]
              IsUnique     = false
              IsPrimaryKey = false }
        ]
        Assert.Equal<Index list>(expectedIndexes, actualIndexes)

// ---------------------------------------------------------------------------
// Slice 4 — Composite primary key (chapter 3, session 28).
//
// OrderItem with a 2-column PK on (OrderId, LineNumber). Verifies that
// the slice-1 `primaryKeyColumnNames` Set<string> shape handles
// composite PKs correctly: every PK column has IsPrimaryKey=true;
// non-PK columns have IsPrimaryKey=false. This slice is test-only —
// no parser change required; the existing implementation handles the
// case structurally.
// ---------------------------------------------------------------------------

let private slice4Scripts : string list =
    [
        """
CREATE TABLE [dbo].[OrderItem]
(
    [OrderId]    INT NOT NULL,
    [LineNumber] INT NOT NULL,
    [Quantity]   INT NULL,
    CONSTRAINT [PK_OrderItem] PRIMARY KEY ([OrderId], [LineNumber])
);
"""
    ]

let private orderItemKindKey       = mkKey "OS_KIND_Pipeline_OrderItem"
let private orderItemOrderIdKey    = mkKey "OS_ATTR_Pipeline_OrderItem_OrderId"
let private orderItemLineKey       = mkKey "OS_ATTR_Pipeline_OrderItem_LineNumber"
let private orderItemQuantityKey   = mkKey "OS_ATTR_Pipeline_OrderItem_Quantity"

[<Fact>]
let ``differential: slice-4 composite PK marks both PK columns as IsPrimaryKey`` () =
    let bytes = buildDacpacBytes slice4Scripts
    let result = CatalogReader.parse (CatalogReader.DacpacBytes bytes)
    match result with
    | Failure errors ->
        Assert.Fail(
            sprintf
                "Expected Result.Success; got %d error(s): %A"
                errors.Length errors)
    | Success actual ->
        let orderItem =
            actual.Modules
            |> List.collect (fun m -> m.Kinds)
            |> List.find (fun k -> k.SsKey = orderItemKindKey)
        let pkSet =
            orderItem.Attributes
            |> List.filter (fun a -> a.IsPrimaryKey)
            |> List.map (fun a -> a.SsKey)
            |> Set.ofList
        let nonPkSet =
            orderItem.Attributes
            |> List.filter (fun a -> not a.IsPrimaryKey)
            |> List.map (fun a -> a.SsKey)
            |> Set.ofList
        Assert.Equal<Set<SsKey>>(
            Set.ofList [ orderItemOrderIdKey; orderItemLineKey ],
            pkSet)
        Assert.Equal<Set<SsKey>>(
            Set.ofList [ orderItemQuantityKey ],
            nonPkSet)
