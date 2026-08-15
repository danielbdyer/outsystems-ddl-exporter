namespace Projection.Adapters.OssysSql

open System
open System.Text.Json
open Projection.Core
open FsToolkit.ErrorHandling

/// The sink's snapshot codec (the data-sink chapter, S3 2026-08-15;
/// CHAPTER_SINK_OPEN.md). Round-trip law: `deserialize (serialize s) = Ok s`
/// for every `MetadataSnapshotRunner.MetadataSnapshot` — the persisted form
/// of the acquisition witness, the sink's `snapshots/<syncId>/snapshot.json`.
///
/// - **Total.** Every field of every rowset record is written explicitly
///   (`None` ⇒ JSON null — the estate sidecar's cell-fidelity rule) and
///   every rowset array is REQUIRED on decode (a missing array is a decode
///   error, never a defaulted empty — the witness is total or it is not the
///   witness).
/// - **Deterministic (T1).** Fixed field order (record order), indented
///   writer via `JsonWriting.writeToString` (pinned options), decimals as
///   invariant-culture strings, Guids in RFC "D" form lowercase.
/// - **Versioned.** `codecVersion` is the FIRST key (the `CatalogCodec`
///   precedent — head-sniffable); an unknown version fails closed.
/// - **Structural rebuild only.** No `Catalog.create` re-validation — the
///   bundle is pre-IR by design; recovery must be able to load exactly the
///   snapshot whose model no longer constructs.
///
/// Decode helpers are local siblings of `JsonCodecKernel` (Targets.Json,
/// which this adapter cannot reference — the hexagonal direction). With one
/// fixed code family (`sink.codec.*`) every code is a complete literal, so
/// this copy carries no error-code composition at all. The kernel's
/// extraction to Core is a named close-ritual audit candidate (S16).
[<RequireQualifiedAccess>]
module MetadataSnapshotCodec =

    /// Codec format version. Emitted as the FIRST key so a reader can
    /// head-sniff without a full parse; bump on any shape change.
    [<Literal>]
    let version : int = 1

    // ------------------------------------------------------------------
    // Decode kernel (local; see the module docstring).
    // ------------------------------------------------------------------

    let private fail (code: string) (msg: string) : Result<'a> =
        Result.failureOf (ValidationError.create code msg)

    let private prop (el: JsonElement) (name: string) : Result<JsonElement> =
        match el.TryGetProperty name with
        | true, v -> Ok v
        | _ -> fail "sink.codec.missingField" (sprintf "missing field '%s'" name)

    let private asString (el: JsonElement) : Result<string> =
        if el.ValueKind = JsonValueKind.String then
            match el.GetString() with
            | null -> fail "sink.codec.expectedString" "string element returned null"
            | s -> Ok s
        else fail "sink.codec.expectedString" (sprintf "expected string, got %A" el.ValueKind)

    let private asBool (el: JsonElement) : Result<bool> =
        match el.ValueKind with
        | JsonValueKind.True  -> Ok true
        | JsonValueKind.False -> Ok false
        | k -> fail "sink.codec.expectedBool" (sprintf "expected bool, got %A" k)

    let private asInt (el: JsonElement) : Result<int> =
        if el.ValueKind = JsonValueKind.Number then
            match el.TryGetInt32() with
            | true, n -> Ok n
            | _ -> fail "sink.codec.expectedInt" "number is not an int32"
        else fail "sink.codec.expectedInt" (sprintf "expected number, got %A" el.ValueKind)

    let private asDecimal (el: JsonElement) : Result<decimal> =
        asString el
        |> Result.bind (fun s ->
            match Decimal.TryParse(s, Globalization.NumberStyles.Number, Globalization.CultureInfo.InvariantCulture) with
            | true, d -> Ok d
            | _ -> fail "sink.codec.expectedDecimal" (sprintf "not an invariant-culture decimal: '%s'" s))

    let private asGuid (el: JsonElement) : Result<Guid> =
        asString el
        |> Result.bind (fun s ->
            match Guid.TryParseExact(s, "D") with
            | true, g -> Ok g
            | _ -> fail "sink.codec.expectedGuid" (sprintf "not an RFC-4122 'D' Guid: '%s'" s))

    let private field (el: JsonElement) (name: string) (read: JsonElement -> Result<'a>) : Result<'a> =
        prop el name |> Result.bind read

    let private optField (el: JsonElement) (name: string) (read: JsonElement -> Result<'a>) : Result<'a option> =
        match el.TryGetProperty name with
        | true, v when v.ValueKind = JsonValueKind.Null -> Ok None
        | true, v -> read v |> Result.map Some
        | _ -> fail "sink.codec.missingField" (sprintf "missing field '%s' (options are written as null, never omitted)" name)

    let private collect (items: Result<'a> list) : Result<'a list> =
        let folder (acc: Result<'a list>) (item: Result<'a>) =
            match acc, item with
            | Ok xs, Ok x -> Ok (x :: xs)
            | Error es, Ok _ -> Error es
            | Ok _, Error es -> Error es
            | Error es1, Error es2 -> Error (es1 @ es2)
        items |> List.fold folder (Ok []) |> Result.map List.rev

    /// A REQUIRED rowset array: missing or non-array fails closed — the
    /// witness's totality is the point (contrast the kernel's `listField`,
    /// whose missing→[] default serves optional document sections).
    let private reqList (el: JsonElement) (name: string) (read: JsonElement -> Result<'a>) : Result<'a list> =
        match el.TryGetProperty name with
        | true, v when v.ValueKind = JsonValueKind.Array ->
            v.EnumerateArray() |> Seq.map read |> Seq.toList |> collect
        | true, v -> fail "sink.codec.expectedArray" (sprintf "field '%s': expected array, got %A" name v.ValueKind)
        | _ -> fail "sink.codec.missingField" (sprintf "missing rowset array '%s'" name)

    let private inv (d: decimal) : string = d.ToString(Globalization.CultureInfo.InvariantCulture)
    let private guidText (g: Guid) : string = g.ToString("D")

    // ------------------------------------------------------------------
    // Encode kernel (write-side twins; no prefix — pure structural writers).
    // ------------------------------------------------------------------

    let private wStr (jw: Utf8JsonWriter) (name: string) (v: string) = jw.WriteString(name, v)
    let private wInt (jw: Utf8JsonWriter) (name: string) (v: int) = jw.WriteNumber(name, v)
    let private wBool (jw: Utf8JsonWriter) (name: string) (v: bool) = jw.WriteBoolean(name, v)

    let private wStrOpt (jw: Utf8JsonWriter) (name: string) (v: string option) =
        match v with
        | Some s -> jw.WriteString(name, s)
        | None -> jw.WriteNull(name)

    let private wIntOpt (jw: Utf8JsonWriter) (name: string) (v: int option) =
        match v with
        | Some n -> jw.WriteNumber(name, n)
        | None -> jw.WriteNull(name)

    let private wGuidOpt (jw: Utf8JsonWriter) (name: string) (v: Guid option) =
        match v with
        | Some g -> jw.WriteString(name, guidText g)
        | None -> jw.WriteNull(name)

    let private wDecOpt (jw: Utf8JsonWriter) (name: string) (v: decimal option) =
        match v with
        | Some d -> jw.WriteString(name, inv d)
        | None -> jw.WriteNull(name)

    let private wRows (jw: Utf8JsonWriter) (name: string) (writeRow: Utf8JsonWriter -> 'a -> unit) (rows: 'a list) =
        jw.WritePropertyName name
        jw.WriteStartArray()
        for r in rows do
            jw.WriteStartObject()
            writeRow jw r
            jw.WriteEndObject()
        jw.WriteEndArray()

    // ------------------------------------------------------------------
    // Per-rowset row writers + readers (field order = record order).
    // ------------------------------------------------------------------

    let private writeModuleRow (jw: Utf8JsonWriter) (r: MetadataSnapshotRunner.OssysModuleRow) =
        wInt jw "espaceId" r.EspaceId
        wStr jw "espaceName" r.EspaceName
        wBool jw "isSystemModule" r.IsSystemModule
        wBool jw "isActive" r.IsActive
        wStrOpt jw "espaceKind" r.EspaceKind
        wGuidOpt jw "espaceSsKey" r.EspaceSsKey

    let private readModuleRow (el: JsonElement) : Result<MetadataSnapshotRunner.OssysModuleRow> =
        result {
            let! espaceId = field el "espaceId" asInt
            let! espaceName = field el "espaceName" asString
            let! isSystemModule = field el "isSystemModule" asBool
            let! isActive = field el "isActive" asBool
            let! espaceKind = optField el "espaceKind" asString
            let! espaceSsKey = optField el "espaceSsKey" asGuid
            return
                { EspaceId = espaceId
                  EspaceName = espaceName
                  IsSystemModule = isSystemModule
                  IsActive = isActive
                  EspaceKind = espaceKind
                  EspaceSsKey = espaceSsKey }
        }

    let private writeEntityRow (jw: Utf8JsonWriter) (r: MetadataSnapshotRunner.OssysEntityRow) =
        wInt jw "entityId" r.EntityId
        wStr jw "entityName" r.EntityName
        wStr jw "physicalTableName" r.PhysicalTableName
        wInt jw "espaceId" r.EspaceId
        wBool jw "isActive" r.IsActive
        wBool jw "isSystemEntity" r.IsSystemEntity
        wBool jw "isExternal" r.IsExternal
        wStrOpt jw "dataKind" r.DataKind
        wGuidOpt jw "primaryKeySsKey" r.PrimaryKeySsKey
        wGuidOpt jw "entitySsKey" r.EntitySsKey
        wStrOpt jw "description" r.Description

    let private readEntityRow (el: JsonElement) : Result<MetadataSnapshotRunner.OssysEntityRow> =
        result {
            let! entityId = field el "entityId" asInt
            let! entityName = field el "entityName" asString
            let! physicalTableName = field el "physicalTableName" asString
            let! espaceId = field el "espaceId" asInt
            let! isActive = field el "isActive" asBool
            let! isSystemEntity = field el "isSystemEntity" asBool
            let! isExternal = field el "isExternal" asBool
            let! dataKind = optField el "dataKind" asString
            let! primaryKeySsKey = optField el "primaryKeySsKey" asGuid
            let! entitySsKey = optField el "entitySsKey" asGuid
            let! description = optField el "description" asString
            return
                { EntityId = entityId
                  EntityName = entityName
                  PhysicalTableName = physicalTableName
                  EspaceId = espaceId
                  IsActive = isActive
                  IsSystemEntity = isSystemEntity
                  IsExternal = isExternal
                  DataKind = dataKind
                  PrimaryKeySsKey = primaryKeySsKey
                  EntitySsKey = entitySsKey
                  Description = description }
        }

    let private writeAttributeRow (jw: Utf8JsonWriter) (r: MetadataSnapshotRunner.OssysAttributeRow) =
        wInt jw "attrId" r.AttrId
        wInt jw "entityId" r.EntityId
        wStr jw "attrName" r.AttrName
        wGuidOpt jw "attrSsKey" r.AttrSsKey
        wStrOpt jw "dataType" r.DataType
        wIntOpt jw "length" r.Length
        wIntOpt jw "precision" r.Precision
        wIntOpt jw "scale" r.Scale
        wStrOpt jw "defaultValue" r.DefaultValue
        wBool jw "isMandatory" r.IsMandatory
        wBool jw "isActive" r.IsActive
        wBool jw "isAutoNumber" r.IsAutoNumber
        wBool jw "isIdentifier" r.IsIdentifier
        wIntOpt jw "refEntityId" r.RefEntityId
        wStrOpt jw "originalName" r.OriginalName
        wStrOpt jw "externalDbType" r.ExternalDbType
        wStrOpt jw "deleteRule" r.DeleteRule
        wStr jw "physicalCol" r.PhysicalCol
        wStrOpt jw "description" r.Description
        wIntOpt jw "order" r.Order

    let private readAttributeRow (el: JsonElement) : Result<MetadataSnapshotRunner.OssysAttributeRow> =
        result {
            let! attrId = field el "attrId" asInt
            let! entityId = field el "entityId" asInt
            let! attrName = field el "attrName" asString
            let! attrSsKey = optField el "attrSsKey" asGuid
            let! dataType = optField el "dataType" asString
            let! length = optField el "length" asInt
            let! precision = optField el "precision" asInt
            let! scale = optField el "scale" asInt
            let! defaultValue = optField el "defaultValue" asString
            let! isMandatory = field el "isMandatory" asBool
            let! isActive = field el "isActive" asBool
            let! isAutoNumber = field el "isAutoNumber" asBool
            let! isIdentifier = field el "isIdentifier" asBool
            let! refEntityId = optField el "refEntityId" asInt
            let! originalName = optField el "originalName" asString
            let! externalDbType = optField el "externalDbType" asString
            let! deleteRule = optField el "deleteRule" asString
            let! physicalCol = field el "physicalCol" asString
            let! description = optField el "description" asString
            let! order = optField el "order" asInt
            return
                { AttrId = attrId
                  EntityId = entityId
                  AttrName = attrName
                  AttrSsKey = attrSsKey
                  DataType = dataType
                  Length = length
                  Precision = precision
                  Scale = scale
                  DefaultValue = defaultValue
                  IsMandatory = isMandatory
                  IsActive = isActive
                  IsAutoNumber = isAutoNumber
                  IsIdentifier = isIdentifier
                  RefEntityId = refEntityId
                  OriginalName = originalName
                  ExternalDbType = externalDbType
                  DeleteRule = deleteRule
                  PhysicalCol = physicalCol
                  Description = description
                  Order = order }
        }

    let private writeReferenceRow (jw: Utf8JsonWriter) (r: MetadataSnapshotRunner.OssysReferenceRow) =
        wInt jw "attrId" r.AttrId
        wIntOpt jw "refEntityId" r.RefEntityId
        wStrOpt jw "refEntityName" r.RefEntityName
        wStrOpt jw "refPhysicalName" r.RefPhysicalName

    let private readReferenceRow (el: JsonElement) : Result<MetadataSnapshotRunner.OssysReferenceRow> =
        result {
            let! attrId = field el "attrId" asInt
            let! refEntityId = optField el "refEntityId" asInt
            let! refEntityName = optField el "refEntityName" asString
            let! refPhysicalName = optField el "refPhysicalName" asString
            return
                { AttrId = attrId
                  RefEntityId = refEntityId
                  RefEntityName = refEntityName
                  RefPhysicalName = refPhysicalName }
        }

    let private writePhysicalTableRow (jw: Utf8JsonWriter) (r: MetadataSnapshotRunner.OssysPhysicalTableRow) =
        wInt jw "entityId" r.EntityId
        wStr jw "schemaName" r.SchemaName
        wStr jw "tableName" r.TableName
        wInt jw "objectId" r.ObjectId

    let private readPhysicalTableRow (el: JsonElement) : Result<MetadataSnapshotRunner.OssysPhysicalTableRow> =
        result {
            let! entityId = field el "entityId" asInt
            let! schemaName = field el "schemaName" asString
            let! tableName = field el "tableName" asString
            let! objectId = field el "objectId" asInt
            return
                { EntityId = entityId
                  SchemaName = schemaName
                  TableName = tableName
                  ObjectId = objectId }
        }

    let private writeColumnRealityRow (jw: Utf8JsonWriter) (r: MetadataSnapshotRunner.OssysColumnRealityRow) =
        wInt jw "attrId" r.AttrId
        wBool jw "isNullable" r.IsNullable
        wStrOpt jw "sqlType" r.SqlType
        wIntOpt jw "maxLength" r.MaxLength
        wIntOpt jw "precision" r.Precision
        wIntOpt jw "scale" r.Scale
        wStrOpt jw "collationName" r.CollationName
        wBool jw "isIdentity" r.IsIdentity
        wBool jw "isComputed" r.IsComputed
        wStrOpt jw "computedDefinition" r.ComputedDefinition
        wStrOpt jw "defaultConstraintName" r.DefaultConstraintName
        wStrOpt jw "defaultDefinition" r.DefaultDefinition
        wStrOpt jw "physicalColumn" r.PhysicalColumn
        wBool jw "isPersisted" r.IsPersisted

    let private readColumnRealityRow (el: JsonElement) : Result<MetadataSnapshotRunner.OssysColumnRealityRow> =
        result {
            let! attrId = field el "attrId" asInt
            let! isNullable = field el "isNullable" asBool
            let! sqlType = optField el "sqlType" asString
            let! maxLength = optField el "maxLength" asInt
            let! precision = optField el "precision" asInt
            let! scale = optField el "scale" asInt
            let! collationName = optField el "collationName" asString
            let! isIdentity = field el "isIdentity" asBool
            let! isComputed = field el "isComputed" asBool
            let! computedDefinition = optField el "computedDefinition" asString
            let! defaultConstraintName = optField el "defaultConstraintName" asString
            let! defaultDefinition = optField el "defaultDefinition" asString
            let! physicalColumn = optField el "physicalColumn" asString
            let! isPersisted = field el "isPersisted" asBool
            return
                { AttrId = attrId
                  IsNullable = isNullable
                  SqlType = sqlType
                  MaxLength = maxLength
                  Precision = precision
                  Scale = scale
                  CollationName = collationName
                  IsIdentity = isIdentity
                  IsComputed = isComputed
                  ComputedDefinition = computedDefinition
                  DefaultConstraintName = defaultConstraintName
                  DefaultDefinition = defaultDefinition
                  PhysicalColumn = physicalColumn
                  IsPersisted = isPersisted }
        }

    let private writeColumnCheckRow (jw: Utf8JsonWriter) (r: MetadataSnapshotRunner.OssysColumnCheckRow) =
        wInt jw "attrId" r.AttrId
        wStr jw "constraintName" r.ConstraintName
        wStrOpt jw "definition" r.Definition
        wBool jw "isNotTrusted" r.IsNotTrusted

    let private readColumnCheckRow (el: JsonElement) : Result<MetadataSnapshotRunner.OssysColumnCheckRow> =
        result {
            let! attrId = field el "attrId" asInt
            let! constraintName = field el "constraintName" asString
            let! definition = optField el "definition" asString
            let! isNotTrusted = field el "isNotTrusted" asBool
            return
                { AttrId = attrId
                  ConstraintName = constraintName
                  Definition = definition
                  IsNotTrusted = isNotTrusted }
        }

    let private writePhysColsPresentRow (jw: Utf8JsonWriter) (r: MetadataSnapshotRunner.OssysPhysColsPresentRow) =
        wInt jw "attrId" r.AttrId

    let private readPhysColsPresentRow (el: JsonElement) : Result<MetadataSnapshotRunner.OssysPhysColsPresentRow> =
        result {
            let! attrId = field el "attrId" asInt
            return ({ AttrId = attrId } : MetadataSnapshotRunner.OssysPhysColsPresentRow)
        }

    let private writeAllIdxRow (jw: Utf8JsonWriter) (r: MetadataSnapshotRunner.OssysAllIdxRow) =
        wInt jw "entityId" r.EntityId
        wInt jw "objectId" r.ObjectId
        wInt jw "indexId" r.IndexId
        wStr jw "indexName" r.IndexName
        wBool jw "isUnique" r.IsUnique
        wBool jw "isPrimary" r.IsPrimary
        wStr jw "kind" r.Kind
        wStrOpt jw "filterDefinition" r.FilterDefinition
        wBool jw "isDisabled" r.IsDisabled
        wBool jw "isPadded" r.IsPadded
        wInt jw "fillFactor" r.FillFactor
        wBool jw "ignoreDupKey" r.IgnoreDupKey
        wBool jw "allowRowLocks" r.AllowRowLocks
        wBool jw "allowPageLocks" r.AllowPageLocks
        wBool jw "noRecompute" r.NoRecompute
        wStrOpt jw "dataSpaceName" r.DataSpaceName
        wStrOpt jw "dataSpaceType" r.DataSpaceType
        wStrOpt jw "partitionColumnsJson" r.PartitionColumnsJson
        wStrOpt jw "dataCompressionJson" r.DataCompressionJson

    let private readAllIdxRow (el: JsonElement) : Result<MetadataSnapshotRunner.OssysAllIdxRow> =
        result {
            let! entityId = field el "entityId" asInt
            let! objectId = field el "objectId" asInt
            let! indexId = field el "indexId" asInt
            let! indexName = field el "indexName" asString
            let! isUnique = field el "isUnique" asBool
            let! isPrimary = field el "isPrimary" asBool
            let! kind = field el "kind" asString
            let! filterDefinition = optField el "filterDefinition" asString
            let! isDisabled = field el "isDisabled" asBool
            let! isPadded = field el "isPadded" asBool
            let! fillFactor = field el "fillFactor" asInt
            let! ignoreDupKey = field el "ignoreDupKey" asBool
            let! allowRowLocks = field el "allowRowLocks" asBool
            let! allowPageLocks = field el "allowPageLocks" asBool
            let! noRecompute = field el "noRecompute" asBool
            let! dataSpaceName = optField el "dataSpaceName" asString
            let! dataSpaceType = optField el "dataSpaceType" asString
            let! partitionColumnsJson = optField el "partitionColumnsJson" asString
            let! dataCompressionJson = optField el "dataCompressionJson" asString
            return
                { EntityId = entityId
                  ObjectId = objectId
                  IndexId = indexId
                  IndexName = indexName
                  IsUnique = isUnique
                  IsPrimary = isPrimary
                  Kind = kind
                  FilterDefinition = filterDefinition
                  IsDisabled = isDisabled
                  IsPadded = isPadded
                  FillFactor = fillFactor
                  IgnoreDupKey = ignoreDupKey
                  AllowRowLocks = allowRowLocks
                  AllowPageLocks = allowPageLocks
                  NoRecompute = noRecompute
                  DataSpaceName = dataSpaceName
                  DataSpaceType = dataSpaceType
                  PartitionColumnsJson = partitionColumnsJson
                  DataCompressionJson = dataCompressionJson }
        }

    let private writeIdxColRow (jw: Utf8JsonWriter) (r: MetadataSnapshotRunner.OssysIdxColMappedRow) =
        wInt jw "entityId" r.EntityId
        wStr jw "indexName" r.IndexName
        wInt jw "ordinal" r.Ordinal
        wStrOpt jw "physicalColumn" r.PhysicalColumn
        wBool jw "isIncluded" r.IsIncluded
        wStrOpt jw "direction" r.Direction
        wStrOpt jw "humanAttr" r.HumanAttr

    let private readIdxColRow (el: JsonElement) : Result<MetadataSnapshotRunner.OssysIdxColMappedRow> =
        result {
            let! entityId = field el "entityId" asInt
            let! indexName = field el "indexName" asString
            let! ordinal = field el "ordinal" asInt
            let! physicalColumn = optField el "physicalColumn" asString
            let! isIncluded = field el "isIncluded" asBool
            let! direction = optField el "direction" asString
            let! humanAttr = optField el "humanAttr" asString
            return
                { EntityId = entityId
                  IndexName = indexName
                  Ordinal = ordinal
                  PhysicalColumn = physicalColumn
                  IsIncluded = isIncluded
                  Direction = direction
                  HumanAttr = humanAttr }
        }

    let private writeFkRealityRow (jw: Utf8JsonWriter) (r: MetadataSnapshotRunner.OssysFkRealityRow) =
        wInt jw "entityId" r.EntityId
        wInt jw "fkObjectId" r.FkObjectId
        wStr jw "fkName" r.FkName
        wStrOpt jw "deleteAction" r.DeleteAction
        wStrOpt jw "updateAction" r.UpdateAction
        wInt jw "referencedObjectId" r.ReferencedObjectId
        wIntOpt jw "referencedEntityId" r.ReferencedEntityId
        wStrOpt jw "referencedSchema" r.ReferencedSchema
        wStrOpt jw "referencedTable" r.ReferencedTable
        wBool jw "isNoCheck" r.IsNoCheck

    let private readFkRealityRow (el: JsonElement) : Result<MetadataSnapshotRunner.OssysFkRealityRow> =
        result {
            let! entityId = field el "entityId" asInt
            let! fkObjectId = field el "fkObjectId" asInt
            let! fkName = field el "fkName" asString
            let! deleteAction = optField el "deleteAction" asString
            let! updateAction = optField el "updateAction" asString
            let! referencedObjectId = field el "referencedObjectId" asInt
            let! referencedEntityId = optField el "referencedEntityId" asInt
            let! referencedSchema = optField el "referencedSchema" asString
            let! referencedTable = optField el "referencedTable" asString
            let! isNoCheck = field el "isNoCheck" asBool
            return
                { EntityId = entityId
                  FkObjectId = fkObjectId
                  FkName = fkName
                  DeleteAction = deleteAction
                  UpdateAction = updateAction
                  ReferencedObjectId = referencedObjectId
                  ReferencedEntityId = referencedEntityId
                  ReferencedSchema = referencedSchema
                  ReferencedTable = referencedTable
                  IsNoCheck = isNoCheck }
        }

    let private writeFkColumnRow (jw: Utf8JsonWriter) (r: MetadataSnapshotRunner.OssysFkColumnRow) =
        wInt jw "entityId" r.EntityId
        wInt jw "fkObjectId" r.FkObjectId
        wInt jw "ordinal" r.Ordinal
        wStr jw "parentColumn" r.ParentColumn
        wStr jw "referencedColumn" r.ReferencedColumn
        wIntOpt jw "parentAttrId" r.ParentAttrId
        wStrOpt jw "parentAttrName" r.ParentAttrName
        wIntOpt jw "referencedAttrId" r.ReferencedAttrId
        wStrOpt jw "referencedAttrName" r.ReferencedAttrName

    let private readFkColumnRow (el: JsonElement) : Result<MetadataSnapshotRunner.OssysFkColumnRow> =
        result {
            let! entityId = field el "entityId" asInt
            let! fkObjectId = field el "fkObjectId" asInt
            let! ordinal = field el "ordinal" asInt
            let! parentColumn = field el "parentColumn" asString
            let! referencedColumn = field el "referencedColumn" asString
            let! parentAttrId = optField el "parentAttrId" asInt
            let! parentAttrName = optField el "parentAttrName" asString
            let! referencedAttrId = optField el "referencedAttrId" asInt
            let! referencedAttrName = optField el "referencedAttrName" asString
            return
                { EntityId = entityId
                  FkObjectId = fkObjectId
                  Ordinal = ordinal
                  ParentColumn = parentColumn
                  ReferencedColumn = referencedColumn
                  ParentAttrId = parentAttrId
                  ParentAttrName = parentAttrName
                  ReferencedAttrId = referencedAttrId
                  ReferencedAttrName = referencedAttrName }
        }

    let private writeTriggerRow (jw: Utf8JsonWriter) (r: MetadataSnapshotRunner.OssysTriggerRow) =
        wInt jw "entityId" r.EntityId
        wStr jw "triggerName" r.TriggerName
        wBool jw "isDisabled" r.IsDisabled
        wStrOpt jw "triggerDefinition" r.TriggerDefinition

    let private readTriggerRow (el: JsonElement) : Result<MetadataSnapshotRunner.OssysTriggerRow> =
        result {
            let! entityId = field el "entityId" asInt
            let! triggerName = field el "triggerName" asString
            let! isDisabled = field el "isDisabled" asBool
            let! triggerDefinition = optField el "triggerDefinition" asString
            return
                { EntityId = entityId
                  TriggerName = triggerName
                  IsDisabled = isDisabled
                  TriggerDefinition = triggerDefinition }
        }

    let private writeSequenceRow (jw: Utf8JsonWriter) (r: MetadataSnapshotRunner.OssysSequenceRow) =
        wStr jw "schema" r.Schema
        wStr jw "name" r.Name
        wStr jw "dataType" r.DataType
        wDecOpt jw "startValue" r.StartValue
        wDecOpt jw "increment" r.Increment
        wDecOpt jw "minimumValue" r.MinimumValue
        wDecOpt jw "maximumValue" r.MaximumValue
        wBool jw "isCycling" r.IsCycling
        wBool jw "isCached" r.IsCached
        wIntOpt jw "cacheSize" r.CacheSize

    let private readSequenceRow (el: JsonElement) : Result<MetadataSnapshotRunner.OssysSequenceRow> =
        result {
            let! schema = field el "schema" asString
            let! name = field el "name" asString
            let! dataType = field el "dataType" asString
            let! startValue = optField el "startValue" asDecimal
            let! increment = optField el "increment" asDecimal
            let! minimumValue = optField el "minimumValue" asDecimal
            let! maximumValue = optField el "maximumValue" asDecimal
            let! isCycling = field el "isCycling" asBool
            let! isCached = field el "isCached" asBool
            let! cacheSize = optField el "cacheSize" asInt
            return
                { Schema = schema
                  Name = name
                  DataType = dataType
                  StartValue = startValue
                  Increment = increment
                  MinimumValue = minimumValue
                  MaximumValue = maximumValue
                  IsCycling = isCycling
                  IsCached = isCached
                  CacheSize = cacheSize }
        }

    let private writeTemporalRow (jw: Utf8JsonWriter) (r: MetadataSnapshotRunner.OssysTemporalRow) =
        wInt jw "entityId" r.EntityId
        wStrOpt jw "historySchema" r.HistorySchema
        wStrOpt jw "historyTable" r.HistoryTable
        wStrOpt jw "periodStart" r.PeriodStart
        wStrOpt jw "periodEnd" r.PeriodEnd
        wIntOpt jw "retentionValue" r.RetentionValue
        wStrOpt jw "retentionUnit" r.RetentionUnit

    let private readTemporalRow (el: JsonElement) : Result<MetadataSnapshotRunner.OssysTemporalRow> =
        result {
            let! entityId = field el "entityId" asInt
            let! historySchema = optField el "historySchema" asString
            let! historyTable = optField el "historyTable" asString
            let! periodStart = optField el "periodStart" asString
            let! periodEnd = optField el "periodEnd" asString
            let! retentionValue = optField el "retentionValue" asInt
            let! retentionUnit = optField el "retentionUnit" asString
            return
                { EntityId = entityId
                  HistorySchema = historySchema
                  HistoryTable = historyTable
                  PeriodStart = periodStart
                  PeriodEnd = periodEnd
                  RetentionValue = retentionValue
                  RetentionUnit = retentionUnit }
        }

    let private writeCapabilityRow (jw: Utf8JsonWriter) (r: MetadataSnapshotRunner.OssysCapabilityRow) =
        wBool jw "hasDataType" r.HasDataType
        wBool jw "hasType" r.HasType
        wBool jw "hasPrecision" r.HasPrecision
        wBool jw "hasScale" r.HasScale
        wBool jw "hasDecimals" r.HasDecimals
        wBool jw "hasOriginalName" r.HasOriginalName
        wBool jw "hasExternalColumnType" r.HasExternalColumnType
        wBool jw "hasPhysicalColumnName" r.HasPhysicalColumnName
        wBool jw "hasDatabaseName" r.HasDatabaseName
        wBool jw "hasIsIdentifier" r.HasIsIdentifier
        wBool jw "hasRefEntityId" r.HasRefEntityId
        wBool jw "hasIsAutoNumber" r.HasIsAutoNumber
        wBool jw "hasDefaultValue" r.HasDefaultValue
        wBool jw "hasDeleteRule" r.HasDeleteRule
        wBool jw "hasOriginalType" r.HasOriginalType
        wBool jw "hasAttrSsKey" r.HasAttrSsKey
        wBool jw "hasLength" r.HasLength
        wBool jw "hasOrderNum" r.HasOrderNum
        wBool jw "hasEntityDescription" r.HasEntityDescription

    let private readCapabilityRow (el: JsonElement) : Result<MetadataSnapshotRunner.OssysCapabilityRow> =
        result {
            let! hasDataType = field el "hasDataType" asBool
            let! hasType = field el "hasType" asBool
            let! hasPrecision = field el "hasPrecision" asBool
            let! hasScale = field el "hasScale" asBool
            let! hasDecimals = field el "hasDecimals" asBool
            let! hasOriginalName = field el "hasOriginalName" asBool
            let! hasExternalColumnType = field el "hasExternalColumnType" asBool
            let! hasPhysicalColumnName = field el "hasPhysicalColumnName" asBool
            let! hasDatabaseName = field el "hasDatabaseName" asBool
            let! hasIsIdentifier = field el "hasIsIdentifier" asBool
            let! hasRefEntityId = field el "hasRefEntityId" asBool
            let! hasIsAutoNumber = field el "hasIsAutoNumber" asBool
            let! hasDefaultValue = field el "hasDefaultValue" asBool
            let! hasDeleteRule = field el "hasDeleteRule" asBool
            let! hasOriginalType = field el "hasOriginalType" asBool
            let! hasAttrSsKey = field el "hasAttrSsKey" asBool
            let! hasLength = field el "hasLength" asBool
            let! hasOrderNum = field el "hasOrderNum" asBool
            let! hasEntityDescription = field el "hasEntityDescription" asBool
            return
                { HasDataType = hasDataType
                  HasType = hasType
                  HasPrecision = hasPrecision
                  HasScale = hasScale
                  HasDecimals = hasDecimals
                  HasOriginalName = hasOriginalName
                  HasExternalColumnType = hasExternalColumnType
                  HasPhysicalColumnName = hasPhysicalColumnName
                  HasDatabaseName = hasDatabaseName
                  HasIsIdentifier = hasIsIdentifier
                  HasRefEntityId = hasRefEntityId
                  HasIsAutoNumber = hasIsAutoNumber
                  HasDefaultValue = hasDefaultValue
                  HasDeleteRule = hasDeleteRule
                  HasOriginalType = hasOriginalType
                  HasAttrSsKey = hasAttrSsKey
                  HasLength = hasLength
                  HasOrderNum = hasOrderNum
                  HasEntityDescription = hasEntityDescription }
        }

    // ------------------------------------------------------------------
    // The document.
    // ------------------------------------------------------------------

    /// Serialize a snapshot to indented JSON text. Deterministic: fixed
    /// field order, pinned writer options, invariant-culture decimals.
    let serialize (s: MetadataSnapshotRunner.MetadataSnapshot) : string =
        JsonWriting.writeToString (fun jw ->
            jw.WriteStartObject()
            jw.WriteNumber("codecVersion", version)
            wRows jw "modules" writeModuleRow s.Modules
            wRows jw "entities" writeEntityRow s.Entities
            wRows jw "attributes" writeAttributeRow s.Attributes
            wRows jw "references" writeReferenceRow s.References
            wRows jw "physicalTables" writePhysicalTableRow s.PhysicalTables
            wRows jw "columnReality" writeColumnRealityRow s.ColumnReality
            wRows jw "columnChecks" writeColumnCheckRow s.ColumnChecks
            wRows jw "physColsPresent" writePhysColsPresentRow s.PhysColsPresent
            wRows jw "indexes" writeAllIdxRow s.Indexes
            wRows jw "indexColumns" writeIdxColRow s.IndexColumns
            wRows jw "foreignKeysReality" writeFkRealityRow s.ForeignKeysReality
            wRows jw "foreignKeyColumns" writeFkColumnRow s.ForeignKeyColumns
            wRows jw "triggers" writeTriggerRow s.Triggers
            wRows jw "sequences" writeSequenceRow s.Sequences
            wRows jw "temporal" writeTemporalRow s.Temporal
            wRows jw "capabilities" writeCapabilityRow s.Capabilities
            jw.WriteEndObject())

    /// The `serialize` sibling that stays in UTF-8 (for a future embedder
    /// whose destination is itself a `Utf8JsonWriter` — the
    /// `LifecycleStore` raw-embed precedent).
    let serializeUtf8 (s: MetadataSnapshotRunner.MetadataSnapshot) : byte[] =
        System.Text.Encoding.UTF8.GetBytes(serialize s)

    /// Deserialize snapshot JSON. Structural rebuild only; fail-closed:
    /// malformed documents, unknown versions, missing rowset arrays, and
    /// mistyped fields all land as named `sink.codec.*` errors, never
    /// exceptions.
    let deserialize (text: string) : Result<MetadataSnapshotRunner.MetadataSnapshot> =
        let parsed =
            try
                Ok (JsonDocument.Parse text)
            with ex ->
                fail "sink.codec.malformedJson" (sprintf "snapshot JSON did not parse: %s" ex.Message)
        parsed
        |> Result.bind (fun doc ->
            use doc = doc
            let root = doc.RootElement
            if root.ValueKind <> JsonValueKind.Object then
                fail "sink.codec.expectedObject" (sprintf "expected a JSON object, got %A" root.ValueKind)
            else
                result {
                    let! ver = field root "codecVersion" asInt
                    do!
                        if ver = version then Ok ()
                        else fail "sink.codec.versionUnsupported" (sprintf "snapshot codecVersion %d is not the supported %d" ver version)
                    let! modules = reqList root "modules" readModuleRow
                    let! entities = reqList root "entities" readEntityRow
                    let! attributes = reqList root "attributes" readAttributeRow
                    let! references = reqList root "references" readReferenceRow
                    let! physicalTables = reqList root "physicalTables" readPhysicalTableRow
                    let! columnReality = reqList root "columnReality" readColumnRealityRow
                    let! columnChecks = reqList root "columnChecks" readColumnCheckRow
                    let! physColsPresent = reqList root "physColsPresent" readPhysColsPresentRow
                    let! indexes = reqList root "indexes" readAllIdxRow
                    let! indexColumns = reqList root "indexColumns" readIdxColRow
                    let! foreignKeysReality = reqList root "foreignKeysReality" readFkRealityRow
                    let! foreignKeyColumns = reqList root "foreignKeyColumns" readFkColumnRow
                    let! triggers = reqList root "triggers" readTriggerRow
                    let! sequences = reqList root "sequences" readSequenceRow
                    let! temporal = reqList root "temporal" readTemporalRow
                    let! capabilities = reqList root "capabilities" readCapabilityRow
                    return
                        ({ Modules = modules
                           Entities = entities
                           Attributes = attributes
                           References = references
                           PhysicalTables = physicalTables
                           ColumnReality = columnReality
                           ColumnChecks = columnChecks
                           PhysColsPresent = physColsPresent
                           Indexes = indexes
                           IndexColumns = indexColumns
                           ForeignKeysReality = foreignKeysReality
                           ForeignKeyColumns = foreignKeyColumns
                           Triggers = triggers
                           Sequences = sequences
                           Temporal = temporal
                           Capabilities = capabilities } : MetadataSnapshotRunner.MetadataSnapshot)
                })
