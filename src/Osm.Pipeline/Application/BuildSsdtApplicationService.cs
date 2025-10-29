using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
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

        var requestContextResult = BuildRequestContext(input);
        if (requestContextResult.IsFailure)
        {
            return Result<BuildSsdtApplicationResult>.Failure(requestContextResult.Errors);
        }

        var requestContext = requestContextResult.Value;
        var outputDirectory = _outputDirectoryResolver.Resolve(input.Overrides);

        var modelResolutionResult = await ResolveModelAsync(
            input,
            requestContext,
            outputDirectory,
            cancellationToken).ConfigureAwait(false);
        if (modelResolutionResult.IsFailure)
        {
            await requestContext.FlushMetadataAsync(cancellationToken).ConfigureAwait(false);
            return Result<BuildSsdtApplicationResult>.Failure(modelResolutionResult.Errors);
        }

        var staticDataProviderResult = CreateStaticDataProvider(input, requestContext);
        if (staticDataProviderResult.IsFailure)
        {
            await requestContext.FlushMetadataAsync(cancellationToken).ConfigureAwait(false);
            return Result<BuildSsdtApplicationResult>.Failure(staticDataProviderResult.Errors);
        }

        var executionContext = new BuildSsdtExecutionContext(
            requestContext,
            outputDirectory,
            modelResolutionResult.Value,
            staticDataProviderResult.Value);

        var assemblyResult = AssemblePipelineRequest(input, executionContext);
        if (assemblyResult.IsFailure)
        {
            await requestContext.FlushMetadataAsync(cancellationToken).ConfigureAwait(false);
            return Result<BuildSsdtApplicationResult>.Failure(assemblyResult.Errors);
        }

        var assembly = assemblyResult.Value;

        var pipelineResult = await _dispatcher
            .DispatchAsync<BuildSsdtPipelineRequest, BuildSsdtPipelineResult>(assembly.Request, cancellationToken)
            .ConfigureAwait(false);

        await requestContext.FlushMetadataAsync(cancellationToken).ConfigureAwait(false);

        if (pipelineResult.IsFailure)
        {
            return Result<BuildSsdtApplicationResult>.Failure(pipelineResult.Errors);
        }

        return new BuildSsdtApplicationResult(
            pipelineResult.Value,
            assembly.ProfilerProvider,
            assembly.ProfilePath,
            assembly.OutputDirectory,
            executionContext.ModelResolution.ModelPath,
            executionContext.ModelResolution.WasExtracted,
            executionContext.ModelResolution.Warnings);
    }

    private Result<PipelineRequestContext> BuildRequestContext(BuildSsdtApplicationInput input)
    {
        var namingOverrides = new NamingOverridesRequest(input.Overrides, _namingOverridesBinder);
        var request = new PipelineRequestContextBuilderRequest(
            input.ConfigurationContext,
            input.ModuleFilter,
            input.Sql,
            input.Cache,
            input.Overrides.SqlMetadataOutputPath,
            namingOverrides);

        return PipelineRequestContextBuilder.Build(request);
    }

    private Task<Result<ModelResolutionResult>> ResolveModelAsync(
        BuildSsdtApplicationInput input,
        PipelineRequestContext context,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        return _modelResolutionService.ResolveModelAsync(
            context.Configuration,
            input.Overrides,
            context.ModuleFilter,
            context.SqlOptions,
            outputDirectory,
            context.SqlMetadataLog,
            cancellationToken);
    }

    private Result<IStaticEntityDataProvider?> CreateStaticDataProvider(
        BuildSsdtApplicationInput input,
        PipelineRequestContext context)
    {
        return _staticDataProviderFactory.Create(input.Overrides, context.SqlOptions, context.Tightening);
    }

    private Result<BuildSsdtRequestAssembly> AssemblePipelineRequest(
        BuildSsdtApplicationInput input,
        BuildSsdtExecutionContext context)
    {
        var baseSmoOptions = SmoBuildOptions.FromEmission(context.RequestContext.Tightening.Emission);
        var namingOverrides = context.RequestContext.NamingOverrides ?? baseSmoOptions.NamingOverrides;
        var smoOptions = baseSmoOptions.WithNamingOverrides(namingOverrides);

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

        var requestContext = context.RequestContext;
        return _assembler.Assemble(new BuildSsdtRequestAssemblerContext(
            requestContext.Configuration,
            input.Overrides,
            requestContext.ModuleFilter,
            requestContext.SqlOptions,
            requestContext.Tightening,
            requestContext.TypeMappingPolicy,
            smoOptions,
            context.ModelResolution.ModelPath,
            context.OutputDirectory,
            context.StaticDataProvider,
            requestContext.CacheOverrides,
            requestContext.ConfigPath,
            requestContext.SqlMetadataLog));
    }

    private readonly record struct BuildSsdtExecutionContext(
        PipelineRequestContext RequestContext,
        string OutputDirectory,
        ModelResolutionResult ModelResolution,
        IStaticEntityDataProvider? StaticDataProvider);
}
