using System.Linq;
using Xunit;

namespace DbChange.Budgets.Tests;

/// <summary>
/// M0 exit 1's setting: every v3 project builds with warnings as errors and the culture and ordinal rules (CA1304, CA1305, CA1307,
/// CA1309) as errors, from Directory.Build.props alone; no project clears either or silences a warning. The fast CI job builds and runs the
/// fast tests, which is the exit itself. dbchange-lite's folder (DBCHANGE_LITE.md §9) stops the search at its own Directory.Build.props,
/// which is held to the same rule.
/// </summary>
public sealed class WarningsAsErrors
{
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "D2")]
    [Trait("Exit", "M0.1")]
    public void Every_v3_project_builds_with_warnings_and_the_culture_rules_as_errors()
    {
        string[] overriding =
            ["TreatWarningsAsErrors", "MSBuildTreatWarningsAsErrors", "WarningsAsErrors", "WarningsNotAsErrors", "NoWarn"];
        string[] settings = ["Directory.Build.props", "tools/dbchange-lite/Directory.Build.props"];
        foreach (var props in settings.Select(Repository.Xml))
        {
            Assert.Equal("true", props.Descendants("TreatWarningsAsErrors").Single().Value);
            var errors = props.Descendants("WarningsAsErrors").Single().Value.Split(';');
            Assert.All((string[])["CA1304", "CA1305", "CA1307", "CA1309"], rule => Assert.Contains(rule, errors));
        }

        Assert.Empty(Repository.MsBuildFiles.Where(f => !settings.Contains(f))
            .SelectMany(f => Repository.Xml(f).Descendants().Where(e => overriding.Contains(e.Name.LocalName))
                .Select(e => f + " sets " + e.Name.LocalName)));
    }
}
