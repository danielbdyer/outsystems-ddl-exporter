namespace Projection.Core

/// Typed SQL literal expression. Per the chapter 4.1.A close arc
/// (RawTextEmitter retirement Tier-1 #4): the IR→SQL projection is
/// the concept-shaped middle layer between V2's IR (`PrimitiveType` +
/// raw-form `string` per `RawValueCodec`) and the absolute terminal
/// SQL text. Both consumers of "format an IR value as a SQL literal"
/// (SSDT.Render's `formatSqlLiteral`, Data.StaticSeedsEmitter's
/// `formatValuesTuple`) flow through this module.
///
/// **Pillar 8 four-question domain-naming analysis:**
///   1. Domain concept: a SQL literal expression — the typed
///      representation of a value as it appears in SQL. Cutover-
///      business term: "the value as the database sees it."
///   2. V2 already names this concept somewhere? No — `Render
///      .formatSqlLiteral` was a function, not a typed value.
///   3. Concept-shaped: yes — `SqlLiteral` IS the literal.
///   4. Generic-suffix smell test: no Helper / Util / Manager.
///
/// **Pillar 1 (data-structure-oriented):** `SqlLiteral` is the typed
/// shape; `toString` is the terminal rendering boundary. The IR→
/// literal projection (`ofRaw`) is data-structure work; the literal→
/// text projection (`toString`) is the only string-builder site.
///
/// **Why this enables Tier-1 #1 (MERGE → ScriptDom MergeStatement):**
/// the MERGE typed AST needs typed VALUES tuples. `SqlLiteral` is the
/// IR-side input to `ScriptDomBuild.toScriptDomLiteral` (a future
/// SSDT-resident builder that maps `SqlLiteral` to ScriptDom's
/// `Literal` AST node — `IntegerLiteral`, `StringLiteral` with
/// `IsNational=true` for N'...', `NullLiteral`, etc.). Core stays
/// dep-light (no ScriptDom); the ScriptDom mapping lives in SSDT.
type SqlLiteral =
    /// SQL `NULL` — represents IR's `""` raw-form sentinel for
    /// absence (per `RawValueCodec` convention).
    | NullLit
    /// Integer literal — the raw-form digits, formatted bare.
    /// Maps to ScriptDom `IntegerLiteral`.
    | IntegerLit of digits: string
    /// Decimal/numeric literal — the raw-form digits with sign /
    /// decimal point. Maps to ScriptDom `NumericLiteral`.
    | DecimalLit of digits: string
    /// Boolean literal — the V2 IR's `true`/`false` parsed via
    /// `RawValueCodec.parseBoolean`; rendered as SQL `1`/`0` (the
    /// `BIT` literal convention). Maps to ScriptDom `IntegerLiteral`
    /// 1/0 (SQL Server has no native boolean literal).
    | BooleanLit of value: bool
    /// Text/Unicode-string literal — the raw value pre-escaping;
    /// rendered as `N'<escaped>'` (single-quote doubled). Maps to
    /// ScriptDom `StringLiteral` with `IsNational=true`.
    | TextLit of raw: string
    /// DateTime literal — the raw 7-digit form per `RawValueCodec
    /// .DateTimeFormat`. WP-17(d) (DECISIONS 2026-07-16): rendered as
    /// V1's explicit `CAST('<raw>' AS datetime2(7))`
    /// (`SqlLiteralFormatter.cs:90` parity) — precision-explicit and
    /// language-independent (`datetime2` parses the ISO form the same
    /// under any `SET DATEFORMAT`/`LANGUAGE`; the pre-WP-17 bare
    /// `'<raw>'` relied on implicit conversion). Maps to ScriptDom
    /// `CastCall`. The three temporal categories are distinct variants
    /// because each owns its CAST target type.
    | DateTimeLit of raw: string
    /// Date literal — the raw `yyyy-MM-dd` form per `RawValueCodec
    /// .DateFormat`. Rendered as `CAST('<raw>' AS date)` (V1 parity).
    | DateLit of raw: string
    /// Time literal — the raw TimeSpan `c` form per `RawValueCodec
    /// .TimeFormat`. Rendered as `CAST('<raw>' AS time(7))` (V1 parity).
    | TimeLit of raw: string
    /// Guid literal — the raw `D` form per `RawValueCodec.GuidFormat`
    /// (8-4-4-4-12 hyphenated). Rendered as `'<raw>'`. Maps to
    /// ScriptDom `StringLiteral` with `IsNational=false`.
    | GuidLit of raw: string
    /// Binary literal — the hex-prefixed form per `RawValueCodec
    /// .withHexPrefix` (e.g., `0xCAFEBABE`). Rendered as the prefixed
    /// hex bare (no quoting). Maps to ScriptDom `BinaryLiteral`.
    | BinaryLit of hexPrefixed: string

/// WP-17(e) (DECISIONS 2026-07-16) — one segment of a Text literal whose
/// raw value carries control characters. V1 escapes CR/LF/TAB into
/// `CHAR()` concatenation (`SqlLiteralFormatter.EscapeUnicodeString`)
/// so the emitted SQL contains no raw control bytes; V2's two terminal
/// planes (text `toString`, ScriptDom `buildSqlLiteral`) both compose
/// from this shared segmentation so they cannot drift. `TextRun` may be
/// empty (the V1-parity blind splice: a leading/trailing/adjacent
/// control char yields an empty `N''` run).
type TextLiteralSegment =
    | TextRun of string
    | ControlChar of code: int

[<RequireQualifiedAccess>]
module SqlLiteral =

    /// The named refusal code for an empty raw on a type with no
    /// empty-capable literal form (WP-3). `category.subject.problem`
    /// lower-dot convention; carried in the `FormatException` message
    /// (the NM-20 malformed-raw precedent — loud, named, never coerced).
    [<Literal>]
    let EmptyNotEmptyCapableCode : string = "rawValue.empty.notEmptyCapable"

    /// WP-17(e) — split a Text raw into literal runs and the control
    /// characters between them. Exactly V1's escape set (CR 13, LF 10,
    /// TAB 9 — `SqlLiteralFormatter.cs:58-61`); every other character
    /// stays inside its run. A control-char-free raw yields the single
    /// `[ TextRun raw ]` (the byte-identical default for both planes).
    let textLiteralSegments (raw: string) : TextLiteralSegment list =
        if raw.IndexOfAny [| '\r'; '\n'; '\t' |] < 0 then [ TextRun raw ]
        else
            let segments = System.Collections.Generic.List<TextLiteralSegment>()
            let run = System.Text.StringBuilder()
            let flush () =
                segments.Add(TextRun (run.ToString()))
                run.Clear() |> ignore
            for c in raw do
                match c with
                | '\r' -> flush (); segments.Add(ControlChar 13)
                | '\n' -> flush (); segments.Add(ControlChar 10)
                | '\t' -> flush (); segments.Add(ControlChar 9)
                | c    -> run.Append c |> ignore
            flush ()
            List.ofSeq segments

    /// Project an IR `(PrimitiveType, raw)` cell into a typed
    /// `SqlLiteral`. WP-3 (F11): NULL is carried OUT-OF-BAND as `None`
    /// — the retired NM-18 universal `""`-as-NULL sentinel is gone.
    /// `Some ""` is a faithful value only where the type has an empty
    /// form: Text (`N''`) and Binary (zero-length `0x`); on any other
    /// type an empty raw is malformed and refuses loudly (NM-20
    /// precedent). All canonical-form normalization (Boolean parse,
    /// hex prefix) flows through `RawValueCodec` so the V2 raw-form
    /// contract has a single source of truth across emit / parse /
    /// readback.
    let ofRaw (typ: PrimitiveType) (raw: string option) : SqlLiteral =
        match raw with
        | None -> NullLit
        | Some raw ->
            match typ with
            | Text -> TextLit raw
            | Binary -> BinaryLit (RawValueCodec.withHexPrefix raw)
            | _ when raw = "" ->
                raise (System.FormatException(
                    sprintf "%s: empty raw value for %A (only Text/Binary carry an empty form; NULL is None)"
                        EmptyNotEmptyCapableCode typ))
            | Integer -> IntegerLit raw
            | Decimal -> DecimalLit raw
            | Boolean -> BooleanLit (RawValueCodec.parseBoolean raw)
            | DateTime -> DateTimeLit raw
            | Date -> DateLit raw
            | Time -> TimeLit raw
            | Guid -> GuidLit raw

    /// Render a typed `SqlLiteral` as SQL text. The terminal boundary
    /// — strings emerge here, not earlier. SQL injection isn't a
    /// concern (the IR is the input contract; non-IR sources don't
    /// produce `SqlLiteral` values). Single-quote doubling for `Text`
    /// is the SQL-standard escape; other literal forms have no
    /// escapable characters in the IR's canonical raw-form contract.
    let toString (lit: SqlLiteral) : string =
        match lit with
        | NullLit            -> "NULL"
        | IntegerLit s       -> s
        | DecimalLit s       -> s
        | BooleanLit true    -> "1"
        | BooleanLit false   -> "0"
        // WP-17(d) — V1's explicit-CAST temporal forms (SqlLiteralFormatter.cs:90):
        // precision-explicit, language-independent. The raw carries no escapable
        // characters (RawValueCodec canonical forms).
        | DateTimeLit raw    -> System.String.Concat("CAST('", raw, "' AS datetime2(7))")  // LINT-ALLOW: terminal SQL temporal-literal text formatting; raw is from `RawValueCodec.DateTimeFormat` (typed canonical form, no escapable characters); BCL `String.Concat` IS the use-case-specific library at the absolute terminal SQL-text boundary
        | DateLit raw        -> System.String.Concat("CAST('", raw, "' AS date)")  // LINT-ALLOW: terminal SQL temporal-literal text formatting; raw is from `RawValueCodec.DateFormat`; same boundary as above
        | TimeLit raw        -> System.String.Concat("CAST('", raw, "' AS time(7))")  // LINT-ALLOW: terminal SQL temporal-literal text formatting; raw is from `RawValueCodec.TimeFormat`; same boundary as above
        | GuidLit raw        -> System.String.Concat("'", raw, "'")  // LINT-ALLOW: terminal SQL Guid-literal text formatting; raw is from `RawValueCodec.GuidFormat` (canonical D form, no escapable characters); BCL `String.Concat` IS the use-case-specific library at the absolute terminal SQL-text boundary
        | TextLit raw        ->
            // Single-quote doubling per the SQL-standard escape; `N`
            // prefix per the unicode-string-literal SQL convention.
            // WP-17(e): CR/LF/TAB splice into `CHAR()` concatenation
            // (V1 `EscapeUnicodeString` parity) — the emitted SQL
            // carries no raw control bytes; a control-char-free raw
            // renders byte-identically to the pre-WP-17 form.
            textLiteralSegments raw
            |> List.map (fun segment ->
                match segment with
                | TextRun run ->
                    System.String.Concat("N'", run.Replace("'", "''"), "'")  // LINT-ALLOW: terminal Unicode SQL string-literal text formatting; segments are typed (escaped from raw via single-quote-doubling, the SQL-standard escape); BCL `String.Concat` IS the use-case-specific library at the absolute terminal SQL-text boundary
                | ControlChar code ->
                    System.String.Concat("CHAR(", string code, ")"))  // LINT-ALLOW: terminal SQL CHAR() call formatting at the same boundary; code is one of the typed 13/10/9 set
            |> String.concat " + "  // LINT-ALLOW: terminal SQL concatenation-operator join over the typed segment renderings at the same boundary
        | BinaryLit prefixed -> prefixed

    /// Convenience: `ofRaw` then `toString`. The combined surface
    /// matches the legacy `Render.formatSqlLiteral` shape (typed
    /// `PrimitiveType -> raw cell -> string`) so consumers can migrate
    /// without restructuring their call sites.
    let formatRaw (typ: PrimitiveType) (raw: string option) : string =
        ofRaw typ raw |> toString
