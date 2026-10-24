using System;
using System.IO;
using Osm.Domain.Configuration;
using Osm.Dmm;
using Osm.Smo;
using Osm.Validation.Tightening;
using Tests.Support;
using Xunit;

namespace Osm.Dmm.Tests;

public class SsdtTableLayoutComparatorTests
{
    private readonly SmoModel _model;
    private readonly SmoBuildOptions _options;
    private readonly SsdtTableLayoutComparator _comparator = new();

    public SsdtTableLayoutComparatorTests()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var policy = new TighteningPolicy();
        var decisions = policy.Decide(model, snapshot, TighteningOptions.Default);
        var factory = new SmoModelFactory();
        _options = SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission, applyNamingOverrides: false);
        _model = factory.Create(model, decisions, snapshot, _options);
    }

    [Fact]
    public void Compare_returns_match_when_layout_matches()
    {
        var baselineRoot = Path.Combine(
            FixtureFile.RepositoryRoot,
            "tests",
            "Fixtures",
            "emission",
            "edge-case");

        var result = _comparator.Compare(_model, _options, baselineRoot);

        Assert.True(result.IsMatch);
        Assert.Empty(result.ModelDifferences);
        Assert.Empty(result.SsdtDifferences);
    }

    [Fact]
    public void Compare_detects_missing_table_file()
    {
        using var workspace = new TempDirectory();
        var baselineRoot = Path.Combine(
            FixtureFile.RepositoryRoot,
            "tests",
            "Fixtures",
            "emission",
            "edge-case");
        TestFileSystem.CopyDirectory(baselineRoot, workspace.Path);

        var customerPath = Path.Combine(workspace.Path, "Modules", "AppCore", "dbo.Customer.sql");
        File.Delete(customerPath);

        var result = _comparator.Compare(_model, _options, workspace.Path);

        Assert.False(result.IsMatch);
        Assert.Contains(
            result.ModelDifferences,
            diff => string.Equals(diff.Property, "FilePresence", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Schema, "dbo", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Table, "Customer", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Expected, Path.Combine("Modules", "AppCore", "dbo.Customer.sql"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compare_detects_mismatched_folder()
    {
        using var workspace = new TempDirectory();
        var baselineRoot = Path.Combine(
            FixtureFile.RepositoryRoot,
            "tests",
            "Fixtures",
            "emission",
            "edge-case");
        TestFileSystem.CopyDirectory(baselineRoot, workspace.Path);

        var sourcePath = Path.Combine(workspace.Path, "Modules", "AppCore", "dbo.Customer.sql");
        var destinationDirectory = Path.Combine(workspace.Path, "Modules", "Ops");
        Directory.CreateDirectory(destinationDirectory);
        var destinationPath = Path.Combine(destinationDirectory, "dbo.Customer.sql");
        File.Move(sourcePath, destinationPath, overwrite: true);

        var result = _comparator.Compare(_model, _options, workspace.Path);

        Assert.False(result.IsMatch);
        Assert.Contains(
            result.SsdtDifferences,
            diff => string.Equals(diff.Property, "FileLocation", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Schema, "dbo", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Table, "Customer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compare_detects_unexpected_file()
    {
        using var workspace = new TempDirectory();
        var baselineRoot = Path.Combine(
            FixtureFile.RepositoryRoot,
            "tests",
            "Fixtures",
            "emission",
            "edge-case");
        TestFileSystem.CopyDirectory(baselineRoot, workspace.Path);

        var extraPath = Path.Combine(workspace.Path, "Modules", "AppCore", "dbo.Extra.sql");
        File.WriteAllText(extraPath, string.Empty);

        var result = _comparator.Compare(_model, _options, workspace.Path);

        Assert.False(result.IsMatch);
        Assert.Contains(
            result.SsdtDifferences,
            diff => string.Equals(diff.Property, "FilePresence", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Schema, "dbo", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Table, "Extra", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Actual, Path.Combine("Modules", "AppCore", "dbo.Extra.sql"), StringComparison.OrdinalIgnoreCase));
    }
}
