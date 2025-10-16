using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Tests.Support;
using Xunit;
using CommandResult = Osm.Cli.Tests.DotNetCli.CommandResult;

namespace Osm.Cli.Tests;

public class CliIntegrationTests
{
    [Fact]
    public async Task BuildSsdt_and_dmm_compare_complete_successfully()
    {
        var repoRoot = FixtureFile.RepositoryRoot;
        var cliProject = Path.Combine(repoRoot, "src", "Osm.Cli", "Osm.Cli.csproj");
        var modelPath = FixtureFile.GetPath("model.edge-case.json");
        var profilePath = FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json"));
        var staticDataPath = FixtureFile.GetPath(Path.Combine("static-data", "static-entities.edge-case.json"));
        var smokeSnapshotRoot = Path.Combine(repoRoot, "tests", "Fixtures", "emission-smoke", "edge-case");

        using var output = new TempDirectory();
        using var comparisonWorkspace = new TempDirectory();
        var diffPath = Path.Combine(output.Path, "dmm-diff.json");

        var buildResult = await RunCliAsync(repoRoot, $"run --project {cliProject} -- build-ssdt --model {modelPath} --profile {profilePath} --static-data {staticDataPath} --max-degree-of-parallelism 2 --out {output.Path}");
        AssertExitCode(buildResult, 0);

        var manifestPath = Path.Combine(output.Path, "manifest.json");
        Assert.True(File.Exists(manifestPath));

        var decisionLogPath = Path.Combine(output.Path, "policy-decisions.json");
        Assert.True(File.Exists(decisionLogPath));
        using (var decisionStream = File.OpenRead(decisionLogPath))
        using (var document = System.Text.Json.JsonDocument.Parse(decisionStream))
        {
            var root = document.RootElement;
            Assert.True(root.GetProperty("TightenedColumnCount").GetInt32() >= 0);
            Assert.True(root.GetProperty("UniqueIndexCount").GetInt32() >= 0);
            Assert.True(root.GetProperty("Columns").GetArrayLength() > 0);
            Assert.True(root.GetProperty("UniqueIndexes").GetArrayLength() >= 0);
        }

        var emission = EmissionOutput.Load(output.Path);

        Assert.Equal(4, emission.Manifest.Tables.Count);
        Assert.Contains("Customer", emission.Manifest.Tables.Select(t => t.Table));
        Assert.True(emission.Manifest.Options.SanitizeModuleNames);

        using (var subset = emission.CreateSnapshot(
                   "manifest.json",
                   "policy-decisions.json",
                   "Modules/AppCore/Tables/dbo.Customer.sql"))
        {
            DirectorySnapshot.AssertMatches(smokeSnapshotRoot, subset.Path);
        }

        var dmmScriptPath = Path.Combine(comparisonWorkspace.Path, "edge-case.dmm.sql");
        await File.WriteAllTextAsync(dmmScriptPath, EdgeCaseScript);

        var compareResult = await RunCliAsync(repoRoot, $"run --project {cliProject} -- dmm-compare --model {modelPath} --profile {profilePath} --dmm {dmmScriptPath} --max-degree-of-parallelism 2 --out {output.Path}");
        AssertExitCode(compareResult, 0);

        Assert.True(File.Exists(diffPath));
        using (var diffStream = File.OpenRead(diffPath))
        using (var diffJson = JsonDocument.Parse(diffStream))
        {
            var root = diffJson.RootElement;
            Assert.True(root.GetProperty("isMatch").GetBoolean());
            Assert.Equal(modelPath, root.GetProperty("modelPath").GetString());
            Assert.Equal(profilePath, root.GetProperty("profilePath").GetString());
            Assert.Equal(dmmScriptPath, root.GetProperty("dmmPath").GetString());
            Assert.Equal(0, root.GetProperty("modelDifferences").GetArrayLength());
            Assert.Equal(0, root.GetProperty("ssdtDifferences").GetArrayLength());
        }
    }

    [Fact]
    public async Task DmmCompare_writes_diff_artifact_when_drift_detected()
    {
        var repoRoot = FixtureFile.RepositoryRoot;
        var cliProject = Path.Combine(repoRoot, "src", "Osm.Cli", "Osm.Cli.csproj");
        var modelPath = FixtureFile.GetPath("model.edge-case.json");
        var profilePath = FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json"));

        using var output = new TempDirectory();
        var diffPath = Path.Combine(output.Path, "dmm-diff.json");

        var dmmScriptPath = Path.Combine(output.Path, "edge-case.dmm.sql");
        await File.WriteAllTextAsync(dmmScriptPath, MismatchedScript);

        var compareResult = await RunCliAsync(repoRoot, $"run --project {cliProject} -- dmm-compare --model {modelPath} --profile {profilePath} --dmm {dmmScriptPath} --out {output.Path}");
        AssertExitCode(compareResult, 2);

        Assert.True(File.Exists(diffPath));
        using var diffStream = File.OpenRead(diffPath);
        using var diffJson = JsonDocument.Parse(diffStream);
        var root = diffJson.RootElement;

        Assert.False(root.GetProperty("isMatch").GetBoolean());
        Assert.Equal(modelPath, root.GetProperty("modelPath").GetString());
        Assert.Equal(profilePath, root.GetProperty("profilePath").GetString());
        Assert.Equal(dmmScriptPath, root.GetProperty("dmmPath").GetString());

        var ssdtDifferences = root.GetProperty("ssdtDifferences");
        Assert.True(ssdtDifferences.GetArrayLength() > 0);
        Assert.Contains(
            ssdtDifferences.EnumerateArray(),
            element => string.Equals(element.GetProperty("property").GetString(), "Nullability", StringComparison.OrdinalIgnoreCase)
                && string.Equals(element.GetProperty("column").GetString(), "EMAIL", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildSsdt_honors_rename_table_overrides()
    {
        var repoRoot = FixtureFile.RepositoryRoot;
        var cliProject = Path.Combine(repoRoot, "src", "Osm.Cli", "Osm.Cli.csproj");
        var modelPath = FixtureFile.GetPath("model.edge-case.json");
        var profilePath = FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json"));
        var staticDataPath = FixtureFile.GetPath(Path.Combine("static-data", "static-entities.edge-case.json"));

        using var output = new TempDirectory();
        var command = $"run --project {cliProject} -- build-ssdt --model \"{modelPath}\" --profile \"{profilePath}\" --static-data \"{staticDataPath}\" --max-degree-of-parallelism 2 --out \"{output.Path}\" --rename-table dbo.OSUSR_ABC_CUSTOMER=CUSTOMER_PORTAL";
        var result = await RunCliAsync(repoRoot, command);
        AssertExitCode(result, 0);

        var emission = EmissionOutput.Load(output.Path);

        var renamed = Assert.Single(emission.Manifest.Tables.Where(t => t.Table == "CUSTOMER_PORTAL"));
        Assert.Equal("Modules/AppCore/Tables/dbo.CUSTOMER_PORTAL.sql", renamed.TableFile);
        Assert.DoesNotContain(emission.Manifest.Tables, t => t.Table == "Customer");

        Assert.Contains("Modules/AppCore/Tables/dbo.CUSTOMER_PORTAL.sql", emission.TableScripts);
        Assert.DoesNotContain(
            emission.TableScripts,
            path => path.Equals("Modules/AppCore/Tables/dbo.Customer.sql", StringComparison.OrdinalIgnoreCase));

        var script = await File.ReadAllTextAsync(emission.GetAbsolutePath(renamed.TableFile));
        Assert.Contains("CREATE TABLE [dbo].[CUSTOMER_PORTAL]", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OSUSR_ABC_CUSTOMER", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildSsdt_honors_entity_override_from_configuration()
    {
        var repoRoot = FixtureFile.RepositoryRoot;
        var cliProject = Path.Combine(repoRoot, "src", "Osm.Cli", "Osm.Cli.csproj");

        using var workspace = new TempDirectory();
        var tighteningPath = Path.Combine(workspace.Path, "tightening.json");
        var defaultTighteningPath = Path.Combine(repoRoot, "config", "default-tightening.json");
        var tighteningJson = await File.ReadAllTextAsync(defaultTighteningPath);
        var tighteningNode = JsonNode.Parse(tighteningJson)!;
        var emissionNode = tighteningNode["emission"] as JsonObject ?? new JsonObject();
        tighteningNode["emission"] = emissionNode;
        var namingOverridesNode = emissionNode["namingOverrides"] as JsonObject ?? new JsonObject();
        emissionNode["namingOverrides"] = namingOverridesNode;
        var rules = namingOverridesNode["rules"] as JsonArray ?? new JsonArray();
        rules.Clear();
        rules.Add(new JsonObject
        {
            ["entity"] = "Customer",
            ["override"] = "CUSTOMER_STATIC"
        });
        namingOverridesNode["rules"] = rules;
        await File.WriteAllTextAsync(
            tighteningPath,
            tighteningNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var configPath = Path.Combine(workspace.Path, "appsettings.json");
        var outputPath = Path.Combine(workspace.Path, "out");

        var config = new
        {
            tighteningPath,
            model = new { path = FixtureFile.GetPath("model.edge-case.json") },
            profile = new { path = FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json")) }
        };

        await File.WriteAllTextAsync(
            configPath,
            JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

        var staticDataPath = FixtureFile.GetPath(Path.Combine("static-data", "static-entities.edge-case.json"));
        var command = $"run --project {cliProject} -- build-ssdt --config \"{configPath}\" --static-data \"{staticDataPath}\" --out \"{outputPath}\"";
        var result = await RunCliAsync(repoRoot, command);
        AssertExitCode(result, 0);

        var emission = EmissionOutput.Load(outputPath);

        var renamed = Assert.Single(emission.Manifest.Tables.Where(t => t.Table == "CUSTOMER_STATIC"));
        Assert.Equal("Modules/AppCore/Tables/dbo.CUSTOMER_STATIC.sql", renamed.TableFile);
        Assert.DoesNotContain(emission.Manifest.Tables, t => t.Table == "Customer");

        Assert.Contains("Modules/AppCore/Tables/dbo.CUSTOMER_STATIC.sql", emission.TableScripts);

        foreach (var tableScript in emission.TableScripts)
        {
            var contents = await File.ReadAllTextAsync(emission.GetAbsolutePath(tableScript));
            Assert.DoesNotContain("OSUSR_ABC_CUSTOMER", contents, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task BuildSsdt_AppliesModuleFilterFromCli()
    {
        var repoRoot = FixtureFile.RepositoryRoot;
        var cliProject = Path.Combine(repoRoot, "src", "Osm.Cli", "Osm.Cli.csproj");
        var modelPath = FixtureFile.GetPath("model.edge-case.json");
        var profilePath = FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json"));
        var staticDataPath = FixtureFile.GetPath(Path.Combine("static-data", "static-entities.edge-case.json"));

        using var output = new TempDirectory();

        var command = $"run --project {cliProject} -- build-ssdt --model \"{modelPath}\" --profile \"{profilePath}\" --static-data \"{staticDataPath}\" --modules AppCore --out \"{output.Path}\"";
        var result = await RunCliAsync(repoRoot, command);
        AssertExitCode(result, 0);

        var manifestPath = Path.Combine(output.Path, "manifest.json");
        using var manifestStream = File.OpenRead(manifestPath);
        using var manifestJson = JsonDocument.Parse(manifestStream);
        var modules = manifestJson.RootElement
            .GetProperty("Tables")
            .EnumerateArray()
            .Select(table => table.GetProperty("Module").GetString())
            .Distinct()
            .ToArray();

        Assert.Equal(new[] { "AppCore" }, modules);
    }

    [Fact]
    public async Task BuildSsdt_AppliesModuleFilterFromConfiguration()
    {
        var repoRoot = FixtureFile.RepositoryRoot;
        var cliProject = Path.Combine(repoRoot, "src", "Osm.Cli", "Osm.Cli.csproj");
        using var workspace = new TempDirectory();

        var configPath = Path.Combine(workspace.Path, "appsettings.json");
        var outputPath = Path.Combine(workspace.Path, "out");

        var config = new
        {
            tighteningPath = Path.Combine(repoRoot, "config", "default-tightening.json"),
            model = new
            {
                path = FixtureFile.GetPath("model.edge-case.json"),
                modules = new[] { "ExtBilling" }
            },
            profile = new { path = FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json")) }
        };

        await File.WriteAllTextAsync(
            configPath,
            JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

        var staticDataPath = FixtureFile.GetPath(Path.Combine("static-data", "static-entities.edge-case.json"));
        var command = $"run --project {cliProject} -- build-ssdt --config \"{configPath}\" --static-data \"{staticDataPath}\" --out \"{outputPath}\"";
        var result = await RunCliAsync(repoRoot, command);
        AssertExitCode(result, 0);

        var manifestPath = Path.Combine(outputPath, "manifest.json");
        using var manifestStream = File.OpenRead(manifestPath);
        using var manifestJson = JsonDocument.Parse(manifestStream);
        var modules = manifestJson.RootElement
            .GetProperty("Tables")
            .EnumerateArray()
            .Select(table => table.GetProperty("Module").GetString())
            .Distinct()
            .ToArray();

        Assert.Equal(new[] { "ExtBilling" }, modules);
    }

    [Fact]
    public async Task BuildSsdt_ShouldWriteEvidenceCacheManifest()
    {
        var repoRoot = FixtureFile.RepositoryRoot;
        var cliProject = Path.Combine(repoRoot, "src", "Osm.Cli", "Osm.Cli.csproj");
        var modelPath = FixtureFile.GetPath("model.edge-case.json");
        var profilePath = FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json"));
        var staticDataPath = FixtureFile.GetPath(Path.Combine("static-data", "static-entities.edge-case.json"));

        using var output = new TempDirectory();
        using var cacheRoot = new TempDirectory();

        var command = $"run --project {cliProject} -- build-ssdt --model \"{modelPath}\" --profile \"{profilePath}\" --static-data \"{staticDataPath}\" --out \"{output.Path}\" --cache-root \"{cacheRoot.Path}\" --refresh-cache";
        var result = await RunCliAsync(repoRoot, command);
        AssertExitCode(result, 0);

        var entries = Directory.GetDirectories(cacheRoot.Path);
        var cacheEntry = Assert.Single(entries);
        var manifestPath = Path.Combine(cacheEntry, "manifest.json");
        Assert.True(File.Exists(manifestPath));

        using var stream = File.OpenRead(manifestPath);
        using var manifest = JsonDocument.Parse(stream);
        var root = manifest.RootElement;
        Assert.Equal("build-ssdt", root.GetProperty("Command").GetString());
        Assert.Equal(Path.GetFileName(cacheEntry), root.GetProperty("Key").GetString());

        var artifacts = root.GetProperty("Artifacts");
        Assert.True(artifacts.GetArrayLength() >= 2);
    }

    [Fact]
    public async Task BuildSsdt_UsesCliConfigurationDefaults()
    {
        var repoRoot = FixtureFile.RepositoryRoot;
        var cliProject = Path.Combine(repoRoot, "src", "Osm.Cli", "Osm.Cli.csproj");
        using var workspace = new TempDirectory();

        var configPath = Path.Combine(workspace.Path, "appsettings.json");
        var outputPath = Path.Combine(workspace.Path, "out");
        var cacheRoot = Path.Combine(workspace.Path, "cache");

        var config = new
        {
            tighteningPath = Path.Combine(repoRoot, "config", "default-tightening.json"),
            model = new { path = FixtureFile.GetPath("model.edge-case.json") },
            profile = new { path = FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json")) },
            cache = new { root = cacheRoot, refresh = true }
        };

        await File.WriteAllTextAsync(
            configPath,
            JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

        var staticDataPath = FixtureFile.GetPath(Path.Combine("static-data", "static-entities.edge-case.json"));
        var command = $"run --project {cliProject} -- build-ssdt --config \"{configPath}\" --static-data \"{staticDataPath}\" --out \"{outputPath}\"";
        var result = await RunCliAsync(repoRoot, command);
        AssertExitCode(result, 0);
        Assert.True(File.Exists(Path.Combine(outputPath, "manifest.json")));

        var cacheEntries = Directory.GetDirectories(cacheRoot);
        Assert.NotEmpty(cacheEntries);
    }

    [Fact]
    public async Task BuildSsdt_AllowsEnvironmentOverrides()
    {
        var repoRoot = FixtureFile.RepositoryRoot;
        var cliProject = Path.Combine(repoRoot, "src", "Osm.Cli", "Osm.Cli.csproj");
        using var workspace = new TempDirectory();

        var configPath = Path.Combine(workspace.Path, "appsettings.json");
        var outputPath = Path.Combine(workspace.Path, "out");

        var config = new
        {
            tighteningPath = Path.Combine(repoRoot, "config", "default-tightening.json"),
            model = new { path = "missing-model.json" },
            profile = new { path = "missing-profile.json" }
        };

        await File.WriteAllTextAsync(
            configPath,
            JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

        var modelPath = FixtureFile.GetPath("model.edge-case.json");
        var profilePath = FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json"));
        var staticDataPath = FixtureFile.GetPath(Path.Combine("static-data", "static-entities.edge-case.json"));

        var originalModelEnv = Environment.GetEnvironmentVariable("OSM_CLI_MODEL_PATH");
        var originalProfileEnv = Environment.GetEnvironmentVariable("OSM_CLI_PROFILE_PATH");

        try
        {
            Environment.SetEnvironmentVariable("OSM_CLI_MODEL_PATH", modelPath);
            Environment.SetEnvironmentVariable("OSM_CLI_PROFILE_PATH", profilePath);

            var command = $"run --project {cliProject} -- build-ssdt --config \"{configPath}\" --static-data \"{staticDataPath}\" --out \"{outputPath}\"";
            var result = await RunCliAsync(repoRoot, command);
            AssertExitCode(result, 0);
            Assert.True(File.Exists(Path.Combine(outputPath, "manifest.json")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OSM_CLI_MODEL_PATH", originalModelEnv);
            Environment.SetEnvironmentVariable("OSM_CLI_PROFILE_PATH", originalProfileEnv);
        }
    }



    private static void AssertExitCode(CommandResult result, int expectedExitCode)
    {
        Assert.True(result.ExitCode == expectedExitCode, result.FormatFailureMessage(expectedExitCode));
    }

    private static async Task<CommandResult> RunCliAsync(string workingDirectory, string arguments)
    {
        var startInfo = DotNetCli.CreateStartInfo(workingDirectory, arguments);
        startInfo.Environment["DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE"] = "false";

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to launch dotnet process.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(process.WaitForExitAsync(), stdoutTask, stderrTask).ConfigureAwait(false);

        return new CommandResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    private const string EdgeCaseScript = @"CREATE TABLE [dbo].[OSUSR_ABC_CUSTOMER](
    [ID] BIGINT IDENTITY (1, 1) NOT NULL,
    [EMAIL] NVARCHAR(255) COLLATE Latin1_General_CI_AI NOT NULL,
    [FIRSTNAME] NVARCHAR(100) NULL DEFAULT (''),
    [LASTNAME] NVARCHAR(100) NULL DEFAULT (''),
    [CITYID] BIGINT NOT NULL,
    CONSTRAINT [PK_Customer] PRIMARY KEY ([ID])
);
CREATE TABLE [dbo].[OSUSR_DEF_CITY](
    [ID] BIGINT IDENTITY (1, 1) NOT NULL,
    [NAME] NVARCHAR(200) NOT NULL,
    [ISACTIVE] BIT NOT NULL DEFAULT ((1)),
    CONSTRAINT [PK_City] PRIMARY KEY ([ID])
);
CREATE TABLE [billing].[BILLING_ACCOUNT](
    [ID] BIGINT IDENTITY (1, 1) NOT NULL,
    [ACCOUNTNUMBER] VARCHAR(50) NOT NULL,
    [EXTREF] VARCHAR(50) NULL,
    CONSTRAINT [PK_BillingAccount] PRIMARY KEY ([ID])
);
CREATE TABLE [dbo].[OSUSR_XYZ_JOBRUN](
    [ID] BIGINT IDENTITY (1, 1) NOT NULL,
    [TRIGGEREDBYUSERID] BIGINT NULL,
    [CREATEDON] DATETIME NOT NULL DEFAULT (getutcdate()),
    CONSTRAINT [PK_JobRun] PRIMARY KEY ([ID])
);
ALTER TABLE [dbo].[OSUSR_ABC_CUSTOMER]
ADD CONSTRAINT [FK_Customer_CityId]
FOREIGN KEY ([CITYID]) REFERENCES [dbo].[OSUSR_DEF_CITY]([ID]);";

    private const string MismatchedScript = @"CREATE TABLE [dbo].[OSUSR_ABC_CUSTOMER](
    [ID] BIGINT IDENTITY (1, 1) NOT NULL,
    [EMAIL] NVARCHAR(255) COLLATE Latin1_General_CI_AI NULL,
    [FIRSTNAME] NVARCHAR(100) NULL DEFAULT (''),
    [LASTNAME] NVARCHAR(100) NULL DEFAULT (''),
    [CITYID] BIGINT NOT NULL,
    CONSTRAINT [PK_Customer] PRIMARY KEY ([ID])
);
CREATE TABLE [dbo].[OSUSR_DEF_CITY](
    [ID] BIGINT IDENTITY (1, 1) NOT NULL,
    [NAME] NVARCHAR(200) NOT NULL,
    [ISACTIVE] BIT NOT NULL DEFAULT ((1)),
    CONSTRAINT [PK_City] PRIMARY KEY ([ID])
);
CREATE TABLE [billing].[BILLING_ACCOUNT](
    [ID] BIGINT IDENTITY (1, 1) NOT NULL,
    [ACCOUNTNUMBER] VARCHAR(50) NOT NULL,
    [EXTREF] VARCHAR(50) NULL,
    CONSTRAINT [PK_BillingAccount] PRIMARY KEY ([ID])
);
CREATE TABLE [dbo].[OSUSR_XYZ_JOBRUN](
    [ID] BIGINT IDENTITY (1, 1) NOT NULL,
    [TRIGGEREDBYUSERID] BIGINT NULL,
    [CREATEDON] DATETIME NOT NULL DEFAULT (getutcdate()),
    CONSTRAINT [PK_JobRun] PRIMARY KEY ([ID])
);
ALTER TABLE [dbo].[OSUSR_ABC_CUSTOMER]
ADD CONSTRAINT [FK_Customer_CityId]
FOREIGN KEY ([CITYID]) REFERENCES [dbo].[OSUSR_DEF_CITY]([ID]);";
}
