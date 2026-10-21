using System;
using System.Linq;
using System.Text.RegularExpressions;
using DbChange.Io;
using Xunit;

namespace DbChange.Budgets.Tests;

/// <summary>
/// R4, nothing generated is committed or shared (M3 exit 5): git tracks no SQL Server backup (.bak), no data-tier export (.bacpac), no
/// package (.dacpac) and no database file (.mdf, .ldf), and nothing a build or a run writes, the output under bin/, obj/ and dist/,
/// TestResults/ and the tool's local state under .dbchange/, where the registry, the runs and the copy cache live; and the root .gitignore
/// ignores .dbchange/ and dist/ at the root, so a run's state and the published tool are never offered to a commit.
/// </summary>
public sealed class NothingGenerated
{
    private static readonly Regex Generated = new(@"\.(?:bak|bacpac|dacpac|mdf|ldf)$|(?:^|/)(?:bin|obj|dist|TestResults|\.dbchange)/",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Exit", "M3.5")]
    public void No_backup_package_or_build_output_is_tracked_and_the_local_state_folder_is_ignored()
    {
        var tracked = Git("ls-files", "-z", "--cached").Split('\0', StringSplitOptions.RemoveEmptyEntries);

        Assert.NotEmpty(tracked);
        Assert.DoesNotContain(tracked, f => Generated.IsMatch(f));
        Assert.Equal([".gitignore:.dbchange/\t.dbchange/copies.json", ".gitignore:dist/\tdist/dbchange/dbchange.dll"],
            Git("check-ignore", "-v", "--no-index", ".dbchange/copies.json", "dist/dbchange/dbchange.dll")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => Regex.Replace(line, @"^([^:]+):\d+:", "$1:")));
    }

    /// <summary>The scan as minimal pairs: each generated path it refuses beside a tracked path of the same shape it keeps.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("tests/Golden/copies/base.bak", true)]
    [InlineData("tests/Golden/project/bin/Debug/SampleCatalog.dacpac", true)]
    [InlineData("exports/Dev.bacpac", true)]
    [InlineData(".dbchange/copies.json", true)]
    [InlineData("cli/obj/project.assets.json", true)]
    [InlineData("dist/dbchange/dbchange.dll", true)]
    [InlineData("tests/Golden/dbchange/environments.json", false)]
    [InlineData("tests/Golden/copies/README.md", false)]
    [InlineData("ci/publish.sh", false)]
    [InlineData("io/Ssdt.Package.cs", false)]
    public void The_scan_refuses_a_backup_a_package_and_a_build_output_and_keeps_a_file_of_the_same_folder(string path, bool generated) =>
        Assert.Equal(generated, Generated.IsMatch(path));

    private static string Git(params string[] arguments) =>
        new Command("git", arguments, TimeSpan.FromMinutes(2)) { Directory = Repository.Root }.Run() switch
        {
            Ran.Exited { Code: 0 } ran => ran.Output,
            var failed => throw new InvalidOperationException("git " + string.Join(' ', arguments) + " failed in " + Repository.Root + ": "
                + failed),
        };
}
