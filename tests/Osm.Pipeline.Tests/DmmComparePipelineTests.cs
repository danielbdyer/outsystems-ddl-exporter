using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Json;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.SqlExtraction;
using Osm.Validation.Tightening;
using Osm.Smo;
using Tests.Support;
using Xunit;
using Osm.Validation.Profiling;
using Osm.Pipeline.Configuration;

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
            Assert.Equal(profilePath, request.Telemetry.ProfilingStartMetadata["paths.profile"]);
            var captureResult = await request.ProfileCaptureAsync(default!, token);
            Assert.True(captureResult.IsSuccess);

            var error = ValidationError.Create("test.bootstrap.stop", "Bootstrapper halted pipeline for verification.");
            return Result<PipelineBootstrapContext>.Failure(error);
        });

        var scope = new ModelExecutionScope(
            modelPath,
            ModuleFilterOptions.IncludeAll,
            SupplementalModelOptions.Default,
            TighteningOptions.Default,
            new ResolvedSqlOptions(
                ConnectionString: null,
                CommandTimeoutSeconds: null,
                Sampling: new SqlSamplingSettings(null, null),
                Authentication: new SqlAuthenticationSettings(null, null, null, null),
                MetadataContract: MetadataContractOverrides.Strict,
                ProfilingConnectionStrings: ImmutableArray<string>.Empty,
                TableNameMappings: ImmutableArray<TableNameMappingConfiguration>.Empty),
            SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission, applyNamingOverrides: false),
            TypeMappingPolicyLoader.LoadDefault(),
            profilePath);

        var request = new DmmComparePipelineRequest(
            scope,
            scriptPath,
            Path.Combine(workspace.Path, "dmm-diff.json"),
            null);

        var pipeline = CreatePipeline(bootstrapper);
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

        var scope = new ModelExecutionScope(
            modelPath,
            ModuleFilterOptions.IncludeAll,
            SupplementalModelOptions.Default,
            TighteningOptions.Default,
            new ResolvedSqlOptions(
                ConnectionString: null,
                CommandTimeoutSeconds: null,
                Sampling: new SqlSamplingSettings(null, null),
                Authentication: new SqlAuthenticationSettings(null, null, null, null),
                MetadataContract: MetadataContractOverrides.Strict,
                ProfilingConnectionStrings: ImmutableArray<string>.Empty,
                TableNameMappings: ImmutableArray<TableNameMappingConfiguration>.Empty),
            SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission, applyNamingOverrides: false),
            TypeMappingPolicyLoader.LoadDefault(),
            profilePath);

        var request = new DmmComparePipelineRequest(
            scope,
            scriptPath,
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

        var pipeline = CreatePipeline();
        var result = await pipeline.HandleAsync(request);

        Assert.True(result.IsSuccess);
        var value = result.Value;
        Assert.False(value.Comparison.IsMatch);
        Assert.Contains(
            value.Comparison.ModelDifferences,
            diff => string.Equals(diff.Property, "TablePresence", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Schema, "dbo", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Table, "OSUSR_U_USER", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            value.Comparison.SsdtDifferences,
            diff => string.Equals(diff.Property, "Collation", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Column, "Email", StringComparison.OrdinalIgnoreCase));
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

        var customerPath = Path.Combine(workspace.Path, "Modules", "AppCore", "dbo.Customer.sql");
        File.Delete(customerPath);

        var diffPath = Path.Combine(workspace.Path, "dmm-diff.json");

        var scope = new ModelExecutionScope(
            modelPath,
            ModuleFilterOptions.IncludeAll,
            SupplementalModelOptions.Default,
            TighteningOptions.Default,
            new ResolvedSqlOptions(
                ConnectionString: null,
                CommandTimeoutSeconds: null,
                Sampling: new SqlSamplingSettings(null, null),
                Authentication: new SqlAuthenticationSettings(null, null, null, null),
                MetadataContract: MetadataContractOverrides.Strict,
                ProfilingConnectionStrings: ImmutableArray<string>.Empty,
                TableNameMappings: ImmutableArray<TableNameMappingConfiguration>.Empty),
            SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission, applyNamingOverrides: false),
            TypeMappingPolicyLoader.LoadDefault(),
            profilePath);

        var request = new DmmComparePipelineRequest(
            scope,
            workspace.Path,
            diffPath,
            null);

        var pipeline = CreatePipeline();
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

    private static DmmComparePipeline CreatePipeline(IPipelineBootstrapper? bootstrapper = null)
    {
        return new DmmComparePipeline(bootstrapper ?? CreatePipelineBootstrapper());
    }

    private static PipelineBootstrapper CreatePipelineBootstrapper()
    {
        return new PipelineBootstrapper(
            new ModelIngestionService(new ModelJsonDeserializer()),
            new ModuleFilter(),
            new SupplementalEntityLoader(new ModelJsonDeserializer()),
            new ProfilingInsightGenerator());
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
