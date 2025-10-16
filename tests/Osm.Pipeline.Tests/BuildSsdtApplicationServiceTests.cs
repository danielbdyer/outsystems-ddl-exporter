using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Profiling;
using Osm.Domain.Profiling.Insights;
using Osm.Emission;
using Osm.Emission.Seeds;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Orchestration;
using Osm.Validation.Tightening;
using Xunit;

namespace Osm.Pipeline.Tests;

public sealed class BuildSsdtApplicationServiceTests
{
    [Fact]
    public async Task RunAsync_ComposesPipelineRequestFromDependencies()
    {
        var overrides = new BuildSsdtOverrides(
            ModelPath: "model.json",
            ProfilePath: "profile.snapshot",
            OutputDirectory: "out",
            ProfilerProvider: "fixture",
            StaticDataPath: null,
            RenameOverrides: null,
            MaxDegreeOfParallelism: null);
        var moduleFilterOverrides = new ModuleFilterOverrides(
            Array.Empty<string>(),
            IncludeSystemModules: null,
            IncludeInactiveModules: null,
            AllowMissingPrimaryKey: Array.Empty<string>(),
            AllowMissingSchema: Array.Empty<string>());
        var sqlOverrides = new SqlOptionsOverrides(null, null, null, null, null, null, null, null);
        var cacheOverrides = new CacheOptionsOverrides(null, null);
        var configuration = new CliConfiguration(
            TighteningOptions.Default,
            ModelPath: null,
            ProfilePath: null,
            DmmPath: null,
            CacheConfiguration.Empty,
            new ProfilerConfiguration("fixture", null, null),
            SqlConfiguration.Empty,
            ModuleFilterConfiguration.Empty,
            TypeMappingConfiguration.Empty,
            SupplementalModelConfiguration.Empty);
        var context = new CliConfigurationContext(configuration, "config.json");
        var input = new BuildSsdtApplicationInput(context, overrides, moduleFilterOverrides, sqlOverrides, cacheOverrides);

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
            Array.Empty<string>());
        return new BuildSsdtPipelineResult(
            profileResult.Value,
            ProfileInsightReport.Empty,
            report,
            manifest,
            "decision.log",
            ImmutableArray<string>.Empty,
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
            CancellationToken cancellationToken)
        {
            var model = new ModelResolutionResult(overrides.ModelPath!, false, ImmutableArray<string>.Empty);
            return Task.FromResult(Result<ModelResolutionResult>.Success(model));
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
