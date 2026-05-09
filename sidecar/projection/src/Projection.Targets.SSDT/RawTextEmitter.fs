namespace Projection.Targets.SSDT

open System
open System.Text
open Projection.Core

/// Raw .sql-style text emitter — the first sibling Π for V2. The output
/// is diffable text reflecting every kind, attribute, and reference in
/// the catalog. This is the synthetic-fixture milestone form per
/// DECISIONS.md (2026-05-06 — Π_SSDT first emission target is raw
/// .sql-style text); DacFx-backed real SSDT artifacts arrive later.
///
/// Π is mechanical (A18): no policy parameter enters this module. The
/// type-correspondence mapping below is the synthetic-milestone default;
/// when Policy lands as a structured input it will replace these
/// hard-codes.
[<RequireQualifiedAccess>]
module RawTextEmitter =

    /// Emitter version. The lineage / output may change shape across
    /// versions; bump when the textual layout or the synthetic type map
    /// is altered.
    [<Literal>]
    let version : int = 1

    // -----------------------------------------------------------------------
    // Synthetic-milestone defaults. These belong in Policy when Policy
    // lands; for now they are constants here so Π stays mechanical.
    // -----------------------------------------------------------------------

    let private defaultSqlType (t: PrimitiveType) : string =
        match t with
        | Integer  -> "INT"
        | Decimal  -> "DECIMAL(18, 4)"
        | Text     -> "NVARCHAR(MAX)"
        | Boolean  -> "BIT"
        | DateTime -> "DATETIME2"
        | Date     -> "DATE"
        | Time     -> "TIME"
        | Binary   -> "VARBINARY(MAX)"
        | Guid     -> "UNIQUEIDENTIFIER"

    let private renderAction (a: ReferenceAction) : string =
        match a with
        | NoAction -> "NO ACTION"
        | Cascade  -> "CASCADE"
        | SetNull  -> "SET NULL"
        | Restrict -> "NO ACTION"  // SQL Server: Restrict is encoded as NO ACTION

    let private quote (s: string) : string = sprintf "[%s]" s

    let private originLabel (o: Origin) : string =
        match o with
        | OsNative                     -> "OsNative"
        | ExternalViaIntegrationStudio -> "ExternalViaIS"
        | ExternalDirect               -> "ExternalDirect"

    let private modalityLabel (m: ModalityMark) : string =
        match m with
        | Static rows   -> sprintf "Static(%d)" rows.Length
        | TenantScoped  -> "TenantScoped"
        | SoftDeletable -> "SoftDeletable"

    let private rootKey (k: SsKey) : string = SsKey.rootOriginal k

    // -----------------------------------------------------------------------
    // Per-element rendering. Each function takes a StringBuilder so the
    // emitter is allocation-friendly without sacrificing purity.
    // -----------------------------------------------------------------------

    let private renderAttribute (sb: StringBuilder) (a: Attribute) : unit =
        let name = quote a.Column.ColumnName
        let typ = defaultSqlType a.Type
        let nullness = if a.Column.IsNullable then "NULL" else "NOT NULL"
        sb.Append("    ").Append(name).Append(' ').Append(typ).Append(' ').Append(nullness)
            .Append("  -- ").Append(Name.value a.Name).Append(" (").Append(rootKey a.SsKey).Append(')')
        |> ignore

    let private renderKindHeader (sb: StringBuilder) (k: Kind) : unit =
        sb.Append("-- Kind: ").Append(Name.value k.Name)
            .Append(" (").Append(rootKey k.SsKey).Append(") origin=")
            .Append(originLabel k.Origin) |> ignore
        if not (List.isEmpty k.Modality) then
            let labels = k.Modality |> List.map modalityLabel |> String.concat ", "
            sb.Append(" modality=[").Append(labels).Append(']') |> ignore
        sb.AppendLine() |> ignore

    let private renderTable (sb: StringBuilder) (k: Kind) : unit =
        renderKindHeader sb k
        let qualified =
            sprintf "%s.%s" (quote k.Physical.Schema) (quote k.Physical.Table)
        sb.Append("CREATE TABLE ").Append(qualified).AppendLine(" (") |> ignore
        // PK constraint emission (M3 prep): if any attributes carry
        // IsPrimaryKey, append a trailing CONSTRAINT clause so the
        // deployed table actually carries a PRIMARY KEY (rather than
        // just the inline `-- PK` comment marker). The canary's
        // round-trip property requires this fidelity to compare
        // PhysicalSchema.IsPrimaryKey across source and target.
        let pkColumns =
            k.Attributes
            |> List.filter (fun a -> a.IsPrimaryKey)
            |> List.map (fun a -> a.Column.ColumnName)
        let hasPkConstraint = not (List.isEmpty pkColumns)
        let lastColumnIdx = k.Attributes.Length - 1
        k.Attributes
        |> List.iteri (fun i a ->
            let name = quote a.Column.ColumnName
            let typ = defaultSqlType a.Type
            let nullness = if a.Column.IsNullable then "NULL" else "NOT NULL"
            let needsComma = i < lastColumnIdx || hasPkConstraint
            let sep = if needsComma then "," else ""
            let pkTag = if a.IsPrimaryKey then " PK" else ""
            sb.Append("    ").Append(name).Append(' ').Append(typ).Append(' ').Append(nullness)
                .Append(sep).Append("  -- ").Append(Name.value a.Name).Append(" (").Append(rootKey a.SsKey).Append(')')
                .Append(pkTag).AppendLine() |> ignore)
        if hasPkConstraint then
            let pkColumnList = pkColumns |> List.map quote |> String.concat ", "
            let pkConstraintName =
                sprintf "PK_%s_%s" k.Physical.Schema k.Physical.Table
            sb.Append("    CONSTRAINT ").Append(quote pkConstraintName)
                .Append(" PRIMARY KEY (").Append(pkColumnList).AppendLine(")")
            |> ignore
        sb.AppendLine(");") |> ignore

    /// Render the FK constraints from a kind's references. The target
    /// kind is looked up in the surrounding catalog so we can emit the
    /// physical schema/table; if the target is absent (e.g., a previous
    /// pass removed it) we emit a comment noting the dangling reference
    /// rather than silently dropping the FK.
    let private renderReferences (sb: StringBuilder) (catalog: Catalog) (k: Kind) : unit =
        for r in k.References do
            sb.AppendLine() |> ignore
            sb.Append("ALTER TABLE ")
                .Append(quote k.Physical.Schema).Append('.').Append(quote k.Physical.Table)
                .AppendLine() |> ignore
            sb.Append("    ADD CONSTRAINT ").Append(quote (sprintf "FK_%s" (rootKey r.SsKey)))
                .AppendLine() |> ignore
            // Source column is the attribute identified by SourceAttribute.
            let sourceColumn =
                k.Attributes
                |> List.tryFind (fun a -> a.SsKey = r.SourceAttribute)
                |> Option.map (fun a -> a.Column.ColumnName)
                |> Option.defaultValue "<missing-source-column>"
            match Catalog.tryFindKind r.TargetKind catalog with
            | Some target ->
                // PK resolved from the IR's IsPrimaryKey marker (Attribute
                // refinement landed alongside the EntitySeedDeterminizer
                // admire). For composite PKs, the first PK attribute wins
                // here — composite-FK semantics arrive when a real V1
                // fixture has them.
                let targetPk =
                    target.Attributes
                    |> List.tryFind (fun a -> a.IsPrimaryKey)
                    |> Option.map (fun a -> a.Column.ColumnName)
                    |> Option.defaultValue "<missing-target-pk>"
                sb.Append("    FOREIGN KEY (").Append(quote sourceColumn).AppendLine(")")
                  .Append("    REFERENCES ")
                    .Append(quote target.Physical.Schema).Append('.').Append(quote target.Physical.Table)
                    .Append(" (").Append(quote targetPk).AppendLine(")")
                  .Append("    ON DELETE ").Append(renderAction r.OnDelete).AppendLine(";")
                |> ignore
            | None ->
                sb.AppendLine("-- WARNING: target kind not present in catalog; FK omitted") |> ignore

    let private renderStaticPopulations (sb: StringBuilder) (k: Kind) : unit =
        for m in k.Modality do
            match m with
            | Static rows ->
                sb.Append("-- Static populations: ").Append(rows.Length).AppendLine(" rows") |> ignore
                for row in rows do
                    sb.Append("--   ").Append(rootKey row.Identifier) |> ignore
                    let pairs =
                        row.Values
                        |> Map.toList
                        |> List.map (fun (n, v) -> sprintf "%s=%s" (Name.value n) v)
                        |> String.concat ", "
                    if pairs <> "" then
                        sb.Append(" { ").Append(pairs).Append(" }") |> ignore
                    sb.AppendLine() |> ignore
            | _ -> ()

    let private renderModule (sb: StringBuilder) (catalog: Catalog) (m: Module) : unit =
        sb.AppendLine() |> ignore
        sb.Append("-- Module: ").Append(Name.value m.Name)
            .Append(" (").Append(rootKey m.SsKey).Append(')').AppendLine() |> ignore
        for k in m.Kinds do
            sb.AppendLine() |> ignore
            renderTable sb k
            renderReferences sb catalog k
            renderStaticPopulations sb k

    // -----------------------------------------------------------------------
    // Public surface.
    // -----------------------------------------------------------------------

    /// Emit the catalog as raw .sql-style text. Output is deterministic:
    /// for any byte-identical input catalog, output is byte-identical
    /// across runs (T1).
    let emit (catalog: Catalog) : string =
        let sb = StringBuilder(2048)
        sb.Append("-- Generated by Projection.Targets.SSDT.RawTextEmitter v")
            .Append(version).AppendLine() |> ignore
        sb.AppendLine("-- Project = Π_SSDT ∘ E") |> ignore
        sb.AppendLine("-- (synthetic-milestone form: raw text, dependency-free)") |> ignore
        for m in catalog.Modules do
            renderModule sb catalog m
        sb.ToString()
