namespace Projection.Targets.Json

open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
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
        | SystemOwned   -> "SystemOwned"
        | Temporal _    -> "Temporal"

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

    /// Pinned-deterministic JSON writer options. Both forms come
    /// from `Projection.Core.JsonOptions` — the single sanctioned
    /// home for the BCL's mutable `JsonWriterOptions` struct (per
    /// the FP strict-mode discipline). `indented` is the document-
    /// writer form; `compact` is the per-kind slice form whose
    /// composer flows through `JsonNode.Parse(stream)` so the
    /// typed JsonNode lives at the Π port surface (pillar 1
    /// data-structure-oriented).

    /// Render one kind's JSON object as a typed `JsonNode`. Used by
    /// `emitSlices` to produce the per-kind value indexed in
    /// `ArtifactByKind`. Property order is fixed by `writeKind`'s
    /// call sequence and matches what the indented writer would
    /// emit at depth-3 in the catalog document, modulo indentation.
    ///
    /// **Pillar-1 cash-out (chapter-3.7 slice ε; audit Tier-1 #7).**
    /// The per-kind value flows through `JsonNode` rather than
    /// `string` so the typed structure survives at the Π port
    /// boundary — strings emerge ONLY at the absolute terminal
    /// `Utf8JsonWriter` step in `emit`. The internal serialization
    /// path (`Utf8JsonWriter` → `MemoryStream` → `byte[]` →
    /// `JsonNode.Parse(ReadOnlySpan<byte>)`) is BCL-typed
    /// end-to-end; no managed `string` is materialized and no
    /// stream-position mutation is needed (the byte-buffer is
    /// passed directly via the span overload).
    let private kindJsonNode (k: Kind) : JsonNode =
        use stream = new MemoryStream()
        do
            use writer = new Utf8JsonWriter(stream, (JsonOptions.compact ()))
            writeKind writer k
            writer.Flush()
        let bytes = stream.ToArray()
        match JsonNode.Parse(System.ReadOnlySpan<byte>(bytes)) with
        | null  -> invalidOp "JsonEmitter.kindJsonNode: writer produced empty stream (unreachable; writeKind always emits an object)"
        | node  -> node

    /// Π port realization (chapter 3.5 slice β; chapter-3.7 slice ε
    /// pillar-1 cash-out). Per A18, `Catalog` only — no Profile, no
    /// Policy. Per T11 (structural by construction), the smart-
    /// constructor's strict-equality check guarantees the artifact's
    /// keyset equals `Catalog.allKinds`'s SsKey set. Per pillar 1
    /// (data-structure-oriented over string-parsing), the per-kind
    /// value is a typed `JsonNode` carrying the kind's structure;
    /// strings emerge only at the terminal `Utf8JsonWriter` boundary
    /// in `emit`. T11 is now structural at BOTH the keyset axis AND
    /// the per-kind value-type axis — sibling-Π consumers can mutate
    /// the typed tree (drift detection, post-write enrichment) without
    /// re-parsing.
    let emitSlices : Emitter<JsonNode> = fun catalog ->
        use _ = Bench.scope "emit.json.emitSlices"
        let allKinds = Catalog.allKinds catalog
        let slices =
            allKinds
            |> List.map (fun k -> k.SsKey, kindJsonNode k)
            |> Map.ofList
        ArtifactByKind.create catalog slices

    /// Emit the catalog as JSON text. Output is deterministic: byte-
    /// identical for byte-identical input (T1). Composes through the
    /// typed `emitSlices` port so the seam is exercised by the canonical
    /// text realization. Per-kind `JsonNode` values write directly
    /// through the indented document writer via `node.WriteTo(writer)`
    /// — no `JsonNode.Parse(string)` round-trip (chapter-3.7 slice ε
    /// retired the prior re-parse path; the typed JsonNode is the
    /// canonical seam).
    let emit (catalog: Catalog) : string =
        use _ = Bench.scope "emit.json.emit"
        match emitSlices catalog with
        | Error err ->
            invalidOp
                (sprintf
                    "JsonEmitter.emit: ArtifactByKind invariant breach: %A"
                    err)
        | Ok artifact ->
            let slices = ArtifactByKind.toMap artifact
            use stream = new MemoryStream()
            do
                use writer = new Utf8JsonWriter(stream, (JsonOptions.indented ()))
                writer.WriteStartObject()
                writer.WriteString("emitter", "Projection.Targets.Json")
                writer.WriteNumber("version", version)
                writer.WritePropertyName("modules")
                writer.WriteStartArray()
                for m in catalog.Modules do
                    use _ = Bench.scope "emit.json.catalogModule"
                    writer.WriteStartObject()
                    writer.WriteString("ssKey", renderSsKey m.SsKey)
                    writer.WriteString("name",  Name.value m.Name)
                    writer.WritePropertyName("kinds")
                    writer.WriteStartArray()
                    for k in m.Kinds do
                        use _ = Bench.scope "emit.json.moduleKind"
                        match Map.tryFind k.SsKey slices with
                        | Some node ->
                            // Typed JsonNode → writer directly; no
                            // intermediate string. The BCL handles
                            // depth-tracking internally.
                            node.WriteTo(writer)
                        | None -> ()  // unreachable: T11 guarantees coverage
                    writer.WriteEndArray()
                    writer.WriteEndObject()
                writer.WriteEndArray()
                writer.WriteEndObject()
                writer.Flush()
            Encoding.UTF8.GetString(stream.ToArray())
