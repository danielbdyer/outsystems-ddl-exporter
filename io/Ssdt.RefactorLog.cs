using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using DbChange.Kernel;

namespace DbChange.Io;

public static partial class Ssdt
{
    /// <summary>
    /// One refactorlog operation as SSDT writes it (an Operation element): its key; its name as written (Rename Refactor, Move Schema) and
    /// what dbchange reads it as; its ChangeDateTime as written; the element it acts on and that element's serialized type; the parent and
    /// its type, where the element has one; and the new name of a rename or the new schema of a move.
    /// </summary>
    public sealed record RefactorLogOperation(
        string Key, string Operation, string? ChangeDateTime, string ElementName, string? ElementType, string? ParentName,
        string? ParentType, string? NewName, string? NewSchema)
    {
        /// <summary>
        /// What the operation does to its element's key, read from what SSDT records with it: a NewName is a rename (Rename Refactor), a
        /// NewSchema a move to another schema (Move Schema), the two that move a key; any other operation moves nothing.
        /// </summary>
        public RefactorOperationKind Kind => (NewName, NewSchema) switch
        {
            (not null, _) => RefactorOperationKind.Rename,
            (_, not null) => RefactorOperationKind.MoveSchema,
            _ => RefactorOperationKind.Other,
        };
    }

    /// <summary>
    /// What a refactorlog operation does to an element's key: renames it, moves it to another schema, or nothing (any other operation SSDT
    /// records).
    /// </summary>
    public enum RefactorOperationKind
    {
        Rename,
        MoveSchema,
        Other,
    }

    private static readonly XNamespace Dac = "http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02";

    /// <summary>A .refactorlog file's operations, read as Open reads the copy a build puts in the package.</summary>
    public static Result<IReadOnlyList<RefactorLogOperation>> RefactorLog(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            return Result.Ok(Operations(file));
        }
        catch (Exception e) when (e is IOException or XmlException or UnauthorizedAccessException)
        {
            return new Error("refactorlog.unreadable", path + " is not a refactorlog SSDT reads: " + e.Message,
                "Restore the file from git, then repeat the rename in Visual Studio so SSDT writes its entry.");
        }
    }

    /// <summary>
    /// The operations of a refactorlog, in file order. A build's copy leaves the root element outside the namespace its
    /// operations carry, so the operations are found by name wherever they sit; one with no key, name or element makes the whole file
    /// unreadable.
    /// </summary>
    private static IReadOnlyList<RefactorLogOperation> Operations(Stream log)
    {
        using var reader = XmlReader.Create(log, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        return [.. XDocument.Load(reader).Descendants(Dac + "Operation").Select(operation =>
        {
            string? Property(string name) =>
                (string?)operation.Elements(Dac + "Property").FirstOrDefault(p => (string?)p.Attribute("Name") == name)?.Attribute("Value");
            string Required(string? value, string what) => value ?? throw new XmlException("A refactorlog operation has no " + what + ".");
            return new RefactorLogOperation(
                Required((string?)operation.Attribute("Key"), "Key"), Required((string?)operation.Attribute("Name"), "Name"),
                (string?)operation.Attribute("ChangeDateTime"),
                Required(Property("ElementName"), "ElementName"), Property("ElementType"), Property("ParentElementName"),
                Property("ParentElementType"), Property("NewName"), Property("NewSchema"));
        })];
    }

    /// <summary>A refactorlog operation as an element: its key, and as text each attribute and property the file gives it.</summary>
    private static Result<Element> Entry(RefactorLogOperation r) => Element.RefactorLogEntry(r.Key, new (string Name, string? Value)[] {
        ("Operation", r.Operation), ("ChangeDateTime", r.ChangeDateTime), ("ElementName", r.ElementName), ("ElementType", r.ElementType),
        ("ParentElementName", r.ParentName), ("ParentElementType", r.ParentType), ("NewName", r.NewName), ("NewSchema", r.NewSchema) }
        .Where(p => p.Value is not null).Select(p => new Element.Property(p.Name, new Value.Text(p.Value!))));

    /// <summary>
    /// An operation's rename: its element's key (past two parts, under its parent's, as Elements keys it) to the key its NewName or
    /// NewSchema gives, each serialized type read through ModelTypes and each name through ScriptDom.
    /// </summary>
    private static Result<Rename> Renaming(RefactorLogOperation r) =>
        Parts(r.ElementName).Bind(parts => parts.Length > 2 && r.ParentName is { } parent
                ? Parts(parent).Bind(home =>
                    ModelTypes.Key(TypeOf(r.ParentType), home, null)
                    .Bind(key => ModelTypes.Key(TypeOf(r.ElementType), ModelTypes.Beneath(parts, home), key)))
                : ModelTypes.Key(TypeOf(r.ElementType), parts, null))
            .Bind(before => r.Kind == RefactorOperationKind.Rename
                ? Parts(r.NewName!).Bind(n => Rename.Of(before, n[^1]))
                : Parts(r.NewSchema!).Bind(s => Name.Of(s[^1], before.Name.Base)).Bind(n => ElementKey.Of(before.Type, n))
                    .Map(after => new Rename(before, after)));

    /// <summary>An operation's element type, written as DacFx serializes it; a name the map cannot place keys as written.</summary>
    private static string TypeOf(string? serialized) => serialized is null ? "" : ModelTypes.Element(serialized) ?? serialized;

    private static Result<string[]> Parts(string name) => TSql.NameParts(name) is { } parts ? parts
        : new Error("refactorlog.name", "The refactorlog names " + name + ", which is not a name of one to four parts.",
            "Restore the refactorlog from git, then repeat the rename in Visual Studio so SSDT writes its entry.");
}
