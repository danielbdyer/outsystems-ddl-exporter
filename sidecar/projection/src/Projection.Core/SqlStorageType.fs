namespace Projection.Core

/// Length facet for variable-width SQL Server types (`NVARCHAR`,
/// `VARCHAR`, `NCHAR`, `CHAR`, `VARBINARY`, `BINARY`). `Max` is SQL
/// Server's `(MAX)` open-ended marker; `Bounded n` is an explicit
/// character / byte width.
type SqlLength =
    | Bounded of int
    | Max

/// Concrete SQL Server storage type — the evidence-bearing realization
/// of *what physically lands in the DDL*. Distinct from `PrimitiveType`,
/// which is the semantic OutSystems-domain category (A13). The two
/// concepts diverge wherever the semantic vocabulary is coarser than
/// the SQL Server emission vocabulary:
///
///   - `PrimitiveType.Integer` collapses OutSystems `Integer` and
///     `LongInteger` to one variant; SQL emission must distinguish
///     `INT` from `BIGINT`.
///   - `PrimitiveType.DateTime` collapses `DateTime` / `DateTime2` /
///     `SmallDateTime` / `DateTimeOffset`; SQL emission must keep them
///     apart (a `datetime` column is not a `datetime2` column).
///   - `Text` / `Binary` length and the `(MAX)` marker must survive
///     the round-trip (`NVARCHAR(100)` ≠ `NVARCHAR(MAX)`).
///   - Less-common SQL Server types (`MONEY`, `XML`, `IMAGE`,
///     `FLOAT`) have no dedicated `PrimitiveType` variant but are
///     legitimate OutSystems attribute realizations.
///
/// When source evidence names the concrete type — the OSSYS adapter
/// reading `ossys_EntityAttr.Type` (`rtLongInteger`, `rtDateTime`,
/// ...), or an `external_dbType` override — the carrier is `Some`;
/// emitters prefer it. When evidence carries only the semantic
/// category (test fixtures; `ReadSide`'s structural reflection), the
/// carrier is `None` and emitters fall back to the canonical
/// `PrimitiveType` mapping. Both halves of an attribute's typing stay
/// consistent: `SqlStorageType.toPrimitiveType` projects any storage
/// type back to its semantic category, so the `SqlStorage` evidence an
/// adapter sets never disagrees with the `Type` it sets.
[<RequireQualifiedAccess>]
type SqlStorageType =
    | BigInt
    | Int
    | SmallInt
    | TinyInt
    | Bit
    | Decimal of precision: int * scale: int
    | Numeric of precision: int * scale: int
    | Money
    | SmallMoney
    | Float
    | Real
    | NVarChar of length: SqlLength
    | VarChar of length: SqlLength
    | NChar of length: int
    | Char of length: int
    | NText
    | Text
    | DateTime
    | DateTime2 of scale: int option
    | DateTimeOffset of scale: int option
    | SmallDateTime
    | Date
    | Time of scale: int option
    | VarBinary of length: SqlLength
    | Binary of length: int
    | Image
    | UniqueIdentifier
    | Xml

[<RequireQualifiedAccess>]
module SqlStorageType =

    /// Project a concrete storage type back to its semantic
    /// `PrimitiveType` category. The inverse of "narrow a semantic
    /// category to a concrete realization"; used as the consistency
    /// witness that an adapter's `(Type, SqlStorage)` pair agrees
    /// (`toPrimitiveType storage = Type`). Closed-DU dispatch — adding
    /// a storage variant lights up an exhaustiveness error here.
    let toPrimitiveType (st: SqlStorageType) : PrimitiveType =
        match st with
        | SqlStorageType.BigInt
        | SqlStorageType.Int
        | SqlStorageType.SmallInt
        | SqlStorageType.TinyInt -> Integer
        | SqlStorageType.Decimal _
        | SqlStorageType.Numeric _
        | SqlStorageType.Money
        | SqlStorageType.SmallMoney
        | SqlStorageType.Float
        | SqlStorageType.Real -> Decimal
        | SqlStorageType.Bit -> Boolean
        | SqlStorageType.NVarChar _
        | SqlStorageType.VarChar _
        | SqlStorageType.NChar _
        | SqlStorageType.Char _
        | SqlStorageType.NText
        | SqlStorageType.Text
        | SqlStorageType.Xml -> Text
        | SqlStorageType.DateTime
        | SqlStorageType.DateTime2 _
        | SqlStorageType.DateTimeOffset _
        | SqlStorageType.SmallDateTime -> DateTime
        | SqlStorageType.Date -> Date
        | SqlStorageType.Time _ -> Time
        | SqlStorageType.VarBinary _
        | SqlStorageType.Binary _
        | SqlStorageType.Image -> Binary
        | SqlStorageType.UniqueIdentifier -> Guid

    /// The canonical concrete realization a bare `PrimitiveType`
    /// implies when no source evidence narrows it further. This is the
    /// semantic fallback emitters use today via the `PrimitiveType` →
    /// `SqlDataTypeOption` table; naming it as a `SqlStorageType` keeps
    /// the fallback equivalence checkable (`toPrimitiveType
    /// (ofPrimitiveType pt) = pt`). The values mirror the existing
    /// `SqlTypeCorrespondence.baseName` / `ScriptDomBuild` defaults:
    /// `Integer → INT`, `DateTime → DATETIME2`, `Text → NVARCHAR(MAX)`.
    let ofPrimitiveType (pt: PrimitiveType) : SqlStorageType =
        match pt with
        | Integer  -> SqlStorageType.Int
        | Decimal  -> SqlStorageType.Decimal (18, 4)
        | Text     -> SqlStorageType.NVarChar Max
        | Boolean  -> SqlStorageType.Bit
        | DateTime -> SqlStorageType.DateTime2 None
        | Date     -> SqlStorageType.Date
        | Time     -> SqlStorageType.Time None
        | Binary   -> SqlStorageType.VarBinary Max
        | Guid     -> SqlStorageType.UniqueIdentifier

    /// Parse a SQL Server type expression into a concrete storage type.
    /// Two evidence shapes converge here:
    ///   - inline-parenthesized strings (`external_dbType` overrides
    ///     such as `"NVARCHAR(MAX)"`, `"DECIMAL(18,2)"`, `"BIGINT"`);
    ///   - bare base names with facets supplied separately (the
    ///     `INFORMATION_SCHEMA.COLUMNS` shape: `DATA_TYPE = "nvarchar"`
    ///     plus `CHARACTER_MAXIMUM_LENGTH` / `NUMERIC_PRECISION` /
    ///     `NUMERIC_SCALE`).
    ///
    /// Parenthesized parameters take precedence; the `length` /
    /// `precision` / `scale` arguments fill in when the string carries
    /// no parens. A `length` of `-1` is SQL Server's `MAX` sentinel.
    /// Returns `None` for an unrecognized base name — the caller keeps
    /// the semantic fallback rather than emitting an unsupported type.
    let ofSqlType
        (dataType: string)
        (length: int option)
        (precision: int option)
        (scale: int option)
        : SqlStorageType option =
        if System.String.IsNullOrWhiteSpace dataType then None
        else
            let trimmed = dataType.Trim()
            let parenIdx = trimmed.IndexOf('(')
            let baseName, parenParts =
                if parenIdx >= 0 && trimmed.EndsWith(")") then
                    let inner = trimmed.Substring(parenIdx + 1, trimmed.Length - parenIdx - 2)
                    trimmed.Substring(0, parenIdx).Trim().ToLowerInvariant(),
                    inner.Split(',') |> Array.map (fun p -> p.Trim())
                else
                    trimmed.ToLowerInvariant(), [||]
            let parenInt (idx: int) : int option =
                if idx < parenParts.Length then
                    match System.Int32.TryParse parenParts.[idx] with
                    | true, n -> Some n
                    | _ -> None
                else None
            let parenIsMax () : bool =
                parenParts.Length = 1
                && System.String.Equals(parenParts.[0], "max", System.StringComparison.OrdinalIgnoreCase)
            // Variable-length resolution: explicit `(MAX)` → Max;
            // explicit `(n)` or facet column → Bounded n; `-1` sentinel
            // or absent → Max.
            let resolveLength () : SqlLength =
                if parenIsMax () then Max
                else
                    match parenInt 0 |> Option.orElse length with
                    | Some n when n > 0 -> Bounded n
                    | _ -> Max
            // Fixed-length resolution (NCHAR / CHAR / BINARY): explicit
            // width or default 1 (SQL Server's CHAR/BINARY default).
            let resolveFixed () : int =
                match parenInt 0 |> Option.orElse length with
                | Some n when n > 0 -> n
                | _ -> 1
            let resolvePrecisionScale () : int * int =
                (parenInt 0 |> Option.orElse precision |> Option.defaultValue 18),
                (parenInt 1 |> Option.orElse scale |> Option.defaultValue 0)
            let resolveScale () : int option =
                parenInt 0 |> Option.orElse scale
            match baseName with
            | "bigint"           -> Some SqlStorageType.BigInt
            | "int"              -> Some SqlStorageType.Int
            | "smallint"         -> Some SqlStorageType.SmallInt
            | "tinyint"          -> Some SqlStorageType.TinyInt
            | "bit"              -> Some SqlStorageType.Bit
            | "decimal"          -> Some (SqlStorageType.Decimal (resolvePrecisionScale ()))
            | "numeric"          -> Some (SqlStorageType.Numeric (resolvePrecisionScale ()))
            | "money"            -> Some SqlStorageType.Money
            | "smallmoney"       -> Some SqlStorageType.SmallMoney
            | "float"            -> Some SqlStorageType.Float
            | "real"             -> Some SqlStorageType.Real
            | "nvarchar"         -> Some (SqlStorageType.NVarChar (resolveLength ()))
            | "varchar"          -> Some (SqlStorageType.VarChar (resolveLength ()))
            | "nchar"            -> Some (SqlStorageType.NChar (resolveFixed ()))
            | "char"             -> Some (SqlStorageType.Char (resolveFixed ()))
            | "ntext"            -> Some SqlStorageType.NText
            | "text"             -> Some SqlStorageType.Text
            | "datetime"         -> Some SqlStorageType.DateTime
            | "datetime2"        -> Some (SqlStorageType.DateTime2 (resolveScale ()))
            | "datetimeoffset"   -> Some (SqlStorageType.DateTimeOffset (resolveScale ()))
            | "smalldatetime"    -> Some SqlStorageType.SmallDateTime
            | "date"             -> Some SqlStorageType.Date
            | "time"             -> Some (SqlStorageType.Time (resolveScale ()))
            | "varbinary"        -> Some (SqlStorageType.VarBinary (resolveLength ()))
            | "binary"           -> Some (SqlStorageType.Binary (resolveFixed ()))
            | "image"            -> Some SqlStorageType.Image
            | "uniqueidentifier" -> Some SqlStorageType.UniqueIdentifier
            | "xml"              -> Some SqlStorageType.Xml
            | _                  -> None
