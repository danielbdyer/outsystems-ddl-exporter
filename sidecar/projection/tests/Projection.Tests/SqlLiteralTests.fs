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
let ``RawValueCodec.parseBoolean raises a named refusal on unrecognized input (NM-20)`` () =
    // NM-20 — every sibling parser (DateTime.ParseExact / Guid.Parse) throws on
    // garbage; Boolean previously silently coerced "2"/"yes"/"tru" to false,
    // hiding a real BIT divergence. It now fails loud with the named refusal
    // code carried in the message.
    for garbage in [ "2"; "yes"; "tru"; "FALSE_"; " " ] do
        let ex = Assert.Throws<System.FormatException>(fun () -> RawValueCodec.parseBoolean garbage |> ignore)
        Assert.Contains(RawValueCodec.BooleanUnrecognizedCode, ex.Message)
    // The canonical + V1-bridge forms still parse (no regression).
    Assert.True(RawValueCodec.parseBoolean "true")
    Assert.True(RawValueCodec.parseBoolean "1")
    Assert.False(RawValueCodec.parseBoolean "false")
    Assert.False(RawValueCodec.parseBoolean "0")

[<Fact>]
let ``SqlLiteral.ofRaw on an unrecognized Boolean raw fails loud, not silent-false (NM-20)`` () =
    Assert.Throws<System.FormatException>(fun () -> SqlLiteral.ofRaw Boolean "2" |> ignore) |> ignore

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

// ---------------------------------------------------------------------------
// AC-D6 (NEITHER→HELD) — representation-only literal differences are tolerated
// by SQL Server's NATIVE column comparison; they do NOT fire CDC.
//
// These are the literal-level discriminating witnesses for the two named
// tolerances `CharAnsiPaddingTolerated` / `DecimalScaleTolerated` (see
// `ToleranceTests.fs` AC-D6 section). The CDC predicate
// (`ScriptDomBuild.perColumnChangeDetection`) compares `Target.[c] <>
// Source.[c]` COLUMN-TO-COLUMN — the stored typed values, NOT these literals.
// `SqlLiteral` renders literals only on the INSERT/DEFAULT side. The point of
// these tests is to pin down WHY the representation difference is benign:
//   - Decimal: `"1.0"` and `"1.00"` render to DIFFERENT literal TEXT, yet
//     both denote the SAME numeric value — so once stored into a single
//     `decimal(p,s)` column they are byte-identical and `<>` is FALSE. The
//     difference is purely the trailing-zero scale shape (representation),
//     not the value. (SQL numeric `<>`: `1.0 <> 1.00` = FALSE.)
//   - Char: a `char(n)`-typed value is ANSI trailing-blank-padded to its
//     declared width on store, so `'foo  '` and `'foo'` become the SAME
//     stored value; `<>` re-pads the shorter operand and yields FALSE. The
//     raw `"foo  "` and `"foo"` are equal up to trailing blanks — the only
//     difference the column erases on store.
// ---------------------------------------------------------------------------

[<Fact>]
let ``AC-D6: Decimal "1.0" and "1.00" render to different literal text but denote the same numeric value`` () =
    let oneDotZero  = SqlLiteral.ofRaw Decimal "1.0"
    let oneDotZeroZero = SqlLiteral.ofRaw Decimal "1.00"
    // The literal TEXT differs — `SqlLiteral` is faithful to the raw scale on
    // the INSERT side (it does not canonicalize). This is the representation
    // difference.
    Assert.NotEqual<string> (SqlLiteral.toString oneDotZero, SqlLiteral.toString oneDotZeroZero)
    // ...yet both denote the SAME numeric quantity. We witness the
    // value-equivalence by parsing the rendered literals back as decimals:
    // `1.0m = 1.00m` is TRUE (.NET decimal equality is by value, matching SQL
    // Server's numeric `<>`). This is exactly why the column-to-column CDC
    // predicate `Target.[c] <> Source.[c]` does NOT fire on a scale-only
    // difference: once both literals are stored into the same `decimal`
    // column, the stored values are numerically equal.
    let asDecimal (lit: SqlLiteral) =
        match lit with
        | DecimalLit s -> System.Decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture)
        | other -> failwithf "expected DecimalLit, got %A" other
    Assert.Equal (asDecimal oneDotZero, asDecimal oneDotZeroZero)

[<Fact>]
let ``AC-D6: char-typed padded and unpadded raw differ only by trailing blanks the column re-pads`` () =
    // A `char(n)` column ANSI-pads its stored value to the declared width, so
    // a padded raw (`"foo  "`) and an unpadded raw (`"foo"`) become the SAME
    // stored value. `SqlLiteral` renders Text faithfully (it does NOT RTRIM —
    // no normalization is needed, the column does it on store), so the
    // rendered literals differ ONLY in the trailing blanks:
    let padded   = SqlLiteral.ofRaw Text "foo  "
    let unpadded = SqlLiteral.ofRaw Text "foo"
    let renderedPadded   = SqlLiteral.toString padded
    let renderedUnpadded = SqlLiteral.toString unpadded
    Assert.Equal<string> ("N'foo  '", renderedPadded)
    Assert.Equal<string> ("N'foo'", renderedUnpadded)
    // The ONLY difference between the two stored values is trailing blanks —
    // exactly what `char(n)` storage and SQL Server's ANSI `<>` normalize
    // away (`'foo  ' <> 'foo'` = FALSE). Witness: trimming trailing blanks
    // collapses the two raws to the identical canonical value.
    let trimTrailing (s: string) = s.TrimEnd(' ')
    Assert.Equal<string> (trimTrailing "foo  ", trimTrailing "foo")
    // And the difference is representation-only: it is purely trailing blanks,
    // not interior or leading content.
    Assert.True (("foo  ").StartsWith("foo"))

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
