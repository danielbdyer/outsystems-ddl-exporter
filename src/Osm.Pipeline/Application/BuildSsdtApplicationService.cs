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
        private readonly PipelineRequestContextFactory _contextFactory;

        public BuildSsdtApplicationService(
            ICommandDispatcher dispatcher,
            BuildSsdtRequestAssembler assembler,
            IModelResolutionService modelResolutionService,
            IOutputDirectoryResolver outputDirectoryResolver,
            INamingOverridesBinder namingOverridesBinder,
            IStaticDataProviderFactory staticDataProviderFactory,
            PipelineRequestContextFactory contextFactory)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _assembler = assembler ?? throw new ArgumentNullException(nameof(assembler));
            _modelResolutionService = modelResolutionService ?? throw new ArgumentNullException(nameof(modelResolutionService));
            _outputDirectoryResolver = outputDirectoryResolver ?? throw new ArgumentNullException(nameof(outputDirectoryResolver));
            _namingOverridesBinder = namingOverridesBinder ?? throw new ArgumentNullException(nameof(namingOverridesBinder));
            _staticDataProviderFactory = staticDataProviderFactory ?? throw new ArgumentNullException(nameof(staticDataProviderFactory));
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        }

        public async Task<Result<BuildSsdtApplicationResult>> RunAsync(BuildSsdtApplicationInput input, CancellationToken cancellationToken = default)
        {
            if (input is null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            await using var contextScope = await _contextFactory
                .CreateAsync(
                    new PipelineRequestContextFactoryRequest(
                        input.ConfigurationContext,
                        input.ModuleFilter,
                        input.Sql,
                        input.Cache,
                        input.Overrides.SqlMetadataOutputPath,
                        new NamingOverridesRequest(input.Overrides, _namingOverridesBinder)),
                    cancellationToken)
                .ConfigureAwait(false);

            if (contextScope.IsFailure)
            {
                return Result<BuildSsdtApplicationResult>.Failure(contextScope.Errors);
            }

            var context = contextScope.Context;
            var outputDirectory = _outputDirectoryResolver.Resolve(input.Overrides);

            var modelResolutionResult = await _modelResolutionService.ResolveModelAsync(
                context.Configuration,
                input.Overrides,
                context.ModuleFilter,
                context.SqlOptions,
                outputDirectory,
                context.SqlMetadataLog,
                cancellationToken).ConfigureAwait(false);
            if (modelResolutionResult.IsFailure)
            {
                return Result<BuildSsdtApplicationResult>.Failure(modelResolutionResult.Errors);
            }

            var modelResolution = modelResolutionResult.Value;
            var staticDataProviderResult = _staticDataProviderFactory.Create(input.Overrides, context.SqlOptions, context.Tightening);
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
                    return ValidationError.Create(
                        "cli.buildSsdt.parallelism.invalid",
                        "--max-degree-of-parallelism must be a positive integer when specified.");
                }

                smoOptions = smoOptions with { ModuleParallelism = moduleParallelism };
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
                staticDataProviderResult.Value,
                context.CacheOverrides,
                context.ConfigPath,
                context.SqlMetadataLog));
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
                assembly.ProfilerProvider,
                assembly.ProfilePath,
                assembly.OutputDirectory,
                modelResolution.ModelPath,
                modelResolution.WasExtracted,
                modelResolution.Warnings);
        }

    }
