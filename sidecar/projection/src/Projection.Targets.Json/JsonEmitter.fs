namespace Projection.Targets.Json

// LINT-ALLOW-FILE-MUTATION: BCL JsonWriterOptions is a mutable
//   struct exposing fields by mutation (BCL surface). The mutation
//   is local to the option-builder; pure output is emitted to
//   Utf8JsonWriter. Per audit Lens-2 Tier-2 (justified — BCL forces
//   the shape).

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

    /// Compact (un-indented) writer options for per-kind slice
    /// rendering. Per-kind values land in the `Map<SsKey, string>`
    /// keyed by `ArtifactByKind`'s smart constructor; the composer
    /// (`emit`) parses each compact kind and writes it through the
    /// indented document writer, so the BCL's `Utf8JsonWriter` handles
    /// indentation depth-tracking by construction. T1 byte-determinism
    /// of the whole document is preserved because (a) per-kind JSON
    /// is byte-deterministic in compact form by virtue of `JsonObject`
    /// preserving property insertion order, and (b) the indented
    /// writer's emitted text is a function of (input nodes, pinned
    /// options) — not of arrival path.
    let private compactOptions : JsonWriterOptions =
        let mutable opts = JsonWriterOptions()
        opts.Indented <- false
        opts.NewLine <- "\n"
        opts

    /// Render one kind's JSON object into a compact UTF-8 string.
    /// Used by `emitSlices` to produce the per-kind value indexed in
    /// `ArtifactByKind`. The object's property order is fixed by
    /// `writeKind`'s call sequence and matches what the indented
    /// writer would emit at depth-3 in the catalog document, modulo
    /// indentation.
    let private kindJsonText (k: Kind) : string =
        use stream = new MemoryStream()
        do
            use writer = new Utf8JsonWriter(stream, compactOptions)
            writeKind writer k
            writer.Flush()
        Encoding.UTF8.GetString(stream.ToArray())

    /// Π port realization (chapter 3.5 slice β). Per A18, `Catalog`
    /// only — no Profile, no Policy. Per T11 (structural by
    /// construction), the smart-constructor's strict-equality check
    /// guarantees the artifact's keyset equals `Catalog.allKinds`'s
    /// SsKey set. Per the chapter-open §8 two-consumer threshold,
    /// the per-kind value is `string` (the kind's JSON object as
    /// compact text); a richer `JsonObject` per-kind type earns its
    /// place when a second consumer (e.g., DacpacEmitter or chapter-
    /// 4.4 drift detection) forces typed manipulation.
    let emitSlices : Emitter<string> = fun catalog ->
        use _ = Bench.scope "emit.json.emitSlices"
        let allKinds = Catalog.allKinds catalog
        let slices =
            allKinds
            |> List.map (fun k -> k.SsKey, kindJsonText k)
            |> Map.ofList
        ArtifactByKind.create catalog slices

    /// Emit the catalog as JSON text. Output is deterministic: byte-
    /// identical for byte-identical input (T1). Composes through the
    /// typed `emitSlices` port so the seam is exercised by the canonical
    /// text realization. Per-kind JSON fragments are re-parsed via
    /// `JsonNode.Parse` and written through the indented writer so the
    /// BCL handles indentation-depth tracking; the round trip preserves
    /// byte-determinism because `JsonObject` preserves property
    /// insertion order.
    let emit (catalog: Catalog) : string =
        use _ = Bench.scope "emit.json.emit"
        match emitSlices catalog with
        | Result.Error err ->
            invalidOp
                (sprintf
                    "JsonEmitter.emit: ArtifactByKind invariant breach: %A"
                    err)
        | Result.Ok artifact ->
            let slices = ArtifactByKind.toMap artifact
            use stream = new MemoryStream()
            do
                use writer = new Utf8JsonWriter(stream, writerOptions)
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
                        | Some kindText ->
                            // Re-parse the compact per-kind JSON and
                            // write through the indented writer so
                            // depth-tracking matches the surrounding
                            // catalog document. Unreachable `None`
                            // case eliminated by the smart constructor's
                            // strict-equality contract (T11 by type).
                            match JsonNode.Parse(kindText) with
                            | null -> ()  // unreachable: kindText is non-empty by construction
                            | node -> node.WriteTo(writer)
                        | None -> ()  // unreachable: T11 guarantees coverage
                    writer.WriteEndArray()
                    writer.WriteEndObject()
                writer.WriteEndArray()
                writer.WriteEndObject()
                writer.Flush()
            Encoding.UTF8.GetString(stream.ToArray())
