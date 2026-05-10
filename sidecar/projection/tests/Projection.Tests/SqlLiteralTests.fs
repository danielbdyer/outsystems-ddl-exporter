module Projection.Tests.SqlLiteralTests

open Xunit
open Projection.Core

// ---------------------------------------------------------------------------
// Tier 1 #4 (RawTextEmitter retirement arc) — Projection.Core.SqlLiteral.
// The typed SQL-literal projection: IR (PrimitiveType + raw form) →
// typed `SqlLiteral` value → terminal SQL text. Both consumers (SSDT.
// Render.formatSqlLiteral, Data.StaticSeedsEmitter.formatValuesTuple)
// flow through this module.
//
// Tests cover:
//   - `ofRaw` projection per PrimitiveType variant (NULL sentinel,
//     numeric, boolean, temporal, text, binary)
//   - `toString` rendering per typed-literal variant
//   - `formatRaw` round-trip equivalence (the consumer-facing shape)
//   - Closed-DU coverage (every PrimitiveType variant resolves)
// ---------------------------------------------------------------------------

[<Fact>]
let ``SqlLiteral.ofRaw maps empty raw to NullLit (V2 IR NULL sentinel)`` () =
    Assert.Equal<SqlLiteral> (NullLit, SqlLiteral.ofRaw Integer "")
    Assert.Equal<SqlLiteral> (NullLit, SqlLiteral.ofRaw Text "")
    Assert.Equal<SqlLiteral> (NullLit, SqlLiteral.ofRaw Boolean "")

[<Fact>]
let ``SqlLiteral.ofRaw maps Integer to IntegerLit`` () =
    Assert.Equal<SqlLiteral> (IntegerLit "42", SqlLiteral.ofRaw Integer "42")
    Assert.Equal<SqlLiteral> (IntegerLit "-1", SqlLiteral.ofRaw Integer "-1")

[<Fact>]
let ``SqlLiteral.ofRaw maps Decimal to DecimalLit`` () =
    Assert.Equal<SqlLiteral> (DecimalLit "3.14", SqlLiteral.ofRaw Decimal "3.14")

[<Fact>]
let ``SqlLiteral.ofRaw maps Boolean via RawValueCodec.parseBoolean`` () =
    Assert.Equal<SqlLiteral> (BooleanLit true,  SqlLiteral.ofRaw Boolean "true")
    Assert.Equal<SqlLiteral> (BooleanLit false, SqlLiteral.ofRaw Boolean "false")
    // V1-bridge tolerance per RawValueCodec: "1"/"0" also accepted.
    Assert.Equal<SqlLiteral> (BooleanLit true,  SqlLiteral.ofRaw Boolean "1")
    Assert.Equal<SqlLiteral> (BooleanLit false, SqlLiteral.ofRaw Boolean "0")

[<Fact>]
let ``SqlLiteral.ofRaw maps temporal types to TemporalLit`` () =
    Assert.Equal<SqlLiteral> (TemporalLit "2026-05-10", SqlLiteral.ofRaw Date "2026-05-10")
    Assert.Equal<SqlLiteral> (TemporalLit "2026-05-10 12:30:00.0000000", SqlLiteral.ofRaw DateTime "2026-05-10 12:30:00.0000000")
    Assert.Equal<SqlLiteral> (TemporalLit "12:30:00", SqlLiteral.ofRaw Time "12:30:00")

[<Fact>]
let ``SqlLiteral.ofRaw maps Guid to GuidLit`` () =
    Assert.Equal<SqlLiteral> (GuidLit "0F0E0D0C-0B0A-0908-0706-050403020100", SqlLiteral.ofRaw Guid "0F0E0D0C-0B0A-0908-0706-050403020100")

[<Fact>]
let ``SqlLiteral.ofRaw maps Text to TextLit (raw, unescaped)`` () =
    Assert.Equal<SqlLiteral> (TextLit "Hello", SqlLiteral.ofRaw Text "Hello")
    // Escaping happens at toString time, not ofRaw time.
    Assert.Equal<SqlLiteral> (TextLit "O'Brien", SqlLiteral.ofRaw Text "O'Brien")

[<Fact>]
let ``SqlLiteral.ofRaw maps Binary to BinaryLit (with 0x prefix)`` () =
    let prefixed = RawValueCodec.withHexPrefix "CAFEBABE"
    Assert.Equal<SqlLiteral> (BinaryLit prefixed, SqlLiteral.ofRaw Binary "CAFEBABE")

[<Fact>]
let ``SqlLiteral.toString renders NullLit as NULL`` () =
    Assert.Equal<string> ("NULL", SqlLiteral.toString NullLit)

[<Fact>]
let ``SqlLiteral.toString renders IntegerLit and DecimalLit bare (no quoting)`` () =
    Assert.Equal<string> ("42", SqlLiteral.toString (IntegerLit "42"))
    Assert.Equal<string> ("3.14", SqlLiteral.toString (DecimalLit "3.14"))

[<Fact>]
let ``SqlLiteral.toString renders BooleanLit as SQL bit (1 / 0)`` () =
    Assert.Equal<string> ("1", SqlLiteral.toString (BooleanLit true))
    Assert.Equal<string> ("0", SqlLiteral.toString (BooleanLit false))

[<Fact>]
let ``SqlLiteral.toString renders TextLit with N prefix and single-quote doubling`` () =
    Assert.Equal<string> ("N'Hello'", SqlLiteral.toString (TextLit "Hello"))
    Assert.Equal<string> ("N'O''Brien'", SqlLiteral.toString (TextLit "O'Brien"))

[<Fact>]
let ``SqlLiteral.toString renders TemporalLit and GuidLit with single-quote wrapping`` () =
    Assert.Equal<string> ("'2026-05-10'", SqlLiteral.toString (TemporalLit "2026-05-10"))
    Assert.Equal<string> ("'0F0E0D0C-0B0A-0908-0706-050403020100'", SqlLiteral.toString (GuidLit "0F0E0D0C-0B0A-0908-0706-050403020100"))

[<Fact>]
let ``SqlLiteral.toString renders BinaryLit bare (already hex-prefixed)`` () =
    let prefixed = RawValueCodec.withHexPrefix "CAFEBABE"
    Assert.Equal<string> (prefixed, SqlLiteral.toString (BinaryLit prefixed))

[<Fact>]
let ``SqlLiteral.formatRaw equals ofRaw |> toString (consumer-facing convenience)`` () =
    let typ = Text
    let raw = "Hello, world"
    Assert.Equal<string> (
        SqlLiteral.ofRaw typ raw |> SqlLiteral.toString,
        SqlLiteral.formatRaw typ raw)

[<Fact>]
let ``Closed-DU coverage: every PrimitiveType variant produces a SqlLiteral via ofRaw`` () =
    // The closed-DU expansion empirical-test discipline (DECISIONS
    // 2026-05-13): when a new PrimitiveType variant lands, this match
    // fires F# exhaustiveness errors at compile time under
    // TreatWarningsAsErrors. Adding the corresponding `ofRaw` case is
    // forced; this runtime test confirms the coverage is real.
    let variants : PrimitiveType list =
        [ Integer; Decimal; Boolean; DateTime; Date; Time; Guid; Text; Binary ]
    for v in variants do
        let lit = SqlLiteral.ofRaw v "0"  // any non-empty raw
        Assert.NotEqual<SqlLiteral> (NullLit, lit)
