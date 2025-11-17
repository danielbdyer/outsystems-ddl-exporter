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
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.DynamicData;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Sql;
using Osm.Pipeline.Profiling;
using Osm.Validation.Tightening;
using Opportunities = Osm.Validation.Tightening.Opportunities;
using ValidationReport = Osm.Validation.Tightening.Validations.ValidationReport;
using Tests.Support;
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
            MaxDegreeOfParallelism: null,
            SqlMetadataOutputPath: null);
        var moduleFilterOverrides = new ModuleFilterOverrides(
            Array.Empty<string>(),
            IncludeSystemModules: null,
            IncludeInactiveModules: null,
            AllowMissingPrimaryKey: Array.Empty<string>(),
            AllowMissingSchema: Array.Empty<string>());
        var sqlOverrides = new SqlOptionsOverrides(null, null, null, null, null, null, null, null, null);
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
            SupplementalModelConfiguration.Empty,
            DynamicDataConfiguration.Empty,
            UatUsersConfiguration.Empty);
        var context = new CliConfigurationContext(configuration, "config.json");
        var input = new BuildSsdtApplicationInput(
            context,
            overrides,
            moduleFilterOverrides,
            sqlOverrides,
            cacheOverrides,
            TighteningOverrides: null,
            DynamicDataset: null,
            EnableDynamicSqlExtraction: true);

        var dispatcher = new RecordingDispatcher();
        dispatcher.SetResult(Result<BuildSsdtPipelineResult>.Success(CreatePipelineResult()));
        var assembler = new BuildSsdtRequestAssembler();
        var modelResolution = new StubModelResolutionService();
        var outputResolver = new TestOutputDirectoryResolver();
        var namingBinder = new TestNamingOverridesBinder();
        var staticDataProvider = new TestStaticEntityDataProvider();
        var staticDataFactory = new TestStaticDataProviderFactory(staticDataProvider);
        var modelIngestion = new TestModelIngestionService();
        var dynamicDataProvider = new TestDynamicEntityDataProvider();

        var service = new BuildSsdtApplicationService(
            dispatcher,
            assembler,
            modelResolution,
            outputResolver,
            namingBinder,
            staticDataFactory,
            modelIngestion,
            dynamicDataProvider);

        Assert.True(input.EnableDynamicSqlExtraction);

        var result = await service.RunAsync(input, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(dispatcher.Request);
        Assert.Same(staticDataProvider, dispatcher.Request!.StaticDataProvider);
        Assert.Equal("out", result.Value.OutputDirectory);
        Assert.Equal("model.json", result.Value.ModelPath);
        Assert.False(result.Value.ModelWasExtracted);
    }

    [Fact]
    public async Task RunAsync_UsesSqlDynamicDataProvider_WhenDatasetMissingAndConnectionAvailable()
    {
        var modelPath = FixtureFile.GetPath("model.edge-case.json");
        var overrides = new BuildSsdtOverrides(
            ModelPath: modelPath,
            ProfilePath: FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json")),
            OutputDirectory: Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            ProfilerProvider: "fixture",
            StaticDataPath: null,
            RenameOverrides: null,
            MaxDegreeOfParallelism: null,
            SqlMetadataOutputPath: null);

        var moduleFilterOverrides = new ModuleFilterOverrides(
            Array.Empty<string>(),
            IncludeSystemModules: null,
            IncludeInactiveModules: null,
            AllowMissingPrimaryKey: Array.Empty<string>(),
            AllowMissingSchema: Array.Empty<string>());

        var sqlOverrides = new SqlOptionsOverrides(
            ConnectionString: "Server=(localdb)\\MSSQLLocalDB;Database=Fake;Integrated Security=true;",
            CommandTimeoutSeconds: null,
            SamplingThreshold: null,
            SamplingSize: null,
            AuthenticationMethod: null,
            TrustServerCertificate: null,
            ApplicationName: null,
            AccessToken: null,
            ProfilingConnectionStrings: null);

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
            SupplementalModelConfiguration.Empty,
            DynamicDataConfiguration.Empty,
            UatUsersConfiguration.Empty);
        var context = new CliConfigurationContext(configuration, "config.json");
        var input = new BuildSsdtApplicationInput(
            context,
            overrides,
            moduleFilterOverrides,
            sqlOverrides,
            cacheOverrides,
            EnableDynamicSqlExtraction: true);

        var dispatcher = new RecordingDispatcher();
        dispatcher.SetResult(Result<BuildSsdtPipelineResult>.Success(CreatePipelineResult()));
        var assembler = new BuildSsdtRequestAssembler();
        var modelResolution = new StubModelResolutionService { ModelPathOverride = modelPath };
        var outputResolver = new TestOutputDirectoryResolver();
        var namingBinder = new TestNamingOverridesBinder();
        var staticDataProvider = new TestStaticEntityDataProvider();
        var staticDataFactory = new TestStaticDataProviderFactory(staticDataProvider);
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var ingestion = new TestModelIngestionService
        {
            Loader = _ => Result<Osm.Domain.Model.OsmModel>.Success(model)
        };

        var dynamicDataset = CreateDynamicDataset();
        var dynamicProvider = new TestDynamicEntityDataProvider
        {
            Dataset = dynamicDataset
        };

        var service = new BuildSsdtApplicationService(
            dispatcher,
            assembler,
            modelResolution,
            outputResolver,
            namingBinder,
            staticDataFactory,
            ingestion,
            dynamicProvider);

        var result = await service.RunAsync(input, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(dispatcher.Request);
        Assert.NotNull(dynamicProvider.LastRequest);
        Assert.Same(dynamicDataset, dispatcher.Request!.DynamicDataset);
        Assert.False(dispatcher.Request.DynamicDataset.IsEmpty);
        Assert.Equal(DynamicDatasetSource.SqlProvider, dispatcher.Request.DynamicDatasetSource);
        Assert.Equal(model, dynamicProvider.LastRequest!.Model);
        Assert.Equal(sqlOverrides.ConnectionString, dynamicProvider.LastRequest.ConnectionString);
        Assert.Equal(modelPath, ingestion.LastPath);
    }

    [Fact]
    public async Task RunAsync_DoesNotUseSqlDynamicDataProvider_WhenExtractionDisabled()
    {
        var modelPath = FixtureFile.GetPath("model.edge-case.json");
        var overrides = new BuildSsdtOverrides(
            ModelPath: modelPath,
            ProfilePath: FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json")),
            OutputDirectory: Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            ProfilerProvider: "fixture",
            StaticDataPath: null,
            RenameOverrides: null,
            MaxDegreeOfParallelism: null,
            SqlMetadataOutputPath: null);

        var moduleFilterOverrides = new ModuleFilterOverrides(
            Array.Empty<string>(),
            IncludeSystemModules: null,
            IncludeInactiveModules: null,
            AllowMissingPrimaryKey: Array.Empty<string>(),
            AllowMissingSchema: Array.Empty<string>());

        var sqlOverrides = new SqlOptionsOverrides(
            ConnectionString: "Server=(localdb)\\MSSQLLocalDB;Database=Fake;Integrated Security=true;",
            CommandTimeoutSeconds: null,
            SamplingThreshold: null,
            SamplingSize: null,
            AuthenticationMethod: null,
            TrustServerCertificate: null,
            ApplicationName: null,
            AccessToken: null,
            ProfilingConnectionStrings: null);

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
            SupplementalModelConfiguration.Empty,
            DynamicDataConfiguration.Empty,
            UatUsersConfiguration.Empty);
        var context = new CliConfigurationContext(configuration, "config.json");
        var input = new BuildSsdtApplicationInput(context, overrides, moduleFilterOverrides, sqlOverrides, cacheOverrides);

        var dispatcher = new RecordingDispatcher();
        dispatcher.SetResult(Result<BuildSsdtPipelineResult>.Success(CreatePipelineResult()));
        var assembler = new BuildSsdtRequestAssembler();
        var modelResolution = new StubModelResolutionService { ModelPathOverride = modelPath };
        var outputResolver = new TestOutputDirectoryResolver();
        var namingBinder = new TestNamingOverridesBinder();
        var staticDataProvider = new TestStaticEntityDataProvider();
        var staticDataFactory = new TestStaticDataProviderFactory(staticDataProvider);
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var ingestion = new TestModelIngestionService
        {
            Loader = _ => Result<Osm.Domain.Model.OsmModel>.Success(model)
        };

        var dynamicProvider = new TestDynamicEntityDataProvider();

        var service = new BuildSsdtApplicationService(
            dispatcher,
            assembler,
            modelResolution,
            outputResolver,
            namingBinder,
            staticDataFactory,
            ingestion,
            dynamicProvider);

        var result = await service.RunAsync(input, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(dispatcher.Request);
        Assert.True(dispatcher.Request!.DynamicDataset.IsEmpty);
        Assert.Equal(DynamicDatasetSource.None, dispatcher.Request.DynamicDatasetSource);
        Assert.Null(dynamicProvider.LastRequest);
    }

    private static DynamicEntityDataset CreateDynamicDataset()
    {
        var columns = ImmutableArray.Create(new StaticEntitySeedColumn(
            LogicalName: "Identifier",
            ColumnName: "ID",
            EmissionName: "ID",
            DataType: "int",
            Length: null,
            Precision: null,
            Scale: null,
            IsPrimaryKey: true,
            IsIdentity: true,
            IsNullable: false));

        var definition = new StaticEntitySeedTableDefinition(
            Module: "Core",
            LogicalName: "User",
            Schema: "dbo",
            PhysicalName: "OSUSR_CORE_USER",
            EffectiveName: "OSUSR_CORE_USER",
            Columns: columns);

        var rows = ImmutableArray.Create(StaticEntityRow.Create(new object?[] { 1 }));
        var table = StaticEntityTableData.Create(definition, rows);
        return DynamicEntityDataset.Create(new[] { table });
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
            ImmutableDictionary<string, ToggleExportValue>.Empty,
            ImmutableDictionary<string, string>.Empty,
            ImmutableDictionary<string, string>.Empty,
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
            ImmutableDictionary<Opportunities.OpportunityCategory, int>.Empty,
            ImmutableDictionary<Opportunities.OpportunityType, int>.Empty,
            ImmutableDictionary<RiskLevel, int>.Empty,
            DateTimeOffset.UtcNow);

        var validations = ValidationReport.Empty(DateTimeOffset.UtcNow);

        return new BuildSsdtPipelineResult(
            profileResult.Value,
            ImmutableArray<ProfilingInsight>.Empty,
            report,
            opportunities,
            validations,
            manifest,
            ImmutableDictionary<string, ModuleManifestRollup>.Empty,
            ImmutableArray<PipelineInsight>.Empty,
            "decision.log",
            "opportunities.json",
            "validations.json",
            "suggestions/safe-to-apply.sql",
            "-- safe script\nGO\n",
            "suggestions/needs-remediation.sql",
            "-- remediation script\nGO\n",
            Path.Combine("output", "OutSystemsModel.sqlproj"),
            ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty,
            SsdtSqlValidationSummary.Empty,
            null,
            PipelineExecutionLog.Empty,
            StaticSeedTopologicalOrderApplied: false,
            DynamicInsertTopologicalOrderApplied: false,
            DynamicInsertOutputMode: DynamicInsertOutputMode.PerEntity,
            ImmutableArray<string>.Empty,
            MultiEnvironmentProfileReport.Empty);
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
        public string? ModelPathOverride { get; set; }

        public bool WasExtracted { get; set; }

        public ImmutableArray<string> Warnings { get; set; } = ImmutableArray<string>.Empty;

        public Task<Result<ModelResolutionResult>> ResolveModelAsync(
            CliConfiguration configuration,
            BuildSsdtOverrides overrides,
            ModuleFilterOptions moduleFilter,
            ResolvedSqlOptions sqlOptions,
            string outputDirectory,
            SqlMetadataLog? sqlMetadataLog,
            CancellationToken cancellationToken)
        {
            var path = ModelPathOverride ?? overrides.ModelPath ?? configuration.ModelPath ?? "model.json";
            var model = new ModelResolutionResult(path!, WasExtracted, Warnings);
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

    private sealed class TestModelIngestionService : IModelIngestionService
    {
        public Func<string, Result<Osm.Domain.Model.OsmModel>>? Loader { get; set; }

        public string? LastPath { get; private set; }

        public Task<Result<Osm.Domain.Model.OsmModel>> LoadFromFileAsync(
            string path,
            ICollection<string>? warnings = null,
            CancellationToken cancellationToken = default,
            ModelIngestionOptions? options = null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path must be provided.", nameof(path));
            }

            LastPath = path;
            if (Loader is not null)
            {
                return Task.FromResult(Loader(path));
            }

            var model = ModelFixtures.LoadModel(Path.GetFileName(path));
            return Task.FromResult(Result<Osm.Domain.Model.OsmModel>.Success(model));
        }
    }

    private sealed class TestDynamicEntityDataProvider : IDynamicEntityDataProvider
    {
        public DynamicEntityDataset Dataset { get; set; } = DynamicEntityDataset.Empty;

        public SqlDynamicEntityExtractionRequest? LastRequest { get; private set; }

        public Task<Result<DynamicEntityExtractionResult>> ExtractAsync(
            SqlDynamicEntityExtractionRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            var result = new DynamicEntityExtractionResult(
                Dataset,
                DynamicEntityExtractionTelemetry.Empty,
                ImmutableArray<StaticSeedParentStatus>.Empty);
            return Task.FromResult(Result<DynamicEntityExtractionResult>.Success(result));
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
