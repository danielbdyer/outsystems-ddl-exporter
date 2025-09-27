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

        using var output = new TempDirectory();

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

        var dmmScriptPath = Path.Combine(output.Path, "edge-case.dmm.sql");
        await File.WriteAllTextAsync(dmmScriptPath, EdgeCaseScript);

        var compareExit = await RunCliAsync(repoRoot, $"run --project {cliProject} -- dmm-compare --model {modelPath} --profile {profilePath} --dmm {dmmScriptPath}");
        Assert.Equal(0, compareExit);
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

}
