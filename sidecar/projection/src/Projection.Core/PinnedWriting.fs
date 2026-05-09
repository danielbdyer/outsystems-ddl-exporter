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

open System.Text
open System.Text.Json
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
