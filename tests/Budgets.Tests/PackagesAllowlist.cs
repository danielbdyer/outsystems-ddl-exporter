using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Estate.Budgets.Tests;

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
    public void Every_project_restores_from_its_lock_file_and_CI_restores_locked()
    {
        var unlocked = Repository.MsBuildFiles
            .Where(f => f.EndsWith(".csproj", StringComparison.Ordinal))
            .Where(f => !Repository.Files.Contains(Path.GetDirectoryName(f)!.Replace('\\', '/') + "/packages.lock.json"))
            .Select(f => f + " has no packages.lock.json beside it");
        var properties = Repository.Xml("Directory.Build.props").Descendants().ToList();

        Assert.Empty(unlocked);
        Assert.Contains(properties, e => e.Name.LocalName == "RestorePackagesWithLockFile" && e.Value == "true");
        Assert.Contains(properties, e => e.Name.LocalName == "RestoreLockedMode" && e.Value == "true" && ((string?)e.Attribute("Condition") ?? "").Contains("$(CI)", StringComparison.Ordinal));
    }

    private static string[] Named(string msbuildFile, params string[] items) => Repository.Xml(msbuildFile).Descendants()
        .Where(e => items.Contains(e.Name.LocalName))
        .Select(e => (string?)e.Attribute("Include") ?? (string?)e.Attribute("Update") ?? "")
        .Where(p => p.Length > 0)
        .ToArray();
}
