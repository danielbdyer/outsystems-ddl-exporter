using System.IO;
using System.Linq;
using System.Text.Json;
using Osm.Pipeline.Configuration;
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
        Assert.Empty(result.Value.ModuleFilter.EntityFilters);
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

        await File.WriteAllTextAsync(tighteningPath, CreateLegacyTighteningJson());
        await File.WriteAllTextAsync(modelPath, "{}");
        await File.WriteAllTextAsync(profilePath, "{}");

        var config = new
        {
            tighteningPath = "tightening.json",
            model = new { path = "model.json", modules = new[] { "AppCore", "Ops" }, includeSystemModules = false, includeInactiveModules = false },
            profile = new { path = "profile.json" },
            cache = new { root = "cache" },
            profiler = new { provider = "Fixture", profilePath = "profile.json", mockFolder = "mocks" },
            sql = new { connectionString = "Server=.;Database=Test;", profilingConnectionStrings = new[] { " Server=.;Database=Secondary; " } }
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
        Assert.Equal(new[] { "Server=.;Database=Secondary;" }, result.Value.Sql.ProfilingConnectionStrings);
        Assert.Equal(new[] { "AppCore", "Ops" }, result.Value.ModuleFilter.Modules);
        Assert.Equal(false, result.Value.ModuleFilter.IncludeSystemModules);
        Assert.Equal(false, result.Value.ModuleFilter.IncludeInactiveModules);
        Assert.Empty(result.Value.ModuleFilter.EntityFilters);
    }

    [Fact]
    public async Task LoadAsync_ReadsModuleEntityFilters()
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
                modules = new object[]
                {
                    new { name = "ServiceCenter", entities = new object[] { "User", "OSUSR_U_USER" } },
                    "AppCore",
                    new { name = "ExtBilling", entities = "*" }
                },
                includeSystemModules = false,
                includeInactiveModules = true
            }
        };

        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config));

        var loader = new CliConfigurationLoader();
        var result = await loader.LoadAsync(configPath);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "ServiceCenter", "AppCore", "ExtBilling" }, result.Value.ModuleFilter.Modules);
        Assert.False(result.Value.ModuleFilter.IncludeSystemModules);
        Assert.True(result.Value.ModuleFilter.IncludeInactiveModules);
        Assert.True(result.Value.ModuleFilter.EntityFilters.ContainsKey("ServiceCenter"));
        Assert.Equal(new[] { "User", "OSUSR_U_USER" }, result.Value.ModuleFilter.EntityFilters["ServiceCenter"]);
        Assert.False(result.Value.ModuleFilter.EntityFilters.ContainsKey("ExtBilling"));
        Assert.False(result.Value.ModuleFilter.EntityFilters.ContainsKey("AppCore"));
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

    [Fact]
    public async Task LoadAsync_ReadsSqlMetadataContract()
    {
        using var directory = new TempDirectory();
        var configPath = Path.Combine(directory.Path, "appsettings.json");

        var config = new
        {
            sql = new
            {
                metadataContract = new
                {
                    optionalColumns = new
                    {
                        AttributeJson = new[] { "AttributesJson", "  Extra  " },
                        ForeignKeyColumnsJson = new[] { "ColumnsJson" }
                    }
                }
            }
        };

        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config));

        var loader = new CliConfigurationLoader();
        var result = await loader.LoadAsync(configPath);

        Assert.True(result.IsSuccess);
        var optionalColumns = result.Value.Sql.MetadataContract.OptionalColumns;
        Assert.Equal(2, optionalColumns.Count);
        Assert.True(optionalColumns.ContainsKey("AttributeJson"));
        Assert.True(optionalColumns.ContainsKey("ForeignKeyColumnsJson"));
        Assert.Contains("AttributesJson", optionalColumns["AttributeJson"]);
        Assert.Contains("Extra", optionalColumns["AttributeJson"]);
        Assert.Contains("ColumnsJson", optionalColumns["ForeignKeyColumnsJson"]);
    }

    [Fact]
    public async Task LoadAsync_ReadsUatUsersConfiguration()
    {
        using var directory = new TempDirectory();
        var configPath = Path.Combine(directory.Path, "appsettings.json");

        var config = new
        {
            uatUsers = new
            {
                model = "model.json",
                connectionString = "Server=.;Database=UAT;",
                fromLiveMetadata = true,
                schema = "app",
                table = "dbo.Users",
                idColumn = "UserId",
                includeColumns = new[] { "CreatedBy", "UpdatedBy" },
                output = "out",
                userMap = "map.csv",
                allowedUsersSql = "allowed.sql",
                allowedUserIds = "allowed.csv",
                snapshot = "snapshot.json",
                entityId = "UserEntity"
            }
        };

        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config));

        var loader = new CliConfigurationLoader();
        var result = await loader.LoadAsync(configPath);

        Assert.True(result.IsSuccess);
        var uatUsers = result.Value.UatUsers;
        Assert.Equal(Path.GetFullPath(Path.Combine(directory.Path, "model.json")), uatUsers.ModelPath);
        Assert.Equal("Server=.;Database=UAT;", uatUsers.ConnectionString);
        Assert.True(uatUsers.FromLiveMetadata);
        Assert.Equal("app", uatUsers.UserSchema);
        Assert.Equal("dbo.Users", uatUsers.UserTable);
        Assert.Equal("UserId", uatUsers.UserIdColumn);
        Assert.Equal(new[] { "CreatedBy", "UpdatedBy" }, uatUsers.IncludeColumns);
        Assert.Equal(Path.GetFullPath(Path.Combine(directory.Path, "out")), uatUsers.OutputRoot);
        Assert.Equal(Path.GetFullPath(Path.Combine(directory.Path, "map.csv")), uatUsers.UserMapPath);
        Assert.Equal(Path.GetFullPath(Path.Combine(directory.Path, "allowed.sql")), uatUsers.AllowedUsersSqlPath);
        Assert.Equal(Path.GetFullPath(Path.Combine(directory.Path, "allowed.csv")), uatUsers.AllowedUserIdsPath);
        Assert.Equal(Path.GetFullPath(Path.Combine(directory.Path, "snapshot.json")), uatUsers.SnapshotPath);
        Assert.Equal("UserEntity", uatUsers.UserEntityIdentifier);
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
