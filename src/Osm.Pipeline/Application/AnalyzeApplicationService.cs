using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Orchestration;

namespace Osm.Pipeline.Application;

public sealed record AnalyzeApplicationInput(
    CliConfigurationContext ConfigurationContext,
    AnalyzeOverrides Overrides);

public sealed record AnalyzeApplicationResult(
    TighteningAnalysisPipelineResult PipelineResult,
    string OutputDirectory,
    string ModelPath,
    string ProfilePath);

public sealed class AnalyzeApplicationService : IApplicationService<AnalyzeApplicationInput, AnalyzeApplicationResult>
{
    private readonly ICommandDispatcher _dispatcher;

    public AnalyzeApplicationService(ICommandDispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public async Task<Result<AnalyzeApplicationResult>> RunAsync(
        AnalyzeApplicationInput input,
        CancellationToken cancellationToken = default)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (input.ConfigurationContext is null)
        {
            throw new ArgumentNullException(nameof(input.ConfigurationContext));
        }

        var configuration = input.ConfigurationContext.Configuration ?? CliConfiguration.Empty;
        var overrides = input.Overrides ?? new AnalyzeOverrides(null, null, null);
        var tighteningOptions = configuration.Tightening;

        var moduleFilterResult = ModuleFilterResolver.Resolve(
            configuration,
            new ModuleFilterOverrides(
                Array.Empty<string>(),
                null,
                null,
                Array.Empty<string>(),
                Array.Empty<string>()));

        if (moduleFilterResult.IsFailure)
        {
            return Result<AnalyzeApplicationResult>.Failure(moduleFilterResult.Errors);
        }

        var moduleFilter = moduleFilterResult.Value;

        var modelPath = overrides.ModelPath ?? configuration.ModelPath;
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            return ValidationError.Create(
                "pipeline.analyze.model.missing",
                "Model path must be provided for tightening analysis.");
        }

        var profilePath = overrides.ProfilePath
            ?? configuration.ProfilePath
            ?? configuration.Profiler.ProfilePath;
        if (string.IsNullOrWhiteSpace(profilePath))
        {
            return ValidationError.Create(
                "pipeline.analyze.profile.missing",
                "Profile path must be provided for tightening analysis.");
        }

        var outputDirectory = string.IsNullOrWhiteSpace(overrides.OutputDirectory)
            ? "out"
            : overrides.OutputDirectory!;

        Directory.CreateDirectory(outputDirectory);

        var request = new TighteningAnalysisPipelineRequest(
            modelPath,
            moduleFilter,
            tighteningOptions,
            ResolveSupplementalOptions(configuration.SupplementalModels),
            profilePath,
            outputDirectory);

        var pipelineResult = await _dispatcher
            .DispatchAsync<TighteningAnalysisPipelineRequest, TighteningAnalysisPipelineResult>(request, cancellationToken)
            .ConfigureAwait(false);

        if (pipelineResult.IsFailure)
        {
            return Result<AnalyzeApplicationResult>.Failure(pipelineResult.Errors);
        }

        return new AnalyzeApplicationResult(
            pipelineResult.Value,
            outputDirectory,
            modelPath,
            profilePath);
    }

    private static SupplementalModelOptions ResolveSupplementalOptions(SupplementalModelConfiguration configuration)
    {
        configuration ??= SupplementalModelConfiguration.Empty;
        var includeUsers = configuration.IncludeUsers ?? true;
        var paths = configuration.Paths ?? Array.Empty<string>();
        return new SupplementalModelOptions(includeUsers, paths.ToArray());
    }
}
