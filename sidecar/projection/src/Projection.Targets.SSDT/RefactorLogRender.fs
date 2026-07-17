namespace Projection.Targets.SSDT

open System
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
///   - `ChangeDateTime` carries the **episode's real `At`** (a
///     boundary-supplied `DateTimeOffset`, threaded through from the
///     `EpisodeCoordinate`). DacFx ignores `ChangeDateTime` for
///     refactor application, so this is audit metadata only — but the
///     audit metadata is now *truthful* (it records when the episode
///     actually emitted) rather than a fictional pinned constant.
///     T1 determinism holds because `At` is an *input* (deterministic
///     given the episode), not a clock read inside Core / the
///     renderer. The legacy no-clock overload pins `2000-01-01` for
///     callers that have no episode in hand (gap N6: the pinned
///     constant is retired from the threading path, retained only as
///     the explicit no-episode default).
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

    /// `ChangeDateTime` no-episode default. Retained only for the
    /// legacy `toRefactorLogXml` overload (callers with no episode in
    /// hand); the episode-threaded `toRefactorLogXmlAt` path no longer
    /// uses it (gap N6 — the pinned `2000-01-01` constant is retired
    /// from the real-clock threading path).
    let private pinnedChangeDateTime : DateTimeOffset =
        DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero)

    /// Render a `DateTimeOffset` to the `ChangeDateTime` wire form —
    /// UTC, ISO-8601, second precision, `Z` suffix. Pinned at every
    /// formatting axis (invariant culture, fixed format string) so the
    /// bytes are deterministic given the input instant. SSDT's GUI
    /// writes this same shape; DacFx parses it loosely but ignores the
    /// value for refactor application.
    let private changeDateTimeString (at: DateTimeOffset) : string =
        at.ToUniversalTime()
            .ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture)

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
        | SqlForeignKey   -> "SqlForeignKeyConstraint"
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
    /// `changeDateTime` is the pre-rendered episode-`At` string (one
    /// value for the whole document, threaded in by the caller).
    let private writeOperation
        (w: XmlWriter)
        (changeDateTime: string)
        (entry: RefactorLogEntry)
        : unit =
        w.WriteStartElement("Operation")
        w.WriteAttributeString("Name", operationKindString entry.OperationKind)
        w.WriteAttributeString("Key", entry.OperationKey.ToString("D"))
        w.WriteAttributeString("ChangeDateTime", changeDateTime)
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

    /// The byte-determinism core — render an entry list into one
    /// `.refactorlog` XML document at a given `ChangeDateTime`. Entries
    /// sorted by `OperationKey` (deterministic UUIDv5 derivation; stable
    /// sort over the typed Guid). Per A35, every render produces
    /// byte-identical output for byte-identical input — the timestamp is
    /// one such input, so two episodes with different `At` produce
    /// different (but each deterministic) bytes.
    let private renderEntries
        (at: DateTimeOffset)
        (entries: RefactorLogEntry list)
        : string =
        let changeDateTime = changeDateTimeString at
        let sorted = entries |> List.sortBy (fun e -> e.OperationKey)
        use stream = new MemoryStream()
        do
            use writer = XmlWriter.Create(stream, XmlSettings.indentedUtf8NoBom ())
            writer.WriteStartDocument()
            writer.WriteStartElement("Operations", xmlNamespace)
            writer.WriteAttributeString("Version", operationsVersion)
            for entry in sorted do
                writeOperation writer changeDateTime entry
            writer.WriteEndElement()
            writer.WriteEndDocument()
            writer.Flush()
        Encoding.UTF8.GetString(stream.ToArray())

    /// Compose per-key entries into one `.refactorlog` XML document at
    /// the **episode's real `At`** (the boundary-supplied
    /// `DateTimeOffset` from the `EpisodeCoordinate`). This is the
    /// real-clock path (gap N6): `ChangeDateTime` records when the
    /// episode actually emitted. T1 holds — `at` is an input,
    /// deterministic given the episode, never a clock read here.
    let toRefactorLogXmlAt
        (at: DateTimeOffset)
        (artifact: ArtifactByKind<RefactorLogEntry list>)
        : string =
        use _ = Bench.scope "render.refactorLog.toXml"
        artifact
        |> ArtifactByKind.toMap
        |> Map.toSeq
        |> Seq.collect snd
        |> Seq.toList
        |> renderEntries at

    /// Legacy no-episode overload — renders at the pinned
    /// `2000-01-01` constant. Retained for callers that have no episode
    /// in hand (and for the byte-determinism golden tests that pin the
    /// constant). New callers thread the episode's `At` via
    /// `toRefactorLogXmlAt`.
    let toRefactorLogXml
        (artifact: ArtifactByKind<RefactorLogEntry list>)
        : string =
        toRefactorLogXmlAt pinnedChangeDateTime artifact

    /// The ACCUMULATED (cross-episode) document — the flat entry-list
    /// form `RefactorLogEmitter.accumulate` produces. G3 (DECISIONS
    /// 2026-07-16): the bundle's `<project>.refactorlog` renders the
    /// timeline's whole accumulated log, not a single episode's per-kind
    /// artifact, so the flat form is the production input. Same
    /// byte-determinism core as the artifact overloads (entries sorted
    /// by `OperationKey`; pinned writer settings; `at` is an input —
    /// the episode's boundary-supplied instant — never a clock read).
    let ofEntriesAt
        (at: DateTimeOffset)
        (entries: RefactorLogEntry list)
        : string =
        use _ = Bench.scope "render.refactorLog.ofEntries"
        renderEntries at entries
