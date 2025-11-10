using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Emission;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.DynamicData;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Sql;
using Osm.Smo;
using Osm.Validation.Tightening;

namespace Osm.Pipeline.Application;

public sealed record BuildSsdtApplicationInput(
    CliConfigurationContext ConfigurationContext,
    BuildSsdtOverrides Overrides,
    ModuleFilterOverrides ModuleFilter,
    SqlOptionsOverrides Sql,
    CacheOptionsOverrides Cache,
    TighteningOverrides? TighteningOverrides = null,
    DynamicEntityDataset? DynamicDataset = null);

public sealed record BuildSsdtApplicationResult(
    BuildSsdtPipelineResult PipelineResult,
    string ProfilerProvider,
    string? ProfilePath,
    string OutputDirectory,
    string ModelPath,
    bool ModelWasExtracted,
    ImmutableArray<string> ModelExtractionWarnings);

public sealed class BuildSsdtApplicationService : PipelineApplicationServiceBase, IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>
{
    private readonly ICommandDispatcher _dispatcher;
    private readonly BuildSsdtRequestAssembler _assembler;
    private readonly IModelResolutionService _modelResolutionService;
    private readonly IOutputDirectoryResolver _outputDirectoryResolver;
    private readonly INamingOverridesBinder _namingOverridesBinder;
    private readonly IStaticDataProviderFactory _staticDataProviderFactory;
    private readonly IModelIngestionService _modelIngestionService;
    private readonly IDynamicEntityDataProvider _dynamicDataProvider;

    public BuildSsdtApplicationService(
        ICommandDispatcher dispatcher,
        BuildSsdtRequestAssembler assembler,
        IModelResolutionService modelResolutionService,
        IOutputDirectoryResolver outputDirectoryResolver,
        INamingOverridesBinder namingOverridesBinder,
        IStaticDataProviderFactory staticDataProviderFactory,
        IModelIngestionService modelIngestionService,
        IDynamicEntityDataProvider dynamicDataProvider)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _assembler = assembler ?? throw new ArgumentNullException(nameof(assembler));
        _modelResolutionService = modelResolutionService ?? throw new ArgumentNullException(nameof(modelResolutionService));
        _outputDirectoryResolver = outputDirectoryResolver ?? throw new ArgumentNullException(nameof(outputDirectoryResolver));
        _namingOverridesBinder = namingOverridesBinder ?? throw new ArgumentNullException(nameof(namingOverridesBinder));
        _staticDataProviderFactory = staticDataProviderFactory ?? throw new ArgumentNullException(nameof(staticDataProviderFactory));
        _modelIngestionService = modelIngestionService ?? throw new ArgumentNullException(nameof(modelIngestionService));
        _dynamicDataProvider = dynamicDataProvider ?? throw new ArgumentNullException(nameof(dynamicDataProvider));
    }

    public async Task<Result<BuildSsdtApplicationResult>> RunAsync(BuildSsdtApplicationInput input, CancellationToken cancellationToken = default)
    {
        input = EnsureNotNull(input, nameof(input));

        var configurationContext = EnsureNotNull(input.ConfigurationContext, nameof(input.ConfigurationContext));

        var contextResult = BuildContext(new PipelineRequestContextBuilderRequest(
            configurationContext,
            input.ModuleFilter,
            input.Sql,
            input.Cache,
            input.Overrides.SqlMetadataOutputPath,
            new NamingOverridesRequest(input.Overrides, _namingOverridesBinder),
            input.TighteningOverrides));
        if (contextResult.IsFailure)
        {
            return Result<BuildSsdtApplicationResult>.Failure(contextResult.Errors);
        }

        var context = contextResult.Value;
        var dynamicDatasetSource = DynamicDatasetSource.None;
        var dynamicDataset = input.DynamicDataset ?? DynamicEntityDataset.Empty;
        if (!dynamicDataset.IsEmpty)
        {
            dynamicDatasetSource = DynamicDatasetSource.UserProvided;
        }
        var outputDirectory = _outputDirectoryResolver.Resolve(input.Overrides);

        var modelResolutionResult = await _modelResolutionService.ResolveModelAsync(
            context.Configuration,
            input.Overrides,
            context.ModuleFilter,
            context.SqlOptions,
            outputDirectory,
            context.SqlMetadataLog,
            cancellationToken).ConfigureAwait(false);
        modelResolutionResult = await EnsureSuccessOrFlushAsync(modelResolutionResult, context, cancellationToken).ConfigureAwait(false);
        if (modelResolutionResult.IsFailure)
        {
            return Result<BuildSsdtApplicationResult>.Failure(modelResolutionResult.Errors);
        }

        var modelResolution = modelResolutionResult.Value;
        if (dynamicDataset.IsEmpty && modelResolution.Extraction is { } extractionResult && !extractionResult.Dataset.IsEmpty)
        {
            dynamicDataset = extractionResult.Dataset;
            dynamicDatasetSource = DynamicDatasetSource.Extraction;
        }
        var staticDataProviderResult = _staticDataProviderFactory.Create(input.Overrides, context.SqlOptions, context.Tightening);
        staticDataProviderResult = await EnsureSuccessOrFlushAsync(staticDataProviderResult, context, cancellationToken).ConfigureAwait(false);
        if (staticDataProviderResult.IsFailure)
        {
            return Result<BuildSsdtApplicationResult>.Failure(staticDataProviderResult.Errors);
        }

        var baseSmoOptions = SmoBuildOptions.FromEmission(context.Tightening.Emission);
        var namingOverrides = context.NamingOverrides ?? baseSmoOptions.NamingOverrides;
        var smoOptions = baseSmoOptions.WithNamingOverrides(namingOverrides);

        if (input.Overrides.MaxDegreeOfParallelism is int moduleParallelism)
        {
            if (moduleParallelism <= 0)
            {
                await FlushMetadataAsync(context, cancellationToken).ConfigureAwait(false);
                return ValidationError.Create(
                    "cli.buildSsdt.parallelism.invalid",
                    "--max-degree-of-parallelism must be a positive integer when specified.");
            }

            smoOptions = smoOptions with { ModuleParallelism = moduleParallelism };
        }

        if (dynamicDataset.IsEmpty && !string.IsNullOrWhiteSpace(context.SqlOptions.ConnectionString))
        {
            var modelForDynamicData = modelResolution.Extraction?.Model;
            if (modelForDynamicData is null)
            {
                var modelLoadResult = await _modelIngestionService
                    .LoadFromFileAsync(modelResolution.ModelPath, warnings: null, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                modelLoadResult = await EnsureSuccessOrFlushAsync(modelLoadResult, context, cancellationToken).ConfigureAwait(false);
                if (modelLoadResult.IsFailure)
                {
                    return Result<BuildSsdtApplicationResult>.Failure(modelLoadResult.Errors);
                }

                modelForDynamicData = modelLoadResult.Value;
            }

            var authentication = context.SqlOptions.Authentication;
            var connectionOptions = new SqlConnectionOptions(
                authentication.Method,
                authentication.TrustServerCertificate,
                authentication.ApplicationName,
                authentication.AccessToken);

            var extractionRequest = new SqlDynamicEntityExtractionRequest(
                context.SqlOptions.ConnectionString!,
                connectionOptions,
                modelForDynamicData!,
                context.ModuleFilter,
                namingOverrides,
                context.SqlOptions.CommandTimeoutSeconds);

            var dynamicDatasetResult = await _dynamicDataProvider
                .ExtractAsync(extractionRequest, cancellationToken)
                .ConfigureAwait(false);
            dynamicDatasetResult = await EnsureSuccessOrFlushAsync(dynamicDatasetResult, context, cancellationToken).ConfigureAwait(false);
            if (dynamicDatasetResult.IsFailure)
            {
                return Result<BuildSsdtApplicationResult>.Failure(dynamicDatasetResult.Errors);
            }

            dynamicDataset = dynamicDatasetResult.Value;
            dynamicDatasetSource = DynamicDatasetSource.SqlProvider;
        }

        var assemblyResult = _assembler.Assemble(new BuildSsdtRequestAssemblerContext(
            context.Configuration,
            input.Overrides,
            context.ModuleFilter,
            context.SqlOptions,
            context.Tightening,
            context.TypeMappingPolicy,
            smoOptions,
            modelResolution.ModelPath,
            outputDirectory,
            dynamicDataset,
            dynamicDatasetSource,
            staticDataProviderResult.Value,
            context.CacheOverrides,
            context.ConfigPath,
            context.SqlMetadataLog,
            modelResolution.Extraction?.Model,
            modelResolution.Warnings));
        assemblyResult = await EnsureSuccessOrFlushAsync(assemblyResult, context, cancellationToken).ConfigureAwait(false);
        if (assemblyResult.IsFailure)
        {
            return Result<BuildSsdtApplicationResult>.Failure(assemblyResult.Errors);
        }

        var assembly = assemblyResult.Value;

        var pipelineResult = await _dispatcher.DispatchAsync<BuildSsdtPipelineRequest, BuildSsdtPipelineResult>(
            assembly.Request,
            cancellationToken).ConfigureAwait(false);

        await FlushMetadataAsync(context, cancellationToken).ConfigureAwait(false);

        if (pipelineResult.IsFailure)
        {
            return Result<BuildSsdtApplicationResult>.Failure(pipelineResult.Errors);
        }

        return new BuildSsdtApplicationResult(
            pipelineResult.Value,
            assembly.ProfilerProvider,
            assembly.ProfilePath,
            assembly.OutputDirectory,
            modelResolution.ModelPath,
            modelResolution.WasExtracted,
            modelResolution.Warnings);
    }
}
