using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Immutable;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.SqlExtraction;
using Osm.Validation.Tightening;
using Xunit;

namespace Osm.Pipeline.Tests;

public sealed class ModuleFilterConsistencyTests
{
    private static readonly SqlOptionsOverrides DefaultSqlOverrides = new(
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null);

    [Fact]
    public async Task CliOverrides_AreAppliedConsistently()
    {
        var configuration = CreateConfiguration(
            new[] { "ConfigOnly" },
            includeSystem: true,
            includeInactive: true);
        var context = new CliConfigurationContext(configuration, "config.json");

        var extractDispatcher = new RecordingDispatcher();
        var extractService = new ExtractModelApplicationService(extractDispatcher);
        var extractOverrides = new ExtractModelOverrides(
            new[] { "Ops", "AppCore", "ops" },
            IncludeSystemModules: false,
            OnlyActiveAttributes: true,
            OutputPath: null,
            MockAdvancedSqlManifest: null);

        await extractService.RunAsync(new ExtractModelApplicationInput(context, extractOverrides, DefaultSqlOverrides));

        var extractRequest = extractDispatcher.ExtractRequest;
        Assert.NotNull(extractRequest);
        var extractCommand = extractRequest!.Command;
        Assert.Equal(new[] { "AppCore", "Ops" }, extractCommand.ModuleNames.Select(static module => module.Value).ToArray());
        Assert.False(extractCommand.IncludeSystemModules);
        Assert.True(extractCommand.OnlyActiveAttributes);

        var buildDispatcher = new RecordingDispatcher();
        var buildService = CreateBuildService(buildDispatcher);
        var buildModuleFilter = new ModuleFilterOverrides(
            new[] { "Ops", "AppCore", "ops" },
            IncludeSystemModules: false,
            IncludeInactiveModules: false,
            AllowMissingPrimaryKey: Array.Empty<string>(),
            AllowMissingSchema: Array.Empty<string>());
        var buildOverrides = new BuildSsdtOverrides(
            ModelPath: "model.json",
            ProfilePath: "profile.snapshot",
            OutputDirectory: "out",
            ProfilerProvider: null,
            StaticDataPath: null,
            RenameOverrides: null,
            MaxDegreeOfParallelism: null);

        await buildService.RunAsync(new BuildSsdtApplicationInput(
            context,
            buildOverrides,
            buildModuleFilter,
            DefaultSqlOverrides,
            new CacheOptionsOverrides(null, null)));

        var buildRequest = buildDispatcher.BuildRequest;
        Assert.NotNull(buildRequest);
        Assert.Equal(new[] { "AppCore", "Ops" }, buildRequest!.ModuleFilter.Modules.Select(static module => module.Value).ToArray());
        Assert.False(buildRequest.ModuleFilter.IncludeSystemModules);
        Assert.False(buildRequest.ModuleFilter.IncludeInactiveModules);

        var compareDispatcher = new RecordingDispatcher();
        var compareService = new CompareWithDmmApplicationService(compareDispatcher);
        var compareModuleFilter = new ModuleFilterOverrides(
            new[] { "Ops", "AppCore", "ops" },
            IncludeSystemModules: false,
            IncludeInactiveModules: false,
            AllowMissingPrimaryKey: Array.Empty<string>(),
            AllowMissingSchema: Array.Empty<string>());
        var compareOverrides = new CompareWithDmmOverrides(
            ModelPath: "model.json",
            ProfilePath: "profile.snapshot",
            DmmPath: "baseline.dmm",
            OutputDirectory: "out",
            MaxDegreeOfParallelism: null);

        await compareService.RunAsync(new CompareWithDmmApplicationInput(
            context,
            compareOverrides,
            compareModuleFilter,
            DefaultSqlOverrides,
            new CacheOptionsOverrides(null, null)));

        var compareRequest = compareDispatcher.CompareRequest;
        Assert.NotNull(compareRequest);
        Assert.Equal(new[] { "AppCore", "Ops" }, compareRequest!.ModuleFilter.Modules.Select(static module => module.Value).ToArray());
        Assert.False(compareRequest.ModuleFilter.IncludeSystemModules);
        Assert.False(compareRequest.ModuleFilter.IncludeInactiveModules);
    }

    [Fact]
    public async Task ConfigurationDefaults_AreRespected_WhenOverridesMissing()
    {
        var configuration = CreateConfiguration(
            new[] { "ConfigA", "ConfigB" },
            includeSystem: true,
            includeInactive: false);
        var context = new CliConfigurationContext(configuration, "config.json");

        var extractDispatcher = new RecordingDispatcher();
        var extractService = new ExtractModelApplicationService(extractDispatcher);
        await extractService.RunAsync(new ExtractModelApplicationInput(
            context,
            new ExtractModelOverrides(null, null, null, null, null),
            DefaultSqlOverrides));

        var extractRequest = extractDispatcher.ExtractRequest;
        Assert.NotNull(extractRequest);
        var extractCommand = extractRequest!.Command;
        Assert.Equal(new[] { "ConfigA", "ConfigB" }, extractCommand.ModuleNames.Select(static module => module.Value).ToArray());
        Assert.True(extractCommand.IncludeSystemModules);
        Assert.True(extractCommand.OnlyActiveAttributes);

        var buildDispatcher = new RecordingDispatcher();
        var buildService = CreateBuildService(buildDispatcher);
        var buildOverrides = new BuildSsdtOverrides(
            ModelPath: "model.json",
            ProfilePath: "profile.snapshot",
            OutputDirectory: "out",
            ProfilerProvider: null,
            StaticDataPath: null,
            RenameOverrides: null,
            MaxDegreeOfParallelism: null);

        await buildService.RunAsync(new BuildSsdtApplicationInput(
            context,
            buildOverrides,
            new ModuleFilterOverrides(
                Array.Empty<string>(),
                IncludeSystemModules: null,
                IncludeInactiveModules: null,
                AllowMissingPrimaryKey: Array.Empty<string>(),
                AllowMissingSchema: Array.Empty<string>()),
            DefaultSqlOverrides,
            new CacheOptionsOverrides(null, null)));

        var buildRequest = buildDispatcher.BuildRequest;
        Assert.NotNull(buildRequest);
        Assert.Equal(new[] { "ConfigA", "ConfigB" }, buildRequest!.ModuleFilter.Modules.Select(static module => module.Value).ToArray());
        Assert.True(buildRequest.ModuleFilter.IncludeSystemModules);
        Assert.False(buildRequest.ModuleFilter.IncludeInactiveModules);

        var compareDispatcher = new RecordingDispatcher();
        var compareService = new CompareWithDmmApplicationService(compareDispatcher);
        var compareOverrides = new CompareWithDmmOverrides(
            ModelPath: "model.json",
            ProfilePath: "profile.snapshot",
            DmmPath: "baseline.dmm",
            OutputDirectory: "out",
            MaxDegreeOfParallelism: null);

        await compareService.RunAsync(new CompareWithDmmApplicationInput(
            context,
            compareOverrides,
            new ModuleFilterOverrides(
                Array.Empty<string>(),
                IncludeSystemModules: null,
                IncludeInactiveModules: null,
                AllowMissingPrimaryKey: Array.Empty<string>(),
                AllowMissingSchema: Array.Empty<string>()),
            DefaultSqlOverrides,
            new CacheOptionsOverrides(null, null)));

        var compareRequest = compareDispatcher.CompareRequest;
        Assert.NotNull(compareRequest);
        Assert.Equal(new[] { "ConfigA", "ConfigB" }, compareRequest!.ModuleFilter.Modules.Select(static module => module.Value).ToArray());
        Assert.True(compareRequest.ModuleFilter.IncludeSystemModules);
        Assert.False(compareRequest.ModuleFilter.IncludeInactiveModules);
    }

    [Fact]
    public async Task ValidationOverrideErrors_SurfaceAcrossAllServices()
    {
        var validationOverrides = new Dictionary<string, ModuleValidationOverrideConfiguration>(StringComparer.OrdinalIgnoreCase)
        {
            [" "] = ModuleValidationOverrideConfiguration.Empty
        };
        var configuration = CreateConfiguration(
            modules: Array.Empty<string>(),
            includeSystem: null,
            includeInactive: null,
            validationOverrides: validationOverrides);
        var context = new CliConfigurationContext(configuration, "config.json");

        var extractService = new ExtractModelApplicationService(new RecordingDispatcher());
        var extractResult = await extractService.RunAsync(new ExtractModelApplicationInput(
            context,
            new ExtractModelOverrides(null, null, null, null, null),
            DefaultSqlOverrides));
        AssertValidationOverrideFailure(extractResult);

        var buildService = CreateBuildService(new RecordingDispatcher());
        var buildOverrides = new BuildSsdtOverrides(
            ModelPath: "model.json",
            ProfilePath: "profile.snapshot",
            OutputDirectory: "out",
            ProfilerProvider: null,
            StaticDataPath: null,
            RenameOverrides: null,
            MaxDegreeOfParallelism: null);
        var buildResult = await buildService.RunAsync(new BuildSsdtApplicationInput(
            context,
            buildOverrides,
            new ModuleFilterOverrides(
                Array.Empty<string>(),
                IncludeSystemModules: null,
                IncludeInactiveModules: null,
                AllowMissingPrimaryKey: Array.Empty<string>(),
                AllowMissingSchema: Array.Empty<string>()),
            DefaultSqlOverrides,
            new CacheOptionsOverrides(null, null)));
        AssertValidationOverrideFailure(buildResult);

        var compareService = new CompareWithDmmApplicationService(new RecordingDispatcher());
        var compareOverrides = new CompareWithDmmOverrides(
            ModelPath: "model.json",
            ProfilePath: "profile.snapshot",
            DmmPath: "baseline.dmm",
            OutputDirectory: "out",
            MaxDegreeOfParallelism: null);
        var compareResult = await compareService.RunAsync(new CompareWithDmmApplicationInput(
            context,
            compareOverrides,
            new ModuleFilterOverrides(
                Array.Empty<string>(),
                IncludeSystemModules: null,
                IncludeInactiveModules: null,
                AllowMissingPrimaryKey: Array.Empty<string>(),
                AllowMissingSchema: Array.Empty<string>()),
            DefaultSqlOverrides,
            new CacheOptionsOverrides(null, null)));
        AssertValidationOverrideFailure(compareResult);
    }

    private static void AssertValidationOverrideFailure<T>(Result<T> result)
    {
        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, static error => error.Code == "moduleFilter.validationOverrides.module.empty");
    }

    private static CliConfiguration CreateConfiguration(
        IReadOnlyList<string> modules,
        bool? includeSystem,
        bool? includeInactive,
        IReadOnlyDictionary<string, ModuleValidationOverrideConfiguration>? validationOverrides = null)
    {
        return new CliConfiguration(
            TighteningOptions.Default,
            ModelPath: null,
            ProfilePath: null,
            DmmPath: null,
            CacheConfiguration.Empty,
            ProfilerConfiguration.Empty,
            SqlConfiguration.Empty,
            new ModuleFilterConfiguration(
                modules,
                includeSystem,
                includeInactive,
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
                validationOverrides ?? new Dictionary<string, ModuleValidationOverrideConfiguration>(StringComparer.OrdinalIgnoreCase)),
            TypeMappingConfiguration.Empty,
            SupplementalModelConfiguration.Empty);
    }

    private static BuildSsdtApplicationService CreateBuildService(RecordingDispatcher dispatcher)
    {
        var assembler = new BuildSsdtRequestAssembler();
        var modelResolution = new StubModelResolutionService();
        var outputDirectoryResolver = new OutputDirectoryResolver();
        var namingOverridesBinder = new NamingOverridesBinder();
        var staticDataProviderFactory = new StaticDataProviderFactory();
        return new BuildSsdtApplicationService(
            dispatcher,
            assembler,
            modelResolution,
            outputDirectoryResolver,
            namingOverridesBinder,
            staticDataProviderFactory);
    }

    private sealed class RecordingDispatcher : ICommandDispatcher
    {
        public ExtractModelPipelineRequest? ExtractRequest { get; private set; }

        public BuildSsdtPipelineRequest? BuildRequest { get; private set; }

        public DmmComparePipelineRequest? CompareRequest { get; private set; }

        public Task<Result<TResponse>> DispatchAsync<TCommand, TResponse>(TCommand command, CancellationToken cancellationToken = default)
            where TCommand : ICommand<TResponse>
        {
            switch (command)
            {
                case ExtractModelPipelineRequest extract:
                    ExtractRequest = extract;
                    break;
                case BuildSsdtPipelineRequest build:
                    BuildRequest = build;
                    break;
                case DmmComparePipelineRequest compare:
                    CompareRequest = compare;
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected command type: {typeof(TCommand).Name}");
            }

            return Task.FromResult(Result<TResponse>.Failure(ValidationError.Create("test.dispatch", "stub failure")));
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
            var path = overrides.ModelPath ?? configuration.ModelPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("Tests must provide a model path override.");
            }

            var result = new ModelResolutionResult(path!, false, ImmutableArray<string>.Empty);
            return Task.FromResult(Result<ModelResolutionResult>.Success(result));
        }
    }
}
