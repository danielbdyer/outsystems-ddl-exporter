module Projection.Tests.SqlStorageTypeTests

open Xunit
open Projection.Core

// ---------------------------------------------------------------------------
// `SqlStorageType` — the concrete SQL Server realization, distinct from the
// semantic `PrimitiveType`. These tests pin three contracts:
//   1. `toPrimitiveType` projects every storage variant to the right
//      semantic category (the adapter-consistency witness).
//   2. `ofPrimitiveType` is a section of `toPrimitiveType` (round-trip).
//   3. `ofSqlType` parses both inline-parenthesized strings (external_dbType)
//      and bare-name + facet-column shapes (INFORMATION_SCHEMA).
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// toPrimitiveType — the semantic category each concrete storage type belongs
// to. The collapse points the separation exists to preserve: BigInt and Int
// both project to Integer; DateTime and DateTime2 both project to DateTime.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Integer family projects to PrimitiveType.Integer`` () =
    Assert.Equal (Integer, SqlStorageType.toPrimitiveType SqlStorageType.BigInt)
    Assert.Equal (Integer, SqlStorageType.toPrimitiveType SqlStorageType.Int)
    Assert.Equal (Integer, SqlStorageType.toPrimitiveType SqlStorageType.SmallInt)
    Assert.Equal (Integer, SqlStorageType.toPrimitiveType SqlStorageType.TinyInt)

[<Fact>]
let ``DateTime family projects to PrimitiveType.DateTime`` () =
    Assert.Equal (DateTime, SqlStorageType.toPrimitiveType SqlStorageType.DateTime)
    Assert.Equal (DateTime, SqlStorageType.toPrimitiveType (SqlStorageType.DateTime2 None))
    Assert.Equal (DateTime, SqlStorageType.toPrimitiveType (SqlStorageType.DateTimeOffset (Some 7)))
    Assert.Equal (DateTime, SqlStorageType.toPrimitiveType SqlStorageType.SmallDateTime)

[<Fact>]
let ``Text family projects to PrimitiveType.Text`` () =
    Assert.Equal (Text, SqlStorageType.toPrimitiveType (SqlStorageType.NVarChar Max))
    Assert.Equal (Text, SqlStorageType.toPrimitiveType (SqlStorageType.VarChar (Bounded 50)))
    Assert.Equal (Text, SqlStorageType.toPrimitiveType SqlStorageType.NText)
    Assert.Equal (Text, SqlStorageType.toPrimitiveType SqlStorageType.Xml)

[<Fact>]
let ``Decimal family projects to PrimitiveType.Decimal`` () =
    Assert.Equal (Decimal, SqlStorageType.toPrimitiveType (SqlStorageType.Decimal (18, 2)))
    Assert.Equal (Decimal, SqlStorageType.toPrimitiveType SqlStorageType.Money)
    Assert.Equal (Decimal, SqlStorageType.toPrimitiveType SqlStorageType.Float)

[<Fact>]
let ``Binary family / Bit / Guid project to their categories`` () =
    Assert.Equal (Binary, SqlStorageType.toPrimitiveType (SqlStorageType.VarBinary Max))
    Assert.Equal (Binary, SqlStorageType.toPrimitiveType SqlStorageType.Image)
    Assert.Equal (Boolean, SqlStorageType.toPrimitiveType SqlStorageType.Bit)
    Assert.Equal (Guid, SqlStorageType.toPrimitiveType SqlStorageType.UniqueIdentifier)

// ---------------------------------------------------------------------------
// ofPrimitiveType — the canonical fallback storage. Round-trips through
// toPrimitiveType for every PrimitiveType (the section law).
// ---------------------------------------------------------------------------

[<Fact>]
let ``ofPrimitiveType is a section of toPrimitiveType over every PrimitiveType`` () =
    for pt in SqlTypeCorrespondence.allPrimitives do
        let recovered = SqlStorageType.toPrimitiveType (SqlStorageType.ofPrimitiveType pt)
        Assert.Equal (pt, recovered)

[<Fact>]
let ``ofPrimitiveType mirrors the legacy SqlTypeCorrespondence defaults`` () =
    // Integer → INT (semantic fallback keeps the v2 default; only the
    // OSSYS adapter's longinteger evidence narrows to BIGINT).
    Assert.Equal (SqlStorageType.Int, SqlStorageType.ofPrimitiveType Integer)
    // DateTime → DATETIME2 (the existing fallback; the OSSYS adapter's
    // `datetime` evidence narrows to DATETIME).
    Assert.Equal (SqlStorageType.DateTime2 None, SqlStorageType.ofPrimitiveType DateTime)
    Assert.Equal (SqlStorageType.NVarChar Max, SqlStorageType.ofPrimitiveType Text)

// ---------------------------------------------------------------------------
// ofSqlType — inline-parenthesized form (external_dbType override strings).
// ---------------------------------------------------------------------------

[<Fact>]
let ``ofSqlType parses bare scalar SQL types`` () =
    Assert.Equal (Some SqlStorageType.BigInt, SqlStorageType.ofSqlType "BIGINT" None None None)
    Assert.Equal (Some SqlStorageType.DateTime, SqlStorageType.ofSqlType "datetime" None None None)
    Assert.Equal (Some SqlStorageType.UniqueIdentifier, SqlStorageType.ofSqlType "UNIQUEIDENTIFIER" None None None)

[<Fact>]
let ``ofSqlType parses parenthesized length and MAX`` () =
    Assert.Equal (Some (SqlStorageType.NVarChar Max), SqlStorageType.ofSqlType "NVARCHAR(MAX)" None None None)
    Assert.Equal (Some (SqlStorageType.NVarChar (Bounded 255)), SqlStorageType.ofSqlType "NVARCHAR(255)" None None None)
    Assert.Equal (Some (SqlStorageType.VarChar (Bounded 20)), SqlStorageType.ofSqlType "varchar(20)" None None None)

[<Fact>]
let ``ofSqlType parses parenthesized precision and scale`` () =
    Assert.Equal (Some (SqlStorageType.Decimal (18, 2)), SqlStorageType.ofSqlType "DECIMAL(18,2)" None None None)
    Assert.Equal (Some (SqlStorageType.Numeric (10, 0)), SqlStorageType.ofSqlType "numeric(10, 0)" None None None)

// ---------------------------------------------------------------------------
// ofSqlType — bare-name + facet-column form (INFORMATION_SCHEMA shape:
// DATA_TYPE plus separate length / precision / scale columns).
// ---------------------------------------------------------------------------

[<Fact>]
let ``ofSqlType folds facet columns into the storage type`` () =
    Assert.Equal (Some (SqlStorageType.NVarChar (Bounded 100)), SqlStorageType.ofSqlType "nvarchar" (Some 100) None None)
    Assert.Equal (Some (SqlStorageType.Decimal (12, 4)), SqlStorageType.ofSqlType "decimal" None (Some 12) (Some 4))

[<Fact>]
let ``ofSqlType maps the -1 length sentinel to MAX`` () =
    Assert.Equal (Some (SqlStorageType.NVarChar Max), SqlStorageType.ofSqlType "nvarchar" (Some -1) None None)

[<Fact>]
let ``ofSqlType returns None for empty and unknown types`` () =
    Assert.Equal<SqlStorageType option> (None, SqlStorageType.ofSqlType "" None None None)
    Assert.Equal<SqlStorageType option> (None, SqlStorageType.ofSqlType "   " None None None)
    Assert.Equal<SqlStorageType option> (None, SqlStorageType.ofSqlType "HIERARCHYID" None None None)

[<Fact>]
let ``ofSqlType parenthesized parameters win over facet arguments`` () =
    // External override "NVARCHAR(MAX)" beats a stray facet length.
    Assert.Equal (Some (SqlStorageType.NVarChar Max), SqlStorageType.ofSqlType "NVARCHAR(MAX)" (Some 50) None None)
