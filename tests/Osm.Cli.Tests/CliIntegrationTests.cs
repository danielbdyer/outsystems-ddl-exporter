using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Tests.Support;

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
        var expectedEmissionRoot = Path.Combine(repoRoot, "tests", "Fixtures", "emission", "edge-case");

        using var output = new TempDirectory();
        using var comparisonWorkspace = new TempDirectory();
        var diffPath = Path.Combine(comparisonWorkspace.Path, "dmm-diff.json");

        var buildExit = await RunCliAsync(repoRoot, $"run --project {cliProject} -- build-ssdt --model {modelPath} --profile {profilePath} --out {output.Path}");
        Assert.Equal(0, buildExit);

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

        DirectorySnapshot.AssertMatches(expectedEmissionRoot, output.Path);

        var dmmScriptPath = Path.Combine(comparisonWorkspace.Path, "edge-case.dmm.sql");
        await File.WriteAllTextAsync(dmmScriptPath, EdgeCaseScript);

        var compareExit = await RunCliAsync(repoRoot, $"run --project {cliProject} -- dmm-compare --model {modelPath} --profile {profilePath} --dmm {dmmScriptPath} --out {diffPath}");
        Assert.Equal(0, compareExit);

        Assert.True(File.Exists(diffPath));
        using (var diffStream = File.OpenRead(diffPath))
        using (var diffJson = JsonDocument.Parse(diffStream))
        {
            var root = diffJson.RootElement;
            Assert.True(root.GetProperty("IsMatch").GetBoolean());
            Assert.Equal(modelPath, root.GetProperty("ModelPath").GetString());
            Assert.Equal(profilePath, root.GetProperty("ProfilePath").GetString());
            Assert.Equal(dmmScriptPath, root.GetProperty("DmmPath").GetString());
            Assert.Equal(0, root.GetProperty("Differences").GetArrayLength());
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

        var compareExit = await RunCliAsync(repoRoot, $"run --project {cliProject} -- dmm-compare --model {modelPath} --profile {profilePath} --dmm {dmmScriptPath} --out {diffPath}");
        Assert.Equal(2, compareExit);

        Assert.True(File.Exists(diffPath));
        using var diffStream = File.OpenRead(diffPath);
        using var diffJson = JsonDocument.Parse(diffStream);
        var root = diffJson.RootElement;

        Assert.False(root.GetProperty("IsMatch").GetBoolean());
        Assert.Equal(modelPath, root.GetProperty("ModelPath").GetString());
        Assert.Equal(profilePath, root.GetProperty("ProfilePath").GetString());
        Assert.Equal(dmmScriptPath, root.GetProperty("DmmPath").GetString());

        var differences = root.GetProperty("Differences");
        Assert.True(differences.GetArrayLength() > 0);
        Assert.Contains(
            differences.EnumerateArray().Select(element => element.GetString()),
            difference => difference != null && difference.Contains("nullability mismatch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildSsdt_honors_rename_table_overrides()
    {
        var repoRoot = FixtureFile.RepositoryRoot;
        var cliProject = Path.Combine(repoRoot, "src", "Osm.Cli", "Osm.Cli.csproj");
        var modelPath = FixtureFile.GetPath("model.edge-case.json");
        var profilePath = FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json"));

        using var output = new TempDirectory();
        var expectedRoot = Path.Combine(repoRoot, "tests", "Fixtures", "emission", "edge-case-rename");

        var command = $"run --project {cliProject} -- build-ssdt --model \"{modelPath}\" --profile \"{profilePath}\" --out \"{output.Path}\" --rename-table dbo.OSUSR_ABC_CUSTOMER=CUSTOMER_PORTAL";
        var exit = await RunCliAsync(repoRoot, command);
        Assert.Equal(0, exit);

        var renamedTable = Directory.GetFiles(output.Path, "dbo.CUSTOMER_PORTAL.sql", SearchOption.AllDirectories);
        var originalTable = Directory.GetFiles(output.Path, "dbo.Customer.sql", SearchOption.AllDirectories);

        Assert.Single(renamedTable);
        Assert.Empty(originalTable);

        var script = await File.ReadAllTextAsync(renamedTable[0]);
        Assert.Contains("CREATE TABLE dbo.CUSTOMER_PORTAL", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OSUSR_ABC_CUSTOMER", script, StringComparison.OrdinalIgnoreCase);

        var manifestPath = Path.Combine(output.Path, "manifest.json");
        using var manifestStream = File.OpenRead(manifestPath);
        using var manifestJson = JsonDocument.Parse(manifestStream);
        var tables = manifestJson.RootElement.GetProperty("Tables");
        var renamedEntry = tables.EnumerateArray().Single(t => t.GetProperty("Table").GetString() == "CUSTOMER_PORTAL");
        Assert.EndsWith("dbo.CUSTOMER_PORTAL.sql", renamedEntry.GetProperty("TableFile").GetString(), StringComparison.OrdinalIgnoreCase);

        DirectorySnapshot.AssertMatches(expectedRoot, output.Path);
    }

    [Fact]
    public async Task BuildSsdt_AppliesModuleFilterFromCli()
    {
        var repoRoot = FixtureFile.RepositoryRoot;
        var cliProject = Path.Combine(repoRoot, "src", "Osm.Cli", "Osm.Cli.csproj");
        var modelPath = FixtureFile.GetPath("model.edge-case.json");
        var profilePath = FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json"));

        using var output = new TempDirectory();

        var command = $"run --project {cliProject} -- build-ssdt --model \"{modelPath}\" --profile \"{profilePath}\" --modules AppCore --out \"{output.Path}\"";
        var exit = await RunCliAsync(repoRoot, command);

        Assert.Equal(0, exit);

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

        var command = $"run --project {cliProject} -- build-ssdt --config \"{configPath}\" --out \"{outputPath}\"";
        var exit = await RunCliAsync(repoRoot, command);

        Assert.Equal(0, exit);

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

        using var output = new TempDirectory();
        using var cacheRoot = new TempDirectory();

        var command = $"run --project {cliProject} -- build-ssdt --model \"{modelPath}\" --profile \"{profilePath}\" --out \"{output.Path}\" --cache-root \"{cacheRoot.Path}\" --refresh-cache";
        var exit = await RunCliAsync(repoRoot, command);
        Assert.Equal(0, exit);

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

        var command = $"run --project {cliProject} -- build-ssdt --config \"{configPath}\" --out \"{outputPath}\"";
        var exit = await RunCliAsync(repoRoot, command);

        Assert.Equal(0, exit);
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

        var originalModelEnv = Environment.GetEnvironmentVariable("OSM_CLI_MODEL_PATH");
        var originalProfileEnv = Environment.GetEnvironmentVariable("OSM_CLI_PROFILE_PATH");

        try
        {
            Environment.SetEnvironmentVariable("OSM_CLI_MODEL_PATH", modelPath);
            Environment.SetEnvironmentVariable("OSM_CLI_PROFILE_PATH", profilePath);

            var command = $"run --project {cliProject} -- build-ssdt --config \"{configPath}\" --out \"{outputPath}\"";
            var exit = await RunCliAsync(repoRoot, command);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(Path.Combine(outputPath, "manifest.json")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OSM_CLI_MODEL_PATH", originalModelEnv);
            Environment.SetEnvironmentVariable("OSM_CLI_PROFILE_PATH", originalProfileEnv);
        }
    }

    [Fact]
    public async Task BuildSsdt_honors_rename_table_overrides()
    {
        var repoRoot = FixtureFile.RepositoryRoot;
        var cliProject = Path.Combine(repoRoot, "src", "Osm.Cli", "Osm.Cli.csproj");
        var modelPath = FixtureFile.GetPath("model.edge-case.json");
        var profilePath = FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json"));

        using var output = new TempDirectory();

        var command = $"run --project {cliProject} -- build-ssdt --model \"{modelPath}\" --profile \"{profilePath}\" --out \"{output.Path}\" --rename-table dbo.OSUSR_ABC_CUSTOMER=CUSTOMER_PORTAL";
        var exit = await RunCliAsync(repoRoot, command);
        Assert.Equal(0, exit);

        var renamedTable = Directory.GetFiles(output.Path, "dbo.CUSTOMER_PORTAL.sql", SearchOption.AllDirectories);
        var originalTable = Directory.GetFiles(output.Path, "dbo.Customer.sql", SearchOption.AllDirectories);

        Assert.Single(renamedTable);
        Assert.Empty(originalTable);

        var script = await File.ReadAllTextAsync(renamedTable[0]);
        Assert.Contains("CREATE TABLE dbo.CUSTOMER_PORTAL", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OSUSR_ABC_CUSTOMER", script, StringComparison.OrdinalIgnoreCase);

        var manifestPath = Path.Combine(output.Path, "manifest.json");
        using var manifestStream = File.OpenRead(manifestPath);
        using var manifestJson = JsonDocument.Parse(manifestStream);
        var tables = manifestJson.RootElement.GetProperty("Tables");
        var renamedEntry = tables.EnumerateArray().Single(t => t.GetProperty("Table").GetString() == "CUSTOMER_PORTAL");
        Assert.EndsWith("dbo.CUSTOMER_PORTAL.sql", renamedEntry.GetProperty("TableFile").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildSsdt_ShouldWriteEvidenceCacheManifest()
    {
        var repoRoot = FixtureFile.RepositoryRoot;
        var cliProject = Path.Combine(repoRoot, "src", "Osm.Cli", "Osm.Cli.csproj");
        var modelPath = FixtureFile.GetPath("model.edge-case.json");
        var profilePath = FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json"));

        using var output = new TempDirectory();
        using var cacheRoot = new TempDirectory();

        var command = $"run --project {cliProject} -- build-ssdt --model \"{modelPath}\" --profile \"{profilePath}\" --out \"{output.Path}\" --cache-root \"{cacheRoot.Path}\" --refresh-cache";
        var exit = await RunCliAsync(repoRoot, command);
        Assert.Equal(0, exit);

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

    private static async Task<int> RunCliAsync(string workingDirectory, string arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(startInfo)!;
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private const string EdgeCaseScript = @"CREATE TABLE [dbo].[OSUSR_ABC_CUSTOMER](
    [ID] INT NOT NULL,
    [EMAIL] NVARCHAR(255) NOT NULL,
    [FIRSTNAME] NVARCHAR(100) NULL,
    [LASTNAME] NVARCHAR(100) NULL,
    [CITYID] INT NOT NULL,
    CONSTRAINT [PK_Customer] PRIMARY KEY ([ID])
);
CREATE TABLE [dbo].[OSUSR_DEF_CITY](
    [ID] INT NOT NULL,
    [NAME] NVARCHAR(200) NOT NULL,
    [ISACTIVE] BIT NOT NULL,
    CONSTRAINT [PK_City] PRIMARY KEY ([ID])
);
CREATE TABLE [billing].[BILLING_ACCOUNT](
    [ID] INT NOT NULL,
    [ACCOUNTNUMBER] VARCHAR(50) NOT NULL,
    [EXTREF] VARCHAR(50) NULL,
    CONSTRAINT [PK_BillingAccount] PRIMARY KEY ([ID])
);
CREATE TABLE [dbo].[OSUSR_XYZ_JOBRUN](
    [ID] INT NOT NULL,
    [TRIGGEREDBYUSERID] INT NULL,
    [CREATEDON] DATETIME NOT NULL,
    CONSTRAINT [PK_JobRun] PRIMARY KEY ([ID])
);";

    private const string MismatchedScript = @"CREATE TABLE [dbo].[OSUSR_ABC_CUSTOMER](
    [ID] INT NOT NULL,
    [EMAIL] NVARCHAR(255) NULL,
    [FIRSTNAME] NVARCHAR(100) NULL,
    [LASTNAME] NVARCHAR(100) NULL,
    [CITYID] INT NOT NULL,
    CONSTRAINT [PK_Customer] PRIMARY KEY ([ID])
);
CREATE TABLE [dbo].[OSUSR_DEF_CITY](
    [ID] INT NOT NULL,
    [NAME] NVARCHAR(200) NOT NULL,
    [ISACTIVE] BIT NOT NULL,
    CONSTRAINT [PK_City] PRIMARY KEY ([ID])
);
CREATE TABLE [billing].[BILLING_ACCOUNT](
    [ID] INT NOT NULL,
    [ACCOUNTNUMBER] VARCHAR(50) NOT NULL,
    [EXTREF] VARCHAR(50) NULL,
    CONSTRAINT [PK_BillingAccount] PRIMARY KEY ([ID])
);
CREATE TABLE [dbo].[OSUSR_XYZ_JOBRUN](
    [ID] INT NOT NULL,
    [TRIGGEREDBYUSERID] INT NULL,
    [CREATEDON] DATETIME NOT NULL,
    CONSTRAINT [PK_JobRun] PRIMARY KEY ([ID])
);";
}
