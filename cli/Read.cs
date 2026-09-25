using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Estate.Io;
using Estate.Kernel;

namespace Estate.Cli;

public static partial class Verbs
{
    /// <summary>What read adds to the envelope: the target read, its fingerprint, how many elements it holds, and its elements, a list that can be long.</summary>
    public static JsonObject ReadContent => new()
    {
        ["read"] = Render.Record(new() { ["from"] = Render.Text(), ["fingerprint"] = Render.Fingerprint(), ["count"] = Count(), ["elements"] = Render.Long(Render.Record(new()
        {
            ["key"] = Render.Text(),
            ["properties"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = Values() },
            ["relationships"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = Render.List(Render.Text()) },
        })) }),
    };

    /// <summary>A count of what the whole answer holds, which stands whether or not the list was cut.</summary>
    internal static JsonObject Count() => new() { ["type"] = "integer", ["minimum"] = 0 };

    /// <summary>estate read --from &lt;target&gt; [--project &lt;path&gt;]: a ref built at its commit, a package or a database, read whole (V3_ARCHITECTURE.md §8.1).</summary>
    public static Envelope Read(Checkout here, IReadOnlyList<string> words)
    {
        if (Contract.Flags(words, ["--from"], ["--project"], []).Bind(flags => SqlServer.Target.Parse(flags["--from"], "--from")
            .Bind(from => Pinned(here).Bind(pin => Reading(here, from, flags.GetValueOrDefault("--project")).Map(source => (Source: source, Pin: pin)))))
            .Failed(out var reading, out var error))
        {
            return Contract.Failed(Of("read"), error, Stamped(null, null));
        }

        var (source, fingerprint, printer) = (reading.Source, Fingerprint.Of(reading.Source.Model.Elements), new Printer());
        var elements = Render.Array(source.Model.Elements.Select(printer.Json));
        return Contract.Answer(Of("read").Output, Of("read").Outcome("done"), 0, source.Target + ": " + source.Model.Elements.Count + " elements, fingerprint " + Render.Digest(fingerprint),
            printer.Findings, Stamped(source.Image, reading.Pin), content: new JsonObject
            {
                ["read"] = new JsonObject { ["from"] = source.Target.ToString(), ["fingerprint"] = Render.Digest(fingerprint), ["count"] = source.Model.Elements.Count, ["elements"] = elements },
            });
    }

    /// <summary>
    /// The collation a model's names compare under (decision 2.26): its DatabaseOptions element's Collation property, which
    /// io/Ssdt.Elements reads from a package as its project's default collation and from a database as the database's; the comparison
    /// that follows no database when the model has none.
    /// </summary>
    internal static Result<Collation> CollationOf(SortedArray<Element> elements) =>
        elements.FirstOrDefault(e => e.Key.Type == "DatabaseOptions")?["Collation"] is Value.Text { Content: var name } ? Collation.Of(name) : Result.Ok(Collation.CaseSensitive);

    /// <summary>A case-only pair as a note: the collation reads the two spellings as one name, and DacFx plans nothing for the difference.</summary>
    internal static Finding CaseOnly(string code, Rename pair, Collation collation) => Finding.Note(code, pair.After.ToString(),
        pair.Before + " and " + pair.After + " differ in letter case alone, which " + collation.Name + " reads as one name; DacFx plans nothing for it.");

    /// <summary>A target's model read whole into elements, with the SQL Server image a database ran in; a package's model carries its refactorlog's renames.</summary>
    internal sealed record Source(SqlServer.Target Target, Ssdt.ModelElements Model, string? Image, bool IsDatabase);

    internal static Result<Source> Reading(Checkout here, SqlServer.Target target, string? project) => target.Match(
        _ => Modelled(here, target), _ => Modelled(here, target), () => Modelled(here, target),
        reference => Built(here, reference.Name, project).Bind(built => Packaged(built.Dacpac)).Map(model => new Source(target, model, null, false)),
        dacpac => Packaged(Path.GetFullPath(Path.Combine(here.WorkingDirectory, dacpac.Path))).Map(model => new Source(target, model, null, false)));

    /// <summary>A ref's project built at its commit (io/Git.At, io/Ssdt.Build): the package, and the commit.</summary>
    internal static Result<(string Dacpac, string Commit)> Built(Checkout here, string reference, string? project) => Git.At(here.Root, reference).Bind(at =>
        Ssdt.Project(at.Path, project).Bind(file => Ssdt.Tool(AppContext.BaseDirectory, here.Tool, here.WorkingDirectory)
            .Bind(tool => Ssdt.Build(at, file, tool, Path.Combine(here.Root, ".estate", "build")))).Map(built => (built.Path, at.Commit)));

    internal static Result<Ssdt.ModelElements> Packaged(string dacpac) => Ssdt.Load(dacpac).Bind(package =>
    {
        using (package)
        {
            return Ssdt.Elements(package);
        }
    });

    private static Result<Source> Modelled(Checkout here, SqlServer.Target target) => SqlServer.Resolve(target, here.Root).Bind(database =>
        SqlServer.Model(database, here.Run).Map(elements => new Source(target, new Ssdt.ModelElements(elements, []), ScratchServer.Image(database), true)));

    /// <summary>The toolchain ledger's pin, which every verb that builds reads (R13), or the rejection of a committed engine outside its window.</summary>
    internal static Result<Pin> Pinned(Checkout here) => Io.Doctor.Toolchain(here.Root, Contract.Version)
        .Bind(pin => pin.Rejects(Stamped(null, pin).Engine) is { } outside ? Result.Fail<Pin>(outside) : Result.Ok(pin));

    /// <summary>The engine as stamped: the committed DacFx, the image's digest where a copy ran in the container, and the pin when a ledger was read.</summary>
    internal static Stamp Stamped(string? image, Pin? pin) => new(Engine.Of(Io.Doctor.DacFx, image).Match(engine => engine, error => throw new UnreachableException(error.Message)), pin);

    /// <summary>
    /// The writer of the values an answer prints, and the findings printing raises (decision 2.27): a script is written through
    /// io/SchemaText, so each value a known password form sets is printed as left out, with one schema.password-literal warning per
    /// element and form, and a script ScriptDom cannot parse is left out whole, with a schema.text-unparsed note. Markdown prints no
    /// script, so the findings are the same in both forms.
    /// </summary>
    internal sealed class Printer
    {
        private readonly List<Finding> findings = [];
        private readonly HashSet<(string Key, PasswordForm Form)> warned = [];

        public IReadOnlyList<Finding> Findings => findings;

        /// <summary>An element as JSON: its key, its properties by name and its relationships' target keys in DacFx's order.</summary>
        public JsonObject Json(Element element) => new()
        {
            ["key"] = element.Key.ToString(),
            ["properties"] = new JsonObject(element.Properties.Select(p => KeyValuePair.Create(p.Name, Json(element.Key, p.Name, p.Value)))),
            ["relationships"] = new JsonObject(element.Relationships.Select(r => KeyValuePair.Create<string, JsonNode?>(r.Name, Render.Array(r.Targets.Select(t => (JsonNode?)t.Key.ToString()))))),
        };

        /// <summary>A value of <paramref name="property"/> of the element keyed <paramref name="key"/> as JSON: a boolean, an integer, a string, an enumeration as Type.Member, a script as printed, or null.</summary>
        public JsonNode? Json(ElementKey key, string property, Value? value) =>
            value?.Match<JsonNode?>(b => b, n => n, s => s, (type, member) => type + "." + member, script => Printed(key, property, script), () => null);

        private string Printed(ElementKey key, string property, string script)
        {
            switch (SchemaText.Print(script))
            {
                case PrintedScript.Printed printed:
                    foreach (var form in printed.Forms.Where(form => warned.Add((key.ToString(), form))))
                    {
                        findings.Add(Finding.Warning("schema.password-literal", key.ToString(), key + " sets a password with a literal in " + form.Syntax + "; this output leaves the value out.",
                            "Replace the literal with a SQLCMD variable the Octopus step supplies, so the repository holds no password."));
                    }

                    return printed.Text;
                case PrintedScript.Unparsed unparsed:
                    findings.Add(Finding.Note("schema.text-unparsed", key.ToString(), key + ": its " + property + " is left out of this output, because ScriptDom could not parse it at line "
                        + unparsed.Line.ToString(CultureInfo.InvariantCulture) + ", column " + unparsed.Column.ToString(CultureInfo.InvariantCulture) + " to look for a password."));
                    return SchemaText.LeftOut;
                default:
                    throw new UnreachableException();
            }
        }
    }

    internal static JsonObject Values() => new() { ["type"] = new JsonArray("boolean", "integer", "string", "null") };

    /// <summary>Whether a result failed: its value when it did not, its error when it did.</summary>
    internal static bool Failed<T>(this Result<T> result, [MaybeNullWhen(true)] out T value, [MaybeNullWhen(false)] out Error error)
    {
        (value, error) = (default, null);
        if (result is Result<T>.Ok ok)
        {
            value = ok.Value;
            return false;
        }

        error = ((Result<T>.Failed)result).Error;
        return true;
    }
}
