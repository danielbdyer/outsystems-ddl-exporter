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
let ``SqlLiteral.ofRaw maps None to NullLit (WP-3: NULL is out-of-band, not the empty raw)`` () =
    Assert.Equal<SqlLiteral> (NullLit, SqlLiteral.ofRaw Integer None)
    Assert.Equal<SqlLiteral> (NullLit, SqlLiteral.ofRaw Text None)
    Assert.Equal<SqlLiteral> (NullLit, SqlLiteral.ofRaw Boolean None)

[<Fact>]
let ``SqlLiteral.ofRaw preserves the empty raw where the type has an empty form (F11)`` () =
    // A genuine empty string is a VALUE, distinct from NULL: Text renders
    // `N''`; Binary renders the zero-length `0x`. The retired NM-18
    // universal sentinel used to collapse both to NULL.
    Assert.Equal<SqlLiteral> (TextLit "", SqlLiteral.ofRaw Text (Some ""))
    Assert.Equal<string> ("N''", SqlLiteral.toString (SqlLiteral.ofRaw Text (Some "")))
    Assert.Equal<SqlLiteral> (BinaryLit "0x", SqlLiteral.ofRaw Binary (Some ""))

[<Fact>]
let ``SqlLiteral.ofRaw refuses an empty raw on a type with no empty form (named, NM-20 shape)`` () =
    for typ in [ Integer; Decimal; Boolean; DateTime; Date; Time; Guid ] do
        let ex = Assert.Throws<System.FormatException>(fun () -> SqlLiteral.ofRaw typ (Some "") |> ignore)
        Assert.Contains(SqlLiteral.EmptyNotEmptyCapableCode, ex.Message)

[<Fact>]
let ``SqlLiteral.ofRaw maps Integer to IntegerLit`` () =
    Assert.Equal<SqlLiteral> (IntegerLit "42", SqlLiteral.ofRaw Integer (Some "42"))
    Assert.Equal<SqlLiteral> (IntegerLit "-1", SqlLiteral.ofRaw Integer (Some "-1"))

[<Fact>]
let ``SqlLiteral.ofRaw maps Decimal to DecimalLit`` () =
    Assert.Equal<SqlLiteral> (DecimalLit "3.14", SqlLiteral.ofRaw Decimal (Some "3.14"))

[<Fact>]
let ``SqlLiteral.ofRaw maps Boolean via RawValueCodec.parseBoolean`` () =
    Assert.Equal<SqlLiteral> (BooleanLit true,  SqlLiteral.ofRaw Boolean (Some "true"))
    Assert.Equal<SqlLiteral> (BooleanLit false, SqlLiteral.ofRaw Boolean (Some "false"))
    // V1-bridge tolerance per RawValueCodec: "1"/"0" also accepted.
    Assert.Equal<SqlLiteral> (BooleanLit true,  SqlLiteral.ofRaw Boolean (Some "1"))
    Assert.Equal<SqlLiteral> (BooleanLit false, SqlLiteral.ofRaw Boolean (Some "0"))

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
    Assert.Throws<System.FormatException>(fun () -> SqlLiteral.ofRaw Boolean (Some "2") |> ignore) |> ignore

[<Fact>]
let ``SqlLiteral.ofRaw maps each temporal category to its own variant (WP-17d)`` () =
    Assert.Equal<SqlLiteral> (DateLit "2026-05-10", SqlLiteral.ofRaw Date (Some "2026-05-10"))
    Assert.Equal<SqlLiteral> (DateTimeLit "2026-05-10 12:30:00.0000000", SqlLiteral.ofRaw DateTime (Some "2026-05-10 12:30:00.0000000"))
    Assert.Equal<SqlLiteral> (TimeLit "12:30:00", SqlLiteral.ofRaw Time (Some "12:30:00"))

[<Fact>]
let ``SqlLiteral.ofRaw maps Guid to GuidLit`` () =
    Assert.Equal<SqlLiteral> (GuidLit "0F0E0D0C-0B0A-0908-0706-050403020100", SqlLiteral.ofRaw Guid (Some "0F0E0D0C-0B0A-0908-0706-050403020100"))

[<Fact>]
let ``SqlLiteral.ofRaw maps Text to TextLit (raw, unescaped)`` () =
    Assert.Equal<SqlLiteral> (TextLit "Hello", SqlLiteral.ofRaw Text (Some "Hello"))
    // Escaping happens at toString time, not ofRaw time.
    Assert.Equal<SqlLiteral> (TextLit "O'Brien", SqlLiteral.ofRaw Text (Some "O'Brien"))

[<Fact>]
let ``SqlLiteral.ofRaw maps Binary to BinaryLit (with 0x prefix)`` () =
    let prefixed = RawValueCodec.withHexPrefix "CAFEBABE"
    Assert.Equal<SqlLiteral> (BinaryLit prefixed, SqlLiteral.ofRaw Binary (Some "CAFEBABE"))

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
let ``SqlLiteral.toString renders the V1 explicit-CAST temporal forms (WP-17d)`` () =
    // V1 `SqlLiteralFormatter.cs:90` parity: precision-explicit,
    // language-independent — never a bare quoted string.
    Assert.Equal<string> ("CAST('2026-05-10' AS date)", SqlLiteral.toString (DateLit "2026-05-10"))
    Assert.Equal<string> ("CAST('2026-05-10 12:30:00.0000000' AS datetime2(7))", SqlLiteral.toString (DateTimeLit "2026-05-10 12:30:00.0000000"))
    Assert.Equal<string> ("CAST('08:30:00' AS time(7))", SqlLiteral.toString (TimeLit "08:30:00"))

[<Fact>]
let ``SqlLiteral.toString renders GuidLit with single-quote wrapping`` () =
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
        SqlLiteral.ofRaw typ (Some raw) |> SqlLiteral.toString,
        SqlLiteral.formatRaw typ (Some raw))

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
    let oneDotZero  = SqlLiteral.ofRaw Decimal (Some "1.0")
    let oneDotZeroZero = SqlLiteral.ofRaw Decimal (Some "1.00")
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
    let padded   = SqlLiteral.ofRaw Text (Some "foo  ")
    let unpadded = SqlLiteral.ofRaw Text (Some "foo")
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
        let lit = SqlLiteral.ofRaw v (Some "0")  // any non-empty raw
        Assert.NotEqual<SqlLiteral> (NullLit, lit)

// ---------------------------------------------------------------------------
// WP-17(e) (DECISIONS 2026-07-16) — CR/LF/TAB splice into CHAR() concatenation
// (V1 `EscapeUnicodeString` parity); the emitted SQL carries no raw control
// bytes. A control-char-free raw renders byte-identically to the pre-WP-17
// form (the default everywhere).
// ---------------------------------------------------------------------------

[<Fact>]
let ``WP-17e: toString splices CR LF TAB into CHAR() concatenation (V1 parity)`` () =
    Assert.Equal<string> ("N'a' + CHAR(13) + N'b'", SqlLiteral.toString (TextLit "a\rb"))
    Assert.Equal<string> ("N'a' + CHAR(10) + N'b'", SqlLiteral.toString (TextLit "a\nb"))
    Assert.Equal<string> ("N'a' + CHAR(9) + N'b'",  SqlLiteral.toString (TextLit "a\tb"))
    // CRLF = two adjacent control chars — the V1-parity blind splice keeps
    // the empty run between them.
    Assert.Equal<string> ("N'a' + CHAR(13) + N'' + CHAR(10) + N'b'", SqlLiteral.toString (TextLit "a\r\nb"))
    // Leading/trailing control chars yield empty edge runs (V1 parity).
    Assert.Equal<string> ("N'' + CHAR(10) + N'x'", SqlLiteral.toString (TextLit "\nx"))
    Assert.Equal<string> ("N'x' + CHAR(9) + N''",  SqlLiteral.toString (TextLit "x\t"))

[<Fact>]
let ``WP-17e: quote-doubling still applies inside spliced runs`` () =
    Assert.Equal<string> ("N'O''Brien' + CHAR(10) + N'line2'", SqlLiteral.toString (TextLit "O'Brien\nline2"))

[<Fact>]
let ``WP-17e: a control-char-free Text raw renders byte-identically (no splice)`` () =
    Assert.Equal<string> ("N'Hello'", SqlLiteral.toString (TextLit "Hello"))
    Assert.Equal<string> ("N''", SqlLiteral.toString (TextLit ""))

[<Fact>]
let ``WP-17e: textLiteralSegments — the shared segmentation both planes ride`` () =
    Assert.Equal<TextLiteralSegment list>([ TextRun "plain" ], SqlLiteral.textLiteralSegments "plain")
    Assert.Equal<TextLiteralSegment list>(
        [ TextRun "a"; ControlChar 13; TextRun ""; ControlChar 10; TextRun "b" ],
        SqlLiteral.textLiteralSegments "a\r\nb")

// ---------------------------------------------------------------------------
// WP-17(a/b) (DECISIONS 2026-07-16) — faithful carriage for the collapsing
// concrete types. The raw STRING carries the concrete value (G17/G9 for
// float/real; the offset-bearing form for datetimeoffset); the boundaries
// dispatch on the raw shape — no new carrier, the 9-way category stands.
// ---------------------------------------------------------------------------

[<Fact>]
let ``WP-17b: RawValueCodec round-trips a DateTimeOffset with its offset preserved`` () =
    let value = System.DateTimeOffset(2026, 7, 16, 12, 30, 0, System.TimeSpan.FromHours -3.0)
    let raw = RawValueCodec.formatDateTimeOffset value
    Assert.Equal<string> ("2026-07-16 12:30:00.0000000 -03:00", raw)
    Assert.True(RawValueCodec.hasUtcOffset raw)
    let back = RawValueCodec.parseDateTimeOffset raw
    Assert.Equal(value, back)
    Assert.Equal(System.TimeSpan.FromHours -3.0, back.Offset)

[<Fact>]
let ``WP-17b: hasUtcOffset is disjoint from every offset-less canonical raw shape`` () =
    Assert.True(RawValueCodec.hasUtcOffset "2026-07-16 12:30:00.0000000 +03:00")
    Assert.False(RawValueCodec.hasUtcOffset "2026-07-16 12:30:00.0000000")
    Assert.False(RawValueCodec.hasUtcOffset "2026-07-16")
    Assert.False(RawValueCodec.hasUtcOffset "08:30:00")
    Assert.False(RawValueCodec.hasUtcOffset "-00:30:00")

[<Fact>]
let ``WP-17b: an offset-bearing DateTime raw owns its CAST target (datetimeoffset(7))`` () =
    let offsetRaw = "2026-07-16 12:30:00.0000000 -03:00"
    Assert.Equal<SqlLiteral> (DateTimeOffsetLit offsetRaw, SqlLiteral.ofRaw DateTime (Some offsetRaw))
    Assert.Equal<string> (
        "CAST('2026-07-16 12:30:00.0000000 -03:00' AS datetimeoffset(7))",
        SqlLiteral.toString (DateTimeOffsetLit offsetRaw))
    // The offset-less canonical form keeps datetime2(7) — the shapes are disjoint.
    Assert.Equal<SqlLiteral> (
        DateTimeLit "2026-07-16 12:30:00.0000000",
        SqlLiteral.ofRaw DateTime (Some "2026-07-16 12:30:00.0000000"))
