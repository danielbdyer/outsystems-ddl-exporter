namespace Projection.Targets.Json

open System.Globalization
open System.Text.Json
open Projection.Core
open FsToolkit.ErrorHandling

/// Round-trippable `Catalog ↔ JSON` codec — the persistence-boundary
/// `realize`/`ingest` pair, whose round-trip law `deserialize (serialize c) = c`
/// is the adjunction applied to durability (the keystone the `LifecycleStore`
/// and the durable provenance substrate, EXECUTION_PLAN 6.H, rest on).
///
/// **Total** — every IR type, field, and DU variant reachable from `Catalog`
/// is encoded (the inventory is the totality contract; a missed variant is an
/// `unknownKind` decode error, never a silent drop). **Deterministic (T1)** —
/// `JsonOptions.indented`, fixed write order, decimals via InvariantCulture,
/// `Map` keys emitted in sorted order. **Re-validating (A39)** — decode rebuilds
/// raw records (structural, no normalization) then funnels through
/// `Module.create` / `Catalog.create`, so a decoded catalog re-proves the
/// aggregate invariants (disjoint keys, no dangling FK) and surfaces a
/// `Result<Catalog>` rather than an unchecked value.
///
/// Distinct from `JsonEmitter` (one-directional, lossy projection for SSDT
/// consumers): this reuses the writer/reader *idioms*, none of its field shapes.
[<RequireQualifiedAccess>]
module CatalogCodec =

    [<Literal>]
    let version : int = 1

    let private inv (d: decimal) : string = JsonCodecKernel.inv d

    // ======================================================================
    // ENCODE — each `writeX : Utf8JsonWriter -> X -> unit` writes one JSON value.
    // Named fields: `jw.WriteString/Boolean/Number(name, v)` for primitives;
    // `wField` / `wOpt` / `wList` for value-objects, options, and lists.
    // ======================================================================

    let private wField (jw: Utf8JsonWriter) (name: string) (write: Utf8JsonWriter -> 'a -> unit) (v: 'a) : unit =
        jw.WritePropertyName name
        write jw v

    let private wOpt (jw: Utf8JsonWriter) (name: string) (write: Utf8JsonWriter -> 'a -> unit) (v: 'a option) : unit =
        jw.WritePropertyName name
        match v with
        | Some x -> write jw x
        | None   -> jw.WriteNullValue()

    let private wList (jw: Utf8JsonWriter) (name: string) (write: Utf8JsonWriter -> 'a -> unit) (xs: 'a list) : unit =
        jw.WritePropertyName name
        jw.WriteStartArray()
        for x in xs do write jw x
        jw.WriteEndArray()

    let private wStrVal  (jw: Utf8JsonWriter) (s: string)  : unit = jw.WriteStringValue s
    let private wDecVal  (jw: Utf8JsonWriter) (d: decimal) : unit = jw.WriteStringValue (inv d)
    let private wIntVal  (jw: Utf8JsonWriter) (i: int)     : unit = jw.WriteNumberValue i

    // -- value-types (Tier 0) ---------------------------------------------

    let private wSsKey (jw: Utf8JsonWriter) (k: SsKey) : unit =
        jw.WriteStringValue (SsKey.serialize k)

    let private wName (jw: Utf8JsonWriter) (n: Name) : unit =
        jw.WriteStringValue (Name.value n)

    let private wPrimitiveType (jw: Utf8JsonWriter) (t: PrimitiveType) : unit =
        jw.WriteStringValue (
            match t with
            | PrimitiveType.Integer  -> "Integer"
            | PrimitiveType.Decimal  -> "Decimal"
            | PrimitiveType.Text     -> "Text"
            | PrimitiveType.Boolean  -> "Boolean"
            | PrimitiveType.DateTime -> "DateTime"
            | PrimitiveType.Date     -> "Date"
            | PrimitiveType.Time     -> "Time"
            | PrimitiveType.Binary   -> "Binary"
            | PrimitiveType.Guid     -> "Guid")

    let private wOrigin (jw: Utf8JsonWriter) (o: Origin) : unit =
        jw.WriteStringValue (
            match o with
            | Origin.Native           -> "Native"
            | Origin.ExternalIndirect -> "ExternalIndirect"
            | Origin.ExternalDirect   -> "ExternalDirect")

    let private wReferenceAction (jw: Utf8JsonWriter) (a: ReferenceAction) : unit =
        jw.WriteStringValue (
            match a with
            | ReferenceAction.NoAction -> "NoAction"
            | ReferenceAction.Cascade  -> "Cascade"
            | ReferenceAction.SetNull  -> "SetNull"
            | ReferenceAction.Restrict -> "Restrict")

    let private wIndexDirection (jw: Utf8JsonWriter) (d: IndexColumnDirection) : unit =
        jw.WriteStringValue (
            match d with
            | IndexColumnDirection.Ascending  -> "Ascending"
            | IndexColumnDirection.Descending -> "Descending")

    let private wCompression (jw: Utf8JsonWriter) (c: DataCompressionLevel) : unit =
        jw.WriteStringValue (
            match c with
            | DataCompressionLevel.None -> "None"
            | DataCompressionLevel.Row  -> "Row"
            | DataCompressionLevel.Page -> "Page")

    let private wCacheMode (jw: Utf8JsonWriter) (m: SequenceCacheMode) : unit =
        jw.WriteStringValue (
            match m with
            | SequenceCacheMode.Unspecified -> "Unspecified"
            | SequenceCacheMode.Cache       -> "Cache"
            | SequenceCacheMode.NoCache     -> "NoCache")

    let private wRetentionUnit (jw: Utf8JsonWriter) (u: TemporalRetentionUnit) : unit =
        jw.WriteStringValue (
            match u with
            | TemporalRetentionUnit.Days   -> "Days"
            | TemporalRetentionUnit.Weeks  -> "Weeks"
            | TemporalRetentionUnit.Months -> "Months"
            | TemporalRetentionUnit.Years  -> "Years")

    let private wSqlLength (jw: Utf8JsonWriter) (l: SqlLength) : unit =
        jw.WriteStartObject()
        (match l with
         | SqlLength.Bounded n -> jw.WriteString("kind", "Bounded"); jw.WriteNumber("value", n)
         | SqlLength.Max       -> jw.WriteString("kind", "Max"))
        jw.WriteEndObject()

    // -- Tier 1 -----------------------------------------------------------

    let private wSqlStorage (jw: Utf8JsonWriter) (s: SqlStorageType) : unit =
        jw.WriteStartObject()
        jw.WritePropertyName "kind"
        (match s with
         | SqlStorageType.BigInt           -> jw.WriteStringValue "BigInt"
         | SqlStorageType.Int              -> jw.WriteStringValue "Int"
         | SqlStorageType.SmallInt         -> jw.WriteStringValue "SmallInt"
         | SqlStorageType.TinyInt          -> jw.WriteStringValue "TinyInt"
         | SqlStorageType.Bit              -> jw.WriteStringValue "Bit"
         | SqlStorageType.Decimal (p, sc)  -> jw.WriteStringValue "Decimal"; jw.WriteNumber("precision", p); jw.WriteNumber("scale", sc)
         | SqlStorageType.Numeric (p, sc)  -> jw.WriteStringValue "Numeric"; jw.WriteNumber("precision", p); jw.WriteNumber("scale", sc)
         | SqlStorageType.Money            -> jw.WriteStringValue "Money"
         | SqlStorageType.SmallMoney       -> jw.WriteStringValue "SmallMoney"
         | SqlStorageType.Float            -> jw.WriteStringValue "Float"
         | SqlStorageType.Real             -> jw.WriteStringValue "Real"
         | SqlStorageType.NVarChar len     -> jw.WriteStringValue "NVarChar"; wField jw "length" wSqlLength len
         | SqlStorageType.VarChar len      -> jw.WriteStringValue "VarChar"; wField jw "length" wSqlLength len
         | SqlStorageType.NChar n          -> jw.WriteStringValue "NChar"; jw.WriteNumber("length", n)
         | SqlStorageType.Char n           -> jw.WriteStringValue "Char"; jw.WriteNumber("length", n)
         | SqlStorageType.NText            -> jw.WriteStringValue "NText"
         | SqlStorageType.Text             -> jw.WriteStringValue "Text"
         | SqlStorageType.DateTime         -> jw.WriteStringValue "DateTime"
         | SqlStorageType.DateTime2 sc     -> jw.WriteStringValue "DateTime2"; wOpt jw "scale" wIntVal sc
         | SqlStorageType.DateTimeOffset sc -> jw.WriteStringValue "DateTimeOffset"; wOpt jw "scale" wIntVal sc
         | SqlStorageType.SmallDateTime    -> jw.WriteStringValue "SmallDateTime"
         | SqlStorageType.Date             -> jw.WriteStringValue "Date"
         | SqlStorageType.Time sc          -> jw.WriteStringValue "Time"; wOpt jw "scale" wIntVal sc
         | SqlStorageType.VarBinary len    -> jw.WriteStringValue "VarBinary"; wField jw "length" wSqlLength len
         | SqlStorageType.Binary n         -> jw.WriteStringValue "Binary"; jw.WriteNumber("length", n)
         | SqlStorageType.Image            -> jw.WriteStringValue "Image"
         | SqlStorageType.UniqueIdentifier -> jw.WriteStringValue "UniqueIdentifier"
         | SqlStorageType.Xml              -> jw.WriteStringValue "Xml")
        jw.WriteEndObject()

    let private wSqlLiteral (jw: Utf8JsonWriter) (lit: SqlLiteral) : unit =
        jw.WriteStartObject()
        (match lit with
         | SqlLiteral.NullLit         -> jw.WriteString("kind", "NullLit")
         | SqlLiteral.IntegerLit d    -> jw.WriteString("kind", "IntegerLit");  jw.WriteString("value", d)
         | SqlLiteral.DecimalLit d    -> jw.WriteString("kind", "DecimalLit");  jw.WriteString("value", d)
         | SqlLiteral.BooleanLit b    -> jw.WriteString("kind", "BooleanLit");  jw.WriteBoolean("value", b)
         | SqlLiteral.TextLit r       -> jw.WriteString("kind", "TextLit");     jw.WriteString("value", r)
         | SqlLiteral.TemporalLit r   -> jw.WriteString("kind", "TemporalLit"); jw.WriteString("value", r)
         | SqlLiteral.GuidLit r       -> jw.WriteString("kind", "GuidLit");     jw.WriteString("value", r)
         | SqlLiteral.BinaryLit h     -> jw.WriteString("kind", "BinaryLit");   jw.WriteString("value", h))
        jw.WriteEndObject()

    let private wTableId (jw: Utf8JsonWriter) (t: TableId) : unit =
        jw.WriteStartObject()
        wOpt jw "catalog" wStrVal t.Catalog
        jw.WriteString("schema", TableId.schemaText t)
        jw.WriteString("table", TableId.tableText t)
        jw.WriteEndObject()

    let private wDataSpace (jw: Utf8JsonWriter) (d: DataSpace) : unit =
        jw.WriteStartObject()
        (match d with
         | DataSpace.Filegroup name -> jw.WriteString("kind", "Filegroup"); jw.WriteString("name", name)
         | DataSpace.PartitionScheme (name, cols) ->
             jw.WriteString("kind", "PartitionScheme")
             jw.WriteString("name", name)
             wList jw "columns" wStrVal cols)
        jw.WriteEndObject()

    let private wIndexColumn (jw: Utf8JsonWriter) (c: IndexColumn) : unit =
        jw.WriteStartObject()
        wField jw "attribute" wSsKey c.Attribute
        wField jw "direction" wIndexDirection c.Direction
        jw.WriteEndObject()

    let private wTemporalRetention (jw: Utf8JsonWriter) (r: TemporalRetention) : unit =
        jw.WriteStartObject()
        (match r with
         | TemporalRetention.Infinite -> jw.WriteString("kind", "Infinite")
         | TemporalRetention.Limited (v, u) ->
             jw.WriteString("kind", "Limited")
             jw.WriteNumber("value", v)
             wField jw "unit" wRetentionUnit u)
        jw.WriteEndObject()

    let private wExtendedProperty (jw: Utf8JsonWriter) (e: ExtendedProperty) : unit =
        jw.WriteStartObject()
        jw.WriteString("name", e.Name)
        wOpt jw "value" wStrVal e.Value
        jw.WriteEndObject()

    let private wColumnRealization (jw: Utf8JsonWriter) (c: ColumnRealization) : unit =
        jw.WriteStartObject()
        jw.WriteString("columnName", ColumnRealization.columnNameText c)
        jw.WriteBoolean("isNullable", c.IsNullable)
        jw.WriteEndObject()

    let private wComputed (jw: Utf8JsonWriter) (c: ComputedColumnConfig) : unit =
        jw.WriteStartObject()
        jw.WriteString("expression", c.Expression)
        jw.WriteBoolean("isPersisted", c.IsPersisted)
        jw.WriteEndObject()

    let private wStaticRow (jw: Utf8JsonWriter) (r: StaticRow) : unit =
        jw.WriteStartObject()
        wField jw "identifier" wSsKey r.Identifier
        jw.WritePropertyName "values"
        jw.WriteStartArray()
        // Sorted by column name for T1 determinism (Map iteration order is unstable).
        for KeyValue (name, value) in (r.Values |> Map.toSeq |> Seq.sortBy (fun (n, _) -> Name.value n) |> Map.ofSeq) do
            jw.WriteStartObject()
            jw.WriteString("name", Name.value name)
            jw.WriteString("value", value)
            jw.WriteEndObject()
        jw.WriteEndArray()
        jw.WriteEndObject()

    // -- Tier 2 -----------------------------------------------------------

    let private wTemporalConfig (jw: Utf8JsonWriter) (c: TemporalConfig) : unit =
        jw.WriteStartObject()
        wOpt jw "historySchema" wStrVal c.HistorySchema
        wOpt jw "historyTable" wStrVal c.HistoryTable
        wOpt jw "periodStart" wName c.PeriodStart
        wOpt jw "periodEnd" wName c.PeriodEnd
        wField jw "retention" wTemporalRetention c.Retention
        jw.WriteEndObject()

    let private wColumnCheck (jw: Utf8JsonWriter) (c: ColumnCheck) : unit =
        jw.WriteStartObject()
        wField jw "ssKey" wSsKey c.SsKey
        wOpt jw "name" wName c.Name
        jw.WriteString("definition", c.Definition)
        jw.WriteBoolean("isNotTrusted", c.IsNotTrusted)
        jw.WriteEndObject()

    let private wTrigger (jw: Utf8JsonWriter) (t: Trigger) : unit =
        jw.WriteStartObject()
        wField jw "ssKey" wSsKey t.SsKey
        wField jw "name" wName t.Name
        jw.WriteBoolean("isDisabled", t.IsDisabled)
        jw.WriteString("definition", t.Definition)
        jw.WriteEndObject()

    let private wSequence (jw: Utf8JsonWriter) (s: Sequence) : unit =
        jw.WriteStartObject()
        wField jw "ssKey" wSsKey s.SsKey
        wField jw "name" wName s.Name
        jw.WriteString("schema", s.Schema)
        jw.WriteString("dataType", s.DataType)
        wOpt jw "startValue" wDecVal s.StartValue
        wOpt jw "increment" wDecVal s.Increment
        wOpt jw "minimum" wDecVal s.Minimum
        wOpt jw "maximum" wDecVal s.Maximum
        jw.WriteBoolean("isCycleEnabled", s.IsCycleEnabled)
        wField jw "cacheMode" wCacheMode s.CacheMode
        wOpt jw "cacheSize" wIntVal s.CacheSize
        jw.WriteEndObject()

    // -- Tier 3 -----------------------------------------------------------

    let private wModalityMark (jw: Utf8JsonWriter) (m: ModalityMark) : unit =
        jw.WriteStartObject()
        (match m with
         | ModalityMark.Static populations -> jw.WriteString("kind", "Static"); wList jw "populations" wStaticRow populations
         | ModalityMark.TenantScoped       -> jw.WriteString("kind", "TenantScoped")
         | ModalityMark.SoftDeletable      -> jw.WriteString("kind", "SoftDeletable")
         | ModalityMark.SystemOwned        -> jw.WriteString("kind", "SystemOwned")
         | ModalityMark.Temporal config    -> jw.WriteString("kind", "Temporal"); wField jw "config" wTemporalConfig config)
        jw.WriteEndObject()

    let private wAttribute (jw: Utf8JsonWriter) (a: Attribute) : unit =
        jw.WriteStartObject()
        wField jw "ssKey" wSsKey a.SsKey
        wField jw "name" wName a.Name
        wField jw "type" wPrimitiveType a.Type
        wField jw "column" wColumnRealization a.Column
        jw.WriteBoolean("isPrimaryKey", a.IsPrimaryKey)
        jw.WriteBoolean("isMandatory", a.IsMandatory)
        wOpt jw "length" wIntVal a.Length
        wOpt jw "precision" wIntVal a.Precision
        wOpt jw "scale" wIntVal a.Scale
        jw.WriteBoolean("isIdentity", a.IsIdentity)
        wOpt jw "description" wStrVal a.Description
        jw.WriteBoolean("isActive", a.IsActive)
        wOpt jw "defaultValue" wSqlLiteral a.DefaultValue
        wOpt jw "defaultName" wName a.DefaultName
        wOpt jw "computed" wComputed a.Computed
        wList jw "extendedProperties" wExtendedProperty a.ExtendedProperties
        wOpt jw "originalName" wStrVal a.OriginalName
        wOpt jw "externalDatabaseType" wStrVal a.ExternalDatabaseType
        wOpt jw "sqlStorage" wSqlStorage a.SqlStorage
        // WP8 / NM-72 — persist the authored Service-Studio order for
        // round-trip fidelity (the codec is the IR's lossless store).
        wOpt jw "order" wIntVal a.Order
        jw.WriteEndObject()

    let private wReference (jw: Utf8JsonWriter) (r: Reference) : unit =
        jw.WriteStartObject()
        wField jw "ssKey" wSsKey r.SsKey
        wField jw "name" wName r.Name
        wField jw "sourceAttribute" wSsKey r.SourceAttribute
        wField jw "targetKind" wSsKey r.TargetKind
        wField jw "onDelete" wReferenceAction r.OnDelete
        jw.WriteBoolean("isUserFk", r.IsUserFk)
        // M4 — the `ConstraintState` DU projects to the legacy boolean pair on
        // the wire (`IndexUniqueness`'s `(isUnique, isPrimaryKey)` precedent), so
        // serialized catalogs round-trip byte-identically (no store migration).
        jw.WriteBoolean("hasDbConstraint", Reference.hasDbConstraint r)
        wOpt jw "onUpdate" wReferenceAction r.OnUpdate
        jw.WriteBoolean("isConstraintTrusted", Reference.isConstraintTrusted r)
        jw.WriteEndObject()

    let private wIndex (jw: Utf8JsonWriter) (i: Index) : unit =
        jw.WriteStartObject()
        wField jw "ssKey" wSsKey i.SsKey
        wField jw "name" wName i.Name
        wList jw "columns" wIndexColumn i.Columns
        // Slice 2a (2026-06-02): wire format preserves the legacy
        // (isUnique, isPrimaryKey) boolean pair so existing serialized
        // catalogs round-trip; project Uniqueness through to the booleans
        // at the codec boundary.
        let isUniqueBool, isPrimaryKeyBool =
            match i.Uniqueness with
            | PrimaryKey -> true,  true
            | Unique     -> true,  false
            | NotUnique  -> false, false
        jw.WriteBoolean("isUnique", isUniqueBool)
        jw.WriteBoolean("isPrimaryKey", isPrimaryKeyBool)
        wList jw "extendedProperties" wExtendedProperty i.ExtendedProperties
        wOpt jw "filter" wStrVal i.Filter
        wList jw "includedColumns" wSsKey i.IncludedColumns
        jw.WriteBoolean("isPlatformAuto", i.IsPlatformAuto)
        wOpt jw "fillFactor" wIntVal i.FillFactor
        jw.WriteBoolean("isPadded", i.IsPadded)
        jw.WriteBoolean("allowRowLocks", i.AllowRowLocks)
        jw.WriteBoolean("allowPageLocks", i.AllowPageLocks)
        jw.WriteBoolean("noRecomputeStatistics", i.NoRecomputeStatistics)
        jw.WriteBoolean("ignoreDuplicateKey", i.IgnoreDuplicateKey)
        jw.WriteBoolean("isDisabled", i.IsDisabled)
        wOpt jw "dataCompression" wCompression i.DataCompression
        wOpt jw "dataSpace" wDataSpace i.DataSpace
        jw.WriteEndObject()

    // -- Tier 4 -----------------------------------------------------------

    let private wKind (jw: Utf8JsonWriter) (k: Kind) : unit =
        jw.WriteStartObject()
        wField jw "ssKey" wSsKey k.SsKey
        wField jw "name" wName k.Name
        wField jw "origin" wOrigin k.Origin
        wList jw "modality" wModalityMark k.Modality
        wField jw "physical" wTableId k.Physical
        wList jw "attributes" wAttribute k.Attributes
        wList jw "references" wReference k.References
        wList jw "indexes" wIndex k.Indexes
        wOpt jw "description" wStrVal k.Description
        jw.WriteBoolean("isActive", k.IsActive)
        wList jw "triggers" wTrigger k.Triggers
        wList jw "columnChecks" wColumnCheck k.ColumnChecks
        wList jw "extendedProperties" wExtendedProperty k.ExtendedProperties
        jw.WriteEndObject()

    let private wModule (jw: Utf8JsonWriter) (m: Module) : unit =
        jw.WriteStartObject()
        wField jw "ssKey" wSsKey m.SsKey
        wField jw "name" wName m.Name
        wList jw "kinds" wKind m.Kinds
        jw.WriteBoolean("isActive", m.IsActive)
        wList jw "extendedProperties" wExtendedProperty m.ExtendedProperties
        jw.WriteEndObject()

    let private wCatalog (jw: Utf8JsonWriter) (c: Catalog) : unit =
        jw.WriteStartObject()
        jw.WriteNumber("codecVersion", version)
        wList jw "modules" wModule c.Modules
        wList jw "sequences" wSequence c.Sequences
        jw.WriteEndObject()

    /// Serialize a `Catalog` to deterministic JSON text. Pure (no I/O).
    let serialize (catalog: Catalog) : string =
        use _ = Bench.scope "codec.catalog.serialize"
        JsonWriting.writeToString (fun jw -> wCatalog jw catalog)

    // ======================================================================
    // DECODE — each `readX : JsonElement -> Result<X>` reads one JSON value.
    // Leaf records are rebuilt structurally (perfect round-trip); the aggregate
    // gate `Module.create` / `Catalog.create` re-proves invariants (A39).
    // ======================================================================

    // Decode kernel — thin delegations to the shared `JsonCodecKernel` (prefix
    // `"codec"`), so the emitted error codes stay byte-identical. `fail` is a
    // passthrough (call sites compose the full `codec.<x>` code themselves).
    let private fail (code: string) (msg: string) : Result<'a> = JsonCodecKernel.fail code msg
    let private prop (el: JsonElement) (name: string) : Result<JsonElement> = JsonCodecKernel.prop "codec" el name
    let private asString (el: JsonElement) : Result<string> = JsonCodecKernel.asString "codec" el
    let private asBool (el: JsonElement) : Result<bool> = JsonCodecKernel.asBool "codec" el
    let private asInt (el: JsonElement) : Result<int> = JsonCodecKernel.asInt "codec" el
    let private asDecimal (el: JsonElement) : Result<decimal> = JsonCodecKernel.asDecimal "codec" el
    let private field (el: JsonElement) (name: string) (read: JsonElement -> Result<'a>) : Result<'a> = JsonCodecKernel.field "codec" el name read
    let private optField (el: JsonElement) (name: string) (read: JsonElement -> Result<'a>) : Result<'a option> = JsonCodecKernel.optField el name read
    let private listField (el: JsonElement) (name: string) (read: JsonElement -> Result<'a>) : Result<'a list> = JsonCodecKernel.listField "codec" el name read

    // -- value-types ------------------------------------------------------

    let private readSsKey (el: JsonElement) : Result<SsKey> =
        asString el |> Result.bind SsKey.deserialize

    let private readName (el: JsonElement) : Result<Name> =
        asString el |> Result.bind Name.create

    let private byTag (code: string) (read: string -> JsonElement -> Result<'a>) (el: JsonElement) : Result<'a> =
        field el "kind" asString |> Result.bind (fun kind -> read kind el)

    let private readPrimitiveType (el: JsonElement) : Result<PrimitiveType> =
        asString el
        |> Result.bind (function
            | "Integer"  -> Ok PrimitiveType.Integer
            | "Decimal"  -> Ok PrimitiveType.Decimal
            | "Text"     -> Ok PrimitiveType.Text
            | "Boolean"  -> Ok PrimitiveType.Boolean
            | "DateTime" -> Ok PrimitiveType.DateTime
            | "Date"     -> Ok PrimitiveType.Date
            | "Time"     -> Ok PrimitiveType.Time
            | "Binary"   -> Ok PrimitiveType.Binary
            | "Guid"     -> Ok PrimitiveType.Guid
            | o -> fail "codec.primitiveType.unknown" (sprintf "unknown PrimitiveType '%s'" o))

    let private readOrigin (el: JsonElement) : Result<Origin> =
        asString el
        |> Result.bind (function
            | "Native"           -> Ok Origin.Native
            | "ExternalIndirect" -> Ok Origin.ExternalIndirect
            | "ExternalDirect"   -> Ok Origin.ExternalDirect
            | o -> fail "codec.origin.unknown" (sprintf "unknown Origin '%s'" o))

    let private readReferenceAction (el: JsonElement) : Result<ReferenceAction> =
        asString el
        |> Result.bind (function
            | "NoAction" -> Ok ReferenceAction.NoAction
            | "Cascade"  -> Ok ReferenceAction.Cascade
            | "SetNull"  -> Ok ReferenceAction.SetNull
            | "Restrict" -> Ok ReferenceAction.Restrict
            | o -> fail "codec.referenceAction.unknown" (sprintf "unknown ReferenceAction '%s'" o))

    let private readIndexDirection (el: JsonElement) : Result<IndexColumnDirection> =
        asString el
        |> Result.bind (function
            | "Ascending"  -> Ok IndexColumnDirection.Ascending
            | "Descending" -> Ok IndexColumnDirection.Descending
            | o -> fail "codec.indexDirection.unknown" (sprintf "unknown IndexColumnDirection '%s'" o))

    let private readCompression (el: JsonElement) : Result<DataCompressionLevel> =
        asString el
        |> Result.bind (function
            | "None" -> Ok DataCompressionLevel.None
            | "Row"  -> Ok DataCompressionLevel.Row
            | "Page" -> Ok DataCompressionLevel.Page
            | o -> fail "codec.compression.unknown" (sprintf "unknown DataCompressionLevel '%s'" o))

    let private readCacheMode (el: JsonElement) : Result<SequenceCacheMode> =
        asString el
        |> Result.bind (function
            | "Unspecified" -> Ok SequenceCacheMode.Unspecified
            | "Cache"       -> Ok SequenceCacheMode.Cache
            | "NoCache"     -> Ok SequenceCacheMode.NoCache
            | o -> fail "codec.cacheMode.unknown" (sprintf "unknown SequenceCacheMode '%s'" o))

    let private readRetentionUnit (el: JsonElement) : Result<TemporalRetentionUnit> =
        asString el
        |> Result.bind (function
            | "Days"   -> Ok TemporalRetentionUnit.Days
            | "Weeks"  -> Ok TemporalRetentionUnit.Weeks
            | "Months" -> Ok TemporalRetentionUnit.Months
            | "Years"  -> Ok TemporalRetentionUnit.Years
            | o -> fail "codec.retentionUnit.unknown" (sprintf "unknown TemporalRetentionUnit '%s'" o))

    let private readSqlLength (el: JsonElement) : Result<SqlLength> =
        byTag "sqlLength" (fun kind el ->
            match kind with
            | "Bounded" -> field el "value" asInt |> Result.map SqlLength.Bounded
            | "Max"     -> Ok SqlLength.Max
            | o -> fail "codec.sqlLength.unknown" (sprintf "unknown SqlLength kind '%s'" o)) el

    let private readSqlStorage (el: JsonElement) : Result<SqlStorageType> =
        byTag "sqlStorage" (fun kind el ->
            match kind with
            | "BigInt"           -> Ok SqlStorageType.BigInt
            | "Int"              -> Ok SqlStorageType.Int
            | "SmallInt"         -> Ok SqlStorageType.SmallInt
            | "TinyInt"          -> Ok SqlStorageType.TinyInt
            | "Bit"              -> Ok SqlStorageType.Bit
            | "Decimal"          -> result { let! p = field el "precision" asInt in let! s = field el "scale" asInt in return SqlStorageType.Decimal (p, s) }
            | "Numeric"          -> result { let! p = field el "precision" asInt in let! s = field el "scale" asInt in return SqlStorageType.Numeric (p, s) }
            | "Money"            -> Ok SqlStorageType.Money
            | "SmallMoney"       -> Ok SqlStorageType.SmallMoney
            | "Float"            -> Ok SqlStorageType.Float
            | "Real"             -> Ok SqlStorageType.Real
            | "NVarChar"         -> field el "length" readSqlLength |> Result.map SqlStorageType.NVarChar
            | "VarChar"          -> field el "length" readSqlLength |> Result.map SqlStorageType.VarChar
            | "NChar"            -> field el "length" asInt |> Result.map SqlStorageType.NChar
            | "Char"             -> field el "length" asInt |> Result.map SqlStorageType.Char
            | "NText"            -> Ok SqlStorageType.NText
            | "Text"             -> Ok SqlStorageType.Text
            | "DateTime"         -> Ok SqlStorageType.DateTime
            | "DateTime2"        -> optField el "scale" asInt |> Result.map SqlStorageType.DateTime2
            | "DateTimeOffset"   -> optField el "scale" asInt |> Result.map SqlStorageType.DateTimeOffset
            | "SmallDateTime"    -> Ok SqlStorageType.SmallDateTime
            | "Date"             -> Ok SqlStorageType.Date
            | "Time"             -> optField el "scale" asInt |> Result.map SqlStorageType.Time
            | "VarBinary"        -> field el "length" readSqlLength |> Result.map SqlStorageType.VarBinary
            | "Binary"           -> field el "length" asInt |> Result.map SqlStorageType.Binary
            | "Image"            -> Ok SqlStorageType.Image
            | "UniqueIdentifier" -> Ok SqlStorageType.UniqueIdentifier
            | "Xml"              -> Ok SqlStorageType.Xml
            | o -> fail "codec.sqlStorage.unknown" (sprintf "unknown SqlStorageType kind '%s'" o)) el

    let private readSqlLiteral (el: JsonElement) : Result<SqlLiteral> =
        byTag "sqlLiteral" (fun kind el ->
            match kind with
            | "NullLit"     -> Ok SqlLiteral.NullLit
            | "IntegerLit"  -> field el "value" asString |> Result.map SqlLiteral.IntegerLit
            | "DecimalLit"  -> field el "value" asString |> Result.map SqlLiteral.DecimalLit
            | "BooleanLit"  -> field el "value" asBool   |> Result.map SqlLiteral.BooleanLit
            | "TextLit"     -> field el "value" asString |> Result.map SqlLiteral.TextLit
            | "TemporalLit" -> field el "value" asString |> Result.map SqlLiteral.TemporalLit
            | "GuidLit"     -> field el "value" asString |> Result.map SqlLiteral.GuidLit
            | "BinaryLit"   -> field el "value" asString |> Result.map SqlLiteral.BinaryLit
            | o -> fail "codec.sqlLiteral.unknown" (sprintf "unknown SqlLiteral kind '%s'" o)) el

    let private readTableId (el: JsonElement) : Result<TableId> =
        result {
            let! catalog = optField el "catalog" asString
            let! schema = field el "schema" asString
            let! table = field el "table" asString
            let! schemaName = SchemaName.create schema
            let! tableName = TableName.create table
            return { Catalog = catalog; Schema = schemaName; Table = tableName }
        }

    let private readDataSpace (el: JsonElement) : Result<DataSpace> =
        byTag "dataSpace" (fun kind el ->
            match kind with
            | "Filegroup" -> field el "name" asString |> Result.map DataSpace.Filegroup
            | "PartitionScheme" ->
                result {
                    let! name = field el "name" asString
                    let! cols = listField el "columns" asString
                    return DataSpace.PartitionScheme (name, cols)
                }
            | o -> fail "codec.dataSpace.unknown" (sprintf "unknown DataSpace kind '%s'" o)) el

    let private readIndexColumn (el: JsonElement) : Result<IndexColumn> =
        result {
            let! attr = field el "attribute" readSsKey
            let! dir = field el "direction" readIndexDirection
            return { Attribute = attr; Direction = dir }
        }

    let private readTemporalRetention (el: JsonElement) : Result<TemporalRetention> =
        byTag "temporalRetention" (fun kind el ->
            match kind with
            | "Infinite" -> Ok TemporalRetention.Infinite
            | "Limited" ->
                result {
                    let! v = field el "value" asInt
                    let! u = field el "unit" readRetentionUnit
                    return TemporalRetention.Limited (v, u)
                }
            | o -> fail "codec.temporalRetention.unknown" (sprintf "unknown TemporalRetention kind '%s'" o)) el

    let private readExtendedProperty (el: JsonElement) : Result<ExtendedProperty> =
        result {
            let! name = field el "name" asString
            let! value = optField el "value" asString
            return! ExtendedProperty.create name value
        }

    let private readColumnRealization (el: JsonElement) : Result<ColumnRealization> =
        result {
            let! columnName = field el "columnName" asString
            let! isNullable = field el "isNullable" asBool
            return! ColumnRealization.create columnName isNullable
        }

    let private readComputed (el: JsonElement) : Result<ComputedColumnConfig> =
        result {
            let! expression = field el "expression" asString
            let! isPersisted = field el "isPersisted" asBool
            return! ComputedColumnConfig.create expression isPersisted
        }

    let private readStaticRow (el: JsonElement) : Result<StaticRow> =
        result {
            let! identifier = field el "identifier" readSsKey
            let! pairs =
                listField el "values" (fun v ->
                    result {
                        let! name = field v "name" readName
                        let! value = field v "value" asString
                        return (name, value)
                    })
            return { Identifier = identifier; Values = Map.ofList pairs }
        }

    let private readTemporalConfig (el: JsonElement) : Result<TemporalConfig> =
        result {
            let! historySchema = optField el "historySchema" asString
            let! historyTable = optField el "historyTable" asString
            let! periodStart = optField el "periodStart" readName
            let! periodEnd = optField el "periodEnd" readName
            let! retention = field el "retention" readTemporalRetention
            return
                { HistorySchema = historySchema
                  HistoryTable = historyTable
                  PeriodStart = periodStart
                  PeriodEnd = periodEnd
                  Retention = retention }
        }

    let private readColumnCheck (el: JsonElement) : Result<ColumnCheck> =
        result {
            let! ssKey = field el "ssKey" readSsKey
            let! name = optField el "name" readName
            let! definition = field el "definition" asString
            let! isNotTrusted = field el "isNotTrusted" asBool
            return! ColumnCheck.create ssKey name definition isNotTrusted
        }

    let private readTrigger (el: JsonElement) : Result<Trigger> =
        result {
            let! ssKey = field el "ssKey" readSsKey
            let! name = field el "name" readName
            let! isDisabled = field el "isDisabled" asBool
            let! definition = field el "definition" asString
            return! Trigger.create ssKey name isDisabled definition
        }

    let private readSequence (el: JsonElement) : Result<Sequence> =
        result {
            let! ssKey = field el "ssKey" readSsKey
            let! name = field el "name" readName
            let! schema = field el "schema" asString
            let! dataType = field el "dataType" asString
            let! startValue = optField el "startValue" asDecimal
            let! increment = optField el "increment" asDecimal
            let! minimum = optField el "minimum" asDecimal
            let! maximum = optField el "maximum" asDecimal
            let! isCycleEnabled = field el "isCycleEnabled" asBool
            let! cacheMode = field el "cacheMode" readCacheMode
            let! cacheSize = optField el "cacheSize" asInt
            return! Sequence.create ssKey name schema dataType startValue increment minimum maximum isCycleEnabled cacheMode cacheSize
        }

    let private readModalityMark (el: JsonElement) : Result<ModalityMark> =
        byTag "modalityMark" (fun kind el ->
            match kind with
            | "Static"        -> listField el "populations" readStaticRow |> Result.map ModalityMark.Static
            | "TenantScoped"  -> Ok ModalityMark.TenantScoped
            | "SoftDeletable" -> Ok ModalityMark.SoftDeletable
            | "SystemOwned"   -> Ok ModalityMark.SystemOwned
            | "Temporal"      -> field el "config" readTemporalConfig |> Result.map ModalityMark.Temporal
            | o -> fail "codec.modalityMark.unknown" (sprintf "unknown ModalityMark kind '%s'" o)) el

    let private readAttribute (el: JsonElement) : Result<Attribute> =
        result {
            let! ssKey = field el "ssKey" readSsKey
            let! name = field el "name" readName
            let! typ = field el "type" readPrimitiveType
            let! column = field el "column" readColumnRealization
            let! isPrimaryKey = field el "isPrimaryKey" asBool
            let! isMandatory = field el "isMandatory" asBool
            let! length = optField el "length" asInt
            let! precision = optField el "precision" asInt
            let! scale = optField el "scale" asInt
            let! isIdentity = field el "isIdentity" asBool
            let! description = optField el "description" asString
            let! isActive = field el "isActive" asBool
            let! defaultValue = optField el "defaultValue" readSqlLiteral
            let! defaultName = optField el "defaultName" readName
            let! computed = optField el "computed" readComputed
            let! extendedProperties = listField el "extendedProperties" readExtendedProperty
            let! originalName = optField el "originalName" asString
            let! externalDatabaseType = optField el "externalDatabaseType" asString
            let! sqlStorage = optField el "sqlStorage" readSqlStorage
            // WP8 / NM-72 — authored Service-Studio order, round-tripped.
            let! order = optField el "order" asInt
            return
                { Attribute.create ssKey name typ with
                    Column = column
                    IsPrimaryKey = isPrimaryKey
                    IsMandatory = isMandatory
                    Length = length
                    Precision = precision
                    Scale = scale
                    IsIdentity = isIdentity
                    Description = description
                    IsActive = isActive
                    DefaultValue = defaultValue
                    DefaultName = defaultName
                    Computed = computed
                    ExtendedProperties = extendedProperties
                    OriginalName = originalName
                    ExternalDatabaseType = externalDatabaseType
                    SqlStorage = sqlStorage
                    Order = order }
        }

    let private readReference (el: JsonElement) : Result<Reference> =
        result {
            let! ssKey = field el "ssKey" readSsKey
            let! name = field el "name" readName
            let! sourceAttribute = field el "sourceAttribute" readSsKey
            let! targetKind = field el "targetKind" readSsKey
            let! onDelete = field el "onDelete" readReferenceAction
            let! isUserFk = field el "isUserFk" asBool
            let! hasDbConstraint = field el "hasDbConstraint" asBool
            let! onUpdate = optField el "onUpdate" readReferenceAction
            let! isConstraintTrusted = field el "isConstraintTrusted" asBool
            return
                { Reference.create ssKey name sourceAttribute targetKind with
                    OnDelete = onDelete
                    IsUserFk = isUserFk
                    OnUpdate = onUpdate
                    // M4 — reconstruct the DU from the legacy boolean pair
                    // (`ofLegacyBooleans` normalizes the illegal quadrant).
                    ConstraintState = ConstraintState.ofLegacyBooleans hasDbConstraint isConstraintTrusted }
        }

    let private readIndex (el: JsonElement) : Result<Index> =
        result {
            let! ssKey = field el "ssKey" readSsKey
            let! name = field el "name" readName
            let! columns = listField el "columns" readIndexColumn
            let! isUnique = field el "isUnique" asBool
            let! isPrimaryKey = field el "isPrimaryKey" asBool
            let! extendedProperties = listField el "extendedProperties" readExtendedProperty
            let! filter = optField el "filter" asString
            let! includedColumns = listField el "includedColumns" readSsKey
            let! isPlatformAuto = field el "isPlatformAuto" asBool
            let! fillFactor = optField el "fillFactor" asInt
            let! isPadded = field el "isPadded" asBool
            let! allowRowLocks = field el "allowRowLocks" asBool
            let! allowPageLocks = field el "allowPageLocks" asBool
            let! noRecomputeStatistics = field el "noRecomputeStatistics" asBool
            let! ignoreDuplicateKey = field el "ignoreDuplicateKey" asBool
            let! isDisabled = field el "isDisabled" asBool
            let! dataCompression = optField el "dataCompression" readCompression
            let! dataSpace = optField el "dataSpace" readDataSpace
            return
                { Index.create ssKey name columns with
                    Uniqueness = IndexUniqueness.ofLegacyBooleans isUnique isPrimaryKey
                    ExtendedProperties = extendedProperties
                    Filter = filter
                    IncludedColumns = includedColumns
                    IsPlatformAuto = isPlatformAuto
                    FillFactor = fillFactor
                    IsPadded = isPadded
                    AllowRowLocks = allowRowLocks
                    AllowPageLocks = allowPageLocks
                    NoRecomputeStatistics = noRecomputeStatistics
                    IgnoreDuplicateKey = ignoreDuplicateKey
                    IsDisabled = isDisabled
                    DataCompression = dataCompression
                    DataSpace = dataSpace }
        }

    let private readKind (el: JsonElement) : Result<Kind> =
        result {
            let! ssKey = field el "ssKey" readSsKey
            let! name = field el "name" readName
            let! origin = field el "origin" readOrigin
            let! modality = listField el "modality" readModalityMark
            let! physical = field el "physical" readTableId
            let! attributes = listField el "attributes" readAttribute
            let! references = listField el "references" readReference
            let! indexes = listField el "indexes" readIndex
            let! description = optField el "description" asString
            let! isActive = field el "isActive" asBool
            let! triggers = listField el "triggers" readTrigger
            let! columnChecks = listField el "columnChecks" readColumnCheck
            let! extendedProperties = listField el "extendedProperties" readExtendedProperty
            return
                { Kind.create ssKey name physical attributes with
                    Origin = origin
                    Modality = modality
                    References = references
                    Indexes = indexes
                    Description = description
                    IsActive = isActive
                    Triggers = triggers
                    ColumnChecks = columnChecks
                    ExtendedProperties = extendedProperties }
        }

    let private readModule (el: JsonElement) : Result<Module> =
        result {
            let! ssKey = field el "ssKey" readSsKey
            let! name = field el "name" readName
            let! kinds = listField el "kinds" readKind
            let! isActive = field el "isActive" asBool
            let! extendedProperties = listField el "extendedProperties" readExtendedProperty
            return! Module.create ssKey name kinds isActive extendedProperties
        }

    /// Deserialize JSON text to a `Catalog`, re-validating aggregate invariants
    /// via `Module.create` / `Catalog.create`. Malformed JSON is a structured
    /// `Result` failure, never an exception. Pure (no I/O).
    let deserialize (json: string) : Result<Catalog> =
        use _ = Bench.scope "codec.catalog.deserialize"
        let parsed =
            try Ok (JsonDocument.Parse json)
            with ex -> fail "codec.parse" (sprintf "malformed JSON: %s" ex.Message)
        parsed
        |> Result.bind (fun doc ->
            use doc = doc
            let root = doc.RootElement
            if root.ValueKind <> JsonValueKind.Object then
                fail "codec.root" (sprintf "expected a JSON object at the root, got %A" root.ValueKind)
            else
                result {
                    let! modules = listField root "modules" readModule
                    let! sequences = listField root "sequences" readSequence
                    return! Catalog.create modules sequences
                })
