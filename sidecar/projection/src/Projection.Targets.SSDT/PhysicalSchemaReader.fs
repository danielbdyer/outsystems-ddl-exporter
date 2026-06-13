namespace Projection.Targets.SSDT

open Projection.Core

/// In-process structural reader: `seq<Statement> -> PhysicalSchema`.
///
/// HORIZON H-050 follow-up (Cluster F follow-on, 2026-05-22). The
/// canonical `ReadSide.read` (in `Projection.Adapters.Sql`) reads
/// `INFORMATION_SCHEMA` rows from a live SQL Server. The full adjunction
/// property `reader ∘ emitter = id` therefore requires deploying the
/// emitted DDL and reading it back — Docker-bound. Per HORIZON H-050,
/// the trigger to ship a property-test sweep is either a Docker pool
/// of N≥20 ephemeral containers OR an in-memory equivalent.
///
/// This module is the second leg of that trigger: it projects the
/// emitter's typed `seq<Statement>` directly to a `PhysicalSchema`,
/// bypassing the live-database round-trip. The property sweep
/// `PhysicalSchema.ofCatalog c = ofStatementStream (SsdtDdlEmitter.statements c)`
/// becomes runnable at FsCheck-property-test speed.
///
/// **What this reader handles.** Columns + FKs from `Statement.CreateTable`
/// — the axes the canary asserts on. PrimaryKey columns are recovered
/// by combining `ColumnDef.IsPrimaryKey` with `PrimaryKeyDef.Columns`
/// (both surfaces exist in the emitter; either alone is a partial view).
///
/// **What this reader doesn't handle.** Static rows (InsertRow
/// statements would need a name-keyed row reconstruction; out of scope
/// for the structural adjunction sweep), row digests (require streaming
/// row data not present in Statement), indexes (PhysicalSchema doesn't
/// carry index identity at this slice), extended properties,
/// triggers, sequences. These are HORIZON expansion candidates — see
/// AXIOMS.md A.0' for the deferred fidelity axes.
[<RequireQualifiedAccess>]
module PhysicalSchemaReader =

    let private toPhysicalColumns
            (table: TableId)
            (columns: ColumnDef list)
            (pk: PrimaryKeyDef option)
            : PhysicalColumn list =
        let pkColumnNames =
            match pk with
            | Some def -> Set.ofList def.Columns
            | None -> Set.empty
        let schemaStr, tableStr = TableId.qualifiedParts table
        columns
        |> List.map (fun col ->
            {
                Schema = schemaStr
                Table = tableStr
                Column = col.Name
                Type = col.Type
                Nullable = col.Nullable
                IsPrimaryKey = col.IsPrimaryKey || Set.contains col.Name pkColumnNames
                Length = col.Length
                Precision = col.Precision
                Scale = col.Scale
                IsIdentity = col.IsIdentity
                Default =
                    col.DefaultValue
                    |> Option.map (fun lit -> PhysicalSchema.normalizeDefault (SqlLiteral.toString lit))
                Computed = col.Computed |> Option.map PhysicalSchema.encodeComputed
            })

    let private toPhysicalForeignKeys
            (table: TableId)
            (fks: ForeignKeyDef list)
            : PhysicalForeignKey list =
        let srcSchemaStr, srcTableStr = TableId.qualifiedParts table
        fks
        |> List.map (fun fk ->
            let tgtSchemaStr, tgtTableStr = TableId.qualifiedParts fk.Target
            {
                SourceSchema = srcSchemaStr
                SourceTable = srcTableStr
                SourceColumn = fk.SourceColumn
                TargetSchema = tgtSchemaStr
                TargetTable = tgtTableStr
                TargetColumn = fk.TargetColumn
            })

    /// Project a typed `seq<Statement>` to a `PhysicalSchema`.
    ///
    /// **Adjunction property** (H-050; verified in
    /// `tests/Projection.Tests/AdjunctionLawTests.fs`):
    ///
    /// ```
    /// PhysicalSchema.ofCatalog catalog
    ///     = ofStatementStream (SsdtDdlEmitter.statements catalog)
    /// ```
    ///
    /// on the (Columns, ForeignKeys) axes. The full PhysicalSchema
    /// shape adds Rows (static seeds), populated by the StaticSeeds
    /// sibling reader outside this module's scope. (The RowDigests
    /// axis was deleted 2026-06-12 — backlog card F7.)
    let ofStatementStream (statements: seq<Statement>) : PhysicalSchema =
        use _ = Bench.scope "physicalSchemaReader.ofStatementStream"
        let columns =
            statements
            |> Seq.collect (fun stmt ->
                match stmt with
                | CreateTable (table, columns, pk, _, _, _) ->
                    toPhysicalColumns table columns pk :> seq<_>
                | _ -> Seq.empty)
            |> Set.ofSeq
        let foreignKeys =
            statements
            |> Seq.collect (fun stmt ->
                match stmt with
                | CreateTable (table, _, _, fks, _, _) ->
                    toPhysicalForeignKeys table fks :> seq<_>
                | _ -> Seq.empty)
            |> Set.ofSeq
        // Slice D.1.c — recover logical-name bindings from the
        // `V2.LogicalName` SetExtendedProperty statements V2 emits
        // (slice D.1.b). The adjunction holds on this axis too: the
        // statement-stream projection produces the same bindings as
        // `PhysicalSchema.ofCatalog` does from the source catalog.
        let logicalNameBindings =
            statements
            |> Seq.choose (fun stmt ->
                match stmt with
                // WP5 / C1 — paired with the writer's renamed property
                // (`Projection.LogicalName`). The in-memory round-trip is
                // always fresh emission, so it reads the new name only; the
                // dual-read (legacy `V2.*`) lives in the live `ReadSide`.
                | SetExtendedProperty (owner, "Projection.LogicalName", Some value) ->
                    match owner with
                    | TableProperty table ->
                        let schemaStr, tableStr = TableId.qualifiedParts table
                        Some
                            { Schema = schemaStr
                              Table = tableStr
                              Column = None
                              LogicalName = value }
                    | ColumnProperty (table, col) ->
                        let schemaStr, tableStr = TableId.qualifiedParts table
                        Some
                            { Schema = schemaStr
                              Table = tableStr
                              Column = Some col
                              LogicalName = value }
                    | _ -> None
                | _ -> None)
            |> Set.ofSeq
        {
            Columns = columns
            ForeignKeys = foreignKeys
            Rows = Set.empty
            LogicalNameBindings = logicalNameBindings
            // Wave-1 slice 1.3 — the in-process AST reader projects the
            // SCHEMA adjunction (columns + FKs + logical names + computed,
            // all column/table-shape recoverable from the typed statement
            // stream). The annotation axis (triggers / checks / sequences /
            // extended properties) is verified through the REAL SQL Server
            // canary (ReadSide path), which is the authoritative round-trip;
            // statement-stream recovery of annotation bodies is deferred to
            // a follow-on (no consumer needs the AST-side annotation diff
            // today — the two-consumer threshold gates it). Empty here keeps
            // the H-050 in-process adjunction green on the axes it owns.
            Annotations = Set.empty
            // E1 — same rationale as Annotations: the index axis is verified
            // through the REAL SQL Server canary (ReadSide path), the
            // authoritative round-trip. AST-side index recovery is deferred
            // (no consumer needs the in-process index diff; the adjunction
            // test asserts only Columns + FKs).
            Indexes = Set.empty
        }
