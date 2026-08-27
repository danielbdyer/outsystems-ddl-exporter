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
      Frequencies = []; Numeric = None; Text = None }

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

// ---------------------------------------------------------------------------
// The null-rate floor and the row windows: witnesses must never destroy the
// reality another witness (or the mint's own null draws) planted.
// ---------------------------------------------------------------------------

let private nullableAttr (key: SsKey) (logical: string) (column: string) (ptype: PrimitiveType) (length: int option) : Attribute =
    { attrOf key logical column ptype false length false with
        Column = ColumnRealization.create column true |> Result.value }

/// A trunk whose witnessable columns are NULLABLE — the seat of the
/// null-rate floor and the window ledger.
let private customerN : Kind =
    { Kind.create (kindKey ["CN"]) (name "CustomerN") (mkTableId "dbo" "CustomerN")
        [ attrOf (attrKey ["CN"; "Id"]) "Id" "Id" Integer true None true
          nullableAttr (attrKey ["CN"; "Email"]) "Email" "Email" Text (Some 40)
          nullableAttr (attrKey ["CN"; "RegionId"]) "RegionId" "RegionId" Integer None ] with
        Modality = [] }

let private region : Kind =
    { Kind.create (kindKey ["R"]) (name "Region") (mkTableId "dbo" "Region")
        [ attrOf (attrKey ["R"; "Id"]) "Id" "Id" Integer true None false ] with
        Modality = [] }

let private indexN =
    CatalogIndex.ofCatalog
        (Catalog.create [ mkModule (modKey "WN") (name "WN") [ customerN; region ] ] [] |> Result.value)

[<Fact>]
let ``the null-rate floor is planned first, ranks non-null rows, and asserts the recorded count`` () =
    let pack =
        packOf
            [ { Table = "dbo.CustomerN"; RowCount = 25L
                Columns = [ { col "Email" 25L with NullCount = 10L; MaxLength = Some 30 } ] } ] []
    let plan, skips = Witness.plan indexN pack
    Assert.Empty skips
    match plan.Cases with
    | [ NullRateWitness (_, "Email", _, 10L, 15L); MaxLengthWitness _ ] -> ()
    | other -> failwithf "the null-rate floor must precede the value witnesses: %A" other
    let sql = Witness.emitSql 7UL plan
    Assert.Contains("UPDATE w SET v = NULL WHERE rn > 15;", sql)
    // Every value witness ranks only non-null rows — planting a value
    // never converts a NULL.
    Assert.Contains("IS NOT NULL", sql)
    Assert.Contains("IS NULL) >= 10", Witness.emitAssertSql plan)

[<Fact>]
let ``a recorded null on a NOT NULL trunk column is a named skip`` () =
    let pack =
        packOf
            [ { Table = "dbo.Customer"; RowCount = 10L
                Columns = [ { col "Email" 10L with NullCount = 3L } ] } ] []
    let plan, skips = Witness.plan index pack
    Assert.Empty plan.Cases
    Assert.Equal("notNullable", (List.exactlyOne skips).Reason)

[<Fact>]
let ``witnesses on one column claim disjoint row windows`` () =
    // RegionId carries a null rate, an envelope, a duplicate, and an
    // orphan edge at once: the envelope holds rows 1-2, the duplicate
    // lands on row 3, the orphans take rows 4-6 — no witness overwrites
    // another's planted reality.
    let pack =
        packOf
            [ { Table = "dbo.CustomerN"; RowCount = 25L
                Columns =
                    [ { col "RegionId" 25L with
                          NullCount = 10L
                          Numeric = Some (shape 1m 7777m)
                          HasDuplicates = true } ] } ]
            [ { ChildTable = "dbo.CustomerN"; ChildColumn = "RegionId"; ParentTable = "dbo.Region"; OrphanCount = 3L } ]
    let plan, skips = Witness.plan indexN pack
    Assert.Empty skips
    Assert.Equal(4, List.length plan.Cases)
    let sql = Witness.emitSql 7UL plan
    Assert.Contains("WHERE rn = 3;", sql)
    Assert.Contains("WHERE rn > 3 AND rn <= 6;", sql)

[<Fact>]
let ``an orphan plant clamps to the non-null budget; a budget with no room is a named skip`` () =
    let clamped =
        packOf
            [ { Table = "dbo.CustomerN"; RowCount = 10L
                Columns = [ { col "RegionId" 10L with NullCount = 6L } ] } ]
            [ { ChildTable = "dbo.CustomerN"; ChildColumn = "RegionId"; ParentTable = "dbo.Region"; OrphanCount = 99L } ]
    let plan, skips = Witness.plan indexN clamped
    Assert.Empty skips
    Assert.Contains("WHERE rn > 0 AND rn <= 4;", Witness.emitSql 7UL plan)
    let noRoom =
        packOf
            [ { Table = "dbo.CustomerN"; RowCount = 10L
                Columns = [ { col "RegionId" 10L with NullCount = 10L } ] } ]
            [ { ChildTable = "dbo.CustomerN"; ChildColumn = "RegionId"; ParentTable = "dbo.Region"; OrphanCount = 2L } ]
    let _, skips2 = Witness.plan indexN noRoom
    Assert.Contains("insufficientNonNullRows", skips2 |> List.map (fun s -> s.Reason))

[<Fact>]
let ``the duplicate assertion ignores NULL groups`` () =
    // Two NULLs group together under GROUP BY; they must never satisfy a
    // duplicate witness whose planted reality is a copied VALUE.
    let pack =
        packOf
            [ { Table = "dbo.Customer"; RowCount = 10L
                Columns = [ { col "Code" 10L with HasDuplicates = true } ] } ] []
    let plan, _ = Witness.plan index pack
    Assert.Contains("IS NOT NULL GROUP BY", Witness.emitAssertSql plan)
// -- The string-plane witnesses (F1) ----------------------------------------

let private textOnly (empty: int64) (trailing: int64) (collisions: int64) : TextShape =
    { EmptyCount = empty; TrailingSpaceCount = trailing; CaseCollisions = collisions
      LengthP50 = None; LengthP90 = None }

[<Fact>]
let ``the empty-string floor claims the tail of the non-null space, above every bottom claim`` () =
    let pack =
        packOf
            [ { Table = "dbo.Customer"; RowCount = 10L
                Columns =
                    [ { col "Email" 10L with
                          MaxLength = Some 32
                          Text = Some (textOnly 3L 0L 0L) } ] } ] []
    let plan, skips = Witness.plan index pack
    Assert.Empty skips
    let sql = Witness.emitSql 7UL plan
    // The max-length witness claims row 1; the floor sets the top three
    // of the ten non-null rows empty — windows disjoint by construction.
    // (An empty string is a VALUE: the floor is legal on a NOT NULL
    // column, exactly the case the NOT-NULL-passes-over-'' trap needs.)
    Assert.Contains("WHERE rn = 1;", sql)
    Assert.Contains("SET v = N'' WHERE rn > 7;", sql)
    let assertSql = Witness.emitAssertSql plan
    Assert.Contains("DATALENGTH", assertSql)
    Assert.Contains(">= 3", assertSql)

[<Fact>]
let ``the trailing-space witness reshapes one row length-safely`` () =
    let pack =
        packOf
            [ { Table = "dbo.Customer"; RowCount = 10L
                Columns = [ { col "Email" 10L with Text = Some (textOnly 0L 2L 0L) } ] } ] []
    let plan, skips = Witness.plan index pack
    Assert.Empty skips
    let sql = Witness.emitSql 7UL plan
    // Email is NVARCHAR(40): the reshape trims to 39 then appends the space.
    Assert.Contains("LEFT(v, 39) + N' ' WHERE rn = 1;", sql)
    Assert.Contains("RTRIM", Witness.emitAssertSql plan)

[<Fact>]
let ``the case-collision pair differs only in case and stays under the observed max`` () =
    let pack =
        packOf
            [ { Table = "dbo.Customer"; RowCount = 10L
                Columns =
                    [ { col "Email" 10L with
                          MaxLength = Some 3
                          Text = Some (textOnly 0L 0L 2L) } ] } ] []
    let plan, skips = Witness.plan index pack
    Assert.Empty skips
    let sql = Witness.emitSql 7UL plan
    // The stem is capped at MaxLength - 1 = 2 characters, so the planted
    // pair never disturbs the max-length claim.
    let stems =
        [ for line in sql.Split '\n' do
            let t = line.Trim()
            if t.StartsWith "UPDATE w SET v = N'" && (t.Contains "a' WHERE" || t.Contains "A' WHERE") then t ]
    Assert.Equal(2, List.length stems)
    Assert.Contains("a' WHERE rn = 2;", sql)
    Assert.Contains("A' WHERE rn = 3;", sql)
    let assertSql = Witness.emitAssertSql plan
    Assert.Contains("GROUP BY UPPER(", assertSql)
    Assert.Contains("COUNT(DISTINCT", assertSql)

[<Fact>]
let ``a MAX-typed column cannot host the collision pair — a named skip`` () =
    let pack =
        packOf
            [ { Table = "dbo.Customer"; RowCount = 10L
                Columns = [ { col "Code" 10L with Text = Some (textOnly 0L 0L 1L) } ] } ] []
    let plan, skips = Witness.plan index pack
    Assert.Empty plan.Cases
    Assert.Equal("typeUnsupported", (List.exactlyOne skips).Reason)

[<Fact>]
let ``string witnesses claim disjoint windows after the duplicate claim`` () =
    let pack =
        packOf
            [ { Table = "dbo.Customer"; RowCount = 10L
                Columns =
                    [ { col "Email" 10L with
                          HasDuplicates = true
                          Text = Some (textOnly 2L 1L 1L) } ] } ] []
    let plan, skips = Witness.plan index pack
    Assert.Empty skips
    let sql = Witness.emitSql 7UL plan
    // Duplicate claims row 2, trailing row 3, the collision rows 4 and 5,
    // and the empty floor covers rows 9 and 10 — nothing overlaps.
    Assert.Contains("WHERE rn = 2;", sql)
    Assert.Contains("+ N' ' WHERE rn = 3;", sql)
    Assert.Contains("a' WHERE rn = 4;", sql)
    Assert.Contains("A' WHERE rn = 5;", sql)
    Assert.Contains("SET v = N'' WHERE rn > 8;", sql)
