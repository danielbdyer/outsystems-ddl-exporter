namespace Projection.Core

/// The OutSystems(OSSYS) → V2 type correspondence (recon #10). These are
/// *decisions*, not translations — the 2000-char `(MAX)` threshold, the
/// `currency → DECIMAL(37,8)` choice, the platform-native `email`/`phone`
/// `NVARCHAR(250)/(20)` (WP-4), the `longinteger → BIGINT` / `datetime → DATETIME` (legacy, not
/// `DATETIME2`) collapses — so they belong in Core next to `SqlTypeCorrespondence`,
/// where they are property-testable WITHOUT an OSSYS fixture and reusable by a
/// second source adapter (the rowset reader, a future XML reader). The adapter's
/// job shrinks to raw-string hygiene (`normalizeAttributeType`) + naming the
/// refusal; the *classifier* lives here.
///
/// `tryParse` is total and pure: `Some (primitive, storage)` for a mapped type,
/// `None` for an unmapped one — the adapter turns `None` into its own
/// `adapter.osm.unmappedDataType` refusal (the error vocabulary stays at the
/// boundary; the mapping data stays in Core).
[<RequireQualifiedAccess>]
module OssysTypeMapping =

    /// `Text` / `VarChar` width: OutSystems treats a declared length at or above
    /// the unicode-text threshold (V1's `maxLengthThreshold = 2000`) as open-ended
    /// `(MAX)`; a positive sub-threshold length is `Bounded`; absence is `(MAX)`.
    let textLength (length: int option) : SqlLength =
        match length with
        | Some n when n >= 2000 -> Max
        | Some n when n > 0     -> Bounded n
        | _                     -> Max

    /// A positive declared length wins (`Bounded`); otherwise the supplied
    /// fallback width applies (the V1-parity imposition or `(MAX)`).
    let boundedOr (fallback: SqlLength) (length: int option) : SqlLength =
        match length with
        | Some n when n > 0 -> Bounded n
        | _                 -> fallback

    /// Resolve an OSSYS attribute's semantic category AND concrete SQL Server
    /// storage type from the NORMALIZED type string plus declared
    /// length / precision / scale. V1-derived from `config/type-mapping.default.json`
    /// (DECISIONS 2026-05-15 — OSSYS adapter translation rules):
    ///   - `longinteger` → `BIGINT` (the `Integer` category collapses int/bigint;
    ///     the concrete storage keeps them apart);
    ///   - `datetime` → `DATETIME` (legacy; `rtDateTime2` is the path to `DATETIME2`).
    /// `None` ⇒ the normalized type has no V2 mapping yet (the adapter names that
    /// refusal). Reference attributes (`entityreference`, the structural
    /// `bt<EspaceSsKey>*<EntitySsKey>` binding-type encoding) store the target
    /// entity's identifier — Long Integer (`BIGINT`) by OutSystems convention.
    let tryParse
        (normalizedType: string)
        (length: int option)
        (precision: int option)
        (scale: int option)
        : (PrimitiveType * SqlStorageType) option =
        match normalizedType with
        | "identifier"     -> Some (Integer, SqlStorageType.BigInt)
        | "autonumber"     -> Some (Integer, SqlStorageType.BigInt)
        | "integer"        -> Some (Integer, SqlStorageType.Int)
        | "longinteger"    -> Some (Integer, SqlStorageType.BigInt)
        | "boolean"        -> Some (Boolean, SqlStorageType.Bit)
        | "datetime"       -> Some (DateTime, SqlStorageType.DateTime)
        | "datetime2"      -> Some (DateTime, SqlStorageType.DateTime2 (Some 7))
        | "datetimeoffset" -> Some (DateTime, SqlStorageType.DateTimeOffset (Some 7))
        | "date"           -> Some (Date, SqlStorageType.Date)
        | "time"           -> Some (Time, SqlStorageType.Time (Some 7))
        | "decimal"        ->
            Some
                (Decimal,
                 SqlStorageType.Decimal
                    (Option.defaultValue 18 precision, Option.defaultValue 0 scale))
        | "currency"       -> Some (Decimal, SqlStorageType.Decimal (37, 8))
        | "double" | "float" -> Some (Decimal, SqlStorageType.Float)
        | "real"           -> Some (Decimal, SqlStorageType.Real)
        | "binarydata" | "longbinarydata" ->
            Some (Binary, SqlStorageType.VarBinary Max)
        | "binary"         -> Some (Binary, SqlStorageType.VarBinary (boundedOr Max length))
        | "varbinary"      -> Some (Binary, SqlStorageType.VarBinary (boundedOr Max length))
        | "image"          -> Some (Binary, SqlStorageType.Image)
        | "longtext"       -> Some (Text, SqlStorageType.NVarChar Max)
        | "text"           -> Some (Text, SqlStorageType.NVarChar (textLength length))
        // WP-4 (DECISIONS 2026-07-16; register C3). `email`/`phone` map to
        // NVARCHAR — the PLATFORM-NATIVE storage (modeled `ossys_User.EMAIL` is
        // on-disk `nvarchar(250)`; the handbook's guidance is "Email, Phone
        // Number → NVARCHAR(n)"; `notes/note14.md` names "Using VARCHAR for
        // User Text" an anti-pattern). The prior ANSI `VARCHAR` was an imposed
        // V1-parity inference that left two ANSI islands in an otherwise
        // NVARCHAR schema (collation/codepage sensitivity, implicit-conversion
        // risk, non-Latin truncation) — and V2's text literals already emit as
        // `N'…'`, so NVARCHAR is also the consistent carrier. The default width
        // budgets (250 / 20) stay; an explicit declared length always wins
        // (`boundedOr`).
        | "email"          -> Some (Text, SqlStorageType.NVarChar (boundedOr (Bounded 250) length))
        | "phonenumber" | "phone" ->
            Some (Text, SqlStorageType.NVarChar (boundedOr (Bounded 20) length))
        | "url" | "password" | "username" | "identifiertext" ->
            Some (Text, SqlStorageType.NVarChar (textLength length))
        | "guid" | "uniqueidentifier" -> Some (Guid, SqlStorageType.UniqueIdentifier)
        | "xml"            -> Some (Text, SqlStorageType.Xml)
        | "entityreference" -> Some (Integer, SqlStorageType.BigInt)
        | other when other.StartsWith("bt", System.StringComparison.Ordinal)
                     && other.Contains("*") ->
            Some (Integer, SqlStorageType.BigInt)
        | _ -> None
