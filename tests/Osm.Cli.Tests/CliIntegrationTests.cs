using System;
using System.Diagnostics;
using System.IO;
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
            Assert.True(root.GetProperty("Columns").GetArrayLength() > 0);
        }

        var dmmScriptPath = Path.Combine(output.Path, "edge-case.dmm.sql");
        await File.WriteAllTextAsync(dmmScriptPath, EdgeCaseScript);

        var compareExit = await RunCliAsync(repoRoot, $"run --project {cliProject} -- dmm-compare --model {modelPath} --profile {profilePath} --dmm {dmmScriptPath}");
        Assert.Equal(0, compareExit);
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
    [LEGACYCODE] NVARCHAR(50) NULL,
    CONSTRAINT [PK_OSUSR_ABC_CUSTOMER] PRIMARY KEY ([ID])
);
CREATE TABLE [dbo].[OSUSR_DEF_CITY](
    [ID] INT NOT NULL,
    [NAME] NVARCHAR(200) NOT NULL,
    [ISACTIVE] BIT NOT NULL,
    CONSTRAINT [PK_OSUSR_DEF_CITY] PRIMARY KEY ([ID])
);
CREATE TABLE [billing].[BILLING_ACCOUNT](
    [ID] INT NOT NULL,
    [ACCOUNTNUMBER] VARCHAR(50) NOT NULL,
    [EXTREF] VARCHAR(50) NULL,
    CONSTRAINT [PK_BILLING_ACCOUNT] PRIMARY KEY ([ID])
);
CREATE TABLE [dbo].[OSUSR_XYZ_JOBRUN](
    [ID] INT NOT NULL,
    [TRIGGEREDBYUSERID] INT NULL,
    [CREATEDON] DATETIME NOT NULL,
    CONSTRAINT [PK_OSUSR_XYZ_JOBRUN] PRIMARY KEY ([ID])
);";

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
