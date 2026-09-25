using System;
using System.Collections.Generic;
using System.Linq;
using Estate.Kernel;

namespace Estate.Io;

/// <summary>
/// DacFx's serialized type names read as the types io/Ssdt.Elements keys elements by, and the one keying rule for an object named by its
/// parts: shared by the model's objects, a refactorlog entry's element and a deploy report's item. model.xml, refactor.xml and the deploy
/// report write an object's type as Sql and its public model type's name (SqlTable for Table), save where one public type has several
/// serialized forms or another name (measured over 45 object kinds on DacFx 170.5.96): those are the exceptions below, and a name the
/// rule cannot place, one not led by Sql, has no element type.
/// </summary>
internal static class ModelTypes
{
    /// <summary>The serialized names that are not Sql and the public type's name.</summary>
    private static readonly Dictionary<string, string> Exceptions = new(StringComparer.Ordinal)
    {
        ["SqlSimpleColumn"] = "Column", ["SqlComputedColumn"] = "Column", ["SqlColumnSet"] = "Column",
        ["SqlInlineTableValuedFunction"] = "TableValuedFunction", ["SqlMultiStatementTableValuedFunction"] = "TableValuedFunction",
        ["SqlTableTypeSimpleColumn"] = "TableTypeColumn", ["SqlTableTypeComputedColumn"] = "TableTypeColumn",
        ["SqlPermissionStatement"] = "Permission", ["SqlStatistic"] = "Statistics", ["SqlUserDefinedDataType"] = "DataType",
        ["SqlSubroutineParameter"] = "Parameter",
    };

    /// <summary>The element type of a serialized type name (SqlSimpleColumn is Column), or null for a name not led by Sql.</summary>
    public static string? Element(string serialized) =>
        Exceptions.TryGetValue(serialized, out var type) ? type
        : serialized.StartsWith("Sql", StringComparison.Ordinal) && serialized.Length > 3 ? serialized[3..]
        : null;

    /// <summary>
    /// A key from name parts: under <paramref name="home"/>, each part a level down; with no home, the first one or two parts at the top and
    /// each further part a level down, the type given at each level.
    /// </summary>
    public static Result<ElementKey> Key(string type, string[] parts, ElementKey? home) => parts.Skip(home is null ? 2 : 0).Aggregate(
        home is not null ? Result.Ok(home) : (parts.Length > 1 ? Name.Of(parts[0], parts[1]) : Name.Of(parts.FirstOrDefault() ?? "")).Bind(name => ElementKey.Of(type, name)),
        (key, part) => key.Bind(parent => Name.Of(part).Bind(name => ElementKey.Of(parent, type, name))));

    /// <summary>A name's parts less the run of its parent's name parts (ignoring case) where that run leaves one or more; else all of them.</summary>
    public static string[] Beneath(string[] own, string[] home) => Enumerable.Range(0, Math.Max(0, own.Length - home.Length + 1))
        .Where(i => own.Skip(i).Take(home.Length).SequenceEqual(home, StringComparer.OrdinalIgnoreCase)).Select(i => (string[])[.. own[..i], .. own[(i + home.Length)..]]).FirstOrDefault(rest => rest.Length > 0) ?? own;
}
