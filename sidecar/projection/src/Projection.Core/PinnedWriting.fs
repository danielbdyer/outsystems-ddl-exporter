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
        let bytes = stream.ToArray()
        match JsonNode.Parse(System.ReadOnlySpan<byte>(bytes)) with
        | null -> invalidOp "JsonWriting.writeToNode: writer produced empty stream (unreachable; the imperative writer always emits an object)"
        | node -> node

    /// Render a typed `JsonNode` to text under **indented** options:
    /// `node.WriteTo` a `Utf8JsonWriter` over a `MemoryStream`, flush,
    /// then `Encoding.UTF8.GetString(stream.ToArray())`. The terminal
    /// interpreter for a typed-tree description (the document `emit`
    /// whose body is a `JsonNode`).
    let renderNodeToString (node: JsonNode) : string =
        use stream = new MemoryStream()
        do
            use writer = new Utf8JsonWriter(stream, JsonOptions.indented ())
            node.WriteTo(writer)
            writer.Flush()
        Encoding.UTF8.GetString(stream.ToArray())

    /// Write imperatively to a `Utf8JsonWriter` over a `MemoryStream`
    /// under **indented** options, flush, then
    /// `Encoding.UTF8.GetString(stream.ToArray())`. The codec/document
    /// `serialize` path: imperative-write straight to indented text
    /// with NO re-parse through a node (which would be wasteful).
    let writeToString (write: Utf8JsonWriter -> unit) : string =
        use stream = new MemoryStream()
        do
            use writer = new Utf8JsonWriter(stream, JsonOptions.indented ())
            write writer
            writer.Flush()
        Encoding.UTF8.GetString(stream.ToArray())

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
