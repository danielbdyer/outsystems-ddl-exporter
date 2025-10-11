using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Osm.Domain.Configuration;
using Osm.Pipeline.Orchestration;
using Osm.Validation.Tightening;
using Osm.Smo;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests;

public class DmmComparePipelineTests
{
    [Fact]
    public async Task HandleAsync_confirms_parity_and_writes_diff()
    {
        var modelPath = FixtureFile.GetPath("model.edge-case.json");
        var profilePath = FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json"));

        using var workspace = new TempDirectory();
        using var cache = new TempDirectory();

        var scriptPath = Path.Combine(workspace.Path, "edge-case.dmm.sql");
        await File.WriteAllTextAsync(scriptPath, EdgeCaseScript);
        var diffPath = Path.Combine(workspace.Path, "dmm-diff.json");

        var request = new DmmComparePipelineRequest(
            modelPath,
            ModuleFilterOptions.IncludeAll,
            profilePath,
            scriptPath,
            TighteningOptions.Default,
            SupplementalModelOptions.Default,
            new ResolvedSqlOptions(
                ConnectionString: null,
                CommandTimeoutSeconds: null,
                Sampling: new SqlSamplingSettings(null, null),
                Authentication: new SqlAuthenticationSettings(null, null, null, null),
                ProfilerExecution: SqlProfilerExecutionSettings.Default),
            SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission, applyNamingOverrides: false),
            diffPath,
            new EvidenceCachePipelineOptions(
                cache.Path,
                Refresh: false,
                Command: "dmm-compare",
                ModelPath: modelPath,
                ProfilePath: profilePath,
                DmmPath: scriptPath,
                ConfigPath: null,
                Metadata: new Dictionary<string, string?>()));

        var pipeline = new DmmComparePipeline();
        var result = await pipeline.HandleAsync(request);

        Assert.True(result.IsSuccess);
        var value = result.Value;
        Assert.True(value.Comparison.IsMatch);
        Assert.True(File.Exists(diffPath));
        Assert.NotNull(value.EvidenceCache);
        Assert.True(Directory.Exists(value.EvidenceCache!.CacheDirectory));
        Assert.NotNull(value.ExecutionLog);
        Assert.True(value.ExecutionLog.Entries.Count > 0);
        Assert.Contains(value.ExecutionLog.Entries, entry => entry.Step == "pipeline.completed");
    }

    private const string EdgeCaseScript = @"CREATE TABLE [dbo].[OSUSR_ABC_CUSTOMER](
    [ID] BIGINT NOT NULL,
    [EMAIL] NVARCHAR(255) NOT NULL,
    [FIRSTNAME] NVARCHAR(100) NULL,
    [LASTNAME] NVARCHAR(100) NULL,
    [CITYID] BIGINT NOT NULL,
    CONSTRAINT [PK_Customer] PRIMARY KEY ([ID])
);
CREATE TABLE [dbo].[OSUSR_DEF_CITY](
    [ID] BIGINT NOT NULL,
    [NAME] NVARCHAR(200) NOT NULL,
    [ISACTIVE] BIT NOT NULL,
    CONSTRAINT [PK_City] PRIMARY KEY ([ID])
);
CREATE TABLE [billing].[BILLING_ACCOUNT](
    [ID] BIGINT NOT NULL,
    [ACCOUNTNUMBER] VARCHAR(50) NOT NULL,
    [EXTREF] VARCHAR(50) NULL,
    CONSTRAINT [PK_BillingAccount] PRIMARY KEY ([ID])
);
CREATE TABLE [dbo].[OSUSR_XYZ_JOBRUN](
    [ID] BIGINT NOT NULL,
    [TRIGGEREDBYUSERID] BIGINT NULL,
    [CREATEDON] DATETIME NOT NULL,
    CONSTRAINT [PK_JobRun] PRIMARY KEY ([ID])
);";
}
