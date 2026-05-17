namespace Projection.Adapters.OssysSql

open System
open System.Data
open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Projection.Core
open Projection.Adapters.Osm

/// V2's metadata-snapshot runner. Carbon-copies V1's `MetadataSnapshotRunner`
/// (`Osm.Pipeline.SqlExtraction.MetadataSnapshotRunner`) at a much smaller
/// surface: V1 layers `IDbConnectionFactory` + `IDbCommandExecutor` +
/// per-processor abstractions over a generic V1-domain pipeline; V2 ships
/// a direct `SqlConnection`-receiving function because V2's runner is the
/// canary's offline-extraction surface — it walks the carbon-copied SQL's
/// 22 result sets, parses the first 5 into typed F# records mirroring V1's
/// DTOs, and assembles a `CatalogReader.RowsetBundle` consumable by V2's
/// existing `CatalogReader.parse` JSON / rowset adapter.
///
/// **Chapter 5.0 slice γ.** The canary's bootstrap+extract flow:
///   1. Caller deploys `MetadataExtractionSql.readEdgeCaseSeed()` against
///      a clean SQL Server database (creates synthetic OSSYS schema +
///      edge-case data).
///   2. Caller invokes `MetadataSnapshotRunner.runAsync` with an open
///      `SqlConnection` to that database + the V1 parameters.
///   3. This module reads `MetadataExtractionSql.read()` (the rowsets SQL),
///      executes it via `SqlCommand`, and walks `DbDataReader.NextResultAsync`
///      to enumerate all 22 result sets.
///   4. The first 5 result sets (Modules / Entities / Attributes /
///      References / PhysicalTables) parse into typed F# records; the
///      remaining 17 are skipped (the SQL still emits them but V2's
///      current consumption surface is the narrow 4-rowset
///      `CatalogReader.RowsetBundle`).
///   5. Slice δ composes the typed records into the `RowsetBundle` via
///      JOIN logic (PhysicalTables → KindRow.DbSchema; ForeignKey reality
///      → ReferenceRow.DeleteRuleCode / HasDbConstraint).
///
/// The runner does not own the connection lifecycle; the caller opens +
/// disposes. T1 byte-determinism preserved: the SQL is deterministic
/// (no `NEWID()` or `GETDATE()` in user-visible projections per V1's
/// script); result-set ordering is fixed by the script.
[<RequireQualifiedAccess>]
module MetadataSnapshotRunner =

    /// Parameters for V1's rowsets SQL script. Mirrors the 5 declared
    /// parameters at the head of `outsystems_metadata_rowsets.sql`:
    ///   - `ModuleNames`: CSV of module (eSpace) names to include.
    ///     Empty = all modules.
    ///   - `IncludeSystem`: include system modules (e.g., `SystemUsers`).
    ///   - `IncludeInactive`: include inactive entities.
    ///   - `OnlyActiveAttributes`: skip attributes with `Is_Active = 0`.
    ///   - `EntityFilterJson`: per-module entity allow-list as JSON.
    ///     Null = no per-module filter.
    type SnapshotParameters =
        {
            ModuleNames          : string list
            IncludeSystem        : bool
            IncludeInactive      : bool
            OnlyActiveAttributes : bool
            EntityFilterJson     : string option
        }

    /// Default parameter shape: all modules, all entities, all attributes,
    /// include system + inactive. The "show me everything" stance — useful
    /// for the canary and for first-extract baselines.
    let defaultParameters : SnapshotParameters =
        {
            ModuleNames          = []
            IncludeSystem        = true
            IncludeInactive      = true
            OnlyActiveAttributes = false
            EntityFilterJson     = None
        }

    /// V1-shaped typed rowsets parsed from the first 5 result sets.
    /// These mirror V1's `Outsystems*Row` DTOs at the columns V2's
    /// `CatalogReader.RowsetBundle` consumes (with the JOIN composition
    /// happening in slice δ).
    type OssysModuleRow =
        { EspaceId       : int
          EspaceName     : string
          IsSystemModule : bool
          IsActive       : bool
          EspaceKind     : string option
          EspaceSsKey    : Guid option }

    type OssysEntityRow =
        { EntityId          : int
          EntityName        : string
          PhysicalTableName : string
          EspaceId          : int
          IsActive          : bool
          IsSystemEntity    : bool
          IsExternal        : bool
          DataKind          : string option
          PrimaryKeySsKey   : Guid option
          EntitySsKey       : Guid option
          Description       : string option }

    type OssysAttributeRow =
        { AttrId            : int
          EntityId          : int
          AttrName          : string
          AttrSsKey         : Guid option
          DataType          : string option
          Length            : int option
          Precision         : int option
          Scale             : int option
          IsMandatory       : bool
          IsActive          : bool
          IsAutoNumber      : bool
          IsIdentifier      : bool
          RefEntityId       : int option
          OriginalName      : string option
          ExternalDbType    : string option
          DeleteRule        : string option
          PhysicalCol       : string
          Description       : string option }

    type OssysReferenceRow =
        { AttrId          : int
          RefEntityId     : int option
          RefEntityName   : string option
          RefPhysicalName : string option }

    type OssysPhysicalTableRow =
        { EntityId   : int
          SchemaName : string
          TableName  : string
          ObjectId   : int }

    /// Aggregate snapshot: the 5 V2-consumed rowsets in their V1-shaped
    /// typed form. Slice δ composes this into `CatalogReader.RowsetBundle`.
    type MetadataSnapshot =
        {
            Modules        : OssysModuleRow list
            Entities       : OssysEntityRow list
            Attributes     : OssysAttributeRow list
            References     : OssysReferenceRow list
            PhysicalTables : OssysPhysicalTableRow list
        }

    // -------------------------------------------------------------------
    // Internal helpers — typed SqlDataReader column readers. Pattern
    // mirrors V1's `Column.StringOrNull` etc. but uses ordinal-indexed
    // access for performance + F# idioms.
    // -------------------------------------------------------------------

    let private readString (reader: SqlDataReader) (ordinal: int) : string =
        if reader.IsDBNull(ordinal) then
            invalidOp (sprintf "MetadataSnapshotRunner: required column at ordinal %d was NULL" ordinal)
        else
            reader.GetString(ordinal)

    let private readStringOpt (reader: SqlDataReader) (ordinal: int) : string option =
        if reader.IsDBNull(ordinal) then None
        else Some (reader.GetString(ordinal))

    let private readInt (reader: SqlDataReader) (ordinal: int) : int =
        // V1 sometimes returns int via flexible widening (Int16 / Int64);
        // SqlDataReader.GetInt32 throws on type mismatch. Use Convert to
        // tolerate width variation.
        let value = reader.GetValue(ordinal)
        System.Convert.ToInt32(value)

    let private readIntOpt (reader: SqlDataReader) (ordinal: int) : int option =
        if reader.IsDBNull(ordinal) then None
        else Some (readInt reader ordinal)

    let private readBool (reader: SqlDataReader) (ordinal: int) : bool =
        let value = reader.GetValue(ordinal)
        match value with
        | :? bool as b -> b
        | :? byte as b -> b <> 0uy
        | :? int as i  -> i <> 0
        | _ -> System.Convert.ToBoolean(value)

    let private readBoolOpt (reader: SqlDataReader) (ordinal: int) : bool option =
        if reader.IsDBNull(ordinal) then None
        else Some (readBool reader ordinal)

    let private readGuidOpt (reader: SqlDataReader) (ordinal: int) : Guid option =
        if reader.IsDBNull(ordinal) then None
        else Some (reader.GetGuid(ordinal))

    /// Read all rows of the current result set via `mapper`; advance to
    /// the next result set when complete. Returns the rows in source
    /// order.
    let private readResultSet<'T>
            (reader: SqlDataReader)
            (mapper: SqlDataReader -> 'T)
            : Task<'T list> =
        task {
            let acc = ResizeArray<'T>()
            let mutable hasMore = true
            while hasMore do
                let! advanced = reader.ReadAsync()
                if advanced then acc.Add(mapper reader)
                else hasMore <- false
            return List.ofSeq acc
        }

    /// Skip the current result set without parsing any rows. Used for the
    /// 17 result sets V2 doesn't yet consume; `NextResultAsync` advances
    /// past them.
    let private skipResultSet (reader: SqlDataReader) : Task<unit> =
        task {
            let mutable hasMore = true
            while hasMore do
                let! advanced = reader.ReadAsync()
                if not advanced then hasMore <- false
            return ()
        }

    let private mapModuleRow (r: SqlDataReader) : OssysModuleRow =
        { EspaceId       = readInt r 0
          EspaceName     = readString r 1
          IsSystemModule = readBool r 2
          IsActive       = readBool r 3
          EspaceKind     = readStringOpt r 4
          EspaceSsKey    = readGuidOpt r 5 }

    let private mapEntityRow (r: SqlDataReader) : OssysEntityRow =
        { EntityId          = readInt r 0
          EntityName        = readString r 1
          PhysicalTableName = readString r 2
          EspaceId          = readInt r 3
          IsActive          = readBool r 4
          IsSystemEntity    = readBool r 5
          IsExternal        = readBool r 6
          DataKind          = readStringOpt r 7
          PrimaryKeySsKey   = readGuidOpt r 8
          EntitySsKey       = readGuidOpt r 9
          Description       = readStringOpt r 10 }

    let private mapAttributeRow (r: SqlDataReader) : OssysAttributeRow =
        { AttrId         = readInt r 0
          EntityId       = readInt r 1
          AttrName       = readString r 2
          AttrSsKey      = readGuidOpt r 3
          DataType       = readStringOpt r 4
          Length         = readIntOpt r 5
          Precision      = readIntOpt r 6
          Scale          = readIntOpt r 7
          // ordinal 8 is DefaultValue (string?) — not consumed by V2
          // RowsetBundle today; skipped.
          IsMandatory    = readBool r 9
          IsActive       = readBool r 10
          IsAutoNumber   = match readBoolOpt r 11 with Some b -> b | None -> false
          IsIdentifier   = match readBoolOpt r 12 with Some b -> b | None -> false
          RefEntityId    = readIntOpt r 13
          OriginalName   = readStringOpt r 14
          ExternalDbType = readStringOpt r 15
          DeleteRule     = readStringOpt r 16
          PhysicalCol    =
              // ordinal 17 = PhysicalColumnName; V1 reads it as
              // nullable but V2 requires non-null for `KindRow.PhysicalCol`.
              // Fall back to AttrName when V1 source omits.
              match readStringOpt r 17 with
              | Some n when not (System.String.IsNullOrWhiteSpace n) -> n
              | _ -> (readString r 2).ToUpperInvariant()
          Description    = readStringOpt r 22 }

    let private mapReferenceRow (r: SqlDataReader) : OssysReferenceRow =
        { AttrId          = readInt r 0
          RefEntityId     = readIntOpt r 1
          RefEntityName   = readStringOpt r 2
          RefPhysicalName = readStringOpt r 3 }

    let private mapPhysicalTableRow (r: SqlDataReader) : OssysPhysicalTableRow =
        { EntityId   = readInt r 0
          SchemaName = readString r 1
          TableName  = readString r 2
          ObjectId   = readInt r 3 }

    /// Execute the carbon-copied rowsets SQL against `cnn` (already open)
    /// with the supplied parameters. Walks all 22 result sets;
    /// parses the first 5 into typed records and skips the remaining 17.
    /// Returns a `MetadataSnapshot` carrying the 5 V2-relevant rowsets.
    ///
    /// **Determinism.** The SQL script is deterministic by construction
    /// (V1's pillar 1 / T1 commitment); parameter inputs + database state
    /// fully determine the output. Caller is responsible for fixing the
    /// database state (e.g., applying `readEdgeCaseSeed()` first) when
    /// determinism across runs matters.
    let runAsync (cnn: SqlConnection) (parameters: SnapshotParameters)
            : Task<Result<MetadataSnapshot>> =
        task {
            try
                let script = MetadataExtractionSql.read()
                use command = new SqlCommand(script, cnn)
                command.CommandType <- CommandType.Text
                command.CommandTimeout <- 0  // unlimited; V1's SET TEXTSIZE -1 + complex queries can run long
                let moduleCsv =
                    parameters.ModuleNames |> String.concat ","
                command.Parameters.AddWithValue("@ModuleNamesCsv", box moduleCsv)
                |> ignore
                command.Parameters.AddWithValue("@IncludeSystem", box parameters.IncludeSystem)
                |> ignore
                command.Parameters.AddWithValue("@IncludeInactive", box parameters.IncludeInactive)
                |> ignore
                command.Parameters.AddWithValue("@OnlyActiveAttributes", box parameters.OnlyActiveAttributes)
                |> ignore
                let entityFilterParam = SqlParameter("@EntityFilterJson", SqlDbType.NVarChar, -1)
                entityFilterParam.Value <-
                    match parameters.EntityFilterJson with
                    | Some j -> j :> obj
                    | None -> System.DBNull.Value :> obj
                command.Parameters.Add(entityFilterParam) |> ignore

                use! reader = command.ExecuteReaderAsync(CommandBehavior.SequentialAccess)
                // The V1 script also emits diagnostic prints; result sets
                // are emitted in fixed order starting with the validation
                // sanity-check first. We rely on the script's documented
                // contract: the 22 user-visible result sets begin at the
                // SELECT statements at the script's tail (rowsets 0..21).
                //
                // Note: V1's `SqlClientOutsystemsMetadataReader` skips
                // PRINT messages by enumerating all readers regardless of
                // the row shape; we mirror that by reading every result
                // set sequentially.
                let! modules        = readResultSet reader mapModuleRow
                let! _              = reader.NextResultAsync()
                let! entities       = readResultSet reader mapEntityRow
                let! _              = reader.NextResultAsync()
                let! attributes     = readResultSet reader mapAttributeRow
                let! _              = reader.NextResultAsync()
                let! references     = readResultSet reader mapReferenceRow
                let! _              = reader.NextResultAsync()
                let! physicalTables = readResultSet reader mapPhysicalTableRow
                // Skip the remaining 17 result sets — V2's RowsetBundle
                // doesn't yet consume them. Future slice ε can expand.
                let mutable hasMore = true
                while hasMore do
                    let! advanced = reader.NextResultAsync()
                    if not advanced then hasMore <- false
                    else do! skipResultSet reader
                return Result.success {
                    Modules        = modules
                    Entities       = entities
                    Attributes     = attributes
                    References     = references
                    PhysicalTables = physicalTables
                }
            with
            | ex ->
                return Result.failureOf (
                    ValidationError.create
                        "adapter.ossysSql.runFailed"
                        (sprintf "MetadataSnapshotRunner.runAsync failed: %s" ex.Message))
        }

    /// Compose the typed snapshot into V2's `CatalogReader.RowsetBundle`.
    /// JOIN logic:
    ///   - Each `OssysEntityRow` produces one `KindRow` joined against the
    ///     `OssysPhysicalTableRow` by EntityId for the `DbSchema` value.
    ///     When the physical-table row is absent, defaults to `"dbo"`.
    ///   - Each `OssysAttributeRow` produces one `AttributeRow`.
    ///   - Each `OssysReferenceRow` produces one `ReferenceRow` with the
    ///     DeleteRule lifted from the joined Attribute (V1 carries it on
    ///     the attribute; V2's `ReferenceRow` carries it on the reference).
    ///     `HasDbConstraint` defaults to `true` when an FK is reflected
    ///     in the references rowset.
    let toBundle (snapshot: MetadataSnapshot) : CatalogReader.RowsetBundle =
        let physicalByEntity =
            snapshot.PhysicalTables
            |> List.map (fun pt -> pt.EntityId, pt)
            |> Map.ofList
        let attributeById =
            snapshot.Attributes
            |> List.map (fun a -> a.AttrId, a)
            |> Map.ofList

        let modules =
            snapshot.Modules
            |> List.map (fun m ->
                {
                    EspaceId       = m.EspaceId
                    EspaceName     = m.EspaceName
                    IsSystemModule = m.IsSystemModule
                    IsActive       = m.IsActive
                    EspaceKind     = m.EspaceKind
                    EspaceSsKey    = m.EspaceSsKey
                } : CatalogReader.ModuleRow)

        let kinds =
            snapshot.Entities
            |> List.map (fun e ->
                let dbSchema =
                    match Map.tryFind e.EntityId physicalByEntity with
                    | Some pt -> pt.SchemaName
                    | None -> "dbo"
                {
                    EntityId          = e.EntityId
                    EspaceId          = e.EspaceId
                    EntityName        = e.EntityName
                    PhysicalTableName = e.PhysicalTableName
                    DbSchema          = dbSchema
                    IsStatic          = false
                    IsExternal        = e.IsExternal
                    IsSystemEntity    = e.IsSystemEntity
                    IsActive          = e.IsActive
                    EntitySsKey       = e.EntitySsKey
                    PrimaryKeySsKey   = e.PrimaryKeySsKey
                    Description       = e.Description
                } : CatalogReader.KindRow)

        let attributes =
            snapshot.Attributes
            |> List.map (fun a ->
                {
                    AttrId               = a.AttrId
                    EntityId             = a.EntityId
                    AttrName             = a.AttrName
                    PhysicalCol          = a.PhysicalCol
                    DataType             = match a.DataType with Some s -> s | None -> "Text"
                    IsMandatory          = a.IsMandatory
                    IsIdentifier         = a.IsIdentifier
                    IsAutoNumber         = a.IsAutoNumber
                    Length               = a.Length
                    Precision            = a.Precision
                    Scale                = a.Scale
                    AttrSsKey            = a.AttrSsKey
                    IsActive             = a.IsActive
                    Description          = a.Description
                    OriginalName         = a.OriginalName
                    ExternalDatabaseType = a.ExternalDbType
                } : CatalogReader.AttributeRow)

        let references =
            snapshot.References
            |> List.choose (fun r ->
                match r.RefEntityName, Map.tryFind r.AttrId attributeById with
                | Some refName, Some attr ->
                    Some
                        ({
                            AttrId          = r.AttrId
                            RefEntityName   = refName
                            RefEntityId     = r.RefEntityId
                            DeleteRuleCode  = attr.DeleteRule
                            HasDbConstraint = true
                        } : CatalogReader.ReferenceRow)
                | _ -> None)

        {
            Modules    = modules
            Kinds      = kinds
            Attributes = attributes
            References = references
        }
