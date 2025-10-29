using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Profiling;
using Osm.Emission;
using Osm.Emission.Seeds;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Sql;
using Osm.Validation.Tightening;
using Opportunities = Osm.Validation.Tightening.Opportunities;
using Xunit;

namespace Osm.Pipeline.Tests;

public sealed class BuildSsdtApplicationServiceTests
{
    [Fact]
    public async Task RunAsync_ComposesPipelineRequestFromDependencies()
    {
        var input = CreateInput(overrides: new BuildSsdtOverrides(
            ModelPath: "model.json",
            ProfilePath: "profile.snapshot",
            OutputDirectory: "out",
            ProfilerProvider: "fixture",
            StaticDataPath: null,
            RenameOverrides: null,
            MaxDegreeOfParallelism: null,
            SqlMetadataOutputPath: null));

        var dispatcher = new RecordingDispatcher();
        dispatcher.SetResult(Result<BuildSsdtPipelineResult>.Success(CreatePipelineResult()));
        var assembler = new BuildSsdtRequestAssembler();
        var modelResolution = new StubModelResolutionService();
        var outputResolver = new TestOutputDirectoryResolver();
        var namingBinder = new TestNamingOverridesBinder();
        var staticDataProvider = new TestStaticEntityDataProvider();
        var staticDataFactory = new TestStaticDataProviderFactory(staticDataProvider);

        var service = new BuildSsdtApplicationService(
            dispatcher,
            assembler,
            modelResolution,
            outputResolver,
            namingBinder,
            staticDataFactory);

        var result = await service.RunAsync(input, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(dispatcher.Request);
        Assert.Same(staticDataProvider, dispatcher.Request!.StaticDataProvider);
        Assert.Equal("out", result.Value.OutputDirectory);
        Assert.Equal("model.json", result.Value.ModelPath);
        Assert.False(result.Value.ModelWasExtracted);
    }

    [Fact]
    public async Task RunAsync_FlushesMetadataWhenModelResolutionFails()
    {
        var metadataPath = CreateMetadataPath();
        try
        {
            var input = CreateInput(overrides: new BuildSsdtOverrides(
                ModelPath: "model.json",
                ProfilePath: "profile.snapshot",
                OutputDirectory: "out",
                ProfilerProvider: "fixture",
                StaticDataPath: null,
                RenameOverrides: null,
                MaxDegreeOfParallelism: null,
                SqlMetadataOutputPath: metadataPath));

            var dispatcher = new RecordingDispatcher();
            var assembler = new BuildSsdtRequestAssembler();
            var modelResolution = new FailingModelResolutionService();
            var outputResolver = new TestOutputDirectoryResolver();
            var namingBinder = new TestNamingOverridesBinder();
            var staticDataFactory = new TestStaticDataProviderFactory(new TestStaticEntityDataProvider());

            var service = new BuildSsdtApplicationService(
                dispatcher,
                assembler,
                modelResolution,
                outputResolver,
                namingBinder,
                staticDataFactory);

            var result = await service.RunAsync(input, CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Contains(result.Errors, error => error.Code == FailingModelResolutionService.ErrorCode);
            Assert.True(File.Exists(metadataPath));
        }
        finally
        {
            DeleteMetadataDirectory(metadataPath);
        }
    }

    [Fact]
    public async Task RunAsync_FlushesMetadataWhenStaticProviderCreationFails()
    {
        var metadataPath = CreateMetadataPath();
        try
        {
            var input = CreateInput(overrides: new BuildSsdtOverrides(
                ModelPath: "model.json",
                ProfilePath: "profile.snapshot",
                OutputDirectory: "out",
                ProfilerProvider: "fixture",
                StaticDataPath: null,
                RenameOverrides: null,
                MaxDegreeOfParallelism: null,
                SqlMetadataOutputPath: metadataPath));

            var dispatcher = new RecordingDispatcher();
            var assembler = new BuildSsdtRequestAssembler();
            var modelResolution = RecordingModelResolutionService.CreateWithRequestLog();
            var outputResolver = new TestOutputDirectoryResolver();
            var namingBinder = new TestNamingOverridesBinder();
            var staticDataFactory = new FailingStaticDataProviderFactory();

            var service = new BuildSsdtApplicationService(
                dispatcher,
                assembler,
                modelResolution,
                outputResolver,
                namingBinder,
                staticDataFactory);

            var result = await service.RunAsync(input, CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Contains(result.Errors, error => error.Code == FailingStaticDataProviderFactory.ErrorCode);
            Assert.True(File.Exists(metadataPath));
        }
        finally
        {
            DeleteMetadataDirectory(metadataPath);
        }
    }

    [Fact]
    public async Task RunAsync_FlushesMetadataWhenAssemblerFails()
    {
        var metadataPath = CreateMetadataPath();
        try
        {
            var input = CreateInput(
                overrides: new BuildSsdtOverrides(
                    ModelPath: "model.json",
                    ProfilePath: null,
                    OutputDirectory: "out",
                    ProfilerProvider: "fixture",
                    StaticDataPath: null,
                    RenameOverrides: null,
                    MaxDegreeOfParallelism: null,
                    SqlMetadataOutputPath: metadataPath),
                configuration: CreateConfiguration(profilePath: null));

            var dispatcher = new RecordingDispatcher();
            var assembler = new BuildSsdtRequestAssembler();
            var modelResolution = RecordingModelResolutionService.CreateWithRequestLog();
            var outputResolver = new TestOutputDirectoryResolver();
            var namingBinder = new TestNamingOverridesBinder();
            var staticDataFactory = new TestStaticDataProviderFactory(new TestStaticEntityDataProvider());

            var service = new BuildSsdtApplicationService(
                dispatcher,
                assembler,
                modelResolution,
                outputResolver,
                namingBinder,
                staticDataFactory);

            var result = await service.RunAsync(input, CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Contains(result.Errors, error => error.Code == "pipeline.buildSsdt.profile.missing");
            Assert.True(File.Exists(metadataPath));
            Assert.Null(dispatcher.Request);
        }
        finally
        {
            DeleteMetadataDirectory(metadataPath);
        }
    }

    [Fact]
    public async Task RunAsync_FlushesMetadataWhenParallelismInvalid()
    {
        var metadataPath = CreateMetadataPath();
        try
        {
            var input = CreateInput(overrides: new BuildSsdtOverrides(
                ModelPath: "model.json",
                ProfilePath: "profile.snapshot",
                OutputDirectory: "out",
                ProfilerProvider: "fixture",
                StaticDataPath: null,
                RenameOverrides: null,
                MaxDegreeOfParallelism: 0,
                SqlMetadataOutputPath: metadataPath));

            var dispatcher = new RecordingDispatcher();
            var assembler = new BuildSsdtRequestAssembler();
            var modelResolution = RecordingModelResolutionService.CreateWithRequestLog();
            var outputResolver = new TestOutputDirectoryResolver();
            var namingBinder = new TestNamingOverridesBinder();
            var staticDataFactory = new TestStaticDataProviderFactory(new TestStaticEntityDataProvider());

            var service = new BuildSsdtApplicationService(
                dispatcher,
                assembler,
                modelResolution,
                outputResolver,
                namingBinder,
                staticDataFactory);

            var result = await service.RunAsync(input, CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Contains(result.Errors, error => error.Code == "cli.buildSsdt.parallelism.invalid");
            Assert.True(File.Exists(metadataPath));
            Assert.Null(dispatcher.Request);
        }
        finally
        {
            DeleteMetadataDirectory(metadataPath);
        }
    }

    private static BuildSsdtApplicationInput CreateInput(
        BuildSsdtOverrides overrides,
        CliConfiguration? configuration = null)
    {
        var moduleFilterOverrides = new ModuleFilterOverrides(
            Array.Empty<string>(),
            IncludeSystemModules: null,
            IncludeInactiveModules: null,
            AllowMissingPrimaryKey: Array.Empty<string>(),
            AllowMissingSchema: Array.Empty<string>());
        var sqlOverrides = new SqlOptionsOverrides(null, null, null, null, null, null, null, null);
        var cacheOverrides = new CacheOptionsOverrides(null, null);
        configuration ??= CreateConfiguration();
        var context = new CliConfigurationContext(configuration, "config.json");
        return new BuildSsdtApplicationInput(context, overrides, moduleFilterOverrides, sqlOverrides, cacheOverrides);
    }

    private static CliConfiguration CreateConfiguration(string? profilePath = "profile.snapshot")
    {
        return new CliConfiguration(
            TighteningOptions.Default,
            ModelPath: null,
            ProfilePath: profilePath,
            DmmPath: null,
            CacheConfiguration.Empty,
            new ProfilerConfiguration("fixture", profilePath, null),
            SqlConfiguration.Empty,
            ModuleFilterConfiguration.Empty,
            TypeMappingConfiguration.Empty,
            SupplementalModelConfiguration.Empty);
    }

    private static string CreateMetadataPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        return Path.Combine(directory, "sql-metadata.json");
    }

    private static void DeleteMetadataDirectory(string metadataPath)
    {
        if (string.IsNullOrWhiteSpace(metadataPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(metadataPath);
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static BuildSsdtPipelineResult CreatePipelineResult()
    {
        var profileResult = ProfileSnapshot.Create(Array.Empty<ColumnProfile>(), Array.Empty<UniqueCandidateProfile>(), Array.Empty<CompositeUniqueCandidateProfile>(), Array.Empty<ForeignKeyReality>());
        if (profileResult.IsFailure)
        {
            throw new InvalidOperationException("Failed to create profile snapshot for test.");
        }

        var toggles = TighteningToggleSnapshot.Create(TighteningOptions.Default);
        var report = new PolicyDecisionReport(
            ImmutableArray<ColumnDecisionReport>.Empty,
            ImmutableArray<UniqueIndexDecisionReport>.Empty,
            ImmutableArray<ForeignKeyDecisionReport>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableArray<TighteningDiagnostic>.Empty,
            ImmutableDictionary<string, ModuleDecisionRollup>.Empty,
            toggles);
        var manifest = new SsdtManifest(
            Array.Empty<TableManifestEntry>(),
            new SsdtManifestOptions(false, false, true, 1),
            null,
            new SsdtEmissionMetadata("sha256", "hash"),
            Array.Empty<PreRemediationManifestEntry>(),
            SsdtCoverageSummary.CreateComplete(0, 0, 0),
            SsdtPredicateCoverage.Empty,
            Array.Empty<string>());
        var opportunities = new Opportunities.OpportunitiesReport(
            ImmutableArray<Opportunities.Opportunity>.Empty,
            ImmutableDictionary<Opportunities.OpportunityDisposition, int>.Empty,
            ImmutableDictionary<Opportunities.OpportunityType, int>.Empty,
            ImmutableDictionary<RiskLevel, int>.Empty,
            DateTimeOffset.UtcNow);

        return new BuildSsdtPipelineResult(
            profileResult.Value,
            ImmutableArray<ProfilingInsight>.Empty,
            report,
            opportunities,
            manifest,
            ImmutableArray<PipelineInsight>.Empty,
            "decision.log",
            "opportunities.json",
            "suggestions/safe-to-apply.sql",
            "-- safe script\nGO\n",
            "suggestions/needs-remediation.sql",
            "-- remediation script\nGO\n",
            ImmutableArray<string>.Empty,
            SsdtSqlValidationSummary.Empty,
            null,
            PipelineExecutionLog.Empty,
            ImmutableArray<string>.Empty);
    }

    private sealed class RecordingDispatcher : ICommandDispatcher
    {
        private Result<BuildSsdtPipelineResult>? _result;

        public BuildSsdtPipelineRequest? Request { get; private set; }

        public void SetResult(Result<BuildSsdtPipelineResult> result)
        {
            _result = result ?? throw new ArgumentNullException(nameof(result));
        }

        public Task<Result<TResponse>> DispatchAsync<TCommand, TResponse>(TCommand command, CancellationToken cancellationToken = default)
            where TCommand : ICommand<TResponse>
        {
            if (command is BuildSsdtPipelineRequest build)
            {
                Request = build;
                if (_result is null)
                {
                    throw new InvalidOperationException("Dispatcher result was not configured.");
                }

                if (_result.IsFailure)
                {
                    return Task.FromResult(Result<TResponse>.Failure(_result.Errors));
                }

                return Task.FromResult(Result<TResponse>.Success((TResponse)(object)_result.Value));
            }

            throw new InvalidOperationException($"Unexpected command type: {typeof(TCommand).Name}");
        }
    }

    private sealed class StubModelResolutionService : IModelResolutionService
    {
        public Task<Result<ModelResolutionResult>> ResolveModelAsync(
            CliConfiguration configuration,
            BuildSsdtOverrides overrides,
            ModuleFilterOptions moduleFilter,
            ResolvedSqlOptions sqlOptions,
            string outputDirectory,
            SqlMetadataLog? sqlMetadataLog,
            CancellationToken cancellationToken)
        {
            var model = new ModelResolutionResult(overrides.ModelPath!, false, ImmutableArray<string>.Empty);
            return Task.FromResult(Result<ModelResolutionResult>.Success(model));
        }
    }

    private sealed class FailingModelResolutionService : IModelResolutionService
    {
        public const string ErrorCode = "pipeline.modelResolution.failed";

        public Task<Result<ModelResolutionResult>> ResolveModelAsync(
            CliConfiguration configuration,
            BuildSsdtOverrides overrides,
            ModuleFilterOptions moduleFilter,
            ResolvedSqlOptions sqlOptions,
            string outputDirectory,
            SqlMetadataLog? sqlMetadataLog,
            CancellationToken cancellationToken)
        {
            sqlMetadataLog?.RecordFailure(
                new[] { ValidationError.Create(ErrorCode, "Model resolution failed.") },
                rowSnapshot: null);

            return Task.FromResult(Result<ModelResolutionResult>.Failure(
                ValidationError.Create(ErrorCode, "Model resolution failed.")));
        }
    }

    private sealed class FailingStaticDataProviderFactory : IStaticDataProviderFactory
    {
        public const string ErrorCode = "pipeline.staticData.missingSource";

        public Result<IStaticEntityDataProvider?> Create(
            BuildSsdtOverrides overrides,
            ResolvedSqlOptions sqlOptions,
            TighteningOptions tighteningOptions)
        {
            return ValidationError.Create(ErrorCode, "Static data provider creation failed.");
        }
    }

    private sealed class RecordingModelResolutionService : IModelResolutionService
    {
        private readonly ModelResolutionResult _result;
        private readonly Action<SqlMetadataLog?> _logCallback;

        private RecordingModelResolutionService(ModelResolutionResult result, Action<SqlMetadataLog?> logCallback)
        {
            _result = result;
            _logCallback = logCallback;
        }

        public static RecordingModelResolutionService CreateWithRequestLog()
        {
            var model = new ModelResolutionResult("model.json", false, ImmutableArray<string>.Empty);
            return new RecordingModelResolutionService(model, log => log?.RecordRequest("test", new { value = 1 }));
        }

        public Task<Result<ModelResolutionResult>> ResolveModelAsync(
            CliConfiguration configuration,
            BuildSsdtOverrides overrides,
            ModuleFilterOptions moduleFilter,
            ResolvedSqlOptions sqlOptions,
            string outputDirectory,
            SqlMetadataLog? sqlMetadataLog,
            CancellationToken cancellationToken)
        {
            _logCallback(sqlMetadataLog);
            return Task.FromResult(Result<ModelResolutionResult>.Success(_result));
        }
    }

    private sealed class TestOutputDirectoryResolver : IOutputDirectoryResolver
    {
        public string Resolve(BuildSsdtOverrides overrides) => overrides.OutputDirectory ?? "out";
    }

    private sealed class TestNamingOverridesBinder : INamingOverridesBinder
    {
        public Result<NamingOverrideOptions> Bind(BuildSsdtOverrides overrides, TighteningOptions tighteningOptions)
        {
            return Result<NamingOverrideOptions>.Success(NamingOverrideOptions.Empty);
        }
    }

    private sealed class TestStaticDataProviderFactory : IStaticDataProviderFactory
    {
        private readonly IStaticEntityDataProvider _provider;

        public TestStaticDataProviderFactory(IStaticEntityDataProvider provider)
        {
            _provider = provider;
        }

        public Result<IStaticEntityDataProvider?> Create(BuildSsdtOverrides overrides, ResolvedSqlOptions sqlOptions, TighteningOptions tighteningOptions)
        {
            return Result<IStaticEntityDataProvider?>.Success(_provider);
        }
    }

    private sealed class TestStaticEntityDataProvider : IStaticEntityDataProvider
    {
        public Task<Result<IReadOnlyList<StaticEntityTableData>>> GetDataAsync(IReadOnlyList<StaticEntitySeedTableDefinition> definitions, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<StaticEntityTableData>>.Success(Array.Empty<StaticEntityTableData>()));
        }
    }
}
