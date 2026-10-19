using System;
using System.IO;
using System.Linq;
using Xunit;

namespace DbChange.Budgets.Tests;

/// <summary>
/// The supply chain is pinned: every PackageVersion in Directory.Packages.props, and every PackageReference or
/// PackageDownload in a v3 MSBuild file, is a line of ci/packages.allow; every line there is used; every project
/// restores from its lock file, and CI restores locked. A new package is a decision line, a PackageVersion and a line.
/// </summary>
public sealed class PackagesAllowlist
{
    // A line's first word is the package; a licence may follow it.
    private static readonly string[] Allowed = Repository.Lines("ci/packages.allow")
        .Select(l => l.Trim())
        .Where(l => l.Length > 0 && !l.StartsWith('#'))
        .Select(l => l.Split(' ', '\t')[0])
        .ToArray();

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "X3")]
    [Trait("Value", "L9")]
    public void Every_package_is_allowed_and_every_allowed_package_is_used()
    {
        var versions = Named("Directory.Packages.props", "PackageVersion");
        var references = Repository.MsBuildFiles.SelectMany(f => Named(f, "PackageReference", "PackageDownload")).ToList();

        Assert.Empty(versions.Concat(references).Except(Allowed, StringComparer.OrdinalIgnoreCase)
            .Select(p => "ci/packages.allow: " + p + " is not allowed; a new package is a decision line and a line here"));
        Assert.Empty(Allowed.Except(references, StringComparer.OrdinalIgnoreCase)
            .Select(p => "ci/packages.allow: " + p + " is referenced by no v3 project; remove its line and its PackageVersion"));
    }

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "X3")]
    public void Every_project_restores_from_its_lock_file_and_CI_restores_locked()
    {
        var unlocked = Repository.MsBuildFiles
            .Where(f => f.EndsWith(".csproj", StringComparison.Ordinal))
            .Where(f => !Repository.Files.Contains(Path.GetDirectoryName(f)!.Replace('\\', '/') + "/packages.lock.json"))
            .Select(f => f + " has no packages.lock.json beside it");
        var properties = Repository.Xml("Directory.Build.props").Descendants().ToList();

        Assert.Empty(unlocked);
        Assert.Contains(properties, e => e.Name.LocalName == "RestorePackagesWithLockFile" && e.Value == "true");
        Assert.Contains(properties, e => e.Name.LocalName == "RestoreLockedMode" && e.Value == "true"
            && ((string?)e.Attribute("Condition") ?? "").Contains("$(CI)", StringComparison.Ordinal));
    }

    /// <summary>
    /// One version per package holds for every v3 project but one (DECISIONS.md, 2026-10-19): tests/DacFx162/ runs DacFx 162.5.57,
    /// the release the pipeline is presumed to run, beside the committed one, and no other MSBuild file overrides a central version.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "X3")]
    public void Only_the_DacFx_162_runner_overrides_a_central_package_version()
    {
        var overrides = Repository.MsBuildFiles.SelectMany(f => Repository.Xml(f).Descendants()
            .Where(e => e.Attribute("VersionOverride") is not null)
            .Select(e => f + ": " + ((string?)e.Attribute("Include") ?? "") + " " + (string?)e.Attribute("VersionOverride")));

        Assert.Equal(["tests/DacFx162/DacFx162.csproj: Microsoft.SqlServer.DacFx 162.5.57"], overrides);
    }

    private static string[] Named(string msbuildFile, params string[] items) => Repository.Xml(msbuildFile).Descendants()
        .Where(e => items.Contains(e.Name.LocalName))
        .Select(e => (string?)e.Attribute("Include") ?? (string?)e.Attribute("Update") ?? "")
        .Where(p => p.Length > 0)
        .ToArray();
}
