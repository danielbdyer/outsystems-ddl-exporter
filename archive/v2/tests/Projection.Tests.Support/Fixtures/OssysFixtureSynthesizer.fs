namespace Projection.Tests.SourceFixtures

open System
open System.Text
open Projection.Core
open Projection.Tests.SourceFixtures

/// Synthesizes the OSSYS-metadata seed describing the schema that
/// `FixtureGenerator.generate spec` emits. The carbon-copied OSSYS
/// rowsets SQL (`outsystems_metadata_rowsets.sql`) reads from
/// `dbo.ossys_Espace`, `dbo.ossys_Entity`, `dbo.ossys_Entity_Attr` plus
/// reflection of `sys.columns` / `sys.indexes` / `sys.triggers` /
/// `sys.foreign_keys`. The DDL (which is FixtureGenerator's existing
/// output) populates the latter; this module populates the former.
///
/// **Determinism.** Each entity / attribute gets a deterministic
/// integer ID derived from the entity's emission index and the spec's
/// seed, so the synthesized seed is byte-identical for any given
/// `GeneratedFixture`.
///
/// **Slice A.4.7'-prelude.comprehensive-canary (2026-05-19).** Pillar 9
/// classification: `DataIntent` — purely describes data the synthesizer
/// observes from the generator's output; carries no operator opinion.
[<RequireQualifiedAccess>]
module OssysFixtureSynthesizer =

    /// Build the OSSYS-schema DDL (the three `ossys_*` tables). The
    /// schema is taken from the carbon-copied edge-case seed's CREATE
    /// TABLE blocks — same column layout so the rowsets SQL reads
    /// them identically.
    let private ossysSchemaDdl : string =
        """
CREATE TABLE [dbo].[ossys_Espace]
(
    [Id] INT NOT NULL PRIMARY KEY,
    [Name] NVARCHAR(256) NOT NULL,
    [Is_System] BIT NOT NULL,
    [Is_Active] BIT NOT NULL,
    [EspaceKind] NVARCHAR(50) NULL,
    [SS_Key] UNIQUEIDENTIFIER NULL
);
GO

CREATE TABLE [dbo].[ossys_Entity]
(
    [Id] INT NOT NULL PRIMARY KEY,
    [Name] NVARCHAR(256) NOT NULL,
    [Physical_Table_Name] NVARCHAR(256) NOT NULL,
    [Espace_Id] INT NOT NULL,
    [Is_Active] BIT NOT NULL,
    [Is_System] BIT NOT NULL,
    [Is_External] BIT NOT NULL,
    [Data_Kind] NVARCHAR(50) NULL,
    [PrimaryKey_SS_Key] UNIQUEIDENTIFIER NULL,
    [SS_Key] UNIQUEIDENTIFIER NULL,
    [Description] NVARCHAR(MAX) NULL,
    CONSTRAINT FK_ossys_Entity_Espace FOREIGN KEY ([Espace_Id]) REFERENCES [dbo].[ossys_Espace]([Id])
);
GO

CREATE TABLE [dbo].[ossys_Entity_Attr]
(
    [Id] INT NOT NULL PRIMARY KEY,
    [Entity_Id] INT NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [SS_Key] UNIQUEIDENTIFIER NULL,
    [Data_Type] NVARCHAR(200) NULL,
    [Length] INT NULL,
    [Precision] INT NULL,
    [Scale] INT NULL,
    [Default_Value] NVARCHAR(MAX) NULL,
    [Is_Mandatory] BIT NOT NULL,
    [Is_Active] BIT NOT NULL,
    [Is_AutoNumber] BIT NULL,
    [Is_Identifier] BIT NULL,
    [Referenced_Entity_Id] INT NULL,
    [Original_Name] NVARCHAR(200) NULL,
    [External_Column_Type] NVARCHAR(200) NULL,
    [Delete_Rule] NVARCHAR(50) NULL,
    [Physical_Column_Name] NVARCHAR(200) NULL,
    [Database_Name] NVARCHAR(200) NULL,
    [Legacy_Type] NVARCHAR(200) NULL,
    [Decimals] INT NULL,
    [Original_Type] NVARCHAR(200) NULL,
    [Description] NVARCHAR(MAX) NULL,
    CONSTRAINT FK_ossys_Entity_Attr_Entity FOREIGN KEY ([Entity_Id]) REFERENCES [dbo].[ossys_Entity]([Id])
);
GO
"""

    /// Deterministic Espace ID = 100 + moduleIndex. Numbers above 100
    /// match the edge-case seed's convention.
    let private espaceId (moduleIndex: int) : int = 100 + moduleIndex

    /// Deterministic Entity ID = 10000 + emissionIndex. Avoids
    /// collision with the synthetic IDs the edge-case seed uses
    /// (1000-50000 range).
    let private entityId (emissionIndex: int) : int =
        100000 + emissionIndex

    /// Deterministic Attribute ID. Composite from
    /// (entityId, ordinal) so collisions across entities don't
    /// happen.
    let private attrId (entityIdValue: int) (ordinal: int) : int =
        entityIdValue * 100 + ordinal

    /// Synthesize a deterministic GUID from a seed-prefixed string.
    /// We reuse `Guid.Parse` on a hex-formatted SHA1 prefix; pure +
    /// deterministic + byte-stable across runs.
    let private deterministicGuid (key: string) : Guid =
        use sha = System.Security.Cryptography.SHA1.Create()
        let bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes key)
        // GUID is 16 bytes; SHA1 is 20 — take first 16.
        let trimmed : byte[] = Array.sub bytes 0 16
        System.Guid trimmed

    /// Map a SQL Server type string (FixtureGenerator's `pickColumnType`
    /// output) to V1's OSSYS Data_Type / Length / Precision / Scale
    /// triple. V1's data-type vocabulary is a small set (`Text`,
    /// `Integer`, `LongInteger`, `Boolean`, `DateTime`, `Date`,
    /// `Decimal`, `Identifier`, `BinaryData`, `Currency`); the V2
    /// CatalogReader maps these back via `SqlTypeCorrespondence`.
    /// This synthesizer collapses the SQL-type vocabulary onto V1's
    /// OSSYS axis so the rowset adapter sees a consistent metadata
    /// view.
    let private classifyOssysType (sqlType: string) (isIdentifier: bool)
            : string * int option * int option * int option =
        let upper = sqlType.ToUpperInvariant()
        if isIdentifier then ("Identifier", None, None, None)
        elif upper.StartsWith "NVARCHAR" || upper.StartsWith "VARCHAR" then
            // Try to extract the length.
            let openIdx = upper.IndexOf '('
            let closeIdx = upper.IndexOf ')'
            let length =
                if openIdx > 0 && closeIdx > openIdx then
                    let inner = upper.Substring(openIdx + 1, closeIdx - openIdx - 1).Trim()
                    if inner = "MAX" then Some -1
                    else
                        match Int32.TryParse inner with
                        | true, n -> Some n
                        | false, _ -> None
                else None
            ("Text", length, None, None)
        elif upper = "INT" then ("Integer", None, None, None)
        elif upper = "BIGINT" then ("LongInteger", None, None, None)
        elif upper = "BIT" then ("Boolean", None, None, None)
        elif upper.StartsWith "DATETIME" then ("DateTime", None, None, None)
        elif upper = "DATE" then ("Date", None, None, None)
        elif upper.StartsWith "DECIMAL" then
            let openIdx = upper.IndexOf '('
            let closeIdx = upper.IndexOf ')'
            let p, s =
                if openIdx > 0 && closeIdx > openIdx then
                    let inner = upper.Substring(openIdx + 1, closeIdx - openIdx - 1)
                    let parts = inner.Split ','
                    let pVal =
                        match Int32.TryParse (parts.[0].Trim()) with
                        | true, n -> Some n
                        | _ -> None
                    let sVal =
                        if parts.Length > 1 then
                            match Int32.TryParse (parts.[1].Trim()) with
                            | true, n -> Some n
                            | _ -> None
                        else None
                    pVal, sVal
                else None, None
            ("Decimal", None, p, s)
        elif upper = "UNIQUEIDENTIFIER" then
            // V1's `Identifier` maps Identifier OS-types onto GUID; for
            // non-PK columns the closest V1 type is `Text` since
            // OSSYS-native fixtures don't ship GUID columns. We use
            // `Text` for SS_KEY-style columns so the V2 adapter doesn't
            // mis-classify them as references.
            ("Text", Some 36, None, None)
        elif upper.StartsWith "VARBINARY" then ("BinaryData", None, None, None)
        else ("Text", Some 255, None, None)

    /// Emit one row of an INSERT VALUES list. Quotes values so they
    /// land cleanly as a multi-row INSERT.
    let private sqlInt (n: int) = string n
    let private sqlNullableInt (n: int option) =
        match n with
        | Some v -> string v
        | None -> "NULL"
    let private sqlBit (b: bool) = if b then "1" else "0"
    let private sqlString (s: string) =
        // T-SQL N'...' with single-quote escaping.
        sprintf "N'%s'" (s.Replace("'", "''"))
    let private sqlNullableString (s: string option) =
        match s with
        | Some v -> sqlString v
        | None -> "NULL"
    let private sqlGuid (g: Guid) =
        sprintf "'%s'" (g.ToString "D")
    let private sqlNullableGuid (g: Guid option) =
        match g with
        | Some v -> sqlGuid v
        | None -> "NULL"

    /// Resolve a physical-table name back to its EntityId. Returns
    /// `None` when the synthesizer doesn't know the table (e.g., FK
    /// targets that escaped the generator).
    let private buildEntityIdByPhysicalTable
            (entities: GeneratedEntityModel list) : Map<string, int> =
        entities
        |> List.mapi (fun idx e -> e.PhysicalTable, entityId idx)
        |> Map.ofList

    /// Synthesize the per-fixture OSSYS-metadata seed (the
    /// `ossys_Espace` / `ossys_Entity` / `ossys_Entity_Attr` INSERTs).
    /// Returns the SQL text ready to deploy alongside the
    /// FixtureGenerator's DDL.
    let synthesize (fixture: GeneratedFixture) : string =
        use _ = Bench.scope "canary.comprehensive.synthesize"
        let sb = StringBuilder(64 * 1024)
        sb.AppendLine ossysSchemaDdl |> ignore

        // Determine module count from the entity models.
        let moduleIndexes =
            fixture.Entities
            |> List.map (fun e -> e.ModuleIndex)
            |> List.distinct
            |> List.sort

        // -----------------------------------------------------------------
        // ossys_Espace — one row per module
        // -----------------------------------------------------------------
        if not (List.isEmpty moduleIndexes) then
            sb.AppendLine "INSERT INTO [dbo].[ossys_Espace] ([Id], [Name], [Is_System], [Is_Active], [EspaceKind], [SS_Key]) VALUES" |> ignore
            let last = List.last moduleIndexes
            moduleIndexes
            |> List.iter (fun mi ->
                let moduleName = sprintf "Module%02d" (mi + 1)
                let espId = espaceId mi
                let guid = deterministicGuid (sprintf "espace:%d" mi)
                let suffix = if mi = last then ";" else ","
                sb.AppendLine
                    (sprintf "    (%d, %s, 0, 1, N'eSpace', %s)%s"
                        espId
                        (sqlString moduleName)
                        (sqlGuid guid)
                        suffix)
                |> ignore)
            sb.AppendLine "GO" |> ignore

        // -----------------------------------------------------------------
        // ossys_Entity — one row per generated entity
        // -----------------------------------------------------------------
        if not (List.isEmpty fixture.Entities) then
            sb.AppendLine "INSERT INTO [dbo].[ossys_Entity] ([Id], [Name], [Physical_Table_Name], [Espace_Id], [Is_Active], [Is_System], [Is_External], [Data_Kind], [PrimaryKey_SS_Key], [SS_Key], [Description]) VALUES" |> ignore
            let lastIdx = fixture.Entities.Length - 1
            fixture.Entities
            |> List.iteri (fun idx e ->
                let eId = entityId idx
                let espId = espaceId e.ModuleIndex
                let pkGuid = deterministicGuid (sprintf "entity-pk:%d:%s" idx e.PhysicalTable)
                let entGuid = deterministicGuid (sprintf "entity:%d:%s" idx e.PhysicalTable)
                let suffix = if idx = lastIdx then ";" else ","
                sb.AppendLine
                    (sprintf "    (%d, %s, %s, %d, 1, 0, 0, N'entity', %s, %s, NULL)%s"
                        eId
                        (sqlString e.EntityName)
                        (sqlString e.PhysicalTable)
                        espId
                        (sqlGuid pkGuid)
                        (sqlGuid entGuid)
                        suffix)
                |> ignore)
            sb.AppendLine "GO" |> ignore

        // -----------------------------------------------------------------
        // ossys_Entity_Attr — one row per attribute, with FK resolution
        // -----------------------------------------------------------------
        let physicalTableToEntityId = buildEntityIdByPhysicalTable fixture.Entities

        // Emit attributes in chunks of <= 500 to keep INSERT statements
        // bounded.
        let allAttrs =
            fixture.Entities
            |> List.mapi (fun entIdx e ->
                let eId = entityId entIdx
                e.Attributes
                |> List.mapi (fun attrOrdinal attr ->
                    let aId = attrId eId attrOrdinal
                    let dataType, length, precision, scale =
                        classifyOssysType attr.SqlType attr.IsIdentifier
                    let refEntityId =
                        attr.ReferencedTable
                        |> Option.bind (fun tbl -> Map.tryFind tbl physicalTableToEntityId)
                    let deleteRule =
                        if attr.ReferencedTable.IsSome then Some "Protect" else None
                    let attrGuid = deterministicGuid (sprintf "attr:%d:%s" aId attr.PhysicalName)
                    let attrName =
                        // V1 OSSYS names are typically PascalCase; collapse
                        // SCREAMING_SNAKE to PascalCase for the human name.
                        let lower = attr.PhysicalName.ToLowerInvariant()
                        let parts = lower.Split([| '_' |], StringSplitOptions.RemoveEmptyEntries)
                        parts
                        |> Array.map (fun p ->
                            if p.Length = 0 then p
                            else
                                System.String.Concat(
                                    System.Char.ToUpperInvariant p.[0] |> string,
                                    p.Substring 1))
                        |> System.String.Concat
                    aId, eId, attrName, attrGuid, dataType, length, precision, scale,
                    attr.IsMandatory, attr.IsAutoNumber, attr.IsIdentifier,
                    refEntityId, deleteRule, attr.PhysicalName))
            |> List.concat

        let chunkSize = 500
        let chunked =
            allAttrs
            |> List.chunkBySize chunkSize

        chunked
        |> List.iter (fun chunk ->
            if not (List.isEmpty chunk) then
                sb.AppendLine "INSERT INTO [dbo].[ossys_Entity_Attr] ([Id], [Entity_Id], [Name], [SS_Key], [Data_Type], [Length], [Precision], [Scale], [Default_Value], [Is_Mandatory], [Is_Active], [Is_AutoNumber], [Is_Identifier], [Referenced_Entity_Id], [Original_Name], [External_Column_Type], [Delete_Rule], [Physical_Column_Name], [Database_Name], [Legacy_Type], [Decimals], [Original_Type], [Description]) VALUES" |> ignore
                let lastIdx = chunk.Length - 1
                chunk
                |> List.iteri (fun i tup ->
                    let aId, eId, attrName, attrGuid, dataType, length, precision, scale,
                        isMandatory, isAutoNumber, isIdentifier,
                        refEntityId, deleteRule, physicalCol = tup
                    let suffix = if i = lastIdx then ";" else ","
                    sb.AppendLine
                        (sprintf "    (%d, %d, %s, %s, %s, %s, %s, %s, NULL, %s, 1, %s, %s, %s, NULL, NULL, %s, %s, NULL, NULL, NULL, NULL, NULL)%s"
                            aId
                            eId
                            (sqlString attrName)
                            (sqlGuid attrGuid)
                            (sqlString dataType)
                            (sqlNullableInt length)
                            (sqlNullableInt precision)
                            (sqlNullableInt scale)
                            (sqlBit isMandatory)
                            (sqlBit isAutoNumber)
                            (sqlBit isIdentifier)
                            (sqlNullableInt refEntityId)
                            (sqlNullableString deleteRule)
                            (sqlString physicalCol)
                            suffix)
                    |> ignore)
                sb.AppendLine "GO" |> ignore)

        sb.ToString()
