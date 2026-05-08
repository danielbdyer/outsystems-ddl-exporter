/// DACPAC → V2 Catalog adapter (chapter 3, session 27 — first
/// substantive slice of the canary chapter).
///
/// Reads a DACPAC artifact (a zip-of-XML produced by SSDT or by
/// DacpacEmitter when it lands at slice 5+) and translates the
/// DacFx-loaded model into a V2 `Catalog`. The adapter is the
/// **read-side** of the canary's round-trip closure (chapter-3
/// strategic-frame axis 1).
///
/// Pure-data parse: `DacPackage.Load(stream)` + `TSqlModel.LoadFromDacpac`
/// + `model.GetObjects` parse the artifact as a structured zip with
/// no SQL Server connection. Testcontainers stay deferred until the
/// canary loop closes end-to-end (chapter-3 axis 6).
///
/// Slice-1 scope: single-module placeholder ("Pipeline"); one
/// physical Table per Kind; columns map to attributes (PK + nullable
/// flag); Origin defaults to `OsNative`; no FKs, no indexes, no
/// modality. SsKey synthesis mirrors the OSSYS convention exactly
/// (chapter-3 axis 3 — synthesis-convention stability):
///   - `OS_MOD_<modName>`,
///   - `OS_KIND_<modName>_<entName>`,
///   - `OS_ATTR_<modName>_<entName>_<attrName>`.
///
/// Subsequent slices extend the parser surface under empirical
/// pressure (slice 2: FKs; slice 3: indexes; slice 4: composite PKs
/// + multi-Module fixture; slice 5: DacpacEmitter; slice 6: round-
/// trip closure offline; slice 7+: testcontainers).
namespace Projection.Adapters.Dacpac

open System
open System.IO
open Microsoft.SqlServer.Dac
open Microsoft.SqlServer.Dac.Model
open Projection.Core

// `Index` is both a V2 IR type (Projection.Core.Catalog.Index) and a
// DacFx model class (Microsoft.SqlServer.Dac.Model.Index). The latest
// `open` wins; V2's Index resolves bare. Alias DacFx's Index so the
// adapter can name the DacFx side without ambiguity.
type private DacIndex = Microsoft.SqlServer.Dac.Model.Index

[<RequireQualifiedAccess>]
module CatalogReader =

    /// Source of DACPAC bytes — file path or in-memory bytes. The
    /// shape mirrors the OSSYS adapter's `SnapshotSource` DU.
    /// Closed-DU expansion empirical-test discipline: adding a
    /// variant should produce F# exhaustiveness errors only at
    /// match sites in this module.
    type DacpacSource =
        | DacpacFile of path: string
        | DacpacBytes of bytes: byte[]

    /// Slice-1 placeholder module name. Chapter-3 axis 7 (Module →
    /// Schema mapping) is open under fixture pressure; for slice 1
    /// every parsed DACPAC produces a single-module Catalog with
    /// this fixed module name. Refines when the first multi-module
    /// canary fixture forces the question.
    [<Literal>]
    let private SliceOnePlaceholderModule = "Pipeline"

    let private adapterError (code: string) (message: string) : ValidationError =
        ValidationError.create (sprintf "adapter.dacpac.%s" code) message

    // -----------------------------------------------------------------------
    // SsKey synthesis — mirrors OSSYS (chapter-3 axis 3).
    // The duplication is load-bearing: the canary loop closes only
    // when both adapters use the symbolically-identical synthesis.
    // Future round-trip slices stress this; if the conventions ever
    // drift, the round-trip test surfaces it. Two-consumer threshold
    // for extraction: first consumer = OSSYS adapter; second consumer
    // = this adapter. Extraction defers to a shared `Synthesis`
    // module if the round-trip slice (slice 6) confirms the symbolic
    // identity is exactly right; for now, local definitions with
    // the same shape.
    // -----------------------------------------------------------------------

    let private moduleSsKey (moduleName: string) : Result<SsKey> =
        SsKey.original (sprintf "OS_MOD_%s" moduleName)

    let private kindSsKey (moduleName: string) (entityName: string) : Result<SsKey> =
        SsKey.original (sprintf "OS_KIND_%s_%s" moduleName entityName)

    let private attributeSsKey
        (moduleName: string) (entityName: string) (attrName: string)
        : Result<SsKey> =
        SsKey.original (sprintf "OS_ATTR_%s_%s_%s" moduleName entityName attrName)

    let private referenceSsKey
        (sourceModuleName: string) (sourceEntityName: string) (viaAttrName: string)
        : Result<SsKey> =
        SsKey.original (sprintf "OS_REF_%s_%s_%s" sourceModuleName sourceEntityName viaAttrName)

    let private indexSsKey
        (moduleName: string) (entityName: string) (indexName: string)
        : Result<SsKey> =
        SsKey.original (sprintf "OS_IDX_%s_%s_%s" moduleName entityName indexName)

    // -----------------------------------------------------------------------
    // DacFx primitive helpers — wrap the C# exception-throwing API
    // surface with `Result<'a>`. Adapter-language rule: foreign-API
    // I/O lives at the boundary; the value-typed seam is the
    // adapter's outer signature.
    // -----------------------------------------------------------------------

    let private tryWith (code: string) (message: string) (f: unit -> 'a) : Result<'a> =
        try Result.success (f ())
        with ex -> Result.failureOf (adapterError code (sprintf "%s: %s" message ex.Message))

    /// Materialize bytes to a temp .dacpac path. DacFx's stream-based
    /// loaders are awkward across version boundaries; the file-path
    /// constructor is the stable path. Caller is responsible for
    /// cleanup (paired with `File.Delete` in a `try/finally`).
    let private writeBytesToTempFile (bytes: byte[]) : Result<string> =
        tryWith "tempFileWrite" "Failed to materialize DACPAC bytes to temp file" (fun () ->
            let path = Path.GetTempFileName()
            File.WriteAllBytes(path, bytes)
            path)

    let private loadModel (path: string) : Result<TSqlModel> =
        tryWith "modelLoad"
                (sprintf "Failed to load TSqlModel from '%s'" path)
                (fun () -> new TSqlModel(path, DacSchemaModelStorageType.Memory))

    // -----------------------------------------------------------------------
    // DacFx → V2 type translation. Slice 1 maps the data types
    // that the slice-1 fixture exercises (Int / NVarChar). Subsequent
    // slices extend the table under empirical pressure (mirrors
    // OSSYS adapter's parsePrimitiveType discipline).
    // -----------------------------------------------------------------------

    /// Map a SQL type name (DacFx's `DataType` referenced object's
    /// name) to a V2 `PrimitiveType`. The mapping table grows under
    /// fixture pressure.
    let private parsePrimitiveType (sqlTypeName: string) : Result<PrimitiveType> =
        match sqlTypeName.ToLowerInvariant() with
        | "int" | "bigint" | "smallint" | "tinyint" -> Result.success Integer
        | "nvarchar" | "varchar" | "nchar" | "char" | "text" | "ntext" ->
            Result.success Text
        | other ->
            Result.failureOf (
                adapterError
                    "unmappedSqlType"
                    (sprintf "SQL type '%s' has no V2 PrimitiveType mapping yet." other))

    /// Map DacFx's `ForeignKeyAction` enum to V2's `ReferenceAction`.
    /// DacFx exposes NoAction / Cascade / SetNull / SetDefault; V2's
    /// IR has NoAction / Cascade / SetNull / Restrict. SetDefault
    /// has no V2 mapping today (V1 does not surface it; per
    /// rules-under-empirical-pressure, fail until a fixture forces
    /// a decision). Restrict is reserved-but-unproduced from this
    /// adapter (mirrors OSSYS — `parseDeleteRule` never emits it).
    let private parseReferenceAction (action: ForeignKeyAction) : Result<ReferenceAction> =
        match action with
        | ForeignKeyAction.NoAction   -> Result.success NoAction
        | ForeignKeyAction.Cascade    -> Result.success Cascade
        | ForeignKeyAction.SetNull    -> Result.success SetNull
        | ForeignKeyAction.SetDefault ->
            Result.failureOf (
                adapterError
                    "unmappedFkAction"
                    "ForeignKeyAction.SetDefault has no V2 ReferenceAction mapping yet.")
        | other ->
            Result.failureOf (
                adapterError
                    "unmappedFkAction"
                    (sprintf "ForeignKeyAction '%A' has no V2 ReferenceAction mapping yet." other))

    // -----------------------------------------------------------------------
    // TSqlObject → IR translation. Each Table → Kind; each Column →
    // Attribute. PK detection via the table's PrimaryKeyConstraint
    // referencing relationship.
    // -----------------------------------------------------------------------

    /// Extract the schema-qualified table name from a Table TSqlObject.
    /// DacFx names use `ObjectIdentifier.Parts`; for tables, parts
    /// are `[schema; table]`.
    let private parseTableName (tableObj: TSqlObject) : Result<string * string> =
        let parts = tableObj.Name.Parts
        if parts.Count = 2
        then Result.success (parts.[0], parts.[1])
        else
            Result.failureOf (
                adapterError
                    "tableName"
                    (sprintf
                        "Table name has %d parts; expected 2 (schema, table). Parts: %s"
                        parts.Count
                        (String.concat "." (parts |> Seq.toArray))))

    /// Build the set of PK column names for a table by walking the
    /// PrimaryKeyConstraint (if present). Returns an empty set when
    /// no PK is declared (DacpacEmitter slice 5+ enforces PK
    /// presence; the read-side adapter is permissive on input).
    let private primaryKeyColumnNames (tableObj: TSqlObject) : Set<string> =
        tableObj.GetReferencing(PrimaryKeyConstraint.Host)
        |> Seq.collect (fun pk ->
            pk.GetReferenced(PrimaryKeyConstraint.Columns)
            |> Seq.choose (fun col ->
                let parts = col.Name.Parts
                if parts.Count >= 1 then Some parts.[parts.Count - 1] else None))
        |> Set.ofSeq

    let private parseColumn
        (moduleName: string) (entityName: string)
        (pkColumns: Set<string>) (columnObj: TSqlObject)
        : Result<Attribute> =
        let parts = columnObj.Name.Parts
        if parts.Count < 1 then
            Result.failureOf (
                adapterError
                    "columnName"
                    (sprintf
                        "Column on '%s.%s' has no name parts."
                        moduleName entityName))
        else
            let columnName = parts.[parts.Count - 1]
            let isNullable = columnObj.GetProperty<bool>(Column.Nullable)
            let isPrimaryKey = pkColumns.Contains(columnName)
            let isMandatory = not isNullable
            let dataTypeRefs =
                columnObj.GetReferenced(Column.DataType) |> Seq.toList
            match dataTypeRefs with
            | [dataTypeObj] ->
                let typeNameParts = dataTypeObj.Name.Parts
                if typeNameParts.Count < 1 then
                    Result.failureOf (
                        adapterError
                            "columnTypeNameMissing"
                            (sprintf
                                "Column '%s' on '%s.%s' has a data type with no name parts."
                                columnName moduleName entityName))
                else
                    let sqlTypeName = typeNameParts.[typeNameParts.Count - 1]
                    let attrKey = attributeSsKey moduleName entityName columnName
                    let attrName = Name.create columnName
                    let primitive = parsePrimitiveType sqlTypeName
                    match attrKey, attrName, primitive with
                    | Success k, Success n, Success p ->
                        Result.success
                            { SsKey        = k
                              Name         = n
                              Type         = p
                              Column       = { ColumnName = columnName; IsNullable = isNullable }
                              IsPrimaryKey = isPrimaryKey
                              IsMandatory  = isMandatory }
                    | Failure es, _, _ -> Failure es
                    | _, Failure es, _ -> Failure es
                    | _, _, Failure es -> Failure es
            | [] ->
                Result.failureOf (
                    adapterError
                        "columnTypeMissing"
                        (sprintf
                            "Column '%s' on '%s.%s' has no DataType reference."
                            columnName moduleName entityName))
            | _ ->
                Result.failureOf (
                    adapterError
                        "columnTypeAmbiguous"
                        (sprintf
                            "Column '%s' on '%s.%s' has multiple DataType references."
                            columnName moduleName entityName))

    /// Extract the bare name (last part) from a DacFx
    /// `ObjectIdentifier`. DacFx's qualifier varies by object kind
    /// (columns are `[schema].[table].[col]`; indexes are
    /// `[schema].[table].[idx]`); the bare name is always the last
    /// part.
    let private bareName (obj: TSqlObject) : string option =
        let parts = obj.Name.Parts
        if parts.Count >= 1 then Some parts.[parts.Count - 1] else None

    /// Translate one DacFx FK constraint to a V2 `Reference`.
    /// Slice-2 scope: single-source-column FKs only. Multi-column
    /// FKs surface as `multiColumnFk` Failure (V2's IR models
    /// `Reference.SourceAttribute : SsKey` as a single attribute;
    /// the multi-column case requires either an IR refinement or
    /// per-column splitting — defers to empirical pressure when a
    /// fixture forces the question).
    ///
    /// Cross-schema / cross-module FK targets surface with a
    /// `parsedTargetSchema`-vs-`SliceOnePlaceholderModule`
    /// mismatch question; for slice 2 the resolution mirrors the
    /// single-module assumption (target's V2 module = source's
    /// V2 module = SliceOnePlaceholderModule). The multi-Module
    /// fixture (slice 4+) refines.
    let private parseReference
        (moduleName: string) (entityName: string) (fkObj: TSqlObject)
        : Result<Reference> =
        let sourceCols = fkObj.GetReferenced(ForeignKeyConstraint.Columns) |> Seq.toList
        let foreignTables = fkObj.GetReferenced(ForeignKeyConstraint.ForeignTable) |> Seq.toList
        match sourceCols, foreignTables with
        | [srcCol], [tgtTable] ->
            match bareName srcCol, bareName tgtTable with
            | Some srcColName, Some tgtTableName ->
                let action = fkObj.GetProperty<ForeignKeyAction>(ForeignKeyConstraint.DeleteAction)
                let onDelete    = parseReferenceAction action
                let refKey      = referenceSsKey moduleName entityName srcColName
                let refName     = Name.create srcColName
                let srcAttrKey  = attributeSsKey moduleName entityName srcColName
                let tgtKindKey  = kindSsKey moduleName tgtTableName
                match refKey, refName, srcAttrKey, tgtKindKey, onDelete with
                | Success rKey, Success rName, Success sKey, Success tKey, Success rule ->
                    Result.success
                        { SsKey           = rKey
                          Name            = rName
                          SourceAttribute = sKey
                          TargetKind      = tKey
                          OnDelete        = rule }
                | Failure es, _, _, _, _ -> Failure es
                | _, Failure es, _, _, _ -> Failure es
                | _, _, Failure es, _, _ -> Failure es
                | _, _, _, Failure es, _ -> Failure es
                | _, _, _, _, Failure es -> Failure es
            | _ ->
                Result.failureOf (
                    adapterError
                        "fkNamePartsMissing"
                        (sprintf
                            "Foreign key on '%s.%s' has missing column or target name parts."
                            moduleName entityName))
        | xs, _ when xs.Length <> 1 ->
            Result.failureOf (
                adapterError
                    "multiColumnFk"
                    (sprintf
                        "Foreign key on '%s.%s' spans %d source columns; V2's per-attribute reference shape models single-column FKs only (rules-under-empirical-pressure)."
                        moduleName entityName xs.Length))
        | _ ->
            Result.failureOf (
                adapterError
                    "fkTargetMissing"
                    (sprintf
                        "Foreign key on '%s.%s' has no resolvable target table."
                        moduleName entityName))

    /// Translate one DacFx Index to a V2 `Index`. Slice-3 scope:
    /// non-PK indexes (PKs are `PrimaryKeyConstraint` in DacFx,
    /// not `Index`); single-column unique, composite, non-unique.
    /// The V2 `Columns` field carries SsKeys in DacFx-declared
    /// column order.
    let private parseIndex
        (moduleName: string) (entityName: string) (idxObj: TSqlObject)
        : Result<Index> =
        match bareName idxObj with
        | None ->
            Result.failureOf (
                adapterError
                    "indexName"
                    (sprintf
                        "Index on '%s.%s' has no name parts."
                        moduleName entityName))
        | Some indexName ->
            let isUnique = idxObj.GetProperty<bool>(DacIndex.Unique)
            let columnObjs = idxObj.GetReferenced(DacIndex.Columns) |> Seq.toList
            let columnKeyResults =
                columnObjs
                |> List.map (fun col ->
                    match bareName col with
                    | Some cn -> attributeSsKey moduleName entityName cn
                    | None ->
                        Result.failureOf (
                            adapterError
                                "indexColumnName"
                                (sprintf
                                    "Index '%s' on '%s.%s' has a column with no name parts."
                                    indexName moduleName entityName)))
            let foldedCols =
                columnKeyResults
                |> List.fold
                    (fun acc next ->
                        match acc, next with
                        | Success xs, Success x -> Result.success (xs @ [x])
                        | Failure es, _         -> Failure es
                        | _, Failure es         -> Failure es)
                    (Result.success [])
            let idxKey  = indexSsKey moduleName entityName indexName
            let idxName = Name.create indexName
            match idxKey, idxName, foldedCols with
            | Success k, Success n, Success cols ->
                Result.success
                    { SsKey        = k
                      Name         = n
                      Columns      = cols
                      IsUnique     = isUnique
                      IsPrimaryKey = false }
            | Failure es, _, _ -> Failure es
            | _, Failure es, _ -> Failure es
            | _, _, Failure es -> Failure es

    /// Fold a list of `Result<'a>` into a `Result<'a list>` with
    /// first-failure-wins semantics. Mirrors the OSSYS adapter's
    /// fold pattern; small helper extracted on its second consumer
    /// (parseTable consumes it for attrs / refs / indexes).
    let private collect (rs: Result<'a> list) : Result<'a list> =
        rs
        |> List.fold
            (fun acc next ->
                match acc, next with
                | Success xs, Success x -> Result.success (xs @ [x])
                | Failure es, _         -> Failure es
                | _, Failure es         -> Failure es)
            (Result.success [])

    /// Test whether two TSqlObjects refer to the same model element by
    /// comparing their fully-qualified `Name.Parts`. DacFx's reference-
    /// equality semantics are not reliable across `GetParent()` calls;
    /// name-equality is the stable surface for "is this index parented
    /// to this table?"-style questions.
    let private sameObject (a: TSqlObject) (b: TSqlObject) : bool =
        if isNull (box a) || isNull (box b) then false
        else
            let pa, pb = a.Name.Parts, b.Name.Parts
            pa.Count = pb.Count
            && Seq.forall2 (=) pa pb

    /// Enumerate all indexes whose parent is the given table. DacFx's
    /// `Index` is a top-level model object (its name parts include
    /// schema + table + index); we filter `model.GetObjects(...,
    /// DacIndex.TypeClass)` by `GetParent`.
    let private indexesOnTable (model: TSqlModel) (tableObj: TSqlObject) : TSqlObject list =
        model.GetObjects(DacQueryScopes.UserDefined, DacIndex.TypeClass)
        |> Seq.filter (fun idx ->
            let parent = idx.GetParent()
            sameObject parent tableObj)
        |> Seq.toList

    let private parseTable
        (model: TSqlModel) (moduleName: string) (tableObj: TSqlObject)
        : Result<Kind> =
        match parseTableName tableObj with
        | Failure es -> Failure es
        | Success (schema, tableName) ->
            let pkCols = primaryKeyColumnNames tableObj
            let foldedAttrs =
                tableObj.GetReferenced(Table.Columns)
                |> Seq.toList
                |> List.map (parseColumn moduleName tableName pkCols)
                |> collect
            let foldedRefs =
                tableObj.GetReferencing(ForeignKeyConstraint.Host)
                |> Seq.toList
                |> List.map (parseReference moduleName tableName)
                |> collect
            let foldedIdx =
                indexesOnTable model tableObj
                |> List.map (parseIndex moduleName tableName)
                |> collect
            let kindKey  = kindSsKey moduleName tableName
            let kindName = Name.create tableName
            match kindKey, kindName, foldedAttrs, foldedRefs, foldedIdx with
            | Success k, Success n, Success attrs, Success refs, Success idxs ->
                Result.success
                    { SsKey      = k
                      Name       = n
                      Origin     = OsNative
                      Modality   = []
                      Physical   = { Schema = schema; Table = tableName }
                      Attributes = attrs
                      References = refs
                      Indexes    = idxs }
            // Preserve inner Failure codes (carries forward the
            // session-26 lesson — diagnostic codes must survive the
            // outer cascade rather than collapsing to a generic
            // tableBuild error).
            | Failure es, _, _, _, _ -> Failure es
            | _, Failure es, _, _, _ -> Failure es
            | _, _, Failure es, _, _ -> Failure es
            | _, _, _, Failure es, _ -> Failure es
            | _, _, _, _, Failure es -> Failure es

    // -----------------------------------------------------------------------
    // Top-level parse — TSqlModel → V2 Catalog. Slice 1 single
    // placeholder module; subsequent slices extend.
    // -----------------------------------------------------------------------

    let private parseModel (model: TSqlModel) : Result<Catalog> =
        let moduleName = SliceOnePlaceholderModule
        let foldedKinds =
            model.GetObjects(DacQueryScopes.UserDefined, Table.TypeClass)
            |> Seq.toList
            |> List.map (parseTable model moduleName)
            |> collect
        let modKey  = moduleSsKey moduleName
        let modName = Name.create moduleName
        match modKey, modName, foldedKinds with
        | Success k, Success n, Success kinds ->
            Result.success
                { Modules = [
                    { SsKey = k; Name = n; Kinds = kinds } ] }
        | Failure es, _, _ -> Failure es
        | _, Failure es, _ -> Failure es
        | _, _, Failure es -> Failure es

    /// Parse a DACPAC artifact into a V2 `Catalog`.
    ///
    /// The bytes/file is materialized to a temp path (DacFx's stable
    /// stream API surface across versions), loaded into a TSqlModel
    /// in `Memory` storage, enumerated, translated, and the model is
    /// disposed. The temp file is cleaned up regardless of outcome.
    let parse (source: DacpacSource) : Result<Catalog> =
        let pathResult, cleanup =
            match source with
            | DacpacFile path -> Result.success path, fun () -> ()
            | DacpacBytes bytes ->
                match writeBytesToTempFile bytes with
                | Success p -> Result.success p, fun () -> try File.Delete(p) with _ -> ()
                | Failure es -> Failure es, fun () -> ()
        try
            match pathResult with
            | Failure es -> Failure es
            | Success path ->
                match loadModel path with
                | Failure es -> Failure es
                | Success model ->
                    use _ = model
                    parseModel model
        finally
            cleanup ()
