namespace Projection.Core

/// The OutSystems(OSSYS) → V2 type correspondence (recon #10). These are
/// *decisions*, not translations — the verbatim-to-4000 text-length rule, the
/// `currency → DECIMAL(37,8)` choice, the platform-mapped `email`/`phone`
/// `VARCHAR(250)/(20)` (OutSystems 11 database data types; DECISIONS 2026-07-18),
/// the `longinteger → BIGINT` / `datetime → DATETIME` (legacy, not
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

    /// `Text` width: a declared length is preserved VERBATIM (`Bounded n`) up to
    /// NVARCHAR's bounded physical ceiling (4000); beyond it — and on absence —
    /// the width is open-ended `(MAX)`. Source-model intent outranks the
    /// platform's own >2000 → `nvarchar(max)` storage collapse (operator ruling;
    /// DECISIONS 2026-07-18 — which also retired V1's `maxLengthThreshold = 2000`
    /// flip, an off-by-one against the platform's ≤2000-stays-bounded contract).
    let textLength (length: int option) : SqlLength =
        match length with
        | Some n when n > 4000 -> Max
        | Some n when n > 0    -> Bounded n
        | _                    -> Max

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
        // Date-only and time-only attributes store as DATETIME — the OutSystems 11
        // platform mapping (Date → datetime, Time → datetime; operator ruling,
        // DECISIONS 2026-07-18). The semantic category collapses with the storage
        // because the two-field law holds (storage always projects back to the
        // semantic type it was set with); a true DATE / TIME(n) column still
        // arrives intact via the deployed-reflection lane.
        | "date"           -> Some (DateTime, SqlStorageType.DateTime)
        | "time"           -> Some (DateTime, SqlStorageType.DateTime)
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
        // `email`/`phone` map to ANSI VARCHAR(250)/(20) — the OutSystems 11
        // platform mapping (Database Data Types reference: Email → varchar(250),
        // Phone Number → varchar(20); confirmed against the live page and the
        // docs source, DECISIONS 2026-07-18). These are the platform's own
        // deliberate ASCII islands in an otherwise-NVARCHAR schema. WP-4's
        // NVARCHAR revision (2026-07-16) rested on `ossys_User.EMAIL` being
        // `nvarchar(250)` — true, but that is hand-shipped SYSTEM-table DDL,
        // not attribute-mapping output — and is reverted. The default width
        // budgets (250 / 20) stay; an explicit declared length always wins
        // (`boundedOr`); `N'…'` literals implicit-convert into varchar, so the
        // data plane is unchanged.
        | "email"          -> Some (Text, SqlStorageType.VarChar (boundedOr (Bounded 250) length))
        | "phonenumber" | "phone" ->
            Some (Text, SqlStorageType.VarChar (boundedOr (Bounded 20) length))
        | "url" | "password" | "username" | "identifiertext" ->
            Some (Text, SqlStorageType.NVarChar (textLength length))
        | "guid" | "uniqueidentifier" -> Some (Guid, SqlStorageType.UniqueIdentifier)
        | "xml"            -> Some (Text, SqlStorageType.Xml)
        | "entityreference" -> Some (Integer, SqlStorageType.BigInt)
        | other when other.StartsWith("bt", System.StringComparison.Ordinal)
                     && other.Contains("*") ->
            Some (Integer, SqlStorageType.BigInt)
        | _ -> None
