using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Estate.Io;
using Estate.Kernel;

namespace Estate.Cli;

public static partial class Verbs
{
    /// <summary>What read adds to the envelope: the target read, its fingerprint, and its elements.</summary>
    public static JsonObject ReadContent => new()
    {
        ["read"] = Render.Record(new() { ["from"] = Render.Text(), ["fingerprint"] = Render.Fingerprint(), ["elements"] = Render.List(Render.Record(new()
        {
            ["key"] = Render.Text(),
            ["properties"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = Values() },
            ["relationships"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = Render.List(Render.Text()) },
        })) }),
    };

    /// <summary>estate read --from &lt;target&gt; [--project &lt;path&gt;]: a ref built at its commit, a package or a database, read whole (V3_ARCHITECTURE.md §8.1).</summary>
    public static Envelope Read(Checkout here, IReadOnlyList<string> words)
    {
        if (Contract.Flags(words, ["--from"], ["--project"], []).Bind(flags => SqlServer.Target.Parse(flags["--from"], "--from")
            .Bind(from => Pinned(here).Bind(pin => Reading(here, from, flags.GetValueOrDefault("--project")).Map(source => (Source: source, Pin: pin)))))
            .Failed(out var read, out var error))
        {
            return Contract.Failed(Of("read"), error, Stamped(null, null));
        }

        var (source, fingerprint) = (read.Source, Fingerprint.Of(read.Source.Read.Elements));
        return Contract.Answer(Of("read").Output, "done", source.Target + ": " + source.Read.Elements.Count + " elements, fingerprint " + Render.Digest(fingerprint), [], 0,
            Stamped(source.Image, read.Pin), content: new JsonObject
            {
                ["read"] = new JsonObject { ["from"] = source.Target.ToString(), ["fingerprint"] = Render.Digest(fingerprint), ["elements"] = Render.Array(source.Read.Elements.Select(Json)) },
            });
    }

    /// <summary>A target read whole, with the SQL Server image a database ran in; a package's read carries its refactorlog's renames.</summary>
    internal sealed record Source(SqlServer.Target Target, Ssdt.Read Read, string? Image, bool IsDatabase);

    internal static Result<Source> Reading(Checkout here, SqlServer.Target target, string? project) => target.Match(
        _ => Modelled(here, target), _ => Modelled(here, target), () => Modelled(here, target),
        reference => Built(here, reference.Name, project).Bind(built => Packaged(built.Dacpac)).Map(read => new Source(target, read, null, false)),
        dacpac => Packaged(Path.GetFullPath(Path.Combine(here.WorkingDirectory, dacpac.Path))).Map(read => new Source(target, read, null, false)));

    /// <summary>A ref's project built at its commit (io/Git.At, io/Ssdt.Build): the package, and the commit.</summary>
    internal static Result<(string Dacpac, string Commit)> Built(Checkout here, string reference, string? project) => Git.At(here.Root, reference).Bind(at =>
        Ssdt.Project(at.Path, project).Bind(file => Ssdt.Tool(AppContext.BaseDirectory, here.Tool, here.WorkingDirectory)
            .Bind(tool => Ssdt.Build(at, file, tool, Path.Combine(here.Root, ".estate", "build")))).Map(built => (built.Path, at.Commit)));

    internal static Result<Ssdt.Read> Packaged(string dacpac) => Ssdt.Load(dacpac).Bind(package =>
    {
        using (package)
        {
            return Ssdt.Walk(package);
        }
    });

    private static Result<Source> Modelled(Checkout here, SqlServer.Target target) => SqlServer.Resolve(target, here.Root).Bind(database =>
        SqlServer.Model(database, SqlServer.QueryLog.Start(here.Root)).Map(elements => new Source(target, new Ssdt.Read(elements, []), Substrate.Image(database), true)));

    /// <summary>The toolchain ledger's pin, which every verb that builds reads (R13), or the rejection of a committed engine outside its window.</summary>
    internal static Result<Pin> Pinned(Checkout here) => Io.Doctor.Toolchain(here.Root, Contract.Version)
        .Bind(pin => pin.Rejects(Stamped(null, pin).Engine) is { } outside ? Result.Fail<Pin>(outside) : Result.Ok(pin));

    /// <summary>The engine as stamped: the committed DacFx, the image's digest where a copy ran in the container, and the pin when a ledger was read.</summary>
    internal static Stamp Stamped(string? image, Pin? pin) => new(Engine.Of(Io.Doctor.DacFx, image).Match(engine => engine, error => throw new UnreachableException(error.Message)), pin);

    /// <summary>An element as JSON: its key, its properties by name and its relationships' target keys in DacFx's order.</summary>
    internal static JsonObject Json(Element element) => new()
    {
        ["key"] = element.Key.ToString(),
        ["properties"] = new JsonObject(element.Properties.Select(p => KeyValuePair.Create(p.Name, Json(p.Value)))),
        ["relationships"] = new JsonObject(element.Relationships.Select(r => KeyValuePair.Create<string, JsonNode?>(r.Name, Render.Array(r.Targets.Select(t => (JsonNode?)t.Key.ToString()))))),
    };

    /// <summary>A value as JSON: a boolean, an integer, a string, an enumeration as Type.Member, or null.</summary>
    internal static JsonNode? Json(Value? value) => value?.Match<JsonNode?>(b => b, n => n, s => s, (type, member) => type + "." + member, () => null);

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
