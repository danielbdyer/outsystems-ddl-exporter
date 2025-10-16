using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Profiling.Insights;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Orchestration;
using Osm.Smo;
using Osm.Validation.Tightening;

namespace Osm.Pipeline.Application;

public sealed record BuildSsdtApplicationInput(
    CliConfigurationContext ConfigurationContext,
    BuildSsdtOverrides Overrides,
    ModuleFilterOverrides ModuleFilter,
    SqlOptionsOverrides Sql,
    CacheOptionsOverrides Cache);

public sealed record BuildSsdtApplicationResult(
    BuildSsdtPipelineResult PipelineResult,
    ProfileInsightReport ProfileInsights,
    string ProfilerProvider,
    string? ProfilePath,
    string OutputDirectory,
    string ModelPath,
    bool ModelWasExtracted,
    ImmutableArray<string> ModelExtractionWarnings);

public sealed class BuildSsdtApplicationService : IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>
{
    private readonly ICommandDispatcher _dispatcher;
    private readonly BuildSsdtRequestAssembler _assembler;
    private readonly IModelResolutionService _modelResolutionService;
    private readonly IOutputDirectoryResolver _outputDirectoryResolver;
    private readonly INamingOverridesBinder _namingOverridesBinder;
    private readonly IStaticDataProviderFactory _staticDataProviderFactory;

    public BuildSsdtApplicationService(
        ICommandDispatcher dispatcher,
        BuildSsdtRequestAssembler assembler,
        IModelResolutionService modelResolutionService,
        IOutputDirectoryResolver outputDirectoryResolver,
        INamingOverridesBinder namingOverridesBinder,
        IStaticDataProviderFactory staticDataProviderFactory)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _assembler = assembler ?? throw new ArgumentNullException(nameof(assembler));
        _modelResolutionService = modelResolutionService ?? throw new ArgumentNullException(nameof(modelResolutionService));
        _outputDirectoryResolver = outputDirectoryResolver ?? throw new ArgumentNullException(nameof(outputDirectoryResolver));
        _namingOverridesBinder = namingOverridesBinder ?? throw new ArgumentNullException(nameof(namingOverridesBinder));
        _staticDataProviderFactory = staticDataProviderFactory ?? throw new ArgumentNullException(nameof(staticDataProviderFactory));
    }

    public async Task<Result<BuildSsdtApplicationResult>> RunAsync(BuildSsdtApplicationInput input, CancellationToken cancellationToken = default)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        var configuration = input.ConfigurationContext.Configuration;
        var tighteningOptions = configuration.Tightening;

        var sqlOptionsResult = SqlOptionsResolver.Resolve(configuration, input.Sql);
        if (sqlOptionsResult.IsFailure)
        {
            return Result<BuildSsdtApplicationResult>.Failure(sqlOptionsResult.Errors);
        }

        var moduleFilterResult = ModuleFilterResolver.Resolve(configuration, input.ModuleFilter);
        if (moduleFilterResult.IsFailure)
        {
            return Result<BuildSsdtApplicationResult>.Failure(moduleFilterResult.Errors);
        }

        var moduleFilter = moduleFilterResult.Value;

        var typeMappingResult = TypeMappingPolicyResolver.Resolve(input.ConfigurationContext);
        if (typeMappingResult.IsFailure)
        {
            return Result<BuildSsdtApplicationResult>.Failure(typeMappingResult.Errors);
        }

        var typeMappingPolicy = typeMappingResult.Value;
        var outputDirectory = _outputDirectoryResolver.Resolve(input.Overrides);

        var modelResolutionResult = await _modelResolutionService.ResolveModelAsync(
            configuration,
            input.Overrides,
            moduleFilter,
            sqlOptionsResult.Value,
            outputDirectory,
            cancellationToken).ConfigureAwait(false);
        if (modelResolutionResult.IsFailure)
        {
            return Result<BuildSsdtApplicationResult>.Failure(modelResolutionResult.Errors);
        }

        var modelResolution = modelResolutionResult.Value;

        var namingOverridesResult = _namingOverridesBinder.Bind(input.Overrides, tighteningOptions);

        if (namingOverridesResult.IsFailure)
        {
            return Result<BuildSsdtApplicationResult>.Failure(namingOverridesResult.Errors);
        }

        var staticDataProviderResult = _staticDataProviderFactory.Create(input.Overrides, sqlOptionsResult.Value, tighteningOptions);
        if (staticDataProviderResult.IsFailure)
        {
            return Result<BuildSsdtApplicationResult>.Failure(staticDataProviderResult.Errors);
        }

        var smoOptions = SmoBuildOptions.FromEmission(tighteningOptions.Emission)
            .WithNamingOverrides(namingOverridesResult.Value);

        if (input.Overrides.MaxDegreeOfParallelism is int moduleParallelism)
        {
            if (moduleParallelism <= 0)
            {
                return ValidationError.Create(
                    "cli.buildSsdt.parallelism.invalid",
                    "--max-degree-of-parallelism must be a positive integer when specified.");
            }

            smoOptions = smoOptions with { ModuleParallelism = moduleParallelism };
        }

        var assemblyResult = _assembler.Assemble(new BuildSsdtRequestAssemblerContext(
            configuration,
            input.Overrides,
            moduleFilter,
            sqlOptionsResult.Value,
            tighteningOptions,
            typeMappingPolicy,
            smoOptions,
            modelResolution.ModelPath,
            outputDirectory,
            staticDataProviderResult.Value,
            input.Cache,
            input.ConfigurationContext.ConfigPath));
        if (assemblyResult.IsFailure)
        {
            return Result<BuildSsdtApplicationResult>.Failure(assemblyResult.Errors);
        }

        var assembly = assemblyResult.Value;

        var pipelineResult = await _dispatcher.DispatchAsync<BuildSsdtPipelineRequest, BuildSsdtPipelineResult>(
            assembly.Request,
            cancellationToken).ConfigureAwait(false);
        if (pipelineResult.IsFailure)
        {
            return Result<BuildSsdtApplicationResult>.Failure(pipelineResult.Errors);
        }

        return new BuildSsdtApplicationResult(
            pipelineResult.Value,
            pipelineResult.Value.ProfileInsights,
            assembly.ProfilerProvider,
            assembly.ProfilePath,
            assembly.OutputDirectory,
            modelResolution.ModelPath,
            modelResolution.WasExtracted,
            modelResolution.Warnings);
    }
}
