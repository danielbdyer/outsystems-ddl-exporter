module Projection.Tests.RefactorLogEmitterTests

open Xunit
open Projection.Core
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

// FSharp.Core's two-arity Result case constructors collide with
// `Projection.Core.DiagnosticSeverity.Error` once `Projection.Core`
// is opened; qualifying via a private alias mirrors the pattern at
// `ArtifactByKindTests.fs` and `CatalogDiffTests.fs`.
type private FsResult<'a, 'b> = Microsoft.FSharp.Core.Result<'a, 'b>

let private mustOk (r: FsResult<'a, 'b>) : 'a =
    match r with
    | FsResult.Ok v -> v
    | FsResult.Error err ->
        Assert.Fail(sprintf "%A" err)
        Unchecked.defaultof<'a>

let private mustResultOk (r: Result<'a>) : 'a =
    match r with
    | Ok v -> v
    | Error es ->
        Assert.Fail(sprintf "%A" es)
        Unchecked.defaultof<'a>

let private nameOf (s: string) : Name =
    Name.create s |> mustResultOk

// ---------------------------------------------------------------------------
// Build a "renamed" catalog by rewriting one kind's `Name` while
// preserving its `SsKey`. Per A1 (identity-survives-rename), renames
// preserve SsKey; this is the diff-rename scenario.
// ---------------------------------------------------------------------------

let private renamedCustomerKind : Kind =
    { customer with Name = nameOf "Patron" }

let private renamedSalesModule : Module =
    { salesModule with Kinds = [ renamedCustomerKind; order; country ] }

let private targetCatalog : Catalog =
    IRBuilders.mkCatalog [ renamedSalesModule ]

// 6.A.12 — a COLUMN rename: Customer's `Name` attribute's logical name
// changes (Name → FullName) while the kind name stays stable. Mirrors the
// kind-level rename (logical-name basis); SSDT needs a SqlSimpleColumn
// refactorlog entry so DacFx does sp_rename, not DROP+ADD (data loss).
let private columnRenamedCustomer : Kind =
    { customer with
        Attributes =
            customer.Attributes
            |> List.map (fun a ->
                if a.SsKey = customerNameKey then { a with Name = nameOf "FullName" } else a) }

let private columnRenameTarget : Catalog =
    IRBuilders.mkCatalog [ { salesModule with Kinds = [ columnRenamedCustomer; order; country ] } ]

// N1 — a FOREIGN-KEY rename: Order's reference to Customer keeps its
// `SsKey` (orderRefToCustomer) while its logical `Name` changes
// (Customer → Patron). Mirrors the kind/column logical-rename basis;
// SSDT needs a SqlForeignKey refactorlog entry so DacFx does sp_rename
// rather than DROP CONSTRAINT + ADD CONSTRAINT (gap N1: a renamed FK
// silently dropped its refactorlog entry).
let private fkRenamedOrder : Kind =
    { order with
        References =
            order.References
            |> List.map (fun r ->
                if r.SsKey = orderRefToCustomer then { r with Name = nameOf "Patron" } else r) }

let private fkRenameTarget : Catalog =
    IRBuilders.mkCatalog [ { salesModule with Kinds = [ customer; fkRenamedOrder; country ] } ]

// ---------------------------------------------------------------------------
// Slice θ acceptance — RefactorLogEmitter realizes EmitterOverDiff
// <RefactorLogEntry list>; T11 (sibling-Π commutativity, structural
// type encoding, extended to diff-typed inputs) holds by construction.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T11 (diff-typed inputs): RefactorLogEmitter.emit key-set equals target Catalog.allKinds`` () =
    let diff = CatalogDiff.between sampleCatalog targetCatalog
    let expected =
        Catalog.allKinds targetCatalog
        |> List.map (fun k -> k.SsKey)
        |> Set.ofList
    let artifact = RefactorLogEmitter.emit diff |> mustOk
    Assert.Equal<Set<SsKey>>(expected, ArtifactByKind.keys artifact)

[<Fact>]
let ``RefactorLogEmitter: identity diff produces empty entries for every kind`` () =
    let diff = CatalogDiff.between sampleCatalog sampleCatalog
    let artifact = RefactorLogEmitter.emit diff |> mustOk
    let entries = ArtifactByKind.toMap artifact
    Assert.All(
        entries |> Map.toSeq |> Seq.map snd,
        fun list -> Assert.Empty(list))

[<Fact>]
let ``RefactorLogEmitter: one-rename diff produces one SqlTable entry on the renamed kind`` () =
    let diff = CatalogDiff.between sampleCatalog targetCatalog
    let artifact = RefactorLogEmitter.emit diff |> mustOk
    let entries = ArtifactByKind.toMap artifact
    // Renamed kind: `customer` (SsKey unchanged; Name "Customer" → "Patron")
    let customerEntries = Map.find customerKey entries
    Assert.Equal(1, List.length customerEntries)
    let entry = List.head customerEntries
    Assert.Equal(RenameRefactor, entry.OperationKind)
    Assert.Equal(SqlTable, entry.ElementType)
    Assert.Equal(SqlSchema, entry.ParentElementType)
    Assert.Equal("[dbo].[OSUSR_S1S_CUSTOMER]", entry.ElementName)
    Assert.Equal("[dbo]", entry.ParentElementName)
    Assert.Equal("Patron", entry.NewName)
    Assert.Equal(RefactorLogEmitter.version, entry.PassVersion)

[<Fact>]
let ``RefactorLogEmitter: unrenamed kinds produce empty entries`` () =
    let diff = CatalogDiff.between sampleCatalog targetCatalog
    let artifact = RefactorLogEmitter.emit diff |> mustOk
    let entries = ArtifactByKind.toMap artifact
    Assert.Empty(Map.find orderKey entries)
    Assert.Empty(Map.find countryKey entries)

// ---------------------------------------------------------------------------
// T1 — determinism. Same CatalogDiff produces the same OperationKey
// across repeat invocations. Per chapter 3.5 prescope §3, this is the
// load-bearing UUIDv5-derivation property: SSDT's GUI generates random
// GUIDs but DacFx accepts any stable Guid; V2 chooses determinism.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: RefactorLogEmitter produces stable OperationKey across repeat invocations`` () =
    let diff = CatalogDiff.between sampleCatalog targetCatalog
    let runs =
        [ for _ in 1 .. 10 -> RefactorLogEmitter.emit diff |> mustOk ]
    let head =
        runs
        |> List.head
        |> ArtifactByKind.toMap
        |> Map.find customerKey
        |> List.head
    Assert.All(
        runs,
        fun artifact ->
            let entry =
                artifact
                |> ArtifactByKind.toMap
                |> Map.find customerKey
                |> List.head
            Assert.Equal(head.OperationKey, entry.OperationKey))

[<Fact>]
let ``RefactorLogEmitter: OperationKey is UUIDv5 (version digit 5)`` () =
    let diff = CatalogDiff.between sampleCatalog targetCatalog
    let artifact = RefactorLogEmitter.emit diff |> mustOk
    let entry =
        artifact
        |> ArtifactByKind.toMap
        |> Map.find customerKey
        |> List.head
    let dashedForm = entry.OperationKey.ToString("D")
    // Position 14 in the dashed form is the version digit.
    Assert.Equal('5', dashedForm.[14])

// ---------------------------------------------------------------------------
// Empty-diff edge cases (added / removed kinds produce no rename
// entries; the diff classifies them outside `Renamed`).
// ---------------------------------------------------------------------------

[<Fact>]
let ``RefactorLogEmitter: Added kind produces empty entries (it's a CREATE not a rename)`` () =
    let empty = Catalog.create [] [] |> mustResultOk
    let diff = CatalogDiff.between empty sampleCatalog
    let artifact = RefactorLogEmitter.emit diff |> mustOk
    let entries = ArtifactByKind.toMap artifact
    Assert.All(
        entries |> Map.toSeq |> Seq.map snd,
        fun list -> Assert.Empty(list))

[<Fact>]
let ``RefactorLogEmitter: Removed kind produces no artifact entry (target is empty)`` () =
    let empty = Catalog.create [] [] |> mustResultOk
    let diff = CatalogDiff.between sampleCatalog empty
    let artifact = RefactorLogEmitter.emit diff |> mustOk
    // Target catalog is empty; artifact's keyset is empty per T11.
    Assert.Empty(ArtifactByKind.keys artifact)

// ---------------------------------------------------------------------------
// 6.A.12 — column-rename refactorlog entries (SqlSimpleColumn). The crucial
// SSDT coupling: a column rename without a refactorlog entry is interpreted
// by DacFx as DROP COLUMN + ADD COLUMN — data loss. Detection is the logical
// `Attribute.Name` change (mirrors the kind-level logical rename), keyed by
// attribute SsKey; NewName is the new logical name.
// ---------------------------------------------------------------------------

[<Fact>]
let ``RefactorLogEmitter: a column rename produces a SqlSimpleColumn entry`` () =
    let diff = CatalogDiff.between sampleCatalog columnRenameTarget
    let artifact = RefactorLogEmitter.emit diff |> mustOk
    let customerEntries = Map.find customerKey (ArtifactByKind.toMap artifact)
    let columnEntry =
        customerEntries |> List.filter (fun e -> e.ElementType = SqlSimpleColumn)
    Assert.Equal(1, List.length columnEntry)
    let e = List.head columnEntry
    Assert.Equal(RenameRefactor, e.OperationKind)
    Assert.Equal(SqlTable, e.ParentElementType)
    // ElementName carries the OLD logical column name; NewName the new one.
    Assert.Equal("[dbo].[OSUSR_S1S_CUSTOMER].[Name]", e.ElementName)
    Assert.Equal("[dbo].[OSUSR_S1S_CUSTOMER]", e.ParentElementName)
    Assert.Equal("FullName", e.NewName)

[<Fact>]
let ``RefactorLogEmitter: a column rename's OperationKey is deterministic`` () =
    let key () =
        let diff = CatalogDiff.between sampleCatalog columnRenameTarget
        let artifact = RefactorLogEmitter.emit diff |> mustOk
        (Map.find customerKey (ArtifactByKind.toMap artifact)
         |> List.find (fun e -> e.ElementType = SqlSimpleColumn)).OperationKey
    Assert.Equal(key (), key ())

[<Fact>]
let ``RefactorLogEmitter: no column rename produces no SqlSimpleColumn entry`` () =
    let diff = CatalogDiff.between sampleCatalog sampleCatalog
    let artifact = RefactorLogEmitter.emit diff |> mustOk
    let allColumnEntries =
        ArtifactByKind.toMap artifact
        |> Map.toSeq
        |> Seq.collect snd
        |> Seq.filter (fun e -> e.ElementType = SqlSimpleColumn)
    Assert.Empty(allColumnEntries)

// ---------------------------------------------------------------------------
// N1 — FOREIGN-KEY-rename refactorlog entries (SqlForeignKey). A renamed FK
// without a refactorlog entry is interpreted by DacFx as DROP CONSTRAINT +
// ADD CONSTRAINT. Detection is the logical `Reference.Name` change (mirrors
// the kind/column logical rename), keyed by reference SsKey; NewName is the
// new logical FK name. The discriminating check: a renamed FK MUST produce
// exactly one entry (the un-extended emitter produced none — gap N1).
// ---------------------------------------------------------------------------

[<Fact>]
let ``RefactorLogEmitter: a foreign-key rename produces a SqlForeignKey entry`` () =
    let diff = CatalogDiff.between sampleCatalog fkRenameTarget
    let artifact = RefactorLogEmitter.emit diff |> mustOk
    let orderEntries = Map.find orderKey (ArtifactByKind.toMap artifact)
    let fkEntry =
        orderEntries |> List.filter (fun e -> e.ElementType = SqlForeignKey)
    Assert.Equal(1, List.length fkEntry)
    let e = List.head fkEntry
    Assert.Equal(RenameRefactor, e.OperationKind)
    Assert.Equal(SqlTable, e.ParentElementType)
    // ElementName carries the OLD logical FK name; NewName the new one.
    Assert.Equal("[dbo].[OSUSR_S1S_ORDER].[Customer]", e.ElementName)
    Assert.Equal("[dbo].[OSUSR_S1S_ORDER]", e.ParentElementName)
    Assert.Equal("Patron", e.NewName)
    Assert.Equal(RefactorLogEmitter.version, e.PassVersion)

[<Fact>]
let ``RefactorLogEmitter: a foreign-key rename's OperationKey is deterministic`` () =
    let key () =
        let diff = CatalogDiff.between sampleCatalog fkRenameTarget
        let artifact = RefactorLogEmitter.emit diff |> mustOk
        (Map.find orderKey (ArtifactByKind.toMap artifact)
         |> List.find (fun e -> e.ElementType = SqlForeignKey)).OperationKey
    Assert.Equal(key (), key ())

[<Fact>]
let ``RefactorLogEmitter: no foreign-key rename produces no SqlForeignKey entry`` () =
    let diff = CatalogDiff.between sampleCatalog sampleCatalog
    let artifact = RefactorLogEmitter.emit diff |> mustOk
    let allFkEntries =
        ArtifactByKind.toMap artifact
        |> Map.toSeq
        |> Seq.collect snd
        |> Seq.filter (fun e -> e.ElementType = SqlForeignKey)
    Assert.Empty(allFkEntries)

// ---------------------------------------------------------------------------
// Rename ⊥ Reshape composition. A single column that is BOTH renamed (logical
// `Name`) AND reshaped (`DataType`) lands in BOTH `AttributeDiff` axes —
// `Renamed` AND `Changed` — independently. The refactorlog rename channel and
// the SchemaMigration ALTER channel each fire once for it, disjointly (they
// emit different operations — sp_rename vs ALTER COLUMN — never the same one).
// This proves the orthogonal composition the emitters only asserted in a
// comment; rename and reshape are NOT mutually exclusive.
// ---------------------------------------------------------------------------

let private columnRenamedAndReshapedCustomer : Kind =
    { customer with
        Attributes =
            customer.Attributes
            |> List.map (fun a ->
                if a.SsKey = customerNameKey then { a with Name = nameOf "FullName"; Type = Integer } else a) }

let private renameReshapeTarget : Catalog =
    IRBuilders.mkCatalog [ { salesModule with Kinds = [ columnRenamedAndReshapedCustomer; order; country ] } ]

[<Fact>]
let ``CatalogDiff: a renamed-and-reshaped column lands in BOTH Renamed and Changed`` () =
    let diff = CatalogDiff.between sampleCatalog renameReshapeTarget
    match CatalogDiff.attributeDiffOf customerKey diff with
    | None -> Assert.Fail "expected an attribute diff on customer"
    | Some ad ->
        Assert.True(Map.containsKey customerNameKey ad.Renamed)
        let changed = ad.Reshaped |> List.filter (fun c -> c.AttributeKey = customerNameKey)
        Assert.Equal(1, List.length changed)
        Assert.Contains(AttributeFacet.DataType, (List.head changed).Facets)

[<Fact>]
let ``composition: rename → one SqlSimpleColumn AND reshape → one ALTER COLUMN, disjoint`` () =
    let diff = CatalogDiff.between sampleCatalog renameReshapeTarget
    // The refactorlog rename channel: exactly one SqlSimpleColumn entry.
    let artifact = RefactorLogEmitter.emit diff |> mustOk
    let columnRenames =
        Map.find customerKey (ArtifactByKind.toMap artifact)
        |> List.filter (fun e -> e.ElementType = SqlSimpleColumn)
    Assert.Equal(1, List.length columnRenames)
    Assert.Equal("FullName", (List.head columnRenames).NewName)
    // The SchemaMigration ALTER channel: exactly one ALTER COLUMN (the reshape),
    // never a CREATE or a DROP+ADD — the rename does not double-emit here.
    let migration = SchemaMigrationEmitter.emit diff
    let alters =
        migration.Value |> List.choose (function Statement.AlterTableAlterColumn (_, c) -> Some c | _ -> None)
    Assert.Equal(1, List.length alters)
    Assert.False(migration.Value |> List.exists (function Statement.CreateTable _ -> true | _ -> false))
