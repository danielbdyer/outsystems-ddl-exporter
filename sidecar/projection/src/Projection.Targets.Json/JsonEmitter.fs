namespace Projection.Targets.Json

open System.IO
open System.Text
open System.Text.Json
open Projection.Core

/// The second sibling Π for V2. Emits the catalog as JSON text.
///
/// Implementation uses the built-in `System.Text.Json.Utf8JsonWriter`
/// (no third-party dependency). Property order is determined by the
/// order of writes — explicit and stable. Pretty-print indentation and
/// newline are pinned for cross-platform deterministic output (T1).
[<RequireQualifiedAccess>]
module JsonEmitter =

    [<Literal>]
    let version : int = 2

    // -----------------------------------------------------------------------
    // Synthetic-milestone string forms for DUs. These belong in Policy
    // when Policy lands; for now they are constants here so Π stays
    // mechanical (A18).
    // -----------------------------------------------------------------------

    let private renderSsKey (key: SsKey) : string =
        let root = SsKey.rootOriginal key
        if SsKey.isDerived key then sprintf "%s [derived]" root else root

    let private originString (o: Origin) : string =
        match o with
        | OsNative                     -> "OsNative"
        | ExternalViaIntegrationStudio -> "ExternalViaIntegrationStudio"
        | ExternalDirect               -> "ExternalDirect"

    let private primitiveString (t: PrimitiveType) : string =
        match t with
        | Integer  -> "Integer"
        | Decimal  -> "Decimal"
        | Text     -> "Text"
        | Boolean  -> "Boolean"
        | DateTime -> "DateTime"
        | Date     -> "Date"
        | Time     -> "Time"
        | Binary   -> "Binary"
        | Guid     -> "Guid"

    let private actionString (a: ReferenceAction) : string =
        match a with
        | NoAction -> "NoAction"
        | Cascade  -> "Cascade"
        | SetNull  -> "SetNull"
        | Restrict -> "Restrict"

    let private modalityString (m: ModalityMark) : string =
        match m with
        | Static rows   -> sprintf "Static(%d)" rows.Length
        | TenantScoped  -> "TenantScoped"
        | SoftDeletable -> "SoftDeletable"

    // -----------------------------------------------------------------------
    // Per-element writers. Each takes the writer and an IR node and emits
    // a JSON object / array. Writers are passed the catalog when needed
    // (none of these need it for the synthetic milestone).
    // -----------------------------------------------------------------------

    let private writeAttribute (w: Utf8JsonWriter) (a: Attribute) : unit =
        w.WriteStartObject()
        w.WriteString("ssKey",     renderSsKey a.SsKey)
        w.WriteString("name",      Name.value a.Name)
        w.WriteString("type",      primitiveString a.Type)
        w.WriteString("column",    a.Column.ColumnName)
        w.WriteBoolean("nullable", a.Column.IsNullable)
        w.WriteBoolean("primaryKey", a.IsPrimaryKey)
        w.WriteBoolean("mandatory", a.IsMandatory)
        w.WriteEndObject()

    let private writeReference (w: Utf8JsonWriter) (r: Reference) : unit =
        w.WriteStartObject()
        w.WriteString("ssKey",           renderSsKey r.SsKey)
        w.WriteString("name",            Name.value r.Name)
        w.WriteString("sourceAttribute", renderSsKey r.SourceAttribute)
        w.WriteString("targetKind",      renderSsKey r.TargetKind)
        w.WriteString("onDelete",        actionString r.OnDelete)
        w.WriteEndObject()

    let private writeModality (w: Utf8JsonWriter) (marks: ModalityMark list) : unit =
        w.WriteStartArray()
        for m in marks do w.WriteStringValue(modalityString m)
        w.WriteEndArray()

    let private writePhysical (w: Utf8JsonWriter) (p: PhysicalRealization) : unit =
        w.WriteStartObject()
        w.WriteString("schema", p.Schema)
        w.WriteString("table",  p.Table)
        w.WriteEndObject()

    let private writeKind (w: Utf8JsonWriter) (k: Kind) : unit =
        use _ = Bench.scope "emit.json.kind"
        w.WriteStartObject()
        w.WriteString("ssKey",  renderSsKey k.SsKey)
        w.WriteString("name",   Name.value k.Name)
        w.WriteString("origin", originString k.Origin)
        w.WritePropertyName("modality"); writeModality w k.Modality
        w.WritePropertyName("physical"); writePhysical w k.Physical
        w.WritePropertyName("attributes")
        w.WriteStartArray()
        k.Attributes |> Bench.iterDo "emit.json.attribute" (writeAttribute w)
        w.WriteEndArray()
        w.WritePropertyName("references")
        w.WriteStartArray()
        k.References |> Bench.iterDo "emit.json.reference" (writeReference w)
        w.WriteEndArray()
        w.WriteEndObject()

    let private writeModule (w: Utf8JsonWriter) (m: Module) : unit =
        use _ = Bench.scope "emit.json.module"
        w.WriteStartObject()
        w.WriteString("ssKey", renderSsKey m.SsKey)
        w.WriteString("name",  Name.value m.Name)
        w.WritePropertyName("kinds")
        w.WriteStartArray()
        m.Kinds |> Bench.iterDo "emit.json.moduleKind" (writeKind w)
        w.WriteEndArray()
        w.WriteEndObject()

    // -----------------------------------------------------------------------
    // Public surface.
    // -----------------------------------------------------------------------

    let private writerOptions : JsonWriterOptions =
        // Indented + pinned LF newline so output is deterministic across
        // platforms (T1). IndentCharacter / IndentSize default to two
        // spaces, which matches the rest of the V2 documentation style.
        let mutable opts = JsonWriterOptions()
        opts.Indented <- true
        opts.NewLine <- "\n"
        opts

    /// Emit the catalog as JSON text. Output is deterministic: byte-
    /// identical for byte-identical input (T1).
    let emit (catalog: Catalog) : string =
        use stream = new MemoryStream()
        do
            use writer = new Utf8JsonWriter(stream, writerOptions)
            writer.WriteStartObject()
            writer.WriteString("emitter", "Projection.Targets.Json")
            writer.WriteNumber("version", version)
            writer.WritePropertyName("modules")
            writer.WriteStartArray()
            catalog.Modules
            |> Bench.iterDo "emit.json.catalogModule" (writeModule writer)
            writer.WriteEndArray()
            writer.WriteEndObject()
            writer.Flush()
        Encoding.UTF8.GetString(stream.ToArray())
