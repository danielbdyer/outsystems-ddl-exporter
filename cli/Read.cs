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
        if (DacFx.Version.Failed(out var dacfx, out var error))
        {
            return Contract.Failed(Of("read"), error);
        }

        var stamp = new Stamp(dacfx);
        if (Contract.Flags(words, ["--from"], ["--project"], []).Bind(flags => SqlServer.Target(flags["--from"], "--from").Map(from => (Flags: flags, From: from)))
            .Bind(asked => Io.Doctor.Toolchain(here.Root, Contract.Version).Map(pin => (asked.Flags, asked.From, Pin: pin))).Failed(out var asked, out error))
        {
            return Contract.Failed(Of("read"), error, stamp);
        }

        stamp = stamp with { Pin = asked.Pin };
        if ((asked.Pin.Rejects(dacfx) is { } outside ? Result.Fail<Source>(outside) : Reading(here, asked.From, asked.Flags.GetValueOrDefault("--project"))).Failed(out var source, out error))
        {
            return Contract.Failed(Of("read"), error, stamp);
        }

        var (fingerprint, printer) = (Fingerprint.Of(source.Model.Elements), new Printer());
        var elements = Render.Array(source.Model.Elements.Select(printer.Json));
        return Contract.Answer(Of("read").Output, Of("read").Outcome("done"), 0, source.Target + ": " + source.Model.Elements.Count + " elements, fingerprint " + Render.Digest(fingerprint),
            [.. source.Notes, .. printer.Findings], stamp with { Server = source.Server }, content: new JsonObject
            {
                ["read"] = new JsonObject { ["from"] = source.Target.ToString(), ["fingerprint"] = Render.Digest(fingerprint), ["count"] = source.Model.Elements.Count, ["elements"] = elements },
            });
    }

    /// <summary>A case-only pair as a note: the collation reads the two spellings as one name, and DacFx plans nothing for the difference.</summary>
    internal static Finding CaseOnly(string code, Rename pair, Collation collation) => Finding.Note(code, pair.After.ToString(),
        pair.Before + " and " + pair.After + " differ in letter case alone, which " + collation.Name + " reads as one name; DacFx plans nothing for it.");

    /// <summary>
    /// A target's model read whole into elements, with the SQL Server a copy runs on; a package's model carries its refactorlog's renames,
    /// and a database's read says what the identity could read there.
    /// </summary>
    internal sealed record Source(Target Target, Ssdt.ModelElements Model, Server? Server, bool IsDatabase, SqlServer.Readable? Readable = null)
    {
        /// <summary>The notes reading the target raised: each error DacFx found in the model, and, for a database, an identity without the server's scope.</summary>
        public IEnumerable<Finding> Notes => [.. Model.Notes(Target.ToString()), .. Readable?.Notes ?? []];
    }

    internal static Result<Source> Reading(Checkout here, Target target, string? project) => target.Match(
        _ => Modelled(here, target), _ => Modelled(here, target), () => Modelled(here, target),
        reference => Ssdt.Build(here.Root, reference.Ref.ToString(), project, here.Tool, here.WorkingDirectory).Bind(built => Packaged(built.Built.Path))
            .Map(model => new Source(target, model, null, false)),
        dacpac => Packaged(Path.GetFullPath(Path.Combine(here.WorkingDirectory, dacpac.Path))).Map(model => new Source(target, model, null, false)));

    /// <summary>A package's model read into elements, the package opened once and released.</summary>
    private static Result<Ssdt.ModelElements> Packaged(string dacpac) => Ssdt.Open(dacpac).Bind(package =>
    {
        using (package)
        {
            return package.Elements;
        }
    });

    /// <summary>A database read once (io/DacFx.Extract), after this identity is found to hold VIEW DEFINITION there, with a copy's SQL Server and what the identity could read.</summary>
    private static Result<Source> Modelled(Checkout here, Target target) => SqlServer.Resolve(target, here.Root).Bind(database => SqlServer.Reach(database, here.Run)
        .Bind(readable => DacFx.Extract(database).Bind(package =>
        {
            using (package)
            {
                return package.Elements;
            }
        })
        .Bind(model => (database is SqlServer.Copy copy ? SqlServer.ServerOf(copy, here.Run).Map(server => (Server?)server) : Result.Ok<Server?>(null))
            .Map(server => new Source(target, model, server, true, readable)))));

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
