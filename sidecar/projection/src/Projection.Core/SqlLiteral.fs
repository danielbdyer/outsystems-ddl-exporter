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
    /// Temporal literal (DateTime / Date / Time) — the raw ISO-8601
    /// form per `RawValueCodec.DateTimeFormat` / `DateFormat` /
    /// `TimeFormat`. Rendered as `'<raw>'`. Maps to ScriptDom
    /// `StringLiteral` with `IsNational=false` (SQL Server temporal
    /// literals are non-national strings).
    | TemporalLit of raw: string
    /// Guid literal — the raw `D` form per `RawValueCodec.GuidFormat`
    /// (8-4-4-4-12 hyphenated). Rendered as `'<raw>'`. Maps to
    /// ScriptDom `StringLiteral` with `IsNational=false`.
    | GuidLit of raw: string
    /// Binary literal — the hex-prefixed form per `RawValueCodec
    /// .withHexPrefix` (e.g., `0xCAFEBABE`). Rendered as the prefixed
    /// hex bare (no quoting). Maps to ScriptDom `BinaryLiteral`.
    | BinaryLit of hexPrefixed: string

[<RequireQualifiedAccess>]
module SqlLiteral =

    /// The named refusal code for an empty raw on a type with no
    /// empty-capable literal form (WP-3). `category.subject.problem`
    /// lower-dot convention; carried in the `FormatException` message
    /// (the NM-20 malformed-raw precedent — loud, named, never coerced).
    [<Literal>]
    let EmptyNotEmptyCapableCode : string = "rawValue.empty.notEmptyCapable"

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
            | DateTime | Date | Time -> TemporalLit raw
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
        | TemporalLit raw    -> System.String.Concat("'", raw, "'")  // LINT-ALLOW: terminal SQL temporal-literal text formatting; raw is from `RawValueCodec.DateTimeFormat` / `DateFormat` / `TimeFormat` (typed canonical form, no escapable characters); BCL `String.Concat` IS the use-case-specific library at the absolute terminal SQL-text boundary
        | GuidLit raw        -> System.String.Concat("'", raw, "'")  // LINT-ALLOW: terminal SQL Guid-literal text formatting; raw is from `RawValueCodec.GuidFormat` (canonical D form, no escapable characters); BCL `String.Concat` IS the use-case-specific library at the absolute terminal SQL-text boundary
        | TextLit raw        ->
            // Single-quote doubling per the SQL-standard escape; `N`
            // prefix per the unicode-string-literal SQL convention.
            let escaped = raw.Replace("'", "''")
            System.String.Concat("N'", escaped, "'")  // LINT-ALLOW: terminal Unicode SQL string-literal text formatting; segments are typed (escaped from raw via single-quote-doubling, the SQL-standard escape); BCL `String.Concat` IS the use-case-specific library at the absolute terminal SQL-text boundary
        | BinaryLit prefixed -> prefixed

    /// Convenience: `ofRaw` then `toString`. The combined surface
    /// matches the legacy `Render.formatSqlLiteral` shape (typed
    /// `PrimitiveType -> raw cell -> string`) so consumers can migrate
    /// without restructuring their call sites.
    let formatRaw (typ: PrimitiveType) (raw: string option) : string =
        ofRaw typ raw |> toString
