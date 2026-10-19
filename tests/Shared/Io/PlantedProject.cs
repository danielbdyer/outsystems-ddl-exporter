using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DbChange.Budgets.Tests;

namespace DbChange.Tests;

/// <summary>
/// Uses planted in a C# file, Planted.cs, beside a project file, and the project built with dotnet: the error the compiler gives each
/// use, found by the line the use stands on, so a test states which uses must not compile, and a build with no uses shows the rest does.
/// </summary>
internal static class PlantedProject
{
    private static readonly Regex Error = new(@"Planted\.cs\((\d+),\d+\): error (\w+)", RegexOptions.CultureInvariant);

    /// <summary>
    /// Planted.cs beside <paramref name="project"/>: the lines <paramref name="before"/>, each use on a line of its own, then the lines
    /// <paramref name="after"/>; the project built; its exit and output, and each use's error code, or null where the use compiled.
    /// </summary>
    public static (int Exit, string Output, IReadOnlyList<string?> Errors) Build(string project, IReadOnlyList<string> before, IReadOnlyList<string> uses, IReadOnlyList<string> after)
    {
        var folder = Path.GetDirectoryName(project)!;
        File.WriteAllText(Path.Combine(folder, "Planted.cs"), string.Join('\n', [.. before, .. uses, .. after]));
        var (exit, output) = (Programs.InRepository("dotnet", "build", project, "-nologo", "-v", "q", "-clp:NoSummary", "-nodeReuse:false") with { Directory = folder }).Finish().Joined();
        var errors = Error.Matches(output).Select(m => (Line: int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), Code: m.Groups[2].Value)).ToList();
        return (exit, output, [.. uses.Select((_, i) => errors.FirstOrDefault(e => e.Line == before.Count + 1 + i).Code)]);
    }
}
