using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Microsoft.SqlServer.Dac.Model;

namespace DbChangeLite;

public sealed record RefactorOperation(string Key, string Name, string ElementType, string ElementName, string NewName, string NewSchema);

public static class ModelDiff
{
    public static IReadOnlyList<string> Lines(string fromPackage, string toPackage)
    {
        using var from = DacFx.Guard(() => TSqlModel.LoadFromDacpac(fromPackage, new ModelLoadOptions { LoadAsScriptBackedModel = true }), "package.unreadable", fromPackage);
        using var to = DacFx.Guard(() => TSqlModel.LoadFromDacpac(toPackage, new ModelLoadOptions { LoadAsScriptBackedModel = true }), "package.unreadable", toPackage);
        var before = Elements(from);
        var after = Elements(to);
        var operations = Refactorlog(toPackage).ExceptBy(Refactorlog(fromPackage).Select(o => o.Key), o => o.Key).ToList();
        var renames = operations.Where(o => o.Name is "Rename Refactor" or "Move Schema")
            .Select(o => (Type: TypeOf(o.ElementType), Old: o.ElementName, New: o.Name == "Move Schema" ? Names.Quote(o.NewSchema, Names.Parts(o.ElementName)[^1]) : Renamed(o))).ToList();
        var renamedKeys = renames.SelectMany(r => new[] { (r.Type, r.Old), (r.Type, r.New) }).ToHashSet();
        bool Moved((string Type, string Name) key) => renamedKeys.Contains(key) || renames.Any(r => key.Name.StartsWith(r.Old + ".", StringComparison.Ordinal) || key.Name.StartsWith(r.New + ".", StringComparison.Ordinal));
        var lines = new List<string>();
        lines.AddRange(after.Keys.Except(before.Keys).Where(k => !Moved(k)).Order().Select(k => "created " + k.Type + " " + k.Name));
        lines.AddRange(before.Keys.Except(after.Keys).Where(k => !Moved(k)).Order().Select(k => "dropped " + k.Type + " " + k.Name));
        lines.AddRange(renames.OrderBy(r => r.Old, StringComparer.Ordinal).Select(r => "renamed " + r.Type + " " + r.Old + " to " + r.Type + " " + r.New));
        lines.AddRange(operations.OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => "created RefactorLogOperation [" + o.Key + "]"));
        var renamed = renames.ToDictionary(r => r.Old, r => r.New, StringComparer.Ordinal);
        foreach (var key in before.Keys.Intersect(after.Keys).Order())
        {
            lines.AddRange(Altered(key, before[key], after[key], renamed));
        }

        lines.AddRange(Scripts(fromPackage, toPackage));
        return lines;
    }

    private static Dictionary<(string Type, string Name), TSqlObject> Elements(TSqlModel model)
    {
        var elements = new Dictionary<(string Type, string Name), TSqlObject>();
        void Add(TSqlObject element)
        {
            var name = element.Name is { HasName: true } named ? Names.Quote(named.Parts) : "<unnamed " + Fingerprint.OfString(Identity(element))[7..19] + ">";
            elements.TryAdd((element.ObjectType.Name, name), element);
            foreach (var child in element.GetChildren())
            {
                Add(child);
            }
        }

        foreach (var element in model.GetObjects(DacQueryScopes.UserDefined))
        {
            Add(element);
        }

        return elements;
    }

    private static IEnumerable<string> Altered((string Type, string Name) key, TSqlObject before, TSqlObject after, IReadOnlyDictionary<string, string> renamed)
    {
        var differences = new List<string>();
        foreach (var property in before.ObjectType.Properties.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            var (readable, old) = Value(before, property);
            var (_, now) = Value(after, property);
            if (!readable)
            {
                if (!string.Equals(Script(before), Script(after), StringComparison.Ordinal) && property.Name is "Expression")
                {
                    differences.Add(property.Name);
                }

                continue;
            }

            if (!string.Equals(old, now, StringComparison.Ordinal))
            {
                differences.Add(property.Name + (old is { Length: <= 60 } && now is { Length: <= 60 } ? " " + old + " → " + now : ""));
            }
        }

        foreach (var relationship in before.ObjectType.Relationships.OrderBy(r => r.Name, StringComparer.Ordinal))
        {
            var old = before.GetReferenced(relationship).Select(NameOf).Select(n => renamed.GetValueOrDefault(n, n)).ToList();
            var now = after.GetReferenced(relationship).Select(NameOf).ToList();
            if (!old.SequenceEqual(now, StringComparer.Ordinal))
            {
                differences.Add(relationship.Name);
            }
        }

        return differences.Select(d => key.Type + " " + key.Name + ": " + d);
    }

    private static (bool Readable, string? Value) Value(TSqlObject element, ModelPropertyClass property)
    {
        try
        {
            return (true, element.GetProperty(property) switch
            {
                null => null,
                bool b => b ? "true" : "false",
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                var other => other.ToString(),
            });
        }
        catch (ArgumentException)
        {
            return (false, null);
        }
        catch (InvalidOperationException)
        {
            return (false, null);
        }
    }

    private static string Script(TSqlObject element) => element.TryGetScript(out var script) ? script : "";

    private static string Identity(TSqlObject element) => string.Join('|', element.ObjectType.Relationships.OrderBy(r => r.Name, StringComparer.Ordinal).SelectMany(r => element.GetReferenced(r).Select(NameOf))
        .Concat(element.ObjectType.Properties.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => Value(element, p).Value ?? "")));

    private static string NameOf(TSqlObject element) => element.Name is { HasName: true } named ? Names.Quote(named.Parts) : element.ObjectType.Name;

    public static IReadOnlyList<RefactorOperation> Refactorlog(string package)
    {
        using var zip = ZipFile.OpenRead(package);
        var entry = zip.GetEntry("refactor.xml");
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        var root = XDocument.Load(stream).Root;
        return root is null ? [] : root.Elements().Where(e => e.Name.LocalName == "Operation").Select(o =>
        {
            string Property(string name) => o.Elements().FirstOrDefault(p => p.Name.LocalName == "Property" && (string?)p.Attribute("Name") == name)?.Attribute("Value")?.Value ?? "";
            return new RefactorOperation((string?)o.Attribute("Key") ?? "", (string?)o.Attribute("Name") ?? "", Property("ElementType"), Property("ElementName"), Property("NewName"), Property("NewSchema"));
        }).ToList();
    }

    private static string Renamed(RefactorOperation operation)
    {
        var parts = Names.Parts(operation.ElementName);
        return parts.Count <= 1 ? operation.NewName : Names.Quote(parts.Take(parts.Count - 1)) + "." + operation.NewName;
    }

    private static string TypeOf(string elementType) => elementType switch
    {
        "SqlSimpleColumn" or "SqlComputedColumn" => "Column",
        var t when t.StartsWith("Sql", StringComparison.Ordinal) => t[3..],
        var t => t,
    };

    private static IEnumerable<string> Scripts(string fromPackage, string toPackage)
    {
        using var from = DacFx.Load(fromPackage);
        using var to = DacFx.Load(toPackage);
        if (DacFx.Text(from.PreDeploymentScript).ReplaceLineEndings("\n") != DacFx.Text(to.PreDeploymentScript).ReplaceLineEndings("\n"))
        {
            yield return "PreDeploymentScript [PreDeploy]: Text";
        }

        if (DacFx.Text(from.PostDeploymentScript).ReplaceLineEndings("\n") != DacFx.Text(to.PostDeploymentScript).ReplaceLineEndings("\n"))
        {
            yield return "PostDeploymentScript [PostDeploy]: Text";
        }
    }
}
