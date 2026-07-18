namespace Projection.Core
// LINT-ALLOW-FILE: the only non-typed primitive here is `sprintf` composing the
//   authored-default parse-failure PROSE ("'<raw>' does not parse as an <type>
//   value") — human-readable diagnostic text, the discipline's named allowed
//   exception (same as the pass drivers). No typed AST produces equivalent
//   prose; the interpolated value is an already-refused raw literal. The literal
//   emission itself is typed-AST (ScriptDom); this marker covers the prose only.

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
    /// WP-17(b) (DECISIONS 2026-07-16) — OFFSET-BEARING datetime literal
    /// for `datetimeoffset` columns (DBA/External only). The raw carries
    /// the signed offset (`RawValueCodec.DateTimeOffsetFormat`);
    /// rendered as `CAST('<raw>' AS datetimeoffset(7))` (V1's
    /// test-witnessed form) — casting an offset-bearing string to
    /// `datetime2` refuses on SQL Server, so the offset shape MUST own
    /// its CAST target. `ofRaw` dispatches on the raw shape
    /// (`RawValueCodec.hasUtcOffset`): the semantic category stays the
    /// 9-way `DateTime`; only the literal realization is offset-aware.
    | DateTimeOffsetLit of raw: string
    /// Guid literal — the raw `D` form per `RawValueCodec.GuidFormat`
    /// (8-4-4-4-12 hyphenated). Rendered as `'<raw>'`. Maps to
    /// ScriptDom `StringLiteral` with `IsNational=false`.
    | GuidLit of raw: string
    /// Binary literal — the hex-prefixed form per `RawValueCodec
    /// .withHexPrefix` (e.g., `0xCAFEBABE`). Rendered as the prefixed
    /// hex bare (no quoting). Maps to ScriptDom `BinaryLiteral`.
    | BinaryLit of hexPrefixed: string
    /// Niladic function-call default expression (`getutcdate()`,
    /// `newid()`, `sysutcdatetime()`) — the DEFAULT constraint's payload
    /// when the authored default is a call, not a value (DECISIONS
    /// 2026-07-18; the #669 M-1 finding: lifting the call as a value
    /// rendered a quoted string that deployed but failed at first
    /// insert). Produced ONLY by `ofAuthoredDefault` — the data plane's
    /// `ofRaw` never yields it, because rows carry values. Rendered
    /// bare; maps to ScriptDom `FunctionCall`.
    | ExpressionLit of call: string

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

    /// The reason an authored DEFAULT literal is NOT a parseable value of
    /// its own type — `None` when it parses (or is a form with no value to
    /// validate: Text / Boolean / Binary). A `Some reason` literal deploys as
    /// a DEFAULT and then fails at the first insert that relies on it (#669
    /// M-1). The check is SQL-shaped (invariant `TryParse`), not
    /// canonical-format-strict, so a legitimate authored `2024-01-01` passes.
    /// The ONE predicate shared by the board's `EmissionAuthoredDefault`
    /// finding and the emitter's `AuthoredDefaultRefused` refusal, so a red
    /// board line and a refused publish are the same fact (the
    /// downgrades-never-silent law; DECISIONS 2026-07-18).
    let unparsableValueReason (lit: SqlLiteral) : string option =
        let inv = System.Globalization.CultureInfo.InvariantCulture
        match lit with
        | IntegerLit raw when not (fst (System.Int64.TryParse(raw, System.Globalization.NumberStyles.Integer, inv))) ->
            Some (sprintf "'%s' does not parse as an integer value" raw)
        | DecimalLit raw when not (fst (System.Decimal.TryParse(raw, System.Globalization.NumberStyles.Number, inv))) ->
            Some (sprintf "'%s' does not parse as a decimal value" raw)
        | DateTimeLit raw when not (fst (System.DateTime.TryParse(raw, inv, System.Globalization.DateTimeStyles.None))) ->
            Some (sprintf "'%s' does not parse as a date-time value" raw)
        | DateLit raw when not (fst (System.DateTime.TryParse(raw, inv, System.Globalization.DateTimeStyles.None))) ->
            Some (sprintf "'%s' does not parse as a date value" raw)
        | TimeLit raw when not (fst (System.TimeSpan.TryParse(raw, inv))) ->
            Some (sprintf "'%s' does not parse as a time value" raw)
        | DateTimeOffsetLit raw when not (fst (System.DateTimeOffset.TryParse(raw, inv, System.Globalization.DateTimeStyles.None))) ->
            Some (sprintf "'%s' does not parse as an offset-bearing date-time value" raw)
        | GuidLit raw when not (fst (System.Guid.TryParse raw)) ->
            Some (sprintf "'%s' does not parse as a GUID value" raw)
        | _ -> None

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
            // WP-17(b) — an offset-bearing raw (a `datetimeoffset`
            // column's faithful carriage) owns its own CAST target;
            // the offset-less canonical form stays `datetime2(7)`.
            | DateTime when RawValueCodec.hasUtcOffset raw -> DateTimeOffsetLit raw
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
        | DateTimeOffsetLit raw -> System.String.Concat("CAST('", raw, "' AS datetimeoffset(7))")  // LINT-ALLOW: terminal SQL temporal-literal text formatting; raw is from `RawValueCodec.DateTimeOffsetFormat`; same boundary as above
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
        | ExpressionLit call -> call

    /// Convenience: `ofRaw` then `toString`. The combined surface
    /// matches the legacy `Render.formatSqlLiteral` shape (typed
    /// `PrimitiveType -> raw cell -> string`) so consumers can migrate
    /// without restructuring their call sites.
    let formatRaw (typ: PrimitiveType) (raw: string option) : string =
        ofRaw typ raw |> toString

    /// True when a trimmed authored default is a niladic function call —
    /// `name()` with an identifier-shaped name (`getutcdate()`, `newid()`,
    /// `sysutcdatetime()`). Argument-bearing calls stay out: the authored
    /// channel (Service Studio) cannot produce them, and treating free
    /// text with parentheses as SQL would corrupt text defaults.
    let private isNiladicCall (trimmed: string) : bool =
        trimmed.Length > 2
        && trimmed.EndsWith "()"
        && (System.Char.IsLetter trimmed.[0] || trimmed.[0] = '_')
        && trimmed.Substring(0, trimmed.Length - 2)
           |> Seq.forall (fun c -> System.Char.IsLetterOrDigit c || c = '_' || c = '$')

    /// Classify an AUTHORED default (`ossys_Entity_Attr.Default_Value` /
    /// the JSON `default` field) into its typed literal (DECISIONS
    /// 2026-07-18; the #669 M-1 finding). The authored channel carries
    /// three shapes the value lift (`ofRaw`) cannot discriminate:
    ///   - a niladic function call (`getutcdate()`) — an EXPRESSION;
    ///     lifted as a value it rendered `CAST('getutcdate()' …)`, which
    ///     deployed and then failed at first insert;
    ///   - a SQL-quoted text form (`''`, `'Draft'`) — the VALUE inside
    ///     the quotes; lifted verbatim the quotes doubled (`N''''''`);
    ///   - the bare value forms `ofRaw` already carries faithfully.
    /// An absent or whitespace-only raw carries nothing — a nullable
    /// column's implicit NULL is normal SQL behavior, not a configured
    /// default.
    let ofAuthoredDefault (typ: PrimitiveType) (raw: string) : SqlLiteral option =
        let trimmed = raw.Trim()
        if trimmed = "" then None
        elif isNiladicCall trimmed then Some (ExpressionLit trimmed)
        elif typ = Text
             && trimmed.Length >= 2
             && trimmed.StartsWith "'" && trimmed.EndsWith "'" then
            // The SQL-quoted authored form: unwrap the outer quotes and
            // undo the standard single-quote doubling. `''` is the
            // authored EMPTY STRING (`DEFAULT N''`), the estate's most
            // common authored default.
            let inner = trimmed.Substring(1, trimmed.Length - 2).Replace("''", "'")
            Some (TextLit inner)
        else Some (ofRaw typ (Some raw))
