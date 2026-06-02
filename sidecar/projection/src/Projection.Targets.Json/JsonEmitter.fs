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

    // SsKey rendering moved to `Projection.Core.SsKey.display` (sibling
    // to `rootOriginal` / `isDerived`); call sites reference the
    // canonical projection directly.

    let private originString (o: Origin) : string =
        match o with
        | Native           -> "Native"
        | ExternalIndirect -> "ExternalIndirect"
        | ExternalDirect   -> "ExternalDirect"

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
        w.WriteString("ssKey",     SsKey.display a.SsKey)
        w.WriteString("name",      Name.value a.Name)
        w.WriteString("type",      primitiveString a.Type)
        w.WriteString("column",    ColumnRealization.columnNameText a.Column)
        w.WriteBoolean("nullable", a.Column.IsNullable)
        w.WriteBoolean("primaryKey", a.IsPrimaryKey)
        w.WriteBoolean("mandatory", a.IsMandatory)
        w.WriteEndObject()

    let private writeReference (w: Utf8JsonWriter) (r: Reference) : unit =
        w.WriteStartObject()
        w.WriteString("ssKey",           SsKey.display r.SsKey)
        w.WriteString("name",            Name.value r.Name)
        w.WriteString("sourceAttribute", SsKey.display r.SourceAttribute)
        w.WriteString("targetKind",      SsKey.display r.TargetKind)
        w.WriteString("onDelete",        actionString r.OnDelete)
        w.WriteEndObject()

    let private writeModality (w: Utf8JsonWriter) (marks: ModalityMark list) : unit =
        w.WriteStartArray()
        for m in marks do w.WriteStringValue(modalityString m)
        w.WriteEndArray()

    let private writePhysical (w: Utf8JsonWriter) (p: PhysicalRealization) : unit =
        w.WriteStartObject()
        w.WriteString("schema", TableId.schemaText p)
        w.WriteString("table",  TableId.tableText p)
        w.WriteEndObject()

    let private writeKind (w: Utf8JsonWriter) (k: Kind) : unit =
        use _ = Bench.scope "emit.json.kind"
        w.WriteStartObject()
        w.WriteString("ssKey",  SsKey.display k.SsKey)
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
        w.WriteString("ssKey", SsKey.display m.SsKey)
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
                    writer.WriteString("ssKey", SsKey.display m.SsKey)
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

    // -----------------------------------------------------------------------
    // Slice 5.13.sibling-emitter-registry-json — `registeredMetadata`
    // entry for the JsonEmitter sibling Π. Mirrors the
    // `SsdtDdlEmitter.registeredMetadata` precedent (chapter 5.13 slice
    // emit-features-registry, 2026-05-18) on the JSON-projection axis.
    //
    // **Classification.** All Sites carry `DataIntent` — `emitSlices`
    // signature is `Catalog → Result<ArtifactByKind<JsonNode>, EmitError>`
    // (per A18, Catalog only; no Profile, no Policy). The projection is
    // shape-preserving: every IR field maps 1:1 to a JSON property. No
    // operator policy enters at any site.
    //
    // **Project-boundary note.** Lives in `Projection.Targets.Json`; the
    // consumer that wants the full sibling-emitter chorus (CLI / canary /
    // ManifestEmitter prepend chain) assembles via project-owned lists
    // concatenated at the call site. Same cherry-pick boundary precedent
    // as `CatalogReader.registeredMetadata` (in `Projection.Adapters.Osm`)
    // + `RegisteredDataTransforms.all` (in `Projection.Targets.Data`).
    // -----------------------------------------------------------------------

    let registeredMetadata : RegisteredTransformMetadata =
        RegisteredTransformMetadata.emitter "jsonEmitter" Schema
            [ TransformSite.dataIntent "catalogDocument"
                "Top-level emit assembles `{ emitter, version, modules : [...] }` via `Utf8JsonWriter` with pinned `JsonOptions.indented` (T1 byte-determinism). Catalog → string projection; per-module envelope wraps `kindJson` outputs in declaration order."
              TransformSite.dataIntent "kindJson"
                "Project Kind → JsonNode via `writeKind` — ssKey / name / origin / modality / physical / attributes[] / references[] in fixed property order. Path flows through `kindJsonNode` (`Utf8JsonWriter` → `MemoryStream` → `byte[]` → `JsonNode.Parse(ReadOnlySpan<byte>)`) so the typed JsonNode is the canonical sibling-Π port value (pillar 1; chapter 3.7 slice ε)."
              TransformSite.dataIntent "attributeJson"
                "Project Attribute → JsonNode via `writeAttribute` — ssKey / name / type / column / nullable / primaryKey / mandatory. Closed-DU `PrimitiveType` flattens to `primitiveString` (9 variants); ssKey flattens via `SsKey.display` (root + optional `[derived]` marker)."
              TransformSite.dataIntent "referenceJson"
                "Project Reference → JsonNode via `writeReference` — ssKey / name / sourceAttribute / targetKind / onDelete. Closed-DU `ReferenceAction` flattens to `actionString` (4 variants). `OnUpdate` + `IsConstraintTrusted` (slice 5.13.fk-features-emit IR lifts) carriage-deferred pending JSON consumer demand."
              TransformSite.dataIntent "modalityProjection"
                "Project ModalityMark list → JSON string array via `writeModality` — closed-DU `ModalityMark` dispatch carries `Static(n)` row-count summary, `TenantScoped / SoftDeletable / SystemOwned` payload-free marks, `Temporal _` summary. Per-variant string projection collapses payload; consumers needing full payload reach for the typed IR."
              TransformSite.dataIntent "emitSlices"
                "Π port realization — `Catalog → Result<ArtifactByKind<JsonNode>, EmitError>` (A35 stream-realization pattern on the JSON axis). Per-kind `JsonNode` carries the typed structure at the port boundary (T11 structural by construction); `ArtifactByKind.create` validates the smart-constructor invariant that keyset equals `Catalog.allKinds`'s SsKey set." ]
