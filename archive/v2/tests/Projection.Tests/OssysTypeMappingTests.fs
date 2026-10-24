module Projection.Tests.OssysTypeMappingTests

open Xunit
open Projection.Core

// recon #10 — the OSSYS→V2 type-correspondence DECISIONS now live in pure Core,
// so they are pinned here directly (no OSSYS JSON/DB fixture). These guard the
// load-bearing choices: the legacy `datetime`, the OutSystems 11 platform widths,
// the verbatim-to-4000 text-length rule, and the reference-storage convention.

[<Fact>]
let ``tryParse: longinteger collapses to Integer but keeps BIGINT storage`` () =
    Assert.Equal(Some (Integer, SqlStorageType.BigInt), OssysTypeMapping.tryParse "longinteger" None None None)
    Assert.Equal(Some (Integer, SqlStorageType.Int), OssysTypeMapping.tryParse "integer" None None None)

[<Fact>]
let ``tryParse: datetime maps to legacy DATETIME, not DATETIME2`` () =
    Assert.Equal(Some (DateTime, SqlStorageType.DateTime), OssysTypeMapping.tryParse "datetime" None None None)
    Assert.Equal(Some (DateTime, SqlStorageType.DateTime2 (Some 7)), OssysTypeMapping.tryParse "datetime2" None None None)

[<Fact>]
let ``tryParse: date-only and time-only store as DATETIME, the platform mapping (DECISIONS 2026-07-18)`` () =
    Assert.Equal(Some (DateTime, SqlStorageType.DateTime), OssysTypeMapping.tryParse "date" None None None)
    Assert.Equal(Some (DateTime, SqlStorageType.DateTime), OssysTypeMapping.tryParse "time" None None None)

[<Fact>]
let ``tryParse: currency is the imposed DECIMAL(37,8); decimal reads precision from the Length property, defaulting to the platform (37,8)`` () =
    // Database Data Types (OutSystems 11): Decimal deploys as decimal() whose
    // "precision and scale match the Length and Decimals properties"; the
    // Service-Studio defaults are Length = 37, Decimals = 8. OSSYS carries the
    // digit budget in `Length` (standard estates have no `Precision` column),
    // so the reader takes precision ← explicit precision, else positive
    // Length, else 37 — retiring the invented (18, 0) fallback that made every
    // standard Decimal diverge from its deployed decimal(37, 8).
    Assert.Equal(Some (Decimal, SqlStorageType.Decimal (37, 8)), OssysTypeMapping.tryParse "currency" None None None)
    Assert.Equal(Some (Decimal, SqlStorageType.Decimal (37, 8)), OssysTypeMapping.tryParse "decimal" None None None)
    // The standard estate shape: digits in Length, fraction via Decimals→scale.
    Assert.Equal(Some (Decimal, SqlStorageType.Decimal (37, 8)), OssysTypeMapping.tryParse "decimal" (Some 37) None (Some 8))
    Assert.Equal(Some (Decimal, SqlStorageType.Decimal (20, 2)), OssysTypeMapping.tryParse "decimal" (Some 20) None (Some 2))
    // An explicit Precision column (or external override) wins over Length.
    Assert.Equal(Some (Decimal, SqlStorageType.Decimal (12, 4)), OssysTypeMapping.tryParse "decimal" (Some 37) (Some 12) (Some 4))
    // A non-positive Length carries no digit budget — the platform default applies.
    Assert.Equal(Some (Decimal, SqlStorageType.Decimal (37, 8)), OssysTypeMapping.tryParse "decimal" (Some 0) None None)
    // An explicit zero scale (an integer-valued decimal) is preserved, never defaulted.
    Assert.Equal(Some (Decimal, SqlStorageType.Decimal (18, 0)), OssysTypeMapping.tryParse "decimal" (Some 18) None (Some 0))

[<Fact>]
let ``email/phone map to the OutSystems 11 platform VARCHAR(250)/(20), with the default width overridden by a declared length (DECISIONS 2026-07-18)`` () =
    Assert.Equal(Some (Text, SqlStorageType.VarChar (Bounded 250)), OssysTypeMapping.tryParse "email" None None None)
    Assert.Equal(Some (Text, SqlStorageType.VarChar (Bounded 50)),  OssysTypeMapping.tryParse "email" (Some 50) None None)
    Assert.Equal(Some (Text, SqlStorageType.VarChar (Bounded 20)),  OssysTypeMapping.tryParse "phone" None None None)
    Assert.Equal(Some (Text, SqlStorageType.VarChar (Bounded 20)),  OssysTypeMapping.tryParse "phonenumber" None None None)

[<Fact>]
let ``tryParse: a reference (entityreference and the bt*-binding form) stores the target identifier as BIGINT`` () =
    Assert.Equal(Some (Integer, SqlStorageType.BigInt), OssysTypeMapping.tryParse "entityreference" None None None)
    Assert.Equal(Some (Integer, SqlStorageType.BigInt), OssysTypeMapping.tryParse "btabc*def" None None None)

[<Fact>]
let ``tryParse: an unmapped type is None (the adapter names the refusal)`` () =
    Assert.Equal(None, OssysTypeMapping.tryParse "nonsensetype" None None None)

[<Theory>]
[<InlineData(100)>]
[<InlineData(2000)>]
[<InlineData(2500)>]
[<InlineData(4000)>]
let ``textLength: a declared length is preserved verbatim through the NVARCHAR bounded ceiling (DECISIONS 2026-07-18)`` (n: int) =
    Assert.Equal(Bounded n, OssysTypeMapping.textLength (Some n))

[<Theory>]
[<InlineData(4001)>]
[<InlineData(5000)>]
let ``textLength: beyond the 4000 bounded ceiling is open-ended (MAX)`` (n: int) =
    Assert.Equal(Max, OssysTypeMapping.textLength (Some n))

[<Fact>]
let ``textLength: absence (or a non-positive length) is MAX`` () =
    Assert.Equal(Max, OssysTypeMapping.textLength None)
    Assert.Equal(Max, OssysTypeMapping.textLength (Some 0))

[<Fact>]
let ``boundedOr: a declared length wins; otherwise the fallback`` () =
    Assert.Equal(Bounded 30, OssysTypeMapping.boundedOr (Bounded 250) (Some 30))
    Assert.Equal(Bounded 250, OssysTypeMapping.boundedOr (Bounded 250) None)
    Assert.Equal(Max, OssysTypeMapping.boundedOr Max (Some 0))
