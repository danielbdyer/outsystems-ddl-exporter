using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;

namespace Osm.Pipeline.Runtime.Verbs;

public sealed record ExtractModelVerbOptions
{
    public string? ConfigurationPath { get; init; }
    public ExtractModelOverrides Overrides { get; init; } = new(null, null, null, null, null, null);
    public SqlOptionsOverrides Sql { get; init; } = new(null, null, null, null, null, null, null, null, null);
    public string? SqlMetadataOutputPath { get; init; }
}

public sealed record ExtractModelVerbResult(
    CliConfigurationContext Configuration,
    ExtractModelApplicationResult ApplicationResult);

public sealed class ExtractModelVerb : PipelineVerb<ExtractModelVerbOptions, ExtractModelVerbResult>
{
    public const string VerbName = "extract-model";

    private readonly ICliConfigurationService _configurationService;
    private readonly IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult> _applicationService;

    public ExtractModelVerb(
        ICliConfigurationService configurationService,
        IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult> applicationService,
        TimeProvider? timeProvider = null)
        : base(timeProvider)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _applicationService = applicationService ?? throw new ArgumentNullException(nameof(applicationService));
    }

    public override string Name => VerbName;

    protected override async Task<Result<ExtractModelVerbResult>> ExecuteAsync(
        ExtractModelVerbOptions options,
        CancellationToken cancellationToken)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var configurationResult = await _configurationService
            .LoadAsync(options.ConfigurationPath, cancellationToken)
            .ConfigureAwait(false);

        if (configurationResult.IsFailure)
        {
            return Result<ExtractModelVerbResult>.Failure(configurationResult.Errors);
        }

        var overrides = options.Overrides ?? new ExtractModelOverrides(null, null, null, null, null, null);
        if (string.IsNullOrWhiteSpace(overrides.SqlMetadataOutputPath) && !string.IsNullOrWhiteSpace(options.SqlMetadataOutputPath))
        {
            overrides = overrides with { SqlMetadataOutputPath = options.SqlMetadataOutputPath };
        }

        var input = new ExtractModelApplicationInput(
            configurationResult.Value,
            overrides,
            options.Sql);

        var applicationResult = await _applicationService
            .RunAsync(input, cancellationToken)
            .ConfigureAwait(false);

        if (applicationResult.IsFailure)
        {
            return Result<ExtractModelVerbResult>.Failure(applicationResult.Errors);
        }

        return new ExtractModelVerbResult(configurationResult.Value, applicationResult.Value);
    }

    protected override IReadOnlyList<PipelineArtifact> DescribeArtifacts(ExtractModelVerbResult result)
    {
        var artifacts = new List<PipelineArtifact>();
        var output = result.ApplicationResult.OutputPath;
        if (!string.IsNullOrWhiteSpace(output))
        {
            artifacts.Add(new PipelineArtifact("model-json", output, "application/json"));
        }

        return artifacts;
    }

    protected override IReadOnlyDictionary<string, string?> BuildMetadata(
        ExtractModelVerbOptions options,
        Result<ExtractModelVerbResult> outcome)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(options.ConfigurationPath))
        {
            builder["configOverride"] = options.ConfigurationPath;
        }

        if (outcome.IsSuccess)
        {
            var result = outcome.Value;
            var configuration = result.Configuration.Configuration;
            builder["configPath"] = result.Configuration.ConfigPath;
            builder["outputPath"] = result.ApplicationResult.OutputPath;
            builder["moduleFilter"] = configuration.ModuleFilter?.Modules is { Count: > 0 }
                ? string.Join(',', configuration.ModuleFilter.Modules)
                : "<all>";
        }

        return builder.ToImmutable();
    }
}
