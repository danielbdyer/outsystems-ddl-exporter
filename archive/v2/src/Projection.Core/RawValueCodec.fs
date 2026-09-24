namespace Projection.Core

/// Canonical raw-value codec for V2's IR `string`-form scalar values.
/// Three consumers (chapter-3.6 cash-out of audit Top-10 #9 — "unify
/// datetime/guid parsing across dual sites" — reached three-consumer
/// pressure):
///
///   1. **`Projection.Targets.SSDT.Render.formatSqlLiteral`** — emits
///      a SQL literal from the raw form (CREATE TABLE seed-row
///      INSERT statements).
///   2. **`Projection.Pipeline.Bulk.parseRaw`** — parses the raw
///      form into the CLR object that `SqlBulkCopy` expects.
///   3. **`Projection.Adapters.Sql.ReadSide.formatRawValue`** — emits
///      the raw form from a CLR object read back from SQL Server.
///
/// All three converge here. Format strings are `[<Literal>]`
/// constants so any change has a single edit site; the canonical
/// formatter / parser helpers preserve the V2 round-trip property
/// `parse (format value) = value` for every PrimitiveType variant
/// (modulo culture-invariance + canonical hex-prefix + canonical
/// boolean spelling).
///
/// **The contract is bidirectional and round-trip-stable.** Adding
/// a variant to `PrimitiveType` requires extending this module with
/// the canonical format string AND adding the corresponding case
/// across the three consumers — the closed-DU expansion empirical
/// test catches incomplete updates at the consumers, but the
/// **codification of the format convention lives here**.
[<RequireQualifiedAccess>]
module RawValueCodec =

    open System
    open System.Globalization

    // ---------------------------------------------------------------------
    // Canonical format strings — the V2 raw-form invariants.
    // ---------------------------------------------------------------------

    /// `DateTime` raw form: ISO-8601-like with tick precision, no
    /// timezone (V2 IR is timezone-agnostic; the boundary adapter
    /// normalizes to UTC before producing the raw).
    [<Literal>]
    let DateTimeFormat : string = "yyyy-MM-dd HH:mm:ss.fffffff"

    /// `Date` raw form: ISO-8601 date, no time component.
    [<Literal>]
    let DateFormat : string = "yyyy-MM-dd"

    /// WP-17(b) (DECISIONS 2026-07-16) — the OFFSET-BEARING datetime raw
    /// form for `datetimeoffset` columns (DBA/External-Entity only; no
    /// OutSystems native type produces them). `zzz` renders the signed
    /// offset (`+03:00`); the space-separated shape matches V1's
    /// test-witnessed `CAST('… -03:00' AS datetimeoffset(7))` literal
    /// input and SQL Server's datetimeoffset string parsing. DISJOINT
    /// from `DateTimeFormat` by construction (that form never carries an
    /// offset suffix) — `hasUtcOffset` is the shape detector the parse /
    /// literal planes dispatch on.
    [<Literal>]
    let DateTimeOffsetFormat : string = "yyyy-MM-dd HH:mm:ss.fffffff zzz"

    /// `Time` raw form: BCL's canonical `TimeSpan` "constant" format
    /// (`"c"`) — round-trippable via `TimeSpan.Parse`.
    [<Literal>]
    let TimeFormat : string = "c"

    /// `Guid` raw form: BCL's canonical `"D"` format (8-4-4-4-12
    /// hyphenated). `Guid.Parse` accepts any input form; we emit `D`
    /// for stability across the round-trip.
    [<Literal>]
    let GuidFormat : string = "D"

    /// SQL Server hex-literal prefix. Canonical for the wire form;
    /// the parser strips the prefix (case-insensitive) before
    /// `Convert.FromHexString`. Matched by `Render.formatSqlLiteral`
    /// (Binary case) and `Bulk.parseRaw` (Binary case).
    [<Literal>]
    let HexLiteralPrefix : string = "0x"

    /// `Boolean` raw form: spelled `"true"` / `"false"` per V2 IR
    /// canonical. The parser additionally accepts `"1"` / `"0"`
    /// (case-insensitive) for V1-bridge tolerance.
    [<Literal>]
    let BooleanTrue : string = "true"

    [<Literal>]
    let BooleanFalse : string = "false"

    // ---------------------------------------------------------------------
    // Boolean parse / format. Canonical form is `"true"`/`"false"`;
    // the parser additionally accepts `"1"`/`"0"` for V1 source data
    // tolerance (`Bulk.parseRaw` previously hard-coded the predicate).
    // ---------------------------------------------------------------------

    /// NM-20 — the named refusal code for an unrecognized Boolean raw.
    /// `category.subject.problem` lower-dot convention (mirrors
    /// `ValidationError.Code`). Carried in the `FormatException` message
    /// so the fail-loud refusal is *named*, not a silent default.
    [<Literal>]
    let BooleanUnrecognizedCode : string = "rawValue.boolean.unrecognized"

    /// Parse a Boolean raw value. Accepts the canonical V2 form
    /// (`"true"` / `"false"` case-insensitive) and the V1 numeric
    /// form (`"1"` / `"0"`).
    ///
    /// **NM-20 — fails loud on garbage, like every sibling parser.**
    /// `DateTime.ParseExact` / `Guid.Parse` / `Int64.Parse` (the other
    /// `Bulk.parseRaw` arms) all throw a `FormatException` on a malformed
    /// raw; Boolean previously *silently coerced* any unrecognized input
    /// (`"2"`, `"yes"`, `"tru"`) to `false`, making a real BIT divergence
    /// indistinguishable from a legitimate `false`. It now raises a
    /// `FormatException` carrying the named refusal code
    /// `rawValue.boolean.unrecognized` — the malformed-raw hard-fail is the
    /// same loud failure the siblings already use, now NAMED rather than
    /// swallowed. (A `Result` return was rejected: both callers —
    /// `SqlLiteral.ofRaw` and `Bulk.parseRaw` — are total IR projections
    /// whose siblings throw, so a `Result` would ripple the loud-fail
    /// discipline they already rely on; a `ToleratedDivergence` was rejected
    /// because the audit names this a divergence to *surface*, not a
    /// coercion to *accept* — and that DU is the canary's round-trip
    /// equivalence surface, not a place for a parse refusal.)
    let parseBoolean (raw: string) : bool =
        match raw.ToLowerInvariant() with
        | "true" | "1"  -> true
        | "false" | "0" -> false
        | _ ->
            raise (
                System.FormatException(
                    String.concat "" [  // LINT-ALLOW: terminal raw-value codec string composition; the codec output IS a string primitive
                        BooleanUnrecognizedCode
                        ": unrecognized Boolean raw value '"
                        raw
                        "' (expected 'true'/'false'/'1'/'0')" ]))

    /// Format a Boolean as the canonical V2 raw form.
    let formatBoolean (value: bool) : string =
        if value then BooleanTrue else BooleanFalse

    // ---------------------------------------------------------------------
    // Hex-prefix utilities — the SQL-literal binary boundary. Both
    // emitter and parser use these so the prefix-handling rule lives
    // in exactly one place.
    // ---------------------------------------------------------------------

    /// True when `raw` begins with the canonical hex-literal prefix
    /// (case-insensitive).
    let hasHexPrefix (raw: string) : bool =
        raw.StartsWith(HexLiteralPrefix, StringComparison.OrdinalIgnoreCase)

    /// Strip the hex prefix if present (case-insensitive); pass-through
    /// otherwise. Used by `Bulk.parseRaw` before `Convert.FromHexString`.
    let stripHexPrefix (raw: string) : string =
        if hasHexPrefix raw then raw.Substring(HexLiteralPrefix.Length) else raw

    /// Prepend the canonical hex prefix if absent. Idempotent. Used
    /// by `Render.formatSqlLiteral` to emit binary literals.
    let withHexPrefix (raw: string) : string =
        if hasHexPrefix raw then raw
        else HexLiteralPrefix + raw  // LINT-ALLOW: terminal text-emission boundary; HexLiteralPrefix is the canonical typed segment, raw is already vetted hex

    // ---------------------------------------------------------------------
    // DateTime / Date / Time / Guid format helpers — invariant-culture
    // parsing/formatting against the canonical format strings.
    // ---------------------------------------------------------------------

    /// Format a DateTime as the canonical V2 raw form.
    let formatDateTime (value: DateTime) : string =
        value.ToString(DateTimeFormat, CultureInfo.InvariantCulture)

    /// WP-17(b) — format a DateTimeOffset as the canonical OFFSET-BEARING
    /// raw form (`DateTimeOffsetFormat`; the offset is PRESERVED, never
    /// normalized to UTC — faithful carriage of what the column stores).
    let formatDateTimeOffset (value: DateTimeOffset) : string =
        value.ToString(DateTimeOffsetFormat, CultureInfo.InvariantCulture)

    /// WP-17(b) — the offset-shape detector the parse / literal planes
    /// dispatch on: an offset-bearing raw ends `±HH:mm` (the `zzz`
    /// rendering — exactly six chars, sign + two digits + colon + two
    /// digits). `DateTimeFormat` raws end in fractional-second digits,
    /// so the two shapes are disjoint by construction.
    let hasUtcOffset (raw: string) : bool =
        raw.Length > 6
        && (let sign = raw.[raw.Length - 6]
            (sign = '+' || sign = '-') && raw.[raw.Length - 3] = ':')

    /// WP-17(b) — parse the canonical offset-bearing raw back to a
    /// `DateTimeOffset` (the exact inverse of `formatDateTimeOffset`).
    /// Fails loud on malformed input (the NM-20 sibling-parser shape).
    let parseDateTimeOffset (raw: string) : DateTimeOffset =
        DateTimeOffset.ParseExact(raw, DateTimeOffsetFormat, CultureInfo.InvariantCulture)

    /// Format a Date-only DateTime as the canonical V2 raw form.
    let formatDate (value: DateTime) : string =
        value.ToString(DateFormat, CultureInfo.InvariantCulture)

    /// Format a TimeSpan as the canonical V2 raw form.
    let formatTime (value: TimeSpan) : string =
        value.ToString(TimeFormat, CultureInfo.InvariantCulture)

    /// Format a Guid as the canonical V2 raw form.
    let formatGuid (value: Guid) : string =
        value.ToString(GuidFormat)
