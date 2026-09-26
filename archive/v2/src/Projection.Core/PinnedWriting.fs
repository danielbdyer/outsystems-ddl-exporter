namespace Projection.Core

// LINT-ALLOW-FILE-MUTATION: BCL `JsonWriterOptions` is a mutable
//   struct and `XmlWriterSettings` is a mutable class; both expose
//   their fields/properties via mutation as their option-builder
//   surface. This module is the single sanctioned site for the
//   pinned-deterministic forms of both — `JsonEmitter`,
//   `DistributionsEmitter`, and `RefactorLogRender` consume the
//   forms named here rather than building their own. Per the FP
//   strict-mode discipline (`DECISIONS 2026-05-09`), reify the
//   mutation in one named home.

open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Xml

/// Pinned `System.Text.Json` writer-option forms. Both forms set
/// `NewLine = "\n"` (cross-platform deterministic) and the indent
/// flag per the form name. Returned as fresh structs (struct
/// semantics protect callers' independence).
[<RequireQualifiedAccess>]
module JsonOptions =

    /// Indented form (two-space indent, LF newlines). Used by Π
    /// emitters whose canonical output is human-readable JSON
    /// (JsonEmitter / DistributionsEmitter whole-document writers).
    let indented () : JsonWriterOptions =
        let mutable opts = JsonWriterOptions()
        opts.Indented <- true
        opts.NewLine <- "\n"
        opts

    /// Compact (no indent, LF newlines). Used by per-kind slice
    /// rendering whose composer re-parses each fragment through
    /// `JsonNode.Parse` and writes via the indented document
    /// writer. Indentation depth-tracking is handled by the BCL
    /// at composition time.
    let compact () : JsonWriterOptions =
        let mutable opts = JsonWriterOptions()
        opts.Indented <- false
        opts.NewLine <- "\n"
        opts

/// Pinned `System.Text.Json` write→materialize seam. The
/// `Utf8JsonWriter → MemoryStream → byte[] → JsonNode.Parse` dance
/// (and its node→indented-string sibling) was duplicated across
/// every per-kind `kindJsonNode` and whole-document `emit`/`serialize`
/// site; these three forms are the single sanctioned home. The
/// compact/indented options choice is fixed PER FORM and matches the
/// site each replaces exactly — byte-identity is the contract
/// (`GoldenEmissionTests` guard the emitted bytes).
[<RequireQualifiedAccess>]
module JsonWriting =

    /// The flushed stream's live buffer as `(array, offset, count)` —
    /// PL-6 (S31): every `MemoryStream` here is the expandable,
    /// publicly-visible-buffer form, so `TryGetBuffer` always succeeds
    /// and the serialized bytes are read IN PLACE (the prior
    /// `ToArray()` paid a full second copy of the whole artifact per
    /// serialize). The `ToArray` arms are the defensive fallback for a
    /// hypothetical non-exposable stream (and the nullable
    /// `ArraySegment.Array` of a default segment), never taken by
    /// these callers.
    let private writtenBytes (stream: MemoryStream) : byte[] * int * int =
        match stream.TryGetBuffer() with
        | true, seg ->
            match seg.Array with
            | null -> let a = stream.ToArray() in a, 0, a.Length
            | arr -> arr, seg.Offset, seg.Count
        | false, _ ->
            let a = stream.ToArray()
            a, 0, a.Length

    /// Write imperatively to a `Utf8JsonWriter` over a `MemoryStream`
    /// under **compact** options, flush, then re-parse the bytes into
    /// a typed `JsonNode` via the `ReadOnlySpan<byte>` overload (no
    /// managed `string` materialized). The defensive null→`invalidOp`
    /// guard preserves each call site's "writer always emits an
    /// object" unreachable-case contract. Used by every per-kind
    /// `kindJsonNode` seam (pillar-1 cash-out: typed node at the Π
    /// port; strings emerge only at the terminal writer).
    let writeToNode (write: Utf8JsonWriter -> unit) : JsonNode =
        use stream = new MemoryStream()
        do
            use writer = new Utf8JsonWriter(stream, JsonOptions.compact ())
            write writer
            writer.Flush()
        let arr, off, count = writtenBytes stream
        match JsonNode.Parse(System.ReadOnlySpan<byte>(arr, off, count)) with
        | null -> invalidOp "JsonWriting.writeToNode: writer produced empty stream (unreachable; the imperative writer always emits an object)"
        | node -> node

    /// Render a typed `JsonNode` to text under **indented** options:
    /// `node.WriteTo` a `Utf8JsonWriter` over a `MemoryStream`, flush,
    /// then decode the stream's live buffer (S31 — no intermediate
    /// byte-array copy). The terminal interpreter for a typed-tree
    /// description (the document `emit` whose body is a `JsonNode`).
    let renderNodeToString (node: JsonNode) : string =
        use stream = new MemoryStream()
        do
            use writer = new Utf8JsonWriter(stream, JsonOptions.indented ())
            node.WriteTo(writer)
            writer.Flush()
        let arr, off, count = writtenBytes stream
        Encoding.UTF8.GetString(arr, off, count)

    /// Write imperatively to a `Utf8JsonWriter` over a `MemoryStream`
    /// under **indented** options, flush, then decode the stream's
    /// live buffer (S31). The codec/document `serialize` path:
    /// imperative-write straight to indented text with NO re-parse
    /// through a node (which would be wasteful).
    let writeToString (write: Utf8JsonWriter -> unit) : string =
        use stream = new MemoryStream()
        do
            use writer = new Utf8JsonWriter(stream, JsonOptions.indented ())
            write writer
            writer.Flush()
        let arr, off, count = writtenBytes stream
        Encoding.UTF8.GetString(arr, off, count)

    /// The `writeToString` sibling that stays in UTF-8: the serialized
    /// bytes, one copy, no UTF-16 string materialized. PL-6 (S30) —
    /// for an embedder whose destination is itself a UTF-8 writer
    /// (`Utf8JsonWriter.WriteRawValue(ReadOnlySpan<byte>)`), the
    /// byte→string→byte transcode round-trip is pure waste; the bytes
    /// ARE the carrier. Same writer, same indented options, so the
    /// bytes equal `Encoding.UTF8.GetBytes(writeToString write)`
    /// exactly.
    let writeToUtf8 (write: Utf8JsonWriter -> unit) : byte[] =
        use stream = new MemoryStream()
        do
            use writer = new Utf8JsonWriter(stream, JsonOptions.indented ())
            write writer
            writer.Flush()
        stream.ToArray()

/// Pinned `System.Xml.XmlWriterSettings` forms. Each call returns
/// a fresh instance (XmlWriterSettings is a class — sharing one
/// instance across consumers would alias state).
[<RequireQualifiedAccess>]
module XmlSettings =

    /// UTF-8 (no BOM) + indented (two-space) + LF newlines. Used
    /// by `RefactorLogRender.toRefactorLogXml`'s `.refactorlog`
    /// XML emission. T1 byte-determinism rests on every formatting
    /// axis being pinned at this surface.
    let indentedUtf8NoBom () : XmlWriterSettings =
        let s = XmlWriterSettings()
        s.Encoding         <- UTF8Encoding(false)
        s.Indent           <- true
        s.IndentChars      <- "  "
        s.NewLineChars     <- "\n"
        s.NewLineHandling  <- NewLineHandling.Replace
        s.OmitXmlDeclaration <- false
        s
