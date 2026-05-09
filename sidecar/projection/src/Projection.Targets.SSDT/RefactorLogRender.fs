namespace Projection.Targets.SSDT

open System.IO
open System.Text
open System.Xml
open Projection.Core

/// Pure XML rendering of `RefactorLogEntry` slices into the
/// SSDT-native `.refactorlog` document. Per chapter 3.5 prescope §6,
/// the rendered XML is what DacFx's incremental deploy planner reads
/// to convert `DROP COLUMN` + `ADD COLUMN` into `sp_rename`. T1
/// byte-determinism on the rendered document rests on:
///
///   - Operations sorted by `OperationKey` (deterministic UUIDv5;
///     stable lexicographic sort over the dashed Guid form).
///   - `ChangeDateTime` pinned to `2000-01-01T00:00:00Z` (DacFx
///     ignores `ChangeDateTime` for refactor application; pinning
///     a constant avoids threading `Lifecycle` for what is audit
///     metadata only — chapter 3.5 prescope §6 option 1).
///   - `XmlWriterSettings` pinned at every formatting axis (UTF-8
///     no-BOM encoding, two-space indentation, `\n` newlines,
///     namespace declaration emitted by the writer).
///
/// Per the codebase's no-string-concatenation discipline, **all**
/// XML construction goes through `XmlWriter`'s typed API
/// (`WriteStartElement`, `WriteAttributeString`, etc.). No `sprintf`
/// no `String.concat` on XML fragments, no manual `<Operation
/// Name="...">` interpolation. The writer handles namespace, escaping,
/// indentation, and encoding by construction; pinning the settings
/// makes the bytes deterministic.
[<RequireQualifiedAccess>]
module RefactorLogRender =

    /// SSDT's `.refactorlog` schema namespace. Pinned across SQL
    /// Server versions through SQL 2022 (the year `2012` in the
    /// namespace is the schema's revision marker, not the server's
    /// version). Per chapter 3.5 prescope §11 R1 mitigation,
    /// changing this URI invalidates DacFx's parse — keep the
    /// constant verbatim until a future SQL Server version forces
    /// the bump (re-open trigger named in DECISIONS).
    [<Literal>]
    let private xmlNamespace : string =
        "http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02"

    /// `<Operations Version="…">` attribute. Pinned to `1.0` matching
    /// SSDT's current schema.
    [<Literal>]
    let private operationsVersion : string = "1.0"

    /// `ChangeDateTime` pinned constant. DacFx ignores this attribute
    /// for refactor application; pinning a constant avoids a
    /// Lifecycle-input plumbing cost that earns nothing operationally.
    /// Chapter 3.5 prescope §6 option 1.
    [<Literal>]
    let private pinnedChangeDateTime : string = "2000-01-01T00:00:00Z"

    /// Map operation kind to the SSDT-native string. Closed-DU
    /// dispatch — adding a new variant lights up an exhaustiveness
    /// error here only.
    let private operationKindString (k: RefactorOperationKind) : string =
        match k with
        | RenameRefactor -> "Rename Refactor"

    /// Map element type to the SSDT-native string. Closed-DU
    /// dispatch — same discipline.
    let private elementTypeString (t: RefactorElementType) : string =
        match t with
        | SqlTable        -> "SqlTable"
        | SqlSimpleColumn -> "SqlSimpleColumn"
        | SqlSchema       -> "SqlSchema"

    /// Write one `<Property Name="…" Value="…" />` element through
    /// the typed `XmlWriter` API. No string interpolation; the writer
    /// handles attribute escaping by construction.
    let private writeProperty
        (w: XmlWriter)
        (propName: string)
        (propValue: string)
        : unit =
        w.WriteStartElement("Property")
        w.WriteAttributeString("Name", propName)
        w.WriteAttributeString("Value", propValue)
        w.WriteEndElement()

    /// Write one `<Operation>` element with its five `<Property>`
    /// children. The Guid uses the `"D"` format specifier (lowercase
    /// dashed form, the .NET default) — pinned for byte-determinism.
    let private writeOperation
        (w: XmlWriter)
        (entry: RefactorLogEntry)
        : unit =
        w.WriteStartElement("Operation")
        w.WriteAttributeString("Name", operationKindString entry.OperationKind)
        w.WriteAttributeString("Key", entry.OperationKey.ToString("D"))
        w.WriteAttributeString("ChangeDateTime", pinnedChangeDateTime)
        writeProperty w "ElementName"        entry.ElementName
        writeProperty w "ElementType"        (elementTypeString entry.ElementType)
        writeProperty w "ParentElementName"  entry.ParentElementName
        writeProperty w "ParentElementType"  (elementTypeString entry.ParentElementType)
        writeProperty w "NewName"            entry.NewName
        w.WriteEndElement()

    // `XmlWriterSettings` pinned at every byte-affecting axis comes
    // from `Projection.Core.XmlSettings.indentedUtf8NoBom` — the
    // single sanctioned home for the BCL's mutable settings class
    // (per the FP strict-mode discipline). `XmlWriter.Create` takes
    // a fresh instance per call; the helper returns one.

    /// Compose per-key entries into one `.refactorlog` XML document.
    /// Entries sorted by `OperationKey` (deterministic UUIDv5
    /// derivation; stable sort over the typed Guid). Per A35, every
    /// emit run produces byte-identical output for byte-identical
    /// input — verified by the golden-file test.
    let toRefactorLogXml
        (artifact: ArtifactByKind<RefactorLogEntry list>)
        : string =
        use _ = Bench.scope "render.refactorLog.toXml"
        let entries =
            artifact
            |> ArtifactByKind.toMap
            |> Map.toSeq
            |> Seq.collect snd
            |> Seq.sortBy (fun e -> e.OperationKey)
            |> Seq.toList
        use stream = new MemoryStream()
        do
            use writer = XmlWriter.Create(stream, XmlSettings.indentedUtf8NoBom ())
            writer.WriteStartDocument()
            writer.WriteStartElement("Operations", xmlNamespace)
            writer.WriteAttributeString("Version", operationsVersion)
            for entry in entries do
                writeOperation writer entry
            writer.WriteEndElement()
            writer.WriteEndDocument()
            writer.Flush()
        Encoding.UTF8.GetString(stream.ToArray())
