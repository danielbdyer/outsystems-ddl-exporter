using System.IO;
using System.Text.Json;
using Osm.App.Configuration;
using Osm.Domain.Configuration;
using Tests.Support;

namespace Osm.Cli.Tests.Configuration;

public sealed class CliConfigurationLoaderTests
{
    [Fact]
    public async Task LoadAsync_ReturnsDefault_WhenPathMissing()
    {
        var loader = new CliConfigurationLoader();
        var result = await loader.LoadAsync(null);

        Assert.True(result.IsSuccess);
        Assert.Equal(TighteningOptions.Default, result.Value.Tightening);
        Assert.Null(result.Value.ModelPath);
    }

    [Fact]
    public async Task LoadAsync_ParsesModuleCsvAndBooleanStrings()
    {
        using var directory = new TempDirectory();
        var configPath = Path.Combine(directory.Path, "appsettings.json");
        var modelPath = Path.Combine(directory.Path, "model.json");

        await File.WriteAllTextAsync(modelPath, "{}");

        var config = new
        {
            model = new
            {
                path = "model.json",
                modules = "AppCore; ExtBilling",
                includeSystemModules = "true",
                includeInactiveModules = "false"
            }
        };

        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config));

        var loader = new CliConfigurationLoader();
        var result = await loader.LoadAsync(configPath);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "AppCore", "ExtBilling" }, result.Value.ModuleFilter.Modules);
        Assert.Equal(true, result.Value.ModuleFilter.IncludeSystemModules);
        Assert.Equal(false, result.Value.ModuleFilter.IncludeInactiveModules);
    }

    [Fact]
    public async Task LoadAsync_ReadsLegacyTighteningDocument()
    {
        using var directory = new TempDirectory();
        var configPath = Path.Combine(directory.Path, "tightening.json");
        await File.WriteAllTextAsync(configPath, CreateLegacyTighteningJson());
        var loader = new CliConfigurationLoader();

        var result = await loader.LoadAsync(configPath);

        Assert.True(result.IsSuccess);
        Assert.Equal(TighteningMode.EvidenceGated, result.Value.Tightening.Policy.Mode);
        Assert.True(result.Value.Tightening.ForeignKeys.EnableCreation);
    }

    [Fact]
    public async Task LoadAsync_ResolvesRelativePaths()
    {
        using var directory = new TempDirectory();
        var configPath = Path.Combine(directory.Path, "appsettings.json");
        var tighteningPath = Path.Combine(directory.Path, "tightening.json");
        var modelPath = Path.Combine(directory.Path, "model.json");
        var profilePath = Path.Combine(directory.Path, "profile.json");
        var typeMapPath = Path.Combine(directory.Path, "type-map.json");

        await File.WriteAllTextAsync(tighteningPath, CreateLegacyTighteningJson());
        await File.WriteAllTextAsync(modelPath, "{}");
        await File.WriteAllTextAsync(profilePath, "{}");
        await File.WriteAllTextAsync(typeMapPath, "{}");

        var config = new
        {
            tighteningPath = "tightening.json",
            model = new { path = "model.json", modules = new[] { "AppCore", "Ops" }, includeSystemModules = false, includeInactiveModules = false },
            profile = new { path = "profile.json" },
            cache = new { root = "cache" },
            profiler = new { provider = "Fixture", profilePath = "profile.json", mockFolder = "mocks" },
            sql = new { connectionString = "Server=.;Database=Test;" },
            typeMap = new { path = "type-map.json" }
        };

        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config));

        var loader = new CliConfigurationLoader();
        var result = await loader.LoadAsync(configPath);

        Assert.True(result.IsSuccess);
        Assert.Equal(Path.GetFullPath(modelPath), result.Value.ModelPath);
        Assert.Equal(Path.GetFullPath(profilePath), result.Value.ProfilePath);
        Assert.Equal(Path.Combine(directory.Path, "cache"), result.Value.Cache.Root);
        Assert.Equal("Fixture", result.Value.Profiler.Provider);
        Assert.Equal("Server=.;Database=Test;", result.Value.Sql.ConnectionString);
        Assert.Equal(new[] { "AppCore", "Ops" }, result.Value.ModuleFilter.Modules);
        Assert.Equal(false, result.Value.ModuleFilter.IncludeSystemModules);
        Assert.Equal(false, result.Value.ModuleFilter.IncludeInactiveModules);
        Assert.Equal(Path.GetFullPath(typeMapPath), result.Value.TypeMapping.Path);
    }

    [Fact]
    public async Task LoadAsync_ReadsSupplementalModelConfiguration()
    {
        using var directory = new TempDirectory();
        var configPath = Path.Combine(directory.Path, "appsettings.json");
        var supplementalPath = Path.Combine(directory.Path, "supplemental.json");

        await File.WriteAllTextAsync(supplementalPath, "{}");

        var config = new
        {
            supplementalModels = new
            {
                includeUsers = false,
                paths = new[] { "supplemental.json" }
            }
        };

        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config));

        var loader = new CliConfigurationLoader();
        var result = await loader.LoadAsync(configPath);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.SupplementalModels.IncludeUsers);
        Assert.Single(result.Value.SupplementalModels.Paths, Path.GetFullPath(supplementalPath));
    }

    private static string CreateLegacyTighteningJson()
    {
        return JsonSerializer.Serialize(new
        {
            policy = new { mode = "EvidenceGated", nullBudget = 0.0 },
            foreignKeys = new { enableCreation = true, allowCrossSchema = false, allowCrossCatalog = false },
            uniqueness = new { enforceSingleColumnUnique = true, enforceMultiColumnUnique = true },
            remediation = new
            {
                generatePreScripts = true,
                sentinels = new { numeric = "0", text = string.Empty, date = "1900-01-01" },
                maxRowsDefaultBackfill = 1000
            },
            emission = new
            {
                perTableFiles = true,
                includePlatformAutoIndexes = false,
                sanitizeModuleNames = true,
                emitBareTableOnly = false,
                namingOverrides = new { rules = Array.Empty<object>() }
            },
            mocking = new { useProfileMockFolder = false, profileMockFolder = (string?)null }
        });
    }
}
