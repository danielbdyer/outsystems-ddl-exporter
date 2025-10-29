using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.SqlExtraction;
using Osm.Validation.Tightening;
using Osm.Smo;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests;

public class DmmComparePipelineTests
{
    [Fact]
    public async Task HandleAsync_uses_fixture_profile_strategy_via_bootstrapper()
    {
        var modelPath = FixtureFile.GetPath("model.edge-case.json");
        var profilePath = FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json"));
        using var workspace = new TempDirectory();
        var scriptPath = Path.Combine(workspace.Path, "baseline.dmm.sql");
        await File.WriteAllTextAsync(scriptPath, EdgeCaseScript);

        var bootstrapper = new FakePipelineBootstrapper(async (_, request, token) =>
        {
            Assert.Equal(profilePath, request.Telemetry.ProfilingStartMetadata["profilePath"]);
            var captureResult = await request.ProfileCaptureAsync(default!, token);
            Assert.True(captureResult.IsSuccess);

            var error = ValidationError.Create("test.bootstrap.stop", "Bootstrapper halted pipeline for verification.");
            return Result<PipelineBootstrapContext>.Failure(error);
        });

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
                MetadataContract: MetadataContractOverrides.Strict),
            SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission, applyNamingOverrides: false),
            TypeMappingPolicy.LoadDefault(),
            Path.Combine(workspace.Path, "dmm-diff.json"),
            null);

        var pipeline = new DmmComparePipeline(bootstrapper: bootstrapper);
        var result = await pipeline.HandleAsync(request);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("test.bootstrap.stop", error.Code);
        Assert.NotNull(bootstrapper.LastRequest);
    }

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
                MetadataContract: MetadataContractOverrides.Strict),
            SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission, applyNamingOverrides: false),
            TypeMappingPolicy.LoadDefault(),
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

    [Fact]
    public async Task HandleAsync_reports_table_file_layout_differences()
    {
        var modelPath = FixtureFile.GetPath("model.edge-case.json");
        var profilePath = FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json"));

        using var workspace = new TempDirectory();
        var baselineSource = Path.Combine(
            FixtureFile.RepositoryRoot,
            "tests",
            "Fixtures",
            "emission",
            "edge-case");
        TestFileSystem.CopyDirectory(baselineSource, workspace.Path);

        var customerPath = Path.Combine(workspace.Path, "Modules", "AppCore", "Tables", "dbo.Customer.sql");
        File.Delete(customerPath);

        var diffPath = Path.Combine(workspace.Path, "dmm-diff.json");

        var request = new DmmComparePipelineRequest(
            modelPath,
            ModuleFilterOptions.IncludeAll,
            profilePath,
            workspace.Path,
            TighteningOptions.Default,
            SupplementalModelOptions.Default,
            new ResolvedSqlOptions(
                ConnectionString: null,
                CommandTimeoutSeconds: null,
                Sampling: new SqlSamplingSettings(null, null),
                Authentication: new SqlAuthenticationSettings(null, null, null, null),
                MetadataContract: MetadataContractOverrides.Strict),
            SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission, applyNamingOverrides: false),
            TypeMappingPolicy.LoadDefault(),
            diffPath,
            null);

        var pipeline = new DmmComparePipeline();
        var result = await pipeline.HandleAsync(request);

        Assert.True(result.IsSuccess);
        var comparison = result.Value.Comparison;
        Assert.False(comparison.IsMatch);
        Assert.Contains(
            comparison.ModelDifferences,
            diff => string.Equals(diff.Property, "FilePresence", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Schema, "dbo", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Table, "Customer", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FakePipelineBootstrapper : IPipelineBootstrapper
    {
        private readonly Func<PipelineExecutionLogBuilder, PipelineBootstrapRequest, CancellationToken, Task<Result<PipelineBootstrapContext>>> _callback;

        public FakePipelineBootstrapper(
            Func<PipelineExecutionLogBuilder, PipelineBootstrapRequest, CancellationToken, Task<Result<PipelineBootstrapContext>>> callback)
        {
            _callback = callback;
        }

        public PipelineBootstrapRequest? LastRequest { get; private set; }

        public Task<Result<PipelineBootstrapContext>> BootstrapAsync(
            PipelineExecutionLogBuilder log,
            PipelineBootstrapRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return _callback(log, request, cancellationToken);
        }
    }

    private const string EdgeCaseScript = @"CREATE TABLE [dbo].[OSUSR_ABC_CUSTOMER](
    [ID] BIGINT NOT NULL,
    [EMAIL] NVARCHAR(255) COLLATE Latin1_General_CI_AI NOT NULL,
    [FIRSTNAME] NVARCHAR(100) NULL DEFAULT ('') ,
    [LASTNAME] NVARCHAR(100) NULL DEFAULT ('') ,
    [CITYID] BIGINT NOT NULL,
    CONSTRAINT [PK_Customer_Id] PRIMARY KEY ([ID])
);
CREATE TABLE [dbo].[OSUSR_DEF_CITY](
    [ID] BIGINT NOT NULL,
    [NAME] NVARCHAR(200) NOT NULL,
    [ISACTIVE] BIT NOT NULL DEFAULT (1),
    CONSTRAINT [PK_City_Id] PRIMARY KEY ([ID])
);
CREATE TABLE [billing].[BILLING_ACCOUNT](
    [ID] BIGINT NOT NULL,
    [ACCOUNTNUMBER] VARCHAR(50) NOT NULL,
    [EXTREF] VARCHAR(50) NULL,
    CONSTRAINT [PK_BillingAccount_Id] PRIMARY KEY ([ID])
);
CREATE TABLE [dbo].[OSUSR_XYZ_JOBRUN](
    [ID] BIGINT NOT NULL,
    [TRIGGEREDBYUSERID] BIGINT NULL,
    [CREATEDON] DATETIME NOT NULL DEFAULT (getutcdate()),
    CONSTRAINT [PK_JobRun_Id] PRIMARY KEY ([ID])
);";
}
