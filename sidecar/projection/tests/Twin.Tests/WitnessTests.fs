module Twin.Tests.WitnessTests

open Xunit
open Projection.Core
open Projection.Tests.Fixtures
open Projection.Tests.IRBuilders
open Twin.Core
open Twin.Runtime

// ---------------------------------------------------------------------------
// The witness pass (PROVING_SURFACE_DESIGN §5.2): deterministic UPDATEs of
// minted rows planting the recorded realities; a witness that would violate
// a rule the trunk enforces is a named skip, never an error.
// ---------------------------------------------------------------------------

let private name (s: string) : Name = Name.create s |> Result.value

let private attrOf (key: SsKey) (logical: string) (column: string) (ptype: PrimitiveType) (isPk: bool) (length: int option) (identity: bool) : Attribute =
    { Attribute.create key (name logical) ptype with
        Column       = ColumnRealization.create column false |> Result.value
        IsPrimaryKey = isPk
        Length       = length
        IsIdentity   = identity }

let private customer : Kind =
    { Kind.create (kindKey ["C"]) (name "Customer") (mkTableId "dbo" "Customer")
        [ attrOf (attrKey ["C"; "Id"]) "Id" "Id" Integer true None true
          attrOf (attrKey ["C"; "Email"]) "Email" "Email" Text false (Some 40) false
          attrOf (attrKey ["C"; "Score"]) "Score" "Score" Integer false None false
          attrOf (attrKey ["C"; "Code"]) "Code" "Code" Text false None false ] with
        Modality = [] }

let private order : Kind =
    { Kind.create (kindKey ["O"]) (name "Order") (mkTableId "dbo" "Order")
        [ attrOf (attrKey ["O"; "Id"]) "Id" "Id" Integer true None true
          attrOf (attrKey ["O"; "CustomerId"]) "CustomerId" "CustomerId" Integer false None false ] with
        References =
            [ Reference.create (refKey ["O"; "Customer"]) (name "Customer") (attrKey ["O"; "CustomerId"]) (kindKey ["C"]) ] }

let private index =
    CatalogIndex.ofCatalog
        (Catalog.create [ mkModule (modKey "W") (name "W") [ customer; order ] ] [] |> Result.value)

let private col (n: string) (rows: int64) : ColumnEvidence =
    { Column = n; RowCount = rows; NullCount = 0L; MaxLength = None
      DistinctCount = None; Truncated = false; HasDuplicates = false
      Frequencies = []; Numeric = None }

let private shape (lo: decimal) (hi: decimal) : NumericShape =
    { Min = lo; P25 = lo; P50 = (lo + hi) / 2m; P75 = hi; P95 = hi; P99 = hi; Max = hi }

let private packOf (tables: TableEvidence list) (orphans: OrphanEvidence list) : EvidencePack =
    { Evidence.emptyPack RichTier with Sources = [ "uat" ]; Tables = tables; Orphans = orphans }

let private fullPack : EvidencePack =
    packOf
        [ { Table = "dbo.Customer"; RowCount = 10L
            Columns =
                [ { col "Email" 10L with MaxLength = Some 32 }
                  { col "Score" 10L with Numeric = Some (shape -3m 250m) }
                  { col "Code" 10L with HasDuplicates = true } ] } ]
        // Customer.Score -> Order carries no reference in the trunk: the
        // FK-add case, so the orphan witness is legal.
        [ { ChildTable = "dbo.Customer"; ChildColumn = "Score"; ParentTable = "dbo.Order"; OrphanCount = 2L } ]

[<Fact>]
let ``the emission is byte-deterministic per seed`` () =
    let plan, _ = Witness.plan index fullPack
    Assert.Equal(Witness.emitSql 7UL plan, Witness.emitSql 7UL plan)
    Assert.NotEqual<string>(Witness.emitSql 7UL plan, Witness.emitSql 8UL plan)
    Assert.Equal(Witness.emitAssertSql plan, Witness.emitAssertSql plan)

[<Fact>]
let ``witnesses are UPDATEs of minted rows, never INSERTs`` () =
    let plan, skips = Witness.plan index fullPack
    Assert.Equal(4, List.length plan.Cases)
    Assert.Empty skips
    let sql = Witness.emitSql 7UL plan
    Assert.Contains("UPDATE w SET", sql)
    Assert.DoesNotContain("INSERT", sql)
    // The duplicate witness copies a synthetic value from the first row.
    Assert.Contains("w2", sql)
    // The orphan witness points past the largest parent key.
    Assert.Contains("ISNULL(MAX(", sql)

[<Fact>]
let ``an orphan on an edge the trunk enforces is a named skip`` () =
    let pack =
        packOf []
            [ { ChildTable = "dbo.Order"; ChildColumn = "CustomerId"; ParentTable = "dbo.Customer"; OrphanCount = 3L } ]
    let plan, skips = Witness.plan index pack
    Assert.Empty plan.Cases
    let s = List.exactlyOne skips
    Assert.Equal("enforcedReference", s.Reason)
    Assert.Contains("dbo.Order.CustomerId", s.Coordinate)

[<Fact>]
let ``a length past the declared width is a named skip`` () =
    let pack =
        packOf
            [ { Table = "dbo.Customer"; RowCount = 10L
                Columns = [ { col "Email" 10L with MaxLength = Some 50 } ] } ] []
    let plan, skips = Witness.plan index pack
    Assert.Empty plan.Cases
    Assert.Equal("exceedsDeclaredLength", (List.exactlyOne skips).Reason)

[<Fact>]
let ``primary-key, identity, and too-few-row columns are named skips`` () =
    let pack =
        packOf
            [ { Table = "dbo.Customer"; RowCount = 1L
                Columns =
                    [ { col "Id" 1L with Numeric = Some (shape 1m 9m) }
                      { col "Score" 1L with Numeric = Some (shape 1m 9m) }
                      { col "Code" 1L with HasDuplicates = true } ] } ] []
    let plan, skips = Witness.plan index pack
    Assert.Empty plan.Cases
    let reasons = skips |> List.map (fun s -> s.Reason)
    Assert.Contains("primaryKeyColumn", reasons)
    Assert.Contains("tooFewRows", reasons)

[<Fact>]
let ``the assert script carries one check per witness and one failures summary`` () =
    let plan, _ = Witness.plan index fullPack
    let sql = Witness.emitAssertSql plan
    // Each check appears twice (the detail set and the summary re-derive
    // the same CTE).
    let occurrences (needle: string) (hay: string) =
        let mutable count = 0
        let mutable at = hay.IndexOf(needle, System.StringComparison.Ordinal)
        while at >= 0 do
            count <- count + 1
            at <- hay.IndexOf(needle, at + 1, System.StringComparison.Ordinal)
        count
    Assert.Equal(2 * List.length plan.Cases, occurrences " AS ok" sql)
    Assert.Contains("AS failures", sql)
    Assert.Contains("maxLength dbo.Customer.Email = 32", sql)

[<Fact>]
let ``an empty plan asserts zero failures`` () =
    let plan, _ = Witness.plan index (packOf [] [])
    Assert.Contains("SELECT 0 AS failures", Witness.emitAssertSql plan)